// C ABI exports of finalscan_host_api.h owned by the executor (ABI 2): FsHost_*, FsSched_*, FsScan_RequestTick.
// FsWorld_/FsResidency_/FsMeas_/FsRender_ live in world/ and render/ (weak defaults in weak_defaults.cpp).
#include "executor/executor_internal.h"
#include "../json_writer.h"
#include "../log.h"
#include "../../include/finalscan_native_api.h"
#include <cstring>

extern "C" {

int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsHost_GetAbiVersion(void) { return FS_HOST_ABI_VERSION; }

int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsHost_Init(const FsHostConfig* cfg) {
    if (cfg != nullptr && cfg->structSize != 0 && cfg->structSize < 4 * (7 + 2 * FS_JOB_CLASS_COUNT + 2)) return FS_ERR_INVALID;   // must carry at least the fields up to enableSyntheticWorld
    return fs::exec::HostInit(cfg) ? FS_OK : FS_ERR_BUSY;
}

int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsHost_Shutdown(void) { fs::exec::HostShutdown(); return FS_OK; }
int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsHost_GetStatus(void) { return fs::ExecStatus(); }
int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsHost_GetTelemetryJson(char* buf, int32_t cap) { return fs::CopyJsonOut(fs::exec::HostTelemetryJson(true), buf, cap); }
int64_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsHost_GetCounter(int32_t counter) { return fs::Tele().CounterGet(counter); }
void*   UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsHost_GetRenderEventFunc(void) { return FsNative_GetRenderEventFunc(); }   // one dispatcher (plugin_entry.cpp) routes FS_HEVT_* to the executor

int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsHost_SetStorageRoot(const char* path) {
    if (path == nullptr || *path == '\0') return FS_ERR_INVALID;
    fs::exec::HostSetStorageRoot(path);
    fs::Log("FsHost_SetStorageRoot: %s", fs::StorageDir().c_str());
    return FS_OK;
}

int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsSched_SetFrameBudgetUs(uint32_t us) {
    if (us > 20000u) return FS_ERR_INVALID;   // more than a frame at 72 Hz is never a scan budget
    fs::exec::HostSetFrameBudget(us);
    return FS_OK;
}

int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsSched_SetFrameHint(double predictedDisplayTimeSec, float headPos[3], float headVel[3], float headRot[4]) {
    fs::exec::HostSetFrameHint(predictedDisplayTimeSec, headPos, headVel, headRot);
    return FS_OK;
}

int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsSched_GetClassStats(int32_t cls, int64_t out[8]) {
    if (out == nullptr) return FS_ERR_INVALID;
    return fs::exec::HostGetClassStats(cls, out) ? FS_OK : FS_ERR_INVALID;
}

int32_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API FsScan_RequestTick(uint32_t observationId) {
    const int32_t status = fs::ExecStatus();
    if (status != FS_HOST_READY && status != FS_HOST_WARMING_UP) return status == FS_HOST_UNINITIALIZED || status == FS_HOST_SHUTDOWN ? FS_ERR_UNAVAILABLE : FS_ERR_DEVICE;
    fs::exec::HostRequestScan(observationId);
    return FS_OK;
}

} // extern "C"
