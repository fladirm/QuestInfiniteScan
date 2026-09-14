// Shared SSBO block declarations for world kernels (binding numbers are the contract with
// fs_world_kernels.h; every pipeline uses a subset, <= 8 storage bindings, gate §15.6).
// The slot table block packs the three per-slot arrays into ONE binding: static layouts, root words
// (host-written, root-last publish) and page headers (GPU atomics on backCount during SCAN).
#ifndef FS_WORLD_BINDINGS_GLSL
#define FS_WORLD_BINDINGS_GLSL
#define FS_B_MEAS     0
#define FS_B_HASH     1
#define FS_B_SLOTS    2
#define FS_B_SURFELS  3
#define FS_B_EVIDENCE 4
#define FS_B_INDEX    5
#define FS_B_GCTR     6
#define FS_B_NODES    7
#define FS_B_SCRATCH  7   // publish kernels use the scratch ring instead of nodes
#endif
