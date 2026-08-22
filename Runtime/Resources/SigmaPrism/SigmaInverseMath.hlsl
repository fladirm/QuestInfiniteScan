#ifndef SIGMA_INVERSE_MATH_INCLUDED
#define SIGMA_INVERSE_MATH_INCLUDED

#include "SigmaOperatorPlan.hlsl"
#include "SigmaInverseAbi.hlsl"

StructuredBuffer<uint2> _DepthCalibrationQ48;
Texture2D<float4> _DepthSlopeBoundsLeft;
Texture2D<float4> _DepthSlopeBoundsRight;

static const uint2 SIGMA_Q48_ZERO = uint2(0u, 0u);
static const uint2 SIGMA_Q48_ONE = uint2(0u, 0x00010000u);

uint2 SigmaCalibrationValue(uint eye, uint field)
{
    return _DepthCalibrationQ48[eye * SIGMA_DEPTH_VIEW_STRIDE + field];
}

bool SigmaQ48Positive(uint2 value)
{
    return !SigmaQ48Less(value, SIGMA_Q48_ZERO) &&
        !SigmaU64Equal(value, SIGMA_Q48_ZERO);
}

uint2 SigmaQ48FromUnsignedInteger(uint value, inout uint valid)
{
    uint2 raw = uint2(value, 0u);
    return SigmaQ48ShiftLeftChecked(raw, 48u, valid);
}

// Exact conversion of the represented IEEE-754 float value into Q16.48. The
// source float is sensor/readout input; after this point every accept/reject and
// mutation decision is packed integer arithmetic.
uint2 SigmaQ48FromFloatNearestEven(float value, inout uint valid)
{
    uint bits = asuint(value);
    bool negative = (bits & 0x80000000u) != 0u;
    uint exponentBits = (bits >> 23u) & 0xffu;
    uint fraction = bits & 0x007fffffu;
    if (exponentBits == 0xffu)
    {
        valid = 0u;
        return SIGMA_Q48_ZERO;
    }
    if (exponentBits == 0u && fraction == 0u)
        return SIGMA_Q48_ZERO;

    uint mantissa = exponentBits == 0u ? fraction : fraction | 0x00800000u;
    int exponent = exponentBits == 0u ? -126 : (int)exponentBits - 127;
    int rawShift = exponent + 25;
    uint2 magnitude = uint2(mantissa, 0u);
    if (rawShift >= 0)
        magnitude = SigmaQ48ShiftLeftChecked(magnitude, (uint)rawShift, valid);
    else
        magnitude = SigmaQ48ShiftRightNearestEven(magnitude,
            (uint)(-rawShift), valid);
    return SigmaApplyMagnitudeSign(magnitude, negative, valid);
}

uint2 SigmaQ48AbsDifference(uint2 a, uint2 b, inout uint valid)
{
    return SigmaQ48Less(a, b)
        ? SigmaQ48SubChecked(b, a, valid)
        : SigmaQ48SubChecked(a, b, valid);
}

uint2 SigmaQ48Midpoint(uint2 lo, uint2 hi, inout uint valid)
{
    uint2 delta = SigmaQ48SubChecked(hi, lo, valid);
    uint2 half = SigmaQ48ShiftRightNearestEven(delta, 1u, valid);
    return SigmaQ48AddChecked(lo, half, valid);
}

SigmaQ48Bounds SigmaQ48PointBounds(uint2 value)
{
    SigmaQ48Bounds result;
    result.lo = value;
    result.hi = value;
    return result;
}

SigmaQ48Bounds SigmaQ48Widen(uint2 center, uint2 halfWidth, inout uint valid)
{
    SigmaQ48Bounds result;
    result.lo = SigmaQ48SubChecked(center, halfWidth, valid);
    result.hi = SigmaQ48AddChecked(center, halfWidth, valid);
    return result;
}

