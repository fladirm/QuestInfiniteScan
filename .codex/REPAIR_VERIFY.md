# Quest Merkaba Repair Verification

All results below are filled with exact commands and outcomes as each checkpoint closes.

## R1 Geometry Authority

Commit `3d14f65` proved useful shared-authority plumbing but encoded a superseded
96-microtriangle exact-Boolean-union interpretation. R1b preserves only the plumbing
and replaces its geometry authority.

Commands:

```bash
/mnt/kingston-unity/Unity/Hub/Editor/6000.5.9f1/Editor/Unity \
  -batchmode -nographics -quit \
  -projectPath /mnt/kingston-unity/Unity/Projects/QuestMerkabaScanHost \
  -executeMethod Genesis.RoomScan.Editor.MerkabaCanonicalGeometryGenerator.GenerateForBatch
Tools/unity/run_merkaba_tests.sh
```

Corrected result:

- CPU-to-HLSL generation: PASS; 14 vertices, 8 directions, 32 possible triangles.
- Direct-rule EditMode suite: 25 passed, 0 failed, 0 skipped.
- Isolated kernel: exactly 8 octahedron faces, 0 tip sides.
- Each of 8 body-diagonal pairs: connecting base suppressed and 3 tip sides emitted on both kernels; 10 active triangles per kernel.
- Neighbour removal restores base; axis/face-diagonal neighbours do not activate tips.
- Chunk-border and negative-coordinate translation invariance: PASS.
- Cube-axis-normal anti-regression: PASS.
- Result XML: `/mnt/kingston-unity/Builds/TestResults/merkaba-results.xml`.
- Log: `/mnt/kingston-unity/Builds/TestResults/merkaba-tests.log`.

## R2 Shared Live/GLB Geometry

- Production CPU topology: direct 32-bit rule mask.
- GPU topology across chunk border equals CPU mask: PASS.
- Live shader consumes generated primitive vertex IDs: shader compile PASS.
- GLB isolated kernel: 8 triangles / 24 vertices, all non-axis normals: PASS.
- GLB body-diagonal pair: 20 triangles / 60 vertices: PASS.
- Old `MerkabaTopology`, cube `BoundaryPatchCount`, `PatchVertex`, and cube shader vertex authority removed.

## R3 Depth Pipeline

Command:

```bash
Tools/unity/run_merkaba_tests.sh
```

Result at R3 checkpoint:

- Unity EditMode: 30 passed, 0 failed, 0 skipped.
- `DepthNormals.compute`: all padded threads guard dimensions before reads; border taps are forward/backward bounded; both array layers finite and written.
- `DepthDilation.compute`: signed tap coordinates are validated before reads; both layers independently retain their depth.
- Exact default dilation steps: `256,128,64,32,16,8,4,2,1`.
- Raw-frame cadence fixture: repeated consumer call on one sensor version does no preprocessing; skipped intermediate versions collapse to the latest frame.
- Production integration fixture with invalid left eye and valid right eye reaches OCCUPIED.
- Production false-foreground carve and true-wall preservation remains PASS.
- C# and compute shader import/compile: PASS.
- Result XML: `/mnt/kingston-unity/Builds/TestResults/merkaba-results.xml`.
- Log: `/mnt/kingston-unity/Builds/TestResults/merkaba-tests.log`.

## R4 Integration and Carve

Command:

```bash
Tools/unity/run_merkaba_tests.sh
rg -n "IntegrateMerkaba|IntegrationChunkCount \\* MerkabaConstants.KernelsPerChunk|_MerkabaIntegrationChunkCount \\* MERKABA_KERNELS_PER_CHUNK" Runtime Tests
```

Result at R4 checkpoint:

- Unity EditMode after R4b correction: 33 passed, 0 failed, 0 skipped.
- Old production domain: up to `48 × 32768 = 1,572,864` depth projections/tick.
- New production domain: depth pixels generate a bounded deduplicated union of three ray-band candidates and three dominant-axis boundary guards; deduplicated SURFACE and existing-evidence CARVE queues dispatch indirectly.
- A single-pixel off-axis GPU fixture proves the queue retains at least one diagonal ray-derived lattice transition absent from the dominant-axis guard set, and equals the exact deduplicated union.
- `IntegrateSurfaceCandidates` re-runs the preserved stereo depth relation, normal, dilation/disparity, distance and angle quality predicates; work-list broadening cannot add positive evidence without full SURFACE revalidation.
- Test fixture explicitly asserts surface work is less than the resident dense state count.
- Sparse production GPU path: false 1 m foreground removed; true 2 m wall retained.
- Right-eye-only valid depth reaches occupied state through the actual sparse path.
- Surface and carve cross the negative z=-32/-33 chunk boundary identically.
- Empty frustum residency consists only of transient pages and leaves canonical chunk count zero.
- Repeated FREE observations over untouched world allocate zero canonical chunks.
- An allocated surface chunk retains useful local signed FREE evidence without allocating neighbouring air.
- Dense `IntegrateMerkaba` kernel/symbol and dense total-work multiplication search: zero matches.
- Result XML: `/mnt/kingston-unity/Builds/TestResults/merkaba-results.xml`.

## R5 Residency

Command:

```bash
Tools/unity/run_merkaba_tests.sh
```

Result:

- Unity EditMode: 35 passed, 0 failed, 0 skipped.
- Derived canonical halo: exactly `6 × 32²` bits = 192 uints = 768 bytes/chunk; persistence remains `KernelState` only.
- Production GPU topology across simultaneous X/Y/Z chunk boundaries: resident, pending eviction, summarized nonresident, and reloaded masks are bit-identical.
- A pending page remains in the live 27-slot page-neighbour table until its readback completes.
- Eviction scheduling is bounded to one outstanding page and publishes the summary before removing live lookup.
- Render refresh leaves integration slots unchanged, includes transient GPU pages and honors an independently farther render range.
- A 5 cm camera move retains the identical visible page set under the one-chunk guard/hysteresis rule.
- Renderer invokes render residency every `LateUpdate`, including while scanning.

