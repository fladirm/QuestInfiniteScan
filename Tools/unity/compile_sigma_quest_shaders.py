#!/usr/bin/env python3
"""Compile the exact N4.1R production variants to Vulkan 1.1 SPIR-V."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CONTRACT = ROOT / "Tools" / "sigma" / "quest_shader_contract.json"
SHADER_ROOT = ROOT / "Runtime" / "Resources" / "SigmaPrism"


def require(name: str) -> str:
    path = shutil.which(name)
    if path is None:
        raise RuntimeError(f"required tool is missing: {name}")
    return path


def run(command: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, cwd=ROOT, text=True,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          check=False)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--contract", type=Path, default=DEFAULT_CONTRACT)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--keep-output", type=Path)
    args = parser.parse_args()

    glslang = require("glslangValidator")
    spirv_val = require("spirv-val")
    spirv_dis = require("spirv-dis")
    contract = json.loads(args.contract.read_text(encoding="utf-8"))
    temporary = None
    if args.keep_output:
        output_root = args.keep_output.resolve()
        output_root.mkdir(parents=True, exist_ok=True)
    else:
        temporary = tempfile.TemporaryDirectory(prefix="sigma-quest-spv-")
        output_root = Path(temporary.name)

    failures: list[str] = []
    records: list[dict] = []
    for variant in contract["shader_variants"]:
        source = ROOT / variant["source"]
        for entry in variant["entry_points"]:
            label = f"{variant['name']}__{entry}"
            output = output_root / f"{label}.spv"
            command = [
                glslang, "-D", "-V", "--target-env", "vulkan1.1",
                "-S", "comp", "-e", entry, f"-I{SHADER_ROOT}",
                "-DSHADER_API_VULKAN=1",
            ]
            command.extend(f"-D{define}" for define in variant["defines"])
            command.extend([str(source), "-o", str(output)])
            compiled = run(command)
            if compiled.returncode != 0:
                failures.append(
                    f"{label}: glslang failed\n{compiled.stdout}{compiled.stderr}"
                )
                continue
            validated = run([
                spirv_val, "--target-env", "vulkan1.1", str(output),
            ])
            if validated.returncode != 0:
                failures.append(
                    f"{label}: spirv-val failed\n"
                    f"{validated.stdout}{validated.stderr}"
                )
                continue
            disassembled = run([spirv_dis, str(output)])
            local_size = "unknown"
            for line in disassembled.stdout.splitlines():
                if "OpExecutionMode" in line and "LocalSize" in line:
                    local_size = " ".join(line.split("LocalSize", 1)[1].split())
                    break
            payload = output.read_bytes()
            records.append({
                "variant": variant["name"],
                "entry_point": entry,
                "source": variant["source"],
                "defines": variant["defines"],
                "bytes": len(payload),
                "sha256": hashlib.sha256(payload).hexdigest(),
                "local_size": local_size,
            })
            print(f"PASS {label}: {len(payload)} bytes, local={local_size}")

    manifest = {
        "contract": contract["contract"],
        "target_env": "vulkan1.1",
        "compiled_entry_points": len(records),
        "records": records,
        "failures": failures,
    }
    if args.manifest:
        args.manifest.parent.mkdir(parents=True, exist_ok=True)
        args.manifest.write_text(json.dumps(manifest, indent=2) + "\n",
                                 encoding="utf-8")
    if temporary is not None:
        temporary.cleanup()
    if failures:
        print("Sigma Quest shader compilation failed:", file=sys.stderr)
        for failure in failures:
            print(failure, file=sys.stderr)
        return 1
    print(f"Compiled and validated {len(records)} exact production variants.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
