#ifndef SIGMA_POSE_CONSUME_INCLUDED
#define SIGMA_POSE_CONSUME_INCLUDED

// Exact pose-gauge proof stays packed Q16.48 in _PoseResult.  These helpers are
// a disposable FP lowering used only to focus association/projection work.  The
// resulting sensor cells still pass the exact inverse meet before Psi mutation.
StructuredBuffer<uint4> _PoseResult;
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
    translation = float3(SigmaPoseQ48Float(SigmaPoseTwistPacked(0u)),
        SigmaPoseQ48Float(SigmaPoseTwistPacked(1u)),
        SigmaPoseQ48Float(SigmaPoseTwistPacked(2u)));
    rotation = float3(SigmaPoseQ48Float(SigmaPoseTwistPacked(3u)),
        SigmaPoseQ48Float(SigmaPoseTwistPacked(4u)),
        SigmaPoseQ48Float(SigmaPoseTwistPacked(5u)));
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
    reference += translation + cross(rotation, reference);
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
    // The exact proof bounds the correction to the small Meta-pose prior.  The
    // first-order inverse is the matching deterministic lowering of the solver's
    // point-to-plane twist model; it never becomes canonical state.
    reference -= translation + cross(rotation, reference);
    return mul(_PoseConsumeWorldFromReference,
        float4(reference, 1.0)).xyz;
}

#endif
