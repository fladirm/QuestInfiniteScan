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

"$qis_editor" -batchmode -nographics -buildTarget Android \
    -projectPath "$qis_project" \
    -executeMethod Genesis.RoomScan.Editor.RoomScanSetupWizard.PrepareQuestInfiniteScanSmokeProject \
    -logFile "$qis_log_dir/prepare.log"

"$qis_editor" -batchmode -nographics -buildTarget Android \
    -projectPath "$qis_project" \
    -executeMethod Genesis.RoomScan.Editor.RoomScanSetupWizard.BuildQuestInfiniteScanSmokeApk \
    -logFile "$qis_log_dir/build.log"

test -s "$QIS_APK_PATH"
printf 'APK ready: %s\n' "$QIS_APK_PATH"
