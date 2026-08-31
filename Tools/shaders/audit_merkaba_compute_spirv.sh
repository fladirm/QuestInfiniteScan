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
  M8WritebackQueue M8LoadStagingAddresses M8SurfaceCandidates
  M8SurfaceQueue M8CarveTiles M8VisibleTiles
)

kernel_count=0
for shader_name in MerkabaWorld.compute MerkabaIntegration.compute \
  MerkabaReadout.compute DepthNormals.compute DepthDilation.compute \
  StereoRgbdRefine.compute; do
  shader="$shader_dir/$shader_name"
  while read -r _ _ kernel; do
    spv="$audit_dir/$kernel.spv"
    assembly="$audit_dir/$kernel.spvasm"
    glslangValidator -D -V --target-env vulkan1.1 -S comp -e "$kernel" \
      -I"$shader_dir" "$shader" -o "$spv" >/dev/null
    spirv-val --target-env vulkan1.1 "$spv"
    spirv-dis "$spv" -o "$assembly"

    if [[ "$kernel" == "PreflightReadout" ||
          "$kernel" == "EmitReadoutVertices" ]]; then
      if grep -Eq 'OpTypeInt 64|Op[US](Div|Mod)|OpSRem' "$assembly"; then
        echo "FAIL: $kernel regressed to int64/integer div/mod" >&2
        exit 1
      fi
      if ! grep -Eq 'OpExecutionMode .* LocalSize 8 8 2' "$assembly"; then
        echo "FAIL: $kernel is not the frozen 128-lane tile workgroup" >&2
        exit 1
      fi
    fi

    if [[ "$kernel" == "IntegrateCarveTiles" ]]; then
      if ! grep -Eq 'OpExecutionMode .* LocalSize 128 1 1' "$assembly"; then
        echo "FAIL: $kernel is not the frozen 128-lane tile workgroup" >&2
        exit 1
      fi
      barrier_count=$(grep -c 'OpControlBarrier' "$assembly" || true)
      if (( barrier_count != 2 )); then
        echo "FAIL: $kernel must contain exactly two tile-group barriers" >&2
        exit 1
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
      exit 1
    fi

    for base in "${alias_bases[@]}"; do
      rw_count=$(awk -v pattern="^%_${base}(_[0-9]+)? = OpVariable .* StorageBuffer$" \
        '$0 ~ pattern { count++ } END { print count + 0 }' "$assembly")
      read_count=$(awk -v pattern="^%_${base}Read(_[0-9]+)? = OpVariable .* StorageBuffer$" \
        '$0 ~ pattern { count++ } END { print count + 0 }' "$assembly")
      if (( rw_count != 0 && read_count != 0 )); then
        echo "FAIL: $kernel contains RW/read alias pair for _$base" >&2
        exit 1
      fi
    done

    printf '%-38s buffers=%2d writable=%d images=%d readonly=%2d\n' \
      "$kernel" "$total" "$writable" "$writable_images" "$readonly"
    kernel_count=$((kernel_count + 1))
  done < <(rg '^#pragma kernel ' "$shader")
done

if (( kernel_count != 57 )); then
  echo "FAIL: audited $kernel_count kernels; expected 57" >&2
  exit 1
fi

echo "PASS: 57 Quest compute kernels validate; writable buffer/image storage <= 8; no RW/read alias pair"
