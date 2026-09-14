// FinalScan world ABI: packed GPU record layouts shared by C++ (native), GLSL (Native~/shaders) and C#
// (Runtime/World/WorldAbi.cs). Tools~/native/gen_abi.py verifies the three copies agree (sizes/offsets).
// Contract §8.1 (candidate 32 B surfel), §9 (two-level addressing), §11 (publication), §13 (draw).
#pragma once
#include <stdint.h>

#define FS_WORLD_ABI_VERSION 1

// ---- Surfel (32 B, contract §8.1) --------------------------------------------------------------
// Positions are page-local fixed point: units of 0.25 mm, int16 → ±8.19 m around the page origin.
#define FS_SURFEL_POS_UNIT_M 0.00025f
#define FS_PAGE_EXTENT_M     4.0f      // page cube edge (provisional); page origin = cube center
#define FS_CELL_EXTENT_M     0.125f    // page-local cell edge (provisional): 32^3 cells per page
#define FS_CELLS_PER_AXIS    32
#define FS_CELLS_PER_PAGE    (FS_CELLS_PER_AXIS * FS_CELLS_PER_AXIS * FS_CELLS_PER_AXIS)

typedef struct FsSurfel {
    int16_t  px, py, pz;        // page-local center, q0.25 mm
    uint16_t normalOct;         // oct-encoded normal, 8+8 bits
    uint16_t normalOctHi;       // extra precision: 8+8 low bits of a 16+16 oct encoding
    uint16_t tangentAngle;      // rotation of the major tangent axis about the normal, [0,2π) → u16
    uint16_t radiusMajor;       // log-encoded metres: r = 0.5mm * 2^(v/4096)   (0.5 mm .. ~32 m)
    uint16_t radiusMinor;
    uint16_t sigmaN;            // log-encoded std dev along normal (same encoding, 0.1 mm base)
    uint16_t sigmaTMajor;
    uint16_t sigmaTMinor;
    uint16_t evidenceFlags;     // bits 0..9 support count (sat), 10 sidedA, 11 sidedB, 12 transient,
                                // 13 promoted, 14 removed(ERASE), 15 detail
    uint32_t surfaceId;         // stable SurfaceID (never a physical slot)
    uint32_t appearanceHandle;  // atlas tile handle or 0
} FsSurfel;                     // 32 B

// ---- Page (contract §9.1, §11) -----------------------------------------------------------------
typedef struct FsPageKey { int32_t anchorId; int32_t x, y, z; } FsPageKey;   // logical page coordinate

typedef struct FsPageHeader {
    FsPageKey key;
    uint32_t  slot;               // resident slot (physical; never persisted as identity)
    uint32_t  generation;         // bumped on every publish; 0 = empty slot
    uint32_t  frontOffset;        // surfel index of FRONT range in the surfel arena
    uint32_t  frontCount;
    uint32_t  backOffset;         // BACK range (scan writes here)
    uint32_t  backCount;
    uint32_t  capacity;           // surfels reserved per range
    uint32_t  dirty;              // 1 = BACK has unpublished changes
    uint32_t  cellIndexOffset;    // offset into the cell-index arena (page-local index, §9.2)
    uint32_t  freeSpaceOffset;    // offset into free-space arena (coarse, §8.6)
    uint32_t  lastTouchedFrame;
    uint32_t  nodeBase;           // first FsClusterNode of the published FRONT parity (derived ClusterTree, §13.4)
    uint32_t  nodeCount;          // nodes of that tree (0 = no tree yet)
} FsPageHeader;                   // 68 B

// Page-local index (default variant A: fixed bucket grid, benchmarked in C08 against B/C/D).
// One u32 head per cell → linked list through FsSurfelLink.next; 0xFFFFFFFF = end.
#define FS_INDEX_NONE 0xFFFFFFFFu
typedef struct FsSurfelLink { uint32_t next; } FsSurfelLink;   // parallel array to surfel arena

// ---- Page hash (contract §9.1): open addressing, robin-hood, generation protected ----------------
typedef struct FsPageHashEntry {
    FsPageKey key;
    uint32_t  slot;               // FS_INDEX_NONE = empty
    uint32_t  generation;
    uint32_t  probeDistance;      // robin-hood displacement
    uint32_t  reserved;
} FsPageHashEntry;                // 32 B

// ---- Measurements (contract §7) ----------------------------------------------------------------
typedef struct FsSurfaceMeasurement {
    float    px, py, pz;          // anchor-local metres
    float    nx, ny, nz;
    float    sigmaN, sigmaT;      // std dev along normal / tangent (m)
    float    footprint;           // projected pixel footprint at this depth (m)
    uint32_t sourceFlags;         // 0 stereo, 1 temporal, 2 depthPrior, 3 planarFit, 8 edge, 9 lowTexture
    uint32_t observationId;
    uint32_t reserved;
} FsSurfaceMeasurement;           // 48 B

// ---- Draw (contract §13) -----------------------------------------------------------------------
// The native CULL pass writes compact draw records for the published FRONT of resident pages.
typedef struct FsDrawRecord {
    float    cx, cy, cz;          // world metres (anchor transform applied at cull time)
    uint32_t normalOct32;
    uint32_t tangentAndRadii;     // tangentAngle u16 | radiusMajor u8 log | radiusMinor u8 log
    uint32_t colorOrHandle;       // RGBA8 preview color (C05) or appearance handle (C17)
    uint32_t surfaceId;
    uint32_t flags;               // bit 0 detail, bit 1 selected, bit 2 erased-preview
} FsDrawRecord;                   // 32 B

