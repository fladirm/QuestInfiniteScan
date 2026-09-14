# World module (C04 / C05b / C06 / C08) — design notes and limits

Owner: `Native~/src/host/world/**`, `Native~/src/host/render/**`, `Native~/shaders/{world,render}/*.comp`,
`Native~/tests/host_tests_world.cpp`, `Tools~/native/gen_abi.py`. Everything here is provisional until the
C05/C08 device benchmarks land; numbers live in `fs_world_params.h` (one header, included by C++ and GLSL).

## Division of labour (hard rule)
* **GPU selects page work.** The CPU never enumerates pages to submit jobs. It sets per-slot flags in the
  host-visible `gctr` buffer (`FS_SLOT_FLAG_REBUILD`, `FS_SLOT_FLAG_DIRTY`) and submits ONE maintenance job:
  `maint_collect(rebuild)` → `index_rebuild/count/subdivide/insert` (2D indirect, y = list index) →
  `maint_collect(publish)` → `publish_count/scan/copy` → `publish_commit` (GPU root-last) →
  `cluster_leaves` + 7 × `cluster_internal` (indirect per level, batched over all published pages) →
  `cluster_commit`. 19 bounded dispatches, ≤ `FS_MAINT_MAX_PAGES` (32) pages per list; leftovers keep their
  flag and are picked up by the next job (`maintenanceWanted_` is re-derived from the host-visible flags
  after the fence, never from a blocking readback).
* **Zero readback.** Every value the CPU consumes (counters, flags, page headers) is read from host-visible
  memory after the executor reported the fence retired. Page generations and root words are GPU authority
  (`world_publish_commit.comp`); the host mirror is telemetry.
* **CPU = residency + SSD stand-in.** Slot allocation/eviction, cold-store copies (RAM until C14) and the
  scan pre-pass (page creation for unseen keys) are the only CPU loops; they touch host-visible memory only
  when the GPU is quiescent for that slot (evictions require `GpuIdle()`; loads write BACK then the flag).

## Arenas (per slot, `FsSlotLayout`)
`FS_RANGES_PER_SLOT = 3` surfel ranges: BACK (scan writes, indexed) + FRONT parity 0/1 (Morton-sorted
immutable snapshots). Publish sorts BACK into the *inactive* parity, then the GPU commit flips the single
root word (`FS_ROOT_*` bits: count, published, parity, treeValid, leafShift, gen low byte). The cull reads
one u32 per slot; a parity that stopped being root is left untouched for ≥ `FS_PUBLISH_MIN_FRAME_GAP` frames
(maintenance job spacing) so culls already recorded against it finish first. Cluster nodes/errors are
per parity too (`nodeBase + parity * nodesPerSlot`).

Defaults (config overrides slots/capacity): 256 standard slots × 16384 surfels + 8 big slots × 262144
(FS_BIG_SLOT_FACTOR 16, node cap 4096 → leafShift ≥ 1). Memory: surfels 40 B (32 + 8 evidence) × 3 ranges
→ ~500 MB standard + ~250 MB big, index (heads+counts 256 KiB + micro 16 KiB + links) ~100 MB, nodes
88 B × 2 parities. All host-visible on the UMA device except index/scratch/free-space.

