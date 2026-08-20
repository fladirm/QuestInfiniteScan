# Cone-PRISM-Q3 — finite-cone probabilistic ContactFilm reconstruction

Canonical algorithm and acceptance requirements live in
[`specka.md`](../../specka.md). This document is supporting architecture rationale;
it cannot weaken or replace the canonical specification.

Status: production target on `feat/quest-radiance-meshlets`
Supersedes: scalar TSDF, proposed DTSDF replacement, fixed surfel/triangle-soup
canonical maps, and hybrid DiffSoup production paths
Hardware: Meta Quest 3 and Quest 3S, Unity 6000.5, Android ARM64, Vulkan

## 1. System boundary

QuestInfiniteScan treats the headset as a calibrated moving stereo RGB-D cone-field rig with a
tracked metric trajectory. The mapper does not solve general camera pose recovery
and does not train a radiance field. Cone-PRISM turns synchronized finite-footprint
first-contact constraints into a graph of probabilistic one-sided ContactFilms.
Ordinary GPU meshlets and GLB are
continuously regenerated views of that richer canonical state.

```text
Meta XR capture/tracking
  -> StereoRigFrame
  -> DepthConsensus + EdgeConfidence
  -> UncertainTileQueue -> NarrowMVS (budgeted)
  -> ConeEventBuffer (pre-hit free + contact + unknown behind + footprint)
  -> PredictSurface raster (film/UV/depth/normal/sigma)
  -> ContactCompare + ContactClassify
  -> ContactFilmAssociate + pressure/information update
  -> soft-shell focusing + BoundaryEvidence
  -> film split/merge + hierarchical displacement
  -> MeshletTopology inactive generation
  -> atomic publish
  -> GPU cull + geometry LOD + appearance LOD + indirect draw
  -> async immutable page persistence / GLB export
```

All frame-critical geometry and appearance data stays on GPU. C# owns workflow,
small manifests, resource lifetimes, budgets, and asynchronous persistence
transactions. It never reads live counts or reconstructs a Unity `Mesh` per frame.

## 2. Coordinate and time contract

Named transforms follow `destinationFromSource`:

- `worldFromTracking`: current anchor/relocation transform.
- `trackingFromCameraEye`: pose sampled for the image timestamp.
- `worldFromChunk`: pose-graph result for a chunk.
- `chunkFromWorld = inverse(worldFromChunk)`.
- `cameraEyeFromWorld = inverse(worldFromTracking * trackingFromCameraEye)`.

`StereoRigFrame` is immutable after publication and contains:

- RGB left/right `Texture` handles, dimensions, format, and timestamps;
- depth left/right `Texture` handles, dimensions, format, timestamps, confidence
  metadata, and valid metric range;
- per-stream intrinsics plus distortion/calibration version;
- rigid eye/depth extrinsics and timestamp-matched tracking poses;
- frame sequence, ownership token, tracking state, and rejection diagnostics.

The synchronizer accepts a frame only when all required handles and calibrations are
valid, timestamp spread is within the active profile limit, poses bracket/sample the
correct timestamp, and the calibration version is coherent. It drops data rather
than pairing a stale eye. A ring of immutable frame leases prevents texture reuse
until all consuming GPU fences complete.

Center-ray, ray-differential/cone-footprint, undistortion, and epipolar LUTs are
calibration-dependent and may be cached. Depth-to-RGB coordinates and projected
elliptical footprints are evaluated with candidate metric depth and surface frame.

## 3. Canonical ContactFilm model

Each one-sided `ContactFilm` owns local `SurfaceChartGeometry`, an orthonormal frame
`(p,U,V,N)`, and a bounded 2D UV domain. Its geometry is hierarchical:

```text
x(u,v) = p + uU + vV + h_base(u,v)N + h_micro(u,v)N

h_base = a + bu + cv + 0.5(du^2 + 2euv + fv^2)
h_micro = sparse multiresolution displacement tiles
```

The quadratic term is not a resolution limit. It supplies a stable, compact capture
surface and coarse LOD. Sparse GPU microtiles retain supported relief, curved
profiles, and high-frequency detail at observation-driven resolution. Charts may
remain tiny where a single local parameterization is invalid.

Canonical state also contains normal covariance, robust ConeEvent sufficient
statistics, sidedness, first-contact/free-space/unknown visibility state, adjacency,
persistent ContactBoundaries with spline BoundaryCurve geometry,
UV/virtual-texture page tables, surface-light-field lobes, material estimates with
confidence, and monotonic revision/generation IDs.

An immature film represents a probability density along its normal. GPU procedural
draw/compute emits an adaptive 3/5/7/9-sample quadrature shell over `mu +/- k*sigma`
for association and cone-bundle photometric focusing. Layer count follows projected
uncertainty and information gain. As covariance shrinks, weights and offsets
continuously collapse to the single opaque canonical surface. No volumetric grid or
duplicated persistent shell is introduced.

