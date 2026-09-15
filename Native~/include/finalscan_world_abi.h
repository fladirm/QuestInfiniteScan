// FinalScan world ABI: packed GPU record layouts shared by C++ (native), GLSL (Native~/shaders) and C#
// (Runtime/World/WorldAbi.cs). Tools~/native/gen_abi.py verifies the three copies agree (sizes/offsets).
// Contract §8.1 (candidate 32 B surfel), §9 (two-level addressing), §11 (publication), §13 (draw).
#pragma once
#include <stdint.h>

#define FS_WORLD_ABI_VERSION 2

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

#define FS_INDEX_NONE 0xFFFFFFFFu

typedef struct FsPageKey { int32_t anchorId; int32_t x, y, z; } FsPageKey;   // logical page coordinate (§9.1)

// ---- Segmented canonical pools (C09R, contract §1.0, §9, §11) --------------------------------------
// Canonical surfels exist once, in one global arena addressed by a 32-bit surfel handle
// (handle = slab * FS_SLAB_SURFELS + i). A logical page owns an ordered list of slabs (its slab directory);
// page-local index k*FS_SLAB_SURFELS+i maps to the handle through that directory. Growth = one more slab.
#define FS_SLAB_SURFELS     256      // surfels per slab (8 KiB of FsSurfel, 2 KiB of evidence)
#define FS_PAGE_MAX_SLABS   4096     // 1,048,576 surfels per page ceiling (pool-bounded, not slot-bounded)
#define FS_PAGE_FLAG_DIRTY  1u       // canonical changes not yet published
#define FS_PAGE_FLAG_ERASE  2u       // an erase touched the page this epoch

typedef struct FsPageDesc {          // resident page descriptor = page table entry (twin of the GLSL block)
    FsPageKey key;
    uint32_t  generation;            // slot generation (lease protection, §9.3); 0 = empty slot
    uint32_t  slabCount;             // valid entries of the slab directory
    uint32_t  surfelCount;           // allocation cursor: page-local indices < surfelCount are allocated
    uint32_t  shortfall;             // new candidates refused this epoch because no slab was free (CPU tops up)
    uint32_t  cellDirBase;           // u32 index of the page's 32^3 cell directory in the index arena
    uint32_t  freeSpaceBase;         // u8 offset of the page's free-space stamps
    uint32_t  renderRoot;            // published render root node (written only on the graphics stream) or FS_INDEX_NONE
    uint32_t  renderCount;           // surfels represented by the published root (telemetry)
    uint32_t  pendingRoot;           // scanner-built root awaiting graphics publication or FS_INDEX_NONE
    uint32_t  pendingCount;
    uint32_t  flags;                 // FS_PAGE_FLAG_*
    uint32_t  lastTouchedTick;
} FsPageDesc;                        // 64 B

// ---- Adaptive page-local index (contract §9.2): 32^3 direct cell directory, leaf buckets, 2x2x2 nodes ---
// Directory / node child entry: 0 = empty, bit 31 = leaf id (bits 0..30), bit 30 = node id (bits 0..29).
#define FS_INDEX_ENTRY_LEAF  0x80000000u
#define FS_INDEX_ENTRY_NODE  0x40000000u
#define FS_INDEX_ENTRY_ID    0x3FFFFFFFu
#define FS_INDEX_LEAF_CAP    14      // handles per leaf bucket
#define FS_INDEX_MAX_DEPTH   3       // 12.5 cm cell -> 6.25 -> 3.125 -> 1.5625 cm micro cells
#define FS_INDEX_CHAIN_MAX   4       // at max depth a leaf may chain (56 surfels in a 1.56 cm cell), never beyond

typedef struct FsIndexLeaf {
    uint32_t count;
    uint32_t next;                   // chained leaf (only at max depth) or FS_INDEX_NONE
    uint32_t handles[FS_INDEX_LEAF_CAP];
} FsIndexLeaf;                       // 64 B

typedef struct FsIndexNode { uint32_t child[8]; } FsIndexNode;   // 32 B, octant order x | y<<1 | z<<2

// ---- Staged fusion (C09R, contract §8.5): association records sorted by key, reduced per segment ------
// key: matched = surfel handle; unmatched = FS_ASSOC_KEY_UNMATCHED | pageSlot << 15 | cell (owner cell).
#define FS_ASSOC_KEY_UNMATCHED 0x80000000u
#define FS_ASSOC_KEY_NONE      0xFFFFFFFFu   // measurement rejected (no page, out of range)
typedef struct FsAssociation {
    uint32_t key;
    uint32_t meas;                   // measurement index in the tick's ring slot
    float    score;                  // Mahalanobis score of the match (matched) / 0
    uint32_t pageSlot;               // owner page slot (matched and unmatched)
} FsAssociation;                     // 16 B

