# MIGRATION_LEDGER

C00 deliverable. Authority: `FINALSCAN_V4_CONTRACT.md` §0 ("Kód přenesený kvůli platformě musí být přejmenovaný, vyčištěný, minimální a doložený v MIGRATION_LEDGER.md"). Companion: `VULKAN_DONOR_LEDGER.md` (native mechanisms and invariants), `FEATURE_PARITY.md` (user-facing capabilities).

Scope rule: only **platform** code and patterns are migrated (Unity/Meta/Android/Vulkan/Gradle plumbing and product I/O). Nothing of the donor scanner core (lattice, Merkaba, Flower, Sigma, membrane, readout compiler) crosses this boundary.

Donor checkouts (all branches of `fladirm/QuestInfiniteScan`):

* `SS` = `/mnt/aidisk/prace/simplescan` (`fix/merkaba-runtime-root-causes`)
* `US` = `/mnt/aidisk/prace/uniscan` (`refactor/m8-dual-sphere-flower-rev-b`, `9cfed011`)
* `OS` = `/mnt/aidisk/prace/otherscan` (`forensic/n4r-cut-e-scheduler`)
* Host template: `/mnt/kingston-unity/Unity/Projects/QuestInfiniteScanHost`

Status values: PLANNED (not yet ported), PORTED (in FinalScan, cleaned, tested), REJECTED.

## 1. Build, host project, deployment

| Item | Donor path | Why needed | Rename / clean / minimize | FinalScan location | Status |
|---|---|---|---|---|---|
| Frozen dev environment script | `SS`/`US` `Tools/storage/dev_environment.sh` | one source of Unity/NDK/SDK/adb paths on kingston | `QIS_*` → `FS_*`; drop Merkaba build dir vars; add `FS_HOST_PROJECT=/mnt/kingston-unity/Unity/Projects/FinalScanHost`, `FS_BUILD_DIR=/mnt/kingston-unity/Builds/FinalScan` | `Tools/env/dev_environment.sh` | PLANNED (C00) |
| Host project creation from template + package symlink | `US` `Tools/unity/create_merkaba_host.sh` | Unity host with Meta XR SDK 205 / OpenXR 1.17 / URP 17.5 / ARFoundation 6.5 config without committing a Unity project | rsync template excluding `Scenes/`, `*Sigma*`, `*PRISM*`, `*Merkaba*`, donor panels; symlink `Packages/com.finalscan.scanner` → repo | `Tools/unity/create_host.sh` | PLANNED (C00) |
| Install verification | `US` `Tools/unity/verify_unity_install.sh` | fail-closed check of editor, host, symlink, adb | rename vars only | `Tools/unity/verify_install.sh` | PLANNED (C00) |
| Two-phase batch build (Prepare execute-method, then Build execute-method) with log success markers, fresh-mtime and `error CS`/`Shader error` grep | `US` `Tools/unity/build_merkaba_apk.sh` | proven Unity 6000.5 batch build path on this machine; bounded RAM via `systemd-run --scope` | drop CAPTURE/BINARY_ONLY gating of the Merkaba pipeline pack; keep zipalign `-P 16` + apksigner; debug keystore for dev, release keystore via env | `Tools/unity/build_apk.sh` | PLANNED (C01) |
| Deploy via bundled adb with single authorized serial | `US` `Tools/unity/deploy_merkaba_apk.sh` (27 lines) | install `-r -d`, permission grants, launch, logcat capture | add `pm grant` for HEADSET_CAMERA / CAMERA / USE_SCENE; wake/unlock guard (evidence: launch blocked by controller dialog / asleep) | `Tools/unity/deploy_apk.sh` | PLANNED (C01) |
| Editor test runner | `US` `Tools/unity/run_merkaba_tests.sh` | EditMode tests in batch | rename | `Tools/unity/run_tests.sh` | PLANNED (C02) |
| Native plugin build (NDK clang from Unity install, `-Wl,-z,max-page-size=16384`, `--no-undefined`, strip, `.pluginmeta`) | `US` `Tools/unity/build_merkaba_vulkan_timestamps.sh`, `MerkabaVulkanTimestamps.pluginmeta` | correct 16 KB page alignment and Unity PluginAPI include path | one library `libfinalscan_native.so`; multiple translation units (not one 3–4k-line cpp); no shader `.inc` generation inside the build script (moved to `Tools/shaders/`) | `Tools/native/build_native.sh`, `Native/CMakeLists.txt` | PLANNED (C02) |

