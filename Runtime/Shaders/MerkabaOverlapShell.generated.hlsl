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
// A plane is usable on a line of chart c only while |N[c]| >= this.
#define M8_MEMBRANE_COMPATIBLE_AXIS_COSINE 0.5
// g: branch height gap and local-sheet plane agreement.
#define M8_MEMBRANE_BRANCH_GAP (M8_MEMBRANE_PATCH_PITCH * 0.6)
#define M8_MEMBRANE_CHART_COSINE 0.5
#define M8_MEMBRANE_MAX_EDGE 0.045
// The uses of one node span at most two normal layers.
#define M8_MEMBRANE_MAX_LAYER_SPAN 2
#define M8_MEMBRANE_TRANSITION_EDGE_COSINE 0.5

// The kernel that includes this oracle defines the cell accessors:
//   bool M8MembraneCell(int3, out bool resolved, out bool measured,
//       out bool knownFree)
//   float4 M8MembranePlaneOf(int3)   unit normal, dot(C, N) + d
//   int M8MembraneChartOf(int3)      the C5.1 chart of a measured cell
//   void M8LoadMembraneColor(int3, out uint packed, out uint confidence)

int M8MembraneDominantAxis(float3 value)
{
    float3 magnitude = abs(value);
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

// Floor division by two (arithmetic shift).
int M8MembraneFloorDiv2(int value)
{
    return value >> 1;
}

bool M8MembraneMeasuredAt(int3 coord)
{
    bool resolved;
    bool measured;
    bool knownFree;
    bool exists = M8MembraneCell(coord, resolved, measured, knownFree);
    return exists && measured;
}

bool M8MembraneFreeAt(int3 coord)
{
    bool resolved;
    bool measured;
    bool knownFree;
    bool exists = M8MembraneCell(coord, resolved, measured, knownFree);
    return exists && knownFree;
}

// Canonical sheet side along the chart axis. Exact one-sided FREE evidence
// wins. UNKNOWN/UNKNOWN and FREE/FREE fall back to the measured plane
// orientation, so incomplete free-space evidence cannot split one physical
// sheet into disconnected node families.
uint M8MembraneSignature(int3 coord, int chart)
{
    int3 axis = M8MembraneAxis(chart);
    uint signature = M8MembraneFreeAt(coord - axis) ? 1u : 0u;
    if (M8MembraneFreeAt(coord + axis)) signature |= 2u;
    if (signature == 1u || signature == 2u)
        return signature;
    if (!M8MembraneMeasuredAt(coord))
        return signature;
    float4 plane = M8MembranePlaneOf(coord);
    return plane[chart] >= 0.0 ? 2u : 1u;
}

// C5.1 canonical chart of a measured cell: dominant axis of its normal plus
// the normals of its 26 neighbours of the same local sheet (measured,
// dot >= 0.5 without abs, planes agreeing both ways within g, no KNOWN FREE
// on the single-axis steps of an edge or corner neighbour). Fixed order dx,
// dy, dz; equal weights; tie X, Y, Z. Falls back to the raw dominant axis of
// the cell when its own |N[chart]| < 0.5.
int M8MembraneCanonicalChart(int3 coord)
{
    float4 plane = M8MembranePlaneOf(coord);
    float3 normal = plane.xyz;
    float3 centre = (float3)coord * M8_MEMBRANE_PATCH_PITCH;
    float3 sum = normal;
    [loop]
    for (int dx = -1; dx <= 1; dx++)
    [loop]
    for (int dy = -1; dy <= 1; dy++)
    [loop]
    for (int dz = -1; dz <= 1; dz++)
    {
        if (dx == 0 && dy == 0 && dz == 0) continue;
        int3 neighbour = coord + int3(dx, dy, dz);
        if (!M8MembraneMeasuredAt(neighbour)) continue;
        float4 otherPlane = M8MembranePlaneOf(neighbour);
        if (dot(normal, otherPlane.xyz) < M8_MEMBRANE_CHART_COSINE) continue;
        if (abs(dot((float3)neighbour * M8_MEMBRANE_PATCH_PITCH, normal) -
                plane.w) > M8_MEMBRANE_BRANCH_GAP ||
            abs(dot(centre, otherPlane.xyz) - otherPlane.w) >
                M8_MEMBRANE_BRANCH_GAP)
            continue;
        int nonZero = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0) +
            (dz != 0 ? 1 : 0);
        if (nonZero >= 2)
        {
            if (dx != 0 && M8MembraneFreeAt(coord + int3(dx, 0, 0))) continue;
            if (dy != 0 && M8MembraneFreeAt(coord + int3(0, dy, 0))) continue;
            if (dz != 0 && M8MembraneFreeAt(coord + int3(0, 0, dz))) continue;
        }
        sum += otherPlane.xyz;
    }
    int chart = M8MembraneDominantAxis(sum);
    if (abs(normal[chart]) < M8_MEMBRANE_COMPATIBLE_AXIS_COSINE)
        chart = M8MembraneDominantAxis(normal);
    return chart;
}

