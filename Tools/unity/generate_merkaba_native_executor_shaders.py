#!/usr/bin/env python3
"""Embed the exact production M8 scanner/readout SPIR-V and reflected ABI.

The native queue executor compiles the same HLSL entry points as Unity.  All
descriptor bindings and global-uniform offsets are reflected here so the
plugin never maintains a second handwritten shader ABI.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import struct
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SHADER_ROOT = ROOT / "Runtime" / "Shaders"
# Apply the same ID compaction on PC for embedding AND audit. Preserve
# glslang's instruction/control-flow graph: on Adreno 740 driver 0x80345009,
# extra redundancy-elimination breaks StereoFlowerRefine and merge-blocks
# breaks CompactDirtyFlowerSymbols (-13). Both original graphs link on that
# device without a cache. spirv-val alone cannot detect these driver failures.
SPIRV_CLEANUP = ("--preserve-bindings", "--compact-ids")


@dataclass(frozen=True)
class Pipeline:
    label: str
    source: str
    entry: str
    dispatch: str
    resource_overrides: tuple[tuple[str, str], ...] = ()


PIPELINES = (
    Pipeline("StereoFlowerRefine", "StereoRgbdRefine.compute",
             "StereoFlowerRefine", "refine"),
    Pipeline("BuildDepthCertificate", "MerkabaDepthCertificate.compute",
             "BuildDepthCertificate", "certificate_local"),
    Pipeline("ReduceDepthCertificate", "MerkabaDepthCertificate.compute",
             "ReduceDepthCertificate", "certificate_root"),
    Pipeline("ResetObservationBins", "MerkabaObservationBins.compute",
             "ResetObservationBins", "one"),
    Pipeline("CountObservationBins", "MerkabaObservationBins.compute",
             "CountObservationBins", "depth"),
    Pipeline("ResolveMissingSpatialNodes", "MerkabaObservationBins.compute",
             "ResolveMissingSpatialNodes", "allocation_gate"),
    Pipeline("ResolveObservationTileRequests", "MerkabaObservationBins.compute",
             "ResolveObservationTileRequests", "allocation_gate"),
    Pipeline("InitializeNewTiles", "MerkabaWorld.compute",
             "InitializeNewTiles", "allocation_tiles"),
    Pipeline("ReserveObservationBins", "MerkabaObservationBins.compute",
             "ReserveObservationBins", "one"),
    Pipeline("EmitObservationBins", "MerkabaObservationBins.compute",
             "EmitObservationBins", "depth"),
    Pipeline("UpdateObservationDual", "MerkabaIntegration.compute",
             "UpdateObservationDual", "query"),
    Pipeline("FlowerCommit", "MerkabaIntegration.compute",
             "FlowerCommit", "observation_indirect"),
    Pipeline("DrainFlowerGeometry", "MerkabaIntegration.compute",
             "DrainFlowerGeometry", "observation_indirect"),
    Pipeline("AdvanceRefinementStage", "MerkabaIntegration.compute",
             "AdvanceRefinementStage", "one"),
    Pipeline("ResolveFlowerCarriers", "MerkabaIntegration.compute",
             "ResolveFlowerCarriers", "observation_indirect"),
    Pipeline("DrainFlowerSkinRgb", "MerkabaIntegration.compute",
             "DrainFlowerSkinRgb", "signal_items"),
    Pipeline("DrainFlowerSkinV", "MerkabaIntegration.compute",
             "DrainFlowerSkinV", "signal_items"),
    Pipeline("FinalizeObservation", "MerkabaIntegration.compute",
             "FinalizeObservation", "one"),
    Pipeline("ClassifyHotFlowerPages", "MerkabaReadout.compute",
             "ClassifyHotFlowerPages", "flower_slots"),
    Pipeline("PrepareDirtyFlowerBatch", "MerkabaReadout.compute",
             "PrepareDirtyFlowerBatch", "one"),
    Pipeline("CompactDirtyFlowerSymbols", "MerkabaReadout.compute",
             "CompactDirtyFlowerSymbols", "flower_batch"),
    Pipeline("ReserveDirtyFlowerBatch", "MerkabaReadout.compute",
             "ReserveDirtyFlowerBatch", "one"),
    Pipeline("PublishDirtyFlowerPages", "MerkabaReadout.compute",
             "PublishDirtyFlowerPages", "one"),
    Pipeline("CullFlowerPages", "MerkabaReadout.compute",
             "CullFlowerPages", "flower_slots"),
    Pipeline("QueryFineEraseTiles", "MerkabaIntegration.compute",
             "QueryFineEraseTiles", "query"),
    Pipeline("EraseFineTiles", "MerkabaIntegration.compute",
             "EraseFineTiles", "observation_indirect"),
    Pipeline("FinalizeFineErase", "MerkabaIntegration.compute",
             "FinalizeFineErase", "one"),
)


def command_schedules():
    """The exact serialized command order embedded in the native executor."""
    labels = [pipeline.label for pipeline in PIPELINES]
    index = {label: ordinal for ordinal, label in enumerate(labels)}
    if len(index) != len(labels):
        raise RuntimeError("duplicate native pipeline identity")
    allocation = labels[index["ResolveMissingSpatialNodes"]:index["ReserveObservationBins"]]
    # One snapshot is one bounded synchronous scan transaction. Everything the
    # snapshot owes is a barrier INSIDE this one graph: there is no second
    # attempt at the same frame, no retry, no continuation and no workset that
    # outlives FinalizeObservation. The order is written out rather than
    # filtered out of the pipeline list, because the order IS the contract.
    observation = [
        # Acquire the evidence. A new observation resets the bins in eye zero
        # of certificate reduction.
        "StereoFlowerRefine", "BuildDepthCertificate", "ReduceDepthCertificate",
        # Counting discovers the owners whose tiles do not exist yet. The
        # allocator creates them and the SAME snapshot counts again against a
        # complete world - the round trip ObservationRetry used to perform as a
        # second frame, done here as two barriers. Emit consumes that count.
        "CountObservationBins", *allocation,
        "ResetObservationBins", "CountObservationBins",
        "ReserveObservationBins", "EmitObservationBins",
        # Excavate the complementary view, then reserve what its requests need.
        "UpdateObservationDual", "ReserveObservationBins",
        # Commit the direct evidence, then refine: root -> L1 -> L2 -> skin.
        # Each advance is its own one-group dispatch, because a workgroup
        # cannot publish a stage its own sibling groups may not have read yet.
        "FlowerCommit",
        "DrainFlowerGeometry", "AdvanceRefinementStage",
        "DrainFlowerGeometry", "AdvanceRefinementStage",
        "DrainFlowerGeometry", "AdvanceRefinementStage",
        "ResolveFlowerCarriers", "DrainFlowerSkinRgb", "DrainFlowerSkinV",
        # The drain may have requested residency for what it could not read.
        *allocation,
        # Publish the canonical generation, mark the dirty pages, RELEASE.
        "FinalizeObservation",
    ]
    flower = ["ClassifyHotFlowerPages", "PrepareDirtyFlowerBatch",
              "CompactDirtyFlowerSymbols", "ReserveDirtyFlowerBatch",
              "CompactDirtyFlowerSymbols", "PublishDirtyFlowerPages", "CullFlowerPages"]
    fine = labels[index["QueryFineEraseTiles"]:]
    return tuple((name, tuple(index[label] for label in schedule)) for name, schedule in (
        ("Observation", observation),
        ("FlowerReadout", flower), ("FineErase", fine)))


RESOURCE_NAMES = (
    "HashEntries", "OwnerRecords", "BlockChunkRefs", "BlockPresenceL0",
    "BlockPresenceL1", "BlockPresenceL2", "ChunkTileRefs",
    "ChunkPresence", "KernelStates0", "KernelStates1", "KernelStates2",
    "KernelStates3", "TileBits", "TileRecords", "FreeTileStack",
    "Counters", "ClaimQueue", "PendingNewTileRefs", "LoadRequests",
    "LoadRequestReadCount",
    "TouchedTileQueue", "ObservationDispatchArgs", "AttemptCompletion",
    "RefineMetrics", "RawDepth", "RefinedDepth", "Normals", "CameraLeft", "CameraRight",
    "FrameDispatchArgs", "ObservationRecords", "ObservationTileBins", "TileHalo", "DepthCertificate",
    "DualBlockState", "DualChunkState", "DualLeaves",
    "FlowerDetailPages", "ThreadAtlasPages", "FlowerSymbolArena", "FlowerPageDirectory", "FlowerIndirectCommands",
    "FlowerTables", "FlowerSignalItems",
)
RESOURCE_IDS = {name: index for index, name in enumerate(RESOURCE_NAMES)}


ALIASES = {
    **{f"_M8{name}": name for name in (
        "HashEntries", "OwnerRecords", "BlockChunkRefs", "BlockPresenceL0",
        "BlockPresenceL1", "BlockPresenceL2", "ChunkTileRefs",
        "ChunkPresence", "KernelStates0", "KernelStates1", "KernelStates2",
        "KernelStates3", "TileBits", "TileRecords", "FreeTileStack",
        "Counters", "ClaimQueue", "PendingNewTileRefs", "LoadRequests",
        "LoadRequestReadCount",
        "TouchedTileQueue", "ObservationDispatchArgs", "AttemptCompletion",
        "FrameDispatchArgs", "ObservationRecords",
        "ObservationTileBins", "TileHalo", "DepthCertificate",
        "DualBlockState", "DualChunkState", "DualLeaves",
        "FlowerDetailPages", "ThreadAtlasPages", "FlowerSymbolArena", "FlowerPageDirectory", "FlowerIndirectCommands", "FlowerTables", "FlowerSignalItems")},
    "_RefineMetrics": "RefineMetrics",
    "_SrcDepth": "RawDepth",
    "_DstDepth": "RefinedDepth",
    "_DstNormal": "Normals",
    "gsDepthTex": "RefinedDepth",
    "gsDepthNormalTex": "Normals",
    "_MerkabaCameraRgbLeft": "CameraLeft",
    "_MerkabaCameraRgbRight": "CameraRight",
}

for base in tuple(name for name in ALIASES if name.startswith("_M8")):
    ALIASES[base + "Read"] = ALIASES[base]

KIND_STORAGE_BUFFER = 0
KIND_SAMPLED_IMAGE = 1
KIND_STORAGE_IMAGE = 2
KIND_UNIFORM_BUFFER = 3
KIND_BILINEAR_SAMPLER = 4
KIND_POINT_SAMPLER = 5


def require(name: str) -> str:
    path = shutil.which(name)
    if path is None:
        raise RuntimeError(f"required tool is missing: {name}")
    return path


def run(command: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, cwd=ROOT, text=True,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          check=False)


def parse_int(line: str, label: str) -> int:
    match = re.search(rf"\b{label} (-?\d+)", line)
    if match is None:
        raise RuntimeError(f"reflection line has no {label}: {line}")
    return int(match.group(1))


def resource_id(pipeline: Pipeline, shader_name: str) -> int:
    overrides = dict(pipeline.resource_overrides)
    semantic = overrides.get(shader_name, ALIASES.get(shader_name))
    if semantic is None:
        raise RuntimeError(
            f"unmapped native resource {pipeline.label}: {shader_name}")
    return RESOURCE_IDS[semantic]


def descriptor_kind(name: str, type_code: str) -> int:
    if name in ("gsBilinearClampSampler", "m8CameraBilinearClampSampler"):
        return KIND_BILINEAR_SAMPLER
    if name == "gsPointClampSampler":
        return KIND_POINT_SAMPLER
    if type_code.lower() == "904d":
        return KIND_STORAGE_IMAGE
    return KIND_SAMPLED_IMAGE


def patch_storage_image_formats(words: tuple[int, ...], descriptors):
    """Declare the exact Unity RenderTexture storage formats in SPIR-V.

    glslang emits every HLSL RWTexture2D<float4> as rgba32f, while the exact
    production normal resource is RGBA8_SNORM.
    Arithmetic remains the production float code; only OpTypeImage's required
    Vulkan view format is corrected.
    """
    mutable = list(words)
    bindings: dict[int, int] = {}
    variables: dict[int, int] = {}
    pointers: dict[int, int] = {}
    image_offsets: dict[int, int] = {}
    offset = 5
    while offset < len(mutable):
        instruction = mutable[offset]
        count = instruction >> 16
        opcode = instruction & 0xffff
        if count == 0 or offset + count > len(mutable):
            raise RuntimeError("malformed SPIR-V instruction stream")
        if opcode == 71 and count >= 4 and mutable[offset + 2] == 33:
            bindings[mutable[offset + 1]] = mutable[offset + 3]
        elif opcode == 59 and count >= 4:
            variables[mutable[offset + 2]] = mutable[offset + 1]
        elif opcode == 32 and count >= 4:
            pointers[mutable[offset + 1]] = mutable[offset + 3]
        elif opcode == 25 and count >= 9:
            image_offsets[mutable[offset + 1]] = offset
        offset += count

    required = {
        RESOURCE_IDS["Normals"]: 5,      # SpvImageFormatRgba8Snorm
        RESOURCE_IDS["RefinedDepth"]: 3, # SpvImageFormatR32f
    }
    storage_by_binding = {
        binding: resource for binding, kind, resource in descriptors
        if kind == KIND_STORAGE_IMAGE
    }
    patched: set[int] = set()
    for variable, binding in bindings.items():
        resource = storage_by_binding.get(binding)
        if resource not in required:
            continue
        pointer = variables.get(variable)
        image_type = pointers.get(pointer)
        image_offset = image_offsets.get(image_type)
        if image_offset is None:
            raise RuntimeError(
                f"storage binding {binding} has no OpTypeImage")
        mutable[image_offset + 8] = required[resource]
        patched.add(resource)
    expected = set(storage_by_binding.values()) & set(required)
    if patched != expected:
        raise RuntimeError(
            f"storage image format patch mismatch: {patched} != {expected}")
    return tuple(mutable)


def compile_command(glslang: str, pipeline: Pipeline, output: Path) -> list[str]:
    """One flag source for native embedding and the existing compute audit."""
    return [
        glslang, "-D", "-V", "--target-env", "vulkan1.1", "-S", "comp",
        "-e", pipeline.entry, f"-I{SHADER_ROOT}",
        "-DSHADER_API_VULKAN=1", "--auto-map-bindings", "-l", "-q",
        str(SHADER_ROOT / pipeline.source), "-o", str(output),
    ]


def compile_pipeline(glslang: str, spirv_val: str, temporary: Path,
                     index: int, pipeline: Pipeline):
    output = temporary / f"pipeline-{index}.spv"
    command = compile_command(glslang, pipeline, output)
    compiled = run(command)
    # glslang can return zero when its SPIR-V optimizer reports a malformed
    # module. Reject that diagnostic before attempting descriptor reflection.
    if compiled.returncode != 0 or re.search(r"(?im)^error:", compiled.stderr):
        raise RuntimeError(f"{pipeline.label}: glslang failed\n" +
                           compiled.stdout + compiled.stderr)
    payload = output.read_bytes()
    if len(payload) % 4:
        raise RuntimeError(f"{pipeline.label}: malformed SPIR-V size")
    words = struct.unpack(f"<{len(payload) // 4}I", payload)
    # Source reflection can report a resource-valued function parameter while
    # omitting the live buffer passed to it. Reflect buffer identity from the
    # emitted descriptor variable, not that parameter or its shared block type.
    names: dict[int, str] = {}
    bindings: dict[int, int] = {}
    descriptor_sets: dict[int, int] = {}
    variables: dict[int, tuple[int, int]] = {}
    pointers: dict[int, int] = {}
    offset = 5
    while offset < len(words):
        count, opcode = words[offset] >> 16, words[offset] & 0xffff
        if count == 0 or offset + count > len(words):
            raise RuntimeError(f"{pipeline.label}: malformed SPIR-V instruction")
        if opcode == 5 and count >= 3:  # OpName
            encoded = struct.pack(f"<{count - 2}I",
                                  *words[offset + 2:offset + count])
            names[words[offset + 1]] = encoded.split(b"\0", 1)[0].decode("utf-8")
        elif opcode == 71 and count == 4:  # OpDecorate
            if words[offset + 2] == 33:  # Binding
                bindings[words[offset + 1]] = words[offset + 3]
            elif words[offset + 2] == 34:  # DescriptorSet
                descriptor_sets[words[offset + 1]] = words[offset + 3]
        elif opcode == 59 and count >= 4:  # OpVariable
            variables[words[offset + 2]] = (words[offset + 1], words[offset + 3])
        elif opcode == 32 and count == 4:  # OpTypePointer
            pointers[words[offset + 1]] = words[offset + 3]
        offset += count
    live_bindings = set(bindings.values())
    if len(live_bindings) != len(bindings):
        raise RuntimeError(f"{pipeline.label}: duplicate emitted binding")
    descriptors: list[tuple[int, int, int]] = []
    global_binding = -1
    for variable, binding in bindings.items():
        if variable not in variables or descriptor_sets.get(variable) != 0:
            raise RuntimeError(f"{pipeline.label}: invalid native descriptor {binding}")
        pointer, storage_class = variables[variable]
        if storage_class == 12:  # StorageBuffer
            descriptors.append((binding, KIND_STORAGE_BUFFER,
                                resource_id(pipeline, names.get(variable, ""))))
        elif storage_class == 2:  # Uniform
            if names.get(pointers.get(pointer)) != "$Global" or global_binding >= 0:
                raise RuntimeError(f"{pipeline.label}: unsupported uniform buffer {binding}")
            global_binding = binding
            descriptors.append((binding, KIND_UNIFORM_BUFFER, -1))
    uniform_lines: list[str] = []
    uniforms: list[tuple[str, int]] = []
    global_size = 0
    global_index = -1
    section = ""
    for line in compiled.stdout.splitlines():
        if line.endswith("reflection:"):
            section = line
            continue
        if section == "Uniform block reflection:" and ": offset " in line:
            name = line.split(": offset", 1)[0]
            binding = parse_int(line, "binding")
            if binding not in live_bindings:
                continue
            if name == "$Global" and binding == global_binding:
                global_size = parse_int(line, "size")
                global_index = parse_int(line, "index")
        elif section == "Uniform reflection:" and ": offset " in line:
            name = line.split(": offset", 1)[0]
            offset = parse_int(line, "offset")
            if offset >= 0:
                uniform_lines.append(line)
                continue
            binding = parse_int(line, "binding")
            if binding not in live_bindings:
                continue
            kind = descriptor_kind(name,
                                   re.search(r"\btype ([0-9a-fA-F]+)", line).group(1))
            resource = -1 if kind in (KIND_BILINEAR_SAMPLER,
                                      KIND_POINT_SAMPLER) else resource_id(
                                          pipeline, name)
            descriptors.append((binding, kind, resource))

    for line in uniform_lines:
        if parse_int(line, "index") != global_index:
            continue
        name = line.split(": offset", 1)[0]
        uniforms.append((name, parse_int(line, "offset")))
    if global_binding >= 0 and (global_index < 0 or global_size <= 0):
        raise RuntimeError(f"{pipeline.label}: emitted globals missing from reflection")
    descriptors.sort()
    uniforms = sorted(set(uniforms), key=lambda item: (item[1], item[0]))
    if len({item[0] for item in descriptors}) != len(descriptors):
        raise RuntimeError(f"{pipeline.label}: duplicate reflected binding")
    if {item[0] for item in descriptors} != live_bindings:
        raise RuntimeError(f"{pipeline.label}: emitted descriptor missing from reflection")
    words = patch_storage_image_formats(words, descriptors)
    output.write_bytes(struct.pack(f"<{len(words)}I", *words))
    optimized = temporary / f"pipeline-{index}-optimized.spv"
    cleanup = run([require("spirv-opt"), "--target-env=vulkan1.1",
                   *SPIRV_CLEANUP, str(output), "-o", str(optimized)])
    if cleanup.returncode != 0:
        raise RuntimeError(f"{pipeline.label}: SPIR-V cleanup failed\n" +
                           cleanup.stdout + cleanup.stderr)
    optimized.replace(output)
    payload = output.read_bytes()
    words = struct.unpack(f"<{len(payload) // 4}I", payload)
    validated = run([spirv_val, "--target-env", "vulkan1.1", str(output)])
    if validated.returncode != 0:
        raise RuntimeError(f"{pipeline.label}: spirv-val failed\n" +
                           validated.stdout + validated.stderr)
    return words, descriptors, uniforms, global_size


def spirv_metrics(words: tuple[int, ...], entry: str) -> dict:
    """Measure the final binary, without another optimizer or source estimate.

    Body count excludes OpFunction/OpFunctionEnd, including the instructions
    between them (parameters and labels included). Constant bytes mean encoded
    SPIR-V constant instructions, NOT per-invocation register allocation.
    Workgroup bytes count live declared payload, not driver occupancy/spills.
    """
    if len(words) < 5 or words[0] != 0x07230203:
        raise RuntimeError("invalid SPIR-V header")
    types, constants, decorations, member_decorations = {}, {}, {}, {}
    variables, names, entries, local_sizes = {}, {}, {}, {}
    body = False
    instruction_count = body_count = constant_bytes = control = memory = precise = 0
    offset = 5
    while offset < len(words):
        count, opcode = words[offset] >> 16, words[offset] & 0xffff
        if count == 0 or offset + count > len(words):
            raise RuntimeError("malformed SPIR-V instruction stream")
        operands = words[offset + 1:offset + count]
        instruction_count += 1
        if opcode == 54:  # OpFunction
            body = True
        elif opcode == 56:  # OpFunctionEnd
            body = False
        elif body:
            body_count += 1
        if opcode in (41, 42, 43, 44, 45, 46, 48, 49, 50, 51, 52):
            constant_bytes += count * 4
        if opcode == 43:  # Integer array lengths use ordinary OpConstant.
            constants[operands[1]] = operands[2:]
        elif opcode == 5:
            names[operands[0]] = struct.pack(
                f"<{len(operands) - 1}I", *operands[1:]).split(b"\0", 1)[0].decode("utf-8")
        elif opcode == 15:  # OpEntryPoint, including trailing interface IDs.
            name = struct.pack(f"<{len(operands) - 2}I", *operands[2:]).split(b"\0", 1)[0]
            entries[name.decode("utf-8")] = operands[1]
        elif opcode == 16 and operands[1] == 17:  # LocalSize
            local_sizes[operands[0]] = list(operands[2:5])
        elif opcode == 71:
            decorations.setdefault(operands[0], {})[operands[1]] = operands[2:]
            precise += operands[1] == 42  # NoContraction
        elif opcode == 72:
            member_decorations.setdefault((operands[0], operands[1]), {})[operands[2]] = operands[3:]
        elif 19 <= opcode <= 39:
            types[operands[0]] = (opcode, operands[1:])
        elif opcode == 59:  # OpVariable
            variables[operands[1]] = (operands[0], operands[2])
        control += opcode == 224
        memory += opcode == 225
        offset += count
    local_size = local_sizes.get(entries.get(entry))
    if local_size is None:
        raise RuntimeError(f"{entry}: no fixed emitted LocalSize")

    sizes = {}

    def type_bytes(type_id: int) -> int:
        if type_id in sizes:
            return sizes[type_id]
        opcode, operands = types[type_id]
        if opcode == 20:  # Physical boolean payload, when present.
            size = 4
        elif opcode in (21, 22):
            size = operands[0] // 8
        elif opcode in (23, 24):  # Vector/matrix.
            size = type_bytes(operands[0]) * operands[1]
        elif opcode == 28:
            length_words = constants.get(operands[1])
            if length_words is None:
                raise RuntimeError("unresolved Workgroup array length")
            length = sum(word << (32 * index) for index, word in enumerate(length_words))
            stride = decorations.get(type_id, {}).get(6, (type_bytes(operands[0]),))[0]
            size = stride * length
        elif opcode == 30:
            size = 0
            for index, member in enumerate(operands):
                layout = member_decorations.get((type_id, index), {})
                member_size = type_bytes(member)
                if 7 in layout:  # MatrixStride: count the declared major vectors.
                    member_op, member_args = types[member]
                    if member_op != 24:
                        raise RuntimeError("unsupported Workgroup matrix layout")
                    vectors = types[member_args[0]][1][1] if 4 in layout else member_args[1]
                    member_size = layout[7][0] * vectors
                size = max(size, layout.get(35, (size,))[0] + member_size)
        else:
            raise RuntimeError(f"unsupported Workgroup type opcode {opcode}")
        sizes[type_id] = size
        return size

    def nonwritable(type_id: int) -> bool:
        if 24 in decorations.get(type_id, {}):
            return True
        opcode, operands = types[type_id]
        if opcode in (28, 29):
            return nonwritable(operands[0])
        return opcode == 30 and bool(operands) and all(
            24 in member_decorations.get((type_id, index), {}) or nonwritable(member)
            for index, member in enumerate(operands))

    workgroup = total_buffers = readonly_buffers = writable_images = 0
    bindings = []
    for variable, (pointer, storage) in variables.items():
        _, pointer_args = types[pointer]
        pointee = pointer_args[1]
        if storage == 4:  # Workgroup: only variables surviving native compilation.
            workgroup += type_bytes(pointee)
        decoration = decorations.get(variable, {})
        if 33 not in decoration:
            continue
        readonly = 24 in decoration or nonwritable(pointee)
        opcode, operands = types[pointee]
        kind = "other"
        if storage == 12 or (storage == 2 and 3 in decorations.get(pointee, {})):
            kind = "storage_buffer"
            total_buffers += 1
            readonly_buffers += readonly
        elif opcode == 25 and operands[5] == 2:
            kind = "storage_image"
            writable_images += not readonly
        elif storage == 2:
            kind = "uniform_buffer"
            readonly = True
        elif opcode in (25, 27):
            kind = "sampled_image"
            readonly = True
        elif opcode == 26:
            kind = "sampler"
            readonly = True
        bindings.append({"set": decoration.get(34, (0,))[0], "binding": decoration[33][0],
                         "name": names.get(variable, str(variable)), "kind": kind,
                         "readonly": readonly})
    payload = struct.pack(f"<{len(words)}I", *words)
    return {
        "entrypoint": entry, "sha256": hashlib.sha256(payload).hexdigest(),
        "spirv_bytes": len(payload), "spirv_bound": words[3],
        "module_instructions": instruction_count, "function_body_instructions": body_count,
        "constant_instruction_bytes": constant_bytes, "local_size": local_size,
        "groupshared_payload_bytes": workgroup,
        "storage_buffers": total_buffers, "readonly_storage_buffers": readonly_buffers,
        "writable_storage_buffers": total_buffers - readonly_buffers,
        "writable_storage_images": writable_images,
        "writable_storage_bindings": total_buffers - readonly_buffers + writable_images,
        "control_barriers": control, "memory_barriers": memory,
        "barriers": control + memory, "no_contraction": precise,
        "float64": any(op == 22 and args[0] == 64 for op, args in types.values()),
        "bindings": sorted(bindings, key=lambda item: (item["set"], item["binding"])),
    }


def metric_report(words, pipeline: Pipeline, command: list[str], compiler_version: str,
                  native: bool, oracle: bool = False) -> dict:
    report = spirv_metrics(words, pipeline.entry)
    report.update(source=pipeline.source, profile="oracle" if oracle else "production",
                  native_embedding_payload=native, compiler=command[0], compiler_version=compiler_version,
                  compile_command=command,
                  postprocess=("native storage-image formats + " + " ".join(SPIRV_CLEANUP))
                      if native else "none")
    failures, reviews = [], []
    # Oracle modules deliberately contain many operation branches. Report their
    # sizes, but do not mislabel them as a production shader-size failure.
    if not oracle:
        for key, review, fail in (("spirv_bytes", 512 * 1024, 1024 * 1024),
                                  ("function_body_instructions", 20000, 50000)):
            if report[key] > fail:
                failures.append(f"{key}={report[key]} > {fail}")
            elif report[key] > review:
                reviews.append(f"{key}={report[key]} > {review}")
    if report["groupshared_payload_bytes"] > 32768:
        failures.append("groupshared_payload_bytes > 32768")
    elif report["groupshared_payload_bytes"] > 16384:
        reviews.append("groupshared_payload_bytes > 16384")
    if report["writable_storage_bindings"] > 8:
        failures.append("writable_storage_bindings > 8")
    if report["float64"]:
        failures.append("float64 runtime type")
    report.update(metric_gate="FAIL" if failures else "REVIEW" if reviews else "PASS",
                  failures=failures, reviews=reviews)
    return report


def print_metrics(report: dict) -> None:
    print("METRICS " + json.dumps(report, sort_keys=True))


def c_string(value: str) -> str:
    return '"' + value.replace('\\', '\\\\').replace('"', '\\"') + '"'


def emit(output: Path, compiled) -> None:
    # These three transient zero writes replace a scalar ERASE setup kernel.
    # Derive their byte offsets from the actual counter ABI, not a second
    # handwritten native layout. No allocator/residency word is included.
    world = (SHADER_ROOT / "MerkabaWorld.hlsl").read_text(encoding="utf-8")
    fine_reset_offsets = []
    for name in ("M8_COUNTER_FINE_ERASE_TILE_COUNT",
                 "M8_COUNTER_UNRESOLVED_OBSERVATION_TILES",
                 "M8_COUNTER_OBSERVATION_CHANGE_MASK"):
        matches = re.findall(r"^#define\s+" + name + r"\s+(\d+)u\s*$", world, re.MULTILINE)
        if len(matches) != 1:
            raise RuntimeError(f"missing/ambiguous native ERASE setup counter: {name}")
        fine_reset_offsets.append(4 * int(matches[0]))
    lines = [
        "// Generated at native-plugin build time. Do not commit this file.",
        f"static constexpr uint32_t kMerkabaExecutorResourceCount = {len(RESOURCE_NAMES)}u;",
        "static constexpr uint32_t kMerkabaFineEraseCounterResetOffsets[] = {" +
        ", ".join(f"{offset}u" for offset in fine_reset_offsets) + "};",
        "",
    ]
    for index, (pipeline, words, descriptors, uniforms, global_size) in \
            enumerate(compiled):
        lines.append(f"static const uint32_t kMerkabaExecutorSpv{index}[] = {{")
        for begin in range(0, len(words), 8):
            lines.append("    " + ", ".join(
                f"0x{word:08x}u" for word in words[begin:begin + 8]) + ",")
        lines.append("};")
        lines.append(
            f"static const MerkabaEmbeddedDescriptor kMerkabaExecutorDesc{index}[] = {{")
        for binding, kind, resource in descriptors:
            lines.append(f"    {{{binding}u, {kind}u, {resource}}},")
        lines.append("};")
        lines.append(
            f"static const MerkabaEmbeddedUniform kMerkabaExecutorUniform{index}[] = {{")
        for name, offset in uniforms:
            lines.append(f"    {{{c_string(name)}, {offset}u}},")
        lines.append("};")
        lines.append("")

    lines.append("static const MerkabaEmbeddedPipeline kMerkabaExecutorPipelines[] = {")
    for index, (pipeline, words, descriptors, uniforms, global_size) in \
            enumerate(compiled):
        lines.append(
            "    {" + f"{c_string(pipeline.label)}, {c_string(pipeline.entry)}, "
            f"{c_string(pipeline.dispatch)}, kMerkabaExecutorSpv{index}, "
            f"{len(words)}u, kMerkabaExecutorDesc{index}, "
            f"{len(descriptors)}u, kMerkabaExecutorUniform{index}, "
            f"{len(uniforms)}u, {global_size}u" + "},")
    lines.append("};")
    lines.append(
        "static constexpr uint32_t kMerkabaExecutorPipelineCount = "
        "sizeof(kMerkabaExecutorPipelines) / sizeof(kMerkabaExecutorPipelines[0]);")
    for index, pipeline in enumerate(PIPELINES):
        lines.append(f"static constexpr uint32_t kPipeline{pipeline.label} = {index}u;")
    schedules = command_schedules()
    for name, indices in schedules:
        lines.append(f"static const uint32_t kMerkaba{name}Dispatches[] = {{" +
                     ", ".join(f"{index}u" for index in indices) + "};")
    lines.append("static const MerkabaEmbeddedSchedule kMerkabaExecutorSchedules[] = {")
    for name, indices in schedules:
        lines.append(f"    {{{min(indices)}u, {max(indices) + 1}u, {len(indices)}u, "
                     f"kMerkaba{name}Dispatches}},")
    lines.append("};")
    lines.append("")
    output.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, help="native C++ embedding output (unchanged ABI)")
    parser.add_argument("--metrics-report", type=Path, help="write the exact emitted-module metrics as JSON")
    parser.add_argument("--audit-shader", type=Path, help="compile one entry for the existing compute audit")
    parser.add_argument("--audit-entry", help="entrypoint in --audit-shader")
    parser.add_argument("--artifact-dir", type=Path, help="retain the single audit module and its metrics")
    parser.add_argument("--command-graph", action="store_true", help="report the exact embedded native schedules; no shader compilation")
    args = parser.parse_args()
    if args.command_graph:
        schedules = []
        for name, indices in command_schedules():
            allocation = sum(PIPELINES[index].dispatch in
                             ("allocation_gate", "allocation_tiles") for index in indices)
            schedules.append({"job": name, "scheduled_dispatches": len(indices),
                "allocation_indirect_commands": allocation,
                "non_allocation_commands": len(indices) - allocation,
                "between_dispatch_barriers": len(indices) - 1,
                "optional_flower_passes": name == "FlowerReadout",
                "setup_transfer_fills": 5 if name == "FineErase" else
                    1 if name == "FlowerReadout" else 0,
                "commands": [{"pipeline": PIPELINES[index].label,
                              "dispatch": PIPELINES[index].dispatch} for index in indices]})
        print(json.dumps({"authority": "native embedded command schedules",
            "normal_dispatch_target": 10, "schedules": schedules,
            "gpu_nonzero_work": "device measurement required; an indirect call may dispatch zero groups"},
            indent=2))
        return 0
    auditing = args.audit_shader is not None
    if auditing:
        if args.output or not args.audit_entry or not args.artifact_dir:
            parser.error("--audit-shader needs --audit-entry and --artifact-dir, without --output")
    elif not args.output or args.audit_entry or args.artifact_dir:
        parser.error("provide --output, or the complete single-entry --audit-shader arguments")
    glslang = require("glslangValidator")
    spirv_val = require("spirv-val")
    version = run([glslang, "--version"])
    if version.returncode != 0:
        raise RuntimeError("cannot record the actual glslang version")
    compiler_version = version.stdout.strip()
    if auditing:
        shader = args.audit_shader.resolve()
        if not shader.is_file() or not shader.is_relative_to(ROOT):
            parser.error("--audit-shader must name an existing repository shader")
        native = next((pipeline for pipeline in PIPELINES
                       if (SHADER_ROOT / pipeline.source).resolve() == shader
                       and pipeline.entry == args.audit_entry), None)
        pipeline = native or Pipeline(args.audit_entry, str(shader), args.audit_entry, "audit")
        args.artifact_dir.mkdir(parents=True, exist_ok=True)
        artifact = args.artifact_dir / "pipeline-0.spv"
        command = compile_command(glslang, pipeline, artifact)
        if native:
            # EXACT native words, including the required final image-format
            # patch, validation and reflected resource/global ABI checks.
            words, _, _, _ = compile_pipeline(glslang, spirv_val, args.artifact_dir, 0, pipeline)
        else:
            # Auxiliary runtime and oracle kernels are not embedded pipelines;
            # keep that distinction explicit, using the same compiler flags.
            compiled = run(command)
            if compiled.returncode != 0:
                raise RuntimeError(f"{pipeline.label}: glslang failed\n" + compiled.stdout + compiled.stderr)
            validated = run([spirv_val, "--target-env", "vulkan1.1", str(artifact)])
            if validated.returncode != 0:
                raise RuntimeError(f"{pipeline.label}: spirv-val failed\n" + validated.stdout + validated.stderr)
            payload = artifact.read_bytes()
            if len(payload) % 4:
                raise RuntimeError(f"{pipeline.label}: malformed SPIR-V size")
            words = struct.unpack(f"<{len(payload) // 4}I", payload)
        report = metric_report(words, pipeline, command, compiler_version,
                               native is not None, shader.is_relative_to(ROOT / "Tests"))
        report["source"] = str(shader.relative_to(ROOT))
        print_metrics(report)
        report_path = args.metrics_report or args.artifact_dir / "metrics.json"
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        # Compilation/reflection errors fail above. The calling audit combines
        # metric failures with its existing alias/precision checks, continuing
        # through every entry rather than hiding later failures.
        return 0
    with tempfile.TemporaryDirectory(prefix="merkaba-native-executor-") as value:
        temporary = Path(value)
        compiled = []
        reports = []
        for index, pipeline in enumerate(PIPELINES):
            result = compile_pipeline(glslang, spirv_val, temporary, index,
                                      pipeline)
            compiled.append((pipeline, *result))
            report = metric_report(result[0], pipeline,
                                   compile_command(glslang, pipeline, temporary / f"pipeline-{index}.spv"),
                                   compiler_version, True)
            reports.append(report)
            print_metrics(report)
            print(f"COMPILED {pipeline.label}: {len(result[0]) * 4} bytes, "
                  f"descriptors={len(result[1])}, uniforms={len(result[2])}, "
                  f"globals={result[3]}, metric_gate={report['metric_gate']}")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        emit(args.output, compiled)
        if args.metrics_report:
            args.metrics_report.parent.mkdir(parents=True, exist_ok=True)
            args.metrics_report.write_text(json.dumps(reports, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Embedded {len(compiled)} native executor pipelines: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
