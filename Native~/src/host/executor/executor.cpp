// FinalScan native executor (contract cut C02): lifecycle, per-class fence rings + timeline semaphores,
// SubmitJob, the fs-sched scheduler thread, render events (frame-begin modes, frame marks), warm-up,
// quarantine, telemetry JSON. Contract §15, §20; docs/NATIVE_EXECUTOR_DESIGN.md; VULKAN_DONOR_LEDGER invariants.
//
// Threads
//   Unity render thread : FS_HEVT_* events (frame hooks, frame marks, AccessQueue callbacks), device init/shutdown
//   fs-sched            : fence polling (vkGetFenceStatus, never wait), retirement, budget, scheduler ticks
//   fs-warmup           : pipeline creation before FS_HOST_READY
//   any                 : SubmitJob / Create* (executor mutex); the scanner queue is submitted under its own mutex
//
// Frame-begin modes (FS_HEVT_FRAME_BEGIN, event configured EnsureOutsideRenderPass + GraphicsQueueAccess_DontCare)
//   FS_FRAME_MODE_UNITY_CMD : Unity's recording state has a command buffer and renderPass == VK_NULL_HANDLE (Unity
//                             6000.5 reports subPassIndex 0 outside a pass, so the render pass handle is the test):
//                             hooks record into Unity's command buffer; a COMPUTE -> DRAW_INDIRECT|VERTEX|FRAGMENT
//                             barrier follows so this frame's Unity draw observes the results. Expected mode on Quest.
//   FS_FRAME_MODE_SCANNER   : Unity is inside a render pass despite the precondition (SRP RenderPass API): hooks
//                             record into an executor command buffer. Inside an AccessQueue(flush=true) callback the
//                             executor submits (1) an empty graphics-queue batch signalling "ready" (Unity's barriers
//                             for imported resources are then submitted), (2) the frame command buffer on the scanner
//                             queue waiting "ready" and signalling "done", (3) an empty graphics-queue batch waiting
//                             "done" so every later Unity submission (this frame's draw) is ordered after it. Without a
//                             scanner queue the frame command buffer is submitted on the graphics queue itself.
//   FS_FRAME_MODE_SKIPPED   : no recording state / quarantine / frame ring full: hooks are not run (counted).
// Unity's graphics queue is touched only inside AccessQueue callbacks (ledger invariant 8).
#include "executor_internal.h"
#include "../../json_writer.h"
#include "../../log.h"
#include <algorithm>
#include <chrono>
#include <cstring>
#include <pthread.h>

