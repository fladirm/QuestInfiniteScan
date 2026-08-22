#ifndef SIGMA_OPERATOR_PLAN_INCLUDED
#define SIGMA_OPERATOR_PLAN_INCLUDED

#include "Sedenion16.hlsl"

StructuredBuffer<uint> _SigmaExactBackendGate;

bool SigmaCanonicalMutationEnabled()
{
    return _SigmaExactBackendGate[0] == 1u;
}

// These fixed operators are lowered from the same generated signed-XOR and
// Hadamard descriptors consumed by the CPU plan builder. No dense schoolbook
// multiplication is used for conjugation, basis/dyad action, G or F.

void SigmaConjugatePlan(uint2 inputState[16], out uint2 outputState[16],
    inout uint valid)
{
    outputState[0] = inputState[0];
    [unroll]
    for (uint lane = 1u; lane < 16u; ++lane)
        outputState[lane] = SigmaQ48NegateChecked(inputState[lane], valid);
}

bool SigmaHadamardButterflySafe(uint2 inputState[16])
{
    // If every input magnitude is <= INT64_MAX/16, every partial sum in any
    // 16-point Walsh-Hadamard association is representable. In that domain the
    // four-stage butterfly is bit-identical to the generated row plan and cannot
    // change checked-overflow validity. Near the signed limits we deliberately
    // fall back to the canonical row association below.
    const uint2 limit = uint2(0xffffffffu, 0x07ffffffu);
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        if (SigmaU64Less(limit, SigmaU64AbsSigned(inputState[lane])))
            return false;
    return true;
}

void SigmaHadamardButterfly(uint2 inputState[16], out uint2 outputState[16],
    inout uint valid)
{
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        outputState[lane] = inputState[lane];
    [unroll]
    for (uint span = 1u; span < 16u; span <<= 1u)
    {
        [unroll]
        for (uint base = 0u; base < 16u; base += (span << 1u))
        {
            [unroll]
            for (uint offset = 0u; offset < 8u; ++offset)
            {
                if (offset >= span)
                    continue;
                uint aIndex = base + offset;
                uint bIndex = aIndex + span;
                uint2 a = outputState[aIndex];
                uint2 b = outputState[bIndex];
                outputState[aIndex] = SigmaQ48AddChecked(a, b, valid);
                outputState[bIndex] = SigmaQ48SubChecked(a, b, valid);
            }
        }
    }
}

void SigmaHadamardBPlan(uint2 inputState[16], out uint2 outputState[16],
    inout uint valid)
{
    if (SigmaHadamardButterflySafe(inputState))
    {
        SigmaHadamardButterfly(inputState, outputState, valid);
        return;
    }
    [unroll]
    for (uint row = 0u; row < 16u; ++row)
        outputState[row] = SigmaHadamardRow(inputState, row, valid);
}

void SigmaGeometryGPlan(uint2 inputState[16], out uint2 geometry[4],
    inout uint valid)
{
    [unroll]
    for (uint row = 0u; row < 4u; ++row)
        geometry[row] = SigmaHadamardRow(inputState,
            SIGMA_GEOMETRY_ROWS[row], valid);
}

void SigmaHiddenFPlan(uint2 inputState[16], out uint2 hidden[12],
    inout uint valid)
{
    if (SigmaHadamardButterflySafe(inputState))
    {
        uint2 transformed[16];
        SigmaHadamardButterfly(inputState, transformed, valid);
        [unroll]
        for (uint row = 0u; row < 12u; ++row)
            hidden[row] = transformed[SIGMA_HIDDEN_ROWS[row]];
        return;
    }
    [unroll]
    for (uint row = 0u; row < 12u; ++row)
        hidden[row] = SigmaHadamardRow(inputState,
            SIGMA_HIDDEN_ROWS[row], valid);
}

uint2 SigmaProjectiveMeetLower(uint2 lowerA, uint2 lowerB)
{
    return SigmaQ48Max(lowerA, lowerB);
}

uint2 SigmaProjectiveMeetUpper(uint2 upperA, uint2 upperB)
{
    return SigmaQ48Min(upperA, upperB);
}

uint2 SigmaProjectiveClamp(uint2 prior, uint2 lower, uint2 upper)
{
    return SigmaQ48Min(SigmaQ48Max(prior, lower), upper);
}

#endif
