#ifndef SIGMA_SEDENION16_INCLUDED
#define SIGMA_SEDENION16_INCLUDED

#include "Generated/SigmaGeneratedTables.hlsl"
#include "SigmaExactCompare.hlsl"

// Exact signed Q16.48 storage lowering. x is the least-significant limb; y is
// the signed most-significant limb. All mutation callers carry an explicit
// validity bit so checked overflow fails closed instead of saturating.

uint2 SigmaU64Add(uint2 a, uint2 b, out uint carry)
{
    uint low = a.x + b.x;
    carry = low < a.x ? 1u : 0u;
    uint high0 = a.y + b.y;
    uint carryHigh = high0 < a.y ? 1u : 0u;
    uint high = high0 + carry;
    carry = carryHigh | (high < high0 ? 1u : 0u);
    return uint2(low, high);
}

uint2 SigmaU64Increment(uint2 value, out uint carry)
{
    uint low = value.x + 1u;
    uint lowCarry = low == 0u ? 1u : 0u;
    uint high = value.y + lowCarry;
    carry = lowCarry != 0u && high == 0u ? 1u : 0u;
    return uint2(low, high);
}

uint2 SigmaU64Subtract(uint2 a, uint2 b, out uint borrow)
{
    uint low = a.x - b.x;
    uint lowBorrow = a.x < b.x ? 1u : 0u;
    uint high0 = a.y - b.y;
    uint highBorrow = a.y < b.y ? 1u : 0u;
    uint high = high0 - lowBorrow;
    borrow = highBorrow | (high0 < lowBorrow ? 1u : 0u);
    return uint2(low, high);
}

uint2 SigmaU64NegateRaw(uint2 value)
{
    uint2 inverted = ~value;
    uint ignored;
    return SigmaU64Increment(inverted, ignored);
}

uint2 SigmaU64AbsSigned(uint2 value)
{
    return (value.y & 0x80000000u) != 0u ? SigmaU64NegateRaw(value) : value;
}

uint2 SigmaU64ShiftRight(uint2 value, uint count)
{
    uint2 result = uint2(0u, 0u);
    if (count == 0u)
        return value;
    if (count < 32u)
        result = uint2((value.x >> count) | (value.y << (32u - count)),
            value.y >> count);
    else if (count < 64u)
        result = uint2(value.y >> (count - 32u), 0u);
    return result;
}

uint2 SigmaU64ShiftLeftRaw(uint2 value, uint count)
{
    uint2 result = uint2(0u, 0u);
    if (count == 0u)
        return value;
    if (count < 32u)
        result = uint2(value.x << count,
            (value.y << count) | (value.x >> (32u - count)));
    else if (count < 64u)
        result = uint2(0u, value.x << (count - 32u));
    return result;
}

uint2 SigmaI64ShiftRightRaw(uint2 value, uint count)
{
    if (count == 0u)
        return value;
    uint sign = (value.y & 0x80000000u) != 0u ? 0xffffffffu : 0u;
    if (count < 32u)
        return uint2((value.x >> count) | (value.y << (32u - count)),
            asuint(asint(value.y) >> count));
    if (count < 64u)
        return uint2(asuint(asint(value.y) >> (count - 32u)), sign);
    return uint2(sign, sign);
}

bool SigmaU64AnyLowBits(uint2 value, uint count)
{
    bool result = false;
    if (count == 0u)
        return false;
    if (count < 32u)
        result = (value.x & ((1u << count) - 1u)) != 0u;
    else if (count == 32u)
        result = value.x != 0u;
    else if (count < 64u)
        result = value.x != 0u ||
            (value.y & ((1u << (count - 32u)) - 1u)) != 0u;
    else
        result = any(value != 0u);
    return result;
}

bool SigmaU64Bit(uint2 value, uint bit)
{
    return bit < 32u ? ((value.x >> bit) & 1u) != 0u :
        ((value.y >> (bit - 32u)) & 1u) != 0u;
}

bool SigmaMagnitudeFitsSigned(uint2 magnitude, bool negative)
{
    uint2 limit = negative ? uint2(0u, 0x80000000u) :
        uint2(0xffffffffu, 0x7fffffffu);
    return !SigmaU64Less(limit, magnitude);
}

