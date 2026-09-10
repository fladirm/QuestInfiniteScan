#ifndef GENESIS_MERKABA_OBSERVATION_BINS_INCLUDED
#define GENESIS_MERKABA_OBSERVATION_BINS_INCLUDED

#include "MerkabaWorld.hlsl"
#include "MerkabaSphereFlowerDataAbi.generated.hlsl"

// Exactly four words per HOT slot: observation stamp, count, offset, emit cursor.
// Count starts at zero after slot installation or touched-only finalization.
// Each count pass registers its touched slots immediately. Allocation discovery
// can therefore retire precisely those counts before the final count, without
// depending on a Reserve dispatch that has not happened yet.
RWStructuredBuffer<uint4> _M8ObservationTileBins;
StructuredBuffer<uint4> _M8ObservationTileBinsRead;
RWStructuredBuffer<M8ObservationRecord> _M8ObservationRecords;
RWStructuredBuffer<uint> _M8TouchedTileQueue;
StructuredBuffer<uint> _M8TouchedTileQueueRead;
RWStructuredBuffer<uint> _M8ObservationDispatchArgs;
// Three uint4 packets in the existing indirect resource: tile work at 0,
// allocation-publication gate at 16 B, physical installs at 32 B.
#define M8_OBSERVATION_ALLOCATION_ARGS 4u
#define M8_OBSERVATION_INSTALL_ARGS 8u
uint _M8ObservationToken;
uint _M8ObservationHotSlotCount;
uint _M8ObservationRecordCapacity;

void M8FlowerBinFailure(uint reason)
{
    InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_FAILURE], reason);
}

// Only an already-resolved HOT owner may enter this storage operation.
// No occupancy, normal, root, or surface selection is performed here.
void M8FlowerCountObservationOwner(uint physicalSlot)
{
    if (_M8ObservationToken == 0u ||
        physicalSlot >= _M8ObservationHotSlotCount ||
        physicalSlot >= MERKABA_M8_PHYSICAL_TILE_CAPACITY)
    {
        M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return;
    }
    InterlockedAdd(_M8ObservationTileBins[physicalSlot].y, 1u);
    uint previousStamp;
    InterlockedExchange(_M8ObservationTileBins[physicalSlot].x,
        _M8ObservationToken, previousStamp);
    if (previousStamp != _M8ObservationToken)
    {
        uint touched;
        InterlockedAdd(_M8Counters[M8_COUNTER_TOUCHED_TILE_COUNT], 1u, touched);
        if (touched < MERKABA_M8_PHYSICAL_TILE_CAPACITY)
            _M8TouchedTileQueue[touched] = physicalSlot;
        else
            M8FlowerBinFailure(M8_OBSERVATION_FAILURE_PHYSICAL_CAPACITY);
    }
}

bool M8FlowerEmitObservationOwner(uint physicalSlot, uint kernelLocal,
    uint sourcePixel, uint symbolTag, uint precisionKey)
{
    if (_M8Counters[M8_COUNTER_OBSERVATION_FAILURE] != 0u) return false;
    if (_M8ObservationToken == 0u ||
        physicalSlot >= _M8ObservationHotSlotCount ||
        physicalSlot >= MERKABA_M8_PHYSICAL_TILE_CAPACITY ||
        kernelLocal >= MERKABA_M8_KERNELS_PER_TILE ||
        sourcePixel >= M8_FLOWER_OBSERVATION_SOURCE_CAPACITY)
    {
        M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return false;
    }
    uint4 bin = _M8ObservationTileBins[physicalSlot];
    if (bin.x != _M8ObservationToken || bin.y == 0u)
    {
        M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return false;
    }
    uint ordinal;
    InterlockedAdd(_M8ObservationTileBins[physicalSlot].w, 1u, ordinal);
    if (ordinal >= bin.y || bin.z >= _M8ObservationRecordCapacity ||
        ordinal >= _M8ObservationRecordCapacity - bin.z)
    {
        M8FlowerBinFailure(M8_OBSERVATION_FAILURE_SURFACE_CAPACITY);
        return false;
    }
    M8ObservationRecord record;
    record.TileAndKernel = (physicalSlot << 9u) | kernelLocal;
    record.SourcePixel = sourcePixel;
    record.SymbolTag = symbolTag;
    record.PrecisionKey = precisionKey;
    _M8ObservationRecords[bin.z + ordinal] = record;
    return true;
}

