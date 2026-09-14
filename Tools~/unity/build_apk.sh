#!/usr/bin/env bash
# FinalScan release APK: native plugin -> Unity Prepare -> Unity Build -> zipalign(16K) -> apksigner.
# Two-phase batch build ported from the donor (MIGRATION_LEDGER §1), bounded by a systemd scope.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
if [[ "${FS_UNITY_BUILD_SCOPE:-0}" != 1 ]]; then
  exec systemd-run --user --scope --quiet \
    -p MemoryHigh=12G -p MemoryMax=16G -p MemorySwapMax=2G \
    taskset --cpu-list "${FS_UNITY_BUILD_CPUS:-0-1}" \
    env FS_UNITY_BUILD_SCOPE=1 bash "${SCRIPT_DIR}/build_apk.sh" "$@"
fi
source "${SCRIPT_DIR}/../dev_environment.sh"
"${SCRIPT_DIR}/verify_install.sh" >/dev/null
ulimit -n 65536
export GRADLE_USER_HOME="${FS_GRADLE_USER_HOME}"
export ANDROID_SDK_ROOT="${FS_ANDROID_SDK_ROOT}"
export ANDROID_HOME="${FS_ANDROID_SDK_ROOT}"
mkdir -p "${FS_BUILD_DIR}" "${GRADLE_USER_HOME}"
PREPARE_LOG="${FS_BUILD_DIR}/prepare.log"
BUILD_LOG="${FS_BUILD_DIR}/build.log"

# Release signing identity: keystore path from the environment, passwords from the keys dir only.
KEYS_ENV="${FS_KEYS_DIR}/finalscan-release.env"
test -f "${FS_RELEASE_KEYSTORE}" || { echo "missing release keystore ${FS_RELEASE_KEYSTORE}" >&2; exit 1; }
test -f "${KEYS_ENV}" || { echo "missing ${KEYS_ENV} (FS_RELEASE_STORE_PASSWORD, FS_RELEASE_KEY_PASSWORD)" >&2; exit 1; }
set -a; source "${KEYS_ENV}"; set +a
: "${FS_RELEASE_STORE_PASSWORD:?${KEYS_ENV} must define FS_RELEASE_STORE_PASSWORD}"
: "${FS_RELEASE_KEY_PASSWORD:?${KEYS_ENV} must define FS_RELEASE_KEY_PASSWORD}"

# Native plugin (libFinalScanNative.so into the host Assets/Plugins/Android). Owned by Tools~/native.
NATIVE_BUILD="${SCRIPT_DIR}/../native/build_native.sh"
if [[ -f "${NATIVE_BUILD}" ]]; then
  bash "${NATIVE_BUILD}"
else
  echo "WARNING: ${NATIVE_BUILD} not found; building without libFinalScanNative.so" >&2
fi

check_log() {
  local log="$1" marker="$2"
  grep -Fq "${marker}" "${log}" || { echo "marker '${marker}' missing; inspect ${log}" >&2; return 1; }
  if grep -Eq 'error CS[0-9]+|Shader error in' "${log}"; then
    echo "compilation errors; inspect ${log}" >&2; return 1
  fi
}

PREVIOUS_MTIME=0
[[ -f "${FS_APK_PATH}" ]] && PREVIOUS_MTIME="$(stat -c %Y "${FS_APK_PATH}")"

"${FS_UNITY_EXECUTABLE}" -batchmode -nographics -job-worker-count 2 -buildTarget Android \
  -projectPath "${FS_HOST_PROJECT}" \
  -executeMethod FinalScan.Editor.FinalScanHostSetup.PrepareHostProject \
  -logFile "${PREPARE_LOG}"
check_log "${PREPARE_LOG}" '[FinalScan] Prepare Succeeded:'

FS_APK_PATH="${FS_APK_PATH}" "${FS_UNITY_EXECUTABLE}" -batchmode -nographics -job-worker-count 2 -buildTarget Android \
  -projectPath "${FS_HOST_PROJECT}" \
  -executeMethod FinalScan.Editor.FinalScanHostSetup.BuildApk -quit \
  -logFile "${BUILD_LOG}"
check_log "${BUILD_LOG}" '[FinalScan] APK build Succeeded:'
test -s "${FS_APK_PATH}"
CURRENT_MTIME="$(stat -c %Y "${FS_APK_PATH}")"
if (( CURRENT_MTIME <= PREVIOUS_MTIME )); then
  echo "APK is stale: mtime ${CURRENT_MTIME} did not exceed ${PREVIOUS_MTIME}" >&2; exit 1
fi

BUILD_TOOLS="$(find "${FS_ANDROID_SDK_ROOT}/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -n 1)"
test -x "${BUILD_TOOLS}/zipalign" && test -x "${BUILD_TOOLS}/apksigner"
"${BUILD_TOOLS}/zipalign" -P 16 -f 4 "${FS_APK_PATH}" "${FS_APK_PATH}.aligned"
"${BUILD_TOOLS}/apksigner" sign --ks "${FS_RELEASE_KEYSTORE}" --ks-key-alias "${FS_RELEASE_KEY_ALIAS}" \
  --ks-pass env:FS_RELEASE_STORE_PASSWORD --key-pass env:FS_RELEASE_KEY_PASSWORD \
  --out "${FS_APK_PATH}.signed" "${FS_APK_PATH}.aligned"
rm -f "${FS_APK_PATH}.aligned"
mv -- "${FS_APK_PATH}.signed" "${FS_APK_PATH}"
"${BUILD_TOOLS}/apksigner" verify "${FS_APK_PATH}"
"${BUILD_TOOLS}/zipalign" -c -P 16 4 "${FS_APK_PATH}"
printf 'APK: %s\nSHA256: %s\nPREPARE LOG: %s\nBUILD LOG: %s\n' \
  "${FS_APK_PATH}" "$(sha256sum "${FS_APK_PATH}" | cut -d' ' -f1)" "${PREPARE_LOG}" "${BUILD_LOG}"
