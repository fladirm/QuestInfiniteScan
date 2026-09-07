#ifndef GENESIS_MERKABA_FLOWER_SKIN_OBSERVATION_INCLUDED
#define GENESIS_MERKABA_FLOWER_SKIN_OBSERVATION_INCLUDED

#include "MerkabaFlowerGeometry.hlsl"

// These are the SAME calibrated bounds retained by StereoFlowerRefine for
// this immutable observation. No presentation light or colour score enters
// captured-radiance admission.
float4 _M8RgbErrorBounds[2];
#define M8_FLOWER_SKIN_INVALID_FOOTPRINT 3u

M8FlowerInterval3 M8FlowerSkinPoint(float3 p)
{
    M8FlowerInterval3 result;
    result.x=M8FlowerI(p.x,p.x);result.y=M8FlowerI(p.y,p.y);result.z=M8FlowerI(p.z,p.z);
    return result;
}

M8FlowerInterval3 M8FlowerSkinTransform(float4x4 transformMatrix,M8FlowerInterval3 p)
{
    M8FlowerInterval3 result;
    result.x=M8DepthIntervalRow(transformMatrix[0],p);
    result.y=M8DepthIntervalRow(transformMatrix[1],p);
    result.z=M8DepthIntervalRow(transformMatrix[2],p);
    return result;
}

M8FlowerInterval3 M8FlowerSkinAdd(M8FlowerInterval3 a,M8FlowerInterval3 b)
{
    a.x=M8FlowerIAdd(a.x,b.x);a.y=M8FlowerIAdd(a.y,b.y);a.z=M8FlowerIAdd(a.z,b.z);
    return a;
}

M8FlowerInterval3 M8FlowerSkinScale(M8FlowerInterval3 a,M8FlowerInterval scale)
{
    a.x=M8FlowerIMul(a.x,scale);a.y=M8FlowerIMul(a.y,scale);a.z=M8FlowerIMul(a.z,scale);
    return a;
}

void M8FlowerSkinEnclose(inout M8FlowerInterval3 bound,M8FlowerInterval3 value)
{
    bound.x=M8FlowerI(min(bound.x.lo,value.x.lo),max(bound.x.hi,value.x.hi));
    bound.y=M8FlowerI(min(bound.y.lo,value.y.lo),max(bound.y.hi,value.y.hi));
    bound.z=M8FlowerI(min(bound.z.lo,value.z.lo),max(bound.z.hi,value.z.hi));
}

// Evaluate a CERTAIN symbol on its ORIGINAL loop. The integer conversion,
// canonical radius and grid transform are enclosed before projection.
bool M8FlowerSkinRootWorld(M8FlowerPhaseRootEvidence root,out M8FlowerInterval3 world)
{
    world=M8FlowerSkinPoint(0.0.xxx);
    if(root.Classification!=1u || !M8FlowerPhaseIdentityValid(root))return false;
    uint level=root.Tag&7u,lineClass=(root.Tag>>3u)&15u;
    if(level>2u || lineClass>=13u)return false;
    float3 junction=float3(root.Junction);
    M8FlowerInterval halfStep=M8FlowerI(0.5*M8FlowerLevelStep(level),
        0.5*M8FlowerLevelStep(level));
    M8FlowerInterval3 centre;
    centre.x=M8FlowerIMul(M8FlowerI(M8FlowerPrevious(junction.x),M8FlowerNext(junction.x)),halfStep);
    centre.y=M8FlowerIMul(M8FlowerI(M8FlowerPrevious(junction.y),M8FlowerNext(junction.y)),halfStep);
    centre.z=M8FlowerIMul(M8FlowerI(M8FlowerPrevious(junction.z),M8FlowerNext(junction.z)),halfStep);
    centre=M8FlowerSkinTransform(_MerkabaGridToWorld,centre);
    float radius=M8FlowerGeometryLoopRadius[level*13u+lineClass];
    M8FlowerInterval radiusBound=M8FlowerI(M8FlowerPrevious(radius),M8FlowerNext(radius));
    M8FlowerInterval3 axis1=M8FlowerObservedLoopAxis(M8FlowerLineE1[lineClass],radiusBound,_MerkabaGridToWorld);
    M8FlowerInterval3 axis2=M8FlowerObservedLoopAxis(M8FlowerLineE2[lineClass],radiusBound,_MerkabaGridToWorld);
    world=M8FlowerSkinAdd(M8FlowerSkinAdd(centre,M8FlowerSkinScale(axis1,root.Root.x)),
        M8FlowerSkinScale(axis2,root.Root.y));
    return all(isfinite(float4(world.x.lo,world.x.hi,world.y.lo,world.y.hi))) &&
        all(isfinite(float2(world.z.lo,world.z.hi)));
}

