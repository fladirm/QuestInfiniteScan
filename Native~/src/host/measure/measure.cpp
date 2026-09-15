// FinalScan measurement front-end, C09 cut: Environment Depth (320x320x2, 25 Hz, own per-eye pose / fov /
// near / far / XrTime) -> anchor-local FsSurfaceMeasurement records written by the GPU into the device
// measurement ring (fs_meas_gpu.h). No CPU readback, no CPU push: the world module's SCAN tick integrates the
// ring slot directly (README.md "Integration point").
//
// Threads: FsMeas_* exports run on Unity's main thread; the frame-begin hook on the render thread (texture
// import + descriptor update); the SCAN-class scheduler tick and every onRetired callback on the fs-sched
// thread. One mutex guards the module state; nothing here blocks on the GPU.
//
// Data flow per depth frame:
//   C# EnvDepthFeeder -> FsMeas_SetEnvDepth(tex, poses, fovs, near, far, XrTime)     [main thread, latest only]
//   FS_HEVT_FRAME_BEGIN hook: ImportUnityTexture(tex) + BindImage (only while no backproject job reads the
//        previous image)                                                              [render thread]
//   SCAN tick: pick a FREE (or unleased READY) ring slot, reset its counters + write the per-eye and prediction
//        parameters into its frame block, BindBuffer records/frame block, submit ONE job with FIVE dispatches
//        (C09R-E2, contract §7.6): score (both layers, 3200 workgroups of 64) -> select (1 workgroup: histogram
//        threshold) -> count (per workgroup) -> prefix (1 workgroup) -> emit (selected texels, deterministic order)  [fs-sched]
//   onRetired: slot -> READY, counters -> FS_CTR_MEASUREMENTS / _DROPPED, FsScan_RequestTick(obsId)
//   world SCAN tick: MeasGpu_PeekFrame -> integrate jobs -> MeasGpu_ReleaseFrame
#include "fs_meas_math.h"
#include "fs_meas_gpu.h"
#include "fs_meas_api.h"
#include "../fs_executor.h"
#include "../world/fs_world.h"
#include "../../log.h"
#include "../../../include/finalscan_native_api.h"
#include "../render/fs_render.h"
#include "../world/fs_world_params.h"
#include "measure_score_spirv.inc"
#include "measure_select_spirv.inc"
#include "measure_count_spirv.inc"
#include "measure_prefix_spirv.inc"
#include "measure_emit_spirv.inc"
#include "measure_texture_spirv.inc"
#include "measure_planar_spirv.inc"
#include "measure_stereo_spirv.inc"
#include "measure_temporal_spirv.inc"
#include "measure_targets_spirv.inc"
#include "../fs_cost_model.h"
#include <algorithm>
#include <cstdlib>
#include <chrono>
#include <atomic>
#include <math.h>
#include <mutex>
#include <string.h>

#define FS_API extern "C" __attribute__((visibility("default")))

namespace fs {
namespace meas {
namespace {

constexpr uint32_t kLogEveryFrames = 250;        // ~10 s at 25 Hz
constexpr uint32_t kMaxThreads = FS_MEAS_DEPTH_W * FS_MEAS_DEPTH_H * FS_MEAS_DEPTH_LAYERS;   // score scratch (larger images are refused, logged)
enum MeasKernel : uint32_t { K_SCORE = 0, K_SELECT, K_COUNT, K_PREFIX, K_EMIT, K_TEXTURE, K_PLANAR, K_STEREO, K_TEMPORAL, K_TARGETS, K_COUNT_ };
struct PushRefine { uint32_t offset, count, recordCap, pad; };   // twin: FS_MEAS_REFINE_PUSH
struct KernelSpec { const char* name; const uint32_t* spirv; size_t words; uint32_t bindings[12]; uint32_t bindingCount; uint32_t samplers; uint32_t pushBytes; };
#define FS_KS(sym) sym, sizeof(sym) / 4
static const KernelSpec kKernels[K_COUNT_] = {     // sampler bindings come LAST in each list
    {"measure_score",    FS_KS(kMeasureScoreSpirv),    {FS_MEAS_B_COUNTERS, FS_MEAS_B_SCORE, FS_MEAS_B_SELECT, FS_MEAS_B_DEPTH, FS_MEAS_B_PRED}, 5, 2, sizeof(PushCompact)},
    {"measure_select",   FS_KS(kMeasureSelectSpirv),   {FS_MEAS_B_COUNTERS, FS_MEAS_B_SELECT}, 2, 0, sizeof(PushCompact)},
    {"measure_count",    FS_KS(kMeasureCountSpirv),    {FS_MEAS_B_COUNTERS, FS_MEAS_B_SCORE, FS_MEAS_B_SELECT}, 3, 0, sizeof(PushCompact)},
    {"measure_prefix",   FS_KS(kMeasurePrefixSpirv),   {FS_MEAS_B_COUNTERS, FS_MEAS_B_SELECT}, 2, 0, sizeof(PushCompact)},
    {"measure_emit",     FS_KS(kMeasureEmitSpirv),     {FS_MEAS_B_RECORDS, FS_MEAS_B_COUNTERS, FS_MEAS_B_SCORE, FS_MEAS_B_SELECT, FS_MEAS_B_TILES, FS_MEAS_B_DEPTH, FS_MEAS_B_CAM_L, FS_MEAS_B_CAM_R, FS_MEAS_B_CAM_K}, 9, 4, sizeof(PushCompact)},
    {"measure_texture",  FS_KS(kMeasureTextureSpirv),  {FS_MEAS_B_COUNTERS, FS_MEAS_B_TILES, FS_MEAS_B_CAM_L, FS_MEAS_B_CAM_R, FS_MEAS_B_CAM_K}, 5, 3, sizeof(PushCompact)},
    {"measure_planar",   FS_KS(kMeasurePlanarSpirv),   {FS_MEAS_B_COUNTERS, FS_MEAS_B_TILES, FS_MEAS_B_DEPTH}, 3, 1, sizeof(PushCompact)},
    {"measure_stereo",   FS_KS(kMeasureStereoSpirv),   {FS_MEAS_B_RECORDS, FS_MEAS_B_COUNTERS, FS_MEAS_B_CAM_L, FS_MEAS_B_CAM_R, FS_MEAS_B_CAM_K}, 5, 3, sizeof(PushRefine)},
    {"measure_temporal", FS_KS(kMeasureTemporalSpirv), {FS_MEAS_B_RECORDS, FS_MEAS_B_COUNTERS, FS_MEAS_B_CAM_L, FS_MEAS_B_CAM_R, FS_MEAS_B_CAM_K}, 5, 3, sizeof(PushRefine)},
    {"measure_targets",  FS_KS(kMeasureTargetsSpirv),  {FS_MEAS_B_RECORDS, FS_MEAS_B_COUNTERS, FS_MEAS_B_TILES, FS_MEAS_B_TARGETS, FS_MEAS_B_CAM_L, FS_MEAS_B_CAM_R, FS_MEAS_B_CAM_K}, 7, 3, sizeof(PushCompact)},
};
#undef FS_KS
constexpr int32_t  kAnchorId = 0;                // one anchor until the AnchorGraph cut (C19) assigns observations to anchors

struct StereoPairInput { uint32_t observationId = 0; int64_t xrTimeNsL = 0, xrTimeNsR = 0; uint32_t skewClass = 3; bool geometryEligible = false; float confidence = 0, blurPenalty = 1; int64_t uncertaintyNs = 0; uint64_t seq = 0; };
struct CameraInput { void* texture = nullptr; uint32_t width = 0, height = 0; float worldFromCamera[16]; float k[4]; int32_t rowFlip = 0; int64_t xrTimeNs = 0; uint64_t seq = 0; };
struct EnvDepthInput {
    void* texture = nullptr; uint32_t width = 0, height = 0;
    float poseL[16], poseR[16], fovL[4], fovR[4];
    float nearZ = 0.1f, farZ = 0.f; int64_t xrTimeNs = 0;
    uint64_t seq = 0;                            // bumped per NEW depth frame
};

enum SlotState : uint32_t { SLOT_FREE = 0, SLOT_IN_FLIGHT = 1, SLOT_READY = 2, SLOT_LEASED = 3, SLOT_REFINING = 4 };
struct RingSlot { MeasGpuRing ring; FrameBlock* blk = nullptr; SlotState state = SLOT_FREE; MeasGpuFrame frame; uint32_t refineOffset = 0, candidates = 0; int64_t refineUs = 0; uint64_t seq = 0; uint32_t maxOut = 0, targetBase = 0; };

class Measure {
public:
    void Init() {
        std::lock_guard<std::mutex> g(m_);
        if (inited_) return;
        inited_ = true;
        memset(&input_, 0, sizeof input_);
        RegisterDeviceHook([this](bool up) { OnDevice(up); });
        RegisterWarmupStep("measure.pipelines", [this]() { return CreatePipeline(); });
        RegisterFrameBeginHook([this](VkCommandBuffer, uint32_t frame) { FrameBegin(frame); });
        RegisterClassSchedulerTick(FS_JOB_SCAN, [this](uint32_t budgetUs) { Tick(budgetUs); });
        Log("FS-MEAS init: ring %u x %u records, budget=%u maxOut=%u flags=0x%x", (unsigned)FS_MEAS_GPU_RING_SLOTS, (unsigned)FS_MEAS_GPU_RING_CAPACITY, budget_, maxOut_, flags_);
    }