// ---- C5.2 line node ----

int3 M8MembraneColumnCell(int3 halfAddress, int chart, int tangentAxis0,
    int tangentAxis1, uint column, int layer)
{
    int3 cell = int3(0, 0, 0);
    cell = M8MembraneSetIntComponent(cell, tangentAxis0,
        M8MembraneFloorDiv2(halfAddress[tangentAxis0]) + (int)(column >> 1u));
    cell = M8MembraneSetIntComponent(cell, tangentAxis1,
        M8MembraneFloorDiv2(halfAddress[tangentAxis1]) + (int)(column & 1u));
    return M8MembraneSetIntComponent(cell, chart, layer);
}

bool M8MembraneLineHeight(float4 plane, int chart, int3 halfAddress,
    out float height)
{
    height = 0.0;
    float denominator = plane[chart];
    if (abs(denominator) < M8_MEMBRANE_COMPATIBLE_AXIS_COSINE) return false;
    float3 linePoint = M8MembraneSetFloatComponent(
        (float3)halfAddress * M8_MEMBRANE_HALF_PITCH, chart, 0.0);
    height = (plane.w - dot(linePoint, plane.xyz)) / denominator;
    return isfinite(height);
}

// A use of the line: a measured cell of this chart and side whose plane is
// usable on the line.
bool M8MembraneUseHeight(int3 cell, int chart, uint side, int3 halfAddress,
    out float height)
{
    height = 0.0;
    if (!M8MembraneMeasuredAt(cell)) return false;
    if (M8MembraneChartOf(cell) != chart) return false;
    if (M8MembraneSignature(cell, chart) != side) return false;
    return M8MembraneLineHeight(M8MembranePlaneOf(cell), chart, halfAddress,
        height);
}

bool M8MembraneFreeInColumn(int3 halfAddress, int chart, int tangentAxis0,
    int tangentAxis1, uint column, int layerA, int layerB)
{
    int low = min(layerA, layerB);
    int high = max(layerA, layerB);
    [loop]
    for (int layer = low; layer <= high; layer++)
    {
        if (M8MembraneFreeAt(M8MembraneColumnCell(halfAddress, chart,
                tangentAxis0, tangentAxis1, column, layer)))
            return true;
    }
    return false;
}

// Relation R of two uses of one side: |dL| <= 2, |dh| <= g, no KNOWN FREE in
// either column between their layers.
bool M8MembraneRelated(int3 halfAddress, int chart, int tangentAxis0,
    int tangentAxis1, uint columnU, int layerU, float heightU, uint columnV,
    int layerV, float heightV)
{
    if (abs(layerU - layerV) > M8_MEMBRANE_MAX_LAYER_SPAN) return false;
    if (abs(heightU - heightV) > M8_MEMBRANE_BRANCH_GAP) return false;
    if (M8MembraneFreeInColumn(halfAddress, chart, tangentAxis0,
            tangentAxis1, columnU, layerU, layerV))
        return false;
    return !M8MembraneFreeInColumn(halfAddress, chart, tangentAxis0,
        tangentAxis1, columnV, layerU, layerV);
}

