# QuestInfiniteScan

QuestInfiniteScan is a development fork of
[QuestRoomScan](https://github.com/arghyasur1991/QuestRoomScan) for scanning spaces
larger than one TSDF volume on Meta Quest 3 and Quest 3S. It keeps the upstream
Unity/Vulkan mapper on the headset, moves through overlapping local chunks with one
reusable GPU volume, persists inactive chunks, optionally refines them with DiffSoup
on a LAN CUDA notebook, and exports standards-compliant GLB/PBR assets.

The active implementation lives on `feat/infinite-submaps-diffsoup`. It is a
feature-complete development checkpoint, not a production release. Automated host,
CUDA, Android, and format validation are present. The remaining release gate is a
physical multi-chunk Quest acceptance pass; the known revisit/finalization problem is
listed in [Known issues](docs/KNOWN_ISSUES.md) and is intentionally not hidden behind
a “done” badge.

## Architecture

```text
Quest 3 / 3S                               CUDA notebook (optional)

RGB + depth + tracking
          │
          ▼
existing QRS Vulkan mapper
TSDF → Surface Nets → live mesh
          │
          ▼
SubmapManager ── overlap / hysteresis ──► next local chunk
     │                                    one reusable TSDF
     ├── atomic world/chunk persistence
     ├── bounded nearby mesh cache
     ├── pose graph + bounded overlap ICP
     ├── per-chunk keyframes                  Wi-Fi, asynchronous
     └── local atlas / GLB export ───────────────┐
                                                 ▼
                                    durable protocol-v2 job service
                                    pinned DiffSoup Python/CUDA worker
                                                 │
                                                 ▼
                                    validated triangle/LUT/MLP artifact
                                                 │
                                                 ▼
Quest URP DiffSoup renderer ◄── atomic promotion ┘
```

Notebook or Wi-Fi loss never belongs to the real-time integration path. A returned
artifact is hash-, schema-, geometry-, texture-, and network-validated before it can
replace a coarse representation.

## What this fork adds

- Versioned `WorldManifest`, chunks, revisions, artifacts, neighbor constraints, and
  `T_world_chunk` pose-graph transforms.
- Three-axis chunk rollover with overlap and Schmitt hysteresis while keeping exactly
  one resident TSDF volume.
- Atomic volume/live-mesh/keyframe/refined-artifact persistence and bounded nearby
  coarse/DiffSoup renderer caches.
- Distance-, confidence-, incidence-, normal-, and visibility-aware TSDF arbitration
  to reduce far-view erosion and updates through an already observed surface.
- Deterministic point-to-plane overlap ICP and robust graph optimization that changes
  chunk transforms, never local geometry.
- Durable offline-first LAN queue and a clean FastAPI/SQLite service around the pinned
  upstream DiffSoup CUDA worker, including exact-compatible revision warm starts.
- A bounded Unity URP renderer for the DiffSoup triangle/LUT/SH2/MLP artifact.
- Honest PBR GLB 2.0 export for a chunk, sharded building export, and optional bounded
  monolithic world export.
- Quest operator diagnostics, reproducible build/deploy scripts, hostile-input tests,
  independent glTF validation, and chunk-count/memory/frame-time profiling.

The upstream single-room path and optional legacy modules remain available. Large
world behavior is enabled by the `SubmapManager`; it is not a rewrite of the mapper.

## Tested target stack

| Layer | Pinned/tested target |
|---|---|
| Headset | Meta Quest 3 / Quest 3S, Horizon OS, depth API |
| Unity | `6000.5.9f1` |
| Unity modules | Android Build Support, SDK/NDK Tools, OpenJDK from Unity Hub |
| Android | ARM64, IL2CPP, Vulkan-only, OpenXR |
| Python | `3.14.4` in isolated environments |
| NVIDIA | RTX 4070 Laptop, CUDA toolkit 13.3 / SM89 |
| PyTorch | `2.13.0+cu130` |
| DiffSoup | commit `c74e35de74ad0116977b23e7951f4cbc25ab0f6b` |

Unity package dependencies are pinned in [package.json](package.json). The server
lock is [Server/uv.lock](Server/uv.lock), and the CUDA worker lock is
[Server/diffsoup-worker.lock.json](Server/diffsoup-worker.lock.json).

## Workstation setup

Install Unity Hub yourself, then install Unity `6000.5.9f1` with Android Build
Support, Android SDK & NDK Tools, and OpenJDK. Do not mix in a random system Android
NDK. The remaining host tools are `git`, `git-lfs`, `adb`, `uv`, Node/npm, a C/C++
toolchain, an NVIDIA driver, and CUDA `nvcc` when the real DiffSoup backend is used.

This checkout includes helpers for the external ext4 development container used by
the project. The default is `/mnt/kingston-unity`; set `QIS_DEV_ROOT` only to another
ext4 mount with equivalent free space.

```bash
Tools/storage/mount_kingston_container.sh
source Tools/storage/dev_environment.sh
Tools/unity/verify_unity_install.sh
Tools/unity/create_host_project.sh
```

The source checkout remains in Git. Unity editors, generated projects, caches,
builds, server state, and DiffSoup data live on ext4. Never place SQLite/Unity build
state directly on exFAT, and never use the cleanup workflow on Codex session data.
See [Developer guide](docs/DEVELOPMENT.md) for a fresh-machine and recovery runbook.

## Unity client setup

Open the generated `QuestInfiniteScanHost` project and choose
`RoomScan > Setup Scene`. Apply **Game-Ready Setup** until the bootstrap audit is
green, then add **Debug Tools** for the in-headset operator UI. The setup is
idempotent:

- a newly added `SubmapManager` receives the documented large-world defaults;
- an existing component keeps operator-tuned boundary/overlap/hysteresis values;
- the single-room configuration remains possible by omitting/disabling large-world
  mode;
- triplanar is disabled only in large-world mode to avoid its large persistent GPU
  cache;
- the GLB export controller and offline-safe refinement scheduler are separate
  modules.

The tested large-world baseline is a 1 m boundary margin, 2 m nominal overlap,
0.75 m rearm hysteresis, one resident TSDF, and at most three chunk mesh
representations including the active presentation. These are safety defaults, not a
claim of finished tuning.

Build and deploy:

```bash
Tools/unity/build_smoke_apk.sh
adb devices
Tools/unity/deploy_smoke_apk.sh
```

The Quest must have Developer Mode and USB debugging enabled. The manifest generated
by the wizard includes the current Meta VR SDK tag and headset camera, scene, anchor,
camera, and network permissions. Quest 3S uses the same client/API path as Quest 3.

## Local DiffSoup server

The scanner works without this server. To provision the pinned worker on the tested
CUDA notebook and start the durable LAN service:

```bash
Tools/server/bootstrap_diffsoup.sh
Tools/server/run_cuda_tests.sh
Tools/server/run_server.sh
curl http://127.0.0.1:8420/v2/capabilities
```

Set the server URL on `ChunkRefinementScheduler` to the notebook's reachable LAN URL.
Do not commit a machine-specific address. Jobs are keyed by
`(worldId, chunkId, chunkRevision)`, survive restarts, validate bounded streaming
uploads, and cannot let an older revision overwrite a newer chunk. Full wire and
artifact details are in [protocol v2](Server/contracts/v2/PROTOCOL.md) and the
[server guide](Server/README.md).

## Operator workflow and UI meanings

1. Start a scan and move steadily with useful RGB light and surface texture.
2. The active local volume integrates depth. Near a boundary the next chunk is
   selected using overlap and hysteresis; finalized data continues to persist in the
   background.
3. Stop scanning before explicit refinement or GLB export of the active revision.
4. Use **Infinite World** to inspect chunk lifecycle, resident representations,
   graph, durable DiffSoup queue, network mode, artifact counts/storage, and export
   status.
5. Export a chunk immediately, or export a world as `building.json + chunks/*.glb`.
   Request `world.glb` only when the configured monolithic size bound permits it.

The similarly named controls do different things:

- **Freeze Tint** only colors already frozen voxels blue. It changes presentation,
  not scan integration.
- **Freeze In View** locks voxels currently in the view frustum so future depth does
  not change them.
- **Unfreeze In View** allows those voxels to integrate again.
- **Wireframe / Vertex / Refined / DiffSoup / None** select representation only.
  The controller applies the selected mode to the active and cached chunk renderers.

The operator guide explains scan lighting, recovery, exports, and diagnostic capture:
[docs/OPERATOR_GUIDE.md](docs/OPERATOR_GUIDE.md).

## Persistence and export

```text
InfiniteWorlds/<world-id>/
  world.json                         schema-v1 graph and chunk manifest
  chunks/<chunk-id>/
    revisions/<revision>/            immutable volume/mesh/keyframes/refined data
    enhancements/<revision>/         validated DiffSoup and GLB artifacts

Exports/<timestamp>/
  building.json                      versioned world/shard manifest
  chunks/<content-addressed>.glb
  world.glb                          optional, bounded
```

Chunk GLB contains positions, normals, generated tangents, UV0, uint32 indices,
embedded base-color and normal PNGs, `metallicFactor=0`, and configurable constant
roughness/normal scale. It does not invent measured roughness, metallic, or occlusion
maps. World export applies the current pose-graph transform exactly once per named
chunk node. Uncompressed GLB is mandatory; meshopt/KTX2 are selected only when a real
verified encoder and declared consumer support are both available.

The fixtures pass the official Khronos glTF Validator with zero errors/warnings and
an independent glTF Transform `NodeIO` import. Blender is not required to write GLB.

## Validation

Run all host gates and write a machine-readable report outside the repository:

```bash
Tools/verify_all.sh

# Include the real pinned CUDA worker and Android/Vulkan IL2CPP build:
QIS_VERIFY_CUDA=1 QIS_VERIFY_ANDROID=1 Tools/verify_all.sh
```

Individual gates remain available:

```bash
python3 Tools/validate_goal_state.py
Tools/server/run_contract_tests.sh
Tools/server/run_tests.sh
Tools/server/run_cuda_tests.sh
Tools/unity/run_editmode_tests.sh
Tools/gltf/verify_interoperability.sh
Tools/unity/build_smoke_apk.sh
```

During a real multi-chunk scan, capture correlated chunk count, Unity allocated and
reserved memory, resident representations, TSDF bytes, CPU integration time, CPU/GPU
frame time, process PSS, and GPU profiler output:

```bash
Tools/unity/profile_tsdf_on_quest.sh 30
```

The analyzer fails if either world-residency or TSDF/frame telemetry is missing and
emits `performance-summary.json`, `world-profile.csv`, and `tsdf-profile.csv` next to
the raw ADB evidence.

## Resolution and current limits

The default 256³ mapper uses 5 cm voxels: 32 MiB TSDF/confidence plus 64 MiB
color/best-quality. The Quest's reported 128 MiB `maxStorageBufferRange` is a
per-storage-buffer limit, not an app-, APK-, total-RAM-, or texture-allocation limit.
The app may use shared headset RAM, but every allocation and the active-set policy
must still be bounded and profiled.

Do not interpret an approximately 8 mm texture-cache/atlas sampling figure as 8 mm
geometric accuracy. Geometry quality is constrained by the depth stream, voxel size,
view incidence, calibration, motion, lighting, and fusion policy. Multi-floor motion
is represented by three-axis chunks and pose-graph transforms, but it is not declared
accepted until the physical device matrix passes.

See [Known issues](docs/KNOWN_ISSUES.md) before field testing.

## Upstream and license

This fork preserves and extends QuestRoomScan rather than replacing its mapper. The
audited upstream commit and extension points are recorded in
[docs/architecture/UPSTREAM_AUDIT.md](docs/architecture/UPSTREAM_AUDIT.md). Original
algorithm documentation remains in [ALGORITHM.md](ALGORITHM.md).

QuestInfiniteScan is MIT licensed; original copyrights remain intact. DiffSoup and
all validation/runtime dependencies retain their own licenses. See
[LICENSE.md](LICENSE.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
