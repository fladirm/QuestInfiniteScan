#ifndef GENESIS_MERKABA_FLOWER_COMMIT_INCLUDED
#define GENESIS_MERKABA_FLOWER_COMMIT_INCLUDED

#include "MerkabaObservationBins.hlsl"
#include "MerkabaObservationReduction.hlsl"
#define M8_FLOWER_SIDECAR_WRITE
#include "MerkabaFlowerSidecar.hlsl"

// Same immutable record backing as Emit. No scratch eligibility writes may
// alias this view; own eligibility is checked at its existing plane reader.
StructuredBuffer<M8ObservationRecord> _M8ObservationRecordsRead;

// The low two bits retain the existing root/L1/L2/skin stage. These two
// transaction states reuse its existing held-observation Finalize barrier.
// The invalidation cut has its own persistent phase, not two bits borrowed
// from the refinement stage. The refinement stage is transient - it is the
// barrier index inside one snapshot graph and resets with every snapshot -
// whereas an owed cut is a property of the world and must outlive the frame
// that discovered it. GATHER retires all local epochs and required gather or
// load work; PEER retires every successful exact peer ACK.
#define M8_FLOWER_INVALIDATION_GATHER_PHASE 1u
#define M8_FLOWER_INVALIDATION_PEER_PHASE 2u
#define M8_FLOWER_INVALIDATION_PHASE_MASK 3u
#define M8_FLOWER_INVALIDATION_WRITE_FAILED (1u<<8u)

groupshared uint m8FlowerOwnerSource[512];
groupshared uint m8FlowerOwnerPrecision[512];
groupshared uint m8FlowerRootPresence[512];
// Six directed R1 relations, each with two algebraic signs. Each five-bit
// field is 0 (no witness) or 1+sector; R1 has 24 generated order sectors.
// x.bit31 reuses a non-sector bit for this owner's rejection state.
groupshared uint2 m8FlowerR1Witness[512];
groupshared uint m8FlowerDualReady;
groupshared uint m8FlowerR1Changed;

#define M8_FLOWER_OWNER_REJECTED (1u<<31u)
bool M8FlowerOwnerIsRejected(uint local)
{
    return (m8FlowerR1Witness[local].x&M8_FLOWER_OWNER_REJECTED)!=0u;
}
void M8FlowerRejectOwner(uint local)
{
    // Only this owner's canonical lane writes this field, after the same
    // reduction barriers as before. No extra shared bank or atomic is needed.
    m8FlowerR1Witness[local].x|=M8_FLOWER_OWNER_REJECTED;
}

bool M8FlowerMeasurement(uint sourcePixel, int3 owner, out uint plane,
    out float3 worldPosition)
{
    plane = 0u;
    worldPosition = 0.0.xxx;
    if (sourcePixel >= gsDepthTexSize.x * gsDepthTexSize.y ||
        _M8PlaneErrorBounds.w != 1.0 ||
        !all(M8FlowerIsFinite(_M8PlaneErrorBounds)) ||
        any(_M8PlaneErrorBounds.xy < 0.0) ||
        !M8FlowerIsFinite(_MerkabaMaxUpdateDistance) ||
        _MerkabaMaxUpdateDistance <= 0.0) return false;
    uint2 pixel = uint2(sourcePixel % gsDepthTexSize.x,
        sourcePixel / gsDepthTexSize.x);
    float depth = gsDepthTex.Load(int3(pixel,0));
    float4 measured = gsDepthNormalTex.Load(int3(pixel,0));
    if (!(depth > 0.0 && depth < 1.0) || measured.w != 1.0 ||
        !all(M8FlowerIsFinite(measured.xyz))) return false;
    precise float2 uv = (float2(pixel)+0.5)/float2(gsDepthTexSize);
    worldPosition = gsDepthNDCtoWorld(float3(uv,depth));
    if (!M8ObservationContains(worldPosition,gsDepthEyePos())) return false;
    precise float3 gridPosition = mul(_MerkabaWorldToGrid,
        float4(worldPosition,1.0)).xyz;
    // A plane normal is a covector. Even for the rigid scan frame use the
    // canonical inverse-transpose transport, not position-vector transport.
    precise float3 gridNormal = mul(transpose((float3x3)_MerkabaGridToWorld),
        measured.xyz);
    precise float3 normalSquare = gridNormal*gridNormal;
    precise float normalSquareXY = normalSquare.x+normalSquare.y;
    precise float normalLength = sqrt(normalSquareXY+normalSquare.z);
    if (!(normalLength > 0.0) || !M8FlowerIsFinite(normalLength)) return false;
    // The persisted offset is a metric signed distance. Compute it from
    // the unit normal that plane packing will encode, not from a scaled
    // world-to-grid normal (including ordinary binary32 rotation drift).
    gridNormal /= normalLength;
    precise float3 relative = gridPosition-float3(owner)*M8_FLOWER_LATTICE_STEP;
    precise float3 terms = relative*gridNormal;
    precise float xy = terms.x+terms.y;
    precise float offset = xy+terms.z;
    return M8FlowerTryPackPlane(0u,gridNormal,offset,plane);
}

