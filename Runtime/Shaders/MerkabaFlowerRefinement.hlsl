#ifndef GENESIS_MERKABA_FLOWER_REFINEMENT_INCLUDED
#define GENESIS_MERKABA_FLOWER_REFINEMENT_INCLUDED

#define M8_FLOWER_GEOMETRY_PACKET_READ
#define M8_FLOWER_CARRIER_ALTERNATIVES_READ
// Geometry stays on GPU; signal stages consume the resolver's snapshot items.
#include "MerkabaFlowerGeometry.hlsl"
#include "MerkabaFlowerSupport.hlsl"
#include "MerkabaFlowerSkinObservation.hlsl"
#include "MerkabaFlowerEvidencePacket.hlsl"
#include "MerkabaFlowerSignalItems.hlsl"

// Observation supports address generated relations. A reached bit is only
// workgroup scratch; neither it nor an iteration cursor belongs to the world.
#define M8_FLOWER_FINE_ACTIVE 1u
#define M8_FLOWER_FINE_NOVEL 2u
#define M8_FLOWER_FINE_HAS_PHASE 4u
#define M8_FLOWER_FINE_CHANGED 128u
// Endpoint presence is transient and disjoint from the three owner flags.
// The same owner-local reset/reduction barrier owns both fields.
#define M8_FLOWER_FINE_PRESENCE_SHIFT 3u
#define M8_FLOWER_FINE_PRESENCE_MASK (15u<<M8_FLOWER_FINE_PRESENCE_SHIFT)

groupshared uint m8FineReachedRelations[M8_FLOWER_OBSERVED_RELATION_WORDS];
groupshared uint m8FineObservedCells[16]; // 8^3 L2 cells; not owner/child existence.
groupshared uint m8FineOwner;
groupshared uint m8FineState;
groupshared uint m8FinePlane;
groupshared uint m8FineWriteStatus;
groupshared uint m8FineSignalFirst;
groupshared uint m8FineSignalLast;
groupshared uint m8FineSignalCount;
groupshared uint m8FineSignalFailed;
groupshared uint m8FineAbsentNodes;
groupshared uint4 m8FineDrawMask;
groupshared uint4 m8FineDrawSymbols[128];
groupshared uint m8FineDrawComplete;
groupshared uint m8FineDrawAddress;
groupshared uint2 m8FineOriginalKnown;
groupshared uint2 m8FineOriginalRequired;
groupshared uint m8FineSourceNodes;
groupshared uint m8FineAnchorAlternatives[18];
groupshared uint m8FineAnchorReceipts[18];
groupshared uint2 m8FineCarrierTriples[3];
groupshared M8FlowerSkinMetricFrame m8FineSkinFrames[6];
groupshared uint2 m8FineSkinReached;
groupshared uint m8FineSkinUnboundedWedges;

void M8FlowerLoadObservedHalo(uint slot,uint lane)
{
    if(lane==0u)
    {
        m8FlowerHaloTile=M8GlobalKernelCoord(slot,0u)>>3;
        m8FlowerHaloUnresolvedReads=0u;
    }
    if(lane<M8_FLOWER_HALO_COUNT)
        m8FlowerHalo[lane]=_M8TileHaloRead[slot*M8_FLOWER_HALO_COUNT+lane];
    GroupMemoryBarrierWithGroupSync();
}

// Phase buckets are dead after endpoint1 SEAL, before this packet is filled.
// Reuse their seven existing 512-word banks; no additional shared allocation.
// Geometry retains one owner's requested family (19 words). Signal resolution
// retains that owner's original roots while its reached carriers reuse sites.
// World enclosures/validity must not alias originals used by the next carrier.
#define M8_FINE_PACKET_SITES 2964u
#define M8_FINE_PACKET_SKIN_CONTROL 3384u
#define M8_FINE_PACKET_WORLD_SITES 2500u
#define M8_FINE_PACKET_WORLD_VALID 2544u

static uint m8FinePacketSlot;
static uint m8FinePacketLocal;
static uint m8FinePacketCarrier;
static float2 m8FinePacketErrors;
static bool m8FinePacketCollectEndpoints;
static uint m8FinePacketEndpointReceipt;

// Exactly the mapping the seven-way switch performed, including its default
// arm: banks at or above six aliased onto the representative bank, so the
// index is min(address>>9,6)*512 + (address&511), not a flat address mask.
uint M8FlowerPacketAddress(uint address)
{
    return min(address >> 9u, 6u) * 512u + (address & 511u);
}

