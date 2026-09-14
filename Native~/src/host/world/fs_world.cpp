// FinalScan world module (C04 surfel ABI + synthetic world, C05b ClusterTree, C06 immutable publication,
// C08 addressing). Implements FsWorld_*, FsResidency_*, FsMeas_* on top of the executor (fs_executor.h).
//
// Division of labour (hard rule): the GPU owns page-level work selection. The CPU only (a) decides
// residency (which logical pages occupy which slots, cold-store/SSD stand-in copies), (b) sets per-slot
// flags in a host-visible buffer, (c) submits ONE maintenance job (collect -> batched index rebuild ->
// collect -> batched publish -> GPU root-last commit -> batched cluster levels -> tree commit) and ONE
// scan job per tick, (d) reads host-visible counters after fences retired (never a blocking readback).
// Page generations / roots are written by the GPU (world_publish_commit.comp); the host mirror is telemetry.
//
// Threads: exports run on Unity's main thread (FsMeas_Push on sensor threads); the scheduler tick and
// every onRetired callback run on the fs-sched thread; the device hook on the render thread. One
// recursive mutex guards world state; the measurement ring is lock-free.
//
// Slot arenas (FS_RANGES_PER_SLOT = 3 surfel ranges per slot): BACK (scan writes, page-local cell index)
// + FRONT parity 0/1 (Morton-sorted immutable snapshots). See README.md for the index variant and limits.
#include "fs_world.h"
#include "fs_world_kernels.h"
#include "fs_page_hash.h"
#include "fs_meas_ring.h"
#include "fs_residency_math.h"
#include "fs_synthetic.h"
#include "fs_cluster_build.h"
#include "../measure/fs_meas_gpu.h"
#include "../../log.h"
#include <algorithm>
#include <atomic>
#include <memory>
#include <mutex>
#include <string.h>
#include <unordered_map>
#include <vector>

#define FS_API extern "C" __attribute__((visibility("default")))

namespace fs {
namespace world {
namespace {

constexpr uint32_t kLoadsPerTick = 2, kEvictsPerTick = 2, kUploadsPerTick = 4;
constexpr uint32_t kSurfaceIdBase = 1u << 20;    // GPU-created surface ids start here (synthetic ids are small)
constexpr uint32_t kFeedPerTick = FS_INTEGRATE_MAX_MEAS;
constexpr float    kPredictHorizonS = 0.5f, kPredictMaxShiftM = 1.5f;
constexpr int      kResultOk = 0, kResultUnavailable = 1, kResultInvalid = 2, kResultBusy = 4;

struct KeyHasher { size_t operator()(const FsPageKey& k) const { return HashPageKey(k); } };
struct KeyEqual  { bool operator()(const FsPageKey& a, const FsPageKey& b) const { return KeyEq(a, b); } };

struct ColdPage {                 // RAM stand-in for the C14 page store (evicted / not yet resident pages)
    std::vector<FsSurfel> surfels; std::vector<FsSurfelEvidence> evidence;
    std::vector<FsClusterNode> nodes; std::vector<FsClusterError> errors;   // optional prebuilt tree (synthetic upload)
    uint32_t leafShift = 0; bool sortedFront = false;
};
struct LogicalPage { FsPageKey key{}; uint32_t slot = FS_INDEX_NONE; ColdPage cold; bool hasCold = false; uint8_t zone = ZONE_OUTSIDE; };
struct SlotState { bool used = false, big = false; FsPageKey key{}; uint32_t generation = 0, lastTouched = 0; uint8_t zone = ZONE_OUTSIDE; };
struct JobStorage { std::vector<Dispatch> d; std::vector<uint8_t> push; };

inline void AtomicStoreU32(uint32_t* p, uint32_t v) { __atomic_store_n(p, v, __ATOMIC_RELEASE); }
inline uint32_t AtomicLoadU32(const uint32_t* p) { return __atomic_load_n(p, __ATOMIC_ACQUIRE); }

class World {
public:
    // Registration with the executor happens at library load (static AutoInit below) so the world pipelines are
    // part of the regular warm-up before FS_HOST_READY (contract §15.7: never a compile spike after READY). The
    // arena sizes come from FsHostConfig, which exists only after FsHost_Init: Configure() runs in the device hook.
    void Init() {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (inited_) return;
        inited_ = true;
        for (int a = 0; a < FS_MAX_ANCHORS; ++a) { memset(anchors_[a], 0, sizeof anchors_[a]); anchors_[a][0] = anchors_[a][5] = anchors_[a][10] = anchors_[a][15] = 1.f; }
        RegisterDeviceHook([this](bool up) { OnDevice(up); });
        RegisterWarmupStep("world.pipelines", [this]() { return CreatePipelines(); });
        RegisterSchedulerTick([this](uint32_t budgetUs) { Tick(budgetUs); });
        RegisterLeaseValidator([this](uint32_t slot, uint32_t gen) { return slot < slots_.size() && slots_[slot].generation == gen; });
        if (ExecReady()) OnDevice(true);
    }
    void Configure() {
        if (configured_) return;
        configured_ = true;
        const FsHostConfig* cfg = ExecConfig();
        stdSlots_ = cfg && cfg->residentPageSlots ? cfg->residentPageSlots : FS_DEFAULT_RESIDENT_SLOTS;
        stdCap_   = cfg && cfg->surfelsPerPage ? cfg->surfelsPerPage : FS_DEFAULT_SURFELS_PER_PAGE;
        hashCap_  = Pow2(cfg && cfg->pageHashCapacity ? cfg->pageHashCapacity : FS_DEFAULT_PAGE_HASH_CAPACITY);
        ringCap_  = Pow2(cfg && cfg->measurementRingCapacity ? cfg->measurementRingCapacity : FS_DEFAULT_MEAS_RING_CAPACITY);
        bigSlots_ = FS_BIG_SLOTS; bigCap_ = std::min<uint32_t>(stdCap_ * FS_BIG_SLOT_FACTOR, FS_MAX_FRONT_COUNT + 1);
        if (stdSlots_ + bigSlots_ > FS_MAX_SLOTS) stdSlots_ = FS_MAX_SLOTS - bigSlots_;
        hash_.Init(hashCap_); ring_.Init(ringCap_);
        slots_.assign(stdSlots_ + bigSlots_, SlotState{});
        for (uint32_t i = stdSlots_; i < slots_.size(); ++i) slots_[i].big = true;
        Log("FS-WORLD configured: slots=%u(+%u big) cap=%u/%u hash=%u ring=%u", stdSlots_, bigSlots_, stdCap_, bigCap_, hashCap_, ringCap_);
    }
    static uint32_t Pow2(uint32_t v) { uint32_t p = 1; while (p < v && p < (1u << 30)) p <<= 1; return p; }

