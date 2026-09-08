#ifndef GENESIS_MERKABA_DUAL_HIERARCHY_INCLUDED
#define GENESIS_MERKABA_DUAL_HIERARCHY_INCLUDED

#include "MerkabaWorld.hlsl"
#include "MerkabaSphereFlowerDataAbi.generated.hlsl"

// See MerkabaDualGpuLayout.cs. No second coordinate index, no dense leaf
// table per logical chunk, and no resident leaf implies free space.
RWByteAddressBuffer _M8DualBlockState;
RWByteAddressBuffer _M8DualChunkState;
RWByteAddressBuffer _M8DualLeaves;
ByteAddressBuffer _M8DualBlockStateRead;
ByteAddressBuffer _M8DualChunkStateRead;
ByteAddressBuffer _M8DualLeavesRead;

#define M8_DUAL_FULL 0u
#define M8_DUAL_THROUGH 1u
#define M8_DUAL_MIXED 2u
#define M8_DUAL_AMBIGUOUS 3u
// A failed prepared-leaf read has three different meanings and they must not
// share one scheduler state. COLD is a residency DEPENDENCY: enqueue the load
// and retry once residency moves. STALE is a HOT slot whose leaf ref no longer
// describes this tile - wrong generation, wrong meta, or no back reference - so
// the volume is recomputed and rebound, never pended. M8_DUAL_AMBIGUOUS keeps
// its structural meaning: the chunk payload itself is not canonical, which
// BeginChunk should already have prevented, so it is a transaction failure
// rather than something to wait for. Collapsing STALE into COLD requested no
// load and pended forever, which is a real liveness hole.
#define M8_DUAL_READ_COLD 4u
#define M8_DUAL_READ_STALE 5u
#define M8_DUAL_WRITE_UNCHANGED 0u
#define M8_DUAL_WRITE_COMMITTED 1u
#define M8_DUAL_WRITE_RETRY 2u
#define M8_DUAL_WRITE_COLD 3u
#define M8_DUAL_WRITE_INVALID 4u
#define M8_DUAL_NO_REF 0xffffffffu
#define M8_DUAL_BLOCK_MASKS_OFFSET 65536u
#define M8_DUAL_LEAF_REFS_OFFSET 2097152u
#define M8_DUAL_REF_ALLOCATION_OFFSET 2228224u
#define M8_DUAL_REF_CAPACITY 32768u
#define M8_DUAL_REF_ALLOCATION_WORDS 1024u
#define M8_DUAL_COLD_REF 0x40000000u
#define M8_DUAL_HOT_REF 0x80000000u
#define M8_DUAL_SLOT_MASK 0x7fffu
#define M8_DUAL_MAX_GENERATION 0x3fffffffu
#define M8_DUAL_STORAGE_PACKET_BYTES 256u
#define M8_DUAL_STORAGE_PACKET_RECORDS 16u
#define M8_DUAL_DIRTY_BITS_OFFSET 1114112u
#define M8_DUAL_DIRTY_SUMMARY_OFFSET 1147904u
#define M8_DUAL_DIRTY_TOP_OFFSET 1148960u
#define M8_DUAL_DIRTY_ROOT_OFFSET 1148996u
#define M8_DUAL_DIRTY_NODE_COUNT 270336u
#define M8_DUAL_DIRTY_WORD_COUNT 8448u
#define M8_DUAL_DIRTY_SUMMARY_WORD_COUNT 264u
#define M8_DUAL_DIRTY_TOP_WORD_COUNT 9u

// Storage-only dirty publication over existing physical node indices. The
// summaries locate dirty records without scanning the world or adding a
// coordinate index. Generation-checked storage acknowledgement owns clears.
void M8DualMarkNodeDirty(uint node)
{
    uint word=node >> 5u, summary=word >> 5u, top=summary >> 5u, ignored;
    _M8DualBlockState.InterlockedOr(M8_DUAL_DIRTY_BITS_OFFSET+word*4u,
        1u << (node & 31u),ignored);
    _M8DualBlockState.InterlockedOr(M8_DUAL_DIRTY_SUMMARY_OFFSET+summary*4u,
        1u << (word & 31u),ignored);
    _M8DualBlockState.InterlockedOr(M8_DUAL_DIRTY_TOP_OFFSET+top*4u,
        1u << (summary & 31u),ignored);
    _M8DualBlockState.InterlockedOr(M8_DUAL_DIRTY_ROOT_OFFSET,
        1u << top,ignored);
}

uint M8DualLowBits(uint count)
{
    return count >= 32u ? 0xffffffffu : (1u << count)-1u;
}

uint M8DualRank64(uint2 mask, uint bit)
{
    return bit < 32u ? countbits(mask.x & M8DualLowBits(bit)) :
        countbits(mask.x)+countbits(mask.y & M8DualLowBits(bit-32u));
}

uint M8DualCount64(uint2 mask)
{
    return countbits(mask.x)+countbits(mask.y);
}

uint M8DualReferenceSpan(uint count)
{
    return count;
}

uint M8DualBlockMaskAddress(uint block, uint word)
{
    return M8_DUAL_BLOCK_MASKS_OFFSET+block*128u+word*4u;
}

// These view selectors are compile-time constants at every entry point.
// Storage mutation reads its RW view; surface commit binds the same backing
// allocation as SRV. No kernel needs both views of a dual allocation.
M8DualChunkPayload M8DualLoadChunkPayload(uint chunk, bool readOnly = false)
{
    uint4 a = readOnly ? _M8DualChunkStateRead.Load4(chunk*32u) :
        _M8DualChunkState.Load4(chunk*32u);
    uint4 b = readOnly ? _M8DualChunkStateRead.Load4(chunk*32u+16u) :
        _M8DualChunkState.Load4(chunk*32u+16u);
    M8DualChunkPayload value;
    value.NonFullMaskLo=a.x; value.NonFullMaskHi=a.y;
    value.MixedMaskLo=a.z; value.MixedMaskHi=a.w;
    value.LeafRefBase=b.x; value.Generation=b.y;
    value.Reserved0=b.z; value.Reserved1=b.w;
    return value;
}

void M8DualStoreChunkPayload(uint chunk, M8DualChunkPayload value)
{
    _M8DualChunkState.Store4(chunk*32u,uint4(value.NonFullMaskLo,
        value.NonFullMaskHi,value.MixedMaskLo,value.MixedMaskHi));
    _M8DualChunkState.Store4(chunk*32u+16u,uint4(value.LeafRefBase,
        value.Generation,0u,0u));
}

