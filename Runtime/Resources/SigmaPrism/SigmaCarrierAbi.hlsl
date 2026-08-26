#ifndef SIGMA_CARRIER_ABI_INCLUDED
#define SIGMA_CARRIER_ABI_INCLUDED

#define SIGMA_PAGE_SIZE 64u
#define SIGMA_PAGE_SAMPLE_COUNT 4096u
#define SIGMA_PAGE_BLOCK_SIZE 8u
#define SIGMA_PAGE_LANE_COUNT 65536u
#define SIGMA_LANE_COUNT 16u
#define SIGMA_REPRESENTATION_WORDS_PER_SAMPLE 18u
#define SIGMA_REPRESENTATION_GAUGE_COORDINATE 0u
#define SIGMA_REPRESENTATION_GAUGE_PLAN 1u
#define SIGMA_REPRESENTATION_CERTIFICATE 2u
#define SIGMA_CERTIFICATE_WORD_COUNT 16u
#define SIGMA_CERTIFICATE_IDENTITY 0u
#define SIGMA_CERTIFICATE_CONTEXT 1u
#define SIGMA_CERTIFICATE_INDEPENDENCE 2u
#define SIGMA_CERTIFICATE_RELATION 3u
#define SIGMA_CERTIFICATE_AXIS0 4u
#define SIGMA_CERTIFICATE_INFORMATION0 8u
#define SIGMA_CERTIFICATE_RECEIPTS0 12u

#define SIGMA_PAGE_ALLOCATED 1u
#define SIGMA_PAGE_PUBLISHED 2u
#define SIGMA_PAGE_DIRTY 4u
#define SIGMA_REPRESENTATION_HAS_GAUGE 1u
#define SIGMA_REPRESENTATION_HAS_CERTIFICATE 2u

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
    uint gaugeGeneration;
    uint certificateGeneration;
    uint representationFlags;
    uint representationFingerprint;
    uint activeSampleCount;
    uint reserved0;
};

uint SigmaCarrierRepresentationAddress(uint pageSlot, uint sample,
    uint word)
{
    return (pageSlot * SIGMA_PAGE_SAMPLE_COUNT + sample) *
        SIGMA_REPRESENTATION_WORDS_PER_SAMPLE + word;
}

bool SigmaCarrierSameLogicalPage(SigmaCarrierPageMetaGpu left,
    SigmaCarrierPageMetaGpu right)
{
    return all(uint4(left.pageXLo, left.pageXHi, left.pageYLo, left.pageYHi) ==
        uint4(right.pageXLo, right.pageXHi, right.pageYLo, right.pageYHi));
}

// A physical pair is only backing. The single published revision root selects
// the newest complete generation without mutating either bank before publication.
bool SigmaCarrierVisibleAtRoot(SigmaCarrierPageMetaGpu page,
    SigmaCarrierPageMetaGpu sibling, bool hasSibling, uint publishedRevision)
{
    bool visible = false;
    if (publishedRevision == 0u ||
        (page.flags & SIGMA_PAGE_ALLOCATED) == 0u || page.revision == 0u ||
        page.revision > publishedRevision)
        visible = false;
    else if (!hasSibling || (sibling.flags & SIGMA_PAGE_ALLOCATED) == 0u ||
        sibling.revision == 0u || sibling.revision > publishedRevision ||
        !SigmaCarrierSameLogicalPage(page, sibling))
        visible = true;
    else
        visible = page.revision > sibling.revision ||
            (page.revision == sibling.revision &&
                page.generation >= sibling.generation);
    return visible;
}

#endif
