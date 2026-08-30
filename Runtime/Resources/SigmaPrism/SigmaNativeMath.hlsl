#ifndef SIGMA_NATIVE_MATH_INCLUDED
#define SIGMA_NATIVE_MATH_INCLUDED

// N3R exact lowering. The generated native relation descriptors remain the
// authority; these fixed schedules execute their full-S16 primitives on Vulkan.
// Kernel-local defines prevent the complete relation circuit from being cloned
// into unrelated support/query/reduce variants.
#if defined(SIGMA_NATIVE_ORACLE_RELATION_MATH)
static const uint4 SIGMA_U128_ZERO = uint4(0u, 0u, 0u, 0u);
static const uint4 SIGMA_U128_MAX =
    uint4(0xffffffffu, 0xffffffffu, 0xffffffffu, 0xffffffffu);

bool SigmaU128Equal(uint4 left, uint4 right)
{
    return all(left == right);
}

uint4 SigmaU128Add64(uint4 value, uint2 addend, inout uint valid)
{
    uint previous = value.x;
    value.x += addend.x;
    uint carry = value.x < previous ? 1u : 0u;
    previous = value.y;
    value.y += addend.y;
    uint nextCarry = value.y < previous ? 1u : 0u;
    previous = value.y;
    value.y += carry;
    nextCarry |= value.y < previous ? 1u : 0u;
    previous = value.z;
    value.z += nextCarry;
    carry = value.z < previous ? 1u : 0u;
    previous = value.w;
    value.w += carry;
    if (value.w < previous)
        valid = 0u;
    return value;
}
#endif

#define SIGMA_NATIVE_OPTICAL_CHANNELS 3u
#define SIGMA_NATIVE_FACTOR_INCOMPATIBLE 0u
#define SIGMA_NATIVE_FACTOR_EXACT_CLOSED 1u
#define SIGMA_NATIVE_FACTOR_UNRESOLVED 2u

#if defined(SIGMA_NATIVE_ORACLE_QUERY_MATH) || \
    defined(SIGMA_NATIVE_ORACLE_RELATION_MATH)
void SigmaNativeLoadState(uint stateOffset, out uint2 state[16])
{
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        state[lane] = _NativeStates[stateOffset + lane];
}

bool SigmaNativeArrayIsZero(uint2 state[16])
{
    uint aggregate = 0u;
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        aggregate |= state[lane].x | state[lane].y;
    return aggregate == 0u;
}

bool SigmaNativeStateIsZero(uint stateOffset)
{
    uint2 state[16];
    SigmaNativeLoadState(stateOffset, state);
    return SigmaNativeArrayIsZero(state);
}
#endif

#if defined(SIGMA_NATIVE_ORACLE_QUERY_MATH)
void SigmaNativeEvaluateStateAtRows(uint stateOffset, uint rowOffset,
    out uint2 order,
    out uint2 optical[3], inout uint valid)
{
    uint2 shadow[4];
    [unroll]
    for (uint shadowAxis = 0u; shadowAxis < 4u; ++shadowAxis)
    {
        uint2 sum = uint2(0u, 0u);
        [unroll]
        for (uint lane = 0u; lane < 16u; ++lane)
        {
            uint2 term = SigmaMerkabaMultiplyShadowCoefficient(
                _NativeStates[stateOffset + lane], lane, shadowAxis, valid);
            sum = SigmaQ48AddChecked(sum, term, valid);
        }
        shadow[shadowAxis] = sum;
    }
    order = uint2(0u, 0u);
    [unroll]
    for (uint channel = 0u; channel < SIGMA_NATIVE_OPTICAL_CHANNELS; ++channel)
        optical[channel] = uint2(0u, 0u);
    [unroll]
    for (uint contractionAxis = 0u; contractionAxis < 4u; ++contractionAxis)
    {
        order = SigmaQ48AddChecked(order,
            SigmaQ48MulNearestEven(shadow[contractionAxis],
                _NativeQueryRows[rowOffset + contractionAxis], valid),
            valid);
        [unroll]
        for (uint channel = 0u; channel < SIGMA_NATIVE_OPTICAL_CHANNELS; ++channel)
            optical[channel] = SigmaQ48AddChecked(optical[channel],
                SigmaQ48MulNearestEven(shadow[contractionAxis],
                    _NativeQueryRows[rowOffset + 4u + channel * 4u +
                        contractionAxis], valid),
                valid);
    }
}


