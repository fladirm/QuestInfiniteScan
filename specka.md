# Cone-PRISM-Q3

## Pure-Quest contact-film reconstruction — canonical build specification

This file is the canonical product and reconstruction specification for
the active Cone-PRISM implementation branch. If another planning document conflicts with it,
this file wins. The specification may be improved when measurements justify it, but
must not be simplified by removing reconstruction quality mechanisms.

Reconstruction physics baseline: **`CPQ3-2026-08-21-v5` — frozen for
implementation.** It may be strengthened by measured evidence, but changing the
canonical measurement primitive, first-hit/unknown semantics, ContactFilm world
state, pressure/information solve, or non-degradation invariants requires explicit
user authorization plus a replacement ADR and DAG re-baseline. Implementation
convenience is not sufficient justification.

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

The physical axiom is elastic injection moulding, not carving:

> Before injection, the latent manifold is collapsed to the optical seed (the
> "closed eye"). Calibrated RGB-D cone pressure pushes this conserved elastic sheet
> outwards through the pre-hit interval until the first-hit mould stops it. Adjacent
> pressure points remain connected. Later cone fields squeeze, stretch, pinch,
> split, or merge the same closed orientable manifold, but never delete material to
> manufacture a hole. Pressure is exactly zero behind the first hit.

This is literal solver/topology behaviour, not naming. The initial sheet is a
connected elastic injection domain collapsed at one optical seed. An observed
`ContactFilm` is the part currently pressed against a measured mould. Unobserved
material stays latent, high-sigma, and force-free; it is not guessed as a physical
back wall and is not submitted to first-hit prediction. New pressure advances and
deforms the same sheet. It does not allocate isolated squares and it does not erase
matter along a depth ray.

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

## 2. Single canonical geometry: closed elastic PressureManifolds

The world is a graph of closed orientable probabilistic `PressureManifold`s. A
`ContactFilm` is one local one-sided chart on that connected sheet: the maximal
region whose observation history supports one continuous first-contact surface
hypothesis. It is not an independent tile, point, ray, or open plate. Its geometric
component is a `SurfaceChartGeometry`, not a triangle:

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

Terminology is strict:

```text
ConeEvent            finite-footprint calibrated measurement constraint
PressureManifold     conserved closed elastic connected surface/body
ContactFilm          canonical observed one-sided hypothesis and posterior
SurfaceChartGeometry film's quadratic + displacement parameterization
ContactBoundary      canonical shared/terminating contact discontinuity
BoundaryCurve        ContactBoundary's uncertainty-bearing 3D spline geometry
LatentFrontier       unpressed sheet returning only the outer manifold to its optical seed
Meshlet              derived raster/render/export materialization
```

Canonical film state is conceptually:

```c
struct ContactFilm {
    uint64 id;
    uint64 chunkId;
    uint64 manifoldId;

    float3 P;
    float3 N;
    float3 Tu;
    float3 Tv;
    float2 extent;

    SurfaceChartGeometry geometry; // frame, extent, q[6]
    Information6x6 information;

    Grid16 displacementBase;
    Grid16 sigma;
    Grid16 information;
    Grid16 support;
    Grid16 coverage;
    Grid16 bestPrecision;
    Grid16 bestFootprint;
    Grid16 preHitPressure;
    Grid16 preHitPressureViewMask;
    Grid16 compressionQuality;
    MicrotileRefs displacementChildren;

    ContactBoundaryRefs boundaries;
    AppearancePageRefs appearance;

    float supportTotal;
    float contradiction;
    VisibilityState visibility;
    DepositedObservationState observations;
    uint sidedness;
    uint generation;
    uint revision;
};
```

`compressionQuality` is not an arbitrary strength scalar. It is a compact cache of
the monotonic posterior/quality ordering whose authoritative state is the accumulated
information, covariance and quality envelope. At minimum it preserves best metric
footprint, range, incidence, pose/calibration quality, view diversity, residual
scale and support. It is legal to replace a mean only when the accepted innovation
adds information in the affected normal/detail band. A weak sample may increase
support while leaving the mean, covariance floor and best-detail envelope unchanged.

`Grid16` is a logical base surface domain, not a maximum detail resolution. A cell
may allocate sparse recursive displacement/appearance microtiles when measured
footprint and residual information support more detail. The analytic base remains
the stable coarse representation and LOD.

### 2.1 Conserved continuous manifold domain

`coverage` is not occupancy, a display alpha, rectangular chart extent, or an
independent square primitive. It is the continuous observation/information field on
one already-existing elastic manifold.
At spawn, every finite cone deposits its metric footprint into a conservative
bootstrap support mask. Once a Grid16 displacement page exists, its interpolated
coverage is authoritative. Microtiles refine displacement, normal and uncertainty;
they cannot fragment the film into unrelated display tiles. Persistent
`ContactBoundary` curves refine and terminate this domain below the base-cell scale.

