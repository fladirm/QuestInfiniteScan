#ifndef GENESIS_MERKABA_FLOWER_REFINEMENT_INCLUDED
#define GENESIS_MERKABA_FLOWER_REFINEMENT_INCLUDED

#define M8_FLOWER_GEOMETRY_PACKET_READ
#include "MerkabaFlowerGeometry.hlsl"
#include "MerkabaFlowerSupport.hlsl"
#include "MerkabaFlowerSkinObservation.hlsl"
#include "MerkabaFlowerEvidencePacket.hlsl"

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
// Endpoint presence is transient and disjoint from the three owner flags.
// The same reset/reduction barrier owns both fields; no extra 512-word bank.
#define M8_FLOWER_FINE_PRESENCE_SHIFT 3u
#define M8_FLOWER_FINE_PRESENCE_MASK (15u<<M8_FLOWER_FINE_PRESENCE_SHIFT)

uint _M8RefinementQuantum;
groupshared uint m8FineOwner[512];
groupshared uint m8FineState[512];
groupshared uint m8FinePlane[512];
groupshared uint m8FineWriteStatus;
groupshared uint m8FineAnyNovelty;
groupshared uint m8FineAnyActive;
groupshared uint m8FineSkinNext;
groupshared uint m8FineSkinConsumed;

// Phase buckets are dead after endpoint1 SEAL, before this packet is filled.
// Reuse their seven existing 512-word banks; no additional shared allocation.
// Skin: 3 owners * (52 original alternatives * 19 + 14 site roots * 10)
// = 3384 words. Phase: 128 owners * one actual family alternative * 19
// = 2432 words. Both fit the existing 3584 words including packet controls.
#define M8_FINE_PACKET_SITES 2964u
#define M8_FINE_PACKET_SKIN_CONTROL 3384u
#define M8_FINE_PACKET_SKIN_STRIDE 24u
#define M8_FINE_PACKET_READY 3456u
#define M8_FINE_PACKET_WORLD_SITES 0u
#define M8_FINE_PACKET_CONTEXT 128u
#define M8_FINE_PACKET_CONTEXT_STRIDE 13u
#define M8_FINE_PACKET_WORLD_VALID 176u
#define M8_FINE_PACKET_SIGNAL_VALUES 256u
#define M8_FINE_PACKET_SIGNAL_FLAGS 352u

static uint m8FinePacketSlot;
static uint m8FinePacketFirst;
static uint m8FinePacketCount;
static uint m8FinePacketAlternatives;
static uint m8FinePacketSingleAlternative;
static uint m8FinePacketCarrier;
static float2 m8FinePacketErrors;
static bool m8FinePacketCollectEndpoints;
static uint m8FinePacketEndpointReceipt;

uint M8FlowerPacketLoadWord(uint address)
{
    uint index=address&511u;
    switch(address>>9u)
    {
        case 0u:return m8FlowerTagIntersection[index];
        case 1u:return m8FlowerTagUnion[index];
        case 2u:return m8FlowerLower[index];
        case 3u:return m8FlowerUpper[index];
        case 4u:return m8FlowerRootLowerY[index];
        case 5u:return m8FlowerRootUpperY[index];
        default:return m8FlowerRepresentative[index];
    }
}

void M8FlowerPacketStoreWord(uint address,uint value)
{
    uint index=address&511u;
    switch(address>>9u)
    {
        case 0u:m8FlowerTagIntersection[index]=value;break;
        case 1u:m8FlowerTagUnion[index]=value;break;
        case 2u:m8FlowerLower[index]=value;break;
        case 3u:m8FlowerUpper[index]=value;break;
        case 4u:m8FlowerRootLowerY[index]=value;break;
        case 5u:m8FlowerRootUpperY[index]=value;break;
        default:m8FlowerRepresentative[index]=value;break;
    }
}

bool M8FlowerFinePacketIdentity(uint slot,uint local,uint ownerRef,int3 owner,uint flags,float2 errors)
{
    return slot==m8FinePacketSlot && local>=m8FinePacketFirst &&
        local-m8FinePacketFirst<m8FinePacketCount && ownerRef==m8FineOwner[local] &&
        flags==m8FinePlane[local] && all(owner==M8FlowerEndpointOwner(slot,local)) &&
        all(asuint(errors)==asuint(m8FinePacketErrors));
}

uint M8FlowerReadOriginalPacket(uint ownerSlot,uint ownerRef,int3 owner,uint flags,
    uint nodeIndex,bool plus,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence root,out bool provisional,
    out M8FlowerPhaseRootEvidence rawLocalBase,out uint endpointReceipt)
{
    root=(M8FlowerPhaseRootEvidence)0;root.Classification=2u;
    rawLocalBase=root;provisional=false;endpointReceipt=0u;
    int3 relative=owner-M8FlowerEndpointOwner(m8FinePacketSlot,0u);
    uint local=(uint)(relative.x+8*(relative.y+8*relative.z));
    uint alternative=2u*nodeIndex+(plus?1u:0u);
    if(any(relative<0) || any(relative>=8) || nodeIndex>=26u ||
        !M8FlowerFinePacketIdentity(ownerSlot,local,ownerRef,owner,flags,float2(normalError,offsetError)) ||
        (m8FinePacketAlternatives!=52u &&
            (m8FinePacketAlternatives!=1u || alternative!=m8FinePacketSingleAlternative)))
    {InterlockedMax(m8FineWriteStatus,M8_FLOWER_ARENA_INVALID);return 2u;}
    uint item=(local-m8FinePacketFirst)*m8FinePacketAlternatives+
        (m8FinePacketAlternatives==52u?alternative:0u);
    uint status=M8FlowerPacketReadOriginal(19u*item,root,rawLocalBase,provisional,endpointReceipt);
    if(m8FinePacketCollectEndpoints)
    {
        m8FinePacketEndpointReceipt|=endpointReceipt;
        endpointReceipt=0u;
    }
    return status;
}

