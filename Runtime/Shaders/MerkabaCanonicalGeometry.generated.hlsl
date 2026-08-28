// GENERATED from MerkabaCanonicalGeometry.cs. DO NOT EDIT.
#ifndef GENESIS_MERKABA_CANONICAL_GEOMETRY_INCLUDED
#define GENESIS_MERKABA_CANONICAL_GEOMETRY_INCLUDED

#define MERKABA_CANONICAL_VERTEX_COUNT 50
#define MERKABA_CANONICAL_PRIMITIVE_COUNT 96
#define MERKABA_VERTICES_PER_PRIMITIVE 3
#define MERKABA_CANONICAL_HALF_UNIT 0.0125

static const int3 kMerkabaNeighbourOffsets[26] =
{
    int3(-1, -1, -1),
    int3(0, -1, -1),
    int3(1, -1, -1),
    int3(-1, 0, -1),
    int3(0, 0, -1),
    int3(1, 0, -1),
    int3(-1, 1, -1),
    int3(0, 1, -1),
    int3(1, 1, -1),
    int3(-1, -1, 0),
    int3(0, -1, 0),
    int3(1, -1, 0),
    int3(-1, 0, 0),
    int3(1, 0, 0),
    int3(-1, 1, 0),
    int3(0, 1, 0),
    int3(1, 1, 0),
    int3(-1, -1, 1),
    int3(0, -1, 1),
    int3(1, -1, 1),
    int3(-1, 0, 1),
    int3(0, 0, 1),
    int3(1, 0, 1),
    int3(-1, 1, 1),
    int3(0, 1, 1),
    int3(1, 1, 1)
};

static const int3 kMerkabaCanonicalVertexHalfUnits[MERKABA_CANONICAL_VERTEX_COUNT] =
{
    int3(-2, -2, -2),
    int3(2, -2, -2),
    int3(-2, 2, -2),
    int3(2, 2, -2),
    int3(-2, -2, 2),
    int3(2, -2, 2),
    int3(-2, 2, 2),
    int3(2, 2, 2),
    int3(-2, 0, 0),
    int3(2, 0, 0),
    int3(0, -2, 0),
    int3(0, 2, 0),
    int3(0, 0, -2),
    int3(0, 0, 2),
    int3(-1, -2, -1),
    int3(-1, -1, 0),
    int3(-2, -1, -1),
    int3(-1, -1, -2),
    int3(0, -1, -1),
    int3(-1, 0, -1),
    int3(2, -1, -1),
    int3(1, -1, 0),
    int3(1, -2, -1),
    int3(1, -1, -2),
    int3(1, 0, -1),
    int3(-2, 1, -1),
    int3(-1, 1, 0),
    int3(-1, 2, -1),
    int3(0, 1, -1),
    int3(-1, 1, -2),
    int3(1, 2, -1),
    int3(1, 1, 0),
    int3(2, 1, -1),
    int3(1, 1, -2),
    int3(-2, -1, 1),
    int3(-1, -2, 1),
    int3(0, -1, 1),
    int3(-1, -1, 2),
    int3(-1, 0, 1),
    int3(1, -2, 1),
    int3(2, -1, 1),
    int3(1, -1, 2),
    int3(1, 0, 1),
    int3(-1, 2, 1),
    int3(-2, 1, 1),
    int3(-1, 1, 2),
    int3(0, 1, 1),
    int3(2, 1, 1),
    int3(1, 2, 1),
    int3(1, 1, 2)
};

