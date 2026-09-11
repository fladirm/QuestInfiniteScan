#ifndef GENESIS_MERKABA_FLOWER_OWNER_CACHE
#define GENESIS_MERKABA_FLOWER_OWNER_CACHE

#include "MerkabaFlowerSidecar.hlsl"

// GPU derived owner replacements. No camera packets, metric XYZ, algorithm
// cursor or persistent record lives here. The existing buddy allocator bounds
// both sparse 512-entry tile indices and compact actual-carrier lists.
#define M8_FLOWER_CACHE_DIRECTORY 64u
#define M8_FLOWER_CACHE_CONTROL (64u+32768u*8u)
#define M8_FLOWER_CACHE_DATA M8_FLOWER_ALIGN256(M8_FLOWER_CACHE_CONTROL+M8_FLOWER_ARENA_META_BYTES(18u))
#define M8_FLOWER_CACHE_STALE 0x80000000u
globallycoherent RWByteAddressBuffer _M8FlowerOwnerCache;
uint _M8FlowerOwnerCacheEnabled;

M8FlowerArena M8FlowerOwnerCacheArena()
{
    M8FlowerArena arena;
    arena.Control=M8_FLOWER_CACHE_CONTROL;
    arena.Data=M8_FLOWER_CACHE_DATA;arena.MaxOrder=18u;
    return arena;
}

uint M8FlowerOwnerCacheIndex(uint slot)
{
    if(_M8FlowerOwnerCacheEnabled==0u || slot>=32768u)return 0u;
    uint address=_M8FlowerOwnerCache.Load(M8_FLOWER_CACHE_DIRECTORY+8u*slot);
    return address==0xffffffffu?0u:address;
}

uint M8FlowerEnsureOwnerCacheIndex(uint slot)
{
    uint directory=M8_FLOWER_CACHE_DIRECTORY+8u*slot;
    uint index=M8FlowerOwnerCacheIndex(slot);
    if(index!=0u || _M8FlowerOwnerCacheEnabled==0u)return index;
    uint previous;
    _M8FlowerOwnerCache.InterlockedCompareExchange(directory,0u,0xffffffffu,previous);
    if(previous!=0u)return previous==0xffffffffu?0u:previous;
    uint capacity;
    if(M8FlowerArenaAllocate(_M8FlowerOwnerCache,M8FlowerOwnerCacheArena(),
        2048u,index,capacity)!=M8_FLOWER_ARENA_OK)
    {_M8FlowerOwnerCache.Store(directory,0u);return 0u;}
    // Only the first actual owner creates a tile index. Other WGs never spin:
    // a cache miss remains rebuild work, never a failed canonical observation.
    [loop]for(uint word=0u;word<512u;word+=4u)
        _M8FlowerOwnerCache.Store4(index+4u*word,0u.xxxx);
    DeviceMemoryBarrier();
    _M8FlowerOwnerCache.Store(directory,index);
    return index;
}

void M8FlowerInvalidateCachedOwner(uint slot,uint local)
{
    uint index=M8FlowerOwnerCacheIndex(slot);
    if(index==0u)return;
    uint ignored;
    _M8FlowerOwnerCache.InterlockedOr(index+4u*local,M8_FLOWER_CACHE_STALE,ignored);
}

void M8FlowerInvalidateCachedTile(uint slot,uint generation)
{
    if(_M8FlowerOwnerCacheEnabled==0u || slot>=32768u)return;
    uint ignored;
    _M8FlowerOwnerCache.InterlockedMax(M8_FLOWER_CACHE_DIRECTORY+8u*slot+4u,
        generation,ignored);
}

void M8FlowerDropCachedOwner(uint slot,uint local)
{
    uint index=M8FlowerOwnerCacheIndex(slot);
    if(index==0u)return;
    uint old;
    _M8FlowerOwnerCache.InterlockedExchange(index+4u*local,0u,old);
    old&=~M8_FLOWER_CACHE_STALE;
    if(old!=0u)
        M8FlowerArenaFree(_M8FlowerOwnerCache,M8FlowerOwnerCacheArena(),old,
            _M8FlowerOwnerCache.Load(old+12u));
}

