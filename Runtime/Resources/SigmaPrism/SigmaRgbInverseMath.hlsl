#ifndef SIGMA_RGB_INVERSE_MATH_INCLUDED
#define SIGMA_RGB_INVERSE_MATH_INCLUDED

#include "SigmaConstraintPrior.hlsl"

// Exact S4-06 lowering.  Sensor filtering/projection is disposable FP proposal
// work; RGB values cross the canonical decision boundary exactly once through
// SigmaQ48FromFloatNearestEven.  Every cell contraction and accept/reject after
// that point is packed Q16.48.

#define SIGMA_RGB_VIEW_COUNT 27u
#define SIGMA_RGB_VIEW_NULL 13u
#define SIGMA_RGB_OPERATOR_ROWS 4u
#define SIGMA_RGB_CAL_STRIDE 8u
#define SIGMA_RGB_CAL_CAMERA_X 0u
#define SIGMA_RGB_CAL_CAMERA_Y 1u
#define SIGMA_RGB_CAL_CAMERA_Z 2u
#define SIGMA_RGB_CAL_BASE_WIDTH 3u
#define SIGMA_RGB_CAL_SUPPORT_FLOOR 4u
#define SIGMA_RGB_CAL_POSE_WIDTH 5u
#define SIGMA_RGB_CAL_FOOTPRINT_WIDTH 6u

Texture2D<float4> _RgbLeft;
Texture2D<float4> _RgbRight;
StructuredBuffer<uint2> _RgbViewOperators;
StructuredBuffer<uint> _RgbViewSupportScale;
StructuredBuffer<uint2> _RgbCalibrationQ48;

float4x4 _RgbOpticalFromWorldLeft;
float4x4 _RgbOpticalFromWorldRight;
float4 _RgbIntrinsicsLeft;
float4 _RgbIntrinsicsRight;
uint2 _RgbResolutionLeft;
uint2 _RgbResolutionRight;
uint _RgbLeftIndependenceKey;
uint _RgbRightIndependenceKey;
uint _RgbPhase;

uint2 SigmaRgbCalibrationValue(uint eye, uint field)
{
    return _RgbCalibrationQ48[eye * SIGMA_RGB_CAL_STRIDE + field];
}

uint SigmaRgbOperatorAddress(uint direction, uint row, uint lane)
{
    return (direction * SIGMA_RGB_OPERATOR_ROWS + row) * 16u + lane;
}

uint2 SigmaRgbOperator(uint direction, uint row, uint lane)
{
    return _RgbViewOperators[SigmaRgbOperatorAddress(direction, row, lane)];
}

uint2 SigmaRgbCeilHalfMagnitude(uint2 magnitude, inout uint valid)
{
    uint roundUp = magnitude.x & 1u;
    uint2 half = SigmaU64ShiftRight(magnitude, 1u);
    if (roundUp != 0u)
        half = SigmaQ48AddChecked(half, uint2(1u, 0u), valid);
    return half;
}

int SigmaRgbQuantizedComponent(uint2 component, uint2 maximum,
    inout uint valid)
{
    uint2 magnitude = SigmaU64AbsSigned(component);
    uint2 threshold = SigmaRgbCeilHalfMagnitude(maximum, valid);
    if (SigmaU64Less(magnitude, threshold))
        return 0;
    return (component.y & 0x80000000u) != 0u ? -1 : 1;
}

uint SigmaRgbQuantizedDirection(uint2 worldX, uint2 worldY, uint2 worldZ, uint eye,
    inout uint valid)
{
    uint2 direction[3];
    direction[0] = SigmaQ48SubChecked(
        SigmaRgbCalibrationValue(eye, SIGMA_RGB_CAL_CAMERA_X),
        worldX, valid);
    direction[1] = SigmaQ48SubChecked(
        SigmaRgbCalibrationValue(eye, SIGMA_RGB_CAL_CAMERA_Y),
        worldY, valid);
    direction[2] = SigmaQ48SubChecked(
        SigmaRgbCalibrationValue(eye, SIGMA_RGB_CAL_CAMERA_Z),
        worldZ, valid);
    uint2 maximum = SigmaQ48Max(SigmaU64AbsSigned(direction[0]),
        SigmaQ48Max(SigmaU64AbsSigned(direction[1]),
            SigmaU64AbsSigned(direction[2])));
    if (valid == 0u || SigmaU64Equal(maximum, SIGMA_Q48_ZERO))
        return SIGMA_RGB_VIEW_NULL;
    int x = SigmaRgbQuantizedComponent(direction[0], maximum, valid);
    int y = SigmaRgbQuantizedComponent(direction[1], maximum, valid);
    int z = SigmaRgbQuantizedComponent(direction[2], maximum, valid);
    return (uint)((z + 1) * 9 + (y + 1) * 3 + x + 1);
}

