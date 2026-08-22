# Latest exchange snapshots

After compaction read `new_spec.md`, goal, state, DAG, this file and relevant ADRs
in the order in `AGENTS.md`. Trust checked-in code/evidence over prose and never
touch `~/.codex` or untracked device/archive data.

## Exchange 1

User: Asked for current status during the new independent Σ-PRISM pursuit and
required one commit after every DAG node, with a source-only `git archive` after
S4-07 before further work.

Assistant: Continued the active goal without restarting completed work. S4-00 and
S4-01 remained accepted checkpoints; S4-02 was implemented as the sparse exact
carrier/codec vertical slice.

## Exchange 2

User: Reiterated that effort must stay approximately 90% implementation and only
10% proportional tests plus control-file overhead.

Assistant: Kept work on implementation, batched verification at the S4-02 gate,
and deferred controls to this completion checkpoint. S4-02 now has exact CPU/GPU
codec parity, byte-identical restart, 26/26 Vulkan EditMode tests and a successful
Android build; S4-03 is the next active node.
