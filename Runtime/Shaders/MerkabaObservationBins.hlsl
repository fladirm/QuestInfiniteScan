#ifndef GENESIS_MERKABA_OBSERVATION_BINS_INCLUDED
#define GENESIS_MERKABA_OBSERVATION_BINS_INCLUDED

#include "MerkabaWorld.hlsl"
#include "MerkabaSphereFlowerDataAbi.generated.hlsl"

// Exactly four words per HOT slot: observation stamp, count, offset, emit cursor.
// Count starts at zero after slot installation or touched-only finalization.
// There is one count pass per reservation; retries retain the frozen evidence,
// retire the preceding reservation, and start a new count pass.
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

#endif
