#ifndef GENESIS_MERKABA_FLOWER_COMMIT_INCLUDED
#define GENESIS_MERKABA_FLOWER_COMMIT_INCLUDED

#include "MerkabaObservationBins.hlsl"
#include "MerkabaObservationReduction.hlsl"

groupshared uint m8FlowerOwnerRejected[512];
groupshared uint m8FlowerOwnerSource[512];
groupshared uint m8FlowerOwnerPrecision[512];
groupshared uint m8FlowerRootPresence[512];
// Six directed R1 relations, each with two algebraic signs. Each five-bit
// field is 0 (no witness) or 1+sector; R1 has sixteen generated sectors.
groupshared uint2 m8FlowerR1Witness[512];
groupshared uint m8FlowerDualReady;
groupshared uint m8FlowerR1Changed;

// Attempt-local classification in the symbol tag's generated scratch bits.
// This caches only admissibility, never a branch rank or a stored plane.
#define M8_FLOWER_RECORD_ELIGIBLE (1u << 20u)

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

// A common generated child flag may contain two R1 loop nodes even though
// its parent has just one R1 anchor. Check the actual inherited/new loop
// sectors using bounded plane evidence; R2/R3 nodes never gate admission.
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
    // Root nodes and the 72 immutable strand classes are the only possible
    // tests. Shared child nodes reuse the result, not another root solve.
    uint3 tested=0u.xxx, certain=0u.xxx;
    [loop] for (uint petal=0u; petal<48u; petal++)
    {
        [unroll] for (uint child=0u; child<4u; child++)
        {
            uint supportedAxes=0u;
            [unroll] for (uint node=0u; node<3u; node++)
            {
                uint level,lineClass,strandClass;
                int3 junction;
                int endpoint,phase,inherited;
                if (!M8FlowerTryGetChildPhaseLoop(petal,child+1u,node,
                        level,junction,lineClass,strandClass,endpoint,phase,inherited) ||
                    lineClass>=3u) continue;
                uint direction=2u*lineClass+(endpoint<0?1u:0u);
                uint minus=(witness.x>>(5u*direction))&31u;
                uint plus=(witness.y>>(5u*direction))&31u;
                if ((minus|plus)==0u) continue;
                uint cache=level==0u?72u+direction:strandClass;
                uint word=cache>>5u,bit=1u<<(cache&31u);
                if ((tested[word]&bit)==0u)
                {
                    tested[word]|=bit;
                    precise float3 relative=float3(junction)*
                        (M8_FLOWER_LATTICE_STEP*(level==0u?0.5:0.25));
                    M8FlowerInterval3 abc;
                    bool admitted=false;
                    if (M8FlowerPlaneIntervals(normal,offset,relative,
                            M8FlowerGeometryLoopRadius[level*13u+lineClass],lineClass,
                            normalError,offsetError,abc))
                    {
                        [unroll] for (uint sign=0u; sign<2u; sign++)
                        {
                            uint expected=sign==0u?minus:plus;
                            if (expected==0u) continue;
                            uint tag,classification;
                            M8FlowerInterval2 root;
                            if (M8FlowerClassifyObservationRoot(level,lineClass,
                                    endpoint<0,sign!=0u,abc,tag,root,classification) &&
                                ((tag>>8u)&31u)==expected-1u)
                                admitted=true;
                        }
                    }
                    if (admitted) certain[word]|=bit;
                }
                if ((certain[word]&bit)!=0u) supportedAxes|=1u<<lineClass;
            }
            if (countbits(supportedAxes)>=2u) return true;
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
    float3 beforeNormal,afterNormal;float beforeOffset,afterOffset;
    M8FlowerUnpackPlane(previous,beforeNormal,beforeOffset);
    M8FlowerUnpackPlane(next,afterNormal,afterOffset);
    if(all(beforeNormal==afterNormal)&&beforeOffset==afterOffset)return false;
    float normalError=M8FlowerNext(_M8PlaneErrorBounds.x+M8_FLOWER_NORMAL_QUANTIZATION_UPPER);
    float offsetError=M8FlowerNext(_M8PlaneErrorBounds.y+M8_FLOWER_OFFSET_QUANTIZATION_UPPER);
    [loop]for(uint direction=0u;direction<26u;direction++)
    {
        uint lineClass=(uint)M8FlowerDirection[direction].w;
        float3 relative=float3(M8FlowerDirection[direction].xyz)*(M8_FLOWER_LATTICE_STEP*0.5);
        M8FlowerInterval3 oldAbc,newAbc;
        if(!M8FlowerPlaneIntervals(beforeNormal,beforeOffset,relative,
                M8FlowerGeometryLoopRadius[lineClass],lineClass,normalError,offsetError,oldAbc) ||
            !M8FlowerPlaneIntervals(afterNormal,afterOffset,relative,
                M8FlowerGeometryLoopRadius[lineClass],lineClass,normalError,offsetError,newAbc))continue;
        [unroll]for(uint sign=0u;sign<2u;sign++)
        {
            uint oldTag,newTag,oldClass,newClass;
            M8FlowerInterval2 oldRoot,newRoot;
            bool oldCertain=M8FlowerClassifyObservationRoot(0u,lineClass,false,sign!=0u,
                oldAbc,oldTag,oldRoot,oldClass);
            bool newCertain=M8FlowerClassifyObservationRoot(0u,lineClass,false,sign!=0u,
                newAbc,newTag,newRoot,newClass);
            if(oldCertain&&newCertain&&((oldTag^newTag)&0x1f80u)!=0u)return true;
            if((oldCertain&&newClass==M8_FLOWER_ROOT_IMPOSSIBLE)||
                (newCertain&&oldClass==M8_FLOWER_ROOT_IMPOSSIBLE))return true;
        }
    }
    // An unresolved root is not proof of a structural change. Its consumer
    // must re-evaluate it against this carrier; compatible refinements keep
    // the epoch and never discard all fine data merely for new plane bits.
    return false;
}

void M8FlowerStoreR1(uint slot, uint local, KernelState before, uint4 value)
{
    if (all(value == uint4(asuint(before.evidence),before.packedColor,
            before.colorConfidence,before.flags))) return;
    KernelState after = before;
    UpdateOccupancy(slot,local,after,asint(value.x)-before.evidence);
    after.evidence = asint(value.x);
    after.packedColor = value.y;
    after.colorConfidence = value.z;
    after.flags = value.w;
    if(M8FlowerParentStructureChanged(before.flags,after.flags))
        InterlockedOr(_M8TileBits[M8TileWordIndex(slot,local>>5u)].w,
            1u<<(local&31u));
    M8StoreKernelState(slot,local,after);
    if (M8FlowerHasPlane(after.flags) || (after.flags & M8_FLOWER_OCCUPIED_FLAG) != 0u)
        M8MarkR1Active(slot,local);
    else M8FlowerUnmarkR1Active(slot,local);
    M8MarkTileDirty(slot);
    InterlockedOr(m8FlowerR1Changed,1u);
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
    uint4 next=M8FlowerContradictR1(uint4(asuint(before.evidence),before.packedColor,
        before.colorConfidence,before.flags));
    if (asint(next.x) < before.evidence)
        M8CounterIncrement(M8_COUNTER_CARVE_EVIDENCE_DECREMENTS);
    if ((before.flags & M8_FLOWER_OCCUPIED_FLAG) != 0u &&
        (next.w & M8_FLOWER_OCCUPIED_FLAG) == 0u)
        M8CounterIncrement(M8_COUNTER_CARVE_OCCUPIED_TO_FREE);
    M8FlowerStoreR1(slot,local,before,next);
}

// One workgroup owns every positive write in this tile. The four plane
// coordinates and then the twelve R1 root buckets are reduced over the SAME
// frozen records. Atomic arrival order cannot select a conflicting sheet.
[numthreads(128,1,1)]
void FlowerCommit(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    if (_M8Counters[M8_COUNTER_OBSERVATION_COMPLETED] != 0u ||
        _M8Counters[M8_COUNTER_OBSERVATION_FAILURE] != 0u ||
        _M8Counters[M8_COUNTER_UNRESOLVED_SURFACE_TILES] != 0u ||
        _M8Counters[M8_COUNTER_UNRESOLVED_CARVE_TILES] != 0u) return;
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
    if (_M8TileRecords[M8TileRuntimeIndex(slot)].x == _M8ObservationToken) return;
    uint4 tileMeta=_M8TileRecords[M8TileMetaIndex(slot)];
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
        if (lane == 0u) M8CounterIncrement(M8_COUNTER_UNRESOLVED_CARVE_TILES);
        return;
    }
    if (bin.y == 0u)
    {
        [loop] for (uint local=lane; local<512u; local+=128u)
            M8FlowerApplyDualVeto(slot,local,chunkOwner.x,chunkOwner.y,tileMeta.y);
        DeviceMemoryBarrierWithGroupSync();
        if (lane == 0u)
        {
            if (m8FlowerR1Changed != 0u)
                InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK],
                    M8_OBSERVATION_CHANGED_R1);
            _M8TileRecords[M8TileRuntimeIndex(slot)].x=_M8ObservationToken;
        }
        return;
    }
    M8FlowerCacheTileHalo(slot,lane,false,true);
    for (uint local = lane; local < 512u; local += 128u)
    {
        m8FlowerOwnerRejected[local] = 0u;
        m8FlowerOwnerSource[local] = 0xffffffffu;
        m8FlowerOwnerPrecision[local] = 0xffffffffu;
        m8FlowerR1Witness[local] = 0u.xx;
    }
    GroupMemoryBarrierWithGroupSync();
    float normalError = M8FlowerNext(_M8PlaneErrorBounds.x+
        M8_FLOWER_NORMAL_QUANTIZATION_UPPER);
    float offsetError = M8FlowerNext(_M8PlaneErrorBounds.y+
        M8_FLOWER_OFFSET_QUANTIZATION_UPPER);
    // Classify the previous carrier once per input record, not once per
    // root pass. Every strict endpoint excludes negative mutation even when
    // its owner is occupied by an incompatible sheet that must be preserved.
    [loop] for (uint index = lane; index < bin.y; index += 128u)
    {
        M8ObservationRecord record = _M8ObservationRecords[bin.z+index];
        record.SymbolTag &= ~M8_FLOWER_RECORD_ELIGIBLE;
        uint plane;
        float3 normal;
        float offset;
        if (M8FlowerInputPlane(record,slot,plane,normal,offset))
        {
            uint local = record.TileAndKernel & 511u;
            InterlockedOr(_M8TileBits[M8TileWordIndex(slot,local >> 5u)].z,
                1u << (local & 31u));
            KernelState previous = M8LoadKernelState(slot,local);
            if ((previous.flags & M8_FLOWER_OCCUPIED_FLAG) == 0u ||
                M8FlowerCompatibleCarrier(previous.flags,plane,normalError,offsetError))
                record.SymbolTag |= M8_FLOWER_RECORD_ELIGIBLE;
        }
        // Peers consume only the two immutable identity words. Do not
        // rewrite the whole record while another tile reads that identity.
        _M8ObservationRecords[bin.z+index].SymbolTag = record.SymbolTag;
    }
    DeviceMemoryBarrierWithGroupSync();
    [loop] for (uint component = 0u; component < 4u; ++component)
    {
        for (uint local = lane; local < 512u; local += 128u)
            M8FlowerResetObservationBucket(local);
        GroupMemoryBarrierWithGroupSync();
        [loop] for (uint index = lane; index < bin.y; index += 128u)
        {
            M8ObservationRecord record = _M8ObservationRecords[bin.z+index];
            if ((record.SymbolTag & M8_FLOWER_RECORD_ELIGIBLE) == 0u) continue;
            uint plane;
            float3 normal;
            float offset;
            if (!M8FlowerInputPlane(record,slot,plane,normal,offset)) continue;
            uint local = record.TileAndKernel & 511u;
            // The literal four-component loop must never form normal[3].
            // Unity's Vulkan front-end checks both sides of a conditional
            // during unrolling, even when that branch selects the offset.
            float centre = float4(normal,offset)[component];
            M8FlowerInterval interval = M8FlowerCenterRadius(centre,
                component == 3u ? offsetError : normalError);
            InterlockedMax(m8FlowerLower[local],M8FlowerOrderedFloat(interval.lo));
            InterlockedMin(m8FlowerUpper[local],M8FlowerOrderedFloat(interval.hi));
            uint freeSide = plane & M8_FLOWER_PLANE_FREE_SIDE;
            InterlockedAnd(m8FlowerTagIntersection[local],freeSide);
            InterlockedOr(m8FlowerTagUnion[local],freeSide);
            InterlockedMin(m8FlowerOwnerSource[local],record.SourcePixel);
        }
        GroupMemoryBarrierWithGroupSync();
        for (uint local = lane; local < 512u; local += 128u)
            if (m8FlowerOwnerSource[local] != 0xffffffffu &&
                (m8FlowerLower[local] > m8FlowerUpper[local] ||
                 m8FlowerTagIntersection[local] != m8FlowerTagUnion[local]))
                m8FlowerOwnerRejected[local] = 1u;
        GroupMemoryBarrierWithGroupSync();
    }
    // SourcePixel uses only eighteen bits. Its owner-local scratch word can
    // retain the unanimously intersected free-side orientation through the
    // root passes without another groupshared allocation.
    for (uint sideLocal=lane; sideLocal<512u; sideLocal+=128u)
        if (m8FlowerOwnerSource[sideLocal]!=0xffffffffu)
            m8FlowerOwnerSource[sideLocal] |=
                m8FlowerTagIntersection[sideLocal]&M8_FLOWER_PLANE_FREE_SIDE;
    GroupMemoryBarrierWithGroupSync();
    [loop] for (uint relation = 0u; relation < 12u; ++relation)
    {
        uint direction = relation >> 1u;
        uint lineClass = (uint)M8FlowerDirection[direction].w;
        bool plus = (relation & 1u) != 0u;
        precise float3 relative = float3(M8FlowerDirection[direction].xyz)*
            (M8_FLOWER_LATTICE_STEP*0.5);
        for (uint local = lane; local < 512u; local += 128u)
        {
            M8FlowerResetObservationBucket(local);
            m8FlowerRootPresence[local] = 0u;
        }
        GroupMemoryBarrierWithGroupSync();
        // Intersect first. Root ambiguity does not invent a knot; a strict
        // compatible plane may still be retained as a non-drawing R1 seed.
        [loop] for (uint index = lane; index < bin.y; index += 128u)
        {
            M8ObservationRecord record = _M8ObservationRecords[bin.z+index];
            if ((record.SymbolTag & M8_FLOWER_RECORD_ELIGIBLE) == 0u) continue;
            uint local = record.TileAndKernel & 511u;
            uint plane;
            float3 normal;
            float offset;
            if (m8FlowerOwnerRejected[local] != 0u ||
                !M8FlowerInputPlane(record,slot,plane,normal,offset)) continue;
            M8FlowerInterval3 abc;
            if (!M8FlowerPlaneIntervals(normal,offset,relative,
                    M8_FLOWER_R1_RADIUS,lineClass,normalError,offsetError,abc)) continue;
            uint tag,classification;
            M8FlowerInterval2 root;
            if (M8FlowerClassifyObservationRoot(0u,lineClass,
                    (direction & 1u) != 0u,plus,abc,tag,root,classification))
            {
                InterlockedOr(m8FlowerRootPresence[local],2u);
                M8FlowerIntersectRoot(local,tag,root);
            }
            else if (classification == M8_FLOWER_ROOT_IMPOSSIBLE)
                InterlockedOr(m8FlowerRootPresence[local],1u);
        }
        GroupMemoryBarrierWithGroupSync();
        for (uint local = lane; local < 512u; local += 128u)
            if (m8FlowerRootPresence[local] == 3u ||
                (m8FlowerTagIntersection[local] != 0xffffffffu &&
                 !M8FlowerObservationBucketCertain(local)))
                m8FlowerOwnerRejected[local] = 1u;
        GroupMemoryBarrierWithGroupSync();
        // A representative key can act only inside a nonempty compatible
        // bucket. It never chooses a sector, root sign, or petal class.
        [loop] for (uint index = lane; index < bin.y; index += 128u)
        {
            M8ObservationRecord record = _M8ObservationRecords[bin.z+index];
            if ((record.SymbolTag & M8_FLOWER_RECORD_ELIGIBLE) == 0u) continue;
            uint local = record.TileAndKernel & 511u;
            uint plane;
            float3 normal;
            float offset;
            if (m8FlowerOwnerRejected[local] != 0u ||
                !M8FlowerInputPlane(record,slot,plane,normal,offset)) continue;
            M8FlowerInterval3 abc;
            if (!M8FlowerPlaneIntervals(normal,offset,relative,
                    M8_FLOWER_R1_RADIUS,lineClass,normalError,offsetError,abc)) continue;
            uint tag,classification;
            M8FlowerInterval2 root;
            if (M8FlowerClassifyObservationRoot(0u,lineClass,
                    (direction & 1u) != 0u,plus,abc,tag,root,classification))
                M8FlowerSelectRootRepresentative(local,tag,classification,
                    root,record.SourcePixel);
        }
        GroupMemoryBarrierWithGroupSync();
        for (uint local = lane; local < 512u; local += 128u)
            m8FlowerOwnerPrecision[local] = min(m8FlowerOwnerPrecision[local],
                m8FlowerRepresentative[local]);
        GroupMemoryBarrierWithGroupSync();

        // Preserve each lane's four OWN endpoint intersections in registers,
        // then reuse the same groupshared reducer for the fixed other endpoint.
        // Peer conflicts disable this relation witness; they never discard
        // the owner's independently measured R1 surface/seed.
        uint ownTags[4];
        uint4 ownBounds[4];
        bool ownCertain[4];
        [unroll] for (uint owned=0u; owned<4u; owned++)
        {
            uint ownerLocal=lane+128u*owned;
            ownTags[owned]=m8FlowerTagIntersection[ownerLocal];
            ownBounds[owned]=uint4(m8FlowerLower[ownerLocal],m8FlowerUpper[ownerLocal],
                m8FlowerRootLowerY[ownerLocal],m8FlowerRootUpperY[ownerLocal]);
            ownCertain[owned]=m8FlowerOwnerRejected[ownerLocal]==0u &&
                m8FlowerRepresentative[ownerLocal]!=0xffffffffu &&
                M8FlowerObservationBucketCertain(ownerLocal);
            m8FlowerTagIntersection[ownerLocal]=0xffffffffu;
            m8FlowerTagUnion[ownerLocal]=0u;
            m8FlowerLower[ownerLocal]=0u;
            m8FlowerUpper[ownerLocal]=0xffffffffu;
            m8FlowerRootLowerY[ownerLocal]=0u;
            m8FlowerRootUpperY[ownerLocal]=0xffffffffu;
            m8FlowerRootPresence[ownerLocal]=0u;
        }
        GroupMemoryBarrierWithGroupSync();
        int3 step=M8FlowerDirection[direction].xyz;
        // Exactly two spans: this tile and its one signed face neighbour.
        // Each record maps arithmetically to K=Q-d; no record-pair search.
        [loop] for (uint peerSpan=0u; peerSpan<2u; peerSpan++)
        {
            uint peerSlot=slot, ignoredLocal=0u;
            if (peerSpan!=0u && !M8FlowerHaloKernel(int3(4,4,4)+8*step,
                    peerSlot,ignoredLocal,true)) continue;
            uint4 peerBin=_M8ObservationTileBinsRead[peerSlot];
            if (peerBin.x!=_M8ObservationToken || peerBin.y==0u ||
                peerBin.w!=peerBin.y || peerBin.z>=_M8ObservationRecordCapacity ||
                peerBin.y>_M8ObservationRecordCapacity-peerBin.z) continue;
            [loop] for (uint peerIndex=lane; peerIndex<peerBin.y; peerIndex+=128u)
            {
                M8ObservationRecord peer;
                peer.TileAndKernel=_M8ObservationRecords[peerBin.z+peerIndex].TileAndKernel;
                peer.SourcePixel=_M8ObservationRecords[peerBin.z+peerIndex].SourcePixel;
                peer.SymbolTag=0u;
                peer.PrecisionKey=0u;
                uint peerLocal=peer.TileAndKernel&511u;
                int3 q=int3(peerLocal&7u,(peerLocal>>3u)&7u,peerLocal>>6u);
                if (peerSpan!=0u) q+=8*step;
                int3 k=q-step;
                if (any(k<0) || any(k>7)) continue;
                uint ownerLocal=(uint)k.x+8u*((uint)k.y+8u*(uint)k.z);
                uint source=m8FlowerRepresentative[ownerLocal];
                if (source==0xffffffffu || m8FlowerOwnerRejected[ownerLocal]!=0u) continue;
                uint peerPlane;
                float3 peerNormal;
                float peerOffset;
                M8FlowerInterval3 peerAbc;
                if (!M8FlowerInputPlane(peer,peerSlot,peerPlane,peerNormal,peerOffset) ||
                    (peerPlane&M8_FLOWER_PLANE_FREE_SIDE)!=
                        (m8FlowerOwnerSource[ownerLocal]&M8_FLOWER_PLANE_FREE_SIDE) ||
                    !M8FlowerPlaneIntervals(peerNormal,peerOffset,-relative,
                        M8_FLOWER_R1_RADIUS,lineClass,normalError,offsetError,peerAbc))
                {
                    InterlockedOr(m8FlowerRootPresence[ownerLocal],8u);
                    continue;
                }
                uint peerTag,peerClass;
                M8FlowerInterval2 peerRoot;
                if (M8FlowerClassifyObservationRoot(0u,lineClass,
                        (direction&1u)!=0u,plus,peerAbc,peerTag,peerRoot,peerClass))
                {
                    uint independent=peer.SourcePixel!=
                        (source&M8_FLOWER_OBSERVATION_PIXEL_MASK)?4u:0u;
                    InterlockedOr(m8FlowerRootPresence[ownerLocal],2u|independent);
                    M8FlowerIntersectRoot(ownerLocal,peerTag,peerRoot);
                }
                else InterlockedOr(m8FlowerRootPresence[ownerLocal],
                    peerClass==M8_FLOWER_ROOT_IMPOSSIBLE?1u:8u);
            }
        }
        GroupMemoryBarrierWithGroupSync();
        [unroll] for (uint witnessOwner=0u; witnessOwner<4u; witnessOwner++)
        {
            uint ownerLocal=lane+128u*witnessOwner;
            uint4 prior=ownBounds[witnessOwner];
            if (!ownCertain[witnessOwner] || m8FlowerRootPresence[ownerLocal]!=6u ||
                !M8FlowerObservationBucketCertain(ownerLocal) ||
                m8FlowerTagIntersection[ownerLocal]!=ownTags[witnessOwner] ||
                max(prior.x,m8FlowerLower[ownerLocal])>min(prior.y,m8FlowerUpper[ownerLocal]) ||
                max(prior.z,m8FlowerRootLowerY[ownerLocal])>min(prior.w,m8FlowerRootUpperY[ownerLocal]))
                continue;
            uint sector=(ownTags[witnessOwner]>>8u)&31u;
            // The exact generated R1 table contains sixteen sectors. Never
            // truncate a changed ABI table into this five-bit witness field.
            if (sector>=16u) continue;
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
        if (m8FlowerOwnerRejected[local] != 0u) continue;
        uint representative = m8FlowerOwnerPrecision[local];
        if (representative != 0xffffffffu)
            source = representative & M8_FLOWER_OBSERVATION_PIXEL_MASK;
        uint plane;
        float3 world;
        if (!M8FlowerMeasurement(source,M8GlobalKernelCoord(slot,local),plane,world))
            continue;
        KernelState before = M8LoadKernelState(slot,local);
        bool compatible = M8FlowerCompatibleCarrier(before.flags,plane,
            normalError,offsetError);
        if ((before.flags & M8_FLOWER_OCCUPIED_FLAG) == 0u &&
            (before.flags & M8_FLOWER_SEED_FLAG) != 0u)
            compatible = compatible && M8FlowerSeedCorrespondence(
                before.flags,plane,normalError,offsetError);
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
        if (m8FlowerR1Changed != 0u)
            InterlockedOr(_M8Counters[M8_COUNTER_OBSERVATION_CHANGE_MASK],
                M8_OBSERVATION_CHANGED_R1);
        _M8TileRecords[M8TileRuntimeIndex(slot)].x=_M8ObservationToken;
    }
}

#endif