## 2. Unity setup wizard (Editor)

| Item | Donor path | Why needed | Rename / clean / minimize | FinalScan location | Status |
|---|---|---|---|---|---|
| Programmatic scene: `OVRManager`, `OVRCameraRig`, `OVRPassthroughLayer` underlay, `EnvironmentDepthManager`, event system + `OVRInputModule` | `US` `Editor/RoomScanSetupWizard.cs:161-260` | reproducible scene without committed `.unity` assets | `EnsureQuestRigAndDepthManager` → `FinalScanHostSetup.EnsureRig`; drop canonical-scanner wiring; keep depth manager | `Editor/FinalScanHostSetup.cs` | PLANNED (C01) |
| Player settings (Vulkan only, IL2CPP, ARM64, minSdk 32 / target 36, display refresh, color space) | `US` `Editor/RoomScanSetupWizard.cs:130-160`, `OVRProjectConfig` commit | Quest build requirements; Meta camera feature rejects minSdk 29 (m16 evidence) | keep; explicit 72 Hz request | same | PLANNED (C01) |
| Permission manifest (`horizonos.permission.HEADSET_CAMERA`, `android.permission.CAMERA`, `com.oculus.permission.USE_SCENE`, hand tracking) | `US` `Editor/RoomScanSetupWizard.cs:293-360`, `Editor/VRProjectBootstrap.cs` (manifest post-processing) | PCA + Env Depth permissions | rename package id `com.finalscan.quest`; no Merkaba providers except pipeline bootstrap if C02 keeps it | `Editor/FinalScanManifest.cs` | PLANNED (C01) |
| URP asset/renderer configuration (renderer feature slot, MSAA, no post) | `US` `Editor/RoomScanSetupWizard.URP.cs` | URP 17.5 settings for XR multiview + one indirect draw pass | drop Merkaba render feature; add FinalScan surfel pass feature | `Editor/FinalScanUrpSetup.cs` | PLANNED (C05) |
| Building-block discovery (borrow `PassthroughCameraAccess` from Meta building blocks if present) | `US` `Editor/RoomScanSetupWizard.BuildingBlocks.cs`, `SS` `PassthroughCameraProvider.cs` `DiscoverExactCameras` (`f60ac68`) | avoid duplicate camera sessions | only if MRUK adapter is the production ingest (C01-A decides) | `Runtime/Sensor/MrukCameraAdapter.cs` | PLANNED (C03) |
| Build method + `BuildReport` checks, sanitizer of donor host artifacts and Meta diagnostics (`SanitizeMetaDiagnostics`) | `US` `Editor/RoomScanSetupWizard.cs:362-530`, `Editor/MerkabaBuildSanitizer.cs` | deterministic APK content; strip Meta immersive debugger and dev-agent assets | rename | `Editor/FinalScanBuild.cs` | PLANNED (C01) |

## 3. Sensor plumbing

