#ifndef GENESIS_MERKABA_FLOWER_SKIN_OBSERVATION_INCLUDED
#define GENESIS_MERKABA_FLOWER_SKIN_OBSERVATION_INCLUDED

#include "MerkabaFlowerGeometry.hlsl"

// These are the SAME calibrated bounds retained by StereoFlowerRefine for
// this immutable observation. No presentation light or colour score enters
// captured-radiance admission.
float4 _M8RgbErrorBounds[2];

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
        c3=M8FlowerSkinThreadToCanonicalAt(57u*(parentOrdinal-1u));
        return c3<7u;
    }
    depth=3u;
    uint j4=parentOrdinal-8u;
    uint canonical=M8FlowerSkinThreadToCanonicalAt(57u*(j4/7u)+1u+8u*(j4%7u));
    if(canonical<7u || canonical>=56u)return false;
    c3=(canonical-7u)/7u;c4=(canonical-7u)%7u;
    return true;
}

// Enumerate the actual generated chamber union, at exactly the requested
// scan parent. A box encloses each COMPLETE union; extra covered image
// texels can widen RGB evidence but never manufacture a distinction.
// Empty generated children stay absent from covered; they are not painted.
bool M8FlowerSkinChildFootprints(M8FlowerPhaseRootEvidence roots[7],uint parentOrdinal,uint activeWedgeMask,
    out M8FlowerInterval3 footprints[7],out uint covered)
{
    covered=0u;
    uint depth,c3,c4;
    if(activeWedgeMask==0u || activeWedgeMask>=64u ||
        !M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4))return false;
    uint siteMask=1u;
    [unroll]for(uint wedge=0u;wedge<6u;wedge++)
        if((activeWedgeMask&(1u<<wedge))!=0u)
        {
            uint3 indices=M8FlowerSkinWedgeSites(wedge);
            siteMask|=(1u<<indices.y)|(1u<<indices.z);
        }
    M8FlowerInterval3 sites[7];
    [loop]for(uint site=0u;site<7u;site++)
    {
        footprints[site]=M8FlowerSkinPoint(0.0.xxx);
        sites[site]=M8FlowerSkinPoint(0.0.xxx);
        if((siteMask&(1u<<site))!=0u && !M8FlowerSkinRootWorld(roots[site],sites[site]))return false;
    }
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        if((activeWedgeMask&(1u<<wedge))==0u)continue;
        uint3 indices=M8FlowerSkinWedgeSites(wedge);
        M8FlowerInterval3 p0[3];
        p0[0]=sites[indices.x];p0[1]=sites[indices.y];p0[2]=sites[indices.z];
        [loop]for(uint order3=0u;order3<6u;order3++)
        {
            int4 rule3=M8FlowerSkinChamberAt(6u*wedge+order3);
            uint meta3=asuint(rule3.w),child3=meta3&255u;
            if(depth>1u && child3!=c3)continue;
            M8FlowerInterval3 p3[3];M8FlowerSkinChamberFootprint(p0,rule3.xyz,p3);
            if(depth==1u){M8FlowerSkinAddChildFootprint(child3,p3,footprints,covered);continue;}
            uint wedge3=(meta3>>8u)&255u;
            [loop]for(uint order4=0u;order4<6u;order4++)
            {
                int4 rule4=M8FlowerSkinChamberAt(6u*wedge3+order4);
                uint meta4=asuint(rule4.w),child4=meta4&255u;
                if(depth>2u && child4!=c4)continue;
                M8FlowerInterval3 p4[3];M8FlowerSkinChamberFootprint(p3,rule4.xyz,p4);
                if(depth==2u){M8FlowerSkinAddChildFootprint(child4,p4,footprints,covered);continue;}
                uint wedge4=(meta4>>8u)&255u;
                [loop]for(uint order5=0u;order5<6u;order5++)
                {
                    int4 rule5=M8FlowerSkinChamberAt(6u*wedge4+order5);
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
        if(!all(M8FlowerIsFinite(captured)) || any(captured<0.0) || any(captured>65504.0))return false;
        lower=min(lower,captured);upper=max(upper,captured);
    }
    float3 error=_M8RgbErrorBounds[eye].xyz;
    if(!all(M8FlowerIsFinite(error)) || any(error<0.0))return false;
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

bool M8FlowerSkinMetricFrameFromTriangle(M8FlowerInterval3 footprintTriangle[3],
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
    if(!(frame.Determinant.lo>0.0))return false;
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

// Apply the SAME generated chamber matrix to bounded coordinates. Taking
// its columns on the three exact unit vectors avoids a second transition
// formula in the sensor consumer.
M8FlowerInterval3 M8FlowerSkinIntervalChamber(M8FlowerInterval3 value,int3 order)
{
    float3 x=M8FlowerSkinApplyChamber(float3(1,0,0),order);
    float3 y=M8FlowerSkinApplyChamber(float3(0,1,0),order);
    float3 z=M8FlowerSkinApplyChamber(float3(0,0,1),order);
    M8FlowerInterval components[3];
    [loop]for(uint axis=0u;axis<3u;axis++)
        components[axis]=M8DepthIntervalRow(float4(x[axis],y[axis],z[axis],0),value);
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
    M8FlowerInterval error=M8FlowerIAdd(M8FlowerI(errors.x,errors.x),M8FlowerI(errors.z,errors.z));
    depth=M8FlowerIAdd(depth,M8FlowerI(-error.hi,error.hi));
    float3 measured=gsDepthNormalTex.Load(int3(pixel,0)).xyz;
    M8FlowerInterval nError=M8FlowerI(-_M8PlaneErrorBounds.x,_M8PlaneErrorBounds.x);
    normal=M8FlowerSkinPoint(measured);
    normal.x=M8FlowerIAdd(normal.x,nError);
    normal.y=M8FlowerIAdd(normal.y,nError);
    normal.z=M8FlowerIAdd(normal.z,nError);
    return true;
}

// The ordinal enumerates a fixed generated chamber union, not a spatial
// tree or an adaptive fitting search. Nonmatching parent addresses are
// geometrically EMPTY, independently of whether any sensor texel exists.
bool M8FlowerSkinMetricChamber(M8FlowerInterval3 sites[7],uint depth,uint c3,uint c4,uint activeWedgeMask,
    uint ordinal,out M8FlowerInterval3 footprintTriangle[3],out M8FlowerInterval3 chart[3],
    out uint3 rules,out uint child)
{
    rules=0u.xxx;child=0u;
    uint chambersPerWedge=depth==1u?6u:depth==2u?36u:216u;
    uint wedge=ordinal/chambersPerWedge;
    if(wedge>=6u || (activeWedgeMask&(1u<<wedge))==0u)return false;
    uint remainder=ordinal%chambersPerWedge;
    uint3 indices=M8FlowerSkinWedgeSites(wedge);
    footprintTriangle[0]=sites[indices.x];footprintTriangle[1]=sites[indices.y];footprintTriangle[2]=sites[indices.z];
    chart[0]=M8FlowerSkinPoint(float3(1,0,0));
    chart[1]=M8FlowerSkinPoint(float3(0,1,0));
    chart[2]=M8FlowerSkinPoint(float3(0,0,1));
    [loop]for(uint level=0u;level<depth;level++)
    {
        chambersPerWedge/=6u;
        uint order=remainder/chambersPerWedge;
        remainder%=chambersPerWedge;
        rules[level]=6u*wedge+order;
        int4 rule=M8FlowerSkinChamberAt(rules[level]);
        child=asuint(rule.w)&255u;
        if((level==0u && depth>1u && child!=c3) ||
            (level==1u && depth>2u && child!=c4))return false;
        M8FlowerInterval3 nextTriangle[3],nextChart[3];
        M8FlowerSkinChamberFootprint(footprintTriangle,rule.xyz,nextTriangle);
        M8FlowerSkinChamberFootprint(chart,rule.xyz,nextChart);
        [unroll]for(uint i=0u;i<3u;i++){footprintTriangle[i]=nextTriangle[i];chart[i]=nextChart[i];}
        wedge=(asuint(rule.w)>>8u)&255u;
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
        parent=M8FlowerSkinIntervalChamber(parent,M8FlowerSkinChamberAt(rules[level]).xyz);
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
uint M8FlowerMeasureMetricSkinChildren(M8FlowerPhaseRootEvidence roots[7],uint parentOrdinal,uint activeWedgeMask,
    int3 owner,uint ownerFlags,float normalError,float offsetError,
    M8FlowerSkinMetricRun run,bool existing,out M8FlowerVInterval children[7],
    out uint supportMask)
{
    supportMask=0u;
    uint depth,c3,c4;
    if(activeWedgeMask==0u || activeWedgeMask>=64u)return M8_FLOWER_SKIN_AMBIGUOUS;
    uint siteMask=1u;
    [unroll]for(uint wedge=0u;wedge<6u;wedge++)
        if((activeWedgeMask&(1u<<wedge))!=0u)
        {
            uint3 indices=M8FlowerSkinWedgeSites(wedge);
            siteMask|=(1u<<indices.y)|(1u<<indices.z);
        }
    M8FlowerInterval3 sites[7];
    M8FlowerInterval amplitudes[7];
    [loop]for(uint child=0u;child<7u;child++)
    {
        children[child]=(M8FlowerVInterval)0; // EMPTY: no metric innovation.
        amplitudes[child]=M8FlowerI(0,0);
        sites[child]=M8FlowerSkinPoint(0.0.xxx);
        if((siteMask&(1u<<child))!=0u && !M8FlowerSkinRootWorld(roots[child],sites[child]))
            return M8_FLOWER_SKIN_AMBIGUOUS;
    }
    if(!M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4))return M8_FLOWER_SKIN_AMBIGUOUS;
    M8FlowerInterval amplitude3=M8FlowerI(0,0),amplitude4=M8FlowerI(0,0);
    if(depth>1u && !M8FlowerSkinReadAmplitude(run,existing,3u,c3,c4,amplitude3))
        return M8_FLOWER_SKIN_AMBIGUOUS;
    if(depth>2u && !M8FlowerSkinReadAmplitude(run,existing,4u,c3,c4,amplitude4))
        return M8_FLOWER_SKIN_AMBIGUOUS;
    M8FlowerInterval inheritedRange=M8FlowerIAdd(
        M8FlowerIMul(amplitude3,M8FlowerI(0,1)),M8FlowerIMul(amplitude4,M8FlowerI(0,1)));
    uint chamberCount=depth==1u?36u:depth==2u?216u:1296u;
    uint constrainedMask=0u;
    [loop]for(uint reductionPass=0u;reductionPass<2u;reductionPass++)
    {
        [loop]for(uint ordinal=0u;ordinal<chamberCount;ordinal++)
        {
            M8FlowerInterval3 footprintTriangle[3],chart[3];uint3 rules;uint child;
            if(!M8FlowerSkinMetricChamber(sites,depth,c3,c4,activeWedgeMask,ordinal,
                footprintTriangle,chart,rules,child))continue;
            supportMask|=1u<<child;
            M8FlowerSkinMetricFrame frame;
            if(!M8FlowerSkinMetricFrameFromTriangle(footprintTriangle,frame))return M8_FLOWER_SKIN_AMBIGUOUS;
            M8FlowerInterval range=inheritedRange;
            if(reductionPass!=0u)range=M8FlowerIAdd(range,M8FlowerIMul(amplitudes[child],M8FlowerI(0,1)));
            int2 first,last;M8FlowerInterval modelDepth;
            if(!M8FlowerSkinMetricPixelCover(frame,footprintTriangle,range,first,last,modelDepth))
                return M8_FLOWER_SKIN_AMBIGUOUS;
            [loop]for(int y=first.y;y<=last.y;y++)
            [loop]for(int x=first.x;x<=last.x;x++)
            {
                M8FlowerInterval measuredDepth;
                M8FlowerInterval3 measuredNormal;
                if(!M8FlowerSkinMetricMeasurement(int2(x,y),owner,ownerFlags,normalError,offsetError,
                    measuredDepth,measuredNormal))return M8_FLOWER_SKIN_AMBIGUOUS;
                // Decode the measured centre first; the validation pass
                // then decodes the entire model-depth pixel cell. Both use
                // the same exact inverse projection, not cloned dividers.
                M8FlowerInterval3 positions[2];
                [loop]for(uint projection=0u;projection<=reductionPass;projection++)
                {
                    M8FlowerInterval depth;
                    if(projection==0u)depth=measuredDepth;else depth=modelDepth;
                    if(!M8FlowerSkinDepthCell(int2(x,y),depth,projection!=0u,positions[projection]))
                        return M8_FLOWER_SKIN_AMBIGUOUS;
                }
                M8FlowerInterval3 measuredPosition=positions[0];
                M8FlowerInterval3 localitySource=measuredPosition;
                if(reductionPass!=0u)localitySource=positions[1];
                M8FlowerInterval3 local;
                if(!M8FlowerSkinMetricCoordinates(frame,localitySource,local))return M8_FLOWER_SKIN_AMBIGUOUS;
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
                    return M8_FLOWER_SKIN_AMBIGUOUS;
                if(reductionPass==0u)
                {
                    bool constrained=(constrainedMask&(1u<<child))!=0u;
                    if(!M8FlowerSkinConstrainAmplitude(M8FlowerISub(height,inherited),bubble,
                        amplitudes[child],constrained))return M8_FLOWER_SKIN_AMBIGUOUS;
                    if(constrained)constrainedMask|=1u<<child;
                }
                else
                {
                    M8FlowerInterval composed=M8FlowerIAdd(inherited,M8FlowerIMul(amplitudes[child],bubble));
                    // Requiring the complete composed interval to fit the
                    // measured enclosure is stronger than an overlap test
                    // at a centroid. Incompatible boundary values reject V;
                    // they never move the L2 carrier or replace parent V.
                    if(composed.lo<height.lo || composed.hi>height.hi)return M8_FLOWER_SKIN_AMBIGUOUS;
                }
            }
        }
        if(reductionPass==0u)
        {
            if((constrainedMask&supportMask)!=supportMask)return M8_FLOWER_SKIN_AMBIGUOUS;
            [loop]for(uint child=0u;child<7u;child++)
            {
                if((supportMask&(1u<<child))==0u)continue;
                if(!M8FlowerSkinEncodeAmplitude(amplitudes[child],children[child]))
                    return M8_FLOWER_SKIN_AMBIGUOUS;
                amplitudes[child]=M8FlowerSkinDecodeAmplitude(children[child]);
            }
        }
    }
    float2 intervals[7];
    [loop]for(uint child=0u;child<7u;child++)
        intervals[child]=float2(amplitudes[child].lo,amplitudes[child].hi);
    return M8FlowerClassifyScalarSkinSplit(intervals,constrainedMask,supportMask);
}

uint M8FlowerMeasureRgbSkinChildren(M8FlowerPhaseRootEvidence roots[7],uint parentOrdinal,uint activeWedgeMask,
    int3 owner,uint ownerFlags,float normalError,float offsetError,
    M8ThreadColorInterval inherited,out M8ThreadColorInterval children[7],
    out uint supportMask)
{
    M8FlowerInterval3 footprints[7];uint covered;
    supportMask=0u;
    [loop]for(uint child=0u;child<7u;child++)children[child]=inherited;
    if(!M8FlowerSkinChildFootprints(roots,parentOrdinal,activeWedgeMask,footprints,covered))
        return M8_FLOWER_SKIN_AMBIGUOUS;
    // All seven logical children exist. Only the exact generated chamber
    // intersection decides whether one has support inside this L2 carrier.
    // EMPTY is not missing camera evidence and never supplies measured zero.
    supportMask=covered;
    if(supportMask==0u)return M8_FLOWER_SKIN_UNIFORM;
    float2 intervals[21];uint certain=0u;
    [loop]for(uint child=0u;child<7u;child++)
    {
        intervals[3u*child]=0.0.xx;
        intervals[3u*child+1u]=0.0.xx;
        intervals[3u*child+2u]=0.0.xx;
        if((supportMask&(1u<<child))==0u)continue;
        if(!M8FlowerSkinDirectFootprint(footprints[child],owner,ownerFlags,normalError,offsetError))
            return M8_FLOWER_SKIN_AMBIGUOUS;
        M8FlowerInterval3 eyeRgb[2];
        [loop]for(uint eye=0u;eye<2u;eye++)
            if(!M8FlowerSkinRgbFootprint(eye,footprints[child],eyeRgb[eye]))
                return M8_FLOWER_SKIN_AMBIGUOUS;
        M8FlowerInterval3 left=eyeRgb[0],right=eyeRgb[1];
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
    return M8FlowerClassifyRgbSkinSplit(intervals,certain,supportMask);
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

// Caller supplies a genuinely CERTAIN seven-site carrier selected by the
// direct/flag predicate, not a guessed sign mask. This commits measurement
// only: geometry admission and Thread storage do not select each other.
uint M8FlowerCommitRgbSkinSplit(uint slot,uint local,uint slotGeneration,uint flowerKey,
    uint parentOrdinal,uint activeWedgeMask,M8FlowerPhaseRootEvidence roots[7],float normalError,float offsetError,
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
    if(!M8FlowerSkinCarrierIdentity(owner,flowerKey,activeWedgeMask,roots))return M8_FLOWER_ARENA_INVALID;
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
    M8ThreadColorInterval canonical[7],thread[7],inherited;
    if(!M8FlowerSkinInheritedColor(parentOrdinal,state.packedColor,run,existing,inherited))
        return M8_FLOWER_ARENA_INVALID;
    uint supportMask;
    classification=M8FlowerMeasureRgbSkinChildren(roots,parentOrdinal,activeWedgeMask,owner,
        state.flags,normalError,offsetError,inherited,canonical,supportMask);
    if(supportMask==0u)return M8_FLOWER_ARENA_OK;
    bool replacing=existing && (bits[parentOrdinal>>5u]&(1u<<(parentOrdinal&31u)))!=0u;
    if(classification==M8_FLOWER_SKIN_AMBIGUOUS ||
        (classification==M8_FLOWER_SKIN_UNIFORM && !replacing))return M8_FLOWER_ARENA_OK;
    uint depth,c3,c4;
    M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4);
    [loop]for(uint child=0u;child<7u;child++)
    {
        uint rank=depth==1u?M8FlowerSkinL3ChildRankAt(child):depth==2u?
            M8FlowerSkinL4ChildRankAt(7u*c3+child):M8FlowerSkinL5ChildRankAt(49u*c3+7u*c4+child);
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

uint M8FlowerCommitMetricSkinSplit(uint slot,uint local,uint slotGeneration,uint flowerKey,
    uint parentOrdinal,uint activeWedgeMask,M8FlowerPhaseRootEvidence roots[7],float normalError,float offsetError,
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
    if(!M8FlowerSkinCarrierIdentity(owner,flowerKey,activeWedgeMask,roots))return M8_FLOWER_ARENA_INVALID;
    uint ownerRef=M8FlowerFindOwner(slot,local,slotGeneration),epoch=M8FlowerGetOwnerEpoch(ownerRef);
    M8FlowerSkinMetricRun run;uint runRef;
    bool existing=M8FlowerFindMetricRun(ownerRef,flowerKey,epoch,run,runRef);
    uint2 bits=uint2(run.SplitBitsLo,run.SplitBitsHi);
    if(parentOrdinal!=0u)
    {
        uint predecessor=parentOrdinal<8u?0u:1u+(parentOrdinal-8u)/7u;
        if(!existing || (bits[predecessor>>5u]&(1u<<(predecessor&31u)))==0u)
        {classification=M8_FLOWER_SKIN_UNIFORM;return M8_FLOWER_ARENA_OK;}
    }
    M8FlowerVInterval canonical[7],thread[7];uint supportMask;
    classification=M8FlowerMeasureMetricSkinChildren(roots,parentOrdinal,activeWedgeMask,owner,state.flags,
        normalError,offsetError,run,existing,canonical,supportMask);
    if(supportMask==0u)return M8_FLOWER_ARENA_OK;
    bool replacing=existing && (bits[parentOrdinal>>5u]&(1u<<(parentOrdinal&31u)))!=0u;
    if(classification==M8_FLOWER_SKIN_AMBIGUOUS ||
        (classification==M8_FLOWER_SKIN_UNIFORM && !replacing))return M8_FLOWER_ARENA_OK;
    uint depth,c3,c4;
    M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4);
    [loop]for(uint child=0u;child<7u;child++)
    {
        uint rank=depth==1u?M8FlowerSkinL3ChildRankAt(child):depth==2u?
            M8FlowerSkinL4ChildRankAt(7u*c3+child):M8FlowerSkinL5ChildRankAt(49u*c3+7u*c4+child);
        thread[rank]=canonical[child];
    }
    if(replacing)
    {
        uint group=run.GroupBase+M8FlowerSplitRank(bits,parentOrdinal);
        if(group<run.GroupBase || group>0xffffffffu/56u || !M8FlowerDetailRange(group*56u,56u))
            return M8_FLOWER_ARENA_INVALID;
        bool same=true;
        [loop]for(uint child=0u;child<7u;child++)
        {
            uint2 previous=M8_FLOWER_DETAIL_SOURCE.Load2(group*56u+8u*child);
            same=same && all(previous==uint2(asuint(thread[child].Lower),asuint(thread[child].Upper)));
        }
        if(same)return M8_FLOWER_ARENA_OK;
    }
    uint status=M8_FLOWER_ARENA_OK;
    if(ownerRef==0u)
    {
        status=M8FlowerEnsureOwner(slot,local,slotGeneration,state.flags,_M8DualPublishingGeneration,ownerRef);
        if(status!=M8_FLOWER_ARENA_OK)return status;
        M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
    }
    status=M8FlowerCommitMetricGroup(ownerRef,flowerKey,parentOrdinal,thread,
        _M8DualPublishingGeneration,_M8DualRetiredGeneration);
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
    uint depth,c3,c4;
    if(!M8FlowerSkinParentAddress(parentOrdinal,depth,c3,c4))return true;
    if(depth==1u)return true;
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        if((wedgeMask&(1u<<wedge))==0u)continue;
        [loop]for(uint order3=0u;order3<6u;order3++)
        {
            uint rule3=asuint(M8FlowerSkinChamberAt(6u*wedge+order3).w);
            if((rule3&255u)!=c3)continue;
            if(depth==2u)return true;
            uint wedge3=(rule3>>8u)&255u;
            [loop]for(uint order4=0u;order4<6u;order4++)
            {
                uint rule4=asuint(M8FlowerSkinChamberAt(6u*wedge3+order4).w);
                if((rule4&255u)==c4)return true;
            }
        }
    }
    return false;
}

// Both persistent signals consume the SAME finite canonical parent workset.
// A successful RGB write does not consume a still-blocked V write (or vice
// versa); retry replays the exact seven-value comparison before advancing.
// The physical masks remain separate. Their union belongs only to readout.
bool M8FlowerSkinHasRootRefinement(uint slot,uint local,uint slotGeneration,uint flowerKey)
{
    uint ownerRef=M8FlowerFindOwner(slot,local,slotGeneration);
    uint epoch=M8FlowerGetOwnerEpoch(ownerRef),runRef;
    M8ThreadRun rgb;M8FlowerSkinMetricRun metric;
    bool rgbSplit=M8FlowerFindThreadRun(ownerRef,flowerKey,epoch,rgb,runRef) &&
        (rgb.SplitBitsLo&1u)!=0u;
    bool metricSplit=M8FlowerFindMetricRun(ownerRef,flowerKey,epoch,metric,runRef) &&
        (metric.SplitBitsLo&1u)!=0u;
    return rgbSplit || metricSplit;
}

uint M8FlowerDrainSkinCarrier(uint slot,uint local,uint slotGeneration,uint flowerKey,uint activeWedgeMask,
    uint unresolvedWedgeMask,M8FlowerPhaseRootEvidence roots[7],float normalError,float offsetError,uint quantum,
    inout uint canonicalCursor,out uint progressed,out uint ambiguous)
{
    progressed=0u;ambiguous=0u;
    if(canonicalCursor>57u || activeWedgeMask==0u || activeWedgeMask>=64u ||
        unresolvedWedgeMask>=64u || (activeWedgeMask&unresolvedWedgeMask)!=0u)
        return M8_FLOWER_ARENA_INVALID;
    if(canonicalCursor>0u && canonicalCursor<57u &&
        !M8FlowerSkinHasRootRefinement(slot,local,slotGeneration,flowerKey))
    {
        canonicalCursor=57u;progressed=1u;
        return M8_FLOWER_ARENA_OK;
    }
    uint consumed=0u;
    [loop]while(canonicalCursor<57u && consumed<quantum)
    {
        uint parentOrdinal=0u;
        if(canonicalCursor>0u && canonicalCursor<8u)
            parentOrdinal=1u+M8FlowerSkinL3ChildRankAt(canonicalCursor-1u);
        else if(canonicalCursor>=8u)
            parentOrdinal=8u+M8FlowerSkinL4ParentThreadAt(canonicalCursor-8u);
        uint rgbClassification=M8_FLOWER_SKIN_AMBIGUOUS,metricClassification=M8_FLOWER_SKIN_AMBIGUOUS;
        if(!M8FlowerSkinParentTouchesWedges(parentOrdinal,unresolvedWedgeMask))
        {
            bool rgbChanged,metricChanged;
            uint status=M8FlowerCommitRgbSkinSplit(slot,local,slotGeneration,flowerKey,parentOrdinal,activeWedgeMask,
                roots,normalError,offsetError,rgbClassification,rgbChanged);
            if(status!=M8_FLOWER_ARENA_OK)return status;
            if(rgbChanged)progressed++;
            status=M8FlowerCommitMetricSkinSplit(slot,local,slotGeneration,flowerKey,parentOrdinal,activeWedgeMask,
                roots,normalError,offsetError,metricClassification,metricChanged);
            if(status!=M8_FLOWER_ARENA_OK)return status;
            if(metricChanged)progressed++;
        }
        // Genuine information ambiguity exhausts this observation's branch;
        // it does not become an endless same-input scheduling retry.
        if(rgbClassification==M8_FLOWER_SKIN_AMBIGUOUS)ambiguous++;
        if(metricClassification==M8_FLOWER_SKIN_AMBIGUOUS)ambiguous++;
        canonicalCursor++;consumed++;progressed++;
        if(canonicalCursor==1u && !M8FlowerSkinHasRootRefinement(slot,local,slotGeneration,flowerKey))
            canonicalCursor=57u;
    }
    return M8_FLOWER_ARENA_OK;
}

#endif
