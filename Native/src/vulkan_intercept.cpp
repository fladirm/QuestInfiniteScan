// vkCreateInstance / vkCreateDevice interception registered through Unity's Vulkan interface.
// Gathers physical-device capabilities, builds patched create infos (device_patch.cpp) and
// retries with progressively smaller patches; Unity's original create info is the last resort.
#include "plugin_state.h"
#include "log.h"
#include <sys/system_properties.h>
#include <cstring>

namespace fs {

InterceptState& Intercept() { static InterceptState s; return s; }

namespace {

PFN_vkGetInstanceProcAddr g_next = nullptr;
VkInstance g_instance = VK_NULL_HANDLE;

bool SystemPropertyEnabled(const char* name) {
    char value[PROP_VALUE_MAX] = {};
    if (__system_property_get(name, value) <= 0) return false;
    return std::strcmp(value, "1") == 0 || std::strcmp(value, "true") == 0;
}

VkResult VKAPI_PTR InterceptCreateInstance(const VkInstanceCreateInfo* createInfo,
    const VkAllocationCallbacks* allocator, VkInstance* instance)
{
    auto next = reinterpret_cast<PFN_vkCreateInstance>(g_next ? g_next(VK_NULL_HANDLE, "vkCreateInstance") : nullptr);
    if (next == nullptr) return VK_ERROR_INITIALIZATION_FAILED;
    {
        std::lock_guard<std::mutex> lock(Intercept().mutex);
        Intercept().instanceApiVersion = (createInfo && createInfo->pApplicationInfo)
            ? createInfo->pApplicationInfo->apiVersion : VK_API_VERSION_1_0;
    }
    const VkResult result = next(createInfo, allocator, instance);
    if (result == VK_SUCCESS && instance) g_instance = *instance;
    Log("vkCreateInstance observed: apiVersion=%u.%u.%u result=%d",
        VK_API_VERSION_MAJOR(Intercept().instanceApiVersion), VK_API_VERSION_MINOR(Intercept().instanceApiVersion),
        VK_API_VERSION_PATCH(Intercept().instanceApiVersion), static_cast<int>(result));
    return result;
}

void GatherCapabilities(VkPhysicalDevice physicalDevice, InterceptState& st) {
    DevicePatchCaps& caps = st.caps;
    caps = DevicePatchCaps{};
    caps.instanceApiVersion = st.instanceApiVersion;

    vkGetPhysicalDeviceProperties(physicalDevice, &st.props);
    st.propsValid = true;
    caps.physicalApiVersion = st.props.apiVersion;

    uint32_t familyCount = 0;
    vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &familyCount, nullptr);
    std::vector<VkQueueFamilyProperties> families(familyCount);
    vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &familyCount, families.data());
    for (uint32_t i = 0; i < familyCount; ++i) {
        QueueFamilyInfo f;
        f.index = i; f.flags = families[i].queueFlags; f.count = families[i].queueCount;
        f.timestampValidBits = families[i].timestampValidBits;
        caps.families.push_back(f);
        Log("queue-family %u: flags=0x%08x count=%u timestampValidBits=%u", i, f.flags, f.count, f.timestampValidBits);
    }

    uint32_t extCount = 0;
    if (vkEnumerateDeviceExtensionProperties(physicalDevice, nullptr, &extCount, nullptr) == VK_SUCCESS) {
        std::vector<VkExtensionProperties> exts(extCount);
        if (vkEnumerateDeviceExtensionProperties(physicalDevice, nullptr, &extCount, exts.data()) == VK_SUCCESS)
            for (const auto& e : exts) caps.availableExtensions.emplace_back(e.extensionName);
    }
    const uint32_t effectiveApi = caps.EffectiveApiVersion();
    const bool core11 = effectiveApi >= VK_API_VERSION_1_1;
    if (!core11 && !(caps.physicalApiVersion >= VK_API_VERSION_1_1)) {
        Log("physical device API < 1.1; feature query limited to core features");
        return;
    }

