// FinalScan render module (C09R-D; contract §13.1, §13.4, §13.5): the frame-begin hook records, into the
// executor's frame command buffer, at most 3 HZB dispatches (only when a new depth prior arrived) and the
// bounded hierarchical cut: pages -> FS_CULL_LEVELS frontier expansions (one indirect dispatch per level,
// ping-pong queues) -> leaf blocks -> compact+finish. Every node visited belongs to the frontier of a visible
// page: hidden or coarse branches never enumerate their subtree. The world's published render roots
// (FsPageDesc.renderRoot, graphics stream) are the only entry; the render tree/blocks are immutable COW pools.
// No CPU decisions between dispatches, no readback; the cut is recomputed at most every 2 frames or when the
// head moved beyond the prediction margin. Outputs (Unity-final layout): draw records [opaque leaf
// surfels][aggregates] contiguous; args = two FsIndirectDrawArgs {24, opaque*2, 0, 0} {6, agg*2, 0, 0}.
// Every dispatch group is bracketed by a frame-stage timestamp pair (§15.8 receipts: hzb.scatter/combine/mips,
// cull.pages/expand/blocks/compact). Render modes (§13.5): SCAN (band occlusion + LOD, HZB fails open), XRAY
// (no depth-prior cull), PLAN (no backface cone).
#include "fs_render.h"
#include "fs_render_kernels.h"
#include "fs_cull_math.h"
#include "fs_hzb_math.h"
#include "../fs_executor.h"
#include "../world/fs_world.h"
#include "../../log.h"
#include <math.h>
#include <mutex>
#include <string.h>
#include <stdio.h>
#include <algorithm>

#define FS_API extern "C" __attribute__((visibility("default")))

