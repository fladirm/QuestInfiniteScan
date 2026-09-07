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
groupshared uint m8FlowerSupportPartialHalves[M8_FLOWER_SUPPORT_HALF_WORDS];
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
        m8FlowerSupportPartialHalves[faceWord * 2u] = 0u;
        m8FlowerSupportPartialHalves[faceWord * 2u + 1u] = 0u;
        // Before PrepareSymbols these words hold proved complete coverage;
        // the prefix calculation reuses them only after the coverage barrier.
        m8FlowerSupportOffsets[faceWord * 2u] = 0u;
        m8FlowerSupportOffsets[faceWord * 2u + 1u] = 0u;
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

bool M8FlowerSupportWedgeBounds(int3 owner,uint carrier,uint wedge,
    M8FlowerPhaseRootEvidence roots[7],out M8FlowerInterval3 minimumMaximum[3],
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
    [unroll]for(uint vertex=0u;vertex<3u;vertex++)
    {
        M8FlowerInterval3 relative;
        uint site=sites[vertex];
        if(!M8FlowerRootRelativeBounds(M8FlowerL2CarrierKnot(carrier,site),roots[site],relative))return false;
        minimumMaximum[vertex].x=M8FlowerIAdd(relative.x,translation.x);
        minimumMaximum[vertex].y=M8FlowerIAdd(relative.y,translation.y);
        minimumMaximum[vertex].z=M8FlowerIAdd(relative.z,translation.z);
    }
    M8FlowerInterval components[3];
    components[0]=M8FlowerI(min(min(minimumMaximum[0].x.lo,minimumMaximum[1].x.lo),minimumMaximum[2].x.lo),
        max(max(minimumMaximum[0].x.hi,minimumMaximum[1].x.hi),minimumMaximum[2].x.hi));
    components[1]=M8FlowerI(min(min(minimumMaximum[0].y.lo,minimumMaximum[1].y.lo),minimumMaximum[2].y.lo),
        max(max(minimumMaximum[0].y.hi,minimumMaximum[1].y.hi),minimumMaximum[2].y.hi));
    components[2]=M8FlowerI(min(min(minimumMaximum[0].z.lo,minimumMaximum[1].z.lo),minimumMaximum[2].z.lo),
        max(max(minimumMaximum[0].z.hi,minimumMaximum[1].z.hi),minimumMaximum[2].z.hi));
    [unroll]for(uint axis=0u;axis<3u;axis++)
    {
        M8FlowerInterval cells;
        if(!M8FlowerIDivPositive(components[axis],
            M8FlowerI(M8_FLOWER_LATTICE_STEP,M8_FLOWER_LATTICE_STEP),cells) ||
            !all(M8FlowerIsFinite(float2(cells.lo,cells.hi))) || cells.lo < -8.0 || cells.hi >= 15.0)return false;
        firstCell[axis]=(int)floor(cells.lo);lastCell[axis]=(int)floor(cells.hi);
    }
    return true;
}

// The convex hull of the THREE metric knot intervals is contained in this
// cover. It is not an M8 owner box. All cells THROUGH proves a veto over the
// entire represented wedge; all FULL proves no portion is vetoed. Mixed or
// nonresident covers remain unresolved rather than deleting possible matter.
uint M8FlowerSupportWedgeDual(int3 owner,uint carrier,uint wedge,
    M8FlowerPhaseRootEvidence roots[7])
{
    M8FlowerInterval3 bounds[3];int3 first,last;
    if(!M8FlowerSupportWedgeBounds(owner,carrier,wedge,roots,bounds,first,last))return 2u;
    bool full=false,through=false;
    [loop]for(int z=first.z;z<=last.z;z++)
        [loop]for(int y=first.y;y<=last.y;y++)
            [loop]for(int x=first.x;x<=last.x;x++)
            {
                uint state=M8FlowerSupportFreeCell(int3(x,y,z));
                if(state==M8_FLOWER_CELL_AMBIGUOUS)return 2u;
                if(state==M8_FLOWER_CELL_FREE)through=true;
                else full=true;
                if(full && through)return 2u;
            }
    return through?1u:0u;
}

