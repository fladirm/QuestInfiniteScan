// Frame block + shared per-texel classification of the C09R-E2 compaction kernels (twin: fs::meas::FrameBlock,
// ClassifyTexel). Requires FS_MEAS_USE_DEPTH (sampler) where classification runs.
#ifndef FS_MEAS_FRAME_GLSL
#define FS_MEAS_FRAME_GLSL
layout(std430, set = 0, binding = FS_MEAS_B_COUNTERS) buffer FrameBlock {
    uint ctr[FS_MEAS_CTR_WORDS]; mat4 anchorFromEye[2]; vec4 fov[2]; mat4 worldFromEye[2]; mat4 predViewProj[2]; mat4 predInvViewProj[2]; uvec4 predInfo;
    mat4 camFromWorld[2]; vec4 camIntrinsics[2]; uvec4 camInfo; mat4 anchorFromWorld; uvec4 stereoInfo; mat4 keyCamFromWorld; vec4 keyIntrinsics; uvec4 keyInfo;
} blk;
// Twin: fs::meas::PushCompact (48 B). zp = (nearZ, farZ, maxDepthM, minDepthM); a = (layers, budget, maxOut, flags); b = (obsId, frame, width, height).
layout(push_constant) uniform Push { vec4 zp; uvec4 a; uvec4 b; } pc;
#ifdef FS_MEAS_USE_DEPTH
layout(set = 0, binding = FS_MEAS_B_DEPTH) uniform sampler2DArray depthTex;
float fsFetch(uint layer, uint x, uint y) { return texelFetch(depthTex, ivec3(int(x), int(y), int(layer)), 0).r; }
struct FsTexelGeometry { float z, zxm, zxp, zym, zyp; bool edge, isFlat, steep; };
// 0 invalid, 1 edge (skipped unless DETAIL), 3 usable (twin: ClassifyTexel)
int fsMeasClassify(uint layer, uint x, uint y, out FsTexelGeometry g) {
    const uint w = pc.b.z, h = pc.b.w, flags = pc.a.w;
    const float nearZ = pc.zp.x, farZ = pc.zp.y, maxDepth = pc.zp.z, minDepth = pc.zp.w;
    g.z = 0.0; g.zxm = 0.0; g.zxp = 0.0; g.zym = 0.0; g.zyp = 0.0; g.edge = false; g.isFlat = false; g.steep = false;
    if (x >= w || y >= h) return 0;
    if (x == 0u || y == 0u || x + 1u >= w || y + 1u >= h) return 0;
    g.z = fsMeasLinearizeDepth(fsFetch(layer, x, y), nearZ, farZ, flags);
    if (!fsMeasDepthUsable(g.z, minDepth, maxDepth)) return 0;
    g.zxm = fsMeasLinearizeDepth(fsFetch(layer, x - 1u, y), nearZ, farZ, flags);
    g.zxp = fsMeasLinearizeDepth(fsFetch(layer, x + 1u, y), nearZ, farZ, flags);
    g.zym = fsMeasLinearizeDepth(fsFetch(layer, x, y - 1u), nearZ, farZ, flags);
    g.zyp = fsMeasLinearizeDepth(fsFetch(layer, x, y + 1u), nearZ, farZ, flags);
    if (!(fsMeasDepthUsable(g.zxm, minDepth, maxDepth) && fsMeasDepthUsable(g.zxp, minDepth, maxDepth) &&
          fsMeasDepthUsable(g.zym, minDepth, maxDepth) && fsMeasDepthUsable(g.zyp, minDepth, maxDepth))) return 0;
    g.edge = fsMeasIsEdge(g.z, g.zxm, g.zxp, g.zym, g.zyp);
    if (g.edge && (flags & FS_MEAS_FLAG_DETAIL) == 0u) return 1;
    g.isFlat = fsMeasIsFlat(g.z, g.zxm, g.zxp, g.zym, g.zyp);
    g.steep = fsMeasIsSteep(g.z, g.zxm, g.zxp, g.zym, g.zyp);
    return 3;
}
// C11 planar path (twin: fs::meas::PlanarFit): robust plane over the (2R+1)^2 Env Depth window. A plane n.P = d seen from the eye
// is linear in inverse depth over the ray tangents: 1/z = alpha tx + beta ty + gamma. Huber IRLS (3 passes) on the inverse-depth
// residual; returns the centre depth on the plane, the eye-space normal (facing the eye), the inlier RMS (m) and inliers.
bool fsMeasPlanarFit(uint layer, uint x, uint y, vec4 fov, out float zc, out vec3 nEye, out float rmsZ, out uint inliers) {
    const uint w = pc.b.z, h = pc.b.w, flags = pc.a.w; const int R = FS_PLANAR_RADIUS;
    zc = 0.0; nEye = vec3(0.0, 0.0, -1.0); rmsZ = 0.0; inliers = 0u;
    if (x < uint(R) || y < uint(R) || x + uint(R) >= w || y + uint(R) >= h) return false;
    const int N = (2 * R + 1) * (2 * R + 1);
    float tx[25], ty[25], iz[25], zz[25]; bool ok[25];
    for (int j = 0; j < N; ++j) {
        int dx = j % (2 * R + 1) - R, dy = j / (2 * R + 1) - R;
        uint xx = uint(int(x) + dx), yy = uint(int(y) + dy);
        float z = fsMeasLinearizeDepth(fsFetch(layer, xx, yy), pc.zp.x, pc.zp.y, flags);
        vec2 t = fsMeasRayTangents(xx, yy, w, h, fov, flags);
        ok[j] = fsMeasDepthUsable(z, pc.zp.w, pc.zp.z); tx[j] = t.x; ty[j] = t.y; zz[j] = z; iz[j] = ok[j] ? 1.0 / z : 0.0;
    }
    vec3 abc = vec3(0.0); float scale = 0.0;
    for (int it = 0; it < 3; ++it) {
        mat3 A = mat3(0.0); vec3 rhs = vec3(0.0); float wsum = 0.0;
        for (int j = 0; j < N; ++j) {
            if (!ok[j]) continue;
            float wgt = 1.0;
            if (it > 0) { float r = iz[j] - dot(abc, vec3(tx[j], ty[j], 1.0)); float c = FS_PLANAR_HUBER_K * max(scale, 1e-6); wgt = abs(r) <= c ? 1.0 : c / abs(r); }
            vec3 a = vec3(tx[j], ty[j], 1.0);
            A += wgt * outerProduct(a, a); rhs += wgt * iz[j] * a; wsum += wgt;
        }
        if (wsum < float(FS_PLANAR_MIN_INLIERS) * 0.5 || abs(determinant(A)) < 1e-12) return false;
        abc = inverse(A) * rhs;
        float s2 = 0.0; uint n = 0u;
        for (int j = 0; j < N; ++j) { if (!ok[j]) continue; float r = iz[j] - dot(abc, vec3(tx[j], ty[j], 1.0)); s2 += r * r; n++; }
        scale = sqrt(s2 / max(float(n), 1.0));
    }
    vec2 tc = fsMeasRayTangents(x, y, w, h, fov, flags);
    float izc = dot(abc, vec3(tc, 1.0));
    if (izc <= 1e-6) return false;
    zc = 1.0 / izc;
    float r2 = 0.0; uint n = 0u;
    for (int j = 0; j < N; ++j) {
        if (!ok[j]) continue;
        float r = iz[j] - dot(abc, vec3(tx[j], ty[j], 1.0));
        if (abs(r) > 3.0 * max(scale, 1e-6)) continue;
        float izp = dot(abc, vec3(tx[j], ty[j], 1.0)); float ez = izp > 1e-6 ? zz[j] - 1.0 / izp : 0.0;
        r2 += ez * ez; n++;
    }
    inliers = n;
    rmsZ = sqrt(r2 / max(float(n), 1.0));
    nEye = -normalize(abc);                  // n.P = d with n = abc d: the plane normal; flip toward the eye (P.z > 0)
    return n >= uint(FS_PLANAR_MIN_INLIERS) && rmsZ <= FS_PLANAR_MAX_RMS_RATIO * zc;
}
#endif
#endif
