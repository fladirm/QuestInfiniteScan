# Latest exchange snapshots

After compaction read `specka.md`, goal, state, DAG, this file, and relevant ADRs in
the order in `AGENTS.md`. Do not repeat accepted work. Never touch `~/.codex` or
session/history data.

## Exchange 1

User: Asked whether the implementation mental model is exact and complete.

Assistant: Confirmed the full pipeline: coherent stereo RGB-D rig; one-sided
probabilistic quadratic charts with information/covariance and sparse detail;
renderer-based first-hit association; non-averaged hypotheses; persistent 3D
boundaries; soft shell; chart-conditioned stereo/temporal focusing; adaptive GPU
meshlets/LOD; surface-space superresolution/directional appearance; resumable chunks,
pose graph, and direct GLB, all GPU/indirect and fully offline.

## Exchange 2

User: Supplied the final 40-section PRISM-Q3 specification, explicitly declared it
canonical, authorized improvements but no quality-reducing simplification, and asked
to save it as `specka.md`, put it into the DAG, and implement it.

Assistant: Saved an enhanced canonical `specka.md` preserving every reconstruction
mechanism and adding hierarchical microtiles, procedural posterior shell, monotonic
geometry/texture quality, GPU-indirect hot path, dynamic non-destructive LOD, native
PRISM resumability, and the exact `Q3-01` through `Q3-22` implementation runs.
