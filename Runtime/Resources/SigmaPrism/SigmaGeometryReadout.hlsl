#ifndef SIGMA_GEOMETRY_READOUT_INCLUDED
#define SIGMA_GEOMETRY_READOUT_INCLUDED

#include "SigmaOperatorPlan.hlsl"

float SigmaQ48ToReadoutFloat(uint2 raw)
{
    // raw / 2^48 = signed(high32) / 2^16 + low32 / 2^48.
    // This conversion is disposable FP readout only; support and qdiv decisions
    // have already executed in exact packed Q16.48.
    return (float)asint(raw.y) * (1.0 / 65536.0) +
        (float)raw.x * (1.0 / 281474976710656.0);
}

// Exact support/readout decision shared by forward rasterization and intrinsic
// topology.  Keeping the checked projective divisions here prevents topology
// from treating an out-of-range projective state as physical contact when the
// renderer would correctly fail closed.
bool SigmaGeometryReadoutExact(uint2 state[16], out uint2 geometry[4],
    out uint2 projectivePosition[3], out uint readoutValid)
{
    uint valid = 1u;
    SigmaGeometryGPlan(state, geometry, valid);
    bool supported = valid != 0u &&
        SigmaQ48Less(uint2(0u, 0u), geometry[0]);
    [unroll]
    for (uint axis = 0u; axis < 3u; ++axis)
        projectivePosition[axis] = uint2(0u, 0u);
    if (supported)
    {
        [unroll]
        for (uint axis = 0u; axis < 3u; ++axis)
            projectivePosition[axis] = SigmaQ48DivNearestEven(
                geometry[axis + 1u], geometry[0], valid);
    }
    readoutValid = valid;
    return supported && valid != 0u;
}

bool SigmaGeometryReadoutPlan(uint2 state[16], out float3 position,
    out float informationMass)
{
    uint2 geometry[4];
    uint2 projectivePosition[3];
    uint readoutValid;
    bool supported = SigmaGeometryReadoutExact(state, geometry,
        projectivePosition, readoutValid);
    position = supported ? float3(
        SigmaQ48ToReadoutFloat(projectivePosition[0]),
        SigmaQ48ToReadoutFloat(projectivePosition[1]),
        SigmaQ48ToReadoutFloat(projectivePosition[2])) : 0.0;
    informationMass = supported ? SigmaQ48ToReadoutFloat(geometry[0]) : 0.0;
    return supported;
}

#endif
