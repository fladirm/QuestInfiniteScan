// FinalScan world/render module parameters (C09R). Preprocessor-only so the same file is included by C++
// (fs_world_types.h) and GLSL (fs_world_common.glsl via GL_GOOGLE_include_directive). Every dispatch item
// count and every bound below is a config constant (contract §15.4/§15.6: bounded stages, no unbounded
// loops). Pool sizes are default budgets (bytes accounted by the memory governor, FsHostConfig overrides).
#ifndef FS_WORLD_PARAMS_H
#define FS_WORLD_PARAMS_H

// ---- workgroups ------------------------------------------------------------------------------------
#define FS_WG_SMALL 64            // Adreno wave width; per-item kernels
#define FS_WG_SCAN  256           // single-workgroup prefix sums / radix blocks (<= 256 threads, gate §15.6)

// ---- pools (defaults; FsHostConfig overrides). Canonical geometry exists ONCE (contract §11, C09R) --------
#define FS_DEFAULT_RESIDENT_PAGES     256       // page table entries (resident logical pages)
#define FS_DEFAULT_PAGE_HASH_CAPACITY 4096
#define FS_DEFAULT_CANONICAL_SURFELS  4194304   // 16384 slabs = 128 MiB surfels + 32 MiB evidence
#define FS_DEFAULT_INDEX_LEAVES       1048576   // 64 MiB
#define FS_DEFAULT_INDEX_NODES        262144    // 8 MiB
#define FS_DEFAULT_RENDER_BLOCKS      65536     // 4 M render surfel copies = 128 MiB
#define FS_DEFAULT_RENDER_NODES       524288    // 40 MiB nodes + 4 MiB errors + 16 MiB child tables
#define FS_DEFAULT_DRAW_CAPACITY      1048576
#define FS_MAX_PAGES                  1024      // page table bound (descriptor arrays)
#define FS_MAX_ANCHORS                64
#define FS_PAGE_INITIAL_SLABS         8         // a page created for live scanning starts with 2048 surfels of room
#define FS_PAGE_RESERVE_SLABS         2         // the CPU keeps at least this many free slabs ahead of the cursor
#define FS_SLAB_WORDS_PER_PAGE        (FS_PAGE_MAX_SLABS)   // slab directory stride (u32 per page)

// ---- adaptive page-local index (contract §9.2, C09R): direct 32^3 directory + bounded leaves/nodes -----
#define FS_INDEX_LOOKUP_MAX     (FS_INDEX_CHAIN_MAX * FS_INDEX_LEAF_CAP)   // 56 handles per micro cell
#define FS_ASSOC_NEIGHBOURHOOD  8       // 2x2x2 cells around the measurement (drifted surfels near a boundary)
#define FS_ASSOC_CANDIDATE_MAX  128     // hard bound of candidates examined per measurement

// ---- staged fusion (contract §8.2, §8.3, §8.5, §8.6, §8.7; C09R-B) --------------------------------------
#define FS_TICK_MEAS_MAX      65536     // measurements per fusion epoch (= ring slot capacity)
#define FS_SORT_BITS          8         // LSD radix: 4 passes of 8 bits over 32-bit keys
#define FS_SORT_PASSES        4
#define FS_SORT_BLOCK         256       // keys per radix block (one workgroup)
#define FS_SORT_BLOCKS_MAX    (FS_TICK_MEAS_MAX / FS_SORT_BLOCK)   // 256
#define FS_SEG_REDUCE_MAX     256       // contributions folded per matched surfel segment (rest counted)
#define FS_SEG_CLUSTER_MAX    256       // measurements examined per unmatched cell segment (rest counted)
#define FS_CELL_NEW_MAX       8         // candidates created per cell per epoch (dedupe bound)
#define FS_ASSOC_SIGMA_GATE   3.0       // |signed plane distance| <= gate * sqrt(sigmaN_s^2 + sigmaN_m^2) -> same sheet
#define FS_ASSOC_MIN_DOT      0.8       // normal compatibility; opposite-facing = the other side of a thin wall
#define FS_ASSOC_TANGENT_K    2.0       // tangent distance <= max(k * (surfel radius + measurement footprint), FS_ASSOC_REACH_MIN_M)
#define FS_ASSOC_REACH_MIN_M  0.03      // C09R-E3: association tube (donor: kernel half support, not the pixel footprint); a re-observation
                                        // landing between 1 cm surfels refines the nearest one instead of seeding a new candidate
