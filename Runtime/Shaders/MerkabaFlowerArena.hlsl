#ifndef GENESIS_MERKABA_FLOWER_ARENA_INCLUDED
#define GENESIS_MERKABA_FLOWER_ARENA_INCLUDED

// Resident memory only, never Flower topology. Heap-node bit 1 is the whole
// pool; its children are 2n/2n+1. Free bits denote disjoint free buddies.
// Atomic bit removal reserves one block. Split/coalesce publish only space
// already owned by that operation: no global lease, waiting WG or BUSY loss.
#define M8_FLOWER_ARENA_OK 0u
#define M8_FLOWER_ARENA_BUSY 1u // an unretired reader, not allocator contention
#define M8_FLOWER_ARENA_CAPACITY 2u
#define M8_FLOWER_ARENA_INVALID 3u
#define M8_FLOWER_ARENA_MIN_SHIFT 8u
#define M8_FLOWER_ARENA_HEADER_BYTES 32u
#define M8_FLOWER_ARENA_INVALID_REF 0xffffffffu

struct M8FlowerArena
{
    uint Control;
    uint Data;
    uint MaxOrder;
};

uint M8FlowerArenaWordCount(M8FlowerArena arena)
{
    return (1u << (arena.MaxOrder + 1u)) >> 5u;
}

// Two bitplanes (free/allocated), then three 32-way availability summaries.
// They fit inside the existing resident metadata reservation. Summary bits
// are hints during concurrent publication; only a FREE-bit atomic can allocate.
uint M8FlowerArenaBitmap(M8FlowerArena arena,uint level,uint word)
{
    uint base=arena.Control+M8_FLOWER_ARENA_HEADER_BYTES;
    uint count=M8FlowerArenaWordCount(arena);
    uint first=(count+31u)>>5u,second=(first+31u)>>5u;
    uint4 bases=base+uint4(0u,8u*count,8u*count+4u*first,8u*count+4u*(first+second));
    return bases[level]+4u*word;
}

uint M8FlowerArenaRead(RWByteAddressBuffer memory,uint address)
{
    uint value;memory.InterlockedOr(address,0u,value);return value;
}

void M8FlowerArenaRefresh(RWByteAddressBuffer memory,M8FlowerArena arena,uint word)
{
    [loop]for(uint level=0u;level<3u;level++)
    {
        uint child=M8FlowerArenaBitmap(arena,level,word);
        uint parent=M8FlowerArenaBitmap(arena,level+1u,word>>5u);
        uint bit=1u<<(word&31u),ignored;
        if(M8FlowerArenaRead(memory,child)==0u)
        {
            memory.InterlockedAnd(parent,~bit,ignored);
            // A concurrent free may have published after the empty read.
            if(M8FlowerArenaRead(memory,child)!=0u)
                memory.InterlockedOr(parent,bit,ignored);
        }
        else memory.InterlockedOr(parent,bit,ignored);
        word>>=5u;
    }
}

void M8FlowerArenaPublishFree(RWByteAddressBuffer memory,M8FlowerArena arena,uint node)
{
    uint word=node>>5u,ignored;
    memory.InterlockedOr(M8FlowerArenaBitmap(arena,0u,word),1u<<(node&31u),ignored);
    [loop]for(uint level=1u;level<4u;level++)
    {
        memory.InterlockedOr(M8FlowerArenaBitmap(arena,level,word>>5u),
            1u<<(word&31u),ignored);
        word>>=5u;
    }
}

uint M8FlowerArenaRangeMask(uint first,uint end,uint level,uint word)
{
    uint lo=first>>(5u*level),hi=(end-1u)>>(5u*level),base=word*32u;
    if(hi<base || lo>=base+32u)return 0u;
    lo=max(lo,base)-base;hi=min(hi-base,31u);
    return (0xffffffffu<<lo)&(hi==31u?0xffffffffu:(1u<<(hi+1u))-1u);
}

uint M8FlowerArenaClaim(RWByteAddressBuffer memory,M8FlowerArena arena,
    uint first,uint end,bool raw)
{
    // Four scalar bitmap levels, held in two uint4s; no private tree array.
    uint4 pending=0u.xxxx,words=0u.xxxx;
    uint level=raw?0u:3u;
    words[level]=raw?(first>>5u):0u;
    pending[level]=M8FlowerArenaRead(memory,M8FlowerArenaBitmap(arena,level,words[level]))&
        M8FlowerArenaRangeMask(first,end,level,words[level]);
    [loop]while(level<4u)
    {
        if(pending[level]==0u)
        {
            if(raw)
            {
                if(++words.x >= ((end+31u)>>5u))break;
                pending.x=M8FlowerArenaRead(memory,M8FlowerArenaBitmap(arena,0u,words.x))&
                    M8FlowerArenaRangeMask(first,end,0u,words.x);
            }
            else level++;
            continue;
        }
        uint bit=(uint)firstbitlow(pending[level]);pending[level]&=pending[level]-1u;
        uint child=32u*words[level]+bit;
        if(level!=0u)
        {
            level--;words[level]=child;
            pending[level]=M8FlowerArenaRead(memory,M8FlowerArenaBitmap(arena,level,child))&
                M8FlowerArenaRangeMask(first,end,level,child);
            continue;
        }
        uint previous;
        memory.InterlockedAnd(M8FlowerArenaBitmap(arena,0u,words.x),~(1u<<bit),previous);
        if((previous&(1u<<bit))==0u)continue;
        M8FlowerArenaRefresh(memory,arena,words.x);
        return child;
    }
    return 0u;
}

