#!/usr/bin/env bash
# Live scan run on the Quest: install (optional), wake, launch, capture logcat + meminfo + screencaps,
# pull sensor jsonl, summarise FS-HOST / FS-SENSOR / FS-NATIVE lines. Usage: run_live.sh [duration_s] [--no-install]
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "${SCRIPT_DIR}/../dev_environment.sh"
DURATION="${1:-120}"; INSTALL=1; [[ "${2:-}" == "--no-install" ]] && INSTALL=0
ADB=("${FS_ADB}" -s "${FS_DEVICE_SERIAL}")
# Canonical run: no debug property may survive from an earlier session (mode/spin/synthetic/reset overrides).
for prop in debug.finalscan.mode debug.finalscan.spintest debug.finalscan.synthetic debug.finalscan.resetat; do "${ADB[@]}" shell setprop "$prop" '""' >/dev/null 2>&1 || true; done
# Acceptance fixtures (C09R §40-§41), applied AFTER the clearing so they hold for this run only:
#   FS_RUN_SYNTHETIC=<kind+1> (5 = dense foliage fed through the fusion epochs), FS_RUN_SPIN=1 (residency spin test),
#   FS_RUN_RESET_AT=<seconds after READY> (RESET WORLD once, same path as the panel button), FS_RUN_TAG=<name>.
[[ -n "${FS_RUN_SYNTHETIC:-}" ]] && "${ADB[@]}" shell setprop debug.finalscan.synthetic "$FS_RUN_SYNTHETIC"
[[ -n "${FS_RUN_SPIN:-}" ]] && "${ADB[@]}" shell setprop debug.finalscan.spintest "$FS_RUN_SPIN"
[[ -n "${FS_RUN_RESET_AT:-}" ]] && "${ADB[@]}" shell setprop debug.finalscan.resetat "$FS_RUN_RESET_AT"
OUT="${FS_EVIDENCE_DIR}/${FS_RUN_TAG:-live}-$(date -u +%Y%m%dT%H%M%SZ)"; mkdir -p "$OUT/meminfo" "$OUT/screens"
( cd "${SCRIPT_DIR}/../.." && git rev-parse HEAD ) > "$OUT/ACCEPTANCE_SHA" 2>/dev/null || true
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
"${ADB[@]}" pull "/sdcard/Android/data/$FS_APP_ID/files/host" "$OUT/" >/dev/null 2>&1 || true
grep -E 'FS-HOST|FS-SENSOR|FS-NATIVE|FS-MEAS|FS-WORLD|FS-RENDER|FinalScanNative|FinalScan\]|FS-ACCEPT|FS-SPIN' "$OUT/logcat.txt" > "$OUT/fs.txt" || true
grep -E 'FATAL|SIGSEGV|SIGABRT|DEVICE_LOST|Exception|VrApi' "$OUT/logcat.txt" | grep -vE 'MessageExecutionMonitor' > "$OUT/errors.txt" || true
echo "[run_live] FS lines: $(wc -l < "$OUT/fs.txt")  error/vrapi lines: $(wc -l < "$OUT/errors.txt")"
echo "[run_live] done -> $OUT"
