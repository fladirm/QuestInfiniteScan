#ifndef GENESIS_MERKABA_FLOWER_GEOMETRY_INCLUDED
#define GENESIS_MERKABA_FLOWER_GEOMETRY_INCLUDED

#include "MerkabaFlowerSidecar.hlsl"

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

bool M8FlowerCarrierRoot(int3 owner,uint flags,uint level,int3 offset,
    uint lineClass,bool plus,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence root)
{
    root=(M8FlowerPhaseRootEvidence)0;
    root.Classification=2u;
    if(!M8FlowerHasPlane(flags) || level>2u || lineClass>=13u)return false;
    int3 junction;
    if(!M8FlowerPhaseJunction(owner,level+1u,offset,junction))return false;
    float3 normal;float delta;
    M8FlowerUnpackPlane(flags,normal,delta);
    M8FlowerInterval3 abc;
    precise float3 relative=(0.5*M8FlowerLevelStep(level))*float3(offset);
    if(!M8FlowerPlaneIntervals(normal,delta,relative,
        M8FlowerGeometryLoopRadius[level*13u+lineClass],lineClass,normalError,offsetError,abc))return false;
    uint tag,classification;
    M8FlowerInterval2 phase;
    if(!M8FlowerClassifyObservationRoot(level,lineClass,false,plus,abc,
        tag,phase,classification))return false;
    root.Junction=junction;root.Tag=tag;root.Root=phase;root.Classification=1u;
    return true;
}

// A committed ancestor cannot disappear merely because its re-evaluated
// sector is unresolved. This bounded exact-key query is storage, not geometry matching.
bool M8FlowerHasPhaseFamily(uint ownerRef,uint epoch,uint level,uint path,
    uint petal,uint lineClass,bool plus)
{
    if(ownerRef==0u || epoch==0u)return false;
    uint sectors=M8FlowerLineMeta[lineClass].z;
    uint kind=M8FlowerLineMeta[lineClass].x==3u?1u:0u;
    [loop]for(uint sector=0u;sector<sectors;sector++)
    {
        uint key=M8FlowerPackDetailKey(level,path,petal,lineClass,kind,plus,sector);
        M8FlowerDetailRecord record;
        if(M8FlowerFindPhase(ownerRef,key,epoch,record))return true;
    }
    return false;
}

bool M8FlowerPredictGeometryNode(uint ownerRef,uint epoch,int3 owner,uint flags,
    M8FlowerGeometryNode task,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence prediction)
{
    if(!M8FlowerCarrierRoot(owner,flags,task.Level,task.Offset,task.Line,task.Plus,
        normalError,offsetError,prediction))return false;
    if(task.Level==0u)return true;
    M8FlowerPhaseRootEvidence parents[3],ancestors[2];
    M8FlowerDetailRecord records[2];uint keys[2];
    [unroll]for(uint i=0u;i<3u;i++)parents[i]=(M8FlowerPhaseRootEvidence)0;
    [unroll]for(uint i=0u;i<2u;i++)
    {
        ancestors[i]=(M8FlowerPhaseRootEvidence)0;
        records[i]=(M8FlowerDetailRecord)0;keys[i]=0u;
    }
    uint count=0u;
    M8FlowerPhaseFamilyRule family=M8FlowerGetPhaseFamily(task.Strand);
    M8FlowerPhaseRootEvidence source;
    M8FlowerDetailRecord record;
    uint sourcePetal=family.RootIncidentPetals.x!=0u?
        (uint)firstbitlow(family.RootIncidentPetals.x):
        32u+(uint)firstbitlow(family.RootIncidentPetals.y);
    if(M8FlowerCarrierRoot(owner,flags,0u,M8FlowerNode[family.RootNode].xyz,
        task.Line,task.Plus,normalError,offsetError,source))
    {
        uint key=M8FlowerPackDetailKey(0u,0u,sourcePetal,task.Line,0u,task.Plus,
            (source.Tag>>8u)&31u);
        if(M8FlowerFindPhase(ownerRef,key,epoch,record))
        {
            M8FlowerPhaseRootEvidence synthesized;
            if(M8FlowerSynthesizePhaseRecord(source,key,record,epoch,synthesized)!=1u)return false;
            ancestors[count]=synthesized;records[count]=record;keys[count]=key;count++;
        }
    }
    else if(M8FlowerHasPhaseFamily(ownerRef,epoch,0u,0u,sourcePetal,
        task.Line,task.Plus))return false;
    if(task.Level==2u)
    {
        uint4 strand=M8FlowerStrand[task.Strand];
        int3 sourceOffset=M8FlowerNode[strand.x].xyz+M8FlowerNode[strand.y].xyz;
        if(M8FlowerCarrierRoot(owner,flags,1u,sourceOffset,task.Line,task.Plus,
            normalError,offsetError,source))
        {
            uint key=M8FlowerPackDetailKey(1u,family.FinePath0,strand.w&255u,
                task.Line,0u,task.Plus,(source.Tag>>8u)&31u);
            if(M8FlowerFindPhase(ownerRef,key,epoch,record))
            {
                // Reconstruct this source on its own exact loop before
                // validating the stored L1 innovation. No phase is halved.
                if(count!=0u)
                {
                    M8FlowerInterval turn=M8FlowerDecodePhaseInterval(records[0].Lower,records[0].Upper);
                    if(family.PhaseOrientation<0)turn=M8FlowerI(-turn.hi,-turn.lo);
                    M8FlowerInterval2 transported;
                    if(!M8FlowerRotateInterval(source.Root,turn,task.Line,
                        (source.Tag>>8u)&31u,transported))return false;
                    source.Root=transported;
                }
                M8FlowerPhaseRootEvidence synthesized;
                if(M8FlowerSynthesizePhaseRecord(source,key,record,epoch,synthesized)!=1u)return false;
                ancestors[count]=synthesized;records[count]=record;keys[count]=key;count++;
            }
        }
        else if(M8FlowerHasPhaseFamily(ownerRef,epoch,1u,family.FinePath0,
            strand.w&255u,task.Line,task.Plus))return false;
    }
    float3 normal;float delta;
    M8FlowerUnpackPlane(flags,normal,delta);
    return M8FlowerPredictChildFromFamily(owner,task.Petal,task.ParentContext,task.KnotSite,
        normal,delta,normalError,offsetError,(prediction.Tag>>8u)&31u,task.Plus,
        parents,ancestors,records,keys,count,epoch,prediction)==1u;
}

