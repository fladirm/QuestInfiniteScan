#ifndef GENESIS_MERKABA_FLOWER_GEOMETRY_INCLUDED
#define GENESIS_MERKABA_FLOWER_GEOMETRY_INCLUDED

#include "MerkabaFlowerSidecar.hlsl"
#if defined(MERKABA_WORLD_INCLUDED)
#include "MerkabaFlowerTileHalo.hlsl"
#endif

// Read-only evaluation shared by frozen refinement and procedural consumers.
// Generated incidence selects every ancestor; no XYZ or adjacency is stored.
struct M8FlowerGeometryNode
{
    uint Level;
    int3 Offset;
    uint Line;
    uint Petal;
    uint Path;
    uint Strand;
    uint ParentContext;
    uint KnotSite;
    uint RootNode;
    uint Kind;
    bool Plus;
};

#if defined(M8_FLOWER_GEOMETRY_BASE_PACKET_READ)
uint M8FlowerReadOriginalBasePacket(uint ownerRef,int3 owner,uint flags,uint nodeIndex,
    bool plus,float normalError,float offsetError,out M8FlowerPhaseRootEvidence root);
bool M8FlowerReadGeometryBasePacket(int3 owner,uint flags,M8FlowerGeometryNode task,
    uint rootIndex,float normalError,float offsetError,out M8FlowerPhaseRootEvidence root);
#endif

bool M8FlowerGeometryTetraParity(int3 owner,M8FlowerGeometryNode node,out uint parity)
{
    parity=0u;
    if(node.Level>2u || node.Line>=13u)return false;
    int endpoint;
    if(node.Level==0u)
    {
        if(node.RootNode>=26u || (uint)M8FlowerNodeAt(node.RootNode).w!=node.Line ||
            any(M8FlowerNodeAt(node.RootNode).xyz!=node.Offset))return false;
        endpoint=(node.RootNode&1u)==0u?1:-1;
    }
    else
    {
        uint level,lineClass,strand;int3 offset;int phase,inherited;
        if(!M8FlowerTryGetChildPhaseLoop(node.Petal,node.ParentContext,node.KnotSite,
            level,offset,lineClass,strand,endpoint,phase,inherited) || inherited>=0 ||
            level!=node.Level || lineClass!=node.Line || any(offset!=node.Offset))return false;
    }
    if(endpoint!=1 && endpoint!=-1)return false;
    int3 junction;
    if(!M8FlowerPhaseJunction(owner,node.Level+1u,node.Offset,junction))return false;
    int3 direction=endpoint*M8FlowerNodeAt(2u*node.Line).xyz;
    if(any(((junction^direction)&1)!=0))return false;
    // Exact (J-d)/2 without overflowing J-d at INT_MIN/INT_MAX. The
    // directed endpoint comes from substitution incidence, never +level.
    int3 cell=(junction>>1)+(((junction&1)-direction)/2);
    parity=((uint)cell.x&1u)|(((uint)cell.y&1u)<<1u)|(((uint)cell.z&1u)<<2u);
    return true;
}

uint M8FlowerCarrierRootProof(int3 owner,uint flags,uint level,int3 offset,
    uint lineClass,bool plus,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence root)
{
    root=(M8FlowerPhaseRootEvidence)0;
    root.Classification=2u;
    if(!M8FlowerHasPlane(flags))return 2u;
    if(level>2u || lineClass>=13u){root.Classification=0u;return 0u;}
    int3 junction;
    if(!M8FlowerPhaseJunction(owner,level+1u,offset,junction))
    {root.Classification=0u;return 0u;}
    float3 normal;float delta;
    M8FlowerUnpackPlane(flags,normal,delta);
    M8FlowerInterval3 abc;
    precise float3 relative=(0.5*M8FlowerLevelStep(level))*float3(offset);
    if(!M8FlowerPlaneLoopIntervals(normal,delta,relative,
        M8FlowerGeometryLoopRadiusAt(level*13u+lineClass),lineClass,normalError,offsetError,
        level,offset,abc))return 2u;
    uint tag,classification;
    M8FlowerInterval2 phase;
    if(!M8FlowerClassifyPlaneRoot(level,lineClass,false,plus,normal,delta,offset,abc,
        tag,phase,classification))
    {
        // The generated classifier owns tangent-minus canonicalization.
        // A sector crossing is unresolved, not proof that this sign is absent.
        if(classification==M8_FLOWER_ROOT_IMPOSSIBLE ||
            (classification==M8_FLOWER_ROOT_CERTAIN_TANGENT && plus))
        {root.Classification=0u;return 0u;}
        return 2u;
    }
    root.Junction=junction;root.Tag=tag;root.Root=phase;root.Classification=1u;
    return 1u;
}

bool M8FlowerCarrierRoot(int3 owner,uint flags,uint level,int3 offset,
    uint lineClass,bool plus,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence root)
{
    return M8FlowerCarrierRootProof(owner,flags,level,offset,lineClass,plus,
        normalError,offsetError,root)==1u;
}

// Finite alternatives of one original anchor in one R1-led root flag.
// The two bits are algebraic signs, never a persistent branch ordinal.
struct M8FlowerR1FlagRoots
{
    uint CandidateMask;
    uint UnresolvedMask;
    M8FlowerPhaseRootEvidence Minus;
    M8FlowerPhaseRootEvidence Plus;
};

uint M8FlowerR1FlagRootStatus(M8FlowerR1FlagRoots candidates,
    out M8FlowerPhaseRootEvidence uniqueRoot)
{
    uniqueRoot=(M8FlowerPhaseRootEvidence)0;
    if(candidates.UnresolvedMask!=0u || countbits(candidates.CandidateMask)>1u)
    {uniqueRoot.Classification=2u;return 2u;}
    if(candidates.CandidateMask==0u)return 0u;
    if(candidates.CandidateMask==1u)uniqueRoot=candidates.Minus;
    else uniqueRoot=candidates.Plus;
    return 1u;
}

uint M8FlowerSelectAnchorFlagRoots(int3 owner,uint flags,uint petal,uint anchorIndex,
    float normalError,float offsetError,out M8FlowerR1FlagRoots candidates,
    out M8FlowerPhaseRootEvidence uniqueRoot)
{
    candidates=(M8FlowerR1FlagRoots)0;
    if(petal>=48u || anchorIndex>=3u ||
        (flags&(M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID|M8_FLOWER_SEED_FLAG))!=
        (M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID))
        return M8FlowerR1FlagRootStatus(candidates,uniqueRoot);
    uint anchor=M8FlowerPetalNodesAt(petal)[anchorIndex];
    int4 node=M8FlowerNodeAt(anchor);
    [loop]for(uint sign=0u;sign<2u;sign++)
    {
        M8FlowerPhaseRootEvidence root;
        uint result=M8FlowerCarrierRootProof(owner,flags,0u,node.xyz,(uint)node.w,
            sign!=0u,normalError,offsetError,root);
        if(result==0u)continue;
        uint2 allowed;
        if(result!=1u || !M8FlowerAnchorRootReferences(anchor,root.Tag,allowed))
        {candidates.UnresolvedMask|=1u<<sign;continue;}
        if((allowed[petal>>5u]&(1u<<(petal&31u)))==0u)continue;
        candidates.CandidateMask|=1u<<sign;
        if(sign==0u)candidates.Minus=root;
        else candidates.Plus=root;
    }
    return M8FlowerR1FlagRootStatus(candidates,uniqueRoot);
}

uint M8FlowerSelectR1FlagRoots(int3 owner,uint flags,uint petal,
    float normalError,float offsetError,out M8FlowerR1FlagRoots candidates,
    out M8FlowerPhaseRootEvidence uniqueRoot)
{
    return M8FlowerSelectAnchorFlagRoots(owner,flags,petal,0u,
        normalError,offsetError,candidates,uniqueRoot);
}