uint M8FlowerArenaOrder(uint bytes)
{
    if(bytes==0u || bytes>0xffffff00u)return 0xffffffffu;
    uint units=(bytes+255u)>>M8_FLOWER_ARENA_MIN_SHIFT;
    return units<=1u?0u:(uint)firstbithigh(units-1u)+1u;
}

uint M8FlowerArenaAllocate(RWByteAddressBuffer memory,M8FlowerArena arena,
    uint bytes,out uint address,out uint capacity)
{
    address=M8_FLOWER_ARENA_INVALID_REF;capacity=0u;
    uint wanted=M8FlowerArenaOrder(bytes);
    if(arena.MaxOrder<5u || arena.MaxOrder>18u)return M8_FLOWER_ARENA_INVALID;
    if(wanted>arena.MaxOrder)return M8_FLOWER_ARENA_CAPACITY;
    uint required=1u<<(wanted+8u),size=1u<<(arena.MaxOrder+8u);
    uint used=M8FlowerArenaRead(memory,arena.Control+4u);
    if(used>size)return M8_FLOWER_ARENA_INVALID;
    if(required>size-used)return M8_FLOWER_ARENA_CAPACITY;
    // Search summaries first. Before returning capacity inspect the actual
    // bounded free bitplanes: racing hint maintenance cannot hide free space.
    [loop]for(uint raw=0u;raw<2u;raw++)
    [loop]for(uint order=wanted;order<=arena.MaxOrder;order++)
    {
        uint first=1u<<(arena.MaxOrder-order);
        uint node=M8FlowerArenaClaim(memory,arena,first,2u*first,raw!=0u);
        if(node==0u)continue;
        [loop]for(uint split=order;split>wanted;split--)
        {
            node*=2u;
            M8FlowerArenaPublishFree(memory,arena,node+1u);
        }
        uint allocated=arena.Control+32u+4u*M8FlowerArenaWordCount(arena)+4u*(node>>5u);
        uint previous;
        memory.InterlockedOr(allocated,1u<<(node&31u),previous);
        if((previous&(1u<<(node&31u)))!=0u)return M8_FLOWER_ARENA_INVALID;
        memory.InterlockedAdd(arena.Control+4u,required,previous);
        capacity=required;
        address=arena.Data+(node-(1u<<(arena.MaxOrder-wanted)))*required;
        return M8_FLOWER_ARENA_OK;
    }
    return M8_FLOWER_ARENA_CAPACITY;
}

// The caller has retired all raw readers and owns this exact allocation.
// CAS on a sibling pair arbitrates coalescing against concurrent allocation;
// retries hold no lock or resource needed by a different invocation.
bool M8FlowerArenaFree(RWByteAddressBuffer memory,M8FlowerArena arena,
    uint address,uint capacity)
{
    if(arena.MaxOrder<5u || arena.MaxOrder>18u || address<arena.Data ||
        capacity<256u || (capacity&(capacity-1u))!=0u)return false;
    uint order=(uint)firstbithigh(capacity)-8u,offset=address-arena.Data;
    uint size=1u<<(arena.MaxOrder+8u);
    if(order>arena.MaxOrder || (offset&(capacity-1u))!=0u || offset>size-capacity)return false;
    uint node=(1u<<(arena.MaxOrder-order))+offset/capacity;
    uint allocated=arena.Control+32u+4u*M8FlowerArenaWordCount(arena)+4u*(node>>5u);
    uint previous;memory.InterlockedAnd(allocated,~(1u<<(node&31u)),previous);
    if((previous&(1u<<(node&31u)))==0u)return false;
    memory.InterlockedAdd(arena.Control+4u,0u-capacity,previous);
    M8FlowerArenaPublishFree(memory,arena,node);
    [loop]while(node>1u)
    {
        uint word=node>>5u,mask=3u<<(node&30u);
        uint entry=M8FlowerArenaBitmap(arena,0u,word);
        uint current=M8FlowerArenaRead(memory,entry);
        if((current&mask)!=mask)break;
        memory.InterlockedCompareExchange(entry,current,current&~mask,previous);
        if(previous!=current)continue;
        M8FlowerArenaRefresh(memory,arena,word);
        node>>=1u;M8FlowerArenaPublishFree(memory,arena,node);
    }
    return true;
}

#endif
