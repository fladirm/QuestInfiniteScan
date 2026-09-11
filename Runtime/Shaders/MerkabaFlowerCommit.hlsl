#ifndef GENESIS_MERKABA_FLOWER_COMMIT_INCLUDED
#define GENESIS_MERKABA_FLOWER_COMMIT_INCLUDED

#include "MerkabaObservationBins.hlsl"
#include "MerkabaObservationReduction.hlsl"
#define M8_FLOWER_SIDECAR_WRITE
#include "MerkabaFlowerSidecar.hlsl"
#include "MerkabaFlowerSignalItems.hlsl"

// Same immutable record backing as Emit. No scratch eligibility writes may
// alias this view; own eligibility is checked at its existing plane reader.
StructuredBuffer<M8ObservationRecord> _M8ObservationRecordsRead;

void M8FlowerRequestSkinDependencies(uint neededHalo);

groupshared uint m8FlowerOwnerSource[512];
groupshared uint m8FlowerOwnerPrecision[512];
groupshared uint m8FlowerRootPresence[512];
// Six directed R1 relations, each with two algebraic signs. Each five-bit
// field is 0 (no witness) or 1+sector; R1 has 24 generated order sectors.
// x.bit31 reuses a non-sector bit for this owner's rejection state.
groupshared uint2 m8FlowerR1Witness[512];
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

bool M8FlowerReadMeasuredEndpoint(M8ObservationRecord record,out uint plane,out float3 world)
{
    plane=record.MeasuredPlane;world=0.0.xxx;
    if(record.Reserved!=0u || !M8FlowerHasPlane(plane) ||
        record.SourcePixel>=gsDepthTexSize.x*gsDepthTexSize.y)return false;
    uint2 pixel=uint2(record.SourcePixel%gsDepthTexSize.x,record.SourcePixel/gsDepthTexSize.x);
    float depth=gsDepthTex.Load(int3(pixel,0));
    precise float2 uv=(float2(pixel)+0.5)/float2(gsDepthTexSize);
    world=gsDepthNDCtoWorld(float3(uv,depth));
    return true;
}

bool M8FlowerInputPlane(M8ObservationRecord record,uint slot,
    out uint plane,out float3 normal,out float offset)
{
    plane=record.MeasuredPlane;normal=0.0.xxx;offset=0.0;
    if((record.TileAndKernel>>9u)!=slot || record.Reserved!=0u ||
        record.SourcePixel>=gsDepthTexSize.x*gsDepthTexSize.y ||
        !M8FlowerHasPlane(plane))return false;
    M8FlowerUnpackPlane(plane,normal,offset);
    return true;
}

bool M8FlowerOwnPlaneEligible(uint slot,uint local,uint plane,
    float normalError,float offsetError)
{
    KernelState previous=M8LoadKernelStateRead(slot,local);
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

void M8FlowerRequestErasePeer(uint halo)
{
    // ERASE has no observation allocation pass. Request the existing COLD
    // address directly through the same SSD load queue as its brush query.
    int3 tile=m8FlowerHaloTile+M8FlowerHaloDelta(halo);
    if(any(tile< -268435456) || any(tile>268435455))
    {M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);return;}
    MerkabaM8Address address=MerkabaAddressOf(tile*8);
    uint block;
    if(!M8FindBlock(address.blockCoord,block))return;
    uint chunkRef=_M8BlockChunkRefsRead[block*512u+address.chunkLocal];
    if(chunkRef==MERKABA_REF_EMPTY || chunkRef>=MERKABA_REF_EVICTING)return;
    uint chunk=chunkRef-1u;
    M8QueueColdTileLoad(chunk*64u+address.tileLocal,chunk,address.tileLocal);
}

