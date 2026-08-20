#!/usr/bin/env bash
set -euo pipefail

qis_repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
export PYTHONPATH="$qis_repo_root/Server/src${PYTHONPATH:+:$PYTHONPATH}"
export PYTHONDONTWRITEBYTECODE=1
exec python3 -m unittest discover -s "$qis_repo_root/Server/tests" -v
