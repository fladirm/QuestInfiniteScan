#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include "IUnityLog.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cstring>
#include <cstdio>
#include <cstdint>
#include <mutex>
#include <string>
#include <vector>

namespace
{
    enum SigmaExecutorResource : int32_t
    {
        kResourceExactGate = 0,
        kResourceDepthCalibration,
        kResourceRgbCalibration,
        kResourcePoseResult,
        kResourceNativeFrame,
        kResourceObservation,
        kResourceCloseScratch,
        kResourceStates,
        kResourceStateDelta,
        kResourceGaugeDelta,
        kResourceLocalityCertificates,
        kResourceRevisions,
        kResourceCounters,
        kResourceCompletionJournal,
        kResourceCarrierState,
        kResourceCarrierRepresentation,
        kResourceCarrierMetadata,
        kResourceCarrierPublicationRoot,
        kResourceCarrierDirtyFlags,
        kResourceCarrierReadoutDirtyFlags,
        kResourceRelationInputs,
        kResourceRelationPlans,
        kResourceRelationNearIntervals,
        kResourceRelationResults,
        kResourceRelationFactors,
        kResourceRelationHashes,
        kResourceRelationNorms,
        kResourceBranchHeaders,
        kResourceBranchSupports,
        kResourceBranchPredictions,
        kResourceRawDepth,
        kResourceMetricDepth,
        kResourceDepthFlags,
        kResourceDepthRayCenterLeft,
        kResourceDepthRayCenterRight,
        kResourceDepthRayDifferentialXLeft,
        kResourceDepthRayDifferentialXRight,
        kResourceDepthRayDifferentialYLeft,
        kResourceDepthRayDifferentialYRight,
        kResourceDepthSlopeBoundsLeft,
        kResourceDepthSlopeBoundsRight,
        kResourceRgbLeft,
        kResourceRgbRight,
        kResourcePredCarrierPage,
        kResourcePredCarrierUvNormal,
        kResourcePredStateKey,
        kResourceCount,
    };

    enum SigmaEmbeddedDescriptorKind : uint32_t
    {
        kEmbeddedStorageBuffer = 0,
        kEmbeddedSampledImage = 1,
        kEmbeddedUniformBuffer = 2,
    };

    struct SigmaEmbeddedDescriptor
    {
        uint32_t binding;
        uint32_t kind;
        int32_t resource;
    };

    struct SigmaEmbeddedPipeline
    {
        const char* label;
        const char* entryPoint;
        const uint32_t* words;
        uint32_t wordCount;
        const SigmaEmbeddedDescriptor* descriptors;
        uint32_t descriptorCount;
        uint32_t globalSize;
    };

#include "SigmaNativeExecutorShaders.inc"

    static_assert(kSigmaExecutorResourceCount == kResourceCount,
        "C#/native NativeCloseCommit resource ABI mismatch");

    constexpr uint32_t kExecutorAbiVersion = 3;
    constexpr uint32_t kExecutorDispatchCount = 16;
    constexpr uint32_t kExecutorSliceCount = 7;
    constexpr std::array<uint32_t, kExecutorSliceCount + 1>
        kExecutorSliceBounds = {0, 1, 2, 5, 9, 11, 12, 16};
    static_assert(kExecutorSliceBounds[0] == 0 &&
        kExecutorSliceBounds[1] == 1 &&
        kExecutorSliceBounds[2] == 2 &&
        kExecutorSliceBounds[3] == 5 &&
        kExecutorSliceBounds[4] == 9 &&
        kExecutorSliceBounds[5] == 11 &&
        kExecutorSliceBounds[6] == 12 &&
        kExecutorSliceBounds[7] == kExecutorDispatchCount,
        "N4.2R slice schedule must cover each fixed dispatch exactly once");
    constexpr uint32_t kExecutorQueryCount =
        kExecutorDispatchCount * 2 + 2;
    constexpr uint32_t kCompletionWordCount = 80;
    constexpr VkDeviceSize kCompletionRecordBytes =
        kCompletionWordCount * sizeof(uint32_t) * 2;

    struct SigmaExecutorJobDescriptor
    {
        uint32_t structSize;
        uint32_t abiVersion;
        uint32_t revision;
        uint32_t resourceCount;
        void* const* resources;
        const uint8_t* frameConstants;
        const uint8_t* contractConstants;
        const uint8_t* queryBoundaryConstants;
        const uint8_t* queryGlobalConstants;
        uint32_t frameConstantsSize;
        uint32_t contractConstantsSize;
        uint32_t queryBoundaryConstantsSize;
        uint32_t queryGlobalConstantsSize;
        uint32_t observationGroups;
        uint32_t footprintGroupsX;
        uint32_t footprintGroupsY;
        uint32_t tileGroups;
        uint32_t completionRecordIndex;
    };

    enum ExecutorJobState : int
    {
        kJobCreated = 0,
        kJobPreparing,
        kJobPrepared,
        kJobSubmitted,
        kJobSliceReady,
        kJobSliceSubmitting,
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
        uint32_t revision = 0;
        std::array<void*, kResourceCount> nativeResources = {};
        std::array<UnityVulkanBuffer, kResourceCount> buffers = {};
        std::array<UnityVulkanImage, kResourceCount> images = {};
        std::array<VkImageView, kResourceCount> imageViews = {};
        std::vector<uint8_t> frameConstants;
        std::vector<uint8_t> contractConstants;
        std::vector<uint8_t> queryBoundaryConstants;
        std::vector<uint8_t> queryGlobalConstants;
        uint32_t observationGroups = 0;
        uint32_t footprintGroupsX = 0;
        uint32_t footprintGroupsY = 0;
        uint32_t tileGroups = 0;
        uint32_t completionRecordIndex = 0;
        VkBuffer uniformBuffer = VK_NULL_HANDLE;
        VkDeviceMemory uniformMemory = VK_NULL_HANDLE;
        VkMemoryPropertyFlags uniformMemoryFlags = 0;
        VkBuffer completionReadbackBuffer = VK_NULL_HANDLE;
        VkDeviceMemory completionReadbackMemory = VK_NULL_HANDLE;
        VkMemoryPropertyFlags completionReadbackMemoryFlags = 0;
        VkDeviceSize frameUniformOffset = 0;
        VkDeviceSize contractUniformOffset = 0;
        VkDeviceSize queryBoundaryUniformOffset = 0;
        VkDeviceSize queryGlobalUniformOffset = 0;
        VkDescriptorPool descriptorPool = VK_NULL_HANDLE;
        std::array<VkDescriptorSet, kExecutorDispatchCount> descriptorSets = {};
        std::array<VkCommandBuffer, kExecutorSliceCount> commandBuffers = {};
        uint32_t submittedSlice = UINT32_MAX;
        VkSemaphore graphicsReady = VK_NULL_HANDLE;
        VkSemaphore nativeDone = VK_NULL_HANDLE;
        VkFence graphicsFence = VK_NULL_HANDLE;
        VkFence nativeFence = VK_NULL_HANDLE;
        VkFence acquireFence = VK_NULL_HANDLE;
        VkQueryPool queryPool = VK_NULL_HANDLE;
        std::array<uint64_t, kExecutorQueryCount> timestamps = {};
        VkResult error = VK_SUCCESS;
        bool graphicsSubmitted = false;
        bool timingsReady = false;
        bool completionReady = false;
    };

