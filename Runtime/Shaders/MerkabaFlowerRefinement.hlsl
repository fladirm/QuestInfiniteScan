#ifndef GENESIS_MERKABA_FLOWER_REFINEMENT_INCLUDED
#define GENESIS_MERKABA_FLOWER_REFINEMENT_INCLUDED

#define M8_FLOWER_SIDECAR_WRITE
#include "MerkabaFlowerGeometry.hlsl"
#include "MerkabaFlowerSupport.hlsl"
#include "MerkabaFlowerSkinObservation.hlsl"

// One group owns a touched tile throughout this finite, observation-local
// program. A cursor is compute state only; it never enters a metric key.
// The two child levels use the SAME generated family tables as readout.
#define M8_FLOWER_R2_ROOT_TASKS 24u
#define M8_FLOWER_R3_ROOT_TASKS 16u
#define M8_FLOWER_ROOT_PHASE_TASKS (M8_FLOWER_R2_ROOT_TASKS+M8_FLOWER_R3_ROOT_TASKS)
#define M8_FLOWER_R2_L1_TASKS 144u
#define M8_FLOWER_R2_L2_TASKS 1152u
#define M8_FLOWER_PHASE_TASKS (M8_FLOWER_ROOT_PHASE_TASKS+M8_FLOWER_R2_L1_TASKS+M8_FLOWER_R2_L2_TASKS)
#define M8_FLOWER_SKIN_CURSOR_BASE (M8_FLOWER_PHASE_TASKS+1u)
#define M8_FLOWER_SKIN_PARENTS 57u
#define M8_FLOWER_SKIN_CARRIERS 128u
#define M8_FLOWER_FINE_ACTIVE 1u
#define M8_FLOWER_FINE_NOVEL 2u
#define M8_FLOWER_FINE_HAS_PHASE 4u

uint _M8RefinementQuantum;
groupshared uint m8FineOwner[512];
groupshared uint m8FineState[512];
groupshared uint m8FinePlane[512];
groupshared uint m8FinePresence[512];
groupshared uint m8FineWriteStatus;
groupshared uint m8FineAnyNovelty;
groupshared uint m8FineAnyActive;
groupshared uint m8FineSkinNext;
groupshared uint m8FineSkinConsumed;

// Required COLD endpoint addresses reuse the already drained block-claim
// prefix and the same frozen storage domain. This adds no coordinate index,
// world scan, or semantic claim about the unresolved surface.
void M8FlowerRequestSkinDependencies(uint neededHalo)
{
    [loop]while(neededHalo!=0u)
    {
        uint halo=(uint)firstbitlow(neededHalo);neededHalo&=neededHalo-1u;
        if(halo>=M8_FLOWER_HALO_COUNT)continue;
        if((m8FlowerHalo[halo]>>30u)!=M8_FLOWER_HALO_COLD)
        {M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);continue;}
        int3 tile=m8FlowerHaloTile+M8FlowerHaloDelta(halo);
        if(any(tile< -268435456) || any(tile>268435455))
        {M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);continue;}
        MerkabaM8Address address=MerkabaAddressOf(tile*8);
        int3 relative=address.blockCoord-_M8ScanCenterBlock+_M8ScanBlockRadius;
        if(_M8ScanBlockSide<=0 || any(relative<0) || any(relative>=_M8ScanBlockSide))
        {M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);continue;}
        uint side=(uint)_M8ScanBlockSide;
        uint ordinal=(uint)relative.x+side*((uint)relative.y+side*(uint)relative.z);
        int3 decoded;
        if(!M8ObservationBlockAddress(_M8ScanCenterBlock,_M8ScanBlockRadius,_M8ScanBlockSide,
            ordinal,decoded) || any(decoded!=address.blockCoord))
        {M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);continue;}
        M8DualRequestStorage(ordinal,M8_DUAL_INTENT_TILE_COLD,address.chunkLocal,address.tileLocal);
    }
}