// Called only by the single finalization WG, after its publication barrier.
// A pending quantum retains the complete frozen observation and its bins.
void M8FlowerRetireObservation(uint lane, uint laneCount)
{
    if (_M8Counters[M8_COUNTER_OBSERVATION_COMPLETED] == 0u) return;
    uint touched = min(_M8Counters[M8_COUNTER_TOUCHED_TILE_COUNT],
        MERKABA_M8_PHYSICAL_TILE_CAPACITY);
    for (uint i = lane; i < touched*16u; i += laneCount)
    {
        uint slot = _M8TouchedTileQueueRead[i >> 4u];
        if (slot >= MERKABA_M8_PHYSICAL_TILE_CAPACITY) continue;
        _M8TileBits[M8TileWordIndex(slot,i & 15u)].z = 0u;
        if ((i & 15u) == 0u)
        {
            _M8ObservationTileBins[slot] = 0u.xxxx;
            _M8TileRecords[M8TileRuntimeIndex(slot)].x = 0u;
        }
    }
    uint pending = min(_M8Counters[M8_COUNTER_CLEANUP_PENDING_COUNT],
        MERKABA_M8_PHYSICAL_TILE_CAPACITY);
    for (uint i = lane; i < pending; i += laneCount)
    {
        uint refIndex = _M8PendingNewTileRefsRead[i];
        uint previous;
        InterlockedCompareExchange(_M8ChunkTileRefs[refIndex],
            MERKABA_REF_CLAIMED_NEW,MERKABA_REF_EMPTY,previous);
    }
    DeviceMemoryBarrierWithGroupSync();
    if (lane == 0u)
    {
        _M8Counters[M8_COUNTER_TOUCHED_TILE_COUNT] = 0u;
        _M8Counters[M8_COUNTER_PENDING_NEW_TILE_COUNT] = 0u;
    }
}

