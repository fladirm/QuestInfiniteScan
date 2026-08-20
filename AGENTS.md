# QuestInfiniteScan working contract

These instructions apply to the whole repository. They keep implementation moving
after context compaction without rediscovering or repeating completed work.

## Resume order

At the start of every implementation turn and after every compaction, read, in
order:

1. `specka.md` (canonical reconstruction/product specification)
2. `.codex/GOAL.md`
3. `.codex/STATE.md`
4. `.codex/TASK_DAG.json`
5. `.codex/SESSION_TAIL.md`
6. `.codex/DECISIONS.md` when the current node touches architecture

`SESSION_TAIL.md` must preserve the intent of the latest two user/assistant
exchanges. Trust checked-in code and verification evidence over prose. Never redo a
DAG node marked `done` unless its evidence is missing or a later change regressed
it.

## Execution budget

- Spend roughly 90% of effort on implementation, 5% on proportional automated/
  milestone testing, and 5% on control files/prose. Do not repeat already-proven
  baseline capture or run a separate headset test for every small contract/pass.
- Keep at most one DAG node `in_progress`.
- Update control files only at meaningful checkpoints.
- After every completed DAG task, run `python3 Tools/generate_code_graph.py`; the
  generated `.codex/CODE_GRAPH.json` and `docs/architecture/CODE_GRAPH.md` are the
  current file/type/function/GPU-kernel/data-flow map and must land in the same
  checkpoint. `validate_goal_state.py` rejects a stale graph.
- Prefer complete vertical slices over disconnected scaffolds.
- Do not report percentages from file count. Report accepted DAG nodes and the next
  blocked or executable gate.

## Product invariant

The production product is a fully on-device Quest 3/3S spatial scanner named
Cone-PRISM-Q3. Reconstruction physics `CPQ3-2026-08-20-v1` is frozen in
`specka.md`. Canonical world state is a chunk-local graph of one-sided probabilistic
`ContactFilm`s; `SurfaceChartGeometry` is their quadratic plus hierarchical
displacement parameterization, `ContactBoundary` is their persistent contact
discontinuity, and meshlets are only derived render/export caches. The product does not require a
notebook, network, Python, CUDA, DiffSoup, Gaussian splatting, TSDF, or DTSDF to
scan, refine, render, persist, revisit, or export.

The retained QuestRoomScan shell supplies Meta XR permissions/session plumbing,
tracking, Unity/Vulkan build setup, anchors, and selected reusable utilities. The
old scalar TSDF/Surface Nets and optional LAN/DiffSoup/GS paths are migration
fallbacks only. Remove their production wiring after the replacement passes A/B
gates; do not leave two competing product architectures.

## Mapping guardrails

- Capture both passthrough RGB streams and both environment-depth views directly as
  GPU textures with timestamps, intrinsics, extrinsics, and poses in one immutable
  `StereoRigFrame` contract.
- Pair frames by timestamp and pose validity. Reject mismatched data; never silently
  fuse a stale eye or reuse a pose from another timestamp.
- Static LUTs contain center rays, ray differentials/cone footprint support,
  distortion, and epipolar geometry. Depth-to-RGB
  reprojection remains depth-dependent.
- Treat each pixel as a finite calibrated cone event: pre-hit space is observed
  free, first hit is contact, and everything behind it is UNKNOWN. Treat transient
  depth triangles/patches as observations, never permanent canonical topology.
- Associate observations by rasterizing film ID, mean depth, normal, UV,
  sidedness, confidence, and normal uncertainty from the observation pose. A
  spatial hash/page table is an index, not a voxel geometry resolution.
- Fuse only position-, normal-, visibility-, and confidence-compatible evidence.
  Opposite-facing or occluded observations create/target another layer or are
  rejected; they never erase a stable surface.
- Update active film shape with bounded robust pressure/information sufficient
  statistics. Pressure precision follows learned range noise, projected footprint,
  incidence, pose/calibration uncertainty, motion, consensus, and robust innovation;
  never use constant voting or blindly assume a universal inverse-square law.
  Persisted information/covariance and quality envelopes are the film's resistance:
  weak far/grazing observations may confirm but cannot pull or blur a strongly
  compressed close film. Geometry is
  hierarchical: a tangent/quadratic base plus sparse multiresolution displacement
  microtiles. The base supplies stable low-frequency structure; microtiles preserve
  all supported high-frequency detail without forcing a global resolution.
- Model the capture basin as normal uncertainty `mu +/- k*sigma`. Uncertain films
  procedurally emit adaptive quadrature shell layers for association/photometric
  focusing; as covariance shrinks they collapse continuously to one opaque surface.
  Shell samples are derived GPU work, not duplicated canonical geometry.
- Accumulate persistent RGB/depth/visibility discontinuities into canonical
  `ContactBoundary` records with uncertainty-bearing 3D spline `BoundaryCurve`
  geometry and GPU-refined controls. Boundaries constrain
  film domains, splits, and tessellation; a noisy single-frame edge cannot create
  or erase a boundary.
- Build adaptive local meshlets from ContactFilms. Publish topology with
  generation IDs and double buffering; the renderer must never consume buffers
  being mutated.
