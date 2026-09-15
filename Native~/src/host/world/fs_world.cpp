// FinalScan world module (C09R): segmented canonical pools, adaptive page index, staged single-writer fusion,
// sparse immutable COW readout, graphics-domain root publication, retirement-safe lifetime, residency and
// the dedicated reset transaction. Implements FsWorld_*, FsResidency_*, FsMeas_* on top of the executor.
//
// Division of labour: the GPU owns every canonical mutation (fusion epochs, one writer per surfel / cell per
// epoch) and every render-generation construction; the CPU owns residency (which logical pages are resident),
// slab / id bookkeeping between jobs, hash-mirror sync at epoch boundaries, publication hand-over to the
// graphics stream at FRAME_BEGIN and retirement (executor ledger). The CPU never reads a GPU buffer while a
// job that writes it is in flight; every value it consumes comes from host-visible memory after the fence.
//
// Epoch = one measurement frame: fuse job (SCAN class, 20 bounded dispatches) -> publish job (PUBLISH class,
// waits on the fuse timeline, 13 bounded dispatches) -> CPU queues the pending roots -> FRAME_BEGIN applies
// them on Unity's command stream -> retired generations are freed once scanner + graphics retirement is proven.
#include "../fs_cost_model.h"
#include <chrono>
#include "fs_world.h"
#include "fs_world_kernels.h"
#include "fs_page_hash.h"
#include "fs_pools.h"
#include "fs_residency_math.h"
#include "fs_synthetic.h"
#include "../measure/fs_meas_gpu.h"
#include "../measure/fs_meas_params.h"
#include "../../json_writer.h"
#include "../../log.h"
#include <algorithm>
#include <atomic>
#include <deque>
#include <memory>
#include <mutex>
#include <string.h>
#include <unordered_map>
#include <vector>

#define FS_API extern "C" __attribute__((visibility("default")))

namespace fs {
namespace world {
namespace {

constexpr uint32_t kLoadsPerTick = 2, kEvictsPerTick = 2, kCreatesPerTick = 4, kReleasesPerJob = 8;
constexpr uint32_t kSurfaceIdBase = 1u << 20;
constexpr float    kPredictHorizonS = 0.5f, kPredictMaxShiftM = 1.5f;
constexpr int      kResultOk = 0, kResultUnavailable = 1, kResultInvalid = 2, kResultBusy = 4;
constexpr uint32_t kPublishRingSlots = 16;
constexpr uint32_t kRetireKindRBlock = 0, kRetireKindRNode = 1, kRetireKindILeaf = 2, kRetireKindINode = 3;

struct KeyHasher { size_t operator()(const FsPageKey& k) const { return HashPageKey(k); } };
struct KeyEqual  { bool operator()(const FsPageKey& a, const FsPageKey& b) const { return KeyEq(a, b); } };

struct ColdPage { std::vector<FsSurfel> surfels; std::vector<FsSurfelEvidence> evidence; };   // sorted by cell (C14 store stand-in)
struct LogicalPage { FsPageKey key{}; uint32_t slot = FS_INDEX_NONE; ColdPage cold; bool hasCold = false; uint8_t zone = ZONE_OUTSIDE; };
enum PageLife : uint8_t { PAGE_FREE = 0, PAGE_ACTIVE = 1, PAGE_RELEASING = 2 };
struct PageState { PageLife life = PAGE_FREE; FsPageKey key{}; uint32_t generation = 0, lastTouched = 0; uint8_t zone = ZONE_OUTSIDE; std::vector<uint32_t> slabs; bool loadPending = false; };
struct JobStorage { std::vector<Dispatch> d; std::vector<uint8_t> push; };
struct PublishBatch { std::vector<FsPendingPublish> entries; std::vector<uint32_t> renderRetire; std::vector<uint32_t> releasedPages; };
struct EpochStats { int64_t fuseUs = 0, publishUs = 0, publishMaintUs = 0, publishLeavesUs = 0, publishLevelsUs = 0, sheetUs = 0; uint64_t epochs = 0; };

inline void AtomicStoreU32(uint32_t* p, uint32_t v) { __atomic_store_n(p, v, __ATOMIC_RELEASE); }
inline uint32_t AtomicLoadU32(const uint32_t* p) { return __atomic_load_n(p, __ATOMIC_ACQUIRE); }
inline void AtomicAndU32(uint32_t* p, uint32_t v) { __atomic_and_fetch(p, v, __ATOMIC_RELEASE); }

class World {
public:
    void Init() {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (inited_) return;
        inited_ = true;
        for (int a = 0; a < FS_MAX_ANCHORS; ++a) { memset(anchors_[a], 0, sizeof anchors_[a]); anchors_[a][0] = anchors_[a][5] = anchors_[a][10] = anchors_[a][15] = 1.f; }
        RegisterDeviceHook([this](bool up) { OnDevice(up); });
        RegisterWarmupStep("world.pipelines", [this]() { return CreatePipelines(); });
        RegisterSchedulerTick([this](uint32_t budgetUs) { Tick(budgetUs); });
        RegisterFrameBeginHook([this](VkCommandBuffer cmd, uint32_t frame) { OnFrameBegin(cmd, frame); });
        RegisterLeaseValidator([this](uint32_t slot, uint32_t gen) { return slot < pages_.size() && pages_[slot].generation == gen; });
        if (ExecReady()) OnDevice(true);
    }
    void Configure() {
        if (configured_) return;
        configured_ = true;
        const FsHostConfig* cfg = ExecConfig();
        pageCount_ = std::min<uint32_t>(cfg && cfg->residentPages ? cfg->residentPages : FS_DEFAULT_RESIDENT_PAGES, FS_MAX_PAGES);
        hashCap_ = Pow2Ceil(cfg && cfg->pageHashCapacity ? cfg->pageHashCapacity : FS_DEFAULT_PAGE_HASH_CAPACITY);
        surfelCap_ = (cfg && cfg->canonicalSurfels ? cfg->canonicalSurfels : FS_DEFAULT_CANONICAL_SURFELS) / FS_SLAB_SURFELS * FS_SLAB_SURFELS;
        leafCap_ = Pow2Ceil(cfg && cfg->indexLeaves ? cfg->indexLeaves : FS_DEFAULT_INDEX_LEAVES);
        nodeCap_ = Pow2Ceil(cfg && cfg->indexNodes ? cfg->indexNodes : FS_DEFAULT_INDEX_NODES);
        rblockCap_ = Pow2Ceil(cfg && cfg->renderBlocks ? cfg->renderBlocks : FS_DEFAULT_RENDER_BLOCKS);
        rnodeCap_ = Pow2Ceil(cfg && cfg->renderNodes ? cfg->renderNodes : FS_DEFAULT_RENDER_NODES);
        hash_.Init(hashCap_);
        pages_.assign(pageCount_, PageState{});
        Log("FS-WORLD configured: pages=%u surfels=%u (%u slabs) indexLeaves=%u indexNodes=%u renderBlocks=%u renderNodes=%u hash=%u",
            pageCount_, surfelCap_, surfelCap_ / FS_SLAB_SURFELS, leafCap_, nodeCap_, rblockCap_, rnodeCap_, hashCap_);
    }

