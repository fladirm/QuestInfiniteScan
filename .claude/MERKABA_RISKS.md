# Verified risk ledger (Claude)

Evidence-backed only. Each entry names the exact code that produces it. `OPEN` is not
authorization to change scope.

## RISK-1 — the membrane "codegen" is two hand-maintained texts [OPEN]

`MerkabaOverlapShell.BuildGeneratedHlsl()` returns a **verbatim HLSL string literal**
with five numeric placeholders (`__PITCH__`, `__HALF_PITCH__`, counts). It is not a
translation of `TryBuildPatch`/`TryResolveCorner`. `MerkabaOverlapShellGenerator.Verify`
and the parity test only assert that `MerkabaOverlapShell.generated.hlsl` equals that
literal — i.e. that the file matches the string, never that the string matches the C#
algorithm.

Contract C7 says "do not hand-maintain a different GPU algorithm"; structurally that is
exactly what exists. Today the two texts agree because they were written together. There
is no mechanism that detects divergence.

Cheapest real closure: a differential test that runs `TryBuildPatch` and the compiled
`BuildReadoutVertices` over the same seeded lattice fixtures and compares corner
positions bitwise. Until that exists, treat every edit to either side as a two-file edit.

## RISK-2 — the shared-corner invariant is asymmetric and only tested flat [PARTLY FIXED IN CODE 2026-09-15; FINDING B CLOSED 2026-09-17]

Update 2026-09-17: the two quantized admission predicates (`DominantAxis ==`,
`CanonicalSheet(NearestGridNormalStep) ==`) are gone from both twins; a contributor is
admitted when `abs(normal[chart]) >= 0.5`, and the connected height branch plus the free
side decide sheet membership. `NoisyNormalsAcrossTheChartBoundaryStillContribute` covers
the 45 degree straddle; walls and slopes assert bit-identical shared knots.

Update 82c5a0a: TryResolveCorner (CPU + HLSL twin) gathers <=12 candidates from the four
columns, picks a seed, grows the contiguous height branch (gap <= 0.6 patch pitch,
sorted by height then coord), and averages the per-column members nearest the branch
median. Inside one sheet the corner no longer depends on which MAIN asks. The seed is
still chosen by MAIN signature + residual to mainHeight, so two MAINs on different
close sheets can still seed different branches; no non-flat shared-corner parity test
was added. C7 suite 306/306 editor. DEVICE ACCEPTANCE PENDING.

Original finding:

In `TryResolveCorner`, a corner shared by two adjacent MAINs is enumerated over the same
four tangent columns (columns derive from the half-lattice address, so that part is
MAIN-independent). But the per-column winner is chosen with two MAIN-dependent inputs:

```text
residual  = |contributorHeight - mainHeight|        // mainHeight is MAIN's own plane
signature = FreeSideSignature(...) == mainFreeSignature
window    = main[dominantAxis] + {-1, 0, +1}        // MAIN's own normal layer
```

Where a column contains more than one admissible contributor — two close parallel
sheets, a band that is occupied 2-3 layers thick, a step where adjacent owners sit on
different normal layers — two MAINs sharing the corner can pick different contributors
and therefore compute different corner positions. That is a crack / T-junction, and it
also breaks the C5 requirement of bit-identical shared corners.

`MerkabaOverlapShellTests.AdjacentPatches_CalculateBitIdenticalSharedCorners` uses
normal `(1,0,0)` with all MAINs on the same `x` layer, so it exercises only the case
where each column has one candidate and the selection is trivially identical. The C7
cases *45-degree plane*, *arbitrary quantized slope*, *thin partition*, *two close
parallel sheets* are tested for other properties, not for shared-corner identity.

Correct fix direction: make the corner solve a pure function of the corner address —
resolve the corner from the union of its four columns with a MAIN-independent tie-break
(sheet identity + lexicographic), never from MAIN's own height, signature or layer
window. Do not paper over it with a tolerance.

