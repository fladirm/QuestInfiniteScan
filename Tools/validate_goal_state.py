#!/usr/bin/env python3
"""Validate the lightweight Codex goal/DAG control plane using only stdlib."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DAG_PATH = ROOT / ".codex" / "TASK_DAG.json"
REQUIRED_FILES = (
    ROOT / "AGENTS.md",
    ROOT / ".codex" / "GOAL.md",
    ROOT / ".codex" / "STATE.md",
    DAG_PATH,
    ROOT / ".codex" / "SESSION_TAIL.md",
    ROOT / ".codex" / "DECISIONS.md",
)


def fail(message: str) -> None:
    print(f"control-plane error: {message}", file=sys.stderr)
    raise SystemExit(1)


def main() -> None:
    missing = [str(path.relative_to(ROOT)) for path in REQUIRED_FILES if not path.is_file()]
    if missing:
        fail(f"missing required files: {', '.join(missing)}")

    try:
        dag = json.loads(DAG_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"cannot parse {DAG_PATH.relative_to(ROOT)}: {exc}")

    if dag.get("schema_version") != 1:
        fail("unsupported task DAG schema_version")

    allowed = set(dag.get("allowed_statuses", []))
    expected = {"pending", "in_progress", "done", "blocked"}
    if allowed != expected:
        fail(f"allowed_statuses must be exactly {sorted(expected)}")

    nodes = dag.get("nodes")
    if not isinstance(nodes, list) or not nodes:
        fail("nodes must be a non-empty list")

    by_id: dict[str, dict] = {}
    for node in nodes:
        if not isinstance(node, dict):
            fail("every node must be an object")
        node_id = node.get("id")
        if not isinstance(node_id, str) or not node_id:
            fail("every node requires a non-empty string id")
        if node_id in by_id:
            fail(f"duplicate node id: {node_id}")
        if node.get("status") not in allowed:
            fail(f"{node_id} has invalid status: {node.get('status')!r}")
        if not isinstance(node.get("depends_on"), list):
            fail(f"{node_id}.depends_on must be a list")
        if not isinstance(node.get("acceptance"), list) or not node["acceptance"]:
            fail(f"{node_id}.acceptance must be a non-empty list")
        if not isinstance(node.get("evidence"), list):
            fail(f"{node_id}.evidence must be a list")
        if node["status"] == "done" and not node["evidence"]:
            fail(f"{node_id} is done without evidence")
        by_id[node_id] = node

    active = [node_id for node_id, node in by_id.items() if node["status"] == "in_progress"]
    if len(active) > 1:
        fail(f"more than one node is in_progress: {', '.join(active)}")

    for node_id, node in by_id.items():
        for dep in node["depends_on"]:
            if dep not in by_id:
                fail(f"{node_id} depends on unknown node {dep}")
            if node["status"] == "done" and by_id[dep]["status"] != "done":
                fail(f"{node_id} is done before dependency {dep}")

    visiting: set[str] = set()
    visited: set[str] = set()

    def visit(node_id: str) -> None:
        if node_id in visiting:
            fail(f"dependency cycle includes {node_id}")
        if node_id in visited:
            return
        visiting.add(node_id)
        for dep in by_id[node_id]["depends_on"]:
            visit(dep)
        visiting.remove(node_id)
        visited.add(node_id)

    for node_id in by_id:
        visit(node_id)

    print(
        f"control plane valid: {len(nodes)} nodes, "
        f"active={active[0] if active else 'none'}, "
        f"done={sum(node['status'] == 'done' for node in nodes)}"
    )
    graph_check = subprocess.run(
        [sys.executable, str(ROOT / "Tools" / "generate_code_graph.py"), "--check"],
        cwd=ROOT, check=False)
    if graph_check.returncode != 0:
        fail("code graph is stale; run python3 Tools/generate_code_graph.py")


if __name__ == "__main__":
    main()