// Inverse of the frozen ordered barycentric chamber, applied to a surface
// footprint only. These interpolated locations are NOT new world knots.
// The three columns are high, (high+middle)/2, (high+middle+low)/3.
void M8FlowerSkinChamberFootprint(M8FlowerInterval3 parent[3],int3 order,
    out M8FlowerInterval3 child[3])
{
    child[0]=parent[order.x];
    child[1]=M8FlowerSkinScale(M8FlowerSkinAdd(parent[order.x],parent[order.y]),
        M8FlowerI(0.5,0.5));
    M8FlowerInterval third;
    M8FlowerIDivPositive(M8FlowerI(1.0,1.0),M8FlowerI(3.0,3.0),third);
    child[2]=M8FlowerSkinScale(M8FlowerSkinAdd(M8FlowerSkinAdd(parent[order.x],
        parent[order.y]),parent[order.z]),third);
}

void M8FlowerSkinAddChildFootprint(uint child,M8FlowerInterval3 footprintTriangle[3],
    inout M8FlowerInterval3 footprints[7],inout uint covered)
{
    if((covered&(1u<<child))==0u)footprints[child]=footprintTriangle[0];
    else M8FlowerSkinEnclose(footprints[child],footprintTriangle[0]);
    M8FlowerSkinEnclose(footprints[child],footprintTriangle[1]);
    M8FlowerSkinEnclose(footprints[child],footprintTriangle[2]);
    covered|=1u<<child;
}

// parentOrdinal is the existing 57-bit THREAD-parent address. All lookups
// below invert the immutable embroidery, never scan history or camera LOD.
bool M8FlowerSkinParentAddress(uint parentOrdinal,out uint depth,out uint c3,out uint c4)
{
    depth=1u;c3=0u;c4=0u;
    if(parentOrdinal>=57u)return false;
    if(parentOrdinal==0u)return true;
    if(parentOrdinal<8u)
    {
        depth=2u;
        c3=M8FlowerSkinThreadToCanonical[57u*(parentOrdinal-1u)];
        return c3<7u;
    }
    depth=3u;
    uint j4=parentOrdinal-8u;
    uint canonical=M8FlowerSkinThreadToCanonical[57u*(j4/7u)+1u+8u*(j4%7u)];
    if(canonical<7u || canonical>=56u)return false;
    c3=(canonical-7u)/7u;c4=(canonical-7u)%7u;
    return true;
}

