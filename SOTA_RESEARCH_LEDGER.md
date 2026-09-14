# FinalScan SOTA RESEARCH LEDGER

C00 deliverable. Web research performed 2026-09-14. Classification: **[OFFICIAL]** = Meta / Khronos / Android / Unity / Qualcomm first-party; **[COMMUNITY]** = credible third-party measurement, code, or peer-reviewed paper; **[UNVERIFIED]** = no source reached confirms it. Source date is the page's stated update/publication date where visible, otherwise the access date. "Supports" names the `FINALSCAN_V4_CONTRACT.md` section.

## A. Passthrough Camera API (PCA)

| Source | Date / version | Class | Claim | Supports |
|---|---|---|---|---|
| https://developers.meta.com/horizon/documentation/native/android/pca-native-overview/ | updated 2026-04-21 | OFFICIAL | Capture latency 20–40 ms; data rate 60 Hz; GPU ~1–2 % per stream; memory ~45 MB; max 1280×1280; YUV420; vendor tags `com.meta.extra_metadata.camera_source/position`; pinhole intrinsics only | §3.2, §4.6, §5 |
| https://developers.meta.com/horizon/documentation/spatial-sdk/spatial-sdk-pca-overview/ | 2026 | OFFICIAL | Same figures for Spatial SDK | §3.2 |
| https://developers.meta.com/horizon/reference/mruk/v83/class_meta_x_r_passthrough_camera_access/ | MRUK v83 | OFFICIAL | `MaxFramerate` default 60; "actual framerate may vary based on lighting and workload"; `GetCameraPose()` has no time parameter; `Intrinsics` = FocalLength, PrincipalPoint, SensorResolution, LensOffset | §3.2, §4.3 |
| https://developers.meta.com/horizon/documentation/unity/unity-pca-documentation/ | 2026 | OFFICIAL | 1280×960 since v74, 1280×1280 since v83; twelve resolutions via `GetSupportedResolutions()`; 1280×960 crop "smaller than what a user sees" | §3.2, R29 |
| https://developers.meta.com/horizon/documentation/unity/unity-pca-migration-from-webcamtexture/ | v81+ | OFFICIAL | Simultaneous access to both cameras | §4.4 |
| https://developers.meta.com/horizon/blog/new-era-mixed-reality-passthrough-camera-api-machine-learning-computer-vision/ | 2025-04-30 | OFFICIAL | v74 experimental, v76 production/Store; permission `horizonos.permission.HEADSET_CAMERA` | §2 |
| https://www.uploadvr.com/quest-passthrough-camera-api-experimental-out-now/ | 2025-03-14 | COMMUNITY (quoting Meta) | 1280×960 @ 30 fps, 40–60 ms latency at v74 — superseded | §3.2 (lower bound) |
| https://immernews.com/spatial-cameras-in-vr-from-quest-3-to-steam-frame-arcturus-with-project-phoenix-on-the-horizon/ | 2026-09-03 | COMMUNITY | v81 lower latency + dual camera; v83 1280×1280; Aug 2026 faster YUV→RGBA; "synchronized stereo pairs" April 2026 (unconfirmed by Meta) | §4.4 |
| https://github.com/samuelm2/OpenQuestCapture ; https://github.com/t-34400/QuestRealityCapture | 2025–2026 | COMMUNITY | L/R paired by nearest native timestamp; monotonic↔Unix base pairs stored | §4.4, §4.2 |
| https://learn.microsoft.com/en-us/dotnet/api/android.hardware.camera2.cameracharacteristics.sensorinfotimestampsource | Android API | OFFICIAL (Android semantics) | `SENSOR_TIMESTAMP` = start of exposure; comparable across subsystems only when `TIMESTAMP_SOURCE == REALTIME` | §4.2 gate |
| https://arxiv.org/pdf/2604.22118 | 2026 | COMMUNITY | Quest 3 tracking cameras use Fisheye62 (SLAM cameras, not RGB) | §4.6 (context only) |
| https://arxiv.org/html/2509.18929v1 | 2025-09-23 | COMMUNITY (partly simulated) | Throttling within 5–10 min of sustained 720p30 MR compositing; 6–7 GB RAM | §21.9, R8 |
| Quest TIMESTAMP_SOURCE value; rectified/undistorted output; exposure/ISO metadata; sustained 60 fps stereo at 1280×1280 | — | UNVERIFIED | Must be probed in C01 | §4.2, §4.6, R2, R5, R32 |

