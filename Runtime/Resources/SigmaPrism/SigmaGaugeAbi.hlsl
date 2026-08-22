#ifndef SIGMA_GAUGE_ABI_INCLUDED
#define SIGMA_GAUGE_ABI_INCLUDED

#include "SigmaConstraintLedgerAbi.hlsl"
#include "SigmaTopologyAbi.hlsl"

#define SIGMA_GAUGE_AXIS_X 0u
#define SIGMA_GAUGE_AXIS_Y 1u
#define SIGMA_GAUGE_POSITIVE 0u
#define SIGMA_GAUGE_NEGATIVE 1u
#define SIGMA_GAUGE_REQUIRED_NULL_BANDS 2u
#define SIGMA_GAUGE_STATUS_WORD4 4u
#define SIGMA_GAUGE_FAILED (1u << 31u)

// 64 bytes. Small asynchronous scheduler result, never physical state.
struct SigmaGaugeRequestGpu
{
    uint4 control;  // valid, source block, axis, direction
    uint4 region;   // span blocks, proof slot, source generation, revision
    uint4 metric;   // reproduction error and joint width Q48
    uint4 evidence; // independent keys, source mask, proof revision
};

// 16 bytes. One immutable raw observation clone instruction.
struct SigmaGaugeRawCloneGpu
{
    uint4 transfer; // source raw, target raw, target block, source block
};

uint SigmaGaugeBlockX(uint block) { return block & 7u; }
uint SigmaGaugeBlockY(uint block) { return block >> 3u; }
uint SigmaGaugeBlock(uint x, uint y) { return y * 8u + x; }

uint SigmaGaugeSourceAxisBlock(SigmaGaugeRequestGpu request)
{
    return request.control.z == SIGMA_GAUGE_AXIS_X
        ? SigmaGaugeBlockX(request.control.y)
        : SigmaGaugeBlockY(request.control.y);
}

int SigmaGaugeOrientedCoordinate(int pageCoordinate,
    SigmaGaugeRequestGpu request)
{
    int sourceStart = (int)SigmaGaugeSourceAxisBlock(request) * 8;
    return request.control.w == SIGMA_GAUGE_NEGATIVE
        ? sourceStart + 7 - pageCoordinate
        : pageCoordinate - sourceStart;
}

int SigmaGaugePageCoordinate(int oriented,
    SigmaGaugeRequestGpu request)
{
    int sourceStart = (int)SigmaGaugeSourceAxisBlock(request) * 8;
    return request.control.w == SIGMA_GAUGE_NEGATIVE
        ? sourceStart + 7 - oriented
        : sourceStart + oriented;
}

int SigmaGaugeMapRetainedCoordinate(int sourceCoordinate,
    SigmaGaugeRequestGpu request)
{
    int oriented = SigmaGaugeOrientedCoordinate(sourceCoordinate, request);
    int regionLength = (int)request.region.x * 8;
    int retainedLength = regionLength -
        (int)SIGMA_GAUGE_REQUIRED_NULL_BANDS * 8;
    if (oriented < 0 || oriented >= regionLength)
        return sourceCoordinate;
    if (oriented >= retainedLength)
        return -1;
    int mapped = oriented < 8 ? oriented * 2 : oriented + 8;
    return SigmaGaugePageCoordinate(mapped, request);
}

uint SigmaGaugeMapSourceSample(uint sourceSample,
    SigmaGaugeRequestGpu request, out uint targetSample)
{
    uint x = sourceSample & 63u;
    uint y = sourceSample >> 6u;
    int sourceAxis = request.control.z == SIGMA_GAUGE_AXIS_X
        ? (int)x : (int)y;
    int targetAxis = SigmaGaugeMapRetainedCoordinate(sourceAxis, request);
    if (targetAxis < 0 || targetAxis >= 64)
    {
        targetSample = 0u;
        return 0u;
    }
    uint targetX = request.control.z == SIGMA_GAUGE_AXIS_X
        ? (uint)targetAxis : x;
    uint targetY = request.control.z == SIGMA_GAUGE_AXIS_Y
        ? (uint)targetAxis : y;
    targetSample = targetY * 64u + targetX;
    return 1u;
}

#endif
