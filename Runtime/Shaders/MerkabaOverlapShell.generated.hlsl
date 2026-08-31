// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.
#ifndef GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED
#define GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED

#include "MerkabaSurfaceOrientation.generated.hlsl"

#define M8_OVERLAP_TRIANGLES_PER_PATCH 2u
#define M8_OVERLAP_PATCH_HALF_EXTENT 0.025

struct M8OverlapPatch
{
    float3 corner00;
    float3 corner10;
    float3 corner11;
    float3 corner01;
    uint packedColor;
};

void M8MeasuredPlaneTangentBasis(float3 normal,
    out float3 tangent0, out float3 tangent1)
{
    float3 absolute = abs(normal);
    float3 helper = absolute.x <= absolute.y && absolute.x <= absolute.z
        ? float3(1.0, 0.0, 0.0)
        : absolute.y <= absolute.z
            ? float3(0.0, 1.0, 0.0)
            : float3(0.0, 0.0, 1.0);
    tangent0 = normalize(cross(normal, helper));
    float first = tangent0.x != 0.0 ? tangent0.x :
        tangent0.y != 0.0 ? tangent0.y : tangent0.z;
    if (first < 0.0) tangent0 = -tangent0;
    tangent1 = normalize(cross(normal, tangent0));
}

bool M8TryBuildMeasuredPlanePatch(int3 globalCoord, KernelState state,
    out M8OverlapPatch patch)
{
    patch = (M8OverlapPatch)0;
    if ((state.flags & MERKABA_OCCUPIED_FLAG) == 0u ||
        !M8HasSurfacePlane(state.flags))
        return false;
    float3 normal = float3(1.0, 0.0, 0.0);
    float signedOffset = 0.0;
    M8DecodeSurfacePlane(state.flags, normal, signedOffset);
    float3 tangent0 = float3(0.0, 1.0, 0.0);
    float3 tangent1 = float3(0.0, 0.0, 1.0);
    M8MeasuredPlaneTangentBasis(normal, tangent0, tangent1);
    float3 center = (float3)globalCoord * MERKABA_LATTICE_STEP +
        normal * signedOffset;
    float3 extent0 = tangent0 * M8_OVERLAP_PATCH_HALF_EXTENT;
    float3 extent1 = tangent1 * M8_OVERLAP_PATCH_HALF_EXTENT;
    patch.corner00 = center - extent0 - extent1;
    patch.corner10 = center + extent0 - extent1;
    patch.corner11 = center + extent0 + extent1;
    patch.corner01 = center - extent0 + extent1;
    patch.packedColor = state.packedColor;
    return true;
}

float3 M8OverlapPatchCorner(M8OverlapPatch patch, uint corner)
{
    if (corner == 0u) return patch.corner00;
    if (corner == 1u) return patch.corner10;
    if (corner == 2u) return patch.corner11;
    return patch.corner01;
}

uint M8OverlapTriangleCorner(uint vertex)
{
    if (vertex == 0u || vertex == 3u) return 0u;
    if (vertex == 1u) return 1u;
    if (vertex == 2u || vertex == 4u) return 2u;
    return 3u;
}

#endif