Derived triangulation always preserves connected sheet topology. Coverage modulates
posterior confidence, refinement density, appearance deposition, and latent/observed
render state; it never removes a triangle. `ContactBoundary` edges weld neighbouring
charts or create an elastic crease/occlusion curtain. Only the outer unresolved
frontier of the whole pressure sheet returns to the optical seed. Tile borders are
never frontier and may not create eye-to-surface rays. Split/merge must preserve or
repartition these links, so the user sees one moulded surface rather than 8x8/16x16
plates or disconnected pressure points.

### 2.2 Canonical manifold graph and derived closure

The implementation represents the conserved sheet without storing a dense volume:

```text
PressureManifoldHeader
  id + generation + chunk + opticalSeed + firstFilm + outerFrontier
  FilmMembership[film generation]
  ManifoldLinks[FilmA/generation <-> FilmB/generation + link class]
  LatentFrontierLoops[ordered outer contour + seed return]

ContactFilm A -- smooth continuation --> merge/weld
ContactFilm A -- supported discontinuity --> elastic crease/curtain --> ContactFilm B
outer unresolved contour -- LatentFrontier --> opticalSeed
```

`FilmA/FilmB` adjacency is generation tagged and canonical. Smooth compatible
neighbours may collapse into one larger chart while preserving sufficient
statistics. A supported depth/normal/visibility discontinuity remains two charts
joined by an uncertainty-bearing elastic connector. A connector follows both chart
posteriors as pressure moves them; it is derived render topology with `FilmID=0`, so
it cannot falsely become a measured first-hit association target.

The outer frontier is one contour set per connected manifold/component, never four
edges per chart and never one closure per `8x8` spawn tile. Its return to the optical
seed is canonical latent topology but normally omitted from first-hit prediction and
opaque export until actual contact supports it. This preserves a mathematically
closed injected membrane without displaying or baking imaginary eye rays.

Spawn therefore has two obligations in the same GPU work graph: publish robust
surface charts and publish/refresh ownership adjacency across tile/eye boundaries.
Topology adaptation must transfer those links on split and remap them on merge.
Generation mismatch fails closed to latent/unlinked state; it never connects to a
reused film slot.

The canonical topology records are bounded segmented GPU pools:

```c
struct PressureManifoldHeader {
    uint id, generation, chunkId, flags;
    float3 opticalSeedChunk;
    uint firstFilm, filmCount;
    uint firstLink, linkCount;
    uint firstFrontier, frontierCount;
    uint revision;
};

struct ManifoldLink {
    uint manifoldId, generation;
    uint filmA, generationA;
    uint filmB, generationB;
    uint boundaryId;              // zero for smooth weld candidates
    uint class;                   // Smooth, Crease, OcclusionFold, Latent
    float stiffness, sigma, support;
};

struct LatentFrontierSegment {
    uint manifoldId, generation;
    uint filmId, filmGeneration;
    float2 uv0, uv1;
    uint next, previous;           // ordered loop, not unordered tile edges
    float sigma, support;
};
```

The initial manifold is one closed latent sheet collapsed to `opticalSeedChunk`.
Spawning contact does not create another closed tile. It replaces a supported arc
of the current outer latent frontier with a `ContactFilm` and two ordered frontier
continuations. Neighbouring spawn components from either eye are unioned by
generation-safe footprint overlap before publication. Smooth links are candidates
for information-preserving merge/weld; a supported discontinuity becomes a crease
or occlusion fold. Only a genuinely disconnected scan session/relocalization may
create another `PressureManifoldHeader` and optical seed.

Frontier maintenance is a GPU topology pass, not per-frame CPU graph walking. Split
partitions a film's incident links/frontier segments among its children. Merge
rewrites both endpoints to the new generation and removes duplicate internal links.
A publication is rejected if an active film edge is neither paired with a compatible
link nor represented exactly once by an ordered outer-frontier segment. This is the
watertightness invariant that prevents squares, gaps and eye spikes.

## 3. One-sided contact films

Fusion is legal only from a compatible side. A thin partition contains two
independent canonical surfaces:

```text
ROOM A

camera ->  ========================= Chart A
                       20 mm
           ========================= Chart B  <- camera

ROOM B
```

Spatial proximity is never sufficient reason to merge them. A cone from the back
cannot push the front film because that film is neither the compatible first hit nor
the compatible side in that view. Representation imposes
no minimum wall thickness; observability of the calibrated sensors is the limit.

## 4. Fundamental finite cone measurement

Each pixel is a calibrated finite angular cone/truncated pyramid, not an infinitely
thin line. Its center ray is:

\[
R(s)=O+sD.
\]

