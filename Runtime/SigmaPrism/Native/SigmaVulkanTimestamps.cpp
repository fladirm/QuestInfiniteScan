#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"

#include <atomic>
#include <cstdint>

namespace
{
    constexpr uint32_t kMaximumDispatches = 4096;
    constexpr uint32_t kMaximumQueries = kMaximumDispatches * 2;

    enum CaptureState : int
    {
        kUnavailable = 0,
        kIdle,
        kArmed,
        kRecording,
        kRecorded,
    };

    IUnityInterfaces* g_interfaces = nullptr;
    IUnityGraphics* g_graphics = nullptr;
    IUnityGraphicsVulkanV2* g_vulkan = nullptr;
    UnityVulkanInstance g_instance = {};
    VkQueryPool g_queryPool = VK_NULL_HANDLE;
    PFN_vkCmdDispatch g_originalDispatch = nullptr;
    PFN_vkCmdDispatchIndirect g_originalDispatchIndirect = nullptr;
    std::atomic<int> g_state{kUnavailable};
    std::atomic<uint32_t> g_dispatchCount{0};
    std::atomic<uint32_t> g_overflow{0};
    std::atomic<uintptr_t> g_commandBuffer{0};
    uint32_t g_recordedDispatches = 0;
    uint64_t g_revision = 0;
    double g_timestampPeriod = 0.0;
    uint32_t g_timestampValidBits = 0;
    uint64_t g_queryScratch[kMaximumQueries * 2] = {};
    int g_beginEvent = 0;
    int g_endEvent = 0;
    bool g_eventsReserved = false;

    bool IsCaptureCommand(VkCommandBuffer commandBuffer)
    {
        return g_state.load(std::memory_order_acquire) == kRecording &&
            g_commandBuffer.load(std::memory_order_relaxed) ==
                reinterpret_cast<uintptr_t>(commandBuffer);
    }

    uint32_t BeginDispatch(VkCommandBuffer commandBuffer)
    {
        if (!IsCaptureCommand(commandBuffer))
            return kMaximumDispatches;
        uint32_t ordinal = g_dispatchCount.fetch_add(
            1, std::memory_order_relaxed);
        if (ordinal >= kMaximumDispatches)
        {
            g_overflow.store(1, std::memory_order_relaxed);
            return kMaximumDispatches;
        }
        vkCmdWriteTimestamp(commandBuffer, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
            g_queryPool, ordinal * 2);
        return ordinal;
    }

    void EndDispatch(VkCommandBuffer commandBuffer, uint32_t ordinal)
    {
        if (ordinal >= kMaximumDispatches)
            return;
        vkCmdWriteTimestamp(commandBuffer, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
            g_queryPool, ordinal * 2 + 1);
    }

    VKAPI_ATTR void VKAPI_CALL HookDispatch(VkCommandBuffer commandBuffer,
        uint32_t groupCountX, uint32_t groupCountY, uint32_t groupCountZ)
    {
        uint32_t ordinal = BeginDispatch(commandBuffer);
        g_originalDispatch(commandBuffer, groupCountX, groupCountY,
            groupCountZ);
        EndDispatch(commandBuffer, ordinal);
    }

    VKAPI_ATTR void VKAPI_CALL HookDispatchIndirect(
        VkCommandBuffer commandBuffer, VkBuffer buffer, VkDeviceSize offset)
    {
        uint32_t ordinal = BeginDispatch(commandBuffer);
        g_originalDispatchIndirect(commandBuffer, buffer, offset);
        EndDispatch(commandBuffer, ordinal);
    }

    void UNITY_INTERFACE_API OnRenderEvent(int eventId)
    {
        if (g_vulkan == nullptr || g_queryPool == VK_NULL_HANDLE)
            return;
        UnityVulkanRecordingState recording = {};
        if (!g_vulkan->CommandRecordingState(&recording,
                kUnityVulkanGraphicsQueueAccess_DontCare))
            return;
        if (eventId == g_beginEvent)
        {
            if (g_state.load(std::memory_order_acquire) != kArmed)
                return;
            vkCmdResetQueryPool(recording.commandBuffer, g_queryPool, 0,
                kMaximumQueries);
            g_dispatchCount.store(0, std::memory_order_relaxed);
            g_overflow.store(0, std::memory_order_relaxed);
            g_commandBuffer.store(
                reinterpret_cast<uintptr_t>(recording.commandBuffer),
                std::memory_order_relaxed);
            g_state.store(kRecording, std::memory_order_release);
            return;
        }
        if (eventId != g_endEvent || !IsCaptureCommand(
                recording.commandBuffer))
            return;
        g_recordedDispatches = g_dispatchCount.load(std::memory_order_relaxed);
        if (g_recordedDispatches > kMaximumDispatches)
            g_recordedDispatches = kMaximumDispatches;
        g_commandBuffer.store(0, std::memory_order_relaxed);
        g_state.store(kRecorded, std::memory_order_release);
    }