    // ---- device lifecycle --------------------------------------------------------------------------
    void OnDevice(bool up) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (up == deviceUp_) return;
        if (!up) {
            DestroyArenas(); deviceUp_ = false; bound_ = false; liveBound_ = false;
            pipesReady_ = false; for (Pipeline& p : pipes_) p = Pipeline{}; for (Pipeline& p : ingestLive_) p = Pipeline{}; ingestCpu_ = Pipeline{}; histB_ = Pipeline{}; scatterB_ = Pipeline{};
            return;
        }
        Configure();
        if (!CreateArenas()) { LogError("FS-WORLD arena creation failed; world stays inactive"); DestroyArenas(); return; }
        deviceUp_ = true;
        EnsureBound();
    }
    uint64_t Bytes() const { return bytesTotal_; }
    bool Make(Buffer& b, VkDeviceSize size, bool host, const char* name, bool indirect = false) {
        VkBufferUsageFlags use = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_TRANSFER_SRC_BIT | (indirect ? VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT : 0);
        if (!CreateBuffer(b, size, use, host, name)) return false;
        if (host && !b.mapped) { LogError("FS-WORLD %s not mapped", name); return false; }
        bytesTotal_ += size;
        return true;
    }
    bool CreateArenas() {
        bytesTotal_ = 0;
        for (auto& p : logical_) { p.second.slot = FS_INDEX_NONE; }
        for (PageState& s : pages_) { uint32_t gen = s.generation + 1; s = PageState{}; s.generation = gen; }
        slabs_.Init(surfelCap_ / FS_SLAB_SURFELS);
        const VkDeviceSize pagesBytes = sizeof(FsPageDesc) * FS_MAX_PAGES + (VkDeviceSize)FS_MAX_PAGES * FS_PAGE_MAX_SLABS * 4;
        idxLeafBase_ = (uint32_t)pageCount_ * FS_CELLS_PER_PAGE;
        idxNodeBase_ = idxLeafBase_ + leafCap_ * 16;
        const VkDeviceSize indexWords = (VkDeviceSize)idxNodeBase_ + (VkDeviceSize)nodeCap_ * 8;
        const VkDeviceSize poolWords = FS_POOL_COUNT * FS_POOL_HEADER_WORDS + (VkDeviceSize)leafCap_ + nodeCap_ + rblockCap_ + rnodeCap_ + FS_TOPO_VERTEX_CAP + FS_SHEET_REC_CAP;
        const VkDeviceSize dirtyWords = (VkDeviceSize)FS_MAX_PAGES * 1024 + (VkDeviceSize)FS_MAX_PAGES * 160 + (VkDeviceSize)7 * FS_DIRTY_NODES_MAX + (VkDeviceSize)4 * FS_RELOC_MAX + (VkDeviceSize)2 * FS_DIRTY_RING_CAP;
        const VkDeviceSize sortWords = (VkDeviceSize)FS_SORT_BLOCKS_MAX * 256 + 256 + 2 * FS_TICK_MEAS_MAX + FS_SORT_BLOCKS_MAX + 64;
        bool ok = true;
        ok &= Make(buf_.pages, pagesBytes, true, "world.pages");
        ok &= Make(buf_.surfels, (VkDeviceSize)surfelCap_ * sizeof(FsSurfel), true, "world.surfels");
        ok &= Make(buf_.evidence, (VkDeviceSize)surfelCap_ * sizeof(FsSurfelEvidence), true, "world.evidence");
        ok &= Make(buf_.index, indexWords * 4, true, "world.index");
        ok &= Make(buf_.gctr, (VkDeviceSize)(FS_GCTR_COUNT + FS_T_WORDS) * 4, true, "world.gctr", true);
        ok &= Make(buf_.meas, (VkDeviceSize)FS_TICK_MEAS_MAX * sizeof(FsSurfaceMeasurement), false, "world.meas");
        ok &= Make(buf_.assocA, (VkDeviceSize)FS_TICK_MEAS_MAX * sizeof(FsAssociation), false, "world.assocA");
        ok &= Make(buf_.assocB, (VkDeviceSize)FS_TICK_MEAS_MAX * sizeof(FsAssociation), false, "world.assocB");
        ok &= Make(buf_.sort, sortWords * 4, true, "world.sort");
        ok &= Make(buf_.freeSpace, (VkDeviceSize)pageCount_ * FS_CELLS_PER_PAGE, true, "world.freeSpace");
        ok &= Make(buf_.pools, poolWords * 4, true, "world.pools");
        ok &= Make(buf_.dirty, dirtyWords * 4, true, "world.dirty");
        ok &= Make(buf_.render, (VkDeviceSize)rnodeCap_ * sizeof(FsRenderNode), false, "world.renderNodes");
        ok &= Make(buf_.rblocks, (VkDeviceSize)rblockCap_ * FS_RENDER_BLOCK_SURFELS * sizeof(FsSurfel), false, "world.renderBlocks");
        ok &= Make(buf_.rdir, (VkDeviceSize)pageCount_ * 37449 * 4, true, "world.rdir");
        ok &= Make(buf_.retire, (VkDeviceSize)FS_RETIRE_MAX * 4, true, "world.retire");
        ok &= Make(buf_.pending, (VkDeviceSize)FS_MAX_PAGES * sizeof(FsPendingPublish), true, "world.pending");
        ok &= Make(buf_.publishRing, (VkDeviceSize)kPublishRingSlots * FS_MAX_PAGES * sizeof(FsPendingPublish), true, "world.publishRing");
        ok &= Make(buf_.hash, (VkDeviceSize)hashCap_ * sizeof(FsPageHashEntry), true, "world.hash");
        ok &= Make(buf_.sheet, SheetWords() * 4, true, "world.sheet");
        ok &= Make(buf_.topo, TopoWords() * 4, true, "world.topology");
        ok &= Make(cpuRecords_, (VkDeviceSize)FS_TICK_MEAS_MAX * sizeof(FsSurfaceMeasurement), true, "world.cpuRecords");
        ok &= Make(cpuCounters_, 64, true, "world.cpuCounters");
        if (!ok) return false;
        buf_.pageCount = pageCount_;
        pagesGpu_ = (FsPageDesc*)buf_.pages.mapped; slabDir_ = (uint32_t*)((uint8_t*)buf_.pages.mapped + sizeof(FsPageDesc) * FS_MAX_PAGES);
        memset(buf_.pages.mapped, 0, pagesBytes);
        surfels_ = (FsSurfel*)buf_.surfels.mapped; evidence_ = (FsSurfelEvidence*)buf_.evidence.mapped;
        index_ = (uint32_t*)buf_.index.mapped; memset(index_, 0, (size_t)idxLeafBase_ * 4);
        gctr_ = (uint32_t*)buf_.gctr.mapped; memset(gctr_, 0, (FS_GCTR_COUNT + FS_T_WORDS) * 4); memset(gctrLast_, 0, sizeof gctrLast_);
        sort_ = (uint32_t*)buf_.sort.mapped;
        memset(buf_.freeSpace.mapped, 0, (size_t)pageCount_ * FS_CELLS_PER_PAGE);
        memset(buf_.dirty.mapped, 0, (size_t)dirtyWords * 4);
        rdir_ = (uint32_t*)buf_.rdir.mapped; memset(rdir_, 0xFF, (size_t)pageCount_ * 37449 * 4);
        retire_ = (uint32_t*)buf_.retire.mapped; pending_ = (FsPendingPublish*)buf_.pending.mapped; publishRing_ = (FsPendingPublish*)buf_.publishRing.mapped;
        memset(buf_.sheet.mapped, 0, (size_t)SheetWords() * 4);
        for (uint32_t h = 0; h < surfelCap_; ++h) { uint32_t* nw = (uint32_t*)buf_.sheet.mapped + (size_t)h * FS_SHEET_NODE_WORDS; for (uint32_t k = 0; k < FS_SHEET_K; ++k) nw[k] = FS_INDEX_NONE; nw[9] = FS_INDEX_NONE; }
        memset(buf_.topo.mapped, 0, (size_t)TopoWords() * 4);
        hashMirror_ = (FsPageHashEntry*)buf_.hash.mapped; hash_.Init(hashCap_); memcpy(hashMirror_, hash_.Data(), hash_.Bytes()); hashDirty_ = false;
        uint32_t* pools = (uint32_t*)buf_.pools.mapped;
        uint32_t ringBase = FS_POOL_COUNT * FS_POOL_HEADER_WORDS;
        const uint32_t caps[6] = {leafCap_, nodeCap_, rblockCap_, rnodeCap_, FS_TOPO_VERTEX_CAP, FS_SHEET_REC_CAP};
        for (uint32_t p = 0; p < 6; ++p) { rings_[p].Init(caps[p], pools + p * FS_POOL_HEADER_WORDS, pools + ringBase, ringBase); ringBase += caps[p]; }
        cpuRec_ = (FsSurfaceMeasurement*)cpuRecords_.mapped; cpuCtr_ = (uint32_t*)cpuCounters_.mapped; memset(cpuCtr_, 0, 64);
        fuseInFlight_ = publishInFlight_ = releaseInFlight_ = sheetInFlight_ = 0; pubStage_ = PUB_IDLE; publishWanted_ = false; cpuSlotReady_ = false; cpuSlotBusy_ = false; liveLease_ = 0;
        idBase_ = kSurfaceIdBase; tick_ = 0; toPublish_.clear(); publishSeq_ = 0;
        Log("FS-WORLD pools: %.1f MiB total (surfels %.1f, evidence %.1f, index %.1f, render nodes %.1f, render blocks %.1f, rdir %.1f)",
            bytesTotal_ / 1048576.0, buf_.surfels.size / 1048576.0, buf_.evidence.size / 1048576.0, buf_.index.size / 1048576.0, buf_.render.size / 1048576.0, buf_.rblocks.size / 1048576.0, buf_.rdir.size / 1048576.0);
        return true;
    }
    void DestroyArenas() {
        Buffer* all[] = {&buf_.pages, &buf_.surfels, &buf_.evidence, &buf_.index, &buf_.gctr, &buf_.meas, &buf_.assocA, &buf_.assocB, &buf_.sort, &buf_.freeSpace,
                         &buf_.pools, &buf_.dirty, &buf_.render, &buf_.rblocks, &buf_.rdir, &buf_.retire, &buf_.pending, &buf_.publishRing, &buf_.hash, &buf_.sheet, &buf_.topo, &cpuRecords_, &cpuCounters_};
        for (Buffer* b : all) if (b->buffer != VK_NULL_HANDLE) DestroyBuffer(*b);
        pagesGpu_ = nullptr; slabDir_ = nullptr; surfels_ = nullptr; evidence_ = nullptr; index_ = nullptr; gctr_ = nullptr; sort_ = nullptr; rdir_ = nullptr; retire_ = nullptr; pending_ = nullptr; publishRing_ = nullptr; hashMirror_ = nullptr; cpuRec_ = nullptr; cpuCtr_ = nullptr;
        bytesTotal_ = 0;
    }
    bool CreatePipelines() {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (pipesReady_) return true;
        auto make = [&](Pipeline& p, uint32_t k) {
            const KernelSpec& ks = kWorldKernels[k];
            VkDescriptorSetLayoutBinding b[12] = {};
            for (uint32_t i = 0; i < ks.bindingCount; ++i) { b[i].binding = ks.bindings[i]; b[i].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER; b[i].descriptorCount = 1; b[i].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT; }
            if (!CreateComputePipeline(p, ks.spirv, ks.words, ks.pushBytes, b, ks.bindingCount, ks.name)) { LogError("FS-WORLD pipeline %s failed", ks.name); return false; }
            return true;
        };
        for (uint32_t k = 0; k < K_COUNT; ++k) if (!make(pipes_[k], k)) return false;
        for (uint32_t r = 0; r < kLiveSlots; ++r) if (!make(ingestLive_[r], K_INGEST)) return false;
        if (!make(ingestCpu_, K_INGEST) || !make(histB_, K_SORT_HIST) || !make(scatterB_, K_SORT_SCATTER)) return false;
        pipesReady_ = true; bound_ = false; liveBound_ = false;
        EnsureBound();
        return true;
    }
    const Buffer* BufferFor(uint32_t binding) {
        switch (binding) {
        case B_MEAS: return &buf_.meas; case B_HASH: return &buf_.hash; case B_PAGES: return &buf_.pages; case B_SURFELS: return &buf_.surfels;
        case B_EVIDENCE: return &buf_.evidence; case B_INDEX: return &buf_.index; case B_GCTR: return &buf_.gctr; case B_ASSOC_A: return &buf_.assocA;
        case B_ASSOC_B: return &buf_.assocB; case B_SORT: return &buf_.sort; case B_FREESPACE: return &buf_.freeSpace; case B_POOLS: return &buf_.pools;
        case B_SHEET: return &buf_.sheet; case B_TOPO: return &buf_.topo;
        case B_DIRTY: return &buf_.dirty; case B_RENDER: return &buf_.render; case B_RBLOCKS: return &buf_.rblocks; case B_RDIR: return &buf_.rdir;
        case B_RETIRE: return &buf_.retire; case B_PENDING: return &buf_.pending; case B_MEAS_RING: return &cpuRecords_; case B_MEAS_CTR: return &cpuCounters_;
        default: return nullptr;
        }
    }
    bool BindAll(Pipeline& p, uint32_t k, const Buffer* overrideA = nullptr, const Buffer* overrideB = nullptr, const Buffer* ring = nullptr, const Buffer* ringCtr = nullptr, const Buffer* pendingOverride = nullptr) {
        const KernelSpec& ks = kWorldKernels[k];
        for (uint32_t i = 0; i < ks.bindingCount; ++i) {
            const uint32_t bnd = ks.bindings[i];
            const Buffer* b = BufferFor(bnd);
            if (bnd == B_ASSOC_A && overrideA) b = overrideA;
            if (bnd == B_ASSOC_B && overrideB) b = overrideB;
            if (bnd == B_MEAS_RING && ring) b = ring;
            if (bnd == B_MEAS_CTR && ringCtr) b = ringCtr;
            if (bnd == B_PENDING && pendingOverride) b = pendingOverride;
            if (!b || !BindBuffer(p, bnd, *b)) { LogError("FS-WORLD bind %s:%u failed", ks.name, bnd); return false; }
        }
        return true;
    }
    void EnsureBound() {
        if (bound_ || !pipesReady_ || !deviceUp_) return;
        for (uint32_t k = 0; k < K_COUNT; ++k) {
            if (k == K_PUBLISH_ROOTS) { if (!BindAll(pipes_[k], k, nullptr, nullptr, nullptr, nullptr, &buf_.publishRing)) return; continue; }
            if (!BindAll(pipes_[k], k)) return;
        }
        if (!BindAll(histB_, K_SORT_HIST, &buf_.assocB) || !BindAll(scatterB_, K_SORT_SCATTER, &buf_.assocB, &buf_.assocA)) return;
        if (!BindAll(ingestCpu_, K_INGEST)) return;
        bound_ = true;
        EnsureLiveBound();
    }
    void EnsureLiveBound() {
        if (liveBound_ || !bound_) return;
        for (uint32_t r = 0; r < kLiveSlots; ++r) {
            const meas::MeasGpuRing* ring = r < meas::MeasGpu_RingSlots() ? meas::MeasGpu_Ring(r) : nullptr;
            if (!ring || ring->records.buffer == VK_NULL_HANDLE) return;
            if (!BindAll(ingestLive_[r], K_INGEST, nullptr, nullptr, &ring->records, &ring->counters)) return;
        }
        liveBound_ = true;
    }
    bool Ready() const { return deviceUp_ && pipesReady_ && bound_ && ExecReady(); }
    bool JobsIdle() const { return fuseInFlight_ == 0 && publishInFlight_ == 0 && releaseInFlight_ == 0 && sheetInFlight_ == 0; }

