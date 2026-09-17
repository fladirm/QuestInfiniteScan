#ifndef MERKABA_WORLD_INCLUDED
#define MERKABA_WORLD_INCLUDED

#include "MerkabaSpatial.hlsl"

#define MERKABA_M8_BLOCK_CAPACITY 8192u
#define MERKABA_M8_CHUNK_CAPACITY 262144u
#define MERKABA_M8_PHYSICAL_TILE_CAPACITY 32768u
#define MERKABA_M8_TILE_BANK_CAPACITY 8192u
#define MERKABA_M8_TILE_BANK_SHIFT 13u
#define MERKABA_M8_TILE_BANK_MASK 8191u
#define MERKABA_M8_TILE_WORDS 16u
// Indexed publication pages. One page carries 64 shared vertices and 256
// indices of one tile; a tile record stores the head of its page chain.
#define M8_RENDER_PAGE_VERTICES 64u
#define M8_RENDER_PAGE_VERTEX_SHIFT 6u
#define M8_RENDER_PAGE_INDICES 256u
#define M8_RENDER_PAGE_INDEX_SHIFT 8u
#define M8_RENDER_PAGE_CAPACITY 65536u
#define M8_RENDER_PAGE_ID_MASK 0xffffu
#define M8_RENDER_VISIBLE_USED_SHIFT 16u
#define M8_RENDER_VISIBLE_USED_MASK 0xffu
#define M8_RENDER_PAGE_INVALID 0xffffffffu
#define M8_RENDER_TILE_MAX_PAGES 2560u
#define M8_RENDER_RECORD_WORDS 8u
#define M8_RENDER_RECORD_FIRST_PAGE 6u
// Publication version of the tile this record was built from.
#define M8_RENDER_RECORD_VERSION 7u
// Header: 0 tiles, 1 indices, 2 identity of this slot, 3 active list region,
// 4 tile count of the previous list of this slot.
#define M8_RENDER_HEADER_LIST_REGION 3u
#define M8_RENDER_HEADER_PREVIOUS_TILES 4u
#define M8_RENDER_INDEX_HEADER 8u
#define M8_RENDER_TILE_LIST_BASE \
    (M8_RENDER_INDEX_HEADER + MERKABA_M8_PHYSICAL_TILE_CAPACITY * \
    M8_RENDER_RECORD_WORDS)
// Two tile-list regions per slot alternate between builds of that slot.
#define M8_RENDER_TILE_LIST_REGION_WORDS MERKABA_M8_PHYSICAL_TILE_CAPACITY
// Page allocator state lives at the head of its own queue buffer.
#define M8_RENDER_CONTROL_FREE_PAGES 0u
#define M8_RENDER_CONTROL_RETIRE_COUNT 1u
#define M8_RENDER_CONTROL_ALLOC_COUNT 2u
#define M8_RENDER_CONTROL_RECLAIM_COUNT 3u
// Retire is a ring owned by one publication slot. A slot is never drawn while
// it is BACK, so everything retired by its previous build is free again when
// that slot builds next.
#define M8_RENDER_CONTROL_RETIRE_HEAD 4u
#define M8_RENDER_CONTROL_RETIRE_TAIL 5u
#define M8_RENDER_CONTROL_WORDS 8u
#define M8_RENDER_RETIRE_MASK (M8_RENDER_PAGE_CAPACITY - 1u)
#define M8_RENDER_FREE_BASE M8_RENDER_CONTROL_WORDS
#define M8_RENDER_RETIRE_BASE (M8_RENDER_CONTROL_WORDS + M8_RENDER_PAGE_CAPACITY)
#define M8_RENDER_ALLOC_BASE \
    (M8_RENDER_CONTROL_WORDS + 2u * M8_RENDER_PAGE_CAPACITY)
#define M8_RENDER_LINK_BASE \
    (M8_RENDER_CONTROL_WORDS + 3u * M8_RENDER_PAGE_CAPACITY)
// Owner (tile + 1) of every page of the publication under validation.
#define M8_RENDER_OWNER_BASE \
    (M8_RENDER_CONTROL_WORDS + 4u * M8_RENDER_PAGE_CAPACITY)
// Tile runtime.w: disposable readout scheduling bits, never persisted.
// Bits 3..28 are exact 26-neighbour boundary dependencies. Bit 1 is unused.
#define M8_RENDER_DIRTY 1u
#define M8_RENDER_REBUILD 4u
#define M8_RENDER_DEPENDENCY_SHIFT 3u
#define M8_RENDER_DEPENDENCY_MASK 0x1ffffff8u
#define M8_RENDER_DIRTY_QUEUED 0x20000000u
#define M8_RENDER_VIEW_MARK 0x40000000u
#define M8_RENDER_JOURNAL_CAPACITY 65536u
#define M8_RENDER_JOURNAL_MASK (M8_RENDER_JOURNAL_CAPACITY - 1u)
#define M8_RENDER_JOURNAL_DIRTY_BASE 0u
// One journal entry is (physical slot, logical identity); a slot recycled by
// eviction can never mark the tile that took its place.
#define M8_RENDER_JOURNAL_ENTRY_WORDS 2u
#define M8_RENDER_JOURNAL_WORDS \
    (M8_RENDER_JOURNAL_CAPACITY * M8_RENDER_JOURNAL_ENTRY_WORDS)