bool SigmaProjectWorldToRgb(float3 worldPosition, uint eye,
    out float2 pixel)
{
    float4x4 opticalFromWorld = eye == 0u
        ? _RgbOpticalFromWorldLeft : _RgbOpticalFromWorldRight;
    float4 intrinsics = eye == 0u ? _RgbIntrinsicsLeft : _RgbIntrinsicsRight;
    uint2 resolution = eye == 0u ? _RgbResolutionLeft : _RgbResolutionRight;
    float3 optical = mul(opticalFromWorld, float4(worldPosition, 1.0)).xyz;
    if (!all(isfinite(optical)) || optical.z <= 1e-6)
    {
        pixel = 0.0;
        return false;
    }
    pixel = optical.xy / optical.z * intrinsics.xy + intrinsics.zw;
    return all(pixel >= 0.5) && all(pixel <= (float2)resolution - 1.5);
}

float4 SigmaRgbLoad(uint eye, int2 pixel)
{
    return eye == 0u ? _RgbLeft.Load(int3(pixel, 0)) :
        _RgbRight.Load(int3(pixel, 0));
}

// One source produces one finite-footprint colour interval.  The bilinear centre
// retains subpixel phase; the four represented sensor samples define a
// conservative local hull and therefore cannot manufacture finer bandwidth than
// the calibrated image footprint.
bool SigmaBuildRgbMeasurement(uint eye, float2 pixel,
    out SigmaQ48Bounds rgb[3], inout uint valid)
{
    int2 p00 = int2(floor(pixel - 0.5));
    float2 phase = frac(pixel - 0.5);
    float4 c00 = SigmaRgbLoad(eye, p00);
    float4 c10 = SigmaRgbLoad(eye, p00 + int2(1, 0));
    float4 c01 = SigmaRgbLoad(eye, p00 + int2(0, 1));
    float4 c11 = SigmaRgbLoad(eye, p00 + int2(1, 1));
    if (!all(isfinite(c00)) || !all(isfinite(c10)) ||
        !all(isfinite(c01)) || !all(isfinite(c11)))
        return false;
    float4 centre = lerp(lerp(c00, c10, phase.x),
        lerp(c01, c11, phase.x), phase.y);
    float4 minimum = min(min(c00, c10), min(c01, c11));
    float4 maximum = max(max(c00, c10), max(c01, c11));
    uint2 baseWidth = SigmaQ48AddChecked(
        SigmaRgbCalibrationValue(eye, SIGMA_RGB_CAL_BASE_WIDTH),
        SigmaRgbCalibrationValue(eye, SIGMA_RGB_CAL_POSE_WIDTH), valid);
    baseWidth = SigmaQ48AddChecked(baseWidth,
        SigmaRgbCalibrationValue(eye, SIGMA_RGB_CAL_FOOTPRINT_WIDTH), valid);
    uint2 oneLsb = uint2(1u, 0u);
    [unroll]
    for (uint channel = 0u; channel < 3u; ++channel)
    {
        uint2 centreQ = SigmaQ48FromFloatNearestEven(
            saturate(centre[channel]), valid);
        float hullRadius = max(abs(centre[channel] - minimum[channel]),
            abs(maximum[channel] - centre[channel]));
        uint2 hullQ = SigmaQ48FromFloatNearestEven(hullRadius, valid);
        uint2 width = SigmaQ48AddChecked(baseWidth, hullQ, valid);
        width = SigmaQ48AddChecked(width, oneLsb, valid);
        rgb[channel] = SigmaQ48Widen(centreQ, width, valid);
        rgb[channel].lo = SigmaQ48Max(rgb[channel].lo, SIGMA_Q48_ZERO);
        rgb[channel].hi = SigmaQ48Min(rgb[channel].hi, SIGMA_Q48_ONE);
    }
    return valid != 0u;
}

uint2 SigmaRgbSignedOperator(uint direction, uint row, uint lane,
    bool negate, inout uint valid)
{
    uint2 value = SigmaRgbOperator(direction, row, lane);
    return negate ? SigmaQ48NegateChecked(value, valid) : value;
}

