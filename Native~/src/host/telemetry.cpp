// Host telemetry; see telemetry.h.
#include "telemetry.h"
#include "../json_writer.h"
#include <cmath>
#include <cstring>

namespace fs {

Telemetry& Tele() { static Telemetry t; return t; }

const char* CounterName(int32_t c) {
    static const char* names[FS_CTR_COUNT] = {
        "pcaFramesL", "pcaFramesR", "stereoPairs", "pairsRejectedSkew",
        "measurements", "measurementsDropped", "pageLookups", "pageMisses",
        "cellLookups", "surfelCreate", "surfelUpdate", "surfelDelete",
        "surfelSplit", "surfelMerge", "publish", "residentDrawn",
        "visibleSurfels", "scanTick", "scanTickSkipped", "orientationResidencyRequests",
        "pageHashOverflow", "indexOverflow", "deferredPublish", "deferredScan",
        "deferredResidency", "deferredAppearance", "deferredCold", "timestampNonMonotonic", "duplicateObservations" };
    return (c >= 0 && c < FS_CTR_COUNT) ? names[c] : "?";
}

// ---- ClockFit --------------------------------------------------------------------------------------------
void ClockFit::Add(double gpuNs, int64_t cpuNs) {
    if (samples_.size() >= kClockFitWindow) samples_.erase(samples_.begin());
    samples_.push_back({gpuNs, static_cast<double>(cpuNs)});
}

bool ClockFit::Fit() {
    const size_t n = samples_.size();
    if (n < 2) { valid_ = false; return false; }
    // Centre the data to keep the normal equations well conditioned (ns magnitudes ~1e12).
    double mg = 0, mc = 0;
    for (const Sample& s : samples_) { mg += s.gpu; mc += s.cpu; }
    mg /= static_cast<double>(n); mc /= static_cast<double>(n);
    double sxx = 0, sxy = 0;
    for (const Sample& s : samples_) { const double dx = s.gpu - mg, dy = s.cpu - mc; sxx += dx * dx; sxy += dx * dy; }
    if (sxx <= 0.0) { // all GPU samples identical: offset only
        slope_ = 1.0; offset_ = mc - mg; residualRms_ = 0.0; valid_ = true; return true;
    }
    slope_ = sxy / sxx;
    if (!(slope_ > 0.5 && slope_ < 2.0)) slope_ = 1.0;     // guard against degenerate fits (few, noisy samples)
    offset_ = mc - slope_ * mg;
    double ss = 0;
    for (const Sample& s : samples_) { const double r = s.cpu - (offset_ + slope_ * s.gpu); ss += r * r; }
    residualRms_ = std::sqrt(ss / static_cast<double>(n));
    valid_ = true;
    return true;
}

// ---- Telemetry -------------------------------------------------------------------------------------------
void Telemetry::CounterAdd(int32_t c, int64_t v) { if (c >= 0 && c < FS_CTR_COUNT) counters_[c].fetch_add(v, std::memory_order_relaxed); }
int64_t Telemetry::CounterGet(int32_t c) const { return (c >= 0 && c < FS_CTR_COUNT) ? counters_[c].load(std::memory_order_relaxed) : 0; }

void Telemetry::Stage(FsJobClass cls, const char* stage, uint64_t gpuStartNs, uint64_t gpuEndNs, uint32_t frameIndex) {
    const uint64_t seq = stageSeq_.fetch_add(1, std::memory_order_relaxed);
    std::lock_guard<std::mutex> lock(mutex_);
    StageRecord& r = stages_[seq % kStageRingSize];
    r.cls = static_cast<uint8_t>(cls);
    std::strncpy(r.stage, stage ? stage : "", kStageNameChars); r.stage[kStageNameChars] = '\0';
    r.gpuStartNs = gpuStartNs; r.gpuEndNs = gpuEndNs; r.frameIndex = frameIndex; r.sequence = seq + 1;
}

void Telemetry::Error(const char* text) {
    std::lock_guard<std::mutex> lock(mutex_);
    ++errorsTotal_;
    if (errors_.size() >= kLastErrorCount) errors_.erase(errors_.begin());
    errors_.push_back(text ? text : "");
}

void Telemetry::FrameMark(double gpuTopNs, double gpuBottomNs, int64_t cpuNs, uint64_t unityFrame) {
    std::lock_guard<std::mutex> lock(mutex_);
    ++marksTotal_;
    if (marksTotal_ > 1 && gpuTopNs < lastMarkGpuNs_) { ++nonMonotonicMarks_; counters_[FS_CTR_TIMESTAMP_NONMONO].fetch_add(1, std::memory_order_relaxed); }
    lastMarkGpuNs_ = gpuTopNs; lastMarkCpuNs_ = cpuNs; lastUnityFrame_ = unityFrame; lastMarkSpanNs_ = gpuBottomNs - gpuTopNs;
    clock_.Add(gpuTopNs, cpuNs);
    if ((marksTotal_ & 15u) == 0 || !clock_.Valid()) clock_.Fit();   // refit every 16 marks (~0.2 s)
}

double Telemetry::GpuNowEstimateNs(int64_t cpuNowNs) const {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!clock_.Valid()) return 0.0;
    return clock_.GpuFromCpu(cpuNowNs);
}

