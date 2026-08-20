#!/usr/bin/env python3
"""Turn one Quest ADB capture into stable CSV and JSON performance evidence."""

from __future__ import annotations

import argparse
import csv
import json
import os
from pathlib import Path
import re
import tempfile
from typing import Iterable


WORLD_PREFIX = "QIS_WORLD_PROFILE"
TSDF_PREFIX = "QIS_TSDF_PROFILE"
WORLD_INTEGER_FIELDS = (
    "unixMs",
    "chunks",
    "activeRevision",
    "activeState",
    "edges",
    "residentVolumes",
    "maxResidentVolumes",
    "residentMeshes",
    "residentDiffSoup",
    "backgroundPublications",
    "allocatedBytes",
    "reservedBytes",
)
TSDF_INTEGER_FIELDS = (
    "samples",
    "protection",
    "tsdfBytes",
    "colorBytes",
    "frustumBytes",
    "integrations",
)
TSDF_FLOAT_FIELDS = (
    "cpuIntegrationAvgMs",
    "cpuIntegrationMaxMs",
    "cpuFrameAvgMs",
    "cpuFrameMaxMs",
    "gpuFrameAvgMs",
    "gpuFrameMaxMs",
)


class ProfileError(ValueError):
    pass


def _marker_values(line: str, prefix: str) -> dict[str, str] | None:
    marker = line.find(prefix)
    if marker < 0:
        return None
    values: dict[str, str] = {}
    for token in line[marker + len(prefix) :].strip().split():
        if "=" not in token:
            raise ProfileError(f"malformed {prefix} token: {token!r}")
        key, value = token.split("=", 1)
        if not key or not value or key in values:
            raise ProfileError(f"invalid {prefix} field: {token!r}")
        values[key] = value
    return values


def _typed(values: dict[str, str], integer_fields: Iterable[str],
           float_fields: Iterable[str] = ()) -> dict[str, object]:
    required = set(integer_fields) | set(float_fields)
    missing = required - values.keys()
    if missing:
        raise ProfileError(f"profile marker is missing: {', '.join(sorted(missing))}")
    result: dict[str, object] = dict(values)
    try:
        for field in integer_fields:
            result[field] = int(values[field])
        for field in float_fields:
            result[field] = float(values[field])
    except ValueError as exc:
        raise ProfileError(f"profile marker contains a non-numeric value: {exc}") from exc
    return result


def parse_log(lines: Iterable[str]) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    worlds: list[dict[str, object]] = []
    tsdf: list[dict[str, object]] = []
    latest_world: dict[str, object] | None = None
    for sequence, line in enumerate(lines, 1):
        world_values = _marker_values(line, WORLD_PREFIX)
        if world_values is not None:
            latest_world = _typed(world_values, WORLD_INTEGER_FIELDS)
            if latest_world.get("reason") not in {"start", "attach", "rollover", "periodic"}:
                raise ProfileError("world profile contains an unsupported reason")
            latest_world["sequence"] = sequence
            worlds.append(latest_world)
            continue
        tsdf_values = _marker_values(line, TSDF_PREFIX)
        if tsdf_values is not None:
            sample = _typed(tsdf_values, TSDF_INTEGER_FIELDS, TSDF_FLOAT_FIELDS)
            sample["sequence"] = sequence
            sample["chunks"] = latest_world.get("chunks") if latest_world else ""
            sample["residentVolumes"] = (
                latest_world.get("residentVolumes") if latest_world else ""
            )
            sample["allocatedBytes"] = (
                latest_world.get("allocatedBytes") if latest_world else ""
            )
            sample["reservedBytes"] = (
                latest_world.get("reservedBytes") if latest_world else ""
            )
            tsdf.append(sample)
    return worlds, tsdf


def _write_csv(path: Path, rows: list[dict[str, object]], fields: list[str]) -> None:
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def _pss_kib(path: Path) -> int | None:
    if not path.is_file():
        return None
    text = path.read_text(encoding="utf-8", errors="replace")
    match = re.search(r"TOTAL PSS:\s*([0-9]+)", text)
    if match:
        return int(match.group(1))
    match = re.search(r"^\s*TOTAL\s+([0-9]+)\s", text, re.MULTILINE)
    return int(match.group(1)) if match else None


def analyze(capture: Path, *, allow_empty: bool = False) -> dict[str, object]:
    log = capture / "app-logcat.txt"
    if not log.is_file():
        raise ProfileError(f"capture is missing {log.name}")
    worlds, tsdf = parse_log(log.read_text(encoding="utf-8", errors="replace").splitlines())
    if not allow_empty and (not worlds or not tsdf):
        raise ProfileError(
            f"capture requires both telemetry families; world={len(worlds)}, tsdf={len(tsdf)}"
        )

    _write_csv(capture / "world-profile.csv", worlds,
               ["sequence", "unixMs", "reason", *WORLD_INTEGER_FIELDS[1:]])
    _write_csv(capture / "tsdf-profile.csv", tsdf,
               ["sequence", "chunks", "residentVolumes", "allocatedBytes",
                "reservedBytes", *TSDF_INTEGER_FIELDS, *TSDF_FLOAT_FIELDS])

    bound_held = all(
        int(row["residentVolumes"]) <= int(row["maxResidentVolumes"])
        for row in worlds
    )
    summary: dict[str, object] = {
        "schemaVersion": 1,
        "worldSamples": len(worlds),
        "tsdfSamples": len(tsdf),
        "pairedTsdfSamples": sum(row["chunks"] != "" for row in tsdf),
        "chunksObserved": sorted({int(row["chunks"]) for row in worlds}),
        "maximumChunkCount": max((int(row["chunks"]) for row in worlds), default=0),
        "maximumResidentVolumes": max(
            (int(row["residentVolumes"]) for row in worlds), default=0
        ),
        "residentVolumeBoundHeld": bound_held,
        "peakUnityAllocatedBytes": max(
            (int(row["allocatedBytes"]) for row in worlds), default=0
        ),
        "peakUnityReservedBytes": max(
            (int(row["reservedBytes"]) for row in worlds), default=0
        ),
        "maximumCpuIntegrationAverageMs": max(
            (float(row["cpuIntegrationAvgMs"]) for row in tsdf), default=0.0
        ),
        "maximumCpuFrameAverageMs": max(
            (float(row["cpuFrameAvgMs"]) for row in tsdf), default=0.0
        ),
        "maximumGpuFrameAverageMs": max(
            (float(row["gpuFrameAvgMs"]) for row in tsdf), default=0.0
        ),
        "processPssKiBBefore": _pss_kib(capture / "process-memory-before.txt"),
        "processPssKiBAfter": _pss_kib(capture / "process-memory-after.txt"),
    }
    destination = capture / "performance-summary.json"
    with tempfile.NamedTemporaryFile(
        "w", encoding="utf-8", dir=capture, prefix=".performance-", delete=False
    ) as stream:
        json.dump(summary, stream, indent=2, sort_keys=True)
        stream.write("\n")
        temporary = Path(stream.name)
    os.replace(temporary, destination)
    return summary


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("capture", type=Path)
    parser.add_argument("--allow-empty", action="store_true")
    args = parser.parse_args()
    summary = analyze(args.capture.resolve(), allow_empty=args.allow_empty)
    print(json.dumps(summary, sort_keys=True))


if __name__ == "__main__":
    main()
