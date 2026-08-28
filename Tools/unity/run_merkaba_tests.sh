#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "${SCRIPT_DIR}/../storage/dev_environment.sh"
ulimit -n 65536
"${SCRIPT_DIR}/verify_unity_install.sh" >/dev/null

RESULT_DIR="${QIS_BUILD_ROOT}/TestResults"
RESULT_XML="${RESULT_DIR}/merkaba-results.xml"
LOG_FILE="${RESULT_DIR}/merkaba-tests.log"
mkdir -p "${RESULT_DIR}"

"${QIS_UNITY_EXECUTABLE}" \
  -batchmode \
  -force-vulkan \
  -projectPath "${QIS_UNITY_HOST_PROJECT}" \
  -runTests \
  -testPlatform EditMode \
  -testResults "${RESULT_XML}" \
  -logFile "${LOG_FILE}"

test -s "${RESULT_XML}"
grep -q 'result="Passed"' "${RESULT_XML}"
if grep -Eq 'error CS[0-9]+|Shader error in' "${LOG_FILE}"; then
  echo "C# or shader compilation failed; inspect ${LOG_FILE}" >&2
  exit 1
fi
printf 'PASS: %s\nLOG: %s\n' "${RESULT_XML}" "${LOG_FILE}"