    // ---- pages (residency: the CPU's job) ----------------------------------------------------------
    uint32_t AllocPageSlot() { for (uint32_t i = 0; i < pages_.size(); ++i) if (pages_[i].life == PAGE_FREE) return i; return FS_INDEX_NONE; }
    bool AddSlabs(uint32_t slot, uint32_t n) {
        PageState& s = pages_[slot];
        for (uint32_t i = 0; i < n; ++i) {
            if (s.slabs.size() >= FS_PAGE_MAX_SLABS) return false;
            uint32_t slab; if (!slabs_.Alloc(slab)) { slabStalls_++; return false; }
            slabDir_[slot * FS_PAGE_MAX_SLABS + s.slabs.size()] = slab; s.slabs.push_back(slab);
        }
        std::atomic_thread_fence(std::memory_order_release);
        AtomicStoreU32(&pagesGpu_[slot].slabCount, (uint32_t)s.slabs.size());
        return true;
    }
    // Creates an empty resident page for `key` (INNER pages ahead of the scan, or a cold page being loaded).
    uint32_t CreatePage(LogicalPage& p, uint8_t zone, uint32_t initialSlabs) {
        uint32_t slot = AllocPageSlot();
        if (slot == FS_INDEX_NONE) { stalls_++; return FS_INDEX_NONE; }
        PageState& s = pages_[slot];
        s.life = PAGE_ACTIVE; s.key = p.key; s.generation++; s.lastTouched = FrameIndex(); s.zone = zone; s.slabs.clear(); s.loadPending = false;
        FsPageDesc d{}; d.key = p.key; d.generation = s.generation; d.slabCount = 0; d.surfelCount = 0; d.shortfall = 0;
        d.cellDirBase = slot * FS_CELLS_PER_PAGE; d.freeSpaceBase = slot * FS_CELLS_PER_PAGE; d.renderRoot = FS_INDEX_NONE; d.renderCount = 0;
        d.pendingRoot = FS_INDEX_NONE; d.pendingCount = 0; d.flags = 0; d.lastTouchedTick = tick_;
        memset(index_ + (size_t)slot * FS_CELLS_PER_PAGE, 0, FS_CELLS_PER_PAGE * 4);
        memset(rdir_ + (size_t)slot * 37449, 0xFF, 37449 * 4);
        memset((uint8_t*)buf_.freeSpace.mapped + (size_t)slot * FS_CELLS_PER_PAGE, 0, FS_CELLS_PER_PAGE);
        pagesGpu_[slot] = d;
        std::atomic_thread_fence(std::memory_order_release);
        AddSlabs(slot, initialSlabs);
        if (!hash_.Insert(p.key, slot, s.generation)) CounterAdd(FS_CTR_PAGE_HASH_OVERFLOW, 1);
        hashDirty_ = true;
        p.slot = slot;
        pagesCreated_++;
        return slot;
    }
    // Keeps every active page's slab headroom ahead of its cursor (+ the shortfall the GPU reported).
    void TopUpSlabs() {
        for (uint32_t i = 0; i < pages_.size(); ++i) {
            PageState& s = pages_[i];
            if (s.life != PAGE_ACTIVE) continue;
            uint32_t cursor = AtomicLoadU32(&pagesGpu_[i].surfelCount), shortfall = AtomicLoadU32(&pagesGpu_[i].shortfall);
            uint32_t capacity = (uint32_t)s.slabs.size() * FS_SLAB_SURFELS;
            uint32_t want = std::min<uint32_t>(cursor, capacity) + shortfall + FS_PAGE_RESERVE_SLABS * FS_SLAB_SURFELS + std::max<uint32_t>(shortfall, 0);
            if (cursor > capacity) { AtomicStoreU32(&pagesGpu_[i].surfelCount, capacity); want += cursor - capacity; }   // overshoot: unusable, counted
            if (shortfall) { AtomicStoreU32(&pagesGpu_[i].shortfall, 0); shortfallTotal_ += shortfall; }
            if (want > capacity) AddSlabs(i, (want - capacity + FS_SLAB_SURFELS - 1) / FS_SLAB_SURFELS);
        }
    }
    LogicalPage& PageFor(const FsPageKey& key) {
        auto it = logical_.find(key);
        if (it == logical_.end()) { LogicalPage p; p.key = key; it = logical_.emplace(key, p).first; }
        return it->second;
    }
    // Cold copy (C14 stand-in): live surfels sorted by cell, read from host-visible slabs while no job runs.
    void SnapshotCold(uint32_t slot, ColdPage& cold) {
        PageState& s = pages_[slot];
        uint32_t cursor = std::min<uint32_t>(AtomicLoadU32(&pagesGpu_[slot].surfelCount), (uint32_t)s.slabs.size() * FS_SLAB_SURFELS);
        std::vector<std::pair<uint32_t, uint32_t>> order; order.reserve(cursor);
        for (uint32_t local = 0; local < cursor; ++local) {
            uint32_t h = s.slabs[local / FS_SLAB_SURFELS] * FS_SLAB_SURFELS + (local % FS_SLAB_SURFELS);
            if (surfels_[h].evidenceFlags & kFlagRemoved) continue;
            order.push_back({CellOf(DecodePos(surfels_[h].px), DecodePos(surfels_[h].py), DecodePos(surfels_[h].pz)), h});
        }
        std::stable_sort(order.begin(), order.end(), [](const auto& a, const auto& b) { return a.first < b.first; });
        cold.surfels.resize(order.size()); cold.evidence.resize(order.size());
        for (size_t i = 0; i < order.size(); ++i) { cold.surfels[i] = surfels_[order[i].second]; cold.evidence[i] = evidence_[order[i].second]; }
    }
    // Begins releasing a page: leaves the hash (mirror synced before the next epoch), the GPU release job frees
    // its index/render subtrees, the empty root goes through graphics publication, slabs are freed on retirement.
    void BeginRelease(uint32_t slot, bool keepCold) {
        PageState& s = pages_[slot];
        if (s.life != PAGE_ACTIVE) return;
        auto it = logical_.find(s.key);
        if (it != logical_.end()) {
            LogicalPage& p = it->second;
            if (keepCold) { SnapshotCold(slot, p.cold); p.hasCold = !p.cold.surfels.empty(); } else { p.cold = ColdPage{}; p.hasCold = false; }
            p.slot = FS_INDEX_NONE;
        }
        hash_.Erase(s.key); hashDirty_ = true;
        s.life = PAGE_RELEASING; releaseQueue_.push_back(slot);
        evictions_++;
    }
    void FinishRelease(uint32_t slot, uint32_t generation) {        // retirement callback (fs-sched): graphics passed the empty root
        std::lock_guard<std::recursive_mutex> g(m_);
        if (slot >= pages_.size() || pages_[slot].generation != generation) return;
        PageState& s = pages_[slot];
        for (uint32_t slab : s.slabs) {
            // E4.1R: the slab's handles will be reused by another page: no stale graph proposals or graph-dirty bits survive
            uint32_t* sh = (uint32_t*)buf_.sheet.mapped;
            if (sh) for (uint32_t k = 0; k < FS_SLAB_SURFELS; ++k) {
                const uint32_t h = slab * FS_SLAB_SURFELS + k;
                for (uint32_t w = 0; w < FS_SHEET_NODE_WORDS; ++w) sh[h * FS_SHEET_NODE_WORDS + w] = (w < FS_SHEET_K || w == 9) ? FS_INDEX_NONE : 0u;   // E6R: topology records of the recycled handles are guarded by SurfaceID and released by their neighbours' commits
                AtomicAndU32(sh + surfelCap_ * FS_SHEET_NODE_WORDS + (h >> 5), ~(1u << (h & 31u)));
            }
            slabs_.Free(slab);
        }
        s.slabs.clear(); s.life = PAGE_FREE; s.generation++;
        // C09R-E5R: pending dirty-ring entries of the released page die with its dedupe bits (dirty_take skips them)
        if (buf_.dirty.mapped) { uint32_t* dw = (uint32_t*)buf_.dirty.mapped; for (uint32_t w = 0; w < 1024u; ++w) AtomicStoreU32(dw + (size_t)slot * 1024u + w, 0u); }
        if (pagesGpu_) { FsPageDesc d{}; d.generation = 0; d.renderRoot = FS_INDEX_NONE; d.pendingRoot = FS_INDEX_NONE; pagesGpu_[slot] = d; }
        pagesReleased_++;
    }

