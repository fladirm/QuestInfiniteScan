# Active architecture decisions

Earlier hybrid/DiffSoup and directional-TSDF decisions remain intact on
`archive/hybrid-diffsoup-checkpoint-20260820` at commit `e9f37c1`. They are not
active product decisions on this branch.

## ADR-1001 — Isolate the architecture pivot in a new branch

- Status: accepted
- Context: the hybrid implementation contains valuable world, persistence, GLB,
  build, and diagnostic work but its production mapper/server goal is superseded.
- Decision: preserve it verbatim in the pushed archive branch and implement the
  on-device mapper on `feat/quest-radiance-meshlets`.
- Consequences: reusable foundations can be adapted without losing a buildable
  historical checkpoint or the old DAG.

## ADR-1002 — Canonical layered surfaces with adaptive meshlets

- Status: accepted
- Context: scalar/directional voxel fields impose resolution/memory trade-offs, and
  permanent per-frame depth triangles accumulate noisy duplicate topology.
- Decision: transient depth patches are observations. Canonical chunk geometry is a
  layered pool of stable surface records plus point-to-plane information state;
  adaptive meshlets are a separately published topology over those records.
  Position/normal/visibility-incompatible evidence allocates or targets a separate
  surface layer.
- Consequences: opposing sides, thin objects, poles, and silhouettes do not compete
  for one signed-distance value; topology and spatial indexing no longer determine
  metric resolution.

## ADR-1003 — One immutable synchronized stereo RGB-D frame contract

- Status: accepted
- Context: Quest exposes two calibrated RGB cameras, stereo environment depth,
  timestamps, intrinsics/extrinsics, and tracked poses. Wrong timestamp/pose pairing
  creates irreversible map errors.
- Decision: `StereoRigFrame` owns all four GPU views and calibration/pose metadata.
  `RigFrameSynchronizer` accepts only frames satisfying explicit timestamp, pose,
  tracking, and intrinsic-version gates. It fails closed and records rejection
  reason. Static LUTs contain ray/distortion/epipolar terms only; reprojection uses
  actual depth.
- Consequences: every downstream pass consumes one coherent frame and stale-eye
  fusion is structurally difficult.

## ADR-1004 — GPU-only live pipeline, indirect work, and dynamic LOD

- Status: accepted
- Context: CPU readback, Unity `Mesh` reconstruction, and CPU draw traversal would
  destroy latency and bandwidth on Quest as resolution grows.
- Decision: association, fusion, regularization, topology build, compaction,
  visibility, LOD, and rendering remain in compute/raster GPU passes. GPU-generated
  counters drive indirect dispatch and indexed indirect draw. Geometry and
  appearance LOD are selected independently from screen-space error and confidence.
  CPU handles small workflow/manifests only. Persistence/export may consume fenced,
  immutable pages through bounded `AsyncGPUReadback`; it never blocks capture or
  rendering.
- Consequences: the hot path scales with visible/dirty work, not world size. Export
  latency is decoupled from scanning.

## ADR-1005 — Monotonic information quality

- Status: accepted
- Context: the previous mapper let later distant or grazing depth pull a stable
  surface and let weak imagery wash out sharper texture.
- Decision: geometry and texels retain a quality envelope including projected
  sampling density, range, incidence, sharpness, stereo/temporal baseline,
  exposure, residual, and confidence. A weaker observation can confirm occupancy or
  update uncertainty but cannot reduce spatial/detail state. Replacing or moving a
  stable value requires measured information gain and a bounded robust residual.
- Consequences: deliberate close scans remain authoritative while genuinely better
  revisits can still refine them.

## ADR-1006 — Native depth first, narrow MVS only on uncertain tiles

- Status: accepted
- Context: full-frame learned stereo is too expensive and may hallucinate; native
  depth is metric but loses detail at edges and weak surfaces.
- Decision: first compute cross-eye/temporal depth consensus, normals, discontinuity,
  and confidence. A budgeted GPU tile scheduler runs 8–16 native-depth-centered
  hypotheses using robust gradient/Census costs against the other eye and 2–4 valid
  temporal frames. Inconsistent solutions remain unresolved.
- Consequences: compute concentrates on silhouettes, thin structures, and missing
  regions while stable planes stay cheap.

## ADR-1007 — Two-arena transitions and page-level durability

- Status: accepted
- Context: the archived mapper allowed transitions to outrun finalization, retained
  only one previous snapshot, and failed to rehydrate evicted chunks, causing map
  disappearance after repeated rollovers.
- Decision: source and target chunk arenas overlap. Source remains published and
  renderable while target integrates. Dirty immutable pages stage asynchronously;
  durable publication precedes eviction. Visible-page residency actively
  rehydrates on demand. Revisit starts from the last complete revision.
- Consequences: world growth remains O(1) in active GPU residency without sacrificing
  continuity or recovery.

## ADR-1008 — Honest incremental appearance, not training

- Status: accepted
- Context: view-dependent appearance improves perceived fidelity, but server-side
  training contradicts the on-device goal and unconstrained PBR inference is not
  physically identifiable.
- Decision: use bounded GPU incremental least-squares for exposure-normalized
  diffuse plus compact directional residuals. Multiresolution appearance pages keep
  the best supported texel density. Normals/roughness/metallic carry confidence;
  uncertain metallic is zero.
- Consequences: appearance improves during revisits without network, optimizer
  state, or fabricated certainty.

## ADR-1009 — Retire legacy paths only after captured-corpus parity

- Status: accepted
- Context: deleting the old mapper before the replacement can build and render
  would prevent device iteration; leaving it indefinitely would produce a confused
  product and UI.
- Decision: introduce the new mapper behind a temporary migration boundary, pass
  synthetic/captured A/B and Android gates, then remove TSDF/DTSDF/Surface Nets,
  GS, HeavyCompute/DiffSoup/server production code and UI. The archive branch is the
  recovery path.
- Consequences: migration stays testable, while the shipped package ends with one
  coherent architecture.

## ADR-1010 — Task-oriented operator experience

- Status: accepted
- Context: the inherited debug menu exposes implementation mechanisms and server
  controls rather than scan quality and workflow.
- Decision: the primary UI is Scan, Worlds, Quality, Export, Settings, backed by an
  explicit workflow state machine and immutable diagnostic snapshots. Fast,
  Balanced, and Detail profiles change budgets, not data semantics. Developer
  diagnostics are opt-in.
- Consequences: operators see whether capture is trustworthy and where more views
  are needed without learning mapper internals.
