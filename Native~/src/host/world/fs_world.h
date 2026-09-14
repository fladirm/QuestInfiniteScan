// World module internal interface (native only, C09R): what render/ needs from world/ (pools to bind, page
// table, anchors) and the module init entry every export goes through. The public C ABI (FsWorld_*,
// FsResidency_*, FsMeas_*) is implemented in fs_world.cpp. Contract §8, §9, §11, §12.
#pragma once
#include <stdint.h>
#include "../fs_executor.h"
#include "fs_world_types.h"

namespace fs {
namespace world {

struct WorldBuffers {          // valid while DeviceUp(); all fs::Buffer (§15.3 persistent resources)
    Buffer pages;              // FsPageDesc[FS_MAX_PAGES] + slab directory (host-visible; CPU owns slabs, GPU owns cursors)
    Buffer surfels;            // canonical FsSurfel pool (host-visible: cold copies / receipts after retirement)
    Buffer evidence;           // canonical FsSurfelEvidence pool
    Buffer index;              // adaptive index arena (host-visible: page creation clears its directory region)
    Buffer gctr;               // counters + epoch words + indirect args (host-visible)
    Buffer meas;               // epoch measurement copy
    Buffer assocA, assocB;     // association records (sort ping-pong)
    Buffer sort;               // sort / prefix scratch
    Buffer freeSpace;          // u8 stamps
    Buffer pools;              // free-id rings (host-visible)
    Buffer dirty;              // dirty masks + per-level lists
    Buffer render;             // FsRenderNode pool
    Buffer rblocks;            // render block pool (FsSurfel copies)
    Buffer rdir;               // per page render directory (host-visible: page creation fills NONE)
    Buffer retire;             // released ids (host-visible)
    Buffer pending;            // FsPendingPublish[FS_MAX_PAGES] (host-visible)
    Buffer publishRing;        // FS_CULL_FRAME_RING x FS_MAX_PAGES pending entries applied at FRAME_BEGIN
    Buffer hash;               // FsPageHashEntry mirror
    uint32_t pageCount = 0;
};

void          EnsureInit();                // idempotent, thread-safe; called by every export
bool          DeviceUp();                  // pools exist
const WorldBuffers* Buffers();             // null until DeviceUp()
uint32_t      PageCount();                 // page table entries
uint64_t      CopyAnchors(float* out16xN); // worldFromAnchor matrices (column-major, FS_MAX_ANCHORS x 16); returns a change sequence

} // namespace world
} // namespace fs
