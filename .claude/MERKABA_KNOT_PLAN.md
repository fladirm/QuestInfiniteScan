# Merkaba knot-line membrane, atomic readout transaction, draw/UX closure (2026-09-17)

Binding implementation plan. Written before the code; every deviation must be recorded
here in the same commit. Base `ff158cf` (runtime `beb903c`). Serves contr.md C5-C7 and
the frozen invariants of CLAUDE.md; adds nothing that contradicts them.

## 0. Invariants that this plan must not bend

- Truth is M8 KernelState. Membrane, readout, GLB, 3D Tiles are derived; one oracle
  (`MerkabaOverlapShell`) with a hand-maintained HLSL twin; parity test bit-exact.
- One 25 mm patch per measured MAIN; four half-lattice corners; a corner is a SHARED
  KNOT computed once from its own neighbourhood, never from "who asks".
- Contributors only from the four immediately sharing tangent columns and normal
  layers -1/0/+1 of the MAIN that uses the knot. No distance-two bridging. FREE
  separates, UNKNOWN contributes nothing, parallel sheets stay separate.
- Publication: SCAN -> one BACK transaction -> validate -> ONE atomic FRONT/BACK swap.
  No intermediate PUBLISHED, no progressive FRONT, no tile ever appears a frame later
  than its neighbour. A rejected BACK keeps FRONT and the scan barrier.
- Quest kernels: flat, parallel, no private arrays, no int div/mod, <= 8 writable
  bindings, <= 8 barriers, <= 32 KB groupshared, no lane walks a tile serially.
- No second geometry/appearance authority, no invented points, no clamp to MAIN.

## 1. Oracle: knot per (line, chart, side, layer), identity by winners

Definitions (chart c, tangent axes t0/t1, pitch p = 25 mm, gap g = 0.6 p):

- Line: absolute half-lattice address `(2*main + sign0*e_t0 + sign1*e_t1)` with the
  c component zero. A line has four tangent columns (the 2x2 cells around it).
- Use: a measured cell with canonical chart c and free-side signature s at layer L
  (its c coordinate) in one of the four columns. `h_use` = its plane intersected
  with the line.
- Knot task key: (line, c, s, L). Owner of the task = lexicographically smallest use
  cell of that key inside the solving tile (halo cells never own a task; they are
  inputs only).
- Reference height `h_ref(line, c, s, L)`: uses of the key are clustered by height
  connectivity (|dh| <= g); the cluster that contains the owner defines the task;
  `h_ref` = median of its members' `h_use` (2 members: mean). Members outside the
  cluster are a different knot task of the same key (cluster ordinal in the key).
- Candidate: cell in the four columns at layers L-1..L+1, occupied, measured,
  `|N_c| >= 0.5`, signature s, not separated from the line by KNOWN FREE (the cell
  between candidate and layer L on the c axis, when the candidate is at L+-1).
- Admission: connected component of the member cells inside the 12-candidate window
  under |dh| <= g (bit masks, at most 3 expansions over 12 slots).
- Winner per column: admitted candidate with minimum |h - h_ref|; tie: smaller
  |layer - L|, then lexicographically smaller coordinate. Contract C5 step 6 with the
  residual taken against the knot's own reference instead of one MAIN.
- Knot: height = mean of winner heights (canonical column order 00,10,01,11), colour =
  confidence-weighted RGB of winners, normal = confidence-weighted mean of winner
  plane normals, normalised. Position = line point with c component = height.
- Identity: two knot tasks of the same (line, c, s) with the same winner cell set are
  ONE knot (bit-identical by construction: same inputs, same order). This is the
  exact merge; no epsilon. Different winner sets are different knots.
- Patch corner: MAIN (chart c, side s, layer L) uses knot task (line, c, s, L,
  cluster of MAIN). MAIN is a member of its own task, so the task always exists.
  A patch is valid iff its four knots exist and every edge <= 45 mm. Invalid or
  unresolved patch emits nothing. There is no fallback height.
- Boundary invariance: every input of a knot task lies within layers L-2..L+2 of the
  four columns (window +-1, signature +-1, separator). The 12^3 halo (tile +-2) covers
  it. Halo cells participate as uses and candidates exactly as in-tile cells, so both
  tiles sharing a line compute identical knots from identical inputs.

Canonical chart per measured cell (derived readout preprocessing, both twins):

