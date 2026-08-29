#include "MerkabaSpatial.hlsl"

StructuredBuffer<int4> InputBlockCoords : register(t0);
RWStructuredBuffer<uint4> OutputHashes : register(u0);

[numthreads(64, 1, 1)]
void main(uint id : SV_DispatchThreadID)
{
    uint3 hash = MerkabaPcg3d(InputBlockCoords[id].xyz);
    uint2 buckets = hash.xy & MERKABA_M8_HASH_BUCKET_MASK;
    if (buckets.x == buckets.y) buckets.y ^= 1u;
    OutputHashes[id] = uint4(hash, buckets.x ^ buckets.y);
}