The cone carries differential rays or a projection Jacobian describing its footprint.
Depth `z` means the cone applies outward pressure through `0<s<z` and the first-hit
mould constrains the sheet near `s=z`. It contains no force or evidence about
`s>z`.

Nothing behind the first supported hit is modified. A predicted surface intersected
by the supported pre-hit pressure path receives opposing elastic pressure; one
observation never deletes it.
Space behind the hit is explicitly `UNKNOWN`, not `EMPTY`.

The projected cone footprint is generally an ellipse on a film. Its size/orientation
comes from intrinsics, candidate distance, local surface orientation, and calibration
uncertainty. The same footprint controls geometry filtering, contact pressure,
boundary support, and EWA RGB deposition.

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

Ray/undistortion/epipolar LUTs include center rays and differential/angular footprint
terms and eliminate inverse-intrinsics work per pixel. Projection
between sensors still uses the actual candidate depth. Per-frame state is only the
timestamp-matched world pose and immutable GPU texture leases.

The four sensor callbacks are not assumed to arrive in timestamp order. Each eye
and the stereo-depth stream enters a small timestamp-sorted metadata queue; bounded
callback reordering is accepted without copying pixels. The synchronizer chooses
the earliest depth timestamp for which the minimum-cost coherent `RGB_L/R` pair
exists inside the exact RGB-eye and RGB-to-depth gates. Published
`StereoRigFrame`s remain strictly monotonic in metric-depth time. Duplicate,
outside-horizon, calibration-epoch-mismatched, pose-invalid, or genuinely stale
samples fail closed and immediately release their GPU lease. Pairing never widens a
gate merely to keep cadence and never assumes that queue-head arrival order is
sensor time.

### 5.1 Information-driven reconstruction field of view

Passthrough presentation, tracking and relocalization keep the full available FOV.
Canonical geometry ingress does not blindly allocate across every peripheral pixel.
The default high-quality pressure aperture is approximately `30 deg x 30 deg`
centred on each calibrated depth optical axis. It admits native-resolution samples
and all justified microdetail. An overlap/revisit aperture may expand to roughly
`50 deg x 46 deg`, but only for rays that predict an existing film, close a current
outer frontier, contribute a new baseline/side, or carry measured information gain.
Peripheral low-confidence `UNSEEN` rays do not spawn topology.

This aperture is a quality scheduler, never a crop of already accepted world
geometry and never a reduced-resolution capture path. The two eyes retain their
independent cones and overlap; the scheduler prioritizes high-gradient, thin,
boundary, disagreement and low-sigma regions within the aperture through GPU
compaction/indirect arguments. It may postpone low-value work but may not lower the
resolution of an accepted measurement.

## 6. Reconstruction tick

```text
Acquire coherent StereoRigFrame
  -> normalize independent depths
  -> L/R depth consensus
  -> robust normals and boundary confidence
  -> render current world into both depth views
  -> emit finite ConeEvents and classify measured versus predicted contacts
  -> update compatible ContactFilms by robust pressure/information solve
  -> spawn independent hypotheses
  -> update persistent boundaries
  -> update generation-safe manifold adjacency / outer frontier
  -> schedule unresolved film/chart regions
  -> surface-conditioned stereo/temporal focusing
  -> displacement and appearance update
  -> request one coalesced dirty meshlet publication
```

Only observed/dirty ContactFilms are processed. The entire world is never reprocessed.
All mutations in one sensor tick share one publication request; the previous immutable
generation remains visible while the inactive generation is rebuilt. Capture must
never synchronously launch several complete mesh publications in one tick.

The work graph has one bounded in-flight frame per stage. If a refinement or mesh
publication generation is still fenced, the newest coherent capture may replace a
not-yet-consumed older capture metadata lease; canonical dirty work is never
dropped. Prediction uses the last complete immutable meshlet generation. At most one
full derived mesh publication is requested per rendered frame, and publication
coalesces all film/boundary/topology mutations from that frame. No capture callback
waits for mesh generation or XR rendering.

The initial preview publication ceiling is `15 Hz`, independently tuneable from
sensor ingress and display refresh. It limits only replacement of the derived
meshlet cache; every accepted cone still updates canonical state at native input
resolution. Fences/back-pressure may lower publication cadence, never canonical
detail, while the last complete generation remains spatially stable on screen.

Scan lifecycle is serialized and transactional. Concurrent Start requests coalesce
onto one active-chunk preparation task; repeated UI enable cannot multiply click
callbacks, and a toggle received while `Starting` cannot become Stop or initiate a
canonical snapshot. Sensor ingress opens only after active canonical arenas are
resident. A failed activation is surfaced, leaves the scanner stopped/retryable,
and preserves the last durable revision and last complete render publication.

