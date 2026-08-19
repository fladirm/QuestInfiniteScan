# Execution state

Updated: 2026-08-19 (Europe/Prague)

## Repository

- Upstream: `arghyasur1991/QuestRoomScan`
- Upstream checkout commit: `2fdaaae71f60b21b7853e67db943fc42f75d0c2f`
- Working branch: `feat/infinite-submaps-diffsoup`
- Workspace was empty; upstream was cloned at the user's request.
- GitHub CLI is authenticated as `fladirm`; creation of the named fork/remote is
  pending a repository-name collision check.

## Current position

- Current DAG node: `B01`
- Phase: versioned world/chunk/pose-graph domain model and tests
- Product code changed: none yet
- Control plane added: goal, guardrails, DAG, runbooks, decisions, session tail,
  validator

## Confirmed from upstream

- QRS is a Unity 6 UPM package with GPU TSDF, GPU Surface Nets, on-device atlas
  refinement, package persistence, anchors, and optional GSplat assembly.
- Existing GSplat client already implements zip upload, status polling, cancellation,
  and result download, but its legacy endpoints are global rather than keyed by
  world/chunk/revision.
- Local workspace has no Unity host project yet; package compilation will require a
  generated test project or an installed Unity 6000.x editor.

## Completed checkpoint

- `A01` is complete. `docs/architecture/UPSTREAM_AUDIT.md` pins the audited commits,
  exact QRS lifecycle/coordinate/persistence constraints, DiffSoup input/artifact
  contract, server licensing/robustness assessment, and verified host/device stack.
- ADR-0001 is frozen. The audit also establishes the explicit movable-volume
  transform and clean-server decisions.
- Unity Hub 3.19.5 is installed. Unity 6000.0.81f1 and Hub-managed Android modules
  remain to be installed in the dedicated KINGSTON ext4 image after its one-time
  format completes.
- `A02` is complete. `fladirm/QuestInfiniteScan` is a real GitHub fork; `origin`
  points to it, `upstream` fetches the source repository, upstream pushing is
  disabled, and the feature branch exists remotely.

## Next concrete actions

1. Implement `B01` with pure versioned world/chunk/pose-graph types and EditMode
   tests.
2. In parallel with normal implementation waits, finish the KINGSTON mount and
   install the pinned Unity editor/modules without touching Codex data.

## Verification evidence

- `git clone` succeeded and branch was created.
- `git status --short --branch` was clean before adding control files.
- `python3 Tools/validate_goal_state.py`: passed with the initial 18-node DAG.
- ADB reports one authorized target: `Quest_3S`, Android 14, with approximately
  214 GB free in shared storage.
- NVIDIA RTX 4070 Laptop GPU, driver 610.43.02, CUDA 13.3 runtime/toolkit and
  `nvcc 13.3.73` are present.
- git-lfs, adb, CMake, Ninja, Clang/GCC, Python, uv, Rust, Java, Node, and npm are
  already present; do not duplicate them.

## Known gates

- Quest 3S is connected and authorized. Treat it as the target and verify actual
  depth-frame delivery at runtime rather than blocking on the model string.
- Unity Hub 3.19.5 is installed. Editor 6000.0.81f1 and its Hub-managed Android
  modules are not installed yet.
- `/mnt/aidisk` has only ~2.8 GB free and `/` ~7.3 GB; `/dev/sdb1` is FAT/VFAT with
  ~28 GB free. Any relocation must exclude development and all Codex data, preserve
  checksums, and account for FAT's per-file size limit.
- DiffSoup is research code; its verified current contract and compatibility gaps
  are recorded in `docs/architecture/UPSTREAM_AUDIT.md`.
- Disk cleanup must never touch Codex sessions/history/goals/caches or `~/.codex`;
  only this repository's regenerable build outputs are in scope.
