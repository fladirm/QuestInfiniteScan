#!/usr/bin/env bash
set -euo pipefail

qis_repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=Tools/server/dev_environment.sh
source "$qis_repo_root/Tools/server/dev_environment.sh"
export PYTHONPATH="$qis_repo_root/Server/src${PYTHONPATH:+:$PYTHONPATH}"
exec "$QIS_DIFFSOUP_PYTHON" -m quest_infinite_server.diffsoup_worker probe
