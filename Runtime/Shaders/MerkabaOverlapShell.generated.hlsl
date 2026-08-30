// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.
#ifndef GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED
#define GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED

#define M8_OVERLAP_QUARTERS_PER_STEP 4
#define M8_OVERLAP_SUPPORT_HALF_QUARTERS 4
#define M8_OVERLAP_TRIANGLES_PER_PATCH 2u
#define M8_OVERLAP_NORMAL_COUNT 13u

struct M8OverlapSignature
{
    uint normalIndex;
    uint chartAxis;
    int freeSign;
    int3 normal;
};

struct M8OverlapBranch
{
    uint normalIndex;
    uint chartAxis;
    int freeSign;
    int3 normal;
    uint tangentSupport;
    uint normalResidual;
    uint freeCoherence;
};

struct M8OverlapCorner
{
    int3 quarterCoordinate;
    uint packedColor;
};

struct M8OverlapPatch
{
    M8OverlapSignature signature;
    M8OverlapCorner corner00;
    M8OverlapCorner corner10;
    M8OverlapCorner corner11;
    M8OverlapCorner corner01;
};

int3 M8OverlapNormal(uint index)
{
    if (index == 0u) return int3(1, 0, 0);
    if (index == 1u) return int3(0, 1, 0);
    if (index == 2u) return int3(0, 0, 1);
    if (index == 3u) return int3(1, 1, 0);
    if (index == 4u) return int3(1, -1, 0);
    if (index == 5u) return int3(1, 0, 1);
    if (index == 6u) return int3(1, 0, -1);
    if (index == 7u) return int3(0, 1, 1);
    if (index == 8u) return int3(0, 1, -1);
    if (index == 9u) return int3(1, 1, 1);
    if (index == 10u) return int3(1, 1, -1);
    if (index == 11u) return int3(1, -1, 1);
    return int3(1, -1, -1);
}

uint M8OverlapFirstNonZeroAxis(int3 value)
{
    if (value.x != 0) return 0u;
    return value.y != 0 ? 1u : 2u;
}

void M8OverlapAxes(uint chartAxis, out int3 chartNormal,
    out int3 tangent0, out int3 tangent1)
{
    if (chartAxis == 0u)
    {
        chartNormal = int3(1, 0, 0);
        tangent0 = int3(0, 1, 0);
        tangent1 = int3(0, 0, 1);
    }
    else if (chartAxis == 1u)
    {
        chartNormal = int3(0, 1, 0);
        tangent0 = int3(0, 0, 1);
        tangent1 = int3(1, 0, 0);
    }
    else
    {
        chartNormal = int3(0, 0, 1);
        tangent0 = int3(1, 0, 0);
        tangent1 = int3(0, 1, 0);
    }
}

int2 M8OverlapTangentDirection(uint index)
{
    if (index == 0u) return int2(-1, -1);
    if (index == 1u) return int2(0, -1);
    if (index == 2u) return int2(1, -1);
    if (index == 3u) return int2(-1, 0);
    if (index == 4u) return int2(1, 0);
    if (index == 5u) return int2(-1, 1);
    if (index == 6u) return int2(0, 1);
    return int2(1, 1);
}

uint M8OverlapResidualScale(uint normalSquared)
{
    if (normalSquared == 1u) return 6u;
    return normalSquared == 2u ? 3u : 2u;
}

void M8OverlapFreeSide(int3 mainHaloCoord, int3 normal,
    out uint positive, out uint negative)
{
    positive = 0u;
    negative = 0u;
    [unroll]
    for (int z = -1; z <= 1; z++)
    [unroll]
    for (int y = -1; y <= 1; y++)
    [unroll]
    for (int x = -1; x <= 1; x++)
    {
        int3 offset = int3(x, y, z);
        int signedDistance = dot(offset, normal);
        int evidence = gM8ShellEvidence[M8ShellIndex(mainHaloCoord, offset)];
        if (evidence >= 0 || signedDistance == 0) continue;
        uint weight = (uint)(-evidence * abs(signedDistance));
        if (signedDistance > 0) positive += weight;
        else negative += weight;
    }
}

