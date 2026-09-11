#ifndef GENESIS_MERKABA_FLOWER_SKIN_OBSERVATION_INCLUDED
#define GENESIS_MERKABA_FLOWER_SKIN_OBSERVATION_INCLUDED

#include "MerkabaFlowerGeometry.hlsl"

// These are the SAME calibrated bounds retained by StereoFlowerRefine for
// this immutable observation. No presentation light or colour score enters
// captured-radiance admission.
float4 _M8RgbErrorBounds[2];

// Immutable sites of the current SignalItem, loaded once by seven lanes.
// The metric reduction uses eight lanes per child, never a private 7-site
// array per pixel worker. All 64 lanes participate in its phase barriers.
groupshared M8FlowerInterval3 m8SkinSites[7];
groupshared uint4 m8SkinReduction[128];

M8FlowerInterval3 M8FlowerSkinPoint(float3 p)
{
    M8FlowerInterval3 result;
    result.x=M8FlowerI(p.x,p.x);result.y=M8FlowerI(p.y,p.y);result.z=M8FlowerI(p.z,p.z);
    return result;
}

M8FlowerInterval3 M8FlowerSkinTransform(float4x4 transformMatrix,M8FlowerInterval3 p)
{
    M8FlowerInterval components[3];
    [loop]for(uint axis=0u;axis<3u;axis++)
        components[axis]=M8DepthIntervalRow(transformMatrix[axis],p);
    M8FlowerInterval3 result;
    result.x=components[0];result.y=components[1];result.z=components[2];
    return result;
}

M8FlowerInterval3 M8FlowerSkinAdd(M8FlowerInterval3 a,M8FlowerInterval3 b)
{
    M8FlowerInterval left[3],right[3];
    left[0]=a.x;left[1]=a.y;left[2]=a.z;
    right[0]=b.x;right[1]=b.y;right[2]=b.z;
    [loop]for(uint axis=0u;axis<3u;axis++)left[axis]=M8FlowerIAdd(left[axis],right[axis]);
    a.x=left[0];a.y=left[1];a.z=left[2];
    return a;
}

M8FlowerInterval3 M8FlowerSkinScale(M8FlowerInterval3 a,M8FlowerInterval scale)
{
    M8FlowerInterval components[3];
    components[0]=a.x;components[1]=a.y;components[2]=a.z;
    [loop]for(uint axis=0u;axis<3u;axis++)components[axis]=M8FlowerIMul(components[axis],scale);
    a.x=components[0];a.y=components[1];a.z=components[2];
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
    M8FlowerInterval components[3];
    [loop]for(uint axis=0u;axis<3u;axis++)
        components[axis]=M8FlowerIMul(M8FlowerI(M8FlowerPrevious(junction[axis]),
            M8FlowerNext(junction[axis])),halfStep);
    M8FlowerInterval3 centre;
    centre.x=components[0];centre.y=components[1];centre.z=components[2];
    centre=M8FlowerSkinTransform(_MerkabaGridToWorld,centre);
    float radius=M8FlowerGeometryLoopRadiusAt(level*13u+lineClass);
    M8FlowerInterval radiusBound=M8FlowerI(M8FlowerPrevious(radius),M8FlowerNext(radius));
    M8FlowerInterval3 axes[2];
    [loop]for(uint axis=0u;axis<2u;axis++)
        axes[axis]=M8FlowerObservedLoopAxis(axis==0u?M8FlowerLineE1At(lineClass):
            M8FlowerLineE2At(lineClass),radiusBound,_MerkabaGridToWorld);
    world=M8FlowerSkinAdd(M8FlowerSkinAdd(centre,M8FlowerSkinScale(axes[0],root.Root.x)),
        M8FlowerSkinScale(axes[1],root.Root.y));
    return all(M8FlowerIsFinite(float4(world.x.lo,world.x.hi,world.y.lo,world.y.hi))) &&
        all(M8FlowerIsFinite(float2(world.z.lo,world.z.hi)));
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
    float2 generatedThird=M8FlowerSkinThirdAt(0u);
    M8FlowerInterval third=M8FlowerI(generatedThird.x,generatedThird.y);
    child[2]=M8FlowerSkinScale(M8FlowerSkinAdd(M8FlowerSkinAdd(parent[order.x],
        parent[order.y]),parent[order.z]),third);
}

// parentOrdinal is the existing 57-bit THREAD-parent address. Its canonical
// identity and clipped support span are resolved once by the Editor codegen.
bool M8FlowerSkinParentAddress(uint parentOrdinal,out uint depth,out uint c3,out uint c4)
{
    depth=1u;c3=0u;c4=0u;
    if(parentOrdinal>=57u)return false;
    uint address=M8FlowerSkinParentWorkAt(parentOrdinal).z;
    depth=address&3u;c3=(address>>2u)&7u;c4=(address>>5u)&7u;
    return true;
}

// Execute the generated pullback on THIS measured L2 carrier. There is no
// child search/branch construction here. The three bounded applications keep
// the original interval operation order; chart-only work is already in SRV.
void M8FlowerSkinWorkFootprint(uint4 work,
    out M8FlowerInterval3 footprintTriangle[3])
{
    uint3 indices=M8FlowerSkinWedgeSites(work.x&7u);
    footprintTriangle[0]=m8SkinSites[indices.x];
    footprintTriangle[1]=m8SkinSites[indices.y];
    footprintTriangle[2]=m8SkinSites[indices.z];
    uint depth=(work.x>>6u)&3u;
    [loop]for(uint level=0u;level<depth;level++)
    {
        uint rule=(work.y>>(6u*level))&63u;
        M8FlowerInterval3 next[3];
        M8FlowerSkinChamberFootprint(footprintTriangle,M8FlowerSkinChamberAt(rule).xyz,next);
        [unroll]for(uint vertex=0u;vertex<3u;vertex++)footprintTriangle[vertex]=next[vertex];
    }
}

// Consume only this parent's pre-generated complete chamber span. A box
// encloses each COMPLETE child union; extra covered image texels may widen
// evidence but cannot manufacture a distinction. Logical-empty children are
// still implicit Flower-7 addresses, never an existence/allocation decision.
bool M8FlowerSkinChildFootprint(uint parentOrdinal,
    uint activeWedgeMask,uint wantedChild,out M8FlowerInterval3 footprint,out bool covered)
{
    covered=false;footprint=M8FlowerSkinPoint(0.0.xxx);
    uint depth,c3,c4;
    if(activeWedgeMask==0u || activeWedgeMask>=64u || wantedChild>=7u ||
        !M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4))return false;
    uint2 childWork=M8FlowerSkinChildWorkAt(7u*parentOrdinal+wantedChild);
    [loop]for(uint index=0u;index<childWork.y;index++)
    {
        uint4 work=M8FlowerSkinFootprintWorkAt(childWork.x+index);
        uint wedge=work.x&7u;
        if((activeWedgeMask&(1u<<wedge))==0u)continue;
        M8FlowerInterval3 footprintTriangle[3];
        M8FlowerSkinWorkFootprint(work,footprintTriangle);
        if(!covered)footprint=footprintTriangle[0];
        else M8FlowerSkinEnclose(footprint,footprintTriangle[0]);
        M8FlowerSkinEnclose(footprint,footprintTriangle[1]);
        M8FlowerSkinEnclose(footprint,footprintTriangle[2]);
        covered=true;
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
        !all(M8FlowerIsFinite(float4(sensor,size))) || !all(M8FlowerIsFinite(float4(focal,principal))))return false;
    world.x=M8FlowerISub(world.x,M8FlowerI(origin.x,origin.x));
    world.y=M8FlowerISub(world.y,M8FlowerI(origin.y,origin.y));
    world.z=M8FlowerISub(world.z,M8FlowerI(origin.z,origin.z));
    M8FlowerInterval3 local=M8FlowerSkinTransform(rotation,world);
    float error=_M8DepthErrorBounds[eye].y;
    if(!M8FlowerIsFinite(error) || error<0.0)return false;
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
    return all(M8FlowerIsFinite(float4(x.lo,x.hi,y.lo,y.hi))) && x.lo>=0.0 && y.lo>=0.0 &&
        x.hi<size.x-1.0 && y.hi<size.y-1.0;
}

