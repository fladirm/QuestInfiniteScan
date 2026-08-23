#ifndef SIGMA_INVERSE_WORK_ABI_INCLUDED
#define SIGMA_INVERSE_WORK_ABI_INCLUDED

#include "SigmaStreamingAbi.hlsl"

// Disposable inverse execution ABI retained by both the bounded streaming
// lowering and the direct exact fixtures.  It never owns canonical state.
#define SIGMA_INVERSE_WORK_MATCHED 1u
#define SIGMA_INVERSE_WORK_GAUGE 2u
#define SIGMA_INVERSE_INVALID_SLOT 0xffffffffu

// 48 bytes.  Page coordinates are intrinsic signed-64 Sigma_2 addresses;
// image origin is only source sampling metadata.
struct SigmaInverseWorkGpu
{
    uint4 slots;
    uint4 control;
    uint4 coordinate;
};

uint SigmaInverseWorkImageX(SigmaInverseWorkGpu work)
{
    return work.control.y & 0xffffu;
}

uint SigmaInverseWorkImageY(SigmaInverseWorkGpu work)
{
    return work.control.y >> 16u;
}

// Execution geometry only. A proof block, microtile, source-handle segment and
// candidate window bound one dispatch. None bounds the canonical evidence set.
#define SIGMA_STREAM_WORK_ITEMS_PER_OPCODE 64u
#define SIGMA_STREAM_RAW_TILES_PER_BUNDLE 64u
#define SIGMA_STREAM_RAW_WORD4_PER_SAMPLE 6u
#define SIGMA_STREAM_RAW_WORD4_PER_TILE \
    (64u * SIGMA_STREAM_RAW_WORD4_PER_SAMPLE)
#define SIGMA_STREAM_RAW_WORD4_PER_BUNDLE \
    (SIGMA_PAGE_SAMPLE_COUNT * SIGMA_STREAM_RAW_WORD4_PER_SAMPLE)

// GPU-owned scheduler words. Host code records fixed submissions and never
// chooses a transaction or interprets these words as mutation authority.
#define SIGMA_STREAM_CONTROL_TICKET_LO 0u
#define SIGMA_STREAM_CONTROL_TICKET_HI 1u
#define SIGMA_STREAM_CONTROL_GAUGE_ORDINAL 2u
#define SIGMA_STREAM_CONTROL_INGRESS_CURSOR 3u
#define SIGMA_STREAM_CONTROL_INGRESS_COUNT 4u
#define SIGMA_STREAM_CONTROL_SKIPPED_ADMISSION 5u
#define SIGMA_STREAM_CONTROL_PUBLISHED_COUNT 6u
#define SIGMA_STREAM_CONTROL_DORMANT_COUNT 7u
#define SIGMA_STREAM_CONTROL_ACTIVE_COUNT 8u
#define SIGMA_STREAM_CONTROL_REVALIDATE_OWNER 9u
#define SIGMA_STREAM_CONTROL_PROBATION_COUNT 10u
#define SIGMA_STREAM_CONTROL_FAILED_COUNT 11u
#define SIGMA_STREAM_CONTROL_CLASS_DEFICIT 12u
#define SIGMA_STREAM_CONTROL_CLASS_BUDGET 17u
#define SIGMA_STREAM_CONTROL_PROOF_OWNER 22u
#define SIGMA_STREAM_CONTROL_SOURCE_SEGMENT_HINT 23u
#define SIGMA_STREAM_CONTROL_PROOF_SPILL_COUNT 24u
#define SIGMA_STREAM_CONTROL_PENDING_PHASE_FAULTS 25u
#define SIGMA_STREAM_CONTROL_PENDING_OWNER_MISMATCH 26u
#define SIGMA_STREAM_CONTROL_PENDING_INGRESS_EXHAUSTION 27u

#define SIGMA_STREAM_PROBATION_FREE 0u
#define SIGMA_STREAM_PROBATION_OPEN 1u
#define SIGMA_STREAM_PROBATION_SEALED 2u

#define SIGMA_STREAM_WORK_FLAG_NEEDS_REVALIDATION (1u << 0u)
#define SIGMA_STREAM_WORK_FLAG_GAUGE (1u << 1u)
#define SIGMA_STREAM_WORK_FLAG_DEPENDENCY_WAIT (1u << 2u)
#define SIGMA_STREAM_WORK_FLAG_SOURCE_STREAM (1u << 3u)
#define SIGMA_STREAM_WORK_FLAG_PROOF_SPILLED (1u << 4u)

#define SIGMA_STREAM_BUNDLE_FLAG_MATCHED (1u << 0u)
#define SIGMA_STREAM_BUNDLE_FLAG_GAUGE (1u << 1u)
#define SIGMA_STREAM_BUNDLE_FLAG_HAS_ASSOCIATION (1u << 2u)
#define SIGMA_STREAM_BUNDLE_FLAG_NEEDS_REVALIDATION (1u << 3u)
#define SIGMA_STREAM_BUNDLE_FLAG_DUAL_DEPTH (1u << 4u)
#define SIGMA_STREAM_BUNDLE_FLAG_DEPTH_RGB (1u << 5u)
#define SIGMA_STREAM_BUNDLE_FLAG_TEMPORAL_DEPTH (1u << 6u)
#define SIGMA_STREAM_BUNDLE_FLAG_STREAMED (1u << 7u)
#define SIGMA_STREAM_BUNDLE_FLAG_DORMANT_PROPOSAL (1u << 8u)

