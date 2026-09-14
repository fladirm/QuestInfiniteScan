// FinalScan world/render module parameters. Preprocessor-only so the same file is included by C++
// (fs_world_types.h) and GLSL (fs_world_common.glsl via GL_GOOGLE_include_directive). Every dispatch
// item count and every bound below is a config constant (contract §15.4/§15.6: bounded stages, no
// unbounded loops). Numbers are provisional until the C05/C08 device benchmarks land.
#ifndef FS_WORLD_PARAMS_H
#define FS_WORLD_PARAMS_H

// ---- workgroups ------------------------------------------------------------------------------------
#define FS_WG_SMALL 64            // Adreno wave width; per-item kernels
#define FS_WG_SCAN  256           // single-workgroup prefix sums (<= 256 threads, gate §15.6)

// ---- arenas (defaults; FsHostConfig overrides slots/surfelsPerPage/hash/ring/draw capacities) --------
#define FS_DEFAULT_RESIDENT_SLOTS     256
#define FS_DEFAULT_SURFELS_PER_PAGE   16384
#define FS_DEFAULT_PAGE_HASH_CAPACITY 4096
#define FS_DEFAULT_MEAS_RING_CAPACITY 262144
#define FS_DEFAULT_DRAW_CAPACITY      1048576
#define FS_BIG_SLOT_FACTOR            16      // big slots hold surfelsPerPage * factor surfels (dense pages, §21.5b)
#define FS_BIG_SLOTS                  8
#define FS_RANGES_PER_SLOT            3       // BACK, FRONT parity 0, FRONT parity 1 (immutable publication, §11)
#define FS_MAX_ANCHORS                64
#define FS_MAX_SLOTS                  1024    // FsSlotLayout table bound
#define FS_MAX_FRONT_COUNT            262143  // 18-bit root word field

// ---- page-local index, variant A (fixed 32^3 bucket grid + one micro-bucket level, §9.2) --------------
#define FS_CELL_OVERFLOW_THRESHOLD 32         // list longer than this -> subdivide 2x2x2 on the next rebuild
#define FS_MICRO_BLOCKS_PER_SLOT   512        // per-slot micro-bucket pool: 512 blocks x 8 heads
#define FS_CELL_HEAD_SUBDIV_BIT    0x80000000u// cell head word: bit 31 set -> low bits = micro block index
#define FS_CELL_WALK_MAX           2048       // bounded list walk (publish count/copy); overflow counted
#define FS_CANDIDATE_MAX           16         // bounded candidate scan per measurement (§9.2)

// ---- integrate stub (C12 replaces the fusion rule) ---------------------------------------------------
#define FS_INTEGRATE_MAX_MEAS 16384           // measurements per SCAN job
#define FS_ASSOC_DIST_M       0.02            // existing surfel within 2 cm ...
#define FS_ASSOC_MIN_DOT      0.8             // ... and normal dot > 0.8 -> weighted update, else allocate in BACK
#define FS_EVIDENCE_COUNT_MAX 1023

// ---- cluster tree (§13.4) -----------------------------------------------------------------------------
#define FS_CLUSTER_FANOUT         8
#define FS_CLUSTER_MAX_LEVELS     8           // 64 * 8^7 surfels: far beyond any slot capacity
#define FS_CLUSTER_LEAF_LOOP_MAX  2048        // leaf kernel loop bound (leafSize <= 64 << 5)
#define FS_CLUSTER_MAX_LEAF_SHIFT 5
#define FS_MORTON_BITS            15          // 5 bits per axis over 32^3 cells
#define FS_COVERAGE_GRID          6           // leaf coverage = occupied cells of a 6x6 grid inside the footprint ellipse
#define FS_COVERAGE_INSIDE_CELLS  32          // cells of that grid whose centre lies inside the unit ellipse

