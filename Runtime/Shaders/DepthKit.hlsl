// Genesis RoomScan - Depth utility functions
// Adapted from Anaglyph/lasertag DepthKit (MIT)

// Downstream reconstruction consumes one joint four-stream measurement field.
// The raw Environment Depth L/R array exists only in StereoRgbdRefine.compute.
Texture2D<float> gsDepthTex;
Texture2D<float4> gsDepthNormalTex;
uniform uint2 gsDepthTexSize;

SamplerState gsBilinearClampSampler;
SamplerState gsPointClampSampler;

uniform float4x4 gsDepthProj[2];
uniform float4x4 gsDepthProjInv[2];
uniform float4x4 gsDepthView[2];
uniform float4x4 gsDepthViewInv[2];

uniform float4 gsDepthZParams; // (near, far, 0, 0)

float3 gsDepthEyePos()
{
    return float3(gsDepthViewInv[0][0][3],
        gsDepthViewInv[0][1][3], gsDepthViewInv[0][2][3]);
}

float gsDepthSample(float2 uv)
{
    return gsDepthTex.SampleLevel(gsPointClampSampler, uv, 0);
}

float gsDepthNDCToLinear(float depthNDC)
{
    float z = depthNDC * 2.0 - 1.0;
    float A = gsDepthProj[0][2][2];
    float B = gsDepthProj[0][2][3];
    return abs(B / (z + A));
}

float4 gsDepthNormalSample(float2 uv)
{
    return gsDepthNormalTex.SampleLevel(gsPointClampSampler, uv, 0);
}

float4 gsDepthWorldToHCS(float3 worldPos)
{
    return mul(gsDepthProj[0], mul(gsDepthView[0], float4(worldPos, 1)));
}

float4 gsDepthHCStoWorldH(float4 hcs)
{
    return mul(gsDepthViewInv[0], mul(gsDepthProjInv[0], hcs));
}

float3 gsDepthHCStoNDC(float4 hcs)
{
    return (hcs.xyz / hcs.w) * 0.5 + 0.5;
}

float4 gsDepthNDCtoHCS(float3 ndc)
{
    return float4(ndc * 2.0 - 1.0, 1);
}

float3 gsDepthWorldToNDC(float3 worldPos)
{
    float4 hcs = gsDepthWorldToHCS(worldPos);
    return gsDepthHCStoNDC(hcs);
}

float3 gsDepthNDCtoWorld(float3 ndc)
{
    float4 hcs = gsDepthNDCtoHCS(ndc);
    float4 worldH = gsDepthHCStoWorldH(hcs);
    return worldH.xyz / worldH.w;
}
