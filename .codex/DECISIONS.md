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
