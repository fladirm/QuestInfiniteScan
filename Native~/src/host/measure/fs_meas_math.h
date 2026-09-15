// Pure measurement math (no Vulkan): Environment Depth texel -> anchor-local FsSurfaceMeasurement.
// Byte-for-byte twin of Native~/shaders/measure/fs_meas_common.glsl and of the per-thread body of
// measure_depth_backproject.comp; Native~/tests/host_tests_measure.cpp pins both sides' behaviour.
// Conventions (assumed from the C# feeder, Runtime/Host/EnvDepthFeeder.cs, and Unity camera space):
//   * pose = world-from-eye TRS (column-major, m[col*4+row]); eye space is Unity's (+X right, +Y up, +Z forward);
//   * fov = [tan left, tan right, tan up, tan down] with provider signs (left/down negative);
//   * texel value = projected depth in [0,1] (OpenGL-style, XR_META_environment_depth near/far), or metres
//     when FS_MEAS_FLAG_LINEAR_DEPTH is set; farZ <= 0 or infinite = unbounded far plane;
//   * row 0 = top (tan up) edge unless FS_MEAS_FLAG_FLIP_Y (the Measure default: XR_META_environment_depth row 0 = bottom).
// Contract §6 (depth = prior), §7.4 (uncertainty), §7.6 (information-driven compaction, C09R-E2): every valid
// texel gets an information score from the canonical prediction (the previous rendered depth = FRONT reprojected
// into the camera); a score histogram yields the threshold that keeps `budget` texels; the emitted records are
// ordered by (workgroup, lane) through a prefix sum, so the measurement stream is a pure function of the inputs.
#pragma once
#include <stdint.h>
#include <math.h>
#include <string.h>
#include <vector>
#include "../../../include/finalscan_world_abi.h"
#include "fs_meas_params.h"

