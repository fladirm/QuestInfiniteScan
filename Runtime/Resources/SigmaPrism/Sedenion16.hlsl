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
        result = value;
    else if (count < 32u)
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
        result = value;
    else if (count < 32u)
        result = uint2(value.x << count,
            (value.y << count) | (value.x >> (32u - count)));
    else if (count < 64u)
        result = uint2(0u, value.x << (count - 32u));
    return result;
}

uint2 SigmaI64ShiftRightRaw(uint2 value, uint count)
{
    uint sign = (value.y & 0x80000000u) != 0u ? 0xffffffffu : 0u;
    uint2 result = uint2(sign, sign);
    if (count == 0u)
        result = value;
    else if (count < 32u)
        result = uint2((value.x >> count) | (value.y << (32u - count)),
            asuint(asint(value.y) >> count));
    else if (count < 64u)
        result = uint2(asuint(asint(value.y) >> (count - 32u)), sign);
    return result;
}

bool SigmaU64AnyLowBits(uint2 value, uint count)
{
    bool result = false;
    if (count > 0u && count < 32u)
        result = (value.x & ((1u << count) - 1u)) != 0u;
    else if (count == 32u)
        result = value.x != 0u;
    else if (count < 64u && count > 32u)
        result = value.x != 0u ||
            (value.y & ((1u << (count - 32u)) - 1u)) != 0u;
    else if (count >= 64u)
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
    uint2 result = uint2(0u, 0u);
    if (count >= 64u)
        valid = 0u;
    else
    {
        result = SigmaU64ShiftLeftRaw(value, count);
        if (!SigmaU64Equal(SigmaI64ShiftRightRaw(result, count), value))
            valid = 0u;
    }
    return result;
}

