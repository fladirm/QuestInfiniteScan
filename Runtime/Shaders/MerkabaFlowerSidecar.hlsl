#ifndef GENESIS_MERKABA_FLOWER_SIDECAR_INCLUDED
#define GENESIS_MERKABA_FLOWER_SIDECAR_INCLUDED

#include "MerkabaSphereFlower.generated.hlsl"
#include "MerkabaFlowerArena.hlsl"

// The matching C# constants are MerkabaFlowerGpuLayout. Only canonical epoch,
// record, run and group payloads cross persistence; these resident indices,
// capacities, locks and frozen-work cursors are never serialized.
#define M8_FLOWER_TILE_DIRECTORY 64u
#define M8_FLOWER_TILE_DIRECTORY_STRIDE 16u
#define M8_FLOWER_OWNER_INDEX_BYTES 2048u
#define M8_FLOWER_OWNER_HEADER_BYTES 64u
#define M8_FLOWER_PERSISTENT_ORDER 17u
#define M8_FLOWER_PERSISTENT_BYTES 33554432u
#define M8_FLOWER_DRAW_ORDER 18u
#define M8_FLOWER_ALIGN256(n) (((n)+255u)&~255u)
#define M8_FLOWER_ARENA_META_BYTES(o) (32u+((1u<<((o)+1u))-1u)*4u)
#define M8_FLOWER_DETAIL_CONTROL (64u+32768u*16u)
#define M8_FLOWER_DETAIL_DATA M8_FLOWER_ALIGN256(M8_FLOWER_DETAIL_CONTROL+M8_FLOWER_ARENA_META_BYTES(17u))
#define M8_FLOWER_THREAD_CONTROL 0u
#define M8_FLOWER_THREAD_DATA M8_FLOWER_ALIGN256(M8_FLOWER_ARENA_META_BYTES(17u))
#define M8_FLOWER_DRAW_CONTROL (M8_FLOWER_THREAD_DATA+M8_FLOWER_PERSISTENT_BYTES)
#define M8_FLOWER_DRAW_DATA M8_FLOWER_ALIGN256(M8_FLOWER_DRAW_CONTROL+M8_FLOWER_ARENA_META_BYTES(18u))
#define M8_FLOWER_SIDECAR_STALE_SLOT 4u
#define M8_FLOWER_RESIDENT_RUN_STRIDE 32u

// Observation-transaction receipt in the formerly unused owner cursor.
// +48 is the frozen scheduling token, +52 retains the original phase read
// set BEFORE an epoch change/removal. Neither field is a persistent key.
// +60 keeps its two existing history bits; bits2..21 acknowledge the exact
// twenty original R2/R3 peers, bit22 acknowledges this owner's local cut.
#define M8_FLOWER_INVALIDATION_ROOTS 0x000fffffu
#define M8_FLOWER_INVALIDATION_SOURCE (1u<<28u)
#define M8_FLOWER_INVALIDATION_LOCAL_PENDING (1u<<29u)
#define M8_FLOWER_INVALIDATION_PEERS_PENDING (1u<<30u)
#define M8_FLOWER_INVALIDATION_THROUGH (1u<<31u)
#define M8_FLOWER_INVALIDATION_LOCAL_DONE (1u<<22u)

#ifndef M8_FLOWER_SRV
#define M8_FLOWER_SRV(registerName)
#endif
ByteAddressBuffer _M8FlowerDetailPagesRead M8_FLOWER_SRV(t7);
ByteAddressBuffer _M8ThreadAtlasPagesRead M8_FLOWER_SRV(t8);
#if defined(M8_FLOWER_SIDECAR_WRITE)
RWByteAddressBuffer _M8FlowerDetailPages;
#define M8_FLOWER_DETAIL_SOURCE _M8FlowerDetailPages
#else
#define M8_FLOWER_DETAIL_SOURCE _M8FlowerDetailPagesRead
#endif
#if defined(M8_FLOWER_SIDECAR_WRITE) || defined(M8_FLOWER_THREAD_DRAW_WRITE)
RWByteAddressBuffer _M8ThreadAtlasPages;
#define M8_FLOWER_THREAD_SOURCE _M8ThreadAtlasPages
#else
#define M8_FLOWER_THREAD_SOURCE _M8ThreadAtlasPagesRead
#endif

M8FlowerArena M8FlowerDetailArena()
{
    M8FlowerArena p;
    p.Control=M8_FLOWER_DETAIL_CONTROL; p.Data=M8_FLOWER_DETAIL_DATA;
    p.MaxOrder=M8_FLOWER_PERSISTENT_ORDER;
    return p;
}
M8FlowerArena M8FlowerThreadArena()
{
    M8FlowerArena p;
    p.Control=M8_FLOWER_THREAD_CONTROL; p.Data=M8_FLOWER_THREAD_DATA;
    p.MaxOrder=M8_FLOWER_PERSISTENT_ORDER;
    return p;
}
M8FlowerArena M8FlowerDrawArena()
{
    M8FlowerArena p;
    p.Control=M8_FLOWER_DRAW_CONTROL; p.Data=M8_FLOWER_DRAW_DATA;
    p.MaxOrder=M8_FLOWER_DRAW_ORDER;
    return p;
}

bool M8FlowerDetailRange(uint address, uint bytes)
{
    return address>=M8_FLOWER_DETAIL_DATA && bytes<=M8_FLOWER_PERSISTENT_BYTES &&
        address-M8_FLOWER_DETAIL_DATA<=M8_FLOWER_PERSISTENT_BYTES-bytes;
}
bool M8FlowerThreadRange(uint address, uint bytes)
{
    return address>=M8_FLOWER_THREAD_DATA && bytes<=M8_FLOWER_PERSISTENT_BYTES &&
        address-M8_FLOWER_THREAD_DATA<=M8_FLOWER_PERSISTENT_BYTES-bytes;
}

uint M8FlowerFindOwner(uint slot, uint kernelLocal, uint slotGeneration)
{
    if (slot>=32768u || kernelLocal>=512u || slotGeneration==0u) return 0u;
    uint2 tile=M8_FLOWER_DETAIL_SOURCE.Load2(M8_FLOWER_TILE_DIRECTORY+16u*slot);
    if (tile.y!=slotGeneration || !M8FlowerDetailRange(tile.x,2048u)) return 0u;
    uint owner=M8_FLOWER_DETAIL_SOURCE.Load(tile.x+4u*kernelLocal);
    if (!M8FlowerDetailRange(owner,64u) ||
        M8_FLOWER_DETAIL_SOURCE.Load(owner)!=kernelLocal) return 0u;
    return owner;
}
uint M8FlowerGetOwnerEpoch(uint ownerRef)
{
    return M8FlowerDetailRange(ownerRef,64u)
        ? M8_FLOWER_DETAIL_SOURCE.Load(ownerRef+4u) : 0u;
}
uint M8FlowerGetOwnerEpoch(uint slot, uint kernelLocal, uint slotGeneration)
{
    return M8FlowerGetOwnerEpoch(M8FlowerFindOwner(slot,kernelLocal,slotGeneration));
}

uint M8FlowerFindPhaseIndex(uint ownerRef, uint key, out uint first, out uint count)
{
    first=0u; count=0u;
    if (!M8FlowerDetailRange(ownerRef,64u)) return 0xffffffffu;
    first=M8_FLOWER_DETAIL_SOURCE.Load(ownerRef+8u);
    count=M8_FLOWER_DETAIL_SOURCE.Load(ownerRef+12u);
    if (count>M8_FLOWER_PERSISTENT_BYTES/16u ||
        (count!=0u && !M8FlowerDetailRange(first,count*16u))) return 0xffffffffu;
    uint lo=0u,hi=count;
    [loop] while(lo<hi)
    {
        uint mid=lo+((hi-lo)>>1u);
        if (M8_FLOWER_DETAIL_SOURCE.Load(first+16u*mid)<key) lo=mid+1u;
        else hi=mid;
    }
    return lo;
}
bool M8FlowerFindPhase(uint ownerRef,uint key,uint epoch,out M8FlowerDetailRecord record)
{
    record=(M8FlowerDetailRecord)0;
    if (epoch==0u || M8FlowerGetOwnerEpoch(ownerRef)!=epoch) return false;
    uint first,count;
    uint index=M8FlowerFindPhaseIndex(ownerRef,key,first,count);
    if (index>=count) return false;
    uint4 row=M8_FLOWER_DETAIL_SOURCE.Load4(first+16u*index);
    if (row.x!=key || row.w!=epoch || asint(row.y)>asint(row.z)) return false;
    record.Key=row.x;record.Lower=asint(row.y);record.Upper=asint(row.z);record.ParentEpoch=row.w;
    return true;
}

