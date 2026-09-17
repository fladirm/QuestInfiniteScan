# Merkaba line-node membrane on the 045be20 baseline, event-driven residency (plan, 2026-09-17)

Binding plan. Written before code. Every deviation is recorded in section 13 in the same
commit that makes it. A test is never rewritten to match code without a written reason
in section 13. Supersedes `.claude/MERKABA_KNOT_PLAN.md` sections 1, 2, 4 and the
readout-scheduling part of 3.

## 0. Baseline: back to the best build, keep the real fixes, remove what was dragged in

`045be20` is the best device build of the 2026-09-17 sessions (user verdict). Its
geometry rules are the baseline. Everything after it is classified below. Work happens on
the current branch head; no hard reset (the keep list lives in the same files).

### 0.1 Geometry baseline taken from 045be20 (MerkabaOverlapShell)

- Every measured occupied MAIN gets one 25 mm patch with four half-lattice corners.
- Contributors: four columns around the corner line, normal layers +-1, `|N[c]| >= 0.5`,
  no KNOWN FREE separator toward the reference layer.
- Free-side signature: two bits, KNOWN FREE at -axis and +axis (exact equality).
- Branch: connected height interval with gap `0.6 * pitch`, median, per-column winner by
  residual then nearer layer then lexicographic coordinate, mean height, weighted RGB,
  mean normal.
- No owner chain, no layer merge, no root hops, no winner-identity weld.

The only structural change: the corner is solved once per absolute line branch (node),
not once per MAIN. That removes the per-MAIN disagreement that produced 045be20's
remaining cracks. The chart becomes the C5.1 smoothed chart.

### 0.2 Keep (with commit of origin)

| origin | kept part |
|---|---|
| beb903c | fixed patch descriptors by kernelLocal; `MerkabaVisibility.compute` (Cull/PrepareVisible/EmitVisible isolated, Resources.Load); native producer stage list; ERASE as controller capsule, kernels inside become KNOWN FREE; NOTE field read-only, one keyboard authority; pointer, cursor and fine cursor on UI layer |
| 0ea6cb8 | `MerkabaUiOnTopFeature` URP pass, UI layer removed from renderer masks by wizard; real viewer alpha, depth-only pass + ZTest Equal; continuous coverage film; erase tube from the controller |
| f805aea | Begin/Batch/Finalize native transaction, 64-tile batches, FRONT immutable until Finalize, page owner at M8TakeRenderPages and cleared at reclaim, index total by delta, Recount removed, publication-invalid reason bits, completion record 48 B |
| 64c4d1d | dispose of the finished native job before creating the next |
| f5d3f90 | ERASE selection previews its capsule without trigger/surface (RoomScanner); export `MeasuredPatchCount` excludes transitions, export patch normal = patch normal; `RenderScratchTiles = 64` |
| 3010d13 | only the rule that every corner lies within the branch gap of MAIN's measured plane (becomes C5.3) |

### 0.3 Remove (dragged in)

| origin | removed part |
|---|---|
| beb903c | register-resident corner solve (`M8MembraneResolveCorner`), "coherent geometry" claim (withdrawn) |
| f805aea | corner tasks `measuredCount * 4`, Phase A/B/C corner pipeline, `M8KnotTaskPosition`, `KnotIdentity` as winner cells, CPU `TrySolveKnot(main, ...)` |
| f5d3f90 | owner-only knot solve, `M8KnotTaskOwner` (caller layer), in-tile-only ownership, six-face confidence-weighted chart, same-tile-only `TryBuildStitch`/Phase C2, prologue folding done only to fit corner phases |
| 3010d13 | `M8WinnerKeysSameBranch`, `M8KnotRootTask` 12 hops, six-face equal-weight chart, first-rejection warm retry + `_waitingForResidencyEpoch` |
| pre-045be20 and later | every residency/frame epoch mechanism, listed exhaustively in section 6 |

## 1. Decisions (closed)