    // Supported features via individual extension structs (each only chained when its
    // extension exists, so the query is legal on any driver).
    VkPhysicalDeviceFeatures2 f2 = {}; f2.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;
    VkPhysicalDeviceTimelineSemaphoreFeatures timeline = {}; timeline.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES;
    VkPhysicalDeviceBufferDeviceAddressFeatures bda = {}; bda.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_BUFFER_DEVICE_ADDRESS_FEATURES;
    VkPhysicalDeviceHostQueryResetFeatures hqr = {}; hqr.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_HOST_QUERY_RESET_FEATURES;
    VkPhysicalDeviceShaderFloat16Int8Features f16 = {}; f16.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_FLOAT16_INT8_FEATURES;
    VkPhysicalDevice16BitStorageFeatures s16 = {}; s16.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_16BIT_STORAGE_FEATURES;
    VkPhysicalDeviceSynchronization2Features sync2 = {}; sync2.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES;
    VkPhysicalDevicePipelineBinaryFeaturesKHR pb = {}; pb.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_FEATURES_KHR;
    VkPhysicalDeviceSamplerYcbcrConversionFeatures ycbcr = {}; ycbcr.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES;
    VkPhysicalDeviceGlobalPriorityQueryFeaturesKHR gpq = {}; gpq.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_GLOBAL_PRIORITY_QUERY_FEATURES_KHR;
    void** link = &f2.pNext;
    auto chain = [&](void* node) { *link = node; link = reinterpret_cast<void**>(&reinterpret_cast<VkBaseOutStructure*>(node)->pNext); };
    const bool phys12 = caps.physicalApiVersion >= VK_API_VERSION_1_2;
    const bool phys13 = caps.physicalApiVersion >= VK_API_VERSION_1_3;
    if (phys12 || caps.HasExtension(VK_KHR_TIMELINE_SEMAPHORE_EXTENSION_NAME)) chain(&timeline);
    if (phys12 || caps.HasExtension(VK_KHR_BUFFER_DEVICE_ADDRESS_EXTENSION_NAME)) chain(&bda);
    if (phys12 || caps.HasExtension(VK_EXT_HOST_QUERY_RESET_EXTENSION_NAME)) chain(&hqr);
    if (phys12 || caps.HasExtension(VK_KHR_SHADER_FLOAT16_INT8_EXTENSION_NAME)) chain(&f16);
    if (caps.physicalApiVersion >= VK_API_VERSION_1_1 || caps.HasExtension(VK_KHR_16BIT_STORAGE_EXTENSION_NAME)) chain(&s16);
    if (phys13 || caps.HasExtension(VK_KHR_SYNCHRONIZATION_2_EXTENSION_NAME)) chain(&sync2);
    if (caps.HasExtension(VK_KHR_PIPELINE_BINARY_EXTENSION_NAME)) chain(&pb);
    if (caps.physicalApiVersion >= VK_API_VERSION_1_1 || caps.HasExtension(VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME)) chain(&ycbcr);
    const bool gpqExt = caps.HasExtension(VK_KHR_GLOBAL_PRIORITY_EXTENSION_NAME) || caps.HasExtension(VK_EXT_GLOBAL_PRIORITY_QUERY_EXTENSION_NAME);
    if (gpqExt) chain(&gpq);
    vkGetPhysicalDeviceFeatures2(physicalDevice, &f2);
    FeatureSet& sup = caps.supported;
    sup.timelineSemaphore = timeline.timelineSemaphore != VK_FALSE;
    sup.bufferDeviceAddress = bda.bufferDeviceAddress != VK_FALSE;
    sup.hostQueryReset = hqr.hostQueryReset != VK_FALSE;
    sup.shaderFloat16 = f16.shaderFloat16 != VK_FALSE;
    sup.shaderInt8 = f16.shaderInt8 != VK_FALSE;
    sup.storageBuffer16BitAccess = s16.storageBuffer16BitAccess != VK_FALSE;
    sup.uniformAndStorageBuffer16BitAccess = s16.uniformAndStorageBuffer16BitAccess != VK_FALSE;
    sup.synchronization2 = sync2.synchronization2 != VK_FALSE;
    sup.pipelineBinaries = pb.pipelineBinaries != VK_FALSE;
    sup.samplerYcbcrConversion = ycbcr.samplerYcbcrConversion != VK_FALSE;
    st.globalPriorityQuerySupported = gpq.globalPriorityQuery != VK_FALSE;