SigmaQ48Bounds SigmaQ48IntervalProduct(SigmaQ48Bounds a, SigmaQ48Bounds b,
    inout uint valid)
{
    uint localValid = 1u;
    uint2 lo00 = SigmaQ48MulLower(a.lo, b.lo, localValid);
    uint2 lo01 = SigmaQ48MulLower(a.lo, b.hi, localValid);
    uint2 lo10 = SigmaQ48MulLower(a.hi, b.lo, localValid);
    uint2 lo11 = SigmaQ48MulLower(a.hi, b.hi, localValid);
    uint2 hi00 = SigmaQ48MulUpper(a.lo, b.lo, localValid);
    uint2 hi01 = SigmaQ48MulUpper(a.lo, b.hi, localValid);
    uint2 hi10 = SigmaQ48MulUpper(a.hi, b.lo, localValid);
    uint2 hi11 = SigmaQ48MulUpper(a.hi, b.hi, localValid);
    SigmaQ48Bounds result;
    result.lo = SigmaQ48Min(SigmaQ48Min(lo00, lo01), SigmaQ48Min(lo10, lo11));
    result.hi = SigmaQ48Max(SigmaQ48Max(hi00, hi01), SigmaQ48Max(hi10, hi11));
    valid &= localValid;
    return result;
}

SigmaQ48Bounds SigmaQ48IntervalAdd(SigmaQ48Bounds a, SigmaQ48Bounds b,
    inout uint valid)
{
    SigmaQ48Bounds result;
    result.lo = SigmaQ48AddChecked(a.lo, b.lo, valid);
    result.hi = SigmaQ48AddChecked(a.hi, b.hi, valid);
    return result;
}

SigmaQ48Bounds SigmaQ48ScaleBounds(uint2 coefficient, SigmaQ48Bounds value,
    inout uint valid)
{
    return SigmaQ48IntervalProduct(SigmaQ48PointBounds(coefficient), value, valid);
}

uint2 SigmaDepthHalfWidth(uint eye, uint2 range, inout uint valid)
{
    uint selected = 5u;
    [loop]
    for (uint bin = 0u; bin < 6u; ++bin)
    {
        uint2 threshold = SigmaCalibrationValue(eye,
            SIGMA_CAL_RANGE_THRESHOLDS + bin);
        if (selected == 5u && !SigmaQ48Less(threshold, range))
            selected = bin;
    }
    uint2 width = SigmaCalibrationValue(eye, SIGMA_CAL_RANGE_WIDTHS + selected);
    uint2 poseWidth = SigmaCalibrationValue(eye, SIGMA_CAL_POSE_WIDTH);
    return SigmaQ48AddChecked(width, poseWidth, valid);
}

uint2 SigmaPriorHalfWidth(uint2 mass, inout uint valid)
{
    uint2 floorWidth = SigmaCalibrationValue(0u, SIGMA_CAL_PRIOR_FLOOR);
    uint2 ceilingWidth = SigmaCalibrationValue(0u, SIGMA_CAL_PRIOR_CEILING);
    uint2 width = floorWidth;
    uint2 threshold = SIGMA_Q48_ONE;
    // Exact dyadic resistance ladder: weaker projective mass broadens the prior
    // cell, but never changes its centre or turns confidence into a vote.
    [loop]
    for (uint step = 0u; step < 16u; ++step)
    {
        bool widen = SigmaQ48Less(mass, threshold) &&
            SigmaQ48Less(width, ceilingWidth);
        if (widen)
        {
            threshold = SigmaQ48ShiftRightNearestEven(threshold, 1u, valid);
            width = SigmaQ48Min(ceilingWidth,
                SigmaQ48ShiftLeftChecked(width, 1u, valid));
        }
    }
    return SigmaQ48Max(floorWidth, SigmaQ48Min(width, ceilingWidth));
}

