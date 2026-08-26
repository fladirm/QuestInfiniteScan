#ifndef SIGMA_INVERSE_ABI_INCLUDED
#define SIGMA_INVERSE_ABI_INCLUDED

// Representation-neutral calibrated depth-query record retained by the pose
// query boundary. No source-cell, proposal or canonical support identity lives
// in this ABI.
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

struct SigmaQ48Bounds
{
    uint2 lo;
    uint2 hi;
};

#endif
