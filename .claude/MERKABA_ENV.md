# Verified environment ledger (Claude)

Recorded 2026-09-05 (Europe/Prague) by direct probe.

**Correction:** an earlier revision of this file claimed `/mnt/kingston-unity` was
unmounted. That was wrong — it was read once through a sandboxed shell that showed the
mountpoint before/without the loop mount. Always confirm with `findmnt`/`lsblk`, never a
single `ls`.

## Storage

```text
/mnt/kingston-unity   /dev/loop17  ext4  250G  LABEL=UNITY_KINGSTON   MOUNTED
/mnt/aidisk           /dev/nvme0n1p5 ext4 384G  97% full (15 GiB free)
/run/media/wraith/KINGSTON  /dev/sdb1 exfat 1.8T  personal drive, unrelated
```

## Repository

```text
TARGET_ROOT   /mnt/aidisk/prace/simplescan
REMOTE        git@github.com:fladirm/QuestInfiniteScan.git
BRANCH        fix/merkaba-runtime-root-causes  (in sync with origin)
HEAD          863e75dae4ae4667d3b6ec46bcb7ea8b4d1c7e6e
CONTRACT BASE 1b581635…  REGRESSION REF 62b69064…   (both present locally)
DONOR         /mnt/aidisk/prace/otherscan (read-only material)
```

The tree is **shared with a live Codex session**: during this audit it produced
`863e75d`, wrote `MerkabaDesignDocument.cs` + `MerkabaPaintEngine.cs` (untracked), edited
`Tests/Editor/MerkabaPersistenceTests.cs` at 00:36Z and started a Unity test run at
00:38Z. Re-probe `git status` and `Builds/TestResults` before and after every action.

## Unity host — PRESENT AND WORKING

```text
UNITY         /mnt/kingston-unity/Unity/Hub/Editor/6000.5.9f1/Editor/Unity
HOST          /mnt/kingston-unity/Unity/Projects/QuestMerkabaScanHost   (6000.5.9f1)
PACKAGE LINK  <HOST>/Packages/com.genesis.roomscan -> /mnt/aidisk/prace/simplescan
SCENE         Assets/Scenes/QuestMerkabaScan.unity
              [BuildingBlock] Camera Rig + Passthrough, EventSystem, Merkaba Menu,
              [Quest Infinite Merkaba], [Merkaba Room Space], MerkabaGrid
PANEL         Assets/Settings/MerkabaPanelSettings.asset (UI Toolkit, world space)
NATIVE        Assets/Plugins/Android/libMerkabaVulkanTimestamps.so (built from
              Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp by NDK clang)
BUILD ROOT    /mnt/kingston-unity/Builds
OTHER HOSTS   QuestInfiniteScanHost (donor), QuestMerkabaR2BisectHost,
              QuestMerkabaR5BisectHost, M16InfiniteScannerCache
```

All `Tools/unity/*.sh`, `Tools/gltf/*.sh` and `Tools/shaders/*.sh` are runnable.
`glslangValidator 11:16.2.0`, `spirv-dis`, `spirv-val`, `node v24.16.0`, `python3`,
system `adb 1.0.41` are present; Unity ships its own SDK/NDK/ADB under the editor.

Disk headroom: a full Library rebuild lands on the 250 G Kingston volume, not on
`/mnt/aidisk`, so the 97 % figure above does not block builds.

## Last known gate results (read from Builds, not re-run by me)

```text
Tools/shaders/audit_merkaba_compute_spirv.sh   RE-RAN HERE: PASS, 57 kernels,
                                               writable storage <= 8, no RW/read alias
Builds/TestResults/merkaba-results.xml         2026-09-05 00:34Z  280 total 279 passed
                                               1 FAILED (see below); a newer run started
                                               00:38Z and was still in flight
Builds/TestResults/readout-cut.xml             2026-09-04 03:29Z  60/60 PASS
Builds/TestResults/overlap-shell-tests.xml     2026-09-03 22:10Z  25/25 PASS
Builds/TestResults/export-membrane-results.xml 2026-09-03 18:11Z   9/9  PASS
Builds/TestResults/editmode-results.xml        2026-09-02 12:29Z 170/170 PASS
APK  Builds/QuestMerkabaScan/QuestMerkabaScan-release.apk
     2026-09-03 16:02, 58.9 MB, 323 entries, arm64-v8a only,
     libil2cpp.so 106.7 MB, libunity.so 24.3 MB, libOVRPlugin.so 7.0 MB,
     libmrutilitykitshared.so 5.2 MB, libUnityOpenXR.so 1.4 MB
```

The single failing case is
`MerkabaPersistenceTests.AnchoredResumeFailsClosedBeforeRegisteringTheM8World`
(`anchored` was `-1`). It is a **source-text assertion**: it `File.ReadAllText`s
`MerkabaPersistence.cs` and asserts exact literals including indentation and newlines.
Codex edited that test 2 minutes after the run, so the failure is a transient of its
live edit loop, not a proven runtime regression. Thirteen of seventeen test files use
`ReadAllText` source/shader-text assertions (`MerkabaUxTests` 14 occurrences,
`MerkabaPersistenceTests` and `MerkabaTilesetWriterTests` 5 each). Treat that family as
a refactor tripwire, not as behavioural coverage.

## Device evidence on disk (Quest 3S 340YC20G7X0QZ4)

`Builds/DeviceEvidence/` and `Builds/QuestMerkabaScan/evidence/` hold real logcat runs
through 2026-09-02, plus `.m8log` session captures and screenshots. Measured numbers
are quoted in `MERKABA_GEOMETRY_REVIEW.md`. Newest device run is **2026-09-02**, i.e.
before the membrane cut `2e5c071` and everything after it: the current membrane readout
has **no device evidence yet**.

## Update 2026-09-15

`QuestMerkabaScanHost/Packages/com.genesis.roomscan` now links to
`/mnt/aidisk/prace/uniscan`, NOT simplescan. Its build dir holds uniscan APKs.
Dedicated clean host for this tree: `/mnt/kingston-unity/Unity/Projects/SimpleScanHost`
(-> simplescan), build dir `/mnt/kingston-unity/Builds/SimpleScan`. Build with
`QIS_UNITY_HOST_PROJECT=<that host> QIS_MERKABA_BUILD_DIR=<that dir> Tools/unity/build_merkaba_apk.sh`.
`/mnt/aidisk` is 99% full (4.8 G free).