// The caller supplies the actual neighbour owner+d, never an arbitrary nearby
// plane. Endpoint order follows the generated undirected line convention.
uint M8FlowerSelectAnchorFlagRelation(int3 owner,uint flags,uint neighbourFlags,
    uint petal,uint anchorIndex,float normalError,float offsetError,out M8FlowerR1FlagRoots candidates,
    out M8FlowerPhaseRootEvidence uniqueRoot)
{
    M8FlowerSelectAnchorFlagRoots(owner,flags,petal,anchorIndex,
        normalError,offsetError,candidates,uniqueRoot);
    if(candidates.CandidateMask==0u)return M8FlowerR1FlagRootStatus(candidates,uniqueRoot);
    uint anchorNode=M8FlowerPetalNodesAt(petal)[anchorIndex];
    int4 node=M8FlowerNodeAt(anchorNode);
    bool reversed=(anchorNode&1u)!=0u;
    [loop]for(uint sign=0u;sign<2u;sign++)
    {
        uint bit=1u<<sign;
        if((candidates.CandidateMask&bit)==0u)continue;
        M8FlowerPhaseRootEvidence anchor,relation;
        if(sign==0u)anchor=candidates.Minus;
        else anchor=candidates.Plus;
        M8FlowerInterval bend;
        uint result=M8FlowerEvaluateCarrierRelation(anchor.Junction,(uint)node.w,sign!=0u,
            reversed?neighbourFlags:flags,reversed?flags:neighbourFlags,
            normalError,offsetError,relation,bend);
        uint2 allowed=0u;
        if(result==1u && (!M8FlowerAnchorRootReferences(anchorNode,relation.Tag,allowed) ||
            (allowed[petal>>5u]&(1u<<(petal&31u)))==0u))result=2u;
        if(result!=1u)
        {
            candidates.CandidateMask&=~bit;
            if(result!=0u)candidates.UnresolvedMask|=bit;
            if(sign==0u)candidates.Minus=(M8FlowerPhaseRootEvidence)0;
            else candidates.Plus=(M8FlowerPhaseRootEvidence)0;
        }
        else if(sign==0u)candidates.Minus=relation;
        else candidates.Plus=relation;
    }
    return M8FlowerR1FlagRootStatus(candidates,uniqueRoot);
}

uint M8FlowerSelectR1FlagRelation(int3 owner,uint flags,uint neighbourFlags,
    uint petal,float normalError,float offsetError,out M8FlowerR1FlagRoots candidates,
    out M8FlowerPhaseRootEvidence uniqueRoot)
{
    return M8FlowerSelectAnchorFlagRelation(owner,flags,neighbourFlags,petal,0u,
        normalError,offsetError,candidates,uniqueRoot);
}

// A committed ancestor cannot disappear merely because its re-evaluated
// sector is unresolved. This bounded exact-key query is storage, not geometry matching.
bool M8FlowerHasPhaseFamily(uint ownerRef,uint epoch,uint level,uint path,
    uint petal,uint lineClass,bool plus)
{
    if(ownerRef==0u || epoch==0u)return false;
    uint sectors=M8FlowerLineMetaAt(lineClass).z;
    uint kind=M8FlowerLineMetaAt(lineClass).x==3u?1u:0u;
    [loop]for(uint sector=0u;sector<sectors;sector++)
    {
        uint key=M8FlowerPackDetailKey(level,path,petal,lineClass,kind,plus,sector);
        M8FlowerDetailRecord record;
        if(M8FlowerFindPhase(ownerRef,key,epoch,record))return true;
    }
    return false;
}

// Apply only this endpoint's metric innovation to its own prediction. In
// particular, a stored residual is never silently rebased onto a SEAL root.
bool M8FlowerApplyGeometryDetail(uint ownerRef,uint epoch,int3 owner,
    M8FlowerGeometryNode node,inout M8FlowerPhaseRootEvidence root)
{
    if(M8FlowerLineMetaAt(node.Line).x==1u)return true;
    uint key=M8FlowerPackDetailKey(node.Level,node.Path,node.Petal,node.Line,
        node.Kind,node.Plus,(root.Tag>>8u)&31u);
    M8FlowerDetailRecord record;
    if(!M8FlowerFindPhase(ownerRef,key,epoch,record))
    {
        if(!M8FlowerHasPhaseFamily(ownerRef,epoch,node.Level,node.Path,
            node.Petal,node.Line,node.Plus))return true;
        root.Classification=2u;return false;
    }
    if(node.Kind==0u)
    {
        M8FlowerPhaseRootEvidence synthesized;
        uint status=M8FlowerSynthesizePhaseRecord(root,key,record,epoch,synthesized);
        root=synthesized;root.Classification=status;
        return status==1u;
    }
    // R3 stores eta*tau. Eta belongs to THIS endpoint's level-local frame.
    uint parity;
    if(!M8FlowerGeometryTetraParity(owner,node,parity))
    {root.Classification=2u;return false;}
    int eta=0;
    [unroll]for(uint axis=0u;axis<4u;axis++)
        if(M8FlowerTetraLineAt(parity)[axis]==(int)node.Line)eta=M8FlowerTetraEtaAt(parity)[axis];
    if(eta==0){root.Classification=2u;return false;}
    M8FlowerInterval turn=M8FlowerDecodePhaseInterval(record.Lower,record.Upper);
    if(eta<0)turn=M8FlowerI(-turn.hi,-turn.lo);
    M8FlowerPhaseRootEvidence rotated;
    uint status=M8FlowerRotatePhaseEvidence(root,turn,rotated);
    root=rotated;root.Classification=status;
    return status==1u;
}

uint M8FlowerReadOriginalLocal(uint ownerRef,int3 owner,uint flags,uint nodeIndex,
    bool plus,float normalError,float offsetError,out M8FlowerPhaseRootEvidence root,
    out M8FlowerPhaseRootEvidence rawBase)
{
    root=(M8FlowerPhaseRootEvidence)0;
    rawBase=root;
    const uint required=M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID;
    if(nodeIndex>=26u)return 0u;
    if(M8FlowerOriginalInvalidationPending(ownerRef,nodeIndex))
    {
        // A transaction receipt is missing residency/work, not a new root
        // or a zero phase. Keep shared reads unresolved until local epoch
        // publication and all actually dependent peer cuts are acknowledged.
        root.Classification=2u;rawBase=root;return 2u;
    }
    if((flags&(required|M8_FLOWER_SEED_FLAG))!=required)return 0u;
    int4 source=M8FlowerNodeAt(nodeIndex);
#if defined(M8_FLOWER_GEOMETRY_BASE_PACKET_READ)
    // The same pure plane restriction, consumed only AFTER the original
    // pending/occupancy guards. The callback never substitutes synthesized
    // phase evidence for this endpoint-local prediction.
    uint status=M8FlowerReadOriginalBasePacket(ownerRef,owner,flags,nodeIndex,plus,
        normalError,offsetError,root);
#else
    uint status=M8FlowerCarrierRootProof(owner,flags,0u,source.xyz,(uint)source.w,
        plus,normalError,offsetError,root);
#endif
    // Preserve the actual R1 prediction before any endpoint-local R2/R3
    // innovation. R3 residuals must never use the synthesized observation as
    // their prediction, nor re-evaluate a second copy of the exact root graph.
    rawBase=root;
    if(status!=1u)return status;
    M8FlowerGeometryNode node=(M8FlowerGeometryNode)0;
    node.Offset=source.xyz;node.Line=(uint)source.w;node.RootNode=nodeIndex;node.Plus=plus;
    uint2 incidence=M8FlowerNodeIncidentPetalsAt(nodeIndex);
    // Canonical record address only; this does not select a geometric flag.
    node.Petal=incidence.x!=0u?(uint)firstbitlow(incidence.x):
        32u+(uint)firstbitlow(incidence.y);
    node.Kind=M8FlowerLineMetaAt(node.Line).x==3u?1u:0u;
    M8FlowerApplyGeometryDetail(ownerRef,M8FlowerGetOwnerEpoch(ownerRef),owner,node,root);
    return root.Classification;
}