bool M8FlowerInputPlane(M8ObservationRecord record, uint slot,
    out uint plane, out float3 normal, out float offset)
{
    plane = 0u;
    normal = 0.0.xxx;
    offset = 0.0;
    uint local = record.TileAndKernel & 511u;
    if ((record.TileAndKernel >> 9u) != slot) return false;
    float3 world;
    if (!M8FlowerMeasurement(record.SourcePixel,
            M8GlobalKernelCoord(slot,local),plane,world)) return false;
    M8FlowerUnpackPlane(plane,normal,offset);
    return true;
}

bool M8FlowerOwnPlaneEligible(uint slot,uint local,uint plane,
    float normalError,float offsetError)
{
    KernelState previous=M8LoadKernelState(slot,local);
    return (previous.flags&M8_FLOWER_OCCUPIED_FLAG)==0u ||
        M8FlowerCompatibleCarrier(previous.flags,plane,normalError,offsetError);
}

// Decode inverse child incidence from the reached R1 directions. Metric
// roots are evaluated once per reached loop, not once per theoretical flag.
// Generated peer masks share a child construction, not a root owner cell.
bool M8FlowerR1FlagWitness(uint2 witness, uint plane,
    float normalError, float offsetError)
{
    uint axisMask = 0u;
    [unroll] for (uint direction=0u; direction<6u; direction++)
        if ((((witness.x|witness.y)>>(5u*direction))&31u)!=0u)
            axisMask |= 1u<<(direction>>1u);
    if (countbits(axisMask)<2u) return false;
    float3 normal;
    float offset;
    M8FlowerUnpackPlane(plane,normal,offset);
    uint3 pending=0u,admitted=0u;
    [unroll]for(uint direction=0u;direction<6u;direction++)
        if((((witness.x|witness.y)>>(5u*direction))&31u)!=0u)
            pending|=M8FlowerR1WitnessDirectionsAt(direction).xyz;
    [loop]for(uint word=0u;word<3u;word++)
    {
        [loop]while(pending[word]!=0u)
        {
            uint bit=(uint)firstbitlow(pending[word]),index=word*32u+bit;
            pending[word]&=pending[word]-1u;
            uint4 row=M8FlowerR1WitnessLoopsAt(index);
            int3 junction=asint(row.xyz);
            uint level=row.w&3u,direction=row.w>>2u,lineClass=direction>>1u;
            precise float3 relative=float3(junction)*
                (M8_FLOWER_LATTICE_STEP*(level==0u?0.5:0.25));
            M8FlowerInterval3 abc;
            if(!M8FlowerPlaneLoopIntervals(normal,offset,relative,
                M8FlowerGeometryLoopRadiusAt(level*13u+lineClass),lineClass,
                normalError,offsetError,level,junction,abc))continue;
            bool certain=false;
            [loop]for(uint sign=0u;sign<2u;sign++)
            {
                uint expected=(witness[sign]>>(5u*direction))&31u;
                if(expected==0u)continue;
                uint tag,classification;M8FlowerInterval2 root;
                if(M8FlowerClassifyPlaneRoot(level,lineClass,(direction&1u)!=0u,
                    sign!=0u,normal,offset,junction,abc,tag,root,classification) &&
                    ((tag>>8u)&31u)==expected-1u)certain=true;
            }
            if(!certain)continue;
            if(any((M8FlowerR1WitnessPeersAt(index).xyz&admitted)!=0u))return true;
            admitted[word]|=1u<<bit;
        }
    }
    return false;
}

void M8FlowerUnmarkR1Active(uint slot, uint local)
{
    uint bit=1u << (local & 31u), previous;
    InterlockedAnd(_M8TileBits[M8TileWordIndex(slot,local >> 5u)].y,~bit,previous);
    if ((previous & bit) != 0u)
    {
        uint ignored;
        InterlockedAdd(_M8TileRecords[M8TileMetaIndex(slot)].w,0xffffffffu,ignored);
    }
}

