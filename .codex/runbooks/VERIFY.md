# Verification runbook

Use the applicable layers in this order; later layers do not replace earlier ones.

1. Code map: `python3 Tools/generate_code_graph.py` after each completed DAG task
2. Control plane and graph freshness: `python3 Tools/validate_goal_state.py`
3. Formatting/static hygiene: `git diff --check`, targeted source analyzers
4. Pure C# domain/format tests in Unity EditMode
5. CPU reference versus real-GPU compute/raster parity on synthetic/captured PRISM fixtures
6. Unity package compilation in the pinned Unity 6000.5 project
7. Android Vulkan/IL2CPP ARM64 build
8. Quest 3/3S physical run-specific acceptance from `Q3-02` through `Q3-22`

Device acceptance must capture at least: Unity version, headset OS, package commit,
calibration epoch, stream timing/poses, scene/corpus item, PRISM revision, chart/
boundary/posterior metrics, number of traversed chunks, GPU/RAM/storage residency,
scan/render frame costs, restart/revisit result, GLB validation where applicable,
and logs for any rejected/fallback evidence. Do not substitute a build for the
physical acceptance item named by the active DAG run.
