#ifndef GENESIS_MERKABA_FLOWER_TILE_HALO_INCLUDED
#define GENESIS_MERKABA_FLOWER_TILE_HALO_INCLUDED

#include "MerkabaWorld.hlsl"
#include "MerkabaSphereFlower.generated.hlsl"

#define M8_FLOWER_HALO_COUNT 27u
#define M8_FLOWER_HALO_MISSING 0u
#define M8_FLOWER_HALO_COLD 1u
#define M8_FLOWER_HALO_HOT 2u
#define M8_FLOWER_HALO_INVALID 3u
#define M8_FLOWER_HALO_SLOT_MASK 0x7fffu

StructuredBuffer<uint> _M8TileHaloRead M8_FLOWER_SRV(t11);
#if !defined(M8_FLOWER_HALO_READ_ONLY)
RWStructuredBuffer<uint> _M8TileHalo;
groupshared uint m8FlowerHalo[M8_FLOWER_HALO_COUNT];
groupshared int3 m8FlowerHaloTile;
// Requested unresolved endpoints, not every COLD entry in the cached halo.
// The observation drain consumes this mask after its collective barrier.
groupshared uint m8FlowerHaloUnresolvedReads;
#endif

int3 M8FlowerHaloDelta(uint index)
{
    return int3(index % 3u, (index / 3u) % 3u, index / 9u) - 1;
}

uint M8FlowerResolveHaloRef(int3 tile, bool tileMetadataWriteView = false,
    bool chunkRefsWriteView = false)
{
    // Tile coordinates have 29 signed bits. Reject an out-of-world neighbour
    // before multiplying by eight; signed overflow cannot wrap the halo.
    if (any(tile < -268435456) || any(tile > 268435455))
        return M8_FLOWER_HALO_INVALID << 30u;
    MerkabaM8Address address = MerkabaAddressOf(tile * 8);
    uint block;
    if (!M8FindBlock(address.blockCoord, block))
        return M8_FLOWER_HALO_MISSING << 30u;
    uint chunkRef = _M8BlockChunkRefsRead[block *
        MERKABA_M8_BLOCK_CHUNK_COUNT + address.chunkLocal];
    if (chunkRef == MERKABA_REF_EMPTY)
        return M8_FLOWER_HALO_MISSING << 30u;
    if (chunkRef - 1u >= MERKABA_M8_CHUNK_CAPACITY)
        return M8_FLOWER_HALO_INVALID << 30u;
    uint tileRefIndex = (chunkRef - 1u) * MERKABA_M8_TILES_PER_CHUNK + address.tileLocal;
    uint tileRef = chunkRefsWriteView ? _M8ChunkTileRefs[tileRefIndex] :
        _M8ChunkTileRefsRead[tileRefIndex];
    if (tileRef == MERKABA_REF_EMPTY)
        return M8_FLOWER_HALO_MISSING << 30u;
    if (!M8IsHotRef(tileRef))
        return M8_FLOWER_HALO_COLD << 30u;
    uint slot = M8PhysicalSlot(tileRef);
    uint generation = tileMetadataWriteView ? _M8TileRecords[M8TileRuntimeIndex(slot)].w :
        M8LoadTileRuntimeRead(slot).w;
    if (generation == 0u || generation > M8_FLOWER_HALO_SLOT_MASK)
        return M8_FLOWER_HALO_INVALID << 30u;
    return slot | (generation << 15u) | (M8_FLOWER_HALO_HOT << 30u);
}

#if !defined(M8_FLOWER_HALO_READ_ONLY)
// All lanes call this once at entry to FlowerCommit/page compilation. The
// 27 canonical lookups are cooperative and outside the kernel/root loops.
// No residency install/eviction can interleave this serialized GPU job.
void M8FlowerCacheTileHalo(uint ownerSlot, uint lane, bool publish = true,
    bool tileMetadataWriteView = false)
{
    if (lane == 0u)
    {
        m8FlowerHaloUnresolvedReads = 0u;
        m8FlowerHaloTile = (tileMetadataWriteView ?
            M8GlobalKernelCoord(ownerSlot, 0u) :
            M8GlobalKernelCoordRead(ownerSlot, 0u)) >> 3;
    }
    GroupMemoryBarrierWithGroupSync();
    if (lane < M8_FLOWER_HALO_COUNT)
    {
        uint packed = M8FlowerResolveHaloRef(
            m8FlowerHaloTile + M8FlowerHaloDelta(lane),tileMetadataWriteView);
        m8FlowerHalo[lane] = packed;
        if (publish) _M8TileHalo[ownerSlot * M8_FLOWER_HALO_COUNT + lane] = packed;
    }
    GroupMemoryBarrierWithGroupSync();
}
#endif

bool M8FlowerValidateHaloKernel(uint packed,int3 expectedTile,int3 relativeKernel,out uint slot,
    out uint kernelLocal, bool tileMetadataWriteView = false)
{
    slot = 0u;
    kernelLocal = 0u;
    if ((packed >> 30u) != M8_FLOWER_HALO_HOT) return false;
    slot = packed & M8_FLOWER_HALO_SLOT_MASK;
    uint generation = (packed >> 15u) & M8_FLOWER_HALO_SLOT_MASK;
    if (generation == 0u) return false;
    uint runtimeGeneration = tileMetadataWriteView ? _M8TileRecords[M8TileRuntimeIndex(slot)].w :
        M8LoadTileRuntimeRead(slot).w;
    if (runtimeGeneration != generation) return false;
    uint4 meta = tileMetadataWriteView ? _M8TileRecords[M8TileMetaIndex(slot)] :
        M8LoadTileMetaRead(slot);
    if (meta.x >= MERKABA_M8_CHUNK_CAPACITY ||
        meta.y >= MERKABA_M8_TILES_PER_CHUNK) return false;
    if (_M8ChunkTileRefsRead[meta.x * MERKABA_M8_TILES_PER_CHUNK + meta.y]
        != slot + 1u) return false;
    int3 actualTile = (tileMetadataWriteView ? M8GlobalKernelCoord(slot,0u) :
        M8GlobalKernelCoordRead(slot,0u)) >> 3;
    if (any(actualTile != expectedTile)) return false;
    uint3 local = asuint(relativeKernel) & 7u;
    kernelLocal = local.x + 8u * (local.y + 8u * local.z);
    return true;
}

