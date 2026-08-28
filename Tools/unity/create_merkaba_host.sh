#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "${SCRIPT_DIR}/../storage/dev_environment.sh"
TEMPLATE_HOST="${QIS_TEMPLATE_HOST:-${QIS_UNITY_PROJECT_ROOT}/QuestInfiniteScanHost}"

if [[ -f "${QIS_UNITY_HOST_PROJECT}/ProjectSettings/ProjectVersion.txt" ]]; then
  "${SCRIPT_DIR}/verify_unity_install.sh"
  exit 0
fi

test -f "${TEMPLATE_HOST}/ProjectSettings/ProjectVersion.txt"
mkdir -p "${QIS_UNITY_HOST_PROJECT}/Assets"
# The template contributes only generic Unity/Meta configuration. The canonical setup
# method creates the target scene, manifest, menu, and scanner; reconstruction and donor
# diagnostics never cross this boundary.
rsync -a \
  --exclude '/Scenes/' \
  --exclude '*Sigma*' \
  --exclude '*sigma*' \
  --exclude '*PRISM*' \
  --exclude '*Prism*' \
  --exclude '/Settings/DebugMenuPanelSettings.asset*' \
  --exclude '/Resources/DevAgentSettings.asset*' \
  --exclude '/Resources/ImmersiveDebuggerSettings.asset*' \
  --exclude '/Resources/PerformanceTestRun*.json*' \
  --exclude '/Plugins/Android/AndroidManifest.xml*' \
  --exclude '/Plugins/Android/QuestRoomScanManifest.androidlib/' \
  --exclude '/Plugins/Android/NetworkSecurityConfig.androidlib/' \
  "${TEMPLATE_HOST}/Assets/" "${QIS_UNITY_HOST_PROJECT}/Assets/"
rsync -a "${TEMPLATE_HOST}/Packages/" "${QIS_UNITY_HOST_PROJECT}/Packages/"
rsync -a "${TEMPLATE_HOST}/ProjectSettings/" \
  "${QIS_UNITY_HOST_PROJECT}/ProjectSettings/"

PACKAGE_LINK="${QIS_UNITY_HOST_PROJECT}/Packages/com.genesis.roomscan"
if [[ -L "${PACKAGE_LINK}" ]]; then
  unlink "${PACKAGE_LINK}"
elif [[ -e "${PACKAGE_LINK}" ]]; then
  echo "Refusing to replace non-symlink package path ${PACKAGE_LINK}" >&2
  exit 1
fi
ln -s "${QIS_TARGET_ROOT}" "${PACKAGE_LINK}"
"${SCRIPT_DIR}/verify_unity_install.sh"
