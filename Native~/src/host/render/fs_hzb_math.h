// Pure HZB band-occlusion math (contract §13.5.1, invariant): a node is rejected only when it is
// provably behind the observed surface band: nearestDepth(node) > occluderDepth + margin with
// margin = max(sigma, FS_HZB_MARGIN_MIN_M) + FS_HZB_GRAD_K * |grad depth|. Never rejects inside the band,
// so the scan stays visible on the real surface and both sides of a thin partition survive. XRAY/RADAR
// mode disables the test entirely. Twin of the GLSL in render_cull_expand.comp / render_hzb_*.comp.
#pragma once
#include <stdint.h>
#include <math.h>
#include "../world/fs_world_params.h"

namespace fs {
namespace render {

constexpr float kHzbNoOccluder = 0.f;          // stored depth 0 = no occluder in that tile (treated as +inf)

inline float HzbMargin(float sigmaM, float gradM) {
    float s = sigmaM > (float)FS_HZB_MARGIN_MIN_M ? sigmaM : (float)FS_HZB_MARGIN_MIN_M;
    return s + (float)FS_HZB_GRAD_K * fabsf(gradM);
}
enum HzbVerdict { HZB_KEEP = 0, HZB_KEEP_IN_BAND = 1, HZB_REJECT = 2, HZB_KEEP_UNCERTAIN = 3 };
// nearestDepth: nearest view-space depth of the node (metres); occluderDepth: conservative (max) tile depth;
// uncertain: the combine pass flagged the tile (sources disagree, or Environment Depth without scan history).
// Fails OPEN (C09R §21): no occluder or an uncertain tile keeps the node; only a proven band violation rejects.
inline HzbVerdict HzbTest(float nearestDepth, float occluderDepth, bool uncertain, float sigmaM, float gradM, int renderMode) {
    if (renderMode == FS_RENDER_MODE_XRAY) return HZB_KEEP;
    if (occluderDepth <= kHzbNoOccluder) return HZB_KEEP;
    if (uncertain) return HZB_KEEP_UNCERTAIN;
    float margin = HzbMargin(sigmaM, gradM);
    if (nearestDepth > occluderDepth + margin) return HZB_REJECT;
    if (nearestDepth > occluderDepth) return HZB_KEEP_IN_BAND;   // exact test would have culled it: the band keeps it
    return HZB_KEEP;
}
// Combine rule of render_hzb_combine.comp: scan depth primary; env depth agrees within K x margin -> farther value,
// disagrees -> farther value + uncertain; env only -> uncertain. Returns the occluder, sets `uncertain`.
inline float HzbCombineSources(float prev, float envEroded, float gradM, bool& uncertain) {
    uncertain = false;
    if (prev > 0.f && envEroded > 0.f) {
        float margin = (float)FS_HZB_AGREE_K * HzbMargin(0.f, gradM);
        uncertain = fabsf(prev - envEroded) > margin;
        return prev > envEroded ? prev : envEroded;
    }
    if (prev > 0.f) return prev;
    if (envEroded > 0.f) { uncertain = true; return envEroded; }
    return 0.f;
}
// Mip level whose texel covers the screen rect (in level-0 tiles): the 2x2 fetch at that level covers it.
inline uint32_t HzbLevelForExtent(float extentTiles) {
    uint32_t lv = 0; float e = extentTiles;
    while (e > 1.f && lv + 1 < FS_HZB_LEVELS) { e *= 0.5f; ++lv; }
    return lv;
}
inline uint32_t HzbLevelOffset(uint32_t level) {        // texel offset of a level inside the flat HZB buffer
    uint32_t off = 0, s = FS_HZB_SIZE;
    for (uint32_t l = 0; l < level; ++l) { off += s * s; s >>= 1; }
    return off;
}
inline uint32_t HzbTotalTexels() { return HzbLevelOffset(FS_HZB_LEVELS); }
inline uint32_t HzbLevelSize(uint32_t level) { return FS_HZB_SIZE >> level; }
// Conservative combine of two occluder depths: the farther one wins (missing = no occluder).
inline float HzbCombine(float a, float b) { return a > b ? a : b; }

} // namespace render
} // namespace fs
