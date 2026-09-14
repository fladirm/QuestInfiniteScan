// Host (linux x86_64) unit tests: json_writer and the pure vkCreateDevice patching logic.
// No Vulkan driver involved; uses the NDK vulkan_core.h with VK_NO_PROTOTYPES.
#include "../src/json_writer.h"
#include "../src/device_patch.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>

static int g_failures = 0;
#define CHECK(cond) do { if (!(cond)) { std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); ++g_failures; } } while (0)
#define CHECK_EQ_STR(a, b) do { std::string _a = (a), _b = (b); if (_a != _b) { std::printf("FAIL %s:%d: %s\n  got:  %s\n  want: %s\n", __FILE__, __LINE__, #a, _a.c_str(), _b.c_str()); ++g_failures; } } while (0)

static void TestJsonWriter() {
    fs::JsonWriter w;
    w.BeginObject();
    w.KV("a", 1);
    w.KV("b", "x\"y\\z\n\t\x01");
    w.KV("c", true);
    w.KV("d", 1.5);
    w.KV("nan", std::nan(""));
    w.KVHex("h", 0xdeadbeefull);
    w.Key("arr"); w.BeginArray(); w.Int(1); w.Int(-2); w.String("s"); w.BeginObject(); w.KV("k", uint64_t(18446744073709551615ull)); w.EndObject(); w.EndArray();
    w.Key("empty"); w.BeginObject(); w.EndObject();
    w.Key("emptyArr"); w.BeginArray(); w.EndArray();
    w.KVNull("n");
    w.RawValue("raw", "{\"z\":1}");
    w.EndObject();
    CHECK(w.Balanced());
    CHECK_EQ_STR(w.Str(),
        "{\"a\":1,\"b\":\"x\\\"y\\\\z\\n\\t\\u0001\",\"c\":true,\"d\":1.5,\"nan\":null,\"h\":\"0xdeadbeef\","
        "\"arr\":[1,-2,\"s\",{\"k\":18446744073709551615}],\"empty\":{},\"emptyArr\":[],\"n\":null,\"raw\":{\"z\":1}}");

    // CopyJsonOut contract: returns full length, NUL-terminates, truncates to cap-1.
    char buf[8];
    const int32_t need = fs::CopyJsonOut("0123456789", buf, sizeof(buf));
    CHECK(need == 10);
    CHECK_EQ_STR(buf, "0123456");
    CHECK(fs::CopyJsonOut("abc", nullptr, 0) == 3);
    char big[64];
    CHECK(fs::CopyJsonOut("abc", big, sizeof(big)) == 3);
    CHECK_EQ_STR(big, "abc");
}

// ---- Patch tests -----------------------------------------------------------------
struct Scenario {
    fs::DevicePatchCaps caps;
    // Unity's create info (const to the patcher)
    float prio[2] = { 1.0f, 1.0f };
    VkDeviceQueueCreateInfo queue = {};
    std::vector<const char*> exts;
    VkPhysicalDeviceFeatures core = {};
    VkDeviceCreateInfo info = {};
    Scenario() {
        caps.physicalApiVersion = VK_MAKE_API_VERSION(0, 1, 3, 295);
        caps.instanceApiVersion = VK_MAKE_API_VERSION(0, 1, 3, 0);
        caps.families = { {0, VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT | VK_QUEUE_TRANSFER_BIT, 4, 48}, {1, 0, 1, 0} };
        caps.availableExtensions = {
            "VK_KHR_swapchain", "VK_KHR_synchronization2", "VK_KHR_timeline_semaphore", "VK_KHR_pipeline_binary",
            "VK_KHR_maintenance5", "VK_EXT_host_query_reset", "VK_KHR_buffer_device_address", "VK_KHR_external_memory",
            "VK_KHR_dedicated_allocation", "VK_KHR_sampler_ycbcr_conversion", "VK_EXT_queue_family_foreign",
            "VK_ANDROID_external_memory_android_hardware_buffer", "VK_KHR_shader_float16_int8", "VK_KHR_16bit_storage",
            "VK_EXT_global_priority", "VK_KHR_dynamic_rendering", "VK_KHR_draw_indirect_count" };
        caps.supported.timelineSemaphore = true;
        caps.supported.bufferDeviceAddress = true;
        caps.supported.hostQueryReset = true;
        caps.supported.shaderFloat16 = true;
        caps.supported.shaderInt8 = true;
        caps.supported.storageBuffer16BitAccess = true;
        caps.supported.uniformAndStorageBuffer16BitAccess = false;
        caps.supported.synchronization2 = true;
        caps.supported.pipelineBinaries = true;
        caps.supported.samplerYcbcrConversion = true;
        queue.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
        queue.queueFamilyIndex = 0;
        queue.queueCount = 1;
        queue.pQueuePriorities = prio;
        exts = { "VK_KHR_swapchain", "VK_KHR_draw_indirect_count" };
        info.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
        info.queueCreateInfoCount = 1;
        info.pQueueCreateInfos = &queue;
        info.pEnabledFeatures = &core;
        Finalize();
    }
    void Finalize() {
        info.enabledExtensionCount = static_cast<uint32_t>(exts.size());
        info.ppEnabledExtensionNames = exts.data();
    }
};

