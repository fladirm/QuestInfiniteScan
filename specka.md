# PRISM-Q3

## Pure-Quest continuous surface reconstruction — canonical build specification

This file is the canonical product and reconstruction specification for
`feat/quest-radiance-meshlets`. If another planning document conflicts with it,
this file wins. The specification may be improved when measurements justify it, but
must not be simplified by removing reconstruction quality mechanisms.

## Product invariant

Quest 3/3S supplies synchronized, timestamped:

```text
RGB_L(t)          RGB_R(t)
DEPTH_L(t)        DEPTH_R(t)
POSE_L(t)         POSE_R(t)
K_RGB_L/R         K_DEPTH_L/R
fixed transforms between rig sensors within one calibration epoch
```

Walking with the headset continuously produces:

```text
persistent unbounded 3D world
├── true two-sided geometry
├── arbitrary topology
├── realtime mesh
├── revisit-driven geometric refinement
├── measured multi-view texture superresolution
├── directional appearance
├── confidence-bearing PBR
└── GLB export
```

The complete reconstruction path runs on Quest. It requires no server, CUDA, 3DGS
training, TSDF volume, COLMAP, global remesh, or neural MVS.

Hard implementation invariants:

- Live pixel, geometry, topology, appearance, culling, LOD, and drawing work stays
  on GPU and is driven by indirect dispatch/draw arguments.
- There is no synchronous GPU readback, CPU pixel loop, CPU meshing, or Unity `Mesh`
  rebuild in the scan/render critical path. Persistence/export may stage fenced,
  immutable dirty pages asynchronously.
- Consistent evidence fuses. Inconsistent evidence creates a hypothesis, layer,
  boundary, split, or unresolved state. It is never destructively averaged.
- A lower-information later observation cannot degrade a stable closer/sharper
  geometry or texture estimate. Replacement requires measured information gain.
- Dynamic LOD changes only derived resident/render caches. It never deletes or
  decimates canonical measured detail.

## 1. Project basis

Fork QuestRoomScan and retain only useful Quest infrastructure:

```text
Unity / Meta XR and Quest build configuration
permissions and lifecycle
stereo depth and pose acquisition
anchors
VR UI/input shell
versioned persistent storage
Vulkan render plumbing
existing reusable world/pose-graph and GLB primitives
```

Adapt the direct GPU capture pattern demonstrated by QuestRealityCapture for:

```text
RGB_L/R, DEPTH_L/R, timestamps, per-camera poses, intrinsics, extrinsics
```

Do not copy its disk-logging/CPU-color path into the live mapper.

Replace as reconstruction core:

```text
fixed room volume
scalar TSDF / proposed DTSDF
Surface Nets canonical geometry
triplanar canonical appearance
server-side GS / DiffSoup reconstruction
room-centric assumptions
```

## 2. Single canonical geometry: one-sided SurfaceCharts

The world is a graph of one-sided probabilistic `SurfaceChart`s. A chart is not a
triangle. It is a local parametric manifold:

\[
X(u,v)=P+uT_u+vT_v+h(u,v)N
\]

with:

\[
h(u,v)=a+bu+cv+du^2+euv+fv^2+D(u,v).
\]

This gives a compact quadratic base plus measured high-frequency displacement.
Large planes need few parameters, curved surfaces use the analytic terms, and true
detail uses displacement. Complex or non-single-valued regions split into several
charts.

Canonical chart state is conceptually:

```c
struct SurfaceChart {
    uint64 id;
    uint64 chunkId;

    float3 P;
    float3 N;
    float3 Tu;
    float3 Tv;
    float2 extent;

    float q[6];
    Information6x6 information;

    Grid16 displacementBase;
    Grid16 sigma;
    Grid16 support;
    Grid16 coverage;
    MicrotileRefs displacementChildren;

    BoundaryRefs boundaries;
    AppearancePageRefs appearance;

    float supportTotal;
    float contradiction;
    uint sidedness;
    uint generation;
    uint revision;
};
```

`Grid16` is a logical base surface domain, not a maximum detail resolution. A cell
may allocate sparse recursive displacement/appearance microtiles when measured
footprint and residual information support more detail. The analytic base remains
the stable coarse representation and LOD.

## 3. One-sided surfaces

Fusion is legal only from a compatible side. A thin partition contains two
independent canonical surfaces:

