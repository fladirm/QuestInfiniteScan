// Geometry dependency stage. Skin uses its own observation-local signal items.
void M8FlowerDrainGeometryBody(uint3 group,uint lane)
{
    if(_M8Counters[M8_COUNTER_OBSERVATION_COMPLETED]!=0u ||
        group.x>=min(_M8Counters[M8_COUNTER_TOUCHED_TILE_COUNT],32768u))return;
    uint slot=_M8TouchedTileQueueRead[group.x];
    if(slot>=_M8ObservationHotSlotCount)return;
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    uint4 meta=_M8TileRecords[M8TileMetaIndex(slot)];
    if(runtime.w==0u ||
        meta.x>=MERKABA_M8_CHUNK_CAPACITY || meta.y>=64u ||
        _M8ChunkTileRefsRead[meta.x*64u+meta.y]!=slot+1u)
    {
        if(lane==0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
        return;
    }
    if(lane==0u){m8FineWriteStatus=0u;m8FineAnyNovelty=0u;m8FineAnyActive=0u;
        }
    GroupMemoryBarrierWithGroupSync();
    uint stage=_M8Counters[M8_COUNTER_REFINEMENT_STAGE];
    uint phase=_M8Counters[M8_COUNTER_INVALIDATION_OWED]&
        M8_FLOWER_INVALIDATION_PHASE_MASK;
    if(phase==0u && stage==3u)return;
    M8FlowerDrainLocalInvalidations(slot,lane,runtime.w,phase);
    if(phase!=0u && m8FineWriteStatus==0u)
        M8FlowerDrainPeerInvalidations(slot,lane,runtime.w,
            phase==M8_FLOWER_INVALIDATION_PEER_PHASE);
    if(m8FineWriteStatus!=0u)
    {
        if(lane==0u)
        {
            if(phase!=0u)
                InterlockedOr(_M8Counters[M8_COUNTER_INVALIDATION_OWED],
                    M8_FLOWER_INVALIDATION_WRITE_FAILED);
            M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
            M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
        }
        return;
    }
    if(phase!=0u)return;
    if(_M8Counters[M8_COUNTER_OBSERVATION_FAILURE]!=0u ||
        _M8Counters[M8_COUNTER_FINE_LEASE_BUSY]!=0u ||
        _M8Counters[M8_COUNTER_UNRESOLVED_SURFACE_TILES]!=0u ||
        runtime.x!=_M8ObservationToken)return;
    uint4 bin=_M8ObservationTileBinsRead[slot];
    if(bin.x!=_M8ObservationToken || bin.w!=bin.y ||
        (bin.y!=0u&&(bin.z>=_M8ObservationRecordCapacity ||
            bin.y>_M8ObservationRecordCapacity-bin.z)))return;
    uint cursor=M8FlowerTileRefinementCursor(slot,runtime.w);
    uint stageBegin=stage==0u?0u:stage==1u?M8_FLOWER_ROOT_PHASE_TASKS:
        M8_FLOWER_ROOT_PHASE_TASKS+M8_FLOWER_R2_L1_TASKS;
    uint stageEnd=stage==0u?M8_FLOWER_ROOT_PHASE_TASKS:stage==1u?
        M8_FLOWER_ROOT_PHASE_TASKS+M8_FLOWER_R2_L1_TASKS:M8_FLOWER_PHASE_TASKS;
    if(cursor==M8_FLOWER_PHASE_TASKS || (stage<3u && cursor>=stageEnd))return;
    if(stage>3u)
    {
        if(lane==0u)M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return;
    }
    if(cursor<stageBegin)return;
    if(bin.y==0u)
    {
        if(lane==0u)M8FlowerStoreTileRefinementCursor(slot,runtime.w,
            _M8ObservationToken,M8_FLOWER_PHASE_TASKS);
        return;
    }
    for(uint local=lane;local<512u;local+=128u)
    {
        KernelState state=M8LoadKernelStateRead(slot,local);
        m8FinePlane[local]=state.flags;
        m8FineOwner[local]=M8FlowerFindOwner(slot,local,runtime.w);
        uint phaseCount=m8FineOwner[local]==0u?0u:
            M8_FLOWER_DETAIL_SOURCE.Load4(m8FineOwner[local]).w;
        m8FineState[local]=phaseCount==0u?0u:M8_FLOWER_FINE_HAS_PHASE;
    }
    GroupMemoryBarrierWithGroupSync();
    [loop]for(uint index=lane;index<bin.y;index+=128u)
    {
        M8ObservationRecord record=_M8ObservationRecordsRead[bin.z+index];
        if((record.TileAndKernel>>9u)!=slot)continue;
        uint local=record.TileAndKernel&511u;
        uint flags=m8FinePlane[local];
        if((flags&(M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID|M8_FLOWER_SEED_FLAG))!=
            (M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID))continue;
        uint plane;float3 world;
        if(!M8FlowerMeasurement(record.SourcePixel,M8GlobalKernelCoord(slot,local),plane,world))continue;
        uint state=M8_FLOWER_FINE_ACTIVE;
        InterlockedOr(m8FineAnyActive,1u);
        if(((plane^flags)&M8_FLOWER_PLANE_STORAGE_MASK)!=0u ||
            (m8FineState[local]&M8_FLOWER_FINE_HAS_PHASE)!=0u)
            state|=M8_FLOWER_FINE_NOVEL;
        InterlockedOr(m8FineState[local],state);
        if((state&M8_FLOWER_FINE_NOVEL)!=0u)InterlockedOr(m8FineAnyNovelty,1u);
    }
    GroupMemoryBarrierWithGroupSync();
    if(m8FineAnyActive==0u)
    {
        if(lane==0u)M8FlowerStoreTileRefinementCursor(slot,runtime.w,
            _M8ObservationToken,M8_FLOWER_PHASE_TASKS);
        return;
    }
    M8FlowerCacheTileHalo(slot,lane,false,true);
    float normalError=M8FlowerNext(_M8PlaneErrorBounds.x+M8_FLOWER_NORMAL_QUANTIZATION_UPPER);
    float offsetError=M8FlowerNext(_M8PlaneErrorBounds.y+M8_FLOWER_OFFSET_QUANTIZATION_UPPER);
    uint consumed=0u;
    [loop]while(consumed<_M8RefinementQuantum &&
        cursor<stageEnd)
    {
        M8FlowerGeometryNode task=(M8FlowerGeometryNode)0;
        uint observedTags[4];float4 observedBounds[4];uint observedCertain=0u;
        {
            if(cursor>=M8_FLOWER_ROOT_PHASE_TASKS && m8FineAnyNovelty==0u)
            {cursor=stageEnd;break;}
            if(!M8FlowerGeometryNodeAt(cursor,task)){cursor++;continue;}
            [loop]for(uint endpoint=0u;endpoint<2u;endpoint++)
            {
                M8FlowerReduceFineEndpoint(slot,lane,task,endpoint,normalError,offsetError);
                [loop]for(uint item=0u;item<4u;item++)
                {
                    uint local=lane+128u*item,bit=1u<<item;
                    if(endpoint!=0u && ((m8FineState[local]&M8_FLOWER_FINE_ACTIVE)==0u ||
                        (task.Level!=0u&&(m8FineState[local]&M8_FLOWER_FINE_NOVEL)==0u) ||
                        (observedCertain&bit)==0u))
                    {observedCertain&=~bit;continue;}
                    int3 junction;
                    if(!M8FlowerPhaseJunction(M8GlobalKernelCoord(slot,local),task.Level+1u,
                        task.Offset,junction))
                    {observedCertain&=~bit;continue;}
                    M8FlowerPhaseRootEvidence root;
                    bool certain=M8FlowerFineReadBucket(local,junction,root);
                    if(endpoint==0u)
                    {
                        observedTags[item]=root.Tag;
                        observedBounds[item]=float4(root.Root.x.lo,root.Root.x.hi,root.Root.y.lo,root.Root.y.hi);
                        if(root.Classification==1u)observedCertain|=bit;
                    }
                    else
                    {
                        if(certain)
                        {
                            M8FlowerPhaseRootEvidence first,closed;M8FlowerInterval bend;
                            first.Junction=junction;first.Tag=observedTags[item];first.Classification=1u;
                            first.Root.x=M8FlowerI(observedBounds[item].x,observedBounds[item].y);
                            first.Root.y=M8FlowerI(observedBounds[item].z,observedBounds[item].w);
                            certain=M8FlowerSealPhaseRelation(first,root,closed,bend)==1u;
                            if(certain)
                            {
                                observedTags[item]=closed.Tag;
                                observedBounds[item]=float4(closed.Root.x.lo,closed.Root.x.hi,closed.Root.y.lo,closed.Root.y.hi);
                            }
                        }
                        if(!certain)observedCertain&=~bit;
                    }
                }
                GroupMemoryBarrierWithGroupSync();
            }
        }
        uint ownerBatch=128u;
        uint alternatives=task.Level==0u?0u:1u;
        uint singleAlternative=task.Level!=0u?
            2u*M8FlowerGetPhaseFamily(task.Strand).RootNode+(task.Plus?1u:0u):0u;
        [loop]for(uint first=0u;first<512u;first+=ownerBatch)
        {
            uint count=min(ownerBatch,512u-first);
            M8FlowerPrepareFinePacket(slot,lane,runtime.w,first,count,alternatives,
                singleAlternative,0u,float2(normalError,offsetError));
            if(lane<count)
            {
                uint local=first+lane,item=first>>7u;
                if((observedCertain&(1u<<item))!=0u)
                {
                    M8FlowerPhaseRootEvidence observed;
                    M8FlowerPhaseJunction(M8GlobalKernelCoord(slot,local),task.Level+1u,task.Offset,observed.Junction);
                    observed.Tag=observedTags[item];observed.Classification=1u;
                    observed.Root.x=M8FlowerI(observedBounds[item].x,observedBounds[item].y);
                    observed.Root.y=M8FlowerI(observedBounds[item].z,observedBounds[item].w);
                    M8FlowerFineSchedulingStatus(M8FlowerCommitObservedPhase(slot,local,runtime.w,
                        task,observed,normalError,offsetError));
                }
            }
            DeviceMemoryBarrierWithGroupSync();
        }
        if(m8FlowerHaloUnresolvedReads!=0u && lane==0u)
        {
            M8FlowerRequestSkinDependencies(m8FlowerHaloUnresolvedReads);
            m8FineWriteStatus=M8_FLOWER_ARENA_BUSY;
        }
        GroupMemoryBarrierWithGroupSync();
        if(m8FineWriteStatus!=0u)break;
        cursor++;consumed++;
        if(lane==0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
    if(lane==0u)
    {
        if(!M8FlowerStoreTileRefinementCursor(slot,runtime.w,_M8ObservationToken,cursor))
            m8FineWriteStatus=M8_FLOWER_SIDECAR_STALE_SLOT;
        if(cursor<stageEnd || m8FineWriteStatus!=0u)
            M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
        if(m8FineWriteStatus!=0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
    }
}