## 7. Prediction raster: renderer as measurement associator

Before fusion, render the published world from the exact current depth-camera poses.
Per eye, use MRT or equivalent targets:

```text
PredDepth       R32F
PredNormal      RG16_SNORM
PredFilmID      R32_UINT
PredUV          RG16F
PredSigma       R16F
```

Additional packed targets may carry film generation, sidedness, confidence, and
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

## 11. Exhaustive ConeEvent classification

For measured `(dm,Nm,sigma_m)` and predicted `(dp,Np,sigma_p,FilmID)`:

\[
\sigma=\sqrt{\sigma_m^2+\sigma_p^2},\qquad G=3\sigma+2\text{ mm}.
\]

Every cone event ends in exactly one state:

```text
MATCH | NEW_FRONT | BEHIND | NEW_LAYER | UNSEEN | BOUNDARY | INVALID
```

- `MATCH`: `|dm-dp|<=G`, `Nm dot Np > cos(25 deg)`, and sidedness/visibility agree.
- `NEW_FRONT`: `dm<dp-G`; spawn a foreground hypothesis.
- `BEHIND`: `dm>dp+G`; only the cone segment before `dm` supplies contradiction to
  a predicted front surface. The region after `dm` remains unknown; never immediate
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

## 13. ContactFilm spawn

Process `UNSEEN` and `NEW_FRONT` samples in GPU `8x8` image tiles:

```text
valid samples
 -> depth-connected components
 -> normal-compatible components
 -> deprojection
 -> local PCA frame
 -> robust quadratic fit
 -> new ContactFilm with SurfaceChartGeometry
```

Fit:

\[
z_i=a+bu_i+cv_i+du_i^2+eu_iv_i+fv_i^2
\]

with two bounded IRLS/Huber passes. Actual support determines chart extent; there is
no global primitive size.

## 14. Pressure-equilibrium information geometry

Compatible cone contacts exert robust normal-direction pressure on the film:

\[
F(S)=\sum_i \pi_i\psi(d_i-\hat d_i(S))N_i,
\]

where `pi_i` is measurement precision and `psi` is a bounded robust influence.

The equilibrium `F(S) approximately 0` is not a second solver. Linearizing this
contact pressure over the six chart parameters yields the information-filter update
below. Different baselines squeeze the posterior; bounded robust influence prevents
one outlier from exerting unlimited force. Stereo/temporal photometric costs later
add pressure along the same normal search dimension.

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

### 14.1 Range-aware pressure and posterior resistance

Pressure is information, not a constant vote and not a hard-coded inverse-square
law. For a cone contact, derive normal-direction covariance from the measured Quest
range-noise model, pose/calibration covariance, motion, L/R agreement, incidence,
and the finite projected cone footprint:

\[
\sigma_{c,i}^2=
\sigma_{depth}^2(r_i)+
\sigma_{pose,n}^2+
\sigma_{cal,n}^2+
\sigma_{footprint,n}^2+
\sigma_{model}^2.
\]

The robust geometric pressure precision is conceptually:

\[
w_i=
q_{valid}
q_{consensus}
q_{incidence}
q_{motion}
\frac{\alpha_{robust}(e_i)}{\max(\sigma_{c,i}^2,\epsilon)},
\]

where `alpha_robust` is the bounded IRLS influence ratio with its continuous
zero-residual limit.

Distance therefore normally weakens pressure because measured depth noise and the
metric pixel footprint grow with range; grazing incidence, motion, uncertain pose,
mixed pixels, and disagreement weaken it further. The exact curve is learned and
validated from Quest residuals by range/incidence bin, not assumed universally as
`1/r^2`. A close cone does not win merely because it is close if it is blurred,
occluded, invalid, or poorly calibrated.

Every film cell persists its accumulated information matrix/covariance and a
quality envelope including best range, incidence, metric footprint, baseline,
sharpness, pose/calibration uncertainty, residual distribution, and support. This
is the film's measured **compression/resistance**. A strongly compressed film has a
large prior precision, so a later weak distant/grazing cone may add support but its
bounded innovation produces negligible displacement. It cannot lower resolution,
inflate accepted uncertainty, or overwrite a better estimate. A genuinely changed
or statistically incompatible contact creates a new hypothesis/dynamic state;
enough stronger compatible information may refine the old posterior through the
same solver.

For compatible static evidence, the production update obeys these monotonic rules
per affected chart/cell frequency band:

```text
H_new              = H_old + positive-semidefinite robust information
sigma_new           <= max(sigma_old, calibrated model floor)
bestFootprint_new   = min(bestFootprint_old, acceptedFootprint)
bestPrecision_new   = max(bestPrecision_old, acceptedPrecision)
detailLevel_new     >= detailLevel_old
```

