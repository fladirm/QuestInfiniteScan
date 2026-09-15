// Device measurement ring (C09): the measurement front-end writes FsSurfaceMeasurement records straight
// from the GPU (measure_depth_backproject.comp) into one of FS_MEAS_GPU_RING_SLOTS slots; the world module's
// SCAN tick consumes a completed slot with its integrate kernel (no CPU readback, no FsMeas_Push).
//
// Why a separate ring: fs::world::MeasRing (world/fs_meas_ring.h) reserves and commits with CPU atomics, so a
// kernel cannot append to it. This ring is device-written (atomics on `counters`) and host-visible (UMA,
// HOST_COHERENT), so the world's CPU pre-pass (page creation from measurement positions) can read
// `records.mapped` after the job retired exactly like it reads its own ring.
//
// Slot life cycle: FREE -> IN_FLIGHT (backproject job) -> READY (retired, count known) -> LEASED (consumer
// peeked) -> FREE (consumer released). A new depth frame may overwrite a READY slot that nobody leased
// (latest-observation-only, contract §3.3; the superseded records are counted as dropped) but never a
// LEASED or IN_FLIGHT one. Consumer contract (the world module, see README.md "Integration point"):
//   MeasGpuFrame f; if (fs::meas::MeasGpu_PeekFrame(f)) { ... integrate f.count records of f.ring->records
//   (ring index 0 .. count-1, ringMask = capacity-1, in jobs of <= FS_INTEGRATE_MAX_MEAS) ...; once the last
//   integrate job retired: fs::meas::MeasGpu_ReleaseFrame(f.sequence); }
#pragma once
#include <stdint.h>
#include "../fs_executor.h"
#include "fs_meas_params.h"

namespace fs {
namespace meas {

struct MeasGpuRing {
    Buffer   records;             // FsSurfaceMeasurement[capacity], STORAGE, host-visible
    Buffer   counters;            // fs::meas::FrameBlock: uint32 ctr[FS_MEAS_CTR_WORDS] (see FS_MEAS_CTR_*) + per-eye params, STORAGE, host-visible
    uint32_t capacity = 0;        // FS_MEAS_GPU_RING_CAPACITY (power of two: ringMask = capacity - 1)
};

struct MeasGpuFrame {
    const MeasGpuRing* ring = nullptr;
    uint32_t slot = 0;            // ring slot index (stable for the life of the device: bind once per slot)
    uint32_t count = 0;           // stored records: min(reserved, maxOut) <= capacity
    uint32_t overflow = 0;        // records refused by the per-frame cap
    uint32_t observationId = 0;   // depth XrTime low 32 bits (FsScan_RequestTick was called with it)
    int32_t  anchorId = 0;        // anchor the records are local to
    uint64_t sequence = 0;        // handoff sequence (pass back to MeasGpu_ReleaseFrame)
    int64_t  xrTimeNs = 0;        // depth frame time
    uint64_t gpuStartNs = 0, gpuEndNs = 0;   // backproject job timestamps (executor clock)
    uint32_t frameIndex = 0;      // executor FrameIndex() at submit
    uint64_t importFrameEnd = 0;  // executor frame whose FRAME_END ordered the depth image barrier (JobDesc::waitFrameEndValue)
    float    eyeOrigin[2][3] = {{0, 0, 0}, {0, 0, 0}};   // anchor-local eye origins (sourceFlags bit 16 selects the eye)
    bool     eyeOriginValid = false;
    int64_t  readyMonoNs = 0;     // E6R: steady-clock ns when the frame became READY (fusion deadline age)
};

// Leases the newest READY slot (older READY slots are dropped, counted). False when nothing is ready.
bool MeasGpu_PeekFrame(MeasGpuFrame& out);
// Returns a leased slot to the producer. Unknown / already released sequences are ignored.
void MeasGpu_ReleaseFrame(uint64_t sequence);
// Ring slots (valid while the device is up; null when the arenas do not exist). For consumers that bind
// each slot's buffers to their own pipeline instances once (persistent descriptors, contract §15.3).
uint32_t           MeasGpu_RingSlots();
const MeasGpuRing* MeasGpu_Ring(uint32_t slot);
// Number of READY (unconsumed) frames right now; telemetry / tests.
uint32_t MeasGpu_ReadyFrames();
// E6R C11R2: uncertain vertices of existing surface faces, world space, handed from the topology pass to the measurement front-end;
// the next compaction job gates them (visibility, keyframe baseline, texture) and appends temporal refinement candidates.
struct RefineTarget { float pos[3]; float normal[3]; float sigmaN; uint32_t surfaceId; uint32_t support; };
void MeasGpu_SetRefineTargets(const RefineTarget* targets, uint32_t count);
// E6R: steady ns of the newest READY frame (0 = none); totals of frames made READY and superseded before fusion.
int64_t  MeasGpu_NewestReadyNs();
void     MeasGpu_FrameTotals(uint64_t& ready, uint64_t& superseded);

} // namespace meas
} // namespace fs