## RISK-3 — legacy 32-chunk constants still live in the frozen persistence header [FIXED IN CODE 2026-09-15]

Chunk helpers deleted from `MerkabaConstants`; the header field is the explicit pinned
literal `MerkabaSsdStore.HeaderPinnedSpan = 32`. Checkpoint format is now v4 (overlay
log stays v3).


`MerkabaConstants.ChunkSize/KernelsPerChunk/Neighbours/Flatten/Unflatten` are leftovers
of the superseded 32^3 design. `ChunkSize` is still written into and validated from the
`MerkabaSsdStore` header (`MerkabaGrid.Storage`/`MerkabaSsdStore.cs:419,590`), so the
number is load-bearing for format compatibility even though the runtime no longer uses
32^3 chunks. Removing the constant silently changes the on-disk contract. Any cleanup
must bump/pin the format version explicitly.

## RISK-4 — the gate suite leans on source-text assertions [OPEN]

Thirteen of seventeen EditMode test files assert on `File.ReadAllText` of production
source or shader text — exact literals including indentation and newlines
(`MerkabaUxTests` 14 occurrences, `MerkabaPersistenceTests` and
`MerkabaTilesetWriterTests` 5 each). The one failure in the 2026-09-05 00:34Z run,
`AnchoredResumeFailsClosedBeforeRegisteringTheM8World`, is exactly this: `IndexOf`
returned `-1` for a literal that a concurrent refactor had reformatted.

These are useful as anti-regression tripwires for deleted authorities (the S-gate
symbols), but they are not behavioural coverage and they turn every reformat into a red
suite. Do not read "279/280 passed" as "the scanner is correct"; read it as "the text
still looks like it did". Behavioural depth lives in `MerkabaGpuIntegrationTests`,
`MerkabaOverlapShellTests`, `MerkabaEvidenceTests` and `MerkabaSpatialTests`.

## RISK-5 — concurrent agent writes into the same working tree [OPEN]

A Codex session is editing this checkout live (it produced `863e75d` and two untracked
runtime files during a single audit). Read-modify-write on any file is racy. Probe
`git status` immediately before and after edits, and never `git add -A`.

## RISK-6 — README/ALGORITHM describe a design that no longer exists [OPEN]

They document 32^3 chunks, 24 face-quadrant patches, 26-bit neighbourhood masks and
files that were deleted. Anyone onboarding from them builds a wrong mental model. They
carry a pointer to `contr.md` but are otherwise unrevised.

---

# Audit 2026-09-15 @ 651cea6 (source read only, no gates, DEVICE ACCEPTANCE PENDING)

## RISK-7 — readout is a full rebuild, dirty after nearly every observation [FIXED IN CODE, DEVICE MEASURED 2026-09-16: publication is sparse and cheap; see RISK-15]

Fix: paged delta publication. Integration marks `runtime.w` DIRTY only on membrane-input
change (occupancy, plane payload, KNOWN-FREE class, colour top 6 bits); install marks DIRTY.
A build marks dirty tiles + 26 resident neighbours, rebuilds <= 2048 tiles into free pages
(16 patches/page, 131072 pages) and publishes FRONT/BACK tile index slots atomically.
Retired pages reclaim after publication; unresolved halos keep the previous record (PENDING).
Stereo depth mesh readout deleted. Device risk: one vertex pool is drawn by queue0 while the
native queue writes free pages of the same VkBuffer. GPU tests: `MerkabaReadoutGpuTests`.
Editor finding: plain writes to `_M8Counters` indices >=105 from some kernels were lost in
the editor Vulkan path; allocator state therefore lives at the head of `_M8RenderPageQueues`.

`MerkabaReadout.compute` QueryM8Readout selects every occupied HOT tile within
renderDistance+guard (no dirty-tile set); BuildReadoutVertices pays 27-tile halo +
1728 halo loads + 512 MAINs per tile, loading full KernelState before the emit
decision (C8 violation). `MerkabaIntegration.compute` FinalizeObservation sets
`readoutChanged` whenever `SURFACE_QUEUE_COUNT != 0`, so coalescing (C9) is almost
always true. Build cost O(occupied tiles) holds the serial native queue and blocks
the next observation -> scan AND readout slow down with scanned volume (U violation).

