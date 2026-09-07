#ifndef GENESIS_MERKABA_OBSERVATION_OWNERS_INCLUDED
#define GENESIS_MERKABA_OBSERVATION_OWNERS_INCLUDED

#include "MerkabaObservationBins.hlsl"
#include "MerkabaSphereFlower.generated.hlsl"

// firstOwner = floor(endpoint / a). Half-open supports give precisely
// firstOwner + {0,1}^3, including at integer and negative-coordinate ties.
// A root is looked up once per distinct Block, Chunk, or Tile, not per owner.
// Tile claims share the existing bounded storage claim arena. The service
// pass consumes these requests before the arena is reused for HOT installs.
// x = existing chunk/tile ref index; y = the claimed EMPTY or COLD value.
void M8FlowerRequestObservationTile(uint refIndex, uint currentRef)
{
    if (currentRef != MERKABA_REF_EMPTY &&
        currentRef != MERKABA_REF_COLD_ON_SSD) return;
    uint claimed = currentRef == MERKABA_REF_EMPTY ?
        MERKABA_REF_CLAIMED_NEW : MERKABA_REF_LOADING;
    uint previous;
    InterlockedCompareExchange(_M8ChunkTileRefs[refIndex], currentRef,
        claimed, previous);
    if (previous != currentRef) return;
    uint ticket;
    InterlockedAdd(_M8Counters[M8_COUNTER_NEW_TILE_QUEUE_COUNT], 1u, ticket);
    if (ticket < MERKABA_M8_PHYSICAL_TILE_CAPACITY)
        _M8ClaimQueue[MERKABA_M8_CLAIM_TILE_OFFSET + ticket] =
            uint2(refIndex, currentRef);
    else
    {
        InterlockedCompareExchange(_M8ChunkTileRefs[refIndex], claimed,
            currentRef, previous);
        M8FlowerBinFailure(M8_OBSERVATION_FAILURE_PHYSICAL_CAPACITY);
    }
}

