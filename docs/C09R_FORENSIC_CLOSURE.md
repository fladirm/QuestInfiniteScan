# C09R — forensic world-core closure

Audited commit: `075e3711237f2452a19c470a682a99f48e807b69` (branch `finalscan`). Every defect below was
verified against the source of that commit (file:line) and, where a device run exists, against its
receipts (`/mnt/kingston-unity/Builds/FinalScan/evidence/live-20260914T190219Z` = APK of the pre-075e371
tree + fusion, `live-20260914T191458Z` = APK of 075e371). Nothing here relies on the commit message.

Authority: `FINALSCAN_V4_CONTRACT.md` (§1.0, §8, §9, §11, §12, §13.4, §15, §20, §21). The C09R execution
rule: replace defective mechanisms, one canonical implementation, no compatibility fallback.

## 1. Observed defects (verified)

| # | Defect | Source (075e371) | Evidence / consequence |
|---|---|---|---|
| A | Fusion is data-racy: an invocation reads `surfels[best]`/`evidence[best]`, computes, and rewrites the whole records; `lastSeenFrame == tick` is a read, not an ownership claim. Two measurements of one surfel in one tick both pass the test and last-writer-wins. | `Native~/shaders/world/world_integrate.comp:147,179,183`; motion evidence RMW `:193` | Result depends on invocation order; a second observation of the same surfel in a tick is discarded (`FS_CTR_DUPLICATE_OBSERVATIONS` = 1.2 M in run 19:02 vs 28 k updates) instead of fused. |
| B | Insert publishes a broken list: `atomicExchange(head, g)` then `next[g] = old`. A concurrent traversal sees the new head with an uninitialised link. The later duplicate re-scan walks that list. | `world_integrate.comp:216-217` | Undefined traversal; the duplicate guard cannot repair it. |
| C | Canonical surfels are the render FRONT buffer: every slot reserves BACK + FRONT0 + FRONT1 (`FS_RANGES_PER_SLOT 3`); publish copies whole pages; the renderer reads canonical copies. | `fs_world_params.h:21`, `fs_world.cpp` CreateArenas (`surfelCursor += capacity * FS_RANGES_PER_SLOT`) | 755 MB of surfel arenas + 151 MB evidence + 99 MB index at start (`FS-WORLD arenas` log line, both runs). |
| D | Fixed page capacity: 256 standard slots × 16 384 + 16 BIG slots × 262 144; a page at 85 % is copied whole into a BIG slot (`MigrateToBig`). | `fs_world_params.h:19-20`, `fs_world.cpp:299-342` | Run 19:02: front stuck at 2 × 16 384 (no migration yet); run 21:15: three whole-page migrations in the first seconds, BIG slot ceiling 262 144 still hard. |
| E | Page-wide publish compilation: one 22-dispatch job does collect → evidence → merge → publish count/scan/copy → root commit → FRONT→BACK compaction → full index rebuild → full ClusterTree rebuild for every dirty page. | `fs_world.cpp` SubmitMaintenance | O(all surfels in page) per change. Run 21:15 receipt `FS-WORLD receipt #2`: 128/128 sampled FRONT surfels at the page corner (2,2,-2) with radius 0.5 mm = zero records published by the count/scan/copy path (prefix/copy mismatch); they were then copied into BACK and re-indexed, i.e. the compilation path fabricated canonical geometry. |
| F | Root flip on the scanner queue: `world_publish_commit.comp` writes `roots[slot]` directly; the renderer reads it in the next FRAME_BEGIN with only a class-timeline acquire; lifetime of the previous parity is "`FS_PUBLISH_MIN_FRAME_GAP` = 3 frames". | `world_publish_commit.comp:39`, `fs_world_params.h:122` | No proof that Unity's reader retired; a parity can be overwritten while a recorded frame still reads it. |
| G | Eviction/migration release slots on `GpuIdle()` (scan/maintenance/erase counts == 0), not on graphics retirement; roots are zeroed and headers cleared immediately. | `fs_world.cpp:225,230,278,342,531` | A frame in flight can read a zeroed root / reused slot (page pop, ABA). |
| H | Flat cull: every cluster node of every visible page is an item of the node dispatch (`items = layout.total`); hidden branches are enumerated. | `render_cull_pages.comp:41`, `render_cull_nodes.comp:75` | Run 19:02: `render.cull` 6 ms avg / 14 ms max at 16–32 k visible surfels (receipt stages). Cost grows with canonical node count, violating §1.0/§13.4. |
| I | HZB combine takes `min(prevDepth, envDepth)` whenever both exist: Environment Depth disagreement can hide correct scan geometry. | `render_hzb_combine.comp:20` | Fails closed instead of open (§13.5 rule: never cull the observed band). |
| J | Free-space ray is clamped to the page cube containing the hit; intermediate pages never receive free-space evidence. | `world_integrate.comp` `fsFreeSpaceRay` slab clamp | A background surface seen through a stale page cannot invalidate it. |
| K | Evidence sidecar has the same races as geometry (motion evidence RMW on `motionBest`, static evidence in the fused write). | `world_integrate.comp:190-194` | Non-deterministic evidence. |
| L | Observation identity = low 32 bits of `xrTimeNs`. | `measure.cpp:172` | Time and identity conflated; wraps every 4.29 s of ns. |
| M | RESET WORLD = `FsHost_Shutdown` + `FsHost_Init` (full executor teardown, `vkDeviceWaitIdle`, pipeline re-creation). | `Runtime/Host/FinalScanHost.cs:163` | Run 19:02: after reset the world could not rebind pipelines (fixed in 075e371 by dropping pipelines on teardown) — the operation itself remains a device teardown. |
| N | No-timeline fallback keeps the separate scanner queue and orders cross-queue work by CPU bookkeeping only. | `executor.cpp:1043` | Not a synchronisation guarantee. |
| O | Warm-up: a step registered after warm-up completed runs on the calling thread while status stays READY. | `executor.cpp:961` | Scan-time compile possible (run 19:02 before the AutoInit fix: 413 ms + 1235 ms on the main thread after READY). |
| P | HZB build cost: run 21:15 receipts `render.hzb` 400 ms / 366 ms per build with `hzbTiles=0`; `FS-HOST-CS cullGpuUs` 336–346 ms every 2 s; VrApi `FPS=3/72, GPU%=1.00, App≈320 ms`. | `render_hzb_scatter.comp` (shared-window version), `render_hzb_mips.comp`, `RecordHzb` | The HZB path alone breaks 72 Hz; the receipt cannot attribute it to a single dispatch (one stage for three dispatches). |
| Q | Measurement receipt run 21:15 `FS-MEAS receipt #1`: 512 samples, distance from eye 0.13–0.23 m (headset lying on the desk at launch); linearisation `z = near/(1-d)` for infinite far matches Meta's official `EnvironmentDepthUtils.ComputeNdcToLinearDepthParameters` (`x=-2·near, y=-1`, `linear = x/(2d-1+y)`). Not a defect; recorded so the semantics are no longer "unverified". | Meta XR SDK core `Scripts/EnvironmentDepth/EnvironmentDepthUtils.cs`, `Shaders/EnvironmentDepth/EnvironmentOcclusion.cginc:41` | — |
| R | Render kernels copy the per-frame block by value (`FsCullFrame F = frames[slot]`, 4768 B incl. 64 anchor matrices, dynamically indexed) — one private-memory copy per thread. | `render_hzb_scatter.comp:27`, `render_cull_*.comp` (same idiom in 075e371 `render_cull_nodes.comp`) | Root cause of P: acceptance run 1 (d31744b) `render.hzb.scatter` 370–420 ms per build with `hzbTiles=0` and no atomics, `cull.blocks` up to 8.9 ms for ≤ 4 k records; the ~750 k scatter threads moved gigabytes of private memory per build. Fixed in C09R-G: every field read in place from the SSBO. |

