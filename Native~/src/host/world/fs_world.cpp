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
struct EpochStats { int64_t fuseUs = 0, publishUs = 0; uint64_t epochs = 0; };

inline void AtomicStoreU32(uint32_t* p, uint32_t v) { __atomic_store_n(p, v, __ATOMIC_RELEASE); }
inline uint32_t AtomicLoadU32(const uint32_t* p) { return __atomic_load_n(p, __ATOMIC_ACQUIRE); }

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
        const VkDeviceSize poolWords = FS_POOL_COUNT * FS_POOL_HEADER_WORDS + (VkDeviceSize)leafCap_ + nodeCap_ + rblockCap_ + rnodeCap_;
        const VkDeviceSize dirtyWords = (VkDeviceSize)FS_MAX_PAGES * 1024 + (VkDeviceSize)FS_MAX_PAGES * 160 + (VkDeviceSize)7 * FS_DIRTY_NODES_MAX;
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
        hashMirror_ = (FsPageHashEntry*)buf_.hash.mapped; hash_.Init(hashCap_); memcpy(hashMirror_, hash_.Data(), hash_.Bytes()); hashDirty_ = false;
        uint32_t* pools = (uint32_t*)buf_.pools.mapped;
        uint32_t ringBase = FS_POOL_COUNT * FS_POOL_HEADER_WORDS;
        const uint32_t caps[4] = {leafCap_, nodeCap_, rblockCap_, rnodeCap_};
        for (uint32_t p = 0; p < 4; ++p) { rings_[p].Init(caps[p], pools + p * FS_POOL_HEADER_WORDS, pools + ringBase, ringBase); ringBase += caps[p]; }
        cpuRec_ = (FsSurfaceMeasurement*)cpuRecords_.mapped; cpuCtr_ = (uint32_t*)cpuCounters_.mapped; memset(cpuCtr_, 0, 64);
        fuseInFlight_ = publishInFlight_ = releaseInFlight_ = 0; publishWanted_ = false; cpuSlotReady_ = false; cpuSlotBusy_ = false; liveLease_ = 0;
        idBase_ = kSurfaceIdBase; tick_ = 0; toPublish_.clear(); publishSeq_ = 0;
        Log("FS-WORLD pools: %.1f MiB total (surfels %.1f, evidence %.1f, index %.1f, render nodes %.1f, render blocks %.1f, rdir %.1f)",
            bytesTotal_ / 1048576.0, buf_.surfels.size / 1048576.0, buf_.evidence.size / 1048576.0, buf_.index.size / 1048576.0, buf_.render.size / 1048576.0, buf_.rblocks.size / 1048576.0, buf_.rdir.size / 1048576.0);
        return true;
    }
    void DestroyArenas() {
        Buffer* all[] = {&buf_.pages, &buf_.surfels, &buf_.evidence, &buf_.index, &buf_.gctr, &buf_.meas, &buf_.assocA, &buf_.assocB, &buf_.sort, &buf_.freeSpace,
                         &buf_.pools, &buf_.dirty, &buf_.render, &buf_.rblocks, &buf_.rdir, &buf_.retire, &buf_.pending, &buf_.publishRing, &buf_.hash, &cpuRecords_, &cpuCounters_};
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
    bool JobsIdle() const { return fuseInFlight_ == 0 && publishInFlight_ == 0 && releaseInFlight_ == 0; }

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
        for (uint32_t slab : s.slabs) slabs_.Free(slab);
        s.slabs.clear(); s.life = PAGE_FREE; s.generation++;
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
    void BeforeJobs() {                              // epoch boundary: slabs, rings, hash mirror (no job in flight)
        TopUpSlabs();
        for (IdRing& r : rings_) r.Refill();
        if (hashDirty_) { memcpy(hashMirror_, hash_.Data(), hash_.Bytes()); hashDirty_ = false; }
        std::atomic_thread_fence(std::memory_order_release);
    }
    // Fuse job: ingest -> associate -> free space -> 4 x (hist, scan, scatter) -> reduce -> cluster count -> prefix -> carry -> cluster write.
    bool SubmitFuse(const Pipeline& ingest, uint32_t offset, uint32_t maxCount, int32_t anchorId, bool freeSpace, const float eye0[3], const float eye1[3], uint64_t waitFrameEnd, std::function<void(bool)> onDone) {
        auto js = std::make_shared<JobStorage>();
        const Buffer* G = &buf_.gctr;
        const uint32_t tick = ++tick_;
        PushIngest pin{maxCount, tick, idBase_, idxLeafBase_, idxNodeBase_, offset, pageCount_};
        PushAssoc pa{hashCap_ - 1, anchorId};
        PushFreeSpace pf{hashCap_ - 1, anchorId, freeSpace ? FS_FUSE_FLAG_FREE_SPACE : 0u, 0, {eye0[0], eye0[1], eye0[2], 0}, {eye1[0], eye1[1], eye1[2], 0}};
        PushShift sh[4] = {{0}, {8}, {16}, {24}};
        AddDispatch(*js, ingest, (FS_TICK_MEAS_MAX + FS_WG_SMALL - 1) / FS_WG_SMALL, 1, &pin, sizeof pin);
        AddDispatch(*js, pipes_[K_ASSOC], 1, 1, &pa, sizeof pa, G, FS_GCTR_COUNT + FS_T_ARGS_MEAS);
        AddDispatch(*js, pipes_[K_FREESPACE], 1, 1, &pf, sizeof pf, G, FS_GCTR_COUNT + FS_T_ARGS_MEAS);
        for (uint32_t p = 0; p < 4; ++p) {
            const bool fromA = (p & 1) == 0;
            AddDispatch(*js, fromA ? pipes_[K_SORT_HIST] : histB_, 1, 1, &sh[p], sizeof sh[p], G, FS_GCTR_COUNT + FS_T_ARGS_SORTBLK);
            AddDispatch(*js, pipes_[K_SORT_SCAN], 1, 1, nullptr, 0);
            AddDispatch(*js, fromA ? pipes_[K_SORT_SCATTER] : scatterB_, 1, 1, &sh[p], sizeof sh[p], G, FS_GCTR_COUNT + FS_T_ARGS_SORTBLK);
        }
        AddDispatch(*js, pipes_[K_REDUCE], 1, 1, nullptr, 0, G, FS_GCTR_COUNT + FS_T_ARGS_MEAS);
        AddDispatch(*js, pipes_[K_CLUSTER_COUNT], FS_TICK_MEAS_MAX / FS_WG_SMALL, 1, nullptr, 0);
        AddDispatch(*js, pipes_[K_PREFIX], FS_TICK_MEAS_MAX / 256, 1, nullptr, 0);
        AddDispatch(*js, pipes_[K_PREFIX_CARRY], 1, 1, nullptr, 0);
        AddDispatch(*js, pipes_[K_CLUSTER_WRITE], 1, 1, nullptr, 0, G, FS_GCTR_COUNT + FS_T_ARGS_MEAS);
        FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_SCAN; jd.name = "world.fuse"; jd.dispatches = js->d.data(); jd.dispatchCount = (uint32_t)js->d.size(); jd.waitFrameEndValue = waitFrameEnd;
        jd.onRetired = [this, js, onDone](bool ok, uint64_t s, uint64_t e) {
            std::lock_guard<std::recursive_mutex> g(m_);
            fuseInFlight_--;
            if (ok) { stats_.fuseUs = e > s ? (int64_t)((e - s) / 1000) : 0; AfterJob(false); QuantumGate(stats_.fuseUs, FS_JOB_SCAN, epochMeasCap_, FS_EPOCH_MEAS_MIN, FS_TICK_MEAS_MAX); }
            onDone(ok);
        };
        if (!SubmitJob(jd)) { tick_--; return false; }
        fuseInFlight_++;
        return true;
    }
    // Publish job: dirty compaction -> maintenance (count, prefix, apply) -> leaves -> levels 1..5. Waits on the SCAN timeline.
    bool SubmitPublish(uint64_t waitScanValue, std::vector<std::pair<uint32_t, bool>> loads) {
        auto js = std::make_shared<JobStorage>();
        const Buffer* G = &buf_.gctr;
        PushPageCount ppc{pageCount_, epochDirtyCap_};
        for (auto& l : loads) { PushPage pp{l.first}; AddDispatch(*js, pipes_[K_PAGE_LOAD], FS_CELLS_PER_PAGE / FS_WG_SMALL, 1, &pp, sizeof pp); }
        AddDispatch(*js, pipes_[K_DIRTY_PAGES], pageCount_, 1, nullptr, 0);
        AddDispatch(*js, pipes_[K_DIRTY_PREFIX], 1, 1, &ppc, sizeof ppc);
        AddDispatch(*js, pipes_[K_DIRTY_EMIT], pageCount_, 1, nullptr, 0);
        AddDispatch(*js, pipes_[K_MAINT_COUNT], FS_TICK_MEAS_MAX / FS_WG_SMALL, 1, nullptr, 0);
        AddDispatch(*js, pipes_[K_PREFIX], FS_TICK_MEAS_MAX / 256, 1, nullptr, 0);
        AddDispatch(*js, pipes_[K_PREFIX_CARRY], 1, 1, nullptr, 0);
        AddDispatch(*js, pipes_[K_MAINT_APPLY], 1, 1, nullptr, 0, G, FS_GCTR_COUNT + FS_T_ARGS_DIRTY);
        AddDispatch(*js, pipes_[K_PUBLISH_LEAVES], 1, 1, nullptr, 0, G, FS_GCTR_COUNT + FS_T_ARGS_DIRTY);
        for (uint32_t lv = 1; lv <= FS_RENDER_TREE_DEPTH; ++lv) { PushLevel pl{lv}; AddDispatch(*js, pipes_[K_PUBLISH_LEVEL], 1, 1, &pl, sizeof pl, G, FS_GCTR_COUNT + FS_T_LEVEL_ARGS + 4 * lv); }
        FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_PUBLISH; jd.name = "world.publish"; jd.dispatches = js->d.data(); jd.dispatchCount = (uint32_t)js->d.size();
        if (waitScanValue) { jd.waitClass = FS_JOB_SCAN; jd.waitTimelineValue = waitScanValue; }
        jd.onRetired = [this, js](bool ok, uint64_t s, uint64_t e) {
            std::lock_guard<std::recursive_mutex> g(m_);
            publishInFlight_--;
            if (ok) { stats_.publishUs = e > s ? (int64_t)((e - s) / 1000) : 0; stats_.epochs++; AfterJob(true); QuantumGate(stats_.publishUs, FS_JOB_PUBLISH, epochDirtyCap_, 1024u, FS_DIRTY_CELLS_MAX); if (AtomicLoadU32(&gctr_[FS_GCTR_DIRTY_OVERFLOW]) != dirtyOverflowSeen_) { dirtyOverflowSeen_ = AtomicLoadU32(&gctr_[FS_GCTR_DIRTY_OVERFLOW]); publishWanted_ = true; } }
        };
        if (!SubmitJob(jd)) return false;
        publishInFlight_++; publishWanted_ = false;
        for (auto& l : loads) pages_[l.first].loadPending = false;
        return true;
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
        if (!SubmitJob(jd)) return false;
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
        if (!SubmitJob(jd)) return false;
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
        if (!publish) { idBase_ += AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_NEW_TOTAL) + 2 * AtomicLoadU32(gctr_ + FS_GCTR_SPLITS) - 2 * splitsSeen_; splitsSeen_ = AtomicLoadU32(gctr_ + FS_GCTR_SPLITS); return; }
        PublishBatch batch;
        CollectRetire(batch.renderRetire);
        uint32_t n = std::min<uint32_t>(AtomicLoadU32(gctr_ + FS_GCTR_COUNT + FS_T_PENDING_COUNT), FS_PENDING_PUBLISH_MAX);
        for (uint32_t i = 0; i < n; ++i) batch.entries.push_back(pending_[i]);
        AtomicStoreU32(gctr_ + FS_GCTR_COUNT + FS_T_PENDING_COUNT, 0);
        idBase_ += 2 * (AtomicLoadU32(gctr_ + FS_GCTR_SPLITS) - splitsSeen_); splitsSeen_ = AtomicLoadU32(gctr_ + FS_GCTR_SPLITS);
        if (!batch.entries.empty() || !batch.renderRetire.empty()) toPublish_.push_back(std::move(batch));
    }
    // Measured GPU time of the job vs its class quantum: over -> halve the epoch cap (slice more), under -> grow.
    void QuantumGate(int64_t gpuUs, FsJobClass cls, uint32_t& cap, uint32_t minCap, uint32_t maxCap) {
        int64_t stats[8]; uint32_t quantum = 2000;
        if (ExecClassStats(cls, stats)) quantum = (uint32_t)stats[7];
        if (gpuUs > (int64_t)(quantum * FS_EPOCH_OVER_K) && cap > minCap) { cap = std::max<uint32_t>(cap / 2, minCap); capHalvings_++; Log("FS-WORLD quantum gate: class %d job %lld us > %u us quantum -> epoch cap %u", (int)cls, (long long)gpuUs, quantum, cap); }
        else if (gpuUs < (int64_t)(quantum * FS_EPOCH_UNDER_K) && cap < maxCap) cap = std::min<uint32_t>(cap * 2, maxCap);
    }
    void FoldGpuCounters() {
        if (!gctr_) return;
        for (uint32_t i = 0; i < FS_GCTR_COUNT; ++i) { uint32_t v = AtomicLoadU32(&gctr_[i]); gctrTotal_[i] += (uint64_t)(v - gctrLast_[i]); gctrLast_[i] = v; }
        static const int32_t map[FS_GCTR_COUNT] = { FS_CTR_PAGE_LOOKUPS, FS_CTR_PAGE_MISSES, -1, -1, -1, FS_CTR_SURFEL_UPDATE, -1, -1, FS_CTR_SURFEL_CREATE, -1,
            FS_CTR_SURFEL_SPLIT, FS_CTR_SURFEL_MERGE, FS_CTR_SURFEL_DELETE, -1, -1, FS_CTR_INDEX_OVERFLOW, -1, -1, -1, FS_CTR_PUBLISH, FS_CTR_MEASUREMENTS_DROPPED, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
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
            rootsPublished_ += count;
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
        // page releases (eviction / reset) go first: they need no measurements
        if (!releaseQueue_.empty()) {
            std::vector<uint32_t> batch; while (!releaseQueue_.empty() && batch.size() < kReleasesPerJob) { batch.push_back(releaseQueue_.front()); releaseQueue_.erase(releaseQueue_.begin()); }
            PrepareEpochWords(tick_);
            if (!SubmitRelease(batch)) for (uint32_t s : batch) releaseQueue_.push_back(s);
            return;
        }
        for (; eraseQueue_.size();) { EraseReq r = eraseQueue_.front(); eraseQueue_.erase(eraseQueue_.begin()); PrepareEpochWords(tick_); if (!SubmitErase(r.c, r.r, r.anchor)) break; return; }
        if (budgetUs == 0) return;
        // a measurement frame (sliced by the epoch cap): the current slice state, a live ring slot (latest only) or the CPU slot
        if (slice_.count == 0 || slice_.offset >= slice_.count) {
            meas::MeasGpuFrame f;
            bool live = liveBound_ && meas::MeasGpu_PeekFrame(f);
            if (live) { slice_ = SliceState{}; slice_.live = true; slice_.seq = f.sequence; slice_.slot = f.slot; slice_.count = std::min(f.ring->capacity, std::max(f.count, 1u)); slice_.anchor = f.anchorId; slice_.eyeValid = f.eyeOriginValid; memcpy(slice_.eye, f.eyeOrigin, sizeof slice_.eye); slice_.importFrameEnd = f.importFrameEnd; scanAnchor_ = f.anchorId; }
            else if (cpuSlotReady_) { slice_ = SliceState{}; slice_.live = false; slice_.count = std::max(cpuCtr_[0], 1u); slice_.anchor = scanAnchor_; slice_.eyeValid = cpuEyeValid_; memcpy(slice_.eye, cpuEye_, sizeof slice_.eye); cpuSlotBusy_ = true; cpuSlotReady_ = false; }
            else slice_.count = 0;
        }
        if (slice_.count != 0 && slice_.offset < slice_.count) {
            const uint32_t n = std::min<uint32_t>(epochMeasCap_, slice_.count - slice_.offset);
            const bool last = slice_.offset + n >= slice_.count;
            const bool live = slice_.live; const uint64_t seq = slice_.seq;
            bool ok = SubmitFuse(live ? ingestLive_[slice_.slot] : ingestCpu_, slice_.offset, n, slice_.anchor, slice_.eyeValid, slice_.eye[0], slice_.eye[1], live ? slice_.importFrameEnd : 0,
                                 [this, live, seq, last](bool) { if (!last) return; if (live) meas::MeasGpu_ReleaseFrame(seq); else cpuSlotBusy_ = false; });
            if (!ok) return;                                   // deferred by the executor (ring/budget): retried next tick, same slice
            slice_.offset += n; slices_++;
            if (last) slice_.count = 0;
            CounterAdd(FS_CTR_SCAN_TICK, 1);
            uint64_t v = ClassTimelineAllocated(FS_JOB_SCAN);
            std::vector<std::pair<uint32_t, bool>> loads; TakeLoads(loads);
            if (!SubmitPublish(v, loads)) publishWanted_ = true;
            return;
        }
        if (publishWanted_ || !pendingLoads_.empty()) { PrepareEpochWords(tick_); std::vector<std::pair<uint32_t, bool>> loads; TakeLoads(loads); SubmitPublish(0, loads); }
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
        if (releaseQueue_.empty()) { idBase_ = kSurfaceIdBase; tick_ = 0; splitsSeen_ = 0; resetPending_ = false; resets_++; Log("FS-WORLD reset complete"); }
        else resetDraining_ = true;
        if (resetDraining_ && releaseQueue_.empty()) { idBase_ = kSurfaceIdBase; tick_ = 0; splitsSeen_ = 0; resetPending_ = false; resetDraining_ = false; resets_++; Log("FS-WORLD reset complete (pages released)"); }
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
        std::lock_guard<std::recursive_mutex> g(m_);
        JsonWriter w; w.BeginObject();
        w.KV("epochs", stats_.epochs); w.KV("tick", tick_); w.KV("fuseGpuUsLast", stats_.fuseUs); w.KV("publishGpuUsLast", stats_.publishUs);
        w.KV("idBase", idBase_); w.KV("rootsPublished", rootsPublished_); w.KV("pendingBatches", (uint64_t)toPublish_.size()); w.KV("retireBacklogIds", retireBacklogIds_); w.KV("retirementBacklog", (uint64_t)RetirementBacklog());
        int32_t active = 0, releasing = 0; for (const PageState& s : pages_) { if (s.life == PAGE_ACTIVE) active++; if (s.life == PAGE_RELEASING) releasing++; }
        w.KV("pagesActive", active); w.KV("pagesReleasing", releasing); w.KV("pagesLogical", (uint64_t)logical_.size()); w.KV("pagesCreated", pagesCreated_); w.KV("pagesReleased", pagesReleased_);
        w.KV("shortfallTotal", shortfallTotal_); w.KV("slabStalls", slabStalls_); w.KV("resets", resets_);
        w.KV("epochMeasCap", epochMeasCap_); w.KV("epochDirtyCap", epochDirtyCap_); w.KV("slices", slices_); w.KV("capHalvings", capHalvings_);
        w.Key("fusion"); w.BeginObject();
        static const char* names[FS_GCTR_COUNT] = {"pageLookups","pageMisses","assocRecords","assocMatched","assocUnmatched","segmentsMatched","contributions","segOverflow","newSurfels","newShortfall","splits","merges","ghosts","candidateOverflow","indexLeafSplits","indexOverflow","dirtyCells","cowNodes","renderBlocks","rootsPending","measOutOfRange","dirtyOverflow","poolLeafEmpty","poolNodeEmpty","poolRBlockEmpty","poolRNodeEmpty","freeStamps","segmentsUnmatched","freeHopOverflow","_29","nextSurfaceId","_31"};
        for (uint32_t i = 0; i < FS_GCTR_COUNT; ++i) if (names[i][0] != '_') w.KV(names[i], gctrTotal_[i]);
        w.EndObject();
        w.Key("memory"); w.BeginObject();
        w.KV("bytesCommitted", bytesTotal_); w.KV("slabsLive", slabs_.Live()); w.KV("slabsTotal", slabs_.Total()); w.KV("canonicalBytesLive", (uint64_t)slabs_.Live() * FS_SLAB_SURFELS * (sizeof(FsSurfel) + sizeof(FsSurfelEvidence)));
        const char* pn[4] = {"indexLeaves", "indexNodes", "renderBlocks", "renderNodes"}; const uint64_t pb[4] = {64, 32, FS_RENDER_BLOCK_SURFELS * sizeof(FsSurfel), sizeof(FsRenderNode)};
        for (uint32_t p = 0; p < 4; ++p) { w.Key(pn[p]); w.BeginObject(); w.KV("live", rings_[p].Live()); w.KV("capacity", rings_[p].Capacity()); w.KV("bytesLive", (uint64_t)rings_[p].Live() * pb[p]); w.KV("allocFailures", rings_[p].Failures()); w.EndObject(); }
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
    SlabAllocator slabs_; IdRing rings_[4];
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
    uint32_t fuseInFlight_ = 0, publishInFlight_ = 0, releaseInFlight_ = 0;
    bool publishWanted_ = false, resetPending_ = false, resetDraining_ = false;
    uint32_t tick_ = 0, idBase_ = kSurfaceIdBase, splitsSeen_ = 0; int32_t scanAnchor_ = 0;
    // Quantum gate (C09R review gap 1): epoch caps adapt to the measured job GPU time vs the class quantum; a frame
    // larger than the cap is processed in slices (offset walks the frame), dirty cells beyond the cap stay dirty.
    uint32_t epochMeasCap_ = FS_TICK_MEAS_MAX, epochDirtyCap_ = FS_DIRTY_CELLS_MAX; uint64_t slices_ = 0, capHalvings_ = 0;
    struct SliceState { bool live = false; uint64_t seq = 0; uint32_t slot = 0, offset = 0, count = 0; int32_t anchor = 0; bool eyeValid = false; float eye[2][3] = {}; uint64_t importFrameEnd = 0; } slice_;
    std::vector<uint32_t> releaseQueue_; std::vector<std::pair<uint32_t, uint32_t>> pendingLoads_;
    std::deque<PublishBatch> toPublish_;
    std::vector<EraseReq> eraseQueue_;
    std::vector<FsSurfaceMeasurement> feed_, pushStaging_; size_t feedCursor_ = 0; uint32_t feedObs_ = 0; float feedEye_[2][3] = {};
    EpochStats stats_; uint32_t dirtyOverflowSeen_ = 0;
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
