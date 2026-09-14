#!/usr/bin/env bash
# Builds and runs the host (linux x86_64) unit tests for the FinalScan native plugin:
# json_writer and the pure vkCreateDevice create-info patching logic (no Vulkan driver).
set -euo pipefail
fs_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
source "$fs_script_dir/../dev_environment.sh"
fs_native="$FS_PACKAGE_ROOT/Native"
fs_sysroot="$FS_ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/sysroot"
fs_out="${FS_HOST_TEST_DIR:-${TMPDIR:-/tmp}/finalscan-host-tests}"
mkdir -p "$fs_out"
if command -v g++ >/dev/null 2>&1; then fs_cxx=g++; elif command -v clang++ >/dev/null 2>&1; then fs_cxx=clang++; else
    echo "no host C++ compiler (g++/clang++)" >&2; exit 1; fi
test -f "$fs_sysroot/usr/include/vulkan/vulkan_core.h" || { echo "NDK vulkan_core.h missing at $fs_sysroot" >&2; exit 1; }
# Only the Vulkan headers come from the NDK sysroot (bionic's libc headers must not leak in).
mkdir -p "$fs_out/include"
ln -sfn "$fs_sysroot/usr/include/vulkan" "$fs_out/include/vulkan"
ln -sfn "$fs_sysroot/usr/include/vk_video" "$fs_out/include/vk_video"
"$fs_cxx" -std=c++17 -O1 -g -Wall -Wextra -DVK_NO_PROTOTYPES \
    -isystem "$fs_out/include" \
    -I"$fs_native/src" \
    "$fs_native/tests/host_tests.cpp" "$fs_native/src/device_patch.cpp" \
    -o "$fs_out/finalscan_host_tests"
"$fs_out/finalscan_host_tests"
