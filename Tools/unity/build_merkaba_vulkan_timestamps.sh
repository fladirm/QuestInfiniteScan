#!/usr/bin/env bash
set -euo pipefail

qis_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
source "$qis_script_dir/../storage/dev_environment.sh"

qis_editor="$(dirname -- "$QIS_UNITY_EXECUTABLE")"
qis_ndk="$qis_editor/Data/PlaybackEngines/AndroidPlayer/NDK"
qis_toolchain="$qis_ndk/toolchains/llvm/prebuilt/linux-x86_64"
qis_clang="$qis_toolchain/bin/aarch64-linux-android29-clang++"
qis_strip="$qis_toolchain/bin/llvm-strip"
qis_repo="$(git -C "$qis_script_dir/../.." rev-parse --show-toplevel)"
qis_project="${1:-$QIS_UNITY_HOST_PROJECT}"
qis_output_dir="$qis_project/Assets/Plugins/Android"
qis_output="$qis_output_dir/libMerkabaVulkanTimestamps.so"
qis_generated_dir="$(mktemp -d -t merkaba-native-executor.XXXXXX)"
trap 'rm -rf -- "$qis_generated_dir"' EXIT

test -x "$qis_clang"
test -x "$qis_strip"
test -f "$qis_project/ProjectSettings/ProjectVersion.txt"
install -d -- "$qis_output_dir"
python3 "$qis_script_dir/generate_merkaba_native_executor_shaders.py" \
    --output "$qis_generated_dir/MerkabaNativeExecutorShaders.inc" \
    --metrics-report "$qis_generated_dir/shader-metrics.json"
if [[ "${QIS_PIPELINE_COMPILE_ONLY:-1}" == 0 ]]; then
    qis_performance_args=(--metrics "$qis_generated_dir/shader-metrics.json")
    if [[ "${QIS_APK_PURPOSE:-RELEASE}" == FUNCTIONAL_TEST ]]; then
        if [[ "${QIS_PIPELINE_MODE:-}" != CAPTURE ]]; then
            echo "FUNCTIONAL_TEST requires CAPTURE; BINARY_ONLY release gates cannot be waived" >&2
            exit 1
        fi
        qis_performance_args+=(--functional-test)
    fi
    if [[ -n "${QIS_ADRENO_EVIDENCE:-}" ]]; then
        qis_performance_args+=(--adreno-evidence "$QIS_ADRENO_EVIDENCE")
    fi
    python3 "$qis_script_dir/validate_merkaba_hot_path.py" "${qis_performance_args[@]}"
fi
qis_pack_args=(--unity-project "$qis_project" --mode "${QIS_PIPELINE_MODE:-BINARY_ONLY}"
    --shader-metrics "$qis_generated_dir/shader-metrics.json"
    --output "$qis_generated_dir/MerkabaPipelinePack.inc"
    --manifest "${QIS_PIPELINE_MANIFEST:-$qis_generated_dir/pipeline-manifest.json}")
if [[ "${QIS_PIPELINE_COMPILE_ONLY:-1}" == 1 ]]; then
    qis_pack_args+=(--allow-empty-for-compile)
fi
if [[ -n "${QIS_PIPELINE_CAPTURE_DIR:-}" ]]; then
    qis_pack_args+=(--capture-dir "$QIS_PIPELINE_CAPTURE_DIR"
        --required-log "$QIS_PIPELINE_REQUIRED_LOG" --graphics-trace "$QIS_PIPELINE_GRAPHICS_TRACE")
fi
python3 "$qis_script_dir/generate_merkaba_pipeline_pack.py" "${qis_pack_args[@]}"
"$qis_clang" --std=c++17 -O2 -fPIC -fvisibility=hidden -shared \
    -Wl,--no-undefined -Wl,-z,max-page-size=16384 \
    -I"$qis_editor/Data/PluginAPI" \
    -I"$qis_generated_dir" \
    "$qis_repo/Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp" \
    -lvulkan -llog -o "$qis_output.tmp"
# Gradle strips Android plugin symbols when packaging. Stamp that exact
# release payload, not the unstripped host library that never enters the APK.
"$qis_strip" --strip-unneeded "$qis_output.tmp"
mv -- "$qis_output.tmp" "$qis_output"
python3 "$qis_script_dir/generate_merkaba_pipeline_pack.py" --stamp-native "$qis_output" \
    --manifest "${QIS_PIPELINE_MANIFEST:-$qis_generated_dir/pipeline-manifest.json}"
install -m 0644 -- "$qis_script_dir/MerkabaVulkanTimestamps.pluginmeta" \
    "$qis_output.meta"
printf 'Merkaba Vulkan timestamps ready: %s\n' "$qis_output"