    // ---- device lifecycle --------------------------------------------------------------------------
    void OnDevice(bool up) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (up == deviceUp_) return;
        if (!up) {
            DestroyArenas(); deviceUp_ = false; bound_ = false; liveBound_ = false;
            pipesReady_ = false; for (Pipeline& p : pipes_) p = Pipeline{}; for (Pipeline& p : liveIntegrate_) p = Pipeline{}; for (Pipeline& p : liveArgs_) p = Pipeline{};   // died with the device objects
            return;
        }
        Configure();
        if (!CreateArenas()) { LogError("FS-WORLD arena creation failed; world stays inactive"); DestroyArenas(); return; }
        deviceUp_ = true;
        EnsureBound();
    }
    bool CreateArenas() {
        uint32_t S = (uint32_t)slots_.size();
        for (auto& p : pages_) p.second.slot = FS_INDEX_NONE;          // device (re)init: everything reloads from cold
        for (SlotState& s : slots_) { bool big = s.big; uint32_t gen = s.generation + 1; s = SlotState{}; s.big = big; s.generation = gen; }
        buf_.slotCount = S;
        uint32_t surfelCursor = 0, indexCursor = 2 * S, nodeCursor = 0, freeCursor = 0;
        table_.reset(new SlotTable()); memset(table_.get(), 0, sizeof(SlotTable));
        for (uint32_t i = 0; i < S; ++i) {
            FsSlotLayout& l = table_->layouts[i];
            l.capacity = slots_[i].big ? bigCap_ : stdCap_;
            l.surfelBase = surfelCursor; surfelCursor += l.capacity * FS_RANGES_PER_SLOT;
            l.cellBase = indexCursor; indexCursor += 2 * FS_CELLS_PER_PAGE;
            l.microBase = indexCursor; indexCursor += FS_MICRO_BLOCKS_PER_SLOT * 8;
            l.linkBase = indexCursor; indexCursor += l.capacity;
            l.nodesPerSlot = NodesPerSlotFor(l.capacity);
            l.nodeBase = nodeCursor; nodeCursor += 2 * l.nodesPerSlot;
            l.freeSpaceBase = freeCursor; freeCursor += FS_CELLS_PER_PAGE;
        }
        buf_.totalSurfels = surfelCursor; buf_.totalNodes = nodeCursor;
        maxCapGroups_ = (bigCap_ + FS_WG_SMALL - 1) / FS_WG_SMALL;
        const VkBufferUsageFlags use = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
        bool ok = true;
        ok &= CreateBuffer(buf_.slotTable, sizeof(SlotTable), use, true, "world.slotTable");
        ok &= CreateBuffer(buf_.surfels, (VkDeviceSize)surfelCursor * sizeof(FsSurfel), use, true, "world.surfels");
        ok &= CreateBuffer(buf_.evidence, (VkDeviceSize)surfelCursor * sizeof(FsSurfelEvidence), use, true, "world.evidence");
        ok &= CreateBuffer(buf_.index, (VkDeviceSize)indexCursor * 4, use, false, "world.index");
        ok &= CreateBuffer(buf_.gctr, (VkDeviceSize)FS_G_WORDS * 4, use | VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT, true, "world.gctr");
        ok &= CreateBuffer(buf_.nodes, (VkDeviceSize)nodeCursor * sizeof(FsClusterNode), use, true, "world.nodes");
        ok &= CreateBuffer(buf_.errors, (VkDeviceSize)nodeCursor * sizeof(FsClusterError), use, true, "world.errors");
        ok &= CreateBuffer(buf_.hash, (VkDeviceSize)hashCap_ * sizeof(FsPageHashEntry), use, true, "world.hash");
        ok &= CreateBuffer(buf_.meas, (VkDeviceSize)ringCap_ * sizeof(FsSurfaceMeasurement), use, true, "world.meas");
        ok &= CreateBuffer(buf_.scratch, (VkDeviceSize)FS_MAINT_MAX_PAGES * (FS_CELLS_PER_PAGE + 16) * 4, use, false, "world.scratch");
        ok &= CreateBuffer(buf_.freeSpace, (VkDeviceSize)freeCursor, use, true, "world.freeSpace");   // u8 stamps, must start at 0 (never seen through)
        if (!ok) return false;
        if (!buf_.freeSpace.mapped) { LogError("FS-WORLD free-space arena not mapped"); return false; }
        memset(buf_.freeSpace.mapped, 0, freeCursor);
        if (!buf_.slotTable.mapped || !buf_.surfels.mapped || !buf_.evidence.mapped || !buf_.gctr.mapped || !buf_.nodes.mapped ||
            !buf_.errors.mapped || !buf_.hash.mapped || !buf_.meas.mapped) { LogError("FS-WORLD host-visible buffer not mapped"); return false; }
        tableGpu_ = (SlotTable*)buf_.slotTable.mapped; memcpy(tableGpu_, table_.get(), sizeof(SlotTable));
        surfels_ = (FsSurfel*)buf_.surfels.mapped; evidence_ = (FsSurfelEvidence*)buf_.evidence.mapped;
        nodes_ = (FsClusterNode*)buf_.nodes.mapped; errors_ = (FsClusterError*)buf_.errors.mapped;
        gctr_ = (uint32_t*)buf_.gctr.mapped; memset(gctr_, 0, FS_G_WORDS * 4); gctr_[FS_GCTR_NEXT_SURFACE_ID] = kSurfaceIdBase;
        memset(gctrLast_, 0, sizeof gctrLast_); gctrLast_[FS_GCTR_NEXT_SURFACE_ID] = kSurfaceIdBase;
        hashMirror_ = (FsPageHashEntry*)buf_.hash.mapped;
        hash_.Init(hashCap_); memcpy(hashMirror_, hash_.Data(), hash_.Bytes()); hashDirty_ = false;
        ring_.Init(ringCap_); ring_.Attach((FsSurfaceMeasurement*)buf_.meas.mapped);
        scanInFlight_ = maintenanceInFlight_ = eraseInFlight_ = 0; maintenanceWanted_ = false; lastMaintFrame_ = 0; liveBound_ = false; liveLease_ = 0;
        lastScanTick_ = evidenceTickDone_ = 0;
        Log("FS-WORLD arenas: surfels=%u (%.1f MB) nodes=%u index=%.1f MB", surfelCursor, surfelCursor * 40.0 / 1e6, nodeCursor, indexCursor * 4.0 / 1e6);
        return true;
    }
    void DestroyArenas() {
        Buffer* all[] = {&buf_.slotTable, &buf_.surfels, &buf_.evidence, &buf_.index, &buf_.gctr, &buf_.nodes, &buf_.errors, &buf_.hash, &buf_.meas, &buf_.scratch, &buf_.freeSpace};
        for (Buffer* b : all) if (b->buffer != VK_NULL_HANDLE) DestroyBuffer(*b);
        tableGpu_ = nullptr; surfels_ = nullptr; evidence_ = nullptr; nodes_ = nullptr; errors_ = nullptr; gctr_ = nullptr; hashMirror_ = nullptr;
        ring_.Init(ringCap_);
    }
    bool CreatePipelines() {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (pipesReady_) return true;
        for (uint32_t k = 0; k < K_COUNT; ++k) {
            const KernelSpec& ks = kWorldKernels[k];
            VkDescriptorSetLayoutBinding b[8] = {};
            for (uint32_t i = 0; i < ks.bindingCount; ++i) { b[i].binding = ks.bindings[i]; b[i].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER; b[i].descriptorCount = 1; b[i].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT; }
            if (!CreateComputePipeline(pipes_[k], ks.spirv, ks.words, ks.pushBytes, b, ks.bindingCount, ks.name)) { LogError("FS-WORLD pipeline %s failed", ks.name); return false; }
        }
        for (uint32_t r = 0; r < kLiveSlots; ++r) {
            for (uint32_t k : {(uint32_t)K_INTEGRATE, (uint32_t)K_SCAN_ARGS}) {
                const KernelSpec& ks = kWorldKernels[k];
                VkDescriptorSetLayoutBinding b[8] = {};
                for (uint32_t i = 0; i < ks.bindingCount; ++i) { b[i].binding = ks.bindings[i]; b[i].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER; b[i].descriptorCount = 1; b[i].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT; }
                Pipeline& p = k == K_INTEGRATE ? liveIntegrate_[r] : liveArgs_[r];
                if (!CreateComputePipeline(p, ks.spirv, ks.words, ks.pushBytes, b, ks.bindingCount, ks.name)) { LogError("FS-WORLD live pipeline %s[%u] failed", ks.name, r); return false; }
            }
        }
        pipesReady_ = true; bound_ = false; liveBound_ = false;
        EnsureBound();
        return true;
    }
    // Live (C09) instances: same SPIR-V, B_MEAS = the ring slot's records, B_HASH of the args kernel = its counters. Bound once.
    void EnsureLiveBound() {
        if (liveBound_ || !bound_) return;
        for (uint32_t r = 0; r < kLiveSlots; ++r) {
            const meas::MeasGpuRing* ring = r < meas::MeasGpu_RingSlots() ? meas::MeasGpu_Ring(r) : nullptr;
            if (!ring || ring->records.buffer == VK_NULL_HANDLE) return;
            const KernelSpec& ki = kWorldKernels[K_INTEGRATE];
            for (uint32_t i = 0; i < ki.bindingCount; ++i) {
                const Buffer* b = ki.bindings[i] == B_MEAS ? &ring->records : BufferFor(K_INTEGRATE, ki.bindings[i]);
                if (!b || !BindBuffer(liveIntegrate_[r], ki.bindings[i], *b)) { LogError("FS-WORLD live bind integrate[%u]:%u failed", r, ki.bindings[i]); return; }
            }
            if (!BindBuffer(liveArgs_[r], B_HASH, ring->counters) || !BindBuffer(liveArgs_[r], B_GCTR, buf_.gctr)) { LogError("FS-WORLD live bind args[%u] failed", r); return; }
        }
        liveBound_ = true;
    }
    const Buffer* BufferFor(uint32_t kernel, uint32_t binding) {
        switch (binding) {
        case B_MEAS: return &buf_.meas; case B_HASH: return &buf_.hash; case B_SLOTS: return &buf_.slotTable; case B_SURFELS: return &buf_.surfels;
        case B_EVIDENCE: return (kernel == K_CLUSTER_LEAVES || kernel == K_CLUSTER_INTERNAL) ? &buf_.errors : &buf_.evidence;
        case B_INDEX: return &buf_.index; case B_GCTR: return &buf_.gctr;
        case B_NODES:
            if (kernel == K_PUBLISH_COUNT || kernel == K_PUBLISH_SCAN || kernel == K_PUBLISH_COPY || kernel == K_PUBLISH_COMMIT) return &buf_.scratch;
            if (kernel == K_INTEGRATE || kernel == K_EVIDENCE) return &buf_.freeSpace;   // B_FREESPACE
            return &buf_.nodes;
        default: return nullptr;
        }
    }
    void EnsureBound() {
        if (bound_ || !pipesReady_ || !deviceUp_) return;
        for (uint32_t k = 0; k < K_COUNT; ++k)
            for (uint32_t i = 0; i < kWorldKernels[k].bindingCount; ++i) {
                const Buffer* b = BufferFor(k, kWorldKernels[k].bindings[i]);
                if (!b || !BindBuffer(pipes_[k], kWorldKernels[k].bindings[i], *b)) { LogError("FS-WORLD bind %s:%u failed", kWorldKernels[k].name, kWorldKernels[k].bindings[i]); return; }
            }
        bound_ = true;
        EnsureLiveBound();
    }
    bool Ready() const { return deviceUp_ && pipesReady_ && bound_ && ExecReady(); }
    bool GpuIdle() const { return scanInFlight_ == 0 && maintenanceInFlight_ == 0 && eraseInFlight_ == 0; }

