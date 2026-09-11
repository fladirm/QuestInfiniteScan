#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# Keep the whole compiler/Gradle process tree inside a bounded host-only
# scope. A shader compiler failure must not exhaust RAM in the Codex session.
# CPU affinity limits concurrent compiler workers, not the generated GPU WGs.
if [[ "${QIS_UNITY_BUILD_SCOPE:-0}" != 1 ]]; then
  exec systemd-run --user --scope --quiet \
    -p MemoryHigh=12G -p MemoryMax=16G -p MemorySwapMax=2G \
    taskset --cpu-list "${QIS_UNITY_BUILD_CPUS:-0-1}" \
    env QIS_UNITY_BUILD_SCOPE=1 bash "${SCRIPT_DIR}/build_merkaba_apk.sh" "$@"
fi
source "${SCRIPT_DIR}/../storage/dev_environment.sh"
: "${QIS_PIPELINE_MODE:?Choose CAPTURE or BINARY_ONLY; no implicit compiling APK mode}"
case "$QIS_PIPELINE_MODE" in CAPTURE|BINARY_ONLY) ;; *) exit 1 ;; esac
: "${QIS_RELEASE_KEYSTORE:?Provide the production keystore path}"
: "${QIS_RELEASE_KEY_ALIAS:?Provide the production key alias}"
: "${QIS_RELEASE_STORE_PASSWORD:?Provide keystore password through environment}"
: "${QIS_RELEASE_KEY_PASSWORD:?Provide key password through environment}"
test -f "$QIS_RELEASE_KEYSTORE"
export QIS_PIPELINE_COMPILE_ONLY=0
export QIS_PIPELINE_MANIFEST="${QIS_MERKABA_BUILD_DIR}/pipeline-manifest.json"
ulimit -n 65536
"${SCRIPT_DIR}/verify_unity_install.sh" >/dev/null
export GRADLE_USER_HOME="${QIS_GRADLE_USER_HOME}"
export ANDROID_SDK_ROOT="${QIS_ANDROID_SDK_ROOT}"
export ANDROID_HOME="${QIS_ANDROID_SDK_ROOT}"

mkdir -p "${QIS_MERKABA_BUILD_DIR}" "${GRADLE_USER_HOME}"
PREPARE_LOG="${QIS_MERKABA_BUILD_DIR}/prepare.log"
BUILD_LOG="${QIS_MERKABA_BUILD_DIR}/build.log"
PREVIOUS_MTIME=0
if [[ -f "${QIS_MERKABA_APK_PATH}" ]]; then
  PREVIOUS_MTIME="$(stat -c %Y "${QIS_MERKABA_APK_PATH}")"
fi

if [[ "$QIS_PIPELINE_MODE" == CAPTURE ]]; then
"${QIS_UNITY_EXECUTABLE}" \
  -batchmode \
  -nographics \
  -job-worker-count 2 \
  -buildTarget Android \
  -projectPath "${QIS_UNITY_HOST_PROJECT}" \
  -executeMethod Genesis.RoomScan.Editor.RoomScanSetupWizard.PrepareQuestMerkabaScanProject \
  -logFile "${PREPARE_LOG}"

grep -Fq '[QuestMerkabaScan] Prepare Succeeded:' "${PREPARE_LOG}"
if grep -Eq 'error CS[0-9]+|Shader error in' "${PREPARE_LOG}"; then
  echo "Prepare compiled with errors; inspect ${PREPARE_LOG}" >&2
  exit 1
fi
else
  # Rebuilding the scene can change serialized identities/PSO state. Release
  # must reuse the exact prepared capture project, not run a second Prepare.
  test -n "${QIS_PIPELINE_CAPTURE_DIR:-}"
  test -n "${QIS_PIPELINE_REQUIRED_LOG:-}"
  test -n "${QIS_PIPELINE_GRAPHICS_TRACE:-}"
fi

# Fingerprint final prepared settings, not the inherited host before Prepare.
# This compilation emits the bundle; an empty BINARY_ONLY bundle is forbidden.
"${SCRIPT_DIR}/build_merkaba_vulkan_timestamps.sh" "${QIS_UNITY_HOST_PROJECT}"
mkdir -p "${QIS_UNITY_HOST_PROJECT}/Assets/StreamingAssets/MerkabaPipelines"
install -m 0644 "$QIS_PIPELINE_MANIFEST" \
  "${QIS_UNITY_HOST_PROJECT}/Assets/StreamingAssets/MerkabaPipelines/manifest.json"
python3 "$SCRIPT_DIR/generate_merkaba_pipeline_pack.py" --verify-source \
  --unity-project "$QIS_UNITY_HOST_PROJECT" --manifest "$QIS_PIPELINE_MANIFEST"

"${QIS_UNITY_EXECUTABLE}" \
  -batchmode \
  -nographics \
  -job-worker-count 2 \
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
python3 "$SCRIPT_DIR/generate_merkaba_pipeline_pack.py" --verify-source \
  --unity-project "$QIS_UNITY_HOST_PROJECT" --manifest "$QIS_PIPELINE_MANIFEST"
qis_build_tools="$(find "$QIS_ANDROID_SDK_ROOT/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -n 1)"
test -x "$qis_build_tools/apksigner"
test -x "$qis_build_tools/zipalign"
"$qis_build_tools/zipalign" -P 16 -f 4 "$QIS_MERKABA_APK_PATH" "$QIS_MERKABA_APK_PATH.aligned"
"$qis_build_tools/apksigner" sign --ks "$QIS_RELEASE_KEYSTORE" --ks-key-alias "$QIS_RELEASE_KEY_ALIAS" \
  --ks-pass env:QIS_RELEASE_STORE_PASSWORD --key-pass env:QIS_RELEASE_KEY_PASSWORD \
  --out "$QIS_MERKABA_APK_PATH.signed" "$QIS_MERKABA_APK_PATH.aligned"
mv -- "$QIS_MERKABA_APK_PATH.signed" "$QIS_MERKABA_APK_PATH"
"${SCRIPT_DIR}/verify_merkaba_apk.sh" "$QIS_MERKABA_APK_PATH" "$QIS_PIPELINE_MANIFEST"
printf 'FRESH APK: %s\nPREPARE LOG: %s\nBUILD LOG: %s\n' \
  "${QIS_MERKABA_APK_PATH}" "${PREPARE_LOG}" "${BUILD_LOG}"