#define FS_TRANSIENT_TTL      64        // C09R-E3: an unpromoted candidate in a re-observed (dirty) cell without support for this many ticks is removed
#define FS_FREE_END_CLEARANCE_M 0.15    // C09R-E3 (donor MERKABA_FREE_FULL_CLEARANCE): free-space rays stop this far before the measured point;
                                        // the cell holding the hit is never stamped (run 23:52: ghosts = 55 % of new surfels = churn)
#define FS_PRED_ROW0_TOP      1         // C09R-E2: the previous rendered depth (Unity RT, GPU projection) stores row 0 at the TOP: prediction and
                                        // HZB scatter flip v; receipt: FS-MEAS consistent vs consistentAlt (the other convention)
#define FS_MOTION_BAND_M      0.05      // outside the sigma gate but within this band: motion evidence
#define FS_PROMOTE_STATIC     3         // DISTINCT consistent observations (frames, not pixels) before a transient candidate becomes canonical
#define FS_HUBER_K            1.345     // C09R-E4 robust update: contribution weight min(1, k * gate_sigma / |plane residual|)
#define FS_ASSOC_PLANE_MAX_M  0.08      // C09R-E4 topological bound of the plane gate: a broad depth-prior sigma never joins sheets farther apart (provisional)
#define FS_DEPTH_PRIOR_SIGMA_FLOOR_M 0.005  // C09R-E4 systematic floor of a depth-prior-only surfel (random noise averages, bias does not; C01 characterises it)
#define FS_RELOC_MAX          1024      // C09R-E4 relocation records per epoch (centre crossed its index cell); beyond: the position update waits (counted)
#define FS_GHOST_MOTION_MIN   3         // free-space contradictions before a candidate / weak surfel is removed
#define FS_GHOST_STATIC_K     2         // + staticEvidence / K contradictions for supported surfels
#define FS_SPLIT_VAR_K        4.0       // split when the residual std exceeds K x the MEASUREMENT sigma (normalised variance) and support >= FS_SPLIT_MIN_SUPPORT
#define FS_VAR_NORM_BASE      0.001     // log base of the normalised residual variance (varianceQ): 0.001 .. ~6e4 (E3b; run 00:12: 111 k splits against the fused sigma)
#define FS_SPLIT_MIN_SUPPORT  6
#define FS_MERGE_MIN_DOT      0.98      // coplanar within ~11 deg ...
#define FS_MERGE_OVERLAP_K    0.75      // ... centres closer than k * (rA + rB), plane distance inside the gate
#define FS_SIGMA_N_FLOOR_M    0.0003
#define FS_SIGMA_T_FLOOR_M    0.001
#define FS_RADIUS_MIN_M       0.003
#define FS_RADIUS_MAX_M       0.25
#define FS_FREE_STEP_M        0.0625    // free-space DDA step (half a cell)
#define FS_FREE_WALK_MAX      128       // 8 m at half-cell steps; bounded by the measurement range
#define FS_FREE_MAX_RANGE_M   6.0
#define FS_FREE_PAGE_HOPS_MAX 4         // page transitions (hash lookups) per ray; a 6 m ray crosses <= 3 page boundaries
#define FS_EPOCH_MEAS_MIN     4096      // slicing floor: an epoch never processes fewer than this many records
#define FS_EPOCH_OVER_K       1.25      // measured job GPU time > K x class quantum -> halve the epoch cap (slice)
#define FS_EPOCH_UNDER_K      0.5       // measured job GPU time < K x class quantum -> double the epoch cap
#define FS_EVIDENCE_COUNT_MAX 1023
#define FS_FUSE_FLAG_FREE_SPACE 1u      // PushFuse.flags: eye origins valid