D1 CHART. C5 amended (section 2): 26-neighbour, radius-one, sheet-compatible,
equal-weight mean of measured normals; `ColorConfidence` never weights geometry. If
`|N_MAIN[chart]| < 0.5` the raw dominant axis of MAIN is used.

D2 NODE. A node is one branch of an absolute half-grid line. Uses sharing a node have a
non-empty common legal window (`max L - min L <= 2`). Contributors only from that
window. No union, no chain, no distance-two bridging.

D3 COLD. Never ignored, never guessed, never a stale pending tile beside new tiles.
Dependencies are HOT before a canonical commit (scan, erase). The readout never waits
for residency and never retries on a clock or epoch.

D4 EVENTS ONLY. Nothing in scan, erase, readout or eviction is driven by an epoch,
frame counter, safe-age window or free-running timer. Only concrete events: depth frame,
dependency tiles installed, load failed, canonical commit, coverage change, native job
completion.

## 2. Contract amendment (contr.md, commit C0)

C5.1 Canonical chart of measured cell m:
- `S = N_m + sum N_n` over the 26 neighbours in fixed lexicographic order where n is
  measured, `dot(N_m, N_n) >= 0.5` (no abs), `|plane_m(centre_n)| <= g` and
  `|plane_n(centre_m)| <= g` with `g = 0.6 * pitch`, and no KNOWN FREE on the
  single-axis step cells between m and n.
- `chart = dominantAxis(S)` (tie X, Y, Z); if `|N_m[chart]| < 0.5`, raw dominant axis.

C5.2 Line node:
- Line: half address `2*m + s0*e_t0 + s1*e_t1`, chart component set to 0, chart c.
- Use: measured cell u in one of the four columns, canonical chart c, free-side signature
  s (045be20 definition), `|N_u[c]| >= 0.5`; height `h_u` on the line; layer `L_u`.
- Relation R(u, v): same s; `|L_u - L_v| <= 2`; `|h_u - h_v| <= g`; no KNOWN FREE cell in
  either column within `[min(L_u, L_v), max(L_u, L_v)]`.
- Component: connected set under R. Valid iff `max L - min L <= 2`. Invalid components
  create no node and count `M8_COUNTER_MEMBRANE_INVALID_BRANCH`.
- Window `W = [max L - 1, min L + 1]`.
- Candidates: measured cells of the four columns with layer in W, `|N[c]| >= 0.5`,
  signature s, no KNOWN FREE in the candidate column between its layer and the nearest
  member layer (inclusive).
- Reference `h_ref`: median of member heights (order height, layer, column; even count =
  mean of the middle two).
- Admission: connected height interval (gap <= g) of candidates containing the candidate
  nearest to `h_ref` (045be20 interval rule).
- Winner per column: min `|h - h_ref|`, then nearer to a member layer, then smaller layer,
  then lexicographic coordinate.
- Node: mean winner height (column order 00, 10, 01, 11), confidence-weighted RGB, mean
  winner normal.
- Node key: (line address, c, s, min member layer, min member column at that layer).
- A tile holds a node only for components with a member in its layers 0..7; the node
  value is a global function and identical in every tile that holds it.

C5.3 Patch of MAIN m (chart c, signature s): the four nodes whose components contain m.
All four exist; every edge <= 45 mm; every corner within g of m's measured plane
(reject, never clamp); winding from `N_m`.

C5.4 Transition: m and `n = m + e_t` (t tangent of m), n measured with chart c' != c
and c' != t, `dot(N_m, N_n) >= 0.5`, reciprocal residual <= g, no KNOWN FREE between.
Quad from m's +t edge nodes and n's -t edge nodes, four distinct nodes, edges <= 45 mm,
facing edges co-directed (`cos >= 0.5`), no self-intersection, winding from `N_m + N_n`.
Owner m. Works across tile edges.

C9 Scheduling (new section):
- A scan or erase commit requires every tile it may dirty and their 26-neighbour tile
  ring HOT or ABSENT (never on SSD, never LOADING).