## 4. GPU pass graph

Passes communicate through append/consume buffers, prefix-sum compaction, and
GPU-written indirect arguments. CPU does not fetch element counts.

1. `ValidateRigFrame`: emits per-pixel validity and converts native depth to a
   common metric convention.
2. `DepthConsensus`: cross-projects both depth eyes, estimates normals, rejects
   inconsistent/mixed pixels, and writes depth/confidence/discontinuity.
3. `ClassifyUncertainTiles`: reduces confidence/residual/edge/topology signals into
   a bounded priority queue and indirect dispatch arguments.
4. `NarrowMvs`: evaluates 8–16 depth hypotheses around native depth using
   Census/gradient cost in the other eye and 2–4 pose-valid temporal frames;
   ambiguous consistency remains invalid.
5. `BuildConeEvents`: creates discontinuity-safe transient pre-hit free-space and
   first-contact constraints, explicit unknown-behind state, normal/covariance,
   finite elliptical geometry/color footprints, and quality.
6. `PredictSurface`: rasterizes the published ContactFilm meshlet cache from both
   observation poses to integer film/generation, mean depth, normal, UV, sidedness,
   visibility, confidence, and sigma targets. Uncertain films may procedurally emit
   adaptive shell samples into the refinement targets.
7. `ContactCompare` / `ContactClassify`: classify agree, nearer foreground, persistent
   front-space contradiction, uncovered, wrong-sided, edge, dynamic, or invalid.
   Nothing behind the first hit is carved.
8. `ContactFilmAssociate`: validates residual distributions, normal, generation,
   silhouette/boundary, visibility, dynamics, uncertainty, and information gain;
   route to existing film, new hypothesis, or rejection.
9. `ContactFilmUpdate`: linearizes robust range/footprint/incidence-aware contact
   pressure into the film's basis `H/g`, covariance, quality resistance, and
   hierarchical displacement evidence without degrading stable high-quality estimates.
10. `BoundaryEvidence`: fuses multi-view RGB/depth silhouettes into 3D spline
    control points and covariance, and emits film-domain/topology constraints.
11. `ConeBundleFocus`: for high-value dirty film tiles, jointly samples the shell
    along the film normal against current stereo and selected temporal views.
12. `TopologyScheduler`: spawns/splits/merges/retires films from multimodal
    residuals, boundary evidence, curvature, and persistent free-space evidence.
13. `BuildDirtyMeshlets`: tessellates film chart geometry, boundaries, and resident microtiles
    into the inactive topology generation, validates
    capacities/winding/IDs, emits LOD error metrics, then atomically publishes.
14. `UpdateAppearance`: EWA-projects accepted RGB cone footprints into film virtual
    texture pages, preserves best sampling detail, and incrementally fits diffuse
    plus adaptive directional lobe mixtures with confidence.
15. `MicroRegister`: optional bounded GPU reductions estimate small keyframe/chunk
    SE(3) corrections with Meta pose covariance as a strong prior; accepted results
    enter the existing pose graph rather than rewriting raw frame history.
16. `CullAndSelectLod`: per XR view, performs frustum/Hi-Z culling and independently
    selects meshlet and appearance LOD from projected error, confidence, residency,
    and bandwidth.
17. `CompactDraws`: writes indirect draw lists. The renderer issues bounded
    indexed-indirect draws with ordinary depth testing and PBR shading.

No pass requires `GraphicsBuffer.GetData`, a CPU triangle list, or a per-frame
`Mesh`. Debug sampling uses separately throttled async readback and is disabled in
production profiles.

## 5. Canonical chunk data

The spatial index accelerates association/allocation; it does not quantize geometry.
A cell may reference several mutually incompatible surface layers.