// Every texel that any bilinear cell in the projected footprint can read
// participates. This is a finite image rectangle reduction, not sparse
// root/centroid colour sampling and not a clamp-at-FOV fallback.
bool M8FlowerSkinRgbFootprint(uint eye,M8FlowerInterval3 world,uint pixelLane,out M8FlowerInterval3 rgb)
{
    rgb=M8FlowerSkinPoint(0.0.xxx);
    M8FlowerInterval x,y;int2 resolution;
    if(!M8FlowerSkinProjectRgb(eye,world,x,y,resolution))return false;
    int2 first=int2(floor(x.lo),floor(y.lo));
    int2 last=int2(floor(x.hi),floor(y.hi))+1;
    float3 lower=asfloat(0x7f800000u).xxx,upper=-asfloat(0x7f800000u).xxx;
    uint width=(uint)(last.x-first.x+1),count=width*(uint)(last.y-first.y+1);
    [loop]for(uint pixel=pixelLane;pixel<count;pixel+=8u)
    {
        int px=first.x+(int)(pixel%width),py=first.y+(int)(pixel/width);
        float3 captured=eye==0u?_MerkabaCameraRgbLeft.Load(int3(px,py,0)).rgb:
            _MerkabaCameraRgbRight.Load(int3(px,py,0)).rgb;
        if(!all(M8FlowerIsFinite(captured)) || any(captured<0.0) || any(captured>65504.0))return false;
        lower=min(lower,captured);upper=max(upper,captured);
    }
    // Empty lane stripes retain +/-infinity identities. Calibrated RGB
    // uncertainty is applied once after the complete eight-lane hull.
    rgb.x=M8FlowerI(lower.x,upper.x);rgb.y=M8FlowerI(lower.y,upper.y);
    rgb.z=M8FlowerI(lower.z,upper.z);
    return true;
}

// All strict joint measurements covering the complete projected footprint
// must still belong to this R1 carrier. This rejects invalid depth, nearer
// incompatible matter and frozen-observation holes before assigning RGB.
bool M8FlowerSkinDirectFootprint(M8FlowerInterval3 world,int3 owner,uint ownerFlags,
    float normalError,float offsetError,uint pixelLane)
{
    if(!M8SupportInsideMutationScope(world))return false;
    int2 first,last;float unusedDepth;
    if(!M8DepthProjectSupport(float3(world.x.lo,world.y.lo,world.z.lo),
        float3(world.x.hi,world.y.hi,world.z.hi),gsDepthView[0],gsDepthProj[0],
        int2(gsDepthTexSize),_M8DepthErrorBounds[0].y,first,last,unusedDepth))return false;
    uint width=(uint)(last.x-first.x+1),count=width*(uint)(last.y-first.y+1);
    [loop]for(uint pixel=pixelLane;pixel<count;pixel+=8u)
    {
        int x=first.x+(int)(pixel%width),y=first.y+(int)(pixel/width);
        uint plane;float3 position;
        if(!M8FlowerMeasurement((uint)y*gsDepthTexSize.x+(uint)x,owner,plane,position) ||
            !M8FlowerCompatibleCarrier(ownerFlags,plane,normalError,offsetError))return false;
    }
    return true;
}

M8FlowerInterval3 M8FlowerSkinSubtract(M8FlowerInterval3 a,M8FlowerInterval3 b)
{
    M8FlowerInterval left[3],right[3];
    left[0]=a.x;left[1]=a.y;left[2]=a.z;
    right[0]=b.x;right[1]=b.y;right[2]=b.z;
    [loop]for(uint axis=0u;axis<3u;axis++)left[axis]=M8FlowerISub(left[axis],right[axis]);
    a.x=left[0];a.y=left[1];a.z=left[2];
    return a;
}

M8FlowerInterval3 M8FlowerSkinCross(M8FlowerInterval3 a,M8FlowerInterval3 b)
{
    M8FlowerInterval left[3],right[3],components[3];
    left[0]=a.x;left[1]=a.y;left[2]=a.z;
    right[0]=b.x;right[1]=b.y;right[2]=b.z;
    [loop]for(uint axis=0u;axis<3u;axis++)
    {
        uint next=(axis+1u)%3u,after=(axis+2u)%3u;
        components[axis]=M8FlowerISub(M8FlowerIMul(left[next],right[after]),
            M8FlowerIMul(left[after],right[next]));
    }
    M8FlowerInterval3 result;
    result.x=components[0];result.y=components[1];result.z=components[2];
    return result;
}

bool M8FlowerSkinDivide(M8FlowerInterval numerator,M8FlowerInterval denominator,
    out M8FlowerInterval result)
{
    result=M8FlowerI(0.0,0.0);
    if(denominator.hi<0.0)
    {
        numerator=M8FlowerI(-numerator.hi,-numerator.lo);
        denominator=M8FlowerI(-denominator.hi,-denominator.lo);
    }
    return M8FlowerIDivPositive(numerator,denominator,result);
}

// The frame comes from the evaluated carrier vertices, never a measured
// normal fit. The Gram inverse also projects a metric point onto this plane
// without changing its two material coordinates along the normal column.
struct M8FlowerSkinMetricFrame
{
    M8FlowerInterval3 Origin;
    M8FlowerInterval3 EdgeU;
    M8FlowerInterval3 EdgeV;
    M8FlowerInterval3 Normal;
    M8FlowerInterval UU;
    M8FlowerInterval UV;
    M8FlowerInterval VV;
    M8FlowerInterval Determinant;
};

bool M8FlowerSkinChartFrameFromTriangle(M8FlowerInterval3 footprintTriangle[3],
    out M8FlowerSkinMetricFrame frame)
{
    frame=(M8FlowerSkinMetricFrame)0;
    frame.Origin=footprintTriangle[0];
    frame.EdgeU=M8FlowerSkinSubtract(footprintTriangle[1],footprintTriangle[0]);
    frame.EdgeV=M8FlowerSkinSubtract(footprintTriangle[2],footprintTriangle[0]);
    frame.UU=M8FlowerObservedDot(frame.EdgeU,frame.EdgeU);
    frame.UV=M8FlowerObservedDot(frame.EdgeU,frame.EdgeV);
    frame.VV=M8FlowerObservedDot(frame.EdgeV,frame.EdgeV);
    frame.Determinant=M8FlowerISub(M8FlowerIMul(frame.UU,frame.VV),
        M8FlowerISquare(frame.UV));
    return frame.Determinant.lo>0.0;
}

bool M8FlowerSkinMetricFrameFromTriangle(M8FlowerInterval3 footprintTriangle[3],
    out M8FlowerSkinMetricFrame frame)
{
    if(!M8FlowerSkinChartFrameFromTriangle(footprintTriangle,frame))return false;
    M8FlowerInterval3 crossValue=M8FlowerSkinCross(frame.EdgeU,frame.EdgeV);
    M8FlowerInterval length;
    if(!M8FlowerISqrt(M8FlowerIAdd(M8FlowerIAdd(M8FlowerISquare(crossValue.x),
        M8FlowerISquare(crossValue.y)),M8FlowerISquare(crossValue.z)),length) ||
        !(length.lo>0.0))return false;
    M8FlowerInterval components[3],normalized[3];
    components[0]=crossValue.x;components[1]=crossValue.y;components[2]=crossValue.z;
    // One call site for the shared exact divider; do not clone its integer
    // enclosure implementation once per component during HLSL legalization.
    [loop]for(uint axis=0u;axis<3u;axis++)
        if(!M8FlowerIDivPositive(components[axis],length,normalized[axis]))return false;
    frame.Normal.x=normalized[0];frame.Normal.y=normalized[1];frame.Normal.z=normalized[2];
    return true;
}