uint2 SigmaQ48ShiftRightNearestEven(uint2 value, uint count, inout uint valid)
{
    uint2 result = value;
    if (count >= 64u)
    {
        valid = 0u;
        result = uint2(0u, 0u);
    }
    else if (count != 0u)
    {
        bool negative = (value.y & 0x80000000u) != 0u;
        uint2 magnitude = SigmaU64AbsSigned(value);
        uint2 quotient = SigmaU64ShiftRight(magnitude, count);
        bool half = SigmaU64Bit(magnitude, count - 1u);
        bool lower = SigmaU64AnyLowBits(magnitude, count - 1u);
        bool roundUp = half && (lower || (quotient.x & 1u) != 0u);
        if (roundUp)
        {
            uint carry = 0u;
            quotient = SigmaU64Increment(quotient, carry);
            if (carry != 0u)
                valid = 0u;
        }
        result = SigmaApplyMagnitudeSign(quotient, negative, valid);
    }
    return result;
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

// Both outward endpoints share one exact 64x64->128 product.  Boundary
// envelopes need the pair together; computing floor and ceiling separately
// would duplicate the packed multiply on every finite-footprint corner.
void SigmaQ48MulBounds(uint2 a, uint2 b, out uint2 lower, out uint2 upper,
    inout uint valid)
{
    bool negative = ((a.y ^ b.y) & 0x80000000u) != 0u;
    SigmaU128 wide = SigmaU64MultiplyWide(
        SigmaU64AbsSigned(a), SigmaU64AbsSigned(b));
    uint2 quotient = uint2((wide.word.y >> 16u) | (wide.word.z << 16u),
        (wide.word.z >> 16u) | (wide.word.w << 16u));
    uint extra = wide.word.w >> 16u;
    bool remainder = wide.word.x != 0u || (wide.word.y & 0xffffu) != 0u;
    uint2 outwardMagnitude = quotient;
    if (remainder)
    {
        uint carry;
        outwardMagnitude = SigmaU64Increment(outwardMagnitude, carry);
        extra += carry;
    }
    if (extra != 0u)
        valid = 0u;
    if (negative)
    {
        lower = SigmaApplyMagnitudeSign(outwardMagnitude, true, valid);
        upper = SigmaApplyMagnitudeSign(quotient, true, valid);
    }
    else
    {
        lower = SigmaApplyMagnitudeSign(quotient, false, valid);
        upper = SigmaApplyMagnitudeSign(outwardMagnitude, false, valid);
    }
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
    bool handled = lowPower || highPower;
    if (handled)
    {
        uint exponent = lowPower ? (uint)firstbitlow(denominator.x) :
            32u + (uint)firstbitlow(denominator.y);
        int shift = 48 - (int)exponent;
        bool negative = ((a.y ^ b.y) & 0x80000000u) != 0u;
        uint2 magnitude = SigmaU64AbsSigned(a);
        uint2 quotient = uint2(0u, 0u);
        if (shift >= 0)
        {
            uint count = (uint)shift;
            if (count >= 64u)
                valid = 0u;
            else
                quotient = SigmaU64ShiftLeftRaw(magnitude, count);
            if (count < 64u &&
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
                uint carry = 0u;
                quotient = SigmaU64Increment(quotient, carry);
                if (carry != 0u)
                    valid = 0u;
            }
        }
        result = SigmaApplyMagnitudeSign(quotient, negative, valid);
    }
    return handled;
}

// Exact 64/32 division with high<divisor.  This is Hacker's Delight divlu
// circuit lowered to two base-2^16 quotient digits.  All schedules are fixed;
// the unsigned wrap in un21 is intentional and the returned remainder is exact.
uint SigmaU64DivideByU32(uint high, uint low, uint divisor,
    out uint remainder)
{
    uint shift = 31u - (uint)firstbithigh(divisor);
    uint normalizedDivisor = divisor << shift;
    uint highPrefix = high;
    uint lowShifted = low;
    if (shift != 0u)
    {
        highPrefix = (high << shift) | (low >> (32u - shift));
        lowShifted = low << shift;
    }

    uint divisorHigh = normalizedDivisor >> 16u;
    uint divisorLow = normalizedDivisor & 0xffffu;
    uint numeratorLowHigh = lowShifted >> 16u;
    uint numeratorLowLow = lowShifted & 0xffffu;

    uint quotientHigh = highPrefix / divisorHigh;
    uint trialRemainder = highPrefix - quotientHigh * divisorHigh;
    [unroll]
    for (uint lowCorrection = 0u; lowCorrection < 2u; ++lowCorrection)
    {
        if (quotientHigh < 0x10000u &&
            quotientHigh * divisorLow <=
                0x10000u * trialRemainder + numeratorLowHigh)
            break;
        --quotientHigh;
        trialRemainder += divisorHigh;
        if (trialRemainder >= 0x10000u)
            break;
    }

    uint un21 = highPrefix * 0x10000u + numeratorLowHigh -
        quotientHigh * normalizedDivisor;
    uint quotientLow = un21 / divisorHigh;
    trialRemainder = un21 - quotientLow * divisorHigh;
    [unroll]
    for (uint correction = 0u; correction < 2u; ++correction)
    {
        if (quotientLow < 0x10000u &&
            quotientLow * divisorLow <=
                0x10000u * trialRemainder + numeratorLowLow)
            break;
        --quotientLow;
        trialRemainder += divisorHigh;
        if (trialRemainder >= 0x10000u)
            break;
    }

    uint normalizedRemainder = un21 * 0x10000u + numeratorLowLow -
        quotientLow * normalizedDivisor;
    remainder = normalizedRemainder >> shift;
    return quotientHigh * 0x10000u + quotientLow;
}

// One Knuth-D base-2^32 quotient digit for a three-word numerator and a
// normalized two-word divisor.  The estimate needs at most two corrections;
// the final add-back preserves the exact quotient even at the limb boundaries.
uint SigmaU96DivideStep(inout uint low, inout uint middle, inout uint high,
    uint divisorLow, uint divisorHigh)
{
    uint quotient;
    uint trialRemainder;
    bool remainderOverflow;
    if (high == divisorHigh)
    {
        quotient = 0xffffffffu;
        trialRemainder = middle + divisorHigh;
        remainderOverflow = trialRemainder < middle;
    }
    else
    {
        quotient = SigmaU64DivideByU32(high, middle, divisorHigh,
            trialRemainder);
        remainderOverflow = false;
    }

    [unroll]
    for (uint correction = 0u; correction < 2u; ++correction)
    {
        uint2 product = SigmaU32MultiplyWide(quotient, divisorLow);
        if (remainderOverflow ||
            !SigmaU64Less(uint2(low, trialRemainder), product))
            break;
        --quotient;
        uint previous = trialRemainder;
        trialRemainder += divisorHigh;
        remainderOverflow = trialRemainder < previous;
    }

    uint2 productLow = SigmaU32MultiplyWide(quotient, divisorLow);
    uint2 productHigh = SigmaU32MultiplyWide(quotient, divisorHigh);
    uint productMiddle = productLow.y + productHigh.x;
    uint productCarry = productMiddle < productLow.y ? 1u : 0u;
    uint productTop = productHigh.y + productCarry;

    uint nextLow = low - productLow.x;
    uint borrow = low < productLow.x ? 1u : 0u;
    uint middleSubtrahend = productMiddle + borrow;
    uint middleCarry = middleSubtrahend < productMiddle ? 1u : 0u;
    uint nextMiddle = middle - middleSubtrahend;
    uint middleBorrow = middleCarry != 0u || middle < middleSubtrahend
        ? 1u : 0u;
    uint highSubtrahend = productTop + middleBorrow;
    uint highCarry = highSubtrahend < productTop ? 1u : 0u;
    uint nextHigh = high - highSubtrahend;
    bool underflow = highCarry != 0u || high < highSubtrahend;

    if (underflow)
    {
        --quotient;
        uint restoredLow = nextLow + divisorLow;
        uint carry = restoredLow < nextLow ? 1u : 0u;
        uint restoredMiddle = nextMiddle + divisorHigh;
        uint carryMiddle = restoredMiddle < nextMiddle ? 1u : 0u;
        uint restoredMiddleWithCarry = restoredMiddle + carry;
        carryMiddle |= restoredMiddleWithCarry < restoredMiddle ? 1u : 0u;
        nextLow = restoredLow;
        nextMiddle = restoredMiddleWithCarry;
        nextHigh += carryMiddle;
    }

    low = nextLow;
    middle = nextMiddle;
    high = nextHigh;
    return quotient;
}

// Divide the exact 112-bit Q48 numerator |a|<<48 by a 64-bit denominator.
// The one-word and two-word divisor paths are fixed packed-limb circuits; no
// restoring bit loop or data-dependent trip count remains in the live ALU.
uint2 SigmaQ48DivideMagnitude(uint2 numeratorMagnitude, uint2 denominator,
    out uint2 remainder, out bool quotientOverflow)
{
    uint2 quotientResult = uint2(0u, 0u);
    remainder = uint2(0u, 0u);
    quotientOverflow = false;
    uint numerator0 = 0u;
    uint numerator1 = numeratorMagnitude.x << 16u;
    uint numerator2 = (numeratorMagnitude.x >> 16u) |
        (numeratorMagnitude.y << 16u);
    uint numerator3 = numeratorMagnitude.y >> 16u;

    if (denominator.y == 0u)
    {
        uint remainder3;
        uint quotient3 = SigmaU64DivideByU32(0u, numerator3,
            denominator.x, remainder3);
        uint remainder2;
        uint quotient2 = SigmaU64DivideByU32(remainder3, numerator2,
            denominator.x, remainder2);
        uint remainder1;
        uint quotient1 = SigmaU64DivideByU32(remainder2, numerator1,
            denominator.x, remainder1);
        uint remainder0;
        uint quotient0 = SigmaU64DivideByU32(remainder1, numerator0,
            denominator.x, remainder0);
        remainder = uint2(remainder0, 0u);
        quotientOverflow = quotient3 != 0u || quotient2 != 0u;
        quotientResult = uint2(quotient0, quotient1);
    }
    else
    {
        uint shift = 31u - (uint)firstbithigh(denominator.y);
        uint divisorLow = denominator.x;
        uint divisorHigh = denominator.y;
        uint u0 = numerator0;
        uint u1 = numerator1;
        uint u2 = numerator2;
        uint u3 = numerator3;
        uint u4 = 0u;
        if (shift != 0u)
        {
            divisorHigh = (denominator.y << shift) |
                (denominator.x >> (32u - shift));
            divisorLow = denominator.x << shift;
            u4 = numerator3 >> (32u - shift);
            u3 = (numerator3 << shift) | (numerator2 >> (32u - shift));
            u2 = (numerator2 << shift) | (numerator1 >> (32u - shift));
            u1 = (numerator1 << shift) | (numerator0 >> (32u - shift));
            u0 = numerator0 << shift;
        }

        uint quotient2 = SigmaU96DivideStep(u2, u3, u4,
            divisorLow, divisorHigh);
        uint quotient1 = SigmaU96DivideStep(u1, u2, u3,
            divisorLow, divisorHigh);
        uint quotient0 = SigmaU96DivideStep(u0, u1, u2,
            divisorLow, divisorHigh);
        remainder = shift == 0u ? uint2(u0, u1) :
            SigmaU64ShiftRight(uint2(u0, u1), shift);
        quotientOverflow = quotient2 != 0u;
        quotientResult = uint2(quotient0, quotient1);
    }
    return quotientResult;
}

// mode: 0 nearest-even, 1 floor, 2 ceiling.
uint2 SigmaQ48DivRounded(uint2 a, uint2 b, uint mode, inout uint valid)
{
    uint2 result = uint2(0u, 0u);
    uint2 denominator = SigmaU64AbsSigned(b);
    if (all(denominator == 0u))
    {
        valid = 0u;
    }
    else
    {
        uint2 dyadicResult = uint2(0u, 0u);
        bool dyadic = SigmaQ48TryDivDyadic(a, b, mode, dyadicResult, valid);
        if (dyadic)
        {
            result = dyadicResult;
        }
        else
        {
            bool negative = ((a.y ^ b.y) & 0x80000000u) != 0u;
            uint2 remainder = uint2(0u, 0u);
            bool quotientOverflow = false;
            uint2 quotient64 = SigmaQ48DivideMagnitude(SigmaU64AbsSigned(a),
                denominator, remainder, quotientOverflow);
            if (quotientOverflow)
                valid = 0u;
            bool hasRemainder = any(remainder != 0u);
            bool increment = false;
            if (mode == 0u && hasRemainder)
            {
                uint borrow;
                uint2 complement = SigmaU64Subtract(denominator, remainder,
                    borrow);
                bool greaterHalf = SigmaU64Less(complement, remainder);
                bool exactHalf = SigmaU64Equal(complement, remainder);
                increment = greaterHalf ||
                    (exactHalf && (quotient64.x & 1u) != 0u);
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
            result = SigmaApplyMagnitudeSign(quotient64, negative, valid);
        }
    }
    return result;
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
