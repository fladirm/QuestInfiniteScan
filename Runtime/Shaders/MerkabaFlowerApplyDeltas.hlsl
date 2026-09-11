// Presentation only. A dirty page takes already-resolved owner replacements;
// cold/missing replacements request the separate canonical rebuild entry.
#define M8_FLOWER_BATCH_ITEM_REBUILD 7u

void M8FlowerRefreshDirtyHints(uint group,uint word)
{
    uint ignored;
    if(word<1024u)
    {
        _M8FlowerPageDirectory.InterlockedAnd(M8_FLOWER_PAGE_DIRTY_SUMMARY+4u*group,
            ~(1u<<(word&31u)),ignored);
        DeviceMemoryBarrier();
        if(_M8FlowerPageDirectory.Load(M8_FLOWER_PAGE_DIRTY_BITS+4u*word)!=0u)
            _M8FlowerPageDirectory.InterlockedOr(M8_FLOWER_PAGE_DIRTY_SUMMARY+4u*group,
                1u<<(word&31u),ignored);
    }
    _M8FlowerPageDirectory.InterlockedAnd(0u,~(1u<<group),ignored);
    DeviceMemoryBarrier();
    if(_M8FlowerPageDirectory.Load(M8_FLOWER_PAGE_DIRTY_SUMMARY+4u*group)!=0u)
        _M8FlowerPageDirectory.InterlockedOr(0u,1u<<group,ignored);
}

bool M8FlowerClaimDirtyPage(uint seed,out uint slot)
{
    slot=0xffffffffu;
    [loop]for(uint attempt=0u;attempt<32u;attempt++)
    {
        uint root=_M8FlowerPageDirectory.Load(0u);
        if(root==0u)return false;
        uint group=M8FlowerSelectNode(root,(seed+attempt)%countbits(root));
        uint summary=_M8FlowerPageDirectory.Load(M8_FLOWER_PAGE_DIRTY_SUMMARY+4u*group);
        if(summary==0u){M8FlowerRefreshDirtyHints(group,1024u);continue;}
        uint word=32u*group+M8FlowerSelectNode(summary,(seed+attempt)%countbits(summary));
        uint bits=_M8FlowerPageDirectory.Load(M8_FLOWER_PAGE_DIRTY_BITS+4u*word);
        if(bits==0u){M8FlowerRefreshDirtyHints(group,word);continue;}
        uint bit=(uint)firstbitlow(bits),previous;
        _M8FlowerPageDirectory.InterlockedAnd(M8_FLOWER_PAGE_DIRTY_BITS+4u*word,~(1u<<bit),previous);
        if((previous&(1u<<bit))==0u)continue;
        slot=32u*word+bit;
        // Hints may race a producer OR. Clear then recheck the lower level:
        // stale-positive hints are allowed, lost dirty pages are not.
        M8FlowerRefreshDirtyHints(group,word);
        return true;
    }
    return false;
}