// Resident run rows append only an allocation address/capacity to the exact
// 24-byte canonical payload. GroupBase retains group-index semantics: bytes
// are GroupBase*56 for V and GroupBase*112 for RGB, not a second identity.
bool M8FlowerReadRunSpan(uint ownerRef,bool thread,out uint first,out uint count)
{
    first=0u;count=0u;
    if(!M8FlowerDetailRange(ownerRef,64u))return false;
    uint ownerOffset=thread?32u:20u;
    first=M8_FLOWER_DETAIL_SOURCE.Load(ownerRef+ownerOffset);
    count=M8_FLOWER_DETAIL_SOURCE.Load(ownerRef+ownerOffset+4u);
    if(count>M8_FLOWER_PERSISTENT_BYTES/32u)return false;
    if (count!=0u && (thread ? !M8FlowerThreadRange(first,count*32u) :
        !M8FlowerDetailRange(first,count*32u)))return false;
    return true;
}
uint M8FlowerFindRunIndex(uint ownerRef,uint key,bool thread,out uint first,out uint count)
{
    first=0u;count=0u;
    if(!M8FlowerL2KeyValid(key) || !M8FlowerReadRunSpan(ownerRef,thread,first,count))
        return 0xffffffffu;
    uint lo=0u,hi=count;
    [loop] while(lo<hi)
    {
        uint mid=lo+((hi-lo)>>1u);
        uint found;
        if(thread) found=M8_FLOWER_THREAD_SOURCE.Load(first+32u*mid);
        else found=M8_FLOWER_DETAIL_SOURCE.Load(first+32u*mid);
        if(found<key)lo=mid+1u;else hi=mid;
    }
    return lo;
}
bool M8FlowerFindMetricRun(uint ownerRef,uint key,uint epoch,
    out M8FlowerSkinMetricRun run,out uint residentRef)
{
    run=(M8FlowerSkinMetricRun)0;residentRef=0u;
    if(epoch==0u || M8FlowerGetOwnerEpoch(ownerRef)!=epoch)return false;
    uint first,count;uint index=M8FlowerFindRunIndex(ownerRef,key,false,first,count);
    if(index>=count)return false;
    residentRef=first+32u*index;
    uint4 a=M8_FLOWER_DETAIL_SOURCE.Load4(residentRef);
    uint2 b=M8_FLOWER_DETAIL_SOURCE.Load2(residentRef+16u);
    if(a.x!=key || b.x!=epoch)return false;
    run.FlowerKey=a.x;run.GroupBase=a.y;run.SplitBitsLo=a.z;run.SplitBitsHi=a.w;
    run.ParentEpoch=b.x;run.Reserved=b.y;
    return true;
}
bool M8FlowerFindThreadRun(uint ownerRef,uint key,uint epoch,
    out M8ThreadRun run,out uint residentRef)
{
    run=(M8ThreadRun)0;residentRef=0u;
    if(epoch==0u || M8FlowerGetOwnerEpoch(ownerRef)!=epoch)return false;
    uint first,count;uint index=M8FlowerFindRunIndex(ownerRef,key,true,first,count);
    if(index>=count)return false;
    residentRef=first+32u*index;
    uint4 a=M8_FLOWER_THREAD_SOURCE.Load4(residentRef);
    uint2 b=M8_FLOWER_THREAD_SOURCE.Load2(residentRef+16u);
    if(a.x!=key || b.y!=epoch)return false;
    run.FlowerKey=a.x;run.ProgramRef=a.y;run.GroupBase=a.z;run.SplitBitsLo=a.w;
    run.SplitBitsHi=b.x;run.ParentEpoch=b.y;
    return true;
}
uint2 M8FlowerInvalidationReceipt(uint ownerRef)
{
    return M8FlowerDetailRange(ownerRef,64u)?
        M8_FLOWER_DETAIL_SOURCE.Load2(ownerRef+48u):0u.xx;
}
bool M8FlowerOriginalInvalidationPending(uint ownerRef,uint node)
{
    uint2 receipt=M8FlowerInvalidationReceipt(ownerRef);
    if(receipt.x==0u)return false;
    if((receipt.y&M8_FLOWER_INVALIDATION_LOCAL_PENDING)!=0u)return true;
    return node>=6u && node<26u &&
        (receipt.y&M8_FLOWER_INVALIDATION_PEERS_PENDING)!=0u &&
        (receipt.y&(1u<<(node-6u)))!=0u;
}
uint M8FlowerTilePendingCursor(uint slot,uint slotGeneration,uint observationToken)
{
    if(slot>=32768u || slotGeneration==0u)return 0u;
    uint4 tile=M8_FLOWER_DETAIL_SOURCE.Load4(M8_FLOWER_TILE_DIRECTORY+16u*slot);
    return tile.y==slotGeneration && tile.z==observationToken ? tile.w : 0u;
}

// ProgramRef retains its canonical 48-byte record index. Allocation metadata
// and reference counts live in the resident block prefix, never in the record.
bool M8FlowerOpticalAllocation(uint programRef,bool readOnly,out uint allocation)
{
    allocation=0u;
    if(programRef==0xffffffffu || programRef>0xffffffffu/48u)return false;
    uint address=programRef*48u;
    allocation=address&~255u;
    if(!M8FlowerThreadRange(allocation,256u) || address<allocation+16u ||
        address-allocation>208u)return false;
    uint4 header;
    if(readOnly)header=_M8ThreadAtlasPagesRead.Load4(allocation);
    else header=M8_FLOWER_THREAD_SOURCE.Load4(allocation);
    return header.x!=0u && header.y==programRef && header.z==256u;
}
bool M8FlowerLogicalProgramRef(uint programRef,out uint logicalProgramRef,bool readOnly)
{
    logicalProgramRef=0xffffffffu;
    uint allocation;
    if(!M8FlowerOpticalAllocation(programRef,readOnly,allocation))return false;
    if(readOnly)logicalProgramRef=_M8ThreadAtlasPagesRead.Load(allocation+12u);
    else logicalProgramRef=M8_FLOWER_THREAD_SOURCE.Load(allocation+12u);
    return logicalProgramRef!=0xffffffffu;
}

// Pure thread-order addressing is shared by canonical split writers and
// derived draw compaction; it does not require writable FlowerDetail.
uint M8FlowerSplitRank(uint2 bits,uint ordinal)
{
    if(ordinal<32u)
        return countbits(bits.x&((1u<<ordinal)-1u));
    return countbits(bits.x)+countbits(bits.y&((1u<<(ordinal-32u))-1u));
}

#if defined(M8_FLOWER_SIDECAR_WRITE)
bool M8FlowerRetainOpticalLocked(uint programRef)
{
    uint allocation;
    if(!M8FlowerOpticalAllocation(programRef,false,allocation))return false;
    uint count=_M8ThreadAtlasPages.Load(allocation);
    if(count==0u || count==0xffffffffu)return false;
    _M8ThreadAtlasPages.Store(allocation,count+1u);
    return true;
}
bool M8FlowerReleaseOpticalLocked(uint programRef)
{
    uint allocation;
    if(!M8FlowerOpticalAllocation(programRef,false,allocation))return false;
    uint count=_M8ThreadAtlasPages.Load(allocation);
    if(count==0u)return false;
    if(count>1u){_M8ThreadAtlasPages.Store(allocation,count-1u);return true;}
    _M8ThreadAtlasPages.Store(allocation,0u);
    return M8FlowerArenaFreeLocked(_M8ThreadAtlasPages,M8FlowerThreadArena(),allocation,256u);
}