// Residency changes move a whole tile, so they depend on all 26 neighbours.
#define M8_RENDER_RESIDENCY_DEPENDENCY_MASK M8_RENDER_DEPENDENCY_MASK
#define MERKABA_M8_LOAD_REQUEST_CAPACITY 262144u
#define MERKABA_M8_LOAD_REQUEST_MASK 262143u
#define MERKABA_M8_SURFACE_CANDIDATE_CAPACITY 2097152u
#define MERKABA_EXPORT_KNOWN_FREE -512
#define MERKABA_NEEDS_CARVE_FLAG 2u
#define MERKABA_M8_CHUNK_PRESENCE_STRIDE 9u
#define MERKABA_M8_OWNER_CHUNK_OFFSET MERKABA_M8_BLOCK_CAPACITY
#define MERKABA_M8_CLAIM_BLOCK_OFFSET 0u
#define MERKABA_M8_CLAIM_CHUNK_OFFSET MERKABA_M8_BLOCK_CAPACITY
#define MERKABA_M8_CLAIM_TILE_OFFSET \
    (MERKABA_M8_BLOCK_CAPACITY + MERKABA_M8_CHUNK_CAPACITY)

#define M8_COUNTER_BLOCK_COUNT 0u
#define M8_COUNTER_CHUNK_COUNT 1u
#define M8_COUNTER_HOT_TILE_COUNT 2u
#define M8_COUNTER_COLD_TILE_COUNT 3u
#define M8_COUNTER_HASH_COLLISIONS 4u
#define M8_COUNTER_HASH_PROBES 5u
#define M8_COUNTER_HASH_MAX_PROBE 6u
#define M8_COUNTER_BLOCK_OVERFLOW 7u
#define M8_COUNTER_CHUNK_OVERFLOW 8u
#define M8_COUNTER_TILE_STARVATION 9u
#define M8_COUNTER_VALID_SURFACE_CANDIDATES 10u
#define M8_COUNTER_UNIQUE_SURFACE_KERNELS 11u
#define M8_COUNTER_UNRESOLVED_SURFACE_TILES 12u
#define M8_COUNTER_SURFACE_TILES_ALLOCATED 13u
#define M8_COUNTER_SCAN_COLD_MISSES 14u
#define M8_COUNTER_TOUCHED_TILE_COUNT 15u
#define M8_COUNTER_SURFACE_QUEUE_COUNT 16u
#define M8_COUNTER_CARVE_QUERY_TILES 17u
#define M8_COUNTER_CARVE_ACTIVE_KERNELS 18u
#define M8_COUNTER_LOAD_REQUEST_COUNT 19u
#define M8_COUNTER_WRITEBACK_COUNT 20u
#define M8_COUNTER_VISIBLE_TILE_COUNT 21u
#define M8_COUNTER_LOGICAL_VISIBLE_PRIMITIVES 22u
#define M8_COUNTER_RENDER_PRIMITIVE_OVERFLOW 23u
#define M8_COUNTER_LATE_DRAW_COLD_MISSES 24u
// Ring tiles (26-neighbour tile ring of every tile an attempt may dirty)
// that are COLD or LOADING; any non-zero value blocks the canonical commit.
#define M8_COUNTER_UNRESOLVED_RING_TILES 25u
#define M8_COUNTER_CANDIDATE_BLOCKS 26u
#define M8_COUNTER_HASH_HIT_BLOCKS 27u
#define M8_COUNTER_VISIBLE_CHUNKS 28u
#define M8_COUNTER_OCCUPIED_CONSIDERED 29u
#define M8_COUNTER_READOUT_PLANE_VALID 30u
#define M8_COUNTER_READOUT_EMITTED_PATCHES 31u
#define M8_COUNTER_NEW_BLOCK_QUEUE_COUNT 32u
#define M8_COUNTER_NEW_CHUNK_QUEUE_COUNT 33u
#define M8_COUNTER_NEW_TILE_QUEUE_COUNT 34u
#define M8_COUNTER_PENDING_NEW_TILE_COUNT 35u
#define M8_COUNTER_SURFACE_CANDIDATE_COUNT 36u
#define M8_COUNTER_SURFACE_CANDIDATE_OVERFLOW 37u
#define M8_COUNTER_HASH_FULL 38u
#define M8_COUNTER_FAILED_READS 39u
#define M8_COUNTER_FAILED_WRITES 40u
#define M8_COUNTER_STORAGE_BACKPRESSURE 41u
#define M8_COUNTER_OCCUPIED_KERNEL_COUNT 42u
#define M8_COUNTER_CARVE_TILE_COUNT 43u
#define M8_COUNTER_OBSERVATION_COMPLETED 44u
#define M8_COUNTER_EVICTION_CURSOR 45u
#define M8_COUNTER_LOADS_INSTALLED 46u
#define M8_COUNTER_OBSERVATION_TOKEN 47u
#define M8_COUNTER_CARVE_QUERY_BLOCKS 48u
#define M8_COUNTER_WRITEBACK_TILES 49u
// Diagnostic only: tiles of the coverage sphere not drawn yet because the
// tile or its 26-neighbour ring is not resident. Never a publication failure.
#define M8_COUNTER_READOUT_COLD_IN_COVERAGE 50u
#define M8_COUNTER_EVICTION_NEEDED 51u
#define M8_COUNTER_OBSERVATION_FAILURE 52u
#define M8_COUNTER_FAILED_OBSERVATIONS 53u
#define M8_COUNTER_FREE_TILE_COUNT 54u
#define M8_COUNTER_EVICTION_CLEAN_BUDGET 55u
#define M8_COUNTER_CARVE_CLASSIFIED_FREE 56u
#define M8_COUNTER_CARVE_CLASSIFIED_SURFACE 57u
#define M8_COUNTER_CARVE_CLASSIFIED_UNKNOWN 58u
#define M8_COUNTER_CARVE_EVIDENCE_DECREMENTS 59u
#define M8_COUNTER_CARVE_OCCUPIED_TO_FREE 60u
#define M8_COUNTER_CARVE_BITS_RETIRED 61u
#define M8_COUNTER_COLD_CARVE_TILES_REQUESTED 62u
#define M8_COUNTER_UNRESOLVED_CARVE_TILES 63u
#define M8_COUNTER_RENDER_BATCH_CURSOR 64u
#define M8_COUNTER_READOUT_BUILD_STATUS 69u