Receipts summary (run 21:15, APK of 075e371): `FS-WORLD` migrations 3 whole pages within 20 s, front
count 278 k → 786 k (BIG slots), `visibleSurfels` 0 for the entire run (cull never produced records while the
HZB path saturated the GPU), `frameMs` 350–430 ms.

## 2. Replacement architecture (what C09R builds)

Canonical geometry exists once; derived data are sparse, immutable, COW; every mutation has exactly one
writer per epoch; publication happens in graphics ordering; memory reuse is proven by retirement.

### 2.1 Segmented canonical pools (C09R-A)
* `CanonicalSurfelPool` / `EvidencePool`: one global `FsSurfel[]` / `FsSurfelEvidence[]` arena addressed by
  a 32-bit **surfel handle** = `slab * FS_SLAB_SURFELS + i`. Slabs (256 surfels = 8 KiB) come from a
  host-managed free list; a logical page owns an ordered list of slabs (`PageSlabDirectory`, bounded per
  page), page-local index `k*256+i` ↔ handle through the directory. Growth = allocate a slab. No BIG slots,
  no migration, no per-page capacity ceiling other than the pool.
* `IndexLeafPool` / `IndexNodePool`: per resident page a 32³ direct cell directory (u32 per cell:
  EMPTY | LEAF(id) | NODE(id)); leaf = bounded bucket of `FS_INDEX_LEAF_CAP` handles; node = 2×2×2 children;
  up to `FS_INDEX_MAX_DEPTH` levels (12.5 cm → 1.56 cm). Lookup walks ≤ depth + one leaf. A cell subtree is
  rewritten only by the owner of that cell in an epoch (see 2.2), never by concurrent atomics.