    // Properties2: driver id/name, pipeline binary properties.
    VkPhysicalDeviceProperties2 p2 = {}; p2.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2;
    VkPhysicalDeviceDriverProperties drv = {}; drv.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRIVER_PROPERTIES;
    VkPhysicalDevicePipelineBinaryPropertiesKHR pbp = {}; pbp.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_PROPERTIES_KHR;
    void** plink = &p2.pNext;
    auto pchain = [&](void* node) { *plink = node; plink = reinterpret_cast<void**>(&reinterpret_cast<VkBaseOutStructure*>(node)->pNext); };
    const bool drvOk = phys12 || caps.HasExtension(VK_KHR_DRIVER_PROPERTIES_EXTENSION_NAME);
    if (drvOk) pchain(&drv);
    if (caps.HasExtension(VK_KHR_PIPELINE_BINARY_EXTENSION_NAME)) pchain(&pbp);
    vkGetPhysicalDeviceProperties2(physicalDevice, &p2);
    st.driverProps = drv; st.driverPropsValid = drvOk;
    st.pipelineBinaryProps = pbp; st.pipelineBinaryPropsValid = caps.HasExtension(VK_KHR_PIPELINE_BINARY_EXTENSION_NAME);

    // Global priorities per family (VkQueueFamilyGlobalPriorityPropertiesKHR needs the query feature).
    st.familyGlobalPriorities.assign(familyCount, {});
    if (gpqExt && st.globalPriorityQuerySupported) {
        std::vector<VkQueueFamilyProperties2> fam2(familyCount);
        std::vector<VkQueueFamilyGlobalPriorityPropertiesKHR> gp(familyCount);
        for (uint32_t i = 0; i < familyCount; ++i) {
            gp[i] = {}; gp[i].sType = VK_STRUCTURE_TYPE_QUEUE_FAMILY_GLOBAL_PRIORITY_PROPERTIES_KHR;
            fam2[i] = {}; fam2[i].sType = VK_STRUCTURE_TYPE_QUEUE_FAMILY_PROPERTIES_2; fam2[i].pNext = &gp[i];
        }
        uint32_t n = familyCount;
        vkGetPhysicalDeviceQueueFamilyProperties2(physicalDevice, &n, fam2.data());
        for (uint32_t i = 0; i < n; ++i)
            for (uint32_t k = 0; k < gp[i].priorityCount && k < VK_MAX_GLOBAL_PRIORITY_SIZE_KHR; ++k)
                st.familyGlobalPriorities[i].push_back(static_cast<int32_t>(gp[i].priorities[k]));
    }
    Log("device caps: api=%u.%u.%u instanceApi=%u.%u.%u extensions=%zu timeline=%d bda=%d hostQueryReset=%d f16=%d i8=%d s16=%d/%d sync2=%d pipelineBinary=%d ycbcr=%d gpQuery=%d",
        VK_API_VERSION_MAJOR(caps.physicalApiVersion), VK_API_VERSION_MINOR(caps.physicalApiVersion), VK_API_VERSION_PATCH(caps.physicalApiVersion),
        VK_API_VERSION_MAJOR(caps.instanceApiVersion), VK_API_VERSION_MINOR(caps.instanceApiVersion), VK_API_VERSION_PATCH(caps.instanceApiVersion),
        caps.availableExtensions.size(), sup.timelineSemaphore, sup.bufferDeviceAddress, sup.hostQueryReset, sup.shaderFloat16, sup.shaderInt8,
        sup.storageBuffer16BitAccess, sup.uniformAndStorageBuffer16BitAccess, sup.synchronization2, sup.pipelineBinaries, sup.samplerYcbcrConversion,
        st.globalPriorityQuerySupported);
}