#define MERKABA_READOUT_BUILDING 0u
#define MERKABA_READOUT_SKIPPED 1u
#define MERKABA_READOUT_FAILED 2u
#define MERKABA_READOUT_PUBLISHED 3u
#define MERKABA_READOUT_EMIT_FAILED 5u
#define M8_COUNTER_NEW_TILE_RESERVATION_BASE 65u
#define M8_COUNTER_EVICTION_CLEAN_TICKET 66u
#define M8_COUNTER_CLEANUP_TOUCHED_COUNT 67u
#define M8_COUNTER_CLEANUP_PENDING_COUNT 68u
#define M8_COUNTER_CARVE_FREE_RADIAL_0 70u
#define M8_COUNTER_CARVE_FREE_RADIAL_1 71u
#define M8_COUNTER_CARVE_FREE_RADIAL_2 72u
#define M8_COUNTER_CARVE_FREE_RADIAL_3 73u
#define M8_COUNTER_CARVE_FREE_RADIAL_4 74u
#define M8_COUNTER_CARVE_FREE_RADIAL_5 75u
#define M8_COUNTER_CARVE_FREE_RADIAL_6 76u
#define M8_COUNTER_CARVE_FREE_RADIAL_7 77u
#define M8_COUNTER_JOINT_ACCEPTED_CENTER 78u
#define M8_COUNTER_JOINT_ACCEPTED_MID 79u
#define M8_COUNTER_JOINT_ACCEPTED_EDGE 80u
#define M8_COUNTER_AUTHORITY_DISCOVERY 81u
#define M8_COUNTER_AUTHORITY_SUPPORT 82u
#define M8_COUNTER_AUTHORITY_REVISION 83u
#define M8_COUNTER_OFF_AXIS_MUTATION_BLOCKED 84u
#define M8_COUNTER_SURFACE_REPLACEMENT 85u
#define M8_COUNTER_SAME_OBSERVATION_CONFLICT 86u
#define M8_COUNTER_READOUT_EMITTED_TRIANGLES 87u
#define M8_COUNTER_CARVE_CHEAP_INVALID_PROJECTION_DEPTH 88u
#define M8_COUNTER_CARVE_CHEAP_NOT_IN_FRONT 89u
#define M8_COUNTER_CARVE_CHEAP_OUTSIDE_RAY_TUBE 90u
#define M8_COUNTER_CARVE_CHEAP_OUTSIDE_OUTER_ATTENTION 91u
#define M8_COUNTER_CARVE_CHEAP_SURFACE_ENDPOINT 92u
#define M8_COUNTER_CARVE_EXACT_EVALUATIONS 93u
#define M8_COUNTER_CARVE_EXACT_INCIDENCE_REJECT 94u
#define M8_COUNTER_CARVE_EXACT_DILATION_REJECT 95u
#define M8_COUNTER_READOUT_PLANE_LEGACY_INVALID 96u
#define M8_COUNTER_READOUT_EMITTED_VERTICES 97u
// 1 when the attempt was blocked by something no tile address describes
// (EVICTING, CLAIMED chunk, more than M8_DEPENDENCY_SLOTS dependencies).
#define M8_COUNTER_DEPENDENCY_UNADDRESSED 98u
#define M8_COUNTER_RENDER_DIRTY_MARKS 99u
#define M8_COUNTER_RENDER_REBUILD_TILES 100u
#define M8_COUNTER_RENDER_JOURNAL_BATCH 101u
#define M8_COUNTER_RENDER_JOURNAL_HEAD 102u
#define M8_COUNTER_RENDER_JOURNAL_TAIL 103u
#define M8_COUNTER_RENDER_VIEW_OVERFLOW 104u
#define M8_COUNTER_RENDER_PUBLICATION_INVALID 105u
#define M8_COUNTER_RENDER_BATCH_BASE 106u
#define M8_COUNTER_RENDER_JOURNAL_OVERFLOW 107u
// Invariant breach: a listed tile met a non-resident halo tile while
// building. The tile fails closed; nothing waits or retries on it.
#define M8_COUNTER_READOUT_RING_INVARIANT 108u
#define M8_COUNTER_RENDER_CAPACITY_FAILED 109u
#define M8_COUNTER_RENDER_PUBLISHED_PATCHES 110u
#define M8_COUNTER_RENDER_PUBLISHED_TILES 111u
// Dependencies of the open scan/erase attempt: tileRefIndex + 1, 0 = empty.
// FinalizeObservation/FinalizeFineErase issue their loads and publish them
// as logical addresses in _M8AttemptCompletion[3..18]; the attempt retries
// on their installation event, never on an epoch.
#define M8_COUNTER_DEPENDENCY_0 112u
#define M8_COUNTER_DEPENDENCY_1 113u
#define M8_COUNTER_DEPENDENCY_2 114u
#define M8_COUNTER_DEPENDENCY_3 115u
#define M8_COUNTER_DEPENDENCY_4 116u
#define M8_COUNTER_DEPENDENCY_5 117u
#define M8_COUNTER_DEPENDENCY_6 118u
#define M8_COUNTER_DEPENDENCY_7 119u
#define M8_COUNTER_DEPENDENCY_8 120u
#define M8_COUNTER_DEPENDENCY_9 121u
#define M8_COUNTER_DEPENDENCY_10 122u
#define M8_COUNTER_DEPENDENCY_11 123u
#define M8_COUNTER_DEPENDENCY_12 124u
#define M8_COUNTER_DEPENDENCY_13 125u
#define M8_COUNTER_DEPENDENCY_14 126u
#define M8_COUNTER_DEPENDENCY_15 127u
#define M8_COUNTER_COUNT 128u
#define M8_COUNTER_DEPENDENCY_BASE M8_COUNTER_DEPENDENCY_0
#define M8_DEPENDENCY_SLOTS 16u
#define M8_ATTEMPT_DEPENDENCY_RECORD 3u