## B. Environment Depth

| Source | Date / version | Class | Claim | Supports |
|---|---|---|---|---|
| https://developers.meta.com/horizon/documentation/native/android/mobile-depth/ | 2026 | OFFICIAL | `XR_META_environment_depth`; one provider per app; acquire per frame between begin/end; own `nearZ/farZ`, per-eye `fov` + `pose`; display time/pose "likely not the same as your frame"; resolution only via swapchain state; hand removal replaces hands with estimated background; needs `USE_SCENE` | §6 |
| https://developers.meta.com/horizon/documentation/unity/unity-depthapi-overview/ | 2026 | OFFICIAL | Quest 3/3S only; min range ≈0.2 m; hand-occlusion accuracy degrades near headset | §6 |
| https://github.com/oculus-samples/Unity-DepthAPI | Core SDK ≥ v67 | OFFICIAL | `RemoveHands` requires hand tracking; Vulkan + Multiview required | §6 |
| https://community.khronos.org/t/quest-3-depth-api-with-xr-meta-environment-depth/110808 | 2024-05 | COMMUNITY | Swapchain cycles mod 4 | §6 (matches measured swapchain=4) |
| https://www.uploadvr.com/quest-3-mixed-reality-occlusion-v67-sdk-upgrade/ | 2024-07-22 | COMMUNITY | Low resolution; gaps at edges; usable to ~4 m; v67 cut cost 80 % GPU / 50 % CPU | §6, R26 |
| Resolution, cadence, latency, smoothing | — | UNVERIFIED on web; **measured locally**: 320×320×2, swapchain 4, 25 Hz, age ~21 ms | §6 |

## C. Display, frame timing, pose history

| Source | Date / version | Class | Claim | Supports |
|---|---|---|---|---|
| https://developers.meta.com/horizon/documentation/unreal/unreal-change-display-refresh-rate/ | 2026 | OFFICIAL | Default 72 Hz; Quest 3 supports 72/80/90/96/100/120; thermal throttling lowers refresh first | §3.1 |
| https://developers.meta.com/horizon/blog/bringing-phase-sync-to-mobile-vr/ | 2020-12-07 | OFFICIAL | Phase Sync adaptive frame timing; default in Meta OpenXR | §3.1 |
| https://developers.meta.com/horizon/documentation/unity/unity-openxr-settings-quest/ | 2026 | OFFICIAL | Late latching recommended | §3.1 |
| https://developers.meta.com/horizon/documentation/native/android/mobile-openxr-frames/ | 2026 | OFFICIAL | `predictedDisplayTime` increasing but not a unique frame id | §20 (use counter) |
| https://registry.khronos.org/OpenXR/specs/1.1/html/xrspec.html (§Time) | OpenXR 1.1 | OFFICIAL | Runtime must retain ≥ 50 ms historical pose data for `xrLocateSpace`; older requests best-effort | §4.3, R3 |
| https://registry.khronos.org/OpenXR/specs/1.0-khr/html/xrspec.html (`XR_KHR_convert_timespec_time`) | OpenXR | OFFICIAL | timespec ↔ XrTime conversion | §4.2 gate A |
| Meta runtime history beyond 50 ms; OVRPlugin history limits | — | UNVERIFIED | Own pose ring mandatory | §4.3 |

## D. Unity native Vulkan plugin