bool M8FlowerSkinMetricCoordinates(M8FlowerSkinMetricFrame frame,
    M8FlowerInterval3 world,out M8FlowerInterval3 barycentric)
{
    barycentric=M8FlowerSkinPoint(0.0.xxx);
    M8FlowerInterval3 relative=M8FlowerSkinSubtract(world,frame.Origin);
    M8FlowerInterval projections[2],coordinates[2];
    [loop]for(uint axis=0u;axis<2u;axis++)
    {
        M8FlowerInterval3 edge;
        if(axis==0u)edge=frame.EdgeU;else edge=frame.EdgeV;
        projections[axis]=M8FlowerObservedDot(relative,edge);
    }
    [loop]for(uint axis=0u;axis<2u;axis++)
    {
        M8FlowerInterval diagonal;
        if(axis==0u)diagonal=frame.VV;else diagonal=frame.UU;
        if(!M8FlowerIDivPositive(M8FlowerISub(M8FlowerIMul(diagonal,projections[axis]),
            M8FlowerIMul(frame.UV,projections[1u-axis])),frame.Determinant,
            coordinates[axis]))return false;
    }
    barycentric.y=coordinates[0];barycentric.z=coordinates[1];
    barycentric.x=M8FlowerISub(M8FlowerI(1.0,1.0),
        M8FlowerIAdd(barycentric.y,barycentric.z));
    return true;
}

// The integer matrix columns are generated, not rediscovered by running the
// point evaluator three times. Only the measured coordinates remain runtime.
M8FlowerInterval3 M8FlowerSkinIntervalChamber(M8FlowerInterval3 value,uint rule)
{
    M8FlowerInterval components[3];
    [loop]for(uint axis=0u;axis<3u;axis++)
        components[axis]=M8DepthIntervalRow(M8FlowerSkinForwardRowAt(3u*(rule%6u)+axis),value);
    M8FlowerInterval3 result;
    result.x=components[0];result.y=components[1];result.z=components[2];
    return result;
}

// Intersection with the closed triangle is used for conservative pixel
// covers only. It is not a branch classifier: this chamber was selected by
// the immutable generated incidence before any depth sample was read.
bool M8FlowerSkinClipMetricCoordinates(inout M8FlowerInterval3 barycentric)
{
    float3 lo=max(float3(barycentric.x.lo,barycentric.y.lo,barycentric.z.lo),0.0.xxx);
    float3 hi=min(float3(barycentric.x.hi,barycentric.y.hi,barycentric.z.hi),1.0.xxx);
    if(any(lo>hi))return false;
    M8FlowerInterval sum=M8FlowerIAdd(M8FlowerIAdd(M8FlowerI(lo.x,hi.x),
        M8FlowerI(lo.y,hi.y)),M8FlowerI(lo.z,hi.z));
    if(sum.lo>1.0 || sum.hi<1.0)return false;
    barycentric.x=M8FlowerI(lo.x,hi.x);barycentric.y=M8FlowerI(lo.y,hi.y);
    barycentric.z=M8FlowerI(lo.z,hi.z);
    return true;
}

// Work selection over a measured footprint, not root/sector ownership. Every
// possibly intersected ordered chamber is retained, including shared edges.
// Complete-support measurement below still owns certainty and split creation.
uint M8FlowerSkinPossibleChambers(M8FlowerInterval3 barycentric,uint wedge)
{
    float3 lo=float3(barycentric.x.lo,barycentric.y.lo,barycentric.z.lo);
    float3 hi=float3(barycentric.x.hi,barycentric.y.hi,barycentric.z.hi);
    uint mask=0u;
    [loop]for(uint order=0u;order<6u;order++)
    {
        int3 p=M8FlowerSkinChamberAt(6u*wedge+order).xyz;
        if(hi[p.x]>=lo[p.y] && hi[p.y]>=lo[p.z] && hi[p.x]>=lo[p.z])mask|=1u<<order;
    }
    return mask;
}

uint M8FlowerSkinReachChamber(M8FlowerInterval3 parent,uint rule,
    out M8FlowerInterval3 child,out uint site,out uint wedge)
{
    uint address=asuint(M8FlowerSkinChamberAt(rule).w);
    site=address&255u;wedge=(address>>8u)&255u;
    child=M8FlowerSkinIntervalChamber(parent,rule);
    if(!all(M8FlowerIsFinite(float4(child.x.lo,child.x.hi,child.y.lo,child.y.hi))) ||
        !all(M8FlowerIsFinite(float2(child.z.lo,child.z.hi))))return 2u;
    return M8FlowerSkinClipMetricCoordinates(child)?1u:0u;
}

// Three generated descents. The resulting work unit is the seven-value split
// group, not a terminal color sample: all L5 siblings share one L4 parent bit.
// Stop after the first reached L5 sibling of that group; its complete seven
// footprints are measured together when the split predicate is evaluated.
bool M8FlowerSkinFootprintParents(M8FlowerInterval3 barycentric,uint wedge,out uint2 parents)
{
    parents=0u;
    if(!all(M8FlowerIsFinite(float4(barycentric.x.lo,barycentric.x.hi,
        barycentric.y.lo,barycentric.y.hi))) ||
        !all(M8FlowerIsFinite(float2(barycentric.z.lo,barycentric.z.hi))))return false;
    if(!M8FlowerSkinClipMetricCoordinates(barycentric))return true;
    uint pending3=M8FlowerSkinPossibleChambers(barycentric,wedge);
    [loop]while(pending3!=0u)
    {
        uint order3=(uint)firstbitlow(pending3);pending3&=pending3-1u;
        M8FlowerInterval3 bc3;uint c3,w3;
        uint state3=M8FlowerSkinReachChamber(barycentric,6u*wedge+order3,bc3,c3,w3);
        if(state3==2u)return false;
        if(state3==0u)continue;
        uint pending4=M8FlowerSkinPossibleChambers(bc3,w3);
        [loop]while(pending4!=0u)
        {
            uint order4=(uint)firstbitlow(pending4);pending4&=pending4-1u;
            M8FlowerInterval3 bc4;uint c4,w4;
            uint state4=M8FlowerSkinReachChamber(bc3,6u*w3+order4,bc4,c4,w4);
            if(state4==2u)return false;
            if(state4==0u)continue;
            uint pending5=M8FlowerSkinPossibleChambers(bc4,w4);
            [loop]while(pending5!=0u)
            {
                uint order5=(uint)firstbitlow(pending5);pending5&=pending5-1u;
                M8FlowerInterval3 bc5;uint c5,w5;
                uint state5=M8FlowerSkinReachChamber(bc4,6u*w4+order5,bc5,c5,w5);
                if(state5==2u)return false;
                if(state5==0u)continue;
                uint j3=M8FlowerSkinL3ChildRankAt(c3);
                uint parent4=8u+7u*j3+M8FlowerSkinL4ChildRankAt(7u*c3+c4);
                parents.x|=1u|(1u<<(1u+j3));
                parents[parent4>>5u]|=1u<<(parent4&31u);
                break;
            }
        }
    }
    return true;
}

M8FlowerInterval M8FlowerSkinBubbleInterval(M8FlowerInterval3 barycentric)
{
    M8FlowerInterval result=M8FlowerIMul(M8FlowerI(729.0,729.0),
        M8FlowerIMul(M8FlowerIMul(M8FlowerISquare(barycentric.x),
            M8FlowerISquare(barycentric.y)),M8FlowerISquare(barycentric.z)));
    // On the already clipped simplex 0 <= psi <= 1 exactly. This is a
    // polynomial bound, not a clamped amplitude or a novelty threshold.
    return M8FlowerI(max(0.0,result.lo),min(1.0,result.hi));
}

M8FlowerInterval M8FlowerSkinDecodeAmplitude(M8FlowerVInterval value)
{
    // The existing exact signed decoder encloses Q2.29. Multiplication by
    // eight is an exact power-of-two conversion to Q5.26, including INT_MIN.
    return M8FlowerI(M8FlowerDecodePhaseEndpoint(value.Lower,false)*8.0,
        M8FlowerDecodePhaseEndpoint(value.Upper,true)*8.0);
}