    constexpr uint32_t kMaximumDispatches = 4096;
    constexpr uint32_t kMaximumQueries = kMaximumDispatches * 2;
    constexpr uint32_t kNoDispatch = UINT32_MAX;

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
        kSubmissionEnd,
        kEventCount,
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
    VkQueue g_sigmaQueue = VK_NULL_HANDLE;
    VkCommandPool g_executorCommandPool = VK_NULL_HANDLE;
    VkPhysicalDeviceMemoryProperties g_memoryProperties = {};
    VkPhysicalDeviceProperties g_deviceProperties = {};
    std::array<ExecutorPipeline, kExecutorDispatchCount> g_executorPipelines = {};
    std::mutex g_executorMutex;
    std::mutex g_sigmaQueueMutex;
    std::vector<ExecutorJob*> g_executorJobs;
    int g_executorEventBase = 0;
    bool g_executorEventsReserved = false;
    bool g_executorReady = false;
    VkQueryPool g_queryPool = VK_NULL_HANDLE;
    std::atomic<int> g_state{kUnavailable};
    uint32_t g_dispatchCount = 0;
    uint32_t g_recordedDispatches = 0;
    uint32_t g_openDispatch = kNoDispatch;
    uint32_t g_overflow = 0;
    uint64_t g_revision = 0;
    double g_timestampPeriod = 0.0;
    uint32_t g_timestampValidBits = 0;
    uint64_t g_queryScratch[kMaximumQueries * 2] = {};
    int g_eventBase = 0;
    bool g_eventsReserved = false;

    void Log(const char* message)
    {
        if (g_log != nullptr && message != nullptr)
            UNITY_LOG(g_log, message);
    }

    void LogExecutorError(const char* operation, VkResult result)
    {
        if (g_log == nullptr)
            return;
        char message[384] = {};
        std::snprintf(message, sizeof(message),
            "Sigma N4.2R native executor: %s failed VkResult=%d",
            operation, static_cast<int>(result));
        UNITY_LOG_ERROR(g_log, message);
    }

