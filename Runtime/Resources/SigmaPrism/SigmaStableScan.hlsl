#ifndef SIGMA_STABLE_SCAN_INCLUDED
#define SIGMA_STABLE_SCAN_INCLUDED

// One portable deterministic 256-lane exclusive-scan circuit.  Both streams are
// scalar uint and independent; the second stream lets ForwardReadout compact its
// current and dirty lists on the same fixed barrier schedule.  Single-stream
// callers pass zero through the thin wrapper below.  Stable increasing lane order
// is identical on every subgroup width and no execution packing is semantic.
groupshared uint SigmaStablePrimaryPing[256];
groupshared uint SigmaStablePrimaryPong[256];
groupshared uint SigmaStableSecondaryPing[256];
groupshared uint SigmaStableSecondaryPong[256];

void SigmaStableExclusiveScan256Dual(
    uint lane,
    uint primaryValue,
    uint secondaryValue,
    out uint primaryPrefix,
    out uint secondaryPrefix,
    out uint primaryTotal,
    out uint secondaryTotal)
{
    SigmaStablePrimaryPing[lane] = primaryValue;
    SigmaStableSecondaryPing[lane] = secondaryValue;
    GroupMemoryBarrierWithGroupSync();

    uint readPing = 1u;
    [unroll]
    for (uint offset = 1u; offset < 256u; offset <<= 1u)
    {
        uint primary = readPing != 0u
            ? SigmaStablePrimaryPing[lane]
            : SigmaStablePrimaryPong[lane];
        uint secondary = readPing != 0u
            ? SigmaStableSecondaryPing[lane]
            : SigmaStableSecondaryPong[lane];
        uint primaryPreceding = lane >= offset
            ? (readPing != 0u
                ? SigmaStablePrimaryPing[lane - offset]
                : SigmaStablePrimaryPong[lane - offset])
            : 0u;
        uint secondaryPreceding = lane >= offset
            ? (readPing != 0u
                ? SigmaStableSecondaryPing[lane - offset]
                : SigmaStableSecondaryPong[lane - offset])
            : 0u;

        if (readPing != 0u)
        {
            SigmaStablePrimaryPong[lane] = primary + primaryPreceding;
            SigmaStableSecondaryPong[lane] = secondary + secondaryPreceding;
        }
        else
        {
            SigmaStablePrimaryPing[lane] = primary + primaryPreceding;
            SigmaStableSecondaryPing[lane] = secondary + secondaryPreceding;
        }

        GroupMemoryBarrierWithGroupSync();
        readPing ^= 1u;
    }

    uint primaryInclusive = readPing != 0u
        ? SigmaStablePrimaryPing[lane]
        : SigmaStablePrimaryPong[lane];
    uint secondaryInclusive = readPing != 0u
        ? SigmaStableSecondaryPing[lane]
        : SigmaStableSecondaryPong[lane];
    primaryTotal = readPing != 0u
        ? SigmaStablePrimaryPing[255u]
        : SigmaStablePrimaryPong[255u];
    secondaryTotal = readPing != 0u
        ? SigmaStableSecondaryPing[255u]
        : SigmaStableSecondaryPong[255u];
    primaryPrefix = primaryInclusive - primaryValue;
    secondaryPrefix = secondaryInclusive - secondaryValue;
}

uint SigmaStableExclusiveScan256(
    uint lane,
    uint value,
    out uint total)
{
    uint prefix;
    uint unusedPrefix;
    uint unusedTotal;
    SigmaStableExclusiveScan256Dual(
        lane,
        value,
        0u,
        prefix,
        unusedPrefix,
        total,
        unusedTotal);
    return prefix;
}

#endif