M8DualChunkPayload M8DualUniformChunk(uint state, uint generation)
{
    M8DualChunkPayload value = (M8DualChunkPayload)0;
    value.NonFullMaskLo = state == M8_DUAL_THROUGH ? 0xffffffffu : 0u;
    value.NonFullMaskHi = value.NonFullMaskLo;
    value.LeafRefBase = M8_DUAL_NO_REF;
    value.Generation = generation;
    return value;
}

bool M8DualChunkCanonical(M8DualChunkPayload value)
{
    uint2 mixed = uint2(value.MixedMaskLo,value.MixedMaskHi);
    uint count = M8DualCount64(mixed);
    if (value.Generation == 0u || value.Generation > M8_DUAL_MAX_GENERATION ||
        value.Reserved0 != 0u || value.Reserved1 != 0u ||
        any((mixed & ~uint2(value.NonFullMaskLo,value.NonFullMaskHi)) != 0u))
        return false;
    if (count == 0u) return value.LeafRefBase == M8_DUAL_NO_REF;
    uint span = M8DualReferenceSpan(count);
    return value.LeafRefBase < M8_DUAL_REF_CAPACITY &&
        span <= M8_DUAL_REF_CAPACITY-value.LeafRefBase;
}

uint M8DualChunkTileState(M8DualChunkPayload value, uint tile)
{
    uint word = tile >> 5u, bit = 1u << (tile & 31u);
    uint nonFull = word == 0u ? value.NonFullMaskLo : value.NonFullMaskHi;
    uint mixed = word == 0u ? value.MixedMaskLo : value.MixedMaskHi;
    return (nonFull & bit) == 0u ? M8_DUAL_FULL :
        (mixed & bit) == 0u ? M8_DUAL_THROUGH : M8_DUAL_MIXED;
}

uint M8DualChunkCollapseState(M8DualChunkPayload value)
{
    if ((value.MixedMaskLo | value.MixedMaskHi) != 0u) return M8_DUAL_MIXED;
    if ((value.NonFullMaskLo | value.NonFullMaskHi) == 0u) return M8_DUAL_FULL;
    return (value.NonFullMaskLo & value.NonFullMaskHi) == 0xffffffffu ?
        M8_DUAL_THROUGH : M8_DUAL_MIXED;
}

uint M8DualReadBlock(uint block, out M8DualBlockMeta meta, bool readOnly = false)
{
    meta = (M8DualBlockMeta)0;
    meta.PayloadIndex = M8_DUAL_NO_REF;
    if (block >= MERKABA_M8_BLOCK_CAPACITY) return M8_DUAL_AMBIGUOUS;
    uint2 words = readOnly ? _M8DualBlockStateRead.Load2(block*8u) :
        _M8DualBlockState.Load2(block*8u);
    meta.StateAndGeneration = words.x;
    meta.PayloadIndex = words.y;
    // Zero initialized/unmaterialized dual state is the conservative default.
    if (words.x == 0u) return M8_DUAL_FULL;
    uint state = words.x & 3u;
    if ((words.x >> 2u) == 0u || state == M8_DUAL_AMBIGUOUS ||
        (state == M8_DUAL_MIXED ? words.y != block : words.y != M8_DUAL_NO_REF))
        return M8_DUAL_AMBIGUOUS;
    return state;
}

bool M8DualResolveChunk(uint block, uint child, out uint chunk)
{
    chunk = M8_DUAL_NO_REF;
    if (block >= MERKABA_M8_BLOCK_CAPACITY || child >= 512u) return false;
    uint reference = _M8BlockChunkRefsRead[block*512u+child];
    if (reference == 0u || reference-1u >= MERKABA_M8_CHUNK_CAPACITY) return false;
    chunk = reference-1u;
    return all(M8LoadChunkOwnerRead(chunk) == uint2(block,child));
}

uint M8DualPreparedChunkState(uint block, uint child, bool readOnly = false)
{
    uint address = M8DualBlockMaskAddress(block,child >> 4u);
    uint word = readOnly ? _M8DualBlockStateRead.Load(address) :
        _M8DualBlockState.Load(address);
    return (word >> ((child & 15u)*2u)) & 3u;
}

uint M8DualReadChunk(uint block, uint child, out uint chunk,
    out M8DualChunkPayload payload, bool readOnly = false)
{
    chunk = M8_DUAL_NO_REF;
    payload = (M8DualChunkPayload)0;
    M8DualBlockMeta meta;
    uint state = M8DualReadBlock(block,meta,readOnly);
    if (state != M8_DUAL_MIXED) return state;
    if (child >= 512u) return M8_DUAL_AMBIGUOUS;
    state = M8DualPreparedChunkState(block,child,readOnly);
    if (state != M8_DUAL_MIXED) return state;
    if (!M8DualResolveChunk(block,child,chunk)) return M8_DUAL_AMBIGUOUS;
    payload = M8DualLoadChunkPayload(chunk,readOnly);
    return M8DualChunkCanonical(payload) ? M8_DUAL_MIXED : M8_DUAL_AMBIGUOUS;
}

bool M8DualLeafResident(uint packed, uint chunk, uint tile, out uint slot,
    bool tileMetadataWritable = false)
{
    slot = packed & M8_DUAL_SLOT_MASK;
    if ((packed >> 30u) != 2u || chunk >= MERKABA_M8_CHUNK_CAPACITY || tile >= 64u)
        return false;
    uint generation = (packed >> 15u) & M8_DUAL_SLOT_MASK;
    uint4 meta = tileMetadataWritable ? _M8TileRecords[M8TileMetaIndex(slot)] :
        M8LoadTileMetaRead(slot);
    uint slotGeneration = tileMetadataWritable ?
        _M8TileRecords[M8TileRuntimeIndex(slot)].w : M8LoadTileRuntimeRead(slot).w;
    return generation != 0u && slotGeneration == generation &&
        meta.x == chunk && meta.y == tile &&
        _M8ChunkTileRefsRead[chunk*64u+tile] == slot+1u;
}

uint M8DualLeafReference(M8DualChunkPayload payload, uint tile, bool readOnly = false)
{
    uint ordinal = M8DualRank64(uint2(payload.MixedMaskLo,payload.MixedMaskHi),tile);
    uint address = M8_DUAL_LEAF_REFS_OFFSET+(payload.LeafRefBase+ordinal)*4u;
    return readOnly ? _M8DualLeavesRead.Load(address) : _M8DualLeaves.Load(address);
}