    VkResult QueueSubmitDirect(VkQueue queue, uint32_t submitCount,
        const VkSubmitInfo* submits, VkFence fence)
    {
        return vkQueueSubmit(queue, submitCount, submits, fence);
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

        uint32_t physicalFamilyCount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice,
            &physicalFamilyCount, nullptr);
        std::vector<VkQueueFamilyProperties> physicalFamilies(
            physicalFamilyCount);
        vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice,
            &physicalFamilyCount, physicalFamilies.data());

        std::vector<VkDeviceQueueCreateInfo> queueInfos(
            createInfo->pQueueCreateInfos,
            createInfo->pQueueCreateInfos + createInfo->queueCreateInfoCount);
        std::vector<std::vector<float>> priorities(queueInfos.size());
        uint32_t selectedInfo = UINT32_MAX;
        for (uint32_t index = 0; index < queueInfos.size(); ++index)
        {
            VkDeviceQueueCreateInfo& info = queueInfos[index];
            priorities[index].assign(info.pQueuePriorities,
                info.pQueuePriorities + info.queueCount);
            info.pQueuePriorities = priorities[index].data();
            if (selectedInfo != UINT32_MAX ||
                (info.flags & VK_DEVICE_QUEUE_CREATE_PROTECTED_BIT) != 0 ||
                info.queueFamilyIndex >= physicalFamilies.size())
                continue;
            const VkQueueFamilyProperties& family =
                physicalFamilies[info.queueFamilyIndex];
            const VkQueueFlags required =
                VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT;
            if ((family.queueFlags & required) != required ||
                info.queueCount >= family.queueCount)
                continue;
            selectedInfo = index;
        }

        VkDeviceCreateInfo modified = *createInfo;
        if (selectedInfo != UINT32_MAX)
        {
            VkDeviceQueueCreateInfo& info = queueInfos[selectedInfo];
            g_injectedQueueFamily = info.queueFamilyIndex;
            g_injectedQueueIndex = info.queueCount;
            priorities[selectedInfo].push_back(0.1f);
            info.queueCount++;
            info.pQueuePriorities = priorities[selectedInfo].data();
            modified.pQueueCreateInfos = queueInfos.data();
            modified.queueCreateInfoCount =
                static_cast<uint32_t>(queueInfos.size());
        }

        VkResult result = nextCreateDevice(physicalDevice,
            selectedInfo == UINT32_MAX ? createInfo : &modified,
            allocator, device);
        if (result == VK_SUCCESS && selectedInfo != UINT32_MAX)
            g_queueInjected = true;
        else if (result != VK_SUCCESS && selectedInfo != UINT32_MAX)
        {
            LogExecutorError("injected vkCreateDevice", result);
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
                "Sigma N4.2R vkCreateDevice intercept: requestedFamilies=%u "
                "injected=%u family=%u queueIndex=%u result=%d",
                createInfo->queueCreateInfoCount,
                g_queueInjected ? 1u : 0u, g_injectedQueueFamily,
                g_injectedQueueIndex, static_cast<int>(result));
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
        *actualFlags = g_memoryProperties.memoryTypes[fallback].propertyFlags;
        return true;
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
        for (uint32_t index = 0; index < kExecutorDispatchCount; ++index)
        {
            const SigmaEmbeddedPipeline& embedded =
                kSigmaExecutorPipelines[index];
            std::vector<VkDescriptorSetLayoutBinding> bindings;
            bindings.reserve(embedded.descriptorCount);
            for (uint32_t descriptorIndex = 0;
                descriptorIndex < embedded.descriptorCount; ++descriptorIndex)
            {
                const SigmaEmbeddedDescriptor& descriptor =
                    embedded.descriptors[descriptorIndex];
                VkDescriptorSetLayoutBinding binding = {};
                binding.binding = descriptor.binding;
                binding.descriptorCount = 1;
                binding.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
                binding.descriptorType = descriptor.kind ==
                        kEmbeddedSampledImage
                    ? VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE
                    : descriptor.kind == kEmbeddedUniformBuffer
                        ? VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER
                        : VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
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
                LogExecutorError("vkCreateDescriptorSetLayout", result);
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
                LogExecutorError("vkCreatePipelineLayout", result);
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
                LogExecutorError("vkCreateShaderModule", result);
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
                LogExecutorError("vkCreateComputePipelines", result);
                return false;
            }
        }
        return true;
    }

    bool IsTextureResource(uint32_t resource)
    {
        return resource >= static_cast<uint32_t>(kResourceRawDepth) &&
            resource < static_cast<uint32_t>(kResourceCount);
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
        LogExecutorError(operation, result);
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
        if (job->completionReadbackBuffer != VK_NULL_HANDLE)
            vkDestroyBuffer(g_instance.device, job->completionReadbackBuffer,
                nullptr);
        if (job->completionReadbackMemory != VK_NULL_HANDLE)
            vkFreeMemory(g_instance.device, job->completionReadbackMemory,
                nullptr);
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
        if (job->commandBuffers[0] != VK_NULL_HANDLE &&
            g_executorCommandPool != VK_NULL_HANDLE)
            vkFreeCommandBuffers(g_instance.device, g_executorCommandPool,
                kExecutorSliceCount, job->commandBuffers.data());
        job->descriptorPool = VK_NULL_HANDLE;
        job->uniformBuffer = VK_NULL_HANDLE;
        job->uniformMemory = VK_NULL_HANDLE;
        job->completionReadbackBuffer = VK_NULL_HANDLE;
        job->completionReadbackMemory = VK_NULL_HANDLE;
        job->queryPool = VK_NULL_HANDLE;
        job->graphicsReady = VK_NULL_HANDLE;
        job->nativeDone = VK_NULL_HANDLE;
        job->graphicsFence = VK_NULL_HANDLE;
        job->nativeFence = VK_NULL_HANDLE;
        job->acquireFence = VK_NULL_HANDLE;
        job->commandBuffers.fill(VK_NULL_HANDLE);
        job->submittedSlice = UINT32_MAX;
    }

    bool CreateJobUniforms(ExecutorJob* job)
    {
        const VkDeviceSize alignment = std::max<VkDeviceSize>(16,
            g_deviceProperties.limits.minUniformBufferOffsetAlignment);
        job->frameUniformOffset = 0;
        job->contractUniformOffset = AlignUp(
            job->frameConstants.size(), alignment);
        job->queryBoundaryUniformOffset = AlignUp(job->contractUniformOffset +
            job->contractConstants.size(), alignment);
        job->queryGlobalUniformOffset = AlignUp(
            job->queryBoundaryUniformOffset +
                job->queryBoundaryConstants.size(), alignment);
        VkDeviceSize requestedSize = job->queryGlobalUniformOffset +
            job->queryGlobalConstants.size();

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
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkAllocateMemory(uniforms)", false);
            return false;
        }
        result = vkBindBufferMemory(g_instance.device, job->uniformBuffer,
            job->uniformMemory, 0);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkBindBufferMemory(uniforms)", false);
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
        std::memcpy(static_cast<uint8_t*>(mapped) + job->frameUniformOffset,
            job->frameConstants.data(), job->frameConstants.size());
        std::memcpy(static_cast<uint8_t*>(mapped) + job->contractUniformOffset,
            job->contractConstants.data(), job->contractConstants.size());
        std::memcpy(static_cast<uint8_t*>(mapped) +
                job->queryBoundaryUniformOffset,
            job->queryBoundaryConstants.data(),
            job->queryBoundaryConstants.size());
        std::memcpy(static_cast<uint8_t*>(mapped) +
                job->queryGlobalUniformOffset,
            job->queryGlobalConstants.data(),
            job->queryGlobalConstants.size());
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

    bool CreateCompletionReadback(ExecutorJob* job)
    {
        VkBufferCreateInfo bufferInfo = {};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = kCompletionRecordBytes;
        bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        VkResult result = vkCreateBuffer(g_instance.device, &bufferInfo,
            nullptr, &job->completionReadbackBuffer);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkCreateBuffer(completion readback)", false);
            return false;
        }
        VkMemoryRequirements requirements = {};
        vkGetBufferMemoryRequirements(g_instance.device,
            job->completionReadbackBuffer, &requirements);
        uint32_t memoryType = 0;
        if (!FindMemoryType(requirements.memoryTypeBits,
                VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT,
                VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, &memoryType,
                &job->completionReadbackMemoryFlags))
        {
            FailJob(job, VK_ERROR_FEATURE_NOT_PRESENT,
                "host-visible completion memory", false);
            return false;
        }
        VkMemoryAllocateInfo allocation = {};
        allocation.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocation.allocationSize = requirements.size;
        allocation.memoryTypeIndex = memoryType;
        result = vkAllocateMemory(g_instance.device, &allocation, nullptr,
            &job->completionReadbackMemory);
        if (result == VK_SUCCESS)
            result = vkBindBufferMemory(g_instance.device,
                job->completionReadbackBuffer, job->completionReadbackMemory, 0);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "completion readback memory", false);
            return false;
        }
        return true;
    }

    bool AccessJobResources(ExecutorJob* job)
    {
        const VkPipelineStageFlags bufferStages =
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
            VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT;
        const VkAccessFlags bufferAccess = VK_ACCESS_SHADER_READ_BIT |
            VK_ACCESS_SHADER_WRITE_BIT |
            VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
        for (uint32_t resource = 0; resource < kResourceCount; ++resource)
        {
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

            if (!g_vulkan->AccessTexture(nativeResource,
                    UnityVulkanWholeImage,
                    VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                    VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                    VK_ACCESS_SHADER_READ_BIT,
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
        const UnityVulkanBuffer& completion =
            job->buffers[kResourceCompletionJournal];
        VkDeviceSize completionOffset =
            static_cast<VkDeviceSize>(job->completionRecordIndex) *
            kCompletionRecordBytes;
        if ((completion.usage & VK_BUFFER_USAGE_TRANSFER_SRC_BIT) == 0 ||
            completionOffset + kCompletionRecordBytes > completion.sizeInBytes)
        {
            FailJob(job, VK_ERROR_FORMAT_NOT_SUPPORTED,
                "completion journal transfer-source contract", false);
            return false;
        }
        return true;
    }

    VkDeviceSize UniformOffset(uint32_t pipeline, const ExecutorJob* job)
    {
        if (pipeline == 1 || pipeline == 3)
            return job->contractUniformOffset;
        if (pipeline == 2)
            return job->queryBoundaryUniformOffset;
        if (pipeline == 4)
            return job->queryGlobalUniformOffset;
        return job->frameUniformOffset;
    }

    bool CreateJobDescriptors(ExecutorJob* job)
    {
        uint32_t storageCount = 0;
        uint32_t imageCount = 0;
        uint32_t uniformCount = 0;
        uint32_t descriptorCount = 0;
        for (const SigmaEmbeddedPipeline& pipeline :
            kSigmaExecutorPipelines)
        {
            descriptorCount += pipeline.descriptorCount;
            for (uint32_t index = 0; index < pipeline.descriptorCount; ++index)
            {
                uint32_t kind = pipeline.descriptors[index].kind;
                storageCount += kind == kEmbeddedStorageBuffer ? 1u : 0u;
                imageCount += kind == kEmbeddedSampledImage ? 1u : 0u;
                uniformCount += kind == kEmbeddedUniformBuffer ? 1u : 0u;
            }
        }
        VkDescriptorPoolSize poolSizes[3] = {};
        poolSizes[0] = {VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, storageCount};
        poolSizes[1] = {VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, imageCount};
        poolSizes[2] = {VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, uniformCount};
        VkDescriptorPoolCreateInfo poolInfo = {};
        poolInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        poolInfo.maxSets = kExecutorDispatchCount;
        poolInfo.poolSizeCount = 3;
        poolInfo.pPoolSizes = poolSizes;
        VkResult result = vkCreateDescriptorPool(g_instance.device, &poolInfo,
            nullptr, &job->descriptorPool);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkCreateDescriptorPool", false);
            return false;
        }
        std::array<VkDescriptorSetLayout, kExecutorDispatchCount> layouts = {};
        for (uint32_t index = 0; index < kExecutorDispatchCount; ++index)
            layouts[index] = g_executorPipelines[index].descriptorSetLayout;
        VkDescriptorSetAllocateInfo allocateInfo = {};
        allocateInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
        allocateInfo.descriptorPool = job->descriptorPool;
        allocateInfo.descriptorSetCount = kExecutorDispatchCount;
        allocateInfo.pSetLayouts = layouts.data();
        result = vkAllocateDescriptorSets(g_instance.device, &allocateInfo,
            job->descriptorSets.data());
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkAllocateDescriptorSets", false);
            return false;
        }

        std::vector<VkWriteDescriptorSet> writes;
        std::vector<VkDescriptorBufferInfo> bufferInfos;
        std::vector<VkDescriptorImageInfo> imageInfos;
        writes.reserve(descriptorCount);
        bufferInfos.reserve(storageCount + uniformCount);
        imageInfos.reserve(imageCount);
        for (uint32_t pipelineIndex = 0;
            pipelineIndex < kExecutorDispatchCount; ++pipelineIndex)
        {
            const SigmaEmbeddedPipeline& pipeline =
                kSigmaExecutorPipelines[pipelineIndex];
            for (uint32_t descriptorIndex = 0;
                descriptorIndex < pipeline.descriptorCount; ++descriptorIndex)
            {
                const SigmaEmbeddedDescriptor& descriptor =
                    pipeline.descriptors[descriptorIndex];
                VkWriteDescriptorSet write = {};
                write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
                write.dstSet = job->descriptorSets[pipelineIndex];
                write.dstBinding = descriptor.binding;
                write.descriptorCount = 1;
                if (descriptor.kind == kEmbeddedSampledImage)
                {
                    VkDescriptorImageInfo info = {};
                    info.imageView = job->imageViews[descriptor.resource];
                    info.imageLayout =
                        VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
                    imageInfos.push_back(info);
                    write.descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
                    write.pImageInfo = &imageInfos.back();
                }
                else
                {
                    VkDescriptorBufferInfo info = {};
                    if (descriptor.kind == kEmbeddedUniformBuffer)
                    {
                        info.buffer = job->uniformBuffer;
                        info.offset = UniformOffset(pipelineIndex, job);
                        info.range = pipeline.globalSize;
                        write.descriptorType =
                            VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
                    }
                    else
                    {
                        const UnityVulkanBuffer& buffer =
                            job->buffers[descriptor.resource];
                        info.buffer = buffer.buffer;
                        info.offset = 0;
                        info.range = buffer.sizeInBytes;
                        write.descriptorType =
                            VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
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
        commandInfo.commandBufferCount = kExecutorSliceCount;
        VkResult result = vkAllocateCommandBuffers(g_instance.device,
            &commandInfo, job->commandBuffers.data());
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
        VkQueryPoolCreateInfo queryInfo = {};
        queryInfo.sType = VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO;
        queryInfo.queryType = VK_QUERY_TYPE_TIMESTAMP;
        queryInfo.queryCount = kExecutorQueryCount;
        result = vkCreateQueryPool(g_instance.device, &queryInfo, nullptr,
            &job->queryPool);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkCreateQueryPool(native job)", false);
            return false;
        }
        return true;
    }

    void RecordDispatch(ExecutorJob* job, VkCommandBuffer commandBuffer,
        uint32_t index)
    {
        vkCmdWriteTimestamp(commandBuffer,
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, job->queryPool,
            1 + index * 2);
        vkCmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_COMPUTE,
            g_executorPipelines[index].pipeline);
        vkCmdBindDescriptorSets(commandBuffer,
            VK_PIPELINE_BIND_POINT_COMPUTE,
            g_executorPipelines[index].pipelineLayout, 0, 1,
            &job->descriptorSets[index], 0, nullptr);
        if (index == 0)
            vkCmdDispatch(commandBuffer, job->observationGroups, 1, 1);
        else if (index == 1)
            vkCmdDispatch(commandBuffer, job->footprintGroupsX,
                job->footprintGroupsY, 1);
        else if (index == 3)
            vkCmdDispatch(commandBuffer, job->tileGroups, 1, 1);
        else if (index == 15)
            vkCmdDispatch(commandBuffer, 1, 1, 1);
        else
        {
            VkDeviceSize offset = 32;
            if (index == 4)
                offset = 48;
            else if (index == 13)
                offset = 16;
            vkCmdDispatchIndirect(commandBuffer,
                job->buffers[kResourceCounters].buffer, offset);
        }
        vkCmdWriteTimestamp(commandBuffer,
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, job->queryPool,
            2 + index * 2);
    }

    bool RecordJobSlice(ExecutorJob* job, uint32_t slice)
    {
        if (slice >= kExecutorSliceCount)
            return false;
        VkCommandBuffer commandBuffer = job->commandBuffers[slice];
        const bool firstSlice = slice == 0;
        const bool finalSlice = slice + 1 == kExecutorSliceCount;
        VkCommandBufferBeginInfo beginInfo = {};
        beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
        beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        VkResult result = vkBeginCommandBuffer(commandBuffer, &beginInfo);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkBeginCommandBuffer", false);
            return false;
        }
        if (firstSlice)
        {
            vkCmdResetQueryPool(commandBuffer, job->queryPool, 0,
                kExecutorQueryCount);
            VkMemoryBarrier acquire = {};
            acquire.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
            acquire.dstAccessMask = VK_ACCESS_SHADER_READ_BIT |
                VK_ACCESS_SHADER_WRITE_BIT |
                VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
            vkCmdPipelineBarrier(commandBuffer,
                VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
                    VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,
                0, 1, &acquire, 0, nullptr, 0, nullptr);
            vkCmdWriteTimestamp(commandBuffer,
                VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, job->queryPool, 0);
        }
        for (uint32_t index = kExecutorSliceBounds[slice];
            index < kExecutorSliceBounds[slice + 1]; ++index)
        {
            RecordDispatch(job, commandBuffer, index);
            VkMemoryBarrier barrier = {};
            barrier.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
            barrier.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
            barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT |
                VK_ACCESS_SHADER_WRITE_BIT |
                VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
            vkCmdPipelineBarrier(commandBuffer,
                VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
                    VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,
                0, 1, &barrier, 0, nullptr, 0, nullptr);
        }
        if (finalSlice)
        {
            VkMemoryBarrier release = {};
            release.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
            release.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
            release.dstAccessMask = VK_ACCESS_MEMORY_READ_BIT |
                VK_ACCESS_MEMORY_WRITE_BIT;
            vkCmdPipelineBarrier(commandBuffer,
                VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
                0, 1, &release, 0, nullptr, 0, nullptr);
            vkCmdWriteTimestamp(commandBuffer,
                VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, job->queryPool,
                kExecutorQueryCount - 1);

            VkDeviceSize completionOffset =
                static_cast<VkDeviceSize>(job->completionRecordIndex) *
                kCompletionRecordBytes;
            VkBufferMemoryBarrier completionRelease = {};
            completionRelease.sType =
                VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER;
            completionRelease.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
            completionRelease.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
            completionRelease.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            completionRelease.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            completionRelease.buffer =
                job->buffers[kResourceCompletionJournal].buffer;
            completionRelease.offset = completionOffset;
            completionRelease.size = kCompletionRecordBytes;
            vkCmdPipelineBarrier(commandBuffer,
                VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 1,
                &completionRelease, 0, nullptr);
            VkBufferCopy completionCopy = {};
            completionCopy.srcOffset = completionOffset;
            completionCopy.dstOffset = 0;
            completionCopy.size = kCompletionRecordBytes;
            vkCmdCopyBuffer(commandBuffer,
                job->buffers[kResourceCompletionJournal].buffer,
                job->completionReadbackBuffer, 1, &completionCopy);
            VkBufferMemoryBarrier hostAcquire = {};
            hostAcquire.sType = VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER;
            hostAcquire.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            hostAcquire.dstAccessMask = VK_ACCESS_HOST_READ_BIT;
            hostAcquire.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            hostAcquire.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            hostAcquire.buffer = job->completionReadbackBuffer;
            hostAcquire.offset = 0;
            hostAcquire.size = kCompletionRecordBytes;
            vkCmdPipelineBarrier(commandBuffer,
                VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_HOST_BIT,
                0, 0, nullptr, 1, &hostAcquire, 0, nullptr);
        }
        result = vkEndCommandBuffer(commandBuffer);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkEndCommandBuffer", false);
            return false;
        }
        return true;
    }

    bool RecordJobCommands(ExecutorJob* job)
    {
        for (uint32_t slice = 0; slice < kExecutorSliceCount; ++slice)
            if (!RecordJobSlice(job, slice))
                return false;
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
        std::lock_guard<std::mutex> lock(g_executorMutex);
        bool complete = AccessJobResources(job) && CreateJobUniforms(job) &&
            CreateCompletionReadback(job) && CreateJobDescriptors(job) &&
            CreateJobCommandObjects(job) && RecordJobCommands(job);
        if (complete)
        {
            job->state.store(kJobPrepared, std::memory_order_release);
            return;
        }
        // The prepare callback itself is part of the submitted Unity graphics
        // command. Even a local setup failure must retain every input until a
        // queue-0 fence proves that the prepass and inserted barriers completed.
        job->state.store(kJobFailedNeedsGraphicsCompletion,
            std::memory_order_release);
    }

    bool SubmitNativeSlice(ExecutorJob* job, uint32_t slice,
        bool waitForGraphics, bool graphicsCompletionRequired)
    {
        if (job == nullptr || slice >= kExecutorSliceCount)
            return false;
        if (slice != 0)
        {
            VkResult reset = vkResetFences(g_instance.device, 1,
                &job->nativeFence);
            if (reset != VK_SUCCESS)
            {
                FailJob(job, reset, "vkResetFences(native slice)", false);
                return false;
            }
        }
        VkPipelineStageFlags waitStage =
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
            VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT;
        VkSubmitInfo nativeSubmit = {};
        nativeSubmit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
        if (waitForGraphics)
        {
            nativeSubmit.waitSemaphoreCount = 1;
            nativeSubmit.pWaitSemaphores = &job->graphicsReady;
            nativeSubmit.pWaitDstStageMask = &waitStage;
        }
        nativeSubmit.commandBufferCount = 1;
        nativeSubmit.pCommandBuffers = &job->commandBuffers[slice];
        const bool finalSlice = slice + 1 == kExecutorSliceCount;
        if (finalSlice)
        {
            nativeSubmit.signalSemaphoreCount = 1;
            nativeSubmit.pSignalSemaphores = &job->nativeDone;
        }
        VkResult result = VK_SUCCESS;
        {
            std::lock_guard<std::mutex> queueLock(g_sigmaQueueMutex);
            result = QueueSubmitDirect(g_sigmaQueue, 1, &nativeSubmit,
                job->nativeFence);
        }
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkQueueSubmit(Sigma slice)",
                graphicsCompletionRequired);
            return false;
        }
        job->submittedSlice = slice;
        job->state.store(kJobSubmitted, std::memory_order_release);
        if (g_log != nullptr)
        {
            char message[448] = {};
            std::snprintf(message, sizeof(message),
                "Sigma N4.2R native slice submit: revision=%u family=%u "
                "queue=%u slice=%u/%u dispatchRange=[%u,%u) "
                "graphicsReadyWait=%u nativeDoneSignal=%u "
                "unityFenceOnQueue1=0 graphicsWaitOnNative=0",
                job->revision, g_injectedQueueFamily, g_injectedQueueIndex,
                slice + 1, kExecutorSliceCount,
                kExecutorSliceBounds[slice], kExecutorSliceBounds[slice + 1],
                waitForGraphics ? 1u : 0u, finalSlice ? 1u : 0u);
            UNITY_LOG(g_log, message);
        }
        return true;
    }

    void SubmitExecutorJob(ExecutorJob* job)
    {
        if (job == nullptr || g_sigmaQueue == VK_NULL_HANDLE ||
            g_instance.graphicsQueue == VK_NULL_HANDLE)
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
        VkResult result = QueueSubmitDirect(g_instance.graphicsQueue, 1,
            &graphicsSubmit, job->graphicsFence);
        if (result != VK_SUCCESS)
        {
            FailJob(job, result, "vkQueueSubmit(graphicsReady)", false);
            return;
        }
        job->graphicsSubmitted = true;
        if (state != kJobPrepared)
            return;

        SubmitNativeSlice(job, 0, true, true);
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
            VkPipelineStageFlags waitStage =
                VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
            VkSubmitInfo acquire = {};
            acquire.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
            acquire.waitSemaphoreCount = 1;
            acquire.pWaitSemaphores = &job->nativeDone;
            acquire.pWaitDstStageMask = &waitStage;
            VkResult result = QueueSubmitDirect(g_instance.graphicsQueue, 1,
                &acquire, job->acquireFence);
            if (result != VK_SUCCESS)
                FailJob(job, result, "vkQueueSubmit(nativeDone acquire)",
                    true);
            else if (g_log != nullptr)
            {
                char message[256] = {};
                std::snprintf(message, sizeof(message),
                    "Sigma N4.2R acquire submitted: revision=%u "
                    "nativeDoneAlreadySignaled=1", job->revision);
                UNITY_LOG(g_log, message);
            }
        }
    }

    bool CollectJobTimings(ExecutorJob* job)
    {
        if (job->timingsReady)
            return true;
        VkResult result = vkGetQueryPoolResults(g_instance.device,
            job->queryPool, 0, kExecutorQueryCount,
            sizeof(uint64_t) * job->timestamps.size(),
            job->timestamps.data(), sizeof(uint64_t),
            VK_QUERY_RESULT_64_BIT);
        if (result == VK_NOT_READY)
            return false;
        if (result != VK_SUCCESS)
        {
            LogExecutorError("vkGetQueryPoolResults(native job)", result);
            return false;
        }
        job->timingsReady = true;
        return true;
    }

    bool ReadJobCompletion(ExecutorJob* job, uint32_t* words,
        uint32_t wordCapacity)
    {
        if (job == nullptr || words == nullptr ||
            wordCapacity < kCompletionWordCount * 2 ||
            job->state.load(std::memory_order_acquire) != kJobComplete ||
            job->completionReadbackMemory == VK_NULL_HANDLE)
            return false;
        void* mapped = nullptr;
        VkResult result = vkMapMemory(g_instance.device,
            job->completionReadbackMemory, 0, VK_WHOLE_SIZE, 0,
            &mapped);
        if (result != VK_SUCCESS || mapped == nullptr)
        {
            LogExecutorError("vkMapMemory(completion)", result);
            return false;
        }
        if ((job->completionReadbackMemoryFlags &
                VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) == 0)
        {
            VkMappedMemoryRange range = {};
            range.sType = VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
            range.memory = job->completionReadbackMemory;
            range.offset = 0;
            range.size = VK_WHOLE_SIZE;
            VkResult invalidate = vkInvalidateMappedMemoryRanges(
                g_instance.device, 1, &range);
            if (invalidate != VK_SUCCESS)
            {
                vkUnmapMemory(g_instance.device,
                    job->completionReadbackMemory);
                LogExecutorError("vkInvalidateMappedMemoryRanges(completion)",
                    invalidate);
                return false;
            }
        }
        std::memcpy(words, mapped,
            static_cast<size_t>(kCompletionRecordBytes));
        vkUnmapMemory(g_instance.device, job->completionReadbackMemory);
        job->completionReady = true;
        return true;
    }

    void ShutdownExecutor()
    {
        g_executorReady = false;
        if (g_instance.device == VK_NULL_HANDLE)
            return;
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
        if (g_executorCommandPool != VK_NULL_HANDLE)
            vkDestroyCommandPool(g_instance.device, g_executorCommandPool,
                nullptr);
        g_executorCommandPool = VK_NULL_HANDLE;
        g_sigmaQueue = VK_NULL_HANDLE;
    }

    bool InitializeExecutor()
    {
        if (!g_queueInjected ||
            g_injectedQueueFamily != g_instance.queueFamilyIndex ||
            g_injectedQueueIndex == UINT32_MAX)
        {
            Log("Sigma N4.2R native executor unavailable: no injected "
                "same-family compute queue.");
            return false;
        }
        vkGetDeviceQueue(g_instance.device, g_injectedQueueFamily,
            g_injectedQueueIndex, &g_sigmaQueue);
        if (g_sigmaQueue == VK_NULL_HANDLE ||
            g_sigmaQueue == g_instance.graphicsQueue)
        {
            Log("Sigma N4.2R native executor unavailable: Sigma queue is "
                "null or aliases Unity queue 0.");
            g_sigmaQueue = VK_NULL_HANDLE;
            return false;
        }
        VkCommandPoolCreateInfo commandPoolInfo = {};
        commandPoolInfo.sType =
            VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
        commandPoolInfo.flags =
            VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        commandPoolInfo.queueFamilyIndex = g_injectedQueueFamily;
        VkResult result = vkCreateCommandPool(g_instance.device,
            &commandPoolInfo, nullptr, &g_executorCommandPool);
        if (result != VK_SUCCESS)
        {
            LogExecutorError("vkCreateCommandPool", result);
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
            char message[512] = {};
            std::snprintf(message, sizeof(message),
                "Sigma N4.2R native executor ready: family=%u queue=%u "
                "unityQueue=0 pipelines=%u dispatches=16 slices=%u "
                "queue0WaitsNative=0",
                g_injectedQueueFamily, g_injectedQueueIndex,
                kExecutorDispatchCount, kExecutorSliceCount);
            UNITY_LOG(g_log, message);
        }
        return true;
    }

    void LogQueueProbe(const VkPhysicalDeviceProperties& properties,
        const VkQueueFamilyProperties* queues, uint32_t queueFamilyCount)
    {
        if (g_log == nullptr)
            return;

        char message[768] = {};
        std::snprintf(message, sizeof(message),
            "Sigma N4.2R Vulkan queue probe: device=%s vendor=0x%04x "
            "device=0x%04x api=%u.%u.%u driver=0x%08x families=%u "
            "unityFamily=%u timestampComputeAndGraphics=%u",
            properties.deviceName, properties.vendorID, properties.deviceID,
            VK_VERSION_MAJOR(properties.apiVersion),
            VK_VERSION_MINOR(properties.apiVersion),
            VK_VERSION_PATCH(properties.apiVersion), properties.driverVersion,
            queueFamilyCount, g_instance.queueFamilyIndex,
            properties.limits.timestampComputeAndGraphics == VK_TRUE ? 1u : 0u);
        UNITY_LOG(g_log, message);

        bool unityFamilyValid = g_instance.queueFamilyIndex < queueFamilyCount;
        bool sameFamilySecondQueue = false;
        bool separateComputeFamily = false;
        bool dedicatedComputeFamily = false;
        for (uint32_t family = 0; family < queueFamilyCount; ++family)
        {
            const VkQueueFamilyProperties& queue = queues[family];
            bool graphics = (queue.queueFlags & VK_QUEUE_GRAPHICS_BIT) != 0;
            bool compute = (queue.queueFlags & VK_QUEUE_COMPUTE_BIT) != 0;
            bool transfer = (queue.queueFlags & VK_QUEUE_TRANSFER_BIT) != 0;
            bool sparse = (queue.queueFlags & VK_QUEUE_SPARSE_BINDING_BIT) != 0;
            bool protectedQueue =
                (queue.queueFlags & VK_QUEUE_PROTECTED_BIT) != 0;
            std::snprintf(message, sizeof(message),
                "Sigma N4.2R Vulkan queue family[%u]: flags=0x%08x "
                "graphics=%u compute=%u transfer=%u sparse=%u protected=%u "
                "queueCount=%u timestampValidBits=%u granularity=%ux%ux%u%s",
                family, queue.queueFlags, graphics ? 1u : 0u,
                compute ? 1u : 0u, transfer ? 1u : 0u, sparse ? 1u : 0u,
                protectedQueue ? 1u : 0u, queue.queueCount,
                queue.timestampValidBits,
                queue.minImageTransferGranularity.width,
                queue.minImageTransferGranularity.height,
                queue.minImageTransferGranularity.depth,
                family == g_instance.queueFamilyIndex ? " UNITY_GRAPHICS" : "");
            UNITY_LOG(g_log, message);

            if (family == g_instance.queueFamilyIndex)
                sameFamilySecondQueue = compute && queue.queueCount >= 2;
            else if (compute)
            {
                separateComputeFamily = true;
                dedicatedComputeFamily |= !graphics;
            }
        }

        bool unityQueueMatchesFamilyQueue0 = false;
        if (unityFamilyValid)
        {
            VkQueue familyQueue0 = VK_NULL_HANDLE;
            vkGetDeviceQueue(g_instance.device, g_instance.queueFamilyIndex, 0,
                &familyQueue0);
            unityQueueMatchesFamilyQueue0 =
                familyQueue0 != VK_NULL_HANDLE &&
                familyQueue0 == g_instance.graphicsQueue;
        }
        std::snprintf(message, sizeof(message),
            "Sigma N4.2R Vulkan queue verdict: unityFamilyValid=%u "
            "unityQueueMatchesFamilyQueue0=%u "
            "sameFamilySecondQueuePhysicallyAvailable=%u "
            "separateComputeFamily=%u dedicatedComputeFamily=%u",
            unityFamilyValid ? 1u : 0u,
            unityQueueMatchesFamilyQueue0 ? 1u : 0u,
            sameFamilySecondQueue ? 1u : 0u,
            separateComputeFamily ? 1u : 0u,
            dedicatedComputeFamily ? 1u : 0u);
        UNITY_LOG(g_log, message);
    }

    void UNITY_INTERFACE_API OnRenderEvent(int eventId)
    {
        if (g_vulkan == nullptr || g_queryPool == VK_NULL_HANDLE)
            return;
        UnityVulkanRecordingState recording = {};
        if (!g_vulkan->CommandRecordingState(&recording,
                kUnityVulkanGraphicsQueueAccess_DontCare))
            return;
        int event = eventId - g_eventBase;
        if (event == kSubmissionBegin)
        {
            if (g_state.load(std::memory_order_acquire) != kArmed)
                return;
            vkCmdResetQueryPool(recording.commandBuffer, g_queryPool, 0,
                kMaximumQueries);
            g_dispatchCount = 0;
            g_recordedDispatches = 0;
            g_openDispatch = kNoDispatch;
            g_overflow = 0;
            g_state.store(kRecording, std::memory_order_release);
            return;
        }
        if (event == kDispatchBegin)
        {
            if (g_state.load(std::memory_order_acquire) != kRecording)
                return;
            if (g_openDispatch != kNoDispatch)
            {
                g_overflow = 1;
                return;
            }
            g_openDispatch = g_dispatchCount;
            if (g_openDispatch >= kMaximumDispatches)
            {
                g_overflow = 1;
                return;
            }
            vkCmdWriteTimestamp(recording.commandBuffer,
                VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, g_queryPool,
                g_openDispatch * 2);
            return;
        }
        if (event == kDispatchEnd)
        {
            if (g_state.load(std::memory_order_acquire) != kRecording ||
                g_openDispatch == kNoDispatch)
            {
                g_overflow = 1;
                return;
            }
            if (g_openDispatch < kMaximumDispatches)
            {
                vkCmdWriteTimestamp(recording.commandBuffer,
                    VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, g_queryPool,
                    g_openDispatch * 2 + 1);
                ++g_dispatchCount;
            }
            g_openDispatch = kNoDispatch;
            return;
        }
        if (event != kSubmissionEnd ||
            g_state.load(std::memory_order_acquire) != kRecording)
            return;
        if (g_openDispatch != kNoDispatch)
            g_overflow = 1;
        g_recordedDispatches = g_dispatchCount;
        g_openDispatch = kNoDispatch;
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
        LogQueueProbe(properties, queues, queueCount);
        g_timestampValidBits =
            queues[g_instance.queueFamilyIndex].timestampValidBits;
        g_timestampPeriod = properties.limits.timestampPeriod;
        InitializeExecutor();
        if (!properties.limits.timestampComputeAndGraphics)
            return;
        if (g_timestampValidBits == 0 || g_timestampPeriod <= 0.0)
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
                ? "Sigma N4.2R vkCreateDevice interception registered."
                : "Sigma N4.2R vkCreateDevice interception registration "
                  "failed; plugin was loaded too late.");
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

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaExecutor_IsAvailable()
    {
        return g_executorReady && g_sigmaQueue != VK_NULL_HANDLE ? 1 : 0;
    }

    uint32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
        SigmaExecutor_GetAbiVersion()
    {
        return kExecutorAbiVersion;
    }

    void* UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaExecutor_CreateJob(
        const SigmaExecutorJobDescriptor* descriptor)
    {
        if (!g_executorReady || descriptor == nullptr ||
            descriptor->structSize != sizeof(SigmaExecutorJobDescriptor) ||
            descriptor->abiVersion != kExecutorAbiVersion ||
            descriptor->revision == 0 ||
            descriptor->resourceCount != kResourceCount ||
            descriptor->resources == nullptr ||
            descriptor->frameConstants == nullptr ||
            descriptor->contractConstants == nullptr ||
            descriptor->queryBoundaryConstants == nullptr ||
            descriptor->queryGlobalConstants == nullptr ||
            descriptor->frameConstantsSize < 736 ||
            descriptor->contractConstantsSize < 120 ||
            descriptor->queryBoundaryConstantsSize < 120 ||
            descriptor->queryGlobalConstantsSize < 120 ||
            descriptor->observationGroups == 0 ||
            descriptor->footprintGroupsX == 0 ||
            descriptor->footprintGroupsY == 0 ||
            descriptor->tileGroups == 0 ||
            descriptor->observationGroups > 65535 ||
            descriptor->footprintGroupsX > 65535 ||
            descriptor->footprintGroupsY > 65535 ||
            descriptor->tileGroups > 65535 ||
            descriptor->completionRecordIndex >= 16)
            return nullptr;
        ExecutorJob* job = new ExecutorJob();
        job->revision = descriptor->revision;
        std::copy(descriptor->resources,
            descriptor->resources + kResourceCount,
            job->nativeResources.begin());
        job->frameConstants.assign(descriptor->frameConstants,
            descriptor->frameConstants + descriptor->frameConstantsSize);
        job->contractConstants.assign(descriptor->contractConstants,
            descriptor->contractConstants +
                descriptor->contractConstantsSize);
        job->queryBoundaryConstants.assign(
            descriptor->queryBoundaryConstants,
            descriptor->queryBoundaryConstants +
                descriptor->queryBoundaryConstantsSize);
        job->queryGlobalConstants.assign(descriptor->queryGlobalConstants,
            descriptor->queryGlobalConstants +
                descriptor->queryGlobalConstantsSize);
        job->observationGroups = descriptor->observationGroups;
        job->footprintGroupsX = descriptor->footprintGroupsX;
        job->footprintGroupsY = descriptor->footprintGroupsY;
        job->tileGroups = descriptor->tileGroups;
        job->completionRecordIndex = descriptor->completionRecordIndex;
        std::lock_guard<std::mutex> lock(g_executorMutex);
        g_executorJobs.push_back(job);
        return job;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaExecutor_CancelJob(
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
        SigmaExecutor_GetRenderEventFunc()
    {
        return OnExecutorEvent;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaExecutor_GetEventId(
        int offset)
    {
        return g_executorReady && offset >= 0 && offset < 3
            ? g_executorEventBase + offset : 0;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
        SigmaExecutor_SubmitNextSlice(void* handle)
    {
        ExecutorJob* job = static_cast<ExecutorJob*>(handle);
        if (job == nullptr)
            return 0;
        int expected = kJobSliceReady;
        if (!job->state.compare_exchange_strong(expected,
                kJobSliceSubmitting, std::memory_order_acq_rel))
            return 0;
        uint32_t nextSlice = job->submittedSlice + 1;
        if (nextSlice >= kExecutorSliceCount)
        {
            FailJob(job, VK_ERROR_INITIALIZATION_FAILED,
                "invalid Sigma slice continuation", false);
            return 0;
        }
        return SubmitNativeSlice(job, nextSlice, false, false) ? 1 : 0;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaExecutor_PollJob(
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
                const bool finalSlice =
                    job->submittedSlice + 1 == kExecutorSliceCount;
                if (finalSlice)
                {
                    CollectJobTimings(job);
                    job->state.store(kJobNativeComplete,
                        std::memory_order_release);
                    state = kJobNativeComplete;
                    if (g_log != nullptr)
                    {
                        char message[224] = {};
                        std::snprintf(message, sizeof(message),
                            "Sigma N4.2R native fence complete: revision=%u "
                            "queue=%u slices=%u dispatches=%u",
                            job->revision, g_injectedQueueIndex,
                            kExecutorSliceCount, kExecutorDispatchCount);
                        UNITY_LOG(g_log, message);
                    }
                }
                else
                {
                    job->state.store(kJobSliceReady,
                        std::memory_order_release);
                    state = kJobSliceReady;
                    if (g_log != nullptr)
                    {
                        char message[256] = {};
                        std::snprintf(message, sizeof(message),
                            "Sigma N4.2R native slice complete: revision=%u "
                            "slice=%u/%u nextDispatch=%u",
                            job->revision, job->submittedSlice + 1,
                            kExecutorSliceCount,
                            kExecutorSliceBounds[job->submittedSlice + 1]);
                        UNITY_LOG(g_log, message);
                    }
                }
            }
            else if (result != VK_NOT_READY)
            {
                job->error = result;
                job->state.store(kJobFailedSafe,
                    std::memory_order_release);
                state = kJobFailedSafe;
            }
        }
        else if (state == kJobAcquiring)
        {
            VkResult result = vkGetFenceStatus(g_instance.device,
                job->acquireFence);
            if (result == VK_SUCCESS)
            {
                job->state.store(kJobComplete, std::memory_order_release);
                state = kJobComplete;
                if (g_log != nullptr)
                {
                    char message[192] = {};
                    std::snprintf(message, sizeof(message),
                        "Sigma N4.2R acquire fence complete: revision=%u "
                        "queue0WaitWasPostSignal=1", job->revision);
                    UNITY_LOG(g_log, message);
                }
            }
            else if (result != VK_NOT_READY)
            {
                job->error = result;
                job->state.store(kJobFailedSafe,
                    std::memory_order_release);
                state = kJobFailedSafe;
            }
        }
        else if (state == kJobFailedNeedsGraphicsCompletion &&
            job->graphicsSubmitted)
        {
            VkResult result = vkGetFenceStatus(g_instance.device,
                job->graphicsFence);
            if (result == VK_SUCCESS)
            {
                job->state.store(kJobFailedSafe,
                    std::memory_order_release);
                state = kJobFailedSafe;
            }
            else if (result != VK_NOT_READY)
            {
                job->error = result;
                job->state.store(kJobFailedSafe,
                    std::memory_order_release);
                state = kJobFailedSafe;
            }
        }
        *error = static_cast<int>(job->error);
        if (state == kJobComplete)
            return 1;
        if (state == kJobNativeComplete)
            return 2;
        if (state == kJobSliceReady)
            return 3;
        if (state == kJobFailedSafe)
            return -1;
        return 0;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaExecutor_ReadTimings(
        void* handle, uint64_t* timestamps, int timestampCapacity,
        double* timestampPeriod, int* validBits)
    {
        ExecutorJob* job = static_cast<ExecutorJob*>(handle);
        if (job == nullptr || timestamps == nullptr ||
            timestampPeriod == nullptr || validBits == nullptr ||
            timestampCapacity < static_cast<int>(kExecutorQueryCount) ||
            job->state.load(std::memory_order_acquire) != kJobComplete ||
            !CollectJobTimings(job))
            return 0;
        std::copy(job->timestamps.begin(), job->timestamps.end(), timestamps);
        *timestampPeriod = g_deviceProperties.limits.timestampPeriod;
        *validBits = static_cast<int>(g_timestampValidBits);
        return kExecutorQueryCount;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
        SigmaExecutor_ReadCompletion(void* handle, uint32_t* words,
            int wordCapacity)
    {
        ExecutorJob* job = static_cast<ExecutorJob*>(handle);
        if (wordCapacity < 0 || !ReadJobCompletion(job, words,
                static_cast<uint32_t>(wordCapacity)))
            return 0;
        return static_cast<int>(kCompletionWordCount * 2);
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaExecutor_DestroyJob(
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
        SigmaTimestamp_GetRenderEventFunc()
    {
        return OnRenderEvent;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaTimestamp_GetEventId(
        int offset)
    {
        return offset >= 0 && offset < kEventCount ? g_eventBase + offset : 0;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaTimestamp_IsAvailable()
    {
        return g_state.load(std::memory_order_acquire) != kUnavailable &&
            g_queryPool != VK_NULL_HANDLE;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaTimestamp_Arm(
        uint64_t revision)
    {
        if (revision == 0)
            return 0;
        int expected = kIdle;
        if (!g_state.compare_exchange_strong(expected, kPreparing,
                std::memory_order_acq_rel))
            return 0;
        g_revision = revision;
        g_state.store(kArmed, std::memory_order_release);
        return 1;
    }

    void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaTimestamp_Cancel()
    {
        int expected = kArmed;
        g_state.compare_exchange_strong(expected, kIdle,
            std::memory_order_acq_rel);
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SigmaTimestamp_Read(
        uint64_t* timestamps, int timestampCapacity, int* dispatchCount,
        double* timestampPeriod, int* validBits, uint64_t* revision,
        int* overflow)
    {
        if (timestamps == nullptr || dispatchCount == nullptr ||
            timestampPeriod == nullptr || validBits == nullptr ||
            revision == nullptr || overflow == nullptr ||
            g_state.load(std::memory_order_acquire) != kRecorded)
            return -1;
        uint32_t queryCount = g_recordedDispatches * 2;
        if (timestampCapacity < static_cast<int>(queryCount))
            return -1;
        if (queryCount != 0)
        {
            VkResult result = vkGetQueryPoolResults(g_instance.device,
                g_queryPool, 0, queryCount,
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
        *dispatchCount = static_cast<int>(g_recordedDispatches);
        *timestampPeriod = g_timestampPeriod;
        *validBits = static_cast<int>(g_timestampValidBits);
        *revision = g_revision;
        *overflow = static_cast<int>(g_overflow);
        g_state.store(kIdle, std::memory_order_release);
        return 1;
    }
}
