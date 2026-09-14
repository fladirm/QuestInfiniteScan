// Pure vkCreateDevice create-info patching (no driver calls) so the logic is unit-testable
// on the host. The intercept gathers DevicePatchCaps from the physical device and calls
// BuildPatchedDeviceCreateInfo for each attempt variant; storage keeps every array/struct
// the patched VkDeviceCreateInfo points to. Unity's original chain is never written to.
#pragma once
#include "vk_include.h"
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace fs {

struct QueueFamilyInfo {
    uint32_t index = 0;
    VkQueueFlags flags = 0;
    uint32_t count = 0;
    uint32_t timestampValidBits = 0;
};

// Feature bits FinalScan wants; each field mirrors what the physical device supports
// (input) or what ends up enabled (output).
struct FeatureSet {
    bool timelineSemaphore = false;
    bool bufferDeviceAddress = false;
    bool hostQueryReset = false;
    bool shaderFloat16 = false;
    bool shaderInt8 = false;
    bool storageBuffer16BitAccess = false;
    bool uniformAndStorageBuffer16BitAccess = false;
    bool synchronization2 = false;
    bool pipelineBinaries = false;
    bool samplerYcbcrConversion = false;
};

struct DevicePatchCaps {
    uint32_t physicalApiVersion = VK_API_VERSION_1_0;
    uint32_t instanceApiVersion = 0; // 0 = unknown (vkCreateInstance not observed)
    std::vector<QueueFamilyInfo> families;
    std::vector<std::string> availableExtensions;
    FeatureSet supported;
    // Effective version = min(instance, physical); unknown instance version -> 1.0.
    uint32_t EffectiveApiVersion() const;
    bool HasExtension(const char* name) const;
};

enum class PatchVariant {
    InjectWithGlobalPriority = 0, // second queue + LOW global priority + extensions/features
    Inject = 1,                   // second queue + extensions/features
    FeaturesOnly = 2,             // extensions/features only, Unity's queues untouched
    Original = 3                  // Unity's create info verbatim
};

const char* PatchVariantName(PatchVariant v);

struct DevicePatchOptions {
    bool injectQueue = true;          // attempt second-queue injection when a family qualifies
    bool globalPriorityLow = false;   // attach VkDeviceQueueGlobalPriorityCreateInfoKHR(LOW)
    float injectedPriority = 0.1f;
};

struct DevicePatchResult {
    PatchVariant variant = PatchVariant::Original;
    bool queueInjected = false;       // create info carries a second queue
    uint32_t injectedFamily = UINT32_MAX;
    uint32_t injectedIndex = UINT32_MAX;
    bool globalPriorityAttached = false;
    std::vector<std::string> addedExtensions;
    std::vector<std::string> enabledExtensions;   // final list
    FeatureSet requested;             // features set true in the patched chain (by us or Unity)
    std::vector<std::string> notes;   // human-readable decisions (logged + reported)
    bool chainFullyCopied = true;     // false when an opaque pNext node stopped the copy
};

// Owns all memory referenced by the patched create info.
struct DevicePatchStorage {
    VkDeviceCreateInfo info = {};
    std::vector<VkDeviceQueueCreateInfo> queueInfos;
    std::vector<std::vector<float>> priorities;
    std::vector<const char*> extensionPointers;
    std::vector<std::string> extensionStrings;
    std::vector<std::unique_ptr<uint64_t[]>> chainNodes;   // deep copies of Unity's nodes
    VkPhysicalDeviceFeatures coreFeatures = {};
    VkDeviceQueueGlobalPriorityCreateInfoKHR globalPriority = {};
    // Nodes we may prepend.
    VkPhysicalDeviceVulkan12Features vk12 = {};
    VkPhysicalDeviceTimelineSemaphoreFeatures timeline = {};
    VkPhysicalDeviceBufferDeviceAddressFeatures bda = {};
    VkPhysicalDeviceHostQueryResetFeatures hostQueryReset = {};
    VkPhysicalDeviceShaderFloat16Int8Features float16Int8 = {};
    VkPhysicalDevice16BitStorageFeatures storage16 = {};
    VkPhysicalDeviceSynchronization2Features sync2 = {};
    VkPhysicalDevicePipelineBinaryFeaturesKHR pipelineBinary = {};
    VkPhysicalDeviceSamplerYcbcrConversionFeatures ycbcr = {};
};

// Returns the create info to pass to the next vkCreateDevice. For PatchVariant::Original
// the returned pointer is `original` itself.
const VkDeviceCreateInfo* BuildPatchedDeviceCreateInfo(
    const VkDeviceCreateInfo* original,
    const DevicePatchCaps& caps,
    const DevicePatchOptions& options,
    PatchVariant variant,
    DevicePatchStorage& storage,
    DevicePatchResult& result);

// Size of a known feature/create-info pNext node, 0 if opaque.
size_t KnownChainNodeSize(VkStructureType type);

// Extensions FinalScan asks for when available (order = dependency-safe).
const std::vector<const char*>& WantedDeviceExtensions();

} // namespace fs