    // ---- device lifecycle (render thread) ------------------------------------------------------------
    void OnDevice(bool up) {
        std::lock_guard<std::mutex> g(m_);
        if (up == deviceUp_) return;
        if (!up) { DestroyRings(); deviceUp_ = false; imported_ = 0; submitted_ = 0; pendingSubmit_ = false; view_ = VK_NULL_HANDLE; predView_ = VK_NULL_HANDLE; jobInFlight_ = false; for (Pipeline& p : pipes_) p = Pipeline{}; pipeReady_ = false; scratchBound_ = false; return; }   // the executor destroyed every pipeline at teardown
        if (!CreateRings()) { LogError("FS-MEAS ring creation failed; measurement front-end inactive"); DestroyRings(); return; }
        deviceUp_ = true;
        BindScratch();
    }
    // Score / select scratch: one job in flight at a time, so a single copy serves every ring slot.
    void BindScratch() {
        if (scratchBound_ || !deviceUp_ || !pipeReady_) return;
        for (uint32_t k = 0; k < K_COUNT_; ++k) {
            const KernelSpec& ks = kKernels[k];
            for (uint32_t i = 0; i < ks.bindingCount - ks.samplers; ++i) {
                const uint32_t b = ks.bindings[i];
                const Buffer* buf = b == FS_MEAS_B_SCORE ? &score_ : b == FS_MEAS_B_SELECT ? &select_ : b == FS_MEAS_B_TILES ? &tiles_ : b == FS_MEAS_B_TARGETS ? &targetsBuf_ : nullptr;
                if (buf && !BindBuffer(pipes_[k], b, *buf)) { LogError("FS-MEAS bind scratch %s:%u failed", ks.name, b); return; }
            }
        }
        scratchBound_ = true;
    }
    bool CreateRings() {
        const VkBufferUsageFlags use = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
        for (uint32_t s = 0; s < FS_MEAS_GPU_RING_SLOTS; ++s) {
            RingSlot& r = slots_[s];
            r.ring.capacity = FS_MEAS_GPU_RING_CAPACITY;
            char name[48]; snprintf(name, sizeof name, "meas.records[%u]", s);
            if (!CreateBuffer(r.ring.records, (VkDeviceSize)FS_MEAS_GPU_RING_CAPACITY * sizeof(FsSurfaceMeasurement), use, true, name)) return false;
            snprintf(name, sizeof name, "meas.frameBlock[%u]", s);
            if (!CreateBuffer(r.ring.counters, (VkDeviceSize)sizeof(FrameBlock), use, true, name)) return false;
            if (!r.ring.records.mapped || !r.ring.counters.mapped) { LogError("FS-MEAS host-visible ring not mapped"); return false; }
            r.blk = (FrameBlock*)r.ring.counters.mapped; memset(r.blk, 0, sizeof(FrameBlock));
            r.state = SLOT_FREE; r.frame = MeasGpuFrame{}; r.frame.ring = &r.ring; r.frame.slot = s;
        }
        if (!CreateBuffer(score_, (VkDeviceSize)kMaxThreads * 4, use, false, "meas.score")) return false;
        if (!CreateBuffer(select_, (VkDeviceSize)FS_MEAS_SEL_WORDS * 4, use, true, "meas.select") || !select_.mapped) return false;
        if (!CreateBuffer(tiles_, (VkDeviceSize)(3u * FS_TEX_TILES + 2u * FS_PLANAR_TILES * FS_PLANAR_TILE_WORDS) * 4, use, false, "meas.tiles")) return false;
        if (!CreateBuffer(targetsBuf_, (VkDeviceSize)FS_MEAS_TARGETS_MAX * 9 * 4, use, true, "meas.refineTargets") || !targetsBuf_.mapped) return false;
        memset(select_.mapped, 0, FS_MEAS_SEL_WORDS * 4);
        Log("FS-MEAS rings: %u x %u records (%.1f MB)", (unsigned)FS_MEAS_GPU_RING_SLOTS, (unsigned)FS_MEAS_GPU_RING_CAPACITY, FS_MEAS_GPU_RING_SLOTS * FS_MEAS_GPU_RING_CAPACITY * 48.0 / 1e6);
        return true;
    }
    void DestroyRings() {
        for (RingSlot& r : slots_) {
            if (r.ring.records.buffer != VK_NULL_HANDLE) DestroyBuffer(r.ring.records);
            if (r.ring.counters.buffer != VK_NULL_HANDLE) DestroyBuffer(r.ring.counters);
            r.blk = nullptr; r.state = SLOT_FREE; r.ring.capacity = 0;
        }
        if (score_.buffer != VK_NULL_HANDLE) DestroyBuffer(score_);
        if (select_.buffer != VK_NULL_HANDLE) DestroyBuffer(select_);
        if (tiles_.buffer != VK_NULL_HANDLE) DestroyBuffer(tiles_);
        if (targetsBuf_.buffer != VK_NULL_HANDLE) DestroyBuffer(targetsBuf_);
        scratchBound_ = false;
    }
    bool CreatePipeline() {                        // warm-up step (fs-warmup thread)
        std::lock_guard<std::mutex> g(m_);
        if (pipeReady_) return true;
        for (uint32_t k = 0; k < K_COUNT_; ++k) {
            const KernelSpec& ks = kKernels[k];
            VkDescriptorSetLayoutBinding b[12] = {};
            for (uint32_t i = 0; i < ks.bindingCount; ++i) {
                b[i].binding = ks.bindings[i]; b[i].descriptorCount = 1; b[i].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
                b[i].descriptorType = i >= ks.bindingCount - ks.samplers ? VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER : VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
            }
            if (!CreateComputePipeline(pipes_[k], ks.spirv, ks.words, ks.pushBytes, b, ks.bindingCount, ks.name)) { LogError("FS-MEAS pipeline %s failed", ks.name); return false; }
        }
        pipeReady_ = true;
        BindScratch();
        return true;
    }

