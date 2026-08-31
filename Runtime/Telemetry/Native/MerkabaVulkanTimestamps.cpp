#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include "IUnityLog.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstring>
#include <cstdio>
#include <cstdint>
#include <mutex>
#include <string>
#include <vector>

namespace
{
    enum MerkabaExecutorResource : int32_t
    {
        kResourceHashEntries = 0,
        kResourceOwnerRecords,
        kResourceBlockChunkRefs,
        kResourceBlockPresenceL0,
        kResourceBlockPresenceL1,
        kResourceBlockPresenceL2,
        kResourceChunkTileRefs,
        kResourceChunkPresence,
        kResourceKernelStates0,
        kResourceKernelStates1,
        kResourceKernelStates2,
        kResourceKernelStates3,
        kResourceTileBits,
        kResourceTileRecords,
        kResourceFreeTileStack,
        kResourceCounters,
        kResourceClaimQueue,
        kResourcePendingNewTileRefs,
        kResourceLoadRequests,
        kResourceLoadRequestReadCount,
        kResourceSurfaceCandidates,
        kResourceSurfaceQueue,
        kResourceSurfaceWinnerRanks0,
        kResourceSurfaceWinnerRanks1,
        kResourceSurfaceWinnerRanks2,
        kResourceSurfaceWinnerRanks3,
        kResourceTouchedTileQueue,
        kResourceCarveTiles,
        kResourceObservationDispatchArgs,
        kResourceCarveDispatchArgs,
        kResourceAttemptCompletion,
        kResourceRefineMetrics,
        kResourceRawDepth,
        kResourceRefinedDepth,
        kResourceNormals,
        kResourceDilationA,
        kResourceDilationB,
        kResourceCameraLeft,
        kResourceCameraRight,
        kResourceVisibleTiles,
        kResourceFrameDispatchArgs,
        kResourceReadoutVertices0,
        kResourceReadoutVertices1,
        kResourceDrawArgs,
        kResourceCount,
    };

    enum MerkabaEmbeddedDescriptorKind : uint32_t
    {
        kEmbeddedStorageBuffer = 0,
        kEmbeddedSampledImage,
        kEmbeddedStorageImage,
        kEmbeddedUniformBuffer,
        kEmbeddedBilinearSampler,
        kEmbeddedPointSampler,
    };

    struct MerkabaEmbeddedDescriptor
    {
        uint32_t binding;
        uint32_t kind;
        int32_t resource;
    };

    struct MerkabaEmbeddedUniform
    {
        const char* name;
        uint32_t offset;
    };

    struct MerkabaEmbeddedPipeline
    {
        const char* label;
        const char* entryPoint;
        const char* dispatch;
        const uint32_t* words;
        uint32_t wordCount;
        const MerkabaEmbeddedDescriptor* descriptors;
        uint32_t descriptorCount;
        const MerkabaEmbeddedUniform* uniforms;
        uint32_t uniformCount;
        uint32_t globalSize;
    };

#include "MerkabaNativeExecutorShaders.inc"

    static_assert(kMerkabaExecutorResourceCount == kResourceCount,
        "C#/native M8 executor resource ABI mismatch");

    constexpr uint32_t kExecutorAbiVersion = 1;
    constexpr uint32_t kObservationPipelineEnd = 33;
    constexpr uint32_t kReadoutPipelineBegin = 33;
    constexpr uint32_t kFineErasePipelineBegin = 40;
    constexpr uint32_t kMaximumExecutorQueries =
        kMerkabaExecutorPipelineCount * 2 + 2;

    enum ExecutorJobKind : uint32_t
    {
        kJobObservationNew = 0,
        kJobObservationRetry = 1,
        kJobReadout = 2,
        kJobFineErase = 3,
    };

    struct MerkabaUniformValue
    {
        uint32_t nameHash;
        uint32_t offset;
        uint32_t size;
        uint32_t reserved;
    };

    struct MerkabaExecutorJobDescriptor
    {
        uint32_t structSize;
        uint32_t abiVersion;
        uint32_t kind;
        uint32_t revision;
        uint32_t resourceCount;
        void* const* resources;
        uint32_t uniformValueCount;
        const MerkabaUniformValue* uniformValues;
        const uint8_t* uniformData;
        uint32_t uniformDataSize;
        uint32_t depthGroupsX;
        uint32_t depthGroupsY;
        uint32_t queryGroups;
        uint32_t readoutQueryGroups;
    };

    enum ExecutorJobState : int
    {
        kJobCreated = 0,
        kJobPreparing,
        kJobPrepared,
        kJobSubmitted,
        kJobNativeComplete,
        kJobAcquiring,
        kJobComplete,
        kJobFailedNeedsGraphicsCompletion,
        kJobFailedSafe,
    };

    struct ExecutorPipeline
    {
        VkDescriptorSetLayout descriptorSetLayout = VK_NULL_HANDLE;
        VkPipelineLayout pipelineLayout = VK_NULL_HANDLE;
        VkPipeline pipeline = VK_NULL_HANDLE;
    };

    struct ExecutorJob
    {
        std::atomic<int> state{kJobCreated};
        uint32_t kind = kJobObservationNew;
        uint32_t revision = 0;
        std::array<void*, kResourceCount> nativeResources = {};
        std::array<UnityVulkanBuffer, kResourceCount> buffers = {};
        std::array<UnityVulkanImage, kResourceCount> images = {};
        std::array<VkImageView, kResourceCount> imageViews = {};
        std::vector<MerkabaUniformValue> uniformValues;
        std::vector<uint8_t> uniformData;
        uint32_t depthGroupsX = 0;
        uint32_t depthGroupsY = 0;
        uint32_t queryGroups = 0;
        uint32_t readoutQueryGroups = 0;
        VkBuffer uniformBuffer = VK_NULL_HANDLE;
        VkDeviceMemory uniformMemory = VK_NULL_HANDLE;
        VkMemoryPropertyFlags uniformMemoryFlags = 0;
        std::array<VkDeviceSize, kMerkabaExecutorPipelineCount>
            uniformOffsets = {};
        VkDescriptorPool descriptorPool = VK_NULL_HANDLE;
        std::array<VkDescriptorSet, kMerkabaExecutorPipelineCount>
            descriptorSets = {};
        VkCommandBuffer commandBuffer = VK_NULL_HANDLE;
        VkSemaphore graphicsReady = VK_NULL_HANDLE;
        VkSemaphore nativeDone = VK_NULL_HANDLE;
        VkFence graphicsFence = VK_NULL_HANDLE;
        VkFence nativeFence = VK_NULL_HANDLE;
        VkFence acquireFence = VK_NULL_HANDLE;
        VkQueryPool queryPool = VK_NULL_HANDLE;
        std::array<uint64_t, kMaximumExecutorQueries> timestamps = {};
        uint32_t firstPipeline = 0;
        uint32_t lastPipeline = 0;
        uint32_t queryCount = 0;
        VkResult error = VK_SUCCESS;
        bool graphicsSubmitted = false;
        bool timingsReady = false;
        bool terminalLogged = false;
        uint64_t createdNs = 0;
        uint64_t prepareStartNs = 0;
        uint64_t preparedNs = 0;
        uint64_t submittedNs = 0;
        uint64_t nativeCompleteNs = 0;
        uint64_t acquireSubmittedNs = 0;
        uint64_t completeNs = 0;
    };

    constexpr uint32_t kMaximumEntries = 4096;
    constexpr uint32_t kOwnerCount = 6;
    constexpr uint32_t kOwnerStride = 2 + kMaximumEntries * 2;
    constexpr uint32_t kMaximumQueries = kOwnerCount * kOwnerStride;
    constexpr uint32_t kNoEntry = UINT32_MAX;

    enum CaptureState : int
    {
        kUnavailable = 0,
        kIdle,
        kPreparing,
        kArmed,
        kRecording,
        kRecorded,
    };

    enum EventOffset : int
    {
        kSubmissionBegin = 0,
        kDispatchBegin,
        kDispatchEnd,
        kCopyBegin,
        kCopyEnd,
        kDrawBegin,
        kDrawEnd,
        kSubmissionEnd,
        kEventCount,
    };

    enum EntryKind : uint32_t
    {
        kEntryNone = 0,
        kEntryCompute,
        kEntryCopy,
        kEntryDraw,
    };

    IUnityInterfaces* g_interfaces = nullptr;
    IUnityGraphics* g_graphics = nullptr;
    IUnityGraphicsVulkanV2* g_vulkan = nullptr;
    IUnityLog* g_log = nullptr;
    UnityVulkanInstance g_instance = {};
    PFN_vkGetInstanceProcAddr g_nextGetInstanceProcAddr = nullptr;
    VkInstance g_interceptInstance = VK_NULL_HANDLE;
    uint32_t g_injectedQueueFamily = UINT32_MAX;
    uint32_t g_injectedQueueIndex = UINT32_MAX;
    bool g_interceptInstalled = false;
    bool g_queueInjected = false;
    VkQueue g_scannerQueue = VK_NULL_HANDLE;
    VkCommandPool g_executorCommandPool = VK_NULL_HANDLE;
    VkPhysicalDeviceMemoryProperties g_memoryProperties = {};
    VkPhysicalDeviceProperties g_deviceProperties = {};
    std::array<ExecutorPipeline, kMerkabaExecutorPipelineCount>
        g_executorPipelines = {};
    VkSampler g_bilinearSampler = VK_NULL_HANDLE;
    VkSampler g_pointSampler = VK_NULL_HANDLE;
    std::mutex g_executorMutex;
    std::vector<ExecutorJob*> g_executorJobs;
    int g_executorEventBase = 0;
    bool g_executorEventsReserved = false;
    bool g_executorReady = false;
    VkQueryPool g_queryPool = VK_NULL_HANDLE;
    std::atomic<int> g_state{kUnavailable};
    uint32_t g_entryCount = 0;
    uint32_t g_recordedEntries = 0;
    uint32_t g_openEntry = kNoEntry;
    EntryKind g_openKind = kEntryNone;
    uint32_t g_overflow = 0;
    uint32_t g_activeOwner = kOwnerCount;
    uint64_t g_revision = 0;
    double g_timestampPeriod = 0.0;
    uint32_t g_timestampValidBits = 0;
    uint64_t g_queryScratch[kOwnerStride * 2] = {};
    int g_eventBase = 0;
    bool g_eventsReserved = false;

