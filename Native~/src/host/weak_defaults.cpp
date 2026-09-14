// Weak default implementations of the world/residency/measurement/render C ABI (finalscan_host_api.h) so
// libFinalScanNative.so links while Native~/src/host/world/** and Native~/src/host/render/** are being
// implemented. A strong definition in those modules replaces the weak one at link time. Every default
// reports FS_ERR_UNAVAILABLE (generation getters return 0) and never touches the executor.
#include "../../include/finalscan_native_api.h"
#include "../../include/finalscan_host_api.h"
#include <IUnityInterface.h>

#define FS_WEAK __attribute__((weak))

extern "C" {

int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsMeas_Push(const FsSurfaceMeasurement* items, int32_t count, int32_t anchorId) { (void)items; (void)count; (void)anchorId; return FS_ERR_UNAVAILABLE; }

int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsWorld_CreateSynthetic(int32_t kind, float extentM, float surfelSpacingM, uint32_t seed) { (void)kind; (void)extentM; (void)surfelSpacingM; (void)seed; return FS_ERR_UNAVAILABLE; }
int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsWorld_GetPageCount(int32_t* resident, int32_t* logical) { if (resident) *resident = 0; if (logical) *logical = 0; return FS_ERR_UNAVAILABLE; }
int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsWorld_GetSurfelCount(int64_t* frontTotal, int64_t* backTotal) { if (frontTotal) *frontTotal = 0; if (backTotal) *backTotal = 0; return FS_ERR_UNAVAILABLE; }
uint32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsWorld_GetFrontGeneration(void) { return 0u; }
int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsWorld_SetAnchorTransform(int32_t anchorId, const float worldFromAnchor[16]) { (void)anchorId; (void)worldFromAnchor; return FS_ERR_UNAVAILABLE; }
int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsWorld_Erase(const float center[3], float radius, int32_t anchorId) { (void)center; (void)radius; (void)anchorId; return FS_ERR_UNAVAILABLE; }

int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsResidency_SetCenter(const float predictedPos[3], float innerRadius, float warmRadius, float prefetchRadius) { (void)predictedPos; (void)innerRadius; (void)warmRadius; (void)prefetchRadius; return FS_ERR_UNAVAILABLE; }
int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsResidency_GetZoneStats(int64_t out[8]) { if (out) for (int i = 0; i < 8; ++i) out[i] = 0; return FS_ERR_UNAVAILABLE; }

int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsRender_RegisterBuffers(void* unityDrawRecordBuffer, uint32_t drawRecordBytes, void* unityIndirectArgsBuffer) { (void)unityDrawRecordBuffer; (void)drawRecordBytes; (void)unityIndirectArgsBuffer; return FS_ERR_UNAVAILABLE; }
int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsRender_SetView(const float viewL[16], const float projL[16], const float viewR[16], const float projR[16], const float headPos[3]) { (void)viewL; (void)projL; (void)viewR; (void)projR; (void)headPos; return FS_ERR_UNAVAILABLE; }
int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsRender_GetLastCullStats(int64_t out[4]) { if (out) for (int i = 0; i < 4; ++i) out[i] = 0; return FS_ERR_UNAVAILABLE; }
int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsRender_SetPrevDepth(void* unityDepthTexture, uint32_t width, uint32_t height, uint32_t layers,
                                                                                 const float viewL[16], const float projL[16], const float viewR[16], const float projR[16]) {
    (void)unityDepthTexture; (void)width; (void)height; (void)layers; (void)viewL; (void)projL; (void)viewR; (void)projR; return FS_ERR_UNAVAILABLE; }
int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsRender_SetEnvDepth(void* unityDepthTextureArray, uint32_t width, uint32_t height,
                                                                                const float poseL[16], const float poseR[16], const float fovL[4], const float fovR[4],
                                                                                float nearZ, float farZ, int64_t xrTimeNs) {
    (void)unityDepthTextureArray; (void)width; (void)height; (void)poseL; (void)poseR; (void)fovL; (void)fovR; (void)nearZ; (void)farZ; (void)xrTimeNs; return FS_ERR_UNAVAILABLE; }
int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsRender_SetLodPolicy(float fovealErrorPx, float peripheralErrorPx, float predictionMarginDeg, uint32_t screenWorkBudget) { (void)fovealErrorPx; (void)peripheralErrorPx; (void)predictionMarginDeg; (void)screenWorkBudget; return FS_ERR_UNAVAILABLE; }
int32_t FS_WEAK UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsRender_SetGpuHeadroomUs(int32_t headroomUs) { (void)headroomUs; return FS_ERR_UNAVAILABLE; }

} // extern "C"