```text
ROOM A

camera ->  ========================= Chart A
                       20 mm
           ========================= Chart B  <- camera

ROOM B
```

Spatial proximity is never sufficient reason to merge them. Representation imposes
no minimum wall thickness; observability of the calibrated sensors is the limit.

## 4. Fundamental ray measurement

Each valid depth pixel creates a ray:

\[
R(s)=O+sD.
\]

Depth `z` means supported free space for `0<s<z` and a first-hit surface near
`s=z`. It contains no evidence about `s>z`.

Nothing behind the first supported hit is carved. A predicted surface in supported
free space receives contradiction evidence; one observation never deletes it.

## 5. Immutable precomputed rig

At startup or calibration-epoch change, build immutable `RigCalibration` with:

```text
RayRGBL[]     RayRGBR[]
RayDepthL[]   RayDepthR[]

DepthL <-> RGB_L rigid calibration
DepthR <-> RGB_R rigid calibration
RGB_L  <-> RGB_R rigid calibration
DepthL <-> DepthR rigid calibration
```

Ray/undistortion/epipolar LUTs eliminate inverse-intrinsics work per pixel. Projection
between sensors still uses the actual candidate depth. Per-frame state is only the
timestamp-matched world pose and immutable GPU texture leases.

## 6. Reconstruction tick

```text
Acquire coherent StereoRigFrame
  -> normalize independent depths
  -> L/R depth consensus
  -> robust normals and boundary confidence
  -> render current world into both depth views
  -> classify measured versus predicted rays
  -> update compatible charts
  -> spawn independent hypotheses
  -> update persistent boundaries
  -> schedule unresolved chart regions
  -> surface-conditioned stereo/temporal focusing
  -> displacement and appearance update
  -> rebuild dirty meshlets
```

Only observed/dirty charts are processed. The entire world is never reprocessed.

## 7. Prediction raster: renderer as measurement associator

Before fusion, render the published world from the exact current depth-camera poses.
Per eye, use MRT or equivalent targets:

```text
PredDepth       R32F
PredNormal      RG16_SNORM
PredChartID     R32_UINT
PredUV          RG16F
PredSigma       R16F
```

Additional packed targets may carry generation, sidedness, confidence, and
visibility flags. Hardware rasterization provides visibility, occlusion, front-most
surface, chart association, and UV without a spatial nearest-neighbour search.

## 8. Independent L/R depth consensus

Keep the two depth measurements independent until consistency is tested. For a left
sample:

1. deproject with `RayDepthL`;
2. transform through world into the right depth camera;
3. project and sample `DEPTH_R`;
4. compare in one metric frame.

Agreement:

\[
|z_L-z_{R\rightarrow L}|<3\sigma
\]

increases confidence. Disagreement is never blindly averaged; it becomes
`DEPTH_DISAGREEMENT`, a high-value candidate for thin geometry, occlusion, edge,
sensor artifact, or later surface-conditioned refinement.

## 9. Learned-on-device depth uncertainty

Each measurement carries `sigma`. Start conservatively and learn the actual sensor
residual model from stable predicted surfaces. Maintain robust MAD/EMA statistics in
range bins such as:

```text
0–0.5 m, 0.5–1 m, 1–2 m, 2–3 m, 3–5 m, 5 m+
```

\[
\sigma=1.4826\operatorname{MAD}(d_{measured}-d_{predicted}).
\]

Per-pixel uncertainty increases with L/R disagreement, grazing incidence, depth
gradient, invalid neighbours, pose uncertainty, calibration age, and motion.

## 10. Robust depth normal

Fit a local plane over a discontinuity-aware `3x3` or adaptively enlarged
neighbourhood:

```text
reject incompatible depth neighbours
deproject accepted samples
weighted centroid and covariance
smallest eigenvector -> normal
orient toward observing camera
```

Emit position, normal, and normal confidence. A two-neighbour cross product is not
the production normal estimator.

## 11. Exhaustive ray classification

For measured `(dm,Nm,sigma_m)` and predicted `(dp,Np,sigma_p,ChartID)`:

\[
\sigma=\sqrt{\sigma_m^2+\sigma_p^2},\qquad G=3\sigma+2\text{ mm}.
\]

Every ray ends in exactly one state:

