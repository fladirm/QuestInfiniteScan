#ifndef GENESIS_MERKABA_OBSERVATION_REDUCTION_INCLUDED
#define GENESIS_MERKABA_OBSERVATION_REDUCTION_INCLUDED

#include "MerkabaSphereFlower.generated.hlsl"
#include "MerkabaFlowerTileHalo.hlsl"

// One relation bucket per local M8 owner. The caller supplies CERTAIN generated
// root/sector intervals, never unclassified central roots. All three stages
// are separated by workgroup barriers in FlowerCommit. No global winner bank.
#define M8_FLOWER_REDUCTION_OWNER_COUNT 512u
#define M8_FLOWER_OBSERVATION_IDENTITY_MASK 0x00003fffu
#define M8_FLOWER_OBSERVATION_CONFLICT_BIT 0x80000000u
#define M8_FLOWER_OBSERVATION_PIXEL_MASK 0x0003ffffu
#define M8_FLOWER_OBSERVATION_WIDTH_MAX 4095u

groupshared uint m8FlowerTagIntersection[M8_FLOWER_REDUCTION_OWNER_COUNT];
groupshared uint m8FlowerTagUnion[M8_FLOWER_REDUCTION_OWNER_COUNT];
// Bias signed Q2.29 endpoints into monotonically ordered uints so the atomic
// comparison has the same signed semantics in every Unity/native backend.
groupshared uint m8FlowerLower[M8_FLOWER_REDUCTION_OWNER_COUNT];
groupshared uint m8FlowerUpper[M8_FLOWER_REDUCTION_OWNER_COUNT];
groupshared uint m8FlowerRootLowerY[M8_FLOWER_REDUCTION_OWNER_COUNT];
groupshared uint m8FlowerRootUpperY[M8_FLOWER_REDUCTION_OWNER_COUNT];
groupshared uint m8FlowerRepresentative[M8_FLOWER_REDUCTION_OWNER_COUNT];

void M8FlowerResetObservationBucket(uint kernelLocal)
{
    m8FlowerTagIntersection[kernelLocal] = 0xffffffffu;
    m8FlowerTagUnion[kernelLocal] = 0u;
    m8FlowerLower[kernelLocal] = 0u;
    m8FlowerUpper[kernelLocal] = 0xffffffffu;
    m8FlowerRootLowerY[kernelLocal] = 0u;
    m8FlowerRootUpperY[kernelLocal] = 0xffffffffu;
    m8FlowerRepresentative[kernelLocal] = 0xffffffffu;
}

void M8FlowerIntersectObservation(uint kernelLocal, uint symbolTag,
    int lower, int upper)
{
    uint identity = symbolTag & M8_FLOWER_OBSERVATION_IDENTITY_MASK;
    InterlockedAnd(m8FlowerTagIntersection[kernelLocal], identity);
    InterlockedOr(m8FlowerTagUnion[kernelLocal], identity |
        (lower > upper ? M8_FLOWER_OBSERVATION_CONFLICT_BIT : 0u));
    InterlockedMax(m8FlowerLower[kernelLocal], asuint(lower) ^ 0x80000000u);
    InterlockedMin(m8FlowerUpper[kernelLocal], asuint(upper) ^ 0x80000000u);
}

bool M8FlowerObservationBucketCertain(uint kernelLocal)
{
    return m8FlowerTagIntersection[kernelLocal] ==
            m8FlowerTagUnion[kernelLocal] &&
        m8FlowerLower[kernelLocal] <= m8FlowerUpper[kernelLocal] &&
        m8FlowerRootLowerY[kernelLocal] <= m8FlowerRootUpperY[kernelLocal];
}

uint M8FlowerOrderedFloat(float value)
{
    // Signed zeros are the same interval endpoint. Keeping two ordered keys
    // would make [+0,+0] intersect [-0,-0] as an empty interval.
    uint bits = value == 0.0 ? 0u : asuint(value);
    return (bits & 0x80000000u) != 0u ? ~bits : bits ^ 0x80000000u;
}