// xyz = vertex indices, w = neighbour suppression mask.
static const uint4 kMerkabaCanonicalPrimitives[MERKABA_CANONICAL_PRIMITIVE_COUNT] =
{
    uint4(0u, 14u, 16u, 0x0000020Bu),
    uint4(14u, 10u, 15u, 0x0000060Au),
    uint4(16u, 15u, 8u, 0x0000120Au),
    uint4(14u, 15u, 16u, 0x0000161Bu),
    uint4(0u, 17u, 14u, 0x0000020Bu),
    uint4(17u, 12u, 18u, 0x0000021Au),
    uint4(14u, 18u, 10u, 0x0000060Au),
    uint4(17u, 18u, 14u, 0x0000161Bu),
    uint4(0u, 16u, 17u, 0x0000020Bu),
    uint4(16u, 8u, 19u, 0x0000120Au),
    uint4(17u, 19u, 12u, 0x0000021Au),
    uint4(16u, 19u, 17u, 0x0000161Bu),
    uint4(1u, 20u, 22u, 0x00000806u),
    uint4(20u, 9u, 21u, 0x00002802u),
    uint4(22u, 21u, 10u, 0x00000C02u),
    uint4(20u, 21u, 22u, 0x00002C16u),
    uint4(1u, 22u, 23u, 0x00000006u),
    uint4(22u, 10u, 18u, 0x00000402u),
    uint4(23u, 18u, 12u, 0x00000012u),
    uint4(22u, 18u, 23u, 0x00002416u),
    uint4(1u, 23u, 20u, 0x00000026u),
    uint4(23u, 12u, 24u, 0x00000032u),
    uint4(20u, 24u, 9u, 0x00002022u),
    uint4(23u, 24u, 20u, 0x00002436u),
    uint4(2u, 25u, 27u, 0x00004048u),
    uint4(25u, 8u, 26u, 0x00005008u),
    uint4(27u, 26u, 11u, 0x0000C008u),
    uint4(25u, 26u, 27u, 0x0000D058u),
    uint4(2u, 27u, 29u, 0x000040C8u),
    uint4(27u, 11u, 28u, 0x0000C088u),
    uint4(29u, 28u, 12u, 0x00004098u),
    uint4(27u, 28u, 29u, 0x0000D0D8u),
    uint4(2u, 29u, 25u, 0x00004048u),
    uint4(29u, 12u, 19u, 0x00004018u),
    uint4(25u, 19u, 8u, 0x00005008u),
    uint4(29u, 19u, 25u, 0x0000D058u),
    uint4(3u, 30u, 32u, 0x00010100u),
    uint4(30u, 11u, 31u, 0x00018000u),
    uint4(32u, 31u, 9u, 0x00012000u),
    uint4(30u, 31u, 32u, 0x0001A110u),
    uint4(3u, 33u, 30u, 0x00000180u),
    uint4(33u, 12u, 28u, 0x00000090u),
    uint4(30u, 28u, 11u, 0x00008080u),
    uint4(33u, 28u, 30u, 0x0000A190u),
    uint4(3u, 32u, 33u, 0x00000120u),
    uint4(32u, 9u, 24u, 0x00002020u),
    uint4(33u, 24u, 12u, 0x00000030u),
    uint4(32u, 24u, 33u, 0x0000A130u),
    uint4(4u, 34u, 35u, 0x00160200u),
    uint4(34u, 8u, 15u, 0x00141200u),
    uint4(35u, 15u, 10u, 0x00140600u),
    uint4(34u, 15u, 35u, 0x00361600u),
    uint4(4u, 35u, 37u, 0x00160200u),
    uint4(35u, 10u, 36u, 0x00140600u),
    uint4(37u, 36u, 13u, 0x00340200u),
    uint4(35u, 36u, 37u, 0x00361600u),
    uint4(4u, 37u, 34u, 0x00160200u),
    uint4(37u, 13u, 38u, 0x00340200u),
    uint4(34u, 38u, 8u, 0x00141200u),
    uint4(37u, 38u, 34u, 0x00361600u),
    uint4(5u, 39u, 40u, 0x000C0800u),
    uint4(39u, 10u, 21u, 0x00040C00u),
    uint4(40u, 21u, 9u, 0x00042800u),
    uint4(39u, 21u, 40u, 0x002C2C00u),
    uint4(5u, 41u, 39u, 0x000C0000u),
    uint4(41u, 13u, 36u, 0x00240000u),
    uint4(39u, 36u, 10u, 0x00040400u),
    uint4(41u, 36u, 39u, 0x002C2400u),
    uint4(5u, 40u, 41u, 0x004C0000u),
    uint4(40u, 9u, 42u, 0x00442000u),
    uint4(41u, 42u, 13u, 0x00640000u),
    uint4(40u, 42u, 41u, 0x006C2400u),
    uint4(6u, 43u, 44u, 0x00904000u),
    uint4(43u, 11u, 26u, 0x0010C000u),
    uint4(44u, 26u, 8u, 0x00105000u),
    uint4(43u, 26u, 44u, 0x00B0D000u),
    uint4(6u, 45u, 43u, 0x01904000u),
    uint4(45u, 13u, 46u, 0x01304000u),
    uint4(43u, 46u, 11u, 0x0110C000u),
    uint4(45u, 46u, 43u, 0x01B0D000u),
    uint4(6u, 44u, 45u, 0x00904000u),
    uint4(44u, 8u, 38u, 0x00105000u),
    uint4(45u, 38u, 13u, 0x00304000u),
    uint4(44u, 38u, 45u, 0x00B0D000u),
    uint4(7u, 47u, 48u, 0x02010000u),
    uint4(47u, 9u, 31u, 0x00012000u),
    uint4(48u, 31u, 11u, 0x00018000u),
    uint4(47u, 31u, 48u, 0x0221A000u),
    uint4(7u, 48u, 49u, 0x03000000u),
    uint4(48u, 11u, 46u, 0x01008000u),
    uint4(49u, 46u, 13u, 0x01200000u),
    uint4(48u, 46u, 49u, 0x0320A000u),
    uint4(7u, 49u, 47u, 0x02400000u),
    uint4(49u, 13u, 42u, 0x00600000u),
    uint4(47u, 42u, 9u, 0x00402000u),
    uint4(49u, 42u, 47u, 0x0260A000u)
};

float3 MerkabaCanonicalVertexPosition(uint vertexIndex)
{
    return (float3)kMerkabaCanonicalVertexHalfUnits[vertexIndex] * MERKABA_CANONICAL_HALF_UNIT;
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