bool M8FlowerReadCarrierSitePacket(uint slot,uint local,uint ownerRef,int3 owner,uint flags,
    uint carrier,uint site,bool plus,float2 errors,out M8FlowerPhaseRootEvidence root)
{
    root=(M8FlowerPhaseRootEvidence)0;root.Classification=2u;
    if(m8FinePacketAlternatives!=52u || site>=7u || carrier!=m8FinePacketCarrier ||
        !M8FlowerFinePacketIdentity(slot,local,ownerRef,owner,flags,errors))
    {InterlockedMax(m8FineWriteStatus,M8_FLOWER_ARENA_INVALID);return false;}
    uint address=M8_FINE_PACKET_SITES+10u*(14u*(local-m8FinePacketFirst)+2u*site+(plus?1u:0u));
    uint receipt;
    bool read=M8FlowerPacketReadSite(address,root,receipt);
    M8FlowerRequireEndpointReceipt(receipt);
    return read;
}

// This is the one original-root evaluation call site for phase ancestry,
// the R3 check and skin. Unused speculative alternatives publish no COLD
// receipt. The consumer later requires only its actual cached reads.
void M8FlowerPrepareFinePacket(uint slot,uint lane,uint generation,uint first,uint count,
    uint alternatives,uint singleAlternative,uint carrier,float2 errors)
{
    m8FinePacketSlot=slot;m8FinePacketFirst=first;m8FinePacketCount=count;
    m8FinePacketAlternatives=alternatives;m8FinePacketSingleAlternative=singleAlternative;
    m8FinePacketCarrier=carrier;m8FinePacketErrors=errors;
    m8FinePacketCollectEndpoints=false;m8FinePacketEndpointReceipt=0u;
    if(lane<count)
        m8FineOwner[first+lane]=M8FlowerFindOwner(slot,first+lane,generation);
    GroupMemoryBarrierWithGroupSync();
    [loop]for(uint item=lane;item<count*alternatives;item+=128u)
    {
        uint local=first+item/alternatives;
        if((m8FineState[local]&M8_FLOWER_FINE_ACTIVE)==0u)continue;
        uint alternative=alternatives==52u?item%52u:singleAlternative;
        M8FlowerPhaseRootEvidence root,raw;bool provisional;uint receipt;
        M8FlowerEvaluateOriginalShared(slot,m8FineOwner[local],M8FlowerEndpointOwner(slot,local),
            m8FinePlane[local],alternative>>1u,(alternative&1u)!=0u,errors.x,errors.y,
            root,provisional,raw,receipt);
        M8FlowerPacketStoreOriginal(19u*item,root,raw,provisional,receipt);
    }
    GroupMemoryBarrierWithGroupSync();
}

void M8FlowerPrepareFineSites(uint lane)
{
    if(lane<14u*m8FinePacketCount)
    {
        uint local=m8FinePacketFirst+lane/14u,alternative=lane%14u;
        if((m8FineState[local]&M8_FLOWER_FINE_ACTIVE)!=0u)
        {
            M8FlowerPhaseRootEvidence root;
            m8FinePacketCollectEndpoints=true;m8FinePacketEndpointReceipt=0u;
            bool read=M8FlowerReadL2Knot(m8FinePacketSlot,m8FineOwner[local],
                M8FlowerEndpointOwner(m8FinePacketSlot,local),m8FinePlane[local],
                M8FlowerL2CarrierKnot(m8FinePacketCarrier,alternative>>1u),(alternative&1u)!=0u,
                m8FinePacketErrors.x,m8FinePacketErrors.y,root);
            m8FinePacketCollectEndpoints=false;
            M8FlowerPacketStoreSite(M8_FINE_PACKET_SITES+10u*lane,root,read,m8FinePacketEndpointReceipt);
        }
    }
    GroupMemoryBarrierWithGroupSync();
}

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
    uint2 mask=M8FlowerNodeIncidentPetalsAt(node);
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
        int4 node=M8FlowerNodeAt(task.RootNode);
        task.Offset=node.xyz;task.Line=(uint)node.w;
        task.Kind=task.Line>=9u?1u:0u;
        task.Petal=M8FlowerRootRecordPetal(task.RootNode);
        return task.Petal<48u;
    }
    ordinal-=M8_FLOWER_ROOT_PHASE_TASKS;
    if(ordinal<M8_FLOWER_R2_L1_TASKS)
    {
        task.Strand=ordinal>>1u;
        uint4 strand=M8FlowerStrandAt(task.Strand);
        M8FlowerPhaseFamilyRule family=M8FlowerGetPhaseFamily(task.Strand);
        task.Level=1u;task.RootNode=family.RootNode;
        task.Line=(uint)M8FlowerNodeAt(family.RootNode).w;
        if(M8FlowerLineMetaAt(task.Line).x!=2u)return false;
        task.Offset=M8FlowerNodeAt(strand.x).xyz+M8FlowerNodeAt(strand.y).xyz;
        task.Petal=strand.w&255u;task.Path=family.FinePath0;
        [unroll]for(uint edge=0u;edge<3u;edge++)
            if(M8FlowerPetalStrandsAt(task.Petal)[edge]==task.Strand)
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
        inherited>=0 || M8FlowerLineMetaAt(task.Line).x!=2u)return false;
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
        m8FineState[local]&=~M8_FLOWER_FINE_PRESENCE_MASK;
    }
    GroupMemoryBarrierWithGroupSync();
    int3 direction=M8FlowerLineDirectionAt(task.Line);
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
            M8ObservationRecord record=_M8ObservationRecordsRead[bin.z+index];
            uint recordLocal=record.TileAndKernel&511u;
            // Identity words survive re-emission. A retry correctly skips
            // already-committed R1 and still consumes this immutable fine
            // evidence, independent of positive R1 admission eligibility.
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
                InterlockedOr(m8FineState[local],8u<<M8_FLOWER_FINE_PRESENCE_SHIFT);continue;
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
            if(!M8FlowerPlaneLoopIntervals(normal,delta,relative,
                M8FlowerGeometryLoopRadiusAt(task.Level*13u+task.Line),task.Line,
                normalError,offsetError,task.Level,loopOffset,abc))
            {
                InterlockedOr(m8FineState[local],8u<<M8_FLOWER_FINE_PRESENCE_SHIFT);continue;
            }
            uint tag,classification;M8FlowerInterval2 root;
            if(M8FlowerClassifyPlaneRoot(task.Level,task.Line,false,task.Plus,
                normal,delta,loopOffset,abc,tag,root,classification))
            {
                InterlockedOr(m8FineState[local],2u<<M8_FLOWER_FINE_PRESENCE_SHIFT);
                M8FlowerIntersectRoot(local,tag,root);
            }
            else InterlockedOr(m8FineState[local],
                (classification==M8_FLOWER_ROOT_IMPOSSIBLE?1u:8u)<<M8_FLOWER_FINE_PRESENCE_SHIFT);
        }
    }
    GroupMemoryBarrierWithGroupSync();
}

