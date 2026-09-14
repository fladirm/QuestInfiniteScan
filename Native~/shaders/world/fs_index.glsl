// Adaptive page-local index helpers (contract §9.2, C09R). Requires FS_USE_INDEX (or FS_USE_INDEX_RO for the
// lookup only) and FS_USE_GCTR; insertion additionally requires FS_USE_POOLS, FS_USE_SURFELS(_RO) and
// FS_USE_RETIRE. Ownership rule: a cell subtree is mutated only by the single invocation that owns that cell
// in the epoch; lookups run against the epoch's immutable generation. No atomics touch the index.
#ifndef FS_INDEX_GLSL
#define FS_INDEX_GLSL

// Descends from the cell directory entry toward `l`; returns the leaf entry id reached (or FS_INDEX_NONE).
uint fsIndexFindLeaf(uint page, uint cell, vec3 l) {
    uint e = idx[fsIdxDirWord(page, cell)];
    vec3 bmin, bmax; fsCellBounds(cell, bmin, bmax);
    for (uint d = 0u; d < uint(FS_INDEX_MAX_DEPTH); ++d) {
        if (!fsEntryIsNode(e)) break;
        vec3 cmin, cmax; uint o = fsOctantOf(l, bmin, bmax, cmin, cmax); bmin = cmin; bmax = cmax;
        e = idx[fsIdxNodeWord(fsEntryId(e), o)];
    }
    return fsEntryIsLeaf(e) ? fsEntryId(e) : FS_INDEX_NONE;
}

