#!/usr/bin/env bash

# Source this file before Unity, DiffSoup, server, or build commands.
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
export QIS_DIFFSOUP_ROOT="$qis_dev_root/DiffSoup"
export QIS_SERVER_DATA_ROOT="$qis_dev_root/Server"

export TMPDIR="$qis_dev_root/Caches/tmp"
export XDG_CACHE_HOME="$qis_dev_root/Caches/xdg"
export GRADLE_USER_HOME="$qis_dev_root/Caches/gradle"
export PIP_CACHE_DIR="$qis_dev_root/Caches/pip"
export UV_CACHE_DIR="$qis_dev_root/Caches/uv"
export TORCH_HOME="$qis_dev_root/Caches/torch"
export TORCH_EXTENSIONS_DIR="$qis_dev_root/Caches/torch-extensions"
export CUDA_CACHE_PATH="$qis_dev_root/Caches/cuda"
export NUGET_PACKAGES="$qis_dev_root/Caches/nuget"

mkdir -p -- \
    "$QIS_UNITY_EDITOR_ROOT" "$QIS_UNITY_PROJECT_ROOT" "$QIS_BUILD_ROOT" \
    "$QIS_DIFFSOUP_ROOT" "$QIS_SERVER_DATA_ROOT" "$TMPDIR" \
    "$XDG_CACHE_HOME" "$GRADLE_USER_HOME" "$PIP_CACHE_DIR" "$UV_CACHE_DIR" \
    "$TORCH_HOME" "$TORCH_EXTENSIONS_DIR" "$CUDA_CACHE_PATH" "$NUGET_PACKAGES"
