#ifndef GENESIS_MERKABA_FLOWER_SUPPORT_INCLUDED
#define GENESIS_MERKABA_FLOWER_SUPPORT_INCLUDED

#include "MerkabaDualHierarchy.hlsl"
#include "MerkabaSphereFlower.generated.hlsl"
#include "MerkabaFlowerGeometry.hlsl"
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
#if !defined(M8_FLOWER_SUPPORT_SCRATCH_WORDS)
#define M8_FLOWER_SUPPORT_SCRATCH_WORDS M8_FLOWER_SUPPORT_CELL_COUNT
#endif
#define M8_FLOWER_SUPPORT_ARENA_CAPACITY 4194304u
// Every generated knot centre is within owner +/-a/2, and the largest
// (L0,R3) loop radius is 3a/2. This is only a conservative source exclusion,
// never a coverage/dual proof. The complete page [0,8]^3 therefore needs
// integer owners [-2,10]^3, all inside the existing 27-tile reference halo.
#define M8_FLOWER_COVERAGE_OWNER_MIN -2
#define M8_FLOWER_COVERAGE_OWNER_MAX 10
#define M8_FLOWER_COVERAGE_OWNER_SIDE 13u
#define M8_FLOWER_COVERAGE_OWNER_COUNT 2197u
#define M8_FLOWER_COVERAGE_OWNER_WORDS 69u
// Transient query receipt only: bit31 is required ambiguity, low27 bits are
// the actual cold contexts needed by that proof. Never persisted world state.
#define M8_FLOWER_SUPPORT_REQUIRED 0x80000000u
#define M8_FLOWER_SUPPORT_COLD_MASK 0x07ffffffu

// Context: resolved dual tile state, existing chunk index, tileLocal, packed
// physical leaf reference. Uniform ancestors need no positive M8 HOT owner.
groupshared uint4 m8FlowerSupportHalo[M8_FLOWER_SUPPORT_HALO_COUNT];
groupshared uint m8FlowerSupportWords[M8_FLOWER_SUPPORT_HALO_COUNT * 16u];
// BuildFaces owns the first 1000 words until its final collective barrier.
// Page compilation then reuses this backing for a bounded evidence packet.
// The refinement-only dual cache does not reference either lifetime.
groupshared uint m8FlowerSupportScratch[M8_FLOWER_SUPPORT_SCRATCH_WORDS];
groupshared uint m8FlowerSupportFaces[M8_FLOWER_SUPPORT_FACE_WORDS];
groupshared uint m8FlowerSupportHalves[M8_FLOWER_SUPPORT_HALF_WORDS];
groupshared uint m8FlowerSupportPartialHalves[M8_FLOWER_SUPPORT_HALF_WORDS];
groupshared uint m8FlowerSupportWitnessHalves[M8_FLOWER_SUPPORT_HALF_WORDS];
groupshared uint m8FlowerSupportBoundaryHalves[M8_FLOWER_SUPPORT_HALF_WORDS];
groupshared uint m8FlowerSupportOffsets[M8_FLOWER_SUPPORT_HALF_WORDS];
groupshared uint m8FlowerSupportColdHalo;
groupshared uint m8FlowerSupportSymbolCount;
groupshared uint m8FlowerSupportUnresolved;
groupshared int3 m8FlowerSupportOrigin;
groupshared uint m8FlowerSupportOriginValid;
groupshared uint m8FlowerSupportCoverageNeeded;
groupshared uint m8FlowerSupportCoverageUnresolved;
groupshared uint m8FlowerSupportCoverageOwners[M8_FLOWER_COVERAGE_OWNER_WORDS];

bool M8FlowerSupportLeafResident(uint packed,uint chunk,uint tile,out uint slot)
{
#if defined(M8_FLOWER_ENDPOINT_TILE_WRITE_VIEW)
    return M8DualLeafResident(packed,chunk,tile,slot,true);
#else
    return M8DualLeafResident(packed,chunk,tile,slot,false);
#endif
}

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
    if (!M8FlowerSupportLeafResident(context.w, chunk, address.tileLocal, leafSlot))
        context.x = M8_DUAL_AMBIGUOUS;
    return context;
}

// Read-only dual snapshot shared by page compaction and fine refinement.
// All lanes call once under the same dual lease. Only the 27 contexts, their
// 16 words, origin and cold mask are touched; no DIRT/coverage array is used.
void M8FlowerSupportCacheDualTile(int3 logicalTile, uint lane, uint laneCount)
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
            !M8FlowerSupportLeafResident(context.w, context.y, context.z, leafSlot))
            context.x = M8_DUAL_AMBIGUOUS;
        m8FlowerSupportHalo[halo] = context;
    }
    GroupMemoryBarrierWithGroupSync();
}

// The full page path adds its presentation-only initialization after the
// identical dual snapshot. BuildFaces still owns cell/face/coverage filling.
void M8FlowerSupportCacheTile(int3 logicalTile, uint lane, uint laneCount)
{
    M8FlowerSupportCacheDualTile(logicalTile,lane,laneCount);
    if(lane==0u)
    {
        m8FlowerSupportCoverageNeeded=0u;
        m8FlowerSupportCoverageUnresolved=0u;
        m8FlowerSupportUnresolved=0u;
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
        if ((m8FlowerSupportColdHalo & (1u << halo)) == 0u) continue;
        uint4 context = m8FlowerSupportHalo[halo];
        if (context.x != M8_DUAL_AMBIGUOUS ||
            context.y >= MERKABA_M8_CHUNK_CAPACITY) continue;
        uint refIndex = context.y * MERKABA_M8_TILES_PER_CHUNK + context.z;
        if (_M8ChunkTileRefsRead[refIndex] == MERKABA_REF_COLD_ON_SSD)
            M8QueueColdTileLoad(refIndex, context.y, context.z);
    }
}

uint M8FlowerSupportOwnerState(int3 relativeOwner,out uint coldHalo)
{
    coldHalo=0u;
    int3 delta = relativeOwner >> 3;
    if (any(delta < -1) || any(delta > 1)) return M8_DUAL_AMBIGUOUS;
    uint3 halo = uint3(delta + 1);
    uint index = halo.x + 3u * (halo.y + 3u * halo.z);
    uint state = m8FlowerSupportHalo[index].x;
    if(state==M8_DUAL_AMBIGUOUS)coldHalo=1u<<index;
    if (state != M8_DUAL_MIXED) return state;
    uint3 local = asuint(relativeOwner) & 7u;
    uint kernel = local.x + 8u * (local.y + 8u * local.z);
    uint word = m8FlowerSupportWords[index * 16u + (kernel >> 5u)];
    return ((word >> (kernel & 31u)) & 1u) != 0u
        ? M8_DUAL_THROUGH : M8_DUAL_FULL;
}

uint M8FlowerSupportFreeCell(int3 relativeCell,out uint coldHalo)
{
    uint packed = 0u;coldHalo=0u;
    // Cells/wedges already occupy independent lanes. Keep their eight
    // fixed support gathers in one body, not eight copies of halo decoding.
    [loop] for (uint corner = 0u; corner < 8u; ++corner)
    {
        int3 offset = int3(corner & 1u, (corner >> 1u) & 1u,
            (corner >> 2u) & 1u);
        uint cold;
        packed |= M8FlowerSupportOwnerState(relativeCell + offset,cold) << (2u * corner);
        coldHalo|=cold;
    }
    uint state=M8FlowerClassifyFreeCell(packed);
    // A strict THROUGH corner can prove FREE without the other contexts.
    // Such unused cold corners must not request I/O or block the page.
    if(state!=M8_FLOWER_CELL_AMBIGUOUS)coldHalo=0u;
    return state;
}

