// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.
#ifndef GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED
#define GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED

#include "MerkabaSurfaceOrientation.generated.hlsl"

#define M8_MEMBRANE_TRIANGLES_PER_PATCH 2u
#define M8_MEMBRANE_VERTICES_PER_PATCH 4u
#define M8_MEMBRANE_INDICES_PER_PATCH 6u
#define M8_MEMBRANE_PATCH_PITCH 0.025
#define M8_MEMBRANE_HALF_PITCH 0.0125
#define M8_MEMBRANE_NUMERICAL_EPSILON 1.0e-6

struct M8OverlapPatch
{
    float3 corner00;
    float3 corner10;
    float3 corner11;
    float3 corner01;
    float3 normal;
    uint packedColor;
};

int M8MembraneDominantAxis(float3 normal)
{
    float3 magnitude = abs(normalize(normal));
    return magnitude.x >= magnitude.y && magnitude.x >= magnitude.z ? 0 :
        magnitude.y >= magnitude.z ? 1 : 2;
}

void M8MembraneTangentAxes(int dominantAxis, out int tangentAxis0,
    out int tangentAxis1)
{
    tangentAxis0 = dominantAxis == 0 ? 1 : 0;
    tangentAxis1 = dominantAxis == 2 ? 1 : 2;
}

int3 M8MembraneCanonicalSheet(int3 step)
{
    int first = step.x != 0 ? step.x : step.y != 0 ? step.y : step.z;
    return first < 0 ? -step : step;
}

int3 M8MembraneAxis(int axis)
{
    return axis == 0 ? int3(1, 0, 0) :
        axis == 1 ? int3(0, 1, 0) : int3(0, 0, 1);
}

int3 M8MembraneSetIntComponent(int3 value, int axis, int component)
{
    if (axis == 0) value.x = component;
    else if (axis == 1) value.y = component;
    else value.z = component;
    return value;
}

float3 M8MembraneSetFloatComponent(float3 value, int axis, float component)
{
    if (axis == 0) value.x = component;
    else if (axis == 1) value.y = component;
    else value.z = component;
    return value;
}

int M8MembraneFloorDiv2(int value)
{
    return value >> 1;
}

bool M8MembraneLexLess(int3 left, int3 right)
{
    return left.x < right.x || (left.x == right.x &&
        (left.y < right.y || (left.y == right.y && left.z < right.z)));
}

bool M8MembraneKnownFree(KernelState state)
{
    return (state.flags & MERKABA_OCCUPIED_FLAG) == 0u &&
        state.evidence <= MERKABA_EXPORT_KNOWN_FREE;
}

bool M8MembranePlaneLineHeight(int3 owner, float3 normal,
    float signedOffset, int dominantAxis, float3 linePoint, out float height)
{
    float denominator = normal[dominantAxis];
    if (abs(denominator) <= M8_MEMBRANE_NUMERICAL_EPSILON)
    {
        height = 0.0;
        return false;
    }
    float3 basePoint = linePoint;
    basePoint = M8MembraneSetFloatComponent(basePoint, dominantAxis, 0.0);
    float planeConstant = dot((float3)owner * MERKABA_LATTICE_STEP, normal) +
        signedOffset;
    height = (planeConstant - dot(basePoint, normal)) / denominator;
    return isfinite(height);
}

bool M8MembraneFreeSideSignature(int3 coord, int dominantAxis,
    out uint signature, out bool unresolved)
{
    signature = 0u;
    unresolved = false;
    int3 axis = M8MembraneAxis(dominantAxis);
    KernelState state;
    bool resolved;
    bool exists = M8TryLoadMembraneState(coord - axis, state, resolved);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    if (exists && M8MembraneKnownFree(state)) signature |= 1u;
    exists = M8TryLoadMembraneState(coord + axis, state, resolved);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    if (exists && M8MembraneKnownFree(state)) signature |= 2u;
    return true;
}

bool M8MembraneSeparatedByFree(int3 contributor, int normalOffset,
    int dominantAxis, out bool unresolved)
{
    unresolved = false;
    if (normalOffset == 0) return false;
    int3 towardMain = contributor;
    towardMain = M8MembraneSetIntComponent(towardMain, dominantAxis,
        towardMain[dominantAxis] - (normalOffset < 0 ? -1 : 1));
    KernelState separator;
    bool resolved;
    bool exists = M8TryLoadMembraneState(towardMain, separator, resolved);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    return exists && M8MembraneKnownFree(separator);
}

