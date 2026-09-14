// Executor-private state shared by executor.cpp (lifecycle, submit, scheduler thread, render events),
// exec_resources.cpp (buffers, pipelines, cache, binaries, Unity imports) and host_api.cpp (C ABI).
// Android only (Unity plugin headers). Everything below is protected by Executor::mutex unless the
// member comment says atomic / render-thread-only.
#pragma once
#include "../fs_executor.h"
#include "sched_core.h"
#include "../telemetry.h"
#include "../../plugin_state.h"
#include <atomic>
#include <condition_variable>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

namespace fs {
namespace exec {

constexpr uint32_t kQueriesPerSlot = 2 + 2 * sched::kMaxDispatchesPerJob;   // job pair + per-dispatch pairs
constexpr uint32_t kFrameRingSize = 4;                                       // scanner-mode frame command buffers
constexpr uint32_t kFrameMarkRing = 1024;                                    // FRAME_END timestamp pairs
constexpr uint32_t kFrameStageRing = 512;                                    // frame-hook stage timestamp pairs (~8 stages x 64 frames)
constexpr uint32_t kFrameStageNameChars = 31;
constexpr uint32_t kMaxPushBytes = 128;
constexpr int64_t  kLogPeriodNs = 5000000000LL;                              // FS-HOST log line every 5 s
constexpr uint64_t kShutdownFenceTimeoutNs = 2000000000ull;

struct SlotVk {                        // one in-flight job slot (parallel to sched::ClassRing slots)
    VkCommandBuffer cb = VK_NULL_HANDLE;
    VkFence fence = VK_NULL_HANDLE;    // signalled by the job submit; reset before reuse
    VkQueryPool queries = VK_NULL_HANDLE;
    uint32_t dispatchCount = 0;
    bool recorded = false;             // fallback (graphics queue): recorded, waiting for the AccessQueue pump
    bool submitted = false;
    FsJobClass waitClass = FS_JOB_SCAN; uint64_t waitValue = 0;
    std::string name;
    std::vector<std::string> stageNames;
    std::function<void(bool, uint64_t, uint64_t)> onRetired;
};

struct ClassVk {
    VkCommandPool pool = VK_NULL_HANDLE;
    VkSemaphore timeline = VK_NULL_HANDLE;   // timeline semaphore (VK_NULL_HANDLE in fallback mode)
    std::vector<SlotVk> slots;
};

struct FrameSlot {                     // FS_FRAME_MODE_SCANNER: executor command buffer per frame
    VkCommandBuffer cb = VK_NULL_HANDLE;
    VkFence fence = VK_NULL_HANDLE;
    VkSemaphore ready = VK_NULL_HANDLE;    // binary fallback: graphics "ready" -> scanner
    VkSemaphore done = VK_NULL_HANDLE;     // binary fallback: scanner "done" -> graphics
    bool inFlight = false;
    uint64_t readyValue = 0, doneValue = 0; // frame timeline values (timeline mode)
    uint32_t frameIndex = 0;
    bool importedUnity = false;            // a hook imported a Unity resource this frame
};

struct PipelineRecord {
    std::string name;
    VkShaderModule module = VK_NULL_HANDLE;
    std::vector<VkDescriptorSetLayoutBinding> bindings;
    bool fromBinary = false;
    double createMs = 0.0;
};

struct GarbageItem {
    VkBuffer buffer = VK_NULL_HANDLE; VkDeviceMemory memory = VK_NULL_HANDLE;
    VkPipeline pipeline = VK_NULL_HANDLE; VkPipelineLayout layout = VK_NULL_HANDLE;
    VkDescriptorSetLayout setLayout = VK_NULL_HANDLE; VkDescriptorSet set = VK_NULL_HANDLE;
    VkShaderModule module = VK_NULL_HANDLE; VkImageView view = VK_NULL_HANDLE;
    std::function<void()> onRetired;   // world allocations (render blocks/nodes, page slabs): freed by the module
};

struct ImportedBuffer { UnityVulkanBuffer unity = {}; uint64_t frame = 0; uint64_t imports = 0; };
struct ImportedTexture { UnityVulkanImage unity = {}; VkImageView view = VK_NULL_HANDLE; uint64_t frame = 0; uint64_t imports = 0; };

struct WarmupStep { std::string name; std::function<bool()> step; bool done = false; bool failed = false; double ms = 0.0; };
struct FrameStageRec { char name[kFrameStageNameChars + 1] = {}; uint32_t frame = 0; bool ended = false; };

struct Executor {
    std::mutex mutex;
    std::condition_variable cv;                 // scheduler wake
    std::mutex hooksMutex;                      // registries below (register at static init / module init)

