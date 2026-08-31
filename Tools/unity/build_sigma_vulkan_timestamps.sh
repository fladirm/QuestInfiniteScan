#!/usr/bin/env bash
set -euo pipefail

qis_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/storage/dev_environment.sh
source "$qis_script_dir/../storage/dev_environment.sh"

qis_unity_version="${QIS_UNITY_VERSION:-6000.5.9f1}"
qis_editor="$QIS_UNITY_EDITOR_ROOT/$qis_unity_version/Editor"
qis_ndk="$qis_editor/Data/PlaybackEngines/AndroidPlayer/NDK"
qis_toolchain="$qis_ndk/toolchains/llvm/prebuilt/linux-x86_64"
qis_clang="$qis_toolchain/bin/aarch64-linux-android29-clang++"
qis_repo="$(git -C "$qis_script_dir/../.." rev-parse --show-toplevel)"
qis_project="${1:-$QIS_UNITY_PROJECT_ROOT/QuestInfiniteScanHost}"
qis_output_dir="$qis_project/Assets/Plugins/Android"
qis_output="$qis_output_dir/libSigmaVulkanTimestamps.so"
qis_generated_dir="$(mktemp -d)"
trap 'rm -rf -- "$qis_generated_dir"' EXIT

test -x "$qis_clang"
python3 "$qis_script_dir/generate_sigma_native_executor_shaders.py" \
    --output "$qis_generated_dir/SigmaNativeExecutorShaders.inc"
install -d -- "$qis_output_dir"
"$qis_clang" --std=c++17 -O2 -fPIC -fvisibility=hidden -shared \
    -Wl,--no-undefined -Wl,-z,max-page-size=16384 \
    -I"$qis_editor/Data/PluginAPI" \
    -I"$qis_generated_dir" \
    "$qis_repo/Runtime/SigmaPrism/Native/SigmaVulkanTimestamps.cpp" \
    -lvulkan -o "$qis_output.tmp"
mv -- "$qis_output.tmp" "$qis_output"
install -m 0644 -- "$qis_script_dir/SigmaVulkanTimestamps.pluginmeta" \
    "$qis_output.meta"
printf 'Sigma Vulkan timestamps ready: %s\n' "$qis_output"