    // ---- job builders ------------------------------------------------------------------------------
    static void AddDispatch(JobStorage& js, const Pipeline& p, uint32_t gx, uint32_t gy, const void* push, uint32_t pushBytes, const Buffer* args = nullptr, uint32_t argsWord = 0) {
        size_t off = js.push.size(); if (pushBytes) js.push.insert(js.push.end(), (const uint8_t*)push, (const uint8_t*)push + pushBytes);
        Dispatch d; d.pipeline = &p; d.gx = gx; d.gy = gy; d.push = pushBytes ? (const void*)(uintptr_t)off : nullptr; d.pushBytes = pushBytes;
        d.argsBuffer = args; d.argsOffset = (VkDeviceSize)argsWord * 4; js.d.push_back(d);
    }
    static void FixPushPointers(JobStorage& js) { for (Dispatch& d : js.d) if (d.pushBytes) d.push = js.push.data() + (uintptr_t)d.push; }
    void PrepareEpochWords(uint32_t tick) {          // standalone publish / release / erase jobs (no ingest in the job)
        uint32_t* T = gctr_ + FS_GCTR_COUNT;
        T[FS_T_COUNT] = 0; T[FS_T_SORTED] = 0; T[FS_T_SEG_MATCHED] = 0; T[FS_T_SEG_UNMATCHED] = 0; T[FS_T_NEW_TOTAL] = 0; T[FS_T_DIRTY_COUNT] = 0;
        T[FS_T_RETIRE_COUNT] = 0; T[FS_T_PENDING_COUNT] = 0; T[FS_T_COW_COUNT] = 0;
        T[FS_T_ARGS_MEAS] = 0; T[FS_T_ARGS_MEAS + 1] = 1; T[FS_T_ARGS_MEAS + 2] = 1; T[FS_T_ARGS_DIRTY] = 0; T[FS_T_ARGS_DIRTY + 1] = 1; T[FS_T_ARGS_DIRTY + 2] = 1;
        for (uint32_t l = 0; l < 6; ++l) { T[FS_T_LEVEL_ARGS + 4 * l] = 0; T[FS_T_LEVEL_ARGS + 4 * l + 1] = 1; T[FS_T_LEVEL_ARGS + 4 * l + 2] = 1; T[FS_T_LEVEL_ARGS + 4 * l + 3] = 0; }
        T[FS_T_IDX_LEAF_BASE] = idxLeafBase_; T[FS_T_IDX_NODE_BASE] = idxNodeBase_; T[FS_T_ID_BASE] = idBase_; T[FS_T_TICK] = tick; T[FS_T_PAGE_COUNT] = pageCount_;
        std::atomic_thread_fence(std::memory_order_release);
    }
    // E4.1R sheet buffer: nodes[surfelCap x 8] | dirty mask[surfelCap / 32] | dirty ring[FS_SHEET_RING_CAP] | batch deltas[FS_SHEET_BATCH_MAX x 4]
    VkDeviceSize TopoWords() const { return (VkDeviceSize)FS_TOPO_VERTEX_CAP * FS_TOPO_VERTEX_WORDS + (VkDeviceSize)FS_SHEET_REC_CAP * FS_SHEET_REC_WORDS + (VkDeviceSize)FS_SHEET_BATCH_MAX * FS_TOPO_DELTA_WORDS + FS_TOPO_FREE_MAX + (VkDeviceSize)FS_TEMPORAL_TARGETS * FS_TEMPORAL_TARGET_WORDS; }
    uint64_t TopoFreeBase() const { return (uint64_t)FS_TOPO_VERTEX_CAP * FS_TOPO_VERTEX_WORDS + (uint64_t)FS_SHEET_REC_CAP * FS_SHEET_REC_WORDS + (uint64_t)FS_SHEET_BATCH_MAX * FS_TOPO_DELTA_WORDS; }
    VkDeviceSize SheetWords() const { return (VkDeviceSize)surfelCap_ * FS_SHEET_NODE_WORDS + (surfelCap_ / 32 + 1) + FS_SHEET_RING_CAP + (VkDeviceSize)FS_SHEET_BATCH_MAX * 8 + (VkDeviceSize)FS_TICK_MEAS_MAX * 8 + FS_SHEET_RING_CAP; }   // + C09R-E5R sheet ring enqueue ticks
    void BeforeJobs() {                              // epoch boundary: slabs, rings, hash mirror (no job in flight)
        TopUpSlabs();
        for (IdRing& r : rings_) r.Refill();
        if (hashDirty_) { memcpy(hashMirror_, hash_.Data(), hash_.Bytes()); hashDirty_ = false; }
        gctr_[FS_GCTR_COUNT + FS_T_SHEET_MASK_BASE] = surfelCap_ * FS_SHEET_NODE_WORDS;
        gctr_[FS_GCTR_COUNT + FS_T_SHEET_RING_BASE] = surfelCap_ * FS_SHEET_NODE_WORDS + surfelCap_ / 32 + 1;
        std::atomic_thread_fence(std::memory_order_release);
    }
    // Fuse job: ingest -> associate -> free space -> 4 x (hist, scan, scatter) -> reduce -> cluster count -> prefix -> carry -> cluster write -> refinement sites.
    bool SubmitFuse(const Pipeline& ingest, uint32_t phase, uint32_t stride, uint32_t maxCount, int32_t anchorId, bool freeSpace, const float eye0[3], const float eye1[3], uint64_t waitFrameEnd, std::function<void(bool)> onDone) {
        auto js = std::make_shared<JobStorage>();
        const Buffer* G = &buf_.gctr;
        const uint32_t tick = ++tick_;
        PushIngest pin{maxCount, tick, idBase_, idxLeafBase_, idxNodeBase_, phase, pageCount_, stride};
        PushAssoc pa{hashCap_ - 1, anchorId};
        PushFreeSpace pf{hashCap_ - 1, anchorId, freeSpace ? FS_FUSE_FLAG_FREE_SPACE : 0u, 0, {eye0[0], eye0[1], eye0[2], 0}, {eye1[0], eye1[1], eye1[2], 0}};
        {   // E6R: eye origins of the epoch for view-sector evidence (host-visible T words, written before submit)
            uint32_t* T = gctr_ + FS_GCTR_COUNT;
            for (int k = 0; k < 3; ++k) { memcpy(&T[FS_T_EYE0 + k], &eye0[k], 4); memcpy(&T[FS_T_EYE1 + k], &eye1[k], 4); }
            std::atomic_thread_fence(std::memory_order_release);
        }
        PushShift sh[4] = {{0}, {8}, {16}, {24}};
        AddDispatch(*js, ingest, (std::min<uint32_t>(maxCount, FS_TICK_MEAS_MAX) + FS_WG_SMALL - 1) / FS_WG_SMALL, 1, &pin, sizeof pin);
        AddDispatch(*js, pipes_[K_ASSOC], 1, 1, &pa, sizeof pa, G, FS_GCTR_COUNT + FS_T_ARGS_MEAS);
        AddDispatch(*js, pipes_[K_FREESPACE], 1, 1, &pf, sizeof pf, G, FS_GCTR_COUNT + FS_T_ARGS_MEAS);
        for (uint32_t p = 0; p < 4; ++p) {
            const bool fromA = (p & 1) == 0;
            AddDispatch(*js, fromA ? pipes_[K_SORT_HIST] : histB_, 1, 1, &sh[p], sizeof sh[p], G, FS_GCTR_COUNT + FS_T_ARGS_SORTBLK);
            AddDispatch(*js, pipes_[K_SORT_SCAN], 1, 1, nullptr, 0);
            AddDispatch(*js, fromA ? pipes_[K_SORT_SCATTER] : scatterB_, 1, 1, &sh[p], sizeof sh[p], G, FS_GCTR_COUNT + FS_T_ARGS_SORTBLK);
        }
        AddDispatch(*js, pipes_[K_REDUCE], 1, 1, nullptr, 0, G, FS_GCTR_COUNT + FS_T_ARGS_MEAS);
        // C09R-E5R dispatch domain = the epoch's real size: whole 256-entry blocks covering maxCount (cluster count writes 0 past
        // the count inside them), the carry sums only those blocks. No kernel after ingest walks the 65 536 ABI capacity.
        const uint32_t blocks = std::max<uint32_t>(1u, (std::min<uint32_t>(maxCount, FS_TICK_MEAS_MAX) + 255u) / 256u);
        PushBlocks pbk{blocks};
        AddDispatch(*js, pipes_[K_CLUSTER_COUNT], blocks * 256u / FS_WG_SMALL, 1, nullptr, 0);
        AddDispatch(*js, pipes_[K_PREFIX], blocks, 1, nullptr, 0);
        AddDispatch(*js, pipes_[K_PREFIX_CARRY], 1, 1, &pbk, sizeof pbk);
        AddDispatch(*js, pipes_[K_CLUSTER_WRITE], 1, 1, nullptr, 0, G, FS_GCTR_COUNT + FS_T_ARGS_MEAS);
        PushHash phr{hashCap_ - 1};
        AddDispatch(*js, pipes_[K_SHEET_REFINE], 1, 1, &phr, sizeof phr);   // E4.1C refine: site insertion at the maximum residual (cross-page coverage)
        AddDispatch(*js, pipes_[K_RELOCATE], 1, 1, nullptr, 0);         // C09R-E5: the fuse epoch's own relocations (a publication chain may be mid-way)
        FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_SCAN; jd.name = "world.fuse"; jd.dispatches = js->d.data(); jd.dispatchCount = (uint32_t)js->d.size(); jd.waitFrameEndValue = waitFrameEnd;
        jd.onRetired = [this, js, onDone](bool ok, uint64_t s, uint64_t e) {
            std::lock_guard<std::recursive_mutex> g(m_);
            fuseInFlight_--;
            if (ok) {
                stats_.fuseUs = e > s ? (int64_t)((e - s) / 1000) : 0;
                const uint32_t items = AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_COUNT);
                AfterJob(false);
                fuseCost_.Add(items, stats_.fuseUs); Charge(ST_FUSE, stats_.fuseUs);
                epochMeasCap_ = fuseCost_.Batch(ClassQuantumUs(FS_JOB_SCAN), FS_EPOCH_MEAS_MIN, FS_TICK_MEAS_MAX, 4096u);
            }
            onDone(ok);
        };
        if (!SubmitWorldJob(jd)) { tick_--; return false; }
        fuseInFlight_++;
        return true;
    }
    // C09R-E5R publication chain (CPU-driven continuation, FRONT immutable until its last job):
    //   MAINT  : [page load] -> dirty_take (a cost-model batch of the persistent dirty-cell ring) -> maintenance -> relocation
    //   LEAVES : publish_leaves over [offset, offset + chunk) of the taken list, repeated until the list is done
    //   LEVELS : render levels 1..5 -> pending roots -> graphics-owned FRONT publication (AfterJob)
    // Fuse and sheet jobs may run between chain jobs (they never touch the taken list, the level lists or the pending list; their
    // marks go to the ring for the next publication). Each stage has its own cost model (fixed + per item).
    enum PubStage : uint32_t { PUB_IDLE = 0, PUB_LEAVES, PUB_LEVELS };
    void PreparePublicationWords() {
        uint32_t* T = gctr_ + FS_GCTR_COUNT;
        T[FS_T_DIRTY_COUNT] = 0; T[FS_T_ARGS_DIRTY] = 0; T[FS_T_ARGS_DIRTY + 1] = 1; T[FS_T_ARGS_DIRTY + 2] = 1; T[FS_T_COW_COUNT] = 0;
        T[FS_T_ARGS_COW] = 0; T[FS_T_ARGS_COW + 1] = 1; T[FS_T_ARGS_COW + 2] = 1; T[FS_T_ARGS_PENDING] = 0; T[FS_T_ARGS_PENDING + 1] = 1; T[FS_T_ARGS_PENDING + 2] = 1;
        for (uint32_t l = 0; l < 6; ++l) { T[FS_T_LEVEL_ARGS + 4 * l] = 0; T[FS_T_LEVEL_ARGS + 4 * l + 1] = 1; T[FS_T_LEVEL_ARGS + 4 * l + 2] = 1; T[FS_T_LEVEL_ARGS + 4 * l + 3] = 0; }
        T[FS_T_IDX_LEAF_BASE] = idxLeafBase_; T[FS_T_IDX_NODE_BASE] = idxNodeBase_; T[FS_T_PAGE_COUNT] = pageCount_;
        std::atomic_thread_fence(std::memory_order_release);
    }
    uint32_t DirtyPending() const { return AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_DIRTY_TAIL) - AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_DIRTY_HEAD); }
    uint32_t DirtyEntryTick(uint32_t t) const { return ((uint32_t*)buf_.dirty.mapped)[DirtyRingWord() + 2u * (t & (FS_DIRTY_RING_CAP - 1u)) + 1u]; }
    static constexpr uint64_t DirtyRingWord() { return (uint64_t)FS_MAX_PAGES * 1024 + (uint64_t)FS_MAX_PAGES * 160 + (uint64_t)7 * FS_DIRTY_NODES_MAX + (uint64_t)4 * FS_RELOC_MAX; }
    bool SubmitPubMaint(std::vector<std::pair<uint32_t, bool>> loads) {
        PreparePublicationWords();
        auto js = std::make_shared<JobStorage>();
        const Buffer* G = &buf_.gctr;
        const uint32_t head = AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_DIRTY_HEAD), pending = DirtyPending();
        const uint32_t take = std::min<uint32_t>(pending, maintCost_.Batch(ClassQuantumUs(FS_JOB_PUBLISH), FS_PUB_DIRTY_MIN, FS_DIRTY_CELLS_MAX, 1024u));
        epochDirtyCap_ = take;
        // age receipt: enqueue ticks of the batch (evenly sampled), resolved to milliseconds at the FRONT commit
        pubAgeTicks_.clear();
        for (uint32_t k = 0; k < std::min<uint32_t>(take, 32u); ++k) pubAgeTicks_.push_back(DirtyEntryTick(head + (uint32_t)((uint64_t)k * take / std::min<uint32_t>(take, 32u))));
        AtomicStoreU32(gctr_ + FS_GCTR_COUNT + FS_T_DIRTY_HEAD, head + take);
        std::atomic_thread_fence(std::memory_order_release);
        PushTake pt{head, take, pageCount_};
        for (auto& l : loads) { PushPage pp{l.first}; AddDispatch(*js, pipes_[K_PAGE_LOAD], FS_CELLS_PER_PAGE / FS_WG_SMALL, 1, &pp, sizeof pp); }
        AddDispatch(*js, pipes_[K_DIRTY_TAKE], 1, 1, &pt, sizeof pt);
        AddDispatch(*js, pipes_[K_MAINT_APPLY], 1, 1, nullptr, 0, G, FS_GCTR_COUNT + FS_T_ARGS_DIRTY);
        AddDispatch(*js, pipes_[K_RELOCATE], 1, 1, nullptr, 0);         // C09R-E4: index follows geometry (maintenance merges) before the leaves read it
        FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_PUBLISH; jd.name = "world.publish.maint"; jd.dispatches = js->d.data(); jd.dispatchCount = (uint32_t)js->d.size();
        const bool hasLoads = !loads.empty();
        jd.onRetired = [this, js, hasLoads](bool ok, uint64_t s, uint64_t e) {
            std::lock_guard<std::recursive_mutex> g(m_);
            publishInFlight_--;
            if (!ok) { pubStage_ = PUB_IDLE; return; }
            stats_.publishMaintUs = e > s ? (int64_t)((e - s) / 1000) : 0;
            pubTotal_ = AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_DIRTY_COUNT); pubDone_ = 0;
            if (!hasLoads) maintCost_.Add(pubTotal_, stats_.publishMaintUs);    // a page load is not per-cell work
            Charge(ST_PUB, stats_.publishMaintUs);
            FoldGpuCounters();
            pubStage_ = pubTotal_ ? PUB_LEAVES : PUB_IDLE;
            if (!pubTotal_) FinishPublication();
        };
        if (!SubmitWorldJob(jd)) { AtomicStoreU32(gctr_ + FS_GCTR_COUNT + FS_T_DIRTY_HEAD, head); return false; }
        publishInFlight_++; pubStage_ = PUB_LEAVES; pubTotal_ = 0; pubDone_ = 0; pubStartNs_ = NowNs();
        for (auto& l : loads) pages_[l.first].loadPending = false;
        return true;
    }
    bool SubmitPubLeaves() {
        auto js = std::make_shared<JobStorage>();
        const uint32_t chunk = leavesCost_.Batch(ClassQuantumUs(FS_JOB_PUBLISH), FS_PUB_LEAVES_MIN, FS_DIRTY_CELLS_MAX, 256u);
        const uint32_t count = std::min<uint32_t>(chunk, pubTotal_ - pubDone_);
        PushRange pr{pubDone_, count};
        AddDispatch(*js, pipes_[K_PUBLISH_LEAVES], (count + FS_WG_SMALL - 1) / FS_WG_SMALL, 1, &pr, sizeof pr);
        FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_PUBLISH; jd.name = "world.publish.leaves"; jd.dispatches = js->d.data(); jd.dispatchCount = (uint32_t)js->d.size();
        jd.onRetired = [this, js, count](bool ok, uint64_t s, uint64_t e) {
            std::lock_guard<std::recursive_mutex> g(m_);
            publishInFlight_--;
            if (!ok) return;                                                     // same chunk again
            stats_.publishLeavesUs = e > s ? (int64_t)((e - s) / 1000) : 0;
            leavesCost_.Add(count, stats_.publishLeavesUs); Charge(ST_PUB, stats_.publishLeavesUs);
            pubDone_ += count; pubChunks_++; lastLeavesChunk_ = count;
            if (pubDone_ >= pubTotal_) pubStage_ = PUB_LEVELS;
        };
        if (!SubmitWorldJob(jd)) return false;
        publishInFlight_++;
        return true;
    }
    bool SubmitPubLevels() {
        auto js = std::make_shared<JobStorage>();
        const Buffer* G = &buf_.gctr;
        for (uint32_t lv = 1; lv <= FS_RENDER_TREE_DEPTH; ++lv) { PushLevel pl{lv}; AddDispatch(*js, pipes_[K_PUBLISH_LEVEL], 1, 1, &pl, sizeof pl, G, FS_GCTR_COUNT + FS_T_LEVEL_ARGS + 4 * lv); }
        FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_PUBLISH; jd.name = "world.publish.levels"; jd.dispatches = js->d.data(); jd.dispatchCount = (uint32_t)js->d.size();
        jd.onRetired = [this, js](bool ok, uint64_t s, uint64_t e) {
            std::lock_guard<std::recursive_mutex> g(m_);
            publishInFlight_--;
            if (!ok) return;                                                     // levels again
            stats_.publishLevelsUs = e > s ? (int64_t)((e - s) / 1000) : 0;
            stats_.publishUs = stats_.publishMaintUs + stats_.publishLeavesUs + stats_.publishLevelsUs;
            Charge(ST_PUB, stats_.publishLevelsUs);
            FinishPublication();
        };
        if (!SubmitWorldJob(jd)) return false;
        publishInFlight_++;
        return true;
    }
    void FinishPublication() {
        stats_.epochs++;
        AfterJob(true);
        pubStage_ = PUB_IDLE;
        const int64_t now = NowNs();
        for (uint32_t t : pubAgeTicks_) { const float a = WorkAgeMs(t, now); pubAge_.Add(a); if (a > (float)FS_SCHED_PUBLISH_DEADLINE_MS) deadlineMiss_[ST_PUB]++; }        // change -> FRONT commit request (graphics publishes at its next frame)
        pubAgeTicks_.clear();
        pubChainMs_.Add((float)((now - pubStartNs_) / 1e6));
    }
    // C09R-E5R surface complex: its own bounded job (graph -> fit -> apply -> contraction -> relocation) chosen by the deadline scheduler;
    // batch from its cost model (SCAN class quantum).
    bool SubmitSheet() {
        auto js = std::make_shared<JobStorage>();
        const Buffer* G = &buf_.gctr;
        const uint32_t head = AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_SHEET_HEAD), pending = AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_SHEET_TAIL) - head;
        const uint32_t batch = sheetCost_.Batch(ClassQuantumUs(FS_JOB_SCAN), FS_SHEET_BATCH_MIN, FS_SHEET_BATCH_MAX, 1024u);
        const uint32_t n = std::min(pending, batch);
        sheetAgeTicks_.clear();
        for (uint32_t k = 0; k < std::min<uint32_t>(n, 32u); ++k) sheetAgeTicks_.push_back(SheetEntryTick(head + (uint32_t)((uint64_t)k * n / std::min<uint32_t>(n, 32u))));
        PushHash ph{hashCap_ - 1}; PushBatch pb{batch};
        AddDispatch(*js, pipes_[K_SHEET_BEGIN], 1, 1, &pb, sizeof pb);
        AddDispatch(*js, pipes_[K_SHEET_GRAPH], 1, 1, &ph, sizeof ph, G, FS_GCTR_COUNT + FS_T_ARGS_SHEET);
        AddDispatch(*js, pipes_[K_SHEET_FIT], 1, 1, nullptr, 0, G, FS_GCTR_COUNT + FS_T_ARGS_SHEET);
        AddDispatch(*js, pipes_[K_SHEET_APPLY], 1, 1, nullptr, 0, G, FS_GCTR_COUNT + FS_T_ARGS_SHEET);
        AddDispatch(*js, pipes_[K_SHEET_CONTRACT], 1, 1, nullptr, 0);   // E4.1C coarsen: sequential edge contraction of the batch (E6R matching)
        AddDispatch(*js, pipes_[K_TOPO_FACES], 1, 1, nullptr, 0, G, FS_GCTR_COUNT + FS_T_ARGS_SHEET);   // E6R shared faces (owner derivation)
        AddDispatch(*js, pipes_[K_TOPO_COMMIT], 1, 1, nullptr, 0);      // E6R persistent topology commit (sheets, unions, cuts, targets)
        AddDispatch(*js, pipes_[K_RELOCATE], 1, 1, nullptr, 0);
        FixPushPointers(*js);
        { uint32_t* T = gctr_ + FS_GCTR_COUNT; T[FS_T_TOPO_GEN] = ++topoGen_; T[FS_T_TARGET_COUNT] = 0; T[FS_T_TOPO_FREE_COUNT] = 0; std::atomic_thread_fence(std::memory_order_release); }
        JobDesc jd; jd.cls = FS_JOB_SCAN; jd.name = "world.sheet"; jd.dispatches = js->d.data(); jd.dispatchCount = (uint32_t)js->d.size();
        jd.onRetired = [this, js](bool ok, uint64_t s, uint64_t e) {
            std::lock_guard<std::recursive_mutex> g(m_);
            sheetInFlight_--;
            if (!ok) return;
            stats_.sheetUs = e > s ? (int64_t)((e - s) / 1000) : 0;
            const uint32_t items = AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_SHEET_COUNT);
            sheetCost_.Add(items, stats_.sheetUs); Charge(ST_SHEET, stats_.sheetUs);
            FoldGpuCounters();
            const int64_t now = NowNs();
            for (uint32_t t : sheetAgeTicks_) { const float a = WorkAgeMs(t, now); sheetAge_.Add(a); if (a > (float)FS_SCHED_SHEET_DEADLINE_MS) deadlineMiss_[ST_SHEET]++; }
            sheetAgeTicks_.clear();
            sheetJobs_++; lastSheetBatch_ = items;
            CollectTopology();
        };
        if (!SubmitWorldJob(jd)) return false;
        sheetInFlight_++;
        return true;
    }
    // E6R: released topology / sheet ids back to their rings (canonical side, scanner-only) and the C11R2 targets to the measurement front-end
    void CollectTopology() {
        const uint32_t* tp = (uint32_t*)buf_.topo.mapped;
        const uint32_t nFree = std::min<uint32_t>(AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_TOPO_FREE_COUNT), FS_TOPO_FREE_MAX);
        const uint64_t fb = TopoFreeBase();
        for (uint32_t k = 0; k < nFree; ++k) { const uint32_t v = tp[fb + k]; rings_[(v >> 30) == 0 ? 4 : 5].Free(v & 0x3FFFFFFFu); }
        const uint32_t nT = std::min<uint32_t>(AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_TARGET_COUNT), FS_TEMPORAL_TARGETS);
        if (nT) {
            std::vector<meas::RefineTarget> targets(nT);
            const uint64_t tb = fb + FS_TOPO_FREE_MAX;
            float worldFromAnchor[16]; memcpy(worldFromAnchor, anchors_[std::max(0, std::min(scanAnchor_, (int32_t)FS_MAX_ANCHORS - 1))], 64);
            for (uint32_t k = 0; k < nT; ++k) {
                const uint32_t* w = tp + tb + (uint64_t)k * FS_TEMPORAL_TARGET_WORDS;
                float p[3]; memcpy(p, w, 12);
                float n[3]; { float oct[2] = {((w[3] & 0xFFFFu) / 65535.f) * 2.f - 1.f, (((w[3] >> 16) & 0xFFFFu) / 65535.f) * 2.f - 1.f}; float z = 1.f - fabsf(oct[0]) - fabsf(oct[1]);
                    if (z < 0) { float ox = (1.f - fabsf(oct[1])) * (oct[0] >= 0 ? 1.f : -1.f), oy = (1.f - fabsf(oct[0])) * (oct[1] >= 0 ? 1.f : -1.f); oct[0] = ox; oct[1] = oy; }
                    float l = sqrtf(oct[0] * oct[0] + oct[1] * oct[1] + z * z); n[0] = oct[0] / l; n[1] = oct[1] / l; n[2] = z / l; }
                meas::RefineTarget& t = targets[k];
                for (int c = 0; c < 3; ++c) { t.pos[c] = worldFromAnchor[c] * p[0] + worldFromAnchor[4 + c] * p[1] + worldFromAnchor[8 + c] * p[2] + worldFromAnchor[12 + c]; t.normal[c] = worldFromAnchor[c] * n[0] + worldFromAnchor[4 + c] * n[1] + worldFromAnchor[8 + c] * n[2]; }
                memcpy(&t.sigmaN, w + 4, 4); t.surfaceId = w[5]; t.support = w[6];
            }
            meas::MeasGpu_SetRefineTargets(targets.data(), nT);
        }
    }
    uint32_t SheetEntryTick(uint32_t t) const {
        const uint64_t base = (uint64_t)AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_SHEET_RING_BASE) + FS_SHEET_RING_CAP + (uint64_t)FS_SHEET_BATCH_MAX * 8 + (uint64_t)FS_TICK_MEAS_MAX * 8;
        return ((uint32_t*)buf_.sheet.mapped)[base + (t & (FS_SHEET_RING_CAP - 1u))];
    }
    uint32_t SheetPendingCount() const { return AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_SHEET_TAIL) - AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_SHEET_HEAD); }
    // ---- C09R-E5R deadline / deficit scheduler bookkeeping -----------------------------------------------------------------------
    enum SchedStage : uint32_t { ST_FUSE = 0, ST_PUB, ST_SHEET, ST_COUNT };
    static int64_t NowNs() { return std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch()).count(); }
    // E6R scheduler clock: every world job (any class) takes the next work serial; the GPU rings store the serial of the job that
    // enqueued an element and the host maps serial -> steady-clock ns. The fusion tick is no clock authority any more.
    bool SubmitWorldJob(JobDesc& jd) {
        const uint32_t serial = ++workSerial_;
        AtomicStoreU32(gctr_ + FS_GCTR_COUNT + FS_T_WORK_SERIAL, serial);
        std::atomic_thread_fence(std::memory_order_release);
        workTimeNs_[serial & (FS_SCHED_SERIAL_WINDOW - 1u)] = NowNs(); workTimeSerial_[serial & (FS_SCHED_SERIAL_WINDOW - 1u)] = serial;
        return SubmitJob(jd);
    }
    float WorkAgeMs(uint32_t serial, int64_t now) const {
        const uint32_t k = serial & (FS_SCHED_SERIAL_WINDOW - 1u);
        if (workTimeSerial_[k] != serial || workTimeNs_[k] == 0) return (float)FS_SCHED_AGE_UNKNOWN_MS;
        return (float)((now - workTimeNs_[k]) / 1e6);
    }
    uint32_t ClassQuantumUs(FsJobClass cls) { int64_t st[8]; return ExecClassStats(cls, st) && st[7] > 0 ? (uint32_t)st[7] : 2000u; }
    // deficit round robin over GPU time: every charged job credits all stages by their share and debits the stage that ran
    void Charge(SchedStage st, int64_t us) {
        static constexpr double kShare[ST_COUNT] = {FS_SCHED_SHARE_FUSE, FS_SCHED_SHARE_PUBLISH, FS_SCHED_SHARE_SHEET};
        for (uint32_t k = 0; k < ST_COUNT; ++k) deficit_[k] = std::min(deficit_[k] + kShare[k] * (double)us, (double)FS_SCHED_DEFICIT_CAP_US);
        deficit_[st] -= (double)us;
        schedJobs_[st]++;
    }
    bool SubmitRelease(const std::vector<uint32_t>& slots) {
        auto js = std::make_shared<JobStorage>();
        for (uint32_t slot : slots) { PushPage pp{slot}; AddDispatch(*js, pipes_[K_PAGE_RELEASE], FS_CELLS_PER_PAGE / FS_WG_SMALL, 1, &pp, sizeof pp); }
        FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_PUBLISH; jd.name = "world.release"; jd.dispatches = js->d.data(); jd.dispatchCount = (uint32_t)js->d.size();
        auto captured = std::make_shared<std::vector<uint32_t>>(slots);
        jd.onRetired = [this, js, captured](bool ok, uint64_t, uint64_t) {
            std::lock_guard<std::recursive_mutex> g(m_);
            releaseInFlight_--;
            PublishBatch batch;
            if (ok) CollectRetire(batch.renderRetire);
            for (uint32_t slot : *captured) { FsPendingPublish pp; pp.pageSlot = slot; pp.root = FS_INDEX_NONE; pp.count = 0; pp.generation = pages_[slot].generation; batch.entries.push_back(pp); batch.releasedPages.push_back(slot); }
            toPublish_.push_back(std::move(batch));
        };
        if (!SubmitWorldJob(jd)) return false;
        releaseInFlight_++;
        return true;
    }
    bool SubmitErase(const float c[3], float radius, int32_t anchorId) {
        auto js = std::make_shared<JobStorage>();
        PushErase pe{anchorId, c[0], c[1], c[2], radius};
        AddDispatch(*js, pipes_[K_ERASE], FS_CELLS_PER_PAGE / FS_WG_SMALL, pageCount_, &pe, sizeof pe);
        FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_PUBLISH; jd.name = "world.erase"; jd.dispatches = js->d.data(); jd.dispatchCount = 1;
        jd.onRetired = [this, js](bool, uint64_t, uint64_t) { std::lock_guard<std::recursive_mutex> g(m_); releaseInFlight_--; publishWanted_ = true; };
        if (!SubmitWorldJob(jd)) return false;
        releaseInFlight_++;
        return true;
    }
    // Reads the retire list (host-visible, after the fence): index ids back to the CPU stacks now (scanner-only),
    // render ids into `renderOut` (freed after graphics retirement of the publication that made them obsolete).
    void CollectRetire(std::vector<uint32_t>& renderOut) {
        uint32_t n = std::min<uint32_t>(AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_RETIRE_COUNT), FS_RETIRE_MAX);
        for (uint32_t i = 0; i < n; ++i) {
            uint32_t e = retire_[i], kind = e >> 30, id = e & 0x3FFFFFFFu;
            if (kind == kRetireKindILeaf) rings_[0].Free(id); else if (kind == kRetireKindINode) rings_[1].Free(id); else renderOut.push_back(e);
        }
        AtomicStoreU32(gctr_ + FS_GCTR_COUNT + FS_T_RETIRE_COUNT, 0);
    }
    void AfterJob(bool publish) {                    // fs-sched, after the fence: bookkeeping only from host-visible memory
        for (IdRing& r : rings_) r.SyncHead();
        FoldGpuCounters();
        if (!publish) {
            idBase_ += AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_NEW_TOTAL) + AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_REFINE_BIRTHS);
            // index ids retired by the epoch's inserts go back now; render ids (leaves chunks of a chain in progress) wait for the
            // chain's FRONT publication: the current FRONT still references them
            std::vector<uint32_t> render; CollectRetire(render);
            chainRenderRetire_.insert(chainRenderRetire_.end(), render.begin(), render.end());
            return;
        }
        PublishBatch batch;
        batch.renderRetire.swap(chainRenderRetire_);
        CollectRetire(batch.renderRetire);
        uint32_t n = std::min<uint32_t>(AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_PENDING_COUNT), FS_PENDING_PUBLISH_MAX);
        for (uint32_t i = 0; i < n; ++i) batch.entries.push_back(pending_[i]);
        AtomicStoreU32(gctr_ + FS_GCTR_COUNT + FS_T_PENDING_COUNT, 0);
        if (!batch.entries.empty() || !batch.renderRetire.empty()) toPublish_.push_back(std::move(batch));
    }
    void FoldGpuCounters() {
        if (!gctr_) return;
        for (uint32_t i = 0; i < FS_GCTR_COUNT; ++i) { uint32_t v = AtomicLoadU32(&gctr_[i]); gctrTotal_[i] += (uint64_t)(v - gctrLast_[i]); gctrLast_[i] = v; }
        static const int32_t map[FS_GCTR_COUNT] = { FS_CTR_PAGE_LOOKUPS, FS_CTR_PAGE_MISSES, -1, -1, -1, FS_CTR_SURFEL_UPDATE, -1, -1, FS_CTR_SURFEL_CREATE, -1,
            FS_CTR_SURFEL_SPLIT, FS_CTR_SURFEL_MERGE, FS_CTR_SURFEL_DELETE, -1, -1, FS_CTR_INDEX_OVERFLOW, -1, -1, -1, FS_CTR_PUBLISH, FS_CTR_MEASUREMENTS_DROPPED, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        for (uint32_t i = 0; i < FS_GCTR_COUNT; ++i) if (map[i] >= 0 && gctrTotal_[i] != gctrFolded_[i]) { CounterAdd((FsCounter)map[i], (int64_t)(gctrTotal_[i] - gctrFolded_[i])); gctrFolded_[i] = gctrTotal_[i]; }
    }

    // ---- FRAME_BEGIN (render thread): graphics-domain root publication + retirement registration ---------
    void OnFrameBegin(VkCommandBuffer cmd, uint32_t frame) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (!deviceUp_ || !pipesReady_ || !bound_ || toPublish_.empty()) return;
        FrameMode mode = CurrentFrameMode();
        if (mode == FS_FRAME_MODE_SKIPPED || mode == FS_FRAME_MODE_NONE) return;
        const uint32_t slot = (uint32_t)(publishSeq_++ % kPublishRingSlots);
        FsPendingPublish* ring = publishRing_ + (size_t)slot * FS_MAX_PAGES;
        uint32_t count = 0;
        std::vector<uint32_t> renderRetire; std::vector<uint32_t> released;
        while (!toPublish_.empty() && count + toPublish_.front().entries.size() <= FS_MAX_PAGES) {
            PublishBatch& b = toPublish_.front();
            for (const FsPendingPublish& e : b.entries) ring[count++] = e;
            renderRetire.insert(renderRetire.end(), b.renderRetire.begin(), b.renderRetire.end());
            released.insert(released.end(), b.releasedPages.begin(), b.releasedPages.end());
            toPublish_.pop_front();
        }
        std::atomic_thread_fence(std::memory_order_release);
        if (count) {
            PushPublish pp{slot, count};
            Pipeline& p = pipes_[K_PUBLISH_ROOTS];
            vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, p.pipeline);
            vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, p.layout, 0, 1, &p.set, 0, nullptr);
            vkCmdPushConstants(cmd, p.layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof pp, &pp);
            vkCmdDispatch(cmd, (count + FS_WG_SMALL - 1) / FS_WG_SMALL, 1, 1);
            VkMemoryBarrier mb = {}; mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER; mb.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT; mb.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
            vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 1, &mb, 0, nullptr, 0, nullptr);
            rootsPublished_ += count; frontSeq_.fetch_add(1, std::memory_order_relaxed);
        }
        // Physical reuse only after scanner + graphics retirement of this frame (executor ledger).
        std::vector<std::pair<uint32_t, uint32_t>> releaseGen; for (uint32_t s : released) releaseGen.push_back({s, pages_[s].generation});
        retireBacklogIds_ += renderRetire.size();
        RetireLater([this, renderRetire, releaseGen]() {
            std::lock_guard<std::recursive_mutex> g(m_);
            for (uint32_t e : renderRetire) { uint32_t kind = e >> 30, id = e & 0x3FFFFFFFu; if (kind == kRetireKindRBlock) rings_[2].Free(id); else if (kind == kRetireKindRNode) rings_[3].Free(id); }
            retireBacklogIds_ -= renderRetire.size();
            for (auto& rg : releaseGen) FinishRelease(rg.first, rg.second);
        });
        (void)frame;
    }

    // ---- residency (§12): position only; orientation is never an input ----------------------------------
    void ApplyResidency() {
        if (!centerValid_ || appliedSeq_ == centerSeq_) return;
        appliedSeq_ = centerSeq_;
        FrameHint hint; float centre[3] = {center_[0], center_[1], center_[2]};
        if (GetFrameHint(hint)) PredictCentre(center_, hint.headVel, kPredictHorizonS, kPredictMaxShiftM, centre);
        ComputeZonePages(scanAnchor_, centre, radii_[0], radii_[1], radii_[2], zonePages_);
        inner_ = warm_ = prefetch_ = 0; requestsThisFrame_ = 0;
        for (PageState& s : pages_) if (s.life == PAGE_ACTIVE) s.zone = ZONE_OUTSIDE;
        for (auto& kv : logical_) kv.second.zone = ZONE_OUTSIDE;
        uint32_t loads = 0, created = 0;
        for (const ZonePage& zp : zonePages_) {
            LogicalPage& p = PageFor(zp.key); p.zone = zp.zone;
            if (zp.zone == ZONE_INNER) inner_++; else if (zp.zone == ZONE_WARM) warm_++; else prefetch_++;
            if (p.slot != FS_INDEX_NONE) { pages_[p.slot].zone = zp.zone; pages_[p.slot].lastTouched = FrameIndex(); continue; }
            if (p.hasCold && zp.zone != ZONE_PREFETCH && loads < kLoadsPerTick && JobsIdle()) {
                requestsThisFrame_++;
                uint32_t slot = CreatePage(p, zp.zone, (uint32_t)(p.cold.surfels.size() + FS_SLAB_SURFELS - 1) / FS_SLAB_SURFELS + FS_PAGE_RESERVE_SLABS);
                if (slot == FS_INDEX_NONE) continue;
                LoadCold(slot, p); loads++;
            } else if (!p.hasCold && zp.zone == ZONE_INNER && created < kCreatesPerTick) {
                if (CreatePage(p, zp.zone, FS_PAGE_INITIAL_SLABS) != FS_INDEX_NONE) created++;
            }
        }
        uint32_t evicts = 0;
        if (JobsIdle()) for (uint32_t i = 0; i < pages_.size() && evicts < kEvictsPerTick; ++i)
            if (pages_[i].life == PAGE_ACTIVE && pages_[i].zone == ZONE_OUTSIDE) { BeginRelease(i, true); evicts++; }
    }
    // Writes the cold copy into the page's slabs (sorted by cell) and the cell start table into the sort scratch;
    // the next publish job runs world_page_load for it (single owner per cell) before the dirty compaction.
    void LoadCold(uint32_t slot, LogicalPage& p) {
        PageState& s = pages_[slot];
        uint32_t n = (uint32_t)std::min<size_t>(p.cold.surfels.size(), (size_t)s.slabs.size() * FS_SLAB_SURFELS);
        for (uint32_t local = 0; local < n; ++local) {
            uint32_t h = s.slabs[local / FS_SLAB_SURFELS] * FS_SLAB_SURFELS + (local % FS_SLAB_SURFELS);
            surfels_[h] = p.cold.surfels[local]; evidence_[h] = p.cold.evidence[local];
        }
        AtomicStoreU32(&pagesGpu_[slot].surfelCount, n);
        pendingLoads_.push_back({slot, n});
        s.loadPending = true; publishWanted_ = true;
        p.hasCold = false; p.cold = ColdPage{};
        loads_++;
    }
    void WriteLoadTable(uint32_t slot, uint32_t n) {   // cell start offsets of the loaded (cell-sorted) range
        PageState& s = pages_[slot];
        std::vector<uint32_t> counts(FS_CELLS_PER_PAGE + 1, 0);
        for (uint32_t local = 0; local < n; ++local) {
            uint32_t h = s.slabs[local / FS_SLAB_SURFELS] * FS_SLAB_SURFELS + (local % FS_SLAB_SURFELS);
            counts[CellOf(DecodePos(surfels_[h].px), DecodePos(surfels_[h].py), DecodePos(surfels_[h].pz)) + 1]++;
        }
        for (uint32_t c = 0; c < FS_CELLS_PER_PAGE; ++c) counts[c + 1] += counts[c];
        memcpy(sort_, counts.data(), (FS_CELLS_PER_PAGE + 1) * 4);
    }

    // ---- scheduler tick (fs-sched thread) -----------------------------------------------------------
    void Tick(uint32_t budgetUs) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (!deviceUp_) return;
        EnsureBound();
        if (!Ready()) return;
        if (!JobsIdle()) return;                       // epoch boundaries only: everything below reads/writes host-visible bookkeeping
        FoldGpuCounters();
        if (resetPending_) { RunReset(); if (!JobsIdle()) return; }
        ApplyResidency();
        FeedSynthetic();
        MergeCpuPush();
        BeforeJobs();
        // page releases (eviction / reset) and erases go first, but never inside a publication chain (they reset the epoch words)
        if (pubStage_ == PUB_IDLE && !releaseQueue_.empty()) {
            std::vector<uint32_t> batch; while (!releaseQueue_.empty() && batch.size() < kReleasesPerJob) { batch.push_back(releaseQueue_.front()); releaseQueue_.erase(releaseQueue_.begin()); }
            PrepareEpochWords(tick_);
            if (!SubmitRelease(batch)) for (uint32_t s : batch) releaseQueue_.push_back(s);
            return;
        }
        for (; pubStage_ == PUB_IDLE && eraseQueue_.size();) { EraseReq r = eraseQueue_.front(); eraseQueue_.erase(eraseQueue_.begin()); PrepareEpochWords(tick_); if (!SubmitErase(r.c, r.r, r.anchor)) break; return; }
        if (budgetUs == 0) return;
        // ---- C09R-E5R deadline / deficit scheduler: one job per tick among FUSE, PUBLISH (chain stage or a new chain) and SHEET.
        //   1. overdue work first (publication: oldest pending dirty cell older than FS_SCHED_PUBLISH_DEADLINE_MS; sheet: oldest pending
        //      graph node older than FS_SCHED_SHEET_DEADLINE_MS), most overdue relative to its deadline wins;
        //   2. otherwise the runnable stage with the largest GPU-time deficit (shares FS_SCHED_SHARE_*).
        //   A new depth frame is NOT leased while publication or sheet work is overdue (latest-only: it waits in the ring or is superseded).
        const int64_t now = NowNs();
        const uint32_t dirtyPending = DirtyPending(), sheetPending = SheetPendingCount();
        const bool pubRunnable = pubStage_ != PUB_IDLE || dirtyPending != 0 || !pendingLoads_.empty();
        const bool sheetRunnable = sheetPending != 0;
        float pubAge = 0, sheetAge = 0;
        if (pubStage_ != PUB_IDLE) pubAge = (float)((now - pubStartNs_) / 1e6) + (pubAgeTicks_.empty() ? 0.f : WorkAgeMs(pubAgeTicks_.front(), pubStartNs_));
        else if (dirtyPending) pubAge = WorkAgeMs(DirtyEntryTick(AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_DIRTY_HEAD)), now);
        if (!pendingLoads_.empty()) pubAge = std::max(pubAge, (float)FS_SCHED_PUBLISH_DEADLINE_MS);
        if (sheetRunnable) sheetAge = WorkAgeMs(SheetEntryTick(AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_SHEET_HEAD)), now);
        pubPendingAgeMs_ = pubAge; sheetPendingAgeMs_ = sheetAge;
        const float pubOver = pubRunnable ? pubAge / (float)FS_SCHED_PUBLISH_DEADLINE_MS : 0.f, sheetOver = sheetRunnable ? sheetAge / (float)FS_SCHED_SHEET_DEADLINE_MS : 0.f;
        // drop older observations, never display frames: a newer depth frame supersedes the rest of the current one (stride slices)
        if (slice_.count != 0 && slice_.live && slice_.phase > 0 && slice_.phase < slice_.stride && meas::MeasGpu_ReadyFrames() > 0) {
            const uint32_t left = slice_.stride - slice_.phase;
            framesAbandoned_++; measAbandoned_ += (uint64_t)slice_.count * left / slice_.stride;
            CounterAdd(FS_CTR_MEASUREMENTS_DROPPED, (int64_t)((uint64_t)slice_.count * left / slice_.stride));
            meas::MeasGpu_ReleaseFrame(slice_.seq);
            slice_.count = 0;
        }
        const bool fuseRunnable = slice_.count != 0 || (liveBound_ && meas::MeasGpu_ReadyFrames() > 0) || cpuSlotReady_;
        // FUSE age: the observation being sliced, else the newest READY one (a CPU synthetic slot counts as fresh)
        float fuseAge = 0;
        if (slice_.count != 0 && slice_.readyNs) fuseAge = (float)((now - slice_.readyNs) / 1e6);
        else if (liveBound_) { const int64_t r = meas::MeasGpu_NewestReadyNs(); if (r) fuseAge = (float)((now - r) / 1e6); }
        fusePendingAgeMs_ = fuseAge;
        const float fuseOver = fuseRunnable ? fuseAge / (float)FS_SCHED_FUSE_DEADLINE_MS : 0.f;
        // E6R: overdue = normalized lateness > 1; the most late runnable class wins; otherwise the weighted deficit decides. No class can
        // starve: every class ages while it waits and eventually carries the largest lateness.
        (void)fuseOver; (void)pubOver; (void)sheetOver;
        const bool runnable[3] = {fuseRunnable, pubRunnable, sheetRunnable};
        const float ages[3] = {fuseAge, pubAge, sheetAge}, deadlines[3] = {(float)FS_SCHED_FUSE_DEADLINE_MS, (float)FS_SCHED_PUBLISH_DEADLINE_MS, (float)FS_SCHED_SHEET_DEADLINE_MS};
        bool overdue = false;
        const int picked = PickStage(runnable, ages, deadlines, deficit_, &overdue);
        SchedStage pick = picked < 0 ? ST_COUNT : (SchedStage)picked;
        if (overdue && pick < ST_COUNT) schedOverdue_[pick]++;
        if (pick == ST_PUB) {
            if (pubStage_ == PUB_LEAVES && pubTotal_ != 0) SubmitPubLeaves();
            else if (pubStage_ == PUB_LEVELS) SubmitPubLevels();
            else if (pubStage_ == PUB_IDLE) { std::vector<std::pair<uint32_t, bool>> loads; TakeLoads(loads); SubmitPubMaint(loads); }
            return;
        }
        if (pick == ST_SHEET) { SubmitSheet(); return; }
        if (pick != ST_FUSE) return;
        if (slice_.count == 0) {
            meas::MeasGpuFrame f;
            bool live = liveBound_ && meas::MeasGpu_PeekFrame(f);
            if (live) { slice_ = SliceState{}; slice_.live = true; slice_.readyNs = f.readyMonoNs; slice_.seq = f.sequence;
                        fuseAge_.Add((float)((now - f.readyMonoNs) / 1e6)); if ((float)((now - f.readyMonoNs) / 1e6) > (float)FS_SCHED_FUSE_DEADLINE_MS) deadlineMiss_[ST_FUSE]++; observationsLeased_++; slice_.slot = f.slot; slice_.count = std::min(f.ring->capacity, std::max(f.count, 1u)); slice_.anchor = f.anchorId; slice_.eyeValid = f.eyeOriginValid; memcpy(slice_.eye, f.eyeOrigin, sizeof slice_.eye); slice_.importFrameEnd = f.importFrameEnd; scanAnchor_ = f.anchorId; }
            else if (cpuSlotReady_) { slice_ = SliceState{}; slice_.live = false; slice_.count = std::max(cpuCtr_[0], 1u); slice_.anchor = scanAnchor_; slice_.eyeValid = cpuEyeValid_; memcpy(slice_.eye, cpuEye_, sizeof slice_.eye); cpuSlotBusy_ = true; cpuSlotReady_ = false; }
            if (slice_.count) slice_.stride = std::max<uint32_t>(1u, (slice_.count + epochMeasCap_ - 1) / epochMeasCap_);
        }
        if (slice_.count == 0) return;
        const uint32_t n = (slice_.count - slice_.phase + slice_.stride - 1) / slice_.stride;
        const bool last = slice_.phase + 1 >= slice_.stride;
        const bool live = slice_.live; const uint64_t seq = slice_.seq;
        bool ok = SubmitFuse(live ? ingestLive_[slice_.slot] : ingestCpu_, slice_.phase, slice_.stride, n, slice_.anchor, slice_.eyeValid, slice_.eye[0], slice_.eye[1], live ? slice_.importFrameEnd : 0,
                             [this, live, seq, last](bool) { if (!last) return; if (live) meas::MeasGpu_ReleaseFrame(seq); else cpuSlotBusy_ = false; });
        // deferred by the executor (ring / budget): retried next tick, same slice. E6R: the measurement front-end shares the SCAN budget and
        // would take it again every frame (compaction + refine chunks), starving fusion forever (device run 15:09: 0 fuse jobs) -> backpressure
        fuseBackpressure_.store(!ok, std::memory_order_relaxed);
        if (!ok) { fuseDeferred_++; return; }
        slice_.phase++; slices_++;
        if (last) { slice_.count = 0; observationsFused_++; }
        CounterAdd(FS_CTR_SCAN_TICK, 1);
    }
    void TakeLoads(std::vector<std::pair<uint32_t, bool>>& loads) {
        if (pendingLoads_.empty()) return;
        auto l = pendingLoads_.front(); pendingLoads_.erase(pendingLoads_.begin());   // one load per publish job (it owns the sort scratch's cell table)
        WriteLoadTable(l.first, l.second);
        loads.push_back({l.first, true});
    }
    void FeedSynthetic() {
        if (feedCursor_ >= feed_.size() || cpuSlotBusy_ || cpuSlotReady_) { if (feedCursor_ >= feed_.size() && !feed_.empty()) { feed_.clear(); feed_.shrink_to_fit(); feedCursor_ = 0; } return; }
        uint32_t n = (uint32_t)std::min<size_t>(FS_TICK_MEAS_MAX, feed_.size() - feedCursor_);
        memcpy(cpuRec_, feed_.data() + feedCursor_, n * sizeof(FsSurfaceMeasurement));
        cpuCtr_[0] = n; cpuEyeValid_ = true; memcpy(cpuEye_, feedEye_, sizeof cpuEye_);
        feedCursor_ += n; cpuSlotReady_ = true;
        CounterAdd(FS_CTR_MEASUREMENTS, n);
    }
    void MergeCpuPush() {
        std::lock_guard<std::mutex> pl(pushMutex_);
        if (pushStaging_.empty() || cpuSlotBusy_ || cpuSlotReady_) return;
        uint32_t n = (uint32_t)std::min<size_t>(FS_TICK_MEAS_MAX, pushStaging_.size());
        memcpy(cpuRec_, pushStaging_.data(), n * sizeof(FsSurfaceMeasurement));
        pushStaging_.erase(pushStaging_.begin(), pushStaging_.begin() + n);
        cpuCtr_[0] = n; cpuEyeValid_ = false; cpuSlotReady_ = true;
        CounterAdd(FS_CTR_MEASUREMENTS, n);
    }

    // ---- reset transaction (C09R §25) ------------------------------------------------------------------
    void RunReset() {
        // observations are already refused (resetPending_); every active page is released through the normal path
        for (uint32_t i = 0; i < pages_.size(); ++i) if (pages_[i].life == PAGE_ACTIVE) BeginRelease(i, false);
        for (auto& kv : logical_) { kv.second.slot = FS_INDEX_NONE; kv.second.hasCold = false; kv.second.cold = ColdPage{}; }
        logical_.clear(); pendingLoads_.clear(); eraseQueue_.clear(); feed_.clear(); feedCursor_ = 0; cpuSlotReady_ = false;
        { std::lock_guard<std::mutex> pl(pushMutex_); pushStaging_.clear(); }
        if (releaseQueue_.empty()) { idBase_ = kSurfaceIdBase; tick_ = 0; resetPending_ = false; resets_++; Log("FS-WORLD reset complete"); }
        else resetDraining_ = true;
        if (resetDraining_ && releaseQueue_.empty()) { idBase_ = kSurfaceIdBase; tick_ = 0; resetPending_ = false; resetDraining_ = false; resets_++; Log("FS-WORLD reset complete (pages released)"); }
    }

    // ---- exports ------------------------------------------------------------------------------------
    int32_t CreateSynthetic(int32_t kind, float extentM, float spacing, uint32_t seed) {
        SyntheticScene scene;
        GenerateSynthetic(kind & 0xFF, extentM, spacing, seed, scanAnchor_, scene);
        std::lock_guard<std::recursive_mutex> g(m_);
        feed_.clear(); SamplesToMeasurements(scene.samples, 0, scene.samples.size(), ++feedObs_, feed_); feedCursor_ = 0;
        memset(feedEye_, 0, sizeof feedEye_); feedEye_[0][1] = feedEye_[1][1] = 1.5f;   // synthetic eye at (0, 1.5, 0): free-space rays from the room centre
        Log("FS-WORLD synthetic kind %d: %zu measurements (%llu samples, %u planes) fed through the fusion epochs", kind, feed_.size(), (unsigned long long)scene.sampleCount, scene.planeCount);
        return kResultOk;
    }
    void GetPageCount(int32_t* resident, int32_t* logical) {
        std::lock_guard<std::recursive_mutex> g(m_);
        int32_t r = 0; for (const PageState& s : pages_) if (s.life == PAGE_ACTIVE) r++;
        if (resident) *resident = r; if (logical) *logical = (int32_t)logical_.size();
    }
    void GetSurfelCount(int64_t* front, int64_t* back) {   // published (render roots) / canonical cursors (host-visible mirror)
        std::lock_guard<std::recursive_mutex> g(m_);
        int64_t f = 0, b = 0;
        if (pagesGpu_) for (uint32_t i = 0; i < pages_.size(); ++i) if (pages_[i].life == PAGE_ACTIVE) { f += AtomicLoadU32(&pagesGpu_[i].renderCount); b += std::min<uint32_t>(AtomicLoadU32(&pagesGpu_[i].surfelCount), (uint32_t)pages_[i].slabs.size() * FS_SLAB_SURFELS); }
        if (front) *front = f; if (back) *back = b;
    }
    uint32_t FrontGeneration() { std::lock_guard<std::recursive_mutex> g(m_); return (uint32_t)rootsPublished_; }
    int32_t SetAnchor(int32_t id, const float m[16]) { if (id < 0 || id >= FS_MAX_ANCHORS || !m) return kResultInvalid; std::lock_guard<std::recursive_mutex> g(m_); memcpy(anchors_[id], m, 64); anchorSeq_++; return kResultOk; }
    uint64_t CopyAnchors(float* out) { std::lock_guard<std::recursive_mutex> g(m_); memcpy(out, anchors_, sizeof anchors_); return anchorSeq_; }
    uint64_t FrontSequence() const { return frontSeq_.load(std::memory_order_relaxed); }   // lock-free: render recut trigger
    bool FuseBackpressure() const { return fuseBackpressure_.load(std::memory_order_relaxed); }
    std::atomic<bool> fuseBackpressure_{false};
    std::atomic<uint64_t> frontSeq_{0};
    int32_t Erase(const float c[3], float radius, int32_t anchorId) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (!deviceUp_) return kResultUnavailable;
        if (eraseQueue_.size() >= 16) return kResultBusy;
        EraseReq r; memcpy(r.c, c, sizeof r.c); r.r = radius; r.anchor = anchorId; eraseQueue_.push_back(r);
        return kResultOk;
    }
    int32_t Reset() { std::lock_guard<std::recursive_mutex> g(m_); if (!deviceUp_) return kResultUnavailable; resetPending_ = true; return kResultOk; }
    int32_t SetCenter(const float p[3], float inner, float warm, float prefetch) {
        std::lock_guard<std::recursive_mutex> g(m_);
        memcpy(center_, p, sizeof center_); radii_[0] = inner; radii_[1] = warm; radii_[2] = prefetch; centerValid_ = true; centerSeq_++;
        return kResultOk;
    }
    void ZoneStats(int64_t out[8]) {
        std::lock_guard<std::recursive_mutex> g(m_);
        out[0] = inner_; out[1] = warm_; out[2] = prefetch_; out[3] = requestsThisFrame_; out[4] = 0; out[5] = evictions_; out[6] = loads_; out[7] = stalls_ + slabStalls_;
    }
    int32_t Push(const FsSurfaceMeasurement* items, int32_t count, int32_t anchorId) {
        if (!items || count <= 0) return kResultInvalid;
        if (resetPending_) return kResultBusy;
        std::lock_guard<std::mutex> pl(pushMutex_);
        scanAnchor_ = anchorId;
        size_t room = pushStaging_.size() < 4u * FS_TICK_MEAS_MAX ? 4u * FS_TICK_MEAS_MAX - pushStaging_.size() : 0;
        size_t n = std::min<size_t>(room, (size_t)count);
        pushStaging_.insert(pushStaging_.end(), items, items + n);
        if (n < (size_t)count) CounterAdd(FS_CTR_MEASUREMENTS_DROPPED, (int64_t)(count - n));
        return (int32_t)n;
    }
    std::string TelemetryJson() {
        // Do not hold World::m_ while acquiring Executor::mutex. The value is diagnostic and may be one scheduling
        // instant older than the rest of the snapshot; lock-order correctness is more important than atomic telemetry.
        const uint64_t retirementBacklog = (uint64_t)RetirementBacklog();
        std::lock_guard<std::recursive_mutex> g(m_);
        JsonWriter w; w.BeginObject();
        w.KV("epochs", stats_.epochs); w.KV("tick", tick_); w.KV("fuseGpuUsLast", stats_.fuseUs); w.KV("publishGpuUsLast", stats_.publishUs);
        w.KV("idBase", idBase_); w.KV("rootsPublished", rootsPublished_); w.KV("pendingBatches", (uint64_t)toPublish_.size()); w.KV("retireBacklogIds", retireBacklogIds_); w.KV("retirementBacklog", retirementBacklog);
        int32_t active = 0, releasing = 0; for (const PageState& s : pages_) { if (s.life == PAGE_ACTIVE) active++; if (s.life == PAGE_RELEASING) releasing++; }
        w.KV("pagesActive", active); w.KV("pagesReleasing", releasing); w.KV("pagesLogical", (uint64_t)logical_.size()); w.KV("pagesCreated", pagesCreated_); w.KV("pagesReleased", pagesReleased_);
        w.KV("shortfallTotal", shortfallTotal_); w.KV("slabStalls", slabStalls_); w.KV("resets", resets_);
        w.KV("epochMeasCap", epochMeasCap_); w.KV("epochDirtyCap", epochDirtyCap_); w.KV("slices", slices_); w.KV("capHalvings", capHalvings_);
        w.KV("leavesChunk", lastLeavesChunk_); w.KV("publishChunks", pubChunks_); w.KV("sheetBatch", lastSheetBatch_); w.KV("sheetJobs", sheetJobs_);
        w.KV("dirtyRingPending", (uint64_t)DirtyPending()); w.KV("sheetRingPending", (uint64_t)SheetPendingCount());
        w.KV("publicationAgeP50Ms", pubAge_.Quantile(0.5f)); w.KV("publicationAgeP95Ms", pubAge_.Quantile(0.95f)); w.KV("publicationAgeSamples", pubAge_.total);
        w.KV("publicationChainP95Ms", pubChainMs_.Quantile(0.95f));
        w.KV("sheetAgeP50Ms", sheetAge_.Quantile(0.5f)); w.KV("sheetAgeP95Ms", sheetAge_.Quantile(0.95f)); w.KV("sheetAgeSamples", sheetAge_.total);
        w.KV("pendingPublicationAgeMs", pubPendingAgeMs_); w.KV("pendingSheetAgeMs", sheetPendingAgeMs_);
        w.KV("schedFuseJobs", schedJobs_[ST_FUSE]); w.KV("schedPublishJobs", schedJobs_[ST_PUB]); w.KV("schedSheetJobs", schedJobs_[ST_SHEET]);
        w.KV("fuseOverduePicks", schedOverdue_[ST_FUSE]); w.KV("publishOverduePicks", schedOverdue_[ST_PUB]); w.KV("topologyOverduePicks", schedOverdue_[ST_SHEET]);
        w.KV("fuseJobs", schedJobs_[ST_FUSE]); w.KV("publishJobs", schedJobs_[ST_PUB]); w.KV("topologyJobs", schedJobs_[ST_SHEET]);
        w.KV("fuseDeadlineMisses", deadlineMiss_[ST_FUSE]); w.KV("publishDeadlineMisses", deadlineMiss_[ST_PUB]); w.KV("topologyDeadlineMisses", deadlineMiss_[ST_SHEET]);
        w.KV("fuseAgeP50Ms", fuseAge_.Quantile(0.5f)); w.KV("fuseAgeP95Ms", fuseAge_.Quantile(0.95f)); w.KV("fuseAgeMaxMs", fuseAge_.Max());
        w.KV("publishAgeMaxMs", pubAge_.Max()); w.KV("topologyAgeP50Ms", sheetAge_.Quantile(0.5f)); w.KV("topologyAgeP95Ms", sheetAge_.Quantile(0.95f)); w.KV("topologyAgeMaxMs", sheetAge_.Max());
        w.KV("pendingFuseAgeMs", fusePendingAgeMs_);
        { uint64_t ready = 0, sup = 0; meas::MeasGpu_FrameTotals(ready, sup); w.KV("observationsReceived", ready); w.KV("observationsSuperseded", sup + framesAbandoned_); }
        w.KV("fuseDeferred", fuseDeferred_); w.KV("observationsLeased", observationsLeased_); w.KV("observationsFused", observationsFused_); w.KV("workSerial", (uint64_t)workSerial_);
        w.KV("promotedLive", (int64_t)gctrTotal_[FS_GCTR_PROMOTIONS] - (int64_t)gctrTotal_[FS_GCTR_PROMOTED_REMOVED]);
        auto cm = [&](const char* name, const CostModel& c) { w.KV((std::string(name) + "FixedUs").c_str(), (int64_t)c.fixedUs); w.KV((std::string(name) + "PerItemUs1000").c_str(), (int64_t)(c.perItemUs * 1000.0)); w.KV((std::string(name) + "Structural").c_str(), c.structural); w.KV((std::string(name) + "Learned").c_str(), c.samples); w.KV((std::string(name) + "Skipped").c_str(), c.skipped); };
        cm("costFuse", fuseCost_); cm("costMaint", maintCost_); cm("costLeaves", leavesCost_); cm("costSheet", sheetCost_);
        w.KV("sheetGpuUsLast", stats_.sheetUs); w.KV("publishMaintUsLast", stats_.publishMaintUs); w.KV("publishLeavesUsLast", stats_.publishLeavesUs); w.KV("publishLevelsUsLast", stats_.publishLevelsUs);
        w.KV("framesAbandoned", framesAbandoned_); w.KV("measurementsAbandoned", measAbandoned_);
        w.Key("fusion"); w.BeginObject();
        static const char* names[FS_GCTR_COUNT] = {"pageLookups","pageMisses","assocRecords","assocMatched","assocUnmatched","segmentsMatched","contributions","segOverflow","newSurfels","newShortfall","splits","merges","ghosts","candidateOverflow","indexLeafSplits","indexOverflow","dirtyCells","cowNodes","renderBlocks","rootsPending","measOutOfRange","dirtyOverflow","poolLeafEmpty","poolNodeEmpty","poolRBlockEmpty","poolRNodeEmpty","freeStamps","segmentsUnmatched","freeHopOverflow","relocations","nextSurfaceId","relocDeferred","sheetEdges","sheetNodes","crossPageEdges","coverageOverlapMm2","coverageHoleMm2","planeRmsUmSum","normalRmsMdegSum","sheetFrontierDropped","sheetSmoothed","edgeContractions","refinementBirths","sheetCandOverflow","promotions","promotedRemoved","dirtyRingDrop","positiveEvidence","contradictions","contradictionsHealed","surfelSuspect","surfelRecovered","surfelRetired","freeNarrowHits","activeSheets","sheetUnions","sheetSplits","activeFaces","facesCreated","facesRetired","facesSuspect","facesRecovered","boundaryVertices","faceDuplicateRefused","faceOwnerViolations","meshHoleMm2","meshOverlapMm2","topologyVertices","topologyPoolEmpty","bridgesPending","faceOverflow","temporalTargets","contractUnmatched"};
        for (uint32_t i = 0; i < FS_GCTR_COUNT; ++i) if (names[i][0] != '_') w.KV(names[i], gctrTotal_[i]);
        w.EndObject();
        w.Key("memory"); w.BeginObject();
        w.KV("bytesCommitted", bytesTotal_); w.KV("slabsLive", slabs_.Live()); w.KV("slabsTotal", slabs_.Total()); w.KV("canonicalBytesLive", (uint64_t)slabs_.Live() * FS_SLAB_SURFELS * (sizeof(FsSurfel) + sizeof(FsSurfelEvidence)));
        const char* pn[6] = {"indexLeaves", "indexNodes", "renderBlocks", "renderNodes", "topologyVertices", "sheetRecords"}; const uint64_t pb[6] = {64, 32, FS_RENDER_BLOCK_SURFELS * sizeof(FsSurfel), sizeof(FsRenderNode), FS_TOPO_VERTEX_WORDS * 4, FS_SHEET_REC_WORDS * 4};
        for (uint32_t p = 0; p < 6; ++p) { w.Key(pn[p]); w.BeginObject(); w.KV("live", rings_[p].Live()); w.KV("capacity", rings_[p].Capacity()); w.KV("bytesLive", (uint64_t)rings_[p].Live() * pb[p]); w.KV("allocFailures", rings_[p].Failures()); w.EndObject(); }
        w.EndObject();
        w.EndObject();
        return w.Take();
    }
    const WorldBuffers* BuffersIf() const { return deviceUp_ ? &buf_ : nullptr; }
    bool IsDeviceUp() const { return deviceUp_; }
    uint32_t Pages() const { return pageCount_; }

