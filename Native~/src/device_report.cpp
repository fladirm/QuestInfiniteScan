// Device report JSON (FsNative_GetDeviceReportJson): physical device identity, queue families,
// injection outcome, extensions, limits, features, AHB import probe result.
#include "plugin_state.h"
#include "json_writer.h"
#include "log.h"
#include <cstdio>

namespace fs {

namespace {

std::string DecodeDriverVersion(uint32_t vendorId, uint32_t v) {
    char b[64];
    if (vendorId == 0x10DE) // NVIDIA
        std::snprintf(b, sizeof(b), "%u.%u.%u.%u", (v >> 22) & 0x3ff, (v >> 14) & 0xff, (v >> 6) & 0xff, v & 0x3f);
    else if (vendorId == 0x8086) // Intel
        std::snprintf(b, sizeof(b), "%u.%u", v >> 14, v & 0x3fff);
    else // Vulkan convention (Qualcomm/Adreno reports major.minor.patch this way)
        std::snprintf(b, sizeof(b), "%u.%u.%u", VK_API_VERSION_MAJOR(v), VK_API_VERSION_MINOR(v), VK_API_VERSION_PATCH(v));
    return b;
}

std::string ApiVersionString(uint32_t v) {
    char b[48];
    std::snprintf(b, sizeof(b), "%u.%u.%u", VK_API_VERSION_MAJOR(v), VK_API_VERSION_MINOR(v), VK_API_VERSION_PATCH(v));
    return b;
}

std::string HexBytes(const uint8_t* bytes, size_t n) {
    std::string s; s.reserve(n * 2);
    static const char* hex = "0123456789abcdef";
    for (size_t i = 0; i < n; ++i) { s += hex[bytes[i] >> 4]; s += hex[bytes[i] & 15]; }
    return s;
}

void WriteFeatureSet(JsonWriter& w, const FeatureSet& f) {
    w.BeginObject();
    w.KV("timelineSemaphore", f.timelineSemaphore);
    w.KV("bufferDeviceAddress", f.bufferDeviceAddress);
    w.KV("hostQueryReset", f.hostQueryReset);
    w.KV("shaderFloat16", f.shaderFloat16);
    w.KV("shaderInt8", f.shaderInt8);
    w.KV("storageBuffer16BitAccess", f.storageBuffer16BitAccess);
    w.KV("uniformAndStorageBuffer16BitAccess", f.uniformAndStorageBuffer16BitAccess);
    w.KV("synchronization2", f.synchronization2);
    w.KV("pipelineBinaries", f.pipelineBinaries);
    w.KV("samplerYcbcrConversion", f.samplerYcbcrConversion);
    w.EndObject();
}

} // namespace

std::string BuildDeviceReportJson() {
    InterceptState& st = Intercept();
    DeviceState& d = Device();
    std::lock_guard<std::mutex> lock(st.mutex);
    JsonWriter w;
    w.BeginObject();
    w.KV("abi", static_cast<int32_t>(1));
    w.KV("vulkanReady", d.ready.load(std::memory_order_acquire));
    w.KV("initError", d.initError);
    w.Key("intercept"); w.BeginObject();
    w.KV("registered", st.interceptRegistered);
    w.KV("v2", st.usedV2);
    w.KV("createDeviceSeen", st.createDeviceSeen);
    w.KV("instanceApiVersion", ApiVersionString(st.instanceApiVersion));
    w.KV("createResult", static_cast<int32_t>(st.createResult));
    w.EndObject();

    const VkPhysicalDeviceProperties& p = st.propsValid ? st.props : d.properties;
    w.KV("deviceName", p.deviceName);
    w.KV("driverVersionRaw", p.driverVersion);
    w.KV("driverVersion", DecodeDriverVersion(p.vendorID, p.driverVersion));
    w.KV("apiVersionRaw", p.apiVersion);
    w.KV("apiVersion", ApiVersionString(p.apiVersion));
    w.KVHex("vendorID", p.vendorID);
    w.KVHex("deviceID", p.deviceID);
    w.KV("deviceType", static_cast<int32_t>(p.deviceType));
    w.KV("pipelineCacheUUID", HexBytes(p.pipelineCacheUUID, VK_UUID_SIZE));
    if (st.driverPropsValid) {
        w.Key("driver"); w.BeginObject();
        w.KV("driverID", static_cast<int32_t>(st.driverProps.driverID));
        w.KV("driverName", st.driverProps.driverName);
        w.KV("driverInfo", st.driverProps.driverInfo);
        char conf[64];
        std::snprintf(conf, sizeof(conf), "%u.%u.%u.%u", st.driverProps.conformanceVersion.major, st.driverProps.conformanceVersion.minor,
            st.driverProps.conformanceVersion.subminor, st.driverProps.conformanceVersion.patch);
        w.KV("conformanceVersion", conf);
        w.EndObject();
    }

    w.Key("queueFamilies"); w.BeginArray();
    for (const auto& f : st.caps.families) {
        w.BeginObject();
        w.KV("index", f.index);
        w.KV("flags", f.flags);
        w.KV("graphics", (f.flags & VK_QUEUE_GRAPHICS_BIT) != 0);
        w.KV("compute", (f.flags & VK_QUEUE_COMPUTE_BIT) != 0);
        w.KV("transfer", (f.flags & VK_QUEUE_TRANSFER_BIT) != 0);
        w.KV("protected", (f.flags & VK_QUEUE_PROTECTED_BIT) != 0);
        w.KV("count", f.count);
        w.KV("timestampValidBits", f.timestampValidBits);
        if (f.index < st.familyGlobalPriorities.size()) {
            w.Key("globalPriorities"); w.BeginArray();
            for (int32_t pr : st.familyGlobalPriorities[f.index]) w.Int(pr);
            w.EndArray();
        }
        w.EndObject();
    }
    w.EndArray();
    w.KV("globalPriorityQuerySupported", st.globalPriorityQuerySupported);

    w.Key("unity"); w.BeginObject();
    w.KV("queueFamily", d.ready.load() ? d.instance.queueFamilyIndex : st.unityQueueFamily);
    w.KV("queueIndex", static_cast<int32_t>(0));
    w.KV("requestedQueueCreateInfos", st.unityQueueCreateInfoCount);
    w.KV("requestedQueueCount", st.unityQueueCount);
    w.KV("requestedPriority0", static_cast<double>(st.unityQueuePriority0));
    w.Key("pNextChain"); w.BeginArray(); for (uint32_t t : st.unityChainTypes) w.UInt(t); w.EndArray();
    w.KV("pipelineCache", d.instance.pipelineCache != VK_NULL_HANDLE);
    w.EndObject();

    w.Key("injection"); w.BeginObject();
    w.KV("attempted", st.injectionAttempted);
    w.KV("succeeded", st.injectionSucceeded);
    w.KV("globalPriorityOption", st.globalPriorityOptionEnabled);
    w.KV("globalPriorityUsed", st.globalPriorityUsed);
    w.KV("path", st.path);
    w.KV("family", st.injectedFamily == UINT32_MAX ? static_cast<int32_t>(-1) : static_cast<int32_t>(st.injectedFamily));
    w.KV("index", st.injectedIndex == UINT32_MAX ? static_cast<int32_t>(-1) : static_cast<int32_t>(st.injectedIndex));
    w.KV("scannerQueueAcquired", d.scannerQueue != VK_NULL_HANDLE);
    w.KV("scannerQueueAliasesUnity", d.scannerQueueAliasesUnity);
    w.Key("attempts"); w.BeginArray();
    for (const auto& a : st.attempts) {
        w.BeginObject(); w.KV("variant", a.variant); w.KV("result", static_cast<int32_t>(a.result)); w.KV("resultName", VkResultName(a.result));
        w.KV("injected", a.injected); w.KV("globalPriority", a.globalPriority); w.EndObject();
    }
    w.EndArray();
    w.Key("notes"); w.BeginArray(); for (const auto& n : st.finalPatch.notes) w.String(n); w.EndArray();
    w.KV("chainFullyCopied", st.finalPatch.chainFullyCopied);
    w.EndObject();

    w.Key("extensions"); w.BeginObject();
    w.Key("available"); w.BeginArray(); for (const auto& e : st.caps.availableExtensions) w.String(e); w.EndArray();
    w.Key("unity"); w.BeginArray(); for (const auto& e : st.unityExtensions) w.String(e); w.EndArray();
    w.Key("added"); w.BeginArray(); for (const auto& e : st.finalPatch.addedExtensions) w.String(e); w.EndArray();
    w.Key("enabled"); w.BeginArray(); for (const auto& e : st.finalPatch.enabledExtensions) w.String(e); w.EndArray();
    w.Key("wanted"); w.BeginArray(); for (const char* e : WantedDeviceExtensions()) w.String(e); w.EndArray();
    w.KV("calibratedTimestamps", st.caps.HasExtension(VK_KHR_CALIBRATED_TIMESTAMPS_EXTENSION_NAME) || st.caps.HasExtension(VK_EXT_CALIBRATED_TIMESTAMPS_EXTENSION_NAME));
    w.KV("globalPriority", st.caps.HasExtension(VK_KHR_GLOBAL_PRIORITY_EXTENSION_NAME) || st.caps.HasExtension(VK_EXT_GLOBAL_PRIORITY_EXTENSION_NAME));
    w.EndObject();

    const VkPhysicalDeviceLimits& l = p.limits;
    w.Key("limits"); w.BeginObject();
    w.KV("timestampPeriod", static_cast<double>(l.timestampPeriod), 9);
    w.KV("timestampComputeAndGraphics", l.timestampComputeAndGraphics != VK_FALSE);
    w.KV("timestampValidBitsUnityFamily", d.timestampValidBits);
    w.KV("maxComputeWorkGroupInvocations", l.maxComputeWorkGroupInvocations);
    w.KV("maxComputeSharedMemorySize", l.maxComputeSharedMemorySize);
    w.Key("maxComputeWorkGroupCount"); w.BeginArray(); for (int i = 0; i < 3; ++i) w.UInt(l.maxComputeWorkGroupCount[i]); w.EndArray();
    w.Key("maxComputeWorkGroupSize"); w.BeginArray(); for (int i = 0; i < 3; ++i) w.UInt(l.maxComputeWorkGroupSize[i]); w.EndArray();
    w.KV("maxPerStageDescriptorStorageBuffers", l.maxPerStageDescriptorStorageBuffers);
    w.KV("maxPerStageDescriptorStorageImages", l.maxPerStageDescriptorStorageImages);
    w.KV("maxPushConstantsSize", l.maxPushConstantsSize);
    w.KV("maxStorageBufferRange", l.maxStorageBufferRange);
    w.KV("maxBoundDescriptorSets", l.maxBoundDescriptorSets);
    w.KV("maxMemoryAllocationCount", l.maxMemoryAllocationCount);
    w.KV("minStorageBufferOffsetAlignment", static_cast<uint64_t>(l.minStorageBufferOffsetAlignment));
    w.KV("nonCoherentAtomSize", static_cast<uint64_t>(l.nonCoherentAtomSize));
    w.KV("maxImageDimension2D", l.maxImageDimension2D);
    w.EndObject();

    w.Key("featuresSupported"); WriteFeatureSet(w, st.caps.supported);
    w.Key("featuresEnabled"); WriteFeatureSet(w, st.createResult == VK_SUCCESS ? st.finalPatch.requested : FeatureSet{});
    w.Key("features12"); w.BeginObject();
    w.KV("timelineSemaphore", st.finalPatch.requested.timelineSemaphore);
    w.KV("bufferDeviceAddress", st.finalPatch.requested.bufferDeviceAddress);
    w.KV("hostQueryReset", st.finalPatch.requested.hostQueryReset);
    w.KV("shaderFloat16", st.finalPatch.requested.shaderFloat16);
    w.KV("shaderInt8", st.finalPatch.requested.shaderInt8);
    w.EndObject();
    w.KV("sync2", st.finalPatch.requested.synchronization2);
    w.KV("pipelineBinary", st.finalPatch.requested.pipelineBinaries);
    w.KV("ycbcr", st.finalPatch.requested.samplerYcbcrConversion);
    w.KV("fp16", st.finalPatch.requested.shaderFloat16);
    w.KV("int8", st.finalPatch.requested.shaderInt8);
    w.Key("storage16Bit"); w.BeginObject();
    w.KV("storageBuffer", st.finalPatch.requested.storageBuffer16BitAccess);
    w.KV("uniformAndStorageBuffer", st.finalPatch.requested.uniformAndStorageBuffer16BitAccess);
    w.EndObject();
    if (st.pipelineBinaryPropsValid) {
        w.Key("pipelineBinaryProperties"); w.BeginObject();
        w.KV("internalCache", st.pipelineBinaryProps.pipelineBinaryInternalCache != VK_FALSE);
        w.KV("internalCacheControl", st.pipelineBinaryProps.pipelineBinaryInternalCacheControl != VK_FALSE);
        w.KV("prefersInternalCache", st.pipelineBinaryProps.pipelineBinaryPrefersInternalCache != VK_FALSE);
        w.KV("precompiledInternalCache", st.pipelineBinaryProps.pipelineBinaryPrecompiledInternalCache != VK_FALSE);
        w.KV("compressedData", st.pipelineBinaryProps.pipelineBinaryCompressedData != VK_FALSE);
        w.EndObject();
    }
    w.Key("procs"); w.BeginObject();
    w.KV("vkGetPipelineKeyKHR", d.getPipelineKey != nullptr);
    w.KV("vkCreatePipelineBinariesKHR", d.createPipelineBinaries != nullptr);
    w.KV("vkGetPipelineBinaryDataKHR", d.getPipelineBinaryData != nullptr);
    w.KV("vkResetQueryPool", d.resetQueryPool != nullptr);
    w.KV("vkGetAndroidHardwareBufferPropertiesANDROID", d.getAhbProperties != nullptr);
    w.KV("vkCreateSamplerYcbcrConversion", d.createYcbcrConversion != nullptr);
    w.EndObject();
    w.RawValue("ahbImport", AhbImportJson());
    w.EndObject();
    return w.Take();
}

} // namespace fs