- Readout lists a drawn tile only when its ring is HOT or ABSENT. A COLD/LOADING tile or
  ring member in coverage means the tile is not drawn yet (load requested).
- Eviction never selects a pinned tile (section 6.4).
- No epoch, frame counter or safe-age window is part of any correctness rule.

## 3. Halo size (derived, not tunable)

In-tile layers 0..7 along c; valid components containing them lie in -2..9; deciding
validity exactly needs uses in -4..11; their charts read radius one: -5..12. Tangent
axes: line half coordinates -1..15 touch columns -1..8; charts -2..9; within +-5.
Transition neighbours `m + e_t` lie in the tangent range and their layers along c' are
m's coordinates 0..7. Cache cube = tile +-5 = 18^3, inside the 27 neighbour tiles.

Groupshared per kernel (limit 32 KB):

| item | bytes |
|---|---|
| `uint gM8MembraneFlags[5832]` (flags word, decoded on read) | 23,328 |
| cell bits 2 bit x 5832 (measured, KNOWN FREE) | 1,460 |
| chart table 2 bit x 4096 (tile +-4) | 1,024 |
| tile slots 27 | 108 |
| touched-line mask 243 bit | 32 |
| scalars | < 100 |
| total | ~26.1 KB |

The float4 plane cache (27.6 KB) and occupancy words are deleted.

## 4. GPU readout (Batch job: Advance, Collect, SolveSharedKnotLines, ResolveRenderPatches, Emit)

Scratch per work tile, 64 work tiles:
- Patch scratch (kept): 512 descriptors + 128 compact list, stride 640 uint4. After Solve
  descriptor x = kernelLocal | chart | signature | ready | compact index.
- `RenderLineUseScratch` (replaces RenderKnotScratch): 243 tasks x 64 slots uint4.
  task = c * 81 + i0 * 9 + i1 (half = 2 * i - 1); slot = column * 16 + (layer - tileLayer
  + 4). x = height bits, y = valid | signature | nodeId + 1, z = packed colour,
  w = confidence. 15.95 MB.
- `RenderNodeScratch` (replaces RenderKnotOwner): 4096 uint4 per tile. x = height bits,
  y = RGB, z = i0 | i1 | c | signature, w = valid | referenced | vertex ordinal. 4 MB.
- Header: patches, transitions, vertices, nodes, invalid branches, capacity failures.

`CollectRenderPatchInputs` (128 lanes): unchanged.

`SolveSharedKnotLines` (64 lanes, <= 8 barriers, <= 8 writable):
1. G lane 0: context, 27 tile slots (COLD slot = ring invariant breach counter, tile
   fails closed), cell bits clear.
2. G: flags cube and cell bits.
3. D: chart table (C5.1).
4. D: MAIN prepass: descriptor chart/signature/ready; touched-line bits (four corner
   lines of each MAIN, the -t facing lines of each C5.4 neighbour).
5. D: one lane per touched task (four 64-lane passes): gather uses, cluster with 64-bit
   column/layer masks, validity, window, candidates, winners, node record by atomic
   ticket (> 4096 = capacity failure, tile fails closed), node id into member slots.
6. D: header.

`ResolveRenderPatches` (64 lanes, new):
1. G lane 0: context.
2. D: MAIN -> four node ids from its own slots; C5.3 guards; patch ordinal ticket;
   mark nodes referenced.
3. D: C5.4 transitions; ordinal ticket; mark referenced.
4. D: vertex ordinal ticket for referenced nodes.
5. D: header.

`EmitRenderTileGeometry` (128 lanes): pages from totals; one vertex per referenced node
from its record; six indices per patch and per transition; record, delta, dirty bits.

Pipelines: Begin 33..42, Batch 43..47, Finalize 48..50, fine erase from 51. ABI 8.
Resource count unchanged (two renames).

## 5. CPU authority and twin (MerkabaOverlapShell)

- Start from the 045be20 oracle text (its contributor, signature, interval, winner and
  colour rules) and restructure; do not start from f5d3f90/3010d13 code.