    // ---- exports (main thread) -----------------------------------------------------------------------
    int32_t SetEnvDepth(void* tex, uint32_t w, uint32_t h, const float poseL[16], const float poseR[16], const float fovL[4], const float fovR[4], float nearZ, float farZ, int64_t xrTimeNs) {
        if (!tex || !poseL || !poseR || !fovL || !fovR || w == 0 || h == 0) return FS_ERR_INVALID;
        std::lock_guard<std::mutex> g(m_);
        if (input_.seq != 0 && xrTimeNs == input_.xrTimeNs && tex == input_.texture) return FS_OK;   // repeat of the same frame (72 Hz callback)
        input_.texture = tex; input_.width = w; input_.height = h;
        memcpy(input_.poseL, poseL, 64); memcpy(input_.poseR, poseR, 64); memcpy(input_.fovL, fovL, 16); memcpy(input_.fovR, fovR, 16);
        input_.nearZ = nearZ > 0.f ? nearZ : 0.1f; input_.farZ = farZ; input_.xrTimeNs = xrTimeNs;
        input_.seq++; framesSeen_++;
        return FS_OK;
    }
    int32_t SetCameraFrame(int32_t eye, void* tex, uint32_t w, uint32_t h, uint32_t sw, uint32_t sh, const float wfc[16], float fx, float fy, float cx, float cy, int32_t rowFlip, int64_t xrTimeNs) {
        if (eye < 0 || eye > 1) return FS_ERR_INVALID;
        std::lock_guard<std::mutex> g(m_);
        CameraInput& c = cam_[eye];
        if (!tex) { c.texture = nullptr; c.seq++; return FS_OK; }
        if (!wfc || w == 0 || h == 0 || fx <= 0.f || fy <= 0.f) return FS_ERR_INVALID;
        CounterAdd(eye == 0 ? FS_CTR_PCA_FRAMES_L : FS_CTR_PCA_FRAMES_R, 1);
        c.texture = tex; c.width = w; c.height = h; memcpy(c.worldFromCamera, wfc, 64); DeliveredIntrinsics(fx, fy, cx, cy, sw, sh, w, h, c.k); c.rowFlip = rowFlip ? 1 : 0; c.xrTimeNs = xrTimeNs; c.seq++;
        camFrames_++;
        return FS_OK;
    }
    // C10: the managed StereoPairer committed a coherent L/R pair by capture timestamps; the two camera slots set just before
    // carry its frames. The pair is used for a depth frame only when both slots still hold exactly these capture times.
    int32_t SetStereoPair(uint32_t observationId, int64_t tL, int64_t tR, uint32_t skewClass, int32_t geometryEligible, float confidence, float blurPenalty, int64_t uncertaintyNs) {
        std::lock_guard<std::mutex> g(m_);
        if (skewClass >= 3u) { CounterAdd(FS_CTR_PAIRS_REJECTED_SKEW, 1); return FS_ERR_INVALID; }   // SkewClass.Reject never reaches the solve
        pair_.observationId = observationId; pair_.xrTimeNsL = tL; pair_.xrTimeNsR = tR; pair_.skewClass = skewClass; pair_.seq++;
        // C10R motion authority from the managed pairer: fail-closed (a pair the pairer rules out for geometry gives colour only)
        pair_.geometryEligible = geometryEligible != 0; pair_.confidence = confidence; pair_.blurPenalty = blurPenalty; pair_.uncertaintyNs = uncertaintyNs;
        if (!PairGeometryEligible()) pairsGeometryRejected_++;
        CounterAdd(FS_CTR_STEREO_PAIRS, 1);
        return FS_OK;
    }
    // C11: a keyframe slot (an earlier left PCA frame copied by the managed store when the camera moved); tex == null clears it.
    int32_t SetKeyframe(uint32_t slotIndex, void* tex, uint32_t w, uint32_t h, uint32_t sw, uint32_t sh, const float wfc[16], float fx, float fy, float cx, float cy, int32_t rowFlip, int64_t xrTimeNs) {
        if (slotIndex >= FS_MEAS_KEYFRAMES) return FS_ERR_INVALID;
        std::lock_guard<std::mutex> g(m_);
        CameraInput& c = key_[slotIndex];
        if (!tex) { c.texture = nullptr; c.seq++; return FS_OK; }
        if (!wfc || w == 0 || h == 0 || fx <= 0.f || fy <= 0.f) return FS_ERR_INVALID;
        c.texture = tex; c.width = w; c.height = h; memcpy(c.worldFromCamera, wfc, 64); DeliveredIntrinsics(fx, fy, cx, cy, sw, sh, w, h, c.k); c.rowFlip = rowFlip ? 1 : 0; c.xrTimeNs = xrTimeNs; c.seq++;
        keyframesSet_++;
        return FS_OK;
    }
    // Keyframe for the current left PCA frame: baseline within [MIN, MAX], optical axes within MAX_ANGLE, age within MAX_AGE;
    // the baseline closest to BEST wins (twin: SelectKeyframe). -1 = none.
    int32_t SelectKeyframeLocked() const {
        const CameraInput& cur = cam_[0];
        if (!cur.texture) return -1;
        return SelectKeyframe(cur.worldFromCamera, cur.xrTimeNs, [&](uint32_t i, const float*& wfc, int64_t& t) { if (!key_[i].texture) return false; wfc = key_[i].worldFromCamera; t = key_[i].xrTimeNs; return true; });
    }
    bool PairGeometryEligible() const {
        return pair_.geometryEligible && pair_.confidence >= (float)FS_STEREO_MIN_CONFIDENCE && pair_.blurPenalty <= (float)FS_STEREO_MAX_BLUR && pair_.uncertaintyNs <= (int64_t)FS_STEREO_MAX_UNCERTAINTY_NS;
    }
    int32_t SetParams(uint32_t budget, uint32_t maxOut, uint32_t flags) {
        std::lock_guard<std::mutex> g(m_);
        maxOut_ = maxOut == 0 ? FS_MEAS_DEFAULT_MAX_OUT : (maxOut > FS_MEAS_GPU_RING_CAPACITY ? FS_MEAS_GPU_RING_CAPACITY : maxOut);
        budget_ = budget == 0 ? FS_MEAS_DEFAULT_BUDGET : (budget > maxOut_ ? maxOut_ : budget);
        if (budget_ > 32768u) budget_ = 32768u;                        // select kernel: (budget << 16) fits 32 bits
        flags_ = flags;
        Log("FS-MEAS params: budget=%u maxOut=%u flags=0x%x", budget_, maxOut_, flags_);
        return FS_OK;
    }
    // C10 receipts (totals since start): tested, valid, lowTex, ambiguous, bandEdge, noCover, n[4], res0.1mm[4], sigmaUm, envSigmaUm, frames with pair
    void StereoStats(int64_t out[17]) { std::lock_guard<std::mutex> g(m_); for (uint32_t k = 0; k < 16; ++k) out[k] = (int64_t)stereoTotals_[k]; out[16] = (int64_t)stereoFrames_; }
    // C11 receipts (totals): temporal tested, valid, lowTex, ambiguous, bandEdge, noCover, disagree, sigmaUm, planar tested, valid, rejected, sigmaUm, rmsUm, keyframes set, frames with keyframe
    // + candidates stereo, candidates temporal, refine skipped, refine jobs, pairs rejected for geometry, refine ms per frame p95, last compaction us
    // Cross-module mailbox: World::CollectTopology may publish targets without taking the main measurement mutex.
    // Tick consumes the latest complete target set at an observation boundary.
    void SetRefineTargets(const RefineTarget* t, uint32_t n) {
        std::lock_guard<std::mutex> g(targetMutex_);
        pendingRefineTargets_.assign(t, t + std::min<uint32_t>(n, FS_MEAS_TARGETS_MAX));
    }
    // C11R2 totals: considered, textureEligible, keyframeEligible, baselineRejected, visibilityRejected, written, solved, accepted, sigmaBeforeUm, sigmaAfterUm, infoGain x1000
    void TargetStats(int64_t out[11]) { std::lock_guard<std::mutex> g(m_); for (uint32_t k = 0; k < 11; ++k) out[k] = (int64_t)stereoTotals_[32 + k]; }
    void MultiviewStats(int64_t out[22]) { std::lock_guard<std::mutex> g(m_); for (uint32_t k = 0; k < 13; ++k) out[k] = (int64_t)stereoTotals_[16 + k]; out[13] = (int64_t)keyframesSet_; out[14] = (int64_t)keyFramesUsed_;
        out[15] = (int64_t)stereoTotals_[29]; out[16] = (int64_t)stereoTotals_[30]; out[17] = (int64_t)stereoTotals_[31]; out[18] = (int64_t)refineJobs_; out[19] = (int64_t)pairsGeometryRejected_; out[20] = (int64_t)(refineFrameUs_.Quantile(0.95f) * 1000.f); out[21] = lastGpuUs_; }
    void Stats(int64_t out[8]) {
        std::lock_guard<std::mutex> g(m_);
        out[0] = framesSeen_; out[1] = framesSubmitted_; out[2] = framesSuperseded_; out[3] = lastCount_; out[4] = lastRejected_; out[5] = lastGpuUs_; out[6] = importFailures_; out[7] = lastThreshold_;
    }

