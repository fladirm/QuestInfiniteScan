#ifndef GENESIS_MERKABA_FLOWER_PAGES_INCLUDED
#define GENESIS_MERKABA_FLOWER_PAGES_INCLUDED

#include "MerkabaFlowerSidecar.hlsl"

#define M8_FLOWER_PAGE_DIRECTORY_BASE 64u
#define M8_FLOWER_PAGE_HEADER_BASE (64u+32768u*16u)
#define M8_FLOWER_PAGE_AUX_BASE (M8_FLOWER_PAGE_HEADER_BASE+32768u*64u)
#define M8_FLOWER_PAGE_DIRTY_BITS (M8_FLOWER_PAGE_AUX_BASE+32768u*32u)
#define M8_FLOWER_PAGE_DIRTY_SUMMARY (M8_FLOWER_PAGE_DIRTY_BITS+1024u*4u)
#define M8_FLOWER_SYMBOL_CONTROL (M8_FLOWER_PAGE_DIRTY_SUMMARY+32u*4u)
#define M8_FLOWER_INDIRECT_STRIDE 20u
#define M8_FLOWER_INDIRECT_COUNT (32768u*M8_FLOWER_INDIRECT_STRIDE)
#define M8_FLOWER_BATCH_DISPATCH_ARGS ((M8_FLOWER_INDIRECT_COUNT+19u)&~15u)
#define M8_FLOWER_BATCH_CAPACITY 32u
// Execution-only queue/receipts follow the existing arena metadata. No scan
// symbols, XYZ or alternative geometry authority is stored in this scratch.
#define M8_FLOWER_BATCH_BASE ((M8_FLOWER_SYMBOL_CONTROL+32u+524287u*4u+15u)&~15u)
#define M8_FLOWER_BATCH_RECORD_BYTES 64u
#define M8_FLOWER_BATCH_EMIT_INDICES (M8_FLOWER_BATCH_BASE+M8_FLOWER_BATCH_CAPACITY*64u)
#define M8_FLOWER_BATCH_SCRATCH_BASE (M8_FLOWER_BATCH_EMIT_INDICES+M8_FLOWER_BATCH_CAPACITY*4u)
#define M8_FLOWER_BATCH_OWNER_SPANS 0u
#define M8_FLOWER_BATCH_SCRATCH_BYTES 4096u
#define M8_FLOWER_BATCH_COUNT 12u
#define M8_FLOWER_BATCH_PHASE 16u
#define M8_FLOWER_BATCH_DIRTY_COUNT 20u
#define M8_FLOWER_BATCH_CLAIMED 24u
#define M8_FLOWER_BATCH_RESERVED 28u
#define M8_FLOWER_BATCH_COMMITTED 32u
#define M8_FLOWER_BATCH_RETRIED 36u
#define M8_FLOWER_BATCH_FAILED 40u
#define M8_FLOWER_BATCH_ITEM_CLAIMED 1u
#define M8_FLOWER_BATCH_ITEM_COUNTED 2u
#define M8_FLOWER_BATCH_ITEM_RESERVED 3u
#define M8_FLOWER_BATCH_ITEM_READY 4u
#define M8_FLOWER_BATCH_ITEM_ABORT 5u
#define M8_FLOWER_BATCH_ITEM_RETIRED 6u
#define M8_FLOWER_SYMBOL_CAPACITY 4194304u
#define M8_FLOWER_MAX_PAGE_SYMBOLS (512u*128u)
#define M8_FLOWER_PAGE_RESIDENCY_MASK 3u
#define M8_FLOWER_PAGE_SOURCE_INVALID 0x80000000u
// Transient scheduler cursor in the existing 64-byte directory control area.
// It is not a symbol ordinal, branch identity or persistent world address.
#define M8_FLOWER_DIRTY_CURSOR 8u

struct M8FlowerPageHeader
{
    int3 LogicalTile;
    uint Generation;
    uint FirstSymbol;
    uint SymbolCount;
    uint FirstDrawSample;
    uint DrawSampleCount;
};
struct M8FlowerPageBuild
{
    uint Slot;
    uint SourceGeneration;
    uint SlotGeneration;
    uint Header;
    uint SymbolAddress;
    uint SymbolCapacity;
    uint SampleAddress;
    uint SampleCapacity;
};