- Smoothed normal = confidence-weighted sum of the cell's own plane normal and the
  plane normals of its six face neighbours that are measured, `dot(N_cell, N_n) >=
  0.5` (no abs: opposite sides never mix), no KNOWN FREE between (face neighbours
  have no cell between; the neighbour itself must not be FREE), and reciprocal plane
  residual: |plane_cell(centre_n)| <= g and |plane_n(centre_cell)| <= g.
- Chart = dominant axis of the smoothed normal, tie X before Y before Z. Radius 1 only.
- Needs halo 1 around every use cell; uses lie within halo 1 of the tile, so halo 2
  suffices. Computed once per tile into a 2-bit groupshared table (tile +-1 = 10^3).

Chart-transition stitch (part of the oracle, both twins, export included):

- For MAIN m (chart c) and its tangent face neighbour n = m + e_t (t != c), with chart
  c' != c and t != c' (both patches have an edge facing the shared cell face): both
  measured, `dot(N_m, N_n) >= 0.5`, reciprocal residual <= g, n not FREE.
- Owner = lexicographically smaller of m, n. The quad uses m's two corner knots with
  sign(t) = +1 (toward n) and n's two corner knots with sign(t) = -1, ordered along
  the remaining axis so the quad is not a bowtie; all four knots must exist and be
  four distinct knots; every edge <= 45 mm; winding from N_m + N_n.
