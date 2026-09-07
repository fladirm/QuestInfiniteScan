#ifndef GENESIS_MERKABA_FLOWER_ARENA_INCLUDED
#define GENESIS_MERKABA_FLOWER_ARENA_INCLUDED

// Resident allocation metadata only. No node of this tree is scan geometry.
// A node stores the largest free power-of-two block below it, plus one;
// zero means unavailable. A wholly free node lazily initializes its children.
// One non-spinning CAS grants a short metadata lease. Contention is pending
// computation, distinct from capacity exhaustion, and keeps the observation.
#define M8_FLOWER_ARENA_OK 0u
#define M8_FLOWER_ARENA_BUSY 1u
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

uint M8FlowerArenaNode(M8FlowerArena arena, uint node)
{
    return arena.Control + M8_FLOWER_ARENA_HEADER_BYTES + 4u * node;
}

bool M8FlowerArenaAcquire(RWByteAddressBuffer memory, M8FlowerArena arena)
{
    uint previous;
    memory.InterlockedCompareExchange(arena.Control, 0u, 1u, previous);
    if (previous != 0u) return false;
    DeviceMemoryBarrier();
    return true;
}

void M8FlowerArenaRelease(RWByteAddressBuffer memory, M8FlowerArena arena)
{
    DeviceMemoryBarrier();
    memory.Store(arena.Control, 0u);
}

uint M8FlowerArenaOrder(uint bytes)
{
    // Guard addition before rounding; all configured resident pools fit uint.
    if (bytes == 0u || bytes > 0xffffff00u) return 0xffffffffu;
    uint units = (bytes + 255u) >> M8_FLOWER_ARENA_MIN_SHIFT;
    return units <= 1u ? 0u : (uint)firstbithigh(units - 1u) + 1u;
}

uint M8FlowerArenaAllocateLocked(RWByteAddressBuffer metadata,
    M8FlowerArena arena, uint bytes, out uint address, out uint capacity)
{
    address = M8_FLOWER_ARENA_INVALID_REF;
    capacity = 0u;
    uint wanted = M8FlowerArenaOrder(bytes);
    if (wanted == 0xffffffffu || wanted > arena.MaxOrder || arena.MaxOrder > 23u)
        return M8_FLOWER_ARENA_CAPACITY;
    if (metadata.Load(M8FlowerArenaNode(arena, 0u)) < wanted + 1u)
        return M8_FLOWER_ARENA_CAPACITY;
    uint node = 0u, order = arena.MaxOrder, offset = 0u;
    [loop] while (order > wanted)
    {
        uint left = 2u * node + 1u, right = left + 1u;
        if (metadata.Load(M8FlowerArenaNode(arena, node)) == order + 1u)
        {
            metadata.Store(M8FlowerArenaNode(arena, left), order);
            metadata.Store(M8FlowerArenaNode(arena, right), order);
        }
        order--;
        if (metadata.Load(M8FlowerArenaNode(arena, left)) >= wanted + 1u)
            node = left;
        else
        {
            node = right;
            offset += 1u << (order + M8_FLOWER_ARENA_MIN_SHIFT);
        }
    }
    metadata.Store(M8FlowerArenaNode(arena, node), 0u);
    [loop] while (node != 0u)
    {
        node = (node - 1u) >> 1u;
        uint left = 2u * node + 1u;
        uint available = max(metadata.Load(M8FlowerArenaNode(arena, left)),
            metadata.Load(M8FlowerArenaNode(arena, left + 1u)));
        metadata.Store(M8FlowerArenaNode(arena, node), available);
    }
    capacity = 1u << (wanted + M8_FLOWER_ARENA_MIN_SHIFT);
    address = arena.Data + offset;
    uint used = metadata.Load(arena.Control + 4u);
    metadata.Store(arena.Control + 4u, used + capacity);
    return M8_FLOWER_ARENA_OK;
}

uint M8FlowerArenaAllocate(RWByteAddressBuffer metadata,
    M8FlowerArena arena, uint bytes, out uint address, out uint capacity)
{
    address = M8_FLOWER_ARENA_INVALID_REF;
    capacity = 0u;
    if (!M8FlowerArenaAcquire(metadata, arena)) return M8_FLOWER_ARENA_BUSY;
    uint result = M8FlowerArenaAllocateLocked(metadata, arena, bytes, address, capacity);
    M8FlowerArenaRelease(metadata, arena);
    return result;
}

// The caller must first retire every raw reader of this allocation. An epoch
// mismatch hides stale state; it does not by itself permit memory reclamation.
bool M8FlowerArenaFreeLocked(RWByteAddressBuffer metadata,
    M8FlowerArena arena, uint address, uint capacity)
{
    if (address < arena.Data || capacity < 256u ||
        (capacity & (capacity - 1u)) != 0u) return false;
    uint order = (uint)firstbithigh(capacity) - M8_FLOWER_ARENA_MIN_SHIFT;
    uint offset = address - arena.Data;
    uint size = 1u << (arena.MaxOrder + M8_FLOWER_ARENA_MIN_SHIFT);
    if (order > arena.MaxOrder || (offset & (capacity - 1u)) != 0u ||
        offset > size - capacity) return false;
    uint node = (1u << (arena.MaxOrder - order)) - 1u + (offset / capacity);
    if (metadata.Load(M8FlowerArenaNode(arena, node)) != 0u) return false;
    metadata.Store(M8FlowerArenaNode(arena, node), order + 1u);
    [loop] while (node != 0u)
    {
        node = (node - 1u) >> 1u;
        order++;
        uint left = 2u * node + 1u;
        uint a = metadata.Load(M8FlowerArenaNode(arena, left));
        uint b = metadata.Load(M8FlowerArenaNode(arena, left + 1u));
        metadata.Store(M8FlowerArenaNode(arena, node),
            a == order && b == order ? order + 1u : max(a, b));
    }
    uint used = metadata.Load(arena.Control + 4u);
    metadata.Store(arena.Control + 4u, used - capacity);
    return true;
}

#endif