uint M8FlowerPacketLoadWord(uint address)
{
    return m8FlowerReductionBank[M8FlowerPacketAddress(address)];
}

void M8FlowerPacketStoreWord(uint address,uint value)
{
    m8FlowerReductionBank[M8FlowerPacketAddress(address)] = value;
}

bool M8FlowerFinePacketIdentity(uint slot,uint local,uint ownerRef,int3 owner,uint flags,float2 errors)
{
    return slot==m8FinePacketSlot && local==m8FinePacketLocal && ownerRef==m8FineOwner &&
        flags==m8FinePlane && all(owner==M8FlowerEndpointOwner(slot,local)) &&
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
        (m8FineOriginalKnown[min(alternative>>5u,1u)]&(1u<<(alternative&31u)))==0u)
    {InterlockedMax(m8FineWriteStatus,M8_FLOWER_ARENA_INVALID);return 2u;}
    uint status=M8FlowerPacketReadOriginal(19u*alternative,root,rawLocalBase,provisional,endpointReceipt);
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
    if(site>=7u || carrier!=m8FinePacketCarrier ||
        !M8FlowerFinePacketIdentity(slot,local,ownerRef,owner,flags,errors))
    {InterlockedMax(m8FineWriteStatus,M8_FLOWER_ARENA_INVALID);return false;}
    uint address=M8_FINE_PACKET_SITES+10u*(2u*site+(plus?1u:0u));
    uint receipt;
    bool read=M8FlowerPacketReadSite(address,root,receipt);
    M8FlowerRequireEndpointReceipt(receipt);
    return read;
}

void M8FlowerBeginFinePacket(uint slot,uint lane,uint generation,uint local,float2 errors)
{
    m8FinePacketSlot=slot;m8FinePacketLocal=local;m8FinePacketErrors=errors;
    m8FinePacketCollectEndpoints=false;m8FinePacketEndpointReceipt=0u;
    if(lane==0u)
    {
        m8FineOwner=M8FlowerFindOwner(slot,local,generation);
        m8FineOriginalKnown=0u;m8FineOriginalRequired=0u;
    }
    GroupMemoryBarrierWithGroupSync();
}

void M8FlowerRequireFineOriginal(uint alternative)
{
    if(alternative<52u)
        InterlockedOr(m8FineOriginalRequired[alternative>>5u],1u<<(alternative&31u));
}

// One lane per newly requested relation. Known means evaluated in this exact
// owner/plane packet, not CERTAIN; absent and unresolved roots keep their full
// original evidence too. Only an actual consumer forwards a COLD receipt.
void M8FlowerAcquireFineOriginals(uint lane)
{
    GroupMemoryBarrierWithGroupSync();
    uint2 pending=m8FineOriginalRequired&~m8FineOriginalKnown;
    if(lane<52u && (pending[lane>>5u]&(1u<<(lane&31u)))!=0u)
    {
        M8FlowerPhaseRootEvidence root,raw;bool provisional;uint receipt;
        M8FlowerEvaluateOriginalShared(m8FinePacketSlot,m8FineOwner,
            M8FlowerEndpointOwner(m8FinePacketSlot,m8FinePacketLocal),
            m8FinePlane,lane>>1u,(lane&1u)!=0u,m8FinePacketErrors.x,m8FinePacketErrors.y,
            root,provisional,raw,receipt);
        M8FlowerPacketStoreOriginal(19u*lane,root,raw,provisional,receipt);
    }
    GroupMemoryBarrierWithGroupSync();
    if(lane==0u)m8FineOriginalKnown|=pending;
    GroupMemoryBarrierWithGroupSync();
}

void M8FlowerRequireFineSite(uint site,bool plus)
{
    uint knot=M8FlowerL2CarrierKnot(m8FinePacketCarrier,site);
    uint first=M8FlowerL2IncidenceOffsetsAt(knot),end=M8FlowerL2IncidenceOffsetsAt(knot+1u);
    uint epoch=M8FlowerGetOwnerEpoch(m8FineOwner);
    [loop]for(uint incidence=first;incidence<end;incidence++)
    {
        M8FlowerGeometryNode node;
        if(M8FlowerL2GeometryNodeFromSource(M8FlowerL2IncidenceSourcesAt(incidence),plus,node))
            M8FlowerRequireFineOriginal(M8FlowerGeometryOriginalDependency(m8FineOwner,epoch,node));
    }
}