// Resolve a generated terminal knot to the ORIGINAL loop and its canonical
// owner-local record key. Inherited knots therefore take the identical path
// through this function regardless of the incident L2 wedge requesting them.
bool M8FlowerL2GeometryNodeFromSource(uint source,bool plus,out M8FlowerGeometryNode node)
{
    node=(M8FlowerGeometryNode)0;
    node.Petal=source&63u;node.ParentContext=(source>>6u)&7u;
    node.KnotSite=(source>>9u)&7u;node.Plus=plus;
    int endpoint,phase,inherited;
    if(!M8FlowerTryGetChildPhaseLoop(node.Petal,node.ParentContext,node.KnotSite,
        node.Level,node.Offset,node.Line,node.Strand,endpoint,phase,inherited))return false;
    if(node.Level==0u)
    {
        node.RootNode=M8FlowerPetalNodes[node.Petal][node.KnotSite];
        uint2 incidence=M8FlowerNodeIncidentPetals[node.RootNode];
        node.Petal=incidence.x!=0u?(uint)firstbitlow(incidence.x):
            32u+(uint)firstbitlow(incidence.y);
    }
    else
    {
        // BuildL2Carrier records the first creation, never a later inherited
        // alias. A mid-edge knot's ancestry comes from its whole-loop family.
        if(inherited>=0)return false;
        M8FlowerPhaseFamilyRule family=M8FlowerGetPhaseFamily(node.Strand);
        node.RootNode=family.RootNode;
        if(node.Level==1u)
        {
            node.Petal=M8FlowerStrand[node.Strand].w&255u;
            node.Path=family.FinePath0;
        }
        else node.Path=4u*(node.ParentContext-1u)+(node.KnotSite==4u?1u:0u);
    }
    node.Kind=M8FlowerLineMeta[node.Line].x==3u?1u:0u;
    return true;
}

bool M8FlowerL2GeometryNode(uint knot,bool plus,out M8FlowerGeometryNode node)
{
    node=(M8FlowerGeometryNode)0;
    return knot<386u && M8FlowerL2GeometryNodeFromSource(M8FlowerL2KnotSource[knot],plus,node);
}

// Same numbering as L2CarrierKnotIndex: one shared H and six ring knots.
// Normal, color and wedge ownership never participate in this mapping.
uint M8FlowerL2CarrierKnot(uint carrier,uint site)
{
    if(carrier>=128u || site>=7u)return 0xffffffffu;
    uint3 knots=M8FlowerL2WedgeKnots(carrier,site==0u?0u:site-1u);
    return site==0u?knots.x:knots.y;
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
    if(!all(isfinite(uv)))return false;
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
    precise float3 e1=M8FlowerLineE1[lineClass]*x;
    precise float3 e2=M8FlowerLineE2[lineClass]*y;
    precise float3 radial=(e1+e2)*M8FlowerGeometryLoopRadius[level*13u+lineClass];
    position=centre+radial;
    return all(isfinite(position));
}

