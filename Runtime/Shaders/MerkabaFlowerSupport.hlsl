#ifndef GENESIS_MERKABA_FLOWER_SUPPORT_INCLUDED
#define GENESIS_MERKABA_FLOWER_SUPPORT_INCLUDED

#include "MerkabaDualHierarchy.hlsl"
#include "MerkabaSphereFlower.generated.hlsl"
#if defined(M8_FLOWER_PAGE_WRITE)
#include "MerkabaFlowerPages.hlsl"
#endif

// A page compaction workgroup owns this scratch. The serialized queue must
// hold the dual read lease through the snapshot; no install/eviction/mutation
// may interleave it. Graphics consume the immutable symbols, not this cache.
#define M8_FLOWER_SUPPORT_HALO_COUNT 27u
#define M8_FLOWER_SUPPORT_FACE_WORDS 96u
#define M8_FLOWER_SUPPORT_HALF_WORDS 192u
#define M8_FLOWER_SUPPORT_CELL_COUNT 1000u
#define M8_FLOWER_SUPPORT_ARENA_CAPACITY 4194304u

// Context: resolved dual tile state, existing chunk index, tileLocal, packed
// physical leaf reference. Uniform ancestors need no positive M8 HOT owner.
groupshared uint4 m8FlowerSupportHalo[M8_FLOWER_SUPPORT_HALO_COUNT];
groupshared uint m8FlowerSupportWords[M8_FLOWER_SUPPORT_HALO_COUNT * 16u];
groupshared uint m8FlowerSupportCells[M8_FLOWER_SUPPORT_CELL_COUNT];
groupshared uint m8FlowerSupportFaces[M8_FLOWER_SUPPORT_FACE_WORDS];
groupshared uint m8FlowerSupportAmbiguousFaces[M8_FLOWER_SUPPORT_FACE_WORDS];
groupshared uint m8FlowerSupportHalves[M8_FLOWER_SUPPORT_HALF_WORDS];
groupshared uint m8FlowerSupportPartial[M8_FLOWER_SUPPORT_FACE_WORDS];
groupshared uint m8FlowerSupportOffsets[M8_FLOWER_SUPPORT_HALF_WORDS];
groupshared uint m8FlowerSupportColdHalo;
groupshared uint m8FlowerSupportSymbolCount;
groupshared uint m8FlowerSupportUnresolved;
groupshared int3 m8FlowerSupportOrigin;
groupshared uint m8FlowerSupportOriginValid;

uint4 M8FlowerSupportResolveTile(int3 logicalTile)
{
    uint4 context = uint4(M8_DUAL_AMBIGUOUS, M8_DUAL_NO_REF, 0u,
        M8_DUAL_NO_REF);
    // Multiplication by eight must not wrap signed lattice coordinates.
    if (any(logicalTile < -268435456) || any(logicalTile > 268435455))
        return context;
    MerkabaM8Address address = MerkabaAddressOf(logicalTile * 8);
    context.z = address.tileLocal;

    // The existing two-bucket M8 index is queried once per halo tile. A
    // matching CLAIMED entry is unresolved, unlike a genuinely absent block.
    uint block = M8_DUAL_NO_REF;
    uint2 buckets = MerkabaHashBucketSearchOrder(address.blockCoord);
    [unroll] for (uint order = 0u; order < 2u; ++order)
    {
        uint bucket = order == 0u ? buckets.x : buckets.y;
        [unroll] for (uint slot = 0u;
            slot < MERKABA_M8_HASH_SLOTS_PER_BUCKET; ++slot)
        {
            M8HashEntry entry = _M8HashEntriesRead[
                M8HashEntryIndex(bucket, slot)];
            if (entry.blockRef == MERKABA_REF_EMPTY ||
                any(entry.blockCoord != address.blockCoord)) continue;
            if (entry.blockRef - 1u >= MERKABA_M8_BLOCK_CAPACITY)
                return context;
            block = entry.blockRef - 1u;
        }
    }
    if (block == M8_DUAL_NO_REF)
    {
        context.x = M8_DUAL_FULL;
        return context;
    }
    if (any(M8LoadBlockCoordRead(block) != address.blockCoord)) return context;

    uint chunk;
    M8DualChunkPayload payload;
    context.x = M8DualReadChunk(block, address.chunkLocal, chunk, payload, true);
    context.y = chunk;
    if (context.x != M8_DUAL_MIXED) return context;
    context.x = M8DualChunkTileState(payload, address.tileLocal);
    if (context.x != M8_DUAL_MIXED) return context;
    context.w = M8DualLeafReference(payload, address.tileLocal, true);
    uint leafSlot;
    if (!M8DualLeafResident(context.w, chunk, address.tileLocal, leafSlot))
        context.x = M8_DUAL_AMBIGUOUS;
    return context;
}