    // ---- slot / page bookkeeping (residency: the CPU's job) --------------------------------------
    uint32_t AllocSlot(bool big, bool allowEvict) {
        for (uint32_t i = 0; i < slots_.size(); ++i) if (!slots_[i].used && slots_[i].big == big) return i;
        if (!allowEvict || !GpuIdle() || !centerValid_) return FS_INDEX_NONE;
        uint32_t best = FS_INDEX_NONE, bestTouched = 0xFFFFFFFFu;      // LRU among slots outside the prefetch sphere
        for (uint32_t i = 0; i < slots_.size(); ++i) {
            const SlotState& s = slots_[i];
            if (!s.used || s.big != big || s.zone != ZONE_OUTSIDE) continue;
            if (s.lastTouched < bestTouched) { bestTouched = s.lastTouched; best = i; }
        }
        if (best != FS_INDEX_NONE) { EvictSlot(best); return best; }
        return FS_INDEX_NONE;
    }
    void WriteHeader(uint32_t slot, const FsPageKey& key, uint32_t backCount, uint32_t frontCount, uint32_t parity, uint32_t generation, uint32_t nodeCount) {
        const FsSlotLayout& l = table_->layouts[slot];
        FsPageHeader h{}; h.key = key; h.slot = slot; h.generation = generation; h.frontOffset = FrontOffsetOf(l, parity); h.frontCount = frontCount;
        h.backOffset = l.surfelBase; h.backCount = backCount; h.capacity = l.capacity; h.dirty = 0; h.cellIndexOffset = l.cellBase; h.freeSpaceOffset = l.freeSpaceBase;
        h.lastTouchedFrame = FrameIndex(); h.nodeBase = l.nodeBase + parity * l.nodesPerSlot; h.nodeCount = nodeCount;
        tableGpu_->headers[slot] = h;
    }
    // Places a logical page into `slot`: BACK <- cold surfels (SSD stand-in). A sorted, prebuilt snapshot
    // (synthetic) is uploaded to FRONT parity 0 + tree and rooted immediately; otherwise the page is
    // flagged REBUILD|DIRTY and the next maintenance job indexes, publishes and builds its tree on the GPU.
    void LoadIntoSlot(LogicalPage& p, uint32_t slot) {
        SlotState& s = slots_[slot]; const FsSlotLayout& l = table_->layouts[slot];
        uint32_t n = (uint32_t)std::min<size_t>(p.cold.surfels.size(), l.capacity);
        if (n < p.cold.surfels.size()) Log("FS-WORLD page (%d,%d,%d) truncated %zu -> %u (slot capacity)", p.key.x, p.key.y, p.key.z, p.cold.surfels.size(), n);
        s.used = true; s.key = p.key; s.generation++; s.lastTouched = FrameIndex(); s.zone = p.zone;
        AtomicStoreU32(&tableGpu_->roots[slot], 0);
        if (n) { memcpy(surfels_ + l.surfelBase, p.cold.surfels.data(), n * sizeof(FsSurfel)); memcpy(evidence_ + l.surfelBase, p.cold.evidence.data(), n * sizeof(FsSurfelEvidence)); }
        bool snapshot = p.cold.sortedFront && n == p.cold.surfels.size() && !p.cold.nodes.empty() && p.cold.nodes.size() <= l.nodesPerSlot;
        uint32_t frontCount = snapshot ? n : 0;
        if (snapshot) {
            memcpy(surfels_ + FrontOffsetOf(l, 0), p.cold.surfels.data(), n * sizeof(FsSurfel));
            memcpy(evidence_ + FrontOffsetOf(l, 0), p.cold.evidence.data(), n * sizeof(FsSurfelEvidence));
            for (size_t i = 0; i < p.cold.nodes.size(); ++i) {           // leaf offsets are page-relative in the cold copy
                FsClusterNode nd = p.cold.nodes[i];
                if (nd.childCount == 0) nd.firstChildOrSurfel += FrontOffsetOf(l, 0);
                nodes_[l.nodeBase + i] = nd; errors_[l.nodeBase + i] = p.cold.errors[i];
            }
        }
        WriteHeader(slot, p.key, n, frontCount, 0, snapshot ? 1 : 0, snapshot ? (uint32_t)p.cold.nodes.size() : 0);
        std::atomic_thread_fence(std::memory_order_release);
        if (snapshot) { RootWord r; r.frontCount = n; r.published = true; r.parity = 0; r.treeValid = true; r.leafShift = p.cold.leafShift; r.generation = 1; AtomicStoreU32(&tableGpu_->roots[slot], PackRoot(r)); }
        AtomicStoreU32(&gctr_[FS_G_FLAGS + slot], FS_SLOT_FLAG_REBUILD | (snapshot ? 0u : FS_SLOT_FLAG_DIRTY));
        maintenanceWanted_ = true;
        if (!hash_.Insert(p.key, slot, s.generation)) CounterAdd(FS_CTR_PAGE_HASH_OVERFLOW, 1);
        hashDirty_ = true;
        p.slot = slot; p.hasCold = false; p.cold = ColdPage{};
        loads_++;
    }
    void EvictSlot(uint32_t slot) {                          // caller guarantees GpuIdle()
        SlotState& s = slots_[slot]; if (!s.used) return;
        const FsSlotLayout& l = table_->layouts[slot];
        uint32_t back = std::min(AtomicLoadU32(&tableGpu_->headers[slot].backCount), l.capacity);
        auto it = pages_.find(s.key);
        if (it != pages_.end()) {
            LogicalPage& p = it->second; p.cold = ColdPage{};
            p.cold.surfels.assign(surfels_ + l.surfelBase, surfels_ + l.surfelBase + back);
            p.cold.evidence.assign(evidence_ + l.surfelBase, evidence_ + l.surfelBase + back);
            p.hasCold = true; p.slot = FS_INDEX_NONE;
        }
        hash_.Erase(s.key); hashDirty_ = true;
        AtomicStoreU32(&tableGpu_->roots[slot], 0); AtomicStoreU32(&gctr_[FS_G_FLAGS + slot], 0);
        memset(&tableGpu_->headers[slot], 0, sizeof(FsPageHeader));
        uint32_t gen = s.generation + 1; bool big = s.big; s = SlotState{}; s.big = big; s.generation = gen;
        evictions_++;
    }
    // A standard slot whose BACK is FS_MIGRATE_FILL full moves its page into a big slot (16x capacity): BACK, the
    // published FRONT parity, its tree (leaf offsets rebased) and the root word are copied on the CPU (host-visible
    // UMA memory, GPU idle), the hash points at the new slot, the old slot is released. Canonical density may grow
    // (§1.0); nothing is dropped, nothing pops (the FRONT stays published through the move).
    bool MigrateToBig(uint32_t oldSlot) {
        SlotState& so = slots_[oldSlot];
        uint32_t ns = AllocSlot(true, true);
        if (ns == FS_INDEX_NONE) { migrationStalls_++; return false; }
        auto it = pages_.find(so.key); if (it == pages_.end()) return false;
        LogicalPage& p = it->second;
        const FsSlotLayout& lo = table_->layouts[oldSlot]; const FsSlotLayout& ln = table_->layouts[ns];
        const FsPageHeader ho = tableGpu_->headers[oldSlot];
        const RootWord ro = UnpackRoot(AtomicLoadU32(&tableGpu_->roots[oldSlot]));
        uint32_t back = std::min(ho.backCount, lo.capacity), front = ro.published ? std::min(ro.frontCount, lo.capacity) : 0u;
        SlotState& sn = slots_[ns];
        sn.used = true; sn.key = so.key; sn.generation++; sn.lastTouched = FrameIndex(); sn.zone = so.zone;
        AtomicStoreU32(&tableGpu_->roots[ns], 0);
        memcpy(surfels_ + ln.surfelBase, surfels_ + lo.surfelBase, back * sizeof(FsSurfel));
        memcpy(evidence_ + ln.surfelBase, evidence_ + lo.surfelBase, back * sizeof(FsSurfelEvidence));
        uint32_t nodeCount = 0;
        if (front) {
            const uint32_t oldFront = FrontOffsetOf(lo, ro.parity), newFront = FrontOffsetOf(ln, 0);
            memcpy(surfels_ + newFront, surfels_ + oldFront, front * sizeof(FsSurfel));
            memcpy(evidence_ + newFront, evidence_ + oldFront, front * sizeof(FsSurfelEvidence));
            nodeCount = ro.treeValid ? std::min(ho.nodeCount, std::min(lo.nodesPerSlot, ln.nodesPerSlot)) : 0u;
            for (uint32_t i = 0; i < nodeCount; ++i) {
                FsClusterNode nd = nodes_[ho.nodeBase + i];
                if (nd.childCount == 0) nd.firstChildOrSurfel = nd.firstChildOrSurfel - oldFront + newFront;
                nodes_[ln.nodeBase + i] = nd; errors_[ln.nodeBase + i] = errors_[ho.nodeBase + i];
            }
        }
        WriteHeader(ns, so.key, back, front, 0, ho.generation, nodeCount);
        std::atomic_thread_fence(std::memory_order_release);
        if (front) { RootWord r = ro; r.parity = 0; r.treeValid = ro.treeValid && nodeCount == ho.nodeCount; AtomicStoreU32(&tableGpu_->roots[ns], PackRoot(r)); }
        AtomicStoreU32(&gctr_[FS_G_FLAGS + ns], FS_SLOT_FLAG_REBUILD | (ho.dirty || (AtomicLoadU32(&gctr_[FS_G_FLAGS + oldSlot]) & FS_SLOT_FLAG_DIRTY) ? FS_SLOT_FLAG_DIRTY : 0u));
        if (!hash_.Insert(so.key, ns, sn.generation)) CounterAdd(FS_CTR_PAGE_HASH_OVERFLOW, 1);
        hashDirty_ = true; maintenanceWanted_ = true;
        p.slot = ns;
        // release the old slot without a cold copy (the page lives on in the big slot)
        AtomicStoreU32(&tableGpu_->roots[oldSlot], 0); AtomicStoreU32(&gctr_[FS_G_FLAGS + oldSlot], 0);
        memset(&tableGpu_->headers[oldSlot], 0, sizeof(FsPageHeader));
        uint32_t gen = so.generation + 1; bool big = so.big; so = SlotState{}; so.big = big; so.generation = gen;
        migrations_++;
        Log("FS-WORLD page (%d,%d,%d) migrated slot %u -> big slot %u: back=%u front=%u nodes=%u", sn.key.x, sn.key.y, sn.key.z, oldSlot, ns, back, front, nodeCount);
        return true;
    }
    void MigrateFullPages() {
        if (!GpuIdle()) return;
        for (uint32_t i = 0; i < stdSlots_; ++i) {
            if (!slots_[i].used) continue;
            const FsSlotLayout& l = table_->layouts[i];
            if ((float)AtomicLoadU32(&tableGpu_->headers[i].backCount) >= FS_MIGRATE_FILL * (float)l.capacity) { if (!MigrateToBig(i)) return; }
        }
    }
    LogicalPage& PageFor(const FsPageKey& key) {
        auto it = pages_.find(key);
        if (it == pages_.end()) { LogicalPage p; p.key = key; it = pages_.emplace(key, p).first; }
        return it->second;
    }
    bool CreateScanPage(const FsPageKey& key) {           // empty resident page for a key seen in the measurement stream
        LogicalPage& p = PageFor(key);
        if (p.slot != FS_INDEX_NONE) return true;
        uint32_t slot = AllocSlot(false, true);
        if (slot == FS_INDEX_NONE) { stalls_++; return false; }
        if (!p.hasCold) { p.hasCold = true; p.cold = ColdPage{}; }
        LoadIntoSlot(p, slot);
        return true;
    }

