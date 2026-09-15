// Local maintenance of one cell subtree (C09R-B F4): the owner invocation of a dirty cell walks its leaves
// (bounded), applies free-space contradictions (ghost removal) and deterministic merges (fuse_maint_apply). Refinement is
// site insertion (sheet_refine, C09R-E4.1C), coarsening is graph edge contraction (sheet_contract).
#ifndef FS_MAINT_GLSL
#define FS_MAINT_GLSL
#define FS_MAINT_LEAVES_MAX 64
#define FS_MAINT_STACK 32
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
bool fsMergeable(FsPatch a, FsPatch b) {
    if ((a.flags & FS_FLAG_PROMOTED) == 0u || (b.flags & FS_FLAG_PROMOTED) == 0u) return false;
    if (dot(a.n, b.n) < FS_MERGE_MIN_DOT) return false;
    vec3 delta = b.p - a.p; float d = dot(a.n, delta);
    if (abs(d) > FS_ASSOC_SIGMA_GATE * sqrt(a.sigmaN * a.sigmaN + b.sigmaN * b.sigmaN)) return false;
    return length(delta - d * a.n) <= FS_MERGE_OVERLAP_K * (a.rM + b.rM);
}
#endif
