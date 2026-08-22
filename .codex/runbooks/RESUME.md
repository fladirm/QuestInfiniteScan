# Resume and checkpoint runbook

## Resume

1. Read `new_spec.md` and the control files listed by root `AGENTS.md`, in order.
2. Inspect `git status --short --branch`; preserve unknown user files and captures.
3. Confirm exactly one `S4-xx` node is `in_progress` and inspect its real evidence.
4. Continue from `.codex/STATE.md` without repeating completed gates.

## Node checkpoint

1. Run proportional exact fixtures and compilation for the active node.
2. Record only actual commands/results in that node's `evidence` array.
3. Mark it `done`, activate at most the next dependency-ready node, and update state.
4. Run `python3 Tools/generate_code_graph.py` and
   `python3 Tools/validate_goal_state.py`.
5. Commit that one completed node, its controls and generated graph together.

After the S4-07 commit, create a source-only `git archive` ZIP from that exact
revision and do not activate S4-08 until the user has audited it.
