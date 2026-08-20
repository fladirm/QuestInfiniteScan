# Release checklist

A commit is a development checkpoint until every physical item below has retained
evidence. Do not convert an automated skip into a pass.

## Source and host gates

- [ ] `python3 Tools/validate_goal_state.py`
- [ ] `git diff --check`
- [ ] `QIS_VERIFY_CUDA=1 QIS_VERIFY_ANDROID=1 Tools/verify_all.sh`
- [ ] verification report records zero failed steps and the intended Git commit
- [ ] Khronos validator reports zero errors for chunk and world fixtures
- [ ] package version, README status, protocol version, locks, licenses, and notices
      match the commit
- [ ] no credentials, LAN addresses, device IDs, keyframes, captures, model artifacts,
      server databases, or build/cache output are staged

## Quest 3/3S gate

- [ ] record headset model, Horizon OS build, Unity version, APK SHA-256, scene/preset,
      volume dimensions/voxel size, and package commit
- [ ] Vulkan ARM64 install and cold launch succeed with required permissions
- [ ] passthrough, RGB, depth, tracking, UI, Start/Stop, save, and reload succeed
- [ ] traverse more than six chunks in multiple directions with overlap and hysteresis
- [ ] disconnect notebook before/during scan; mapping and persistence continue
- [ ] revisit evicted chunks and restart app; volume/presentation rehydrate correctly
- [ ] anchor relocation and loop correction update only `T_world_chunk`
- [ ] selected render mode applies consistently to active and cached representations
- [ ] real DiffSoup artifact validates, atomically swaps, renders in both eyes with
      correct depth/culling, and cleans up resources
- [ ] chunk GLB and sharded world export succeed from the device workflow
- [ ] 30+ second profile records chunk growth, median/p95-relevant raw frame data,
      TSDF integration timing, process/GPU memory, and the one-volume bound
- [ ] Stop Scan drains or explicitly reports every pending publication/job

## Recovery/failure matrix

- [ ] offline retry and reconnect
- [ ] duplicate idempotent job and conflicting duplicate rejection
- [ ] server restart during running work
- [ ] app interruption during world/chunk/artifact transaction
- [ ] corrupt/oversized/traversal archive and corrupt DiffSoup artifact rejection
- [ ] stale revision cannot replace a newer renderer/export
- [ ] monolithic GLB limit retains an actionable valid sharded export

Store device evidence outside Git and reference hashes/commands in the release record.