uint M8FlowerRootRecordPetal(uint node)
{
    uint2 mask=M8FlowerNodeIncidentPetals[node];
    return mask.x!=0u?(uint)firstbitlow(mask.x):
        mask.y!=0u?32u+(uint)firstbitlow(mask.y):48u;
}

bool M8FlowerGeometryNodeAt(uint ordinal,out M8FlowerGeometryNode task)
{
    task=(M8FlowerGeometryNode)0;
    task.Plus=(ordinal&1u)!=0u;
    if(ordinal<M8_FLOWER_ROOT_PHASE_TASKS)
    {
        task.RootNode=6u+(ordinal>>1u);
        int4 node=M8FlowerNode[task.RootNode];
        task.Offset=node.xyz;task.Line=(uint)node.w;
        task.Kind=task.Line>=9u?1u:0u;
        task.Petal=M8FlowerRootRecordPetal(task.RootNode);
        return task.Petal<48u;
    }
    ordinal-=M8_FLOWER_ROOT_PHASE_TASKS;
    if(ordinal<M8_FLOWER_R2_L1_TASKS)
    {
        task.Strand=ordinal>>1u;
        uint4 strand=M8FlowerStrand[task.Strand];
        M8FlowerPhaseFamilyRule family=M8FlowerGetPhaseFamily(task.Strand);
        task.Level=1u;task.RootNode=family.RootNode;
        task.Line=(uint)M8FlowerNode[family.RootNode].w;
        if(M8FlowerLineMeta[task.Line].x!=2u)return false;
        task.Offset=M8FlowerNode[strand.x].xyz+M8FlowerNode[strand.y].xyz;
        task.Petal=strand.w&255u;task.Path=family.FinePath0;
        [unroll]for(uint edge=0u;edge<3u;edge++)
            if(M8FlowerPetalStrands[task.Petal][edge]==task.Strand)
                task.KnotSite=3u+edge;
        return true;
    }
    ordinal-=M8_FLOWER_R2_L1_TASKS;
    if(ordinal>=M8_FLOWER_R2_L2_TASKS)return false;
    task.Petal=ordinal/24u;
    task.ParentContext=1u+((ordinal/6u)&3u);
    task.KnotSite=3u+((ordinal>>1u)%3u);
    int endpoint,phase,inherited;
    if(!M8FlowerTryGetChildPhaseLoop(task.Petal,task.ParentContext,task.KnotSite,
        task.Level,task.Offset,task.Line,task.Strand,endpoint,phase,inherited) ||
        inherited>=0 || M8FlowerLineMeta[task.Line].x!=2u)return false;
    M8FlowerPhaseFamilyRule family=M8FlowerGetPhaseFamily(task.Strand);
    task.RootNode=family.RootNode;
    // Within THIS parent context the new AB/AC knot first occurs in child0,
    // BC in child1. Different parent predictions retain their own records.
    task.Path=4u*(task.ParentContext-1u)+(task.KnotSite==4u?1u:0u);
    return true;
}