- Emitted as six indices like a patch (indexCount stays a multiple of 6).
- Live GPU: only when both m and n are inside the solving tile (their knots are in
  this tile's scratch). Cross-tile stitches are DEFERRED (documented gap, section 9).
  Export (CPU, global context) emits all stitches.

## 2. GPU readout kernels and scratch

Per work tile (batch-local index w, absolute rebuild index batchBase + w):

- `CollectRenderPatchInputs` (128 lanes): tile identity, halo slots, occupancy,
  cell bits (occupied/known-free), plane cache decoded ONLY for cells whose bit is set
  in the needed mask (needed = cells within +-2 layers on c... computed simply as: all
  occupied cells of the halo; decode cost is one flags load + decode per occupied
  cell, empty cells cost one evidence load for the known-free bit). Chart table for
  tile +-1. Descriptors 512 fixed slots `uint4(kernelLocal | chart<<9 | side<<11,
  flags, colour, confidence)`; compact measured list (<= 512 uint) and count.
- `SolveSharedKnots` (64 lanes): task = compact index * 4 + corner (<= measured*4).
  Phase A: key, membership, owner test; owners solve (12 candidates, winners), write
  knot record `uint4(height bits, colour, key(line t0,t1 | c | s | L | cluster),
  winnerMask/normal packed)`; non-owners write a reference to the owner task.
  Phase B (after device barrier): owner with identical winner set to the task of the
  same (line,c,s) at layer L-1 (found through the use cell at L-1 in the owner
  column) references that task instead (exact merge). Phase C: patch validity
  (4 knots, 45 mm) and stitch validity, ordinals by atomic ticket, referenced roots
  flagged. Phase D: vertex ordinals for referenced roots (owner chain <= 3 hops,
  ordinals written only after ownership is final). Header: valid patches (+stitches),
  vertices, unresolved, measured.
- `EmitRenderTileGeometry` (128 lanes): pages (vertex pages from vertex count, index
  pages from 6*(patches+stitches)), one vertex per root, six indices per valid patch
  or stitch, page owner written at `M8TakeRenderPages`, record with delta index
  count, dirty/pending flags as today.
- Scratch per work tile: patch scratch 512 uint4 (descriptors) + 128 uint4 (compact
  list); knot scratch 2048 uint4; knot owner 2048 uint; header 1 uint4. Work tiles =
  `M8_RENDER_SCRATCH_TILES` = 64 (one batch). Memory: 64 * (640*16 + 2048*16 + 2048*4)
  = 3.1 MB.
- Removed: weld epsilon, support clamp, owner-chain weld over 12 neighbours,
  `RecountRenderPatches`, groupshared patch map, "beyond scratch capacity" branch.

## 3. Atomic transaction in batches (ABI 7)

- Native job kinds: `ReadoutBegin` (BeginRenderBuild .. PrepareRenderBuild, the
  coverage/warm traversal included, `_M8AllowWarmLoads` gates only cold loads),
  `ReadoutBatch` (AdvanceRenderBuildBatch, CollectRenderPatchInputs, SolveSharedKnots,
  EmitRenderTileGeometry), `ReadoutFinalize` (PublishRenderTileList,
  ValidatePublication, FinalizeReadout). Pipeline ranges in the plugin and in
  `MerkabaNativeVulkanExecutor`; generator unchanged in shape.
- `PrepareRenderBuild` stores the immutable rebuild count and resets the batch
  cursor (`M8_COUNTER_RENDER_BATCH_BASE`, `M8_COUNTER_RENDER_REBUILD_TILES`).
  `AdvanceRenderBuildBatch` (1 lane) sets `_M8FrameDispatchArgs[0] =
  min(remaining, 64)`, publishes the cursor for the batch kernels and writes the
  completion record slot 2 = (revision, cursorAfter, rebuildCount, scheduled).
- Renderer state machine: Begin submitted -> poll -> read record 2 -> while cursor <
  count: submit one Batch per LateUpdate (one native job per frame at most) -> submit
  Finalize -> poll -> decide publication from record 1 exactly as today. `_buildInFlight`
  and the scan barrier hold from Begin to the decision. Any error rejects the whole
  transaction (FRONT untouched). Batch size 64 is a constant, not a knob.
- Editor path (graphics queue): one command buffer records Begin, then
  `PhysicalTileCapacity / 64` repetitions of the Batch group behind indirect args
  (zero-group dispatches when done), then Finalize; publication readback as today.
- Completion record grows to 3 uint4 (48 bytes) in plugin, executor and grid.

## 4. Validation and warm residency

- Page owner (`OWNER_BASE`) written by `M8TakeRenderPages` (slot + 1), cleared by
  `ReclaimRenderPages` before the page returns to the free stack; `CopyRenderIndex`
  no longer clears owners. `_M8RenderIndexBack[1]` (total indices) is updated by
  delta in Emit and in `M8ClearDepartedRecord`; never recounted.
- `ValidatePublication` iterates the transaction rebuild list (all `_M8VisibleTiles`
  entries with y == 1), checks identity/version, page ids, chain length ==
  recorded pages, owner per page == slot + 1, `indexCount % 6 == 0`,
  `indexCount <= pages * 256`. Index-level range checks live in Emit (fail-closed
  publication-invalid). `RecountRenderPatches` deleted.
- `RequestWarmResidency` keeps building the BACK coverage list and `needsBuild` on
  every Begin; only `M8RequestWarmLoad` is gated by uniform `_M8AllowWarmLoads`
  (= residencyQuery). Renamed in comments only; kernel name kept for ABI tables.

## 5. Draw and UX

- Delete `MerkabaUiOverlayCamera`; pointer, cursor and fine cursor return to their
  original layers (their shader is already ZTest Always, queue Overlay).
- `MerkabaUiOnTopFeature` (URP ScriptableRendererFeature, Runtime/UI): one pass
  AfterRenderingTransparents, `DrawRenderers` filtered to the UI layer with shader
  tags UniversalForward/SRPDefaultUnlit/UniversalForwardOnly and a RenderStateBlock
  depth Always / no write. `RoomScanSetupWizard` adds the feature to the host
  renderer and removes the UI layer from its opaque/transparent layer masks.
- `MerkabaGrid.shader`: at `M8_ALPHA_BLEND` two passes: depth-only (ZWrite On,
  ColorMask 0, same clip/occlusion) and colour (ZTest Equal, ZWrite Off, blend).
- `MerkabaArtifactViewer.ApplyPreviewOpacity`: opaque = opacity >= 0.999 ->
  `ConfigureMaterial(material, colour, opaque)`; `_AlphaDither` and the dither branch
  of `MerkabaArtifactPreview.shader` deleted.
- Coverage film: fresnel + two smooth grid-space sinusoids, no floor/hash/parity.
- Erase preview: visible for Erase without a surface hit; laser tip =
  `CursorPosition + Axis * Length`.

## 6. CPU twin and export

- `MerkabaOverlapShell`: `CanonicalChart(cell, context)`, `TrySolveKnot(line, chart,
  side, layer, ownerCluster, context)`, `TryBuildPatch(main, context)` (4 knot
  lookups + edge guard + winding), `TryBuildStitch(main, tangentAxis, context)`.
  `Corner` keeps GridPosition/PackedColor/LineAddress/Chart/Normal; add `KnotKey`
  (winner cells) for exact dedupe. HLSL twin: same functions, same order of
  operations, `__HASH__`/`__PITCH__` placeholders as today.
- Export: patches and stitches from the same oracle; GLB dedupe by KnotKey.

## 7. Tests and gates

- Oracle fixtures (add): sloped 45 deg sheet across a tile boundary (bit-identical
  knots on both sides), three sheets stacked in one column (three knots), thin
  double-sided leaf (two sides, two knot families), bend over 45 deg (stitch quad,
  no gap), noisy normals +-30 deg around 45 deg (one chart after canonicalisation),
  isolated MAIN (one patch), 8x8 room with corner and doorway (existing).
- Generated HLSL equals the authority string; SPIR-V audit (73 kernels: Recount
  gone, Advance added); closure gate: no weld epsilon, no clamp, solve iterates
  compact tasks, Recount absent, owner at take, Advance/batch counters, no overlay
  camera, viewer without dither, coverage without floor.
- Device evidence: per-stage timing of one batch, submissions per transaction, no
  intermediate PUBLISHED, FPS producer on/off, export t/v, screenshots (tile edge,
  leaf bend, coverage), erase tube video.

## 8. Commit order

1. Draw/UX (section 5) — builds alone.
2. Oracle CPU + HLSL twin + export + oracle tests (section 1, 6).
3. Readout kernels, scratch, validate/recount/warm, renderer bindings, generator,
   gates (sections 2, 4).
4. Transaction (section 3): plugin, executor, renderer state machine, editor loop.
5. APK, clean install, ledgers.

## 9. Known limits after this plan (not defects of the plan)

- Cross-tile stitch quads are not emitted live (export emits them).
- A sheet steeper than 60 deg in its canonical chart is outside the chart's definition.
- A chart transition where the two sides fail the reciprocal residual is a seam, by
  contract (no bridging).

## 10. Deviations recorded during implementation (2026-09-17)

- [CLOSED by plan-closure diff] Same-tile chart-transition stitch quads are emitted
  from the four already solved edge knots. Export emits the same primitive; live still
  deliberately omits only cross-tile stitch ownership as stated in section 9.
- [CLOSED by plan-closure diff] Canonical chart uses the six face neighbours only and
  the confidence weighting specified in section 1, in the CPU and generated HLSL twin.
- `CollectRenderPatchInputs` no longer builds any halo cache; the solve kernel
  decodes the plane of every occupied halo cell and loads evidence for every empty
  one (the "needed mask" of section 2 is not implemented; measure first).
- Descriptors carry no colour (`z = 0`); colour and confidence of the winners are
  loaded from the kernel state when the knot is solved, exactly as the CPU twin.
- The knot solve kernel is named `SolveSharedKnots`; `AdvanceRenderBuildBatch`
  replaces `RecountRenderPatches` in the pipeline table (55 pipelines, readout 33..49,
  fine erase 50 unchanged). Job ranges: Begin 33..42, Batch 43..46, Finalize 47..49.
- Editor path records `PhysicalTileCapacity / 64` batch groups behind indirect
  arguments in one command buffer (no CPU round trip in the editor).
- The knot winner key packs the absolute layer of each winner modulo 16 (4 bits per
  column) plus the winner mask; two tasks within +-2 layers of each other therefore
  compare exactly.
- [CLOSED by plan-closure diff] Scratch capacity is the 64 tiles of one batch as
  specified in section 2. BACK records/pages, not scratch, persist between batch
  submissions. Any editor resource-lifetime regression must fail its fixture rather
  than silently changing the production memory contract.
- `EvictedTileLeavesThePublicationAndRetiresItsPages` asserted a state the contract
  forbids (a COLD tile inside the draw sphere publishing without it); it failed on the
  0ea6cb8 baseline too. The test now moves the head so the tile leaves coverage first.
- `FirstBuildPublishesMeasuredWallIntoPages` expected four vertex pages (4 vertices per
  patch); with shared knots an 8x8 sheet has 81 vertices, two pages. Baseline failed it.
- `PublicationInvalid` counter carries a reason bit mask (1/2/4 emit ranges, 8 record
  shape, 16 page id, 32 owner, 64 chain length, 128 slot range) for device logs.
