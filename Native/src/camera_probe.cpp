// Camera2 NDK probe: enumerates cameras and characteristics (timestamp source, stream configs,
// fps ranges, intrinsics/distortion/pose, vendor position tags), then opens every camera that
// permission allows with an AImageReader (YUV_420_888, GPU_SAMPLED_IMAGE) and measures frames,
// delivery latency against CLOCK_BOOTTIME and CLOCK_MONOTONIC, timestamp monotonicity and
// AHardwareBuffer availability. Errors are reported in the JSON, never thrown.
#include "plugin_state.h"
#include "json_writer.h"
#include "log.h"
#include "../include/finalscan_native_api.h"
#include <android/hardware_buffer.h>
#include <camera/NdkCameraManager.h>
#include <camera/NdkCameraDevice.h>
#include <camera/NdkCameraMetadata.h>
#include <camera/NdkCameraMetadataTags.h>
#include <camera/NdkCaptureRequest.h>
#include <media/NdkImageReader.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <thread>

namespace fs {
namespace {

constexpr uint32_t kVendorTagSource = 0x80004d00;    // meta camera_source
constexpr uint32_t kVendorTagPosition = 0x80004d01;  // meta camera_position
constexpr int32_t kFormatYuv420 = 0x23;               // AIMAGE_FORMAT_YUV_420_888
constexpr int32_t kMaxImages = 4;

struct LatencyStats { double min = 1e300, max = -1e300, sum = 0; uint64_t n = 0;
    void Add(double v) { min = std::min(min, v); max = std::max(max, v); sum += v; ++n; }
    void Write(JsonWriter& w, const char* key) const { w.Key(key); w.BeginObject(); if (n) { w.KV("min", min, 5); w.KV("avg", sum / static_cast<double>(n), 5); w.KV("max", max, 5); } w.KV("count", n); w.EndObject(); }
};

struct VendorTagValue { bool present = false; camera_status_t status = ACAMERA_OK; uint8_t type = 0; uint32_t count = 0; std::vector<double> values; std::string text; };

struct CameraEntry {
    std::string id;
    // characteristics
    camera_status_t charsResult = ACAMERA_OK;
    int32_t lensFacing = -1, timestampSource = -1, hardwareLevel = -1, orientation = -1, poseReference = -1;
    std::vector<int32_t> activeArray, pixelArray, capabilities;
    std::vector<float> physicalSize, intrinsics, distortion, poseTranslation, poseRotation, focalLengths;
    std::vector<int64_t> exposureRange; std::vector<int32_t> sensitivityRange;
    std::vector<std::array<int32_t, 4>> streamConfigs;      // format, w, h, isInput
    std::vector<std::array<int64_t, 4>> minFrameDurations;  // format, w, h, ns
    std::vector<std::array<int32_t, 2>> fpsRanges;
    VendorTagValue vendorSource, vendorPosition;
    uint32_t tagCount = 0; std::vector<uint32_t> vendorTags;
    // probe
    camera_status_t openResult = ACAMERA_OK; bool openAttempted = false; bool opened = false;
    media_status_t readerResult = AMEDIA_OK; camera_status_t sessionResult = ACAMERA_OK, requestResult = ACAMERA_OK, repeatResult = ACAMERA_OK;
    int32_t width = 0, height = 0, fpsMin = 0, fpsMax = 0;
    std::atomic<uint64_t> frames{0}, captureCompleted{0}, captureFailed{0}, acquireErrors{0}, ahbOk{0}, ahbFail{0}, violations{0}, bufferLost{0};
    std::atomic<int> disconnected{0}, deviceError{0};
    LatencyStats latencyBoot, latencyMono, interval;
    int64_t firstTs = 0, lastTs = 0, prevTs = 0, firstCpuNs = 0, lastCpuNs = 0;
    int32_t planes = 0; std::vector<std::array<int32_t, 2>> planeStrides;   // rowStride, pixelStride
    int32_t imageFormat = 0, imageWidth = 0, imageHeight = 0;
    std::string note;
    // handles
    ACameraDevice* device = nullptr; AImageReader* reader = nullptr; ANativeWindow* window = nullptr;
    ACaptureSessionOutputContainer* outputs = nullptr; ACaptureSessionOutput* output = nullptr;
    ACameraOutputTarget* target = nullptr; ACaptureRequest* request = nullptr; ACameraCaptureSession* session = nullptr;
    std::atomic<int> sessionClosed{0};
};

struct ProbeState {
    std::mutex mutex;
    std::vector<std::unique_ptr<CameraEntry>> cameras;
    std::string managerNote; camera_status_t idListResult = ACAMERA_OK;
    bool running = false; bool started = false; bool stopRequested = false;
    int32_t widthHint = 0, heightHint = 0, fpsHint = 0;
    int64_t startNs = 0, stopNs = 0;
    std::thread thread;
    AHardwareBuffer* latestAhb = nullptr; std::string latestAhbCamera;
};
ProbeState g_state;
std::mutex g_frameMutex;   // guards per-frame stats updates (callbacks come from camera threads)

void OnDisconnected(void* ctx, ACameraDevice*) { static_cast<CameraEntry*>(ctx)->disconnected.store(1); }
void OnError(void* ctx, ACameraDevice*, int error) { static_cast<CameraEntry*>(ctx)->deviceError.store(error); }
void OnSessionClosed(void* ctx, ACameraCaptureSession*) { static_cast<CameraEntry*>(ctx)->sessionClosed.store(1); }
void OnSessionReady(void*, ACameraCaptureSession*) {}
void OnSessionActive(void*, ACameraCaptureSession*) {}
void OnCaptureCompleted(void* ctx, ACameraCaptureSession*, ACaptureRequest*, const ACameraMetadata*) { static_cast<CameraEntry*>(ctx)->captureCompleted.fetch_add(1); }
void OnCaptureFailed(void* ctx, ACameraCaptureSession*, ACaptureRequest*, ACameraCaptureFailure*) { static_cast<CameraEntry*>(ctx)->captureFailed.fetch_add(1); }
void OnBufferLost(void* ctx, ACameraCaptureSession*, ACaptureRequest*, ACameraWindowType*, int64_t) { static_cast<CameraEntry*>(ctx)->bufferLost.fetch_add(1); }

void OnImageAvailable(void* ctx, AImageReader* reader) {
    CameraEntry* cam = static_cast<CameraEntry*>(ctx);
    AImage* image = nullptr;
    media_status_t st = AImageReader_acquireNextImage(reader, &image);
    if (st != AMEDIA_OK || image == nullptr) { cam->acquireErrors.fetch_add(1); return; }
    const int64_t boot = BoottimeNs();
    const int64_t mono = MonotonicNs();
    int64_t ts = 0;
    AImage_getTimestamp(image, &ts);
    AHardwareBuffer* ahb = nullptr;
    const bool ahbOk = AImage_getHardwareBuffer(image, &ahb) == AMEDIA_OK && ahb != nullptr;
    {
        std::lock_guard<std::mutex> lock(g_frameMutex);
        const uint64_t n = cam->frames.fetch_add(1);
        if (ahbOk) cam->ahbOk.fetch_add(1); else cam->ahbFail.fetch_add(1);
        cam->latencyBoot.Add(static_cast<double>(boot - ts) / 1e6);
        cam->latencyMono.Add(static_cast<double>(mono - ts) / 1e6);
        if (n == 0) { cam->firstTs = ts; cam->firstCpuNs = mono; }
        else { if (ts <= cam->prevTs) cam->violations.fetch_add(1); cam->interval.Add(static_cast<double>(ts - cam->prevTs) / 1e6); }
        cam->prevTs = ts; cam->lastTs = ts; cam->lastCpuNs = mono;
        if (n == 0) {
            AImage_getNumberOfPlanes(image, &cam->planes);
            AImage_getFormat(image, &cam->imageFormat); AImage_getWidth(image, &cam->imageWidth); AImage_getHeight(image, &cam->imageHeight);
            for (int32_t p = 0; p < cam->planes && p < 4; ++p) { int32_t rs = 0, ps = 0; AImage_getPlaneRowStride(image, p, &rs); AImage_getPlanePixelStride(image, p, &ps); cam->planeStrides.push_back({rs, ps}); }
        }
    }
    if (ahbOk) {
        // Keep the newest buffer alive for the AHB import probe (released on replacement/stop).
        // Lock order everywhere: g_state.mutex before g_frameMutex, never nested the other way.
        AHardwareBuffer_acquire(ahb);
        AHardwareBuffer* previous = nullptr;
        {
            std::lock_guard<std::mutex> slock(g_state.mutex);
            previous = g_state.latestAhb;
            g_state.latestAhb = ahb; g_state.latestAhbCamera = cam->id;
        }
        if (previous) AHardwareBuffer_release(previous);
    }
    AImage_delete(image);
}

void ReadVendorTag(ACameraMetadata* chars, uint32_t tag, VendorTagValue& out) {
    ACameraMetadata_const_entry e = {};
    out.status = ACameraMetadata_getConstEntry(chars, tag, &e);
    if (out.status != ACAMERA_OK) return;
    out.present = true; out.type = e.type; out.count = e.count;
    const uint32_t n = std::min<uint32_t>(e.count, 16);
    for (uint32_t i = 0; i < n; ++i) {
        switch (e.type) {
            case ACAMERA_TYPE_BYTE: out.values.push_back(e.data.u8[i]); break;
            case ACAMERA_TYPE_INT32: out.values.push_back(e.data.i32[i]); break;
            case ACAMERA_TYPE_FLOAT: out.values.push_back(e.data.f[i]); break;
            case ACAMERA_TYPE_INT64: out.values.push_back(static_cast<double>(e.data.i64[i])); break;
            case ACAMERA_TYPE_DOUBLE: out.values.push_back(e.data.d[i]); break;
            case ACAMERA_TYPE_RATIONAL: out.values.push_back(e.data.r[i].denominator ? static_cast<double>(e.data.r[i].numerator) / e.data.r[i].denominator : 0.0); break;
            default: break;
        }
    }
    if (e.type == ACAMERA_TYPE_BYTE) { out.text.assign(reinterpret_cast<const char*>(e.data.u8), std::min<uint32_t>(e.count, 64)); while (!out.text.empty() && out.text.back() == '\0') out.text.pop_back(); }
}

template <typename T, typename F>
void ReadArray(ACameraMetadata* chars, uint32_t tag, std::vector<T>& out, F pick, uint32_t cap = 256) {
    ACameraMetadata_const_entry e = {};
    if (ACameraMetadata_getConstEntry(chars, tag, &e) != ACAMERA_OK) return;
    for (uint32_t i = 0; i < e.count && i < cap; ++i) out.push_back(pick(e, i));
}

void ReadCharacteristics(ACameraMetadata* chars, CameraEntry& c) {
    ACameraMetadata_const_entry e = {};
    if (ACameraMetadata_getConstEntry(chars, ACAMERA_LENS_FACING, &e) == ACAMERA_OK && e.count) c.lensFacing = e.data.u8[0];
    if (ACameraMetadata_getConstEntry(chars, ACAMERA_SENSOR_INFO_TIMESTAMP_SOURCE, &e) == ACAMERA_OK && e.count) c.timestampSource = e.data.u8[0];
    if (ACameraMetadata_getConstEntry(chars, ACAMERA_INFO_SUPPORTED_HARDWARE_LEVEL, &e) == ACAMERA_OK && e.count) c.hardwareLevel = e.data.u8[0];
    if (ACameraMetadata_getConstEntry(chars, ACAMERA_SENSOR_ORIENTATION, &e) == ACAMERA_OK && e.count) c.orientation = e.data.i32[0];
    if (ACameraMetadata_getConstEntry(chars, ACAMERA_LENS_POSE_REFERENCE, &e) == ACAMERA_OK && e.count) c.poseReference = e.data.u8[0];
    ReadArray(chars, ACAMERA_SENSOR_INFO_ACTIVE_ARRAY_SIZE, c.activeArray, [](const ACameraMetadata_const_entry& x, uint32_t i) { return x.data.i32[i]; });
    ReadArray(chars, ACAMERA_SENSOR_INFO_PIXEL_ARRAY_SIZE, c.pixelArray, [](const ACameraMetadata_const_entry& x, uint32_t i) { return x.data.i32[i]; });
    ReadArray(chars, ACAMERA_SENSOR_INFO_PHYSICAL_SIZE, c.physicalSize, [](const ACameraMetadata_const_entry& x, uint32_t i) { return x.data.f[i]; });
    ReadArray(chars, ACAMERA_LENS_INTRINSIC_CALIBRATION, c.intrinsics, [](const ACameraMetadata_const_entry& x, uint32_t i) { return x.data.f[i]; });
    ReadArray(chars, ACAMERA_LENS_DISTORTION, c.distortion, [](const ACameraMetadata_const_entry& x, uint32_t i) { return x.data.f[i]; });
    ReadArray(chars, ACAMERA_LENS_POSE_TRANSLATION, c.poseTranslation, [](const ACameraMetadata_const_entry& x, uint32_t i) { return x.data.f[i]; });
    ReadArray(chars, ACAMERA_LENS_POSE_ROTATION, c.poseRotation, [](const ACameraMetadata_const_entry& x, uint32_t i) { return x.data.f[i]; });
    ReadArray(chars, ACAMERA_LENS_INFO_AVAILABLE_FOCAL_LENGTHS, c.focalLengths, [](const ACameraMetadata_const_entry& x, uint32_t i) { return x.data.f[i]; });
    ReadArray(chars, ACAMERA_SENSOR_INFO_EXPOSURE_TIME_RANGE, c.exposureRange, [](const ACameraMetadata_const_entry& x, uint32_t i) { return x.data.i64[i]; });
    ReadArray(chars, ACAMERA_SENSOR_INFO_SENSITIVITY_RANGE, c.sensitivityRange, [](const ACameraMetadata_const_entry& x, uint32_t i) { return x.data.i32[i]; });
    ReadArray(chars, ACAMERA_REQUEST_AVAILABLE_CAPABILITIES, c.capabilities, [](const ACameraMetadata_const_entry& x, uint32_t i) { return static_cast<int32_t>(x.data.u8[i]); });
    if (ACameraMetadata_getConstEntry(chars, ACAMERA_SCALER_AVAILABLE_STREAM_CONFIGURATIONS, &e) == ACAMERA_OK)
        for (uint32_t i = 0; i + 3 < e.count && c.streamConfigs.size() < 128; i += 4) c.streamConfigs.push_back({e.data.i32[i], e.data.i32[i + 1], e.data.i32[i + 2], e.data.i32[i + 3]});
    if (ACameraMetadata_getConstEntry(chars, ACAMERA_SCALER_AVAILABLE_MIN_FRAME_DURATIONS, &e) == ACAMERA_OK)
        for (uint32_t i = 0; i + 3 < e.count && c.minFrameDurations.size() < 128; i += 4) c.minFrameDurations.push_back({e.data.i64[i], e.data.i64[i + 1], e.data.i64[i + 2], e.data.i64[i + 3]});
    if (ACameraMetadata_getConstEntry(chars, ACAMERA_CONTROL_AE_AVAILABLE_TARGET_FPS_RANGES, &e) == ACAMERA_OK)
        for (uint32_t i = 0; i + 1 < e.count; i += 2) c.fpsRanges.push_back({e.data.i32[i], e.data.i32[i + 1]});
    ReadVendorTag(chars, kVendorTagSource, c.vendorSource);
    ReadVendorTag(chars, kVendorTagPosition, c.vendorPosition);
    int32_t numTags = 0; const uint32_t* tags = nullptr;
    if (ACameraMetadata_getAllTags(chars, &numTags, &tags) == ACAMERA_OK && tags) {
        c.tagCount = static_cast<uint32_t>(numTags);
        for (int32_t i = 0; i < numTags; ++i) if (tags[i] >= 0x80000000u && c.vendorTags.size() < 64) c.vendorTags.push_back(tags[i]);
    }
}

void ChooseStream(CameraEntry& c, int32_t wHint, int32_t hHint) {
    int64_t best = INT64_MAX;
    for (const auto& s : c.streamConfigs) {
        if (s[0] != kFormatYuv420 || s[3] != 0) continue;
        const int64_t d = std::llabs(static_cast<int64_t>(s[1]) - wHint) + std::llabs(static_cast<int64_t>(s[2]) - hHint);
        if (d < best) { best = d; c.width = s[1]; c.height = s[2]; }
    }
    if (c.width == 0) { c.width = wHint > 0 ? wHint : 1280; c.height = hHint > 0 ? hHint : 960; c.note += "no YUV_420_888 stream config advertised; using hint; "; }
}

void ChooseFps(CameraEntry& c, int32_t fpsHint) {
    int32_t bestScore = INT32_MAX;
    for (const auto& r : c.fpsRanges) {
        int32_t score = (r[1] == fpsHint ? 0 : 1000) + std::abs(r[1] - fpsHint) * 4 + std::abs(r[0] - fpsHint);
        if (score < bestScore) { bestScore = score; c.fpsMin = r[0]; c.fpsMax = r[1]; }
    }
}

void OpenCamera(ACameraManager* mgr, CameraEntry& c) {
    ACameraDevice_StateCallbacks devCb = { &c, OnDisconnected, OnError };
    c.openAttempted = true;
    c.openResult = ACameraManager_openCamera(mgr, c.id.c_str(), &devCb, &c.device);
    if (c.openResult != ACAMERA_OK || c.device == nullptr) { Log("camera %s open failed: %d", c.id.c_str(), static_cast<int>(c.openResult)); return; }
    c.opened = true;
    c.readerResult = AImageReader_newWithUsage(c.width, c.height, kFormatYuv420, AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE, kMaxImages, &c.reader);
    if (c.readerResult != AMEDIA_OK || c.reader == nullptr) {
        c.note += "AImageReader_newWithUsage(GPU_SAMPLED_IMAGE) failed, retrying with CPU_READ_OFTEN; ";
        c.readerResult = AImageReader_newWithUsage(c.width, c.height, kFormatYuv420, AHARDWAREBUFFER_USAGE_CPU_READ_OFTEN, kMaxImages, &c.reader);
        if (c.readerResult != AMEDIA_OK || c.reader == nullptr) return;
    }
    AImageReader_ImageListener listener = { &c, OnImageAvailable };
    AImageReader_setImageListener(c.reader, &listener);
    if (AImageReader_getWindow(c.reader, &c.window) != AMEDIA_OK || c.window == nullptr) { c.note += "AImageReader_getWindow failed; "; return; }
    c.sessionResult = ACaptureSessionOutputContainer_create(&c.outputs);
    if (c.sessionResult == ACAMERA_OK) c.sessionResult = ACaptureSessionOutput_create(c.window, &c.output);
    if (c.sessionResult == ACAMERA_OK) c.sessionResult = ACaptureSessionOutputContainer_add(c.outputs, c.output);
    if (c.sessionResult == ACAMERA_OK) c.requestResult = ACameraDevice_createCaptureRequest(c.device, TEMPLATE_PREVIEW, &c.request);
    if (c.sessionResult == ACAMERA_OK && c.requestResult == ACAMERA_OK) c.requestResult = ACameraOutputTarget_create(c.window, &c.target);
    if (c.sessionResult == ACAMERA_OK && c.requestResult == ACAMERA_OK) c.requestResult = ACaptureRequest_addTarget(c.request, c.target);
    if (c.requestResult == ACAMERA_OK && c.fpsMax > 0) { int32_t range[2] = { c.fpsMin, c.fpsMax }; ACaptureRequest_setEntry_i32(c.request, ACAMERA_CONTROL_AE_TARGET_FPS_RANGE, 2, range); }
    if (c.sessionResult == ACAMERA_OK && c.requestResult == ACAMERA_OK) {
        ACameraCaptureSession_stateCallbacks sessCb = { &c, OnSessionClosed, OnSessionReady, OnSessionActive };
        c.sessionResult = ACameraDevice_createCaptureSession(c.device, c.outputs, &sessCb, &c.session);
    }
    if (c.sessionResult == ACAMERA_OK && c.requestResult == ACAMERA_OK && c.session) {
        ACameraCaptureSession_captureCallbacks capCb = {};
        capCb.context = &c; capCb.onCaptureCompleted = OnCaptureCompleted; capCb.onCaptureFailed = OnCaptureFailed; capCb.onCaptureBufferLost = OnBufferLost;
        c.repeatResult = ACameraCaptureSession_setRepeatingRequest(c.session, &capCb, 1, &c.request, nullptr);
    }
    Log("camera %s: open=%d reader=%d session=%d request=%d repeat=%d size=%dx%d fps=[%d,%d]", c.id.c_str(), static_cast<int>(c.openResult), static_cast<int>(c.readerResult),
        static_cast<int>(c.sessionResult), static_cast<int>(c.requestResult), static_cast<int>(c.repeatResult), c.width, c.height, c.fpsMin, c.fpsMax);
}

void CloseCamera(CameraEntry& c) {
    if (c.session) { ACameraCaptureSession_stopRepeating(c.session); ACameraCaptureSession_close(c.session); c.session = nullptr; }
    if (c.device) { ACameraDevice_close(c.device); c.device = nullptr; }
    if (c.request) { ACaptureRequest_free(c.request); c.request = nullptr; }
    if (c.target) { ACameraOutputTarget_free(c.target); c.target = nullptr; }
    if (c.output) { ACaptureSessionOutput_free(c.output); c.output = nullptr; }
    if (c.outputs) { ACaptureSessionOutputContainer_free(c.outputs); c.outputs = nullptr; }
    if (c.reader) { AImageReader_setImageListener(c.reader, nullptr); AImageReader_delete(c.reader); c.reader = nullptr; c.window = nullptr; }
}

void ProbeThread(int32_t wHint, int32_t hHint, int32_t fpsHint) {
    ACameraManager* mgr = ACameraManager_create();
    if (mgr == nullptr) { std::lock_guard<std::mutex> lock(g_state.mutex); g_state.managerNote = "ACameraManager_create returned null"; g_state.running = false; return; }
    ACameraIdList* ids = nullptr;
    camera_status_t st = ACameraManager_getCameraIdList(mgr, &ids);
    std::vector<CameraEntry*> entries;
    {
        std::lock_guard<std::mutex> lock(g_state.mutex);
        g_state.idListResult = st;
        if (st == ACAMERA_OK && ids) {
            for (int i = 0; i < ids->numCameras; ++i) { auto e = std::make_unique<CameraEntry>(); e->id = ids->cameraIds[i]; entries.push_back(e.get()); g_state.cameras.push_back(std::move(e)); }
        } else g_state.managerNote = "ACameraManager_getCameraIdList failed";
    }
    Log("camera probe: idList result=%d cameras=%zu", static_cast<int>(st), entries.size());
    for (CameraEntry* c : entries) {
        ACameraMetadata* chars = nullptr;
        c->charsResult = ACameraManager_getCameraCharacteristics(mgr, c->id.c_str(), &chars);
        if (c->charsResult == ACAMERA_OK && chars) { ReadCharacteristics(chars, *c); ACameraMetadata_free(chars); }
        ChooseStream(*c, wHint, hHint);
        ChooseFps(*c, fpsHint);
        Log("camera %s: chars=%d facing=%d timestampSource=%d configs=%zu fpsRanges=%zu vendorSource=%d vendorPosition=%d", c->id.c_str(), static_cast<int>(c->charsResult),
            c->lensFacing, c->timestampSource, c->streamConfigs.size(), c->fpsRanges.size(), c->vendorSource.present, c->vendorPosition.present);
    }
    for (CameraEntry* c : entries) {
        bool stop; { std::lock_guard<std::mutex> lock(g_state.mutex); stop = g_state.stopRequested; }
        if (stop) break;
        OpenCamera(mgr, *c);
    }
    // Run until stop is requested.
    for (;;) {
        bool stop; { std::lock_guard<std::mutex> lock(g_state.mutex); stop = g_state.stopRequested; }
        if (stop) break;
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }
    for (CameraEntry* c : entries) CloseCamera(*c);
    if (ids) ACameraManager_deleteCameraIdList(ids);
    ACameraManager_delete(mgr);
    { std::lock_guard<std::mutex> lock(g_state.mutex); g_state.running = false; g_state.stopNs = MonotonicNs(); }
    LogJson("camera-probe", CameraProbeJson());
}

void WriteVendor(JsonWriter& w, const char* key, const VendorTagValue& v) {
    w.Key(key); w.BeginObject();
    w.KV("present", v.present); w.KV("status", static_cast<int32_t>(v.status));
    if (v.present) { w.KV("type", static_cast<uint32_t>(v.type)); w.KV("count", v.count); w.Key("values"); w.BeginArray(); for (double x : v.values) w.Double(x, 9); w.EndArray(); if (!v.text.empty()) w.KV("text", v.text); }
    w.EndObject();
}

} // namespace

int32_t CameraProbeStart(int32_t widthHint, int32_t heightHint, int32_t fpsHint) {
    std::lock_guard<std::mutex> lock(g_state.mutex);
    if (g_state.running) return FS_ERR_BUSY;
    if (g_state.thread.joinable()) g_state.thread.join();
    g_state.cameras.clear();
    g_state.managerNote.clear();
    g_state.running = true; g_state.started = true; g_state.stopRequested = false;
    g_state.widthHint = widthHint > 0 ? widthHint : 1280; g_state.heightHint = heightHint > 0 ? heightHint : 960; g_state.fpsHint = fpsHint > 0 ? fpsHint : 30;
    g_state.startNs = MonotonicNs(); g_state.stopNs = 0;
    g_state.thread = std::thread(ProbeThread, g_state.widthHint, g_state.heightHint, g_state.fpsHint);
    Log("camera probe start: hint=%dx%d@%d", g_state.widthHint, g_state.heightHint, g_state.fpsHint);
    return FS_OK;
}

int32_t CameraProbeStop() {
    std::thread t;
    {
        std::lock_guard<std::mutex> lock(g_state.mutex);
        if (!g_state.started) return FS_ERR_INVALID;
        g_state.stopRequested = true;
        t.swap(g_state.thread);
    }
    if (t.joinable()) t.join();
    std::lock_guard<std::mutex> lock(g_state.mutex);
    if (g_state.latestAhb) { AHardwareBuffer_release(g_state.latestAhb); g_state.latestAhb = nullptr; }
    return FS_OK;
}

AHardwareBuffer* CameraProbeAcquireLatestAhb(std::string* cameraId) {
    std::lock_guard<std::mutex> lock(g_state.mutex);
    if (g_state.latestAhb == nullptr) return nullptr;
    AHardwareBuffer_acquire(g_state.latestAhb);
    if (cameraId) *cameraId = g_state.latestAhbCamera;
    return g_state.latestAhb;
}

std::string CameraProbeJson() {
    std::lock_guard<std::mutex> lock(g_state.mutex);
    std::lock_guard<std::mutex> flock(g_frameMutex);
    JsonWriter w;
    w.BeginObject();
    w.KV("started", g_state.started); w.KV("running", g_state.running);
    w.KV("idListResult", static_cast<int32_t>(g_state.idListResult)); w.KV("note", g_state.managerNote);
    w.KV("hintWidth", g_state.widthHint); w.KV("hintHeight", g_state.heightHint); w.KV("hintFps", g_state.fpsHint);
    w.KV("startCpuNs", g_state.startNs); w.KV("stopCpuNs", g_state.stopNs);
    w.KV("nowBoottimeNs", BoottimeNs()); w.KV("nowMonotonicNs", MonotonicNs());
    w.KV("cameraCount", static_cast<uint64_t>(g_state.cameras.size()));
    w.Key("cameras"); w.BeginArray();
    for (const auto& cp : g_state.cameras) {
        const CameraEntry& c = *cp;
        w.BeginObject();
        w.KV("id", c.id);
        w.Key("characteristics"); w.BeginObject();
        w.KV("result", static_cast<int32_t>(c.charsResult));
        w.KV("lensFacing", c.lensFacing); w.KV("timestampSource", c.timestampSource);
        w.KV("timestampSourceName", c.timestampSource == 1 ? "REALTIME" : c.timestampSource == 0 ? "UNKNOWN" : "n/a");
        w.KV("hardwareLevel", c.hardwareLevel); w.KV("orientation", c.orientation); w.KV("poseReference", c.poseReference);
        auto ia = [&](const char* k, const std::vector<int32_t>& v) { w.Key(k); w.BeginArray(); for (int32_t x : v) w.Int(x); w.EndArray(); };
        auto fa = [&](const char* k, const std::vector<float>& v) { w.Key(k); w.BeginArray(); for (float x : v) w.Double(x, 9); w.EndArray(); };
        ia("activeArraySize", c.activeArray); ia("pixelArraySize", c.pixelArray); fa("physicalSizeMm", c.physicalSize);
        fa("intrinsicCalibration", c.intrinsics); fa("distortion", c.distortion); fa("poseTranslation", c.poseTranslation); fa("poseRotation", c.poseRotation); fa("focalLengths", c.focalLengths);
        w.Key("exposureTimeRangeNs"); w.BeginArray(); for (int64_t x : c.exposureRange) w.Int(x); w.EndArray();
        ia("sensitivityRange", c.sensitivityRange); ia("capabilities", c.capabilities);
        w.Key("fpsRanges"); w.BeginArray(); for (const auto& r : c.fpsRanges) { w.BeginArray(); w.Int(r[0]); w.Int(r[1]); w.EndArray(); } w.EndArray();
        w.Key("streamConfigs"); w.BeginArray();
        for (const auto& s : c.streamConfigs) { w.BeginObject(); w.KVHex("format", static_cast<uint32_t>(s[0])); w.KV("width", s[1]); w.KV("height", s[2]); w.KV("input", s[3] != 0); w.EndObject(); }
        w.EndArray();
        w.Key("minFrameDurations"); w.BeginArray();
        for (const auto& s : c.minFrameDurations) { if (s[0] != kFormatYuv420) continue; w.BeginObject(); w.KV("width", s[1]); w.KV("height", s[2]); w.KV("ns", s[3]); w.KV("fps", s[3] > 0 ? 1e9 / static_cast<double>(s[3]) : 0.0, 5); w.EndObject(); }
        w.EndArray();
        WriteVendor(w, "vendorCameraSource", c.vendorSource); WriteVendor(w, "vendorCameraPosition", c.vendorPosition);
        w.KV("tagCount", c.tagCount);
        w.Key("vendorTags"); w.BeginArray(); for (uint32_t t : c.vendorTags) w.Hex(t); w.EndArray();
        w.EndObject();
        w.Key("probe"); w.BeginObject();
        w.KV("openAttempted", c.openAttempted); w.KV("openResult", static_cast<int32_t>(c.openResult)); w.KV("opened", c.opened);
        w.KV("readerResult", static_cast<int32_t>(c.readerResult)); w.KV("sessionResult", static_cast<int32_t>(c.sessionResult));
        w.KV("requestResult", static_cast<int32_t>(c.requestResult)); w.KV("repeatResult", static_cast<int32_t>(c.repeatResult));
        w.KV("disconnected", c.disconnected.load() != 0); w.KV("deviceError", c.deviceError.load());
        w.KV("width", c.width); w.KV("height", c.height); w.KV("fpsMin", c.fpsMin); w.KV("fpsMax", c.fpsMax);
        const uint64_t frames = c.frames.load();
        w.KV("frames", frames); w.KV("captureCompleted", c.captureCompleted.load()); w.KV("captureFailed", c.captureFailed.load());
        w.KV("bufferLost", c.bufferLost.load()); w.KV("acquireErrors", c.acquireErrors.load());
        const double spanS = frames > 1 ? static_cast<double>(c.lastTs - c.firstTs) / 1e9 : 0.0;
        const double cpuSpanS = frames > 1 ? static_cast<double>(c.lastCpuNs - c.firstCpuNs) / 1e9 : 0.0;
        w.KV("fpsMeasured", spanS > 0 ? static_cast<double>(frames - 1) / spanS : 0.0, 5);
        w.KV("fpsMeasuredCpu", cpuSpanS > 0 ? static_cast<double>(frames - 1) / cpuSpanS : 0.0, 5);
        c.latencyBoot.Write(w, "latencyBoottimeMs"); c.latencyMono.Write(w, "latencyMonotonicMs"); c.interval.Write(w, "sensorIntervalMs");
        w.KV("timestampMonotonicViolations", c.violations.load());
        w.KV("ahbAvailable", frames > 0 && c.ahbOk.load() == frames); w.KV("ahbOkFrames", c.ahbOk.load()); w.KV("ahbFailFrames", c.ahbFail.load());
        w.KV("firstTimestampNs", c.firstTs); w.KV("lastTimestampNs", c.lastTs);
        w.KV("imageFormat", c.imageFormat); w.KV("imageWidth", c.imageWidth); w.KV("imageHeight", c.imageHeight); w.KV("planes", c.planes);
        w.Key("planeStrides"); w.BeginArray(); for (const auto& p : c.planeStrides) { w.BeginObject(); w.KV("rowStride", p[0]); w.KV("pixelStride", p[1]); w.EndObject(); } w.EndArray();
        w.KV("note", c.note);
        w.EndObject();
        w.EndObject();
    }
    w.EndArray();
    w.KV("latestAhbCamera", g_state.latestAhbCamera);
    w.EndObject();
    return w.Take();
}

} // namespace fs
