# Architecture decision log

## ADR-0001 — Hybrid Quest mapper and CUDA DiffSoup worker

- Status: accepted
- Context: QRS already maps and refines textures with Unity compute shaders on
  Quest. DiffSoup optimization combines Python orchestration, PyTorch/autograd,
  custom CUDA rasterization, multiresolution scheduling, and remeshing.
- Decision: retain QRS compute kernels on Quest; use local submaps with bounded
  residency; run DiffSoup per inactive chunk on a notebook; port only the compact
  artifact renderer to Unity for V1.
- Consequences: Wi-Fi is optional for scanning, implementation reuses upstream
  code, and a versioned artifact boundary becomes a critical compatibility surface.

The frozen source audit is `docs/architecture/UPSTREAM_AUDIT.md`.

## ADR-0002 — Versioned data before lifecycle integration

- Status: accepted
- Context: world/chunk persistence, async jobs, and artifacts cross process and
  session boundaries. Unity `JsonUtility` is permissive and weak at schema
  validation.
- Decision: establish explicit schema versions, bounds, hashes, monotonic revisions,
  and fail-closed validators before connecting them to the live scanner.
- Consequences: slightly more initial domain code; substantially safer migrations,
  retries, and atomic renderer promotion.

## ADR-0003 — Repository checkpoint is post-compaction source of truth

- Status: accepted
- Context: conversation context can be compacted and raw session history is not an
  API available to repository code.
- Decision: keep concise latest-two-exchange snapshots plus durable goal/state/DAG
  files. Resume reads them in the order specified by `AGENTS.md`.
- Consequences: checkpoint updates are manual but auditable; implementation evidence
  in code/tests overrides stale prose.

## ADR-0004 — Explicit movable volume transform

- Status: accepted
- Context: QRS volume helpers, integration, extracted vertices, indirect renderer,
  freeze sampling, and triplanar sampling all currently treat volume coordinates as
  world coordinates centered at zero.
- Decision: define `worldFromChunk` as chunk-local to world and pass its inverse
  through every relevant compute/render boundary. Local chunk geometry remains
  stable when the pose graph changes.
- Consequences: rollover is more than a camera-pose wrapper, but existing TSDF and
  Surface Nets algorithms remain unchanged.

## ADR-0005 — Clean job server, behavioral compatibility only

- Status: accepted
- Context: the pinned Gaussian server has a useful LAN lifecycle but a global job
  manager, unbounded-before-read upload handling, and no resolvable repository
  license file despite a README MIT claim.
- Decision: implement a clean server in this repository with versioned
  world/chunk/revision jobs and invoke pinned DiffSoup as a worker. Do not copy the
  upstream server source.
- Consequences: slightly more API work, with clear licensing, bounded inputs,
  durable jobs, restart recovery, and deterministic contract tests.

## ADR-0006 — Surface protection reuses the packed TSDF and derives orientation

- Status: accepted
- Context: distant revisits pulled already-good surfaces, and the upstream
  normalized/metre comparison integrated roughly 30 cm behind a default depth
  surface. A separate normal/quality volume would increase active Quest memory and
  require a snapshot-format migration.
- Decision: retain the exact RG8_SNORM TSDF/confidence and RGBA8 color/quality
  layout; derive existing orientation from six TSDF neighbors only for confident
  near-surface voxels; arbitrate with distance, incidence, confidence, best known
  quality, orientation, and dilated-depth visibility; cap negative support by the
  smaller of `voxelMin` and 1.25 voxels.
- Consequences: old chunk snapshots remain byte-compatible and the active volume
  stays 96 MiB, while resolvable opposite wall faces no longer share a deep update
  band. Close, genuinely better observations can still correct geometry in bounded
  steps; physical Quest performance remains an explicit acceptance gate.

## ADR-0007 — Durable robust pose graph over immutable chunk-local geometry

- Status: accepted
- Context: tracking edges establish nominal chunk placement, but overlap and loop
  observations must correct accumulated drift without resampling local TSDFs or
  blocking the scan frame. A graph correction also changes the frame used by the
  active volume and future keyframes.
- Decision: build immutable bounded point/normal clouds from finalized Surface Nets
  readbacks, run deterministic point-to-plane ICP in a cancellable worker, and admit
  only constraints with explicit covariance, confidence, and provenance. Optimize
  SE(3) chunk vertices with robust outlier rejection and one fixed root per connected
  component. Persist the edge and detached pose solution through atomic, revision-
  checked `WorldStore` commits; never edit local geometry or artifact payloads.
- Consequences: global corrections are cheap in world size and auditable. Cached
  meshes, DiffSoup renderers, the active TSDF frame, and future keyframe conversion
  are refreshed together. A persisted local volume remains valid after its world pose
  moves; its stored capture pose is diagnostic metadata, not an equality lock.

## ADR-0008 — Directional geometry replaces the scalar mapper after checkpoint

- Status: accepted as a post-checkpoint boundary
- Context: scalar TSDF surface protection can reject contradictory observations but
  cannot retain two surface hypotheses in one voxel. Directional TSDF addresses this
  class of thin partitions, columns, pipes, rails, and object edges, but does not
  eliminate voxel-resolution limits. Its public InfiniTAM-derived reference code is
  non-commercial, and the user explicitly scopes this project as non-commercial.
- Decision: first stabilize chunk lifecycle, then add general depth-edge/normal
  confidence and bounded fusion-conflict telemetry to the scalar baseline. Use a
  temporary backend boundary and captured corpus to port and compare the reference
  directional allocation/fusion/extraction code under its retained copyright,
  source, attribution, and non-commercial license. Once parity, memory, persistence,
  and device gates pass, sparse DTSDF replaces scalar TSDF + scalar Surface Nets in
  the main mapper path. Do not add six dense volumes, speculative K=2 voxels,
  RGB-generated geometry, or adaptive fine bricks before the simpler port proves
  parity.
- Consequences: low-risk measurement improvements are reusable now, while the large
  replacement remains testable, license-separated, memory-bounded, and does not delay
  the current feature checkpoint. Detailed reasoning is in
  `docs/architecture/DIRECTIONAL_GEOMETRY_DECISION.md`.