```text
MATCH | NEW_FRONT | BEHIND | NEW_LAYER | UNSEEN | BOUNDARY | INVALID
```

- `MATCH`: `|dm-dp|<=G`, `Nm dot Np > cos(25 deg)`, and sidedness/visibility agree.
- `NEW_FRONT`: `dm<dp-G`; spawn a foreground hypothesis.
- `BEHIND`: `dm>dp+G`; add supported free-space contradiction, never immediate
  deletion.
- `NEW_LAYER`: nearby but orientation, sidedness, visibility ordering, or historical
  residual mode is incompatible.
- `UNSEEN`: valid measurement with no prediction; spawn a chart.
- `BOUNDARY`: supported depth/normal/RGB silhouette; prohibit destructive fusion.
- `INVALID`: missing, stale, calibration-invalid, tracking-invalid, or otherwise
  unsupported data.

Residual bimodality can trigger `NEW_LAYER` or split even when normals are similar.

## 12. No destructive averaging

```text
consistent evidence   -> robust information fusion
inconsistent evidence -> hypothesis, layer, boundary, split, dynamic, or unresolved
```

Never average contradictory geometry. This is the mechanism that preserves both
sides of partitions, doors, thin plates, pipes, railings, recesses, folds, gaps, and
closely adjacent surfaces.

## 13. Chart spawn

Process `UNSEEN` and `NEW_FRONT` samples in GPU `8x8` image tiles:

```text
valid samples
 -> depth-connected components
 -> normal-compatible components
 -> deprojection
 -> local PCA frame
 -> robust quadratic fit
 -> new SurfaceChart
```

Fit:

\[
z_i=a+bu_i+cv_i+du_i^2+eu_iv_i+fv_i^2
\]

with two bounded IRLS/Huber passes. Actual support determines chart extent; there is
no global primitive size.

## 14. Information-filter geometry

For `theta=(a,b,c,d,e,f)^T`, residual `e_i=z_i-z_hat_i`, and
`J_i=[1,u,v,u^2,uv,v^2]`, accumulate:

\[
H\leftarrow H+wJ^TJ,\qquad g\leftarrow g+wJ^Te.
\]

Solve:

\[
H\Delta\theta=g
\]

with a deterministic GPU `6x6` Cholesky/regularized solve. Store posterior
information/covariance, not only a mean. Every valid revisit should increase
information and shrink uncertainty; stable high-information geometry is protected
from lower-information observations.

## 15. Hierarchical local displacement

The quadratic chart captures low-frequency shape. Residual after removing the
analytic surface updates logical `Grid16` displacement cells, each holding at least
`D`, information/weight, sigma, support, coverage, and quality envelope.

When actual projected footprint and consistent residuals justify more detail, a
cell allocates child microtiles recursively. When residual is multimodal,
non-single-valued, boundary-separated, or incompatible, split the chart instead of
inflating displacement. Close detail is never overwritten by later coarse samples.

## 16. Explicit soft uncertainty shell

An immature point represents:

\[
X_s=X+sN,\qquad s\sim\mathcal{N}(0,\sigma^2).
\]

Canonical state stores only center and posterior. For association and photometric
focusing, uncertain charts procedurally emit adaptive 3/5/7/9-point quadrature
samples across `mu +/- k*sigma` using GPU indirect work. As information rises,
sigma and shell layers collapse continuously to one hard opaque surface. This gives
a wide capture basin without storing a volume or permanent alpha cloud.

## 17. Persistent first-class BoundaryCurves

The canonical world contains uncertainty-bearing `BoundaryCurve`s with:

```text
stable ID and generation
3D spline/control points and tangents
covariance/confidence/support
left/right chart and sidedness relations
visibility/termination evidence
```

Evidence combines depth/normal discontinuity, persistent RGB edge, surface
visibility termination, coverage termination, and residual partition. Promotion and
retirement require multi-view support; one noisy frame cannot create or erase a
canonical boundary.

## 18. Multi-view subpixel boundary refinement

Project a predicted boundary into new calibrated RGB/depth views. Search a narrow
normal-to-edge image band (initially around `+/-4 px`) for consistent gradient and
silhouette evidence. Triangulate/update its 3D spline controls and covariance from
multiple poses. This lets door frames, corners, plate edges, trims, pipes, and rails
beat raw depth-edge resolution.

