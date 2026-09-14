// Pure measurement math (no Vulkan): Environment Depth texel -> anchor-local FsSurfaceMeasurement.
// Byte-for-byte twin of Native~/shaders/measure/fs_meas_common.glsl and of the per-thread body of
// measure_depth_backproject.comp; Native~/tests/host_tests_measure.cpp pins both sides' behaviour.
// Conventions (assumed from the C# feeder, Runtime/Host/EnvDepthFeeder.cs, and Unity camera space):
//   * pose = world-from-eye TRS (column-major, m[col*4+row]); eye space is Unity's (+X right, +Y up, +Z forward);
//   * fov = [tan left, tan right, tan up, tan down] with provider signs (left/down negative);
//   * texel value = projected depth in [0,1] (OpenGL-style, XR_META_environment_depth near/far), or metres
//     when FS_MEAS_FLAG_LINEAR_DEPTH is set; farZ <= 0 or infinite = unbounded far plane;
//   * row 0 = top (tan up) edge unless FS_MEAS_FLAG_FLIP_Y.
// Contract §6 (depth = prior), §7.4 (uncertainty), §7.6 (compaction placeholder: decimation + hard cap).
#pragma once
#include <stdint.h>
#include <math.h>
#include <string.h>
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

// ---- push constants / frame block: byte-identical to measure_depth_backproject.comp ------------------------------
struct PushBackproject {                 // 48 B (contract §15.3: <= 128 B)
    float    nearZ, farZ;                // farZ <= 0 = unbounded
    float    maxDepthM, pad0;
    uint32_t layers, decimK, maxOut, flags;
    uint32_t obsId, frame, width, height;
};
static_assert(sizeof(PushBackproject) == 48, "push block layout");
// Per ring slot, host-visible (binding FS_MEAS_B_COUNTERS): the CPU resets ctr[] and writes the per-eye
// parameters before submit; the kernel atomically bumps ctr[] (std430 twin: uint[16], mat4[2], vec4[2]).
struct FrameBlock {
    uint32_t ctr[FS_MEAS_CTR_WORDS];
    float    anchorFromEye[2][16];       // column-major: anchor-local <- depth eye pose (at the depth's own XrTime)
    float    fov[2][4];                  // tan left, right, up, down per eye
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
// Contract §7.6 placeholder: keep texels on a frame-shifted lattice; k <= 1 keeps everything.
inline bool KeepDecimated(uint32_t x, uint32_t y, uint32_t frame, uint32_t k) { return k <= 1u || ((x + y + frame) % k) == 0u; }
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

// ---- per-texel reference: exactly the kernel's per-thread body -------------------------------------------------
enum TexelResult { TEXEL_INVALID = 0, TEXEL_EDGE = 1, TEXEL_DECIMATED = 2, TEXEL_WRITTEN = 3 };
// `depth(x, y)` returns the raw texel of `layer`. `edge` reports the discontinuity flag for
// every result that got that far (TEXEL_EDGE, or TEXEL_WRITTEN in DETAIL mode).
template <class DepthFn>
inline TexelResult BackprojectTexel(const PushBackproject& pc, const FrameBlock& blk, uint32_t layer, uint32_t x, uint32_t y, DepthFn depth, FsSurfaceMeasurement& out, bool& edge) {
    edge = false;
    const uint32_t w = pc.width, h = pc.height;
    const float* fov = blk.fov[layer & 1u];
    if (x >= w || y >= h) return TEXEL_INVALID;
    if (x == 0u || y == 0u || x + 1u >= w || y + 1u >= h) return TEXEL_INVALID;      // no central differences at the border
    const float z = LinearizeDepth(depth(x, y), pc.nearZ, pc.farZ, pc.flags);
    if (!DepthUsable(z, pc.nearZ, pc.maxDepthM)) return TEXEL_INVALID;
    const float zxm = LinearizeDepth(depth(x - 1u, y), pc.nearZ, pc.farZ, pc.flags);
    const float zxp = LinearizeDepth(depth(x + 1u, y), pc.nearZ, pc.farZ, pc.flags);
    const float zym = LinearizeDepth(depth(x, y - 1u), pc.nearZ, pc.farZ, pc.flags);
    const float zyp = LinearizeDepth(depth(x, y + 1u), pc.nearZ, pc.farZ, pc.flags);
    if (!DepthUsable(zxm, pc.nearZ, pc.maxDepthM) || !DepthUsable(zxp, pc.nearZ, pc.maxDepthM) ||
        !DepthUsable(zym, pc.nearZ, pc.maxDepthM) || !DepthUsable(zyp, pc.nearZ, pc.maxDepthM)) return TEXEL_INVALID;   // hole next to us
    edge = IsEdge(z, zxm, zxp, zym, zyp);
    if (edge && !(pc.flags & FS_MEAS_FLAG_DETAIL)) return TEXEL_EDGE;
    if (!(pc.flags & FS_MEAS_FLAG_NO_DECIMATION) && !KeepDecimated(x, y, pc.frame, pc.decimK)) return TEXEL_DECIMATED;
    float tx, ty; RayTangents(x, y, w, h, fov, pc.flags, tx, ty);
    float txm, tym, txp, typ, t2;
    RayTangents(x - 1u, y, w, h, fov, pc.flags, txm, t2);
    RayTangents(x + 1u, y, w, h, fov, pc.flags, txp, t2);
    RayTangents(x, y - 1u, w, h, fov, pc.flags, t2, tym);
    RayTangents(x, y + 1u, w, h, fov, pc.flags, t2, typ);
    const Vec3 p = EyePoint(tx, ty, z);
    const Vec3 n = NormalFromNeighbours(p, EyePoint(txm, ty, zxm), EyePoint(txp, ty, zxp), EyePoint(tx, tym, zym), EyePoint(tx, typ, zyp));
    Mat4 M; memcpy(M.m, blk.anchorFromEye[layer & 1u], sizeof M.m);
    const Vec3 pa = MulPoint(M, p);
    const Vec3 na = Normalize(MulDir(M, n));
    const bool flat = IsFlat(z, zxm, zxp, zym, zyp);
    out.px = pa.x; out.py = pa.y; out.pz = pa.z;
    out.nx = na.x; out.ny = na.y; out.nz = na.z;
    out.sigmaN = SigmaN(z);
    out.footprint = Footprint(z, fov, w, h);
    out.sigmaT = out.footprint;
    out.sourceFlags = FS_MEAS_SRC_DEPTH_PRIOR | (edge ? FS_MEAS_SRC_EDGE : 0u) | (flat ? FS_MEAS_SRC_LOW_TEXTURE : 0u);
    out.observationId = pc.obsId;
    out.reserved = 0u;
    return TEXEL_WRITTEN;
}

// ---- whole-dispatch reference: workgroups of FS_MEAS_WG consecutive threads, one reservation per group ---------
// `depth(layer, x, y)` returns the raw texel. Record order inside a group is lane order; the GPU's group order
// differs, so tests compare counts and sets. Counters accumulate into blk.ctr like the kernel does.
template <class DepthFn>
inline void BackprojectDispatchReference(const PushBackproject& pc, FrameBlock& blk, DepthFn depth, FsSurfaceMeasurement* records) {
    uint32_t* ctr = blk.ctr;
    const uint32_t threads = pc.width * pc.height * pc.layers;
    const uint32_t groups = (threads + FS_MEAS_WG - 1u) / FS_MEAS_WG;
    FsSurfaceMeasurement local[FS_MEAS_WG];
    for (uint32_t g = 0; g < groups; ++g) {
        uint32_t n = 0, edges = 0, invalid = 0, decimated = 0, lowtex = 0;
        for (uint32_t lane = 0; lane < FS_MEAS_WG; ++lane) {
            const uint32_t t = g * FS_MEAS_WG + lane;
            if (t >= threads) continue;
            uint32_t layer, x, y; ThreadToTexel(t, pc.layers, pc.width, pc.height, pc.frame, layer, x, y);
            bool edge = false;
            const TexelResult r = BackprojectTexel(pc, blk, layer, x, y, [&](uint32_t xx, uint32_t yy) { return depth(layer, xx, yy); }, local[n], edge);
            if (edge) ++edges;
            if (r == TEXEL_WRITTEN) { if (local[n].sourceFlags & FS_MEAS_SRC_LOW_TEXTURE) ++lowtex; ++n; }
            else if (r == TEXEL_INVALID) ++invalid;
            else if (r == TEXEL_DECIMATED) ++decimated;
        }
        uint32_t base = 0;
        if (n) { base = ReserveGroup(ctr[FS_MEAS_CTR_RESERVED], n, pc.maxOut, ctr[FS_MEAS_CTR_OVERFLOW]); ctr[FS_MEAS_CTR_VALID] += n; }
        ctr[FS_MEAS_CTR_EDGE] += edges; ctr[FS_MEAS_CTR_INVALID] += invalid; ctr[FS_MEAS_CTR_DECIMATED] += decimated; ctr[FS_MEAS_CTR_LOWTEX] += lowtex;
        ctr[FS_MEAS_CTR_GROUPS] += 1u;
        for (uint32_t i = 0; i < n; ++i) { const uint32_t idx = base + i; if (idx < pc.maxOut) records[idx] = local[i]; }
    }
}

} // namespace meas
} // namespace fs