bool M8FlowerSkinEncodeAmplitude(M8FlowerInterval value,out M8FlowerVInterval result)
{
    result=(M8FlowerVInterval)0;
    precise float lower=floor(value.lo*67108864.0);
    precise float upper=ceil(value.hi*67108864.0);
    if(!all(M8FlowerIsFinite(float2(lower,upper))) || lower>upper ||
        lower< -2147483648.0 || upper>=2147483648.0)return false;
    result.Lower=(int)lower;result.Upper=(int)upper;
    return true;
}

bool M8FlowerSkinReadAmplitude(M8FlowerSkinMetricRun run,bool existing,
    uint level,uint c3,uint c4,out M8FlowerInterval amplitude)
{
    amplitude=M8FlowerI(0.0,0.0);
    if(!existing)return true;
    if(run.Reserved!=0u || !M8FlowerCanonicalSplit(uint2(run.SplitBitsLo,run.SplitBitsHi)))return false;
    uint group,rank;
    if(!M8FlowerSkinCompactChildAddress(run.GroupBase,run.SplitBitsLo,run.SplitBitsHi,
        level,c3,c4,0u,group,rank))return true; // An absent innovation is exactly zero.
    if(rank>=7u || group<run.GroupBase || group>0xffffffffu/56u ||
        !M8FlowerDetailRange(group*56u,56u))return false;
    uint2 words=M8_FLOWER_DETAIL_SOURCE.Load2(group*56u+rank*8u);
    M8FlowerVInterval value;value.Lower=asint(words.x);value.Upper=asint(words.y);
    if(value.Lower>value.Upper)return false;
    amplitude=M8FlowerSkinDecodeAmplitude(value);
    return true;
}

M8FlowerInterval3 M8FlowerSkinTriangleAt(M8FlowerInterval3 footprintTriangle[3],
    M8FlowerInterval3 barycentric)
{
    // Use the exact sum(lambda)=1 rather than multiplying the world origin
    // by three independently widened barycentric intervals.
    return M8FlowerSkinAdd(footprintTriangle[0],M8FlowerSkinAdd(
        M8FlowerSkinScale(M8FlowerSkinSubtract(footprintTriangle[1],footprintTriangle[0]),barycentric.y),
        M8FlowerSkinScale(M8FlowerSkinSubtract(footprintTriangle[2],footprintTriangle[0]),barycentric.z)));
}

// A joint texel has already passed BOTH depth views and BOTH captured RGB
// views. Its w=1 and the held calibration bounds are the evidence here;
// this consumer does not promote an unrefined depth pixel to stereo truth.
// The inverse projection below supports perspective and orthographic input.
bool M8FlowerSkinDepthCell(int2 pixel,M8FlowerInterval depth,bool wholePixel,
    out M8FlowerInterval3 world)
{
    world=M8FlowerSkinPoint(0.0.xxx);
    if(any(pixel<0) || any(pixel>=int2(gsDepthTexSize)) || !(depth.lo>0.0) ||
        !all(M8FlowerIsFinite(float2(depth.lo,depth.hi))))return false;
    float2 lo=float2(pixel)+(wholePixel?0.0:0.5);
    float2 hi=float2(pixel)+(wholePixel?1.0:0.5);
    M8FlowerInterval3 ndc;
    M8FlowerInterval projected[2];
    [loop]for(uint axis=0u;axis<2u;axis++)
        if(!M8FlowerIDivPositive(M8FlowerI(lo[axis],hi[axis]),
            M8FlowerI((float)gsDepthTexSize[axis],(float)gsDepthTexSize[axis]),
            projected[axis]))return false;
    ndc.x=projected[0];ndc.y=projected[1];
    ndc.x=M8FlowerISub(M8FlowerIMul(ndc.x,M8FlowerI(2,2)),M8FlowerI(1,1));
    ndc.y=M8FlowerISub(M8FlowerIMul(ndc.y,M8FlowerI(2,2)),M8FlowerI(1,1));
    ndc.z=M8FlowerI(0,0);
    // Solve view.z/view.w=-depth for clip.z; no fixed perspective-camera
    // simplification, depth epsilon, or sample at a guessed world position.
    M8FlowerInterval z0=M8DepthIntervalRow(gsDepthProjInv[0][2],ndc);
    M8FlowerInterval w0=M8DepthIntervalRow(gsDepthProjInv[0][3],ndc);
    M8FlowerInterval numerator=M8FlowerISub(M8FlowerI(0,0),
        M8FlowerIAdd(z0,M8FlowerIMul(depth,w0)));
    M8FlowerInterval denominator=M8FlowerIAdd(
        M8FlowerI(gsDepthProjInv[0][2][2],gsDepthProjInv[0][2][2]),
        M8FlowerIMul(depth,M8FlowerI(gsDepthProjInv[0][3][2],gsDepthProjInv[0][3][2])));
    if(!M8FlowerSkinDivide(numerator,denominator,ndc.z))return false;
    M8FlowerInterval3 view=M8FlowerSkinTransform(gsDepthProjInv[0],ndc);
    M8FlowerInterval w=M8DepthIntervalRow(gsDepthProjInv[0][3],ndc);
    M8FlowerInterval components[3],normalized[3];
    components[0]=view.x;components[1]=view.y;components[2]=view.z;
    [loop]for(uint axis=0u;axis<3u;axis++)
        if(!M8FlowerIDivPositive(components[axis],w,normalized[axis]))return false;
    view.x=normalized[0];view.y=normalized[1];view.z=normalized[2];
    float error=_M8DepthErrorBounds[0].y;
    if(!M8FlowerIsFinite(error) || error<0.0)return false;
    M8FlowerInterval reprojection=M8FlowerI(-error,error);
    view.x=M8FlowerIAdd(view.x,reprojection);
    view.y=M8FlowerIAdd(view.y,reprojection);
    view.z=M8FlowerIAdd(view.z,reprojection);
    world=M8FlowerSkinTransform(gsDepthViewInv[0],view);
    return all(M8FlowerIsFinite(float4(world.x.lo,world.x.hi,world.y.lo,world.y.hi))) &&
        all(M8FlowerIsFinite(float2(world.z.lo,world.z.hi)));
}

bool M8FlowerSkinMetricMeasurement(int2 pixel,int3 owner,uint ownerFlags,
    float normalError,float offsetError,out M8FlowerInterval depth,
    out M8FlowerInterval3 normal)
{
    depth=M8FlowerI(0,0);normal=M8FlowerSkinPoint(0.0.xxx);
    float4 errors=_M8DepthErrorBounds[0];
    if(errors.w!=1.0 || !all(M8FlowerIsFinite(errors.xyz)) || any(errors.xyz<0.0))return false;
    uint plane;float3 centre;
    if(!M8FlowerMeasurement((uint)pixel.y*gsDepthTexSize.x+(uint)pixel.x,
            owner,plane,centre) ||
        !M8FlowerCompatibleCarrier(ownerFlags,plane,normalError,offsetError))return false;
    float source=gsDepthTex.Load(int3(pixel,0));
    M8FlowerInterval3 clip;
    M8FlowerInterval projected[2];
    [loop]for(uint axis=0u;axis<2u;axis++)
        if(!M8FlowerIDivPositive(M8FlowerI(pixel[axis]+0.5,pixel[axis]+0.5),
            M8FlowerI((float)gsDepthTexSize[axis],(float)gsDepthTexSize[axis]),
            projected[axis]))return false;
    clip.x=projected[0];clip.y=projected[1];
    clip.x=M8FlowerISub(M8FlowerIMul(clip.x,M8FlowerI(2,2)),M8FlowerI(1,1));
    clip.y=M8FlowerISub(M8FlowerIMul(clip.y,M8FlowerI(2,2)),M8FlowerI(1,1));
    clip.z=M8FlowerISub(M8FlowerIMul(M8FlowerI(source,source),M8FlowerI(2,2)),M8FlowerI(1,1));
    M8FlowerInterval z=M8DepthIntervalRow(gsDepthProjInv[0][2],clip);
    M8FlowerInterval w=M8DepthIntervalRow(gsDepthProjInv[0][3],clip);
    if(!M8FlowerIDivPositive(M8FlowerI(-z.hi,-z.lo),w,depth))return false;
    // z is the excavation clearance, not uncertainty in the measured skin.
    M8FlowerInterval error=M8FlowerI(errors.x,errors.x);
    depth=M8FlowerIAdd(depth,M8FlowerI(-error.hi,error.hi));
    float3 measured=gsDepthNormalTex.Load(int3(pixel,0)).xyz;
    M8FlowerInterval nError=M8FlowerI(-_M8PlaneErrorBounds.x,_M8PlaneErrorBounds.x);
    normal=M8FlowerSkinPoint(measured);
    normal.x=M8FlowerIAdd(normal.x,nError);
    normal.y=M8FlowerIAdd(normal.y,nError);
    normal.z=M8FlowerIAdd(normal.z,nError);
    return true;
}