## RISK-8 — carve working set only grows [PARTLY ADDRESSED]

Correction: NEEDS_CARVE is cleared by strong FREE; the set is sticky, bounded by tiles in
the 5 m view, not by session time. Fixed: all COLD halo tiles of an attempt (up to 8) are
requested together. Evidence math untouched (contract E).

NEEDS_CARVE is set on every surface integration and cleared only at evidence <= -512.
QueryCarveTiles queues every HOT tile with `meta.w != 0` in the 5 m frustum; cost grows
with everything ever observed.

## RISK-9 — per-frame cull is O(published patches) [FIXED IN CODE, DEVICE ACCEPTANCE PENDING]

Fix: `CullRenderTiles` tests FRONT tile spheres (both eyes + draw distance), then
`EmitVisibleIndices` writes 96 indices per visible page only.

`MerkabaRenderFeature` runs CullReadoutVisibility every XR frame, one lane per patch,
rewriting FRONT indices + DrawArgs in place. No per-tile AABB coarse cull.

## RISK-10 — residency: no tile reclamation; LRU epoch advances only on readout builds [PARTLY FIXED IN CODE]

Correction: eviction does exist (clean HOT -> COLD, dirty via EVICTING writeback). Fixed:
work epoch advances on observation/erase finalize; readout no longer refreshes warm tiles;
eviction pins tiles by distance to the user (warm radius) via `SetResidencyFocus`; native
lease is released at the GPU fence (completion records are per job kind:
`_M8AttemptCompletion[0]` scan, `[1]` readout); `StorageControlReady` preempts native
submission for one frame so install/ack never starve. GPU tests: `MerkabaResidencyGpuTests`.

`M8_COUNTER_FRAME_EPOCH` increments only in ResetReadoutBuild; eviction refuses
tiles touched within SafeEpoch=3 builds. Nothing returns a live tile ref to EMPTY.
PumpStorage exits whenever the native job is in flight; 32-tile batches, 50 ms poll,
per-batch fsync (`MerkabaSsdStore` WriteThrough + Flush(true)), bitwise CRC.
FinalizeReadout skips publication if any dependency tile is COLD.

## RISK-11 — export: flush evicts world + chunk-window max-flow + 26x patch rebuilds [PARTLY FIXED IN CODE]

Fixed: SAVE/EXPORT flush is `Persist` (dirty HOT -> PERSISTING -> clean, tile stays HOT);
batches 128 tiles, chained without the 50 ms idle poll, one fsync per batch (no
WriteThrough), table CRC, reads through one handle per file in offset order.
Also fixed: owner-chunk-only candidate solve (context ring still read), per-group memo of
`TryBuildPatch` for closure donors, partition sink only at never-stored space (not at the
solve window), array-backed iterative Dinic, 4096-tile export cache with 512-tile reads,
anchor binding checked before the flush.

`FlushAllDirtyTilesAsync` rides the same 50 ms/32-tile pump and AcknowledgeWritebackBatch
moves every flushed tile to COLD (export is not residency-neutral). Per owner chunk the
exporter reads a 26-tile ring (2.3-3.4x context), runs Shell+Membrane+Dinic partition on
all of it, then OwnChunk discards the rest. TryInferClosure re-runs TryBuildPatch for up
to 26 neighbours without memo. Partition sink = window boundary -> seam-dependent result.

## RISK-12 — grid relocation on load uses Awake pose, not saved grid pose [FIXED IN CODE, DEVICE ACCEPTANCE PENDING]

Fix: checkpoint v4 persists `AnchorFromGrid = inv(A_save) * G_save`; `MerkabaGrid.SetAnchorFromGrid`
is the one authority and is re-applied on every `RoomSpaceRoot.Bound`. NEW = identity.
v3 checkpoints load once with `inv(AnchorAtSave)` and are marked dirty to upgrade.
Behavioural tests: `SavedGridRelationSurvivesTrackingOriginShiftBetweenBindAndSave`,
`LegacyCheckpointReconstructsGridRelationOnceAndUpgrades`.

