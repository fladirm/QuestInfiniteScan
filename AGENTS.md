# QuestInfiniteScan working contract

These instructions apply to the whole repository. They keep implementation moving
after context compaction without rediscovering or repeating completed work.

## Resume order

At the start of every implementation turn and after every compaction, read, in
order:

1. `.codex/GOAL.md`
2. `.codex/STATE.md`
3. `.codex/TASK_DAG.json`
4. `.codex/SESSION_TAIL.md`
5. `.codex/DECISIONS.md` when the current node touches architecture

`SESSION_TAIL.md` must preserve the intent of the latest two user/assistant
exchanges. Trust checked-in code and verification evidence over prose. Never redo a
DAG node marked `done` unless its evidence is missing or a later change regressed
it.

## Execution budget

- Spend roughly 80% of effort on product code, shaders, tests, builds, and physical
  Quest verification. Keep process, planning, and prose near 20%.
- Keep at most one DAG node `in_progress`.
- Update control files only at meaningful checkpoints.
- Prefer complete vertical slices over disconnected scaffolds.
- Do not report percentages from file count. Report accepted DAG nodes and the next
  blocked or executable gate.

## Product invariant

The production product is a fully on-device Quest 3/3S spatial scanner. Its
canonical geometry is a chunk-local layered surface pool with adaptive meshlet
topology and confidence-bearing appearance. It does not require a notebook,
network, Python, CUDA, DiffSoup, Gaussian splatting, TSDF, or DTSDF to scan, refine,
render, persist, revisit, or export.

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
- Static LUTs may contain rays, distortion, and epipolar geometry. Depth-to-RGB
  reprojection remains depth-dependent.
- Treat depth triangles/patches as observations, never permanent canonical
  topology.
- Associate observations by rasterizing stable surface IDs/depth/normals from the
  observation pose. A spatial hash/page table is an index, not a voxel geometry
  resolution.
- Fuse only position-, normal-, visibility-, and confidence-compatible evidence.
  Opposite-facing or occluded observations create/target another layer or are
  rejected; they never erase a stable surface.
- Update active geometry with bounded point-to-plane information accumulators.
  Distant or grazing observations cannot degrade a stable close surface.
- Build adaptive local meshlets from stable surfaces. Publish topology with
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
- Keep the headset renderer at 72 Hz. Mapping may be time-sliced at 10–15 Hz with a
  p95 amortized mapper GPU target of 4 ms.
- No storage buffer may exceed 128 MiB. Target active mapper working memory is
  <=1.2 GiB, with a hard fail-closed guard at 2 GiB. World size grows on flash, not
  in the GPU active set.
- Preserve a monotonic per-surface/per-texel quality envelope (projected sampling
  density, range, incidence, sharpness, baseline, exposure, residual, confidence).
  A weaker observation may add support but cannot lower stable geometry or texture
  detail. Replacement requires measured information gain.

## World and persistence guardrails

- Reuse the versioned world/pose-graph/store foundations, but store surface,
  meshlet, and appearance pages rather than TSDF snapshots in the new format.
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

- First produce exposure-normalized base color and geometric normals with explicit
  confidence. Add compact directional residuals incrementally.
- Keep live appearance in GPU-resident, independently streamable multiresolution
  pages. Select geometry and appearance LOD independently from screen-space error,
  visibility, confidence, and bandwidth so close inspection reveals the best
  captured detail without globally inflating residency.
- Roughness is emitted only with evidence and confidence. Metallic remains zero
  until reliable evidence exists; never invent polished PBR maps.
- GLB/PBR export remains mandatory. Support selected chunk, bounded monolithic
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

Every touched layer receives proportional verification: pure contract/unit tests,
compute-shader parity or captured fixtures, Unity compilation/EditMode, Android
Vulkan build, then physical Quest tests. Never claim a device result that was not
actually run.

Before compaction, handoff, or a major commit:

1. Update `.codex/STATE.md` with the current node, changed files, next exact action,
   and verification evidence.
2. Update `.codex/TASK_DAG.json`, then run `python3 Tools/validate_goal_state.py`.
3. Record only real architecture decisions in `.codex/DECISIONS.md`.
4. Replace `.codex/SESSION_TAIL.md` with concise snapshots of the latest two
   user/assistant exchanges.
