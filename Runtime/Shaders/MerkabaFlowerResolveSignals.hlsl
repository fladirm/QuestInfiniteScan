#ifndef GENESIS_MERKABA_FLOWER_RESOLVE_SIGNALS
#define GENESIS_MERKABA_FLOWER_RESOLVE_SIGNALS

void M8FlowerResolveCarriersBody(uint3 group,uint lane)
{
    if(_M8Counters[M8_COUNTER_OBSERVATION_COMPLETED]!=0u ||
        group.x>=min(_M8Counters[M8_COUNTER_TOUCHED_TILE_COUNT],32768u))return;
    uint slot=_M8TouchedTileQueueRead[group.x];
    if(slot>=_M8ObservationHotSlotCount)return;
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    uint4 meta=_M8TileRecords[M8TileMetaIndex(slot)];
    uint4 bin=_M8ObservationTileBinsRead[slot];
    if(runtime.w==0u || runtime.x!=_M8ObservationToken ||
        meta.x>=MERKABA_M8_CHUNK_CAPACITY || meta.y>=64u ||
        _M8ChunkTileRefsRead[meta.x*64u+meta.y]!=slot+1u ||
        bin.x!=_M8ObservationToken || bin.y==0u || bin.w!=bin.y ||
        bin.z>=_M8ObservationRecordCapacity || bin.y>_M8ObservationRecordCapacity-bin.z)return;
    if((_M8Counters[M8_COUNTER_INVALIDATION_OWED]&M8_FLOWER_INVALIDATION_PHASE_MASK)!=0u)return;
    if(lane<16u)m8FineActiveOwners[lane]=0u;
    [loop]for(uint local=lane;local<512u;local+=128u)
    {
        m8FinePlane[local]=M8LoadKernelStateRead(slot,local).flags;
        m8FineState[local]=0u;
    }
    GroupMemoryBarrierWithGroupSync();
    // Only owners reached by this snapshot enter geometry acquisition.
    [loop]for(uint index=lane;index<bin.y;index+=128u)
    {
        M8ObservationRecord record=_M8ObservationRecordsRead[bin.z+index];
        if((record.TileAndKernel>>9u)!=slot)continue;
        uint local=record.TileAndKernel&511u,flags=m8FinePlane[local];
        if((flags&(M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID|M8_FLOWER_SEED_FLAG))!=
            (M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID))continue;
        uint plane;float3 world;
        if(!M8FlowerMeasurement(record.SourcePixel,M8GlobalKernelCoord(slot,local),plane,world))continue;
        InterlockedOr(m8FineState[local],M8_FLOWER_FINE_ACTIVE);
        InterlockedOr(m8FineActiveOwners[local>>5u],1u<<(local&31u));
    }
    GroupMemoryBarrierWithGroupSync();
    M8FlowerCacheTileHalo(slot,lane,false,true);
    float2 errors=float2(M8FlowerNext(_M8PlaneErrorBounds.x+M8_FLOWER_NORMAL_QUANTIZATION_UPPER),
        M8FlowerNext(_M8PlaneErrorBounds.y+M8_FLOWER_OFFSET_QUANTIZATION_UPPER));
    [loop]for(uint word=0u;word<16u;word++)
    {
        uint owners=m8FineActiveOwners[word];
        [loop]while(owners!=0u)
        {
            uint local=32u*word+(uint)firstbitlow(owners);owners&=owners-1u;
            if(lane==0u)
            {
                m8FineWriteStatus=0u;m8FineAbsentNodes=0u;
                m8FineSignalFirst=0u;m8FineSignalLast=0u;
                m8FineSignalCount=0u;m8FineSignalFailed=0u;
            }
            GroupMemoryBarrierWithGroupSync();
            M8FlowerPrepareFinePacket(slot,lane,runtime.w,local,1u,52u,0u,0u,errors);
            if(lane<26u)
            {
                M8FlowerPhaseRootEvidence a,b,raw;bool provisional;uint receipt;
                uint sa=M8FlowerPacketReadOriginal(38u*lane,a,raw,provisional,receipt);
                uint sb=M8FlowerPacketReadOriginal(38u*lane+19u,b,raw,provisional,receipt);
                if(sa==0u && sb==0u)InterlockedOr(m8FineAbsentNodes,1u<<lane);
            }
            GroupMemoryBarrierWithGroupSync();
            uint4 carriers=M8FlowerReachedCarriers(m8FineAbsentNodes);
            [loop]for(uint carrierWord=0u;carrierWord<4u;carrierWord++)
            {
                uint pending=carriers[carrierWord];
                [loop]while(pending!=0u)
                {
                    uint carrier=32u*carrierWord+(uint)firstbitlow(pending);pending&=pending-1u;
                    m8FinePacketCarrier=carrier;
                    if(lane==0u)m8FlowerHaloUnresolvedReads=0u;
                    GroupMemoryBarrierWithGroupSync();
                    M8FlowerResolveSkinCarrier(slot,lane,runtime.w,carrier,errors);
                    if(lane==0u && m8FlowerHaloUnresolvedReads!=0u)
                        M8FlowerRequestSkinDependencies(m8FlowerHaloUnresolvedReads);
                    GroupMemoryBarrierWithGroupSync();
                }
            }
            if(lane==0u)
            {
                // A failed reservation never publishes an incomplete owner.
                if(m8FineSignalFailed==0u)
                    M8FlowerPublishSignalOwner((slot<<9u)|local,m8FineSignalFirst,
                        m8FineSignalCount,runtime.w);
                else M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
            }
            DeviceMemoryBarrierWithGroupSync();
        }
    }
}

#endif