bool M8FlowerParentStructureChanged(uint previous,uint next)
{
    uint occupied=(previous^next)&M8_FLOWER_OCCUPIED_FLAG;
    if(occupied!=0u)return true;
    if((previous&M8_FLOWER_OCCUPIED_FLAG)==0u)return false;
    if(!M8FlowerHasPlane(previous)||!M8FlowerHasPlane(next) ||
        M8FlowerPlaneFreeSide(previous)!=M8FlowerPlaneFreeSide(next))return true;
    float4 planes[2];
    [loop]for(uint snapshot=0u;snapshot<2u;snapshot++)
    {
        float3 normal;float offset;
        M8FlowerUnpackPlane(snapshot==0u?previous:next,normal,offset);
        planes[snapshot]=float4(normal,offset);
    }
    if(all(planes[0]==planes[1]))return false;
    float normalError=M8FlowerNext(_M8PlaneErrorBounds.x+M8_FLOWER_NORMAL_QUANTIZATION_UPPER);
    float offsetError=M8FlowerNext(_M8PlaneErrorBounds.y+M8_FLOWER_OFFSET_QUANTIZATION_UPPER);
    [loop]for(uint direction=0u;direction<26u;direction++)
    {
        uint lineClass=(uint)M8FlowerDirectionAt(direction).w;
        float3 relative=float3(M8FlowerDirectionAt(direction).xyz)*(M8_FLOWER_LATTICE_STEP*0.5);
        M8FlowerInterval3 abc[2];
        bool bounded=true;
        [loop]for(uint snapshot=0u;snapshot<2u;snapshot++)
        {
            if(!M8FlowerPlaneLoopIntervals(planes[snapshot].xyz,planes[snapshot].w,relative,
                M8FlowerGeometryLoopRadiusAt(lineClass),lineClass,normalError,offsetError,
                0u,M8FlowerDirectionAt(direction).xyz,abc[snapshot]))
            {bounded=false;break;}
        }
        if(!bounded)continue;
        [loop]for(uint sign=0u;sign<2u;sign++)
        {
            uint2 tags=0u.xx,classes=0u.xx;
            bool2 certain=bool2(false,false);
            // Keep before -> after for each sign at one exact classifier
            // callsite. Both complete metric enclosures remain unchanged.
            [loop]for(uint snapshot=0u;snapshot<2u;snapshot++)
            {
                uint tag,classification;M8FlowerInterval2 root;
                certain[snapshot]=M8FlowerClassifyPlaneRoot(0u,lineClass,false,sign!=0u,
                    planes[snapshot].xyz,planes[snapshot].w,M8FlowerDirectionAt(direction).xyz,
                    abc[snapshot],tag,root,classification);
                tags[snapshot]=tag;classes[snapshot]=classification;
            }
            if(all(certain)&&((tags.x^tags.y)&0x1f80u)!=0u)return true;
            if((certain.x&&classes.y==M8_FLOWER_ROOT_IMPOSSIBLE)||
                (certain.y&&classes.x==M8_FLOWER_ROOT_IMPOSSIBLE))return true;
        }
    }
    // An unresolved root is not proof of a structural change. Its consumer
    // must re-evaluate it against this carrier; compatible refinements keep
    // the epoch and never discard all fine data merely for new plane bits.
    return false;
}

bool M8FlowerBeginR1FineInvalidation(uint slot,uint local,bool through,bool structural)
{
    uint ownerRef=M8FlowerFindOwner(slot,local,_M8TileRecords[M8TileRuntimeIndex(slot)].w);
    if(ownerRef==0u)return true;
    // The collective capture below has already checked every source owner
    // before the FIRST M8 write. This call only marks that captured receipt.
    uint2 receipt=M8FlowerInvalidationReceipt(ownerRef);
    uint roots;
    if(receipt.x!=_M8ObservationToken ||
        M8FlowerCaptureInvalidation(ownerRef,_M8ObservationToken,through,structural,
            _M8DualPublishingGeneration,_M8DualRetiredGeneration,roots)!=M8_FLOWER_ARENA_OK)
    {
        M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return false;
    }
    InterlockedOr(_M8Counters[M8_COUNTER_INVALIDATION_OWED],
        M8_FLOWER_INVALIDATION_GATHER_PHASE);
    return true;
}

// One publication point for "this tile's direct evidence moved in this
// snapshot", shared by both commit exits.
//
// The refinement cursor says how far this tile has refined AGAINST THE PLANE
// IT HELD. New direct evidence changed that plane, so the rest of the walk
// would refine against evidence the world no longer holds. Reset it: the
// records keep their epoch, and re-walking recommits identical rows where
// nothing moved and tighter ones where it did. A structural change resets the
// epoch as well, through M8FlowerInvalidationChanged; this is the smaller case
// M8FlowerParentStructureChanged deliberately calls compatible.
//
// This is also why the cursor may live in the world at all: it is invalidated
// by what invalidates the thing it refined, not by every camera frame that
// happens to look at this tile.
void M8FlowerPublishR1Change(uint slot)
{
    if (m8FlowerR1Changed == 0u) return;
    InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK],
        M8_OBSERVATION_CHANGED_R1);
    M8FlowerStoreTileRefinementCursor(slot,
        _M8TileRecords[M8TileRuntimeIndex(slot)].w,_M8ObservationToken,0u);
}