`MerkabaGrid.RelocateForLoadedAnchor`: `anchorNow * anchorAtSave.inverse * _sceneGridToWorld`
where `_sceneGridToWorld` is captured once in Awake. Correct is
`anchorNow * inv(anchorSave) * gridSave` (exporter already stores
`SpatialAnchorMatrix.inverse * GridToWorldMatrix`). Any tracking-origin shift between bind
and SAVE mis-places reopened scans; compounds across cycles. Test asserts the wrong formula.

## RISK-13 — resume does not wait for an already-bound but untracked anchor [FIXED IN CODE, DEVICE ACCEPTANCE PENDING]

Fix: `CoordinateAuthorityGate` (generation + 5 stable tracked frames; invalidated by pause,
tracking-origin change, untracked, pose jump >1 cm/0.5 deg, rebinding). Bound UUIDs are
waited on, never reloaded; artifact bindings are promoted/released via the manager.
Observations capture GridToWorld + generation at prepare and abort on change. Resume
waits are frame-based and cancelled by pause; intent survives interrupted resume; depth
is restored even on anchor failure; START shows RETRY ANCHOR.

`RoomAnchorManager.EnsureSessionAnchorAsync` shortcut requires `Localized && IsTracked`;
otherwise it reloads a UUID already bound (SDK skips -> false). After wake head tracking
returns before anchor relocalizes -> "Room anchor not localized". 
`WaitForActiveSpatialAnchorReadyAsync` has no callers. Scanner submit gates on head pose
only, not anchor tracked/stable; no TrackingOriginChangePending / trackingOriginUpdated
handling; pause during unfinished resume clears `_resumeAfterPause`.

## RISK-16 — the analytic skin is published and exported at full density; the artifact viewer collapses [REPLACED IN CODE 2026-09-17 per .claude/MERKABA_SKIN_PLAN.md; DEVICE ACCEPTANCE PENDING]

Replacement: the union-skin facelet authority is deleted. Live readout emits one 25 mm
membrane quad per measured MAIN (4 knots, 6 indices) from the generated
`MerkabaOverlapShell` oracle; export builds the same patches and the GLB writer keys
identical knots (position, normal, colour) onto one vertex. Expected density: about two
triangles per measured kernel, i.e. triangles scale with sheet cells. The numbers below
are the state this replaces; a new device run and export must confirm the new ones.

Export `Scan 2026-09-16 23-15.zip` (build c1dfef8): 3,263,647 vertices, 5,379,025
triangles, 156 MB, largest leaf 1.6 M triangles, max edge 5.4 cm. Vertex duplication
fell ~5x, triangles only ~20%: the mesh is still one triangle per visible support facet.
Device (same build): BuildRenderTiles avg 241.8 ms / max 456.6 ms, readout job 259.7 ms,
VrApi FPS avg 32.9 / min 2 — the 20 Hz producer (50 ms) and the 72 Hz-draw-while-producer
contract are UNMET; the second Vulkan queue shares the Adreno, it does not add one.
The metre-long live triangles were a separate defect: the visible index stream padded
256-index pages (256 % 3 != 0), so page boundaries split triangles; fixed by exact
compaction (step 0 of the plan). Producer hard-off added for the four-mode measurement.

Still open after the review closure: every visible facelet is still one triangle
(no merge of adjacent coplanar facelets on CPU or GPU), so the triangle density of
the export and of the live publication is unproven until a new device export exists;
and vertex identity is shared between kernels inside one 8^3 tile only
(`M8SkinNeighbourKernel` returns invalid across tiles), so tile boundaries still hold
two vertex slots for one physical point (bit-identical position, no seam).

