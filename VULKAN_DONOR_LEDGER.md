# VULKAN_DONOR_LEDGER

C00 deliverable. Contract: `FINALSCAN_V4_CONTRACT.md` §0, §15, §23. Every row cites a file:line or commit in a donor checkout. Nothing here is FinalScan code; it is the evidence base for the native executor design.

## Donor map

All three donors are branches of one repository, `fladirm/QuestInfiniteScan`:

| Donor | Checkout | Branch | HEAD | Native source |
|---|---|---|---|---|
| SimpleScan | `/mnt/aidisk/prace/simplescan` | `fix/merkaba-runtime-root-causes` | `651cea6` (2026-09-06) | `Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp` (2 095 lines, byte-identical to merge-base `c34d27f`) |
| UniScan | `/mnt/aidisk/prace/uniscan` | `refactor/m8-dual-sphere-flower-rev-b` | `9cfed011` (2026-09-14) | same file (3 064 lines) + `MerkabaPipelineBinary.h` (452), `MerkabaPipelineBinaryAbi.h` (112), `MerkabaFlowerDraw.h` (241), `MerkabaSphereFlowerDataAbi.h` (135) |
| OtherScan | `/mnt/aidisk/prace/otherscan` | `forensic/n4r-cut-e-scheduler` | `c6498c0` (2026-09-02) | `Runtime/SigmaPrism/Native/SigmaVulkanTimestamps.cpp` (4 154 lines) |

Shared facts:

* Build: NDK clang from the Unity install (`aarch64-linux-android29-clang++ -O2 -fPIC -shared -Wl,-z,max-page-size=16384`, `Tools/unity/build_merkaba_vulkan_timestamps.sh`), SPIR-V embedded via `generate_merkaba_native_executor_shaders.py`. The `.so` is written to the host project on `/mnt/kingston-unity/Unity/Projects/*Host/Assets/Plugins/Android/`, never committed.
* Plugin lineage: `fbfc07a` (2026-08-28, timestamps only, 322 lines) → `c1d2cfd` (2026-08-31, first native compute executor, +1 711 lines) → `dcb83ce` (09-07, native draw intercept) → `a2df123` (09-11, pipeline binaries) → `9cfed011` (09-14, ABI + fence lifetime).
* OtherScan's `SigmaVulkanTimestamps.cpp` is the same executor architecture forked at `03d0c7a` (08-31) and never received the post-09-05 fixes.

Abbreviations below: `U:` = uniscan cpp line at `9cfed011`; `S:` = simplescan cpp line at `651cea6`; `O:` = otherscan Sigma cpp line at `c6498c0`; `PB.h` = `MerkabaPipelineBinary.h`; `FD.h` = `MerkabaFlowerDraw.h`; `EX.cs` = `Runtime/Telemetry/MerkabaNativeVulkanExecutor.cs`.

## Mechanism ledger