namespace fs {
namespace meas {

static_assert(sizeof(FsSurfaceMeasurement) == 48, "FsSurfaceMeasurement layout");

// ---- small vector / matrix helpers (column-major 4x4, m[col*4+row]) ---------------------------------------
struct Vec3 { float x, y, z; };
struct Mat4 { float m[16]; };
inline Vec3 V3(float x, float y, float z) { Vec3 v; v.x = x; v.y = y; v.z = z; return v; }
inline Vec3 Sub(Vec3 a, Vec3 b) { return V3(a.x - b.x, a.y - b.y, a.z - b.z); }
inline float Dot(Vec3 a, Vec3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
inline Vec3 Cross(Vec3 a, Vec3 b) { return V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
inline float Length(Vec3 a) { return sqrtf(Dot(a, a)); }
inline Vec3 Normalize(Vec3 a) { float l = Length(a); return l < 1e-20f ? V3(0.f, 0.f, 0.f) : V3(a.x / l, a.y / l, a.z / l); }
inline Mat4 Identity() { Mat4 r; memset(r.m, 0, sizeof r.m); r.m[0] = r.m[5] = r.m[10] = r.m[15] = 1.f; return r; }
inline Mat4 Mul(const Mat4& A, const Mat4& B) {          // A * B
    Mat4 r;
    for (int c = 0; c < 4; ++c) for (int row = 0; row < 4; ++row) {
        float s = 0.f; for (int k = 0; k < 4; ++k) s += A.m[k * 4 + row] * B.m[c * 4 + k];
        r.m[c * 4 + row] = s;
    }
    return r;
}
inline Vec3 MulPoint(const Mat4& A, Vec3 p) {            // w = 1, no perspective divide (affine use)
    return V3(A.m[0] * p.x + A.m[4] * p.y + A.m[8] * p.z + A.m[12],
              A.m[1] * p.x + A.m[5] * p.y + A.m[9] * p.z + A.m[13],
              A.m[2] * p.x + A.m[6] * p.y + A.m[10] * p.z + A.m[14]);
}
inline Vec3 MulDir(const Mat4& A, Vec3 d) {              // upper 3x3 (rigid transforms: normals stay unit)
    return V3(A.m[0] * d.x + A.m[4] * d.y + A.m[8] * d.z,
              A.m[1] * d.x + A.m[5] * d.y + A.m[9] * d.z,
              A.m[2] * d.x + A.m[6] * d.y + A.m[10] * d.z);
}
// General 4x4 inverse (cofactor); false when singular (anchor / pose matrices are rigid, but never assume).
inline bool Invert(const Mat4& in, Mat4& out) {
    const float* m = in.m; float inv[16];
    inv[0] = m[5]*m[10]*m[15] - m[5]*m[11]*m[14] - m[9]*m[6]*m[15] + m[9]*m[7]*m[14] + m[13]*m[6]*m[11] - m[13]*m[7]*m[10];
    inv[4] = -m[4]*m[10]*m[15] + m[4]*m[11]*m[14] + m[8]*m[6]*m[15] - m[8]*m[7]*m[14] - m[12]*m[6]*m[11] + m[12]*m[7]*m[10];
    inv[8] = m[4]*m[9]*m[15] - m[4]*m[11]*m[13] - m[8]*m[5]*m[15] + m[8]*m[7]*m[13] + m[12]*m[5]*m[11] - m[12]*m[7]*m[9];
    inv[12] = -m[4]*m[9]*m[14] + m[4]*m[10]*m[13] + m[8]*m[5]*m[14] - m[8]*m[6]*m[13] - m[12]*m[5]*m[10] + m[12]*m[6]*m[9];
    inv[1] = -m[1]*m[10]*m[15] + m[1]*m[11]*m[14] + m[9]*m[2]*m[15] - m[9]*m[3]*m[14] - m[13]*m[2]*m[11] + m[13]*m[3]*m[10];
    inv[5] = m[0]*m[10]*m[15] - m[0]*m[11]*m[14] - m[8]*m[2]*m[15] + m[8]*m[3]*m[14] + m[12]*m[2]*m[11] - m[12]*m[3]*m[10];
    inv[9] = -m[0]*m[9]*m[15] + m[0]*m[11]*m[13] + m[8]*m[1]*m[15] - m[8]*m[3]*m[13] - m[12]*m[1]*m[11] + m[12]*m[3]*m[9];
    inv[13] = m[0]*m[9]*m[14] - m[0]*m[10]*m[13] - m[8]*m[1]*m[14] + m[8]*m[2]*m[13] + m[12]*m[1]*m[10] - m[12]*m[2]*m[9];
    inv[2] = m[1]*m[6]*m[15] - m[1]*m[7]*m[14] - m[5]*m[2]*m[15] + m[5]*m[3]*m[14] + m[13]*m[2]*m[7] - m[13]*m[3]*m[6];
    inv[6] = -m[0]*m[6]*m[15] + m[0]*m[7]*m[14] + m[4]*m[2]*m[15] - m[4]*m[3]*m[14] - m[12]*m[2]*m[7] + m[12]*m[3]*m[6];
    inv[10] = m[0]*m[5]*m[15] - m[0]*m[7]*m[13] - m[4]*m[1]*m[15] + m[4]*m[3]*m[13] + m[12]*m[1]*m[7] - m[12]*m[3]*m[5];
    inv[14] = -m[0]*m[5]*m[14] + m[0]*m[6]*m[13] + m[4]*m[1]*m[14] - m[4]*m[2]*m[13] - m[12]*m[1]*m[6] + m[12]*m[2]*m[5];
    inv[3] = -m[1]*m[6]*m[11] + m[1]*m[7]*m[10] + m[5]*m[2]*m[11] - m[5]*m[3]*m[10] - m[9]*m[2]*m[7] + m[9]*m[3]*m[6];
    inv[7] = m[0]*m[6]*m[11] - m[0]*m[7]*m[10] - m[4]*m[2]*m[11] + m[4]*m[3]*m[10] + m[8]*m[2]*m[7] - m[8]*m[3]*m[6];
    inv[11] = -m[0]*m[5]*m[11] + m[0]*m[7]*m[9] + m[4]*m[1]*m[11] - m[4]*m[3]*m[9] - m[8]*m[1]*m[7] + m[8]*m[3]*m[5];
    inv[15] = m[0]*m[5]*m[10] - m[0]*m[6]*m[9] - m[4]*m[1]*m[10] + m[4]*m[2]*m[9] + m[8]*m[1]*m[6] - m[8]*m[2]*m[5];
    float det = m[0]*inv[0] + m[1]*inv[4] + m[2]*inv[8] + m[3]*inv[12];
    if (fabsf(det) < 1e-20f) return false;
    det = 1.f / det; for (int i = 0; i < 16; ++i) out.m[i] = inv[i] * det;
    return true;
}

// ---- push constants / frame block: byte-identical to the measure_*.comp kernels -------------------------------------
struct PushCompact {                     // 48 B (contract §15.3: <= 128 B)
    float    nearZ, farZ;                // farZ <= 0 = unbounded
    float    maxDepthM, minDepthM;       // gates: minDepthM = max(nearZ, FS_MEAS_MIN_DEPTH_M) (contract §6: hands / cables never measure)
    uint32_t layers, budget, maxOut, flags;
    uint32_t obsId, frame, width, height;
};
static_assert(sizeof(PushCompact) == 48, "push block layout");
// Per ring slot, host-visible (binding FS_MEAS_B_COUNTERS): the CPU resets ctr[] and writes the per-eye and
// prediction parameters before submit; the kernels atomically bump ctr[] and the select kernel writes the
// threshold words (std430 twin: uint[16], mat4[2], vec4[2], mat4[2], mat4[2], mat4[2], uvec4).
struct FrameBlock {
    uint32_t ctr[FS_MEAS_CTR_WORDS];
    float    anchorFromEye[2][16];       // column-major: anchor-local <- depth eye pose (at the depth's own XrTime)
    float    fov[2][4];                  // tan left, right, up, down per eye
    float    worldFromEye[2][16];        // depth eye pose (world): prediction lives in world space
    float    predViewProj[2][16];        // canonical prediction: world -> clip of the previous rendered eye views
    float    predInvViewProj[2][16];     // clip (ndc xy, depth01) -> world
    uint32_t predInfo[4];                // width, height, layers, valid (0 = no prediction this frame: every valid texel is NEW)
};
static_assert(sizeof(FrameBlock) == FS_MEAS_FB_BYTES, "frame block layout");

// ---- thread -> (layer, texel) mapping (twin: kernel main). Lanes alternate eyes so both fill the cap evenly;
// the texel order rotates by FS_MEAS_ROW_PHASE_STRIDE rows per frame so the cap cuts a different band each frame.
inline uint32_t RowPhase(uint32_t frame, uint32_t h) { return (frame * FS_MEAS_ROW_PHASE_STRIDE) % h; }
inline void ThreadToTexel(uint32_t t, uint32_t layers, uint32_t w, uint32_t h, uint32_t frame, uint32_t& layer, uint32_t& x, uint32_t& y) {
    const uint32_t n = w * h;
    layer = t % layers;
    const uint32_t texel = (t / layers + RowPhase(frame, h) * w) % n;
    x = texel % w; y = texel / w;
}

// ---- per-texel math (twins: fsMeas* in fs_meas_common.glsl) ------------------------------------------------
// Projected depth -> metres along the eye z axis. Returns -1 for "no data" (0, 1, NaN, out of range).
inline float LinearizeDepth(float d, float nearZ, float farZ, uint32_t flags) {
    if (!(d == d)) return -1.f;                                   // NaN
    if (flags & FS_MEAS_FLAG_LINEAR_DEPTH) return d;
    if (d <= 0.f || d >= 1.f) return -1.f;
    if (farZ > 0.f && farZ < 3.0e38f) {                           // finite far: GL z01 = ((f+n)/(f-n) - 2fn/((f-n) z) + 1) / 2
        float ndc = d * 2.f - 1.f;
        float den = (farZ + nearZ) - ndc * (farZ - nearZ);
        return den > 1e-12f ? 2.f * farZ * nearZ / den : -1.f;
    }
    return nearZ / (1.f - d);                                     // infinite far: z01 = 1 - n/z
}
inline bool DepthUsable(float z, float nearZ, float maxDepthM) { return z == z && z > nearZ && z <= maxDepthM; }
// Ray tangents of texel (x, y): pixel centre, linear in the fov tangents; row 0 = top unless FLIP_Y.
inline void RayTangents(uint32_t x, uint32_t y, uint32_t w, uint32_t h, const float fov[4], uint32_t flags, float& tx, float& ty) {
    float u = ((float)x + 0.5f) / (float)w, v = ((float)y + 0.5f) / (float)h;
    tx = fov[0] + (fov[1] - fov[0]) * u;
    ty = (flags & FS_MEAS_FLAG_FLIP_Y) ? fov[3] + (fov[2] - fov[3]) * v : fov[2] + (fov[3] - fov[2]) * v;
}
inline Vec3 EyePoint(float tx, float ty, float z) { return V3(tx * z, ty * z, z); }
inline bool IsEdge(float d, float dxm, float dxp, float dym, float dyp) {
    float t = (float)FS_MEAS_EDGE_RATIO * d;
    return fabsf(dxm - d) > t || fabsf(dxp - d) > t || fabsf(dym - d) > t || fabsf(dyp - d) > t;
}
inline bool IsFlat(float d, float dxm, float dxp, float dym, float dyp) {
    return fabsf(dxm + dxp + dym + dyp - 4.f * d) < (float)FS_MEAS_FLAT_LAPLACIAN_RATIO * d;
}
inline float SigmaN(float d) { return (float)FS_MEAS_SIGMA_QUAD * d * d + (float)FS_MEAS_SIGMA_BASE_M; }
// Projected texel footprint at depth d: the larger of the horizontal / vertical angular texel size.
inline float Footprint(float d, const float fov[4], uint32_t w, uint32_t h) {
    float ax = (fov[1] - fov[0]) / (float)w, ay = (fov[2] - fov[3]) / (float)h;
    return d * (ax > ay ? ax : ay);
}
// ---- information score (contract §7.6, C09R-E2; twins: fsMeasScore / fsMeasHash / fsMeasSelected) --------------------
// steep = a neighbour step above half the edge ratio (thin / oblique surface, next to a discontinuity).
inline bool IsSteep(float d, float dxm, float dxp, float dym, float dyp) {
    float t = (float)FS_MEAS_STEEP_RATIO * d;
    return fabsf(dxm - d) > t || fabsf(dxp - d) > t || fabsf(dym - d) > t || fabsf(dyp - d) > t;
}
inline uint32_t ScoreOf(bool predicted, float residualSigma, bool flat, bool steep, bool detail) {
    uint32_t s;
    if (!predicted) s = FS_MEAS_SCORE_NEW;
    else if (residualSigma > 3.f) { uint32_t e = (uint32_t)(residualSigma * 4.f); s = FS_MEAS_SCORE_FAR_BASE + (e > 63u ? 63u : e); }
    else if (residualSigma > 1.f) s = FS_MEAS_SCORE_BAND_BASE + (uint32_t)((residualSigma - 1.f) * 48.f);
    else s = FS_MEAS_SCORE_CONVERGED_BASE + (uint32_t)(residualSigma * 32.f);
    if (!flat) s += FS_MEAS_SCORE_CURVATURE;
    if (steep) s += FS_MEAS_SCORE_STEEP;
    if (detail && s < FS_MEAS_SCORE_DETAIL_MIN) s = FS_MEAS_SCORE_DETAIL_MIN;
    return s < 1u ? 1u : (s > 255u ? 255u : s);
}
// Deterministic per-texel hash (Wang) of the thread index and the frame: ties inside the threshold bin.
inline uint32_t HashTexel(uint32_t t, uint32_t frame) {
    uint32_t h = t * 2654435761u ^ (frame * 0x9E3779B9u);
    h = (h ^ 61u) ^ (h >> 16); h *= 9u; h ^= h >> 4; h *= 0x27d4eb2du; h ^= h >> 15;
    return h;
}
// Threshold of the score histogram: the largest T with count(score >= T) >= budget; `fraction16` (16.16) keeps
// exactly the missing part of bin T on average. All valid texels are kept when they fit the budget.
inline void SelectThreshold(const uint32_t hist[FS_MEAS_SCORE_BINS], uint32_t budget, uint32_t& threshold, uint32_t& fraction16) {
    uint32_t above = 0;                                       // count(score > T)
    for (int32_t t = FS_MEAS_SCORE_BINS - 1; t >= 1; --t) {
        const uint32_t cum = above + hist[t];
        if (cum >= budget) {
            threshold = (uint32_t)t;
            fraction16 = hist[t] ? (uint32_t)(((uint64_t)(budget - above) << 16) / hist[t]) : 65536u;
            if (fraction16 > 65536u) fraction16 = 65536u;
            return;
        }
        above = cum;
    }
    threshold = 1u; fraction16 = 65536u;
}
inline bool Selected(uint32_t score, uint32_t t, uint32_t frame, uint32_t threshold, uint32_t fraction16) {
    if (score == 0u || score < threshold) return false;
    if (score > threshold) return true;
    return (HashTexel(t, frame) >> 16) < fraction16;
}
// Canonical prediction lookup (twin of the score kernel): the measured eye point is projected into the previous
// rendered eye view of the same layer; the rendered depth is unprojected and compared along the eye ray.
// `pred(layer, px, py)` returns the raw rendered depth (0 / 1 = nothing rendered). Returns false = no prediction.
template <class PredFn>
inline bool PredictResidual(const FrameBlock& blk, uint32_t layer, Vec3 pEye, float z, uint32_t flags, PredFn pred, float& residualSigma) {
    if (blk.predInfo[3] == 0u || layer >= blk.predInfo[2]) return false;
    Mat4 W; memcpy(W.m, blk.worldFromEye[layer], 64);
    Mat4 VP; memcpy(VP.m, blk.predViewProj[layer], 64);
    Mat4 IVP; memcpy(IVP.m, blk.predInvViewProj[layer], 64);
    const Vec3 pw = MulPoint(W, pEye);
    const float cw = VP.m[3] * pw.x + VP.m[7] * pw.y + VP.m[11] * pw.z + VP.m[15];
    if (cw <= 1e-6f) return false;
    const float cx = (VP.m[0] * pw.x + VP.m[4] * pw.y + VP.m[8] * pw.z + VP.m[12]) / cw;
    const float cy = (VP.m[1] * pw.x + VP.m[5] * pw.y + VP.m[9] * pw.z + VP.m[13]) / cw;
    if (cx < -1.f || cx > 1.f || cy < -1.f || cy > 1.f) return false;
    float u = cx * 0.5f + 0.5f, v = cy * 0.5f + 0.5f;
    if (flags & FS_MEAS_FLAG_PRED_FLIP_Y) v = 1.f - v;
    const uint32_t pw_ = blk.predInfo[0], ph_ = blk.predInfo[1];
    uint32_t px = (uint32_t)(u * (float)pw_), py = (uint32_t)(v * (float)ph_);
    if (px >= pw_) px = pw_ - 1u; if (py >= ph_) py = ph_ - 1u;
    const float d = pred(layer, px, py);
    if (!(d == d) || d <= 0.f || d >= 1.f) return false;
    // unproject the rendered sample at the texel centre it was fetched from
    const float nx = ((float)px + 0.5f) / (float)pw_ * 2.f - 1.f;
    float ny = ((float)py + 0.5f) / (float)ph_ * 2.f - 1.f; if (flags & FS_MEAS_FLAG_PRED_FLIP_Y) ny = -ny;
    const float hw = IVP.m[3] * nx + IVP.m[7] * ny + IVP.m[11] * d + IVP.m[15];
    if (fabsf(hw) < 1e-12f) return false;
    const Vec3 wp = V3((IVP.m[0] * nx + IVP.m[4] * ny + IVP.m[8] * d + IVP.m[12]) / hw,
                       (IVP.m[1] * nx + IVP.m[5] * ny + IVP.m[9] * d + IVP.m[13]) / hw,
                       (IVP.m[2] * nx + IVP.m[6] * ny + IVP.m[10] * d + IVP.m[14]) / hw);
    const Vec3 eye = V3(W.m[12], W.m[13], W.m[14]);
    const float dm = Length(Sub(pw, eye)), dp = Length(Sub(wp, eye));
    residualSigma = fabsf(dm - dp) / SigmaN(z);
    return true;
}
// Central-difference normal from the four neighbour points (eye space), oriented towards the camera (origin).
inline Vec3 NormalFromNeighbours(Vec3 p, Vec3 pxm, Vec3 pxp, Vec3 pym, Vec3 pyp) {
    Vec3 n = Normalize(Cross(Sub(pxp, pxm), Sub(pyp, pym)));
    if (Dot(n, p) > 0.f) n = V3(-n.x, -n.y, -n.z);
    return n;
}
// Workgroup reservation (twin of thread 0 in the kernel): one atomic per group; records beyond the cap
// are refused and counted. `reserved` may exceed `cap`; the stored count is StoredCount().
inline uint32_t Overflow(uint32_t base, uint32_t n, uint32_t cap) { uint32_t end = base + n; if (end <= cap) return 0u; return base >= cap ? n : end - cap; }
inline uint32_t ReserveGroup(uint32_t& reserved, uint32_t n, uint32_t cap, uint32_t& overflow) {
    uint32_t base = reserved; reserved += n; overflow += Overflow(base, n, cap); return base;
}
inline uint32_t StoredCount(uint32_t reserved, uint32_t cap) { return reserved < cap ? reserved : cap; }

// ---- per-texel reference: exactly the kernels' per-thread bodies -------------------------------------------------
enum TexelResult { TEXEL_INVALID = 0, TEXEL_EDGE = 1, TEXEL_WRITTEN = 3 };
// Shared classification (score kernel and emit kernel run the identical gates). `depth(x, y)` = raw texel of `layer`.
struct TexelGeometry { float z, zxm, zxp, zym, zyp; bool edge, flat, steep; };
template <class DepthFn>
inline TexelResult ClassifyTexel(const PushCompact& pc, uint32_t x, uint32_t y, DepthFn depth, TexelGeometry& g) {
    const uint32_t w = pc.width, h = pc.height;
    g.edge = g.flat = g.steep = false;
    if (x >= w || y >= h) return TEXEL_INVALID;
    if (x == 0u || y == 0u || x + 1u >= w || y + 1u >= h) return TEXEL_INVALID;      // no central differences at the border
    g.z = LinearizeDepth(depth(x, y), pc.nearZ, pc.farZ, pc.flags);
    if (!DepthUsable(g.z, pc.minDepthM, pc.maxDepthM)) return TEXEL_INVALID;
    g.zxm = LinearizeDepth(depth(x - 1u, y), pc.nearZ, pc.farZ, pc.flags);
    g.zxp = LinearizeDepth(depth(x + 1u, y), pc.nearZ, pc.farZ, pc.flags);
    g.zym = LinearizeDepth(depth(x, y - 1u), pc.nearZ, pc.farZ, pc.flags);
    g.zyp = LinearizeDepth(depth(x, y + 1u), pc.nearZ, pc.farZ, pc.flags);
    if (!DepthUsable(g.zxm, pc.minDepthM, pc.maxDepthM) || !DepthUsable(g.zxp, pc.minDepthM, pc.maxDepthM) ||
        !DepthUsable(g.zym, pc.minDepthM, pc.maxDepthM) || !DepthUsable(g.zyp, pc.minDepthM, pc.maxDepthM)) return TEXEL_INVALID;   // hole / too close next to us
    g.edge = IsEdge(g.z, g.zxm, g.zxp, g.zym, g.zyp);
    if (g.edge && !(pc.flags & FS_MEAS_FLAG_DETAIL)) return TEXEL_EDGE;
    g.flat = IsFlat(g.z, g.zxm, g.zxp, g.zym, g.zyp);
    g.steep = IsSteep(g.z, g.zxm, g.zxp, g.zym, g.zyp);
    return TEXEL_WRITTEN;
}
// Score kernel body: 0 for invalid / edge texels, else the information score. Counters: predicted / consistent / new.
template <class DepthFn, class PredFn>
inline uint32_t ScoreTexel(const PushCompact& pc, const FrameBlock& blk, uint32_t layer, uint32_t x, uint32_t y, DepthFn depth, PredFn pred, bool& predicted, bool& consistent) {
    TexelGeometry g; predicted = consistent = false;
    if (ClassifyTexel(pc, x, y, depth, g) != TEXEL_WRITTEN) return 0u;
    float tx, ty; RayTangents(x, y, pc.width, pc.height, blk.fov[layer & 1u], pc.flags, tx, ty);
    float r = 0.f;
    predicted = PredictResidual(blk, layer & 1u, EyePoint(tx, ty, g.z), g.z, pc.flags, pred, r);
    consistent = predicted && r <= 1.f;
    return ScoreOf(predicted, r, g.flat, g.steep, (pc.flags & FS_MEAS_FLAG_DETAIL) != 0u);
}
// Emit kernel body: the full back-projection of a selected texel.
template <class DepthFn>
inline TexelResult BackprojectTexel(const PushCompact& pc, const FrameBlock& blk, uint32_t layer, uint32_t x, uint32_t y, DepthFn depth, FsSurfaceMeasurement& out, bool& edge) {
    TexelGeometry g; const TexelResult r = ClassifyTexel(pc, x, y, depth, g); edge = g.edge;
    if (r != TEXEL_WRITTEN) return r;
    const uint32_t w = pc.width, h = pc.height; const float* fov = blk.fov[layer & 1u];
    float tx, ty; RayTangents(x, y, w, h, fov, pc.flags, tx, ty);
    float txm, tym, txp, typ, t2;
    RayTangents(x - 1u, y, w, h, fov, pc.flags, txm, t2);
    RayTangents(x + 1u, y, w, h, fov, pc.flags, txp, t2);
    RayTangents(x, y - 1u, w, h, fov, pc.flags, t2, tym);
    RayTangents(x, y + 1u, w, h, fov, pc.flags, t2, typ);
    const Vec3 p = EyePoint(tx, ty, g.z);
    const Vec3 n = NormalFromNeighbours(p, EyePoint(txm, ty, g.zxm), EyePoint(txp, ty, g.zxp), EyePoint(tx, tym, g.zym), EyePoint(tx, typ, g.zyp));
    Mat4 M; memcpy(M.m, blk.anchorFromEye[layer & 1u], sizeof M.m);
    const Vec3 pa = MulPoint(M, p);
    const Vec3 na = Normalize(MulDir(M, n));
    out.px = pa.x; out.py = pa.y; out.pz = pa.z;
    out.nx = na.x; out.ny = na.y; out.nz = na.z;
    out.sigmaN = SigmaN(g.z);
    out.footprint = Footprint(g.z, fov, w, h);
    out.sigmaT = out.footprint;
    out.sourceFlags = FS_MEAS_SRC_DEPTH_PRIOR | (edge ? FS_MEAS_SRC_EDGE : 0u) | (g.flat ? FS_MEAS_SRC_LOW_TEXTURE : 0u) | (layer << FS_MEAS_SRC_EYE_SHIFT);
    out.observationId = pc.obsId;
    out.reserved = 0u;
    return TEXEL_WRITTEN;
}

// ---- whole-job reference (score -> select -> count -> prefix -> emit): deterministic record order -------------------
// `depth(layer, x, y)` = raw Environment Depth texel; `pred(layer, px, py)` = raw rendered depth. Counters accumulate
// into blk.ctr exactly like the kernels; `hist` is the select scratch. Records land at wgBase[g] + lane rank.
template <class DepthFn, class PredFn>
inline uint32_t CompactDispatchReference(const PushCompact& pc, FrameBlock& blk, DepthFn depth, PredFn pred, FsSurfaceMeasurement* records, uint32_t* scoresOut = nullptr) {
    uint32_t* ctr = blk.ctr;
    const uint32_t threads = pc.width * pc.height * pc.layers;
    const uint32_t groups = (threads + FS_MEAS_WG - 1u) / FS_MEAS_WG;
    std::vector<uint32_t> score(threads, 0u);
    uint32_t hist[FS_MEAS_SCORE_BINS] = {};
    for (uint32_t t = 0; t < threads; ++t) {                                                   // score
        uint32_t layer, x, y; ThreadToTexel(t, pc.layers, pc.width, pc.height, pc.frame, layer, x, y);
        bool predicted = false, consistent = false; TexelGeometry g;
        const TexelResult cls = ClassifyTexel(pc, x, y, [&](uint32_t xx, uint32_t yy) { return depth(layer, xx, yy); }, g);
        if (g.edge) ctr[FS_MEAS_CTR_EDGE]++;
        if (cls == TEXEL_INVALID) { ctr[FS_MEAS_CTR_INVALID]++; continue; }
        if (cls == TEXEL_EDGE) continue;
        const uint32_t s = ScoreTexel(pc, blk, layer, x, y, [&](uint32_t xx, uint32_t yy) { return depth(layer, xx, yy); }, pred, predicted, consistent);
        score[t] = s; hist[s]++; ctr[FS_MEAS_CTR_VALID]++;
        if (predicted) { ctr[FS_MEAS_CTR_PREDICTED]++; if (consistent) ctr[FS_MEAS_CTR_CONSISTENT]++; } else ctr[FS_MEAS_CTR_NEW]++;
    }
    uint32_t T = 1, f16 = 65536; SelectThreshold(hist, pc.budget, T, f16);                    // select
    ctr[FS_MEAS_CTR_THRESHOLD] = T; ctr[FS_MEAS_CTR_FRACTION] = f16;
    std::vector<uint32_t> wgCount(groups, 0u), wgBase(groups, 0u);
    for (uint32_t g = 0; g < groups; ++g) {                                                    // count
        for (uint32_t lane = 0; lane < FS_MEAS_WG; ++lane) { const uint32_t t = g * FS_MEAS_WG + lane; if (t < threads && Selected(score[t], t, pc.frame, T, f16)) wgCount[g]++; }
        ctr[FS_MEAS_CTR_GROUPS]++;
    }
    uint32_t total = 0; for (uint32_t g = 0; g < groups; ++g) { wgBase[g] = total; total += wgCount[g]; }   // prefix
    ctr[FS_MEAS_CTR_RESERVED] = total < pc.maxOut ? total : pc.maxOut;
    if (total > pc.maxOut) ctr[FS_MEAS_CTR_OVERFLOW] += total - pc.maxOut;
    ctr[FS_MEAS_CTR_REJECTED] = ctr[FS_MEAS_CTR_VALID] - total;
    for (uint32_t g = 0; g < groups; ++g) {                                                    // emit
        uint32_t rank = 0;
        for (uint32_t lane = 0; lane < FS_MEAS_WG; ++lane) {
            const uint32_t t = g * FS_MEAS_WG + lane;
            if (t >= threads || !Selected(score[t], t, pc.frame, T, f16)) continue;
            uint32_t layer, x, y; ThreadToTexel(t, pc.layers, pc.width, pc.height, pc.frame, layer, x, y);
            FsSurfaceMeasurement m; bool edge = false;
            if (BackprojectTexel(pc, blk, layer, x, y, [&](uint32_t xx, uint32_t yy) { return depth(layer, xx, yy); }, m, edge) != TEXEL_WRITTEN) continue;
            const uint32_t idx = wgBase[g] + rank++;
            if (idx < pc.maxOut) { records[idx] = m; if (m.sourceFlags & FS_MEAS_SRC_LOW_TEXTURE) ctr[FS_MEAS_CTR_LOWTEX]++; }
        }
    }
    if (scoresOut) memcpy(scoresOut, score.data(), threads * sizeof(uint32_t));
    return ctr[FS_MEAS_CTR_RESERVED];
}

} // namespace meas
} // namespace fs
