#!/usr/bin/env bash
# Installs the FinalScan APK on the single authorized Quest, grants runtime permissions, wakes and launches.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "${SCRIPT_DIR}/../dev_environment.sh"
APK="${1:-${FS_APK_PATH}}"
test -x "${FS_ADB}"
test -s "${APK}"
ADB=("${FS_ADB}" -s "${FS_DEVICE_SERIAL}")

STATE="$("${ADB[@]}" get-state 2>/dev/null || true)"
if [[ "${STATE}" != "device" ]]; then
  echo "DEPLOY BLOCKED: ${FS_DEVICE_SERIAL} state='${STATE:-absent}' (authorize the Quest and retry)" >&2
  "${FS_ADB}" devices >&2
  exit 2
fi

"${ADB[@]}" install -r -d "${APK}"
for permission in android.permission.CAMERA horizonos.permission.HEADSET_CAMERA com.oculus.permission.USE_SCENE; do
  if "${ADB[@]}" shell pm grant "${FS_APP_ID}" "${permission}" >/dev/null 2>&1; then
    echo "granted ${permission}"
  else
    echo "WARNING: pm grant ${permission} refused (runtime dialog will ask)" >&2
  fi
done

# Wake the headset; a sleeping or dialog-blocked device silently drops am start.
"${ADB[@]}" shell input keyevent KEYCODE_WAKEUP >/dev/null 2>&1 || true
# Quest keeps the app paused unless it believes it is worn: disable the proximity automation and
# fake a proximity-close so headless runs render (donor evidence: runs blocked by mWakefulness=Asleep).
"${ADB[@]}" shell am broadcast -a com.oculus.vrpowermanager.automation_disable >/dev/null 2>&1 || true
"${ADB[@]}" shell am broadcast -a com.oculus.vrpowermanager.prox_close >/dev/null 2>&1 || true
sleep 1

# Launch activity from the APK itself (aapt badging), never from an assumption.
BUILD_TOOLS="$(find "${FS_ANDROID_SDK_ROOT}/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -n 1)"
ACTIVITY=""
if [[ -x "${BUILD_TOOLS}/aapt" ]]; then
  ACTIVITY="$("${BUILD_TOOLS}/aapt" dump badging "${APK}" 2>/dev/null \
    | sed -n "s/^launchable-activity: name='\([^']*\)'.*/\1/p" | head -n 1 || true)"
fi
"${ADB[@]}" shell am force-stop "${FS_APP_ID}" >/dev/null 2>&1 || true
if [[ -n "${ACTIVITY}" ]]; then
  echo "launching ${FS_APP_ID}/${ACTIVITY}"
  "${ADB[@]}" shell am start -n "${FS_APP_ID}/${ACTIVITY}" -a android.intent.action.MAIN -c android.intent.category.LAUNCHER
else
  echo "WARNING: launchable activity not found via aapt; using monkey launcher" >&2
  "${ADB[@]}" shell monkey -p "${FS_APP_ID}" -c android.intent.category.LAUNCHER 1 >/dev/null
fi

PID=""
for _ in $(seq 1 20); do
  PID="$("${ADB[@]}" shell pidof "${FS_APP_ID}" 2>/dev/null | tr -d '\r' || true)"
  [[ -n "${PID}" ]] && break
  sleep 0.5
done
if [[ -z "${PID}" ]]; then
  echo "LAUNCH FAILED: ${FS_APP_ID} has no process after 10 s" >&2; exit 3
fi
printf 'DEPLOYED: %s -> %s\nACTIVITY: %s\nPID: %s\n' "${APK}" "${FS_DEVICE_SERIAL}" "${ACTIVITY:-(monkey)}" "${PID}"