uint2 SigmaInformationMassForWidth(uint2 maximumWidth, inout uint valid)
{
    uint2 floorWidth = SigmaCalibrationValue(0u, SIGMA_CAL_PRIOR_FLOOR);
    uint2 massFloor = SigmaCalibrationValue(0u, SIGMA_CAL_CONTACT_MASS_MIN);
    uint2 mass = SIGMA_Q48_ONE;
    uint2 widthThreshold = floorWidth;
    // Conservative dyadic lowering of section 13.1. It is monotone, exact and
    // contains no source-count term: independent support merely authorizes the
    // width-derived hardness.
    [loop]
    for (uint step = 0u; step < 16u; ++step)
    {
        bool weaken = SigmaQ48Less(widthThreshold, maximumWidth) &&
            SigmaQ48Less(massFloor, mass);
        if (weaken)
        {
            widthThreshold = SigmaQ48ShiftLeftChecked(widthThreshold, 1u, valid);
            mass = SigmaQ48ShiftRightNearestEven(mass, 1u, valid);
        }
    }
    return SigmaQ48Max(massFloor, SigmaQ48Min(mass, SIGMA_Q48_ONE));
}

uint SigmaClassifyFirstHitExact(uint2 measuredRange, uint2 measuredHalfWidth,
    float2 predictedDepthSupport, out SigmaQ48Bounds measuredBounds,
    out SigmaQ48Bounds predictedBounds, inout uint valid)
{
    measuredBounds = SigmaQ48Widen(measuredRange, measuredHalfWidth, valid);
    predictedBounds.lo = SIGMA_Q48_ZERO;
    predictedBounds.hi = uint2(1u, 0u);
    if (!(predictedDepthSupport.y > 0.0) || !isfinite(predictedDepthSupport.x))
        return valid != 0u ? SIGMA_SECTOR_HIT : SIGMA_SECTOR_NO_CONSTRAINT;

    uint2 predictedRange = SigmaQ48FromFloatNearestEven(
        predictedDepthSupport.x, valid);
    uint2 predictedMass = SigmaQ48FromFloatNearestEven(
        predictedDepthSupport.y, valid);
    uint2 predictedWidth = SigmaPriorHalfWidth(SigmaQ48Max(predictedMass,
        SigmaCalibrationValue(0u, SIGMA_CAL_CONTACT_MASS_MIN)), valid);
    predictedBounds = SigmaQ48Widen(predictedRange, predictedWidth, valid);
    if (valid == 0u)
        return SIGMA_SECTOR_NO_CONSTRAINT;
    if (SigmaQ48Less(measuredBounds.hi, predictedBounds.lo))
        return SIGMA_SECTOR_NO_CONSTRAINT;
    if (SigmaQ48Less(predictedBounds.hi, measuredBounds.lo))
        return SIGMA_SECTOR_PRE_HIT_EXCLUSION;
    return SIGMA_SECTOR_HIT;
}

SigmaQ48Bounds SigmaPixelSlopeBounds(uint eye, uint2 pixel, bool horizontal,
    inout uint valid)
{
    float4 slopes = eye == 0u ? _DepthSlopeBoundsLeft[pixel] :
        _DepthSlopeBoundsRight[pixel];
    SigmaQ48Bounds result;
    result.lo = SigmaQ48FromFloatNearestEven(horizontal ? slopes.x : slopes.z,
        valid);
    result.hi = SigmaQ48FromFloatNearestEven(horizontal ? slopes.y : slopes.w,
        valid);
    // Include the represented-float quantization boundary conservatively.
    uint2 oneLsb = uint2(1u, 0u);
    result.lo = SigmaQ48SubChecked(result.lo, oneLsb, valid);
    result.hi = SigmaQ48AddChecked(result.hi, oneLsb, valid);
    return result;
}

