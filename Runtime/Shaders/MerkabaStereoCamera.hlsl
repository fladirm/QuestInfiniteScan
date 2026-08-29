#ifndef MERKABA_STEREO_CAMERA_INCLUDED
#define MERKABA_STEREO_CAMERA_INCLUDED

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

float2 MerkabaProjectCameraUvCore(float3 worldPosition,
    float3 cameraPosition, float4x4 cameraInverseRotation,
    float2 focalLength, float2 principalPoint, float2 sensorResolution,
    float2 currentResolution)
{
    float3 local = mul(cameraInverseRotation,
        float4(worldPosition - cameraPosition, 1.0)).xyz;
    if (local.z <= 0.001) return float2(-1.0, -1.0);

    float2 sensorPoint = float2(
        local.x / local.z * focalLength.x + principalPoint.x,
        local.y / local.z * focalLength.y + principalPoint.y);
    float2 scale = currentResolution / sensorResolution;
    scale /= max(scale.x, scale.y);
    float2 cropMin = sensorResolution * (1.0 - scale) * 0.5;
    float2 cropSize = sensorResolution * scale;
    return (sensorPoint - cropMin) / cropSize;
}

float2 MerkabaProjectCameraUv(uint eye, float3 worldPosition)
{
    if (eye == 0u)
        return MerkabaProjectCameraUvCore(worldPosition,
            _MerkabaCameraPositionLeft, _MerkabaCameraInverseRotationLeft,
            _MerkabaCameraFocalLengthLeft,
            _MerkabaCameraPrincipalPointLeft,
            _MerkabaCameraSensorResolutionLeft,
            _MerkabaCameraCurrentResolutionLeft);
    return MerkabaProjectCameraUvCore(worldPosition,
        _MerkabaCameraPositionRight, _MerkabaCameraInverseRotationRight,
        _MerkabaCameraFocalLengthRight,
        _MerkabaCameraPrincipalPointRight,
        _MerkabaCameraSensorResolutionRight,
        _MerkabaCameraCurrentResolutionRight);
}

float3 MerkabaSampleCameraRgb(uint eye, float2 uv)
{
    if (eye == 0u)
        return _MerkabaCameraRgbLeft.SampleLevel(
            gsBilinearClampSampler, uv, 0).rgb;
    return _MerkabaCameraRgbRight.SampleLevel(
        gsBilinearClampSampler, uv, 0).rgb;
}

#endif
