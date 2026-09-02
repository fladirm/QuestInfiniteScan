#!/usr/bin/env python3
"""Embed the exact production M8 scanner/readout SPIR-V and reflected ABI.

The native queue executor compiles the same HLSL entry points as Unity.  All
descriptor bindings and global-uniform offsets are reflected here so the
plugin never maintains a second handwritten shader ABI.
"""

from __future__ import annotations

import argparse
import re
import shutil
import struct
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SHADER_ROOT = ROOT / "Runtime" / "Shaders"


@dataclass(frozen=True)
class Pipeline:
    label: str
    source: str
    entry: str
    dispatch: str
    resource_overrides: tuple[tuple[str, str], ...] = ()


PIPELINES = (
    Pipeline("StereoRgbdRefine", "StereoRgbdRefine.compute",
             "StereoRgbdRefine", "refine"),
    Pipeline("InitDepthDilation", "DepthDilation.compute",
             "InitDepthDilation", "depth"),
    *tuple(Pipeline(f"DilateDepthStep[{8-index}]", "DepthDilation.compute",
                    "DilateDepthStep", "depth",
                    (("gsDilateSrc", "DilationA" if index % 2 == 0 else
                      "DilationB"),
                     ("gsDilateDest", "DilationB" if index % 2 == 0 else
                      "DilationA"))) for index in range(9)),
    Pipeline("ResetObservationCounters", "MerkabaWorld.compute",
             "ResetObservationCounters", "one"),
    Pipeline("DiscoverSurfaceCandidates", "MerkabaIntegration.compute",
             "DiscoverSurfaceCandidates", "depth"),
    Pipeline("PrepareResolveArgs", "MerkabaIntegration.compute",
             "PrepareResolveArgs", "one"),
    Pipeline("ResolveSurfaceBlocks", "MerkabaIntegration.compute",
             "ResolveSurfaceBlocks", "observation_indirect"),
    Pipeline("PublishNewBlocks", "MerkabaWorld.compute",
             "PublishNewBlocks", "observation_indirect"),
    Pipeline("ResolveSurfaceChunks", "MerkabaIntegration.compute",
             "ResolveSurfaceChunks", "observation_indirect"),
    Pipeline("PublishNewChunks", "MerkabaWorld.compute",
             "PublishNewChunks", "observation_indirect"),
    Pipeline("ResolveSurfaceTiles", "MerkabaIntegration.compute",
             "ResolveSurfaceTiles", "observation_indirect"),
    Pipeline("RetryPendingNewTiles", "MerkabaIntegration.compute",
             "RetryPendingNewTiles", "observation_indirect"),
    Pipeline("PrepareNewTileDispatchArgs", "MerkabaWorld.compute",
             "PrepareNewTileDispatchArgs", "one"),
    Pipeline("InitializeNewTiles", "MerkabaWorld.compute",
             "InitializeNewTiles", "observation_indirect"),
    Pipeline("ResetClaimQueueCounts", "MerkabaWorld.compute",
             "ResetClaimQueueCounts", "one"),
    Pipeline("InitializeSurfaceWinners", "MerkabaIntegration.compute",
             "InitializeSurfaceWinners", "observation_indirect"),
    Pipeline("SelectSurfaceWinners", "MerkabaIntegration.compute",
             "SelectSurfaceWinners", "observation_indirect"),
    Pipeline("QueueResolvedSurfaceCandidates", "MerkabaIntegration.compute",
             "QueueResolvedSurfaceCandidates", "observation_indirect"),
    Pipeline("QueryCarveTiles", "MerkabaIntegration.compute",
             "QueryCarveTiles", "query"),
    Pipeline("PrepareIntegrateArgs", "MerkabaIntegration.compute",
             "PrepareIntegrateArgs", "one"),
    Pipeline("IntegrateSurfaceCandidates", "MerkabaIntegration.compute",
             "IntegrateSurfaceCandidates", "observation_indirect"),
    Pipeline("PrepareCarveArgs", "MerkabaIntegration.compute",
             "PrepareCarveArgs", "one"),
    Pipeline("IntegrateCarveTiles", "MerkabaIntegration.compute",
             "IntegrateCarveTiles", "carve_indirect"),
    Pipeline("FinalizeObservation", "MerkabaIntegration.compute",
             "FinalizeObservation", "one"),
    Pipeline("ClearTouchedSurfaceCandidates", "MerkabaWorld.compute",
             "ClearTouchedSurfaceCandidates", "observation_indirect"),
    Pipeline("ResetReadoutBuild", "MerkabaReadout.compute",
             "ResetReadoutBuild", "readout_reset"),
    Pipeline("QueryM8Readout", "MerkabaReadout.compute",
             "QueryM8Readout", "readout_query"),
    Pipeline("PrepareReadoutBuild", "MerkabaReadout.compute",
             "PrepareReadoutBuild", "one"),
    Pipeline("ProjectReadoutFrontDepth", "MerkabaReadout.compute",
             "ProjectReadoutFrontDepth", "readout_indirect"),
    Pipeline("IndexReadoutVertices", "MerkabaReadout.compute",
             "IndexReadoutVertices", "readout_indirect"),
    Pipeline("BuildReadoutVertices", "MerkabaReadout.compute",
             "BuildReadoutVertices", "readout_indirect"),
    Pipeline("FinalizeReadout", "MerkabaReadout.compute",
             "FinalizeReadout", "one"),
    Pipeline("MeshResetReadoutBuild", "MerkabaReadout.compute",
             "ResetReadoutBuild", "readout_reset"),
    Pipeline("MeshQueryM8Readout", "MerkabaReadout.compute",
             "QueryM8Readout", "readout_query"),
    Pipeline("MeshPrepareReadoutBuild", "MerkabaReadout.compute",
             "PrepareReadoutBuild", "depth"),
    Pipeline("ProjectReadoutMeshPins", "MerkabaReadout.compute",
             "ProjectReadoutMeshPins", "readout_indirect"),
    Pipeline("BuildReadoutMesh", "MerkabaReadout.compute",
             "BuildReadoutMesh", "depth"),
    Pipeline("MeshFinalizeReadout", "MerkabaReadout.compute",
             "FinalizeReadout", "one"),
    Pipeline("ResetFineErase", "MerkabaIntegration.compute",
             "ResetFineErase", "one"),
    Pipeline("QueryFineEraseTiles", "MerkabaIntegration.compute",
             "QueryFineEraseTiles", "query"),
    Pipeline("PrepareFineEraseArgs", "MerkabaIntegration.compute",
             "PrepareFineEraseArgs", "one"),
    Pipeline("EraseFineTiles", "MerkabaIntegration.compute",
             "EraseFineTiles", "carve_indirect"),
    Pipeline("FinalizeFineErase", "MerkabaIntegration.compute",
             "FinalizeFineErase", "one"),
)


