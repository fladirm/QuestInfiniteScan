// SSBO blocks shared by the world kernels (C09R). Each kernel #defines the FS_USE_* it needs before including
// this file so unused blocks do not count against the 8-writable-binding gate. Layout twins: fs_world.h.
#ifndef FS_WORLD_BLOCKS_GLSL
#define FS_WORLD_BLOCKS_GLSL
#include "fs_world_common.glsl"
#include "fs_world_bindings.glsl"

#ifdef FS_USE_MEAS
layout(std430, set = 0, binding = FS_B_MEAS) readonly buffer MeasBlock { FsSurfaceMeasurement meas[]; };
#endif
#ifdef FS_USE_MEAS_W
layout(std430, set = 0, binding = FS_B_MEAS) writeonly buffer MeasBlockW { FsSurfaceMeasurement measW[]; };
#endif
#ifdef FS_USE_HASH
layout(std430, set = 0, binding = FS_B_HASH) readonly buffer HashBlock { FsPageHashEntry hash[]; };
#endif
#ifdef FS_USE_PAGES
layout(std430, set = 0, binding = FS_B_PAGES) buffer PageBlock { FsPageDesc pages[FS_MAX_PAGES]; uint slabDir[FS_MAX_PAGES * FS_PAGE_MAX_SLABS]; };
#endif
#ifdef FS_USE_PAGES_RO
layout(std430, set = 0, binding = FS_B_PAGES) readonly buffer PageBlockRO { FsPageDesc pages[FS_MAX_PAGES]; uint slabDir[FS_MAX_PAGES * FS_PAGE_MAX_SLABS]; };
#endif
#ifdef FS_USE_SURFELS
layout(std430, set = 0, binding = FS_B_SURFELS) buffer SurfelBlock { FsSurfel surfels[]; };
#endif
#ifdef FS_USE_SURFELS_RO
layout(std430, set = 0, binding = FS_B_SURFELS) readonly buffer SurfelBlockRO { FsSurfel surfels[]; };
#endif
#ifdef FS_USE_EVIDENCE
layout(std430, set = 0, binding = FS_B_EVIDENCE) buffer EvidenceBlock { FsSurfelEvidence evidence[]; };
#endif
#ifdef FS_USE_EVIDENCE_RO
layout(std430, set = 0, binding = FS_B_EVIDENCE) readonly buffer EvidenceBlockRO { FsSurfelEvidence evidence[]; };
#endif
#ifdef FS_USE_INDEX
layout(std430, set = 0, binding = FS_B_INDEX) buffer IndexBlock { uint idx[]; };
#endif
#ifdef FS_USE_INDEX_RO
layout(std430, set = 0, binding = FS_B_INDEX) readonly buffer IndexBlockRO { uint idx[]; };
#endif
#ifdef FS_USE_GCTR
layout(std430, set = 0, binding = FS_B_GCTR) buffer GctrBlock { uint gctr[]; };
#define FS_TK(w) gctr[uint(FS_GCTR_COUNT) + uint(w)]
#endif
#ifdef FS_USE_ASSOC_A
layout(std430, set = 0, binding = FS_B_ASSOC_A) buffer AssocA { FsAssociation assocA[]; };
#endif
#ifdef FS_USE_ASSOC_A_RO
layout(std430, set = 0, binding = FS_B_ASSOC_A) readonly buffer AssocARO { FsAssociation assocA[]; };
#endif
#ifdef FS_USE_ASSOC_B
layout(std430, set = 0, binding = FS_B_ASSOC_B) buffer AssocB { FsAssociation assocB[]; };
#endif
#ifdef FS_USE_SORT_RO
layout(std430, set = 0, binding = FS_B_SORT) readonly buffer SortBlockRO { uint sortw[]; };
#define FS_USE_SORT
#define FS_SORT_DECLARED
#endif
#ifdef FS_USE_SORT
#ifndef FS_SORT_DECLARED
layout(std430, set = 0, binding = FS_B_SORT) buffer SortBlock { uint sortw[]; };
#endif
#define FS_SORT_HIST      0                                      // [block * 256 + bin]
#define FS_SORT_BINSUM    (FS_SORT_BLOCKS_MAX * 256)             // [bin] exclusive prefix over bins (global)
#define FS_SORT_SEGNEW    (FS_SORT_BINSUM + 256)                 // [i] new candidates counted at unmatched segment start i
#define FS_SORT_PREFIX    (FS_SORT_SEGNEW + FS_TICK_MEAS_MAX)    // [i] exclusive prefix of SEGNEW
#define FS_SORT_BLOCKSUM  (FS_SORT_PREFIX + FS_TICK_MEAS_MAX)    // [block] block totals for the carry pass
#define FS_SORT_WORDS     (FS_SORT_BLOCKSUM + FS_SORT_BLOCKS_MAX)
#endif
#ifdef FS_USE_FREESPACE
layout(std430, set = 0, binding = FS_B_FREESPACE) buffer FreeSpaceBlock { uint freeSpace[]; };
#endif
#ifdef FS_USE_FREESPACE_RO
layout(std430, set = 0, binding = FS_B_FREESPACE) readonly buffer FreeSpaceBlockRO { uint freeSpace[]; };
#endif
#ifdef FS_USE_POOLS
// Free-id rings (CPU fills, GPU pops). Header per pool: [head, tail, mask, failures]; storage follows.
layout(std430, set = 0, binding = FS_B_POOLS) buffer PoolBlock { uint pool[]; };
#define FS_POOL_ILEAF   0
#define FS_POOL_INODE   1
#define FS_POOL_RBLOCK  2
#define FS_POOL_RNODE   3
// Pops one id or returns FS_INDEX_NONE (failure counted in the pool header). head/tail are monotonic; the CPU
// appends free ids at tail after retirement proof, the GPU consumes at head. Ring storage base/mask per pool.
uint fsPoolAlloc(uint p) {
    uint hb = p * uint(FS_POOL_HEADER_WORDS);
    uint h = atomicAdd(pool[hb + 0u], 1u);
    if (h >= pool[hb + 1u]) { atomicAdd(pool[hb + 3u], 1u); return FS_INDEX_NONE; }
    return pool[pool[hb + 4u] + (h & pool[hb + 2u])];
}
#endif
#ifdef FS_USE_DIRTY_RO
layout(std430, set = 0, binding = FS_B_DIRTY) readonly buffer DirtyBlockRO { uint dirty[]; };
#endif
#define FS_DIRTY_CELL_WORDS   1024                                        // 32768 bits per page
#define FS_DIRTY_RNODE_WORDS  160                                         // 4681 spatial nodes per page -> 147 words, padded
#define FS_DIRTY_CELLMASK(page) ((page) * uint(FS_DIRTY_CELL_WORDS))
#define FS_DIRTY_RNODEMASK(page) (uint(FS_MAX_PAGES) * uint(FS_DIRTY_CELL_WORDS) + (page) * uint(FS_DIRTY_RNODE_WORDS))
#define FS_DIRTY_LIST_BASE   (uint(FS_MAX_PAGES) * uint(FS_DIRTY_CELL_WORDS) + uint(FS_MAX_PAGES) * uint(FS_DIRTY_RNODE_WORDS))
#define FS_DIRTY_LIST(level) (FS_DIRTY_LIST_BASE + uint(level) * uint(FS_DIRTY_NODES_MAX))
#define FS_DIRTY_RELOC       (FS_DIRTY_LIST_BASE + 7u * uint(FS_DIRTY_NODES_MAX))   // FS_RELOC_MAX x 4 words: handle, page<<15|oldCell, newCell|oldPz<<15, oldPx_Py (level lists: 0 cells, 1..5 render levels, 6 page prefix)
#ifdef FS_USE_DIRTY
layout(std430, set = 0, binding = FS_B_DIRTY) buffer DirtyBlock { uint dirty[]; };
// Marks (page, cell) dirty once per epoch; returns true for the first marker.
bool fsMarkDirtyCell(uint page, uint cell) {
    uint prev = atomicOr(dirty[FS_DIRTY_CELLMASK(page) + (cell >> 5u)], 1u << (cell & 31u));
    return (prev & (1u << (cell & 31u))) == 0u;
}
#ifdef FS_USE_GCTR
// C09R-E4 index relocation (the index follows the geometry): a surfel whose fused centre left its index cell keeps its
// SurfaceID and handle; the record {handle, page<<15|oldCell, newCell|oldPz<<15, oldPx_Py} is applied by world_relocate
// (remove from the old leaf by the OLD position, insert into the new cell). False = list full: the caller keeps the
// old position (deferred, counted) instead of deforming the geometry.
bool fsPushRelocation(uint h, uint page, uint oldCell, uint newCell, FsSurfel oldS) {
    uint k = atomicAdd(FS_TK(FS_T_RELOC_COUNT), 1u);
    if (k >= uint(FS_RELOC_MAX)) { atomicAdd(gctr[FS_GCTR_RELOC_DEFERRED], 1u); return false; }
    uint b = FS_DIRTY_RELOC + 4u * k;
    dirty[b] = h; dirty[b + 1u] = (page << 15) | oldCell;
    dirty[b + 2u] = newCell | ((uint(fsGet_FsSurfel_pz(oldS)) & 0xFFFFu) << 15); dirty[b + 3u] = oldS.px_py;
    fsMarkDirtyCell(page, newCell);
    return true;
}
// Representable-range guard only (the page cube); never an index-cell clamp.
vec3 fsPageRangeGuard(vec3 l) { float hh = FS_PAGE_EXTENT_M * 0.5 - 0.0005; return clamp(l, vec3(-hh), vec3(hh)); }
#endif
#endif
#ifdef FS_USE_RENDER
layout(std430, set = 0, binding = FS_B_RENDER) buffer RenderNodeBlock { FsRenderNode rnodes[]; };
#endif
#ifdef FS_USE_RENDER_RO
layout(std430, set = 0, binding = FS_B_RENDER) readonly buffer RenderNodeBlockRO { FsRenderNode rnodes[]; };
#endif
#ifdef FS_USE_RBLOCKS
layout(std430, set = 0, binding = FS_B_RBLOCKS) buffer RenderSurfelBlock { FsSurfel rsurfels[]; };   // block b = [b*64, b*64+64)
#endif
#ifdef FS_USE_RBLOCKS_RO
layout(std430, set = 0, binding = FS_B_RBLOCKS) readonly buffer RenderSurfelBlockRO { FsSurfel rsurfels[]; };
#endif
#ifdef FS_USE_RDIR
// Per page: [0, 32768) leaf cells, then levels 1..5 of the octree (4096, 512, 64, 8, 1) = FS_RDIR_WORDS entries.
layout(std430, set = 0, binding = FS_B_RDIR) buffer RenderDirBlock { uint rdir[]; };
#endif
#ifdef FS_USE_RDIR_RO
layout(std430, set = 0, binding = FS_B_RDIR) readonly buffer RenderDirBlockRO { uint rdir[]; };
#endif
#define FS_RDIR_WORDS 37449u
// Offset of spatial node (level, index) inside a page's rdir region; level 0 index = cell (Morton-free: x|y<<5|z<<10),
// level L index = coarse cell with (32>>L)^3 entries indexed x | y << (5-L) | z << 2*(5-L).
uint fsRdirLevelBase(uint level) {
    uint b = 0u, s = 32u;
    for (uint l = 0u; l < uint(FS_RENDER_TREE_DEPTH); ++l) { if (l >= level) break; b += s * s * s; s >>= 1u; }
    return b;
}
uint fsRdirIndex(uint page, uint level, uint ix, uint iy, uint iz) {
    uint s = 32u >> level;
    return page * FS_RDIR_WORDS + fsRdirLevelBase(level) + ix + iy * s + iz * s * s;
}
#ifdef FS_USE_RETIRE
layout(std430, set = 0, binding = FS_B_RETIRE) buffer RetireBlock { uint retire[]; };
#define FS_RETIRE_KIND_RBLOCK 0u
#define FS_RETIRE_KIND_RNODE  1u
#endif
#ifdef FS_USE_PENDING
layout(std430, set = 0, binding = FS_B_PENDING) buffer PendingBlock { FsPendingPublish pending[]; };
#endif

// ---- index arena layout (twin: fs::world::IndexArena) --------------------------------------------------
// dir  : FS_MAX_PAGES * FS_CELLS_PER_PAGE words
// leaves: FS_INDEX_LEAF_WORDS (16) words each, FS_INDEX_LEAVES entries, base FS_IDX_LEAF_BASE
// nodes : 8 words each, base FS_IDX_NODE_BASE
#define FS_INDEX_LEAF_WORDS 16
#ifdef FS_USE_GCTR
uint fsIdxLeafWord(uint leaf, uint w) { return FS_TK(FS_T_IDX_LEAF_BASE) + leaf * uint(FS_INDEX_LEAF_WORDS) + w; }
uint fsIdxNodeWord(uint node, uint child) { return FS_TK(FS_T_IDX_NODE_BASE) + node * 8u + child; }
#endif
uint fsIdxDirWord(uint page, uint cell) { return page * uint(FS_CELLS_PER_PAGE) + cell; }
#endif
