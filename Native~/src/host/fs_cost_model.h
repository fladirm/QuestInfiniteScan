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
    bool fitted = false, structural = false;

    void Add(uint32_t items, int64_t gpuUs) {
        if (items < minLearnItems || gpuUs <= 0) { skipped++; return; }
        const double a = samples == 0 ? 1.0 : alpha, n = (double)items, t = (double)gpuUs;
        w = (1 - a) * w + a; sn = (1 - a) * sn + a * n; st = (1 - a) * st + a * t; snn = (1 - a) * snn + a * n * n; snt = (1 - a) * snt + a * n * t;
        samples++;
        Fit();
    }
    void Fit() {
        if (w <= 0) { fitted = false; return; }
        const double mn = sn / w, mt = st / w, var = snn / w - mn * mn, cov = snt / w - mn * mt;
        if (var > 0.01 * mn * mn && cov > 0) {                          // identifiable: slope + intercept
            perItemUs = cov / var; fixedUs = std::max(0.0, mt - perItemUs * mn);
        } else {                                                        // one batch size so far: attribute everything to the items (conservative)
            perItemUs = mn > 0 ? mt / mn : 0; fixedUs = 0;
        }
        perItemUs = std::max(perItemUs, 1e-3);
        fitted = true;
    }
    // Items for a target quantum. Unfitted: `initial`. Fixed >= target: structural -> spend one target of variable work.
    uint32_t Batch(double targetUs, uint32_t minItems, uint32_t maxItems, uint32_t initial) {
        if (!fitted) { structural = false; return std::min(std::max(initial, minItems), maxItems); }
        structural = fixedUs >= targetUs;
        const double variable = structural ? targetUs : targetUs - fixedUs;
        const double n = variable / perItemUs;
        return (uint32_t)std::min<double>(std::max<double>(n, (double)minItems), (double)maxItems);
    }
};

// Rolling p50 / p95 of a bounded sample window (ages in ms).
struct AgeWindow {
    static constexpr uint32_t kCap = 512;
    float v[kCap] = {}; uint32_t n = 0, next = 0; uint64_t total = 0;
    void Add(float ms) { v[next] = ms; next = (next + 1) % kCap; n = std::min(n + 1, kCap); total++; }
    float Quantile(float q) const {
        if (!n) return 0;
        float tmp[kCap]; std::copy(v, v + n, tmp); const uint32_t k = std::min<uint32_t>(n - 1, (uint32_t)(q * (float)(n - 1) + 0.5f));
        std::nth_element(tmp, tmp + k, tmp + n); return tmp[k];
    }
};

} // namespace fs
