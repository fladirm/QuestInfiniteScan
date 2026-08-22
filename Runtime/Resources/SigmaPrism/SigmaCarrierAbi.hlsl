#ifndef SIGMA_CARRIER_ABI_INCLUDED
#define SIGMA_CARRIER_ABI_INCLUDED

#define SIGMA_PAGE_SIZE 64u
#define SIGMA_PAGE_SAMPLE_COUNT 4096u
#define SIGMA_PAGE_BLOCK_SIZE 8u
#define SIGMA_PAGE_LANE_COUNT 65536u
#define SIGMA_LANE_COUNT 16u

#define SIGMA_PAGE_ALLOCATED 1u
#define SIGMA_PAGE_PUBLISHED 2u
#define SIGMA_PAGE_DIRTY 4u

// Must remain byte-identical to SigmaCarrier.PageMetadataStride. This record is
// scheduling/proof metadata; decoded Q16.48 samples remain the only physical state.
struct SigmaCarrierPageMetaGpu
{
    uint pageXLo;
    uint pageXHi;
    uint pageYLo;
    uint pageYHi;
    uint generation;
    uint revision;
    uint certificateOffsetLo;
    uint certificateOffsetHi;
    uint certificateCount;
    uint flags;
    uint reserved0;
    uint reserved1;
};

#endif
