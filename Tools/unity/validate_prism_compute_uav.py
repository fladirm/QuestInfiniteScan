#!/usr/bin/env python3
"""Reject Prism compute kernels that exceed Quest Vulkan's eight UAV slots."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SHADER_ROOT = ROOT / "Runtime" / "Resources" / "Prism"
DEFAULT_LIMIT = 8

FUNCTION_RE = re.compile(
    r"(?m)^\s*(?:\[[^\]\n]+\]\s*)?"
    r"(?:void|bool|int|uint|float|float[234](?:x[234])?|half|half[234]|"
    r"[A-Za-z_]\w*)[ \t]+(?P<name>[A-Za-z_]\w*)[ \t]*\("
)
KERNEL_RE = re.compile(r"(?m)^\s*#pragma\s+kernel\s+(?P<name>[A-Za-z_]\w*)")
UAV_RE = re.compile(
    r"(?m)^\s*(?:globallycoherent\s+)?(?:RW[A-Za-z0-9_]*|"
    r"AppendStructuredBuffer|ConsumeStructuredBuffer)\s*<[^;\n]+>\s*"
    r"(?P<name>_[A-Za-z_]\w*)\s*;"
)
CALL_RE = re.compile(r"\b(?P<name>[A-Za-z_]\w*)\s*\(")
CONTROL_WORDS = {"if", "for", "while", "switch", "return"}


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)
    return re.sub(r"//[^\n]*", "", text)


def matching(text: str, opening: int, left: str, right: str) -> int:
    depth = 0
    for index in range(opening, len(text)):
        if text[index] == left:
            depth += 1
        elif text[index] == right:
            depth -= 1
            if depth == 0:
                return index
    return -1


def function_bodies(text: str) -> dict[str, str]:
    bodies: dict[str, str] = {}
    for match in FUNCTION_RE.finditer(text):
        if match.group("name") in CONTROL_WORDS:
            continue
        close_paren = matching(text, match.end() - 1, "(", ")")
        if close_paren < 0:
            continue
        opening = text.find("{", close_paren + 1)
        semicolon = text.find(";", close_paren + 1)
        if opening < 0 or (semicolon >= 0 and semicolon < opening):
            continue
        closing = matching(text, opening, "{", "}")
        if closing < 0:
            continue
        bodies[match.group("name")] = text[opening + 1:closing]
    return bodies


def reachable_uavs(kernel: str, bodies: dict[str, str], uavs: set[str]) -> set[str]:
    pending = [kernel]
    visited: set[str] = set()
    used: set[str] = set()
    while pending:
        name = pending.pop()
        if name in visited:
            continue
        visited.add(name)
        body = bodies.get(name, "")
        used.update(resource for resource in uavs if re.search(
            rf"\b{re.escape(resource)}\b", body))
        pending.extend(call.group("name") for call in CALL_RE.finditer(body)
                       if call.group("name") in bodies)
    return used


def validate(shader_root: Path, limit: int) -> list[str]:
    failures: list[str] = []
    for path in sorted(shader_root.glob("*.compute")):
        text = strip_comments(path.read_text(encoding="utf-8"))
        bodies = function_bodies(text)
        uavs = {match.group("name") for match in UAV_RE.finditer(text)}
        for match in KERNEL_RE.finditer(text):
            kernel = match.group("name")
            used = reachable_uavs(kernel, bodies, uavs)
            if len(used) > limit:
                names = ", ".join(sorted(used))
                failures.append(
                    f"{path.relative_to(ROOT)}::{kernel}: {len(used)} UAVs "
                    f"(limit {limit}): {names}"
                )
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--shader-root", type=Path, default=DEFAULT_SHADER_ROOT)
    parser.add_argument("--limit", type=int, default=DEFAULT_LIMIT)
    args = parser.parse_args()
    failures = validate(args.shader_root, args.limit)
    if failures:
        print("Prism compute UAV validation failed:", file=sys.stderr)
        print("\n".join(f"- {failure}" for failure in failures), file=sys.stderr)
        return 1
    print(f"Prism compute UAV validation passed (limit={args.limit}).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
