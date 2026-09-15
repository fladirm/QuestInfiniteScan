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
// ---- E6R evidence hysteresis (1/8 debt units; provisional numbers, device-calibrated later) ------------------------------------------
#define FS_LIFE_ACTIVE        0u
#define FS_LIFE_SUSPECT       1u
#define FS_LIFE_RETIRING      2u
#define FS_DEBT_W_PRECISE     8         // a contradicting stereo / temporal ray: one unit
#define FS_DEBT_W_PRIOR       4         // a contradicting Env Depth prior / planar ray: half a unit
#define FS_DEBT_MAX_PER_OBS   16        // at most two units charged per observation, however many rays
#define FS_DEBT_HEAL          16        // a positive observation removes two units of debt
#define FS_DEBT_TRANSIENT     8         // an unpromoted candidate dies after one unit (building stays quick, noise dies quick)
#define FS_DEBT_SUSPECT       24        // promoted: ACTIVE -> SUSPECT at three units
#define FS_DEBT_RETIRE        64        // SUSPECT -> RETIRING at eight units, >= FS_RETIRE_MIN_VIEWS contradiction sectors, persistence, neighbours
#define FS_DEBT_REMOVE        96        // RETIRING -> REMOVED at twelve units without a positive observation
#define FS_SUSPECT_MIN_TICKS  30        // SUSPECT must persist this many scan ticks before RETIRING
#define FS_RETIRE_MIN_VIEWS   2
#define FS_RETIRE_NEIGHBOUR_FRACTION 0.5  // spatial consistency: at least this share of the graph neighbours are SUSPECT / RETIRING
#define FS_REFINE_STREAK      2         // refinement: unexplained residual in this many consecutive observations
#define FS_FREE_GRAZE_COS     0.15      // narrow phase: rays more grazing than this never contradict a support
// ---- E6R persistent surface topology --------------------------------------------------------------------------------------------
#define FS_TOPO_VERTEX_CAP      262144   // topology vertex records (promoted surfels in faces); pooled
#define FS_TOPO_VERTEX_WORDS    32       // header 7 + FS_TOPO_FACES x FS_TOPO_FACE_WORDS
#define FS_TOPO_HEADER_WORDS    7        // sheet index, flags, label, label generation, SurfaceID guard, incident faces, label stable count
#define FS_TOPO_FACES           5        // faces owned by one vertex (owner = minimum SurfaceID of the face)
#define FS_TOPO_FACE_WORDS      5        // b packed, c packed, SurfaceID b, SurfaceID c, meta (state | bridge evidence | last tick | attempts)
#define FS_SHEET_REC_CAP        65536    // sheet records (union-find); pooled
#define FS_SHEET_REC_WORDS      8        // sheetId, parent, vertices, faces, area mm2, refs, relabel generation, min label
#define FS_TOPO_DELTA_WORDS     12       // per batch node: flags, 5 x (b packed, c packed), hole receipt
#define FS_TOPO_FREE_MAX        16384    // released ids per topology job (host-visible)
#define FS_TEMPORAL_TARGETS     256      // C11R2 targets per topology job
#define FS_TEMPORAL_TARGET_WORDS 8       // world xyz, normal oct, sigmaN, SurfaceID, positive support, pad
#define FS_TEMPORAL_SIGMA_TARGET_M 0.004 // a face vertex whose sigmaN is above this still needs precision (temporal target)
#define FS_FACE_MAX_GAP_RAD     2.618    // a fan gap wider than 150 deg is a boundary (no face across it)
#define FS_FACE_MAX_CIRCUM_M    0.05     // no face with a circumradius beyond 5 cm (openings, leaf gaps)
#define FS_FACE_LIFT_M2         1e-7     // in-circle predicate: deterministic SurfaceID lift (m^2) breaks cocircular ties identically for every owner
#define FS_FACE_FOLD_COS        0.5      // face normal vs vertex normals below this: folded (overlap receipt)
#define FS_BRIDGE_MIN_SUPPORT   6        // a face joining two sheets needs this positive support on all three vertices
#define FS_BRIDGE_EVIDENCE      3        // ... and this many derivations in distinct observation ticks before the union
#define FS_BRIDGE_FREE_VERTICES 16       // a sheet this small is still growing: a face joining it to another sheet unions at once
#define FS_BRIDGE_ATTEMPTS      8        // a pending bridge re-marks its owner at most this many times without new evidence
#define FS_SPLIT_CONFIRM        3        // a vertex whose component label stays above its sheet's minimum this many passes splits off
#define FS_FACE_PENDING         0u       // face states (meta bits 0..1)
#define FS_FACE_ACTIVE          1u
#define FS_FACE_SUSPECT         2u
#define FS_TOPO_FLAG_INTERIOR   1u       // vertex flags: fan closed
#define FS_TOPO_FLAG_BOUNDARY   2u
#define FS_TRI_OFFSET_RANGE_M   0.128    // render copy: triangle vertex offsets from the owner vertex, 10 bits signed per axis
#define FS_PROMOTE_STATIC     3         // DISTINCT consistent observations (frames, not pixels) before a transient candidate becomes canonical
#define FS_HUBER_K            1.345     // C09R-E4 robust update: contribution weight min(1, k * gate_sigma / |plane residual|)
#define FS_ASSOC_PLANE_MAX_M  0.08      // C09R-E4 topological bound of the plane gate: a broad depth-prior sigma never joins sheets farther apart (provisional)
#define FS_DEPTH_PRIOR_SIGMA_FLOOR_M 0.005  // C09R-E4 systematic floor of a depth-prior-only surfel (random noise averages, bias does not; C01 characterises it)
// ---- C09R-E4.1R surface complex core (provisional numbers). Canonical rM/rm = statistical support only; the SurfaceGraph is
// derived and persistent per surfel handle; coverage (readout) is derived from mutual edges, never grown into the canonical.
#define FS_SHEET_K            8         // proposed neighbours per node (true metric top-K of the ball query; E6R: 8 for the local triangulation)
#define FS_SHEET_LINK_R_M     0.06      // ball query radius / maximum edge length
#define FS_SHEET_CAND_MAX     384       // candidates examined per ball query (counted beyond)
#define FS_SHEET_MIN_DOT      0.9       // edge: normals within ~26 deg (a 90 deg corner never connects)
#define FS_SHEET_PLANE_MAX_M  0.02      // edge: signed plane distance bound in the mean-normal frame (no depth discontinuity)
#define FS_SHEET_FLAT_K       1.5       // flat when the mutual ring's plane RMS <= K x its mean sigma (else curvature is preserved)
#define FS_SHEET_BLEND        0.5       // flat sheet: fraction of the fitted normal offset / normal applied per graph update
#define FS_SHEET_COVER_MAX_M  0.05      // derived coverage radius bound (readout only)
#define FS_SHEET_MOVE_MIN_M   0.002     // reduce marks a promoted surfel graph-dirty when its centre moved more than this ...
#define FS_SHEET_TURN_MIN_COS 0.9994    // ... or its normal turned more than ~2 deg (converged in-plane updates cost no graph work)
#define FS_SHEET_RING_CAP     262144    // graph-dirty ring (power of two; dedupe bit per handle)
#define FS_SHEET_BATCH_MAX    4096      // graph-dirty surfels processed per publication (bounded work)
// ---- C09R-E4.1C adaptive coarsen / refine (provisional numbers)
#define FS_CONTRACT_MIN_STATIC  8       // both ends of a contracted edge are converged (distinct observations)
#define FS_CONTRACT_MIN_DEGREE  3       // the loser is an interior node (boundary / detail nodes never contract)
#define FS_CONTRACT_DIST_MAX_M  0.035   // edge length bound of a contraction
// Ecollapse(a -> b) = |survivor offset from a's fitted ring plane| / ring sigma      (plane error after contraction)
//                  + W_CURV x ring plane RMS / ring sigma                          (curvature the ring loses)
//                  + W_NORMAL x (1 - dot(n_b, n_fit))                              (normal error)
//                  + W_BOUNDARY x (FS_SHEET_K - mutual degree)                     (boundary penalty)
//                  + W_COLOR x |measured colour a - measured colour b|             (appearance / edge penalty)
#define FS_CONTRACT_BUDGET      2.0     // local accuracy budget
#define FS_CONTRACT_W_CURV      0.5
#define FS_CONTRACT_W_NORMAL    20.0
#define FS_CONTRACT_W_BOUNDARY  0.25
#define FS_CONTRACT_W_COLOR     2.0
#define FS_REFINE_OUTLIER_K     3.0     // a contribution beyond K x the combined sigma is surface error the surfel does not explain
#define FS_REFINE_MIN_OUTLIERS  3       // ... in one observation, at least this many
#define FS_REFINE_MIN_SUPPORT   4       // ... on a surfel with this much support: insert a site at the worst residual
#define FS_REFINE_BIRTHS_MAX    256     // site insertions per epoch (bounded sequential pass)
#define FS_SHEET_NODE_WORDS   11        // per handle: nbr[8] (page << 22 | handle), meta (degree), topology vertex record id, contraction generation stamp
#define FS_RELOC_MAX          1024      // C09R-E4 relocation records per epoch (centre crossed its index cell); beyond: the position update waits (counted)
#define FS_VAR_NORM_BASE      0.001     // log base of the normalised residual variance (varianceQ): 0.001 .. ~6e4 (E3b; run 00:12: 111 k splits against the fused sigma)
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
#define FS_EPOCH_MEAS_MIN     512       // slicing floor (C09R-E5: 1-3 ms scanner quanta; stride slices of one depth frame)
// ---- C09R-E5R deadline / deficit scheduler and cost-model floors ------------------------------------------------------------------
#define FS_SCHED_FUSE_DEADLINE_MS    60    // E6R: newest usable depth observation waiting longer than this: fusion is overdue (15-20 fused observations/s)
#define FS_SCHED_PUBLISH_DEADLINE_MS 75    // oldest pending dirty cell older than this: publication work is overdue (acceptance p95 < 100 ms)
#define FS_SCHED_SHEET_DEADLINE_MS   200   // oldest pending SurfaceGraph node older than this: sheet work is overdue (acceptance p95 < 250 ms)
#define FS_SCHED_SHARE_FUSE          0.60  // GPU-time shares of the deficit round robin when nothing is overdue
#define FS_SCHED_SHARE_PUBLISH       0.25
#define FS_SCHED_SHARE_SHEET         0.15
#define FS_SCHED_DEFICIT_CAP_US      20000 // an idle stage cannot bank more than this
#define FS_SCHED_AGE_UNKNOWN_MS      1000  // enqueue serial outside the host serial-time window: treated as this old
#define FS_SCHED_SERIAL_WINDOW       65536 // host ring work serial -> steady-clock ns (power of two)
#define FS_PUB_DIRTY_MIN      16        // C09R-E5 publication chain floors: dirty cells per maintenance job,
#define FS_PUB_LEAVES_MIN     16        //   dirty cells per leaves chunk (PUBLISH class quantum, sub-ms)
#define FS_SHEET_BATCH_MIN    256       // C09R-E5 surface complex batch floor (own job, quantum gated)
#define FS_EVIDENCE_COUNT_MAX 1023
#define FS_FUSE_FLAG_FREE_SPACE 1u      // PushFuse.flags: eye origins valid