bool SigmaBuildDepthWorldCell(uint eye, uint2 pixel, float2 metricDepth,
    uint sourceClass, uint independenceKey, out SigmaDepthCell3 cell,
    out SigmaQ48Bounds measuredRangeBounds)
{
    cell = (SigmaDepthCell3)0;
    measuredRangeBounds = (SigmaQ48Bounds)0;
    uint valid = 1u;
    if (!(metricDepth.x > 0.0) || !(metricDepth.y > 0.0) ||
        !all(isfinite(metricDepth)))
        return false;

    uint2 range = SigmaQ48FromFloatNearestEven(metricDepth.x, valid);
    uint2 viewZ = SigmaQ48FromFloatNearestEven(metricDepth.y, valid);
    uint2 halfWidth = SigmaDepthHalfWidth(eye, range, valid);
    measuredRangeBounds = SigmaQ48Widen(range, halfWidth, valid);
    SigmaQ48Bounds z = SigmaQ48Widen(viewZ, halfWidth, valid);
    z.lo = SigmaQ48Max(z.lo, SigmaCalibrationValue(eye, SIGMA_CAL_NEAR));
    z.hi = SigmaQ48Min(z.hi, SigmaCalibrationValue(eye, SIGMA_CAL_FAR));
    if (valid == 0u || SigmaQ48Less(z.hi, z.lo))
        return false;

    SigmaQ48Bounds slopeX = SigmaPixelSlopeBounds(eye, pixel, true, valid);
    SigmaQ48Bounds slopeY = SigmaPixelSlopeBounds(eye, pixel, false, valid);
    SigmaQ48Bounds cameraAxis[3];
    cameraAxis[0] = SigmaQ48IntervalProduct(slopeX, z, valid);
    cameraAxis[1] = SigmaQ48IntervalProduct(slopeY, z, valid);
    cameraAxis[2] = z;

    [unroll]
    for (uint row = 0u; row < 3u; ++row)
    {
        SigmaQ48Bounds world = SigmaQ48PointBounds(
            SigmaCalibrationValue(eye, SIGMA_CAL_TX + row));
        [loop]
        for (uint axis = 0u; axis < 3u; ++axis)
        {
            uint2 coefficient = SigmaCalibrationValue(eye,
                SIGMA_CAL_R00 + row * 3u + axis);
            world = SigmaQ48IntervalAdd(world,
                SigmaQ48ScaleBounds(coefficient, cameraAxis[axis], valid), valid);
        }
        // Preserve a conservative final LSB around quantized pose/calibration math.
        uint2 oneLsb = uint2(1u, 0u);
        world.lo = SigmaQ48SubChecked(world.lo, oneLsb, valid);
        world.hi = SigmaQ48AddChecked(world.hi, oneLsb, valid);
        cell.axis[row] = world;
    }
    cell.sourceClass = sourceClass;
    cell.independenceKey = independenceKey;
    cell.sector = SIGMA_SECTOR_HIT;
    cell.valid = valid;
    return valid != 0u;
}

void SigmaMeetAxis(SigmaQ48Bounds incoming, uint source,
    inout SigmaQ48Bounds joint, inout uint loSource, inout uint hiSource)
{
    bool takeLo = SigmaQ48Less(joint.lo, incoming.lo) ||
        (SigmaU64Equal(incoming.lo, joint.lo) && source < loSource);
    bool takeHi = SigmaQ48Less(incoming.hi, joint.hi) ||
        (SigmaU64Equal(incoming.hi, joint.hi) && source < hiSource);
    if (takeLo)
    {
        joint.lo = incoming.lo;
        loSource = source;
    }
    if (takeHi)
    {
        joint.hi = incoming.hi;
        hiSource = source;
    }
}

uint SigmaPackSources(uint source[3])
{
    return (source[0] & 3u) | ((source[1] & 3u) << 2u) |
        ((source[2] & 3u) << 4u);
}