// Enumerate the actual generated chamber union, at exactly the requested
// scan parent. A box encloses each COMPLETE union; extra covered image
// texels can widen RGB evidence but never manufacture a distinction.
// Empty generated children stay absent from covered; they are not painted.
bool M8FlowerSkinChildFootprints(M8FlowerPhaseRootEvidence roots[7],uint parentOrdinal,
    out M8FlowerInterval3 footprints[7],out uint covered)
{
    covered=0u;
    uint depth,c3,c4;
    if(!M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4))return false;
    M8FlowerInterval3 sites[7];
    [loop]for(uint site=0u;site<7u;site++)
    {
        footprints[site]=M8FlowerSkinPoint(0.0.xxx);
        if(!M8FlowerSkinRootWorld(roots[site],sites[site]))return false;
    }
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        uint3 indices=M8FlowerSkinWedgeSites(wedge);
        M8FlowerInterval3 p0[3];
        p0[0]=sites[indices.x];p0[1]=sites[indices.y];p0[2]=sites[indices.z];
        [loop]for(uint order3=0u;order3<6u;order3++)
        {
            int4 rule3=M8FlowerSkinChamber[6u*wedge+order3];
            uint meta3=asuint(rule3.w),child3=meta3&255u;
            if(depth>1u && child3!=c3)continue;
            M8FlowerInterval3 p3[3];M8FlowerSkinChamberFootprint(p0,rule3.xyz,p3);
            if(depth==1u){M8FlowerSkinAddChildFootprint(child3,p3,footprints,covered);continue;}
            uint wedge3=(meta3>>8u)&255u;
            [loop]for(uint order4=0u;order4<6u;order4++)
            {
                int4 rule4=M8FlowerSkinChamber[6u*wedge3+order4];
                uint meta4=asuint(rule4.w),child4=meta4&255u;
                if(depth>2u && child4!=c4)continue;
                M8FlowerInterval3 p4[3];M8FlowerSkinChamberFootprint(p3,rule4.xyz,p4);
                if(depth==2u){M8FlowerSkinAddChildFootprint(child4,p4,footprints,covered);continue;}
                uint wedge4=(meta4>>8u)&255u;
                [loop]for(uint order5=0u;order5<6u;order5++)
                {
                    int4 rule5=M8FlowerSkinChamber[6u*wedge4+order5];
                    M8FlowerInterval3 p5[3];M8FlowerSkinChamberFootprint(p4,rule5.xyz,p5);
                    M8FlowerSkinAddChildFootprint(asuint(rule5.w)&255u,p5,footprints,covered);
                }
            }
        }
    }
    return true;
}

bool M8FlowerSkinProjectRgb(uint eye,M8FlowerInterval3 world,
    out M8FlowerInterval x,out M8FlowerInterval y,out int2 resolution)
{
    x=M8FlowerI(0.0,0.0);y=x;resolution=0;
    if(eye>=2u)return false;
    float3 origin=eye==0u?_MerkabaCameraPositionLeft:_MerkabaCameraPositionRight;
    float4x4 rotation=eye==0u?_MerkabaCameraInverseRotationLeft:_MerkabaCameraInverseRotationRight;
    float2 focal=eye==0u?_MerkabaCameraFocalLengthLeft:_MerkabaCameraFocalLengthRight;
    float2 principal=eye==0u?_MerkabaCameraPrincipalPointLeft:_MerkabaCameraPrincipalPointRight;
    float2 sensor=eye==0u?_MerkabaCameraSensorResolutionLeft:_MerkabaCameraSensorResolutionRight;
    float2 size=eye==0u?_MerkabaCameraCurrentResolutionLeft:_MerkabaCameraCurrentResolutionRight;
    uint width,height;
    if(eye==0u)_MerkabaCameraRgbLeft.GetDimensions(width,height);
    else _MerkabaCameraRgbRight.GetDimensions(width,height);
    if(any(size!=float2(width,height)) || any(size<2.0) || any(sensor<=0.0) || any(focal<=0.0) ||
        !all(isfinite(float4(sensor,size))) || !all(isfinite(float4(focal,principal))))return false;
    world.x=M8FlowerISub(world.x,M8FlowerI(origin.x,origin.x));
    world.y=M8FlowerISub(world.y,M8FlowerI(origin.y,origin.y));
    world.z=M8FlowerISub(world.z,M8FlowerI(origin.z,origin.z));
    M8FlowerInterval3 local=M8FlowerSkinTransform(rotation,world);
    float error=_M8DepthErrorBounds[eye].y;
    if(!isfinite(error) || error<0.0)return false;
    M8FlowerInterval reprojection=M8FlowerI(-error,error);
    local.x=M8FlowerIAdd(local.x,reprojection);local.y=M8FlowerIAdd(local.y,reprojection);
    local.z=M8FlowerIAdd(local.z,reprojection);
    if(!M8FlowerIDivPositive(local.x,local.z,x) || !M8FlowerIDivPositive(local.y,local.z,y))return false;
    M8FlowerInterval sx,sy;
    if(!M8FlowerIDivPositive(M8FlowerI(size.x,size.x),M8FlowerI(sensor.x,sensor.x),sx) ||
        !M8FlowerIDivPositive(M8FlowerI(size.y,size.y),M8FlowerI(sensor.y,sensor.y),sy))return false;
    M8FlowerInterval scale=M8FlowerI(max(sx.lo,sy.lo),max(sx.hi,sy.hi));
    x=M8FlowerISub(M8FlowerIAdd(M8FlowerIMul(x,M8FlowerI(focal.x,focal.x)),
        M8FlowerI(principal.x,principal.x)),M8FlowerI(0.5*sensor.x,0.5*sensor.x));
    y=M8FlowerISub(M8FlowerIAdd(M8FlowerIMul(y,M8FlowerI(focal.y,focal.y)),
        M8FlowerI(principal.y,principal.y)),M8FlowerI(0.5*sensor.y,0.5*sensor.y));
    x=M8FlowerISub(M8FlowerIAdd(M8FlowerIMul(x,scale),M8FlowerI(0.5*size.x,0.5*size.x)),M8FlowerI(0.5,0.5));
    y=M8FlowerISub(M8FlowerIAdd(M8FlowerIMul(y,scale),M8FlowerI(0.5*size.y,0.5*size.y)),M8FlowerI(0.5,0.5));
    resolution=int2(width,height);
    return all(isfinite(float4(x.lo,x.hi,y.lo,y.hi))) && x.lo>=0.0 && y.lo>=0.0 &&
        x.hi<size.x-1.0 && y.hi<size.y-1.0;
}

