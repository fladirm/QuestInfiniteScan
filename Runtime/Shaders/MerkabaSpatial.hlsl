#ifndef MERKABA_SPATIAL_INCLUDED
#define MERKABA_SPATIAL_INCLUDED

#define MERKABA_M8_BLOCK_KERNEL_SPAN 256
#define MERKABA_M8_BLOCK_CHUNK_COUNT 512u
#define MERKABA_M8_TILES_PER_CHUNK 64u
#define MERKABA_M8_KERNELS_PER_TILE 512u
#define MERKABA_M8_HASH_BUCKET_COUNT 8192u
#define MERKABA_M8_HASH_BUCKET_MASK 8191u
#define MERKABA_M8_HASH_SLOTS_PER_BUCKET 4u

#define MERKABA_REF_EMPTY 0u
#define MERKABA_REF_CLAIMED_NEW 0xffffffffu
#define MERKABA_REF_COLD_ON_SSD 0xfffffffeu
#define MERKABA_REF_LOADING 0xfffffffdu
#define MERKABA_REF_EVICTING 0xfffffffcu

struct MerkabaM8Address
{
    int3 blockCoord;
    uint3 local;
    uint d4;
    uint d3;
    uint d2;
    uint d1;
    uint d0;
    uint chunkLocal;
    uint tileLocal;
    uint kernelLocal;
};

void MerkabaFloorDivMod256(int value, out int quotient, out uint remainder)
{
    uint bits = asuint(value);
    bool negative = value < 0;
    uint magnitude = negative ? (~bits + 1u) : bits;
    uint magnitudeRemainder = magnitude & 255u;
    uint magnitudeWhole = magnitude >> 8u;
    quotient = negative
        ? -(int)magnitudeWhole - (magnitudeRemainder != 0u ? 1 : 0)
        : (int)magnitudeWhole;
    remainder = negative && magnitudeRemainder != 0u
        ? 256u - magnitudeRemainder
        : magnitudeRemainder;
}

uint MerkabaOctantDigit(uint3 local, uint bit)
{
    return ((local.x >> bit) & 1u) |
        (((local.y >> bit) & 1u) << 1u) |
        (((local.z >> bit) & 1u) << 2u);
}

MerkabaM8Address MerkabaAddressFromBlockLocal(int3 blockCoord, uint3 local)
{
    MerkabaM8Address address = (MerkabaM8Address)0;
    address.blockCoord = blockCoord;
    address.local = local;
    address.d4 = MerkabaOctantDigit(local, 7u);
    address.d3 = MerkabaOctantDigit(local, 6u);
    address.d2 = MerkabaOctantDigit(local, 5u);
    address.d1 = MerkabaOctantDigit(local, 4u);
    address.d0 = MerkabaOctantDigit(local, 3u);
    address.chunkLocal = (address.d4 << 6u) |
        (address.d3 << 3u) | address.d2;
    address.tileLocal = (address.d1 << 3u) | address.d0;
    uint3 kernel = local & 7u;
    address.kernelLocal = kernel.x + 8u * (kernel.y + 8u * kernel.z);
    return address;
}

MerkabaM8Address MerkabaAddressOf(int3 globalCoord)
{
    int3 blockCoord;
    uint3 local;
    MerkabaFloorDivMod256(globalCoord.x, blockCoord.x, local.x);
    MerkabaFloorDivMod256(globalCoord.y, blockCoord.y, local.y);
    MerkabaFloorDivMod256(globalCoord.z, blockCoord.z, local.z);
    return MerkabaAddressFromBlockLocal(blockCoord, local);
}

uint3 MerkabaPcg3d(int3 blockCoord)
{
    uint3 v = asuint(blockCoord);
    v = 1664525u * v + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return v;
}

uint2 MerkabaHashBucketPair(int3 blockCoord)
{
    uint3 hash = MerkabaPcg3d(blockCoord);
    uint2 pair = hash.xy & MERKABA_M8_HASH_BUCKET_MASK;
    if (pair.x == pair.y) pair.y ^= 1u;
    return pair;
}

