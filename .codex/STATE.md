# Execution state

Updated: 2026-08-21 (Europe/Prague)

## Source of truth

- `specka.md` is the frozen canonical Cone-PRISM-Q3 product and reconstruction
  physics specification (`CPQ3-2026-08-21-v5`).
- `.codex/TASK_DAG.json` is the only active pursuit DAG.
- `.codex/runbooks/Q3-15.5_PRESSURE_MANIFOLD_REPAIR.md` is the mandatory repair
  run before geometry work may advance to Q3-16.
- The product remains pure Quest: finite first-hit cone fields, conserved one-sided
  pressure manifolds, persistent boundaries, covariance/information resistance,
  stereo/temporal focusing, adaptive displacement, surface-space appearance,
  out-of-core chunks, realtime meshlets and direct GLB/PBR. Do not replace this
  with TSDF/DTSDF, surfels, patch soup, triangle soup, Gaussian training, CPU
  meshing/readback or a server reconstruction path.

## Repository and branch safety

- Active branch: `fix/cone-prism-closed-pressure-manifold-20260821`.
- Forensic baseline: `8f2b31b1bc72`.
- Pre-PRISM recovery branch: `archive/hybrid-diffsoup-checkpoint-20260820`.
- Failed event-chain recovery branch: `archive/prism-event-chain-20260821`.
- Do not push this repair. Make one local commit, then create a new workspace ZIP
  from that exact commit with `git archive` before APK deployment.
- Never touch `.source-archives/`, existing archives, `~/.codex`, Codex sessions or
  conversation history.

## Current DAG gate

- Q3-15.5 is the only `in_progress` node.
- Static implementation phases A-K are complete in the working tree and have passed
  the current Unity/GPU contract suite. Phase L (commit/archive/build/install and one
  physical Quest geometry batch) remains open.
- Q3-07 through Q3-15 remain `pending`: the forensic audit invalidated their former
  physical acceptance. Their implementation is substantially present, but they are
  not accepted until the repaired vertical slice works on-device.
- Q3-16 through Q3-22 remain pending behind this gate.

## Q3-15.5 implemented checkpoint

- Film, meshlet-vertex, meshlet-descriptor and view flags are separate typed ABIs.
  Ordinary measured vertices receive explicit `MeasuredContact`; latent material
  has zero FilmID/generation and cannot enter first-hit prediction.
- `PressureManifoldPool` now owns generation-tagged manifold headers, film
  memberships, typed links, link/frontier incidences, ordered latent-frontier loops,
  optical seeds, allocators, diagnostics and reusable film-slot state.
- Spawn is a bounded GPU pipeline: provisional 8x8 candidates, spatial hash,
  cross-tile/cross-eye compatibility union, representative compaction, aggregate
  fit/support stamping, transactional capacity reservation, then canonical film,
  membership and frontier publication. A tile or eye cannot publish directly.
- Meshlet materialization keeps one continuous chart sheet but explicitly partitions
  measured and latent fragments at continuous support. Measured fragments carry the
  film identity; latent continuation carries zero identity. Persistent boundaries
  cut cells and both linked sides use stable shared boundary samples.
- Screen-space FilmID adjacency no longer creates a physical connector. Canonical
  links require world-space finite-footprint continuation, sidedness, first-hit order
  and multi-view support. Proximity rectangle merge and five-wave merge repair are
  removed.
- Split topology is preflighted transactionally and remaps both FilmA and FilmB
  boundary/link endpoints plus ordered frontier incidences; capacity failure publishes
  nothing. The manifold validator checks stale endpoints, ordered loops and complete
  edge classification before derived publication.
- Contact H/g now consumes bounded absolute physical precision, saturates correlated
  eye/angular/baseline bins, and derives normal sigma from posterior covariance plus
  a model floor. Stored close-view pressure remains monotonic resistance against a
  weaker distant/grazing observation.
- Canonical mutation uses compact active/dirty lists and reusable generation-tagged
  slots. Derived meshlets use capacity-count/validate/commit passes; overflow cannot
  publish a partial generation. Incremental dirty updates can request an entirely
  GPU-side validated repack without CPU count/readback.
- Prediction and the visible world both consume GPU-generated indirect view lists.
  The visible pass uses front-depth then colour, rather than order-dependent
  transparent Z-writing of the entire resident index buffer.
- Native canonical persistence is schema v5 and includes active/dirty indices,
  manifold headers, membership, typed links/incidences, ordered frontier state and
  allocator state. Legacy schemas widen conservatively into explicit unlinked/latent
  state rather than reinterpreting padding.
- Production setup/UI/runtime ownership no longer wires TSDF/Surface Nets,
  triplanar, GSplat, DiffSoup/server, RoomScanPersistence, XAtlas build UI or their
  renderer/cache allocations. Historical implementations remain recoverable in git.

## Verified evidence for the current working tree

- Full Unity EditMode suite: 102 total, 102 passed, 0 failed, 0 skipped.
  Results: `/mnt/kingston-unity/Builds/TestResults/editmode-results.xml`.
  Log: `/mnt/kingston-unity/Builds/TestResults/editmode.log`.
- `Tools/unity/validate_prism_compute_uav.py`: passed; every reachable PRISM compute
  kernel remains at or below the Quest/Adreno eight-UAV limit.
- `git diff --check`: passed.
- `Tools/generate_code_graph.py`: current graph generated for 174 source files in
  `.codex/CODE_GRAPH.json` and `docs/architecture/CODE_GRAPH.md`.
- `Tools/validate_goal_state.py`: control plane valid; 23 DAG nodes, Q3-15.5 is the
  sole active node.
- Android build, commit hash, archive hash, APK hash and install evidence are not yet
  recorded for this repair tree; do not reuse hashes from earlier broken APKs.

## Next exact actions

1. Re-run control/static checks after this state update.
2. Commit the exact repair tree while excluding `.source-archives/`.
3. Create a uniquely named workspace ZIP with `git archive` from that commit.
4. Build a fresh Android ARM64/Vulkan APK from the committed tree and require Unity
   `BuildReport` success with zero errors.
5. Install the exact APK on the connected Quest and record package/version/hash.
6. Run one physical batch: plane continuity, no pole/background curtain, thin plate
   front/back/front, continued scan growth, Stop/Start and revisit.
7. Close Q3-15.5 only if physical geometry and diagnostic counters agree; otherwise
   keep the gate open and repair the demonstrated systemic cause.

## Safety

- Keep Unity builds/caches and device captures on Kingston where possible.
- Do not commit generated builds, device identifiers, credentials, network addresses
  or captured room imagery.
- Do not lower sensor resolution, chart/microtile detail, topology guarantees or the
  GPU-only/indirect hot path to make acceptance easier.
