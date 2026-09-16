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
- `[~]` **C5/C7** (2026-09-16: analytic union-boundary skin, 26-neighbour key, 422 facelets over 210 shared vertices, one authority for runtime and export) Deterministic membrane + oracle cases. Implemented as specified;
  the C7 list is only partly covered — see RISK-2.
- `[x]` **C6** 4 vertices / 6 indices / 2 triangles per patch.
- `[x]` **C8** Tile-cooperative GPU build with groupshared occupancy + halo cache,
  cheap-occupancy-first, full `KernelState` loaded only for emitters.
- `[x]` **C9** Scheduler (2026-09-16: serial 20 Hz scan->readout transaction, publication generations with fence-gated page reclaim, 6.4 m world-sphere coverage, native scanner-queue readout at ABI 4; RISK-7, RISK-15). `MerkabaGridRenderer.LateUpdate` is the contract's conceptual
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