// Storage may restore epoch history after R1 deletion. This allocates only an
// epoch/index entry; it does not authorize observing new descendants.
uint M8FlowerEnsureOwnerStorage(uint slot,uint kernelLocal,uint slotGeneration,
    uint publishing,out uint ownerRef)
{
    ownerRef=0u;
    if(slot>=32768u || kernelLocal>=512u || slotGeneration==0u || publishing==0u)
        return M8_FLOWER_ARENA_INVALID;
    M8FlowerArena pool=M8FlowerDetailArena();
    if(!M8FlowerArenaAcquire(_M8FlowerDetailPages,pool))return M8_FLOWER_ARENA_BUSY;
    uint directory=M8_FLOWER_TILE_DIRECTORY+16u*slot;
    uint2 tile=_M8FlowerDetailPages.Load2(directory);
    uint result=M8_FLOWER_ARENA_OK;
    if(tile.x!=0u && tile.y!=slotGeneration)result=M8_FLOWER_SIDECAR_STALE_SLOT;
    if(result==M8_FLOWER_ARENA_OK && tile.x==0u)
    {
        uint capacity;
        result=M8FlowerArenaAllocateLocked(_M8FlowerDetailPages,pool,2048u,tile.x,capacity);
        if(result==M8_FLOWER_ARENA_OK)
        {
            [loop]for(uint i=0u;i<512u;i++)_M8FlowerDetailPages.Store(tile.x+4u*i,0u);
            tile.y=slotGeneration;
            DeviceMemoryBarrier();
            _M8FlowerDetailPages.Store2(directory,tile);
        }
    }
    if(result==M8_FLOWER_ARENA_OK)
    {
        ownerRef=_M8FlowerDetailPages.Load(tile.x+4u*kernelLocal);
        if(ownerRef==0u)
        {
            uint capacity;
            result=M8FlowerArenaAllocateLocked(_M8FlowerDetailPages,pool,64u,ownerRef,capacity);
            if(result==M8_FLOWER_ARENA_OK)
            {
                [unroll]for(uint i=0u;i<16u;i++)_M8FlowerDetailPages.Store(ownerRef+4u*i,0u);
                _M8FlowerDetailPages.Store2(ownerRef,uint2(kernelLocal,1u));
                _M8FlowerDetailPages.Store(ownerRef+44u,publishing);
                _M8FlowerDetailPages.Store(ownerRef+56u,capacity);
                DeviceMemoryBarrier();
                _M8FlowerDetailPages.Store(tile.x+4u*kernelLocal,ownerRef);
            }
            else ownerRef=0u;
        }
    }
    M8FlowerArenaRelease(_M8FlowerDetailPages,pool);
    return result;
}
uint M8FlowerEnsureOwner(uint slot,uint kernelLocal,uint slotGeneration,
    uint canonicalFlags,uint publishing,out uint ownerRef)
{
    ownerRef=0u;
    if((canonicalFlags&(M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID|M8_FLOWER_SEED_FLAG))!=
        (M8_FLOWER_OCCUPIED_FLAG|M8_FLOWER_PLANE_VALID))return M8_FLOWER_ARENA_INVALID;
    return M8FlowerEnsureOwnerStorage(slot,kernelLocal,slotGeneration,publishing,ownerRef);
}

bool M8FlowerStoreTilePendingCursor(uint slot,uint slotGeneration,uint observationToken,uint cursor)
{
    if(slot>=32768u || slotGeneration==0u || observationToken==0u)return false;
    uint address=M8_FLOWER_TILE_DIRECTORY+16u*slot;
    uint2 tile=_M8FlowerDetailPages.Load2(address);
    if(tile.x!=0u && tile.y!=slotGeneration)return false;
    _M8FlowerDetailPages.Store(address+4u,slotGeneration);
    _M8FlowerDetailPages.Store2(address+8u,uint2(observationToken,cursor));
    return true;
}

// Caller owns the single serialized raw-reader lease. A previous published
// generation cannot be overwritten/reclaimed until it is actually retired.
bool M8FlowerOwnerWritable(uint ownerRef,uint publishing,uint retired)
{
    if(!M8FlowerDetailRange(ownerRef,64u) || publishing==0u)return false;
    uint generation=_M8FlowerDetailPages.Load(ownerRef+44u);
    return generation<=retired || generation==publishing;
}

// This is an address inverse of the actual phase ancestry, not an inference
// from a nearby root, the skin hub sign or a sector owner. Fine predictions
// consult this original shared source only when its local phase is present.
bool M8FlowerPhaseOriginalNode(uint index,out uint node)
{
    node=0xffffffffu;
    if(index<20u){node=index+6u;return true;}
    uint strand;
    if(index<92u)strand=index-20u;
    else
    {
        if(index>=668u)return false;
        uint ordinal=index-92u,petal=ordinal/12u;
        uint parent=(ordinal/3u)&3u,edge=ordinal%3u;
        uint level,lineClass;int3 offset;int endpoint,phase,inherited;
        if(!M8FlowerTryGetChildPhaseLoop(petal,parent+1u,edge+3u,
            level,offset,lineClass,strand,endpoint,phase,inherited) ||
            level!=2u || inherited>=0)return false;
    }
    M8FlowerPhaseFamilyRule family=M8FlowerGetPhaseFamily(strand);
    node=family.RootNode;
    return node>=6u && node<26u;
}

bool M8FlowerReadOriginalPhasePresence(uint ownerRef,out uint mask)
{
    mask=0u;
    uint first,count;
    if(M8FlowerFindPhaseIndex(ownerRef,0u,first,count)==0xffffffffu)return false;
    uint epoch=M8FlowerGetOwnerEpoch(ownerRef);
    if(epoch==0u)return false;
    [loop]for(uint index=0u;index<count;index++)
    {
        uint4 record=_M8FlowerDetailPages.Load4(first+16u*index);
        if(record.w!=epoch || ((record.x>>16u)&7u)>1u)continue;
        uint dependency;
        if(asint(record.y)>asint(record.z) ||
            !M8FlowerTryPhaseDependencyIndex(record.x,dependency))return false;
        if(dependency<20u)mask|=1u<<dependency;
    }
    return true;
}

// Caller owns the source owner, or the existing detail-arena lease for a
// receiver outside the touched set. Capture MUST precede every mutation of
// the phase span or epoch. Re-entry retains that immutable pre-mutation set.
uint M8FlowerCaptureInvalidation(uint ownerRef,uint observationToken,
    bool through,bool structural,uint publishing,uint retired,out uint roots)
{
    roots=0u;
    if(observationToken==0u || !M8FlowerDetailRange(ownerRef,64u))
        return M8_FLOWER_ARENA_INVALID;
    if(!M8FlowerOwnerWritable(ownerRef,publishing,retired))return M8_FLOWER_ARENA_BUSY;
    uint2 receipt=_M8FlowerDetailPages.Load2(ownerRef+48u);
    if(receipt.x!=observationToken)
    {
        // An unfinished older transaction may not be silently overwritten.
        if((receipt.y&(M8_FLOWER_INVALIDATION_LOCAL_PENDING|
            M8_FLOWER_INVALIDATION_PEERS_PENDING))!=0u)return M8_FLOWER_ARENA_BUSY;
        if(!M8FlowerReadOriginalPhasePresence(ownerRef,roots))return M8_FLOWER_ARENA_INVALID;
        receipt=uint2(observationToken,roots);
        uint history=_M8FlowerDetailPages.Load(ownerRef+60u);
        _M8FlowerDetailPages.Store(ownerRef+60u,history&3u);
    }
    else roots=receipt.y&M8_FLOWER_INVALIDATION_ROOTS;
    if(through)receipt.y|=M8_FLOWER_INVALIDATION_THROUGH;
    if(through || structural)
    {
        receipt.y|=M8_FLOWER_INVALIDATION_SOURCE;
        if((_M8FlowerDetailPages.Load(ownerRef+60u)&M8_FLOWER_INVALIDATION_LOCAL_DONE)==0u)
            receipt.y|=M8_FLOWER_INVALIDATION_LOCAL_PENDING;
        uint acknowledged=(_M8FlowerDetailPages.Load(ownerRef+60u)>>2u)&M8_FLOWER_INVALIDATION_ROOTS;
        if((roots&~acknowledged)!=0u)receipt.y|=M8_FLOWER_INVALIDATION_PEERS_PENDING;
    }
    DeviceMemoryBarrier();
    _M8FlowerDetailPages.Store2(ownerRef+48u,receipt);
    return M8_FLOWER_ARENA_OK;
}

void M8FlowerAcknowledgeLocalInvalidation(uint ownerRef,uint observationToken)
{
    uint2 receipt=M8FlowerInvalidationReceipt(ownerRef);
    if(receipt.x!=observationToken || observationToken==0u)return;
    DeviceMemoryBarrier();
    uint history=_M8FlowerDetailPages.Load(ownerRef+60u);
    _M8FlowerDetailPages.Store(ownerRef+60u,history|M8_FLOWER_INVALIDATION_LOCAL_DONE);
    _M8FlowerDetailPages.Store(ownerRef+52u,receipt.y&~M8_FLOWER_INVALIDATION_LOCAL_PENDING);
}