ByteAddressBuffer _M8FlowerSymbolArenaRead M8_FLOWER_SRV(t9);
ByteAddressBuffer _M8FlowerPageDirectoryRead M8_FLOWER_SRV(t10);
#if defined(M8_FLOWER_PAGE_WRITE)
RWByteAddressBuffer _M8FlowerSymbolArena;
RWByteAddressBuffer _M8FlowerPageDirectory;
RWByteAddressBuffer _M8FlowerIndirectCommands;
#define M8_FLOWER_PAGE_SOURCE _M8FlowerPageDirectory
#else
#define M8_FLOWER_PAGE_SOURCE _M8FlowerPageDirectoryRead
#endif

M8FlowerPageHeader M8FlowerLoadPageHeader(uint address)
{
    uint4 a=M8_FLOWER_PAGE_SOURCE.Load4(address);
    uint4 b=M8_FLOWER_PAGE_SOURCE.Load4(address+16u);
    M8FlowerPageHeader header;
    header.LogicalTile=asint(a.xyz);header.Generation=a.w;
    header.FirstSymbol=b.x;header.SymbolCount=b.y;
    header.FirstDrawSample=b.z;header.DrawSampleCount=b.w;
    return header;
}
bool M8FlowerFrontPage(uint slot,out M8FlowerPageHeader header)
{
    header=(M8FlowerPageHeader)0;
    if(slot>=32768u)return false;
    uint2 directory=M8_FLOWER_PAGE_SOURCE.Load2(M8_FLOWER_PAGE_DIRECTORY_BASE+16u*slot);
    uint first=M8_FLOWER_PAGE_HEADER_BASE+64u*slot;
    if(directory.y==0u || (directory.x!=first && directory.x!=first+32u))return false;
    uint2 source=M8_FLOWER_PAGE_SOURCE.Load2(M8_FLOWER_PAGE_AUX_BASE+32u*slot);
    if((source.y&M8_FLOWER_PAGE_SOURCE_INVALID)!=0u || source.x!=directory.y)return false;
    if((M8_FLOWER_PAGE_SOURCE.Load(M8_FLOWER_PAGE_DIRTY_BITS+4u*(slot>>5u))&
        (1u<<(slot&31u)))!=0u)return false;
    header=M8FlowerLoadPageHeader(directory.x);
    return header.Generation==directory.y &&
        header.FirstSymbol<=M8_FLOWER_SYMBOL_CAPACITY &&
        header.SymbolCount<=M8_FLOWER_SYMBOL_CAPACITY-header.FirstSymbol &&
        header.SymbolCount<=M8_FLOWER_MAX_PAGE_SYMBOLS;
}
M8FlowerSymbolRecord M8FlowerLoadSymbol(uint index)
{
    uint4 packed=_M8FlowerSymbolArenaRead.Load4(index*16u);
    M8FlowerSymbolRecord symbol;
    symbol.OwnerAndCarrier=packed.x;symbol.RootsAndWedges=packed.y;
    symbol.DetailRef=packed.z;symbol.ThreadRef=packed.w;
    return symbol;
}

#if defined(M8_FLOWER_PAGE_WRITE)
M8FlowerArena M8FlowerSymbolArena()
{
    M8FlowerArena arena;
    arena.Control=M8_FLOWER_SYMBOL_CONTROL;arena.Data=0u;arena.MaxOrder=18u;
    return arena;
}
void M8FlowerMarkPageDirty(uint slot,uint generation)
{
    if(slot>=32768u || generation==0u)return;
    uint ignored;
    // Invalidity outlives queue membership: taking work is not publication.
    // This also covers another mutation in the SAME frozen observation.
    _M8FlowerPageDirectory.InterlockedOr(M8_FLOWER_PAGE_AUX_BASE+slot*32u+4u,
        M8_FLOWER_PAGE_SOURCE_INVALID,ignored);
    _M8FlowerPageDirectory.InterlockedMax(M8_FLOWER_PAGE_AUX_BASE+slot*32u,generation,ignored);
    uint word=slot>>5u;
    _M8FlowerPageDirectory.InterlockedOr(M8_FLOWER_PAGE_DIRTY_BITS+4u*word,1u<<(slot&31u),ignored);
    _M8FlowerPageDirectory.InterlockedOr(M8_FLOWER_PAGE_DIRTY_SUMMARY+4u*(word>>5u),1u<<(word&31u),ignored);
    _M8FlowerPageDirectory.InterlockedOr(0u,1u<<(word>>5u),ignored);
}