uint M8FlowerReadOriginalLocal(uint ownerRef,int3 owner,uint flags,uint nodeIndex,
    bool plus,float normalError,float offsetError,out M8FlowerPhaseRootEvidence root)
{
    M8FlowerPhaseRootEvidence rawBase;
    return M8FlowerReadOriginalLocal(ownerRef,owner,flags,nodeIndex,plus,
        normalError,offsetError,root,rawBase);
}

#if defined(MERKABA_WORLD_INCLUDED)
// Both exact endpoints synthesize their OWN plane/epoch/key before SEAL.
// The immutable 27-tile lease supplies the neighbour, never a spatial search.
uint M8FlowerEvaluateOriginalShared(uint ownerSlot,uint ownerRef,int3 owner,uint flags,
    uint nodeIndex,bool plus,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence root,out bool provisional,
    out M8FlowerPhaseRootEvidence rawLocalBase,out uint endpointReceipt)
{
    endpointReceipt=0u;
    provisional=false;
    M8FlowerPhaseRootEvidence localRoot=(M8FlowerPhaseRootEvidence)0;
    root=localRoot;
    rawLocalBase=localRoot;
    uint status=0u,endpointRef=ownerRef,endpointFlags=flags;
    int3 endpointOwner=owner;
    // Keep local -> peer evaluation order, but one syntactic exact-root call
    // avoids duplicating its integer enclosure graph in the mobile shader.
    [loop]for(uint endpoint=0u;endpoint<2u;endpoint++)
    {
        if(endpoint!=0u)
        {
            // The successful local junction proof already bounded 2*owner+d.
            endpointOwner=owner+M8FlowerNodeAt(nodeIndex).xyz;
            uint peerSlot,peerLocal;KernelState peerState;
            status=M8FlowerReadEndpointWithReceipt(ownerSlot,owner,endpointOwner,
                peerSlot,peerLocal,peerState,endpointReceipt);
            if(status==0u && M8FlowerLineMetaAt((uint)M8FlowerNodeAt(nodeIndex).w).x>1u)
            {
                // Absent higher-shell evidence permits a provisional R1
                // prediction only. COLD remains unresolved; completion may
                // never treat this result as directly proved R2/R3 closure.
                provisional=true;root=localRoot;return 1u;
            }
            if(status!=1u){root=localRoot;root.Classification=status;return status;}
            endpointRef=M8FlowerFindOwner(peerSlot,peerLocal,M8FlowerEndpointGeneration(peerSlot));
            endpointFlags=peerState.flags;
        }
        M8FlowerPhaseRootEvidence endpointBase;
        status=M8FlowerReadOriginalLocal(endpointRef,endpointOwner,endpointFlags,
            nodeIndex^endpoint,plus,normalError,offsetError,root,endpointBase);
        if(endpoint==0u)rawLocalBase=endpointBase;
        if(status!=1u)
        {
            if(endpoint!=0u)root=localRoot;
            root.Classification=status;return status;
        }
        if(endpoint==0u)localRoot=root;
    }
    M8FlowerPhaseRootEvidence first=localRoot,second=root;
    if((nodeIndex&1u)!=0u){first=root;second=localRoot;}
    M8FlowerInterval bend;
    status=M8FlowerSealPhaseRelation(first,second,root,bend);
    root.Classification=status;
    return status;
}

#if defined(M8_FLOWER_GEOMETRY_PACKET_READ)
uint M8FlowerReadOriginalPacket(uint ownerSlot,uint ownerRef,int3 owner,uint flags,
    uint nodeIndex,bool plus,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence root,out bool provisional,
    out M8FlowerPhaseRootEvidence rawLocalBase,out uint endpointReceipt);
#endif

// A readout workgroup supplies the same measured endpoint evidence from its
// cooperative packet. The packet is scratch, never another evaluator or an
// optional fallback. COLD receipts become dependencies only when consumed.
uint M8FlowerReadOriginalShared(uint ownerSlot,uint ownerRef,int3 owner,uint flags,
    uint nodeIndex,bool plus,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence root,out bool provisional,
    out M8FlowerPhaseRootEvidence rawLocalBase)
{
    uint receipt;
#if defined(M8_FLOWER_GEOMETRY_PACKET_READ)
    uint status=M8FlowerReadOriginalPacket(ownerSlot,ownerRef,owner,flags,nodeIndex,plus,
        normalError,offsetError,root,provisional,rawLocalBase,receipt);
#else
    uint status=M8FlowerEvaluateOriginalShared(ownerSlot,ownerRef,owner,flags,nodeIndex,plus,
        normalError,offsetError,root,provisional,rawLocalBase,receipt);
#endif
    M8FlowerRequireEndpointReceipt(receipt);
    return status;
}

uint M8FlowerReadOriginalShared(uint ownerSlot,uint ownerRef,int3 owner,uint flags,
    uint nodeIndex,bool plus,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence root,out bool provisional)
{
    M8FlowerPhaseRootEvidence rawLocalBase;
    return M8FlowerReadOriginalShared(ownerSlot,ownerRef,owner,flags,nodeIndex,plus,
        normalError,offsetError,root,provisional,rawLocalBase);
}

uint M8FlowerReadOriginalShared(uint ownerSlot,uint ownerRef,int3 owner,uint flags,
    uint nodeIndex,bool plus,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence root)
{
    bool provisional;
    return M8FlowerReadOriginalShared(ownerSlot,ownerRef,owner,flags,nodeIndex,plus,
        normalError,offsetError,root,provisional);
}

// Metric enclosure and novelty are different predicates. A finite, proven
// q interval around zero is usable closure evidence, but does not establish
// that an innovation is exactly zero or authorize a persistent R3_PHASE.
uint M8FlowerR3MetricResidual(M8FlowerPhaseRootEvidence predicted,M8FlowerPhaseRootEvidence observed,
    uint parity,uint axis,out M8FlowerInterval q)
{
    q=M8FlowerI(0,0);
    if(predicted.Classification==0u || observed.Classification==0u)return 0u;
    if(parity>=8u || axis>=4u || predicted.Classification!=1u || observed.Classification!=1u ||
        !M8FlowerPhaseIdentityValid(predicted) || !M8FlowerPhaseIdentityValid(observed))return 2u;
    uint lineClass=(predicted.Tag>>3u)&15u,pSector,oSector;
    if(!M8FlowerPhaseRootSector(predicted,pSector) ||
        !M8FlowerPhaseRootSector(observed,oSector))return 2u;
    if(any(predicted.Junction!=observed.Junction) ||
        (predicted.Tag&0x1fffu)!=(observed.Tag&0x1fffu) ||
        pSector!=((predicted.Tag>>8u)&31u) || oSector!=((observed.Tag>>8u)&31u) ||
        (uint)M8FlowerTetraLineAt(parity)[axis]!=lineClass)return 0u;
    bool exactIdentity=predicted.Root.x.lo==predicted.Root.x.hi &&
        predicted.Root.y.lo==predicted.Root.y.hi && observed.Root.x.lo==observed.Root.x.hi &&
        observed.Root.y.lo==observed.Root.y.hi && predicted.Root.x.lo==observed.Root.x.lo &&
        predicted.Root.y.lo==observed.Root.y.lo;
    if(!exactIdentity && (!M8FlowerTauInterval(predicted.Root,observed.Root,q) ||
        !all(M8FlowerIsFinite(float2(q.lo,q.hi)))))return 2u;
    if(M8FlowerTetraEtaAt(parity)[axis]<0)q=M8FlowerI(-q.hi,-q.lo);
    return 1u;
}