static bool HasExt(const VkDeviceCreateInfo* ci, const char* name) {
    for (uint32_t i = 0; i < ci->enabledExtensionCount; ++i)
        if (std::strcmp(ci->ppEnabledExtensionNames[i], name) == 0) return true;
    return false;
}
static int CountExt(const VkDeviceCreateInfo* ci, const char* name) {
    int n = 0;
    for (uint32_t i = 0; i < ci->enabledExtensionCount; ++i)
        if (std::strcmp(ci->ppEnabledExtensionNames[i], name) == 0) ++n;
    return n;
}
static const VkBaseInStructure* FindNode(const VkDeviceCreateInfo* ci, VkStructureType t) {
    for (auto n = static_cast<const VkBaseInStructure*>(ci->pNext); n; n = n->pNext)
        if (n->sType == t) return n;
    return nullptr;
}
static int CountNode(const VkDeviceCreateInfo* ci, VkStructureType t) {
    int c = 0;
    for (auto n = static_cast<const VkBaseInStructure*>(ci->pNext); n; n = n->pNext) if (n->sType == t) ++c;
    return c;
}

static void TestInjectBasic() {
    Scenario sc;
    fs::DevicePatchStorage st; fs::DevicePatchResult r;
    fs::DevicePatchOptions o; o.globalPriorityLow = true;
    const VkDeviceCreateInfo* p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::InjectWithGlobalPriority, st, r);
    CHECK(p != nullptr && p != &sc.info);
    CHECK(r.queueInjected && r.injectedFamily == 0 && r.injectedIndex == 1);
    CHECK(p->queueCreateInfoCount == 1);
    CHECK(p->pQueueCreateInfos[0].queueCount == 2);
    CHECK(p->pQueueCreateInfos[0].pQueuePriorities[0] == 1.0f);
    CHECK(std::fabs(p->pQueueCreateInfos[0].pQueuePriorities[1] - 0.1f) < 1e-6f);
    // Unity's info untouched
    CHECK(sc.info.pQueueCreateInfos[0].queueCount == 1);
    CHECK(sc.info.enabledExtensionCount == 2);
    CHECK(sc.info.pNext == nullptr);
    // Global priority attached, extension added
    CHECK(r.globalPriorityAttached);
    auto gp = static_cast<const VkDeviceQueueGlobalPriorityCreateInfoKHR*>(p->pQueueCreateInfos[0].pNext);
    CHECK(gp && gp->sType == VK_STRUCTURE_TYPE_DEVICE_QUEUE_GLOBAL_PRIORITY_CREATE_INFO_KHR && gp->globalPriority == VK_QUEUE_GLOBAL_PRIORITY_LOW_KHR);
    CHECK(HasExt(p, "VK_EXT_global_priority"));
    // Wanted extensions added exactly once, unavailable ones skipped, Unity's kept first
    CHECK(std::strcmp(p->ppEnabledExtensionNames[0], "VK_KHR_swapchain") == 0);
    CHECK(CountExt(p, "VK_KHR_draw_indirect_count") == 1);
    for (const char* e : fs::WantedDeviceExtensions()) CHECK(CountExt(p, e) == 1);
    CHECK(!HasExt(p, "VK_KHR_dynamic_rendering")); // core 1.3 satisfies maintenance5 dependency
    // Vulkan12Features prepended with our bits and the implicit draw_indirect_count restated
    auto v12 = reinterpret_cast<const VkPhysicalDeviceVulkan12Features*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES));
    CHECK(v12 && v12->timelineSemaphore && v12->bufferDeviceAddress && v12->hostQueryReset && v12->shaderFloat16 && v12->shaderInt8 && v12->drawIndirectCount);
    auto s16 = reinterpret_cast<const VkPhysicalDevice16BitStorageFeatures*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_16BIT_STORAGE_FEATURES));
    CHECK(s16 && s16->storageBuffer16BitAccess && !s16->uniformAndStorageBuffer16BitAccess);
    auto sy = reinterpret_cast<const VkPhysicalDeviceSynchronization2Features*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES));
    CHECK(sy && sy->synchronization2);
    auto pb = reinterpret_cast<const VkPhysicalDevicePipelineBinaryFeaturesKHR*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_FEATURES_KHR));
    CHECK(pb && pb->pipelineBinaries);
    auto yc = reinterpret_cast<const VkPhysicalDeviceSamplerYcbcrConversionFeatures*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES));
    CHECK(yc && yc->samplerYcbcrConversion);
    CHECK(r.requested.timelineSemaphore && r.requested.synchronization2 && r.requested.pipelineBinaries && r.requested.storageBuffer16BitAccess && !r.requested.uniformAndStorageBuffer16BitAccess);
    CHECK(p->pEnabledFeatures == &sc.core); // core features untouched
    CHECK(r.chainFullyCopied);
}

