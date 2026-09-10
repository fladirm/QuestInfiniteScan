#ifndef GENESIS_MERKABA_FLOWER_SIGNAL_ITEMS
#define GENESIS_MERKABA_FLOWER_SIGNAL_ITEMS

// Observation-local handoff. These bytes are never FlowerDetail or session data.
#define M8_FLOWER_SIGNAL_HEADER_BYTES 64u
#define M8_FLOWER_SIGNAL_ITEM_BYTES 224u
#define M8_FLOWER_SIGNAL_BUFFER_BYTES (32u * 1024u * 1024u)
#define M8_FLOWER_SIGNAL_CAPACITY ((M8_FLOWER_SIGNAL_BUFFER_BYTES - M8_FLOWER_SIGNAL_HEADER_BYTES) / (M8_FLOWER_SIGNAL_ITEM_BYTES + 16u))
#define M8_FLOWER_SIGNAL_ITEMS_BASE (M8_FLOWER_SIGNAL_HEADER_BYTES + 16u * M8_FLOWER_SIGNAL_CAPACITY)
#define M8_FLOWER_SIGNAL_COUNT 0u
#define M8_FLOWER_SIGNAL_OVERFLOW 4u
#define M8_FLOWER_SIGNAL_OWNER_COUNT 8u
#define M8_FLOWER_SIGNAL_DISPATCH 16u
#define M8_FLOWER_SIGNAL_SOURCE 0u
#define M8_FLOWER_SIGNAL_SYMBOL 16u
#define M8_FLOWER_SIGNAL_REACH 32u
#define M8_FLOWER_SIGNAL_WORLD 48u
#define M8_FLOWER_SIGNAL_NEXT 44u

RWByteAddressBuffer _M8FlowerSignalItems;
ByteAddressBuffer _M8FlowerSignalItemsRead;

uint M8FlowerSignalAddress(uint item)
{
    return M8_FLOWER_SIGNAL_ITEMS_BASE + item * M8_FLOWER_SIGNAL_ITEM_BYTES;
}

bool M8FlowerReserveSignalItem(out uint address)
{
    uint item;
    _M8FlowerSignalItems.InterlockedAdd(M8_FLOWER_SIGNAL_COUNT, 1u, item);
    address = M8FlowerSignalAddress(min(item, M8_FLOWER_SIGNAL_CAPACITY - 1u));
    if (item >= M8_FLOWER_SIGNAL_CAPACITY)
    {
        uint ignored;
        _M8FlowerSignalItems.InterlockedOr(M8_FLOWER_SIGNAL_OVERFLOW, 1u, ignored);
        return false;
    }
    return true;
}

void M8FlowerPublishSignalOwner(uint packedOwner,uint first,uint count,uint generation)
{
    if(count==0u)return;
    uint ordinal;
    _M8FlowerSignalItems.InterlockedAdd(M8_FLOWER_SIGNAL_OWNER_COUNT,1u,ordinal);
    // Every published owner has at least one item, so the same capacity bounds
    // both arrays. The owner list serializes its run-header writers, not tiles.
    _M8FlowerSignalItems.Store4(M8_FLOWER_SIGNAL_HEADER_BYTES+16u*ordinal,
        uint4(packedOwner,first,count,generation));
    uint groups = ordinal + 1u, ignored;
    _M8FlowerSignalItems.InterlockedMax(M8_FLOWER_SIGNAL_DISPATCH, min(groups, 65535u), ignored);
    _M8FlowerSignalItems.InterlockedMax(M8_FLOWER_SIGNAL_DISPATCH + 4u,
        (groups + 65534u) / 65535u, ignored);
}

bool M8FlowerReadSignalOwner(uint3 group, out uint4 owner)
{
    uint ordinal = group.x + group.y * 65535u;
    uint count = min(_M8FlowerSignalItemsRead.Load(M8_FLOWER_SIGNAL_OWNER_COUNT),
        M8_FLOWER_SIGNAL_CAPACITY);
    owner=0u;
    if(ordinal>=count)return false;
    owner=_M8FlowerSignalItemsRead.Load4(M8_FLOWER_SIGNAL_HEADER_BYTES+16u*ordinal);
    return owner.z!=0u && owner.z<=128u;
}

#endif
