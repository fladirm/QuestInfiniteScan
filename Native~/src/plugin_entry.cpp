// UnityPluginLoad/Unload, Unity device events, C ABI exports (finalscan_native_api.h, ABI 1).
#include "plugin_state.h"
#include "json_writer.h"
#include "log.h"
#include "../include/finalscan_native_api.h"
#include <time.h>
#include <cstring>

namespace fs {

DeviceState& Device() { static DeviceState s; return s; }

int64_t MonotonicNs() { timespec ts = {}; clock_gettime(CLOCK_MONOTONIC, &ts); return static_cast<int64_t>(ts.tv_sec) * 1000000000LL + ts.tv_nsec; }
int64_t BoottimeNs() { timespec ts = {}; clock_gettime(CLOCK_BOOTTIME, &ts); return static_cast<int64_t>(ts.tv_sec) * 1000000000LL + ts.tv_nsec; }

uint32_t FindMemoryType(uint32_t typeBits, VkMemoryPropertyFlags required) {
    const VkPhysicalDeviceMemoryProperties& mp = Device().memoryProperties;
    for (uint32_t i = 0; i < mp.memoryTypeCount; ++i)
        if ((typeBits & (1u << i)) && (mp.memoryTypes[i].propertyFlags & required) == required) return i;
    return UINT32_MAX;
}

uint64_t MaskTimestamp(uint64_t ticks) {
    const uint32_t bits = Device().timestampValidBits;
    if (bits == 0 || bits >= 64) return ticks;
    return ticks & ((1ull << bits) - 1ull);
}

double TicksToNs(uint64_t ticks) { return static_cast<double>(MaskTimestamp(ticks)) * static_cast<double>(Device().timestampPeriod); }

namespace {

IUnityInterfaces* g_interfaces = nullptr;
bool g_interceptInstalled = false;

template <typename T>
T DeviceProc(const char* name) {
    DeviceState& d = Device();
    if (d.instance.getInstanceProcAddr == nullptr || d.instance.instance == VK_NULL_HANDLE) return nullptr;
    auto gdpa = reinterpret_cast<PFN_vkGetDeviceProcAddr>(d.instance.getInstanceProcAddr(d.instance.instance, "vkGetDeviceProcAddr"));
    if (gdpa == nullptr) return nullptr;
    return reinterpret_cast<T>(gdpa(d.instance.device, name));
}

void InitializeDevice() {
    DeviceState& d = Device();
    if (d.ready.load(std::memory_order_acquire)) return;
    if (d.graphics == nullptr || d.graphics->GetRenderer() != kUnityGfxRendererVulkan) {
        d.initError = "renderer is not Vulkan";
        Log("device init skipped: renderer is not Vulkan");
        return;
    }
    if (d.vulkanV2 == nullptr && g_interfaces != nullptr) d.vulkanV2 = g_interfaces->Get<IUnityGraphicsVulkanV2>();
    if (d.vulkanV2 == nullptr && d.vulkanV1 == nullptr && g_interfaces != nullptr) d.vulkanV1 = g_interfaces->Get<IUnityGraphicsVulkan>();
    if (d.vulkanV2 != nullptr) d.instance = d.vulkanV2->Instance();
    else if (d.vulkanV1 != nullptr) d.instance = d.vulkanV1->Instance();
    else { d.initError = "no IUnityGraphicsVulkan interface"; LogError("%s", d.initError.c_str()); return; }
    if (d.instance.device == VK_NULL_HANDLE || d.instance.physicalDevice == VK_NULL_HANDLE) {
        d.initError = "Unity Vulkan instance not ready";
        Log("device init deferred: %s", d.initError.c_str());
        return;
    }

    vkGetPhysicalDeviceProperties(d.instance.physicalDevice, &d.properties);
    vkGetPhysicalDeviceMemoryProperties(d.instance.physicalDevice, &d.memoryProperties);
    d.timestampPeriod = d.properties.limits.timestampPeriod;
    d.timestampComputeAndGraphics = d.properties.limits.timestampComputeAndGraphics != VK_FALSE;
    uint32_t familyCount = 0;
    vkGetPhysicalDeviceQueueFamilyProperties(d.instance.physicalDevice, &familyCount, nullptr);
    std::vector<VkQueueFamilyProperties> families(familyCount);
    vkGetPhysicalDeviceQueueFamilyProperties(d.instance.physicalDevice, &familyCount, families.data());
    d.timestampValidBits = d.instance.queueFamilyIndex < familyCount ? families[d.instance.queueFamilyIndex].timestampValidBits : 0;

    InterceptState& st = Intercept();
    {
        std::lock_guard<std::mutex> lock(st.mutex);
        const FeatureSet& req = st.finalPatch.requested;
        auto ext = [&](const char* n) { for (const auto& e : st.finalPatch.enabledExtensions) if (e == n) return true; return false; };
        d.pipelineBinaryEnabled = req.pipelineBinaries && ext(VK_KHR_PIPELINE_BINARY_EXTENSION_NAME);
        d.hostQueryResetEnabled = req.hostQueryReset;
        d.ahbImportEnabled = ext(VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME);
        d.ycbcrEnabled = req.samplerYcbcrConversion;
        d.timelineEnabled = req.timelineSemaphore;
        d.sync2Enabled = req.synchronization2;
        d.maintenance5Enabled = ext(VK_KHR_MAINTENANCE_5_EXTENSION_NAME);
        // Scanner queue: the injected second queue in Unity's family.
        d.scannerQueue = VK_NULL_HANDLE;
        d.scannerFamily = d.scannerIndex = UINT32_MAX;
        d.scannerQueueAliasesUnity = false;
        if (st.injectionSucceeded && st.injectedFamily == d.instance.queueFamilyIndex) {
            VkQueue q = VK_NULL_HANDLE;
            vkGetDeviceQueue(d.instance.device, st.injectedFamily, st.injectedIndex, &q);
            if (q == VK_NULL_HANDLE) LogError("vkGetDeviceQueue(%u,%u) returned null", st.injectedFamily, st.injectedIndex);
            else if (q == d.instance.graphicsQueue) { d.scannerQueueAliasesUnity = true; LogError("injected queue aliases Unity's graphics queue; scanner queue refused"); }
            else { d.scannerQueue = q; d.scannerFamily = st.injectedFamily; d.scannerIndex = st.injectedIndex; }
        } else if (st.injectionSucceeded) {
            LogError("injected family %u differs from Unity's family %u; scanner queue refused", st.injectedFamily, d.instance.queueFamilyIndex);
        }
    }

    VkCommandPoolCreateInfo pool = {};
    pool.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    pool.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
    pool.queueFamilyIndex = d.instance.queueFamilyIndex;
    const VkResult poolResult = vkCreateCommandPool(d.instance.device, &pool, nullptr, &d.commandPool);
    if (poolResult != VK_SUCCESS) { d.initError = std::string("vkCreateCommandPool ") + VkResultName(poolResult); LogError("%s", d.initError.c_str()); return; }

    d.getPipelineKey = DeviceProc<PFN_vkGetPipelineKeyKHR>("vkGetPipelineKeyKHR");
    d.createPipelineBinaries = DeviceProc<PFN_vkCreatePipelineBinariesKHR>("vkCreatePipelineBinariesKHR");
    d.getPipelineBinaryData = DeviceProc<PFN_vkGetPipelineBinaryDataKHR>("vkGetPipelineBinaryDataKHR");
    d.destroyPipelineBinary = DeviceProc<PFN_vkDestroyPipelineBinaryKHR>("vkDestroyPipelineBinaryKHR");
    d.releaseCapturedPipelineData = DeviceProc<PFN_vkReleaseCapturedPipelineDataKHR>("vkReleaseCapturedPipelineDataKHR");
    d.resetQueryPool = DeviceProc<PFN_vkResetQueryPool>("vkResetQueryPool");
    if (d.resetQueryPool == nullptr) d.resetQueryPool = DeviceProc<PFN_vkResetQueryPool>("vkResetQueryPoolEXT");
    d.getAhbProperties = DeviceProc<PFN_vkGetAndroidHardwareBufferPropertiesANDROID>("vkGetAndroidHardwareBufferPropertiesANDROID");
    d.createYcbcrConversion = DeviceProc<PFN_vkCreateSamplerYcbcrConversion>("vkCreateSamplerYcbcrConversion");
    if (d.createYcbcrConversion == nullptr) d.createYcbcrConversion = DeviceProc<PFN_vkCreateSamplerYcbcrConversion>("vkCreateSamplerYcbcrConversionKHR");
    d.destroyYcbcrConversion = DeviceProc<PFN_vkDestroySamplerYcbcrConversion>("vkDestroySamplerYcbcrConversion");
    if (d.destroyYcbcrConversion == nullptr) d.destroyYcbcrConversion = DeviceProc<PFN_vkDestroySamplerYcbcrConversion>("vkDestroySamplerYcbcrConversionKHR");
    d.getImageMemoryRequirements2 = DeviceProc<PFN_vkGetImageMemoryRequirements2>("vkGetImageMemoryRequirements2");
    if (d.getImageMemoryRequirements2 == nullptr) d.getImageMemoryRequirements2 = DeviceProc<PFN_vkGetImageMemoryRequirements2>("vkGetImageMemoryRequirements2KHR");

    Log("device ready: unityFamily=%u scannerQueue=%p (family=%u index=%u) timestampPeriod=%.6f validBits=%u pipelineBinary=%d hostQueryReset=%d ahb=%d ycbcr=%d procs: key=%p binaries=%p data=%p resetQP=%p ahbProps=%p",
        d.instance.queueFamilyIndex, static_cast<void*>(d.scannerQueue), d.scannerFamily, d.scannerIndex, d.timestampPeriod, d.timestampValidBits,
        d.pipelineBinaryEnabled, d.hostQueryResetEnabled, d.ahbImportEnabled, d.ycbcrEnabled,
        reinterpret_cast<void*>(d.getPipelineKey), reinterpret_cast<void*>(d.createPipelineBinaries), reinterpret_cast<void*>(d.getPipelineBinaryData),
        reinterpret_cast<void*>(d.resetQueryPool), reinterpret_cast<void*>(d.getAhbProperties));
    ProbesInitialize();
    d.initError.clear();
    d.ready.store(true, std::memory_order_release);
    LogJson("device-report", BuildDeviceReportJson());
}

void ShutdownDevice() {
    DeviceState& d = Device();
    if (!d.ready.load(std::memory_order_acquire)) return;
    d.ready.store(false, std::memory_order_release);
    ProbesShutdown();   // joins workers; vkDeviceWaitIdle is allowed here (Unity device shutdown/reset)
    if (d.instance.device != VK_NULL_HANDLE) vkDeviceWaitIdle(d.instance.device);
    if (d.commandPool != VK_NULL_HANDLE) { vkDestroyCommandPool(d.instance.device, d.commandPool, nullptr); d.commandPool = VK_NULL_HANDLE; }
    d.scannerQueue = VK_NULL_HANDLE;
    Log("device shutdown complete");
}

void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType) {
    if (eventType == kUnityGfxDeviceEventInitialize || eventType == kUnityGfxDeviceEventAfterReset) InitializeDevice();
    else if (eventType == kUnityGfxDeviceEventShutdown || eventType == kUnityGfxDeviceEventBeforeReset) ShutdownDevice();
}

void UNITY_INTERFACE_API OnRenderEvent(int eventId) {
    if (!Device().ready.load(std::memory_order_acquire)) {
        // Unity may issue events before the device callback ran (plugin loaded late).
        InitializeDevice();
        if (!Device().ready.load(std::memory_order_acquire)) return;
    }
    ProbesHandleRenderEvent(eventId);
}

} // namespace
} // namespace fs

