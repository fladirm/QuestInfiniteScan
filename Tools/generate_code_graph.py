#!/usr/bin/env python3
"""Generate deterministic file/type/function/GPU/DAG maps for Cone-PRISM-Q3."""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import re
import sys
from collections import defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
JSON_PATH = ROOT / ".codex" / "CODE_GRAPH.json"
MARKDOWN_PATH = ROOT / "docs" / "architecture" / "CODE_GRAPH.md"
DAG_PATH = ROOT / ".codex" / "TASK_DAG.json"

SOURCE_PATTERNS = (
    "Runtime/Prism/**/*.cs",
    "Runtime/Resources/Prism/*.*",
    "Runtime/World/*.cs",
    "Runtime/Export/*.cs",
    "Runtime/Core/RoomScanner.cs",
    "Editor/RoomScanSetupWizard.cs",
    "Tools/*.py",
    "Tests/Editor/Prism*.cs",
)

CONTROL_SOURCES = (
    "specka.md",
    "AGENTS.md",
    ".codex/GOAL.md",
    ".codex/STATE.md",
    ".codex/TASK_DAG.json",
    ".codex/DECISIONS.md",
    ".codex/SESSION_TAIL.md",
)

TASK_GLOBS = {
    "Q3-01": ("specka.md", "AGENTS.md", ".codex/*"),
    "Q3-02": ("Runtime/Prism/Capture/*", "Runtime/Capture/*"),
    "Q3-03": ("Runtime/Prism/Calibration/*", "*DepthNormalize*"),
    "Q3-04": ("Runtime/Prism/Preprocessing/*", "*DepthConsensus*"),
    "Q3-05": ("*Prediction*", "*PredictContactFilm*", "*ContactMeshletBuffers*"),
    "Q3-06": ("Runtime/Prism/Association/*", "*ConeClassify*"),
    "Q3-07": ("*ContactFilmPool*", "*PrismFilmSpawner*", "*ContactFilmSpawn*"),
    "Q3-08": ("*PrismFilmUpdater*", "*ContactFilmUpdate*"),
    "Q3-09": ("*Prediction*", "*ConeClassify*", "*ContactFilmSpawn*", "*ContactFilmUpdate*"),
    "Q3-10": ("*Boundary*",),
    "Q3-11": ("*Topology*", "*Displacement*"),
    "Q3-12": ("*Meshlet*", "*PredictContactFilm*"),
    "Q3-13": ("Runtime/World/*", "*PrismCanonical*", "*PrismGpuSnapshot*", "*PrismChunkPublisher*"),
    "Q3-14": ("*Stereo*",),
    "Q3-15": ("*Temporal*", "*Keyframe*"),
    "Q3-16": ("*Displacement*", "*NormalRefine*"),
    "Q3-17": ("*Texture*", "*AppearancePage*"),
    "Q3-18": ("*Superresolution*", "*Texture*"),
    "Q3-19": ("*Appearance*", "*Pbr*", "*PBR*"),
    "Q3-20": ("*PoseGraph*", "*OverlapConstraint*"),
    "Q3-21": ("Runtime/Export/*",),
    "Q3-22": ("*Benchmark*", "*Diagnostics*", "*Telemetry*"),
}

