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
#define MERKABA_M8_VISIBLE_PRIMITIVE_CAPACITY 1048576u
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
#define M8_COUNTER_FRAME_EPOCH 25u
#define M8_COUNTER_CANDIDATE_BLOCKS 26u
#define M8_COUNTER_HASH_HIT_BLOCKS 27u
#define M8_COUNTER_VISIBLE_CHUNKS 28u
#define M8_COUNTER_OCCUPIED_CONSIDERED 29u
#define M8_COUNTER_PRIMITIVES_BEFORE_FACING 30u
#define M8_COUNTER_PRIMITIVES_REJECTED 31u
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
#define M8_COUNTER_VISIBLE_SAFE_COUNT 50u
#define M8_COUNTER_EVICTION_NEEDED 51u
#define M8_COUNTER_OBSERVATION_FAILURE 52u
#define M8_COUNTER_FAILED_OBSERVATIONS 53u
#define M8_COUNTER_FREE_TILE_COUNT 54u
#define M8_COUNTER_EVICTION_CLEAN_BUDGET 55u
#define M8_COUNTER_ATTEMPT_TOKEN 56u
#define M8_COUNTER_ATTEMPT_COMPLETED_TOKEN 57u
#define M8_COUNTER_CARVE_CLASSIFIED_FREE 58u
#define M8_COUNTER_CARVE_CLASSIFIED_SURFACE 59u
#define M8_COUNTER_CARVE_CLASSIFIED_UNKNOWN 60u
#define M8_COUNTER_CARVE_EVIDENCE_DECREMENTS 61u
#define M8_COUNTER_CARVE_OCCUPIED_TO_FREE 62u
#define M8_COUNTER_CARVE_BITS_RETIRED 63u
#define M8_COUNTER_COLD_CARVE_TILES_REQUESTED 64u
#define M8_COUNTER_UNRESOLVED_CARVE_TILES 65u
#define M8_COUNTER_COUNT 72u

#define M8_OBSERVATION_FAILURE_SURFACE_CAPACITY 1u
#define M8_OBSERVATION_FAILURE_BLOCK_CAPACITY 2u
#define M8_OBSERVATION_FAILURE_CHUNK_CAPACITY 4u
#define M8_OBSERVATION_FAILURE_HASH_CAPACITY 8u
#define M8_OBSERVATION_FAILURE_TIMEOUT 16u

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

bool M8IsHotRef(uint tileRef)
{
    return tileRef >= 1u && tileRef <= MERKABA_M8_PHYSICAL_TILE_CAPACITY;
}

uint M8PhysicalSlot(uint tileRef)
{
    return tileRef - 1u;
}

