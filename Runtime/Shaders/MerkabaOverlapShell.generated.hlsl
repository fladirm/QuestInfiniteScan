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
// Two heights on one knot line belong to one branch when closer than this;
// face neighbours belong to one local sheet when their planes agree both
// ways within it.
#define M8_MEMBRANE_BRANCH_GAP (M8_MEMBRANE_PATCH_PITCH * 0.6)
// Face neighbours whose normals agree at least this much shape the chart.
#define M8_MEMBRANE_CHART_COSINE 0.5
// No patch edge may exceed this; a longer patch is not emitted.
#define M8_MEMBRANE_MAX_EDGE 0.045

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
    // Exact knot identity per corner (winner cells): the weld key.
    uint key00;
    uint key10;
    uint key11;
    uint key01;
    int chart;
    uint side;
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

// Register-resident storage for the four uses and twelve contributors of
// one knot: heights in float4 registers, membership in scalar bit masks.
// Nothing is a private array, so Adreno never lowers the solve to scratch.
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

float M8MembraneLoadUse(float4 uses, uint column)
{
    return column == 0u ? uses.x : column == 1u ? uses.y :
        column == 2u ? uses.z : uses.w;
}

// Column of a candidate slot (slot = column * 3 + offset + 1) without an
// integer division: slot < 12.
uint M8MembraneSlotColumn(uint slot)
{
    return slot < 3u ? 0u : slot < 6u ? 1u : slot < 9u ? 2u : 3u;
}

int3 M8MembraneColumnCoord(int3 main, int chart, int tangentAxis0,
    int tangentAxis1, int lower0, int lower1, uint column, int layer)
{
    int3 coord = main;
    coord = M8MembraneSetIntComponent(coord, tangentAxis0,
        lower0 + (int)(column >> 1u));
    coord = M8MembraneSetIntComponent(coord, tangentAxis1,
        lower1 + (int)(column & 1u));
    coord = M8MembraneSetIntComponent(coord, chart, layer);
    return coord;
}

// Canonical chart of a measured cell: dominant axis of the sum of its plane
// normal and the normals of its 26 neighbours of the same local sheet
// (measured, no KNOWN FREE on the axis steps between, normals agreeing by
// the chart cosine, planes agreeing both ways within the branch gap).
// Radius one, fixed order, tie X, Y, Z.
bool M8MembraneCanonicalChart(int3 coord, out int chart, out bool unresolved)
{
    unresolved = false;
    chart = 0;
    float4 plane = M8MembranePlaneOf(coord);
    float3 normal = plane.xyz;
    uint ownColor;
    uint ownConfidence;
    M8LoadMembraneColor(coord, ownColor, ownConfidence);
    float3 sum = normal * (float)max(1u, ownConfidence);
    float planeConstant = plane.w;
    // Binding plan section 1: SIX face neighbours only, fixed axis/sign
    // order, no abs(dot), confidence weighted.
    [unroll]
    for (int axis = 0; axis < 3; axis++)
    [unroll]
    for (int signIndex = 0; signIndex < 2; signIndex++)
    {
        int direction = signIndex == 0 ? -1 : 1;
        int3 neighbour = coord + M8MembraneAxis(axis) * direction;
        bool resolved;
        bool measured;
        bool knownFree;
        bool exists = M8MembraneCell(neighbour, resolved, measured, knownFree);
        if (!resolved)
        {
            unresolved = true;
            return false;
        }
        if (!exists || !measured) continue;
        float4 neighbourPlane = M8MembranePlaneOf(neighbour);
        float3 neighbourNormal = neighbourPlane.xyz;
        if (dot(normal, neighbourNormal) < M8_MEMBRANE_CHART_COSINE) continue;
        float3 neighbourCentre = (float3)neighbour * MERKABA_LATTICE_STEP;
        float residualHere = abs(dot(neighbourCentre, normal) - planeConstant);
        float residualThere = abs(dot((float3)coord * MERKABA_LATTICE_STEP,
            neighbourNormal) - neighbourPlane.w);
        if (residualHere > M8_MEMBRANE_BRANCH_GAP ||
            residualThere > M8_MEMBRANE_BRANCH_GAP)
            continue;
        uint neighbourColor;
        uint neighbourConfidence;
        M8LoadMembraneColor(neighbour, neighbourColor, neighbourConfidence);
        sum += neighbourNormal * (float)max(1u, neighbourConfidence);
    }
    chart = M8MembraneDominantAxis(sum);
    return true;
}