void M8FlowerPrepareFineSites(uint lane)
{
    if(lane<14u)
    {
        if((m8FineState&M8_FLOWER_FINE_ACTIVE)!=0u)
        {
            M8FlowerPhaseRootEvidence root;
            m8FinePacketCollectEndpoints=true;m8FinePacketEndpointReceipt=0u;
            bool read=M8FlowerReadL2Knot(m8FinePacketSlot,m8FineOwner,
                M8FlowerEndpointOwner(m8FinePacketSlot,m8FinePacketLocal),m8FinePlane,
                M8FlowerL2CarrierKnot(m8FinePacketCarrier,lane>>1u),(lane&1u)!=0u,
                m8FinePacketErrors.x,m8FinePacketErrors.y,root);
            m8FinePacketCollectEndpoints=false;
            M8FlowerPacketStoreSite(M8_FINE_PACKET_SITES+10u*lane,root,read,m8FinePacketEndpointReceipt);
        }
    }
    GroupMemoryBarrierWithGroupSync();
}

// Eighteen independent source incidences, then 6*8 independent wedge/sign
// predicates. Only the deterministic combination/publication is scalar.
// Both stages consume the same acquired roots, never another geometry solve.
void M8FlowerPrepareFineAlternatives(uint lane)
{
    uint slot=m8FinePacketSlot,local=m8FinePacketLocal,carrier=m8FinePacketCarrier;
    int3 owner=M8FlowerEndpointOwner(slot,local);
    if(lane<3u)m8FineCarrierTriples[lane]=0u;
    if(lane<18u)
    {
        uint petal=M8FlowerL2WedgeAt(6u*carrier+lane/3u).y>>4u;
        uint available,uncertain,direct;
        m8FinePacketCollectEndpoints=true;m8FinePacketEndpointReceipt=0u;
        M8FlowerSourceAnchorAlternatives(slot,m8FineOwner,owner,m8FinePlane,
            petal,lane%3u,m8FinePacketErrors,available,uncertain,direct);
        m8FinePacketCollectEndpoints=false;
        m8FineAnchorAlternatives[lane]=available|(uncertain<<2u)|(direct<<4u);
        m8FineAnchorReceipts[lane]=m8FinePacketEndpointReceipt;
    }
    GroupMemoryBarrierWithGroupSync();
    if(lane<6u)
    {
        // Match the source predicate's ordered early-out. A later independent
        // lane's speculative COLD anchor is not this wedge's dependency.
        [loop]for(uint anchor=0u;anchor<3u;anchor++)
        {
            uint index=3u*lane+anchor;
            M8FlowerRequireEndpointReceipt(m8FineAnchorReceipts[index]);
            if((m8FineAnchorAlternatives[index]&15u)==0u)break;
        }
    }
    if(lane<48u)
    {
        uint wedge=lane>>3u,triple=lane&7u;
        uint3 anchors=uint3(m8FineAnchorAlternatives[3u*wedge],
            m8FineAnchorAlternatives[3u*wedge+1u],m8FineAnchorAlternatives[3u*wedge+2u]);
        float3 normal;float delta;M8FlowerUnpackPlane(m8FinePlane,normal,delta);
        uint result=M8FlowerClassifyCarrierTriple(slot,local,m8FineOwner,owner,m8FinePlane,
            carrier,wedge,triple,m8FinePacketErrors,normal,anchors&3u,(anchors>>2u)&3u,(anchors>>4u)&3u);
        uint word=wedge>>2u,bit=1u<<(8u*(wedge&3u)+triple);
        if((result&1u)!=0u)InterlockedOr(m8FineCarrierTriples[0][word],bit);
        if((result&2u)!=0u)InterlockedOr(m8FineCarrierTriples[1][word],bit);
        if((result&4u)!=0u)InterlockedOr(m8FineCarrierTriples[2][word],bit);
    }
    GroupMemoryBarrierWithGroupSync();
}

void M8FlowerReadCarrierAlternatives(uint slot,uint local,uint carrier,float2 errors,
    out uint2 certain,out uint2 uncertain,out uint2 direct)
{
    certain=uncertain=direct=0u;
    if(slot!=m8FinePacketSlot || local!=m8FinePacketLocal || carrier!=m8FinePacketCarrier ||
        any(asuint(errors)!=asuint(m8FinePacketErrors)))
    {InterlockedMax(m8FineWriteStatus,M8_FLOWER_ARENA_INVALID);return;}
    certain=m8FineCarrierTriples[0];uncertain=m8FineCarrierTriples[1];direct=m8FineCarrierTriples[2];
}

