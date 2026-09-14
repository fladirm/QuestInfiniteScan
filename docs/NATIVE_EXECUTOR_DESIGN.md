# FinalScan native executor design (C02 input)

Status: design, provisional until C01 numbers land. Implements contract §15. Numbers marked *provisional*.

## 1. Boundaries

```
Unity main thread      C# FinalScan.Host: lifecycle, UI, tools, one indirect draw per frame
Unity render thread    plugin render events: frame mark, publish acknowledgement, resource import
Native scheduler       owns scanner queue (or time-sliced graphics queue), job classes, fence rings
Native workers         pipeline creation (warm-up), disk IO (C14), atlas bake (C17)
```

C# never composes dispatches. C# enqueues *requests* (`FsScan_RequestTick`, `FsResidency_SetCenter`,
`FsTool_Erase(...)`) into a lock-free ring; the native scheduler decides what runs and when.

## 2. Job model

```c
struct FsJob {
    FsJobClass  cls;            // PUBLISH=0, SCAN=1, INNER_RESIDENCY=2, APPEARANCE=3, COLD=4
    uint64_t    deadlineNs;     // XrTime-domain deadline (PUBLISH: next frame; SCAN: +50 ms)
    uint32_t    maxQuantumUs;   // provisional: PUBLISH 500, SCAN 3000, RESIDENCY 2000, APPEARANCE 1500, COLD 1000
    uint64_t    dependsOnTimelineValue;   // timeline semaphore value this job waits for
    FsLeaseSet  leases;         // resource generations this job reads/writes (page slots, ring slots)
    FsCursor    continuation;   // resumable position for bounded stages
    FsJobState  state;          // Created → Prepared → Submitted → Retired | FailedSafe
};
```

Invariants (contract §15.5):
1. All sync objects of a job exist before any Unity resource access.
2. State is published once by the phase owner; helpers only record error codes.
3. A job that imported a Unity resource retires only after the graphics-queue fence proves retirement.
4. Cross-thread fields are `std::atomic` or mutex-protected.
5. Argument/indirect layouts come from one generator (`Tools~/native/gen_abi.py`) shared by C#, GLSL and C++.
6. `VK_ERROR_DEVICE_LOST` → executor enters `Quarantined`: no further submits, all leases released,
   C# receives `FsHost_Status == DEVICE_LOST`, host reinitialises on next `kUnityGfxDeviceEventInitialize`.

## 3. Queues

* Primary: injected second queue in Unity's family (C01 decides). If absent: graphics queue with
  `kUnityVulkanGraphicsQueueAccess_Allow` events, quantum ≤ 1 ms *provisional*.
* One timeline semaphore per class (`fsTimeline[cls]`), monotonically increasing values; PUBLISH waits
  on SCAN's value that produced the BACK page; the renderer never waits on anything.
* Fence ring per class: `inFlightMax[cls]` = PUBLISH 4, SCAN 2, RESIDENCY 2, APPEARANCE 1, COLD 1.
  Submission is refused (job deferred, counter `deferredByClass[cls]++`) when the ring is full. No backlog.

## 4. Scheduling loop (native thread `fs-sched`)

```
every wake (frame mark or job retirement):
  retire completed fences (vkGetFenceStatus, never wait)
  budget = quantumBudgetForNextFrame()          // from measured frame phase (C01 overlap probe)
  for cls in [PUBLISH, SCAN, INNER_RESIDENCY, APPEARANCE, COLD]:
     while ring[cls] has slot and job available and budget >= job.maxQuantumUs:
        record + submit(job); budget -= job.maxQuantumUs
```

SCAN jobs are built from the *latest coherent observation* only (§3.3): the sensor ring exposes
`latest()`; a SCAN job that finds the same observation id already integrated is skipped (`scanTickSkipped++`).

## 5. Resources and leases

* Persistent descriptors: one descriptor set per pipeline bound once; per-job data goes through
  push constants (≤ 128 B) and a per-class ring of uniform blocks (host-visible, 64 B aligned).
* Unity-owned resources (PCA textures, publication buffers read by the Unity draw) are imported once
  per resource generation via `AccessTexture/AccessBuffer(PipelineBarrier)` on the render thread and
  cached as `FsImportedResource {handle, generation, layout}`; re-import only when the Unity handle changes.
* Page slots have `generation` (u32). A lease records `{slot, generation}`; a job whose lease generation
  no longer matches at submit time is dropped, never patched.

## 6. Publication (§11)

* Per page: FRONT record range + BACK record range in one arena; `FsPageHeader {frontOffset, backOffset,
  generation, count}`.
* SCAN writes BACK; PUBLISH job (sub-ms) copies changed records into FRONT range **then** bumps
  `generation` and the root pointer (root-last). The Unity draw reads the draw list buffer whose indirect
  args were produced by the native CULL pass of the last published generation.
* The renderer reads only FRONT; no fences on the C# side, only the generation counter for telemetry.

## 7. Timestamps (§15.8)

* Query pool per class ring slot: 2 queries per stage. Read after fence, never with WAIT.
* Frame correlation: graphics-queue timestamp per frame (`FS_EVT_FRAME_MARK`) + CPU monotonic at
  submit; offset estimated by least squares (`ClockDomains` fit), re-fit every 5 s.
* Every stage reports `{cls, stage, gpuStartNs, gpuEndNs, frameIndex}` into a telemetry ring read by C#.

## 8. Kernel envelope gate (§15.6)

`Tools~/native/build_native.sh` compiles GLSL → SPIR-V (glslang) and fails the build when any pipeline
exceeds 128 KiB SPIR-V, > 8 storage bindings, > 16 KiB shared, > 256 threads, or contains a dynamic
vector component index (spirv-val + a small SPIR-V scanner in `Tools~/native/spv_gate.py`).

## 9. Pipeline delivery (§15.7)

Warm-up phase before scan is enabled: load `VkPipelineCache` blob keyed by `pipelineCacheUUID`; if
`VK_KHR_pipeline_binary` is enabled, try binaries keyed by global key + driver version; otherwise compile
from SPIR-V on `fs-warmup` thread. Scan is refused (`FsHost_Status == WARMING_UP`) until all pipelines exist.

## 10. C ABI surface for C02 (additive to ABI 1)

```
FsHost_Init(config) / FsHost_Shutdown() / FsHost_Status()
FsHost_GetTelemetryJson(buf, cap)
FsSched_SetFrameBudgetUs(us)         // from C# measured frame phase, provisional 2000
FsScan_RequestTick(observationId)
FsWorld_CreateSynthetic(params)      // C04 synthetic world for C05–C08
FsRender_GetDrawArgsBuffer(out nativePtr, out size)   // Unity wraps as GraphicsBuffer for DrawProceduralIndirect
FsRender_GetFrontGeneration()
```