// Codegen already assigned this work item to its one thread parent. Runtime
// loads its inverse chart; it never enumerates other parents' chambers.
bool M8FlowerSkinMetricChamber(uint activeWedgeMask,
    uint workIndex,out M8FlowerInterval3 footprintTriangle[3],out M8FlowerInterval3 chart[3],
    out uint3 rules,out uint child)
{
    rules=0u.xxx;child=0u;
    uint4 work=M8FlowerSkinFootprintWorkAt(workIndex);
    uint wedge=work.x&7u;
    if((activeWedgeMask&(1u<<wedge))==0u)return false;
    child=(work.x>>3u)&7u;
    rules=uint3(work.y&63u,(work.y>>6u)&63u,(work.y>>12u)&63u);
    M8FlowerSkinWorkFootprint(work,footprintTriangle);
    [loop]for(uint vertex=0u;vertex<3u;vertex++)
    {
        float4 xy=M8FlowerSkinFootprintChartAt(work.z*6u+vertex*2u);
        float2 z=M8FlowerSkinFootprintChartAt(work.z*6u+vertex*2u+1u).xy;
        chart[vertex].x=M8FlowerI(xy.x,xy.y);
        chart[vertex].y=M8FlowerI(xy.z,xy.w);
        chart[vertex].z=M8FlowerI(z.x,z.y);
    }
    return child<7u;
}

bool M8FlowerSkinMetricTerms(M8FlowerInterval3 local,M8FlowerInterval3 chart[3],
    uint3 rules,uint depth,M8FlowerInterval amplitude3,M8FlowerInterval amplitude4,
    out M8FlowerInterval inherited,out M8FlowerInterval bubble)
{
    inherited=M8FlowerI(0,0);bubble=M8FlowerSkinBubbleInterval(local);
    M8FlowerInterval3 parent=M8FlowerSkinTriangleAt(chart,local);
    [loop]for(uint level=0u;level+1u<depth;level++)
    {
        parent=M8FlowerSkinIntervalChamber(parent,rules[level]);
        if(!M8FlowerSkinClipMetricCoordinates(parent))return false;
        M8FlowerInterval amplitude;
        if(level==0u)amplitude=amplitude3;else amplitude=amplitude4;
        inherited=M8FlowerIAdd(inherited,M8FlowerIMul(amplitude,M8FlowerSkinBubbleInterval(parent)));
    }
    return all(M8FlowerIsFinite(float4(inherited.lo,inherited.hi,bubble.lo,bubble.hi)));
}

// Intersect an amplitude constraint only where the measured interval has a
// CERTAIN interior locality. Boundary psi=0 is an identity constraint, not a
// division by zero and not a made-up zero measurement. Complete coverage is
// checked separately after all constraints and fixed-point encoding.
bool M8FlowerSkinConstrainAmplitude(M8FlowerInterval residual,M8FlowerInterval bubble,
    inout M8FlowerInterval amplitude,inout bool constrained)
{
    if(bubble.hi==0.0)return residual.lo<=0.0 && residual.hi>=0.0;
    if(!(bubble.lo>0.0))return true;
    M8FlowerInterval candidate;
    if(!M8FlowerIDivPositive(residual,bubble,candidate))return false;
    if(!constrained){amplitude=candidate;constrained=true;}
    else amplitude=M8FlowerI(max(amplitude.lo,candidate.lo),min(amplitude.hi,candidate.hi));
    return amplitude.lo<=amplitude.hi;
}

bool M8FlowerSkinMetricPixelCover(M8FlowerSkinMetricFrame frame,
    M8FlowerInterval3 footprintTriangle[3],M8FlowerInterval displacement,
    out int2 first,out int2 last,out M8FlowerInterval depth)
{
    first=0;last=-1;depth=M8FlowerI(0,0);
    M8FlowerInterval3 support=footprintTriangle[0];
    M8FlowerSkinEnclose(support,footprintTriangle[1]);M8FlowerSkinEnclose(support,footprintTriangle[2]);
    support=M8FlowerSkinAdd(support,M8FlowerSkinScale(frame.Normal,displacement));
    if(!M8SupportInsideMutationScope(support))return false;
    float unusedDepth;
    if(!M8DepthProjectSupport(float3(support.x.lo,support.y.lo,support.z.lo),
        float3(support.x.hi,support.y.hi,support.z.hi),gsDepthView[0],gsDepthProj[0],
        int2(gsDepthTexSize),_M8DepthErrorBounds[0].y,first,last,unusedDepth))return false;
    M8FlowerInterval eyeZ=M8FlowerIAdd(M8DepthIntervalRow(gsDepthView[0][2],support),
        M8FlowerI(-_M8DepthErrorBounds[0].y,_M8DepthErrorBounds[0].y));
    depth=M8FlowerI(-eyeZ.hi,-eyeZ.lo);
    return depth.lo>0.0 && all(M8FlowerIsFinite(float2(depth.lo,depth.hi)));
}

// A normal column intersects each certified measured plane once only when
// n_measurement dot N_carrier excludes zero. Grazing/multisheet evidence
// cannot be converted into a skin coefficient by dividing an uncertain sign.
bool M8FlowerSkinMetricHeight(M8FlowerInterval3 position,M8FlowerInterval3 normal,
    M8FlowerSkinMetricFrame frame,M8FlowerInterval3 base,
    out M8FlowerInterval height)
{
    M8FlowerInterval denominator=M8FlowerObservedDot(normal,frame.Normal);
    M8FlowerInterval numerator=M8FlowerObservedDot(normal,M8FlowerSkinSubtract(position,base));
    numerator=M8FlowerIAdd(numerator,
        M8FlowerI(-_M8PlaneErrorBounds.y,_M8PlaneErrorBounds.y));
    return M8FlowerSkinDivide(numerator,denominator,height);
}

