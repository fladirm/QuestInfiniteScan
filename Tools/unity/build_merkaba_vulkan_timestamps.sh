#!/usr/bin/env bash
set -euo pipefail

qis_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
source "$qis_script_dir/../storage/dev_environment.sh"

qis_editor="$(dirname -- "$QIS_UNITY_EXECUTABLE")"
qis_ndk="$qis_editor/Data/PlaybackEngines/AndroidPlayer/NDK"
qis_toolchain="$qis_ndk/toolchains/llvm/prebuilt/linux-x86_64"
qis_clang="$qis_toolchain/bin/aarch64-linux-android29-clang++"
qis_repo="$(git -C "$qis_script_dir/../.." rev-parse --show-toplevel)"
qis_project="${1:-$QIS_UNITY_HOST_PROJECT}"
qis_output_dir="$qis_project/Assets/Plugins/Android"
qis_output="$qis_output_dir/libMerkabaVulkanTimestamps.so"

test -x "$qis_clang"
test -f "$qis_project/ProjectSettings/ProjectVersion.txt"
install -d -- "$qis_output_dir"
"$qis_clang" --std=c++17 -O2 -fPIC -fvisibility=hidden -shared \
    -Wl,--no-undefined -Wl,-z,max-page-size=16384 \
    -I"$qis_editor/Data/PluginAPI" \
    "$qis_repo/Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp" \
    -lvulkan -o "$qis_output.tmp"
mv -- "$qis_output.tmp" "$qis_output"
install -m 0644 -- "$qis_script_dir/MerkabaVulkanTimestamps.pluginmeta" \
    "$qis_output.meta"
printf 'Merkaba Vulkan timestamps ready: %s\n' "$qis_output"