// ---- cull (§13.5): 3 dispatches per cut (pages -> nodes -> emit+finish), all indirect, no CPU decisions --
#define FS_CULL_LEAF_LIST_CAPACITY 65536      // leaf ranges (uvec4) appended by the node pass, consumed by emit
#define FS_CULL_LEAF_LOOP_MAX     2048        // == FS_CLUSTER_LEAF_LOOP_MAX (lanes stride: <= 32 iterations per lane)
#define FS_CULL_NODE_GROUPS       64          // node pass x-groups per visible page: 64 * FS_WG_SMALL = 4096 items
#define FS_CULL_RAW_CHUNK         64          // trivial (tree-less) page: FRONT emitted in chunks of 64
#define FS_CULL_RAW_CHUNKS_MAX    4096
#define FS_CULL_FRAME_RING        4           // per-frame parameter blocks in flight
#define FS_CULL_STATS_WORDS       16
#define FS_HZB_SIZE               256         // level-0 tiles per axis (centre view covering both eyes)
#define FS_HZB_LEVELS             9           // 256 .. 1
#define FS_HZB_SRC_BLOCK          4           // source depth pixels folded per scatter thread (max = conservative)
#define FS_HZB_MARGIN_MIN_M       0.10        // §13.5.1: margin = max(sigma, 10 cm) + k * |grad|
#define FS_HZB_GRAD_K             2.0
#define FS_HZB_LOWCONF_RANGE_M    0.35        // env-depth block with depth range above this = low confidence, excluded
#define FS_FOVEA_DEG              5.0
#define FS_PERIPHERY_DEG          35.0
#define FS_HEADROOM_BIAS_MAX      4.0
#define FS_HEADROOM_BIAS_PER_US   0.001       // -1000 us headroom -> +1.0 threshold scale
#define FS_EYE_WIDTH_PX_DEFAULT   1680.0
#define FS_RENDER_MODE_SCAN       0
#define FS_RENDER_MODE_XRAY       1
#define FS_RENDER_MODE_PLAN       2

// ---- root word (per slot, single u32 read by the cull; written last = root-last publish, §11) --------
#define FS_ROOT_COUNT_MASK   0x0003FFFFu      // bits 0..17  FRONT surfel count
#define FS_ROOT_PUBLISHED    0x00040000u      // bit 18
#define FS_ROOT_PARITY       0x00080000u      // bit 19      FRONT range parity (0/1)
#define FS_ROOT_TREE_VALID   0x00100000u      // bit 20      cluster tree of this parity matches FRONT
#define FS_ROOT_LEAF_SHIFT   21               // bits 21..23 leafSize = 64 << shift
#define FS_ROOT_LEAF_MASK    0x00E00000u
#define FS_ROOT_GEN_SHIFT    24               // bits 24..31 generation low byte (telemetry / cache keys)

