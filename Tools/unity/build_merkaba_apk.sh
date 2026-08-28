#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "${SCRIPT_DIR}/../storage/dev_environment.sh"
ulimit -n 65536
"${SCRIPT_DIR}/verify_unity_install.sh" >/dev/null
export GRADLE_USER_HOME="${QIS_GRADLE_USER_HOME}"
export ANDROID_SDK_ROOT="${QIS_ANDROID_SDK_ROOT}"
export ANDROID_HOME="${QIS_ANDROID_SDK_ROOT}"

mkdir -p "${QIS_MERKABA_BUILD_DIR}" "${GRADLE_USER_HOME}"
"${SCRIPT_DIR}/build_merkaba_vulkan_timestamps.sh" \
  "${QIS_UNITY_HOST_PROJECT}"
PREPARE_LOG="${QIS_MERKABA_BUILD_DIR}/prepare.log"
BUILD_LOG="${QIS_MERKABA_BUILD_DIR}/build.log"
PREVIOUS_MTIME=0
if [[ -f "${QIS_MERKABA_APK_PATH}" ]]; then
  PREVIOUS_MTIME="$(stat -c %Y "${QIS_MERKABA_APK_PATH}")"
fi

"${QIS_UNITY_EXECUTABLE}" \
  -batchmode \
  -nographics \
  -buildTarget Android \
  -projectPath "${QIS_UNITY_HOST_PROJECT}" \
  -executeMethod Genesis.RoomScan.Editor.RoomScanSetupWizard.PrepareQuestMerkabaScanProject \
  -logFile "${PREPARE_LOG}"

grep -Fq '[QuestMerkabaScan] Prepare Succeeded:' "${PREPARE_LOG}"
if grep -Eq 'error CS[0-9]+|Shader error in' "${PREPARE_LOG}"; then
  echo "Prepare compiled with errors; inspect ${PREPARE_LOG}" >&2
  exit 1
fi

"${QIS_UNITY_EXECUTABLE}" \
  -batchmode \
  -nographics \
  -buildTarget Android \
  -projectPath "${QIS_UNITY_HOST_PROJECT}" \
  -executeMethod Genesis.RoomScan.Editor.RoomScanSetupWizard.BuildQuestMerkabaScanApk \
  -quit \
  -logFile "${BUILD_LOG}"

test -s "${QIS_MERKABA_APK_PATH}"
CURRENT_MTIME="$(stat -c %Y "${QIS_MERKABA_APK_PATH}")"
if (( CURRENT_MTIME <= PREVIOUS_MTIME )); then
  echo "APK is stale: mtime ${CURRENT_MTIME} did not exceed ${PREVIOUS_MTIME}" >&2
  exit 1
fi
grep -Fq '[QuestMerkabaScan] APK build Succeeded:' "${BUILD_LOG}"
if grep -Eq 'error CS[0-9]+|Shader error in' "${BUILD_LOG}"; then
  echo "Build compiled with errors; inspect ${BUILD_LOG}" >&2
  exit 1
fi
printf 'FRESH APK: %s\nPREPARE LOG: %s\nBUILD LOG: %s\n' \
  "${QIS_MERKABA_APK_PATH}" "${PREPARE_LOG}" "${BUILD_LOG}"