void M8FlowerAcknowledgePeerInvalidation(uint ownerRef,uint observationToken,uint node)
{
    uint2 receipt=M8FlowerInvalidationReceipt(ownerRef);
    if(receipt.x!=observationToken || observationToken==0u || node<6u || node>=26u)return;
    // The caller acknowledges only after the exact node^1 receiver's entire
    // dependent cut succeeded. BUSY never acknowledges a prefix of that cut.
    DeviceMemoryBarrier();
    uint history=_M8FlowerDetailPages.Load(ownerRef+60u)|(1u<<(node-6u+2u));
    _M8FlowerDetailPages.Store(ownerRef+60u,history);
    uint outstanding=(receipt.y&M8_FLOWER_INVALIDATION_ROOTS)&~(history>>2u);
    if(outstanding==0u)
        _M8FlowerDetailPages.Store(ownerRef+52u,receipt.y&~M8_FLOWER_INVALIDATION_PEERS_PENDING);
}

bool M8FlowerPhaseUsesInvalidatedRoot(uint dependency,uint capturedRoots,uint roots)
{
    uint node;
    if(!M8FlowerPhaseOriginalNode(dependency,node))return false;
    uint bit=1u<<(node-6u);
    // Original phase removal is unconditional for a requested relation.
    // Fine ancestry reads the peer only through a present local coarse term.
    return (roots&bit)!=0u && (dependency<20u || (capturedRoots&bit)!=0u);
}

// One non-spinning transaction for phase and dependent run headers. The
// supplied root mask is an already-proved invalidation request; it is not
// evidence, and this storage routine performs no dual or geometric decision.
// allLocal is reserved for the proved full-support source, never a peer.
uint M8FlowerInvalidateDependentPhases(uint ownerRef,uint observationToken,
    uint roots,bool allLocal,uint publishing,uint retired,out bool changed)
{
    changed=false;roots&=M8_FLOWER_INVALIDATION_ROOTS;
    if(ownerRef==0u)return M8_FLOWER_ARENA_OK;
    if(allLocal)
    {
        uint2 receipt=M8FlowerInvalidationReceipt(ownerRef);
        if(observationToken==0u || receipt.x!=observationToken ||
            (receipt.y&M8_FLOWER_INVALIDATION_THROUGH)==0u)return M8_FLOWER_ARENA_INVALID;
    }
    if(!M8FlowerOwnerWritable(ownerRef,publishing,retired))return M8_FLOWER_ARENA_BUSY;
    M8FlowerArena detail=M8FlowerDetailArena(),thread=M8FlowerThreadArena();
    if(!M8FlowerArenaAcquire(_M8FlowerDetailPages,detail))return M8_FLOWER_ARENA_BUSY;
    if(!M8FlowerArenaAcquire(_M8ThreadAtlasPages,thread))
    {
        M8FlowerArenaRelease(_M8FlowerDetailPages,detail);
        return M8_FLOWER_ARENA_BUSY;
    }
    // A receiver may have acquired new fine terms in a previous held
    // quantum. Gather its CURRENT local read-set under this same lease;
    // the source receipt separately preserves pre-epoch outgoing relations.
    // Local epoch changes have retired at the global gather barrier.
    uint captured;
    uint status=M8FlowerReadOriginalPhasePresence(ownerRef,captured)?
        M8_FLOWER_ARENA_OK:M8_FLOWER_ARENA_INVALID;
    uint first=0u,count=0u,epoch=M8FlowerGetOwnerEpoch(ownerRef);
    uint4 carriers=0u.xxxx;
    if(status==M8_FLOWER_ARENA_OK &&
        M8FlowerFindPhaseIndex(ownerRef,0u,first,count)==0xffffffffu)
        status=M8_FLOWER_ARENA_INVALID;
    // All range/address validation and dependency gathering precede any
    // mutation. A malformed row cannot leave half of the cut published.
    [loop]for(uint index=0u;status==M8_FLOWER_ARENA_OK && index<count;index++)
    {
        uint4 record=_M8FlowerDetailPages.Load4(first+16u*index);
        if(record.w!=epoch || ((record.x>>16u)&7u)>1u)continue;
        uint dependency;
        if(asint(record.y)>asint(record.z) ||
            !M8FlowerTryPhaseDependencyIndex(record.x,dependency))
        {status=M8_FLOWER_ARENA_INVALID;break;}
        if(allLocal || M8FlowerPhaseUsesInvalidatedRoot(dependency,captured,roots))
            carriers|=M8FlowerPhaseDependentCarriersAt(dependency);
    }
    // SourceAnchorAlternatives reads a shared original root even if this
    // endpoint owns no innovation. Its actual carrier dependents still apply.
    uint remaining=roots;
    [loop]while(remaining!=0u)
    {
        uint index=(uint)firstbitlow(remaining);remaining&=remaining-1u;
        carriers|=M8FlowerPhaseDependentCarriersAt(index);
    }
    [loop]for(uint kind=0u;status==M8_FLOWER_ARENA_OK && kind<2u;kind++)
    {
        uint runFirst,runCount;
        if(!M8FlowerReadRunSpan(ownerRef,kind!=0u,runFirst,runCount))
        {status=M8_FLOWER_ARENA_INVALID;break;}
        [loop]for(uint index=0u;index<runCount;index++)
        {
            uint address=runFirst+32u*index;
            uint key=kind==0u?_M8FlowerDetailPages.Load(address):_M8ThreadAtlasPages.Load(address);
            if(!M8FlowerL2KeyValid(key))
            {status=M8_FLOWER_ARENA_INVALID;break;}
        }
    }
    if(status==M8_FLOWER_ARENA_OK)
    {
        // R3 first, then R2, under one lease. Keep unrelated phase records
        // sorted and bit-identical; a selective cut never advances the epoch.
        [loop]for(uint phasePass=0u;phasePass<2u;phasePass++)
        {
            uint kept=0u;
            [loop]for(uint index=0u;index<count;index++)
            {
                uint4 record=_M8FlowerDetailPages.Load4(first+16u*index);
                uint dependency;
                bool remove=record.w==epoch && ((record.x>>16u)&7u)==1u-phasePass &&
                    M8FlowerTryPhaseDependencyIndex(record.x,dependency) &&
                    (allLocal || M8FlowerPhaseUsesInvalidatedRoot(dependency,captured,roots));
                if(remove){changed=true;continue;}
                if(kept!=index)_M8FlowerDetailPages.Store4(first+16u*kept,record);
                kept++;
            }
            count=kept;
        }
        _M8FlowerDetailPages.Store(ownerRef+12u,count);
        [loop]for(uint kind=0u;kind<2u;kind++)
        {
            uint runFirst,runCount;
            M8FlowerReadRunSpan(ownerRef,kind!=0u,runFirst,runCount);
            [loop]for(uint index=0u;index<runCount;index++)
            {
                uint address=runFirst+32u*index;
                uint key=kind==0u?_M8FlowerDetailPages.Load(address):_M8ThreadAtlasPages.Load(address);
                uint carrier=M8FlowerL2WedgeIndex((key>>4u)&63u,key&15u)/6u;
                if(carrier>=128u || (carriers[carrier>>5u]&(1u<<(carrier&31u)))==0u)continue;
                uint epochAddress=address+(kind==0u?16u:20u);
                uint runEpoch=kind==0u?_M8FlowerDetailPages.Load(epochAddress):
                    _M8ThreadAtlasPages.Load(epochAddress);
                if(runEpoch!=epoch)continue;
                // Invalid resident header, not a manufactured zero signal.
                // Keep allocation/program ownership for deferred replacement
                // or compaction. Existing readers and complete-image capture
                // skip mismatching epochs before reading a single child.
                if(kind==0u)_M8FlowerDetailPages.Store(epochAddress,0u);
                else _M8ThreadAtlasPages.Store(epochAddress,0u);
                changed=true;
            }
        }
        if(changed)_M8FlowerDetailPages.Store(ownerRef+44u,publishing);
        if(allLocal)M8FlowerAcknowledgeLocalInvalidation(ownerRef,observationToken);
    }
    M8FlowerArenaRelease(_M8ThreadAtlasPages,thread);
    M8FlowerArenaRelease(_M8FlowerDetailPages,detail);
    return status;
}