## 19. Topology adaptation

Split a chart when residuals are bimodal, a persistent boundary crosses it,
curvature/displacement is not representable, support is non-single-valued, or
normals/sidedness are incompatible. Partition into 2–4 children along dominant
boundary/residual evidence and transfer sufficient statistics without inventing
samples.

Merge neighbouring charts only when sidedness, analytic continuation, normal
(initially <5 degrees), geometry, appearance, and boundary absence all agree. Refit
from sufficient statistics. Large planes simplify; frames, pipes, boundaries, and
fine detail remain appropriately dense.

## 20. RGB is also a geometry sensor

After a chart exists, RGB refinement searches only along its normal:

\[
X'=X+\delta N.
\]

It performs no global 3D correspondence search. Known chart geometry, UV,
calibration, and poses turn stereo/temporal refinement into a small surface-conditioned
one-dimensional inference problem.

## 21. Surface-conditioned stereo focusing

Use posterior/depth sigma to choose a narrow interval. Evaluate an adaptive coarse
set (typically 9) of `delta` candidates in `RGB_L/R` using robust Census and gradient
cost, then a fine set (typically 5) around the best candidate and a parabolic
sub-step fit.

Accept only when best-versus-second-best separation, texture gradient, occlusion,
pose/calibration confidence, and L/R consistency are sufficient. Otherwise retain
depth geometry and uncertainty.

## 22. Temporal ray-bundle focusing

Each chart references its best current and historical calibrated RGB views. For
`X+delta*N`, project directly into current L/R and typically 4–8 information-rich
views. Minimize robust multi-view photometric disagreement plus the depth/posterior
prior. No SfM, feature matching, global stereo volume, or neural MVS is required.

This is the principal sub-depth refinement mechanism and operates only on unresolved
or information-gain-positive chart regions.

## 23. Keyframe selection by information gain

Keyframes are selected by new surface, baseline/incidence improvement, projected
surface resolution, new side, sharpness, exposure, and unresolved-region value—not
time alone. Each chart holds a bounded active view set (typically 8) maximizing
sharpness, footprint, angular/baseline diversity, frontality, and visibility.
Redundant active references may be replaced; durable PRISM observation statistics
must remain sufficient to continue refinement.

## 24. Native surface-space texture

Every chart has UV from creation. RGB is immediately projected to surface
coordinates; no global unwrap/rebake is required during scanning. Per texel/microtile
retain robust color sufficient statistics, weight/information, variance, coverage,
best sampling footprint, and quality envelope.

Weights include sharpness, view angle, elliptical pixel footprint, motion,
occlusion, geometry confidence, exposure consistency, and dynamic rejection.

## 25. Measured texture superresolution

Subpixel-shifted views provide different elliptical footprints on the same chart.
Accumulate them with EWA/footprint weighting into sparse multiresolution virtual
texture pages. Typical density bands may begin at 256/512/1024 texels per metre, but
allocation is driven by the best actually measured footprint rather than a fixed
cap or blind upscaling. Lower-quality images cannot overwrite sharper supported
detail.

## 26. Canonical directional appearance

Canonical color is:

\[
C(u,v,omega)=A(u,v)+V(u,v,omega).
\]

`A` is robust view-stable appearance. `V` begins with SH1 directional RGB
coefficients and may promote to additional compact lobe state only when angular
residual information supports it. The model is incrementally fitted on GPU; it is
not a Gaussian cloud, neural texture, or destructive PBR bake.

## 27. Confidence-bearing PBR derivation

GLB derives:

- `baseColor` from robust `A`;
- `normal` from analytic derivatives plus supported displacement derivatives;
- `roughness` only with adequate angular diversity, otherwise explicit fallback
  (initially 0.75) and low material confidence;
- `metallic` conservatively, exactly zero when evidence is insufficient.

Canonical directional measurements remain intact even when the interoperable PBR
approximation is ambiguous.

## 28. Infinite chunk world

Chunks are storage/local-coordinate units (initially around `4x4x4 m`), never voxel
geometry or room semantics:

```text
Chunk {
    local/world transform
    SurfaceCharts
    BoundaryCurves
    sufficient-statistic/displacement pages
    derived meshlets
    virtual appearance pages
    keyframe references
    revision
}
```