| Mechanism | SimpleScan | UniScan | OtherScan | Bug it solved | Invariant | FinalScan reuses | FinalScan rejects |
|---|---|---|---|---|---|---|---|
| Unity Vulkan V2 intercept | `S:325-475` `AddInterceptInitialization(…, priority 10000)`, hooks `vkGetInstanceProcAddr` → `vkCreateDevice` only | `U:2639` register; `U:934-952` `InterceptGetInstanceProcAddr` hooks `vkCreateDevice`, `vkGetDeviceProcAddr`, plus PB.h `vkDestroyDevice`, `vkCreate{Compute,Graphics}Pipelines` | same pattern (`03d0c7a`) | V1 `InterceptInitialization` broke when a second interceptor (OpenXR plugin) existed (Unity WebRTC #686) | Register through V2 with a priority that lets the Meta OpenXR interceptor run; hook only what is needed | V2 registration, `vkCreateDevice` hook | Hooking Unity's pipeline creation calls (PB.h:423-425) |
| Second queue injection | `S:` finds the single "safe" family (Unity `queueCount==1`, physical ≥2, GRAPHICS\|COMPUTE, not protected) and appends queue index 1, priority 0.1 | `U:508-901` same, also enables `multiDrawIndirect`, `drawIndirectFirstInstance`, `drawIndirectCount` | `O:505-547` injects at `info.queueCount` of the first eligible family | Unity requests one queue; scan needs its own submission stream | Injection is optional; measured overlap decides whether it is used (contract §15.2) | Injection pattern, same-family (no ownership transfer) | Assuming a compute-only family exists (device has none) |
| `vkCreateDevice` fallback | none — retries only Unity's original list if no safe family found (`S:895-905`) | none: PB.h forces extensions, failure aborts device creation (`U:883-884`) | `O:549-561`: if injected create fails → log, clear injected indices, **call `nextCreateDevice` with the original `createInfo`** | Injected create can fail on another driver/OS | Device creation must never fail because of an optional scanner feature | OtherScan fallback verbatim in spirit | UniScan hard failure |
| Queue selection / aliasing check | `S:1440-1512` `vkGetDeviceQueue(family,1)`; refuses if it aliases Unity's queue 0 | `U:2331` same; Unity's `graphicsQueue` used only inside `kUnityVulkanGraphicsQueueAccess_Allow` events (`U:2336-2343`) | same | Submitting on Unity's queue outside an access-configured event corrupts Unity's frame | Never touch Unity's queue outside `AccessQueue`/`GraphicsQueueAccess_Allow` events | Aliasing check; access-configured events | — |
| Command pool | one pool, `RESET_COMMAND_BUFFER_BIT`, one primary `ONE_TIME_SUBMIT` buffer per job (`U:2346-2350`, `U:1740-1746`) | same | `O:283` `std::array<VkCommandBuffer, kExecutorSliceCount>` per job | — | Pools per job class / per thread; bounded buffers | Reset-per-buffer pool | One pool shared by all classes |
| Per-job sync objects | 2 binary semaphores + 3 fences per job (`U:1751-1774`): `graphicsFence`, `nativeFence`, `acquireFence` | same | same | — | Sync objects exist **before** any Unity resource access (see FailJob row) | Fence ring per job class instead of 3 fences/job; timeline semaphores (present on device) | Per-job allocation of 5 sync objects |
| graphicsReady → nativeDone chain | empty submit on Unity graphics queue signals `graphicsReady`+`graphicsFence` (`U:2110-2118`); scanner submit waits at COMPUTE\|DRAW_INDIRECT, signals `nativeDone`+`nativeFence` (`U:2126-2144`) | same | same | Scan must observe Unity's writes to imported resources | Cross-queue ordering via semaphores, never CPU waits | Ordering contract (with timeline semaphores) | Binary semaphores + fences triplet |
| Acquire submit | empty graphics-queue submit waiting `nativeDone`, `acquireFence` (`U:2177-2186`) | same | same | Unity must not read scanner output before it completes | Graphics retirement is proven by a fence, not assumed | Concept (publish generation after retirement) | Third submit per job; use timeline value instead |
| Poll threading | `MerkabaExecutor_PollJob` on main thread **without** `g_executorMutex` (`U:2827-2924`); `DestroyJob` takes the mutex (`U:2955-2970`) | same | same | — | Cross-thread fields are atomics or under the mutex; state machine is not lock-free by accident | Atomic `error`/`graphicsSubmitted` (`U:246-247`, `9cfed011`) | Lock-free poll relying on C# discipline (`EX.cs:644-649`, `724-728`) |
| FailJob / prepare ownership | n/a (pre-fix) | `9cfed011`: `FailJob` (`U:1234-1247`) refuses to change state while `state == kJobPreparing` (`U:1240`); prepare order `CreateJobCommandObjects → AccessJobResources → Uniforms → Descriptors → Record` (`U:2075-2081`); `SubmitExecutorJob` guard: `graphicsFence == VK_NULL_HANDLE` → `FailedSafe`, no submit (`U:2100-2108`) | n/a | (a) `vkGetFenceStatus(device, NULL)` SIGSEGV inside Adreno when ABI validation failed before fences existed (`lasttrue.md:10787-10789`); (b) helper published `FailedSafe` while render thread was still in prepare → use-after-free window | (1) all pollable sync objects exist before any `AccessBuffer/AccessTexture`; (2) job state is published once by the owning phase; terminal states are never transient; (3) a job that touched Unity resources cannot be `FailedSafe` until graphics fence retirement | All three invariants | Code shape (per-job objects, three fences) |
| Argument-buffer ABI | literal `buffer.sizeInBytes < 64u` and `vkCmdDispatchIndirect` offsets `16u/32u` (`S:1365-1370`, `S:1852`) vs C# 48 B | `9cfed011`: generator `observation_argument_layout()` reads C# constants, cross-checks HLSL `#define`s, emits `kMerkabaObservationArgumentBytes` etc. (`generate_merkaba_native_executor_shaders.py:623-667`); consumed `U:1380`, `U:1855-1856`. **Still literal:** `kFlowerBatchArgsOffset` `U:139`, `kFlowerPageWorkArgsOffset=2359360` `U:143`, `kFlowerColdArgsOffset=3543248` `U:144` | same anti-pattern | `VK_ERROR_INITIALIZATION_FAILED (-3)` at first depth snapshot (`lasttrue.md:10782-10786`) | One generator-derived source of truth for every indirect/argument layout shared by C#, HLSL, native; zero numeric offsets in the plugin | Generator cross-check principle | Any literal offset |
| Uniform table by name hash | C# `MerkabaNativeUniformTable` sends FNV-1a hash + bytes (`EX.cs:734-913`); native matches by hash and memcpys into a host-visible uniform buffer (`U:931-938`, `U:1471-1596`); no push constants (`U:1073-1083`) | same | same | Avoids a hand-written second ABI | Per-dispatch parameters go through push constants with a generated layout; descriptors are persistent | SPIR-V reflection into a generated `.inc` (`generate_…shaders.py:319-401`) | Runtime name hashing, per-job uniform buffers |
| Resource import | `AccessBuffer(ptr, stages, access, kUnityVulkanResourceAccess_PipelineBarrier)` whole-range, capped 128 MiB (`U:1348-1373`, `U:1714-1728`); `AccessTexture(…, WholeImage, GENERAL/SHADER_READ_ONLY, PipelineBarrier)` (`U:1435-1467`) | same | same | Unity owns layouts; the plugin must go through the barrier API | Import once per resource generation, on the render thread, never inside a queue-access event (IUnityGraphicsVulkan.h contract) | The access API | Per-job re-import |
| Per-job views / descriptor pools | per-job `VkImageView`, descriptor pool/sets, uniform buffer, command buffer, query pool (`U:1435-1467`, `1740-1796`) | same | same | Simplicity | Persistent descriptors and views; jobs only bind | — | Per-job allocation of everything |
| Timestamp query pools | `fbfc07a`: 6 owners × (2+4096×2) queries recorded into Unity's command buffer via `CommandRecordingState` (`U:258-261`, `2568-2575`); `f2cfe26` isolates per-owner ranges after owners overwrote each other | per-job pool `dispatchCount*2+2` (`U:1783-1796`), reset on GPU (`U:1910`), read with `vkGetQueryPoolResults(64_BIT)` without WAIT after `nativeFence` (`U:2199-2203`); `0770fd2` telemetry made observational | same | Owners overwriting shared ranges; benchmark work injected into production timing | One owner per range; telemetry never adds work to the measured path; read only after fence | Per-owner ranges, no-wait reads | Sampling once per 5 s as the only truth |
| Pipeline creation | `vkCreateComputePipelines` with `VK_NULL_HANDLE` cache (`S:` 49 pipelines) | on worker thread `g_executorInitWorker` (`U:327`, `2295-2315`), no `VkPipelineCache`, no push constants; one DSL per pipeline (`U:993-1174`) | same | Startup stall | Pipelines are built before scan is allowed (warm-up), never during scanning | Worker-thread creation | `VK_NULL_HANDLE` cache |
| `VK_KHR_pipeline_binary` delivery | none | `a2df123`: `InitializePipelineBinaries` matches `vkGetPipelineKeyKHR` global key to a bundled pack (PB.h:140-175); CAPTURE writes `.m8pb` (PB.h:229-282); BINARY_ONLY loads `vkCreatePipelineBinariesKHR` (PB.h:204-226, 335-340); missing PSO → "no SPIR-V fallback" hard failure (PB.h:384-390); Java `MerkabaPipelineBootstrap` ContentProvider loads the lib before UnityPlayer (`Runtime/Plugins/Android/MerkabaPipelineBootstrap.java:16-25`) | none | Cold link 226 s (`lasttrue.md:8709`) | Binaries keyed by `pipelineBinaryUUID` + driver; `VkPipelineCache` + warm-up compile as fallback | Capture/bundle workflow, key matching, bootstrap provider | "no fallback", intercepting Unity's own PSOs |
| Forced extensions | none | `PreparePipelineBinaryDevice` force-enables `pipeline_binary`, `maintenance5`, `dynamic_rendering`, `depth_stencil_resolve`, `create_renderpass2`; absence returns false and device creation fails (PB.h:76-138, `U:883-884`); timeline/sync2 only logged (`U:620-645`) | none | — | Optional features are requested optionally; device creation never depends on them | — | Entire forced path |
| Native draw intercept | none | `dcb83ce` `FD.h`: `InterceptVulkanAPI("vkCmdDrawIndexedIndirect")` rewrites Unity's draw into `vkCmdDrawIndexedIndirectCount` inside the URP pass (`FD.h:102-155`); `47a223d`: Unity 6000.5 reports `subPassIndex=0` with null render pass outside a pass (`FD.h:79-83`) and `stride=0` (`FD.h:112-129`); `2aada51`/`7ec07b0` cull-reset chain with a retracted measurement | none | Indirect count draw without Unity API support | Native never records into Unity's render pass; render integration is Unity-issued indirect draw of native-published buffers (contract §13.1) | Nothing | Whole mechanism |
| Frame-paced sliced jobs | none | none | `kExecutorSliceCount = 7` (`O:109`), `RecordJobSlice` (`O:2392`), `SubmitNativeSlice` (`O:2561`), `SigmaExecutor_SubmitNextSlice` (`O:3526-3542`); one command buffer per slice (`O:283`, `O:2307`) | Long cold jobs blocking the queue for hundreds of ms | Every stage is bounded, resumable, continuation-based | Slice pattern for P4 COLD / LOOP / COMPACTION | — |
| Cold upload / encode jobs | none | none | `SigmaColdUploadPage/Descriptor` (`O:176-198`), `SigmaColdEncodeDescriptor/Result` (`O:203-212`), 12-resource encoder pipelines (`O:104`, `729`); readback `SigmaExecutor_ReadCompletion` (`O:3677`), `ReadPredictionPageRequests` (`O:3688`) | none | Paged residency uploads as their own job family | Residency has its own job class with its own fences | Job-family separation, GPU-generated page requests | Sigma data formats |
| Shutdown | `vkDeviceWaitIdle` only in `ShutdownExecutor` (`U:2232`), after joining the init worker (`U:2226`) | same | same | — | `vkDeviceWaitIdle` allowed only on `kUnityGfxDeviceEventBeforeReset/Shutdown`; never `vkQueueWaitIdle` at runtime | Shutdown-only idle | — |
| Device-lost handling | absent: `VK_ERROR_DEVICE_LOST` is a generic failure (`U:2848-2853`) | absent | one teardown "GPU completion is unprovable; resources were quarantined" then process death (`otherscan/STATE.md:729`) | — | Explicit device-lost path: quarantine → rebuild (contract §15.5.6) | Quarantine wording | Generic failure |
| Sleep / wake | C# only: `RoomScanner.OnApplicationPause` (`aaeb825`), Env-Depth restart after resume (`b34910b`), anchors on wake (`62b6906`, `cf39413`); native jobs never cancelled on pause | same | same | Env Depth subsystem dead after sleep | Pause completes or quarantines native jobs; Env Depth restarted; single acquire per frame | Restart pattern | Leaving jobs in flight across pause |
| GraphicsFence type | `MerkabaGrid.Gpu.cs:637` still creates `AsyncQueueSynchronisation` | `57ab534`: `.passed` on `AsyncQueueSynchronisation` threw `NotSupportedException` every frame on Quest (2 622 → 0); switched to `CPUSynchronisation` (`MerkabaIntegrator.cs:549, 776, 1359`; `MerkabaGridRenderer.cs:407`) | m16 evidence: 1 567 throws / 12 s in `M16Renderer.LateUpdate` | Unity reports `supportsAsyncCompute=False` on Quest | C# side uses `CPUSynchronisation` or async readback tokens only | `CPUSynchronisation` | `AsyncQueueSynchronisation` |
| Async readback completion tokens | `ba286ac` replaced publication/migration `GraphicsFence` with `AsyncGPUReadback` tokens; `4f5c837` retires attempts from an exact token instead of the 50 ms storage poll; `8052ea7` transactional readout publish | same | same | Fences unreliable on Quest; poll-driven completion starved pairing | Completion is proven by a GPU-written token, never by elapsed time | Token pattern for C#-visible completion | 50 ms polling |
| Producer-owned texture copies | `06e2117`/`969a865`/`30693db`/`748a62d` own transient PCA/Env-Depth textures; blit in the same render callback that latches metadata (errors.md RGBD-6) | same | same | `Graphics.Blit` outside the callback saw the previous image | Sensor image and its metadata are latched in one callback, into scanner-owned memory | Pattern (§5 `CameraFrameLease`) | Reading producer textures later |
| SPI instance doubling | `f4970df`: indirect draw instance count ×2 for single-pass instanced stereo | same | n/a | Right eye missing | Indirect args generated for SPI | ×2 rule in GPU-generated args | — |
| Adreno SPIR-V constraints | `47bd4a3`: no dynamic vector component writes (`v[axis] = x`); ≤ 8 writable storage bindings per kernel (Unity limit) | `897c176`: preserve control flow for Adreno linking; `d635c22`: DrainFlowerSkin 1 638 848 B → `-13` after 83.6 s; `8e2b760`: IntegrateFlowerSkin 746 128 B "Failed to link shaders" → split RGB/V; link refusal bracket 825 256–944 896 B (`lasttrue.md:8285-8287`); cold link 226 164 ms, Compact 152 500 ms (`lasttrue.md:8709`); Drain killed after >270 s at 3.3 GB RSS (`lasttrue.md:8282-8306`) | `STATE.md:910`: dispatch 102401×1×1 exceeded 65535 | Monolithic shaders | Kernel envelope gate (contract §15.6): ≤ 128 KiB SPIR-V, ≤ 8 writable bindings, ≤ 16 KiB groupshared, ≤ 256 threads/WG, no dynamic vector indexing | Constraint list | Any monolithic kernel |
| Hot-path validator | n/a | `validate_merkaba_hot_path.py:52` exempted `RebuildDirtyFlowerOwners`/`RebuildColdFlowerOwners` from the HOT gate (working tree re-adds tripwires) | n/a | A conditional heavy path hidden from the performance validator | Every dispatch that can run in a scan quantum is subject to the gate; no exemptions | Gate concept | Exemption lists |

## Measured device envelope used by this ledger

Source: `m16scanner-device-evidence/20260828-035425/logcat-full.txt:4386-4425`, `simplescan-evidence/d1-20260830/logcat.txt`, `otherscan/evidence/N5R/*.log`.

* Device `panther`, serial `340YC20G7X0QZ4`, Adreno 740, vendor `0x5143`, device `0x43050B00`, driver `512.837.9` (`AdrenoVK-0: 0837.0.9, build 06/24/26`), Vulkan API `1.3.295`, Unity `6000.5.9f1`, OpenXR runtime `Oculus 207.218.0`.
* Queue families: `0 = graphics | compute | transfer, 4 queues, 4 priorities`; `1 = 1 queue` (no capability flags logged). Unity work queue family 0, one queue. OtherScan probe: `separateComputeFamily=0 dedicatedComputeFamily=0`. Injection receipt: `vkCreateDevice intercept: requestedFamilies=1 injected=1 family=0 queueIndex=1 result=0` (`otherscan/evidence/N5R/b1072bb_live_watch.log:263`). Unity: `supportsAsyncCompute=False`.
* Extensions present: `VK_KHR_synchronization2`, `VK_KHR_timeline_semaphore`, `VK_KHR_pipeline_binary`, `VK_KHR_maintenance5`, `VK_KHR_push_descriptor`, `VK_EXT_global_priority`, `VK_EXT_host_query_reset`, `VK_KHR_buffer_device_address`, `VK_KHR_imageless_framebuffer`, `VK_EXT_fragment_density_map/2`, `VK_EXT_robustness2`, `VK_EXT_device_memory_report`, `VK_ANDROID_external_memory_android_hardware_buffer`, `VK_KHR_sampler_ycbcr_conversion`, `VK_EXT_queue_family_foreign`, `VK_KHR_shader_float16_int8`, `VK_KHR_16bit_storage` (uniform 16-bit access false), `VK_NV_optical_flow`.
* Extensions missing: `VK_KHR_calibrated_timestamps`, `VK_QCOM_filter_cubic_weights`.
* Timestamps: `timestampPeriod = 52.083332 ns`, `validBits = 48`, compute and graphics.
* GPU clock during scan runs: pinned 456 MHz (492 MHz idle).
* Memory: `dumpsys meminfo` PSS 1 734–1 867 MB immediately after XR start with passthrough + PCA L/R (Graphics 1 327–1 430 MB); 361 MB without XR; OtherScan hit 2 085 MiB GPU = 100 % allocated; official PSS limit 5.75 GiB.

## Negative lessons (contract §23, each with its citation)

Native layer:

1. Hard-coded argument-buffer ABI — `S:1365-1370`, `S:1852` vs `MerkabaObservationBinsGpu.cs:14-16`; residual literals `U:139`, `U:143-144`.
2. `vkGetFenceStatus(NULL)` SIGSEGV — `lasttrue.md:10787-10789`; guard `U:2100-2108`.
3. `FailJob` changing state during prepare — `U:1238-1243`; `lasttrue.md:10790-10794`.
4. Lock-free `PollJob` vs mutexed `DestroyJob` — `U:2827`, `U:2961`.
5. Forced extensions fail device creation — PB.h:76-138, `U:883-884`.
6. BINARY_ONLY without SPIR-V fallback — PB.h:384-390.
7. Monolithic SPIR-V: link `-13`, 226 s cold link, 3.3 GB RSS — `d635c22`, `8e2b760`, `lasttrue.md:8282-8306, 8709`.
8. Per-job views/pools/uniforms, no push constants — `U:1073-1083`, `U:1435-1467`, `U:1740-1796`.
9. Draw intercept dependent on Unity 6000.5 render-pass semantics — `FD.h:79-83, 112-129`, `47a223d`.
10. Queue injection without fallback — UniScan `U:883-884` vs OtherScan `O:549-561`.
11. One global job slot — `EX.cs:153, 268, 369`; readout job ~42 ms resubmitted every frame occupies the queue (`MerkabaGrid.Storage.cs:265-272`); observation submit median 161 ms = 6.2 Hz (`errors.md:14-19`); readout queueFence 53.9 ms / lifetime 99.4 ms (`.claude/MERKABA_GEOMETRY_REVIEW.md:104-119`).
12. Long readout job blocking world work — `MerkabaGrid.Storage.cs:281` (`PumpStorage` gated on `HasJobInFlight`), `MerkabaGridRenderer.cs:276-291`.
13. Resubmission job permanently occupying the queue — `da37199`, `MerkabaGrid.Storage.cs:265-272`.
14. Heavy path hidden from the validator — `validate_merkaba_hot_path.py:52`; `M8-REALTIME-DAG.md:112`.

Unity / host:

15. `GraphicsFence AsyncQueueSynchronisation` throws every frame — `57ab534`; m16 `0415-bootstrapfix/logcat-12s.txt` (1 567×/12 s).
16. Producer-owned textures read outside the latching callback — `errors.md` RGBD-6, `06e2117`.
17. Latest-only PCA pairing, 46 % acceptance — `errors.md` RGBD-1, baseline run 2026-08-30 (775 accepted / 899 expired).
18. Double `xrAcquireEnvironmentDepthImageMETA` per frame → `XR_ERROR_LIMIT_REACHED` — `simplescan-evidence/d1-20260830/logcat.txt` line ~49894.
19. Env Depth subsystem dead after resume — `b34910b`.
20. Exception outside try/catch holds the native lease forever — `55f6877`.
21. Unity 8-UAV limit, Adreno dynamic vector indexing, SPI instance count — `47bd4a3`, `f4970df`, `m16 .goal/RISKS.md:9`.

Process:

22. Performance claims without device receipts — `7ec07b0` retraction; `CLAUDE.md:44-49` (uniscan).
23. Telemetry injecting work into the measured path — `0770fd2`.
24. Shared timestamp ranges across owners — `f2cfe26`.
25. Source-text tests (13 of 17 files use `File.ReadAllText`) — `.claude/MERKABA_RISKS.md` RISK-4; two agents in one checkout — RISK-5; stale docs and `ChunkSize=32` frozen in the persistence header — RISK-3/6.
26. HUD "Active" with zero integration invocations for a 9-minute session — `simplescan-evidence/d1-20260830/logcat.txt` (`Merkaba gpu-stage … invocations=0`).

## Invariants adopted by the FinalScan native executor (contract §15.5)

1. Every sync object a job can be polled on exists before any `AccessBuffer`/`AccessTexture` call; those calls may record barriers into Unity's stream even when a later validation fails.
2. Job state is published exactly once by the phase that owns it; helpers only record errors; terminal states (`Complete`, `FailedSafe`) are never transient.
3. A job that touched Unity resources cannot become `FailedSafe` until a graphics-queue fence (or timeline value) proves Unity's references retired.
4. Fields written on one thread and read on another are atomics or mutex-protected; no poll path depends on managed-side discipline.
5. Every indirect/argument layout has one generator-derived source of truth shared by C#, HLSL and native; the plugin contains no numeric offset literals.
6. `VK_ERROR_DEVICE_LOST` has an explicit quarantine-and-rebuild path.
7. Device creation never depends on an optional scanner feature; queue injection falls back to Unity's original create info.
8. Unity's graphics queue is touched only inside access-configured plugin events; the scanner queue is a distinct `VkQueue` (or the same queue with bounded quanta if injection is unavailable).
9. `vkDeviceWaitIdle` only on Unity device reset/shutdown events; `vkQueueWaitIdle` never.
10. Descriptors, image views and pipelines are persistent; per-dispatch parameters travel through push constants with a generated layout.
11. One shader = one bounded parallel transformation under the kernel envelope gate; no exemption list.
12. Telemetry is observational: per-owner query ranges, reads after fence, no work injected into the measured path; no performance claim without device timestamps on the same time axis as the Unity frame.
