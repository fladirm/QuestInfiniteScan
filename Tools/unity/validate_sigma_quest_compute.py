#!/usr/bin/env python3
"""Static Quest-first contract gate for the Sigma N4.1R compute graph."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CONTRACT = ROOT / "Tools" / "sigma" / "quest_shader_contract.json"
SHADER_ROOT = ROOT / "Runtime" / "Resources" / "SigmaPrism"
BARRIER_NAMES = (
    "GroupMemoryBarrierWithGroupSync",
    "DeviceMemoryBarrierWithGroupSync",
)
FUNCTION_RE = re.compile(
    r"(?m)^\s*(?:\[[^\]\n]+\]\s*)*"
    r"(?:(?:public|private|internal|protected|static|sealed|virtual|override|"
    r"readonly|inline)\s+)*"
    r"(?:void|bool|int|uint|float|float[234](?:x[234])?|half|half[234]|"
    r"[A-Za-z_]\w*)[ \t]+(?P<name>[A-Za-z_]\w*)[ \t]*\("
)
CALL_RE = re.compile(r"\b(?P<name>[A-Za-z_]\w*)\s*\(")
KERNEL_RE = re.compile(r"(?m)^\s*#pragma\s+kernel\s+(?P<name>[A-Za-z_]\w*)")
NUMTHREADS_RE = re.compile(
    r"\[numthreads\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)\s*\]"
    r"\s*(?:\[[^\]]+\]\s*)*void\s+([A-Za-z_]\w*)\s*\(",
    re.DOTALL,
)
SHARED_RE = re.compile(
    r"(?m)^\s*groupshared\s+(?P<type>[A-Za-z_]\w*)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*\[\s*(?P<count>\d+)\s*\]\s*;"
)
TYPE_BYTES = {
    "bool": 4,
    "int": 4,
    "uint": 4,
    "float": 4,
    "half": 4,
    "int2": 8,
    "uint2": 8,
    "float2": 8,
    "half2": 8,
    "int3": 12,
    "uint3": 12,
    "float3": 12,
    "half3": 12,
    "int4": 16,
    "uint4": 16,
    "float4": 16,
    "half4": 16,
}
CONTROL_WORDS = {"if", "for", "while", "switch", "return"}


@dataclass(frozen=True)
class FunctionBody:
    name: str
    body: str
    line: int


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


def function_bodies(text: str) -> dict[str, FunctionBody]:
    result: dict[str, FunctionBody] = {}
    for match in FUNCTION_RE.finditer(text):
        name = match.group("name")
        if name in CONTROL_WORDS:
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
        result[name] = FunctionBody(name, text[opening + 1:closing],
                                    text.count("\n", 0, match.start()) + 1)
    return result


def calls(body: str, names: set[str]) -> set[str]:
    return {match.group("name") for match in CALL_RE.finditer(body)
            if match.group("name") in names}


def sync_reachable(functions: dict[str, FunctionBody]) -> set[str]:
    names = set(functions)
    result = {name for name, value in functions.items()
              if any(barrier in value.body for barrier in BARRIER_NAMES)}
    changed = True
    while changed:
        changed = False
        for name, value in functions.items():
            if name in result:
                continue
            if calls(value.body, names) & result:
                result.add(name)
                changed = True
    return result


def control_blocks(body: str):
    control = re.compile(r"\b(if|for|while)\s*\(")
    for match in control.finditer(body):
        close = matching(body, match.end() - 1, "(", ")")
        if close < 0:
            continue
        cursor = close + 1
        while cursor < len(body) and body[cursor].isspace():
            cursor += 1
        if cursor >= len(body):
            continue
        if body[cursor] == "{":
            end = matching(body, cursor, "{", "}")
            if end < 0:
                continue
            block = body[cursor + 1:end]
        else:
            end = body.find(";", cursor)
            if end < 0:
                continue
            block = body[cursor:end + 1]
        yield match.group(1), body[match.end():close], block, match.start()


def contains_sync_surface(block: str, sync_names: set[str]) -> bool:
    if any(barrier in block for barrier in BARRIER_NAMES):
        return True
    return any(re.search(rf"\b{re.escape(name)}\s*\(", block)
               for name in sync_names)


def preprocess(source: Path, defines: list[str]) -> str:
    command = [
        "glslangValidator", "-D", "-E", "-S", "comp",
        f"-I{SHADER_ROOT}",
    ]
    command.extend(f"-D{define}" for define in defines)
    command.append(str(source))
    completed = subprocess.run(command, cwd=ROOT, text=True,
                               stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, check=False)
    if completed.returncode != 0:
        raise RuntimeError(
            f"preprocess failed for {source.relative_to(ROOT)}: "
            f"{completed.stderr.strip()}"
        )
    return completed.stdout


def shared_bytes(preprocessed: str) -> tuple[int, list[str]]:
    total = 0
    unknown: list[str] = []
    for match in SHARED_RE.finditer(preprocessed):
        element = TYPE_BYTES.get(match.group("type"))
        if element is None:
            unknown.append(match.group(0).strip())
            continue
        total += element * int(match.group("count"))
    return total, unknown


def validate_dispatch_graph(contract: dict, failures: list[str]) -> None:
    graph_path = ROOT / contract["native_graph"]["graph_source"]
    text = strip_comments(graph_path.read_text(encoding="utf-8"))
    expected_count = contract["project_limits"]["native_hot_dispatch_count"]
    count_match = re.search(r"HotDispatchCount\s*=\s*(\d+)\s*;", text)
    if not count_match or int(count_match.group(1)) != expected_count:
        failures.append(f"HotDispatchCount must be exactly {expected_count}")
    functions = function_bodies(text)
    record = functions.get("RecordNativeCloseCommit")
    if record is None:
        failures.append("RecordNativeCloseCommit was not found")
        return
    actual = re.findall(
        r"command\.DispatchComputeProfiled\(\s*[^,]+,\s*(_[A-Za-z_]\w*)",
        record.body,
    )
    expected = contract["native_graph"]["dispatch_fields"]
    if actual != expected:
        failures.append(
            "native dispatch sequence differs:\n  expected=" +
            ", ".join(expected) + "\n  actual=" + ", ".join(actual)
        )


def validate_kernel_sets(contract: dict, failures: list[str]) -> set[str]:
    hot: set[str] = set()
    for relative, expected in contract["native_graph"]["shader_kernel_sets"].items():
        source = ROOT / relative
        text = source.read_text(encoding="utf-8")
        actual = [match.group("name") for match in KERNEL_RE.finditer(text)]
        if actual != expected:
            failures.append(
                f"{relative}: kernel set/order differs; expected={expected}, "
                f"actual={actual}"
            )
        hot.update(expected)
    return hot


def validate_thread_shapes(contract: dict, hot: set[str], failures: list[str]) -> None:
    limit = contract["project_limits"]["hot_threads_per_workgroup"]
    found: dict[str, tuple[int, int, int]] = {}
    for variant in contract["shader_variants"]:
        relative = variant["source"]
        text = strip_comments(preprocess(ROOT / relative, variant["defines"]))
        for match in NUMTHREADS_RE.finditer(text):
            shape = tuple(int(match.group(index)) for index in (1, 2, 3))
            found[match.group(4)] = shape
    for kernel in sorted(hot):
        shape = found.get(kernel)
        if shape is None:
            failures.append(f"{kernel}: numthreads declaration not found")
            continue
        invocations = shape[0] * shape[1] * shape[2]
        if invocations > limit:
            failures.append(
                f"{kernel}: {shape} = {invocations} threads exceeds {limit}"
            )


def validate_shared_memory(contract: dict, failures: list[str],
                           warnings: list[str], report: dict) -> None:
    hard = contract["project_limits"]["hard_groupshared_bytes"]
    review = contract["project_limits"]["occupancy_review_groupshared_bytes"]
    variants = []
    for variant in contract["shader_variants"]:
        source = ROOT / variant["source"]
        expanded = preprocess(source, variant["defines"])
        size, unknown = shared_bytes(expanded)
        variants.append({"variant": variant["name"], "groupshared_bytes": size})
        if unknown:
            failures.append(
                f"{variant['name']}: unknown groupshared declarations: {unknown}"
            )
        if size > hard:
            failures.append(
                f"{variant['name']}: groupshared {size} exceeds Quest hard {hard}"
            )
        elif size > review:
            warnings.append(
                f"{variant['name']}: groupshared {size} requires occupancy/device "
                f"review (preferred <= {review}, hard {hard})"
            )
    report["groupshared"] = variants


def validate_sync_control(contract: dict, failures: list[str]) -> None:
    tokens = contract["synchronization_contract"]["runtime_control_symbols"]
    uniform = set(contract["synchronization_contract"].get(
        "proved_workgroup_uniform_symbols", []))
    runtime_re = re.compile("|".join(re.escape(token) for token in tokens))
    for relative in contract["native_graph"]["shader_kernel_sets"]:
        source = ROOT / relative
        text = strip_comments(source.read_text(encoding="utf-8"))
        functions = function_bodies(text)
        sync_names = sync_reachable(functions)
        for function in functions.values():
            for kind, expression, block, offset in control_blocks(function.body):
                if not contains_sync_surface(block, sync_names):
                    continue
                if kind == "while":
                    failures.append(
                        f"{relative}:{function.line}: while-loop reaches group sync"
                    )
                if runtime_re.search(expression):
                    line = function.line + function.body.count("\n", 0, offset)
                    failures.append(
                        f"{relative}:{line}: runtime-dependent {kind} controls a "
                        "group synchronization surface"
                    )
            if function.name not in sync_names:
                continue
            for match in re.finditer(
                    r"if\s*\((?P<condition>[^)]*)\)\s*"
                    r"(?:\{\s*)?return\b", function.body):
                condition = match.group("condition")
                controls = {token for token in tokens if token in condition}
                if controls and not controls.issubset(uniform):
                    line = function.line + function.body.count(
                        "\n", 0, match.start())
                    failures.append(
                        f"{relative}:{line}: runtime-dependent early exit in "
                        "sync-reachable function"
                    )


def validate_generated_names(contract: dict, failures: list[str]) -> None:
    generated = (SHADER_ROOT / "Generated" /
                 "SigmaGeneratedMerkabaProgram.hlsl")
    text = strip_comments(generated.read_text(encoding="utf-8"))
    forbidden = set(contract["compiler_contract"]["forbidden_generated_identifiers"])
    prefixes = tuple(contract["compiler_contract"]["generated_symbol_prefixes"])
    functions = set(function_bodies(text))
    constants = set(re.findall(
        r"(?m)^\s*static\s+const\s+[A-Za-z_]\w*\s+"
        r"([A-Za-z_]\w*)\s*(?:\[|=)", text))
    macros = set(re.findall(r"(?m)^\s*#define\s+([A-Za-z_]\w*)", text))
    for name in sorted(functions | constants | macros):
        if name in forbidden:
            failures.append(f"generated HLSL uses reserved identifier {name}")
        if not name.startswith(prefixes):
            failures.append(f"generated HLSL symbol lacks Sigma prefix: {name}")


def validate_uavs(contract: dict, failures: list[str]) -> None:
    sys.path.insert(0, str(ROOT / "Tools" / "unity"))
    from validate_sigma_compute_uav import validate  # pylint: disable=import-outside-toplevel
    failures.extend(validate(
        SHADER_ROOT,
        contract["project_limits"]["max_uav_bindings_per_kernel"],
    ))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--contract", type=Path, default=DEFAULT_CONTRACT)
    parser.add_argument("--json-report", type=Path)
    parser.add_argument("--warnings-as-errors", action="store_true")
    args = parser.parse_args()

    contract = json.loads(args.contract.read_text(encoding="utf-8"))
    failures: list[str] = []
    warnings: list[str] = []
    report: dict = {
        "contract": contract["contract"],
        "failures": failures,
        "warnings": warnings,
    }
    validate_dispatch_graph(contract, failures)
    hot = validate_kernel_sets(contract, failures)
    validate_thread_shapes(contract, hot, failures)
    validate_shared_memory(contract, failures, warnings, report)
    validate_sync_control(contract, failures)
    validate_generated_names(contract, failures)
    validate_uavs(contract, failures)

    if args.warnings_as_errors:
        failures.extend(f"warning promoted: {warning}" for warning in warnings)
    if args.json_report:
        args.json_report.parent.mkdir(parents=True, exist_ok=True)
        args.json_report.write_text(json.dumps(report, indent=2) + "\n",
                                    encoding="utf-8")
    for warning in warnings:
        print(f"warning: {warning}", file=sys.stderr)
    if failures:
        print("Sigma Quest compute contract validation failed:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1
    print(
        f"Sigma Quest compute contract passed: {len(hot)} kernels, "
        f"{contract['project_limits']['native_hot_dispatch_count']} dispatches."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
