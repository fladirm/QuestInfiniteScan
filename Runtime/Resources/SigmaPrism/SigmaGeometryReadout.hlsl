#ifndef SIGMA_GEOMETRY_READOUT_INCLUDED
#define SIGMA_GEOMETRY_READOUT_INCLUDED

#include "SigmaOperatorPlan.hlsl"

static const uint2 SIGMA_Q48_ZERO = uint2(0u, 0u);
static const uint2 SIGMA_Q48_ONE = uint2(0u, 0x00010000u);

#include "Generated/SigmaGeneratedMerkabaProgram.hlsl"

// Disposable N6 output. Exact order, appearance and position have already been
// evaluated from the immutable full S16 value before this FP raster payload is
// written. Page/sample placement remains execution-only.
struct SigmaPureEyeReadoutGpu
{
    float4 leftPositionSupport;
    float4 leftColourOrder;
    float4 rightPositionSupport;
    float4 rightColourOrder;
};

float SigmaQ48ToReadoutFloat(uint2 raw)
{
    return (float)asint(raw.y) * (1.0 / 65536.0) +
        (float)raw.x * (1.0 / 281474976710656.0);
}

#endif
