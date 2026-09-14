#!/usr/bin/env bash
# Builds and runs the host (linux x86_64) unit tests for the FinalScan native plugin (no Vulkan driver):
#   Native~/tests/host_tests.cpp           json_writer + pure vkCreateDevice create-info patching
#   Native~/tests/host_tests_executor.cpp  scheduler core (rings, budget, deferral, leases, root-last
#                                          publish, timelines), pipeline store files, telemetry/clock fit
#   Native~/tests/host_tests_measure.cpp   C09 measurement front-end math (depth back-projection twin; header-only)
#   Native~/tests/host_tests_<name>.cpp    any further suite; its sources come from a line
#                                          "// HOST_TEST_SOURCES: a.cpp b.cpp" (paths relative to Native~/src)
# Each suite is one executable; a failing suite fails the script. FS_HOST_TEST_FILTER=<name> runs one suite.
set -euo pipefail
fs_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
source "$fs_script_dir/../dev_environment.sh"
fs_native="$FS_PACKAGE_ROOT/Native~"
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

fs_flags=(-std=c++17 -O1 -g -Wall -Wextra -DVK_NO_PROTOTYPES -isystem "$fs_out/include" -I"$fs_native/src" -I"$fs_native/include" -pthread)
fs_failed=0
fs_run_suite() {   # name test.cpp sources...
    local name="$1" test="$2"; shift 2
    if [[ -n "${FS_HOST_TEST_FILTER:-}" && "$name" != "$FS_HOST_TEST_FILTER" ]]; then return; fi
    echo "== host suite: $name"
    "$fs_cxx" "${fs_flags[@]}" "$test" "$@" -o "$fs_out/finalscan_$name"
    if ! "$fs_out/finalscan_$name"; then fs_failed=1; fi
}

fs_run_suite host_tests "$fs_native/tests/host_tests.cpp" "$fs_native/src/device_patch.cpp"
fs_run_suite host_tests_executor "$fs_native/tests/host_tests_executor.cpp" \
    "$fs_native/src/host/executor/sched_core.cpp" "$fs_native/src/host/executor/pipeline_store.cpp" "$fs_native/src/host/telemetry.cpp"
# Additional suites declare their sources inline.
for test in "$fs_native"/tests/host_tests_*.cpp; do
    name="$(basename -- "$test" .cpp)"
    [[ "$name" == "host_tests_executor" ]] && continue
    line="$(grep -m1 -E '^// HOST_TEST_SOURCES:' "$test" || true)"
    [[ -n "$line" ]] || { echo "skip $name: no '// HOST_TEST_SOURCES:' line"; continue; }
    srcs=()
    for s in ${line#// HOST_TEST_SOURCES:}; do srcs+=("$fs_native/src/$s"); done
    fs_run_suite "$name" "$test" "${srcs[@]}"
done
if [[ $fs_failed -ne 0 ]]; then echo "host tests: FAILED"; exit 1; fi
echo "host tests: all suites passed"