    void Log(const char* message)
    {
        if (g_log != nullptr && message != nullptr)
            UNITY_LOG(g_log, message);
    }

    void LogError(const char* operation, VkResult result)
    {
        if (g_log == nullptr)
            return;
        char message[384] = {};
        std::snprintf(message, sizeof(message),
            "Merkaba native scanner: %s failed VkResult=%d", operation,
            static_cast<int>(result));
        UNITY_LOG_ERROR(g_log, message);
    }

    uint64_t MonotonicNs()
    {
        return static_cast<uint64_t>(std::chrono::duration_cast<
            std::chrono::nanoseconds>(
                std::chrono::steady_clock::now().time_since_epoch()).count());
    }

    PFN_vkVoidFunction VKAPI_PTR InterceptGetInstanceProcAddr(
        VkInstance instance, const char* name);

    VkResult VKAPI_PTR InterceptCreateDevice(VkPhysicalDevice physicalDevice,
        const VkDeviceCreateInfo* createInfo,
        const VkAllocationCallbacks* allocator, VkDevice* device)
    {
        if (g_nextGetInstanceProcAddr == nullptr || createInfo == nullptr ||
            device == nullptr)
            return VK_ERROR_INITIALIZATION_FAILED;
        auto nextCreateDevice = reinterpret_cast<PFN_vkCreateDevice>(
            g_nextGetInstanceProcAddr(g_interceptInstance, "vkCreateDevice"));
        if (nextCreateDevice == nullptr ||
            nextCreateDevice == InterceptCreateDevice)
            return VK_ERROR_INITIALIZATION_FAILED;

        uint32_t familyCount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice,
            &familyCount, nullptr);
        std::vector<VkQueueFamilyProperties> families(familyCount);
        vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice,
            &familyCount, families.data());

        if (g_log != nullptr)
        {
            for (uint32_t family = 0; family < familyCount; ++family)
            {
                char message[384] = {};
                std::snprintf(message, sizeof(message),
                    "Merkaba queue-family: family=%u flags=0x%08x "
                    "physical=%u timestampBits=%u",
                    family, families[family].queueFlags,
                    families[family].queueCount,
                    families[family].timestampValidBits);
                UNITY_LOG(g_log, message);
            }
        }

        bool globalPriorityEnabled = false;
        for (uint32_t index = 0;
            index < createInfo->enabledExtensionCount; ++index)
        {
            const char* extension = createInfo->ppEnabledExtensionNames[index];
            if (extension != nullptr &&
                std::strcmp(extension, VK_EXT_GLOBAL_PRIORITY_EXTENSION_NAME) == 0)
                globalPriorityEnabled = true;
        }

        std::vector<VkDeviceQueueCreateInfo> queueInfos(
            createInfo->pQueueCreateInfos,
            createInfo->pQueueCreateInfos + createInfo->queueCreateInfoCount);
        std::vector<std::vector<float>> priorities(queueInfos.size());
        uint32_t selected = UINT32_MAX;
        uint32_t safeCandidates = 0;
        for (uint32_t index = 0; index < queueInfos.size(); ++index)
        {
            VkDeviceQueueCreateInfo& info = queueInfos[index];
            priorities[index].assign(info.pQueuePriorities,
                info.pQueuePriorities + info.queueCount);
            info.pQueuePriorities = priorities[index].data();
            bool familyValid = info.queueFamilyIndex < families.size();
            VkQueueFlags flags = familyValid
                ? families[info.queueFamilyIndex].queueFlags : 0;
            uint32_t physicalCount = familyValid
                ? families[info.queueFamilyIndex].queueCount : 0;
            if (g_log != nullptr)
            {
                char message[512] = {};
                std::snprintf(message, sizeof(message),
                    "Merkaba vkCreateDevice queue[%u]: family=%u "
                    "requested=%u flags=0x%08x physical=%u createFlags=0x%08x "
                    "priority0=%.3f globalPriorityExt=%u",
                    index, info.queueFamilyIndex, info.queueCount, flags,
                    physicalCount, info.flags,
                    info.queueCount != 0u ? info.pQueuePriorities[0] : -1.0f,
                    globalPriorityEnabled ? 1u : 0u);
                UNITY_LOG(g_log, message);
            }
            const VkQueueFlags required =
                VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT;
            bool safe = familyValid && info.queueCount == 1u &&
                physicalCount >= 2u && (flags & required) == required &&
                (info.flags & VK_DEVICE_QUEUE_CREATE_PROTECTED_BIT) == 0u;
            if (safe)
            {
                selected = index;
                safeCandidates++;
            }
        }

        VkDeviceCreateInfo modified = *createInfo;
        bool inject = safeCandidates == 1u;
        if (inject)
        {
            VkDeviceQueueCreateInfo& info = queueInfos[selected];
            g_injectedQueueFamily = info.queueFamilyIndex;
            g_injectedQueueIndex = 1u;
            priorities[selected].push_back(0.1f);
            info.queueCount = 2u;
            info.pQueuePriorities = priorities[selected].data();
            modified.pQueueCreateInfos = queueInfos.data();
            modified.queueCreateInfoCount =
                static_cast<uint32_t>(queueInfos.size());
        }
        else
        {
            g_injectedQueueFamily = UINT32_MAX;
            g_injectedQueueIndex = UINT32_MAX;
        }

        VkResult result = nextCreateDevice(physicalDevice,
            inject ? &modified : createInfo, allocator, device);
        g_queueInjected = result == VK_SUCCESS && inject;
        if (result != VK_SUCCESS && inject)
        {
            LogError("injected vkCreateDevice", result);
            g_injectedQueueFamily = UINT32_MAX;
            g_injectedQueueIndex = UINT32_MAX;
            g_queueInjected = false;
            result = nextCreateDevice(physicalDevice, createInfo, allocator,
                device);
        }
        if (g_log != nullptr)
        {
            char message[512] = {};
            std::snprintf(message, sizeof(message),
                "Merkaba vkCreateDevice verdict: safeCandidates=%u "
                "injected=%u family=%u queueIndex=%u queue1Priority=0.100 "
                "globalPriorityClassShared=%u result=%d",
                safeCandidates, g_queueInjected ? 1u : 0u,
                g_injectedQueueFamily, g_injectedQueueIndex,
                globalPriorityEnabled ? 1u : 0u,
                static_cast<int>(result));
            UNITY_LOG(g_log, message);
        }
        return result;
    }

    PFN_vkVoidFunction VKAPI_PTR InterceptGetInstanceProcAddr(
        VkInstance instance, const char* name)
    {
        if (instance != VK_NULL_HANDLE)
            g_interceptInstance = instance;
        if (name != nullptr && std::strcmp(name, "vkCreateDevice") == 0)
            return reinterpret_cast<PFN_vkVoidFunction>(InterceptCreateDevice);
        return g_nextGetInstanceProcAddr == nullptr
            ? nullptr : g_nextGetInstanceProcAddr(instance, name);
    }

    PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API InterceptInitialization(
        PFN_vkGetInstanceProcAddr getInstanceProcAddr, void*)
    {
        g_nextGetInstanceProcAddr = getInstanceProcAddr;
        return InterceptGetInstanceProcAddr;
    }

    VkDeviceSize AlignUp(VkDeviceSize value, VkDeviceSize alignment)
    {
        return alignment == 0 ? value :
            (value + alignment - 1) & ~(alignment - 1);
    }

    uint32_t NameHash(const char* value)
    {
        uint32_t hash = 2166136261u;
        for (const uint8_t* byte = reinterpret_cast<const uint8_t*>(value);
            byte != nullptr && *byte != 0; ++byte)
            hash = (hash ^ *byte) * 16777619u;
        return hash;
    }

    bool FindMemoryType(uint32_t typeBits, VkMemoryPropertyFlags required,
        VkMemoryPropertyFlags preferred, uint32_t* memoryType,
        VkMemoryPropertyFlags* actualFlags)
    {
        int fallback = -1;
        for (uint32_t index = 0;
            index < g_memoryProperties.memoryTypeCount; ++index)
        {
            if ((typeBits & (1u << index)) == 0)
                continue;
            VkMemoryPropertyFlags flags =
                g_memoryProperties.memoryTypes[index].propertyFlags;
            if ((flags & required) != required)
                continue;
            if ((flags & preferred) == preferred)
            {
                *memoryType = index;
                *actualFlags = flags;
                return true;
            }
            if (fallback < 0)
                fallback = static_cast<int>(index);
        }
        if (fallback < 0)
            return false;
        *memoryType = static_cast<uint32_t>(fallback);
        *actualFlags =
            g_memoryProperties.memoryTypes[fallback].propertyFlags;
        return true;
    }

    VkDescriptorType DescriptorType(uint32_t kind)
    {
        if (kind == kEmbeddedSampledImage)
            return VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
        if (kind == kEmbeddedStorageImage)
            return VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
        if (kind == kEmbeddedUniformBuffer)
            return VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
        if (kind == kEmbeddedBilinearSampler || kind == kEmbeddedPointSampler)
            return VK_DESCRIPTOR_TYPE_SAMPLER;
        return VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    }

    void DestroyExecutorPipeline(ExecutorPipeline& pipeline)
    {
        if (g_instance.device == VK_NULL_HANDLE)
            return;
        if (pipeline.pipeline != VK_NULL_HANDLE)
            vkDestroyPipeline(g_instance.device, pipeline.pipeline, nullptr);
        if (pipeline.pipelineLayout != VK_NULL_HANDLE)
            vkDestroyPipelineLayout(g_instance.device,
                pipeline.pipelineLayout, nullptr);
        if (pipeline.descriptorSetLayout != VK_NULL_HANDLE)
            vkDestroyDescriptorSetLayout(g_instance.device,
                pipeline.descriptorSetLayout, nullptr);
        pipeline = {};
    }

    bool CreateExecutorPipelines()
    {
        for (uint32_t index = 0; index < kMerkabaExecutorPipelineCount;
            ++index)
        {
            const MerkabaEmbeddedPipeline& embedded =
                kMerkabaExecutorPipelines[index];
            std::vector<VkDescriptorSetLayoutBinding> bindings;
            bindings.reserve(embedded.descriptorCount);
            for (uint32_t descriptorIndex = 0;
                descriptorIndex < embedded.descriptorCount; ++descriptorIndex)
            {
                const MerkabaEmbeddedDescriptor& descriptor =
                    embedded.descriptors[descriptorIndex];
                VkDescriptorSetLayoutBinding binding = {};
                binding.binding = descriptor.binding;
                binding.descriptorCount = 1;
                binding.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
                binding.descriptorType = DescriptorType(descriptor.kind);
                bindings.push_back(binding);
            }
            VkDescriptorSetLayoutCreateInfo setInfo = {};
            setInfo.sType =
                VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
            setInfo.bindingCount = static_cast<uint32_t>(bindings.size());
            setInfo.pBindings = bindings.data();
            VkResult result = vkCreateDescriptorSetLayout(g_instance.device,
                &setInfo, nullptr,
                &g_executorPipelines[index].descriptorSetLayout);
            if (result != VK_SUCCESS)
            {
                LogError("vkCreateDescriptorSetLayout", result);
                return false;
            }
            VkPipelineLayoutCreateInfo layoutInfo = {};
            layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
            layoutInfo.setLayoutCount = 1;
            layoutInfo.pSetLayouts =
                &g_executorPipelines[index].descriptorSetLayout;
            result = vkCreatePipelineLayout(g_instance.device, &layoutInfo,
                nullptr, &g_executorPipelines[index].pipelineLayout);
            if (result != VK_SUCCESS)
            {
                LogError("vkCreatePipelineLayout", result);
                return false;
            }
            VkShaderModuleCreateInfo moduleInfo = {};
            moduleInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
            moduleInfo.codeSize = embedded.wordCount * sizeof(uint32_t);
            moduleInfo.pCode = embedded.words;
            VkShaderModule module = VK_NULL_HANDLE;
            result = vkCreateShaderModule(g_instance.device, &moduleInfo,
                nullptr, &module);
            if (result != VK_SUCCESS)
            {
                LogError("vkCreateShaderModule", result);
                return false;
            }
            VkPipelineShaderStageCreateInfo stage = {};
            stage.sType =
                VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
            stage.stage = VK_SHADER_STAGE_COMPUTE_BIT;
            stage.module = module;
            stage.pName = embedded.entryPoint;
            VkComputePipelineCreateInfo pipelineInfo = {};
            pipelineInfo.sType =
                VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO;
            pipelineInfo.stage = stage;
            pipelineInfo.layout = g_executorPipelines[index].pipelineLayout;
            result = vkCreateComputePipelines(g_instance.device,
                VK_NULL_HANDLE, 1, &pipelineInfo, nullptr,
                &g_executorPipelines[index].pipeline);
            vkDestroyShaderModule(g_instance.device, module, nullptr);
            if (result != VK_SUCCESS)
            {
                LogError("vkCreateComputePipelines", result);
                return false;
            }
        }

        VkSamplerCreateInfo sampler = {};
        sampler.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        sampler.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        sampler.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        sampler.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        sampler.minLod = 0.0f;
        sampler.maxLod = 0.0f;
        sampler.minFilter = sampler.magFilter = VK_FILTER_LINEAR;
        sampler.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
        VkResult result = vkCreateSampler(g_instance.device, &sampler,
            nullptr, &g_bilinearSampler);
        sampler.minFilter = sampler.magFilter = VK_FILTER_NEAREST;
        if (result == VK_SUCCESS)
            result = vkCreateSampler(g_instance.device, &sampler, nullptr,
                &g_pointSampler);
        if (result != VK_SUCCESS)
        {
            LogError("vkCreateSampler", result);
            return false;
        }
        return true;
    }

    bool IsTextureResource(uint32_t resource)
    {
        return resource >= static_cast<uint32_t>(kResourceRawDepth) &&
            resource <= static_cast<uint32_t>(kResourceCameraRight);
    }

    bool IsStorageImageResource(uint32_t resource)
    {
        return resource >= static_cast<uint32_t>(kResourceRefinedDepth) &&
            resource <= static_cast<uint32_t>(kResourceDilationB);
    }

    bool PipelineRangeForKind(uint32_t kind, uint32_t* first,
        uint32_t* last)
    {
        if (kind == kJobObservationNew)
        {
            *first = 0;
            *last = kObservationPipelineEnd;
            return true;
        }
        if (kind == kJobObservationRetry)
        {
            *first = 13;
            *last = kObservationPipelineEnd;
            return true;
        }
        if (kind == kJobReadout)
        {
            *first = kReadoutPipelineBegin;
            *last = kFineErasePipelineBegin;
            return true;
        }
        if (kind == kJobFineErase)
        {
            *first = kFineErasePipelineBegin;
            *last = kMerkabaExecutorPipelineCount;
            return true;
        }
        return false;
    }

    void FailJob(ExecutorJob* job, VkResult result, const char* operation,
        bool graphicsCompletionRequired)
    {
        if (job == nullptr)
            return;
        job->error = result;
        job->state.store(graphicsCompletionRequired
                ? kJobFailedNeedsGraphicsCompletion : kJobFailedSafe,
            std::memory_order_release);
        LogError(operation, result);
    }

    void DestroyJobObjects(ExecutorJob* job)
    {
        if (job == nullptr || g_instance.device == VK_NULL_HANDLE)
            return;
        for (VkImageView& view : job->imageViews)
        {
            if (view != VK_NULL_HANDLE)
                vkDestroyImageView(g_instance.device, view, nullptr);
            view = VK_NULL_HANDLE;
        }
        if (job->descriptorPool != VK_NULL_HANDLE)
            vkDestroyDescriptorPool(g_instance.device, job->descriptorPool,
                nullptr);
        if (job->uniformBuffer != VK_NULL_HANDLE)
            vkDestroyBuffer(g_instance.device, job->uniformBuffer, nullptr);
        if (job->uniformMemory != VK_NULL_HANDLE)
            vkFreeMemory(g_instance.device, job->uniformMemory, nullptr);
        if (job->queryPool != VK_NULL_HANDLE)
            vkDestroyQueryPool(g_instance.device, job->queryPool, nullptr);
        if (job->graphicsReady != VK_NULL_HANDLE)
            vkDestroySemaphore(g_instance.device, job->graphicsReady,
                nullptr);
        if (job->nativeDone != VK_NULL_HANDLE)
            vkDestroySemaphore(g_instance.device, job->nativeDone, nullptr);
        if (job->graphicsFence != VK_NULL_HANDLE)
            vkDestroyFence(g_instance.device, job->graphicsFence, nullptr);
        if (job->nativeFence != VK_NULL_HANDLE)
            vkDestroyFence(g_instance.device, job->nativeFence, nullptr);
        if (job->acquireFence != VK_NULL_HANDLE)
            vkDestroyFence(g_instance.device, job->acquireFence, nullptr);
        if (job->commandBuffer != VK_NULL_HANDLE &&
            g_executorCommandPool != VK_NULL_HANDLE)
            vkFreeCommandBuffers(g_instance.device, g_executorCommandPool, 1,
                &job->commandBuffer);
        job->descriptorPool = VK_NULL_HANDLE;
        job->uniformBuffer = VK_NULL_HANDLE;
        job->uniformMemory = VK_NULL_HANDLE;
        job->queryPool = VK_NULL_HANDLE;
        job->graphicsReady = VK_NULL_HANDLE;
        job->nativeDone = VK_NULL_HANDLE;
        job->graphicsFence = VK_NULL_HANDLE;
        job->nativeFence = VK_NULL_HANDLE;
        job->acquireFence = VK_NULL_HANDLE;
        job->commandBuffer = VK_NULL_HANDLE;
    }

    std::array<bool, kResourceCount> UsedResources(const ExecutorJob* job)
    {
        std::array<bool, kResourceCount> used = {};
        for (uint32_t pipelineIndex = job->firstPipeline;
            pipelineIndex < job->lastPipeline; ++pipelineIndex)
        {
            const MerkabaEmbeddedPipeline& pipeline =
                kMerkabaExecutorPipelines[pipelineIndex];
            for (uint32_t index = 0; index < pipeline.descriptorCount; ++index)
            {
                int32_t resource = pipeline.descriptors[index].resource;
                if (resource >= 0 && resource < kResourceCount)
                    used[static_cast<uint32_t>(resource)] = true;
            }
        }
        return used;
    }

    bool AccessJobResources(ExecutorJob* job)
    {
        const auto used = UsedResources(job);
        const VkPipelineStageFlags bufferStages =
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
            VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT;
        const VkAccessFlags bufferAccess = VK_ACCESS_SHADER_READ_BIT |
            VK_ACCESS_SHADER_WRITE_BIT |
            VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
        for (uint32_t resource = 0; resource < kResourceCount; ++resource)
        {
            if (!used[resource])
                continue;
            void* nativeResource = job->nativeResources[resource];
            if (nativeResource == nullptr)
            {
                FailJob(job, VK_ERROR_INITIALIZATION_FAILED,
                    "null Unity native resource", false);
                return false;
            }
            if (!IsTextureResource(resource))
            {
                if (!g_vulkan->AccessBuffer(nativeResource, bufferStages,
                        bufferAccess,
                        kUnityVulkanResourceAccess_PipelineBarrier,
                        &job->buffers[resource]))
                {
                    FailJob(job, VK_ERROR_INITIALIZATION_FAILED,
                        "IUnityGraphicsVulkan::AccessBuffer", false);
                    return false;
                }
                continue;
            }

            bool storage = IsStorageImageResource(resource);
            VkImageLayout layout = storage
                ? VK_IMAGE_LAYOUT_GENERAL
                : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            VkAccessFlags access = storage
                ? VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT
                : VK_ACCESS_SHADER_READ_BIT;
            if (!g_vulkan->AccessTexture(nativeResource,
                    UnityVulkanWholeImage, layout,
                    VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, access,
                    kUnityVulkanResourceAccess_PipelineBarrier,
                    &job->images[resource]))
            {
                FailJob(job, VK_ERROR_INITIALIZATION_FAILED,
                    "IUnityGraphicsVulkan::AccessTexture", false);
                return false;
            }
            const UnityVulkanImage& image = job->images[resource];
            VkImageViewCreateInfo viewInfo = {};
            viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
            viewInfo.image = image.image;
            viewInfo.viewType = image.layers > 1
                ? VK_IMAGE_VIEW_TYPE_2D_ARRAY : VK_IMAGE_VIEW_TYPE_2D;
            viewInfo.format = image.format;
            viewInfo.components.r = VK_COMPONENT_SWIZZLE_IDENTITY;
            viewInfo.components.g = VK_COMPONENT_SWIZZLE_IDENTITY;
            viewInfo.components.b = VK_COMPONENT_SWIZZLE_IDENTITY;
            viewInfo.components.a = VK_COMPONENT_SWIZZLE_IDENTITY;
            viewInfo.subresourceRange.aspectMask = image.aspect;
            viewInfo.subresourceRange.baseMipLevel = 0;
            viewInfo.subresourceRange.levelCount = 1;
            viewInfo.subresourceRange.baseArrayLayer = 0;
            viewInfo.subresourceRange.layerCount = image.layers;
            VkResult result = vkCreateImageView(g_instance.device, &viewInfo,
                nullptr, &job->imageViews[resource]);
            if (result != VK_SUCCESS)
            {
                FailJob(job, result, "vkCreateImageView", false);
                return false;
            }
        }
        return true;
    }

    const MerkabaUniformValue* FindUniformValue(const ExecutorJob* job,
        const char* name)
    {
        uint32_t hash = NameHash(name);
        for (const MerkabaUniformValue& value : job->uniformValues)
            if (value.nameHash == hash)
                return &value;
        return nullptr;
    }

    bool CreateJobUniforms(ExecutorJob* job)
    {
        VkDeviceSize alignment = std::max<VkDeviceSize>(16,
            g_deviceProperties.limits.minUniformBufferOffsetAlignment);
        VkDeviceSize requestedSize = 0;
        for (uint32_t pipeline = job->firstPipeline;
            pipeline < job->lastPipeline; ++pipeline)
        {
            job->uniformOffsets[pipeline] = requestedSize;
            requestedSize = AlignUp(requestedSize + std::max(16u,
                kMerkabaExecutorPipelines[pipeline].globalSize), alignment);
        }
        if (requestedSize == 0)
            return true;

        VkBufferCreateInfo bufferInfo = {};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = requestedSize;
        bufferInfo.usage = VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        VkResult result = vkCreateBuffer(g_instance.device, &bufferInfo,
            nullptr, &job->uniformBuffer);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkCreateBuffer(uniforms)", false);
            return false;
        }
        VkMemoryRequirements requirements = {};
        vkGetBufferMemoryRequirements(g_instance.device, job->uniformBuffer,
            &requirements);
        uint32_t memoryType = 0;
        if (!FindMemoryType(requirements.memoryTypeBits,
                VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT,
                VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, &memoryType,
                &job->uniformMemoryFlags))
        {
            FailJob(job, VK_ERROR_FEATURE_NOT_PRESENT,
                "host-visible uniform memory", false);
            return false;
        }
        VkMemoryAllocateInfo allocation = {};
        allocation.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocation.allocationSize = requirements.size;
        allocation.memoryTypeIndex = memoryType;
        result = vkAllocateMemory(g_instance.device, &allocation, nullptr,
            &job->uniformMemory);
        if (result == VK_SUCCESS)
            result = vkBindBufferMemory(g_instance.device,
                job->uniformBuffer, job->uniformMemory, 0);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "uniform allocation/bind", false);
            return false;
        }
        void* mapped = nullptr;
        result = vkMapMemory(g_instance.device, job->uniformMemory, 0,
            requirements.size, 0, &mapped);
        if (result != VK_SUCCESS || mapped == nullptr)
        {
            FailJob(job, result, "vkMapMemory(uniforms)", false);
            return false;
        }
        std::memset(mapped, 0, static_cast<size_t>(requirements.size));
        for (uint32_t pipelineIndex = job->firstPipeline;
            pipelineIndex < job->lastPipeline; ++pipelineIndex)
        {
            const MerkabaEmbeddedPipeline& pipeline =
                kMerkabaExecutorPipelines[pipelineIndex];
            uint8_t* destination = static_cast<uint8_t*>(mapped) +
                job->uniformOffsets[pipelineIndex];
            for (uint32_t index = 0; index < pipeline.uniformCount; ++index)
            {
                const MerkabaEmbeddedUniform& uniform =
                    pipeline.uniforms[index];
                if (pipelineIndex >= 2 && pipelineIndex <= 10 &&
                    std::strcmp(uniform.name, "gsDilateStepSize") == 0)
                {
                    int32_t step = 1 << (10 - pipelineIndex);
                    std::memcpy(destination + uniform.offset, &step,
                        sizeof(step));
                    continue;
                }
                const MerkabaUniformValue* value =
                    FindUniformValue(job, uniform.name);
                if (value == nullptr || value->size == 0 ||
                    value->offset + value->size > job->uniformData.size() ||
                    uniform.offset + value->size > pipeline.globalSize)
                {
                    vkUnmapMemory(g_instance.device, job->uniformMemory);
                    FailJob(job, VK_ERROR_INITIALIZATION_FAILED,
                        "missing/invalid reflected uniform", false);
                    return false;
                }
                std::memcpy(destination + uniform.offset,
                    job->uniformData.data() + value->offset, value->size);
            }
        }
        if ((job->uniformMemoryFlags &
                VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) == 0)
        {
            VkMappedMemoryRange range = {};
            range.sType = VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
            range.memory = job->uniformMemory;
            range.offset = 0;
            range.size = VK_WHOLE_SIZE;
            result = vkFlushMappedMemoryRanges(g_instance.device, 1, &range);
        }
        vkUnmapMemory(g_instance.device, job->uniformMemory);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkFlushMappedMemoryRanges", false);
            return false;
        }
        return true;
    }

    bool CreateJobDescriptors(ExecutorJob* job)
    {
        uint32_t counts[5] = {};
        uint32_t descriptorCount = 0;
        uint32_t setCount = job->lastPipeline - job->firstPipeline;
        for (uint32_t pipelineIndex = job->firstPipeline;
            pipelineIndex < job->lastPipeline; ++pipelineIndex)
        {
            const MerkabaEmbeddedPipeline& pipeline =
                kMerkabaExecutorPipelines[pipelineIndex];
            descriptorCount += pipeline.descriptorCount;
            for (uint32_t index = 0; index < pipeline.descriptorCount; ++index)
            {
                VkDescriptorType type =
                    DescriptorType(pipeline.descriptors[index].kind);
                uint32_t bucket = type == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER
                    ? 0u : type == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE
                    ? 1u : type == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE
                    ? 2u : type == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER
                    ? 3u : 4u;
                counts[bucket]++;
            }
        }
        VkDescriptorType types[5] = {
            VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,
            VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,
            VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,
            VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,
            VK_DESCRIPTOR_TYPE_SAMPLER,
        };
        std::vector<VkDescriptorPoolSize> poolSizes;
        for (uint32_t index = 0; index < 5; ++index)
            if (counts[index] != 0)
                poolSizes.push_back({types[index], counts[index]});
        VkDescriptorPoolCreateInfo poolInfo = {};
        poolInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        poolInfo.maxSets = setCount;
        poolInfo.poolSizeCount =
            static_cast<uint32_t>(poolSizes.size());
        poolInfo.pPoolSizes = poolSizes.data();
        VkResult result = vkCreateDescriptorPool(g_instance.device,
            &poolInfo, nullptr, &job->descriptorPool);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkCreateDescriptorPool", false);
            return false;
        }
        std::vector<VkDescriptorSetLayout> layouts;
        layouts.reserve(setCount);
        for (uint32_t pipelineIndex = job->firstPipeline;
            pipelineIndex < job->lastPipeline; ++pipelineIndex)
            layouts.push_back(
                g_executorPipelines[pipelineIndex].descriptorSetLayout);
        VkDescriptorSetAllocateInfo allocation = {};
        allocation.sType =
            VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
        allocation.descriptorPool = job->descriptorPool;
        allocation.descriptorSetCount = setCount;
        allocation.pSetLayouts = layouts.data();
        result = vkAllocateDescriptorSets(g_instance.device, &allocation,
            job->descriptorSets.data() + job->firstPipeline);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkAllocateDescriptorSets", false);
            return false;
        }

        std::vector<VkWriteDescriptorSet> writes;
        std::vector<VkDescriptorBufferInfo> bufferInfos;
        std::vector<VkDescriptorImageInfo> imageInfos;
        writes.reserve(descriptorCount);
        bufferInfos.reserve(descriptorCount);
        imageInfos.reserve(descriptorCount);
        for (uint32_t pipelineIndex = job->firstPipeline;
            pipelineIndex < job->lastPipeline; ++pipelineIndex)
        {
            const MerkabaEmbeddedPipeline& pipeline =
                kMerkabaExecutorPipelines[pipelineIndex];
            for (uint32_t descriptorIndex = 0;
                descriptorIndex < pipeline.descriptorCount; ++descriptorIndex)
            {
                const MerkabaEmbeddedDescriptor& descriptor =
                    pipeline.descriptors[descriptorIndex];
                VkWriteDescriptorSet write = {};
                write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
                write.dstSet = job->descriptorSets[pipelineIndex];
                write.dstBinding = descriptor.binding;
                write.descriptorCount = 1;
                write.descriptorType = DescriptorType(descriptor.kind);
                if (descriptor.kind == kEmbeddedSampledImage ||
                    descriptor.kind == kEmbeddedStorageImage ||
                    descriptor.kind == kEmbeddedBilinearSampler ||
                    descriptor.kind == kEmbeddedPointSampler)
                {
                    VkDescriptorImageInfo info = {};
                    if (descriptor.kind == kEmbeddedBilinearSampler ||
                        descriptor.kind == kEmbeddedPointSampler)
                        info.sampler = descriptor.kind ==
                            kEmbeddedBilinearSampler
                            ? g_bilinearSampler : g_pointSampler;
                    else
                    {
                        uint32_t resource =
                            static_cast<uint32_t>(descriptor.resource);
                        info.imageView = job->imageViews[resource];
                        info.imageLayout = IsStorageImageResource(resource)
                            ? VK_IMAGE_LAYOUT_GENERAL
                            : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
                    }
                    imageInfos.push_back(info);
                    write.pImageInfo = &imageInfos.back();
                }
                else
                {
                    VkDescriptorBufferInfo info = {};
                    if (descriptor.kind == kEmbeddedUniformBuffer)
                    {
                        info.buffer = job->uniformBuffer;
                        info.offset = job->uniformOffsets[pipelineIndex];
                        info.range = pipeline.globalSize;
                    }
                    else
                    {
                        const UnityVulkanBuffer& buffer =
                            job->buffers[descriptor.resource];
                        info.buffer = buffer.buffer;
                        info.offset = 0;
                        info.range = buffer.sizeInBytes;
                    }
                    bufferInfos.push_back(info);
                    write.pBufferInfo = &bufferInfos.back();
                }
                writes.push_back(write);
            }
        }
        vkUpdateDescriptorSets(g_instance.device,
            static_cast<uint32_t>(writes.size()), writes.data(), 0, nullptr);
        return true;
    }

    bool CreateJobCommandObjects(ExecutorJob* job)
    {
        VkCommandBufferAllocateInfo commandInfo = {};
        commandInfo.sType =
            VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
        commandInfo.commandPool = g_executorCommandPool;
        commandInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        commandInfo.commandBufferCount = 1;
        VkResult result = vkAllocateCommandBuffers(g_instance.device,
            &commandInfo, &job->commandBuffer);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkAllocateCommandBuffers", false);
            return false;
        }
        VkSemaphoreCreateInfo semaphoreInfo = {};
        semaphoreInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;
        result = vkCreateSemaphore(g_instance.device, &semaphoreInfo, nullptr,
            &job->graphicsReady);
        if (result == VK_SUCCESS)
            result = vkCreateSemaphore(g_instance.device, &semaphoreInfo,
                nullptr, &job->nativeDone);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkCreateSemaphore(graphicsReady)", false);
            return false;
        }
        VkFenceCreateInfo fenceInfo = {};
        fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        result = vkCreateFence(g_instance.device, &fenceInfo, nullptr,
            &job->graphicsFence);
        if (result == VK_SUCCESS)
            result = vkCreateFence(g_instance.device, &fenceInfo, nullptr,
                &job->nativeFence);
        if (result == VK_SUCCESS)
            result = vkCreateFence(g_instance.device, &fenceInfo, nullptr,
                &job->acquireFence);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkCreateFence", false);
            return false;
        }
        job->queryCount =
            (job->lastPipeline - job->firstPipeline) * 2u + 2u;
        VkQueryPoolCreateInfo queryInfo = {};
        queryInfo.sType = VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO;
        queryInfo.queryType = VK_QUERY_TYPE_TIMESTAMP;
        queryInfo.queryCount = job->queryCount;
        result = vkCreateQueryPool(g_instance.device, &queryInfo, nullptr,
            &job->queryPool);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkCreateQueryPool(native job)", false);
            return false;
        }
        return true;
    }

    void RecordDispatch(ExecutorJob* job, uint32_t pipelineIndex,
        uint32_t queryIndex)
    {
        const MerkabaEmbeddedPipeline& pipeline =
            kMerkabaExecutorPipelines[pipelineIndex];
        vkCmdWriteTimestamp(job->commandBuffer,
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, job->queryPool,
            1u + queryIndex * 2u);
        vkCmdBindPipeline(job->commandBuffer, VK_PIPELINE_BIND_POINT_COMPUTE,
            g_executorPipelines[pipelineIndex].pipeline);
        vkCmdBindDescriptorSets(job->commandBuffer,
            VK_PIPELINE_BIND_POINT_COMPUTE,
            g_executorPipelines[pipelineIndex].pipelineLayout, 0, 1,
            &job->descriptorSets[pipelineIndex], 0, nullptr);
        if (std::strcmp(pipeline.dispatch, "depth") == 0 ||
            std::strcmp(pipeline.dispatch, "refine") == 0)
            vkCmdDispatch(job->commandBuffer, job->depthGroupsX,
                job->depthGroupsY, 1);
        else if (std::strcmp(pipeline.dispatch, "query") == 0)
            vkCmdDispatch(job->commandBuffer, job->queryGroups, 1, 1);
        else if (std::strcmp(pipeline.dispatch, "readout_query") == 0)
            vkCmdDispatch(job->commandBuffer, job->readoutQueryGroups, 1, 1);
        else if (std::strcmp(pipeline.dispatch, "observation_indirect") == 0)
            vkCmdDispatchIndirect(job->commandBuffer,
                job->buffers[kResourceObservationDispatchArgs].buffer, 0);
        else if (std::strcmp(pipeline.dispatch, "carve_indirect") == 0)
            vkCmdDispatchIndirect(job->commandBuffer,
                job->buffers[kResourceCarveDispatchArgs].buffer, 0);
        else if (std::strcmp(pipeline.dispatch, "readout_indirect") == 0)
            vkCmdDispatchIndirect(job->commandBuffer,
                job->buffers[kResourceFrameDispatchArgs].buffer, 0);
        else
            vkCmdDispatch(job->commandBuffer, 1, 1, 1);
        vkCmdWriteTimestamp(job->commandBuffer,
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, job->queryPool,
            2u + queryIndex * 2u);
    }

    bool RecordJobCommand(ExecutorJob* job)
    {
        VkCommandBufferBeginInfo beginInfo = {};
        beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
        beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        VkResult result = vkBeginCommandBuffer(job->commandBuffer, &beginInfo);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkBeginCommandBuffer", false);
            return false;
        }
        vkCmdResetQueryPool(job->commandBuffer, job->queryPool, 0,
            job->queryCount);
        VkMemoryBarrier acquire = {};
        acquire.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
        acquire.dstAccessMask = VK_ACCESS_SHADER_READ_BIT |
            VK_ACCESS_SHADER_WRITE_BIT |
            VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
        vkCmdPipelineBarrier(job->commandBuffer,
            VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
                VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,
            0, 1, &acquire, 0, nullptr, 0, nullptr);
        vkCmdWriteTimestamp(job->commandBuffer,
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, job->queryPool, 0);
        uint32_t queryIndex = 0;
        for (uint32_t pipelineIndex = job->firstPipeline;
            pipelineIndex < job->lastPipeline; ++pipelineIndex, ++queryIndex)
        {
            RecordDispatch(job, pipelineIndex, queryIndex);
            VkMemoryBarrier barrier = {};
            barrier.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
            barrier.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
            barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT |
                VK_ACCESS_SHADER_WRITE_BIT |
                VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
            vkCmdPipelineBarrier(job->commandBuffer,
                VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
                    VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,
                0, 1, &barrier, 0, nullptr, 0, nullptr);
        }
        VkMemoryBarrier release = {};
        release.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
        release.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
        release.dstAccessMask = VK_ACCESS_MEMORY_READ_BIT |
            VK_ACCESS_MEMORY_WRITE_BIT;
        vkCmdPipelineBarrier(job->commandBuffer,
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
            VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
            0, 1, &release, 0, nullptr, 0, nullptr);
        vkCmdWriteTimestamp(job->commandBuffer,
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, job->queryPool,
            job->queryCount - 1u);
        result = vkEndCommandBuffer(job->commandBuffer);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkEndCommandBuffer", false);
            return false;
        }
        return true;
    }

    void PrepareExecutorJob(ExecutorJob* job)
    {
        if (job == nullptr || !g_executorReady)
            return;
        int expected = kJobCreated;
        if (!job->state.compare_exchange_strong(expected, kJobPreparing,
                std::memory_order_acq_rel))
            return;
        job->prepareStartNs = MonotonicNs();
        std::lock_guard<std::mutex> lock(g_executorMutex);
        bool complete = AccessJobResources(job) && CreateJobUniforms(job) &&
            CreateJobDescriptors(job) && CreateJobCommandObjects(job) &&
            RecordJobCommand(job);
        if (complete)
        {
            job->preparedNs = MonotonicNs();
            job->state.store(kJobPrepared, std::memory_order_release);
            return;
        }
        job->state.store(kJobFailedNeedsGraphicsCompletion,
            std::memory_order_release);
    }

    void SubmitExecutorJob(ExecutorJob* job)
    {
        if (job == nullptr || g_instance.graphicsQueue == VK_NULL_HANDLE)
            return;
        int state = job->state.load(std::memory_order_acquire);
        if (state != kJobPrepared &&
            state != kJobFailedNeedsGraphicsCompletion)
            return;
        VkSubmitInfo graphicsSubmit = {};
        graphicsSubmit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
        if (state == kJobPrepared)
        {
            graphicsSubmit.signalSemaphoreCount = 1;
            graphicsSubmit.pSignalSemaphores = &job->graphicsReady;
        }
        VkResult result = vkQueueSubmit(g_instance.graphicsQueue, 1,
            &graphicsSubmit, job->graphicsFence);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkQueueSubmit(graphicsReady)", false);
            return;
        }
        job->graphicsSubmitted = true;
        if (state != kJobPrepared)
            return;

        VkPipelineStageFlags waitStage =
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
            VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT;
        VkSubmitInfo nativeSubmit = {};
        nativeSubmit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
        nativeSubmit.waitSemaphoreCount = 1;
        nativeSubmit.pWaitSemaphores = &job->graphicsReady;
        nativeSubmit.pWaitDstStageMask = &waitStage;
        nativeSubmit.commandBufferCount = 1;
        nativeSubmit.pCommandBuffers = &job->commandBuffer;
        nativeSubmit.signalSemaphoreCount = 1;
        nativeSubmit.pSignalSemaphores = &job->nativeDone;
        result = vkQueueSubmit(g_scannerQueue, 1, &nativeSubmit,
            job->nativeFence);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkQueueSubmit(scanner queue)", true);
            return;
        }
        job->submittedNs = MonotonicNs();
        job->state.store(kJobSubmitted, std::memory_order_release);
        if (g_log != nullptr)
        {
            char message[384] = {};
            std::snprintf(message, sizeof(message),
                "Merkaba native submit: revision=%u kind=%u family=%u "
                "queue=%u dispatches=%u graphicsWaitOnNative=0",
                job->revision, job->kind, g_injectedQueueFamily,
                g_injectedQueueIndex,
                job->lastPipeline - job->firstPipeline);
            UNITY_LOG(g_log, message);
        }
    }

    void UNITY_INTERFACE_API OnExecutorEvent(int eventId, void* data)
    {
        ExecutorJob* job = static_cast<ExecutorJob*>(data);
        int event = eventId - g_executorEventBase;
        if (event == 0)
            PrepareExecutorJob(job);
        else if (event == 1)
            SubmitExecutorJob(job);
        else if (event == 2 && job != nullptr)
        {
            int expected = kJobNativeComplete;
            if (!job->state.compare_exchange_strong(expected, kJobAcquiring,
                    std::memory_order_acq_rel))
                return;
            VkPipelineStageFlags waitStage = VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
            VkSubmitInfo acquire = {};
            acquire.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
            acquire.waitSemaphoreCount = 1;
            acquire.pWaitSemaphores = &job->nativeDone;
            acquire.pWaitDstStageMask = &waitStage;
            VkResult result = vkQueueSubmit(g_instance.graphicsQueue, 1,
                &acquire, job->acquireFence);
            if (result != VK_SUCCESS)
                FailJob(job, result, "vkQueueSubmit(nativeDone acquire)",
                    true);
            else
                job->acquireSubmittedNs = MonotonicNs();
        }
    }

    bool CollectJobTimings(ExecutorJob* job)
    {
        if (job->timingsReady)
            return true;
        VkResult result = vkGetQueryPoolResults(g_instance.device,
            job->queryPool, 0, job->queryCount,
            sizeof(uint64_t) * job->timestamps.size(),
            job->timestamps.data(), sizeof(uint64_t),
            VK_QUERY_RESULT_64_BIT);
        if (result == VK_NOT_READY)
            return false;
        if (result != VK_SUCCESS)
        {
            LogError("vkGetQueryPoolResults(native job)", result);
            return false;
        }
        job->timingsReady = true;
        return true;
    }

    void ShutdownExecutor()
    {
        g_executorReady = false;
        if (g_instance.device == VK_NULL_HANDLE)
            return;
        // Lifecycle shutdown is the only blocking point. Normal XR frames never
        // wait for the scanner queue.
        vkDeviceWaitIdle(g_instance.device);
        std::lock_guard<std::mutex> lock(g_executorMutex);
        for (ExecutorJob* job : g_executorJobs)
        {
            DestroyJobObjects(job);
            delete job;
        }
        g_executorJobs.clear();
        for (ExecutorPipeline& pipeline : g_executorPipelines)
            DestroyExecutorPipeline(pipeline);
        if (g_bilinearSampler != VK_NULL_HANDLE)
            vkDestroySampler(g_instance.device, g_bilinearSampler, nullptr);
        if (g_pointSampler != VK_NULL_HANDLE)
            vkDestroySampler(g_instance.device, g_pointSampler, nullptr);
        g_bilinearSampler = g_pointSampler = VK_NULL_HANDLE;
        if (g_executorCommandPool != VK_NULL_HANDLE)
            vkDestroyCommandPool(g_instance.device, g_executorCommandPool,
                nullptr);
        g_executorCommandPool = VK_NULL_HANDLE;
        g_scannerQueue = VK_NULL_HANDLE;
    }

    bool InitializeExecutor()
    {
        if (!g_queueInjected ||
            g_injectedQueueFamily != g_instance.queueFamilyIndex ||
            g_injectedQueueIndex != 1u)
        {
            Log("Merkaba native scanner unavailable: safe same-family queue "
                "1 was not injected.");
            return false;
        }
        vkGetDeviceQueue(g_instance.device, g_injectedQueueFamily,
            g_injectedQueueIndex, &g_scannerQueue);
        if (g_scannerQueue == VK_NULL_HANDLE ||
            g_scannerQueue == g_instance.graphicsQueue)
        {
            Log("Merkaba native scanner unavailable: queue 1 is null or "
                "aliases Unity queue 0.");
            g_scannerQueue = VK_NULL_HANDLE;
            return false;
        }
        VkCommandPoolCreateInfo commandPoolInfo = {};
        commandPoolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
        commandPoolInfo.flags =
            VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        commandPoolInfo.queueFamilyIndex = g_injectedQueueFamily;
        VkResult result = vkCreateCommandPool(g_instance.device,
            &commandPoolInfo, nullptr, &g_executorCommandPool);
        if (result != VK_SUCCESS)
        {
            LogError("vkCreateCommandPool", result);
            return false;
        }
        if (!CreateExecutorPipelines())
            return false;
        if (!g_executorEventsReserved)
        {
            g_executorEventBase = g_graphics->ReserveEventIDRange(3);
            g_executorEventsReserved = true;
        }
        UnityVulkanPluginEventConfig prepareConfig = {};
        prepareConfig.renderPassPrecondition =
            kUnityVulkanRenderPass_EnsureOutside;
        prepareConfig.graphicsQueueAccess =
            kUnityVulkanGraphicsQueueAccess_DontCare;
        prepareConfig.flags =
            kUnityVulkanEventConfigFlag_SyncWorkerThreads |
            kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState;
        g_vulkan->ConfigureEvent(g_executorEventBase, &prepareConfig);
        UnityVulkanPluginEventConfig submitConfig = {};
        submitConfig.renderPassPrecondition =
            kUnityVulkanRenderPass_EnsureOutside;
        submitConfig.graphicsQueueAccess =
            kUnityVulkanGraphicsQueueAccess_Allow;
        submitConfig.flags =
            kUnityVulkanEventConfigFlag_FlushCommandBuffers |
            kUnityVulkanEventConfigFlag_SyncWorkerThreads;
        g_vulkan->ConfigureEvent(g_executorEventBase + 1, &submitConfig);
        g_vulkan->ConfigureEvent(g_executorEventBase + 2, &submitConfig);
        g_executorReady = true;
        if (g_log != nullptr)
        {
            char message[384] = {};
            std::snprintf(message, sizeof(message),
                "Merkaba native scanner ready: family=%u queue=1 "
                "pipelines=%u queue0WaitsNative=0",
                g_injectedQueueFamily, kMerkabaExecutorPipelineCount);
            UNITY_LOG(g_log, message);
        }
        return true;
    }

    bool TryRecordingState(UnityVulkanRecordingState* recording)
    {
        return g_vulkan != nullptr && g_queryPool != VK_NULL_HANDLE &&
            g_vulkan->CommandRecordingState(recording,
                kUnityVulkanGraphicsQueueAccess_DontCare);
    }

    uint32_t OwnerBase()
    {
        return g_activeOwner * kOwnerStride;
    }

    void BeginEntry(UnityVulkanRecordingState* recording,
        VkPipelineStageFlagBits stage, EntryKind kind)
    {
        if (g_state.load(std::memory_order_acquire) != kRecording)
            return;
        if (g_openEntry != kNoEntry || g_entryCount >= kMaximumEntries)
        {
            g_overflow = 1;
            return;
        }
        g_openEntry = g_entryCount;
        g_openKind = kind;
        vkCmdWriteTimestamp(recording->commandBuffer, stage, g_queryPool,
            OwnerBase() + 2 + g_openEntry * 2);
    }

    void EndEntry(UnityVulkanRecordingState* recording,
        VkPipelineStageFlagBits stage, EntryKind kind)
    {
        if (g_state.load(std::memory_order_acquire) != kRecording ||
            g_openEntry == kNoEntry || g_openKind != kind)
        {
            g_overflow = 1;
            return;
        }
        vkCmdWriteTimestamp(recording->commandBuffer, stage, g_queryPool,
            OwnerBase() + 3 + g_openEntry * 2);
        ++g_entryCount;
        g_openEntry = kNoEntry;
        g_openKind = kEntryNone;
    }

    void UNITY_INTERFACE_API OnRenderEvent(int eventId)
    {
        UnityVulkanRecordingState recording = {};
        if (!TryRecordingState(&recording))
            return;
        int event = eventId - g_eventBase;
        if (event == kSubmissionBegin)
        {
            if (g_state.load(std::memory_order_acquire) != kArmed)
                return;
            uint32_t ownerBase = OwnerBase();
            vkCmdResetQueryPool(recording.commandBuffer, g_queryPool,
                ownerBase, kOwnerStride);
            vkCmdWriteTimestamp(recording.commandBuffer,
                VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, g_queryPool,
                ownerBase);
            g_entryCount = 0;
            g_recordedEntries = 0;
            g_openEntry = kNoEntry;
            g_openKind = kEntryNone;
            g_overflow = 0;
            g_state.store(kRecording, std::memory_order_release);
            return;
        }
        if (event == kDispatchBegin)
        {
            BeginEntry(&recording, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                kEntryCompute);
            return;
        }
        if (event == kDispatchEnd)
        {
            EndEntry(&recording, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                kEntryCompute);
            return;
        }
        if (event == kCopyBegin)
        {
            BeginEntry(&recording, VK_PIPELINE_STAGE_TRANSFER_BIT,
                kEntryCopy);
            return;
        }
        if (event == kCopyEnd)
        {
            EndEntry(&recording, VK_PIPELINE_STAGE_TRANSFER_BIT,
                kEntryCopy);
            return;
        }
        if (event == kDrawBegin)
        {
            BeginEntry(&recording, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                kEntryDraw);
            return;
        }
        if (event == kDrawEnd)
        {
            EndEntry(&recording, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
                kEntryDraw);
            return;
        }
        if (event != kSubmissionEnd ||
            g_state.load(std::memory_order_acquire) != kRecording)
            return;
        if (g_openEntry != kNoEntry)
            g_overflow = 1;
        vkCmdWriteTimestamp(recording.commandBuffer,
            VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, g_queryPool,
            OwnerBase() + 1);
        g_recordedEntries = g_entryCount;
        g_openEntry = kNoEntry;
        g_openKind = kEntryNone;
        g_state.store(kRecorded, std::memory_order_release);
    }

    void ShutdownVulkan()
    {
        g_state.store(kUnavailable, std::memory_order_release);
        ShutdownExecutor();
        if (g_queryPool != VK_NULL_HANDLE && g_instance.device != VK_NULL_HANDLE)
            vkDestroyQueryPool(g_instance.device, g_queryPool, nullptr);
        g_queryPool = VK_NULL_HANDLE;
        g_vulkan = nullptr;
        g_instance = {};
    }

    void InitializeVulkan()
    {
        if (g_graphics == nullptr ||
            g_graphics->GetRenderer() != kUnityGfxRendererVulkan ||
            g_instance.device != VK_NULL_HANDLE)
            return;
        if (g_vulkan == nullptr)
            g_vulkan = g_interfaces->Get<IUnityGraphicsVulkanV2>();
        if (g_vulkan == nullptr)
            return;
        g_instance = g_vulkan->Instance();
        if (g_instance.device == VK_NULL_HANDLE ||
            g_instance.physicalDevice == VK_NULL_HANDLE)
            return;

        VkPhysicalDeviceProperties properties = {};
        vkGetPhysicalDeviceProperties(g_instance.physicalDevice, &properties);
        g_deviceProperties = properties;
        vkGetPhysicalDeviceMemoryProperties(g_instance.physicalDevice,
            &g_memoryProperties);
        uint32_t queueCount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(g_instance.physicalDevice,
            &queueCount, nullptr);
        if (g_instance.queueFamilyIndex >= queueCount ||
            g_instance.queueFamilyIndex >= 32)
            return;
        VkQueueFamilyProperties queues[32] = {};
        if (queueCount > 32)
            queueCount = 32;
        vkGetPhysicalDeviceQueueFamilyProperties(g_instance.physicalDevice,
            &queueCount, queues);
        g_timestampValidBits =
            queues[g_instance.queueFamilyIndex].timestampValidBits;
        g_timestampPeriod = properties.limits.timestampPeriod;
        InitializeExecutor();
        if (!properties.limits.timestampComputeAndGraphics ||
            g_timestampValidBits == 0 || g_timestampPeriod <= 0.0)
            return;

        VkQueryPoolCreateInfo createInfo = {};
        createInfo.sType = VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO;
        createInfo.queryType = VK_QUERY_TYPE_TIMESTAMP;
        createInfo.queryCount = kMaximumQueries;
        if (vkCreateQueryPool(g_instance.device, &createInfo, nullptr,
                &g_queryPool) != VK_SUCCESS)
            return;

        if (!g_eventsReserved)
        {
            g_eventBase = g_graphics->ReserveEventIDRange(kEventCount);
            g_eventsReserved = true;
        }
        UnityVulkanPluginEventConfig eventConfig = {};
        eventConfig.renderPassPrecondition =
            kUnityVulkanRenderPass_EnsureOutside;
        eventConfig.graphicsQueueAccess =
            kUnityVulkanGraphicsQueueAccess_DontCare;
        eventConfig.flags = kUnityVulkanEventConfigFlag_SyncWorkerThreads;
        for (int event = 0; event < kEventCount; ++event)
            g_vulkan->ConfigureEvent(g_eventBase + event, &eventConfig);
        g_state.store(kIdle, std::memory_order_release);
    }

    void UNITY_INTERFACE_API OnGraphicsDeviceEvent(
        UnityGfxDeviceEventType eventType)
    {
        if (eventType == kUnityGfxDeviceEventInitialize ||
            eventType == kUnityGfxDeviceEventAfterReset)
            InitializeVulkan();
        else if (eventType == kUnityGfxDeviceEventShutdown ||
            eventType == kUnityGfxDeviceEventBeforeReset)
            ShutdownVulkan();
    }
}