uint M8DualReadTile(uint block, uint child, uint tile, out uint leafSlot,
    bool readOnly = false, bool tileMetadataWritable = false)
{
    leafSlot = M8_DUAL_NO_REF;
    uint chunk;
    M8DualChunkPayload payload;
    uint state = M8DualReadChunk(block,child,chunk,payload,readOnly);
    if (state != M8_DUAL_MIXED) return state;
    if (tile >= 64u) return M8_DUAL_AMBIGUOUS;
    state = M8DualChunkTileState(payload,tile);
    if (state != M8_DUAL_MIXED) return state;
    return M8DualLeafResident(M8DualLeafReference(payload,tile,readOnly),
        chunk,tile,leafSlot,tileMetadataWritable)
        ? M8_DUAL_MIXED : M8_DUAL_AMBIGUOUS;
}

uint M8DualReadKernelAt(uint block, uint child, uint tile, uint kernel,
    bool readOnly = false, bool tileMetadataWritable = false)
{
    if (kernel >= 512u) return M8_DUAL_AMBIGUOUS;
    uint slot;
    uint state = M8DualReadTile(block,child,tile,slot,readOnly,tileMetadataWritable);
    if (state != M8_DUAL_MIXED) return state;
    uint address = slot*64u+(kernel >> 5u)*4u;
    uint word = readOnly ? _M8DualLeavesRead.Load(address) : _M8DualLeaves.Load(address);
    return (word & (1u << (kernel & 31u))) != 0u ? M8_DUAL_THROUGH : M8_DUAL_FULL;
}

uint M8DualReadKernel(int3 kernel)
{
    MerkabaM8Address address=MerkabaAddressOf(kernel);
    uint2 buckets=MerkabaHashBucketSearchOrder(address.blockCoord);
    [unroll] for (uint order=0u; order<2u; ++order)
    {
        uint bucket=order == 0u ? buckets.x : buckets.y;
        [unroll] for (uint slot=0u; slot<MERKABA_M8_HASH_SLOTS_PER_BUCKET; ++slot)
        {
            M8HashEntry entry=_M8HashEntriesRead[M8HashEntryIndex(bucket,slot)];
            if (entry.blockRef == MERKABA_REF_EMPTY ||
                !all(entry.blockCoord == address.blockCoord)) continue;
            if (entry.blockRef-1u >= MERKABA_M8_BLOCK_CAPACITY)
                return M8_DUAL_AMBIGUOUS;
            return M8DualReadKernelAt(entry.blockRef-1u,address.chunkLocal,
                address.tileLocal,address.kernelLocal);
        }
    }
    return M8_DUAL_FULL;
}

// Claim exactly count references, touching at most three allocator words.
// One CAS per word: a lost race rolls back only our earlier claimed bits,
// never waits/spins and never releases another writer's allocation.
bool M8DualClaimReferences(uint first, uint count)
{
    if (count == 0u || count > 64u || first >= M8_DUAL_REF_CAPACITY ||
        count > M8_DUAL_REF_CAPACITY-first) return false;
    uint last=first+count, firstWord=first >> 5u, lastWord=(last-1u) >> 5u;
    uint claimed=0u;
    [loop] for (uint word=firstWord; word<=lastWord; ++word)
    {
        uint lo=max(first,word*32u)-word*32u, hi=min(last,word*32u+32u)-word*32u;
        uint bits=M8DualLowBits(hi) & ~M8DualLowBits(lo);
        uint address=M8_DUAL_REF_ALLOCATION_OFFSET+word*4u;
        uint occupied=_M8DualLeaves.Load(address), previous;
        if ((occupied & bits) != 0u) break;
        _M8DualLeaves.InterlockedCompareExchange(address,occupied,occupied | bits,previous);
        if (previous != occupied) break;
        ++claimed;
    }
    if (claimed == lastWord-firstWord+1u) return true;
    [loop] for (uint rollback=0u; rollback<claimed; ++rollback)
    {
        uint word=firstWord+rollback, ignored;
        uint lo=max(first,word*32u)-word*32u, hi=min(last,word*32u+32u)-word*32u;
        uint bits=M8DualLowBits(hi) & ~M8DualLowBits(lo);
        _M8DualLeaves.InterlockedAnd(M8_DUAL_REF_ALLOCATION_OFFSET+word*4u,~bits,ignored);
    }
    return false;
}

// Bounded first-fit over the existing 32768-reference resident pool. Metadata
// allocation is not a geometry traversal. Failure leaves canonical state intact.
bool M8DualAllocateReferences(uint count, uint hint, out uint first)
{
    first=M8_DUAL_NO_REF;
    if (count == 0u) return true;
    if (count > 64u) return false;
    [loop] for (uint probe=0u; probe<M8_DUAL_REF_ALLOCATION_WORDS; ++probe)
    {
        uint word=(hint+probe) & (M8_DUAL_REF_ALLOCATION_WORDS-1u);
        uint freeBits=~_M8DualLeaves.Load(M8_DUAL_REF_ALLOCATION_OFFSET+word*4u);
        [loop] while (freeBits != 0u)
        {
            uint offset=(uint)firstbitlow(freeBits);
            freeBits &= freeBits-1u;
            uint candidate=word*32u+offset;
            if (M8DualClaimReferences(candidate,count)) { first=candidate; return true; }
        }
    }
    return false;
}

void M8DualReleaseReferences(uint first, uint span)
{
    if (span == 0u) return;
    // A different chunk writer may claim these bits immediately. Complete
    // this writer's earlier ref copies before making their source reusable.
    DeviceMemoryBarrier();
    uint last = first+span;
    [loop] for (uint word = first >> 5u; word <= ((last-1u) >> 5u); ++word)
    {
        uint lo=max(first,word*32u)-word*32u;
        uint hi=min(last,word*32u+32u)-word*32u;
        uint bits=M8DualLowBits(hi) & ~M8DualLowBits(lo);
        uint ignored;
        _M8DualLeaves.InterlockedAnd(M8_DUAL_REF_ALLOCATION_OFFSET+word*4u,
            ~bits,ignored);
    }
}

bool M8DualGenerationWritable(uint oldGeneration, uint publishing, uint retired)
{
    return publishing != 0u && publishing <= M8_DUAL_MAX_GENERATION &&
        oldGeneration <= publishing &&
        (oldGeneration <= retired || oldGeneration == publishing);
}

