// Internal executor interface (native-only). Implemented by executor/*.cpp (C02); consumed by world/*.cpp
// (C04/C06/C08) and render/*.cpp (cull, C05). Contract §15 and docs/NATIVE_EXECUTOR_DESIGN.md.
#pragma once
#include <stdint.h>
#include <functional>
#include <string>
#include "vk_include.h"
#include "../../include/finalscan_host_api.h"

namespace fs {

struct Buffer {                 // device-local unless hostVisible
    VkBuffer       buffer = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    VkDeviceSize   size = 0;
    void*          mapped = nullptr;   // when hostVisible
    VkDeviceAddress address = 0;       // when bufferDeviceAddress enabled
};

struct Pipeline {               // compute only
    VkPipeline            pipeline = VK_NULL_HANDLE;
    VkPipelineLayout      layout = VK_NULL_HANDLE;
    VkDescriptorSetLayout setLayout = VK_NULL_HANDLE;
    VkDescriptorSet       set = VK_NULL_HANDLE;    // persistent, bound once (§15.3)
    uint32_t              pushBytes = 0;
    const char*           name = "";
};

// One bounded dispatch inside a job. Push constants ≤ 128 B. Indirect when argsBuffer != null.
struct Dispatch {
    const Pipeline* pipeline = nullptr;
    uint32_t gx = 1, gy = 1, gz = 1;
    const Buffer* argsBuffer = nullptr; VkDeviceSize argsOffset = 0;
    const void* push = nullptr; uint32_t pushBytes = 0;
    bool barrierAfter = true;             // global memory barrier compute→compute|indirect
};

struct JobDesc {
    FsJobClass cls = FS_JOB_SCAN;
    const char* name = "";
    uint32_t maxQuantumUs = 0;            // 0 = class default
    const Dispatch* dispatches = nullptr; uint32_t dispatchCount = 0;
    uint64_t waitTimelineValue = 0;       // wait on the class timeline of `waitClass` (0 = none)
    FsJobClass waitClass = FS_JOB_SCAN;
    std::function<void(bool success, uint64_t gpuStartNs, uint64_t gpuEndNs)> onRetired; // called on fs-sched thread
    uint32_t leaseSlot = FS_INDEX_NONE; uint32_t leaseGeneration = 0;   // dropped if page generation moved
    // Frame-end ordering (contract §15.3, cross-queue hazard): the job waits until Unity's graphics queue passed the
    // FRAME_END of executor frame `waitFrameEndValue` (its command buffers, including the barriers recorded by
    // ImportUnityTexture/ImportUnityBuffer in that frame, are submitted and ordered before the job). 0 = none. A value
    // whose frame end has not been signalled yet defers the job (counted), never a dangling wait.
    uint64_t waitFrameEndValue = 0;
};

// Executor API. All functions thread-safe unless noted.
bool     ExecReady();
VkDevice ExecDevice();
uint32_t ExecQueueFamily();
bool     ExecHasScannerQueue();
bool     CreateBuffer(Buffer& out, VkDeviceSize size, VkBufferUsageFlags usage, bool hostVisible, const char* name);
void     DestroyBuffer(Buffer& b);                                   // deferred until GPU retirement
bool     CreateComputePipeline(Pipeline& out, const uint32_t* spirv, size_t spirvWords, uint32_t pushBytes,
                               const VkDescriptorSetLayoutBinding* bindings, uint32_t bindingCount, const char* name);
bool     BindBuffer(Pipeline& p, uint32_t binding, const Buffer& b, VkDeviceSize offset = 0, VkDeviceSize range = VK_WHOLE_SIZE);
void     DestroyPipeline(Pipeline& p);
// Submit a job; returns job id or 0 when the class ring is full (job deferred, counter bumped). Never blocks.
uint64_t SubmitJob(const JobDesc& desc);
uint64_t ClassTimelineValue(FsJobClass cls);                          // last retired value
// Unity resources: import once per generation on the render thread (only callable inside render events).
bool     ImportUnityBuffer(void* unityNativePtr, Buffer& out);
// Telemetry
void     CounterAdd(FsCounter c, int64_t v);
int64_t  CounterGet(FsCounter c);
void     TelemetryStage(FsJobClass cls, const char* stage, uint64_t gpuStartNs, uint64_t gpuEndNs);
uint64_t GpuNowEstimateNs();                                         // from frame-mark correlation
uint32_t FrameIndex();

// Frame hooks called by the executor on the render thread each frame (registered by world/render modules).
using FrameHook = std::function<void(VkCommandBuffer unityCmd, uint32_t frameIndex)>;
void     RegisterFrameBeginHook(FrameHook hook);   // runs inside FS_HEVT_FRAME_BEGIN with Unity's command buffer
void     RegisterSchedulerTick(std::function<void(uint32_t budgetUs)> tick);   // runs on fs-sched thread each wake

// ---- C02 additive extensions (executor/*.cpp). Existing signatures above are frozen. ------------------
// Lease validation: registered by the world module; SubmitJob drops a job (counted) when the callback
// returns false for {leaseSlot, leaseGeneration}. Called under the executor lock; must be cheap and non-blocking.
using LeaseValidator = std::function<bool(uint32_t slot, uint32_t generation)>;
void     RegisterLeaseValidator(LeaseValidator validator);

// Warm-up: pipelines are created on the fs-warmup worker thread before the host reports FS_HOST_READY.
// Modules register steps (each creates its pipelines via CreateComputePipeline and returns success); steps
// registered after warm-up completed run immediately on the calling thread (logged as a late warm-up).
void     RegisterWarmupStep(const char* name, std::function<bool()> step);
// Device lifecycle: hook(true) after the executor's Vulkan objects exist (render/main thread, ExecReady() is
// true), hook(false) before they are destroyed (device shutdown/reset or FsHost_Shutdown; vkDeviceWaitIdle done).
void     RegisterDeviceHook(std::function<void(bool up)> hook);

// Frame-begin integration. Inside FS_HEVT_FRAME_BEGIN the executor runs the frame-begin hooks in one of two
// modes chosen at runtime from Unity's recording state (docs in executor/executor.cpp, "frame-begin modes"):
//   FS_FRAME_MODE_UNITY_CMD : hooks record into Unity's command buffer (outside a render pass); the executor
//                             adds a COMPUTE -> DRAW_INDIRECT|VERTEX|FRAGMENT barrier after the hooks, so the
//                             Unity draw of the same frame observes the compute results.
//   FS_FRAME_MODE_SCANNER   : Unity's command buffer was inside a render pass: hooks record into an executor
//                             command buffer submitted on the scanner queue; Unity's graphics queue waits on
//                             the frame timeline value before its draw (AccessQueue empty submit).
//   FS_FRAME_MODE_SKIPPED   : neither possible this frame (no recording state / no scanner queue+timeline).
enum FrameMode { FS_FRAME_MODE_NONE = 0, FS_FRAME_MODE_UNITY_CMD = 1, FS_FRAME_MODE_SCANNER = 2, FS_FRAME_MODE_SKIPPED = 3 };
FrameMode CurrentFrameMode();                       // valid during a frame-begin hook
// The command buffer hooks must record into. Equals the `unityCmd` argument unless a hook called ImportUnityBuffer
// (a fresh import invalidates Unity's recording state); call this after ImportUnityBuffer before recording.
VkCommandBuffer FrameCommandBuffer();
bool     InRenderEvent();                           // true on the render thread inside an FS_HEVT_* event
uint64_t UnitySafeFrameNumber();                    // Unity's safeFrameNumber from the last recording state

// Scan requests (FsScan_RequestTick, contract §3.3): latest coherent observation only. Returns false when no
// request is pending; the world scheduler tick consumes it. Skipped (superseded) requests are counted.
bool     TakeScanRequest(uint32_t& observationId);
// Frame hint (FsSched_SetFrameHint): predicted display time + head pose, for the residency/scheduler ticks.
struct FrameHint { double predictedDisplayTimeSec = 0; float headPos[3] = {0,0,0}; float headVel[3] = {0,0,0}; float headRot[4] = {0,0,0,1}; uint64_t sequence = 0; };
bool     GetFrameHint(FrameHint& out);              // false until the first hint arrived

// Storage root (FsHost_SetStorageRoot): "<root>/finalscan" holds pipeline-cache.bin and binaries/. Empty = unset.
std::string StorageDir();
// Executor status (FsHostStatus) and quarantine helper for modules that observe VK_ERROR_DEVICE_LOST themselves.
int32_t  ExecStatus();
void     ExecQuarantine(const char* reason, VkResult result);
uint32_t ExecFrameBudgetUs();

// plugin_entry.cpp hooks (called from the existing Unity device-event and render-event paths).
void     FsHostExecutorOnDeviceInit();              // after fs::Device() is ready (kUnityGfxDeviceEventInitialize)
void     FsHostExecutorOnDeviceShutdown();          // before fs::Device() tears down (Shutdown/BeforeReset); vkDeviceWaitIdle allowed
bool     FsHostExecutorRenderEvent(int eventId);    // returns true when the id is an FS_HEVT_* event it handled

// ---- C02 additive extensions, second set (executor/*.cpp) ---------------------------------------------
// Unity texture import (depth priors, contract §13.5): AccessTexture(WholeImage, SHADER_READ_ONLY_OPTIMAL,
// COMPUTE_SHADER, SHADER_READ, PipelineBarrier) on every call (Unity may re-transition the image between
// frames), image view cached per native pointer and recreated only when the VkImage changes (old view
// destroyed after retirement). Only callable inside a render event that is not queue-access configured.
bool     ImportUnityTexture(void* unityNativeTexturePtr, VkImage& image, VkImageView& view, VkFormat& fmt,
                            uint32_t& w, uint32_t& h, uint32_t& layers);
// Persistent image descriptor for an imported view. The binding must have been declared at
// CreateComputePipeline (STORAGE_IMAGE / SAMPLED_IMAGE / COMBINED_IMAGE_SAMPLER); sampler only for the
// latter (ExecSampler(...) provides shared samplers). Same lifetime rule as BindBuffer: update before the
// pipeline's jobs are submitted or after they retired.
bool     BindImage(Pipeline& p, uint32_t binding, VkImageView view, VkImageLayout layout, VkSampler sampler = VK_NULL_HANDLE);
VkSampler ExecSampler(bool linear);                 // executor-owned nearest/linear clamp samplers (destroyed at shutdown)
// Scheduler ticks bound to a job class run in class order PUBLISH -> SCAN -> INNER_RESIDENCY -> APPEARANCE
// -> COLD before the generic RegisterSchedulerTick ticks (registration order). Each receives the remaining
// frame budget in µs; SubmitJob consumes the budget.
void     RegisterClassSchedulerTick(FsJobClass cls, std::function<void(uint32_t budgetUs)> tick);
// Additional render event ids (additive to FsHostRenderEvent): C# may issue them; none is required.
enum FsHostRenderEventExt {
    FS_HEVT_QUEUE_ACCESS = 103    // internal id for AccessQueue callbacks (frame timeline wait / graphics-queue pump)
};
// Timeline support actually in use (false = binary-semaphore + fence fallback, in-order queue).
bool     ExecTimelineSemaphores();
// Timeline value a submitted job signals (for JobDesc::waitTimelineValue of a dependent job). Returns false
// once the job retired (its value is then <= ClassTimelineValue(cls): no wait needed) or the id is unknown.
bool     JobTimelineValue(uint64_t jobId, FsJobClass& cls, uint64_t& value);
uint64_t ClassTimelineAllocated(FsJobClass cls);   // value of the most recently submitted job of the class
// Unity's currentFrameNumber from the last recording state (0 until the first frame event).
uint64_t UnityCurrentFrameNumber();
// Frame-end timeline (§15.3): value = executor frame index whose FRAME_END was ordered on the graphics queue.
uint64_t FrameEndSignalled();
// Retirement-safe release (contract §11, C09R §15): `onRetired` runs (fs-sched thread) once every job submitted
// before this call retired AND Unity's safeFrameNumber passed the current frame. Modules free render blocks,
// tree nodes and page slabs through it; a generation is logically obsolete at once, physically reusable only then.
uint64_t RetireLater(std::function<void()> onRetired);
size_t   RetirementBacklog();
// FsSched_GetClassStats layout: submitted, retired, deferred, failed, gpuUsTotal, gpuUsLast, inFlight, quantumUs.
bool     ExecClassStats(FsJobClass cls, int64_t out[8]);

// ---- Frame-hook GPU stages (contract §15.8 / §20 device receipt rule) -----------------------------------
// Work recorded by frame-begin hooks into the frame command buffer carries device timestamps like executor jobs:
// FrameStageBegin/End bracket a stage (TOP_OF_PIPE before, BOTTOM_OF_PIPE after) in a per-frame query ring; the
// results are collected without waiting at later FRAME_ENDs, reported to Telemetry (class FS_JOB_CLASS_COUNT =
// "frame") and to every registered sink. Only callable inside a frame-begin hook. Returns UINT32_MAX when the ring
// is full (the stage is then simply unmeasured this frame, never blocked).
uint32_t FrameStageBegin(VkCommandBuffer cmd, const char* name);
void     FrameStageEnd(VkCommandBuffer cmd, uint32_t handle);
using FrameStageSink = std::function<void(const char* name, uint64_t gpuStartNs, uint64_t gpuEndNs, uint32_t frameIndex)>;
void     RegisterFrameStageSink(FrameStageSink sink);   // called on the render thread when a stage's timestamps arrived
// (additive, world module) The FsHostConfig FsHost_Init received (normalised copy); null before FsHost_Init.
const FsHostConfig* ExecConfig();

} // namespace fs