Fix (review closure `1f39344`..`163b647`): vertices are projected onto the measured
sheets (`P = Q + N (d - dot(Q - C, N))`, mean over the owner sheets in canonical order),
only free-side-facing facelets survive, one physical vertex has one owner per tile,
export keys neighbouring kernels onto one vertex, the publication is a strict two-slot
FRONT/BACK ping-pong validated before the swap, a failed BACK never publishes, and the
stage telemetry names follow the generator order. Contract tests:
`MerkabaSkinContractTests` (sheet residual, no tilted facets, shared identities, no
bridging edges) and `MerkabaReadoutGpuTests` (disjoint slots, FRONT untouched while
BACK builds, failed BACK keeps FRONT).

Original finding:

Device evidence after `232b421` (clean install, one room scan):

```text
export 3D Tiles "Scan 2026-09-16 20-42.zip"
  zip 120.0 MB, unpacked 512 MB, 9 tiles
  16 223 615 vertices / 6 772 144 triangles
  largest tile 000003: 4 218 561 vertices / 1 731 532 triangles / 138.9 MB
previous export (14:56, membrane-era skin)
  2 393 330 vertices / 1 307 947 triangles, zip 15.1 MB
ratio 6.8x vertices, 5.2x triangles, 8x package
```

The user reports the artifact viewer, which used to open instantly, now runs at about
1 FPS when the model is displayed outside ALIGN, the live readout flickers, and lines
appear between parts that are not connected.

Readout compute itself is not the cost (`BuildRenderTiles` 0.012 ms avg, whole readout
job 44 ms avg of which the measured stages are 0.1 ms, the rest is queue waiting);
frames were 17-18/72 while scanning and 35-37/72 idle.

Three candidate causes, none of them confirmed yet, listed so the next run does not
guess:

1. Density. 422 facelets over 210 shared vertices per measured kernel is the exact
   boundary of the union of 50 mm supports, but sharing stops at the tile border and
   coplanar facelets are merged only inside one support, so both the publication and
   the export carry every interior ridge of the union.
2. Export path. `BuildSkin` emits one `MerkabaExportMembranePatch` per facelet with its
   own three corners, so the GLB writer welds nothing: 16.2 M vertices for 6.8 M
   triangles is 2.4 vertices per triangle, i.e. almost no reuse.
3. Stray lines. Indices that address a vertex slot of another kernel or of a recycled
   page would draw exactly such connections. The first builds after a reset run with
   `_safeReclaimGeneration` = 0 and retire tags = 0, where the modular comparison treats
   the tag as already safe, so a page retired in the very first publication can be
   recycled while FRONT still references it.

## RISK-15 — the analytic skin is published at full density and the draw is now the bottleneck [SUPERSEDED BY RISK-16 FIX; the 0.012 ms BuildRenderTiles figure below was mislabelled telemetry, the true value was 43.6 ms avg]

The boundary arrangement publishes 422 refined facelets over 210 shared vertices per
measured kernel; an isolated kernel draws 422 triangles, a kernel inside a flat sheet
still draws ~70. On device (Quest 3S, clean install, commit `232b421`) the readout
compute is no longer the cost: its timed stages are `BuildRenderTiles` 0.012 ms avg
(max 0.021), `CollectRenderRebuildTiles` 0.045 ms, `CopyRenderIndex` 0.010 ms,
`RequestWarmResidency` 0.010 ms, `PublishRenderTileList` 0.004 ms. The readout job's
wall time is 44 ms avg / 116 ms max, which is queue waiting, not compute: the whole
native job waits 180-250 ms behind graphics work.

Measured frames: idle scene 35-37 FPS of 72 (App 24-31 ms). While scanning 17-18 FPS
(App 43-77 ms, VrApi GPU 51-55). Before this run the same scene ran 15-19 FPS with the
readout on the graphics queue, so the queue split helped but the draw volume now
dominates.