uint M8FlowerSupportFreeCell(int3 relativeCell)
{
    uint cold;
    return M8FlowerSupportFreeCell(relativeCell,cold);
}

void M8FlowerSupportRequire(uint receipt)
{
    if((receipt&M8_FLOWER_SUPPORT_REQUIRED)==0u)return;
    InterlockedOr(m8FlowerSupportUnresolved,M8_FLOWER_SUPPORT_REQUIRED);
    InterlockedOr(m8FlowerSupportColdHalo,receipt&M8_FLOWER_SUPPORT_COLD_MASK);
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
        uint cold;
        uint state=M8FlowerSupportFreeCell(cell,cold);
        // Two state bits and the exact 27-halo dependency fit the existing
        // cell word. Both adjacent faces consume this same frozen result.
        m8FlowerSupportScratch[index] = state | (cold << 2u);
    }
    [loop] for (uint faceWord = lane;
        faceWord < M8_FLOWER_SUPPORT_FACE_WORDS; faceWord += laneCount)
        m8FlowerSupportFaces[faceWord] = 0u;
    GroupMemoryBarrierWithGroupSync();
    // One lane evaluates one elementary face, not a fully unrolled word of
    // 32 faces. Only the final bit masks are shared; the exact cell and vertex
    // predicates have one instruction-stream instance for the whole page.
    [loop] for (uint item = lane; item < 6u * 512u; item += laneCount)
    {
        uint face = item >> 9u, kernel = item & 511u;
        uint faceWord = face * 16u + (kernel >> 5u);
        uint bit = 1u << (kernel & 31u);
        int3 cell = int3(kernel & 7u, (kernel >> 3u) & 7u, kernel >> 6u);
        int3 direction = M8FlowerDirtFaceDirection(face);
        uint own = m8FlowerSupportScratch[M8FlowerSupportCellIndex(cell)];
        uint next = m8FlowerSupportScratch[M8FlowerSupportCellIndex(cell + direction)];
        uint boundary = M8FlowerClassifyDirtFace(own & 3u, next & 3u);
        if (boundary == 1u)
        {
            bool representable = m8FlowerSupportOriginValid != 0u;
            int3 latticeVertex;
            [loop] for (uint vertex = 0u; vertex < 6u; ++vertex)
                representable = M8FlowerTryDirtVertex(m8FlowerSupportOrigin + cell,
                    face, vertex / 3u, vertex % 3u, latticeVertex) && representable;
            if (representable) InterlockedOr(m8FlowerSupportFaces[faceWord], bit);
            else InterlockedOr(m8FlowerSupportUnresolved, bit);
        }
        else if (boundary == 2u)
        {
            InterlockedOr(m8FlowerSupportUnresolved, bit);
            // Only a required undecidable face may request a cold context.
            InterlockedOr(m8FlowerSupportColdHalo, (own | next) >> 2u);
        }
    }
    GroupMemoryBarrierWithGroupSync();
    [loop] for (uint faceWord = lane;
        faceWord < M8_FLOWER_SUPPORT_FACE_WORDS; faceWord += laneCount)
    {
        uint exposed = m8FlowerSupportFaces[faceWord];
        if(exposed!=0u)InterlockedOr(m8FlowerSupportCoverageNeeded,1u);
        // Absence of proved direct coverage does not suppress DIRT. An
        // occupied owner, unknown direct root, or missing RGB is not coverage.
        m8FlowerSupportHalves[faceWord * 2u] = exposed;
        m8FlowerSupportHalves[faceWord * 2u + 1u] = exposed;
        m8FlowerSupportPartialHalves[faceWord * 2u] = 0u;
        m8FlowerSupportPartialHalves[faceWord * 2u + 1u] = 0u;
        m8FlowerSupportWitnessHalves[faceWord * 2u] = 0u;
        m8FlowerSupportWitnessHalves[faceWord * 2u + 1u] = 0u;
        m8FlowerSupportBoundaryHalves[faceWord * 2u] = 0u;
        m8FlowerSupportBoundaryHalves[faceWord * 2u + 1u] = 0u;
        // Before PrepareSymbols these words hold proved complete coverage;
        // the prefix calculation reuses them only after the coverage barrier.
        m8FlowerSupportOffsets[faceWord * 2u] = 0u;
        m8FlowerSupportOffsets[faceWord * 2u + 1u] = 0u;
    }
    GroupMemoryBarrierWithGroupSync();
}

bool M8FlowerSupportCoverCells(M8FlowerInterval3 bounds,out int3 firstCell,out int3 lastCell)
{
    firstCell=lastCell=0;
    M8FlowerInterval components[3];
    components[0]=bounds.x;components[1]=bounds.y;components[2]=bounds.z;
    [loop]for(uint axis=0u;axis<3u;axis++)
    {
        M8FlowerInterval cells;
        if(!M8FlowerIDivPositive(components[axis],
            M8FlowerI(M8_FLOWER_LATTICE_STEP,M8_FLOWER_LATTICE_STEP),cells) ||
            !all(M8FlowerIsFinite(float2(cells.lo,cells.hi))) || cells.lo < -8.0 || cells.hi >= 15.0)return false;
        firstCell[axis]=(int)floor(cells.lo);lastCell[axis]=(int)floor(cells.hi);
    }
    return true;
}

#if defined(M8_FLOWER_CARRIER_BATCH_READ)
bool M8FlowerSupportSiteBounds(uint carrier,uint site,M8FlowerPhaseRootEvidence root,
    out M8FlowerInterval3 bounds);
#else
bool M8FlowerSupportSiteBounds(uint carrier,uint site,M8FlowerPhaseRootEvidence root,
    out M8FlowerInterval3 bounds)
{
    return M8FlowerRootRelativeBounds(M8FlowerL2CarrierKnot(carrier,site),root,bounds);
}
#endif

bool M8FlowerSupportWedgeBounds(int3 owner,uint carrier,uint wedge,
    M8FlowerPhaseRootEvidence roots[3],out M8FlowerInterval3 minimumMaximum[3],
    out int3 firstCell,out int3 lastCell)
{
    firstCell=lastCell=0;
    uint3 sites=M8FlowerL2CarrierTriangle(wedge,false);
    int3 relativeOwner=owner-m8FlowerSupportOrigin;
    if(any(relativeOwner < -8) || any(relativeOwner > 15))return false;
    M8FlowerInterval3 translation;
    translation.x=M8FlowerIMul(M8FlowerI(relativeOwner.x,relativeOwner.x),
        M8FlowerI(M8_FLOWER_LATTICE_STEP,M8_FLOWER_LATTICE_STEP));
    translation.y=M8FlowerIMul(M8FlowerI(relativeOwner.y,relativeOwner.y),
        M8FlowerI(M8_FLOWER_LATTICE_STEP,M8_FLOWER_LATTICE_STEP));
    translation.z=M8FlowerIMul(M8FlowerI(relativeOwner.z,relativeOwner.z),
        M8FlowerI(M8_FLOWER_LATTICE_STEP,M8_FLOWER_LATTICE_STEP));
    [loop]for(uint vertex=0u;vertex<3u;vertex++)
    {
        M8FlowerInterval3 relative;
        uint site=sites[vertex];
        if(!M8FlowerSupportSiteBounds(carrier,site,roots[vertex],relative))return false;
        minimumMaximum[vertex].x=M8FlowerIAdd(relative.x,translation.x);
        minimumMaximum[vertex].y=M8FlowerIAdd(relative.y,translation.y);
        minimumMaximum[vertex].z=M8FlowerIAdd(relative.z,translation.z);
    }
    M8FlowerInterval3 bounds;
    bounds.x=M8FlowerI(min(min(minimumMaximum[0].x.lo,minimumMaximum[1].x.lo),minimumMaximum[2].x.lo),
        max(max(minimumMaximum[0].x.hi,minimumMaximum[1].x.hi),minimumMaximum[2].x.hi));
    bounds.y=M8FlowerI(min(min(minimumMaximum[0].y.lo,minimumMaximum[1].y.lo),minimumMaximum[2].y.lo),
        max(max(minimumMaximum[0].y.hi,minimumMaximum[1].y.hi),minimumMaximum[2].y.hi));
    bounds.z=M8FlowerI(min(min(minimumMaximum[0].z.lo,minimumMaximum[1].z.lo),minimumMaximum[2].z.lo),
        max(max(minimumMaximum[0].z.hi,minimumMaximum[1].z.hi),minimumMaximum[2].z.hi));
    return M8FlowerSupportCoverCells(bounds,firstCell,lastCell);
}

