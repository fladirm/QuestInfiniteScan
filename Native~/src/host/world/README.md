# World module (C09R) — design notes and limits

Owner: `Native~/src/host/world/**`, `Native~/src/host/render/**`, `Native~/shaders/{world,render}/*`,
`Native~/tests/host_tests_world.cpp`, `Tools~/native/gen_abi.py`. Numbers live in `fs_world_params.h` (one
header, included by C++ and GLSL); the ABI in `Native~/include/finalscan_world_abi.h` (GLSL/C# twins generated).
Forensic background and the defects this design replaces: `docs/C09R_FORENSIC_CLOSURE.md`.

## Division of labour (hard rule)
* **Single writer per epoch.** The world is mutated only by the fuse job of the current epoch (SCAN class) and
  only by the invocation that owns a cell; no other job (publish, erase, load, release) overlaps a fuse job
  (`JobsIdle` gate in `fs_world.cpp::Tick`). F0 (association) reads the canonical/index generation the previous
  epoch left behind and never writes it; F2–F4 write only what F0 could not have read differently.
* **Zero readback.** Everything the CPU consumes is host-visible memory read after the executor retired the
  job: counters (`gctr`), epoch words (`FS_T_*`), pool ring heads, retire lists, pending publishes.
* **Graphics owns publication.** Scanner-built render roots become visible only through
  `render_publish_roots.comp`, recorded at FRAME_BEGIN on the graphics stream from a host-visible ring; the
  cull reads `FsPageDesc.renderRoot` in the same command buffer, so a frame never sees a half-published root.
* **Memory reuse is retirement-proven.** Ids the fuse/publish job released (`retire` list) are handed back to
  the free rings only through `RetireLater` (scanner fence + graphics `safeFrameNumber`); page release follows
  the same path (`BeginRelease` → publish empty root → `FinishRelease` after retirement).

## Pools (C09R-A)
Canonical surfels (32 B) + evidence (8 B) live in ONE global arena divided into slabs of `FS_SLAB_SURFELS` (256);
a page owns a slab directory (`FS_PAGE_MAX_SLABS`) and grows/shrinks by slabs (`AddSlabs`/`TopUpSlabs`, memory
governor = `FsHostConfig.canonicalSurfels`). Index leaves (64 B, 14 handles), index nodes (32 B), render blocks
(64 surfel copies) and render nodes (120 B) are global pools with free-id rings (`IdRing`: GPU pops with one
atomic, CPU appends after retirement; exhaustion is counted, never a fault). Default ≈ 520 MiB.

## Adaptive index (§9.2)
Per page a 32³ direct-cell directory (entry = leaf | node | empty); a full leaf splits spatially into an
octant node up to `FS_INDEX_MAX_DEPTH` (3 → 1.56 cm micro cells); only at max depth a leaf may chain
(`FS_INDEX_CHAIN_MAX` 4 → 56 surfels per micro cell, the rest counted in `FS_GCTR_INDEX_OVERFLOW`). No linked
lists through the surfel array, no atomics on the index: the owner invocation of a cell is its only mutator.

## Staged fusion (C09R-B), one epoch = one SCAN job of 20 dispatches
`fuse_ingest` (slice of the measurement frame, `offset`/`maxCount`) → F0 `fuse_associate` (page hash → owner
cell → 2×2×2 neighbourhood → ≤ 128 candidates, Mahalanobis best, tie → lower handle; key = handle |
`UNMATCHED|slot<<15|cell` | NONE) + `fuse_freespace` (page-aware DDA, ≤ `FS_FREE_PAGE_HOPS_MAX` page hops) →
F1 stable LSD radix sort by the 32-bit key (4 × 8 bits, sequential rank; ties keep measurement order) → F2
`fuse_reduce` (one invocation per matched segment: sequential precision-weighted fold in sorted order, ≤ 256
contributions, tangent ellipse from 2×2 moments, centre clamped to the owner cell) → F3
`fuse_cluster_count/write` (greedy first-fit over the unmatched segment of a cell, ≤ `FS_CELL_NEW_MAX` (8)
candidates, SurfaceID = idBase + exclusive prefix) → dirty-cell list (deterministic, sliced by
`epochDirtyCap`) → F4 `fuse_maint_apply` (ghosts from free-space stamps, deterministic merge priority:
static evidence → sigma → lower SurfaceID).
C09R-E4.1C adaptive coarsen / refine (no major-axis split any more):
- **Refine** (`fuse_reduce` → `sheet_refine`, fuse job): a converged surfel whose observation holds ≥
  `FS_REFINE_MIN_OUTLIERS` contributions beyond `FS_REFINE_OUTLIER_K` σ requests a site at the robust mean of the
  outliers on the side of the maximum residual; the sequential pass inserts it as a TRANSIENT candidate unless a live
  surfel of the 27-cell neighbourhood already explains it. Ids `idBase + newTotal + births`.
- **Coarsen** (`sheet_fit` decision → `sheet_contract`, publish job): a converged flat interior node that has the lowest
  survival priority of its mutual ring contracts into the neighbour of minimum
  `Ecollapse = plane error/σ + W_CURV·RMS/σ + W_N·(1−n·n_fit) + W_B·(K−degree) + W_C·|Δcolour|` below
  `FS_CONTRACT_BUDGET`; the survivor keeps its SurfaceID and absorbs (`fsAbsorbPatch`), the loser is removed and its
  neighbours relink. The lowest-priority rule rules out chains. Receipts `edgeContractions`, `refinementBirths`.
C09R-E4.2R derived micro-surface readout: `sheet_fit` computes each graph node's Voronoi cell restricted to its sheet
(8 directions in the canonical tangent frame; boundary = nearest perpendicular bisector of a mutual same-sheet
neighbour; a direction no neighbour supports within 60° is capped by the statistical support ellipse). `sheet_apply`
stores it (node words 8/9) and republishes the owner cell when it changed; `publish_leaves` writes it into the render
copy only (`FS_RENDER_CELL_MARK`); the cull encodes `FS_DRAW_FLAG_CELL` records whose 8-gon fan vertices lie on the
cell boundary (`Shaders/FinalScanSurfel.shader`). Sites with fewer than `FS_SHEET_CELL_MIN_DEGREE` edges stay
ellipses. Appearance state: bits 24..27 of `appearanceHandle` count coloured observations — NONE / PROVISIONAL /
CONFIRMED (≥ `FS_APPEARANCE_CONFIRM_OBS`); SCAN draws CONFIRMED photometry only, GEOMETRY is neutral grey.
Determinism: the result of an epoch is a pure function of the measurement sequence (ring order) and the
generation it read; replay is bit-identical (`TestFusionDeterminism`). CPU twins: `fs_fusion_ref.h`.
Budget: `QuantumGate` halves `epochMeasCap`/`epochDirtyCap` when the SCAN class overran its quantum and
doubles them back while it stays under half; a measurement frame larger than the cap is walked in slices
(`SliceState`), never dropped.

## Publication (C09R-C)
Per epoch the PUBLISH job (waits the SCAN timeline value) rebuilds only the dirty cells: `publish_leaves`
writes immutable render blocks (COW: a new block per changed leaf, the old id goes to `retire`), then
`publish_level` ×5 rebuilds the octree levels above (unchanged subtrees are shared), and the new root lands in
`FsPendingPublish`. `OnFrameBegin` records `render_publish_roots` for the pending pages of the ring slot, then
`RetireLater` frees the retired ids after the frame's retirement. No FRONT/BACK copies, no page-wide scans.

## Render (C09R-D, `fs_render.cpp`)
Frame-begin hook: HZB (scatter, combine, mips) only when a new depth prior arrived, then the bounded cut:
`render_cull_pages` (page AABB vs both eye frusta, temporal OUTSIDE cache) → `render_cull_expand` ×
`FS_CULL_LEVELS` (one indirect dispatch per frontier level, ping-pong queues of ≤ `FS_CULL_FRONTIER_CAP`; a
node is emitted as an aggregate when its projected error ≤ τ (foveated × headroom), as its block when it is a
leaf, otherwise its children join the next frontier; overflow emits aggregates, never drops geometry) →
`render_cull_blocks` (64-lane workgroup per emitted block) → `render_cull_compact` (aggregates behind the
opaque records, two `FsIndirectDrawArgs`, stats ring, counter reset). HZB fails OPEN: a tile where the sources
disagree, or with Environment Depth but no scan history, is UNCERTAIN and keeps the node. Every dispatch group
carries a device timestamp pair (`render.hzb.*`, `render.cull.*`), exported by `FsRender_GetTelemetryJson`.

## Lifecycle (C09R-E)
`FsWorld_Reset` is a transaction inside the running executor: observations stop, every page is released
through the publish path, pools and counters reset after retirement; pipelines, sensors and the renderer stay.
No timeline semaphores → the separate scanner queue is disabled (ordered graphics-queue fallback). Observation
identity is a monotonic sequence (time stays XrTime). A mandatory warm-up step registered after READY drops
the host to WARMING_UP until it compiled.

## Tests
`Tools~/native/build_host_tests_world.sh` (hooked from `build_host_tests.sh`): page hash, encodings, Morton,
synthetic scenes (busiest foliage cell reported), residency math, HZB fail-open band + source disagreement,
cull math, and the C09R §32 suite: fusion determinism, concurrent candidates, dense bucket, deterministic merge,
pools/retirement, sparse dirty list, cross-page free ray + hop bound, index generation (split/chain/overflow/
pool exhaustion/reset).

## C09R-E5R execution / dataflow closure (replaces the E5 alternation)
- **Persistent dirty frontier:** `fsMarkDirtyCell` sets the dedupe bit and enqueues `{page<<15|cell, tick}` into the dirty-cell
  ring (`FS_DIRTY_RING_CAP`); `dirty_take` consumes a batch into the publication list and clears the bits; a released page
  clears its bits so its stale entries are skipped. No pass rescans page masks (the pages/prefix/emit compaction is gone).
  A full ring refuses and counts (`dirtyRingDrop`, must stay 0).
- **Deadline / deficit scheduler (`World::Tick`):** one job per tick among FUSE, PUBLISH (next chain stage or a new chain) and
  SHEET. Overdue work first: oldest pending dirty cell older than `FS_SCHED_PUBLISH_DEADLINE_MS`, oldest pending graph
  node older than `FS_SCHED_SHEET_DEADLINE_MS` (ages from the enqueue ticks); otherwise the runnable stage with the
  largest GPU-time deficit (`FS_SCHED_SHARE_*`). A new depth frame is not leased while work is overdue.
- **Per-stage cost models (`fs_cost_model.h`):** `t = fixed + n·perItem` by EWMA least squares per stage (fuse, maint,
  leaves, sheet, measurement refine); the batch follows the variable cost; near-empty jobs are not learned; `fixed ≥
  target` is reported structural (the batch spends one target of variable work, it is never halved to the floor).
- **Fusion dispatch domain = actual N:** cluster count / prefix over the epoch's whole blocks, the carry sums only those.
- **Receipts:** publication age p50/p95 (change → FRONT commit), sheet age p50/p95, chain duration p95, promoted-live
  (FRONT/BACK ratio), scheduler jobs / overdue picks, cost-model fits.
- Refinement coverage (`sheet_refine`) searches the 27 global cells across resident pages through the page hash.

## C09R-E5 bounded GPU / executor closure
- **One bounded job per scheduler tick.** Fuse slices are stride subsamples of one depth frame
  (`phase + k·stride`, stride from `epochMeasCap`, floor `FS_EPOCH_MEAS_MIN` 512) so the SCAN quantum gate can
  reach 1–3 ms; the fuse epoch applies its own relocations.
- **Drop older observations, never display frames.** When a newer depth frame is READY, the rest of the current
  frame's slices is abandoned (`framesAbandoned`, `measurementsAbandoned`, counted as dropped measurements).
- **Publication continuation chain** (FRONT immutable until its last job): `world.publish.maint` (dirty compaction ≤
  `epochDirtyCap`, maintenance, relocation) → `world.publish.leaves` chunks over `[offset, offset + leavesChunk)` →
  `world.publish.levels` (levels 1..5, pending roots → graphics-owned publication). Every stage has its own quantum
  gate on the PUBLISH class quantum; fuse slices alternate with chain stages. Render ids retired by leaves chunks are
  held until the chain's FRONT publication.
- **Surface complex at a lower cadence:** `world.sheet` (graph, fit, apply, contraction, relocation) runs only between
  publications, at most every `FS_SHEET_PERIOD_EPOCHS` fuse epochs, with a quantum-gated batch (`sheetBatch`); the
  graph prefilters candidates by flags and position before decoding.
- **Draw-list temporal reuse** (`fs_render.cpp`): the cut and the HZB are rebuilt only when the pose moved beyond the
  margin, the FRONT published roots (`world::FrontSequence`), the anchors, mode or draw buffers changed
  (`reusedCuts` receipt); no periodic recut.

## E6R persistent surface topology (METRIC SURFELS = control vertices, PERSISTENT SURFACE COMPLEX = shared faces / sheets, RENDER MESH = derived)
- **Clock:** every world job takes a work serial; rings store it; scheduler ages are real enqueue ages. FUSE / PUBLISH /
  TOPOLOGY each have a deadline; the most late runnable class wins, otherwise the weighted deficit.
- **Evidence hysteresis:** support + contradiction debt (healed by positives), narrow-phase free-space contradictions against
  the surfel support ellipse, lifecycle ACTIVE -> SUSPECT -> RETIRING (topology decides, neighbourhood-consistent) -> REMOVED.
- **Faces (`topo_faces`):** per changed vertex, for each mutual same-sheet candidate edge the first CCW candidate forming a
  compatible, bounded (circumradius <= 5 cm) triangle that is locally Delaunay under a deterministic SurfaceID-lifted in-circle
  predicate; the minimum-SurfaceID vertex owns and stores it (face key = sorted SurfaceIDs; no duplicates by construction).
- **Commit (`topo_commit`, sequential):** pooled vertex records (SurfaceID guard), owned faces diffed by SurfaceIDs, incident
  counts, face states from vertex lifecycles, pooled union-find sheet records (root = lower sheet id; growing sheets join,
  established sheets union only after repeated bridge evidence), component labels with relabel generations and confirmed
  splits (persistent cuts), releases, reference-counted sheet reclamation, C11R2 temporal targets.
- **Coarsen / refine:** interior-only contraction with one-contraction-per-vertex matching per topology generation;
  refinement requires a residual streak.
- **Readout:** `publish_leaves` writes owned faces as triangle render copies (`FS_RENDER_TRI_MARK`); ellipses only for vertices
  that are not mesh members or sit on a boundary; the cull fills a third (triangle, 3-vertex) draw bucket. The 8-gon cells are gone.
- Twin `fs_topology_ref.h`; tests `TestTopology` (face key, ownership, duplicates, shared edges, union determinism, wall union,
  corner, door, thin wall, persistent cut, matching, refinement), `TestEvidenceHysteresis`, `TestSchedulerNoStarvation`.

