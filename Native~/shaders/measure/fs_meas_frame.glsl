// Frame block + shared per-texel classification of the C09R-E2 compaction kernels (twin: fs::meas::FrameBlock,
// ClassifyTexel). Requires FS_MEAS_USE_DEPTH (sampler) where classification runs.
#ifndef FS_MEAS_FRAME_GLSL
#define FS_MEAS_FRAME_GLSL
layout(std430, set = 0, binding = FS_MEAS_B_COUNTERS) buffer FrameBlock {
    uint ctr[FS_MEAS_CTR_WORDS]; mat4 anchorFromEye[2]; vec4 fov[2]; mat4 worldFromEye[2]; mat4 predViewProj[2]; mat4 predInvViewProj[2]; uvec4 predInfo;
    mat4 camFromWorld[2]; vec4 camIntrinsics[2]; uvec4 camInfo; mat4 anchorFromWorld; uvec4 stereoInfo; mat4 keyCamFromWorld; vec4 keyIntrinsics; uvec4 keyInfo; mat4 worldFromAnchor;
} blk;
// Twin: fs::meas::PushCompact (48 B). zp = (nearZ, farZ, maxDepthM, minDepthM); a = (layers, budget, maxOut, flags); b = (obsId, frame, width, height).
#ifndef FS_MEAS_REFINE_PUSH
layout(push_constant) uniform Push { vec4 zp; uvec4 a; uvec4 b; } pc;
#else
layout(push_constant) uniform Push { uint offset; uint count; uint recordCap; uint pad; } pc;   // C10R refine chunk over the slot records
#endif
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
// C11R planar tile (twin: fs::meas::PlanarFit over the tile's points): robust plane over one 8x8 Env Depth tile (16 points, stride 2).
// A plane seen from the eye is linear in inverse depth over the ray tangents: 1/z = a tx + b ty + c. Huber IRLS (3 passes); returns
// abc, the inlier depth RMS (m) and the inlier count; valid when enough inliers and the RMS is within the flat bound at the tile depth.
bool fsMeasPlanarTile(uint layer, uint tileX, uint tileY, vec4 fov, out vec3 abc, out float rmsZ, out uint inliers) {
    const uint w = pc.b.z, h = pc.b.w, flags = pc.a.w;
    abc = vec3(0.0); rmsZ = 0.0; inliers = 0u;
    float tx[16], ty[16], iz[16], zz[16]; bool ok[16]; uint nOk = 0u;
    for (int j = 0; j < 16; ++j) {
        uint xx = tileX * uint(FS_PLANAR_TILE) + uint(j % 4) * 2u, yy = tileY * uint(FS_PLANAR_TILE) + uint(j / 4) * 2u;
        ok[j] = false; tx[j] = 0.0; ty[j] = 0.0; iz[j] = 0.0; zz[j] = 0.0;
        if (xx >= w || yy >= h) continue;
        float z = fsMeasLinearizeDepth(fsFetch(layer, xx, yy), pc.zp.x, pc.zp.y, flags);
        if (!fsMeasDepthUsable(z, pc.zp.w, pc.zp.z)) continue;
        vec2 t = fsMeasRayTangents(xx, yy, w, h, fov, flags);
        ok[j] = true; tx[j] = t.x; ty[j] = t.y; zz[j] = z; iz[j] = 1.0 / z; nOk++;
    }
    if (nOk < uint(FS_PLANAR_TILE_MIN_INLIERS)) return false;
    float scale = 0.0;
    for (int it = 0; it < 3; ++it) {
        mat3 A = mat3(0.0); vec3 rhs = vec3(0.0);
        for (int j = 0; j < 16; ++j) {
            if (!ok[j]) continue;
            vec3 a = vec3(tx[j], ty[j], 1.0);
            float wgt = 1.0;
            if (it > 0) { float r = iz[j] - dot(abc, a); float c = FS_PLANAR_HUBER_K * max(scale, 1e-6); wgt = abs(r) <= c ? 1.0 : c / abs(r); }
            A += wgt * outerProduct(a, a); rhs += wgt * iz[j] * a;
        }
        if (abs(determinant(A)) < 1e-12) return false;
        abc = inverse(A) * rhs;
        float s2 = 0.0; for (int j = 0; j < 16; ++j) { if (!ok[j]) continue; float r = iz[j] - dot(abc, vec3(tx[j], ty[j], 1.0)); s2 += r * r; }
        scale = sqrt(s2 / float(nOk));
    }
    float r2 = 0.0, zsum = 0.0; uint n = 0u;
    for (int j = 0; j < 16; ++j) {
        if (!ok[j]) continue;
        float izp = dot(abc, vec3(tx[j], ty[j], 1.0)), r = iz[j] - izp;
        if (abs(r) > 3.0 * max(scale, 1e-6) || izp <= 1e-6) continue;
        float ez = zz[j] - 1.0 / izp; r2 += ez * ez; zsum += zz[j]; n++;
    }
    inliers = n;
    if (n == 0u) return false;
    rmsZ = sqrt(r2 / float(n));
    return n >= uint(FS_PLANAR_TILE_MIN_INLIERS) && rmsZ <= FS_PLANAR_MAX_RMS_RATIO * (zsum / float(n));
}
#endif
#endif