// The convex hull of the THREE metric knot intervals is contained in this
// cover. It is not an M8 owner box. All cells THROUGH proves a veto over the
// entire represented wedge; all FULL proves no portion is vetoed. Mixed or
// nonresident covers remain unresolved rather than deleting possible matter.
uint M8FlowerSupportCoverDual(int3 first,int3 last,out uint coldHalo)
{
    coldHalo=0u;
    bool full=false,through=false;
    [loop]for(int z=first.z;z<=last.z;z++)
        [loop]for(int y=first.y;y<=last.y;y++)
            [loop]for(int x=first.x;x<=last.x;x++)
            {
                uint cold;
                uint state=M8FlowerSupportFreeCell(int3(x,y,z),cold);
                if(state==M8_FLOWER_CELL_AMBIGUOUS){coldHalo=cold;return 2u;}
                if(state==M8_FLOWER_CELL_FREE)through=true;
                else full=true;
                if(full && through)return 2u;
            }
    return through?1u:0u;
}

uint M8FlowerEvaluateWedgeDual(int3 owner,uint carrier,uint wedge,
    M8FlowerPhaseRootEvidence roots[3],out int3 first,out int3 last,out uint coldHalo)
{
    coldHalo=0u;
    M8FlowerInterval3 bounds[3];
    if(!M8FlowerSupportWedgeBounds(owner,carrier,wedge,roots,bounds,first,last))return 2u;
    return M8FlowerSupportCoverDual(first,last,coldHalo);
}

#if defined(M8_FLOWER_CARRIER_BATCH_READ)
uint M8FlowerSupportWedgeDual(int3 owner,uint carrier,uint wedge,
    M8FlowerPhaseRootEvidence roots[7],out int3 first,out int3 last,out uint coldHalo);
#else
uint M8FlowerSupportWedgeDual(int3 owner,uint carrier,uint wedge,
    M8FlowerPhaseRootEvidence roots[7],out int3 first,out int3 last,out uint coldHalo)
{
    uint3 sites=M8FlowerL2CarrierTriangle(wedge,false);
    M8FlowerPhaseRootEvidence knots[3];
    knots[0]=roots[sites.x];knots[1]=roots[sites.y];knots[2]=roots[sites.z];
    return M8FlowerEvaluateWedgeDual(owner,carrier,wedge,knots,first,last,coldHalo);
}
#endif

uint M8FlowerSupportR3RootDual(int3 owner,uint nodeIndex,M8FlowerPhaseRootEvidence root,
    out uint coldHalo)
{
    coldHalo=0u;
    if(nodeIndex<18u || nodeIndex>=26u)return 2u;
    int3 relativeOwner=owner-m8FlowerSupportOrigin;
    if(any(relativeOwner < -8) || any(relativeOwner > 15))return 2u;
    M8FlowerInterval3 bounds;
    if(!M8FlowerRootNodeRelativeBounds(nodeIndex,root,bounds))return 2u;
    M8FlowerInterval step=M8FlowerI(M8_FLOWER_LATTICE_STEP,M8_FLOWER_LATTICE_STEP);
    bounds.x=M8FlowerIAdd(bounds.x,M8FlowerIMul(M8FlowerI(relativeOwner.x,relativeOwner.x),step));
    bounds.y=M8FlowerIAdd(bounds.y,M8FlowerIMul(M8FlowerI(relativeOwner.y,relativeOwner.y),step));
    bounds.z=M8FlowerIAdd(bounds.z,M8FlowerIMul(M8FlowerI(relativeOwner.z,relativeOwner.z),step));
    int3 first,last;
    if(!M8FlowerSupportCoverCells(bounds,first,last))return 2u;
    return M8FlowerSupportCoverDual(first,last,coldHalo);
}

// One independent R3 alternative. The page WG evaluates the eight values
// once, on eight lanes, then shares them between reached completion flags.
// Result bits are known/ambiguous/dual-allowed/dual-veto, not authority state.
uint M8FlowerReadJunctionAlternative(uint slot,uint ownerRef,int3 owner,uint flags,
    float2 errors,uint alternative,out M8FlowerInterval q,out uint tag,out uint cold)
{
    q=M8FlowerI(0,0);tag=0u;cold=0u;
    uint axis=alternative>>1u;
    M8FlowerPhaseRootEvidence observed;
    uint status=M8FlowerReadR3Alternative(slot,ownerRef,owner,flags,axis,
        (alternative&1u)!=0u,errors,q,observed);
    if(status!=1u)return status==0u?0u:2u;
    tag=observed.Tag;
    uint parity=((uint)owner.x&1u)|(((uint)owner.y&1u)<<1u)|(((uint)owner.z&1u)<<2u);
    uint node=2u*(uint)M8FlowerTetraLineAt(parity)[axis]+(M8FlowerTetraEtaAt(parity)[axis]<0?1u:0u);
    uint dual=M8FlowerSupportR3RootDual(owner,node,observed,cold);
    return 1u|(dual==0u?4u:dual==1u?8u:0u);
}