namespace fs {
namespace render {
namespace {

constexpr uint32_t kRecutFrameInterval = 2;
constexpr uint32_t kAggregateCapacityMax = 65536;
constexpr float    kCenterNear = 0.05f, kCenterFar = 200.f;

struct DepthSource {
    void* unityPtr = nullptr; uint32_t width = 0, height = 0, layers = 0;
    Mat4 invViewProj[2]; Mat4 viewProj[2]; bool valid = false; uint64_t version = 0;
    float nearZ = 0.f, farZ = 0.f;
};

class Render {
public:
    void Init() {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (inited_) return;
        inited_ = true;
        world::EnsureInit();
        RegisterDeviceHook([this](bool up) { OnDevice(up); });
        RegisterWarmupStep("render.pipelines", [this]() { return CreatePipelines(); });
        RegisterFrameBeginHook([this](VkCommandBuffer cmd, uint32_t frame) { OnFrameBegin(cmd, frame); });
        RegisterFrameStageSink([this](const char* name, uint64_t start, uint64_t end, uint32_t frame) { OnStage(name, start, end, frame); });
        viewL_ = viewR_ = Identity(); projL_ = projR_ = Identity();
        if (ExecReady()) OnDevice(true);
    }
    // ---- device / pipelines ----------------------------------------------------------------------
    void OnDevice(bool up) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (up == deviceUp_) return;
        if (!up) { Destroy(); deviceUp_ = false; bound_ = false; pipesReady_ = false; for (Pipeline& p : pipes_) p = Pipeline{}; return; }   // pipelines died with the device objects
        const VkBufferUsageFlags use = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        bool ok = true;
        ok &= CreateBuffer(frameRing_, (VkDeviceSize)FS_CULL_FRAME_RING * sizeof(CullFrame), use, true, "render.frameRing");
        ok &= CreateBuffer(work_, (VkDeviceSize)WorkWords() * 4, use | VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT, true, "render.work");
        ok &= CreateBuffer(hzb_, (VkDeviceSize)HzbWords() * 4, use, false, "render.hzb");
        ok &= CreateBuffer(aggScratch_, (VkDeviceSize)kAggregateCapacityMax * sizeof(FsDrawRecord), use, false, "render.aggScratch");
        ok &= CreateBuffer(statsRing_, (VkDeviceSize)FS_CULL_FRAME_RING * FS_CULL_STATS_WORDS * 4, use, true, "render.stats");
        ok &= CreateBuffer(fallbackDraw_, (VkDeviceSize)FS_DEFAULT_DRAW_CAPACITY * sizeof(FsDrawRecord), use, false, "render.fallbackDraw");
        ok &= CreateBuffer(fallbackArgs_, 2 * sizeof(FsIndirectDrawArgs), use | VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT, true, "render.fallbackArgs");
        if (!ok || !frameRing_.mapped || !work_.mapped || !statsRing_.mapped) { LogError("FS-RENDER buffer creation failed"); Destroy(); return; }
        uint32_t* w = (uint32_t*)work_.mapped; memset(w, 0, WorkWords() * 4);
        InitWorkArgs(w);
        memset(statsRing_.mapped, 0, FS_CULL_FRAME_RING * FS_CULL_STATS_WORDS * 4);
        memset(fallbackArgs_.mapped, 0, 2 * sizeof(FsIndirectDrawArgs));
        deviceUp_ = true; drawImported_ = false; lastCutFrame_ = 0; hzbBuiltVersion_ = 0;
        EnsureBound();
    }
    static uint32_t WorkWords() { return WK_WORDS; }
    static void InitWorkArgs(uint32_t* w) {                      // twin of the compact pass's reset
        for (uint32_t p = 0; p < 2; ++p) { w[WK_EXPAND_ARGS + 4 * p + 0] = 0; w[WK_EXPAND_ARGS + 4 * p + 1] = 1; w[WK_EXPAND_ARGS + 4 * p + 2] = 1; w[WK_EXPAND_ARGS + 4 * p + 3] = 0; }
        w[WK_EMIT_ARGS + 0] = 0; w[WK_EMIT_ARGS + 1] = 1; w[WK_EMIT_ARGS + 2] = 1; w[WK_EMIT_ARGS + 3] = 0;
        w[WK_COMPACT_ARGS + 0] = 1; w[WK_COMPACT_ARGS + 1] = 1; w[WK_COMPACT_ARGS + 2] = 1; w[WK_COMPACT_ARGS + 3] = 0;
    }
    static uint32_t HzbWords() { return 3 * FS_HZB_SIZE * FS_HZB_SIZE + 2 * HzbTotalTexels(); }
    void Destroy() {
        Buffer* all[] = {&frameRing_, &work_, &hzb_, &aggScratch_, &statsRing_, &fallbackDraw_, &fallbackArgs_};
        for (Buffer* b : all) if (b->buffer != VK_NULL_HANDLE) DestroyBuffer(*b);
        unityDraw_ = Buffer{}; unityArgs_ = Buffer{}; drawImported_ = false;
    }
    bool CreatePipelines() {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (pipesReady_) return true;
        for (uint32_t k = 0; k < R_COUNT; ++k) {
            const KernelSpec& ks = kRenderKernels[k];
            VkDescriptorSetLayoutBinding b[8] = {};
            for (uint32_t i = 0; i < ks.bindingCount; ++i) {
                b[i].binding = ks.bindings[i]; b[i].descriptorCount = 1; b[i].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
                bool sampler = ks.samplerBindings && i >= ks.bindingCount - ks.samplerBindings;
                b[i].descriptorType = sampler ? VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER : VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
            }
            if (!CreateComputePipeline(pipes_[k], ks.spirv, ks.words, ks.pushBytes, b, ks.bindingCount, ks.name)) { LogError("FS-RENDER pipeline %s failed", ks.name); return false; }
        }
        pipesReady_ = true; bound_ = false;
        EnsureBound();
        return true;
    }
    const Buffer* BufferFor(uint32_t kernel, uint32_t binding) {
        const world::WorldBuffers* wb = world::Buffers();
        switch (kernel) {
        case R_CULL_COMPACT:
            if (binding == RB_AGGSRC) return &aggScratch_;
            if (binding == RB_ARGS) return ArgsBuffer();
            if (binding == RB_STATS) return &statsRing_;
            break;
        case R_CULL_EXPAND:
            if (binding == RB_DRAW) return &aggScratch_;
            break;
        default: break;
        }
        switch (binding) {
        case RB_FRAME: return &frameRing_;
        case RB_PAGES: return wb ? &wb->pages : nullptr;
        case RB_RBLOCKS: return wb ? &wb->rblocks : nullptr;
        case RB_RNODES: return wb ? &wb->render : nullptr;
        case RB_WORK: return &work_;
        case RB_DRAW: return DrawBuffer();
        case RB_HZB: return &hzb_;
        default: return nullptr;
        }
    }
    const Buffer* DrawBuffer() { return drawImported_ ? &unityDraw_ : &fallbackDraw_; }
    const Buffer* ArgsBuffer() { return drawImported_ ? &unityArgs_ : &fallbackArgs_; }
    void EnsureBound() {
        if (bound_ || !pipesReady_ || !deviceUp_ || !world::Buffers()) return;
        for (uint32_t k = 0; k < R_COUNT; ++k) {
            const KernelSpec& ks = kRenderKernels[k];
            for (uint32_t i = 0; i < ks.bindingCount - ks.samplerBindings; ++i) {
                const Buffer* b = BufferFor(k, ks.bindings[i]);
                if (!b || !BindBuffer(pipes_[k], ks.bindings[i], *b)) { LogError("FS-RENDER bind %s:%u failed", ks.name, ks.bindings[i]); return; }
            }
        }
        bound_ = true;
        UpdateDrawLayout();
    }
    void UpdateDrawLayout() {
        uint32_t cap = drawImported_ ? (uint32_t)(unityDrawBytes_ / sizeof(FsDrawRecord)) : (uint32_t)FS_DEFAULT_DRAW_CAPACITY;
        if (cap < 1024) cap = 1024;
        aggCapacity_ = std::min<uint32_t>(kAggregateCapacityMax, cap / 8);
        opaqueCapacity_ = cap - aggCapacity_;
        if (screenWorkBudget_ && screenWorkBudget_ < opaqueCapacity_) opaqueCapacity_ = screenWorkBudget_;
        aggBase_ = opaqueCapacity_;
    }

