#ifndef MERKABA_STEREO_CAMERA_INCLUDED
#define MERKABA_STEREO_CAMERA_INCLUDED

#include "MerkabaSphereFlowerDataAbi.generated.hlsl"

Texture2D<float4> _MerkabaCameraRgbLeft;
Texture2D<float4> _MerkabaCameraRgbRight;
float3 _MerkabaCameraPositionLeft;
float3 _MerkabaCameraPositionRight;
float4x4 _MerkabaCameraInverseRotationLeft;
float4x4 _MerkabaCameraInverseRotationRight;
float2 _MerkabaCameraFocalLengthLeft;
float2 _MerkabaCameraFocalLengthRight;
float2 _MerkabaCameraPrincipalPointLeft;
float2 _MerkabaCameraPrincipalPointRight;
float2 _MerkabaCameraSensorResolutionLeft;
float2 _MerkabaCameraSensorResolutionRight;
float2 _MerkabaCameraCurrentResolutionLeft;
float2 _MerkabaCameraCurrentResolutionRight;

bool MerkabaProjectCameraCell(float3 worldPosition,
    float3 cameraPosition, float4x4 cameraInverseRotation,
    float2 focalLength, float2 principalPoint, float2 sensorResolution,
    float2 currentResolution, out int2 texel, out float2 fraction)
{
    texel = 0;
    fraction = 0.0.xx;
    if (!all(M8FlowerIsFinite(worldPosition)) || !all(M8FlowerIsFinite(cameraPosition)) ||
        !all(M8FlowerIsFinite(focalLength)) || !all(M8FlowerIsFinite(principalPoint)) ||
        !all(M8FlowerIsFinite(sensorResolution)) || !all(M8FlowerIsFinite(currentResolution)) ||
        any(focalLength <= 0.0) || any(sensorResolution <= 0.0) ||
        any(currentResolution < 2.0)) return false;
    precise float3 local = mul(cameraInverseRotation,
        float4(worldPosition - cameraPosition, 1.0)).xyz;
    if (!all(M8FlowerIsFinite(local)) || !(local.z > 0.0)) return false;

    // Same calibrated sensor crop as the interval Flower-root projection.
    // Pixel coordinates are relative to texel centres, not image edges.
    precise float2 projected = local.xy / local.z;
    precise float2 sensorPoint = projected * focalLength + principalPoint;
    precise float2 scales = currentResolution / sensorResolution;
    precise float scale = max(scales.x, scales.y);
    precise float2 pixel = (sensorPoint - sensorResolution * 0.5) * scale;
    pixel = pixel + currentResolution * 0.5;
    pixel = pixel - 0.5;
    if (!all(M8FlowerIsFinite(pixel)) || any(pixel < 0.0) ||
        any(pixel >= currentResolution - 1.0)) return false;
    texel = int2(floor(pixel));
    fraction = pixel - float2(texel);
    return true;
}

bool MerkabaTrySampleCameraRgb(uint eye, float3 worldPosition, out float3 rgb)
{
    rgb = 0.0.xxx;
    int2 texel;
    float2 fraction;
    uint width, height;
    if (eye == 0u)
    {
        _MerkabaCameraRgbLeft.GetDimensions(width, height);
        if (any(float2(width, height) != _MerkabaCameraCurrentResolutionLeft) ||
            !MerkabaProjectCameraCell(worldPosition,
            _MerkabaCameraPositionLeft, _MerkabaCameraInverseRotationLeft,
            _MerkabaCameraFocalLengthLeft,
            _MerkabaCameraPrincipalPointLeft,
            _MerkabaCameraSensorResolutionLeft,
            _MerkabaCameraCurrentResolutionLeft, texel, fraction)) return false;
    }
    else if (eye == 1u)
    {
        _MerkabaCameraRgbRight.GetDimensions(width, height);
        if (any(float2(width, height) != _MerkabaCameraCurrentResolutionRight) ||
            !MerkabaProjectCameraCell(worldPosition,
        _MerkabaCameraPositionRight, _MerkabaCameraInverseRotationRight,
        _MerkabaCameraFocalLengthRight,
        _MerkabaCameraPrincipalPointRight,
        _MerkabaCameraSensorResolutionRight,
        _MerkabaCameraCurrentResolutionRight, texel, fraction)) return false;
    }
    else return false;

    // No clamp sampler: all four captured texels must exist. This chooses
    // only the central packed-colour representative; stereo validity still
    // comes from the bounded root evidence, never from this scalar sample.
    [unroll] for (uint tap = 0u; tap < 4u; ++tap)
    {
        int3 p = int3(texel + int2(tap & 1u, tap >> 1u), 0);
        float3 captured = eye == 0u ? _MerkabaCameraRgbLeft.Load(p).rgb :
            _MerkabaCameraRgbRight.Load(p).rgb;
        if (!all(M8FlowerIsFinite(captured)) || any(captured < 0.0)) return false;
        precise float wx = (tap & 1u) != 0u ? fraction.x : 1.0 - fraction.x;
        precise float wy = (tap & 2u) != 0u ? fraction.y : 1.0 - fraction.y;
        precise float weight = wx * wy;
        precise float3 contribution = captured * weight;
        rgb = rgb + contribution;
    }
    return all(M8FlowerIsFinite(rgb));
}

#endif