// All workgroup lanes call this once for an existing logical page. The 27
// hierarchy lookups and all physical-generation checks finish before any
// cell loop. Cell evaluation subsequently reads groupshared words only.
void M8FlowerSupportCacheTile(int3 logicalTile, uint lane, uint laneCount)
{
    if (lane == 0u)
    {
        m8FlowerSupportColdHalo = 0u;
        m8FlowerSupportOriginValid = all(logicalTile >= -268435456) &&
            all(logicalTile <= 268435455) ? 1u : 0u;
        m8FlowerSupportOrigin = m8FlowerSupportOriginValid != 0u
            ? logicalTile * 8 : int3(0, 0, 0);
    }
    GroupMemoryBarrierWithGroupSync();
    [loop] for (uint halo = lane; halo < M8_FLOWER_SUPPORT_HALO_COUNT;
        halo += laneCount)
    {
        int3 delta = int3(halo % 3u, (halo / 3u) % 3u, halo / 9u) - 1;
        uint4 context = uint4(M8_DUAL_AMBIGUOUS, M8_DUAL_NO_REF, 0u,
            M8_DUAL_NO_REF);
        if (m8FlowerSupportOriginValid != 0u)
            context = M8FlowerSupportResolveTile(logicalTile + delta);
        uint leafSlot = context.w & M8_DUAL_SLOT_MASK;
        [unroll] for (uint word = 0u; word < 16u; ++word)
        {
            uint value = 0u;
            if (context.x == M8_DUAL_MIXED)
                value = _M8DualLeavesRead.Load(leafSlot * 64u + word * 4u);
            m8FlowerSupportWords[halo * 16u + word] = value;
        }
        if (context.x == M8_DUAL_MIXED &&
            !M8DualLeafResident(context.w, context.y, context.z, leafSlot))
            context.x = M8_DUAL_AMBIGUOUS;
        m8FlowerSupportHalo[halo] = context;
        if (context.x == M8_DUAL_AMBIGUOUS)
            InterlockedOr(m8FlowerSupportColdHalo, 1u << halo);
    }
    GroupMemoryBarrierWithGroupSync();
}

// A cold halo never invents FULL. The caller may invoke this after caching to
// enqueue the existing SSD residency operation, while keeping its page dirty.
// Missing/claimed spatial publications remain the caller's existing barrier.
void M8FlowerSupportRequestResidency(uint lane, uint laneCount)
{
    [loop] for (uint halo = lane; halo < M8_FLOWER_SUPPORT_HALO_COUNT;
        halo += laneCount)
    {
        uint4 context = m8FlowerSupportHalo[halo];
        if (context.x != M8_DUAL_AMBIGUOUS ||
            context.y >= MERKABA_M8_CHUNK_CAPACITY) continue;
        uint refIndex = context.y * MERKABA_M8_TILES_PER_CHUNK + context.z;
        if (_M8ChunkTileRefsRead[refIndex] == MERKABA_REF_COLD_ON_SSD)
            M8QueueColdTileLoad(refIndex, context.y, context.z);
    }
}