uint M8FlowerCommitPhase(uint ownerRef,M8FlowerDetailRecord record,
    uint publishing,uint retired)
{
    if(record.ParentEpoch==0u || record.Lower>record.Upper ||
        M8FlowerGetOwnerEpoch(ownerRef)!=record.ParentEpoch ||
        !M8FlowerOwnerWritable(ownerRef,publishing,retired))return M8_FLOWER_ARENA_INVALID;
    M8FlowerArena pool=M8FlowerDetailArena();
    if(!M8FlowerArenaAcquire(_M8FlowerDetailPages,pool))return M8_FLOWER_ARENA_BUSY;
    uint first,count;
    uint index=M8FlowerFindPhaseIndex(ownerRef,record.Key,first,count);
    uint result=M8_FLOWER_ARENA_OK;
    if(index==0xffffffffu)result=M8_FLOWER_ARENA_INVALID;
    bool existing=false;
    if(index<count)existing=_M8FlowerDetailPages.Load(first+16u*index)==record.Key;
    uint capacity=_M8FlowerDetailPages.Load(ownerRef+16u);
    uint required=(count+(existing?0u:1u))*16u;
    if(result==M8_FLOWER_ARENA_OK && required>capacity)
    {
        uint replacement,newCapacity;
        result=M8FlowerArenaAllocateLocked(_M8FlowerDetailPages,pool,required,replacement,newCapacity);
        if(result==M8_FLOWER_ARENA_OK)
        {
            [loop]for(uint i=0u;i<count;i++)
                _M8FlowerDetailPages.Store4(replacement+16u*i,_M8FlowerDetailPages.Load4(first+16u*i));
            if(capacity!=0u)M8FlowerArenaFreeLocked(_M8FlowerDetailPages,pool,first,capacity);
            first=replacement;capacity=newCapacity;
        }
    }
    if(result==M8_FLOWER_ARENA_OK)
    {
        if(!existing)
        {
            [loop]for(uint i=count;i>index;i--)
                _M8FlowerDetailPages.Store4(first+16u*i,_M8FlowerDetailPages.Load4(first+16u*(i-1u)));
            count++;
        }
        _M8FlowerDetailPages.Store4(first+16u*index,uint4(record.Key,
            asuint(record.Lower),asuint(record.Upper),record.ParentEpoch));
        DeviceMemoryBarrier();
        _M8FlowerDetailPages.Store3(ownerRef+8u,uint3(first,count,capacity));
        _M8FlowerDetailPages.Store(ownerRef+44u,publishing);
        uint history=_M8FlowerDetailPages.Load(ownerRef+60u);
        _M8FlowerDetailPages.Store(ownerRef+60u,history|1u);
    }
    M8FlowerArenaRelease(_M8FlowerDetailPages,pool);
    return result;
}

bool M8FlowerCanonicalSplit(uint2 bits)
{
    if((bits.y&~M8_FLOWER_SKIN_SPLIT_HIGH_MASK)!=0u)return false;
    uint root,l3;uint2 l4;
    M8FlowerUnpackSkinSplitBits(bits.x,bits.y,root,l3,l4);
    return M8FlowerSkinSplitClosure(root,l3,l4);
}
// Both RGB and V use this same allocation/publication path. The supplied
// words are already interval-proven observations; this routine neither votes
// on geometry nor invents the seven values required by an atomic split.
uint M8FlowerCommitSkinGroupLocked(uint ownerRef,uint flowerKey,
    uint parentOrdinal,uint childWords[28],bool thread,uint publishing,
    RWByteAddressBuffer payload,M8FlowerArena pool)
{
    uint epoch=M8FlowerGetOwnerEpoch(ownerRef);
    uint first,count;
    uint index=M8FlowerFindRunIndex(ownerRef,flowerKey,thread,first,count);
    if(index==0xffffffffu)return M8_FLOWER_ARENA_INVALID;
    bool existing=index<count && payload.Load(first+index*32u)==flowerKey;
    uint oldRef=existing?first+index*32u:0u;
    uint2 oldBits=uint2(0u,0u);
    uint oldBase=0u,program=0xffffffffu;
    uint staleProgram=0xffffffffu;
    uint2 oldAllocation=uint2(0u,0u);
    if(existing)
    {
        uint4 a=payload.Load4(oldRef);uint2 b=payload.Load2(oldRef+16u);
        oldAllocation=payload.Load2(oldRef+24u);
        uint oldEpoch=thread?b.y:b.x;
        if(oldEpoch==epoch)
        {
            if(thread){oldBits=uint2(a.w,b.x);oldBase=a.z;program=a.y;}
            else{oldBits=uint2(a.z,a.w);oldBase=a.y;}
            if(!M8FlowerCanonicalSplit(oldBits))return M8_FLOWER_ARENA_INVALID;
        }
        else if(thread)staleProgram=a.y;
    }
    uint2 bits=oldBits;
    if(parentOrdinal<32u)bits.x|=1u<<parentOrdinal;
    else bits.y|=1u<<(parentOrdinal-32u);
    if(!M8FlowerCanonicalSplit(bits))return M8_FLOWER_ARENA_INVALID;
    uint groups=countbits(bits.x)+countbits(bits.y);
    uint oldGroups=countbits(oldBits.x)+countbits(oldBits.y);
    uint rank=M8FlowerSplitRank(bits,parentOrdinal);
    bool replacing=groups==oldGroups;
    uint groupBytes=thread?112u:56u;
    if(oldGroups!=0u && (oldBase>0xffffffffu/groupBytes ||
        (thread ? !M8FlowerThreadRange(oldBase*groupBytes,oldGroups*groupBytes) :
            !M8FlowerDetailRange(oldBase*groupBytes,oldGroups*groupBytes))))
        return M8_FLOWER_ARENA_INVALID;
    uint allocation,allocationCapacity;
    uint result=M8FlowerArenaAllocateLocked(payload,pool,groups*groupBytes+groupBytes-1u,
        allocation,allocationCapacity);
    if(result!=M8_FLOWER_ARENA_OK)return result;
    uint groupBase=(allocation+groupBytes-1u)/groupBytes;
    uint newFirst=first;
    uint ownerOffset=thread?32u:20u;
    uint capacity=_M8FlowerDetailPages.Load(ownerRef+ownerOffset+8u);
    uint newCapacity=capacity;
    uint required=(count+(existing?0u:1u))*32u;
    if(required>capacity)
    {
        result=M8FlowerArenaAllocateLocked(payload,pool,required,newFirst,newCapacity);
        if(result!=M8_FLOWER_ARENA_OK)
        {
            M8FlowerArenaFreeLocked(payload,pool,allocation,allocationCapacity);
            return result;
        }
        [loop]for(uint item=0u;item<count;item++)
        {
            payload.Store4(newFirst+32u*item,payload.Load4(first+32u*item));
            payload.Store4(newFirst+32u*item+16u,payload.Load4(first+32u*item+16u));
        }
    }
    [loop]for(uint group=0u;group<groups;group++)
    {
        uint destination=(groupBase+group)*groupBytes;
        uint oldGroup=group-(group>rank && !replacing?1u:0u);
        [loop]for(uint word=0u;word<groupBytes/4u;word++)
        {
            uint value;
            if(group==rank)value=childWords[word];
            else value=payload.Load((oldBase+oldGroup)*groupBytes+4u*word);
            payload.Store(destination+4u*word,value);
        }
    }
    if(!existing)
    {
        [loop]for(uint item=count;item>index;item--)
        {
            payload.Store4(newFirst+32u*item,payload.Load4(newFirst+32u*(item-1u)));
            payload.Store4(newFirst+32u*item+16u,payload.Load4(newFirst+32u*(item-1u)+16u));
        }
        count++;
    }
    uint target=newFirst+32u*index;
    if(thread)
    {
        payload.Store4(target,uint4(flowerKey,program,groupBase,bits.x));
        payload.Store2(target+16u,uint2(bits.y,epoch));
    }
    else
    {
        payload.Store4(target,uint4(flowerKey,groupBase,bits.x,bits.y));
        payload.Store2(target+16u,uint2(epoch,0u));
    }
    payload.Store2(target+24u,uint2(allocation,allocationCapacity));
    DeviceMemoryBarrier();
    _M8FlowerDetailPages.Store3(ownerRef+ownerOffset,uint3(newFirst,count,newCapacity));
    _M8FlowerDetailPages.Store(ownerRef+44u,publishing);
    uint history=_M8FlowerDetailPages.Load(ownerRef+60u);
    _M8FlowerDetailPages.Store(ownerRef+60u,history|1u);
    if(newFirst!=first && capacity!=0u)
        M8FlowerArenaFreeLocked(payload,pool,first,capacity);
    if(oldAllocation.y!=0u)
        M8FlowerArenaFreeLocked(payload,pool,oldAllocation.x,oldAllocation.y);
    if(staleProgram!=0xffffffffu)M8FlowerReleaseOpticalLocked(staleProgram);
    return M8_FLOWER_ARENA_OK;
}