void SigmaNativeEvaluateState(uint stateOffset, out uint2 order,
    out uint2 optical[3], inout uint valid)
{
    SigmaNativeEvaluateStateAtRows(stateOffset, 0u, order, optical, valid);
}
#endif

#if defined(SIGMA_NATIVE_ORACLE_QUERY_MATH) || \
    defined(SIGMA_NATIVE_ORACLE_RELATION_MATH) || \
    defined(SIGMA_NATIVE_FRESH_MATH)
bool SigmaNativePointInInterval(uint2 value, uint2 lower, uint2 upper)
{
    return !SigmaI64Less(value, lower) && !SigmaI64Less(upper, value);
}
#endif

#if defined(SIGMA_NATIVE_ORACLE_RELATION_MATH)
uint SigmaNativeAggregateFactorClass(uint left, uint middle, uint right)
{
    uint result = SIGMA_NATIVE_FACTOR_EXACT_CLOSED;
    if (left == SIGMA_NATIVE_FACTOR_INCOMPATIBLE ||
        middle == SIGMA_NATIVE_FACTOR_INCOMPATIBLE ||
        right == SIGMA_NATIVE_FACTOR_INCOMPATIBLE)
        result = SIGMA_NATIVE_FACTOR_INCOMPATIBLE;
    else if (left == SIGMA_NATIVE_FACTOR_UNRESOLVED ||
        middle == SIGMA_NATIVE_FACTOR_UNRESOLVED ||
        right == SIGMA_NATIVE_FACTOR_UNRESOLVED)
        result = SIGMA_NATIVE_FACTOR_UNRESOLVED;
    return result;
}

uint SigmaNativeHashS16(uint2 state[16])
{
    uint hash = 2166136261u;
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
    {
        hash = (hash ^ state[lane].x) * 16777619u;
        hash = (hash ^ state[lane].y) * 16777619u;
    }
    return hash;
}

// One workgroup owns one complete native relation. The 256 threads are the
// 16x16 signed-XOR product plane; no S16 lane, bracket factor or annihilator
// action becomes a dispatch. Product terms are evaluated in parallel and each
// output lane retains the canonical left-lane accumulation order.
#define SIGMA_NATIVE_PRODUCT_LEFT_RIGHT 0u
#define SIGMA_NATIVE_PRODUCT_RIGHT_CONTEXT 1u
#define SIGMA_NATIVE_PRODUCT_AB_CONTEXT 2u
#define SIGMA_NATIVE_PRODUCT_LEFT_BC 3u
#define SIGMA_NATIVE_PRODUCT_TRANSITION 4u

groupshared uint2 SigmaNativeGroupLeft[16];
groupshared uint2 SigmaNativeGroupRight[16];
groupshared uint2 SigmaNativeGroupContext[16];
groupshared uint2 SigmaNativeGroupAB[16];
groupshared uint2 SigmaNativeGroupBC[16];
groupshared uint2 SigmaNativeGroupLeftBracket[16];
groupshared uint2 SigmaNativeGroupRightBracket[16];
groupshared uint2 SigmaNativeGroupAssociator[16];
groupshared uint2 SigmaNativeGroupTransition[16];
groupshared uint2 SigmaNativeGroupLink[16];
groupshared uint2 SigmaNativeGroupPairTerms[256];
groupshared uint SigmaNativeGroupPairValid[256];
groupshared uint SigmaNativeGroupLaneValid[16];
groupshared uint SigmaNativeGroupAllValid;
groupshared uint SigmaNativeGroupAny[16];
groupshared uint4 SigmaNativeGroupNormLow[256];
groupshared uint4 SigmaNativeGroupNormHigh[256];
groupshared uint SigmaNativeGroupNormSign[256];
groupshared uint SigmaNativeGroupNormValid[256];
groupshared uint4 SigmaNativeGroupFactorNormLow[2];
groupshared uint4 SigmaNativeGroupFactorNormHigh[2];
groupshared uint SigmaNativeGroupFactorClass[2];
groupshared uint SigmaNativeGroupFactorKernel[2];
groupshared uint4 SigmaNativeGroupResiduals[256];
groupshared uint SigmaNativeGroupResidualActions[256];
groupshared uint SigmaNativeGroupResidualValid[256];

