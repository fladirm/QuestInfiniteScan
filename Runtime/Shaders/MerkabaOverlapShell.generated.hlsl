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
// A contributor plane must be well conditioned against the corner line of
// MAIN's chart; the connected height branch and the free side decide sheet
// membership. No quantized dominant-axis or 26-cell equality.
#define M8_MEMBRANE_COMPATIBLE_AXIS_COSINE 0.5

struct M8OverlapPatch
{
    float3 corner00;
    float3 corner10;
    float3 corner11;
    float3 corner01;
    // Knot colour and global identity per corner: the half-lattice address
    // of its corner line (chart component zero) plus the chart axis. Two
    // patches share one vertex exactly when line, chart and position agree.
    uint color00;
    uint color10;
    uint color11;
    uint color01;
    int3 line00;
    int3 line10;
    int3 line11;
    int3 line01;
    int chart;
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
    uint mainFreeSignature, out float3 position, out uint packedColor,
    out int3 lineAddress, out bool unresolved)
{
    unresolved = false;
    packedColor = 0u;
    int3 halfAddress = main * 2;
    halfAddress = M8MembraneSetIntComponent(halfAddress, tangentAxis0,
        halfAddress[tangentAxis0] + cornerSign0);
    halfAddress = M8MembraneSetIntComponent(halfAddress, tangentAxis1,
        halfAddress[tangentAxis1] + cornerSign1);
    lineAddress = M8MembraneSetIntComponent(halfAddress, dominantAxis, 0);
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
    float heights[12];
    uint signatures[12];
    int3 coords[12];
    uint columns[12];
    uint count = 0u;
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
            if (abs(candidateNormal[dominantAxis]) <
                M8_MEMBRANE_COMPATIBLE_AXIS_COSINE)
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
            heights[count] = height;
            signatures[count] = freeSignature;
            coords[count] = coord;
            columns[count] = (uint)(first * 2 + second);
            count++;
        }
    }
    if (count == 0u)
    {
        position = 0.0;
        return false;
    }

    // MAIN chooses only WHICH sheet branch this corner belongs to.
    uint seed = 0u;
    [loop]
    for (uint index = 1u; index < count; index++)
    {
        bool signature = signatures[index] == mainFreeSignature;
        bool seedSignature = signatures[seed] == mainFreeSignature;
        float residual = abs(heights[index] - mainHeight);
        float seedResidual = abs(heights[seed] - mainHeight);
        if ((signature && !seedSignature) ||
            (signature == seedSignature &&
             (residual < seedResidual - M8_MEMBRANE_NUMERICAL_EPSILON ||
              (abs(residual - seedResidual) <=
                   M8_MEMBRANE_NUMERICAL_EPSILON &&
               M8MembraneLexLess(coords[index], coords[seed])))))
            seed = index;
    }

    // Branch membership, median and per-column contributors depend only on
    // the corner neighbourhood, so one branch resolves one shared corner.
    uint order[12];
    uint ordered = 0u;
    [loop]
    for (uint candidateIndex = 0u; candidateIndex < count; candidateIndex++)
        if (signatures[candidateIndex] == signatures[seed])
        {
            order[ordered] = candidateIndex;
            ordered++;
        }
    [loop]
    for (uint sortIndex = 1u; sortIndex < ordered; sortIndex++)
    {
        uint value = order[sortIndex];
        uint slot = sortIndex;
        [loop]
        while (slot > 0u &&
            (heights[value] < heights[order[slot - 1u]] ||
             (heights[value] == heights[order[slot - 1u]] &&
              M8MembraneLexLess(coords[value], coords[order[slot - 1u]]))))
        {
            order[slot] = order[slot - 1u];
            slot--;
        }
        order[slot] = value;
    }
    uint seedPosition = 0u;
    [loop]
    while (seedPosition + 1u < ordered && order[seedPosition] != seed)
        seedPosition++;
    uint low = seedPosition;
    uint high = seedPosition;
    [loop]
    while (low > 0u && heights[order[low]] - heights[order[low - 1u]] <=
        M8_MEMBRANE_PATCH_PITCH * 0.6)
        low--;
    [loop]
    while (high + 1u < ordered &&
        heights[order[high + 1u]] - heights[order[high]] <=
        M8_MEMBRANE_PATCH_PITCH * 0.6)
        high++;
    uint members = high - low + 1u;
    uint middle = low + (members >> 1u);
    float median = (members & 1u) != 0u
        ? heights[order[middle]]
        : (heights[order[middle - 1u]] + heights[order[middle]]) * 0.5;

    float heightSum = 0.0;
    uint accepted = 0u;
    uint red = 0u;
    uint green = 0u;
    uint blue = 0u;
    uint weightTotal = 0u;
    [loop]
    for (uint columnIndex = 0u; columnIndex < 4u; columnIndex++)
    {
        int best = -1;
        float bestDistance = 3.402823466e+38;
        [loop]
        for (uint position2 = low; position2 <= high; position2++)
        {
            uint index = order[position2];
            if (columns[index] != columnIndex) continue;
            float distance = abs(heights[index] - median);
            if (best < 0 ||
                distance < bestDistance - M8_MEMBRANE_NUMERICAL_EPSILON ||
                (abs(distance - bestDistance) <=
                     M8_MEMBRANE_NUMERICAL_EPSILON &&
                 M8MembraneLexLess(coords[index], coords[(uint)best])))
            {
                best = (int)index;
                bestDistance = distance;
            }
        }
        if (best < 0) continue;
        heightSum += heights[(uint)best];
        accepted++;
        // The knot's colour is the confidence-weighted mean of the same
        // contributors in the same column order: one value per shared knot.
        uint ownerColor;
        uint ownerConfidence;
        M8LoadMembraneColor(coords[(uint)best], ownerColor, ownerConfidence);
        uint weight = max(1u, ownerConfidence);
        red += (ownerColor & 255u) * weight;
        green += ((ownerColor >> 8u) & 255u) * weight;
        blue += ((ownerColor >> 16u) & 255u) * weight;
        weightTotal += weight;
    }
    if (accepted == 0u)
    {
        position = 0.0;
        return false;
    }
    cornerLine = M8MembraneSetFloatComponent(cornerLine, dominantAxis,
        heightSum / (float)accepted);
    position = cornerLine;
    if (!all(isfinite(position))) return false;
    float inverse = 1.0 / (float)max(1u, weightTotal);
    uint r = min(255u, (uint)floor((float)red * inverse + 0.5));
    uint g = min(255u, (uint)floor((float)green * inverse + 0.5));
    uint b = min(255u, (uint)floor((float)blue * inverse + 0.5));
    packedColor = r | (g << 8u) | (b << 16u) | 0xff000000u;
    return true;
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
    uint freeSignature;
    if (!M8MembraneFreeSideSignature(main, dominantAxis, freeSignature,
            unresolved))
        return false;
    if (!M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, -1, -1,
            freeSignature, patch.corner00, patch.color00, patch.line00,
            unresolved) ||
        !M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, 1, -1,
            freeSignature, patch.corner10, patch.color10, patch.line10,
            unresolved) ||
        !M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, 1, 1,
            freeSignature, patch.corner11, patch.color11, patch.line11,
            unresolved) ||
        !M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, -1, 1,
            freeSignature, patch.corner01, patch.color01, patch.line01,
            unresolved))
        return false;
    if (dot(cross(patch.corner10 - patch.corner00,
            patch.corner11 - patch.corner00), normal) < 0.0)
    {
        float3 temporary = patch.corner10;
        patch.corner10 = patch.corner01;
        patch.corner01 = temporary;
        uint temporaryColor = patch.color10;
        patch.color10 = patch.color01;
        patch.color01 = temporaryColor;
        int3 temporaryLine = patch.line10;
        patch.line10 = patch.line01;
        patch.line01 = temporaryLine;
    }
    patch.chart = dominantAxis;
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

uint M8OverlapPatchColor(M8OverlapPatch patch, uint corner)
{
    if (corner == 0u) return patch.color00;
    if (corner == 1u) return patch.color10;
    if (corner == 2u) return patch.color11;
    return patch.color01;
}

int3 M8OverlapPatchLine(M8OverlapPatch patch, uint corner)
{
    if (corner == 0u) return patch.line00;
    if (corner == 1u) return patch.line10;
    if (corner == 2u) return patch.line11;
    return patch.line01;
}

uint M8OverlapTriangleCorner(uint vertex)
{
    if (vertex == 0u || vertex == 3u) return 0u;
    if (vertex == 1u) return 1u;
    if (vertex == 2u || vertex == 4u) return 2u;
    return 3u;
}

#endif