uint M8FlowerR1PeersStatus(uint local,bool erase)
{
    int3 origin=int3(local&7u,(local>>3u)&7u,local>>6u);
    [loop]for(uint node=6u;node<26u;node++)
    {
        int3 relative=origin+M8FlowerNodeAt(node).xyz;
        uint3 cell=(uint3)((relative>>3)+1);
        uint halo=cell.x+3u*(cell.y+3u*cell.z);
        uint state=m8FlowerHalo[halo]>>30u;
        if(state==M8_FLOWER_HALO_MISSING)continue;
        if(state==M8_FLOWER_HALO_COLD)
        {
            if(erase)M8FlowerRequestErasePeer(halo);
            else M8FlowerRequestSkinDependencies(1u<<halo);
            return M8_FLOWER_ARENA_BUSY;
        }
        uint peerSlot,peerLocal;
        if(state!=M8_FLOWER_HALO_HOT || !M8FlowerHaloKernel(relative,peerSlot,peerLocal,true,true))
            return M8_FLOWER_ARENA_INVALID;
        uint peer,first,count,captured;
        if(!M8FlowerTryFindOwner(peerSlot,peerLocal,_M8TileRecords[M8TileRuntimeIndex(peerSlot)].w,peer))
            return M8_FLOWER_ARENA_INVALID;
        uint status=M8FlowerValidatePhaseCut(peer,_M8WorldPublishingGeneration,
            _M8WorldRetiredGeneration,first,count,captured);
        if(status!=M8_FLOWER_ARENA_OK)return status;
    }
    return M8_FLOWER_ARENA_OK;
}

bool M8FlowerR1CutAccepted(uint status)
{
    if(status==M8_FLOWER_ARENA_OK)return true;
    if(status==M8_FLOWER_ARENA_BUSY || status==M8_FLOWER_SIDECAR_STALE_SLOT)
        M8CounterIncrement(M8_COUNTER_UNRESOLVED_OBSERVATION_TILES);
    else if(status==M8_FLOWER_ARENA_CAPACITY)
        M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
    else M8CounterIncrement(M8_COUNTER_FAILED_WRITES);
    return false;
}

// One publication point for "this tile's direct evidence moved in this
// snapshot", shared by both commit exits.
void M8FlowerPublishR1Change(uint slot)
{
    if (m8FlowerR1Changed == 0u) return;
    InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK],
        M8_OBSERVATION_CHANGED_R1);
}

bool M8FlowerPrepareR1(uint slot,uint local,KernelState before,uint4 value,
    bool structural,bool erase)
{
    uint generation=_M8TileRecords[M8TileRuntimeIndex(slot)].w;
    if(structural)
    {
        uint ownerRef,first,count,captured;
        if(!M8FlowerTryFindOwner(slot,local,generation,ownerRef))
            return M8FlowerR1CutAccepted(M8_FLOWER_ARENA_INVALID);
        if(!M8FlowerR1CutAccepted(M8FlowerValidatePhaseCut(ownerRef,
            _M8WorldPublishingGeneration,_M8WorldRetiredGeneration,first,count,captured)))return false;
        if(!M8FlowerR1CutAccepted(M8FlowerR1PeersStatus(local,erase)))return false;
    }
    uint address;
    if(!M8FlowerReserveR1Change(address))
    {M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);return false;}
    if(structural)
    {
        InterlockedOr(_M8TileBits[M8TileWordIndex(slot,local>>5u)].w,1u<<(local&31u));
        M8FlowerQueueChangedTile(slot);
        int3 origin=int3(local&7u,(local>>3u)&7u,local>>6u);
        [loop]for(uint node=0u;node<26u;node++)
        {
            uint peerSlot,peerLocal;
            if(M8FlowerHaloKernel(origin+M8FlowerNodeAt(node).xyz,peerSlot,peerLocal,true,true))
                M8FlowerQueueChangedTile(peerSlot);
        }
    }
    // Preflight is read-only: a different WG can inspect this owner's fine
    // rows without racing an epoch/count change. Source/peer invalidation
    // and M8 publication follow global GPU barriers inside this snapshot;
    // nothing is deferred to another frame.
    _M8FlowerSignalItems.Store4(address+16u,value);
    DeviceMemoryBarrier();
    uint flags=M8_FLOWER_R1_PREPARED | (structural?M8_FLOWER_R1_STRUCTURAL:0u);
    _M8FlowerSignalItems.Store4(address,uint4((slot<<9u)|local,generation,before.flags,flags));
    M8MarkTileDirty(slot);
    InterlockedOr(m8FlowerR1Changed,1u);
    return true;
}