## Page-local index: variant A (contract §9.2), `world_index_*.comp`
Fixed 32³ bucket grid per 4 m page (12.5 cm cells), one u32 head per cell + per-cell count, LIFO lists
through the BACK link array (`atomicExchange` push, no CAS loops). **Adaptive subdivision**: a cell whose
count exceeds `FS_CELL_OVERFLOW_THRESHOLD` (32) is marked `FS_CELL_HEAD_SUBDIV_BIT | block` on the next
rebuild and its list moves into a 2×2×2 micro-bucket block (6.25 cm) from a per-slot pool of
`FS_MICRO_BLOCKS_PER_SLOT` (512). One level only (the contract allows up to 3 → 1.5 cm); deeper overflow
and pool exhaustion are counted (`FS_GCTR_INDEX_OVERFLOW`, `FS_GCTR_MICRO_POOL_FULL` → `FS_CTR_INDEX_OVERFLOW`)
and the integrate kernel sets `FS_SLOT_FLAG_REBUILD` so the next maintenance job subdivides ("overflow
persists"). Candidate scans are bounded (`FS_CANDIDATE_MAX` 16, overflow counted); publish walks are
bounded (`FS_CELL_WALK_MAX` 2048 per cell, counted).

## Integrate stub (`world_integrate_stub.comp`; the real fusion is C12)
measurement → page hash (GPU mirror, robin-hood early exit, 32 probes) → cell (+ micro bucket) → ≤ 16
candidates: within 2 cm and normal dot > 0.8 → count-weighted update, else allocate in BACK (atomic
per page, capacity-bounded, `FS_GCTR_BACK_OVERFLOW`). New candidates are transient (§8.6). Known stub
races: two measurements of one job creating the same surfel may duplicate; read-modify-write updates are
not atomic. Stable surface ids come from a global counter (`FS_GCTR_NEXT_SURFACE_ID`), never a slot.

## ClusterTree (§13.4) and coverage
Layout: root first, levels top-down, leaves last; leaves = consecutive runs of `64 << leafShift` surfels of
the Morton-sorted FRONT, fan-out 8 (the last node of a level may have fewer children). `ownError` =
bounds half-diagonal (monotone), `parentError` = parent's ownError (root: INF) in the parallel
`FsClusterError` array. Coverage = projected occupancy of a 6×6 grid inside the node's footprint ellipse
(bounds projected along the cone axis, semi-axes packed as log16 in `reserved`, drawn as
radiusMajor/radiusMinor with tangentAngle 0 in the canonical tangent frame `TangentFrame()`); internal
nodes combine children area-weighted and clamp. Measured on the host reference: wall leaves 0.67 avg
(64-runs that straddle a Morton jump span two patches), wall root 1.0; foliage leaves 0.48 (< 0.6),
level 1 saturates (0.92) because overlapping 3D children add up. **Known limits**: gap preservation holds at
the leaf-cluster level only; cell-aligned leaves and occupancy rasterisation at internal levels are the
follow-up. GLSL and C++ implement the same per-node arithmetic; float summation order differs, so device
parity is checked by invariants (counts, containment, ranges), exact equality only CPU vs CPU.

## Synthetic world (C04)
Kinds 0 room box, 1 corridor + doorway, 2 staircase, 3 thin wall (two sheets 3 cm apart, opposite
normals; they straddle the z = 0 page boundary), 4 dense foliage (shelves of plants, leaf ellipsoids at
≤ 1 cm with spatially coherent 2 cm gaps + one hole per leaf, until 3 M samples or the shelves are full).
Direct path: CPU Morton sort + CPU tree → uploaded to BACK, FRONT parity 0, nodes/errors; rooted at once,
flagged REBUILD so scans can fuse into it. `kind | 0x100` feeds the measurement ring in 16384-sample
slices per tick and requests scan ticks. Pages above the big-slot capacity are truncated (logged).

## Residency (§12)
`FsResidency_SetCenter` stores the request; the tick predicts the centre from the frame hint velocity
(bounded), enumerates pages by sphere-vs-cube distance (`fs_residency_math.h`), loads ≤ 2 / evicts ≤ 2
per tick (LRU outside prefetch, GPU idle). Orientation is not an input anywhere (no rotation parameter
exists); `orientationResidencyRequests` is therefore 0 by construction. Until the first centre arrives all
cold pages are uploaded (synthetic bring-up).

## Render (`fs_render.cpp`, §13.5)
Frame-begin hook: ≤ 3 HZB dispatches (scatter 3D over {prev, env} × {L, R}; combine + level 1; levels 2..8
+ clear of the scatter regions) only when a new depth prior arrived, then 4 cull dispatches: `pages` →
`nodes` (indirect 2D: 64 groups × visible pages, single-pass cut rule) → `emit` (indirect, workgroup per
leaf range, one atomic per range) → `compact` (indirect over aggregates, copies them behind the opaque
records, last workgroup writes both `FsIndirectDrawArgs` {24, opaque×2, 0, 0} {6, agg×2, 0, 0}, the
stats ring and resets). The cut is recomputed every 2 frames or when the head moves beyond the prediction
margin; otherwise the previous draw list/args stay. Temporal page cache: pages cached OUTSIDE are skipped
when the head moved less than the margin. Modes: SCAN (band test), XRAY (no depth-prior cull), PLAN (no
cone). `cullGpuUs` in `FsRender_GetLastCullStats` is 0 (frame-hook work carries no executor timestamps).

Depth-prior assumptions to validate on device: prev depth uses the GPU projection it was rendered with
(generic inverse, reversed-Z fine, 0/1 = no data); Environment Depth is treated as standard [0,1]
perspective depth from XrFovf tangents + near/far; `flipY` bit unused.

## Additive executor / host-API needs
* `const FsHostConfig* ExecConfig()` — weak in `fs_world.cpp`; without it the provisional defaults apply.
* Header additions (done): `FsClusterError` (ABI), `FsRender_SetMode`, `FsRender_GetDrawLayout`.
* Unity shader contract: aggregates start at `opaqueArgs.instanceCount / 2`; canonical tangent frame as
  in `TangentFrame()`; opaque draw = 24 verts (8-gon), aggregate draw = 6 verts.

## Tests
`Tools~/native/build_host_tests_world.sh` (hooked from `build_host_tests.sh`): page hash, encodings,
Morton/root word, cluster layout, synthetic counts, residency math + orientation independence, HZB band
rule, cluster reference invariants, coverage (gaps), frustum/foveation/headroom math, measurement ring.
