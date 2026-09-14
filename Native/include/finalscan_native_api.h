// FinalScan native plugin C ABI (libFinalScanNative.so). ABI 1 = C01 device probe.
// Every function is safe to call from the Unity main thread unless noted.
// JSON getters: write up to cap bytes (NUL-terminated) and return the full required length.
#pragma once
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
#define FS_NATIVE_ABI_VERSION 1
enum FsResult { FS_OK = 0, FS_ERR_UNAVAILABLE = 1, FS_ERR_INVALID = 2, FS_ERR_DEVICE = 3, FS_ERR_BUSY = 4 };
// Render-thread events (issue via CommandBuffer.IssuePluginEvent(GetRenderEventFunc(), id)).
enum FsRenderEvent {
    FS_EVT_PROBE_EMPTY_SUBMIT = 1,   // N empty submits on scanner queue (or graphics queue fallback), timestamped
    FS_EVT_PROBE_TIMESTAMP    = 2,   // timestamp query roundtrip + period check
    FS_EVT_PROBE_OVERLAP_START= 3,   // start long bounded compute job stream on scanner queue
    FS_EVT_PROBE_OVERLAP_STOP = 4,   // stop it; results in probe json
    FS_EVT_PROBE_PIPELINE_BIN = 5,   // create trivial compute pipeline, query pipeline key / binary support
    FS_EVT_PROBE_FRAME_MARK   = 6    // write a graphics-queue timestamp for frame correlation
};
int32_t FsNative_GetAbiVersion(void);
int32_t FsNative_IsVulkanReady(void);
// Device report: queue families (flags, counts, timestampValidBits), enabled/available extensions,
// limits (timestampPeriod, maxComputeWorkGroupInvocations, maxPerStageDescriptorStorageBuffers,
// maxComputeSharedMemorySize, maxPushConstantsSize, maxStorageBufferRange), features
// (synchronization2, timelineSemaphore, shaderFloat16, storageBuffer16BitAccess, bufferDeviceAddress),
// pipelineCacheUUID, driverVersion, apiVersion, deviceName, queue injection outcome, unity queue family/index.
int32_t FsNative_GetDeviceReportJson(char* buf, int32_t cap);
// Probe results accumulated by render events: emptySubmitUs[], timestampPeriodNs, overlap
// {scannerJobs, scannerBusyMs, graphicsFrameMarks[], overlapDetected}, pipelineBinary {supported, keySize}.
int32_t FsNative_GetProbeResultsJson(char* buf, int32_t cap);
void* FsNative_GetRenderEventFunc(void);
// Clocks (CLOCK_MONOTONIC / CLOCK_BOOTTIME) for clock-domain gate measurements.
int64_t FsNative_MonotonicNowNs(void);
int64_t FsNative_BoottimeNowNs(void);
// Camera2 NDK probe: enumerates cameras, logs characteristics (TIMESTAMP_SOURCE, stream configs,
// fps ranges, vendor position tags), opens every camera that permission allows with an
// AImageReader (AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE) and counts frames/timestamps/latency.
int32_t FsNative_CameraProbeStart(int32_t widthHint, int32_t heightHint, int32_t fpsHint);
int32_t FsNative_CameraProbeStop(void);
int32_t FsNative_CameraProbeReportJson(char* buf, int32_t cap);
// AHardwareBuffer -> VkImage import probe (needs Vulkan ready). Result in device report "ahbImport".
int32_t FsNative_AhbImportProbe(void);
#ifdef __cplusplus
}
#endif
