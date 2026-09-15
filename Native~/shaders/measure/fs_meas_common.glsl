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
    float t = float(FS_MEAS_EDGE_RATIO) * d, c = float(FS_MEAS_EDGE_CURV_RATIO) * d;
    return abs(dxm - d) > t || abs(dxp - d) > t || abs(dym - d) > t || abs(dyp - d) > t
        || abs(dxp + dxm - 2.0 * d) > c || abs(dyp + dym - 2.0 * d) > c;
}
bool fsMeasIsFlat(float d, float dxm, float dxp, float dym, float dyp) {
    return abs(dxm + dxp + dym + dyp - 4.0 * d) < float(FS_MEAS_FLAT_LAPLACIAN_RATIO) * d;
}
float fsMeasSigmaN(float d) { return float(FS_MEAS_SIGMA_QUAD) * d * d + float(FS_MEAS_SIGMA_BASE_M); }
float fsMeasFootprint(float d, vec4 fov, uint w, uint h) {
    float ax = (fov.y - fov.x) / float(w), ay = (fov.z - fov.w) / float(h);
    return d * max(ax, ay);
}
// ---- information score (contract §7.6, C09R-E2; twins: IsSteep / ScoreOf / HashTexel / Selected) ----------------
bool fsMeasIsSteep(float d, float dxm, float dxp, float dym, float dyp) {
    float t = float(FS_MEAS_STEEP_RATIO) * d;
    return abs(dxm - d) > t || abs(dxp - d) > t || abs(dym - d) > t || abs(dyp - d) > t;
}
uint fsMeasScore(bool predicted, float residualSigma, bool isFlat, bool steep, bool detail) {
    uint s;
    if (!predicted) s = uint(FS_MEAS_SCORE_NEW);
    else if (residualSigma > 3.0) { uint e = uint(residualSigma * 4.0); s = uint(FS_MEAS_SCORE_FAR_BASE) + min(e, 63u); }
    else if (residualSigma > 1.0) s = uint(FS_MEAS_SCORE_BAND_BASE) + uint((residualSigma - 1.0) * 48.0);
    else s = uint(FS_MEAS_SCORE_CONVERGED_BASE) + uint(residualSigma * 32.0);
    if (!isFlat) s += uint(FS_MEAS_SCORE_CURVATURE);
    if (steep) s += uint(FS_MEAS_SCORE_STEEP);
    if (detail && s < uint(FS_MEAS_SCORE_DETAIL_MIN)) s = uint(FS_MEAS_SCORE_DETAIL_MIN);
    return clamp(s, 1u, 255u);
}
uint fsMeasHash(uint t, uint frame) {
    uint h = t * 2654435761u ^ (frame * 0x9E3779B9u);
    h = (h ^ 61u) ^ (h >> 16); h *= 9u; h ^= h >> 4; h *= 0x27d4eb2du; h ^= h >> 15;
    return h;
}
bool fsMeasSelected(uint score, uint t, uint frame, uint threshold, uint fraction16) {
    if (score == 0u || score < threshold) return false;
    if (score > threshold) return true;
    return (fsMeasHash(t, frame) >> 16) < fraction16;
}
// Shared thread -> (layer, texel) mapping (twin: ThreadToTexel).
void fsMeasThreadToTexel(uint t, uint layers, uint w, uint h, uint frame, out uint layer, out uint x, out uint y) {
    layer = t % layers;
    uint texel = (t / layers + ((frame * uint(FS_MEAS_ROW_PHASE_STRIDE)) % h) * w) % (w * h);
    x = texel % w; y = texel / w;
}
// Central-difference normal oriented towards the camera (eye origin).
// E9 edge-safe normal (twin: NormalFromNeighbours): per axis the central difference on a smooth ramp, the one-sided difference
// towards the smaller depth step when the second difference exceeds FS_MEAS_NORMAL_ONESIDED_RATIO * z (eye z = depth).
vec3 fsMeasAxisTangent(vec3 p, vec3 pm, vec3 pp) {
    if (abs(pp.z + pm.z - 2.0 * p.z) <= float(FS_MEAS_NORMAL_ONESIDED_RATIO) * p.z) return pp - pm;
    return abs(pp.z - p.z) <= abs(p.z - pm.z) ? pp - p : p - pm;
}
vec3 fsMeasNormal(vec3 p, vec3 pxm, vec3 pxp, vec3 pym, vec3 pyp) {
    vec3 c = cross(fsMeasAxisTangent(p, pxm, pxp), fsMeasAxisTangent(p, pym, pyp));
    float l = length(c);
    vec3 n = l < 1e-20 ? vec3(0.0) : c / l;
    return dot(n, p) > 0.0 ? -n : n;
}
// Records of a group reservation [base, base+n) that fall beyond the cap.
uint fsMeasOverflow(uint base, uint n, uint cap) { uint end = base + n; if (end <= cap) return 0u; return base >= cap ? n : end - cap; }

#endif // FS_MEAS_COMMON_GLSL