    // ---- frame-begin hook (render thread, inside FS_HEVT_FRAME_BEGIN) ----------------------------
    void OnFrameBegin(VkCommandBuffer cmd, uint32_t frame) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (!deviceUp_ || !pipesReady_ || !world::DeviceUp()) return;
        FrameMode mode = CurrentFrameMode();
        if (mode == FS_FRAME_MODE_SKIPPED || mode == FS_FRAME_MODE_NONE) return;
        EnsureBound();
        if (!bound_) return;
        // Unity draw/args import (once per handle; descriptor rebind before this frame's dispatches)
        if (regDraw_ && (!drawImported_ || regDrawVersion_ != importedDrawVersion_)) {
            Buffer d, a;
            if (ImportUnityBuffer(regDraw_, d) && ImportUnityBuffer(regArgs_, a)) {
                unityDraw_ = d; unityArgs_ = a; drawImported_ = true; importedDrawVersion_ = regDrawVersion_;
                UpdateDrawLayout();
                BindBuffer(pipes_[R_CULL_BLOCKS], RB_DRAW, unityDraw_); BindBuffer(pipes_[R_CULL_COMPACT], RB_DRAW, unityDraw_); BindBuffer(pipes_[R_CULL_COMPACT], RB_ARGS, unityArgs_);
                Log("FS-RENDER Unity draw buffers imported (%u records, opaque %u + agg %u)", (uint32_t)(unityDrawBytes_ / 32), opaqueCapacity_, aggCapacity_);
            } else LogError("FS-RENDER ImportUnityBuffer failed; native fallback buffers stay bound");
            cmd = FrameCommandBuffer();
        }
        // Depth priors: import + bind every frame we (re)build the HZB (Unity may re-transition the image)
        bool hzbWanted = false, prevOk = false, envOk = false;
        if (renderMode_ == FS_RENDER_MODE_SCAN && (prev_.valid || env_.valid) && hzbBuiltVersion_ != DepthVersion()) {
            VkImage img; VkImageView viewPrev = VK_NULL_HANDLE, viewEnv = VK_NULL_HANDLE; VkFormat fmt; uint32_t w, h, layers;
            if (prev_.valid && ImportUnityTexture(prev_.unityPtr, img, viewPrev, fmt, w, h, layers)) { prevOk = true; prev_.width = w; prev_.height = h; prev_.layers = layers; }
            if (env_.valid && ImportUnityTexture(env_.unityPtr, img, viewEnv, fmt, w, h, layers)) { envOk = true; env_.width = w; env_.height = h; env_.layers = layers; }
            cmd = FrameCommandBuffer();
            if (prevOk || envOk) {
                VkImageView vp = prevOk ? viewPrev : viewEnv, ve = envOk ? viewEnv : viewPrev;   // unused source binds the other view (descriptor must be valid)
                if (BindImage(pipes_[R_HZB_SCATTER], RB_DEPTH_PREV, vp, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, ExecSampler(false)) &&
                    BindImage(pipes_[R_HZB_SCATTER], RB_DEPTH_ENV, ve, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, ExecSampler(false))) hzbWanted = true;
            }
        }
        // Recut decision (§13.5.3): every kRecutFrameInterval frames or when the head moved beyond the margin
        float head[3]; memcpy(head, headPos_, sizeof head);
        float fwd[3] = {-viewL_.m[2], -viewL_.m[6], -viewL_.m[10]};
        float dp[3] = {head[0] - lastHead_[0], head[1] - lastHead_[1], head[2] - lastHead_[2]};
        float movedM = sqrtf(dp[0] * dp[0] + dp[1] * dp[1] + dp[2] * dp[2]);
        float turnDeg = EccentricityDeg(lastFwd_, fwd);
        bool moved = movedM > tanf(marginDeg_ * 0.0174533f) || turnDeg > marginDeg_;
        bool recut = forceRecut_ || moved || hzbWanted || lastCutFrame_ == 0 || frame - lastCutFrame_ >= kRecutFrameInterval || anchorSeq_ != lastAnchorSeq_;
        if (!recut) return;
        uint32_t slot = frame % FS_CULL_FRAME_RING;
        CullFrame& F = ((CullFrame*)frameRing_.mapped)[slot];
        FillFrame(F, frame, !moved && !forceRecut_, prevOk, envOk);
        if (hzbWanted) RecordHzb(cmd, slot);
        RecordCull(cmd, slot);
        recuts_++;
        lastCutFrame_ = frame; memcpy(lastHead_, head, sizeof lastHead_); memcpy(lastFwd_, fwd, sizeof lastFwd_); lastAnchorSeq_ = anchorSeq_; forceRecut_ = false;
        if (hzbWanted) hzbBuiltVersion_ = DepthVersion();
    }
    uint64_t DepthVersion() const { return prev_.version * 1000003ull + env_.version; }
    // Device timestamps of the frame-hook stages (§15.8): the receipt behind FsRender_GetLastCullStats.cullGpuUs and
    // the per-dispatch breakdown in the receipt log / telemetry (the 075e371 3 fps defect hid inside one "render.hzb" bracket).
    enum Stage : uint32_t { S_HZB_SCATTER = 0, S_HZB_COMBINE, S_HZB_MIPS, S_CULL_PAGES, S_CULL_EXPAND, S_CULL_BLOCKS, S_CULL_COMPACT, S_COUNT };
    static constexpr const char* kStageNames[S_COUNT] = {"render.hzb.scatter", "render.hzb.combine", "render.hzb.mips", "render.cull.pages", "render.cull.expand", "render.cull.blocks", "render.cull.compact"};
    struct StageStat { int64_t lastUs = 0, totalUs = 0, maxUs = 0; uint64_t count = 0; };
    void OnStage(const char* name, uint64_t start, uint64_t end, uint32_t frame) {
        std::lock_guard<std::recursive_mutex> g(m_);
        const int64_t us = end > start ? (int64_t)((end - start) / 1000ull) : 0;
        uint32_t k = S_COUNT;
        for (uint32_t i = 0; i < S_COUNT; ++i) if (std::strcmp(name, kStageNames[i]) == 0) { k = i; break; }
        if (k == S_COUNT) return;
        StageStat& st = stages_[k]; st.lastUs = us; st.totalUs += us; st.count++; if (us > st.maxUs) st.maxUs = us;
        if (k == S_CULL_COMPACT) {                                   // last stage of a cut: fold the cut receipt
            lastCullUs_ = stages_[S_CULL_PAGES].lastUs + stages_[S_CULL_EXPAND].lastUs + stages_[S_CULL_BLOCKS].lastUs + stages_[S_CULL_COMPACT].lastUs;
            cullUsTotal_ += lastCullUs_; cullStages_++; lastStageFrame_ = frame;
            if (cullStages_ <= 3 || (cullStages_ % 250) == 0) {
                const uint32_t* c = statsRing_.mapped ? (const uint32_t*)statsRing_.mapped + (frame % FS_CULL_FRAME_RING) * FS_CULL_STATS_WORDS : nullptr;
                Log("FS-RENDER receipt: cull %lld us (avg %lld; pages %lld expand %lld blocks %lld compact %lld) hzb %lld us (scatter %lld combine %lld mips %lld, %llu builds) frame %u tested=%u culled=%u reused=%u roots=%u frontier=%u frustum=%u cone=%u hzbRej=%u inBand=%u uncertain=%u blocks=%u agg=%u records=%u represented=%u dropped=%u overflow=%u hzbTiles=%u mode=%d",
                    (long long)lastCullUs_, (long long)(cullUsTotal_ / (int64_t)cullStages_), (long long)stages_[S_CULL_PAGES].lastUs, (long long)stages_[S_CULL_EXPAND].lastUs, (long long)stages_[S_CULL_BLOCKS].lastUs, (long long)stages_[S_CULL_COMPACT].lastUs,
                    (long long)lastHzbUs_, (long long)stages_[S_HZB_SCATTER].lastUs, (long long)stages_[S_HZB_COMBINE].lastUs, (long long)stages_[S_HZB_MIPS].lastUs, (unsigned long long)stages_[S_HZB_MIPS].count, frame,
                    c ? c[FS_CSTAT_PAGES_TESTED] : 0, c ? c[FS_CSTAT_PAGES_CULLED] : 0, c ? c[FS_CSTAT_PAGES_REUSED] : 0, c ? c[FS_CSTAT_ROOTS_VISITED] : 0, c ? c[FS_CSTAT_FRONTIER_NODES] : 0,
                    c ? c[FS_CSTAT_NODES_FRUSTUM] : 0, c ? c[FS_CSTAT_NODES_CONE] : 0, c ? c[FS_CSTAT_NODES_HZB] : 0, c ? c[FS_CSTAT_NODES_IN_BAND] : 0, c ? c[FS_CSTAT_HZB_UNCERTAIN] : 0,
                    c ? c[FS_CSTAT_BLOCKS] : 0, c ? c[FS_CSTAT_AGGREGATES] : 0, c ? c[FS_CSTAT_DRAW_RECORDS] : 0, c ? c[FS_CSTAT_REPRESENTED] : 0, c ? c[FS_CSTAT_BUDGET_DROPPED] : 0,
                    c ? c[FS_CSTAT_FRONTIER_OVERFLOW] : 0, c ? c[FS_CSTAT_HZB_TILES] : 0, renderMode_);
            }
        } else if (k == S_HZB_MIPS) { lastHzbUs_ = stages_[S_HZB_SCATTER].lastUs + stages_[S_HZB_COMBINE].lastUs + stages_[S_HZB_MIPS].lastUs; hzbUsTotal_ += lastHzbUs_; hzbStages_++; }
    }
    void FillFrame(CullFrame& F, uint32_t frame, bool reuse, bool prevOk, bool envOk) {
        memset(&F, 0, sizeof F);
        Mat4 vpL = Mul(projL_, viewL_), vpR = Mul(projR_, viewR_);
        Plane pl[6], pr[6]; FrustumPlanes(vpL, pl, true); FrustumPlanes(vpR, pr, true);
        for (int i = 0; i < 6; ++i) { F.planesL[i][0] = pl[i].a; F.planesL[i][1] = pl[i].b; F.planesL[i][2] = pl[i].c; F.planesL[i][3] = pl[i].d;
                                      F.planesR[i][0] = pr[i].a; F.planesR[i][1] = pr[i].b; F.planesR[i][2] = pr[i].c; F.planesR[i][3] = pr[i].d; }
        // centre view: rotation of the left eye view, positioned at the head; projection = union of both eye tangents
        Mat4 cv = viewL_; { Vec4 t = MulV(cv, Vec4{-headPos_[0], -headPos_[1], -headPos_[2], 0.f}); cv.m[12] = t.x; cv.m[13] = t.y; cv.m[14] = t.z; cv.m[15] = 1.f; }
        float tl, tr, tb, tt; UnionTangents(projL_, projR_, tl, tr, tb, tt);
        Mat4 cp{}; cp.m[0] = 2.f / (tr - tl); cp.m[8] = (tr + tl) / (tr - tl); cp.m[5] = 2.f / (tt - tb); cp.m[9] = (tt + tb) / (tt - tb);
        cp.m[10] = kCenterFar / (kCenterNear - kCenterFar); cp.m[14] = kCenterNear * kCenterFar / (kCenterNear - kCenterFar); cp.m[11] = -1.f;
        Mat4 cvp = Mul(cp, cv);
        memcpy(F.centerViewProj, cvp.m, 64); memcpy(F.centerView, cv.m, 64);
        for (int e = 0; e < 2; ++e) { memcpy(F.srcInvViewProj[e], prev_.invViewProj[e].m, 64); memcpy(F.envInvViewProj[e], env_.invViewProj[e].m, 64); }
        float eyeWidth = prev_.valid && prev_.width ? (float)prev_.width : (float)FS_EYE_WIDTH_PX_DEFAULT;
        F.headPos_focal[0] = headPos_[0]; F.headPos_focal[1] = headPos_[1]; F.headPos_focal[2] = headPos_[2]; F.headPos_focal[3] = FocalPxFromProj(projL_, eyeWidth);
        float fwd[3] = {-viewL_.m[2], -viewL_.m[6], -viewL_.m[10]}; float fl = sqrtf(fwd[0] * fwd[0] + fwd[1] * fwd[1] + fwd[2] * fwd[2]); if (fl < 1e-9f) fl = 1.f;
        F.fwd_tanMargin[0] = fwd[0] / fl; F.fwd_tanMargin[1] = fwd[1] / fl; F.fwd_tanMargin[2] = fwd[2] / fl; F.fwd_tanMargin[3] = tanf(marginDeg_ * 0.0174533f);
        F.lod[0] = fovealPx_; F.lod[1] = peripheralPx_; F.lod[2] = HeadroomBias(headroomUs_); F.lod[3] = (float)opaqueCapacity_;
        F.misc[0] = (uint32_t)renderMode_; F.misc[1] = frame; F.misc[2] = reuse ? 1u : 0u; F.misc[3] = (prevOk ? 1u : 0u) | (envOk ? 2u : 0u);
        F.srcDepth[0] = (float)prev_.width; F.srcDepth[1] = (float)prev_.height; F.srcDepth[2] = (float)prev_.layers;
        F.envDepth[0] = (float)env_.width; F.envDepth[1] = (float)env_.height; F.envDepth[2] = env_.nearZ; F.envDepth[3] = env_.farZ;
        anchorSeq_ = world::CopyAnchors(&F.anchors[0][0]);
    }
    static void UnionTangents(const Mat4& a, const Mat4& b, float& tl, float& tr, float& tb, float& tt) {
        auto tans = [](const Mat4& p, float& l, float& r, float& bo, float& t) {
            float p00 = p.m[0], p02 = p.m[8], p11 = p.m[5], p12 = p.m[9];
            if (fabsf(p00) < 1e-9f) p00 = 1.f; if (fabsf(p11) < 1e-9f) p11 = 1.f;
            float x0 = (-1.f - p02) / p00, x1 = (1.f - p02) / p00, y0 = (-1.f - p12) / p11, y1 = (1.f - p12) / p11;
            l = fminf(x0, x1); r = fmaxf(x0, x1); bo = fminf(y0, y1); t = fmaxf(y0, y1);
        };
        float l1, r1, b1, t1, l2, r2, b2, t2; tans(a, l1, r1, b1, t1); tans(b, l2, r2, b2, t2);
        tl = fminf(l1, l2); tr = fmaxf(r1, r2); tb = fminf(b1, b2); tt = fmaxf(t1, t2);
        if (tr - tl < 1e-4f) { tl = -1.f; tr = 1.f; } if (tt - tb < 1e-4f) { tb = -1.f; tt = 1.f; }
    }
    static void Barrier(VkCommandBuffer cmd) {
        VkMemoryBarrier mb = {}; mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
        mb.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT; mb.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
        vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT, 0, 1, &mb, 0, nullptr, 0, nullptr);
    }
    // Resets the consumed frontier level's indirect args ({0,1,1,0}) between two expansions: level L read parity p,
    // level L+1 writes parity p. A 16-byte transfer write ordered after the reads and before the next dispatch.
    void ResetFrontierArgs(VkCommandBuffer cmd, uint32_t parity) {
        static const uint32_t zero[4] = {0, 1, 1, 0};
        VkMemoryBarrier toTransfer = {}; toTransfer.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
        toTransfer.srcAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_INDIRECT_COMMAND_READ_BIT; toTransfer.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 1, &toTransfer, 0, nullptr, 0, nullptr);
        vkCmdUpdateBuffer(cmd, work_.buffer, (VkDeviceSize)(WK_EXPAND_ARGS + 4 * parity) * 4, sizeof zero, zero);
        VkMemoryBarrier fromTransfer = {}; fromTransfer.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
        fromTransfer.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT; fromTransfer.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
        vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT, 0, 1, &fromTransfer, 0, nullptr, 0, nullptr);
    }
    void Bind(VkCommandBuffer cmd, uint32_t k, const void* push, uint32_t pushBytes) {
        Pipeline& p = pipes_[k];
        vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, p.pipeline);
        vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, p.layout, 0, 1, &p.set, 0, nullptr);
        if (pushBytes) vkCmdPushConstants(cmd, p.layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, pushBytes, push);
    }
    void RecordHzb(VkCommandBuffer cmd, uint32_t slot) {
        PushFrame pf{slot};
        uint32_t bw = std::max(prev_.valid ? prev_.width : 0u, env_.valid ? env_.width : 0u), bh = std::max(prev_.valid ? prev_.height : 0u, env_.valid ? env_.height : 0u);
        uint32_t gx = ((bw + FS_HZB_SRC_BLOCK - 1) / FS_HZB_SRC_BLOCK + 7) / 8, gy = ((bh + FS_HZB_SRC_BLOCK - 1) / FS_HZB_SRC_BLOCK + 7) / 8;
        uint32_t st = FrameStageBegin(cmd, kStageNames[S_HZB_SCATTER]);
        Bind(cmd, R_HZB_SCATTER, &pf, sizeof pf); vkCmdDispatch(cmd, std::max(gx, 1u), std::max(gy, 1u), 4); Barrier(cmd);
        FrameStageEnd(cmd, st); st = FrameStageBegin(cmd, kStageNames[S_HZB_COMBINE]);
        Bind(cmd, R_HZB_COMBINE, nullptr, 0); vkCmdDispatch(cmd, FS_HZB_SIZE / 16, FS_HZB_SIZE / 16, 1); Barrier(cmd);
        FrameStageEnd(cmd, st); st = FrameStageBegin(cmd, kStageNames[S_HZB_MIPS]);
        Bind(cmd, R_HZB_MIPS, nullptr, 0); vkCmdDispatch(cmd, 1, 1, 1); Barrier(cmd);
        FrameStageEnd(cmd, st);
    }
    void RecordCull(VkCommandBuffer cmd, uint32_t slot) {
        const uint32_t pages = world::PageCount();
        PushPages pp{slot, pages};
        uint32_t st = FrameStageBegin(cmd, kStageNames[S_CULL_PAGES]);
        Bind(cmd, R_CULL_PAGES, &pp, sizeof pp); vkCmdDispatch(cmd, std::max(1u, (pages + FS_WG_SMALL - 1) / FS_WG_SMALL), 1, 1); Barrier(cmd);
        FrameStageEnd(cmd, st); st = FrameStageBegin(cmd, kStageNames[S_CULL_EXPAND]);
        for (uint32_t level = 0; level < FS_CULL_LEVELS; ++level) {          // bounded frontier walk: root .. leaf blocks
            const uint32_t parity = level & 1u;
            PushCull pc{slot, aggBase_, aggCapacity_, opaqueCapacity_, parity};
            Bind(cmd, R_CULL_EXPAND, &pc, sizeof pc); vkCmdDispatchIndirect(cmd, work_.buffer, (VkDeviceSize)(WK_EXPAND_ARGS + 4 * parity) * 4); Barrier(cmd);
            ResetFrontierArgs(cmd, parity);
        }
        FrameStageEnd(cmd, st); st = FrameStageBegin(cmd, kStageNames[S_CULL_BLOCKS]);
        PushCull pc{slot, aggBase_, aggCapacity_, opaqueCapacity_, 0};
        Bind(cmd, R_CULL_BLOCKS, &pc, sizeof pc); vkCmdDispatchIndirect(cmd, work_.buffer, (VkDeviceSize)WK_EMIT_ARGS * 4); Barrier(cmd);
        FrameStageEnd(cmd, st); st = FrameStageBegin(cmd, kStageNames[S_CULL_COMPACT]);
        Bind(cmd, R_CULL_COMPACT, &pc, sizeof pc); vkCmdDispatchIndirect(cmd, work_.buffer, (VkDeviceSize)WK_COMPACT_ARGS * 4); Barrier(cmd);
        FrameStageEnd(cmd, st);
    }
    // Telemetry (C09R §31): per-stage device times of the last cut and the cut statistics the GPU wrote.
    int32_t TelemetryJson(char* out, int32_t cap) {
        std::lock_guard<std::recursive_mutex> g(m_);
        char buf[2048]; int n = 0;
        n += snprintf(buf + n, sizeof buf - n, "{\"recuts\":%llu,\"cuts\":%llu,\"hzbBuilds\":%llu,\"lastCullUs\":%lld,\"lastHzbUs\":%lld,\"stages\":{",
                      (unsigned long long)recuts_, (unsigned long long)cullStages_, (unsigned long long)hzbStages_, (long long)lastCullUs_, (long long)lastHzbUs_);
        for (uint32_t k = 0; k < S_COUNT && n < (int)sizeof buf; ++k)
            n += snprintf(buf + n, sizeof buf - n, "%s\"%s\":{\"lastUs\":%lld,\"avgUs\":%lld,\"maxUs\":%lld,\"count\":%llu}", k ? "," : "", kStageNames[k] + 7,
                          (long long)stages_[k].lastUs, (long long)(stages_[k].count ? stages_[k].totalUs / (int64_t)stages_[k].count : 0), (long long)stages_[k].maxUs, (unsigned long long)stages_[k].count);
        const uint32_t* c = nullptr; uint32_t bestFrame = 0;
        if (statsRing_.mapped) { const uint32_t* sr = (const uint32_t*)statsRing_.mapped; for (uint32_t i = 0; i < FS_CULL_FRAME_RING; ++i) { uint32_t f = __atomic_load_n(&sr[i * FS_CULL_STATS_WORDS + FS_CSTAT_FRAME], __ATOMIC_ACQUIRE); if (f && f >= bestFrame) { bestFrame = f; c = sr + i * FS_CULL_STATS_WORDS; } } }
        n += snprintf(buf + n, sizeof buf - n, "},\"cut\":{\"frame\":%u,\"pagesTested\":%u,\"pagesCulled\":%u,\"frontierNodes\":%u,\"hzbRejected\":%u,\"hzbUncertain\":%u,\"blocks\":%u,\"aggregates\":%u,\"drawRecords\":%u,\"represented\":%u,\"dropped\":%u,\"frontierOverflow\":%u,\"hzbTiles\":%u},\"mode\":%d,\"opaqueCapacity\":%u,\"aggCapacity\":%u}",
                      c ? c[FS_CSTAT_FRAME] : 0, c ? c[FS_CSTAT_PAGES_TESTED] : 0, c ? c[FS_CSTAT_PAGES_CULLED] : 0, c ? c[FS_CSTAT_FRONTIER_NODES] : 0, c ? c[FS_CSTAT_NODES_HZB] : 0, c ? c[FS_CSTAT_HZB_UNCERTAIN] : 0,
                      c ? c[FS_CSTAT_BLOCKS] : 0, c ? c[FS_CSTAT_AGGREGATES] : 0, c ? c[FS_CSTAT_DRAW_RECORDS] : 0, c ? c[FS_CSTAT_REPRESENTED] : 0, c ? c[FS_CSTAT_BUDGET_DROPPED] : 0, c ? c[FS_CSTAT_FRONTIER_OVERFLOW] : 0, c ? c[FS_CSTAT_HZB_TILES] : 0,
                      renderMode_, opaqueCapacity_, aggCapacity_);
        if (n < 0) return 3;
        if (!out || cap <= 0) return n + 1;
        int copy = std::min(n, cap - 1); memcpy(out, buf, copy); out[copy] = 0;
        return n + 1;
    }

    // ---- exports ------------------------------------------------------------------------------------
    int32_t RegisterBuffers(void* draw, uint32_t bytes, void* args) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (!draw || !args || bytes < 1024 * sizeof(FsDrawRecord)) return 2;
        regDraw_ = draw; regArgs_ = args; unityDrawBytes_ = bytes; regDrawVersion_++; forceRecut_ = true;
        return 0;
    }
    int32_t SetView(const float vl[16], const float pl[16], const float vr[16], const float pr[16], const float head[3]) {
        std::lock_guard<std::recursive_mutex> g(m_);
        memcpy(viewL_.m, vl, 64); memcpy(projL_.m, pl, 64); memcpy(viewR_.m, vr, 64); memcpy(projR_.m, pr, 64); memcpy(headPos_, head, sizeof headPos_);
        return 0;
    }
    int32_t SetPrevDepth(void* tex, uint32_t w, uint32_t h, uint32_t layers, const float vl[16], const float pl[16], const float vr[16], const float pr[16]) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (!tex) { prev_.valid = false; return 0; }
        Mat4 v[2], p[2]; memcpy(v[0].m, vl, 64); memcpy(p[0].m, pl, 64); memcpy(v[1].m, vr, 64); memcpy(p[1].m, pr, 64);
        for (int e = 0; e < 2; ++e) { prev_.viewProj[e] = Mul(p[e], v[e]); if (!Invert(prev_.viewProj[e], prev_.invViewProj[e])) return 2; }
        prev_.unityPtr = tex; prev_.width = w; prev_.height = h; prev_.layers = layers; prev_.valid = true; prev_.version++;
        return 0;
    }
    int32_t SetEnvDepth(void* tex, uint32_t w, uint32_t h, const float poseL[16], const float poseR[16], const float fovL[4], const float fovR[4], float nearZ, float farZ, int64_t) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (!tex) { env_.valid = false; return 0; }
        if (nearZ <= 0.f || farZ <= nearZ) return 2;
        for (int e = 0; e < 2; ++e) {
            const float* fov = e ? fovR : fovL; Mat4 pose; memcpy(pose.m, e ? poseR : poseL, 64);
            // OpenXR XrFovf {angleLeft, angleRight, angleUp, angleDown} (radians), Vulkan [0,1] depth, near/far
            float tl = tanf(fov[0]), tr = tanf(fov[1]), tu = tanf(fov[2]), td = tanf(fov[3]);
            if (tr - tl < 1e-6f || tu - td < 1e-6f) return 2;
            Mat4 proj{}; proj.m[0] = 2.f / (tr - tl); proj.m[8] = (tr + tl) / (tr - tl); proj.m[5] = 2.f / (tu - td); proj.m[9] = (tu + td) / (tu - td);
            proj.m[10] = farZ / (nearZ - farZ); proj.m[14] = nearZ * farZ / (nearZ - farZ); proj.m[11] = -1.f;
            Mat4 invProj; if (!Invert(proj, invProj)) return 2;
            env_.invViewProj[e] = Mul(pose, invProj);
        }
        env_.unityPtr = tex; env_.width = w; env_.height = h; env_.layers = 2; env_.nearZ = nearZ; env_.farZ = farZ; env_.valid = true; env_.version++;
        return 0;
    }
    bool Prediction(PredictionInfo& out) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (!prev_.valid || !prev_.unityPtr) return false;
        out.unityPtr = prev_.unityPtr; out.width = prev_.width; out.height = prev_.height; out.layers = prev_.layers; out.version = prev_.version;
        for (int e = 0; e < 2; ++e) { memcpy(out.viewProj[e], prev_.viewProj[e].m, 64); memcpy(out.invViewProj[e], prev_.invViewProj[e].m, 64); }
        return true;
    }
    int32_t SetLodPolicy(float fov, float per, float marginDeg, uint32_t budget) {
        std::lock_guard<std::recursive_mutex> g(m_);
        fovealPx_ = fov > 0.1f ? fov : 0.1f; peripheralPx_ = per > fovealPx_ ? per : fovealPx_; marginDeg_ = marginDeg > 0.f ? marginDeg : 0.f; screenWorkBudget_ = budget;
        UpdateDrawLayout(); forceRecut_ = true; return 0;
    }
    int32_t SetHeadroom(int32_t us) { std::lock_guard<std::recursive_mutex> g(m_); headroomUs_ = us; return 0; }
    int32_t SetMode(int32_t mode) { std::lock_guard<std::recursive_mutex> g(m_); if (mode < 0 || mode > FS_RENDER_MODE_PLAN) return 2; renderMode_ = mode; forceRecut_ = true; return 0; }
    int32_t GetDrawLayout(uint32_t* oc, uint32_t* ab, uint32_t* ac) { std::lock_guard<std::recursive_mutex> g(m_); if (oc) *oc = opaqueCapacity_; if (ab) *ab = aggBase_; if (ac) *ac = aggCapacity_; return 0; }
    int32_t GetLastCullStats(int64_t out[4]) {           // newest stats ring entry the GPU wrote (host-visible, no wait)
        std::lock_guard<std::recursive_mutex> g(m_);
        out[0] = out[1] = out[2] = out[3] = 0;
        if (!statsRing_.mapped) return 1;
        const uint32_t* s = (const uint32_t*)statsRing_.mapped; int best = -1; uint32_t bestFrame = 0;
        for (uint32_t i = 0; i < FS_CULL_FRAME_RING; ++i) { uint32_t f = __atomic_load_n(&s[i * FS_CULL_STATS_WORDS + FS_CSTAT_FRAME], __ATOMIC_ACQUIRE); if (f && f <= lastCutFrame_ && (best < 0 || f > bestFrame)) { best = (int)i; bestFrame = f; } }
        if (best < 0) return 0;
        const uint32_t* e = s + best * FS_CULL_STATS_WORDS;
        out[0] = e[FS_CSTAT_PAGES_CULLED]; out[1] = e[FS_CSTAT_VISIBLE_SURFELS]; out[2] = e[FS_CSTAT_DRAW_RECORDS]; out[3] = lastCullUs_ + lastHzbUs_;
        if (bestFrame != lastStatsFrame_) {
            lastStatsFrame_ = bestFrame;
            CounterAdd(FS_CTR_VISIBLE_SURFELS, (int64_t)e[FS_CSTAT_VISIBLE_SURFELS]);
            CounterAdd(FS_CTR_RESIDENT_DRAWN, (int64_t)(e[FS_CSTAT_PAGES_TESTED] - e[FS_CSTAT_PAGES_CULLED]));
        }
        return 0;
    }