- `CanonicalChart(cell, context)` C5.1; `LineKey`, `NodeKey`, `Node`;
  `SolveLine(LineKey, context, cache)` C5.2; `TryResolvePatch(main, ...)` C5.3;
  `TryBuildTransition(main, t, ...)` C5.4. `Corner` carries `NodeKey`.
- HLSL twin inside the authority string as pure functions called by the kernel loops in
  the CPU order; generated file regenerated only.
- Export uses the same functions; GLB vertex dedupe by `NodeKey`.

## 6. Residency and scheduling without epochs

### 6.1 Deleted symbols (all of them, grep must return nothing)

| file | symbols |
|---|---|
| MerkabaWorld.hlsl | `M8_COUNTER_RESIDENCY_EPOCH`, `M8SignalResidencyChange`, `M8_COUNTER_FRAME_EPOCH`, `M8_RENDER_PENDING`, `M8_COUNTER_RENDER_PENDING_TILES`, `M8_COUNTER_READOUT_UNRESOLVED` |
| MerkabaWorld.compute | `_M8SafeEpoch`, epoch stamps in runtime record z (claim, load reservation), safe-age eviction guard, 4 `M8SignalResidencyChange` calls |
| MerkabaIntegration.compute | `FRAME_EPOCH` increments and runtime z touches (query surface, query carve, FinalizeObservation, FinalizeFineErase), residency epoch in `_M8AttemptCompletion.w` |
| MerkabaReadout.compute | `_M8ResidencyChanged`, `FRAME_EPOCH` increment, PENDING bit set/clear and its rebuild rule, `gM8MembraneUnresolved` path, unresolved publication failure, `M8_MEMBRANE_TILE_UNRESOLVED` as a wait |
| MerkabaGrid.Gpu.cs | `ResidencySafeEpochs`, `SafeEpochId`, `CounterResidencyEpoch`, `CounterReadoutUnresolved` |
| MerkabaGrid.Storage.cs | `_residencyEpoch`, `ResidencyEpoch`, `PublishResidencyEpoch` (both call sites) |
| MerkabaIntegrator.cs | `_attemptResidencyEpoch`, `_fineEraseResidencyEpoch`, epoch compare in `CanRetryPreparedObservation` and `TryPrepareFineErase`, epoch log fields |
| MerkabaGridRenderer.cs | `_builtResidencyEpoch`, `_pendingRetryEpoch`, `_retryPendingTiles`, `_waitingForResidencyEpoch`, ticket `ResidencyEpoch`, `ResidencyChangedId`, `retryPendingTiles` parameters, `_coverageIncomplete` retry loop, `_nextReadoutBuild` free-running cadence, first-rejection warm retry in `RejectPublication` |
| MerkabaGpuTimestamps.cs | readout-unresolved telemetry field (replaced by cold-in-coverage) |
| Tools/verify_m8_sparse_readout_closure.py | checks "unresolved halo keeps the scan transaction open", "unresolved readout waits for a residency epoch" |
| Tests | `ResidencyRetryEpoch_IsCapturedAtAttemptSubmitAndGpuOwned`, `ZeroCarveHotTile_DoesNotRefreshItsResidencyEpoch`, epoch asserts in `CarveQueueIsAttemptLocalWhileSurfaceDedupSurvivesRetry`, `FrameEpoch`/`SetRuntime(epoch)` in `MerkabaResidencyGpuTests`, `UnresolvedHaloKeepsThePreviousTileRecord` |

Not residency scheduling, kept: camera copy fence counters in MerkabaIntegrator (renamed
`_cameraCopySubmittedSequence`/`_cameraCopyRetiredSequence`), lifecycle/source generation
tokens (stale callback guards), time-epoch conversions in camera and depth capture.

### 6.2 Scan and erase dependency preflight (replaces attempt epochs)

GPU, inside the existing observation job before `PrepareIntegrateArgs`:
- Query surface and query carve already resolve touched tiles. New pass
  `QueryDependencyRing` (64 lanes over touched tiles): each of the 26 ring tiles that is
  COLD or LOADING increments `M8_COUNTER_UNRESOLVED_RING_TILES` and records a load
  request; ABSENT is resolved.
