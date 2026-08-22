#ifndef SIGMA_SEDENION16_INCLUDED
#define SIGMA_SEDENION16_INCLUDED

#include "Generated/SigmaGeneratedTables.hlsl"

// Exact signed Q16.48 storage lowering. x is the least-significant limb; y is
// the signed most-significant limb. All mutation callers carry an explicit
// validity bit so checked overflow fails closed instead of saturating.

bool SigmaU64Equal(uint2 a, uint2 b)
{
    return all(a == b);
}

bool SigmaU64Less(uint2 a, uint2 b)
{
    return a.y < b.y || (a.y == b.y && a.x < b.x);
}

bool SigmaI64Less(uint2 a, uint2 b)
{
    int ah = asint(a.y);
    int bh = asint(b.y);
    return ah < bh || (ah == bh && a.x < b.x);
}

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
    if (count == 0u)
        return value;
    if (count < 32u)
        return uint2((value.x >> count) | (value.y << (32u - count)),
            value.y >> count);
    if (count < 64u)
        return uint2(value.y >> (count - 32u), 0u);
    return uint2(0u, 0u);
}

uint2 SigmaU64ShiftLeftRaw(uint2 value, uint count)
{
    if (count == 0u)
        return value;
    if (count < 32u)
        return uint2(value.x << count,
            (value.y << count) | (value.x >> (32u - count)));
    if (count < 64u)
        return uint2(0u, value.x << (count - 32u));
    return uint2(0u, 0u);
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
    if (count == 0u)
        return false;
    if (count < 32u)
        return (value.x & ((1u << count) - 1u)) != 0u;
    if (count == 32u)
        return value.x != 0u;
    if (count < 64u)
        return value.x != 0u ||
            (value.y & ((1u << (count - 32u)) - 1u)) != 0u;
    return any(value != 0u);
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

bool SigmaQ48Less(uint2 a, uint2 b)
{
    return SigmaI64Less(a, b);
}

uint2 SigmaQ48Min(uint2 a, uint2 b)
{
    return SigmaI64Less(a, b) ? a : b;
}

uint2 SigmaQ48Max(uint2 a, uint2 b)
{
    return SigmaI64Less(a, b) ? b : a;
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

SigmaU128 SigmaU64MultiplyWide(uint2 a, uint2 b)
{
    // Base-2^16 schoolbook is confined to this proven generic qmul primitive.
    // Every partial sum is <= 0xffffffff, so no hidden 32-bit overflow occurs.
    uint left[4];
    uint right[4];
    uint digits[8];
    left[0] = a.x & 0xffffu;
    left[1] = a.x >> 16u;
    left[2] = a.y & 0xffffu;
    left[3] = a.y >> 16u;
    right[0] = b.x & 0xffffu;
    right[1] = b.x >> 16u;
    right[2] = b.y & 0xffffu;
    right[3] = b.y >> 16u;
    [unroll]
    for (uint index = 0u; index < 8u; ++index)
        digits[index] = 0u;
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        uint carry = 0u;
        [unroll]
        for (uint j = 0u; j < 4u; ++j)
        {
            uint target = i + j;
            uint sum = digits[target] + left[i] * right[j] + carry;
            digits[target] = sum & 0xffffu;
            carry = sum >> 16u;
        }
        [unroll]
        for (uint target = i + 4u; target < 8u; ++target)
        {
            uint sum = digits[target] + carry;
            digits[target] = sum & 0xffffu;
            carry = sum >> 16u;
        }
    }
    SigmaU128 wideResult;
    wideResult.word = uint4(
        digits[0] | (digits[1] << 16u),
        digits[2] | (digits[3] << 16u),
        digits[4] | (digits[5] << 16u),
        digits[6] | (digits[7] << 16u));
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

uint SigmaU128Bit(uint4 value, uint bit)
{
    uint word = bit >> 5u;
    uint shift = bit & 31u;
    if (word == 0u) return (value.x >> shift) & 1u;
    if (word == 1u) return (value.y >> shift) & 1u;
    if (word == 2u) return (value.z >> shift) & 1u;
    return (value.w >> shift) & 1u;
}

void SigmaU128SetBit(inout uint4 value, uint bit)
{
    uint mask = 1u << (bit & 31u);
    uint word = bit >> 5u;
    if (word == 0u) value.x |= mask;
    else if (word == 1u) value.y |= mask;
    else if (word == 2u) value.z |= mask;
    else value.w |= mask;
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
    bool negative = ((a.y ^ b.y) & 0x80000000u) != 0u;
    uint2 numeratorMagnitude = SigmaU64AbsSigned(a);
    uint4 numerator = uint4(0u, numeratorMagnitude.x << 16u,
        (numeratorMagnitude.x >> 16u) | (numeratorMagnitude.y << 16u),
        numeratorMagnitude.y >> 16u);
    uint4 quotient = uint4(0u, 0u, 0u, 0u);
    uint2 remainder = uint2(0u, 0u);
    uint remainderExtra = 0u;

    [unroll(4)]
    for (int block = 3; block >= 0; --block)
    {
        [unroll(32)]
        for (int localBit = 31; localBit >= 0; --localBit)
        {
            uint bit = (uint)(block * 32 + localBit);
            remainderExtra = remainder.y >> 31u;
            remainder.y = (remainder.y << 1u) | (remainder.x >> 31u);
            remainder.x = (remainder.x << 1u) | SigmaU128Bit(numerator, bit);
            bool subtract = remainderExtra != 0u ||
                !SigmaU64Less(remainder, denominator);
            if (subtract)
            {
                uint borrow;
                remainder = SigmaU64Subtract(remainder, denominator, borrow);
                remainderExtra -= borrow;
                SigmaU128SetBit(quotient, bit);
            }
        }
    }

    if (quotient.z != 0u || quotient.w != 0u)
        valid = 0u;
    uint2 quotient64 = quotient.xy;
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