// Every record is visited once per fixed endpoint, never paired with another
// record. L1/L2 endpoint supports lie inside the owning L0 support. At L0,
// Q=K+d reaches the fixed Cartesian subset of the already cached 27-tile
// halo: at most 2/4/8 spans for an R1/R2/R3 relation. A diagonal relation
// also needs its face-neighbour spans, not only the diagonal tile.
void M8FlowerReduceFineEndpoint(uint slot,uint lane,M8FlowerGeometryNode task,
    uint endpoint,float normalError,float offsetError)
{
    for(uint local=lane;local<512u;local+=128u)
    {
        M8FlowerResetObservationBucket(local);
        m8FinePresence[local]=0u;
    }
    GroupMemoryBarrierWithGroupSync();
    int3 direction=M8FlowerLineDirection[task.Line];
    int3 endpointLocal=(task.Offset-direction)/2+(endpoint==0u?int3(0,0,0):direction);
    uint spanCount=task.Level==0u && any(endpointLocal!=0)?8u:1u;
    [loop]for(uint span=0u;span<spanCount;span++)
    {
        uint3 useNeighbour=uint3(span&1u,(span>>1u)&1u,(span>>2u)&1u);
        if(any((endpointLocal==0)&(useNeighbour!=0u)))continue;
        int3 tileDelta=endpointLocal*int3(useNeighbour);
        uint sourceSlot=slot,ignored=0u;
        if(span!=0u && !M8FlowerHaloKernel(int3(4,4,4)+8*tileDelta,
            sourceSlot,ignored,true))continue;
        uint4 bin=_M8ObservationTileBinsRead[sourceSlot];
        if(bin.x!=_M8ObservationToken || bin.y==0u || bin.w!=bin.y ||
            bin.z>=_M8ObservationRecordCapacity ||
            bin.y>_M8ObservationRecordCapacity-bin.z)continue;
        [loop]for(uint index=lane;index<bin.y;index+=128u)
        {
            M8ObservationRecord record=_M8ObservationRecords[bin.z+index];
            uint recordLocal=record.TileAndKernel&511u;
            // Identity words survive re-emission. The R1 eligibility bit
            // does NOT: a retry correctly skips already-committed R1 and
            // must still consume this same immutable fine evidence.
            if((record.TileAndKernel>>9u)!=sourceSlot)continue;
            int3 q=int3(recordLocal&7u,(recordLocal>>3u)&7u,recordLocal>>6u);
            int3 k=q;
            if(task.Level==0u)
            {
                if(span!=0u)q+=8*tileDelta;
                k=q-endpointLocal;
            }
            if(any(k<0)||any(k>7))continue;
            uint local=(uint)k.x+8u*((uint)k.y+8u*(uint)k.z);
            if((m8FineState[local]&M8_FLOWER_FINE_ACTIVE)==0u ||
                (task.Level!=0u && (m8FineState[local]&M8_FLOWER_FINE_NOVEL)==0u))continue;
            int3 sourceOwner=M8GlobalKernelCoord(sourceSlot,recordLocal);
            uint plane;float3 world;
            if(!M8FlowerMeasurement(record.SourcePixel,sourceOwner,plane,world))continue;
            if((plane&M8_FLOWER_PLANE_FREE_SIDE)!=
                (m8FinePlane[local]&M8_FLOWER_PLANE_FREE_SIDE))
            {
                InterlockedOr(m8FinePresence[local],8u);continue;
            }
            if(task.Level!=0u)
            {
                precise float3 grid=mul(_MerkabaWorldToGrid,float4(world,1.0)).xyz;
                precise float3 relative=grid-float3(sourceOwner)*M8_FLOWER_LATTICE_STEP;
                float step=M8FlowerLevelStep(task.Level);
                float3 lower=float3(endpointLocal-1)*step;
                float3 upper=float3(endpointLocal+1)*step;
                if(any(relative<lower)||any(relative>=upper))continue;
            }
            float3 normal;float delta;
            M8FlowerUnpackPlane(plane,normal,delta);
            int3 loopOffset=task.Offset;
            if(task.Level==0u)loopOffset-=2*endpointLocal;
            precise float3 relative=(0.5*M8FlowerLevelStep(task.Level))*float3(task.Offset);
            if(task.Level==0u)relative-=float3(endpointLocal)*M8_FLOWER_LATTICE_STEP;
            M8FlowerInterval3 abc;
            if(!M8FlowerPlaneIntervals(normal,delta,relative,
                M8FlowerGeometryLoopRadius[task.Level*13u+task.Line],task.Line,
                normalError,offsetError,abc))
            {
                InterlockedOr(m8FinePresence[local],8u);continue;
            }
            uint tag,classification;M8FlowerInterval2 root;
            if(M8FlowerClassifyPlaneRoot(task.Level,task.Line,false,task.Plus,
                normal,delta,loopOffset,abc,tag,root,classification))
            {
                InterlockedOr(m8FinePresence[local],2u);
                M8FlowerIntersectRoot(local,tag,root);
            }
            else InterlockedOr(m8FinePresence[local],
                classification==M8_FLOWER_ROOT_IMPOSSIBLE?1u:8u);
        }
    }
    GroupMemoryBarrierWithGroupSync();
}