// Every texel that any bilinear cell in the projected footprint can read
// participates. This is a finite image rectangle reduction, not sparse
// root/centroid colour sampling and not a clamp-at-FOV fallback.
bool M8FlowerSkinRgbFootprint(uint eye,M8FlowerInterval3 world,out M8FlowerInterval3 rgb)
{
    rgb=M8FlowerSkinPoint(0.0.xxx);
    M8FlowerInterval x,y;int2 resolution;
    if(!M8FlowerSkinProjectRgb(eye,world,x,y,resolution))return false;
    int2 first=int2(floor(x.lo),floor(y.lo));
    int2 last=int2(floor(x.hi),floor(y.hi))+1;
    float3 lower=float3(65504.0,65504.0,65504.0),upper=0.0.xxx;
    [loop]for(int py=first.y;py<=last.y;py++)
    [loop]for(int px=first.x;px<=last.x;px++)
    {
        float3 captured=eye==0u?_MerkabaCameraRgbLeft.Load(int3(px,py,0)).rgb:
            _MerkabaCameraRgbRight.Load(int3(px,py,0)).rgb;
        if(!all(isfinite(captured)) || any(captured<0.0) || any(captured>65504.0))return false;
        lower=min(lower,captured);upper=max(upper,captured);
    }
    float3 error=_M8RgbErrorBounds[eye].xyz;
    if(!all(isfinite(error)) || any(error<0.0))return false;
    rgb.x=M8FlowerI(max(0.0,M8FlowerPrevious(lower.x-error.x)),M8FlowerNext(upper.x+error.x));
    rgb.y=M8FlowerI(max(0.0,M8FlowerPrevious(lower.y-error.y)),M8FlowerNext(upper.y+error.y));
    rgb.z=M8FlowerI(max(0.0,M8FlowerPrevious(lower.z-error.z)),M8FlowerNext(upper.z+error.z));
    return true;
}

