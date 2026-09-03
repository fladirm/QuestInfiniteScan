# Durable Merkaba decisions

> Current production closure authority: [`contr.md`](../contr.md). Read and follow it verbatim; it supersedes conflicting historical notes in this file.

- Chunk edge is 32 kernels: 32^3 dense states per allocated sparse chunk, 0.8 m wide.
- Coordinates are signed `int3`; chunk/local split is mathematical floorDiv/floorMod.
- Evidence is signed saturating fixed-point with distinct occupied-on and occupied-off
  thresholds; occupancy is a cached minimal flag, never the authority.
- RGB is stored packed per kernel with an integer colour-confidence accumulator.
- Canonical support is the exact 5 cm cube decomposition into one central octahedron,
  eight corner tips, and twelve edge wedges; live fixed boundary patches are frozen
  subdivisions of the external edge-wedge faces.
- A 24-bit boundary-patch mask (six faces times four half-step quadrants) is sufficient;
  each patch emits two fixed triangles. It is derived only from the 26 neighbours.
- Patch ownership is the lexicographically least occupied centre sharing a coplanar
  patch; any occupied centre one half-support outward suppresses the patch as interior.
- Topology has exactly the specified immediate 26 inputs. Kernels separated by an empty
  lattice centre remain distinct close sheets even where their 5 cm support bounds touch;
  an unobservable distance-two kernel cannot silently become a topology input.
- Topology dirtiness propagates only to self plus the 26 local neighbours on occupancy
  threshold transitions.
- GPU residency is a bounded 96-slot LRU working set; at most 48 frustum chunks integrate
  and 64 frustum-culled chunks compact/render in one coarse pass. Eviction uses one-shot
  asynchronous page readback; ordinary scanning/rendering has no CPU readback.
- Derived topology masks live per resident GPU slot. A transition dirties only its 3x3x3
  kernel neighbourhood; a residency edge dirties only the changed page and adjacent pages.
- The target host is `/mnt/kingston-unity/Unity/Projects/QuestMerkabaScanHost` and embeds
  `/mnt/aidisk/prace/simplescan` via `Packages/com.genesis.roomscan`.
- GLB mechanics come from the target's own historical `e9f37c1` writer/validator, adapted
  to Merkaba kernel boundary readout; PRISM world/export controllers are not imported.
- Persistence format v1 stores sorted chunk coordinates followed by dense 16-byte
  evidence/RGB/confidence/flag records, plus integration count and spatial-anchor metadata.
  Empty never-observed chunks and all derived topology/render data are omitted.
- GLB mirrors Unity X and reverses triangle winding, matching the proven historical writer;
  it emits float POSITION/NORMAL, normalized RGBA8 COLOR_0, uint indices, and one white,
  metallic-zero, roughness-0.85 material without UVs or textures.
- Quest UI styling/ray mechanics come from the current read-only donor UI files, with all
  controller bindings rewritten to the one Merkaba scanner.
- The sole lifecycle component is `RoomScanner`; it directly owns the retained sensor
  frontend and one grid/integrator/renderer/persistence/export set, without backend modes.
- Occupancy-to-empty transitions discard stale RGB/confidence so a disproved foreground
  cannot bias a later real surface; signed free evidence remains reversible canonical state.
- GPU pages that read back with no canonical state are removed from the sparse CPU map.
- The VR menu exposes exactly Start/Resume, Stop, Save, Load, New/Clear, and Export GLB;
  left thumbstick toggles it and the right controller ray/trigger selects controls.
- The canonical target setup writes one Android manifest, min SDK 32, Vulkan/ARM64/IL2CPP,
  the target scene, and required scene/anchor/headset-camera permissions.
- Meta SDK 205 injects a workstation address/token during Android preprocess even when its
  DevAgent is disabled; a target-only callback at order 10000 sanitizes the build asset.
- Target Gradle state is isolated under the target build root because the user-level Gradle
  daemon path is a broken external symlink; Unity's bundled SDK/OpenJDK/NDK remain canonical.
- Final APK identity is `com.genesis.questmerkabascan`, version 0.1.0/code 8, at the frozen
  target APK path; fresh-mtime and Unity success-marker checks are mandatory.
