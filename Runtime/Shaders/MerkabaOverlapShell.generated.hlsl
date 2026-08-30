// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.
#ifndef GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED
#define GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED

#define M8_OVERLAP_QUARTERS_PER_STEP 4
#define M8_OVERLAP_HALF_STEP_QUARTERS 2
#define M8_OVERLAP_TRIANGLES_PER_PATCH 2u

struct M8OverlapSignature
{
    uint normalAxis;
    int freeSign;
    uint hasKnownFreeSide;
};

struct M8OverlapCorner
{
    int3 quarterCoordinate;
    uint packedColor;
};

void M8OverlapAxes(uint normalAxis, out int3 normal, out int3 tangent0, out int3 tangent1)
{
    if (normalAxis == 0u)
    {
        normal = int3(1, 0, 0);
        tangent0 = int3(0, 1, 0);
        tangent1 = int3(0, 0, 1);
    }
    else if (normalAxis == 1u)
    {
        normal = int3(0, 1, 0);
        tangent0 = int3(0, 0, 1);
        tangent1 = int3(1, 0, 0);
    }
    else
    {
        normal = int3(0, 0, 1);
        tangent0 = int3(1, 0, 0);
        tangent1 = int3(0, 1, 0);
    }
}

uint M8OverlapMinimumAxis(int3 value)
{
    if (value.x <= value.y && value.x <= value.z) return 0u;
    return value.y <= value.z ? 1u : 2u;
}

void M8OverlapMedianBand(int4 values, uint count, out int lower, out int upper)
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
        int median = values.x + values.y + values.z - min(values.x, min(values.y, values.z)) - max(values.x, max(values.y, values.z));
        lower = upper = median;
        return;
    }
    int x = values.x; int y = values.y;
    int z = values.z; int w = values.w; int swap;
    if (x > y) { swap = x; x = y; y = swap; }
    if (z > w) { swap = z; z = w; w = swap; }
    if (x > z) { swap = x; x = z; z = swap; }
    if (y > w) { swap = y; y = w; w = swap; }
    if (y > z) { swap = y; y = z; z = swap; }
    lower = y; upper = z;
}

int M8OverlapMedianQuarterHeight(int4 values, uint count)
{
    int lower = 0; int upper = 0;
    M8OverlapMedianBand(values, count, lower, upper);
    return 2 * (lower + upper);
}

uint M8OverlapTriangleCorner(int freeSign, uint vertex)
{
    bool forward = freeSign > 0;
    switch (vertex)
    {
        case 0u: return forward ? 0u : 0u;
        case 1u: return forward ? 1u : 2u;
        case 2u: return forward ? 2u : 1u;
        case 3u: return forward ? 0u : 0u;
        case 4u: return forward ? 2u : 3u;
        case 5u: return forward ? 3u : 2u;
        default: return 0u;
    }
}

#endif