bool M8FlowerStoreR1(uint slot, uint local, KernelState before, uint4 value)
{
    if (all(value == uint4(asuint(before.evidence),before.packedColor,
            before.colorConfidence,before.flags))) return true;
    bool structural=M8FlowerParentStructureChanged(before.flags,value.w);
    if(structural && !M8FlowerBeginR1FineInvalidation(slot,local,false,true))return false;
    // Both the receipt and pending read guard precede occupancy counters,
    // flags and the packed plane. Epoch invalidation follows in THIS same
    // submission's Drain, never after a peer-load/gather retry.
    if(structural)
        InterlockedOr(_M8TileBits[M8TileWordIndex(slot,local>>5u)].w,
            1u<<(local&31u));
    KernelState after = before;
    UpdateOccupancy(slot,local,after,asint(value.x)-before.evidence);
    after.evidence = asint(value.x);
    after.packedColor = value.y;
    after.colorConfidence = value.z;
    after.flags = value.w;
    M8StoreKernelState(slot,local,after);
    if (M8FlowerHasPlane(after.flags) || (after.flags & M8_FLOWER_OCCUPIED_FLAG) != 0u)
        M8MarkR1Active(slot,local);
    else M8FlowerUnmarkR1Active(slot,local);
    M8MarkTileDirty(slot);
    InterlockedOr(m8FlowerR1Changed,1u);
    return true;
}

// This branch applies certified free volume to measured R1 evidence. The
// excavation boundary supplies DIRT during derived page evaluation, never a
// measured plane here. Evidence changes once per immutable observation.
void M8FlowerApplyDualVeto(uint slot, uint local, uint block, uint child, uint tile)
{
    uint word=M8TileWordIndex(slot,local >> 5u), bit=1u << (local & 31u);
    if ((_M8TileBits[word].y & bit) == 0u || (_M8TileBits[word].z & bit) != 0u) return;
    int3 owner=M8GlobalKernelCoord(slot,local);
    float3 world=mul(_MerkabaGridToWorld,float4(float3(owner)*M8_FLOWER_LATTICE_STEP,1.0)).xyz;
    if (!M8ObservationContains(world,gsDepthEyePos())) return;
    if (M8DualReadKernelAt(block,child,tile,local,true,true) != M8_DUAL_THROUGH) return;
    KernelState before=M8LoadKernelState(slot,local);
    if (!M8FlowerHasPlane(before.flags) && (before.flags & M8_FLOWER_OCCUPIED_FLAG) == 0u)
    {
        M8FlowerUnmarkR1Active(slot,local);
        return;
    }
    // Full-support THROUGH invalidates dependent fine state even while a
    // stable R1 remains above OFF. Direct endpoints were excluded above.
    if(!M8FlowerBeginR1FineInvalidation(slot,local,true,false))return;
    uint4 next=M8FlowerContradictR1(uint4(asuint(before.evidence),before.packedColor,
        before.colorConfidence,before.flags));
    if(!M8FlowerStoreR1(slot,local,before,next))return;
    if (asint(next.x) < before.evidence)
        M8CounterIncrement(M8_COUNTER_THROUGH_EVIDENCE_DECREMENTS);
    if ((before.flags & M8_FLOWER_OCCUPIED_FLAG) != 0u &&
        (next.w & M8_FLOWER_OCCUPIED_FLAG) == 0u)
        M8CounterIncrement(M8_COUNTER_THROUGH_OCCUPIED_TO_FREE);
}