// ---- render tree / publication (contract §11, §13.4; C09R-C) ----------------------------------------------
#define FS_RENDER_TREE_DEPTH   5        // 32^3 cells -> 16^3 -> 8^3 -> 4^3 -> 2^3 -> root (level 5)
#define FS_RENDER_LEAF_LEVELS  2        // a cell with > 64 surfels extends to <= 2 levels of 8 blocks (4096 surfels)
#define FS_DIRTY_CELLS_MAX     65536    // dirty (page,cell) entries per epoch
#define FS_DIRTY_NODES_MAX     65536    // COW nodes rebuilt per epoch (all levels)
#define FS_RETIRE_MAX          131072   // node/block ids released per epoch (freed after graphics retirement)
#define FS_PENDING_PUBLISH_MAX FS_MAX_PAGES
#define FS_COVERAGE_GRID       6
#define FS_COVERAGE_INSIDE_CELLS 32
#define FS_CLUSTER_FANOUT      8
#define FS_MORTON_BITS         15

// ---- cull (contract §13.5; C09R-D): frontier expansion through compact indirect queues --------------------
#define FS_CULL_FRONTIER_CAP      32768   // nodes per frontier level (fail-open: overflow emits aggregates)
#define FS_CULL_LEVELS            8       // page root .. leaf blocks (tree depth 5 + 2 leaf levels + block)
#define FS_CULL_BLOCK_LIST_CAP    65536   // leaf blocks emitted per frame
#define FS_CULL_FRAME_RING        4
#define FS_CULL_STATS_WORDS       32
#define FS_HZB_SIZE               256
#define FS_HZB_LEVELS             9
#define FS_HZB_SRC_BLOCK          4
#define FS_HZB_MARGIN_MIN_M       0.10
#define FS_HZB_GRAD_K             2.0
#define FS_HZB_LOWCONF_RANGE_M    0.35
#define FS_HZB_AGREE_K            1.5     // env depth contributes only within K * margin of the scan depth
#define FS_FOVEA_DEG              5.0
#define FS_PERIPHERY_DEG          35.0
#define FS_HEADROOM_BIAS_MAX      4.0
#define FS_HEADROOM_BIAS_PER_US   0.001
#define FS_EYE_WIDTH_PX_DEFAULT   1680.0
#define FS_RENDER_MODE_SCAN       0
#define FS_RENDER_MODE_XRAY       1
#define FS_RENDER_MODE_PLAN       2

// ---- GPU counter words (gctr buffer, host-visible; the sched tick folds deltas into FsCounter / telemetry) --
#define FS_GCTR_PAGE_LOOKUPS       0
#define FS_GCTR_PAGE_MISSES        1
#define FS_GCTR_ASSOC_RECORDS      2
#define FS_GCTR_ASSOC_MATCHED      3
#define FS_GCTR_ASSOC_UNMATCHED    4
#define FS_GCTR_SEGMENTS_MATCHED   5      // unique target surfels
#define FS_GCTR_CONTRIBUTIONS      6      // contributions reduced
#define FS_GCTR_SEG_OVERFLOW       7      // contributions beyond FS_SEG_REDUCE_MAX / FS_SEG_CLUSTER_MAX
#define FS_GCTR_NEW_SURFELS        8      // created after dedupe
#define FS_GCTR_NEW_SHORTFALL      9      // refused: page had no free slab
#define FS_GCTR_SPLITS             10
#define FS_GCTR_MERGES             11
#define FS_GCTR_GHOSTS             12
#define FS_GCTR_CANDIDATE_OVERFLOW 13
#define FS_GCTR_INDEX_LEAF_SPLITS  14
#define FS_GCTR_INDEX_OVERFLOW     15     // max depth + chain exhausted
#define FS_GCTR_DIRTY_CELLS        16
#define FS_GCTR_COW_NODES          17
#define FS_GCTR_RENDER_BLOCKS      18
#define FS_GCTR_ROOTS_PENDING      19
#define FS_GCTR_MEAS_OUT_OF_RANGE  20
#define FS_GCTR_DIRTY_OVERFLOW     21
#define FS_GCTR_POOL_LEAF_EMPTY    22     // allocation failed: index leaf pool
#define FS_GCTR_POOL_NODE_EMPTY    23
#define FS_GCTR_POOL_RBLOCK_EMPTY  24
#define FS_GCTR_POOL_RNODE_EMPTY   25
#define FS_GCTR_FREE_STAMPS        26
#define FS_GCTR_SEGMENTS_UNMATCHED 27     // owner-cell segments (unmatched)
#define FS_GCTR_FREE_HOP_OVERFLOW  28     // rays that hit the page-hop bound
#define FS_GCTR_RELOCATIONS        29     // index relocations applied (surfel centre moved to another cell, SurfaceID kept)
#define FS_GCTR_NEXT_SURFACE_ID    30     // deterministic id base for the epoch (CPU advances it by the epoch's total)
#define FS_GCTR_RELOC_DEFERRED     31     // relocations refused by FS_RELOC_MAX (position update deferred to a later observation)
#define FS_GCTR_COUNT              32

