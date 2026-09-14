// AHardwareBuffer -> VkImage import probe: allocate AHB (RGBA8 and YUV_420_888), query Vulkan
// properties, create image (external memory + external format), import + dedicated allocation,
// bind, create views (ycbcr conversion / plane 0), destroy. Also imports the camera probe's latest
// AHB when one is available. Every step's VkResult is reported; nothing here may crash.
#include "plugin_state.h"
#include "json_writer.h"
#include "log.h"
#include <android/hardware_buffer.h>
#include <cstring>
#include "../include/finalscan_native_api.h"
namespace { constexpr int32_t FS_OK_VALUE = FS_OK; constexpr int32_t FS_ERR_UNAVAILABLE_VALUE = FS_ERR_UNAVAILABLE; constexpr int32_t FS_ERR_DEVICE_VALUE = FS_ERR_DEVICE; }

namespace fs {
namespace {

std::mutex g_mutex;
std::string g_json = "null";

struct ImportOutcome {
    std::string name;
    int allocateResult = -1;
    uint32_t ahbFormat = 0, width = 0, height = 0; uint64_t usage = 0, stride = 0;
    VkResult propsResult = VK_NOT_READY;
    VkFormat format = VK_FORMAT_UNDEFINED; uint64_t externalFormat = 0; VkFormatFeatureFlags formatFeatures = 0;
    uint32_t ycbcrModel = 0, ycbcrRange = 0, xChroma = 0, yChroma = 0; uint64_t allocationSize = 0; uint32_t memoryTypeBits = 0;
    VkResult imageResult = VK_NOT_READY, memoryResult = VK_NOT_READY, bindResult = VK_NOT_READY;
    VkResult ycbcrResult = VK_NOT_READY, viewResult = VK_NOT_READY, plane0ViewResult = VK_NOT_READY;
    bool usedExternalFormat = false, multiPlanar = false, dedicatedRequired = false;
    std::string note;
};

bool IsMultiPlanar(VkFormat f) {
    switch (f) {
        case VK_FORMAT_G8_B8R8_2PLANE_420_UNORM: case VK_FORMAT_G8_B8_R8_3PLANE_420_UNORM:
        case VK_FORMAT_G8_B8R8_2PLANE_422_UNORM: case VK_FORMAT_G8_B8_R8_3PLANE_422_UNORM: case VK_FORMAT_G8_B8_R8_3PLANE_444_UNORM:
        case VK_FORMAT_G10X6_B10X6R10X6_2PLANE_420_UNORM_3PACK16: case VK_FORMAT_G10X6_B10X6_R10X6_3PLANE_420_UNORM_3PACK16:
        case VK_FORMAT_G16_B16R16_2PLANE_420_UNORM: case VK_FORMAT_G16_B16_R16_3PLANE_420_UNORM:
            return true;
        default: return false;
    }
}

void ImportAhb(AHardwareBuffer* ahb, ImportOutcome& o) {
    DeviceState& d = Device();
    VkDevice dev = d.instance.device;
    AHardwareBuffer_Desc desc = {};
    AHardwareBuffer_describe(ahb, &desc);
    o.ahbFormat = desc.format; o.width = desc.width; o.height = desc.height; o.usage = desc.usage; o.stride = desc.stride;
    if (!d.ahbImportEnabled) { o.note = "VK_ANDROID_external_memory_android_hardware_buffer not enabled"; return; }
    if (d.getAhbProperties == nullptr) { o.note = "vkGetAndroidHardwareBufferPropertiesANDROID unavailable"; return; }

    VkAndroidHardwareBufferFormatPropertiesANDROID fmt = {}; fmt.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID;
    VkAndroidHardwareBufferPropertiesANDROID props = {}; props.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID; props.pNext = &fmt;
    o.propsResult = d.getAhbProperties(dev, ahb, &props);
    if (o.propsResult != VK_SUCCESS) return;
    o.format = fmt.format; o.externalFormat = fmt.externalFormat; o.formatFeatures = fmt.formatFeatures;
    o.ycbcrModel = fmt.suggestedYcbcrModel; o.ycbcrRange = fmt.suggestedYcbcrRange; o.xChroma = fmt.suggestedXChromaOffset; o.yChroma = fmt.suggestedYChromaOffset;
    o.allocationSize = props.allocationSize; o.memoryTypeBits = props.memoryTypeBits;
    o.usedExternalFormat = fmt.format == VK_FORMAT_UNDEFINED;
    o.multiPlanar = IsMultiPlanar(fmt.format);

    VkExternalFormatANDROID extFormat = {}; extFormat.sType = VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID; extFormat.externalFormat = fmt.externalFormat;
    VkExternalMemoryImageCreateInfo extMem = {}; extMem.sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO;
    extMem.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID;
    if (o.usedExternalFormat) extMem.pNext = &extFormat;
    VkImageCreateInfo ici = {}; ici.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO; ici.pNext = &extMem;
    ici.imageType = VK_IMAGE_TYPE_2D; ici.format = fmt.format; ici.extent = { desc.width, desc.height, 1 };
    ici.mipLevels = 1; ici.arrayLayers = 1; ici.samples = VK_SAMPLE_COUNT_1_BIT; ici.tiling = VK_IMAGE_TILING_OPTIMAL;
    ici.usage = VK_IMAGE_USAGE_SAMPLED_BIT; ici.sharingMode = VK_SHARING_MODE_EXCLUSIVE; ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (o.multiPlanar && !o.usedExternalFormat) ici.flags = VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT;   // plane views need it
    VkImage image = VK_NULL_HANDLE;
    o.imageResult = vkCreateImage(dev, &ici, nullptr, &image);
    if (o.imageResult != VK_SUCCESS && ici.flags != 0) {
        ici.flags = 0;   // retry without MUTABLE (some drivers reject it for AHB-backed images)
        o.imageResult = vkCreateImage(dev, &ici, nullptr, &image);
        o.note += "MUTABLE_FORMAT rejected, retried without; ";
    }
    if (o.imageResult != VK_SUCCESS) return;

    if (d.getImageMemoryRequirements2) {
        VkImageMemoryRequirementsInfo2 ri = {}; ri.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_REQUIREMENTS_INFO_2; ri.image = image;
        VkMemoryDedicatedRequirements ded = {}; ded.sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_REQUIREMENTS;
        VkMemoryRequirements2 mr = {}; mr.sType = VK_STRUCTURE_TYPE_MEMORY_REQUIREMENTS_2; mr.pNext = &ded;
        if (!o.usedExternalFormat) { d.getImageMemoryRequirements2(dev, &ri, &mr); o.dedicatedRequired = ded.requiresDedicatedAllocation != VK_FALSE; }
    }
    VkImportAndroidHardwareBufferInfoANDROID import = {}; import.sType = VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID; import.buffer = ahb;
    VkMemoryDedicatedAllocateInfo dedicated = {}; dedicated.sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO; dedicated.image = image; dedicated.pNext = &import;
    VkMemoryAllocateInfo mai = {}; mai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO; mai.pNext = &dedicated;
    mai.allocationSize = props.allocationSize;
    uint32_t type = UINT32_MAX;
    for (uint32_t i = 0; i < 32; ++i) if (props.memoryTypeBits & (1u << i)) { type = i; break; }
    mai.memoryTypeIndex = type == UINT32_MAX ? 0 : type;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    o.memoryResult = type == UINT32_MAX ? VK_ERROR_FEATURE_NOT_PRESENT : vkAllocateMemory(dev, &mai, nullptr, &memory);
    if (o.memoryResult == VK_SUCCESS) o.bindResult = vkBindImageMemory(dev, image, memory, 0);

    if (o.bindResult == VK_SUCCESS) {
        VkSamplerYcbcrConversion conv = VK_NULL_HANDLE;
        const bool needConversion = o.usedExternalFormat || o.multiPlanar;
        VkSamplerYcbcrConversionInfo convInfo = {}; convInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO;
        if (needConversion) {
            if (d.ycbcrEnabled && d.createYcbcrConversion) {
                VkSamplerYcbcrConversionCreateInfo ci = {}; ci.sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO;
                if (o.usedExternalFormat) ci.pNext = &extFormat;
                ci.format = fmt.format; ci.ycbcrModel = fmt.suggestedYcbcrModel; ci.ycbcrRange = fmt.suggestedYcbcrRange;
                ci.components = fmt.samplerYcbcrConversionComponents; ci.xChromaOffset = fmt.suggestedXChromaOffset; ci.yChromaOffset = fmt.suggestedYChromaOffset;
                ci.chromaFilter = VK_FILTER_NEAREST; ci.forceExplicitReconstruction = VK_FALSE;
                o.ycbcrResult = d.createYcbcrConversion(dev, &ci, nullptr, &conv);
                convInfo.conversion = conv;
            } else o.note += "ycbcr conversion feature/proc unavailable; ";
        }
        VkImageViewCreateInfo vci = {}; vci.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        vci.image = image; vci.viewType = VK_IMAGE_VIEW_TYPE_2D; vci.format = fmt.format;
        vci.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        if (conv != VK_NULL_HANDLE) vci.pNext = &convInfo;
        VkImageView view = VK_NULL_HANDLE;
        if (!needConversion || conv != VK_NULL_HANDLE) {
            o.viewResult = vkCreateImageView(dev, &vci, nullptr, &view);
            if (view) vkDestroyImageView(dev, view, nullptr);
        }
        // Y-plane-only view (production zero-copy path wants the luma plane as R8).
        if (o.multiPlanar && !o.usedExternalFormat) {
            VkImageViewCreateInfo pv = {}; pv.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
            pv.image = image; pv.viewType = VK_IMAGE_VIEW_TYPE_2D; pv.format = VK_FORMAT_R8_UNORM;
            pv.subresourceRange = { VK_IMAGE_ASPECT_PLANE_0_BIT, 0, 1, 0, 1 };
            VkImageView planeView = VK_NULL_HANDLE;
            o.plane0ViewResult = vkCreateImageView(dev, &pv, nullptr, &planeView);
            if (planeView) vkDestroyImageView(dev, planeView, nullptr);
        }
        if (conv != VK_NULL_HANDLE && d.destroyYcbcrConversion) d.destroyYcbcrConversion(dev, conv, nullptr);
    }
    if (image) vkDestroyImage(dev, image, nullptr);
    if (memory) vkFreeMemory(dev, memory, nullptr);
}

void WriteOutcome(JsonWriter& w, const ImportOutcome& o) {
    w.BeginObject();
    w.KV("name", o.name);
    w.KV("allocateResult", static_cast<int32_t>(o.allocateResult));
    w.KVHex("ahbFormat", o.ahbFormat); w.KV("width", o.width); w.KV("height", o.height); w.KV("stride", o.stride); w.KVHex("usage", o.usage);
    w.KV("propsResult", static_cast<int32_t>(o.propsResult));
    w.KV("format", static_cast<int32_t>(o.format)); w.KV("externalFormat", o.externalFormat); w.KVHex("formatFeatures", o.formatFeatures);
    w.KV("usedExternalFormat", o.usedExternalFormat); w.KV("multiPlanar", o.multiPlanar);
    w.KV("ycbcrModel", o.ycbcrModel); w.KV("ycbcrRange", o.ycbcrRange); w.KV("xChromaOffset", o.xChroma); w.KV("yChromaOffset", o.yChroma);
    w.KV("allocationSize", o.allocationSize); w.KVHex("memoryTypeBits", o.memoryTypeBits); w.KV("dedicatedRequired", o.dedicatedRequired);
    w.KV("imageResult", static_cast<int32_t>(o.imageResult)); w.KV("memoryResult", static_cast<int32_t>(o.memoryResult)); w.KV("bindResult", static_cast<int32_t>(o.bindResult));
    w.KV("ycbcrResult", static_cast<int32_t>(o.ycbcrResult)); w.KV("viewResult", static_cast<int32_t>(o.viewResult)); w.KV("plane0ViewResult", static_cast<int32_t>(o.plane0ViewResult));
    w.KV("ok", o.bindResult == VK_SUCCESS && (o.viewResult == VK_SUCCESS || o.plane0ViewResult == VK_SUCCESS));
    w.KV("note", o.note);
    w.EndObject();
}

ImportOutcome ProbeAllocated(const char* name, uint32_t format, uint32_t w, uint32_t h, uint64_t usage) {
    ImportOutcome o; o.name = name;
    AHardwareBuffer_Desc desc = {}; desc.width = w; desc.height = h; desc.layers = 1; desc.format = format; desc.usage = usage;
    AHardwareBuffer* ahb = nullptr;
    o.allocateResult = AHardwareBuffer_allocate(&desc, &ahb);
    if (o.allocateResult != 0 || ahb == nullptr) { o.note = "AHardwareBuffer_allocate failed"; return o; }
    ImportAhb(ahb, o);
    AHardwareBuffer_release(ahb);
    return o;
}

} // namespace

int32_t RunAhbImportProbe() {
    DeviceState& d = Device();
    if (!d.ready.load(std::memory_order_acquire)) return FS_ERR_UNAVAILABLE_VALUE;
    JsonWriter w;
    w.BeginObject();
    w.KV("extensionEnabled", d.ahbImportEnabled);
    w.KV("ycbcrEnabled", d.ycbcrEnabled);
    w.Key("results"); w.BeginArray();
    ImportOutcome rgba = ProbeAllocated("rgba8", AHARDWAREBUFFER_FORMAT_R8G8B8A8_UNORM, 256, 256, AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE);
    WriteOutcome(w, rgba);
    ImportOutcome yuv = ProbeAllocated("yuv420", AHARDWAREBUFFER_FORMAT_Y8Cb8Cr8_420, 1280, 960, AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE);
    WriteOutcome(w, yuv);
    ImportOutcome yuvCpu = ProbeAllocated("yuv420+cpuRead", AHARDWAREBUFFER_FORMAT_Y8Cb8Cr8_420, 1280, 960, AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE | AHARDWAREBUFFER_USAGE_CPU_READ_OFTEN);
    WriteOutcome(w, yuvCpu);
    std::string cameraId;
    AHardwareBuffer* cam = CameraProbeAcquireLatestAhb(&cameraId);
    if (cam != nullptr) {
        ImportOutcome o; o.name = "cameraYuv:" + cameraId; o.allocateResult = 0;
        ImportAhb(cam, o);
        AHardwareBuffer_release(cam);
        WriteOutcome(w, o);
    }
    w.EndArray();
    w.KV("cameraAhbAvailable", cam != nullptr);
    w.KV("rgbaOk", rgba.bindResult == VK_SUCCESS && rgba.viewResult == VK_SUCCESS);
    w.KV("yuvOk", yuv.bindResult == VK_SUCCESS && (yuv.viewResult == VK_SUCCESS || yuv.plane0ViewResult == VK_SUCCESS));
    w.EndObject();
    std::string json = w.Take();
    { std::lock_guard<std::mutex> lock(g_mutex); g_json = json; }
    LogJson("ahb-import", json);
    return (rgba.bindResult == VK_SUCCESS) ? FS_OK_VALUE : FS_ERR_DEVICE_VALUE;
}

std::string AhbImportJson() { std::lock_guard<std::mutex> lock(g_mutex); return g_json; }

} // namespace fs
