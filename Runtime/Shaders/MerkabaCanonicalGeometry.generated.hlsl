// GENERATED from MerkabaCanonicalGeometry.cs. DO NOT EDIT.
#ifndef GENESIS_MERKABA_CANONICAL_GEOMETRY_INCLUDED
#define GENESIS_MERKABA_CANONICAL_GEOMETRY_INCLUDED

#define MERKABA_DIRECTION_COUNT 8
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

// Literal cases avoid nested dynamic static-const indexing on Quest/Vulkan.
float3 MerkabaCanonicalPrimitivePosition(uint primitiveId, uint corner)
{
    float3 a = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
    float3 b = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
    float3 c = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
    switch (primitiveId)
    {
        case 0u:
            a = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            c = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 1u:
            a = float3(-1, -1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            break;
        case 2u:
            a = float3(-1, -1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            c = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 3u:
            a = float3(-1, -1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 4u:
            a = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            b = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            break;
        case 5u:
            a = float3(1, -1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 6u:
            a = float3(1, -1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            break;
        case 7u:
            a = float3(1, -1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            c = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 8u:
            a = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            break;
        case 9u:
            a = float3(-1, 1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 10u:
            a = float3(-1, 1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            break;
        case 11u:
            a = float3(-1, 1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            c = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 12u:
            a = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 13u:
            a = float3(1, 1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            break;
        case 14u:
            a = float3(1, 1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, -1) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 15u:
            a = float3(1, 1, -1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 16u:
            a = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            b = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            break;
        case 17u:
            a = float3(-1, -1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 18u:
            a = float3(-1, -1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            break;
        case 19u:
            a = float3(-1, -1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            c = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 20u:
            a = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            c = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 21u:
            a = float3(1, -1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            break;
        case 22u:
            a = float3(1, -1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            c = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 23u:
            a = float3(1, -1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, -1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 24u:
            a = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 25u:
            a = float3(-1, 1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            break;
        case 26u:
            a = float3(-1, 1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 27u:
            a = float3(-1, 1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(-1, 0, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 28u:
            a = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            break;
        case 29u:
            a = float3(1, 1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            break;
        case 30u:
            a = float3(1, 1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 1, 0) * MERKABA_CANONICAL_UNIT;
            c = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            break;
        case 31u:
            a = float3(1, 1, 1) * MERKABA_CANONICAL_UNIT;
            b = float3(0, 0, 1) * MERKABA_CANONICAL_UNIT;
            c = float3(1, 0, 0) * MERKABA_CANONICAL_UNIT;
            break;
        default: break;
    }
    return corner == 0u ? a : (corner == 1u ? b : c);
}

#endif
