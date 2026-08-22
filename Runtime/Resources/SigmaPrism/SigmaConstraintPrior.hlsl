#ifndef SIGMA_CONSTRAINT_PRIOR_INCLUDED
#define SIGMA_CONSTRAINT_PRIOR_INCLUDED

#include "SigmaConstraintLedgerAbi.hlsl"

// Durable certificates narrow the same projective prior used by the current
// four-stream inverse.  They are proof metadata for Psi, never another state.
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

bool SigmaApplyConstraintPrior(SigmaCarrierPageMetaGpu metadata, uint sample,
    inout SigmaQ48Bounds prior[16])
{
    if (metadata.certificateCount == 0u)
        return true;
    if (metadata.certificateCount != SIGMA_CERTIFICATES_PER_PAGE ||
        metadata.certificateOffsetHi != 0u ||
        metadata.certificateOffsetLo % SIGMA_CERTIFICATES_PER_PAGE != 0u)
        return false;
    uint proofSlot = metadata.certificateOffsetLo /
        SIGMA_CERTIFICATES_PER_PAGE;
    if (proofSlot >= _ConstraintProofCapacity || sample >= SIGMA_PAGE_SAMPLE_COUNT)
        return false;
    uint block = SigmaProofBlockForPageSample(sample);
    uint local = SigmaProofLocalForPageSample(sample);
    SigmaConstraintBlockGpu proof = _ConstraintBlocks[
        SigmaConstraintBlockAddress(proofSlot, block)];
    if (proof.counts.x > SIGMA_CERTIFICATES_PER_BLOCK ||
        proof.counts.y > SIGMA_CERTIFICATE_BOUNDS_PER_BLOCK)
        return false;
    [loop]
    for (uint certificateIndex = 0u; certificateIndex < proof.counts.x;
        ++certificateIndex)
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
        if (certificate.range.z != countbits(mask) ||
            end < cursor || end > proof.counts.y ||
            end > SIGMA_CERTIFICATE_BOUNDS_PER_BLOCK)
            return false;
        [loop]
        for (uint lane = 0u; lane < 16u; ++lane)
        {
            if ((mask & (1u << lane)) == 0u)
                continue;
            SigmaQ48Bounds bound = _ConstraintCertificateBounds[
                SigmaCertificateBoundAddress(proofSlot, block, cursor++)];
            prior[lane].lo = SigmaQ48Max(prior[lane].lo, bound.lo);
            prior[lane].hi = SigmaQ48Min(prior[lane].hi, bound.hi);
            if (SigmaQ48Less(prior[lane].hi, prior[lane].lo))
                return false;
        }
    }
    return true;
}

#endif
