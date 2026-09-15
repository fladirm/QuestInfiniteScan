// Binding numbers of the world kernels (C09R). The contract with fs_world_kernels.h; every pipeline binds a
// subset (<= 8 writable storage buffers, gate §15.6). Read-only blocks are declared readonly.
#ifndef FS_WORLD_BINDINGS_GLSL
#define FS_WORLD_BINDINGS_GLSL
#define FS_B_MEAS       0    // FsSurfaceMeasurement[FS_TICK_MEAS_MAX] (epoch copy, world-owned)
#define FS_B_HASH       1    // FsPageHashEntry[] mirror (CPU authority)
#define FS_B_PAGES      2    // FsPageDesc pages[FS_MAX_PAGES]; uint slabDir[FS_MAX_PAGES * FS_PAGE_MAX_SLABS]
#define FS_B_SURFELS    3    // FsSurfel[] canonical pool
#define FS_B_EVIDENCE   4    // FsSurfelEvidence[] canonical pool
#define FS_B_INDEX      5    // u32 arena: [dir: FS_MAX_PAGES*32768][leaves: 16 words each][nodes: 8 words each]
#define FS_B_GCTR       6    // u32: [0,FS_GCTR_COUNT) counters, [FS_GCTR_COUNT, +FS_T_WORDS) epoch words + indirect args
#define FS_B_ASSOC_A    7    // FsAssociation[FS_TICK_MEAS_MAX]
#define FS_B_ASSOC_B    8    // FsAssociation[FS_TICK_MEAS_MAX] (sort ping-pong)
#define FS_B_SORT       9    // u32 scratch: histograms, block sums, segment counts, prefix
#define FS_B_FREESPACE  10   // u8 stamps packed in u32 (per page FS_CELLS_PER_PAGE bytes)
#define FS_B_POOLS      11   // free-id rings: {head, tail, cap, pad} x 4 pools + ring storage
#define FS_B_DIRTY      12   // per page: cell bitmask (1024 words) + render node bitmask; then per-level lists
#define FS_B_RENDER     13   // FsRenderNode[] (node + error + child table) pool
#define FS_B_RBLOCKS    14   // FsSurfel[FS_RENDER_BLOCK_SURFELS] blocks pool
#define FS_B_RDIR       15   // per page: current render node id per spatial node (cells + 4 internal levels + root)
#define FS_B_RETIRE     16   // u32 ids released this epoch: (kind << 30 | id)
#define FS_B_PENDING    17   // FsPendingPublish[FS_MAX_PAGES]
#define FS_B_MEAS_RING  18   // ingest only: the measurement ring slot records
#define FS_B_MEAS_CTR   19   // ingest only: the ring slot frame block (count)
#define FS_B_SHEET      20   // E4.1R SurfaceGraph: nodes[surfelCap * FS_SHEET_NODE_WORDS], dirty bitmask, dirty ring, batch deltas
#endif