| Item | Donor path | Why needed | Rename / clean / minimize | FinalScan location | Status |
|---|---|---|---|---|---|
| PCA history ring per eye + pairing by minimal joint spread; copy of producer texture in the same render callback that latches timestamp/pose/intrinsics (RGBD-1, RGBD-6 lessons) | `SS` `Runtime/Camera/PassthroughCameraProvider.cs` (614 lines) | proven fix for 46 % pair acceptance and stale-image reads | keep pattern only; becomes the MRUK adapter behind `CameraFrameLease`; window = multiple of measured camera period; monotonicity check | `Runtime/Sensor/MrukCameraAdapter.cs`, `Runtime/Sensor/StereoPairing.cs` | PLANNED (C03) |
| Environment Depth capture: `Tex2DArray` 2 layers, per-eye FOV/pose/near-far/timestamp, owned copy, restart after resume (`b34910b`), single acquire per frame | `SS` `Runtime/Core/DepthCapture.cs` (1533 lines, device path :1178-1212) | correct XR_META_environment_depth consumption | strip FINE cursor and Merkaba resources; minimal owned copy + metadata | `Runtime/Sensor/EnvironmentDepthSource.cs` | PLANNED (C09) |
| Sensor clock mapper (bracketing `OVRPlugin.GetTimeInSeconds` around `DateTime.UtcNow`) | `SS` `Runtime/Camera/SensorClockMapper.cs` (86 lines) | only as path **C** fallback of the clock gate (§4.2) with explicit uncertainty | never "exact"; replaced by native `XR_KHR_convert_timespec_time` on paths A/B | `Runtime/Sensor/ClockGate.cs` (+ native) | PLANNED (C03) |
| XR runtime guard / pause handling | `SS` `Runtime/Core/XRRuntimeGuard.cs`, `RoomScanner.cs:365-377, 1279-1343` | sleep/wake, tracking loss | pattern only; native jobs must be finished or quarantined on pause (§19) | `Runtime/Lifecycle/XrLifecycle.cs` | PLANNED (C21) |
| Room anchor manager (create only on NEW, localize on OPEN, relocation matrix) | `SS` `Runtime/Core/RoomAnchorManager.cs`, `RoomSpaceRoot.cs` | OS Spatial Anchor as localization observation | generalized to AnchorGraph: N anchors, observation not origin | `Runtime/Anchors/PlatformAnchorObserver.cs` | PLANNED (C19) |

## 4. Native executor skeleton

| Item | Donor path | Why needed | Rename / clean / minimize | FinalScan location | Status |
|---|---|---|---|---|---|
| `IUnityGraphicsVulkanV2` intercept + `vkCreateDevice` hook + safe-family queue injection | `US` `Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp:508-917, 2609-2630` | only proven way to get a second queue on Unity's device | split into `Native/device/*.cpp`; add OS-style fallback; never force extensions | `Native/device/UnityVulkanIntercept.cpp` | PLANNED (C02) |
| Fallback to original `vkCreateDevice` when injection fails | `OS` `Runtime/SigmaPrism/.../SigmaVulkanTimestamps.cpp:539-560` | app must start on any driver | pattern | same | PLANNED (C02) |
| Frame-paced sliced jobs (`RecordJobSlice`, `SubmitNextSlice`) | `OS` `SigmaVulkanTimestamps.cpp:2392, 2561` | P4 COLD quanta without stalls | pattern → scheduler continuation cursor | `Native/scheduler/JobScheduler.cpp` | PLANNED (C02) |
| Per-owner timestamp query ranges (`f2cfe26`) | `SS` `Runtime/Telemetry/MerkabaGpuTimestamps.cs`, native query pools | correct multi-owner GPU timing | pattern | `Native/telemetry/GpuTimestamps.cpp` | PLANNED (C02) |
| Job lifecycle invariants (fence creation before resource access, prepare ownership, atomic error publication) | `US` `9cfed011` (cpp:1234-1247, 2075-2108, 2827-2924) | SIGSEGV / use-after-free classes | invariants only (VULKAN_DONOR_LEDGER §5); code shape rejected | `Native/scheduler/Job.cpp` | PLANNED (C02) |
| SPIR-V reflection → generated binding/offset tables, cross-checked against C# and HLSL constants | `US` `Tools/unity/generate_merkaba_native_executor_shaders.py:319-401, 623-667` | single source of truth for ABI (no literal offsets) | rewrite: emits C header + C# constants + validates kernel envelope (§15.6); push constants instead of uniform-name hashing | `Tools/shaders/gen_abi.py` | PLANNED (C02) |
| Pipeline binary CAPTURE / BINARY_ONLY workflow + Java ContentProvider bootstrap loading the `.so` before UnityPlayer | `US` `Runtime/Telemetry/Native/MerkabaPipelineBinary.h`, `Runtime/Plugins/Android/MerkabaPipelineBootstrap.java`, `Tools/unity/generate_merkaba_pipeline_pack.py`, `MERKABA_PIPELINE_DELIVERY.md` | pre-warmed pipelines keyed by `pipelineBinaryUUID` | optional feature: `VkPipelineCache` + warm-up screen fallback; no forced extensions; no "no SPIR-V fallback" | `Native/pipeline/PipelineStore.cpp`, `Tools/pipeline/pack.py` | PLANNED (C02, closed in C30) |
| C# ↔ native job descriptor validated by `structSize` + `abiVersion` | `US` `Runtime/Telemetry/MerkabaNativeVulkanExecutor.cs`, cpp:2735-2739 | ABI drift detection | keep principle, generated constants | `Runtime/Native/NativeBridge.cs` | PLANNED (C02) |
| Async readback completion tokens instead of `GraphicsFence` (`ba286ac`, `57ab534`) | `SS` `Runtime/Merkaba/MerkabaGrid.Storage.cs`, `MerkabaIntegrator.cs` | `AsyncQueueSynchronisation` fences throw on Quest | pattern for the C# side only | `Runtime/Native/CompletionToken.cs` | PLANNED (C02) |

