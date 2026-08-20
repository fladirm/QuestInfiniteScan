# QuestInfiniteScan development and recovery

## Repository topology

```text
origin    git@github.com:fladirm/QuestInfiniteScan.git     writable fork
upstream  git@github.com:arghyasur1991/QuestRoomScan.git  fetch only
branch    feat/infinite-submaps-diffsoup
```

Never rewrite upstream history. Keep machine addresses, captured scans, Unity host
projects, builds, Python environments, CUDA products, and server databases out of the
package checkout.

## Prerequisites

- Ubuntu x86-64 with `git`, `git-lfs`, `adb`, `uv`, Node/npm, CMake/Ninja, and a C++
  compiler.
- Unity Hub plus Unity `6000.5.9f1`, Android Build Support, Unity-managed SDK/NDK,
  and Unity-managed OpenJDK.
- A Developer Mode Quest 3/3S for runtime acceptance.
- For real DiffSoup only: NVIDIA driver, CUDA toolkit 13.3 with `nvcc`, and a tested
  SM89-class GPU. Fake-backend/server/format tests do not need CUDA.

Do not replace the system Python. Server and DiffSoup use isolated Python 3.14.4
environments on ext4.

## External development root

The supplied workstation stores regenerable large state in a 250 GiB ext4 image on
the external Kingston disk:

```bash
Tools/storage/mount_kingston_container.sh
source Tools/storage/dev_environment.sh
```

The default mount is `/mnt/kingston-unity`. `QIS_DEV_ROOT` may name another ext4
mount. `Tools/storage/dev_environment.sh` routes Unity projects/editor roots, builds,
temporary files, Gradle, uv/pip, Torch extensions, CUDA cache, DiffSoup, and server
data beneath it.

The source Git checkout is not moved. User backup data belongs outside the ext4
image. Never delete, relocate, compress, or prune `~/.codex` or any Codex
sessions/history/goals/caches.

## Unity host

Set Unity Hub's editor install path to:

```text
/mnt/kingston-unity/Unity/Hub/Editor
```

Then:

```bash
Tools/unity/verify_unity_install.sh
Tools/unity/create_host_project.sh
```

The host project is generated outside Git and embeds this checkout through a checked
symlink at `Packages/com.genesis.roomscan`. Open it in Unity, run
`RoomScan > Setup Scene`, and re-run the idempotent Game-Ready/Debug presets after any
domain reload.

## Server and pinned DiffSoup worker

```bash
# Reproducibly creates/validates the target Python 3.14.4 + CUDA worker.
Tools/server/bootstrap_diffsoup.sh

# Server environment and hostile-input/restart/fake-backend suite.
Tools/server/run_tests.sh

# Real two-revision optimization, exact warm start, and safe fresh fallback.
Tools/server/run_cuda_tests.sh

# Explicit LAN binding; defaults to port 8420.
Tools/server/run_server.sh
```

`bootstrap_diffsoup.sh` refuses a dirty upstream checkout, pins the recorded commit,
installs exact runtime packages from `diffsoup-worker.lock.json`, compiles the CUDA
extension, and runs a direct worker probe. The API process never imports Torch; it
spawns the worker across a process boundary.

For another CUDA architecture, review upstream's hard-coded CMake architecture and
create a new tested worker lock instead of silently reusing the SM89 claim.

## Verification and build

```bash
# Host-only mandatory gates and an external JSON report.
Tools/verify_all.sh

# Full target-machine feature checkpoint.
QIS_VERIFY_CUDA=1 QIS_VERIFY_ANDROID=1 Tools/verify_all.sh

# Deploy only after a successful non-empty build.
Tools/unity/deploy_smoke_apk.sh
```

The verifier runs the control-plane validator, diff hygiene, profile-parser tests,
dependency-free contracts, full fake server suite, Unity EditMode, official Khronos
plus independent glTF import, and optional real CUDA/Android gates. Its report and
logs are written beneath `$QIS_BUILD_ROOT/Verification`; generated evidence is not
committed.

For physical performance evidence, start scanning on the device and run:

```bash
Tools/unity/profile_tsdf_on_quest.sh 30
```

The capture must include both `QIS_WORLD_PROFILE` and `QIS_TSDF_PROFILE`. This joins
frame/integration timings to chunk count and residency rather than reporting one
uncontextualized memory snapshot.

## Failure recovery

- **Unity import/build interrupted:** close Unity/Hub, retain source, and delete only
  this generated host project's `Library`, `Temp`, or failed build output when a
  rebuild actually requires it.
- **Server interrupted:** restart `Tools/server/run_server.sh`. SQLite WAL recovers an
  orphaned running job as queued; terminal jobs stay immutable.
- **Wi-Fi unavailable:** do nothing destructive. The Quest queue remains durable and
  scanning continues; reconnect and let it retry.
- **Partial chunk write:** the transaction staging directory is never allowed to
  replace the last-known-good manifest. Preserve the world and logs if recovery does
  not select the backup.
- **Bad DiffSoup artifact:** retain the prior renderer and capture job/artifact hashes;
  never bypass validation to make a test appear successful.
- **Low disk space:** remove only this repository's known regenerable build/cache
  outputs. Do not clean unrelated projects, user backups, or Codex state.

## Change discipline

Add a `.meta` file for every Unity-visible asset. Keep network/file work outside
depth integration. Keep persisted formats versioned and bounded. Run targeted tests
first, then `Tools/verify_all.sh`, then Android/device gates proportional to the
change. A hardware result is evidence only when it was actually captured.