void M8FlowerSetPageResidency(uint slot,uint classification)
{
    if(slot>=32768u)return;
    uint ignored;
    uint address=M8_FLOWER_PAGE_AUX_BASE+32u*slot+4u;
    _M8FlowerPageDirectory.InterlockedAnd(address,~M8_FLOWER_PAGE_RESIDENCY_MASK,ignored);
    _M8FlowerPageDirectory.InterlockedOr(address,
        classification&M8_FLOWER_PAGE_RESIDENCY_MASK,ignored);
}

bool M8FlowerPageSourceUnchanged(uint slot,uint generation)
{
    return _M8FlowerPageDirectory.Load(M8_FLOWER_PAGE_AUX_BASE+32u*slot)==generation &&
        (_M8FlowerPageDirectory.Load(M8_FLOWER_PAGE_DIRTY_BITS+4u*(slot>>5u))&
            (1u<<(slot&31u)))==0u;
}

uint M8FlowerBatchRecord(uint item)
{
    return M8_FLOWER_BATCH_BASE+item*M8_FLOWER_BATCH_RECORD_BYTES;
}
uint M8FlowerBatchScratch(uint item)
{
    return M8_FLOWER_BATCH_SCRATCH_BASE+item*M8_FLOWER_BATCH_SCRATCH_BYTES;
}
M8FlowerPageBuild M8FlowerLoadBatchBuild(uint item)
{
    uint address=M8FlowerBatchRecord(item);
    uint4 identity=_M8FlowerPageDirectory.Load4(address);
    uint4 allocation=_M8FlowerPageDirectory.Load4(address+32u);
    M8FlowerPageBuild build;
    build.Slot=identity.x;build.SlotGeneration=identity.y;build.SourceGeneration=identity.z;
    build.Header=allocation.x;build.SymbolAddress=allocation.y;
    build.SymbolCapacity=allocation.z;build.SampleAddress=allocation.w;
    build.SampleCapacity=_M8FlowerPageDirectory.Load(address+48u);
    return build;
}
void M8FlowerStoreBatchBuild(uint item,M8FlowerPageBuild build)
{
    uint address=M8FlowerBatchRecord(item);
    _M8FlowerPageDirectory.Store4(address+32u,uint4(build.Header,
        build.SymbolAddress,build.SymbolCapacity,build.SampleAddress));
    _M8FlowerPageDirectory.Store(address+48u,build.SampleCapacity);
}
void M8FlowerSetBatchState(uint item,uint state)
{
    DeviceMemoryBarrier();
    _M8FlowerPageDirectory.Store(M8FlowerBatchRecord(item)+12u,state);
}

// The page publisher owns its directory entry and the retired raw-reader
// generation. Its two arenas reserve/free blocks independently with atomics.
uint M8FlowerBeginPage(uint slot,uint slotGeneration,uint sourceGeneration,
    uint symbolCount,uint sampleBytes,out M8FlowerPageBuild build)
{
    build=(M8FlowerPageBuild)0;
    if(slot>=32768u || slotGeneration==0u || sourceGeneration==0u ||
        symbolCount>M8_FLOWER_MAX_PAGE_SYMBOLS)return M8_FLOWER_ARENA_INVALID;
    uint directory=M8_FLOWER_PAGE_DIRECTORY_BASE+16u*slot;
    uint4 current=_M8FlowerPageDirectory.Load4(directory);
    if(current.w!=0u)return M8_FLOWER_ARENA_BUSY;
    if(!M8FlowerPageSourceUnchanged(slot,sourceGeneration))return M8_FLOWER_ARENA_INVALID;
    build.Slot=slot;build.SourceGeneration=sourceGeneration;build.SlotGeneration=slotGeneration;
    uint header0=M8_FLOWER_PAGE_HEADER_BASE+64u*slot;
    build.Header=current.x==header0?header0+32u:header0;
    M8FlowerArena symbols=M8FlowerSymbolArena(),samples=M8FlowerDrawArena();
    uint result=M8_FLOWER_ARENA_OK;
    if(symbolCount!=0u)
        result=M8FlowerArenaAllocate(_M8FlowerPageDirectory,symbols,symbolCount*16u,
            build.SymbolAddress,build.SymbolCapacity);
    if(result==M8_FLOWER_ARENA_OK && sampleBytes!=0u)
    {
        result=M8FlowerArenaAllocate(_M8ThreadAtlasPages,samples,sampleBytes,
            build.SampleAddress,build.SampleCapacity);
        if(result!=M8_FLOWER_ARENA_OK)
        {
            if(build.SymbolCapacity!=0u &&
                !M8FlowerArenaFree(_M8FlowerPageDirectory,symbols,build.SymbolAddress,build.SymbolCapacity))
                return M8_FLOWER_ARENA_INVALID; // receipt retains this allocation
            build.SymbolAddress=0u;build.SymbolCapacity=0u;
        }
    }
    return result;
}