## 5. Product I/O

| Item | Donor path | Why needed | Rename / clean / minimize | FinalScan location | Status |
|---|---|---|---|---|---|
| SAF package picker (Android Java) | `SS` `Runtime/Plugins/Android/MerkabaPackagePicker.java` (280 lines) | export "save as" to user storage | rename `FinalScanFilePicker.java`; strip Merkaba naming | `Runtime/Plugins/Android/FinalScanFilePicker.java` | PLANNED (C28) |
| GLB writer (buffers, accessors, X mirror, winding) | `SS` `Runtime/Merkaba/MerkabaGlbWriter.cs` (607 lines) | glTF 2.0 encoding | input becomes FinalScan export mesh + textures; streaming chunks | `Runtime/Export/GlbWriter.cs` | PLANNED (C28) |
| 3D Tiles writer (leaf sizes, ZIP) | `SS` `Runtime/Merkaba/MerkabaTilesetWriter.cs` (605 lines) | tileset export | decoupled from membrane oracle | `Runtime/Export/TilesetWriter.cs` | PLANNED (C28) |
| Three.js web viewer + licenses | `SS` `Runtime/Resources/Merkaba/QuestMerkabaScanViewer*.txt` | browser preview of exports | rebrand; keep licenses | `Runtime/Resources/FinalScan/Viewer.html.txt` | PLANNED (C28) |
| GLB verification tooling | `SS` `Tools/gltf/verify_merkaba_glb.mjs`, `validate_merkaba_glb.sh`, `package.json` | export acceptance | rename | `Tools/gltf/` | PLANNED (C28) |
| Journal + checkpoint store: append-only overlay with per-record CRC32 and fsync, rollback truncation, `.tmp` + `File.Replace` durable publish, index rebuild on open | `SS` `Runtime/Merkaba/MerkabaSsdStore.cs` (711 lines), `MerkabaPersistence.cs:432` `MerkabaFilePublishing` | crash-safe persistence | new record format (pages, AnchorGraph, free-space); no `ChunkSize=32` header relic; no 28 B/8192 B tile format | `Runtime/Storage/PageJournal.cs`, `Runtime/Storage/DurableFile.cs` | PLANNED (C14, C26) |
| Session catalog (`session.json`, rename/delete, recovery) | `SS` `Runtime/Merkaba/MerkabaSessionCatalog.cs` (317 lines) | session management UI | rename; manifest gains AnchorGraph + page index | `Runtime/Storage/SessionCatalog.cs` | PLANNED (C26) |
| Content-addressed model library | `SS` `Runtime/Merkaba/MerkabaDesignLibrary.cs` (871 lines) | imported model storage | keep SHA-256 scheme; instances `AnchorID+transform` | `Runtime/Models/ModelLibrary.cs` | PLANNED (C25) |
| Paint engine tools and design document | `SS` `Runtime/Merkaba/MerkabaPaintEngine.cs`, `MerkabaDesignDocument.cs` | paint parity | strokes rebased to chart coordinates | `Runtime/Annotation/PaintEngine.cs` | PLANNED (C24) |
| Artifact viewer interactions (grab, two-hand, plan view, align, annotations) | `SS` `Runtime/UI/MerkabaArtifactViewer.cs` (4065 lines) | viewer parity | split into ≤ 500-line components; bindings per FEATURE_PARITY | `Runtime/Viewer/*` | PLANNED (C24, C25) |