// All strict joint measurements covering the complete projected footprint
// must still belong to this R1 carrier. This rejects invalid depth, nearer
// incompatible matter and frozen-observation holes before assigning RGB.
bool M8FlowerSkinDirectFootprint(M8FlowerInterval3 world,int3 owner,uint ownerFlags,
    float normalError,float offsetError)
{
    if(!M8SupportInsideMutationScope(world))return false;
    int2 first,last;float unusedDepth;
    if(!M8DepthProjectSupport(float3(world.x.lo,world.y.lo,world.z.lo),
        float3(world.x.hi,world.y.hi,world.z.hi),gsDepthView[0],gsDepthProj[0],
        int2(gsDepthTexSize),_M8DepthErrorBounds[0].y,first,last,unusedDepth))return false;
    [loop]for(int y=first.y;y<=last.y;y++)
    [loop]for(int x=first.x;x<=last.x;x++)
    {
        uint plane;float3 position;
        if(!M8FlowerMeasurement((uint)y*gsDepthTexSize.x+(uint)x,owner,plane,position) ||
            !M8FlowerCompatibleCarrier(ownerFlags,plane,normalError,offsetError))return false;
    }
    return true;
}

uint M8FlowerMeasureRgbSkinChildren(M8FlowerPhaseRootEvidence roots[7],uint parentOrdinal,
    int3 owner,uint ownerFlags,float normalError,float offsetError,
    out M8ThreadColorInterval children[7])
{
    M8FlowerInterval3 footprints[7];uint covered;
    [loop]for(uint child=0u;child<7u;child++)children[child]=(M8ThreadColorInterval)0;
    if(!M8FlowerSkinChildFootprints(roots,parentOrdinal,footprints,covered))
        return M8_FLOWER_SKIN_AMBIGUOUS;
    // A hole in the generated fixed topology is an implementation error,
    // not an observation that needs a different camera viewpoint.
    if(covered!=0x7fu)return M8_FLOWER_SKIN_INVALID_FOOTPRINT;
    float2 intervals[21];uint certain=0u;
    [loop]for(uint child=0u;child<7u;child++)
    {
        if(!M8FlowerSkinDirectFootprint(footprints[child],owner,ownerFlags,normalError,offsetError))
            return M8_FLOWER_SKIN_AMBIGUOUS;
        M8FlowerInterval3 left,right;
        if(!M8FlowerSkinRgbFootprint(0u,footprints[child],left) ||
            !M8FlowerSkinRgbFootprint(1u,footprints[child],right))return M8_FLOWER_SKIN_AMBIGUOUS;
        float3 lower=max(float3(left.x.lo,left.y.lo,left.z.lo),float3(right.x.lo,right.y.lo,right.z.lo));
        float3 upper=min(float3(left.x.hi,left.y.hi,left.z.hi),float3(right.x.hi,right.y.hi,right.z.hi));
        if(any(lower>upper) || !M8ThreadEncodeColorInterval(float4(lower,1.0),float4(upper,1.0),children[child]))
            return M8_FLOWER_SKIN_AMBIGUOUS;
        // Classification encloses the actual binary16 values persisted.
        uint2 lo=children[child].LowerLinearRgba,hi=children[child].UpperLinearRgba;
        intervals[3u*child]=float2(f16tof32(lo.x&65535u),f16tof32(hi.x&65535u));
        intervals[3u*child+1u]=float2(f16tof32(lo.x>>16u),f16tof32(hi.x>>16u));
        intervals[3u*child+2u]=float2(f16tof32(lo.y&65535u),f16tof32(hi.y&65535u));
        certain|=1u<<child;
    }
    return M8FlowerClassifyRgbSkinSplit(intervals,certain);
}

