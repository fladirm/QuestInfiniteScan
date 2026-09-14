#include "device_patch.h"
#include <algorithm>
#include <cstdio>
#include <cstring>

namespace fs {

uint32_t DevicePatchCaps::EffectiveApiVersion() const {
    const uint32_t inst = instanceApiVersion == 0 ? VK_API_VERSION_1_0 : instanceApiVersion;
    return std::min(inst, physicalApiVersion);
}

bool DevicePatchCaps::HasExtension(const char* name) const {
    for (const auto& e : availableExtensions)
        if (e == name) return true;
    return false;
}

const char* PatchVariantName(PatchVariant v) {
    switch (v) {
        case PatchVariant::InjectWithGlobalPriority: return "inject+globalPriorityLow";
        case PatchVariant::Inject: return "inject";
        case PatchVariant::FeaturesOnly: return "featuresOnly";
        case PatchVariant::Original: return "original";
    }
    return "?";
}

const std::vector<const char*>& WantedDeviceExtensions() {
    static const std::vector<const char*> wanted = {
        VK_KHR_SYNCHRONIZATION_2_EXTENSION_NAME,
        VK_KHR_TIMELINE_SEMAPHORE_EXTENSION_NAME,
        VK_KHR_MAINTENANCE_5_EXTENSION_NAME,
        VK_KHR_PIPELINE_BINARY_EXTENSION_NAME,
        VK_EXT_HOST_QUERY_RESET_EXTENSION_NAME,
        VK_KHR_BUFFER_DEVICE_ADDRESS_EXTENSION_NAME,
        VK_KHR_EXTERNAL_MEMORY_EXTENSION_NAME,
        VK_KHR_DEDICATED_ALLOCATION_EXTENSION_NAME,
        VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME,
        VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME,
        VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME,
        VK_KHR_SHADER_FLOAT16_INT8_EXTENSION_NAME,
        VK_KHR_16BIT_STORAGE_EXTENSION_NAME,
    };
    return wanted;
}

namespace {

struct ExtDependency { const char* ext; const char* dep; uint32_t coreVersion; };
// dep is satisfied when enabled, or when the effective API version >= coreVersion (0 = never core).
const ExtDependency kDependencies[] = {
    { VK_KHR_MAINTENANCE_5_EXTENSION_NAME, VK_KHR_DYNAMIC_RENDERING_EXTENSION_NAME, VK_API_VERSION_1_3 },
    { VK_KHR_DYNAMIC_RENDERING_EXTENSION_NAME, VK_KHR_DEPTH_STENCIL_RESOLVE_EXTENSION_NAME, VK_API_VERSION_1_2 },
    { VK_KHR_DEPTH_STENCIL_RESOLVE_EXTENSION_NAME, VK_KHR_CREATE_RENDERPASS_2_EXTENSION_NAME, VK_API_VERSION_1_2 },
    { VK_KHR_CREATE_RENDERPASS_2_EXTENSION_NAME, VK_KHR_MULTIVIEW_EXTENSION_NAME, VK_API_VERSION_1_1 },
    { VK_KHR_CREATE_RENDERPASS_2_EXTENSION_NAME, VK_KHR_MAINTENANCE_2_EXTENSION_NAME, VK_API_VERSION_1_1 },
    { VK_KHR_PIPELINE_BINARY_EXTENSION_NAME, VK_KHR_MAINTENANCE_5_EXTENSION_NAME, 0 },
    { VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME, VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME, VK_API_VERSION_1_1 },
    { VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME, VK_KHR_EXTERNAL_MEMORY_EXTENSION_NAME, VK_API_VERSION_1_1 },
    { VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME, VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME, 0 },
    { VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME, VK_KHR_DEDICATED_ALLOCATION_EXTENSION_NAME, VK_API_VERSION_1_1 },
    { VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME, VK_KHR_MAINTENANCE_1_EXTENSION_NAME, VK_API_VERSION_1_1 },
    { VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME, VK_KHR_BIND_MEMORY_2_EXTENSION_NAME, VK_API_VERSION_1_1 },
    { VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME, VK_KHR_GET_MEMORY_REQUIREMENTS_2_EXTENSION_NAME, VK_API_VERSION_1_1 },
    { VK_KHR_DEDICATED_ALLOCATION_EXTENSION_NAME, VK_KHR_GET_MEMORY_REQUIREMENTS_2_EXTENSION_NAME, VK_API_VERSION_1_1 },
    { VK_KHR_16BIT_STORAGE_EXTENSION_NAME, VK_KHR_STORAGE_BUFFER_STORAGE_CLASS_EXTENSION_NAME, VK_API_VERSION_1_1 },
};

bool ListHas(const std::vector<std::string>& list, const char* name) {
    for (const auto& e : list) if (e == name) return true;
    return false;
}

// Adds `name` (and its dependencies) to `enabled` when the device offers it.
// Returns false if it cannot be enabled legally.
bool TryEnableExtension(const char* name, const DevicePatchCaps& caps, std::vector<std::string>& enabled,
                        std::vector<std::string>& added, std::vector<std::string>& notes, int depth = 0) {
    if (ListHas(enabled, name)) return true;
    if (depth > 8) return false;
    if (!caps.HasExtension(name)) return false;
    const uint32_t api = caps.EffectiveApiVersion();
    for (const auto& d : kDependencies) {
        if (std::strcmp(d.ext, name) != 0) continue;
        if (d.coreVersion != 0 && api >= d.coreVersion) continue;
        if (!TryEnableExtension(d.dep, caps, enabled, added, notes, depth + 1)) {
            notes.push_back(std::string("skip ") + name + ": dependency " + d.dep + " unavailable");
            return false;
        }
    }
    enabled.push_back(name);
    added.push_back(name);
    return true;
}

} // namespace

size_t KnownChainNodeSize(VkStructureType type) {
#define FS_NODE(tag, T) case tag: return sizeof(T)
    switch (static_cast<uint32_t>(type)) {
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, VkPhysicalDeviceFeatures2);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES, VkPhysicalDeviceVulkan11Features);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES, VkPhysicalDeviceVulkan12Features);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES, VkPhysicalDeviceVulkan13Features);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_16BIT_STORAGE_FEATURES, VkPhysicalDevice16BitStorageFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MULTIVIEW_FEATURES, VkPhysicalDeviceMultiviewFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VARIABLE_POINTERS_FEATURES, VkPhysicalDeviceVariablePointersFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROTECTED_MEMORY_FEATURES, VkPhysicalDeviceProtectedMemoryFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES, VkPhysicalDeviceSamplerYcbcrConversionFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_DRAW_PARAMETERS_FEATURES, VkPhysicalDeviceShaderDrawParametersFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_8BIT_STORAGE_FEATURES, VkPhysicalDevice8BitStorageFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_ATOMIC_INT64_FEATURES, VkPhysicalDeviceShaderAtomicInt64Features);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_FLOAT16_INT8_FEATURES, VkPhysicalDeviceShaderFloat16Int8Features);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DESCRIPTOR_INDEXING_FEATURES, VkPhysicalDeviceDescriptorIndexingFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SCALAR_BLOCK_LAYOUT_FEATURES, VkPhysicalDeviceScalarBlockLayoutFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGELESS_FRAMEBUFFER_FEATURES, VkPhysicalDeviceImagelessFramebufferFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_UNIFORM_BUFFER_STANDARD_LAYOUT_FEATURES, VkPhysicalDeviceUniformBufferStandardLayoutFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_SUBGROUP_EXTENDED_TYPES_FEATURES, VkPhysicalDeviceShaderSubgroupExtendedTypesFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SEPARATE_DEPTH_STENCIL_LAYOUTS_FEATURES, VkPhysicalDeviceSeparateDepthStencilLayoutsFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_HOST_QUERY_RESET_FEATURES, VkPhysicalDeviceHostQueryResetFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES, VkPhysicalDeviceTimelineSemaphoreFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_BUFFER_DEVICE_ADDRESS_FEATURES, VkPhysicalDeviceBufferDeviceAddressFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_BUFFER_DEVICE_ADDRESS_FEATURES_EXT, VkPhysicalDeviceBufferDeviceAddressFeaturesEXT);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_MEMORY_MODEL_FEATURES, VkPhysicalDeviceVulkanMemoryModelFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES, VkPhysicalDeviceSynchronization2Features);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DYNAMIC_RENDERING_FEATURES, VkPhysicalDeviceDynamicRenderingFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MAINTENANCE_4_FEATURES, VkPhysicalDeviceMaintenance4Features);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MAINTENANCE_5_FEATURES_KHR, VkPhysicalDeviceMaintenance5FeaturesKHR);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_FEATURES_KHR, VkPhysicalDevicePipelineBinaryFeaturesKHR);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FRAGMENT_DENSITY_MAP_FEATURES_EXT, VkPhysicalDeviceFragmentDensityMapFeaturesEXT);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FRAGMENT_DENSITY_MAP_2_FEATURES_EXT, VkPhysicalDeviceFragmentDensityMap2FeaturesEXT);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FRAGMENT_DENSITY_MAP_OFFSET_FEATURES_QCOM, VkPhysicalDeviceFragmentDensityMapOffsetFeaturesQCOM);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FRAGMENT_SHADING_RATE_FEATURES_KHR, VkPhysicalDeviceFragmentShadingRateFeaturesKHR);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ROBUSTNESS_2_FEATURES_EXT, VkPhysicalDeviceRobustness2FeaturesEXT);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TEXTURE_COMPRESSION_ASTC_HDR_FEATURES, VkPhysicalDeviceTextureCompressionASTCHDRFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_CLOCK_FEATURES_KHR, VkPhysicalDeviceShaderClockFeaturesKHR);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_INDEX_TYPE_UINT8_FEATURES_EXT, VkPhysicalDeviceIndexTypeUint8FeaturesEXT);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SUBGROUP_SIZE_CONTROL_FEATURES, VkPhysicalDeviceSubgroupSizeControlFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_ROBUSTNESS_FEATURES, VkPhysicalDeviceImageRobustnessFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_CREATION_CACHE_CONTROL_FEATURES, VkPhysicalDevicePipelineCreationCacheControlFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_TERMINATE_INVOCATION_FEATURES, VkPhysicalDeviceShaderTerminateInvocationFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_DEMOTE_TO_HELPER_INVOCATION_FEATURES, VkPhysicalDeviceShaderDemoteToHelperInvocationFeatures);
        FS_NODE(VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_GLOBAL_PRIORITY_QUERY_FEATURES_KHR, VkPhysicalDeviceGlobalPriorityQueryFeaturesKHR);
        FS_NODE(VK_STRUCTURE_TYPE_DEVICE_GROUP_DEVICE_CREATE_INFO, VkDeviceGroupDeviceCreateInfo);
        FS_NODE(VK_STRUCTURE_TYPE_DEVICE_PRIVATE_DATA_CREATE_INFO, VkDevicePrivateDataCreateInfo);
        FS_NODE(VK_STRUCTURE_TYPE_DEVICE_MEMORY_OVERALLOCATION_CREATE_INFO_AMD, VkDeviceMemoryOverallocationCreateInfoAMD);
        FS_NODE(VK_STRUCTURE_TYPE_DEVICE_DEVICE_MEMORY_REPORT_CREATE_INFO_EXT, VkDeviceDeviceMemoryReportCreateInfoEXT);
        default: return 0;
    }