// ---- fusion scratch layout (u32 words in the `tick` buffer; per epoch) -------------------------------------
#define FS_T_COUNT           0          // measurements in the epoch (GPU-written from the ring counters)
#define FS_T_SORTED          1          // records after key filtering (== count)
#define FS_T_SEG_MATCHED     2
#define FS_T_SEG_UNMATCHED   3
#define FS_T_NEW_TOTAL       4          // exclusive prefix total of new candidates (deterministic ids)
#define FS_T_DIRTY_COUNT     5
#define FS_T_RETIRE_COUNT    6
#define FS_T_PENDING_COUNT   7
#define FS_T_COW_COUNT       8          // dirty nodes at the current level (per-level indirect args reuse)
#define FS_T_RELOC_COUNT     9          // relocation records (written by fuse_reduce / fuse_maint_apply, consumed and zeroed by world_relocate)
#define FS_T_ARGS_MEAS       16         // {ceil(count/64), 1, 1, 0}
#define FS_T_ARGS_SORTBLK    20         // {blocks, 1, 1, 0}
#define FS_T_ARGS_DIRTY      24         // {ceil(dirty/64), 1, 1, 0}
#define FS_T_ARGS_COW        28         // {ceil(cowNodes/64), 1, 1, 0}
#define FS_T_ARGS_PENDING    32         // {ceil(pending/64), 1, 1, 0}
#define FS_T_LEVEL_ARGS      40         // 6 x {groups, 1, 1, count}: indirect args + entry count of the dirty list per render-tree level 1..5 (index by level)
#define FS_T_IDX_LEAF_BASE   72         // u32 word offsets of the leaf / node regions inside the index arena (pool sizes are runtime)
#define FS_T_IDX_NODE_BASE   73
#define FS_T_ID_BASE         74         // deterministic SurfaceID base of the epoch
#define FS_T_TICK            75
#define FS_T_PAGE_COUNT      76
#define FS_T_WORDS           96
#define FS_CELL_RENDER_MAX   4096       // surfels published per cell (64 blocks); beyond = coverage aggregate only (counted)
#define FS_POOL_HEADER_WORDS 8          // per free-id ring: head, tail, mask, failures, ringBase, cap, pad, pad
#define FS_POOL_COUNT        4

// ---- cull stats words (device -> host ring) -----------------------------------------------------------
#define FS_CSTAT_FRAME            0
#define FS_CSTAT_PAGES_TESTED     1
#define FS_CSTAT_PAGES_CULLED     2
#define FS_CSTAT_PAGES_REUSED     3
#define FS_CSTAT_ROOTS_VISITED    4
#define FS_CSTAT_FRONTIER_NODES   5
#define FS_CSTAT_NODES_FRUSTUM    6
#define FS_CSTAT_NODES_CONE       7
#define FS_CSTAT_NODES_HZB        8
#define FS_CSTAT_NODES_IN_BAND    9
#define FS_CSTAT_HZB_UNCERTAIN    10
#define FS_CSTAT_AGGREGATES       11
#define FS_CSTAT_BLOCKS           12
#define FS_CSTAT_DRAW_RECORDS     13
#define FS_CSTAT_BUDGET_DROPPED   14
#define FS_CSTAT_FRONTIER_OVERFLOW 15
#define FS_CSTAT_VISIBLE_SURFELS  16
#define FS_CSTAT_REPRESENTED      17     // canonical surfels represented by the cut (leaves + aggregates)
#define FS_CSTAT_HZB_TILES        18
#define FS_CSTAT_HZB_KEEP         19
#define FS_CSTAT_HZB_REJECT       20

#endif // FS_WORLD_PARAMS_H