bool M8TryPopPhysicalTile(out uint physicalSlot)
{
    physicalSlot = 0u;
    [unroll]
    for (uint attempt = 0u; attempt < 16u; attempt++)
    {
        uint available = _M8Counters[M8_COUNTER_FREE_TILE_COUNT];
        if (available == 0u)
        {
            uint ignoredSignal;
            InterlockedExchange(_M8Counters[M8_COUNTER_EVICTION_NEEDED], 1u,
                ignoredSignal);
            return false;
        }
        uint previous;
        InterlockedCompareExchange(
            _M8Counters[M8_COUNTER_FREE_TILE_COUNT], available,
            available - 1u, previous);
        if (previous != available) continue;
        if (available <= 256u)
        {
            uint ignoredSignal;
            InterlockedExchange(_M8Counters[M8_COUNTER_EVICTION_NEEDED], 1u,
                ignoredSignal);
        }
        physicalSlot = _M8FreeTileStackRead[available - 1u];
        return true;
    }
    uint ignoredSignal;
    InterlockedExchange(_M8Counters[M8_COUNTER_EVICTION_NEEDED], 1u,
        ignoredSignal);
    return false;
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

bool M8FindBlock(int3 blockCoord, out uint blockIndex)
{
    blockIndex = 0u;
    uint2 buckets = MerkabaHashBucketSearchOrder(blockCoord);
    [unroll]
    for (uint bucketOrder = 0u; bucketOrder < 2u; bucketOrder++)
    {
        uint bucket = bucketOrder == 0u ? buckets.x : buckets.y;
        [unroll]
        for (uint slot = 0u; slot < MERKABA_M8_HASH_SLOTS_PER_BUCKET; slot++)
        {
            uint entryIndex = M8HashEntryIndex(bucket, slot);
            M8HashEntry entry = _M8HashEntriesRead[entryIndex];
            if (entry.blockRef != MERKABA_REF_EMPTY &&
                entry.blockRef != MERKABA_REF_CLAIMED_NEW &&
                all(entry.blockCoord == blockCoord))
            {
                blockIndex = entry.blockRef - 1u;
                return blockIndex < MERKABA_M8_BLOCK_CAPACITY;
            }
        }
    }
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

bool M8TryOccupiedTileRef(uint tileRef, uint kernelLocal, out bool occupied)
{
    occupied = false;
    if (tileRef == MERKABA_REF_EMPTY) return true;
    if (!M8IsHotRef(tileRef)) return false;
    uint physicalSlot = M8PhysicalSlot(tileRef);
    uint word = M8TileWordIndex(physicalSlot, kernelLocal >> 5u);
    occupied = (_M8TileBitsRead[word].x &
        (1u << (kernelLocal & 31u))) != 0u;
    return true;
}

// Missing logical paths are exact empty. Existing non-HOT payload is unresolved.
// The block hash is touched only when the body-diagonal step crosses an M8 block.
bool M8TryOccupiedNeighbour(MerkabaM8Address currentAddress,
    uint currentPhysicalSlot, uint currentChunkIndex, uint currentBlockIndex,
    int3 neighbourStep, out bool occupied)
{
    occupied = false;
    int3 localSigned = int3(currentAddress.local) + neighbourStep;
    bool sameBlock = all(localSigned >= 0) && all(localSigned < 256);
    int3 blockCoord = currentAddress.blockCoord;
    if (!sameBlock)
    {
        if (localSigned.x < 0) { blockCoord.x--; localSigned.x += 256; }
        else if (localSigned.x >= 256) { blockCoord.x++; localSigned.x -= 256; }
        if (localSigned.y < 0) { blockCoord.y--; localSigned.y += 256; }
        else if (localSigned.y >= 256) { blockCoord.y++; localSigned.y -= 256; }
        if (localSigned.z < 0) { blockCoord.z--; localSigned.z += 256; }
        else if (localSigned.z >= 256) { blockCoord.z++; localSigned.z -= 256; }
    }
    MerkabaM8Address neighbourAddress = MerkabaAddressFromBlockLocal(
        blockCoord, uint3(localSigned));

    if (sameBlock &&
        neighbourAddress.chunkLocal == currentAddress.chunkLocal &&
        neighbourAddress.tileLocal == currentAddress.tileLocal)
    {
        uint word = M8TileWordIndex(currentPhysicalSlot,
            neighbourAddress.kernelLocal >> 5u);
        occupied = (_M8TileBitsRead[word].x &
            (1u << (neighbourAddress.kernelLocal & 31u))) != 0u;
        return true;
    }

    uint chunkIndex = currentChunkIndex;
    if (neighbourAddress.chunkLocal != currentAddress.chunkLocal || !sameBlock)
    {
        uint blockIndex = currentBlockIndex;
        if (!sameBlock && !M8FindBlock(blockCoord, blockIndex)) return true;
        uint chunkRef = _M8BlockChunkRefsRead[blockIndex *
            MERKABA_M8_BLOCK_CHUNK_COUNT + neighbourAddress.chunkLocal];
        if (chunkRef == MERKABA_REF_EMPTY) return true;
        if (chunkRef == MERKABA_REF_CLAIMED_NEW) return false;
        chunkIndex = chunkRef - 1u;
    }

    uint tileRef = _M8ChunkTileRefsRead[chunkIndex *
        MERKABA_M8_TILES_PER_CHUNK + neighbourAddress.tileLocal];
    return M8TryOccupiedTileRef(tileRef, neighbourAddress.kernelLocal,
        occupied);
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
