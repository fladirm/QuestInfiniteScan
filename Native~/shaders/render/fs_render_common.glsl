// Render (cull/HZB) GLSL common: per-frame block, work/HZB buffer layouts and the pure-math twins of
// Native~/src/host/render/fs_cull_math.h and fs_hzb_math.h. Binding numbers are the contract with
// fs_render.cpp (<= 8 storage bindings per pipeline, gate §15.6).
#ifndef FS_RENDER_COMMON_GLSL
#define FS_RENDER_COMMON_GLSL
#include "../world/fs_world_common.glsl"

// ---- bindings --------------------------------------------------------------------------------------
#define FS_RB_FRAME   0
#define FS_RB_SLOTS   1
#define FS_RB_SURFELS 2
#define FS_RB_NODES   3
#define FS_RB_ERRORS  4
#define FS_RB_WORK    5
#define FS_RB_DRAW    6      // emit/compact: Unity draw record buffer; nodes: native aggregate scratch (same binding id)
#define FS_RB_HZB     7
#define FS_RB_ARGS    3      // compact kernel: Unity indirect args (2 x FsIndirectDrawArgs, 32 B)
#define FS_RB_STATS   4      // compact kernel: host-visible stats ring
#define FS_RB_AGGSRC  2      // compact kernel: native aggregate scratch (read)
#define FS_RB_DEPTH   2      // hzb scatter: combined image samplers, binding 2 = previous own depth, 3 = Environment Depth
#define FS_OPAQUE_VERTS 24   // circumscribed 8-gon fan per opaque surfel (no discard, LRZ friendly)
#define FS_AGG_VERTS    6    // quad per aggregate (hashed alpha / alpha-to-coverage)

// ---- per-frame block (twin: fs::render::CullFrame) ---------------------------------------------------
struct FsCullFrame {
    vec4 planesL[6];
    vec4 planesR[6];
    mat4 centerViewProj;
    mat4 centerView;
    mat4 srcInvViewProj[2];     // previous-frame own depth, per eye: NDC(x,y,z01) -> world
    mat4 envInvViewProj[2];     // Environment Depth, per eye
    vec4 headPos_focal;         // xyz head position (world), w focal length px
    vec4 fwd_tanMargin;         // xyz view forward (world), w tan(prediction margin)
    vec4 lod;                   // fovealPx, peripheralPx, headroom bias, screenWorkBudget (as float)
    uvec4 misc;                 // renderMode, frameIndex, reuseFlag, srcFlags (bit0 prev valid, bit1 env valid, bit2 flipY)
    vec4 srcDepth;              // prev width, height, layers, unused
    vec4 envDepth;              // env width, height, nearZ, farZ
    mat4 anchors[FS_MAX_ANCHORS];
};
#ifdef FS_USE_FRAME
layout(std430, set = 0, binding = FS_RB_FRAME) readonly buffer FrameBlock { FsCullFrame frames[FS_CULL_FRAME_RING]; };
#endif
#ifdef FS_USE_RSLOTS
layout(std430, set = 0, binding = FS_RB_SLOTS) readonly buffer SlotBlock {
    FsSlotLayout layouts[FS_MAX_SLOTS];
    uint         roots[FS_MAX_SLOTS];
    FsPageHeader headers[FS_MAX_SLOTS];
};
#endif
#ifdef FS_USE_RSURFELS
layout(std430, set = 0, binding = FS_RB_SURFELS) readonly buffer SurfelBlock { FsSurfel surfels[]; };
#endif
#ifdef FS_USE_RNODES
layout(std430, set = 0, binding = FS_RB_NODES) readonly buffer NodeBlock { FsClusterNode nodes[]; };
#endif
#ifdef FS_USE_RERRORS
layout(std430, set = 0, binding = FS_RB_ERRORS) readonly buffer ErrorBlock { FsClusterError errors[]; };
#endif
// ---- work buffer (u32 words, host-visible UMA, STORAGE|INDIRECT): counters, indirect args, stats,
// visible page list, temporal page cache, leaf-range list. Reset by the emit pass's last workgroup.
#define FS_WK_OPAQUE       0
#define FS_WK_AGG          1
#define FS_WK_FINISHED     2     // compact pass completion counter (last workgroup writes args/stats/reset)
#define FS_WK_NODE_ARGS    4     // {FS_CULL_NODE_GROUPS, visiblePages, 1, 0}: byte offset 16 (y = visible page count)
#define FS_WK_EMIT_ARGS    8     // {leafRanges, 1, 1, 0}: byte offset 32 (x = leaf range count)
#define FS_WK_COMPACT_ARGS 12    // {ceil(aggregates/64) >= 1, 1, 1, 0}: byte offset 48
#define FS_WK_STAT         16    // FS_CSTAT_* words live at FS_WK_STAT + k
#define FS_WK_VIS_SLOT     32
#define FS_WK_VIS_ITEMS    (FS_WK_VIS_SLOT + FS_MAX_SLOTS)
#define FS_WK_CACHE_ROOT   (FS_WK_VIS_ITEMS + FS_MAX_SLOTS)
#define FS_WK_CACHE_STATE  (FS_WK_CACHE_ROOT + FS_MAX_SLOTS)
#define FS_WK_LEAF_LIST    (FS_WK_CACHE_STATE + FS_MAX_SLOTS)   // uvec4 {firstSurfel, count, slot, flags} x capacity
#define FS_WK_WORDS        (FS_WK_LEAF_LIST + 4 * FS_CULL_LEAF_LIST_CAPACITY)
#define FS_WK_RAW_BIT      0x80000000u
#define FS_PAGE_STATE_OUTSIDE 1u
#define FS_PAGE_STATE_VISIBLE 2u
#ifdef FS_USE_WORK
layout(std430, set = 0, binding = FS_RB_WORK) buffer WorkBlock { uint work[]; };
#endif
#ifdef FS_USE_DRAW
layout(std430, set = 0, binding = FS_RB_DRAW) writeonly buffer DrawBlock { FsDrawRecord draws[]; };
#endif
// ---- HZB buffer (u32 words: float bits; 0 = no occluder) -----------------------------------------------
#define FS_HZB_L0_TEXELS   (FS_HZB_SIZE * FS_HZB_SIZE)
#define FS_HZB_TOTAL       87381   // 256^2 + 128^2 + ... + 1 (twin: HzbTotalTexels)
#define FS_HZB_OFF_PREV    0
#define FS_HZB_OFF_ENV     FS_HZB_L0_TEXELS
#define FS_HZB_OFF_GRAD    (2 * FS_HZB_L0_TEXELS)
#define FS_HZB_OFF_COMB    (3 * FS_HZB_L0_TEXELS)
#define FS_HZB_OFF_CGRAD   (FS_HZB_OFF_COMB + FS_HZB_TOTAL)
#define FS_HZB_WORDS       (FS_HZB_OFF_CGRAD + FS_HZB_TOTAL)
#ifdef FS_USE_HZB
layout(std430, set = 0, binding = FS_RB_HZB) buffer HzbBlock { uint hzb[]; };
#endif
uint fsHzbLevelOffset(uint level) { uint off = 0u, s = uint(FS_HZB_SIZE); for (uint l = 0u; l < uint(FS_HZB_LEVELS); ++l) { if (l >= level) break; off += s * s; s >>= 1u; } return off; }
uint fsHzbLevelForExtent(float extentTiles) { uint lv = 0u; float e = extentTiles; for (uint i = 0u; i < uint(FS_HZB_LEVELS); ++i) { if (!(e > 1.0 && lv + 1u < uint(FS_HZB_LEVELS))) break; e *= 0.5; lv++; } return lv; }