uint2 SigmaApplyMagnitudeSign(uint2 magnitude, bool negative, inout uint valid)
{
    if (!SigmaMagnitudeFitsSigned(magnitude, negative))
        valid = 0u;
    return negative ? SigmaU64NegateRaw(magnitude) : magnitude;
}

uint2 SigmaQ48Mask(bool predicate)
{
    return predicate ? uint2(0xffffffffu, 0xffffffffu) : uint2(0u, 0u);
}

uint2 SigmaQ48Select(bool predicate, uint2 whenTrue, uint2 whenFalse)
{
    uint mask = predicate ? 0xffffffffu : 0u;
    return (whenTrue & mask) | (whenFalse & ~mask);
}

uint2 SigmaQ48AddChecked(uint2 a, uint2 b, inout uint valid)
{
    uint carry;
    uint2 result = SigmaU64Add(a, b, carry);
    bool signA = (a.y & 0x80000000u) != 0u;
    bool signB = (b.y & 0x80000000u) != 0u;
    bool signR = (result.y & 0x80000000u) != 0u;
    if (signA == signB && signA != signR)
        valid = 0u;
    return result;
}

uint2 SigmaQ48NegateChecked(uint2 value, inout uint valid)
{
    if (value.x == 0u && value.y == 0x80000000u)
        valid = 0u;
    return SigmaU64NegateRaw(value);
}

uint2 SigmaQ48SubChecked(uint2 a, uint2 b, inout uint valid)
{
    uint borrow;
    uint2 result = SigmaU64Subtract(a, b, borrow);
    bool signA = (a.y & 0x80000000u) != 0u;
    bool signB = (b.y & 0x80000000u) != 0u;
    bool signR = (result.y & 0x80000000u) != 0u;
    if (signA != signB && signA != signR)
        valid = 0u;
    return result;
}

uint2 SigmaQ48ShiftLeftChecked(uint2 value, uint count, inout uint valid)
{
    if (count >= 64u)
    {
        valid = 0u;
        return uint2(0u, 0u);
    }
    uint2 result = SigmaU64ShiftLeftRaw(value, count);
    if (!SigmaU64Equal(SigmaI64ShiftRightRaw(result, count), value))
        valid = 0u;
    return result;
}

uint2 SigmaQ48ShiftRightNearestEven(uint2 value, uint count, inout uint valid)
{
    if (count >= 64u)
    {
        valid = 0u;
        return uint2(0u, 0u);
    }
    if (count == 0u)
        return value;
    bool negative = (value.y & 0x80000000u) != 0u;
    uint2 magnitude = SigmaU64AbsSigned(value);
    uint2 quotient = SigmaU64ShiftRight(magnitude, count);
    bool half = SigmaU64Bit(magnitude, count - 1u);
    bool lower = SigmaU64AnyLowBits(magnitude, count - 1u);
    bool roundUp = half && (lower || (quotient.x & 1u) != 0u);
    if (roundUp)
    {
        uint carry;
        quotient = SigmaU64Increment(quotient, carry);
        if (carry != 0u)
            valid = 0u;
    }
    return SigmaApplyMagnitudeSign(quotient, negative, valid);
}

struct SigmaU128
{
    uint4 word;
};

// Exact 32x32 -> 64 packed multiply. Unity 6's Vulkan HLSL frontend does
// not expose SM5 umulExtended even though SPIR-V has OpUMulExtended. Keep the
// operation backend-independent and exact with four scalar 16x16 products and
// a fixed carry network. There are no indexed limb arrays or data-dependent
// loops, and both value and checked-validity behaviour match the semantic path.
uint2 SigmaU32MultiplyWide(uint a, uint b)
{
    uint a0 = a & 0xffffu;
    uint a1 = a >> 16u;
    uint b0 = b & 0xffffu;
    uint b1 = b >> 16u;
    uint p00 = a0 * b0;
    uint p01 = a0 * b1;
    uint p10 = a1 * b0;
    uint p11 = a1 * b1;
    uint middle = (p00 >> 16u) + (p01 & 0xffffu) + (p10 & 0xffffu);
    uint low = (p00 & 0xffffu) | (middle << 16u);
    uint high = p11 + (p01 >> 16u) + (p10 >> 16u) + (middle >> 16u);
    return uint2(low, high);
}