uint M8FlowerCommitMetricGroup(uint ownerRef,uint flowerKey,uint parentOrdinal,
    M8FlowerVInterval children[7],uint publishing,uint retired)
{
    if(parentOrdinal>=57u || !M8FlowerL2KeyValid(flowerKey) ||
        M8FlowerGetOwnerEpoch(ownerRef)==0u ||
        !M8FlowerOwnerWritable(ownerRef,publishing,retired))return M8_FLOWER_ARENA_INVALID;
    uint words[28];
    [unroll]for(uint child=0u;child<7u;child++)
    {
        if(children[child].Lower>children[child].Upper)return M8_FLOWER_ARENA_INVALID;
        words[2u*child]=asuint(children[child].Lower);
        words[2u*child+1u]=asuint(children[child].Upper);
    }
    [unroll]for(uint word=14u;word<28u;word++)words[word]=0u;
    M8FlowerArena pool=M8FlowerDetailArena();
    if(!M8FlowerArenaAcquire(_M8FlowerDetailPages,pool))return M8_FLOWER_ARENA_BUSY;
    uint result=M8FlowerCommitSkinGroupLocked(ownerRef,flowerKey,parentOrdinal,
        words,false,publishing,_M8FlowerDetailPages,pool);
    M8FlowerArenaRelease(_M8FlowerDetailPages,pool);
    return result;
}

uint M8FlowerCommitThreadGroup(uint ownerRef,uint flowerKey,uint parentOrdinal,
    M8ThreadColorInterval children[7],uint publishing,uint retired)
{
    if(parentOrdinal>=57u || !M8FlowerL2KeyValid(flowerKey) ||
        M8FlowerGetOwnerEpoch(ownerRef)==0u ||
        !M8FlowerOwnerWritable(ownerRef,publishing,retired))return M8_FLOWER_ARENA_INVALID;
    uint words[28];
    [unroll]for(uint child=0u;child<7u;child++)
    {
        uint2 lo=children[child].LowerLinearRgba,hi=children[child].UpperLinearRgba;
        float4 a=float4(f16tof32(lo.x&65535u),f16tof32(lo.x>>16u),
            f16tof32(lo.y&65535u),f16tof32(lo.y>>16u));
        float4 b=float4(f16tof32(hi.x&65535u),f16tof32(hi.x>>16u),
            f16tof32(hi.y&65535u),f16tof32(hi.y>>16u));
        if(!all(M8FlowerIsFinite(a)) || !all(M8FlowerIsFinite(b)) || any(a>b))return M8_FLOWER_ARENA_INVALID;
        words[4u*child]=lo.x;words[4u*child+1u]=lo.y;
        words[4u*child+2u]=hi.x;words[4u*child+3u]=hi.y;
    }
    M8FlowerArena detail=M8FlowerDetailArena(),pool=M8FlowerThreadArena();
    if(!M8FlowerArenaAcquire(_M8FlowerDetailPages,detail))return M8_FLOWER_ARENA_BUSY;
    if(!M8FlowerArenaAcquire(_M8ThreadAtlasPages,pool))
    {
        M8FlowerArenaRelease(_M8FlowerDetailPages,detail);
        return M8_FLOWER_ARENA_BUSY;
    }
    uint result=M8FlowerCommitSkinGroupLocked(ownerRef,flowerKey,parentOrdinal,
        words,true,publishing,_M8ThreadAtlasPages,pool);
    M8FlowerArenaRelease(_M8ThreadAtlasPages,pool);
    M8FlowerArenaRelease(_M8FlowerDetailPages,detail);
    return result;
}

void M8FlowerFreeOwnerPayloadLocked(uint ownerRef)
{
    M8FlowerArena detail=M8FlowerDetailArena(),thread=M8FlowerThreadArena();
    uint3 phases=_M8FlowerDetailPages.Load3(ownerRef+8u);
    if(phases.z!=0u)M8FlowerArenaFreeLocked(_M8FlowerDetailPages,detail,phases.x,phases.z);
    [unroll]for(uint kind=0u;kind<2u;kind++)
    {
        uint offset=kind==0u?20u:32u;
        uint3 runs=_M8FlowerDetailPages.Load3(ownerRef+offset);
        [loop]for(uint index=0u;index<runs.y;index++)
        {
            uint2 allocation;
            if(kind==0u)allocation=_M8FlowerDetailPages.Load2(runs.x+32u*index+24u);
            else
            {
                allocation=_M8ThreadAtlasPages.Load2(runs.x+32u*index+24u);
                uint program=_M8ThreadAtlasPages.Load(runs.x+32u*index+4u);
                if(program!=0xffffffffu)M8FlowerReleaseOpticalLocked(program);
            }
            if(allocation.y==0u)continue;
            if(kind==0u)M8FlowerArenaFreeLocked(_M8FlowerDetailPages,detail,allocation.x,allocation.y);
            else M8FlowerArenaFreeLocked(_M8ThreadAtlasPages,thread,allocation.x,allocation.y);
        }
        if(runs.z!=0u)
        {
            if(kind==0u)M8FlowerArenaFreeLocked(_M8FlowerDetailPages,detail,runs.x,runs.z);
            else M8FlowerArenaFreeLocked(_M8ThreadAtlasPages,thread,runs.x,runs.z);
        }
    }
    _M8FlowerDetailPages.Store3(ownerRef+8u,uint3(0u,0u,0u));
    _M8FlowerDetailPages.Store3(ownerRef+20u,uint3(0u,0u,0u));
    _M8FlowerDetailPages.Store3(ownerRef+32u,uint3(0u,0u,0u));
}

uint M8FlowerInvalidateOwner(uint slot,uint kernelLocal,uint slotGeneration,
    uint publishing,uint retired)
{
    uint ownerRef=M8FlowerFindOwner(slot,kernelLocal,slotGeneration);
    if(ownerRef==0u)return M8_FLOWER_ARENA_OK;
    if(!M8FlowerOwnerWritable(ownerRef,publishing,retired))return M8_FLOWER_ARENA_BUSY;
    uint epoch=_M8FlowerDetailPages.Load(ownerRef+4u);
    if(epoch==0u)return M8_FLOWER_ARENA_INVALID;
    if(epoch!=0xffffffffu)
    {
        // One commit workgroup owns this tile, and one lane owns this kernel.
        // Logical invalidation changes no allocation; independent owners must
        // not contend for the global allocator merely to advance their epoch.
        _M8FlowerDetailPages.Store(ownerRef+4u,epoch+1u);
        // The old phase span has no live records in the new epoch. Forget
        // its searchable prefix in O(1), retaining the allocation/capacity
        // for reuse after the existing reader lease. Otherwise stale phase
        // count alone would request blind geometry work on a flat parent.
        // RGB/V runs remain logically invalidated by their ParentEpoch.
        _M8FlowerDetailPages.Store(ownerRef+12u,0u);
        _M8FlowerDetailPages.Store(ownerRef+44u,publishing);
        // The pre-mutation original-phase receipt must survive this epoch
        // transition until every exact peer has acknowledged its own cut.
        return M8_FLOWER_ARENA_OK;
    }
    M8FlowerArena detail=M8FlowerDetailArena();
    if(!M8FlowerArenaAcquire(_M8FlowerDetailPages,detail))return M8_FLOWER_ARENA_BUSY;
    if(epoch==0xffffffffu)
    {
        M8FlowerArena thread=M8FlowerThreadArena();
        if(!M8FlowerArenaAcquire(_M8ThreadAtlasPages,thread))
        {
            M8FlowerArenaRelease(_M8FlowerDetailPages,detail);
            return M8_FLOWER_ARENA_BUSY;
        }
        // Rebase is the exceptional bounded owner transaction. No descendant
        // with an ancient epoch1 survives physically. The complete-image SSD
        // transaction also purges historical descendants before publishing1.
        M8FlowerFreeOwnerPayloadLocked(ownerRef);
        uint history=_M8FlowerDetailPages.Load(ownerRef+60u);
        _M8FlowerDetailPages.Store(ownerRef+60u,history|2u);
        M8FlowerArenaRelease(_M8ThreadAtlasPages,thread);
        epoch=1u;
    }
    _M8FlowerDetailPages.Store(ownerRef+4u,epoch);
    _M8FlowerDetailPages.Store(ownerRef+44u,publishing);
    // The exceptional rebase retains the same transient peer receipt too.
    M8FlowerArenaRelease(_M8FlowerDetailPages,detail);
    return M8_FLOWER_ARENA_OK;
}

