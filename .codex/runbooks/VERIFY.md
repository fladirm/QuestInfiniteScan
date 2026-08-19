# Verification runbook

Use the applicable layers in this order; later layers do not replace earlier ones.

1. Control plane: `python3 Tools/validate_goal_state.py`
2. Formatting/static hygiene: `git diff --check`, targeted source analyzers
3. Pure C# domain/format tests in Unity EditMode
4. Server unit and HTTP contract tests without CUDA (fake backend)
5. DiffSoup conversion/golden-artifact tests and CUDA smoke job
6. Unity package compilation in a pinned Unity 6000.x test project
7. Android Vulkan/IL2CPP ARM64 build
8. Quest 3 smoke and failure-injection matrix

Device acceptance must capture at least: Unity version, headset OS, package commit,
scene/preset, chunk dimensions, resident-volume cap, number of traversed chunks,
peak GPU/RAM/storage, median and p95 scan frame cost, network-disconnect behavior,
artifact revision/hash, reload/relocation result, and logs for any fallback.