    // ---- jobs --------------------------------------------------------------------------------------
    static void AddDispatch(JobStorage& js, const Pipeline& p, uint32_t gx, const void* push, uint32_t pushBytes, const Buffer* args = nullptr, uint32_t argsWord = 0) {
        size_t off = js.push.size(); if (pushBytes) js.push.insert(js.push.end(), (const uint8_t*)push, (const uint8_t*)push + pushBytes);
        Dispatch d; d.pipeline = &p; d.gx = gx; d.push = pushBytes ? (const void*)(uintptr_t)off : nullptr; d.pushBytes = pushBytes;
        d.argsBuffer = args; d.argsOffset = (VkDeviceSize)argsWord * 4; js.d.push_back(d);
    }
    static void FixPushPointers(JobStorage& js) { for (Dispatch& d : js.d) if (d.pushBytes) d.push = js.push.data() + (uintptr_t)d.push; }
    static uint32_t Groups(uint32_t n) { return n ? (n + FS_WG_SMALL - 1) / FS_WG_SMALL : 1; }

    // ONE job, 22 bounded dispatches, every page item indirect (the GPU selects the pages):
    //   [collect publish] -> evidence (free-space contradictions, ghost removal) -> merge (§8.4)
    //   -> publish count/scan/copy -> GPU root-last commit -> BACK compaction (flags REBUILD)
    //   -> [collect rebuild] -> 4 batched index passes -> cluster leaves + 7 levels -> tree commit.
    // Pages published in this job get their index rebuilt in the same job: no scan tick sees a stale index.
    bool SubmitMaintenance() {
        auto js = std::make_shared<JobStorage>();
        const Buffer* G = &buf_.gctr;
        PushCollect c0{(uint32_t)slots_.size(), 0, maxCapGroups_}, c1{(uint32_t)slots_.size(), 1, maxCapGroups_};
        PushCommit pcm{FrameIndex()};
        PushEvidence pev{lastScanTick_ != evidenceTickDone_ ? lastScanTick_ : 0u};   // 0 = no scan tick since the last pass
        AddDispatch(*js, pipes_[K_MAINT_COLLECT], 1, &c1, sizeof c1);
        AddDispatch(*js, pipes_[K_EVIDENCE], 1, &pev, sizeof pev, G, FS_G_PUBLISH_ARGS_SURF);
        AddDispatch(*js, pipes_[K_MERGE], 1, nullptr, 0, G, FS_G_PUBLISH_ARGS_CELLS);
        AddDispatch(*js, pipes_[K_PUBLISH_COUNT], 1, nullptr, 0, G, FS_G_PUBLISH_ARGS_CELLS);
        AddDispatch(*js, pipes_[K_PUBLISH_SCAN], 1, nullptr, 0, G, FS_G_PUBLISH_ARGS_SCAN);
        AddDispatch(*js, pipes_[K_PUBLISH_COPY], 1, nullptr, 0, G, FS_G_PUBLISH_ARGS_CELLS);
        AddDispatch(*js, pipes_[K_PUBLISH_COMMIT], 1, &pcm, sizeof pcm);
        AddDispatch(*js, pipes_[K_PUBLISH_BACK], 1, nullptr, 0, G, FS_G_PUBLISH_ARGS_SURF);
        AddDispatch(*js, pipes_[K_MAINT_COLLECT], 1, &c0, sizeof c0);
        AddDispatch(*js, pipes_[K_INDEX_REBUILD], 1, nullptr, 0, G, FS_G_REBUILD_ARGS_CELLS);
        AddDispatch(*js, pipes_[K_INDEX_COUNT], 1, nullptr, 0, G, FS_G_REBUILD_ARGS_SURF);
        AddDispatch(*js, pipes_[K_INDEX_SUBDIVIDE], 1, nullptr, 0, G, FS_G_REBUILD_ARGS_CELLS);
        AddDispatch(*js, pipes_[K_INDEX_INSERT], 1, nullptr, 0, G, FS_G_REBUILD_ARGS_SURF);
        AddDispatch(*js, pipes_[K_CLUSTER_LEAVES], 1, nullptr, 0, G, FS_G_PUBLISH_ARGS_LEAVES);
        for (uint32_t lv = 1; lv < FS_CLUSTER_MAX_LEVELS; ++lv) { PushLevel pl{lv}; AddDispatch(*js, pipes_[K_CLUSTER_INTERNAL], 1, &pl, sizeof pl, G, FS_G_PUBLISH_ARGS_LEVEL + 4 * lv); }
        AddDispatch(*js, pipes_[K_CLUSTER_COMMIT], 1, nullptr, 0);
        FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_PUBLISH; jd.name = "world.maintenance"; jd.dispatches = js->d.data(); jd.dispatchCount = (uint32_t)js->d.size();
        jd.onRetired = [this, js](bool ok, uint64_t, uint64_t) {
            std::lock_guard<std::recursive_mutex> g(m_);
            maintenanceInFlight_--;
            if (!gctr_) return;
            FoldGpuCounters();
            maintenanceJobs_++;
            if (maintenanceJobs_ <= 3 || (maintenanceJobs_ % 100) == 0) LogFrontReceipt();
            bool remaining = false;                              // host-visible flags after the fence: leftovers beyond the batch cap
            for (uint32_t i = 0; i < slots_.size(); ++i) if (slots_[i].used && AtomicLoadU32(&gctr_[FS_G_FLAGS + i])) { remaining = true; break; }
            maintenanceWanted_ = remaining || !ok;
        };
        if (!SubmitJob(jd)) return false;
        maintenanceInFlight_++; maintenanceWanted_ = false; lastMaintFrame_ = FrameIndex(); evidenceTickDone_ = lastScanTick_;
        return true;
    }
    bool SubmitErase(const float c[3], float radius, int32_t anchorId) {
        auto js = std::make_shared<JobStorage>();
        PushErase pe{(uint32_t)slots_.size(), anchorId, c[0], c[1], c[2], radius, maxCapGroups_};
        AddDispatch(*js, pipes_[K_ERASE_COLLECT], 1, &pe, sizeof pe);
        AddDispatch(*js, pipes_[K_ERASE], 1, &pe, sizeof pe, &buf_.gctr, FS_G_ERASE_ARGS);
        FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_PUBLISH; jd.name = "world.erase"; jd.dispatches = js->d.data(); jd.dispatchCount = 2;
        jd.onRetired = [this, js](bool, uint64_t, uint64_t) { std::lock_guard<std::recursive_mutex> g(m_); eraseInFlight_--; maintenanceWanted_ = true; FoldGpuCounters(); };
        if (!SubmitJob(jd)) return false;
        eraseInFlight_++;
        return true;
    }
    // SCAN job: CPU pre-pass over the slice (page creation for unseen keys = residency, hash mirror) then
    // one bounded integrate dispatch. Pages whose index is not built yet defer the slice to the next tick.
    bool SubmitScan() {
        uint32_t base; uint64_t dropped;
        uint32_t n = ring_.Peek(FS_INTEGRATE_MAX_MEAS, &base, &dropped);
        if (dropped) CounterAdd(FS_CTR_MEASUREMENTS_DROPPED, (int64_t)dropped);
        if (!n) return false;
        const FsSurfaceMeasurement* ringData = (const FsSurfaceMeasurement*)buf_.meas.mapped;
        bool created = false, blocked = false; uint32_t lastSlot = FS_INDEX_NONE;
        for (uint32_t i = 0; i < n; ++i) {
            const FsSurfaceMeasurement& m = ringData[(base + i) & (ringCap_ - 1)];
            float p[3] = {m.px, m.py, m.pz}; FsPageKey key = MakePageKey(scanAnchor_, p);
            uint32_t slot;
            if (!hash_.Lookup(key, &slot)) { if (CreateScanPage(key)) created = true; continue; }
            if (slot != lastSlot) { lastSlot = slot; slots_[slot].lastTouched = FrameIndex(); if (AtomicLoadU32(&gctr_[FS_G_FLAGS + slot]) & FS_SLOT_FLAG_REBUILD) blocked = true; }
        }
        if (created || blocked) { maintenanceWanted_ = true; return false; }   // index first (next maintenance job), slice stays in the ring
        if (hashDirty_) { memcpy(hashMirror_, hash_.Data(), hash_.Bytes()); hashDirty_ = false; }
        auto js = std::make_shared<JobStorage>();
        PushIntegrate pi{base, n, ringCap_ - 1, hashCap_ - 1, scanAnchor_, ++tick_, 0u, 0u, {0, 0, 0, 0}, {0, 0, 0, 0}};   // ring measurements carry no eye origin: no free-space evidence
        AddDispatch(*js, pipes_[K_INTEGRATE], Groups(n), &pi, sizeof pi); FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_SCAN; jd.name = "world.integrate"; jd.dispatches = js->d.data(); jd.dispatchCount = 1;
        const uint32_t tick = tick_;
        jd.onRetired = [this, js, tick](bool ok, uint64_t, uint64_t) { std::lock_guard<std::recursive_mutex> g(m_); scanInFlight_--; if (ok) { maintenanceWanted_ = true; lastScanTick_ = tick; } FoldGpuCounters(); };
        if (!SubmitJob(jd)) return false;
        ring_.Advance(n); scanInFlight_++; scanPending_ = false;
        CounterAdd(FS_CTR_SCAN_TICK, 1);
        return true;
    }
    // Live scan job (C09 device ring): [scan_args: count -> indirect args] -> [integrate, indirect]. No CPU pass
    // over records; pages come from residency (INNER pages are created empty ahead of the scan).
    bool SubmitLiveScan() {
        const meas::MeasGpuFrame& f = liveFrame_;
        if (f.slot >= kLiveSlots || !f.ring) return false;
        if (hashDirty_) { memcpy(hashMirror_, hash_.Data(), hash_.Bytes()); hashDirty_ = false; }
        scanAnchor_ = f.anchorId;
        auto js = std::make_shared<JobStorage>();
        PushScanArgs pa{std::min(f.ring->capacity, std::max(f.count, 1u))};
        PushIntegrate pi{0, FS_INTEGRATE_COUNT_FROM_GCTR, f.ring->capacity - 1, hashCap_ - 1, f.anchorId, ++tick_,
                         f.eyeOriginValid ? FS_INTEGRATE_FLAG_FREE_SPACE : 0u, 0u,
                         {f.eyeOrigin[0][0], f.eyeOrigin[0][1], f.eyeOrigin[0][2], 0.f}, {f.eyeOrigin[1][0], f.eyeOrigin[1][1], f.eyeOrigin[1][2], 0.f}};
        AddDispatch(*js, liveArgs_[f.slot], 1, &pa, sizeof pa);
        AddDispatch(*js, liveIntegrate_[f.slot], 1, &pi, sizeof pi, &buf_.gctr, FS_G_SCAN_ARGS);
        FixPushPointers(*js);
        JobDesc jd; jd.cls = FS_JOB_SCAN; jd.name = "world.integrateLive"; jd.dispatches = js->d.data(); jd.dispatchCount = 2;
        jd.waitFrameEndValue = f.importFrameEnd;          // the depth image barrier of that frame precedes this job (cross-queue, §15.3)
        uint64_t seq = f.sequence; const uint32_t tick = tick_;
        jd.onRetired = [this, js, seq, tick](bool ok, uint64_t, uint64_t) {
            std::lock_guard<std::recursive_mutex> g(m_);
            scanInFlight_--; meas::MeasGpu_ReleaseFrame(seq); if (liveLease_ == seq) liveLease_ = 0;
            if (ok) { maintenanceWanted_ = true; lastScanTick_ = tick; }
            FoldGpuCounters();
        };
        if (!SubmitJob(jd)) return false;
        scanInFlight_++;
        CounterAdd(FS_CTR_SCAN_TICK, 1);
        return true;
    }
    void FoldGpuCounters() {
        if (!gctr_) return;
        static const FsCounter map[FS_GCTR_COUNT] = { FS_CTR_PAGE_LOOKUPS, FS_CTR_PAGE_MISSES, FS_CTR_CELL_LOOKUPS, FS_CTR_SURFEL_CREATE, FS_CTR_SURFEL_UPDATE,
            FS_CTR_MEASUREMENTS_DROPPED, FS_CTR_INDEX_OVERFLOW, FS_CTR_INDEX_OVERFLOW, FS_CTR_COUNT, FS_CTR_COUNT, FS_CTR_INDEX_OVERFLOW, FS_CTR_MEASUREMENTS_DROPPED,
            FS_CTR_PUBLISH, FS_CTR_SURFEL_DELETE, FS_CTR_DUPLICATE_OBSERVATIONS, FS_CTR_COUNT };
        for (uint32_t i = 0; i < FS_GCTR_COUNT; ++i) {
            uint32_t v = AtomicLoadU32(&gctr_[i]); uint32_t d = v - gctrLast_[i]; gctrLast_[i] = v;
            if (d && map[i] != FS_CTR_COUNT) CounterAdd(map[i], (int64_t)d);
        }
    }

