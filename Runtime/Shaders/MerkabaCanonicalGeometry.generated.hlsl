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

void MerkabaCanonicalPrimitiveFacing(uint primitiveId,
    out float3 primitiveCenterOffset, out float3 orientedNormal)
{
    primitiveCenterOffset = 0.0;
    orientedNormal = float3(0, 0, 1);
    switch (primitiveId)
    {
        case 0u:
            primitiveCenterOffset = float3(-1, -1, -1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, -1, -1);
            break;
        case 1u:
            primitiveCenterOffset = float3(-2, -1, -2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, 1, -1);
            break;
        case 2u:
            primitiveCenterOffset = float3(-1, -2, -2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, -1, -1);
            break;
        case 3u:
            primitiveCenterOffset = float3(-2, -2, -1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, -1, 1);
            break;
        case 4u:
            primitiveCenterOffset = float3(1, -1, -1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, -1, -1);
            break;
        case 5u:
            primitiveCenterOffset = float3(2, -2, -1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, -1, 1);
            break;
        case 6u:
            primitiveCenterOffset = float3(1, -2, -2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, -1, -1);
            break;
        case 7u:
            primitiveCenterOffset = float3(2, -1, -2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, 1, -1);
            break;
        case 8u:
            primitiveCenterOffset = float3(-1, 1, -1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, 1, -1);
            break;
        case 9u:
            primitiveCenterOffset = float3(-2, 2, -1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, 1, 1);
            break;
        case 10u:
            primitiveCenterOffset = float3(-1, 2, -2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, 1, -1);
            break;
        case 11u:
            primitiveCenterOffset = float3(-2, 1, -2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, -1, -1);
            break;
        case 12u:
            primitiveCenterOffset = float3(1, 1, -1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, 1, -1);
            break;
        case 13u:
            primitiveCenterOffset = float3(2, 1, -2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, -1, -1);
            break;
        case 14u:
            primitiveCenterOffset = float3(1, 2, -2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, 1, -1);
            break;
        case 15u:
            primitiveCenterOffset = float3(2, 2, -1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, 1, 1);
            break;
        case 16u:
            primitiveCenterOffset = float3(-1, -1, 1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, -1, 1);
            break;
        case 17u:
            primitiveCenterOffset = float3(-2, -2, 1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, -1, -1);
            break;
        case 18u:
            primitiveCenterOffset = float3(-1, -2, 2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, -1, 1);
            break;
        case 19u:
            primitiveCenterOffset = float3(-2, -1, 2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, 1, 1);
            break;
        case 20u:
            primitiveCenterOffset = float3(1, -1, 1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, -1, 1);
            break;
        case 21u:
            primitiveCenterOffset = float3(2, -1, 2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, 1, 1);
            break;
        case 22u:
            primitiveCenterOffset = float3(1, -2, 2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, -1, 1);
            break;
        case 23u:
            primitiveCenterOffset = float3(2, -2, 1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, -1, -1);
            break;
        case 24u:
            primitiveCenterOffset = float3(-1, 1, 1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, 1, 1);
            break;
        case 25u:
            primitiveCenterOffset = float3(-2, 1, 2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, -1, 1);
            break;
        case 26u:
            primitiveCenterOffset = float3(-1, 2, 2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, 1, 1);
            break;
        case 27u:
            primitiveCenterOffset = float3(-2, 2, 1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, 1, -1);
            break;
        case 28u:
            primitiveCenterOffset = float3(1, 1, 1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, 1, 1);
            break;
        case 29u:
            primitiveCenterOffset = float3(2, 2, 1) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, 1, -1);
            break;
        case 30u:
            primitiveCenterOffset = float3(1, 2, 2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(-1, 1, 1);
            break;
        case 31u:
            primitiveCenterOffset = float3(2, 1, 2) * (MERKABA_CANONICAL_UNIT / 3.0);
            orientedNormal = float3(1, -1, 1);
            break;
        default: break;
    }
}

#endif
