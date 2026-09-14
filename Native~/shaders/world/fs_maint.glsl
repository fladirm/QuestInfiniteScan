// Local maintenance of one cell subtree (C09R-B F4): the owner invocation of a dirty cell walks its leaves
// (bounded), applies free-space contradictions (ghost removal), deterministic merges and explicit splits.
// Shared by fuse_maint_count (plans, counts splits) and fuse_maint_apply (executes with deterministic ids).
#ifndef FS_MAINT_GLSL
#define FS_MAINT_GLSL
#define FS_MAINT_LEAVES_MAX 64
#define FS_MAINT_STACK 32
#define FS_SPLITS_PER_CELL 8
// Collects the leaf ids of the cell subtree in a deterministic order (depth-first, octant order).
uint fsCollectLeaves(uint page, uint cell, out uint leaves[FS_MAINT_LEAVES_MAX]) {
    uint n = 0u;
    uint stack[FS_MAINT_STACK]; uint sp = 0u;
    uint e = idx[fsIdxDirWord(page, cell)];
    if (e == 0u) return 0u;
    stack[sp++] = e;
    for (uint it = 0u; it < 256u; ++it) {
        if (sp == 0u) break;
        e = stack[--sp];
        if (fsEntryIsLeaf(e)) {
            uint leaf = fsEntryId(e);
            for (uint c = 0u; c < uint(FS_INDEX_CHAIN_MAX); ++c) {
                if (leaf == FS_INDEX_NONE || n >= uint(FS_MAINT_LEAVES_MAX)) break;
                leaves[n++] = leaf;
                leaf = idx[fsIdxLeafWord(leaf, 1u)];
            }
        } else if (fsEntryIsNode(e)) {
            for (int k = 7; k >= 0; --k) { uint ce = idx[fsIdxNodeWord(fsEntryId(e), uint(k))]; if (ce != 0u && sp < uint(FS_MAINT_STACK)) stack[sp++] = ce; }
        }
    }
    return n;
}
bool fsSplitWanted(FsPatch q, FsSurfelEvidence ev) {
    uint cnt = q.flags & FS_EVIDENCE_COUNT_MASK;
    if (cnt < uint(FS_SPLIT_MIN_SUPPORT) || (q.flags & FS_FLAG_PROMOTED) == 0u) return false;
    float var = fsDecodeLog(fsGet_FsSurfelEvidence_varianceQ(ev), FS_SIGMA_BASE_M * FS_SIGMA_BASE_M);
    return sqrt(var) > FS_SPLIT_VAR_K * q.sigmaN && q.rM > 2.0 * FS_RADIUS_MIN_M;
}
// Merge priority: higher static evidence, then lower sigma, then lower SurfaceID survives.
bool fsSurvives(FsPatch a, FsSurfelEvidence ea, FsPatch b, FsSurfelEvidence eb) {
    uint sa = fsGet_FsSurfelEvidence_staticEvidence(ea), sb = fsGet_FsSurfelEvidence_staticEvidence(eb);
    if (sa != sb) return sa > sb;
    if (a.sigmaN != b.sigmaN) return a.sigmaN < b.sigmaN;
    return a.surfaceId < b.surfaceId;
}
bool fsMergeable(FsPatch a, FsPatch b) {
    if ((a.flags & FS_FLAG_PROMOTED) == 0u || (b.flags & FS_FLAG_PROMOTED) == 0u) return false;
    if (dot(a.n, b.n) < FS_MERGE_MIN_DOT) return false;
    vec3 delta = b.p - a.p; float d = dot(a.n, delta);
    if (abs(d) > FS_ASSOC_SIGMA_GATE * sqrt(a.sigmaN * a.sigmaN + b.sigmaN * b.sigmaN)) return false;
    return length(delta - d * a.n) <= FS_MERGE_OVERLAP_K * (a.rM + b.rM);
}
#endif
