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
grid = text("Runtime/Merkaba/MerkabaGrid.Gpu.cs")
renderer = text("Runtime/Merkaba/MerkabaGridRenderer.cs")
integrator = text("Runtime/Merkaba/MerkabaIntegrator.cs")
native = text("Runtime/Telemetry/MerkabaNativeVulkanExecutor.cs")
skin = text("Runtime/Merkaba/MerkabaSkin.cs")
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
    "25mm carrier uses one sixth lattice half-vertex unit":
        "MerkabaConstants.LatticeStep / 6f" in skin,
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
    "live skin rides the measured signed plane offset":
        "M8SkinMeasuredShift" in readout and
        "M8SkinVertexPosition(int3 globalCoord" in readout,
    "live skin publishes shared vertices and real indices":
        "M8SkinUsedVertices" in readout and
        "M8SkinVertexRank" in readout and
        "_M8RenderIndices[M8SkinIndexSlot" in readout,
    "export consumes the same boundary authority":
        "MerkabaSkin.VertexPosition(kernel.Coord" in exporter and
        "MerkabaSkin.VertexColor(kernel.Coord" in exporter,
    "departed view pages are retired":
        "M8_RENDER_VIEW_MARK" in readout and
        "M8_RENDER_RETIRE_BASE" in readout and
        "void CollectRenderRebuildTiles" in readout,
    "scanner pending work does not starve graphics-queue readout":
        "HasPendingObservation" not in renderer and
        "HasPendingFineErase" not in renderer,
    "patch recount is parallel over current view":
        "void RecountRenderPatches" in readout,
    "legacy per-kernel overlap oracle absent from live path":
        '#include "MerkabaOverlapShell.generated.hlsl"' not in readout and
        "M8TryBuildMembranePatch" not in readout,
    "BuildTileCap tuning authority removed":
        "BuildTileCap" not in renderer and
        "_M8RenderBuildTileCap" not in readout,
    "32k render-slot dispatch removed":
        "slotGroups" not in renderer,
    "native readout executes on the scanner queue":
        "if (kind == JobKind.Readout) return false;" not in native and
        "AbiVersion = 4" in native and
        "SubmitNativeReadoutBuild" in renderer,
    "device publication is confirmed by the native fence copy":
        "TryReadCompletion(_completionRecord)" in renderer and
        "CreateGraphicsFence" in renderer and
        "Editor only" in renderer,
    "coverage radius is the world sphere":
        "ReadoutCoverageRadius = 6.4f" in renderer,
    "skin is one analytic authority, not a hardcoded table":
        "BuildBoundary()" in skin and "ConstraintsValue" in skin and
        "SupportHalfUnits = 6" in skin,
    "the support is 50 mm on the 25 mm lattice":
        "LatticeUnits = 6" in skin and
        "MerkabaConstants.LatticeStep / 6f" in skin,
    "no field sampling or marching reconstruction in the readout":
        "marching" not in readout.lower() and "SDF" not in readout and
        "TrilinearField" not in readout,
    "CPU export uses same Skin SSOT":
        "MerkabaSkin.Facelets" in exporter and
        "MerkabaSkin.CompatibleNeighbourMask" in exporter,
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
checks["capacity failure makes BACK non-publishable"] = \
    "MERKABA_READOUT_FAILED : MERKABA_READOUT_PUBLISHED" in readout
checks["publication is decided from the native completion copy"] = \
    "MerkabaExecutor_ReadCompletion" in text(
        "Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp") and \
    "TryReadCompletion" in renderer and "RejectPublication" in renderer
checks["skin classifies normals on the canonical 26-direction chart"] = \
    "MerkabaOverlapShell.NearestGridNormalStep" in skin and \
    "return MerkabaNearestGridNormalStep(normal);" in readout

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
