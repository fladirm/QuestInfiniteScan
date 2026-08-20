#!/usr/bin/env bash

qis_server_external_root="${QIS_SERVER_EXTERNAL_ROOT:-/mnt/kingston-unity/Server}"
export QIS_SERVER_DATA_ROOT="${QIS_SERVER_DATA_ROOT:-$qis_server_external_root/data}"
export UV_PROJECT_ENVIRONMENT="${UV_PROJECT_ENVIRONMENT:-$qis_server_external_root/.venv}"
export UV_CACHE_DIR="${UV_CACHE_DIR:-$qis_server_external_root/uv-cache}"
export QIS_COMPUTE_BACKEND="${QIS_COMPUTE_BACKEND:-diffsoup}"
export QIS_DIFFSOUP_PYTHON="${QIS_DIFFSOUP_PYTHON:-/mnt/kingston-unity/DiffSoup/.venv/bin/python}"
export QIS_DIFFSOUP_UPSTREAM_COMMIT="${QIS_DIFFSOUP_UPSTREAM_COMMIT:-c74e35de74ad0116977b23e7951f4cbc25ab0f6b}"
export PYTHONDONTWRITEBYTECODE=1
