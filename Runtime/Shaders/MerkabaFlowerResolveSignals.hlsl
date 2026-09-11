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
        m8FineDrawMask=0u;m8FineDrawComplete=1u;m8FineDrawAddress=0u;
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
    M8FlowerBeginFinePacket(slot,lane,runtime.w,local,errors);
    // R1 -> reached R2 -> reached R3 -> actual carriers. The single acquisition
    // call serves both source selection and the selected knots' phase ancestry.
    // No camera-independent 52-root evaluation or per-owner candidate program.
    [loop]for(uint shell=0u;shell<4u;shell++)
    {
        uint4 reached=M8FlowerReachedCarriers(m8FineAbsentNodes);
        uint4 work=shell<3u?uint4(1u,0u,0u,0u):reached;
        [loop]for(uint word=0u;word<4u;word++)
        {
            uint pending=work[word];
            [loop]while(pending!=0u)
            {
                uint carrier=32u*word+(uint)firstbitlow(pending);pending&=pending-1u;
                m8FinePacketCarrier=carrier;
                if(lane==0u)
                {
                    m8FlowerHaloUnresolvedReads=0u;m8FineOriginalRequired=0u;
                    m8FineSourceNodes=shell==0u?63u:
                        shell==3u?M8FlowerDecodeCarrierPetalsAt(carrier).z:0u;
                }
                GroupMemoryBarrierWithGroupSync();
                if(shell==1u || shell==2u)
                {
                    if((reached[lane>>5u]&(1u<<(lane&31u)))!=0u)
                        InterlockedOr(m8FineSourceNodes,M8FlowerDecodeCarrierPetalsAt(lane).z&
                            (shell==1u?0x0003ffc0u:0x03fc0000u));
                }
                GroupMemoryBarrierWithGroupSync();
                if(lane<52u && (m8FineSourceNodes&(1u<<(lane>>1u)))!=0u)
                    M8FlowerRequireFineOriginal(lane);
                if(shell==3u && lane<14u)M8FlowerRequireFineSite(lane>>1u,(lane&1u)!=0u);
                M8FlowerAcquireFineOriginals(lane);
                if(shell<3u)
                {
                    if(lane<26u && (m8FineSourceNodes&(1u<<lane))!=0u)
                    {
                        M8FlowerPhaseRootEvidence a,b,raw;bool provisional;uint receipt;
                        uint sa=M8FlowerPacketReadOriginal(38u*lane,a,raw,provisional,receipt);
                        uint sb=M8FlowerPacketReadOriginal(38u*lane+19u,b,raw,provisional,receipt);
                        if(sa==0u && sb==0u)InterlockedOr(m8FineAbsentNodes,1u<<lane);
                    }
                }
                else
                {
                    M8FlowerResolveSkinCarrier(measured,lane,carrier,errors);
                }
                GroupMemoryBarrierWithGroupSync();
                if(lane==0u && m8FlowerHaloUnresolvedReads!=0u)
                {
                    m8FineDrawComplete=0u;
                    M8FlowerRequestSkinDependencies(m8FlowerHaloUnresolvedReads);
                }
                GroupMemoryBarrierWithGroupSync();
            }
        }
    }
    if(lane==0u)
    {
        if(m8FineWriteStatus!=0u)M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        if(m8FineSignalFailed==0u && m8FineWriteStatus==0u)
            M8FlowerPublishSignalOwner(measured.x,m8FineSignalFirst,m8FineSignalCount,runtime.w);
        else M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
        if(m8FineDrawComplete!=0u && m8FineWriteStatus==0u)
            m8FineDrawAddress=M8FlowerBeginOwnerSnapshot(slot,local,runtime.w,flags,
                M8FlowerGetOwnerEpoch(m8FineOwner),errors,m8FineDrawMask);
    }
    GroupMemoryBarrierWithGroupSync();
    if(m8FineDrawAddress!=0u && (m8FineDrawMask[lane>>5u]&(1u<<(lane&31u)))!=0u)
    {
        uint rank=0u;
        [unroll]for(uint word=0u;word<4u;word++)
            rank+=countbits(m8FineDrawMask[word]&(word<(lane>>5u)?0xffffffffu:
                word==(lane>>5u)?((1u<<(lane&31u))-1u):0u));
        _M8FlowerOwnerCache.Store4(m8FineDrawAddress+64u+16u*rank,m8FineDrawSymbols[lane]);
    }
    DeviceMemoryBarrierWithGroupSync();
    if(lane==0u)M8FlowerFinishOwnerSnapshot(slot,local,m8FineDrawAddress);
}

#endif
