// Driver-free scheduler core; see sched_core.h.
#include "sched_core.h"
#include <algorithm>
#include <cstring>

namespace fs {
namespace sched {

const uint32_t kDefaultInFlight[kClassCount] = { 4, 2, 2, 1, 1 };
const uint32_t kDefaultQuantumUs[kClassCount] = { 500, 2000, 2000, 1500, 1000 };

const char* ClassName(uint32_t cls) {
    static const char* names[kClassCount] = { "PUBLISH", "SCAN", "INNER_RESIDENCY", "APPEARANCE", "COLD" };
    return cls < kClassCount ? names[cls] : "?";
}

CoreConfig NormalizeConfig(const FsHostConfig* cfg, bool queueFallback) {
    CoreConfig c;
    c.queueFallback = queueFallback;
    const bool have = cfg != nullptr && cfg->structSize >= sizeof(FsHostConfig);
    c.frameBudgetUs = have && cfg->frameBudgetUs ? cfg->frameBudgetUs : kDefaultFrameBudgetUs;
    for (uint32_t i = 0; i < kClassCount; ++i) {
        uint32_t f = have ? cfg->inFlightMax[i] : 0;
        uint32_t q = have ? cfg->quantumUs[i] : 0;
        if (f == 0) f = kDefaultInFlight[i];
        if (q == 0) q = kDefaultQuantumUs[i];
        c.inFlightMax[i] = std::min(f, kMaxInFlightPerClass);
        if (queueFallback) q = std::min(q, kFallbackQuantumCapUs);
        c.quantumUs[i] = q;
    }
    return c;
}

// ---- ClassRing --------------------------------------------------------------------------------------
void ClassRing::Init(uint32_t inFlightMax) {
    slots_.assign(std::max(1u, inFlightMax), SlotState{});
    next_ = 0; inFlight_ = 0;
}

int32_t ClassRing::Acquire() {
    if (Full()) return -1;
    for (uint32_t k = 0; k < Capacity(); ++k) {
        const uint32_t i = (next_ + k) % Capacity();
        if (!slots_[i].inFlight) {
            slots_[i] = SlotState{};
            slots_[i].inFlight = true;
            ++inFlight_;
            next_ = (i + 1) % Capacity();
            return static_cast<int32_t>(i);
        }
    }
    return -1;
}

void ClassRing::Release(uint32_t slot) {
    if (slot >= Capacity() || !slots_[slot].inFlight) return;
    slots_[slot].inFlight = false;
    --inFlight_;
}

int32_t ClassRing::OldestInFlight() const {
    int32_t best = -1; uint64_t bestEpoch = UINT64_MAX;
    for (uint32_t i = 0; i < Capacity(); ++i)
        if (slots_[i].inFlight && slots_[i].submitEpoch < bestEpoch) { bestEpoch = slots_[i].submitEpoch; best = static_cast<int32_t>(i); }
    return best;
}

// ---- TimelineTracker -----------------------------------------------------------------------------
void TimelineTracker::MarkRetired(uint64_t value) {
    if (value == 0 || value <= retired_ || value > allocated_) return;
    if (value == retired_ + 1) {
        retired_ = value;
        // absorb any out-of-order values that are now contiguous
        bool progressed = true;
        while (progressed && !outOfOrder_.empty()) {
            progressed = false;
            for (size_t i = 0; i < outOfOrder_.size(); ++i) {
                if (outOfOrder_[i] == retired_ + 1) {
                    retired_ = outOfOrder_[i];
                    outOfOrder_.erase(outOfOrder_.begin() + static_cast<std::ptrdiff_t>(i));
                    progressed = true;
                    break;
                }
            }
        }
        return;
    }
    if (std::find(outOfOrder_.begin(), outOfOrder_.end(), value) == outOfOrder_.end()) outOfOrder_.push_back(value);
}

// ---- BudgetLedger ------------------------------------------------------------------------------------
bool BudgetLedger::TryConsume(uint32_t quantumUs) {
    if (quantumUs > Remaining()) return false;
    used_ += quantumUs;
    return true;
}

// ---- SchedulerCore -------------------------------------------------------------------------------------
void SchedulerCore::Init(const CoreConfig& cfg) {
    cfg_ = cfg;
    for (uint32_t i = 0; i < kClassCount; ++i) {
        rings_[i].Init(cfg.inFlightMax[i]);
        stats_[i] = ClassStats{};
        timelines_[i].Reset();
    }
    budget_ = BudgetLedger{};
    budget_.BeginFrame(cfg.frameBudgetUs);
    nextJobId_ = 1; submitEpoch_ = 0; frame_ = 0; accepting_ = true;
}

void SchedulerCore::BeginFrame(uint32_t frameIndex) {
    frame_ = frameIndex;
    budget_.BeginFrame(cfg_.frameBudgetUs);
}

uint32_t SchedulerCore::EffectiveQuantumUs(FsJobClass cls, uint32_t requested) const {
    uint32_t q = requested ? requested : cfg_.quantumUs[cls];
    if (cfg_.queueFallback) q = std::min(q, kFallbackQuantumCapUs);
    return q;
}

SubmitDecision SchedulerCore::TrySubmit(FsJobClass cls, uint32_t requestedQuantumUs, uint32_t leaseSlot, uint32_t leaseGeneration,
                                        const LeaseValidator* validator, int64_t nowNs, FsJobClass waitClass, uint64_t waitValue) {
    SubmitDecision d;
    if (cls < 0 || static_cast<uint32_t>(cls) >= kClassCount || !accepting_) { d.outcome = SubmitOutcome::Refused; return d; }
    ClassStats& st = stats_[cls];
    d.quantumUs = EffectiveQuantumUs(cls, requestedQuantumUs);
    if (leaseSlot != FS_INDEX_NONE && validator != nullptr && *validator && !(*validator)(leaseSlot, leaseGeneration)) {
        ++st.droppedLease; d.outcome = SubmitOutcome::DroppedLease; return d;
    }
    if (waitValue != 0) {
        if (waitClass < 0 || static_cast<uint32_t>(waitClass) >= kClassCount) { d.outcome = SubmitOutcome::Refused; return d; }
        if (waitValue > timelines_[waitClass].LastAllocated()) { ++st.deferredDependency; d.outcome = SubmitOutcome::DeferredDependency; return d; }
    }
    ClassRing& ring = rings_[cls];
    if (ring.Full()) { ++st.deferredRing; d.outcome = SubmitOutcome::DeferredRing; return d; }
    if (!budget_.TryConsume(d.quantumUs)) { ++st.deferredBudget; d.outcome = SubmitOutcome::DeferredBudget; return d; }
    const int32_t slot = ring.Acquire();
    if (slot < 0) { ++st.deferredRing; d.outcome = SubmitOutcome::DeferredRing; return d; }   // unreachable: Full() checked
    SlotState& s = ring.Slot(static_cast<uint32_t>(slot));
    s.jobId = nextJobId_++;
    s.timelineValue = timelines_[cls].Allocate();
    s.submitEpoch = ++submitEpoch_;
    s.quantumUs = d.quantumUs;
    s.leaseSlot = leaseSlot; s.leaseGeneration = leaseGeneration;
    s.frameIndex = frame_;
    s.cpuSubmitNs = nowNs;
    ++st.submitted;
    d.outcome = SubmitOutcome::Accepted;
    d.slot = static_cast<uint32_t>(slot); d.timelineValue = s.timelineValue; d.jobId = s.jobId; d.submitEpoch = s.submitEpoch;
    return d;
}

void SchedulerCore::Retire(FsJobClass cls, uint32_t slot, bool success, double gpuUs) {
    if (cls < 0 || static_cast<uint32_t>(cls) >= kClassCount) return;
    ClassRing& ring = rings_[cls];
    if (slot >= ring.Capacity() || !ring.Slot(slot).inFlight) return;
    ClassStats& st = stats_[cls];
    const uint64_t value = ring.Slot(slot).timelineValue;
    if (success) {
        ++st.retired;
        const int64_t us = gpuUs > 0.0 ? static_cast<int64_t>(gpuUs) : 0;
        st.gpuUsTotal += us; st.gpuUsLast = us;
    } else ++st.failed;
    timelines_[cls].MarkRetired(value);   // a failed job still retires its timeline value (host-signalled)
    ring.Release(slot);
}

void SchedulerCore::FailAllInFlight(std::vector<std::pair<FsJobClass, SlotState>>& out) {
    for (uint32_t c = 0; c < kClassCount; ++c) {
        ClassRing& ring = rings_[c];
        for (uint32_t i = 0; i < ring.Capacity(); ++i) {
            if (!ring.Slot(i).inFlight) continue;
            out.emplace_back(static_cast<FsJobClass>(c), ring.Slot(i));
            Retire(static_cast<FsJobClass>(c), i, false, 0.0);
        }
    }
    accepting_ = false;
}

bool SchedulerCore::AnyInFlight() const {
    for (uint32_t c = 0; c < kClassCount; ++c) if (rings_[c].InFlight() > 0) return true;
    return false;
}

uint64_t SchedulerCore::MinInFlightEpoch() const {
    uint64_t m = UINT64_MAX;
    for (uint32_t c = 0; c < kClassCount; ++c)
        for (uint32_t i = 0; i < rings_[c].Capacity(); ++i)
            if (rings_[c].Slot(i).inFlight) m = std::min(m, rings_[c].Slot(i).submitEpoch);
    return m;
}

bool SchedulerCore::GetClassStats(int32_t cls, int64_t out[8]) const {
    if (cls < 0 || static_cast<uint32_t>(cls) >= kClassCount || out == nullptr) return false;
    const ClassStats& s = stats_[cls];
    out[0] = s.submitted; out[1] = s.retired; out[2] = s.Deferred(); out[3] = s.failed;
    out[4] = s.gpuUsTotal; out[5] = s.gpuUsLast; out[6] = rings_[cls].InFlight(); out[7] = cfg_.quantumUs[cls];
    return true;
}

// ---- PublishLedger ---------------------------------------------------------------------------------------
void PublishLedger::Reset(uint32_t slots) { roots_.assign(slots, 0u); pending_.clear(); rootEpoch_ = 0; }

void PublishLedger::BeginPublish(uint64_t jobId, uint32_t slot, uint32_t generation) {
    if (slot >= roots_.size()) roots_.resize(static_cast<size_t>(slot) + 1, 0u);
    pending_.push_back({jobId, slot, generation});
}

bool PublishLedger::Retire(uint64_t jobId, bool success) {
    for (size_t i = 0; i < pending_.size(); ++i) {
        if (pending_[i].jobId != jobId) continue;
        const Pending p = pending_[i];
        pending_.erase(pending_.begin() + static_cast<std::ptrdiff_t>(i));
        if (!success || p.slot >= roots_.size() || p.generation <= roots_[p.slot]) return false;
        roots_[p.slot] = p.generation;   // root-last: data retired (fence) before the root moves
        ++rootEpoch_;
        return true;
    }
    return false;
}

uint32_t PublishLedger::FrontGeneration(uint32_t slot) const { return slot < roots_.size() ? roots_[slot] : 0u; }

uint32_t PublishLedger::FrontGenerationXor() const {
    uint32_t x = 0;
    for (size_t i = 0; i < roots_.size(); ++i) x ^= roots_[i] * 2654435761u + static_cast<uint32_t>(i);
    return x;
}

// ---- GarbageLedger -----------------------------------------------------------------------------------------
void GarbageLedger::Collect(uint64_t minInFlightEpoch, uint64_t safeFrame, std::vector<uint64_t>& freed) {
    for (size_t i = 0; i < items_.size();) {
        const Item& it = items_[i];
        if (it.submitEpoch < minInFlightEpoch && it.frame <= safeFrame) {
            freed.push_back(it.token);
            items_.erase(items_.begin() + static_cast<std::ptrdiff_t>(i));
        } else ++i;
    }
}

void GarbageLedger::TakeAll(std::vector<uint64_t>& out) {
    for (const Item& it : items_) out.push_back(it.token);
    items_.clear();
}

// ---- ScanRequestSlot ---------------------------------------------------------------------------------------
void ScanRequestSlot::Request(uint32_t observationId) {
    ++requested_;
    if (pending_) ++skipped_;      // superseded before consumption: no backlog (contract §3.3)
    pending_ = true; observation_ = observationId;
}

bool ScanRequestSlot::Take(uint32_t& observationId) {
    if (!pending_) return false;
    observationId = observation_; pending_ = false;
    return true;
}

} // namespace sched
} // namespace fs