RESOURCE_NAMES = (
    "HashEntries", "OwnerRecords", "BlockChunkRefs", "BlockPresenceL0",
    "BlockPresenceL1", "BlockPresenceL2", "ChunkTileRefs",
    "ChunkPresence", "KernelStates0", "KernelStates1", "KernelStates2",
    "KernelStates3", "TileBits", "TileRecords", "FreeTileStack",
    "Counters", "ClaimQueue", "PendingNewTileRefs", "LoadRequests",
    "LoadRequestReadCount", "SurfaceCandidates", "SurfaceQueue",
    "SurfaceWinnerRanks0", "SurfaceWinnerRanks1", "SurfaceWinnerRanks2",
    "SurfaceWinnerRanks3", "TouchedTileQueue", "CarveTiles",
    "ObservationDispatchArgs", "CarveDispatchArgs", "AttemptCompletion",
    "RefineMetrics", "RawDepth", "RefinedDepth", "Normals", "DilationA",
    "DilationB", "CameraLeft", "CameraRight", "VisibleTiles",
    "FrameDispatchArgs", "ReadoutVertices0", "ReadoutVertices1",
    "ReadoutIndices", "DrawArgs",
)
RESOURCE_IDS = {name: index for index, name in enumerate(RESOURCE_NAMES)}


