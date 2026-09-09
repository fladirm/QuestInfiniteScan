// The observation drain body, included once per stage class. It is a plain
// include with no guard: MerkabaFlowerRefinement.hlsl defines M8_DRAIN_SKIN
// and M8_DRAIN_BODY_NAME and includes it twice. The two entry points are not
// two algorithms; they are the same serialized continuation chain split at
// the boundary the contract already freezes, geometry to L2 and L3..L5 as
// signal only. A runtime flag would leave both branches in every module,
// which is the whole cost this split exists to remove.

void M8_DRAIN_BODY_NAME(uint3 group,uint lane)
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
#if M8_DRAIN_SKIN
    // The receipt arena is a physical scratch bound and it belongs to skin
    // alone: m8FineReceiptTile addresses it, and only M8FlowerStoreSkinCarrier
    // and M8FlowerRestoreSkinCarrier read it. Geometry authors no receipt, so
    // gating it here deferred committed geometry for no capacity reason at all.
    // A tile past the arena is not skinned by THIS snapshot; its geometry still
    // commits now and the next snapshot, with its own touched queue, skins it.
    if(group.x>=M8_FLOWER_SKIN_RECEIPT_TILES)
    {
        if(lane==0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
        return;
    }
#endif
    if(lane==0u){m8FineWriteStatus=0u;m8FineAnyNovelty=0u;m8FineAnyActive=0u;
#if M8_DRAIN_SKIN
        m8FineReceiptTile=group.x;
#endif
        }
    GroupMemoryBarrierWithGroupSync();
    uint stage=_M8Counters[M8_COUNTER_REFINEMENT_STAGE];
    // The owed invalidation cut is a property of the world, so it is read from
    // its own persistent counter. While a cut is owed no refinement runs at
    // all: no tile may advance fine evidence past a structural change whose
    // epoch has not been retired yet.
    uint phase=_M8Counters[M8_COUNTER_INVALIDATION_OWED]&
        M8_FLOWER_INVALIDATION_PHASE_MASK;
    // Each entry point serves one side of the frozen geometry/signal
    // boundary. Geometry is stages 0..2, skin is stage 3, and the global
    // Finalize barrier already guarantees they never overlap, so the other
    // side's code is not merely unreached here, it is not emitted at all.
#if M8_DRAIN_SKIN
    if(phase==0u && stage!=3u)return;
#else
    if(phase==0u && stage==3u)return;
#endif
#if !M8_DRAIN_SKIN
    // The invalidation writer belongs to geometry. The signal stages never
    // author epochs, so carrying its code would only put a geometry writer
    // inside a signal module.
    M8FlowerDrainLocalInvalidations(slot,lane,runtime.w,phase);
    if(phase!=0u && m8FineWriteStatus==0u)
        M8FlowerDrainPeerInvalidations(slot,lane,runtime.w,
            phase==M8_FLOWER_INVALIDATION_PEER_PHASE);
#endif
    if(m8FineWriteStatus!=0u)
    {
        if(lane==0u)
        {
            // A failed invalidation writer keeps its phase where it is, so
            // the next snapshot repeats that phase. Ordinary refinement that
            // ran out of its bounded budget signals nothing.
            if(phase!=0u)
                InterlockedOr(_M8Counters[M8_COUNTER_INVALIDATION_OWED],
                    M8_FLOWER_INVALIDATION_WRITE_FAILED);
            M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
            M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
        }
        return;
    }
    // No phase or skin writer runs between the global gather and peer ACK
    // boundaries. Finalize retains even a failed observation until both cuts
    // retire. This precedes bin.y==0, cursor and evidence-failure early outs.
    if(phase!=0u)return;
    // Even a later coarse allocation/error must not strand an earlier M8
    // structural change with its old fine epoch. Only metric work waits for
    // the complete direct/dual attempt; the epoch transaction runs above.
    // UNRESOLVED_OBSERVATION_TILES counts tiles this snapshot skipped: a COLD
    // dual chunk, a kernel the dual has not certified FULL, a bin the allocator
    // only created now. Those are the normal state of a world whose default is
    // UNKNOWN, so a global veto on them meant no tile ever refined. The two
    // conditions that really are global stay: a measurement-identity failure,
    // and a parent whose once-only R1 commit still holds its read lease.
    if(_M8Counters[M8_COUNTER_OBSERVATION_FAILURE]!=0u ||
        _M8Counters[M8_COUNTER_FINE_LEASE_BUSY]!=0u ||
        _M8Counters[M8_COUNTER_UNRESOLVED_SURFACE_TILES]!=0u ||
        runtime.x!=_M8ObservationToken)return;
    uint4 bin=_M8ObservationTileBinsRead[slot];
    if(bin.x!=_M8ObservationToken || bin.w!=bin.y ||
        (bin.y!=0u&&(bin.z>=_M8ObservationRecordCapacity ||
            bin.y>_M8ObservationRecordCapacity-bin.z)))return;
    // The persistent FlowerDetail world is the refinement memory, so the
    // cursor is keyed by the slot generation alone. Keyed by the observation
    // token it restarted at zero every snapshot, which is why the old model
    // had to hold one snapshot until the whole worklist drained. It now
    // survives snapshots and dies with the tile generation that changed.
    uint cursor=M8FlowerTileRefinementCursor(slot,runtime.w);
    uint stageBegin=stage==0u?0u:stage==1u?M8_FLOWER_ROOT_PHASE_TASKS:
        stage==2u?M8_FLOWER_ROOT_PHASE_TASKS+M8_FLOWER_R2_L1_TASKS:M8_FLOWER_SKIN_CURSOR_BASE;
    uint stageEnd=stage==0u?M8_FLOWER_ROOT_PHASE_TASKS:stage==1u?
        M8_FLOWER_ROOT_PHASE_TASKS+M8_FLOWER_R2_L1_TASKS:M8_FLOWER_PHASE_TASKS;
    if(cursor==M8_FLOWER_PHASE_TASKS || (stage<3u && cursor>=stageEnd))return;
    if(stage>3u)
    {
        if(lane==0u)M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return;
    }
    // Stages are dependency barriers of one snapshot command graph, so every
    // tile passes all four dispatches in order. Tiles do not advance at the
    // same rate: one still inside the root span when the L1 barrier arrives
    // has simply not reached that dependency yet, and continues its roots in
    // the next snapshot's root dispatch. That is progress, not an identity
    // failure - the cursor is monotone and lives in the world.
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
        // The owner also exists for RGB-only and skin-V state. Neither is
        // a world-geometry ancestor and neither may force L1/L2 expansion.
        // Read the existing owner phase span once, not once per measurement.
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
        // Identical plane evidence and no ancestor innovation cannot prove
        // a new nonzero child residual. This exact-equality case prunes a
        // flat carrier without a variance score or blind dyadic expansion.
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
    // The cursor this entry started from. The resolver and the RGB pass must
    // leave it untouched; only the V pass publishes an advance.
    uint entryCursor=cursor;
    uint consumed=0u;
    // Two modes only. The post-root R3 junction pass that used to run here
    // produced no state: its sole consumer was M8_COUNTER_REFINEMENT_UNRESOLVED,
    // a telemetry counter read only by MerkabaGpuTimestamps. Geometry keeps
    // stages 0..2 and skin is stage 3; M8FlowerReadR3Junction remains where it
    // is functionally required, in M8FlowerPrepareCompletionPetal.
    [loop]while(consumed<_M8RefinementQuantum &&
        (stage<3u?cursor<stageEnd:cursor>=M8_FLOWER_SKIN_CURSOR_BASE))
    {
#if M8_DRAIN_SKIN
        const uint mode=2u;
#else
        const uint mode=0u;
#endif
        uint budget=_M8RefinementQuantum-consumed;
        uint carrier=0u,parentCursor=0u;
        M8FlowerGeometryNode task=(M8FlowerGeometryNode)0;
        uint observedTags[4];float4 observedBounds[4];uint observedCertain=0u;
        if(mode==0u)
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
                // Endpoint1's final SEAL overwrites the same four private
                // tag/interval slots. No second roots array survives here.
                GroupMemoryBarrierWithGroupSync();
            }
        }
        else
        {
            uint ordinal=cursor-M8_FLOWER_SKIN_CURSOR_BASE;
            carrier=ordinal/M8_FLOWER_SKIN_PARENTS;parentCursor=ordinal%M8_FLOWER_SKIN_PARENTS;
            if(carrier>=128u){cursor=M8_FLOWER_PHASE_TASKS;break;}
            if(lane==0u)
            {
                m8FineSkinNext=57u;m8FineSkinConsumed=1u;
                m8FlowerHaloUnresolvedReads=0u;
            }
            GroupMemoryBarrierWithGroupSync();
        }
        // A single acquire call serves every stage. Phase keeps 128 owner
        // consumers; skin/R3 distributes complete alternatives over all lanes
        // for three owners. The skin's 21 children then consume in parallel.
        uint ownerBatch=mode==0u?128u:3u;
        uint alternatives=mode==0u?(task.Level==0u?0u:1u):52u;
        uint singleAlternative=mode==0u && task.Level!=0u?
            2u*M8FlowerGetPhaseFamily(task.Strand).RootNode+(task.Plus?1u:0u):0u;
        [loop]for(uint first=0u;first<512u;first+=ownerBatch)
        {
            uint count=min(ownerBatch,512u-first);
#if !M8_DRAIN_SKIN || M8_DRAIN_RESOLVE
            // The original shared-root packet feeds parent prediction, so it
            // belongs to geometry and to the resolver. A signal pass consumes
            // the resolved receipt and must never rebuild it.
            M8FlowerPrepareFinePacket(slot,lane,runtime.w,first,count,alternatives,
                singleAlternative,carrier,float2(normalError,offsetError));
#endif
            if(mode==0u && lane<count)
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
            else if(mode==2u)
#if M8_DRAIN_RESOLVE
                M8FlowerResolveSkinCarrier(slot,lane,runtime.w,carrier,parentCursor,
                    float2(normalError,offsetError));
#elif M8_DRAIN_SIGNAL==0
                M8FlowerDrainSkinRgb(slot,lane,runtime.w,carrier,parentCursor,budget,
                    float2(normalError,offsetError));
#else
                M8FlowerDrainSkinV(slot,lane,runtime.w,carrier,parentCursor,budget,
                    float2(normalError,offsetError));
#endif
            DeviceMemoryBarrierWithGroupSync();
        }
        if(m8FlowerHaloUnresolvedReads!=0u && lane==0u)
        {
            M8FlowerRequestSkinDependencies(m8FlowerHaloUnresolvedReads);
            m8FineWriteStatus=M8_FLOWER_ARENA_BUSY;
        }
        GroupMemoryBarrierWithGroupSync();
        if(m8FineWriteStatus!=0u)break;
        if(mode==0u)
        {
            cursor++;consumed++;
        }
        else
        {
            uint next=m8FineSkinNext;
            if(next<=parentCursor)
            {
                if(lane==0u)m8FineWriteStatus=M8_FLOWER_ARENA_INVALID;
                GroupMemoryBarrierWithGroupSync();
                break;
            }
            consumed+=min(budget,m8FineSkinConsumed);
            cursor=next==57u?(carrier+1u==128u?M8_FLOWER_PHASE_TASKS:
                M8_FLOWER_SKIN_CURSOR_BASE+(carrier+1u)*57u):
                M8_FLOWER_SKIN_CURSOR_BASE+carrier*57u+next;
            // One dispatch triple resolves and consumes exactly one L2
            // carrier. Resolve is per carrier, so crossing this boundary
            // inside the loop would run the signal passes against receipts
            // that were never written for the next carrier, read them as an
            // already finished parent and step the cursor straight over that
            // carrier's skin work. The next triple picks it up instead.
            if(cursor<M8_FLOWER_SKIN_CURSOR_BASE ||
                (cursor-M8_FLOWER_SKIN_CURSOR_BASE)/M8_FLOWER_SKIN_PARENTS!=carrier)break;
        }
        if(lane==0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
    if(stage==2u && cursor==M8_FLOWER_PHASE_TASKS && m8FineWriteStatus==0u)
        cursor=M8_FLOWER_SKIN_CURSOR_BASE;
    if(lane==0u)
    {
        // The three skin entries walk one cursor. Only the last of them, the
        // V signal, may advance it: the resolver and the RGB pass have not
        // finished the parent step, and moving the cursor under them would
        // let a retry skip work the observation still owes. They therefore
        // leave the stored cursor exactly as they found it, which also makes
        // their retry idempotent under the same observation token.
#if M8_DRAIN_SKIN && (M8_DRAIN_RESOLVE || M8_DRAIN_SIGNAL==0)
        if(!M8FlowerStoreTileRefinementCursor(slot,runtime.w,_M8ObservationToken,entryCursor))
            m8FineWriteStatus=M8_FLOWER_SIDECAR_STALE_SLOT;
        if(lane==0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
#else
        if(!M8FlowerStoreTileRefinementCursor(slot,runtime.w,_M8ObservationToken,cursor))
            m8FineWriteStatus=M8_FLOWER_SIDECAR_STALE_SLOT;
        if((stage<3u?cursor<stageEnd:cursor!=M8_FLOWER_PHASE_TASKS) || m8FineWriteStatus!=0u)
            M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
#endif
        if(m8FineWriteStatus!=0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
    }
}