// Only the four generated geometry rows participate in S4-04 depth inversion.
// Re-running a complete 16x16 B/B^T transform for three changed rows caused a
// pathological shader expansion and unnecessary register pressure. Orthogonality
// gives the exact sparse update below:
//
//   s' = s + (B_geometry^T * (targetGeometry-currentGeometry)) / 16.
//
// The original state is already an exact inverse image of B*s, so adding the
// nearest-even dyadic correction is bit-identical to replacing those rows in the
// complete transform and applying B^T>>4. Candidate rows are still re-evaluated
// before publication.
bool SigmaApplyGeometryTargets(uint2 state[16], uint2 geometry[4],
    uint2 targetGeometry[3], out uint2 candidate[16], inout uint valid)
{
    uint2 delta[3];
    [loop]
    for (uint axis = 0u; axis < 3u; ++axis)
        delta[axis] = SigmaQ48SubChecked(targetGeometry[axis],
            geometry[axis + 1u], valid);

    [loop]
    for (uint lane = 0u; lane < 16u; ++lane)
    {
        uint2 correction = SIGMA_Q48_ZERO;
        [loop]
        for (uint axis = 0u; axis < 3u; ++axis)
        {
            uint row = SIGMA_GEOMETRY_ROWS[axis + 1u];
            uint2 term = SigmaHadamardSign(row, lane) < 0
                ? SigmaQ48NegateChecked(delta[axis], valid)
                : delta[axis];
            correction = SigmaQ48AddChecked(correction, term, valid);
        }
        correction = SigmaQ48ShiftRightNearestEven(correction, 4u, valid);
        candidate[lane] = SigmaQ48AddChecked(state[lane], correction, valid);
    }
    return valid != 0u;
}

bool SigmaRevalidateGeometryCandidate(uint2 candidate[16], uint2 expectedMass,
    SigmaQ48Bounds accepted[3], inout uint valid)
{
    uint2 check[4];
    [loop]
    for (uint checkRow = 0u; checkRow < 4u; ++checkRow)
        check[checkRow] = SIGMA_Q48_ZERO;
    SigmaGeometryGPlan(candidate, check, valid);
    uint2 oneLsb = uint2(1u, 0u);
    uint2 massDelta = SigmaQ48AbsDifference(check[0], expectedMass, valid);
    if (valid == 0u || SigmaQ48Less(oneLsb, massDelta) ||
        !SigmaQ48Positive(check[0]))
        return false;

    [loop]
    for (uint axis = 0u; axis < 3u; ++axis)
    {
        uint2 coordinate = SigmaQ48DivNearestEven(check[axis + 1u],
            check[0], valid);
        uint2 lowWithAllowance = SigmaQ48SubChecked(accepted[axis].lo,
            oneLsb, valid);
        uint2 highWithAllowance = SigmaQ48AddChecked(accepted[axis].hi,
            oneLsb, valid);
        if (valid == 0u || SigmaQ48Less(coordinate, lowWithAllowance) ||
            SigmaQ48Less(highWithAllowance, coordinate))
            return false;
    }
    return true;
}

bool SigmaStrengthenCandidateMass(inout uint2 candidate[16], uint2 oldMass,
    uint2 targetMass, inout uint valid)
{
    if (!SigmaQ48Less(oldMass, targetMass))
        return valid != 0u;
    uint2 ratio = SigmaQ48DivNearestEven(targetMass, oldMass, valid);
    [loop]
    for (uint stateLane = 0u; stateLane < 16u; ++stateLane)
        candidate[stateLane] = SigmaQ48MulNearestEven(candidate[stateLane],
            ratio, valid);
    return valid != 0u;
}