bool M8FlowerReadL2Incidence(uint ownerRef,int3 owner,uint flags,uint source,bool plus,
    float normalError,float offsetError,out M8FlowerPhaseRootEvidence root)
{
    root=(M8FlowerPhaseRootEvidence)0;root.Classification=2u;
    if((flags&(M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID|M8_FLOWER_SEED_FLAG))!=
        (M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID))return false;
    M8FlowerGeometryNode node;
    if(!M8FlowerL2GeometryNodeFromSource(source,plus,node))return false;
    uint epoch=M8FlowerGetOwnerEpoch(ownerRef);
    if(!M8FlowerPredictGeometryNode(ownerRef,epoch,owner,flags,node,
        normalError,offsetError,root))return false;
    uint shell=M8FlowerLineMeta[node.Line].x;
    if(shell==1u)return true;
    uint key=M8FlowerPackDetailKey(node.Level,node.Path,node.Petal,node.Line,
        node.Kind,plus,(root.Tag>>8u)&31u);
    M8FlowerDetailRecord record;
    if(!M8FlowerFindPhase(ownerRef,key,epoch,record))
        return !M8FlowerHasPhaseFamily(ownerRef,epoch,node.Level,node.Path,
            node.Petal,node.Line,plus);
    if(node.Kind==0u)
    {
        M8FlowerPhaseRootEvidence synthesized;
        if(M8FlowerSynthesizePhaseRecord(root,key,record,epoch,synthesized)!=1u)return false;
        root=synthesized;return true;
    }
    // R3 records contain eta*tau, not a replacement normal. Reading a metric
    // root here does not assert that a junction class/chirality is admissible.
    uint parity=((uint)owner.x&1u)|(((uint)owner.y&1u)<<1u)|(((uint)owner.z&1u)<<2u);
    int eta=0;
    [unroll]for(uint axis=0u;axis<4u;axis++)
        if(M8FlowerTetraLine[parity][axis]==(int)node.Line)eta=M8FlowerTetraEta[parity][axis];
    if(eta==0)return false;
    M8FlowerInterval turn=M8FlowerDecodePhaseInterval(record.Lower,record.Upper);
    if(eta<0)turn=M8FlowerI(-turn.hi,-turn.lo);
    M8FlowerInterval2 rotated;
    if(!M8FlowerRotateInterval(root.Root,turn,node.Line,(root.Tag>>8u)&31u,rotated))return false;
    root.Root=rotated;
    return true;
}

bool M8FlowerReadL2Knot(uint ownerRef,int3 owner,uint flags,uint knot,bool plus,
    float normalError,float offsetError,out M8FlowerPhaseRootEvidence root)
{
    root=(M8FlowerPhaseRootEvidence)0;root.Classification=2u;
    if(knot>=386u)return false;
    uint first=M8FlowerL2IncidenceOffsets[knot];
    uint end=M8FlowerL2IncidenceOffsets[knot+1u];
    // Codegen proves a maximum of two distinct creation predictions.
    // Inherited L0/L1 aliases already use their single canonical record.
    // Never let the requesting wedge choose one of two conflicting knots.
    [loop]for(uint incidence=first;incidence<end;incidence++)
    {
        M8FlowerPhaseRootEvidence candidate;
        if(!M8FlowerReadL2Incidence(ownerRef,owner,flags,
            M8FlowerL2IncidenceSources[incidence],plus,normalError,offsetError,candidate))return false;
        if(incidence==first)root=candidate;
        else
        {
            M8FlowerPhaseRootEvidence closed;
            if(M8FlowerCloseSharedPhaseRoot(root,candidate,closed)!=1u)return false;
            root=closed;
        }
    }
    return root.Classification==1u;
}

// rootSigns encodes the three requested algebraic alternatives. Admissible
// flags and shared incidence are still classified by the caller; this reader
// never chooses an alternative by distance or by a root ordinal.
bool M8FlowerReadL2WedgeRoots(uint ownerRef,int3 owner,uint flags,uint hub,uint wedge,
    uint rootSigns,float normalError,float offsetError,
    out M8FlowerPhaseRootEvidence roots[3])
{
    [unroll]for(uint i=0u;i<3u;i++)
    { roots[i]=(M8FlowerPhaseRootEvidence)0;roots[i].Classification=2u; }
    if(hub>=128u || wedge>=6u || rootSigns>=8u)return false;
    uint3 knots=M8FlowerL2WedgeKnots(hub,wedge);
    [loop]for(uint node=0u;node<3u;node++)
        if(!M8FlowerReadL2Knot(ownerRef,owner,flags,knots[node],
            (rootSigns&(1u<<node))!=0u,normalError,offsetError,roots[node]))return false;
    return true;
}

#endif
