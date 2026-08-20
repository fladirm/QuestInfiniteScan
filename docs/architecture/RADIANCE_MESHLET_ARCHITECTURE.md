# QuestInfiniteScan radiance-meshlet architecture

Status: production target on `feat/quest-radiance-meshlets`  
Supersedes: scalar TSDF, proposed DTSDF replacement, and hybrid DiffSoup production
paths  
Hardware: Meta Quest 3 and Quest 3S, Unity 6000.5, Android ARM64, Vulkan

## 1. System boundary

QuestInfiniteScan treats the headset as a calibrated moving stereo RGB-D rig with a
tracked metric trajectory. The mapper does not solve general camera pose recovery
and does not train a radiance field. It turns synchronized metric observations into
layered surfaces, then publishes ordinary GPU meshlets with incremental appearance.

```text
Meta XR capture/tracking
  -> StereoRigFrame
  -> DepthConsensus + EdgeConfidence
  -> UncertainTileQueue -> NarrowMVS (budgeted)
  -> ObservationPatchBuffer
  -> SurfaceAssociation raster
  -> LayeredSurfaceFusion
  -> SurfaceRegularization
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

Ray, undistortion, and epipolar direction LUTs are calibration-dependent and may be
cached. Depth-to-RGB coordinates are evaluated with each candidate metric depth.

## 3. GPU pass graph

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
5. `BuildObservationPatches`: creates discontinuity-safe transient points/patches
   with position, normal, covariance/confidence, color sample coordinates, and
   quality envelope.
6. Association raster: draws the current published meshlet generation from the
   observation pose to integer surface-ID/generation, predicted depth, normal, and
   visibility targets.
7. `ClassifyAssociation`: validates residual, normal, generation, silhouette,
   visibility, dynamics, and quality; routes each observation to an existing
   surface, a new layer candidate, or rejection.
8. `FuseSurfaceInformation`: robustly accumulates point-to-plane information for
   accepted active surfaces and updates uncertainty/quality without degrading
   stable high-quality estimates.
9. `AllocateSurfaceLayers`: inserts unmatched candidates into the chunk-local
   spatial index with generation-safe bounded allocation.
10. `RegularizeDirtySurfaces`: applies local compatible plane/curvature support
    while respecting layer, discontinuity, and chunk-boundary locks.
11. `BuildDirtyMeshlets`: updates topology into the inactive generation, validates
    capacities/winding/IDs, emits LOD error metrics, then atomically publishes.
12. `UpdateAppearance`: exposure-normalizes accepted RGB observations, updates the
    best multiresolution texels and compact directional state, and writes confidence.
13. `CullAndSelectLod`: per XR view, performs frustum/Hi-Z culling and independently
    selects meshlet and appearance LOD from projected error, confidence, residency,
    and bandwidth.
14. `CompactDraws`: writes indirect draw lists. The renderer issues bounded
    indexed-indirect draws with ordinary depth testing and PBR shading.

No pass requires `GraphicsBuffer.GetData`, a CPU triangle list, or a per-frame
`Mesh`. Debug sampling uses separately throttled async readback and is disabled in
production profiles.

## 4. Canonical chunk data

The spatial index accelerates association/allocation; it does not quantize geometry.
A cell may reference several mutually incompatible surface layers.

Initial layout contract for `M01` (all strides are 16-byte aligned and must receive
C#/HLSL parity tests):

| Buffer | Draft stride | Purpose | Initial bounded capacity |
|---|---:|---|---:|
| `SurfaceState` | 64 B | position/confidence, packed normal/flags/generation/count, appearance handle, quality envelope | 1,048,576 = 64 MiB |
| `SurfaceInformation` | 48 B | symmetric 3x3 normal matrix, RHS, robust weight/residual state | 1,048,576 = 48 MiB |
| `CellHeader` | 16 B | key, first reference, count, generation | 1,048,576 = 16 MiB |
| `CellSurfaceRef` | 4 B | generation-safe surface reference | 8,388,608 = 32 MiB |
| `MeshletHeader` | 32 B | ranges, bounds/cone, generation, LOD error | 262,144 = 8 MiB |
| `MeshletIndex` | 4 B | packed surface/index references | 16,777,216 = 64 MiB |
| queues/indirect args | segmented | dirty/new/visible work and dispatch/draw args | each <=32 MiB |

Capacities are profile-controlled and split into segments before any buffer reaches
128 MiB. The planner sums textures, buffers, double-buffered topology, frame leases,
and transient peaks. Balanced targets <=1.2 GiB and allocation fails closed before
2 GiB, falling back by reducing active radius/uncertain-tile budget—not by erasing
captured detail.

Stable persisted pages quantize positions chunk-locally only after proving that
quantization error stays below the geometry quality budget. Dirty active information
remains full precision. Every handle combines index and generation.

## 5. Monotonic geometry quality

Each observation and surface records a quality envelope, not a single weight:

- native/stereo/temporal confidence;
- range and incidence;
- projected metric sampling footprint;
- pose and calibration uncertainty;
- edge/mixed-pixel risk;
- baseline/view diversity;
- residual history and observation count.

For an unstable surface, robust information fusion may move the estimate within its
uncertainty. Once stable, a lower-information observation may confirm visibility or
increase a disagreement counter, but it cannot move the surface outside the tighter
existing bound or reduce stored sampling detail. A move/replacement requires both
compatible multi-view residuals and measured information gain. Persistent conflict
creates a separate layer, dynamic hypothesis, or unresolved status.

This directly covers near-then-far, grazing revisits, opposite sides, and observations
through a wall. The UI visualizes unresolved evidence instead of silently smoothing
it.

## 6. Adaptive topology and rendering LOD

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

## 7. Chunk lifecycle and infinite world

Chunk bounds are numerical/locality units, never room semantics. Current and target
arenas overlap with Schmitt hysteresis in X/Y/Z. The source published generation
remains renderable while the target accepts observations.

Dirty surface/topology/appearance pages freeze into immutable generations and stage
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

## 8. Appearance and PBR

Base color is a robust exposure-normalized estimate from visibility-compatible
observations. A small bounded incremental least-squares state captures compact
view-dependent residual (initially diffuse plus first-order directional terms or a
measured lobe model selected during `A02`). This is refinement, not training.

Geometric normals remain primary. A detail normal may encode supported repeatable
photometric residual and always carries confidence. Roughness requires angular
evidence; metallic remains zero when evidence is ambiguous. Export can use constant
roughness as an explicitly declared fallback but never invent material maps.

## 9. UI architecture

`RoomScanner` becomes a thin `ScanWorkflowCoordinator`. Subsystems publish immutable
snapshots; presenters do not query GPU resources directly.

- Scan: explicit Idle, Permission/Warmup, Scanning, Paused, Finalizing, Ready, Fault;
  tracking, stereo pairing/rates, mapper lag, stable percentage, unresolved edges,
  active chunk, memory, and thermal health.
- Worlds: chunks/residency/durability, graph/relocation, revisits, and storage.
- Quality: Fast/Balanced/Detail budgets plus geometry, appearance, material, and
  detail backlog/confidence.
- Export: selected chunk/world/sharded GLB state and validation.
- Settings: capture and profile controls with safe defaults.
- Diagnostics: opt-in GPU counters, timings, rejection reasons, and visualization
  modes Natural/Coverage/Geometry/Uncertainty/Wireframe applied globally.

## 10. Migration sequence

1. Keep the archived branch as immutable recovery.
2. Introduce capture and mapping interfaces beside the fallback mapper.
3. Complete the first GPU vertical slice on a fixed fixture: rig frame -> consensus
   -> surface -> meshlet -> indirect render -> one persisted page.
4. Extend layers, topology, appearance, chunks, and GLB while A/B testing against
   retained captured inputs.
5. Pass clean Unity/Android and physical Quest gates.
6. Remove scalar TSDF/DTSDF/Surface Nets, triplanar, GS, HeavyCompute/DiffSoup/server,
   legacy UI, and their production resources from this branch.

The shipped project ends with one mapper and one operator workflow.
