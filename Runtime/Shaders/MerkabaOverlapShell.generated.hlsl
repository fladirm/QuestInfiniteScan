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
    bool resolved;
    bool measured;
    bool knownFree;
    bool exists = M8MembraneCell(coord - axis, resolved, measured, knownFree);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    if (exists && knownFree) signature |= 1u;
    exists = M8MembraneCell(coord + axis, resolved, measured, knownFree);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    if (exists && knownFree) signature |= 2u;
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
    bool resolved;
    bool measured;
    bool knownFree;
    bool exists = M8MembraneCell(towardMain, resolved, measured, knownFree);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    return exists && knownFree;
}

// Register-resident candidate storage for the twelve corner contributors.
// Three float4 values replace the old seven 12-element private arrays. The
// candidate mask and the two-bit free-side signatures stay in scalar uints,
// so the Adreno compiler does not lower the corner solve to scratch memory.
void M8MembraneStoreHeight(inout float4 h0, inout float4 h1,
    inout float4 h2, uint slot, float value)
{
    uint lane = slot & 3u;
    if (slot < 4u)
    {
        if (lane == 0u) h0.x = value;
        else if (lane == 1u) h0.y = value;
        else if (lane == 2u) h0.z = value;
        else h0.w = value;
    }
    else if (slot < 8u)
    {
        if (lane == 0u) h1.x = value;
        else if (lane == 1u) h1.y = value;
        else if (lane == 2u) h1.z = value;
        else h1.w = value;
    }
    else
    {
        if (lane == 0u) h2.x = value;
        else if (lane == 1u) h2.y = value;
        else if (lane == 2u) h2.z = value;
        else h2.w = value;
    }
}

float M8MembraneLoadHeight(float4 h0, float4 h1, float4 h2, uint slot)
{
    uint lane = slot & 3u;
    float4 values = slot < 4u ? h0 : slot < 8u ? h1 : h2;
    return lane == 0u ? values.x :
        lane == 1u ? values.y :
        lane == 2u ? values.z : values.w;
}

uint M8MembraneLoadSignature(uint signatures, uint slot)
{
    return (signatures >> (slot * 2u)) & 3u;
}

int3 M8MembraneCandidateCoord(int3 main, int dominantAxis,
    int tangentAxis0, int tangentAxis1, int lower0, int lower1, uint slot,
    out uint columnIndex, out uint layerDistance, out int normalOffset)
{
    columnIndex = slot < 3u ? 0u : slot < 6u ? 1u :
        slot < 9u ? 2u : 3u;
    uint layerIndex = slot - columnIndex * 3u;
    int first = (int)(columnIndex >> 1u);
    int second = (int)(columnIndex & 1u);
    normalOffset = (int)layerIndex - 1;
    layerDistance = layerIndex == 1u ? 0u : 1u;
    int3 coord = main;
    coord = M8MembraneSetIntComponent(coord, tangentAxis0, lower0 + first);
    coord = M8MembraneSetIntComponent(coord, tangentAxis1, lower1 + second);
    coord = M8MembraneSetIntComponent(coord, dominantAxis,
        main[dominantAxis] + normalOffset);
    return coord;
}

bool M8MembraneEvaluateCandidate(int3 main, int dominantAxis,
    int tangentAxis0, int tangentAxis1, int lower0, int lower1,
    float3 cornerLine, uint slot, out float height, out uint signature,
    out int3 coord, out uint layerDistance, out uint columnIndex,
    out bool unresolved)
{
    height = 0.0;
    signature = 0u;
    unresolved = false;
    int normalOffset;
    coord = M8MembraneCandidateCoord(main, dominantAxis, tangentAxis0,
        tangentAxis1, lower0, lower1, slot, columnIndex, layerDistance,
        normalOffset);

    bool resolved;
    bool measured;
    bool knownFree;
    bool exists = M8MembraneCell(coord, resolved, measured, knownFree);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    if (!exists || !measured) return false;

    bool separatorUnresolved;
    bool separated = M8MembraneSeparatedByFree(coord, normalOffset,
        dominantAxis, separatorUnresolved);
    if (separatorUnresolved)
    {
        unresolved = true;
        return false;
    }
    if (separated) return false;

    float4 plane = M8MembranePlaneOf(coord);
    float denominator = plane[dominantAxis];
    if (abs(denominator) < M8_MEMBRANE_COMPATIBLE_AXIS_COSINE)
        return false;
    float3 basePoint = M8MembraneSetFloatComponent(cornerLine,
        dominantAxis, 0.0);
    height = (plane.w - dot(basePoint, plane.xyz)) / denominator;
    if (!isfinite(height)) return false;

    bool signatureUnresolved;
    if (!M8MembraneFreeSideSignature(coord, dominantAxis, signature,
            signatureUnresolved))
    {
        if (signatureUnresolved) unresolved = true;
        return false;
    }
    return true;
}