bool M8FlowerFineReadBucket(uint local,int3 junction,out M8FlowerPhaseRootEvidence root)
{
    root=(M8FlowerPhaseRootEvidence)0;root.Classification=2u;
    uint tag;M8FlowerInterval2 intersection;
    if(m8FinePresence[local]!=2u || !M8FlowerReadRootIntersection(local,tag,intersection))return false;
    root.Junction=junction;
    root.Tag=tag&(0x1fffu|M8_FLOWER_BOUNDARY_WITNESS_MASK);
    root.Root=intersection;
    root.Classification=1u;
    return M8FlowerPhaseIdentityValid(root);
}

// Returns scheduling status only. AMBIGUOUS metric evidence is a locally
// exhausted candidate, not a GPU-capacity retry and never an invented root.
uint M8FlowerCommitObservedPhase(uint slot,uint local,uint generation,
    M8FlowerGeometryNode task,M8FlowerPhaseRootEvidence first,
    M8FlowerPhaseRootEvidence second,float normalError,float offsetError)
{
    M8FlowerPhaseRootEvidence observed;M8FlowerInterval bend;
    if(M8FlowerSealPhaseRelation(first,second,observed,bend)!=1u)
        return M8_FLOWER_ARENA_OK;
    int3 owner=M8GlobalKernelCoord(slot,local);
    uint ownerRef=m8FineOwner[local],epoch=M8FlowerGetOwnerEpoch(ownerRef);
    M8FlowerPhaseRootEvidence predicted;
    if(!M8FlowerPredictGeometryNode(slot,ownerRef,epoch,owner,m8FinePlane[local],task,
        normalError,offsetError,predicted))return M8_FLOWER_ARENA_OK;
    M8FlowerPhaseResidualResult analysis;
    int r3Orientation=1;
    if(task.Kind==1u)
    {
        uint parity=((uint)owner.x&1u)|(((uint)owner.y&1u)<<1u)|(((uint)owner.z&1u)<<2u);
        uint axis=4u;
        [unroll]for(uint i=0u;i<4u;i++)
            if(M8FlowerTetraLine[parity][i]==(int)task.Line)axis=i;
        if(axis>=4u)return M8_FLOWER_ARENA_INVALID;
        analysis=M8FlowerAnalyzeR3PhaseResidual(predicted,observed,parity,axis);
        r3Orientation=M8FlowerTetraEta[parity][axis];
    }
    else analysis=M8FlowerAnalyzePhaseResidual(predicted,observed);
    uint key=M8FlowerPackDetailKey(task.Level,task.Path,task.Petal,task.Line,
        task.Kind,task.Plus,(predicted.Tag>>8u)&31u);
    if(analysis.Classification!=M8_FLOWER_PHASE_CERTAIN_NONZERO)
    {
        if(analysis.Classification==M8_FLOWER_PHASE_EXACT_ZERO && ownerRef!=0u)
        {
            M8FlowerDetailRecord previous;
            if(M8FlowerFindPhase(ownerRef,key,epoch,previous))
            {
                uint status=M8FlowerRemovePhase(ownerRef,key,
                    _M8DualPublishingGeneration,_M8DualRetiredGeneration);
                if(status==M8_FLOWER_ARENA_OK)
                {
                    M8MarkTileDirty(slot);
                    InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK],4u);
                    M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
                }
                return status;
            }
        }
        if(analysis.Classification==M8_FLOWER_PHASE_AMBIGUOUS ||
            analysis.Classification==M8_FLOWER_PHASE_PROMOTE)
            M8CounterIncrement(M8_COUNTER_REFINEMENT_UNRESOLVED);
        return M8_FLOWER_ARENA_OK;
    }
    uint status=M8_FLOWER_ARENA_OK;
    if(ownerRef==0u)
    {
        status=M8FlowerEnsureOwner(slot,local,generation,m8FinePlane[local],
            _M8DualPublishingGeneration,ownerRef);
        if(status!=M8_FLOWER_ARENA_OK)return status;
        M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
    m8FineOwner[local]=ownerRef;epoch=M8FlowerGetOwnerEpoch(ownerRef);
    M8FlowerDetailRecord record;
    record.Key=key;
    record.Lower=analysis.Lower;record.Upper=analysis.Upper;record.ParentEpoch=epoch;
    M8FlowerPhaseRootEvidence synthesized,closedRoot;
    if(task.Kind==0u)
    {
        if(M8FlowerSynthesizePhaseRecord(predicted,record.Key,record,epoch,synthesized)!=1u)
            return M8_FLOWER_ARENA_OK;
    }
    else
    {
        // R3 stores eta*tau. Undo ONLY that generated orientation for the
        // physical root synthesis. A measured metric record is not a
        // declaration of a unique chirally valid junction/petal class.
        M8FlowerInterval phase=M8FlowerDecodePhaseInterval(record.Lower,record.Upper);
        if(r3Orientation<0)phase=M8FlowerI(-phase.hi,-phase.lo);
        if(M8FlowerRotatePhaseEvidence(predicted,phase,synthesized)!=1u)
            return M8_FLOWER_ARENA_OK;
    }
    if(M8FlowerCloseSharedPhaseRoot(synthesized,observed,closedRoot)!=1u)
        return M8_FLOWER_ARENA_OK;
    // Non-spinning allocation may split one candidate across GPU quanta.
    // Already-published identical owners must not contend for the arena
    // again, otherwise a group could wait forever for simultaneous success.
    M8FlowerDetailRecord previous;
    if(M8FlowerFindPhase(ownerRef,record.Key,epoch,previous) &&
        previous.Lower==record.Lower && previous.Upper==record.Upper)
        return M8_FLOWER_ARENA_OK;
    status=M8FlowerCommitPhase(ownerRef,record,_M8DualPublishingGeneration,_M8DualRetiredGeneration);
    if(status==M8_FLOWER_ARENA_OK)
    {
        m8FineState[local]|=M8_FLOWER_FINE_NOVEL;
        InterlockedOr(m8FineAnyNovelty,1u);
        M8MarkTileDirty(slot);
        InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK],4u);
        M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
    return status;
}