The mean innovation is preconditioned by `H_old` and then clamped by the posterior
credible interval, the measured cone footprint and an elastic per-tick trust region.
If the innovation is statistically incompatible, it never enters these equations:
it targets a layer, split, boundary, dynamic hypothesis, or unresolved queue. Sensor
noise cannot increase canonical certainty below the learned model/pose/calibration
floor, so repeated near-identical frames do not manufacture impossible precision.

RGB photometric pressure follows the same rule with additional focus/gradient,
exposure, occlusion, and angular-diversity terms. Geometry resistance and texture
quality resistance are stored independently so a sharp color observation cannot
pretend to be precise metric depth, and a precise depth observation cannot erase a
better surface-space texture sample.

### 14.2 Local pre-hit elastic pressure

A `BEHIND` event means the currently predicted manifold lies inside the cone segment
through which the new injection pressure travelled before reaching its mould. It
does not retire a film, subtract coverage, delete triangles, or modify anything
behind the measured first hit. The event maps through prediction UV and deposits a
bounded target displacement plus force/information into the Grid16 elastic domain.
When a second depth peel is the measured hit, its foreground occluder receives the
same local pressure at its own UV.

This opposing pressure is canonical posterior state, not a frame-local counter. Each
base cell persists `preHitPressure` and an eye/angular `preHitPressureViewMask` across
frames, chunk eviction, restart, and revisit. A view contributes at most one bounded
vote to its calibrated direction bin; the production base grid currently provides
ten independent bins. Compatible contact pressure cancels accumulated opposing
pressure before increasing support, and weak contradiction decays rather than
remaining an immortal deletion command. A child microtile inherits geometry/detail
but does not clone one parent cone into many independent contradiction votes.

Elastic motion requires at least two independent eye/angular bins and pressure
exceeding the cell's baked resistance. The implementation baseline is:

```text
resistance = 1 + 0.06 * sqrt(bestPrecision) * sqrt(support)
delta      = bounded(targetDisplacement-currentDisplacement) * response(excess/resistance)
```

Actual displacement consumes the corresponding accumulated pressure work. It is
therefore bounded, local, elastically coupled to neighbours, reversible by later
compatible contact, and cannot repeat forever from one stale event. A close, sharp,
front-facing contact is a strongly compressed film and resists a distant/grazing
cone; sufficiently strong independent first-hit pressure pushes a view-axis
artifact into the mould-supported equilibrium without puncturing the sheet. This is
not TSDF carving: unknown material behind the hit is force-free, an occluded back
surface is untouched, and one weak frame cannot move a strong bake.

Neighbour coupling is topology aware. A cell displacement contributes elastic
continuity terms to linked cells/charts, but a persistent `ContactBoundary` changes
that coupling into a hinge/crease rather than smoothing through the edge. The solve
may stretch a latent connector or move a weakly baked region; it may not reduce
coverage, deactivate a film, or remove indices. When pressure cannot be reconciled
as a single-valued deformation, topology creates a new layer or split instead of
puncturing the sheet.

## 15. Hierarchical local displacement

The quadratic chart captures low-frequency shape. Residual after removing the
analytic surface updates logical `Grid16` displacement cells, each holding at least
`D`, information/weight, sigma, support, coverage, best precision/footprint, the
persistent pre-hit pressure posterior, its independent view mask, compression
quality, and revision.

When actual projected footprint and consistent residuals justify more detail, a
cell allocates child microtiles recursively. When residual is multimodal,
non-single-valued, boundary-separated, or incompatible, split the chart instead of
inflating displacement. Close detail is never overwritten by later coarse samples.

## 16. Explicit soft ContactFilm uncertainty shell

An immature film point represents:

\[
X_s=X+sN,\qquad s\sim\mathcal{N}(0,\sigma^2).
\]

Canonical state stores only center and posterior. For association and photometric
focusing, uncertain films procedurally emit adaptive 3/5/7/9-point quadrature
samples across `mu +/- k*sigma` using GPU indirect work. As information rises,
sigma and shell layers collapse continuously to one hard opaque surface. This gives
a wide capture basin without storing a volume or permanent alpha cloud.

## 17. Persistent first-class ContactBoundaries

The canonical world contains `ContactBoundary` entities represented by
uncertainty-bearing 3D spline `BoundaryCurve`s with:

```text
stable ID and generation
3D spline/control points and tangents
covariance/confidence/support
left/right film and sidedness relations
visibility/termination evidence
```

Evidence combines depth/normal discontinuity, persistent RGB edge, surface
visibility termination, coverage termination, and residual partition. Promotion and
retirement require multi-view support; one noisy frame cannot create or erase a
canonical boundary. A ContactBoundary is a discontinuity of first-contact support,
not merely a strong image gradient.

## 18. Multi-view subpixel boundary refinement

