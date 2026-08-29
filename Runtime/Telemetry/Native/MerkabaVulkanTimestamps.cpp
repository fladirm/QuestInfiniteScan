#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"

#include <atomic>
#include <cstdint>

namespace
{
    constexpr uint32_t kMaximumEntries = 4096;
    constexpr uint32_t kMaximumQueries = kMaximumEntries * 2;
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
        kDrawBegin,
        kDrawEnd,
        kSubmissionEnd,
        kEventCount,
    };

    enum EntryKind : uint32_t
    {
        kEntryNone = 0,
        kEntryCompute,
        kEntryDraw,
    };

    IUnityInterfaces* g_interfaces = nullptr;
    IUnityGraphics* g_graphics = nullptr;
    IUnityGraphicsVulkanV2* g_vulkan = nullptr;
    UnityVulkanInstance g_instance = {};
    VkQueryPool g_queryPool = VK_NULL_HANDLE;
    std::atomic<int> g_state{kUnavailable};
    uint32_t g_entryCount = 0;
    uint32_t g_recordedEntries = 0;
    uint32_t g_openEntry = kNoEntry;
    EntryKind g_openKind = kEntryNone;
    uint32_t g_overflow = 0;
    uint64_t g_revision = 0;
    double g_timestampPeriod = 0.0;
    uint32_t g_timestampValidBits = 0;
    uint64_t g_queryScratch[kMaximumQueries * 2] = {};
    int g_eventBase = 0;
    bool g_eventsReserved = false;

    bool TryRecordingState(UnityVulkanRecordingState* recording)
    {
        return g_vulkan != nullptr && g_queryPool != VK_NULL_HANDLE &&
            g_vulkan->CommandRecordingState(recording,
                kUnityVulkanGraphicsQueueAccess_DontCare);
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
            g_openEntry * 2);
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
            g_openEntry * 2 + 1);
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
            vkCmdResetQueryPool(recording.commandBuffer, g_queryPool, 0,
                kMaximumQueries);
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
        g_recordedEntries = g_entryCount;
        g_openEntry = kNoEntry;
        g_openKind = kEntryNone;
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

    void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaTimestamp_Cancel()
    {
        int expected = kArmed;
        g_state.compare_exchange_strong(expected, kIdle,
            std::memory_order_acq_rel);
    }

    int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API MerkabaTimestamp_Read(
        uint64_t* timestamps, int timestampCapacity, int* entryCount,
        double* timestampPeriod, int* validBits, uint64_t* revision,
        int* overflow)
    {
        if (timestamps == nullptr || entryCount == nullptr ||
            timestampPeriod == nullptr || validBits == nullptr ||
            revision == nullptr || overflow == nullptr)
            return -1;
        int state = g_state.load(std::memory_order_acquire);
        if (state == kArmed || state == kRecording || state == kPreparing)
            return 0;
        if (state != kRecorded)
            return -1;
        uint32_t queryCount = g_recordedEntries * 2;
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
        *entryCount = static_cast<int>(g_recordedEntries);
        *timestampPeriod = g_timestampPeriod;
        *validBits = static_cast<int>(g_timestampValidBits);
        *revision = g_revision;
        *overflow = static_cast<int>(g_overflow);
        g_state.store(kIdle, std::memory_order_release);
        return 1;
    }
}
