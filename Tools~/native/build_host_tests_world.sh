#!/usr/bin/env bash
# Builds and runs the host (linux x86_64) unit tests of the world/render modules (pure headers: page hash,
# encodings, cluster build reference, residency math, HZB band rule, synthetic scenes). No Vulkan needed.
set -euo pipefail
fs_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
fs_root="$(cd -- "$fs_script_dir/../.." && pwd -P)"
fs_native="$fs_root/Native~"
fs_out="${FS_HOST_TEST_DIR:-${TMPDIR:-/tmp}/finalscan-host-tests}"
mkdir -p "$fs_out"
if command -v g++ >/dev/null 2>&1; then fs_cxx=g++; elif command -v clang++ >/dev/null 2>&1; then fs_cxx=clang++; else echo "no host C++ compiler" >&2; exit 1; fi
"$fs_cxx" -std=c++17 -O2 -g -Wall -Wextra -Wno-misleading-indentation \
    -I"$fs_native/src" -I"$fs_native/include" \
    "$fs_native/tests/host_tests_world.cpp" "$fs_native/src/host/world/fs_synthetic.cpp" \
    -o "$fs_out/finalscan_host_tests_world"
"$fs_out/finalscan_host_tests_world"
