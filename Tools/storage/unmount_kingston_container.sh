#!/usr/bin/env bash
set -euo pipefail

qis_mount="${QIS_DEV_ROOT:-/mnt/kingston-unity}"
if ! findmnt -rn -M "$qis_mount" >/dev/null 2>&1; then
    printf 'Unity development container is not mounted.\n'
    exit 0
fi

qis_users="$(sudo fuser -m "$qis_mount" 2>/dev/null || true)"
if [[ -n "$qis_users" ]]; then
    printf 'Refusing to unmount: processes still use %s (PIDs:%s)\n' \
        "$qis_mount" "$qis_users" >&2
    exit 1
fi

sudo umount -- "$qis_mount"
printf 'Unmounted %s. The outer KINGSTON volume can now be ejected normally.\n' "$qis_mount"
