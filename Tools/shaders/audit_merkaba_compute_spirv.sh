#!/usr/bin/env bash
set -euo pipefail

tool_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$tool_dir/../.." && pwd)"
shader_dir="$repo_root/Runtime/Shaders"
audit_dir="$(mktemp -d)"
trap 'rm -rf -- "$audit_dir"' EXIT

for tool in glslangValidator spirv-dis spirv-val; do
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
  M8WritebackQueue M8LoadStagingAddresses M8VisibleTiles
  M8DualBlockState M8DualChunkState M8DualLeaves
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
  "$shader_dir/MerkabaDepthCertificate.compute"
  "$shader_dir/StereoRgbdRefine.compute"
  "$repo_root/Tests/Editor/MerkabaSphereFlowerOracle.compute"
  "$repo_root/Tests/Editor/MerkabaSphereFlowerDataAbi.compute"
  "$repo_root/Tests/Editor/MerkabaObservationBinsProbe.compute"
  "$repo_root/Tests/Editor/MerkabaDepthCertificateProbe.compute"
)

kernel_count=0
failed_kernel_count=0
expected_kernel_count=$(rg -c '^#pragma kernel ' "${shaders[@]}" |
  awk -F: '{ count += $NF } END { print count + 0 }')
for shader in "${shaders[@]}"; do
  while read -r _ _ kernel; do
    kernel_count=$((kernel_count + 1))
    kernel_failed=0
    spv="$audit_dir/$kernel.spv"
    assembly="$audit_dir/$kernel.spvasm"
    if ! glslangValidator -D -V --target-env vulkan1.1 -S comp -e "$kernel" \
      -I"$shader_dir" "$shader" -o "$spv" >"$audit_dir/compile.log" 2>&1; then
      echo "FAIL: $shader kernel $kernel did not compile" >&2
      cat "$audit_dir/compile.log" >&2
      failed_kernel_count=$((failed_kernel_count + 1))
      continue
    fi
    if ! spirv-val --target-env vulkan1.1 "$spv" ||
       ! spirv-dis "$spv" -o "$assembly"; then
      echo "FAIL: $kernel SPIR-V validation/disassembly failed" >&2
      failed_kernel_count=$((failed_kernel_count + 1))
      continue
    fi

    if [[ "$kernel" == "BuildReadoutVertices" ]]; then
      if grep -Eq 'OpTypeInt 64|Op[US](Div|Mod)|OpSRem' "$assembly"; then
        echo "FAIL: $kernel regressed to int64/integer div/mod" >&2
        kernel_failed=1
      fi
      if ! grep -Eq 'OpExecutionMode .* LocalSize 128 1 1' "$assembly"; then
        echo "FAIL: $kernel is not the frozen 128-lane tile workgroup" >&2
        kernel_failed=1
      fi
      barrier_count=$(grep -c 'OpControlBarrier' "$assembly" || true)
      if (( barrier_count > 8 )); then
        echo "FAIL: $kernel contains $barrier_count group barriers (>8)" >&2
        kernel_failed=1
      fi
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

    total=$(awk '/OpVariable .* StorageBuffer$/ { count++ }
      END { print count + 0 }' "$assembly")
    readonly=$(awk '
      /OpDecorate %[^ ]+ NonWritable$/ { read_only[$2] = 1 }
      /OpVariable .* StorageBuffer$/ { variables[$1] = 1 }
      END {
        for (variable in variables)
          if (read_only[variable]) count++
        print count + 0
      }' "$assembly")
    writable_buffers=$((total - readonly))
    writable_images=$(awk '
      $3 == "OpTypeImage" && $9 == "2" { storage_image[$1] = 1 }
      $3 == "OpTypePointer" && $4 == "UniformConstant" &&
        storage_image[$5] { storage_pointer[$1] = 1 }
      $3 == "OpVariable" && $5 == "UniformConstant" &&
        storage_pointer[$4] { count++ }
      END { print count + 0 }
    ' "$assembly")
    writable=$((writable_buffers + writable_images))
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

    printf '%-38s buffers=%2d writable=%d images=%d readonly=%2d\n' \
      "$kernel" "$total" "$writable" "$writable_images" "$readonly"
    failed_kernel_count=$((failed_kernel_count + kernel_failed))
  done < <(rg '^#pragma kernel ' "$shader")
done

if (( kernel_count != expected_kernel_count )); then
  echo "FAIL: audited $kernel_count kernels; expected $expected_kernel_count" >&2
  exit 1
fi

if (( failed_kernel_count != 0 )); then
  echo "FAIL: $failed_kernel_count of $kernel_count Quest compute kernels failed the unchanged validation/binding limits" >&2
  exit 1
fi

echo "PASS: $kernel_count Quest compute kernels validate; writable buffer/image storage <= 8; no RW/read alias pair"
