#ifndef SIGMA_POSE_CONSUME_INCLUDED
#define SIGMA_POSE_CONSUME_INCLUDED

// _PoseResult is exact packed Q16.48 proof. These routines are only its
// disposable FP rigid-readout lowering after integer acceptance. They neither
// decide nor persist canonical state.
#ifndef SIGMA_POSE_RESULT_EXTERNAL
StructuredBuffer<uint4> _PoseResult;
#endif

float4x4 _PoseConsumeReferenceFromWorld;
float4x4 _PoseConsumeWorldFromReference;

float SigmaPoseQ48Float(uint2 packed)
{
    return (float)(int)packed.y * (1.0 / 65536.0) +
        (float)packed.x * (1.0 / 281474976710656.0);
}

uint2 SigmaPoseTwistPacked(uint component)
{
    uint4 pair = _PoseResult[1u + (component >> 1u)];
    return (component & 1u) == 0u ? pair.xy : pair.zw;
}

void SigmaPoseTwist(out float3 translation, out float3 rotation)
{
    translation = float3(
        SigmaPoseQ48Float(SigmaPoseTwistPacked(0u)),
        SigmaPoseQ48Float(SigmaPoseTwistPacked(1u)),
        SigmaPoseQ48Float(SigmaPoseTwistPacked(2u)));
    rotation = float3(
        SigmaPoseQ48Float(SigmaPoseTwistPacked(3u)),
        SigmaPoseQ48Float(SigmaPoseTwistPacked(4u)),
        SigmaPoseQ48Float(SigmaPoseTwistPacked(5u)));
}

void SigmaPoseRodriguesCoefficients(float3 rotation,
    out float sineOverAngle, out float oneMinusCosineOverAngleSquared)
{
    float thetaSquared = dot(rotation, rotation);
    if (thetaSquared < 1e-10)
    {
        float thetaFourth = thetaSquared * thetaSquared;
        sineOverAngle = 1.0 - thetaSquared * (1.0 / 6.0) +
            thetaFourth * (1.0 / 120.0);
        oneMinusCosineOverAngleSquared = 0.5 -
            thetaSquared * (1.0 / 24.0) +
            thetaFourth * (1.0 / 720.0);
        return;
    }
    float theta = sqrt(thetaSquared);
    sineOverAngle = sin(theta) / theta;
    oneMinusCosineOverAngleSquared = (1.0 - cos(theta)) / thetaSquared;
}

float3 SigmaPoseRotate(float3 value, float3 rotation, bool inverse)
{
    float sineOverAngle;
    float oneMinusCosineOverAngleSquared;
    SigmaPoseRodriguesCoefficients(rotation, sineOverAngle,
        oneMinusCosineOverAngleSquared);
    float3 first = cross(rotation, value);
    float3 second = cross(rotation, first);
    float signedSine = inverse ? -sineOverAngle : sineOverAngle;
    return value + signedSine * first +
        oneMinusCosineOverAngleSquared * second;
}

float3 SigmaPoseApplyVectorWorld(float3 rawWorldVector)
{
    if (_PoseResult[0u].x == 0u)
        return rawWorldVector;
    float3 translation;
    float3 rotation;
    SigmaPoseTwist(translation, rotation);
    float3 reference = mul((float3x3)_PoseConsumeReferenceFromWorld,
        rawWorldVector);
    reference = SigmaPoseRotate(reference, rotation, false);
    return mul((float3x3)_PoseConsumeWorldFromReference, reference);
}

float3 SigmaPoseUnapplyVectorWorld(float3 correctedWorldVector)
{
    if (_PoseResult[0u].x == 0u)
        return correctedWorldVector;
    float3 translation;
    float3 rotation;
    SigmaPoseTwist(translation, rotation);
    float3 reference = mul((float3x3)_PoseConsumeReferenceFromWorld,
        correctedWorldVector);
    reference = SigmaPoseRotate(reference, rotation, true);
    return mul((float3x3)_PoseConsumeWorldFromReference, reference);
}

float3 SigmaPoseApplyWorld(float3 rawWorld)
{
    if (_PoseResult[0u].x == 0u)
        return rawWorld;
    float3 translation;
    float3 rotation;
    SigmaPoseTwist(translation, rotation);
    float3 reference = mul(_PoseConsumeReferenceFromWorld,
        float4(rawWorld, 1.0)).xyz;
    reference = SigmaPoseRotate(reference, rotation, false) + translation;
    return mul(_PoseConsumeWorldFromReference,
        float4(reference, 1.0)).xyz;
}

float3 SigmaPoseUnapplyWorld(float3 correctedWorld)
{
    if (_PoseResult[0u].x == 0u)
        return correctedWorld;
    float3 translation;
    float3 rotation;
    SigmaPoseTwist(translation, rotation);
    float3 reference = mul(_PoseConsumeReferenceFromWorld,
        float4(correctedWorld, 1.0)).xyz;
    reference = SigmaPoseRotate(reference - translation, rotation, true);
    return mul(_PoseConsumeWorldFromReference,
        float4(reference, 1.0)).xyz;
}

#endif