#if !defined(M8_FLOWER_HALO_READ_ONLY)
bool M8FlowerHaloKernel(int3 relativeKernel,out uint slot,
    out uint kernelLocal,bool tileMetadataWriteView = false)
{
    slot=kernelLocal=0u;
    int3 delta=relativeKernel>>3;
    if(any(delta < -1) || any(delta > 1))return false;
    uint3 index=uint3(delta+1);
    uint packed=m8FlowerHalo[index.x+3u*(index.y+3u*index.z)];
    return M8FlowerValidateHaloKernel(packed,m8FlowerHaloTile+delta,
        relativeKernel,slot,kernelLocal,tileMetadataWriteView);
}
#endif

// One read-only endpoint path for page admission and indexed vertex synthesis.
// The queue lease protects the published 27 references. Slot generation AND
// logical identity are checked before a kernel load; no hash is traversed in
// the geometry loop. Missing is resolved absence, COLD/stale is unresolved.
uint M8FlowerEndpointGeneration(uint slot)
{
#if defined(M8_FLOWER_ENDPOINT_TILE_WRITE_VIEW)
    return _M8TileRecords[M8TileRuntimeIndex(slot)].w;
#else
    return M8LoadTileRuntimeRead(slot).w;
#endif
}

int3 M8FlowerEndpointOwner(uint slot,uint kernelLocal)
{
#if defined(M8_FLOWER_ENDPOINT_TILE_WRITE_VIEW)
    return M8GlobalKernelCoord(slot,kernelLocal);
#else
    return M8GlobalKernelCoordRead(slot,kernelLocal);
#endif
}

uint M8FlowerReadEndpoint(uint ownerSlot,int3 owner,int3 coordinate,
    out uint slot,out uint kernelLocal,out KernelState state)
{
    slot=kernelLocal=0u;state=(KernelState)0;
    if(ownerSlot>=MERKABA_M8_PHYSICAL_TILE_CAPACITY)return 2u;
    int3 ownerTile=owner>>3,targetTile=coordinate>>3;
    int3 delta=targetTile-ownerTile;
    if(any(delta < -1) || any(delta > 1))return 2u;
    uint3 index=uint3(delta+1);
    uint halo=index.x+3u*(index.y+3u*index.z);
    uint packed,origin,sourceHalo=13u;
#if defined(M8_FLOWER_HALO_READ_ONLY)
    origin=_M8TileHaloRead[ownerSlot*M8_FLOWER_HALO_COUNT+13u];
    packed=_M8TileHaloRead[ownerSlot*M8_FLOWER_HALO_COUNT+halo];
#else
    // Coverage can evaluate a neighbouring owner inside this same frozen
    // 27-tile lease. Both references are indexed in the cached page chart,
    // never in an unpublished neighbour halo or a newly resolved hash lookup.
    int3 sourceDelta=ownerTile-m8FlowerHaloTile;
    int3 targetDelta=targetTile-m8FlowerHaloTile;
    if(any(sourceDelta < -1) || any(sourceDelta > 1) ||
        any(targetDelta < -1) || any(targetDelta > 1))return 2u;
    uint3 sourceIndex=uint3(sourceDelta+1),targetIndex=uint3(targetDelta+1);
    sourceHalo=sourceIndex.x+3u*(sourceIndex.y+3u*sourceIndex.z);
    halo=targetIndex.x+3u*(targetIndex.y+3u*targetIndex.z);
    origin=m8FlowerHalo[sourceHalo];packed=m8FlowerHalo[halo];
#endif
    uint sourceSlot,sourceLocal;
#if defined(M8_FLOWER_ENDPOINT_TILE_WRITE_VIEW)
    const bool tileWriteView=true;
#else
    const bool tileWriteView=false;
#endif
    if(!M8FlowerValidateHaloKernel(origin,ownerTile,owner&7,
        sourceSlot,sourceLocal,tileWriteView) || sourceSlot!=ownerSlot)
    {
#if !defined(M8_FLOWER_HALO_READ_ONLY)
        InterlockedOr(m8FlowerHaloUnresolvedReads,1u<<sourceHalo);
#endif
        return 2u;
    }
    if((packed>>30u)==M8_FLOWER_HALO_MISSING)return 0u;
    if(!M8FlowerValidateHaloKernel(packed,targetTile,coordinate&7,
        slot,kernelLocal,tileWriteView))
    {
#if !defined(M8_FLOWER_HALO_READ_ONLY)
        InterlockedOr(m8FlowerHaloUnresolvedReads,1u<<halo);
#endif
        return 2u;
    }
    state=M8LoadKernelStateRead(slot,kernelLocal);
    uint required=M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID;
    return (state.flags&(required|M8_FLOWER_SEED_FLAG))==required?1u:0u;
}

#endif