bool M8FlowerFineReadBucket(uint local,int3 junction,out M8FlowerPhaseRootEvidence root)
{
    root=(M8FlowerPhaseRootEvidence)0;root.Classification=2u;
    uint tag;M8FlowerInterval2 intersection;
    if((m8FineState[local]&M8_FLOWER_FINE_PRESENCE_MASK)!=(2u<<M8_FLOWER_FINE_PRESENCE_SHIFT) ||
        !M8FlowerReadRootIntersection(local,tag,intersection))return false;
    root.Junction=junction;
    root.Tag=tag&(0x1fffu|M8_FLOWER_BOUNDARY_WITNESS_MASK);
    root.Root=intersection;
    root.Classification=1u;
    return M8FlowerPhaseIdentityValid(root);
}

// Returns scheduling status only. AMBIGUOUS metric evidence is a locally
// exhausted candidate, not a GPU-capacity retry and never an invented root.
uint M8FlowerCommitObservedPhase(uint slot,uint local,uint generation,
    M8FlowerGeometryNode task,M8FlowerPhaseRootEvidence observed,
    float normalError,float offsetError)
{
    int3 owner=M8GlobalKernelCoord(slot,local);
    uint ownerRef=m8FineOwner[local],epoch=M8FlowerGetOwnerEpoch(ownerRef);
    M8FlowerPhaseRootEvidence predicted;
    if(!M8FlowerPredictGeometryNode(slot,ownerRef,epoch,owner,m8FinePlane[local],task,
        normalError,offsetError,predicted))return M8_FLOWER_ARENA_OK;
    int r3Orientation=1;
    uint r3Parity=0u,r3Axis=4u;
    if(task.Kind==1u)
    {
        r3Parity=((uint)owner.x&1u)|(((uint)owner.y&1u)<<1u)|(((uint)owner.z&1u)<<2u);
        [unroll]for(uint i=0u;i<4u;i++)
            if(M8FlowerTetraLineAt(r3Parity)[i]==(int)task.Line)r3Axis=i;
        if(r3Axis>=4u)return M8_FLOWER_ARENA_INVALID;
    }
    // R3's wrapper used to inline the same full analysis as the R2 branch.
    // Preserve its axis guard before the one shared ordered evaluation.
    M8FlowerPhaseResidualResult analysis=M8FlowerAnalyzePhaseResidual(predicted,observed);
    if(task.Kind==1u)
    {
        analysis=M8FlowerOrientR3PhaseResidual(analysis,(predicted.Tag>>3u)&15u,r3Parity,r3Axis);
        r3Orientation=M8FlowerTetraEtaAt(r3Parity)[r3Axis];
    }
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
    M8FlowerInterval phase;
    if(task.Kind==0u)
    {
        if(!M8FlowerPreparePhaseRecordSynthesis(predicted,record.Key,record,epoch,phase))
            return M8_FLOWER_ARENA_OK;
    }
    else
    {
        // R3 stores eta*tau. Undo ONLY that generated orientation for the
        // physical root synthesis. A measured metric record is not a
        // declaration of a unique chirally valid junction/petal class.
        phase=M8FlowerDecodePhaseInterval(record.Lower,record.Upper);
        if(r3Orientation<0)phase=M8FlowerI(-phase.hi,-phase.lo);
    }
    // Record admission differs between R2 and R3, but their ordered metric
    // rotation is identical. Keep it at one live call site, not two inlined
    // evaluator copies. Preserve the R2-only finite postcondition verbatim.
    M8FlowerPhaseRootEvidence synthesized,closedRoot;
    if(M8FlowerRotatePhaseEvidence(predicted,phase,synthesized)!=1u ||
        (task.Kind==0u && !M8FlowerFinitePhaseRoot(synthesized.Root)))
        return M8_FLOWER_ARENA_OK;
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

void M8FlowerInvalidationChanged(uint slot,uint generation)
{
    M8MarkTileDirty(slot);
    InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK],4u);
    // A changed ancestor cannot leave this same observation's already
    // advanced phase/skin cursor behind. Unaffected tiles retain theirs.
    if(!M8FlowerStoreTilePendingCursor(slot,generation,_M8ObservationToken,0u))
        M8FlowerFineSchedulingStatus(M8_FLOWER_SIDECAR_STALE_SLOT);
    M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
}

// Every generated relation of this source can require a peer cut, not only
// the ones it holds locally: the peer may own the node^1 phase that predicts
// from the shared original reading this raw endpoint. Its exact endpoint is
// K+Node[node].xyz and its canonical relation is node^1.
// No occupancy gate: OFF may have cleared R1 while its epoch receipt lives.
uint M8FlowerResolveInvalidationPeer(uint local,uint node,out uint peerSlot,out uint peerLocal)
{
    peerSlot=peerLocal=0u;
    int3 relative=int3(local&7u,(local>>3u)&7u,local>>6u)+M8FlowerNodeAt(node).xyz;
    int3 delta=relative>>3;
    if(any(delta < -1) || any(delta > 1))return M8_FLOWER_ARENA_INVALID;
    uint3 index=uint3(delta+1);
    uint halo=index.x+3u*(index.y+3u*index.z);
    uint state=m8FlowerHalo[halo]>>30u;
    if(state==M8_FLOWER_HALO_MISSING)return M8_FLOWER_ARENA_OK;
    if(state==M8_FLOWER_HALO_COLD)
    {
        InterlockedOr(m8FlowerHaloUnresolvedReads,1u<<halo);
        return M8_FLOWER_ARENA_BUSY;
    }
    if(state!=M8_FLOWER_HALO_HOT)return M8_FLOWER_ARENA_INVALID;
    if(!M8FlowerHaloKernel(relative,peerSlot,peerLocal,true))return M8_FLOWER_ARENA_BUSY;
    // Zero is a real slot; return a separate existence bit in this status.
    return 0x100u;
}