private:
    struct EraseReq { float c[3]; float r; int32_t anchor; };
    std::recursive_mutex m_;
    std::mutex pushMutex_;
    bool inited_ = false, configured_ = false, deviceUp_ = false, pipesReady_ = false, bound_ = false, liveBound_ = false;
    uint32_t pageCount_ = 0, hashCap_ = 0, surfelCap_ = 0, leafCap_ = 0, nodeCap_ = 0, rblockCap_ = 0, rnodeCap_ = 0, idxLeafBase_ = 0, idxNodeBase_ = 0;
    uint64_t bytesTotal_ = 0;
    PageHash hash_; bool hashDirty_ = false; FsPageHashEntry* hashMirror_ = nullptr;
    std::vector<PageState> pages_;
    std::unordered_map<FsPageKey, LogicalPage, KeyHasher, KeyEqual> logical_;
    SlabAllocator slabs_; IdRing rings_[6];
    WorldBuffers buf_; Buffer cpuRecords_, cpuCounters_;
    FsPageDesc* pagesGpu_ = nullptr; uint32_t* slabDir_ = nullptr; FsSurfel* surfels_ = nullptr; FsSurfelEvidence* evidence_ = nullptr; uint32_t* index_ = nullptr;
    uint32_t* gctr_ = nullptr; uint32_t gctrLast_[FS_GCTR_COUNT] = {}; uint64_t gctrTotal_[FS_GCTR_COUNT] = {}, gctrFolded_[FS_GCTR_COUNT] = {};
    uint32_t* sort_ = nullptr; uint32_t* rdir_ = nullptr; uint32_t* retire_ = nullptr; FsPendingPublish* pending_ = nullptr; FsPendingPublish* publishRing_ = nullptr;
    FsSurfaceMeasurement* cpuRec_ = nullptr; uint32_t* cpuCtr_ = nullptr; bool cpuSlotReady_ = false, cpuSlotBusy_ = false, cpuEyeValid_ = false; float cpuEye_[2][3] = {};
    Pipeline pipes_[K_COUNT]; static constexpr uint32_t kLiveSlots = FS_MEAS_GPU_RING_SLOTS; Pipeline ingestLive_[kLiveSlots], ingestCpu_, histB_, scatterB_;
    uint64_t liveLease_ = 0;
    float anchors_[FS_MAX_ANCHORS][16]; uint64_t anchorSeq_ = 1;
    bool centerValid_ = false; float center_[3] = {0, 0, 0}; float radii_[3] = {3, 6, 10}; uint64_t centerSeq_ = 0, appliedSeq_ = 0;
    std::vector<ZonePage> zonePages_;
    int64_t inner_ = 0, warm_ = 0, prefetch_ = 0, requestsThisFrame_ = 0, evictions_ = 0, loads_ = 0, stalls_ = 0;
    uint64_t slabStalls_ = 0, shortfallTotal_ = 0, pagesCreated_ = 0, pagesReleased_ = 0, rootsPublished_ = 0, retireBacklogIds_ = 0, resets_ = 0, publishSeq_ = 0;
    uint32_t fuseInFlight_ = 0, publishInFlight_ = 0, releaseInFlight_ = 0, sheetInFlight_ = 0;
    uint32_t pubStage_ = 0, pubTotal_ = 0, pubDone_ = 0, lastLeavesChunk_ = 0, lastSheetBatch_ = 0, topoGen_ = 0;
    CostModel fuseCost_, maintCost_, leavesCost_, sheetCost_;
    uint32_t workSerial_ = 0; std::vector<int64_t> workTimeNs_ = std::vector<int64_t>(FS_SCHED_SERIAL_WINDOW, 0); std::vector<uint32_t> workTimeSerial_ = std::vector<uint32_t>(FS_SCHED_SERIAL_WINDOW, 0u); int64_t pubStartNs_ = 0;
    AgeWindow fuseAge_; float fusePendingAgeMs_ = 0; uint64_t fuseDeferred_ = 0; uint64_t deadlineMiss_[3] = {0, 0, 0}, observationsLeased_ = 0, observationsFused_ = 0;
    std::vector<uint32_t> pubAgeTicks_, sheetAgeTicks_; AgeWindow pubAge_, sheetAge_, pubChainMs_;
    double deficit_[3] = {0, 0, 0}; uint64_t schedJobs_[3] = {0, 0, 0}, schedOverdue_[3] = {0, 0, 0}; float pubPendingAgeMs_ = 0, sheetPendingAgeMs_ = 0;
    uint64_t pubChunks_ = 0, sheetJobs_ = 0, framesAbandoned_ = 0, measAbandoned_ = 0; std::vector<uint32_t> chainRenderRetire_;
    bool publishWanted_ = false, resetPending_ = false, resetDraining_ = false;
    uint32_t tick_ = 0, idBase_ = kSurfaceIdBase; int32_t scanAnchor_ = 0;
    // Quantum gate (C09R review gap 1): epoch caps adapt to the measured job GPU time vs the class quantum; a frame
    // larger than the cap is processed in slices (offset walks the frame), dirty cells beyond the cap stay dirty.
    uint32_t epochMeasCap_ = FS_TICK_MEAS_MAX, epochDirtyCap_ = FS_DIRTY_CELLS_MAX; uint64_t slices_ = 0, capHalvings_ = 0;
    struct SliceState { bool live = false; uint64_t seq = 0; uint32_t slot = 0, phase = 0, stride = 1, count = 0; int32_t anchor = 0; bool eyeValid = false; float eye[2][3] = {}; uint64_t importFrameEnd = 0; int64_t readyNs = 0; } slice_;
    std::vector<uint32_t> releaseQueue_; std::vector<std::pair<uint32_t, uint32_t>> pendingLoads_;
    std::deque<PublishBatch> toPublish_;
    std::vector<EraseReq> eraseQueue_;
    std::vector<FsSurfaceMeasurement> feed_, pushStaging_; size_t feedCursor_ = 0; uint32_t feedObs_ = 0; float feedEye_[2][3] = {};
    EpochStats stats_;
};