#define M8_OBSERVATION_FAILURE_SURFACE_CAPACITY 1u
#define M8_OBSERVATION_FAILURE_BLOCK_CAPACITY 2u
#define M8_OBSERVATION_FAILURE_CHUNK_CAPACITY 4u
#define M8_OBSERVATION_FAILURE_HASH_CAPACITY 8u
#define M8_OBSERVATION_FAILURE_TIMEOUT 16u
#define M8_OBSERVATION_FAILURE_PHYSICAL_CAPACITY 32u
#define M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY 64u
#define M8_ATTEMPT_COMPLETION_READOUT_CHANGED 0x80000000u

struct KernelState
{
    int evidence;
    uint packedColor;
    uint colorConfidence;
    uint flags;
};

struct M8HashEntry
{
    int3 blockCoord;
    uint blockRef;
};

struct M8TileAddress
{
    int3 blockCoord;
    uint localAddress;
};

// Canonical 26-direction M8 chart. It classifies support direction only; the
// stored measured plane normal remains full octahedral precision.
int3 MerkabaNearestGridNormalStep(float3 gridNormal)
{
    gridNormal = normalize(gridNormal);
    float3 magnitude = abs(gridNormal);
    int3 direction = int3(gridNormal.x >= 0.0 ? 1 : -1,
        gridNormal.y >= 0.0 ? 1 : -1,
        gridNormal.z >= 0.0 ? 1 : -1);

    float axisScore = max(magnitude.x, max(magnitude.y, magnitude.z));
    float3 faceScores = float3(magnitude.x + magnitude.y,
        magnitude.x + magnitude.z, magnitude.y + magnitude.z) *
        0.70710678118;
    float faceScore = max(faceScores.x, max(faceScores.y, faceScores.z));
    float bodyScore = (magnitude.x + magnitude.y + magnitude.z) *
        0.57735026919;

    int3 step = direction;
    if (axisScore >= faceScore && axisScore >= bodyScore)
    {
        if (magnitude.x >= magnitude.y && magnitude.x >= magnitude.z)
            step = int3(direction.x, 0, 0);
        else if (magnitude.y >= magnitude.z)
            step = int3(0, direction.y, 0);
        else
            step = int3(0, 0, direction.z);
    }
    else if (faceScore >= bodyScore)
    {
        if (faceScores.x >= faceScores.y && faceScores.x >= faceScores.z)
            step = int3(direction.x, direction.y, 0);
        else if (faceScores.y >= faceScores.z)
            step = int3(direction.x, 0, direction.z);
        else
            step = int3(0, direction.y, direction.z);
    }
    return step;
}

// Deterministic integer tangents for the existing 26-direction M8 chart.
// These are topology directions only; measured plane normals remain exact.
void MerkabaGridSheetTangents(int3 step, out int3 tangent0,
    out int3 tangent1)
{
    int first = step.x != 0 ? step.x : step.y != 0 ? step.y : step.z;
    if (first < 0) step = -step;
    int nonZero = abs(step.x) + abs(step.y) + abs(step.z);
    if (nonZero == 1)
    {
        tangent0 = step.x != 0 ? int3(0, 1, 0) : int3(1, 0, 0);
        tangent1 = step.z != 0 ? int3(0, 1, 0) : int3(0, 0, 1);
        return;
    }
    if (nonZero == 2)
    {
        if (step.z == 0)
        {
            tangent0 = int3(step.y, -step.x, 0);
            tangent1 = int3(0, 0, 1);
        }
        else if (step.y == 0)
        {
            tangent0 = int3(step.z, 0, -step.x);
            tangent1 = int3(0, 1, 0);
        }
        else
        {
            tangent0 = int3(0, step.z, -step.y);
            tangent1 = int3(1, 0, 0);
        }
        return;
    }
    tangent0 = int3(step.y, -step.x, 0);
    tangent1 = int3(step.z, 0, -step.x);
}

RWStructuredBuffer<M8HashEntry> _M8HashEntries;
StructuredBuffer<M8HashEntry> _M8HashEntriesRead;
RWStructuredBuffer<uint4> _M8OwnerRecords;
StructuredBuffer<uint4> _M8OwnerRecordsRead;
RWStructuredBuffer<uint> _M8BlockChunkRefs;
StructuredBuffer<uint> _M8BlockChunkRefsRead;
RWStructuredBuffer<uint> _M8BlockPresenceL0;
RWStructuredBuffer<uint> _M8BlockPresenceL1;
RWStructuredBuffer<uint> _M8BlockPresenceL2;
StructuredBuffer<uint> _M8BlockPresenceL0Read;
StructuredBuffer<uint> _M8BlockPresenceL1Read;
StructuredBuffer<uint> _M8BlockPresenceL2Read;

RWStructuredBuffer<uint> _M8ChunkTileRefs;
StructuredBuffer<uint> _M8ChunkTileRefsRead;
RWStructuredBuffer<uint> _M8ChunkPresence;
StructuredBuffer<uint> _M8ChunkPresenceRead;

RWStructuredBuffer<KernelState> _M8KernelStates0;
RWStructuredBuffer<KernelState> _M8KernelStates1;
RWStructuredBuffer<KernelState> _M8KernelStates2;
RWStructuredBuffer<KernelState> _M8KernelStates3;
StructuredBuffer<KernelState> _M8KernelStates0Read;
StructuredBuffer<KernelState> _M8KernelStates1Read;
StructuredBuffer<KernelState> _M8KernelStates2Read;
StructuredBuffer<KernelState> _M8KernelStates3Read;
RWStructuredBuffer<uint4> _M8TileBits;
StructuredBuffer<uint4> _M8TileBitsRead;
RWStructuredBuffer<uint4> _M8TileRecords;
StructuredBuffer<uint4> _M8TileRecordsRead;
RWStructuredBuffer<uint> _M8FreeTileStack;
StructuredBuffer<uint> _M8FreeTileStackRead;
RWStructuredBuffer<uint> _M8Counters;
StructuredBuffer<uint> _M8CountersRead;