bool M8MembraneResolveCorner(int3 main, KernelState mainState,
    float3 mainNormal, float mainOffset, int dominantAxis,
    int tangentAxis0, int tangentAxis1, int cornerSign0, int cornerSign1,
    uint mainFreeSignature, out float3 position, out uint packedColor,
    out int3 lineAddress, out bool unresolved)
{
    // Exact CPU-oracle math with register-resident GPU state. Candidate
    // geometry is decoded once from the groupshared tile cache. Connectivity,
    // median rank and per-column winner selection then operate on three
    // float4 height registers plus bit masks; no 12-element private arrays,
    // dynamic local-memory indexing or pairwise near/below tables survive.
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
    float4 heights0 = 0.0;
    float4 heights1 = 0.0;
    float4 heights2 = 0.0;
    uint validMask = 0u;
    uint signatureBits = 0u;

    // Decode the twelve candidates once. Any unresolved dependency preserves
    // the old FRONT exactly as before.
    [loop]
    for (uint slot = 0u; slot < 12u; slot++)
    {
        float height;
        uint signature;
        int3 coord;
        uint layerDistance;
        uint columnIndex;
        bool candidateUnresolved;
        bool valid = M8MembraneEvaluateCandidate(main, dominantAxis,
            tangentAxis0, tangentAxis1, lower0, lower1, cornerLine, slot,
            height, signature, coord, layerDistance, columnIndex,
            candidateUnresolved);
        if (candidateUnresolved)
        {
            unresolved = true;
            position = 0.0;
            return false;
        }
        if (!valid) continue;
        validMask |= 1u << slot;
        signatureBits |= (signature & 3u) << (slot * 2u);
        M8MembraneStoreHeight(heights0, heights1, heights2, slot, height);
    }
    if (validMask == 0u)
    {
        position = 0.0;
        return false;
    }

    // MAIN selects only the sheet branch: same free side first, then plane
    // residual, normal-layer distance, and lexicographic coordinate.
    bool haveSeed = false;
    float seedHeight = 0.0;
    int3 seedCoord = int3(0, 0, 0);
    uint seedSignature = 0u;
    uint seedLayer = 0u;
    [loop]
    for (uint slot = 0u; slot < 12u; slot++)
    {
        if ((validMask & (1u << slot)) == 0u) continue;
        float height = M8MembraneLoadHeight(heights0, heights1, heights2,
            slot);
        uint signature = M8MembraneLoadSignature(signatureBits, slot);
        uint columnIndex;
        uint layerDistance;
        int normalOffset;
        int3 coord = M8MembraneCandidateCoord(main, dominantAxis,
            tangentAxis0, tangentAxis1, lower0, lower1, slot, columnIndex,
            layerDistance, normalOffset);
        bool signatureMatch = signature == mainFreeSignature;
        bool seedSignatureMatch = seedSignature == mainFreeSignature;
        float residual = abs(height - mainHeight);
        float seedResidual = abs(seedHeight - mainHeight);
        if (!haveSeed ||
            (signatureMatch && !seedSignatureMatch) ||
            (signatureMatch == seedSignatureMatch &&
             (residual < seedResidual - M8_MEMBRANE_NUMERICAL_EPSILON ||
              (abs(residual - seedResidual) <=
                   M8_MEMBRANE_NUMERICAL_EPSILON &&
               (layerDistance < seedLayer ||
                (layerDistance == seedLayer &&
                 M8MembraneLexLess(coord, seedCoord)))))))
        {
            haveSeed = true;
            seedHeight = height;
            seedCoord = coord;
            seedSignature = signature;
            seedLayer = layerDistance;
        }
    }
    if (!haveSeed)
    {
        position = 0.0;
        return false;
    }

    // In one dimension the connected component of |dh| <= gap is exactly
    // represented by its current [low, high] interval. Eleven bounded scans
    // grow the component from the seed without a near[12] adjacency table.
    float branchLow = seedHeight;
    float branchHigh = seedHeight;
    float branchGap = M8_MEMBRANE_PATCH_PITCH * 0.6;
    [loop]
    for (uint expansion = 0u; expansion < 11u; expansion++)
    {
        float nextLow = branchLow;
        float nextHigh = branchHigh;
        [loop]
        for (uint slot = 0u; slot < 12u; slot++)
        {
            if ((validMask & (1u << slot)) == 0u ||
                M8MembraneLoadSignature(signatureBits, slot) != seedSignature)
                continue;
            float height = M8MembraneLoadHeight(heights0, heights1,
                heights2, slot);
            if (height < branchLow - branchGap ||
                height > branchHigh + branchGap)
                continue;
            nextLow = min(nextLow, height);
            nextHigh = max(nextHigh, height);
        }
        bool stable = nextLow == branchLow && nextHigh == branchHigh;
        branchLow = nextLow;
        branchHigh = nextHigh;
        if (stable) break;
    }

    uint members = 0u;
    [loop]
    for (uint slot = 0u; slot < 12u; slot++)
    {
        if ((validMask & (1u << slot)) == 0u ||
            M8MembraneLoadSignature(signatureBits, slot) != seedSignature)
            continue;
        float height = M8MembraneLoadHeight(heights0, heights1, heights2,
            slot);
        if (height >= branchLow && height <= branchHigh) members++;
    }
    if (members == 0u)
    {
        position = 0.0;
        return false;
    }

    // Exact CPU ordering median. Rank is recomputed from the register-resident
    // heights, avoiding below[12] while retaining the same (height, coord)
    // tie-break.
    uint middle = members >> 1u;
    float medianHigh = 0.0;
    float medianLow = 0.0;
    [loop]
    for (uint slot = 0u; slot < 12u; slot++)
    {
        if ((validMask & (1u << slot)) == 0u ||
            M8MembraneLoadSignature(signatureBits, slot) != seedSignature)
            continue;
        float height = M8MembraneLoadHeight(heights0, heights1, heights2,
            slot);
        if (height < branchLow || height > branchHigh) continue;
        uint columnIndex;
        uint layerDistance;
        int normalOffset;
        int3 coord = M8MembraneCandidateCoord(main, dominantAxis,
            tangentAxis0, tangentAxis1, lower0, lower1, slot, columnIndex,
            layerDistance, normalOffset);
        uint rank = 0u;
        [loop]
        for (uint other = 0u; other < 12u; other++)
        {
            if ((validMask & (1u << other)) == 0u ||
                M8MembraneLoadSignature(signatureBits, other) !=
                    seedSignature)
                continue;
            float otherHeight = M8MembraneLoadHeight(heights0, heights1,
                heights2, other);
            if (otherHeight < branchLow || otherHeight > branchHigh)
                continue;
            uint otherColumn;
            uint otherLayer;
            int otherNormalOffset;
            int3 otherCoord = M8MembraneCandidateCoord(main, dominantAxis,
                tangentAxis0, tangentAxis1, lower0, lower1, other,
                otherColumn, otherLayer, otherNormalOffset);
            if (otherHeight < height ||
                (otherHeight == height &&
                 M8MembraneLexLess(otherCoord, coord)))
                rank++;
        }
        if (rank == middle) medianHigh = height;
        if (middle > 0u && rank == middle - 1u) medianLow = height;
    }
    float median = (members & 1u) != 0u
        ? medianHigh : (medianLow + medianHigh) * 0.5;

    float heightSum = 0.0;
    uint accepted = 0u;
    uint red = 0u;
    uint green = 0u;
    uint blue = 0u;
    uint weightTotal = 0u;
    [loop]
    for (uint columnIndex = 0u; columnIndex < 4u; columnIndex++)
    {
        bool haveBest = false;
        float bestHeight = 0.0;
        float bestDistance = 0.0;
        int3 bestCoord = int3(0, 0, 0);
        uint bestLayer = 0u;
        [loop]
        for (uint layerIndex = 0u; layerIndex < 3u; layerIndex++)
        {
            uint slot = columnIndex * 3u + layerIndex;
            if ((validMask & (1u << slot)) == 0u ||
                M8MembraneLoadSignature(signatureBits, slot) != seedSignature)
                continue;
            float height = M8MembraneLoadHeight(heights0, heights1, heights2,
                slot);
            if (height < branchLow || height > branchHigh) continue;
            uint candidateColumn;
            uint layerDistance;
            int normalOffset;
            int3 coord = M8MembraneCandidateCoord(main, dominantAxis,
                tangentAxis0, tangentAxis1, lower0, lower1, slot,
                candidateColumn, layerDistance, normalOffset);
            float distance = abs(height - median);
            if (!haveBest ||
                distance < bestDistance - M8_MEMBRANE_NUMERICAL_EPSILON ||
                (abs(distance - bestDistance) <=
                     M8_MEMBRANE_NUMERICAL_EPSILON &&
                 (layerDistance < bestLayer ||
                  (layerDistance == bestLayer &&
                   M8MembraneLexLess(coord, bestCoord)))))
            {
                haveBest = true;
                bestHeight = height;
                bestDistance = distance;
                bestCoord = coord;
                bestLayer = layerDistance;
            }
        }
        if (!haveBest) continue;
        heightSum += bestHeight;
        accepted++;
        uint ownerColor;
        uint ownerConfidence;
        M8LoadMembraneColor(bestCoord, ownerColor, ownerConfidence);
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
    // One corner solve, inlined once: corners 00, 10, 11, 01.
    [loop]
    for (uint corner = 0u; corner < 4u; corner++)
    {
        int sign0 = (corner == 1u || corner == 2u) ? 1 : -1;
        int sign1 = corner >= 2u ? 1 : -1;
        float3 position;
        uint color;
        int3 knotLine;
        if (!M8MembraneResolveCorner(main, state, normal, signedOffset,
                dominantAxis, tangentAxis0, tangentAxis1, sign0, sign1,
                freeSignature, position, color, knotLine, unresolved))
            return false;
        if (corner == 0u) { patch.corner00 = position; patch.color00 = color; patch.line00 = knotLine; }
        else if (corner == 1u) { patch.corner10 = position; patch.color10 = color; patch.line10 = knotLine; }
        else if (corner == 2u) { patch.corner11 = position; patch.color11 = color; patch.line11 = knotLine; }
        else { patch.corner01 = position; patch.color01 = color; patch.line01 = knotLine; }
    }
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