extern "C" {

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces) {
    using namespace fs;
    g_interfaces = unityInterfaces;
    DeviceState& d = Device();
    d.graphics = unityInterfaces->Get<IUnityGraphics>();
    d.vulkanV2 = unityInterfaces->Get<IUnityGraphicsVulkanV2>();
    d.vulkanV1 = d.vulkanV2 == nullptr ? unityInterfaces->Get<IUnityGraphicsVulkan>() : nullptr;
    InterceptState& st = Intercept();
    if (d.vulkanV2 != nullptr) {
        g_interceptInstalled = d.vulkanV2->AddInterceptInitialization(InterceptInitialization, nullptr, 10000);
        st.usedV2 = true;
        st.interceptRegistered = g_interceptInstalled;
        Log("IUnityGraphicsVulkanV2 AddInterceptInitialization(priority=10000): %s", g_interceptInstalled ? "registered" : "FAILED (plugin loaded after device creation?)");
    } else if (d.vulkanV1 != nullptr) {
        g_interceptInstalled = d.vulkanV1->InterceptInitialization(InterceptInitialization, nullptr);
        st.usedV2 = false;
        st.interceptRegistered = g_interceptInstalled;
        Log("IUnityGraphicsVulkanV2 unavailable; V1 InterceptInitialization: %s", g_interceptInstalled ? "registered" : "FAILED");
    } else {
        Log("no IUnityGraphicsVulkan interface at plugin load (renderer not Vulkan?)");
    }
    if (d.graphics != nullptr) {
        d.graphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
        OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
    }
}

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
    using namespace fs;
    DeviceState& d = Device();
    if (d.graphics != nullptr) d.graphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
    if (d.vulkanV2 != nullptr && g_interceptInstalled) d.vulkanV2->RemoveInterceptInitialization(InterceptInitialization);
    ShutdownDevice();
    g_interceptInstalled = false;
    g_interfaces = nullptr;
}

