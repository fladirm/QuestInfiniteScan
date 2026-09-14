#!/usr/bin/env bash
# Runs the host project's EditMode tests in batch; results under $FS_BUILD_DIR/TestResults.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
if [[ "${FS_UNITY_BUILD_SCOPE:-0}" != 1 ]]; then
  exec systemd-run --user --scope --quiet \
    -p MemoryHigh=12G -p MemoryMax=16G -p MemorySwapMax=2G \
    taskset --cpu-list "${FS_UNITY_BUILD_CPUS:-0-1}" \
    env FS_UNITY_BUILD_SCOPE=1 bash "${SCRIPT_DIR}/run_unity_tests.sh" "$@"
fi
source "${SCRIPT_DIR}/../dev_environment.sh"
"${SCRIPT_DIR}/verify_install.sh" >/dev/null
ulimit -n 65536
RESULT_DIR="${FS_BUILD_DIR}/TestResults"
RESULT_XML="${RESULT_DIR}/editmode-results.xml"
LOG_FILE="${RESULT_DIR}/editmode-tests.log"
mkdir -p "${RESULT_DIR}"
rm -f "${RESULT_XML}"

set +e
"${FS_UNITY_EXECUTABLE}" -batchmode -nographics -job-worker-count 2 -buildTarget Android \
  -projectPath "${FS_HOST_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${RESULT_XML}" \
  -logFile "${LOG_FILE}"
UNITY_EXIT=$?
set -e
if grep -Eq 'error CS[0-9]+|Shader error in' "${LOG_FILE}"; then
  echo "C# or shader compilation failed; inspect ${LOG_FILE}" >&2; exit 1
fi
test -s "${RESULT_XML}" || { echo "no test results (unity exit ${UNITY_EXIT}); inspect ${LOG_FILE}" >&2; exit 1; }
python3 - "${RESULT_XML}" <<'PY'
import sys
import xml.etree.ElementTree as ET
run = ET.parse(sys.argv[1]).getroot()
if run.tag != 'test-run' or run.get('result') != 'Passed' or int(run.get('failed', '-1')) != 0:
    raise SystemExit('FAIL: Unity test-run did not pass: ' + str(run.attrib))
if int(run.get('total', '0')) == 0:
    raise SystemExit('FAIL: Unity test-run executed no tests')
print('tests total=%s passed=%s' % (run.get('total'), run.get('passed')))
PY
printf 'PASS: %s\nLOG: %s\n' "${RESULT_XML}" "${LOG_FILE}"
