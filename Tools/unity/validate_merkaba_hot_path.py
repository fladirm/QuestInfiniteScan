#!/usr/bin/env python3
"""Release gate over the exact embedded payload, not an optimized audit twin.

An optional Adreno review receipt references retained compiler/profiler reports
by hash. It records an explicit GPR/I-cache review, not a conclusion inferred
from SPIR-V size. Receipt fields never waive the hard module/ABI limits.
"""
import argparse
import hashlib
import json
from pathlib import Path

from generate_merkaba_native_executor_shaders import PIPELINES, command_schedules


def approved(report, evidence, base):
    entry = evidence.get(report["entrypoint"], {})
    if (entry.get("spirvSha256") != report["sha256"] or
            entry.get("gpu") != "Adreno 740" or not entry.get("driver") or
            entry.get("spillBytes") != 0 or entry.get("gprInstructionCacheReviewed") is not True):
        return False
    reports = entry.get("reports", [])
    if not reports:
        return False
    for retained in reports:
        path = (base / retained["path"]).resolve()
        if (not path.is_file() or path.stat().st_size == 0 or
                hashlib.sha256(path.read_bytes()).hexdigest() != retained["sha256"]):
            return False
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--metrics", type=Path, required=True)
    parser.add_argument("--adreno-evidence", type=Path)
    args = parser.parse_args()
    reports = json.loads(args.metrics.read_text())
    by_name = {r["entrypoint"]: r for r in reports}
    expected = {p.entry for p in PIPELINES}
    if len(by_name) != len(reports) or set(by_name) != expected:
        raise ValueError("metrics are not the complete current native pipeline inventory")
    schedules = dict(command_schedules())
    observation = schedules["Observation"]
    hot = {PIPELINES[i].entry for name, ids in schedules.items() for i in ids
           if name in ("Observation", "FlowerReadout") and PIPELINES[i].entry != "RebuildDirtyFlowerOwners"}
    evidence = json.loads(args.adreno_evidence.read_text()) if args.adreno_evidence else {}
    base = args.adreno_evidence.parent if args.adreno_evidence else Path.cwd()
    failures = []
    if len(observation) > 11:
        failures.append(f"HOT observation has {len(observation)} dispatches; target is 9–11")
    for name, report in by_name.items():
        if report["metric_gate"] == "FAIL":
            failures.append(f"{name}: hard shader/ABI gate failed")
        if name in hot and report["function_body_instructions"] > 20000 and not approved(report, evidence, base):
            failures.append(f"{name}: >20k HOT instructions without exact-hash Adreno zero-spill/GPR/I-cache evidence")
    if failures:
        raise SystemExit("RELEASE BLOCKED:\n" + "\n".join(failures))
    print(f"HOT release gate PASS: {len(observation)} dispatches; {len(hot)} exact shader payloads")


if __name__ == "__main__":
    main()