bool SigmaBuildSupportedDepthProposal(uint2 state[16], SigmaDepthCell3 left,
    SigmaDepthCell3 right, out uint2 targetGeometry[3], out uint2 targetMass,
    out uint status, out uint conflictMask, out uint loSourcesPacked,
    out uint hiSourcesPacked, out uint2 gaps[3])
{
    status = 0u;
    conflictMask = 0u;
    loSourcesPacked = 0u;
    hiSourcesPacked = 0u;
    targetMass = SIGMA_Q48_ZERO;
    [loop]
    for (uint axis = 0u; axis < 3u; ++axis)
    {
        targetGeometry[axis] = SIGMA_Q48_ZERO;
        gaps[axis] = SIGMA_Q48_ZERO;
    }

    uint valid = 1u;
    uint2 geometry[4];
    SigmaGeometryGPlan(state, geometry, valid);
    uint2 mass = geometry[0];
    if (valid == 0u || !SigmaQ48Positive(mass))
        return false;

    uint2 priorWidth = SigmaPriorHalfWidth(mass, valid);
    SigmaQ48Bounds joint[3];
    uint loSource[3];
    uint hiSource[3];
    uint2 current[3];
    [loop]
    for (uint axis = 0u; axis < 3u; ++axis)
    {
        current[axis] = SigmaQ48DivNearestEven(
            geometry[axis + 1u], mass, valid);
        joint[axis] = SigmaQ48Widen(current[axis], priorWidth, valid);
        loSource[axis] = SIGMA_SOURCE_PRIOR;
        hiSource[axis] = SIGMA_SOURCE_PRIOR;
    }

    uint hitCount = 0u;
    uint independenceKey0 = 0u;
    uint independenceKey1 = 0u;
    if (left.valid != 0u && left.sector == SIGMA_SECTOR_HIT)
    {
        [loop]
        for (uint axis = 0u; axis < 3u; ++axis)
            SigmaMeetAxis(left.axis[axis], SIGMA_SOURCE_DEPTH_LEFT,
                joint[axis], loSource[axis], hiSource[axis]);
        status |= SIGMA_PROPOSAL_HIT_LEFT;
        independenceKey0 = left.independenceKey;
        ++hitCount;
    }
    if (right.valid != 0u && right.sector == SIGMA_SECTOR_HIT)
    {
        [loop]
        for (uint axis = 0u; axis < 3u; ++axis)
            SigmaMeetAxis(right.axis[axis], SIGMA_SOURCE_DEPTH_RIGHT,
                joint[axis], loSource[axis], hiSource[axis]);
        status |= SIGMA_PROPOSAL_HIT_RIGHT;
        if (hitCount == 0u)
            independenceKey0 = right.independenceKey;
        else
            independenceKey1 = right.independenceKey;
        ++hitCount;
    }
    if (hitCount == 0u || valid == 0u)
        return false;

    [loop]
    for (uint axis = 0u; axis < 3u; ++axis)
    {
        if (SigmaQ48Less(joint[axis].hi, joint[axis].lo))
        {
            conflictMask |= 1u << axis;
            gaps[axis] = SigmaQ48SubChecked(joint[axis].lo,
                joint[axis].hi, valid);
        }
    }
    loSourcesPacked = SigmaPackSources(loSource);
    hiSourcesPacked = SigmaPackSources(hiSource);
    if (conflictMask != 0u || valid == 0u)
    {
        status |= SIGMA_PROPOSAL_CONFLICT;
        return false;
    }


    uint2 maximumWidth = SIGMA_Q48_ZERO;
    [loop]
    for (uint widthAxis = 0u; widthAxis < 3u; ++widthAxis)
        maximumWidth = SigmaQ48Max(maximumWidth,
            SigmaQ48SubChecked(joint[widthAxis].hi, joint[widthAxis].lo, valid));
    targetMass = mass;
    bool independentPair = hitCount >= 2u && independenceKey0 != 0u &&
        independenceKey1 != 0u && independenceKey0 != independenceKey1;
    if (independentPair)
        targetMass = SigmaQ48Max(mass,
            SigmaInformationMassForWidth(maximumWidth, valid));

    bool changed = SigmaQ48Less(mass, targetMass);
    uint2 acceptedCoordinate[3];
    [loop]
    for (uint axis = 0u; axis < 3u; ++axis)
    {
        acceptedCoordinate[axis] = SigmaProjectiveClamp(current[axis],
            joint[axis].lo, joint[axis].hi);
        changed = changed || !SigmaU64Equal(acceptedCoordinate[axis], current[axis]);
        targetGeometry[axis] = SigmaQ48MulNearestEven(mass,
            acceptedCoordinate[axis], valid);
    }
    if (valid == 0u)
        return false;

    // Build and re-read the exact sparse candidate now. The page-write kernel
    // repeats this operation only after the source generation check on CPU.
    uint2 candidate[16];
    if (!SigmaApplyGeometryTargets(state, geometry, targetGeometry, candidate,
            valid) || !SigmaStrengthenCandidateMass(candidate, mass, targetMass,
            valid) || !SigmaRevalidateGeometryCandidate(candidate, targetMass,
            joint, valid))
        return false;

    status |= SIGMA_PROPOSAL_ACCEPTED;
    if (changed)
        status |= SIGMA_PROPOSAL_CHANGED;
    return true;
}