A chart has one owner chunk and may reference neighbours/ghost boundaries. Chunk
dimensions are tuneable locality parameters, not reconstruction resolution.

## 29. Active world and GPU residency

GPU retains visible chunks, current scan neighbourhood, overlap, and dirty
refinement pages. Other durable chunks remain on flash and rehydrate through RAM/GPU
when visible or revisited. Dirty immutable generations stage asynchronously before
eviction. Active reconstruction cost therefore follows visible/dirty area, not total
building size.

No individual buffer exceeds the device-reported Vulkan storage-buffer range;
segmented pools can use the actual safe Quest app memory budget. Pressure changes
residency/scheduling, never canonical detail.

## 30. Revisit is continuation of one posterior

Revisit loads canonical charts, boundaries, information/covariance, displacement,
appearance, and view references; prediction then associates new rays into the same
solve. Information, stereo/temporal focusing, texture SR, and PBR continue from the
last complete revision rather than starting a new reconstruction.

## 31. Bounded pose correction

Quest pose is the primary trajectory. With adequate overlap, reduce point-to-chart
residuals:

\[
e_i=N_i^T(X_i-Q_i)
\]

into a small robust `6x6` SE(3) correction with Meta pose/covariance as a strong
prior. Accepted revisit constraints update chunk transforms in the existing pose
graph; chunk-local canonical geometry is not globally remeshed.

## 32. Derived render mesh

Dirty charts materialize into adaptive GPU meshlets. A standard base chart may map
to at most `16x16` cells / `17x17` base vertices / `512` base triangles before
microtile refinement, but flat areas tessellate more coarsely and boundaries,
curvature, and supported displacement more finely. Vertices along a
`BoundaryCurve` snap to the refined spline. Meshlets use generation IDs and
double-buffered atomic publication.

## 33. Realtime renderer and dynamic LOD

The renderer consumes ordinary opaque indexed meshlets, base/normal/PBR virtual
pages, and directional residuals using Vulkan hardware rasterization and Z-buffer.
GPU frustum/Hi-Z culling, screen-space geometric error, confidence, and page
residency generate indirect draw lists. Geometry and appearance LOD are independent;
close inspection uses maximum measured detail while distant views stay efficient.

## 34. Native canonical persistence

GLB is not reconstruction state. Persist a versioned `.prism` world:

```text
world/
├── manifest + rig calibration epoch
├── chunks/<id>/
│   ├── charts + information/covariance
│   ├── boundaries
│   ├── displacement/microtiles
│   ├── derived meshlets
│   ├── appearance/directional pages
│   ├── texture pages
│   └── keyframe references
└── selected keyframe observations
```

Publication is per immutable dirty generation and preserves the previous complete
revision on interruption. Restart must retain enough posterior state to keep
refining a week later.

## 35. Direct GLB/PBR export

```text
canonical SurfaceCharts
 -> final tessellation and boundary welding
 -> requested texture pages/atlas
 -> confidence-bearing PBR
 -> glTF 2.0 / GLB
```

Export `POSITION`, `NORMAL`, `TANGENT`, `TEXCOORD_0`, indices, base color, normal,
and metallic-roughness texture/factors. Support chunk, selected region, pose-graph
chunk-node world, sharded `world manifest + chunk_*.glb`, and optional bounded
flattened `building.glb`. Do not route through PLY.

## 36. GPU reconstruction passes

Implement the main algorithm as these bounded compute/raster passes:

```text
01 DepthNormalize.compute
02 DepthConsensus.compute
03 DepthNormalBoundary.compute

04 PredictSurface.shader

05 RayClassify.compute
06 ChartSpawn.compute
07 ChartAccumulate.compute
08 ChartSolve.compute

09 BoundaryAccumulate.compute
10 TopologyClassify.compute

11 NarrowStereo.compute
12 TemporalFocus.compute
13 DisplacementUpdate.compute

14 TextureAccumulate.compute
15 DirectionalAppearance.compute

16 MeshletBuild.compute
17 MeshletCullLod.compute / indirect args
```

Prefix sums/compaction, queues, counts, topology, culling, and draw arguments stay on
GPU.

## 37. C# orchestration core