- `PrepareIntegrateArgs`, `PrepareCarveArgs` and `FinalizeObservation` gate on
  unresolved surface + carve + ring == 0 (same rule as today, one more term). Nothing is
  written before the gate; the attempt stays prepared.
- `FinalizeObservation` writes `_M8AttemptDependencies[16]`: tile refs whose absence
  blocked the attempt (first 16 by lane order) and the total count.
- Fine erase: same pass before `EraseFineTiles`, same dependency record.

CPU:
- `RequestAttemptCompletion` reads completion (16 B) and dependencies (68 B) in one
  readback.
- Storage exposes two events from its install and failure paths:
  `TilesInstalled(ReadOnlySpan<uint> tileRefs)` and `TileLoadFailed(uint tileRef)`, and
  `IsLoadPendingOrInstalled(uint tileRef)` for the race where installation finished
  before the completion readback.
- Integrator keeps `_dependencySet`. Retry iff the set is empty and at least one
  `TilesInstalled` event arrived since the attempt completed (covers the overflow case
  where more than 16 tiles were missing). `TileLoadFailed` of a member fails the
  observation (`FinishObservation(DEPENDENCY_LOAD_FAILED)`), never a silent drop.
- Same for fine erase.

### 6.3 Readout (replaces pending tiles and warm retry)

- Traversal lists a HOT drawn tile only if its 26 ring is HOT or ABSENT. Otherwise it
  requests loads for the COLD ring members and the tile, counts
  `M8_COUNTER_READOUT_COLD_IN_COVERAGE`, and does not list it.
- BACK therefore never contains a tile built against a partial ring; FRONT never mixes
  stale pending tiles with new tiles.
- Warm-load requests are allowed in every traversal (requests of LOADING tiles are
  no-ops by ref state). No loop: builds start only on events.
- Build triggers: canonical commit (scan/erase journal), `TilesInstalled` event
  (one flag, consumed by one build), coverage change beyond the translation guard,
  lifecycle reset. No timer, no incomplete-coverage polling.
- Halo slot COLD inside `SolveSharedKnotLines` = invariant breach: counter
  `M8_COUNTER_READOUT_RING_INVARIANT`, tile fails closed, logged once; not a retry reason.
- `RejectPublication` handles validation/capacity failures only: FRONT stays, one
  rebuild on the next frame, same counters.
- Session-load coverage readiness = a published transaction with
  `READOUT_COLD_IN_COVERAGE == 0`.
- Native pacing: a finished job is disposed, its completion read, the next phase stored;
  it is submitted in the next `LateUpdate` (at most one readout job submission per frame).

### 6.4 Eviction (replaces safe-epoch window)

- Runtime pin bit `M8_RUNTIME_PIN_ATTEMPT`: set by the dependency preflight on touched
  tiles and their ring, cleared by `FinalizeObservation`/`FinalizeFineErase` when the
  attempt completes or fails. Survives while the attempt waits for dependencies.
- Readout pin: the focus radius becomes coverage radius + one tile diagonal (0.35 m) so
  every listed tile's ring is inside it; `M8_RENDER_VIEW_MARK` tiles are skipped too.
- `SelectEvictionVictims`: skip DIRTY, PERSISTING, pinned, view-marked, inside focus.
  No age test.

## 7. Scratch lifetime (64 tiles, no size workaround)

1. Diagnose in `MerkabaReadoutGpuTests.SetUp`: page-queue control words right after
   `EnsureGpuResources` and after SetUp; buffer identity per test; which dispatch writes
   the foreign record into the new page queue.
2. Fix at the source: GPU buffers released only after a fence created after the last
   submitted command; renderer release rebinds all readout kernels to 1-element dummy
   buffers.
3. Acceptance: `MerkabaReadoutGpuTests` all green in three consecutive fixture runs.

## 8. Tests

