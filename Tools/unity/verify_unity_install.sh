#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "${SCRIPT_DIR}/../storage/dev_environment.sh"

test -x "${QIS_UNITY_EXECUTABLE}"
test -f "${QIS_UNITY_HOST_PROJECT}/ProjectSettings/ProjectVersion.txt"
test -L "${QIS_UNITY_HOST_PROJECT}/Packages/com.genesis.roomscan"
PACKAGE_TARGET="$(readlink -f "${QIS_UNITY_HOST_PROJECT}/Packages/com.genesis.roomscan")"
test "${PACKAGE_TARGET}" = "${QIS_TARGET_ROOT}"
test -x "${QIS_ADB_EXECUTABLE}"

PROJECT_VERSION="$(sed -n 's/^m_EditorVersion: //p' "${QIS_UNITY_HOST_PROJECT}/ProjectSettings/ProjectVersion.txt")"
test "${PROJECT_VERSION}" = "${QIS_UNITY_VERSION}"
printf 'Unity %s\nEditor %s\nHost %s\nPackage %s\nADB %s\n' \
  "${PROJECT_VERSION}" "${QIS_UNITY_EXECUTABLE}" "${QIS_UNITY_HOST_PROJECT}" \
  "${PACKAGE_TARGET}" "${QIS_ADB_EXECUTABLE}"