// Same invocation in the count and emit passes; the caller's source lease
// fixes both M8/detail and dual snapshots between them.
uint M8FlowerPageCarrier(uint slot,uint local,uint carrier,float2 errors,
    out M8FlowerSymbolRecord symbol,out uint unresolved,
    out M8FlowerPhaseRootEvidence roots[7],out float3 positions[7])
{
    uint status=M8FlowerClassifyL2Carrier(slot,local,carrier,errors,symbol,unresolved,roots,positions);
    // The raw selector remains owner-local for scan/phase closure. Only the
    // generated canonical incident owner emits each complete three-knot
    // petal; ambiguity belonging solely to another owner cannot dirty this
    // page. This decision uses no normal, sampled position or residency tie.
    uint owned=0u;
    [unroll]for(uint wedge=0u;wedge<6u;wedge++)
        if(M8FlowerOwnsL2Wedge(carrier,wedge))owned|=1u<<wedge;
    unresolved&=owned;
    if(status!=1u)return status==2u && unresolved!=0u?2u:0u;
    uint active=M8FlowerDrawActiveWedgeMask(symbol)&owned;
    int3 owner=M8FlowerEndpointOwner(slot,local);
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        uint bit=1u<<wedge;if((active&bit)==0u)continue;
        uint dual=M8FlowerSupportWedgeDual(owner,carrier,wedge,roots);
        if(dual!=0u)active&=~bit;
        if(dual==2u)unresolved|=bit;
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

M8FlowerInterval M8FlowerSupportDifference(float a,float b)
{
    if(a==b)return M8FlowerI(0.0,0.0);
    return M8FlowerISub(M8FlowerI(a,a),M8FlowerI(b,b));
}

M8FlowerInterval M8FlowerSupportOrient2(float2 a,float2 b,float2 p)
{
    return M8FlowerISub(
        M8FlowerIMul(M8FlowerSupportDifference(b.x,a.x),M8FlowerSupportDifference(p.y,a.y)),
        M8FlowerIMul(M8FlowerSupportDifference(b.y,a.y),M8FlowerSupportDifference(p.x,a.x)));
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
    float2 direct[3],halfTriangle[3];
    [unroll]for(uint i=0u;i<3u;i++)
    {direct[i]=float2(directVertices[i][u],directVertices[i][v]);halfTriangle[i]=float2(halfVertices[i][u],halfVertices[i][v]);}
    M8FlowerInterval orientation=M8FlowerSupportOrient2(direct[0],direct[1],direct[2]);
    int winding=orientation.lo>0.0?1:orientation.hi<0.0?-1:0;
    if(winding==0)return 2u;
    bool covered=true;
    [unroll]for(uint edge=0u;edge<3u;edge++)
    {
        bool outside=true;
        [unroll]for(uint sampleIndex=0u;sampleIndex<3u;sampleIndex++)
        {
            M8FlowerInterval side=M8FlowerSupportOrient2(direct[edge],direct[(edge+1u)%3u],halfTriangle[sampleIndex]);
            if(winding<0)side=M8FlowerI(-side.hi,-side.lo);
            covered=covered && side.lo>=0.0;
            outside=outside && side.hi<0.0;
        }
        if(outside)return 0u;
    }
    if(covered)return 1u;
    // The other triangle's three separating axes are also required before
    // a non-containing pair may be called disjoint.
    M8FlowerInterval halfOrientation=M8FlowerSupportOrient2(halfTriangle[0],halfTriangle[1],halfTriangle[2]);
    int halfWinding=halfOrientation.lo>0.0?1:halfOrientation.hi<0.0?-1:0;
    if(halfWinding==0)return 2u;
    [unroll]for(uint edge=0u;edge<3u;edge++)
    {
        bool outside=true;
        [unroll]for(uint sampleIndex=0u;sampleIndex<3u;sampleIndex++)
        {
            M8FlowerInterval side=M8FlowerSupportOrient2(halfTriangle[edge],halfTriangle[(edge+1u)%3u],direct[sampleIndex]);
            if(halfWinding<0)side=M8FlowerI(-side.hi,-side.lo);
            outside=outside && side.hi<0.0;
        }
        if(outside)return 0u;
    }
    return 2u;
}

// Can this segment enter the OPEN half-triangle? Clip only an outward
// enclosure of possible parameters. Returning false is a proof; boundary
// contact alone is not a crossing into the represented two-dimensional area.
bool M8FlowerSupportSegmentMayEnter(float2 first,float2 last,float2 halfVertices[3],int winding)
{
    float lower=0.0,upper=1.0;
    [unroll]for(uint edge=0u;edge<3u;edge++)
    {
        M8FlowerInterval a=M8FlowerSupportOrient2(halfVertices[edge],halfVertices[(edge+1u)%3u],first);
        M8FlowerInterval b=M8FlowerSupportOrient2(halfVertices[edge],halfVertices[(edge+1u)%3u],last);
        if(winding<0){a=M8FlowerI(-a.hi,-a.lo);b=M8FlowerI(-b.hi,-b.lo);}
        // s(t) <= (1-t)*a.hi+t*b.hi on the entire segment.
        if(a.hi<=0.0 && b.hi<=0.0)return false;
        if(a.hi>0.0 && b.hi>0.0)continue;
        M8FlowerInterval numerator,denominator,limit;
        if(a.hi<=0.0)
        {
            numerator=M8FlowerI(-a.hi,-a.hi);
            denominator=M8FlowerISub(M8FlowerI(b.hi,b.hi),M8FlowerI(a.hi,a.hi));
            if(!M8FlowerIDivPositive(numerator,denominator,limit))return true;
            lower=max(lower,limit.lo);
        }
        else
        {
            numerator=M8FlowerI(a.hi,a.hi);
            denominator=M8FlowerISub(M8FlowerI(a.hi,a.hi),M8FlowerI(b.hi,b.hi));
            if(!M8FlowerIDivPositive(numerator,denominator,limit))return true;
            upper=min(upper,limit.hi);
        }
        if(lower>=upper)return false;
    }
    return lower<upper;
}

bool M8FlowerSupportContainsWitness(float2 a,float2 b,float2 c,M8FlowerInterval2 witness)
{
    M8FlowerInterval orientation=M8FlowerSupportOrient2(a,b,c);
    int winding=orientation.lo>0.0?1:orientation.hi<0.0?-1:0;
    if(winding==0)return false;
    float2 vertices[3];vertices[0]=a;vertices[1]=b;vertices[2]=c;
    [unroll]for(uint edge=0u;edge<3u;edge++)
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

// Union of the actual active coplanar fan, not six independent whole-half
// tests. The generated fan cancels a spoke iff both incident wedges exist.
// Its remaining oriented edges contain every boundary of this union even
// when unrelated triangles overlap. No summed-area assumption is required.
uint M8FlowerSupportCarrierCoverage(float3 positions[7],uint active,int3 cell,uint face,uint halfFace)
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
        if(directWinding==0 || (commonWinding!=0 && directWinding!=commonWinding))return 2u;
        commonWinding=directWinding;
        uint individual=M8FlowerSupportTriangleCoverage(directVertices,cell,face,halfFace);
        if(individual==1u)return 1u;
        overlaps=overlaps || individual==2u;
    }
    if(!overlaps)return 0u;
    float2 halfVertices[3],sites2d[7];
    [unroll]for(uint sampleIndex=0u;sampleIndex<3u;sampleIndex++)
        halfVertices[sampleIndex]=float2(halfWorld[sampleIndex][u],halfWorld[sampleIndex][v]);
    [unroll]for(uint site=0u;site<7u;site++)sites2d[site]=float2(positions[site][u],positions[site][v]);
    M8FlowerInterval orientation=M8FlowerSupportOrient2(halfVertices[0],halfVertices[1],halfVertices[2]);
    int winding=orientation.lo>0.0?1:orientation.hi<0.0?-1:0;
    if(winding==0)return 2u;
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        if((coplanar&(1u<<wedge))==0u)continue;
        uint first=1u+wedge,last=1u+(wedge+1u)%6u;
        if(M8FlowerSupportSegmentMayEnter(sites2d[first],sites2d[last],halfVertices,winding))return 2u;
        if((coplanar&(1u<<((wedge+5u)%6u)))==0u &&
            M8FlowerSupportSegmentMayEnter(sites2d[0],sites2d[first],halfVertices,winding))return 2u;
        if((coplanar&(1u<<((wedge+1u)%6u)))==0u &&
            M8FlowerSupportSegmentMayEnter(sites2d[last],sites2d[0],halfVertices,winding))return 2u;
    }
    // A strictly interior dyadic barycentric witness. If no possible union
    // boundary enters the connected open half and this point is covered,
    // the complete closed half belongs to the finite closed triangle union.
    M8FlowerInterval2 witness;
    witness.x=M8FlowerIMul(M8FlowerIAdd(M8FlowerIAdd(
        M8FlowerIMul(M8FlowerI(halfVertices[0].x,halfVertices[0].x),M8FlowerI(2.0,2.0)),
        M8FlowerI(halfVertices[1].x,halfVertices[1].x)),M8FlowerI(halfVertices[2].x,halfVertices[2].x)),
        M8FlowerI(0.25,0.25));
    witness.y=M8FlowerIMul(M8FlowerIAdd(M8FlowerIAdd(
        M8FlowerIMul(M8FlowerI(halfVertices[0].y,halfVertices[0].y),M8FlowerI(2.0,2.0)),
        M8FlowerI(halfVertices[1].y,halfVertices[1].y)),M8FlowerI(halfVertices[2].y,halfVertices[2].y)),
        M8FlowerI(0.25,0.25));
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
        if((coplanar&(1u<<wedge))!=0u && M8FlowerSupportContainsWitness(sites2d[0],
            sites2d[1u+wedge],sites2d[1u+(wedge+1u)%6u],witness))return 1u;
    return 2u;
}

