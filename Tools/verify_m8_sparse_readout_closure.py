#!/usr/bin/env python3
"""Static closure gate for the M8 sparse readout publication cut."""
from pathlib import Path
import sys
sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")

world = text("Runtime/Shaders/MerkabaWorld.hlsl")
world_compute = text("Runtime/Shaders/MerkabaWorld.compute")
readout = text("Runtime/Shaders/MerkabaReadout.compute")
visibility = text("Runtime/Resources/Merkaba/MerkabaVisibility.compute")
grid = text("Runtime/Merkaba/MerkabaGrid.Gpu.cs")
renderer = text("Runtime/Merkaba/MerkabaGridRenderer.cs")
integrator = text("Runtime/Merkaba/MerkabaIntegrator.cs")
native = text("Runtime/Telemetry/MerkabaNativeVulkanExecutor.cs")
membrane = text("Runtime/Merkaba/MerkabaOverlapShell.cs")
exporter = text("Runtime/Merkaba/MerkabaExportMembrane.cs")
glb = text("Runtime/Merkaba/MerkabaGlbWriter.cs")
viewer = text("Runtime/UI/MerkabaArtifactViewer.cs")
stereo = text("Runtime/Shaders/MerkabaStereoCamera.hlsl")
shader = text("Runtime/Shaders/MerkabaGrid.shader")

checks = {
    "persistent KernelState remains separate from readout journal":
        "M8_RENDER_JOURNAL_CAPACITY" in world,
    "dirty journal seeded from observation touched tiles":
        "SeedRenderTouchedJournal" in world_compute,
    "dirty journal seeded from carve/fine tiles":
        "SeedRenderCarveJournal" in world_compute,
    "residency eviction enters sparse journal":
        "M8JournalRenderSlot(physicalSlot" in world_compute,
    "render journal has dedicated GPU storage":
        "RenderMutationJournalCount" in grid and
        "_m8RenderMutationQueue" in grid,
    "membrane knots sit on the half-lattice of the 25 mm pitch":
        "MembraneHalfPitch =\n            MerkabaConstants.LatticeStep * 0.5f" in membrane,
    "integrator seals observation delta into journal":
        "SeedRenderMutationJournal(true, true)" in integrator,
    "fine erase seals its delta into journal":
        "SeedRenderMutationJournal(false, true)" in integrator,
    "readout working set is rebuilt from current camera coverage":
        "_M8RenderIndexBack[0] = 0u" in readout and
        "M8_RENDER_VIEW_MARK" in readout and
        "RequestWarmResidency" in readout,
    "readout consumes mutation journal":
        "M8_COUNTER_RENDER_JOURNAL_HEAD" in readout and
        "ApplyRenderMutationJournal" in readout,
    "journal is not the render scheduler":
        "M8_RENDER_MUTATION_BATCH" not in world and
        "M8_RENDER_JOURNAL_DRAIN_LIMIT" not in world,
    "view dirty tiles dispatch without an arbitrary tile cap":
        "M8_COUNTER_RENDER_REBUILD_TILES" in readout and
        "MERKABA_M8_PHYSICAL_TILE_CAPACITY" in readout and
        "M8_RENDER_MUTATION_BATCH" not in readout,
    "dirty closure covers all 26 neighbours":
        "M8_RENDER_AXIS_DEPENDENCY_MASK" not in world and
        "M8_RENDER_RESIDENCY_DEPENDENCY_MASK M8_RENDER_DEPENDENCY_MASK"
        in world,
    "journal entries carry a logical identity":
        "M8_RENDER_JOURNAL_ENTRY_WORDS" in world and
        "journalledIdentity" in readout,
    "departed view pages are retired":
        "M8_RENDER_VIEW_MARK" in readout and
        "M8_RENDER_RETIRE_BASE" in readout and
        "void CollectRenderRebuildTiles" in readout,
    "scanner pending work does not starve graphics-queue readout":
        "HasPendingObservation" not in renderer and
        "HasPendingFineErase" not in renderer,
    "patch recount is parallel over current view":
        "void AdvanceRenderBuildBatch" in readout,
    "BuildTileCap tuning authority removed":
        "BuildTileCap" not in renderer and
        "_M8RenderBuildTileCap" not in readout,
    "32k render-slot dispatch removed":
        "slotGroups" not in renderer,
    "native readout executes on the scanner queue":
        "if (kind == JobKind.Readout) return false;" not in native and
        "AbiVersion = 7" in native and
        "kExecutorAbiVersion = 7;" in text(
            "Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp") and
        "SubmitNativeReadoutBuild" in renderer,
    "device publication is confirmed by the native fence copy":
        "TryReadCompletion(_completionRecord)" in renderer and
        "CreateGraphicsFence" in renderer and
        "Editor only" in renderer,
    "coverage radius is the world sphere":
        "ReadoutCoverageRadius = 6.4f" in renderer,
    "no field sampling or marching reconstruction in the readout":
        "marching" not in readout.lower() and "SDF" not in readout and
        "TrilinearField" not in readout,
    "stereo invalid UV rejection remains":
        "MerkabaCameraUvValid" in stereo and
        "MerkabaSampleStereoRgb" in stereo,
    "no-RGB fallback remains neutral":
        "half3(0.625h, 0.625h, 0.625h)" in shader,
    "standalone GLB keeps spatial binding":
        "questMerkabaSpatialBinding" in glb,
    "artifact owns local registration":
        ".registration.json" in viewer,
    "artifact owns design sidecars":
        ".design.json" in viewer and ".design-assets" in viewer,
    "artifact/scanner coexistence remains":
        "QuiesceScanningAsync" not in viewer and
        "ReadoutDrawEnabled = false" not in viewer and
        "FineMode = false" not in viewer,
}

