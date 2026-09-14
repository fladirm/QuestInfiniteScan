// Host telemetry (contract §20, §15.8): atomic counters indexed by FsCounter, stage timing ring (last 256),
// GPU<->CPU clock correlation from frame marks (least squares), last errors. Driver-free (host-testable);
// executor.cpp feeds it, FsHost_GetTelemetryJson and the 5 s "FS-HOST" log line read it.
#pragma once
#include <stdint.h>
#include <atomic>
#include <mutex>
#include <string>
#include <vector>
#include "../../include/finalscan_host_api.h"

namespace fs {

constexpr uint32_t kStageRingSize = 256;
constexpr uint32_t kStageNameChars = 31;
constexpr uint32_t kClockFitWindow = 512;       // frame marks kept for the fit (~7 s at 72 Hz)
constexpr uint32_t kLastErrorCount = 8;

struct StageRecord {
    uint8_t  cls = 0;
    char     stage[kStageNameChars + 1] = {};
    uint64_t gpuStartNs = 0, gpuEndNs = 0;
    uint32_t frameIndex = 0;
    uint64_t sequence = 0;
};

// Least-squares fit cpuNs = offset + slope * gpuNs over the last kClockFitWindow samples.
// Samples come from frame marks: GPU TOP_OF_PIPE timestamp of the mark vs CPU monotonic time when the mark
// was recorded (a constant record->execute latency bias remains; documented in the JSON as "biasNote").
class ClockFit {
public:
    void   Add(double gpuNs, int64_t cpuNs);
    bool   Fit();                                  // recomputes offset/slope; false with < 2 samples
    bool   Valid() const { return valid_; }
    double Offset() const { return offset_; }
    double Slope() const { return slope_; }
    double ResidualRmsNs() const { return residualRms_; }
    size_t Samples() const { return samples_.size(); }
    double CpuFromGpu(double gpuNs) const { return offset_ + slope_ * gpuNs; }
    double GpuFromCpu(int64_t cpuNs) const { return slope_ != 0.0 ? (static_cast<double>(cpuNs) - offset_) / slope_ : 0.0; }
    void   Reset() { samples_.clear(); valid_ = false; offset_ = 0; slope_ = 1; residualRms_ = 0; }
private:
    struct Sample { double gpu; double cpu; };
    std::vector<Sample> samples_;
    bool   valid_ = false;
    double offset_ = 0.0, slope_ = 1.0, residualRms_ = 0.0;
};

class Telemetry {
public:
    void    CounterAdd(int32_t c, int64_t v);
    int64_t CounterGet(int32_t c) const;
    void    Stage(FsJobClass cls, const char* stage, uint64_t gpuStartNs, uint64_t gpuEndNs, uint32_t frameIndex);
    void    Error(const char* text);
    void    FrameMark(double gpuTopNs, double gpuBottomNs, int64_t cpuNs, uint64_t unityFrame);
    double  GpuNowEstimateNs(int64_t cpuNowNs) const;
    bool    ClockValid() const;
    uint64_t StageSequence() const { return stageSeq_.load(std::memory_order_relaxed); }
    // Snapshot for JSON: caller supplies the executor-side sections through `extra` (already-serialized JSON object
    // members without braces, may be empty).
    std::string Json(bool includeStages, const std::string& extraMembers) const;
    void    Reset();
private:
    std::atomic<int64_t> counters_[FS_CTR_COUNT] = {};
    std::atomic<uint64_t> stageSeq_{0};
    mutable std::mutex mutex_;
    StageRecord stages_[kStageRingSize] = {};
    std::vector<std::string> errors_;
    uint64_t errorsTotal_ = 0;
    ClockFit clock_;
    uint64_t marksTotal_ = 0;
    double lastMarkGpuNs_ = 0.0; int64_t lastMarkCpuNs_ = 0; uint64_t lastUnityFrame_ = 0;
    double lastMarkSpanNs_ = 0.0;
    uint64_t nonMonotonicMarks_ = 0;
};

Telemetry& Tele();
const char* CounterName(int32_t c);

} // namespace fs