// No root equation, alternative enumeration or carrier classifier here.
// 512 implicit owners map to lanes; each lane visits only cached actual symbols.
uint2 M8FlowerApplyCachedOwner(uint slot,uint local,bool emit,uint2 first)
{
    uint2 count=0u;
    uint flags=M8LoadKernelStateRead(slot,local).flags;
    if((flags&(M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID|M8_FLOWER_SEED_FLAG))!=
        (M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID))
    {
        if(!emit)M8FlowerDropCachedOwner(slot,local);
        return count;
    }
    if(!emit)InterlockedAdd(m8FlowerCacheActiveOwners,1u);
    uint owner=M8FlowerFindOwner(slot,local,m8FlowerCompileSlotGeneration);
    uint4 mask;
    uint snapshot=M8FlowerReadOwnerSnapshot(slot,local,m8FlowerCompileSlotGeneration,
        flags,M8FlowerGetOwnerEpoch(owner),_M8FlowerPlaneErrorBounds,mask);
    if(snapshot==0u){InterlockedOr(m8FlowerCacheMissing,1u);return count;}
    uint carriers=countbits(mask.x)+countbits(mask.y)+countbits(mask.z)+countbits(mask.w);
    uint color=M8LoadKernelStateRead(slot,local).packedColor;
    float3 rgbColor=float3(color&255u,(color>>8u)&255u,(color>>16u)&255u)*(1.0/255.0);
    [loop]for(uint item=0u;item<carriers;item++)
    {
        uint4 packed=M8FlowerCachedCarrier(snapshot,item);
        M8FlowerSymbolRecord symbol;
        symbol.OwnerAndCarrier=packed.x;symbol.RootsAndWedges=packed.y;
        symbol.DetailRef=owner;symbol.ThreadRef=0xffffffffu;
        uint carrier=M8FlowerDrawCarrierId(symbol),active=M8FlowerDrawActiveWedgeMask(symbol);
        uint signs=M8FlowerDrawRootSigns(symbol),sector=M8FlowerDrawHubSector(symbol),bytes;
        M8ThreadRun rgb;M8FlowerSkinMetricRun metric;uint4 signal;
        if(!M8FlowerPrepareCarrierSkin(owner,carrier,active,signs,sector,bytes,rgb,metric,signal))
        {InterlockedOr(m8FlowerCompileStatus,M8_FLOWER_ARENA_INVALID);continue;}
        if(emit)
        {
            uint sampleBase=m8FlowerDeltaBuild.SampleAddress+first.y+count.y;
            uint sampleCount=bytes==0u?0u:(bytes-64u)/64u;
            uint key=M8FlowerCarrierSkinKey(carrier,signs,sector);
            [loop]for(uint sampleIndex=0u;sampleIndex<sampleCount;sampleIndex++)
            {
                uint level;uint3 child;
                M8FlowerSkinSampleLocality(signal.xy,sampleIndex,level,child);
                M8FlowerSkinDrawSample sample;
                if(M8FlowerCompileSkinRegion(rgbColor,signal.z,key,level,child,
                    rgb,(signal.w&1u)!=0u,metric,(signal.w&2u)!=0u,sample))
                    M8FlowerStoreSkinDrawSample(sampleBase+64u*(sampleIndex+1u),sample);
                else InterlockedOr(m8FlowerCompileStatus,M8_FLOWER_ARENA_INVALID);
            }
            if(bytes!=0u)
            {
                _M8ThreadAtlasPages.Store4(sampleBase,uint4(signal.xy,(sampleBase+64u)/64u,signal.z));
                symbol.ThreadRef=sampleBase;
            }
            M8FlowerStoreSymbol(m8FlowerDeltaBuild.SymbolAddress,first.x+count.x,symbol);
        }
        else InterlockedAdd(m8FlowerCompileTriangles,countbits(active));
        count+=uint2(1u,bytes);
    }
    return count;
}

[numthreads(256,1,1)]
void RebuildDirtyFlowerOwners(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    if(group.x>=M8_FLOWER_BATCH_CAPACITY)return;
    uint record=M8FlowerBatchRecord(group.x);
    uint4 identity=_M8FlowerPageDirectory.Load4(record);
    if(identity.w!=M8_FLOWER_BATCH_ITEM_REBUILD)return;
    uint slot=identity.x;
    if(lane==0u)
    {
        m8FlowerCompileStatus=M8_FLOWER_ARENA_OK;
        m8FlowerCompileSlotGeneration=slot<32768u?M8LoadTileRuntimeRead(slot).w:0u;
    }
    GroupMemoryBarrierWithGroupSync();
    if(slot>=32768u || m8FlowerCompileSlotGeneration!=identity.y)
    {
        if(lane==0u)M8FlowerSetBatchState(group.x,M8_FLOWER_BATCH_ITEM_ABORT);
        return;
    }
    M8FlowerCacheTileHalo(slot,lane,true);
    if(lane<16u)m8FlowerActiveOwners[lane]=0u;
    GroupMemoryBarrierWithGroupSync();
    [unroll]for(uint half=0u;half<2u;half++)
    {
        uint local=lane+256u*half,flags=M8LoadKernelStateRead(slot,local).flags;
        uint required=M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID;
        if((flags&(required|M8_FLOWER_SEED_FLAG))!=required)continue;
        uint4 mask;
        if(M8FlowerReadOwnerSnapshot(slot,local,identity.y,flags,
            M8FlowerGetOwnerEpoch(M8FlowerFindOwner(slot,local,identity.y)),
            _M8FlowerPlaneErrorBounds,mask)==0u)
            InterlockedOr(m8FlowerActiveOwners[local>>5u],1u<<(local&31u));
    }
    GroupMemoryBarrierWithGroupSync();
    [loop]for(uint word=0u;word<16u;word++)
    {
        uint remaining=m8FlowerActiveOwners[word];
        [loop]while(remaining!=0u)
        {
            uint local=32u*word+(uint)firstbitlow(remaining);remaining&=remaining-1u;
            M8FlowerRebuildOwnerSnapshot(slot,local,lane);
        }
    }
    M8FlowerRequestPageEndpoints(lane);
    DeviceMemoryBarrierWithGroupSync();
    if(lane==0u)
    {
        if(m8FlowerHaloUnresolvedReads!=0u)m8FlowerCompileStatus=M8_FLOWER_ARENA_BUSY;
        _M8FlowerPageDirectory.Store(record+52u,m8FlowerCompileStatus);
        M8FlowerSetBatchState(group.x,m8FlowerCompileStatus==M8_FLOWER_ARENA_OK?
            M8_FLOWER_BATCH_ITEM_CLAIMED:M8_FLOWER_BATCH_ITEM_ABORT);
    }
}

