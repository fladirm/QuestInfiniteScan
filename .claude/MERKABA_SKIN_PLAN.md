# Merkaba skin — consolidated model and execution order (2026-09-17)

Written after the `c1dfef8` review, the device video 23:15, the export
`Scan 2026-09-16 23-15.zip` (3.26 M vertices / 5.38 M triangles / 156 MB, max edge 5.4 cm)
and the neighbour-connected polyhedron demo. This file is the plan I hold while
implementing. contr.md section C (C1–C7) is the contract it serves; it adds nothing that
contradicts it.

## 1. The model, one line per layer

```text
truth          M8 KernelState: occupancy, measured plane (N,d), free side, colour — 25 mm lattice only
connectivity   the 50 mm support says WHICH samples are joined: 26-neighbour graph filtered by
               sheet compatibility (same free side, plane residual within the branch gap)
complex        the connected sheet = kernels + links + filled lattice faces of ONE sheet identity
skin           C5/C6: one 25 mm patch per measured MAIN, 4 half-lattice corners in the dominant
               chart; the corner is a SHARED KNOT solved once from the corner neighbourhood
               (4 tangent columns x 3 normal layers), bit-identical from every side
vertex identity absolute half-lattice address + dominant chart + resolved height bits;
               shared by every patch of the same branch; across tiles duplicates are
               bit-identical (C6 forbids a global vertex hash map)
position       plane–line intersection of accepted contributors, mean in canonical column order
colour         confidence-weighted mean of the accepted contributors, same canonical order
mesh           2 triangles per measured kernel; triangles scale with sheet cells, never with
               kernels x support facets; one authority for live readout, GLB and 3D Tiles
publication    strict 2-slot FRONT/BACK, page ownership validation, EXACT index stream
               (no page padding, total % 3 == 0), rejected BACK keeps FRONT, unresolved retries
```

Two rules that must never be broken again:

- The support is connectivity, never tessellation. Do not triangulate a per-kernel hull
  and move its corners afterwards. Connectivity comes from the complex, position from
  the measured planes. (The 422-facelet `MerkabaSkin` did the opposite; it is deleted.)
- A visual artefact is first classified: connectivity (complex), position (projection)
  or stream (indices/pages). Never add a threshold or a mask to hide it.

## 2. What stays, what goes

Stays: two publication slots and the atomic swap after the native fence; native executor,
ABI 5, plugin logcat (`MerkabaNative`); plane decoding and projection; canonical integer
scaffold arithmetic; 26-neighbour dirty closure and journal; page ownership validation;
retry semantics (unresolved / capacity keep FRONT and the scan transaction); the canonical
world and residency; `MerkabaNearestGridNormalStep` as the normal chart.

Goes: `MerkabaSkin` (facelets, occluder masks, facing threshold, VERTEX_LOOKUP), its
generator and generated HLSL, `M8SkinFaceletVisible`, the 422-facelet walk in
`BuildRenderTiles`, tile-local template ownership, padded visible index stream, facelet
emission in `MerkabaExportMembrane.BuildSkin`, `MerkabaSkinContractTests`.

## 3. Order of work

0. Independent of the skin: exact compaction of the visible index stream
   (used per page -> exclusive prefix -> copy exactly `used` -> indexCount = sum,
   gate `sum % 3 == 0`); diagnostic producer hard-off (`readoutProducerEnabled`, gates
   `LateUpdate` submissions, not only the draw); ledger: RISK-16 = ROOT DESIGN NOT CLOSED
   until this plan lands, 72 Hz-with-producer contract marked unmet (avg 32.9 / min 2).
1. CPU oracle = `MerkabaOverlapShell` with FINDING B fixed: contributor compatibility is
   `abs(candidateNormal[dominant]) >= MembraneCompatibleAxisCosine`, same free-side
   signature and the connected height branch — no exact `DominantAxis ==` and no
   `CanonicalSheet ==` predicates. Corner solve also yields the colour. Fixtures (C7 +
   review): isolated, pair, line, 2x2, 8x8 across a tile boundary, 45° and quantized
   slopes, convex/concave corner, T, doorway, thin double-sided wall, two close parallel
   sheets, FREE separator, UNKNOWN neighbour, negative coordinates, chunk/block boundary.
   Asserts: shared corners bit-identical, 2 triangles per measured kernel, no bridging
   across FREE, no merge of parallel sheets, translation invariance.
2. Export (GLB + 3D Tiles) from the same patches with global corner dedupe on CPU
   (dictionary keyed by half address + chart + height bits), then a new device export and
   the counts compared with 3.26 M / 5.38 M.