// Same invocation in the count and emit passes; the caller's source lease
// fixes both M8/detail and dual snapshots between them.
uint M8FlowerPageCarrier(uint slot,uint local,uint carrier,float2 errors,
    uint completion,bool sourceProof,uint requiredWedges,
    out M8FlowerSymbolRecord symbol,out uint unresolved,
    out uint directWedges,out M8FlowerPhaseRootEvidence roots[7],out float3 positions[7],
    out int3 carrierFirst,out int3 carrierLast,out uint supportReceipt)
{
    carrierFirst=15;carrierLast=-8;supportReceipt=0u;
    uint status=M8FlowerClassifyL2Carrier(slot,local,carrier,errors,completion,symbol,unresolved,
        directWedges,roots,positions);
    // The raw selector remains owner-local for scan/phase closure. Only the
    // generated canonical incident owner emits each complete three-knot
    // petal; ambiguity belonging solely to another owner cannot dirty this
    // page. This decision uses no normal, sampled position or residency tie.
    uint owned=sourceProof?63u:M8FlowerL2CarrierOwnedMask(carrier);
    unresolved&=owned;
    if(status!=1u)return status==2u && unresolved!=0u?2u:0u;
    uint active=M8FlowerDrawActiveWedgeMask(symbol)&owned;
    // Parent closure consumes the complete source petal even if another
    // generated incident owner emits a wedge. Draw ownership is not evidence.
    uint examine=active|directWedges;
    int3 owner=M8FlowerEndpointOwner(slot,local);
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        uint bit=1u<<wedge;if((examine&bit)==0u)continue;
        int3 first,last;
        uint cold;
        uint dual=M8FlowerSupportWedgeDual(owner,carrier,wedge,roots,first,last,cold);
        if(dual!=0u){active&=~bit;directWedges&=~bit;}
        else if((active&bit)!=0u)
        {
            // Coverage reuses the EXACT interval cell bounds just consumed
            // by dual admission. Only surviving draw wedges contribute;
            // do not evaluate their metric roots a second time.
            carrierFirst=min(carrierFirst,first);carrierLast=max(carrierLast,last);
        }
        if(dual==2u && (owned&bit)!=0u)unresolved|=bit;
        if(dual==2u && (requiredWedges&bit)!=0u)
            supportReceipt|=M8_FLOWER_SUPPORT_REQUIRED|cold;
    }
    if(active!=0u)
    {
        // A boundary face belongs to either adjacent U. Include the lower
        // neighbour at an exact integer coordinate, with the same clipping
        // as the former separate coverage-bound evaluation.
        carrierFirst=max(carrierFirst-1,0);carrierLast=min(carrierLast,7);
    }
    symbol.OwnerAndCarrier=(symbol.OwnerAndCarrier&
        ~(M8_FLOWER_DRAW_WEDGE_MASK<<M8_FLOWER_DRAW_ACTIVE_SHIFT))|
        (active<<M8_FLOWER_DRAW_ACTIVE_SHIFT);
    uint completed=M8FlowerDrawCompletedWedgeMask(symbol)&active;
    uint reverse=M8FlowerDrawReverseWedgeMask(symbol)&active;
    uint used=0u;
    [unroll]for(uint wedge=0u;wedge<6u;wedge++)if((active&(1u<<wedge))!=0u)
        used|=1u|(1u<<(1u+wedge))|(1u<<(1u+(wedge+1u)%6u));
    symbol.RootsAndWedges=(M8FlowerDrawHubSector(symbol)<<M8_FLOWER_DRAW_HUB_SECTOR_SHIFT)|
        (M8FlowerDrawRootSigns(symbol)&used)|(completed<<M8_FLOWER_DRAW_COMPLETED_SHIFT)|
        (reverse<<M8_FLOWER_DRAW_REVERSE_SHIFT);
    [unroll]for(uint site=0u;site<7u;site++)if((used&(1u<<site))==0u)
    {roots[site]=(M8FlowerPhaseRootEvidence)0;positions[site]=0.0;}
    return active!=0u?1u:unresolved!=0u?2u:0u;
}

uint M8FlowerPageCarrier(uint slot,uint local,uint carrier,float2 errors,
    uint completion,
    out M8FlowerSymbolRecord symbol,out uint unresolved,
    out uint directWedges,out M8FlowerPhaseRootEvidence roots[7],out float3 positions[7])
{
    int3 carrierFirst,carrierLast;uint supportReceipt;
    return M8FlowerPageCarrier(slot,local,carrier,errors,completion,false,
        M8FlowerL2CarrierOwnedMask(carrier),symbol,unresolved,directWedges,roots,positions,
        carrierFirst,carrierLast,supportReceipt);
}

uint M8FlowerPageCarrier(uint slot,uint local,uint carrier,float2 errors,
    out M8FlowerSymbolRecord symbol,out uint unresolved,
    out uint directWedges,out M8FlowerPhaseRootEvidence roots[7],out float3 positions[7])
{
    return M8FlowerPageCarrier(slot,local,carrier,errors,0xffffffffu,
        symbol,unresolved,directWedges,roots,positions);
}

uint M8FlowerPageCarrier(uint slot,uint local,uint carrier,float2 errors,
    out M8FlowerSymbolRecord symbol,out uint unresolved,
    out M8FlowerPhaseRootEvidence roots[7],out float3 positions[7])
{
    uint directWedges;
    return M8FlowerPageCarrier(slot,local,carrier,errors,symbol,unresolved,
        directWedges,roots,positions);
}

// The immutable direct pass records the actual selected original anchors.
// These are 48-bit sign/seen planes, not another root or position cache.
// Same owner/node/sign under the source lease reproduces the identical
// symbolic root and enclosure; disagreements remove the whole source petal.
struct M8FlowerDirectAnchors
{
    uint2 Signs[3];
    uint2 Seen[3];
    uint2 Conflict;
};

void M8FlowerResetDirectAnchors(out M8FlowerDirectAnchors receipt)
{
    [unroll]for(uint anchor=0u;anchor<3u;anchor++)
    {receipt.Signs[anchor]=0u;receipt.Seen[anchor]=0u;}
    receipt.Conflict=0u;
}

void M8FlowerRecordDirectAnchors(uint carrier,uint directWedges,
    M8FlowerPhaseRootEvidence roots[7],inout M8FlowerDirectAnchors receipt)
{
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        if((directWedges&(1u<<wedge))==0u)continue;
        uint petal=M8FlowerL2WedgeAt(6u*carrier+wedge).y>>4u;
        uint word=petal>>5u,bit=1u<<(petal&31u);
        uint3 sites=M8FlowerL2CarrierTriangle(wedge,false);
        [loop]for(uint vertex=0u;vertex<3u;vertex++)
        {
            uint site=sites[vertex],sign=(roots[site].Tag>>7u)&1u;
            M8FlowerGeometryNode original;
            if(!M8FlowerL2GeometryNode(M8FlowerL2CarrierKnot(carrier,site),sign!=0u,original))
            {receipt.Conflict[word]|=bit;continue;}
            if(original.Level!=0u)continue;
            uint anchor=3u,node=0u;
            [unroll]for(uint candidate=0u;candidate<3u;candidate++)
            {
                uint incident=M8FlowerPetalNodesAt(petal)[candidate];
                if((uint)M8FlowerNodeAt(incident).w==original.Line &&
                    all(M8FlowerNodeAt(incident).xyz==original.Offset))
                {anchor=candidate;node=incident;}
            }
            uint2 references;uint sector;
            if(anchor==3u || roots[site].Classification!=1u || (roots[site].Tag&7u)!=0u ||
                !M8FlowerPhaseRootSector(roots[site],sector) ||
                !M8FlowerAnchorRootReferences(node,roots[site].Tag,references) ||
                (references[word]&bit)==0u)
            {receipt.Conflict[word]|=bit;continue;}
            if((receipt.Seen[anchor][word]&bit)!=0u &&
                (((receipt.Signs[anchor][word]&bit)!=0u)!=(sign!=0u)))
                receipt.Conflict[word]|=bit;
            else
            {
                receipt.Seen[anchor][word]|=bit;
                if(sign!=0u)receipt.Signs[anchor][word]|=bit;
            }
        }
    }
}

uint2 M8FlowerDirectAnchorMask(M8FlowerDirectAnchors receipt)
{
    return receipt.Seen[0]&receipt.Seen[1]&receipt.Seen[2]&~receipt.Conflict;
}