float M8FlowerFromOrderedFloat(uint value)
{
    return asfloat((value & 0x80000000u) != 0u ?
        value ^ 0x80000000u : ~value);
}

// Reuse the owner scratch for each fixed directed relation/root sign within
// the same tile workgroup. Roots of different relations are never pooled.
// Both coordinates of the analytic root are intersected before selecting a
// representative; an overlapping x interval alone cannot certify a match.

void M8FlowerIntersectRoot(uint kernelLocal, uint symbolTag,
    M8FlowerInterval2 root)
{
    InterlockedAnd(m8FlowerTagIntersection[kernelLocal], symbolTag);
    InterlockedOr(m8FlowerTagUnion[kernelLocal], symbolTag);
    InterlockedMax(m8FlowerLower[kernelLocal], M8FlowerOrderedFloat(root.x.lo));
    InterlockedMin(m8FlowerUpper[kernelLocal], M8FlowerOrderedFloat(root.x.hi));
    InterlockedMax(m8FlowerRootLowerY[kernelLocal], M8FlowerOrderedFloat(root.y.lo));
    InterlockedMin(m8FlowerRootUpperY[kernelLocal], M8FlowerOrderedFloat(root.y.hi));
}

bool M8FlowerIntersectClassifiedRoot(uint kernelLocal, uint level,
    uint lineClass, bool endpointOrientation, bool plusRoot,
    M8FlowerInterval3 abc, out uint symbolTag)
{
    M8FlowerInterval2 root;
    uint classification;
    if (!M8FlowerClassifyObservationRoot(level, lineClass,
            endpointOrientation, plusRoot, abc, symbolTag, root, classification))
    {
        if (classification == M8_FLOWER_ROOT_AMBIGUOUS)
            InterlockedOr(m8FlowerTagUnion[kernelLocal],
                M8_FLOWER_OBSERVATION_CONFLICT_BIT);
        return false;
    }
    M8FlowerIntersectRoot(kernelLocal, symbolTag, root);
    return true;
}

// Ordering only, never geometry admission: class, normalized unit-circle
// enclosure width, source pixel. Twelve width bits plus eighteen pixel bits
// leave the high bit clear even at maximum width/pixel, so UINT_MAX remains
// an unambiguous empty representative. Width quantization cannot resolve a
// conflicting bucket because selection runs after the intersection barrier.
uint M8FlowerObservationPrecisionKey(uint classification,
    M8FlowerInterval2 root, uint sourcePixel)
{
    if (sourcePixel > M8_FLOWER_OBSERVATION_PIXEL_MASK ||
        (classification != M8_FLOWER_ROOT_CERTAIN_SECANT &&
         classification != M8_FLOWER_ROOT_CERTAIN_TANGENT)) return 0xffffffffu;
    precise float widthX = root.x.hi - root.x.lo;
    precise float widthY = root.y.hi - root.y.lo;
    precise float normalized = saturate(max(widthX, widthY) * 0.5);
    uint width = (uint)ceil(normalized * float(M8_FLOWER_OBSERVATION_WIDTH_MAX));
    uint intervalClass = classification == M8_FLOWER_ROOT_CERTAIN_TANGENT ? 0u : 1u;
    return (intervalClass << 30u) | (width << 18u) | sourcePixel;
}

void M8FlowerSelectRootRepresentative(uint kernelLocal, uint symbolTag,
    uint classification, M8FlowerInterval2 root, uint sourcePixel)
{
    if (!M8FlowerObservationBucketCertain(kernelLocal) ||
        m8FlowerTagIntersection[kernelLocal] != symbolTag) return;
    uint key = M8FlowerObservationPrecisionKey(classification, root, sourcePixel);
    InterlockedMin(m8FlowerRepresentative[kernelLocal], key);
}