SigmaU128 SigmaU64MultiplyWide(uint2 a, uint2 b)
{
    uint2 p00 = SigmaU32MultiplyWide(a.x, b.x);
    uint2 p01 = SigmaU32MultiplyWide(a.x, b.y);
    uint2 p10 = SigmaU32MultiplyWide(a.y, b.x);
    uint2 p11 = SigmaU32MultiplyWide(a.y, b.y);
    uint p00Lo = p00.x, p00Hi = p00.y;
    uint p01Lo = p01.x, p01Hi = p01.y;
    uint p10Lo = p10.x, p10Hi = p10.y;
    uint p11Lo = p11.x, p11Hi = p11.y;

    uint word1 = p00Hi;
    uint next = word1 + p01Lo;
    uint carry1 = next < word1 ? 1u : 0u;
    word1 = next;
    next = word1 + p10Lo;
    uint carry2 = next < word1 ? 1u : 0u;
    word1 = next;

    uint word2 = p01Hi;
    uint word3 = p11Hi;
    next = word2 + p10Hi;
    word3 += next < word2 ? 1u : 0u;
    word2 = next;
    next = word2 + p11Lo;
    word3 += next < word2 ? 1u : 0u;
    word2 = next;
    uint carry12 = carry1 + carry2;
    next = word2 + carry12;
    word3 += next < word2 ? 1u : 0u;
    word2 = next;

    SigmaU128 wideResult;
    wideResult.word = uint4(p00Lo, word1, word2, word3);
    return wideResult;
}

// mode: 0 nearest-even, 1 floor, 2 ceiling.
uint2 SigmaQ48MulRounded(uint2 a, uint2 b, uint mode, inout uint valid)
{
    bool negative = ((a.y ^ b.y) & 0x80000000u) != 0u;
    SigmaU128 wide = SigmaU64MultiplyWide(
        SigmaU64AbsSigned(a), SigmaU64AbsSigned(b));
    uint2 quotient = uint2((wide.word.y >> 16u) | (wide.word.z << 16u),
        (wide.word.z >> 16u) | (wide.word.w << 16u));
    uint extra = wide.word.w >> 16u;
    bool remainder = wide.word.x != 0u || (wide.word.y & 0xffffu) != 0u;
    bool increment = false;
    if (mode == 0u)
    {
        bool half = (wide.word.y & 0x8000u) != 0u;
        bool lower = wide.word.x != 0u || (wide.word.y & 0x7fffu) != 0u;
        increment = half && (lower || (quotient.x & 1u) != 0u);
    }
    else if (mode == 1u)
        increment = negative && remainder;
    else
        increment = !negative && remainder;
    if (increment)
    {
        uint carry;
        quotient = SigmaU64Increment(quotient, carry);
        extra += carry;
    }
    if (extra != 0u)
        valid = 0u;
    return SigmaApplyMagnitudeSign(quotient, negative, valid);
}

uint2 SigmaQ48MulNearestEven(uint2 a, uint2 b, inout uint valid)
{
    return SigmaQ48MulRounded(a, b, 0u, valid);
}

uint2 SigmaQ48MulLower(uint2 a, uint2 b, inout uint valid)
{
    return SigmaQ48MulRounded(a, b, 1u, valid);
}

uint2 SigmaQ48MulUpper(uint2 a, uint2 b, inout uint valid)
{
    return SigmaQ48MulRounded(a, b, 2u, valid);
}

