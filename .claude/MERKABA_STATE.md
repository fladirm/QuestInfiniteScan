# Implementation state vs `contr.md` (Claude, verified 2026-09-05 @ 863e75d)

Read by direct source inspection. `[x]` = implemented and covered by a test I located;
`[~]` = implemented, coverage partial or narrow; `[ ]` = not implemented.

## Data flow as it actually exists

```text
Environment Depth L/R + PassthroughCamera L/R   (owned, immutable, timestamped)
        -> StereoRgbdRefine.compute             one joint endpoint H + joint normal N
        -> DepthDilation (9 passes)             derived occlusion/FREE support only
        -> DiscoverSurfaceCandidates            one candidate = one canonical key
        -> ResolveSurfaceBlocks/Chunks/Tiles    deterministic owner routing
                                                DISCOVERY / SUPPORT / REVISION
        -> IntegrateSurfaceCandidates           evidence + RGB + measured plane -> KernelState
        -> QueryCarveTiles / IntegrateCarveTiles same-ray FREE, OFF+1 clamp
        =  M8   block 256^3 -> tile 8^3 -> 512 kernels, 32768 HOT tiles, SSD COLD
        -> QueryM8Readout / BuildReadoutVertices  membrane oracle, BACK slot
        -> FinalizeReadout                        atomic publish to FRONT
        -> MerkabaGridRenderer indirect indexed draw (one per XR frame)

offline: MerkabaExportShell -> MerkabaExportMembrane -> MerkabaGlbWriter
                                                     -> MerkabaTilesetWriter (3D Tiles ZIP)
```

## Section-by-section

- `[x]` **A** Invariants. `MerkabaConstants` / `KernelState` match the frozen values;
  `MerkabaSpatial` holds the block/tile/PCG3D/cuckoo address contract.
- `[x]` **C1** View removed from membrane topology. All eye-mask / front-depth symbols
  survive only as *negative* assertions in `MerkabaGpuIntegrationTests`.
- `[x]` **C2** Pin/glyph/noodle path deleted; same negative-assertion evidence.
- `[~]` **C3** One oracle. `MerkabaOverlapShell` is the CPU authority and is genuinely
  shared by live-readout codegen, GLB and 3D Tiles. But see RISK-1: the "generated"
  HLSL is a hand-written string literal, not a translation of the C# algorithm.
- `[x]` **C4** 25 mm pitch. `MembranePatchPitch = LatticeStep`, `HalfSupport` is never
  a patch half-extent (`PatchHalfExtent` has zero occurrences).
- `[x]` **C5/C7** (2026-09-17: the 422-facelet `MerkabaSkin` is deleted; live readout,
  GLB and 3D Tiles consume `MerkabaOverlapShell` — one 25 mm quad per measured MAIN whose
  four corners are shared knots solved once from the corner neighbourhood, with the
  knot colour and export normal from the same contributors. Contributor compatibility is
  residual-based (`MembraneCompatibleAxisCosine`, branch gap, free side), not the two
  quantized equalities of GEOMETRY_REVIEW FINDING B. Oracle fixtures: isolated, pair,
  plane corners, 3x3 and 8x8 walls across a tile boundary (one knot per corner), doorway,
  chart-boundary noise, quantized slope, FREE separator, UNKNOWN, parallel sheets,
  translation/tile/chunk/block invariance, generated HLSL identical to the CPU text.)
  Plan and deviations: `.claude/MERKABA_SKIN_PLAN.md`.
- `[x]` **C6** 4 vertices / 6 indices / 2 triangles per patch.
- `[x]` **C8** Tile-cooperative GPU build with groupshared occupancy + halo cache,
  cheap-occupancy-first, full `KernelState` loaded only for emitters.
- `[x]` **C9** Scheduler (2026-09-16: serial 20 Hz scan->readout transaction closed only by a published BACK, strict two-slot FRONT/BACK publication with draw-fence release and pre-swap validation, 6.4 m world-sphere coverage, native scanner-queue readout at ABI 5 with host-visible completion copy, exact visible index stream (no page padding, whole triangles), producer hard-off for measurement; RISK-7, RISK-16). `MerkabaGridRenderer.LateUpdate` is the contract's conceptual
  form verbatim: scanner work wins, coalesced dirty, no head-motion rebuild, no
  `GridToWorld`-difference rebuild.
- `[x]` **C10** FRONT/BACK. Full capacity per slot, failed BACK leaves FRONT untouched.
- `[x]` **D1** Owner bug fixed: `targetCoord = bestOwner` with REVISION/SUPPORT
  authority; `nearestKernel` only on DISCOVERY.
- `[x]` **D2** Fast fill: strict + planar + DISCOVERY raises `surfaceDelta` to
  `OCCUPIED_ON - max(evidence,0)`; thresholds untouched.
- `[x]` **E1/E2** Carve: cross-tile halo, same-ray, SURFACE precedence, five-condition
  clear, otherwise clamp to `OFF+1`.
- `[x]` **F** FINE is an exact controller-ray cylinder. `FineBrushDescriptor` has
  exactly the contract fields; eye/cone/soft-weight symbols only in negative tests.
  Preview, CPU admission, shader mutation and ERASE share one descriptor.