void M8FlowerFineSchedulingStatus(uint status)
{
    if(status!=M8_FLOWER_ARENA_OK)InterlockedMax(m8FineWriteStatus,status);
}

// All phase stages have retired before this is called. A carrier reads one
// immutable selected root bundle, then drains both signal authorities. The
// minimum unfinished parent is the tile cursor; already committed groups
// replay idempotently when another owner yields on capacity or residency.
void M8FlowerDrainSkinTile(uint slot,uint lane,uint slotGeneration,float2 errors,
    inout uint cursor,inout uint consumed)
{
    [loop]while(cursor>=M8_FLOWER_SKIN_CURSOR_BASE && consumed<_M8RefinementQuantum)
    {
        uint task=cursor-M8_FLOWER_SKIN_CURSOR_BASE;
        uint carrier=task/M8_FLOWER_SKIN_PARENTS,parentCursor=task%M8_FLOWER_SKIN_PARENTS;
        if(carrier>=M8_FLOWER_SKIN_CARRIERS){cursor=M8_FLOWER_PHASE_TASKS;break;}
        if(lane==0u)
        {
            m8FineSkinNext=M8_FLOWER_SKIN_PARENTS;
            m8FineSkinConsumed=1u;
            m8FlowerHaloUnresolvedReads=0u;
        }
        GroupMemoryBarrierWithGroupSync();
        uint budget=_M8RefinementQuantum-consumed;
        [loop]for(uint local=lane;local<512u;local+=128u)
        {
            if((m8FineState[local]&M8_FLOWER_FINE_ACTIVE)==0u)continue;
            M8FlowerSymbolRecord symbol;uint unresolved;
            M8FlowerPhaseRootEvidence roots[7];float3 positions[7];
            uint classification=M8FlowerClassifyL2Carrier(slot,local,carrier,errors,
                symbol,unresolved,roots,positions);
            if(classification!=1u)
            {
                if(classification==2u)M8CounterIncrement(M8_COUNTER_REFINEMENT_UNRESOLVED);
                continue;
            }
            uint source=M8FlowerL2Wedge[6u*carrier].y;
            uint flowerKey=M8FlowerPackL2Key(source&15u,source>>4u,
                (roots[0].Tag&(1u<<7u))!=0u,(roots[0].Tag>>8u)&31u);
            uint next=parentCursor,progress,ambiguous;
            uint status=M8FlowerDrainSkinCarrier(slot,local,slotGeneration,flowerKey,
                M8FlowerDrawActiveWedgeMask(symbol),unresolved,roots,errors.x,errors.y,
                budget,next,progress,ambiguous);
            if(progress!=0u)InterlockedAdd(_M8Counters[M8_COUNTER_REFINEMENT_WORK_PROGRESS],progress);
            InterlockedMax(m8FineSkinConsumed,min(budget,max(1u,progress)));
            if(ambiguous!=0u)InterlockedAdd(_M8Counters[M8_COUNTER_REFINEMENT_UNRESOLVED],ambiguous);
            M8FlowerFineSchedulingStatus(status);
            InterlockedMin(m8FineSkinNext,next);
        }
        DeviceMemoryBarrierWithGroupSync();
        if(m8FlowerHaloUnresolvedReads!=0u)
        {
            if(lane==0u)M8FlowerRequestSkinDependencies(m8FlowerHaloUnresolvedReads);
            // Revisit the same parent after the actual requested COLD data
            // becomes resident. This is scheduling, not new camera evidence.
            if(lane==0u)m8FineWriteStatus=M8_FLOWER_ARENA_BUSY;
        }
        GroupMemoryBarrierWithGroupSync();
        if(m8FineWriteStatus!=0u)break;
        uint next=m8FineSkinNext;
        if(next<=parentCursor)
        {
            if(lane==0u)m8FineWriteStatus=M8_FLOWER_ARENA_INVALID;
            GroupMemoryBarrierWithGroupSync();
            break;
        }
        // A proved absent/uniform carrier can prune all57 parents after
        // one finite classification. It consumes at least one budget unit,
        // without spending57 empty quanta or using Fibonacci as a scheduler.
        consumed+=min(budget,m8FineSkinConsumed);
        if(next==M8_FLOWER_SKIN_PARENTS)
        {
            cursor=carrier+1u==M8_FLOWER_SKIN_CARRIERS?M8_FLOWER_PHASE_TASKS:
                M8_FLOWER_SKIN_CURSOR_BASE+(carrier+1u)*M8_FLOWER_SKIN_PARENTS;
        }
        else cursor=M8_FLOWER_SKIN_CURSOR_BASE+carrier*M8_FLOWER_SKIN_PARENTS+next;
        if(lane==0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
}

[numthreads(128,1,1)]
void DrainObservationRefinement(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
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
    if(lane==0u){m8FineWriteStatus=0u;m8FineAnyNovelty=0u;m8FineAnyActive=0u;}
    GroupMemoryBarrierWithGroupSync();
    // Invalidation and the preceding M8 publication are one source
    // transaction. Storage cannot capture a tile while these bits remain.
    if(lane<16u)
    {
        uint word=M8TileWordIndex(slot,lane);
        uint pending=_M8TileBits[word].w;
        [loop]while(pending!=0u)
        {
            uint bit=(uint)firstbitlow(pending);pending&=pending-1u;
            uint status=M8FlowerInvalidateOwner(slot,32u*lane+bit,runtime.w,
                _M8DualPublishingGeneration,_M8DualRetiredGeneration);
            if(status==M8_FLOWER_ARENA_OK)
            {
                DeviceMemoryBarrier();
                InterlockedAnd(_M8TileBits[word].w,~(1u<<bit));
                M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
                M8MarkTileDirty(slot);
            }
            else M8FlowerFineSchedulingStatus(status);
        }
    }
    DeviceMemoryBarrierWithGroupSync();
    if(m8FineWriteStatus!=0u)
    {
        if(lane==0u)
        {
            M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
            M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
        }
        return;
    }
    // Even a later coarse allocation/error must not strand an earlier M8
    // structural change with its old fine epoch. Only metric work waits for
    // the complete direct/dual attempt; the epoch transaction runs above.
    if(_M8Counters[M8_COUNTER_OBSERVATION_FAILURE]!=0u ||
        _M8Counters[M8_COUNTER_UNRESOLVED_SURFACE_TILES]!=0u ||
        _M8Counters[M8_COUNTER_UNRESOLVED_CARVE_TILES]!=0u ||
        runtime.x!=_M8ObservationToken)return;
    uint4 bin=_M8ObservationTileBinsRead[slot];
    if(bin.x!=_M8ObservationToken || bin.w!=bin.y ||
        (bin.y!=0u&&(bin.z>=_M8ObservationRecordCapacity ||
            bin.y>_M8ObservationRecordCapacity-bin.z)))return;
    uint cursor=M8FlowerTilePendingCursor(slot,runtime.w,_M8ObservationToken);
    uint stage=_M8Counters[M8_COUNTER_REFINEMENT_STAGE];
    uint stageBegin=stage==0u?0u:stage==1u?M8_FLOWER_ROOT_PHASE_TASKS:
        stage==2u?M8_FLOWER_ROOT_PHASE_TASKS+M8_FLOWER_R2_L1_TASKS:M8_FLOWER_SKIN_CURSOR_BASE;
    uint stageEnd=stage==0u?M8_FLOWER_ROOT_PHASE_TASKS:stage==1u?
        M8_FLOWER_ROOT_PHASE_TASKS+M8_FLOWER_R2_L1_TASKS:M8_FLOWER_PHASE_TASKS;
    if(cursor==M8_FLOWER_PHASE_TASKS || (stage<3u && cursor>=stageEnd))return;
    if(stage>3u || cursor<stageBegin)
    {
        if(lane==0u)M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return;
    }
    if(bin.y==0u)
    {
        if(lane==0u)M8FlowerStoreTilePendingCursor(slot,runtime.w,
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
        M8ObservationRecord record=_M8ObservationRecords[bin.z+index];
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
        if(lane==0u)M8FlowerStoreTilePendingCursor(slot,runtime.w,
            _M8ObservationToken,M8_FLOWER_PHASE_TASKS);
        return;
    }
    M8FlowerCacheTileHalo(slot,lane,false,true);
    float normalError=M8FlowerNext(_M8PlaneErrorBounds.x+M8_FLOWER_NORMAL_QUANTIZATION_UPPER);
    float offsetError=M8FlowerNext(_M8PlaneErrorBounds.y+M8_FLOWER_OFFSET_QUANTIZATION_UPPER);
    uint consumed=0u;
    [loop]while(stage<3u && cursor<stageEnd && consumed<_M8RefinementQuantum)
    {
        if(cursor>=M8_FLOWER_ROOT_PHASE_TASKS && m8FineAnyNovelty==0u)
        {cursor=stageEnd;break;}
        M8FlowerGeometryNode task;
        if(!M8FlowerGeometryNodeAt(cursor,task)){cursor++;continue;}
        M8FlowerPhaseRootEvidence first[4];
        [loop]for(uint endpoint=0u;endpoint<2u;endpoint++)
        {
            M8FlowerReduceFineEndpoint(slot,lane,task,endpoint,normalError,offsetError);
            if(endpoint==0u)
            {
                [loop]for(uint item=0u;item<4u;item++)
                {
                    uint local=lane+128u*item;
                    int3 junction;
                    first[item]=(M8FlowerPhaseRootEvidence)0;first[item].Classification=2u;
                    if(M8FlowerPhaseJunction(M8GlobalKernelCoord(slot,local),task.Level+1u,
                        task.Offset,junction))M8FlowerFineReadBucket(local,junction,first[item]);
                }
                // The loop index is workgroup-uniform. Retain first roots
                // privately before endpoint1 reuses the shared reduction.
                GroupMemoryBarrierWithGroupSync();
            }
        }
        [loop]for(uint item=0u;item<4u;item++)
        {
            uint local=lane+128u*item;
            if((m8FineState[local]&M8_FLOWER_FINE_ACTIVE)==0u ||
                (task.Level!=0u&&(m8FineState[local]&M8_FLOWER_FINE_NOVEL)==0u))continue;
            M8FlowerPhaseRootEvidence second;
            if(first[item].Classification!=1u ||
                !M8FlowerFineReadBucket(local,first[item].Junction,second))continue;
            M8FlowerFineSchedulingStatus(M8FlowerCommitObservedPhase(slot,local,runtime.w,
                task,first[item],second,normalError,offsetError));
        }
        DeviceMemoryBarrierWithGroupSync();
        if(m8FlowerHaloUnresolvedReads!=0u)
        {
            if(lane==0u)
            {
                M8FlowerRequestSkinDependencies(m8FlowerHaloUnresolvedReads);
                m8FineWriteStatus=M8_FLOWER_ARENA_BUSY;
            }
        }
        GroupMemoryBarrierWithGroupSync();
        if(m8FineWriteStatus!=0u)break;
        cursor++;consumed++;
        if(cursor==M8_FLOWER_ROOT_PHASE_TASKS)
        {
            // The same frozen observation now has all original phase work.
            // Fetch dual descriptors once per tile, then classify the actual
            // generated junction alternatives. No metric bundle is mistaken
            // for a branch proof, and R1/R2 remain independently valid.
            M8FlowerSupportCacheDualTile(M8GlobalKernelCoord(slot,0u)>>3,lane,128u);
            for(uint local=lane;local<512u;local+=128u)
            {
                if((m8FineState[local]&M8_FLOWER_FINE_ACTIVE)==0u || m8FineOwner[local]==0u)continue;
                M8FlowerJunctionSelection junction=M8FlowerReadR3Junction(slot,m8FineOwner[local],
                    M8GlobalKernelCoord(slot,local),m8FinePlane[local],float2(normalError,offsetError));
                if(junction.Classification!=1u)M8CounterIncrement(M8_COUNTER_REFINEMENT_UNRESOLVED);
            }
        }
        if(lane==0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
    if(stage==2u && cursor==M8_FLOWER_PHASE_TASKS && m8FineWriteStatus==0u)
        cursor=M8_FLOWER_SKIN_CURSOR_BASE;
    if(stage==3u)
        M8FlowerDrainSkinTile(slot,lane,runtime.w,float2(normalError,offsetError),cursor,consumed);
    if(lane==0u)
    {
        if(!M8FlowerStoreTilePendingCursor(slot,runtime.w,_M8ObservationToken,cursor))
            m8FineWriteStatus=M8_FLOWER_SIDECAR_STALE_SLOT;
        if((stage<3u?cursor<stageEnd:cursor!=M8_FLOWER_PHASE_TASKS) || m8FineWriteStatus!=0u)
            M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
        if(m8FineWriteStatus!=0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
    }
}

#endif