    // camera images (linear) to every kernel that samples them
    bool BindImageAll(uint32_t binding, VkImageView v) {
        for (uint32_t k = 0; k < K_COUNT_; ++k) {
            const KernelSpec& ks = kKernels[k];
            for (uint32_t i = ks.bindingCount - ks.samplers; i < ks.bindingCount; ++i)
                if (ks.bindings[i] == binding && !BindImage(pipes_[k], binding, v, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, ExecSampler(true))) return false;
        }
        return true;
    }
    // ---- frame-begin hook (render thread): import the latest depth texture, bind it once per image --------
    void FrameBegin(uint32_t frame) {
        std::lock_guard<std::mutex> g(m_);
        if (!deviceUp_ || !pipeReady_ || input_.seq == 0 || imported_ == input_.seq) return;
        if (jobInFlight_) return;                                        // a job still reads the bound view: retry next frame
        VkImage image; VkImageView view; VkFormat fmt; uint32_t w, h, layers;
        if (!ImportUnityTexture(input_.texture, image, view, fmt, w, h, layers)) { importFailures_++; return; }
        if (w * h * (layers >= 2 ? 2u : 1u) > kMaxThreads) { if (importFailures_++ == 0) LogError("FS-MEAS depth image %ux%ux%u exceeds the score scratch (%u texels)", w, h, layers, kMaxThreads); return; }
        if (view != view_ || fmt != fmt_) {
            if (!BindImage(pipes_[K_SCORE], FS_MEAS_B_DEPTH, view, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, ExecSampler(false)) ||
                !BindImage(pipes_[K_EMIT], FS_MEAS_B_DEPTH, view, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, ExecSampler(false)) ||
                !BindImage(pipes_[K_PLANAR], FS_MEAS_B_DEPTH, view, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, ExecSampler(false))) { importFailures_++; return; }
            view_ = view; fmt_ = fmt; ++binds_;
            if (binds_ <= 4 || (binds_ % 500) == 0) Log("FS-MEAS depth image bound #%llu: %ux%u layers=%u format=%d", (unsigned long long)binds_, w, h, layers, (int)fmt);
        }
        // Canonical prediction (§7.6): the previous rendered depth. Import every frame (Unity re-transitions it); the
        // descriptor of binding 3 must always be valid, so without a prediction the depth view stands in and predInfo.valid = 0.
        pred_ = fs::render::PredictionInfo{}; predValid_ = false;
        fs::render::PredictionInfo pi;
        if (fs::render::GetPrediction(pi) && pi.unityPtr) {
            VkImage pimg; VkImageView pview; VkFormat pfmt; uint32_t pw, ph, pl;
            if (ImportUnityTexture(pi.unityPtr, pimg, pview, pfmt, pw, ph, pl)) {
                if (pview != predView_) { if (!BindImage(pipes_[K_SCORE], FS_MEAS_B_PRED, pview, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, ExecSampler(false))) { importFailures_++; return; } predView_ = pview; ++predBinds_; }
                pred_ = pi; pred_.width = pw; pred_.height = ph; pred_.layers = pl; predValid_ = true;
            }
        }
        if (!predValid_ && predView_ != view) { if (BindImage(pipes_[K_SCORE], FS_MEAS_B_PRED, view, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, ExecSampler(false))) predView_ = view; }
        // PCA frames (C16a): import both eyes every frame (Unity re-transitions the copies); a missing frame binds the depth view.
        for (uint32_t e = 0; e < 2; ++e) {
            CameraInput& c = cam_[e]; camImported_[e] = false;
            VkImageView cv = view;
            if (c.texture) {
                VkImage ci; VkImageView cvv; VkFormat cf; uint32_t cw, ch, cl;
                if (ImportUnityTexture(c.texture, ci, cvv, cf, cw, ch, cl)) { cv = cvv; camImported_[e] = true; camImportedW_[e] = cw; camImportedH_[e] = ch; }
            }
            if (cv != camView_[e]) { if (!BindImageAll(e == 0 ? FS_MEAS_B_CAM_L : FS_MEAS_B_CAM_R, cv)) { importFailures_++; return; } camView_[e] = cv; }
        }
        // C11 keyframe: select against the current left PCA frame, import, bind (none: the left camera / depth view stands in)
        {
            keySel_ = SelectKeyframeLocked(); keyImported_ = false;
            VkImageView kv = camView_[0] ? camView_[0] : view;
            if (keySel_ >= 0) {
                CameraInput& kc = key_[keySel_];
                VkImage ki; VkImageView kvv; VkFormat kf; uint32_t kw, kh, kl;
                if (ImportUnityTexture(kc.texture, ki, kvv, kf, kw, kh, kl) && kw == kc.width && kh == kc.height) { kv = kvv; keyImported_ = true; keySelSeq_ = kc.seq; }
            }
            if (kv != keyView_) { if (!BindImageAll(FS_MEAS_B_CAM_K, kv)) { importFailures_++; return; } keyView_ = kv; }
        }
        importedW_ = w; importedH_ = h; importedLayers_ = layers; imported_ = input_.seq; importedFrame_ = frame;
        pendingSubmit_ = true;
    }