- `[x]` **G** ERASE is explicit canonical reset through `EraseFineTiles`.
- `[~]` **H** Anchor authority (2026-09-15: RISK-12/13 fixed in code, device pending). `RoomScanner` + `RoomAnchorManager` gate creation to
  NEW; export uses the session UUID and cannot create.
- `[x]` **I** Named sessions. `MerkabaSessionCatalog` + `sessions/<uuid>/session.json`;
  NEW/OPEN/SAVE/SAVE AS/RENAME/DELETE on `RoomScanner`.
- `[x]` **J** 3D Tiles empty export: per-owner-group counters and an immediate throw
  on `occupied>0 && measured>0 && ownedPatches==0` (`0011dcf`).
- `[~]` **K/L** Production UX. SCAN / REFINE / DESIGN / VIEW tabs exist, USS/UXML were
  rewritten, UI wins the raycast. Not audited against the visual-language checklist.
- `[~]` **M** Colour: HSV wheel, recent/saved swatches, eyedropper wired.
- `[~]` **N** Paint (2026-09-15: design v2 in session-anchor frame, RISK-14): `MerkabaPaintEngine.cs` + `MerkabaDesignDocument.cs` appeared
  UNTRACKED at 02:27 on 2026-09-05 from the concurrent Codex session. Not reviewed here.
- `[ ]` **O** Object library: `MerkabaDesignLibrary.cs` does not exist; no GLB import,
  no `library/` store, no placement persistence.
- `[x]` **S** Grep gates: every forbidden symbol has zero production occurrences and is
  additionally locked by a negative test.
- `[~]` **T** Build gates: all gates are runnable; see
  `MERKABA_ENV.md` for the last recorded results.
- `[ ]` **U** Device acceptance: DEVICE ACCEPTANCE PENDING.

## Documentation truth

`README.md` and `ALGORITHM.md` still describe the superseded 32^3-chunk /
24-face-quadrant / 26-neighbour-mask design and name files that do not exist
(`MerkabaChunk.cs`, `MerkabaTopology.compute`). They are historical. `contr.md` and the
source are authoritative.

## 2026-09-17 knot-line membrane and batched readout transaction [SUPERSEDED by the line-node section below]

- Membrane: `MerkabaOverlapShell.TrySolveKnot` (CPU) and `M8MembraneSolveKnot` (HLSL twin)
  solve one knot per (line, chart, side, layer) from the line's own uses; identity =
  winner cells (`KnotIdentity`); canonical chart per cell from the face-neighbour sheet
  (`CanonicalChart`). No weld epsilon, no support clamp, no owner-chain weld.
- Readout: one BACK transaction = ReadoutBegin -> ReadoutBatch (64 tiles, one per
  frame at most) -> ReadoutFinalize; scratch 64 tiles; page owner written at
  allocation; index total by delta; ValidatePublication over the rebuild list;
  RecountRenderPatches deleted; warm cold loads gated by `_M8AllowWarmLoads`. ABI 7.
- Draw/UX: UI layer drawn by `MerkabaUiOnTopFeature` after transparents (depth Always);
  translucent scan = depth-only pass + ZTest Equal colour pass; viewer opacity is real
  alpha; coverage film continuous; erase tube shown from the controller.
- Plan and deviations: `.claude/MERKABA_KNOT_PLAN.md`. DEVICE ACCEPTANCE PENDING.

## 2026-09-17 line-node membrane C5.1-C5.4 (C2+C3 of `.claude/MERKABA_LINE_PLAN.md`)

- Membrane authority: `MerkabaOverlapShell` from the 045be20 rules. C5.1 `CanonicalChart`
  (26-neighbour sheet sum, raw-axis fallback); C5.2 `TrySolveNode` (components of line
  uses under R, span <= 2 layers else no node, common window, admission, per-column
  winner, column-order mean; `NodeKey` = line, chart, side, min layer, min column);
  C5.3 `TryResolvePatch` (four nodes, 45 mm edge and g plane guards); C5.4
  `TryBuildTransition` (existing nodes only, across tile edges). `TrySolveKnot`,
  `KnotIdentity`, `TryBuildStitch` are deleted; the HLSL twin is regenerated.
- Readout batch: Advance -> Collect -> `SolveSharedKnotLines` (18^3 flags cube, 16^3
  chart table, one C5.2 solve per touched line task, invalid branches counter 128) ->
  `ResolveRenderPatches` (patches, transitions, vertex ordinals) -> Emit (one vertex per
  referenced node). Scratch `RenderLineUseScratch` 15.9 MB, `RenderNodeScratch` 3.9 MB.
  Native ABI 9, 60 pipelines.
- Tests: span 3, FREE separator, shrub, noisy diagonal, chart fallback, fold in and across
  a tile, window -5..12 equals global, 045be20 corners on wall and room corner, GLB
  vertices = node keys, GPU numeric parity on the corpus. Deviations and the C5.1/C5.4
  fixture findings: plan section 13. DEVICE ACCEPTANCE PENDING.