#if defined(FS_USE_INDEX) && defined(FS_USE_POOLS) && defined(FS_USE_RETIRE)
#define FS_RETIRE_KIND_ILEAF 2u
#define FS_RETIRE_KIND_INODE 3u
void fsRetirePush(uint kind, uint id) {
    uint k = atomicAdd(FS_TK(FS_T_RETIRE_COUNT), 1u);
    if (k < uint(FS_RETIRE_MAX)) retire[k] = (kind << 30u) | id;
}
uint fsIndexNewLeaf(uint h) {
    uint leaf = fsPoolAlloc(FS_POOL_ILEAF);
    if (leaf == FS_INDEX_NONE) { atomicAdd(gctr[FS_GCTR_POOL_LEAF_EMPTY], 1u); return FS_INDEX_NONE; }
    idx[fsIdxLeafWord(leaf, 0u)] = 1u; idx[fsIdxLeafWord(leaf, 1u)] = FS_INDEX_NONE; idx[fsIdxLeafWord(leaf, 2u)] = h;
    return leaf;
}
// Appends h to the leaf chain starting at `leaf` (chains exist only at max depth). Returns true on success.
bool fsIndexChainAppend(uint leaf, uint h) {
    uint cur = leaf;
    for (uint c = 0u; c < uint(FS_INDEX_CHAIN_MAX); ++c) {
        uint cnt = idx[fsIdxLeafWord(cur, 0u)];
        if (cnt < uint(FS_INDEX_LEAF_CAP)) { idx[fsIdxLeafWord(cur, 2u + cnt)] = h; idx[fsIdxLeafWord(cur, 0u)] = cnt + 1u; return true; }
        uint nx = idx[fsIdxLeafWord(cur, 1u)];
        if (nx == FS_INDEX_NONE) {
            if (c + 1u >= uint(FS_INDEX_CHAIN_MAX)) break;
            uint nl = fsIndexNewLeaf(h); if (nl == FS_INDEX_NONE) return false;
            idx[fsIdxLeafWord(cur, 1u)] = nl; return true;
        }
        cur = nx;
    }
    atomicAdd(gctr[FS_GCTR_INDEX_OVERFLOW], 1u);
    return false;
}
// Inserts handle h (local position l inside cell `cell`) into the owner's cell subtree; a full leaf above the
// maximum depth splits spatially into an octant node (its handles are redistributed by their positions).
bool fsIndexInsert(uint page, uint cell, vec3 l, uint h) {
    uint parentWord = fsIdxDirWord(page, cell);
    uint e = idx[parentWord];
    vec3 bmin, bmax; fsCellBounds(cell, bmin, bmax);
    for (uint d = 0u; d <= uint(FS_INDEX_MAX_DEPTH); ++d) {
        if (e == 0u) { uint nl = fsIndexNewLeaf(h); if (nl == FS_INDEX_NONE) return false; idx[parentWord] = fsLeafEntry(nl); return true; }
        if (fsEntryIsLeaf(e)) {
            uint leaf = fsEntryId(e);
            uint cnt = idx[fsIdxLeafWord(leaf, 0u)];
            if (cnt < uint(FS_INDEX_LEAF_CAP) || d == uint(FS_INDEX_MAX_DEPTH)) return fsIndexChainAppend(leaf, h);
            // split: node + redistribute this (single, unchained) leaf
            uint node = fsPoolAlloc(FS_POOL_INODE);
            if (node == FS_INDEX_NONE) { atomicAdd(gctr[FS_GCTR_POOL_NODE_EMPTY], 1u); return false; }
            for (uint k = 0u; k < 8u; ++k) idx[fsIdxNodeWord(node, k)] = 0u;
            for (uint i = 0u; i < uint(FS_INDEX_LEAF_CAP); ++i) {
                if (i >= cnt) break;
                uint hh = idx[fsIdxLeafWord(leaf, 2u + i)];
                vec3 pl = fsSurfelLocalPos(surfels[hh]);
                vec3 cmin, cmax; uint o = fsOctantOf(pl, bmin, bmax, cmin, cmax);
                uint ce = idx[fsIdxNodeWord(node, o)];
                if (ce == 0u) { uint nl = fsIndexNewLeaf(hh); if (nl == FS_INDEX_NONE) return false; idx[fsIdxNodeWord(node, o)] = fsLeafEntry(nl); }
                else { if (!fsIndexChainAppend(fsEntryId(ce), hh)) return false; }
            }
            fsRetirePush(FS_RETIRE_KIND_ILEAF, leaf);
            atomicAdd(gctr[FS_GCTR_INDEX_LEAF_SPLITS], 1u);
            idx[parentWord] = fsNodeEntry(node);
            e = fsNodeEntry(node);
        }
        // node: descend toward l
        vec3 cmin, cmax; uint o = fsOctantOf(l, bmin, bmax, cmin, cmax); bmin = cmin; bmax = cmax;
        parentWord = fsIdxNodeWord(fsEntryId(e), o);
        e = idx[parentWord];
    }
    atomicAdd(gctr[FS_GCTR_INDEX_OVERFLOW], 1u);
    return false;
}
// Removes handle h from the leaf chain that holds it (owner-only). Returns true when found.
bool fsIndexRemove(uint page, uint cell, vec3 l, uint h) {
    uint leaf = fsIndexFindLeaf(page, cell, l);
    for (uint c = 0u; c < uint(FS_INDEX_CHAIN_MAX); ++c) {
        if (leaf == FS_INDEX_NONE) return false;
        uint cnt = idx[fsIdxLeafWord(leaf, 0u)];
        for (uint i = 0u; i < uint(FS_INDEX_LEAF_CAP); ++i) {
            if (i >= cnt) break;
            if (idx[fsIdxLeafWord(leaf, 2u + i)] == h) {
                idx[fsIdxLeafWord(leaf, 2u + i)] = idx[fsIdxLeafWord(leaf, 2u + cnt - 1u)];
                idx[fsIdxLeafWord(leaf, 0u)] = cnt - 1u;
                return true;
            }
        }
        leaf = idx[fsIdxLeafWord(leaf, 1u)];
    }
    return false;
}
#endif
#endif // FS_INDEX_GLSL
