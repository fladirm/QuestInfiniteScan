// Production is compiled offline into the native Vulkan plugin. The Editor
// fixture includes this same source; no runtime Unity ComputeShader is needed.

#include "SigmaCarrierAbi.hlsl"
#include "SigmaGeometryReadout.hlsl"

StructuredBuffer<uint2> _CarrierState0;
StructuredBuffer<uint2> _CarrierState1;
StructuredBuffer<SigmaCarrierPageMetaGpu> _PageMetadata0;
StructuredBuffer<SigmaCarrierPageMetaGpu> _PageMetadata1;
StructuredBuffer<uint> _PublishedRevisionRoot;

RWStructuredBuffer<SigmaPureEyeReadoutGpu> _ReadoutSamples0;
RWStructuredBuffer<SigmaPureEyeReadoutGpu> _ReadoutSamples1;
RWStructuredBuffer<uint> _CurrentPageSlots0;
RWStructuredBuffer<uint> _CurrentPageSlots1;
RWStructuredBuffer<SigmaCarrierPageMetaGpu> _RenderPageMetadata0;
RWStructuredBuffer<SigmaCarrierPageMetaGpu> _RenderPageMetadata1;
RWStructuredBuffer<uint> _ReadoutDrawArguments0;
RWStructuredBuffer<uint> _ReadoutDrawArguments1;

// Eleven uint4 values, serialized without matrices by SigmaRenderer. All
// current-eye inputs are immutable for one disposable BACK generation.
uint4 _N6ReadoutConstants[11];

#define SIGMA_READOUT_VERTICES_PER_PAGE 24576u

groupshared uint SigmaReadoutVisible;
groupshared SigmaCarrierPageMetaGpu SigmaReadoutMetadata;

uint2 SigmaReadoutEyePair(uint4 packed, uint eye)
{
    return eye == 0u ? packed.xy : packed.zw;
}

uint2 SigmaReadoutEyeComponent(uint component, uint eye)
{
    return SigmaReadoutEyePair(_N6ReadoutConstants[component], eye);
}

uint2 SigmaReadoutDot3(uint2 left[3], uint2 right[3], inout uint valid)
{
    uint2 result = SIGMA_Q48_ZERO;
    [unroll]
    for (uint axis = 0u; axis < 3u; ++axis)
        result = SigmaQ48AddChecked(result,
            SigmaQ48MulNearestEven(left[axis], right[axis], valid), valid);
    return result;
}

bool SigmaReadoutVisibleAtRoot(uint bank, uint pageSlot, uint pageCapacity,
    out SigmaCarrierPageMetaGpu page)
{
    if (bank == 0u)
        page = _PageMetadata0[pageSlot];
    else
        page = _PageMetadata1[pageSlot];
    uint siblingSlot = pageSlot ^ 1u;
    bool hasSibling = siblingSlot < pageCapacity;
    SigmaCarrierPageMetaGpu sibling = page;
    if (hasSibling)
    {
        if (bank == 0u)
            sibling = _PageMetadata0[siblingSlot];
        else
            sibling = _PageMetadata1[siblingSlot];
    }
    return SigmaCarrierVisibleAtRoot(page, sibling, hasSibling,
        _PublishedRevisionRoot[0u]);
}

void SigmaReadoutLoadState(uint bank, uint pageSlot, uint sample,
    out uint2 state[16])
{
    uint stateBase = pageSlot * SIGMA_PAGE_LANE_COUNT +
        sample * SIGMA_LANE_COUNT;
    [unroll]
    for (uint lane = 0u; lane < SIGMA_LANE_COUNT; ++lane)
        state[lane] = bank == 0u ? _CarrierState0[stateBase + lane] :
            _CarrierState1[stateBase + lane];
}