#undef FS_NODE
}

namespace {

struct ChainView {
    // Pointers into the *copied* chain (mutable) or null when the node is absent/uncopied.
    VkPhysicalDeviceFeatures2* features2 = nullptr;
    VkPhysicalDeviceVulkan11Features* vk11 = nullptr;
    VkPhysicalDeviceVulkan12Features* vk12 = nullptr;
    VkPhysicalDeviceVulkan13Features* vk13 = nullptr;
    VkPhysicalDeviceTimelineSemaphoreFeatures* timeline = nullptr;
    VkPhysicalDeviceBufferDeviceAddressFeatures* bda = nullptr;
    VkPhysicalDeviceHostQueryResetFeatures* hostQueryReset = nullptr;
    VkPhysicalDeviceShaderFloat16Int8Features* float16Int8 = nullptr;
    VkPhysicalDevice16BitStorageFeatures* storage16 = nullptr;
    VkPhysicalDeviceSynchronization2Features* sync2 = nullptr;
    VkPhysicalDevicePipelineBinaryFeaturesKHR* pipelineBinary = nullptr;
    VkPhysicalDeviceSamplerYcbcrConversionFeatures* ycbcr = nullptr;
    // Presence in the ORIGINAL chain (copied or not).
    bool has11 = false, has12 = false, has13 = false, hasTimeline = false, hasBda = false, hasBdaExt = false,
         hasHostQueryReset = false, hasFloat16Int8 = false, hasStorage16 = false, hasSync2 = false,
         hasPipelineBinary = false, hasYcbcr = false, hasFeatures2 = false;
    bool separate11 = false; // any struct promoted into Vulkan11Features present (VUID 02829)
    bool separate12 = false; // any struct promoted into Vulkan12Features present (VUID 02830)
    bool separate13 = false; // any struct promoted into Vulkan13Features present (VUID 06532)
};

void ClassifyNode(VkStructureType t, ChainView& v) {
    switch (static_cast<uint32_t>(t)) {
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2: v.hasFeatures2 = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES: v.has11 = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES: v.has12 = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES: v.has13 = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES: v.hasTimeline = true; v.separate12 = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_BUFFER_DEVICE_ADDRESS_FEATURES: v.hasBda = true; v.separate12 = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_BUFFER_DEVICE_ADDRESS_FEATURES_EXT: v.hasBdaExt = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_HOST_QUERY_RESET_FEATURES: v.hasHostQueryReset = true; v.separate12 = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_FLOAT16_INT8_FEATURES: v.hasFloat16Int8 = true; v.separate12 = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_16BIT_STORAGE_FEATURES: v.hasStorage16 = true; v.separate11 = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES: v.hasSync2 = true; v.separate13 = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_FEATURES_KHR: v.hasPipelineBinary = true; break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES: v.hasYcbcr = true; v.separate11 = true; break;
        // Other structs promoted to 1.1
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MULTIVIEW_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VARIABLE_POINTERS_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROTECTED_MEMORY_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_DRAW_PARAMETERS_FEATURES:
            v.separate11 = true; break;
        // Other structs promoted to 1.2
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_8BIT_STORAGE_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_ATOMIC_INT64_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DESCRIPTOR_INDEXING_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SCALAR_BLOCK_LAYOUT_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGELESS_FRAMEBUFFER_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_UNIFORM_BUFFER_STANDARD_LAYOUT_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_SUBGROUP_EXTENDED_TYPES_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SEPARATE_DEPTH_STENCIL_LAYOUTS_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_MEMORY_MODEL_FEATURES:
            v.separate12 = true; break;
        // Other structs promoted to 1.3
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DYNAMIC_RENDERING_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MAINTENANCE_4_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TEXTURE_COMPRESSION_ASTC_HDR_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SUBGROUP_SIZE_CONTROL_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_ROBUSTNESS_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_CREATION_CACHE_CONTROL_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_TERMINATE_INVOCATION_FEATURES:
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_DEMOTE_TO_HELPER_INVOCATION_FEATURES:
            v.separate13 = true; break;
        default: break;
    }
}

void BindCopiedNode(VkBaseOutStructure* node, ChainView& v) {
    switch (static_cast<uint32_t>(node->sType)) {
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2: v.features2 = reinterpret_cast<VkPhysicalDeviceFeatures2*>(node); break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES: v.vk11 = reinterpret_cast<VkPhysicalDeviceVulkan11Features*>(node); break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES: v.vk12 = reinterpret_cast<VkPhysicalDeviceVulkan12Features*>(node); break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES: v.vk13 = reinterpret_cast<VkPhysicalDeviceVulkan13Features*>(node); break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES: v.timeline = reinterpret_cast<VkPhysicalDeviceTimelineSemaphoreFeatures*>(node); break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_BUFFER_DEVICE_ADDRESS_FEATURES: v.bda = reinterpret_cast<VkPhysicalDeviceBufferDeviceAddressFeatures*>(node); break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_HOST_QUERY_RESET_FEATURES: v.hostQueryReset = reinterpret_cast<VkPhysicalDeviceHostQueryResetFeatures*>(node); break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_FLOAT16_INT8_FEATURES: v.float16Int8 = reinterpret_cast<VkPhysicalDeviceShaderFloat16Int8Features*>(node); break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_16BIT_STORAGE_FEATURES: v.storage16 = reinterpret_cast<VkPhysicalDevice16BitStorageFeatures*>(node); break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES: v.sync2 = reinterpret_cast<VkPhysicalDeviceSynchronization2Features*>(node); break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_FEATURES_KHR: v.pipelineBinary = reinterpret_cast<VkPhysicalDevicePipelineBinaryFeaturesKHR*>(node); break;
        case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES: v.ycbcr = reinterpret_cast<VkPhysicalDeviceSamplerYcbcrConversionFeatures*>(node); break;
        default: break;
    }
}

template <typename T>
void Prepend(T& node, VkStructureType type, DevicePatchStorage& s) {
    node = T{};
    node.sType = type;
    node.pNext = const_cast<void*>(s.info.pNext);
    s.info.pNext = &node;
}

} // namespace