// The chart-transition predicate is part of the same generated oracle.
// It creates no point: Resolve/Emit may only use already solved knot edges.
bool M8MembraneStitchCompatible(int3 first, int3 second)
{
    float4 firstPlane = M8MembranePlaneOf(first);
    float4 secondPlane = M8MembranePlaneOf(second);
    if (dot(firstPlane.xyz, secondPlane.xyz) < M8_MEMBRANE_CHART_COSINE)
        return false;
    float3 firstCentre = (float3)first * MERKABA_LATTICE_STEP;
    float3 secondCentre = (float3)second * MERKABA_LATTICE_STEP;
    return abs(dot(secondCentre, firstPlane.xyz) - firstPlane.w) <=
               M8_MEMBRANE_BRANCH_GAP &&
           abs(dot(firstCentre, secondPlane.xyz) - secondPlane.w) <=
               M8_MEMBRANE_BRANCH_GAP;
}

// One shared knot of a MAIN's corner (contract C5 steps 1-9). The knot is a
// function of its line, chart, free side and layer only: the uses of the
// line at that layer define the reference height, the four sharing columns
// at layers -1/0/+1 supply the contributors, one winner per column. Every
// MAIN of the same cluster, and every MAIN of an adjacent layer with the
// same winners, resolves the identical knot. winnerKey encodes the winner
// cells (mask and layer per column), the exact identity for welding.
bool M8MembraneSolveKnot(int3 main, float3 mainNormal, int chart,
    int tangentAxis0, int tangentAxis1, int cornerSign0, int cornerSign1,
    uint side, out float3 position, out uint packedColor,
    out int3 lineAddress, out uint winnerKey, out bool unresolved)
{
    unresolved = false;
    position = 0.0;
    packedColor = 0u;
    winnerKey = 0u;
    int3 halfAddress = main * 2;
    halfAddress = M8MembraneSetIntComponent(halfAddress, tangentAxis0,
        halfAddress[tangentAxis0] + cornerSign0);
    halfAddress = M8MembraneSetIntComponent(halfAddress, tangentAxis1,
        halfAddress[tangentAxis1] + cornerSign1);
    lineAddress = M8MembraneSetIntComponent(halfAddress, chart, 0);
    float3 linePoint = (float3)halfAddress * (M8_MEMBRANE_PATCH_PITCH * 0.5);
    int layer = main[chart];
    int lower0 = M8MembraneFloorDiv2(halfAddress[tangentAxis0]);
    int lower1 = M8MembraneFloorDiv2(halfAddress[tangentAxis1]);

    // Uses: cells of this chart and side in the four columns at MAIN's layer.
    float4 useHeights = 0.0;
    uint useMask = 0u;
    [loop]
    for (uint column = 0u; column < 4u; column++)
    {
        int3 coord = M8MembraneColumnCoord(main, chart, tangentAxis0,
            tangentAxis1, lower0, lower1, column, layer);
        bool resolved;
        bool measured;
        bool knownFree;
        bool exists = M8MembraneCell(coord, resolved, measured, knownFree);
        if (!resolved)
        {
            unresolved = true;
            return false;
        }
        if (!exists || !measured) continue;
        int useChart;
        bool chartUnresolved;
        if (!M8MembraneCanonicalChart(coord, useChart, chartUnresolved))
        {
            unresolved = true;
            return false;
        }
        if (useChart != chart) continue;
        uint useSide;
        bool sideUnresolved;
        if (!M8MembraneFreeSideSignature(coord, chart, useSide, sideUnresolved))
        {
            if (sideUnresolved) unresolved = true;
            return false;
        }
        if (useSide != side) continue;
        float4 plane = M8MembranePlaneOf(coord);
        float denominator = plane[chart];
        if (abs(denominator) < M8_MEMBRANE_COMPATIBLE_AXIS_COSINE) continue;
        float3 basePoint = M8MembraneSetFloatComponent(linePoint, chart, 0.0);
        float height = (plane.w - dot(basePoint, plane.xyz)) / denominator;
        if (!isfinite(height)) continue;
        useHeights = column == 0u ? float4(height, useHeights.yzw) :
            column == 1u ? float4(useHeights.x, height, useHeights.zw) :
            column == 2u ? float4(useHeights.xy, height, useHeights.w) :
            float4(useHeights.xyz, height);
        useMask |= 1u << column;
    }
    uint mainColumn = (uint)(main[tangentAxis0] - lower0) * 2u +
        (uint)(main[tangentAxis1] - lower1);
    if ((useMask & (1u << mainColumn)) == 0u) return false;

    // Cluster: connected height component of the uses that holds MAIN.
    uint cluster = 1u << mainColumn;
    [loop]
    for (uint expansion = 0u; expansion < 3u; expansion++)
    [loop]
    for (uint column = 0u; column < 4u; column++)
    {
        if ((useMask & (1u << column)) == 0u ||
            (cluster & (1u << column)) != 0u)
            continue;
        float height = M8MembraneLoadUse(useHeights, column);
        [loop]
        for (uint member = 0u; member < 4u; member++)
        {
            if ((cluster & (1u << member)) != 0u &&
                abs(height - M8MembraneLoadUse(useHeights, member)) <=
                M8_MEMBRANE_BRANCH_GAP)
            {
                cluster |= 1u << column;
                break;
            }
        }
    }
    // Median of the cluster: rank by (height, column) among members.
    // (bit sum instead of countbits: FXC rejects the intrinsic here.)
    uint members = (cluster & 1u) + ((cluster >> 1u) & 1u) +
        ((cluster >> 2u) & 1u) + ((cluster >> 3u) & 1u);
    uint middle = members >> 1u;
    float medianHigh = 0.0;
    float medianLow = 0.0;
    [loop]
    for (uint column = 0u; column < 4u; column++)
    {
        if ((cluster & (1u << column)) == 0u) continue;
        float height = M8MembraneLoadUse(useHeights, column);
        uint rank = 0u;
        [loop]
        for (uint other = 0u; other < 4u; other++)
        {
            if ((cluster & (1u << other)) == 0u || other == column) continue;
            float otherHeight = M8MembraneLoadUse(useHeights, other);
            if (otherHeight < height ||
                (otherHeight == height && other < column))
                rank++;
        }
        if (rank == middle) medianHigh = height;
        if (middle > 0u && rank == middle - 1u) medianLow = height;
    }
    float reference = (members & 1u) != 0u
        ? medianHigh : (medianLow + medianHigh) * 0.5;

    // Candidates: the four columns at layers -1, 0, +1 (slot = column*3 +
    // offset + 1). Contract C5 step 4.
    float4 heights0 = 0.0;
    float4 heights1 = 0.0;
    float4 heights2 = 0.0;
    uint validMask = 0u;
    [loop]
    for (uint slot = 0u; slot < 12u; slot++)
    {
        uint column = M8MembraneSlotColumn(slot);
        int normalOffset = (int)(slot - column * 3u) - 1;
        int3 coord = M8MembraneColumnCoord(main, chart, tangentAxis0,
            tangentAxis1, lower0, lower1, column, layer + normalOffset);
        bool resolved;
        bool measured;
        bool knownFree;
        bool exists = M8MembraneCell(coord, resolved, measured, knownFree);
        if (!resolved)
        {
            unresolved = true;
            return false;
        }
        if (!exists || !measured) continue;
        if (normalOffset != 0)
        {
            int3 between = M8MembraneSetIntComponent(coord, chart, layer);
            bool separatorResolved;
            bool separatorMeasured;
            bool separatorFree;
            bool separatorExists = M8MembraneCell(between, separatorResolved,
                separatorMeasured, separatorFree);
            if (!separatorResolved)
            {
                unresolved = true;
                return false;
            }
            if (separatorExists && separatorFree) continue;
        }
        float4 plane = M8MembranePlaneOf(coord);
        float denominator = plane[chart];
        if (abs(denominator) < M8_MEMBRANE_COMPATIBLE_AXIS_COSINE) continue;
        uint candidateSide;
        bool sideUnresolved;
        if (!M8MembraneFreeSideSignature(coord, chart, candidateSide,
                sideUnresolved))
        {
            if (sideUnresolved) unresolved = true;
            return false;
        }
        if (candidateSide != side) continue;
        float3 basePoint = M8MembraneSetFloatComponent(linePoint, chart, 0.0);
        float height = (plane.w - dot(basePoint, plane.xyz)) / denominator;
        if (!isfinite(height)) continue;
        M8MembraneStoreHeight(heights0, heights1, heights2, slot, height);
        validMask |= 1u << slot;
    }

    // Admission: connected height component of the cluster cells among the
    // candidates. Every member stays inside the +-1 window.
    uint admitted = 0u;
    [unroll]
    for (uint column = 0u; column < 4u; column++)
    {
        uint slot = column * 3u + 1u;
        if ((cluster & (1u << column)) != 0u &&
            (validMask & (1u << slot)) != 0u)
            admitted |= 1u << slot;
    }
    [loop]
    for (uint expansion = 0u; expansion < 11u; expansion++)
    {
        uint grown = admitted;
        [loop]
        for (uint slot = 0u; slot < 12u; slot++)
        {
            if ((validMask & (1u << slot)) == 0u ||
                (grown & (1u << slot)) != 0u)
                continue;
            float height = M8MembraneLoadHeight(heights0, heights1, heights2,
                slot);
            [loop]
            for (uint other = 0u; other < 12u; other++)
            {
                if ((admitted & (1u << other)) == 0u) continue;
                if (abs(height - M8MembraneLoadHeight(heights0, heights1,
                        heights2, other)) <= M8_MEMBRANE_BRANCH_GAP)
                {
                    grown |= 1u << slot;
                    break;
                }
            }
        }
        if (grown == admitted) break;
        admitted = grown;
    }
    if (admitted == 0u) return false;

    // Winner per column: nearest to the reference height, then the nearer
    // layer, then the lexicographically smaller coordinate.
    float heightSum = 0.0;
    uint accepted = 0u;
    uint red = 0u;
    uint green = 0u;
    uint blue = 0u;
    uint weightTotal = 0u;
    float3 normalSum = 0.0;
    [loop]
    for (uint column = 0u; column < 4u; column++)
    {
        bool haveBest = false;
        uint bestSlot = 0u;
        float bestDistance = 0.0;
        int bestOffset = 0;
        int3 bestCoord = int3(0, 0, 0);
        [unroll]
        for (int normalOffset = -1; normalOffset <= 1; normalOffset++)
        {
            uint slot = column * 3u + (uint)(normalOffset + 1);
            if ((admitted & (1u << slot)) == 0u) continue;
            float distance = abs(M8MembraneLoadHeight(heights0, heights1,
                heights2, slot) - reference);
            int3 coord = M8MembraneColumnCoord(main, chart, tangentAxis0,
                tangentAxis1, lower0, lower1, column, layer + normalOffset);
            int layerDistance = normalOffset < 0 ? -normalOffset : normalOffset;
            if (!haveBest ||
                distance < bestDistance - M8_MEMBRANE_NUMERICAL_EPSILON ||
                (abs(distance - bestDistance) <=
                     M8_MEMBRANE_NUMERICAL_EPSILON &&
                 (layerDistance < bestOffset ||
                  (layerDistance == bestOffset &&
                   M8MembraneLexLess(coord, bestCoord)))))
            {
                haveBest = true;
                bestSlot = slot;
                bestDistance = distance;
                bestOffset = layerDistance;
                bestCoord = coord;
            }
        }
        if (!haveBest) continue;
        heightSum += M8MembraneLoadHeight(heights0, heights1, heights2,
            bestSlot);
        accepted++;
        winnerKey |= 1u << column;
        winnerKey |= ((uint)bestCoord[chart] & 15u) << (4u + column * 4u);
        float4 winnerPlane = M8MembranePlaneOf(bestCoord);
        uint ownerColor;
        uint ownerConfidence;
        M8LoadMembraneColor(bestCoord, ownerColor, ownerConfidence);
        uint weight = max(1u, ownerConfidence);
        normalSum += winnerPlane.xyz * (float)weight;
        red += (ownerColor & 255u) * weight;
        green += ((ownerColor >> 8u) & 255u) * weight;
        blue += ((ownerColor >> 16u) & 255u) * weight;
        weightTotal += weight;
    }
    if (accepted == 0u) return false;
    position = M8MembraneSetFloatComponent(linePoint, chart,
        heightSum / (float)accepted);
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
    int chart;
    if (!M8MembraneCanonicalChart(main, chart, unresolved)) return false;
    int tangentAxis0;
    int tangentAxis1;
    M8MembraneTangentAxes(chart, tangentAxis0, tangentAxis1);
    uint side;
    if (!M8MembraneFreeSideSignature(main, chart, side, unresolved))
        return false;
    // One knot solve, inlined once: corners 00, 10, 11, 01.
    [loop]
    for (uint corner = 0u; corner < 4u; corner++)
    {
        int sign0 = (corner == 1u || corner == 2u) ? 1 : -1;
        int sign1 = corner >= 2u ? 1 : -1;
        float3 position;
        uint color;
        int3 knotLine;
        uint key;
        if (!M8MembraneSolveKnot(main, normal, chart, tangentAxis0,
                tangentAxis1, sign0, sign1, side, position, color, knotLine,
                key, unresolved))
            return false;
        if (corner == 0u) { patch.corner00 = position; patch.color00 = color; patch.line00 = knotLine; patch.key00 = key; }
        else if (corner == 1u) { patch.corner10 = position; patch.color10 = color; patch.line10 = knotLine; patch.key10 = key; }
        else if (corner == 2u) { patch.corner11 = position; patch.color11 = color; patch.line11 = knotLine; patch.key11 = key; }
        else { patch.corner01 = position; patch.color01 = color; patch.line01 = knotLine; patch.key01 = key; }
    }
    // Edge guard: a patch whose knots drifted apart is not emitted.
    float maxEdge2 = M8_MEMBRANE_MAX_EDGE * M8_MEMBRANE_MAX_EDGE;
    float3 e0 = patch.corner10 - patch.corner00;
    float3 e1 = patch.corner11 - patch.corner10;
    float3 e2 = patch.corner01 - patch.corner11;
    float3 e3 = patch.corner00 - patch.corner01;
    if (dot(e0, e0) > maxEdge2 || dot(e1, e1) > maxEdge2 ||
        dot(e2, e2) > maxEdge2 || dot(e3, e3) > maxEdge2)
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
        uint temporaryKey = patch.key10;
        patch.key10 = patch.key01;
        patch.key01 = temporaryKey;
    }
    patch.chart = chart;
    patch.side = side;
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