// A failed producer releases only its unpublished reservations. It never
// changes FRONT. The caller persists every changed capacity before retiring
// the receipt; a failed second free can never repeat the first successful free.
uint M8FlowerAbortPage(inout M8FlowerPageBuild build)
{
    M8FlowerArena symbols=M8FlowerSymbolArena(),samples=M8FlowerDrawArena();
    bool valid=true;
    if(build.SymbolCapacity!=0u)
    {
        valid=M8FlowerArenaFree(_M8FlowerPageDirectory,symbols,
            build.SymbolAddress,build.SymbolCapacity);
        if(valid){build.SymbolAddress=0u;build.SymbolCapacity=0u;}
    }
    if(valid && build.SampleCapacity!=0u)
    {
        valid=M8FlowerArenaFree(_M8ThreadAtlasPages,samples,
            build.SampleAddress,build.SampleCapacity);
        if(valid){build.SampleAddress=0u;build.SampleCapacity=0u;}
    }
    return valid?M8_FLOWER_ARENA_OK:M8_FLOWER_ARENA_INVALID;
}

void M8FlowerStoreSymbol(uint address,uint index,M8FlowerSymbolRecord symbol)
{
    _M8FlowerSymbolArena.Store4(address+16u*index,uint4(symbol.OwnerAndCarrier,
        symbol.RootsAndWedges,symbol.DetailRef,symbol.ThreadRef));
}

// Called only after every lane has finished validated symbols/sample packets.
// Graphics can never observe this header before the queue-ready publication.
bool M8FlowerFinishPendingPage(M8FlowerPageBuild build,int3 logicalTile,
    uint symbolCount,uint sampleCount)
{
    if(build.Slot>=32768u || build.SlotGeneration==0u || build.SourceGeneration==0u ||
        symbolCount>build.SymbolCapacity/16u || sampleCount>build.SampleCapacity/64u)
        return false;
    if(!M8FlowerPageSourceUnchanged(build.Slot,build.SourceGeneration))return false;
    uint header0=M8_FLOWER_PAGE_HEADER_BASE+64u*build.Slot;
    if(build.Header!=header0 && build.Header!=header0+32u)return false;
    _M8FlowerPageDirectory.Store4(build.Header,uint4(asuint(logicalTile),build.SourceGeneration));
    _M8FlowerPageDirectory.Store4(build.Header+16u,uint4(build.SymbolAddress/16u,
        symbolCount,build.SampleAddress,sampleCount));
    uint aux=M8_FLOWER_PAGE_AUX_BASE+32u*build.Slot;
    _M8FlowerPageDirectory.Store(aux+8u,build.SymbolCapacity);
    _M8FlowerPageDirectory.Store(aux+16u,build.SampleCapacity);
    _M8FlowerPageDirectory.Store(aux+24u,build.SlotGeneration);
    DeviceMemoryBarrier();
    _M8FlowerPageDirectory.Store2(M8_FLOWER_PAGE_DIRECTORY_BASE+16u*build.Slot+8u,
        uint2(build.Header,build.SourceGeneration));
    return true;
}

