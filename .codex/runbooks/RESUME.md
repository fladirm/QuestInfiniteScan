# Resume and checkpoint runbook

## Resume

1. Read `specka.md` and the five control files listed in root `AGENTS.md` in order.
2. Run `git status --short --branch` and preserve unknown changes.
3. Run `python3 Tools/validate_goal_state.py`.
4. Confirm exactly one DAG node is `in_progress`; if none, select the earliest
   dependency-ready node. Do not skip an unfinished prerequisite.
5. Inspect the evidence and relevant code before changing the selected node.
6. Continue from `.codex/STATE.md`'s next concrete action.

## Checkpoint

1. Run the narrowest relevant tests, then broader available validation.
2. Put commands and meaningful results in the node's `evidence` list.
3. Mark a node `done` only when every acceptance item is evidenced. Move exactly one
   dependency-ready node to `in_progress`.
4. Update `.codex/STATE.md` and the two-exchange snapshot.
5. Run the control-plane validator and inspect `git diff --check`.

## When blocked

- Exhaust safe local inspection and deterministic alternatives first.
- Record the exact missing hardware, credential, external state, or user decision.
- Keep the node active unless the goal system's blocked threshold is actually met.
- Provide the exact resume command or device procedure, not only a prose label.