// Q16.48 division by a signed power-of-two denominator is only a checked
// shift. Geometry information mass is deliberately quantized on this exact
// ladder, so the canonical inverse hot path does not execute software 128/64
// long division for every active carrier sample. The generic path below
// remains the semantic fallback for genuinely non-dyadic operands.
bool SigmaQ48TryDivDyadic(uint2 a, uint2 b, uint mode,
    out uint2 result, inout uint valid)
{
    result = uint2(0u, 0u);
    uint2 denominator = SigmaU64AbsSigned(b);
    bool lowPower = denominator.y == 0u && denominator.x != 0u &&
        (denominator.x & (denominator.x - 1u)) == 0u;
    bool highPower = denominator.x == 0u && denominator.y != 0u &&
        (denominator.y & (denominator.y - 1u)) == 0u;
    if (!lowPower && !highPower)
    {
        return false;
    }

    uint exponent = lowPower ? (uint)firstbitlow(denominator.x) :
        32u + (uint)firstbitlow(denominator.y);
    int shift = 48 - (int)exponent;
    bool negative = ((a.y ^ b.y) & 0x80000000u) != 0u;
    uint2 magnitude = SigmaU64AbsSigned(a);
    uint2 quotient = uint2(0u, 0u);
    if (shift >= 0)
    {
        uint count = (uint)shift;
        quotient = SigmaU64ShiftLeftRaw(magnitude, count);
        if (count >= 64u ||
            !SigmaU64Equal(SigmaU64ShiftRight(quotient, count), magnitude))
            valid = 0u;
    }
    else
    {
        uint count = (uint)(-shift);
        quotient = SigmaU64ShiftRight(magnitude, count);
        bool hasRemainder = SigmaU64AnyLowBits(magnitude, count);
        bool increment = false;
        if (mode == 0u && hasRemainder)
        {
            bool half = SigmaU64Bit(magnitude, count - 1u);
            bool lower = SigmaU64AnyLowBits(magnitude, count - 1u);
            increment = half && (lower || (quotient.x & 1u) != 0u);
        }
        else if (mode == 1u)
            increment = negative && hasRemainder;
        else if (mode == 2u)
            increment = !negative && hasRemainder;
        if (increment)
        {
            uint carry;
            quotient = SigmaU64Increment(quotient, carry);
            if (carry != 0u)
                valid = 0u;
        }
    }
    result = SigmaApplyMagnitudeSign(quotient, negative, valid);
    return true;
}

// mode: 0 nearest-even, 1 floor, 2 ceiling.
uint2 SigmaQ48DivRounded(uint2 a, uint2 b, uint mode, inout uint valid)
{
    uint2 denominator = SigmaU64AbsSigned(b);
    if (all(denominator == 0u))
    {
        valid = 0u;
        return uint2(0u, 0u);
    }
    uint2 dyadicResult = uint2(0u, 0u);
    if (SigmaQ48TryDivDyadic(a, b, mode, dyadicResult, valid))
        return dyadicResult;
    bool negative = ((a.y ^ b.y) & 0x80000000u) != 0u;
    uint2 numeratorMagnitude = SigmaU64AbsSigned(a);
    uint2 quotient64 = uint2(0u, 0u);
    uint2 remainder = uint2(0u, 0u);
    uint remainderExtra = 0u;
    bool quotientOverflow = false;

    // numerator = |a| << 48. Seed restoring division from the aligned
    // high prefix, then emit only quotient bits. Every valid Q16.48 quotient
    // has at most 65 candidate bits, so the normal path executes <= 65
    // iterations instead of walking the old fixed 128-bit numerator. Invalid
    // gross-overflow inputs still run the exact longer path so diagnostics keep
    // the same low quotient/remainder as the semantic reference.
    if (all(numeratorMagnitude == 0u))
        return uint2(0u, 0u);
    int magnitudeTop = numeratorMagnitude.y != 0u
        ? 32 + firstbithigh(numeratorMagnitude.y)
        : firstbithigh(numeratorMagnitude.x);
    int denominatorTop = denominator.y != 0u
        ? 32 + firstbithigh(denominator.y)
        : firstbithigh(denominator.x);
    int numeratorTop = magnitudeTop + 48;
    int quotientTop = numeratorTop - denominatorTop;

    if (quotientTop < 0)
    {
        // numerator < denominator, therefore the shifted numerator fits 64 bit.
        remainder = SigmaU64ShiftLeftRaw(numeratorMagnitude, 48u);
    }
    else
    {
        int seedShift = 48 - quotientTop;
        remainder = seedShift >= 0
            ? SigmaU64ShiftLeftRaw(numeratorMagnitude, (uint)seedShift)
            : SigmaU64ShiftRight(numeratorMagnitude, (uint)(-seedShift));

        bool subtractSeed = !SigmaU64Less(remainder, denominator);
        if (subtractSeed)
        {
            uint borrow;
            remainder = SigmaU64Subtract(remainder, denominator, borrow);
            if (quotientTop >= 64)
                quotientOverflow = true;
            else if (quotientTop < 32)
                quotient64.x |= 1u << (uint)quotientTop;
            else
                quotient64.y |= 1u << (uint)(quotientTop - 32);
        }

        [loop]
        for (int bit = quotientTop - 1; bit >= 0; --bit)
        {
            uint sourceBit = bit >= 48
                ? (SigmaU64Bit(numeratorMagnitude, (uint)(bit - 48))
                    ? 1u : 0u)
                : 0u;
            remainderExtra = remainder.y >> 31u;
            remainder.y = (remainder.y << 1u) | (remainder.x >> 31u);
            remainder.x = (remainder.x << 1u) | sourceBit;
            bool subtract = remainderExtra != 0u ||
                !SigmaU64Less(remainder, denominator);
            if (!subtract)
                continue;
            uint borrow;
            remainder = SigmaU64Subtract(remainder, denominator, borrow);
            remainderExtra -= borrow;
            if (bit >= 64)
                quotientOverflow = true;
            else if (bit < 32)
                quotient64.x |= 1u << (uint)bit;
            else
                quotient64.y |= 1u << (uint)(bit - 32);
        }
    }

    if (quotientOverflow)
        valid = 0u;
    bool hasRemainder = any(remainder != 0u);
    bool increment = false;
    if (mode == 0u && hasRemainder)
    {
        uint borrow;
        uint2 complement = SigmaU64Subtract(denominator, remainder, borrow);
        bool greaterHalf = SigmaU64Less(complement, remainder);
        bool exactHalf = SigmaU64Equal(complement, remainder);
        increment = greaterHalf || (exactHalf && (quotient64.x & 1u) != 0u);
    }
    else if (mode == 1u)
        increment = negative && hasRemainder;
    else if (mode == 2u)
        increment = !negative && hasRemainder;
    if (increment)
    {
        uint carry;
        quotient64 = SigmaU64Increment(quotient64, carry);
        if (carry != 0u)
            valid = 0u;
    }
    return SigmaApplyMagnitudeSign(quotient64, negative, valid);
}