uint2 SigmaRgbInequalityCoefficient(uint constraint, uint direction,
    uint lane, SigmaQ48Bounds rgbR, SigmaQ48Bounds rgbG,
    SigmaQ48Bounds rgbB, bool negativeDenominator,
    inout uint valid)
{
    if (constraint == 0u)
        return SigmaRgbSignedOperator(direction, 0u, lane,
            !negativeDenominator, valid);

    uint channel = (constraint - 1u) >> 1u;
    bool high = ((constraint - 1u) & 1u) != 0u;
    SigmaQ48Bounds channelBounds = rgbR;
    if (channel == 1u)
        channelBounds = rgbG;
    else if (channel == 2u)
        channelBounds = rgbB;
    uint2 colourBound = high ? channelBounds.hi : channelBounds.lo;
    uint2 denominator = SigmaRgbSignedOperator(direction, 0u, lane,
        negativeDenominator, valid);
    uint2 colour = SigmaRgbOperator(direction, channel + 1u, lane);
    uint2 product = SigmaQ48MulNearestEven(colourBound, denominator, valid);
    return high ? SigmaQ48SubChecked(colour, product, valid)
        : SigmaQ48SubChecked(product, colour, valid);
}

void SigmaJointMeetCoordinate(SigmaQ48Bounds incoming, uint source,
    uint lane, inout SigmaQ48Bounds joint[16], inout uint loSource[16],
    inout uint hiSource[16])
{
    bool takeLo = SigmaQ48Less(joint[lane].lo, incoming.lo) ||
        (SigmaU64Equal(joint[lane].lo, incoming.lo) &&
            source < loSource[lane]);
    bool takeHi = SigmaQ48Less(incoming.hi, joint[lane].hi) ||
        (SigmaU64Equal(incoming.hi, joint[lane].hi) &&
            source < hiSource[lane]);
    if (takeLo)
    {
        joint[lane].lo = incoming.lo;
        loSource[lane] = source;
    }
    if (takeHi)
    {
        joint[lane].hi = incoming.hi;
        hiSource[lane] = source;
    }
}

void SigmaAccumulateIndependentKey(uint key, inout uint firstKey,
    inout bool independent)
{
    if (key == 0u)
        return;
    if (firstKey == 0u)
        firstKey = key;
    else if (key != firstKey)
        independent = true;
}

bool SigmaRevalidateJointCandidate(uint2 candidate[16], uint2 expectedMass,
    SigmaQ48Bounds accepted[16], uint constrainedMask, inout uint valid)
{
    uint2 transformed[16];
    SigmaHadamardBPlan(candidate, transformed, valid);
    uint2 mass = transformed[SIGMA_GEOMETRY_ROWS[0]];
    uint2 oneLsb = uint2(1u, 0u);
    if (valid == 0u || !SigmaQ48Positive(mass) ||
        SigmaQ48Less(oneLsb, SigmaQ48AbsDifference(mass, expectedMass, valid)))
        return false;
    [loop]
    for (uint lane = 0u; lane < 16u; ++lane)
    {
        if ((constrainedMask & (1u << lane)) == 0u)
            continue;
        uint2 coordinate = SigmaQ48DivNearestEven(transformed[lane], mass,
            valid);
        uint2 lo = SigmaQ48SubChecked(accepted[lane].lo, oneLsb, valid);
        uint2 hi = SigmaQ48AddChecked(accepted[lane].hi, oneLsb, valid);
        if (valid == 0u || SigmaQ48Less(coordinate, lo) ||
            SigmaQ48Less(hi, coordinate))
            return false;
    }
    return true;
}