const VkDeviceCreateInfo* BuildPatchedDeviceCreateInfo(
    const VkDeviceCreateInfo* original, const DevicePatchCaps& caps, const DevicePatchOptions& options,
    PatchVariant variant, DevicePatchStorage& s, DevicePatchResult& r)
{
    r = DevicePatchResult{};
    r.variant = variant;
    s = DevicePatchStorage{};
    if (original == nullptr) return nullptr;

    // Record Unity's own extension list in the result even for the Original variant.
    for (uint32_t i = 0; i < original->enabledExtensionCount; ++i)
        if (original->ppEnabledExtensionNames[i]) r.enabledExtensions.emplace_back(original->ppEnabledExtensionNames[i]);

    if (variant == PatchVariant::Original) {
        r.notes.push_back("original create info passed through untouched");
        return original;
    }

    s.info = *original;
    const uint32_t api = caps.EffectiveApiVersion();

    // ---- 1. Extensions -------------------------------------------------------------
    std::vector<std::string> enabled = r.enabledExtensions;
    for (const char* want : WantedDeviceExtensions()) {
        if (ListHas(enabled, want)) continue;
        if (!caps.HasExtension(want)) { r.notes.push_back(std::string("absent ") + want); continue; }
        TryEnableExtension(want, caps, enabled, r.addedExtensions, r.notes);
    }

    // ---- 2. pNext chain: deep-copy known nodes so we may edit them ----------------
    ChainView view;
    for (auto n = static_cast<const VkBaseInStructure*>(original->pNext); n; n = n->pNext)
        ClassifyNode(n->sType, view);

    s.info.pNext = nullptr;
    VkBaseOutStructure* tail = nullptr;
    for (auto n = static_cast<const VkBaseInStructure*>(original->pNext); n; n = n->pNext) {
        const size_t bytes = KnownChainNodeSize(n->sType);
        if (bytes == 0) {
            // Opaque node: keep it and the rest of Unity's chain by pointer.
            r.chainFullyCopied = false;
            char note[96];
            std::snprintf(note, sizeof(note), "opaque pNext sType=%u kept by reference; later nodes not editable", static_cast<unsigned>(n->sType));
            r.notes.push_back(note);
            if (tail) tail->pNext = const_cast<VkBaseOutStructure*>(reinterpret_cast<const VkBaseOutStructure*>(n));
            else s.info.pNext = n;
            break;
        }
        s.chainNodes.emplace_back(new uint64_t[(bytes + 7) / 8]);
        auto copy = reinterpret_cast<VkBaseOutStructure*>(s.chainNodes.back().get());
        std::memcpy(copy, n, bytes);
        copy->pNext = nullptr;
        if (tail) tail->pNext = copy; else s.info.pNext = copy;
        tail = copy;
        BindCopiedNode(copy, view);
    }

    // ---- 3. Features ---------------------------------------------------------------
    const FeatureSet& sup = caps.supported;
    auto extOn = [&](const char* e) { return ListHas(enabled, e); };
    const bool core12 = api >= VK_API_VERSION_1_2;
    const bool core11 = api >= VK_API_VERSION_1_1;
    const bool core13 = api >= VK_API_VERSION_1_3;

    // Vulkan12-promoted features: timeline, bda, hostQueryReset, shaderFloat16/int8.
    const bool want12 = sup.timelineSemaphore || sup.bufferDeviceAddress || sup.hostQueryReset || sup.shaderFloat16 || sup.shaderInt8;
    if (want12) {
        if (view.has12) {
            if (view.vk12) {
                if (sup.timelineSemaphore) view.vk12->timelineSemaphore = VK_TRUE;
                if (sup.bufferDeviceAddress) view.vk12->bufferDeviceAddress = VK_TRUE;
                if (sup.hostQueryReset) view.vk12->hostQueryReset = VK_TRUE;
                if (sup.shaderFloat16) view.vk12->shaderFloat16 = VK_TRUE;
                if (sup.shaderInt8) view.vk12->shaderInt8 = VK_TRUE;
                r.notes.push_back("Vulkan12Features: Unity node edited");
            } else r.notes.push_back("Vulkan12Features present but not editable (behind opaque node)");
        } else if (core12 && !view.separate12) {
            Prepend(s.vk12, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES, s);
            view.vk12 = &s.vk12;
            if (sup.timelineSemaphore) s.vk12.timelineSemaphore = VK_TRUE;
            if (sup.bufferDeviceAddress) s.vk12.bufferDeviceAddress = VK_TRUE;
            if (sup.hostQueryReset) s.vk12.hostQueryReset = VK_TRUE;
            if (sup.shaderFloat16) s.vk12.shaderFloat16 = VK_TRUE;
            if (sup.shaderInt8) s.vk12.shaderInt8 = VK_TRUE;
            // VUID-VkDeviceCreateInfo-pNext-02831..02835: extensions Unity enabled implicitly
            // must be re-stated as features once Vulkan12Features is in the chain.
            if (extOn(VK_KHR_DRAW_INDIRECT_COUNT_EXTENSION_NAME)) s.vk12.drawIndirectCount = VK_TRUE;
            if (extOn(VK_KHR_SAMPLER_MIRROR_CLAMP_TO_EDGE_EXTENSION_NAME)) s.vk12.samplerMirrorClampToEdge = VK_TRUE;
            if (extOn(VK_EXT_DESCRIPTOR_INDEXING_EXTENSION_NAME)) s.vk12.descriptorIndexing = VK_TRUE;
            if (extOn(VK_EXT_SAMPLER_FILTER_MINMAX_EXTENSION_NAME)) s.vk12.samplerFilterMinmax = VK_TRUE;
            if (extOn(VK_EXT_SHADER_VIEWPORT_INDEX_LAYER_EXTENSION_NAME)) { s.vk12.shaderOutputViewportIndex = VK_TRUE; s.vk12.shaderOutputLayer = VK_TRUE; }
            r.notes.push_back("Vulkan12Features: prepended");
        } else {
            // Individual promoted structs (each needs its extension or core 1.2).
            if (sup.timelineSemaphore) {
                if (view.hasTimeline) { if (view.timeline) view.timeline->timelineSemaphore = VK_TRUE; }
                else if (core12 || extOn(VK_KHR_TIMELINE_SEMAPHORE_EXTENSION_NAME)) {
                    Prepend(s.timeline, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES, s);
                    s.timeline.timelineSemaphore = VK_TRUE; view.timeline = &s.timeline;
                }
            }
            if (sup.bufferDeviceAddress && !view.hasBdaExt) {
                if (view.hasBda) { if (view.bda) view.bda->bufferDeviceAddress = VK_TRUE; }
                else if (core12 || extOn(VK_KHR_BUFFER_DEVICE_ADDRESS_EXTENSION_NAME)) {
                    Prepend(s.bda, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_BUFFER_DEVICE_ADDRESS_FEATURES, s);
                    s.bda.bufferDeviceAddress = VK_TRUE; view.bda = &s.bda;
                }
            }
            if (sup.hostQueryReset) {
                if (view.hasHostQueryReset) { if (view.hostQueryReset) view.hostQueryReset->hostQueryReset = VK_TRUE; }
                else if (core12 || extOn(VK_EXT_HOST_QUERY_RESET_EXTENSION_NAME)) {
                    Prepend(s.hostQueryReset, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_HOST_QUERY_RESET_FEATURES, s);
                    s.hostQueryReset.hostQueryReset = VK_TRUE; view.hostQueryReset = &s.hostQueryReset;
                }
            }
            if (sup.shaderFloat16 || sup.shaderInt8) {
                if (view.hasFloat16Int8) {
                    if (view.float16Int8) { if (sup.shaderFloat16) view.float16Int8->shaderFloat16 = VK_TRUE; if (sup.shaderInt8) view.float16Int8->shaderInt8 = VK_TRUE; }
                } else if (core12 || extOn(VK_KHR_SHADER_FLOAT16_INT8_EXTENSION_NAME)) {
                    Prepend(s.float16Int8, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_FLOAT16_INT8_FEATURES, s);
                    if (sup.shaderFloat16) s.float16Int8.shaderFloat16 = VK_TRUE;
                    if (sup.shaderInt8) s.float16Int8.shaderInt8 = VK_TRUE;
                    view.float16Int8 = &s.float16Int8;
                }
            }
            r.notes.push_back(view.separate12 ? "Vulkan12 features: individual structs (Unity uses promoted structs)" : "Vulkan12 features: individual structs (API < 1.2)");
        }
    }

    // Vulkan11-promoted: 16-bit storage + sampler ycbcr conversion.
    if (sup.storageBuffer16BitAccess || sup.samplerYcbcrConversion) {
        if (view.has11) {
            if (view.vk11) {
                if (sup.storageBuffer16BitAccess) view.vk11->storageBuffer16BitAccess = VK_TRUE;
                if (sup.uniformAndStorageBuffer16BitAccess) view.vk11->uniformAndStorageBuffer16BitAccess = VK_TRUE;
                if (sup.samplerYcbcrConversion) view.vk11->samplerYcbcrConversion = VK_TRUE;
                r.notes.push_back("Vulkan11Features: Unity node edited");
            } else r.notes.push_back("Vulkan11Features present but not editable");
        } else {
            if (sup.storageBuffer16BitAccess) {
                if (view.hasStorage16) {
                    if (view.storage16) { view.storage16->storageBuffer16BitAccess = VK_TRUE; if (sup.uniformAndStorageBuffer16BitAccess) view.storage16->uniformAndStorageBuffer16BitAccess = VK_TRUE; }
                } else if (core11 || extOn(VK_KHR_16BIT_STORAGE_EXTENSION_NAME)) {
                    Prepend(s.storage16, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_16BIT_STORAGE_FEATURES, s);
                    s.storage16.storageBuffer16BitAccess = VK_TRUE;
                    if (sup.uniformAndStorageBuffer16BitAccess) s.storage16.uniformAndStorageBuffer16BitAccess = VK_TRUE;
                    view.storage16 = &s.storage16;
                }
            }
            if (sup.samplerYcbcrConversion) {
                if (view.hasYcbcr) { if (view.ycbcr) view.ycbcr->samplerYcbcrConversion = VK_TRUE; }
                else if (core11 || extOn(VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME)) {
                    Prepend(s.ycbcr, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES, s);
                    s.ycbcr.samplerYcbcrConversion = VK_TRUE; view.ycbcr = &s.ycbcr;
                }
            }
        }
    }

    // Synchronization2 (Vulkan13-promoted).
    if (sup.synchronization2) {
        if (view.has13) {
            if (view.vk13) { view.vk13->synchronization2 = VK_TRUE; r.notes.push_back("Vulkan13Features: Unity node edited"); }
            else r.notes.push_back("Vulkan13Features present but not editable");
        } else if (view.hasSync2) {
            if (view.sync2) view.sync2->synchronization2 = VK_TRUE;
        } else if (core13 || extOn(VK_KHR_SYNCHRONIZATION_2_EXTENSION_NAME)) {
            Prepend(s.sync2, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES, s);
            s.sync2.synchronization2 = VK_TRUE; view.sync2 = &s.sync2;
        }
    }

    // Pipeline binaries (KHR extension struct, never promoted).
    if (sup.pipelineBinaries && extOn(VK_KHR_PIPELINE_BINARY_EXTENSION_NAME)) {
        if (view.hasPipelineBinary) { if (view.pipelineBinary) view.pipelineBinary->pipelineBinaries = VK_TRUE; }
        else {
            Prepend(s.pipelineBinary, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_FEATURES_KHR, s);
            s.pipelineBinary.pipelineBinaries = VK_TRUE; view.pipelineBinary = &s.pipelineBinary;
        }
    }

    // Summarize what the patched chain requests.
    auto b = [](VkBool32 x) { return x != VK_FALSE; };
    r.requested.timelineSemaphore = (view.vk12 && b(view.vk12->timelineSemaphore)) || (view.timeline && b(view.timeline->timelineSemaphore));
    r.requested.bufferDeviceAddress = (view.vk12 && b(view.vk12->bufferDeviceAddress)) || (view.bda && b(view.bda->bufferDeviceAddress));
    r.requested.hostQueryReset = (view.vk12 && b(view.vk12->hostQueryReset)) || (view.hostQueryReset && b(view.hostQueryReset->hostQueryReset));
    r.requested.shaderFloat16 = (view.vk12 && b(view.vk12->shaderFloat16)) || (view.float16Int8 && b(view.float16Int8->shaderFloat16));
    r.requested.shaderInt8 = (view.vk12 && b(view.vk12->shaderInt8)) || (view.float16Int8 && b(view.float16Int8->shaderInt8));
    r.requested.storageBuffer16BitAccess = (view.vk11 && b(view.vk11->storageBuffer16BitAccess)) || (view.storage16 && b(view.storage16->storageBuffer16BitAccess));
    r.requested.uniformAndStorageBuffer16BitAccess = (view.vk11 && b(view.vk11->uniformAndStorageBuffer16BitAccess)) || (view.storage16 && b(view.storage16->uniformAndStorageBuffer16BitAccess));
    r.requested.synchronization2 = (view.vk13 && b(view.vk13->synchronization2)) || (view.sync2 && b(view.sync2->synchronization2));
    r.requested.pipelineBinaries = view.pipelineBinary && b(view.pipelineBinary->pipelineBinaries);
    r.requested.samplerYcbcrConversion = (view.vk11 && b(view.vk11->samplerYcbcrConversion)) || (view.ycbcr && b(view.ycbcr->samplerYcbcrConversion));

    // ---- 4. Queues -----------------------------------------------------------------
    s.queueInfos.assign(original->pQueueCreateInfos, original->pQueueCreateInfos + original->queueCreateInfoCount);
    s.priorities.resize(s.queueInfos.size());
    for (size_t i = 0; i < s.queueInfos.size(); ++i) {
        VkDeviceQueueCreateInfo& q = s.queueInfos[i];
        s.priorities[i].assign(q.pQueuePriorities, q.pQueuePriorities + q.queueCount);
        q.pQueuePriorities = s.priorities[i].data();
    }
    const bool injectRequested = options.injectQueue &&
        (variant == PatchVariant::Inject || variant == PatchVariant::InjectWithGlobalPriority);
    if (injectRequested) {
        uint32_t selected = UINT32_MAX, candidates = 0;
        for (size_t i = 0; i < s.queueInfos.size(); ++i) {
            const VkDeviceQueueCreateInfo& q = s.queueInfos[i];
            if (q.queueFamilyIndex >= caps.families.size()) continue;
            const QueueFamilyInfo& f = caps.families[q.queueFamilyIndex];
            const VkQueueFlags required = VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT;
            const bool safe = q.queueCount == 1u && f.count >= 2u && (f.flags & required) == required &&
                              (q.flags & VK_DEVICE_QUEUE_CREATE_PROTECTED_BIT) == 0u;
            if (safe) { selected = static_cast<uint32_t>(i); ++candidates; }
        }
        if (candidates == 1u) {
            VkDeviceQueueCreateInfo& q = s.queueInfos[selected];
            s.priorities[selected].push_back(options.injectedPriority);
            q.queueCount = 2u;
            q.pQueuePriorities = s.priorities[selected].data();
            r.queueInjected = true;
            r.injectedFamily = q.queueFamilyIndex;
            r.injectedIndex = 1u;
            const bool gpExt = caps.HasExtension(VK_KHR_GLOBAL_PRIORITY_EXTENSION_NAME) || caps.HasExtension(VK_EXT_GLOBAL_PRIORITY_EXTENSION_NAME);
            if (variant == PatchVariant::InjectWithGlobalPriority && options.globalPriorityLow && gpExt) {
                // One VkDeviceQueueCreateInfo per family (VUID 02802): the priority applies to
                // every queue of the family, Unity's included. Reported as such.
                const char* gpName = caps.HasExtension(VK_KHR_GLOBAL_PRIORITY_EXTENSION_NAME)
                    ? VK_KHR_GLOBAL_PRIORITY_EXTENSION_NAME : VK_EXT_GLOBAL_PRIORITY_EXTENSION_NAME;
                if (!ListHas(enabled, gpName)) { enabled.push_back(gpName); r.addedExtensions.push_back(gpName); }
                s.globalPriority = {};
                s.globalPriority.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_GLOBAL_PRIORITY_CREATE_INFO_KHR;
                s.globalPriority.pNext = q.pNext;
                s.globalPriority.globalPriority = VK_QUEUE_GLOBAL_PRIORITY_LOW_KHR;
                q.pNext = &s.globalPriority;
                r.globalPriorityAttached = true;
                r.notes.push_back("global priority LOW attached to the shared family create info (applies to Unity's queue too)");
            } else if (variant == PatchVariant::InjectWithGlobalPriority) {
                r.notes.push_back(gpExt ? "global priority not requested (option off)" : "global priority extension unavailable");
            }
        } else {
            char note[96];
            std::snprintf(note, sizeof(note), "no injection: safeCandidates=%u", candidates);
            r.notes.push_back(note);
        }
    }
    s.info.pQueueCreateInfos = s.queueInfos.data();
    s.info.queueCreateInfoCount = static_cast<uint32_t>(s.queueInfos.size());

    // ---- 5. Finalize extension pointers -------------------------------------------
    s.extensionStrings = enabled;
    s.extensionPointers.clear();
    for (const auto& e : s.extensionStrings) s.extensionPointers.push_back(e.c_str());
    s.info.ppEnabledExtensionNames = s.extensionPointers.data();
    s.info.enabledExtensionCount = static_cast<uint32_t>(s.extensionPointers.size());
    r.enabledExtensions = enabled;
    return &s.info;
}

} // namespace fs
