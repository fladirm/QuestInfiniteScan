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
- PRISM control checkpoints `9fee431` and `00fee18` are pushed on the active branch;
  current frozen Cone-PRISM spec/DAG refinements are not yet committed.

## Current DAG position

- `Q3-01` is done: fork/build/device baseline, archive branch, active PRISM branch,
  and canonical spec are preserved.
- `Q3-02` is the only active run: coherent 2x RGB + 2x depth + exact pose/calibration
  immutable GPU capture.
- No new PRISM reconstruction core is yet falsely claimed implemented. The next code
  change is the capture contract, not another architecture rewrite.

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

1. Validate and commit `specka.md`, canonical 22-run DAG, controls, and decisions.
2. Implement `StereoRigFrame`, calibration/pose/timestamp value contracts, GPU
   resource leases, rejection reasons, and deterministic tests.
3. Replace single-PCA discovery with two explicitly eye-keyed Meta PCA streams while
   retaining old `ICameraProvider` only as a temporary fallback adapter.
4. Expose both environment-depth slices plus frame FOV/pose/near-far metadata into
   the same coherent frame without CPU image copies.
5. Run Unity tests/build, deploy, and physically verify `Q3-02` before closing it.

## Safety

- Never delete, move, compress, prune, or modify `~/.codex` or Codex sessions.
- Keep builds/caches/captures on Kingston; do not commit them, device IDs, addresses,
  credentials, or room imagery.
