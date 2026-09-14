// Measurement GLSL common: byte-identical twins of Native~/src/host/measure/fs_meas_math.h (depth
// linearisation, ray tangents, edge / flat tests, sigma model, footprint, decimation, group reservation).
// Requires GL_GOOGLE_include_directive (glslangValidator -I). Conventions documented in fs_meas_math.h.
#ifndef FS_MEAS_COMMON_GLSL
#define FS_MEAS_COMMON_GLSL
#include "../../src/host/measure/fs_meas_params.h"

// Projected depth -> metres along the eye z axis; -1 = no data.
float fsMeasLinearizeDepth(float d, float nearZ, float farZ, uint flags) {
    if (isnan(d)) return -1.0;
    if ((flags & FS_MEAS_FLAG_LINEAR_DEPTH) != 0u) return d;
    if (d <= 0.0 || d >= 1.0) return -1.0;
    if (farZ > 0.0 && farZ < 3.0e38) {
        float ndc = d * 2.0 - 1.0;
        float den = (farZ + nearZ) - ndc * (farZ - nearZ);
        return den > 1e-12 ? 2.0 * farZ * nearZ / den : -1.0;
    }
    return nearZ / (1.0 - d);
}
bool fsMeasDepthUsable(float z, float nearZ, float maxDepthM) { return !isnan(z) && z > nearZ && z <= maxDepthM; }
// Ray tangents of texel (x, y); fov = (tanL, tanR, tanU, tanD); row 0 = top unless FLIP_Y.
vec2 fsMeasRayTangents(uint x, uint y, uint w, uint h, vec4 fov, uint flags) {
    float u = (float(x) + 0.5) / float(w), v = (float(y) + 0.5) / float(h);
    float tx = fov.x + (fov.y - fov.x) * u;
    float ty = (flags & FS_MEAS_FLAG_FLIP_Y) != 0u ? fov.w + (fov.z - fov.w) * v : fov.z + (fov.w - fov.z) * v;
    return vec2(tx, ty);
}
vec3 fsMeasEyePoint(vec2 t, float z) { return vec3(t.x * z, t.y * z, z); }
bool fsMeasIsEdge(float d, float dxm, float dxp, float dym, float dyp) {
    float t = float(FS_MEAS_EDGE_RATIO) * d;
    return abs(dxm - d) > t || abs(dxp - d) > t || abs(dym - d) > t || abs(dyp - d) > t;
}
bool fsMeasIsFlat(float d, float dxm, float dxp, float dym, float dyp) {
    return abs(dxm + dxp + dym + dyp - 4.0 * d) < float(FS_MEAS_FLAT_LAPLACIAN_RATIO) * d;
}
float fsMeasSigmaN(float d) { return float(FS_MEAS_SIGMA_QUAD) * d * d + float(FS_MEAS_SIGMA_BASE_M); }
float fsMeasFootprint(float d, vec4 fov, uint w, uint h) {
    float ax = (fov.y - fov.x) / float(w), ay = (fov.z - fov.w) / float(h);
    return d * max(ax, ay);
}
bool fsMeasKeepDecimated(uint x, uint y, uint frame, uint k) { return k <= 1u || ((x + y + frame) % k) == 0u; }
// Central-difference normal oriented towards the camera (eye origin).
vec3 fsMeasNormal(vec3 p, vec3 pxm, vec3 pxp, vec3 pym, vec3 pyp) {
    vec3 c = cross(pxp - pxm, pyp - pym);
    float l = length(c);
    vec3 n = l < 1e-20 ? vec3(0.0) : c / l;
    return dot(n, p) > 0.0 ? -n : n;
}
// Records of a group reservation [base, base+n) that fall beyond the cap.
uint fsMeasOverflow(uint base, uint n, uint cap) { uint end = base + n; if (end <= cap) return 0u; return base >= cap ? n : end - cap; }

#endif // FS_MEAS_COMMON_GLSL