    // ---- residency (§12): position only; orientation is not an input anywhere here ----------------
    void ApplyResidency() {
        if (!centerValid_ || appliedSeq_ == centerSeq_) return;
        appliedSeq_ = centerSeq_;
        FrameHint hint; float centre[3] = {center_[0], center_[1], center_[2]};
        if (GetFrameHint(hint)) PredictCentre(center_, hint.headVel, kPredictHorizonS, kPredictMaxShiftM, centre);
        ComputeZonePages(scanAnchor_, centre, radii_[0], radii_[1], radii_[2], zonePages_);
        inner_ = warm_ = prefetch_ = 0; requestsThisFrame_ = 0;
        for (SlotState& s : slots_) s.zone = ZONE_OUTSIDE;
        for (auto& kv : pages_) kv.second.zone = ZONE_OUTSIDE;
        uint32_t loads = 0, created = 0;
        for (const ZonePage& zp : zonePages_) {
            auto it = pages_.find(zp.key);
            if (it == pages_.end()) {
                // unknown page: INNER pages are created empty (bounded per tick) so live scans have a target;
                // WARM/PREFETCH pages wait for the C14 store
                if (zp.zone != ZONE_INNER || created >= kUploadsPerTick) continue;
                uint32_t slot = AllocSlot(false, true);
                if (slot == FS_INDEX_NONE) { stalls_++; continue; }
                LogicalPage& np = PageFor(zp.key); np.hasCold = true; np.cold = ColdPage{}; np.zone = zp.zone;
                LoadIntoSlot(np, slot); slots_[slot].zone = zp.zone; created++; inner_++;
                continue;
            }
            LogicalPage& p = it->second; p.zone = zp.zone;
            if (zp.zone == ZONE_INNER) inner_++; else if (zp.zone == ZONE_WARM) warm_++; else prefetch_++;
            if (p.slot != FS_INDEX_NONE) { slots_[p.slot].zone = zp.zone; slots_[p.slot].lastTouched = FrameIndex(); continue; }
            if (p.hasCold && loads < kLoadsPerTick) {
                requestsThisFrame_++;
                uint32_t slot = AllocSlot(p.cold.surfels.size() > stdCap_, true);
                if (slot == FS_INDEX_NONE) { stalls_++; continue; }
                LoadIntoSlot(p, slot); slots_[slot].zone = zp.zone; loads++;
            }
        }
        if (!GpuIdle()) return;                                        // evictions need a quiescent GPU (batched jobs touch any slot)
        uint32_t evicts = 0;
        for (uint32_t i = 0; i < slots_.size() && evicts < kEvictsPerTick; ++i)
            if (slots_[i].used && slots_[i].zone == ZONE_OUTSIDE) { EvictSlot(i); evicts++; }
    }
    void UploadPending() {          // pages created before residency was told anything (synthetic): make them resident
        if (centerValid_) return;
        uint32_t n = 0;
        for (auto& kv : pages_) {
            if (n >= kUploadsPerTick) break;
            LogicalPage& p = kv.second;
            if (p.slot != FS_INDEX_NONE || !p.hasCold) continue;
            uint32_t slot = AllocSlot(p.cold.surfels.size() > stdCap_, false);
            if (slot == FS_INDEX_NONE) continue;
            LoadIntoSlot(p, slot); n++;
        }
    }