// ---- Publication (C09R, contract §11): the scanner builds complete immutable render generations; the
// graphics stream applies the root descriptors at FRAME_BEGIN after the scanner timeline was acquired -----
typedef struct FsPendingPublish {
    uint32_t pageSlot;
    uint32_t root;                   // new render root node or FS_INDEX_NONE (page becomes empty / evicted)
    uint32_t count;                  // surfels represented
    uint32_t generation;             // page generation the root belongs to
} FsPendingPublish;                  // 16 B

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
    uint32_t reserved;            // appearance sample (C16a): FS_MEAS_COLOR_VALID | RGB8 (R low byte) or 0
} FsSurfaceMeasurement;           // 48 B
#define FS_MEAS_COLOR_VALID    0x80000000u   // FsSurfaceMeasurement.reserved carries a measured colour
#define FS_APPEARANCE_MEASURED 0x80000000u   // FsSurfel.appearanceHandle: low 24 bits are a measured colour (else preview from the normal)
// E4.2R appearance state: bits 24..27 = distinct coloured observations (saturating 15). NONE = not measured,
// PROVISIONAL = measured by fewer than FS_APPEARANCE_CONFIRM_OBS observations, CONFIRMED = at least that many.
#define FS_APPEARANCE_OBS_SHIFT   24
#define FS_APPEARANCE_OBS_MASK    0x0F000000u
#define FS_APPEARANCE_CONFIRM_OBS 3
// E6R shared surface mesh in the RENDER COPY (publish_leaves): sigmaTMinor == FS_RENDER_TRI_MARK marks a triangle of the persistent
// topology owned by that vertex: position = the owner vertex, radiusMajor_radiusMinor / sigmaN_sigmaTMajor = offsets of the other
// two vertices (3 x 10-bit signed, FS_TRI_OFFSET_RANGE_M), normal = face normal. Derived readout only: the canonical surfels stay.
#define FS_RENDER_TRI_MARK        0xFFFEu

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
// E6R hysteretic evidence: building is quicker than destruction. A positive observation raises support and HEALS contradiction debt;
// free-space contradictions (narrow phase against the surfel support) accumulate weighted debt once per observation; the lifecycle
// ACTIVE -> SUSPECT -> RETIRING -> REMOVED needs debt, view diversity, persistence and a spatially consistent neighbourhood.
typedef struct FsSurfelEvidence {
    uint16_t positiveSupport;     // DISTINCT consistent observations (one depth / camera frame = at most one), saturating
    uint16_t contradictionDebt;   // weighted contradiction debt in 1/8 units (FS_DEBT_*), healed by positive observations
    uint16_t varianceQ;           // log-encoded residual variance normalised by the measurement sigma^2 (FS_VAR_NORM_BASE)
    uint16_t lastSeenFrame;       // low 16 bits of the scan tick index of the last positive update
    uint32_t lastObservationId;   // observationId of the last positive update (0 = never): one observation updates once
    uint32_t lastContradictionObs;// observationId of the latest contradicting ray (free-space narrow phase, idempotent write)
    uint32_t contradictionHits;   // atomic accumulator of weighted contradicting rays since the last maintenance (1/8 units)
    uint32_t lastDebtObs;         // observationId whose contradictions were last charged to the debt (one charge per observation)
    uint16_t viewDiversity;       // 16 view sectors (8 azimuth x 2 elevation) with positive observations
    uint16_t contradictionViews;  // view sectors of contradicting rays
    uint16_t lifecycle;           // bits 0..1 state (FS_LIFE_*), bits 8..15 residual streak (persistent unexplained residual)
    uint16_t suspectSince;        // low 16 bits of the tick the surfel became SUSPECT
} FsSurfelEvidence;               // 32 B

// ---- Derived render tree (contract §13.4, C09R): persistent COW octree of immutable render leaf blocks.
// Nodes and blocks live in global pools; unchanged subtrees are shared between generations. A leaf node
// references one render block of <= FS_RENDER_BLOCK_SURFELS surfel copies.
#define FS_RENDER_BLOCK_SURFELS 64
#define FS_RENDER_CHILDREN 8
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
    uint32_t firstChildOrSurfel;  // internal: index of its 8-entry child table in the render child pool; leaf: render block id
    uint16_t childCount;          // 0 = leaf
    uint16_t leafSurfelCount;     // valid when leaf (surfels in the block)
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

// (C09R) Render tree node record: cluster node + LOD errors + child table in one pool entry so a COW rebuild
// allocates one id per node. Internal nodes: child[k] = node id of octant k or FS_INDEX_NONE; leaf nodes
// (node.childCount == 0): node.firstChildOrSurfel = render block id, child[] unused.
typedef struct FsRenderNode {
    FsClusterNode  node;
    FsClusterError err;
    uint32_t       child[8];
} FsRenderNode;                   // 120 B

// Draw record flags (FsDrawRecord.flags): bit 0 detail, bit 1 selected, bit 2 erased-preview,
// bit 3 aggregate (coverage LOD; low 16 bits of colorOrHandle alpha carry coverage), bit 4 transient.
#define FS_DRAW_FLAG_AGGREGATE (1u << 3)
#define FS_DRAW_FLAG_TRANSIENT (1u << 4)
#define FS_DRAW_FLAG_TRIANGLE (1u << 6)        // E6R mesh triangle: centre = vertex 0 (world), tangentAndRadii / surfaceId = world offsets of vertices 1 / 2
                                               // (3 x 10-bit signed, FS_TRI_OFFSET_RANGE_M); drawn by the triangle bucket (3 vertices)
#define FS_DRAW_FLAG_NO_APPEARANCE (1u << 5)   // geometry without a measured appearance: never in SCAN; XRAY / PLAN draw it neutral grey (E4.2)
