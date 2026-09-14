// Driver-free scheduler core (contract §15.4, docs/NATIVE_EXECUTOR_DESIGN.md §3-§6). No Vulkan calls:
// executor.cpp wraps it with the real fence rings / timeline semaphores; Native~/tests/host_tests_executor.cpp
// tests it on the host. Every method expects the caller to hold the executor lock (not thread-safe by itself).
#pragma once
#include <stdint.h>
#include <functional>
#include <vector>
#include "../../../include/finalscan_host_api.h"

namespace fs {
namespace sched {

constexpr uint32_t kClassCount = FS_JOB_CLASS_COUNT;
constexpr uint32_t kMaxInFlightPerClass = 8;            // ring capacity clamp (provisional)
constexpr uint32_t kDefaultFrameBudgetUs = 2500;        // contract §15.4 / FsHostConfig.frameBudgetUs
constexpr uint32_t kFallbackQuantumCapUs = 1000;        // graphics-queue fallback: quantum <= 1 ms (design §3)
constexpr uint32_t kMaxDispatchesPerJob = 32;           // query pool sizing: 2 per dispatch + 2 per job
extern const uint32_t kDefaultInFlight[kClassCount];    // PUBLISH 4, SCAN 2, INNER_RESIDENCY 2, APPEARANCE 1, COLD 1
extern const uint32_t kDefaultQuantumUs[kClassCount];   // PUBLISH 500, SCAN 2000, RESIDENCY 2000, APPEARANCE 1500, COLD 1000
                                                        // (C01: GPU interleaves scanner jobs with frames at job granularity; 2 ms jobs kept 72 Hz)
const char* ClassName(uint32_t cls);

struct CoreConfig {
    uint32_t inFlightMax[kClassCount] = {};
    uint32_t quantumUs[kClassCount] = {};
    uint32_t frameBudgetUs = kDefaultFrameBudgetUs;
    bool     queueFallback = false;     // no scanner queue: quanta capped at kFallbackQuantumCapUs
};
// Applies defaults for zero fields and clamps (inFlightMax <= kMaxInFlightPerClass, quantum <= budget cap).
CoreConfig NormalizeConfig(const FsHostConfig* cfg, bool queueFallback);

struct SlotState {
    bool     inFlight = false;
    uint64_t jobId = 0;
    uint64_t timelineValue = 0;      // class timeline value signalled by this slot
    uint64_t submitEpoch = 0;        // global submission counter at submit
    uint32_t quantumUs = 0;
    uint32_t leaseSlot = FS_INDEX_NONE;
    uint32_t leaseGeneration = 0;
    uint32_t frameIndex = 0;
    int64_t  cpuSubmitNs = 0;
};

// Fixed-capacity ring of in-flight slots per class. Slots are acquired round-robin so that fence
// retirement (in submission order on one queue) walks the ring in order.
class ClassRing {
public:
    void     Init(uint32_t inFlightMax);
    uint32_t Capacity() const { return static_cast<uint32_t>(slots_.size()); }
    uint32_t InFlight() const { return inFlight_; }
    bool     Full() const { return inFlight_ >= Capacity(); }
    int32_t  Acquire();                  // -1 when full; marks the slot in flight
    void     Release(uint32_t slot);
    int32_t  OldestInFlight() const;     // -1 when none
    SlotState&       Slot(uint32_t i) { return slots_[i]; }
    const SlotState& Slot(uint32_t i) const { return slots_[i]; }
private:
    std::vector<SlotState> slots_;
    uint32_t next_ = 0;
    uint32_t inFlight_ = 0;
};

// Monotonic timeline values per class; retirement may complete out of order (two queues in fallback
// mode) so "last retired" is the highest value with every lower value retired.
class TimelineTracker {
public:
    uint64_t Allocate() { return ++allocated_; }
    uint64_t LastAllocated() const { return allocated_; }
    uint64_t LastRetired() const { return retired_; }
    void     MarkRetired(uint64_t value);
    uint32_t PendingCount() const { return static_cast<uint32_t>(allocated_ - retired_); }
    void     Reset() { allocated_ = retired_ = 0; outOfOrder_.clear(); }
private:
    uint64_t allocated_ = 0;
    uint64_t retired_ = 0;
    std::vector<uint64_t> outOfOrder_;
};

// Per-frame GPU budget in microseconds (contract §15.4): reset each frame, consumed per submitted quantum.
class BudgetLedger {
public:
    void     BeginFrame(uint32_t budgetUs) { budget_ = budgetUs; used_ = 0; ++frames_; }
    bool     TryConsume(uint32_t quantumUs);
    uint32_t Remaining() const { return budget_ > used_ ? budget_ - used_ : 0; }
    uint32_t Used() const { return used_; }
    uint32_t FrameBudget() const { return budget_; }
    uint64_t Frames() const { return frames_; }
private:
    uint32_t budget_ = 0, used_ = 0;
    uint64_t frames_ = 0;
};

struct ClassStats {
    int64_t submitted = 0, retired = 0, deferredRing = 0, deferredBudget = 0, deferredDependency = 0, failed = 0, droppedLease = 0;
    int64_t gpuUsTotal = 0, gpuUsLast = 0;
    int64_t Deferred() const { return deferredRing + deferredBudget + deferredDependency; }
};

enum class SubmitOutcome { Accepted = 0, DeferredRing, DeferredBudget, DroppedLease, Refused, DeferredDependency };
struct SubmitDecision {
    SubmitOutcome outcome = SubmitOutcome::Refused;
    uint32_t slot = 0;
    uint64_t timelineValue = 0;
    uint64_t jobId = 0;
    uint64_t submitEpoch = 0;
    uint32_t quantumUs = 0;
};
using LeaseValidator = std::function<bool(uint32_t slot, uint32_t generation)>;

class SchedulerCore {
public:
    void Init(const CoreConfig& cfg);
    const CoreConfig& Config() const { return cfg_; }
    void SetFrameBudgetUs(uint32_t us) { cfg_.frameBudgetUs = us; }
    void SetAcceptingSubmits(bool accepting) { accepting_ = accepting; }
    bool AcceptingSubmits() const { return accepting_; }