uint M8FlowerSupportOwnerState(int3 relativeOwner)
{
    int3 delta = relativeOwner >> 3;
    if (any(delta < -1) || any(delta > 1)) return M8_DUAL_AMBIGUOUS;
    uint3 halo = uint3(delta + 1);
    uint index = halo.x + 3u * (halo.y + 3u * halo.z);
    uint state = m8FlowerSupportHalo[index].x;
    if (state != M8_DUAL_MIXED) return state;
    uint3 local = asuint(relativeOwner) & 7u;
    uint kernel = local.x + 8u * (local.y + 8u * local.z);
    uint word = m8FlowerSupportWords[index * 16u + (kernel >> 5u)];
    return ((word >> (kernel & 31u)) & 1u) != 0u
        ? M8_DUAL_THROUGH : M8_DUAL_FULL;
}

uint M8FlowerSupportFreeCell(int3 relativeCell)
{
    uint packed = 0u;
    [unroll] for (uint corner = 0u; corner < 8u; ++corner)
    {
        int3 offset = int3(corner & 1u, (corner >> 1u) & 1u,
            (corner >> 2u) & 1u);
        packed |= M8FlowerSupportOwnerState(relativeCell + offset)
            << (2u * corner);
    }
    return M8FlowerClassifyFreeCell(packed);
}

uint M8FlowerSupportCellIndex(int3 relativeCell)
{
    uint3 local = uint3(relativeCell + 1);
    return local.x + 10u * (local.y + 10u * local.z);
}

// Six masks describe exact exposed elementary faces, owned by the FREE-side
// U in this tile. Corner/edge halo cells are only bounded scratch, never a
// persistent cell world. AMBIGUOUS neighbour pairs are reported separately.
void M8FlowerSupportBuildFaces(uint lane, uint laneCount)
{
    [loop] for (uint index = lane; index < M8_FLOWER_SUPPORT_CELL_COUNT;
        index += laneCount)
    {
        int3 cell = int3(index % 10u, (index / 10u) % 10u,
            index / 100u) - 1;
        m8FlowerSupportCells[index] = M8FlowerSupportFreeCell(cell);
    }
    GroupMemoryBarrierWithGroupSync();
    [loop] for (uint faceWord = lane;
        faceWord < M8_FLOWER_SUPPORT_FACE_WORDS; faceWord += laneCount)
    {
        uint face = faceWord >> 4u;
        uint first = (faceWord & 15u) << 5u;
        int3 direction = M8FlowerDirtFaceDirection(face);
        uint exposed = 0u, ambiguous = 0u;
        [unroll] for (uint bit = 0u; bit < 32u; ++bit)
        {
            uint kernel = first + bit;
            int3 cell = int3(kernel & 7u, (kernel >> 3u) & 7u,
                kernel >> 6u);
            uint own = m8FlowerSupportCells[M8FlowerSupportCellIndex(cell)];
            uint next = m8FlowerSupportCells[
                M8FlowerSupportCellIndex(cell + direction)];
            uint boundary = M8FlowerClassifyDirtFace(own, next);
            if (boundary == 1u)
            {
                bool representable = m8FlowerSupportOriginValid != 0u;
                int3 latticeVertex;
                [unroll] for (uint halfFace = 0u; halfFace < 2u; ++halfFace)
                    [unroll] for (uint vertex = 0u; vertex < 3u; ++vertex)
                        representable = M8FlowerTryDirtVertex(
                            m8FlowerSupportOrigin + cell, face, halfFace, vertex,
                            latticeVertex) && representable;
                if (representable) exposed |= 1u << bit;
                else ambiguous |= 1u << bit;
            }
            else if (boundary == 2u) ambiguous |= 1u << bit;
        }
        m8FlowerSupportFaces[faceWord] = exposed;
        m8FlowerSupportAmbiguousFaces[faceWord] = ambiguous;
        // Absence of proved direct coverage does not suppress DIRT. An
        // occupied owner, unknown direct root, or missing RGB is not coverage.
        m8FlowerSupportHalves[faceWord * 2u] = exposed;
        m8FlowerSupportHalves[faceWord * 2u + 1u] = exposed;
        m8FlowerSupportPartial[faceWord] = 0u;
    }
    GroupMemoryBarrierWithGroupSync();
}

