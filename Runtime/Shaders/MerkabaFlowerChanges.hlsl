#ifndef GENESIS_MERKABA_FLOWER_CHANGES
#define GENESIS_MERKABA_FLOWER_CHANGES

// Pull the changed sources into each receiver once. The bitmap/queue belongs
// to this snapshot, not to FlowerDetail. Missing/COLD sources cannot have a
// prepared change; the source preflight already required every actual receiver.
[numthreads(128,1,1)]
void InvalidateFlowerPeers(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    if(group.x>=min(_M8FlowerSignalItemsRead.Load(M8_FLOWER_CHANGE_TILE_COUNT),32768u))return;
    uint slot=_M8FlowerSignalItemsRead.Load(M8_FLOWER_CHANGE_TILE_QUEUE+4u*group.x);
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    M8FlowerCacheTileHalo(slot,lane,false,true);
    [loop]for(uint local=lane;local<512u;local+=128u)
    {
        uint ownerRef=M8FlowerFindOwner(slot,local,runtime.w);
        if(ownerRef==0u)continue;
        int3 origin=int3(local&7u,(local>>3u)&7u,local>>6u);
        uint roots=0u;
        [loop]for(uint node=6u;node<26u;node++)
        {
            uint sourceSlot,sourceLocal;
            if(!M8FlowerHaloKernel(origin+M8FlowerNodeAt(node).xyz,sourceSlot,sourceLocal,false))continue;
            if(_M8TileRecords[M8TileRuntimeIndex(sourceSlot)].x!=_M8ObservationToken)continue;
            if((_M8TileBits[M8TileWordIndex(sourceSlot,sourceLocal>>5u)].w&
                (1u<<(sourceLocal&31u)))!=0u)roots|=1u<<(node-6u);
        }
        if(roots==0u)continue;
        bool changed;
        uint status=M8FlowerInvalidateDependentPhases(ownerRef,roots,false,
            _M8DualPublishingGeneration,_M8DualRetiredGeneration,changed);
        if(status!=M8_FLOWER_ARENA_OK)
        {
            // No pending transaction is created. Invalid resident data is an
            // explicit failure, not a fabricated empty/valid Flower relation.
            M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
            continue;
        }
        if(changed)
        {
            M8MarkTileDirty(slot);
            InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK],4u);
            M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
        }
    }
}

// Epochs and peer cuts have retired. This entrypoint only publishes the
// already prepared M8 values; it performs no geometry or fine allocation.
[numthreads(128,1,1)]
void PublishFlowerR1(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    uint index=128u*group.x+lane;
    if(index>=min(_M8FlowerSignalItemsRead.Load(M8_FLOWER_SIGNAL_COUNT),M8_FLOWER_SIGNAL_CAPACITY))return;
    uint address=M8FlowerSignalAddress(index);
    uint4 source=_M8FlowerSignalItemsRead.Load4(address);
    if(source.w==0u)return;
    uint slot=source.x>>9u,local=source.x&511u;
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    if(runtime.w!=source.y || runtime.x!=_M8ObservationToken)
    {M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);return;}
    KernelState before=M8LoadKernelState(slot,local);
    if(before.flags!=source.z)
    {M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);return;}
    uint4 value=_M8FlowerSignalItemsRead.Load4(address+16u);
    KernelState after=before;
    UpdateOccupancy(slot,local,after,asint(value.x)-before.evidence);
    after.evidence=asint(value.x);after.packedColor=value.y;
    after.colorConfidence=value.z;after.flags=value.w;
    M8StoreKernelState(slot,local,after);
    if(M8FlowerHasPlane(after.flags)||(after.flags&M8_FLOWER_OCCUPIED_FLAG)!=0u)
        M8MarkR1Active(slot,local);
    else M8FlowerUnmarkR1Active(slot,local);
    DeviceMemoryBarrier();
    InterlockedAnd(_M8TileBits[M8TileWordIndex(slot,local>>5u)].w,~(1u<<(local&31u)));
}

#endif