// Original R3 anchors are inherited unchanged by both R2 substitutions.
// Their R1+R2 prediction therefore remains this exact original R1 root.
// Observed roots use each endpoint's committed innovation before canonical
// SEAL; a provisional missing peer can never establish even a zero residual.
uint M8FlowerReadR3Alternative(uint ownerSlot,uint ownerRef,int3 owner,uint flags,
    uint axis,bool plus,float2 errors,out M8FlowerInterval q,
    out M8FlowerPhaseRootEvidence observed)
{
    q=M8FlowerI(0,0);observed=(M8FlowerPhaseRootEvidence)0;
    const uint required=M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID;
    if(axis>=4u || (flags&(required|M8_FLOWER_SEED_FLAG))!=required)return 0u;
    uint parity=((uint)owner.x&1u)|(((uint)owner.y&1u)<<1u)|(((uint)owner.z&1u)<<2u);
    uint lineClass=(uint)M8FlowerTetraLineAt(parity)[axis];
    uint node=2u*lineClass+(M8FlowerTetraEtaAt(parity)[axis]<0?1u:0u);
    M8FlowerPhaseRootEvidence prediction;
    bool provisional;
    uint status=M8FlowerReadOriginalShared(ownerSlot,ownerRef,owner,flags,node,plus,
        errors.x,errors.y,observed,provisional,prediction);
    // The former raw-prediction guard returned before producing observed
    // evidence. Keep that failure output unchanged while sharing its work.
    if(prediction.Classification!=1u)observed=(M8FlowerPhaseRootEvidence)0;
    if(status!=1u)return status;
    if(provisional)return 2u;
    return M8FlowerR3MetricResidual(prediction,observed,parity,axis,q);
}

bool M8FlowerPredictGeometryNode(uint ownerSlot,uint ownerRef,uint epoch,int3 owner,uint flags,
    M8FlowerGeometryNode task,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence prediction)
{
    prediction=(M8FlowerPhaseRootEvidence)0;prediction.Classification=2u;
    M8FlowerPhaseRootEvidence parents[3],ancestors[2];
    M8FlowerDetailRecord records[2];uint keys[2];
    [unroll]for(uint parentIndex=0u;parentIndex<3u;parentIndex++)
        parents[parentIndex]=(M8FlowerPhaseRootEvidence)0;
    [unroll]for(uint ancestorIndex=0u;ancestorIndex<2u;ancestorIndex++)
    {
        ancestors[ancestorIndex]=(M8FlowerPhaseRootEvidence)0;
        records[ancestorIndex]=(M8FlowerDetailRecord)0;keys[ancestorIndex]=0u;
    }
    uint count=0u;
    M8FlowerPhaseFamilyRule family=(M8FlowerPhaseFamilyRule)0;
    uint4 strand=0u;uint sourcePetal=0u;
    // The same exact R1 restriction evaluates child base, coarse source and
    // (only for L2) L1 source, in that order. One call site keeps the shared
    // interval/rounding implementation from being triplicated by inlining.
    [loop]for(uint rootIndex=0u;rootIndex<=task.Level;rootIndex++)
    {
        uint sourceLevel=rootIndex==0u?task.Level:rootIndex-1u;
        int3 sourceOffset=task.Offset;
        if(rootIndex==1u)sourceOffset=M8FlowerNodeAt(family.RootNode).xyz;
        else if(rootIndex==2u)sourceOffset=M8FlowerNodeAt(strand.x).xyz+M8FlowerNodeAt(strand.y).xyz;
        M8FlowerPhaseRootEvidence source;
#if defined(M8_FLOWER_GEOMETRY_BASE_PACKET_READ)
        // Unlike ReadOriginalLocal, this is the PURE coarse/L1 base before
        // pending/detail admission. Do not read its postguard rawLocalBase.
        bool certain=M8FlowerReadGeometryBasePacket(owner,flags,task,rootIndex,
            normalError,offsetError,source);
#else
        bool certain=M8FlowerCarrierRoot(owner,flags,sourceLevel,sourceOffset,task.Line,task.Plus,
            normalError,offsetError,source);
#endif
        if(rootIndex==0u)
        {
            prediction=source;
            if(!certain)return false;
            if(task.Level==0u)return true;
            family=M8FlowerGetPhaseFamily(task.Strand);
            strand=M8FlowerStrandAt(task.Strand);
            sourcePetal=family.RootIncidentPetals.x!=0u?
                (uint)firstbitlow(family.RootIncidentPetals.x):
                32u+(uint)firstbitlow(family.RootIncidentPetals.y);
            continue;
        }
        uint sourcePath=rootIndex==1u?0u:family.FinePath0;
        uint recordPetal=rootIndex==1u?sourcePetal:strand.w&255u;
        if(!certain)
        {
            if(M8FlowerHasPhaseFamily(ownerRef,epoch,sourceLevel,sourcePath,recordPetal,
                task.Line,task.Plus))return false;
            continue;
        }
        uint key=M8FlowerPackDetailKey(sourceLevel,sourcePath,recordPetal,
            task.Line,0u,task.Plus,(source.Tag>>8u)&31u);
        M8FlowerDetailRecord record;
        if(!M8FlowerFindPhase(ownerRef,key,epoch,record))continue;
        if(rootIndex==1u)
        {
            M8FlowerPhaseRootEvidence sharedSource;
            if(M8FlowerReadOriginalShared(ownerSlot,ownerRef,owner,flags,family.RootNode,
                task.Plus,normalError,offsetError,sharedSource)!=1u)return false;
            // Validate source identity with the shared synthesized relation;
            // transport still consumes this owner's unchanged Q2.29 ints.
            source=sharedSource;
        }
        else
        {
            // Reconstruct L1 on its own exact loop before validating that
            // stored innovation. Coarse phase is not halved or replaced.
            if(count!=0u)
            {
                M8FlowerInterval turn=M8FlowerDecodePhaseInterval(records[0].Lower,records[0].Upper);
                if(family.PhaseOrientation<0)turn=M8FlowerI(-turn.hi,-turn.lo);
                M8FlowerPhaseRootEvidence transported;
                if(M8FlowerRotatePhaseEvidence(source,turn,transported)!=1u)return false;
                source=transported;
            }
            M8FlowerPhaseRootEvidence synthesized;
            if(M8FlowerSynthesizePhaseRecord(source,key,record,epoch,synthesized)!=1u)return false;
            source=synthesized;
        }
        ancestors[count]=source;records[count]=record;keys[count]=key;count++;
    }
    M8FlowerPhaseRootEvidence childBase=prediction;
    return M8FlowerTransportChildFromFamily(owner,task.Petal,task.ParentContext,task.KnotSite,
        childBase,parents,ancestors,records,keys,count,epoch,prediction)==1u;
}
#endif

// Resolve a generated terminal knot to the ORIGINAL loop and its canonical
// owner-local record key. Inherited knots therefore take the identical path
// through this function regardless of the incident L2 wedge requesting them.
bool M8FlowerL2GeometryNodeFromSource(uint source,bool plus,out M8FlowerGeometryNode node)
{
    node=(M8FlowerGeometryNode)0;
    node.Petal=source&63u;node.ParentContext=(source>>6u)&7u;
    node.KnotSite=(source>>9u)&7u;node.Plus=plus;
    if(node.Petal>=48u || node.ParentContext>=5u || node.KnotSite>=6u)return false;
    uint index=(node.Petal*5u+node.ParentContext)*6u+node.KnotSite;
    uint key=M8FlowerChildCreationAt(index);
    if((key&0x80000000u)==0u)return false;
    uint4 loopAddress=M8FlowerChildLoopAddressAt(index);
    node.Offset=asint(loopAddress.xyz);node.Level=loopAddress.w&3u;
    node.Line=(loopAddress.w>>2u)&15u;node.Strand=(loopAddress.w>>6u)&127u;
    node.Petal=key&63u;node.Path=(key>>6u)&15u;
    node.RootNode=(key>>10u)&31u;node.Kind=(key>>15u)&1u;
    return true;
}

