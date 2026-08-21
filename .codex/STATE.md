# Execution state

Updated: 2026-08-21 (Europe/Prague)

## Source of truth

- `specka.md` is the canonical Cone-PRISM-Q3 implementation specification;
  reconstruction physics `CPQ3-2026-08-21-v3` is frozen for implementation.
- `.codex/TASK_DAG.json` contains the only active `Q3-01` through `Q3-22` pursuit.
- The product remains a pure-Quest finite-cone/contact-film scanner. Do not simplify
  it to TSDF/DTSDF, fixed plates, surfels, triangle soup, Gaussian reconstruction,
  constant averaging, or a server path.
- Mandatory quality mechanisms remain: four calibrated GPU streams, first-hit/free/
  unknown semantics, one-sided quadratic ContactFilms, information/covariance,
  persistent local pressure versus stored resistance, continuous support,
  multihypothesis layers, BoundaryCurves, hierarchical displacement, soft-to-hard
  uncertainty, stereo/temporal focusing, measured surface-space superresolution,
  directional appearance, adaptive meshlets, and resumable chunks.

## Repository and branch safety

- Active branch: `fix/cone-prism-contact-domain-resume-20260821`.
- Its base checkpoint is `93ac693`; the repair checkpoint is represented by the
  branch `HEAD` and its exact hash is recorded in the source-archive filename and
  APK build evidence.
- Preserved pre-PRISM work remains at
  `archive/hybrid-diffsoup-checkpoint-20260820` (`e9f37c1`).
- Preserved failed event-chain prototype remains at
  `archive/prism-event-chain-20260821` (`125a7aa`).
- Do not push this repair. Before deployment, make one local commit and create a
  workspace source ZIP from that exact commit with `git archive`.

## Current DAG gate

- `Q3-01` through `Q3-10` remain accepted.
- `Q3-11` is the only `in_progress` node. It was reopened from physical evidence:
  the first visible PRISM scan was metrically promising but exposed rectangular
  patch support, view-axis artifacts, severe mesh-build cost, and destructive
  revisit/Stop behavior.
- `Q3-12` through `Q3-15` contain substantial implemented code but remain `pending`
  behind Q3-11 and one consolidated physical acceptance. They are not claimed done
  merely because they build.
- `Q3-16` through `Q3-22` remain pending. No later quality mechanism was removed or
  replaced by the current repair.

## Implemented repair checkpoint

- Contact support is a continuous interpolated Grid16 manifold domain seeded by
  finite cone footprints. Rectangular film extents no longer authorize triangles,
  and boundary-crossing cells retain supported geometry instead of being discarded.
- Every base displacement cell now persists displacement, sigma, information,
  support, coverage, best precision/footprint, `freeSpacePressure`, a ten-bin
  eye/angular evidence mask, and revision in a 40-byte GPU ABI.
- Compatible contact cancels opposing pressure. Local erosion requires at least two
  independent bins, pressure above the stored close-view resistance, consumes its
  work, and cannot be multiplied through microtiles. Nothing behind first hit is
  carved.
- Split/merge resamples the real support domain and preserves pressure/detail;
  children do not receive duplicated contradiction evidence.
- `MeshletBuild.compute` now uses one `8x8` workgroup per film. Its 64 lanes cache
  up to all 289 support samples and cooperatively emit the full supported 17x17 /
  512-triangle base materialization. This removes the serial one-thread-per-film
  bottleneck without lowering canonical or display detail.
- Display presentation no longer uses a mono `Camera.main` cull for stereo XR. The
  preview uses depth-correct coverage dithering; UI/compositor alpha is untouched.
- Ordinary Stop/Start now pauses only sensor ingress and retains the canonical GPU
  graph, arenas and last meshlet publication. Full teardown is explicit only.
- Native PRISM schema v4 persists support and local pressure. Strict v3/v2 readers
  widen legacy 144-byte film headers and 32-byte cells with new state zeroed.

## Verification evidence

- Real-Vulkan Unity EditMode: 119 total, 116 passed, 0 failed, 3 intentionally
  ignored. Results: `/mnt/kingston-unity/Builds/TestResults/editmode-results.xml`;
  log: `/mnt/kingston-unity/Builds/TestResults/editmode.log`.
- Android Vulkan/IL2CPP precommit build passed with no C# or shader errors:
  `/mnt/kingston-unity/Builds/QuestInfiniteScan/QuestInfiniteScan-dev.apk`, SHA-256
  `3a2ec28413d9dc6be6f8d1d2a560da2a4b0f0ac35f9c3f082a92920d822ab13e`.
- The final exact-commit build/deploy has not yet run. The last small shader warning
  cleanup occurred after the precommit APK, so that APK must not be presented as the
  final repair artifact.

## Changed implementation surface

- Scan lifecycle: `Runtime/Core/RoomScanner.cs`.
- Cone/contact/topology/refinement: `Runtime/Prism/**` and
  `Runtime/Resources/Prism/**`.
- Native persistence migration: `Runtime/World/PrismCanonicalChunkCodec.cs`.
- Contracts: `Tests/Editor/PrismPersistenceContractTests.cs` and
  `Tests/Editor/PrismTopologyContractTests.cs`.
- Build/test control: `Tools/unity/run_editmode_tests.sh`, `specka.md`, `AGENTS.md`,
  and `.codex/**`.

## Next exact actions

1. Regenerate the code graph and validate the DAG/control plane; run static hygiene.
2. Commit this exact checkpoint locally on the active fix branch.
3. Produce and verify `QuestInfiniteScan-<commit>.zip` in the workspace using
   `git archive`; do not push.
4. Rebuild Android/Vulkan from that commit, verify the APK hash and zero mapper/
   shader errors, then deploy that exact artifact.
5. Run one batched physical acceptance: continuous film/no square plates, stable
   stereo presentation, local artifact pressure, close-bake resistance, Stop/Start,
   revisit visibility, and frame cost. Only evidence from that run may close
   Q3-11 through its dependent implemented checkpoints.

## Safety

- Never delete, move, compress, prune, or modify `~/.codex` or any Codex session.
- Keep Unity builds, caches and device captures on Kingston.
- Do not commit or archive generated builds, device IDs, credentials, addresses, or
  captured room imagery.