uint2 MerkabaHashBucketSearchOrder(int3 blockCoord)
{
    uint3 hash = MerkabaPcg3d(blockCoord);
    uint2 pair = hash.xy & MERKABA_M8_HASH_BUCKET_MASK;
    if (pair.x == pair.y) pair.y ^= 1u;
    return (hash.z & 1u) == 0u ? pair : pair.yx;
}

int3 MerkabaM8OctantOffset(uint digit, int childSpan)
{
    return int3((digit & 1u) != 0u ? childSpan : 0,
        (digit & 2u) != 0u ? childSpan : 0,
        (digit & 4u) != 0u ? childSpan : 0);
}

bool MerkabaM8AabbIntersectsDistance(int3 globalMin, int span,
    float distance, float3 cameraWorld, float4x4 gridToWorld,
    float4x4 worldToGrid, float latticeStep, float halfSupport)
{
    float3 localMin = (float3)globalMin * latticeStep - halfSupport;
    float3 localMax = (float3)(globalMin + span - 1) * latticeStep +
        halfSupport;
    float3 cameraLocal = mul(worldToGrid, float4(cameraWorld, 1.0)).xyz;
    float3 nearestLocal = clamp(cameraLocal, localMin, localMax);
    float3 nearestWorld = mul(gridToWorld, float4(nearestLocal, 1.0)).xyz;
    float3 delta = nearestWorld - cameraWorld;
    return dot(delta, delta) <= distance * distance;
}

uint MerkabaM8DistanceChildMask(int3 parentMin, int parentSpan,
    float distance, float3 cameraWorld, float4x4 gridToWorld,
    float4x4 worldToGrid, float latticeStep, float halfSupport)
{
    int childSpan = parentSpan / 2;
    uint mask = 0u;
    [unroll]
    for (uint child = 0u; child < 8u; child++)
    {
        if (MerkabaM8AabbIntersectsDistance(parentMin +
            MerkabaM8OctantOffset(child, childSpan), childSpan, distance,
            cameraWorld, gridToWorld, worldToGrid, latticeStep, halfSupport))
            mask |= 1u << child;
    }
    return mask;
}

uint MerkabaM8PlaneChildMask(int3 parentMin, int parentSpan, float4 plane,
    float4x4 gridToWorld, float latticeStep, float halfSupport)
{
    int childSpan = parentSpan / 2;
    float3 parentCenterLocal = ((float3)parentMin +
        (parentSpan - 1) * 0.5) * latticeStep;
    float childOffset = parentSpan * 0.25 * latticeStep;
    float childExtent = (childSpan * latticeStep + halfSupport) * 0.5;
    float3 centerWorld = mul(gridToWorld,
        float4(parentCenterLocal, 1.0)).xyz;
    float3 offsetX = mul((float3x3)gridToWorld,
        float3(childOffset, 0, 0));
    float3 offsetY = mul((float3x3)gridToWorld,
        float3(0, childOffset, 0));
    float3 offsetZ = mul((float3x3)gridToWorld,
        float3(0, 0, childOffset));
    float3 axisX = mul((float3x3)gridToWorld,
        float3(childExtent, 0, 0));
    float3 axisY = mul((float3x3)gridToWorld,
        float3(0, childExtent, 0));
    float3 axisZ = mul((float3x3)gridToWorld,
        float3(0, 0, childExtent));
    float base = dot(plane.xyz, centerWorld) + plane.w;
    float sx = dot(plane.xyz, offsetX);
    float sy = dot(plane.xyz, offsetY);
    float sz = dot(plane.xyz, offsetZ);
    float radius = abs(dot(plane.xyz, axisX)) +
        abs(dot(plane.xyz, axisY)) + abs(dot(plane.xyz, axisZ));
    uint mask = 0u;
    [unroll]
    for (uint child = 0u; child < 8u; child++)
    {
        float score = base + ((child & 1u) != 0u ? sx : -sx) +
            ((child & 2u) != 0u ? sy : -sy) +
            ((child & 4u) != 0u ? sz : -sz);
        if (score + radius >= 0.0) mask |= 1u << child;
    }
    return mask;
}

#endif