TYPE_RE = re.compile(
    r"(?m)^\s*(?:(?:public|private|protected|internal|static|sealed|abstract|partial|readonly)\s+)*"
    r"(?P<kind>class|struct|enum|interface)\s+(?P<name>[A-Za-z_]\w*)"
)
CS_METHOD_RE = re.compile(
    r"(?m)^\s*(?:(?:public|private|protected|internal|static|async|virtual|override|sealed|"
    r"partial|extern|new|unsafe|readonly)\s+)+"
    r"(?:[A-Za-z_][\w.<>,?\[\]]*\s+)+(?P<name>[A-Za-z_]\w*)\s*\("
)
HLSL_FUNCTION_RE = re.compile(
    r"(?m)^\s*(?:\[[^\]\n]+\]\s*)?(?:void|bool|int|uint|float|float[234](?:x[234])?|"
    r"half|half[234]|[A-Za-z_]\w*)\s+(?P<name>[A-Za-z_]\w*)\s*\("
)
PY_FUNCTION_RE = re.compile(r"(?m)^\s*(?:async\s+)?def\s+(?P<name>[A-Za-z_]\w*)\s*\(")
KERNEL_RE = re.compile(r"(?m)^\s*#pragma\s+kernel\s+(?P<name>[A-Za-z_]\w*)")
EVENT_RE = re.compile(
    r"(?m)(?P<source>[A-Za-z_]\w*)\.(?P<event>[A-Za-z_]\w*)\s*\+=\s*(?P<handler>[A-Za-z_]\w*)"
)
FIELD_RE = re.compile(
    r"(?m)^\s*(?:(?:public|private|protected|internal|static|readonly|const|volatile|"
    r"\[SerializeField[^\]]*\])\s+)*(?P<type>[A-Za-z_]\w*(?:<[^;=]+>)?(?:\[\])?)\s+"
    r"(?P<name>_[A-Za-z_]\w*|[a-z][A-Za-z0-9_]*)\s*(?:[;=])"
)


def source_files() -> list[Path]:
    found: set[Path] = set()
    for pattern in SOURCE_PATTERNS:
        found.update(path for path in ROOT.glob(pattern) if path.is_file() and
                     path.suffix != ".meta")
    found.update(ROOT / relative for relative in CONTROL_SOURCES
                 if (ROOT / relative).is_file())
    return sorted(found, key=lambda path: path.relative_to(ROOT).as_posix())