void M8OverlapColumnFreeSide(int3 mainHaloCoord, int3 normal,
    int3 chartNormal, int3 tangentOffset, out uint positive, out uint negative)
{
    positive = 0u;
    negative = 0u;
    [unroll]
    for (int normalOffset = -1; normalOffset <= 1; normalOffset++)
    {
        int3 offset = tangentOffset + chartNormal * normalOffset;
        int evidence = gM8ShellEvidence[M8ShellIndex(mainHaloCoord, offset)];
        int signedDistance = dot(offset, normal);
        if (evidence >= 0 || signedDistance == 0) continue;
        uint weight = (uint)(-evidence * abs(signedDistance));
        if (signedDistance > 0) positive += weight;
        else negative += weight;
    }
}

bool M8OverlapHasKnownFreeSeparator(int3 mainHaloCoord,
    int3 chartNormal, int3 tangentOffset, int normalOffset)
{
    if (normalOffset > 0)
    {
        [unroll]
        for (int step = 0; step < normalOffset; step++)
            if (gM8ShellEvidence[M8ShellIndex(mainHaloCoord,
                    tangentOffset + chartNormal * step)] < 0)
                return true;
    }
    if (normalOffset < 0)
    {
        [unroll]
        for (int step = 0; step > normalOffset; step--)
            if (gM8ShellEvidence[M8ShellIndex(mainHaloCoord,
                    tangentOffset + chartNormal * step)] < 0)
                return true;
    }
    return false;
}

bool M8TryOverlapContributor(int3 globalCoord, int3 mainHaloCoord,
    int3 normal, int3 chartNormal, int freeSign, int3 tangentOffset,
    out int selectedHeight, out uint selectedStateToken,
    out uint selectedResidual)
{
    uint positiveFree;
    uint negativeFree;
    M8OverlapColumnFreeSide(mainHaloCoord, normal, chartNormal,
        tangentOffset, positiveFree, negativeFree);
    if (positiveFree != negativeFree &&
        (positiveFree > negativeFree ? 1 : -1) != freeSign)
    {
        selectedHeight = 0;
        selectedStateToken = 0u;
        selectedResidual = 0u;
        return false;
    }

    bool found = false;
    int selectedOffset = 0;
    selectedHeight = 0;
    selectedStateToken = 0u;
    selectedResidual = 0u;
    uint chartAxis = M8OverlapFirstNonZeroAxis(normal);
    uint normalSquared = (uint)dot(normal, normal);
    [unroll]
    for (int normalOffset = -1; normalOffset <= 1; normalOffset++)
    {
        int3 offset = tangentOffset + chartNormal * normalOffset;
        uint sampleIndex = M8ShellIndex(mainHaloCoord, offset);
        if (gM8ShellOccupied[sampleIndex] == 0u) continue;
        if (M8OverlapHasKnownFreeSeparator(mainHaloCoord, chartNormal,
                tangentOffset, normalOffset))
            continue;
        uint residual = (uint)abs(dot(offset, normal));
        if (residual > normalSquared) continue;
        if (!found || abs(normalOffset) < abs(selectedOffset) ||
            (abs(normalOffset) == abs(selectedOffset) &&
             residual < selectedResidual))
        {
            found = true;
            selectedOffset = normalOffset;
            selectedResidual = residual;
            selectedHeight = globalCoord[chartAxis] + normalOffset;
            selectedStateToken = gM8ShellStateTokens[sampleIndex];
        }
    }
    return found;
}

bool M8OverlapHasNonCollinearSupport(uint supportMask)
{
    [unroll]
    for (uint first = 0u; first < 8u; first++)
    {
        if ((supportMask & (1u << first)) == 0u) continue;
        int2 a = M8OverlapTangentDirection(first);
        [unroll]
        for (uint second = first + 1u; second < 8u; second++)
        {
            if ((supportMask & (1u << second)) == 0u) continue;
            int2 b = M8OverlapTangentDirection(second);
            if (a.x * b.y - a.y * b.x != 0) return true;
        }
    }
    return false;
}

