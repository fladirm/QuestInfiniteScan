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

## Exchange 3

User: Repeatedly requested concrete implementation progress and a percentage, while
requiring the permanent code file/function graph to remain current after every task.

Assistant: Completed Q3-11 rather than only reporting it. Added segmented hierarchical
Grid16/Grid8 displacement with close-view quality resistance, persistent bimodal
evidence, GPU-indirect posterior-preserving film split, multi-segment boundary transfer,
conservative stable-plane merge, and displacement-aware meshlet positions/normals.
Unity 6000.5.9f1 passed 104/104 runnable EditMode tests; Q3-12 is now active and the
code graph was regenerated at task close.

## Exchange 4

User: Repeatedly requested implementation progress and the exact remaining percentage,
with implementation strongly preferred over repeated explanations or trivial device tests.

Assistant: Completed Q3-12. Replaced mutable whole-generation rendering with fenced
front/back GPU publications, descriptor-driven adaptive tessellation, conservative exact
boundary clipping, dual-eye range Hi-Z, GPU frustum/occlusion culling, compact indirect
view indices, and independent geometry/appearance LOD. Five focused contracts passed on
both OpenGL and forced Vulkan; Q3-13 native persistence/paging is now active.