// One exact fusion primitive consumes already-constructed independent source
// cells. Source-cell construction is a disposable hyperlinearized GPU stage;
// only this meet may mutate the single canonical Psi state.
bool SigmaSolveJointCells(uint2 state[16],
    SigmaCarrierPageMetaGpu metadata, uint sample, SigmaDepthCell3 depthLeft,
    SigmaDepthCell3 depthRight, bool scheduleRgb,
    SigmaAdmissibleCell16 rgbLeftEvidence,
    SigmaAdmissibleCell16 rgbRightEvidence, uint rgbUnobservableMask,
    out uint2 candidate[16], out SigmaQ48Bounds accepted[16],
    out uint constrainedMask, out uint conflictMask,
    out uint loSource[16], out uint hiSource[16], out uint2 gaps[16],
    out uint status)
{
    status = 0u;
    constrainedMask = 0u;
    conflictMask = 0u;
    uint valid = 1u;
    uint2 transformed[16];
    uint2 currentY[16];
    SigmaHadamardBPlan(state, transformed, valid);
    uint2 mass = transformed[SIGMA_GEOMETRY_ROWS[0]];
    if (valid == 0u || !SigmaQ48Positive(mass))
        return false;
    uint2 priorWidth = SigmaPriorHalfWidth(mass, valid);
    SigmaQ48Bounds prior[16];
    [loop]
    for (uint lane = 0u; lane < 16u; ++lane)
    {
        currentY[lane] = SigmaQ48DivNearestEven(transformed[lane], mass,
            valid);
        prior[lane] = SigmaQ48Widen(currentY[lane], priorWidth, valid);
        accepted[lane] = prior[lane];
        loSource[lane] = SIGMA_SOURCE_PRIOR;
        hiSource[lane] = SIGMA_SOURCE_PRIOR;
        gaps[lane] = SIGMA_Q48_ZERO;
        candidate[lane] = state[lane];
    }
    if (valid == 0u || !SigmaU64Equal(
            currentY[SIGMA_GEOMETRY_ROWS[0]], SIGMA_Q48_ONE))
        return false;
    if (!SigmaApplyConstraintPrior(metadata, sample, prior))
        return false;
    [loop]
    for (uint priorLane = 0u; priorLane < 16u; ++priorLane)
        accepted[priorLane] = prior[priorLane];

    if (depthLeft.valid != 0u && depthLeft.sector == SIGMA_SECTOR_HIT)
    {
        [unroll]
        for (uint axis = 0u; axis < 3u; ++axis)
        {
            uint lane = SIGMA_GEOMETRY_ROWS[axis + 1u];
            SigmaJointMeetCoordinate(depthLeft.axis[axis],
                SIGMA_SOURCE_DEPTH_LEFT, lane, accepted, loSource, hiSource);
            constrainedMask |= 1u << lane;
        }
        status |= SIGMA_PROPOSAL_HIT_LEFT;
    }
    if (depthRight.valid != 0u && depthRight.sector == SIGMA_SECTOR_HIT)
    {
        [unroll]
        for (uint axis = 0u; axis < 3u; ++axis)
        {
            uint lane = SIGMA_GEOMETRY_ROWS[axis + 1u];
            SigmaJointMeetCoordinate(depthRight.axis[axis],
                SIGMA_SOURCE_DEPTH_RIGHT, lane, accepted, loSource, hiSource);
            constrainedMask |= 1u << lane;
        }
        status |= SIGMA_PROPOSAL_HIT_RIGHT;
    }

    if (scheduleRgb)
    {
        bool hasLeft = rgbLeftEvidence.valid != 0u &&
            rgbLeftEvidence.coordinateMask != 0u;
        bool hasRight = rgbRightEvidence.valid != 0u &&
            rgbRightEvidence.coordinateMask != 0u;
        if (rgbUnobservableMask != 0u)
            status |= SIGMA_PROPOSAL_RGB_UNOBSERVABLE;
        if (hasLeft)
        {
            [loop]
            for (uint lane = 0u; lane < 16u; ++lane)
            {
                if ((rgbLeftEvidence.coordinateMask & (1u << lane)) == 0u)
                    continue;
                SigmaJointMeetCoordinate(rgbLeftEvidence.coordinate[lane],
                    SIGMA_SOURCE_RGB_LEFT, lane, accepted, loSource, hiSource);
                constrainedMask |= 1u << lane;
            }
            status |= SIGMA_PROPOSAL_RGB_LEFT;
        }
        if (hasRight)
        {
            [loop]
            for (uint lane = 0u; lane < 16u; ++lane)
            {
                if ((rgbRightEvidence.coordinateMask & (1u << lane)) == 0u)
                    continue;
                SigmaJointMeetCoordinate(rgbRightEvidence.coordinate[lane],
                    SIGMA_SOURCE_RGB_RIGHT, lane, accepted, loSource, hiSource);
                constrainedMask |= 1u << lane;
            }
            status |= SIGMA_PROPOSAL_RGB_RIGHT;
        }
    }

    if (constrainedMask == 0u || valid == 0u)
        return false;
    [loop]
    for (uint conflictLane = 0u; conflictLane < 16u; ++conflictLane)
    {
        if (SigmaQ48Less(accepted[conflictLane].hi,
                accepted[conflictLane].lo))
        {
            conflictMask |= 1u << conflictLane;
            gaps[conflictLane] = SigmaQ48SubChecked(
                accepted[conflictLane].lo, accepted[conflictLane].hi, valid);
        }
    }
    if (conflictMask != 0u || valid == 0u)
    {
        status |= SIGMA_PROPOSAL_CONFLICT;
        return false;
    }

    uint independentCoordinateMask = 0u;
    [loop]
    for (uint keyLane = 0u; keyLane < 16u; ++keyLane)
    {
        if ((constrainedMask & (1u << keyLane)) == 0u)
            continue;
        uint firstKey = 0u;
        bool independent = false;
        bool geometryLane = keyLane == SIGMA_GEOMETRY_ROWS[1] ||
            keyLane == SIGMA_GEOMETRY_ROWS[2] ||
            keyLane == SIGMA_GEOMETRY_ROWS[3];
        if (geometryLane && depthLeft.valid != 0u &&
            depthLeft.sector == SIGMA_SECTOR_HIT)
            SigmaAccumulateIndependentKey(depthLeft.independenceKey,
                firstKey, independent);
        if (geometryLane && depthRight.valid != 0u &&
            depthRight.sector == SIGMA_SECTOR_HIT)
            SigmaAccumulateIndependentKey(depthRight.independenceKey,
                firstKey, independent);
        if ((rgbLeftEvidence.coordinateMask & (1u << keyLane)) != 0u)
            SigmaAccumulateIndependentKey(rgbLeftEvidence.independenceKey,
                firstKey, independent);
        if ((rgbRightEvidence.coordinateMask & (1u << keyLane)) != 0u)
            SigmaAccumulateIndependentKey(rgbRightEvidence.independenceKey,
                firstKey, independent);
        if (independent)
            independentCoordinateMask |= 1u << keyLane;
    }

    uint2 maximumWidth = SIGMA_Q48_ZERO;
    [loop]
    for (uint widthLane = 0u; widthLane < 16u; ++widthLane)
    {
        if ((constrainedMask & (1u << widthLane)) == 0u)
            continue;
        maximumWidth = SigmaQ48Max(maximumWidth,
            SigmaQ48SubChecked(accepted[widthLane].hi,
                accepted[widthLane].lo, valid));
    }
    uint2 targetMass = mass;
    if ((independentCoordinateMask & constrainedMask) == constrainedMask)
        targetMass = SigmaQ48Max(targetMass,
            SigmaInformationMassForWidth(maximumWidth, valid));

    uint2 targetY[16];
    bool changed = SigmaQ48Less(mass, targetMass);
    [loop]
    for (uint targetLane = 0u; targetLane < 16u; ++targetLane)
    {
        targetY[targetLane] = (constrainedMask & (1u << targetLane)) != 0u
            ? SigmaProjectiveClamp(currentY[targetLane],
                accepted[targetLane].lo, accepted[targetLane].hi)
            : currentY[targetLane];
        changed = changed || !SigmaU64Equal(targetY[targetLane],
            currentY[targetLane]);
    }
    if (!changed || valid == 0u)
    {
        status |= SIGMA_PROPOSAL_ACCEPTED;
        return true;
    }

    uint2 inverse[16];
    SigmaHadamardBPlan(targetY, inverse, valid);
    [loop]
    for (uint stateLane = 0u; stateLane < 16u; ++stateLane)
    {
        uint2 projective = SigmaQ48ShiftRightNearestEven(inverse[stateLane],
            4u, valid);
        candidate[stateLane] = SigmaQ48MulNearestEven(targetMass, projective,
            valid);
    }
    if (valid == 0u || !SigmaRevalidateJointCandidate(candidate, targetMass,
            accepted, constrainedMask, valid))
    {
        status |= SIGMA_PROPOSAL_INVALID;
        return false;
    }
    status |= SIGMA_PROPOSAL_ACCEPTED | SIGMA_PROPOSAL_CHANGED;
    return true;
}