Project a predicted ContactBoundary into new calibrated RGB/depth views. Search a narrow
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

Merge neighbouring films only when sidedness, analytic continuation, normal
(initially <5 degrees), geometry, appearance, and boundary absence all agree. Refit
from sufficient statistics. Large planes simplify; frames, pipes, boundaries, and
fine detail remain appropriately dense.

## 20. RGB cone deposition is also a geometry sensor

After a film exists, RGB cone footprints refine appearance and search geometry only
along its chart normal:

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

## 22. Temporal cone-bundle focusing

Each ContactFilm references its best current and historical calibrated RGB views. For
`X+delta*N`, project directly into current L/R and typically 4–8 information-rich
views. Minimize robust multi-view photometric disagreement plus the depth/posterior
prior. No SfM, feature matching, global stereo volume, or neural MVS is required.

This is the principal sub-depth refinement mechanism and operates only on unresolved
or information-gain-positive film regions.

## 23. Keyframe selection by ContactFilm information gain

Keyframes are selected by new surface, baseline/incidence improvement, projected
surface resolution, new side, sharpness, exposure, and unresolved-region value—not
time alone. Each ContactFilm holds a bounded active view set (typically 8) maximizing
sharpness, footprint, angular/baseline diversity, frontality, and visibility.
Redundant active references may be replaced; durable PRISM observation statistics
must remain sufficient to continue refinement.

## 24. Native surface-space RGB deposition

Every film/chart has UV from creation. Each RGB cone deposits a sample containing
color, view direction, elliptical footprint, sharpness, exposure, and confidence.
It is immediately projected to surface
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

## 28. Infinite ContactWorld

Chunks are storage/local-coordinate units (initially around `4x4x4 m`), never voxel
geometry or room semantics:

```text
Chunk {
    local/world transform
    ContactFilms with SurfaceChartGeometry
    ContactBoundaries with BoundaryCurve geometry
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
segmented pools can use the actual safe Quest app memory budget. In particular, the
per-cell transient solve retains all information words and cells but exposes its
base-Grid16 and micro-Grid8 arenas as separate bindings under the Quest 128 MiB
single-buffer limit. A storage-binding limit never authorizes reducing films,
microtile depth, sensor resolution, posterior precision, or canonical detail.
Pressure changes residency/scheduling, never canonical detail.

## 30. Revisit is continuation of one ContactFilm posterior

Revisit loads canonical films/chart geometry, boundaries, information/covariance, displacement,
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

## 32. Derived closed PressureManifold render mesh

Dirty charts materialize their SurfaceChartGeometry and manifold adjacency into
adaptive GPU meshlets. Neighbouring charts share/snap boundary vertices; incompatible
but adjacent first-contact charts receive an uncertainty-bearing elastic connector
rather than a hole. Only the outer LatentFrontier closes to its optical injection
seed, so no internal tile edge can form an eye ray. A standard base chart may map
to at most `16x16` cells / `17x17` base vertices / `512` base triangles before
microtile refinement, but flat areas tessellate more coarsely and boundaries,
curvature, and supported displacement more finely. Vertices along a
`ContactBoundary` snap to the refined `BoundaryCurve`. Coverage is sampled once per
materialized vertex and controls confidence, refinement, appearance and LOD only;
it never deletes triangles. Canonical FilmA/FilmB boundary links emit exact-index
connector meshlets, while smooth chart joins weld/merge. Latent connector vertices
carry no measured FilmID and prediction discards them, preventing derived closure
from contaminating first-hit association.

Materialization is not a serial one-thread-per-film loop. One `8x8` GPU workgroup is
assigned to each dirty film: its 64 lanes cooperatively evaluate up to all `17x17`
base vertices, cache the continuous information field in group-shared memory, count
and emit up to 512 base triangles, and reserve output only for active films. Recursive
microtile detail and boundary refinement are materialized by the Q3-16 indirect
refinement stage rather than serializing or globally over-tessellating every film. This changes
scheduling, not quality: there is no reduced chart resolution, dropped measurement,
or CPU fallback. Meshlets use generation IDs and double-buffered atomic publication.

## 33. Realtime renderer and dynamic LOD

The renderer consumes ordinary indexed meshlets, base/normal/PBR virtual pages, and
directional residuals using Vulkan hardware rasterization and Z-buffer.
GPU frustum/Hi-Z culling, screen-space geometric error, confidence, and page
residency generate indirect draw lists. Geometry and appearance LOD are independent;
close inspection uses maximum measured detail while distant views stay efficient.

Scan preview transparency applies only to the PRISM mesh material, never to the VR
menu, compositor layer, passthrough provider or prediction MRT. During geometry
bring-up the default is mesh-local alpha blending with depth writes: passthrough
remains visible, while the first drawn contact prevents hidden layer stacks from
becoming an alpha soup. It uses no global shader/UI alpha and no screen-space dither,
so head motion cannot turn the preview into a flickering Bayer pattern. Observed
contact is more opaque as confidence rises; high-sigma latent connectors/frontier
are visibly softer.
Prediction is a separate one-sided pass and discards every vertex/descriptor without
a valid measured FilmID/generation, including latent connectors and seed closure.
Stable export tessellation never bakes preview alpha as missing geometry.

## 34. Native canonical persistence

GLB is not reconstruction state. Persist a versioned `.prism` world:

```text
world/
├── manifest + rig calibration epoch
├── chunks/<id>/
│   ├── contact films + chart geometry + information/covariance
│   ├── pressure manifolds + links + ordered latent-frontier loops
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

