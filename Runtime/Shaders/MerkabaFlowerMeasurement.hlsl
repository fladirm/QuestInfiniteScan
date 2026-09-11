#ifndef GENESIS_MERKABA_FLOWER_MEASUREMENT_INCLUDED
#define GENESIS_MERKABA_FLOWER_MEASUREMENT_INCLUDED

#include "MerkabaObservationScope.hlsl"
#include "MerkabaSphereFlower.generated.hlsl"

float4x4 _MerkabaWorldToGrid;
float4x4 _MerkabaGridToWorld;
float4 _M8PlaneErrorBounds;

// The frozen sensor frame is transformed once by Emit. Owner-relative plane
// packing is then shared by every consumer of that immutable 16-byte record.
bool M8FlowerMeasurementFrame(uint sourcePixel,out float3 worldPosition,
    out float3 measuredGrid,out float3 measuredNormal)
{
    measuredGrid=0.0.xxx;measuredNormal=0.0.xxx;
    worldPosition = 0.0.xxx;
    if (sourcePixel >= gsDepthTexSize.x * gsDepthTexSize.y ||
        _M8PlaneErrorBounds.w != 1.0 ||
        !all(M8FlowerIsFinite(_M8PlaneErrorBounds)) ||
        any(_M8PlaneErrorBounds.xy < 0.0) ||
        !M8FlowerIsFinite(_MerkabaMaxUpdateDistance) ||
        _MerkabaMaxUpdateDistance <= 0.0) return false;
    uint2 pixel = uint2(sourcePixel % gsDepthTexSize.x,
        sourcePixel / gsDepthTexSize.x);
    float depth = gsDepthTex.Load(int3(pixel,0));
    float4 measured = gsDepthNormalTex.Load(int3(pixel,0));
    if (!(depth > 0.0 && depth < 1.0) || measured.w != 1.0 ||
        !all(M8FlowerIsFinite(measured.xyz))) return false;
    precise float2 uv = (float2(pixel)+0.5)/float2(gsDepthTexSize);
    worldPosition = gsDepthNDCtoWorld(float3(uv,depth));
    if (!M8ObservationContains(worldPosition,gsDepthEyePos())) return false;
    precise float3 gridPosition = mul(_MerkabaWorldToGrid,
        float4(worldPosition,1.0)).xyz;
    // A plane normal is a covector. Even for the rigid scan frame use the
    // canonical inverse-transpose transport, not position-vector transport.
    precise float3 gridNormal = mul(transpose((float3x3)_MerkabaGridToWorld),
        measured.xyz);
    precise float3 normalSquare = gridNormal*gridNormal;
    precise float normalSquareXY = normalSquare.x+normalSquare.y;
    precise float normalLength = sqrt(normalSquareXY+normalSquare.z);
    if (!(normalLength > 0.0) || !M8FlowerIsFinite(normalLength)) return false;
    // The persisted offset is a metric signed distance. Compute it from
    // the unit normal that plane packing will encode, not from a scaled
    // world-to-grid normal (including ordinary binary32 rotation drift).
    gridNormal /= normalLength;
    measuredGrid=gridPosition;measuredNormal=gridNormal;
    return true;
}

bool M8FlowerPackMeasurement(int3 owner,float3 gridPosition,float3 gridNormal,out uint plane)
{
    plane=0u;
    precise float3 relative = gridPosition-float3(owner)*M8_FLOWER_LATTICE_STEP;
    precise float3 terms = relative*gridNormal;
    precise float xy = terms.x+terms.y;
    precise float offset = xy+terms.z;
    return M8FlowerTryPackPlane(0u,gridNormal,offset,plane);
}

bool M8FlowerMeasurement(uint sourcePixel,int3 owner,out uint plane,out float3 world)
{
    float3 grid,normal;
    plane=0u;
    return M8FlowerMeasurementFrame(sourcePixel,world,grid,normal) &&
        M8FlowerPackMeasurement(owner,grid,normal,plane);
}

#endif
