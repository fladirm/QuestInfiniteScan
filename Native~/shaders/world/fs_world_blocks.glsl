// SSBO blocks shared by the world kernels. Each kernel #defines the FS_USE_* it needs before including
// this file so unused blocks do not count against the 8-binding gate.
#ifndef FS_WORLD_BLOCKS_GLSL
#define FS_WORLD_BLOCKS_GLSL
#include "fs_world_common.glsl"
#include "fs_world_bindings.glsl"

#ifdef FS_USE_MEAS
layout(std430, set = 0, binding = FS_B_MEAS) readonly buffer MeasBlock { FsSurfaceMeasurement meas[]; };
#endif
#ifdef FS_USE_HASH
layout(std430, set = 0, binding = FS_B_HASH) readonly buffer HashBlock { FsPageHashEntry hash[]; };
#endif
#ifdef FS_USE_SLOTS
// One binding, three regions (twin: fs::world::SlotTable): layouts, root words, page headers.
layout(std430, set = 0, binding = FS_B_SLOTS) buffer SlotBlock {
    FsSlotLayout layouts[FS_MAX_SLOTS];
    uint         roots[FS_MAX_SLOTS];
    FsPageHeader headers[FS_MAX_SLOTS];
};
#endif
#ifdef FS_USE_SURFELS
layout(std430, set = 0, binding = FS_B_SURFELS) buffer SurfelBlock { FsSurfel surfels[]; };
#endif
#ifdef FS_USE_EVIDENCE
layout(std430, set = 0, binding = FS_B_EVIDENCE) buffer EvidenceBlock { FsSurfelEvidence evidence[]; };
#endif
#ifdef FS_USE_INDEX
// u32 arena: [0,S) micro pool cursors; per slot: heads (32768) then counts (32768) at layout.cellBase,
// micro heads at layout.microBase (512*8), BACK links at layout.linkBase.
layout(std430, set = 0, binding = FS_B_INDEX) buffer IndexBlock { uint idx[]; };
#endif
#ifdef FS_USE_GCTR
// [0,16) GPU counters (FS_GCTR_*), [FS_G_FLAGS + slot] per-slot flags (FS_SLOT_FLAG_*), then the
// GPU-generated maintenance lists + indirect dispatch args (FS_G_* regions, fs_world_params.h).
layout(std430, set = 0, binding = FS_B_GCTR) buffer GctrBlock { uint gctr[]; };
#endif
#ifdef FS_USE_NODES
layout(std430, set = 0, binding = FS_B_NODES) buffer NodeBlock { FsClusterNode nodes[]; };
#endif
#ifdef FS_USE_SCRATCH
// Publish scratch: per published page (list index) FS_CELLS_PER_PAGE counts/offsets + 16 words (total at [FS_CELLS_PER_PAGE]).
#define FS_SCRATCH_STRIDE (FS_CELLS_PER_PAGE + 16)
layout(std430, set = 0, binding = FS_B_SCRATCH) buffer ScratchBlock { uint scratch[]; };
#endif
#ifdef FS_USE_ERRORS
layout(std430, set = 0, binding = FS_B_EVIDENCE) buffer ErrorBlock { FsClusterError errors[]; };   // cluster kernels: binding 4 = errors
#endif

#ifdef FS_USE_INDEX
uint fsHeadIndex(FsSlotLayout l, uint cell) { return l.cellBase + cell; }
uint fsCountIndex(FsSlotLayout l, uint cell) { return l.cellBase + uint(FS_CELLS_PER_PAGE) + cell; }
uint fsMicroIndex(FsSlotLayout l, uint block, uint oct) { return l.microBase + block * 8u + oct; }
uint fsLinkIndex(FsSlotLayout l, uint globalSurfel) { return l.linkBase + (globalSurfel - l.surfelBase); }
// Resolves the list head word index for a page-local position (coarse cell or its micro bucket).
uint fsListHeadIndex(FsSlotLayout l, vec3 local, uint cell) {
    uint h = idx[fsHeadIndex(l, cell)];
    if ((h & FS_CELL_HEAD_SUBDIV_BIT) != 0u) return fsMicroIndex(l, h & ~FS_CELL_HEAD_SUBDIV_BIT, fsOctant(local));
    return fsHeadIndex(l, cell);
}
#endif
#endif
