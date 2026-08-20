# Latest exchange snapshots

After compaction read `specka.md`, goal, state, DAG, this file, and relevant ADRs in
the order in `AGENTS.md`. Do not repeat accepted work. Never touch `~/.codex` or
session/history data.

## Exchange 1

User: Required a generated graph of files/functions that stays current after every
DAG task so the codebase always has a reliable map.

Assistant: Added deterministic machine-readable and Markdown graphs with file,
symbol, call, event, GPU-kernel, runtime-flow, and DAG ownership relationships. The
control validator now fails stale graphs; checkpoint `3a36f71` was pushed.

## Exchange 2

User: Asked for concrete progress and the percentage remaining while implementation
continued.

Assistant: Reported 9/22 formally accepted runs and roughly 55-60% weighted work
remaining. Then completed Q3-10's GPU multi-view RGB/depth boundary posterior, 3D
cubic controls, conservative promotion/retirement, association protection, and
boundary-aware meshlet snapping; Unity compilation and 10/10 focused tests passed.
