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

## RISK-2 — the shared-corner invariant is asymmetric and only tested flat [OPEN]

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

## RISK-3 — legacy 32-chunk constants still live in the frozen persistence header [OPEN]

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

## RISK-7 — readout is a full rebuild, dirty after nearly every observation [OPEN]
`MerkabaReadout.compute` QueryM8Readout selects every occupied HOT tile within
renderDistance+guard (no dirty-tile set); BuildReadoutVertices pays 27-tile halo +
1728 halo loads + 512 MAINs per tile, loading full KernelState before the emit
decision (C8 violation). `MerkabaIntegration.compute` FinalizeObservation sets
`readoutChanged` whenever `SURFACE_QUEUE_COUNT != 0`, so coalescing (C9) is almost
always true. Build cost O(occupied tiles) holds the serial native queue and blocks
the next observation -> scan AND readout slow down with scanned volume (U violation).

## RISK-8 — carve working set only grows [OPEN]
NEEDS_CARVE is set on every surface integration and cleared only at evidence <= -512.
QueryCarveTiles queues every HOT tile with `meta.w != 0` in the 5 m frustum; cost grows
with everything ever observed.

## RISK-9 — per-frame cull is O(published patches) [OPEN]
`MerkabaRenderFeature` runs CullReadoutVisibility every XR frame, one lane per patch,
rewriting FRONT indices + DrawArgs in place. No per-tile AABB coarse cull.

## RISK-10 — residency: no tile reclamation; LRU epoch advances only on readout builds [OPEN]
`M8_COUNTER_FRAME_EPOCH` increments only in ResetReadoutBuild; eviction refuses
tiles touched within SafeEpoch=3 builds. Nothing returns a live tile ref to EMPTY.
PumpStorage exits whenever the native job is in flight; 32-tile batches, 50 ms poll,
per-batch fsync (`MerkabaSsdStore` WriteThrough + Flush(true)), bitwise CRC.
FinalizeReadout skips publication if any dependency tile is COLD.

## RISK-11 — export: flush evicts world + chunk-window max-flow + 26x patch rebuilds [OPEN]
`FlushAllDirtyTilesAsync` rides the same 50 ms/32-tile pump and AcknowledgeWritebackBatch
moves every flushed tile to COLD (export is not residency-neutral). Per owner chunk the
exporter reads a 26-tile ring (2.3-3.4x context), runs Shell+Membrane+Dinic partition on
all of it, then OwnChunk discards the rest. TryInferClosure re-runs TryBuildPatch for up
to 26 neighbours without memo. Partition sink = window boundary -> seam-dependent result.

## RISK-12 — grid relocation on load uses Awake pose, not saved grid pose [OPEN, CONFIRMED]
`MerkabaGrid.RelocateForLoadedAnchor`: `anchorNow * anchorAtSave.inverse * _sceneGridToWorld`
where `_sceneGridToWorld` is captured once in Awake. Correct is
`anchorNow * inv(anchorSave) * gridSave` (exporter already stores
`SpatialAnchorMatrix.inverse * GridToWorldMatrix`). Any tracking-origin shift between bind
and SAVE mis-places reopened scans; compounds across cycles. Test asserts the wrong formula.

## RISK-13 — resume does not wait for an already-bound but untracked anchor [OPEN, CONFIRMED]
`RoomAnchorManager.EnsureSessionAnchorAsync` shortcut requires `Localized && IsTracked`;
otherwise it reloads a UUID already bound (SDK skips -> false). After wake head tracking
returns before anchor relocalizes -> "Room anchor not localized". 
`WaitForActiveSpatialAnchorReadyAsync` has no callers. Scanner submit gates on head pose
only, not anchor tracked/stable; no TrackingOriginChangePending / trackingOriginUpdated
handling; pause during unfinished resume clears `_resumeAfterPause`.

## RISK-14 — design frame is package/grid-local, legacy paint migration double-transforms [OPEN, CONFIRMED]
Paint/objects parent under `_designDisplayRoot = modelRoot * T(-scanCenter)` (package
frame), not session room space; no AnchorUuid guard in OpenSessionDesign. Legacy
migration maps points to physical world via AnchorFromPackage then back through the
floating display root -> permanently stored "on world" strokes. Spray/eraser fallback
operate at controller+0.20 m; hidden (opacity 0) tiles still take model raycasts; brush
radius/offset are world-sized. Design works only inside GLB View; live membrane has no
raycastable representation.
