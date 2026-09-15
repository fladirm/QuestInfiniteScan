// Global world grid cell -> (resident page slot, page-local cell) through the page hash (shared by sheet_graph and sheet_refine).
// Requires FS_USE_HASH and FS_USE_PAGES(_RO).
#ifndef FS_GLOBAL_CELL_GLSL
#define FS_GLOBAL_CELL_GLSL
#ifndef FS_FLOOR_DIV32_DEFINED
#define FS_FLOOR_DIV32_DEFINED
int fsFloorDiv32(int g) { return g >= 0 ? g / 32 : -((-g + 31) / 32); }
#endif
bool fsPageHashLookup(uint hashMask, FsPageKey key, out uint slot) {
    uint i = fsHashPageKey(key) & hashMask;
    for (uint d = 0u; d < FS_PAGE_HASH_MAX_PROBE; ++d) {
        FsPageHashEntry e = hash[i];
        if (e.slot == FS_INDEX_NONE) return false;
        if (e.probeDistance < d) return false;
        if (fsKeyEq(e.key, key)) { slot = e.slot; return true; }
        i = (i + 1u) & hashMask;
    }
    return false;
}
// global grid cell -> (resident page slot, page-local cell); false when the page is not resident
bool fsGlobalCell(uint hashMask, ivec3 g, uint ownPage, int anchorId, out uint slot, out uint cell) {
    ivec3 k = ivec3(fsFloorDiv32(g.x), fsFloorDiv32(g.y), fsFloorDiv32(g.z));
    ivec3 c = g - k * 32;
    cell = uint(c.x) | (uint(c.y) << 5) | (uint(c.z) << 10);
    FsPageKey own = pages[ownPage].key;
    if (k.x == own.x && k.y == own.y && k.z == own.z) { slot = ownPage; return true; }
    FsPageKey key; key.anchorId = anchorId; key.x = k.x; key.y = k.y; key.z = k.z;
    return fsPageHashLookup(hashMask, key, slot);
}
#endif