The persisted ABI is explicitly versioned and self-describing: magic, schema,
endianness, record strides/counts, calibration epoch, chunk transform, pool
generations, CRC/checksum and feature flags precede payload pages. Unknown required
features fail closed; older supported records are widened with new fields zeroed or
latent, never reinterpreted by current struct size. Derived meshlets may be omitted
or regenerated, but manifold/link/frontier, films, information, displacement,
boundaries and appearance sufficient statistics are canonical and durable.

Ordinary Scan Stop is a sensor-ingress pause, not reconstruction teardown. It keeps
canonical film/boundary/displacement arenas, prediction targets, GPU rings and the
compiled work graph resident. Durable persistence stages an immutable GPU copy and
may read only that detached copy asynchronously. Stop -> Start resumes the same
posterior without disposing/reallocating multi-gigabyte arenas, without losing the
last published meshlet generation, and without racing the live buffers. Full graph
teardown is reserved for explicit resource release or application destruction.

## 35. Direct GLB/PBR export

```text
canonical ContactFilms / SurfaceChartGeometry
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

The production assets/stage ownership are these bounded compute/raster passes. A
single asset may expose several clear/compact/indirect/solve kernels; the names below
are the repository contract, not illustrative aliases:

```text
01 RigImageCopy.compute                    borrowed native textures -> leased GPU rings
02 ConeLut.compute                         immutable ray/differential/solid-angle LUTs
03 DepthNormalize.compute                  independent metric cone range
04 DepthConsensusNormalBoundary.compute    L/R evidence, learned sigma, normals, edges
05 PredictContactFilm.shader               dual-depth-eye measured-film MRT prediction
06 HiZRangePyramid.compute                 conservative front range hierarchy
07 ConeClassify.compute                    ROI + exhaustive finite-cone event queues
08 ContactFilmSpawn.compute                components, robust quadratic charts
09 ContactFilmUpdate.compute               H/g pressure posterior and hypotheses
10 ContactBoundaryUpdate.compute           persistent curves and FilmA/FilmB evidence
11 DisplacementTopology.compute            per-cell pressure/resistance + microtiles
12 TopologyAdapt.compute                   split/merge/link/frontier transfer
13 PhotometricFocus.compute                L/R narrow stereo and temporal normal solve
14 TextureAccumulate.compute               surface EWA/virtual measured pages
15 DirectionalAppearance.compute           diffuse + adaptive directional/PBR state
16 MeshletBuild.compute                    charts/connectors/frontier -> inactive cache
17 MeshletViewCull.compute                 stereo-safe GPU cull/LOD/draw compaction
18 ChunkStage.compute                      immutable dirty canonical page staging
```

Prefix sums/compaction, queues, counts, topology, culling, and draw arguments stay on
GPU. If an appearance asset is not yet present in the repository it remains a
required DAG deliverable; it is not silently folded into a CPU bake or removed from
the product.

## 37. C# orchestration core

```text
RoomScanner / PrismGpuWorkGraph
PrismRigCapture / RigFrameSynchronizer
RigCalibration

ContactFilmPool
PressureManifoldPool
PrismBoundaryGraph
PrismFilmSpawner / PrismFilmUpdater
PrismDisplacementTopology

PrismPhotometricRefiner
PrismMeshletBuilder / PrismWorldMeshletRenderer
KeyframePool / appearance page scheduler

WorldSession / WorldChunkManager
PrismCanonicalChunkCodec / WorldStore

