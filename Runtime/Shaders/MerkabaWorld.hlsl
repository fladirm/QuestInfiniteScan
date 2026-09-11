#ifndef MERKABA_WORLD_INCLUDED
#define MERKABA_WORLD_INCLUDED
#define M8_WORLD_MAX_GENERATION 0x3fffffffu

#include "MerkabaSpatial.hlsl"

#define MERKABA_M8_BLOCK_CAPACITY 8192u
#define MERKABA_M8_CHUNK_CAPACITY 262144u
#define MERKABA_M8_PHYSICAL_TILE_CAPACITY 32768u
#define MERKABA_M8_TILE_BANK_CAPACITY 8192u
#define MERKABA_M8_TILE_BANK_SHIFT 13u
#define MERKABA_M8_TILE_BANK_MASK 8191u
#define MERKABA_M8_TILE_WORDS 16u
#define MERKABA_M8_LOAD_REQUEST_CAPACITY 262144u
#define MERKABA_M8_LOAD_REQUEST_MASK 262143u
#define MERKABA_EXPORT_KNOWN_FREE -512
#define MERKABA_R1_SEED_FLAG 2u
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
#define M8_COUNTER_UNRESOLVED_SURFACE_TILES 10u
#define M8_COUNTER_SURFACE_TILES_ALLOCATED 11u
#define M8_COUNTER_SCAN_COLD_MISSES 12u
#define M8_COUNTER_TOUCHED_TILE_COUNT 13u
#define M8_COUNTER_LOAD_REQUEST_COUNT 14u
#define M8_COUNTER_WRITEBACK_COUNT 15u
#define M8_COUNTER_FRAME_EPOCH 16u
#define M8_COUNTER_NEW_BLOCK_QUEUE_COUNT 17u
#define M8_COUNTER_NEW_CHUNK_QUEUE_COUNT 18u
#define M8_COUNTER_NEW_TILE_QUEUE_COUNT 19u
#define M8_COUNTER_PENDING_NEW_TILE_COUNT 20u
#define M8_COUNTER_HASH_FULL 21u
#define M8_COUNTER_FAILED_READS 22u
#define M8_COUNTER_FAILED_WRITES 23u
#define M8_COUNTER_STORAGE_BACKPRESSURE 24u
#define M8_COUNTER_OCCUPIED_KERNEL_COUNT 25u
#define M8_COUNTER_FINE_ERASE_TILE_COUNT 26u
#define M8_COUNTER_OBSERVATION_COMPLETED 27u
#define M8_COUNTER_EVICTION_CURSOR 28u
#define M8_COUNTER_LOADS_INSTALLED 29u
#define M8_COUNTER_OBSERVATION_TOKEN 30u
#define M8_COUNTER_WRITEBACK_TILES 31u
#define M8_COUNTER_READOUT_UNRESOLVED 32u
#define M8_COUNTER_EVICTION_NEEDED 33u
#define M8_COUNTER_OBSERVATION_FAILURE 34u
#define M8_COUNTER_FAILED_OBSERVATIONS 35u
#define M8_COUNTER_FREE_TILE_COUNT 36u
#define M8_COUNTER_EVICTION_CLEAN_BUDGET 37u
#define M8_COUNTER_OBSERVATION_CHANGE_MASK 38u
#define M8_OBSERVATION_CHANGED_R1 1u
#define M8_COUNTER_DIRTY_TILE_COUNT 39u
#define M8_COUNTER_UNRESOLVED_OBSERVATION_TILES 40u
#define M8_COUNTER_RESIDENCY_EPOCH 41u
#define M8_COUNTER_NEW_TILE_RESERVATION_BASE 42u
#define M8_COUNTER_EVICTION_CLEAN_TICKET 43u
#define M8_COUNTER_CLEANUP_TOUCHED_COUNT 44u
#define M8_COUNTER_CLEANUP_PENDING_COUNT 45u
#define M8_COUNTER_READOUT_EMITTED_TRIANGLES 46u
#define M8_COUNTER_READOUT_EMITTED_VERTICES 47u
#define M8_COUNTER_REFINEMENT_PENDING_TILES 48u
#define M8_COUNTER_REFINEMENT_WORK_PROGRESS 49u
#define M8_COUNTER_REFINEMENT_BACKPRESSURE 50u
#define M8_COUNTER_REFINEMENT_UNRESOLVED 51u
#define M8_COUNTER_COUNT 52u

// Observation-local address intents reuse the released BLOCK claim span.
// They contain no surface evidence and are consumed by the existing storage
// publication barrier before that span is used for the next observation.

bool M8ObservationBlockAddress(int3 center, int radius, int side,
    uint ordinal, out int3 coordinate)
{
    coordinate=0;
    // 1625^3 fits uint32; block limits keep all 256 kernel coordinates
    // representable, including signed negative-coordinate boundaries.
    if (radius < 0 || radius > 812 || side != 2*radius+1) return false;
    uint width=(uint)side;
    if (ordinal >= width*width*width) return false;
    int3 offset=int3(ordinal % width,(ordinal/width) % width,
        ordinal/(width*width))-radius;
    if (any(center < -8388608-offset) || any(center > 8388607-offset))
        return false;
    coordinate=center+offset;
    return true;
}

#define M8_OBSERVATION_FAILURE_SURFACE_CAPACITY 1u
#define M8_OBSERVATION_FAILURE_BLOCK_CAPACITY 2u
#define M8_OBSERVATION_FAILURE_CHUNK_CAPACITY 4u
#define M8_OBSERVATION_FAILURE_HASH_CAPACITY 8u
#define M8_OBSERVATION_FAILURE_PHYSICAL_CAPACITY 32u
#define M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY 64u
#define M8_ATTEMPT_COMPLETION_READOUT_CHANGED 0x80000000u
#define M8_ATTEMPT_COMPLETION_REFINEMENT_PROGRESS 0x40000000u

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