// Completion consumes only the frozen direct boundary. The stored sign is
// regenerated through the SAME endpoint reader; root-owner masks never
// reselect it. Derived results are never inserted into this receipt.
uint M8FlowerCompletionDonor(uint slot,uint ownerRef,int3 owner,uint flags,
    float2 errors,uint petal,uint anchor,uint2 direct,M8FlowerDirectAnchors receipt,
    out M8FlowerPhaseRootEvidence selected)
{
    selected=(M8FlowerPhaseRootEvidence)0;
    uint node=M8FlowerPetalNodesAt(petal)[anchor];
    uint2 donors=M8FlowerCompletionNeighboursAt(petal)&M8FlowerNodeIncidentPetalsAt(node)&direct;
    bool found=false;
    [loop]for(uint word=0u;word<2u;word++)
    {
        uint bits=donors[word];
        [loop]while(bits!=0u)
        {
            uint donor=32u*word+(uint)firstbitlow(bits),bit=1u<<(donor&31u);
            bits&=bits-1u;
            uint donorAnchor=3u;
            [unroll]for(uint candidate=0u;candidate<3u;candidate++)
                if(M8FlowerPetalNodesAt(donor)[candidate]==node)donorAnchor=candidate;
            if(donorAnchor==3u || (receipt.Seen[donorAnchor][word]&bit)==0u ||
                (receipt.Conflict[word]&bit)!=0u)return 2u;
            bool sign=(receipt.Signs[donorAnchor][word]&bit)!=0u;
            M8FlowerPhaseRootEvidence root;bool provisional;uint2 allowed;uint sector;
            uint read=M8FlowerReadOriginalShared(slot,ownerRef,owner,flags,node,sign,
                errors.x,errors.y,root,provisional);
            if(read!=1u || provisional || !M8FlowerPhaseRootSector(root,sector) ||
                !M8FlowerAnchorRootReferences(node,root.Tag,allowed) ||
                (allowed[word]&bit)==0u)return 2u;
            if((allowed[petal>>5u]&(1u<<(petal&31u)))==0u)return 0u;
            if(!found)
            {
                selected=root;found=true;continue;
            }
            if(any(selected.Junction!=root.Junction) ||
                ((selected.Tag^root.Tag)&0x1fffu)!=0u)return 0u;
            M8FlowerPhaseRootEvidence closed;
            uint status=M8FlowerCloseSharedPhaseRoot(selected,root,closed);
            if(status!=1u)return status;
            selected=closed;
        }
    }
    return found?1u:0u;
}

// Donor order and its endpoint receipts remain local to one actual flag.
// A failed donor set does not request the shared R3 alternative evaluation.
uint M8FlowerPrepareCompletionAnchors(uint slot,uint local,float2 errors,
    uint2 direct,uint petal,M8FlowerDirectAnchors receipt,out uint token,out uint orientation)
{
    token=petal;orientation=2u;
    KernelState state=M8LoadKernelStateRead(slot,local);
    int3 owner=M8FlowerEndpointOwner(slot,local);
    uint ownerRef=M8FlowerFindOwner(slot,local,M8FlowerEndpointGeneration(slot));
    float3 normal;float delta;M8FlowerUnpackPlane(state.flags,normal,delta);
    bool uncertain=false;M8FlowerInterval3 bounds[3];
    [loop]for(uint anchor=0u;anchor<3u;anchor++)
    {
        M8FlowerPhaseRootEvidence root;
        uint status=M8FlowerCompletionDonor(slot,ownerRef,owner,state.flags,errors,
            petal,anchor,direct,receipt,root);
        if(status==0u)return 0u;
        if(status!=1u){uncertain=true;continue;}
        token|=((root.Tag>>7u)&1u)<<(6u+anchor);
        uint nodeIndex=M8FlowerPetalNodesAt(petal)[anchor];
        if(!M8FlowerRootNodeRelativeBounds(nodeIndex,root,bounds[anchor]))uncertain=true;
    }
    if(uncertain)return 2u;
    if(M8FlowerPetalNodesAt(petal).w==0u)
    {M8FlowerInterval3 swap=bounds[1];bounds[1]=bounds[2];bounds[2]=swap;}
    orientation=M8FlowerCarrierWedgeOrientation(bounds[0],bounds[1],bounds[2],normal,errors.x);
    return 1u;
}

// Junction precedence is unchanged: an unresolved junction is not replaced
// by a precomputed orientation result. All sixteen child proofs are still
// required by the caller before a prepared token can become COMPLETED.
uint M8FlowerFinishCompletionPetal(uint token,uint parity,uint orientation,
    M8FlowerJunctionSelection junction)
{
    if(junction.Classification!=1u)return junction.Classification==0u?0u:2u;
    uint petal=token&63u;
    uint junctionAxis=4u;uint2 rule=M8FlowerJunctionRuleAt(junction.ClassIndex);
    [unroll]for(uint axis=0u;axis<4u;axis++)
    {
        uint lineClass=(uint)M8FlowerTetraLineAt(parity)[axis];
        if(((rule.y>>(6u*(lineClass-9u)))&63u)==petal)junctionAxis=axis;
    }
    if(junctionAxis==4u || ((token>>8u)&1u)!=((junction.RootSigns>>junctionAxis)&1u))return 0u;
    return orientation;
}

M8FlowerInterval M8FlowerSupportDifference(float a,float b)
{
    if(a==b)return M8FlowerI(0.0,0.0);
    return M8FlowerISub(M8FlowerI(a,a),M8FlowerI(b,b));
}

M8FlowerInterval M8FlowerSupportOrient2(float2 a,float2 b,float2 p)
{
    M8FlowerInterval products[2];
    [loop]for(uint axis=0u;axis<2u;axis++)
        products[axis]=M8FlowerIMul(M8FlowerSupportDifference(b[axis],a[axis]),
            M8FlowerSupportDifference(p[1u-axis],a[1u-axis]));
    return M8FlowerISub(products[0],products[1]);
}

// Final binary32 draw positions are exact inputs here, not refitted metric
// planes. Only COPLANAR geometry can cover the same two-dimensional face;
// there is no projection/tolerance capable of hiding another parallel sheet.
// 0 disjoint, 1 complete half covered, 2 partial/undecidable overlap.
uint M8FlowerSupportTriangleCoverage(float3 directVertices[3],int3 cell,uint face,uint halfFace)
{
    uint axis=face>>1u,u=(axis+1u)%3u,v=(axis+2u)%3u;
    float3 halfVertices[3];
    [unroll]for(uint i=0u;i<3u;i++)halfVertices[i]=M8FlowerDirtGridPosition(cell,face,halfFace,i);
    float plane=halfVertices[0][axis];
    if(directVertices[0][axis]!=plane || directVertices[1][axis]!=plane || directVertices[2][axis]!=plane)return 0u;
    float2 vertices[6];
    [unroll]for(uint i=0u;i<3u;i++)
    {
        vertices[i]=float2(directVertices[i][u],directVertices[i][v]);
        vertices[3u+i]=float2(halfVertices[i][u],halfVertices[i][v]);
    }
    // Independent face/half queries already occupy distinct GPU lanes.
    // Both triangles' ordered separating axes use this one interval call
    // site; do not expand eighteen copies of its outward arithmetic.
    [loop]for(uint side=0u;side<2u;side++)
    {
        uint first=3u*side,other=3u-first;
        M8FlowerInterval orientation=M8FlowerSupportOrient2(vertices[first],vertices[first+1u],vertices[first+2u]);
        int winding=orientation.lo>0.0?1:orientation.hi<0.0?-1:0;
        if(winding==0)return 2u;
        bool covered=true;
        [loop]for(uint edge=0u;edge<3u;edge++)
        {
            bool outside=true;
            [loop]for(uint sampleIndex=0u;sampleIndex<3u;sampleIndex++)
            {
                M8FlowerInterval distance=M8FlowerSupportOrient2(vertices[first+edge],
                    vertices[first+(edge+1u)%3u],vertices[other+sampleIndex]);
                if(winding<0)distance=M8FlowerI(-distance.hi,-distance.lo);
                covered=covered && distance.lo>=0.0;
                outside=outside && distance.hi<0.0;
            }
            if(outside)return 0u;
        }
        // Only direct containment certifies coverage. The converse merely
        // excludes disjointness; it never turns the DIRT half into a donor.
        if(side==0u && covered)return 1u;
    }
    return 2u;
}