uint2 SigmaQ48DivNearestEven(uint2 a, uint2 b, inout uint valid)
{
    return SigmaQ48DivRounded(a, b, 0u, valid);
}

uint2 SigmaQ48DivLower(uint2 a, uint2 b, inout uint valid)
{
    return SigmaQ48DivRounded(a, b, 1u, valid);
}

uint2 SigmaQ48DivUpper(uint2 a, uint2 b, inout uint valid)
{
    return SigmaQ48DivRounded(a, b, 2u, valid);
}

void SigmaLeftBasisAction(uint basis, uint2 inputState[16],
    out uint2 outputState[16], inout uint valid)
{
    [unroll]
    for (uint outputLane = 0u; outputLane < 16u; ++outputLane)
    {
        uint source = basis ^ outputLane;
        uint2 value = inputState[source];
        outputState[outputLane] = SigmaMulBasisSign(basis, source) < 0 ?
            SigmaQ48NegateChecked(value, valid) : value;
    }
}

void SigmaRightBasisAction(uint2 inputState[16], uint basis,
    out uint2 outputState[16], inout uint valid)
{
    [unroll]
    for (uint outputLane = 0u; outputLane < 16u; ++outputLane)
    {
        uint source = basis ^ outputLane;
        uint2 value = inputState[source];
        outputState[outputLane] = SigmaMulBasisSign(source, basis) < 0 ?
            SigmaQ48NegateChecked(value, valid) : value;
    }
}

void SigmaRightSignedDyadAction(uint2 inputState[16], int4 dyad,
    out uint2 outputState[16], inout uint valid)
{
    uint2 first[16];
    uint2 second[16];
    SigmaRightBasisAction(inputState, (uint)dyad.x, first, valid);
    SigmaRightBasisAction(inputState, (uint)dyad.z, second, valid);
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
    {
        uint2 a = dyad.y < 0 ? SigmaQ48NegateChecked(first[lane], valid) : first[lane];
        uint2 b = dyad.w < 0 ? SigmaQ48NegateChecked(second[lane], valid) : second[lane];
        outputState[lane] = SigmaQ48AddChecked(a, b, valid);
    }
}

uint2 SigmaHadamardRow(uint2 state[16], uint row, inout uint valid)
{
    uint2 sum = uint2(0u, 0u);
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
    {
        uint2 value = SigmaHadamardSign(row, lane) < 0 ?
            SigmaQ48NegateChecked(state[lane], valid) : state[lane];
        sum = SigmaQ48AddChecked(sum, value, valid);
    }
    return sum;
}

#endif
