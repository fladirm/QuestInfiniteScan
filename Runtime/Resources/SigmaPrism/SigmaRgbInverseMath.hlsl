#ifndef SIGMA_RGB_INVERSE_MATH_INCLUDED
#define SIGMA_RGB_INVERSE_MATH_INCLUDED


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

#endif