uint SigmaStreamWorkAddress(uint opcode, uint item)
{
    return opcode * SIGMA_STREAM_WORK_ITEMS_PER_OPCODE + item;
}

uint SigmaStreamDispatchAddress(uint opcode, uint axis)
{
    return opcode * 3u + axis;
}

uint SigmaStreamRawWordAddress(uint rawTileBase, uint sample, uint word)
{
    return rawTileBase * SIGMA_STREAM_RAW_WORD4_PER_TILE +
        sample * SIGMA_STREAM_RAW_WORD4_PER_SAMPLE + word;
}

uint SigmaStreamRawTileAddress(uint rawTileBase, uint block)
{
    return rawTileBase + block;
}

uint SigmaStreamBundleGeneration(SigmaSealedSourceBundleGpu bundle)
{
    return bundle.identity.y;
}

uint SigmaStreamBundleState(SigmaSealedSourceBundleGpu bundle)
{
    return bundle.identity.x;
}

uint SigmaStreamTransactionState(SigmaTransactionGpu transaction)
{
    return transaction.identity.x;
}

uint SigmaStreamTransactionGeneration(SigmaTransactionGpu transaction)
{
    return transaction.identity.y;
}

uint2 SigmaStreamTicket(SigmaTransactionGpu transaction)
{
    return transaction.ticket.xy;
}

bool SigmaStreamTicketLess(uint2 left, uint2 right)
{
    return left.y < right.y || (left.y == right.y && left.x < right.x);
}

bool SigmaStreamTicketEqual(uint2 left, uint2 right)
{
    return all(left == right);
}

bool SigmaStreamHandleValid(uint slot, uint generation, uint capacity)
{
    return slot < capacity && generation != 0u;
}

uint2 SigmaStreamSourceHandle(SigmaSourceHandleSegmentGpu segment,
    uint ordinal)
{
    uint4 pair = ordinal < 2u ? segment.handle01 :
        ordinal < 4u ? segment.handle23 :
        ordinal < 6u ? segment.handle45 : segment.handle67;
    return (ordinal & 1u) == 0u ? pair.xy : pair.zw;
}

void SigmaStreamSetSourceHandle(inout SigmaSourceHandleSegmentGpu segment,
    uint ordinal, uint2 handle)
{
    if (ordinal < 2u)
    {
        if ((ordinal & 1u) == 0u)
            segment.handle01.xy = handle;
        else
            segment.handle01.zw = handle;
    }
    else if (ordinal < 4u)
    {
        if ((ordinal & 1u) == 0u)
            segment.handle23.xy = handle;
        else
            segment.handle23.zw = handle;
    }
    else if (ordinal < 6u)
    {
        if ((ordinal & 1u) == 0u)
            segment.handle45.xy = handle;
        else
            segment.handle45.zw = handle;
    }
    else
    {
        if ((ordinal & 1u) == 0u)
            segment.handle67.xy = handle;
        else
            segment.handle67.zw = handle;
    }
}

bool SigmaStreamSourceSegmentValid(SigmaSourceHandleSegmentGpu segment,
    uint generation)
{
    return segment.identity.x != SIGMA_STREAM_SOURCE_SEGMENT_FREE &&
        segment.identity.y == generation && generation != 0u &&
        segment.identity.z <= SIGMA_STREAM_SOURCE_HANDLE_WINDOW;
}

bool SigmaStreamMaskIntersects(uint4 leftLo, uint4 leftHi,
    uint4 rightLo, uint4 rightHi)
{
    return any((leftLo & rightLo) != 0u) ||
        any((leftHi & rightHi) != 0u);
}

uint SigmaStreamFirstSetBit(uint value)
{
    int bit = firstbitlow(value);
    return bit < 0 ? SIGMA_STREAM_INVALID : (uint)bit;
}

uint SigmaStreamFirstIncompleteBlock(SigmaTransactionGpu transaction)
{
    [unroll]
    for (uint word = 0u; word < 4u; ++word)
    {
        uint pending = transaction.affectedMaskLo[word] &
            ~transaction.completedMaskLo[word];
        if (pending != 0u)
            return word * 32u + SigmaStreamFirstSetBit(pending);
    }
    [unroll]
    for (uint word = 0u; word < 4u; ++word)
    {
        uint pending = transaction.affectedMaskHi[word] &
            ~transaction.completedMaskHi[word];
        if (pending != 0u)
            return 128u + word * 32u + SigmaStreamFirstSetBit(pending);
    }
    return SIGMA_STREAM_INVALID;
}

void SigmaStreamSetCompletedBlock(inout SigmaTransactionGpu transaction,
    uint block)
{
    uint word = (block >> 5u) & 3u;
    uint bit = 1u << (block & 31u);
    if (block < 128u)
        transaction.completedMaskLo[word] |= bit;
    else
        transaction.completedMaskHi[word] |= bit;
}

#endif