// Can this segment enter the OPEN half-triangle? Clip only an outward
// enclosure of possible parameters. Returning false is a proof; boundary
// contact alone is not a crossing into the represented two-dimensional area.
bool M8FlowerSupportSegmentMayEnter(float2 first,float2 last,float2 halfVertices[3],int winding)
{
    float lower=0.0,upper=1.0;
    [loop]for(uint edge=0u;edge<3u;edge++)
    {
        M8FlowerInterval endpoint[2];
        [loop]for(uint side=0u;side<2u;side++)
        {
            M8FlowerInterval value=M8FlowerSupportOrient2(halfVertices[edge],
                halfVertices[(edge+1u)%3u],side==0u?first:last);
            if(winding<0)value=M8FlowerI(-value.hi,-value.lo);
            endpoint[side]=value;
        }
        M8FlowerInterval a=endpoint[0],b=endpoint[1];
        // s(t) <= (1-t)*a.hi+t*b.hi on the entire segment.
        if(a.hi<=0.0 && b.hi<=0.0)return false;
        if(a.hi>0.0 && b.hi>0.0)continue;
        bool entering=a.hi<=0.0;
        float numerator=entering?-a.hi:a.hi;
        float head=entering?b.hi:a.hi,tail=entering?a.hi:b.hi;
        M8FlowerInterval denominator=M8FlowerISub(M8FlowerI(head,head),M8FlowerI(tail,tail)),limit;
        if(!M8FlowerIDivPositive(M8FlowerI(numerator,numerator),denominator,limit))return true;
        if(entering)lower=max(lower,limit.lo);
        else upper=min(upper,limit.hi);
        if(lower>=upper)return false;
    }
    return lower<upper;
}

bool M8FlowerSupportContainsWitness(float2 a,float2 b,float2 c,M8FlowerInterval2 witness,int winding)
{
    if(winding==0)return false;
    float2 vertices[3];vertices[0]=a;vertices[1]=b;vertices[2]=c;
    [loop]for(uint edge=0u;edge<3u;edge++)
    {
        float2 first=vertices[edge],last=vertices[(edge+1u)%3u];
        M8FlowerInterval x=M8FlowerISub(witness.x,M8FlowerI(first.x,first.x));
        M8FlowerInterval y=M8FlowerISub(witness.y,M8FlowerI(first.y,first.y));
        M8FlowerInterval side=M8FlowerISub(
            M8FlowerIMul(M8FlowerSupportDifference(last.x,first.x),y),
            M8FlowerIMul(M8FlowerSupportDifference(last.y,first.y),x));
        if(winding<0)side=M8FlowerI(-side.hi,-side.lo);
        if(side.lo<0.0)return false;
    }
    return true;
}

// A generated edge match precedes this comparison. Equal midpoint positions
// alone are never identity. Equal complete symbols and frozen root intervals
// reproduce the same two endpoints through the shared position evaluator.
bool M8FlowerSupportSameRoot(M8FlowerPhaseRootEvidence a,M8FlowerPhaseRootEvidence b)
{
    return a.Classification==1u && b.Classification==1u && all(a.Junction==b.Junction) &&
        (a.Tag&0x1fffu)==(b.Tag&0x1fffu) &&
        all(asuint(float4(a.Root.x.lo,a.Root.x.hi,a.Root.y.lo,a.Root.y.hi))==
            asuint(float4(b.Root.x.lo,b.Root.x.hi,b.Root.y.lo,b.Root.y.hi)));
}

uint M8FlowerSupportPairAxes(M8FlowerPhaseRootEvidence firstRoot,M8FlowerPhaseRootEvidence lastRoot,
    M8FlowerPhaseRootEvidence peerFirst,M8FlowerPhaseRootEvidence peerLast,
    float3 first,float3 last,float3 ownThird,float3 peerThird)
{
    if(!M8FlowerSupportSameRoot(firstRoot,peerFirst) ||
        !M8FlowerSupportSameRoot(lastRoot,peerLast))return 0u;
    uint axes=0u;
    [loop]for(uint axis=0u;axis<3u;axis++)
    {
        if(first[axis]!=last[axis] || first[axis]!=ownThird[axis] || first[axis]!=peerThird[axis])continue;
        uint u=(axis+1u)%3u,v=(axis+2u)%3u;
        float2 a=float2(first[u],first[v]),b=float2(last[u],last[v]);
        M8FlowerInterval side[2];
        [loop]for(uint endpoint=0u;endpoint<2u;endpoint++)
        {
            float3 third=endpoint==0u?ownThird:peerThird;
            side[endpoint]=M8FlowerSupportOrient2(a,b,float2(third[u],third[v]));
        }
        M8FlowerInterval own=side[0],peer=side[1];
        // Coincident edges on the SAME side do not cancel a union boundary.
        if((own.lo>0.0 && peer.hi<0.0) || (own.hi<0.0 && peer.lo>0.0))axes|=1u<<axis;
    }
    return axes;
}

#define M8_FLOWER_COVERAGE_OVERLAP 1u
#define M8_FLOWER_COVERAGE_WITNESS 2u
#define M8_FLOWER_COVERAGE_BOUNDARY 4u
#define M8_FLOWER_COVERAGE_COMPLETE 8u