    // ---- scheduler tick (fs-sched thread) -----------------------------------------------------------
    void Tick(uint32_t budgetUs) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (!deviceUp_) return;
        EnsureBound();
        if (!Ready()) return;
        FoldGpuCounters();
        UploadPending();
        ApplyResidency();
        MigrateFullPages();
        FeedSynthetic();
        uint32_t frame = FrameIndex();
        if (maintenanceWanted_ && GpuIdle() && frame >= lastMaintFrame_ + FS_PUBLISH_MIN_FRAME_GAP) SubmitMaintenance();
        for (; eraseQueue_.size() && GpuIdle();) { EraseReq r = eraseQueue_.front(); eraseQueue_.erase(eraseQueue_.begin()); if (!SubmitErase(r.c, r.r, r.anchor)) break; }
        uint32_t obs;
        if (TakeScanRequest(obs)) { scanPending_ = true; scanObs_ = obs; }
        if (meas::MeasGpu_ReadyFrames() > 0) scanPending_ = true;              // C09 requests ticks on retirement; ready frames are pending work
        bool gpuFree = maintenanceInFlight_ == 0 && eraseInFlight_ == 0 && scanInFlight_ < 2 && budgetUs > 0;
        if (liveBound_ && liveLease_ == 0 && gpuFree && meas::MeasGpu_PeekFrame(liveFrame_)) {
            liveLease_ = liveFrame_.sequence;
            if (!SubmitLiveScan()) { meas::MeasGpu_ReleaseFrame(liveLease_); liveLease_ = 0; }
        }
        if (scanPending_ && ring_.Pending() > 0 && gpuFree && scanInFlight_ < 2) SubmitScan();
        else if (scanPending_ && ring_.Pending() == 0 && meas::MeasGpu_ReadyFrames() == 0) scanPending_ = false;
    }
    void FeedSynthetic() {
        if (feedCursor_ >= feed_.size()) { if (!feed_.empty()) { feed_.clear(); feed_.shrink_to_fit(); feedCursor_ = 0; } return; }
        uint32_t room = (uint32_t)(ringCap_ - std::min<uint64_t>(ring_.Pending(), ringCap_));
        uint32_t n = (uint32_t)std::min<size_t>(std::min<uint32_t>(kFeedPerTick, room), feed_.size() - feedCursor_);
        if (!n) return;
        ring_.Push(feed_.data() + feedCursor_, (int32_t)n); CounterAdd(FS_CTR_MEASUREMENTS, n);
        feedCursor_ += n;
        FsScan_RequestTick(++feedObs_);
    }