bool M8FlowerL2GeometryNode(uint knot,bool plus,out M8FlowerGeometryNode node)
{
    node=(M8FlowerGeometryNode)0;
    return knot<386u && M8FlowerL2GeometryNodeFromSource(M8FlowerL2KnotSourceAt(knot),plus,node);
}

uint3 M8FlowerL2CarrierTriangle(uint wedge,bool reverse)
{
    if(wedge>=6u)return 0xffffffffu;
    uint a=1u+wedge,b=1u+(wedge+1u)%6u;
    return reverse?uint3(0u,b,a):uint3(0u,a,b);
}

// Same affine presentation chart as L2CarrierChartSite. These are UV values,
// never world coordinates or a replacement skin child partition.
float2 M8FlowerCarrierChartSite(uint site)
{
    if(site==1u)return float2(1.0,0.0);
    if(site==2u)return float2(1.0,1.0);
    if(site==3u)return float2(0.0,1.0);
    if(site==4u)return float2(-1.0,0.0);
    if(site==5u)return float2(-1.0,-1.0);
    if(site==6u)return float2(0.0,-1.0);
    return 0.0.xx;
}

bool M8FlowerCarrierChartWedge(float2 uv,out uint wedge,out float3 barycentric,
    out float3 derivativeU,out float3 derivativeV)
{
    wedge=0u;barycentric=derivativeU=derivativeV=0.0;
    if(!all(M8FlowerIsFinite(uv)))return false;
    wedge=uv.y>=0.0?(uv.x>=uv.y?0u:uv.x>=0.0?1u:2u):
        (uv.x<=uv.y?3u:uv.x<=0.0?4u:5u);
    float2 a=M8FlowerCarrierChartSite(1u+wedge);
    float2 b=M8FlowerCarrierChartSite(1u+(wedge+1u)%6u);
    precise float beta=uv.x*b.y-uv.y*b.x;
    precise float gamma=a.x*uv.y-a.y*uv.x;
    barycentric=float3((1.0-beta)-gamma,beta,gamma);
    derivativeU=float3(a.y-b.y,b.y,-a.y);
    derivativeV=float3(b.x-a.x,-b.x,a.x);
    return all(barycentric>=0.0);
}

// Presentation value of an already CERTAIN symbolic knot. This is the
// contract's ordered interval midpoint followed by its original loop frame;
// the requesting carrier, its normals and its material never enter the sum.
bool M8FlowerRootGridPosition(M8FlowerPhaseRootEvidence root,out float3 position)
{
    position=0.0;
    if(root.Classification!=1u || !M8FlowerPhaseIdentityValid(root))return false;
    uint level=root.Tag&7u,lineClass=(root.Tag>>3u)&15u;
    precise float x=root.Root.x.lo+(root.Root.x.hi-root.Root.x.lo)*0.5;
    precise float y=root.Root.y.lo+(root.Root.y.hi-root.Root.y.lo)*0.5;
    precise float halfStep=0.5*M8FlowerLevelStep(level);
    precise float3 centre=float3(root.Junction)*halfStep;
    precise float3 e1=M8FlowerLineE1At(lineClass)*x;
    precise float3 e2=M8FlowerLineE2At(lineClass)*y;
    precise float3 radial=(e1+e2)*M8FlowerGeometryLoopRadiusAt(level*13u+lineClass);
    position=centre+radial;
    return all(M8FlowerIsFinite(position));
}

// The coordinate ABC enclosures are finite generated data. Only the measured
// root remains an interval expression; its operation order is unchanged.
bool M8FlowerCoordinateBounds(uint form,M8FlowerPhaseRootEvidence root,
    out M8FlowerInterval3 bounds)
{
    bounds=(M8FlowerInterval3)0;
    M8FlowerInterval coordinate[3];
    [loop]for(uint axis=0u;axis<3u;axis++)
    {
        float4 ab=M8FlowerCoordinateFormAt(form*6u+axis*2u);
        float2 c=M8FlowerCoordinateFormAt(form*6u+axis*2u+1u).xy;
        coordinate[axis]=M8FlowerIAdd(M8FlowerIAdd(M8FlowerI(ab.x,ab.y),
            M8FlowerIMul(M8FlowerI(ab.z,ab.w),root.Root.x)),
            M8FlowerIMul(M8FlowerI(c.x,c.y),root.Root.y));
        if(!all(M8FlowerIsFinite(float2(coordinate[axis].lo,coordinate[axis].hi))))return false;
    }
    bounds.x=coordinate[0];bounds.y=coordinate[1];bounds.z=coordinate[2];
    return true;
}

bool M8FlowerRootNodeRelativeBounds(uint node,M8FlowerPhaseRootEvidence root,
    out M8FlowerInterval3 bounds)
{
    bounds=(M8FlowerInterval3)0;
    if(node>=26u || root.Classification!=1u || (root.Tag&7u)!=0u ||
        ((root.Tag>>3u)&15u)!=(uint)M8FlowerNodeAt(node).w)return false;
    return M8FlowerCoordinateBounds(node,root,bounds);
}

bool M8FlowerRootRelativeBounds(uint knot,M8FlowerPhaseRootEvidence root,
    out M8FlowerInterval3 bounds)
{
    bounds=(M8FlowerInterval3)0;
    uint level,lineClass;int3 offset;
    if(root.Classification!=1u || !M8FlowerL2KnotLoop(knot,level,offset,lineClass) ||
        (root.Tag&7u)!=level || ((root.Tag>>3u)&15u)!=lineClass)return false;
    return M8FlowerCoordinateBounds(26u+knot,root,bounds);
}

M8FlowerInterval M8FlowerCarrierDot(int3 direction,M8FlowerInterval3 position)
{
    return M8FlowerIAdd(M8FlowerIAdd(
        M8FlowerIMul(M8FlowerI(direction.x,direction.x),position.x),
        M8FlowerIMul(M8FlowerI(direction.y,direction.y),position.y)),
        M8FlowerIMul(M8FlowerI(direction.z,direction.z),position.z));
}

// A source cell uses its generated knots, not every root owner's flag
// interior. The actual source/path proves incidence; the retained tag still
// belongs to the knot's original loop and keeps its canonical sector owner.
uint M8FlowerSourceFlagContainment(uint petal,uint childPath,uint knot,
    uint proofTag,M8FlowerInterval3 position)
{
    if(petal>=48u || childPath>=16u || knot>=386u)return 0u;
    uint wedge=M8FlowerL2WedgeIndex(petal,childPath);
    if(wedge>=768u || all(M8FlowerL2WedgeKnots(wedge/6u,wedge%6u)!=knot))return 0u;
    M8FlowerGeometryNode original;
    if(!M8FlowerL2GeometryNode(knot,((proofTag>>7u)&1u)!=0u,original))return 0u;
    if((proofTag&7u)!=original.Level || ((proofTag>>3u)&15u)!=original.Line ||
        ((proofTag>>17u)&7u)>5u)return 2u;
    uint sector=(proofTag>>8u)&31u;
    if(sector>=M8FlowerLineMetaAt(original.Line).z)return 2u;
    uint code=(proofTag&M8_FLOWER_BOUNDARY_WITNESS_MASK)>>21u;
    uint boundaryOwner;
    if(code!=0u && (!M8FlowerBoundarySector(original.Line,code-1u,boundaryOwner) ||
        boundaryOwner!=sector))return 2u;
    if(!all(M8FlowerIsFinite(float3(position.x.lo,position.y.lo,position.z.lo))) ||
        !all(M8FlowerIsFinite(float3(position.x.hi,position.y.hi,position.z.hi))) ||
        position.x.lo>position.x.hi || position.y.lo>position.y.hi ||
        position.z.lo>position.z.hi)return 2u;
    return 1u;
}