3. GPU: `BuildRenderTiles` around the generated `M8TryBuildMembranePatch`; per tile:
   phase A corner heights into groupshared (512 x 4 floats), phase B corner ownership =
   lexicographically smallest in-tile sharer with bit-equal height, prefix sums, phase C
   owners write vertices (<= 4 per kernel), every kernel writes 6 indices to owner slots.
   Budgets: <= 32 KB groupshared, no private `KernelState` arrays, <= 8 writable buffers,
   whole readout cycle < 50 ms, 72 Hz draw while the producer runs (this is the
   acceptance, not the queue structure).
4. Parity CPU vs GPU on the fixture corpus incl. halo across tile/chunk/block, then APK,
   clean install, the four measurement modes (DRAW x PRODUCER on/off), screenshots, export.

## 4. Deliberate deviations, stated

- Cross-tile vertex sharing is by bit-identical duplicate corners, not one global index:
  contr.md C6 forbids a global vertex hash map on the GPU path; the export dedupes globally.
- No coplanar-region merging: with shared 25 mm corners the mesh is already ~2 triangles
  per measured kernel; merging would need a second geometry authority.
- Live GPU emission writes four knots per patch (bit-identical duplicates between
  patches, contract C6); the export dedupes identical knots. In-tile ownership phases
  were dropped in favour of the reviewer's reference: one atomic ticket per patch,
  an upper-bound page take trimmed after emission.
- Visible pages are `uint4(page, used, streamOffset, 0)`; cull control is
  `[pages, exactIndexCount, overflow]` and PrepareVisibleIndices rewrites it into the
  emit dispatch after copying the exact count to the draw arguments.

## 5. Revision after the 045be20 review (this patch)

`BuildRenderTiles` is split into three flat dispatches per rebuild tile:
`CollectRenderPatchInputs` (patch descriptors only, O(measured MAINs)),
`ReduceAndSolveSharedKnots` (plane cache, one corner solve per task, weld of corners
of one line/chart/free side within 1 mm so one knot is one vertex, support guard: a
knot farther than one lattice step from MAIN's plane falls back to MAIN's height) and
`EmitRenderTileGeometry` (pages, one vertex per root knot, six indices per patch,
45 mm edge guard degenerates a patch, records). Scratch UAVs: patch descriptors,
corner results, knot owners, header; 512 rebuild tiles per build, the rest stay
dirty (carry-over). Native: pipelines 55 (readout 33..49, fine erase 50), resources
52, ABI 6. Draw: real alpha blending selected on the CPU (no dither coverage);
coverage mode = dark base + fresnel film. Erase: controller capsule, kernel-centre
test, cells become KNOWN FREE at once.

## 6. Hybrid pass (2026-09-17, after the two alternative reviews)

Taken from the alternatives: the corner solve is register resident in both twins
(three `float4` height registers, validity and free-side bits in scalar masks, the
branch as a `[low, high]` interval, no `[12]` private array); the three draw kernels
live in `Runtime/Resources/Merkaba/MerkabaVisibility.compute` loaded by the renderer
with `Resources.Load`, so the native producer asset holds exactly its 17 stages and
Android cannot renumber the draw kernels into it; patch descriptors have a fixed
`kernelLocal` slot (no atomic compaction, no 2 KB groupshared patch map, geometry
order deterministic); a patch that fails the support or edge guard is absent from the
index stream instead of emitting degenerate triangles.

Kept against the alternatives: the 1 mm weld epsilon (the solve depends on MAIN's
plane, so corners of one knot are equal only up to rounding); the support guard falls
back to MAIN's own measured plane height instead of dropping the measured MAIN; a
build beyond 512 dirty tiles publishes what it solved and leaves the rest dirty
(progressive), it never rejects BACK for capacity; every phase is parallel (ordinals
by atomic ticket), no lane walks 512 patches or 2048 tasks serially.

Fixed on the way: the solve kernel prologue filled the halo cache with a 128-lane
stride in a 64-lane group (half the cache stayed empty); vertex ordinals were
assigned while other lanes still followed owner chains (a lane could read an ordinal
as a task index). Ownership is now final before any ordinal is written.

UX: the annotation note field is read-only (one keyboard authority: "Edit note");
selecting an annotation no longer opens the system keyboard; while the keyboard is
open no pointer tool runs; the keyboard session retires when input focus returns or
when the keyboard never opened. The UX, the pointer and the cursor are drawn by a
depth-clearing URP overlay camera on the UI layer: always on top of the scan and the
GLB viewer. DEVICE ACCEPTANCE PENDING for all of the above.