// (height, layer, column) order of the median and the admission seed.
bool M8MembraneOrderBefore(float heightA, int layerA, uint columnA,
    float heightB, int layerB, uint columnB)
{
    return heightA < heightB || (heightA == heightB &&
        (layerA < layerB || (layerA == layerB && columnA < columnB)));
}

// A contributor of the common window W: measured, usable plane, same side,
// no KNOWN FREE between its layer and the nearest member layer (inclusive).
bool M8MembraneCandidateHeight(int3 halfAddress, int chart, int tangentAxis0,
    int tangentAxis1, uint side, uint column, int layer,
    int nearestMemberLayer, out float height, out float3 normal)
{
    height = 0.0;
    normal = 0.0;
    int3 cell = M8MembraneColumnCell(halfAddress, chart, tangentAxis0,
        tangentAxis1, column, layer);
    if (!M8MembraneMeasuredAt(cell)) return false;
    float4 plane = M8MembranePlaneOf(cell);
    normal = plane.xyz;
    if (!M8MembraneLineHeight(plane, chart, halfAddress, height)) return false;
    if (M8MembraneSignature(cell, chart) != side) return false;
    int distance = abs(nearestMemberLayer - layer);
    int step = nearestMemberLayer > layer ? 1 : -1;
    [loop]
    for (int offset = 1; offset <= distance; offset++)
    {
        if (M8MembraneFreeAt(M8MembraneColumnCell(halfAddress, chart,
                tangentAxis0, tangentAxis1, column, layer + step * offset)))
            return false;
    }
    return true;
}

// Winner per column: nearest the reference, then nearer to a member layer,
// then the smaller layer.
bool M8MembraneWinnerBefore(float distance, int memberDistance, int layer,
    bool haveBest, float bestDistance, int bestMemberDistance, int bestLayer)
{
    return !haveBest ||
        distance < bestDistance - M8_MEMBRANE_NUMERICAL_EPSILON ||
        (abs(distance - bestDistance) <= M8_MEMBRANE_NUMERICAL_EPSILON &&
         (memberDistance < bestMemberDistance ||
          (memberDistance == bestMemberDistance && layer < bestLayer)));
}

uint M8MembranePackMeanColor(uint red, uint green, uint blue,
    uint weightTotal)
{
    float inverse = 1.0 / (float)max(1u, weightTotal);
    uint r = min(255u, (uint)floor((float)red * inverse + 0.5));
    uint g = min(255u, (uint)floor((float)green * inverse + 0.5));
    uint b = min(255u, (uint)floor((float)blue * inverse + 0.5));
    return r | (g << 8u) | (b << 16u) | 0xff000000u;
}

float3 M8MembraneNodeNormal(float3 normalSum, int chart)
{
    float lengthSquared = dot(normalSum, normalSum);
    return lengthSquared > 1.17549435e-38
        ? normalSum * rsqrt(lengthSquared)
        : (float3)M8MembraneAxis(chart);
}

float3 M8MembraneNodePosition(int3 halfAddress, int chart, float height)
{
    return M8MembraneSetFloatComponent(
        (float3)halfAddress * M8_MEMBRANE_HALF_PITCH, chart, height);
}

// ---- C5.3 patch ----