GltfExporter
```

C# coordinates state, resource lifetimes, immutable snapshots, persistence, and
workflow. It does not decide per pixel or traverse live geometry.

## 38. Canonical implementation DAG

| Run | Required outcome |
|---|---|
| **Q3-01** | Preserve fork, verified original Quest build, old implementation branch, and activate PRISM branch/spec |
| **Q3-02** | 2x RGB + 2x depth + timestamped poses + calibration GPU frame contract/dump |
| **Q3-03** | Immutable rig calibration, cone/ray-footprint LUT, and depth normalization |
| **Q3-04** | Independent L/R depth consensus, learned sigma, robust normals, boundary confidence |
| **Q3-05** | Dual-eye prediction raster of current ContactFilm meshlets |
| **Q3-06** | Exhaustive first-hit ConeEvent classification with explicit unknown-behind state |
| **Q3-07** | Robust GPU ContactFilm/SurfaceChartGeometry spawn |
| **Q3-08** | Robust pressure-equilibrium 6x6 posterior refinement and uncertainty collapse |
| **Q3-09** | Multihypothesis/one-sided/opposite-side and bimodal ContactFilms |
| **Q3-10** | Persistent ContactBoundaries with multi-view 3D BoundaryCurves |
| **Q3-11** | Closed PressureManifold graph, elastic chart links/frontier, split/merge, hierarchical displacement, persistent pressure/resistance |
| **Q3-12** | Cooperative watertight manifold/connector meshlets, culling, LOD, indirect renderer |
| **Q3-13** | Infinite chunk paging, native PRISM persistence, restart/revisit |
| **Q3-14** | ContactFilm-conditioned L/R narrow stereo pressure/focusing |
| **Q3-15** | Temporal cone-bundle focusing and information-gain keyframes |
| **Q3-15.5** | Mandatory forensic PressureManifold integrity repair: typed flag ABI, canonical manifold/link/frontier pools, cross-tile/cross-eye spawn union, measured/latent separation, link-safe topology, covariance-consistent pressure, and batched geometry acceptance |
| **Q3-16** | Measured hierarchical displacement/normal refinement |
| **Q3-17** | Immediate surface-space RGB/EWA accumulation |
| **Q3-18** | Multi-view measured texture superresolution/virtual pages |
| **Q3-19** | Directional appearance and confidence-bearing PBR derivation |
| **Q3-20** | Bounded revisit pose/chunk correction |
| **Q3-21** | Direct chunk/region/world GLB/PBR export from PRISM meshlets |
| **Q3-22** | Physical whole-building quality, revisit, scale, and interoperability acceptance |

A run is not complete merely because placeholder code compiles, but verification is
proportional and batched. Use cheap contract/captured-fixture checks while building;
run Android/device verification only at meaningful vertical milestones rather than
retesting the headset after every small pass. Effort target is 90% implementation,
5% testing, and 5% control/prose. The final physical corpus remains mandatory.

## 39. Critical acceptance corpus

Thin plates:

```text
5 mm, 10 mm, 20 mm, 50 mm
front -> back -> front revisit -> repeated close/distant views
```

Both surfaces must remain independent; supported measured thickness and edge error
must improve, not average into one surface.

Visibility/contact test:

```text
scan only the front/side of a pole, plate, recess, and open door
```

The pre-hit interval becomes a supported pressure path and first contact becomes a
high-information chart. Hidden backs remain latent, force-free `UNKNOWN` manifold,
not empty space and not a hole. They stay elastically connected through the outer
frontier collapsed toward the optical seed until another compatible cone field
pushes them into a supported mould contact.

Footprint test verifies projected ellipse area/orientation across range and grazing
angles, prevents detail above measured support, and proves that multiple subpixel
footprints sharpen rather than blur the same film.

Pressure-equilibrium test permutes view order and injects bounded outliers. The
posterior converges to the same supported film within tolerance, sigma shrinks for
consistent baselines, and a weaker later cone cannot pull a stable high-information
surface.

Near/far resistance test scans a static target close with good incidence and sharp
RGB, then repeatedly from farther range and grazing angles. Persisted information
and the quality envelope must show stronger close compression; weak later cones may
confirm support but must not move geometry beyond its posterior bound, inflate
sigma, remove microdetail, or replace the best texture footprint. Reversing the
order must allow the later, demonstrably higher-information close observations to
improve the film.

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
RGB L/R and temporal views refine raw depth along existing film chart normals
surface-space texture measurably sharpens across revisits
directional appearance is retained and PBR is derived honestly
canonical mesh exists continuously through GPU meshlet materialization
direct GLB/PBR exports validate and import correctly
no core capability requires a server or CPU geometry hot path
```

Complexity remains where it directly increases reconstruction quality:
multihypothesis contact films, posterior information/uncertainty, finite-cone
first-hit semantics and explicit unknown space,
persistent boundaries, soft-to-hard capture, surface-conditioned multiview focusing,
adaptive topology/displacement, measured superresolution, directional appearance,
and resumable infinite-world state.

This `CPQ3-2026-08-21-v5` physics is frozen for implementation. Do not substitute
an infinitesimal-ray shortcut, constant-vote averaging, scalar volume, fixed
triangle/surfel soup, Gaussian map, neural reconstruction, or CPU geometry path
because it is easier to implement.