RWStructuredBuffer<uint2> _M8ClaimQueue;
StructuredBuffer<uint2> _M8ClaimQueueRead;
RWStructuredBuffer<uint> _M8PendingNewTileRefs;
StructuredBuffer<uint> _M8PendingNewTileRefsRead;
RWStructuredBuffer<M8TileAddress> _M8LoadRequests;
StructuredBuffer<uint> _M8LoadRequestReadCount;
RWStructuredBuffer<uint2> _M8WritebackQueue;
StructuredBuffer<uint2> _M8WritebackQueueRead;
RWStructuredBuffer<uint4> _M8WritebackStaging;
RWStructuredBuffer<M8TileAddress> _M8LoadStagingAddresses;
StructuredBuffer<M8TileAddress> _M8LoadStagingAddressesRead;
StructuredBuffer<KernelState> _M8LoadStagingStates;

uint M8ChunkPresenceL0Index(uint chunkIndex)
{
    return chunkIndex * MERKABA_M8_CHUNK_PRESENCE_STRIDE;
}

uint M8ChunkPresenceL1Index(uint chunkIndex, uint d1)
{
    return M8ChunkPresenceL0Index(chunkIndex) + 1u + d1;
}

uint M8TileWordIndex(uint physicalSlot, uint word)
{
    return physicalSlot * MERKABA_M8_TILE_WORDS + word;
}

uint M8TileMetaIndex(uint physicalSlot)
{
    return physicalSlot * 2u;
}

uint M8TileRuntimeIndex(uint physicalSlot)
{
    return physicalSlot * 2u + 1u;
}

uint4 M8LoadTileMetaRead(uint physicalSlot)
{
    return _M8TileRecordsRead[M8TileMetaIndex(physicalSlot)];
}

uint4 M8LoadTileRuntimeRead(uint physicalSlot)
{
    return _M8TileRecordsRead[M8TileRuntimeIndex(physicalSlot)];
}

uint2 M8LoadChunkOwnerRead(uint chunkIndex)
{
    return _M8OwnerRecordsRead[MERKABA_M8_OWNER_CHUNK_OFFSET +
        chunkIndex].xy;
}

int3 M8LoadBlockCoordRead(uint blockIndex)
{
    return asint(_M8OwnerRecordsRead[blockIndex].xyz);
}

uint M8BankStateIndex(uint physicalSlot, uint kernelLocal)
{
    return (physicalSlot & MERKABA_M8_TILE_BANK_MASK) *
        MERKABA_M8_KERNELS_PER_TILE + kernelLocal;
}

KernelState M8LoadKernelState(uint physicalSlot, uint kernelLocal)
{
    uint index = M8BankStateIndex(physicalSlot, kernelLocal);
    uint bank = physicalSlot >> MERKABA_M8_TILE_BANK_SHIFT;
    KernelState state = (KernelState)0;
    if (bank == 0u) state = _M8KernelStates0[index];
    else if (bank == 1u) state = _M8KernelStates1[index];
    else if (bank == 2u) state = _M8KernelStates2[index];
    else state = _M8KernelStates3[index];
    return state;
}

KernelState M8LoadKernelStateRead(uint physicalSlot, uint kernelLocal)
{
    uint index = M8BankStateIndex(physicalSlot, kernelLocal);
    uint bank = physicalSlot >> MERKABA_M8_TILE_BANK_SHIFT;
    KernelState state = (KernelState)0;
    if (bank == 0u) state = _M8KernelStates0Read[index];
    else if (bank == 1u) state = _M8KernelStates1Read[index];
    else if (bank == 2u) state = _M8KernelStates2Read[index];
    else state = _M8KernelStates3Read[index];
    return state;
}

void M8StoreKernelState(uint physicalSlot, uint kernelLocal, KernelState state)
{
    uint index = M8BankStateIndex(physicalSlot, kernelLocal);
    uint bank = physicalSlot >> MERKABA_M8_TILE_BANK_SHIFT;
    if (bank == 0u) _M8KernelStates0[index] = state;
    else if (bank == 1u) _M8KernelStates1[index] = state;
    else if (bank == 2u) _M8KernelStates2[index] = state;
    else _M8KernelStates3[index] = state;
}

void M8CounterIncrement(uint counter)
{
    uint ignored;
    InterlockedAdd(_M8Counters[counter], 1u, ignored);
}

void M8RecordUnaddressedDependency()
{
    uint ignored;
    InterlockedMax(_M8Counters[M8_COUNTER_DEPENDENCY_UNADDRESSED], 1u,
        ignored);
}

// One distinct tile the open attempt waits for. Slots are claimed by
// compare-exchange; a full table degrades to an unaddressed dependency.
void M8RecordDependency(uint tileRefIndex)
{
    uint value = tileRefIndex + 1u;
    [loop]
    for (uint slot = 0u; slot < M8_DEPENDENCY_SLOTS; slot++)
    {
        uint prior;
        InterlockedCompareExchange(
            _M8Counters[M8_COUNTER_DEPENDENCY_BASE + slot], 0u, value, prior);
        if (prior == 0u || prior == value) return;
    }
    M8RecordUnaddressedDependency();
}

