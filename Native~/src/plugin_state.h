// Shared state of the FinalScan native plugin (Android only). One InterceptState written by
// the vkCreateDevice hook before Unity's device exists, one DeviceState valid between Unity's
// device Initialize and Shutdown events.
#pragma once
#include "vk_include.h"
#include "device_patch.h"
#include <IUnityInterface.h>
#include <IUnityGraphics.h>
#include <IUnityGraphicsVulkan.h>
#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>
#include <vector>

struct AHardwareBuffer;   // android/hardware_buffer.h (global scope)

namespace fs {

struct CreateAttempt { std::string variant; VkResult result = VK_SUCCESS; bool injected = false; bool globalPriority = false; };

struct InterceptState {
    std::mutex mutex;
    bool interceptRegistered = false;
    bool usedV2 = false;
    bool createDeviceSeen = false;
    uint32_t instanceApiVersion = 0;         // from vkCreateInstance (0 = not observed)
    DevicePatchCaps caps;                    // families, available extensions, supported features
    VkPhysicalDeviceProperties props = {};
    bool propsValid = false;
    VkPhysicalDeviceDriverProperties driverProps = {};
    bool driverPropsValid = false;
    VkPhysicalDevicePipelineBinaryPropertiesKHR pipelineBinaryProps = {};
    bool pipelineBinaryPropsValid = false;
    bool globalPriorityQuerySupported = false;
    std::vector<std::vector<int32_t>> familyGlobalPriorities;  // per family, supported priorities
    // Unity's original request
    std::vector<std::string> unityExtensions;
    uint32_t unityQueueCreateInfoCount = 0;
    uint32_t unityQueueFamily = UINT32_MAX;
    uint32_t unityQueueCount = 0;
    float unityQueuePriority0 = -1.0f;
    std::vector<uint32_t> unityChainTypes;
    // Injection outcome
    bool injectionAttempted = false;
    bool injectionSucceeded = false;
    bool globalPriorityOptionEnabled = false;
    bool globalPriorityUsed = false;
    std::string path = "none";
    uint32_t injectedFamily = UINT32_MAX;
    uint32_t injectedIndex = UINT32_MAX;
    std::vector<CreateAttempt> attempts;
    DevicePatchResult finalPatch;
    VkResult createResult = VK_NOT_READY;
};
InterceptState& Intercept();

struct DeviceState {
    std::atomic<bool> ready{false};
    UnityVulkanInstance instance = {};
    IUnityGraphicsVulkanV2* vulkanV2 = nullptr;
    IUnityGraphicsVulkan* vulkanV1 = nullptr;
    IUnityGraphics* graphics = nullptr;
    // Scanner queue (injected second queue of Unity's family) or VK_NULL_HANDLE.
    VkQueue scannerQueue = VK_NULL_HANDLE;
    uint32_t scannerFamily = UINT32_MAX;
    uint32_t scannerIndex = UINT32_MAX;
    bool scannerQueueAliasesUnity = false;
    std::mutex scannerSubmitMutex;
    VkCommandPool commandPool = VK_NULL_HANDLE;
    float timestampPeriod = 0.0f;
    uint32_t timestampValidBits = 0;
    bool timestampComputeAndGraphics = false;
    VkPhysicalDeviceMemoryProperties memoryProperties = {};
    VkPhysicalDeviceProperties properties = {};
    // Enabled optional features (from the successful create attempt).
    bool pipelineBinaryEnabled = false;
    bool hostQueryResetEnabled = false;
    bool ahbImportEnabled = false;
    bool ycbcrEnabled = false;
    bool timelineEnabled = false;
    bool sync2Enabled = false;
    bool maintenance5Enabled = false;
    // Proc addrs (device level, resolved at Initialize).
    PFN_vkGetPipelineKeyKHR getPipelineKey = nullptr;
    PFN_vkCreatePipelineBinariesKHR createPipelineBinaries = nullptr;
    PFN_vkGetPipelineBinaryDataKHR getPipelineBinaryData = nullptr;
    PFN_vkDestroyPipelineBinaryKHR destroyPipelineBinary = nullptr;
    PFN_vkReleaseCapturedPipelineDataKHR releaseCapturedPipelineData = nullptr;
    PFN_vkResetQueryPool resetQueryPool = nullptr;
    PFN_vkGetAndroidHardwareBufferPropertiesANDROID getAhbProperties = nullptr;
    PFN_vkCreateSamplerYcbcrConversion createYcbcrConversion = nullptr;
    PFN_vkDestroySamplerYcbcrConversion destroyYcbcrConversion = nullptr;
    PFN_vkGetImageMemoryRequirements2 getImageMemoryRequirements2 = nullptr;
    std::string initError;
};
DeviceState& Device();

// Helpers shared across translation units.
int64_t MonotonicNs();
int64_t BoottimeNs();
const char* VkResultName(VkResult r);
uint32_t FindMemoryType(uint32_t typeBits, VkMemoryPropertyFlags required);
uint64_t MaskTimestamp(uint64_t ticks);            // apply timestampValidBits
double TicksToNs(uint64_t ticks);                  // masked ticks * timestampPeriod

// probes.cpp
void ProbesInitialize();
void ProbesShutdown();
void ProbesHandleRenderEvent(int eventId);
std::string ProbeResultsJson();
// ahb_probe.cpp
int32_t RunAhbImportProbe();
std::string AhbImportJson();            // "null" until the probe ran
// camera_probe.cpp
int32_t CameraProbeStart(int32_t widthHint, int32_t heightHint, int32_t fpsHint);
int32_t CameraProbeStop();
std::string CameraProbeJson();
::AHardwareBuffer* CameraProbeAcquireLatestAhb(std::string* cameraId);  // caller releases; null if none
// device_report.cpp
std::string BuildDeviceReportJson();
// vulkan_intercept.cpp
PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API InterceptInitialization(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void* userdata);

} // namespace fs