bool SigmaReadoutEvaluateEye(uint2 tangent[4], uint2 commonMode,
    uint2 seed[3], uint eye, out float4 positionSupport,
    out float4 colourOrder)
{
    uint valid = 1u;
    uint2 origin[3];
    uint2 forward[3];
    uint2 ray[3];
    [unroll]
    for (uint axis = 0u; axis < 3u; ++axis)
    {
        origin[axis] = SigmaReadoutEyeComponent(axis, eye);
        forward[axis] = SigmaReadoutEyeComponent(3u + axis, eye);
        ray[axis] = SigmaQ48SubChecked(seed[axis], origin[axis], valid);
    }

    uint4 permutation = uint4(0u, 1u, 2u, 3u);
    int globalSign;
    SigmaMerkabaBuildInstrumentRowPermutation(ray, permutation, globalSign,
        valid);

    uint2 code[4];
    [unroll]
    for (uint leaf = 0u; leaf < 4u; ++leaf)
        SigmaMerkabaRecoverUnitCode(tangent[permutation[leaf]], commonMode,
            code[leaf], valid);

    uint2 nearPlane = SigmaReadoutEyePair(_N6ReadoutConstants[6], eye);
    uint2 farPlane = SigmaReadoutEyePair(_N6ReadoutConstants[7], eye);
    uint2 depthSpan = SigmaQ48SubChecked(farPlane, nearPlane, valid);
    uint2 depthDenominator = SigmaQ48SubChecked(farPlane,
        SigmaQ48MulNearestEven(code[0], depthSpan, valid), valid);
    uint2 viewZ = SigmaQ48DivNearestEven(
        SigmaQ48MulNearestEven(nearPlane, farPlane, valid),
        depthDenominator, valid);
    uint2 rayForward = SigmaReadoutDot3(ray, forward, valid);
    valid &= SigmaQ48Less(SIGMA_Q48_ZERO, rayForward) ? 1u : 0u;
    uint2 scale = valid != 0u
        ? SigmaQ48DivNearestEven(viewZ, rayForward, valid)
        : SIGMA_Q48_ZERO;

    uint2 exactPosition[3];
    [unroll]
    for (uint component = 0u; component < 3u; ++component)
        exactPosition[component] = SigmaQ48AddChecked(origin[component],
            SigmaQ48MulNearestEven(ray[component], scale, valid), valid);

    float3 position = float3(
        SigmaQ48ToReadoutFloat(exactPosition[0]),
        SigmaQ48ToReadoutFloat(exactPosition[1]),
        SigmaQ48ToReadoutFloat(exactPosition[2]));
    float3 colour = saturate(float3(
        SigmaQ48ToReadoutFloat(code[1]),
        SigmaQ48ToReadoutFloat(code[2]),
        SigmaQ48ToReadoutFloat(code[3])));
    float3 eyePosition = float3(
        SigmaQ48ToReadoutFloat(origin[0]),
        SigmaQ48ToReadoutFloat(origin[1]),
        SigmaQ48ToReadoutFloat(origin[2]));
    float range = length(position - eyePosition);
    float3 head = float3(
        SigmaQ48ToReadoutFloat(_N6ReadoutConstants[8].xy),
        SigmaQ48ToReadoutFloat(_N6ReadoutConstants[8].zw),
        SigmaQ48ToReadoutFloat(_N6ReadoutConstants[9].xy));
    float radius = SigmaQ48ToReadoutFloat(_N6ReadoutConstants[9].zw);
    bool supported = valid != 0u && all(isfinite(position)) &&
        isfinite(range) && range > 0.0 && distance(position, head) <= radius;
    positionSupport = supported ? float4(position, 1.0) : 0.0;
    // Exact projection-depth code and exact Q48 point are resolved above. This
    // metric range is only their disposable FP raster lowering.
    colourOrder = supported ? float4(colour, range) : 0.0;
    return supported;
}

[numthreads(64, 1, 1)]
void BuildPureEyeReadout(uint3 groupId : SV_GroupID,
    uint3 groupThreadId : SV_GroupThreadID)
{
    uint bank = groupId.z;
    if (bank >= 2u)
        return;
    uint pageCapacity = bank == 0u ? _N6ReadoutConstants[10].x :
        _N6ReadoutConstants[10].y;
    uint pageSlot = groupId.x;
    if (pageSlot >= pageCapacity)
        return;

    uint lane = groupThreadId.x;
    if (lane == 0u)
    {
        SigmaCarrierPageMetaGpu page = (SigmaCarrierPageMetaGpu)0;
        SigmaReadoutVisible = _SigmaExactBackendGate[0u] == 1u &&
            SigmaReadoutVisibleAtRoot(bank, pageSlot, pageCapacity, page)
                ? 1u : 0u;
        SigmaReadoutMetadata = page;
        if (pageSlot == 0u && groupId.y == 0u)
        {
            if (bank == 0u) _ReadoutDrawArguments0[1u] = 1u;
            else _ReadoutDrawArguments1[1u] = 1u;
        }
        if (SigmaReadoutVisible != 0u && groupId.y == 0u)
        {
            uint vertexOffset;
            uint activePage;
            if (bank == 0u)
            {
                InterlockedAdd(_ReadoutDrawArguments0[0u],
                    SIGMA_READOUT_VERTICES_PER_PAGE, vertexOffset);
                activePage = vertexOffset / SIGMA_READOUT_VERTICES_PER_PAGE;
                _CurrentPageSlots0[activePage] = pageSlot;
                _RenderPageMetadata0[pageSlot] = page;
            }
            else
            {
                InterlockedAdd(_ReadoutDrawArguments1[0u],
                    SIGMA_READOUT_VERTICES_PER_PAGE, vertexOffset);
                activePage = vertexOffset / SIGMA_READOUT_VERTICES_PER_PAGE;
                _CurrentPageSlots1[activePage] = pageSlot;
                _RenderPageMetadata1[pageSlot] = page;
            }
        }
    }
    GroupMemoryBarrierWithGroupSync();

    // A thread owns one sample. The Y grid covers the 64 fixed chunks of a
    // page; it is execution layout only, never a geometric coordinate.
    uint sample = groupId.y * 64u + lane;
    if (sample < SIGMA_PAGE_SAMPLE_COUNT)
    {
        SigmaPureEyeReadoutGpu readout = (SigmaPureEyeReadoutGpu)0;
        if (SigmaReadoutVisible != 0u &&
            sample < SigmaReadoutMetadata.activeSampleCount)
        {
            uint2 state[16];
            SigmaReadoutLoadState(bank, pageSlot, sample, state);
            uint valid = 1u;
            uint2 tangent[4];
            SigmaMerkabaEvaluateShadow(state, tangent, valid);
            uint2 commonMode = SigmaMerkabaEvaluateCommonMode(state, valid);
            uint2 seed[3];
            SigmaMerkabaEvaluateEyeCharacterSeed(state, seed, valid);
            if (valid != 0u)
            {
                SigmaReadoutEvaluateEye(tangent, commonMode, seed, 0u,
                    readout.leftPositionSupport, readout.leftColourOrder);
                SigmaReadoutEvaluateEye(tangent, commonMode, seed, 1u,
                    readout.rightPositionSupport, readout.rightColourOrder);
            }
        }
        uint output = pageSlot * SIGMA_PAGE_SAMPLE_COUNT + sample;
        if (bank == 0u) _ReadoutSamples0[output] = readout;
        else _ReadoutSamples1[output] = readout;
    }
}
