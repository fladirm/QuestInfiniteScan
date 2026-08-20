#!/usr/bin/env bash
set -euo pipefail

qis_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/storage/dev_environment.sh
# shellcheck disable=SC1091
source "$qis_script_dir/../storage/dev_environment.sh"

qis_duration="${1:-30}"
if [[ ! "$qis_duration" =~ ^[0-9]+$ ]] ||
   (( qis_duration < 5 || qis_duration > 300 )); then
    printf 'Usage: %s [duration-seconds: 5..300]\n' "$0" >&2
    exit 2
fi

qis_package="${QIS_ANDROID_PACKAGE:-com.questinfinitescan.smoke}"
qis_serial="${QIS_ADB_SERIAL:-}"
if [[ -z "$qis_serial" ]]; then
    mapfile -t qis_devices < <(adb devices | awk 'NR > 1 && $2 == "device" {print $1}')
    if (( ${#qis_devices[@]} != 1 )); then
        printf 'Expected exactly one authorized ADB device; found %d.\n' \
            "${#qis_devices[@]}" >&2
        exit 1
    fi
    qis_serial="${qis_devices[0]}"
fi

qis_adb=(adb -s "$qis_serial")
qis_pid="$("${qis_adb[@]}" shell pidof -s "$qis_package" | tr -d '\r')"
if [[ -z "$qis_pid" ]]; then
    printf '%s is not running. Wear/wake the headset, launch the app, and start scanning.\n' \
        "$qis_package" >&2
    exit 1
fi

qis_stamp="$(date -u +%Y%m%dT%H%M%SZ)"
qis_output="$QIS_BUILD_ROOT/QuestInfiniteScan/profiles/tsdf-$qis_stamp"
install -d -- "$qis_output"

{
    printf 'captured_utc=%s\n' "$qis_stamp"
    printf 'duration_seconds=%s\n' "$qis_duration"
    printf 'serial=%s\n' "$qis_serial"
    printf 'package=%s\n' "$qis_package"
    printf 'pid=%s\n' "$qis_pid"
    "${qis_adb[@]}" shell getprop ro.product.model
    "${qis_adb[@]}" shell getprop ro.hzos.build.display_name
    "${qis_adb[@]}" shell dumpsys package "$qis_package" |
        awk '/versionCode=|versionName=|lastUpdateTime=|uses-metavr-sdk=/'
    "${qis_adb[@]}" shell dumpsys power |
        awk '/mWakefulness=|mProximityPositive=/'
} > "$qis_output/metadata.txt"

"${qis_adb[@]}" shell gpumeminfo -p "$qis_pid" -o -m \
    > "$qis_output/gpu-memory-before.txt" 2>&1 || true
"${qis_adb[@]}" shell dumpsys meminfo "$qis_pid" \
    > "$qis_output/process-memory-before.txt" 2>&1 || true

timeout --signal=INT "$qis_duration" \
    "${qis_adb[@]}" shell ovrgpuprofiler \
        --realtime='2,4,17,18,43,50,52' \
    > "$qis_output/gpu-realtime.txt" 2>&1 &
qis_gpu_capture_pid=$!

timeout --signal=INT "$qis_duration" \
    "${qis_adb[@]}" shell top -b -d 1 -p "$qis_pid" \
    > "$qis_output/process-top.txt" 2>&1 &
qis_top_capture_pid=$!

timeout --signal=INT "$qis_duration" \
    "${qis_adb[@]}" logcat --pid="$qis_pid" -v threadtime \
    > "$qis_output/app-logcat.txt" 2>&1 &
qis_log_capture_pid=$!

wait "$qis_gpu_capture_pid" || true
wait "$qis_top_capture_pid" || true
wait "$qis_log_capture_pid" || true

"${qis_adb[@]}" shell gpumeminfo -p "$qis_pid" -o -m \
    > "$qis_output/gpu-memory-after.txt" 2>&1 || true
"${qis_adb[@]}" shell dumpsys meminfo "$qis_pid" \
    > "$qis_output/process-memory-after.txt" 2>&1 || true
"${qis_adb[@]}" shell dumpsys gfxinfo "$qis_package" framestats \
    > "$qis_output/gfx-framestats.txt" 2>&1 || true

awk -F: '
    NF == 2 {
        name=$1; value=$2;
        gsub(/^[[:space:]]+|[[:space:]]+$/, "", name);
        gsub(/^[[:space:]]+|[[:space:]]+$/, "", value);
        if (value ~ /^-?[0-9]+([.][0-9]+)?$/) {
            sum[name] += value; count[name]++; if (value > max[name]) max[name]=value;
        }
    }
    END {
        for (name in count)
            printf "%s\tsamples=%d\tavg=%.3f\tmax=%.3f\n", name,
                count[name], sum[name]/count[name], max[name];
    }
' "$qis_output/gpu-realtime.txt" | sort > "$qis_output/gpu-summary.tsv"

rg 'QIS_TSDF_PROFILE' "$qis_output/app-logcat.txt" \
    > "$qis_output/tsdf-profile-lines.txt" || true
rg 'QIS_WORLD_PROFILE' "$qis_output/app-logcat.txt" \
    > "$qis_output/world-profile-lines.txt" || true

python3 "$qis_script_dir/analyze_quest_profile.py" "$qis_output"

printf 'Quest TSDF profile captured: %s\n' "$qis_output"
printf 'Inspect performance-summary.json, world-profile.csv, tsdf-profile.csv, gpu-summary.tsv, and raw memory snapshots.\n'
