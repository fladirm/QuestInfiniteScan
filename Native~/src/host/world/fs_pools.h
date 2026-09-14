// CPU side of the world pools (C09R-A): slab allocator of the canonical arena and the free-id rings the GPU
// pops from (index leaves/nodes, render blocks/nodes). Pure bookkeeping (no Vulkan); host tests cover it.
// Ring protocol (twin: fsPoolAlloc in fs_world_blocks.glsl): header words {head, tail, mask, failures,
// ringBase, cap}; the GPU advances head with atomics and reads ring[ringBase + (head & mask)]; the CPU
// appends free ids at tail (host-coherent write before job submission) and reads head after job retirement.
#pragma once
#include <stdint.h>
#include <vector>
#include "fs_world_types.h"

namespace fs {
namespace world {

class SlabAllocator {
public:
    void Init(uint32_t slabs) { free_.clear(); free_.reserve(slabs); for (uint32_t i = slabs; i > 0; --i) free_.push_back(i - 1); total_ = slabs; }
    bool Alloc(uint32_t& slab) { if (free_.empty()) return false; slab = free_.back(); free_.pop_back(); return true; }
    void Free(uint32_t slab) { free_.push_back(slab); }
    uint32_t Free() const { return (uint32_t)free_.size(); }
    uint32_t Total() const { return total_; }
    uint32_t Live() const { return total_ - Free(); }
private:
    std::vector<uint32_t> free_; uint32_t total_ = 0;
};

class IdRing {
public:
    // `hdr` = the pool's 8 header words in the host-visible pools buffer, `ring` = its storage (cap words).
    void Init(uint32_t cap, uint32_t* hdr, uint32_t* ring, uint32_t ringBaseWord) {
        cap_ = cap; mask_ = cap - 1; hdr_ = hdr; ring_ = ring; headSeen_ = 0; tail_ = 0;
        free_.clear(); free_.reserve(cap); for (uint32_t i = cap; i > 0; --i) free_.push_back(i - 1);
        if (hdr_) { hdr_[0] = 0; hdr_[1] = 0; hdr_[2] = mask_; hdr_[3] = 0; hdr_[4] = ringBaseWord; hdr_[5] = cap; hdr_[6] = 0; hdr_[7] = 0; }
        Refill();
    }
    // Appends free ids until the ring is full (call between jobs, before submission).
    uint32_t Refill() {
        uint32_t added = 0;
        while (tail_ - headSeen_ < cap_ && !free_.empty()) { ring_[tail_ & mask_] = free_.back(); free_.pop_back(); ++tail_; ++added; }
        if (hdr_) hdr_[1] = (uint32_t)tail_;
        return added;
    }
    // After a job retired: ids in [headSeen, head) were consumed by the GPU.
    void SyncHead() { if (hdr_) { uint32_t h = hdr_[0]; headSeen_ = h; } }
    void Free(uint32_t id) { free_.push_back(id); }
    uint32_t Failures() const { return hdr_ ? hdr_[3] : 0; }
    uint32_t Capacity() const { return cap_; }
    uint32_t Available() const { return (uint32_t)free_.size() + (uint32_t)(tail_ - headSeen_); }   // free stack + ring
    uint32_t Live() const { return cap_ - Available(); }
private:
    uint32_t cap_ = 0, mask_ = 0; uint32_t* hdr_ = nullptr; uint32_t* ring_ = nullptr;
    uint64_t headSeen_ = 0, tail_ = 0;
    std::vector<uint32_t> free_;
};

inline uint32_t Pow2Ceil(uint32_t v) { uint32_t p = 1; while (p < v && p < (1u << 30)) p <<= 1; return p; }

} // namespace world
} // namespace fs