bool Telemetry::ClockValid() const { std::lock_guard<std::mutex> lock(mutex_); return clock_.Valid(); }

void Telemetry::Reset() {
    for (auto& c : counters_) c.store(0, std::memory_order_relaxed);
    std::lock_guard<std::mutex> lock(mutex_);
    for (auto& s : stages_) s = StageRecord{};
    errors_.clear(); errorsTotal_ = 0; clock_.Reset(); marksTotal_ = 0; nonMonotonicMarks_ = 0;
}

std::string Telemetry::Json(bool includeStages, const std::string& extraMembers) const {
    JsonWriter w;
    w.BeginObject();
    if (!extraMembers.empty()) {
        // Splice pre-serialized members: JsonWriter has no raw-member API, so emit a placeholder object and patch.
        w.RawValue("host", "{" + extraMembers + "}");
    }
    w.Key("counters"); w.BeginObject();
    for (int32_t c = 0; c < FS_CTR_COUNT; ++c) w.KV(CounterName(c), counters_[c].load(std::memory_order_relaxed));
    w.EndObject();
    std::lock_guard<std::mutex> lock(mutex_);
    w.Key("clock"); w.BeginObject();
    w.KV("valid", clock_.Valid()); w.KV("samples", static_cast<uint64_t>(clock_.Samples()));
    w.KV("offsetNs", clock_.Offset(), 15); w.KV("slope", clock_.Slope(), 12); w.KV("residualRmsUs", clock_.ResidualRmsNs() / 1e3, 4);
    w.KV("frameMarks", marksTotal_); w.KV("nonMonotonicMarks", nonMonotonicMarks_);
    w.KV("lastMarkGpuNs", lastMarkGpuNs_, 15); w.KV("lastMarkCpuNs", lastMarkCpuNs_); w.KV("lastMarkSpanUs", lastMarkSpanNs_ / 1e3, 4);
    w.KV("lastUnityFrame", lastUnityFrame_);
    w.KV("biasNote", "cpu = offset + slope*gpu; cpu sample is the record time of the mark (execute latency bias, constant)");
    w.EndObject();
    w.Key("errors"); w.BeginObject();
    w.KV("total", errorsTotal_);
    w.Key("last"); w.BeginArray(); for (const auto& e : errors_) w.String(e); w.EndArray();
    w.EndObject();
    const uint64_t seq = stageSeq_.load(std::memory_order_relaxed);
    w.KV("stagesTotal", seq);
    if (includeStages) {
        w.Key("stages"); w.BeginArray();
        const uint64_t count = seq < kStageRingSize ? seq : kStageRingSize;
        for (uint64_t i = 0; i < count; ++i) {
            const StageRecord& r = stages_[(seq - count + i) % kStageRingSize];
            if (r.sequence == 0) continue;
            w.BeginObject();
            w.KV("seq", r.sequence); w.KV("cls", static_cast<int32_t>(r.cls)); w.KV("stage", r.stage);
            w.KV("gpuStartNs", r.gpuStartNs); w.KV("gpuEndNs", r.gpuEndNs); w.KV("gpuUs", static_cast<double>(r.gpuEndNs - r.gpuStartNs) / 1e3, 4);
            w.KV("frame", r.frameIndex);
            w.EndObject();
        }
        w.EndArray();
    }
    w.EndObject();
    return w.Take();
}

} // namespace fs