// MUTATION LEASE: every previously published dual reader must have retired
// on the existing native queue before this call. No graphics reader may retain
// these raw buffers across the lease. Current-generation writes are not made
// visible to readers until the caller's final queue publication barrier.
// One workgroup owns a block; distinct lanes may own its distinct chunks.
uint M8DualBeginBlock(uint block, uint publishing, uint retired)
{
    M8DualBlockMeta meta;
    uint state = M8DualReadBlock(block,meta);
    if (state == M8_DUAL_AMBIGUOUS) return M8_DUAL_WRITE_COLD;
    if (!M8DualGenerationWritable(meta.StateAndGeneration >> 2u,publishing,retired))
        return M8_DUAL_WRITE_RETRY;
    if (state != M8_DUAL_MIXED)
    {
        uint fill = state == M8_DUAL_THROUGH ? 0x55555555u : 0u;
        [unroll] for (uint word=0u; word<32u; ++word)
            _M8DualBlockState.Store(M8DualBlockMaskAddress(block,word),fill);
    }
    return M8_DUAL_WRITE_COMMITTED;
}

uint M8DualBeginChunk(uint block, uint child, uint publishing, uint retired,
    out uint chunk, out M8DualChunkPayload payload)
{
    chunk=M8_DUAL_NO_REF;
    payload=(M8DualChunkPayload)0;
    if (block >= MERKABA_M8_BLOCK_CAPACITY || child >= 512u)
        return M8_DUAL_WRITE_INVALID;
    uint state=M8DualPreparedChunkState(block,child);
    if (state == M8_DUAL_AMBIGUOUS || !M8DualResolveChunk(block,child,chunk))
        return M8_DUAL_WRITE_COLD;
    if (state == M8_DUAL_MIXED)
    {
        payload=M8DualLoadChunkPayload(chunk);
        if (!M8DualChunkCanonical(payload)) return M8_DUAL_WRITE_COLD;
        if (!M8DualGenerationWritable(payload.Generation,publishing,retired))
            return M8_DUAL_WRITE_RETRY;
    }
    else payload=M8DualUniformChunk(state,publishing);
    return M8_DUAL_WRITE_COMMITTED;
}

void M8DualPublishChunkState(uint block, uint child, uint state)
{
    uint address=M8DualBlockMaskAddress(block,child >> 4u);
    uint shift=(child & 15u)*2u, ignored;
    _M8DualBlockState.InterlockedAnd(address,~(3u << shift),ignored);
    _M8DualBlockState.InterlockedOr(address,state << shift,ignored);
}

uint M8DualReadPreparedTileWords(uint chunk, M8DualChunkPayload payload,
    uint tile, out uint words[16])
{
    uint state=chunk < MERKABA_M8_CHUNK_CAPACITY && tile < 64u &&
        M8DualChunkCanonical(payload) ? M8DualChunkTileState(payload,tile) :
        M8_DUAL_AMBIGUOUS;
    uint slot=M8_DUAL_NO_REF;
    if (state == M8_DUAL_MIXED)
    {
        uint packed=M8DualLeafReference(payload,tile);
        if (!M8DualLeafResident(packed,chunk,tile,slot,true))
            state=(packed & M8_DUAL_COLD_REF) != 0u &&
                (packed & M8_DUAL_HOT_REF) == 0u ?
                M8_DUAL_READ_COLD : M8_DUAL_READ_STALE;
    }
    [unroll] for (uint word=0u; word<16u; ++word)
        words[word]=state == M8_DUAL_MIXED ? _M8DualLeaves.Load(slot*64u+word*4u) :
            state == M8_DUAL_THROUGH ? 0xffffffffu : 0u;
    return state;
}