uint2 SigmaNativeGroupProductLeft(uint phase, uint lane, inout uint valid)
{
    uint2 result = SigmaNativeGroupLeft[lane];
    if (phase == SIGMA_NATIVE_PRODUCT_RIGHT_CONTEXT)
        result = SigmaNativeGroupRight[lane];
    else if (phase == SIGMA_NATIVE_PRODUCT_AB_CONTEXT)
        result = SigmaNativeGroupAB[lane];
    else if (phase == SIGMA_NATIVE_PRODUCT_TRANSITION && lane != 0u)
        result = SigmaQ48NegateChecked(SigmaNativeGroupLeft[lane], valid);
    return result;
}

uint2 SigmaNativeGroupProductRight(uint phase, uint lane)
{
    if (phase == SIGMA_NATIVE_PRODUCT_LEFT_RIGHT ||
        phase == SIGMA_NATIVE_PRODUCT_TRANSITION)
        return SigmaNativeGroupRight[lane];
    if (phase == SIGMA_NATIVE_PRODUCT_RIGHT_CONTEXT ||
        phase == SIGMA_NATIVE_PRODUCT_AB_CONTEXT)
        return SigmaNativeGroupContext[lane];
    return SigmaNativeGroupBC[lane];
}

void SigmaNativeGroupStoreProduct(uint phase, uint lane, uint2 value)
{
    if (phase == SIGMA_NATIVE_PRODUCT_LEFT_RIGHT)
        SigmaNativeGroupAB[lane] = value;
    else if (phase == SIGMA_NATIVE_PRODUCT_RIGHT_CONTEXT)
        SigmaNativeGroupBC[lane] = value;
    else if (phase == SIGMA_NATIVE_PRODUCT_AB_CONTEXT)
        SigmaNativeGroupLeftBracket[lane] = value;
    else if (phase == SIGMA_NATIVE_PRODUCT_LEFT_BC)
        SigmaNativeGroupRightBracket[lane] = value;
    else
        SigmaNativeGroupTransition[lane] = value;
}

void SigmaNativeGroupProduct(uint phase, uint threadIndex)
{
    uint leftLane = threadIndex >> 4u;
    uint rightLane = threadIndex & 15u;
    uint localValid = 1u;
    uint2 left = SigmaNativeGroupProductLeft(phase, leftLane, localValid);
    uint2 right = SigmaNativeGroupProductRight(phase, rightLane);
    uint2 term = SigmaQ48MulNearestEven(left, right, localValid);
    if (SigmaMulBasisSign(leftLane, rightLane) < 0)
        term = SigmaQ48NegateChecked(term, localValid);
    SigmaNativeGroupPairTerms[threadIndex] = term;
    SigmaNativeGroupPairValid[threadIndex] = localValid;
    GroupMemoryBarrierWithGroupSync();

    if (threadIndex < 16u)
    {
        uint outputLane = threadIndex;
        uint2 sum = uint2(0u, 0u);
        uint laneValid = 1u;
        [unroll]
        for (uint sourceLane = 0u; sourceLane < 16u; ++sourceLane)
        {
            uint sourceRight = sourceLane ^ outputLane;
            uint pairIndex = sourceLane * 16u + sourceRight;
            laneValid &= SigmaNativeGroupPairValid[pairIndex];
            sum = SigmaQ48AddChecked(sum,
                SigmaNativeGroupPairTerms[pairIndex], laneValid);
        }
        SigmaNativeGroupStoreProduct(phase, outputLane, sum);
        SigmaNativeGroupLaneValid[outputLane] = laneValid;
    }
    GroupMemoryBarrierWithGroupSync();
    if (threadIndex == 0u)
    {
        [unroll]
        for (uint outputLane = 0u; outputLane < 16u; ++outputLane)
            SigmaNativeGroupAllValid &= SigmaNativeGroupLaneValid[outputLane];
    }
    GroupMemoryBarrierWithGroupSync();
}

uint2 SigmaNativeGroupFactorValue(uint factorIndex, uint lane)
{
    return factorIndex == 0u ? SigmaNativeGroupLink[lane] :
        SigmaNativeGroupAssociator[lane];
}