uint2 SigmaOwnedRgbCalibrationValue(uint calibrationBase, uint eye,
    uint field)
{
    uint depthCount = SIGMA_DEPTH_VIEW_STRIDE * 2u;
    return _StreamBundleCalibration[calibrationBase + depthCount +
        eye * SIGMA_RGB_CAL_STRIDE + field];
}

float4 SigmaRgbUnpackUnorm4x8(uint packed)
{
    return float4(packed & 255u, (packed >> 8u) & 255u,
        (packed >> 16u) & 255u, (packed >> 24u) & 255u) *
        (1.0 / 255.0);
}

bool SigmaBuildOwnedRgbMeasurement(uint calibrationBase, uint eye,
    uint4 first, uint4 second, out SigmaQ48Bounds rgb[3],
    inout uint valid)
{
    if (first.x == 0xffffffffu || first.y == 0xffffffffu ||
        (second.z & 1u) == 0u)
        return false;
    float2 pixel = float2(asfloat(first.x), asfloat(first.y));
    if (!all(isfinite(pixel)))
        return false;
    float2 phase = frac(pixel - 0.5);
    float4 c00 = SigmaRgbUnpackUnorm4x8(first.z);
    float4 c10 = SigmaRgbUnpackUnorm4x8(first.w);
    float4 c01 = SigmaRgbUnpackUnorm4x8(second.x);
    float4 c11 = SigmaRgbUnpackUnorm4x8(second.y);
    float4 centre = lerp(lerp(c00, c10, phase.x),
        lerp(c01, c11, phase.x), phase.y);
    float4 minimum = min(min(c00, c10), min(c01, c11));
    float4 maximum = max(max(c00, c10), max(c01, c11));

    uint2 baseWidth = SigmaQ48AddChecked(
        SigmaOwnedRgbCalibrationValue(calibrationBase, eye,
            SIGMA_RGB_CAL_BASE_WIDTH),
        SigmaOwnedRgbCalibrationValue(calibrationBase, eye,
            SIGMA_RGB_CAL_POSE_WIDTH), valid);
    baseWidth = SigmaQ48AddChecked(baseWidth,
        SigmaOwnedRgbCalibrationValue(calibrationBase, eye,
            SIGMA_RGB_CAL_FOOTPRINT_WIDTH), valid);
    uint2 oneLsb = uint2(1u, 0u);
    [unroll]
    for (uint channel = 0u; channel < 3u; ++channel)
    {
        uint2 centreQ = SigmaQ48FromFloatNearestEven(
            saturate(centre[channel]), valid);
        float hullRadius = max(abs(centre[channel] - minimum[channel]),
            abs(maximum[channel] - centre[channel]));
        uint2 hullQ = SigmaQ48FromFloatNearestEven(hullRadius, valid);
        uint2 width = SigmaQ48AddChecked(baseWidth, hullQ, valid);
        width = SigmaQ48AddChecked(width, oneLsb, valid);
        rgb[channel] = SigmaQ48Widen(centreQ, width, valid);
        rgb[channel].lo = SigmaQ48Max(rgb[channel].lo, SIGMA_Q48_ZERO);
        rgb[channel].hi = SigmaQ48Min(rgb[channel].hi, SIGMA_Q48_ONE);
    }
    return valid != 0u;
}