// Missing direct peers use the existing SSD tile queue, without excavation.
void M8FlowerRequestSkinDependencies(uint neededHalo)
{
    [loop] while (neededHalo != 0u)
    {
        uint halo=(uint)firstbitlow(neededHalo);
        neededHalo &= neededHalo-1u;
        if (halo >= M8_FLOWER_HALO_COUNT) continue;
        if ((m8FlowerHalo[halo]>>30u) != M8_FLOWER_HALO_COLD)
        { M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY); continue; }
        M8FlowerRequestErasePeer(halo);
    }
}

M8FlowerGeometryNode M8FlowerObservedRelation(uint level,uint relation,bool plus)
{
    uint index=M8FlowerObservedRelationLevelsAt(level).x+relation;
    uint4 address=M8FlowerObservedRelationsAt(2u*index);
    uint key=M8FlowerObservedRelationsAt(2u*index+1u).x;
    M8FlowerGeometryNode node=(M8FlowerGeometryNode)0;
    node.Level=level;node.Offset=asint(address.xyz);
    node.Line=(address.w>>2u)&15u;node.Strand=(address.w>>6u)&127u;
    node.Petal=key&63u;node.Path=(key>>6u)&15u;
    node.RootNode=(key>>10u)&31u;node.Kind=(key>>15u)&1u;
    node.ParentContext=(key>>16u)&7u;node.KnotSite=(key>>19u)&7u;
    node.Plus=plus;
    return node;
}

void M8FlowerReachObservedRelations(uint level,int3 owner,float3 world)
{
    uint4 table=M8FlowerObservedRelationLevelsAt(level);
    uint cellIndex=0u;
    if(level!=0u)
    {
        precise float3 grid=mul(_MerkabaWorldToGrid,float4(world,1.0)).xyz;
        precise float3 relative=grid-float3(owner)*M8_FLOWER_LATTICE_STEP;
        float step=M8FlowerLevelStep(level);
        int half=1<<(int)level;
        // Correct the division hint using the SAME ordered products as the
        // endpoint-support predicate. No epsilon, shifted point or clamp.
        int3 cell=(int3)floor(relative/step);
        cell-=int3(relative<float3(cell)*step);
        cell+=int3(relative>=float3(cell+1)*step);
        if(any(cell< -half)||any(cell>=half))return;
        uint3 local=(uint3)(cell+half);
        cellIndex=local.x+table.w*(local.y+table.w*local.z);
    }
    InterlockedOr(m8FineObservedCells[cellIndex>>5u],1u<<(cellIndex&31u));
}

void M8FlowerExpandObservedCells(uint level,uint lane)
{
    uint4 table=M8FlowerObservedRelationLevelsAt(level);
    // Each occupied support cell is expanded once, regardless of the number
    // of source pixels in it. Lanes own different cells; no per-pixel walk of
    // the relation alphabet and no CPU-produced work list.
    uint cells=table.w*table.w*table.w;
    [loop]for(uint cell=lane;cell<cells;cell+=128u)
    {
        if((m8FineObservedCells[cell>>5u]&(1u<<(cell&31u)))==0u)continue;
        uint4 cover=M8FlowerObservedRelationCellsAt(table.z+cell);
        [loop]for(uint item=0u;item<cover.y;item++)
        {
            uint relation=M8FlowerObservedRelationReferencesAt(cover.x+item);
            InterlockedOr(m8FineReachedRelations[relation>>5u],1u<<(relation&31u));
        }
    }
    GroupMemoryBarrierWithGroupSync();
}

