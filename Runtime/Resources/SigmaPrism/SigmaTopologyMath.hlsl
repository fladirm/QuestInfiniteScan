#ifndef SIGMA_TOPOLOGY_MATH_INCLUDED
#define SIGMA_TOPOLOGY_MATH_INCLUDED

#include "SigmaGeometryReadout.hlsl"
#include "SigmaTopologyAbi.hlsl"

static const uint4 SIGMA_U128_ZERO = uint4(0u, 0u, 0u, 0u);
static const uint4 SIGMA_U128_ONE = uint4(1u, 0u, 0u, 0u);
static const uint4 SIGMA_U128_MAX =
    uint4(0xffffffffu, 0xffffffffu, 0xffffffffu, 0xffffffffu);

bool SigmaU128Equal(uint4 a, uint4 b)
{
    return all(a == b);
}

bool SigmaU128Less(uint4 a, uint4 b)
{
    if (a.w != b.w) return a.w < b.w;
    if (a.z != b.z) return a.z < b.z;
    if (a.y != b.y) return a.y < b.y;
    return a.x < b.x;
}

uint4 SigmaU128Add64(uint4 value, uint2 addend, inout uint valid)
{
    uint oldX = value.x;
    value.x += addend.x;
    uint carry = value.x < oldX ? 1u : 0u;
    uint oldY = value.y;
    value.y += addend.y;
    uint carryY = value.y < oldY ? 1u : 0u;
    oldY = value.y;
    value.y += carry;
    carryY |= value.y < oldY ? 1u : 0u;
    uint oldZ = value.z;
    value.z += carryY;
    uint carryZ = value.z < oldZ ? 1u : 0u;
    uint oldW = value.w;
    value.w += carryZ;
    if (value.w < oldW)
        valid = 0u;
    return value;
}

uint4 SigmaU128Add(uint4 a, uint4 b, inout uint valid)
{
    uint lowCarry;
    uint2 low = SigmaU64Add(a.xy, b.xy, lowCarry);
    uint highCarry;
    uint2 high = SigmaU64Add(a.zw, b.zw, highCarry);
    uint carryOut;
    uint2 incremented = SigmaU64Add(high, uint2(lowCarry, 0u), carryOut);
    if (highCarry != 0u || carryOut != 0u)
        valid = 0u;
    return uint4(low, incremented);
}

uint4 SigmaU128ShiftLeftSmall(uint4 value, uint shift, inout uint valid)
{
    if (shift == 0u)
        return value;
    if (shift >= 32u || (value.w >> (32u - shift)) != 0u)
    {
        valid = 0u;
        return SIGMA_U128_MAX;
    }
    return uint4(value.x << shift,
        (value.y << shift) | (value.x >> (32u - shift)),
        (value.z << shift) | (value.y >> (32u - shift)),
        (value.w << shift) | (value.z >> (32u - shift)));
}

uint4 SigmaS16L1(uint2 state[16], inout uint valid)
{
    uint4 sum = SIGMA_U128_ZERO;
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        sum = SigmaU128Add64(sum, SigmaU64AbsSigned(state[lane]), valid);
    return sum;
}

void SigmaDenseProductPlan(uint2 left[16], uint2 right[16],
    out uint2 output[16], inout uint valid)
{
    // The transition/associator descriptors genuinely require dense arbitrary
    // coefficient products. This is the explicitly selected generated fallback;
    // annihilator scanning below never enters it.
    // Arbitrary coefficient products are the explicitly selected dense fallback.
    // Keep the fixed 16x16 schedule as compact loops: forcing the compiler to
    // clone every widened Q16.48 primitive produces enormous Vulkan kernels with
    // no algebraic or runtime benefit.
    [loop]
    for (uint outputLane = 0u; outputLane < 16u; ++outputLane)
    {
        uint2 sum = uint2(0u, 0u);
        [loop]
        for (uint leftLane = 0u; leftLane < 16u; ++leftLane)
        {
            uint rightLane = leftLane ^ outputLane;
            uint2 term = SigmaQ48MulNearestEven(left[leftLane],
                right[rightLane], valid);
            if (SigmaMulBasisSign(leftLane, rightLane) < 0)
                term = SigmaQ48NegateChecked(term, valid);
            sum = SigmaQ48AddChecked(sum, term, valid);
        }
        output[outputLane] = sum;
    }
}

