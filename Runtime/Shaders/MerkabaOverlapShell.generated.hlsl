// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.
#ifndef GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED
#define GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED

#include "MerkabaSurfaceOrientation.generated.hlsl"

#define M8_OVERLAP_TRIANGLES_PER_PATCH 2u
#define M8_OVERLAP_MAX_CONTRIBUTORS 12u
#define M8_OVERLAP_NEIGHBOUR_COUNT 26u
#define M8_OVERLAP_PATCH_HALF_EXTENT 0.025

struct M8OverlapCorner
{
    float3 gridPosition;
    uint packedColor;
};

struct M8OverlapPatch
{
    M8OverlapCorner corner00;
    M8OverlapCorner corner10;
    M8OverlapCorner corner11;
    M8OverlapCorner corner01;
};

void M8OverlapTangentBasis(uint normalIndex, out float3 normal,
    out float3 tangent0, out float3 tangent1)
{
    normal = normalize((float3)M8CanonicalSurfaceOrientationNormal(normalIndex));
    float3 absolute = abs(normal);
    uint helperIndex = absolute.x <= absolute.y && absolute.x <= absolute.z
        ? 0u : (absolute.y <= absolute.z ? 1u : 2u);
    float3 helper = helperIndex == 0u ? float3(1, 0, 0) :
        (helperIndex == 1u ? float3(0, 1, 0) : float3(0, 0, 1));
    tangent0 = normalize(cross(normal, helper));
    float first = tangent0.x != 0.0 ? tangent0.x :
        (tangent0.y != 0.0 ? tangent0.y : tangent0.z);
    if (first < 0.0) tangent0 = -tangent0;
    tangent1 = normalize(cross(normal, tangent0));
}

int3 M8OverlapNeighbourOffset(uint index)
{
    int3 value = int3(1, 1, 1);
    if (index == 0u) value = int3(-1, -1, -1);
    else if (index == 1u) value = int3(0, -1, -1);
    else if (index == 2u) value = int3(1, -1, -1);
    else if (index == 3u) value = int3(-1, 0, -1);
    else if (index == 4u) value = int3(0, 0, -1);
    else if (index == 5u) value = int3(1, 0, -1);
    else if (index == 6u) value = int3(-1, 1, -1);
    else if (index == 7u) value = int3(0, 1, -1);
    else if (index == 8u) value = int3(1, 1, -1);
    else if (index == 9u) value = int3(-1, -1, 0);
    else if (index == 10u) value = int3(0, -1, 0);
    else if (index == 11u) value = int3(1, -1, 0);
    else if (index == 12u) value = int3(-1, 0, 0);
    else if (index == 13u) value = int3(1, 0, 0);
    else if (index == 14u) value = int3(-1, 1, 0);
    else if (index == 15u) value = int3(0, 1, 0);
    else if (index == 16u) value = int3(1, 1, 0);
    else if (index == 17u) value = int3(-1, -1, 1);
    else if (index == 18u) value = int3(0, -1, 1);
    else if (index == 19u) value = int3(1, -1, 1);
    else if (index == 20u) value = int3(-1, 0, 1);
    else if (index == 21u) value = int3(0, 0, 1);
    else if (index == 22u) value = int3(1, 0, 1);
    else if (index == 23u) value = int3(-1, 1, 1);
    else if (index == 24u) value = int3(0, 1, 1);
    else if (index == 25u) value = int3(1, 1, 1);
    return value;
}

bool M8OverlapDonorContainsCorner(int3 offset, float3 cornerInSteps,
    float3 tangent0, float3 tangent1)
{
    float3 relative = cornerInSteps - (float3)offset;
    return abs(dot(relative, tangent0)) <= 1.0 &&
        abs(dot(relative, tangent1)) <= 1.0;
}

void M8SortOverlapInts(inout int values[M8_OVERLAP_MAX_CONTRIBUTORS],
    uint count)
{
    [loop]
    for (uint index = 1u; index < count; index++)
    {
        int value = values[index];
        uint insert = index;
        [loop]
        while (insert > 0u && value < values[insert - 1u])
        {
            values[insert] = values[insert - 1u];
            insert--;
        }
        values[insert] = value;
    }
}

uint M8MedianOverlapColorChannel(
    uint colors[M8_OVERLAP_MAX_CONTRIBUTORS], uint count, uint shift)
{
    int values[M8_OVERLAP_MAX_CONTRIBUTORS];
    [loop]
    for (uint index = 0u; index < count; index++)
        values[index] = (int)((colors[index] >> shift) & 255u);
    M8SortOverlapInts(values, count);
    int lower = values[(count - 1u) >> 1u];
    int upper = values[count >> 1u];
    return (uint)((lower + upper) >> 1);
}