```text
PrismScanner
RigCapture
RigCalibration

ChartPool
BoundaryGraph

ReconstructionScheduler
TopologyScheduler
KeyframePool

ChunkManager
ChunkPersistence

MeshletRenderer
GltfExporter
```

C# coordinates state, resource lifetimes, immutable snapshots, persistence, and
workflow. It does not decide per pixel or traverse live geometry.

## 38. Canonical implementation DAG

| Run | Required outcome |
|---|---|
| **Q3-01** | Preserve fork, verified original Quest build, old implementation branch, and activate PRISM branch/spec |
| **Q3-02** | 2x RGB + 2x depth + timestamped poses + calibration GPU frame contract/dump |
| **Q3-03** | Immutable rig calibration, ray LUT, and depth normalization |
| **Q3-04** | Independent L/R depth consensus, learned sigma, robust normals, boundary confidence |
| **Q3-05** | Dual-eye prediction raster of current chart meshlets |
| **Q3-06** | Exhaustive first-hit ray classification |
| **Q3-07** | Robust GPU SurfaceChart spawn |
| **Q3-08** | 6x6 information-filter chart refinement and uncertainty collapse |
| **Q3-09** | Multihypothesis/one-sided/opposite-side and bimodal surfaces |
| **Q3-10** | Persistent multi-view 3D BoundaryCurves |
| **Q3-11** | Chart split/merge and hierarchical displacement microtiles |
| **Q3-12** | Adaptive GPU meshlet materialization, culling, LOD, indirect renderer |
| **Q3-13** | Infinite chunk paging, native PRISM persistence, restart/revisit |
| **Q3-14** | Surface-conditioned L/R narrow stereo focusing |
| **Q3-15** | Temporal ray-bundle focusing and information-gain keyframes |
| **Q3-16** | Measured hierarchical displacement/normal refinement |
| **Q3-17** | Immediate surface-space RGB/EWA accumulation |
| **Q3-18** | Multi-view measured texture superresolution/virtual pages |
| **Q3-19** | Directional appearance and confidence-bearing PBR derivation |
| **Q3-20** | Bounded revisit pose/chunk correction |
| **Q3-21** | Direct chunk/region/world GLB/PBR export from PRISM meshlets |
| **Q3-22** | Physical whole-building quality, revisit, scale, and interoperability acceptance |

A run is not complete because it compiles. It requires its own synthetic/captured
contract tests, real GPU path, Android build, and applicable physical Quest test.

## 39. Critical acceptance corpus

Thin plates:

```text
5 mm, 10 mm, 20 mm, 50 mm
front -> back -> front revisit -> repeated close/distant views
```

Both surfaces must remain independent; supported measured thickness and edge error
must improve, not average into one surface.

Complex topology:

```text
open/closed door, frame, corner, recess, round/square pipe or pole,
railing, trim, stairs, oblique wall, narrow gap
```

No foreground/background triangles cross supported silhouettes. Persistent
boundaries must outperform raw depth-edge localization when RGB view diversity is
valid.

Revisit:

\[
E_5<E_2<E_1
\]

for observable static geometry and texture. A closer/sharper result cannot be
degraded by a later distant/blurred/grazing pass.

Scale:

```text
room -> floor -> stairwell -> next floor -> whole building -> return visits
```

Active GPU state must follow visible/dirty locality rather than total world size;
all durable chunks must rehydrate and remain spatially stable.

## 40. Definition of the complete system

Complete means all of the following are physically demonstrated:

```text
both RGB streams, both depth views, fixed rig, and exact timestamped poses are used

two-sided thin surfaces survive and refine independently
arbitrary complex topology and persistent boundaries work
scan continues across an unbounded chunk world
old chunks reload and revisit continues the same posterior
information and uncertainty improve with valid repeated observations
RGB L/R and temporal views refine raw depth along existing chart normals
surface-space texture measurably sharpens across revisits
directional appearance is retained and PBR is derived honestly
canonical mesh exists continuously through GPU meshlet materialization
direct GLB/PBR exports validate and import correctly
no core capability requires a server or CPU geometry hot path
```

Complexity remains where it directly increases reconstruction quality:
multihypothesis geometry, posterior information/uncertainty, first-hit semantics,
persistent boundaries, soft-to-hard capture, surface-conditioned multiview focusing,
adaptive topology/displacement, measured superresolution, directional appearance,
and resumable infinite-world state.