VkResult VKAPI_PTR InterceptCreateDevice(VkPhysicalDevice physicalDevice, const VkDeviceCreateInfo* createInfo,
    const VkAllocationCallbacks* allocator, VkDevice* device)
{
    if (g_next == nullptr || createInfo == nullptr || device == nullptr) return VK_ERROR_INITIALIZATION_FAILED;
    auto next = reinterpret_cast<PFN_vkCreateDevice>(g_next(g_instance, "vkCreateDevice"));
    if (next == nullptr || next == InterceptCreateDevice) return VK_ERROR_INITIALIZATION_FAILED;

    InterceptState& st = Intercept();
    std::lock_guard<std::mutex> lock(st.mutex);
    st.createDeviceSeen = true;
    st.attempts.clear();
    GatherCapabilities(physicalDevice, st);

    // Record Unity's request.
    st.unityExtensions.clear();
    for (uint32_t i = 0; i < createInfo->enabledExtensionCount; ++i)
        if (createInfo->ppEnabledExtensionNames[i]) st.unityExtensions.emplace_back(createInfo->ppEnabledExtensionNames[i]);
    st.unityQueueCreateInfoCount = createInfo->queueCreateInfoCount;
    if (createInfo->queueCreateInfoCount > 0) {
        st.unityQueueFamily = createInfo->pQueueCreateInfos[0].queueFamilyIndex;
        st.unityQueueCount = createInfo->pQueueCreateInfos[0].queueCount;
        st.unityQueuePriority0 = createInfo->pQueueCreateInfos[0].queueCount ? createInfo->pQueueCreateInfos[0].pQueuePriorities[0] : -1.0f;
    }
    for (uint32_t i = 0; i < createInfo->queueCreateInfoCount; ++i)
        Log("unity vkCreateDevice queue[%u]: family=%u count=%u flags=0x%x priority0=%.3f", i,
            createInfo->pQueueCreateInfos[i].queueFamilyIndex, createInfo->pQueueCreateInfos[i].queueCount,
            createInfo->pQueueCreateInfos[i].flags,
            createInfo->pQueueCreateInfos[i].queueCount ? createInfo->pQueueCreateInfos[i].pQueuePriorities[0] : -1.0f);
    st.unityChainTypes.clear();
    for (auto n = static_cast<const VkBaseInStructure*>(createInfo->pNext); n; n = n->pNext) {
        st.unityChainTypes.push_back(static_cast<uint32_t>(n->sType));
        Log("unity vkCreateDevice pNext: sType=%u knownSize=%zu", static_cast<unsigned>(n->sType), KnownChainNodeSize(n->sType));
    }
    for (const auto& e : st.unityExtensions) Log("unity vkCreateDevice extension: %s", e.c_str());

    DevicePatchOptions options;
    options.injectQueue = !SystemPropertyEnabled("debug.finalscan.noinject");
    // Global priority applies to the whole family create info (Unity's queue included, VUID 02802),
    // so it is opt-in: adb shell setprop debug.finalscan.globalpriority 1
    options.globalPriorityLow = SystemPropertyEnabled("debug.finalscan.globalpriority");
    st.globalPriorityOptionEnabled = options.globalPriorityLow;
    const bool gpExt = st.caps.HasExtension(VK_KHR_GLOBAL_PRIORITY_EXTENSION_NAME) || st.caps.HasExtension(VK_EXT_GLOBAL_PRIORITY_EXTENSION_NAME);

    std::vector<PatchVariant> variants;
    if (options.globalPriorityLow && gpExt) variants.push_back(PatchVariant::InjectWithGlobalPriority);
    variants.push_back(PatchVariant::Inject);
    variants.push_back(PatchVariant::FeaturesOnly);
    variants.push_back(PatchVariant::Original);

    VkResult result = VK_ERROR_INITIALIZATION_FAILED;
    st.injectionAttempted = false;
    st.injectionSucceeded = false;
    st.globalPriorityUsed = false;
    st.path = "none";
    st.injectedFamily = st.injectedIndex = UINT32_MAX;
    for (PatchVariant variant : variants) {
        DevicePatchStorage storage;
        DevicePatchResult patch;
        const VkDeviceCreateInfo* info = BuildPatchedDeviceCreateInfo(createInfo, st.caps, options, variant, storage, patch);
        if (info == nullptr) continue;
        // Inject variants that could not find a safe family collapse into FeaturesOnly; skip duplicates.
        if ((variant == PatchVariant::Inject || variant == PatchVariant::InjectWithGlobalPriority) && !patch.queueInjected) {
            Log("variant %s: no safe family to inject; skipping to next variant", PatchVariantName(variant));
            for (const auto& n : patch.notes) Log("  note: %s", n.c_str());
            continue;
        }
        for (const auto& n : patch.notes) Log("variant %s note: %s", PatchVariantName(variant), n.c_str());
        for (const auto& e : patch.addedExtensions) Log("variant %s adds extension %s", PatchVariantName(variant), e.c_str());
        if (patch.queueInjected) st.injectionAttempted = true;
        *device = VK_NULL_HANDLE;
        result = next(physicalDevice, info, allocator, device);
        CreateAttempt attempt;
        attempt.variant = PatchVariantName(variant);
        attempt.result = result;
        attempt.injected = patch.queueInjected;
        attempt.globalPriority = patch.globalPriorityAttached;
        st.attempts.push_back(attempt);
        Log("vkCreateDevice attempt %s: result=%d (%s) injected=%d globalPriority=%d extensions=%u",
            attempt.variant.c_str(), static_cast<int>(result), VkResultName(result), attempt.injected, attempt.globalPriority, info->enabledExtensionCount);
        if (result == VK_SUCCESS) {
            st.path = attempt.variant;
            st.injectionSucceeded = patch.queueInjected;
            st.globalPriorityUsed = patch.globalPriorityAttached;
            st.injectedFamily = patch.injectedFamily;
            st.injectedIndex = patch.injectedIndex;
            st.finalPatch = patch;
            break;
        }
        LogError("vkCreateDevice attempt %s failed (%d); retrying with a smaller patch", attempt.variant.c_str(), static_cast<int>(result));
    }
    st.createResult = result;
    Log("vkCreateDevice verdict: path=%s injected=%d family=%u index=%u globalPriority=%d result=%d",
        st.path.c_str(), st.injectionSucceeded, st.injectedFamily, st.injectedIndex, st.globalPriorityUsed, static_cast<int>(result));
    return result;
}