// Exact inverse of the fixed one-coefficient child bubble. The first pass
// intersects constraints from actual bounded interior measurements. The
// second pass covers the COMPLETE displaced footprint, after Q5.26 outward
// encoding, and requires the composed parent+child signal to stay inside
// the measured graph enclosure in every possibly intersecting pixel cell.
// There is no least-squares estimate, central-value amplitude, or epsilon.
bool M8FlowerMeasureMetricSkinLane(uint parentOrdinal,uint activeWedgeMask,
    uint wantedChild,int3 owner,uint ownerFlags,float normalError,float offsetError,
    uint reductionPass,uint pixelLane,M8FlowerInterval amplitude3,M8FlowerInterval amplitude4,
    inout M8FlowerInterval amplitude,out bool constrained,out bool supported)
{
    constrained=false;supported=false;
    uint depth,c3,c4;
    if(!M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4))return false;
    M8FlowerInterval inheritedRange=M8FlowerIAdd(
        M8FlowerIMul(amplitude3,M8FlowerI(0,1)),M8FlowerIMul(amplitude4,M8FlowerI(0,1)));
    uint2 childWork=M8FlowerSkinChildWorkAt(7u*parentOrdinal+wantedChild);
    [loop]for(uint ordinal=0u;ordinal<childWork.y;ordinal++)
    {
        M8FlowerInterval3 footprintTriangle[3],chart[3];uint3 rules;uint child;
        if(!M8FlowerSkinMetricChamber(activeWedgeMask,childWork.x+ordinal,
            footprintTriangle,chart,rules,child))continue;
        supported=true;
        M8FlowerSkinMetricFrame frame;
        if(!M8FlowerSkinMetricFrameFromTriangle(footprintTriangle,frame))return false;
        M8FlowerInterval range=inheritedRange;
        if(reductionPass!=0u)range=M8FlowerIAdd(range,M8FlowerIMul(amplitude,M8FlowerI(0,1)));
        int2 first,last;M8FlowerInterval modelDepth;
        if(!M8FlowerSkinMetricPixelCover(frame,footprintTriangle,range,first,last,modelDepth))
            return false;
        uint width=(uint)(last.x-first.x+1),count=width*(uint)(last.y-first.y+1);
        [loop]for(uint pixel=pixelLane;pixel<count;pixel+=8u)
        {
            int x=first.x+(int)(pixel%width),y=first.y+(int)(pixel/width);
            M8FlowerInterval measuredDepth;
            M8FlowerInterval3 measuredNormal;
            if(!M8FlowerSkinMetricMeasurement(int2(x,y),owner,ownerFlags,normalError,offsetError,
                measuredDepth,measuredNormal))return false;
            // Decode the measured centre first; the validation pass
            // then decodes the entire model-depth pixel cell. Both use
            // the same exact inverse projection, not cloned dividers.
            M8FlowerInterval3 positions[2];
            [loop]for(uint projection=0u;projection<=reductionPass;projection++)
            {
                M8FlowerInterval depth;
                if(projection==0u)depth=measuredDepth;else depth=modelDepth;
                if(!M8FlowerSkinDepthCell(int2(x,y),depth,projection!=0u,positions[projection]))
                    return false;
            }
            M8FlowerInterval3 measuredPosition=positions[0];
            M8FlowerInterval3 localitySource=measuredPosition;
            if(reductionPass!=0u)localitySource=positions[1];
            M8FlowerInterval3 local;
            if(!M8FlowerSkinMetricCoordinates(frame,localitySource,local))return false;
            if(reductionPass==0u)
            {
                // A possibly outside measurement is not silently
                // assigned to a child. It contributes no coefficient
                // constraint; the second full-cover pass still checks
                // every actual cell of that child's signal.
                if(any(float3(local.x.lo,local.y.lo,local.z.lo)<0.0) ||
                    any(float3(local.x.hi,local.y.hi,local.z.hi)>1.0))continue;
            }
            else if(!M8FlowerSkinClipMetricCoordinates(local))continue;
            M8FlowerInterval3 base=M8FlowerSkinTriangleAt(footprintTriangle,local);
            M8FlowerInterval height,inherited,bubble;
            if(!M8FlowerSkinMetricHeight(measuredPosition,measuredNormal,frame,base,height) ||
                !M8FlowerSkinMetricTerms(local,chart,rules,depth,amplitude3,amplitude4,inherited,bubble))
                return false;
            if(reductionPass==0u)
            {
                if(!M8FlowerSkinConstrainAmplitude(M8FlowerISub(height,inherited),bubble,
                    amplitude,constrained))return false;
            }
            else
            {
                M8FlowerInterval composed=M8FlowerIAdd(inherited,M8FlowerIMul(amplitude,bubble));
                // Requiring the complete composed interval to fit the
                // measured enclosure is stronger than an overlap test
                // at a centroid. Incompatible boundary values reject V;
                // they never move the L2 carrier or replace parent V.
                if(composed.lo<height.lo || composed.hi>height.hi)return false;
            }
        }
    }
    return true;
}

// One complete seven-child predicate, seven independent eight-lane pixel
// reductions. Intersection is max(lower)/min(upper), so regrouping does not
// change an endpoint. Encode once AFTER that intersection, then all lanes
// validate the same complete encoded amplitude. No subgroup-size assumption.
bool M8FlowerMeasureMetricSkinChildren(uint parentOrdinal,uint activeWedgeMask,
    int3 owner,uint ownerFlags,float normalError,float offsetError,
    M8FlowerSkinMetricRun run,bool existing,uint lane,
    out M8FlowerVInterval value,out bool supported)
{
    uint child=lane>>3u,pixelLane=lane&7u;
    value=(M8FlowerVInterval)0;supported=false;
    uint depth=0u,c3=0u,c4=0u;
    bool valid=child<7u && activeWedgeMask!=0u && activeWedgeMask<64u &&
        M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4);
    M8FlowerInterval amplitude=M8FlowerI(0,0);
    M8FlowerInterval amplitude3=M8FlowerI(0,0),amplitude4=M8FlowerI(0,0);
    if(valid && depth>1u)valid=M8FlowerSkinReadAmplitude(run,existing,3u,c3,c4,amplitude3);
    if(valid && depth>2u)valid=M8FlowerSkinReadAmplitude(run,existing,4u,c3,c4,amplitude4);
    [loop]for(uint pass=0u;pass<2u;pass++)
    {
        bool constrained=false,laneSupport=false;
        if(valid)valid=M8FlowerMeasureMetricSkinLane(parentOrdinal,activeWedgeMask,child,
            owner,ownerFlags,normalError,offsetError,pass,pixelLane,
            amplitude3,amplitude4,amplitude,constrained,laneSupport);
        m8SkinReduction[lane]=uint4(asuint(amplitude.lo),asuint(amplitude.hi),0u,
            (valid?1u:0u)|(laneSupport?2u:0u)|(constrained?4u:0u));
        GroupMemoryBarrierWithGroupSync();
        if(pixelLane==0u)
        {
            bool allValid=true,anySupport=false,anyConstraint=false;
            M8FlowerInterval combined=M8FlowerI(0,0);
            [loop]for(uint peer=0u;peer<8u;peer++)
            {
                uint4 part=m8SkinReduction[lane+peer];
                allValid=allValid && (part.w&1u)!=0u;
                anySupport=anySupport || (part.w&2u)!=0u;
                if((part.w&4u)!=0u)
                {
                    M8FlowerInterval bound=M8FlowerI(asfloat(part.x),asfloat(part.y));
                    if(!anyConstraint)combined=bound;
                    else combined=M8FlowerI(max(combined.lo,bound.lo),min(combined.hi,bound.hi));
                    anyConstraint=true;
                }
            }
            if(pass==0u && anySupport && allValid)
                allValid=anyConstraint && combined.lo<=combined.hi &&
                    M8FlowerSkinEncodeAmplitude(combined,value);
            m8SkinReduction[lane]=uint4(asuint(value.Lower),asuint(value.Upper),0u,
                (allValid?1u:0u)|(anySupport?2u:0u));
        }
        GroupMemoryBarrierWithGroupSync();
        uint4 result=m8SkinReduction[child*8u];
        value.Lower=asint(result.x);value.Upper=asint(result.y);
        valid=(result.w&1u)!=0u;supported=(result.w&2u)!=0u;
        amplitude=M8FlowerSkinDecodeAmplitude(value);
        // All readers consume the group result before the next phase reuses
        // its first lane for an independent partial interval.
        GroupMemoryBarrierWithGroupSync();
    }
    return valid;
}