    // ---- exports ------------------------------------------------------------------------------------
    int32_t CreateSynthetic(int32_t kind, float extentM, float spacing, uint32_t seed) {
        SyntheticScene scene;
        GenerateSynthetic(kind, extentM, spacing, seed, scanAnchor_, scene);
        std::lock_guard<std::recursive_mutex> g(m_);
        if (kind & kSyntheticViaRingFlag) {
            feed_.clear(); SamplesToMeasurements(scene.samples, 0, scene.samples.size(), ++feedObs_, feed_); feedCursor_ = 0;
            Log("FS-WORLD synthetic kind %d via ring: %zu measurements", kind, feed_.size());
            return kResultOk;
        }
        for (SyntheticPage& sp : scene.pages) {
            LogicalPage& p = PageFor(sp.key);
            if (p.slot != FS_INDEX_NONE) { if (!GpuIdle()) return kResultBusy; EvictSlot(p.slot); }
            p.cold = ColdPage{}; p.hasCold = true;
            MortonSort(sp.surfels);
            if (sp.surfels.size() > bigCap_) { Log("FS-WORLD synthetic page truncated %zu -> %u", sp.surfels.size(), bigCap_); sp.surfels.resize(bigCap_); }
            p.cold.surfels = std::move(sp.surfels);
            p.cold.evidence.assign(p.cold.surfels.size(), FsSurfelEvidence{1, 0, 0, 0});
            uint32_t n = (uint32_t)p.cold.surfels.size();
            uint32_t cap = NodesPerSlotFor(n > stdCap_ ? bigCap_ : stdCap_);
            bool overflow = false;
            ClusterLayout L = BuildClusterTree(p.cold.surfels.data(), n, 0, cap, p.cold.nodes, &overflow);
            if (overflow) CounterAdd(FS_CTR_INDEX_OVERFLOW, 1);
            p.cold.errors.assign(p.cold.nodes.size(), FsClusterError{});
            for (uint32_t lv = 0; lv < L.levels; ++lv)
                for (uint32_t j = 0; j < L.count[lv]; ++j) {
                    uint32_t ni = L.offset[lv] + j; p.cold.errors[ni].ownError = p.cold.nodes[ni].repRadius;
                    p.cold.errors[ni].parentError = (lv + 1 < L.levels) ? p.cold.nodes[L.offset[lv + 1] + j / FS_CLUSTER_FANOUT].repRadius : FS_CLUSTER_ERROR_INF;
                }
            p.cold.leafShift = ChooseLeafShift(n, cap); p.cold.sortedFront = true;
        }
        Log("FS-WORLD synthetic kind %d: %zu pages, %llu surfels, %u planes", kind, scene.pages.size(), (unsigned long long)scene.sampleCount, scene.planeCount);
        return kResultOk;
    }
    void GetPageCount(int32_t* resident, int32_t* logical) {
        std::lock_guard<std::recursive_mutex> g(m_);
        int32_t r = 0; for (const SlotState& s : slots_) if (s.used) r++;
        if (resident) *resident = r;
        if (logical) *logical = (int32_t)pages_.size();
    }
    void GetSurfelCount(int64_t* front, int64_t* back) {   // host-visible mirror reads (telemetry)
        std::lock_guard<std::recursive_mutex> g(m_);
        int64_t f = 0, b = 0;
        if (tableGpu_) for (uint32_t i = 0; i < slots_.size(); ++i) if (slots_[i].used) {
            const FsSlotLayout& l = table_->layouts[i];
            f += std::min(AtomicLoadU32(&tableGpu_->headers[i].frontCount), l.capacity); b += std::min(AtomicLoadU32(&tableGpu_->headers[i].backCount), l.capacity);
        }
        for (auto& kv : pages_) if (kv.second.slot == FS_INDEX_NONE && kv.second.hasCold) b += (int64_t)kv.second.cold.surfels.size();
        if (front) *front = f;
        if (back) *back = b;
    }
    uint32_t FrontGeneration() {
        std::lock_guard<std::recursive_mutex> g(m_);
        uint32_t x = 0; if (tableGpu_) for (uint32_t i = 0; i < slots_.size(); ++i) if (slots_[i].used) x ^= AtomicLoadU32(&tableGpu_->headers[i].generation) * (i + 1);
        return x;
    }
    int32_t SetAnchor(int32_t id, const float m[16]) {
        if (id < 0 || id >= FS_MAX_ANCHORS || !m) return kResultInvalid;
        std::lock_guard<std::recursive_mutex> g(m_); memcpy(anchors_[id], m, 64); anchorSeq_++; return kResultOk;
    }
    uint64_t CopyAnchors(float* out) { std::lock_guard<std::recursive_mutex> g(m_); memcpy(out, anchors_, sizeof anchors_); return anchorSeq_; }
    // Receipt (§20): where the published FRONT surfels are (host-visible mirror, after the fence).
    void LogFrontReceipt() {
        if (!tableGpu_) return;
        uint32_t pagesWithFront = 0, sampled = 0; int64_t frontTotal = 0, backTotal = 0;
        float bmin[3] = {1e30f, 1e30f, 1e30f}, bmax[3] = {-1e30f, -1e30f, -1e30f}; double rsum = 0.0; uint32_t transient = 0, removed = 0;
        for (uint32_t i = 0; i < slots_.size(); ++i) {
            if (!slots_[i].used) continue;
            const RootWord r = UnpackRoot(AtomicLoadU32(&tableGpu_->roots[i]));
            backTotal += std::min(AtomicLoadU32(&tableGpu_->headers[i].backCount), table_->layouts[i].capacity);
            if (!r.published || r.frontCount == 0) continue;
            pagesWithFront++; frontTotal += r.frontCount;
            const FsSlotLayout& l = table_->layouts[i];
            const FsPageKey& k = slots_[i].key;
            const uint32_t base = FrontOffsetOf(l, r.parity), n = std::min(r.frontCount, l.capacity), step = n > 128 ? n / 128 : 1;
            for (uint32_t j = 0; j < n; j += step) {
                const FsSurfel& s = surfels_[base + j];
                float w[3] = {DecodePos(s.px) + PageOrigin(k.x), DecodePos(s.py) + PageOrigin(k.y), DecodePos(s.pz) + PageOrigin(k.z)};
                for (int a = 0; a < 3; ++a) { bmin[a] = std::min(bmin[a], w[a]); bmax[a] = std::max(bmax[a], w[a]); }
                rsum += DecodeLogRadius(s.radiusMajor); ++sampled;
                if (s.evidenceFlags & kFlagTransient) ++transient; if (s.evidenceFlags & kFlagRemoved) ++removed;
            }
        }
        Log("FS-WORLD receipt: maint #%llu pagesFront=%u front=%lld back=%lld sampled=%u bbox=(%.2f %.2f %.2f)-(%.2f %.2f %.2f) radiusMean=%.4f m transient=%u removed=%u migrations=%llu stalls=%llu",
            (unsigned long long)maintenanceJobs_, pagesWithFront, (long long)frontTotal, (long long)backTotal, sampled, bmin[0], bmin[1], bmin[2], bmax[0], bmax[1], bmax[2],
            sampled ? rsum / sampled : 0.0, transient, removed, (unsigned long long)migrations_, (unsigned long long)migrationStalls_);
    }
    int32_t Erase(const float c[3], float radius, int32_t anchorId) {
        std::lock_guard<std::recursive_mutex> g(m_);
        if (!deviceUp_) return kResultUnavailable;
        if (eraseQueue_.size() >= 16) return kResultBusy;
        EraseReq r; memcpy(r.c, c, sizeof r.c); r.r = radius; r.anchor = anchorId; eraseQueue_.push_back(r);
        return kResultOk;
    }
    int32_t SetCenter(const float p[3], float inner, float warm, float prefetch) {
        std::lock_guard<std::recursive_mutex> g(m_);
        memcpy(center_, p, sizeof center_); radii_[0] = inner; radii_[1] = warm; radii_[2] = prefetch; centerValid_ = true; centerSeq_++;
        return kResultOk;
    }
    void ZoneStats(int64_t out[8]) {
        std::lock_guard<std::recursive_mutex> g(m_);
        out[0] = inner_; out[1] = warm_; out[2] = prefetch_; out[3] = requestsThisFrame_;
        out[4] = 0;                                    // orientation residency requests: impossible by construction (§12.4)
        out[5] = evictions_; out[6] = loads_; out[7] = stalls_;
    }
    int32_t Push(const FsSurfaceMeasurement* items, int32_t count, int32_t anchorId) {
        if (!items || count <= 0) return kResultInvalid;
        scanAnchor_ = anchorId;                        // one anchor per stream in this cut (README)
        int32_t n = ring_.Push(items, count); CounterAdd(FS_CTR_MEASUREMENTS, n); return n;
    }
    const WorldBuffers* BuffersIf() const { return deviceUp_ ? &buf_ : nullptr; }
    bool IsDeviceUp() const { return deviceUp_; }
    uint32_t Slots() const { return (uint32_t)slots_.size(); }

private:
    struct EraseReq { float c[3]; float r; int32_t anchor; };
    std::recursive_mutex m_;
    bool inited_ = false, configured_ = false, deviceUp_ = false, pipesReady_ = false, bound_ = false;
    uint32_t stdSlots_ = 0, stdCap_ = 0, bigSlots_ = 0, bigCap_ = 0, hashCap_ = 0, ringCap_ = 0, maxCapGroups_ = 1;
    PageHash hash_; bool hashDirty_ = false; FsPageHashEntry* hashMirror_ = nullptr;
    MeasRing ring_;
    std::vector<SlotState> slots_;
    std::unordered_map<FsPageKey, LogicalPage, KeyHasher, KeyEqual> pages_;
    std::unique_ptr<SlotTable> table_; SlotTable* tableGpu_ = nullptr;
    FsSurfel* surfels_ = nullptr; FsSurfelEvidence* evidence_ = nullptr; FsClusterNode* nodes_ = nullptr; FsClusterError* errors_ = nullptr;
    uint32_t* gctr_ = nullptr; uint32_t gctrLast_[FS_GCTR_COUNT] = {};
    WorldBuffers buf_; Pipeline pipes_[K_COUNT];
    static constexpr uint32_t kLiveSlots = FS_MEAS_GPU_RING_SLOTS;
    Pipeline liveIntegrate_[kLiveSlots], liveArgs_[kLiveSlots]; bool liveBound_ = false;
    meas::MeasGpuFrame liveFrame_; uint64_t liveLease_ = 0;
    float anchors_[FS_MAX_ANCHORS][16]; uint64_t anchorSeq_ = 1;
    bool centerValid_ = false; float center_[3] = {0, 0, 0}; float radii_[3] = {3, 6, 10}; uint64_t centerSeq_ = 0, appliedSeq_ = 0;
    std::vector<ZonePage> zonePages_;
    int64_t inner_ = 0, warm_ = 0, prefetch_ = 0, requestsThisFrame_ = 0, evictions_ = 0, loads_ = 0, stalls_ = 0;
    uint32_t scanInFlight_ = 0, maintenanceInFlight_ = 0, eraseInFlight_ = 0, lastMaintFrame_ = 0;
    uint64_t maintenanceJobs_ = 0, migrations_ = 0, migrationStalls_ = 0;
    bool maintenanceWanted_ = false, scanPending_ = false; uint32_t scanObs_ = 0, tick_ = 0, lastScanTick_ = 0, evidenceTickDone_ = 0; int32_t scanAnchor_ = 0;
    std::vector<EraseReq> eraseQueue_;
    std::vector<FsSurfaceMeasurement> feed_; size_t feedCursor_ = 0; uint32_t feedObs_ = 0;
};

World& W() { static World w; return w; }
std::once_flag g_once;
void EnsureInitImpl() { std::call_once(g_once, []() { W().Init(); }); }
// Register with the executor at library load: warm-up creates the world pipelines before FS_HOST_READY.
struct AutoInit { AutoInit() { EnsureInitImpl(); } } g_autoInit;

} // namespace

