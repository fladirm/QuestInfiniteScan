# Latest exchange snapshots

After compaction read `specka.md`, goal, state, DAG, this file, and relevant ADRs in
the order in `AGENTS.md`. Do not repeat accepted work. Never touch `~/.codex` or
session/history data.

## Exchange 1

User: Rejected per-triviality Quest testing and fixed the execution ratio at roughly
90% implementation, 5% tests, and 5% control/prose.

Assistant: Adopted milestone-batched physical tests: cheap contracts/compilation
during implementation, with Quest runs only for useful vertical slices such as
capture-to-film, multilayer/boundary, persistence/revisit, and final export/quality.

## Exchange 2

User: Asked for status while expecting continued implementation rather than another
architecture cycle.

Assistant: Completed the Q3-02 four-stream GPU capture contracts and proceeded into
Q3-03: immutable calibration, finite-cone center/Jacobian/angular LUTs, raw Meta
depth encoding metadata, metric depth normalization, and fenced GPU output leases.

## Exchange 3

User: Asked for the exact current DAG state and emphasized that persistence and
other useful QuestRoomScan infrastructure should be reused/adapted instead of
rewritten.

Assistant: Reported Q3-01 through Q3-08 complete, the multilayer work ahead of the
formal Q3-09 marker, and early reusable foundations for boundaries, adaptive
meshlets, canonical persistence, and the existing GLB path.

## Exchange 4

User: Required a generated file/function graph that remains current after every DAG
task so the team always has a navigable codebase map.

Assistant: Added deterministic JSON and Markdown code graphs, task/file ownership,
call/event/GPU-kernel relationships, a runtime-flow view, and a stale-graph failure
to the control-plane validator. Q3-09 was then closed and Q3-10 activated, forcing a
second graph generation from the new DAG state.
