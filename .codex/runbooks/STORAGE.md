# KINGSTON development storage

The source checkout stays at `/mnt/aidisk/prace/otherscan`. Regeneratable and
large development state lives in the 250 GiB ext4 image
`KINGSTON/.unity-linux-ext4-250g.img`, mounted at `/mnt/kingston-unity`.

Never put Codex state in this image and never move, delete, or compress anything
under `~/.codex`. User backups belong in the outer exFAT directory
`KINGSTON/zaloha_ubuntu_codex`, not in the development image.

## Mount and environment

```bash
Tools/storage/mount_kingston_container.sh
source Tools/storage/dev_environment.sh
```

The environment routes Unity projects/builds, temp data, Gradle, Python/uv,
PyTorch extensions, CUDA cache, NuGet, DiffSoup work, and server jobs to ext4.

Start the graphical Hub with the same cache/temp routing:

```bash
Tools/storage/start_unity_hub.sh
```

Hub's editor install path must be `/mnt/kingston-unity/Unity/Hub/Editor`; its
custom download location must be `/mnt/kingston-unity/Unity/Caches/hub-downloads`.
Do not begin an install while Hub reports the system disk's free-space value.

## Safe removal

Close Unity, Hub, Gradle, DiffSoup workers, and the local server, then run:

```bash
Tools/storage/unmount_kingston_container.sh
```

Only after the inner ext4 mount is gone may the outer KINGSTON disk be ejected.
The helper refuses to unmount while any process still uses the inner filesystem.

The kernel reported that the outer exFAT volume had previously been removed
without a clean unmount. Before trusting it with editor/model data, perform a
one-time unmounted `fsck.exfat -n /dev/sda1`; do not auto-repair user data without
reviewing the report.