    // ---- SCAN-class scheduler tick (fs-sched thread) -----------------------------------------------------
    void Tick(uint32_t budgetUs) {
        if (budgetUs == 0) return;

        // LOCK-ORDER CLOSURE:
        // Measure::m_ must never be held while entering World. World legitimately samples the measurement ring
        // while holding World::m_; the reverse edge caused the device ABBA freeze at t=153.5 s.
        const bool yieldToFusion = fs::world::FuseBackpressure();                 // lock-free atomic
        float anchorSnapshot[FS_MAX_ANCHORS * 16] = {};
        if (!yieldToFusion) fs::world::CopyAnchors(anchorSnapshot);               // World lock, but Measure::m_ is NOT held

        std::lock_guard<std::mutex> g(m_);
        if (!deviceUp_ || !pipeReady_ || jobInFlight_) return;
        // E6R backpressure: fusion could not get SCAN budget -> this frame's budget is left to it (a newer depth frame just waits / supersedes)
        if (yieldToFusion) { yieldedToFusion_++; return; }
        // C10R: a frame whose cheap pass admitted stereo / temporal candidates is refined (bounded chunks) before a new frame is compacted
        for (uint32_t s = 0; s < FS_MEAS_GPU_RING_SLOTS; ++s) if (slots_[s].state == SLOT_REFINING) { SubmitRefineLocked(s); return; }
        if (!pendingSubmit_) return;
        if (imported_ == submitted_) { pendingSubmit_ = false; return; }
        // choose a slot: FREE first, else the oldest unleased READY (superseded, counted); never LEASED / IN_FLIGHT
        int32_t pick = -1;
        for (uint32_t s = 0; s < FS_MEAS_GPU_RING_SLOTS; ++s) if (slots_[s].state == SLOT_FREE) { pick = (int32_t)s; break; }
        if (pick < 0) {
            uint64_t oldest = ~0ull;
            for (uint32_t s = 0; s < FS_MEAS_GPU_RING_SLOTS; ++s) if (slots_[s].state == SLOT_READY && slots_[s].frame.sequence < oldest) { oldest = slots_[s].frame.sequence; pick = (int32_t)s; }
            if (pick < 0) return;                                        // consumer holds every slot: keep the frame pending
            RingSlot& v = slots_[pick];
            CounterAdd(FS_CTR_MEASUREMENTS_DROPPED, (int64_t)v.frame.count); CounterAdd(FS_CTR_SCAN_TICK_SKIPPED, 1);
            framesSuperseded_++; v.state = SLOT_FREE;
        }
        RingSlot& slot = slots_[pick];
        // anchor-local transform: anchorFromWorld * worldFromEye (anchor 0), from the lock-order-safe snapshot above.
        Mat4 worldFromAnchor = Identity();
        memcpy(worldFromAnchor.m, anchorSnapshot + kAnchorId * 16, 64);
        Mat4 anchorFromWorld; if (!Invert(worldFromAnchor, anchorFromWorld)) anchorFromWorld = Identity();
        const uint32_t w = importedW_ ? importedW_ : input_.width, h = importedH_ ? importedH_ : input_.height;
        const uint32_t layers = importedLayers_ >= 2 ? 2u : 1u;
        const uint32_t obsId = (uint32_t)(++observationSeq_ & 0x7FFFFFFFull);   // identity = monotonic sequence (time stays XrTime), wraps at 2^31
        // frame block (host-coherent, written before submit): counters reset + per-eye anchorFromEye / fov
        FrameBlock& blk = *slot.blk;
        memset(blk.ctr, 0, sizeof blk.ctr);
        for (uint32_t eye = 0; eye < 2; ++eye) {
            Mat4 worldFromEye; memcpy(worldFromEye.m, eye == 0 ? input_.poseL : input_.poseR, 64);
            const Mat4 anchorFromEye = Mul(anchorFromWorld, worldFromEye);
            memcpy(blk.anchorFromEye[eye], anchorFromEye.m, 64);
            memcpy(blk.fov[eye], eye == 0 ? input_.fovL : input_.fovR, 16);
            memcpy(blk.worldFromEye[eye], worldFromEye.m, 64);
            memcpy(blk.predViewProj[eye], pred_.viewProj[eye], 64);
            memcpy(blk.predInvViewProj[eye], pred_.invViewProj[eye], 64);
        }
        blk.predInfo[0] = pred_.width; blk.predInfo[1] = pred_.height; blk.predInfo[2] = pred_.layers; blk.predInfo[3] = predValid_ && pred_.width && pred_.height ? 1u : 0u;
        // cameras (C16a): a frame is usable for this depth frame when its capture time is within FS_MEAS_CAM_MAX_AGE_NS
        uint32_t camMask = 0; blk.camInfo[0] = blk.camInfo[1] = 0;
        for (uint32_t e = 0; e < 2; ++e) {
            CameraInput& c = cam_[e];
            Mat4 wfc; memcpy(wfc.m, c.worldFromCamera, 64); Mat4 cfw; if (!Invert(wfc, cfw)) cfw = Identity();
            memcpy(blk.camFromWorld[e], cfw.m, 64); memcpy(blk.camIntrinsics[e], c.k, 16);
            const int64_t age = c.xrTimeNs > input_.xrTimeNs ? c.xrTimeNs - input_.xrTimeNs : input_.xrTimeNs - c.xrTimeNs;
            if (camImported_[e] && c.texture && age <= FS_MEAS_CAM_MAX_AGE_NS && camImportedW_[e] == c.width && camImportedH_[e] == c.height) { camMask |= 1u << e; blk.camInfo[0] = c.width; blk.camInfo[1] = c.height; }
        }
        // C10 stereo pair: both slots usable, identical delivered size / row convention, and exactly the pair's capture times
        const int64_t pairAge = std::max(std::llabs(pair_.xrTimeNsL - input_.xrTimeNs), std::llabs(pair_.xrTimeNsR - input_.xrTimeNs));
        if (camMask == 3u && pair_.seq && PairGeometryEligible() && cam_[0].xrTimeNs == pair_.xrTimeNsL && cam_[1].xrTimeNs == pair_.xrTimeNsR && pairAge <= FS_STEREO_MAX_AGE_NS &&
            cam_[0].width == cam_[1].width && cam_[0].height == cam_[1].height && cam_[0].rowFlip == cam_[1].rowFlip) { camMask |= 4u; stereoFrames_++; }
        blk.camInfo[2] = camMask; blk.camInfo[3] = (uint32_t)cam_[(camMask & 1u) ? 0 : 1].rowFlip; if (camMask) camFramesUsed_++;
        memcpy(blk.anchorFromWorld, anchorFromWorld.m, 64); memcpy(blk.worldFromAnchor, worldFromAnchor.m, 64);
        blk.stereoInfo[0] = pair_.observationId; blk.stereoInfo[1] = pair_.skewClass; blk.stereoInfo[2] = 0; blk.stereoInfo[3] = 0;
        // C11 keyframe (selected + imported at frame begin; still the same slot content)
        memset(blk.keyCamFromWorld, 0, sizeof blk.keyCamFromWorld); memset(blk.keyIntrinsics, 0, sizeof blk.keyIntrinsics); memset(blk.keyInfo, 0, sizeof blk.keyInfo);
        if ((camMask & 1u) && keyImported_ && keySel_ >= 0 && key_[keySel_].seq == keySelSeq_) {
            const CameraInput& kc = key_[keySel_];
            Mat4 kw; memcpy(kw.m, kc.worldFromCamera, 64); Mat4 kcfw; if (!Invert(kw, kcfw)) kcfw = Identity();
            memcpy(blk.keyCamFromWorld, kcfw.m, 64); memcpy(blk.keyIntrinsics, kc.k, 16);
            blk.keyInfo[0] = kc.width; blk.keyInfo[1] = kc.height; blk.keyInfo[2] = 1; blk.keyInfo[3] = (uint32_t)kc.rowFlip;
            keyFramesUsed_++;
        }
        memset(select_.mapped, 0, FS_MEAS_SCORE_BINS * 4);            // histogram reset (host-coherent, before submit)
        if (!scratchBound_) BindScratch();
        if (!scratchBound_) return;
        static PushCompact p;                                         // referenced by the dispatches until the executor recorded them (SubmitJob records synchronously)
        p.nearZ = input_.nearZ; p.farZ = input_.farZ; p.maxDepthM = (float)FS_MEAS_MAX_DEPTH_M; p.minDepthM = std::max(input_.nearZ, (float)FS_MEAS_MIN_DEPTH_M);
        p.layers = layers; p.budget = budget_; p.maxOut = maxOut_; p.flags = flags_;
        p.obsId = obsId; p.frame = (uint32_t)input_.seq; p.width = w; p.height = h;
        const uint32_t groups = (w * h * layers + FS_MEAS_WG - 1) / FS_MEAS_WG;
        // compaction job (information-first, cheap): texture tiles -> planar tiles -> score -> select -> count -> prefix -> emit (+ gates)
        // C11R2: atomically take the latest topology target mailbox at this observation boundary, then consume it once.
        {
            {
                std::lock_guard<std::mutex> tg(targetMutex_);
                if (!pendingRefineTargets_.empty()) refineTargets_.swap(pendingRefineTargets_);
            }
            const uint32_t nT = std::min<uint32_t>((uint32_t)refineTargets_.size(), FS_MEAS_TARGETS_MAX);
            uint32_t* tw = (uint32_t*)targetsBuf_.mapped;
            for (uint32_t k = 0; k < nT; ++k) { const RefineTarget& t = refineTargets_[k]; memcpy(tw + k * 9, t.pos, 12); memcpy(tw + k * 9 + 3, t.normal, 12); memcpy(tw + k * 9 + 6, &t.sigmaN, 4); tw[k * 9 + 7] = t.surfaceId; tw[k * 9 + 8] = t.support; }
            blk.ctr[FS_MEAS_CTR_TGT_CONSIDERED] = nT; refineTargets_.clear();
        }
        static const uint32_t kOrder[8] = {K_TEXTURE, K_PLANAR, K_SCORE, K_SELECT, K_COUNT, K_PREFIX, K_EMIT, K_TARGETS};
        Dispatch d[8];
        for (uint32_t i = 0; i < 8; ++i) {
            const uint32_t k = kOrder[i];
            d[i].pipeline = &pipes_[k]; d[i].push = &p; d[i].pushBytes = sizeof p;
            d[i].gx = (k == K_SELECT || k == K_PREFIX || k == K_TARGETS) ? 1u : k == K_TEXTURE ? (3u * FS_TEX_TILES + 63u) / 64u : k == K_PLANAR ? (2u * FS_PLANAR_TILES + 63u) / 64u : groups;
        }
        // persistent descriptors: no job of these pipelines is in flight, so the ring slot may be re-bound now
        for (uint32_t k = 0; k < K_COUNT_; ++k) {
            if (!BindBuffer(pipes_[k], FS_MEAS_B_COUNTERS, slot.ring.counters)) { LogError("FS-MEAS bind ring slot %d failed", pick); return; }
        }
        if (!BindBuffer(pipes_[K_TARGETS], FS_MEAS_B_RECORDS, slot.ring.records) || !BindBuffer(pipes_[K_EMIT], FS_MEAS_B_RECORDS, slot.ring.records)) { LogError("FS-MEAS bind ring slot %d records failed", pick); return; }
        slot.frame = MeasGpuFrame{}; slot.frame.ring = &slot.ring; slot.frame.slot = (uint32_t)pick;
        slot.frame.observationId = obsId; slot.frame.anchorId = kAnchorId; slot.frame.xrTimeNs = input_.xrTimeNs; slot.frame.frameIndex = FrameIndex();
        slot.frame.importFrameEnd = importedFrame_;
        for (uint32_t eye = 0; eye < 2; ++eye) {
            const float* pose = eye == 0 ? input_.poseL : input_.poseR;
            Vec3 o = MulPoint(anchorFromWorld, V3(pose[12], pose[13], pose[14]));
            slot.frame.eyeOrigin[eye][0] = o.x; slot.frame.eyeOrigin[eye][1] = o.y; slot.frame.eyeOrigin[eye][2] = o.z;
        }
        slot.frame.eyeOriginValid = true;
        slot.frame.sequence = ++handoffSeq_;
        const uint64_t seq = input_.seq; const uint32_t maxOut = maxOut_;
        JobDesc jd; jd.cls = FS_JOB_SCAN; jd.name = "depth_compact"; jd.dispatches = d; jd.dispatchCount = 8;
        jd.waitFrameEndValue = importedFrame_;   // Unity's layout transition of the imported depth image precedes this job (§15.3)
        jd.onRetired = [this, pick, seq, maxOut](bool ok, uint64_t gpuStart, uint64_t gpuEnd) { OnRetired((uint32_t)pick, seq, maxOut, ok, gpuStart, gpuEnd); };
        if (!SubmitJob(jd)) return;                                      // deferred (ring / budget): retried next tick
        slot.state = SLOT_IN_FLIGHT; jobInFlight_ = true; submitted_ = imported_; pendingSubmit_ = false; framesSubmitted_++;
    }
    void OnRetired(uint32_t pick, uint64_t seq, uint32_t maxOut, bool ok, uint64_t gpuStart, uint64_t gpuEnd) {
        uint32_t obs = 0; bool request = false;
        {
            std::lock_guard<std::mutex> g(m_);
            jobInFlight_ = false;
            RingSlot& slot = slots_[pick];
            if (!ok || !slot.blk) { slot.state = SLOT_FREE; return; }
            const uint32_t* ctr = slot.blk->ctr;
            const uint32_t reserved = ctr[FS_MEAS_CTR_RESERVED];
            slot.frame.count = StoredCount(reserved, maxOut);
            slot.targetBase = slot.frame.count; slot.frame.count += std::min<uint32_t>(ctr[FS_MEAS_CTR_TGT_WRITTEN], FS_MEAS_GPU_RING_CAPACITY - slot.frame.count);   // C11R2 target candidates follow the records
            slot.frame.overflow = ctr[FS_MEAS_CTR_OVERFLOW];
            slot.frame.gpuStartNs = gpuStart; slot.frame.gpuEndNs = gpuEnd;
            lastCount_ = slot.frame.count; lastOverflow_ = slot.frame.overflow; lastEdge_ = ctr[FS_MEAS_CTR_EDGE]; lastRejected_ = ctr[FS_MEAS_CTR_REJECTED]; lastThreshold_ = ctr[FS_MEAS_CTR_THRESHOLD];
            lastGpuUs_ = gpuEnd > gpuStart ? (int64_t)((gpuEnd - gpuStart) / 1000ull) : 0;
            lastGpuUs_ = gpuEnd > gpuStart ? (int64_t)((gpuEnd - gpuStart) / 1000ull) : 0;
            slot.seq = seq; slot.maxOut = maxOut;
            const uint32_t cand = ctr[FS_MEAS_CTR_CAND_STEREO] + ctr[FS_MEAS_CTR_CAND_TEMPORAL];
            if (slot.frame.count && cand) { slot.state = SLOT_REFINING; slot.refineOffset = 0; slot.refineUs = 0; slot.candidates = cand; return; }   // refine jobs follow
            request = FinalizeLocked(pick, obs);
        }
        // Outside the module lock (takes the executor lock): the world SCAN tick runs next with this observation.
        if (request) FsScan_RequestTick(obs);
    }
    // Frame complete (compaction + refine): READY for the world, totals, receipts.
    bool FinalizeLocked(uint32_t pick, uint32_t& obs) {
        RingSlot& slot = slots_[pick];
        const uint32_t* ctr = slot.blk->ctr; const uint64_t seq = slot.seq;
        bool request = false;
        // C11R2: only target records the temporal solve accepted remain measurements (a refused target is not evidence)
        if (slot.frame.count > slot.targetBase) {
            FsSurfaceMeasurement* rec = (FsSurfaceMeasurement*)slot.ring.records.mapped;
            uint32_t kept = slot.targetBase;
            for (uint32_t i = slot.targetBase; i < slot.frame.count; ++i) if ((rec[i].sourceFlags & FS_MEAS_SRC_TEMPORAL) && (rec[i].sourceFlags & FS_MEAS_SRC_TARGET)) rec[kept++] = rec[i];
            slot.frame.count = kept;
        }
            CounterAdd(FS_CTR_MEASUREMENTS, (int64_t)slot.frame.count);
            for (uint32_t k = 0; k < 48; ++k) stereoTotals_[k] += ctr[FS_MEAS_CTR_STEREO_TESTED + k];
            if (slot.frame.overflow) CounterAdd(FS_CTR_MEASUREMENTS_DROPPED, (int64_t)slot.frame.overflow);
            if (slot.frame.count) { slot.state = SLOT_READY; obs = slot.frame.observationId; request = true; slot.frame.readyMonoNs = std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch()).count(); framesReady_++; }
            else slot.state = SLOT_FREE;
            if (seq <= 3 || (seq % kLogEveryFrames) == 0) {
                Log("FS-MEAS depth #%llu: %u records (valid %u, rejected %u, threshold %u frac %.2f, predicted %u consistent %u (alt convention %u) new %u, edge %u, lowTex %u, invalid %u, overflow %u, groups %u, pred %ux%ux%u valid=%u, cam %ux%u mask=%u used=%llu/%llu) gpu %lld us", (unsigned long long)seq,
                    slot.frame.count, ctr[FS_MEAS_CTR_VALID], ctr[FS_MEAS_CTR_REJECTED], ctr[FS_MEAS_CTR_THRESHOLD], ctr[FS_MEAS_CTR_FRACTION] / 65536.0, ctr[FS_MEAS_CTR_PREDICTED], ctr[FS_MEAS_CTR_CONSISTENT], ctr[FS_MEAS_CTR_CONSISTENT_ALT], ctr[FS_MEAS_CTR_NEW],
                    ctr[FS_MEAS_CTR_EDGE], ctr[FS_MEAS_CTR_LOWTEX], ctr[FS_MEAS_CTR_INVALID], slot.frame.overflow, ctr[FS_MEAS_CTR_GROUPS], slot.blk->predInfo[0], slot.blk->predInfo[1], slot.blk->predInfo[2], slot.blk->predInfo[3], slot.blk->camInfo[0], slot.blk->camInfo[1], slot.blk->camInfo[2], (unsigned long long)camFramesUsed_, (unsigned long long)camFrames_, (long long)lastGpuUs_);
                if (ctr[FS_MEAS_CTR_STEREO_TESTED]) {
                    auto mm = [&](uint32_t bin) { const uint32_t n = ctr[FS_MEAS_CTR_STEREO_BIN_N + bin]; return n ? (double)ctr[FS_MEAS_CTR_STEREO_BIN_RES + bin] * 0.1 / n : 0.0; };
                    const uint32_t v = ctr[FS_MEAS_CTR_STEREO_VALID];
                    Log("FS-MEAS stereo #%llu: pair obs %u skew %u tested %u valid %u lowTex %u ambiguous %u bandEdge %u noCover %u | |z_stereo-z_env| mean mm by prior bin <0.75:%.1f(n%u) <1.5:%.1f(n%u) <2.5:%.1f(n%u) >=2.5:%.1f(n%u) | sigma mean stereo %.2f mm vs env %.2f mm (frames with pair %llu)",
                        (unsigned long long)seq, slot.blk->stereoInfo[0], slot.blk->stereoInfo[1], ctr[FS_MEAS_CTR_STEREO_TESTED], v, ctr[FS_MEAS_CTR_STEREO_LOWTEX], ctr[FS_MEAS_CTR_STEREO_AMBIG], ctr[FS_MEAS_CTR_STEREO_EDGEBAND], ctr[FS_MEAS_CTR_STEREO_NOCOVER],
                        mm(0), ctr[FS_MEAS_CTR_STEREO_BIN_N], mm(1), ctr[FS_MEAS_CTR_STEREO_BIN_N + 1], mm(2), ctr[FS_MEAS_CTR_STEREO_BIN_N + 2], mm(3), ctr[FS_MEAS_CTR_STEREO_BIN_N + 3],
                        v ? ctr[FS_MEAS_CTR_STEREO_SIGMA_UM] * 1e-3 / v : 0.0, v ? ctr[FS_MEAS_CTR_ENV_SIGMA_UM] * 1e-3 / v : 0.0, (unsigned long long)stereoFrames_);
                }
                if (ctr[FS_MEAS_CTR_TEMPORAL_TESTED] || ctr[FS_MEAS_CTR_PLANAR_TESTED]) {
                    const uint32_t tv = ctr[FS_MEAS_CTR_TEMPORAL_VALID], pv = ctr[FS_MEAS_CTR_PLANAR_VALID];
                    Log("FS-MEAS multiview #%llu: keyframe %s %ux%u | temporal tested %u valid %u lowTex %u ambiguous %u bandEdge %u noCover %u disagree %u sigma mean %.2f mm | planar tested %u valid %u rejected %u sigma mean %.2f mm rms mean %.2f mm",
                        (unsigned long long)seq, slot.blk->keyInfo[2] ? "bound" : "none", slot.blk->keyInfo[0], slot.blk->keyInfo[1],
                        ctr[FS_MEAS_CTR_TEMPORAL_TESTED], tv, ctr[FS_MEAS_CTR_TEMPORAL_LOWTEX], ctr[FS_MEAS_CTR_TEMPORAL_AMBIG], ctr[FS_MEAS_CTR_TEMPORAL_EDGE], ctr[FS_MEAS_CTR_TEMPORAL_NOCOVER], ctr[FS_MEAS_CTR_TEMPORAL_DISAGREE], tv ? ctr[FS_MEAS_CTR_TEMPORAL_SIGMA_UM] * 1e-3 / tv : 0.0,
                        ctr[FS_MEAS_CTR_PLANAR_TESTED], pv, ctr[FS_MEAS_CTR_PLANAR_REJECTED], pv ? ctr[FS_MEAS_CTR_PLANAR_SIGMA_UM] * 1e-3 / pv : 0.0, pv ? ctr[FS_MEAS_CTR_PLANAR_RMS_UM] * 1e-3 / pv : 0.0);
                }
                // Receipt (§20): where the records are. Host-visible ring, read after the fence retired.
                const FsSurfaceMeasurement* rec = (const FsSurfaceMeasurement*)slot.ring.records.mapped;
                const uint32_t n = slot.frame.count, step = n > 512 ? n / 512 : 1;
                float dmin = 1e30f, dmax = 0.f, dsum = 0.f; uint32_t ns = 0, inRange = 0;
                float bmin[3] = {1e30f, 1e30f, 1e30f}, bmax[3] = {-1e30f, -1e30f, -1e30f};
                for (uint32_t i = 0; i < n; i += step) {
                    const FsSurfaceMeasurement& m = rec[i];
                    const float* e = slot.frame.eyeOrigin[(m.sourceFlags >> FS_MEAS_SRC_EYE_SHIFT) & 1u];
                    const float dx = m.px - e[0], dy = m.py - e[1], dz = m.pz - e[2], d = sqrtf(dx * dx + dy * dy + dz * dz);
                    dmin = std::min(dmin, d); dmax = std::max(dmax, d); dsum += d; ++ns; if (d > 0.2f && d < 6.f) ++inRange;
                    bmin[0] = std::min(bmin[0], m.px); bmin[1] = std::min(bmin[1], m.py); bmin[2] = std::min(bmin[2], m.pz);
                    bmax[0] = std::max(bmax[0], m.px); bmax[1] = std::max(bmax[1], m.py); bmax[2] = std::max(bmax[2], m.pz);
                }
                if (ns) Log("FS-MEAS receipt #%llu: eyeL=(%.2f %.2f %.2f) eyeR=(%.2f %.2f %.2f) sampled=%u dist min/mean/max=%.2f/%.2f/%.2f m inRange=%u bbox=(%.2f %.2f %.2f)-(%.2f %.2f %.2f) fov0=(%.2f %.2f %.2f %.2f) near=%.2f far=%.2f",
                    (unsigned long long)seq, slot.frame.eyeOrigin[0][0], slot.frame.eyeOrigin[0][1], slot.frame.eyeOrigin[0][2], slot.frame.eyeOrigin[1][0], slot.frame.eyeOrigin[1][1], slot.frame.eyeOrigin[1][2],
                    ns, dmin, dsum / ns, dmax, inRange, bmin[0], bmin[1], bmin[2], bmax[0], bmax[1], bmax[2], input_.fovL[0], input_.fovL[1], input_.fovL[2], input_.fovL[3], input_.nearZ, input_.farZ);
            }
                return request;
    }
    // C10R / C11R bounded refine chunk: stereo then temporal over [offset, offset + chunk) of the frame's records. The chunk is sized
    // from the refine cost model (items = image solves actually run) and the frame's candidate density; the frame's refine budget
    // FS_MEAS_REFINE_MAX_US ends refinement (remaining candidates keep their Env Depth records, counted).
    void SubmitRefineLocked(uint32_t s) {
        RingSlot& slot = slots_[s];
        const uint32_t count = slot.frame.count;
        const double density = std::max(1e-3, (double)slot.candidates / (double)std::max(count, 1u));
        const uint32_t items = refineCost_.Batch(2000.0, 32u, 65536u, 512u);
        const uint32_t chunk = std::min<uint32_t>(count - slot.refineOffset, std::max<uint32_t>(256u, (uint32_t)((double)items / density)));
        if (!BindBuffer(pipes_[K_STEREO], FS_MEAS_B_RECORDS, slot.ring.records) || !BindBuffer(pipes_[K_TEMPORAL], FS_MEAS_B_RECORDS, slot.ring.records) ||
            !BindBuffer(pipes_[K_STEREO], FS_MEAS_B_COUNTERS, slot.ring.counters) || !BindBuffer(pipes_[K_TEMPORAL], FS_MEAS_B_COUNTERS, slot.ring.counters)) { LogError("FS-MEAS bind refine slot %u failed", s); return; }
        static PushRefine pr; pr.offset = slot.refineOffset; pr.count = chunk; pr.recordCap = count; pr.pad = 0;
        Dispatch d[2];
        for (uint32_t i = 0; i < 2; ++i) { d[i].pipeline = &pipes_[i == 0 ? K_STEREO : K_TEMPORAL]; d[i].push = &pr; d[i].pushBytes = sizeof pr; d[i].gx = (chunk + 63u) / 64u; }
        const uint32_t before = slot.blk->ctr[FS_MEAS_CTR_STEREO_TESTED] + slot.blk->ctr[FS_MEAS_CTR_TEMPORAL_TESTED];
        JobDesc jd; jd.cls = FS_JOB_SCAN; jd.name = "meas_refine"; jd.dispatches = d; jd.dispatchCount = 2;
        jd.onRetired = [this, s, chunk, before](bool ok, uint64_t gs, uint64_t ge) {
            uint32_t obs = 0; bool request = false;
            {
                std::lock_guard<std::mutex> g(m_);
                jobInFlight_ = false;
                RingSlot& sl = slots_[s];
                if (sl.state != SLOT_REFINING) return;
                const int64_t us = ge > gs ? (int64_t)((ge - gs) / 1000ull) : 0;
                if (ok) {
                    const uint32_t solved = sl.blk->ctr[FS_MEAS_CTR_STEREO_TESTED] + sl.blk->ctr[FS_MEAS_CTR_TEMPORAL_TESTED] - before;
                    refineCost_.Add(solved, us); sl.refineOffset += chunk; sl.refineUs += us; refineJobs_++; lastRefineUs_ = us;
                }
                if (sl.refineOffset >= sl.frame.count || sl.refineUs >= (int64_t)FS_MEAS_REFINE_MAX_US || !ok) {
                    uint32_t skipped = 0;
                    const FsSurfaceMeasurement* rec = (const FsSurfaceMeasurement*)sl.ring.records.mapped;
                    for (uint32_t i = sl.refineOffset; i < sl.frame.count; ++i) if (rec[i].sourceFlags & (FS_MEAS_SRC_CAND_STEREO | FS_MEAS_SRC_CAND_TEMPORAL)) skipped++;
                    sl.blk->ctr[FS_MEAS_CTR_REFINE_SKIPPED] = skipped;
                    refineFrameUs_.Add((float)sl.refineUs / 1000.f);
                    request = FinalizeLocked(s, obs);
                }
            }
            if (request) FsScan_RequestTick(obs);
        };
        if (!SubmitJob(jd)) return;
        jobInFlight_ = true;
    }
    // ---- consumer handoff (any thread) --------------------------------------------------------------------
    bool PeekFrame(MeasGpuFrame& out) {
        std::lock_guard<std::mutex> g(m_);
        int32_t newest = -1; uint64_t best = 0;
        for (uint32_t s = 0; s < FS_MEAS_GPU_RING_SLOTS; ++s) if (slots_[s].state == SLOT_READY && slots_[s].frame.sequence > best) { best = slots_[s].frame.sequence; newest = (int32_t)s; }
        if (newest < 0) return false;
        for (uint32_t s = 0; s < FS_MEAS_GPU_RING_SLOTS; ++s)            // older READY frames are stale (latest only)
            if ((int32_t)s != newest && slots_[s].state == SLOT_READY) { CounterAdd(FS_CTR_MEASUREMENTS_DROPPED, (int64_t)slots_[s].frame.count); framesSuperseded_++; slots_[s].state = SLOT_FREE; }
        slots_[newest].state = SLOT_LEASED; out = slots_[newest].frame;
        return true;
    }
    void ReleaseFrame(uint64_t sequence) {
        std::lock_guard<std::mutex> g(m_);
        for (RingSlot& r : slots_) if (r.state == SLOT_LEASED && r.frame.sequence == sequence) { r.state = SLOT_FREE; return; }
    }
    const MeasGpuRing* Ring(uint32_t slot) { std::lock_guard<std::mutex> g(m_); return deviceUp_ && slot < FS_MEAS_GPU_RING_SLOTS ? &slots_[slot].ring : nullptr; }
    uint32_t ReadyFrames() { std::lock_guard<std::mutex> g(m_); uint32_t n = 0; for (RingSlot& r : slots_) if (r.state == SLOT_READY) n++; return n; }
    int64_t NewestReadyNs() { std::lock_guard<std::mutex> g(m_); int64_t t = 0; uint64_t best = 0; for (RingSlot& r : slots_) if (r.state == SLOT_READY && r.frame.sequence > best) { best = r.frame.sequence; t = r.frame.readyMonoNs; } return t; }
    void FrameTotals(uint64_t& ready, uint64_t& superseded) { std::lock_guard<std::mutex> g(m_); ready = framesReady_; superseded = (uint64_t)framesSuperseded_; }

private:
    std::mutex m_;
    std::mutex targetMutex_;                    // cross-module C11R2 mailbox; never nests World::m_ with Measure::m_
    bool inited_ = false, deviceUp_ = false, pipeReady_ = false;
    // XR_META_environment_depth / OpenGL depth image convention: texel row 0 is the LOWER (tanDown) edge (the Meta
    // reference sample projects NDC to depth UV as xy * 0.5 + 0.5 with no Y flip; the donor's FineLoadWorld maps texel
    // y = 0 to NDC y = -1). The owned R32 copy preserves texel addressing verbatim, so the default is FLIP_Y.
    // Device receipt without it (run 23:22, 5e339b9): eye at y = 1.87 m, back-projected bbox up to y = 5.55 m, 15 %
    // matched associations, canonical growing linearly (correct range, mirrored ray direction).
    uint32_t budget_ = FS_MEAS_DEFAULT_BUDGET, maxOut_ = FS_MEAS_DEFAULT_MAX_OUT, flags_ = FS_MEAS_FLAG_FLIP_Y | (FS_PRED_ROW0_TOP ? FS_MEAS_FLAG_PRED_FLIP_Y : 0u);
    EnvDepthInput input_;
    uint64_t imported_ = 0, submitted_ = 0; uint32_t importedFrame_ = 0, importedW_ = 0, importedH_ = 0, importedLayers_ = 0; bool pendingSubmit_ = false;
    VkImageView view_ = VK_NULL_HANDLE; VkFormat fmt_ = VK_FORMAT_UNDEFINED;
    VkImageView predView_ = VK_NULL_HANDLE; fs::render::PredictionInfo pred_; bool predValid_ = false; uint64_t predBinds_ = 0;
    CameraInput cam_[2]; StereoPairInput pair_; uint64_t stereoFrames_ = 0; uint64_t stereoTotals_[48] = {};
    uint64_t framesReady_ = 0, yieldedToFusion_ = 0;
    Buffer tiles_, targetsBuf_;
    std::vector<RefineTarget> refineTargets_, pendingRefineTargets_;
    CostModel refineCost_; AgeWindow refineFrameUs_; uint64_t refineJobs_ = 0, pairsGeometryRejected_ = 0; int64_t lastRefineUs_ = 0;
    CameraInput key_[FS_MEAS_KEYFRAMES]; int32_t keySel_ = -1; uint64_t keySelSeq_ = 0; bool keyImported_ = false; VkImageView keyView_ = VK_NULL_HANDLE; uint64_t keyframesSet_ = 0, keyFramesUsed_ = 0; VkImageView camView_[2] = {VK_NULL_HANDLE, VK_NULL_HANDLE}; bool camImported_[2] = {false, false}; uint32_t camImportedW_[2] = {0, 0}, camImportedH_[2] = {0, 0}; uint64_t camFrames_ = 0, camFramesUsed_ = 0;
    Pipeline pipes_[K_COUNT_]; Buffer score_, select_; bool scratchBound_ = false;
    RingSlot slots_[FS_MEAS_GPU_RING_SLOTS];
    bool jobInFlight_ = false; uint64_t handoffSeq_ = 0, binds_ = 0, observationSeq_ = 0;
    int64_t framesSeen_ = 0, framesSubmitted_ = 0, framesSuperseded_ = 0, lastCount_ = 0, lastOverflow_ = 0, lastGpuUs_ = 0, importFailures_ = 0, lastEdge_ = 0, lastRejected_ = 0, lastThreshold_ = 0;
};

Measure& M() { static Measure m; return m; }
std::once_flag g_once;
void EnsureInit() { std::call_once(g_once, []() { M().Init(); }); }
// Register with the executor at library load so the pipeline is part of the regular warm-up (never a late,
// main-thread compile when the first depth frame arrives; contract §15.7). The executor's state is a
// function-local static, so this is safe at static-initialisation time.
struct AutoInit { AutoInit() { EnsureInit(); } } g_autoInit;

} // namespace

bool MeasGpu_PeekFrame(MeasGpuFrame& out) { EnsureInit(); return M().PeekFrame(out); }
void MeasGpu_ReleaseFrame(uint64_t sequence) { EnsureInit(); M().ReleaseFrame(sequence); }
uint32_t MeasGpu_RingSlots() { return FS_MEAS_GPU_RING_SLOTS; }
const MeasGpuRing* MeasGpu_Ring(uint32_t slot) { EnsureInit(); return M().Ring(slot); }
uint32_t MeasGpu_ReadyFrames() { EnsureInit(); return M().ReadyFrames(); }
int64_t MeasGpu_NewestReadyNs() { EnsureInit(); return M().NewestReadyNs(); }
void MeasGpu_SetRefineTargets(const RefineTarget* t, uint32_t n) { EnsureInit(); M().SetRefineTargets(t, n); }
void MeasGpu_FrameTotals(uint64_t& ready, uint64_t& superseded) { EnsureInit(); M().FrameTotals(ready, superseded); }

} // namespace meas
} // namespace fs