CPU oracle:
- 8x8 wall across a tangent tile edge: 81 nodes, bit-identical corners, 64 patches.
- 45 degree staircase across a chart-axis tile edge (layers 7/8): identical nodes from
  both tiles' windows.
- Window independence: global context vs GPU window (-5..12) give identical nodes for
  every line of the fixture corpus.
- Span 3 component: no node, invalid counter, its patches absent.
- Two parallel sheets beyond g: two nodes per line.
- Thin leaf with both signatures: two node families.
- Shrub fixture: curved leaves with measured normals within +-20 degrees: no corner
  farther than g from its MAIN plane, no edge > 45 mm.
- Noisy diagonal +-15 degrees around 48.6 degrees: one chart (C5.1). Chart fallback case.
- Transition fold same-tile and cross-tile: closed, no self-intersection.
- FREE separator blocks membership and candidates.
- 045be20 regression: on a flat wall and a room corner fixture the new corners equal the
  045be20 corners wherever all four MAINs of a line agreed in 045be20.
- Export: t/v ratio of 8x8 wall and GLB dedupe by NodeKey.

GPU / scheduler:
- Numeric parity: corpus through GPU readout, positions and indices equal CPU oracle.
- Tile with a COLD ring member is not listed; FRONT stays coherent; installing the
  member lists it in the next transaction.
- Observation with a COLD ring: no mutation, dependency record lists the refs; install
  event retries once; load failure fails the observation.
- Eviction skips pinned and view-marked tiles, no age input.
- At most one readout job submission per frame.
- A replacement assert for every deleted test in 6.1.

Gates: generated HLSL equals authority; SPIR-V audit (+2 kernels: ResolveRenderPatches,
QueryDependencyRing; <= 8 barriers and <= 8 writable per kernel); closure script updated
(every symbol of 0.3/6.1 absent, C5.1-C5.4 and C9 symbols present, groupshared <= 32 KB
per kernel, `RenderScratchTiles = 64`, no `Epoch` in Runtime except camera/depth time
conversion); FXC log grep; Unity EditMode 100 %; APK build clean.

## 9. Memory and cost budget

| item | value |
|---|---|
| LineUse scratch | 15.95 MB |
| Node scratch | 4 MB |
| groupshared Solve | ~26.1 KB |
| Solve target per 64-tile batch | <= 8 ms on Quest |
| Resolve target per 64-tile batch | <= 2 ms on Quest |

## 10. Commit order (gates of the scope green before the next)

- C0 contr.md C5.1-C5.4 + C9, this plan, ledgers.
- C1 epoch removal and event-driven dependencies (6.1, 6.2, 6.4) with their tests; the
  old readout still runs, so this is measurable alone.
- C2 CPU oracle from 045be20 rules + line nodes + export + CPU tests.
- C3 GPU readout (section 4), generator, ABI 8, executor, plugin, bindings, audit and
  closure, parity test, readout scheduling (6.3), pacing.
- C4 scratch lifetime (section 7).
- C5 APK, clean install, device evidence, STATE/RISKS.

## 11. Device acceptance

- Per-stage timing: SolveSharedKnotLines, ResolveRenderPatches avg/max per batch.
- One readout job per frame; no Begin loop; observations per minute while scanning.
- Counters: RING_INVARIANT = 0, invalid branches, cold in coverage, dependency waits.
- FPS producer on/off.
- Screenshots: flat wall, tile edge, 45 degree surface, shrub/flowers, fold.
- Export t/v ratio and GLB vertex count.

## 12. Known risks

- Solve cost with an 18^3 cache (3.4x the cells of the 045be20 cache).
- 20 MB extra GPU scratch.
- Steep noisy sheets beyond C5.1 produce invalid components (holes, by contract).
- Ring preflight lengthens the first scan at the residency edge (loads before commit).

## 13. Deviations

- C0: C9 was already the SCHEDULER section of contr.md. The scheduling rules were
  added there as an amendment block instead of a new section with the same number.
  Content identical to section 2 C9.