bool M8FlowerMeasureRgbSkinChildren(uint parentOrdinal,uint activeWedgeMask,
    int3 owner,uint ownerFlags,float normalError,float offsetError,
    M8ThreadColorInterval inherited,uint lane,out M8ThreadColorInterval value,out bool supported)
{
    uint child=lane>>3u,pixelLane=lane&7u;
    value=inherited;supported=false;
    M8FlowerInterval3 footprint;
    bool valid=child<7u &&
        M8FlowerSkinChildFootprint(parentOrdinal,activeWedgeMask,child,footprint,supported);
    if(valid && supported)valid=M8FlowerSkinDirectFootprint(footprint,owner,ownerFlags,
        normalError,offsetError,pixelLane);
    float3 lower=0.0.xxx,upper=0.0.xxx;
    [loop]for(uint eye=0u;eye<2u;eye++)
    {
        M8FlowerInterval3 part=(M8FlowerInterval3)0;
        if(valid && supported)valid=M8FlowerSkinRgbFootprint(eye,footprint,pixelLane,part);
        m8SkinReduction[2u*lane]=uint4(asuint(float3(part.x.lo,part.y.lo,part.z.lo)),valid?1u:0u);
        m8SkinReduction[2u*lane+1u]=uint4(asuint(float3(part.x.hi,part.y.hi,part.z.hi)),0u);
        GroupMemoryBarrierWithGroupSync();
        if(pixelLane==0u)
        {
            float3 lo=asfloat(0x7f800000u).xxx,hi=-asfloat(0x7f800000u).xxx;
            bool allValid=true;
            [loop]for(uint peer=0u;peer<8u;peer++)
            {
                uint4 low=m8SkinReduction[2u*(lane+peer)];
                lo=min(lo,asfloat(low.xyz));
                hi=max(hi,asfloat(m8SkinReduction[2u*(lane+peer)+1u].xyz));
                allValid=allValid && low.w!=0u;
            }
            if(supported && allValid)
            {
                float3 error=_M8RgbErrorBounds[eye].xyz;
                allValid=all(M8FlowerIsFinite(error)) && !any(error<0.0);
                float3 l=lo-error,h=hi+error;
                lo=max(0.0.xxx,float3(M8FlowerPrevious(l.x),M8FlowerPrevious(l.y),M8FlowerPrevious(l.z)));
                hi=float3(M8FlowerNext(h.x),M8FlowerNext(h.y),M8FlowerNext(h.z));
            }
            m8SkinReduction[2u*lane]=uint4(asuint(lo),allValid?1u:0u);
            m8SkinReduction[2u*lane+1u]=uint4(asuint(hi),0u);
        }
        GroupMemoryBarrierWithGroupSync();
        uint4 low=m8SkinReduction[16u*child];
        float3 high=asfloat(m8SkinReduction[16u*child+1u].xyz);
        valid=low.w!=0u;
        // Form each complete eye hull BEFORE intersecting the two eyes.
        // Intersecting partial stripes would be a different predicate.
        if(eye==0u){lower=asfloat(low.xyz);upper=high;}
        else {lower=max(lower,asfloat(low.xyz));upper=min(upper,high);}
        GroupMemoryBarrierWithGroupSync();
    }
    if(!valid)return false;
    if(!supported)return true; // Logical-empty children inherit, never measure.
    return !any(lower>upper) &&
        M8ThreadEncodeColorInterval(float4(lower,1.0),float4(upper,1.0),value);
}

// Measurement/encoding above guarantees ordered finite intervals for every
// certain supported child. For such intervals a disjoint pair exists iff
// max(lower) > min(upper); extrema from the same child cannot satisfy it.
// The generated pairwise classifier remains the independent oracle.
uint M8FlowerClassifyMeasuredSkin(uint4 children[7],uint certain,uint supportMask,bool metric)
{
    if((supportMask&~127u)!=0u || (certain&supportMask)!=supportMask)
        return M8_FLOWER_SKIN_AMBIGUOUS;
    float3 lower=-asfloat(0x7f800000u).xxx,upper=asfloat(0x7f800000u).xxx;
    [loop]for(uint child=0u;child<7u;child++)
    {
        if((supportMask&(1u<<child))==0u)continue;
        uint4 words=children[child];
        float3 lo,hi;
        if(metric)
        {
            M8FlowerVInterval amplitude;
            amplitude.Lower=asint(words.x);amplitude.Upper=asint(words.y);
            M8FlowerInterval value=M8FlowerSkinDecodeAmplitude(amplitude);
            lo=value.lo.xxx;hi=value.hi.xxx;
        }
        else
        {
            lo=float3(f16tof32(words.x&65535u),f16tof32(words.x>>16u),f16tof32(words.y&65535u));
            hi=float3(f16tof32(words.z&65535u),f16tof32(words.z>>16u),f16tof32(words.w&65535u));
        }
        lower=max(lower,lo);upper=min(upper,hi);
    }
    return any(lower>upper)?M8_FLOWER_SKIN_SPLIT:M8_FLOWER_SKIN_UNIFORM;
}

// The unused member of a seven-value group inherits the existing signal of
// this scan parent. This is not a new observation and does not take part in
// split classification. The shared compact-address function owns the exact
// thread-order mapping, identical to the procedural signal consumer.
bool M8FlowerSkinInheritedColor(uint parentOrdinal,uint rootPackedColor,
    M8ThreadRun run,bool existing,out M8ThreadColorInterval inherited)
{
    inherited=(M8ThreadColorInterval)0;
    uint depth,c3,c4;
    if(!M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4))return false;
    if(parentOrdinal==0u)
    {
        float3 rgb=float3(rootPackedColor&255u,(rootPackedColor>>8u)&255u,
            (rootPackedColor>>16u)&255u)*(1.0/255.0);
        return M8ThreadEncodeColorInterval(float4(rgb,1.0),float4(rgb,1.0),inherited);
    }
    if(!existing || !M8FlowerCanonicalSplit(uint2(run.SplitBitsLo,run.SplitBitsHi)))return false;
    uint group,rank;
    if(!M8FlowerSkinCompactChildAddress(run.GroupBase,run.SplitBitsLo,run.SplitBitsHi,
        depth+1u,c3,c4,0u,group,rank) || rank>=7u || group<run.GroupBase ||
        group>0xffffffffu/112u || !M8FlowerThreadRange(group*112u,112u))return false;
    uint4 value=_M8ThreadAtlasPages.Load4(group*112u+16u*rank);
    inherited.LowerLinearRgba=value.xy;inherited.UpperLinearRgba=value.zw;
    float4 lo=float4(f16tof32(value.x&65535u),f16tof32(value.x>>16u),
        f16tof32(value.y&65535u),f16tof32(value.y>>16u));
    float4 hi=float4(f16tof32(value.z&65535u),f16tof32(value.z>>16u),
        f16tof32(value.w&65535u),f16tof32(value.w>>16u));
    return all(M8FlowerIsFinite(lo)) && all(M8FlowerIsFinite(hi)) && all(lo<=hi);
}

