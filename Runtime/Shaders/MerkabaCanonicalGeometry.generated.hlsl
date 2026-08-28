// GENERATED from MerkabaCanonicalGeometry.cs. DO NOT EDIT.
#ifndef GENESIS_MERKABA_CANONICAL_GEOMETRY_INCLUDED
#define GENESIS_MERKABA_CANONICAL_GEOMETRY_INCLUDED

#define MERKABA_DIRECTION_COUNT 8
#define MERKABA_CANONICAL_VERTEX_COUNT 14
#define MERKABA_CANONICAL_PRIMITIVE_COUNT 32
#define MERKABA_PRIMITIVES_PER_DIRECTION 4
#define MERKABA_VERTICES_PER_PRIMITIVE 3
#define MERKABA_CANONICAL_UNIT 0.025

static const int3 kMerkabaBodyDiagonalOffsets[MERKABA_DIRECTION_COUNT] =
{
    int3(-1, -1, -1),
    int3(1, -1, -1),
    int3(-1, 1, -1),
    int3(1, 1, -1),
    int3(-1, -1, 1),
    int3(1, -1, 1),
    int3(-1, 1, 1),
    int3(1, 1, 1)
};

static const int3 kMerkabaCanonicalVertexUnits[MERKABA_CANONICAL_VERTEX_COUNT] =
{
    int3(-1, 0, 0),
    int3(1, 0, 0),
    int3(0, -1, 0),
    int3(0, 1, 0),
    int3(0, 0, -1),
    int3(0, 0, 1),
    int3(-1, -1, -1),
    int3(1, -1, -1),
    int3(-1, 1, -1),
    int3(1, 1, -1),
    int3(-1, -1, 1),
    int3(1, -1, 1),
    int3(-1, 1, 1),
    int3(1, 1, 1)
};

// xyz = vertex indices, w = (direction << 1) | tip-side flag.
static const uint4 kMerkabaCanonicalPrimitives[MERKABA_CANONICAL_PRIMITIVE_COUNT] =
{
    uint4(0u, 4u, 2u, 0u),
    uint4(6u, 0u, 4u, 1u),
    uint4(6u, 4u, 2u, 1u),
    uint4(6u, 2u, 0u, 1u),
    uint4(1u, 2u, 4u, 2u),
    uint4(7u, 1u, 2u, 3u),
    uint4(7u, 2u, 4u, 3u),
    uint4(7u, 4u, 1u, 3u),
    uint4(0u, 3u, 4u, 4u),
    uint4(8u, 0u, 3u, 5u),
    uint4(8u, 3u, 4u, 5u),
    uint4(8u, 4u, 0u, 5u),
    uint4(1u, 4u, 3u, 6u),
    uint4(9u, 1u, 4u, 7u),
    uint4(9u, 4u, 3u, 7u),
    uint4(9u, 3u, 1u, 7u),
    uint4(0u, 2u, 5u, 8u),
    uint4(10u, 0u, 2u, 9u),
    uint4(10u, 2u, 5u, 9u),
    uint4(10u, 5u, 0u, 9u),
    uint4(1u, 5u, 2u, 10u),
    uint4(11u, 1u, 5u, 11u),
    uint4(11u, 5u, 2u, 11u),
    uint4(11u, 2u, 1u, 11u),
    uint4(0u, 5u, 3u, 12u),
    uint4(12u, 0u, 5u, 13u),
    uint4(12u, 5u, 3u, 13u),
    uint4(12u, 3u, 0u, 13u),
    uint4(1u, 3u, 5u, 14u),
    uint4(13u, 1u, 3u, 15u),
    uint4(13u, 3u, 5u, 15u),
    uint4(13u, 5u, 1u, 15u)
};

float3 MerkabaCanonicalVertexPosition(uint vertexIndex)
{
    return (float3)kMerkabaCanonicalVertexUnits[vertexIndex] * MERKABA_CANONICAL_UNIT;
}

void MerkabaCanonicalPrimitiveVertex(uint primitiveId, uint corner, out float3 position, out float3 normal)
{
    uint4 primitive = kMerkabaCanonicalPrimitives[primitiveId];
    uint vertexIndex = corner == 0u ? primitive.x : (corner == 1u ? primitive.y : primitive.z);
    float3 a = MerkabaCanonicalVertexPosition(primitive.x);
    float3 b = MerkabaCanonicalVertexPosition(primitive.y);
    float3 c = MerkabaCanonicalVertexPosition(primitive.z);
    position = MerkabaCanonicalVertexPosition(vertexIndex);
    normal = normalize(cross(b - a, c - a));
}

#endif