ALIASES = {
    **{f"_M8{name}": name for name in (
        "HashEntries", "OwnerRecords", "BlockChunkRefs", "BlockPresenceL0",
        "BlockPresenceL1", "BlockPresenceL2", "ChunkTileRefs",
        "ChunkPresence", "KernelStates0", "KernelStates1", "KernelStates2",
        "KernelStates3", "TileBits", "TileRecords", "FreeTileStack",
        "Counters", "ClaimQueue", "PendingNewTileRefs", "LoadRequests",
        "LoadRequestReadCount", "SurfaceCandidates", "SurfaceQueue",
        "SurfaceWinnerRanks0", "SurfaceWinnerRanks1", "SurfaceWinnerRanks2",
        "SurfaceWinnerRanks3", "TouchedTileQueue", "CarveTiles",
        "ObservationDispatchArgs", "CarveDispatchArgs", "AttemptCompletion",
        "VisibleTiles", "FrameDispatchArgs", "ReadoutVertices0",
        "ReadoutVertices1", "ReadoutIndices", "DrawArgs")},
    "_RefineMetrics": "RefineMetrics",
    "_SrcDepth": "RawDepth",
    "_DstDepth": "RefinedDepth",
    "_DstNormal": "Normals",
    "gsDepthTex": "RefinedDepth",
    "gsDepthNormalTex": "Normals",
    "gsDilatedDepth": "DilationB",
    "gsDilateSrc": "DilationA",
    "gsDilateDest": "DilationB",
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
    if name == "gsBilinearClampSampler":
        return KIND_BILINEAR_SAMPLER
    if name == "gsPointClampSampler":
        return KIND_POINT_SAMPLER
    if type_code.lower() == "904d":
        return KIND_STORAGE_IMAGE
    return KIND_SAMPLED_IMAGE


def patch_storage_image_formats(words: tuple[int, ...], descriptors):
    """Declare the exact Unity RenderTexture storage formats in SPIR-V.

    glslang emits every HLSL RWTexture2D<float4> as rgba32f, while the exact
    production resources are RGBA8_SNORM normals and RGBA16_SFLOAT dilation.
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
        RESOURCE_IDS["DilationA"]: 2,    # SpvImageFormatRgba16f
        RESOURCE_IDS["DilationB"]: 2,
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


def compile_pipeline(glslang: str, spirv_val: str, temporary: Path,
                     index: int, pipeline: Pipeline):
    output = temporary / f"pipeline-{index}.spv"
    command = [
        glslang, "-D", "-V", "--target-env", "vulkan1.1", "-S", "comp",
        "-e", pipeline.entry, f"-I{SHADER_ROOT}",
        "-DSHADER_API_VULKAN=1", "--auto-map-bindings", "-l", "-q",
        str(SHADER_ROOT / pipeline.source), "-o", str(output),
    ]
    compiled = run(command)
    if compiled.returncode != 0:
        raise RuntimeError(f"{pipeline.label}: glslang failed\n" +
                           compiled.stdout + compiled.stderr)
    descriptors: list[tuple[int, int, int]] = []
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
            if name == "$Global":
                global_size = parse_int(line, "size")
                global_index = parse_int(line, "index")
                descriptors.append((binding, KIND_UNIFORM_BUFFER, -1))
            else:
                descriptors.append((binding, KIND_STORAGE_BUFFER,
                                    resource_id(pipeline, name)))
        elif section == "Uniform reflection:" and ": offset " in line:
            name = line.split(": offset", 1)[0]
            offset = parse_int(line, "offset")
            if offset >= 0:
                uniform_lines.append(line)
                continue
            binding = parse_int(line, "binding")
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
    descriptors.sort()
    uniforms = sorted(set(uniforms), key=lambda item: (item[1], item[0]))
    if len({item[0] for item in descriptors}) != len(descriptors):
        raise RuntimeError(f"{pipeline.label}: duplicate reflected binding")
    payload = output.read_bytes()
    if len(payload) % 4:
        raise RuntimeError(f"{pipeline.label}: malformed SPIR-V size")
    words = struct.unpack(f"<{len(payload) // 4}I", payload)
    words = patch_storage_image_formats(words, descriptors)
    output.write_bytes(struct.pack(f"<{len(words)}I", *words))
    validated = run([spirv_val, "--target-env", "vulkan1.1", str(output)])
    if validated.returncode != 0:
        raise RuntimeError(f"{pipeline.label}: spirv-val failed\n" +
                           validated.stdout + validated.stderr)
    return words, descriptors, uniforms, global_size


def c_string(value: str) -> str:
    return '"' + value.replace('\\', '\\\\').replace('"', '\\"') + '"'


def emit(output: Path, compiled) -> None:
    lines = [
        "// Generated at native-plugin build time. Do not commit this file.",
        f"static constexpr uint32_t kMerkabaExecutorResourceCount = {len(RESOURCE_NAMES)}u;",
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
    lines.append("")
    output.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    glslang = require("glslangValidator")
    spirv_val = require("spirv-val")
    with tempfile.TemporaryDirectory(prefix="merkaba-native-executor-") as value:
        temporary = Path(value)
        compiled = []
        for index, pipeline in enumerate(PIPELINES):
            result = compile_pipeline(glslang, spirv_val, temporary, index,
                                      pipeline)
            compiled.append((pipeline, *result))
            print(f"PASS {pipeline.label}: {len(result[0]) * 4} bytes, "
                  f"descriptors={len(result[1])}, uniforms={len(result[2])}, "
                  f"globals={result[3]}")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        emit(args.output, compiled)
    print(f"Embedded {len(compiled)} native executor pipelines: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