## R6 Publication

Commit: `d0874f9422807acdb0c9611e1e663edd48c921af`

Command:

```bash
Tools/unity/run_merkaba_tests.sh
```

Result:

- Unity EditMode: 37 passed, 0 failed, 0 skipped.
- Dirty build shape: `dirtyChunkCount × 512 × 64`; exactly one thread per 32³ lattice kernel and no serial kernel loop/group barrier.
- Per occupied kernel: eight body-diagonal occupancy queries and one atomic reservation for all 8..24 actual triangle records.
- Static unchanged publication: version/count/draw args remain bit-identical and zero topology/publication workgroups are emitted.
- Overflow fixture: exact required count 8 against capacity 4; prior bank/count=2/args/version=7 remain visible and unchanged.
- Exceptional migration copies the active source bank in parallel; successful capacity-8 rebuild atomically publishes count=8/version=8.
- Runtime record storage is one contiguous two-bank buffer; bank selection is an arithmetic offset, not a per-vertex dual-buffer branch.
- Residency movement alone does not enqueue topology rebuilds; occupancy transitions are the only normal publication source.
- Shader import/compile: PASS; no lost compiler connection, invalid kernel, or shader error in final log.
- Result XML: `/mnt/kingston-unity/Builds/TestResults/merkaba-results.xml`.
- Log: `/mnt/kingston-unity/Builds/TestResults/merkaba-tests.log`.

## R7 UX and Export Shell

Commit: `f6d011a8e16dd84ce9df83d734f88fb2d04bd6e4`

Commands:

```bash
Tools/unity/run_merkaba_tests.sh
Tools/gltf/validate_merkaba_glb.sh
Tools/gltf/validate_merkaba_glb.sh \
  /mnt/kingston-unity/Builds/TestResults/merkaba-fixture.glb
```

Result:

- Unity EditMode: 52 passed, 0 failed, 0 skipped; C#, shader, UXML and USS import clean.
- Opacity: 0..1 live slider, default 1.0, CPU-selected opaque/ghost material state, no dynamic opacity branch in the Merkaba shader.
- SAVE/LOAD/EXPORT: one operation state; indeterminate spinner, measurable progress bar, current stage text, reactive busy button labels.
- Export evidence: full canonical snapshot including local negative evidence, centralized `ExportKnownFreeThreshold = -OccupiedOnThreshold`.
- Cleanup: one sparse 3x3x3 closing, strong-FREE veto, observed-frontier selection, two-sided retention, component fallback, deterministic synthetic colour.
- Rear occupancy pruning, UNKNOWN single-cell heal, real opening preservation, large opening, diagonal wall, chunk-border equivalence, deterministic ordering, and no canonical mutation: PASS.
- Final geometry after cleanup comes only from shared direct Merkaba primitives; cube normals/faces cannot leak from the morphological structuring element.
- Fixture GLB: 4,948 bytes, 120 vertices/indices = 40 triangles, POSITION/NORMAL/COLOR_0, metallic 0, roughness 0.85.
- glTF Validator: 0 errors, 0 warnings.
- Explicit-path validator completed in 0.15 s and validated the supplied fixture without launching Unity or substituting another file.

## R8 Full Local Verification and APK

Commit: `fbfc07a`

Commands:

```bash
Tools/unity/run_merkaba_tests.sh
Tools/unity/build_merkaba_vulkan_timestamps.sh
Tools/unity/build_merkaba_apk.sh
unzip -l /mnt/kingston-unity/Builds/QuestMerkabaScan/QuestMerkabaScan-release.apk
```

Result:

- Unity EditMode: 57 passed, 0 failed, 0 skipped.
- Six fixed real GPU timestamp stages: DEPTH_PREPROCESS, SURFACE_INTEGRATION, CARVE_INTEGRATION, TOPOLOGY_UPDATE, PUBLICATION_COMPACTION, MERKABA_DRAW.
- No Unity Profiler, ProfilerRecorder, CPU stopwatch, or FPS-derived stage timing in production.
- Timestamp cadence: one sampled frame after 2 s, then at 5 s intervals; normal frames issue no timing events/readbacks.
- Native plugin: ARM64 Android API 29, NDK r27c, 16 KB ELF segment alignment; required exported ABI symbols present.
- Fresh APK: 63,166,399 bytes, mtime 2026-08-28 19:14:37 +0200.
- APK SHA-256: `a5607650086656cad8f76b1fbd052c76c4dac8271e286223d8cba0490b3c39ac`.
- APK contains `lib/arm64-v8a/libMerkabaVulkanTimestamps.so`; no Sigma plugin/path present.
- Unity build log: `Build Finished, Result: Success` and fresh-output verification PASS.

## R9 Device Evidence

- Device: authorized Quest 3S `340YC20G7X0QZ4`.
- Install: `adb -s 340YC20G7X0QZ4 install -r -d <APK>` returned `Success`.
- Launch: `adb -s 340YC20G7X0QZ4 shell am start -n com.genesis.questmerkabascan/com.unity3d.player.UnityPlayerGameActivity` succeeded; PID 15158 remained alive.
- Idle/non-scanning performance: 72/72 Hz, App ~3.9 ms, CPU&GPU ~5.0-5.6 ms, GPU%=0.41.
- Active scan, timestamps, fresh GLB, opacity/progress interaction, and scan screenshots: PENDING real VR control input.