uint M8FlowerRemovePhase(uint ownerRef,uint key,uint publishing,uint retired)
{
    if(!M8FlowerOwnerWritable(ownerRef,publishing,retired))return M8_FLOWER_ARENA_BUSY;
    M8FlowerArena pool=M8FlowerDetailArena();
    if(!M8FlowerArenaAcquire(_M8FlowerDetailPages,pool))return M8_FLOWER_ARENA_BUSY;
    uint first,count;uint index=M8FlowerFindPhaseIndex(ownerRef,key,first,count);
    if(index<count && _M8FlowerDetailPages.Load(first+16u*index)==key)
    {
        [loop]for(uint i=index+1u;i<count;i++)
            _M8FlowerDetailPages.Store4(first+16u*(i-1u),_M8FlowerDetailPages.Load4(first+16u*i));
        _M8FlowerDetailPages.Store(ownerRef+12u,count-1u);
        _M8FlowerDetailPages.Store(ownerRef+44u,publishing);
    }
    M8FlowerArenaRelease(_M8FlowerDetailPages,pool);
    // Removal becomes an exact persistent tombstone at complete-image append;
    // omission in a partial capture is never interpreted as removal.
    return index==0xffffffffu?M8_FLOWER_ARENA_INVALID:M8_FLOWER_ARENA_OK;
}

// Installation is a storage barrier, not positive evidence. Call only for an
// empty owner created from its validated incoming canonical M8 tile packet.
bool M8FlowerRestoreOwnerEpoch(uint ownerRef,uint epoch,uint publishing)
{
    if(epoch==0u || publishing==0u || !M8FlowerDetailRange(ownerRef,64u))return false;
    if(_M8FlowerDetailPages.Load(ownerRef+12u)!=0u ||
        _M8FlowerDetailPages.Load(ownerRef+24u)!=0u ||
        _M8FlowerDetailPages.Load(ownerRef+36u)!=0u)return false;
    _M8FlowerDetailPages.Store(ownerRef+4u,epoch);
    _M8FlowerDetailPages.Store(ownerRef+44u,publishing);
    _M8FlowerDetailPages.Store(ownerRef+60u,1u);
    return true;
}

uint M8FlowerRetireTile(uint slot,uint slotGeneration,uint retired)
{
    if(slot>=32768u)return M8_FLOWER_ARENA_INVALID;
    uint address=M8_FLOWER_TILE_DIRECTORY+16u*slot;
    uint2 tile=_M8FlowerDetailPages.Load2(address);
    if(tile.x==0u)return M8_FLOWER_ARENA_OK;
    if(tile.y!=slotGeneration)return M8_FLOWER_SIDECAR_STALE_SLOT;
    [loop]for(uint local=0u;local<512u;local++)
    {
        uint owner=_M8FlowerDetailPages.Load(tile.x+4u*local);
        if(owner!=0u && _M8FlowerDetailPages.Load(owner+44u)>retired)
            return M8_FLOWER_ARENA_BUSY;
    }
    M8FlowerArena detail=M8FlowerDetailArena(),thread=M8FlowerThreadArena();
    if(!M8FlowerArenaAcquire(_M8FlowerDetailPages,detail))return M8_FLOWER_ARENA_BUSY;
    if(!M8FlowerArenaAcquire(_M8ThreadAtlasPages,thread))
    {
        M8FlowerArenaRelease(_M8FlowerDetailPages,detail);
        return M8_FLOWER_ARENA_BUSY;
    }
    [loop]for(uint local=0u;local<512u;local++)
    {
        uint owner=_M8FlowerDetailPages.Load(tile.x+4u*local);
        if(owner==0u)continue;
        M8FlowerFreeOwnerPayloadLocked(owner);
        M8FlowerArenaFreeLocked(_M8FlowerDetailPages,detail,owner,
            _M8FlowerDetailPages.Load(owner+56u));
    }
    M8FlowerArenaFreeLocked(_M8FlowerDetailPages,detail,tile.x,2048u);
    _M8FlowerDetailPages.Store4(address,uint4(0u,0u,0u,0u));
    M8FlowerArenaRelease(_M8ThreadAtlasPages,thread);
    M8FlowerArenaRelease(_M8FlowerDetailPages,detail);
    return M8_FLOWER_ARENA_OK;
}

uint M8FlowerAllocateImportedGroups(uint groupCount,bool thread,
    out uint groupBase,out uint allocation,out uint capacity)
{
    groupBase=0xffffffffu;allocation=0u;capacity=0u;
    if(groupCount==0u || groupCount>57u)return M8_FLOWER_ARENA_INVALID;
    uint stride=thread?112u:56u;
    uint result;
    if(thread)result=M8FlowerArenaAllocate(_M8ThreadAtlasPages,M8FlowerThreadArena(),
        groupCount*stride+stride-1u,allocation,capacity);
    else result=M8FlowerArenaAllocate(_M8FlowerDetailPages,M8FlowerDetailArena(),
        groupCount*stride+stride-1u,allocation,capacity);
    if(result==M8_FLOWER_ARENA_OK)groupBase=(allocation+stride-1u)/stride;
    return result;
}

bool M8FlowerStoreImportedGroup(bool thread,uint groupBase,uint groupCount,uint rank,
    uint allocation,uint allocationCapacity,uint words[28])
{
    if(groupCount==0u || groupCount>57u || rank>=groupCount || groupBase==0xffffffffu)return false;
    uint stride=thread?112u:56u;
    if(groupBase>0xffffffffu/stride || rank>(0xffffffffu/stride)-groupBase)return false;
    uint address=(groupBase+rank)*stride;
    if(address<allocation || stride>allocationCapacity ||
        address-allocation>allocationCapacity-stride)return false;
    if(thread ? !M8FlowerThreadRange(address,stride) : !M8FlowerDetailRange(address,stride))return false;
    [unroll]for(uint child=0u;child<7u;child++)
    {
        if(thread)
        {
            uint4 value=uint4(words[4u*child],words[4u*child+1u],
                words[4u*child+2u],words[4u*child+3u]);
            float4 lo=float4(f16tof32(value.x&65535u),f16tof32(value.x>>16u),
                f16tof32(value.y&65535u),f16tof32(value.y>>16u));
            float4 hi=float4(f16tof32(value.z&65535u),f16tof32(value.z>>16u),
                f16tof32(value.w&65535u),f16tof32(value.w>>16u));
            if(!all(M8FlowerIsFinite(lo)) || !all(M8FlowerIsFinite(hi)) || any(lo>hi))return false;
        }
        else if(asint(words[2u*child])>asint(words[2u*child+1u]))return false;
    }
    [loop]for(uint word=0u;word<stride/4u;word++)
    {
        if(thread)_M8ThreadAtlasPages.Store(address+4u*word,words[word]);
        else _M8FlowerDetailPages.Store(address+4u*word,words[word]);
    }
    return true;
}

uint M8FlowerCancelImportedGroups(bool thread,uint allocation,uint capacity)
{
    M8FlowerArena pool;
    if(thread)pool=M8FlowerThreadArena();else pool=M8FlowerDetailArena();
    bool acquired;
    if(thread)acquired=M8FlowerArenaAcquire(_M8ThreadAtlasPages,pool);
    else acquired=M8FlowerArenaAcquire(_M8FlowerDetailPages,pool);
    if(!acquired)return M8_FLOWER_ARENA_BUSY;
    bool result;
    if(thread)
    {
        result=M8FlowerArenaFreeLocked(_M8ThreadAtlasPages,pool,allocation,capacity);
        M8FlowerArenaRelease(_M8ThreadAtlasPages,pool);
    }
    else
    {
        result=M8FlowerArenaFreeLocked(_M8FlowerDetailPages,pool,allocation,capacity);
        M8FlowerArenaRelease(_M8FlowerDetailPages,pool);
    }
    return result?M8_FLOWER_ARENA_OK:M8_FLOWER_ARENA_INVALID;
}