using fs::meas::M; using fs::meas::EnsureInit;

FS_API int32_t FsMeas_SetEnvDepth(void* unityDepthTextureArray, uint32_t width, uint32_t height, const float poseL[16], const float poseR[16], const float fovL[4], const float fovR[4], float nearZ, float farZ, int64_t xrTimeNs) {
    EnsureInit(); return M().SetEnvDepth(unityDepthTextureArray, width, height, poseL, poseR, fovL, fovR, nearZ, farZ, xrTimeNs);
}
FS_API int32_t FsMeas_SetParams(uint32_t budget, uint32_t maxOut, uint32_t flags) { EnsureInit(); return M().SetParams(budget, maxOut, flags); }
FS_API int32_t FsMeas_SetCameraFrame(int32_t eye, void* unityTexture, uint32_t width, uint32_t height, uint32_t sensorWidth, uint32_t sensorHeight, const float worldFromCamera[16], float fx, float fy, float cx, float cy, int32_t rowFlip, int64_t xrTimeNs) {
    EnsureInit(); return M().SetCameraFrame(eye, unityTexture, width, height, sensorWidth, sensorHeight, worldFromCamera, fx, fy, cx, cy, rowFlip, xrTimeNs); }
FS_API int32_t FsMeas_SetStereoPair(uint32_t observationId, int64_t xrTimeNsL, int64_t xrTimeNsR, uint32_t skewClass, int32_t geometryEligible, float confidence, float blurPenalty, int64_t uncertaintyNs) { EnsureInit(); return M().SetStereoPair(observationId, xrTimeNsL, xrTimeNsR, skewClass, geometryEligible, confidence, blurPenalty, uncertaintyNs); }
FS_API int32_t FsMeas_SetKeyframe(uint32_t slotIndex, void* unityTexture, uint32_t width, uint32_t height, uint32_t sensorWidth, uint32_t sensorHeight, const float worldFromCamera[16], float fx, float fy, float cx, float cy, int32_t rowFlip, int64_t xrTimeNs) {
    EnsureInit(); return M().SetKeyframe(slotIndex, unityTexture, width, height, sensorWidth, sensorHeight, worldFromCamera, fx, fy, cx, cy, rowFlip, xrTimeNs); }
FS_API int32_t FsMeas_GetMultiviewStats(int64_t out[22]) { EnsureInit(); if (!out) return FS_ERR_INVALID; M().MultiviewStats(out); return FS_OK; }
FS_API int32_t FsMeas_GetTargetStats(int64_t out[11]) { EnsureInit(); if (!out) return FS_ERR_INVALID; M().TargetStats(out); return FS_OK; }
FS_API int32_t FsMeas_GetStereoStats(int64_t out[17]) { EnsureInit(); if (!out) return FS_ERR_INVALID; M().StereoStats(out); return FS_OK; }
FS_API int32_t FsMeas_GetStats(int64_t out[8]) { EnsureInit(); if (!out) return FS_ERR_INVALID; M().Stats(out); return FS_OK; }