bool M8FlowerStoreR1(uint slot,uint local,KernelState before,uint4 value)
{
    if(all(value==uint4(asuint(before.evidence),before.packedColor,
        before.colorConfidence,before.flags)))return true;
    return M8FlowerPrepareR1(slot,local,before,value,
        M8FlowerParentStructureChanged(before.flags,value.w),false);
}

// One workgroup owns every positive write in this tile. The four plane
// coordinates and then the twelve R1 root buckets are reduced over the SAME
// frozen records. Atomic arrival order cannot select a conflicting sheet.
[numthreads(128,1,1)]
void FlowerCommit(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    // Missing residency counters are telemetry, not global admission gates.
    // Validate this bin/slot and its own endpoint/epoch dependencies below.
    if (_M8Counters[M8_COUNTER_OBSERVATION_COMPLETED] != 0u ||
        _M8Counters[M8_COUNTER_OBSERVATION_FAILURE] != 0u) return;
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
    // Runtime.x stamps preparation of this snapshot. TileBits.w identifies
    // its changed sources only until the peer cut and M8 publication barrier.
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    if (runtime.x == _M8ObservationToken) return;
    uint4 tileMeta=_M8TileRecords[M8TileMetaIndex(slot)];
    if(runtime.w==0u || tileMeta.x>=MERKABA_M8_CHUNK_CAPACITY || tileMeta.y>=64u ||
        _M8ChunkTileRefs[tileMeta.x*64u+tileMeta.y]!=slot+1u)
    {
        if(lane==0u)
        {
            M8CounterIncrement(M8_COUNTER_REFINEMENT_PENDING_TILES);
            M8CounterIncrement(M8_COUNTER_UNRESOLVED_OBSERVATION_TILES);
        }
        return;
    }
    if (lane == 0u) m8FlowerR1Changed=0u;
    M8FlowerCacheTileHalo(slot,lane,false,true,true);
    if (bin.y == 0u) return;
    // The receiver pass also groups this bin's measured owners. Its union
    // queue includes every touched tile plus actual structural peer targets.
    if(lane==0u)M8FlowerQueueChangedTile(slot);
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
                            sourceSlot,ignoredLocal,true,true)) continue;
                    sourceBin=_M8ObservationTileBinsRead[sourceSlot];
                    if (sourceBin.x!=_M8ObservationToken || sourceBin.y==0u ||
                        sourceBin.w!=sourceBin.y || sourceBin.z>=_M8ObservationRecordCapacity ||
                        sourceBin.y>_M8ObservationRecordCapacity-sourceBin.z) continue;
                }
                [loop] for (uint index=lane; index<sourceBin.y; index+=128u)
                {
                    M8ObservationRecord record=_M8ObservationRecordsRead[sourceBin.z+index];
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
        if (M8FlowerOwnerIsRejected(local)) continue;
        uint representative = m8FlowerOwnerPrecision[local];
        if (representative != 0xffffffffu)
            source = representative & M8_FLOWER_OBSERVATION_PIXEL_MASK;
        uint plane;
        float3 world;
        if (!M8FlowerMeasurement(source,M8GlobalKernelCoord(slot,local),plane,world))
            continue;
        KernelState before = M8LoadKernelStateRead(slot,local);
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
    if (lane == 0u)
    {
        M8FlowerPublishR1Change(slot);
        _M8TileRecords[M8TileRuntimeIndex(slot)].x=_M8ObservationToken;
    }
}

#endif
