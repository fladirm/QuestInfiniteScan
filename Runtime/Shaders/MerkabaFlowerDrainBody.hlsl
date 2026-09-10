// One observed owner per workgroup, with fixed ancestry barriers.
void M8FlowerDrainGeometryBody(uint3 group,uint lane,uint stage)
{
    uint4 measured;
    if(_M8Counters[M8_COUNTER_OBSERVATION_COMPLETED]!=0u ||
        !M8FlowerReadMeasuredGroup(group,measured))return;
    uint slot=measured.x>>9u,local=measured.x&511u;
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    if(runtime.w!=measured.w || runtime.x!=_M8ObservationToken || stage>=3u)return;
    uint flags=M8LoadKernelStateRead(slot,local).flags;
    if((flags&(M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID|M8_FLOWER_SEED_FLAG))!=
        (M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID))return;
    if(lane==0u)
    {
        m8FineWriteStatus=0u;m8FinePlane=flags;
        m8FineOwner=M8FlowerFindOwner(slot,local,runtime.w);
        uint phaseCount=m8FineOwner==0u?0u:M8_FLOWER_DETAIL_SOURCE.Load4(m8FineOwner).w;
        m8FineState=phaseCount==0u?0u:M8_FLOWER_FINE_HAS_PHASE;
    }
    if(lane<M8_FLOWER_OBSERVED_RELATION_WORDS)m8FineReachedRelations[lane]=0u;
    if(lane<16u)m8FineObservedCells[lane]=0u;
    GroupMemoryBarrierWithGroupSync();
    int3 owner=M8GlobalKernelCoord(slot,local);
    [loop]for(uint index=lane;index<measured.z;index+=128u)
    {
        uint source=M8FlowerMeasuredRecordIndex(measured.y,index);
        M8ObservationRecord record=_M8ObservationRecordsRead[source];
        uint plane;float3 world;
        if(!M8FlowerMeasurement(record.SourcePixel,owner,plane,world))continue;
        uint state=M8_FLOWER_FINE_ACTIVE;
        if(((plane^flags)&M8_FLOWER_PLANE_STORAGE_MASK)!=0u ||
            (m8FineState&M8_FLOWER_FINE_HAS_PHASE)!=0u)state|=M8_FLOWER_FINE_NOVEL;
        InterlockedOr(m8FineState,state);
        M8FlowerReachObservedRelations(stage,owner,world);
    }
    GroupMemoryBarrierWithGroupSync();
    if((m8FineState&M8_FLOWER_FINE_ACTIVE)==0u ||
        (stage!=0u && (m8FineState&M8_FLOWER_FINE_NOVEL)==0u))return;
    M8FlowerExpandObservedCells(stage,lane);
    M8FlowerLoadObservedHalo(slot,lane);
    float2 errors=float2(M8FlowerNext(_M8PlaneErrorBounds.x+M8_FLOWER_NORMAL_QUANTIZATION_UPPER),
        M8FlowerNext(_M8PlaneErrorBounds.y+M8_FLOWER_OFFSET_QUANTIZATION_UPPER));
    uint words=(M8FlowerObservedRelationLevelsAt(stage).y+31u)/32u;
    [loop]for(uint word=0u;word<words;word++)
    {
        uint pending=m8FineReachedRelations[word];
        [loop]while(pending!=0u)
        {
            uint relation=32u*word+(uint)firstbitlow(pending);pending&=pending-1u;
            [loop]for(uint sign=0u;sign<2u;sign++)
            {
                M8FlowerGeometryNode task=M8FlowerObservedRelation(stage,relation,sign!=0u);
                M8FlowerPhaseRootEvidence observed=(M8FlowerPhaseRootEvidence)0;
                observed.Classification=2u;
                [loop]for(uint endpoint=0u;endpoint<2u;endpoint++)
                {
                    M8FlowerReduceFineEndpoint(measured,lane,task,endpoint,errors.x,errors.y);
                    if(lane==0u)
                    {
                        int3 junction;M8FlowerPhaseRootEvidence root;
                        if(!M8FlowerPhaseJunction(owner,task.Level+1u,task.Offset,junction) ||
                            !M8FlowerFineReadBucket(junction,root))observed.Classification=2u;
                        else if(endpoint==0u)observed=root;
                        else if(observed.Classification==1u)
                        {
                            M8FlowerPhaseRootEvidence closed;M8FlowerInterval bend;
                            if(M8FlowerSealPhaseRelation(observed,root,closed,bend)==1u)observed=closed;
                            else observed.Classification=2u;
                        }
                    }
                    GroupMemoryBarrierWithGroupSync();
                }
                // Endpoint reductions reused the same shared banks. Rebind the
                // packet after SEAL; never reuse roots overwritten by a bucket.
                if(stage!=0u)
                {
                    M8FlowerBeginFinePacket(slot,lane,runtime.w,local,errors);
                    if(lane==0u && observed.Classification==1u)
                        M8FlowerRequireFineOriginal(M8FlowerGeometryOriginalDependency(
                            m8FineOwner,M8FlowerGetOwnerEpoch(m8FineOwner),task));
                    M8FlowerAcquireFineOriginals(lane);
                }
                if(lane==0u && observed.Classification==1u)
                    M8FlowerRecordFineWriteStatus(M8FlowerCommitObservedPhase(slot,local,runtime.w,
                        task,observed,errors.x,errors.y));
                DeviceMemoryBarrierWithGroupSync();
                if(lane==0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
            }
        }
    }
    if(lane==0u)
    {
        if(m8FlowerHaloUnresolvedReads!=0u)
        {
            M8FlowerRequestSkinDependencies(m8FlowerHaloUnresolvedReads);
            M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
        }
        if(m8FineWriteStatus!=0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
    }
}
