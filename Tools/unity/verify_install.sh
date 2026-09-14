#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "${SCRIPT_DIR}/../dev_environment.sh"
test -x "${FS_UNITY_EXECUTABLE}"
test -f "${FS_HOST_PROJECT}/ProjectSettings/ProjectVersion.txt"
test -L "${FS_HOST_PROJECT}/Packages/${FS_PACKAGE_NAME}"
test "$(readlink -f "${FS_HOST_PROJECT}/Packages/${FS_PACKAGE_NAME}")" = "${FS_PACKAGE_ROOT}"
test -x "${FS_ADB}"
test -d "${FS_ANDROID_NDK_ROOT}"
V="$(sed -n 's/^m_EditorVersion: //p' "${FS_HOST_PROJECT}/ProjectSettings/ProjectVersion.txt")"
test "$V" = "${FS_UNITY_VERSION}"
printf 'Unity %s\nHost %s\nPackage %s\nNDK %s\nADB %s\n' "$V" "$FS_HOST_PROJECT" "$FS_PACKAGE_ROOT" "$FS_ANDROID_NDK_ROOT" "$FS_ADB"