int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_GetAbiVersion(void) { return FS_NATIVE_ABI_VERSION; }
int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_IsVulkanReady(void) { return fs::Device().ready.load(std::memory_order_acquire) ? 1 : 0; }

int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_GetDeviceReportJson(char* buf, int32_t cap) {
    return fs::CopyJsonOut(fs::BuildDeviceReportJson(), buf, cap);
}
int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_GetProbeResultsJson(char* buf, int32_t cap) {
    return fs::CopyJsonOut(fs::ProbeResultsJson(), buf, cap);
}
void* UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_GetRenderEventFunc(void) {
    return reinterpret_cast<void*>(static_cast<UnityRenderingEvent>(fs::OnRenderEvent));
}
int64_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_MonotonicNowNs(void) { return fs::MonotonicNs(); }
int64_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_BoottimeNowNs(void) { return fs::BoottimeNs(); }
int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_CameraProbeStart(int32_t widthHint, int32_t heightHint, int32_t fpsHint) {
    return fs::CameraProbeStart(widthHint, heightHint, fpsHint);
}
int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_CameraProbeStop(void) { return fs::CameraProbeStop(); }
int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_CameraProbeReportJson(char* buf, int32_t cap) {
    return fs::CopyJsonOut(fs::CameraProbeJson(), buf, cap);
}
int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_AhbImportProbe(void) { return fs::RunAhbImportProbe(); }
int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsNative_GetScannerQueueInfo(int32_t* family, int32_t* index) {
    const fs::DeviceState& d = fs::Device();
    if (family) *family = d.scannerQueue != VK_NULL_HANDLE ? static_cast<int32_t>(d.scannerFamily) : -1;
    if (index) *index = d.scannerQueue != VK_NULL_HANDLE ? static_cast<int32_t>(d.scannerIndex) : -1;
    return d.scannerQueue != VK_NULL_HANDLE ? FS_OK : FS_ERR_UNAVAILABLE;
}

} // extern "C"