uint M8FlowerInstallImportedRunLocked(uint ownerRef,uint canonical[6],bool thread,
    uint allocation,uint allocationCapacity,uint publishing,
    RWByteAddressBuffer payload,M8FlowerArena pool)
{
    uint first,count;
    uint index=M8FlowerFindRunIndex(ownerRef,canonical[0],thread,first,count);
    if(index==0xffffffffu)return M8_FLOWER_ARENA_INVALID;
    if(index<count && payload.Load(first+32u*index)==canonical[0])
        return M8_FLOWER_ARENA_INVALID;
    uint ownerOffset=thread?32u:20u;
    uint capacity=_M8FlowerDetailPages.Load(ownerRef+ownerOffset+8u);
    uint replacement=first,newCapacity=capacity;
    if((count+1u)*32u>capacity)
    {
        uint result=M8FlowerArenaAllocateLocked(payload,pool,(count+1u)*32u,replacement,newCapacity);
        if(result!=M8_FLOWER_ARENA_OK)return result;
        [loop]for(uint item=0u;item<count;item++)
        {
            payload.Store4(replacement+32u*item,payload.Load4(first+32u*item));
            payload.Store4(replacement+32u*item+16u,payload.Load4(first+32u*item+16u));
        }
    }
    if(thread && canonical[1]!=0xffffffffu && !M8FlowerRetainOpticalLocked(canonical[1]))
    {
        if(replacement!=first)M8FlowerArenaFreeLocked(payload,pool,replacement,newCapacity);
        return M8_FLOWER_ARENA_INVALID;
    }
    [loop]for(uint item=count;item>index;item--)
    {
        payload.Store4(replacement+32u*item,payload.Load4(replacement+32u*(item-1u)));
        payload.Store4(replacement+32u*item+16u,payload.Load4(replacement+32u*(item-1u)+16u));
    }
    uint target=replacement+32u*index;
    payload.Store4(target,uint4(canonical[0],canonical[1],canonical[2],canonical[3]));
    payload.Store4(target+16u,uint4(canonical[4],canonical[5],allocation,allocationCapacity));
    DeviceMemoryBarrier();
    _M8FlowerDetailPages.Store3(ownerRef+ownerOffset,uint3(replacement,count+1u,newCapacity));
    _M8FlowerDetailPages.Store(ownerRef+44u,publishing);
    uint history=_M8FlowerDetailPages.Load(ownerRef+60u);
    _M8FlowerDetailPages.Store(ownerRef+60u,history|1u);
    if(replacement!=first && capacity!=0u)M8FlowerArenaFreeLocked(payload,pool,first,capacity);
    return M8_FLOWER_ARENA_OK;
}

// The transport loader must have validated/copied every advertised group
// before invoking this publication. M8 remains LOADING until the whole image
// is installed, so an incomplete image can neither draw nor resume scanning.
uint M8FlowerInstallImportedRun(uint ownerRef,uint canonical[6],bool thread,
    uint allocation,uint allocationCapacity,uint publishing,uint retired)
{
    uint epoch=thread?canonical[5]:canonical[4];
    uint2 bits;
    if(thread)bits=uint2(canonical[3],canonical[4]);
    else bits=uint2(canonical[2],canonical[3]);
    uint groups=countbits(bits.x)+countbits(bits.y);
    if(epoch==0u || M8FlowerGetOwnerEpoch(ownerRef)!=epoch ||
        !M8FlowerL2KeyValid(canonical[0]) ||
        !M8FlowerCanonicalSplit(bits) || !M8FlowerOwnerWritable(ownerRef,publishing,retired))
        return M8_FLOWER_ARENA_INVALID;
    uint groupBase=thread?canonical[2]:canonical[1];
    uint stride=thread?112u:56u;
    if(groups==0u)
    {
        if(!thread || groupBase!=0xffffffffu || canonical[1]==0xffffffffu ||
            allocation!=0u || allocationCapacity!=0u)return M8_FLOWER_ARENA_INVALID;
    }
    else if(groupBase>0xffffffffu/stride ||
        groupBase*stride<allocation || groups*stride>allocationCapacity ||
        groupBase*stride-allocation>allocationCapacity-groups*stride ||
        (thread ? !M8FlowerThreadRange(allocation,allocationCapacity) :
            !M8FlowerDetailRange(allocation,allocationCapacity)))return M8_FLOWER_ARENA_INVALID;
    if(!thread && canonical[5]!=0u)return M8_FLOWER_ARENA_INVALID;
    M8FlowerArena detail=M8FlowerDetailArena();
    if(!M8FlowerArenaAcquire(_M8FlowerDetailPages,detail))return M8_FLOWER_ARENA_BUSY;
    uint result;
    if(thread)
    {
        M8FlowerArena pool=M8FlowerThreadArena();
        if(!M8FlowerArenaAcquire(_M8ThreadAtlasPages,pool))
        {
            M8FlowerArenaRelease(_M8FlowerDetailPages,detail);
            return M8_FLOWER_ARENA_BUSY;
        }
        result=M8FlowerInstallImportedRunLocked(ownerRef,canonical,true,allocation,
            allocationCapacity,publishing,_M8ThreadAtlasPages,pool);
        M8FlowerArenaRelease(_M8ThreadAtlasPages,pool);
    }
    else result=M8FlowerInstallImportedRunLocked(ownerRef,canonical,false,allocation,
        allocationCapacity,publishing,_M8FlowerDetailPages,detail);
    M8FlowerArenaRelease(_M8FlowerDetailPages,detail);
    return result;
}

uint M8FlowerInstallOpticalProgram(M8ThreadProgramRecord program,uint logicalProgramRef,
    out uint programRef,out uint allocation,out uint capacity)
{
    programRef=0xffffffffu;allocation=0u;capacity=0u;
    if(logicalProgramRef==0xffffffffu || (program.Flags&~1u)!=0u || program.Reserved0!=0u ||
        program.Reserved1!=0u || program.Reserved2!=0u)return M8_FLOWER_ARENA_INVALID;
    float4 opticalLo=float4(f16tof32(program.OpticalLower.x&65535u),f16tof32(program.OpticalLower.x>>16u),
        f16tof32(program.OpticalLower.y&65535u),f16tof32(program.OpticalLower.y>>16u));
    float4 opticalHi=float4(f16tof32(program.OpticalUpper.x&65535u),f16tof32(program.OpticalUpper.x>>16u),
        f16tof32(program.OpticalUpper.y&65535u),f16tof32(program.OpticalUpper.y>>16u));
    float4 viewLo=float4(f16tof32(program.CaptureViewLower.x&65535u),f16tof32(program.CaptureViewLower.x>>16u),
        f16tof32(program.CaptureViewLower.y&65535u),f16tof32(program.CaptureViewLower.y>>16u));
    float4 viewHi=float4(f16tof32(program.CaptureViewUpper.x&65535u),f16tof32(program.CaptureViewUpper.x>>16u),
        f16tof32(program.CaptureViewUpper.y&65535u),f16tof32(program.CaptureViewUpper.y>>16u));
    if(!all(M8FlowerIsFinite(opticalLo)) || !all(M8FlowerIsFinite(opticalHi)) ||
        !all(M8FlowerIsFinite(viewLo)) || !all(M8FlowerIsFinite(viewHi)) ||
        any(opticalLo>opticalHi) || any(viewLo>viewHi) ||
        (program.Flags==0u && (any(opticalLo!=0.0f) || any(opticalHi!=0.0f) ||
            any(viewLo!=0.0f) || any(viewHi!=0.0f))))return M8_FLOWER_ARENA_INVALID;
    uint result=M8FlowerArenaAllocate(_M8ThreadAtlasPages,M8FlowerThreadArena(),111u,
        allocation,capacity);
    if(result!=M8_FLOWER_ARENA_OK)return result;
    programRef=(allocation+16u+47u)/48u;
    uint address=programRef*48u;
    // The physical payload index may be recycled after retirement; persistent
    // programs keep the incoming logical identity across that relocation.
    _M8ThreadAtlasPages.Store4(allocation,uint4(1u,programRef,capacity,logicalProgramRef));
    _M8ThreadAtlasPages.Store4(address,uint4(program.Flags,0u,0u,0u));
    _M8ThreadAtlasPages.Store4(address+16u,uint4(program.OpticalLower,program.OpticalUpper));
    _M8ThreadAtlasPages.Store4(address+32u,uint4(program.CaptureViewLower,program.CaptureViewUpper));
    DeviceMemoryBarrier();
    return M8_FLOWER_ARENA_OK;
}

// Release the transport's temporary lease only after every referencing run
// was published or cancelled. Run retirement releases its own reference.
uint M8FlowerReleaseImportedProgram(uint programRef)
{
    M8FlowerArena pool=M8FlowerThreadArena();
    if(!M8FlowerArenaAcquire(_M8ThreadAtlasPages,pool))return M8_FLOWER_ARENA_BUSY;
    bool result=M8FlowerReleaseOpticalLocked(programRef);
    M8FlowerArenaRelease(_M8ThreadAtlasPages,pool);
    return result?M8_FLOWER_ARENA_OK:M8_FLOWER_ARENA_INVALID;
}
#endif

#endif
