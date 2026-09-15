// Frame block + shared per-texel classification of the C09R-E2 compaction kernels (twin: fs::meas::FrameBlock,
// ClassifyTexel). Requires FS_MEAS_USE_DEPTH (sampler) where classification runs.
#ifndef FS_MEAS_FRAME_GLSL
#define FS_MEAS_FRAME_GLSL
layout(std430, set = 0, binding = FS_MEAS_B_COUNTERS) buffer FrameBlock {
    uint ctr[FS_MEAS_CTR_WORDS]; mat4 anchorFromEye[2]; vec4 fov[2]; mat4 worldFromEye[2]; mat4 predViewProj[2]; mat4 predInvViewProj[2]; uvec4 predInfo;
    mat4 camFromWorld[2]; vec4 camIntrinsics[2]; uvec4 camInfo;
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
#endif
#endif