// The caller has already proved its support predicate. This operation only
// publishes the resulting 512-bit volume, compact refs and exact collapses.
// On COLD/allocation failure neither the payload nor the summary is changed.
uint M8DualWriteTileWords(uint block, uint child, uint chunk, uint tile,
    uint hotSlot, uint publishing, uint retired,
    inout M8DualChunkPayload payload, uint words[16])
{
    if (block >= MERKABA_M8_BLOCK_CAPACITY || child >= 512u ||
        chunk >= MERKABA_M8_CHUNK_CAPACITY || tile >= 64u ||
        !all(M8LoadChunkOwnerRead(chunk) == uint2(block,child)))
        return M8_DUAL_WRITE_INVALID;
    if (!M8DualChunkCanonical(payload) ||
        !M8DualGenerationWritable(payload.Generation,publishing,retired))
        return M8_DUAL_WRITE_RETRY;
    uint oldState=M8DualChunkTileState(payload,tile), oldSlot=M8_DUAL_NO_REF;
    if (oldState == M8_DUAL_MIXED &&
        !M8DualLeafResident(M8DualLeafReference(payload,tile),chunk,tile,oldSlot,true))
        return M8_DUAL_WRITE_COLD;
    uint allZero=0u, allOne=0xffffffffu;
    bool equal=true;
    [unroll] for (uint word=0u; word<16u; ++word)
    {
        allZero |= words[word]; allOne &= words[word];
        uint previous=oldState == M8_DUAL_MIXED ?
            _M8DualLeaves.Load(oldSlot*64u+word*4u) :
            oldState == M8_DUAL_THROUGH ? 0xffffffffu : 0u;
        equal=equal && previous == words[word];
    }
    if (equal) return M8_DUAL_WRITE_UNCHANGED;
    uint state=allZero == 0u ? M8_DUAL_FULL :
        allOne == 0xffffffffu ? M8_DUAL_THROUGH : M8_DUAL_MIXED;
    uint packedLeaf=M8_DUAL_NO_REF;
    if (state == M8_DUAL_MIXED)
    {
        if (hotSlot >= MERKABA_M8_PHYSICAL_TILE_CAPACITY) return M8_DUAL_WRITE_COLD;
        uint generation=_M8TileRecords[M8TileRuntimeIndex(hotSlot)].w;
        packedLeaf=M8_DUAL_HOT_REF | (generation << 15u) | hotSlot;
        uint resolved;
        if (generation == 0u || generation > M8_DUAL_SLOT_MASK ||
            !M8DualLeafResident(packedLeaf,chunk,tile,resolved,true)) return M8_DUAL_WRITE_COLD;
    }
    uint2 oldMixed=uint2(payload.MixedMaskLo,payload.MixedMaskHi);
    uint oldCount=M8DualCount64(oldMixed);
    uint oldSpan=M8DualReferenceSpan(oldCount);
    uint2 newMixed=oldMixed;
    uint2 nonFull=uint2(payload.NonFullMaskLo,payload.NonFullMaskHi);
    uint bit=1u << (tile & 31u), component=tile >> 5u;
    newMixed[component]=(newMixed[component] & ~bit) |
        (state == M8_DUAL_MIXED ? bit : 0u);
    nonFull[component]=(nonFull[component] & ~bit) |
        (state != M8_DUAL_FULL ? bit : 0u);
    uint newCount=M8DualCount64(newMixed), newSpan=M8DualReferenceSpan(newCount);
    uint first=payload.LeafRefBase;
    bool allocate=newSpan > oldSpan;
    if (allocate && oldSpan != 0u && M8DualClaimReferences(first+oldSpan,newSpan-oldSpan))
        allocate=false;
    if (allocate && !M8DualAllocateReferences(newCount,chunk,first))
        return M8_DUAL_WRITE_RETRY;
    uint rank=M8DualRank64(oldMixed,tile);
    if (state == M8_DUAL_MIXED)
        [unroll] for (uint word=0u; word<16u; ++word)
            _M8DualLeaves.Store(hotSlot*64u+word*4u,words[word]);
    if (allocate)
    {
        [loop] for (uint index=0u; index<newCount; ++index)
        {
            uint value;
            if (index == rank) value=packedLeaf;
            else
            {
                uint oldIndex=index < rank ? index : index-1u;
                value=_M8DualLeaves.Load(M8_DUAL_LEAF_REFS_OFFSET+
                    (payload.LeafRefBase+oldIndex)*4u);
            }
            _M8DualLeaves.Store(M8_DUAL_LEAF_REFS_OFFSET+(first+index)*4u,value);
        }
    }
    else if (newCount > oldCount)
    {
        [loop] for (uint index=oldCount; index>rank; --index)
            _M8DualLeaves.Store(M8_DUAL_LEAF_REFS_OFFSET+(first+index)*4u,
                _M8DualLeaves.Load(M8_DUAL_LEAF_REFS_OFFSET+(first+index-1u)*4u));
        _M8DualLeaves.Store(M8_DUAL_LEAF_REFS_OFFSET+(first+rank)*4u,packedLeaf);
    }
    else if (newCount < oldCount)
    {
        [loop] for (uint index=rank; index<newCount; ++index)
            _M8DualLeaves.Store(M8_DUAL_LEAF_REFS_OFFSET+(first+index)*4u,
                _M8DualLeaves.Load(M8_DUAL_LEAF_REFS_OFFSET+(first+index+1u)*4u));
    }
    else if (state == M8_DUAL_MIXED)
        _M8DualLeaves.Store(M8_DUAL_LEAF_REFS_OFFSET+(first+rank)*4u,packedLeaf);
    // Every old published reader is retired by the mutation lease. Releasing
    // an unpublished current-generation span needs no second retirement queue.
    if (allocate) M8DualReleaseReferences(payload.LeafRefBase,oldSpan);
    else if (newSpan < oldSpan)
        M8DualReleaseReferences(first+newSpan,oldSpan-newSpan);
    payload.NonFullMaskLo=nonFull.x; payload.NonFullMaskHi=nonFull.y;
    payload.MixedMaskLo=newMixed.x; payload.MixedMaskHi=newMixed.y;
    payload.LeafRefBase=newCount == 0u ? M8_DUAL_NO_REF : first;
    payload.Generation=publishing;
    M8DualStoreChunkPayload(chunk,payload);
    M8DualMarkNodeDirty(MERKABA_M8_BLOCK_CAPACITY+chunk);
    M8DualPublishChunkState(block,child,M8DualChunkCollapseState(payload));
    return M8_DUAL_WRITE_COMMITTED;
}

uint M8DualWriteKernelBit(uint block, uint child, uint chunk, uint tile,
    uint hotSlot, uint kernel, bool through, uint publishing, uint retired,
    inout M8DualChunkPayload payload)
{
    if (kernel >= 512u) return M8_DUAL_WRITE_INVALID;
    uint words[16];
    uint prepared=M8DualReadPreparedTileWords(chunk,payload,tile,words);
    if (prepared == M8_DUAL_READ_COLD) return M8_DUAL_WRITE_COLD;
    if (prepared == M8_DUAL_AMBIGUOUS) return M8_DUAL_WRITE_INVALID;
    // A STALE ref carries zero words, so the bit is set on a fresh volume and
    // M8DualWriteTileWords rebinds the leaf. That is the repair, not a wait.
    uint word=kernel >> 5u, bit=1u << (kernel & 31u);
    words[word]=(words[word] & ~bit) | (through ? bit : 0u);
    return M8DualWriteTileWords(block,child,chunk,tile,hotSlot,publishing,retired,
        payload,words);
}

// Uniform replacement cannot silently discard unresolved COLD leaf data.
// The storage owner must first materialize every required payload. Enumeration
// is bounded by this one chunk's finite 64 tile children, never by world size.
bool M8DualCanDiscardChunk(uint chunk, M8DualChunkPayload payload)
{
    if (!M8DualChunkCanonical(payload)) return false;
    uint2 mixed=uint2(payload.MixedMaskLo,payload.MixedMaskHi);
    [unroll] for (uint word=0u; word<2u; ++word)
    {
        uint remaining=mixed[word];
        [loop] while (remaining != 0u)
        {
            uint bit=(uint)firstbitlow(remaining);
            remaining &= remaining-1u;
            uint tile=word*32u+bit, slot;
            if (!M8DualLeafResident(M8DualLeafReference(payload,tile),chunk,tile,slot,true))
                return false;
        }
    }
    return true;
}

// A whole certified chunk needs no M8 chunk allocation when its summary was
// already uniform. Only MIXED state requires the existing M8 chunk relation.
uint M8DualWriteChunkUniform(uint block, uint child, uint state,
    uint publishing, uint retired)
{
    if (block >= MERKABA_M8_BLOCK_CAPACITY || child >= 512u || state > M8_DUAL_THROUGH ||
        !M8DualGenerationWritable(0u,publishing,retired)) return M8_DUAL_WRITE_INVALID;
    uint previous=M8DualPreparedChunkState(block,child);
    if (previous == state) return M8_DUAL_WRITE_UNCHANGED;
    if (previous == M8_DUAL_AMBIGUOUS) return M8_DUAL_WRITE_COLD;
    if (previous == M8_DUAL_MIXED)
    {
        uint chunk;
        if (!M8DualResolveChunk(block,child,chunk)) return M8_DUAL_WRITE_COLD;
        M8DualChunkPayload payload=M8DualLoadChunkPayload(chunk);
        if (!M8DualCanDiscardChunk(chunk,payload)) return M8_DUAL_WRITE_COLD;
        if (!M8DualGenerationWritable(payload.Generation,publishing,retired))
            return M8_DUAL_WRITE_RETRY;
        M8DualReleaseReferences(payload.LeafRefBase,M8DualReferenceSpan(
            M8DualCount64(uint2(payload.MixedMaskLo,payload.MixedMaskHi))));
        M8DualStoreChunkPayload(chunk,M8DualUniformChunk(state,publishing));
        M8DualMarkNodeDirty(MERKABA_M8_BLOCK_CAPACITY+chunk);
    }
    M8DualPublishChunkState(block,child,state);
    return M8_DUAL_WRITE_COMMITTED;
}