// A contribution to the finite closed union, not an area sum. Internal fan
// spokes cancel locally. An outer edge cancels only after an exact generated
// partner was read and proved present on the opposite side of that SAME edge.
uint M8FlowerSupportCarrierCoverageProof(float3 positions[7],uint active,uint pairedOuterEdges,
    int3 cell,uint face,uint halfFace)
{
    float3 halfWorld[3];
    [unroll]for(uint sampleIndex=0u;sampleIndex<3u;sampleIndex++)
        halfWorld[sampleIndex]=M8FlowerDirtGridPosition(cell,face,halfFace,sampleIndex);
    uint axis=face>>1u,u=(axis+1u)%3u,v=(axis+2u)%3u;
    float plane=halfWorld[0][axis];
    uint coplanar=0u;bool overlaps=false;int commonWinding=0;
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        if((active&(1u<<wedge))==0u)continue;
        uint3 sites=M8FlowerL2CarrierTriangle(wedge,false);
        if(positions[sites.x][axis]!=plane || positions[sites.y][axis]!=plane ||
            positions[sites.z][axis]!=plane)continue;
        coplanar|=1u<<wedge;
        float3 directVertices[3];
        directVertices[0]=positions[sites.x];directVertices[1]=positions[sites.y];directVertices[2]=positions[sites.z];
        M8FlowerInterval area=M8FlowerSupportOrient2(float2(directVertices[0][u],directVertices[0][v]),
            float2(directVertices[1][u],directVertices[1][v]),float2(directVertices[2][u],directVertices[2][v]));
        int directWinding=area.lo>0.0?1:area.hi<0.0?-1:0;
        if(directWinding==0 || (commonWinding!=0 && directWinding!=commonWinding))
            return M8_FLOWER_COVERAGE_OVERLAP|M8_FLOWER_COVERAGE_BOUNDARY;
        commonWinding=directWinding;
        uint individual=M8FlowerSupportTriangleCoverage(directVertices,cell,face,halfFace);
        if(individual==1u)return M8_FLOWER_COVERAGE_OVERLAP|M8_FLOWER_COVERAGE_COMPLETE;
        overlaps=overlaps || individual==2u;
    }
    if(!overlaps)return 0u;
    float2 halfVertices[3],sites2d[7];
    [unroll]for(uint sampleIndex=0u;sampleIndex<3u;sampleIndex++)
        halfVertices[sampleIndex]=float2(halfWorld[sampleIndex][u],halfWorld[sampleIndex][v]);
    [unroll]for(uint site=0u;site<7u;site++)sites2d[site]=float2(positions[site][u],positions[site][v]);
    M8FlowerInterval orientation=M8FlowerSupportOrient2(halfVertices[0],halfVertices[1],halfVertices[2]);
    int winding=orientation.lo>0.0?1:orientation.hi<0.0?-1:0;
    if(winding==0)return M8_FLOWER_COVERAGE_OVERLAP|M8_FLOWER_COVERAGE_BOUNDARY;
    uint proof=M8_FLOWER_COVERAGE_OVERLAP;
    bool ownBoundary=false;
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        if((coplanar&(1u<<wedge))==0u)continue;
        uint first=1u+wedge,last=1u+(wedge+1u)%6u;
        // Outer edge, entering spoke, leaving spoke: preserve the original
        // predicate order with one clip evaluator, not three inlined copies.
        [loop]for(uint boundary=0u;boundary<3u;boundary++)
        {
            uint a=first,b=last;
            bool paired=(pairedOuterEdges&(1u<<wedge))!=0u;
            if(boundary==1u)
            {
                if((coplanar&(1u<<((wedge+5u)%6u)))!=0u)continue;
                a=0u;b=first;paired=false;
            }
            else if(boundary==2u)
            {
                if((coplanar&(1u<<((wedge+1u)%6u)))!=0u)continue;
                a=last;b=0u;paired=false;
            }
            if(M8FlowerSupportSegmentMayEnter(sites2d[a],sites2d[b],halfVertices,winding))
            {
                ownBoundary=true;
                if(!paired)proof|=M8_FLOWER_COVERAGE_BOUNDARY;
            }
        }
    }
    // A strictly interior dyadic barycentric witness. If no possible union
    // boundary enters the connected open half and this point is covered,
    // the complete closed half belongs to the finite closed triangle union.
    M8FlowerInterval coordinates[2];
    [loop]for(uint coordinate=0u;coordinate<2u;coordinate++)
        coordinates[coordinate]=M8FlowerIMul(M8FlowerIAdd(M8FlowerIAdd(
            M8FlowerIMul(M8FlowerI(halfVertices[0][coordinate],halfVertices[0][coordinate]),M8FlowerI(2.0,2.0)),
            M8FlowerI(halfVertices[1][coordinate],halfVertices[1][coordinate])),
            M8FlowerI(halfVertices[2][coordinate],halfVertices[2][coordinate])),M8FlowerI(0.25,0.25));
    M8FlowerInterval2 witness;witness.x=coordinates[0];witness.y=coordinates[1];
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
        if((coplanar&(1u<<wedge))!=0u && M8FlowerSupportContainsWitness(sites2d[0],
            sites2d[1u+wedge],sites2d[1u+(wedge+1u)%6u],witness,commonWinding))proof|=M8_FLOWER_COVERAGE_WITNESS;
    if(!ownBoundary && (proof&M8_FLOWER_COVERAGE_WITNESS)!=0u)proof|=M8_FLOWER_COVERAGE_COMPLETE;
    return proof;
}

uint M8FlowerSupportCoverageResult(uint proof)
{
    if((proof&M8_FLOWER_COVERAGE_COMPLETE)!=0u ||
        (proof&(M8_FLOWER_COVERAGE_WITNESS|M8_FLOWER_COVERAGE_BOUNDARY))==M8_FLOWER_COVERAGE_WITNESS)return 1u;
    return (proof&M8_FLOWER_COVERAGE_OVERLAP)!=0u?2u:0u;
}

uint M8FlowerSupportCarrierCoverage(float3 positions[7],uint active,int3 cell,uint face,uint halfFace)
{
    return M8FlowerSupportCoverageResult(M8FlowerSupportCarrierCoverageProof(
        positions,active,0u,cell,face,halfFace));
}

// Resolve the generated partner only. The page workgroup evaluates it through
// its SAME carrier call site, then applies PairAxes to the two actual results.
// Calling PageCarrier here would inline a second complete geometry evaluator.
bool M8FlowerSupportResolveOuterPeer(uint slot,int3 owner,uint carrier,uint wedge,
    out uint2 peerAddress,out uint canonicalWedge,out uint3 peerSites)
{
    peerAddress=0u;canonicalWedge=0u;peerSites=0u;
    uint peerWedge,peerEdge;bool reversed;
    if(!M8FlowerL2AcrossEdge(6u*carrier+wedge,1u,peerWedge,peerEdge,reversed) || !reversed)return false;
    int3 delta;uint permutation;
    if(!M8FlowerCanonicalL2WedgeOwner(peerWedge/6u,peerWedge%6u,
        delta,canonicalWedge,permutation))return false;
    int3 relative=(owner-m8FlowerSupportOrigin)+delta;
    // Being somewhere in the resident halo is not sufficient: the partner
    // must belong to this exact complete coverage stencil.
    if(any(relative<M8_FLOWER_COVERAGE_OWNER_MIN) || any(relative>M8_FLOWER_COVERAGE_OWNER_MAX))return false;
    if(any(m8FlowerSupportOrigin>2147483647-max(relative,0)) ||
        any(m8FlowerSupportOrigin<(-2147483647-1)-min(relative,0)))return false;
    int3 peerOwner=m8FlowerSupportOrigin+relative;
    uint peerSlot,local;KernelState peerState;
    if(M8FlowerReadEndpoint(slot,owner,peerOwner,peerSlot,local,peerState)!=1u)return false;
    peerAddress=uint2(peerSlot,local);
    uint3 sites=M8FlowerL2CarrierTriangle(canonicalWedge%6u,false);
    peerSites=uint3(sites[(permutation>>(2u*((peerEdge+1u)%3u)))&3u],
        sites[(permutation>>(2u*peerEdge))&3u],
        sites[(permutation>>(2u*((peerEdge+2u)%3u)))&3u]);
    return true;
}

// Independent (cell, face, half) predicates share the lanes assigned to this
// actual carrier. Atomics only accumulate proof bits; no lane can erase a
// peer's complete-coverage proof or promote a partial union early.
void M8FlowerSupportAccumulateCarrier(float3 positions[7],uint active,uint3 pairedEdges,
    int3 carrierFirst,int3 carrierLast,uint lane,uint laneCount)
{
    if(any(carrierLast<carrierFirst))return;
    uint3 size=(uint3)(carrierLast-carrierFirst+1);
    uint cells=size.x*size.y*size.z;
    [loop]for(uint item=lane;item<12u*cells;item+=laneCount)
    {
        uint index=item/12u,face=(item%12u)>>1u,halfFace=item&1u;
        int3 relative=carrierFirst+(int3)uint3(index%size.x,(index/size.x)%size.y,index/(size.x*size.y));
        uint local=(uint)(relative.x+8*(relative.y+8*relative.z)),bit=1u<<(local&31u);
        uint faceWord=face*16u+(local>>5u);
        if((m8FlowerSupportFaces[faceWord]&bit)==0u)continue;
        uint proof=M8FlowerSupportCarrierCoverageProof(positions,active,pairedEdges[face>>1u],
            m8FlowerSupportOrigin+relative,face,halfFace);
        uint word=2u*faceWord+halfFace;
        if((proof&M8_FLOWER_COVERAGE_COMPLETE)!=0u)InterlockedOr(m8FlowerSupportOffsets[word],bit);
        if((proof&M8_FLOWER_COVERAGE_OVERLAP)!=0u)InterlockedOr(m8FlowerSupportPartialHalves[word],bit);
        if((proof&M8_FLOWER_COVERAGE_WITNESS)!=0u)InterlockedOr(m8FlowerSupportWitnessHalves[word],bit);
        if((proof&M8_FLOWER_COVERAGE_BOUNDARY)!=0u)InterlockedOr(m8FlowerSupportBoundaryHalves[word],bit);
    }
}

