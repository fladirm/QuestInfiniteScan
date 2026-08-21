# Active architecture decisions

The canonical detail is in `specka.md`. Superseded hybrid/DiffSoup/DTSDF decisions
remain recoverable from archive commit `e9f37c1` and are not active on this branch.

## ADR-P001 — One canonical spec and isolated branch

- Decision: develop PRISM-Q3 on the active isolated implementation/fix branch;
  preserve all earlier work and its DAG on
  `archive/hybrid-diffsoup-checkpoint-20260820`.
- Consequence: `specka.md` wins over summaries and may be improved but not weakened.

## ADR-P002 — Probabilistic one-sided ContactFilms are canonical

- Decision: canonical world state is a graph of one-sided `ContactFilm` hypotheses;
  each film owns `SurfaceChartGeometry` with quadratic base, sparse hierarchical
  displacement, posterior information/covariance, sidedness, first-contact
  visibility, ContactBoundaries, UV, deposited observations, appearance, and revision.
  Meshlets and GLB are derived materializations.
- Consequence: representation resolution follows evidence, and opposing/nearby
  surfaces do not compete for one voxel or averaged primitive.

## ADR-P003 — Finite first-hit cones and renderer-based association

- Decision: each calibrated pixel is a finite cone/truncated-pyramid measurement
  with a projected elliptical footprint. It applies supported outward pressure only
  before its first hit, a contact constraint at the hit, and explicitly UNKNOWN
  state behind it. Render current films from exact eye poses into
  depth/normal/film/UV/sigma MRTs and classify ConeEvents against that prediction.
- Consequence: visibility and association use the hardware rasterizer; contradictory
  evidence becomes a hypothesis/boundary/split rather than an average.

## ADR-P004 — Pressure-equilibrium posterior and monotonic information resistance

- Decision: compatible cone contact pressure linearizes to quadratic-basis `H/g`
  and a deterministic 6x6 solve; it is one solver, not simulated forces plus a
  second estimator. Precision follows measured range noise, footprint, incidence,
  pose/calibration covariance, motion, consensus, and robust innovation. Persisted
  information/covariance and geometry/appearance quality envelopes are film
  resistance. Lower-information observations cannot degrade stable better data.
- Consequence: revisits converge a posterior instead of repeatedly smoothing the
  map, and uncertainty directly schedules refinement.

## ADR-P005 — Persistent boundaries and probabilistic soft-to-hard surfaces

- Decision: multi-view depth/RGB/visibility evidence creates uncertainty-bearing 3D
  `ContactBoundary` entities with spline BoundaryCurve geometry. Immature films procedurally sample their normal posterior
  as an adaptive GPU shell and collapse to one opaque surface as sigma shrinks.
- Consequence: edges and capture range are first-class without a volume, permanent
  alpha cloud, or depth-edge bridges.

## ADR-P006 — Surface-conditioned RGB geometry refinement

- Decision: after a ContactFilm exists, stereo and temporal refinement solve only a small
  normal displacement using calibrated current/historical views and posterior prior.
  There is no global correspondence search, cost volume, or neural MVS.
- Consequence: Quest motion and both RGB cameras contribute sub-depth information
  exactly where unresolved geometry can benefit.

## ADR-P007 — Surface-space measured appearance remains richer than PBR

- Decision: film UV exists at spawn. Finite RGB cone EWA footprints build multiresolution measured
  superresolution and canonical diffuse plus adaptive directional state. PBR is a
  confidence-bearing derivative; uncertain metallic is zero.
- Consequence: GLB interoperability does not destroy measured view-dependent data or
  block later refinement.

## ADR-P008 — GPU-only hot path and indirect derived caches

- Decision: pixel work, ContactFilm allocation/update, topology, meshlet build, culling,
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

## ADR-P011 — Freeze Cone-PRISM reconstruction physics

- Decision: `specka.md` baseline `CPQ3-2026-08-21-v5` freezes finite-cone
  first-hit/contact/unknown semantics, ContactFilms, pressure-information solve,
  ContactBoundaries, monotonic resistance, and meshlets as derived state. Evidence
  may strengthen it; changing those foundations requires explicit user authority, a
  replacement ADR, and DAG re-baseline.
- Consequence: implementation work now proceeds from Q3-02 and cannot silently
  collapse the design back to infinitesimal rays, constant averaging, voxels,
  surfels, or fixed triangle soup for convenience.

## ADR-P012 — Persistent local pressure and cooperative full-detail materialization

- Decision: local pre-hit contradiction is a persisted per-cell pressure posterior
  with independent calibrated eye/angular evidence. It competes against persisted
  close-view precision/support resistance, is cancelled by compatible contact, and
  is consumed when it performs bounded displacement work. It is never a one-frame whole-film
  delete and is not multiplied by displacement children. Derived meshlets are built
  cooperatively by one `8x8` GPU workgroup per film with shared continuous-coverage
  samples and full supported chart/microtile detail.
- Consequence: repeated independent cones can push unsupported view-axis artifacts
  while distant/grazing evidence cannot punch through a strongly baked nearby film;
  reconstruction remains full quality without the serial per-film mesh-build
  bottleneck or a CPU/readback fallback.

## ADR-P013 — Conserved topology, informational coverage, and outer-only closure

- Decision: coverage is posterior confidence/support, never occupancy or a triangle
  deletion mask. Every active chart retains its complete logical lattice. Charts are
  generation-linked members of a conserved `PressureManifold`; compatible borders
  weld, physical discontinuities form elastic FilmA/FilmB connectors, and only one
  ordered outer `LatentFrontier` may return the unresolved sheet to its optical
  injection seed. Latent connector/frontier geometry has no measured FilmID and is
  excluded from prediction/association.
- Consequence: sparse observations cannot expose square tile holes or eye rays, and
  derived closure cannot hallucinate a measured contact. Split/merge must transfer
  topology; an active edge with neither one compatible link nor exactly one ordered
  frontier segment is a publication error, not permission to invent an independent
  cap.

## ADR-P014 — Information-positive aperture and coalesced immutable publication

- Decision: canonical surface spawn is restricted to the calibrated central
  30x30-degree high-quality cone field. The wider 50x46-degree revisit field may
  update already predicted geometry only above an information-confidence floor;
  passthrough/tracking remain full-FOV. Canonical sensor/fusion work stays at native
  resolution and cadence, while all dirty mutations coalesce into at most one
  derived mesh publication request and an initial 15 Hz preview-publication ceiling.
- Consequence: peripheral noise cannot flood topology, and repeated full mesh builds
  cannot block XR. The last immutable mesh generation remains visible until a fenced
  replacement is complete; throttling derived publication never drops measurements
  or lowers canonical detail.
