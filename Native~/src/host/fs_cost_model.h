// C09R-E5R per-stage GPU cost model: t_job(n) = t_fixed + n * t_item, fitted by exponentially weighted least squares over the
// measured (items, gpu us) of ONE stage. The batch for a target quantum comes from the VARIABLE cost; a job whose fixed cost alone
// reaches the target is reported as structural (the batch then spends one target of variable work on top of the fixed cost, it is
// never halved toward zero). Samples below the learning floor (near-empty jobs, e.g. an empty depth frame) are not learned, so they
// cannot swing the batch. Header-only, no Vulkan: shared by the world and measurement schedulers and the host tests.
#pragma once
#include <algorithm>
#include <cstdint>
#include <cmath>

namespace fs {

struct CostModel {
    double alpha = 0.15;                  // EWMA weight of a new sample
    uint32_t minLearnItems = 16;          // samples with fewer items are not learned
    double w = 0, sn = 0, st = 0, snn = 0, snt = 0;
    uint64_t samples = 0, skipped = 0;
    double fixedUs = 0, perItemUs = 0;    // last fit
    bool fitted = false, structural = false, identifiable = false;
    uint32_t probeCalls = 0; uint64_t probes = 0;
    uint32_t lastItems = 0;
    int64_t lastGpuUs = 0;

    void Add(uint32_t items, int64_t gpuUs) {
        if (items != 0 && gpuUs > 0) { lastItems = items; lastGpuUs = gpuUs; }
        if (items < minLearnItems || gpuUs <= 0) { skipped++; return; }
        const double a = samples == 0 ? 1.0 : alpha, n = (double)items, t = (double)gpuUs;
        w = (1 - a) * w + a; sn = (1 - a) * sn + a * n; st = (1 - a) * st + a * t; snn = (1 - a) * snn + a * n * n; snt = (1 - a) * snt + a * n * t;
        samples++;
        Fit();
    }
    void Fit() {
        if (w <= 0) { fitted = false; return; }
        const double mn = sn / w, mt = st / w, var = snn / w - mn * mn, cov = snt / w - mn * mt;
        identifiable = var > 0.01 * mn * mn && cov > 0;
        if (identifiable) {                                             // slope + intercept
            perItemUs = cov / var; fixedUs = std::max(0.0, mt - perItemUs * mn);
        } else {                                                        // one batch size so far: attribute everything to the items (conservative)
            perItemUs = mn > 0 ? mt / mn : 0; fixedUs = 0;
        }
        perItemUs = std::max(perItemUs, 1e-3);
        fitted = true;
    }
    // Items for a target quantum. A fixed/structural cost above target is NOT permission to add another
    // target of variable work: the only safe action on a shared XR queue is the minimum bounded batch.
    // The most recent measured job is an independent safety receipt and caps both shrink/growth.
    uint32_t Batch(double targetUs, uint32_t minItems, uint32_t maxItems, uint32_t initial) {
        minItems = std::max<uint32_t>(1u, minItems);
        maxItems = std::max(maxItems, minItems);
        if (!fitted) { structural = false; return std::min(std::max(initial, minItems), maxItems); }

        structural = fixedUs >= targetUs;
        if (structural) return minItems;

        double n = (targetUs - fixedUs) / perItemUs;
        if (lastItems != 0 && lastGpuUs > 0 && targetUs > 0) {
            if ((double)lastGpuUs > targetUs) {
                // Scale from what the GPU actually did, with headroom. Never trust a noisy linear fit to grow after an overrun.
                const double safe = (double)lastItems * targetUs / (double)lastGpuUs * 0.85;
                n = std::min(n, std::max<double>((double)minItems, safe));
            } else {
                // Convergence is deliberately gradual; prevents oscillation such as 512 -> 4714 -> 512.
                n = std::min(n, std::max<double>((double)minItems, (double)lastItems * 1.5));
            }
        }
        return (uint32_t)std::min<double>(std::max<double>(n, (double)minItems), (double)maxItems);
    }
};

// E6R scheduler decision (twin of World::Tick).
//
// There are TWO ages and they are intentionally not interchangeable:
//   pendingAge: how old the oldest item of the class is;
//   serviceAge: how long the runnable class itself has received no completed GPU service.
//
// A large persistent backlog must not monopolise the executor merely because its oldest item stays old while a
// bounded job makes progress. Service starvation therefore wins first, then pending-deadline lateness, then
// weighted deficit. This is the device-proven closure for PUBLISH/TOPOLOGY starving FUSE.
inline int PickStage(const bool runnable[3], const float pendingAgeMs[3], const float serviceAgeMs[3],
                     const float deadlineMs[3], const double deficit[3], bool* overdueOut = nullptr) {
    int pick = -1;
    float best = 1.f;

    // Hard anti-starvation: a runnable class not serviced for one class deadline is overdue independently of backlog age.
    for (int k = 0; k < 3; ++k) {
        if (!runnable[k] || deadlineMs[k] <= 0.f) continue;
        const float late = serviceAgeMs[k] / deadlineMs[k];
        if (late > best) { best = late; pick = k; }
    }
    if (pick >= 0) {
        if (overdueOut) *overdueOut = true;
        return pick;
    }

    // No service starvation: now honour the oldest pending work.
    best = 1.f;
    for (int k = 0; k < 3; ++k) {
        if (!runnable[k] || deadlineMs[k] <= 0.f) continue;
        const float late = pendingAgeMs[k] / deadlineMs[k];
        if (late > best) { best = late; pick = k; }
    }
    if (pick >= 0) {
        if (overdueOut) *overdueOut = true;
        return pick;
    }

    // All runnable classes are inside their service and pending deadlines: weighted GPU-time deficit decides.
    double bestDeficit = -1e300;
    for (int k = 0; k < 3; ++k)
        if (runnable[k] && deficit[k] > bestDeficit) { bestDeficit = deficit[k]; pick = k; }

    if (overdueOut) *overdueOut = false;
    return pick;
}

// Compatibility overload for pure host callers which do not model service age.
inline int PickStage(const bool runnable[3], const float pendingAgeMs[3], const float deadlineMs[3],
                     const double deficit[3], bool* overdueOut = nullptr) {
    const float serviceAgeMs[3] = {0.f, 0.f, 0.f};
    return PickStage(runnable, pendingAgeMs, serviceAgeMs, deadlineMs, deficit, overdueOut);
}

// Rolling p50 / p95 of a bounded sample window (ages in ms).
struct AgeWindow {
    static constexpr uint32_t kCap = 512;
    float v[kCap] = {}; uint32_t n = 0, next = 0; uint64_t total = 0;
    void Add(float ms) { v[next] = ms; next = (next + 1) % kCap; n = std::min(n + 1, kCap); total++; }
    float Max() const { float m = 0; for (uint32_t i = 0; i < n; ++i) m = std::max(m, v[i]); return m; }
    float Quantile(float q) const {
        if (!n) return 0;
        float tmp[kCap]; std::copy(v, v + n, tmp); const uint32_t k = std::min<uint32_t>(n - 1, (uint32_t)(q * (float)(n - 1) + 0.5f));
        std::nth_element(tmp, tmp + k, tmp + n); return tmp[k];
    }
};

} // namespace fs