// Union skin depends on occupancy, measured-plane/free-side compatibility
// and emitted colour. KNOWN-FREE threshold crossings are geometry inputs
// because they select which overlapping sheet owns a shared boundary.
bool M8RenderInputChanged(KernelState before, KernelState after)
{
    const uint planeMask = 0xfffffffcu;
    bool beforeFree = (before.flags & 1u) == 0u &&
        before.evidence <= MERKABA_EXPORT_KNOWN_FREE;
    bool afterFree = (after.flags & 1u) == 0u &&
        after.evidence <= MERKABA_EXPORT_KNOWN_FREE;
    return ((before.flags ^ after.flags) & 1u) != 0u ||
        ((before.flags ^ after.flags) & planeMask) != 0u ||
        beforeFree != afterFree ||
        ((before.packedColor ^ after.packedColor) & 0xfcfcfcfcu) != 0u;
}

bool M8RenderNeighbourInputChanged(KernelState before, KernelState after)
{
    const uint planeMask = 0xfffffffcu;
    bool beforeFree = (before.flags & 1u) == 0u &&
        before.evidence <= MERKABA_EXPORT_KNOWN_FREE;
    bool afterFree = (after.flags & 1u) == 0u &&
        after.evidence <= MERKABA_EXPORT_KNOWN_FREE;
    return ((before.flags ^ after.flags) & 1u) != 0u ||
        ((before.flags ^ after.flags) & planeMask) != 0u ||
        beforeFree != afterFree;
}

uint M8RenderBoundaryDependencyMask(uint kernelLocal)
{
    uint x = kernelLocal & 7u;
    uint y = (kernelLocal >> 3u) & 7u;
    uint z = (kernelLocal >> 6u) & 7u;
    // A boundary mutation can change the shared skin of every neighbour tile
    // its support and connectivity reach, so the closure is the full 26 set.
    uint ordinal = 0u;
    uint mask = 0u;
    [unroll]
    for (int dz = -1; dz <= 1; dz++)
    [unroll]
    for (int dy = -1; dy <= 1; dy++)
    [unroll]
    for (int dx = -1; dx <= 1; dx++)
    {
        if (dx == 0 && dy == 0 && dz == 0) continue;
        bool reaches = (dx >= 0 || x == 0u) && (dx <= 0 || x == 7u) &&
            (dy >= 0 || y == 0u) && (dy <= 0 || y == 7u) &&
            (dz >= 0 || z == 0u) && (dz <= 0 || z == 7u);
        if (reaches)
            mask |= 1u << (M8_RENDER_DEPENDENCY_SHIFT + ordinal);
        ordinal++;
    }
    return mask;
}

void M8MarkRenderDirty(uint physicalSlot, uint kernelLocal,
    bool affectsNeighbours)
{
    uint bits = M8_RENDER_DIRTY;
    if (affectsNeighbours)
        bits |= M8RenderBoundaryDependencyMask(kernelLocal);
    uint previous;
    InterlockedOr(_M8TileRecords[M8TileRuntimeIndex(physicalSlot)].w,
        bits, previous);
    if ((previous & M8_RENDER_DIRTY) == 0u)
        M8CounterIncrement(M8_COUNTER_RENDER_DIRTY_MARKS);
}

bool M8IsHotRef(uint tileRef)
{
    return tileRef >= 1u && tileRef <= MERKABA_M8_PHYSICAL_TILE_CAPACITY;
}

uint M8PhysicalSlot(uint tileRef)
{
    return tileRef - 1u;
}

void M8PushPhysicalTile(uint physicalSlot)
{
    uint previous;
    InterlockedAdd(_M8Counters[M8_COUNTER_FREE_TILE_COUNT], 1u, previous);
    if (previous < MERKABA_M8_PHYSICAL_TILE_CAPACITY)
        _M8FreeTileStack[previous] = physicalSlot;
}

uint M8HashEntryIndex(uint bucket, uint slot)
{
    return bucket * MERKABA_M8_HASH_SLOTS_PER_BUCKET + slot;
}

bool M8TryMatchBlockEntry(int3 blockCoord, uint bucket, uint slot,
    inout uint blockIndex)
{
    M8HashEntry entry = _M8HashEntriesRead[M8HashEntryIndex(bucket, slot)];
    if (entry.blockRef == MERKABA_REF_EMPTY ||
        entry.blockRef == MERKABA_REF_CLAIMED_NEW ||
        !all(entry.blockCoord == blockCoord))
        return false;
    blockIndex = entry.blockRef - 1u;
    return true;
}

bool M8FindBlock(int3 blockCoord, out uint blockIndex)
{
    blockIndex = 0u;
    uint2 buckets = MerkabaHashBucketSearchOrder(blockCoord);
    if (M8TryMatchBlockEntry(blockCoord, buckets.x, 0u, blockIndex) ||
        M8TryMatchBlockEntry(blockCoord, buckets.x, 1u, blockIndex) ||
        M8TryMatchBlockEntry(blockCoord, buckets.x, 2u, blockIndex) ||
        M8TryMatchBlockEntry(blockCoord, buckets.x, 3u, blockIndex) ||
        M8TryMatchBlockEntry(blockCoord, buckets.y, 0u, blockIndex) ||
        M8TryMatchBlockEntry(blockCoord, buckets.y, 1u, blockIndex) ||
        M8TryMatchBlockEntry(blockCoord, buckets.y, 2u, blockIndex) ||
        M8TryMatchBlockEntry(blockCoord, buckets.y, 3u, blockIndex))
        return blockIndex < MERKABA_M8_BLOCK_CAPACITY;
    return false;
}