uint4 SigmaNativeU128Subtract(uint4 left, uint4 right)
{
    uint borrow0 = left.x < right.x ? 1u : 0u;
    uint x = left.x - right.x;
    uint rightY = right.y + borrow0;
    uint carryY = rightY < right.y ? 1u : 0u;
    uint borrow1 = carryY != 0u || left.y < rightY ? 1u : 0u;
    uint y = left.y - rightY;
    uint rightZ = right.z + borrow1;
    uint carryZ = rightZ < right.z ? 1u : 0u;
    uint borrow2 = carryZ != 0u || left.z < rightZ ? 1u : 0u;
    uint z = left.z - rightZ;
    uint w = left.w - right.w - borrow2;
    return uint4(x, y, z, w);
}

uint4 SigmaNativeU128AddCarry(uint4 left, uint4 right, out uint carryOut)
{
    uint x = left.x + right.x;
    uint carry = x < left.x ? 1u : 0u;
    uint y0 = left.y + right.y;
    uint carryY = y0 < left.y ? 1u : 0u;
    uint y = y0 + carry;
    carryY |= y < y0 ? 1u : 0u;
    uint z0 = left.z + right.z;
    uint carryZ = z0 < left.z ? 1u : 0u;
    uint z = z0 + carryY;
    carryZ |= z < z0 ? 1u : 0u;
    uint w0 = left.w + right.w;
    uint carryW = w0 < left.w ? 1u : 0u;
    uint w = w0 + carryZ;
    carryW |= w < w0 ? 1u : 0u;
    carryOut = carryW;
    return uint4(x, y, z, w);
}

void SigmaNativeU256Add(inout uint4 low, inout uint4 high,
    uint4 addLow, uint4 addHigh, inout uint valid)
{
    uint carryLow;
    low = SigmaNativeU128AddCarry(low, addLow, carryLow);
    uint carryHigh;
    high = SigmaNativeU128AddCarry(high, addHigh, carryHigh);
    if (carryLow != 0u)
    {
        uint carryIncrement;
        high = SigmaNativeU128AddCarry(high, uint4(1u, 0u, 0u, 0u),
            carryIncrement);
        carryHigh |= carryIncrement;
    }
    valid &= carryHigh == 0u ? 1u : 0u;
}

bool SigmaNativeU128LessInitialized(uint4 left, uint4 right)
{
    bool result = left.x < right.x;
    if (left.w != right.w)
        result = left.w < right.w;
    else if (left.z != right.z)
        result = left.z < right.z;
    else if (left.y != right.y)
        result = left.y < right.y;
    return result;
}

bool SigmaNativeU256Less(uint4 leftLow, uint4 leftHigh,
    uint4 rightLow, uint4 rightHigh)
{
    bool result = SigmaNativeU128LessInitialized(leftLow, rightLow);
    if (!SigmaU128Equal(leftHigh, rightHigh))
        result = SigmaNativeU128LessInitialized(leftHigh, rightHigh);
    return result;
}

void SigmaNativeU256Subtract(uint4 leftLow, uint4 leftHigh,
    uint4 rightLow, uint4 rightHigh, out uint4 resultLow,
    out uint4 resultHigh)
{
    uint borrow = SigmaNativeU128LessInitialized(leftLow, rightLow) ? 1u : 0u;
    resultLow = SigmaNativeU128Subtract(leftLow, rightLow);
    resultHigh = SigmaNativeU128Subtract(leftHigh, rightHigh);
    if (borrow != 0u)
        resultHigh = SigmaNativeU128Subtract(resultHigh,
            uint4(1u, 0u, 0u, 0u));
}

void SigmaNativeSignedU256Add(inout uint4 magnitudeLow,
    inout uint4 magnitudeHigh, inout uint negative,
    uint4 termLow, uint4 termHigh, uint termNegative, inout uint valid)
{
    bool termZero = all(termLow == 0u) && all(termHigh == 0u);
    bool magnitudeZero = all(magnitudeLow == 0u) && all(magnitudeHigh == 0u);
    if (termZero)
        return;
    if (magnitudeZero)
    {
        magnitudeLow = termLow;
        magnitudeHigh = termHigh;
        negative = termNegative;
        return;
    }
    if (negative == termNegative)
    {
        SigmaNativeU256Add(magnitudeLow, magnitudeHigh, termLow, termHigh,
            valid);
        return;
    }
    bool magnitudeLess = SigmaNativeU256Less(magnitudeLow, magnitudeHigh,
        termLow, termHigh);
    uint4 resultLow;
    uint4 resultHigh;
    if (magnitudeLess)
    {
        SigmaNativeU256Subtract(termLow, termHigh, magnitudeLow,
            magnitudeHigh, resultLow, resultHigh);
        negative = termNegative;
    }
    else
        SigmaNativeU256Subtract(magnitudeLow, magnitudeHigh, termLow,
            termHigh, resultLow, resultHigh);
    magnitudeLow = resultLow;
    magnitudeHigh = resultHigh;
    if (all(magnitudeLow == 0u) && all(magnitudeHigh == 0u))
        negative = 0u;
}