static void TestVariantsAndGlobalPriorityOff() {
    Scenario sc;
    fs::DevicePatchStorage st; fs::DevicePatchResult r;
    fs::DevicePatchOptions o; // globalPriorityLow = false (default)
    const VkDeviceCreateInfo* p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::InjectWithGlobalPriority, st, r);
    CHECK(r.queueInjected && !r.globalPriorityAttached);
    CHECK(p->pQueueCreateInfos[0].pNext == nullptr);
    CHECK(!HasExt(p, "VK_EXT_global_priority"));

    p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(r.queueInjected && !r.globalPriorityAttached && p->pQueueCreateInfos[0].queueCount == 2);

    p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::FeaturesOnly, st, r);
    CHECK(!r.queueInjected && p->pQueueCreateInfos[0].queueCount == 1);
    CHECK(HasExt(p, "VK_KHR_synchronization2"));

    p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::Original, st, r);
    CHECK(p == &sc.info);
    CHECK(r.enabledExtensions.size() == 2 && r.enabledExtensions[1] == "VK_KHR_draw_indirect_count");
}

static void TestNoInjectionWhenNotSafe() {
    // Unity already requests 2 queues -> no injection.
    Scenario sc; sc.queue.queueCount = 2;
    fs::DevicePatchStorage st; fs::DevicePatchResult r; fs::DevicePatchOptions o;
    const VkDeviceCreateInfo* p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(!r.queueInjected && p->pQueueCreateInfos[0].queueCount == 2);
    // Family has a single queue -> no injection.
    Scenario sc2; sc2.caps.families[0].count = 1;
    p = fs::BuildPatchedDeviceCreateInfo(&sc2.info, sc2.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(!r.queueInjected);
    // Protected queue -> no injection.
    Scenario sc3; sc3.queue.flags = VK_DEVICE_QUEUE_CREATE_PROTECTED_BIT;
    p = fs::BuildPatchedDeviceCreateInfo(&sc3.info, sc3.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(!r.queueInjected);
    // Unity's family index 1 (no graphics|compute) -> no injection.
    Scenario sc4; sc4.queue.queueFamilyIndex = 1; sc4.caps.families[1].count = 4;
    p = fs::BuildPatchedDeviceCreateInfo(&sc4.info, sc4.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(!r.queueInjected);
}

static void TestExistingVulkan12NodeIsCopiedAndEdited() {
    Scenario sc;
    VkPhysicalDeviceVulkan12Features unity12 = {};
    unity12.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES;
    unity12.descriptorIndexing = VK_TRUE;
    VkPhysicalDeviceFeatures2 f2 = {};
    f2.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;
    f2.pNext = &unity12;
    f2.features.shaderInt64 = VK_TRUE;
    sc.info.pNext = &f2;
    sc.info.pEnabledFeatures = nullptr;
    fs::DevicePatchStorage st; fs::DevicePatchResult r; fs::DevicePatchOptions o;
    const VkDeviceCreateInfo* p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(CountNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES) == 1);
    auto v12 = reinterpret_cast<const VkPhysicalDeviceVulkan12Features*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES));
    CHECK(v12 && v12 != &unity12);
    CHECK(v12->timelineSemaphore && v12->descriptorIndexing && v12->hostQueryReset);
    CHECK(!unity12.timelineSemaphore); // Unity's struct untouched
    auto pf2 = reinterpret_cast<const VkPhysicalDeviceFeatures2*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2));
    CHECK(pf2 && pf2 != &f2 && pf2->features.shaderInt64 == VK_TRUE);
    CHECK(p->pEnabledFeatures == nullptr);
    CHECK(!FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES));
    CHECK(r.chainFullyCopied);
}