// Returns READY block index or leaves a single CLAIMED entry for the publish pass.
// Any observed CLAIMED entry defers instead of walking to a later empty slot.
bool M8FindOrClaimBlock(int3 blockCoord, out uint blockIndex,
    out uint failureReason)
{
    blockIndex = 0u;
    failureReason = 0u;
    uint2 buckets = MerkabaHashBucketSearchOrder(blockCoord);
    uint firstEmpty = 0xffffffffu;
    uint probes = 0u;
    bool claimedSeen = false;
    [unroll]
    for (uint bucketOrder = 0u; bucketOrder < 2u; bucketOrder++)
    {
        uint bucket = bucketOrder == 0u ? buckets.x : buckets.y;
        [unroll]
        for (uint slot = 0u; slot < MERKABA_M8_HASH_SLOTS_PER_BUCKET; slot++)
        {
            probes++;
            uint entryIndex = M8HashEntryIndex(bucket, slot);
            M8HashEntry entry = _M8HashEntries[entryIndex];
            if (entry.blockRef != MERKABA_REF_EMPTY &&
                entry.blockRef != MERKABA_REF_CLAIMED_NEW &&
                all(entry.blockCoord == blockCoord))
            {
                uint ignored;
                InterlockedAdd(_M8Counters[M8_COUNTER_HASH_PROBES], probes, ignored);
                InterlockedMax(_M8Counters[M8_COUNTER_HASH_MAX_PROBE], probes, ignored);
                blockIndex = entry.blockRef - 1u;
                return blockIndex < MERKABA_M8_BLOCK_CAPACITY;
            }
            if (entry.blockRef == MERKABA_REF_CLAIMED_NEW)
            {
                claimedSeen = true;
                continue;
            }
            if (entry.blockRef == MERKABA_REF_EMPTY && firstEmpty == 0xffffffffu)
                firstEmpty = entryIndex;
            else if (entry.blockRef != MERKABA_REF_EMPTY)
                M8CounterIncrement(M8_COUNTER_HASH_COLLISIONS);
        }
    }

    if (claimedSeen)
    {
        blockIndex = 0u;
        return false;
    }

    if (firstEmpty == 0xffffffffu)
    {
        M8CounterIncrement(M8_COUNTER_HASH_FULL);
        failureReason = M8_OBSERVATION_FAILURE_HASH_CAPACITY;
        blockIndex = 0u;
        return false;
    }

    uint prior;
    InterlockedCompareExchange(_M8HashEntries[firstEmpty].blockRef,
        MERKABA_REF_EMPTY, MERKABA_REF_CLAIMED_NEW, prior);
    if (prior != MERKABA_REF_EMPTY)
    {
        blockIndex = 0u;
        return false;
    }

    uint allocated;
    InterlockedAdd(_M8Counters[M8_COUNTER_BLOCK_COUNT], 1u, allocated);
    if (allocated >= MERKABA_M8_BLOCK_CAPACITY)
    {
        _M8Counters[M8_COUNTER_BLOCK_OVERFLOW] = 1u;
        failureReason = M8_OBSERVATION_FAILURE_BLOCK_CAPACITY;
        _M8HashEntries[firstEmpty].blockRef = MERKABA_REF_EMPTY;
        blockIndex = 0u;
        return false;
    }
    _M8HashEntries[firstEmpty].blockCoord = blockCoord;
    _M8OwnerRecords[allocated] = uint4(asuint(blockCoord), 0u);
    uint queueIndex;
    InterlockedAdd(_M8Counters[M8_COUNTER_NEW_BLOCK_QUEUE_COUNT], 1u,
        queueIndex);
    if (queueIndex < MERKABA_M8_BLOCK_CAPACITY)
        _M8ClaimQueue[MERKABA_M8_CLAIM_BLOCK_OFFSET + queueIndex] =
            uint2(firstEmpty, allocated);
    blockIndex = allocated;
    return false;
}

bool M8FindOrClaimChunk(uint blockIndex, uint chunkLocal,
    out uint chunkIndex, out uint failureReason)
{
    chunkIndex = 0u;
    failureReason = 0u;
    uint refIndex = blockIndex * MERKABA_M8_BLOCK_CHUNK_COUNT + chunkLocal;
    uint chunkRef = _M8BlockChunkRefs[refIndex];
    if (chunkRef != MERKABA_REF_EMPTY &&
        chunkRef != MERKABA_REF_CLAIMED_NEW)
    {
        chunkIndex = chunkRef - 1u;
        return chunkIndex < MERKABA_M8_CHUNK_CAPACITY;
    }
    if (chunkRef == MERKABA_REF_CLAIMED_NEW)
    {
        chunkIndex = 0u;
        return false;
    }

    uint prior;
    InterlockedCompareExchange(_M8BlockChunkRefs[refIndex],
        MERKABA_REF_EMPTY, MERKABA_REF_CLAIMED_NEW, prior);
    if (prior != MERKABA_REF_EMPTY)
    {
        chunkIndex = 0u;
        return false;
    }
    uint allocated;
    InterlockedAdd(_M8Counters[M8_COUNTER_CHUNK_COUNT], 1u, allocated);
    if (allocated >= MERKABA_M8_CHUNK_CAPACITY)
    {
        _M8Counters[M8_COUNTER_CHUNK_OVERFLOW] = 1u;
        failureReason = M8_OBSERVATION_FAILURE_CHUNK_CAPACITY;
        _M8BlockChunkRefs[refIndex] = MERKABA_REF_EMPTY;
        chunkIndex = 0u;
        return false;
    }
    _M8OwnerRecords[MERKABA_M8_OWNER_CHUNK_OFFSET + allocated] =
        uint4(blockIndex, chunkLocal, 0u, 0u);
    uint queueIndex;
    InterlockedAdd(_M8Counters[M8_COUNTER_NEW_CHUNK_QUEUE_COUNT], 1u,
        queueIndex);
    if (queueIndex < MERKABA_M8_CHUNK_CAPACITY)
        _M8ClaimQueue[MERKABA_M8_CLAIM_CHUNK_OFFSET + queueIndex] =
            uint2(refIndex, allocated);
    chunkIndex = allocated;
    return false;
}