void M8FlowerResolveObservationOwners(int3 firstOwner, bool requestMissing,
    out uint physicalSlots[8], out uint readyOwners)
{
    uint blocks[8];
    uint chunks[8];
    uint readyBlocks = 0u;
    uint readyChunks = 0u;
    uint blockMask = M8FlowerOverlapBoundaryMask(firstOwner, 8u);
    uint chunkMask = M8FlowerOverlapBoundaryMask(firstOwner, 5u);
    uint tileMask = M8FlowerOverlapBoundaryMask(firstOwner, 3u);
    readyOwners = 0u;
    [unroll]
    for (uint init = 0u; init < 8u; init++)
    {
        blocks[init] = 0u;
        chunks[init] = 0u;
        physicalSlots[init] = 0xffffffffu;
    }
    [unroll]
    for (uint blockKey = 0u; blockKey < 8u; blockKey++)
    {
        if ((blockKey & ~blockMask) != 0u) continue;
        MerkabaM8Address address = MerkabaAddressOf(
            firstOwner + M8FlowerOverlapDelta(blockKey));
        uint index = 0u;
        uint failure = 0u;
        bool ready;
        if (requestMissing)
            ready = M8FindOrClaimBlock(address.blockCoord, index, failure);
        else
            ready = M8FindBlock(address.blockCoord, index);
        if (failure != 0u) M8FlowerBinFailure(failure);
        blocks[blockKey] = index;
        if (ready) readyBlocks |= 1u << blockKey;
    }
    [unroll]
    for (uint chunkKey = 0u; chunkKey < 8u; chunkKey++)
    {
        if ((chunkKey & ~chunkMask) != 0u) continue;
        uint blockKey = chunkKey & blockMask;
        if ((readyBlocks & (1u << blockKey)) == 0u) continue;
        MerkabaM8Address address = MerkabaAddressOf(
            firstOwner + M8FlowerOverlapDelta(chunkKey));
        uint index = 0u;
        uint failure = 0u;
        bool ready;
        if (requestMissing)
            ready = M8FindOrClaimChunk(blocks[blockKey], address.chunkLocal,
                index, failure);
        else
        {
            uint chunkRef = _M8BlockChunkRefsRead[blocks[blockKey] *
                MERKABA_M8_BLOCK_CHUNK_COUNT + address.chunkLocal];
            index = chunkRef - 1u;
            ready = chunkRef != MERKABA_REF_EMPTY &&
                index < MERKABA_M8_CHUNK_CAPACITY;
        }
        if (failure != 0u) M8FlowerBinFailure(failure);
        chunks[chunkKey] = index;
        if (ready) readyChunks |= 1u << chunkKey;
    }
    [unroll]
    for (uint tileKey = 0u; tileKey < 8u; tileKey++)
    {
        if ((tileKey & ~tileMask) != 0u) continue;
        uint chunkKey = tileKey & chunkMask;
        if ((readyChunks & (1u << chunkKey)) == 0u) continue;
        MerkabaM8Address address = MerkabaAddressOf(
            firstOwner + M8FlowerOverlapDelta(tileKey));
        uint refIndex = chunks[chunkKey] * MERKABA_M8_TILES_PER_CHUNK +
            address.tileLocal;
        uint tileRef;
        if (requestMissing) tileRef = _M8ChunkTileRefs[refIndex];
        else tileRef = _M8ChunkTileRefsRead[refIndex];
        if (M8IsHotRef(tileRef))
            physicalSlots[tileKey] = M8PhysicalSlot(tileRef);
        else if (requestMissing)
            M8FlowerRequestObservationTile(refIndex, tileRef);
    }
    [unroll]
    for (uint owner = 0u; owner < 8u; owner++)
    {
        uint slot = physicalSlots[owner & tileMask];
        physicalSlots[owner] = slot;
        if (slot != 0xffffffffu) readyOwners |= 1u << owner;
    }
}

void M8FlowerCountObservationOwners(int3 firstOwner)
{
    uint slots[8];
    uint ready;
    M8FlowerResolveObservationOwners(firstOwner, true, slots, ready);
    [unroll]
    for (uint owner = 0u; owner < 8u; owner++)
        if ((ready & (1u << owner)) != 0u)
            M8FlowerCountObservationOwner(slots[owner]);
    if (ready != 0xffu)
        M8CounterIncrement(M8_COUNTER_UNRESOLVED_SURFACE_TILES);
}

void M8FlowerEmitObservationOwners(int3 firstOwner, uint sourcePixel,
    uint symbolTag, uint precisionKey)
{
    // An allocation retry must recount the same frozen observation after
    // touched-bin retirement. Never silently drop newly resident owners.
    if (_M8Counters[M8_COUNTER_UNRESOLVED_SURFACE_TILES] != 0u) return;
    uint slots[8];
    uint ready;
    M8FlowerResolveObservationOwners(firstOwner, false, slots, ready);
    if (ready != 0xffu)
    {
        M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return;
    }
    [unroll]
    for (uint owner = 0u; owner < 8u; owner++)
    {
        int3 coordinate = firstOwner + M8FlowerOverlapDelta(owner);
        uint3 local = asuint(coordinate) & 7u;
        uint kernelLocal = local.x + 8u * (local.y + 8u * local.z);
        if (M8FlowerEmitObservationOwner(slots[owner], kernelLocal,
                sourcePixel, symbolTag, precisionKey))
        {
            // Publish endpoint support before the dual pass. The immutable
            // joint field has already accepted this endpoint; no existing
            // sheet or parent prediction may erase its support. This bit is
            // attempt scratch, not positive occupancy or a drawable seed.
            InterlockedOr(_M8TileBits[M8TileWordIndex(slots[owner],
                kernelLocal >> 5u)].z, 1u << (kernelLocal & 31u));
        }
    }
}

#endif
