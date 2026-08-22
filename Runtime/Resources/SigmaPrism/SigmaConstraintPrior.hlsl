#ifndef SIGMA_CONSTRAINT_PRIOR_INCLUDED
#define SIGMA_CONSTRAINT_PRIOR_INCLUDED

#include "SigmaConstraintLedgerAbi.hlsl"

// Durable certificates narrow the projective prior of the same Psi sample.
// The lane form is authoritative for cooperative 16D kernels; the array form
// is a semantic convenience for scalar fixtures and non-coordinate layouts.
StructuredBuffer<SigmaConstraintCertificateGpu> _ConstraintCertificates;
StructuredBuffer<SigmaQ48Bounds> _ConstraintCertificateBounds;
StructuredBuffer<SigmaConstraintBlockGpu> _ConstraintBlocks;
uint _ConstraintProofCapacity;

bool SigmaCertificateContainsSample(uint2 mask, uint sample)
{
    return sample < 32u
        ? (mask.x & (1u << sample)) != 0u
        : (mask.y & (1u << (sample - 32u))) != 0u;
}

bool SigmaApplyConstraintPriorLane(SigmaCarrierPageMetaGpu metadata,
    uint sample, uint lane, inout SigmaQ48Bounds prior)
{
    if (metadata.certificateCount == 0u)
        return true;
    if (metadata.certificateCount != SIGMA_CERTIFICATES_PER_PAGE ||
        metadata.certificateOffsetHi != 0u ||
        metadata.certificateOffsetLo % SIGMA_CERTIFICATES_PER_PAGE != 0u ||
        sample >= SIGMA_PAGE_SAMPLE_COUNT || lane >= SIGMA_LANE_COUNT)
        return false;

    uint proofSlot = metadata.certificateOffsetLo /
        SIGMA_CERTIFICATES_PER_PAGE;
    if (proofSlot >= _ConstraintProofCapacity)
        return false;
    uint block = SigmaProofBlockForPageSample(sample);
    uint local = SigmaProofLocalForPageSample(sample);
    SigmaConstraintBlockGpu proof = _ConstraintBlocks[
        SigmaConstraintBlockAddress(proofSlot, block)];
    if (proof.counts.x > SIGMA_CERTIFICATES_PER_BLOCK ||
        proof.counts.y > SIGMA_CERTIFICATE_BOUNDS_PER_BLOCK)
        return false;

    [loop]
    for (uint certificateIndex = 0u;
        certificateIndex < proof.counts.x; ++certificateIndex)
    {
        SigmaConstraintCertificateGpu certificate = _ConstraintCertificates[
            SigmaCertificateAddress(proofSlot, block, certificateIndex)];
        if ((certificate.range.w & SIGMA_CERTIFICATE_ACTIVE) == 0u ||
            (certificate.range.w & SIGMA_CERTIFICATE_UNRESOLVED) != 0u ||
            !SigmaCertificateContainsSample(certificate.sampleMask, local))
            continue;
        uint mask = certificate.identity.x;
        uint cursor = certificate.range.y;
        uint end = cursor + certificate.range.z;
        if (certificate.range.z != countbits(mask) || end < cursor ||
            end > proof.counts.y ||
            end > SIGMA_CERTIFICATE_BOUNDS_PER_BLOCK)
            return false;
        uint laneBit = 1u << lane;
        if ((mask & laneBit) == 0u)
            continue;
        uint precedingMask = lane == 0u ? 0u : mask & (laneBit - 1u);
        uint bound = cursor + countbits(precedingMask);
        SigmaQ48Bounds certificateBound = _ConstraintCertificateBounds[
            SigmaCertificateBoundAddress(proofSlot, block, bound)];
        prior.lo = SigmaQ48Max(prior.lo, certificateBound.lo);
        prior.hi = SigmaQ48Min(prior.hi, certificateBound.hi);
        if (SigmaQ48Less(prior.hi, prior.lo))
            return false;
    }
    return true;
}

bool SigmaApplyConstraintPrior(SigmaCarrierPageMetaGpu metadata, uint sample,
    inout SigmaQ48Bounds prior[16])
{
    bool valid = true;
    [unroll]
    for (uint lane = 0u; lane < SIGMA_LANE_COUNT; ++lane)
        valid = SigmaApplyConstraintPriorLane(metadata, sample, lane,
            prior[lane]) && valid;
    return valid;
}

#endif
