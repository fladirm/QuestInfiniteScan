// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.
#ifndef GENESIS_MERKABA_SURFACE_ORIENTATION_INCLUDED
#define GENESIS_MERKABA_SURFACE_ORIENTATION_INCLUDED

#define MERKABA_SURFACE_ORIENTATION_COUNT 13u
#define MERKABA_SURFACE_ORIENTATION_SHIFT 2u
#define MERKABA_SURFACE_ORIENTATION_MASK 0x3cu

int3 M8CanonicalSurfaceOrientationNormal(uint index)
{
    int3 value = int3(1, -1, -1);
    if (index == 0u) value = int3(1, 0, 0);
    else if (index == 1u) value = int3(0, 1, 0);
    else if (index == 2u) value = int3(0, 0, 1);
    else if (index == 3u) value = int3(1, 1, 0);
    else if (index == 4u) value = int3(1, -1, 0);
    else if (index == 5u) value = int3(1, 0, 1);
    else if (index == 6u) value = int3(1, 0, -1);
    else if (index == 7u) value = int3(0, 1, 1);
    else if (index == 8u) value = int3(0, 1, -1);
    else if (index == 9u) value = int3(1, 1, 1);
    else if (index == 10u) value = int3(1, 1, -1);
    else if (index == 11u) value = int3(1, -1, 1);
    return value;
}

uint M8SelectCanonicalSurfaceOrientation(float3 normalGrid)
{
    float3 normalized = normalize(normalGrid);
    uint bestIndex = 0u;
    float bestAlignment = -1.0;
    [loop]
    for (uint index = 0u; index < MERKABA_SURFACE_ORIENTATION_COUNT; index++)
    {
        float3 branch = normalize((float3)M8CanonicalSurfaceOrientationNormal(index));
        float alignment = abs(dot(normalized, branch));
        if (alignment > bestAlignment)
        {
            bestAlignment = alignment;
            bestIndex = index;
        }
    }
    return bestIndex;
}

uint M8GetSurfaceOrientation(uint flags)
{
    return (flags & MERKABA_SURFACE_ORIENTATION_MASK) >> MERKABA_SURFACE_ORIENTATION_SHIFT;
}

uint M8SetSurfaceOrientation(uint flags, uint branchIndex)
{
    uint encoded = (branchIndex + 1u) << MERKABA_SURFACE_ORIENTATION_SHIFT;
    return (flags & ~MERKABA_SURFACE_ORIENTATION_MASK) | encoded;
}

uint M8ClearSurfaceOrientation(uint flags)
{
    return flags & ~MERKABA_SURFACE_ORIENTATION_MASK;
}

#endif
