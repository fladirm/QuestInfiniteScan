#!/usr/bin/env python3
"""Embed the exact production NativeCloseCommit SPIR-V for the Vulkan plugin.

The native executor uses the same HLSL sources and entry points as Unity.  This
tool intentionally reflects descriptor bindings from each compiled module so
the plugin does not maintain a second handwritten shader binding ABI.
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
SHADER_ROOT = ROOT / "Runtime" / "Resources" / "SigmaPrism"


@dataclass(frozen=True)
class Pipeline:
    label: str
    source: str
    entry: str
    defines: tuple[str, ...] = ()


# This order is the fixed 16-dispatch NativeCloseCommit graph.
PIPELINES = (
    Pipeline("BuildNativeObservation", "SigmaNativeFrame.compute",
             "BuildNativeObservation"),
    Pipeline("ContractNativeQuery.FOOTPRINT", "SigmaNativeContract.compute",
             "ContractNativeQuery", ("SIGMA_NATIVE_FRESH_MATH=1",)),
    Pipeline("EvaluateNativeRelation.BOUNDARY", "SigmaNativeQuery.compute",
             "EvaluateNativeRelation", ("SIGMA_NATIVE_ORACLE_RELATION_MATH=1",
                                         "SIGMA_N4_BOUNDARY_VARIANT=1")),
    Pipeline("ContractNativeQuery.TILE_CLOSE", "SigmaNativeContract.compute",
             "ContractNativeQuery", ("SIGMA_NATIVE_FRESH_MATH=1",
                                      "SIGMA_N4_TILE_CLOSE_VARIANT=1")),
    Pipeline("EvaluateNativeRelation.GLOBAL_CLOSE", "SigmaNativeQuery.compute",
             "EvaluateNativeRelation", ("SIGMA_NATIVE_ORACLE_RELATION_MATH=1",
                                         "SIGMA_N4_GLOBAL_CLOSE_VARIANT=1")),
    Pipeline("PrepareNativeCanonicalSeed", "SigmaNativeFrame.compute",
             "PrepareNativeCanonicalSeed"),
    Pipeline("PrepareNativeCanonicalRuns", "SigmaNativeFrame.compute",
             "PrepareNativeCanonicalRuns"),
    Pipeline("PrepareNativeRefinementPlan", "SigmaNativeFrame.compute",
             "PrepareNativeRefinementPlan"),
    Pipeline("PrepareNativeCanonicalSelect", "SigmaNativeFrame.compute",
             "PrepareNativeCanonicalSelect"),
    Pipeline("PrepareNativeRefinementProof", "SigmaNativeFrame.compute",
             "PrepareNativeRefinementProof"),
    Pipeline("PrepareNativeComponentOrder", "SigmaNativeFrame.compute",
             "PrepareNativeComponentOrder"),
    Pipeline("PrepareNativeRefinementScan", "SigmaNativeFrame.compute",
             "PrepareNativeRefinementScan"),
    Pipeline("PrepareNativeRevision", "SigmaNativeFrame.compute",
             "PrepareNativeRevision"),
    Pipeline("PrepareNativePage", "SigmaNativeFrame.compute",
             "PrepareNativePage"),
    Pipeline("ScatterNativeState", "SigmaNativeFrame.compute",
             "ScatterNativeState"),
    Pipeline("CloseAndPublishNativeRevision", "SigmaNativeFrame.compute",
             "CloseAndPublishNativeRevision"),
)


# Numeric values are shared with SigmaNativeVulkanExecutor.Resource in C# and
# SigmaExecutorResource in the native plugin.  Names below are shader ABI names,
# never physical/canonical identity.
RESOURCE_IDS = {
    "_SigmaExactBackendGate": 0,
    "_DepthCalibrationQ48": 1,
    "_RgbCalibrationQ48": 2,
    "_PoseResult": 3,
    "_NativeFrames": 4,
    "_NativeObservations": 5,
    "_NativePrepareObservations": 5,
    "_NativeCloseScratch": 6,
    "_NativeStates": 7,
    "_NativePrepareStates": 7,
    "_NativeStateDeltas": 8,
    "_NativeGaugeDeltas": 9,
    "_NativeLocalityCertificateWords": 10,
    "_NativeRevisions": 11,
    "_NativeCounters": 12,
    "_NativeCompletionJournal": 13,
    "_NativeFreshEvidenceWords": 13,
    "_NativeSourceCarrierState": 14,
    "_TargetCarrierState": 14,
    "_NativeSourceCarrierRepresentation": 15,
    "_TargetCarrierRepresentation": 15,
    "_NativeSourcePageMetadata": 16,
    "_TargetPageMetadata": 16,
    "_NativeSourcePublicationRoot": 17,
    "_PublishedRevisionRoot": 17,
    "_TargetDirtyFlags": 18,
    "_TargetReadoutDirtyFlags": 19,
    "_NativeRelationInputs": 20,
    "_NativeRelationPlans": 21,
    "_NativeRelationNearIntervals": 22,
    "_NativeRelationResults": 23,
    "_NativeReverseRelationResults": 23,
    "_NativeRelationFactors": 24,
    "_NativeRelationHashes": 25,
    "_NativeRelationNorms": 26,
    "_NativeBranchHeaders": 27,
    "_NativeBranchSupports": 28,
    "_NativeBranchPredictions": 29,
    "_NativeRawDepth": 30,
    "_NativeMetricDepth": 31,
    "_NativeDepthFlags": 32,
    "_NativeDepthRayCenterLeft": 33,
    "_NativeDepthRayCenterRight": 34,
    "_NativeDepthRayDifferentialXLeft": 35,
    "_NativeDepthRayDifferentialXRight": 36,
    "_NativeDepthRayDifferentialYLeft": 37,
    "_NativeDepthRayDifferentialYRight": 38,
    "_NativeDepthSlopeBoundsLeft": 39,
    "_NativeDepthSlopeBoundsRight": 40,
    "_NativeRgbLeft": 41,
    "_NativeRgbRight": 42,
    "_NativePredCarrierPage": 43,
    "_NativePredCarrierUvNormal": 44,
    "_NativePredStateKey": 45,
}

RESOURCE_COUNT = 46
KIND_STORAGE = 0
KIND_SAMPLED_IMAGE = 1
KIND_UNIFORM = 2


def require(name: str) -> str:
    path = shutil.which(name)
    if path is None:
        raise RuntimeError(f"required tool is missing: {name}")
    return path


def run(command: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, cwd=ROOT, text=True,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          check=False)


def resource_id(pipeline: Pipeline, name: str) -> int:
    # The root-last close deliberately aliases this SRV to the page-plan image
    # in CloseScratch.  Every other entry point reads the canonical carrier.
    if (pipeline.entry == "CloseAndPublishNativeRevision" and
            name == "_NativeSourceCarrierState"):
        return RESOURCE_IDS["_NativeCloseScratch"]
    try:
        return RESOURCE_IDS[name]
    except KeyError as exception:
        raise RuntimeError(
            f"unmapped native executor resource {pipeline.label}: {name}") \
            from exception


def parse_binding(line: str) -> int:
    match = re.search(r"\bbinding (-?\d+)", line)
    if match is None:
        raise RuntimeError(f"reflection line has no binding: {line}")
    return int(match.group(1))


def parse_size(line: str) -> int:
    match = re.search(r"\bsize (\d+)", line)
    if match is None:
        raise RuntimeError(f"reflection line has no size: {line}")
    return int(match.group(1))


def validate_frame_matrix_abi(spirv_dis: str, module: Path,
                              pipeline: Pipeline) -> None:
    if pipeline.source != "SigmaNativeFrame.compute":
        return
    disassembled = run([spirv_dis, str(module)])
    if disassembled.returncode != 0:
        raise RuntimeError(f"{pipeline.label}: spirv-dis failed\n" +
                           disassembled.stdout + disassembled.stderr)
    text = disassembled.stdout
    for member, offset in ((0, 0), (1, 64), (7, 176), (8, 240),
                           (9, 304), (10, 368)):
        required = (
            f"OpMemberDecorate %_Global {member} RowMajor",
            f"OpMemberDecorate %_Global {member} MatrixStride 16",
            f"OpMemberDecorate %_Global {member} Offset {offset}",
        )
        if any(item not in text for item in required):
            raise RuntimeError(
                f"{pipeline.label}: native matrix ABI member {member} must "
                f"remain RowMajor/stride16/offset{offset}")


def compile_pipeline(glslang: str, spirv_val: str, spirv_dis: str,
                     temporary: Path,
                     index: int, pipeline: Pipeline):
    output = temporary / f"pipeline-{index}.spv"
    command = [
        glslang, "-D", "-V", "--target-env", "vulkan1.1", "-S", "comp",
        "-e", pipeline.entry, f"-I{SHADER_ROOT}",
        "-DSHADER_API_VULKAN=1", "--auto-map-bindings", "-l", "-q",
    ]
    command.extend(f"-D{define}" for define in pipeline.defines)
    command.extend([str(SHADER_ROOT / pipeline.source), "-o", str(output)])
    compiled = run(command)
    if compiled.returncode != 0:
        raise RuntimeError(f"{pipeline.label}: glslang failed\n" +
                           compiled.stdout + compiled.stderr)
    validated = run([spirv_val, "--target-env", "vulkan1.1", str(output)])
    if validated.returncode != 0:
        raise RuntimeError(f"{pipeline.label}: spirv-val failed\n" +
                           validated.stdout + validated.stderr)
    validate_frame_matrix_abi(spirv_dis, output, pipeline)

    descriptors: list[tuple[int, int, int]] = []
    global_size = 0
    section = ""
    for line in compiled.stdout.splitlines():
        if line.endswith("reflection:"):
            section = line
            continue
        if section == "Uniform block reflection:" and ": offset " in line:
            name = line.split(": offset", 1)[0]
            binding = parse_binding(line)
            if name == "$Global":
                global_size = parse_size(line)
                descriptors.append((binding, KIND_UNIFORM, -1))
            else:
                descriptors.append((binding, KIND_STORAGE,
                                    resource_id(pipeline, name)))
        elif (section == "Uniform reflection:" and "offset -1" in line and
              "index -1" in line):
            name = line.split(": offset", 1)[0]
            descriptors.append((parse_binding(line), KIND_SAMPLED_IMAGE,
                                resource_id(pipeline, name)))

    if global_size == 0:
        raise RuntimeError(f"{pipeline.label}: $Global was not reflected")
    descriptors.sort()
    bindings = [item[0] for item in descriptors]
    if len(bindings) != len(set(bindings)):
        raise RuntimeError(f"{pipeline.label}: duplicate reflected binding")
    payload = output.read_bytes()
    if len(payload) % 4:
        raise RuntimeError(f"{pipeline.label}: malformed SPIR-V size")
    words = struct.unpack(f"<{len(payload) // 4}I", payload)
    return words, descriptors, global_size


def emit(output: Path, compiled) -> None:
    lines = [
        "// Generated at native-plugin build time. Do not commit this file.",
        f"static constexpr uint32_t kSigmaExecutorResourceCount = {RESOURCE_COUNT}u;",
        "",
    ]
    for index, (pipeline, words, descriptors, global_size) in enumerate(compiled):
        lines.append(f"static const uint32_t kSigmaExecutorSpv{index}[] = {{")
        for begin in range(0, len(words), 8):
            row = ", ".join(f"0x{word:08x}u" for word in words[begin:begin + 8])
            lines.append("    " + row + ",")
        lines.append("};")
        lines.append(
            f"static const SigmaEmbeddedDescriptor kSigmaExecutorDesc{index}[] = {{")
        for binding, kind, resource in descriptors:
            lines.append(
                f"    {{{binding}u, {kind}u, {resource}}},")
        lines.append("};")
        lines.append("")

    lines.append("static const SigmaEmbeddedPipeline kSigmaExecutorPipelines[] = {")
    for index, (pipeline, words, descriptors, global_size) in enumerate(compiled):
        escaped_label = pipeline.label.replace('"', '\\"')
        escaped_entry = pipeline.entry.replace('"', '\\"')
        lines.append(
            "    {" + f'"{escaped_label}", "{escaped_entry}", '
            f"kSigmaExecutorSpv{index}, {len(words)}u, "
            f"kSigmaExecutorDesc{index}, {len(descriptors)}u, {global_size}u" + "},")
    lines.append("};")
    lines.append(
        "static constexpr uint32_t kSigmaExecutorPipelineCount = "
        "sizeof(kSigmaExecutorPipelines) / sizeof(kSigmaExecutorPipelines[0]);")
    lines.append("static_assert(kSigmaExecutorPipelineCount == 16u, "
                 '"NativeCloseCommit must contain exactly 16 pipelines");')
    lines.append("")
    output.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    glslang = require("glslangValidator")
    spirv_val = require("spirv-val")
    spirv_dis = require("spirv-dis")
    with tempfile.TemporaryDirectory(prefix="sigma-native-executor-") as value:
        temporary = Path(value)
        compiled = []
        for index, pipeline in enumerate(PIPELINES):
            words, descriptors, global_size = compile_pipeline(
                glslang, spirv_val, spirv_dis, temporary, index, pipeline)
            compiled.append((pipeline, words, descriptors, global_size))
            print(f"PASS {pipeline.label}: {len(words) * 4} bytes, "
                  f"descriptors={len(descriptors)}, globals={global_size}")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        emit(args.output, compiled)
    print(f"Embedded {len(compiled)}/16 native executor pipelines: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