void EnsureInit() { EnsureInitImpl(); }
bool DeviceUp() { return W().IsDeviceUp(); }
const WorldBuffers* Buffers() { return W().BuffersIf(); }
uint32_t SlotCount() { return W().Slots(); }
uint64_t CopyAnchors(float* out) { return W().CopyAnchors(out); }
uint32_t RenderModeHint() { return 0; }

} // namespace world
} // namespace fs

using fs::world::W; using fs::world::EnsureInit;

FS_API int32_t FsWorld_CreateSynthetic(int32_t kind, float extentM, float surfelSpacingM, uint32_t seed) { EnsureInit(); return W().CreateSynthetic(kind, extentM, surfelSpacingM, seed); }
FS_API int32_t FsWorld_GetPageCount(int32_t* resident, int32_t* logical) { EnsureInit(); W().GetPageCount(resident, logical); return 0; }
FS_API int32_t FsWorld_GetSurfelCount(int64_t* frontTotal, int64_t* backTotal) { EnsureInit(); W().GetSurfelCount(frontTotal, backTotal); return 0; }
FS_API uint32_t FsWorld_GetFrontGeneration(void) { EnsureInit(); return W().FrontGeneration(); }
FS_API int32_t FsWorld_SetAnchorTransform(int32_t anchorId, const float worldFromAnchor[16]) { EnsureInit(); return W().SetAnchor(anchorId, worldFromAnchor); }
FS_API int32_t FsWorld_Erase(const float center[3], float radius, int32_t anchorId) { EnsureInit(); if (!center) return 2; return W().Erase(center, radius, anchorId); }
FS_API int32_t FsResidency_SetCenter(const float predictedPos[3], float innerRadius, float warmRadius, float prefetchRadius) { EnsureInit(); if (!predictedPos) return 2; return W().SetCenter(predictedPos, innerRadius, warmRadius, prefetchRadius); }
FS_API int32_t FsResidency_GetZoneStats(int64_t out[8]) { EnsureInit(); if (!out) return 2; W().ZoneStats(out); return 0; }
FS_API int32_t FsMeas_Push(const FsSurfaceMeasurement* items, int32_t count, int32_t anchorId) { EnsureInit(); return W().Push(items, count, anchorId); }