uint M8FlowerCarrierWedgeOrientation(M8FlowerInterval3 a,M8FlowerInterval3 b,
    M8FlowerInterval3 c,float3 normal,float normalError)
{
    if(!all(M8FlowerIsFinite(normal)) || !M8FlowerIsFinite(normalError) || normalError<0.0)return 2u;
    M8FlowerInterval3 u,v;
    u.x=M8FlowerISub(b.x,a.x);u.y=M8FlowerISub(b.y,a.y);u.z=M8FlowerISub(b.z,a.z);
    v.x=M8FlowerISub(c.x,a.x);v.y=M8FlowerISub(c.y,a.y);v.z=M8FlowerISub(c.z,a.z);
    M8FlowerInterval3 area;
    area.x=M8FlowerISub(M8FlowerIMul(u.y,v.z),M8FlowerIMul(u.z,v.y));
    area.y=M8FlowerISub(M8FlowerIMul(u.z,v.x),M8FlowerIMul(u.x,v.z));
    area.z=M8FlowerISub(M8FlowerIMul(u.x,v.y),M8FlowerIMul(u.y,v.x));
    M8FlowerInterval det=M8FlowerIAdd(M8FlowerIAdd(
        M8FlowerIMul(area.x,M8FlowerCenterRadius(normal.x,normalError)),
        M8FlowerIMul(area.y,M8FlowerCenterRadius(normal.y,normalError))),
        M8FlowerIMul(area.z,M8FlowerCenterRadius(normal.z,normalError)));
    if(!all(M8FlowerIsFinite(float2(det.lo,det.hi))))return 2u;
    // L2 ring generation already applies flag orientation. There is no
    // normal-driven geometry flip; directFreeSide determines presentation.
    if(det.lo>0.0)return 1u;
    if(det.hi<0.0)return 0u;
    return 2u;
}

uint M8FlowerCarrierTriple(uint signs,uint wedge)
{
    return (signs&1u)|(((signs>>(1u+wedge))&1u)<<1u)|
        (((signs>>(1u+(wedge+1u)%6u))&1u)<<2u);
}

// The generated triple masks are inverse images in the fixed 128-bit
// seven-site alphabet. Intersecting their unions is exactly the former
// 128-assignment enumeration, without recomputing six projections per bit.
// Unused sites cannot create ambiguity: a wedge is emitted only when all
// surviving assignments have the same CERTAIN triple on that wedge.
// Six wedges carry one eight-bit triple mask each. Forty-eight bits are two
// words, so the per-wedge masks travel packed instead of in a six-entry lane
// array: same masks, same order, no indexed private storage.
uint M8FlowerWedgeByte(uint2 packed,uint wedge)
{
    return ((wedge<4u?packed.x:packed.y)>>(8u*(wedge&3u)))&255u;
}

void M8FlowerSetWedgeByte(inout uint2 packed,uint wedge,uint value)
{
    uint shift=8u*(wedge&3u),cleared=~(255u<<shift),kept=(value&255u)<<shift;
    packed.x=wedge<4u?((packed.x&cleared)|kept):packed.x;
    packed.y=wedge<4u?packed.y:((packed.y&cleared)|kept);
}

// An interval3 splits into its two float3 endpoints so a selection between two
// candidates is a component select, never a struct ternary. The round trip is
// exact: M8FlowerI is the plain constructor and adds no rounding.
void M8FlowerBoundsSplit(M8FlowerInterval3 bounds,out float3 lo,out float3 hi)
{
    lo=float3(bounds.x.lo,bounds.y.lo,bounds.z.lo);
    hi=float3(bounds.x.hi,bounds.y.hi,bounds.z.hi);
}

M8FlowerInterval3 M8FlowerBoundsFrom(float3 lo,float3 hi)
{
    M8FlowerInterval3 bounds;
    bounds.x=M8FlowerI(lo.x,hi.x);bounds.y=M8FlowerI(lo.y,hi.y);bounds.z=M8FlowerI(lo.z,hi.z);
    return bounds;
}

uint M8FlowerCombineCarrierCandidates(uint2 certain,uint2 uncertain,uint4 admissibleSigns,
    out uint signs,out uint active,out uint unresolved)
{
    signs=active=unresolved=0u;
    uint potential=0u;
    [unroll]for(uint w=0u;w<6u;w++)
        if((M8FlowerWedgeByte(certain,w)|M8FlowerWedgeByte(uncertain,w))!=0u)potential|=1u<<w;
    if(potential==0u)return 0u;
    uint4 surviving=admissibleSigns;
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        uint alternatives=M8FlowerWedgeByte(certain,wedge)|M8FlowerWedgeByte(uncertain,wedge);
        if(alternatives==0u)continue;
        uint4 allowed=0u;
        alternatives&=255u;
        [loop]while(alternatives!=0u)
        {
            uint triple=(uint)firstbitlow(alternatives);
            alternatives&=alternatives-1u;
            allowed|=M8FlowerCarrierTripleMaskAt(8u*wedge+triple);
        }
        surviving&=allowed;
    }
    if(all(surviving==0u)){unresolved=potential;return 2u;}
    uint word=surviving.x!=0u?0u:surviving.y!=0u?1u:surviving.z!=0u?2u:3u;
    uint first=32u*word+(uint)firstbitlow(surviving[word]);
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        uint bit=1u<<wedge;
        if((potential&bit)==0u)continue;
        uint triple=M8FlowerCarrierTriple(first,wedge);
        if((M8FlowerWedgeByte(certain,wedge)&(1u<<triple))==0u)continue;
        uint4 same=M8FlowerCarrierTripleMaskAt(8u*wedge+triple);
        if(all((surviving&~same)==0u))active|=bit;
    }
    unresolved=potential&~active;
    uint used=0u;
    [unroll]for(uint wedge=0u;wedge<6u;wedge++)if((active&(1u<<wedge))!=0u)
        used|=1u|(1u<<(1u+wedge))|(1u<<(1u+(wedge+1u)%6u));
    signs=first&used;
    return active!=0u?1u:2u;
}

#if defined(MERKABA_WORLD_INCLUDED)
bool M8FlowerReadL2Incidence(uint ownerSlot,uint ownerRef,int3 owner,uint flags,uint source,bool plus,
    float normalError,float offsetError,out M8FlowerPhaseRootEvidence root)
{
    root=(M8FlowerPhaseRootEvidence)0;root.Classification=2u;
    if((flags&(M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID|M8_FLOWER_SEED_FLAG))!=
        (M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID))return false;
    M8FlowerGeometryNode node;
    if(!M8FlowerL2GeometryNodeFromSource(source,plus,node))return false;
    if(node.Level==0u)return M8FlowerReadOriginalShared(ownerSlot,ownerRef,owner,flags,
        node.RootNode,plus,normalError,offsetError,root)==1u;
    uint epoch=M8FlowerGetOwnerEpoch(ownerRef);
    if(!M8FlowerPredictGeometryNode(ownerSlot,ownerRef,epoch,owner,flags,node,
        normalError,offsetError,root))
    {if(root.Classification==1u)root.Classification=2u;return false;}
    return M8FlowerApplyGeometryDetail(ownerRef,epoch,owner,node,root);
}

