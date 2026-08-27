#ifndef SIGMA_INVERSE_MATH_INCLUDED
#define SIGMA_INVERSE_MATH_INCLUDED

#include "SigmaOperatorPlan.hlsl"
#include "SigmaInverseAbi.hlsl"

StructuredBuffer<uint2> _DepthCalibrationQ48;

static const uint2 SIGMA_Q48_ZERO = uint2(0u, 0u);
static const uint2 SIGMA_Q48_ONE = uint2(0u, 0x00010000u);

uint2 SigmaCalibrationValue(uint eye, uint field)
{
    return _DepthCalibrationQ48[eye * SIGMA_DEPTH_VIEW_STRIDE + field];
}

// Exact conversion of the represented IEEE-754 sensor/readout value into
// Q16.48. Every closure decision after this boundary is packed arithmetic.
uint2 SigmaQ48FromFloatNearestEven(float value, inout uint valid)
{
    uint2 result = SIGMA_Q48_ZERO;
    uint bits = asuint(value);
    bool negative = (bits & 0x80000000u) != 0u;
    uint exponentBits = (bits >> 23u) & 0xffu;
    uint fraction = bits & 0x007fffffu;
    if (exponentBits == 0xffu)
    {
        valid = 0u;
    }
    else if (exponentBits != 0u || fraction != 0u)
    {
        uint mantissa = exponentBits == 0u ? fraction :
            fraction | 0x00800000u;
        int exponent = exponentBits == 0u ? -126 :
            (int)exponentBits - 127;
        int rawShift = exponent + 25;
        uint2 magnitude = uint2(mantissa, 0u);
        if (rawShift >= 0)
            magnitude = SigmaQ48ShiftLeftChecked(magnitude,
                (uint)rawShift, valid);
        else
            magnitude = SigmaQ48ShiftRightNearestEven(magnitude,
                (uint)(-rawShift), valid);
        result = SigmaApplyMagnitudeSign(magnitude, negative, valid);
    }
    return result;
}

uint2 SigmaQ48AbsDifference(uint2 a, uint2 b, inout uint valid)
{
    return SigmaQ48Less(a, b)
        ? SigmaQ48SubChecked(b, a, valid)
        : SigmaQ48SubChecked(a, b, valid);
}

SigmaQ48Bounds SigmaQ48Widen(uint2 centre, uint2 halfWidth,
    inout uint valid)
{
    SigmaQ48Bounds result;
    result.lo = SigmaQ48SubChecked(centre, halfWidth, valid);
    result.hi = SigmaQ48AddChecked(centre, halfWidth, valid);
    return result;
}

uint2 SigmaDepthHalfWidth(uint eye, uint2 range, inout uint valid)
{
    uint selected = 5u;
    [unroll]
    for (uint bin = 0u; bin < 6u; ++bin)
    {
        uint2 threshold = SigmaCalibrationValue(eye,
            SIGMA_CAL_RANGE_THRESHOLDS + bin);
        if (selected == 5u && !SigmaQ48Less(threshold, range))
            selected = bin;
    }
    return SigmaQ48AddChecked(SigmaCalibrationValue(eye,
            SIGMA_CAL_RANGE_WIDTHS + selected),
        SigmaCalibrationValue(eye, SIGMA_CAL_POSE_WIDTH), valid);
}

uint2 SigmaPriorHalfWidth(uint2 mass, inout uint valid)
{
    uint2 floorWidth = SigmaCalibrationValue(0u,
        SIGMA_CAL_PRIOR_FLOOR);
    uint2 ceilingWidth = SigmaCalibrationValue(0u,
        SIGMA_CAL_PRIOR_CEILING);
    uint2 width = floorWidth;
    uint2 threshold = SIGMA_Q48_ONE;
    [unroll]
    for (uint step = 0u; step < 16u; ++step)
    {
        bool widen = SigmaQ48Less(mass, threshold) &&
            SigmaQ48Less(width, ceilingWidth);
        if (widen)
        {
            threshold = SigmaQ48ShiftRightNearestEven(threshold, 1u,
                valid);
            width = SigmaQ48Min(ceilingWidth,
                SigmaQ48ShiftLeftChecked(width, 1u, valid));
        }
    }
    return SigmaQ48Max(floorWidth, SigmaQ48Min(width, ceilingWidth));
}

#endif