// Exactly one lane writes each faceWord. Masks must come from the actual
// emitted CONFIRMED/COMPLETED footprint, never an owner bounding box. Covered
// certifies the whole fixed half-face. Partial explicitly needs the shared
// direct/support coverage resolver; this helper does not invent a clipped mesh.
// Unknown/unemitted direct geometry contributes zero to all four masks.
void M8FlowerSupportApplyCoverage(uint faceWord, uint2 coveredHalves,
    uint2 partialHalves)
{
    uint faces = m8FlowerSupportFaces[faceWord];
    m8FlowerSupportHalves[faceWord * 2u] = faces &
        ~(coveredHalves.x | partialHalves.x);
    m8FlowerSupportHalves[faceWord * 2u + 1u] = faces &
        ~(coveredHalves.y | partialHalves.y);
    m8FlowerSupportPartial[faceWord] = faces &
        (partialHalves.x | partialHalves.y);
}

// All lanes call after coverage writes. One bounded page-local prefix fixes
// output order (face, word, half, increasing owner bit), independent of lane
// scheduling. Allocation/publication remains the shared generational arena's
// responsibility. A nonzero unresolved result must not be reported as closure.
void M8FlowerSupportPrepareSymbols(uint lane)
{
    GroupMemoryBarrierWithGroupSync();
    if (lane == 0u)
    {
        uint count = 0u, unresolved = 0u;
        [loop] for (uint word = 0u; word < M8_FLOWER_SUPPORT_HALF_WORDS; ++word)
        {
            m8FlowerSupportOffsets[word] = count;
            count += countbits(m8FlowerSupportHalves[word]);
        }
        [loop] for (uint faceWord = 0u;
            faceWord < M8_FLOWER_SUPPORT_FACE_WORDS; ++faceWord)
            unresolved |= m8FlowerSupportAmbiguousFaces[faceWord] |
                m8FlowerSupportPartial[faceWord];
        m8FlowerSupportSymbolCount = count;
        m8FlowerSupportUnresolved = unresolved;
    }
    GroupMemoryBarrierWithGroupSync();
}

// Writes only within the caller's newly reserved immutable shared-arena span.
// It neither allocates a parallel surface arena nor changes FRONT. Any failed
// capacity check performs no writes, allowing the caller to retain old FRONT.
bool M8FlowerSupportEmitSymbols(RWByteAddressBuffer arena, uint firstSymbol,
    uint reservedSymbols, uint lane, uint laneCount)
{
    uint count = m8FlowerSupportSymbolCount;
    if (firstSymbol > M8_FLOWER_SUPPORT_ARENA_CAPACITY ||
        reservedSymbols > M8_FLOWER_SUPPORT_ARENA_CAPACITY - firstSymbol ||
        count > reservedSymbols) return false;
    [loop] for (uint word = lane; word < M8_FLOWER_SUPPORT_HALF_WORDS;
        word += laneCount)
    {
        uint bits = m8FlowerSupportHalves[word];
        uint faceWord = word >> 1u;
        uint firstOwner = (faceWord & 15u) << 5u;
        uint target = firstSymbol + m8FlowerSupportOffsets[word];
        [loop] while (bits != 0u)
        {
            uint bit = firstbitlow(bits);
            M8FlowerSymbolRecord symbol = M8FlowerCreateDirtSymbol(
                firstOwner + bit, faceWord >> 4u, word & 1u);
            arena.Store4(target * 16u, uint4(symbol.OwnerAndCarrier,
                symbol.RootsAndWedges, symbol.DetailRef, symbol.ThreadRef));
            target++;
            bits &= bits - 1u;
        }
    }
    return true;
}

