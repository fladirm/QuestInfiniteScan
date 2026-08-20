#!/usr/bin/env bash
set -euo pipefail

qis_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/storage/dev_environment.sh
source "$qis_script_dir/../storage/dev_environment.sh"

qis_unity_version="${QIS_UNITY_VERSION:-6000.5.9f1}"
qis_editor="$QIS_UNITY_EDITOR_ROOT/$qis_unity_version/Editor/Unity"
qis_project="$QIS_UNITY_PROJECT_ROOT/QuestInfiniteScanHost"
qis_repo="$(git -C "$qis_script_dir/../.." rev-parse --show-toplevel)"

if [[ ! -x "$qis_editor" ]]; then
    printf 'Unity editor is not installed at %s\n' "$qis_editor" >&2
    exit 1
fi

if [[ ! -f "$qis_project/ProjectSettings/ProjectVersion.txt" ]]; then
    install -d -- "$qis_project"
    "$qis_editor" -batchmode -nographics -quit \
        -createProject "$qis_project" \
        -logFile "$QIS_BUILD_ROOT/unity-create-project.log"
fi

qis_package_link="$qis_project/Packages/com.genesis.roomscan"
if [[ -L "$qis_package_link" ]]; then
    qis_existing_target="$(readlink -f -- "$qis_package_link")"
    if [[ "$qis_existing_target" != "$qis_repo" ]]; then
        printf 'Refusing package link with unexpected target: %s\n' \
            "$qis_existing_target" >&2
        exit 1
    fi
elif [[ -e "$qis_package_link" ]]; then
    printf 'Refusing to replace existing non-symlink package path: %s\n' \
        "$qis_package_link" >&2
    exit 1
else
    ln -s -- "$qis_repo" "$qis_package_link"
fi

printf 'Unity host project ready: %s\nEmbedded package: %s -> %s\n' \
    "$qis_project" "$qis_package_link" "$qis_repo"
