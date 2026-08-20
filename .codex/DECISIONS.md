# Active architecture decisions

The canonical detail is in `specka.md`. Superseded hybrid/DiffSoup/DTSDF decisions
remain recoverable from archive commit `e9f37c1` and are not active on this branch.

## ADR-P001 — One canonical spec and isolated branch

- Decision: develop PRISM-Q3 on `feat/quest-radiance-meshlets`; preserve all earlier
  work and its DAG on `archive/hybrid-diffsoup-checkpoint-20260820`.
- Consequence: `specka.md` wins over summaries and may be improved but not weakened.

## ADR-P002 — Probabilistic one-sided SurfaceCharts are canonical

- Decision: canonical geometry is a graph of one-sided local manifolds with
  quadratic base, sparse hierarchical displacement, posterior information/
  covariance, sidedness, visibility, boundaries, UV, appearance, and revision.
  Meshlets and GLB are derived materializations.
- Consequence: representation resolution follows evidence, and opposing/nearby
  surfaces do not compete for one voxel or averaged primitive.

## ADR-P003 — First-hit rays and renderer-based association

- Decision: depth supplies supported free space only before its first hit. Render
  current charts from exact eye poses into depth/normal/chart/UV/sigma MRTs and
  classify measured rays against that prediction. Never carve behind a hit.
- Consequence: visibility and association use the hardware rasterizer; contradictory
  evidence becomes a hypothesis/boundary/split rather than an average.

## ADR-P004 — Posterior update and monotonic information quality

- Decision: charts accumulate quadratic-basis `H/g` and solve deterministic 6x6
  systems; displacement, boundaries, and texels likewise retain uncertainty and
  quality envelopes. Lower-information observations cannot degrade stable better
  data.
- Consequence: revisits converge a posterior instead of repeatedly smoothing the
  map, and uncertainty directly schedules refinement.

## ADR-P005 — Persistent boundaries and probabilistic soft-to-hard surfaces

- Decision: multi-view depth/RGB/visibility evidence creates uncertainty-bearing 3D
  spline BoundaryCurves. Immature charts procedurally sample their normal posterior
  as an adaptive GPU shell and collapse to one opaque surface as sigma shrinks.
- Consequence: edges and capture range are first-class without a volume, permanent
  alpha cloud, or depth-edge bridges.

## ADR-P006 — Surface-conditioned RGB geometry refinement

- Decision: after a chart exists, stereo and temporal refinement solve only a small
  normal displacement using calibrated current/historical views and posterior prior.
  There is no global correspondence search, cost volume, or neural MVS.
- Consequence: Quest motion and both RGB cameras contribute sub-depth information
  exactly where unresolved geometry can benefit.

## ADR-P007 — Surface-space measured appearance remains richer than PBR

- Decision: chart UV exists at spawn. EWA footprints build multiresolution measured
  superresolution and canonical diffuse plus adaptive directional state. PBR is a
  confidence-bearing derivative; uncertain metallic is zero.
- Consequence: GLB interoperability does not destroy measured view-dependent data or
  block later refinement.

## ADR-P008 — GPU-only hot path and indirect derived caches

- Decision: pixel work, chart allocation/update, topology, meshlet build, culling,
  LOD, virtual-page feedback, and drawing are GPU/indirect. CPU owns small workflow
  and durable manifests. Only fenced immutable dirty pages stage asynchronously for
  persistence/export.
- Consequence: no synchronous readback, CPU mesh, CPU per-pixel work, or fixed global
  geometry/texture resolution enters the production loop.

## ADR-P009 — Resumable PRISM chunks and pose corrections

- Decision: chunks are local storage/residency units containing the full posterior,
  boundaries, microtiles, appearance, and views. Revisit resumes it. Meta pose is a
  strong prior; accepted small SE(3) revisit constraints update chunk transforms,
  not local geometry.
- Consequence: world size grows on flash with local active cost, and week-later
  refinement remains possible.

## ADR-P010 — Reuse infrastructure, replace the reconstruction product

- Decision: retain Quest shell, world/store/pose graph, resource fences, and GLB
  primitives. Replace the mapper, renderer, appearance model, persistence payload,
  workflow/UI, then remove TSDF/DTSDF/Surface Nets/GS/DiffSoup/server production
  wiring after PRISM physical parity.
- Consequence: implementation can remain buildable during migration while shipping
  one coherent product architecture.
