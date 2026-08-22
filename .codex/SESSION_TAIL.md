# Latest exchange snapshots

After compaction read `new_spec.md`, goal, state, DAG, this file and relevant ADRs
in the order in `AGENTS.md`. Trust checked-in code/evidence over prose and never
touch `~/.codex` or untracked device/archive data.

## Exchange 1

User: Asked for exact progress/state after the new independent Σ-PRISM pursuit had
started.

Assistant: Reported only accepted DAG evidence: S4-00 committed as `be95c9e`, S4-01
in progress, with no fabricated file-count percentage, then continued implementation.

## Exchange 2

User: Reiterated the 90% implementation / 10% proportional tests and control-file
budget.

Assistant: Kept work on the S4-01 vertical slice and deferred control updates to
the acceptance checkpoint. S4-01 subsequently passed 21/21 exact CPU/Vulkan tests,
the generated-descriptor/UAV gates and a final Android build; S4-02 is next.
