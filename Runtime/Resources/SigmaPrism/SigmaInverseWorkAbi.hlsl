#ifndef SIGMA_INVERSE_WORK_ABI_INCLUDED
#define SIGMA_INVERSE_WORK_ABI_INCLUDED

#define SIGMA_INVERSE_WORK_MATCHED 1u
#define SIGMA_INVERSE_WORK_GAUGE 2u
#define SIGMA_INVERSE_INVALID_SLOT 0xffffffffu

// Indirect dispatch ABI:
//   RGB     = (256 sample phases, matchedEyeCount, 1)
//   SOLVE   = (64 carrier rows, matchedWorkCount, 1)
//   PROMOTE = (8 local X groups, 8 local Y groups, gaugeWorkCount)
//   PROOF   = (64 carrier blocks, totalWorkCount, 1)
//   COMMIT  = (1, totalWorkCount, 1)
// A zero work/gauge count always writes a zero dispatch axis during compaction;
// stale indirect dimensions may never authorize work.

// 48 bytes.  This is disposable execution scheduling metadata for a transaction
// over the one Psi carrier, never a second reconstruction state.
struct SigmaInverseWorkGpu
{
    uint4 slots;      // source carrier, target carrier, source proof, target proof
    uint4 control;    // kind, image origin x/y packed, block mask, work revision
    uint4 coordinate; // signed-64 logical page x/y limbs for target publication
};

uint SigmaInverseWorkImageX(SigmaInverseWorkGpu work)
{
    return work.control.y & 0xffffu;
}

uint SigmaInverseWorkImageY(SigmaInverseWorkGpu work)
{
    return work.control.y >> 16u;
}

#endif
