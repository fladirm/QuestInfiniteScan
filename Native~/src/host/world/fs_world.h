// World module internal interface (native only): what render/ needs from world/ (arenas to bind, slot
// count, anchor transforms) and the module init entry every export goes through. The public C ABI
// (FsWorld_*, FsResidency_*, FsMeas_*) is implemented in fs_world.cpp. Contract §8, §9, §11, §12.
#pragma once
#include <stdint.h>
#include "../fs_executor.h"
#include "fs_world_types.h"

namespace fs {
namespace world {

// Host mirror of the GLSL SlotBlock (one binding, three regions). Host-visible; the CPU writes layouts
// (once), roots (root-last publish) and headers (slot assignment / publish); SCAN kernels atomically
// bump headers[].backCount and set headers[].dirty.
struct SlotTable {
    FsSlotLayout layouts[FS_MAX_SLOTS];
    uint32_t     roots[FS_MAX_SLOTS];
    FsPageHeader headers[FS_MAX_SLOTS];
};

struct WorldBuffers {          // valid while DeviceUp(); all fs::Buffer (§15.3 persistent resources)
    Buffer slotTable;          // SlotTable, host-visible
    Buffer surfels;            // FsSurfel[totalSurfels], host-visible (UMA; CPU uploads/evictions memcpy)
    Buffer evidence;           // FsSurfelEvidence[totalSurfels], host-visible
    Buffer index;              // u32 arena (heads, counts, micro heads, links), device-local
    Buffer gctr;               // u32[16 + FS_MAX_SLOTS], host-visible (GPU counters + per-slot flags)
    Buffer nodes;              // FsClusterNode[totalNodes], host-visible (CPU tree upload for synthetic pages)
    Buffer errors;             // FsClusterError[totalNodes], host-visible
    Buffer hash;               // FsPageHashEntry[capacity] mirror, host-visible (CPU authority)
    Buffer meas;               // FsSurfaceMeasurement ring, host-visible
    Buffer scratch;            // publish scratch ring, host-visible
    Buffer freeSpace;          // u8 per cell per slot (stub), device-local
    uint32_t slotCount = 0;    // resident slots (standard + big)
    uint32_t totalSurfels = 0, totalNodes = 0;
};

void          EnsureInit();                // idempotent, thread-safe; called by every export
bool          DeviceUp();                  // arenas exist
const WorldBuffers* Buffers();             // null until DeviceUp()
uint32_t      SlotCount();
// Copies the current worldFromAnchor matrices (column-major, FS_MAX_ANCHORS x 16 floats); returns a
// change sequence (bumped by FsWorld_SetAnchorTransform).
uint64_t      CopyAnchors(float* out16xN);
uint32_t      RenderModeHint();            // reserved

} // namespace world
} // namespace fs
