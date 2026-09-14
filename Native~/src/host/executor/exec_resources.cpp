// Executor resources: buffers (device-local / host-visible, BDA), compute pipelines (persistent descriptor
// set, push constants, VkPipelineCache from disk, VK_KHR_pipeline_binary delivery), Unity buffer/texture
// imports (cached per native pointer), samplers. Contract §15.3, §15.7; docs/NATIVE_EXECUTOR_DESIGN.md §5, §9.
#include "executor_internal.h"
#include "pipeline_store.h"
#include "../../json_writer.h"
#include "../../log.h"
#include <algorithm>
#include <cstring>

namespace fs {
namespace exec {
namespace {

store::CacheIdentity Identity() {
    store::CacheIdentity id;
    const VkPhysicalDeviceProperties& p = Device().properties;
    id.driverVersion = p.driverVersion; id.vendorID = p.vendorID; id.deviceID = p.deviceID;
    std::memcpy(id.pipelineCacheUUID, p.pipelineCacheUUID, store::kUuidSize);
    return id;
}

bool AccessBufferUnity(void* ptr, UnityVulkanResourceAccessMode mode, UnityVulkanBuffer* out) {
    DeviceState& d = Device();
    const VkPipelineStageFlags stages = VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
    const VkAccessFlags access = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT;
    if (d.vulkanV2) return d.vulkanV2->AccessBuffer(ptr, stages, access, mode, out);
    if (d.vulkanV1) return d.vulkanV1->AccessBuffer(ptr, stages, access, mode, out);
    return false;
}

bool AccessTextureUnity(void* ptr, UnityVulkanResourceAccessMode mode, UnityVulkanImage* out) {
    DeviceState& d = Device();
    const VkPipelineStageFlags stages = VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
    if (d.vulkanV2) return d.vulkanV2->AccessTexture(ptr, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, stages, VK_ACCESS_SHADER_READ_BIT, mode, out);
    if (d.vulkanV1) return d.vulkanV1->AccessTexture(ptr, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, stages, VK_ACCESS_SHADER_READ_BIT, mode, out);
    return false;
}

VkDescriptorType BindingType(const PipelineRecord& rec, uint32_t binding, bool& found) {
    for (const auto& b : rec.bindings) if (b.binding == binding) { found = true; return b.descriptorType; }
    found = false; return VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
}

bool StorePipelineBinaries(Executor& x, VkPipeline pipeline, const std::string& globalHex, const std::string& pipelineHex, const store::CacheIdentity& id) {
    DeviceState& d = Device();
    VkPipelineBinaryCreateInfoKHR bci = {}; bci.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_CREATE_INFO_KHR; bci.pipeline = pipeline;
    VkPipelineBinaryHandlesInfoKHR handles = {}; handles.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_HANDLES_INFO_KHR;
    VkResult r = d.createPipelineBinaries(x.device, &bci, nullptr, &handles);
    if ((r != VK_SUCCESS && r != VK_INCOMPLETE) || handles.pipelineBinaryCount == 0) return false;
    std::vector<VkPipelineBinaryKHR> bins(handles.pipelineBinaryCount, VK_NULL_HANDLE);
    handles.pPipelineBinaries = bins.data();
    r = d.createPipelineBinaries(x.device, &bci, nullptr, &handles);
    bool ok = r == VK_SUCCESS;
    std::vector<store::BinaryBlob> blobs;
    for (VkPipelineBinaryKHR b : bins) {
        if (b == VK_NULL_HANDLE) { ok = false; continue; }
        VkPipelineBinaryDataInfoKHR di = {}; di.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_DATA_INFO_KHR; di.pipelineBinary = b;
        VkPipelineBinaryKeyKHR key = {}; key.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR;
        size_t size = 0;
        if (d.getPipelineBinaryData(x.device, &di, &key, &size, nullptr) != VK_SUCCESS || size == 0) { ok = false; continue; }
        store::BinaryBlob blob; blob.keySize = std::min<uint32_t>(key.keySize, store::kMaxBinaryKeySize);
        std::memcpy(blob.key, key.key, store::kMaxBinaryKeySize);
        blob.data.resize(size);
        if (d.getPipelineBinaryData(x.device, &di, &key, &size, blob.data.data()) != VK_SUCCESS) { ok = false; continue; }
        blob.data.resize(size);
        blobs.push_back(std::move(blob));
    }
    for (VkPipelineBinaryKHR b : bins) if (b) d.destroyPipelineBinary(x.device, b, nullptr);
    if (d.releaseCapturedPipelineData) {
        VkReleaseCapturedPipelineDataInfoKHR rel = {}; rel.sType = VK_STRUCTURE_TYPE_RELEASE_CAPTURED_PIPELINE_DATA_INFO_KHR; rel.pipeline = pipeline;
        d.releaseCapturedPipelineData(x.device, &rel, nullptr);
    }
    if (!ok || blobs.empty()) return false;
    const std::vector<uint8_t> file = store::EncodeBinaryPack(id, blobs);
    const std::string path = store::BinaryFilePath(StorageDir(), globalHex, pipelineHex);
    if (!store::WriteFileAtomic(path, file.data(), file.size())) { Log("pipeline binary store failed: %s", path.c_str()); return false; }
    std::lock_guard<std::mutex> lock(x.mutex);
    ++x.binariesStored;
    return true;
}

// Tries <storage>/binaries/<global>/<pipeline>.bin; on success `out` is the created pipeline.
bool CreateFromBinaries(Executor& x, const VkComputePipelineCreateInfo& base, const std::string& globalHex, const std::string& pipelineHex,
                        const store::CacheIdentity& id, VkPipeline& out, double& ms) {
    DeviceState& d = Device();
    std::vector<uint8_t> file;
    if (!store::ReadFile(store::BinaryFilePath(StorageDir(), globalHex, pipelineHex), file)) return false;
    std::vector<store::BinaryBlob> blobs;
    if (!store::DecodeBinaryPack(file, id, blobs)) { std::lock_guard<std::mutex> lock(x.mutex); ++x.binariesLoadFailed; return false; }
    std::vector<VkPipelineBinaryKeyKHR> keys(blobs.size());
    std::vector<VkPipelineBinaryDataKHR> datas(blobs.size());
    for (size_t i = 0; i < blobs.size(); ++i) {
        keys[i] = {}; keys[i].sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR; keys[i].keySize = blobs[i].keySize; std::memcpy(keys[i].key, blobs[i].key, store::kMaxBinaryKeySize);
        datas[i].dataSize = blobs[i].data.size(); datas[i].pData = blobs[i].data.data();
    }
    VkPipelineBinaryKeysAndDataKHR kd = {}; kd.binaryCount = static_cast<uint32_t>(blobs.size()); kd.pPipelineBinaryKeys = keys.data(); kd.pPipelineBinaryData = datas.data();
    VkPipelineBinaryCreateInfoKHR bci = {}; bci.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_CREATE_INFO_KHR; bci.pKeysAndDataInfo = &kd;
    std::vector<VkPipelineBinaryKHR> bins(blobs.size(), VK_NULL_HANDLE);
    VkPipelineBinaryHandlesInfoKHR handles = {}; handles.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_HANDLES_INFO_KHR;
    handles.pipelineBinaryCount = static_cast<uint32_t>(bins.size()); handles.pPipelineBinaries = bins.data();
    const int64_t t0 = MonotonicNs();
    VkResult r = d.createPipelineBinaries(x.device, &bci, nullptr, &handles);
    bool ok = r == VK_SUCCESS;
    if (ok) {
        VkPipelineBinaryInfoKHR info = {}; info.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_INFO_KHR; info.binaryCount = handles.pipelineBinaryCount; info.pPipelineBinaries = bins.data();
        VkComputePipelineCreateInfo cpi = base; cpi.pNext = &info;
        r = vkCreateComputePipelines(x.device, VK_NULL_HANDLE, 1, &cpi, nullptr, &out);
        ok = r == VK_SUCCESS && out != VK_NULL_HANDLE;
        if (r == VK_ERROR_DEVICE_LOST) { std::lock_guard<std::mutex> lock(x.mutex); Quarantine(x, "vkCreateComputePipelines(binary)", r); }
    }
    ms = static_cast<double>(MonotonicNs() - t0) / 1e6;
    for (VkPipelineBinaryKHR b : bins) if (b) d.destroyPipelineBinary(x.device, b, nullptr);
    if (!ok) { { std::lock_guard<std::mutex> lock(x.mutex); ++x.binariesLoadFailed; } out = VK_NULL_HANDLE; Log("pipeline binary %s/%s rejected (%s); falling back to SPIR-V", globalHex.c_str(), pipelineHex.c_str(), VkResultName(r)); }
    return ok;
}

} // namespace

// ---- init / shutdown ---------------------------------------------------------------------------------
bool ResourcesInit(Executor& x) {
    VkDescriptorPoolSize sizes[5] = {
        { VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 4096 }, { VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 256 }, { VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 256 },
        { VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER, 128 }, { VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 256 } };
    VkDescriptorPoolCreateInfo dpi = {}; dpi.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    dpi.flags = VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT; dpi.maxSets = 512; dpi.poolSizeCount = 5; dpi.pPoolSizes = sizes;
    VkResult r = vkCreateDescriptorPool(x.device, &dpi, nullptr, &x.descPool);
    if (r != VK_SUCCESS) { LogError("executor: vkCreateDescriptorPool %s", VkResultName(r)); return false; }

    // Pipeline cache: seeded from disk when the header matches this driver/device (invalid → ignored).
    std::vector<uint8_t> initial;
    x.pipelineCacheLoaded = false; x.pipelineCacheLoadedBytes = 0;
    if (!StorageDir().empty()) {
        std::vector<uint8_t> file;
        if (store::ReadFile(store::CacheFilePath(StorageDir()), file) && store::DecodeCacheFile(file, Identity(), initial)) {
            x.pipelineCacheLoaded = true; x.pipelineCacheLoadedBytes = initial.size();
        } else if (!file.empty()) Log("executor: pipeline-cache.bin ignored (foreign or malformed, %zu bytes)", file.size());
    }
    VkPipelineCacheCreateInfo pci = {}; pci.sType = VK_STRUCTURE_TYPE_PIPELINE_CACHE_CREATE_INFO;
    pci.initialDataSize = initial.size(); pci.pInitialData = initial.empty() ? nullptr : initial.data();
    r = vkCreatePipelineCache(x.device, &pci, nullptr, &x.pipelineCache);
    if (r != VK_SUCCESS && !initial.empty()) {   // driver refused the blob: retry empty
        Log("executor: vkCreatePipelineCache with %zu initial bytes: %s; retrying empty", initial.size(), VkResultName(r));
        pci.initialDataSize = 0; pci.pInitialData = nullptr; x.pipelineCacheLoaded = false; x.pipelineCacheLoadedBytes = 0;
        r = vkCreatePipelineCache(x.device, &pci, nullptr, &x.pipelineCache);
    }
    if (r != VK_SUCCESS) { LogError("executor: vkCreatePipelineCache %s (continuing without cache)", VkResultName(r)); x.pipelineCache = VK_NULL_HANDLE; }

    for (int linear = 0; linear < 2; ++linear) {
        VkSamplerCreateInfo si = {}; si.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        si.magFilter = si.minFilter = linear ? VK_FILTER_LINEAR : VK_FILTER_NEAREST;
        si.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
        si.addressModeU = si.addressModeV = si.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        si.maxLod = VK_LOD_CLAMP_NONE;
        VkSampler* out = linear ? &x.samplerLinear : &x.samplerNearest;
        if (vkCreateSampler(x.device, &si, nullptr, out) != VK_SUCCESS) *out = VK_NULL_HANDLE;
    }
    x.pipelines.clear(); x.importedBuffers.clear(); x.importedTextures.clear();
    return true;
}

void SavePipelineCache(Executor& x) {
    if (x.pipelineCache == VK_NULL_HANDLE || StorageDir().empty()) return;
    size_t bytes = 0;
    if (vkGetPipelineCacheData(x.device, x.pipelineCache, &bytes, nullptr) != VK_SUCCESS || bytes == 0) return;
    std::vector<uint8_t> data(bytes);
    if (vkGetPipelineCacheData(x.device, x.pipelineCache, &bytes, data.data()) != VK_SUCCESS) return;
    data.resize(bytes);
    const std::vector<uint8_t> file = store::EncodeCacheFile(Identity(), data.data(), data.size());
    if (store::WriteFileAtomic(store::CacheFilePath(StorageDir()), file.data(), file.size())) {
        std::lock_guard<std::mutex> lock(x.mutex);
        x.pipelineCacheSavedBytes = data.size();
    } else Log("executor: pipeline cache save failed (%s)", store::CacheFilePath(StorageDir()).c_str());
}

void ResourcesShutdown(Executor& x) {
    // Caller: after vkDeviceWaitIdle, x.mutex held. Module-owned pipelines/buffers should already be gone
    // (device hooks); whatever remains is destroyed here so the device can go down cleanly.
    for (auto& kv : x.importedTextures) if (kv.second.view) vkDestroyImageView(x.device, kv.second.view, nullptr);
    x.importedTextures.clear(); x.importedBuffers.clear();
    for (auto& kv : x.pipelines) {
        if (kv.second.module) vkDestroyShaderModule(x.device, kv.second.module, nullptr);
        vkDestroyPipeline(x.device, kv.first, nullptr);
    }
    if (!x.pipelines.empty()) Log("executor: %zu pipelines still registered at shutdown (destroyed)", x.pipelines.size());
    x.pipelines.clear();
    if (x.samplerNearest) { vkDestroySampler(x.device, x.samplerNearest, nullptr); x.samplerNearest = VK_NULL_HANDLE; }
    if (x.samplerLinear) { vkDestroySampler(x.device, x.samplerLinear, nullptr); x.samplerLinear = VK_NULL_HANDLE; }
    if (x.descPool) { vkDestroyDescriptorPool(x.device, x.descPool, nullptr); x.descPool = VK_NULL_HANDLE; }
    if (x.pipelineCache) { vkDestroyPipelineCache(x.device, x.pipelineCache, nullptr); x.pipelineCache = VK_NULL_HANDLE; }
}

void ResourcesJson(Executor& x, std::string& m) {
    JsonWriter w;
    w.BeginObject();
    w.KV("pipelines", static_cast<uint64_t>(x.pipelines.size())); w.KV("pipelinesCreated", x.pipelinesCreated); w.KV("pipelinesFromBinary", x.pipelinesFromBinary);
    w.KV("binariesStored", x.binariesStored); w.KV("binariesLoadFailed", x.binariesLoadFailed);
    w.KV("pipelineCacheLoaded", x.pipelineCacheLoaded); w.KV("pipelineCacheLoadedBytes", x.pipelineCacheLoadedBytes); w.KV("pipelineCacheSavedBytes", x.pipelineCacheSavedBytes);
    w.KV("buffersLive", x.buffersLive); w.KV("bufferBytesLive", x.bufferBytesLive); w.KV("buffersCreated", x.buffersCreated); w.KV("buffersDestroyed", x.buffersDestroyed);
    w.KV("importedBuffers", static_cast<uint64_t>(x.importedBuffers.size())); w.KV("importedTextures", static_cast<uint64_t>(x.importedTextures.size()));
    w.KV("garbagePending", static_cast<uint64_t>(x.garbage.Pending()));
    w.Key("pipelineList"); w.BeginArray();
    for (const auto& kv : x.pipelines) { w.BeginObject(); w.KV("name", kv.second.name); w.KV("fromBinary", kv.second.fromBinary); w.KV("createMs", kv.second.createMs, 4); w.EndObject(); }
    w.EndArray();
    w.EndObject();
    m += "\"resources\":" + w.Take();
}

} // namespace exec

// ---- public API (fs_executor.h) ---------------------------------------------------------------------
using exec::Executor; using exec::X;

bool CreateBuffer(Buffer& out, VkDeviceSize size, VkBufferUsageFlags usage, bool hostVisible, const char* name) {
    Executor& x = X();
    out = Buffer{};
    if (!x.vkReady.load(std::memory_order_acquire) || size == 0) return false;
    const bool bdaEnabled = x.bdaEnabled && x.getBufferDeviceAddress != nullptr;
    VkBufferCreateInfo bi = {}; bi.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bi.size = size; bi.usage = usage | (bdaEnabled ? VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT : 0u); bi.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    VkBuffer buffer = VK_NULL_HANDLE;
    VkResult r = vkCreateBuffer(x.device, &bi, nullptr, &buffer);
    if (r != VK_SUCCESS) { LogError("CreateBuffer(%s, %llu): vkCreateBuffer %s", name ? name : "", static_cast<unsigned long long>(size), VkResultName(r)); return false; }
    VkMemoryRequirements mr = {}; vkGetBufferMemoryRequirements(x.device, buffer, &mr);
    uint32_t type = UINT32_MAX;
    if (hostVisible) {
        type = FindMemoryType(mr.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT | VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        if (type == UINT32_MAX) type = FindMemoryType(mr.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
    } else {
        type = FindMemoryType(mr.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        if (type == UINT32_MAX) type = FindMemoryType(mr.memoryTypeBits, 0);
    }
    if (type == UINT32_MAX) { vkDestroyBuffer(x.device, buffer, nullptr); LogError("CreateBuffer(%s): no memory type", name ? name : ""); return false; }
    VkMemoryAllocateFlagsInfo flags = {}; flags.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_FLAGS_INFO; flags.flags = VK_MEMORY_ALLOCATE_DEVICE_ADDRESS_BIT;
    VkMemoryAllocateInfo mai = {}; mai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO; mai.pNext = bdaEnabled ? &flags : nullptr;
    mai.allocationSize = mr.size; mai.memoryTypeIndex = type;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    r = vkAllocateMemory(x.device, &mai, nullptr, &memory);
    if (r != VK_SUCCESS) { vkDestroyBuffer(x.device, buffer, nullptr); LogError("CreateBuffer(%s, %llu): vkAllocateMemory %s", name ? name : "", static_cast<unsigned long long>(size), VkResultName(r)); return false; }
    r = vkBindBufferMemory(x.device, buffer, memory, 0);
    if (r != VK_SUCCESS) { vkFreeMemory(x.device, memory, nullptr); vkDestroyBuffer(x.device, buffer, nullptr); LogError("CreateBuffer(%s): vkBindBufferMemory %s", name ? name : "", VkResultName(r)); return false; }
    void* mapped = nullptr;
    if (hostVisible && vkMapMemory(x.device, memory, 0, VK_WHOLE_SIZE, 0, &mapped) != VK_SUCCESS) mapped = nullptr;
    out.buffer = buffer; out.memory = memory; out.size = size; out.mapped = mapped;
    if (bdaEnabled) {
        VkBufferDeviceAddressInfo ai = {}; ai.sType = VK_STRUCTURE_TYPE_BUFFER_DEVICE_ADDRESS_INFO; ai.buffer = buffer;
        out.address = x.getBufferDeviceAddress(x.device, &ai);
    }
    std::lock_guard<std::mutex> lock(x.mutex);
    ++x.buffersCreated; ++x.buffersLive; x.bufferBytesLive += size;
    return true;
}

void DestroyBuffer(Buffer& b) {
    Executor& x = X();
    if (b.buffer == VK_NULL_HANDLE && b.memory == VK_NULL_HANDLE) { b = Buffer{}; return; }
    std::lock_guard<std::mutex> lock(x.mutex);
    if (!x.vkReady.load(std::memory_order_acquire)) { b = Buffer{}; return; }   // device already gone (objects freed with it)
    if (b.mapped) vkUnmapMemory(x.device, b.memory);
    exec::GarbageItem g; g.buffer = b.buffer; g.memory = b.memory;
    exec::PushGarbageLocked(x, g);
    ++x.buffersDestroyed; if (x.buffersLive) --x.buffersLive; x.bufferBytesLive -= std::min<uint64_t>(x.bufferBytesLive, b.size);
    b = Buffer{};
}

bool CreateComputePipeline(Pipeline& out, const uint32_t* spirv, size_t spirvWords, uint32_t pushBytes,
                           const VkDescriptorSetLayoutBinding* bindings, uint32_t bindingCount, const char* name) {
    Executor& x = X();
    out = Pipeline{};
    const char* nm = name ? name : "";
    if (!x.vkReady.load(std::memory_order_acquire) || spirv == nullptr || spirvWords < 5) return false;
    if (pushBytes > exec::kMaxPushBytes) { LogError("CreateComputePipeline(%s): push constants %u > %u", nm, pushBytes, exec::kMaxPushBytes); return false; }
    if (spirvWords * 4 > 128u * 1024u) { LogError("CreateComputePipeline(%s): SPIR-V %zu bytes over the kernel envelope (contract §15.6)", nm, spirvWords * 4); return false; }
    DeviceState& d = Device();
    VkShaderModuleCreateInfo smi = {}; smi.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO; smi.codeSize = spirvWords * 4; smi.pCode = spirv;
    VkShaderModule module = VK_NULL_HANDLE;
    VkResult r = vkCreateShaderModule(x.device, &smi, nullptr, &module);
    if (r != VK_SUCCESS) { LogError("CreateComputePipeline(%s): vkCreateShaderModule %s", nm, VkResultName(r)); return false; }
    std::vector<VkDescriptorSetLayoutBinding> binds(bindings, bindings + bindingCount);
    for (auto& b : binds) { b.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT; if (b.descriptorCount == 0) b.descriptorCount = 1; }
    VkDescriptorSetLayoutCreateInfo dli = {}; dli.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO; dli.bindingCount = bindingCount; dli.pBindings = binds.data();
    VkDescriptorSetLayout dsl = VK_NULL_HANDLE;
    r = vkCreateDescriptorSetLayout(x.device, &dli, nullptr, &dsl);
    if (r != VK_SUCCESS) { vkDestroyShaderModule(x.device, module, nullptr); LogError("CreateComputePipeline(%s): vkCreateDescriptorSetLayout %s", nm, VkResultName(r)); return false; }
    VkPushConstantRange pcr = {}; pcr.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT; pcr.offset = 0; pcr.size = pushBytes;
    VkPipelineLayoutCreateInfo pli = {}; pli.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    pli.setLayoutCount = 1; pli.pSetLayouts = &dsl; pli.pushConstantRangeCount = pushBytes ? 1u : 0u; pli.pPushConstantRanges = pushBytes ? &pcr : nullptr;
    VkPipelineLayout layout = VK_NULL_HANDLE;
    r = vkCreatePipelineLayout(x.device, &pli, nullptr, &layout);
    if (r != VK_SUCCESS) { vkDestroyDescriptorSetLayout(x.device, dsl, nullptr); vkDestroyShaderModule(x.device, module, nullptr); LogError("CreateComputePipeline(%s): vkCreatePipelineLayout %s", nm, VkResultName(r)); return false; }

    VkPipelineShaderStageCreateInfo stage = {}; stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    stage.stage = VK_SHADER_STAGE_COMPUTE_BIT; stage.module = module; stage.pName = "main";
    VkComputePipelineCreateInfo cpi = {}; cpi.sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO; cpi.stage = stage; cpi.layout = layout;

    VkPipeline pipeline = VK_NULL_HANDLE;
    bool fromBinary = false; double ms = 0.0;
    const bool binaries = d.pipelineBinaryEnabled && d.getPipelineKey && d.createPipelineBinaries && d.getPipelineBinaryData && d.destroyPipelineBinary && !StorageDir().empty();
    std::string globalHex, pipelineHex;
    if (binaries) {
        VkPipelineBinaryKeyKHR gk = {}; gk.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR;
        VkPipelineCreateInfoKHR ki = {}; ki.sType = VK_STRUCTURE_TYPE_PIPELINE_CREATE_INFO_KHR; ki.pNext = &cpi;
        VkPipelineBinaryKeyKHR pk = {}; pk.sType = VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR;
        if (d.getPipelineKey(x.device, nullptr, &gk) == VK_SUCCESS && d.getPipelineKey(x.device, &ki, &pk) == VK_SUCCESS && gk.keySize && pk.keySize) {
            globalHex = store::HexString(gk.key, std::min<uint32_t>(gk.keySize, store::kMaxBinaryKeySize));
            pipelineHex = store::HexString(pk.key, std::min<uint32_t>(pk.keySize, store::kMaxBinaryKeySize));
            fromBinary = exec::CreateFromBinaries(x, cpi, globalHex, pipelineHex, exec::Identity(), pipeline, ms);
        }
    }
    if (!fromBinary) {
        const bool capture = binaries && !globalHex.empty();
        VkPipelineCreateFlags2CreateInfoKHR flags2 = {}; flags2.sType = VK_STRUCTURE_TYPE_PIPELINE_CREATE_FLAGS_2_CREATE_INFO_KHR; flags2.flags = VK_PIPELINE_CREATE_2_CAPTURE_DATA_BIT_KHR;
        cpi.pNext = capture ? &flags2 : nullptr;
        const int64_t t0 = MonotonicNs();
        r = vkCreateComputePipelines(x.device, capture ? VK_NULL_HANDLE : x.pipelineCache, 1, &cpi, nullptr, &pipeline);
        ms = static_cast<double>(MonotonicNs() - t0) / 1e6;
        if (r != VK_SUCCESS && capture) {   // never fail on the binary path: retry plain
            Log("CreateComputePipeline(%s): capture create %s; retrying without capture", nm, VkResultName(r));
            cpi.pNext = nullptr; pipeline = VK_NULL_HANDLE;
            r = vkCreateComputePipelines(x.device, x.pipelineCache, 1, &cpi, nullptr, &pipeline);
        } else if (r == VK_SUCCESS && capture) {
            exec::StorePipelineBinaries(x, pipeline, globalHex, pipelineHex, exec::Identity());
        }
        if (r != VK_SUCCESS) {
            if (r == VK_ERROR_DEVICE_LOST) { std::lock_guard<std::mutex> lock(x.mutex); exec::Quarantine(x, "vkCreateComputePipelines", r); }
            vkDestroyPipelineLayout(x.device, layout, nullptr); vkDestroyDescriptorSetLayout(x.device, dsl, nullptr); vkDestroyShaderModule(x.device, module, nullptr);
            LogError("CreateComputePipeline(%s): vkCreateComputePipelines %s", nm, VkResultName(r));
            return false;
        }
    }
    VkDescriptorSet set = VK_NULL_HANDLE;
    {
        std::lock_guard<std::mutex> lock(x.mutex);   // descriptor pool is externally synchronized
        VkDescriptorSetAllocateInfo dsi = {}; dsi.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO; dsi.descriptorPool = x.descPool; dsi.descriptorSetCount = 1; dsi.pSetLayouts = &dsl;
        r = bindingCount ? vkAllocateDescriptorSets(x.device, &dsi, &set) : VK_SUCCESS;
        if (r == VK_SUCCESS) {
            exec::PipelineRecord rec; rec.name = nm; rec.module = module; rec.bindings = binds; rec.fromBinary = fromBinary; rec.createMs = ms;
            x.pipelines[pipeline] = std::move(rec);
            ++x.pipelinesCreated; if (fromBinary) ++x.pipelinesFromBinary;
            out.name = x.pipelines[pipeline].name.c_str();
        }
    }
    if (r != VK_SUCCESS) {
        vkDestroyPipeline(x.device, pipeline, nullptr); vkDestroyPipelineLayout(x.device, layout, nullptr); vkDestroyDescriptorSetLayout(x.device, dsl, nullptr); vkDestroyShaderModule(x.device, module, nullptr);
        LogError("CreateComputePipeline(%s): vkAllocateDescriptorSets %s", nm, VkResultName(r));
        out = Pipeline{}; return false;
    }
    out.pipeline = pipeline; out.layout = layout; out.setLayout = dsl; out.set = set; out.pushBytes = pushBytes;
    Log("pipeline %s: %s in %.3f ms (bindings=%u push=%u)", nm, fromBinary ? "from binary" : (x.pipelineCacheLoaded ? "compiled (cache seeded)" : "compiled"), ms, bindingCount, pushBytes);
    return true;
}

bool BindBuffer(Pipeline& p, uint32_t binding, const Buffer& b, VkDeviceSize offset, VkDeviceSize range) {
    Executor& x = X();
    if (!x.vkReady.load(std::memory_order_acquire) || p.set == VK_NULL_HANDLE || b.buffer == VK_NULL_HANDLE) return false;
    VkDescriptorType type = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    {
        std::lock_guard<std::mutex> lock(x.mutex);
        auto it = x.pipelines.find(p.pipeline);
        if (it == x.pipelines.end()) return false;
        bool found = false; type = exec::BindingType(it->second, binding, found);
        if (!found) { LogError("BindBuffer(%s): binding %u not declared", it->second.name.c_str(), binding); return false; }
    }
    if (type != VK_DESCRIPTOR_TYPE_STORAGE_BUFFER && type != VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER) return false;
    VkDescriptorBufferInfo dbi = {}; dbi.buffer = b.buffer; dbi.offset = offset; dbi.range = range;
    VkWriteDescriptorSet wds = {}; wds.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET; wds.dstSet = p.set; wds.dstBinding = binding; wds.descriptorCount = 1; wds.descriptorType = type; wds.pBufferInfo = &dbi;
    vkUpdateDescriptorSets(x.device, 1, &wds, 0, nullptr);
    return true;
}

bool BindImage(Pipeline& p, uint32_t binding, VkImageView view, VkImageLayout layout, VkSampler sampler) {
    Executor& x = X();
    if (!x.vkReady.load(std::memory_order_acquire) || p.set == VK_NULL_HANDLE || view == VK_NULL_HANDLE) return false;
    VkDescriptorType type;
    {
        std::lock_guard<std::mutex> lock(x.mutex);
        auto it = x.pipelines.find(p.pipeline);
        if (it == x.pipelines.end()) return false;
        bool found = false; type = exec::BindingType(it->second, binding, found);
        if (!found) return false;
    }
    if (type != VK_DESCRIPTOR_TYPE_STORAGE_IMAGE && type != VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE && type != VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER) return false;
    if (type == VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER && sampler == VK_NULL_HANDLE) sampler = x.samplerNearest;
    VkDescriptorImageInfo dii = {}; dii.sampler = sampler; dii.imageView = view; dii.imageLayout = layout;
    VkWriteDescriptorSet wds = {}; wds.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET; wds.dstSet = p.set; wds.dstBinding = binding; wds.descriptorCount = 1; wds.descriptorType = type; wds.pImageInfo = &dii;
    vkUpdateDescriptorSets(x.device, 1, &wds, 0, nullptr);
    return true;
}

VkSampler ExecSampler(bool linear) { return linear ? X().samplerLinear : X().samplerNearest; }

void DestroyPipeline(Pipeline& p) {
    Executor& x = X();
    if (p.pipeline == VK_NULL_HANDLE) { p = Pipeline{}; return; }
    std::lock_guard<std::mutex> lock(x.mutex);
    if (!x.vkReady.load(std::memory_order_acquire)) { p = Pipeline{}; return; }
    exec::GarbageItem g; g.pipeline = p.pipeline; g.layout = p.layout; g.setLayout = p.setLayout; g.set = p.set;
    auto it = x.pipelines.find(p.pipeline);
    if (it != x.pipelines.end()) { g.module = it->second.module; x.pipelines.erase(it); }
    exec::PushGarbageLocked(x, g);
    p = Pipeline{};
}

bool ImportUnityBuffer(void* unityNativePtr, Buffer& out) {
    Executor& x = X();
    out = Buffer{};
    if (unityNativePtr == nullptr || !x.vkReady.load(std::memory_order_acquire) || !x.inRenderEvent.load(std::memory_order_acquire)) return false;
    UnityVulkanBuffer ub = {};
    bool fresh = false;
    {
        std::lock_guard<std::mutex> lock(x.mutex);
        auto it = x.importedBuffers.find(unityNativePtr);
        if (it != x.importedBuffers.end()) {
            // Cheap re-validation (no barrier): Unity may have reallocated the backing VkBuffer.
            UnityVulkanBuffer probe = {};
            if (exec::AccessBufferUnity(unityNativePtr, kUnityVulkanResourceAccess_ObserveOnly, &probe) && probe.buffer == it->second.unity.buffer && probe.sizeInBytes == it->second.unity.sizeInBytes) {
                ub = it->second.unity;
            } else fresh = true;
        } else fresh = true;
    }
    if (fresh) {
        if (!exec::AccessBufferUnity(unityNativePtr, kUnityVulkanResourceAccess_PipelineBarrier, &ub) || ub.buffer == VK_NULL_HANDLE) return false;
        std::lock_guard<std::mutex> lock(x.mutex);
        exec::ImportedBuffer& ib = x.importedBuffers[unityNativePtr];
        ib.unity = ub; ib.frame = x.unityCurrentFrame.load(std::memory_order_relaxed); ++ib.imports;
    }
    exec::RefreshRecordingState(x);   // any Access* call invalidates Unity's recording state
    out.buffer = ub.buffer; out.memory = ub.memory.memory; out.size = ub.sizeInBytes; out.mapped = ub.memory.mapped;
    if ((ub.usage & VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT) && x.getBufferDeviceAddress) {
        VkBufferDeviceAddressInfo ai = {}; ai.sType = VK_STRUCTURE_TYPE_BUFFER_DEVICE_ADDRESS_INFO; ai.buffer = ub.buffer;
        out.address = x.getBufferDeviceAddress(x.device, &ai);
    }
    return true;
}

bool ImportUnityTexture(void* unityNativeTexturePtr, VkImage& image, VkImageView& view, VkFormat& fmt, uint32_t& w, uint32_t& h, uint32_t& layers) {
    Executor& x = X();
    image = VK_NULL_HANDLE; view = VK_NULL_HANDLE; fmt = VK_FORMAT_UNDEFINED; w = h = layers = 0;
    if (unityNativeTexturePtr == nullptr || !x.vkReady.load(std::memory_order_acquire) || !x.inRenderEvent.load(std::memory_order_acquire)) return false;
    UnityVulkanImage ui = {};
    // Barrier every call: Unity re-transitions depth targets between frames; the view stays cached.
    if (!exec::AccessTextureUnity(unityNativeTexturePtr, kUnityVulkanResourceAccess_PipelineBarrier, &ui) || ui.image == VK_NULL_HANDLE) return false;
    exec::RefreshRecordingState(x);
    std::lock_guard<std::mutex> lock(x.mutex);
    exec::ImportedTexture& it = x.importedTextures[unityNativeTexturePtr];
    if (it.view != VK_NULL_HANDLE && (it.unity.image != ui.image || it.unity.format != ui.format || it.unity.layers != ui.layers)) {
        exec::GarbageItem g; g.view = it.view; exec::PushGarbageLocked(x, g);
        it.view = VK_NULL_HANDLE;
    }
    if (it.view == VK_NULL_HANDLE) {
        VkImageViewCreateInfo vi = {}; vi.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        vi.image = ui.image; vi.viewType = ui.layers > 1 ? VK_IMAGE_VIEW_TYPE_2D_ARRAY : VK_IMAGE_VIEW_TYPE_2D; vi.format = ui.format;
        VkImageAspectFlags aspect = ui.aspect ? ui.aspect : VK_IMAGE_ASPECT_COLOR_BIT;
        if (aspect & VK_IMAGE_ASPECT_DEPTH_BIT) aspect = VK_IMAGE_ASPECT_DEPTH_BIT;   // a view for shader reads: depth only
        vi.subresourceRange.aspectMask = aspect; vi.subresourceRange.baseMipLevel = 0; vi.subresourceRange.levelCount = 1;
        vi.subresourceRange.baseArrayLayer = 0; vi.subresourceRange.layerCount = ui.layers > 0 ? static_cast<uint32_t>(ui.layers) : 1u;
        VkResult r = vkCreateImageView(x.device, &vi, nullptr, &it.view);
        if (r != VK_SUCCESS) { it.view = VK_NULL_HANDLE; LogError("ImportUnityTexture: vkCreateImageView %s", VkResultName(r)); return false; }
    }
    it.unity = ui; it.frame = x.unityCurrentFrame.load(std::memory_order_relaxed); ++it.imports;
    image = ui.image; view = it.view; fmt = ui.format; w = ui.extent.width; h = ui.extent.height; layers = ui.layers > 0 ? static_cast<uint32_t>(ui.layers) : 1u;
    return true;
}

} // namespace fs