#ifndef M8_FLOWER_SRV
#define M8_FLOWER_SRV(registerName)
#endif

RWStructuredBuffer<M8HashEntry> _M8HashEntries;
StructuredBuffer<M8HashEntry> _M8HashEntriesRead;
RWStructuredBuffer<uint4> _M8OwnerRecords;
StructuredBuffer<uint4> _M8OwnerRecordsRead M8_FLOWER_SRV(t0);
RWStructuredBuffer<uint> _M8BlockChunkRefs;
StructuredBuffer<uint> _M8BlockChunkRefsRead;
RWStructuredBuffer<uint> _M8BlockPresenceL0;
RWStructuredBuffer<uint> _M8BlockPresenceL1;
RWStructuredBuffer<uint> _M8BlockPresenceL2;
StructuredBuffer<uint> _M8BlockPresenceL0Read;
StructuredBuffer<uint> _M8BlockPresenceL1Read;
StructuredBuffer<uint> _M8BlockPresenceL2Read;

RWStructuredBuffer<uint> _M8ChunkTileRefs;
StructuredBuffer<uint> _M8ChunkTileRefsRead M8_FLOWER_SRV(t1);
RWStructuredBuffer<uint> _M8ChunkPresence;
StructuredBuffer<uint> _M8ChunkPresenceRead;

RWStructuredBuffer<KernelState> _M8KernelStates0;
RWStructuredBuffer<KernelState> _M8KernelStates1;
RWStructuredBuffer<KernelState> _M8KernelStates2;
RWStructuredBuffer<KernelState> _M8KernelStates3;
StructuredBuffer<KernelState> _M8KernelStates0Read M8_FLOWER_SRV(t2);
StructuredBuffer<KernelState> _M8KernelStates1Read M8_FLOWER_SRV(t3);
StructuredBuffer<KernelState> _M8KernelStates2Read M8_FLOWER_SRV(t4);
StructuredBuffer<KernelState> _M8KernelStates3Read M8_FLOWER_SRV(t5);
RWStructuredBuffer<uint4> _M8TileBits;
StructuredBuffer<uint4> _M8TileBitsRead;
RWStructuredBuffer<uint4> _M8TileRecords;
StructuredBuffer<uint4> _M8TileRecordsRead M8_FLOWER_SRV(t6);
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

// The runtime dirty bit and this exact count change together at a source
// mutation/storage acknowledgement. Receipt capture executes after those
// kernels retire under the source-cut lease; it never samples mid-transition.
// Multiple kernels in one tile may mark concurrently, but only 0 -> 1 counts.
void M8MarkTileDirty(uint physicalSlot)
{
    uint previous;
    InterlockedOr(_M8TileRecords[M8TileRuntimeIndex(physicalSlot)].y, 1u, previous);
    if ((previous & 1u) == 0u)
    {
        uint ignored;
        InterlockedAdd(_M8Counters[M8_COUNTER_DIRTY_TILE_COUNT], 1u, ignored);
    }
}

void M8ClearTileDirty(uint physicalSlot)
{
    uint previous;
    InterlockedAnd(_M8TileRecords[M8TileRuntimeIndex(physicalSlot)].y,
        0xfffffffeu, previous);
    if ((previous & 1u) != 0u)
    {
        uint ignored;
        InterlockedAdd(_M8Counters[M8_COUNTER_DIRTY_TILE_COUNT],
            0xffffffffu, ignored);
    }
}

// Slot lifetime, not surface/fine-detail generation. A halo is refreshed at
// each consumer workgroup entry on the serialized queue; accesses additionally
// validate the logical tile identity, so a 15-bit rollover cannot alias a
// different neighbour. Zero denotes an uninstalled physical slot.
uint M8NextTileSlotGeneration(uint previous)
{
    uint generation = (previous + 1u) & 0x7fffu;
    return generation == 0u ? 1u : generation;
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

void M8SignalResidencyChange()
{
    M8CounterIncrement(M8_COUNTER_RESIDENCY_EPOCH);
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
    {
        _M8FreeTileStack[previous] = physicalSlot;
        M8SignalResidencyChange();
    }
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
    uint queueIndex;
    InterlockedAdd(_M8Counters[M8_COUNTER_NEW_CHUNK_QUEUE_COUNT], 1u,
        queueIndex);
    if (queueIndex < MERKABA_M8_CHUNK_CAPACITY)
        _M8ClaimQueue[MERKABA_M8_CLAIM_CHUNK_OFFSET + queueIndex] =
            uint2(refIndex, allocated);
    chunkIndex = allocated;
    return false;
}

// Claims contain the complete address. Owner records are initialized at the
// existing publication barrier, not by each endpoint claimant. In particular,
// Count needs no ninth writable descriptor for redundant owner-address stores.
void M8PublishBlockClaim(uint2 claim)
{
    _M8OwnerRecords[claim.y] = uint4(asuint(_M8HashEntries[claim.x].blockCoord), 0u);
    _M8HashEntries[claim.x].blockRef = claim.y + 1u;
}

uint2 M8PublishChunkOwner(uint2 claim)
{
    uint2 owner = uint2(claim.x / MERKABA_M8_BLOCK_CHUNK_COUNT,
        claim.x % MERKABA_M8_BLOCK_CHUNK_COUNT);
    _M8OwnerRecords[MERKABA_M8_OWNER_CHUNK_OFFSET + claim.y] = uint4(owner, 0u, 0u);
    return owner;
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
