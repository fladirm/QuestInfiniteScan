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

bool SigmaGeometryReadoutPlan(uint2 state[16], out float3 position,
    out float informationMass)
{
    uint valid = 1u;
    uint2 geometry[4];
    SigmaGeometryGPlan(state, geometry, valid);
    bool supported = valid != 0u &&
        SigmaQ48Less(uint2(0u, 0u), geometry[0]);
    uint2 x = uint2(0u, 0u);
    uint2 y = uint2(0u, 0u);
    uint2 z = uint2(0u, 0u);
    if (supported)
    {
        x = SigmaQ48DivNearestEven(geometry[1], geometry[0], valid);
        y = SigmaQ48DivNearestEven(geometry[2], geometry[0], valid);
        z = SigmaQ48DivNearestEven(geometry[3], geometry[0], valid);
    }
    supported = supported && valid != 0u;
    position = supported ? float3(SigmaQ48ToReadoutFloat(x),
        SigmaQ48ToReadoutFloat(y), SigmaQ48ToReadoutFloat(z)) : 0.0;
    informationMass = supported ? SigmaQ48ToReadoutFloat(geometry[0]) : 0.0;
    return supported;
}

#endif