// Whole-block certificate fast path. The first pass validates every payload
// that would be discarded; the second pass cannot fail. This preserves the
// old block atomically on a COLD dependency and requires no new ref allocation.
uint M8DualWriteBlockUniform(uint block, uint state, uint publishing, uint retired)
{
    if (state > M8_DUAL_THROUGH) return M8_DUAL_WRITE_INVALID;
    M8DualBlockMeta meta;
    uint previous=M8DualReadBlock(block,meta);
    if (previous == M8_DUAL_AMBIGUOUS) return M8_DUAL_WRITE_COLD;
    if (!M8DualGenerationWritable(meta.StateAndGeneration >> 2u,publishing,retired))
        return M8_DUAL_WRITE_RETRY;
    if (previous == state) return M8_DUAL_WRITE_UNCHANGED;
    if (previous == M8_DUAL_MIXED)
    {
        [loop] for (uint child=0u; child<512u; ++child)
        {
            uint childState=M8DualPreparedChunkState(block,child);
            if (childState == M8_DUAL_AMBIGUOUS) return M8_DUAL_WRITE_COLD;
            if (childState != M8_DUAL_MIXED) continue;
            uint chunk;
            if (!M8DualResolveChunk(block,child,chunk)) return M8_DUAL_WRITE_COLD;
            M8DualChunkPayload payload=M8DualLoadChunkPayload(chunk);
            if (!M8DualCanDiscardChunk(chunk,payload)) return M8_DUAL_WRITE_COLD;
            if (!M8DualGenerationWritable(payload.Generation,publishing,retired))
                return M8_DUAL_WRITE_RETRY;
        }
        [loop] for (uint child=0u; child<512u; ++child)
        {
            if (M8DualPreparedChunkState(block,child) != M8_DUAL_MIXED) continue;
            uint chunk;
            M8DualResolveChunk(block,child,chunk);
            M8DualChunkPayload payload=M8DualLoadChunkPayload(chunk);
            M8DualReleaseReferences(payload.LeafRefBase,M8DualReferenceSpan(
                M8DualCount64(uint2(payload.MixedMaskLo,payload.MixedMaskHi))));
            M8DualStoreChunkPayload(chunk,M8DualUniformChunk(state,publishing));
            M8DualMarkNodeDirty(MERKABA_M8_BLOCK_CAPACITY+chunk);
        }
    }
    _M8DualBlockState.Store2(block*8u,uint2((publishing << 2u) | state,M8_DUAL_NO_REF));
    M8DualMarkNodeDirty(block);
    return M8_DUAL_WRITE_COMMITTED;
}

// Storage hydration runs under the same exclusive queue lease as mutations.
// A new block snapshot supersedes all older resident descendant summaries.
// Invalidate them before publishing MIXED; missing summaries then read COLD,
// never as stale THROUGH. SSD payload indices are not GPU physical indices.
uint M8DualInstallBlock(uint block, M8DualBlockMeta incoming, uint words[32],
    uint publishing, uint retired)
{
    if (block >= MERKABA_M8_BLOCK_CAPACITY) return M8_DUAL_WRITE_INVALID;
    uint state=incoming.StateAndGeneration & 3u;
    uint generation=incoming.StateAndGeneration >> 2u;
    if (state > M8_DUAL_MIXED || generation == 0u ||
        generation > M8_DUAL_MAX_GENERATION) return M8_DUAL_WRITE_INVALID;
    if (generation > publishing) return M8_DUAL_WRITE_RETRY;
    if (state == M8_DUAL_MIXED)
    {
        uint anyValue=0u, allThrough=0xffffffffu, invalid=0u;
        [unroll] for (uint word=0u; word<32u; ++word)
        {
            anyValue |= words[word]; allThrough &= words[word];
            invalid |= words[word] & (words[word] >> 1u) & 0x55555555u;
        }
        if (invalid != 0u || anyValue == 0u || allThrough == 0x55555555u)
            return M8_DUAL_WRITE_INVALID;
    }
    M8DualBlockMeta previousMeta;
    uint previous=M8DualReadBlock(block,previousMeta);
    if (previous == M8_DUAL_AMBIGUOUS) return M8_DUAL_WRITE_COLD;
    uint previousGeneration=previousMeta.StateAndGeneration >> 2u;
    if (generation < previousGeneration) return M8_DUAL_WRITE_UNCHANGED;
    if (generation == previousGeneration)
    {
        if (state != previous) return M8_DUAL_WRITE_INVALID;
        if (state == M8_DUAL_MIXED)
            [unroll] for (uint word=0u; word<32u; ++word)
                if (words[word] != _M8DualBlockState.Load(M8DualBlockMaskAddress(block,word)))
                    return M8_DUAL_WRITE_INVALID;
        return M8_DUAL_WRITE_UNCHANGED;
    }
    if (!M8DualGenerationWritable(previousGeneration,publishing,retired))
        return M8_DUAL_WRITE_RETRY;
    // Validate the entire replacement before changing any resident payload.
    [loop] for (uint child=0u; child<512u; ++child)
    {
        uint chunk;
        if (!M8DualResolveChunk(block,child,chunk)) continue;
        M8DualChunkPayload old=M8DualLoadChunkPayload(chunk);
        if (old.Generation == 0u) continue;
        if (!M8DualChunkCanonical(old)) return M8_DUAL_WRITE_COLD;
        if (old.Generation > generation ||
            !M8DualGenerationWritable(old.Generation,publishing,retired))
            return M8_DUAL_WRITE_RETRY;
    }
    [loop] for (uint child=0u; child<512u; ++child)
    {
        uint chunk;
        if (!M8DualResolveChunk(block,child,chunk)) continue;
        M8DualChunkPayload old=M8DualLoadChunkPayload(chunk);
        if (old.Generation != 0u)
            M8DualReleaseReferences(old.LeafRefBase,M8DualReferenceSpan(
                M8DualCount64(uint2(old.MixedMaskLo,old.MixedMaskHi))));
        M8DualStoreChunkPayload(chunk,(M8DualChunkPayload)0);
    }
    if (state == M8_DUAL_MIXED)
        [unroll] for (uint word=0u; word<32u; ++word)
            _M8DualBlockState.Store(M8DualBlockMaskAddress(block,word),words[word]);
    DeviceMemoryBarrier();
    _M8DualBlockState.Store2(block*8u,uint2(incoming.StateAndGeneration,
        state == M8_DUAL_MIXED ? block : M8_DUAL_NO_REF));
    return M8_DUAL_WRITE_COMMITTED;
}