void SigmaNativeU256AddShifted128(inout uint4 low, inout uint4 high,
    uint4 addend, uint limbShift, inout uint valid)
{
    uint4 addLow = uint4(0u, 0u, 0u, 0u);
    uint4 addHigh = uint4(0u, 0u, 0u, 0u);
    if (limbShift == 0u)
        addLow = addend;
    else if (limbShift == 2u)
    {
        addLow = uint4(0u, 0u, addend.x, addend.y);
        addHigh = uint4(addend.z, addend.w, 0u, 0u);
    }
    else
        addHigh = addend;
    SigmaNativeU256Add(low, high, addLow, addHigh, valid);
}

void SigmaNativeU128MultiplyU32(uint4 magnitude, uint scalar,
    out uint4 low, out uint4 high,
    inout uint valid)
{
    SigmaU128 p0 = SigmaU64MultiplyWide(magnitude.xy, uint2(scalar, 0u));
    SigmaU128 p1 = SigmaU64MultiplyWide(magnitude.zw, uint2(scalar, 0u));
    low = uint4(0u, 0u, 0u, 0u);
    high = uint4(0u, 0u, 0u, 0u);
    SigmaNativeU256AddShifted128(low, high, p0.word, 0u, valid);
    SigmaNativeU256AddShifted128(low, high, p1.word, 2u, valid);
}

void SigmaNativeGroupClassifyFactor(uint factorIndex, uint threadIndex)
{
    if (threadIndex < 16u)
        SigmaNativeGroupAny[threadIndex] = any(
            SigmaNativeGroupFactorValue(factorIndex, threadIndex) != 0u)
            ? 1u : 0u;
    GroupMemoryBarrierWithGroupSync();

    [unroll]
    for (uint stride = 8u; stride != 0u; stride >>= 1u)
    {
        if (threadIndex < stride)
            SigmaNativeGroupAny[threadIndex] |=
                SigmaNativeGroupAny[threadIndex + stride];
        GroupMemoryBarrierWithGroupSync();
    }

    uint row = threadIndex >> 4u;
    uint column = threadIndex & 15u;
    uint2 rowRaw = SigmaNativeGroupFactorValue(factorIndex, row);
    uint2 columnRaw = SigmaNativeGroupFactorValue(factorIndex, column);
    int metric = SIGMA_MERKABA_INFORMATION_METRIC[row * 16u + column];
    uint metricMagnitude = metric < 0 ? (uint)(-metric) : (uint)metric;
    SigmaU128 coefficientProduct = SigmaU64MultiplyWide(
        SigmaU64AbsSigned(rowRaw), SigmaU64AbsSigned(columnRaw));
    uint termValid = 1u;
    SigmaNativeU128MultiplyU32(coefficientProduct.word, metricMagnitude,
        SigmaNativeGroupNormLow[threadIndex],
        SigmaNativeGroupNormHigh[threadIndex], termValid);
    SigmaNativeGroupNormSign[threadIndex] =
        ((rowRaw.y >> 31u) ^ (columnRaw.y >> 31u) ^
            (metric < 0 ? 1u : 0u)) & 1u;
    SigmaNativeGroupNormValid[threadIndex] = termValid;
    GroupMemoryBarrierWithGroupSync();

    // The complete 16x16 G quadratic plane is reduced as signed 256-bit
    // magnitudes. Fractional Q48 values and the full checked int64 range remain
    // exact; relation cardinality changes workgroups, never dispatch structure.
    [unroll]
    for (uint metricStride = 128u; metricStride != 0u;
        metricStride >>= 1u)
    {
        if (threadIndex < metricStride)
        {
            uint4 low = SigmaNativeGroupNormLow[threadIndex];
            uint4 high = SigmaNativeGroupNormHigh[threadIndex];
            uint sign = SigmaNativeGroupNormSign[threadIndex];
            uint valid = SigmaNativeGroupNormValid[threadIndex] &
                SigmaNativeGroupNormValid[threadIndex + metricStride];
            SigmaNativeSignedU256Add(low, high, sign,
                SigmaNativeGroupNormLow[threadIndex + metricStride],
                SigmaNativeGroupNormHigh[threadIndex + metricStride],
                SigmaNativeGroupNormSign[threadIndex + metricStride], valid);
            SigmaNativeGroupNormLow[threadIndex] = low;
            SigmaNativeGroupNormHigh[threadIndex] = high;
            SigmaNativeGroupNormSign[threadIndex] = sign;
            SigmaNativeGroupNormValid[threadIndex] = valid;
        }
        GroupMemoryBarrierWithGroupSync();
    }

    uint anyRaw = SigmaNativeGroupAny[0];
    if (threadIndex == 0u)
    {
        uint normValid = SigmaNativeGroupNormValid[0];
        uint4 normLow = SigmaNativeGroupNormLow[0];
        uint4 normHigh = SigmaNativeGroupNormHigh[0];
        bool normNegative = SigmaNativeGroupNormSign[0] != 0u &&
            (any(normLow != 0u) || any(normHigh != 0u));
        normValid &= normNegative ? 0u : 1u;
        // Primitive content is a positive common scale and cancels from
        // raw/sqrt(raw^T G raw). This exact raw norm therefore classifies the
        // identical normalized factor without integer-only preconditions.
        bool normZero = all(normLow == 0u) && all(normHigh == 0u);
        uint kernel = 0u;
        uint factorClass;
        if (anyRaw == 0u)
            factorClass = SIGMA_NATIVE_FACTOR_EXACT_CLOSED;
        else if (normValid == 0u)
        {
            factorClass = SIGMA_NATIVE_FACTOR_UNRESOLVED;
            SigmaNativeGroupAllValid = 0u;
        }
        else if (normZero)
        {
            factorClass = SIGMA_NATIVE_FACTOR_UNRESOLVED;
            kernel = 1u;
        }
        else
            factorClass = SIGMA_NATIVE_FACTOR_INCOMPATIBLE;
        SigmaNativeGroupFactorClass[factorIndex] = factorClass;
        SigmaNativeGroupFactorKernel[factorIndex] = kernel;
        SigmaNativeGroupFactorNormLow[factorIndex] = normLow;
        SigmaNativeGroupFactorNormHigh[factorIndex] = normHigh;
    }
    GroupMemoryBarrierWithGroupSync();
}

