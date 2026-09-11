#!/usr/bin/env bash
set -euo pipefail

tool_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$tool_dir/../.." && pwd)"
shader_dir="$repo_root/Runtime/Shaders"
generator="$repo_root/Tools/unity/generate_merkaba_native_executor_shaders.py"
if (( $# == 0 )); then
  audit_dir="$(mktemp -d)"
  trap 'rm -rf -- "$audit_dir"' EXIT
elif (( $# == 2 )) && [[ "$1" == "--output-dir" && -n "$2" ]]; then
  audit_dir="$2"
  mkdir -p -- "$audit_dir"
  audit_dir="$(cd -- "$audit_dir" && pwd)"
elif (( $# == 1 )) && [[ "$1" == "--help" ]]; then
  echo "Usage: $0 [--output-dir DIR]"
  echo "Audit exact native compile flags/payloads; retain per-entry SPIR-V, JSON metrics and logs in DIR."
  echo "Production: >512 KiB/20000 body instructions REVIEW; >1 MiB/50000 FAIL."
  echo "Oracle sizes reported separately. All entries: >16 KiB GS REVIEW; >32 KiB or >8 writable FAIL."
  exit 0
else
  echo "Usage: $0 [--output-dir DIR]" >&2
  exit 2
fi

for tool in python3 rg glslangValidator spirv-opt spirv-dis spirv-val; do
  command -v "$tool" >/dev/null || {
    echo "FAIL: required shader audit tool is missing: $tool" >&2
    exit 1
  }
done

alias_bases=(
  M8HashEntries M8OwnerRecords M8BlockChunkRefs
  M8BlockPresenceL0 M8BlockPresenceL1 M8BlockPresenceL2
  M8ChunkTileRefs M8ChunkPresence M8KernelStates0 M8KernelStates1
  M8KernelStates2 M8KernelStates3 M8TileBits M8TileRecords
  M8FreeTileStack M8Counters M8ClaimQueue M8PendingNewTileRefs
  M8WritebackQueue M8LoadStagingAddresses
  M8ObservationTileBins M8TouchedTileQueue
  M8FlowerDetailPages M8ThreadAtlasPages
  M8FlowerSymbolArena M8FlowerPageDirectory M8FlowerIndirectCommands
)

shaders=(
  "$shader_dir/MerkabaWorld.compute"
  "$shader_dir/MerkabaIntegration.compute"
  "$shader_dir/MerkabaObservationBins.compute"
  "$shader_dir/MerkabaReadout.compute"
  "$shader_dir/DepthNormals.compute"
  "$shader_dir/StereoRgbdRefine.compute"
  "$repo_root/Tests/Editor/MerkabaSphereFlowerOracle.compute"
  "$repo_root/Tests/Editor/MerkabaSphereFlowerDataAbi.compute"
  "$repo_root/Tests/Editor/MerkabaObservationBinsProbe.compute"
  "$repo_root/Tests/Editor/MerkabaDepthCertificateProbe.compute"
)

kernel_count=0
failed_kernel_count=0
production_count=0
oracle_count=0
native_count=0
expected_kernel_count=$(rg -c '^#pragma kernel ' "${shaders[@]}" |
  awk -F: '{ count += $NF } END { print count + 0 }')
for shader in "${shaders[@]}"; do
  while read -r _ _ kernel; do
    kernel_count=$((kernel_count + 1))
    kernel_failed=0
    entry_dir="$audit_dir/$(basename "$shader" .compute)/$kernel"
    mkdir -p -- "$entry_dir"
    spv="$entry_dir/pipeline-0.spv"
    assembly="$entry_dir/module.spvasm"
    metrics="$entry_dir/metrics.json"
    # The native generator owns the flags and final storage-image format patch.
    # Its native PIPELINES are also checked against the emitted descriptor ABI.
    # Native entries include the SAME PC post-link cleanup as APK embedding;
    # the audit never optimizes a different payload just to reduce its metric.
    # Other runtime/oracle entries do not pretend to be an embedded pipeline.
    if ! python3 "$generator" --audit-shader "$shader" --audit-entry "$kernel" \
      --artifact-dir "$entry_dir" >"$entry_dir/compile.log" 2>&1; then
      echo "FAIL: $shader kernel $kernel did not compile" >&2
      cat "$entry_dir/compile.log" >&2
      failed_kernel_count=$((failed_kernel_count + 1))
      continue
    fi
    if ! spirv-val --target-env vulkan1.1 "$spv" ||
       ! spirv-dis "$spv" -o "$assembly"; then
      echo "FAIL: $kernel SPIR-V validation/disassembly failed" >&2
      failed_kernel_count=$((failed_kernel_count + 1))
      continue
    fi

    if [[ "$kernel" == "SphereFlowerOracle" ||
          "$kernel" == "SphereFlowerSkinAddressOracle" ]]; then
      if ! grep -Eq 'OpExecutionMode .* LocalSize 64 1 1' "$assembly"; then
        echo "FAIL: $kernel is not the frozen 64-lane parity workgroup" >&2
        kernel_failed=1
      fi
      if grep -Eq 'OpTypeFloat 64' "$assembly"; then
        echo "FAIL: $kernel contains a float64 runtime path" >&2
        kernel_failed=1
      fi
    fi

    if [[ "$kernel" == "SphereFlowerOracle" ]]; then
      precise_count=$(grep -c 'OpDecorate .* NoContraction' "$assembly" || true)
      if (( precise_count < 8 )); then
        echo "FAIL: $kernel lost precise arithmetic ($precise_count decorations)" >&2
        kernel_failed=1
      fi
    fi

    # The shared binary reflector accounts for NonWritable on variables,
    # block types AND members. Do not infer private storage from static const.
    if ! measurement=$(python3 - "$metrics" <<'PY'
import json
import sys
from pathlib import Path

report = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
keys = ("storage_buffers", "readonly_storage_buffers", "writable_storage_bindings",
        "writable_storage_images", "spirv_bytes", "function_body_instructions",
        "groupshared_payload_bytes", "constant_instruction_bytes", "barriers",
        "metric_gate", "profile", "sha256")
values = [str(report[key]) for key in keys]
values.extend(("x".join(map(str, report["local_size"])),
               "native" if report["native_embedding_payload"] else "auxiliary"))
print(" ".join(values))
for level, key in (("FAIL", "failures"), ("REVIEW", "reviews")):
    for issue in report[key]:
        print(f"{level}: {report['profile']} {report['entrypoint']}: {issue}", file=sys.stderr)
PY
    ); then
      echo "FAIL: $kernel emitted-module metrics could not be read" >&2
      failed_kernel_count=$((failed_kernel_count + 1))
      continue
    fi
    read -r total readonly writable writable_images bytes body_instructions \
      groupshared constant_bytes barriers metric_gate profile sha256 local_size artifact_kind <<< "$measurement"
    if [[ "$profile" == "oracle" ]]; then
      oracle_count=$((oracle_count + 1))
    else
      production_count=$((production_count + 1))
    fi
    if [[ "$artifact_kind" == "native" ]]; then native_count=$((native_count + 1)); fi
    if [[ "$metric_gate" == "FAIL" ]]; then kernel_failed=1; fi
    if (( writable > 8 )); then
      echo "FAIL: $kernel has $writable writable storage bindings (>8)" >&2
      kernel_failed=1
    fi

    for base in "${alias_bases[@]}"; do
      rw_count=$(awk -v pattern="^%_${base}(_[0-9]+)? = OpVariable .* StorageBuffer$" \
        '$0 ~ pattern { count++ } END { print count + 0 }' "$assembly")
      read_count=$(awk -v pattern="^%_${base}Read(_[0-9]+)? = OpVariable .* StorageBuffer$" \
        '$0 ~ pattern { count++ } END { print count + 0 }' "$assembly")
      if (( rw_count != 0 && read_count != 0 )); then
        echo "FAIL: $kernel contains RW/read alias pair for _$base" >&2
        kernel_failed=1
      fi
    done

    # Load transfer has an explicit writable view of the existing staging
    # allocation. Other storage stages use its original read-only view.
    load_read=$(awk '/^%_M8LoadStagingStates(_[0-9]+)? = OpVariable .* StorageBuffer$/ { count++ }
      END { print count + 0 }' "$assembly")
    load_write=$(awk '/^%_M8LoadStagingStatesWrite(_[0-9]+)? = OpVariable .* StorageBuffer$/ { count++ }
      END { print count + 0 }' "$assembly")
    if (( load_read != 0 && load_write != 0 )); then
      echo "FAIL: $kernel contains read/write alias pair for _M8LoadStagingStates" >&2
      kernel_failed=1
    fi

    printf '%-38s profile=%s payload=%s gate=%s bytes=%d body=%d GS=%d LocalSize=%s buffers=%d RW=%d RWimages=%d RO=%d barriers=%d constantSPV=%d sha256=%s\n' \
      "$kernel" "$profile" "$artifact_kind" "$metric_gate" "$bytes" "$body_instructions" \
      "$groupshared" "$local_size" "$total" "$writable" "$writable_images" "$readonly" \
      "$barriers" "$constant_bytes" "$sha256"
    failed_kernel_count=$((failed_kernel_count + kernel_failed))
  done < <(rg '^#pragma kernel ' "$shader")
done

if (( kernel_count != expected_kernel_count )); then
  echo "FAIL: audited $kernel_count kernels; expected $expected_kernel_count" >&2
  exit 1
fi

if (( failed_kernel_count != 0 )); then
  echo "FAIL: $failed_kernel_count of $kernel_count kernels failed validation, binding or production engineering gates (production=$production_count native=$native_count oracle=$oracle_count)" >&2
  exit 1
fi

echo "PASS: $kernel_count kernels validate; production=$production_count native=$native_count oracle=$oracle_count; production size/body gates pass; GS <=32 KiB; writable storage <=8; no RW/read alias pair. REVIEW remains advisory; this is not device/RUN_08 acceptance."