bool M8FlowerSkinCarrierIdentity(int3 owner,uint flowerKey,
    M8FlowerPhaseRootEvidence roots[7])
{
    uint petal=(flowerKey>>M8_FLOWER_L2_KEY_PETAL_SHIFT)&63u;
    if(!M8FlowerL2KeyValid(flowerKey) ||
        ((flowerKey>>M8_FLOWER_L2_KEY_ROOT_SHIFT)&1u)!=((roots[0].Tag>>7u)&1u) ||
        ((flowerKey>>M8_FLOWER_L2_KEY_SECTOR_SHIFT)&31u)!=((roots[0].Tag>>8u)&31u))return false;
    uint hub=M8FlowerL2WedgeIndex(petal,flowerKey&15u)/6u;
    [loop]for(uint site=0u;site<7u;site++)
    {
        uint knot=M8FlowerL2CarrierKnot(hub,site);
        M8FlowerGeometryNode identity;
        if(!M8FlowerL2GeometryNode(knot,((roots[site].Tag>>7u)&1u)!=0u,identity) ||
            roots[site].Classification!=1u || !M8FlowerPhaseIdentityValid(roots[site]) ||
            (roots[site].Tag&7u)!=identity.Level ||
            ((roots[site].Tag>>3u)&15u)!=identity.Line)return false;
        int3 junction;
        if(!M8FlowerPhaseJunction(owner,identity.Level+1u,identity.Offset,junction) ||
            any(roots[site].Junction!=junction))return false;
    }
    return true;
}

// Caller supplies a genuinely CERTAIN seven-site carrier selected by the
// direct/flag predicate, not a guessed sign mask. This commits measurement
// only: geometry admission and Thread storage do not select each other.
uint M8FlowerCommitRgbSkinSplit(uint slot,uint local,uint slotGeneration,uint flowerKey,
    uint parentOrdinal,M8FlowerPhaseRootEvidence roots[7],float normalError,float offsetError,
    out uint classification,out bool changed)
{
    classification=M8_FLOWER_SKIN_AMBIGUOUS;changed=false;
    if(slot>=32768u || local>=512u || parentOrdinal>=57u)return M8_FLOWER_ARENA_INVALID;
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)],meta=_M8TileRecords[M8TileMetaIndex(slot)];
    if(runtime.w!=slotGeneration || meta.x>=MERKABA_M8_CHUNK_CAPACITY || meta.y>=64u ||
        _M8ChunkTileRefsRead[meta.x*64u+meta.y]!=slot+1u)return M8_FLOWER_SIDECAR_STALE_SLOT;
    KernelState state=M8LoadKernelStateRead(slot,local);
    if((state.flags&(M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID|M8_FLOWER_SEED_FLAG))!=
        (M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID))return M8_FLOWER_ARENA_INVALID;
    int3 owner=M8GlobalKernelCoord(slot,local);
    if(!M8FlowerSkinCarrierIdentity(owner,flowerKey,roots))return M8_FLOWER_ARENA_INVALID;
    uint ownerRef=M8FlowerFindOwner(slot,local,slotGeneration),epoch=M8FlowerGetOwnerEpoch(ownerRef);
    M8ThreadRun run;uint runRef;
    bool existing=M8FlowerFindThreadRun(ownerRef,flowerKey,epoch,run,runRef);
    uint2 bits=uint2(run.SplitBitsLo,run.SplitBitsHi);
    if(parentOrdinal!=0u)
    {
        uint predecessor=parentOrdinal<8u?0u:1u+(parentOrdinal-8u)/7u;
        if(!existing || (bits[predecessor>>5u]&(1u<<(predecessor&31u)))==0u)
        {classification=M8_FLOWER_SKIN_UNIFORM;return M8_FLOWER_ARENA_OK;}
    }
    M8ThreadColorInterval canonical[7],thread[7];
    classification=M8FlowerMeasureRgbSkinChildren(roots,parentOrdinal,owner,
        state.flags,normalError,offsetError,canonical);
    if(classification==M8_FLOWER_SKIN_INVALID_FOOTPRINT)return M8_FLOWER_ARENA_INVALID;
    bool replacing=existing && (bits[parentOrdinal>>5u]&(1u<<(parentOrdinal&31u)))!=0u;
    if(classification==M8_FLOWER_SKIN_AMBIGUOUS ||
        (classification==M8_FLOWER_SKIN_UNIFORM && !replacing))return M8_FLOWER_ARENA_OK;
    uint depth,c3,c4;
    M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4);
    [loop]for(uint child=0u;child<7u;child++)
    {
        uint rank=depth==1u?M8FlowerSkinL3ChildRank[child]:depth==2u?
            M8FlowerSkinL4ChildRank[7u*c3+child]:M8FlowerSkinL5ChildRank[49u*c3+7u*c4+child];
        thread[rank]=canonical[child];
    }
    if(replacing)
    {
        uint address=112u*(run.GroupBase+M8FlowerSplitRank(bits,parentOrdinal));
        if(!M8FlowerThreadRange(address,112u))return M8_FLOWER_ARENA_INVALID;
        bool same=true;
        [loop]for(uint child=0u;child<7u;child++)
        {
            uint4 value=_M8ThreadAtlasPages.Load4(address+16u*child);
            same= same && all(value==uint4(thread[child].LowerLinearRgba,thread[child].UpperLinearRgba));
        }
        if(same)return M8_FLOWER_ARENA_OK;
    }
    uint status=M8_FLOWER_ARENA_OK;
    if(ownerRef==0u)
    {
        status=M8FlowerEnsureOwner(slot,local,slotGeneration,state.flags,_M8DualPublishingGeneration,ownerRef);
        if(status!=M8_FLOWER_ARENA_OK)return status;
        // If the subsequent non-spinning group allocation yields, this
        // successful publication is still progress for the frozen workset.
        M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
    status=M8FlowerCommitThreadGroup(ownerRef,flowerKey,parentOrdinal,thread,
        _M8DualPublishingGeneration,_M8DualRetiredGeneration);
    if(status==M8_FLOWER_ARENA_OK)
    {
        changed=true;M8MarkTileDirty(slot);
        InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK],4u);
    }
    return status;
}