bool M8FlowerReadRootBucket(uint kernelLocal, out uint symbolTag,
    out M8FlowerInterval2 root, out uint sourcePixel)
{
    symbolTag = m8FlowerTagIntersection[kernelLocal];
    root.x = M8FlowerI(M8FlowerFromOrderedFloat(m8FlowerLower[kernelLocal]),
        M8FlowerFromOrderedFloat(m8FlowerUpper[kernelLocal]));
    root.y = M8FlowerI(M8FlowerFromOrderedFloat(m8FlowerRootLowerY[kernelLocal]),
        M8FlowerFromOrderedFloat(m8FlowerRootUpperY[kernelLocal]));
    uint representative = m8FlowerRepresentative[kernelLocal];
    sourcePixel = representative & M8_FLOWER_OBSERVATION_PIXEL_MASK;
    return M8FlowerObservationBucketCertain(kernelLocal) &&
        representative != 0xffffffffu;
}

// This stage runs only AFTER all interval intersections and conflict bits are
// visible. A PrecisionKey can therefore never choose between incompatible
// sectors, root signs, orientations, levels, or line classes.
void M8FlowerSelectObservationRepresentative(uint kernelLocal,
    uint precisionKey)
{
    if (M8FlowerObservationBucketCertain(kernelLocal))
        InterlockedMin(m8FlowerRepresentative[kernelLocal], precisionKey);
}

bool M8FlowerReadObservationBucket(uint kernelLocal, out uint identity,
    out int lower, out int upper, out uint sourcePixel)
{
    identity = m8FlowerTagIntersection[kernelLocal];
    lower = asint(m8FlowerLower[kernelLocal] ^ 0x80000000u);
    upper = asint(m8FlowerUpper[kernelLocal] ^ 0x80000000u);
    uint representative = m8FlowerRepresentative[kernelLocal];
    sourcePixel = representative & M8_FLOWER_OBSERVATION_PIXEL_MASK;
    return M8FlowerObservationBucketCertain(kernelLocal) &&
        representative != 0xffffffffu;
}

// Compatibility of two quantized carriers on the SAME arithmetic owner.
// Failure preserves the existing R1. It cannot choose a different sheet by
// normal angle, distance, confidence, or a nearest-root match.
bool M8FlowerCompatibleCarrier(uint previousFlags, uint incomingFlags,
    float normalUncertainty, float offsetUncertainty)
{
    if (!M8FlowerHasPlane(previousFlags)) return true;
    if (!M8FlowerHasPlane(incomingFlags) ||
        M8FlowerPlaneFreeSide(previousFlags) !=
        M8FlowerPlaneFreeSide(incomingFlags)) return false;
    if ((previousFlags & M8_FLOWER_PLANE_STORAGE_MASK) ==
        (incomingFlags & M8_FLOWER_PLANE_STORAGE_MASK)) return true;
    uint2 flags=uint2(previousFlags,incomingFlags);
    float3 normals[2];float offsets[2];
    // Preserve previous -> incoming evaluation, with one syntactic copy of
    // the exact decoder/root algebra in the native HLSL legalization path.
    [loop]for(uint endpoint=0u;endpoint<2u;endpoint++)
        M8FlowerUnpackPlane(flags[endpoint],normals[endpoint],offsets[endpoint]);
    bool isolated = false;
    [loop]
    for (uint direction = 0u; direction < 6u; ++direction)
    {
        uint lineClass = (uint)M8FlowerDirection[direction].w;
        precise float3 relative = float3(M8FlowerDirection[direction].xyz) *
            (M8_FLOWER_LATTICE_STEP * 0.5);
        M8FlowerInterval3 abc[2];
        [loop]for(uint endpoint=0u;endpoint<2u;endpoint++)
            if(!M8FlowerPlaneIntervals(normals[endpoint],offsets[endpoint],relative,
                M8_FLOWER_R1_RADIUS,lineClass,normalUncertainty,offsetUncertainty,
                abc[endpoint]))return false;
        [loop]
        for (uint rootSign = 0u; rootSign < 2u; ++rootSign)
        {
            M8FlowerInterval2 roots[2];uint2 classes=0u;
            [loop]for(uint endpoint=0u;endpoint<2u;endpoint++)
                classes[endpoint]=M8FlowerRootInterval(abc[endpoint],rootSign!=0u,roots[endpoint]);
            uint previousClass=classes.x,incomingClass=classes.y;
            if (previousClass == M8_FLOWER_ROOT_AMBIGUOUS ||
                incomingClass != previousClass) return false;
            if (previousClass == M8_FLOWER_ROOT_IMPOSSIBLE ||
                previousClass == M8_FLOWER_ROOT_COPLANAR) continue;
            if (rootSign != 0u &&
                previousClass == M8_FLOWER_ROOT_CERTAIN_TANGENT) continue;
            uint2 sectors=0u;
            [loop]for(uint endpoint=0u;endpoint<2u;endpoint++)
                if(!M8FlowerRootSector(lineClass,roots[endpoint],sectors[endpoint]))return false;
            if(sectors.x!=sectors.y ||
                max(roots[0].x.lo,roots[1].x.lo)>min(roots[0].x.hi,roots[1].x.hi) ||
                max(roots[0].y.lo,roots[1].y.lo)>min(roots[0].y.hi,roots[1].y.hi))return false;
            isolated = true;
        }
    }
    return isolated;
}

