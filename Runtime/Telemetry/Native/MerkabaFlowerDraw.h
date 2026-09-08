// Included by the existing Vulkan executor. Unity supplies the material,
// pipeline, XR state and resource bindings. Only its draw API call for the
// registered Flower argument buffer is replaced; no second graphics pipeline.
constexpr uint32_t kFlowerMaximumDraws = 32768u;
static_assert(sizeof(VkDrawIndexedIndirectCommand) == 20u,
    "Flower indexed command layout must match the shared 20-byte GPU ABI.");
constexpr uint32_t kFlowerCommandStride = sizeof(VkDrawIndexedIndirectCommand);
constexpr VkDeviceSize kFlowerCountOffset = kFlowerMaximumDraws * kFlowerCommandStride;
PFN_vkCmdDrawIndexedIndirect g_nextFlowerDraw = nullptr;
PFN_vkCmdDrawIndexedIndirectCount g_flowerCountDraw = nullptr;
std::atomic<VkBuffer> g_flowerDrawBuffer{VK_NULL_HANDLE};
// Availability is independent of an individual registration. A failed access
// suppresses its wrapper, but a subsequent valid registration can recover.
std::atomic<bool> g_flowerDrawReady{false};
std::atomic<bool> g_flowerDrawRegistrationValid{false};
std::atomic<bool> g_flowerDrawWrapperPending{false};
std::atomic<VkBuffer> g_flowerCountResetBuffer{VK_NULL_HANDLE};
bool g_flowerDrawDeviceEnabled = false;
bool g_flowerDrawHardwareSupported = false;
int g_flowerDrawEvent = -1;

void RecordFlowerCountReset(VkCommandBuffer command, VkBuffer buffer)
{
    VkBufferMemoryBarrier before = {};
    before.sType = VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER;
    before.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
    before.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    before.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    before.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    before.buffer = buffer;
    before.offset = kFlowerCountOffset;
    before.size = sizeof(uint32_t);
    vkCmdPipelineBarrier(command,
        VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,
        VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 1, &before, 0, nullptr);
    vkCmdFillBuffer(command, buffer, kFlowerCountOffset, sizeof(uint32_t), 0u);
    VkBufferMemoryBarrier after = before;
    after.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    after.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT;
    vkCmdPipelineBarrier(command, VK_PIPELINE_STAGE_TRANSFER_BIT,
        VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 0, nullptr, 1, &after, 0, nullptr);
}

void UNITY_INTERFACE_API ResetFlowerCullCount(int eventId, void* resource)
{
    if (g_flowerDrawEvent < 0 || eventId != g_flowerDrawEvent + 1) return;
    g_flowerCountResetBuffer.store(VK_NULL_HANDLE, std::memory_order_release);
    if (g_vulkan == nullptr || !g_flowerDrawReady.load(std::memory_order_acquire)) return;
    UnityVulkanBuffer buffer = {};
    if (resource == nullptr || !g_vulkan->AccessBuffer(resource,
        VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
        kUnityVulkanResourceAccess_PipelineBarrier, &buffer) ||
        buffer.buffer == VK_NULL_HANDLE ||
        (buffer.usage & (VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT)) !=
            (VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT) ||
        buffer.sizeInBytes < kFlowerCountOffset + sizeof(uint32_t) ||
        buffer.sizeInBytes > kMaximumQuestBufferBytes ||
        (g_deviceMaxBufferSize != 0 && buffer.sizeInBytes > g_deviceMaxBufferSize))
    {
        Log("Flower cull count reset rejected: invalid indexed argument buffer.");
        return;
    }
    // Resource access may change Unity's recording state, so query it only
    // afterwards. The caller is the existing outside-render-pass cull stage.
    UnityVulkanRecordingState recording = {};
    if (!g_vulkan->CommandRecordingState(&recording, kUnityVulkanGraphicsQueueAccess_DontCare) ||
        recording.commandBuffer == VK_NULL_HANDLE || recording.subPassIndex >= 0)
    {
        Log("Flower cull count reset requires an outside-render-pass command buffer.");
        return;
    }
    RecordFlowerCountReset(recording.commandBuffer, buffer.buffer);
    g_flowerCountResetBuffer.store(buffer.buffer, std::memory_order_release);
}

void VKAPI_PTR FlowerDrawIndexedIndirect(VkCommandBuffer command, VkBuffer buffer,
    VkDeviceSize offset, uint32_t drawCount, uint32_t stride)
{
    // The registration event immediately precedes exactly one wrapper draw on
    // Unity's serialized graphics stream. Consume that ticket even when access
    // failed: forwarding its single command would silently draw only one page.
    if (g_flowerDrawWrapperPending.exchange(false, std::memory_order_acq_rel))
    {
        const bool registrationValid = g_flowerDrawRegistrationValid.exchange(
            false, std::memory_order_acq_rel);
        if (!registrationValid || !g_flowerDrawReady.load(std::memory_order_acquire) ||
            g_flowerCountDraw == nullptr || buffer == VK_NULL_HANDLE ||
            buffer != g_flowerDrawBuffer.load(std::memory_order_acquire) ||
            offset != 0 || drawCount != 1 || stride != kFlowerCommandStride)
        {
            Log("Flower draw rejected: invalid registered indexed indirect invocation.");
            return;
        }
        // Unity binds the immutable index template, material and XR state.
        // Only the GPU consumes the complete compacted page count.
        g_flowerCountDraw(command, buffer, 0, buffer, kFlowerCountOffset,
            kFlowerMaximumDraws, kFlowerCommandStride);
        return;
    }
    if (buffer != VK_NULL_HANDLE &&
        buffer == g_flowerDrawBuffer.load(std::memory_order_acquire))
    {
        Log("Flower draw rejected: indexed wrapper has no fresh registration.");
        return;
    }
    if (g_nextFlowerDraw != nullptr)
        g_nextFlowerDraw(command, buffer, offset, drawCount, stride);
}