// Rebuild only a still-current MIXED chunk's resident dense reference span.
// Every imported leaf begins COLD. Subsequent InstallLeaf calls publish HOT
// references after their M8 tile slots and actual leaf bytes are ready.
uint M8DualInstallChunk(uint block, uint child, uint expectedBlockGeneration,
    M8DualChunkPayload incoming, uint publishing, uint retired)
{
    M8DualBlockMeta blockMeta;
    uint state=M8DualReadBlock(block,blockMeta);
    if (state == M8_DUAL_AMBIGUOUS) return M8_DUAL_WRITE_COLD;
    if (state != M8_DUAL_MIXED) return M8_DUAL_WRITE_UNCHANGED;
    if ((blockMeta.StateAndGeneration >> 2u) != expectedBlockGeneration)
        return M8_DUAL_WRITE_UNCHANGED;
    if (child >= 512u) return M8_DUAL_WRITE_INVALID;
    if (M8DualPreparedChunkState(block,child) != M8_DUAL_MIXED)
        return M8_DUAL_WRITE_UNCHANGED;
    uint chunk;
    if (!M8DualResolveChunk(block,child,chunk)) return M8_DUAL_WRITE_COLD;
    uint2 incomingMixed=uint2(incoming.MixedMaskLo,incoming.MixedMaskHi);
    uint count=M8DualCount64(incomingMixed), span=M8DualReferenceSpan(count);
    if (incoming.Generation == 0u || incoming.Generation > expectedBlockGeneration ||
        incoming.Reserved0 != 0u || incoming.Reserved1 != 0u ||
        any((incomingMixed & ~uint2(incoming.NonFullMaskLo,incoming.NonFullMaskHi)) != 0u) ||
        M8DualChunkCollapseState(incoming) != M8_DUAL_MIXED ||
        (count == 0u ? incoming.LeafRefBase != M8_DUAL_NO_REF :
            incoming.LeafRefBase == M8_DUAL_NO_REF)) return M8_DUAL_WRITE_INVALID;
    if (incoming.Generation > publishing) return M8_DUAL_WRITE_RETRY;
    M8DualChunkPayload old=M8DualLoadChunkPayload(chunk);
    if (old.Generation != 0u && !M8DualChunkCanonical(old))
        return M8_DUAL_WRITE_COLD;
    if (old.Generation > incoming.Generation) return M8_DUAL_WRITE_UNCHANGED;
    if (old.Generation == incoming.Generation)
    {
        if (any(uint4(old.NonFullMaskLo,old.NonFullMaskHi,old.MixedMaskLo,old.MixedMaskHi) !=
            uint4(incoming.NonFullMaskLo,incoming.NonFullMaskHi,incoming.MixedMaskLo,incoming.MixedMaskHi)))
            return M8_DUAL_WRITE_INVALID;
        return M8_DUAL_WRITE_UNCHANGED;
    }
    if (!M8DualGenerationWritable(old.Generation,publishing,retired))
        return M8_DUAL_WRITE_RETRY;
    uint oldSpan=old.Generation == 0u ? 0u : M8DualReferenceSpan(
        M8DualCount64(uint2(old.MixedMaskLo,old.MixedMaskHi)));
    uint first=M8_DUAL_NO_REF;
    if (span != 0u)
    {
        bool reuse=span <= oldSpan;
        if (!reuse && oldSpan != 0u)
            reuse=M8DualClaimReferences(old.LeafRefBase+oldSpan,span-oldSpan);
        if (reuse) first=old.LeafRefBase;
        else if (!M8DualAllocateReferences(count,chunk,first)) return M8_DUAL_WRITE_RETRY;
        [loop] for (uint ordinal=0u; ordinal<count; ++ordinal)
            _M8DualLeaves.Store(M8_DUAL_LEAF_REFS_OFFSET+(first+ordinal)*4u,M8_DUAL_COLD_REF);
    }
    if (oldSpan != 0u)
    {
        if (first != old.LeafRefBase) M8DualReleaseReferences(old.LeafRefBase,oldSpan);
        else if (span < oldSpan) M8DualReleaseReferences(first+span,oldSpan-span);
    }
    incoming.LeafRefBase=first;
    DeviceMemoryBarrier();
    M8DualStoreChunkPayload(chunk,incoming);
    return M8_DUAL_WRITE_COMMITTED;
}

// Reclaim a wholly COLD reference span only when its actual summary is already
// durable. The parent remains MIXED, so a subsequent query is AMBIGUOUS until
// that exact SSD summary is rehydrated. No uniform value is substituted.
uint M8DualMakeChunkCold(uint block, uint child, uint publishing,
    uint retired, uint durable)
{
    uint chunk;
    M8DualChunkPayload payload;
    uint state=M8DualReadChunk(block,child,chunk,payload);
    if (state == M8_DUAL_AMBIGUOUS) return M8_DUAL_WRITE_UNCHANGED;
    if (state != M8_DUAL_MIXED) return M8_DUAL_WRITE_UNCHANGED;
    uint count=M8DualCount64(uint2(payload.MixedMaskLo,payload.MixedMaskHi));
    if (count == 0u) return M8_DUAL_WRITE_UNCHANGED;
    if (payload.Generation > durable ||
        !M8DualGenerationWritable(payload.Generation,publishing,retired))
        return M8_DUAL_WRITE_RETRY;
    // A HOT uniform M8 tile also reads this summary. Keep its exact dual
    // context resident; otherwise its normal HOT path has no SSD miss to
    // trigger rehydration before a later writeback or direct commit.
    [unroll] for (uint tile=0u;tile<64u;++tile)
    {
        uint tileRef=_M8ChunkTileRefsRead[chunk*64u+tile];
        if (M8IsHotRef(tileRef) || tileRef == MERKABA_REF_LOADING ||
            tileRef == MERKABA_REF_EVICTING) return M8_DUAL_WRITE_RETRY;
    }
    [loop] for (uint ordinal=0u;ordinal<count;++ordinal)
        if (_M8DualLeaves.Load(M8_DUAL_LEAF_REFS_OFFSET+
            (payload.LeafRefBase+ordinal)*4u) != M8_DUAL_COLD_REF)
            return M8_DUAL_WRITE_RETRY;
    M8DualReleaseReferences(payload.LeafRefBase,count);
    DeviceMemoryBarrier();
    M8DualStoreChunkPayload(chunk,(M8DualChunkPayload)0);
    return M8_DUAL_WRITE_COMMITTED;
}