bool SigmaLiftNullDepthMeet(SigmaDepthCell3 left, SigmaDepthCell3 right,
    out uint2 state[16])
{
    [loop]
    for (uint initialLane = 0u; initialLane < 16u; ++initialLane)
        state[initialLane] = SIGMA_Q48_ZERO;
    uint valid = left.valid & right.valid;
    bool independentStereo = left.sourceClass == SIGMA_SOURCE_DEPTH_LEFT &&
        right.sourceClass == SIGMA_SOURCE_DEPTH_RIGHT &&
        left.independenceKey != 0u && right.independenceKey != 0u &&
        left.independenceKey != right.independenceKey;
    bool inclusiveFirstHits = left.sector == SIGMA_SECTOR_HIT &&
        right.sector == SIGMA_SECTOR_HIT;
    if (!independentStereo || !inclusiveFirstHits)
        valid = 0u;
    SigmaQ48Bounds joint[3];
    uint2 maximumWidth = SIGMA_Q48_ZERO;
    [loop]
    for (uint axis = 0u; axis < 3u; ++axis)
    {
        joint[axis].lo = SigmaQ48Max(left.axis[axis].lo, right.axis[axis].lo);
        joint[axis].hi = SigmaQ48Min(left.axis[axis].hi, right.axis[axis].hi);
        if (SigmaQ48Less(joint[axis].hi, joint[axis].lo))
            valid = 0u;
        else
            maximumWidth = SigmaQ48Max(maximumWidth,
                SigmaQ48SubChecked(joint[axis].hi, joint[axis].lo, valid));
    }
    if (valid == 0u)
        return false;

    uint2 mass = SigmaInformationMassForWidth(maximumWidth, valid);

    uint2 geometry[4];
    geometry[0] = SIGMA_Q48_ONE;
    [loop]
    for (uint axis = 0u; axis < 3u; ++axis)
        geometry[axis + 1u] =
            SigmaQ48Midpoint(joint[axis].lo, joint[axis].hi, valid);

    [loop]
    for (uint lane = 0u; lane < 16u; ++lane)
    {
        uint2 projective = SIGMA_Q48_ZERO;
        [loop]
        for (uint rowIndex = 0u; rowIndex < 4u; ++rowIndex)
        {
            uint row = SIGMA_GEOMETRY_ROWS[rowIndex];
            uint2 term = SigmaHadamardSign(row, lane) < 0
                ? SigmaQ48NegateChecked(geometry[rowIndex], valid)
                : geometry[rowIndex];
            projective = SigmaQ48AddChecked(projective, term, valid);
        }
        projective = SigmaQ48ShiftRightNearestEven(projective, 4u, valid);
        state[lane] = SigmaQ48MulNearestEven(mass, projective, valid);
    }

    uint2 check[4];
    SigmaGeometryGPlan(state, check, valid);
    uint2 checkMass = check[0];
    if (valid == 0u || !SigmaQ48Positive(checkMass))
        return false;
    [loop]
    for (uint axis = 0u; axis < 3u; ++axis)
    {
        uint2 coordinate = SigmaQ48DivNearestEven(
            check[axis + 1u], checkMass, valid);
        uint2 oneLsb = uint2(1u, 0u);
        uint2 lo = SigmaQ48SubChecked(joint[axis].lo, oneLsb, valid);
        uint2 hi = SigmaQ48AddChecked(joint[axis].hi, oneLsb, valid);
        if (valid == 0u || SigmaQ48Less(coordinate, lo) ||
            SigmaQ48Less(hi, coordinate))
            return false;
    }
    return true;
}

#endif