bool M8FlowerReadL2Knot(uint ownerSlot,uint ownerRef,int3 owner,uint flags,uint knot,bool plus,
    float normalError,float offsetError,out M8FlowerPhaseRootEvidence root)
{
    root=(M8FlowerPhaseRootEvidence)0;root.Classification=2u;
    if(knot>=386u)return false;
    uint first=M8FlowerL2IncidenceOffsetsAt(knot);
    uint end=M8FlowerL2IncidenceOffsetsAt(knot+1u);
    // Codegen proves a maximum of two distinct creation predictions.
    // Inherited L0/L1 aliases already use their single canonical record.
    // Never let the requesting wedge choose one of two conflicting knots.
    [loop]for(uint incidence=first;incidence<end;incidence++)
    {
        M8FlowerPhaseRootEvidence candidate;
        if(!M8FlowerReadL2Incidence(ownerSlot,ownerRef,owner,flags,
            M8FlowerL2IncidenceSourcesAt(incidence),plus,normalError,offsetError,candidate))
        {root=candidate;return false;}
        if(incidence==first)root=candidate;
        else
        {
            M8FlowerPhaseRootEvidence closed;
            uint status=M8FlowerCloseSharedPhaseRoot(root,candidate,closed);
            if(status!=1u){root=closed;root.Classification=status;return false;}
            root=closed;
        }
    }
    return root.Classification==1u;
}

#if defined(M8_FLOWER_GEOMETRY_PACKET_READ)
bool M8FlowerReadCarrierSitePacket(uint slot,uint local,uint ownerRef,int3 owner,uint flags,
    uint carrier,uint site,bool plus,float2 errors,out M8FlowerPhaseRootEvidence root);
#endif

bool M8FlowerReadCarrierSite(uint slot,uint local,uint ownerRef,int3 owner,uint flags,
    uint carrier,uint site,bool plus,float2 errors,out M8FlowerPhaseRootEvidence root)
{
#if defined(M8_FLOWER_GEOMETRY_PACKET_READ)
    return M8FlowerReadCarrierSitePacket(slot,local,ownerRef,owner,flags,carrier,site,plus,
        errors,root);
#else
    return M8FlowerReadL2Knot(slot,ownerRef,owner,flags,M8FlowerL2CarrierKnot(carrier,site),
        plus,errors.x,errors.y,root);
#endif
}

// Original source alternatives read the terminal reader's synthesized SEAL.
// Two available signs are finite choices for joint carrier closure, not an
// independent ambiguity and not a reason to intersect root-owner interiors.
void M8FlowerSourceAnchorAlternatives(uint slot,uint ownerRef,int3 owner,uint flags,
    uint petal,uint anchorIndex,float2 errors,uint completion,
    out uint available,out uint uncertain,out uint nonprovisional)
{
    available=uncertain=nonprovisional=0u;
    uint node=M8FlowerPetalNodesAt(petal)[anchorIndex];
    bool completedSource=completion!=0xffffffffu && (completion&63u)==petal;
    [loop]for(uint sign=0u;sign<2u;sign++)
    {
        if(completedSource && sign!=((completion>>(6u+anchorIndex))&1u))continue;
        M8FlowerPhaseRootEvidence root;
        bool provisional;
        uint status=M8FlowerReadOriginalShared(slot,ownerRef,owner,flags,node,sign!=0u,
            errors.x,errors.y,root,provisional);
        if(status==0u)continue;
        uint2 allowed;uint sector;
        if(status!=1u || !M8FlowerPhaseRootSector(root,sector) ||
            !M8FlowerAnchorRootReferences(node,root.Tag,allowed))
        {uncertain|=1u<<sign;continue;}
        if((allowed[petal>>5u]&(1u<<(petal&31u)))==0u)continue;
        if(completedSource && provisional){uncertain|=1u<<sign;continue;}
        available|=1u<<sign;
        if(!provisional && !completedSource)nonprovisional|=1u<<sign;
    }
}