void M8FlowerDrainLocalInvalidations(uint slot,uint lane,uint generation,uint stage)
{
    bool gathering=(stage&M8_FLOWER_INVALIDATION_GATHER_STAGE)!=0u;
    bool mutating=(stage&M8_FLOWER_INVALIDATION_PEER_STAGE)!=0u && !gathering;
    [loop]for(uint local=lane;local<512u;local+=128u)
    {
        uint word=M8TileWordIndex(slot,local>>5u),bit=1u<<(local&31u);
        bool structural=(_M8TileBits[word].w&bit)!=0u;
        if(!structural && !gathering)continue;
        uint ownerRef=M8FlowerFindOwner(slot,local,generation);
        uint2 receipt=M8FlowerInvalidationReceipt(ownerRef);
        bool source=receipt.x==_M8ObservationToken &&
            (receipt.y&M8_FLOWER_INVALIDATION_SOURCE)!=0u;
        if(structural)
        {
            // All source captures retired at Commit -> Drain. A structural
            // write must never first advance its epoch during peer mutation.
            if(ownerRef!=0u && (!source || !gathering || mutating))
            {M8FlowerFineSchedulingStatus(M8_FLOWER_ARENA_INVALID);continue;}
            uint status=M8FlowerInvalidateOwner(slot,local,generation,
                _M8DualPublishingGeneration,_M8DualRetiredGeneration);
            if(status!=M8_FLOWER_ARENA_OK)
            {M8FlowerFineSchedulingStatus(status);continue;}
            if(source)M8FlowerAcknowledgeLocalInvalidation(ownerRef,_M8ObservationToken);
            DeviceMemoryBarrier();
            InterlockedAnd(_M8TileBits[word].w,~bit);
            M8FlowerInvalidationChanged(slot,generation);
        }
        else if(source && (receipt.y&M8_FLOWER_INVALIDATION_THROUGH)!=0u &&
            (receipt.y&M8_FLOWER_INVALIDATION_LOCAL_PENDING)!=0u)
        {
            bool changed;
            // The entire source support was certified THROUGH in Commit.
            // Every original R2/R3 relation of that support is invalidated;
            // unrelated R1 and the source owner's epoch remain untouched.
            uint status=M8FlowerInvalidateDependentPhases(ownerRef,_M8ObservationToken,
                M8_FLOWER_INVALIDATION_ROOTS,true,
                _M8DualPublishingGeneration,_M8DualRetiredGeneration,changed);
            M8FlowerFineSchedulingStatus(status);
            if(status==M8_FLOWER_ARENA_OK)
                M8FlowerInvalidationChanged(slot,generation);
        }
    }
    DeviceMemoryBarrierWithGroupSync();
}

// Gather is residency/read-set work only; it never edits another owner.
// Finalize's existing dispatch boundary starts the mutation phase only when
// every touched source completed this gather and its immediate local cut.
void M8FlowerDrainPeerInvalidations(uint slot,uint lane,uint generation,bool mutate)
{
    M8FlowerCacheTileHalo(slot,lane,false,true);
    [loop]for(uint local=lane;local<512u;local+=128u)
    {
        uint ownerRef=M8FlowerFindOwner(slot,local,generation);
        uint2 receipt=M8FlowerInvalidationReceipt(ownerRef);
        // Gather must reach every SOURCE, including one whose own original
        // phase presence is empty: its peer may still hold the node^1 phase
        // predicting from the shared original that reads this raw endpoint.
        // Mutation keeps the existing pending-only gate.
        if(receipt.x!=_M8ObservationToken)continue;
        if((receipt.y&(mutate?M8_FLOWER_INVALIDATION_PEERS_PENDING
            :M8_FLOWER_INVALIDATION_SOURCE))==0u)continue;
        if((receipt.y&M8_FLOWER_INVALIDATION_LOCAL_PENDING)!=0u)
        {M8FlowerFineSchedulingStatus(M8_FLOWER_ARENA_BUSY);continue;}
        uint acknowledged=(_M8FlowerDetailPages.Load(ownerRef+60u)>>2u)&M8_FLOWER_INVALIDATION_ROOTS;
        uint captured=receipt.y&M8_FLOWER_INVALIDATION_ROOTS,discovered=captured;
        // The pre-mutation read-set has two endpoints. Gather probes every
        // generated relation once; mutation walks only what is still owed.
        uint remaining=mutate?(captured&~acknowledged):M8_FLOWER_INVALIDATION_ROOTS;
        [loop]while(remaining!=0u)
        {
            uint dependency=(uint)firstbitlow(remaining);remaining&=remaining-1u;
            uint node=dependency+6u,peerSlot,peerLocal;
            uint status=M8FlowerResolveInvalidationPeer(local,node,peerSlot,peerLocal);
            if(status!=M8_FLOWER_ARENA_OK && status!=0x100u)
            {M8FlowerFineSchedulingStatus(status);continue;}
            bool resident=status==0x100u;
            if(resident)
                _M8TileRecords[M8TileRuntimeIndex(peerSlot)].z=_M8Counters[M8_COUNTER_FRAME_EPOCH];
            if(!mutate)
            {
                if(!resident || (discovered&(1u<<dependency))!=0u)continue;
                uint gatherGeneration=_M8TileRecords[M8TileRuntimeIndex(peerSlot)].w;
                uint gatherRef=M8FlowerFindOwner(peerSlot,peerLocal,gatherGeneration);
                if(gatherRef==0u)continue;
                uint peerRoots;
                // A failed presence read is not proof of absence. Admit the
                // relation instead of stranding a dependent peer detail; this
                // stays per-relation and never becomes an epoch blanket.
                if(!M8FlowerReadOriginalPhasePresence(gatherRef,peerRoots) ||
                    (peerRoots&(1u<<((node^1u)-6u)))!=0u)
                    discovered|=1u<<dependency;
                continue;
            }
            if(resident)
            {
                uint peerGeneration=_M8TileRecords[M8TileRuntimeIndex(peerSlot)].w;
                uint peerRef=M8FlowerFindOwner(peerSlot,peerLocal,peerGeneration);
                bool changed;
                status=M8FlowerInvalidateDependentPhases(peerRef,_M8ObservationToken,
                    1u<<((node^1u)-6u),false,
                    _M8DualPublishingGeneration,_M8DualRetiredGeneration,changed);
                if(status!=M8_FLOWER_ARENA_OK)
                {M8FlowerFineSchedulingStatus(status);continue;}
                if(changed)M8FlowerInvalidationChanged(peerSlot,peerGeneration);
            }
            // MISSING is an actual absent endpoint, not COLD or a failed
            // read. Both it and an atomic successful cut acknowledge exactly
            // this relation. Partial/BUSY cuts never clear the source guard.
            M8FlowerAcknowledgePeerInvalidation(ownerRef,_M8ObservationToken,node);
            M8FlowerInvalidationChanged(slot,generation);
        }
        if(!mutate && discovered!=captured)
        {
            // Retain the completed two-endpoint read-set under this same
            // observation token. Mutation and source retirement then require
            // the peer ACKs that a local-only capture could not have known.
            receipt.y=(receipt.y&~M8_FLOWER_INVALIDATION_ROOTS)|discovered;
            if((discovered&~acknowledged)!=0u)
                receipt.y|=M8_FLOWER_INVALIDATION_PEERS_PENDING;
            DeviceMemoryBarrier();
            _M8FlowerDetailPages.Store2(ownerRef+48u,receipt);
            M8FlowerInvalidationChanged(slot,generation);
        }
    }
    DeviceMemoryBarrierWithGroupSync();
    if(lane==0u && m8FlowerHaloUnresolvedReads!=0u)
    {
        M8FlowerRequestSkinDependencies(m8FlowerHaloUnresolvedReads);
        m8FineWriteStatus=max(m8FineWriteStatus,M8_FLOWER_ARENA_BUSY);
    }
    GroupMemoryBarrierWithGroupSync();
}