// Each generated relation has exactly two endpoints. Grouped observation
// ranges address either endpoint without walking its whole tile/halo bins.
void M8FlowerReduceFineEndpoint(uint4 measured,uint lane,M8FlowerGeometryNode task,
    uint endpoint,float normalError,float offsetError)
{
    if(lane==0u)
    {
        M8FlowerResetObservationBucket(0u);
        m8FineState&=~M8_FLOWER_FINE_PRESENCE_MASK;
    }
    GroupMemoryBarrierWithGroupSync();
    uint slot=measured.x>>9u,local=measured.x&511u;
    int3 direction=M8FlowerLineDirectionAt(task.Line);
    int3 endpointLocal=(task.Offset-direction)/2+(endpoint==0u?int3(0,0,0):direction);
    uint4 source=measured;
    uint sourceSlot=slot,sourceLocal=local;
    bool available=true;
    if(task.Level==0u && any(endpointLocal!=0))
    {
        int3 origin=int3(local&7u,(local>>3u)&7u,local>>6u);
        available=M8FlowerHaloKernel(origin+endpointLocal,sourceSlot,sourceLocal,true,true);
        if(available)
        {
            // Untouched slots have no evidence in this snapshot, even when a
            // reused observation token could match old scratch after NEW/OPEN.
            uint4 bin=_M8ObservationTileBinsRead[sourceSlot];
            available=bin.x==_M8ObservationToken && bin.y!=0u && bin.w==bin.y &&
                M8FlowerFindMeasuredOwner(sourceSlot,sourceLocal,
                    _M8TileRecords[M8TileRuntimeIndex(sourceSlot)].w,_M8ObservationToken,source);
            if(available)available=source.y>=bin.z && source.y-bin.z<bin.y &&
                source.z<=bin.y-(source.y-bin.z);
        }
    }
    if(available)
    {
        int3 sourceOwner=M8GlobalKernelCoord(sourceSlot,sourceLocal);
        [loop]for(uint index=lane;index<source.z;index+=128u)
        {
            uint recordIndex=M8FlowerMeasuredRecordIndex(source.y,index);
            M8ObservationRecord record=_M8ObservationRecordsRead[recordIndex];
            uint plane;float3 world;
            if(!M8FlowerMeasurement(record.SourcePixel,sourceOwner,plane,world))continue;
            if((plane&M8_FLOWER_PLANE_FREE_SIDE)!=(m8FinePlane&M8_FLOWER_PLANE_FREE_SIDE))
            {InterlockedOr(m8FineState,8u<<M8_FLOWER_FINE_PRESENCE_SHIFT);continue;}
            if(task.Level!=0u)
            {
                precise float3 grid=mul(_MerkabaWorldToGrid,float4(world,1.0)).xyz;
                precise float3 relative=grid-float3(sourceOwner)*M8_FLOWER_LATTICE_STEP;
                float step=M8FlowerLevelStep(task.Level);
                float3 lower=float3(endpointLocal-1)*step,upper=float3(endpointLocal+1)*step;
                if(any(relative<lower)||any(relative>=upper))continue;
            }
            float3 normal;float delta;M8FlowerUnpackPlane(plane,normal,delta);
            int3 loopOffset=task.Offset;
            if(task.Level==0u)loopOffset-=2*endpointLocal;
            precise float3 relative=(0.5*M8FlowerLevelStep(task.Level))*float3(task.Offset);
            if(task.Level==0u)relative-=float3(endpointLocal)*M8_FLOWER_LATTICE_STEP;
            M8FlowerInterval3 abc;
            if(!M8FlowerPlaneLoopIntervals(normal,delta,relative,
                M8FlowerGeometryLoopRadiusAt(task.Level*13u+task.Line),task.Line,
                normalError,offsetError,task.Level,loopOffset,abc))
            {InterlockedOr(m8FineState,8u<<M8_FLOWER_FINE_PRESENCE_SHIFT);continue;}
            uint tag,classification;M8FlowerInterval2 root;
            if(M8FlowerClassifyPlaneRoot(task.Level,task.Line,false,task.Plus,
                normal,delta,loopOffset,abc,tag,root,classification))
            {
                InterlockedOr(m8FineState,2u<<M8_FLOWER_FINE_PRESENCE_SHIFT);
                M8FlowerIntersectRoot(0u,tag,root);
            }
            else InterlockedOr(m8FineState,
                (classification==M8_FLOWER_ROOT_IMPOSSIBLE?1u:8u)<<M8_FLOWER_FINE_PRESENCE_SHIFT);
        }
    }
    GroupMemoryBarrierWithGroupSync();
}

bool M8FlowerFineReadBucket(int3 junction,out M8FlowerPhaseRootEvidence root)
{
    root=(M8FlowerPhaseRootEvidence)0;root.Classification=2u;
    uint tag;M8FlowerInterval2 intersection;
    if((m8FineState&M8_FLOWER_FINE_PRESENCE_MASK)!=(2u<<M8_FLOWER_FINE_PRESENCE_SHIFT) ||
        !M8FlowerReadRootIntersection(0u,tag,intersection))return false;
    root.Junction=junction;root.Tag=tag&(0x1fffu|M8_FLOWER_BOUNDARY_WITNESS_MASK);
    root.Root=intersection;root.Classification=1u;
    return M8FlowerPhaseIdentityValid(root);
}

