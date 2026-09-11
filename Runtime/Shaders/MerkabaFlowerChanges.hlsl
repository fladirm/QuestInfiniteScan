#ifndef GENESIS_MERKABA_FLOWER_CHANGES
#define GENESIS_MERKABA_FLOWER_CHANGES

bool M8FlowerReadPreparedR1(uint index,bool kernelWriteView,
    out uint address,out uint4 source,out KernelState before)
{
    address=0u;source=0u.xxxx;before=(KernelState)0;
    if(_M8Counters[M8_COUNTER_OBSERVATION_FAILURE]!=0u)return false;
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

// Preflight completed before this dispatch and the serialized job owns the
// same fine-reader lease. No allocation/owner install occurs between them.
// Each receiver has one writer: a local epoch cut OR a selective peer cut.
// Peer requests read only the immutable source bitmap, never a peer's mutable
// epoch/span. Therefore source -> peer was not a global dependency. The next
// M8 publication barrier remains: four writable M8 banks cannot share this
// fine-state writer's descriptor set on Quest.
[numthreads(128,1,1)]
void PublishStructuralChanges(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    if(group.x>=min(_M8FlowerSignalItems.Load(M8_FLOWER_CHANGE_TILE_COUNT),32768u))return;
    uint slot=_M8FlowerSignalItems.Load(M8_FLOWER_CHANGE_TILE_QUEUE+4u*group.x);
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    M8FlowerCacheTileHalo(slot,lane,true,true);
    M8FlowerPrepareObservedOwners(slot,lane);
    DeviceMemoryBarrierWithGroupSync();
    [loop]for(uint local=lane;local<512u;local+=128u)
    {
        uint ownerRef=M8FlowerFindOwner(slot,local,runtime.w);
        int3 origin=int3(local&7u,(local>>3u)&7u,local>>6u);
        uint roots=0u;bool drawChanged=false;
        [loop]for(uint node=0u;node<26u;node++)
        {
            uint sourceSlot,sourceLocal;
            if(!M8FlowerHaloKernel(origin+M8FlowerNodeAt(node).xyz,sourceSlot,sourceLocal,true))continue;
            if(_M8TileRecords[M8TileRuntimeIndex(sourceSlot)].x!=_M8ObservationToken)continue;
            if((_M8TileBits[M8TileWordIndex(sourceSlot,sourceLocal>>5u)].w&
                (1u<<(sourceLocal&31u)))!=0u)
            {
                drawChanged=true;
                if(node>=6u)roots|=1u<<(node-6u);
            }
        }
        bool ownCut=runtime.x==_M8ObservationToken &&
            (_M8TileBits[M8TileWordIndex(slot,local>>5u)].w&(1u<<(local&31u)))!=0u;
        if(drawChanged || ownCut)M8FlowerInvalidateCachedOwner(slot,local);
        if((!ownCut && roots==0u) || ownerRef==0u)continue;
        bool changed=false;
        uint status;
        if(ownCut)
            status=M8FlowerInvalidateOwner(slot,local,runtime.w,
                _M8WorldPublishingGeneration,_M8WorldRetiredGeneration);
        else
            status=M8FlowerInvalidateDependentPhases(ownerRef,roots,false,
                _M8WorldPublishingGeneration,_M8WorldRetiredGeneration,changed);
        if(status!=M8_FLOWER_ARENA_OK)
        {
            // Preflight has established range/epoch/lease validity for every
            // writer. An unexpected violation is fatal; no prepared R1 is
            // published afterward, and no cross-snapshot retry is invented.
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

void M8FlowerInvalidatePreparedDrawPeers(uint slot,uint local)
{
    int3 tile=M8GlobalKernelCoord(slot,0u)>>3;
    int3 origin=int3(local&7u,(local>>3u)&7u,local>>6u);
    [loop]for(uint node=0u;node<26u;node++)
    {
        int3 relative=origin+M8FlowerNodeAt(node).xyz,delta=relative>>3;
        uint3 index=uint3(delta+1);
        uint packed=_M8TileHaloRead[slot*M8_FLOWER_HALO_COUNT+index.x+3u*(index.y+3u*index.z)];
        uint peerSlot,peerLocal;
        if(M8FlowerValidateHaloKernel(packed,tile+delta,relative,peerSlot,peerLocal,true))
            M8FlowerInvalidateCachedOwner(peerSlot,peerLocal);
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
    if(after.flags!=before.flags)
    {
        M8FlowerInvalidateCachedOwner(slot,local);
        // Compatible plane refinement can move a shared root without an
        // epoch cut. Its incident cached peers still have to be replaced.
        M8FlowerInvalidatePreparedDrawPeers(slot,local);
    }
    if(all(value==0u))M8FlowerDropCachedOwner(slot,local);
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