uint M8FlowerReadOwnerSnapshot(uint slot,uint local,uint slotGeneration,
    uint flags,uint epoch,float2 errors,out uint4 carriers)
{
    carriers=0u;
    uint index=M8FlowerOwnerCacheIndex(slot);
    if(index==0u)return 0u;
    uint address=_M8FlowerOwnerCache.Load(index+4u*local);
    if(address==0u || (address&M8_FLOWER_CACHE_STALE)!=0u)return 0u;
    uint4 identity=_M8FlowerOwnerCache.Load4(address);
    uint revision=_M8FlowerOwnerCache.Load(M8_FLOWER_CACHE_DIRECTORY+8u*slot+4u);
    if(identity.x!=slotGeneration || identity.y!=flags || identity.z!=epoch ||
        _M8FlowerOwnerCache.Load(address+32u)!=revision ||
        any(_M8FlowerOwnerCache.Load2(address+48u)!=asuint(errors)))return 0u;
    carriers=_M8FlowerOwnerCache.Load4(address+16u);
    return address;
}

uint M8FlowerBeginOwnerSnapshot(uint slot,uint local,uint slotGeneration,
    uint flags,uint epoch,float2 errors,uint4 carriers)
{
    uint index=M8FlowerEnsureOwnerCacheIndex(slot);
    if(index==0u)return 0u;
    uint count=countbits(carriers.x)+countbits(carriers.y)+countbits(carriers.z)+countbits(carriers.w);
    uint needed=64u+16u*count,address=0u,capacity=0u;
    uint old=_M8FlowerOwnerCache.Load(index+4u*local)&~M8_FLOWER_CACHE_STALE;
    if(old!=0u)
    {
        capacity=_M8FlowerOwnerCache.Load(old+12u);
        if(capacity>=needed)address=old;
    }
    if(address==0u && M8FlowerArenaAllocate(_M8FlowerOwnerCache,
        M8FlowerOwnerCacheArena(),needed,address,capacity)!=M8_FLOWER_ARENA_OK)
    {M8FlowerInvalidateCachedOwner(slot,local);return 0u;}
    _M8FlowerOwnerCache.Store(index+4u*local,address|M8_FLOWER_CACHE_STALE);
    if(old!=0u && old!=address)
        M8FlowerArenaFree(_M8FlowerOwnerCache,M8FlowerOwnerCacheArena(),old,
            _M8FlowerOwnerCache.Load(old+12u));
    _M8FlowerOwnerCache.Store4(address,uint4(slotGeneration,flags,epoch,capacity));
    _M8FlowerOwnerCache.Store4(address+16u,carriers);
    _M8FlowerOwnerCache.Store4(address+32u,uint4(
        _M8FlowerOwnerCache.Load(M8_FLOWER_CACHE_DIRECTORY+8u*slot+4u),count,0u,0u));
    _M8FlowerOwnerCache.Store4(address+48u,uint4(asuint(errors),0u,0u));
    return address;
}

// Caller has cooperatively dropped all 512 owners, and synchronized the WG.
// The native queue serializes cache producers against page publication.
void M8FlowerDropEmptyCacheIndex(uint slot)
{
    uint index=M8FlowerOwnerCacheIndex(slot);
    if(index==0u)return;
    _M8FlowerOwnerCache.Store(M8_FLOWER_CACHE_DIRECTORY+8u*slot,0u);
    M8FlowerArenaFree(_M8FlowerOwnerCache,M8FlowerOwnerCacheArena(),index,2048u);
}

void M8FlowerFinishOwnerSnapshot(uint slot,uint local,uint address)
{
    if(address==0u)return;
    DeviceMemoryBarrier();
    _M8FlowerOwnerCache.Store(M8FlowerOwnerCacheIndex(slot)+4u*local,address);
}

uint4 M8FlowerCachedCarrier(uint snapshot,uint ordinal)
{
    return _M8FlowerOwnerCache.Load4(snapshot+64u+16u*ordinal);
}

void M8FlowerRetagOwnerSnapshot(uint slot,uint local,uint flags,uint epoch)
{
    uint index=M8FlowerOwnerCacheIndex(slot);
    if(index==0u)return;
    uint address=_M8FlowerOwnerCache.Load(index+4u*local);
    if(address!=0u && (address&M8_FLOWER_CACHE_STALE)==0u &&
        _M8FlowerOwnerCache.Load(address+4u)==flags)
        _M8FlowerOwnerCache.Store(address+8u,epoch);
}

#endif