// Returns storage status only. AMBIGUOUS metric evidence is a locally
// exhausted candidate, not a GPU-capacity retry and never an invented root.
uint M8FlowerCommitObservedPhase(uint slot,uint local,uint generation,
    M8FlowerGeometryNode task,M8FlowerPhaseRootEvidence observed,
    float normalError,float offsetError)
{
    int3 owner=M8GlobalKernelCoord(slot,local);
    uint ownerRef=m8FineOwner,epoch=M8FlowerGetOwnerEpoch(ownerRef);
    M8FlowerPhaseRootEvidence predicted;
    if(!M8FlowerPredictGeometryNode(slot,ownerRef,epoch,owner,m8FinePlane,task,
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
                    _M8WorldPublishingGeneration,_M8WorldRetiredGeneration);
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
        status=M8FlowerEnsureOwner(slot,local,generation,m8FinePlane,
            _M8WorldPublishingGeneration,ownerRef);
        if(status!=M8_FLOWER_ARENA_OK)return status;
        M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
    m8FineOwner=ownerRef;epoch=M8FlowerGetOwnerEpoch(ownerRef);
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
    // Identical stored evidence needs no allocator transaction. Contention
    // is a local storage result, never a retained refinement program.
    M8FlowerDetailRecord previous;
    if(M8FlowerFindPhase(ownerRef,record.Key,epoch,previous) &&
        previous.Lower==record.Lower && previous.Upper==record.Upper)
        return M8_FLOWER_ARENA_OK;
    status=M8FlowerCommitPhase(ownerRef,record,_M8WorldPublishingGeneration,_M8WorldRetiredGeneration);
    if(status==M8_FLOWER_ARENA_OK)
    {
        m8FineState|=M8_FLOWER_FINE_NOVEL|M8_FLOWER_FINE_CHANGED;
        M8MarkTileDirty(slot);
        InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK],4u);
        M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
    return status;
}

void M8FlowerRecordFineWriteStatus(uint status)
{
    if(status!=M8_FLOWER_ARENA_OK)InterlockedMax(m8FineWriteStatus,status);
}