uint SigmaOwnedRgbQuantizedDirection(uint calibrationBase, uint2 worldX,
    uint2 worldY, uint2 worldZ, uint eye, inout uint valid)
{
    uint2 direction[3];
    direction[0] = SigmaQ48SubChecked(
        SigmaOwnedRgbCalibrationValue(calibrationBase, eye,
            SIGMA_RGB_CAL_CAMERA_X), worldX, valid);
    direction[1] = SigmaQ48SubChecked(
        SigmaOwnedRgbCalibrationValue(calibrationBase, eye,
            SIGMA_RGB_CAL_CAMERA_Y), worldY, valid);
    direction[2] = SigmaQ48SubChecked(
        SigmaOwnedRgbCalibrationValue(calibrationBase, eye,
            SIGMA_RGB_CAL_CAMERA_Z), worldZ, valid);
    uint2 maximum = SigmaQ48Max(SigmaU64AbsSigned(direction[0]),
        SigmaQ48Max(SigmaU64AbsSigned(direction[1]),
            SigmaU64AbsSigned(direction[2])));
    if (valid == 0u || SigmaU64Equal(maximum, SIGMA_Q48_ZERO))
        return SIGMA_RGB_VIEW_NULL;
    int x = SigmaRgbQuantizedComponent(direction[0], maximum, valid);
    int y = SigmaRgbQuantizedComponent(direction[1], maximum, valid);
    int z = SigmaRgbQuantizedComponent(direction[2], maximum, valid);
    return (uint)((z + 1) * 9 + (y + 1) * 3 + x + 1);
}

#endif
