#!/usr/bin/env bash
set -uo pipefail

qis_repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=Tools/storage/dev_environment.sh
source "$qis_repo_root/Tools/storage/dev_environment.sh" || exit 1
export PYTHONDONTWRITEBYTECODE=1

qis_stamp="$(date -u +%Y%m%dT%H%M%SZ)"
qis_output="${QIS_VERIFY_OUTPUT:-$QIS_BUILD_ROOT/Verification/$qis_stamp}"
qis_steps="$qis_output/steps.tsv"
qis_failed=0
install -d -- "$qis_output/logs"
: > "$qis_steps"

qis_run_step() {
    local qis_name="$1"
    shift
    local qis_log="$qis_output/logs/$qis_name.log"
    local qis_started qis_finished qis_status
    qis_started="$(date +%s)"
    printf '[verify] %-24s ' "$qis_name"
    if "$@" > "$qis_log" 2>&1; then
        qis_status=0
        printf 'PASS\n'
    else
        qis_status=$?
        qis_failed=1
        printf 'FAIL (%d)\n' "$qis_status"
        tail -n 40 "$qis_log" >&2 || true
    fi
    qis_finished="$(date +%s)"
    printf '%s\t%s\t%s\t%s\n' "$qis_name" "$qis_status" \
        "$((qis_finished - qis_started))" "$qis_log" >> "$qis_steps"
}

qis_run_step control_plane \
    python3 "$qis_repo_root/Tools/validate_goal_state.py"
qis_run_step diff_hygiene git -C "$qis_repo_root" diff --check
qis_run_step sigma_compute_uav \
    python3 "$qis_repo_root/Tools/unity/validate_sigma_compute_uav.py"
qis_run_step unity_editmode \
    "$qis_repo_root/Tools/unity/run_editmode_tests.sh"
qis_run_step gltf_interoperability \
    "$qis_repo_root/Tools/gltf/verify_interoperability.sh"

if [[ "${QIS_VERIFY_ANDROID:-0}" == "1" ]]; then
    qis_run_step android_vulkan_build \
        "$qis_repo_root/Tools/unity/build_smoke_apk.sh"
fi

qis_revision="$(git -C "$qis_repo_root" rev-parse HEAD)"
if [[ -n "$(git -C "$qis_repo_root" status --porcelain)" ]]; then
    qis_dirty=true
else
    qis_dirty=false
fi
python3 - "$qis_steps" "$qis_output/verification-report.json" \
    "$qis_stamp" "$qis_revision" "$qis_dirty" <<'PY'
import json
from pathlib import Path
import sys

steps_path, report_path, stamp, revision, dirty = sys.argv[1:]
steps = []
for line in Path(steps_path).read_text(encoding="utf-8").splitlines():
    name, status, duration, log = line.split("\t", 3)
    steps.append({
        "name": name,
        "passed": int(status) == 0,
        "exitCode": int(status),
        "durationSeconds": int(duration),
        "log": log,
    })
report = {
    "schemaVersion": 1,
    "capturedUtc": stamp,
    "repositoryRevision": revision,
    "worktreeDirty": dirty == "true",
    "allPassed": all(step["passed"] for step in steps),
    "steps": steps,
}
Path(report_path).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
PY

printf 'Verification report: %s\n' "$qis_output/verification-report.json"
exit "$qis_failed"
