#!/usr/bin/env bash
set -euo pipefail

qis_login_name="$(id -un)"
qis_kingston_root="${QIS_KINGSTON_ROOT:-/run/media/$qis_login_name/KINGSTON}"
qis_image="${QIS_UNITY_IMAGE:-$qis_kingston_root/.unity-linux-ext4-250g.img}"
qis_mount="${QIS_DEV_ROOT:-/mnt/kingston-unity}"
qis_minimum_bytes=$((200 * 1024 * 1024 * 1024))

qis_mount_message="Mounted $qis_image at $qis_mount"
if findmnt -rn -M "$qis_mount" >/dev/null 2>&1; then
    qis_fstype="$(findmnt -rn -o FSTYPE -M "$qis_mount")"
    qis_label="$(findmnt -rn -o LABEL -M "$qis_mount")"
    if [[ "$qis_fstype" != "ext4" || "$qis_label" != "UNITY_KINGSTON" ]]; then
        printf 'Refusing unexpected filesystem at %s: %s\n' "$qis_mount" "$qis_fstype" >&2
        exit 1
    fi
    qis_mount_message="Unity development container already mounted at $qis_mount"
else
    if [[ "$(findmnt -rn -o FSTYPE -M "$qis_kingston_root" 2>/dev/null || true)" != "exfat" ]]; then
        printf 'KINGSTON exFAT volume is not mounted at %s\n' "$qis_kingston_root" >&2
        exit 1
    fi
    if [[ ! -f "$qis_image" ]]; then
        printf 'Container image not found: %s\n' "$qis_image" >&2
        exit 1
    fi
    qis_image_bytes="$(stat -c '%s' -- "$qis_image")"
    if (( qis_image_bytes < qis_minimum_bytes )); then
        printf 'Container is smaller than 200 GiB: %s bytes\n' "$qis_image_bytes" >&2
        exit 1
    fi

    sudo mkdir -p -- "$qis_mount"
    sudo mount -o loop,noatime -- "$qis_image" "$qis_mount"
    if [[ "$(findmnt -rn -o FSTYPE -M "$qis_mount")" != "ext4" ||
          "$(findmnt -rn -o LABEL -M "$qis_mount")" != "UNITY_KINGSTON" ]]; then
        sudo umount -- "$qis_mount" || true
        printf 'Mounted container did not identify as expected UNITY_KINGSTON ext4\n' >&2
        exit 1
    fi
fi

qis_uid="$(id -u)"
qis_gid="$(id -g)"
sudo install -d -o "$qis_uid" -g "$qis_gid" -- \
    "$qis_mount/Unity/Hub/Editor" \
    "$qis_mount/Unity/Projects" \
    "$qis_mount/Unity/Caches/hub-downloads" \
    "$qis_mount/Caches" \
    "$qis_mount/Builds"

printf '%s\n' "$qis_mount_message"
df -h -- "$qis_mount"
