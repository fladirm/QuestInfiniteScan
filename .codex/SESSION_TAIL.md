# Latest exchange snapshots

After compaction read `new_spec.md`, goal, state, DAG, this file and relevant ADRs
in the order in `AGENTS.md`. Trust checked-in code/evidence over prose and never
touch `~/.codex` or untracked device/archive data.

## Exchange 1

User: Required a separate commit after every completed S4 task and, after the S4-07
commit, a source ZIP produced by `git archive` for audit before any S4-08 work.

Assistant: Recorded that checkpoint protocol in `AGENTS.md` and `.codex/STATE.md`;
it is now a compaction-safe hard execution gate.

## Exchange 2

User: Asked for exact status.

Assistant: Reported S4-00 near closure: old reconstruction/persistence removed,
clean four-stream shell retained, Unity compile and 7/7 rig tests passed, and only
final controls/code-graph validation plus the isolated S4-00 commit remained.