void M8FlowerSupportReachOwnerRow(int firstX,int lastX,int y,int z)
{
    if(firstX>lastX)return;
    uint count=(uint)(lastX-firstX+1),bits=(1u<<count)-1u;
    uint index=(uint)(firstX-M8_FLOWER_COVERAGE_OWNER_MIN)+M8_FLOWER_COVERAGE_OWNER_SIDE*
        ((uint)(y-M8_FLOWER_COVERAGE_OWNER_MIN)+M8_FLOWER_COVERAGE_OWNER_SIDE*(uint)(z-M8_FLOWER_COVERAGE_OWNER_MIN));
    uint word=index>>5u,shift=index&31u;
    InterlockedOr(m8FlowerSupportCoverageOwners[word],bits<<shift);
    if(shift+count>32u)InterlockedOr(m8FlowerSupportCoverageOwners[word+1u],bits>>(32u-shift));
    InterlockedOr(m8FlowerSupportCoverageNeeded,1u);
}

// Only an actually uncovered elementary face reaches neighbor owners. Its
// integer support bound is codegenerated from the same +/-2a knot bound as
// the CPU frozen reader. X runs are six bits at most, not 180 scalar atomics.
bool M8FlowerSupportPrepareNeighborCoverage(uint lane,uint laneCount)
{
    GroupMemoryBarrierWithGroupSync();
    if(lane==0u)m8FlowerSupportCoverageNeeded=0u;
    [loop]for(uint word=lane;word<M8_FLOWER_COVERAGE_OWNER_WORDS;word+=laneCount)
        m8FlowerSupportCoverageOwners[word]=0u;
    GroupMemoryBarrierWithGroupSync();
    [loop]for(uint word=lane;word<M8_FLOWER_SUPPORT_FACE_WORDS;word+=laneCount)
    {
        uint faces=m8FlowerSupportFaces[word]&
            ~(m8FlowerSupportOffsets[2u*word]&m8FlowerSupportOffsets[2u*word+1u]);
        [loop]while(faces!=0u)
        {
            uint local=32u*(word&15u)+(uint)firstbitlow(faces);
            faces&=faces-1u;
            int3 first,last;
            M8FlowerDirtCoverageOwnerBounds(int3(local&7u,(local>>3u)&7u,local>>6u),word>>4u,first,last);
            [loop]for(int z=first.z;z<=last.z;z++)
                [loop]for(int y=first.y;y<=last.y;y++)
                {
                    if(y>=0 && y<8 && z>=0 && z<8)
                    {
                        // Central owners were already decoded for this page.
                        M8FlowerSupportReachOwnerRow(first.x,min(last.x,-1),y,z);
                        M8FlowerSupportReachOwnerRow(max(first.x,8),last.x,y,z);
                    }
                    else M8FlowerSupportReachOwnerRow(first.x,last.x,y,z);
                }
        }
    }
    GroupMemoryBarrierWithGroupSync();
    return m8FlowerSupportCoverageNeeded!=0u;
}

bool M8FlowerSupportResolveCoverageOwner(uint pageSlot,uint index,
    out uint slot,out uint local)
{
    slot=local=0u;
    if(index>=M8_FLOWER_COVERAGE_OWNER_COUNT || m8FlowerSupportCoverageNeeded==0u)return false;
    if((m8FlowerSupportCoverageOwners[index>>5u]&(1u<<(index&31u)))==0u)return false;
    int3 relative=int3(index%M8_FLOWER_COVERAGE_OWNER_SIDE,
        (index/M8_FLOWER_COVERAGE_OWNER_SIDE)%M8_FLOWER_COVERAGE_OWNER_SIDE,
        index/(M8_FLOWER_COVERAGE_OWNER_SIDE*M8_FLOWER_COVERAGE_OWNER_SIDE))+M8_FLOWER_COVERAGE_OWNER_MIN;
    if(all(relative>=0) && all(relative<=7))return false;
    if(any(m8FlowerSupportOrigin>2147483647-max(relative,0)) ||
        any(m8FlowerSupportOrigin<(-2147483647-1)-min(relative,0)))
    {InterlockedOr(m8FlowerSupportCoverageUnresolved,1u);return false;}
    int3 owner=m8FlowerSupportOrigin+relative;
    KernelState state;
    uint resident=M8FlowerReadEndpoint(pageSlot,m8FlowerSupportOrigin,owner,slot,local,state);
    if(resident==2u)InterlockedOr(m8FlowerSupportCoverageUnresolved,1u);
    return resident==1u;
}

void M8FlowerSupportFinalizeCoverage(uint lane,uint laneCount)
{
    GroupMemoryBarrierWithGroupSync();
    [loop]for(uint faceWord=lane;faceWord<M8_FLOWER_SUPPORT_FACE_WORDS;faceWord+=laneCount)
    {
        uint faces=m8FlowerSupportFaces[faceWord];
        uint a=faceWord*2u,b=a+1u;
        m8FlowerSupportOffsets[a]|=m8FlowerSupportWitnessHalves[a]&~m8FlowerSupportBoundaryHalves[a];
        m8FlowerSupportOffsets[b]|=m8FlowerSupportWitnessHalves[b]&~m8FlowerSupportBoundaryHalves[b];
        uint partialA=m8FlowerSupportPartialHalves[a]&~m8FlowerSupportOffsets[a];
        uint partialB=m8FlowerSupportPartialHalves[b]&~m8FlowerSupportOffsets[b];
        if(m8FlowerSupportCoverageUnresolved!=0u)
        {
            partialA|=faces&~m8FlowerSupportOffsets[a];
            partialB|=faces&~m8FlowerSupportOffsets[b];
        }
        m8FlowerSupportHalves[a]=faces&~(m8FlowerSupportOffsets[a]|partialA);
        m8FlowerSupportHalves[b]=faces&~(m8FlowerSupportOffsets[b]|partialB);
        // Do not accumulate this before the complete union barrier: another
        // carrier may still supply its exact boundary cancellation/witness.
        // At this point the former Partial array was only reduced by OR.
        InterlockedOr(m8FlowerSupportUnresolved,faces&(partialA|partialB));
    }
    GroupMemoryBarrierWithGroupSync();
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
        uint count = 0u;
        [loop] for (uint word = 0u; word < M8_FLOWER_SUPPORT_HALF_WORDS; ++word)
        {
            m8FlowerSupportOffsets[word] = count;
            count += countbits(m8FlowerSupportHalves[word]);
        }
        m8FlowerSupportSymbolCount = count;
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
groupshared uint m8FlowerSupportPageResult;

// Count saved the exact coverage half-masks under the same source lease.
// Reserve granted this page's receipt before this emission dispatch. No lane
// performs allocation or loses a reservation when another page is BUSY.
uint M8FlowerSupportEmitPage(M8FlowerPageBuild build,uint directSymbolCount,
    uint lane,uint laneCount)
{
    if(lane==0u)
    {
        if(m8FlowerSupportUnresolved!=0u || m8FlowerSupportOriginValid==0u ||
            !M8FlowerPageSourceUnchanged(build.Slot,build.SourceGeneration))
            m8FlowerSupportPageResult=M8_FLOWER_SUPPORT_AMBIGUOUS;
        else if(directSymbolCount>build.SymbolCapacity/16u ||
            m8FlowerSupportSymbolCount>build.SymbolCapacity/16u-directSymbolCount)
            m8FlowerSupportPageResult=M8_FLOWER_ARENA_CAPACITY;
        else m8FlowerSupportPageResult=M8_FLOWER_ARENA_OK;
    }
    GroupMemoryBarrierWithGroupSync();
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
// it or retain its batch receipt for the locked publication/abort stage.
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