static void TestPromotedIndividualStructsPreventVulkan12Insertion() {
    Scenario sc;
    VkPhysicalDeviceTimelineSemaphoreFeatures tl = {};
    tl.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES;
    tl.timelineSemaphore = VK_FALSE;
    sc.info.pNext = &tl;
    fs::DevicePatchStorage st; fs::DevicePatchResult r; fs::DevicePatchOptions o;
    const VkDeviceCreateInfo* p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(!FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES)); // VUID 02830
    auto ptl = reinterpret_cast<const VkPhysicalDeviceTimelineSemaphoreFeatures*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES));
    CHECK(ptl && ptl != &tl && ptl->timelineSemaphore == VK_TRUE && tl.timelineSemaphore == VK_FALSE);
    CHECK(CountNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES) == 1);
    auto bda = reinterpret_cast<const VkPhysicalDeviceBufferDeviceAddressFeatures*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_BUFFER_DEVICE_ADDRESS_FEATURES));
    CHECK(bda && bda->bufferDeviceAddress);
    auto hq = reinterpret_cast<const VkPhysicalDeviceHostQueryResetFeatures*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_HOST_QUERY_RESET_FEATURES));
    CHECK(hq && hq->hostQueryReset);
    auto f16 = reinterpret_cast<const VkPhysicalDeviceShaderFloat16Int8Features*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_FLOAT16_INT8_FEATURES));
    CHECK(f16 && f16->shaderFloat16 && f16->shaderInt8);
    CHECK(r.requested.timelineSemaphore && r.requested.bufferDeviceAddress);
}

static void TestVulkan13NodeEdited() {
    Scenario sc;
    VkPhysicalDeviceVulkan13Features v13 = {};
    v13.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES;
    v13.dynamicRendering = VK_TRUE;
    sc.info.pNext = &v13;
    fs::DevicePatchStorage st; fs::DevicePatchResult r; fs::DevicePatchOptions o;
    const VkDeviceCreateInfo* p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(!FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES)); // VUID 06532
    auto p13 = reinterpret_cast<const VkPhysicalDeviceVulkan13Features*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES));
    CHECK(p13 && p13 != &v13 && p13->synchronization2 && p13->dynamicRendering && !v13.synchronization2);
    CHECK(r.requested.synchronization2);
}

static void TestOpaqueNodeStopsCopy() {
    Scenario sc;
    // An unknown sType (use a huge value) followed by Vulkan12Features: the 1.2 node is not editable.
    struct Opaque { VkStructureType sType; const void* pNext; uint64_t payload[4]; } opaque = {};
    opaque.sType = static_cast<VkStructureType>(2000000000);
    VkPhysicalDeviceVulkan12Features unity12 = {};
    unity12.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES;
    opaque.pNext = &unity12;
    VkPhysicalDeviceSynchronization2Features sy = {};
    sy.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES;
    sy.pNext = &opaque;
    sc.info.pNext = &sy;
    fs::DevicePatchStorage st; fs::DevicePatchResult r; fs::DevicePatchOptions o;
    const VkDeviceCreateInfo* p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(!r.chainFullyCopied);
    // sync2 node copied (before opaque) and edited; opaque + tail kept by reference
    auto psy = reinterpret_cast<const VkPhysicalDeviceSynchronization2Features*>(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES));
    CHECK(psy && psy != &sy && psy->synchronization2 && !sy.synchronization2);
    CHECK(psy->pNext == &opaque);
    CHECK(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES) == reinterpret_cast<const VkBaseInStructure*>(&unity12));
    CHECK(!unity12.timelineSemaphore); // not edited, not duplicated
    CHECK(CountNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES) == 1);
    CHECK(!r.requested.timelineSemaphore);
    // Still injected and extensions added
    CHECK(r.queueInjected && HasExt(p, "VK_KHR_pipeline_binary"));
}