// Original roots, selected sites and world enclosures have disjoint ranges.
uint M8FlowerFineSkinControl()
{
    return M8_FINE_PACKET_SKIN_CONTROL;
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

void M8FlowerReachFineSkin(uint4 measured,uint lane,float2 errors)
{
    uint slot=measured.x>>9u,local=measured.x&511u;
    uint active=M8FlowerPacketLoadWord(M8FlowerFineSkinControl()+1u);
    if(lane==0u){m8FineSkinReached=0u;m8FineSkinUnboundedWedges=0u;}
    GroupMemoryBarrierWithGroupSync();
    if(active==0u || m8FlowerHaloUnresolvedReads!=0u)return;
    if(lane<6u && (active&(1u<<lane))!=0u)
    {
        uint3 sites=M8FlowerSkinWedgeSites(lane);
        M8FlowerInterval3 support[3];
        support[0]=M8FlowerFineLoadWorld(sites.x);
        support[1]=M8FlowerFineLoadWorld(sites.y);
        support[2]=M8FlowerFineLoadWorld(sites.z);
        M8FlowerSkinMetricFrame frame;
        if(!M8FlowerSkinChartFrameFromTriangle(support,frame))
            InterlockedOr(m8FineSkinUnboundedWedges,1u<<lane);
        m8FineSkinFrames[lane]=frame;
    }
    GroupMemoryBarrierWithGroupSync();
    uint bounded=active&~m8FineSkinUnboundedWedges;
    int3 owner=M8FlowerEndpointOwner(slot,local);
    [loop]for(uint index=lane;index<measured.z;index+=128u)
    {
        M8ObservationRecord record=_M8ObservationRecordsRead[M8FlowerMeasuredRecordIndex(measured.y,index)];
        int2 pixel=int2(record.SourcePixel%gsDepthTexSize.x,record.SourcePixel/gsDepthTexSize.x);
        M8FlowerInterval depth;M8FlowerInterval3 normal,world;
        if(!M8FlowerSkinMetricMeasurement(pixel,owner,m8FinePlane,errors.x,errors.y,depth,normal) ||
            !M8FlowerSkinDepthCell(pixel,depth,true,world))
        {
            // Metric projection is not RGB admission. If its enclosure is
            // unavailable, retain possible signal work for both consumers;
            // each will apply its own complete-support measurement predicate.
            InterlockedOr(m8FineSkinUnboundedWedges,active);continue;
        }
        uint wedges=bounded;
        [loop]while(wedges!=0u)
        {
            uint wedge=(uint)firstbitlow(wedges);wedges&=wedges-1u;
            M8FlowerInterval3 barycentric;uint2 reached;
            if(!M8FlowerSkinMetricCoordinates(m8FineSkinFrames[wedge],world,barycentric) ||
                !M8FlowerSkinFootprintParents(barycentric,wedge,reached))
                InterlockedOr(m8FineSkinUnboundedWedges,1u<<wedge);
            else
            {
                InterlockedOr(m8FineSkinReached.x,reached.x);
                InterlockedOr(m8FineSkinReached.y,reached.y);
            }
        }
    }
    GroupMemoryBarrierWithGroupSync();
    // A failed enclosure cannot prove that a footprint misses a child. Retain
    // that wedge's generated possible groups; this is selection only, never
    // new certainty. Independent lanes read finite support masks, not signals.
    if(lane<57u && (M8FlowerSkinParentWorkAt(lane).w&m8FineSkinUnboundedWedges)!=0u)
        InterlockedOr(m8FineSkinReached[lane>>5u],1u<<(lane&31u));
    GroupMemoryBarrierWithGroupSync();
}


// Geometry is evaluated once. Only resolved, identity-checked carriers append
// an item; the two signal stages consume its immutable world enclosures.
void M8FlowerResolveSkinCarrier(uint4 measured,uint lane,uint carrier,
    float2 errors)
{
    uint slot=measured.x>>9u,generation=measured.w;
    M8FlowerPrepareFineSites(lane);
    M8FlowerPrepareFineAlternatives(lane);
    if(lane==0u)
    {
        uint local=m8FinePacketLocal,control=M8FlowerFineSkinControl();
        uint key=0u,active=0u,unresolved=0u,signs=0u,sites=0u;
        if((m8FineState&M8_FLOWER_FINE_ACTIVE)!=0u)
        {
            M8FlowerSymbolRecord symbol;
            M8FlowerPhaseRootEvidence roots[7];float3 positions[7];
            uint classification=M8FlowerClassifyL2Carrier(slot,local,carrier,errors,
                symbol,unresolved,roots,positions);
            if(classification==1u)
            {
                if(_M8FlowerOwnerCacheEnabled!=0u)
                {
                    m8FineDrawSymbols[carrier]=uint4(symbol.OwnerAndCarrier,symbol.RootsAndWedges,
                        symbol.DetailRef,0xffffffffu);
                    m8FineDrawMask[carrier>>5u]|=1u<<(carrier&31u);
                }
                uint source=M8FlowerL2CarrierSource(carrier);
                key=M8FlowerPackL2Key(source&15u,source>>4u,
                    (roots[0].Tag&(1u<<7u))!=0u,(roots[0].Tag>>8u)&31u);
                active=M8FlowerDrawActiveWedgeMask(symbol);
                signs=M8FlowerDrawRootSigns(symbol);sites=1u;
                [unroll]for(uint wedge=0u;wedge<6u;wedge++)
                    if((active&(1u<<wedge))!=0u)
                        sites|=(1u<<(1u+wedge))|(1u<<(1u+(wedge+1u)%6u));
                if(!M8FlowerSkinCarrierIdentity(M8GlobalKernelCoord(slot,local),key,active,roots))
                {active=0u;sites=0u;M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);}
            }
            else if(classification==2u)M8CounterIncrement(M8_COUNTER_REFINEMENT_UNRESOLVED);
        }
        M8FlowerPacketStoreWord(control,key);
        M8FlowerPacketStoreWord(control+1u,active);
        M8FlowerPacketStoreWord(control+2u,unresolved);
        M8FlowerPacketStoreWord(control+3u,signs);
        M8FlowerPacketStoreWord(control+10u,sites);
    }
    // Selected roots and world enclosures have disjoint shared lifetimes.
    // The owner's original roots remain reusable by its other reached carriers.
    GroupMemoryBarrierWithGroupSync();
    if(lane<7u)
    {
        uint site=lane,control=M8FlowerFineSkinControl();
        uint sites=M8FlowerPacketLoadWord(control+10u),signs=M8FlowerPacketLoadWord(control+3u);
        M8FlowerInterval3 world=M8FlowerSkinPoint(0.0.xxx);
        bool valid=true;
        if((sites&(1u<<site))!=0u)
        {
            M8FlowerPhaseRootEvidence root=M8FlowerPacketLoadRoot(M8_FINE_PACKET_SITES+
                10u*(2u*site+((signs>>site)&1u)));
            valid=M8FlowerSkinRootWorld(root,world);
        }
        M8FlowerFineStoreWorld(lane,world);
        M8FlowerPacketStoreWord(M8_FINE_PACKET_WORLD_VALID+lane,valid?1u:0u);
    }
    GroupMemoryBarrierWithGroupSync();
    M8FlowerReachFineSkin(measured,lane,errors);
    if(lane==0u)
    {
        uint local=m8FinePacketLocal,control=M8FlowerFineSkinControl();
        uint siteValid=0u;
        [unroll]for(uint site=0u;site<7u;site++)
            if(M8FlowerPacketLoadWord(M8_FINE_PACKET_WORLD_VALID+site)!=0u)
                siteValid|=1u<<site;
        uint sites=M8FlowerPacketLoadWord(control+10u),active=M8FlowerPacketLoadWord(control+1u);
        if(active!=0u && (siteValid&sites)==sites && m8FlowerHaloUnresolvedReads==0u &&
            any(m8FineSkinReached!=0u))
        {
            uint item;
            if(M8FlowerReserveSignalItem(item))
            {
                _M8FlowerSignalItems.Store4(item+M8_FLOWER_SIGNAL_SOURCE,
                    uint4((slot<<9u)|local,generation,_M8ObservationToken,m8FinePlane));
                _M8FlowerSignalItems.Store4(item+M8_FLOWER_SIGNAL_SYMBOL,
                    uint4(M8FlowerPacketLoadWord(control),active,
                        M8FlowerPacketLoadWord(control+2u),siteValid));
                _M8FlowerSignalItems.Store4(item+M8_FLOWER_SIGNAL_REACH,
                    uint4(sites,0u,M8FlowerGetOwnerEpoch(m8FineOwner),0u));
                _M8FlowerSignalItems.Store2(item+M8_FLOWER_SIGNAL_PARENTS,m8FineSkinReached);
                [loop]for(uint word=0u;word<42u;word++)
                    _M8FlowerSignalItems.Store(item+M8_FLOWER_SIGNAL_WORLD+4u*word,
                        M8FlowerPacketLoadWord(M8_FINE_PACKET_WORLD_SITES+word));
                // One resolver WG owns this owner; no concurrent run-header
                // writers are introduced when the signal work is dispatched.
                if(m8FineSignalCount==0u)m8FineSignalFirst=item;
                else _M8FlowerSignalItems.Store(m8FineSignalLast+M8_FLOWER_SIGNAL_NEXT,item);
                m8FineSignalLast=item;m8FineSignalCount++;
            }
            else m8FineSignalFailed=1u;
        }
    }
    GroupMemoryBarrierWithGroupSync();
}