extern "C"
{
    void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(
        IUnityInterfaces* unityInterfaces)
    {
        g_interfaces = unityInterfaces;
        g_graphics = unityInterfaces->Get<IUnityGraphics>();
        g_vulkan = unityInterfaces->Get<IUnityGraphicsVulkanV2>();
        g_log = unityInterfaces->Get<IUnityLog>();
        if (g_graphics == nullptr)
            return;
        if (g_vulkan != nullptr)
        {
            g_interceptInstalled = g_vulkan->AddInterceptInitialization(
                InterceptInitialization, nullptr, 10000);
            Log(g_interceptInstalled
                ? "Merkaba native scanner vkCreateDevice interception "
                  "registered."
                : "Merkaba native scanner vkCreateDevice interception "
                  "registration failed; plugin was loaded too late.");
        }
        g_graphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
        OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
    }

    void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
    {
        if (g_graphics != nullptr)
            g_graphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
        if (g_vulkan != nullptr && g_interceptInstalled)
            g_vulkan->RemoveInterceptInitialization(InterceptInitialization);
        ShutdownVulkan();
        g_interceptInstalled = false;
        g_nextGetInstanceProcAddr = nullptr;
        g_log = nullptr;
        g_graphics = nullptr;
        g_interfaces = nullptr;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaExecutor_IsAvailable()
    {
        return g_executorReady && g_scannerQueue != VK_NULL_HANDLE ? 1 : 0;
    }

    uint32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
        MerkabaExecutor_GetAbiVersion()
    {
        return kExecutorAbiVersion;
    }

    void* UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaExecutor_CreateJob(
        const MerkabaExecutorJobDescriptor* descriptor)
    {
        if (!g_executorReady || descriptor == nullptr ||
            descriptor->structSize != sizeof(MerkabaExecutorJobDescriptor) ||
            descriptor->abiVersion != kExecutorAbiVersion ||
            descriptor->revision == 0 ||
            descriptor->kind > kJobFineErase ||
            descriptor->resourceCount != kResourceCount ||
            descriptor->resources == nullptr ||
            descriptor->uniformValueCount == 0 ||
            descriptor->uniformValues == nullptr ||
            descriptor->uniformData == nullptr ||
            descriptor->uniformDataSize == 0 ||
            descriptor->depthGroupsX > 65535 ||
            descriptor->depthGroupsY > 65535 ||
            descriptor->queryGroups > 65535 ||
            descriptor->readoutQueryGroups > 65535)
            return nullptr;
        if (descriptor->kind == kJobObservationNew &&
            (descriptor->depthGroupsX == 0 ||
             descriptor->depthGroupsY == 0 ||
             descriptor->queryGroups == 0))
            return nullptr;
        if (descriptor->kind == kJobObservationRetry &&
            descriptor->queryGroups == 0)
            return nullptr;
        if (descriptor->kind == kJobReadout &&
            descriptor->readoutQueryGroups == 0)
            return nullptr;
        if (descriptor->kind == kJobFineErase &&
            descriptor->queryGroups == 0)
            return nullptr;

        uint32_t firstPipeline = 0;
        uint32_t lastPipeline = 0;
        if (!PipelineRangeForKind(descriptor->kind, &firstPipeline,
                &lastPipeline) || firstPipeline >= lastPipeline)
            return nullptr;

        ExecutorJob* job = new ExecutorJob();
        job->createdNs = MonotonicNs();
        job->kind = descriptor->kind;
        job->revision = descriptor->revision;
        job->firstPipeline = firstPipeline;
        job->lastPipeline = lastPipeline;
        std::copy(descriptor->resources,
            descriptor->resources + kResourceCount,
            job->nativeResources.begin());
        job->uniformValues.assign(descriptor->uniformValues,
            descriptor->uniformValues + descriptor->uniformValueCount);
        job->uniformData.assign(descriptor->uniformData,
            descriptor->uniformData + descriptor->uniformDataSize);
        job->depthGroupsX = descriptor->depthGroupsX;
        job->depthGroupsY = descriptor->depthGroupsY;
        job->queryGroups = descriptor->queryGroups;
        job->readoutQueryGroups = descriptor->readoutQueryGroups;
        std::lock_guard<std::mutex> lock(g_executorMutex);
        g_executorJobs.push_back(job);
        return job;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaExecutor_CancelJob(
        void* handle)
    {
        ExecutorJob* job = static_cast<ExecutorJob*>(handle);
        if (job == nullptr ||
            job->state.load(std::memory_order_acquire) != kJobCreated)
            return 0;
        std::lock_guard<std::mutex> lock(g_executorMutex);
        auto found = std::find(g_executorJobs.begin(), g_executorJobs.end(),
            job);
        if (found == g_executorJobs.end())
            return 0;
        g_executorJobs.erase(found);
        delete job;
        return 1;
    }

    UnityRenderingEventAndData UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
        MerkabaExecutor_GetRenderEventFunc()
    {
        return OnExecutorEvent;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaExecutor_GetEventId(
        int offset)
    {
        return g_executorReady && offset >= 0 && offset < 3
            ? g_executorEventBase + offset : 0;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaExecutor_PollJob(
        void* handle, int* error)
    {
        ExecutorJob* job = static_cast<ExecutorJob*>(handle);
        if (job == nullptr || error == nullptr)
            return -1;
        int state = job->state.load(std::memory_order_acquire);
        if (state == kJobSubmitted)
        {
            VkResult result = vkGetFenceStatus(g_instance.device,
                job->nativeFence);
            if (result == VK_SUCCESS)
            {
                CollectJobTimings(job);
                job->nativeCompleteNs = MonotonicNs();
                job->state.store(kJobNativeComplete,
                    std::memory_order_release);
                state = kJobNativeComplete;
            }
            else if (result != VK_NOT_READY)
            {
                job->error = result;
                job->state.store(kJobFailedSafe, std::memory_order_release);
                state = kJobFailedSafe;
            }
        }
        else if (state == kJobAcquiring)
        {
            VkResult result = vkGetFenceStatus(g_instance.device,
                job->acquireFence);
            if (result == VK_SUCCESS)
            {
                job->completeNs = MonotonicNs();
                job->state.store(kJobComplete, std::memory_order_release);
                state = kJobComplete;
            }
            else if (result != VK_NOT_READY)
            {
                job->error = result;
                job->state.store(kJobFailedSafe, std::memory_order_release);
                state = kJobFailedSafe;
            }
        }
        else if (state == kJobFailedNeedsGraphicsCompletion &&
            job->graphicsSubmitted)
        {
            VkResult result = vkGetFenceStatus(g_instance.device,
                job->graphicsFence);
            if (result == VK_SUCCESS || result != VK_NOT_READY)
            {
                if (result != VK_SUCCESS)
                    job->error = result;
                job->state.store(kJobFailedSafe, std::memory_order_release);
                state = kJobFailedSafe;
            }
        }
        *error = static_cast<int>(job->error);
        if ((state == kJobComplete || state == kJobFailedSafe) &&
            !job->terminalLogged && g_log != nullptr)
        {
            job->terminalLogged = true;
            const double queueMs = job->submittedNs != 0u &&
                job->nativeCompleteNs >= job->submittedNs
                ? static_cast<double>(job->nativeCompleteNs -
                    job->submittedNs) / 1000000.0 : -1.0;
            const double acquireMs = job->acquireSubmittedNs != 0u &&
                job->completeNs >= job->acquireSubmittedNs
                ? static_cast<double>(job->completeNs -
                    job->acquireSubmittedNs) / 1000000.0 : -1.0;
            const double lifetimeMs = job->createdNs != 0u &&
                job->completeNs >= job->createdNs
                ? static_cast<double>(job->completeNs - job->createdNs) /
                    1000000.0 : -1.0;
            const double prepareWaitMs = job->prepareStartNs >= job->createdNs
                ? static_cast<double>(job->prepareStartNs - job->createdNs) /
                    1000000.0 : -1.0;
            const double prepareCpuMs = job->preparedNs >= job->prepareStartNs
                ? static_cast<double>(job->preparedNs - job->prepareStartNs) /
                    1000000.0 : -1.0;
            char message[512] = {};
            std::snprintf(message, sizeof(message),
                "Merkaba native terminal: revision=%u kind=%u state=%d "
                "prepareWaitMs=%.3f prepareCpuMs=%.3f queueFenceMs=%.3f "
                "acquireFenceMs=%.3f lifetimeMs=%.3f error=%d",
                job->revision, job->kind, state, prepareWaitMs, prepareCpuMs,
                queueMs, acquireMs, lifetimeMs,
                static_cast<int>(job->error));
            UNITY_LOG(g_log, message);
        }
        if (state == kJobComplete)
            return 1;
        if (state == kJobNativeComplete)
            return 2;
        if (state == kJobFailedSafe)
            return -1;
        return 0;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaExecutor_ReadTimings(
        void* handle, uint64_t* timestamps, int timestampCapacity,
        double* timestampPeriod, int* validBits)
    {
        ExecutorJob* job = static_cast<ExecutorJob*>(handle);
        if (job == nullptr || timestamps == nullptr ||
            timestampPeriod == nullptr || validBits == nullptr ||
            job->state.load(std::memory_order_acquire) != kJobComplete ||
            timestampCapacity < static_cast<int>(job->queryCount) ||
            !CollectJobTimings(job))
            return 0;
        std::copy(job->timestamps.begin(),
            job->timestamps.begin() + job->queryCount, timestamps);
        *timestampPeriod = g_deviceProperties.limits.timestampPeriod;
        *validBits = static_cast<int>(g_timestampValidBits);
        return static_cast<int>(job->queryCount);
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaExecutor_DestroyJob(
        void* handle)
    {
        ExecutorJob* job = static_cast<ExecutorJob*>(handle);
        if (job == nullptr)
            return 0;
        int state = job->state.load(std::memory_order_acquire);
        if (state != kJobComplete && state != kJobFailedSafe)
            return 0;
        std::lock_guard<std::mutex> lock(g_executorMutex);
        auto found = std::find(g_executorJobs.begin(), g_executorJobs.end(),
            job);
        if (found == g_executorJobs.end())
            return 0;
        g_executorJobs.erase(found);
        DestroyJobObjects(job);
        delete job;
        return 1;
    }

    UnityRenderingEvent UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
        MerkabaTimestamp_GetRenderEventFunc()
    {
        return OnRenderEvent;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaTimestamp_GetEventId(
        int offset)
    {
        return offset >= 0 && offset < kEventCount ? g_eventBase + offset : 0;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaTimestamp_IsAvailable()
    {
        return g_state.load(std::memory_order_acquire) != kUnavailable &&
            g_queryPool != VK_NULL_HANDLE;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaTimestamp_Arm(
        int owner, uint64_t revision)
    {
        if (owner < 0 || owner >= static_cast<int>(kOwnerCount) ||
            revision == 0)
            return 0;
        int expected = kIdle;
        if (!g_state.compare_exchange_strong(expected, kPreparing,
                std::memory_order_acq_rel))
            return 0;
        g_activeOwner = static_cast<uint32_t>(owner);
        g_revision = revision;
        g_state.store(kArmed, std::memory_order_release);
        return 1;
    }

    void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaTimestamp_Cancel()
    {
        int expected = kArmed;
        g_state.compare_exchange_strong(expected, kIdle,
            std::memory_order_acq_rel);
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaTimestamp_Read(
        int requestedOwner, uint64_t* timestamps, int timestampCapacity,
        int* capturedOwner, int* entryCount,
        double* timestampPeriod, int* validBits, uint64_t* revision,
        int* overflow)
    {
        if (timestamps == nullptr || capturedOwner == nullptr ||
            entryCount == nullptr ||
            timestampPeriod == nullptr || validBits == nullptr ||
            revision == nullptr || overflow == nullptr)
            return -1;
        int state = g_state.load(std::memory_order_acquire);
        if (state == kArmed || state == kRecording || state == kPreparing)
            return 0;
        if (state != kRecorded)
            return -1;
        *capturedOwner = static_cast<int>(g_activeOwner);
        *entryCount = static_cast<int>(g_recordedEntries);
        *timestampPeriod = g_timestampPeriod;
        *validBits = static_cast<int>(g_timestampValidBits);
        *revision = g_revision;
        *overflow = static_cast<int>(g_overflow);
        if (requestedOwner != static_cast<int>(g_activeOwner))
        {
            g_state.store(kIdle, std::memory_order_release);
            return -1;
        }
        uint32_t queryCount = 2 + g_recordedEntries * 2;
        if (timestampCapacity < static_cast<int>(queryCount))
            return -1;
        if (queryCount != 0)
        {
            VkResult result = vkGetQueryPoolResults(g_instance.device,
                g_queryPool, OwnerBase(), queryCount,
                sizeof(uint64_t) * queryCount * 2, g_queryScratch,
                sizeof(uint64_t) * 2,
                VK_QUERY_RESULT_64_BIT |
                    VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);
            if (result == VK_NOT_READY)
                return 0;
            if (result != VK_SUCCESS)
                return -1;
            for (uint32_t query = 0; query < queryCount; ++query)
            {
                if (g_queryScratch[query * 2 + 1] == 0)
                    return 0;
                timestamps[query] = g_queryScratch[query * 2];
            }
        }
        g_state.store(kIdle, std::memory_order_release);
        return 1;
    }
}