void UNITY_INTERFACE_API RegisterFlowerDrawBuffer(int eventId, void* resource)
{
    if (eventId != g_flowerDrawEvent) return;
    g_flowerDrawRegistrationValid.store(false, std::memory_order_release);
    g_flowerDrawWrapperPending.store(true, std::memory_order_release);
    const VkBuffer resetBuffer = g_flowerCountResetBuffer.exchange(
        VK_NULL_HANDLE, std::memory_order_acq_rel);
    if (g_vulkan == nullptr || !g_flowerDrawReady.load(std::memory_order_acquire)) return;
    UnityVulkanBuffer buffer = {};
    // Indexed DrawProceduralIndirect also declares this resource to Unity. Access
    // here establishes the compute-write -> indirect-read dependency, without
    // examining a single command or count on the CPU.
    if (resource == nullptr || !g_vulkan->AccessBuffer(resource,
        VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT, VK_ACCESS_INDIRECT_COMMAND_READ_BIT,
        kUnityVulkanResourceAccess_PipelineBarrier, &buffer))
    {
        Log("Flower indexed argument buffer access failed; its wrapper will be suppressed.");
        return;
    }
    g_flowerDrawBuffer.store(buffer.buffer, std::memory_order_release);
    if (buffer.buffer == VK_NULL_HANDLE ||
        (buffer.usage & VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT) == 0 ||
        buffer.sizeInBytes < kFlowerCountOffset + sizeof(uint32_t) ||
        buffer.sizeInBytes > kMaximumQuestBufferBytes ||
        (g_deviceMaxBufferSize != 0 && buffer.sizeInBytes > g_deviceMaxBufferSize))
    {
        Log("Flower indexed argument buffer has an invalid layout or exceeds 128 MiB.");
        return;
    }
    if (resetBuffer != buffer.buffer)
    {
        Log("Flower indexed wrapper rejected: current-view cull count was not reset.");
        return;
    }
    g_flowerDrawRegistrationValid.store(true, std::memory_order_release);
}

void InitializeFlowerDraw()
{
    if (!g_flowerDrawDeviceEnabled || !g_flowerDrawHardwareSupported ||
        g_deviceProperties.limits.maxDrawIndirectCount < kFlowerMaximumDraws)
    {
        Log("Flower draw unavailable: drawIndirectCount/firstInstance or 32768-command device limit missing.");
        return;
    }
    g_flowerCountDraw = reinterpret_cast<PFN_vkCmdDrawIndexedIndirectCount>(
        vkGetDeviceProcAddr(g_instance.device, "vkCmdDrawIndexedIndirectCount"));
    if (g_flowerCountDraw == nullptr)
        g_flowerCountDraw = reinterpret_cast<PFN_vkCmdDrawIndexedIndirectCount>(
            vkGetDeviceProcAddr(g_instance.device, "vkCmdDrawIndexedIndirectCountKHR"));
    if (g_flowerCountDraw == nullptr) return;
    g_nextFlowerDraw = reinterpret_cast<PFN_vkCmdDrawIndexedIndirect>(
        g_vulkan->InterceptVulkanAPI("vkCmdDrawIndexedIndirect",
            reinterpret_cast<PFN_vkVoidFunction>(FlowerDrawIndexedIndirect)));
    if (g_nextFlowerDraw == nullptr) return;
    if (g_flowerDrawEvent < 0) g_flowerDrawEvent = g_graphics->ReserveEventIDRange(2);
    UnityVulkanPluginEventConfig config = {};
    config.renderPassPrecondition = kUnityVulkanRenderPass_DontCare;
    config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    config.flags = kUnityVulkanEventConfigFlag_SyncWorkerThreads;
    // Both events stay DontCare on purpose. ResetFlowerCullCount requires an
    // outside-render-pass command buffer, but the precondition cannot deliver
    // it here: IUnityGraphicsVulkan states that EnsureOutside is undefined in
    // combination with the SRP RenderPass API, which URP's render graph uses,
    // and setting it changed nothing on device. The caller must therefore be
    // outside a render pass by construction, which is why the cull runs in its
    // own BeforeRendering pass rather than beside the draw.
    g_vulkan->ConfigureEvent(g_flowerDrawEvent, &config);
    g_vulkan->ConfigureEvent(g_flowerDrawEvent + 1, &config);
    g_flowerDrawReady.store(true, std::memory_order_release);
}

void ShutdownFlowerDraw()
{
    g_flowerDrawReady.store(false, std::memory_order_release);
    g_flowerDrawRegistrationValid.store(false, std::memory_order_release);
    g_flowerDrawWrapperPending.store(false, std::memory_order_release);
    g_flowerCountResetBuffer.store(VK_NULL_HANDLE, std::memory_order_release);
    g_flowerDrawBuffer.store(VK_NULL_HANDLE, std::memory_order_release);
    if (g_vulkan != nullptr && g_nextFlowerDraw != nullptr)
        g_vulkan->InterceptVulkanAPI("vkCmdDrawIndexedIndirect",
            reinterpret_cast<PFN_vkVoidFunction>(g_nextFlowerDraw));
    g_nextFlowerDraw = nullptr;
    g_flowerCountDraw = nullptr;
}