Initial segmented-pool contract for `M01` (all strides are 16-byte aligned and must receive
C#/HLSL parity tests):

| Buffer | Draft stride | Purpose | Initial bounded capacity |
|---|---:|---|---:|
| `ContactFilmState` | 96 B | chart frame/domain, shape variant, sigma, flags/generation, page/boundary/appearance handles, quality | segmented <=128 MiB |
| `ContactFilmInformation` | 112 B | symmetric quadratic-basis information, RHS, robust residual/covariance/resistance state | segmented <=128 MiB |
| `DisplacementMicrotile` | format-dependent | sparse multiresolution normal displacement + covariance/detail quality | virtual segmented pages |
| `BoundaryControl` | 64 B | 3D spline control point/tangent, covariance, evidence, chart/side handles | segmented <=128 MiB |
| `CellHeader` | 16 B | key, first reference, count, generation | 1,048,576 = 16 MiB |
| `CellSurfaceRef` | 4 B | generation-safe surface reference | 8,388,608 = 32 MiB |
| `MeshletHeader` | 32 B | ranges, bounds/cone, generation, LOD error | 262,144 = 8 MiB |
| `MeshletIndex` | 4 B | packed surface/index references | 16,777,216 = 64 MiB |
| queues/indirect args | segmented | dirty/new/visible work and dispatch/draw args | each <=32 MiB |

All pools segment before the device-reported per-buffer limit. The planner discovers
the actual app/device memory budget, measures compositor/Unity headroom, and sums
textures, buffers, double-buffered topology, frame leases, shell samples, virtual
pages, and transient peaks. Under pressure it changes scheduling and resident LOD,
then evicts durable pages; it never lowers or deletes canonical persisted detail.

Stable persisted pages quantize positions chunk-locally only after proving that
quantization error stays below the geometry quality budget. Dirty active information
remains full precision. Every handle combines index and generation.

## 6. Monotonic geometry quality

Each ConeEvent, film chart basis/microtile, boundary control point, and texel records a
quality envelope, not a single weight:

- native/stereo/temporal confidence;
- range and incidence;
- projected metric sampling footprint;
- pose and calibration uncertainty;
- edge/mixed-pixel risk;
- baseline/view diversity;
- residual history and observation count.

Contact pressure is not a constant vote. Its precision derives from the learned
range-dependent Quest depth residual, finite metric footprint, incidence, pose and
calibration covariance, motion, L/R consensus, and bounded robust innovation. Range
normally lowers pressure through larger noise and footprint, but the implementation
does not blindly hard-code `1/r^2`; physical calibration determines the curve.

For an unstable film, robust information fusion may move the estimate within its
uncertainty. Once stable, a lower-information observation may confirm visibility or
increase a disagreement counter, but it cannot move the surface outside the tighter
existing bound or reduce stored sampling detail. A move/replacement requires both
compatible multi-view residuals and measured information gain. Persistent conflict
creates a separate layer, dynamic hypothesis, or unresolved status.

Persisted `H`/covariance plus the quality envelope are the film's resistance: a
close, frontal, precise contact is strongly compressed and weak far/grazing contacts
cannot later pull it. Appearance retains an independent resistance envelope based
on sharpness, exposure, footprint, and visibility, so metric and photometric
confidence are never conflated.

This directly covers near-then-far, grazing revisits, opposite sides, and observations
through a wall. The UI visualizes unresolved evidence instead of silently smoothing
it.

## 7. Boundaries, topology, and rendering LOD

Persistent RGB/depth first-contact discontinuities triangulate uncertainty-bearing
3D `BoundaryCurve` controls inside canonical `ContactBoundary` entities. They
delimit UV domains, lock occlusion sides, guide film split/merge, and provide sub-depth-pixel edge
localization when multiple calibrated RGB views agree. One frame cannot promote or
erase a boundary.

Meshlets reference stable surface IDs. Local construction locks silhouettes,
discontinuities, incompatible layers, material boundaries, and chunk ownership.
Coplanar well-supported regions may merge; curvature, fine objects, and uncertain
areas split or retain dense support.

Every published meshlet has bounds, normal cone, geometric error, confidence range,
and parent/child or cluster-level LOD relation. GPU selection targets subpixel or
profile-specific projected error. Close views choose the finest captured topology;
distant views choose coarser stable meshlets. LOD changes never mutate canonical
surfaces.

Appearance uses independently addressed multiresolution pages. Its quality metric
tracks the best valid projected texel footprint, focus/sharpness, exposure, incidence,
and view support. A blurred/distant frame cannot overwrite a sharper close texel.
GPU feedback requests missing visible pages; fallback mips prevent holes.

## 8. Chunk lifecycle and infinite world

Chunk bounds are numerical/locality units, never room semantics. Current and target
arenas overlap with Schmitt hysteresis in X/Y/Z. The source published generation
remains renderable while the target accepts observations.

Dirty chart/boundary/statistic/topology/appearance pages freeze into immutable generations and stage
through bounded fenced `AsyncGPUReadback` only after they leave the live mutation
set. `WorldStore` writes payloads, flushes/hashes them, then atomically publishes a
manifest revision. Eviction is illegal before durable publication or another valid
resident copy. Visible chunks missing from GPU enter a priority rehydration queue.

Revisit loads the last complete revision, associates new observations with existing
IDs/layers, and produces a monotonic revision. Pose graph changes only
`worldFromChunk`; renderer, capture association, export, and future observation
conversion consume one immutable transform snapshot.

The archived four-rollover disappearance trace is a mandatory regression fixture:
the new lifecycle must tolerate >=20 transitions, return visits, cancellation,
restart, storage latency/failure, and vertical routes without losing presentation.

## 9. Surface-space superresolution, light field, and PBR

Every chart owns UV at birth. Visibility-compatible RGB footprints are projected
directly into sparse virtual pages with EWA weighting from projected footprint,
incidence, sharpness, motion, pose/calibration uncertainty, and exposure. Multiple
subpixel-shifted views create measured surface-space superresolution; no neural
upscaler invents detail.

Canonical appearance retains an online adaptive mixture of diffuse state and 1–2 or
more compact directional/specular lobes where residual evidence warrants them. Lobe
count is information-driven, not globally fixed. This preserves a measured surface
light field for the PRISM viewer while remaining incrementally solvable on GPU.

Geometric normals remain primary. A detail normal may encode supported repeatable
photometric residual and always carries confidence. Roughness requires angular
evidence; metallic remains zero when evidence is ambiguous. Export can use constant
roughness as an explicitly declared fallback but never invent material maps.

## 10. Native PRISM artifact and GLB derivative

`.prism` is a versioned resumable artifact containing ContactFilm graph and chart frames/domains,
base coefficients, displacement microtiles, covariance/sufficient statistics,
boundary splines, observation reservoir, virtual-texture pages, directional lobes,
material estimates/confidence, chunk transforms, and revision hashes. Opening it a
week later resumes refinement rather than starting from a flattened mesh.

GLB/PBR is generated from a stable meshlet cache and resident/export-requested
virtual pages. It is intentionally a lossy interoperable view; export never replaces
canonical PRISM state.

## 11. UI architecture

`RoomScanner` becomes a thin `ScanWorkflowCoordinator`. Subsystems publish immutable
snapshots; presenters do not query GPU resources directly.

- Scan: explicit Idle, Permission/Warmup, Scanning, Paused, Finalizing, Ready, Fault;
  tracking, stereo pairing/rates, mapper lag, stable percentage, unresolved edges,
  active chunk, GPU queue lag, residency, and capture health.
- Worlds: chunks/residency/durability, graph/relocation, revisits, and storage.
- Quality: Fast/Balanced/Detail budgets plus geometry, appearance, material, and
  detail backlog/confidence.
- Export: selected chunk/world/sharded GLB state and validation.
- Settings: capture and profile controls with safe defaults.
- Diagnostics: opt-in GPU counters, timings, rejection reasons, and visualization
  modes Natural/Coverage/Geometry/Uncertainty/Wireframe applied globally.

## 12. Migration sequence

1. Keep the archived branch as immutable recovery.
2. Introduce capture and mapping interfaces beside the fallback mapper.
3. Complete the first GPU vertical slice on a fixed fixture: rig frame -> finite
   cone consensus -> predicted film -> pressure/information update and sigma collapse -> boundary evidence
   -> tessellated meshlet -> indirect render -> one persisted PRISM page.
4. Extend layers, topology, appearance, chunks, and GLB while A/B testing against
   retained captured inputs.
5. Pass clean Unity/Android and physical Quest gates.
6. Remove scalar TSDF/DTSDF/Surface Nets, triplanar, GS, HeavyCompute/DiffSoup/server,
   legacy UI, and their production resources from this branch.

The shipped project ends with one mapper and one operator workflow.

## 13. Research inputs, not implementation templates

- [QuestRealityCapture](https://github.com/t-34400/QuestRealityCapture) demonstrates
  timestamp-paired stereo RGB, stereo depth descriptors, intrinsics, and frame poses.
- [Mesh Splatting](https://arxiv.org/abs/2601.21400) motivates a controllable soft
  surface capture basin; PRISM uses probabilistic procedural shell quadrature without
  its offline differentiable-training pipeline.
- [ExMesh](https://openaccess.thecvf.com/content/CVPR2026/html/Fan_ExMesh_EXplicit_Mesh_Reconstruction_with_Topology_Adaptation_CVPR_2026_paper.html)
  motivates coupled topology adaptation and UV maintenance; PRISM drives local GPU
  changes from ray/boundary statistics rather than global gradient optimization.
- [Deformable Triangle Splatting](https://arxiv.org/abs/2607.22446) supports richer
  primitive boundaries; PRISM stores shared 3D boundary splines instead of per-view
  learnable triangle opacity boundaries.
- [LiTo](https://arxiv.org/abs/2603.11047) supports preserving surface position,
  viewing direction, and color as a surface light field; PRISM fits online lobe
  statistics rather than a trained latent tokenizer.
- [Triangle Splatting SLAM](https://arxiv.org/abs/2605.31419) supports an online
  explicit map plus connected mesh cache; PRISM does not inherit its differentiable
  pose/training loop because Quest provides tracked calibrated poses.
