#!/usr/bin/env bash
set -euo pipefail

qis_repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=Tools/storage/dev_environment.sh
source "$qis_repo_root/Tools/storage/dev_environment.sh"

qis_commit="c74e35de74ad0116977b23e7951f4cbc25ab0f6b"
qis_upstream="$QIS_DIFFSOUP_ROOT/upstream"
qis_environment="$QIS_DIFFSOUP_ROOT/.venv"
qis_python="$qis_environment/bin/python"
qis_uv_cache="$QIS_DIFFSOUP_ROOT/uv-cache"
qis_cuda_home="${CUDA_HOME:-/usr/local/cuda-13.3}"
qis_lock_hash="$(sha256sum "$qis_repo_root/Server/diffsoup-worker.lock.json" | cut -d' ' -f1)"
qis_marker="$QIS_DIFFSOUP_ROOT/.qis-worker-lock-sha256"

command -v git >/dev/null
command -v uv >/dev/null
test -x "$qis_cuda_home/bin/nvcc"

if [[ ! -d "$qis_upstream/.git" ]]; then
    git clone --filter=blob:none https://github.com/kenji-tojo/diffsoup.git \
        "$qis_upstream"
fi
if [[ -n "$(git -C "$qis_upstream" status --porcelain)" ]]; then
    printf 'Refusing a dirty DiffSoup checkout: %s\n' "$qis_upstream" >&2
    exit 1
fi
if [[ "$(git -C "$qis_upstream" rev-parse HEAD)" != "$qis_commit" ]]; then
    git -C "$qis_upstream" fetch --depth=1 origin "$qis_commit"
    git -C "$qis_upstream" switch --detach "$qis_commit"
fi
test "$(git -C "$qis_upstream" rev-parse HEAD)" = "$qis_commit"

install -d -- "$qis_uv_cache"
if [[ -x "$qis_python" ]] && "$qis_python" - <<'PY'
from importlib.metadata import version
import platform

expected = {
    "diffsoup": "0.1.0",
    "numpy": "2.5.2",
    "pillow": "12.3.0",
    "pytorch-msssim": "1.0.0",
    "scipy": "1.18.0",
    "torch": "2.13.0+cu130",
}
assert platform.python_version() == "3.14.4"
assert {name: version(name) for name in expected} == expected
import diffsoup._core  # noqa: F401
PY
then
    QIS_DIFFSOUP_PYTHON="$qis_python" \
    QIS_DIFFSOUP_UPSTREAM_COMMIT="$qis_commit" \
        "$qis_repo_root/Tools/server/probe_diffsoup_worker.sh"
    printf '%s\n' "$qis_lock_hash" > "$qis_marker"
    printf 'Pinned DiffSoup worker already satisfies the lock: %s\n' "$qis_python"
    exit 0
fi
if [[ ! -x "$qis_python" ]]; then
    UV_CACHE_DIR="$qis_uv_cache" uv python install 3.14.4
    UV_CACHE_DIR="$qis_uv_cache" uv venv --python 3.14.4 "$qis_environment"
fi
test "$($qis_python -c 'import platform; print(platform.python_version())')" = "3.14.4"

UV_CACHE_DIR="$qis_uv_cache" uv pip install --python "$qis_python" \
    --index-url https://download.pytorch.org/whl/cu130 \
    'torch==2.13.0+cu130'
UV_CACHE_DIR="$qis_uv_cache" uv pip install --python "$qis_python" \
    'numpy==2.5.2' 'pillow==12.3.0' 'pytorch-msssim==1.0.0' 'scipy==1.18.0'
CUDA_HOME="$qis_cuda_home" UV_CACHE_DIR="$qis_uv_cache" \
    CMAKE_ARGS='-DCMAKE_CUDA_ARCHITECTURES=89' \
    uv pip install --python "$qis_python" --no-deps --reinstall "$qis_upstream"

QIS_DIFFSOUP_PYTHON="$qis_python" \
QIS_DIFFSOUP_UPSTREAM_COMMIT="$qis_commit" \
    "$qis_repo_root/Tools/server/probe_diffsoup_worker.sh"
printf '%s\n' "$qis_lock_hash" > "$qis_marker"
printf 'Pinned DiffSoup worker ready: %s @ %s\n' "$qis_python" "$qis_commit"