// ---- GPU-driven maintenance lists (gctr buffer regions, u32 words; STORAGE|INDIRECT usage) -------------
// The CPU never enumerates pages to submit work: it sets per-slot flags (host-visible writes) and submits
// ONE maintenance job; collect kernels compact flagged slots into bounded lists + indirect dispatch args.
#define FS_MAINT_MAX_PAGES        32          // pages per maintenance job (rebuild list / publish list / erase list)
#define FS_SLOT_FLAG_REBUILD      1u          // bit 0: page-local index must be rebuilt (new slot, persistent overflow)
#define FS_SLOT_FLAG_DIRTY        2u          // bit 1: BACK has unpublished changes
#define FS_G_FLAGS                16          // [16, 16 + FS_MAX_SLOTS): per-slot flags
#define FS_G_REBUILD_COUNT        1040
#define FS_G_REBUILD_ARGS_CELLS   1044        // {512, n, 1, 0}   cell-parallel dispatches
#define FS_G_REBUILD_ARGS_SURF    1048        // {ceil(maxCap/64), n, 1, 0} surfel-parallel dispatches
#define FS_G_REBUILD_LIST         1056        // [.., +32)
#define FS_G_PUBLISH_COUNT        1088
#define FS_G_PUBLISH_ARGS_CELLS   1092        // {512, n, 1, 0}
#define FS_G_PUBLISH_ARGS_SCAN    1096        // {n, 1, 1, 0}
#define FS_G_PUBLISH_ARGS_LEAVES  1100        // {ceil(sum leaves / 64), 1, 1, 0}
#define FS_G_PUBLISH_ARGS_LEVEL   1104        // 8 x {ceil(sum count[lv] / 64), 1, 1, 0} (index by level, level 0 unused)
#define FS_G_PUBLISH_LIST         1136        // [.., +32) slots
#define FS_G_PUBLISH_INFO         1168        // 32 x FS_G_INFO_WORDS per published page
#define FS_G_INFO_WORDS           16          // 0 slot, 1 total, 2 leafShift, 3 parity, 4 levels, 5 nodeTotal, 6..13 levelBase[0..7], 14 gen
#define FS_G_ERASE_COUNT          1680
#define FS_G_ERASE_ARGS           1684        // {ceil(maxCap/64), n, 1, 0}
#define FS_G_ERASE_LIST           1688        // [.., +32)
#define FS_G_SCAN_COUNT           1728        // live (C09) scan: record count written by world_scan_args.comp
#define FS_G_SCAN_ARGS            1732        // {ceil(count/64), 1, 1, 0} indirect args of the integrate dispatch
#define FS_G_WORDS                1744
#define FS_INTEGRATE_COUNT_FROM_GCTR 0xFFFFFFFFu  // PushIntegrate.count sentinel: bound by gctr[FS_G_SCAN_COUNT]
#define FS_PUBLISH_MIN_FRAME_GAP  3           // frames between maintenance jobs: a FRONT parity that stopped being
                                              // root stays untouched at least this long (in-flight culls read it)

// ---- GPU counters buffer (u32 words, host-visible; the sched tick folds deltas into FsCounter) --------
#define FS_GCTR_PAGE_LOOKUPS      0
#define FS_GCTR_PAGE_MISSES       1
#define FS_GCTR_CELL_LOOKUPS      2
#define FS_GCTR_SURFEL_CREATE     3
#define FS_GCTR_SURFEL_UPDATE     4
#define FS_GCTR_BACK_OVERFLOW     5
#define FS_GCTR_INDEX_OVERFLOW    6
#define FS_GCTR_MICRO_POOL_FULL   7
#define FS_GCTR_CANDIDATE_OVERFLOW 8
#define FS_GCTR_CELL_WALK_OVERFLOW 9
#define FS_GCTR_CLUSTER_OVERFLOW  10
#define FS_GCTR_MEAS_OUT_OF_RANGE 11
#define FS_GCTR_PUBLISH           12
#define FS_GCTR_SURFEL_DELETE     13
#define FS_GCTR_NEXT_SURFACE_ID   15
#define FS_GCTR_COUNT             16

// ---- cull stats words (device -> host ring) -----------------------------------------------------------
#define FS_CSTAT_FRAME            0
#define FS_CSTAT_PAGES_TESTED     1
#define FS_CSTAT_PAGES_CULLED     2
#define FS_CSTAT_PAGES_REUSED     3
#define FS_CSTAT_NODES_VISITED    4
#define FS_CSTAT_NODES_FRUSTUM    5
#define FS_CSTAT_NODES_CONE       6
#define FS_CSTAT_NODES_HZB        7
#define FS_CSTAT_NODES_IN_BAND    8
#define FS_CSTAT_AGGREGATES       9
#define FS_CSTAT_LEAVES           10
#define FS_CSTAT_DRAW_RECORDS     11
#define FS_CSTAT_BUDGET_DROPPED   12
#define FS_CSTAT_QUEUE_OVERFLOW   13
#define FS_CSTAT_VISIBLE_SURFELS  14

#endif // FS_WORLD_PARAMS_H