// Actual direct producer input. The caller holds the page/observation source
// lease and has cached its 27 tile refs. This function creates no fine state,
// does not complete holes, and does not turn a dual boundary into evidence.
uint M8FlowerClassifyL2Carrier(uint slot,uint local,uint carrier,float2 errors,
    uint completion,
    out M8FlowerSymbolRecord symbol,out uint unresolvedWedges,
    out uint directWedges,out M8FlowerPhaseRootEvidence roots[7],out float3 positions[7])
{
    symbol=(M8FlowerSymbolRecord)0;symbol.ThreadRef=0xffffffffu;
    unresolvedWedges=directWedges=0u;
    // glslang's HLSL front end does not honour [unroll] here, so the seven
    // published slots are named. A dynamically indexed seven-entry root array
    // is the same lane-private candidate carrier 4.5 rules out.
    M8FlowerPhaseRootEvidence blank=(M8FlowerPhaseRootEvidence)0;
    roots[0]=blank;roots[1]=blank;roots[2]=blank;roots[3]=blank;
    roots[4]=blank;roots[5]=blank;roots[6]=blank;
    positions[0]=0.0;positions[1]=0.0;positions[2]=0.0;
    positions[3]=0.0;positions[4]=0.0;positions[5]=0.0;positions[6]=0.0;
    if(slot>=32768u || local>=512u || carrier>=128u)return 0u;
    int3 owner=M8FlowerEndpointOwner(slot,local);
    uint ownerSlot,ownerLocal;KernelState state;
    uint resident=M8FlowerReadEndpoint(slot,owner,owner,ownerSlot,ownerLocal,state);
    if(resident!=1u){unresolvedWedges=resident==2u?63u:0u;return resident;}
    uint ownerRef=M8FlowerFindOwner(slot,local,M8FlowerEndpointGeneration(slot));
    float3 normal;float delta;M8FlowerUnpackPlane(state.flags,normal,delta);
    uint2 certain=0u,uncertain=0u,directTriples=0u;
    uint signs=0u,active=0u,used=0u,directParents=0u;
    // A wedge's eight triples reference only the six alternatives of its own
    // three sites, so the candidates are enumerated inside the wedge that
    // consumes them. Same reader, same arithmetic, same intervals; what goes
    // is the fourteen-candidate lane-private cache 4.5 forbids carrying.
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        uint source=M8FlowerL2WedgeAt(6u*carrier+wedge).y;
        uint petal=source>>4u,path=source&15u;
        uint parentStatus=1u;
        bool directParent=true;
        uint3 anchorAvailable=0u,anchorUncertain=0u,anchorDirect=0u;
        [loop]for(uint anchor=0u;anchor<3u;anchor++)
        {
            uint available,pending,direct;
            M8FlowerSourceAnchorAlternatives(slot,ownerRef,owner,state.flags,
                petal,anchor,errors,completion,available,pending,direct);
            anchorAvailable[anchor]=available;anchorUncertain[anchor]=pending;
            anchorDirect[anchor]=direct;directParent=directParent && direct!=0u;
            if((available|pending)==0u){parentStatus=0u;break;}
            if(available==0u)parentStatus=2u;
        }
        if(parentStatus==0u)continue;
        uint3 sites=M8FlowerL2CarrierTriangle(wedge,false);
        // Three sites, two analytic endpoint roots each. Both signs are held
        // side by side under a static vertex index, so a triple selects a
        // candidate by component, never by indexing a private array.
        M8FlowerInterval3 minusSupport[3],plusSupport[3];
        uint minusTag[3],plusTag[3],known=0u,unknown=0u;
        [unroll]for(uint vertex=0u;vertex<3u;vertex++)
        {
            uint knot=M8FlowerL2CarrierKnot(carrier,sites[vertex]);
            [unroll]for(uint sign=0u;sign<2u;sign++)
            {
                M8FlowerInterval3 bounds=(M8FlowerInterval3)0;uint tag=0u;
                M8FlowerPhaseRootEvidence root;
                bool read=M8FlowerReadCarrierSite(slot,local,ownerRef,owner,state.flags,
                    carrier,sites[vertex],sign!=0u,errors,root);
                uint bit=1u<<(2u*vertex+sign);
                if(read && M8FlowerRootRelativeBounds(knot,root,bounds))
                {
                    known|=bit;uint sector;
                    tag=root.Tag&~M8_FLOWER_BOUNDARY_WITNESS_MASK;
                    if((root.Tag&M8_FLOWER_BOUNDARY_WITNESS_MASK)!=0u &&
                        M8FlowerPhaseRootSector(root,sector))tag=root.Tag;
                }
                else if(root.Classification!=0u)unknown|=bit;
                if(sign==0u){minusSupport[vertex]=bounds;minusTag[vertex]=tag;}
                else{plusSupport[vertex]=bounds;plusTag[vertex]=tag;}
            }
        }
        uint wedgeCertain=0u,wedgeUncertain=0u,wedgeDirect=0u;
        [loop]for(uint triple=0u;triple<8u;triple++)
        {
            uint3 vsign=uint3(triple&1u,(triple>>1u)&1u,(triple>>2u)&1u);
            uint needed=(1u<<vsign.x)|(1u<<(2u+vsign.y))|(1u<<(4u+vsign.z));
            if((needed&~(known|unknown))!=0u)continue;
            bool resolved=parentStatus==1u && (needed&~known)==0u;
            bool impossible=false;
            bool tripleDirect=directParent;
            M8FlowerInterval3 tripleSupport[3];
            [unroll]for(uint vertex=0u;vertex<3u;vertex++)
            {
                uint chosen=vsign[vertex];
                float3 lo,hi,otherLo,otherHi;
                M8FlowerBoundsSplit(minusSupport[vertex],lo,hi);
                M8FlowerBoundsSplit(plusSupport[vertex],otherLo,otherHi);
                tripleSupport[vertex]=M8FlowerBoundsFrom(chosen!=0u?otherLo:lo,
                    chosen!=0u?otherHi:hi);
                uint tag=chosen!=0u?plusTag[vertex]:minusTag[vertex];
                uint knot=M8FlowerL2CarrierKnot(carrier,sites[vertex]);
                M8FlowerGeometryNode original;
                if(!M8FlowerL2GeometryNode(knot,chosen!=0u,original))
                {impossible=true;break;}
                if(original.Level==0u)
                {
                    uint anchor=3u;
                    [unroll]for(uint candidate=0u;candidate<3u;candidate++)
                    {
                        uint node=M8FlowerPetalNodesAt(petal)[candidate];
                        if((uint)M8FlowerNodeAt(node).w==original.Line &&
                            all(M8FlowerNodeAt(node).xyz==original.Offset))anchor=candidate;
                    }
                    if(anchor==3u){impossible=true;break;}
                    uint signBit=1u<<chosen;
                    if(((anchorAvailable[anchor]|anchorUncertain[anchor])&signBit)==0u)
                    {impossible=true;break;}
                    resolved=resolved && (anchorAvailable[anchor]&signBit)!=0u;
                    tripleDirect=tripleDirect && (anchorDirect[anchor]&signBit)!=0u;
                }
                if((known&(1u<<(2u*vertex+chosen)))==0u)continue;
                uint status=M8FlowerSourceFlagContainment(petal,path,knot,tag,
                    tripleSupport[vertex]);
                if(status==0u){impossible=true;break;}
                if(status!=1u)resolved=false;
            }
            if(impossible)continue;
            if((needed&~known)==0u)
            {
                uint orientation=M8FlowerCarrierWedgeOrientation(tripleSupport[0],
                    tripleSupport[1],tripleSupport[2],normal,errors.x);
                if(orientation==0u)continue;
                if(orientation!=1u)resolved=false;
            }
            if(resolved)
            {
                wedgeCertain|=1u<<triple;
                if(tripleDirect)wedgeDirect|=1u<<triple;
            }
            else wedgeUncertain|=1u<<triple;
        }
        M8FlowerSetWedgeByte(certain,wedge,wedgeCertain);
        M8FlowerSetWedgeByte(uncertain,wedge,wedgeUncertain);
        M8FlowerSetWedgeByte(directTriples,wedge,wedgeDirect);
    }
    uint result=M8FlowerCombineCarrierCandidates(certain,uncertain,
        M8FlowerL2CarrierBranchMasksAt(carrier),signs,active,unresolvedWedges);
    if(result!=1u)return result;
    [unroll]for(uint wedge=0u;wedge<6u;wedge++)if((active&(1u<<wedge))!=0u)
    {
        used|=1u|(1u<<(1u+wedge))|(1u<<(1u+(wedge+1u)%6u));
        if((M8FlowerWedgeByte(directTriples,wedge)&(1u<<M8FlowerCarrierTriple(signs,wedge)))!=0u)
            directParents|=1u<<wedge;
    }
    // Only the selected seven sites are materialized, one root per site, at
    // the sign the combination actually chose.
    [loop]for(uint site=0u;site<7u;site++)
    {
        if((used&(1u<<site))==0u)continue;
        M8FlowerPhaseRootEvidence root;float3 position;
        bool read=M8FlowerReadCarrierSite(slot,local,ownerRef,owner,state.flags,
            carrier,site,((signs>>site)&1u)!=0u,errors,root);
        bool located=M8FlowerRootGridPosition(root,position);
        // One reader call site; only the publication is named per slot.
        if(site==0u){roots[0]=root;positions[0]=position;}
        else if(site==1u){roots[1]=root;positions[1]=position;}
        else if(site==2u){roots[2]=root;positions[2]=position;}
        else if(site==3u){roots[3]=root;positions[3]=position;}
        else if(site==4u){roots[4]=root;positions[4]=position;}
        else if(site==5u){roots[5]=root;positions[5]=position;}
        else{roots[6]=root;positions[6]=position;}
        if(!read || !located){unresolvedWedges|=active;return 2u;}
    }
    bool reverse=M8FlowerPlaneFreeSide(state.flags)<0;
    uint completed=0u;
    [unroll]for(uint wedge=0u;wedge<6u;wedge++)
        if(completion!=0xffffffffu && (M8FlowerL2WedgeAt(6u*carrier+wedge).y>>4u)==(completion&63u))
            completed|=active&(1u<<wedge);
    // This direct stage proves R1 support. Higher-shell metric evaluation
    // alone must not fabricate R3 branch closure or COMPLETED evidence.
    if(!M8FlowerTryCreateCarrierSymbol(local,carrier,1u,reverse,false,active,signs,
        (roots[0].Tag>>8u)&31u,completed,reverse?active:0u,ownerRef,0xffffffffu,symbol))
    {unresolvedWedges|=active;return 2u;}
    // Provisional R2/R3 is lawful R1 presentation but is never direct
    // boundary evidence. Preserve that distinction without gating occupancy.
    directWedges=active&directParents&~completed;
    return 1u;
}

uint M8FlowerClassifyL2Carrier(uint slot,uint local,uint carrier,float2 errors,
    out M8FlowerSymbolRecord symbol,out uint unresolvedWedges,
    out uint directWedges,out M8FlowerPhaseRootEvidence roots[7],out float3 positions[7])
{
    return M8FlowerClassifyL2Carrier(slot,local,carrier,errors,0xffffffffu,
        symbol,unresolvedWedges,directWedges,roots,positions);
}

uint M8FlowerClassifyL2Carrier(uint slot,uint local,uint carrier,float2 errors,
    out M8FlowerSymbolRecord symbol,out uint unresolvedWedges,
    out M8FlowerPhaseRootEvidence roots[7],out float3 positions[7])
{
    uint directWedges;
    return M8FlowerClassifyL2Carrier(slot,local,carrier,errors,symbol,
        unresolvedWedges,directWedges,roots,positions);
}

uint M8FlowerClassifyL2Carrier(uint slot,uint local,uint carrier,float2 errors,
    out M8FlowerSymbolRecord symbol,out uint unresolvedWedges,out float3 positions[7])
{
    M8FlowerPhaseRootEvidence roots[7];
    return M8FlowerClassifyL2Carrier(slot,local,carrier,errors,symbol,unresolvedWedges,roots,positions);
}
#endif

#endif