PFN_vkVoidFunction VKAPI_PTR InterceptGetInstanceProcAddr(VkInstance instance, const char* name) {
    if (instance != VK_NULL_HANDLE) g_instance = instance;
    if (name != nullptr) {
        if (std::strcmp(name, "vkCreateDevice") == 0) return reinterpret_cast<PFN_vkVoidFunction>(InterceptCreateDevice);
        if (std::strcmp(name, "vkCreateInstance") == 0) return reinterpret_cast<PFN_vkVoidFunction>(InterceptCreateInstance);
    }
    return g_next == nullptr ? nullptr : g_next(instance, name);
}

} // namespace

PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API InterceptInitialization(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void*) {
    g_next = getInstanceProcAddr;
    Log("Vulkan intercept initialization callback invoked");
    return InterceptGetInstanceProcAddr;
}

const char* VkResultName(VkResult r) {
    switch (r) {
        case VK_SUCCESS: return "VK_SUCCESS";
        case VK_NOT_READY: return "VK_NOT_READY";
        case VK_TIMEOUT: return "VK_TIMEOUT";
        case VK_INCOMPLETE: return "VK_INCOMPLETE";
        case VK_ERROR_OUT_OF_HOST_MEMORY: return "VK_ERROR_OUT_OF_HOST_MEMORY";
        case VK_ERROR_OUT_OF_DEVICE_MEMORY: return "VK_ERROR_OUT_OF_DEVICE_MEMORY";
        case VK_ERROR_INITIALIZATION_FAILED: return "VK_ERROR_INITIALIZATION_FAILED";
        case VK_ERROR_DEVICE_LOST: return "VK_ERROR_DEVICE_LOST";
        case VK_ERROR_EXTENSION_NOT_PRESENT: return "VK_ERROR_EXTENSION_NOT_PRESENT";
        case VK_ERROR_FEATURE_NOT_PRESENT: return "VK_ERROR_FEATURE_NOT_PRESENT";
        case VK_ERROR_TOO_MANY_OBJECTS: return "VK_ERROR_TOO_MANY_OBJECTS";
        case VK_ERROR_FORMAT_NOT_SUPPORTED: return "VK_ERROR_FORMAT_NOT_SUPPORTED";
        case VK_ERROR_INVALID_EXTERNAL_HANDLE: return "VK_ERROR_INVALID_EXTERNAL_HANDLE";
        case VK_ERROR_NOT_PERMITTED_KHR: return "VK_ERROR_NOT_PERMITTED";
        case VK_ERROR_INVALID_SHADER_NV: return "VK_ERROR_INVALID_SHADER";
        default:
            if (r == VK_PIPELINE_BINARY_MISSING_KHR) return "VK_PIPELINE_BINARY_MISSING_KHR";
            if (r == VK_ERROR_NOT_ENOUGH_SPACE_KHR) return "VK_ERROR_NOT_ENOUGH_SPACE_KHR";
            return "VK_RESULT_OTHER";
    }
}

} // namespace fs
