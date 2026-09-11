#ifndef GENESIS_MERKABA_FLOWER_PAGE_INVALIDATION_INCLUDED
#define GENESIS_MERKABA_FLOWER_PAGE_INVALIDATION_INCLUDED

#include "MerkabaFlowerTileHalo.hlsl"
#define M8_FLOWER_PAGE_WRITE
#include "MerkabaFlowerPages.hlsl"
#include "MerkabaFlowerOwnerCache.hlsl"

bool M8FlowerPageSourceReady(uint generation)
{
    if (generation != 0u && generation <= M8_WORLD_MAX_GENERATION) return true;
    uint ignored;
    InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_FAILURE],
        M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY, ignored);
    return false;
}

// Derived-page invalidation only. Reuse the existing signed address/halo
// lookup and the same backing view as the caller's storage mutations.
// The retired/LOADING centre itself is invalidated before slot reuse; only
// currently HOT, generation- and logical-identity-valid neighbours are added.
void M8FlowerMarkTileNeighborhoodDirty(uint slot, uint generation,
    bool tileMetadataWriteView = false, bool chunkRefsWriteView = false,
    bool invalidateOwnerCache = true)
{
    if (slot >= MERKABA_M8_PHYSICAL_TILE_CAPACITY || generation == 0u) return;
    uint4 meta = tileMetadataWriteView ? _M8TileRecords[M8TileMetaIndex(slot)] :
        M8LoadTileMetaRead(slot);
    uint slotGeneration = tileMetadataWriteView ? _M8TileRecords[M8TileRuntimeIndex(slot)].w :
        M8LoadTileRuntimeRead(slot).w;
    if (meta.x >= MERKABA_M8_CHUNK_CAPACITY || meta.y >= MERKABA_M8_TILES_PER_CHUNK ||
        slotGeneration == 0u || slotGeneration > M8_FLOWER_HALO_SLOT_MASK) return;
    M8FlowerMarkPageDirty(slot, generation);
    if(invalidateOwnerCache)M8FlowerInvalidateCachedTile(slot,generation);
    int3 tile = (tileMetadataWriteView ? M8GlobalKernelCoord(slot, 0u) :
        M8GlobalKernelCoordRead(slot, 0u)) >> 3;
    [loop] for (uint index = 0u; index < M8_FLOWER_HALO_COUNT; ++index)
    {
        if (index == 13u) continue;
        int3 neighbourTile = tile + M8FlowerHaloDelta(index);
        uint packed = M8FlowerResolveHaloRef(neighbourTile, tileMetadataWriteView,
            chunkRefsWriteView);
        if ((packed >> 30u) != M8_FLOWER_HALO_HOT) continue;
        uint neighbour = packed & M8_FLOWER_HALO_SLOT_MASK;
        uint expectedGeneration = (packed >> 15u) & M8_FLOWER_HALO_SLOT_MASK;
        uint4 neighbourMeta = tileMetadataWriteView ? _M8TileRecords[M8TileMetaIndex(neighbour)] :
            M8LoadTileMetaRead(neighbour);
        uint actualGeneration = tileMetadataWriteView ? _M8TileRecords[M8TileRuntimeIndex(neighbour)].w :
            M8LoadTileRuntimeRead(neighbour).w;
        if (actualGeneration != expectedGeneration || neighbourMeta.x >= MERKABA_M8_CHUNK_CAPACITY ||
            neighbourMeta.y >= MERKABA_M8_TILES_PER_CHUNK) continue;
        uint referenceIndex = neighbourMeta.x * MERKABA_M8_TILES_PER_CHUNK + neighbourMeta.y;
        uint reference = chunkRefsWriteView ? _M8ChunkTileRefs[referenceIndex] :
            _M8ChunkTileRefsRead[referenceIndex];
        if (reference != neighbour + 1u) continue;
        int3 actualTile = (tileMetadataWriteView ? M8GlobalKernelCoord(neighbour, 0u) :
            M8GlobalKernelCoordRead(neighbour, 0u)) >> 3;
        if (any(actualTile != neighbourTile)) continue;
        M8FlowerMarkPageDirty(neighbour, generation);
        if(invalidateOwnerCache)M8FlowerInvalidateCachedTile(neighbour,generation);
    }
}

// Ancestor restore/reclaim has no single tile address. Visit only the fixed
// existing descendants of that changed node, never all physical HOT slots.
void M8FlowerMarkResidentChunkNeighborhoodDirty(uint chunk, uint generation,
    bool tileMetadataWriteView = false, bool chunkRefsWriteView = false)
{
    if (chunk >= MERKABA_M8_CHUNK_CAPACITY) return;
    [loop] for (uint tile = 0u; tile < MERKABA_M8_TILES_PER_CHUNK; ++tile)
    {
        uint referenceIndex = chunk * MERKABA_M8_TILES_PER_CHUNK + tile;
        uint reference = chunkRefsWriteView ? _M8ChunkTileRefs[referenceIndex] :
            _M8ChunkTileRefsRead[referenceIndex];
        if (!M8IsHotRef(reference)) continue;
        uint slot = M8PhysicalSlot(reference);
        uint4 meta = tileMetadataWriteView ? _M8TileRecords[M8TileMetaIndex(slot)] :
            M8LoadTileMetaRead(slot);
        if (meta.x == chunk && meta.y == tile)
            M8FlowerMarkTileNeighborhoodDirty(slot, generation,
                tileMetadataWriteView, chunkRefsWriteView);
    }
}

void M8FlowerMarkResidentBlockNeighborhoodDirty(uint block, uint generation,
    bool tileMetadataWriteView = false, bool chunkRefsWriteView = false)
{
    if (block >= MERKABA_M8_BLOCK_CAPACITY) return;
    [loop] for (uint child = 0u; child < MERKABA_M8_BLOCK_CHUNK_COUNT; ++child)
    {
        uint reference = _M8BlockChunkRefsRead[block * MERKABA_M8_BLOCK_CHUNK_COUNT + child];
        if (reference == 0u || reference - 1u >= MERKABA_M8_CHUNK_CAPACITY) continue;
        uint chunk = reference - 1u;
        if (all(M8LoadChunkOwnerRead(chunk) == uint2(block, child)))
            M8FlowerMarkResidentChunkNeighborhoodDirty(chunk, generation,
                tileMetadataWriteView, chunkRefsWriteView);
    }
}

#endif