uint SigmaNativeHashGroupFactor(uint factorIndex)
{
    uint hash = 2166136261u;
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
    {
        uint2 value = factorIndex == 0u ? SigmaNativeGroupLink[lane] :
            (factorIndex == 1u ? SigmaNativeGroupAssociator[lane] :
                SigmaNativeGroupTransition[lane]);
        hash = (hash ^ value.x) * 16777619u;
        hash = (hash ^ value.y) * 16777619u;
    }
    return hash;
}

bool SigmaNativeGroupU128Less(uint4 left, uint4 right)
{
    return SigmaNativeU128LessInitialized(left, right);
}

void SigmaNativeEvaluateRelationGroup(uint relationIndex, uint threadIndex,
    bool entryValid)
{
    uint4 input = _NativeRelationInputs[relationIndex];
    uint4 plan = _NativeRelationPlans[relationIndex];
    uint4 near = _NativeRelationNearIntervals[relationIndex];
    if (threadIndex < 16u)
    {
        SigmaNativeGroupLeft[threadIndex] =
            _NativeStates[input.x + threadIndex];
        SigmaNativeGroupRight[threadIndex] =
            _NativeStates[plan.x + threadIndex];
        SigmaNativeGroupContext[threadIndex] =
            _NativeStates[plan.y + threadIndex];
    }
    if (threadIndex == 0u)
        SigmaNativeGroupAllValid = entryValid ? 1u : 0u;
    GroupMemoryBarrierWithGroupSync();

    SigmaNativeGroupProduct(SIGMA_NATIVE_PRODUCT_LEFT_RIGHT, threadIndex);
    SigmaNativeGroupProduct(SIGMA_NATIVE_PRODUCT_RIGHT_CONTEXT, threadIndex);
    SigmaNativeGroupProduct(SIGMA_NATIVE_PRODUCT_AB_CONTEXT, threadIndex);
    SigmaNativeGroupProduct(SIGMA_NATIVE_PRODUCT_LEFT_BC, threadIndex);
    SigmaNativeGroupProduct(SIGMA_NATIVE_PRODUCT_TRANSITION, threadIndex);

    uint packed = plan.z;
    int transportSign = SigmaMerkabaSignTransport(packed & 15u,
        (packed >> 4u) & 15u);
    if (threadIndex < 16u)
    {
        uint laneValid = 1u;
        uint2 transported = transportSign < 0
            ? SigmaQ48NegateChecked(SigmaNativeGroupLeft[threadIndex], laneValid)
            : SigmaNativeGroupLeft[threadIndex];
        SigmaNativeGroupLink[threadIndex] = SigmaQ48SubChecked(
            SigmaNativeGroupRight[threadIndex], transported, laneValid);
        SigmaNativeGroupAssociator[threadIndex] = SigmaQ48SubChecked(
            SigmaNativeGroupLeftBracket[threadIndex],
            SigmaNativeGroupRightBracket[threadIndex], laneValid);
        SigmaNativeGroupLaneValid[threadIndex] = laneValid;
    }
    GroupMemoryBarrierWithGroupSync();
    if (threadIndex == 0u)
    {
        [unroll]
        for (uint lane = 0u; lane < 16u; ++lane)
            SigmaNativeGroupAllValid &= SigmaNativeGroupLaneValid[lane];
    }
    GroupMemoryBarrierWithGroupSync();

    SigmaNativeGroupClassifyFactor(0u, threadIndex);
    SigmaNativeGroupClassifyFactor(1u, threadIndex);

    uint actionValid = 1u;
    if (threadIndex < SIGMA_ANNIHILATOR_ACTION_COUNT)
    {
        int4 dyad = SIGMA_ANNIHILATOR_ACTIONS[threadIndex];
        uint4 error = SIGMA_U128_ZERO;
        [unroll]
        for (uint outputLane = 0u; outputLane < 16u; ++outputLane)
        {
            uint firstSource = (uint)dyad.x ^ outputLane;
            uint secondSource = (uint)dyad.z ^ outputLane;
            uint2 first = SigmaNativeGroupTransition[firstSource];
            uint2 second = SigmaNativeGroupTransition[secondSource];
            int firstSign = SigmaMulBasisSign(firstSource, (uint)dyad.x) * dyad.y;
            int secondSign = SigmaMulBasisSign(secondSource, (uint)dyad.z) * dyad.w;
            if (firstSign < 0)
                first = SigmaQ48NegateChecked(first, actionValid);
            if (secondSign < 0)
                second = SigmaQ48NegateChecked(second, actionValid);
            uint2 residual = SigmaQ48AddChecked(first, second, actionValid);
            error = SigmaU128Add64(error, SigmaU64AbsSigned(residual), actionValid);
        }
        SigmaNativeGroupResiduals[threadIndex] = error;
        SigmaNativeGroupResidualActions[threadIndex] = threadIndex;
    }
    else
    {
        SigmaNativeGroupResiduals[threadIndex] = SIGMA_U128_MAX;
        SigmaNativeGroupResidualActions[threadIndex] = 0xffffffffu;
    }
    SigmaNativeGroupResidualValid[threadIndex] = actionValid;
    GroupMemoryBarrierWithGroupSync();

    [unroll]
    for (uint residualStride = 128u; residualStride != 0u;
        residualStride >>= 1u)
    {
        if (threadIndex < residualStride)
        {
            uint partner = threadIndex + residualStride;
            uint4 leftResidual = SigmaNativeGroupResiduals[threadIndex];
            uint4 rightResidual = SigmaNativeGroupResiduals[partner];
            uint leftAction = SigmaNativeGroupResidualActions[threadIndex];
            uint rightAction = SigmaNativeGroupResidualActions[partner];
            bool takeRight = SigmaNativeGroupU128Less(rightResidual,
                    leftResidual) ||
                (SigmaU128Equal(rightResidual, leftResidual) &&
                    rightAction < leftAction);
            if (takeRight)
            {
                SigmaNativeGroupResiduals[threadIndex] = rightResidual;
                SigmaNativeGroupResidualActions[threadIndex] = rightAction;
            }
            SigmaNativeGroupResidualValid[threadIndex] &=
                SigmaNativeGroupResidualValid[partner];
        }
        GroupMemoryBarrierWithGroupSync();
    }

    if (threadIndex == 0u)
    {
        SigmaNativeGroupAllValid &= SigmaNativeGroupResidualValid[0];
        uint linkClass = SigmaNativeGroupFactorClass[0];
        uint associatorClass = SigmaNativeGroupFactorClass[1];
        int holonomy = SigmaMerkabaPlaquetteHolonomy((packed >> 8u) & 15u,
            (packed >> 12u) & 15u, (packed >> 16u) & 15u);
        uint plaquetteClass = holonomy == 1
            ? SIGMA_NATIVE_FACTOR_EXACT_CLOSED
            : SIGMA_NATIVE_FACTOR_INCOMPATIBLE;
        uint closureClass = SigmaNativeAggregateFactorClass(linkClass,
            associatorClass, plaquetteClass);
        uint4 minimumResidual = SigmaNativeGroupResiduals[0];
        uint minimumAction = SigmaNativeGroupResidualActions[0];
        bool exactResidual = SigmaU128Equal(minimumResidual, SIGMA_U128_ZERO);
        uint exactAction = exactResidual ? minimumAction : 0xffffffffu;
        uint transitionBits = 0u;
        uint defaultBits = 0u;
        [unroll]
        for (uint stateLane = 0u; stateLane < 16u; ++stateLane)
        {
            transitionBits |= SigmaNativeGroupTransition[stateLane].x |
                SigmaNativeGroupTransition[stateLane].y;
            defaultBits |= SigmaNativeGroupLeft[stateLane].x |
                SigmaNativeGroupLeft[stateLane].y |
                SigmaNativeGroupRight[stateLane].x |
                SigmaNativeGroupRight[stateLane].y |
                SigmaNativeGroupContext[stateLane].x |
                SigmaNativeGroupContext[stateLane].y;
        }
        bool exactZd = transitionBits != 0u && exactAction != 0xffffffffu;
        bool minimumFitsQ48 = minimumResidual.z == 0u && minimumResidual.w == 0u;
        bool calibratedNear = !exactZd && input.y != 0u && minimumFitsQ48 &&
            SigmaNativePointInInterval(minimumResidual.xy, near.xy, near.zw) &&
            !exactResidual;

        uint relationClass;
        if (defaultBits == 0u)
            relationClass = SIGMA_MERKABA_RELATION_DEFAULT_SAT;
        else if (closureClass == SIGMA_NATIVE_FACTOR_UNRESOLVED)
            relationClass = SIGMA_MERKABA_RELATION_UNRESOLVED;
        else if (associatorClass == SIGMA_NATIVE_FACTOR_INCOMPATIBLE)
            relationClass = SIGMA_MERKABA_RELATION_NONASSOCIATIVE_CONTEXT;
        else if (closureClass == SIGMA_NATIVE_FACTOR_INCOMPATIBLE)
            relationClass = SIGMA_MERKABA_RELATION_NO_RELATION;
        else if (exactZd)
            relationClass = SIGMA_MERKABA_RELATION_EXACT_ZD;
        else if (calibratedNear)
            relationClass = SIGMA_MERKABA_RELATION_NEAR_SINGULAR_Q48;
        else
            relationClass = SIGMA_MERKABA_RELATION_REGULAR;

        _NativeRelationResults[relationIndex] = uint4(relationClass,
            exactAction, minimumResidual.y, SigmaNativeGroupAllValid);
        _NativeRelationFactors[relationIndex] = uint4(linkClass,
            associatorClass, plaquetteClass,
            closureClass | (SigmaNativeGroupFactorKernel[0] << 8u) |
                (SigmaNativeGroupFactorKernel[1] << 9u));
        _NativeRelationHashes[relationIndex] = uint4(
            SigmaNativeHashGroupFactor(0u), SigmaNativeHashGroupFactor(1u),
            SigmaNativeHashGroupFactor(2u), minimumResidual.x);
        uint normOffset = relationIndex * 4u;
        _NativeRelationNorms[normOffset] = SigmaNativeGroupFactorNormLow[0];
        _NativeRelationNorms[normOffset + 1u] =
            SigmaNativeGroupFactorNormHigh[0];
        _NativeRelationNorms[normOffset + 2u] =
            SigmaNativeGroupFactorNormLow[1];
        _NativeRelationNorms[normOffset + 3u] =
            SigmaNativeGroupFactorNormHigh[1];
    }
}
#endif

#endif