namespace fs {
namespace exec {

Executor& X() { static Executor x; return x; }

namespace {

const char* StatusName(int32_t s) {
    switch (s) {
        case FS_HOST_UNINITIALIZED: return "UNINITIALIZED"; case FS_HOST_WARMING_UP: return "WARMING_UP"; case FS_HOST_READY: return "READY";
        case FS_HOST_DEVICE_LOST: return "DEVICE_LOST"; case FS_HOST_QUARANTINED: return "QUARANTINED"; case FS_HOST_SHUTDOWN: return "SHUTDOWN";
        default: return "?";
    }
}

template <typename T>
T DeviceProc(const char* name, const char* alt = nullptr) {
    DeviceState& d = Device();
    if (d.instance.getInstanceProcAddr == nullptr || d.instance.instance == VK_NULL_HANDLE) return nullptr;
    auto gdpa = reinterpret_cast<PFN_vkGetDeviceProcAddr>(d.instance.getInstanceProcAddr(d.instance.instance, "vkGetDeviceProcAddr"));
    if (gdpa == nullptr) return nullptr;
    T p = reinterpret_cast<T>(gdpa(d.instance.device, name));
    if (p == nullptr && alt) p = reinterpret_cast<T>(gdpa(d.instance.device, alt));
    return p;
}

void ConfigureEvent(int id, const UnityVulkanPluginEventConfig& cfg) {
    DeviceState& d = Device();
    if (d.vulkanV2) d.vulkanV2->ConfigureEvent(id, &cfg);
    else if (d.vulkanV1) d.vulkanV1->ConfigureEvent(id, &cfg);
}

bool RecordingState(UnityVulkanRecordingState* rs, UnityVulkanGraphicsQueueAccess access) {
    DeviceState& d = Device();
    if (d.vulkanV2) return d.vulkanV2->CommandRecordingState(rs, access);
    if (d.vulkanV1) return d.vulkanV1->CommandRecordingState(rs, access);
    return false;
}

void AccessQueue(UnityRenderingEventAndData cb, void* data, bool flush) {
    DeviceState& d = Device();
    if (d.vulkanV2) d.vulkanV2->AccessQueue(cb, FS_HEVT_QUEUE_ACCESS, data, flush);
    else if (d.vulkanV1) d.vulkanV1->AccessQueue(cb, FS_HEVT_QUEUE_ACCESS, data, flush);
}

VkFence MakeFence(VkDevice dev) { VkFenceCreateInfo fi = {}; fi.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO; VkFence f = VK_NULL_HANDLE; vkCreateFence(dev, &fi, nullptr, &f); return f; }
VkSemaphore MakeBinarySemaphore(VkDevice dev) { VkSemaphoreCreateInfo si = {}; si.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO; VkSemaphore s = VK_NULL_HANDLE; vkCreateSemaphore(dev, &si, nullptr, &s); return s; }
VkSemaphore MakeTimelineSemaphore(VkDevice dev) {
    VkSemaphoreTypeCreateInfo ti = {}; ti.sType = VK_STRUCTURE_TYPE_SEMAPHORE_TYPE_CREATE_INFO; ti.semaphoreType = VK_SEMAPHORE_TYPE_TIMELINE; ti.initialValue = 0;
    VkSemaphoreCreateInfo si = {}; si.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO; si.pNext = &ti;
    VkSemaphore s = VK_NULL_HANDLE;
    if (vkCreateSemaphore(dev, &si, nullptr, &s) != VK_SUCCESS) return VK_NULL_HANDLE;
    return s;
}
VkQueryPool MakeTimestampPool(VkDevice dev, uint32_t count) {
    VkQueryPoolCreateInfo qi = {}; qi.sType = VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO; qi.queryType = VK_QUERY_TYPE_TIMESTAMP; qi.queryCount = count;
    VkQueryPool p = VK_NULL_HANDLE; vkCreateQueryPool(dev, &qi, nullptr, &p); return p;
}
VkCommandPool MakeCommandPool(VkDevice dev, uint32_t family) {
    VkCommandPoolCreateInfo pi = {}; pi.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO; pi.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT; pi.queueFamilyIndex = family;
    VkCommandPool p = VK_NULL_HANDLE; vkCreateCommandPool(dev, &pi, nullptr, &p); return p;
}
VkCommandBuffer AllocPrimary(VkDevice dev, VkCommandPool pool) {
    VkCommandBufferAllocateInfo ai = {}; ai.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO; ai.commandPool = pool; ai.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; ai.commandBufferCount = 1;
    VkCommandBuffer cb = VK_NULL_HANDLE; vkAllocateCommandBuffers(dev, &ai, &cb); return cb;
}

// Scanner-queue submit under the queue mutex shared with the C01 probes (plugin_state.h).
VkResult SubmitScanner(Executor& x, const VkSubmitInfo& si, VkFence fence) {
    std::lock_guard<std::mutex> lock(Device().scannerSubmitMutex);
    return vkQueueSubmit(x.scannerQueue, 1, &si, fence);
}

void DestroyGarbageItem(Executor& x, const GarbageItem& g) {
    if (g.set) vkFreeDescriptorSets(x.device, x.descPool, 1, &g.set);
    if (g.pipeline) vkDestroyPipeline(x.device, g.pipeline, nullptr);
    if (g.layout) vkDestroyPipelineLayout(x.device, g.layout, nullptr);
    if (g.setLayout) vkDestroyDescriptorSetLayout(x.device, g.setLayout, nullptr);
    if (g.module) vkDestroyShaderModule(x.device, g.module, nullptr);
    if (g.view) vkDestroyImageView(x.device, g.view, nullptr);
    if (g.buffer) vkDestroyBuffer(x.device, g.buffer, nullptr);
    if (g.memory) vkFreeMemory(x.device, g.memory, nullptr);
}

// ---- warm-up ---------------------------------------------------------------------------------------
// Runs one pending step (any thread). Returns false when nothing was pending; sets `completed` when the
// list drained with this call (under hooksMutex so a concurrent RegisterWarmupStep cannot slip between).
bool RunOneWarmupStep(Executor& x, bool& completed) {
    completed = false;
    size_t idx = SIZE_MAX; std::function<bool()> fn; std::string name;
    {
        std::lock_guard<std::mutex> lock(x.hooksMutex);
        for (size_t i = 0; i < x.warmup.size(); ++i) if (!x.warmup[i].done) { idx = i; fn = x.warmup[i].step; name = x.warmup[i].name; break; }
        if (idx == SIZE_MAX) {
            if (!x.warmupComplete.load(std::memory_order_acquire)) { x.warmupComplete.store(true, std::memory_order_release); completed = true; }
            return false;
        }
    }
    const int64_t t0 = MonotonicNs();
    bool ok = false;
    if (fn) ok = fn();
    const double ms = static_cast<double>(MonotonicNs() - t0) / 1e6;
    {
        std::lock_guard<std::mutex> lock(x.hooksMutex);
        if (idx < x.warmup.size()) { x.warmup[idx].done = true; x.warmup[idx].failed = !ok; x.warmup[idx].ms = ms; }
        x.warmupTotalMs += ms; if (!ok) ++x.warmupFailed;
    }
    if (ok) Log("warm-up step %s: %.2f ms", name.c_str(), ms);
    else LogError("warm-up step %s FAILED after %.2f ms (host stays WARMING_UP; FS_HEVT_WARMUP_STEP retries)", name.c_str(), ms);
    return true;
}

void FinishWarmup(Executor& x) {
    uint32_t failed; size_t steps; double ms;
    { std::lock_guard<std::mutex> lock(x.hooksMutex); failed = x.warmupFailed; steps = x.warmup.size(); ms = x.warmupTotalMs; }
    int32_t expected = FS_HOST_WARMING_UP;
    if (failed == 0) x.status.compare_exchange_strong(expected, FS_HOST_READY);
    Log("warm-up complete: steps=%zu failed=%u totalMs=%.1f status=%s", steps, failed, ms, StatusName(x.status.load()));
    SavePipelineCache(x);
    x.cv.notify_all();
}

// FS_HEVT_WARMUP_STEP: drives warm-up when the worker thread is unavailable (or finished with failures):
// re-arms one failed step per event and runs one pending step on the render thread.
void WarmupStepEvent(Executor& x) {
    if (x.status.load(std::memory_order_acquire) != FS_HOST_WARMING_UP) return;
    if (x.warmupRunning && !x.warmupComplete.load(std::memory_order_acquire)) return;   // the worker is driving it
    {
        std::lock_guard<std::mutex> lock(x.hooksMutex);
        for (WarmupStep& s : x.warmup) if (s.failed) { s.done = false; s.failed = false; if (x.warmupFailed) --x.warmupFailed; x.warmupComplete.store(false, std::memory_order_release); break; }
    }
    bool completed = false;
    RunOneWarmupStep(x, completed);
    if (completed) FinishWarmup(x);
}

void WarmupThread() {
    pthread_setname_np(pthread_self(), "fs-warmup");
    Executor& x = X();
    bool completed = false;
    while (!x.schedStop.load(std::memory_order_acquire)) {
        if (!RunOneWarmupStep(x, completed)) break;
    }
    if (completed) FinishWarmup(x);
}

// ---- retirement (fs-sched) ---------------------------------------------------------------------------
struct RetiredJob { std::function<void(bool, uint64_t, uint64_t)> cb; bool success; uint64_t start, end; std::string name; };

// Caller holds x.mutex. Polls every in-flight slot once; fills callbacks; returns retired count.
uint32_t RetireLocked(Executor& x, std::vector<RetiredJob>& out) {
    uint32_t retired = 0;
    for (uint32_t c = 0; c < sched::kClassCount; ++c) {
        sched::ClassRing& ring = x.core.Ring(static_cast<FsJobClass>(c));
        ClassVk& cv = x.classes[c];
        for (uint32_t i = 0; i < ring.Capacity(); ++i) {
            sched::SlotState& ss = ring.Slot(i);
            if (!ss.inFlight) continue;
            SlotVk& sv = cv.slots[i];
            if (!sv.submitted) continue;                       // fallback: recorded, waiting for the pump
            if (sv.fence == VK_NULL_HANDLE) continue;          // never poll a null fence (ledger lesson 2)
            const VkResult fr = vkGetFenceStatus(x.device, sv.fence);
            if (fr == VK_NOT_READY) continue;
            if (fr != VK_SUCCESS) { Quarantine(x, "vkGetFenceStatus", fr); return retired; }
            uint64_t ts[kQueriesPerSlot] = {};
            const uint32_t n = 2 + 2 * sv.dispatchCount;
            const VkResult qr = vkGetQueryPoolResults(x.device, sv.queries, 0, n, sizeof(uint64_t) * n, ts, sizeof(uint64_t), VK_QUERY_RESULT_64_BIT);
            const bool haveTs = qr == VK_SUCCESS;
            const uint64_t start = haveTs ? static_cast<uint64_t>(TicksToNs(ts[0])) : 0, end = haveTs ? static_cast<uint64_t>(TicksToNs(ts[1])) : 0;
            const double gpuUs = haveTs && end > start ? static_cast<double>(end - start) / 1e3 : 0.0;
            const uint32_t frame = ss.frameIndex;
            const FsJobClass cls = static_cast<FsJobClass>(c);
            x.core.Retire(cls, i, true, gpuUs);
            if (haveTs) {
                for (uint32_t k = 0; k < sv.dispatchCount; ++k)
                    Tele().Stage(cls, sv.stageNames[k].c_str(), static_cast<uint64_t>(TicksToNs(ts[2 + 2 * k])), static_cast<uint64_t>(TicksToNs(ts[3 + 2 * k])), frame);
                Tele().Stage(cls, sv.name.c_str(), start, end, frame);
            }
            if (qr == VK_ERROR_DEVICE_LOST) { Quarantine(x, "vkGetQueryPoolResults", qr); return retired; }
            out.push_back({std::move(sv.onRetired), true, start, end, sv.name});
            sv.onRetired = nullptr; sv.submitted = false; sv.recorded = false; sv.stageNames.clear();
            vkResetFences(x.device, 1, &sv.fence);
            ++retired;
        }
    }
    // frame ring slots (scanner mode)
    for (FrameSlot& fs : x.frameSlots) {
        if (!fs.inFlight || fs.fence == VK_NULL_HANDLE) continue;
        const VkResult fr = vkGetFenceStatus(x.device, fs.fence);
        if (fr == VK_SUCCESS) { fs.inFlight = false; vkResetFences(x.device, 1, &fs.fence); }
        else if (fr != VK_NOT_READY) { Quarantine(x, "vkGetFenceStatus(frame)", fr); return retired; }
    }
    return retired;
}

bool AnyFrameSlotInFlight(const Executor& x) { for (const FrameSlot& fs : x.frameSlots) if (fs.inFlight) return true; return false; }

void SchedThread() {
    pthread_setname_np(pthread_self(), "fs-sched");
    Executor& x = X();
    std::vector<RetiredJob> retiredJobs;
    std::vector<std::function<void(uint32_t)>> ticks;
    while (true) {
        bool frameEnd = false; uint32_t retired = 0; bool runTicks = false;
        {
            std::unique_lock<std::mutex> lk(x.mutex);
            if (x.schedStop.load(std::memory_order_acquire)) break;
            const bool inflight = x.core.AnyInFlight() || AnyFrameSlotInFlight(x);
            const bool wakePending = x.frameEndSeq != x.frameEndSeen || !x.pendingFailed.empty();   // garbage rides on frame-end / retirement wakes
            if (!inflight && !wakePending) x.cv.wait(lk);
            else x.cv.wait_for(lk, std::chrono::milliseconds(1));
            if (x.schedStop.load(std::memory_order_acquire)) break;
            frameEnd = x.frameEndSeq != x.frameEndSeen; x.frameEndSeen = x.frameEndSeq;
            if (x.vkReady.load(std::memory_order_acquire)) {
                retired = RetireLocked(x, retiredJobs);
                for (auto& pf : x.pendingFailed) retiredJobs.push_back({std::move(pf.first), false, 0, 0, pf.second});
                x.pendingFailed.clear();
                CollectGarbageLocked(x, false);
                if (frameEnd) x.core.BeginFrame(x.frameIndex.load(std::memory_order_relaxed));
            }
            runTicks = (frameEnd || retired > 0) && x.status.load(std::memory_order_acquire) == FS_HOST_READY && x.core.AcceptingSubmits();
        }
        for (RetiredJob& j : retiredJobs) if (j.cb) j.cb(j.success, j.start, j.end);
        retiredJobs.clear();
        if (runTicks) {
            {
                std::lock_guard<std::mutex> lock(x.hooksMutex);
                ticks.clear();
                for (uint32_t c = 0; c < sched::kClassCount; ++c) for (auto& t : x.classTicks[c]) ticks.push_back(t);
                for (auto& t : x.genericTicks) ticks.push_back(t);
            }
            for (auto& t : ticks) {
                uint32_t remaining;
                { std::lock_guard<std::mutex> lock(x.mutex); remaining = x.core.Budget().Remaining(); }
                t(remaining);
            }
        }
        const int64_t now = MonotonicNs();
        bool doLog = false;
        { std::lock_guard<std::mutex> lock(x.mutex); if (now - x.lastLogNs >= kLogPeriodNs) { x.lastLogNs = now; doLog = true; } }
        if (doLog) {
            const std::string json = HostTelemetryJson(false);
            const std::string line = "FS-HOST " + json;
            LogRaw(ANDROID_LOG_INFO, line.c_str());
        }
    }
}

// ---- Vulkan object lifecycle ------------------------------------------------------------------------
void DestroyRingsLocked(Executor& x) {
    for (ClassVk& cv : x.classes) {
        for (SlotVk& s : cv.slots) {
            if (s.fence) vkDestroyFence(x.device, s.fence, nullptr);
            if (s.queries) vkDestroyQueryPool(x.device, s.queries, nullptr);
        }
        cv.slots.clear();
        if (cv.pool) { vkDestroyCommandPool(x.device, cv.pool, nullptr); cv.pool = VK_NULL_HANDLE; }
        if (cv.timeline) { vkDestroySemaphore(x.device, cv.timeline, nullptr); cv.timeline = VK_NULL_HANDLE; }
    }
    for (FrameSlot& fs : x.frameSlots) {
        if (fs.fence) vkDestroyFence(x.device, fs.fence, nullptr);
        if (fs.ready) vkDestroySemaphore(x.device, fs.ready, nullptr);
        if (fs.done) vkDestroySemaphore(x.device, fs.done, nullptr);
        fs = FrameSlot{};
    }
    if (x.framePool) { vkDestroyCommandPool(x.device, x.framePool, nullptr); x.framePool = VK_NULL_HANDLE; }
    if (x.frameTimeline) { vkDestroySemaphore(x.device, x.frameTimeline, nullptr); x.frameTimeline = VK_NULL_HANDLE; }
    if (x.frameMarkPool) { vkDestroyQueryPool(x.device, x.frameMarkPool, nullptr); x.frameMarkPool = VK_NULL_HANDLE; }
}

// Creates every executor Vulkan object. Caller holds x.mutex; Device() is ready; host initialized.
bool VkInitLocked(Executor& x) {
    DeviceState& d = Device();
    x.device = d.instance.device; x.family = d.instance.queueFamilyIndex;
    const bool wantScanner = x.cfg.useScannerQueue != 0;
    x.scannerQueue = wantScanner ? d.scannerQueue : VK_NULL_HANDLE;
    x.queueFallback = x.scannerQueue == VK_NULL_HANDLE;
    x.timelineOk = d.timelineEnabled;
    { InterceptState& st = Intercept(); std::lock_guard<std::mutex> lock(st.mutex); x.bdaEnabled = st.finalPatch.requested.bufferDeviceAddress; }
    x.getSemaphoreCounterValue = DeviceProc<PFN_vkGetSemaphoreCounterValue>("vkGetSemaphoreCounterValue", "vkGetSemaphoreCounterValueKHR");
    x.getBufferDeviceAddress = DeviceProc<PFN_vkGetBufferDeviceAddress>("vkGetBufferDeviceAddress", "vkGetBufferDeviceAddressKHR");
    x.resetQueryPool = d.resetQueryPool;
    if (x.timelineOk && x.getSemaphoreCounterValue == nullptr) { x.timelineOk = false; Log("executor: timeline semaphore feature enabled but vkGetSemaphoreCounterValue missing; using binary fallback"); }

    const sched::CoreConfig cc = sched::NormalizeConfig(&x.cfg, x.queueFallback);
    x.core.Init(cc);
    x.garbage = sched::GarbageLedger{}; x.garbageItems.clear(); x.nextGarbageToken = 1;
    for (uint32_t c = 0; c < sched::kClassCount; ++c) {
        ClassVk& cv = x.classes[c];
        cv.pool = MakeCommandPool(x.device, x.family);
        if (cv.pool == VK_NULL_HANDLE) { LogError("executor: command pool for class %s failed", sched::ClassName(c)); return false; }
        cv.timeline = x.timelineOk ? MakeTimelineSemaphore(x.device) : VK_NULL_HANDLE;
        if (x.timelineOk && cv.timeline == VK_NULL_HANDLE) { x.timelineOk = false; Log("executor: timeline semaphore creation failed; binary/fence fallback"); }
        cv.slots.assign(cc.inFlightMax[c], SlotVk{});
        for (SlotVk& s : cv.slots) {
            s.cb = AllocPrimary(x.device, cv.pool); s.fence = MakeFence(x.device); s.queries = MakeTimestampPool(x.device, kQueriesPerSlot);
            if (!s.cb || !s.fence || !s.queries) { LogError("executor: slot objects for class %s failed", sched::ClassName(c)); return false; }
        }
    }
    if (!x.timelineOk) for (ClassVk& cv : x.classes) if (cv.timeline) { vkDestroySemaphore(x.device, cv.timeline, nullptr); cv.timeline = VK_NULL_HANDLE; }
    x.framePool = MakeCommandPool(x.device, x.family);
    x.frameTimeline = x.timelineOk ? MakeTimelineSemaphore(x.device) : VK_NULL_HANDLE;
    x.frameTimelineValue = 0; x.frameSlotNext = 0;
    for (FrameSlot& fs : x.frameSlots) {
        fs = FrameSlot{};
        fs.cb = AllocPrimary(x.device, x.framePool); fs.fence = MakeFence(x.device);
        if (!x.timelineOk) { fs.ready = MakeBinarySemaphore(x.device); fs.done = MakeBinarySemaphore(x.device); }
        if (!fs.cb || !fs.fence) { LogError("executor: frame slot objects failed"); return false; }
    }
    x.frameMarkPool = MakeTimestampPool(x.device, kFrameMarkRing * 2);
    x.markCpu.assign(kFrameMarkRing, {0, 0}); x.markWrite = x.markRead = 0; x.markPoolReset = false;
    if (!ResourcesInit(x)) return false;
    for (uint32_t c = 0; c < sched::kClassCount; ++c) x.lastAcquiredRetired[c] = 0;
    x.acquireSubmits = 0; x.frameBeginSeen = false; x.haveRecordingState = false;
    x.frameMode.store(FS_FRAME_MODE_NONE); x.frameCb = VK_NULL_HANDLE;
    x.pendingFailed.clear(); x.frameEndSeq = x.frameEndSeen = 0; x.lastLogNs = MonotonicNs();
    x.quarantineReason.clear(); x.quarantineResult = 0;
    x.vkReady.store(true, std::memory_order_release);
    x.status.store(FS_HOST_WARMING_UP, std::memory_order_release);
    Log("executor: vulkan objects ready: queue=%s timeline=%d bda=%d budgetUs=%u inFlight=[%u,%u,%u,%u,%u] quantumUs=[%u,%u,%u,%u,%u] storage=%s cacheSeeded=%d(%llu B)",
        x.queueFallback ? "graphics(fallback, AccessQueue pump, quantum<=1ms)" : "scanner", x.timelineOk, x.bdaEnabled, cc.frameBudgetUs,
        cc.inFlightMax[0], cc.inFlightMax[1], cc.inFlightMax[2], cc.inFlightMax[3], cc.inFlightMax[4],
        cc.quantumUs[0], cc.quantumUs[1], cc.quantumUs[2], cc.quantumUs[3], cc.quantumUs[4],
        StorageDir().empty() ? "(unset)" : StorageDir().c_str(), x.pipelineCacheLoaded, static_cast<unsigned long long>(x.pipelineCacheLoadedBytes));
    return true;
}

void StartThreads(Executor& x) {
    x.schedStop.store(false, std::memory_order_release);
    {
        std::lock_guard<std::mutex> lock(x.hooksMutex);
        for (WarmupStep& s : x.warmup) { s.done = false; s.failed = false; s.ms = 0; }
        x.warmupFailed = 0; x.warmupTotalMs = 0; ++x.warmupGeneration;
    }
    x.warmupComplete.store(false, std::memory_order_release);
    try { x.schedThread = std::thread(SchedThread); x.schedRunning = true; }
    catch (...) { x.schedRunning = false; LogError("executor: fs-sched thread could not be started"); }
    try { x.warmupThread = std::thread(WarmupThread); x.warmupRunning = true; }
    catch (...) { x.warmupRunning = false; LogError("executor: fs-warmup thread could not be started; FS_HEVT_WARMUP_STEP drives warm-up"); }
}

// Full teardown (any thread allowed to wait for the device: Unity device shutdown/reset or host shutdown).
void VkTeardown(Executor& x) {
    if (!x.vkReady.load(std::memory_order_acquire)) return;
    x.schedStop.store(true, std::memory_order_release);
    x.cv.notify_all();
    if (x.schedRunning) { x.schedThread.join(); x.schedRunning = false; }
    if (x.warmupRunning) { x.warmupThread.join(); x.warmupRunning = false; }
    std::vector<std::function<void(bool)>> hooks;
    { std::lock_guard<std::mutex> lock(x.hooksMutex); hooks = x.deviceHooks; }
    { std::lock_guard<std::mutex> lock(x.mutex); x.core.SetAcceptingSubmits(false); }
    vkDeviceWaitIdle(x.device);                       // the only idle wait (contract §15.3, ledger invariant 9)
    SavePipelineCache(x);
    for (auto& h : hooks) if (h) h(false);            // modules release their pipelines/buffers (deferred into garbage)
    std::vector<std::pair<std::function<void(bool, uint64_t, uint64_t)>, std::string>> failed;
    {
        std::lock_guard<std::mutex> lock(x.mutex);
        std::vector<std::pair<FsJobClass, sched::SlotState>> dropped;
        x.core.FailAllInFlight(dropped);
        for (ClassVk& cv : x.classes) for (SlotVk& s : cv.slots) if (s.onRetired) { failed.emplace_back(std::move(s.onRetired), s.name); s.onRetired = nullptr; }
        for (auto& pf : x.pendingFailed) failed.push_back(std::move(pf));
        x.pendingFailed.clear();
        CollectGarbageLocked(x, true);
        ResourcesShutdown(x);
        DestroyRingsLocked(x);
        x.vkReady.store(false, std::memory_order_release);
        x.device = VK_NULL_HANDLE; x.scannerQueue = VK_NULL_HANDLE;
        x.frameMode.store(FS_FRAME_MODE_NONE);
    }
    for (auto& f : failed) if (f.first) f.first(false, 0, 0);
    Log("executor: vulkan objects destroyed (%zu jobs failed at teardown)", failed.size());
}

// ---- frame events (render thread) ---------------------------------------------------------------------
void RecordBarrierAfterHooks(VkCommandBuffer cb) {
    VkMemoryBarrier mb = {}; mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
    mb.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
    mb.dstAccessMask = VK_ACCESS_INDIRECT_COMMAND_READ_BIT | VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT | VK_ACCESS_UNIFORM_READ_BIT;
    vkCmdPipelineBarrier(cb, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
        VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT | VK_PIPELINE_STAGE_VERTEX_SHADER_BIT | VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
        0, 1, &mb, 0, nullptr, 0, nullptr);
}

// AccessQueue callback (graphics queue access allowed). data = FrameSlot* for scanner mode, nullptr for the
// class-timeline acquire and the fallback pump. Runs synchronously or on Unity's submission thread.
void UNITY_INTERFACE_API QueueAccessCallback(int eventId, void* data) {
    (void)eventId;
    Executor& x = X();
    DeviceState& d = Device();
    std::lock_guard<std::mutex> lock(x.mutex);
    if (!x.vkReady.load(std::memory_order_acquire) || !x.core.AcceptingSubmits()) return;
    VkQueue gq = d.instance.graphicsQueue;
    VkResult r = VK_SUCCESS;
    FrameSlot* fs = static_cast<FrameSlot*>(data);

    // (a) class timelines that advanced since the last acquire: order later graphics work after them
    //     (device-side visibility of scanner-queue writes to the Unity draw; the values are fence-proven retired).
    std::vector<VkSemaphore> waitSems; std::vector<uint64_t> waitVals; std::vector<VkPipelineStageFlags> waitStages;
    if (x.timelineOk) {
        for (uint32_t c = 0; c < sched::kClassCount; ++c) {
            const uint64_t v = x.core.Timeline(static_cast<FsJobClass>(c)).LastRetired();
            if (v > x.lastAcquiredRetired[c] && x.classes[c].timeline) { waitSems.push_back(x.classes[c].timeline); waitVals.push_back(v); waitStages.push_back(VK_PIPELINE_STAGE_ALL_COMMANDS_BIT); x.lastAcquiredRetired[c] = v; }
        }
    }
    if (fs != nullptr && fs->inFlight) {
        const VkPipelineStageFlags dst = VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT | VK_PIPELINE_STAGE_VERTEX_SHADER_BIT | VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
        if (x.scannerQueue != VK_NULL_HANDLE) {
            // (1) graphics "ready"
            VkTimelineSemaphoreSubmitInfo tsi = {}; tsi.sType = VK_STRUCTURE_TYPE_TIMELINE_SEMAPHORE_SUBMIT_INFO;
            VkSubmitInfo s1 = {}; s1.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
            VkSemaphore readySem = x.timelineOk ? x.frameTimeline : fs->ready;
            s1.signalSemaphoreCount = 1; s1.pSignalSemaphores = &readySem;
            if (x.timelineOk) { tsi.signalSemaphoreValueCount = 1; tsi.pSignalSemaphoreValues = &fs->readyValue; s1.pNext = &tsi; }
            r = vkQueueSubmit(gq, 1, &s1, VK_NULL_HANDLE);
            if (r == VK_SUCCESS) {
                // (2) scanner: frame command buffer waits "ready", signals "done"
                VkTimelineSemaphoreSubmitInfo ts2 = {}; ts2.sType = VK_STRUCTURE_TYPE_TIMELINE_SEMAPHORE_SUBMIT_INFO;
                VkSubmitInfo s2 = {}; s2.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
                VkSemaphore doneSem = x.timelineOk ? x.frameTimeline : fs->done;
                VkPipelineStageFlags waitStage = VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
                s2.waitSemaphoreCount = 1; s2.pWaitSemaphores = &readySem; s2.pWaitDstStageMask = &waitStage;
                s2.commandBufferCount = 1; s2.pCommandBuffers = &fs->cb;
                s2.signalSemaphoreCount = 1; s2.pSignalSemaphores = &doneSem;
                if (x.timelineOk) { ts2.waitSemaphoreValueCount = 1; ts2.pWaitSemaphoreValues = &fs->readyValue; ts2.signalSemaphoreValueCount = 1; ts2.pSignalSemaphoreValues = &fs->doneValue; s2.pNext = &ts2; }
                r = SubmitScanner(x, s2, fs->fence);
                if (r == VK_SUCCESS) { waitSems.push_back(doneSem); waitVals.push_back(fs->doneValue); waitStages.push_back(dst); }
            }
        } else {
            // graphics-queue fallback: the frame command buffer runs on the graphics queue in submission order
            VkSubmitInfo s2 = {}; s2.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO; s2.commandBufferCount = 1; s2.pCommandBuffers = &fs->cb;
            r = vkQueueSubmit(gq, 1, &s2, fs->fence);
        }
        if (r != VK_SUCCESS) { fs->inFlight = false; vkResetFences(x.device, 1, &fs->fence); }
    }
    // (b) fallback pump: recorded jobs, in submission order
    if (r == VK_SUCCESS && x.queueFallback) {
        std::vector<std::pair<uint64_t, std::pair<uint32_t, uint32_t>>> pending;   // epoch, (class, slot)
        for (uint32_t c = 0; c < sched::kClassCount; ++c) {
            sched::ClassRing& ring = x.core.Ring(static_cast<FsJobClass>(c));
            for (uint32_t i = 0; i < ring.Capacity(); ++i) if (ring.Slot(i).inFlight && x.classes[c].slots[i].recorded && !x.classes[c].slots[i].submitted) pending.push_back({ring.Slot(i).submitEpoch, {c, i}});
        }
        std::sort(pending.begin(), pending.end());
        for (auto& p : pending) {
            SlotVk& sv = x.classes[p.second.first].slots[p.second.second];
            VkSubmitInfo si = {}; si.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO; si.commandBufferCount = 1; si.pCommandBuffers = &sv.cb;
            r = vkQueueSubmit(gq, 1, &si, sv.fence);
            if (r != VK_SUCCESS) break;
            sv.submitted = true; ++x.fallbackPumpedJobs;
        }
        ++x.fallbackPumps;
    }
    // (3) graphics waits: class timelines + frame "done"
    if (r == VK_SUCCESS && !waitSems.empty()) {
        VkTimelineSemaphoreSubmitInfo tsi = {}; tsi.sType = VK_STRUCTURE_TYPE_TIMELINE_SEMAPHORE_SUBMIT_INFO;
        tsi.waitSemaphoreValueCount = static_cast<uint32_t>(waitVals.size()); tsi.pWaitSemaphoreValues = waitVals.data();
        VkSubmitInfo s3 = {}; s3.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO; s3.pNext = x.timelineOk ? &tsi : nullptr;
        s3.waitSemaphoreCount = static_cast<uint32_t>(waitSems.size()); s3.pWaitSemaphores = waitSems.data(); s3.pWaitDstStageMask = waitStages.data();
        r = vkQueueSubmit(gq, 1, &s3, VK_NULL_HANDLE);
        if (r == VK_SUCCESS) ++x.acquireSubmits;
    }
    if (r == VK_ERROR_DEVICE_LOST) Quarantine(x, "vkQueueSubmit(graphics access)", r);
    else if (r != VK_SUCCESS) { Tele().Error((std::string("queue access submit ") + VkResultName(r)).c_str()); LogError("executor: queue access submit %s", VkResultName(r)); }
    x.cv.notify_all();
}

void FrameBegin(Executor& x) {
    x.inRenderEvent.store(true, std::memory_order_release);
    x.frameBeginSeen = true;
    const uint32_t frame = x.frameIndex.fetch_add(1, std::memory_order_acq_rel) + 1;
    UnityVulkanRecordingState rs = {};
    const bool haveRs = RecordingState(&rs, kUnityVulkanGraphicsQueueAccess_DontCare);
    if (haveRs) { x.haveRecordingState = true; x.unityCurrentFrame.store(rs.currentFrameNumber); x.unitySafeFrame.store(rs.safeFrameNumber); }
    std::vector<FrameHook> hooks;
    { std::lock_guard<std::mutex> lock(x.hooksMutex); hooks = x.frameBeginHooks; }
    x.frameHookCount = static_cast<uint32_t>(hooks.size());
    ++x.frameBegins;
    bool accepting = false, needAcquire = false;
    {
        std::lock_guard<std::mutex> lock(x.mutex);
        accepting = x.core.AcceptingSubmits();
        if (x.timelineOk) for (uint32_t c = 0; c < sched::kClassCount; ++c) if (x.core.Timeline(static_cast<FsJobClass>(c)).LastRetired() > x.lastAcquiredRetired[c]) needAcquire = true;
    }
    int32_t mode = FS_FRAME_MODE_SKIPPED;
    FrameSlot* frameSlot = nullptr;
    if (!haveRs || rs.commandBuffer == VK_NULL_HANDLE || !accepting) mode = hooks.empty() ? FS_FRAME_MODE_NONE : FS_FRAME_MODE_SKIPPED;
    else if (hooks.empty()) mode = FS_FRAME_MODE_NONE;
    else if (rs.renderPass == VK_NULL_HANDLE) mode = FS_FRAME_MODE_UNITY_CMD;
    else {
        std::lock_guard<std::mutex> lock(x.mutex);
        FrameSlot& fs = x.frameSlots[x.frameSlotNext % kFrameRingSize];
        if (fs.inFlight) {   // ring full: never wait (the fence retires on fs-sched)
            if (vkGetFenceStatus(x.device, fs.fence) == VK_SUCCESS) { fs.inFlight = false; vkResetFences(x.device, 1, &fs.fence); }
        }
        if (!fs.inFlight) { frameSlot = &fs; x.frameSlotNext = (x.frameSlotNext + 1) % kFrameRingSize; mode = FS_FRAME_MODE_SCANNER; }
        else { mode = FS_FRAME_MODE_SKIPPED; ++x.frameSkippedRing; }
    }
    x.frameMode.store(mode, std::memory_order_release);
    x.frameLastMode = mode; ++x.modeCounts[mode & 3];
    if (mode == FS_FRAME_MODE_UNITY_CMD) {
        if (!x.loggedModeUnity) { x.loggedModeUnity = true; Log("frame-begin mode: UNITY_CMD (Unity command buffer outside render pass; subPassIndex=%d)", rs.subPassIndex); }
        x.frameCb = rs.commandBuffer;
        for (auto& h : hooks) if (h) h(x.frameCb, frame);
        RecordBarrierAfterHooks(x.frameCb);
    } else if (mode == FS_FRAME_MODE_SCANNER) {
        if (!x.loggedModeScanner) { x.loggedModeScanner = true; Log("frame-begin mode: SCANNER (Unity inside render pass %p subPass=%d despite EnsureOutside); executor command buffer + AccessQueue(flush)", static_cast<void*>(rs.renderPass), rs.subPassIndex); }
        VkCommandBufferBeginInfo bi = {}; bi.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO; bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        if (vkBeginCommandBuffer(frameSlot->cb, &bi) == VK_SUCCESS) {
            x.frameCb = frameSlot->cb;
            for (auto& h : hooks) if (h) h(x.frameCb, frame);
            RecordBarrierAfterHooks(x.frameCb);
            if (vkEndCommandBuffer(frameSlot->cb) == VK_SUCCESS) {
                std::lock_guard<std::mutex> lock(x.mutex);
                frameSlot->inFlight = true; frameSlot->frameIndex = frame;
                frameSlot->readyValue = ++x.frameTimelineValue; frameSlot->doneValue = ++x.frameTimelineValue;
            } else frameSlot = nullptr;
        } else frameSlot = nullptr;
        if (frameSlot) AccessQueue(QueueAccessCallback, frameSlot, true);   // flush: Unity's barriers for imported resources go first
        else { x.frameMode.store(FS_FRAME_MODE_SKIPPED); x.frameLastMode = FS_FRAME_MODE_SKIPPED; }
    } else if (mode == FS_FRAME_MODE_SKIPPED && !x.loggedModeSkipped) {
        x.loggedModeSkipped = true;
        Log("frame-begin mode: SKIPPED (recordingState=%d cb=%p accepting=%d)", haveRs, haveRs ? static_cast<void*>(rs.commandBuffer) : nullptr, accepting);
    }
    // Class-timeline acquire (scanner-queue writes -> this frame's Unity draw); SCANNER mode did it in its own callback.
    if (needAcquire && accepting && mode != FS_FRAME_MODE_SCANNER) AccessQueue(QueueAccessCallback, nullptr, false);
    x.frameCb = VK_NULL_HANDLE;
    x.inRenderEvent.store(false, std::memory_order_release);
}

void CollectFrameMarks(Executor& x) {
    while (x.markRead < x.markWrite) {
        const uint32_t n = std::min<uint32_t>(32u, x.markWrite - x.markRead);
        uint64_t data[32 * 4] = {};
        const uint32_t first = (x.markRead % kFrameMarkRing);
        const uint32_t contiguous = std::min<uint32_t>(n, kFrameMarkRing - first);
        VkResult r = vkGetQueryPoolResults(x.device, x.frameMarkPool, first * 2, contiguous * 2, sizeof(uint64_t) * contiguous * 4, data, 2 * sizeof(uint64_t),
            VK_QUERY_RESULT_64_BIT | VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);
        if (r != VK_SUCCESS && r != VK_NOT_READY) { if (r == VK_ERROR_DEVICE_LOST) { std::lock_guard<std::mutex> lock(x.mutex); Quarantine(x, "vkGetQueryPoolResults(frame marks)", r); } return; }
        uint32_t consumed = 0;
        for (uint32_t i = 0; i < contiguous; ++i) {
            if (data[i * 4 + 1] == 0 || data[i * 4 + 3] == 0) break;
            const auto& cpu = x.markCpu[(x.markRead + i) % kFrameMarkRing];
            Tele().FrameMark(TicksToNs(data[i * 4]), TicksToNs(data[i * 4 + 2]), cpu.first, cpu.second);
            ++consumed;
        }
        x.markRead += consumed; x.frameMarksCollected += consumed;
        if (consumed < contiguous) return;
    }
}

void FrameEnd(Executor& x) {
    x.inRenderEvent.store(true, std::memory_order_release);
    ++x.frameEnds;
    if (!x.frameBeginSeen) x.frameIndex.fetch_add(1, std::memory_order_acq_rel);
    x.frameBeginSeen = false;
    UnityVulkanRecordingState rs = {};
    bool accepting = false;
    { std::lock_guard<std::mutex> lock(x.mutex); accepting = x.core.AcceptingSubmits(); }
    if (accepting && x.frameMarkPool != VK_NULL_HANDLE && RecordingState(&rs, kUnityVulkanGraphicsQueueAccess_DontCare) && rs.commandBuffer != VK_NULL_HANDLE) {
        x.haveRecordingState = true; x.unityCurrentFrame.store(rs.currentFrameNumber); x.unitySafeFrame.store(rs.safeFrameNumber);
        if (x.markWrite - x.markRead < kFrameMarkRing) {
            if (!x.markPoolReset) {
                if (Device().hostQueryResetEnabled && x.resetQueryPool) x.resetQueryPool(x.device, x.frameMarkPool, 0, kFrameMarkRing * 2);
                else vkCmdResetQueryPool(rs.commandBuffer, x.frameMarkPool, 0, kFrameMarkRing * 2);
                x.markPoolReset = true;
            }
            const uint32_t idx = x.markWrite % kFrameMarkRing;
            vkCmdResetQueryPool(rs.commandBuffer, x.frameMarkPool, idx * 2, 2);   // event is EnsureOutsideRenderPass
            vkCmdWriteTimestamp(rs.commandBuffer, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, x.frameMarkPool, idx * 2);
            vkCmdWriteTimestamp(rs.commandBuffer, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, x.frameMarkPool, idx * 2 + 1);
            x.markCpu[idx] = { MonotonicNs(), rs.currentFrameNumber };
            ++x.markWrite;
        }
        CollectFrameMarks(x);
    }
    // graphics-queue fallback: submit jobs recorded since the last pump
    bool pump = false;
    {
        std::lock_guard<std::mutex> lock(x.mutex);
        if (x.queueFallback && accepting)
            for (uint32_t c = 0; c < sched::kClassCount && !pump; ++c)
                for (SlotVk& s : x.classes[c].slots) if (s.recorded && !s.submitted) { pump = true; break; }
        ++x.frameEndSeq;
    }
    if (pump) AccessQueue(QueueAccessCallback, nullptr, false);
    x.cv.notify_all();
    x.inRenderEvent.store(false, std::memory_order_release);
}

} // namespace

// ---- shared helpers (executor_internal.h) ------------------------------------------------------------
void Quarantine(Executor& x, const char* reason, VkResult r) {
    if (!x.core.AcceptingSubmits() && (x.status.load() == FS_HOST_QUARANTINED || x.status.load() == FS_HOST_DEVICE_LOST)) return;
    x.quarantineReason = std::string(reason ? reason : "") + " " + VkResultName(r);
    x.quarantineResult = static_cast<int32_t>(r);
    x.status.store(r == VK_ERROR_DEVICE_LOST ? FS_HOST_DEVICE_LOST : FS_HOST_QUARANTINED, std::memory_order_release);
    std::vector<std::pair<FsJobClass, sched::SlotState>> dropped;
    x.core.FailAllInFlight(dropped);                   // releases every lease-holding slot; accepting = false
    for (auto& d : dropped) {
        ClassVk& cv = x.classes[d.first];
        for (SlotVk& s : cv.slots) if (s.onRetired && (s.submitted || s.recorded)) { x.pendingFailed.emplace_back(std::move(s.onRetired), s.name); s.onRetired = nullptr; s.submitted = s.recorded = false; }
    }
    for (FrameSlot& fs : x.frameSlots) fs.inFlight = false;   // fences may never signal; rebuilt on device re-init
    Tele().Error(x.quarantineReason.c_str());
    LogError("executor QUARANTINED: %s (%zu jobs failed, no further submits; rebuild on device re-init)", x.quarantineReason.c_str(), dropped.size());
    x.cv.notify_all();
}

uint64_t PushGarbageLocked(Executor& x, const GarbageItem& item) {
    const uint64_t token = x.nextGarbageToken++;
    x.garbageItems[token] = item;
    x.garbage.Push(token, x.core.SubmitEpoch(), x.unityCurrentFrame.load(std::memory_order_relaxed));
    return token;
}

void CollectGarbageLocked(Executor& x, bool everything) {
    std::vector<uint64_t> freed;
    if (everything) x.garbage.TakeAll(freed);
    else {
        const uint64_t safe = x.haveRecordingState ? x.unitySafeFrame.load(std::memory_order_relaxed) : UINT64_MAX;
        // an item may be freed once every job submitted before its destruction retired and Unity's safe frame passed
        x.garbage.Collect(x.core.MinInFlightEpoch(), safe, freed);
    }
    for (uint64_t t : freed) {
        auto it = x.garbageItems.find(t);
        if (it == x.garbageItems.end()) continue;
        DestroyGarbageItem(x, it->second);
        x.garbageItems.erase(it);
    }
}

void RefreshRecordingState(Executor& x) {
    if (!x.inRenderEvent.load(std::memory_order_acquire)) return;
    UnityVulkanRecordingState rs = {};
    if (!RecordingState(&rs, kUnityVulkanGraphicsQueueAccess_DontCare)) return;
    x.haveRecordingState = true; x.unityCurrentFrame.store(rs.currentFrameNumber); x.unitySafeFrame.store(rs.safeFrameNumber);
    if (x.frameMode.load(std::memory_order_acquire) == FS_FRAME_MODE_UNITY_CMD && rs.commandBuffer != VK_NULL_HANDLE) x.frameCb = rs.commandBuffer;
}

// ---- host-level operations (host_api.cpp) -------------------------------------------------------------
bool HostInit(const FsHostConfig* cfg) {
    Executor& x = X();
    bool teardownFirst = false;
    {
        std::lock_guard<std::mutex> lock(x.mutex);
        if (x.hostInitialized && !x.pendingTeardown) return false;
        if (x.pendingTeardown) teardownFirst = true;
    }
    if (teardownFirst) { Log("executor: re-init after shutdown; tearing down previous objects on the calling thread"); VkTeardown(x); }
    bool createNow = false;
    {
        std::lock_guard<std::mutex> lock(x.mutex);
        x.cfg = FsHostConfig{};
        if (cfg) std::memcpy(&x.cfg, cfg, std::min<size_t>(sizeof(FsHostConfig), cfg->structSize ? cfg->structSize : sizeof(FsHostConfig)));
        x.cfg.structSize = sizeof(FsHostConfig);
        x.hostInitialized = true; x.pendingTeardown = false;
        x.status.store(FS_HOST_WARMING_UP, std::memory_order_release);
        x.frameIndex.store(0);
        createNow = Device().ready.load(std::memory_order_acquire) && !x.vkReady.load(std::memory_order_acquire);
        if (createNow && !VkInitLocked(x)) { LogError("executor: vulkan init failed at FsHost_Init"); x.status.store(FS_HOST_QUARANTINED); x.quarantineReason = "vulkan init failed"; createNow = false; }
    }
    if (createNow) {
        StartThreads(x);
        std::vector<std::function<void(bool)>> hooks;
        { std::lock_guard<std::mutex> lock(x.hooksMutex); hooks = x.deviceHooks; }
        for (auto& h : hooks) if (h) h(true);
    }
    Log("FsHost_Init: status=%s (device %s) budgetUs=%u", StatusName(x.status.load()), createNow ? "ready" : "pending", x.cfg.frameBudgetUs);
    return true;
}

void HostShutdown() {
    Executor& x = X();
    {
        std::lock_guard<std::mutex> lock(x.mutex);
        if (!x.hostInitialized) return;
        x.hostInitialized = false;
        x.status.store(FS_HOST_SHUTDOWN, std::memory_order_release);
        x.core.SetAcceptingSubmits(false);
        x.pendingTeardown = x.vkReady.load(std::memory_order_acquire);
    }
    x.cv.notify_all();
    Log("FsHost_Shutdown: status=SHUTDOWN%s", x.pendingTeardown ? " (Vulkan teardown deferred to the render thread)" : "");
}

void HostSetStorageRoot(const char* path) {
    Executor& x = X();
    std::lock_guard<std::mutex> lock(x.storageMutex);
    x.storageRoot = path ? path : "";
    while (!x.storageRoot.empty() && x.storageRoot.back() == '/') x.storageRoot.pop_back();
}

void HostSetFrameBudget(uint32_t us) {
    Executor& x = X();
    std::lock_guard<std::mutex> lock(x.mutex);
    x.cfg.frameBudgetUs = us;
    x.core.SetFrameBudgetUs(us ? us : sched::kDefaultFrameBudgetUs);
}

void HostSetFrameHint(double t, const float pos[3], const float vel[3], const float rot[4]) {
    Executor& x = X();
    std::lock_guard<std::mutex> lock(x.mutex);
    x.hint.predictedDisplayTimeSec = t;
    for (int i = 0; i < 3; ++i) { x.hint.headPos[i] = pos ? pos[i] : 0.f; x.hint.headVel[i] = vel ? vel[i] : 0.f; }
    for (int i = 0; i < 4; ++i) x.hint.headRot[i] = rot ? rot[i] : (i == 3 ? 1.f : 0.f);
    ++x.hint.sequence; x.hintValid = true;
}

bool HostGetClassStats(int32_t cls, int64_t out[8]) {
    Executor& x = X();
    std::lock_guard<std::mutex> lock(x.mutex);
    return x.core.GetClassStats(cls, out);
}

void HostRequestScan(uint32_t observationId) {
    Executor& x = X();
    { std::lock_guard<std::mutex> lock(x.mutex); x.scanRequests.Request(observationId); }
    x.cv.notify_all();
}

std::string HostTelemetryJson(bool includeStages) {
    Executor& x = X();
    JsonWriter w;
    std::string members;
    {
        std::lock_guard<std::mutex> lock(x.mutex);
        w.BeginObject();
        w.KV("status", x.status.load()); w.KV("statusName", StatusName(x.status.load()));
        w.KV("vkReady", x.vkReady.load()); w.KV("hostInitialized", x.hostInitialized);
        w.KV("scannerQueue", !x.queueFallback && x.vkReady.load()); w.KV("queueFallback", x.queueFallback); w.KV("timelineSemaphores", x.timelineOk); w.KV("bufferDeviceAddress", x.bdaEnabled);
        w.KV("storageDir", StorageDir());
        w.KV("quarantineReason", x.quarantineReason); w.KV("quarantineResult", x.quarantineResult);
        w.Key("frame"); w.BeginObject();
        w.KV("index", x.frameIndex.load()); w.KV("begins", x.frameBegins); w.KV("ends", x.frameEnds); w.KV("lastMode", x.frameLastMode);
        w.KV("modeNone", x.modeCounts[0]); w.KV("modeUnityCmd", x.modeCounts[1]); w.KV("modeScanner", x.modeCounts[2]); w.KV("modeSkipped", x.modeCounts[3]);
        w.KV("skippedRingFull", x.frameSkippedRing); w.KV("hooks", x.frameHookCount); w.KV("marksCollected", x.frameMarksCollected);
        w.KV("unityFrame", x.unityCurrentFrame.load()); w.KV("unitySafeFrame", x.unitySafeFrame.load());
        w.KV("acquireSubmits", x.acquireSubmits); w.KV("fallbackPumps", x.fallbackPumps); w.KV("fallbackPumpedJobs", x.fallbackPumpedJobs);
        w.EndObject();
        w.Key("sched"); w.BeginObject();
        w.KV("frameBudgetUs", x.core.Budget().FrameBudget()); w.KV("usedUs", x.core.Budget().Used()); w.KV("remainingUs", x.core.Budget().Remaining());
        w.KV("frames", x.core.Budget().Frames()); w.KV("submitEpoch", x.core.SubmitEpoch()); w.KV("accepting", x.core.AcceptingSubmits()); w.KV("anyInFlight", x.core.AnyInFlight());
        w.KV("scanRequested", x.scanRequests.Requested()); w.KV("scanSkipped", x.scanRequests.Skipped());
        w.KV("hintValid", x.hintValid); w.KV("hintSequence", x.hint.sequence);
        w.EndObject();
        w.Key("classes"); w.BeginArray();
        for (uint32_t c = 0; c < sched::kClassCount; ++c) {
            const sched::ClassStats& s = x.core.Stats(static_cast<FsJobClass>(c));
            w.BeginObject();
            w.KV("name", sched::ClassName(c)); w.KV("submitted", s.submitted); w.KV("retired", s.retired); w.KV("failed", s.failed);
            w.KV("deferredRing", s.deferredRing); w.KV("deferredBudget", s.deferredBudget); w.KV("deferredDependency", s.deferredDependency); w.KV("droppedLease", s.droppedLease);
            w.KV("gpuUsTotal", s.gpuUsTotal); w.KV("gpuUsLast", s.gpuUsLast);
            w.KV("inFlight", x.core.Ring(static_cast<FsJobClass>(c)).InFlight()); w.KV("capacity", x.core.Ring(static_cast<FsJobClass>(c)).Capacity());
            w.KV("quantumUs", x.core.Config().quantumUs[c]);
            w.KV("timelineAllocated", x.core.Timeline(static_cast<FsJobClass>(c)).LastAllocated()); w.KV("timelineRetired", x.core.Timeline(static_cast<FsJobClass>(c)).LastRetired());
            w.EndObject();
        }
        w.EndArray();
        std::string res; ResourcesJson(x, res);
        w.RawValue("resources", res.substr(std::strlen("\"resources\":")));
    }
    {
        std::lock_guard<std::mutex> lock(x.hooksMutex);
        w.Key("warmup"); w.BeginObject();
        w.KV("complete", x.warmupComplete.load()); w.KV("steps", static_cast<uint64_t>(x.warmup.size())); w.KV("failed", x.warmupFailed); w.KV("totalMs", x.warmupTotalMs, 4);
        w.KV("generation", x.warmupGeneration);
        w.Key("list"); w.BeginArray();
        for (const WarmupStep& s : x.warmup) { w.BeginObject(); w.KV("name", s.name); w.KV("done", s.done); w.KV("failed", s.failed); w.KV("ms", s.ms, 4); w.EndObject(); }
        w.EndArray();
        w.EndObject();
        w.KV("frameBeginHooks", static_cast<uint64_t>(x.frameBeginHooks.size()));
        uint64_t ticks = x.genericTicks.size(); for (uint32_t c = 0; c < sched::kClassCount; ++c) ticks += x.classTicks[c].size();
        w.KV("schedulerTicks", ticks); w.KV("deviceHooks", static_cast<uint64_t>(x.deviceHooks.size()));
        w.EndObject();
    }
    const std::string host = w.Take();
    return Tele().Json(includeStages, host.substr(1, host.size() - 2));
}

} // namespace exec

// ---- public executor API (fs_executor.h) --------------------------------------------------------------
using exec::Executor; using exec::X;

bool     ExecReady() { return X().vkReady.load(std::memory_order_acquire); }
VkDevice ExecDevice() { return X().device; }
uint32_t ExecQueueFamily() { return X().family; }
bool     ExecHasScannerQueue() { Executor& x = X(); return x.vkReady.load(std::memory_order_acquire) && !x.queueFallback; }
bool     ExecTimelineSemaphores() { return X().timelineOk; }
int32_t  ExecStatus() { return X().status.load(std::memory_order_acquire); }
uint32_t ExecFrameBudgetUs() { Executor& x = X(); std::lock_guard<std::mutex> lock(x.mutex); return x.core.Config().frameBudgetUs; }
void     ExecQuarantine(const char* reason, VkResult result) { Executor& x = X(); std::lock_guard<std::mutex> lock(x.mutex); exec::Quarantine(x, reason, result); }
std::string StorageDir() { Executor& x = X(); std::lock_guard<std::mutex> lock(x.storageMutex); return x.storageRoot.empty() ? std::string() : x.storageRoot + "/finalscan"; }

uint64_t ClassTimelineValue(FsJobClass cls) {
    Executor& x = X();
    if (cls < 0 || static_cast<uint32_t>(cls) >= sched::kClassCount) return 0;
    std::lock_guard<std::mutex> lock(x.mutex);
    return x.core.Timeline(cls).LastRetired();
}
uint64_t ClassTimelineAllocated(FsJobClass cls) {
    Executor& x = X();
    if (cls < 0 || static_cast<uint32_t>(cls) >= sched::kClassCount) return 0;
    std::lock_guard<std::mutex> lock(x.mutex);
    return x.core.Timeline(cls).LastAllocated();
}
bool JobTimelineValue(uint64_t jobId, FsJobClass& cls, uint64_t& value) {
    Executor& x = X();
    std::lock_guard<std::mutex> lock(x.mutex);
    for (uint32_t c = 0; c < sched::kClassCount; ++c) {
        const sched::ClassRing& ring = x.core.Ring(static_cast<FsJobClass>(c));
        for (uint32_t i = 0; i < ring.Capacity(); ++i)
            if (ring.Slot(i).inFlight && ring.Slot(i).jobId == jobId) { cls = static_cast<FsJobClass>(c); value = ring.Slot(i).timelineValue; return true; }
    }
    return false;
}

void     CounterAdd(FsCounter c, int64_t v) { Tele().CounterAdd(c, v); }
int64_t  CounterGet(FsCounter c) { return Tele().CounterGet(c); }
void     TelemetryStage(FsJobClass cls, const char* stage, uint64_t gpuStartNs, uint64_t gpuEndNs) { Tele().Stage(cls, stage, gpuStartNs, gpuEndNs, X().frameIndex.load(std::memory_order_relaxed)); }
uint64_t GpuNowEstimateNs() { const double g = Tele().GpuNowEstimateNs(MonotonicNs()); return g > 0.0 ? static_cast<uint64_t>(g) : 0ull; }
uint32_t FrameIndex() { return X().frameIndex.load(std::memory_order_relaxed); }
FrameMode CurrentFrameMode() { return static_cast<FrameMode>(X().frameMode.load(std::memory_order_acquire)); }
VkCommandBuffer FrameCommandBuffer() { return X().frameCb; }
bool     InRenderEvent() { return X().inRenderEvent.load(std::memory_order_acquire); }
uint64_t UnitySafeFrameNumber() { return X().unitySafeFrame.load(std::memory_order_relaxed); }
uint64_t UnityCurrentFrameNumber() { return X().unityCurrentFrame.load(std::memory_order_relaxed); }

void RegisterFrameBeginHook(FrameHook hook) { Executor& x = X(); std::lock_guard<std::mutex> lock(x.hooksMutex); x.frameBeginHooks.push_back(std::move(hook)); }
void RegisterSchedulerTick(std::function<void(uint32_t)> tick) { Executor& x = X(); std::lock_guard<std::mutex> lock(x.hooksMutex); x.genericTicks.push_back(std::move(tick)); }
void RegisterClassSchedulerTick(FsJobClass cls, std::function<void(uint32_t)> tick) {
    Executor& x = X();
    if (cls < 0 || static_cast<uint32_t>(cls) >= sched::kClassCount) return;
    std::lock_guard<std::mutex> lock(x.hooksMutex); x.classTicks[cls].push_back(std::move(tick));
}
void RegisterLeaseValidator(LeaseValidator validator) { Executor& x = X(); std::lock_guard<std::mutex> lock(x.mutex); x.leaseValidator = std::move(validator); }
void RegisterDeviceHook(std::function<void(bool)> hook) {
    Executor& x = X();
    { std::lock_guard<std::mutex> lock(x.hooksMutex); x.deviceHooks.push_back(hook); }
    if (x.vkReady.load(std::memory_order_acquire) && hook) hook(true);
}
void RegisterWarmupStep(const char* name, std::function<bool()> step) {
    Executor& x = X();
    bool runNow = false;
    {
        std::lock_guard<std::mutex> lock(x.hooksMutex);
        exec::WarmupStep s; s.name = name ? name : ""; s.step = step;
        runNow = x.warmupComplete.load(std::memory_order_acquire) && x.vkReady.load(std::memory_order_acquire);
        if (runNow) { s.done = true; }
        x.warmup.push_back(s);
    }
    if (runNow) {
        const int64_t t0 = MonotonicNs();
        const bool ok = step ? step() : false;
        const double ms = static_cast<double>(MonotonicNs() - t0) / 1e6;
        std::lock_guard<std::mutex> lock(x.hooksMutex);
        x.warmup.back().failed = !ok; x.warmup.back().ms = ms; x.warmupTotalMs += ms; if (!ok) ++x.warmupFailed;
        Log("late warm-up step %s: %s in %.2f ms (registered after warm-up completed)", name ? name : "", ok ? "ok" : "FAILED", ms);
    }
}

bool TakeScanRequest(uint32_t& observationId) { Executor& x = X(); std::lock_guard<std::mutex> lock(x.mutex); return x.scanRequests.Take(observationId); }
bool GetFrameHint(FrameHint& out) { Executor& x = X(); std::lock_guard<std::mutex> lock(x.mutex); if (!x.hintValid) return false; out = x.hint; return true; }

uint64_t SubmitJob(const JobDesc& desc) {
    Executor& x = X();
    if (desc.cls < 0 || static_cast<uint32_t>(desc.cls) >= sched::kClassCount) return 0;
    if (desc.dispatchCount == 0 || desc.dispatchCount > sched::kMaxDispatchesPerJob || desc.dispatches == nullptr) { LogError("SubmitJob(%s): %u dispatches (1..%u)", desc.name ? desc.name : "", desc.dispatchCount, sched::kMaxDispatchesPerJob); return 0; }
    for (uint32_t i = 0; i < desc.dispatchCount; ++i) {
        const Dispatch& dp = desc.dispatches[i];
        if (dp.pipeline == nullptr || dp.pipeline->pipeline == VK_NULL_HANDLE || dp.pipeline->layout == VK_NULL_HANDLE) { LogError("SubmitJob(%s): dispatch %u has no pipeline", desc.name ? desc.name : "", i); return 0; }
        if (dp.pushBytes > dp.pipeline->pushBytes || dp.pushBytes > exec::kMaxPushBytes) { LogError("SubmitJob(%s): dispatch %u push %u > %u", desc.name ? desc.name : "", i, dp.pushBytes, dp.pipeline->pushBytes); return 0; }
        if (dp.argsBuffer != nullptr && dp.argsBuffer->buffer == VK_NULL_HANDLE) return 0;
    }
    std::lock_guard<std::mutex> lock(x.mutex);
    if (!x.vkReady.load(std::memory_order_acquire) || !x.core.AcceptingSubmits()) return 0;
    const sched::SubmitDecision d = x.core.TrySubmit(desc.cls, desc.maxQuantumUs, desc.leaseSlot, desc.leaseGeneration, &x.leaseValidator, MonotonicNs(),
                                                     desc.waitClass, desc.waitTimelineValue);
    if (d.outcome != sched::SubmitOutcome::Accepted) {
        if (d.outcome == sched::SubmitOutcome::DeferredRing || d.outcome == sched::SubmitOutcome::DeferredBudget || d.outcome == sched::SubmitOutcome::DeferredDependency)
            Tele().CounterAdd(FS_CTR_DEFERRED_PUBLISH + static_cast<int32_t>(desc.cls), 1);
        return 0;
    }
    exec::ClassVk& cv = x.classes[desc.cls];
    exec::SlotVk& sv = cv.slots[d.slot];
    // ---- record
    VkCommandBufferBeginInfo bi = {}; bi.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO; bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    VkResult r = vkBeginCommandBuffer(sv.cb, &bi);
    if (r == VK_SUCCESS) {
        const uint32_t nq = 2 + 2 * desc.dispatchCount;
        vkCmdResetQueryPool(sv.cb, sv.queries, 0, nq);
        vkCmdWriteTimestamp(sv.cb, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, sv.queries, 0);
        sv.stageNames.clear(); sv.stageNames.reserve(desc.dispatchCount);
        for (uint32_t i = 0; i < desc.dispatchCount; ++i) {
            const Dispatch& dp = desc.dispatches[i];
            const Pipeline& p = *dp.pipeline;
            vkCmdBindPipeline(sv.cb, VK_PIPELINE_BIND_POINT_COMPUTE, p.pipeline);
            if (p.set != VK_NULL_HANDLE) vkCmdBindDescriptorSets(sv.cb, VK_PIPELINE_BIND_POINT_COMPUTE, p.layout, 0, 1, &p.set, 0, nullptr);
            if (dp.pushBytes && dp.push) vkCmdPushConstants(sv.cb, p.layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, dp.pushBytes, dp.push);
            vkCmdWriteTimestamp(sv.cb, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, sv.queries, 2 + 2 * i);
            if (dp.argsBuffer) vkCmdDispatchIndirect(sv.cb, dp.argsBuffer->buffer, dp.argsOffset);
            else vkCmdDispatch(sv.cb, std::max(1u, dp.gx), std::max(1u, dp.gy), std::max(1u, dp.gz));
            vkCmdWriteTimestamp(sv.cb, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, sv.queries, 3 + 2 * i);
            if (dp.barrierAfter || i + 1 == desc.dispatchCount) {
                VkMemoryBarrier mb = {}; mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
                mb.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT; mb.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
                vkCmdPipelineBarrier(sv.cb, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT, 0, 1, &mb, 0, nullptr, 0, nullptr);
            }
            sv.stageNames.emplace_back(p.name ? p.name : "");
        }
        vkCmdWriteTimestamp(sv.cb, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, sv.queries, 1);
        r = vkEndCommandBuffer(sv.cb);
    }
    if (r != VK_SUCCESS) {
        x.core.Retire(desc.cls, d.slot, false, 0.0);
        LogError("SubmitJob(%s): record failed %s", desc.name ? desc.name : "", VkResultName(r));
        if (r == VK_ERROR_DEVICE_LOST) exec::Quarantine(x, "record", r);
        return 0;
    }
    sv.dispatchCount = desc.dispatchCount; sv.name = desc.name ? desc.name : ""; sv.onRetired = desc.onRetired;
    sv.waitClass = desc.waitClass; sv.waitValue = desc.waitTimelineValue; sv.recorded = true; sv.submitted = false;
    // ---- submit
    if (!x.queueFallback) {
        VkTimelineSemaphoreSubmitInfo tsi = {}; tsi.sType = VK_STRUCTURE_TYPE_TIMELINE_SEMAPHORE_SUBMIT_INFO;
        VkSubmitInfo si = {}; si.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO; si.commandBufferCount = 1; si.pCommandBuffers = &sv.cb;
        VkSemaphore waitSem = VK_NULL_HANDLE, signalSem = cv.timeline; uint64_t waitVal = desc.waitTimelineValue, signalVal = d.timelineValue;
        VkPipelineStageFlags waitStage = VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT;
        if (x.timelineOk) {
            if (desc.waitTimelineValue != 0 && x.classes[desc.waitClass].timeline != VK_NULL_HANDLE) {
                waitSem = x.classes[desc.waitClass].timeline;
                si.waitSemaphoreCount = 1; si.pWaitSemaphores = &waitSem; si.pWaitDstStageMask = &waitStage;
                tsi.waitSemaphoreValueCount = 1; tsi.pWaitSemaphoreValues = &waitVal;
            }
            si.signalSemaphoreCount = 1; si.pSignalSemaphores = &signalSem;
            tsi.signalSemaphoreValueCount = 1; tsi.pSignalSemaphoreValues = &signalVal;
            si.pNext = &tsi;
        }
        // Without timeline semaphores the dependency is satisfied by submission order on the in-order scanner
        // queue (TrySubmit refused values not yet submitted) and the fence proves retirement.
        r = exec::SubmitScanner(x, si, sv.fence);
        if (r != VK_SUCCESS) {
            x.core.Retire(desc.cls, d.slot, false, 0.0);
            sv.onRetired = nullptr; sv.recorded = false;
            LogError("SubmitJob(%s): vkQueueSubmit %s", desc.name ? desc.name : "", VkResultName(r));
            if (r == VK_ERROR_DEVICE_LOST) exec::Quarantine(x, "vkQueueSubmit(scanner)", r);
            return 0;
        }
        sv.submitted = true;
    }
    // fallback: stays `recorded`; FS_HEVT_FRAME_END pumps it through AccessQueue on the graphics queue
    x.cv.notify_all();
    return d.jobId;
}

// ---- plugin_entry.cpp hooks ---------------------------------------------------------------------------
void FsHostExecutorOnDeviceInit() {
    Executor& x = X();
    UnityVulkanPluginEventConfig beginCfg = {};
    beginCfg.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
    beginCfg.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    beginCfg.flags = kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState | kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission;
    exec::ConfigureEvent(FS_HEVT_FRAME_BEGIN, beginCfg);
    UnityVulkanPluginEventConfig endCfg = {};
    endCfg.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;   // vkCmdResetQueryPool is not allowed inside a render pass
    endCfg.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    endCfg.flags = 0;
    exec::ConfigureEvent(FS_HEVT_FRAME_END, endCfg);
    UnityVulkanPluginEventConfig warmCfg = {};
    warmCfg.renderPassPrecondition = kUnityVulkanRenderPass_DontCare;
    warmCfg.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    warmCfg.flags = 0;
    exec::ConfigureEvent(FS_HEVT_WARMUP_STEP, warmCfg);
    bool created = false;
    {
        std::lock_guard<std::mutex> lock(x.mutex);
        if (!x.hostInitialized || x.vkReady.load(std::memory_order_acquire)) { Log("executor: device init (host %s)", x.hostInitialized ? "already has objects" : "not initialized yet; objects created at FsHost_Init"); return; }
        created = exec::VkInitLocked(x);
        if (!created) { x.status.store(FS_HOST_QUARANTINED); x.quarantineReason = "vulkan init failed"; LogError("executor: vulkan init failed at device init"); }
    }
    if (created) {
        exec::StartThreads(x);
        std::vector<std::function<void(bool)>> hooks;
        { std::lock_guard<std::mutex> lock(x.hooksMutex); hooks = x.deviceHooks; }
        for (auto& h : hooks) if (h) h(true);
    }
}

void FsHostExecutorOnDeviceShutdown() {
    Executor& x = X();
    exec::VkTeardown(x);
    std::lock_guard<std::mutex> lock(x.mutex);
    x.pendingTeardown = false;
    if (x.hostInitialized) x.status.store(FS_HOST_WARMING_UP, std::memory_order_release);   // objects are rebuilt at the next device init
}

bool FsHostExecutorRenderEvent(int eventId) {
    Executor& x = X();
    if (eventId != FS_HEVT_FRAME_BEGIN && eventId != FS_HEVT_FRAME_END && eventId != FS_HEVT_WARMUP_STEP) return false;
    bool teardown = false;
    { std::lock_guard<std::mutex> lock(x.mutex); teardown = x.pendingTeardown; }
    if (teardown) { exec::VkTeardown(x); std::lock_guard<std::mutex> lock(x.mutex); x.pendingTeardown = false; return true; }
    if (!x.vkReady.load(std::memory_order_acquire)) return true;
    switch (eventId) {
        case FS_HEVT_FRAME_BEGIN: exec::FrameBegin(x); break;
        case FS_HEVT_FRAME_END: exec::FrameEnd(x); break;
        case FS_HEVT_WARMUP_STEP: exec::WarmupStepEvent(x); break;
        default: break;
    }
    return true;
}

} // namespace fs