Open question for the next run: the cost is per-frame vertex/index throughput of the
published skin, not the publication. Candidates, in contract order: cross-kernel vertex
sharing beyond the tile, merging coplanar facelets across kernels (the arrangement only
merges inside one support), and avoiding the per-frame index copy by drawing published
ranges directly. No evidence yet that a lower facelet count is needed; the geometry is
exact and must not be degraded blindly.

## RISK-14 — design frame is package/grid-local, legacy paint migration double-transforms [FIXED IN CODE, DEVICE ACCEPTANCE PENDING]

Fix: the design root local frame is the session anchor frame
(`model * T(-scanCenter) * inverse(AnchorFromPackage)`); paint only opens when the package
AnchorUuid equals the active session anchor. `MerkabaDesignDocument` v2 stores anchor
coordinates; v1 migrates once through AnchorFromPackage and is saved dirty. Legacy
annotation paint imports anchor points directly (no world round-trip). Opacity 0 disables
model surface queries. Eraser and Spray are fail-closed on paint/model hits; SpatialBrush
stays 3D. Still open: brush radius/offset are world-sized; no live-membrane raycast.

Original finding:
Paint/objects parent under `_designDisplayRoot = modelRoot * T(-scanCenter)` (package
frame), not session room space; no AnchorUuid guard in OpenSessionDesign. Legacy
migration maps points to physical world via AnchorFromPackage then back through the
floating display root -> permanently stored "on world" strokes. Spray/eraser fallback
operate at controller+0.20 m; hidden (opacity 0) tiles still take model raycasts; brush
radius/offset are world-sized. Design works only inside GLB View; live membrane has no
raycastable representation.

## RISK-17 — hybrid build beb903c on Quest: 30 fps, judder on movement, three UX/visual defects [OPEN, DEVICE EVIDENCE 2026-09-17 04:38-04:51]

Evidence (logcat of the run on `beb903c`, `~/Stažené` hybrid patch 0430):
- Readout job (17 dispatches): n=86, avg 57.7 ms, max 186 ms (045be20 run: 244 / 1538 ms).
  Small jobs unchanged (5 dispatches 0.12 ms, 33 dispatches 4.6 ms). No per-kernel
  `gpu-operation-session` lines were emitted in this run, so the slow stage among
  Collect/Solve/Emit is not identified yet.
- VrApi FPS avg 30.4, min 15, Stale ~47/s, Prd 60-68 ms (045be20 run: avg 63.8).
  Judder on head movement matches rebuild bursts (dirty tiles from residency/halo) of
  60-186 ms stealing the single Adreno GPU from the draw. The halved baseline FPS is
  independent of rebuilds; prime suspect: the URP overlay camera for the UI layer
  (second XR camera pass, intermediate target + resolve), second: alpha-blended scan
  (ZWrite off) when scanOpacity < 1. Needs an A/B run with the overlay camera disabled.
- Log otherwise clean: 0 "Kernel at index invalid", 0 MerkabaNative errors,
  0 CAPACITY_FAILED, 0 PUBLICATION_INVALID, 55 native pipelines created.

Defects confirmed in code (not yet fixed):
1. GLB/3D Tiles viewer opacity still dithers: `MerkabaArtifactViewer.ApplyPreviewOpacity`
   sets `_AlphaDither` below 0.999 and `MerkabaArtifactPreview.shader` clips against a
   hashed threshold. Real alpha blend exists only for the scan draw (`MerkabaGrid.shader`).
2. Coverage (checker) film is tiled: hue is seeded per 0.2 m cell hash plus 25 mm parity
   base lift, so it reads as 20 cm squares. Required: a continuous, subtle field
   (fresnel + smooth low-frequency function of position, no floor()).
3. Refine ERASE capsule already starts at the controller along its axis (no depth gate),
   but it is not displayed: the erase branch of `TryCreateFineDescriptor` returns
   `cursorOnSurface = false` and `ControllerRayDriver.SetFineBrushPreview` hides the
   cylinder unless the cursor is on a surface; the scan-side highlight shows only where
   the membrane intersects the capsule. Required: show the tube from the controller
   for its full length regardless of surface hit.