private:
    std::recursive_mutex m_;
    bool inited_ = false, deviceUp_ = false, pipesReady_ = false, bound_ = false, drawImported_ = false, forceRecut_ = true;
    Buffer frameRing_, work_, hzb_, aggScratch_, statsRing_, fallbackDraw_, fallbackArgs_, unityDraw_, unityArgs_;
    Pipeline pipes_[R_COUNT];
    void* regDraw_ = nullptr; void* regArgs_ = nullptr; uint32_t unityDrawBytes_ = 0; uint64_t regDrawVersion_ = 0, importedDrawVersion_ = 0;
    uint32_t opaqueCapacity_ = FS_DEFAULT_DRAW_CAPACITY - 65536, aggBase_ = FS_DEFAULT_DRAW_CAPACITY - 65536, aggCapacity_ = 65536, screenWorkBudget_ = 0;
    Mat4 viewL_, viewR_, projL_, projR_; float headPos_[3] = {0, 0, 0};
    DepthSource prev_, env_; uint64_t hzbBuiltVersion_ = 0;
    float fovealPx_ = 1.f, peripheralPx_ = 3.5f, marginDeg_ = 5.f; int32_t headroomUs_ = 0; int32_t renderMode_ = FS_RENDER_MODE_SCAN;
    uint32_t lastCutFrame_ = 0, lastStatsFrame_ = 0; float lastHead_[3] = {0, 0, 0}, lastFwd_[3] = {0, 0, -1}; uint64_t anchorSeq_ = 0, lastAnchorSeq_ = 0;
    int64_t lastCullUs_ = 0, lastHzbUs_ = 0, cullUsTotal_ = 0, hzbUsTotal_ = 0; uint64_t cullStages_ = 0, hzbStages_ = 0, recuts_ = 0; uint32_t lastStageFrame_ = 0;
    StageStat stages_[S_COUNT];
};

