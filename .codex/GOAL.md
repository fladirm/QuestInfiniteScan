# Goal

Build QuestInfiniteScan as a production-oriented, fully on-device Quest 3/3S
scanner for room-to-building-scale capture. It must continuously refine metrically
accurate, thin-structure-safe geometry and confidence-bearing appearance into an
ordinary renderable/exportable PBR mesh while keeping GPU residency bounded as the
world grows.

## Product outcome

The production pipeline is:

```text
stereo RGB + stereo depth + timestamped poses/intrinsics
  -> synchronized StereoRigFrame
  -> depth consensus and discontinuity confidence
  -> narrow stereo/temporal refinement for uncertain tiles
  -> ephemeral observation patches
  -> ID/depth/normal association raster
  -> layered point-to-plane surface fusion
  -> local regularization and adaptive meshlets
  -> on-device directional appearance/PBR refinement
  -> versioned chunk pages, streaming world, GLB/PBR export
```

The canonical geometry is not a scalar field and not a permanent per-frame triangle
soup. Each chunk owns layered surface records, stable local identifiers,
point-to-plane information state, confidence, adaptive meshlet topology, appearance
pages, bounds, revision, and `worldFromChunk`. Depth-derived patches are transient
measurements. Opposite-facing and visibility-incompatible measurements remain
separate surfaces.

## Required capabilities

- Use both Quest RGB cameras and both environment-depth views as timestamped GPU
  streams with their exact intrinsics, extrinsics, and poses.
- Fail closed on invalid or temporally incompatible frame data and expose pairing
  health in diagnostics.
- Refine reliable native metric depth with stereo/temporal evidence only in tiles
  where edge, disagreement, or topology confidence requires it.
- Incrementally associate and fuse observations without global nearest-neighbor
  searches or training loops.
- Run the live geometry and appearance pipeline on GPU with indirect dispatch,
  GPU-generated draw lists, GPU culling, and GPU-selected dynamic LOD. There is no
  synchronous geometry/texture readback or CPU `Mesh` rebuild in the scan/render
  critical path.
- Preserve thin walls, panels, poles, pipes, rails, door edges, and separately
  visible opposing surfaces without carving through occluders.
- Adapt topology: large stable planar regions become coarse meshlets; edges,
  curvature, thin objects, and unresolved regions retain finer support.
- Retain the highest supported geometric and photometric sampling density and select
  geometry/texture detail dynamically by screen-space error. A lower-information
  later measurement cannot overwrite higher-quality close-range geometry or
  texture.
- Keep live geometry visible and spatially stable throughout chunk rollover,
  persistence, eviction, reload, revisit, pose-graph correction, and application
  restart.
- Refine exposure-normalized base color, geometric normals, compact directional
  residuals, and confidence on device. Material estimates must be honest about
  uncertainty.
- Export chunk and world GLB/PBR assets with correct transforms and a sharded mode
  for unbounded worlds.
- Provide a task-oriented VR UI for Scan, Worlds, Quality, Export, and Settings;
  diagnostics remain an explicit developer view.
- Scan, refine, render, revisit, persist, and export with no LAN or notebook.

## Quality contract

Claims apply only where sensor range, lighting, texture, view diversity, and
tracking quality satisfy recorded validity thresholds. The app reports unresolved
or unsupported regions rather than fabricating certainty.

### Geometry

- Median surface error <=8 mm at 0.5–2.0 m on the accepted physical corpus; p95
  <=20 mm.
- Deliberately covered planar regions reach >=95% completeness.
- Edge-bridge rate across strong depth discontinuities is <1% on the fixed corpus.
- Supported 20 mm panels/objects retain distinct opposite faces; general supported
  40 mm thin structures, round/square poles, pipes, rails, and door/panel edges
  retain correct topology.
- A later distant/grazing observation cannot degrade an already stable close
  surface by more than 2 mm. A better close revisit may improve it.
- Observations from an adjoining room cannot change an occluded opposite surface.

### Runtime and scale

- Quest display remains at 72 Hz; mapping sustains >=10 Hz under the balanced
  profile.
- Amortized mapper GPU time targets p95 <=4 ms, without full-volume or whole-chunk
  synchronous readback. Production rendering uses GPU-generated indirect draw
  arguments and contains no per-frame CPU geometry traversal.
- No individual storage buffer exceeds 128 MiB. Active mapper memory targets
  <=1.2 GiB and fails closed before 2 GiB.
- A physical run with >=20 chunk transitions, revisits, and a vertical/multi-floor
  route keeps all durable chunks reloadable and visible with an O(1) GPU active set.
- No chunk transition causes a main-thread stall >20 ms or a one-frame loss of the
  last published geometry.

### Persistence, appearance, and export

- Interrupted page publication leaves the previous revision intact; reload is
  deterministic and revisions are monotonic.
- Base color is robust to exposure changes. Directional/PBR values always carry
  confidence; uncertain metallic is exactly zero.
- Live geometry and appearance have independently selected multiresolution LOD;
  close views use the best captured detail while distant views consume bounded
  bandwidth and memory.
- Chunk and world outputs pass Khronos glTF Validator with zero errors and import
  correctly in an independent consumer with correct transforms/material semantics.
- All core workflows work with networking disabled.

## Reused foundations

- Meta XR/OpenXR/Vulkan Android shell, permissions, tracking, anchors, and build
  tooling from QuestRoomScan.
- Versioned world manifest, atomic `WorldStore`, pose graph, transforms, and GLB
  writer foundations from the preserved hybrid checkpoint.
- Resource retirement/fence utilities, deterministic test tooling, and physical
  Quest deployment runbooks where representation-independent.

## Production non-goals

- Scalar TSDF, DTSDF, Surface Nets, Gaussian splatting, DiffSoup, CUDA, PyTorch,
  Python services, or notebook processing in the production scan path.
- COLMAP/SfM/general SLAM pose recovery; Quest tracking supplies calibrated poses,
  with the existing pose graph correcting chunk transforms.
- Full-frame neural stereo, unconstrained RGB hallucination, or global optimization
  in the real-time loop.
- CPU meshing, CPU surface association, per-frame `GraphicsBuffer.GetData`, Unity
  `Mesh` reconstruction, or synchronous GPU readback in the live pipeline.
- Pretending roughness/metallic are measured when view/illumination evidence cannot
  support them.
- One globally resident map or one monolithic world artifact as the only storage
  format.

## Definition of done

1. Every node in `.codex/TASK_DAG.json` is `done` with inspectable evidence.
2. The legacy hybrid checkpoint and old DAG remain recoverable from
   `archive/hybrid-diffsoup-checkpoint-20260820`.
3. Captured-corpus tests cover sync rejection, discontinuities, layered fusion,
   thin structures, occlusion, revisit ordering, topology, persistence interruption,
   and GPU/CPU contract parity.
4. Unity EditMode/runtime validation and Android ARM64 Vulkan builds pass without
   mapper or shader errors.
5. Physical Quest 3/3S acceptance meets the geometry, performance, memory,
   transition, revisit, multi-floor, offline, and thermal gates above.
6. GLB chunk/world fixtures and physical exports pass official and independent
   interoperability checks.
7. Production scenes, setup wizard, UI, package metadata, README, and runbooks use
   the new on-device mapper; legacy TSDF/DTSDF/GS/DiffSoup/server wiring is absent
   from the shipped product.