    void BeginFrame(uint32_t frameIndex);        // frame budget reset (FS_HEVT_FRAME_END wake)
    uint32_t CurrentFrame() const { return frame_; }
    BudgetLedger& Budget() { return budget_; }
    const BudgetLedger& Budget() const { return budget_; }

    uint32_t EffectiveQuantumUs(FsJobClass cls, uint32_t requested) const;
    // Ring/budget/lease decision. Accepted → the slot is in flight and a timeline value is allocated.
    // waitClass/waitValue: dependency on another class timeline; a value not yet allocated (job not submitted)
    // defers (DeferredDependency) so a GPU wait can never dangle. waitValue 0 = none.
    SubmitDecision TrySubmit(FsJobClass cls, uint32_t requestedQuantumUs, uint32_t leaseSlot, uint32_t leaseGeneration,
                             const LeaseValidator* validator, int64_t nowNs, FsJobClass waitClass = FS_JOB_SCAN, uint64_t waitValue = 0);
    void Retire(FsJobClass cls, uint32_t slot, bool success, double gpuUs);
    // Quarantine: releases every in-flight slot as failed; returns the released states (caller runs callbacks).
    void FailAllInFlight(std::vector<std::pair<FsJobClass, SlotState>>& out);
    bool AnyInFlight() const;
    uint64_t MinInFlightEpoch() const;           // UINT64_MAX when nothing is in flight
    uint64_t SubmitEpoch() const { return submitEpoch_; }

    ClassRing&       Ring(FsJobClass cls) { return rings_[cls]; }
    const ClassRing& Ring(FsJobClass cls) const { return rings_[cls]; }
    ClassStats&       Stats(FsJobClass cls) { return stats_[cls]; }
    const ClassStats& Stats(FsJobClass cls) const { return stats_[cls]; }
    TimelineTracker&       Timeline(FsJobClass cls) { return timelines_[cls]; }
    const TimelineTracker& Timeline(FsJobClass cls) const { return timelines_[cls]; }
    // FsSched_GetClassStats layout: submitted, retired, deferred, failed, gpuUsTotal, gpuUsLast, inFlight, quantumUs
    bool GetClassStats(int32_t cls, int64_t out[8]) const;
private:
    CoreConfig cfg_;
    ClassRing rings_[kClassCount];
    ClassStats stats_[kClassCount];
    TimelineTracker timelines_[kClassCount];
    BudgetLedger budget_;
    uint64_t nextJobId_ = 1;
    uint64_t submitEpoch_ = 0;
    uint32_t frame_ = 0;
    bool accepting_ = true;
};

// Root-last publication (contract §11): a page's FRONT generation (the "root") becomes visible only after
// the PUBLISH job that wrote the FRONT data retired (fence-proven). Out-of-order retirement never regresses a root.
class PublishLedger {
public:
    void     Reset(uint32_t slots);
    void     BeginPublish(uint64_t jobId, uint32_t slot, uint32_t generation);
    bool     Retire(uint64_t jobId, bool success);       // true when a root advanced
    uint32_t FrontGeneration(uint32_t slot) const;        // 0 = never published
    uint32_t PendingCount() const { return static_cast<uint32_t>(pending_.size()); }
    uint64_t RootEpoch() const { return rootEpoch_; }     // bumped on every root advance
    uint32_t FrontGenerationXor() const;                  // telemetry summary (FsWorld_GetFrontGeneration style)
private:
    struct Pending { uint64_t jobId; uint32_t slot; uint32_t generation; };
    std::vector<Pending> pending_;
    std::vector<uint32_t> roots_;
    uint64_t rootEpoch_ = 0;
};

// Deferred destruction: an item is freed once every job submitted before it retired (epoch) and Unity's
// safe frame passed the frame it was last used in.
class GarbageLedger {
public:
    struct Item { uint64_t token; uint64_t submitEpoch; uint64_t frame; };
    void Push(uint64_t token, uint64_t submitEpoch, uint64_t frame) { items_.push_back({token, submitEpoch, frame}); }
    // minInFlightEpoch: SchedulerCore::MinInFlightEpoch(); safeFrame: Unity safeFrameNumber (or current frame when unknown).
    void Collect(uint64_t minInFlightEpoch, uint64_t safeFrame, std::vector<uint64_t>& freed);
    size_t Pending() const { return items_.size(); }
    void   TakeAll(std::vector<uint64_t>& out);
private:
    std::vector<Item> items_;
};

// Latest-only scan request slot (contract §3.3): a newer request supersedes an unconsumed one (counted).
class ScanRequestSlot {
public:
    void Request(uint32_t observationId);
    bool Take(uint32_t& observationId);
    int64_t Requested() const { return requested_; }
    int64_t Skipped() const { return skipped_; }
private:
    bool pending_ = false; uint32_t observation_ = 0; int64_t requested_ = 0, skipped_ = 0;
};

} // namespace sched
} // namespace fs