void SigmaTransitionPlan(uint2 left[16], uint2 right[16],
    out uint2 transition[16], inout uint valid)
{
    uint2 conjugated[16];
    SigmaConjugatePlan(left, conjugated, valid);
    SigmaDenseProductPlan(conjugated, right, transition, valid);
}

void SigmaAssociatorPlan(uint2 a[16], uint2 b[16], uint2 c[16],
    out uint2 associator[16], inout uint valid)
{
    uint2 ab[16];
    uint2 bc[16];
    uint2 leftBracket[16];
    uint2 rightBracket[16];
    SigmaDenseProductPlan(a, b, ab, valid);
    SigmaDenseProductPlan(ab, c, leftBracket, valid);
    SigmaDenseProductPlan(b, c, bc, valid);
    SigmaDenseProductPlan(a, bc, rightBracket, valid);
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        associator[lane] = SigmaQ48SubChecked(leftBracket[lane],
            rightBracket[lane], valid);
}

uint4 SigmaRightDyadL1(uint2 transition[16], int4 dyad, inout uint valid)
{
    uint4 error = SIGMA_U128_ZERO;
    [unroll]
    for (uint outputLane = 0u; outputLane < 16u; ++outputLane)
    {
        uint firstSource = (uint)dyad.x ^ outputLane;
        uint secondSource = (uint)dyad.z ^ outputLane;
        uint2 first = transition[firstSource];
        uint2 second = transition[secondSource];
        int firstSign = SigmaMulBasisSign(firstSource, (uint)dyad.x) * dyad.y;
        int secondSign = SigmaMulBasisSign(secondSource, (uint)dyad.z) * dyad.w;
        if (firstSign < 0)
            first = SigmaQ48NegateChecked(first, valid);
        if (secondSign < 0)
            second = SigmaQ48NegateChecked(second, valid);
        uint2 residual = SigmaQ48AddChecked(first, second, valid);
        error = SigmaU128Add64(error, SigmaU64AbsSigned(residual), valid);
    }
    return error;
}

bool SigmaRelativeNearGate(uint4 error, uint4 scale, uint shift)
{
    uint valid = 1u;
    uint4 shifted = SigmaU128ShiftLeftSmall(error, shift, valid);
    return valid != 0u && !SigmaU128Less(scale, shifted);
}

bool SigmaRelativeAssociatorGate(uint4 error, uint4 scale, uint shift)
{
    uint valid = 1u;
    uint4 shifted = SigmaU128ShiftLeftSmall(error, shift, valid);
    return valid != 0u && !SigmaU128Less(shifted, scale);
}

void SigmaLoadNullState(out uint2 state[16])
{
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
    {
        if (lane == (uint)SIGMA_Z_NULL_DYAD.x)
            state[lane] = SIGMA_Z_NULL_DYAD.y < 0
                ? uint2(0u, 0xffff0000u) : uint2(0u, 0x00010000u);
        else if (lane == (uint)SIGMA_Z_NULL_DYAD.z)
            state[lane] = SIGMA_Z_NULL_DYAD.w < 0
                ? uint2(0u, 0xffff0000u) : uint2(0u, 0x00010000u);
        else
            state[lane] = uint2(0u, 0u);
    }
}

bool SigmaStateHasContact(uint2 state[16], out uint readoutValid)
{
    uint2 geometry[4];
    uint2 projectivePosition[3];
    return SigmaGeometryReadoutExact(state, geometry, projectivePosition,
        readoutValid);
}

#endif