def pipeline_names_match() -> bool:
    import importlib.util, re as _re
    spec = importlib.util.spec_from_file_location("gen",
        ROOT / "Tools/unity/generate_merkaba_native_executor_shaders.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    generated = [pipeline.label for pipeline in module.PIPELINES]
    block = native[native.index("PipelineNames ="):]
    block = block[:block.index("};")]
    managed = _re.findall(r'"([^"]+)"', block)
    if managed != generated:
        print("managed:", managed, file=sys.stderr)
        print("generated:", generated, file=sys.stderr)
    return managed == generated

checks["managed stage names equal the generator order"] = \
    pipeline_names_match()


def pipeline_begins_match() -> bool:
    import re as _re
    cpp = text("Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp")
    pairs = (("kReadoutPipelineBegin", "ReadoutPipelineBegin"),
             ("kFineErasePipelineBegin", "FineErasePipelineBegin"))
    for cpp_name, managed_name in pairs:
        cpp_value = _re.search(cpp_name + r" = (\d+);", cpp)
        managed_value = _re.search(managed_name + r" = (\d+);", native)
        if cpp_value is None or managed_value is None or \
                cpp_value.group(1) != managed_value.group(1):
            return False
    retry = _re.search(r"kJobObservationRetry\)\s*\{\s*\*first = (\d+);", cpp)
    managed_retry = _re.search(r"ObservationRetryPipelineBegin = (\d+);",
        native)
    return retry is not None and managed_retry is not None and \
        retry.group(1) == managed_retry.group(1)


checks["managed stage timing begins equal the plugin job boundaries"] = \
    pipeline_begins_match()
checks["publication validates page ownership, not only index range"] = \
    "M8_RENDER_OWNER_BASE" in readout and \
    "_M8RenderPageQueues[M8_RENDER_OWNER_BASE + page] !=" in readout and \
    "physicalSlot + 1u" in readout[readout.index("void ValidatePublication"):] and \
    "InterlockedOr(_M8Counters[M8_COUNTER_RENDER_PUBLICATION_INVALID]" in readout[readout.index("void EmitRenderTileGeometry"):readout.index("void PublishRenderTileList")]
checks["an unresolved halo keeps the scan transaction open"] = \
    "unresolved != 0u;" in readout
checks["a rejected BACK keeps FRONT and its own pages"] = \
    "_previousPublished = false" not in renderer[renderer.index("void RejectPublication"):renderer.index("void RejectPublication")+600] and \
    "_frontTileCount = VisibleTileCount" not in renderer and \
    "_M8PreviousPublished" not in readout
checks["native front-tile sweeps cover every physical tile slot"] = \
    "kRenderSlotGroupCount = 256" in text(
        "Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp") and \
    "MerkabaSpatial.PhysicalTileCapacity / 128" in renderer
checks["per-tile page chain bound is the contract's 2560"] = \
    "RenderTileMaxPages = 2560" in grid and \
    "M8_RENDER_TILE_MAX_PAGES 2560u" in text("Runtime/Shaders/MerkabaWorld.hlsl")
checks["the visible index stream is exact: no page padding, whole triangles"] = \
    "RWStructuredBuffer<uint4> _M8VisiblePages" in visibility and \
    "InterlockedAdd(_M8CullControl[1], indexCount, tileOutputBase)" in visibility and \
    "_M8RenderDrawArgs[0] = valid ? totalIndices : 0u" in visibility and \
    "(totalIndices % 3u) == 0u" in visibility and \
    "_M8RenderDrawArgs[1] = 2u" in visibility and \
    "_M8RenderDrawArgs[0] = pages * M8_RENDER_PAGE_INDICES" not in visibility and \
    "slot < used ?" not in visibility and \
    "Allocate(RenderPageCapacity, sizeof(uint) * 4)" in grid
checks["draw kernels are isolated from the native publication ABI"] = \
    "#pragma kernel CullRenderTiles" not in readout and \
    visibility.count("#pragma kernel ") == 3 and \
    "_visibilityCompute.FindProfiledKernel" in renderer and \
    'Resources.Load<ComputeShader>(' in renderer
checks["corner solve is register resident (no 12-entry private arrays)"] = all(
    token not in text("Runtime/Shaders/MerkabaOverlapShell.generated.hlsl")
    for token in ("bool valid[12]", "float heights[12]", "uint signatures[12]",
                  "int3 coords[12]", "uint near[12]", "uint below[12]")) and \
    "float4 heights0 = 0.0" in text("Runtime/Shaders/MerkabaOverlapShell.generated.hlsl")
checks["patch descriptors have fixed kernelLocal slots and a compact measured list"] = \
    "gM8PatchOfKernel" not in readout and \
    "_M8RenderPatchScratch[patchBase + kernelLocal] = descriptor" in readout and \
    "M8ScratchCompactKernel(" in readout
checks["invalid membrane patches emit no degenerate triangles"] = \
    "M8_SCRATCH_PATCH_VALID" in readout and "b = a; c = a; d = a;" not in readout
checks["vertex ordinals are assigned only after ownership is final"] = \
    "0x80000000u | ordinal" in readout and \
    readout.index("0x80000000u | ordinal") > readout.index("Phase C:") and \
    "no lane walks the tile serially" in readout
checks["the knot is a function of its line, chart, side and layer (CPU twin)"] = \
    "TrySolveKnot(" in membrane and "CanonicalChart(" in membrane and \
    "KnotIdentity" in membrane and "TryResolveCorner" not in membrane
checks["BACK is one transaction in batches: no batch publishes, no carry-over FRONT"] = \
    "M8_RENDER_BATCH_TILES 64u" in readout and \
    "void AdvanceRenderBuildBatch" in readout and \
    "M8_COUNTER_RENDER_BATCH_CURSOR" in readout and \
    "beyond the scratch capacity" not in readout and \
    "RecountRenderPatches" not in readout and \
    "JobKind.ReadoutBegin" in renderer and "JobKind.ReadoutBatch" in renderer and \
    "JobKind.ReadoutFinalize" in renderer and \
    "NativeReadoutPhase.Finalize" in renderer
checks["page ownership is written at allocation and cleared at reclaim"] = \
    "gM8MembranePhysicalSlot + 1u" in readout[readout.index("bool M8TakeRenderPages"):] and \
    "M8_RENDER_OWNER_BASE + (entry & M8_RENDER_PAGE_ID_MASK)" in readout and \
    "InterlockedAdd(_M8RenderIndexBack[1]" in readout
checks["coverage traversal always builds the BACK list; cold loads are gated"] = \
    "_M8AllowWarmLoads" in readout and \
    "if (_M8AllowWarmLoads != 0u)" in readout
checks["live readout consumes the generated membrane oracle"] = \
    '#include "MerkabaOverlapShell.generated.hlsl"' in readout and \
    "M8MembraneSolveKnot(globalCoord, normal, chart," in readout and \
    "void CollectRenderPatchInputs" in readout and \
    "void SolveSharedKnots" in readout and \
    "void EmitRenderTileGeometry" in readout and \
    "void BuildRenderTiles" not in readout and \
    "M8_MEMBRANE_INDICES_PER_PATCH" in readout
checks["no support clamp and no invented fallback geometry"] = \
    "M8_MEMBRANE_KNOT_SUPPORT_LIMIT" not in readout and \
    "MembraneKnotSupportLimit" not in membrane and \
    "M8_MEMBRANE_MAX_EDGE" in readout and "MembraneMaxEdge" in membrane
checks["CPU export uses the same membrane oracle with knot colours"] = \
    "MerkabaOverlapShell.TryBuildPatch(kernel.Coord, context" in exporter and \
    "MerkabaSkin" not in exporter and \
    "patch.Corner01.Normal, patch.Corner01.PackedColor" in exporter
checks["membrane compatibility is residual-based, not quantized"] = \
    "MembraneCompatibleAxisCosine = 0.5f" in membrane and \
    "CanonicalSheet" not in membrane and \
    "M8_MEMBRANE_COMPATIBLE_AXIS_COSINE" in text("Runtime/Shaders/MerkabaOverlapShell.generated.hlsl")
checks["knots carry global identity and canonical colour"] = \
    "internal readonly int3 LineAddress;" in membrane and \
    "M8LoadMembraneColor(bestCoord, ownerColor, ownerConfidence)" in text("Runtime/Shaders/MerkabaOverlapShell.generated.hlsl")
checks["readout solve and emission are separate dispatches"] = \
    "M8MembraneSolveKnot(" not in readout[readout.index("void EmitRenderTileGeometry"):] and \
    "M8StoreRenderVertex(" not in readout[readout.index("void SolveSharedKnots"):readout.index("void EmitRenderTileGeometry")]
checks["one physical knot is one vertex inside a tile (exact identity, no epsilon)"] = \
    "_M8RenderKnotOwner" in readout and \
    "(theirs.w >> 8u) != (mine.w >> 8u)" in readout and \
    "M8_MEMBRANE_WELD_EPSILON" not in readout
checks["no support clamp and no invented fallback geometry, edge guard on both twins"] = \
    "M8_MEMBRANE_KNOT_SUPPORT_LIMIT" not in readout and \
    "MembraneKnotSupportLimit" not in membrane and \
    "M8_MEMBRANE_MAX_EDGE" in readout and "MembraneMaxEdge" in membrane
checks["draw opacity is real alpha blending selected on the CPU"] = \
    "Blend [_SrcBlend] [_DstBlend]" in shader and "M8_ALPHA_BLEND" in shader and \
    "_material.EnableKeyword(\"M8_ALPHA_BLEND\")" in renderer and \
    "coverageThreshold" not in shader
checks["erase is a controller capsule that leaves known free space"] = \
    "operation == FineBrushOperation.Erase" in text("Runtime/Core/RoomScanner.cs") and \
    "emptyState.evidence = MERKABA_EXPORT_KNOWN_FREE;" in text("Runtime/Shaders/MerkabaIntegration.compute")
checks["membrane pitch is frozen at one 25 mm lattice step"] = \
    "MembranePatchPitch = MerkabaConstants.LatticeStep" in membrane
checks["the publication producer has a diagnostic hard-off"] = \
    "readoutProducerEnabled && !queueBusy" in renderer
checks["capacity failure makes BACK non-publishable"] = \
    "MERKABA_READOUT_FAILED : MERKABA_READOUT_PUBLISHED" in readout
checks["publication is decided from the native completion copy"] = \
    "MerkabaExecutor_ReadCompletion" in text(
        "Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp") and \
    "TryReadCompletion" in renderer and "RejectPublication" in renderer

checks["publication is a strict two-slot ping-pong"] = \
    "PublicationSlotCount = 2" in grid and \
    "GetM8RenderPageQueues(backSlot)" in renderer and \
    "GetM8RenderVertices(backSlot)" in renderer and \
    "GetM8RenderMesh(_frontReadout)" in renderer and \
    "_publicationGeneration" not in renderer and \
    "_safeReclaimGeneration" not in renderer and \
    "M8_RENDER_RETIRE_GENERATION" not in world
checks["old FRONT is written again only after the draw fence"] = \
    "BackSlotReleased()" in renderer and "_frontReleaseFence" in renderer
checks["BACK is validated before it can become FRONT"] = \
    "void ValidatePublication" in readout and \
    "M8_COUNTER_RENDER_PUBLICATION_INVALID" in readout and \
    "ValidatePublication" in native
checks["BACK compares its own records by canonical version"] = \
    "_M8RenderVersionsRead[physicalSlot]" in readout and \
    "M8_RENDER_RECORD_VERSION" in world

failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(("PASS " if ok else "FAIL ") + name)
if failed:
    print(f"\n{len(failed)} closure checks failed.", file=sys.stderr)
    sys.exit(1)
print(f"\nPASS: {len(checks)} M8 sparse-readout closure checks.")