// ---- math twins ----------------------------------------------------------------------------------------
bool fsAabbInFrustum6(vec4 p0, vec4 p1, vec4 p2, vec4 p3, vec4 p4, vec4 p5, vec3 bmin, vec3 bmax, float marginM) {
    vec4 pl[6]; pl[0] = p0; pl[1] = p1; pl[2] = p2; pl[3] = p3; pl[4] = p4; pl[5] = p5;
    for (int k = 0; k < 6; ++k) {
        vec4 p = pl[k];
        vec3 q = vec3(p.x >= 0.0 ? bmax.x + marginM : bmin.x - marginM, p.y >= 0.0 ? bmax.y + marginM : bmin.y - marginM, p.z >= 0.0 ? bmax.z + marginM : bmin.z - marginM);
        if (dot(p.xyz, q) + p.w < 0.0) return false;
    }
    return true;
}
void fsTransformAabb(mat4 M, vec3 bmin, vec3 bmax, out vec3 wmin, out vec3 wmax) {
    wmin = vec3(1e30); wmax = vec3(-1e30);
    for (int i = 0; i < 8; ++i) {
        vec3 c = vec3((i & 1) != 0 ? bmax.x : bmin.x, (i & 2) != 0 ? bmax.y : bmin.y, (i & 4) != 0 ? bmax.z : bmin.z);
        vec3 w = (M * vec4(c, 1.0)).xyz; wmin = min(wmin, w); wmax = max(wmax, w);
    }
}
float fsFoveatedThresholdPx(float fovealPx, float peripheralPx, float eccDeg, float bias) {
    float t = clamp((eccDeg - FS_FOVEA_DEG) / (FS_PERIPHERY_DEG - FS_FOVEA_DEG), 0.0, 1.0); t = t * t * (3.0 - 2.0 * t);
    return mix(fovealPx, peripheralPx, t) * bias;
}
float fsProjectedPx(float diameterM, float depthM, float focalPx) { return diameterM * focalPx / max(depthM, 0.05); }
float fsEccentricityDeg(vec3 fwd, vec3 dir) { float l = length(dir); if (l < 1e-9) return 0.0; return degrees(acos(clamp(dot(fwd, dir) / l, -1.0, 1.0))); }
float fsHzbMargin(float sigmaM, float gradM) { return max(sigmaM, FS_HZB_MARGIN_MIN_M) + FS_HZB_GRAD_K * abs(gradM); }
// 0 keep, 1 keep-in-band, 2 reject (twin: HzbTest)
uint fsHzbTest(float nearestDepth, float occ, float sigmaM, float gradM, uint mode) {
    if (mode == uint(FS_RENDER_MODE_XRAY)) return 0u;
    if (occ <= 0.0) return 0u;
    float margin = fsHzbMargin(sigmaM, gradM);
    if (nearestDepth > occ + margin) return 2u;
    if (nearestDepth > occ) return 1u;
    return 0u;
}
#endif