World& W() { static World w; return w; }
std::once_flag g_once;
void EnsureInitImpl() { std::call_once(g_once, []() { W().Init(); }); }
struct AutoInit { AutoInit() { EnsureInitImpl(); } } g_autoInit;   // warm-up creates the world pipelines before READY

} // namespace

void EnsureInit() { EnsureInitImpl(); }
bool DeviceUp() { return W().IsDeviceUp(); }
const WorldBuffers* Buffers() { return W().BuffersIf(); }
uint32_t PageCount() { return W().Pages(); }
uint64_t CopyAnchors(float* out) { return W().CopyAnchors(out); }
uint64_t FrontSequence() { return W().FrontSequence(); }
bool FuseBackpressure() { return W().FuseBackpressure(); }

} // namespace world
} // namespace fs

using fs::world::W; using fs::world::EnsureInit;

FS_API int32_t FsWorld_CreateSynthetic(int32_t kind, float extentM, float surfelSpacingM, uint32_t seed) { EnsureInit(); return W().CreateSynthetic(kind, extentM, surfelSpacingM, seed); }
FS_API int32_t FsWorld_GetPageCount(int32_t* resident, int32_t* logical) { EnsureInit(); W().GetPageCount(resident, logical); return 0; }
FS_API int32_t FsWorld_GetSurfelCount(int64_t* frontTotal, int64_t* backTotal) { EnsureInit(); W().GetSurfelCount(frontTotal, backTotal); return 0; }
FS_API uint32_t FsWorld_GetFrontGeneration(void) { EnsureInit(); return W().FrontGeneration(); }
FS_API int32_t FsWorld_SetAnchorTransform(int32_t anchorId, const float worldFromAnchor[16]) { EnsureInit(); return W().SetAnchor(anchorId, worldFromAnchor); }
FS_API int32_t FsWorld_Erase(const float center[3], float radius, int32_t anchorId) { EnsureInit(); if (!center) return 2; return W().Erase(center, radius, anchorId); }
FS_API int32_t FsWorld_Reset(void) { EnsureInit(); return W().Reset(); }
FS_API int32_t FsWorld_GetTelemetryJson(char* buf, int32_t cap) { EnsureInit(); return fs::CopyJsonOut(W().TelemetryJson(), buf, cap); }
FS_API int32_t FsResidency_SetCenter(const float predictedPos[3], float innerRadius, float warmRadius, float prefetchRadius) { EnsureInit(); if (!predictedPos) return 2; return W().SetCenter(predictedPos, innerRadius, warmRadius, prefetchRadius); }
FS_API int32_t FsResidency_GetZoneStats(int64_t out[8]) { EnsureInit(); if (!out) return 2; W().ZoneStats(out); return 0; }
FS_API int32_t FsMeas_Push(const FsSurfaceMeasurement* items, int32_t count, int32_t anchorId) { EnsureInit(); return W().Push(items, count, anchorId); }