// The selected site packet remains immutable while its original-root region
// is recycled for world enclosures and complete seven-child measurements.
uint M8FlowerFineSkinControl(uint ownerIndex)
{
    return M8_FINE_PACKET_SKIN_CONTROL+M8_FINE_PACKET_SKIN_STRIDE*ownerIndex;
}

void M8FlowerFineSkinRoots(uint ownerIndex,out M8FlowerPhaseRootEvidence roots[7])
{
    uint control=M8FlowerFineSkinControl(ownerIndex);
    uint signs=M8FlowerPacketLoadWord(control+3u);
    uint sites=M8FlowerPacketLoadWord(control+10u);
    [loop]for(uint site=0u;site<7u;site++)
    {
        roots[site]=(M8FlowerPhaseRootEvidence)0;
        if((sites&(1u<<site))!=0u)
            roots[site]=M8FlowerPacketLoadRoot(M8_FINE_PACKET_SITES+
                10u*(14u*ownerIndex+2u*site+((signs>>site)&1u)));
    }
}

void M8FlowerFineStoreSkinContext(uint ownerIndex,M8FlowerSkinGroupContext context)
{
    uint address=M8_FINE_PACKET_CONTEXT+M8_FINE_PACKET_CONTEXT_STRIDE*ownerIndex;
    M8FlowerPacketStoreWord(address,context.OwnerRef);
    M8FlowerPacketStoreWord(address+1u,context.Flags);
    M8FlowerPacketStoreWord(address+2u,context.Epoch);
    M8FlowerPacketStoreWord(address+3u,context.GroupBase);
    M8FlowerPacketStoreWord(address+4u,context.SplitBits.x);
    M8FlowerPacketStoreWord(address+5u,context.SplitBits.y);
    M8FlowerPacketStoreWord(address+6u,context.Reserved);
    M8FlowerPacketStoreWord(address+7u,context.Existing?1u:0u);
    M8FlowerPacketStoreWord(address+8u,context.Measure?1u:0u);
    M8FlowerPacketStoreWord(address+9u,context.Inherited.LowerLinearRgba.x);
    M8FlowerPacketStoreWord(address+10u,context.Inherited.LowerLinearRgba.y);
    M8FlowerPacketStoreWord(address+11u,context.Inherited.UpperLinearRgba.x);
    M8FlowerPacketStoreWord(address+12u,context.Inherited.UpperLinearRgba.y);
}

M8FlowerSkinGroupContext M8FlowerFineLoadSkinContext(uint ownerIndex)
{
    uint address=M8_FINE_PACKET_CONTEXT+M8_FINE_PACKET_CONTEXT_STRIDE*ownerIndex;
    M8FlowerSkinGroupContext context;
    context.OwnerRef=M8FlowerPacketLoadWord(address);
    context.Flags=M8FlowerPacketLoadWord(address+1u);
    context.Epoch=M8FlowerPacketLoadWord(address+2u);
    context.GroupBase=M8FlowerPacketLoadWord(address+3u);
    context.SplitBits=uint2(M8FlowerPacketLoadWord(address+4u),M8FlowerPacketLoadWord(address+5u));
    context.Reserved=M8FlowerPacketLoadWord(address+6u);
    context.Existing=M8FlowerPacketLoadWord(address+7u)!=0u;
    context.Measure=M8FlowerPacketLoadWord(address+8u)!=0u;
    context.Inherited.LowerLinearRgba=uint2(M8FlowerPacketLoadWord(address+9u),M8FlowerPacketLoadWord(address+10u));
    context.Inherited.UpperLinearRgba=uint2(M8FlowerPacketLoadWord(address+11u),M8FlowerPacketLoadWord(address+12u));
    return context;
}

void M8FlowerFineStoreWorld(uint index,M8FlowerInterval3 world)
{
    uint address=M8_FINE_PACKET_WORLD_SITES+6u*index;
    M8FlowerPacketStoreWord(address,asuint(world.x.lo));
    M8FlowerPacketStoreWord(address+1u,asuint(world.x.hi));
    M8FlowerPacketStoreWord(address+2u,asuint(world.y.lo));
    M8FlowerPacketStoreWord(address+3u,asuint(world.y.hi));
    M8FlowerPacketStoreWord(address+4u,asuint(world.z.lo));
    M8FlowerPacketStoreWord(address+5u,asuint(world.z.hi));
}

M8FlowerInterval3 M8FlowerFineLoadWorld(uint index)
{
    uint address=M8_FINE_PACKET_WORLD_SITES+6u*index;
    M8FlowerInterval3 world;
    world.x=M8FlowerI(asfloat(M8FlowerPacketLoadWord(address)),asfloat(M8FlowerPacketLoadWord(address+1u)));
    world.y=M8FlowerI(asfloat(M8FlowerPacketLoadWord(address+2u)),asfloat(M8FlowerPacketLoadWord(address+3u)));
    world.z=M8FlowerI(asfloat(M8FlowerPacketLoadWord(address+4u)),asfloat(M8FlowerPacketLoadWord(address+5u)));
    return world;
}

