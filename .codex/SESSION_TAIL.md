# Latest exchange snapshots

After compaction read `specka.md`, goal, state, DAG, this file, and relevant ADRs in
the order in `AGENTS.md`. Trust code/evidence over prose, never repeat accepted work,
and never touch `~/.codex` or session/history data.

## Exchange 1

User: Reported that Start Scanning no longer worked at all.

Assistant: Device evidence showed the app remained resumed without a crash, but
depth ingress never opened. The UI generated repeated partial Start sequences from
one physical click and every sequence stopped before the explicitly awaited active-
chunk residency boundary.

## Exchange 2

User: Required the repaired APK to be deployed and the exact sources preserved as a
local commit plus a workspace `git archive` ZIP.

Assistant: Root cause was duplicate retained UI Toolkit ClickEvent handlers: one
click alternated Start/Stop, and the false Stop began a roughly 300 MiB canonical
snapshot which subsequent starts awaited. The fix makes UI binding idempotent,
serializes scan lifecycle and coalesces Start requests, prevents Starting from
staging, removes redundant residency activation, surfaces activation failure, and
bounds the GPU-fence wait. Vulkan tests and Android build passed; deployment and the
exact-source checkpoint are the remaining actions.
