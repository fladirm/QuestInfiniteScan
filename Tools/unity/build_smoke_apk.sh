#!/usr/bin/env bash
set -euo pipefail

qis_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/storage/dev_environment.sh
source "$qis_script_dir/../storage/dev_environment.sh"

qis_unity_version="${QIS_UNITY_VERSION:-6000.5.9f1}"
qis_editor="$QIS_UNITY_EDITOR_ROOT/$qis_unity_version/Editor/Unity"
qis_project="$QIS_UNITY_PROJECT_ROOT/QuestInfiniteScanHost"
qis_log_dir="$QIS_BUILD_ROOT/QuestInfiniteScan/logs"
export QIS_APK_PATH="${QIS_APK_PATH:-$QIS_BUILD_ROOT/QuestInfiniteScan/QuestInfiniteScan-dev.apk}"

if [[ ! -x "$qis_editor" ]]; then
    printf 'Unity editor is missing: %s\n' "$qis_editor" >&2
    exit 1
fi
if [[ ! -f "$qis_project/ProjectSettings/ProjectVersion.txt" ]]; then
    printf 'Unity host project is missing: %s\n' "$qis_project" >&2
    exit 1
fi

install -d -- "$qis_log_dir" "$(dirname -- "$QIS_APK_PATH")"

qis_apk_mtime_before=-1
if [[ -e "$QIS_APK_PATH" ]]; then
    qis_apk_mtime_before="$(stat -c '%Y' -- "$QIS_APK_PATH")"
fi

python3 "$qis_script_dir/validate_prism_compute_uav.py"

"$qis_editor" -batchmode -nographics -buildTarget Android \
    -projectPath "$qis_project" \
    -executeMethod Genesis.RoomScan.Editor.RoomScanSetupWizard.PrepareQuestInfiniteScanSmokeProject \
    -logFile "$qis_log_dir/prepare.log"

"$qis_editor" -batchmode -nographics -buildTarget Android \
    -projectPath "$qis_project" \
    -executeMethod Genesis.RoomScan.Editor.RoomScanSetupWizard.BuildQuestInfiniteScanSmokeApk \
    -logFile "$qis_log_dir/build.log"

test -s "$QIS_APK_PATH"
qis_apk_mtime_after="$(stat -c '%Y' -- "$QIS_APK_PATH")"
if (( qis_apk_mtime_after <= qis_apk_mtime_before )); then
    printf 'Unity did not produce a fresh APK: %s\n' "$QIS_APK_PATH" >&2
    exit 1
fi
if ! grep -Fq '[QuestInfiniteScan] APK build Succeeded:' \
    "$qis_log_dir/build.log"; then
    printf 'Unity success marker is missing from %s\n' \
        "$qis_log_dir/build.log" >&2
    exit 1
fi
printf 'APK ready: %s\n' "$QIS_APK_PATH"
