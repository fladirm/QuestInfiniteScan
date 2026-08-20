# Execution state

Updated: 2026-08-20 (Europe/Prague)

## Repository and branch safety

- Product: QuestInfiniteScan, fully on-device Quest 3/3S scanner.
- Writable fork: `git@github.com:fladirm/QuestInfiniteScan.git` (`origin`).
- Upstream: `arghyasur1991/QuestRoomScan` (`upstream`, push disabled).
- Active branch: `feat/quest-radiance-meshlets`.
- Preserved hybrid checkpoint: commit `e9f37c1`, pushed as
  `origin/archive/hybrid-diffsoup-checkpoint-20260820`.
- The archive contains the old 22-node DAG, hybrid/DiffSoup implementation, DTSDF
  scaffold, documentation, and all prior test evidence. Do not rewrite it.

## Current DAG position

- `R00` is complete: goal, 28-node DAG, ADRs, guardrails, architecture/pass graph,
  buffer budget, migration map, UI target, and measurable quality gates agree.
- `C01` is the only active node: immutable capture contracts and dual GPU RGB
  ownership.
- Reusable foundations `P00`–`P03` are accepted: branch checkpoint, Quest build
  shell, versioned world/store/pose graph, and GLB/PBR writers.
- No new radiance-meshlet production mapper code has yet been claimed complete.

## Active product architecture

- Capture: immutable synchronized `StereoRigFrame` containing RGB-L/R, depth-L/R,
  timestamps, intrinsics/extrinsics, and per-view poses, all GPU-backed.
- Measurement: GPU depth consensus/edge confidence, followed by bounded narrow
  stereo/temporal refinement only for uncertain tiles.
- Geometry: transient depth patches -> association raster -> layered point-to-plane
  surface pool -> regularization -> adaptive double-buffered meshlets.
- Runtime: GPU-only association/fusion/topology/culling/LOD and indirect dispatch/
  draw. No synchronous readback, CPU meshing, or Unity `Mesh` rebuild in the live
  path.
- Quality: persistent information envelopes prevent worse distant/grazing/blurred
  observations from degrading better close geometry or texture.
- World: two-arena overlap, page-level asynchronous persistence, bounded residency,
  on-demand rehydration, revisits, pose graph, anchors, and multi-floor support.
- Appearance: exposure-normalized multiresolution base color, geometric/detail
  normal, compact directional residual, honest confidence-bearing PBR.
- Output: chunk, sharded world, and bounded monolithic GLB/PBR.
- Production is offline/local: no Python, CUDA, notebook, server, DiffSoup, GS,
  TSDF, or DTSDF dependency.

## Concrete migration map

Keep/adapt:

- `Runtime/World/WorldManifest*`, `WorldStore`, pose graph, anchors, and transform
  conventions.
- `Runtime/Export/ChunkGlbWriter`, `WorldGlb*`, deterministic PNG and validation
  tools; change their source artifact to canonical meshlet/appearance pages.
- Meta XR/OpenXR/Vulkan setup, permissions, manifest/build automation, tracking, and
  `GpuResourceRetirementQueue`.
- UI Toolkit/VR input shell; replace information architecture/controllers.

Replace:

- `PassthroughCameraProvider` single-eye abstraction with dual `StereoRigCapture`.
- `DepthCapture` with synchronized rig-frame and confidence pipeline.
- `VolumeIntegrator` with layered surface allocation/fusion.
- `GPUSurfaceNets`, `MeshExtractor`, and CPU/Unity mesh paths with GPU adaptive
  meshlets plus indirect renderer.
- `SubmapManager` and `PersistedChunkMeshCache` payload/lifecycle with two arenas and
  page residency.
- `KeyframeCollector`/atlas bake with bounded observation reservoir and GPU
  multiresolution appearance.
- The ~2000-line `RoomScanner` god object with a thin workflow coordinator and
  explicit services/snapshots.

Remove after replacement A/B gate:

- Scalar TSDF, DTSDF scaffold, Surface Nets, triplanar mapping.
- `Runtime.GSplat`, `Runtime/HeavyCompute`, DiffSoup renderer/resources/contracts,
  server code and server UI.
- Legacy freeze-tint/server/HQ/GS operator controls.

## Quality and physical truth

- Target median geometry error is <=8 mm and p95 <=20 mm at 0.5–2.0 m under valid
  capture conditions; unsupported regions remain visibly unresolved.
- Required topology corpus includes 20/40 mm panels, thin walls, round/square poles,
  pipes, rails, door edges, opposing faces, oblique planes, and occlusion.
- Required scale corpus includes >=20 transitions, revisits after eviction/restart,
  and a vertical multi-floor route.
- Prior physical failure must be explicitly regression-tested: after roughly four
  rollovers, chunks could remain `Finalizing`, disappear, and fail to rehydrate.
  Capture retained at
  `/mnt/kingston-unity/Builds/DeviceCaptures/2026-08-20-141107-revisit-disappears/`.
- Existing successful full verifier is retained at
  `/mnt/kingston-unity/Builds/Verification/20260820T132533Z/verification-report.json`;
  it proves foundations, not the new mapper.

## Immediate next actions

1. Commit and push the completed `R00` control/architecture checkpoint.
2. Execute `C01`: introduce pure immutable capture contracts and tests, then adapt
   Meta dual-camera ownership without touching the fallback mapper.
3. Implement `M01` layouts/memory planner in parallel dependency order after the
   capture contract, then close the first GPU vertical slice through association,
   fusion, meshlet publication, indirect rendering, and a persisted page.

## Safety

- Never delete, move, compress, prune, or modify `~/.codex` or Codex sessions.
- Source stays in this checkout; large builds/caches/captures stay on Kingston.
- Do not commit captures, generated models, APKs, caches, addresses, credentials,
  or device identifiers.