// A repeated packed plane is not itself a root certificate. Seed promotion
// needs a CERTAIN correspondence on one fixed directed R1 relation, with the
// same algebraic sign and generated sector in both immutable observations.
bool M8FlowerSeedCorrespondence(uint previousFlags, uint incomingFlags,
    float normalUncertainty, float offsetUncertainty)
{
    if (!M8FlowerHasPlane(previousFlags) || !M8FlowerHasPlane(incomingFlags) ||
        M8FlowerPlaneFreeSide(previousFlags) != M8FlowerPlaneFreeSide(incomingFlags))
        return false;
    float3 previousNormal, incomingNormal;
    float previousOffset, incomingOffset;
    M8FlowerUnpackPlane(previousFlags,previousNormal,previousOffset);
    M8FlowerUnpackPlane(incomingFlags,incomingNormal,incomingOffset);
    [loop] for (uint direction = 0u; direction < 6u; ++direction)
    {
        uint lineClass = (uint)M8FlowerDirection[direction].w;
        precise float3 relative = float3(M8FlowerDirection[direction].xyz)*
            (M8_FLOWER_LATTICE_STEP*0.5);
        M8FlowerInterval3 previousAbc, incomingAbc;
        if (!M8FlowerPlaneIntervals(previousNormal,previousOffset,relative,
                M8_FLOWER_R1_RADIUS,lineClass,normalUncertainty,
                offsetUncertainty,previousAbc) ||
            !M8FlowerPlaneIntervals(incomingNormal,incomingOffset,relative,
                M8_FLOWER_R1_RADIUS,lineClass,normalUncertainty,
                offsetUncertainty,incomingAbc)) continue;
        [loop] for (uint sign = 0u; sign < 2u; ++sign)
        {
            uint previousTag, incomingTag, previousClass, incomingClass;
            M8FlowerInterval2 previousRoot, incomingRoot;
            if (!M8FlowerClassifyObservationRoot(0u,lineClass,
                    (direction & 1u) != 0u,sign != 0u,previousAbc,
                    previousTag,previousRoot,previousClass) ||
                !M8FlowerClassifyObservationRoot(0u,lineClass,
                    (direction & 1u) != 0u,sign != 0u,incomingAbc,
                    incomingTag,incomingRoot,incomingClass)) continue;
            uint previousFlag,incomingFlag;
            if (previousTag == incomingTag &&
                M8FlowerR1SectorFlag(direction,(previousTag>>8u)&31u,previousFlag) &&
                M8FlowerR1SectorFlag(direction,(incomingTag>>8u)&31u,incomingFlag) &&
                previousFlag==incomingFlag &&
                max(previousRoot.x.lo,incomingRoot.x.lo) <=
                    min(previousRoot.x.hi,incomingRoot.x.hi) &&
                max(previousRoot.y.lo,incomingRoot.y.lo) <=
                    min(previousRoot.y.hi,incomingRoot.y.hi)) return true;
        }
    }
    return false;
}

#endif