* Memory governor: every pool has a byte budget from `PERFORMANCE_BUDGET.md`, committed/live telemetry,
  and cold-page slab eviction; nothing grows silently.

### 2.2 Staged single-writer fusion (C09R-B)
Per scan tick (≤ `FS_TICK_MEAS_MAX` measurements, sliced into jobs that fit their quantum):
* F0 associate (read-only against the tick's immutable canonical/index generation): per measurement the
  owner page/cell, the best compatible surfel handle (2×2×2 neighbourhood of leaves, bounded), score, and
  the free-space ray extent. Output `AssociationRecord[]`.
* F1 stable LSD radix sort of records by 32-bit key (matched: handle; unmatched: `1<<31 | pageSlot<<15 | cell`),
  payload = measurement index. Segment starts flagged.
* F2 segmented reduce: one invocation per matched segment folds all contributions of the tick into one
  precision-weighted update (position, normal, tangent orientation + anisotropic support from the
  contribution scatter, sigma, residual variance, evidence, static/motion) and writes the surfel and its
  evidence once. Deterministic for a given stream.
* F3 unmatched clustering per owner cell (one invocation per cell segment, bounded): cluster compatible
  measurements, dedupe, count justified candidates; exclusive prefix over cell segments gives deterministic
  SurfaceIDs (`idBase + prefix`) and page-local slot offsets; a page's slab headroom is reserved by the CPU
  before the tick (shortfall counted, served next tick). F3b writes the new surfels and inserts them into
  their owner cell subtree (single owner per cell).
* F4 local maintenance on touched cells only: deterministic merge (priority: static evidence, lower
  uncertainty, lower SurfaceID), explicit split when residual variance/anisotropy no longer fit one surfel,
  ghost removal from free-space evidence. Free space: page-aware DDA from the eye to the hit across pages,
  idempotent stamp per cell (order-independent, one deterministic value per tick).

### 2.3 Sparse immutable readout (C09R-C)
* `RenderLeafPool`: immutable blocks of ≤ 64 render surfels (copies of canonical at publication);
  `RenderNodePool`: persistent octree over the 32³ cells (depth 5) with `FsClusterNode` + errors. A changed
  cell → new leaf block(s) + new leaf node + its ≤ 5 ancestors; unchanged subtrees are shared.
* Publication: the scanner job writes `PendingPublish[] {page, newRoot, rootWord}` and signals its timeline;
  FRAME_BEGIN on Unity's command stream applies the descriptors (`render_publish_roots.comp`) after the
  class-timeline acquire. Readers only ever see complete generations.
* Retirement: old blocks/nodes/slabs are freed by the executor retirement ledger when every referencing
  scanner job retired AND Unity's `safeFrameNumber` passed the frame that last referenced them. No frame-gap
  constant.

### 2.4 Bounded hierarchical cut (C09R-D)
Page frustum cull → root frontier → per-level bounded expansion through compact indirect queues (depth ≤ 6):
a node under the foveated screen-error threshold emits its aggregate, otherwise its children join the next
frontier; leaf blocks emit their surfels. Hidden branches are never enumerated. Previous-frame cut reuse when
the head barely moves. HZB fails open: previous scan depth is the primary occluder; Environment Depth is
combined only where it agrees within margin; disagreement / uncertainty / incomplete reprojection = KEEP.
Every HZB dispatch is its own timed stage.

### 2.5 Lifecycle (C09R-E)
Dedicated RESET WORLD transaction (stop observations → retire pending world work → publish empty roots through
the graphics path → free by retirement → clear state), executor/pipelines/sensors stay alive. No usable
timeline semaphore → separate scanner queue disabled (ordered graphics-queue fallback). Observation
identity = monotonic sequence, time = XrTime. Mandatory warm-up steps registered late transition the host
back to WARMING_UP until they finish.

### 2.6 Review gaps closed (plan review, 6 points)
1. **Quantum budget gate + slicing.** `World::QuantumGate` (fs_world.cpp) reads the SCAN class stats after every
   retired fuse job (`ExecClassStats`): GPU time above the class quantum halves `epochMeasCap_` (floor
   `FS_EPOCH_MEAS_MIN`) and `epochDirtyCap_`; below half the quantum both double back to their maxima. A
   measurement frame larger than the cap is consumed in slices (`SliceState`: `fuse_ingest` push `offset`),
   dirty cells beyond the cap stay set in the mask (`fuse_dirty_emit` keeps the bits) — nothing is dropped,
   the epoch just takes more jobs. Telemetry: `epochMeasCap`, `epochDirtyCap`, `slices`, `capHalvings`.
2. **Determinism definition.** An epoch's result is a pure function of (a) the measurement sequence in ring
   order and (b) the canonical/index generation the epoch reads. F2 folds every segment SEQUENTIALLY in
   stable-sort order (key, then measurement index); no tree reduction, no float atomics; F3 is greedy
   first-fit in the same order. Replaying a sequence is bit-identical (host test `TestFusionDeterminism`); a
   permutation of the same set yields the same associations and the same result up to float summation order.
3. **Global 32-bit sort vs page-local grouping.** Frozen as the global stable LSD radix sort (4 × 8-bit passes,
   `sort_histogram/scan/scatter`) because the key already encodes page and cell for unmatched records and the
   handle for matched ones, so one sort yields both segment kinds. Its cost is a device receipt, not an estimate:
   the executor writes a device timestamp pair around every dispatch of a job, so the FS-HOST-CS jsonl `stages`
   ring carries `sort_histogram`/`sort_scan`/`sort_scatter` next to the fuse job total; the sort share is
   recorded in `DEVICE_ACCEPTANCE.md` for both acceptance runs. Decision rule: page-local grouping replaces the
   global sort only if the three sort dispatches exceed 25 % of the fuse job at the acceptance measurement rate.
4. **F3 clustering, exactly.** Per unmatched cell segment, in sorted order, a measurement joins the FIRST
   candidate (creation order) that passes: normal dot ≥ `FS_ASSOC_MIN_DOT`, |plane distance| ≤
   `FS_ASSOC_SIGMA_GATE`·√(σc² + σm²), tangent distance ≤ 2·footprint; otherwise it seeds a new candidate while
   fewer than `FS_CELL_NEW_MAX` (8) exist (later sheets stay unmatched and are retried next epoch, counted);
   at most `FS_SEG_CLUSTER_MAX` (256) measurements are examined (overflow counted). Ties are impossible by
   construction (first fit). SurfaceIDs = `idBase + exclusive prefix` of candidate counts over segments in
   sorted order (`fuse_prefix`/`fuse_cluster_write`). Twin: `ClusterSegment` in `fs_fusion_ref.h`.
5. **Index-generation invariant.** F0 of epoch N reads exactly the index/canonical state left by epoch N−1
   (or by the last publish/erase/load job): the fuse job of N is submitted only when no world job is in flight
   (`JobsIdle`), F0/F1 never write the index or surfels, F2 writes surfels only through their owner segment,
   F3/F4 mutate a cell subtree only from the invocation that owns the cell (`fsIndexInsert/Remove` without
   atomics), and the leaves a split retires are freed only after the job retired (`retire` list →
   `RetireLater`). The host test `TestIndexGeneration` pins split/chain/overflow/pool-exhaustion behaviour.
6. **Cross-page DDA cost bound.** `fuse_freespace.comp` walks ≤ `FS_FREE_WALK_MAX` (128) half-cell steps within
   `FS_FREE_MAX_RANGE_M` (6 m) and changes logical page at most `FS_FREE_PAGE_HOPS_MAX` (4) times; the fifth
   hop stops the ray and counts `FS_GCTR_FREE_HOP_OVERFLOW`. Stamps are idempotent per epoch (1 + tick % 255),
   so write order does not matter. Twin + test: `FreeSpaceWalk`, `TestCrossPageFreeRay`.

### 2.7 Commit series
The C09R change set is committed as review slices A–F (pools/index, fusion, publication + world C++, render,
lifecycle, tests/docs). The slices share one ABI cut and are not independently buildable; the source gate
(`gen_abi.py --check`, relic grep, `build_native.sh`, host tests) ran on the series head, whose SHA is the
`ACCEPTANCE_SHA`.

## 3. Acceptance record
Filled by the device runs of the closure (§40–§43 of the C09R task): `ACCEPTANCE_SHA`, run directories,
p50/p95/p99 tables. See `DEVICE_ACCEPTANCE.md`.