| Source | Date / version | Class | Claim | Supports |
|---|---|---|---|---|
| https://raw.githubusercontent.com/Unity-Technologies/NativeRenderingPlugin/master/PluginSource/source/Unity/IUnityGraphicsVulkan.h | current | OFFICIAL | `UnityVulkanInstance{instance, physicalDevice, device, graphicsQueue, queueFamilyIndex, getInstanceProcAddr, pipelineCache}`; V2 `AddInterceptInitialization(cb, userdata, priority)`; `CommandRecordingState` invalidated by resource access; `AccessTexture/AccessBuffer` must not be called from queue-access events; `kUnityVulkanGraphicsQueueAccess_Allow`; event config flags | §15.2, §15.3, §15.7 |
| https://github.com/frarees/unity-changelog/blob/main/2021.1.18.md | 2021.1.18 | OFFICIAL (mirror) | V2 interception introduced 2020.3.17 / 2021.1.18 | §15.2 |
| https://github.com/Unity-Technologies/com.unity.webrtc/issues/686 | 2022-04 | OFFICIAL (Unity repo) | V1 interception broke when a second interceptor (XR plugin) registered; priority matters | R22 |
| https://github.com/Unity-Technologies/NativeRenderingPlugin/issues/37 ; https://github.com/rudybear/unity-vulkan-interop | — | COMMUNITY | Hook `vkCreateDevice` inside `Hook_vkGetInstanceProcAddr`; full device override pattern | §15.2 |
| https://discussions.unity.com/t/vulkan-native-rendering-plugin-902950/902950 | 2022-12 | COMMUNITY | V2 does not expose active render-pass sample count; hook `vkCreateRenderPass` | §13.1 (why variant B is escape hatch only) |
| https://issuetracker.unity3d.com/issues/android-crash-when-using-vulkan-api-and-addinterceptinitialization-with-optimized-frame-pacing-enabled | — | UNVERIFIED (content unreachable) | Crash with interception + Optimized Frame Pacing | R23 |
| https://docs.unity3d.com/kr/6000.0/ScriptReference/Rendering.CommandBuffer.DrawProceduralIndirect.html | 6000.0 | OFFICIAL | GPU procedural indirect draw without vertex/index buffers | §13.1 variant A |
| https://developers.meta.com/horizon/blog/vulkan-for-mobile-vr-rendering/ ; https://developers.meta.com/horizon/documentation/unity/vulkan-subpasses/ ; https://developers.meta.com/horizon/documentation/native/android/gpu-tiled/ | 2019-08-02 / 2026 / 2024-08-16 | OFFICIAL | Keep work in tile memory; `STORE_OP_DONT_CARE`; avoid `vkCmdResolveImage` (~3 ms); subpass restrictions; RenderGraph support in 6000.3 | §13.2 |

## E. Adreno 740 (XR2 Gen 2) Vulkan

| Source | Date / version | Class | Claim | Supports |
|---|---|---|---|---|
| https://gist.github.com/tcoppex/6ef9f5f60ddae41d6198d2bdf4a40beb | driver 0.837.7, API 1.3.295, updated 2026-08-15 | COMMUNITY (first-party dump) | Present: synchronization2, timeline_semaphore, pipeline_binary, pipeline_creation_cache_control, pipeline_executable_properties, host_query_reset, 16/8-bit storage, float16/int8, global_priority, subgroup_size_control, dynamic_rendering, buffer_device_address, maintenance5, host_image_copy, QCOM tile/render-pass ext, ANDROID_external_memory_android_hardware_buffer, ray_query | §15.2, §15.3, §15.7, §5 |
| https://github.com/K11MCH1/AdrenoToolsDrivers/releases | v837 | COMMUNITY | Same driver family extracted from Quest 3 | §15 |
| https://vulkan.gpuinfo.org/displayreport.php?id=38321 | driver 512.805.0, 2025-04-16 (Windows x86_64 host, not Quest) | COMMUNITY | timestampPeriod 52.0833 ns; timestampComputeAndGraphics; maxComputeWorkGroupInvocations 1024; subgroup 64; family 0 = G\|C\|T ×4, family 1 = transfer ×1; no compute-only family; `uniformAndStorageBuffer16BitAccess` false | §15.2, §15.6, §15.8 (matches local measurement) |
| https://chipsandcheese.com/p/sizing-up-qualcomms-8cx-gen-3-igpu ; https://chipsandcheese.com/p/inside-snapdragon-8-gen-1s-igpu-adreno-gets-big | 2024-04-24 / 2022 | COMMUNITY | Adreno 6xx LPAC (low-priority async compute); 7xx BV/BR split | R6 |
| https://ratatoskr.run/linux-arm-msm/2026/07/17215679/t | 2026-07 | COMMUNITY | LPAC is a distinct hardware path on a7xx (Linux RFC) | R6 |
| https://docs.vulkan.org/samples/latest/samples/performance/async_compute/README.html | current | OFFICIAL (Khronos) | On TBDR, put fragment→compute→fragment work on another queue; give presenting queue priority; gains modest | §15.2, §15.4 |
| Whether Horizon OS driver maps a second VkQueue to LPAC | — | UNVERIFIED | C01 overlap measurement | R6 |
| Local measurement (`m16scanner-device-evidence/20260828-035425/logcat-full.txt:4386-4425`) | driver 512.837.9, 2026-06-24 build | measured | Family 0 G\|C\|T ×4 (4 priorities), family 1 ×1; `VK_KHR_calibrated_timestamps` absent; timestampPeriod 52.083 ns, 48 bits | §15.2, §15.8 |