    void ShutdownVulkan()
    {
        g_state.store(kUnavailable, std::memory_order_release);
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
            g_queryPool != VK_NULL_HANDLE)
            return;
        g_vulkan = g_interfaces->Get<IUnityGraphicsVulkanV2>();
        if (g_vulkan == nullptr)
            return;
        g_instance = g_vulkan->Instance();
        if (g_instance.device == VK_NULL_HANDLE ||
            g_instance.physicalDevice == VK_NULL_HANDLE)
            return;

        VkPhysicalDeviceProperties properties = {};
        vkGetPhysicalDeviceProperties(g_instance.physicalDevice, &properties);
        uint32_t queueCount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(g_instance.physicalDevice,
            &queueCount, nullptr);
        if (!properties.limits.timestampComputeAndGraphics ||
            g_instance.queueFamilyIndex >= queueCount ||
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
            g_beginEvent = g_graphics->ReserveEventIDRange(2);
            g_endEvent = g_beginEvent + 1;
            g_eventsReserved = true;
        }
        UnityVulkanPluginEventConfig eventConfig = {};
        eventConfig.renderPassPrecondition =
            kUnityVulkanRenderPass_EnsureOutside;
        eventConfig.graphicsQueueAccess =
            kUnityVulkanGraphicsQueueAccess_DontCare;
        eventConfig.flags = kUnityVulkanEventConfigFlag_SyncWorkerThreads;
        g_vulkan->ConfigureEvent(g_beginEvent, &eventConfig);
        g_vulkan->ConfigureEvent(g_endEvent, &eventConfig);

        if (g_originalDispatch == nullptr)
            g_originalDispatch = reinterpret_cast<PFN_vkCmdDispatch>(
                g_vulkan->InterceptVulkanAPI("vkCmdDispatch",
                    reinterpret_cast<PFN_vkVoidFunction>(HookDispatch)));
        if (g_originalDispatchIndirect == nullptr)
            g_originalDispatchIndirect =
                reinterpret_cast<PFN_vkCmdDispatchIndirect>(
                    g_vulkan->InterceptVulkanAPI("vkCmdDispatchIndirect",
                        reinterpret_cast<PFN_vkVoidFunction>(
                            HookDispatchIndirect)));
        if (g_originalDispatch == nullptr ||
            g_originalDispatchIndirect == nullptr)
        {
            ShutdownVulkan();
            return;
        }
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
        if (g_graphics == nullptr)
            return;
        g_graphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
        OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
    }

    void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
    {
        if (g_graphics != nullptr)
            g_graphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
        ShutdownVulkan();
        g_graphics = nullptr;
        g_interfaces = nullptr;
    }

    UnityRenderingEvent UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
        SigmaTimestamp_GetRenderEventFunc()
    {
        return OnRenderEvent;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
        SigmaTimestamp_GetBeginEventId()
    {
        return g_beginEvent;
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
        SigmaTimestamp_GetEndEventId()
    {
        return g_endEvent;
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
        if (!g_state.compare_exchange_strong(expected, kArmed,
                std::memory_order_acq_rel))
            return 0;
        g_revision = revision;
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
        VkResult result = vkGetQueryPoolResults(g_instance.device, g_queryPool,
            0, queryCount, sizeof(uint64_t) * queryCount * 2, g_queryScratch,
            sizeof(uint64_t) * 2,
            VK_QUERY_RESULT_64_BIT | VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);
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
        *dispatchCount = static_cast<int>(g_recordedDispatches);
        *timestampPeriod = g_timestampPeriod;
        *validBits = static_cast<int>(g_timestampValidBits);
        *revision = g_revision;
        *overflow = static_cast<int>(
            g_overflow.load(std::memory_order_relaxed));
        g_state.store(kIdle, std::memory_order_release);
        return 1;
    }
}
