#!/usr/bin/env bash
set -euo pipefail

qis_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/storage/dev_environment.sh
source "$qis_script_dir/../storage/dev_environment.sh"

qis_apk="${QIS_APK_PATH:-$QIS_BUILD_ROOT/QuestInfiniteScan/QuestInfiniteScan-release.apk}"
if [[ ! -s "$qis_apk" ]]; then
    printf 'APK is missing or empty: %s\n' "$qis_apk" >&2
    exit 1
fi

mapfile -t qis_devices < <(adb devices | awk 'NR > 1 && $2 == "device" {print $1}')
if [[ ${#qis_devices[@]} -ne 1 ]]; then
    printf 'Expected exactly one authorized ADB device; found %d.\n' \
        "${#qis_devices[@]}" >&2
    exit 1
fi

adb -s "${qis_devices[0]}" install -r -d "$qis_apk"
printf 'Deployed %s to %s\n' "$qis_apk" "${qis_devices[0]}"