// Called independently by owner lanes for actually admitted/emitted carrier
// wedges (including a genuinely unique completion). Atomics only accumulate
// proof bits; scheduling cannot erase another lane's complete-coverage proof.
void M8FlowerSupportAccumulateCarrier(int3 owner,uint carrier,M8FlowerSymbolRecord symbol,
    M8FlowerPhaseRootEvidence roots[7],float3 positions[7])
{
    uint active=M8FlowerDrawActiveWedgeMask(symbol);
    int3 carrierFirst=15,carrierLast=-8;
    bool anyBounds=false;
    [loop]for(uint wedge=0u;wedge<6u;wedge++)
    {
        if((active&(1u<<wedge))==0u)continue;
        M8FlowerInterval3 bounds[3];int3 first,last;
        if(!M8FlowerSupportWedgeBounds(owner,carrier,wedge,roots,bounds,first,last))continue;
        carrierFirst=min(carrierFirst,first);carrierLast=max(carrierLast,last);anyBounds=true;
    }
    if(!anyBounds)return;
    // A boundary face belongs to either adjacent U. Include the lower
    // neighbour at an exact integer coordinate; bounds are metric-derived.
    carrierFirst=max(carrierFirst-1,0);carrierLast=min(carrierLast,7);
    [loop]for(uint face=0u;face<6u;face++)
    {
        [loop]for(int z=carrierFirst.z;z<=carrierLast.z;z++)
            [loop]for(int y=carrierFirst.y;y<=carrierLast.y;y++)
                [loop]for(int x=carrierFirst.x;x<=carrierLast.x;x++)
                    {
                        uint local=(uint)(x+8*(y+8*z)),bit=1u<<(local&31u);
                        uint faceWord=face*16u+(local>>5u);
                        if((m8FlowerSupportFaces[faceWord]&bit)==0u)continue;
                        int3 cell=m8FlowerSupportOrigin+int3(x,y,z);
                        [unroll]for(uint halfFace=0u;halfFace<2u;halfFace++)
                        {
                            uint coverage=M8FlowerSupportCarrierCoverage(positions,active,cell,face,halfFace);
                            uint word=2u*faceWord+halfFace;
                            if(coverage==1u)InterlockedOr(m8FlowerSupportOffsets[word],bit);
                            else if(coverage==2u)InterlockedOr(m8FlowerSupportPartialHalves[word],bit);
                        }
                    }
    }
}

void M8FlowerSupportFinalizeCoverage(uint lane,uint laneCount)
{
    GroupMemoryBarrierWithGroupSync();
    [loop]for(uint faceWord=lane;faceWord<M8_FLOWER_SUPPORT_FACE_WORDS;faceWord+=laneCount)
    {
        uint faces=m8FlowerSupportFaces[faceWord];
        uint a=faceWord*2u,b=a+1u;
        uint partialA=m8FlowerSupportPartialHalves[a]&~m8FlowerSupportOffsets[a];
        uint partialB=m8FlowerSupportPartialHalves[b]&~m8FlowerSupportOffsets[b];
        m8FlowerSupportHalves[a]=faces&~(m8FlowerSupportOffsets[a]|partialA);
        m8FlowerSupportHalves[b]=faces&~(m8FlowerSupportOffsets[b]|partialB);
        m8FlowerSupportPartial[faceWord]=faces&(partialA|partialB);
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
