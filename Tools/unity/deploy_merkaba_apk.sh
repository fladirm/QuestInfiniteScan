#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "${SCRIPT_DIR}/../storage/dev_environment.sh"

test -x "${QIS_ADB_EXECUTABLE}"
test -s "${QIS_MERKABA_APK_PATH}"
mapfile -t DEVICES < <("${QIS_ADB_EXECUTABLE}" devices | \
  awk 'NR > 1 && $2 == "device" { print $1 }')

case "${#DEVICES[@]}" in
  0)
    echo "DEPLOY BLOCKED: no authorized Quest attached" >&2
    exit 2
    ;;
  1)
    "${QIS_ADB_EXECUTABLE}" -s "${DEVICES[0]}" install -r -d "${QIS_MERKABA_APK_PATH}"
    printf 'DEPLOYED: %s -> %s\n' "${QIS_MERKABA_APK_PATH}" "${DEVICES[0]}"
    ;;
  *)
    printf 'DEPLOY BLOCKED: multiple authorized devices:' >&2
    printf ' %s' "${DEVICES[@]}" >&2
    printf '\n' >&2
    exit 3
    ;;
esac
