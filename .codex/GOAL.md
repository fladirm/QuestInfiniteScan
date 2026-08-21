# Goal

The canonical build specification is [`specka.md`](../specka.md). This file states
the pursuit outcome and acceptance boundary; it must not weaken that specification.

Build Cone-PRISM-Q3 (cone-pressure Probabilistic Ray-Integrated Surface Manifold) inside
QuestInfiniteScan as a production-oriented, fully on-device Quest 3/3S scanner for
room-to-building-scale capture. It must continuously refine metrically accurate,
thin-structure-safe probabilistic ContactFilms and surface-light-field appearance
into an ordinary renderable/exportable PBR mesh while keeping GPU residency bounded
as the world grows.

## Product outcome

The production pipeline is:

```text
stereo RGB + stereo depth + timestamped poses/intrinsics
  -> synchronized StereoRigFrame
  -> depth consensus and discontinuity confidence
  -> narrow stereo/temporal refinement for uncertain tiles
  -> finite-footprint ConeEvents: pre-hit free space + contact + unknown behind
  -> predicted film depth/normal/ID/UV/uncertainty raster
  -> first-contact classification and ContactFilm association
  -> range/footprint-aware pressure-information solve + soft-to-hard collapse
  -> continuous Grid16 contact-domain support
  -> persistent local first-hit pressure posterior vs baked close-view resistance
  -> persistent ContactBoundary evidence + film split/merge/retire
  -> GPU tessellation, adaptive meshlets, dynamic LOD
  -> surface-space superresolution + directional appearance/PBR
  -> versioned PRISM chunk pages, streaming world, GLB/PBR derivative
```

The canonical geometry is not a scalar field, surfel cloud, or permanent per-frame
triangle soup. Each chunk owns a graph of one-sided probabilistic `ContactFilm`s;
their `SurfaceChartGeometry` owns stable IDs, tangent frames,
planar/quadratic/micro-detail shape variants,
normal-direction covariance, robust sufficient statistics, sidedness/visibility,
a tangent/quadratic base with sparse displacement microtiles. Persistent
`ContactBoundary` entities own uncertainty-bearing spline `BoundaryCurve` geometry.
Films also own UV domains, appearance pages, adjacency, bounds,
revision, and `worldFromChunk`. Meshlets are a replaceable derived cache.
Depth/RGB pixels create transient finite-footprint ConeEvents. Opposite-facing and
visibility-incompatible measurements remain separate hypotheses.

## Required capabilities

- Use both Quest RGB cameras and both environment-depth views as timestamped GPU
  streams with their exact intrinsics, extrinsics, and poses.
- Model each calibrated pixel as a finite cone/truncated-pyramid footprint. Only its
  segment before the first measured contact is supported free space; everything
  behind that hit remains explicitly unknown and cannot be modified by that event.
- Fail closed on invalid or temporally incompatible frame data and expose pairing
  health in diagnostics.
- Refine reliable native metric depth with stereo/temporal evidence only in tiles
  where edge, disagreement, or topology confidence requires it.
- Incrementally associate observations using the hardware rasterizer, without
  global nearest-neighbor searches or training loops.
- Run the live geometry and appearance pipeline on GPU with indirect dispatch,
  GPU-generated draw lists, GPU culling, and GPU-selected dynamic LOD. There is no
  synchronous geometry/texture readback or CPU `Mesh` rebuild in the scan/render
  critical path.
- Materialize each dirty film cooperatively across a GPU workgroup; never serialize
  all chart vertices/triangles through one lane or trade canonical detail for speed.
- Use an uncertainty-driven soft capture interval around immature ContactFilms and shrink
  it through adaptive GPU quadrature shell layers to a hard opaque surface as
  multi-view information increases.
- Treat persistent RGB/depth silhouettes as first-class 3D boundaries that control
  chart domain, topology, and tessellation.
- Preserve thin walls, panels, poles, pipes, rails, door edges, and separately
  visible opposing surfaces without carving through occluders.
- Treat film coverage as one continuous manifold domain rather than rectangular
  chart tiles; rectangular parameter bounds alone never authorize triangles.
- Persist opposing first-hit pressure and independent eye/angular evidence locally
  per film cell. Compatible contact cancels it; erosion requires multi-view pressure
  to exceed and consume the cell's stored close-view information resistance.
- Adapt topology: large stable planar regions become coarse meshlets; edges,
  curvature, thin objects, and unresolved regions retain finer support.
- Preserve supported sub-chart detail in sparse multiresolution displacement
  microtiles; analytic chart shape is a stable base/LOD, not a ceiling on geometry.
- Retain the highest supported geometric and photometric sampling density and select
  geometry/texture detail dynamically by screen-space error. A lower-information
  later measurement cannot overwrite higher-quality close-range geometry or
  texture.
