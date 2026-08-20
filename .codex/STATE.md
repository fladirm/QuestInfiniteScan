# Execution state

Updated: 2026-08-20 (Europe/Prague)

## Source of truth

- `specka.md` is the canonical Cone-PRISM-Q3 implementation specification;
  reconstruction physics `CPQ3-2026-08-20-v1` is frozen for implementation.
- `.codex/TASK_DAG.json` contains only the canonical `Q3-01` through `Q3-22` runs.
- Current goal: pure-Quest finite ConeEvents and probabilistic one-sided
  ContactFilms with SurfaceChartGeometry, not TSDF/DTSDF, fixed
  surfels/triangle soup, GS, DiffSoup, or server reconstruction.
- Never simplify away finite pixel footprints, explicit unknown-behind semantics,
  quadratic manifolds, range/footprint-aware pressure and stored film resistance,
  posterior information/uncertainty, multihypothesis first-hit semantics,
  persistent 3D ContactBoundaries, soft-to-hard shell,
  surface-conditioned stereo/temporal focusing, hierarchical displacement,
  measured texture superresolution, directional appearance, or resumable chunks.

## Repository and branch safety

- Writable fork: `git@github.com:fladirm/QuestInfiniteScan.git` (`origin`).
- Upstream QuestRoomScan has push disabled.
- Active branch: `feat/quest-radiance-meshlets` (Cone-PRISM-Q3 implementation).
- Preserved pre-PRISM checkpoint: commit `e9f37c1`, pushed as
  `origin/archive/hybrid-diffsoup-checkpoint-20260820`.
- The archive preserves the old DAG and all hybrid/DiffSoup/DTSDF work. Do not
  rewrite it.
- PRISM control checkpoints `9fee431`, `00fee18`, and frozen-spec commit `a29be7b`
  are pushed on the active branch.

## Current DAG position

- `Q3-01` is done: fork/build/device baseline, archive branch, active PRISM branch,
  and canonical spec are preserved.
- `Q3-02` is done: coherent 2x RGB + 2x depth, exact timestamped poses/calibration,
  immutable GPU leases, fail-closed pairing, and diagnostics compile and pass focused
  contracts. Its physical diagnostic is intentionally batched into the first
  capture-to-film Quest milestone.
- `Q3-03` is done. Immutable calibration epochs, four GPU cone LUTs, radial metric
  depth normalization, validity flags, exact-pose reprojection, surface-footprint
  Jacobians, and fenced output leases are implemented and wired into `RoomScanner`.
- `Q3-04` through `Q3-08` are implemented. The GPU pipeline now runs coherent
  stereo preprocessing, dual-eye prediction raster, exhaustive finite-cone
  classification, robust ContactFilm spawn, normalized information accumulation,
  quality-resistant 6x6 pressure solve, and immediate meshlet publication without
  pixel readback or CPU geometry.
- `Q3-09` is implemented. Prediction now exposes a generation-safe second depth
  peel; classification can reassociate a compatible hidden layer while keeping the
  foreground occluder separate, and refinement applies pressure and contradiction
  to the correct films without proximity averaging or carving behind first hit.
- `Q3-10` is implemented. Boundary events accumulate calibrated depth/normal/RGB,
  footprint, adjacency, and distinct-view evidence; a GPU +/-4 px RGB edge search
  focuses the film UV posterior and persists four chunk-local 3D cubic controls.
  Promotion is multi-view, absence is contradiction-only, and retirement is
  conservative. Persistent curves protect association and snap adaptive meshlet
  vertices without CPU traversal/readback. PRISM format v2 stores the expanded
  resumable boundary state.
- `Q3-11` is implemented. A segmented GPU Grid16/Grid8 hierarchy stores measured
  displacement, sigma, support, coverage, residual modes, best precision and best
  footprint without any binding exceeding 128 MiB. Close/precise observations resist
  weaker later pressure. GPU-indirect split/merge transforms quadratic H/g posterior,
  support, microdetail, generations, and multi-segment ContactBoundaries rather than
  discarding them. Derived meshlets sample the deepest detail and its normal derivative.
  Unity imported all kernels and passed 104/104 runnable tests (3 intentionally skipped).
- `Q3-12` is active: replace the current whole-generation adaptive builder with
  generation-safe dirty meshlet arenas, exact boundary clipping, independent dynamic
  geometry/appearance LOD, frustum/Hi-Z culling, and fully indirect visible draw lists.
- Foundations already implemented ahead of their formal DAG position: Q3-12 already has a
  bounded curvature/sigma/displacement-aware indirect meshlet builder; Q3-13 has a strict
  native canonical chunk codec, fenced asynchronous GPU snapshot capture, and
  atomic `WorldStore` publication. These runs stay pending until their remaining
  acceptance work is complete.

## Reuse map

Keep/adapt:

- QuestRoomScan Meta XR/OpenXR/Vulkan setup, permissions, tracking, anchors, Android
  storage/build/deploy, UI/input shell, and GPU resource retirement.
- `Runtime/World/WorldManifest*`, `WorldStore`, pose graph, transforms, and atomic
  revision foundations; payload becomes Cone-PRISM film/page state.
- `Runtime/Export/ChunkGlbWriter`, `WorldGlb*`, deterministic PNG, and glTF validators;
  source becomes stable PRISM meshlets/appearance.

Replace during `Q3-02`–`Q3-13`:

- Single-eye `PassthroughCameraProvider`/`ICameraProvider` with coherent stereo rig
  capture and GPU frame leases.
- `DepthCapture`, `VolumeIntegrator`, `GPUSurfaceNets`, `MeshExtractor`, and
  `GPUMeshRenderer` with the 17 GPU passes and PRISM orchestrators in `specka.md`.
- `SubmapManager`/persisted mesh cache with PRISM chunk residency and resumable page
  state; explicitly eliminate the archived four-rollover disappearance failure.
- Keyframe/atlas model with information-gain views and immediate surface-space
  virtual appearance pages.
- `RoomScanner` god object and debug-centric UI with a thin PRISM workflow.

Remove from the shipped product after PRISM parity:

- Scalar TSDF, proposed DTSDF, Surface Nets, triplanar canonical path.
- `Runtime.GSplat`, `Runtime/HeavyCompute`, DiffSoup resources/client/renderer, Python
  server, and their operator controls.

## Retained evidence

- Foundation verifier:
  `/mnt/kingston-unity/Builds/Verification/20260820T132533Z/verification-report.json`.
- Archived lifecycle failure corpus:
  `/mnt/kingston-unity/Builds/DeviceCaptures/2026-08-20-141107-revisit-disappears/`.
- These prove the reusable baseline and a regression to eliminate, not the PRISM
  mapper.

## Immediate implementation actions

1. Complete Q3-12 adaptive boundary-aware generation-safe meshlets, GPU culling,
   dynamic LOD, and indirect visible draw publication.
2. Complete PRISM page rehydration
   while keeping the scan hot path GPU-only.
3. Batch the first physical Quest validation after Q3-12, when multilayer films,
   boundaries, displacement, adaptive meshlets, and preview form one useful vertical slice.

## Safety

- Never delete, move, compress, prune, or modify `~/.codex` or Codex sessions.
- Keep builds/caches/captures on Kingston; do not commit them, device IDs, addresses,
  credentials, or room imagery.