// Storage lifetime barrier, also used before an allocation retry of the SAME
// immutable observation. The serialized queue must retire all readers first.
// A new token starts its transient counters here; retries preserve failures
// and ancestor stage. No allocator/residency state or M8 is reset.
groupshared uint m8ObservationBegins;
void M8FlowerResetObservationBins(uint lane)
{
    if(lane==0u)
    {
        m8ObservationBegins=_M8Counters[M8_COUNTER_OBSERVATION_TOKEN]!=_M8ObservationToken?1u:0u;
        _M8Counters[M8_COUNTER_OBSERVATION_TOKEN]=_M8ObservationToken;
    }
    DeviceMemoryBarrierWithGroupSync();
    if(m8ObservationBegins!=0u)
    {
        // The dense ABI's disjoint transient ranges. No whole CounterCount
        // clear: occupancy, free lists and durable residency counters survive.
        bool zero=(lane>=M8_COUNTER_UNRESOLVED_SURFACE_TILES && lane<=M8_COUNTER_SCAN_COLD_MISSES) ||
            (lane>=M8_COUNTER_NEW_BLOCK_QUEUE_COUNT && lane<=M8_COUNTER_PENDING_NEW_TILE_COUNT) ||
            (lane>=M8_COUNTER_FINE_ERASE_TILE_COUNT && lane<=M8_COUNTER_OBSERVATION_COMPLETED) ||
            lane==M8_COUNTER_DUAL_QUERY_BLOCKS || lane==M8_COUNTER_OBSERVATION_FAILURE ||
            (lane>=M8_COUNTER_THROUGH_EVIDENCE_DECREMENTS && lane<=M8_COUNTER_UNRESOLVED_OBSERVATION_TILES) ||
            (lane>=M8_COUNTER_CLEANUP_TOUCHED_COUNT && lane<=M8_COUNTER_CLEANUP_PENDING_COUNT) ||
            (lane>=M8_COUNTER_DUAL_STORAGE_INTENT_COUNT && lane<=M8_COUNTER_REFINEMENT_STAGE);
        // INVALIDATION_OWED is deliberately absent from every transient range.
        // An owed source/peer cut belongs to the world, not to the frame that
        // discovered it, and is finished by whichever snapshots follow.
        if(zero)_M8Counters[lane]=0u;
    }
    DeviceMemoryBarrierWithGroupSync();
    uint count = min(_M8Counters[M8_COUNTER_TOUCHED_TILE_COUNT],
        MERKABA_M8_PHYSICAL_TILE_CAPACITY);
    for (uint index = lane; index < count; index += 256u)
    {
        uint slot = _M8TouchedTileQueue[index];
        if (slot >= min(_M8ObservationHotSlotCount,
                MERKABA_M8_PHYSICAL_TILE_CAPACITY))
        {
            M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
            continue;
        }
        if (_M8ObservationTileBins[slot].x != _M8ObservationToken)
        {
            M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
            continue;
        }
        // Keep this held observation's committed sources in the later
        // publication-only touched set even when OFF cleared every active
        // R1 in a negative-only tile. Its runtime stamp also pins the actual
        // slot generation until peer invalidation has acknowledged all work.
        // The ordinary count reserve still excludes these zero-count bins.
        bool committed=m8ObservationBegins==0u &&
            M8LoadTileRuntimeRead(slot).x==_M8ObservationToken;
        _M8ObservationTileBins[slot] = uint4(committed?_M8ObservationToken:0u,0u,0u,0u);
        // The same frozen observation is re-emitted after a retry. Clear
        // only its touched endpoint mask, leaving occupancy/active bits
        // intact; neither old slot contents nor a failed emit may survive
        // as direct precedence for the new reservation.
        [unroll] for (uint word = 0u; word < 16u; ++word)
            _M8TileBits[M8TileWordIndex(slot, word)].z = 0u;
    }
    DeviceMemoryBarrierWithGroupSync();
    if (lane == 0u)
    {
        _M8Counters[M8_COUNTER_TOUCHED_TILE_COUNT] = 0u;
        // The previous attempt's completion has already been consumed. Only
        // changes made by this quantum should invalidate derived consumers.
        _M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK] = 0u;
        _M8Counters[M8_COUNTER_UNRESOLVED_SURFACE_TILES] = 0u;
        _M8Counters[M8_COUNTER_UNRESOLVED_OBSERVATION_TILES] = 0u;
        _M8Counters[M8_COUNTER_STORAGE_BACKPRESSURE] = 0u;
        _M8Counters[M8_COUNTER_REFINEMENT_PENDING_TILES] = 0u;
        _M8Counters[M8_COUNTER_REFINEMENT_WORK_PROGRESS] = 0u;
        _M8Counters[M8_COUNTER_REFINEMENT_BACKPRESSURE] = 0u;
        _M8Counters[M8_COUNTER_REFINEMENT_UNRESOLVED] = 0u;
        _M8Counters[M8_COUNTER_FINE_LEASE_BUSY] = 0u;
        _M8Counters[M8_COUNTER_DUAL_TOUCH_PUBLICATION] = 0u;
        _M8ObservationDispatchArgs[0] = 0u;
        _M8ObservationDispatchArgs[1] = 1u;
        _M8ObservationDispatchArgs[2] = 1u;
        _M8ObservationDispatchArgs[M8_OBSERVATION_ALLOCATION_ARGS] = 0u;
        _M8ObservationDispatchArgs[M8_OBSERVATION_ALLOCATION_ARGS+1u] = 1u;
        _M8ObservationDispatchArgs[M8_OBSERVATION_ALLOCATION_ARGS+2u] = 1u;
        _M8ObservationDispatchArgs[M8_OBSERVATION_INSTALL_ARGS] = 0u;
        _M8ObservationDispatchArgs[M8_OBSERVATION_INSTALL_ARGS+1u] = 1u;
        _M8ObservationDispatchArgs[M8_OBSERVATION_INSTALL_ARGS+2u] = 1u;
    }
}

#endif