// One workgroup owns every positive write in this tile. The four plane
// coordinates and then the twelve R1 root buckets are reduced over the SAME
// frozen records. Atomic arrival order cannot select a conflicting sheet.
[numthreads(128,1,1)]
void FlowerCommit(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    // UNRESOLVED_OBSERVATION_TILES is telemetry, not a gate. Every tile below
    // already proves its own dependencies locally: the structural triple
    // (runtime.w, tileMeta, ChunkTileRefs), then m8FlowerDualReady over this
    // tile's own endpoints, then the all-or-no-M8 invalidation preflight. Each
    // of those failure paths increments the counter, so reading it here let one
    // unresolved tile - or one dual block that merely asked storage to retry -
    // veto every other tile that had proved itself. Readiness is local.
    if (_M8Counters[M8_COUNTER_OBSERVATION_COMPLETED] != 0u ||
        _M8Counters[M8_COUNTER_OBSERVATION_FAILURE] != 0u ||
        _M8Counters[M8_COUNTER_UNRESOLVED_SURFACE_TILES] != 0u) return;
    if (group.x >= min(_M8Counters[M8_COUNTER_TOUCHED_TILE_COUNT],
            MERKABA_M8_PHYSICAL_TILE_CAPACITY)) return;
    uint slot = _M8TouchedTileQueueRead[group.x];
    if (slot >= _M8ObservationHotSlotCount) return;
    uint4 bin = _M8ObservationTileBinsRead[slot];
    if (bin.x != _M8ObservationToken) return;
    if (bin.y != 0u && (bin.w != bin.y || bin.z >= _M8ObservationRecordCapacity ||
        bin.y > _M8ObservationRecordCapacity-bin.z))
    {
        if (lane == 0u) M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        return;
    }
    // Runtime.x is the once-only R1 stamp. TileBits.w is reserved for the
    // 512 pending structural owner invalidations consumed by the fine drain.
    // Keep the stamp across retries; slot installation initializes it to 0.
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    if (runtime.x == _M8ObservationToken) return;
    uint4 tileMeta=_M8TileRecords[M8TileMetaIndex(slot)];
    if(runtime.w==0u || tileMeta.x>=MERKABA_M8_CHUNK_CAPACITY || tileMeta.y>=64u ||
        _M8ChunkTileRefsRead[tileMeta.x*64u+tileMeta.y]!=slot+1u)
    {
        if(lane==0u)
        {
            M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
            M8CounterIncrement(M8_COUNTER_UNRESOLVED_OBSERVATION_TILES);
        }
        return;
    }
    uint2 chunkOwner=M8LoadChunkOwnerRead(tileMeta.x);
    if (lane == 0u)
    {
        m8FlowerDualReady=1u;
        m8FlowerR1Changed=0u;
    }
    GroupMemoryBarrierWithGroupSync();
    if (lane < 16u)
    {
        uint endpoints=_M8TileBits[M8TileWordIndex(slot,lane)].z;
        [loop] while (endpoints != 0u)
        {
            uint bit=(uint)firstbitlow(endpoints);
            endpoints &= endpoints-1u;
            if (M8DualReadKernelAt(chunkOwner.x,chunkOwner.y,tileMeta.y,lane*32u+bit,
                true,true) != M8_DUAL_FULL)
                InterlockedAnd(m8FlowerDualReady,0u);
        }
    }
    GroupMemoryBarrierWithGroupSync();
    if (m8FlowerDualReady == 0u)
    {
        if (lane == 0u) M8CounterIncrement(M8_COUNTER_UNRESOLVED_OBSERVATION_TILES);
        return;
    }
    // All-or-no-M8 preflight: a BUSY old reader must not let some owners
    // decrement and then replay those decrements when this tile retries.
    // Capture existing phase presence before any structural store or clear.
    [loop]for(uint local=lane;local<512u;local+=128u)
    {
        uint ownerRef=M8FlowerFindOwner(slot,local,runtime.w);
        if(ownerRef==0u)continue;
        uint roots;
        uint status=M8FlowerCaptureInvalidation(ownerRef,_M8ObservationToken,false,false,
            _M8DualPublishingGeneration,_M8DualRetiredGeneration,roots);
        if(status!=M8_FLOWER_ARENA_OK)
        {
            InterlockedAnd(m8FlowerDualReady,0u);
            if(status!=M8_FLOWER_ARENA_BUSY)
                M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);
        }
    }
    DeviceMemoryBarrierWithGroupSync();
    if(m8FlowerDualReady==0u)
    {
        if(lane==0u && _M8Counters[M8_COUNTER_OBSERVATION_FAILURE]==0u)
        {
            M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
            M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
            // No other tile may advance fine evidence against a parent
            // whose once-only R1 commit is still waiting for its read lease.
            // This is the one cross-tile veto the drain still honours, and it
            // is a transient write conflict, not the world being UNKNOWN.
            M8CounterIncrement(M8_COUNTER_FINE_LEASE_BUSY);
            M8CounterIncrement(M8_COUNTER_UNRESOLVED_OBSERVATION_TILES);
        }
        return;
    }
    if (bin.y == 0u)
    {
        [loop] for (uint local=lane; local<512u; local+=128u)
            M8FlowerApplyDualVeto(slot,local,chunkOwner.x,chunkOwner.y,tileMeta.y);
        DeviceMemoryBarrierWithGroupSync();
        if (lane == 0u)
        {
            M8FlowerPublishR1Change(slot);
            _M8TileRecords[M8TileRuntimeIndex(slot)].x=_M8ObservationToken;
        }
        return;
    }
    M8FlowerCacheTileHalo(slot,lane,false,true);
    for (uint local = lane; local < 512u; local += 128u)
    {
        m8FlowerOwnerSource[local] = 0xffffffffu;
        m8FlowerOwnerPrecision[local] = 0xffffffffu;
        m8FlowerR1Witness[local] = 0u.xx;
    }
    GroupMemoryBarrierWithGroupSync();
    float normalError = M8FlowerNext(_M8PlaneErrorBounds.x+
        M8_FLOWER_NORMAL_QUANTIZATION_UPPER);
    float offsetError = M8FlowerNext(_M8PlaneErrorBounds.y+
        M8_FLOWER_OFFSET_QUANTIZATION_UPPER);
    // Every strict endpoint excludes negative mutation even when its owner
    // is occupied by an incompatible sheet. Eligibility is derived from the
    // same immutable plane at each existing own reader, never a mutable tag.
    [loop] for (uint index = lane; index < bin.y; index += 128u)
    {
        M8ObservationRecord record = _M8ObservationRecordsRead[bin.z+index];
        uint plane;
        float3 normal;
        float offset;
        if (M8FlowerInputPlane(record,slot,plane,normal,offset))
        {
            uint local = record.TileAndKernel & 511u;
            InterlockedOr(_M8TileBits[M8TileWordIndex(slot,local >> 5u)].z,
                1u << (local & 31u));
        }
    }
    DeviceMemoryBarrierWithGroupSync();
    [loop] for (uint component = 0u; component < 4u; ++component)
    {
        for (uint local = lane; local < 512u; local += 128u)
            M8FlowerResetObservationBucket(local);
        GroupMemoryBarrierWithGroupSync();
        [loop] for (uint index = lane; index < bin.y; index += 128u)
        {
            M8ObservationRecord record = _M8ObservationRecordsRead[bin.z+index];
            uint plane;
            float3 normal;
            float offset;
            if (!M8FlowerInputPlane(record,slot,plane,normal,offset)) continue;
            uint local = record.TileAndKernel & 511u;
            if(!M8FlowerOwnPlaneEligible(slot,local,plane,normalError,offsetError))continue;
            // The literal four-component loop must never form normal[3].
            // Unity's Vulkan front-end checks both sides of a conditional
            // during unrolling, even when that branch selects the offset.
            float centre = float4(normal,offset)[component];
            M8FlowerInterval interval = M8FlowerCenterRadius(centre,
                component == 3u ? offsetError : normalError);
            InterlockedMax(m8FlowerReductionBank[1024u+local],M8FlowerOrderedFloat(interval.lo));
            InterlockedMin(m8FlowerReductionBank[1536u+local],M8FlowerOrderedFloat(interval.hi));
            uint freeSide = plane & M8_FLOWER_PLANE_FREE_SIDE;
            InterlockedAnd(m8FlowerReductionBank[0u+local],freeSide);
            InterlockedOr(m8FlowerReductionBank[512u+local],freeSide);
            InterlockedMin(m8FlowerOwnerSource[local],record.SourcePixel);
        }
        GroupMemoryBarrierWithGroupSync();
        for (uint local = lane; local < 512u; local += 128u)
            if (m8FlowerOwnerSource[local] != 0xffffffffu &&
                (m8FlowerReductionBank[1024u+local] > m8FlowerReductionBank[1536u+local] ||
                 m8FlowerReductionBank[0u+local] != m8FlowerReductionBank[512u+local]))
                M8FlowerRejectOwner(local);
        GroupMemoryBarrierWithGroupSync();
    }
    // SourcePixel uses only eighteen bits. Its owner-local scratch word can
    // retain the unanimously intersected free-side orientation through the
    // root passes without another groupshared allocation.
    for (uint sideLocal=lane; sideLocal<512u; sideLocal+=128u)
        if (m8FlowerOwnerSource[sideLocal]!=0xffffffffu)
            m8FlowerOwnerSource[sideLocal] |=
                m8FlowerReductionBank[0u+sideLocal]&M8_FLOWER_PLANE_FREE_SIDE;
    GroupMemoryBarrierWithGroupSync();
    [loop] for (uint relation = 0u; relation < 12u; ++relation)
    {
        uint direction = relation >> 1u;
        uint lineClass = (uint)M8FlowerDirectionAt(direction).w;
        bool plus = (relation & 1u) != 0u;
        precise float3 relative = float3(M8FlowerDirectionAt(direction).xyz)*
            (M8_FLOWER_LATTICE_STEP*0.5);
        for (uint local = lane; local < 512u; local += 128u)
        {
            M8FlowerResetObservationBucket(local);
            m8FlowerRootPresence[local] = 0u;
        }
        GroupMemoryBarrierWithGroupSync();
        // One callsite, three ordered passes: own intersection -> own
        // representative -> the fixed peer intersection. Every old barrier
        // stays at the same evidence boundary. Peers read immutable identity
        // words and do not inherit this owner's positive eligibility test.
        uint ownTags[4];
        uint4 ownBounds[4];
        uint ownCertain=0u;
        int3 step=M8FlowerDirectionAt(direction).xyz;
        [loop] for (uint rootPass=0u; rootPass<3u; rootPass++)
        {
            bool peerPass=rootPass==2u;
            [loop] for (uint span=0u; span<(peerPass?2u:1u); span++)
            {
                uint sourceSlot=slot,ignoredLocal=0u;
                uint4 sourceBin=bin;
                if (peerPass)
                {
                    if (span!=0u && !M8FlowerHaloKernel(int3(4,4,4)+8*step,
                            sourceSlot,ignoredLocal,true)) continue;
                    sourceBin=_M8ObservationTileBinsRead[sourceSlot];
                    if (sourceBin.x!=_M8ObservationToken || sourceBin.y==0u ||
                        sourceBin.w!=sourceBin.y || sourceBin.z>=_M8ObservationRecordCapacity ||
                        sourceBin.y>_M8ObservationRecordCapacity-sourceBin.z) continue;
                }
                [loop] for (uint index=lane; index<sourceBin.y; index+=128u)
                {
                    M8ObservationRecord record;
                    if (peerPass)
                    {
                        record.TileAndKernel=_M8ObservationRecordsRead[sourceBin.z+index].TileAndKernel;
                        record.SourcePixel=_M8ObservationRecordsRead[sourceBin.z+index].SourcePixel;
                        record.SymbolTag=0u;
                        record.PrecisionKey=0u;
                    }
                    else
                    {
                        record=_M8ObservationRecordsRead[sourceBin.z+index];
                    }
                    uint local=record.TileAndKernel&511u;
                    uint representative=0u;
                    if (peerPass)
                    {
                        int3 q=int3(local&7u,(local>>3u)&7u,local>>6u);
                        if (span!=0u) q+=8*step;
                        int3 k=q-step;
                        if (any(k<0) || any(k>7)) continue;
                        local=(uint)k.x+8u*((uint)k.y+8u*(uint)k.z);
                        representative=m8FlowerReductionBank[3072u+local];
                        if (representative==0xffffffffu) continue;
                    }
                    if (M8FlowerOwnerIsRejected(local)) continue;
                    uint plane;float3 normal;float offset;
                    if (!M8FlowerInputPlane(record,sourceSlot,plane,normal,offset))
                    {
                        if (peerPass) InterlockedOr(m8FlowerRootPresence[local],8u);
                        continue;
                    }
                    if(!peerPass && !M8FlowerOwnPlaneEligible(slot,local,plane,
                        normalError,offsetError))continue;
                    if (peerPass && (plane&M8_FLOWER_PLANE_FREE_SIDE)!=
                            (m8FlowerOwnerSource[local]&M8_FLOWER_PLANE_FREE_SIDE))
                    {
                        InterlockedOr(m8FlowerRootPresence[local],8u);
                        continue;
                    }
                    M8FlowerInterval3 abc;
                    if (!M8FlowerPlaneLoopIntervals(normal,offset,peerPass?-relative:relative,
                            M8_FLOWER_R1_RADIUS,lineClass,normalError,offsetError,
                            0u,peerPass?-step:step,abc))
                    {
                        if (peerPass) InterlockedOr(m8FlowerRootPresence[local],8u);
                        continue;
                    }
                    uint tag,classification;M8FlowerInterval2 root;
                    if (M8FlowerClassifyPlaneRoot(0u,lineClass,(direction&1u)!=0u,plus,
                            normal,offset,peerPass?-step:step,abc,tag,root,classification))
                    {
                        if (rootPass==1u)
                            M8FlowerSelectRootRepresentative(local,tag,classification,root,record.SourcePixel);
                        else
                        {
                            uint independent=peerPass && record.SourcePixel!=
                                (representative&M8_FLOWER_OBSERVATION_PIXEL_MASK)?4u:0u;
                            InterlockedOr(m8FlowerRootPresence[local],2u|independent);
                            M8FlowerIntersectRoot(local,tag,root);
                        }
                    }
                    else if (rootPass!=1u)
                    {
                        if (classification==M8_FLOWER_ROOT_IMPOSSIBLE)
                            InterlockedOr(m8FlowerRootPresence[local],1u);
                        else if (peerPass) InterlockedOr(m8FlowerRootPresence[local],8u);
                    }
                }
            }
            GroupMemoryBarrierWithGroupSync();
            if (rootPass==0u)
            {
                for (uint local=lane; local<512u; local+=128u)
                    if (m8FlowerRootPresence[local]==3u ||
                        (m8FlowerReductionBank[0u+local]!=0xffffffffu &&
                         !M8FlowerRootBucketCertain(local)))
                        M8FlowerRejectOwner(local);
                GroupMemoryBarrierWithGroupSync();
            }
            else if (rootPass==1u)
            {
                for (uint local=lane; local<512u; local+=128u)
                    m8FlowerOwnerPrecision[local]=min(m8FlowerOwnerPrecision[local],
                        m8FlowerReductionBank[3072u+local]);
                GroupMemoryBarrierWithGroupSync();
                // Four compact own receipts survive only the peer pass.
                // Peer conflict cannot reject the owner's independent seed.
                [loop] for (uint owned=0u; owned<4u; owned++)
                {
                    uint local=lane+128u*owned;
                    M8FlowerInterval2 root;
                    bool certain=M8FlowerReadRootIntersection(local,ownTags[owned],root);
                    ownBounds[owned]=uint4(m8FlowerReductionBank[1024u+local],m8FlowerReductionBank[1536u+local],
                        m8FlowerReductionBank[2048u+local],m8FlowerReductionBank[2560u+local]);
                    if (!M8FlowerOwnerIsRejected(local) &&
                        m8FlowerReductionBank[3072u+local]!=0xffffffffu && certain)
                        ownCertain|=1u<<owned;
                    // Keep the representative until peer identity comparison.
                    m8FlowerReductionBank[0u+local]=0xffffffffu;
                    m8FlowerReductionBank[512u+local]=0u;
                    m8FlowerReductionBank[1024u+local]=0u;
                    m8FlowerReductionBank[1536u+local]=0xffffffffu;
                    m8FlowerReductionBank[2048u+local]=0u;
                    m8FlowerReductionBank[2560u+local]=0xffffffffu;
                    m8FlowerRootPresence[local]=0u;
                }
                GroupMemoryBarrierWithGroupSync();
            }
        }
        [loop] for (uint witnessOwner=0u; witnessOwner<4u; witnessOwner++)
        {
            uint ownerLocal=lane+128u*witnessOwner;
            uint4 prior=ownBounds[witnessOwner];
            uint peerTag;M8FlowerInterval2 peerRoot;
            if ((ownCertain&(1u<<witnessOwner))==0u || m8FlowerRootPresence[ownerLocal]!=6u ||
                !M8FlowerReadRootIntersection(ownerLocal,peerTag,peerRoot))
                continue;
            M8FlowerInterval2 intersection;
            intersection.x=M8FlowerI(
                M8FlowerFromOrderedFloat(max(prior.x,m8FlowerReductionBank[1024u+ownerLocal])),
                M8FlowerFromOrderedFloat(min(prior.y,m8FlowerReductionBank[1536u+ownerLocal])));
            intersection.y=M8FlowerI(
                M8FlowerFromOrderedFloat(max(prior.z,m8FlowerReductionBank[2048u+ownerLocal])),
                M8FlowerFromOrderedFloat(min(prior.w,m8FlowerReductionBank[2560u+ownerLocal])));
            uint sharedTag;
            if(!M8FlowerResolveRootProof(ownTags[witnessOwner]&peerTag,
                ownTags[witnessOwner]|peerTag,intersection,sharedTag))continue;
            uint sector=(sharedTag>>8u)&31u;
            uint selectedFlag;
            // The relation is expressed in THIS endpoint owner's face frame,
            // even though the other endpoint supplied the second root. Only
            // its generated selected flag is incident at this certain phase.
            // Do not transport that face flag into a child petal implicitly.
            if (sector>=M8FlowerLineMetaAt(lineClass).z || sector>=31u ||
                !M8FlowerR1RootFlag(direction,sharedTag,selectedFlag) ||
                M8FlowerPetalNodesAt(selectedFlag).x!=direction) continue;
            m8FlowerR1Witness[ownerLocal][plus?1u:0u] |=
                (sector+1u)<<(5u*direction);
        }
        GroupMemoryBarrierWithGroupSync();
    }
    for (uint local = lane; local < 512u; local += 128u)
    {
        uint source = m8FlowerOwnerSource[local];
        if (source == 0xffffffffu) continue;
        source &= M8_FLOWER_OBSERVATION_PIXEL_MASK;
        // Direct support is excluded even if its competing positive symbols
        // are unresolved. Dual is never a way to choose between them.
        InterlockedOr(_M8TileBits[M8TileWordIndex(slot,local >> 5u)].z,
            1u << (local & 31u));
        if (M8FlowerOwnerIsRejected(local)) continue;
        uint representative = m8FlowerOwnerPrecision[local];
        if (representative != 0xffffffffu)
            source = representative & M8_FLOWER_OBSERVATION_PIXEL_MASK;
        uint plane;
        float3 world;
        if (!M8FlowerMeasurement(source,M8GlobalKernelCoord(slot,local),plane,world))
            continue;
        KernelState before = M8LoadKernelState(slot,local);
        bool seed=(before.flags & M8_FLOWER_OCCUPIED_FLAG)==0u &&
            (before.flags & M8_FLOWER_SEED_FLAG)!=0u;
        bool compatible=M8FlowerCompatibleCarrier(before.flags,plane,
            normalError,offsetError,seed);
        float3 leftRgb, rightRgb;
        if (!MerkabaTrySampleCameraRgb(0u,world,leftRgb) ||
            !MerkabaTrySampleCameraRgb(1u,world,rightRgb)) continue;
        precise float3 rgb = (leftRgb+rightRgb)*0.5;
        uint3 color = uint3(round(saturate(rgb)*255.0));
        uint4 next = M8FlowerAdmitR1(uint4(asuint(before.evidence),
                before.packedColor,before.colorConfidence,before.flags),
            plane,PackRgb(color.x,color.y,color.z),compatible,true,
            M8FlowerR1FlagWitness(m8FlowerR1Witness[local],plane,normalError,offsetError));
        // This is one direct admission for the frozen observation. No
        // closure pass or new observation is implied by storing its seed.
        M8FlowerStoreR1(slot,local,before,next);
    }
    DeviceMemoryBarrierWithGroupSync();
    [loop] for (uint local=lane; local<512u; local+=128u)
        M8FlowerApplyDualVeto(slot,local,chunkOwner.x,chunkOwner.y,tileMeta.y);
    DeviceMemoryBarrierWithGroupSync();
    if (lane == 0u)
    {
        M8FlowerPublishR1Change(slot);
        _M8TileRecords[M8TileRuntimeIndex(slot)].x=_M8ObservationToken;
    }
}

#endif
