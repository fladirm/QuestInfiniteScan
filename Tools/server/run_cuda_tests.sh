#!/usr/bin/env bash
set -euo pipefail

qis_repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=Tools/server/dev_environment.sh
source "$qis_repo_root/Tools/server/dev_environment.sh"
export QIS_COMPUTE_BACKEND=diffsoup
export QIS_RUN_CUDA_TESTS=1
exec uv run --project "$qis_repo_root/Server" pytest -q -p no:cacheprovider \
    "$qis_repo_root/Server/tests/test_diffsoup_cuda.py"