M8TileAddress M8LogicalAddress(uint chunkIndex, uint tileLocal)
{
    uint2 owner = M8LoadChunkOwnerRead(chunkIndex);
    M8TileAddress address;
    address.blockCoord = M8LoadBlockCoordRead(owner.x);
    address.localAddress = owner.y | (tileLocal << 9u);
    return address;
}

bool M8QueueColdTileLoad(uint tileRefIndex, uint chunkIndex, uint tileLocal)
{
    uint prior;
    InterlockedCompareExchange(_M8ChunkTileRefs[tileRefIndex],
        MERKABA_REF_COLD_ON_SSD, MERKABA_REF_LOADING, prior);
    if (prior != MERKABA_REF_COLD_ON_SSD) return false;

    uint requestIndex = 0u;
    bool reserved = false;
    [unroll]
    for (uint attempt = 0u; attempt < 4u; attempt++)
    {
        uint expected = _M8Counters[M8_COUNTER_LOAD_REQUEST_COUNT];
        uint consumed = _M8LoadRequestReadCount[0];
        if (expected - consumed >= MERKABA_M8_LOAD_REQUEST_CAPACITY) break;
        InterlockedCompareExchange(
            _M8Counters[M8_COUNTER_LOAD_REQUEST_COUNT], expected,
            expected + 1u, prior);
        if (prior == expected)
        {
            requestIndex = expected;
            reserved = true;
            break;
        }
    }
    if (reserved)
    {
        _M8LoadRequests[requestIndex & MERKABA_M8_LOAD_REQUEST_MASK] =
            M8LogicalAddress(chunkIndex, tileLocal);
        return true;
    }

    InterlockedCompareExchange(_M8ChunkTileRefs[tileRefIndex],
        MERKABA_REF_LOADING, MERKABA_REF_COLD_ON_SSD, prior);
    _M8Counters[M8_COUNTER_STORAGE_BACKPRESSURE] = 1u;
    return false;
}

int3 M8GlobalKernelCoord(uint physicalSlot, uint kernelLocal)
{
    uint4 meta = _M8TileRecords[M8TileMetaIndex(physicalSlot)];
    M8TileAddress address = M8LogicalAddress(meta.x, meta.y);
    uint chunkLocal = address.localAddress & 0x1ffu;
    uint tileLocal = (address.localAddress >> 9u) & 0x3fu;
    uint d4 = (chunkLocal >> 6u) & 7u;
    uint d3 = (chunkLocal >> 3u) & 7u;
    uint d2 = chunkLocal & 7u;
    uint d1 = (tileLocal >> 3u) & 7u;
    uint d0 = tileLocal & 7u;
    uint3 kernel = uint3(kernelLocal & 7u,
        (kernelLocal >> 3u) & 7u, (kernelLocal >> 6u) & 7u);
    uint3 local;
    local.x = (((d4 >> 0u) & 1u) << 7u) |
        (((d3 >> 0u) & 1u) << 6u) | (((d2 >> 0u) & 1u) << 5u) |
        (((d1 >> 0u) & 1u) << 4u) | (((d0 >> 0u) & 1u) << 3u) | kernel.x;
    local.y = (((d4 >> 1u) & 1u) << 7u) |
        (((d3 >> 1u) & 1u) << 6u) | (((d2 >> 1u) & 1u) << 5u) |
        (((d1 >> 1u) & 1u) << 4u) | (((d0 >> 1u) & 1u) << 3u) | kernel.y;
    local.z = (((d4 >> 2u) & 1u) << 7u) |
        (((d3 >> 2u) & 1u) << 6u) | (((d2 >> 2u) & 1u) << 5u) |
        (((d1 >> 2u) & 1u) << 4u) | (((d0 >> 2u) & 1u) << 3u) | kernel.z;
    return address.blockCoord * MERKABA_M8_BLOCK_KERNEL_SPAN + int3(local);
}


int3 M8GlobalKernelCoordRead(uint physicalSlot, uint kernelLocal)
{
    uint4 meta = M8LoadTileMetaRead(physicalSlot);
    M8TileAddress address = M8LogicalAddress(meta.x, meta.y);
    uint chunkLocal = address.localAddress & 0x1ffu;
    uint tileLocal = (address.localAddress >> 9u) & 0x3fu;
    uint d4 = (chunkLocal >> 6u) & 7u;
    uint d3 = (chunkLocal >> 3u) & 7u;
    uint d2 = chunkLocal & 7u;
    uint d1 = (tileLocal >> 3u) & 7u;
    uint d0 = tileLocal & 7u;
    uint3 kernel = uint3(kernelLocal & 7u,
        (kernelLocal >> 3u) & 7u, (kernelLocal >> 6u) & 7u);
    uint3 local;
    local.x = (((d4 >> 0u) & 1u) << 7u) |
        (((d3 >> 0u) & 1u) << 6u) | (((d2 >> 0u) & 1u) << 5u) |
        (((d1 >> 0u) & 1u) << 4u) | (((d0 >> 0u) & 1u) << 3u) | kernel.x;
    local.y = (((d4 >> 1u) & 1u) << 7u) |
        (((d3 >> 1u) & 1u) << 6u) | (((d2 >> 1u) & 1u) << 5u) |
        (((d1 >> 1u) & 1u) << 4u) | (((d0 >> 1u) & 1u) << 3u) | kernel.y;
    local.z = (((d4 >> 2u) & 1u) << 7u) |
        (((d3 >> 2u) & 1u) << 6u) | (((d2 >> 2u) & 1u) << 5u) |
        (((d1 >> 2u) & 1u) << 4u) | (((d0 >> 2u) & 1u) << 3u) | kernel.z;
    return address.blockCoord * MERKABA_M8_BLOCK_KERNEL_SPAN + int3(local);
}

#endif
