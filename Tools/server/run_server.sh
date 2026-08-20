#!/usr/bin/env bash
set -euo pipefail

qis_repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=Tools/server/dev_environment.sh
source "$qis_repo_root/Tools/server/dev_environment.sh"
export QIS_SERVER_HOST="${QIS_SERVER_HOST:-0.0.0.0}"
export QIS_SERVER_PORT="${QIS_SERVER_PORT:-8420}"
exec uv run --project "$qis_repo_root/Server" python -m quest_infinite_server

