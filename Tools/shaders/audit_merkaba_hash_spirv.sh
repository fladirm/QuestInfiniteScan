#!/usr/bin/env bash
set -euo pipefail

tool_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$tool_dir/../.." && pwd)"
audit_dir="$(mktemp -d)"
trap 'rm -rf "$audit_dir"' EXIT

glslangValidator -D -V --target-env vulkan1.1 -S comp -e main \
  -I"$repo_root/Runtime/Shaders" \
  "$tool_dir/MerkabaHashAudit.comp.hlsl" \
  -o "$audit_dir/merkaba-hash.spv" >/dev/null
spirv-dis "$audit_dir/merkaba-hash.spv" \
  -o "$audit_dir/merkaba-hash.spvasm"

if rg -n 'OpTypeInt 64|Op[SU]Div|Op[SU](Mod|Rem)|OpLoopMerge' \
  "$audit_dir/merkaba-hash.spvasm"; then
  echo "FAIL: forbidden 64-bit/divide/modulo/loop instruction in M8 hash path" >&2
  exit 1
fi

echo "PASS: M8 PCG3D SPIR-V contains no int64, divide, modulo, or loop"