Render& R() { static Render r; return r; }
std::once_flag g_once;
void EnsureInitImpl() { std::call_once(g_once, []() { R().Init(); }); }
// Register with the executor at library load: the render pipelines are created by the warm-up thread before READY.
struct AutoInit { AutoInit() { EnsureInitImpl(); } } g_autoInit;

} // namespace

void EnsureInit() { EnsureInitImpl(); }
bool GetPrediction(PredictionInfo& out) { EnsureInitImpl(); return R().Prediction(out); }

} // namespace render
} // namespace fs

using fs::render::R; using fs::render::EnsureInit;

FS_API int32_t FsRender_RegisterBuffers(void* unityDrawRecordBuffer, uint32_t drawRecordBytes, void* unityIndirectArgsBuffer) { EnsureInit(); return R().RegisterBuffers(unityDrawRecordBuffer, drawRecordBytes, unityIndirectArgsBuffer); }
FS_API int32_t FsRender_SetView(const float viewL[16], const float projL[16], const float viewR[16], const float projR[16], const float headPos[3]) { EnsureInit(); if (!viewL || !projL || !viewR || !projR || !headPos) return 2; return R().SetView(viewL, projL, viewR, projR, headPos); }
FS_API int32_t FsRender_GetLastCullStats(int64_t out[4]) { EnsureInit(); if (!out) return 2; return R().GetLastCullStats(out); }
FS_API int32_t FsRender_SetPrevDepth(void* unityDepthTexture, uint32_t width, uint32_t height, uint32_t layers, const float viewL[16], const float projL[16], const float viewR[16], const float projR[16]) {
    EnsureInit(); if (unityDepthTexture && (!viewL || !projL || !viewR || !projR)) return 2; return R().SetPrevDepth(unityDepthTexture, width, height, layers, viewL, projL, viewR, projR); }