groupshared M8FlowerSkinGroupContext m8SignalContext;
groupshared uint4 m8SignalValues[7];
groupshared uint m8SignalFlags[7];
groupshared uint2 m8SignalParents;
groupshared uint m8SignalStatus;

#include "MerkabaFlowerSignalBody.hlsl"

#include "MerkabaFlowerDrainBody.hlsl"

#include "MerkabaFlowerResolveSignals.hlsl"

[numthreads(128,1,1)]
void IntegrateFlowerRoot(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    M8FlowerDrainGeometryBody(group,lane,0u);
}

// Original shared roots read the peer's L0 phase, hence the preceding global
// Root barrier is required. Child transport reads only this owner's L1/L2
// records (M8FlowerApplyGeometryDetail); peer acquisition reads Level=0.
// Consequently the L1 -> L2 edge is owner-local, not a global dispatch edge.
[numthreads(128,1,1)]
void IntegrateFlowerChildren(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    [loop] for (uint level=1u; level<=2u; ++level)
    {
        M8FlowerDrainGeometryBody(group,lane,level);
        DeviceMemoryBarrierWithGroupSync();
    }
}

[numthreads(128,1,1)]
void ResolveFlowerCarriers(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    M8FlowerResolveCarriersBody(group,lane);
}

[numthreads(64,1,1)]
void IntegrateFlowerSkin(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    // Keep one writer per owner across both arenas; parallel RGB/V groups
    // could race creation of their common sparse owner epoch.
    [loop] for (uint signal=0u; signal<2u; ++signal)
    {
        M8FlowerIntegrateSkin(group,lane,signal!=0u);
        DeviceMemoryBarrierWithGroupSync();
    }
}

#endif