bool M8MembraneResolveCorner(int3 main, KernelState mainState,
    float3 mainNormal, float mainOffset, int dominantAxis,
    int tangentAxis0, int tangentAxis1, int cornerSign0, int cornerSign1,
    int3 mainSheet, uint mainFreeSignature, out float3 position,
    out bool unresolved)
{
    unresolved = false;
    int3 halfAddress = main * 2;
    halfAddress = M8MembraneSetIntComponent(halfAddress, tangentAxis0,
        halfAddress[tangentAxis0] + cornerSign0);
    halfAddress = M8MembraneSetIntComponent(halfAddress, tangentAxis1,
        halfAddress[tangentAxis1] + cornerSign1);
    float3 cornerLine = (float3)halfAddress *
        (M8_MEMBRANE_PATCH_PITCH * 0.5);
    float mainHeight;
    if (!M8MembranePlaneLineHeight(main, mainNormal, mainOffset,
            dominantAxis, cornerLine, mainHeight))
    {
        position = 0.0;
        return false;
    }

    int lower0 = M8MembraneFloorDiv2(halfAddress[tangentAxis0]);
    int lower1 = M8MembraneFloorDiv2(halfAddress[tangentAxis1]);
    float heightSum = 0.0;
    uint accepted = 0u;
    [loop]
    for (int first = 0; first < 2; first++)
    [loop]
    for (int second = 0; second < 2; second++)
    {
        int3 column = main;
        column = M8MembraneSetIntComponent(column, tangentAxis0,
            lower0 + first);
        column = M8MembraneSetIntComponent(column, tangentAxis1,
            lower1 + second);
        bool found = false;
        bool bestSignature = false;
        float bestResidual = 3.402823466e+38;
        int bestLayerDistance = 2147483647;
        int3 bestCoord = 0;
        float bestHeight = 0.0;
        [loop]
        for (int normalOffset = -1; normalOffset <= 1; normalOffset++)
        {
            int3 coord = column;
            coord = M8MembraneSetIntComponent(coord, dominantAxis,
                main[dominantAxis] + normalOffset);
            KernelState candidate;
            bool resolved;
            bool exists = M8TryLoadMembraneState(coord, candidate, resolved);
            if (!resolved)
            {
                unresolved = true;
                position = 0.0;
                return false;
            }
            if (!exists ||
                (candidate.flags & MERKABA_OCCUPIED_FLAG) == 0u ||
                !M8HasSurfacePlane(candidate.flags))
                continue;
            bool separatorUnresolved;
            if (M8MembraneSeparatedByFree(coord, normalOffset,
                    dominantAxis, separatorUnresolved))
                continue;
            if (separatorUnresolved)
            {
                unresolved = true;
                position = 0.0;
                return false;
            }
            float3 candidateNormal;
            float candidateOffset;
            M8DecodeSurfacePlane(candidate.flags, candidateNormal,
                candidateOffset);
            if (M8MembraneDominantAxis(candidateNormal) != dominantAxis ||
                any(M8MembraneCanonicalSheet(MerkabaNearestGridNormalStep(
                    candidateNormal)) != mainSheet))
                continue;
            float height;
            if (!M8MembranePlaneLineHeight(coord, candidateNormal,
                    candidateOffset, dominantAxis, cornerLine, height))
                continue;
            uint freeSignature;
            bool signatureUnresolved;
            if (!M8MembraneFreeSideSignature(coord, dominantAxis,
                    freeSignature, signatureUnresolved))
            {
                if (signatureUnresolved)
                {
                    unresolved = true;
                    position = 0.0;
                    return false;
                }
                continue;
            }
            bool signature = freeSignature == mainFreeSignature;
            float residual = abs(height - mainHeight);
            int layerDistance = abs(normalOffset);
            if (!found || (signature && !bestSignature) ||
                (signature == bestSignature &&
                 (residual < bestResidual - M8_MEMBRANE_NUMERICAL_EPSILON ||
                  (abs(residual - bestResidual) <=
                       M8_MEMBRANE_NUMERICAL_EPSILON &&
                   (layerDistance < bestLayerDistance ||
                    (layerDistance == bestLayerDistance &&
                     M8MembraneLexLess(coord, bestCoord)))))))
            {
                found = true;
                bestSignature = signature;
                bestResidual = residual;
                bestLayerDistance = layerDistance;
                bestCoord = coord;
                bestHeight = height;
            }
        }
        if (!found) continue;
        heightSum += bestHeight;
        accepted++;
    }
    if (accepted == 0u)
    {
        position = 0.0;
        return false;
    }
    cornerLine = M8MembraneSetFloatComponent(cornerLine, dominantAxis,
        heightSum / (float)accepted);
    position = cornerLine;
    return all(isfinite(position));
}

bool M8TryBuildMembranePatch(int3 main, KernelState state,
    out M8OverlapPatch patch, out bool unresolved)
{
    patch = (M8OverlapPatch)0;
    unresolved = false;
    if ((state.flags & MERKABA_OCCUPIED_FLAG) == 0u ||
        !M8HasSurfacePlane(state.flags))
        return false;
    float3 normal;
    float signedOffset;
    M8DecodeSurfacePlane(state.flags, normal, signedOffset);
    int dominantAxis = M8MembraneDominantAxis(normal);
    int tangentAxis0;
    int tangentAxis1;
    M8MembraneTangentAxes(dominantAxis, tangentAxis0, tangentAxis1);
    int3 sheet = M8MembraneCanonicalSheet(
        MerkabaNearestGridNormalStep(normal));
    uint freeSignature;
    if (!M8MembraneFreeSideSignature(main, dominantAxis, freeSignature,
            unresolved))
        return false;
    if (!M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, -1, -1, sheet,
            freeSignature, patch.corner00, unresolved) ||
        !M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, 1, -1, sheet,
            freeSignature, patch.corner10, unresolved) ||
        !M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, 1, 1, sheet,
            freeSignature, patch.corner11, unresolved) ||
        !M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, -1, 1, sheet,
            freeSignature, patch.corner01, unresolved))
        return false;
    if (dot(cross(patch.corner10 - patch.corner00,
            patch.corner11 - patch.corner00), normal) < 0.0)
    {
        float3 temporary = patch.corner10;
        patch.corner10 = patch.corner01;
        patch.corner01 = temporary;
    }
    patch.normal = normal;
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