    // ---- host lifecycle
    bool hostInitialized = false;
    bool pendingTeardown = false;               // FsHost_Shutdown ran on the main thread; teardown happens on the render thread
    FsHostConfig cfg = {};
    std::atomic<int32_t> status{FS_HOST_UNINITIALIZED};
    std::mutex storageMutex;                    // storageRoot only (StorageDir() is called under Executor::mutex)
    std::string storageRoot;                    // FsHost_SetStorageRoot argument ("" = unset)
    std::string quarantineReason;
    int32_t quarantineResult = 0;

    // ---- Vulkan objects (valid while vkReady)
    std::atomic<bool> vkReady{false};
    VkDevice device = VK_NULL_HANDLE;
    uint32_t family = UINT32_MAX;
    VkQueue scannerQueue = VK_NULL_HANDLE;      // injected queue or VK_NULL_HANDLE (graphics-queue fallback)
    bool queueFallback = false;
    bool timelineOk = false;                    // timeline semaphores in use
    bool bdaEnabled = false;                    // bufferDeviceAddress feature enabled by the intercept
    ClassVk classes[sched::kClassCount];
    VkDescriptorPool descPool = VK_NULL_HANDLE;
    VkPipelineCache pipelineCache = VK_NULL_HANDLE;
    bool pipelineCacheLoaded = false; uint64_t pipelineCacheLoadedBytes = 0, pipelineCacheSavedBytes = 0;
    VkSampler samplerNearest = VK_NULL_HANDLE, samplerLinear = VK_NULL_HANDLE;
    // frame ring (FS_FRAME_MODE_SCANNER) + frame timeline
    VkCommandPool framePool = VK_NULL_HANDLE;
    VkSemaphore frameTimeline = VK_NULL_HANDLE;
    uint64_t frameTimelineValue = 0;            // last signalled
    FrameSlot frameSlots[kFrameRingSize];
    uint32_t frameSlotNext = 0;
    // frame marks (FS_HEVT_FRAME_END)
    VkQueryPool frameMarkPool = VK_NULL_HANDLE;
    uint32_t markWrite = 0, markRead = 0;       // render thread only
    bool markPoolReset = false;
    std::vector<std::pair<int64_t, uint64_t>> markCpu;   // cpuNs, unity frame per ring entry
    // frame-hook stages (FrameStageBegin/End)
    VkQueryPool frameStagePool = VK_NULL_HANDLE;
    uint32_t stageWrite = 0, stageRead = 0;     // render thread only
    bool stagePoolReset = false;
    std::vector<FrameStageRec> stageRecs;
    uint64_t frameStagesCollected = 0, frameStagesDropped = 0;
    // frame-end timeline: signalled on the graphics queue after each FRAME_END (value = executor frame index)
    VkSemaphore frameEndTimeline = VK_NULL_HANDLE;
    uint64_t frameEndRequested = 0;             // set by FRAME_END before AccessQueue
    uint64_t frameEndSignalled = 0;             // last value whose signal was submitted (mutex)
    int64_t  deferredFrameEnd = 0;
    // device procs
    PFN_vkGetSemaphoreCounterValue getSemaphoreCounterValue = nullptr;
    PFN_vkGetBufferDeviceAddress getBufferDeviceAddress = nullptr;
    PFN_vkResetQueryPool resetQueryPool = nullptr;

    // ---- scheduler
    sched::SchedulerCore core;
    sched::GarbageLedger garbage;
    std::unordered_map<uint64_t, GarbageItem> garbageItems;
    uint64_t nextGarbageToken = 1;
    sched::ScanRequestSlot scanRequests;
    std::thread schedThread; bool schedRunning = false; std::atomic<bool> schedStop{false};
    uint64_t frameEndSeq = 0, frameEndSeen = 0;    // FS_HEVT_FRAME_END wake counter
    bool retirementHappened = false;
    int64_t lastLogNs = 0;
    uint64_t fallbackPumps = 0, fallbackPumpedJobs = 0;
    std::vector<std::pair<std::function<void(bool, uint64_t, uint64_t)>, std::string>> pendingFailed;   // quarantine callbacks (run by fs-sched)
    uint64_t acquireSubmits = 0;                // AccessQueue empty submits (class timeline acquire / frame wait)
    uint64_t lastAcquiredRetired[sched::kClassCount] = {0, 0, 0, 0, 0};