def line_of(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def matching_brace(text: str, opening: int) -> int:
    depth = 0
    for index in range(opening, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return index
    return len(text)


def is_hlsl_definition(text: str, match_end: int) -> bool:
    """Reject control statements and calls accidentally matched as declarations."""
    depth = 1
    index = match_end
    while index < len(text) and depth:
        character = text[index]
        if character == "(":
            depth += 1
        elif character == ")":
            depth -= 1
        index += 1
    if depth:
        return False
    while index < len(text) and text[index].isspace():
        index += 1
    if index < len(text) and text[index] == ":":
        opening = text.find("{", index)
        semicolon = text.find(";", index)
        return opening >= 0 and (semicolon < 0 or opening < semicolon)
    return index < len(text) and text[index] == "{"


def parse_symbols(path: Path, text: str) -> list[dict]:
    relative = path.relative_to(ROOT).as_posix()
    symbols: list[dict] = []
    for match in TYPE_RE.finditer(text):
        symbols.append({
            "id": f"{relative}::{match.group('name')}",
            "file": relative,
            "name": match.group("name"),
            "kind": match.group("kind"),
            "line": line_of(text, match.start()),
        })
    if path.suffix == ".cs":
        function_re = CS_METHOD_RE
        function_kind = "method"
    elif path.suffix == ".py":
        function_re = PY_FUNCTION_RE
        function_kind = "function"
    elif path.suffix in {".compute", ".hlsl", ".shader"}:
        function_re = HLSL_FUNCTION_RE
        function_kind = "gpu_function"
    else:
        function_re = re.compile(r"(?!)")
        function_kind = "function"
    seen: set[tuple[str, int]] = set()
    for match in function_re.finditer(text):
        if function_kind == "gpu_function" and not is_hlsl_definition(
                text, match.end()):
            continue
        name = match.group("name")
        line = line_of(text, match.start())
        if (name, line) in seen:
            continue
        seen.add((name, line))
        opening = text.find("{", match.end())
        semicolon = text.find(";", match.end())
        arrow = text.find("=>", match.end())
        body = ""
        if opening >= 0 and (semicolon < 0 or opening < semicolon) and \
                (arrow < 0 or opening < arrow):
            body = text[opening + 1:matching_brace(text, opening)]
        symbols.append({
            "id": f"{relative}::{name}@{line}",
            "file": relative,
            "name": name,
            "kind": function_kind,
            "line": line,
            "body": body,
        })
    for match in KERNEL_RE.finditer(text):
        symbols.append({
            "id": f"{relative}::kernel:{match.group('name')}",
            "file": relative,
            "name": match.group("name"),
            "kind": "gpu_kernel",
            "line": line_of(text, match.start()),
        })
    return symbols


def task_files(task_id: str, paths: list[str]) -> list[str]:
    patterns = TASK_GLOBS.get(task_id, ())
    return [path for path in paths if any(fnmatch.fnmatch(path, pattern) or
            fnmatch.fnmatch(Path(path).name, pattern) for pattern in patterns)]


def build_graph() -> tuple[dict, str]:
    paths = source_files()
    texts = {path.relative_to(ROOT).as_posix(): path.read_text(
        encoding="utf-8", errors="replace") for path in paths}
    digest = hashlib.sha256()
    for path, text in texts.items():
        digest.update(path.encode())
        digest.update(b"\0")
        digest.update(text.encode())
        digest.update(b"\0")
    digest.update(DAG_PATH.read_bytes())

    all_symbols: list[dict] = []
    for path in paths:
        relative = path.relative_to(ROOT).as_posix()
        all_symbols.extend(parse_symbols(path, texts[relative]))
    symbol_names: dict[str, list[str]] = defaultdict(list)
    for symbol in all_symbols:
        symbol_names[symbol["name"]].append(symbol["id"])

    edges: set[tuple[str, str, str]] = set()
    for symbol in all_symbols:
        body = symbol.pop("body", "")
        if not body:
            continue
        for called in re.findall(r"\b([A-Za-z_]\w*)\s*\(", body):
            targets = symbol_names.get(called, ())
            if len(targets) == 1 and targets[0] != symbol["id"]:
                edges.add((symbol["id"], targets[0], "calls"))

    type_by_name = {symbol["name"]: symbol["id"] for symbol in all_symbols
                    if symbol["kind"] in {"class", "struct", "enum", "interface"}}
    event_links: list[dict] = []
    for path, text in texts.items():
        if Path(path).suffix != ".cs":
            continue
        fields = {match.group("name"): re.sub(r"<.*", "", match.group("type"))
                  for match in FIELD_RE.finditer(text)}
        for match in EVENT_RE.finditer(text):
            statement_end = text.find(";", match.end())
            if statement_end < 0 or text[match.end():statement_end].strip():
                continue
            source_type = fields.get(match.group("source"))
            if source_type is None and not match.group("source")[0].isupper():
                continue
            handler_targets = [symbol["id"] for symbol in all_symbols
                               if symbol["file"] == path and
                               symbol["name"] == match.group("handler")]
            event_links.append({
                "file": path,
                "source_variable": match.group("source"),
                "source_type": source_type,
                "event": match.group("event"),
                "handler": match.group("handler"),
                "line": line_of(text, match.start()),
            })
            if source_type in type_by_name and len(handler_targets) == 1:
                edges.add((type_by_name[source_type], handler_targets[0],
                           f"event:{match.group('event')}"))

    dag = json.loads(DAG_PATH.read_text(encoding="utf-8"))
    path_names = sorted(texts)
    tasks = []
    for task in dag["nodes"]:
        mapped = task_files(task["id"], path_names)
        tasks.append({
            "id": task["id"],
            "title": task["title"],
            "status": task["status"],
            "depends_on": task["depends_on"],
            "files": mapped,
        })
        for path in mapped:
            edges.add((f"task:{task['id']}", f"file:{path}", "implemented_by"))

    graph = {
        "schema_version": 1,
        "source_digest": digest.hexdigest(),
        "scope": list(SOURCE_PATTERNS),
        "summary": {
            "files": len(texts),
            "symbols": len(all_symbols),
            "methods_and_functions": sum(symbol["kind"] in
                {"method", "function", "gpu_function"} for symbol in all_symbols),
            "gpu_kernels": sum(symbol["kind"] == "gpu_kernel" for symbol in all_symbols),
            "event_links": len(event_links),
        },
        "files": [{"id": f"file:{path}", "path": path,
                   "language": Path(path).suffix.lstrip(".")} for path in path_names],
        "symbols": sorted(all_symbols, key=lambda item: item["id"]),
        "event_links": sorted(event_links, key=lambda item:
                              (item["file"], item["line"])),
        "tasks": tasks,
        "edges": [{"source": source, "target": target, "kind": kind}
                  for source, target, kind in sorted(edges)],
    }
    return graph, render_markdown(graph)


def render_markdown(graph: dict) -> str:
    summary = graph["summary"]
    by_file: dict[str, list[dict]] = defaultdict(list)
    for symbol in graph["symbols"]:
        by_file[symbol["file"]].append(symbol)
    lines = [
        "# Cone-PRISM-Q3 code graph",
        "",
        "<!-- Generated by Tools/generate_code_graph.py; do not edit manually. -->",
        "",
        f"Source digest: `{graph['source_digest']}`",
        "",
        f"Scope: {summary['files']} files, {summary['symbols']} symbols, "
        f"{summary['methods_and_functions']} functions/methods, "
        f"{summary['gpu_kernels']} GPU kernels, {summary['event_links']} event links.",
        "",
        "## Runtime data flow",
        "",
        "```mermaid",
        "flowchart LR",
        "  Capture[PrismRigCapture] -->|StereoRigFrame| Pre[PrismDepthPreprocessor]",
        "  Pre -->|NormalizedRigFrame| Predict[PrismPredictionRenderer]",
        "  Predict -->|2-layer MRT| Classify[PrismConeClassifier]",
        "  Classify -->|ConeEvents| Spawn[PrismFilmSpawner]",
        "  Spawn --> Update[PrismFilmUpdater]",
        "  Update --> Boundary[PrismBoundaryGraph]",
        "  Boundary --> Meshlet[PrismMeshletBuilder]",
        "  Meshlet -->|indirect meshlets| Predict",
        "  Update -. canonical pages .-> Store[WorldStore / .prism]",
        "  Meshlet -. derived PBR mesh .-> GLB[ChunkGlbWriter / WorldGlbWriter]",
        "```",
        "",
        "## DAG to code",
        "",
        "| Run | Status | Mapped files |",
        "|---|---|---:|",
    ]
    for task in graph["tasks"]:
        lines.append(f"| {task['id']} | {task['status']} | {len(task['files'])} |")
    lines.extend(["", "## Event links", ""])
    if graph["event_links"]:
        for link in graph["event_links"]:
            source = link["source_type"] or link["source_variable"]
            lines.append(f"- `{source}.{link['event']}` → `{link['handler']}` "
                         f"([{link['file']}:{link['line']}](../../{link['file']}#L{link['line']}))")
    else:
        lines.append("- None detected.")
    lines.extend(["", "## Files and symbols", ""])
    for path in sorted(by_file):
        symbols = by_file[path]
        lines.append(f"### `{path}`")
        lines.append("")
        rendered = ", ".join(
            f"`{symbol['name']}` ({symbol['kind']}, L{symbol['line']})"
            for symbol in symbols)
        lines.append(rendered or "No parsed symbols.")
        lines.append("")
    lines.extend([
        "## Machine-readable graph",
        "",
        "Full nodes, approximate intra-repository call edges, event links, and task/file "
        "ownership are in [`.codex/CODE_GRAPH.json`](../../.codex/CODE_GRAPH.json).",
        "",
    ])
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true",
                        help="fail when generated outputs are stale")
    args = parser.parse_args()
    graph, markdown = build_graph()
    json_text = json.dumps(graph, indent=2, ensure_ascii=False) + "\n"
    if args.check:
        stale = []
        if not JSON_PATH.is_file() or JSON_PATH.read_text(encoding="utf-8") != json_text:
            stale.append(str(JSON_PATH.relative_to(ROOT)))
        if not MARKDOWN_PATH.is_file() or \
                MARKDOWN_PATH.read_text(encoding="utf-8") != markdown:
            stale.append(str(MARKDOWN_PATH.relative_to(ROOT)))
        if stale:
            print("code graph stale: " + ", ".join(stale), file=sys.stderr)
            raise SystemExit(1)
        print(f"code graph current: {graph['source_digest'][:12]} "
              f"({graph['summary']['files']} files)")
        return
    JSON_PATH.parent.mkdir(parents=True, exist_ok=True)
    MARKDOWN_PATH.parent.mkdir(parents=True, exist_ok=True)
    JSON_PATH.write_text(json_text, encoding="utf-8")
    MARKDOWN_PATH.write_text(markdown, encoding="utf-8")
    print(f"generated {JSON_PATH.relative_to(ROOT)} and "
          f"{MARKDOWN_PATH.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