## F. Memory

| Source | Date / version | Class | Claim | Supports |
|---|---|---|---|---|
| https://developers.meta.com/horizon/documentation/unity/po-memory-ram/ | 2026 | OFFICIAL | PSS limit Quest 3/3S 5.75 GiB via lmkd; "Low Memory Kill" crashes; PSS logged once/s | §16 |
| https://developers.meta.com/horizon/blog/getting-a-handle-on-meta-quest-memory-usage/ | 2022-04-15 | OFFICIAL | CPU/GPU share pool; textures count toward PSS | §16 |
| https://developers.meta.com/horizon/blog/start-developing-Meta-Quest-3-tips-performance-mixed-reality/ | 2023-10-11 | OFFICIAL | Passthrough MR ≈ +17 % GPU / +14 % CPU vs VR | §3.2, §16 |
| Local measurement (m16 `dumpsys meminfo`) | 2026-08-28 | measured | PSS 1.73–1.87 GB after XR + PCA L/R; Graphics 1.33–1.43 GB; 361 MB without XR | §16, R9, R10 |

## G. Surfel mapping and splat rendering SOTA

| Source | Date / version | Class | Claim | Supports |
|---|---|---|---|---|
| https://www.roboticsproceedings.org/rss11/p01.pdf (ElasticFusion) | RSS 2015 / IJRR 2016 | COMMUNITY (peer-reviewed) | Surfel map, confidence-weighted fusion, deformation graph | §8 |
| https://github.com/puzzlepaint/surfelmeshing | 2018 | COMMUNITY | Online surfel → mesh | §18.5 |
| https://arxiv.org/pdf/1909.04250 (Real-time Scalable Dense Surfel Mapping) | ICRA 2019 | COMMUNITY | Superpixel surfels, adaptive radius from pixel footprint, local/global maps | §8.2, §10 |
| https://www.robots.ox.ac.uk/~mobile/Papers/2018ICRA_scona.pdf (StaticFusion) | ICRA 2018 | COMMUNITY | Free-space violation checks cull points in front of valid surfels | §8.6 |
| https://dl.acm.org/doi/10.1145/3641519.3657441 (Gaussian Surfels) ; https://arxiv.org/abs/2403.17888 (2DGS) | SIGGRAPH 2024 | COMMUNITY | Flat Gaussians / ray-splat intersection, depth-distortion and normal terms | §8, §13.2 |
| https://arxiv.org/abs/2512.01296 (EGG-Fusion) | 2025-12 | COMMUNITY | On-the-fly RGB-D Gaussian-surfel SLAM with information-filter fusion modelling sensor noise, 0.6 cm, 24 FPS | §7.4, §8.5 |
| https://arxiv.org/abs/2402.00525 (StopThePop) | TOG 2024 | COMMUNITY | Popping analysis of sorted splatting | §13.2 |
| https://arxiv.org/abs/2410.18931 (Sort-free GS, Qualcomm) | ICLR 2025 | COMMUNITY | Weighted-sum rendering, ~1.23× faster on mobile GPU, no popping | §13.2 |
| https://arxiv.org/abs/2504.17545 (Gaussian-enhanced Surfels) | 2025-04 / rev. 2025-12 | COMMUNITY | Opaque surfels through normal pipeline with depth test, then order-independent refinement | §13.2 pass 1 / pass 2 |
| https://arxiv.org/abs/2603.11531 (Mobile-GS) | 2026-03 | COMMUNITY | Depth-aware order-independent rendering, 116 FPS at 1600×1063 on Snapdragon 8 Gen 3 | §13.2 |
| https://github.com/zachdrouin/GaussianSplatViewer | 2025 | COMMUNITY | Quest 3 Vulkan/OpenXR splat viewer, radix sort, 400 k splats at 72 FPS | §13 (what to avoid: sort) |
| https://mixed-news.com/en/gracia-quest-3-hands-on/ | 2025 | COMMUNITY (marketing) | Proprietary fast splat renderer on Horizon Store | §13 (context) |
| https://openaccess.thecvf.com/content_CVPR_2020/html/Lee_TextureFusion_High-Quality_Texture_Acquisition_for_Real-Time_RGB-D_Scanning_CVPR_2020_paper.html ; https://dl.acm.org/doi/10.1145/3503926 (TextureMe) | CVPR 2020 / TOG 2022 | COMMUNITY | Texture-tile voxel grid; real-time texture acquisition | §14 atlas option (b) |
| https://zoynctech.itch.io/room-scan-exporter-for-meta-quest-3 ; https://nianticlabs.com/news/into-the-scaniverse-native-app-meta-quest | 2025 | COMMUNITY | Scene-mesh exporters; Scaniverse on Quest is a viewer, not a scanner | §1 (no shipping Quest-native metric scanner found) |
| On-device mobile multi-view atlas baking | — | UNVERIFIED | Open; C17 spike | §14 |

