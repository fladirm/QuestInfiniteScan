#ifndef GENESIS_MERKABA_FLOWER_OBSERVED_OWNERS
#define GENESIS_MERKABA_FLOWER_OBSERVED_OWNERS

// One pass over a touched bin groups immutable source record indices. The
// histogram/prefix is tile-local; the output contains only observed owners.
groupshared uint m8ObservedCounts[512];
groupshared uint2 m8ObservedPrefix[512];
groupshared uint m8ObservedMask[16];
groupshared uint m8ObservedBase;
groupshared uint m8ObservedCount;
groupshared uint m8ObservedInvalid;

[numthreads(128,1,1)]
void PrepareFlowerOwners(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    if(group.x>=min(_M8Counters[M8_COUNTER_TOUCHED_TILE_COUNT],32768u))return;
    uint slot=_M8TouchedTileQueueRead[group.x];
    if(slot>=_M8ObservationHotSlotCount)return;
    uint tileAddress=M8_FLOWER_MEASURED_TILES_BASE+slot*M8_FLOWER_MEASURED_TILE_BYTES;
    if(lane==0u)_M8FlowerSignalItems.Store4(tileAddress,uint4(0u,0u,0u,0u));
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    uint4 bin=_M8ObservationTileBinsRead[slot];
    if(runtime.w==0u || bin.x!=_M8ObservationToken || bin.y==0u || bin.w!=bin.y ||
        bin.z>=min(_M8ObservationRecordCapacity,M8_FLOWER_RECORD_INDEX_CAPACITY) ||
        bin.y>min(_M8ObservationRecordCapacity,M8_FLOWER_RECORD_INDEX_CAPACITY)-bin.z)return;
    // Resolve the canonical 27 addresses once per touched tile, not once
    // per owner in each following level. No installs interleave these stages.
    M8FlowerCacheTileHalo(slot,lane,true,true);
    if(lane==0u)m8ObservedInvalid=0u;
    if(lane<16u)m8ObservedMask[lane]=0u;
    [unroll]for(uint local=lane;local<512u;local+=128u)m8ObservedCounts[local]=0u;
    GroupMemoryBarrierWithGroupSync();
    [loop]for(uint index=lane;index<bin.y;index+=128u)
    {
        uint packed=_M8ObservationRecordsRead[bin.z+index].TileAndKernel;
        if((packed>>9u)!=slot)InterlockedOr(m8ObservedInvalid,1u);
        else InterlockedAdd(m8ObservedCounts[packed&511u],1u);
    }
    GroupMemoryBarrierWithGroupSync();
    if(m8ObservedInvalid!=0u)
    {
        if(lane==0u)M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return;
    }
    [unroll]for(uint local=lane;local<512u;local+=128u)
    {
        uint count=m8ObservedCounts[local];
        m8ObservedPrefix[local]=uint2(count,count!=0u?1u:0u);
        if(count!=0u)InterlockedOr(m8ObservedMask[local>>5u],1u<<(local&31u));
    }
    GroupMemoryBarrierWithGroupSync();
    // Exclusive scan of (record count, owner count). No serial 512-item lane.
    [loop]for(uint stride=1u;stride<512u;stride*=2u)
    {
        [loop]for(uint pair=lane;pair<512u/(2u*stride);pair+=128u)
        {
            uint end=(pair+1u)*2u*stride-1u;
            m8ObservedPrefix[end]+=m8ObservedPrefix[end-stride];
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if(lane==0u)
    {
        m8ObservedCount=m8ObservedPrefix[511u].y;
        m8ObservedPrefix[511u]=0u;
        _M8FlowerSignalItems.InterlockedAdd(M8_FLOWER_MEASURED_OWNER_COUNT,
            m8ObservedCount,m8ObservedBase);
        if(m8ObservedBase>=M8_FLOWER_SIGNAL_CAPACITY ||
            m8ObservedCount>M8_FLOWER_SIGNAL_CAPACITY-m8ObservedBase)
        {
            m8ObservedInvalid=1u;
            M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
        }
    }
    GroupMemoryBarrierWithGroupSync();
    [loop]for(uint stride=256u;stride!=0u;stride/=2u)
    {
        [loop]for(uint pair=lane;pair<512u/(2u*stride);pair+=128u)
        {
            uint end=(pair+1u)*2u*stride-1u;
            uint2 left=m8ObservedPrefix[end-stride];
            m8ObservedPrefix[end-stride]=m8ObservedPrefix[end];
            m8ObservedPrefix[end]+=left;
        }
        GroupMemoryBarrierWithGroupSync();
    }
    [unroll]for(uint local=lane;local<512u;local+=128u)
    {
        uint2 prefix=m8ObservedPrefix[local];
        uint count=m8ObservedCounts[local],ordinal=m8ObservedBase+prefix.y;
        if(count!=0u && ordinal<M8_FLOWER_SIGNAL_CAPACITY)
            _M8FlowerSignalItems.Store4(M8_FLOWER_MEASURED_OWNERS_BASE+16u*ordinal,
                m8ObservedInvalid!=0u?uint4(0u,0u,0u,0u):
                uint4((slot<<9u)|local,bin.z+prefix.x,count,runtime.w));
        m8ObservedCounts[local]=0u;
    }
    if(lane<16u && m8ObservedInvalid==0u)
        _M8FlowerSignalItems.Store2(tileAddress+16u+8u*lane,
            uint2(m8ObservedMask[lane],m8ObservedBase+m8ObservedPrefix[32u*lane].y));
    GroupMemoryBarrierWithGroupSync();
    if(m8ObservedInvalid==0u)
    {
        [loop]for(uint index=lane;index<bin.y;index+=128u)
        {
            uint local=_M8ObservationRecordsRead[bin.z+index].TileAndKernel&511u,ordinal;
            InterlockedAdd(m8ObservedCounts[local],1u,ordinal);
            uint destination=bin.z+m8ObservedPrefix[local].x+ordinal;
            _M8FlowerSignalItems.Store(M8_FLOWER_RECORD_INDEX_BASE+4u*destination,bin.z+index);
        }
    }
    DeviceMemoryBarrierWithGroupSync();
    if(lane==0u)
    {
        if(m8ObservedInvalid==0u)_M8FlowerSignalItems.Store4(tileAddress,
            uint4(_M8ObservationToken,runtime.w,m8ObservedBase,m8ObservedCount));
        uint count=min(m8ObservedBase+m8ObservedCount,M8_FLOWER_SIGNAL_CAPACITY),ignored;
        _M8FlowerSignalItems.InterlockedMax(M8_FLOWER_MEASURED_OWNER_DISPATCH,min(count,65535u),ignored);
        _M8FlowerSignalItems.InterlockedMax(M8_FLOWER_MEASURED_OWNER_DISPATCH+4u,(count+65534u)/65535u,ignored);
    }
}

#endif