[numthreads(256,1,1)]
void ApplyOwnerDrawDeltas(uint3 group:SV_GroupID,uint lane:SV_GroupIndex)
{
    if(group.x>=M8_FLOWER_BATCH_CAPACITY)return;
    uint record=M8FlowerBatchRecord(group.x);
    if(lane==0u)
    {
        m8FlowerCompileSlot=0xffffffffu;m8FlowerCompileStatus=M8_FLOWER_ARENA_OK;
        m8FlowerCompileTriangles=0u;m8FlowerCacheMissing=0u;
        m8FlowerCacheActiveOwners=0u;m8FlowerCacheRetire=0xffffffffu;
        m8FlowerDeltaBuild=(M8FlowerPageBuild)0;
        if(group.x==0u)
        {
            _M8FlowerPageDirectory.Store(M8_FLOWER_BATCH_COUNT,M8_FLOWER_BATCH_CAPACITY);
            _M8Counters[M8_COUNTER_READOUT_EMITTED_TRIANGLES]=0u;
            _M8Counters[M8_COUNTER_READOUT_EMITTED_VERTICES]=0u;
        }
        uint4 identity=_M8FlowerPageDirectory.Load4(record);
        if(identity.w==0u || identity.w==M8_FLOWER_BATCH_ITEM_RETIRED)
        {
            uint slot;
            if(M8FlowerClaimDirtyPage(group.x,slot))
            {
                identity=uint4(slot,M8LoadTileRuntimeRead(slot).w,
                    _M8FlowerPageDirectory.Load(M8_FLOWER_PAGE_AUX_BASE+32u*slot),M8_FLOWER_BATCH_ITEM_CLAIMED);
                _M8FlowerPageDirectory.Store4(record,identity);
                _M8FlowerPageDirectory.Store4(record+16u,0u.xxxx);
                _M8FlowerPageDirectory.Store4(record+32u,0u.xxxx);
                _M8FlowerPageDirectory.Store4(record+48u,0u.xxxx);
            }
        }
        if(identity.w==M8_FLOWER_BATCH_ITEM_CLAIMED)
        {
            m8FlowerCompileSlot=identity.x;m8FlowerCompileSlotGeneration=identity.y;
            m8FlowerCompileGeneration=identity.z;
            uint4 meta=M8LoadTileMetaRead(identity.x);
            if(identity.y==0u || M8LoadTileRuntimeRead(identity.x).w!=identity.y ||
                meta.x>=MERKABA_M8_CHUNK_CAPACITY || meta.y>=64u ||
                _M8ChunkTileRefsRead[meta.x*64u+meta.y]!=identity.x+1u ||
                !M8FlowerPageSourceUnchanged(identity.x,identity.z))
            {
                _M8FlowerPageDirectory.Store(record+52u,M8_FLOWER_ARENA_INVALID);
                M8FlowerSetBatchState(group.x,M8_FLOWER_BATCH_ITEM_ABORT);
                // A retired physical tile has no live cache consumer. A stale
                // receipt for a reused HOT slot must not retire its new cache.
                if(M8LoadTileRuntimeRead(identity.x).w==identity.y &&
                    (meta.x>=MERKABA_M8_CHUNK_CAPACITY || meta.y>=64u ||
                    _M8ChunkTileRefsRead[meta.x*64u+meta.y]!=identity.x+1u))
                    m8FlowerCacheRetire=identity.x;
                m8FlowerCompileSlot=0xffffffffu;
            }
            else if(_M8FlowerPageDirectory.Load(M8_FLOWER_PAGE_AUX_BASE+32u*identity.x+28u)!=identity.y)
                M8FlowerSetPageResidency(identity.x,
                    M8FlowerClassifyTileDistance(M8GlobalKernelCoordRead(identity.x,0u)));
        }
    }
    GroupMemoryBarrierWithGroupSync();
    if(m8FlowerCacheRetire<32768u)
    {
        M8FlowerDropCachedOwner(m8FlowerCacheRetire,lane);
        M8FlowerDropCachedOwner(m8FlowerCacheRetire,lane+256u);
        DeviceMemoryBarrierWithGroupSync();
        if(lane==0u)M8FlowerDropEmptyCacheIndex(m8FlowerCacheRetire);
        return;
    }
    uint slot=m8FlowerCompileSlot;
    if(slot>=32768u)return;
    [loop]for(uint stage=0u;stage<2u;stage++)
    {
        [unroll]for(uint half=0u;half<2u;half++)
        {
            uint local=lane+256u*half;
            uint2 first=stage==0u?uint2(0u,0u):m8FlowerPageOwnerSpans[local];
            uint2 count=M8FlowerApplyCachedOwner(slot,local,stage!=0u,first);
            if(stage==0u)m8FlowerPageOwnerSpans[local]=count;
        }
        DeviceMemoryBarrierWithGroupSync();
        if(stage==0u)
        {
            M8FlowerPrefixOwners(lane);
            if(lane==0u)
            {
                if(m8FlowerCacheActiveOwners==0u)M8FlowerDropEmptyCacheIndex(slot);
                if(m8FlowerCacheMissing!=0u)
                {
                    M8FlowerSetBatchState(group.x,M8_FLOWER_BATCH_ITEM_REBUILD);
                    uint ignored;
                    _M8FlowerIndirectCommands.InterlockedMax(M8_FLOWER_BATCH_DISPATCH_ARGS,group.x+1u,ignored);
                }
                else if(m8FlowerCompileStatus==M8_FLOWER_ARENA_OK)
                    m8FlowerCompileStatus=M8FlowerBeginPage(slot,m8FlowerCompileSlotGeneration,
                        m8FlowerCompileGeneration,m8FlowerCompileTotal.x,m8FlowerCompileTotal.y,m8FlowerDeltaBuild);
                M8FlowerStoreBatchBuild(group.x,m8FlowerDeltaBuild);
                _M8FlowerPageDirectory.Store4(record,uint4(slot,m8FlowerCompileSlotGeneration,
                    m8FlowerCompileGeneration,_M8FlowerPageDirectory.Load(record+12u)));
                _M8FlowerPageDirectory.Store4(record+16u,uint4(m8FlowerCompileTotal,0u,m8FlowerCompileTriangles));
            }
            GroupMemoryBarrierWithGroupSync();
            if(m8FlowerCacheMissing!=0u)return;
            if(m8FlowerCompileStatus!=M8_FLOWER_ARENA_OK)break;
        }
    }
    if(lane==0u)
    {
        if(m8FlowerCompileStatus==M8_FLOWER_ARENA_OK &&
            !M8FlowerFinishPendingPage(m8FlowerDeltaBuild,M8GlobalKernelCoordRead(slot,0u)>>3,
                m8FlowerCompileTotal.x,m8FlowerCompileTotal.y/64u))
            m8FlowerCompileStatus=M8_FLOWER_ARENA_INVALID;
        _M8FlowerPageDirectory.Store(record+52u,m8FlowerCompileStatus);
        M8FlowerSetBatchState(group.x,m8FlowerCompileStatus==M8_FLOWER_ARENA_OK?
            M8_FLOWER_BATCH_ITEM_READY:M8_FLOWER_BATCH_ITEM_ABORT);
    }
}