## 6. UI shell and input

| Item | Donor path | Why needed | Rename / clean / minimize | FinalScan location | Status |
|---|---|---|---|---|---|
| Controller ray shader + driver (hands as pose source, UI-wins raycast) | `SS` `Runtime/UI/ControllerRay.shader`, `ControllerRayDriver.cs` (329 lines), `VRDocumentRaycaster.cs` | interaction | rename | `Runtime/UI/ControllerRay*.cs` | PLANNED (C22) |
| World-space UI Toolkit panel + follower | `SS` `Runtime/UI/DebugMenu.uxml/.uss`, `DebugMenuController.cs` (1390 lines), `DebugMenuFollower.cs` | panel host | production panel per FEATURE_PARITY §9; diagnostics separate document, hidden by default | `Runtime/UI/ScanPanel.*` | PLANNED (C22) |
| Input mapping (thumbstick click menu, trigger select, grip erase) | `SS` `Runtime/RoomScanInputHandler.cs` (87 lines) | parity | rename | `Runtime/UI/InputMap.cs` | PLANNED (C22) |

## 7. Forbidden to migrate

Nothing in this list may appear in FinalScan under any name (contract §0, §22):

* Merkaba / Flower / Sigma / PRISM / M8 / M16 lattice, `KernelState`, tile/chunk/block addressing, cuckoo tile hash (`MerkabaSpatial.cs`), evidence counters.
* Membrane / overlap-shell readout compiler (`MerkabaOverlapShell.cs`), generated-HLSL string literals, `MerkabaSphereFlowerCodegen.cs`.
* Dual sphere / excavation view / DIRT interface; TSDF, Surface Nets, marching cubes, trilinear.
* `StereoRgbdRefine.compute` joint endpoint kernel and 9-pass depth dilation (replaced by rectified narrow-band stereo + planar path, §7).
* Draw intercept `MerkabaFlowerDraw.h` (`vkCmdDrawIndexedIndirect` → `IndirectCount` rewrite inside Unity's pass).
* Single global job slot `_activeJob`, per-frame readout resubmission, 50 ms storage poll retirement.
* Uniform-name FNV hashing at runtime; hard-coded argument-buffer offsets (`kFlower*ArgsOffset`).
* Forced device extensions and BINARY_ONLY "no SPIR-V fallback".
* Source-text tests (`File.ReadAllText` + `IndexOf` tripwires).
* Stale docs (`README.md`, `ALGORITHM.md` describing dead structures) and persistence header relics (`ChunkSize=32`).
* Object reconstruction (ORT/TripoSR) — out of scope by decision, not a migration candidate.
