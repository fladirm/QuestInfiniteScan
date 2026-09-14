// FinalScan native host C ABI (ABI 2 = C02..C08). Additive to finalscan_native_api.h (ABI 1 probes).
// C# mirror: Runtime/Platform/Native/FinalScanHostNative.cs. Main-thread safe unless noted.
#pragma once
#include <stdint.h>
#include "finalscan_world_abi.h"
#ifdef __cplusplus
extern "C" {
#endif
#define FS_HOST_ABI_VERSION 3

enum FsHostStatus { FS_HOST_UNINITIALIZED = 0, FS_HOST_WARMING_UP = 1, FS_HOST_READY = 2,
                    FS_HOST_DEVICE_LOST = 3, FS_HOST_QUARANTINED = 4, FS_HOST_SHUTDOWN = 5 };

enum FsJobClass { FS_JOB_PUBLISH = 0, FS_JOB_SCAN = 1, FS_JOB_INNER_RESIDENCY = 2, FS_JOB_APPEARANCE = 3, FS_JOB_COLD = 4, FS_JOB_CLASS_COUNT = 5 };

typedef struct FsHostConfig {
    uint32_t structSize;
    uint32_t residentPages;          // page table entries (resident logical pages), provisional 256
    uint32_t canonicalSurfels;       // canonical surfel pool (slabs of FS_SLAB_SURFELS), provisional 4 M = 128 MiB + 32 MiB evidence
    uint32_t pageHashCapacity;       // power of two, provisional 4096
    uint32_t measurementRingCapacity;// unused (the epoch takes FS_TICK_MEAS_MAX records); kept for layout
    uint32_t drawRecordCapacity;     // provisional 1<<20
    uint32_t frameBudgetUs;          // scan GPU budget per frame, from C01 (provisional 2500)
    uint32_t inFlightMax[FS_JOB_CLASS_COUNT];
    uint32_t quantumUs[FS_JOB_CLASS_COUNT];
    uint32_t useScannerQueue;        // 1 = use injected queue when present
    uint32_t enableSyntheticWorld;   // acceptance fixture only
    uint32_t indexLeaves;            // adaptive index leaf pool (64 B each), provisional 1 M
    uint32_t indexNodes;             // adaptive index node pool (32 B each), provisional 256 k
    uint32_t renderBlocks;           // immutable render leaf blocks (64 surfels), provisional 64 k
    uint32_t renderNodes;            // persistent render tree nodes (120 B), provisional 512 k
    uint32_t reserved[2];
} FsHostConfig;

// Lifecycle. Init may be called before Vulkan is ready; the executor completes warm-up on the render
// thread and reports FS_HOST_READY. Shutdown is the only place allowed to wait for the device.
int32_t FsHost_GetAbiVersion(void);
int32_t FsHost_Init(const FsHostConfig* cfg);
int32_t FsHost_Shutdown(void);
int32_t FsHost_GetStatus(void);
int32_t FsHost_GetTelemetryJson(char* buf, int32_t cap);   // counters, per-class timings, ring occupancy, last errors
int64_t FsHost_GetCounter(int32_t counter);
void*   FsHost_GetRenderEventFunc(void);                    // events below
// (ABI 2 additive) Storage root for the pipeline cache and pipeline binaries: C# passes
// Application.persistentDataPath; the plugin uses "<path>/finalscan/". May be called before FsHost_Init.
int32_t FsHost_SetStorageRoot(const char* path);

enum FsHostRenderEvent {
    FS_HEVT_FRAME_BEGIN = 100,   // per frame, before Unity draws: retire fences, publish acknowledgements, cull for this frame
    FS_HEVT_FRAME_END   = 101,   // per frame after the draw: frame timestamp mark, scheduler wake
    FS_HEVT_WARMUP_STEP = 102    // pipeline creation steps while status == WARMING_UP
};

// Scheduler (contract §15.4)
int32_t FsSched_SetFrameBudgetUs(uint32_t us);
int32_t FsSched_SetFrameHint(double predictedDisplayTimeSec, float headPos[3], float headVel[3], float headRot[4]);
int32_t FsSched_GetClassStats(int32_t cls, int64_t out[8]);  // submitted, retired, deferred, failed, gpuUsTotal, gpuUsLast, inFlight, quantumUs

// Sensor → measurements (C03/C09..C11 feed the ring; C04 synthetic path feeds it too)
int32_t FsMeas_Push(const FsSurfaceMeasurement* items, int32_t count, int32_t anchorId);   // lock-free ring, drops oldest on overflow (counted)
int32_t FsScan_RequestTick(uint32_t observationId);          // latest coherent observation only (§3.3)

// World (contract §8..§11)
int32_t FsWorld_CreateSynthetic(int32_t kind, float extentM, float surfelSpacingM, uint32_t seed); // kind: 0 room box, 1 corridor, 2 stairs, 3 thin wall
int32_t FsWorld_GetPageCount(int32_t* resident, int32_t* logical);
int32_t FsWorld_GetSurfelCount(int64_t* frontTotal, int64_t* backTotal);
uint32_t FsWorld_GetFrontGeneration(void);                  // sum/xor of published page generations for telemetry
int32_t FsWorld_SetAnchorTransform(int32_t anchorId, const float worldFromAnchor[16]);   // column-major
int32_t FsWorld_Erase(const float center[3], float radius, int32_t anchorId);            // marks removed (C23 finalizes)
// (ABI 3, C09R) Dedicated world reset transaction: stops observations, publishes empty roots through the graphics
// path, frees every world allocation after retirement, resets the page map / ids. Executor, pipelines, sensors
// and renderer stay alive (no device teardown). Returns FS_OK when the transaction was queued.
int32_t FsWorld_Reset(void);
// (ABI 3) World telemetry JSON (fusion / index / publication / memory counters, contract §20).
int32_t FsWorld_GetTelemetryJson(char* buf, int32_t cap);
// (ABI 3, C09R-D) Render telemetry JSON: per-dispatch device stage times (hzb.scatter/combine/mips,
// cull.pages/expand/blocks/compact) and the last cut statistics. Returns the required capacity (incl. NUL).
int32_t FsRender_GetTelemetryJson(char* buf, int32_t cap);

// Residency (contract §12): position-driven only. Orientation must never reach these calls.
int32_t FsResidency_SetCenter(const float predictedPos[3], float innerRadius, float warmRadius, float prefetchRadius);
int32_t FsResidency_GetZoneStats(int64_t out[8]);          // innerPages, warmPages, prefetchPages, requestsThisFrame, orientationRequests(must be 0), evictions, loads, stalls

// Render (contract §13.1): Unity issues ONE DrawProceduralIndirect with these buffers.
// Buffers are native-owned VkBuffers exposed as Unity GraphicsBuffer via native pointer import on the
// C# side (GraphicsBuffer created by Unity, imported here with AccessBuffer once; see C05 notes).
int32_t FsRender_RegisterBuffers(void* unityDrawRecordBuffer, uint32_t drawRecordBytes, void* unityIndirectArgsBuffer);
int32_t FsRender_SetView(const float viewL[16], const float projL[16], const float viewR[16], const float projR[16], const float headPos[3]);
int32_t FsRender_GetLastCullStats(int64_t out[4]);          // culledPages, visibleSurfels, drawRecords, cullGpuUs (device timestamps of the last cull + HZB frame stages)
// (ABI 2 additive, contract §13.5) Depth priors for occlusion culling. Unity textures imported once per handle.
// prevDepth: the app's own depth buffer of the previous frame (per eye array or two textures), with the view/proj it was rendered with.
// envDepth : Environment Depth texture array (2 layers) with its own per-eye pose/fov and XrTime; may be older than the frame.
int32_t FsRender_SetPrevDepth(void* unityDepthTexture, uint32_t width, uint32_t height, uint32_t layers,
                              const float viewL[16], const float projL[16], const float viewR[16], const float projR[16]);
int32_t FsRender_SetEnvDepth(void* unityDepthTextureArray, uint32_t width, uint32_t height,
                             const float poseL[16], const float poseR[16], const float fovL[4], const float fovR[4],
                             float nearZ, float farZ, int64_t xrTimeNs);
int32_t FsRender_SetLodPolicy(float fovealErrorPx, float peripheralErrorPx, float predictionMarginDeg, uint32_t screenWorkBudget);
int32_t FsRender_SetGpuHeadroomUs(int32_t headroomUs);       // from frame timestamps; negative = over budget → raise error threshold
// (ABI 2 additive, contract §13.5 render modes) 0 SCAN (band occlusion + LOD), 1 XRAY/RADAR (no depth-prior cull),
// 2 PLAN (no backface cone). Changes only the cull policy, never residency or canonical data.
int32_t FsRender_SetMode(int32_t mode);
// (ABI 2 additive) Draw list layout: records [0, opaqueCapacity) are opaque leaf surfels (args[0]),
// records [aggregateBase, aggregateBase + aggregateCapacity) are coverage aggregates (args[1], second
// FsIndirectDrawArgs in the registered args buffer; startInstance = aggregateBase). Two draws per frame.
int32_t FsRender_GetDrawLayout(uint32_t* opaqueCapacity, uint32_t* aggregateBase, uint32_t* aggregateCapacity);

#ifdef __cplusplus
}
#endif
