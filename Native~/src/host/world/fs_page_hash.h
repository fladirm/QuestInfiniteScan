// Host-side page hash (contract §9.1): open addressing, robin-hood, generation protected, bounded probe
// count, explicit overflow. The CPU is the only mutator; the GPU only looks up (world_integrate_stub.comp,
// fsPageHashLookup in fs_world_common.glsl) through a byte-identical mirror synced at safe points (no
// SCAN job in flight, see fs_world.cpp). Erase uses backward-shift deletion so the invariant
// "entry.probeDistance >= distance-so-far, else the key is absent" holds and lookups can exit early.
#pragma once
#include <stdint.h>
#include <vector>
#include "fs_world_types.h"

namespace fs {
namespace world {

class PageHash {
public:
    static constexpr uint32_t kMaxProbe = 32;        // twin: FS_PAGE_HASH_MAX_PROBE in GLSL

    void Init(uint32_t capacityPow2) {
        capacity_ = capacityPow2; mask_ = capacityPow2 - 1; count_ = 0; overflow_ = 0;
        entries_.assign(capacityPow2, Empty());
    }
    uint32_t Capacity() const { return capacity_; }
    uint32_t Count() const { return count_; }
    uint64_t Overflow() const { return overflow_; }
    const FsPageHashEntry* Data() const { return entries_.data(); }
    size_t Bytes() const { return entries_.size() * sizeof(FsPageHashEntry); }

    bool Lookup(const FsPageKey& key, uint32_t* slot, uint32_t* generation = nullptr) const {
        uint32_t i = HashPageKey(key) & mask_;
        for (uint32_t d = 0; d < kMaxProbe; ++d, i = (i + 1) & mask_) {
            const FsPageHashEntry& e = entries_[i];
            if (e.slot == FS_INDEX_NONE) return false;
            if (e.probeDistance < d) return false;           // robin-hood early exit
            if (KeyEq(e.key, key)) { *slot = e.slot; if (generation) *generation = e.generation; return true; }
        }
        return false;
    }

    // Inserts or updates. Returns false (and counts overflow) when a displacement would exceed kMaxProbe
    // or the load factor would exceed 3/4. Never leaves the table in a torn state.
    bool Insert(const FsPageKey& key, uint32_t slot, uint32_t generation) {
        uint32_t existing;
        if (Lookup(key, &existing)) {
            uint32_t i = FindIndex(key);
            entries_[i].slot = slot; entries_[i].generation = generation; return true;
        }
        if ((count_ + 1) * 4 > capacity_ * 3) { ++overflow_; return false; }
        FsPageHashEntry cur; cur.key = key; cur.slot = slot; cur.generation = generation; cur.probeDistance = 0; cur.reserved = 0;
        uint32_t i = HashPageKey(key) & mask_;
        // Dry run first: robin-hood displacement chains are bounded by the table's max probe distance;
        // check that the chain terminates within kMaxProbe for every displaced element.
        if (!CanInsert(i)) { ++overflow_; return false; }
        for (;;) {
            FsPageHashEntry& e = entries_[i];
            if (e.slot == FS_INDEX_NONE) { e = cur; ++count_; return true; }
            if (e.probeDistance < cur.probeDistance) { FsPageHashEntry t = e; e = cur; cur = t; }
            ++cur.probeDistance; i = (i + 1) & mask_;
        }
    }

    bool Erase(const FsPageKey& key) {
        uint32_t s;
        if (!Lookup(key, &s)) return false;
        uint32_t i = FindIndex(key);
        for (;;) {                                           // backward shift
            uint32_t n = (i + 1) & mask_;
            const FsPageHashEntry& next = entries_[n];
            if (next.slot == FS_INDEX_NONE || next.probeDistance == 0) { entries_[i] = Empty(); break; }
            entries_[i] = next; entries_[i].probeDistance -= 1; i = n;
        }
        --count_;
        return true;
    }

    bool SetGeneration(const FsPageKey& key, uint32_t generation) {
        uint32_t s; if (!Lookup(key, &s)) return false;
        entries_[FindIndex(key)].generation = generation; return true;
    }

private:
    static FsPageHashEntry Empty() { FsPageHashEntry e{}; e.slot = FS_INDEX_NONE; return e; }
    uint32_t FindIndex(const FsPageKey& key) const {
        uint32_t i = HashPageKey(key) & mask_;
        for (uint32_t d = 0; d < kMaxProbe; ++d, i = (i + 1) & mask_) if (KeyEq(entries_[i].key, key) && entries_[i].slot != FS_INDEX_NONE) return i;
        return FS_INDEX_NONE;
    }
    // Simulates the displacement chain from index i with a fresh element; true when every element in the
    // chain (including displaced ones) stays within kMaxProbe.
    bool CanInsert(uint32_t i) const {
        uint32_t curDist = 0;
        for (uint32_t steps = 0; steps < capacity_; ++steps) {
            const FsPageHashEntry& e = entries_[i];
            if (e.slot == FS_INDEX_NONE) return curDist < kMaxProbe;
            if (curDist >= kMaxProbe) return false;
            if (e.probeDistance < curDist) curDist = e.probeDistance;   // displaced element continues
            ++curDist; i = (i + 1) & mask_;
        }
        return false;
    }
    std::vector<FsPageHashEntry> entries_;
    uint32_t capacity_ = 0, mask_ = 0, count_ = 0;
    uint64_t overflow_ = 0;
};

} // namespace world
} // namespace fs