uint M8MedianOverlapColor(
    uint colors[M8_OVERLAP_MAX_CONTRIBUTORS], uint count,
    uint fallbackColor)
{
    if (count == 0u) return fallbackColor;
    uint red = M8MedianOverlapColorChannel(colors, count, 0u);
    uint green = M8MedianOverlapColorChannel(colors, count, 8u);
    uint blue = M8MedianOverlapColorChannel(colors, count, 16u);
    return red | (green << 8u) | (blue << 16u) | 0xff000000u;
}

M8OverlapCorner M8BuildOrientedOverlapCorner(int3 globalCoord,
    int3 mainHaloCoord, uint orientation, float3 normal, float3 tangent0,
    float3 tangent1, int tangentSign0, int tangentSign1)
{
    KernelState mainState = M8ShellState(mainHaloCoord);
    int heightNumerators[M8_OVERLAP_MAX_CONTRIBUTORS];
    uint colors[M8_OVERLAP_MAX_CONTRIBUTORS];
    uint heightCount = 1u;
    uint colorCount = 0u;
    heightNumerators[0] = 0;
    if (mainState.colorConfidence > 0u)
        colors[colorCount++] = mainState.packedColor;

    float3 cornerInSteps = tangent0 * tangentSign0 +
        tangent1 * tangentSign1;
    int3 integerNormal = M8CanonicalSurfaceOrientationNormal(orientation - 1u);
    [loop]
    for (uint donorIndex = 0u;
         donorIndex < M8_OVERLAP_NEIGHBOUR_COUNT; donorIndex++)
    {
        int3 offset = M8OverlapNeighbourOffset(donorIndex);
        KernelState donor = M8ShellState(mainHaloCoord + offset);
        if ((donor.flags & MERKABA_OCCUPIED_FLAG) == 0u ||
            M8GetSurfaceOrientation(donor.flags) != orientation ||
            !M8OverlapDonorContainsCorner(offset, cornerInSteps,
                tangent0, tangent1))
            continue;
        heightNumerators[heightCount++] = dot(offset, integerNormal);
        if (donor.colorConfidence > 0u)
            colors[colorCount++] = donor.packedColor;
    }

    M8SortOverlapInts(heightNumerators, heightCount);
    int lower = heightNumerators[(heightCount - 1u) >> 1u];
    int upper = heightNumerators[heightCount >> 1u];
    float height = (lower + upper) * 0.5 * MERKABA_LATTICE_STEP /
        length((float3)integerNormal);
    float3 center = (float3)globalCoord * MERKABA_LATTICE_STEP;
    M8OverlapCorner corner;
    corner.gridPosition = center +
        (tangent0 * tangentSign0 + tangent1 * tangentSign1) *
            M8_OVERLAP_PATCH_HALF_EXTENT + normal * height;
    corner.packedColor = M8MedianOverlapColor(colors, colorCount,
        mainState.packedColor);
    return corner;
}

M8OverlapPatch M8BuildOrientedOverlapPatch(int3 globalCoord,
    int3 mainHaloCoord, uint orientation)
{
    float3 normal;
    float3 tangent0;
    float3 tangent1;
    M8OverlapTangentBasis(orientation - 1u, normal, tangent0, tangent1);
    M8OverlapPatch patch;
    patch.corner00 = M8BuildOrientedOverlapCorner(globalCoord,
        mainHaloCoord, orientation, normal, tangent0, tangent1, -1, -1);
    patch.corner10 = M8BuildOrientedOverlapCorner(globalCoord,
        mainHaloCoord, orientation, normal, tangent0, tangent1, 1, -1);
    patch.corner11 = M8BuildOrientedOverlapCorner(globalCoord,
        mainHaloCoord, orientation, normal, tangent0, tangent1, 1, 1);
    patch.corner01 = M8BuildOrientedOverlapCorner(globalCoord,
        mainHaloCoord, orientation, normal, tangent0, tangent1, -1, 1);
    return patch;
}

M8OverlapCorner M8OverlapPatchCorner(M8OverlapPatch patch, uint index)
{
    M8OverlapCorner corner = patch.corner01;
    if (index == 0u) corner = patch.corner00;
    else if (index == 1u) corner = patch.corner10;
    else if (index == 2u) corner = patch.corner11;
    return corner;
}

uint M8OverlapTriangleCorner(uint vertex)
{
    uint index = 3u;
    if (vertex == 0u || vertex == 3u) index = 0u;
    else if (vertex == 1u) index = 1u;
    else if (vertex == 2u || vertex == 4u) index = 2u;
    return index;
}

#endif