bool M8TryEvaluateOverlapBranch(int3 globalCoord, int3 mainHaloCoord,
    uint normalIndex, out M8OverlapBranch branch)
{
    int3 normal = M8OverlapNormal(normalIndex);
    uint chartAxis = M8OverlapFirstNonZeroAxis(normal);
    int3 chartNormal;
    int3 tangent0;
    int3 tangent1;
    M8OverlapAxes(chartAxis, chartNormal, tangent0, tangent1);
    uint positiveFree;
    uint negativeFree;
    M8OverlapFreeSide(mainHaloCoord, normal, positiveFree, negativeFree);
    if (positiveFree == negativeFree)
    {
        branch = (M8OverlapBranch)0;
        return false;
    }

    int freeSign = positiveFree > negativeFree ? 1 : -1;
    uint supportMask = 0u;
    uint support = 0u;
    uint residual = 0u;
    uint normalSquared = (uint)dot(normal, normal);
    [unroll]
    for (uint directionIndex = 0u; directionIndex < 8u; directionIndex++)
    {
        int2 direction = M8OverlapTangentDirection(directionIndex);
        int3 tangentOffset = tangent0 * direction.x + tangent1 * direction.y;
        int ignoredHeight;
        uint ignoredStateToken;
        uint contributorResidual;
        if (!M8TryOverlapContributor(globalCoord, mainHaloCoord, normal,
                chartNormal, freeSign, tangentOffset, ignoredHeight,
                ignoredStateToken, contributorResidual))
            continue;
        supportMask |= 1u << directionIndex;
        support++;
        residual += contributorResidual * contributorResidual *
            M8OverlapResidualScale(normalSquared);
    }
    if (support < 2u || !M8OverlapHasNonCollinearSupport(supportMask))
    {
        branch = (M8OverlapBranch)0;
        return false;
    }

    branch.normalIndex = normalIndex;
    branch.chartAxis = chartAxis;
    branch.freeSign = freeSign;
    branch.normal = normal;
    branch.tangentSupport = support;
    branch.normalResidual = residual;
    branch.freeCoherence = positiveFree > negativeFree
        ? positiveFree - negativeFree : negativeFree - positiveFree;
    return true;
}

int M8CompareOverlapBranch(M8OverlapBranch left, M8OverlapBranch right)
{
    if (left.tangentSupport != right.tangentSupport)
        return left.tangentSupport > right.tangentSupport ? 1 : -1;
    if (left.normalResidual != right.normalResidual)
        return left.normalResidual < right.normalResidual ? 1 : -1;
    if (left.freeCoherence != right.freeCoherence)
        return left.freeCoherence > right.freeCoherence ? 1 : -1;
    return 0;
}

bool M8TryDeriveOverlapSignature(int3 globalCoord, int3 mainHaloCoord,
    out M8OverlapSignature signature)
{
    bool found = false;
    bool tied = false;
    M8OverlapBranch best = (M8OverlapBranch)0;
    [unroll]
    for (uint normalIndex = 0u; normalIndex < M8_OVERLAP_NORMAL_COUNT;
         normalIndex++)
    {
        M8OverlapBranch candidate;
        if (!M8TryEvaluateOverlapBranch(globalCoord, mainHaloCoord,
                normalIndex, candidate))
            continue;
        int comparison = found ? M8CompareOverlapBranch(candidate, best) : 1;
        if (comparison > 0)
        {
            best = candidate;
            found = true;
            tied = false;
        }
        else if (comparison == 0 && candidate.normalIndex != best.normalIndex)
            tied = true;
    }
    if (!found || tied)
    {
        signature = (M8OverlapSignature)0;
        return false;
    }
    signature.normalIndex = best.normalIndex;
    signature.chartAxis = best.chartAxis;
    signature.freeSign = best.freeSign;
    signature.normal = best.normal;
    return true;
}