- Keep live geometry visible and spatially stable throughout chunk rollover,
  persistence, eviction, reload, revisit, pose-graph correction, and application
  restart.
- Ordinary Stop/Start pauses sensor ingress while retaining the canonical GPU graph,
  arenas and last meshlet publication; only explicit resource release may tear them
  down.
- Give every chart a UV domain at birth and incrementally refine multiframe
  footprint-weighted surface superresolution, compact directional appearance, and
  confidence on device. Material estimates must be honest about uncertainty.
- Preserve canonical surface-light-field samples as adaptive directional lobe
  mixtures; PBR and GLB are derived approximations, never destructive replacements.
- Use Meta tracking as a strong prior while allowing bounded residual-driven
  keyframe/chunk micro-registration and pose-graph correction.
- Persist a native `.prism` artifact containing ContactFilms, ContactBoundaries, uncertainty,
  sufficient statistics, observations, and directional appearance so refinement can
  continue after restart or a later revisit.
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
- Persistent boundary localization median error is <=5 mm where calibrated RGB
  silhouettes have adequate multi-view support.
- Supported 20 mm panels/objects retain distinct opposite faces; general supported
  40 mm thin structures, round/square poles, pipes, rails, and door/panel edges
  retain correct topology.
- A later distant/grazing observation cannot degrade an already stable close
  surface by more than 2 mm. A better close revisit may improve it.
- Persist geometric information/covariance and an independent photometric quality
  envelope as film resistance. Pressure follows measured range noise, footprint,
  incidence, pose/calibration uncertainty, consensus, motion, focus, and robust
  innovation—not a constant vote or blindly assumed inverse-square law.
- Observations from an adjoining room cannot change an occluded opposite surface.

### Runtime and scale

- Preview remains interactive on the physical Quest while capture/refinement work is
  driven by dirty/information-positive GPU queues. Profiling selects scheduling and
  residency, never a lower canonical data resolution.
- Production rendering uses GPU-generated indirect draw arguments and contains no
  synchronous geometry readback, CPU meshing, or per-frame CPU geometry traversal.
- No individual storage buffer exceeds the device-reported Vulkan range (128 MiB on
  the measured Quest). Total residency uses segmented pools and a runtime-discovered
  safe app budget with measured compositor/Unity headroom; there is no arbitrary
  product-wide memory cap and memory pressure never deletes canonical detail.
- A physical run with >=20 chunk transitions, revisits, and a vertical/multi-floor
  route keeps all durable chunks reloadable and visible with an O(1) GPU active set.
- No chunk transition causes a main-thread stall >20 ms or a one-frame loss of the
  last published geometry.

### Persistence, appearance, and export

- Interrupted page publication leaves the previous revision intact; reload is
  deterministic and revisions are monotonic.
- Base color is robust to exposure changes. Directional/PBR values always carry
  confidence; uncertain metallic is exactly zero.
- Reprojection error and accepted texel sampling footprint improve or remain stable
  across revisits; lower-quality imagery cannot lower an existing texture mip/detail
  envelope.
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
- A fixed global geometry/texture resolution, destructive decimation of canonical
  detail, or a low-order appearance model as the only retained visual truth.
- CPU meshing, CPU chart association, per-frame `GraphicsBuffer.GetData`, Unity
  `Mesh` reconstruction, or synchronous GPU readback in the live pipeline.
- Pretending roughness/metallic are measured when view/illumination evidence cannot
  support them.
- One globally resident map or one monolithic world artifact as the only storage
  format.

## Definition of done

1. Every node in `.codex/TASK_DAG.json` is `done` with inspectable evidence.
2. The legacy hybrid checkpoint and old DAG remain recoverable from
   `archive/hybrid-diffsoup-checkpoint-20260820`.
3. Captured-corpus tests cover sync rejection, cone/free-space/contact/unknown
   classification, pressure/resistance and near/far ordering, uncertainty collapse,
   film association/update, discontinuities, boundaries,
   thin structures, occlusion, revisit ordering, topology, persistence interruption,
   micro-registration, and GPU/CPU contract parity.
4. Unity EditMode/runtime validation and Android ARM64 Vulkan builds pass without
   mapper or shader errors.
5. Physical Quest 3/3S acceptance meets the geometry, interactive preview, memory,
   transition, revisit, multi-floor, and offline gates above.
6. Native `.prism` fixtures reopen and continue refinement deterministically; GLB
   chunk/world fixtures and physical exports pass official and independent
   interoperability checks.
7. Production scenes, setup wizard, UI, package metadata, README, and runbooks use
   the new on-device mapper; legacy TSDF/DTSDF/GS/DiffSoup/server wiring is absent
   from the shipped product.