FS_API int32_t FsRender_SetEnvDepth(void* unityDepthTextureArray, uint32_t width, uint32_t height, const float poseL[16], const float poseR[16], const float fovL[4], const float fovR[4], float nearZ, float farZ, int64_t xrTimeNs) {
    EnsureInit(); if (unityDepthTextureArray && (!poseL || !poseR || !fovL || !fovR)) return 2; return R().SetEnvDepth(unityDepthTextureArray, width, height, poseL, poseR, fovL, fovR, nearZ, farZ, xrTimeNs); }
FS_API int32_t FsRender_SetLodPolicy(float fovealErrorPx, float peripheralErrorPx, float predictionMarginDeg, uint32_t screenWorkBudget) { EnsureInit(); return R().SetLodPolicy(fovealErrorPx, peripheralErrorPx, predictionMarginDeg, screenWorkBudget); }
FS_API int32_t FsRender_SetGpuHeadroomUs(int32_t headroomUs) { EnsureInit(); return R().SetHeadroom(headroomUs); }
FS_API int32_t FsRender_SetMode(int32_t mode) { EnsureInit(); return R().SetMode(mode); }
FS_API int32_t FsRender_GetTelemetryJson(char* out, int32_t capacity) { EnsureInit(); return R().TelemetryJson(out, capacity); }
FS_API int32_t FsRender_GetDrawLayout(uint32_t* opaqueCapacity, uint32_t* aggregateBase, uint32_t* aggregateCapacity) { EnsureInit(); return R().GetDrawLayout(opaqueCapacity, aggregateBase, aggregateCapacity); }