- Keep association, fusion, regularization, topology construction, visibility
  culling, screen-space LOD selection, draw-list compaction, and rendering on GPU.
  Use indirect dispatch/draw arguments; never rebuild live geometry through Unity
  `Mesh`, `GetData`, or synchronous CPU readback.
- CPU orchestration may handle small manifests and scheduling metadata. Geometry or
  appearance may leave GPU only as bounded asynchronous immutable page staging for
  persistence/export, outside the frame-critical path and behind fences.
- Schedule narrow stereo/temporal MVS only for uncertain tiles and only around the
  native metric depth prior. Inconsistent photometric evidence must fail closed.
- Keep realtime preview responsive through GPU queues, dirty work, and indirect
  scheduling. Performance tuning may delay refinement work or evict derived pages;
  it must not lower canonical measurement resolution or discard accepted detail.
- No individual storage buffer may exceed the device-reported Vulkan range (128 MiB
  on the measured Quest). Total mapper residency is not capped by that per-buffer
  value. A runtime memory governor discovers the actual device/app budget, reserves
  measured Horizon/compositor/Unity headroom, and may use multiple segmented pools
  up to the safe budget. Pressure evicts/reloads derived or durable pages; it never
  discards canonical detail. World size grows on flash, not without bound in GPU.
- Preserve a monotonic per-surface/per-texel quality envelope (projected sampling
  density, range, incidence, sharpness, baseline, exposure, residual, confidence).
  A weaker observation may add support but cannot lower stable geometry or texture
  detail. Replacement requires measured information gain.
- Treat Meta tracking as a strong pose prior. Optional GPU residual reduction may
  estimate a small bounded keyframe/chunk micro-correction; it never becomes an
  unconstrained second SLAM or rewrites historical frame timestamps.

## World and persistence guardrails

- Reuse the versioned world/pose-graph/store foundations, but store ContactFilm, ContactBoundary,
  sufficient-statistic, meshlet-cache, observation, and appearance pages rather than
  TSDF snapshots in the versioned `.prism` format.
- Chunk transition is a two-arena overlap: the source remains visible while the
  target accepts observations and dirty source pages publish incrementally.
- Durable publication precedes eviction. A chunk cannot disappear merely because
  finalization or readback is pending.
- Revisit loads the last complete revision, preserves stable layers, and publishes
  a monotonic new revision atomically.
- Pose-graph optimization updates only `worldFromChunk`; it never resamples or
  silently mutates chunk-local geometry.
- Keep active GPU residency bounded independently of scanned building size and
  support arbitrary vertical/multi-floor trajectories.

## Appearance and export guardrails

- Every ContactFilm owns UV at creation. Deposit exposure-normalized finite RGB
  cone footprints directly in film/chart
  space with EWA/footprint weighting, producing multi-frame surface superresolution
  without hallucinated upscaling. Preserve the surface light field using an online
  adaptive mixture of compact directional lobes plus diffuse state; do not collapse
  canonical appearance to PBR or low-order SH merely for export convenience.
- Keep live appearance in GPU-resident, independently streamable multiresolution
  pages. Select geometry and appearance LOD independently from screen-space error,
  visibility, confidence, and bandwidth so close inspection reveals the best
  captured detail without globally inflating residency.
- Roughness is emitted only with evidence and confidence. Metallic remains zero
  until reliable evidence exists; never invent polished PBR maps.
- `.prism` persistence/export is mandatory so a scan can reopen and continue
  refining with uncertainty, chart graph, boundaries, directional appearance, and
  sufficient statistics intact. GLB/PBR remains the interoperable derivative.
  Support selected chunk, bounded monolithic
  world, and `building.json + chunks/*.glb`; preserve pose-graph node transforms.
- Validate output with Khronos glTF Validator and an independent importer.

## Migration and safety

- Keep a buildable fallback until the new mapper passes captured-corpus A/B tests.
  Afterwards remove TSDF/DTSDF/Surface Nets/GS/DiffSoup/server production wiring
  and UI, while preserving the archival Git branch.
- Preserve unrelated user changes. Never use destructive git recovery commands.
- Add Unity `.meta` files for every new Unity-visible asset.
- Never commit credentials, LAN addresses, device identifiers, room captures,
  generated models, Unity caches, Android build products, or third-party weights.
- Never delete, move, compress, prune, or otherwise modify anything under
  `~/.codex`; the user explicitly requires all sessions/history/goals/caches.
- Cleanup is limited to clearly regenerable artifacts owned by this repository or
  its dedicated Kingston development environments.

## Verification and checkpoint protocol

Verification is proportional and batched: cheap contract/unit or captured-fixture
checks plus compilation while implementing, Android builds at meaningful vertical
slices, and physical Quest tests only at consolidated hardware milestones (complete
four-stream-to-film slice, multilayer/boundary slice, persistence/revisit, and final
quality/export). Never claim a device result that was not actually run.

Before compaction, handoff, or a major commit:

1. Update `.codex/STATE.md` with the current node, changed files, next exact action,
   and verification evidence.
2. Update `.codex/TASK_DAG.json`, then run `python3 Tools/validate_goal_state.py`.
   Generate the code graph first with `python3 Tools/generate_code_graph.py`.
3. Record only real architecture decisions in `.codex/DECISIONS.md`.
4. Replace `.codex/SESSION_TAIL.md` with concise snapshots of the latest two
   user/assistant exchanges.