static void TestDependenciesAndOldApi() {
    Scenario sc;
    sc.caps.instanceApiVersion = VK_MAKE_API_VERSION(0, 1, 1, 0); // effective 1.1
    // dynamic_rendering unavailable -> maintenance5 and pipeline_binary must be skipped
    sc.caps.availableExtensions.erase(std::remove(sc.caps.availableExtensions.begin(), sc.caps.availableExtensions.end(), std::string("VK_KHR_dynamic_rendering")), sc.caps.availableExtensions.end());
    fs::DevicePatchStorage st; fs::DevicePatchResult r; fs::DevicePatchOptions o;
    const VkDeviceCreateInfo* p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(!HasExt(p, "VK_KHR_maintenance5"));
    CHECK(!HasExt(p, "VK_KHR_pipeline_binary"));
    CHECK(!FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_FEATURES_KHR));
    CHECK(!r.requested.pipelineBinaries);
    // API 1.1: no Vulkan12Features; individual structs require their extensions (all present)
    CHECK(!FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES));
    CHECK(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES));
    CHECK(HasExt(p, "VK_KHR_timeline_semaphore"));
    CHECK(FindNode(p, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES) && HasExt(p, "VK_KHR_synchronization2"));

    // With dynamic_rendering available at 1.1, its own deps (depth_stencil_resolve, create_renderpass2) are missing -> still skipped
    Scenario sc2; sc2.caps.instanceApiVersion = VK_MAKE_API_VERSION(0, 1, 1, 0);
    p = fs::BuildPatchedDeviceCreateInfo(&sc2.info, sc2.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(!HasExt(p, "VK_KHR_maintenance5") && !HasExt(p, "VK_KHR_dynamic_rendering"));
    // Add the chain -> maintenance5 pulled in with dynamic_rendering + resolve + renderpass2
    sc2.caps.availableExtensions.push_back("VK_KHR_depth_stencil_resolve");
    sc2.caps.availableExtensions.push_back("VK_KHR_create_renderpass2");
    p = fs::BuildPatchedDeviceCreateInfo(&sc2.info, sc2.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(HasExt(p, "VK_KHR_maintenance5") && HasExt(p, "VK_KHR_dynamic_rendering") && HasExt(p, "VK_KHR_depth_stencil_resolve") && HasExt(p, "VK_KHR_create_renderpass2"));
    CHECK(HasExt(p, "VK_KHR_pipeline_binary"));
    // Nothing duplicated
    for (uint32_t i = 0; i < p->enabledExtensionCount; ++i) CHECK(CountExt(p, p->ppEnabledExtensionNames[i]) == 1);
}

static void TestUnsupportedFeaturesNeverRequested() {
    Scenario sc;
    sc.caps.supported = fs::FeatureSet{}; // device supports none of the wanted features
    fs::DevicePatchStorage st; fs::DevicePatchResult r; fs::DevicePatchOptions o;
    const VkDeviceCreateInfo* p = fs::BuildPatchedDeviceCreateInfo(&sc.info, sc.caps, o, fs::PatchVariant::Inject, st, r);
    CHECK(p->pNext == nullptr);
    CHECK(!r.requested.timelineSemaphore && !r.requested.synchronization2 && !r.requested.pipelineBinaries);
    CHECK(HasExt(p, "VK_KHR_pipeline_binary")); // extension still enabled when available (harmless), feature not
    CHECK(r.queueInjected);
}

int main() {
    TestJsonWriter();
    TestInjectBasic();
    TestVariantsAndGlobalPriorityOff();
    TestNoInjectionWhenNotSafe();
    TestExistingVulkan12NodeIsCopiedAndEdited();
    TestPromotedIndividualStructsPreventVulkan12Insertion();
    TestVulkan13NodeEdited();
    TestOpaqueNodeStopsCopy();
    TestDependenciesAndOldApi();
    TestUnsupportedFeaturesNeverRequested();
    if (g_failures == 0) std::printf("host tests: all passed\n");
    else std::printf("host tests: %d failure(s)\n", g_failures);
    return g_failures == 0 ? 0 : 1;
}