typedef struct FsIndirectDrawArgs {
    uint32_t vertexCountPerInstance;   // 6 (two triangles → screen-space quad per surfel)
    uint32_t instanceCount;            // visible surfels (×2 for single-pass instanced stereo)
    uint32_t startVertex;
    uint32_t startInstance;
} FsIndirectDrawArgs;

// ---- Telemetry counters (contract §20) ---------------------------------------------------------
enum FsCounter {
    FS_CTR_PCA_FRAMES_L = 0, FS_CTR_PCA_FRAMES_R, FS_CTR_STEREO_PAIRS, FS_CTR_PAIRS_REJECTED_SKEW,
    FS_CTR_MEASUREMENTS, FS_CTR_MEASUREMENTS_DROPPED, FS_CTR_PAGE_LOOKUPS, FS_CTR_PAGE_MISSES,
    FS_CTR_CELL_LOOKUPS, FS_CTR_SURFEL_CREATE, FS_CTR_SURFEL_UPDATE, FS_CTR_SURFEL_DELETE,
    FS_CTR_SURFEL_SPLIT, FS_CTR_SURFEL_MERGE, FS_CTR_PUBLISH, FS_CTR_RESIDENT_DRAWN,
    FS_CTR_VISIBLE_SURFELS, FS_CTR_SCAN_TICK, FS_CTR_SCAN_TICK_SKIPPED, FS_CTR_ORIENTATION_RESIDENCY_REQUESTS,
    FS_CTR_PAGE_HASH_OVERFLOW, FS_CTR_INDEX_OVERFLOW, FS_CTR_DEFERRED_PUBLISH, FS_CTR_DEFERRED_SCAN,
    FS_CTR_DEFERRED_RESIDENCY, FS_CTR_DEFERRED_APPEARANCE, FS_CTR_DEFERRED_COLD, FS_CTR_TIMESTAMP_NONMONO,
    FS_CTR_DUPLICATE_OBSERVATIONS,   // same surfel observed twice in one scan tick (no information, §7.6)
    FS_CTR_COUNT
};

// ---- Evidence sidecar (contract §8.6, C13): canonical, persisted, parallel to the surfel arena ------
typedef struct FsSurfelEvidence {
    uint16_t staticEvidence;      // consistent observations within sigma over time (saturating)
    uint16_t motionEvidence;      // observations outside sigma consistent with motion (saturating, decays)
    uint16_t varianceQ;           // log-encoded geometry residual variance
    uint16_t lastSeenFrame;       // low 16 bits of the scan tick index
} FsSurfelEvidence;               // 8 B

// ---- Derived ClusterTree (contract §13.4, C05b): disposable, rebuilt from FRONT after publish -----
// Per page: a node array; node 0 is the page root. Leaves reference a FRONT surfel range.
#define FS_CLUSTER_LEAF_MAX_SURFELS 64
#define FS_CLUSTER_MAX_NODES_PER_PAGE 4096
typedef struct FsClusterNode {
    float    bmin[3];             // page-local metres
    float    bmax[3];
    float    coneAxis[3];         // normal cone axis (unit)
    float    coneCos;             // cos(half angle); -1 = any direction
    float    repCenter[3];        // representative aggregate surfel (page-local m)
    float    repRadius;           // aggregate footprint radius (m)
    uint32_t repNormalOct32;
    uint32_t repColorOrHandle;    // RGBA8 preview / appearance handle
    uint16_t coverage;            // 0..65535 → fill ratio of the aggregate footprint (gaps preserved)
    uint16_t surfelCount;         // surfels under this node (saturating)
    uint32_t firstChildOrSurfel;  // child node index (internal) or FRONT surfel offset (leaf)
    uint16_t childCount;          // 0 = leaf
    uint16_t leafSurfelCount;     // valid when leaf
    uint32_t footprint;           // footprint ellipse semi-axes (log16 a | log16 b << 16) in the canonical tangent frame
} FsClusterNode;                  // 80 B

// (additive, C05b refinement, contract §13.4) Parallel per-node LOD errors: ownError = metric error of
// replacing the node's subtree by its representative (bounds half-diagonal, monotone up the tree),
// parentError = ownError of the parent (root: FS_CLUSTER_ERROR_INF). The cut is selected by ONE bounded
// dispatch over all nodes of visible pages: emit when projected(parentError) > tau >= projected(ownError);
// a leaf whose own projected error exceeds tau emits its surfels. No dependent traversal on the GPU.
#define FS_CLUSTER_ERROR_INF 1.0e30f
typedef struct FsClusterError {
    float ownError;
    float parentError;
} FsClusterError;                 // 8 B

// Draw record flags (FsDrawRecord.flags): bit 0 detail, bit 1 selected, bit 2 erased-preview,
// bit 3 aggregate (coverage LOD; low 16 bits of colorOrHandle alpha carry coverage), bit 4 transient.
#define FS_DRAW_FLAG_AGGREGATE (1u << 3)
#define FS_DRAW_FLAG_TRANSIENT (1u << 4)