void M8OverlapMedianBand(int4 values, uint count,
    out int lower, out int upper)
{
    if (count == 1u)
    {
        lower = upper = values.x;
        return;
    }
    if (count == 2u)
    {
        lower = min(values.x, values.y);
        upper = max(values.x, values.y);
        return;
    }
    if (count == 3u)
    {
        int minimum = min(values.x, min(values.y, values.z));
        int maximum = max(values.x, max(values.y, values.z));
        lower = upper = values.x + values.y + values.z - minimum - maximum;
        return;
    }
    int x = values.x;
    int y = values.y;
    int z = values.z;
    int w = values.w;
    int swap;
    if (x > y) { swap = x; x = y; y = swap; }
    if (z > w) { swap = z; z = w; w = swap; }
    if (x > z) { swap = x; x = z; z = swap; }
    if (y > w) { swap = y; y = w; w = swap; }
    if (y > z) { swap = y; y = z; z = swap; }
    lower = y;
    upper = z;
}

void M8AppendOverlapContributor(int height, uint stateToken,
    inout int4 heights, inout uint4 stateTokens, inout uint count)
{
    if (count == 0u) { heights.x = height; stateTokens.x = stateToken; }
    else if (count == 1u) { heights.y = height; stateTokens.y = stateToken; }
    else if (count == 2u) { heights.z = height; stateTokens.z = stateToken; }
    else { heights.w = height; stateTokens.w = stateToken; }
    count++;
}

void M8CollectOverlapContributor(int3 globalCoord, int3 mainHaloCoord,
    M8OverlapSignature signature, int3 chartNormal, int3 tangentOffset,
    inout int4 heights, inout uint4 stateTokens, inout uint count)
{
    int height;
    uint stateToken;
    uint residual;
    if (M8TryOverlapContributor(globalCoord, mainHaloCoord, signature.normal,
            chartNormal, signature.freeSign, tangentOffset, height, stateToken,
            residual))
        M8AppendOverlapContributor(height, stateToken, heights, stateTokens,
            count);
}

void M8AccumulateOverlapColor(int height, uint stateToken,
    int lowerHeight, int upperHeight, inout uint4 colorTotal,
    inout uint total)
{
    if (height < lowerHeight || height > upperHeight || stateToken == 0u)
        return;
    uint stateIndex = stateToken - 1u;
    KernelState state = M8LoadKernelStateRead(stateIndex >> 9u,
        stateIndex & 511u);
    uint weight = min(state.colorConfidence, 65535u);
    if (weight == 0u) return;
    uint color = state.packedColor;
    colorTotal += uint4(color & 255u, (color >> 8u) & 255u,
        (color >> 16u) & 255u, (color >> 24u) & 255u) * weight;
    total += weight;
}

uint M8ReduceOverlapColor(int4 heights, uint4 stateTokens,
    uint count, int lowerHeight, int upperHeight)
{
    uint4 colorTotal = uint4(0u, 0u, 0u, 0u);
    uint total = 0u;
    if (count > 0u) M8AccumulateOverlapColor(heights.x, stateTokens.x,
        lowerHeight, upperHeight, colorTotal, total);
    if (count > 1u) M8AccumulateOverlapColor(heights.y, stateTokens.y,
        lowerHeight, upperHeight, colorTotal, total);
    if (count > 2u) M8AccumulateOverlapColor(heights.z, stateTokens.z,
        lowerHeight, upperHeight, colorTotal, total);
    if (count > 3u) M8AccumulateOverlapColor(heights.w, stateTokens.w,
        lowerHeight, upperHeight, colorTotal, total);
    if (total == 0u) return 0x00a0a0a0u;
    float inverseTotal = rcp((float)total);
    uint4 reduced = (uint4)floor((float4)colorTotal * inverseTotal + 0.5);
    return reduced.x | (reduced.y << 8u) | (reduced.z << 16u) |
        (reduced.w << 24u);
}