// One tile still owns its cursor. The packet fans out the actual 3*7 child
// footprints and their full measurement rectangles, not just root loading.
// RGB publishes before V, and each parent retires before its descendants.
void M8FlowerDrainSkinPacket(uint slot,uint lane,uint generation,uint carrier,
    uint parentCursor,uint budget,float2 errors)
{
    M8FlowerPrepareFineSites(lane);
    if(lane<m8FinePacketCount)
    {
        uint local=m8FinePacketFirst+lane,control=M8FlowerFineSkinControl(lane);
        uint key=0u,active=0u,unresolved=0u,signs=0u,sites=0u,next=57u;
        if((m8FineState[local]&M8_FLOWER_FINE_ACTIVE)!=0u)
        {
            M8FlowerSymbolRecord symbol;
            M8FlowerPhaseRootEvidence roots[7];float3 positions[7];
            uint classification=M8FlowerClassifyL2Carrier(slot,local,carrier,errors,
                symbol,unresolved,roots,positions);
            if(classification==1u)
            {
                uint source=M8FlowerL2CarrierSource(carrier);
                key=M8FlowerPackL2Key(source&15u,source>>4u,
                    (roots[0].Tag&(1u<<7u))!=0u,(roots[0].Tag>>8u)&31u);
                active=M8FlowerDrawActiveWedgeMask(symbol);
                signs=M8FlowerDrawRootSigns(symbol);sites=1u;next=parentCursor;
                [unroll]for(uint wedge=0u;wedge<6u;wedge++)
                    if((active&(1u<<wedge))!=0u)
                        sites|=(1u<<(1u+wedge))|(1u<<(1u+(wedge+1u)%6u));
                if(next>0u && !M8FlowerSkinHasRootRefinement(slot,local,generation,key))next=57u;
            }
            else if(classification==2u)M8CounterIncrement(M8_COUNTER_REFINEMENT_UNRESOLVED);
        }
        M8FlowerPacketStoreWord(control,key);
        M8FlowerPacketStoreWord(control+1u,active);
        M8FlowerPacketStoreWord(control+2u,unresolved);
        M8FlowerPacketStoreWord(control+3u,signs);
        M8FlowerPacketStoreWord(control+4u,next);
        M8FlowerPacketStoreWord(control+5u,0u);
        M8FlowerPacketStoreWord(control+6u,0u);
        M8FlowerPacketStoreWord(control+7u,0u);
        M8FlowerPacketStoreWord(control+8u,M8_FLOWER_ARENA_OK);
        M8FlowerPacketStoreWord(control+10u,sites);
    }
    // Every classifier has finished using original roots before world sites
    // overwrite the original region. Site roots keep their separate lifetime.
    GroupMemoryBarrierWithGroupSync();
    if(lane<7u*m8FinePacketCount)
    {
        uint ownerIndex=lane/7u,site=lane%7u,control=M8FlowerFineSkinControl(ownerIndex);
        uint sites=M8FlowerPacketLoadWord(control+10u),signs=M8FlowerPacketLoadWord(control+3u);
        M8FlowerInterval3 world=M8FlowerSkinPoint(0.0.xxx);
        bool valid=true;
        if((sites&(1u<<site))!=0u)
        {
            M8FlowerPhaseRootEvidence root=M8FlowerPacketLoadRoot(M8_FINE_PACKET_SITES+
                10u*(14u*ownerIndex+2u*site+((signs>>site)&1u)));
            valid=M8FlowerSkinRootWorld(root,world);
        }
        M8FlowerFineStoreWorld(lane,world);
        M8FlowerPacketStoreWord(M8_FINE_PACKET_WORLD_VALID+lane,valid?1u:0u);
    }
    GroupMemoryBarrierWithGroupSync();
    [loop]while(true)
    {
        if(lane<m8FinePacketCount)
        {
            uint control=M8FlowerFineSkinControl(lane);
            bool ready=M8FlowerPacketLoadWord(control+4u)<57u &&
                M8FlowerPacketLoadWord(control+6u)<budget &&
                M8FlowerPacketLoadWord(control+8u)==M8_FLOWER_ARENA_OK;
            M8FlowerPacketStoreWord(control+16u,ready?1u:0u);
        }
        GroupMemoryBarrierWithGroupSync();
        if(lane==0u)
        {
            uint ready=0u;
            [unroll]for(uint ownerIndex=0u;ownerIndex<3u;ownerIndex++)
                if(ownerIndex<m8FinePacketCount && M8FlowerPacketLoadWord(M8FlowerFineSkinControl(ownerIndex)+16u)!=0u)
                    ready|=1u<<ownerIndex;
            M8FlowerPacketStoreWord(M8_FINE_PACKET_READY,ready);
        }
        GroupMemoryBarrierWithGroupSync();
        uint ready=M8FlowerPacketLoadWord(M8_FINE_PACKET_READY);
        if(ready==0u)break;
        // All lanes execute both signal barriers, including absent/pruned
        // owners. A BUSY RGB writer cannot fall through into V publication.
        [loop]for(uint signal=0u;signal<2u;signal++)
        {
            if(lane<m8FinePacketCount)
            {
                uint control=M8FlowerFineSkinControl(lane),local=m8FinePacketFirst+lane;
                uint classification=M8_FLOWER_SKIN_AMBIGUOUS;
                M8FlowerSkinGroupContext context=(M8FlowerSkinGroupContext)0;
                if((ready&(1u<<lane))!=0u && M8FlowerPacketLoadWord(control+8u)==M8_FLOWER_ARENA_OK)
                {
                    uint next=M8FlowerPacketLoadWord(control+4u),parentOrdinal=0u;
                    if(next>0u && next<8u)parentOrdinal=1u+M8FlowerSkinL3ChildRankAt(next-1u);
                    else if(next>=8u)parentOrdinal=8u+M8FlowerSkinL4ParentThreadAt(next-8u);
                    M8FlowerPacketStoreWord(control+9u,parentOrdinal);
                    if(!M8FlowerSkinParentTouchesWedges(parentOrdinal,M8FlowerPacketLoadWord(control+2u)))
                    {
                        M8FlowerPhaseRootEvidence roots[7];M8FlowerFineSkinRoots(lane,roots);
                        uint status=M8FlowerPrepareSkinGroup(slot,local,generation,
                            M8FlowerPacketLoadWord(control),parentOrdinal,M8FlowerPacketLoadWord(control+1u),
                            roots,signal!=0u,context,classification);
                        M8FlowerPacketStoreWord(control+8u,status);
                        [unroll]for(uint site=0u;site<7u;site++)
                            if(M8FlowerPacketLoadWord(M8_FINE_PACKET_WORLD_VALID+7u*lane+site)==0u)
                                context.Measure=false;
                    }
                }
                M8FlowerFineStoreSkinContext(lane,context);
                M8FlowerPacketStoreWord(control+11u+signal,classification);
            }
            GroupMemoryBarrierWithGroupSync();
            if(lane<7u*m8FinePacketCount)
            {
                uint ownerIndex=lane/7u,child=lane%7u,control=M8FlowerFineSkinControl(ownerIndex);
                M8FlowerSkinGroupContext context=M8FlowerFineLoadSkinContext(ownerIndex);
                uint flags=0u;
                if(context.Measure)
                {
                    M8FlowerInterval3 sites[7];
                    [loop]for(uint site=0u;site<7u;site++)sites[site]=M8FlowerFineLoadWorld(7u*ownerIndex+site);
                    bool supported,certain;
                    uint parentOrdinal=M8FlowerPacketLoadWord(control+9u),active=M8FlowerPacketLoadWord(control+1u);
                    int3 owner=M8FlowerEndpointOwner(slot,m8FinePacketFirst+ownerIndex);
                    if(signal==0u)
                    {
                        M8ThreadColorInterval value;
                        certain=M8FlowerMeasureRgbSkinChild(sites,parentOrdinal,active,child,owner,
                            context.Flags,errors.x,errors.y,context.Inherited,value,supported);
                        uint address=M8_FINE_PACKET_SIGNAL_VALUES+4u*lane;
                        M8FlowerPacketStoreWord(address,value.LowerLinearRgba.x);
                        M8FlowerPacketStoreWord(address+1u,value.LowerLinearRgba.y);
                        M8FlowerPacketStoreWord(address+2u,value.UpperLinearRgba.x);
                        M8FlowerPacketStoreWord(address+3u,value.UpperLinearRgba.y);
                    }
                    else
                    {
                        M8FlowerSkinMetricRun run;
                        run.FlowerKey=M8FlowerPacketLoadWord(control);run.GroupBase=context.GroupBase;
                        run.SplitBitsLo=context.SplitBits.x;run.SplitBitsHi=context.SplitBits.y;
                        run.ParentEpoch=context.Epoch;run.Reserved=context.Reserved;
                        M8FlowerVInterval value;
                        certain=M8FlowerMeasureMetricSkinChild(sites,parentOrdinal,active,child,owner,
                            context.Flags,errors.x,errors.y,run,context.Existing,value,supported);
                        M8FlowerPacketStoreWord(M8_FINE_PACKET_SIGNAL_VALUES+2u*lane,asuint(value.Lower));
                        M8FlowerPacketStoreWord(M8_FINE_PACKET_SIGNAL_VALUES+2u*lane+1u,asuint(value.Upper));
                    }
                    flags=(certain?1u:0u)|(supported?2u:0u);
                }
                M8FlowerPacketStoreWord(M8_FINE_PACKET_SIGNAL_FLAGS+lane,flags);
            }
            GroupMemoryBarrierWithGroupSync();
            if(lane<m8FinePacketCount)
            {
                uint control=M8FlowerFineSkinControl(lane);
                M8FlowerSkinGroupContext context=M8FlowerFineLoadSkinContext(lane);
                if(context.Measure)
                {
                    uint certain=0u,support=0u;
                    [loop]for(uint child=0u;child<7u;child++)
                    {
                        uint flags=M8FlowerPacketLoadWord(M8_FINE_PACKET_SIGNAL_FLAGS+7u*lane+child);
                        if((flags&1u)!=0u)certain|=1u<<child;
                        if((flags&2u)!=0u)support|=1u<<child;
                    }
                    uint classification=M8_FLOWER_SKIN_AMBIGUOUS,status;
                    bool changed;
                    uint local=m8FinePacketFirst+lane,key=M8FlowerPacketLoadWord(control);
                    uint parentOrdinal=M8FlowerPacketLoadWord(control+9u);
                    if(signal==0u)
                    {
                        M8ThreadColorInterval children[7];
                        [loop]for(uint child=0u;child<7u;child++)
                        {
                            uint address=M8_FINE_PACKET_SIGNAL_VALUES+4u*(7u*lane+child);
                            children[child].LowerLinearRgba=uint2(M8FlowerPacketLoadWord(address),M8FlowerPacketLoadWord(address+1u));
                            children[child].UpperLinearRgba=uint2(M8FlowerPacketLoadWord(address+2u),M8FlowerPacketLoadWord(address+3u));
                        }
                        if(certain==127u)classification=M8FlowerClassifyMeasuredRgbSkin(children,certain,support);
                        status=M8FlowerCommitRgbSkinSplit(slot,local,generation,key,parentOrdinal,
                            context,children,classification,support,changed);
                    }
                    else
                    {
                        M8FlowerVInterval children[7];
                        [loop]for(uint child=0u;child<7u;child++)
                        {
                            uint address=M8_FINE_PACKET_SIGNAL_VALUES+2u*(7u*lane+child);
                            children[child].Lower=asint(M8FlowerPacketLoadWord(address));
                            children[child].Upper=asint(M8FlowerPacketLoadWord(address+1u));
                        }
                        if(certain==127u)classification=M8FlowerClassifyMeasuredMetricSkin(children,certain,support);
                        status=M8FlowerCommitMetricSkinSplit(slot,local,generation,key,parentOrdinal,
                            context,children,classification,support,changed);
                    }
                    M8FlowerPacketStoreWord(control+11u+signal,classification);
                    M8FlowerPacketStoreWord(control+8u,status);
                    if(changed)M8FlowerPacketStoreWord(control+5u,M8FlowerPacketLoadWord(control+5u)+1u);
                }
            }
            // The next signal/parent sees this owner's complete group, never
            // a prefix. Its siblings remain independent owner-exclusive jobs.
            DeviceMemoryBarrierWithGroupSync();
        }
        if(lane<m8FinePacketCount && (ready&(1u<<lane))!=0u)
        {
            uint control=M8FlowerFineSkinControl(lane);
            if(M8FlowerPacketLoadWord(control+8u)==M8_FLOWER_ARENA_OK)
            {
                uint next=M8FlowerPacketLoadWord(control+4u)+1u;
                uint ambiguous=(M8FlowerPacketLoadWord(control+11u)==M8_FLOWER_SKIN_AMBIGUOUS?1u:0u)+
                    (M8FlowerPacketLoadWord(control+12u)==M8_FLOWER_SKIN_AMBIGUOUS?1u:0u);
                M8FlowerPacketStoreWord(control+7u,M8FlowerPacketLoadWord(control+7u)+ambiguous);
                M8FlowerPacketStoreWord(control+5u,M8FlowerPacketLoadWord(control+5u)+1u);
                M8FlowerPacketStoreWord(control+6u,M8FlowerPacketLoadWord(control+6u)+1u);
                if(next==1u && !M8FlowerSkinHasRootRefinement(slot,m8FinePacketFirst+lane,generation,
                    M8FlowerPacketLoadWord(control)))next=57u;
                M8FlowerPacketStoreWord(control+4u,next);
            }
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if(lane<m8FinePacketCount)
    {
        uint control=M8FlowerFineSkinControl(lane);
        uint progress=M8FlowerPacketLoadWord(control+5u),ambiguous=M8FlowerPacketLoadWord(control+7u);
        if(progress!=0u)InterlockedAdd(_M8Counters[M8_COUNTER_REFINEMENT_WORK_PROGRESS],progress);
        if(ambiguous!=0u)InterlockedAdd(_M8Counters[M8_COUNTER_REFINEMENT_UNRESOLVED],ambiguous);
        InterlockedMax(m8FineSkinConsumed,min(budget,max(1u,progress)));
        InterlockedMin(m8FineSkinNext,M8FlowerPacketLoadWord(control+4u));
        M8FlowerFineSchedulingStatus(M8FlowerPacketLoadWord(control+8u));
    }
    DeviceMemoryBarrierWithGroupSync();
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
    uint stage=_M8Counters[M8_COUNTER_REFINEMENT_STAGE];
    M8FlowerDrainLocalInvalidations(slot,lane,runtime.w,stage);
    if((stage&M8_FLOWER_INVALIDATION_STAGE_MASK)!=0u && m8FineWriteStatus==0u)
        M8FlowerDrainPeerInvalidations(slot,lane,runtime.w,
            (stage&M8_FLOWER_INVALIDATION_GATHER_STAGE)==0u);
    if(m8FineWriteStatus!=0u)
    {
        if(lane==0u)
        {
            M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
            M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
        }
        return;
    }
    // No phase or skin writer runs between the global gather and peer ACK
    // boundaries. Finalize retains even a failed observation until both cuts
    // retire. This precedes bin.y==0, cursor and evidence-failure early outs.
    if((stage&M8_FLOWER_INVALIDATION_STAGE_MASK)!=0u)return;
    // Even a later coarse allocation/error must not strand an earlier M8
    // structural change with its old fine epoch. Only metric work waits for
    // the complete direct/dual attempt; the epoch transaction runs above.
    if(_M8Counters[M8_COUNTER_OBSERVATION_FAILURE]!=0u ||
        _M8Counters[M8_COUNTER_UNRESOLVED_SURFACE_TILES]!=0u ||
        _M8Counters[M8_COUNTER_UNRESOLVED_OBSERVATION_TILES]!=0u ||
        runtime.x!=_M8ObservationToken)return;
    uint4 bin=_M8ObservationTileBinsRead[slot];
    if(bin.x!=_M8ObservationToken || bin.w!=bin.y ||
        (bin.y!=0u&&(bin.z>=_M8ObservationRecordCapacity ||
            bin.y>_M8ObservationRecordCapacity-bin.z)))return;
    uint cursor=M8FlowerTilePendingCursor(slot,runtime.w,_M8ObservationToken);
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
        if(lane==0u)M8FlowerStoreTilePendingCursor(slot,runtime.w,
            _M8ObservationToken,M8_FLOWER_PHASE_TASKS);
        return;
    }
    M8FlowerCacheTileHalo(slot,lane,false,true);
    float normalError=M8FlowerNext(_M8PlaneErrorBounds.x+M8_FLOWER_NORMAL_QUANTIZATION_UPPER);
    float offsetError=M8FlowerNext(_M8PlaneErrorBounds.y+M8_FLOWER_OFFSET_QUANTIZATION_UPPER);
    uint consumed=0u;
    bool junctionCheck=false;
    [loop]while(junctionCheck || (consumed<_M8RefinementQuantum &&
        (stage<3u?cursor<stageEnd:cursor>=M8_FLOWER_SKIN_CURSOR_BASE)))
    {
        uint mode=junctionCheck?1u:stage==3u?2u:0u;
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
        else if(mode==1u)
            M8FlowerSupportCacheDualTile(M8GlobalKernelCoord(slot,0u)>>3,lane,128u);
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
            M8FlowerPrepareFinePacket(slot,lane,runtime.w,first,count,alternatives,
                singleAlternative,carrier,float2(normalError,offsetError));
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
            else if(mode==1u && lane<count)
            {
                uint local=first+lane;
                if((m8FineState[local]&M8_FLOWER_FINE_ACTIVE)!=0u && m8FineOwner[local]!=0u)
                {
                    M8FlowerJunctionSelection junction=M8FlowerReadR3Junction(slot,m8FineOwner[local],
                        M8GlobalKernelCoord(slot,local),m8FinePlane[local],float2(normalError,offsetError));
                    if(junction.Classification!=1u)M8CounterIncrement(M8_COUNTER_REFINEMENT_UNRESOLVED);
                }
            }
            else if(mode==2u)
                M8FlowerDrainSkinPacket(slot,lane,runtime.w,carrier,parentCursor,budget,float2(normalError,offsetError));
            DeviceMemoryBarrierWithGroupSync();
        }
        if(mode==1u)
        {
            // This is the existing post-root diagnostic consumer. It neither
            // admits a metric record as a junction nor advances another task.
            junctionCheck=false;
            continue;
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
            junctionCheck=cursor==M8_FLOWER_ROOT_PHASE_TASKS;
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
        }
        if(lane==0u)M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
    if(stage==2u && cursor==M8_FLOWER_PHASE_TASKS && m8FineWriteStatus==0u)
        cursor=M8_FLOWER_SKIN_CURSOR_BASE;
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
