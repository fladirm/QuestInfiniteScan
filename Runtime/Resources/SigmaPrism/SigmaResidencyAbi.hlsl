#ifndef SIGMA_RESIDENCY_ABI_INCLUDED
#define SIGMA_RESIDENCY_ABI_INCLUDED

// N5 residency is disposable execution state.  The signed logical coordinate,
// selected-root lease context and exact page generation form the complete key;
// no hash, bucket, segment or physical slot is canonical identity.
#define SIGMA_RESIDENCY_ABSENT_IN_ROOT 0u
#define SIGMA_RESIDENCY_COLD_DURABLE 1u
#define SIGMA_RESIDENCY_LOADING 2u
#define SIGMA_RESIDENCY_HOT_CLEAN 3u
#define SIGMA_RESIDENCY_HOT_DIRTY 4u
#define SIGMA_RESIDENCY_EVICTING 5u
#define SIGMA_RESIDENCY_QUARANTINED 6u

#define SIGMA_RESIDENCY_INVALID 0xffffffffu
#define SIGMA_RESIDENCY_LOCATOR_WORDS 16u
#define SIGMA_RESIDENCY_LOCATOR_BYTES 64u
#define SIGMA_RESIDENCY_SLOT_WORDS 16u

#define SIGMA_RESIDENCY_ENTRY_CLAIM 0u
#define SIGMA_RESIDENCY_ENTRY_STATE 1u
#define SIGMA_RESIDENCY_ENTRY_HASH 2u
#define SIGMA_RESIDENCY_ENTRY_FLAGS 3u
#define SIGMA_RESIDENCY_ENTRY_PAGE_X_LO 4u
#define SIGMA_RESIDENCY_ENTRY_PAGE_X_HI 5u
#define SIGMA_RESIDENCY_ENTRY_PAGE_Y_LO 6u
#define SIGMA_RESIDENCY_ENTRY_PAGE_Y_HI 7u
#define SIGMA_RESIDENCY_ENTRY_ROOT_LO 8u
#define SIGMA_RESIDENCY_ENTRY_ROOT_HI 9u
#define SIGMA_RESIDENCY_ENTRY_PAGE_GENERATION 10u
#define SIGMA_RESIDENCY_ENTRY_RESIDENT_GENERATION 11u
#define SIGMA_RESIDENCY_ENTRY_SEGMENT 12u
#define SIGMA_RESIDENCY_ENTRY_SLOT 13u
#define SIGMA_RESIDENCY_ENTRY_GLOBAL_SLOT 14u
#define SIGMA_RESIDENCY_ENTRY_RESERVED 15u

#define SIGMA_RESIDENCY_CLAIM_EMPTY 0u
#define SIGMA_RESIDENCY_CLAIM_WRITING 1u
#define SIGMA_RESIDENCY_CLAIM_READY 2u

#define SIGMA_RESIDENCY_UPDATE_UPSERT 1u
#define SIGMA_RESIDENCY_UPDATE_PUBLISH_LOADED 2u
#define SIGMA_RESIDENCY_UPDATE_EVICT 3u
#define SIGMA_RESIDENCY_UPDATE_RETIRE_PHYSICAL 4u

#define SIGMA_RESIDENCY_SLOT_SHADOW 1u

#define SIGMA_RESIDENCY_RESULT_APPLIED 1u
#define SIGMA_RESIDENCY_RESULT_FOUND 2u
#define SIGMA_RESIDENCY_RESULT_NOT_FOUND 3u
#define SIGMA_RESIDENCY_RESULT_TABLE_FULL 4u
#define SIGMA_RESIDENCY_RESULT_INVALID 5u
#define SIGMA_RESIDENCY_RESULT_STATE_CONFLICT 6u
#define SIGMA_RESIDENCY_RESULT_SLOT_CONFLICT 7u

struct SigmaResidencyUpdateGpu
{
    uint4 Coordinate;
    uint2 RootContext;
    uint pageGeneration;
    uint residentGeneration;
    uint segment;
    uint slot;
    uint globalSlot;
    uint state;
    uint operation;
    uint expectedState;
    uint flags;
    uint reserved;
};

struct SigmaResidentSlotGpu
{
    uint4 Coordinate;
    uint2 RootContext;
    uint pageGeneration;
    uint residentGeneration;
    uint segment;
    uint slot;
    uint state;
    uint locatorBucket;
    uint lastReaderTicket;
    uint lastWriterTicket;
    uint leaseCount;
    uint flags;
};

uint SigmaResidencyHashWord(uint hash, uint word)
{
    // Hash is only a probe start.  Full key equality below is mandatory.
    hash ^= word;
    hash *= 16777619u;
    hash ^= hash >> 13u;
    return hash;
}

uint SigmaResidencyHashKey(uint4 coordinate, uint2 rootContext,
    uint pageGeneration)
{
    uint hash = 2166136261u;
    hash = SigmaResidencyHashWord(hash, coordinate.x);
    hash = SigmaResidencyHashWord(hash, coordinate.y);
    hash = SigmaResidencyHashWord(hash, coordinate.z);
    hash = SigmaResidencyHashWord(hash, coordinate.w);
    hash = SigmaResidencyHashWord(hash, rootContext.x);
    hash = SigmaResidencyHashWord(hash, rootContext.y);
    return SigmaResidencyHashWord(hash, pageGeneration);
}

bool SigmaResidencyStateValid(uint state)
{
    return state <= SIGMA_RESIDENCY_QUARANTINED;
}

bool SigmaResidencyStateHasSlot(uint state)
{
    return state == SIGMA_RESIDENCY_LOADING ||
        state == SIGMA_RESIDENCY_HOT_CLEAN ||
        state == SIGMA_RESIDENCY_HOT_DIRTY ||
        state == SIGMA_RESIDENCY_EVICTING ||
        state == SIGMA_RESIDENCY_QUARANTINED;
}

bool SigmaResidencyTransitionAllowed(uint prior, uint next)
{
    if (!SigmaResidencyStateValid(prior) ||
        !SigmaResidencyStateValid(next))
        return false;
    if (prior == next || next == SIGMA_RESIDENCY_QUARANTINED)
        return true;
    if (prior == SIGMA_RESIDENCY_COLD_DURABLE)
        return next == SIGMA_RESIDENCY_LOADING;
    if (prior == SIGMA_RESIDENCY_LOADING)
        return next == SIGMA_RESIDENCY_HOT_CLEAN;
    if (prior == SIGMA_RESIDENCY_HOT_CLEAN)
        return next == SIGMA_RESIDENCY_HOT_DIRTY ||
            next == SIGMA_RESIDENCY_EVICTING;
    if (prior == SIGMA_RESIDENCY_HOT_DIRTY)
        return next == SIGMA_RESIDENCY_HOT_CLEAN;
    if (prior == SIGMA_RESIDENCY_EVICTING)
        return next == SIGMA_RESIDENCY_COLD_DURABLE ||
            next == SIGMA_RESIDENCY_HOT_CLEAN;
    return false;
}

bool SigmaResidencyKeyEqual(SigmaResidentSlotGpu slot,
    SigmaResidencyUpdateGpu update)
{
    return all(slot.Coordinate == update.Coordinate) &&
        all(slot.RootContext == update.RootContext) &&
        slot.pageGeneration == update.pageGeneration;
}

#endif
