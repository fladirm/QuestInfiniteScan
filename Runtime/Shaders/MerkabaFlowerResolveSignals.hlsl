#ifndef GENESIS_MERKABA_FLOWER_RESOLVE_SIGNALS
#define GENESIS_MERKABA_FLOWER_RESOLVE_SIGNALS

void M8FlowerResolveCarriersBody(uint3 group,uint lane)
{
    uint4 measured;
    if(_M8Counters[M8_COUNTER_OBSERVATION_COMPLETED]!=0u ||
        !M8FlowerReadMeasuredGroup(group,measured))return;
    uint slot=measured.x>>9u,local=measured.x&511u;
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    if(runtime.w!=measured.w || runtime.x!=_M8ObservationToken)return;
    uint flags=M8LoadKernelStateRead(slot,local).flags;
    if((flags&(M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID|M8_FLOWER_SEED_FLAG))!=
        (M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID))return;
    if(lane==0u)
    {
        m8FinePlane=flags;m8FineState=0u;m8FineWriteStatus=0u;m8FineAbsentNodes=0u;
        m8FineSignalFirst=0u;m8FineSignalLast=0u;m8FineSignalCount=0u;m8FineSignalFailed=0u;
    }
    GroupMemoryBarrierWithGroupSync();
    [loop]for(uint index=lane;index<measured.z;index+=128u)
    {
        M8ObservationRecord record=_M8ObservationRecordsRead[M8FlowerMeasuredRecordIndex(measured.y,index)];
        uint plane;float3 world;
        if(M8FlowerMeasurement(record.SourcePixel,M8GlobalKernelCoord(slot,local),plane,world))
            InterlockedOr(m8FineState,M8_FLOWER_FINE_ACTIVE);
    }
    GroupMemoryBarrierWithGroupSync();
    if((m8FineState&M8_FLOWER_FINE_ACTIVE)==0u)return;
    M8FlowerLoadObservedHalo(slot,lane);
    float2 errors=float2(M8FlowerNext(_M8PlaneErrorBounds.x+M8_FLOWER_NORMAL_QUANTIZATION_UPPER),
        M8FlowerNext(_M8PlaneErrorBounds.y+M8_FLOWER_OFFSET_QUANTIZATION_UPPER));
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
    [loop]for(uint word=0u;word<4u;word++)
    {
        uint pending=carriers[word];
        [loop]while(pending!=0u)
        {
            uint carrier=32u*word+(uint)firstbitlow(pending);pending&=pending-1u;
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
        if(m8FineSignalFailed==0u)
            M8FlowerPublishSignalOwner(measured.x,m8FineSignalFirst,m8FineSignalCount,runtime.w);
        else M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
    }
}

#endif
