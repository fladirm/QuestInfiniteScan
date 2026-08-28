#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "${SCRIPT_DIR}/../storage/dev_environment.sh"
ulimit -n 65536
FIXTURE_DIR="${QIS_BUILD_ROOT}/TestResults"
FIXTURE_PATH="${FIXTURE_DIR}/merkaba-fixture.glb"
UNITY_LOG="${FIXTURE_DIR}/merkaba-glb-fixture.log"
mkdir -p "${FIXTURE_DIR}"
export QIS_MERKABA_GLB_FIXTURE_PATH="${FIXTURE_PATH}"

"${QIS_UNITY_EXECUTABLE}" \
  -batchmode \
  -nographics \
  -projectPath "${QIS_UNITY_HOST_PROJECT}" \
  -executeMethod Genesis.RoomScan.Editor.MerkabaGlbFixtureBuilder.BuildMerkabaGlbFixture \
  -quit \
  -logFile "${UNITY_LOG}"

test -s "${FIXTURE_PATH}"
grep -Fq '[QuestMerkabaScan] GLB Fixture Succeeded:' "${UNITY_LOG}"
NPM_WORK="$(mktemp -d)"
trap 'rm -rf "${NPM_WORK}"' EXIT
cp "${SCRIPT_DIR}/package.json" "${SCRIPT_DIR}/package-lock.json" \
  "${SCRIPT_DIR}/verify_merkaba_glb.mjs" "${NPM_WORK}/"
npm --prefix "${NPM_WORK}" ci --ignore-scripts --no-audit --no-fund
node "${NPM_WORK}/verify_merkaba_glb.mjs" "${FIXTURE_PATH}"
