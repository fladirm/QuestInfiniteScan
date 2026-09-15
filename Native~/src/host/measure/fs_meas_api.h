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
// Compaction / convention knobs (C09R-E2, contract §7.6): budget = measurements selected per depth frame by the
// information score (0 = default), maxOut = hard cap of records per depth frame (clamped to FS_MEAS_GPU_RING_CAPACITY),
// flags = FS_MEAS_FLAG_* (fs_meas_params.h: 1 flipY, 2 linear depth texels, 4 DETAIL keeps edges, 8 prediction flipY).
int32_t FsMeas_SetParams(uint32_t budget, uint32_t maxOut, uint32_t flags);
// C16a (contract §14 keyframe authority): the latest PCA frame of one eye (owned Unity texture copy, world-from-camera pose
// column-major, pinhole intrinsics in delivered pixels with a bottom-left origin, capture XrTime). The emit kernel samples
// the colour of every measurement whose depth time lies within FS_MEAS_CAM_MAX_AGE_NS of the frame. tex == null clears the eye.
// Intrinsics are MRUK sensor-resolution values (sensorWidth x sensorHeight); the delivered image (width x height) is the
// aspect-keeping centre crop. rowFlip = 1 when the owned copy was written by Graphics.Blit (RT: physical row 0 = top),
// 0 for Graphics.CopyTexture of the producer texture (physical row 0 = bottom).
int32_t FsMeas_SetStereoPair(uint32_t observationId, int64_t xrTimeNsL, int64_t xrTimeNsR, uint32_t skewClass, int32_t geometryEligible, float confidence, float blurPenalty, int64_t uncertaintyNs);   // C10: the two camera slots hold a committed L/R pair
int32_t FsMeas_GetStereoStats(int64_t out[17]);
int32_t FsMeas_SetKeyframe(uint32_t slotIndex, void* unityTexture, uint32_t width, uint32_t height, uint32_t sensorWidth, uint32_t sensorHeight, const float worldFromCamera[16], float fx, float fy, float cx, float cy, int32_t rowFlip, int64_t xrTimeNs);   // C11 keyframe slot
int32_t FsMeas_GetMultiviewStats(int64_t out[22]);
int32_t FsMeas_SetCameraFrame(int32_t eye, void* unityTexture, uint32_t width, uint32_t height, uint32_t sensorWidth, uint32_t sensorHeight, const float worldFromCamera[16], float fx, float fy, float cx, float cy, int32_t rowFlip, int64_t xrTimeNs);   // budget = measurements selected per depth frame (§7.6), maxOut = record cap
// out[8]: framesSeen, framesSubmitted, framesSuperseded, lastCount, lastOverflow, lastGpuUs, importFailures, lastEdgeTexels
int32_t FsMeas_GetStats(int64_t out[8]);

#ifdef __cplusplus
}
#endif
