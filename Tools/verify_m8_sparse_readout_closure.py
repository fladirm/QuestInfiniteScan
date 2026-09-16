#!/usr/bin/env python3
"""Static closure gate for the M8 sparse readout publication cut."""
from pathlib import Path
import sys

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
    "live metric skin consumes measured signed plane offset":
        "M8SkinMeasuredShift" in readout and
        "M8SkinGridVertex(int3 globalCoord, KernelState state" in readout,
    "export metric skin consumes the same measured state/context resolver":
        "kernel.Coord, kernel.State, context, facelet.A" in exporter and
        "MerkabaSkin.FaceletColor" in exporter,
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
    "native stale readout cannot execute":
        "if (kind == JobKind.Readout) return false;" in native,
    "42/80 Skin SSOT remains canonical":
        "VertexCount = 42" in skin and "FaceletCount = 80" in skin,
    "75mm half-lattice skin placement is absent":
        "0.5f * MerkabaConstants.LatticeStep" not in skin,
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

failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(("PASS " if ok else "FAIL ") + name)
if failed:
    print(f"\n{len(failed)} closure checks failed.", file=sys.stderr)
    sys.exit(1)
print(f"\nPASS: {len(checks)} M8 sparse-readout closure checks.")
