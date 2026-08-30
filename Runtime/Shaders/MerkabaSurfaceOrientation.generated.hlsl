// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.
#ifndef GENESIS_MERKABA_SURFACE_ORIENTATION_INCLUDED
#define GENESIS_MERKABA_SURFACE_ORIENTATION_INCLUDED

#define MERKABA_SURFACE_PLANE_NORMAL_U_SHIFT 2u
#define MERKABA_SURFACE_PLANE_NORMAL_V_SHIFT 12u
#define MERKABA_SURFACE_PLANE_OFFSET_SHIFT 22u
#define MERKABA_SURFACE_PLANE_NORMAL_MASK 0x3ffu
#define MERKABA_SURFACE_PLANE_OFFSET_MASK 0xffu
#define MERKABA_SURFACE_PLANE_VALID_FLAG 0x80000000u
#define MERKABA_SURFACE_PLANE_STORAGE_MASK 0xfffffffcu
#define MERKABA_SURFACE_PLANE_OFFSET_RANGE 0.025

bool M8HasSurfacePlane(uint flags)
{
    return (flags & MERKABA_SURFACE_PLANE_VALID_FLAG) != 0u;
}

float2 M8OctSignNotZero(float2 value)
{
    return float2(value.x >= 0.0 ? 1.0 : -1.0,
        value.y >= 0.0 ? 1.0 : -1.0);
}

float M8SurfacePlaneFirstNonZero(float3 value)
{
    return value.x != 0.0 ? value.x :
        value.y != 0.0 ? value.y : value.z;
}

float2 M8OctEncode(float3 normal)
{
    normal /= dot(abs(normal), 1.0.xxx);
    float2 oct = normal.xy;
    if (normal.z < 0.0)
        oct = (1.0 - abs(oct.yx)) * M8OctSignNotZero(oct);
    return oct;
}

float3 M8OctDecode(float2 oct)
{
    float3 normal = float3(oct, 1.0 - abs(oct.x) - abs(oct.y));
    if (normal.z < 0.0)
        normal.xy = (1.0 - abs(normal.yx)) * M8OctSignNotZero(normal.xy);
    return normalize(normal);
}

uint M8SetSurfacePlane(uint flags, float3 normal, float signedOffset)
{
    normal = normalize(normal);
    if (M8SurfacePlaneFirstNonZero(normal) < 0.0)
    {
        normal = -normal;
        signedOffset = -signedOffset;
    }
    float2 oct = M8OctEncode(normal);
    uint encodedU = (uint)clamp(round((oct.x * 0.5 + 0.5) * 1023.0), 0.0, 1023.0);
    uint encodedV = (uint)clamp(round((oct.y * 0.5 + 0.5) * 1023.0), 0.0, 1023.0);
    int encodedOffset = (int)round(clamp(signedOffset /
        MERKABA_SURFACE_PLANE_OFFSET_RANGE, -1.0, 1.0) * 127.0);
    uint payload =
        (encodedU << MERKABA_SURFACE_PLANE_NORMAL_U_SHIFT) |
        (encodedV << MERKABA_SURFACE_PLANE_NORMAL_V_SHIFT) |
        ((uint(encodedOffset) & MERKABA_SURFACE_PLANE_OFFSET_MASK) <<
            MERKABA_SURFACE_PLANE_OFFSET_SHIFT) |
        MERKABA_SURFACE_PLANE_VALID_FLAG;
    return (flags & ~MERKABA_SURFACE_PLANE_STORAGE_MASK) | payload;
}

void M8DecodeSurfacePlane(uint flags, out float3 normal, out float signedOffset)
{
    uint encodedU = (flags >> MERKABA_SURFACE_PLANE_NORMAL_U_SHIFT) &
        MERKABA_SURFACE_PLANE_NORMAL_MASK;
    uint encodedV = (flags >> MERKABA_SURFACE_PLANE_NORMAL_V_SHIFT) &
        MERKABA_SURFACE_PLANE_NORMAL_MASK;
    normal = M8OctDecode(float2(encodedU, encodedV) / 1023.0 * 2.0 - 1.0);
    uint rawOffset = (flags >> MERKABA_SURFACE_PLANE_OFFSET_SHIFT) &
        MERKABA_SURFACE_PLANE_OFFSET_MASK;
    int encodedOffset = rawOffset >= 128u ? int(rawOffset) - 256 : int(rawOffset);
    signedOffset = (float)encodedOffset / 127.0 *
        MERKABA_SURFACE_PLANE_OFFSET_RANGE;
}

uint M8ClearSurfacePlane(uint flags)
{
    return flags & ~MERKABA_SURFACE_PLANE_STORAGE_MASK;
}

#endif