## H. GPU spatial hashing and pipeline binaries

| Source | Date / version | Class | Claim | Supports |
|---|---|---|---|---|
| https://niessnerlab.org/papers/2013/4hashing/niessner2013hashing.pdf | 2013 | COMMUNITY | Sparse voxel block hashing reference | §9.1 |
| https://arxiv.org/pdf/2110.00511 (ASH) ; https://arxiv.org/html/2511.21459 (MrHash) | 2021 / TOG 2025 | COMMUNITY | Flat GPU hash; variance-adaptive multi-resolution flat hash | §9 |
| https://arxiv.org/pdf/2509.16407 (WarpSpeed) ; https://arxiv.org/pdf/2510.15095 (Hive) | 2025-09 / 2025-10 | COMMUNITY | Warp-cooperative bucketed probing at 95 % load; cuckoo lookups degrade at scale; slab hashing suffers pointer chasing | §9.2 (cuckoo excluded for page-local index) |
| https://docs.vulkan.org/features/latest/features/proposals/VK_KHR_pipeline_binary.html | Vulkan 1.3.294, 2024-08 | OFFICIAL | App-owned pipeline binary blobs; key by `pipelineBinaryUUID` | §15.7 |
| https://zeux.io/2019/07/17/serializing-pipeline-cache/ ; https://github.com/Tencent/ncnn/wiki/vulkan-pipeline-cache | 2019 / — | COMMUNITY | Validate cache header (vendor/device/UUID), discard on mismatch; Adreno historically lacked on-disk driver cache | §15.7 fallback |
| Two-level page/cell hashing on Adreno | — | UNVERIFIED (no mobile benchmark) | C08 benchmark A–D | §9.2 |

## Implications adopted into v4

* PCA: 30 Hz is the correctness lower bound, 60 Hz the optimization target, 72 Hz is HAL evidence only (§3.2); capture profiles with hysteresis instead of reconfiguring on motion.
* Camera timestamps are usable across subsystems only through the clock gate A/B/C (§4.2); pose is located on the camera callback and kept in an own ≥ 2 s ring because OpenXR guarantees only 50 ms (§4.3).
* Environment Depth has its own time/pose/fov and is reprojected; single acquire per frame; hand-removal pixels low-confidence (§6).
* All required Vulkan extensions exist on the 2026 driver; no compute-only family exists; second queue overlap is unproven and must be measured (§15.2, R6); `calibrated_timestamps` is absent so GPU↔CPU correlation is manual (§15.8).
* Render integration is one Unity-issued `DrawProceduralIndirect` over native-published buffers; opaque depth-tested surfel rasterization without sorting is the SOTA-endorsed path on mobile (§13).
* Information-filter (covariance-weighted) fusion replaces confidence counters (§7.4, §8.5); free-space violation checks handle thin walls and ghosts (§8.6).
* Memory budget derives from the 5.75 GiB PSS limit minus the measured ~1.8 GB baseline (§16).
* Pipeline binaries keyed on `pipelineBinaryUUID` with a `VkPipelineCache`/SPIR-V warm-up fallback; never forced extensions, never no-fallback (§15.7).
* Page-local addressing uses subgroup-cooperative bucketed probing candidates, cuckoo excluded; structure chosen by C08 benchmark (§9.2).