M8OverlapCorner M8BuildOverlapCorner(int3 globalCoord, int3 mainHaloCoord,
    M8OverlapSignature signature, int3 chartNormal, int3 tangent0,
    int3 tangent1, int tangentSign0, int tangentSign1)
{
    int4 heights = int4(0, 0, 0, 0);
    uint4 stateTokens = uint4(0u, 0u, 0u, 0u);
    uint count = 0u;
    M8CollectOverlapContributor(globalCoord, mainHaloCoord, signature,
        chartNormal, int3(0, 0, 0), heights, stateTokens, count);
    M8CollectOverlapContributor(globalCoord, mainHaloCoord, signature,
        chartNormal, tangent0 * tangentSign0, heights, stateTokens, count);
    M8CollectOverlapContributor(globalCoord, mainHaloCoord, signature,
        chartNormal, tangent1 * tangentSign1, heights, stateTokens, count);
    M8CollectOverlapContributor(globalCoord, mainHaloCoord, signature,
        chartNormal, tangent0 * tangentSign0 + tangent1 * tangentSign1,
        heights, stateTokens, count);

    int lowerHeight;
    int upperHeight;
    M8OverlapMedianBand(heights, count, lowerHeight, upperHeight);
    M8OverlapCorner corner;
    corner.quarterCoordinate = globalCoord * M8_OVERLAP_QUARTERS_PER_STEP +
        tangent0 * (tangentSign0 * M8_OVERLAP_SUPPORT_HALF_QUARTERS) +
        tangent1 * (tangentSign1 * M8_OVERLAP_SUPPORT_HALF_QUARTERS);
    corner.quarterCoordinate[signature.chartAxis] =
        2 * (lowerHeight + upperHeight);
    corner.packedColor = M8ReduceOverlapColor(heights, stateTokens, count,
        lowerHeight, upperHeight);
    return corner;
}

bool M8TryBuildOverlapPatch(int3 globalCoord, int3 mainHaloCoord,
    out M8OverlapPatch patch)
{
    if (gM8ShellOccupied[M8ShellIndex(mainHaloCoord, int3(0, 0, 0))] == 0u)
    {
        patch = (M8OverlapPatch)0;
        return false;
    }
    M8OverlapSignature signature;
    if (!M8TryDeriveOverlapSignature(globalCoord, mainHaloCoord, signature))
    {
        patch = (M8OverlapPatch)0;
        return false;
    }
    int3 chartNormal;
    int3 tangent0;
    int3 tangent1;
    M8OverlapAxes(signature.chartAxis, chartNormal, tangent0, tangent1);
    patch.signature = signature;
    patch.corner00 = M8BuildOverlapCorner(globalCoord, mainHaloCoord,
        signature, chartNormal, tangent0, tangent1, -1, -1);
    patch.corner10 = M8BuildOverlapCorner(globalCoord, mainHaloCoord,
        signature, chartNormal, tangent0, tangent1, 1, -1);
    patch.corner11 = M8BuildOverlapCorner(globalCoord, mainHaloCoord,
        signature, chartNormal, tangent0, tangent1, 1, 1);
    patch.corner01 = M8BuildOverlapCorner(globalCoord, mainHaloCoord,
        signature, chartNormal, tangent0, tangent1, -1, 1);
    return true;
}

M8OverlapCorner M8OverlapPatchCorner(M8OverlapPatch patch, uint index)
{
    if (index == 0u) return patch.corner00;
    if (index == 1u) return patch.corner10;
    if (index == 2u) return patch.corner11;
    return patch.corner01;
}

uint M8OverlapTriangleCorner(int freeSign, uint vertex)
{
    bool forward = freeSign > 0;
    if (vertex == 0u) return 0u;
    if (vertex == 1u) return forward ? 1u : 2u;
    if (vertex == 2u) return forward ? 2u : 1u;
    if (vertex == 3u) return 0u;
    if (vertex == 4u) return forward ? 2u : 3u;
    return forward ? 3u : 2u;
}

#endif
