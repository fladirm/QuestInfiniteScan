#ifndef GENESIS_MERKABA_FLOWER_CHANGES
#define GENESIS_MERKABA_FLOWER_CHANGES

bool M8FlowerReadPreparedR1(uint index,bool kernelWriteView,
    out uint address,out uint4 source,out KernelState before)
{
    address=0u;source=0u.xxxx;before=(KernelState)0;
    if(index>=min(_M8FlowerSignalItemsRead.Load(M8_FLOWER_SIGNAL_COUNT),M8_FLOWER_R1_CHANGE_CAPACITY))return false;
    address=M8FlowerR1ChangeAddress(index);
    source=_M8FlowerSignalItemsRead.Load4(address);
    if((source.w&M8_FLOWER_R1_PREPARED)==0u)return false;
    uint slot=source.x>>9u,local=source.x&511u;
    if(slot>=32768u)return M8FlowerR1CutAccepted(M8_FLOWER_ARENA_INVALID);
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    if(kernelWriteView)before=M8LoadKernelState(slot,local);
    else before=M8LoadKernelStateRead(slot,local);
    if(runtime.w!=source.y || runtime.x!=_M8ObservationToken || before.flags!=source.z)
        return M8FlowerR1CutAccepted(M8_FLOWER_ARENA_INVALID);
    return true;
}

// The global preflight barrier has retired every source/peer read. This
// pass writes fine state only; the M8 publisher needs four separate banks.
// Combining their writes would require nine writable bindings on Quest.
[numthreads(128,1,1)]
void InvalidateFlowerSources(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    uint address;uint4 source;KernelState before;
    if(!M8FlowerReadPreparedR1(128u*group.x+lane,false,address,source,before))return;
    uint slot=source.x>>9u,local=source.x&511u,status=M8_FLOWER_ARENA_OK;
    if((source.w&M8_FLOWER_R1_STRUCTURAL)!=0u)
        status=M8FlowerInvalidateOwner(slot,local,source.y,
            _M8WorldPublishingGeneration,_M8WorldRetiredGeneration);
    if(!M8FlowerR1CutAccepted(status))
    {
        _M8FlowerSignalItems.Store(address+12u,0u);
        InterlockedAnd(_M8TileBits[M8TileWordIndex(slot,local>>5u)].w,~(1u<<(local&31u)));
    }
}

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
            if(!M8FlowerHaloKernel(origin+M8FlowerNodeAt(node).xyz,sourceSlot,sourceLocal,true))continue;
            if(_M8TileRecords[M8TileRuntimeIndex(sourceSlot)].x!=_M8ObservationToken)continue;
            if((_M8TileBits[M8TileWordIndex(sourceSlot,sourceLocal>>5u)].w&
                (1u<<(sourceLocal&31u)))!=0u)roots|=1u<<(node-6u);
        }
        if(roots==0u)continue;
        bool changed;
        uint status=M8FlowerInvalidateDependentPhases(ownerRef,roots,false,
            _M8WorldPublishingGeneration,_M8WorldRetiredGeneration,changed);
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

// Source epochs and peer cuts have retired. Publish only the prepared M8
// values; no fine allocation or geometry is recomputed at this boundary.
[numthreads(128,1,1)]
void PublishFlowerR1(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    uint address;uint4 source;KernelState before;
    if(!M8FlowerReadPreparedR1(128u*group.x+lane,true,address,source,before))return;
    uint slot=source.x>>9u,local=source.x&511u;
    uint4 value=_M8FlowerSignalItemsRead.Load4(address+16u);
    KernelState after=before;
    UpdateOccupancy(slot,local,after,asint(value.x)-before.evidence);
    after.evidence=asint(value.x);after.packedColor=value.y;
    after.colorConfidence=value.z;after.flags=value.w;
    M8StoreKernelState(slot,local,after);
    if(M8FlowerHasPlane(after.flags)||(after.flags&M8_FLOWER_OCCUPIED_FLAG)!=0u)
        M8MarkR1Active(slot,local);
    else M8FlowerUnmarkR1Active(slot,local);
    if(all(value==0u))
        InterlockedAnd(_M8TileBits[M8TileWordIndex(slot,local>>5u)].z,~(1u<<(local&31u)));
    DeviceMemoryBarrier();
    InterlockedAnd(_M8TileBits[M8TileWordIndex(slot,local>>5u)].w,~(1u<<(local&31u)));
}

#endif