// Every edge <= 45 mm and every corner within g of the MAIN plane.
bool M8MembranePatchGuards(float3 p00, float3 p10, float3 p11, float3 p01,
    float4 plane)
{
    float maxEdge = M8_MEMBRANE_MAX_EDGE * M8_MEMBRANE_MAX_EDGE;
    float3 e0 = p00 - p10;
    float3 e1 = p10 - p11;
    float3 e2 = p11 - p01;
    float3 e3 = p01 - p00;
    if (dot(e0, e0) > maxEdge || dot(e1, e1) > maxEdge ||
        dot(e2, e2) > maxEdge || dot(e3, e3) > maxEdge)
        return false;
    return abs(dot(p00, plane.xyz) - plane.w) <= M8_MEMBRANE_BRANCH_GAP &&
        abs(dot(p10, plane.xyz) - plane.w) <= M8_MEMBRANE_BRANCH_GAP &&
        abs(dot(p11, plane.xyz) - plane.w) <= M8_MEMBRANE_BRANCH_GAP &&
        abs(dot(p01, plane.xyz) - plane.w) <= M8_MEMBRANE_BRANCH_GAP;
}

// ---- C5.4 chart transition ----

bool M8MembraneTransitionCompatible(int3 first, float4 firstPlane,
    int3 second, float4 secondPlane)
{
    if (dot(firstPlane.xyz, secondPlane.xyz) < M8_MEMBRANE_CHART_COSINE)
        return false;
    float3 firstCentre = (float3)first * M8_MEMBRANE_PATCH_PITCH;
    float3 secondCentre = (float3)second * M8_MEMBRANE_PATCH_PITCH;
    return abs(dot(secondCentre, firstPlane.xyz) - firstPlane.w) <=
            M8_MEMBRANE_BRANCH_GAP &&
        abs(dot(firstCentre, secondPlane.xyz) - secondPlane.w) <=
            M8_MEMBRANE_BRANCH_GAP;
}

// Quad a, b (MAIN edge) and the facing edge oriented co-directed with b - a:
// edges <= 45 mm, cos >= 0.5, both triangles non-degenerate and equally
// oriented. All four nodes lie in the plane t = +half, so the winding follows
// the reference normal when it has a component along that plane's normal and
// +t otherwise.
bool M8MembraneTransitionQuad(float3 a, float3 b, float3 low, float3 high,
    float3 referenceNormal, int tangentAxis, out bool matchHigh,
    out bool flip)
{
    flip = false;
    float3 edge = b - a;
    matchHigh = dot(edge, high - low) >= 0.0;
    float3 c = matchHigh ? high : low;
    float3 d = matchHigh ? low : high;
    float3 facing = c - d;
    float edgeLength = length(edge);
    float facingLength = length(facing);
    if (!(edgeLength > M8_MEMBRANE_NUMERICAL_EPSILON) ||
        !(facingLength > M8_MEMBRANE_NUMERICAL_EPSILON) ||
        dot(edge, facing) < M8_MEMBRANE_TRANSITION_EDGE_COSINE * edgeLength *
            facingLength)
        return false;
    float maxEdge = M8_MEMBRANE_MAX_EDGE * M8_MEMBRANE_MAX_EDGE;
    float3 ab = a - b;
    float3 bc = b - c;
    float3 cd = c - d;
    float3 da = d - a;
    if (dot(ab, ab) > maxEdge || dot(bc, bc) > maxEdge ||
        dot(cd, cd) > maxEdge || dot(da, da) > maxEdge)
        return false;
    float3 first = cross(b - a, c - a);
    float3 second = cross(c - a, d - a);
    float epsilon2 = M8_MEMBRANE_NUMERICAL_EPSILON *
        M8_MEMBRANE_NUMERICAL_EPSILON;
    if (!(dot(first, first) > epsilon2) || !(dot(second, second) > epsilon2) ||
        dot(first, second) <= 0.0)
        return false;
    float3 quadNormal = first + second;
    float orientation = dot(quadNormal, referenceNormal);
    if (abs(orientation) <= M8_MEMBRANE_NUMERICAL_EPSILON)
        orientation = quadNormal[tangentAxis];
    flip = orientation < 0.0;
    return true;
}

#endif
