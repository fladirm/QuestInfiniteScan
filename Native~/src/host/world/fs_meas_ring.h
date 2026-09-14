// Lock-free measurement ring (contract §7, host API FsMeas_Push). Multi-producer: producers reserve a
// range with fetch_add, copy, then commit in order (bounded spin on the previous producer). Single
// consumer (fs-sched tick) takes bounded slices; overflow drops the oldest and is counted. Storage is
// the host-visible fs::Buffer the integrate kernel reads (slice indices are passed by push constant),
// with a CPU staging vector used until the GPU buffer exists.
#pragma once
#include <stdint.h>
#include <atomic>
#include <string.h>
#include <vector>
#include "../../../include/finalscan_world_abi.h"

namespace fs {
namespace world {

class MeasRing {
public:
    void Init(uint32_t capacityPow2) { capacity_ = capacityPow2; mask_ = capacityPow2 - 1; staging_.reserve(4096); }
    uint32_t Capacity() const { return capacity_; }
    // Points the ring at mapped host-visible memory (called once, before the first GPU slice is taken).
    void Attach(FsSurfaceMeasurement* mapped) {
        FsSurfaceMeasurement* expected = nullptr;
        if (!mapped_.compare_exchange_strong(expected, mapped)) return;
        // Drain staging pushed before attach (staging is only appended under stagingLock_).
        for (const FsSurfaceMeasurement& m : staging_) PushOne(m);
        staging_.clear(); staging_.shrink_to_fit();
    }
    bool Attached() const { return mapped_.load(std::memory_order_acquire) != nullptr; }

    // Lock-free when attached. Returns the number accepted (always count; overflow is resolved at take time).
    int32_t Push(const FsSurfaceMeasurement* items, int32_t count) {
        if (count <= 0) return 0;
        FsSurfaceMeasurement* base = mapped_.load(std::memory_order_acquire);
        if (!base) {
            // Pre-attach fallback (bounded, not lock-free; only before the executor is ready).
            while (stagingLock_.test_and_set(std::memory_order_acquire)) {}
            size_t room = staging_.size() < capacity_ ? capacity_ - staging_.size() : 0;
            size_t n = (size_t)count < room ? (size_t)count : room;
            staging_.insert(staging_.end(), items, items + n);
            droppedPreAttach_ += (uint64_t)((size_t)count - n);
            stagingLock_.clear(std::memory_order_release);
            return count;
        }
        uint64_t start = write_.fetch_add((uint64_t)count, std::memory_order_acq_rel);
        for (int32_t k = 0; k < count; ++k) base[(start + (uint64_t)k) & mask_] = items[k];
        uint64_t expected = start;
        while (!commit_.compare_exchange_weak(expected, start + (uint64_t)count, std::memory_order_acq_rel)) expected = start;
        return count;
    }

    // Consumer (single thread): Peek reports up to maxCount committed items (and the ring base index)
    // without consuming; Advance(n) consumes them once the job that reads them was accepted. Drops the
    // oldest when the producers overran the consumer by more than capacity (counted).
    uint32_t Peek(uint32_t maxCount, uint32_t* baseIndex, uint64_t* droppedOut) {
        uint64_t c = commit_.load(std::memory_order_acquire);
        uint64_t dropped = droppedPreAttach_; droppedPreAttach_ = 0;
        if (c - read_ > capacity_) { uint64_t nr = c - capacity_; dropped += nr - read_; read_ = nr; }
        uint64_t avail = c - read_;
        uint32_t n = avail > maxCount ? maxCount : (uint32_t)avail;
        *baseIndex = (uint32_t)(read_ & mask_);
        *droppedOut = dropped;
        return n;
    }
    void Advance(uint32_t n) { read_ += n; }
    uint32_t Take(uint32_t maxCount, uint32_t* baseIndex, uint64_t* droppedOut) { uint32_t n = Peek(maxCount, baseIndex, droppedOut); Advance(n); return n; }
    uint64_t Pending() const { uint64_t c = commit_.load(std::memory_order_acquire); return c - read_; }

private:
    void PushOne(const FsSurfaceMeasurement& m) {
        FsSurfaceMeasurement* base = mapped_.load(std::memory_order_relaxed);
        uint64_t start = write_.fetch_add(1, std::memory_order_acq_rel);
        base[start & mask_] = m;
        uint64_t expected = start;
        while (!commit_.compare_exchange_weak(expected, start + 1, std::memory_order_acq_rel)) expected = start;
    }
    std::atomic<FsSurfaceMeasurement*> mapped_{nullptr};
    std::atomic<uint64_t> write_{0}, commit_{0};
    uint64_t read_ = 0;                          // consumer-owned
    uint32_t capacity_ = 0, mask_ = 0;
    std::vector<FsSurfaceMeasurement> staging_;
    std::atomic_flag stagingLock_ = ATOMIC_FLAG_INIT;
    uint64_t droppedPreAttach_ = 0;
};

} // namespace world
} // namespace fs