    // ---- warm-up
    std::vector<WarmupStep> warmup;             // under hooksMutex
    std::thread warmupThread; bool warmupRunning = false;
    std::atomic<bool> warmupComplete{false};
    uint32_t warmupFailed = 0; double warmupTotalMs = 0.0;
    uint64_t warmupGeneration = 0;              // bumped per device init (steps re-run on re-init)

    // ---- registries (hooksMutex)
    std::vector<FrameHook> frameBeginHooks;
    std::vector<std::function<void(uint32_t)>> genericTicks;
    std::vector<std::function<void(uint32_t)>> classTicks[sched::kClassCount];
    LeaseValidator leaseValidator;              // under Executor::mutex (called inside SubmitJob)
    std::vector<std::function<void(bool)>> deviceHooks;
    std::vector<FrameStageSink> stageSinks;

    // ---- pipelines / imports (mutex)
    std::unordered_map<VkPipeline, PipelineRecord> pipelines;
    std::unordered_map<void*, ImportedBuffer> importedBuffers;
    std::unordered_map<void*, ImportedTexture> importedTextures;
    uint64_t buffersCreated = 0, buffersDestroyed = 0, pipelinesCreated = 0, pipelinesFromBinary = 0, binariesStored = 0, binariesLoadFailed = 0;
    uint64_t buffersLive = 0; uint64_t bufferBytesLive = 0;

    // ---- frame state (render thread; atomics readable elsewhere)
    std::atomic<uint32_t> frameIndex{0};
    std::atomic<bool> inRenderEvent{false};
    std::atomic<int32_t> frameMode{FS_FRAME_MODE_NONE};
    VkCommandBuffer frameCb = VK_NULL_HANDLE;   // render thread only
    bool frameBeginSeen = false;                // since last FRAME_END
    std::atomic<uint64_t> unityCurrentFrame{0}, unitySafeFrame{0};
    bool haveRecordingState = false;
    uint64_t modeCounts[4] = {0, 0, 0, 0};
    uint64_t frameBegins = 0, frameEnds = 0, frameMarksCollected = 0, frameSkippedRing = 0;
    int32_t frameLastMode = FS_FRAME_MODE_NONE;
    uint32_t frameHookCount = 0;
    bool loggedModeUnity = false, loggedModeScanner = false, loggedModeSkipped = false;

    // ---- frame hint
    FrameHint hint; bool hintValid = false;
};

Executor& X();

// executor.cpp
bool   RunOneWarmupStep(Executor& x, bool& completed);
void   FinishWarmup(Executor& x);
void   Quarantine(Executor& x, const char* reason, VkResult r);       // caller holds x.mutex
void   CollectGarbageLocked(Executor& x, bool everything, std::vector<GarbageItem>& out); // caller holds x.mutex; moves due items only; callbacks/destruction run unlocked
uint64_t PushGarbageLocked(Executor& x, const GarbageItem& item);     // caller holds x.mutex
void   RefreshRecordingState(Executor& x);                            // render thread: re-query Unity recording state
bool   HostInit(const FsHostConfig* cfg);
void   HostShutdown();
std::string HostTelemetryJson(bool includeStages);
void   HostSetStorageRoot(const char* path);
void   HostSetFrameBudget(uint32_t us);
void   HostSetFrameHint(double t, const float pos[3], const float vel[3], const float rot[4]);
bool   HostGetClassStats(int32_t cls, int64_t out[8]);
void   HostRequestScan(uint32_t observationId);

// exec_resources.cpp
bool   ResourcesInit(Executor& x);            // descriptor pool, pipeline cache (+load), samplers; x.mutex held
void   ResourcesShutdown(Executor& x);        // save cache, destroy pipelines/imports/pools; after WaitIdle; x.mutex held
void   SavePipelineCache(Executor& x);        // x.mutex must NOT be held (takes it)
void   ResourcesJson(Executor& x, std::string& members);   // x.mutex held; appends JSON members

} // namespace exec
} // namespace fs
