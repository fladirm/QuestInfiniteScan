#ifndef SIGMA_NATIVE_ORACLE_MATH_INCLUDED
#define SIGMA_NATIVE_ORACLE_MATH_INCLUDED

uint2 SigmaNativeShadowWeight(int numerator)
{
    // numerator / 4 in Q16.48 => numerator * 2^46; low limb is zero.
    return uint2(0u, asuint(numerator * 16384));
}

bool SigmaNativeStateIsZero(uint stateOffset)
{
    uint aggregate = 0u;
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
    {
        uint2 value = _NativeStates[stateOffset + lane];
        aggregate |= value.x | value.y;
    }
    return aggregate == 0u;
}

void SigmaNativeEvaluateState(uint stateOffset, out uint2 order,
    out uint2 optical, inout uint valid)
{
    uint2 shadow[4];
    [unroll]
    for (uint axis = 0u; axis < 4u; ++axis)
    {
        uint2 sum = uint2(0u, 0u);
        [unroll]
        for (uint lane = 0u; lane < 16u; ++lane)
        {
            uint2 weight = SigmaNativeShadowWeight(
                SigmaMerkabaShadowNumerator(lane, axis));
            uint2 term = SigmaQ48MulNearestEven(
                _NativeStates[stateOffset + lane], weight, valid);
            sum = SigmaQ48AddChecked(sum, term, valid);
        }
        shadow[axis] = sum;
    }
    order = uint2(0u, 0u);
    optical = uint2(0u, 0u);
    [unroll]
    for (uint axis = 0u; axis < 4u; ++axis)
    {
        order = SigmaQ48AddChecked(order,
            SigmaQ48MulNearestEven(shadow[axis], _NativeQueryRows[axis], valid),
            valid);
        optical = SigmaQ48AddChecked(optical,
            SigmaQ48MulNearestEven(shadow[axis], _NativeQueryRows[4u + axis],
                valid), valid);
    }
}

bool SigmaNativePointInInterval(uint2 value, uint2 lower, uint2 upper)
{
    return !SigmaI64Less(value, lower) && !SigmaI64Less(upper, value);
}

#endif
