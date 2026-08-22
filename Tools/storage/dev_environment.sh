#!/usr/bin/env bash

# Source this file before Unity or Android build commands.
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    printf 'Source this file instead of executing it: source %s\n' "$0" >&2
    exit 2
fi

qis_dev_root="${QIS_DEV_ROOT:-/mnt/kingston-unity}"
if [[ "$(findmnt -rn -o FSTYPE -M "$qis_dev_root" 2>/dev/null || true)" != "ext4" ]]; then
    printf 'QuestInfiniteScan development container is not mounted at %s\n' \
        "$qis_dev_root" >&2
    return 1
fi

export QIS_DEV_ROOT="$qis_dev_root"
export QIS_UNITY_EDITOR_ROOT="$qis_dev_root/Unity/Hub/Editor"
export QIS_UNITY_PROJECT_ROOT="$qis_dev_root/Unity/Projects"
export QIS_BUILD_ROOT="$qis_dev_root/Builds"
export TMPDIR="$qis_dev_root/Caches/tmp"
export XDG_CACHE_HOME="$qis_dev_root/Caches/xdg"
export GRADLE_USER_HOME="$qis_dev_root/Caches/gradle"
export NUGET_PACKAGES="$qis_dev_root/Caches/nuget"

mkdir -p -- \
    "$QIS_UNITY_EDITOR_ROOT" "$QIS_UNITY_PROJECT_ROOT" "$QIS_BUILD_ROOT" \
    "$TMPDIR" "$XDG_CACHE_HOME" "$GRADLE_USER_HOME" "$NUGET_PACKAGES"
