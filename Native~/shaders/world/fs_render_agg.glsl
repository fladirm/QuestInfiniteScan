// Render tree aggregates (contract §13.4; twin: fs_cluster_build.h). A leaf node summarises one render block
// (<= 64 surfel copies): bounds, normal cone, representative patch, footprint ellipse, coverage = occupied
// cells of a 6x6 grid inside the footprint (gaps stay gaps). An internal node combines up to 8 children
// area-weighted. Errors: ownError = bounds halfAng-diagonal (monotone), parentError filled by the parent pass.
#ifndef FS_RENDER_AGG_GLSL
#define FS_RENDER_AGG_GLSL
void fsAggInit(out FsRenderNode r) {
    r.node.bmin[0] = 1e30; r.node.bmin[1] = 1e30; r.node.bmin[2] = 1e30; r.node.bmax[0] = -1e30; r.node.bmax[1] = -1e30; r.node.bmax[2] = -1e30;
    r.node.coneAxis[0] = 0.0; r.node.coneAxis[1] = 0.0; r.node.coneAxis[2] = 1.0; r.node.coneCos = -1.0;
    r.node.repCenter[0] = 0.0; r.node.repCenter[1] = 0.0; r.node.repCenter[2] = 0.0; r.node.repRadius = 0.0;
    r.node.repNormalOct32 = 0u; r.node.repColorOrHandle = 0xFF000000u; r.node.coverage_surfelCount = 0u;
    r.node.firstChildOrSurfel = FS_INDEX_NONE; r.node.childCount_leafSurfelCount = 0u; r.node.footprint = 0u;
    r.err.ownError = 0.0; r.err.parentError = FS_CLUSTER_ERROR_INF;
    for (uint k = 0u; k < 8u; ++k) r.child[k] = FS_INDEX_NONE;
}
#ifdef FS_USE_RBLOCKS
// Leaf aggregate over the block's `n` surfels (rsurfels[base .. base+n)).
void fsAggLeaf(uint base, uint n, uint blockId, inout FsRenderNode r) {
    vec3 bmin = vec3(1e30), bmax = vec3(-1e30), nsum = vec3(0.0), csum = vec3(0.0), colsum = vec3(0.0);
    float rmax = 0.0; uint used = 0u; float colN = 0.0;
    for (uint i = 0u; i < uint(FS_RENDER_BLOCK_SURFELS); ++i) {
        if (i >= n) break;
        FsSurfel s = rsurfels[base + i];
        vec3 p = fsSurfelLocalPos(s); vec3 nn = fsSurfelNormal(s);
        float rM = fsGet_FsSurfel_sigmaTMinor(s) == uint(FS_RENDER_TRI_MARK)          // E6R triangle: bound by its farthest vertex
                 ? max(length(fsTriOffsetDecode(s.radiusMajor_radiusMinor)), length(fsTriOffsetDecode(s.sigmaN_sigmaTMajor)))
                 : fsDecodeLogRadius(fsGet_FsSurfel_radiusMajor(s));
        bmin = min(bmin, p - vec3(rM)); bmax = max(bmax, p + vec3(rM)); csum += p; nsum += nn;
        if ((s.appearanceHandle & FS_APPEARANCE_MEASURED) != 0u && ((s.appearanceHandle & FS_APPEARANCE_OBS_MASK) >> FS_APPEARANCE_OBS_SHIFT) >= uint(FS_APPEARANCE_CONFIRM_OBS)) { colsum += vec3(float(s.appearanceHandle & 0xFFu), float((s.appearanceHandle >> 8) & 0xFFu), float((s.appearanceHandle >> 16) & 0xFFu)); colN += 1.0; }
        rmax = max(rmax, rM); used++;
    }
    if (used == 0u) { bmin = vec3(0.0); bmax = vec3(0.0); }
    float inv = used != 0u ? 1.0 / float(used) : 0.0;
    float nl = length(nsum);
    vec3 axis = vec3(0.0, 0.0, 1.0); float coneCos = -1.0;
    if (nl > 1e-6) {
        axis = nsum / nl; coneCos = 1.0;
        for (uint i = 0u; i < uint(FS_RENDER_BLOCK_SURFELS); ++i) { if (i >= used) break; coneCos = min(coneCos, dot(axis, fsSurfelNormal(rsurfels[base + i]))); }
    }
    vec3 t1, t2; fsTangentFrame(axis, t1, t2);
    float fa, fb, fcx, fcy; fsFootprintOfAabb(bmin, bmax, t1, t2, fa, fb, fcx, fcy);
    fa = max(fa, rmax); fb = max(fb, rmax);
    uint occ0 = 0u, occ1 = 0u;
    for (uint i = 0u; i < uint(FS_RENDER_BLOCK_SURFELS); ++i) {
        if (i >= used) break;
        vec3 p = fsSurfelLocalPos(rsurfels[base + i]);
        float u = (dot(p, t1) - fcx) / fa, v = (dot(p, t2) - fcy) / fb;
        if (u * u + v * v > 1.0) continue;
        int cx = clamp(int(floor((u + 1.0) * 0.5 * float(FS_COVERAGE_GRID))), 0, FS_COVERAGE_GRID - 1), cy = clamp(int(floor((v + 1.0) * 0.5 * float(FS_COVERAGE_GRID))), 0, FS_COVERAGE_GRID - 1);
        uint bit = uint(cy * FS_COVERAGE_GRID + cx);
        if (bit < 32u) occ0 |= 1u << bit; else occ1 |= 1u << (bit - 32u);
    }
    uint occupied = 0u;
    for (uint cy = 0u; cy < uint(FS_COVERAGE_GRID); ++cy) for (uint cx = 0u; cx < uint(FS_COVERAGE_GRID); ++cx) {
        uint bit = cy * uint(FS_COVERAGE_GRID) + cx;
        bool set = bit < 32u ? ((occ0 >> bit) & 1u) != 0u : ((occ1 >> (bit - 32u)) & 1u) != 0u;
        if (fsCoverageCellInside(cx, cy) && set) occupied++;
    }
    float cov = used != 0u ? min(float(occupied) / float(FS_COVERAGE_INSIDE_CELLS), 1.0) : 0.0;
    vec3 d = bmax - bmin;
    r.node.bmin[0] = bmin.x; r.node.bmin[1] = bmin.y; r.node.bmin[2] = bmin.z; r.node.bmax[0] = bmax.x; r.node.bmax[1] = bmax.y; r.node.bmax[2] = bmax.z;
    r.node.coneAxis[0] = axis.x; r.node.coneAxis[1] = axis.y; r.node.coneAxis[2] = axis.z; r.node.coneCos = coneCos;
    vec3 c = csum * inv; r.node.repCenter[0] = c.x; r.node.repCenter[1] = c.y; r.node.repCenter[2] = c.z;
    r.node.repRadius = max(0.5 * length(d), rmax);
    r.node.repNormalOct32 = fsEncodeOct32(axis);
    uvec3 col = uvec3(clamp(colsum / max(colN, 1.0) + vec3(0.5), vec3(0.0), vec3(255.0)));
    r.node.repColorOrHandle = col.x | (col.y << 8) | (col.z << 16) | (colN > 0.0 ? 0xFF000000u : 0u);   // alpha byte = CONFIRMED appearance present (E4.2R)
    fsSet_FsClusterNode_coverage(r.node, uint(cov * 65535.0 + 0.5)); fsSet_FsClusterNode_surfelCount(r.node, min(used, 65535u));
    r.node.firstChildOrSurfel = blockId; fsSet_FsClusterNode_childCount(r.node, 0u); fsSet_FsClusterNode_leafSurfelCount(r.node, used);
    r.node.footprint = fsPackFootprint(fa, fb);
    r.err.ownError = 0.5 * length(d); r.err.parentError = FS_CLUSTER_ERROR_INF;
}
#endif
// Internal aggregate over the child node ids in r.child[] (FS_INDEX_NONE = absent); sets childCount.
void fsAggInternal(inout FsRenderNode r) {
    vec3 bmin = vec3(1e30), bmax = vec3(-1e30), csum = vec3(0.0), asum = vec3(0.0), colsum = vec3(0.0);
    float areaSum = 0.0, wsum = 0.0, colW = 0.0; uint count = 0u, nchild = 0u; bool anyDir = false;
    for (uint k = 0u; k < 8u; ++k) {
        uint cid = r.child[k];
        if (cid == FS_INDEX_NONE) continue;
        FsClusterNode c = rnodes[cid].node;
        float w = max(float(fsGet_FsClusterNode_surfelCount(c)), 1.0);
        bmin = min(bmin, vec3(c.bmin[0], c.bmin[1], c.bmin[2])); bmax = max(bmax, vec3(c.bmax[0], c.bmax[1], c.bmax[2]));
        csum += vec3(c.repCenter[0], c.repCenter[1], c.repCenter[2]) * w; asum += vec3(c.coneAxis[0], c.coneAxis[1], c.coneAxis[2]) * w;
        if ((c.repColorOrHandle >> 24) != 0u) { colsum += vec3(float(c.repColorOrHandle & 0xFFu), float((c.repColorOrHandle >> 8) & 0xFFu), float((c.repColorOrHandle >> 16) & 0xFFu)) * w; colW += w; }
        areaSum += float(fsGet_FsClusterNode_coverage(c)) / 65535.0 * fsFootprintA(c.footprint) * fsFootprintB(c.footprint);
        wsum += w; count += fsGet_FsClusterNode_surfelCount(c); nchild++; if (c.coneCos <= -1.0) anyDir = true;
    }
    float inv = wsum > 0.0 ? 1.0 / wsum : 0.0;
    float al = length(asum);
    vec3 axis = vec3(0.0, 0.0, 1.0); float coneCos = -1.0;
    if (!anyDir && al > 1e-6) {
        axis = asum / al; float halfAng = 0.0;
        for (uint k = 0u; k < 8u; ++k) {
            uint cid = r.child[k]; if (cid == FS_INDEX_NONE) continue;
            FsClusterNode c = rnodes[cid].node;
            float d = clamp(dot(axis, vec3(c.coneAxis[0], c.coneAxis[1], c.coneAxis[2])), -1.0, 1.0);
            halfAng = max(halfAng, acos(d) + acos(clamp(c.coneCos, -1.0, 1.0)));
        }
        coneCos = halfAng >= FS_PI ? -1.0 : cos(halfAng);
    }
    if (nchild == 0u) { bmin = vec3(0.0); bmax = vec3(0.0); }
    vec3 d = bmax - bmin;
    r.node.bmin[0] = bmin.x; r.node.bmin[1] = bmin.y; r.node.bmin[2] = bmin.z; r.node.bmax[0] = bmax.x; r.node.bmax[1] = bmax.y; r.node.bmax[2] = bmax.z;
    r.node.coneAxis[0] = axis.x; r.node.coneAxis[1] = axis.y; r.node.coneAxis[2] = axis.z; r.node.coneCos = coneCos;
    vec3 c = csum * inv; r.node.repCenter[0] = c.x; r.node.repCenter[1] = c.y; r.node.repCenter[2] = c.z;
    r.node.repRadius = 0.5 * length(d);
    r.node.repNormalOct32 = fsEncodeOct32(axis);
    uvec3 col = uvec3(clamp(colsum / max(colW, 1e-6) + vec3(0.5), vec3(0.0), vec3(255.0)));
    r.node.repColorOrHandle = col.x | (col.y << 8) | (col.z << 16) | (colW > 0.0 ? 0xFF000000u : 0u);
    vec3 t1, t2; fsTangentFrame(axis, t1, t2);
    float fa, fb, fcx, fcy; fsFootprintOfAabb(bmin, bmax, t1, t2, fa, fb, fcx, fcy);
    float cov = fa * fb > 0.0 ? clamp(areaSum / (fa * fb), 0.0, 1.0) : 0.0;
    fsSet_FsClusterNode_coverage(r.node, uint(cov * 65535.0 + 0.5)); fsSet_FsClusterNode_surfelCount(r.node, min(count, 65535u));
    fsSet_FsClusterNode_childCount(r.node, nchild); fsSet_FsClusterNode_leafSurfelCount(r.node, 0u);
    r.node.firstChildOrSurfel = FS_INDEX_NONE;
    r.node.footprint = fsPackFootprint(fa, fb);
    r.err.ownError = 0.5 * length(d); r.err.parentError = FS_CLUSTER_ERROR_INF;
}
#endif
