#!/usr/bin/env bash
set -euo pipefail

qis_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/storage/dev_environment.sh
source "$qis_script_dir/../storage/dev_environment.sh"

qis_unity_version="${QIS_UNITY_VERSION:-6000.5.9f1}"
qis_editor="$QIS_UNITY_EDITOR_ROOT/$qis_unity_version/Editor/Unity"
qis_project="$QIS_UNITY_PROJECT_ROOT/QuestInfiniteScanHost"
qis_fixture_dir="$QIS_BUILD_ROOT/GltfFixtures"
qis_log="$QIS_BUILD_ROOT/GltfFixtures/generate.log"
qis_node_root="$QIS_DEV_ROOT/Caches/quest-infinite-scan-gltf"

if [[ ! -x "$qis_editor" || ! -f "$qis_project/ProjectSettings/ProjectVersion.txt" ]]; then
    printf 'Unity editor or QuestInfiniteScan host project is missing.\n' >&2
    exit 1
fi

install -d -- "$qis_fixture_dir" "$qis_node_root"
QIS_GLTF_FIXTURE_DIR="$qis_fixture_dir" \
    "$qis_editor" -batchmode -nographics -quit \
    -projectPath "$qis_project" \
    -executeMethod Genesis.RoomScan.Editor.GlbInteroperabilityFixtureBuilder.Build \
    -logFile "$qis_log"

qis_lock_hash="$(sha256sum "$qis_script_dir/package-lock.json" | cut -d' ' -f1)"
if [[ ! -f "$qis_node_root/.lock-sha256" ]] || \
   [[ "$(<"$qis_node_root/.lock-sha256")" != "$qis_lock_hash" ]]; then
    install -m 0644 -- "$qis_script_dir/package.json" "$qis_node_root/package.json"
    install -m 0644 -- "$qis_script_dir/package-lock.json" "$qis_node_root/package-lock.json"
    (cd -- "$qis_node_root" && npm ci --no-audit --no-fund)
    printf '%s\n' "$qis_lock_hash" > "$qis_node_root/.lock-sha256"
fi
install -m 0644 -- "$qis_script_dir/verify_interoperability.mjs" \
    "$qis_node_root/verify_interoperability.mjs"
node "$qis_node_root/verify_interoperability.mjs" "$qis_fixture_dir"