// Caller first retires ALL earlier front-page graphics uses through the native
// queue's graphics-acquire fence. Header.Generation alone is not a last-use
// fence: a static page can be drawn in many later frames. No new raw reader
// may bypass the nativeDone publication barrier while this lease is active.
uint M8FlowerPublishPage(uint slot,uint currentSlotGeneration,uint graphicsRetired)
{
    if(slot>=32768u)return M8_FLOWER_ARENA_INVALID;
    uint address=M8_FLOWER_PAGE_DIRECTORY_BASE+16u*slot;
    uint4 directory=_M8FlowerPageDirectory.Load4(address);
    if(directory.w==0u)return M8_FLOWER_ARENA_OK;
    if(directory.y>graphicsRetired)return M8_FLOWER_ARENA_BUSY;
    uint aux=M8_FLOWER_PAGE_AUX_BASE+32u*slot;
    if(currentSlotGeneration==0u ||
        _M8FlowerPageDirectory.Load(aux+24u)!=currentSlotGeneration ||
        !M8FlowerPageSourceUnchanged(slot,directory.w))
    {
        // A later mutation can supersede a finished, still unpublished build.
        // Release only PENDING, retain FRONT's invalid guard and requeue the
        // current source. Never publish or leak the superseded reservations.
        M8FlowerPageHeader pending=M8FlowerLoadPageHeader(directory.z);
        M8FlowerPageBuild stale=(M8FlowerPageBuild)0;
        stale.SymbolAddress=pending.FirstSymbol*16u;
        stale.SymbolCapacity=_M8FlowerPageDirectory.Load(aux+8u);
        stale.SampleAddress=pending.FirstDrawSample;
        stale.SampleCapacity=_M8FlowerPageDirectory.Load(aux+16u);
        uint result=M8FlowerAbortPage(stale);
        _M8FlowerPageDirectory.Store(aux+8u,stale.SymbolCapacity);
        _M8FlowerPageDirectory.Store(aux+16u,stale.SampleCapacity);
        if(result!=M8_FLOWER_ARENA_OK)return result;
        _M8FlowerPageDirectory.Store(aux+24u,0u);
        DeviceMemoryBarrier();
        _M8FlowerPageDirectory.Store2(address+8u,uint2(0u,0u));
        M8FlowerMarkPageDirty(slot,_M8FlowerPageDirectory.Load(aux));
        return M8_FLOWER_ARENA_OK;
    }
    M8FlowerArena symbols=M8FlowerSymbolArena(),samples=M8FlowerDrawArena();
    if(directory.y!=0u)
    {
        M8FlowerPageHeader old=M8FlowerLoadPageHeader(directory.x);
        uint oldSymbols=_M8FlowerPageDirectory.Load(aux+12u);
        uint oldSamples=_M8FlowerPageDirectory.Load(aux+20u);
        if(oldSymbols!=0u)
        {
            if(!M8FlowerArenaFree(_M8FlowerPageDirectory,symbols,old.FirstSymbol*16u,oldSymbols))
                return M8_FLOWER_ARENA_INVALID;
            _M8FlowerPageDirectory.Store(aux+12u,0u);
        }
        if(oldSamples!=0u)
        {
            if(!M8FlowerArenaFree(_M8ThreadAtlasPages,samples,old.FirstDrawSample,oldSamples))
                return M8_FLOWER_ARENA_INVALID;
            _M8FlowerPageDirectory.Store(aux+20u,0u);
        }
    }
    _M8FlowerPageDirectory.Store(aux+12u,_M8FlowerPageDirectory.Load(aux+8u));
    _M8FlowerPageDirectory.Store(aux+20u,_M8FlowerPageDirectory.Load(aux+16u));
    _M8FlowerPageDirectory.Store(aux+28u,currentSlotGeneration);
    DeviceMemoryBarrier();
    _M8FlowerPageDirectory.Store4(address,uint4(directory.z,directory.w,0u,0u));
    DeviceMemoryBarrier();
    uint ignored;
    _M8FlowerPageDirectory.InterlockedAnd(aux+4u,~M8_FLOWER_PAGE_SOURCE_INVALID,ignored);
    if(!M8FlowerPageSourceUnchanged(slot,directory.w))
        _M8FlowerPageDirectory.InterlockedOr(aux+4u,M8_FLOWER_PAGE_SOURCE_INVALID,ignored);
    return M8_FLOWER_ARENA_OK;
}
#endif

#endif
