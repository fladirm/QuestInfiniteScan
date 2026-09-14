#!/usr/bin/env bash
# FinalScan C01 device probe runner.
# Launches the probe build on the headset, samples dumpsys meminfo every 5 s, records logcat, pulls the jsonl,
# takes a screencap and grabs SurfaceFlinger/VrApi lines. Output: $FS_EVIDENCE_DIR/<utc>/.
# Usage: Tools~/probe/run_probe.sh [duration_seconds]   (default 240; env FS_ACTIVITY overrides the launch activity)
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=../dev_environment.sh
source "$HERE/../dev_environment.sh"

DURATION="${1:-${FS_PROBE_DURATION:-240}}"
FS_ACTIVITY="${FS_ACTIVITY:-com.unity3d.player.UnityPlayerGameActivity}"
MEMINFO_INTERVAL="${FS_MEMINFO_INTERVAL:-5}"
ADB=("$FS_ADB" -s "$FS_DEVICE_SERIAL")
UTC="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="$FS_EVIDENCE_DIR/$UTC"
mkdir -p "$OUT/meminfo"
echo "[run_probe] evidence dir: $OUT"
echo "[run_probe] app=$FS_APP_ID activity=$FS_ACTIVITY duration=${DURATION}s serial=$FS_DEVICE_SERIAL"

"${ADB[@]}" wait-for-device
"${ADB[@]}" shell input keyevent KEYCODE_WAKEUP || true
"${ADB[@]}" shell am force-stop "$FS_APP_ID" || true
"${ADB[@]}" shell rm -rf "/sdcard/Android/data/$FS_APP_ID/files/probe" || true
"${ADB[@]}" logcat -c || true
"${ADB[@]}" shell getprop > "$OUT/getprop.txt" || true
"${ADB[@]}" shell dumpsys meminfo "$FS_APP_ID" > "$OUT/meminfo/meminfo-before.txt" 2>/dev/null || true

# logcat capture (threadtime has pid/tid and ms timestamps)
"${ADB[@]}" logcat -v threadtime > "$OUT/logcat.txt" 2>&1 &
LOGCAT_PID=$!
cleanup() { kill "$LOGCAT_PID" 2>/dev/null || true; kill "${MEM_PID:-0}" 2>/dev/null || true; }
trap cleanup EXIT

START_EPOCH="$(date +%s)"
echo "$START_EPOCH" > "$OUT/start_epoch.txt"
"${ADB[@]}" shell am start -S -n "$FS_APP_ID/$FS_ACTIVITY" | tee "$OUT/am_start.txt"

# meminfo sampler: one file per sample, header carries host epoch and device uptime for correlation
(
  i=0
  while true; do
    ts="$(date +%s)"
    f="$OUT/meminfo/meminfo-$(printf '%04d' "$i")-$ts.txt"
    {
      echo "# host_epoch=$ts elapsed=$((ts - START_EPOCH)) device_uptime=$("${ADB[@]}" shell cat /proc/uptime 2>/dev/null | tr -d '\r')"
      "${ADB[@]}" shell dumpsys meminfo "$FS_APP_ID" 2>/dev/null
    } > "$f" || true
    grep -E 'TOTAL PSS|TOTAL RSS|Graphics:|GL mtrack|Native Heap|EGL mtrack' "$f" | tr -s ' ' | sed "s/^/[$((ts - START_EPOCH))s] /" || true
    i=$((i + 1))
    sleep "$MEMINFO_INTERVAL"
  done
) &
MEM_PID=$!

echo "[run_probe] running for ${DURATION}s ..."
END=$((START_EPOCH + DURATION))
while [ "$(date +%s)" -lt "$END" ]; do
  sleep 5
  # early exit when the probe reports done
  if grep -q '"event":"done"' "$OUT/logcat.txt" 2>/dev/null; then echo "[run_probe] probe reported done"; sleep 6; break; fi
done

"${ADB[@]}" exec-out screencap -p > "$OUT/screencap.png" 2>/dev/null || true
"${ADB[@]}" shell dumpsys SurfaceFlinger --latency > "$OUT/surfaceflinger_latency.txt" 2>/dev/null || true
"${ADB[@]}" shell dumpsys SurfaceFlinger 2>/dev/null | grep -iE 'refresh|fps|vsync|latency' | head -200 > "$OUT/surfaceflinger_grep.txt" || true
"${ADB[@]}" shell dumpsys meminfo "$FS_APP_ID" > "$OUT/meminfo/meminfo-after.txt" 2>/dev/null || true
"${ADB[@]}" shell dumpsys thermalservice > "$OUT/thermalservice.txt" 2>/dev/null || true
kill "$MEM_PID" 2>/dev/null || true
sleep 1
kill "$LOGCAT_PID" 2>/dev/null || true
wait "$LOGCAT_PID" 2>/dev/null || true

mkdir -p "$OUT/probe"
"${ADB[@]}" pull "/sdcard/Android/data/$FS_APP_ID/files/probe/" "$OUT/probe/" 2>&1 | tail -2 || echo "[run_probe] jsonl pull failed (permission or not written)"
# Fallback: run-as copy if external files are not directly readable
if ! ls "$OUT"/probe/*/*.jsonl "$OUT"/probe/*.jsonl >/dev/null 2>&1; then
  "${ADB[@]}" shell "run-as $FS_APP_ID sh -c 'cat files/probe/*.jsonl'" > "$OUT/probe/probe-runas.jsonl" 2>/dev/null || true
fi
grep -E 'VrApi|FS-PROBE|FS-NATIVE|FinalScan' "$OUT/logcat.txt" > "$OUT/logcat_filtered.txt" || true
grep -c 'FS-PROBE' "$OUT/logcat.txt" | sed 's/^/[run_probe] FS-PROBE lines: /' || true
echo "[run_probe] done -> $OUT"
if command -v python3 >/dev/null 2>&1; then
  python3 "$HERE/analyze_probe.py" "$OUT" -o "$OUT/DEVICE_ENVELOPE.md" && echo "[run_probe] wrote $OUT/DEVICE_ENVELOPE.md"
fi
