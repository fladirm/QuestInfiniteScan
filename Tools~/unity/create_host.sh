#!/usr/bin/env bash
# Creates the FinalScan Unity host project on kingston from the generic template host.
# Only generic Unity/Meta configuration crosses; no donor scenes, panels, plugins or manifests.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "${SCRIPT_DIR}/../dev_environment.sh"
if [[ -f "${FS_HOST_PROJECT}/ProjectSettings/ProjectVersion.txt" ]]; then
  "${SCRIPT_DIR}/verify_install.sh"; exit 0
fi
test -f "${FS_TEMPLATE_HOST}/ProjectSettings/ProjectVersion.txt"
mkdir -p "${FS_HOST_PROJECT}/Assets"
rsync -a \
  --exclude '/Scenes/' --exclude '*Sigma*' --exclude '*sigma*' --exclude '*PRISM*' --exclude '*Prism*' \
  --exclude '*Merkaba*' --exclude '/Settings/DebugMenuPanelSettings.asset*' \
  --exclude '/Resources/DevAgentSettings.asset*' --exclude '/Resources/ImmersiveDebuggerSettings.asset*' \
  --exclude '/Resources/PerformanceTestRun*.json*' --exclude '/Plugins/' --exclude '/StreamingAssets/' \
  "${FS_TEMPLATE_HOST}/Assets/" "${FS_HOST_PROJECT}/Assets/"
mkdir -p "${FS_HOST_PROJECT}/Packages" "${FS_HOST_PROJECT}/ProjectSettings"
rsync -a --exclude 'com.genesis.roomscan' "${FS_TEMPLATE_HOST}/Packages/" "${FS_HOST_PROJECT}/Packages/"
rsync -a "${FS_TEMPLATE_HOST}/ProjectSettings/" "${FS_HOST_PROJECT}/ProjectSettings/"
LINK="${FS_HOST_PROJECT}/Packages/${FS_PACKAGE_NAME}"
if [[ -L "$LINK" ]]; then unlink "$LINK"; elif [[ -e "$LINK" ]]; then echo "non-symlink at $LINK" >&2; exit 1; fi
ln -s "${FS_PACKAGE_ROOT}" "$LINK"
"${SCRIPT_DIR}/verify_install.sh"
