#!/usr/bin/env bash
set -euo pipefail

qis_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/storage/dev_environment.sh
source "$qis_script_dir/../storage/dev_environment.sh"

qis_unity_version="${QIS_UNITY_VERSION:-6000.5.9f1}"
qis_editor="$QIS_UNITY_EDITOR_ROOT/$qis_unity_version/Editor/Unity"
qis_project="$QIS_UNITY_PROJECT_ROOT/QuestInfiniteScanHost"
qis_results="$QIS_BUILD_ROOT/TestResults/editmode-results.xml"
qis_log="$QIS_BUILD_ROOT/TestResults/editmode.log"

if [[ ! -x "$qis_editor" || ! -f "$qis_project/ProjectSettings/ProjectVersion.txt" ]]; then
    printf 'Unity editor or QuestInfiniteScan host project is missing.\n' >&2
    exit 1
fi

install -d -- "$(dirname -- "$qis_results")"
# The Test Framework owns shutdown. Passing -quit here makes Unity 6.5 exit after
# import/compilation before it writes testResults (while still returning zero).
"$qis_editor" -batchmode -nographics \
    -projectPath "$qis_project" \
    -runTests -testPlatform EditMode \
    -testResults "$qis_results" \
    -logFile "$qis_log"

printf 'EditMode results: %s\nLog: %s\n' "$qis_results" "$qis_log"
