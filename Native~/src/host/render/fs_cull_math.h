// Pure cull math (contract §13.4/§13.5): column-major 4x4 helpers, frustum planes, AABB tests,
// foveated screen-error threshold and GPU-headroom bias. Byte-for-byte twins of the GLSL in
// Native~/shaders/render/render_cull_nodes.comp; the host tests pin these. No Vulkan.
#pragma once
#include <stdint.h>
#include <math.h>
#include <string.h>
#include "../world/fs_world_params.h"

namespace fs {
namespace render {

struct Mat4 { float m[16]; };            // column-major: m[col*4 + row]
struct Vec4 { float x, y, z, w; };
struct Plane { float a, b, c, d; };      // a*x + b*y + c*z + d >= 0 inside

inline Mat4 Identity() { Mat4 r{}; r.m[0] = r.m[5] = r.m[10] = r.m[15] = 1.f; return r; }
inline Mat4 Mul(const Mat4& A, const Mat4& B) {          // A * B
    Mat4 r{};
    for (int c = 0; c < 4; ++c) for (int row = 0; row < 4; ++row) {
        float s = 0.f; for (int k = 0; k < 4; ++k) s += A.m[k * 4 + row] * B.m[c * 4 + k];
        r.m[c * 4 + row] = s;
    }
    return r;
}
inline Vec4 MulV(const Mat4& A, Vec4 v) {
    Vec4 r; r.x = A.m[0] * v.x + A.m[4] * v.y + A.m[8] * v.z + A.m[12] * v.w;
    r.y = A.m[1] * v.x + A.m[5] * v.y + A.m[9] * v.z + A.m[13] * v.w;
    r.z = A.m[2] * v.x + A.m[6] * v.y + A.m[10] * v.z + A.m[14] * v.w;
    r.w = A.m[3] * v.x + A.m[7] * v.y + A.m[11] * v.z + A.m[15] * v.w;
    return r;
}
// General 4x4 inverse (cofactor); returns false when singular.
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
// Frustum planes from a view-projection matrix (Gribb/Hartmann), normalised, inward-facing. Works for
// both GL-style (z in [-w,w]) and Vulkan/reversed-Z (z in [0,w]) projections: the near/far pair comes
// out as {z >= -w or z >= 0, z <= w}; with reversed Z both remain valid half-spaces (order irrelevant).
inline void FrustumPlanes(const Mat4& vp, Plane out[6], bool zeroToOne) {
    const float* m = vp.m;
    auto row = [&](int r, float* o) { o[0] = m[0 * 4 + r]; o[1] = m[1 * 4 + r]; o[2] = m[2 * 4 + r]; o[3] = m[3 * 4 + r]; };
    float r0[4], r1[4], r2[4], r3[4]; row(0, r0); row(1, r1); row(2, r2); row(3, r3);
    float p[6][4];
    for (int i = 0; i < 4; ++i) { p[0][i] = r3[i] + r0[i]; p[1][i] = r3[i] - r0[i]; p[2][i] = r3[i] + r1[i]; p[3][i] = r3[i] - r1[i];
                                  p[4][i] = zeroToOne ? r2[i] : r3[i] + r2[i]; p[5][i] = r3[i] - r2[i]; }
    for (int k = 0; k < 6; ++k) {
        float l = sqrtf(p[k][0] * p[k][0] + p[k][1] * p[k][1] + p[k][2] * p[k][2]); if (l < 1e-20f) l = 1.f;
        out[k].a = p[k][0] / l; out[k].b = p[k][1] / l; out[k].c = p[k][2] / l; out[k].d = p[k][3] / l;
    }
}
// AABB vs frustum: false only when fully outside one plane (conservative). `marginM` widens the box.
inline bool AabbInFrustum(const Plane* planes, int planeCount, const float bmin[3], const float bmax[3], float marginM) {
    for (int k = 0; k < planeCount; ++k) {
        const Plane& p = planes[k];
        float px = p.a >= 0.f ? bmax[0] + marginM : bmin[0] - marginM;
        float py = p.b >= 0.f ? bmax[1] + marginM : bmin[1] - marginM;
        float pz = p.c >= 0.f ? bmax[2] + marginM : bmin[2] - marginM;
        if (p.a * px + p.b * py + p.c * pz + p.d < 0.f) return false;
    }
    return true;
}
// Node visible if inside either eye frustum (one traversal for both eyes, §13.5.5).
inline bool AabbInEitherEye(const Plane planesL[6], const Plane planesR[6], const float bmin[3], const float bmax[3], float marginM) {
    return AabbInFrustum(planesL, 6, bmin, bmax, marginM) || AabbInFrustum(planesR, 6, bmin, bmax, marginM);
}
// World AABB of a page-local AABB under an affine transform (8 corners).
inline void TransformAabb(const Mat4& M, const float bmin[3], const float bmax[3], float wmin[3], float wmax[3]) {
    wmin[0] = wmin[1] = wmin[2] = 1e30f; wmax[0] = wmax[1] = wmax[2] = -1e30f;
    for (int i = 0; i < 8; ++i) {
        Vec4 c{ (i & 1) ? bmax[0] : bmin[0], (i & 2) ? bmax[1] : bmin[1], (i & 4) ? bmax[2] : bmin[2], 1.f };
        Vec4 w = MulV(M, c);
        wmin[0] = fminf(wmin[0], w.x); wmin[1] = fminf(wmin[1], w.y); wmin[2] = fminf(wmin[2], w.z);
        wmax[0] = fmaxf(wmax[0], w.x); wmax[1] = fmaxf(wmax[1], w.y); wmax[2] = fmaxf(wmax[2], w.z);
    }
}
// GPU headroom bias (§13.5.4): negative headroom raises the error threshold, clamped x FS_HEADROOM_BIAS_MAX.
inline float HeadroomBias(int32_t headroomUs) {
    if (headroomUs >= 0) return 1.f;
    float b = 1.f + (float)(-headroomUs) * (float)FS_HEADROOM_BIAS_PER_US;
    return b > (float)FS_HEADROOM_BIAS_MAX ? (float)FS_HEADROOM_BIAS_MAX : b;
}
// Foveated threshold (§13.5.2): fovealPx at <= FS_FOVEA_DEG, peripheralPx at >= FS_PERIPHERY_DEG, smoothstep between.
inline float FoveatedThresholdPx(float fovealPx, float peripheralPx, float eccentricityDeg, float bias) {
    float t = (eccentricityDeg - (float)FS_FOVEA_DEG) / ((float)FS_PERIPHERY_DEG - (float)FS_FOVEA_DEG);
    t = t < 0.f ? 0.f : (t > 1.f ? 1.f : t); t = t * t * (3.f - 2.f * t);
    return (fovealPx + (peripheralPx - fovealPx) * t) * bias;
}
// Projected size of a world-space diameter at view depth, in eye-buffer pixels.
inline float ProjectedPx(float diameterM, float depthM, float focalPx) { float d = depthM < 0.05f ? 0.05f : depthM; return diameterM * focalPx / d; }
// Eccentricity (degrees) of a direction relative to the view forward axis.
inline float EccentricityDeg(const float fwd[3], const float dir[3]) {
    float l = sqrtf(dir[0] * dir[0] + dir[1] * dir[1] + dir[2] * dir[2]); if (l < 1e-9f) return 0.f;
    float d = (fwd[0] * dir[0] + fwd[1] * dir[1] + fwd[2] * dir[2]) / l; d = d < -1.f ? -1.f : (d > 1.f ? 1.f : d);
    return acosf(d) * 57.29577951f;
}
// Focal length in pixels from a projection matrix and eye buffer width: proj[0][0] = 2n/(r-l) = 2*focal/width.
inline float FocalPxFromProj(const Mat4& proj, float eyeWidthPx) { return fabsf(proj.m[0]) * 0.5f * eyeWidthPx; }

} // namespace render
} // namespace fs
