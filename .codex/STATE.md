# Execution state

Updated: 2026-08-20 (Europe/Prague)

## Repository

- Product: QuestInfiniteScan, non-commercial fork of QuestRoomScan.
- Writable fork: `git@github.com:fladirm/QuestInfiniteScan.git` (`origin`).
- Audited upstream: `arghyasur1991/QuestRoomScan` at
  `2fdaaae71f60b21b7853e67db943fc42f75d0c2f` (`upstream`, push disabled).
- Working branch: `feat/infinite-submaps-diffsoup`.
- Source stays in this checkout. Unity, Android, Python/CUDA, server state, caches,
  captures, and builds stay in the Kingston ext4 development mount.

## Current position

- DAG: 22 nodes; 16 `done`; `F01` active.
- Completed since the last checkpoint: `G02` pose-graph world/sharded/monolithic
  GLB and `G03` official plus independent interoperability/compression negotiation.
- `E02`, `F01`, and `F03` implementation is present and tested, but their DAG nodes
  remain open because physical `B06`/`D03`/`F02` dependencies are not accepted.
- User priority: push this breadth-complete feature checkpoint before tuning runtime
  bugs. Do not falsely close the physical gates to manufacture 100% DAG status.

## Implemented checkpoint

- Existing Unity/Vulkan depth capture, scalar TSDF, Surface Nets, keyframes, atlas,
  anchors, optional legacy modules, and single-room path remain available.
- Versioned world/chunk/edge/artifact records, atomic store/backup/transactions,
  migration-safe legacy reads, and one reusable active TSDF are implemented.
- Rollover has overlap, Schmitt hysteresis, revisit discovery, background snapshot
  publication, bounded coarse/DiffSoup presentation caches, per-chunk keyframes and
  refinement, and explicit world/chunk transforms.
- The scalar fusion path includes distance/incidence/confidence/quality/orientation/
  visibility arbitration, bounded negative support, old-snapshot compatibility,
  GPU-fence retirement, and CPU/real-GPU parity tests.
- Pose graph edges carry confidence, covariance, and provenance. Bounded background
  point-to-plane ICP and robust SE(3) optimization publish only chunk transforms.
- Protocol-v2 client/server jobs are durable and idempotent. Streaming inputs,
  hostile archives, restart recovery, offline retry, stale revisions, cancellation,
  fake backend, pinned DiffSoup CUDA subprocess, exact warm start, artifact validation,
  atomic promotion, and bounded Unity URP rendering are implemented.
- GLB exports positions/normals/tangents/UV0/indices, embedded base color and normal,
  honest metallic=0/constant roughness PBR, exact-once pose-graph nodes, sharded
  `building.json + chunks/*.glb`, and optional bounded `world.glb`.
- QuestInfiniteScan setup/UI exposes large-world defaults, chunk lifecycle/residency,
  graph, queue/network, artifacts/storage, exports, and the difference between
  display-only Freeze Tint and integration Freeze/Unfreeze.
- README, operator/developer/recovery/known-issue/release/protocol/license/notice
  documents now describe this fork and its actual status.

## Verification evidence

- Full verifier report:
  `/mnt/kingston-unity/Builds/Verification/20260820T132533Z/verification-report.json`.
  All nine enabled steps passed: control, diff hygiene, profile parser, contracts,
  fake server, Unity, GLB, real CUDA, and Android/Vulkan.
- Server: 9 strict dependency-free contract tests; 30 fake/backend/storage/API tests
  passed with one intentionally separate CUDA test skipped; real CUDA test passed 1/1
  including revision warm start and incompatible fresh fallback.
- Unity 6000.5.9f1: 93 total, 88 passed, 0 failed, 5 intentional live/hardware skips.
- GLB: chunk, second chunk, and two-node world report 0 Khronos errors and 0 warnings;
  independent glTF Transform NodeIO verifies PBR, geometry, graph, and transforms.
- Android/ARM64/IL2CPP/Vulkan build passed. Current generated APK before the final
  clean post-commit rebuild is external and must not be committed.
- The pinned Python 3.14.4 / Torch 2.13.0+cu130 / DiffSoup worker environment was
  detected as already current rather than reinstalled; the RTX 4070 probe passed.
- `git diff --check`, shellcheck for supplied shell tools, JSON parsing, Markdown
  local-link audit, secret/LAN/device scan, and control-plane validation pass.

## Known physical blocker

The retained device capture proves the current lifecycle failure rather than a GPU
capacity limit:

- chunk 0 remained active with volume/live mesh/keyframes while chunks 1–3 remained
  `Finalizing`; the last transaction was incomplete;
- repeated revisit selection failed because no durable or retained volume snapshot
  was available;
- scanning could keep integrating the active volume, while the bounded old
  presentation disappeared and Stop could not drain the inconsistent lifecycle.

Root model: transition commits a source as `Finalizing` and permits further target
selection while background publication is unfinished; only one previous CPU volume
is retained and evicted nearby meshes are not yet dynamically rehydrated. Fix this as
one coherent state-machine/cache-load change after the checkpoint push. Do not hide
it with an unbounded cache or a larger global TSDF.

`B06` also needs fresh oblique/thin-structure and >6-transition profiling. `D03`
needs physical stereo/depth/culling/relocation/disposal and live CUDA swap. `F02`
needs the complete offline/reload/revisit/anchor/memory device matrix.

## Directional replacement decision

After lifecycle stabilization, the main scalar TSDF + scalar Surface Nets path is to
be replaced by sparse Directional TSDF and direction-aware extraction. This is a
general thin-structure solution for partitions, square/round columns, pipes, rails,
panels, and edges—not a wall special case. The project is explicitly non-commercial,
so the InfiniTAM-derived CUDA/C++ reference may be mechanically ported while retaining
its full copyright, source, attribution, license, and non-commercial conditions.

First add trustworthy depth-edge/normal confidence and bounded fusion-conflict
telemetry; then use a temporary backend boundary and fixed capture corpus for A/B
migration. Once persistence, memory, topology, frame-time, and Quest parity pass,
DTSDF replaces the production scalar core. Do not mistake orientation separation for
sub-voxel resolution; local fine bricks remain a later measured step.

## Next actions

1. Inspect the exact staged file set; exclude caches, captures, addresses, credentials,
   device IDs, generated projects/builds, and model artifacts.
2. Commit the feature checkpoint, run the full verifier against the clean commit so
   its report names that revision, then push the feature branch to `origin`.
3. Resume with the recorded P0 lifecycle fix and physical acceptance, without
   redoing completed GLB/server/UI work.
4. After scalar lifecycle stability, execute the directional replacement plan in
   `docs/architecture/DIRECTIONAL_GEOMETRY_DECISION.md`.

## Safety

- Never delete, move, compress, prune, or otherwise modify `~/.codex` or Codex
  sessions/history/goals/caches.
- Cleanup is limited to this repository's explicitly regenerable build/cache output.
- Do not commit room imagery, raw captures, generated models, server databases,
  machine addresses, credentials, or device identifiers.
