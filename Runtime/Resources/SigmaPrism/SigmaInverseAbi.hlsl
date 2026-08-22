#ifndef SIGMA_INVERSE_ABI_INCLUDED
#define SIGMA_INVERSE_ABI_INCLUDED

#include "SigmaCarrierAbi.hlsl"

#define SIGMA_DEPTH_VIEW_STRIDE 36u

#define SIGMA_CAL_FX 0u
#define SIGMA_CAL_FY 1u
#define SIGMA_CAL_CX 2u
#define SIGMA_CAL_CY 3u
#define SIGMA_CAL_R00 4u
#define SIGMA_CAL_TX 13u
#define SIGMA_CAL_NEAR 16u
#define SIGMA_CAL_FAR 17u
#define SIGMA_CAL_POSE_WIDTH 18u
#define SIGMA_CAL_RANGE_THRESHOLDS 19u
#define SIGMA_CAL_RANGE_WIDTHS 25u
#define SIGMA_CAL_PRIOR_FLOOR 31u
#define SIGMA_CAL_PRIOR_CEILING 32u
#define SIGMA_CAL_CONTACT_MASS_MIN 33u

#define SIGMA_SOURCE_PRIOR 0u
#define SIGMA_SOURCE_DEPTH_LEFT 1u
#define SIGMA_SOURCE_DEPTH_RIGHT 2u

#define SIGMA_SECTOR_NO_CONSTRAINT 0u
#define SIGMA_SECTOR_HIT 1u
#define SIGMA_SECTOR_PRE_HIT_EXCLUSION 2u

#define SIGMA_PROPOSAL_ACCEPTED (1u << 0u)
#define SIGMA_PROPOSAL_CHANGED (1u << 1u)
#define SIGMA_PROPOSAL_HIT_LEFT (1u << 2u)
#define SIGMA_PROPOSAL_HIT_RIGHT (1u << 3u)
#define SIGMA_PROPOSAL_CONFLICT (1u << 4u)
#define SIGMA_PROPOSAL_EXCLUSION (1u << 5u)
#define SIGMA_PROPOSAL_INVALID (1u << 6u)

#define SIGMA_COUNTER_ACTIVE_PAGES 0u
#define SIGMA_COUNTER_HIT_SAMPLES 1u
#define SIGMA_COUNTER_CHANGED_SAMPLES 2u
#define SIGMA_COUNTER_EMPTY_MEETS 3u
#define SIGMA_COUNTER_EXCLUSIONS 4u
#define SIGMA_COUNTER_UNMATCHED_BLOCKS 5u
#define SIGMA_COUNTER_PROMOTED_SAMPLES 6u
#define SIGMA_COUNTER_FAILED_CHECKS 7u
#define SIGMA_COUNTER_COUNT 8u

struct SigmaQ48Bounds
{
    uint2 lo;
    uint2 hi;
};

struct SigmaDepthCell3
{
    SigmaQ48Bounds axis[3];
    uint sourceClass;
    uint independenceKey;
    uint sector;
    uint valid;
};

// A transient exact incompatibility/exclusion record. It is evidence about one
// carrier preimage and can never render as another physical surface.
struct SigmaDepthConflictGpu
{
    uint4 carrierPage;
    uint4 stateAndSector; // generation, sample, sector mask, conflict-axis mask
    uint4 provenance;    // lo sources, hi sources, left key, right key
    uint2 gapX;
    uint2 gapY;
    uint2 gapZ;
};

#endif
