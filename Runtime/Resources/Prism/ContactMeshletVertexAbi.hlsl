#ifndef GENESIS_CONTACT_MESHLET_VERTEX_ABI_INCLUDED
#define GENESIS_CONTACT_MESHLET_VERTEX_ABI_INCLUDED

// Compact derived vertex ABI. Canonical chart/posterior data remains full
// precision; this representation is consumed only by materialization, prediction,
// preview and detached derived caches.
struct ContactMeshletVertex
{
    float3 position;
    uint packedFilmMaterial;
    uint packedNormal;
    uint generation;
    uint packedUv;
    uint packedSigmaConfidence;
};

float ContactMeshletSignNotZero(float value)
{
    return value >= 0.0 ? 1.0 : -1.0;
}

uint PackContactMeshletSnorm16x2(float2 value)
{
    int2 encoded = (int2)round(clamp(value, -1.0, 1.0) * 32767.0);
    return ((uint)encoded.x & 0xffffu) |
        (((uint)encoded.y & 0xffffu) << 16u);
}

float2 UnpackContactMeshletSnorm16x2(uint packed)
{
    int x = (int)(packed << 16u) >> 16;
    int y = (int)packed >> 16;
    return clamp(float2((float)x, (float)y) / 32767.0, -1.0, 1.0);
}

uint PackContactMeshletNormal(float3 normal)
{
    float inverseL1 = rcp(max(1e-20,
        abs(normal.x) + abs(normal.y) + abs(normal.z)));
    normal *= inverseL1;
    float2 encoded = normal.xy;
    if (normal.z < 0.0)
    {
        encoded = (1.0 - abs(encoded.yx)) * float2(
            ContactMeshletSignNotZero(encoded.x),
            ContactMeshletSignNotZero(encoded.y));
    }
    return PackContactMeshletSnorm16x2(encoded);
}

float3 UnpackContactMeshletNormal(uint packed)
{
    float2 encoded = UnpackContactMeshletSnorm16x2(packed);
    float3 normal = float3(encoded,
        1.0 - abs(encoded.x) - abs(encoded.y));
    if (normal.z < 0.0)
    {
        normal.xy = (1.0 - abs(normal.yx)) * float2(
            ContactMeshletSignNotZero(normal.x),
            ContactMeshletSignNotZero(normal.y));
    }
    return normalize(normal);
}

uint PackContactMeshletHalf2(float2 value)
{
    return (f32tof16(value.x) & 0xffffu) |
        ((f32tof16(value.y) & 0xffffu) << 16u);
}

float2 UnpackContactMeshletHalf2(uint packed)
{
    return float2(f16tof32(packed & 0xffffu),
        f16tof32(packed >> 16u));
}

static const uint CONTACT_MESHLET_FILM_ID_MASK = 0x1ffffu;

uint PackContactMeshletFilmMaterial(uint filmId, uint sidedness, uint flags,
    float coverage)
{
    uint coverageUnorm = (uint)round(saturate(coverage) * 1023.0);
    return (filmId & CONTACT_MESHLET_FILM_ID_MASK) |
        ((flags & 0xfu) << 17u) | ((sidedness & 0x1u) << 21u) |
        (coverageUnorm << 22u);
}

uint ContactMeshletWithFilmId(uint packedFilmMaterial, uint filmId)
{
    return (packedFilmMaterial & ~CONTACT_MESHLET_FILM_ID_MASK) |
        (filmId & CONTACT_MESHLET_FILM_ID_MASK);
}

uint ContactMeshletFilmId(uint packedFilmMaterial)
{
    return packedFilmMaterial & CONTACT_MESHLET_FILM_ID_MASK;
}

uint ContactMeshletFlags(uint packedFilmMaterial)
{
    return (packedFilmMaterial >> 17u) & 0xfu;
}

uint ContactMeshletSidedness(uint packedFilmMaterial)
{
    return (packedFilmMaterial >> 21u) & 0x1u;
}

float ContactMeshletCoverage(uint packedFilmMaterial)
{
    return (float)(packedFilmMaterial >> 22u) / 1023.0;
}

#endif
