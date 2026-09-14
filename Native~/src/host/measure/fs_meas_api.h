// C ABI of the measurement front-end (C09, part of finalscan_host_api.h ABI 2). Implemented in
// measure.cpp; C# mirror: Runtime/Platform/Native/FinalScanHostNative.cs (SetMeasEnvDepth). Main-thread safe.
#pragma once
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif

// Twin of FsRender_SetEnvDepth (same signature, same frame, same handle): the host calls both on every NEW
// Environment Depth frame (deduplicated by XrTime on the C# side; repeats are ignored here too).
// poseL/R: world-from-eye TRS (column-major) at the depth's own XrTime; fovL/R: tan(left, right, up, down)
// with provider signs; nearZ / farZ as reported (farZ <= 0 or inf = unbounded).
int32_t FsMeas_SetEnvDepth(void* unityDepthTextureArray, uint32_t width, uint32_t height,
                           const float poseL[16], const float poseR[16], const float fovL[4], const float fovR[4],
                           float nearZ, float farZ, int64_t xrTimeNs);
// Compaction / convention knobs (provisional until C10/C12): decimK = lattice modulus (0/1 = keep all),
// maxOut = hard cap of records per depth frame (clamped to FS_MEAS_GPU_RING_CAPACITY), flags = FS_MEAS_FLAG_*
// (fs_meas_params.h: 1 flipY, 2 linear depth texels, 4 DETAIL keeps edges, 8 no decimation).
int32_t FsMeas_SetParams(uint32_t decimK, uint32_t maxOut, uint32_t flags);
// out[8]: framesSeen, framesSubmitted, framesSuperseded, lastCount, lastOverflow, lastGpuUs, importFailures, lastEdgeTexels
int32_t FsMeas_GetStats(int64_t out[8]);

#ifdef __cplusplus
}
#endif
