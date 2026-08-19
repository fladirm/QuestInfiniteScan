# QuestInfiniteScan working contract

These instructions apply to the whole repository. They exist so work resumes from
the repository after context compaction instead of rediscovering or reimplementing
completed work.

## Resume order

At the start of every implementation turn, and after any context compaction, read:

1. `.codex/GOAL.md`
2. `.codex/STATE.md`
3. `.codex/TASK_DAG.json`
4. `.codex/SESSION_TAIL.md`
5. `.codex/DECISIONS.md` only when the current task touches an architectural choice

Trust checked-in code and verification results over prose. Never redo a DAG node
marked `done` unless its acceptance evidence is missing or the implementation has
since regressed.

## Execution budget

- Spend roughly 80% of effort on product code, tests, builds, and device/server
  verification; keep process/docs/coordination at or below roughly 20%.
- Update control files at meaningful checkpoints, not after every small edit.
- Keep at most one DAG node `in_progress`.
- Prefer the smallest end-to-end vertical slice that leaves a testable result.

## Product guardrails

- Preserve the existing QuestRoomScan GPU TSDF, Surface Nets, and atlas compute
  pipeline unless a measured incompatibility requires a change.
- Keep notebook/network work outside the real-time scan critical path. Scanning
  must continue when the server is absent, slow, or returns a bad artifact.
- Bound active Quest GPU volume memory independently of world size. Persist or
  unload inactive chunks; never grow one global TSDF to cover the building.
- All persisted world, chunk, graph, job, and DiffSoup artifact formats must be
  explicitly versioned and validated before allocation or rendering.
- Treat spatial anchors as relocation inputs, not as a substitute for geometric
  graph constraints. Apply loop-closure corrections to chunk transforms rather
  than silently mutating local chunk geometry.
- V1 DiffSoup training remains in the upstream Python/PyTorch/CUDA worker. Do not
  port training/autograd/remeshing to Quest or introduce wgpu in the Unity app.
- The Quest DiffSoup runtime must use ordinary indexed triangle rendering and a
  bounded shader/LUT path; reject unsupported artifact versions cleanly.
- A returned artifact may replace presentation only after complete validation and
  successful GPU resource creation. Keep the prior representation as fallback.
- Do not commit credentials, LAN addresses, captured room imagery, generated model
  artifacts, Unity caches, CUDA build products, or third-party model weights.
- Never delete, prune, relocate, compress, or otherwise modify Codex sessions,
  histories, goals, caches, or any content under `~/.codex`; the user explicitly
  requires all of it to be preserved.
- Disk cleanup is limited to clearly regenerable build artifacts owned by this
  repository or its dedicated test environments (for example this project's
  `Library/`, `Temp/`, `obj/`, build output, and disposable dependency caches).
  Do not clean other repositories or broad user/system caches merely to gain space.
- Preserve upstream compatibility where practical and isolate new functionality
  behind modules/interfaces and an opt-in large-world mode.

## Change and verification rules

- Preserve unrelated changes and avoid destructive git operations.
- Add Unity `.meta` files for new Unity-visible assets.
- Pure data/domain logic should be testable without Quest hardware when practical.
- Validate every touched layer proportionally: format/unit tests, server API tests,
  Unity compilation, Android build, then Quest smoke tests. Never claim a hardware
  result that was not actually run.
- Record blocked hardware-only checks in `.codex/STATE.md` with an exact command or
  runbook for the next operator.

## Checkpoint protocol

Before a likely compaction, handoff, or final response:

1. Update `.codex/STATE.md` with completed work, current node, next concrete action,
   changed files, and the last verification outputs.
2. Update statuses/evidence in `.codex/TASK_DAG.json`, then run
   `python3 Tools/validate_goal_state.py`.
3. Add architectural choices to `.codex/DECISIONS.md` only when a real decision was
   made.
4. Replace `.codex/SESSION_TAIL.md` with intent-preserving snapshots of the latest
   two user/assistant exchanges. Keep it concise and point to durable specs/code.
