// Render-event probes (UnityRenderingEvent ids from finalscan_native_api.h) and their results JSON.
//  EMPTY_SUBMIT  : 32 empty vkQueueSubmit + fence on the scanner queue (graphics-queue fallback)
//  TIMESTAMP     : timestamp query round trips with CPU submit/fence bracketing (no calibrated timestamps)
//  OVERLAP_*     : worker thread streaming ~2 ms bounded compute jobs on the scanner queue, GPU-timestamped
//  OVERLAP_PUMP  : fallback (no scanner queue): one bounded job on the graphics queue per event
//  FRAME_MARK    : TOP/BOTTOM timestamps recorded into Unity's command buffer each frame
//  PIPELINE_BIN  : pipeline creation timing (no cache / Unity cache / own cache) + VK_KHR_pipeline_binary
#include "plugin_state.h"
#include "json_writer.h"
#include "log.h"
#include "../include/finalscan_native_api.h"
#include "overlap_job_spirv.inc"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <thread>

namespace fs {
namespace {

constexpr uint32_t kElementCount = 1u << 20;      // 1M uints, 4 MiB SSBO
constexpr uint32_t kWorkgroupSize = 256;
constexpr uint32_t kMaxIterations = 8192;         // matches the shader clamp (bounded loop)
constexpr uint32_t kMaxRecordedJobs = 2048;
constexpr uint32_t kFrameMarkRing = 4096;
constexpr uint32_t kEmptySubmitCount = 32;
constexpr uint32_t kTimestampIterations = 16;
constexpr int64_t kTargetJobNs = 2000000;         // ~2 ms bounded jobs
constexpr uint64_t kFenceTimeoutNs = 2000000000ull;
constexpr uint32_t kInFlightJobs = 2;

struct PushConstants { uint32_t count; uint32_t iterations; uint32_t seed; };

struct ComputeResources {
    bool ready = false;
    std::string error;
    VkShaderModule module = VK_NULL_HANDLE;
    VkDescriptorSetLayout dsl = VK_NULL_HANDLE;
    VkPipelineLayout layout = VK_NULL_HANDLE;
    VkDescriptorPool pool = VK_NULL_HANDLE;
    VkDescriptorSet set = VK_NULL_HANDLE;
    VkBuffer buffer = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    VkPipeline pipeline = VK_NULL_HANDLE;
    double createNoCacheMs = -1.0;
};

struct JobSlot {
    VkCommandBuffer cb = VK_NULL_HANDLE;
    VkFence fence = VK_NULL_HANDLE;
    VkQueryPool queries = VK_NULL_HANDLE;
    bool inFlight = false;
    int64_t cpuSubmitNs = 0;
    uint32_t iterations = 0;
};

struct JobRecord { double gpuStartNs; double gpuEndNs; int64_t cpuSubmitNs; int64_t cpuFenceNs; uint32_t iterations; };
struct FrameMark { double gpuTopNs; double gpuBottomNs; int64_t cpuNs; uint64_t frame; };

struct Results {
    std::mutex mutex;
    // empty submit
    bool emptyRan = false; std::string emptyQueue; VkResult emptyError = VK_SUCCESS;
    std::vector<double> emptySubmitUs, emptyCompleteUs, emptyRoundtripUs;
    // timestamp
    bool tsRan = false; std::string tsQueue; VkResult tsError = VK_SUCCESS;
    struct TsIter { uint64_t begin, end; double deltaNs; int64_t cpuSubmit, cpuFence; };
    std::vector<TsIter> tsIters;
    // overlap
    bool overlapActive = false; std::string overlapQueue; std::string overlapError;
    std::vector<JobRecord> jobs; uint64_t jobsTotal = 0; uint32_t currentIterations = 0;
    int64_t overlapStartNs = 0, overlapStopNs = 0;
    // frame marks
    std::vector<FrameMark> marks; uint64_t marksRecorded = 0; uint32_t marksOverflow = 0; VkResult markError = VK_SUCCESS;
    // pipeline binary
    bool pbRan = false; std::string pbJson;
};

Results g_results;
ComputeResources g_compute;
std::mutex g_computeMutex;
VkQueryPool g_frameMarkPool = VK_NULL_HANDLE;
uint32_t g_markWrite = 0, g_markRead = 0;
std::vector<std::pair<int64_t, uint64_t>> g_markCpu;   // cpuNs, frame per recorded mark
bool g_markPoolReset = false;
std::atomic<bool> g_overlapStop{false};
std::thread g_overlapThread;
bool g_overlapThreadRunning = false;
std::vector<JobSlot> g_pumpSlots;                        // graphics-queue fallback slots
uint32_t g_pumpIterations = 32;

bool RecordingState(UnityVulkanRecordingState* rs, UnityVulkanGraphicsQueueAccess access) {
    DeviceState& d = Device();
    if (d.vulkanV2) return d.vulkanV2->CommandRecordingState(rs, access);
    if (d.vulkanV1) return d.vulkanV1->CommandRecordingState(rs, access);
    return false;
}

void ConfigureEvent(int id, const UnityVulkanPluginEventConfig& cfg) {
    DeviceState& d = Device();
    if (d.vulkanV2) d.vulkanV2->ConfigureEvent(id, &cfg);
    else if (d.vulkanV1) d.vulkanV1->ConfigureEvent(id, &cfg);
}

VkCommandBuffer AllocCommandBuffer() {
    DeviceState& d = Device();
    VkCommandBufferAllocateInfo ai = {};
    ai.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    ai.commandPool = d.commandPool;
    ai.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    ai.commandBufferCount = 1;
    VkCommandBuffer cb = VK_NULL_HANDLE;
    if (vkAllocateCommandBuffers(d.instance.device, &ai, &cb) != VK_SUCCESS) return VK_NULL_HANDLE;
    return cb;
}

void FreeCommandBuffer(VkCommandBuffer cb) {
    if (cb != VK_NULL_HANDLE) vkFreeCommandBuffers(Device().instance.device, Device().commandPool, 1, &cb);
}

VkFence CreateFence() {
    VkFenceCreateInfo fi = {}; fi.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
    VkFence f = VK_NULL_HANDLE;
    vkCreateFence(Device().instance.device, &fi, nullptr, &f);
    return f;
}

VkQueryPool CreateTimestampPool(uint32_t count) {
    VkQueryPoolCreateInfo qi = {}; qi.sType = VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO;
    qi.queryType = VK_QUERY_TYPE_TIMESTAMP; qi.queryCount = count;
    VkQueryPool p = VK_NULL_HANDLE;
    vkCreateQueryPool(Device().instance.device, &qi, nullptr, &p);
    return p;
}

// Submits on the scanner queue (mutex) or, inside a GraphicsQueueAccess_Allow event, Unity's queue.
VkResult Submit(VkQueue queue, VkCommandBuffer cb, VkFence fence) {
    DeviceState& d = Device();
    VkSubmitInfo si = {}; si.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    si.commandBufferCount = cb != VK_NULL_HANDLE ? 1u : 0u;
    si.pCommandBuffers = cb != VK_NULL_HANDLE ? &cb : nullptr;
    if (queue == d.scannerQueue && queue != VK_NULL_HANDLE) {
        std::lock_guard<std::mutex> lock(d.scannerSubmitMutex);
        return vkQueueSubmit(queue, 1, &si, fence);
    }
    return vkQueueSubmit(queue, 1, &si, fence);
}

// ---- compute resources -------------------------------------------------------------
bool EnsureCompute() {
    std::lock_guard<std::mutex> lock(g_computeMutex);
    if (g_compute.ready) return true;
    if (!g_compute.error.empty()) return false;
    DeviceState& d = Device();
    VkDevice dev = d.instance.device;
    auto fail = [&](const char* stage, VkResult r) {
        g_compute.error = std::string(stage) + " " + VkResultName(r);
        LogError("compute resources: %s", g_compute.error.c_str());
        return false;
    };
    VkShaderModuleCreateInfo smi = {}; smi.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    smi.codeSize = sizeof(kOverlapJobSpirv); smi.pCode = kOverlapJobSpirv;
    VkResult r = vkCreateShaderModule(dev, &smi, nullptr, &g_compute.module);
    if (r != VK_SUCCESS) return fail("vkCreateShaderModule", r);
    VkDescriptorSetLayoutBinding binding = {};
    binding.binding = 0; binding.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER; binding.descriptorCount = 1; binding.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
    VkDescriptorSetLayoutCreateInfo dli = {}; dli.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO; dli.bindingCount = 1; dli.pBindings = &binding;
    r = vkCreateDescriptorSetLayout(dev, &dli, nullptr, &g_compute.dsl);
    if (r != VK_SUCCESS) return fail("vkCreateDescriptorSetLayout", r);
    VkPushConstantRange pcr = {}; pcr.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT; pcr.offset = 0; pcr.size = sizeof(PushConstants);
    VkPipelineLayoutCreateInfo pli = {}; pli.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    pli.setLayoutCount = 1; pli.pSetLayouts = &g_compute.dsl; pli.pushConstantRangeCount = 1; pli.pPushConstantRanges = &pcr;
    r = vkCreatePipelineLayout(dev, &pli, nullptr, &g_compute.layout);
    if (r != VK_SUCCESS) return fail("vkCreatePipelineLayout", r);
    VkBufferCreateInfo bi = {}; bi.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bi.size = static_cast<VkDeviceSize>(kElementCount) * sizeof(uint32_t); bi.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT; bi.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    r = vkCreateBuffer(dev, &bi, nullptr, &g_compute.buffer);
    if (r != VK_SUCCESS) return fail("vkCreateBuffer", r);
    VkMemoryRequirements mr = {};
    vkGetBufferMemoryRequirements(dev, g_compute.buffer, &mr);
    uint32_t type = FindMemoryType(mr.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    if (type == UINT32_MAX) type = FindMemoryType(mr.memoryTypeBits, 0);
    if (type == UINT32_MAX) return fail("memory type", VK_ERROR_FEATURE_NOT_PRESENT);
    VkMemoryAllocateInfo mai = {}; mai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO; mai.allocationSize = mr.size; mai.memoryTypeIndex = type;
    r = vkAllocateMemory(dev, &mai, nullptr, &g_compute.memory);
    if (r != VK_SUCCESS) return fail("vkAllocateMemory", r);
    r = vkBindBufferMemory(dev, g_compute.buffer, g_compute.memory, 0);
    if (r != VK_SUCCESS) return fail("vkBindBufferMemory", r);
    VkDescriptorPoolSize ps = {}; ps.type = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER; ps.descriptorCount = 1;
    VkDescriptorPoolCreateInfo dpi = {}; dpi.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO; dpi.maxSets = 1; dpi.poolSizeCount = 1; dpi.pPoolSizes = &ps;
    r = vkCreateDescriptorPool(dev, &dpi, nullptr, &g_compute.pool);
    if (r != VK_SUCCESS) return fail("vkCreateDescriptorPool", r);
    VkDescriptorSetAllocateInfo dsi = {}; dsi.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO; dsi.descriptorPool = g_compute.pool; dsi.descriptorSetCount = 1; dsi.pSetLayouts = &g_compute.dsl;
    r = vkAllocateDescriptorSets(dev, &dsi, &g_compute.set);
    if (r != VK_SUCCESS) return fail("vkAllocateDescriptorSets", r);
    VkDescriptorBufferInfo dbi = {}; dbi.buffer = g_compute.buffer; dbi.offset = 0; dbi.range = VK_WHOLE_SIZE;
    VkWriteDescriptorSet wds = {}; wds.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET; wds.dstSet = g_compute.set; wds.dstBinding = 0; wds.descriptorCount = 1; wds.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER; wds.pBufferInfo = &dbi;
    vkUpdateDescriptorSets(dev, 1, &wds, 0, nullptr);
    // Working pipeline: no cache (timed; the PIPELINE_BIN probe repeats with caches).
    VkPipelineShaderStageCreateInfo stage = {}; stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    stage.stage = VK_SHADER_STAGE_COMPUTE_BIT; stage.module = g_compute.module; stage.pName = "main";
    VkComputePipelineCreateInfo cpi = {}; cpi.sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO; cpi.stage = stage; cpi.layout = g_compute.layout;
    const int64_t t0 = MonotonicNs();
    r = vkCreateComputePipelines(dev, VK_NULL_HANDLE, 1, &cpi, nullptr, &g_compute.pipeline);
    g_compute.createNoCacheMs = static_cast<double>(MonotonicNs() - t0) / 1e6;
    if (r != VK_SUCCESS) return fail("vkCreateComputePipelines", r);
    g_compute.ready = true;
    Log("compute resources ready: spirvWords=%zu pipelineCreateNoCacheMs=%.3f", sizeof(kOverlapJobSpirv) / 4, g_compute.createNoCacheMs);
    return true;
}

void DestroyCompute() {
    std::lock_guard<std::mutex> lock(g_computeMutex);
    VkDevice dev = Device().instance.device;
    if (dev == VK_NULL_HANDLE) return;
    if (g_compute.pipeline) vkDestroyPipeline(dev, g_compute.pipeline, nullptr);
    if (g_compute.pool) vkDestroyDescriptorPool(dev, g_compute.pool, nullptr);
    if (g_compute.buffer) vkDestroyBuffer(dev, g_compute.buffer, nullptr);
    if (g_compute.memory) vkFreeMemory(dev, g_compute.memory, nullptr);
    if (g_compute.layout) vkDestroyPipelineLayout(dev, g_compute.layout, nullptr);
    if (g_compute.dsl) vkDestroyDescriptorSetLayout(dev, g_compute.dsl, nullptr);
    if (g_compute.module) vkDestroyShaderModule(dev, g_compute.module, nullptr);
    g_compute = ComputeResources{};
}

bool CreateJobSlot(JobSlot& s) {
    s.cb = AllocCommandBuffer(); s.fence = CreateFence(); s.queries = CreateTimestampPool(2);
    return s.cb && s.fence && s.queries;
}
void DestroyJobSlot(JobSlot& s) {
    VkDevice dev = Device().instance.device;
    if (s.fence) vkDestroyFence(dev, s.fence, nullptr);
    if (s.queries) vkDestroyQueryPool(dev, s.queries, nullptr);
    FreeCommandBuffer(s.cb);
    s = JobSlot{};
}

VkResult RecordJob(JobSlot& s, uint32_t iterations, uint32_t seed) {
    VkCommandBufferBeginInfo bi = {}; bi.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO; bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    VkResult r = vkBeginCommandBuffer(s.cb, &bi);
    if (r != VK_SUCCESS) return r;
    vkCmdResetQueryPool(s.cb, s.queries, 0, 2);
    vkCmdWriteTimestamp(s.cb, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, s.queries, 0);
    vkCmdBindPipeline(s.cb, VK_PIPELINE_BIND_POINT_COMPUTE, g_compute.pipeline);
    vkCmdBindDescriptorSets(s.cb, VK_PIPELINE_BIND_POINT_COMPUTE, g_compute.layout, 0, 1, &g_compute.set, 0, nullptr);
    PushConstants pc = { kElementCount, std::min(iterations, kMaxIterations), seed };
    vkCmdPushConstants(s.cb, g_compute.layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(pc), &pc);
    vkCmdDispatch(s.cb, kElementCount / kWorkgroupSize, 1, 1);
    VkMemoryBarrier mb = {}; mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER; mb.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT; mb.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
    vkCmdPipelineBarrier(s.cb, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 1, &mb, 0, nullptr, 0, nullptr);
    vkCmdWriteTimestamp(s.cb, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, s.queries, 1);
    s.iterations = pc.iterations;
    return vkEndCommandBuffer(s.cb);
}

// Reads the slot's timestamps after its fence signalled; records the job; returns the GPU duration ns.
double CollectJob(JobSlot& s, int64_t cpuFenceNs) {
    uint64_t ts[2] = {};
    VkResult r = vkGetQueryPoolResults(Device().instance.device, s.queries, 0, 2, sizeof(ts), ts, sizeof(uint64_t), VK_QUERY_RESULT_64_BIT | VK_QUERY_RESULT_WAIT_BIT);
    s.inFlight = false;
    if (r != VK_SUCCESS) return -1.0;
    JobRecord rec = { TicksToNs(ts[0]), TicksToNs(ts[1]), s.cpuSubmitNs, cpuFenceNs, s.iterations };
    std::lock_guard<std::mutex> lock(g_results.mutex);
    ++g_results.jobsTotal;
    if (g_results.jobs.size() < kMaxRecordedJobs) g_results.jobs.push_back(rec);
    return rec.gpuEndNs - rec.gpuStartNs;
}

uint32_t AdjustIterations(uint32_t iterations, double measuredNs) {
    if (measuredNs <= 0.0) return iterations;
    double next = static_cast<double>(iterations) * (static_cast<double>(kTargetJobNs) / measuredNs);
    next = 0.5 * static_cast<double>(iterations) + 0.5 * next;   // damped
    return static_cast<uint32_t>(std::max(1.0, std::min(static_cast<double>(kMaxIterations), next)));
}

// ---- overlap worker (scanner queue) ------------------------------------------------
void OverlapWorker() {
    DeviceState& d = Device();
    std::vector<JobSlot> slots(kInFlightJobs);
    for (auto& s : slots) if (!CreateJobSlot(s)) { std::lock_guard<std::mutex> lock(g_results.mutex); g_results.overlapError = "job slot creation failed"; for (auto& t : slots) DestroyJobSlot(t); return; }
    uint32_t iterations = 32, seed = 1;
    size_t next = 0;
    while (!g_overlapStop.load(std::memory_order_acquire)) {
        JobSlot& s = slots[next];
        if (s.inFlight) {
            VkResult w = vkWaitForFences(d.instance.device, 1, &s.fence, VK_TRUE, kFenceTimeoutNs);
            const int64_t doneNs = MonotonicNs();
            if (w != VK_SUCCESS) { std::lock_guard<std::mutex> lock(g_results.mutex); g_results.overlapError = std::string("vkWaitForFences ") + VkResultName(w); break; }
            const double gpuNs = CollectJob(s, doneNs);
            iterations = AdjustIterations(iterations, gpuNs);
            { std::lock_guard<std::mutex> lock(g_results.mutex); g_results.currentIterations = iterations; }
        }
        vkResetFences(d.instance.device, 1, &s.fence);
        if (RecordJob(s, iterations, seed++) != VK_SUCCESS) { std::lock_guard<std::mutex> lock(g_results.mutex); g_results.overlapError = "RecordJob failed"; break; }
        s.cpuSubmitNs = MonotonicNs();
        VkResult r = Submit(d.scannerQueue, s.cb, s.fence);
        if (r != VK_SUCCESS) { std::lock_guard<std::mutex> lock(g_results.mutex); g_results.overlapError = std::string("vkQueueSubmit ") + VkResultName(r); break; }
        s.inFlight = true;
        next = (next + 1) % slots.size();
    }
    for (auto& s : slots) {
        if (s.inFlight) {
            if (vkWaitForFences(d.instance.device, 1, &s.fence, VK_TRUE, kFenceTimeoutNs) == VK_SUCCESS) CollectJob(s, MonotonicNs());
            else {
                // Never vkDeviceWaitIdle from a worker thread (Unity's render thread owns its queue);
                // leak the slot rather than destroy objects the GPU may still use.
                LogError("overlap worker: fence timeout on drain; leaking one job slot");
                std::lock_guard<std::mutex> lock(g_results.mutex);
                g_results.overlapError = "fence timeout on drain";
                continue;
            }
        }
        DestroyJobSlot(s);
    }
}

void StartOverlap() {
    DeviceState& d = Device();
    {
        std::lock_guard<std::mutex> lock(g_results.mutex);
        if (g_results.overlapActive) return;
        g_results.jobs.clear(); g_results.jobsTotal = 0; g_results.overlapError.clear();
        g_results.overlapActive = true;
        g_results.overlapStartNs = MonotonicNs();
        g_results.overlapStopNs = 0;
    }
    if (!EnsureCompute()) { std::lock_guard<std::mutex> lock(g_results.mutex); g_results.overlapError = g_compute.error; g_results.overlapActive = false; return; }
    if (d.scannerQueue != VK_NULL_HANDLE) {
        { std::lock_guard<std::mutex> lock(g_results.mutex); g_results.overlapQueue = "scanner"; }
        g_overlapStop.store(false, std::memory_order_release);
        g_overlapThread = std::thread(OverlapWorker);
        g_overlapThreadRunning = true;
        Log("overlap stream started on scanner queue");
    } else {
        std::lock_guard<std::mutex> lock(g_results.mutex);
        g_results.overlapQueue = "graphics(pump)";
        Log("overlap stream: no scanner queue; jobs run on the graphics queue via FS_EVT_PROBE_OVERLAP_PUMP");
    }
}

void PumpCollect(bool wait) {
    DeviceState& d = Device();
    for (auto& s : g_pumpSlots) {
        if (!s.inFlight) continue;
        VkResult st = wait ? vkWaitForFences(d.instance.device, 1, &s.fence, VK_TRUE, kFenceTimeoutNs) : vkGetFenceStatus(d.instance.device, s.fence);
        if (st == VK_SUCCESS) { const double gpuNs = CollectJob(s, MonotonicNs()); g_pumpIterations = AdjustIterations(g_pumpIterations, gpuNs); }
    }
}

void StopOverlap() {
    if (g_overlapThreadRunning) {
        g_overlapStop.store(true, std::memory_order_release);
        g_overlapThread.join();
        g_overlapThreadRunning = false;
    }
    PumpCollect(true);
    std::lock_guard<std::mutex> lock(g_results.mutex);
    if (g_results.overlapActive) g_results.overlapStopNs = MonotonicNs();
    g_results.overlapActive = false;
    Log("overlap stream stopped: jobs=%llu recorded=%zu error=%s", static_cast<unsigned long long>(g_results.jobsTotal), g_results.jobs.size(), g_results.overlapError.c_str());
}

// Graphics-queue fallback: one bounded job per event (inside a GraphicsQueueAccess_Allow event).
void PumpOverlap() {
    bool active;
    { std::lock_guard<std::mutex> lock(g_results.mutex); active = g_results.overlapActive; }
    if (!active || Device().scannerQueue != VK_NULL_HANDLE || !EnsureCompute()) return;
    if (g_pumpSlots.empty()) { g_pumpSlots.resize(kInFlightJobs); for (auto& s : g_pumpSlots) if (!CreateJobSlot(s)) { std::lock_guard<std::mutex> lock(g_results.mutex); g_results.overlapError = "pump slot creation failed"; return; } }
    PumpCollect(false);
    for (auto& s : g_pumpSlots) {
        if (s.inFlight) continue;
        DeviceState& d = Device();
        vkResetFences(d.instance.device, 1, &s.fence);
        static uint32_t seed = 7;
        if (RecordJob(s, g_pumpIterations, seed++) != VK_SUCCESS) return;
        s.cpuSubmitNs = MonotonicNs();
        if (Submit(d.instance.graphicsQueue, s.cb, s.fence) == VK_SUCCESS) s.inFlight = true;
        break;
    }
}

// ---- empty submit probe --------------------------------------------------------------
void EmptySubmitProbe() {
    DeviceState& d = Device();
    const bool scanner = d.scannerQueue != VK_NULL_HANDLE;
    VkQueue queue = scanner ? d.scannerQueue : d.instance.graphicsQueue;
    std::vector<VkFence> fences(kEmptySubmitCount);
    for (auto& f : fences) f = CreateFence();
    std::vector<double> submitUs(kEmptySubmitCount), completeUs(kEmptySubmitCount), roundtripUs(kEmptySubmitCount);
    std::vector<int64_t> submitNs(kEmptySubmitCount);
    VkResult err = VK_SUCCESS;
    // A: 32 back-to-back submits, then fence completion times.
    for (uint32_t i = 0; i < kEmptySubmitCount && err == VK_SUCCESS; ++i) {
        const int64_t t0 = MonotonicNs();
        err = Submit(queue, VK_NULL_HANDLE, fences[i]);
        const int64_t t1 = MonotonicNs();
        submitUs[i] = static_cast<double>(t1 - t0) / 1e3; submitNs[i] = t1;
    }
    for (uint32_t i = 0; i < kEmptySubmitCount && err == VK_SUCCESS; ++i) {
        err = vkWaitForFences(d.instance.device, 1, &fences[i], VK_TRUE, kFenceTimeoutNs);
        completeUs[i] = static_cast<double>(MonotonicNs() - submitNs[i]) / 1e3;
    }
    // B: serial submit+wait round trips.
    if (err == VK_SUCCESS) err = vkResetFences(d.instance.device, kEmptySubmitCount, fences.data());
    for (uint32_t i = 0; i < kEmptySubmitCount && err == VK_SUCCESS; ++i) {
        const int64_t t0 = MonotonicNs();
        err = Submit(queue, VK_NULL_HANDLE, fences[i]);
        if (err == VK_SUCCESS) err = vkWaitForFences(d.instance.device, 1, &fences[i], VK_TRUE, kFenceTimeoutNs);
        roundtripUs[i] = static_cast<double>(MonotonicNs() - t0) / 1e3;
    }
    if (err != VK_SUCCESS) vkDeviceWaitIdle(d.instance.device);
    for (auto f : fences) vkDestroyFence(d.instance.device, f, nullptr);
    std::lock_guard<std::mutex> lock(g_results.mutex);
    g_results.emptyRan = true; g_results.emptyQueue = scanner ? "scanner" : "graphics"; g_results.emptyError = err;
    g_results.emptySubmitUs = submitUs; g_results.emptyCompleteUs = completeUs; g_results.emptyRoundtripUs = roundtripUs;
    Log("empty submit probe on %s queue: result=%d submit[0]=%.1fus complete[0]=%.1fus roundtrip[0]=%.1fus", g_results.emptyQueue.c_str(), static_cast<int>(err), submitUs[0], completeUs[0], roundtripUs[0]);
}

// ---- timestamp probe --------------------------------------------------------------------
void TimestampProbe() {
    DeviceState& d = Device();
    const bool scanner = d.scannerQueue != VK_NULL_HANDLE;
    VkQueue queue = scanner ? d.scannerQueue : d.instance.graphicsQueue;
    VkQueryPool pool = CreateTimestampPool(kTimestampIterations * 2);
    VkCommandBuffer cb = AllocCommandBuffer();
    VkFence fence = CreateFence();
    std::vector<Results::TsIter> iters;
    VkResult err = (pool && cb && fence) ? VK_SUCCESS : VK_ERROR_INITIALIZATION_FAILED;
    for (uint32_t i = 0; i < kTimestampIterations && err == VK_SUCCESS; ++i) {
        VkCommandBufferBeginInfo bi = {}; bi.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO; bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        err = vkBeginCommandBuffer(cb, &bi);
        if (err != VK_SUCCESS) break;
        vkCmdResetQueryPool(cb, pool, i * 2, 2);
        vkCmdWriteTimestamp(cb, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, pool, i * 2);
        VkMemoryBarrier mb = {}; mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER; mb.srcAccessMask = VK_ACCESS_MEMORY_WRITE_BIT; mb.dstAccessMask = VK_ACCESS_MEMORY_READ_BIT;
        vkCmdPipelineBarrier(cb, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, 0, 1, &mb, 0, nullptr, 0, nullptr);
        vkCmdWriteTimestamp(cb, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, pool, i * 2 + 1);
        err = vkEndCommandBuffer(cb);
        if (err != VK_SUCCESS) break;
        vkResetFences(d.instance.device, 1, &fence);
        const int64_t cpuSubmit = MonotonicNs();
        err = Submit(queue, cb, fence);
        if (err != VK_SUCCESS) break;
        err = vkWaitForFences(d.instance.device, 1, &fence, VK_TRUE, kFenceTimeoutNs);
        const int64_t cpuFence = MonotonicNs();
        if (err != VK_SUCCESS) break;
        uint64_t ts[2] = {};
        err = vkGetQueryPoolResults(d.instance.device, pool, i * 2, 2, sizeof(ts), ts, sizeof(uint64_t), VK_QUERY_RESULT_64_BIT | VK_QUERY_RESULT_WAIT_BIT);
        if (err != VK_SUCCESS) break;
        Results::TsIter it = { MaskTimestamp(ts[0]), MaskTimestamp(ts[1]), TicksToNs(ts[1]) - TicksToNs(ts[0]), cpuSubmit, cpuFence };
        iters.push_back(it);
    }
    if (err != VK_SUCCESS) vkDeviceWaitIdle(d.instance.device);
    if (fence) vkDestroyFence(d.instance.device, fence, nullptr);
    if (pool) vkDestroyQueryPool(d.instance.device, pool, nullptr);
    FreeCommandBuffer(cb);
    std::lock_guard<std::mutex> lock(g_results.mutex);
    g_results.tsRan = true; g_results.tsQueue = scanner ? "scanner" : "graphics"; g_results.tsError = err; g_results.tsIters = iters;
    Log("timestamp probe on %s queue: result=%d iterations=%zu period=%.6f", g_results.tsQueue.c_str(), static_cast<int>(err), iters.size(), d.timestampPeriod);
}

// ---- frame marks ---------------------------------------------------------------------
void CollectFrameMarks() {
    if (g_frameMarkPool == VK_NULL_HANDLE) return;
    DeviceState& d = Device();
    while (g_markRead < g_markWrite) {
        const uint32_t n = std::min<uint32_t>(32u, g_markWrite - g_markRead);
        std::vector<uint64_t> data(static_cast<size_t>(n) * 4);   // per query: value, availability
        VkResult r = vkGetQueryPoolResults(d.instance.device, g_frameMarkPool, g_markRead * 2, n * 2, data.size() * sizeof(uint64_t), data.data(), 2 * sizeof(uint64_t),
            VK_QUERY_RESULT_64_BIT | VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);
        if (r != VK_SUCCESS && r != VK_NOT_READY) { std::lock_guard<std::mutex> lock(g_results.mutex); g_results.markError = r; return; }
        uint32_t consumed = 0;
        for (uint32_t i = 0; i < n; ++i) {
            const uint64_t availTop = data[i * 4 + 1], availBottom = data[i * 4 + 3];
            if (availTop == 0 || availBottom == 0) break;
            FrameMark m = { TicksToNs(data[i * 4]), TicksToNs(data[i * 4 + 2]), g_markCpu[g_markRead + i].first, g_markCpu[g_markRead + i].second };
            std::lock_guard<std::mutex> lock(g_results.mutex);
            g_results.marks.push_back(m);
            ++consumed;
        }
        g_markRead += consumed;
        if (consumed < n) return;
    }
}

void FrameMarkProbe() {
    DeviceState& d = Device();
    if (g_frameMarkPool == VK_NULL_HANDLE) return;
    UnityVulkanRecordingState rs = {};
    if (!RecordingState(&rs, kUnityVulkanGraphicsQueueAccess_DontCare) || rs.commandBuffer == VK_NULL_HANDLE) return;
    if (g_markWrite >= kFrameMarkRing) { std::lock_guard<std::mutex> lock(g_results.mutex); ++g_results.marksOverflow; CollectFrameMarks(); return; }
    if (!g_markPoolReset) {
        // Host reset when available (queries start uninitialized), else a one-time in-stream reset.
        if (d.hostQueryResetEnabled && d.resetQueryPool) d.resetQueryPool(d.instance.device, g_frameMarkPool, 0, kFrameMarkRing * 2);
        else vkCmdResetQueryPool(rs.commandBuffer, g_frameMarkPool, 0, kFrameMarkRing * 2);
        g_markPoolReset = true;
    }
    const uint32_t idx = g_markWrite;
    vkCmdResetQueryPool(rs.commandBuffer, g_frameMarkPool, idx * 2, 2);
    vkCmdWriteTimestamp(rs.commandBuffer, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, g_frameMarkPool, idx * 2);
    vkCmdWriteTimestamp(rs.commandBuffer, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, g_frameMarkPool, idx * 2 + 1);
    g_markCpu[idx] = { MonotonicNs(), rs.currentFrameNumber };
    ++g_markWrite;
    { std::lock_guard<std::mutex> lock(g_results.mutex); ++g_results.marksRecorded; }
    CollectFrameMarks();
}

// ---- pipeline binary probe -----------------------------------------------------------
double TimedCreate(VkPipelineCache cache, const void* pNext, VkPipeline* out, VkResult* result) {
    VkPipelineShaderStageCreateInfo stage = {}; stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    stage.stage = VK_SHADER_STAGE_COMPUTE_BIT; stage.module = g_compute.module; stage.pName = "main";
    VkComputePipelineCreateInfo cpi = {}; cpi.sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO; cpi.pNext = pNext; cpi.stage = stage; cpi.layout = g_compute.layout;
    const int64_t t0 = MonotonicNs();
    *result = vkCreateComputePipelines(Device().instance.device, cache, 1, &cpi, nullptr, out);
    return static_cast<double>(MonotonicNs() - t0) / 1e6;
}

void PipelineBinaryProbe() {
    DeviceState& d = Device();
    JsonWriter w;
    w.BeginObject();
    if (!EnsureCompute()) { w.KV("error", g_compute.error); w.EndObject(); std::lock_guard<std::mutex> lock(g_results.mutex); g_results.pbRan = true; g_results.pbJson = w.Take(); return; }
    VkDevice dev = d.instance.device;
    VkResult r = VK_SUCCESS;
    VkPipeline p = VK_NULL_HANDLE;
    w.KV("createNoCacheMs", g_compute.createNoCacheMs, 4);
    double ms = TimedCreate(VK_NULL_HANDLE, nullptr, &p, &r);
    w.KV("createNoCacheSecondMs", ms, 4); w.KV("createNoCacheSecondResult", static_cast<int32_t>(r));
    if (p) { vkDestroyPipeline(dev, p, nullptr); p = VK_NULL_HANDLE; }
    if (d.instance.pipelineCache != VK_NULL_HANDLE) {
        ms = TimedCreate(d.instance.pipelineCache, nullptr, &p, &r);
        w.KV("createUnityCacheMs", ms, 4); w.KV("createUnityCacheResult", static_cast<int32_t>(r));
        if (p) { vkDestroyPipeline(dev, p, nullptr); p = VK_NULL_HANDLE; }
    } else w.KVNull("createUnityCacheMs");
    VkPipelineCacheCreateInfo pci = {}; pci.sType = VK_STRUCTURE_TYPE_PIPELINE_CACHE_CREATE_INFO;
    VkPipelineCache own = VK_NULL_HANDLE;
    if (vkCreatePipelineCache(dev, &pci, nullptr, &own) == VK_SUCCESS) {
        ms = TimedCreate(own, nullptr, &p, &r);
        w.KV("createOwnCacheColdMs", ms, 4);
        if (p) { vkDestroyPipeline(dev, p, nullptr); p = VK_NULL_HANDLE; }
        ms = TimedCreate(own, nullptr, &p, &r);
        w.KV("createOwnCacheWarmMs", ms, 4);
        if (p) { vkDestroyPipeline(dev, p, nullptr); p = VK_NULL_HANDLE; }
        size_t cacheBytes = 0;
        vkGetPipelineCacheData(dev, own, &cacheBytes, nullptr);
        w.KV("ownCacheBytes", static_cast<uint64_t>(cacheBytes));
        vkDestroyPipelineCache(dev, own, nullptr);
    }

    const bool supported = d.pipelineBinaryEnabled && d.getPipelineKey && d.createPipelineBinaries && d.getPipelineBinaryData && d.destroyPipelineBinary;
    w.KV("extensionEnabled", d.pipelineBinaryEnabled);
    w.KV("supported", supported);
    if (supported) {
        VkPipelineBinaryKeyKHR globalKey = {}; globalKey.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR;
        r = d.getPipelineKey(dev, nullptr, &globalKey);
        w.KV("globalKeyResult", static_cast<int32_t>(r));
        w.KV("globalKeySize", globalKey.keySize);
        { std::string hex; static const char* h = "0123456789abcdef"; for (uint32_t i = 0; i < globalKey.keySize && i < VK_MAX_PIPELINE_BINARY_KEY_SIZE_KHR; ++i) { hex += h[globalKey.key[i] >> 4]; hex += h[globalKey.key[i] & 15]; } w.KV("globalKey", hex); }

        VkPipelineShaderStageCreateInfo stage = {}; stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        stage.stage = VK_SHADER_STAGE_COMPUTE_BIT; stage.module = g_compute.module; stage.pName = "main";
        VkPipelineCreateFlags2CreateInfoKHR flags2 = {}; flags2.sType = VK_STRUCTURE_TYPE_PIPELINE_CREATE_FLAGS_2_CREATE_INFO_KHR; flags2.flags = VK_PIPELINE_CREATE_2_CAPTURE_DATA_BIT_KHR;
        VkComputePipelineCreateInfo cpi = {}; cpi.sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO; cpi.pNext = &flags2; cpi.stage = stage; cpi.layout = g_compute.layout;
        VkPipelineCreateInfoKHR keyInfo = {}; keyInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_CREATE_INFO_KHR; keyInfo.pNext = &cpi;
        VkPipelineBinaryKeyKHR pipelineKey = {}; pipelineKey.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR;
        r = d.getPipelineKey(dev, &keyInfo, &pipelineKey);
        w.KV("pipelineKeyResult", static_cast<int32_t>(r));
        w.KV("pipelineKeySize", pipelineKey.keySize);
        w.KV("keySize", pipelineKey.keySize);   // header naming

        VkPipeline captured = VK_NULL_HANDLE;
        ms = TimedCreate(VK_NULL_HANDLE, &flags2, &captured, &r);
        w.KV("createCaptureDataMs", ms, 4); w.KV("createCaptureDataResult", static_cast<int32_t>(r));
        uint32_t binaryCount = 0; uint64_t totalBytes = 0;
        std::vector<VkPipelineBinaryKHR> binaries;
        if (r == VK_SUCCESS && captured) {
            VkPipelineBinaryCreateInfoKHR bci = {}; bci.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_CREATE_INFO_KHR; bci.pipeline = captured;
            VkPipelineBinaryHandlesInfoKHR handles = {}; handles.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_HANDLES_INFO_KHR;
            const int64_t t0 = MonotonicNs();
            r = d.createPipelineBinaries(dev, &bci, nullptr, &handles);
            w.KV("createBinariesCountResult", static_cast<int32_t>(r));
            binaryCount = handles.pipelineBinaryCount;
            if ((r == VK_SUCCESS || r == VK_INCOMPLETE) && binaryCount > 0) {
                binaries.assign(binaryCount, VK_NULL_HANDLE);
                handles.pPipelineBinaries = binaries.data();
                r = d.createPipelineBinaries(dev, &bci, nullptr, &handles);
                w.KV("createBinariesResult", static_cast<int32_t>(r));
            }
            w.KV("createBinariesMs", static_cast<double>(MonotonicNs() - t0) / 1e6, 4);
            w.Key("binaries"); w.BeginArray();
            for (VkPipelineBinaryKHR b : binaries) {
                if (b == VK_NULL_HANDLE) continue;
                VkPipelineBinaryDataInfoKHR di = {}; di.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_DATA_INFO_KHR; di.pipelineBinary = b;
                VkPipelineBinaryKeyKHR key = {}; key.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR;
                size_t size = 0;
                VkResult dr = d.getPipelineBinaryData(dev, &di, &key, &size, nullptr);
                w.BeginObject(); w.KV("result", static_cast<int32_t>(dr)); w.KV("keySize", key.keySize); w.KV("bytes", static_cast<uint64_t>(size)); w.EndObject();
                totalBytes += size;
            }
            w.EndArray();
            // Pipeline creation from binaries (the production path).
            if (!binaries.empty()) {
                VkPipelineBinaryInfoKHR binInfo = {}; binInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_INFO_KHR; binInfo.binaryCount = binaryCount; binInfo.pPipelineBinaries = binaries.data();
                VkPipeline fromBinary = VK_NULL_HANDLE;
                ms = TimedCreate(VK_NULL_HANDLE, &binInfo, &fromBinary, &r);
                w.KV("createFromBinaryMs", ms, 4); w.KV("createFromBinaryResult", static_cast<int32_t>(r));
                if (fromBinary) vkDestroyPipeline(dev, fromBinary, nullptr);
            }
            if (d.releaseCapturedPipelineData) {
                VkReleaseCapturedPipelineDataInfoKHR rel = {}; rel.sType = VK_STRUCTURE_TYPE_RELEASE_CAPTURED_PIPELINE_DATA_INFO_KHR; rel.pipeline = captured;
                w.KV("releaseCapturedResult", static_cast<int32_t>(d.releaseCapturedPipelineData(dev, &rel, nullptr)));
            }
            for (VkPipelineBinaryKHR b : binaries) if (b) d.destroyPipelineBinary(dev, b, nullptr);
            vkDestroyPipeline(dev, captured, nullptr);
        }
        w.KV("binaryCount", binaryCount);
        w.KV("totalBytes", totalBytes);
    }
    w.EndObject();
    std::lock_guard<std::mutex> lock(g_results.mutex);
    g_results.pbRan = true; g_results.pbJson = w.Take();
    LogJson("pipeline-binary", g_results.pbJson);
}

void Stats(JsonWriter& w, const char* key, const std::vector<double>& v) {
    w.Key(key); w.BeginObject();
    if (v.empty()) { w.EndObject(); return; }
    std::vector<double> s(v); std::sort(s.begin(), s.end());
    double sum = 0; for (double x : s) sum += x;
    w.KV("min", s.front(), 5); w.KV("median", s[s.size() / 2], 5); w.KV("max", s.back(), 5); w.KV("avg", sum / static_cast<double>(s.size()), 5); w.KV("count", static_cast<uint64_t>(s.size()));
    w.EndObject();
}

} // namespace

void ProbesInitialize() {
    DeviceState& d = Device();
    UnityVulkanPluginEventConfig queueCfg = {};
    queueCfg.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
    queueCfg.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_Allow;
    queueCfg.flags = kUnityVulkanEventConfigFlag_FlushCommandBuffers | kUnityVulkanEventConfigFlag_SyncWorkerThreads;
    ConfigureEvent(FS_EVT_PROBE_EMPTY_SUBMIT, queueCfg);
    ConfigureEvent(FS_EVT_PROBE_TIMESTAMP, queueCfg);
    ConfigureEvent(FS_EVT_PROBE_OVERLAP_PUMP, queueCfg);
    UnityVulkanPluginEventConfig plainCfg = {};
    plainCfg.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
    plainCfg.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    plainCfg.flags = kUnityVulkanEventConfigFlag_SyncWorkerThreads;
    ConfigureEvent(FS_EVT_PROBE_OVERLAP_START, plainCfg);
    ConfigureEvent(FS_EVT_PROBE_OVERLAP_STOP, plainCfg);
    ConfigureEvent(FS_EVT_PROBE_PIPELINE_BIN, plainCfg);
    UnityVulkanPluginEventConfig markCfg = {};
    markCfg.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;   // vkCmdResetQueryPool is not allowed inside a render pass
    markCfg.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    markCfg.flags = 0;
    ConfigureEvent(FS_EVT_PROBE_FRAME_MARK, markCfg);

    g_frameMarkPool = CreateTimestampPool(kFrameMarkRing * 2);
    g_markCpu.assign(kFrameMarkRing, {0, 0});
    g_markWrite = g_markRead = 0;
    g_markPoolReset = false;
    (void)d;
    Log("probes initialized (events %d..%d configured, frame-mark ring=%u)", FS_EVT_PROBE_EMPTY_SUBMIT, FS_EVT_PROBE_OVERLAP_PUMP, kFrameMarkRing);
}

void ProbesShutdown() {
    DeviceState& d = Device();
    StopOverlap();
    if (d.instance.device != VK_NULL_HANDLE) vkDeviceWaitIdle(d.instance.device);   // shutdown-only idle
    for (auto& s : g_pumpSlots) DestroyJobSlot(s);
    g_pumpSlots.clear();
    if (g_frameMarkPool) { vkDestroyQueryPool(d.instance.device, g_frameMarkPool, nullptr); g_frameMarkPool = VK_NULL_HANDLE; }
    DestroyCompute();
}

void ProbesHandleRenderEvent(int eventId) {
    switch (eventId) {
        case FS_EVT_PROBE_EMPTY_SUBMIT: EmptySubmitProbe(); break;
        case FS_EVT_PROBE_TIMESTAMP: TimestampProbe(); break;
        case FS_EVT_PROBE_OVERLAP_START: StartOverlap(); break;
        case FS_EVT_PROBE_OVERLAP_STOP: StopOverlap(); LogJson("probe-results", ProbeResultsJson()); break;
        case FS_EVT_PROBE_PIPELINE_BIN: PipelineBinaryProbe(); break;
        case FS_EVT_PROBE_FRAME_MARK: FrameMarkProbe(); break;
        case FS_EVT_PROBE_OVERLAP_PUMP: PumpOverlap(); break;
        default: Log("unknown render event %d", eventId); break;
    }
}

std::string ProbeResultsJson() {
    DeviceState& d = Device();
    std::lock_guard<std::mutex> lock(g_results.mutex);
    JsonWriter w;
    w.BeginObject();
    w.KV("vulkanReady", d.ready.load(std::memory_order_acquire));
    w.KV("scannerQueue", d.scannerQueue != VK_NULL_HANDLE);
    w.KV("timestampPeriodNs", static_cast<double>(d.timestampPeriod), 9);
    w.KV("timestampValidBits", d.timestampValidBits);
    w.KV("nowMonotonicNs", MonotonicNs());
    w.KV("nowBoottimeNs", BoottimeNs());

    w.Key("emptySubmit"); w.BeginObject();
    w.KV("ran", g_results.emptyRan); w.KV("queue", g_results.emptyQueue); w.KV("result", static_cast<int32_t>(g_results.emptyError)); w.KV("count", kEmptySubmitCount);
    w.Key("submitUs"); w.BeginArray(); for (double x : g_results.emptySubmitUs) w.Double(x, 4); w.EndArray();
    w.Key("completeUs"); w.BeginArray(); for (double x : g_results.emptyCompleteUs) w.Double(x, 4); w.EndArray();
    w.Key("roundtripUs"); w.BeginArray(); for (double x : g_results.emptyRoundtripUs) w.Double(x, 4); w.EndArray();
    Stats(w, "submitStats", g_results.emptySubmitUs); Stats(w, "completeStats", g_results.emptyCompleteUs); Stats(w, "roundtripStats", g_results.emptyRoundtripUs);
    w.EndObject();
    // ABI comment in the header calls this emptySubmitUs[]: mirror the round-trip array under that name.
    w.Key("emptySubmitUs"); w.BeginArray(); for (double x : g_results.emptyRoundtripUs) w.Double(x, 4); w.EndArray();

    w.Key("timestamp"); w.BeginObject();
    w.KV("ran", g_results.tsRan); w.KV("queue", g_results.tsQueue); w.KV("result", static_cast<int32_t>(g_results.tsError));
    w.KV("timestampPeriodNs", static_cast<double>(d.timestampPeriod), 9); w.KV("validBits", d.timestampValidBits);
    w.KV("computeAndGraphics", d.timestampComputeAndGraphics);
    w.Key("iterations"); w.BeginArray();
    double lower = -1e300, upper = 1e300; std::vector<double> deltas, roundtrips;
    for (const auto& it : g_results.tsIters) {
        w.BeginObject();
        w.KV("gpuBeginTicks", it.begin); w.KV("gpuEndTicks", it.end); w.KV("deltaNs", it.deltaNs, 3);
        w.KV("gpuBeginNs", static_cast<double>(it.begin) * d.timestampPeriod, 12); w.KV("gpuEndNs", static_cast<double>(it.end) * d.timestampPeriod, 12);
        w.KV("cpuSubmitNs", it.cpuSubmit); w.KV("cpuFenceNs", it.cpuFence); w.KV("cpuRoundtripUs", static_cast<double>(it.cpuFence - it.cpuSubmit) / 1e3, 4);
        w.EndObject();
        deltas.push_back(it.deltaNs); roundtrips.push_back(static_cast<double>(it.cpuFence - it.cpuSubmit) / 1e3);
        lower = std::max(lower, static_cast<double>(it.cpuSubmit) - static_cast<double>(it.begin) * d.timestampPeriod);
        upper = std::min(upper, static_cast<double>(it.cpuFence) - static_cast<double>(it.end) * d.timestampPeriod);
    }
    w.EndArray();
    Stats(w, "deltaNsStats", deltas); Stats(w, "cpuRoundtripUsStats", roundtrips);
    w.Key("cpuMinusGpuOffsetNs"); w.BeginObject();
    if (!g_results.tsIters.empty()) { w.KV("lower", lower, 15); w.KV("upper", upper, 15); w.KV("widthUs", (upper - lower) / 1e3, 4); w.KV("mid", (upper + lower) * 0.5, 15); }
    w.EndObject();
    w.EndObject();

    w.Key("overlap"); w.BeginObject();
    w.KV("active", g_results.overlapActive); w.KV("queue", g_results.overlapQueue); w.KV("error", g_results.overlapError);
    w.KV("startCpuNs", g_results.overlapStartNs); w.KV("stopCpuNs", g_results.overlapStopNs);
    w.KV("targetJobUs", static_cast<double>(kTargetJobNs) / 1e3, 4); w.KV("currentIterations", g_results.currentIterations);
    w.KV("elements", kElementCount); w.KV("maxIterations", kMaxIterations);
    w.KV("jobCount", g_results.jobsTotal); w.KV("recorded", static_cast<uint64_t>(g_results.jobs.size())); w.KV("recordCap", kMaxRecordedJobs);
    double busy = 0, first = 0, last = 0; std::vector<double> jobUs, latencyUs;
    for (size_t i = 0; i < g_results.jobs.size(); ++i) {
        const JobRecord& j = g_results.jobs[i];
        busy += j.gpuEndNs - j.gpuStartNs; jobUs.push_back((j.gpuEndNs - j.gpuStartNs) / 1e3); latencyUs.push_back(static_cast<double>(j.cpuFenceNs - j.cpuSubmitNs) / 1e3);
        if (i == 0) first = j.gpuStartNs; last = std::max(last, j.gpuEndNs);
    }
    w.KV("scannerBusyMs", busy / 1e6, 6); w.KV("scannerSpanMs", g_results.jobs.empty() ? 0.0 : (last - first) / 1e6, 6);
    w.KV("scannerDuty", (g_results.jobs.empty() || last <= first) ? 0.0 : busy / (last - first), 4);
    Stats(w, "jobGpuUsStats", jobUs); Stats(w, "jobCpuLatencyUsStats", latencyUs);
    w.Key("scannerJobs"); w.BeginArray();
    for (const JobRecord& j : g_results.jobs) { w.BeginObject(); w.KV("gpuStartNs", j.gpuStartNs, 15); w.KV("gpuEndNs", j.gpuEndNs, 15); w.KV("cpuSubmitNs", j.cpuSubmitNs); w.KV("cpuFenceNs", j.cpuFenceNs); w.KV("iterations", j.iterations); w.EndObject(); }
    w.EndArray();
    // Frame marks whose GPU timestamps fall strictly inside a scanner job interval (same GPU clock):
    // evidence of concurrent execution or time-slicing between the two queues.
    uint64_t inside = 0, insideDuring = 0, marksDuring = 0;
    for (const FrameMark& m : g_results.marks) {
        bool during = !g_results.jobs.empty() && m.gpuTopNs >= first && m.gpuTopNs <= last;
        if (during) ++marksDuring;
        for (const JobRecord& j : g_results.jobs)
            if ((m.gpuTopNs > j.gpuStartNs && m.gpuTopNs < j.gpuEndNs) || (m.gpuBottomNs > j.gpuStartNs && m.gpuBottomNs < j.gpuEndNs)) { ++inside; if (during) ++insideDuring; break; }
    }
    w.KV("frameMarksDuringStream", marksDuring); w.KV("frameMarksInsideScannerJobs", inside);
    w.KV("overlapDetected", insideDuring > 0);
    w.KV("overlapFraction", marksDuring ? static_cast<double>(insideDuring) / static_cast<double>(marksDuring) : 0.0, 4);
    w.EndObject();

    w.Key("frameMarks"); w.BeginObject();
    w.KV("recorded", g_results.marksRecorded); w.KV("collected", static_cast<uint64_t>(g_results.marks.size())); w.KV("overflow", g_results.marksOverflow);
    w.KV("ring", kFrameMarkRing); w.KV("result", static_cast<int32_t>(g_results.markError));
    std::vector<double> gpuIntervalMs, cpuIntervalMs;
    for (size_t i = 1; i < g_results.marks.size(); ++i) { gpuIntervalMs.push_back((g_results.marks[i].gpuTopNs - g_results.marks[i - 1].gpuTopNs) / 1e6); cpuIntervalMs.push_back(static_cast<double>(g_results.marks[i].cpuNs - g_results.marks[i - 1].cpuNs) / 1e6); }
    Stats(w, "gpuIntervalMsStats", gpuIntervalMs); Stats(w, "cpuIntervalMsStats", cpuIntervalMs);
    w.Key("marks"); w.BeginArray();
    for (const FrameMark& m : g_results.marks) { w.BeginObject(); w.KV("gpuNs", m.gpuTopNs, 15); w.KV("gpuBottomNs", m.gpuBottomNs, 15); w.KV("cpuNs", m.cpuNs); w.KV("frame", m.frame); w.EndObject(); }
    w.EndArray();
    w.EndObject();
    w.Key("graphicsFrameMarks"); w.BeginArray();   // header naming: [gpuNs...]
    for (const FrameMark& m : g_results.marks) w.Double(m.gpuTopNs, 15);
    w.EndArray();

    w.RawValue("pipelineBinary", g_results.pbRan ? g_results.pbJson : std::string("{\"ran\":false,\"supported\":") + (d.pipelineBinaryEnabled ? "true" : "false") + "}");
    w.EndObject();
    return w.Take();
}

} // namespace fs
