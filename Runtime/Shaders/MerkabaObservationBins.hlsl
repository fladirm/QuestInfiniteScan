#ifndef GENESIS_MERKABA_OBSERVATION_BINS_INCLUDED
#define GENESIS_MERKABA_OBSERVATION_BINS_INCLUDED

#include "MerkabaWorld.hlsl"
#include "MerkabaSphereFlowerDataAbi.generated.hlsl"

// Exactly four words per HOT slot: attempt stamp, count, offset, emit cursor.
// Count starts at zero after slot installation or touched-only finalization.
// There is one count pass per reservation; retries retain the frozen evidence,
// retire the preceding reservation, and start a new count pass.
RWStructuredBuffer<uint4> _M8ObservationTileBins;
RWStructuredBuffer<M8ObservationRecord> _M8ObservationRecords;
RWStructuredBuffer<uint> _M8TouchedTileQueue;
RWStructuredBuffer<uint> _M8ObservationDispatchArgs;
uint _M8AttemptToken;
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
    if (_M8AttemptToken == 0u ||
        physicalSlot >= _M8ObservationHotSlotCount ||
        physicalSlot >= MERKABA_M8_PHYSICAL_TILE_CAPACITY)
    {
        M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return;
    }
    InterlockedAdd(_M8ObservationTileBins[physicalSlot].y, 1u);
    uint previousStamp;
    InterlockedExchange(_M8ObservationTileBins[physicalSlot].x,
        _M8AttemptToken, previousStamp);
}

bool M8FlowerEmitObservationOwner(uint physicalSlot, uint kernelLocal,
    uint sourcePixel, uint symbolTag, uint precisionKey)
{
    if (_M8Counters[M8_COUNTER_OBSERVATION_FAILURE] != 0u) return false;
    if (_M8AttemptToken == 0u ||
        physicalSlot >= _M8ObservationHotSlotCount ||
        physicalSlot >= MERKABA_M8_PHYSICAL_TILE_CAPACITY ||
        kernelLocal >= MERKABA_M8_KERNELS_PER_TILE ||
        sourcePixel >= M8_FLOWER_OBSERVATION_SOURCE_CAPACITY)
    {
        M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return false;
    }
    uint4 bin = _M8ObservationTileBins[physicalSlot];
    if (bin.x != _M8AttemptToken || bin.y == 0u)
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

#endif