// ---- render tree / publication (contract §11, §13.4; C09R-C) ----------------------------------------------
#define FS_RENDER_TREE_DEPTH   5        // 32^3 cells -> 16^3 -> 8^3 -> 4^3 -> 2^3 -> root (level 5)
#define FS_RENDER_LEAF_LEVELS  2        // a cell with > 64 surfels extends to <= 2 levels of 8 blocks (4096 surfels)
#define FS_DIRTY_CELLS_MAX     65536    // dirty (page,cell) entries per publication batch
#define FS_DIRTY_RING_CAP      262144   // C09R-E5R persistent dirty-cell ring (power of two, 2 words per entry: page<<15|cell, enqueue tick)
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
#define FS_RENDER_MODE_GEOMETRY   3     // E4.1R geometry debug: SCAN culling, every promoted surface neutral grey (no appearance gating)

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
#define FS_GCTR_SPLITS             10     // retired in C09R-E4.1C (major-axis split replaced by refinement births, FS_GCTR_REFINE_BIRTHS); stays 0
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
#define FS_GCTR_SHEET_EDGES        32     // E4.1R: mutual edges seen by the fit (directed: each unordered edge counts once per updated end)
#define FS_GCTR_SHEET_NODES        33     // graph nodes updated (fit)
#define FS_GCTR_CROSS_PAGE_EDGES   34     // mutual edges whose ends live in different pages
#define FS_GCTR_COVER_OVERLAP_MM2  35     // derived coverage overlap area along mutual edges (mm^2, directed sum)
#define FS_GCTR_COVER_HOLE_MM2     36     // derived coverage hole area along mutual edges (mm^2, directed sum)
#define FS_GCTR_PLANE_RMS_UM       37     // sum over updated nodes of the ring plane RMS (um)
#define FS_GCTR_NORMAL_RMS_MDEG    38     // sum over updated nodes of |normal - fitted normal| (millidegrees)
#define FS_GCTR_SHEET_FRONTIER_DROP 39    // graph-dirty marks refused by the ring bound
#define FS_GCTR_SHEET_SMOOTHED     40     // nodes pulled onto their flat local fit
#define FS_GCTR_EDGE_CONTRACTIONS  41     // E4.1C graph edge contractions
#define FS_GCTR_REFINE_BIRTHS      42     // E4.1C refinement births
#define FS_GCTR_SHEET_CAND_OVERFLOW 43    // ball queries that hit FS_SHEET_CAND_MAX
#define FS_GCTR_PROMOTIONS         44     // C09R-E5R surfels promoted (FRONT/BACK ratio receipt: promoted live = promotions - promotedRemoved)
#define FS_GCTR_PROMOTED_REMOVED   45     // promoted surfels removed (ghost, merge, contraction, erase)
#define FS_GCTR_DIRTY_RING_DROP    46     // C09R-E5R dirty-cell ring full: the mark was refused (must stay 0)
#define FS_GCTR_POSITIVE_EVIDENCE  47     // E6R positive observations applied
#define FS_GCTR_CONTRADICTIONS     48     // debt charges (one per surfel per observation)
#define FS_GCTR_CONTRA_HEALED      49     // positive observations that reduced debt
#define FS_GCTR_SURFEL_SUSPECT     50     // ACTIVE -> SUSPECT
#define FS_GCTR_SURFEL_RECOVERED   51     // SUSPECT / RETIRING -> healthier state by positives
#define FS_GCTR_SURFEL_RETIRED     52     // promoted RETIRING -> REMOVED
#define FS_GCTR_FREE_NARROW_HITS   53     // free-space rays that crossed a surfel support (narrow phase)
#define FS_GCTR_ACTIVE_SHEETS      54     // E6R topology receipts: live sheet roots (gauge maintained by the commit pass)
#define FS_GCTR_SHEET_UNIONS       55
#define FS_GCTR_SHEET_SPLITS       56
#define FS_GCTR_ACTIVE_FACES       57     // gauge: live faces (ACTIVE + SUSPECT)
#define FS_GCTR_FACES_CREATED      58
#define FS_GCTR_FACES_RETIRED      59
#define FS_GCTR_FACES_SUSPECT      60     // face transitions to SUSPECT
#define FS_GCTR_FACES_RECOVERED    61
#define FS_GCTR_BOUNDARY_VERTICES  62     // topology passes that found the vertex on a boundary (fan not closed)
#define FS_GCTR_FACE_DUP_REFUSED   63     // a face already present in the owner's list was proposed again (kept, not duplicated)
#define FS_GCTR_FACE_OWNER_VIOL    64     // a face whose owner is not the minimum SurfaceID (must stay 0)
#define FS_GCTR_MESH_HOLE_MM2      65     // interior fan wedges refused (Delaunay / circumradius) area sum
#define FS_GCTR_MESH_OVERLAP_MM2   66     // folded faces (normal disagrees with the vertex normals) area sum
#define FS_GCTR_TOPO_VERTICES      67     // gauge: vertices with a topology record
#define FS_GCTR_TOPO_POOL_EMPTY    68     // topology / sheet pool exhausted (counted, never silent)
#define FS_GCTR_BRIDGES_PENDING    69     // bridge faces waiting for repeated evidence (derivations)
#define FS_GCTR_FACE_OVERFLOW      70     // owned faces beyond FS_TOPO_FACES (counted)
#define FS_GCTR_TEMPORAL_TARGETS   71     // uncertain surface vertices exported for C11R2 refinement
#define FS_GCTR_CONTRACT_UNMATCHED 72     // contraction proposals refused by the matching (vertex already used this generation)
#define FS_GCTR_COUNT              73

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
#define FS_T_SHEET_HEAD      10         // graph-dirty ring: consumer head (sheet_begin)
#define FS_T_SHEET_TAIL      11         // graph-dirty ring: producer tail (atomic)
#define FS_T_SHEET_BEGIN     12         // this publication's batch: first ring index
#define FS_T_SHEET_COUNT     13         // this publication's batch size
#define FS_T_SHEET_MASK_BASE 14         // word offsets inside the sheet buffer (CPU-written, depend on the surfel capacity)
#define FS_T_SHEET_RING_BASE 15
#define FS_T_DIRTY_HEAD      78         // C09R-E5R persistent dirty-cell ring: consumer head (CPU, publication maintenance take)
#define FS_T_WORK_SERIAL     80         // E6R scheduler clock: serial of the world job being recorded (any class); rings store it, the host maps serial -> steady ns
#define FS_T_EYE0            81         // E6R: 3 words float bits, anchor-local eye origins of the fuse epoch (view sectors)
#define FS_T_EYE1            84
#define FS_T_TOPO_GEN        87         // E6R topology generation (one per topology job): contraction matching stamps
#define FS_T_TARGET_COUNT    88         // E6R temporal refinement targets written by the commit pass (host-visible)
#define FS_T_TOPO_FREE_COUNT 89         // E6R topology / sheet ids released by the commit pass (host-visible list)
#define FS_T_DIRTY_TAIL      79         //   producer tail (atomic, fsMarkDirtyCell)
#define FS_T_REFINE_BIRTHS   77         // E4.1C refinement births of the epoch (deterministic ids after the candidates)
#define FS_T_RELOC_COUNT     9          // relocation records (written by fuse_reduce / fuse_maint_apply, consumed and zeroed by world_relocate)
#define FS_T_ARGS_MEAS       16         // {ceil(count/64), 1, 1, 0}
#define FS_T_ARGS_SORTBLK    20         // {blocks, 1, 1, 0}
#define FS_T_ARGS_DIRTY      24         // {ceil(dirty/64), 1, 1, 0}
#define FS_T_ARGS_COW        28         // {ceil(cowNodes/64), 1, 1, 0}
#define FS_T_ARGS_SHEET      36         // {ceil(sheetCount/64), 1, 1, 0}
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
#define FS_POOL_COUNT        6          // + E6R topology vertex records, sheet records

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
