#!/usr/bin/env bash
# Live scan run on the Quest: install (optional), wake, launch, capture logcat + meminfo + screencaps,
# pull sensor jsonl, summarise FS-HOST / FS-SENSOR / FS-NATIVE lines. Usage: run_live.sh [duration_s] [--no-install]
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "${SCRIPT_DIR}/../dev_environment.sh"
DURATION="${1:-120}"; INSTALL=1; [[ "${2:-}" == "--no-install" ]] && INSTALL=0
ADB=("${FS_ADB}" -s "${FS_DEVICE_SERIAL}")
OUT="${FS_EVIDENCE_DIR}/live-$(date -u +%Y%m%dT%H%M%SZ)"; mkdir -p "$OUT/meminfo" "$OUT/screens"
echo "[run_live] evidence: $OUT"
if [[ $INSTALL == 1 ]]; then bash "${SCRIPT_DIR}/../unity/deploy_apk.sh" 2>&1 | grep -vE 'restricted method|loadLibrary|enable-native' | tee "$OUT/deploy.txt"; else
  "${ADB[@]}" shell input keyevent KEYCODE_WAKEUP >/dev/null 2>&1 || true
  "${ADB[@]}" shell am broadcast -a com.oculus.vrpowermanager.automation_disable >/dev/null 2>&1 || true
  "${ADB[@]}" shell am broadcast -a com.oculus.vrpowermanager.prox_close >/dev/null 2>&1 || true
  "${ADB[@]}" shell am start -S -n "$FS_APP_ID/com.unity3d.player.UnityPlayerGameActivity" | tee "$OUT/am_start.txt"; fi
"${ADB[@]}" logcat -c || true
"${ADB[@]}" logcat -v threadtime > "$OUT/logcat.txt" 2>/dev/null & LC=$!
START=$(date +%s); i=0
while (( $(date +%s) - START < DURATION )); do
  sleep 10; i=$((i+1))
  "${ADB[@]}" shell dumpsys meminfo "$FS_APP_ID" 2>/dev/null | grep -E 'TOTAL PSS|Graphics|GL mtrack|Native Heap' | head -4 > "$OUT/meminfo/m-$i.txt" || true
  if (( i % 3 == 0 )); then "${ADB[@]}" exec-out screencap -p > "$OUT/screens/s-$i.png" 2>/dev/null || true; fi
  PID="$("${ADB[@]}" shell pidof "$FS_APP_ID" 2>/dev/null | tr -d '\r' || true)"
  if [[ -z "$PID" ]]; then echo "[run_live] app process gone at ${i}0s"; break; fi
done
kill $LC 2>/dev/null || true; sleep 1
"${ADB[@]}" pull "/sdcard/Android/data/$FS_APP_ID/files/sensor" "$OUT/" >/dev/null 2>&1 || true
"${ADB[@]}" pull "/sdcard/Android/data/$FS_APP_ID/files/finalscan" "$OUT/" >/dev/null 2>&1 || true
grep -E 'FS-HOST|FS-SENSOR|FS-NATIVE|FinalScanNative|FinalScan\]|FS-ACCEPT|FS-SPIN' "$OUT/logcat.txt" > "$OUT/fs.txt" || true
grep -E 'FATAL|SIGSEGV|SIGABRT|DEVICE_LOST|Exception|VrApi' "$OUT/logcat.txt" | grep -vE 'MessageExecutionMonitor' > "$OUT/errors.txt" || true
echo "[run_live] FS lines: $(wc -l < "$OUT/fs.txt")  error/vrapi lines: $(wc -l < "$OUT/errors.txt")"
echo "[run_live] done -> $OUT"