// Finite scan-owned subdivision program for ONE already admitted carrier.
// A caller retains canonicalCursor with the SAME immutable observation when
// this returns BUSY/CAPACITY or quantum exhausts. Genuine interval ambiguity
// consumes the candidate; a generated footprint failure does not.
uint M8FlowerDrainRgbSkinCarrier(uint slot,uint local,uint slotGeneration,uint flowerKey,
    M8FlowerPhaseRootEvidence roots[7],float normalError,float offsetError,uint quantum,
    inout uint canonicalCursor,out uint progressed,out uint ambiguous)
{
    progressed=0u;ambiguous=0u;
    if(canonicalCursor>57u)return M8_FLOWER_ARENA_INVALID;
    uint consumed=0u;
    [loop]while(canonicalCursor<57u && consumed<quantum)
    {
        // Work visits canonical parents in level order. Embroidery ranks
        // translate storage addresses ONLY; Fibonacci never orders scan.
        uint parentOrdinal=0u;
        if(canonicalCursor>0u && canonicalCursor<8u)
            parentOrdinal=1u+M8FlowerSkinL3ChildRank[canonicalCursor-1u];
        else if(canonicalCursor>=8u)
            parentOrdinal=8u+M8FlowerSkinL4ParentThread[canonicalCursor-8u];
        uint classification;bool changed;
        uint status=M8FlowerCommitRgbSkinSplit(slot,local,slotGeneration,flowerKey,
            parentOrdinal,roots,normalError,offsetError,classification,changed);
        if(status!=M8_FLOWER_ARENA_OK)return status;
        if(classification==M8_FLOWER_SKIN_AMBIGUOUS)ambiguous++;
        // Traversal progress is legitimate compute progress independently
        // of whether this observation changes an already identical group.
        canonicalCursor++;consumed++;progressed++;
        if(canonicalCursor==1u)
        {
            uint ownerRef=M8FlowerFindOwner(slot,local,slotGeneration);
            M8ThreadRun run;uint runRef;
            if(!M8FlowerFindThreadRun(ownerRef,flowerKey,M8FlowerGetOwnerEpoch(ownerRef),run,runRef) ||
                (run.SplitBitsLo&1u)==0u)
            {
                // No scan-authored RGB root split: there are no legal child
                // split candidates. The fixed logical 399 sites still exist.
                canonicalCursor=57u;
            }
        }
    }
    return M8_FLOWER_ARENA_OK;
}

#endif