bool M8FlowerSkinCarrierIdentity(int3 owner,uint flowerKey,uint activeWedgeMask,
    M8FlowerPhaseRootEvidence roots[7])
{
    uint petal=(flowerKey>>M8_FLOWER_L2_KEY_PETAL_SHIFT)&63u;
    if(activeWedgeMask==0u || activeWedgeMask>=64u || !M8FlowerL2KeyValid(flowerKey) ||
        ((flowerKey>>M8_FLOWER_L2_KEY_ROOT_SHIFT)&1u)!=((roots[0].Tag>>7u)&1u) ||
        ((flowerKey>>M8_FLOWER_L2_KEY_SECTOR_SHIFT)&31u)!=((roots[0].Tag>>8u)&31u))return false;
    uint hub=M8FlowerL2WedgeIndex(petal,flowerKey&15u)/6u;
    uint siteMask=1u;
    [unroll]for(uint wedge=0u;wedge<6u;wedge++)
        if((activeWedgeMask&(1u<<wedge))!=0u)
        {
            uint3 indices=M8FlowerSkinWedgeSites(wedge);
            siteMask|=(1u<<indices.y)|(1u<<indices.z);
        }
    [loop]for(uint site=0u;site<7u;site++)
    {
        if((siteMask&(1u<<site))==0u)continue;
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

// One parent/signal snapshot feeds all seven independent child lanes. The
// group writer uses this same snapshot after their collective barrier.
struct M8FlowerSkinGroupContext
{
    uint OwnerRef;
    uint Flags;
    uint Epoch;
    uint GroupBase;
    uint2 SplitBits;
    uint Reserved;
    bool Existing;
    bool Measure;
    M8ThreadColorInterval Inherited;
};

uint M8FlowerPrepareSkinGroup(uint slot,uint local,uint slotGeneration,uint flowerKey,
    uint parentOrdinal,uint activeWedgeMask,uint sourceFlags,bool metric,
    out M8FlowerSkinGroupContext context,out uint classification)
{
    context=(M8FlowerSkinGroupContext)0;classification=M8_FLOWER_SKIN_AMBIGUOUS;
    if(slot>=32768u || local>=512u || parentOrdinal>=57u)return M8_FLOWER_ARENA_INVALID;
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)],meta=_M8TileRecords[M8TileMetaIndex(slot)];
    if(runtime.w!=slotGeneration || meta.x>=MERKABA_M8_CHUNK_CAPACITY || meta.y>=64u ||
        _M8ChunkTileRefs[meta.x*64u+meta.y]!=slot+1u)return M8_FLOWER_SIDECAR_STALE_SLOT;
    KernelState state=M8LoadKernelStateRead(slot,local);
    if((state.flags&(M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID|M8_FLOWER_SEED_FLAG))!=
        (M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID))return M8_FLOWER_ARENA_INVALID;
    // The resolver validated every symbolic site before publishing its immutable
    // observation item. Signal stages check that source, not uninitialized roots
    // from a different workgroup or a second geometry solve.
    if(state.flags!=sourceFlags || !M8FlowerL2KeyValid(flowerKey) ||
        activeWedgeMask==0u || activeWedgeMask>=64u)
        return M8_FLOWER_ARENA_INVALID;
    context.Flags=state.flags;
    context.OwnerRef=M8FlowerFindOwner(slot,local,slotGeneration);
    context.Epoch=M8FlowerGetOwnerEpoch(context.OwnerRef);
    uint runRef;
    M8ThreadRun rgb;
    if(metric)
    {
        M8FlowerSkinMetricRun run;
        context.Existing=M8FlowerFindMetricRun(context.OwnerRef,flowerKey,context.Epoch,run,runRef);
        context.GroupBase=run.GroupBase;context.SplitBits=uint2(run.SplitBitsLo,run.SplitBitsHi);
        context.Reserved=run.Reserved;
    }
    else
    {
        context.Existing=M8FlowerFindThreadRun(context.OwnerRef,flowerKey,context.Epoch,rgb,runRef);
        context.GroupBase=rgb.GroupBase;context.SplitBits=uint2(rgb.SplitBitsLo,rgb.SplitBitsHi);
    }
    if(parentOrdinal!=0u)
    {
        uint predecessor=parentOrdinal<8u?0u:1u+(parentOrdinal-8u)/7u;
        if(!context.Existing || (context.SplitBits[predecessor>>5u]&(1u<<(predecessor&31u)))==0u)
        {classification=M8_FLOWER_SKIN_UNIFORM;return M8_FLOWER_ARENA_OK;}
    }
    if(!metric && !M8FlowerSkinInheritedColor(parentOrdinal,state.packedColor,rgb,
        context.Existing,context.Inherited))return M8_FLOWER_ARENA_INVALID;
    context.Measure=true;
    return M8_FLOWER_ARENA_OK;
}

// The child values below are the complete parallel reduction of this exact
// prepared group. Only the owner lane publishes after all children retire.
uint M8FlowerCommitMeasuredSkinSplit(uint slot,uint local,uint slotGeneration,uint flowerKey,
    uint parentOrdinal,M8FlowerSkinGroupContext context,uint4 canonical[7],
    uint classification,uint supportMask,bool metric,out bool changed)
{
    changed=false;
    if(!context.Measure || supportMask==0u)return M8_FLOWER_ARENA_OK;
    uint ownerRef=context.OwnerRef;
    uint2 bits=context.SplitBits;
    bool replacing=context.Existing && (bits[parentOrdinal>>5u]&(1u<<(parentOrdinal&31u)))!=0u;
    if(classification==M8_FLOWER_SKIN_AMBIGUOUS ||
        (classification==M8_FLOWER_SKIN_UNIFORM && !replacing))return M8_FLOWER_ARENA_OK;
    uint depth,c3,c4;
    M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4);
    uint4 thread[7];
    [loop]for(uint child=0u;child<7u;child++)
    {
        uint rank=depth==1u?M8FlowerSkinL3ChildRankAt(child):depth==2u?
            M8FlowerSkinL4ChildRankAt(7u*c3+child):M8FlowerSkinL5ChildRankAt(49u*c3+7u*c4+child);
        thread[rank]=canonical[child];
    }
    if(replacing)
    {
        uint stride=metric?56u:112u;
        uint group=context.GroupBase+M8FlowerSplitRank(bits,parentOrdinal);
        if(group<context.GroupBase || group>0xffffffffu/stride ||
            (metric?!M8FlowerDetailRange(group*stride,stride):!M8FlowerThreadRange(group*stride,stride)))
            return M8_FLOWER_ARENA_INVALID;
        bool same=true;
        [loop]for(uint child=0u;child<7u;child++)
        {
            if(metric)
                same=same && all(M8_FLOWER_DETAIL_SOURCE.Load2(group*stride+8u*child)==thread[child].xy);
            else
                same=same && all(_M8ThreadAtlasPages.Load4(group*stride+16u*child)==thread[child]);
        }
        if(same)return M8_FLOWER_ARENA_OK;
    }
    // Validate the same seven records accepted by the typed storage writers.
    // RGB and V share address mechanics, never evidence or backing storage.
    [loop]for(uint child=0u;child<7u;child++)
    {
        uint4 value=thread[child];
        if(metric)
        {
            if(asint(value.x)>asint(value.y))return M8_FLOWER_ARENA_INVALID;
        }
        else
        {
            float4 lo=float4(f16tof32(value.x&65535u),f16tof32(value.x>>16u),
                f16tof32(value.y&65535u),f16tof32(value.y>>16u));
            float4 hi=float4(f16tof32(value.z&65535u),f16tof32(value.z>>16u),
                f16tof32(value.w&65535u),f16tof32(value.w>>16u));
            if(!all(M8FlowerIsFinite(lo)) || !all(M8FlowerIsFinite(hi)) || any(lo>hi))
                return M8_FLOWER_ARENA_INVALID;
        }
    }
    uint status=M8_FLOWER_ARENA_OK;
    if(ownerRef==0u)
    {
        status=M8FlowerEnsureOwner(slot,local,slotGeneration,context.Flags,_M8WorldPublishingGeneration,ownerRef);
        if(status!=M8_FLOWER_ARENA_OK)return status;
        M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
    if(parentOrdinal>=57u || !M8FlowerL2KeyValid(flowerKey) ||
        M8FlowerGetOwnerEpoch(ownerRef)==0u ||
        !M8FlowerOwnerWritable(ownerRef,_M8WorldPublishingGeneration,_M8WorldRetiredGeneration))
        return M8_FLOWER_ARENA_INVALID;
    if(metric)
        status=M8FlowerCommitSkinGroup(ownerRef,flowerKey,parentOrdinal,thread,false,
            _M8WorldPublishingGeneration,_M8FlowerDetailPages,M8FlowerDetailArena());
    else
        status=M8FlowerCommitSkinGroup(ownerRef,flowerKey,parentOrdinal,thread,true,
            _M8WorldPublishingGeneration,_M8ThreadAtlasPages,M8FlowerThreadArena());
    if(status==M8_FLOWER_ARENA_OK)
    {
        changed=true;M8MarkTileDirty(slot);
        InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK],4u);
    }
    return status;
}

// Does this exact parent footprint intersect a geometrically unresolved
// wedge? Unlike an inactive wedge, it is NOT EMPTY. Existing independent
// sibling regions can still consume their complete certain support.
bool M8FlowerSkinParentTouchesWedges(uint parentOrdinal,uint wedgeMask)
{
    if(wedgeMask==0u)return false;
    if(parentOrdinal>=57u)return true;
    return (M8FlowerSkinParentWorkAt(parentOrdinal).w&wedgeMask)!=0u;
}

#endif