#if defined(M8_FLOWER_PAGE_WRITE)
#define M8_FLOWER_SUPPORT_AMBIGUOUS 5u
#define M8_FLOWER_SUPPORT_WAIT_DIRECT 6u
groupshared M8FlowerPageBuild m8FlowerSupportPage;
groupshared uint m8FlowerSupportPageResult;

// After CacheTile -> BuildFaces -> actual emitted direct coverage, all lanes
// enter this stage together. It appends DIRT to the SAME reserved page after
// the direct-symbol prefix; it neither allocates a DIRT world nor publishes
// an empty/incomplete direct prefix as a finished page.
uint M8FlowerSupportBeginPage(uint slot,uint slotGeneration,uint sourceGeneration,
    uint directSymbolCount,uint drawSampleBytes,uint lane,uint laneCount,
    out M8FlowerPageBuild build)
{
    M8FlowerSupportPrepareSymbols(lane);
    if(lane==0u)
    {
        m8FlowerSupportPage=(M8FlowerPageBuild)0;
        if(m8FlowerSupportUnresolved!=0u || m8FlowerSupportOriginValid==0u)
            m8FlowerSupportPageResult=M8_FLOWER_SUPPORT_AMBIGUOUS;
        else if(directSymbolCount>M8_FLOWER_SYMBOL_CAPACITY-m8FlowerSupportSymbolCount)
            m8FlowerSupportPageResult=M8_FLOWER_ARENA_CAPACITY;
        else m8FlowerSupportPageResult=M8FlowerBeginPage(slot,slotGeneration,
            sourceGeneration,directSymbolCount+m8FlowerSupportSymbolCount,
            drawSampleBytes,m8FlowerSupportPage);
    }
    GroupMemoryBarrierWithGroupSync();
    build=m8FlowerSupportPage;
    if(m8FlowerSupportPageResult!=M8_FLOWER_ARENA_OK)
        return m8FlowerSupportPageResult;
    // BeginPage has already proved the complete span fits its allocation.
    // Every lane therefore reaches the same no-partial-write range decision.
    bool emitted=M8FlowerSupportEmitSymbols(_M8FlowerSymbolArena,
        build.SymbolAddress/16u+directSymbolCount,m8FlowerSupportSymbolCount,lane,laneCount);
    if(!emitted)
    {
        uint previous;
        InterlockedExchange(m8FlowerSupportPageResult,M8_FLOWER_ARENA_INVALID,previous);
    }
    GroupMemoryBarrierWithGroupSync();
    return m8FlowerSupportPageResult;
}

// This is a caller completion status, NOT another persistent receipt/header.
// The direct producer owns both counts and its current-generation coverage.
// False means keep the reserved assembly unpublished. The caller must finish
// it or abort it through M8FlowerAbortPage before reusing its work quantum.
uint M8FlowerSupportFinishPage(M8FlowerPageBuild build,int3 logicalTile,
    uint directSymbolCount,uint writtenDirectSymbols,bool directComplete,
    uint drawSampleCount,uint lane)
{
    GroupMemoryBarrierWithGroupSync();
    if(lane==0u)
    {
        if(!directComplete || writtenDirectSymbols!=directSymbolCount)
            m8FlowerSupportPageResult=M8_FLOWER_SUPPORT_WAIT_DIRECT;
        else if(m8FlowerSupportUnresolved!=0u ||
            directSymbolCount>M8_FLOWER_SYMBOL_CAPACITY-m8FlowerSupportSymbolCount)
            m8FlowerSupportPageResult=M8_FLOWER_SUPPORT_AMBIGUOUS;
        else m8FlowerSupportPageResult=M8FlowerFinishPendingPage(build,logicalTile,
            directSymbolCount+m8FlowerSupportSymbolCount,drawSampleCount)
                ?M8_FLOWER_ARENA_OK:M8_FLOWER_ARENA_INVALID;
    }
    GroupMemoryBarrierWithGroupSync();
    return m8FlowerSupportPageResult;
}
#endif

#endif