// Storage-only residency transition. The caller has durably captured the old
// leaf and has retired every GPU reader before releasing its physical slot.
// COLD is a reference residency marker, never a fifth persistent node state.
uint M8DualMakeLeafCold(uint block, uint child, uint tile, uint publishing, uint retired,
    bool readOnlySummaries = false)
{
    uint chunk;
    M8DualChunkPayload payload;
    uint state=M8DualReadChunk(block,child,chunk,payload,readOnlySummaries);
    if (state == M8_DUAL_AMBIGUOUS) return M8_DUAL_WRITE_COLD;
    if (state != M8_DUAL_MIXED) return M8_DUAL_WRITE_UNCHANGED;
    if (tile >= 64u) return M8_DUAL_WRITE_INVALID;
    if (M8DualChunkTileState(payload,tile) != M8_DUAL_MIXED)
        return M8_DUAL_WRITE_UNCHANGED;
    if (!M8DualGenerationWritable(payload.Generation,publishing,retired))
        return M8_DUAL_WRITE_RETRY;
    uint ordinal=M8DualRank64(uint2(payload.MixedMaskLo,payload.MixedMaskHi),tile);
    _M8DualLeaves.Store(M8_DUAL_LEAF_REFS_OFFSET+
        (payload.LeafRefBase+ordinal)*4u,M8_DUAL_COLD_REF);
    return M8_DUAL_WRITE_COMMITTED;
}

// Storage-only validation while the serialized load command owns the slot.
// A prepared leaf is deliberately not resident to ordinary dual readers:
// M8DualLeafResident still requires the published physical-slot reference.
// This caller also writes tile metadata, so it must use that same RW view.
bool M8DualStorageLeafResident(uint packed, uint chunk, uint tile,
    bool prepared, out uint slot)
{
    slot = packed & M8_DUAL_SLOT_MASK;
    if ((packed >> 30u) != 2u || chunk >= MERKABA_M8_CHUNK_CAPACITY || tile >= 64u)
        return false;
    uint generation = (packed >> 15u) & M8_DUAL_SLOT_MASK;
    uint4 meta = _M8TileRecords[M8TileMetaIndex(slot)];
    uint expected = prepared ? MERKABA_REF_LOADING : slot+1u;
    return generation != 0u && _M8TileRecords[M8TileRuntimeIndex(slot)].w == generation &&
        meta.x == chunk && meta.y == tile &&
        _M8ChunkTileRefsRead[chunk*64u+tile] == expected;
}

// Install is allowed only into the still-current MIXED symbolic tile. A newer
// uniform ancestor supersedes the SSD response without resurrecting its leaf.
// PrepareLoadedTiles may stage the leaf behind a still-LOADING M8 reference;
// InstallLoadedTiles publishes that reference only after all M8 data is ready.
uint M8DualInstallLeaf(uint block, uint child, uint tile, uint hotSlot,
    uint expectedChunkGeneration, uint publishing, uint retired, uint words[16],
    bool prepared)
{
    uint chunk;
    M8DualChunkPayload payload;
    uint state=M8DualReadChunk(block,child,chunk,payload);
    if (state == M8_DUAL_AMBIGUOUS) return M8_DUAL_WRITE_COLD;
    if (state != M8_DUAL_MIXED) return M8_DUAL_WRITE_UNCHANGED;
    if (tile >= 64u || hotSlot >= MERKABA_M8_PHYSICAL_TILE_CAPACITY)
        return M8_DUAL_WRITE_INVALID;
    if (M8DualChunkTileState(payload,tile) != M8_DUAL_MIXED ||
        payload.Generation != expectedChunkGeneration) return M8_DUAL_WRITE_UNCHANGED;
    if (!M8DualGenerationWritable(payload.Generation,publishing,retired))
        return M8_DUAL_WRITE_RETRY;
    uint generation=_M8TileRecords[M8TileRuntimeIndex(hotSlot)].w;
    uint packed=M8_DUAL_HOT_REF | (generation << 15u) | hotSlot, validated;
    if (generation == 0u || generation > M8_DUAL_SLOT_MASK ||
        !M8DualStorageLeafResident(packed,chunk,tile,prepared,validated))
        return M8_DUAL_WRITE_COLD;
    uint nonzero=0u, allOne=0xffffffffu;
    [unroll] for (uint word=0u; word<16u; ++word)
    { nonzero |= words[word]; allOne &= words[word]; }
    if (nonzero == 0u || allOne == 0xffffffffu) return M8_DUAL_WRITE_INVALID;
    [unroll] for (uint word=0u; word<16u; ++word)
        _M8DualLeaves.Store(hotSlot*64u+word*4u,words[word]);
    DeviceMemoryBarrier();
    uint ordinal=M8DualRank64(uint2(payload.MixedMaskLo,payload.MixedMaskHi),tile);
    _M8DualLeaves.Store(M8_DUAL_LEAF_REFS_OFFSET+
        (payload.LeafRefBase+ordinal)*4u,packed);
    return M8_DUAL_WRITE_COMMITTED;
}

// Call once, from the block-owning workgroup, AFTER a device/group barrier
// joining every chunk writer. This is the only block publication point.
uint M8DualEndBlock(uint block, uint publishing)
{
    if (block >= MERKABA_M8_BLOCK_CAPACITY) return M8_DUAL_WRITE_INVALID;
    uint allZero=0u, allThrough=0xffffffffu, invalid=0u;
    [unroll] for (uint word=0u; word<32u; ++word)
    {
        uint value=_M8DualBlockState.Load(M8DualBlockMaskAddress(block,word));
        allZero |= value; allThrough &= value;
        invalid |= value & (value >> 1u) & 0x55555555u;
    }
    if (invalid != 0u || publishing == 0u || publishing > M8_DUAL_MAX_GENERATION)
        return M8_DUAL_WRITE_INVALID;
    uint state=allZero == 0u ? M8_DUAL_FULL :
        allThrough == 0x55555555u ? M8_DUAL_THROUGH : M8_DUAL_MIXED;
    _M8DualBlockState.Store2(block*8u,uint2((publishing << 2u) | state,
        state == M8_DUAL_MIXED ? block : M8_DUAL_NO_REF));
    M8DualMarkNodeDirty(block);
    return M8_DUAL_WRITE_COMMITTED;
}

#endif
