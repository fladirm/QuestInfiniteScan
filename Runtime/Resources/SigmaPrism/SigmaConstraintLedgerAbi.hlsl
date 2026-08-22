#ifndef SIGMA_CONSTRAINT_LEDGER_ABI_INCLUDED
#define SIGMA_CONSTRAINT_LEDGER_ABI_INCLUDED

#include "SigmaInverseAbi.hlsl"

// Proof storage is sparse in constrained coordinates, not in carrier samples.
// Four exact block certificates cover the four simultaneous rig sources.  Any
// further non-redundant temporal constraint remains losslessly represented by a
// retained raw observation tile until it can be replayed and reduced.
#define SIGMA_CERTIFICATES_PER_BLOCK 4u
#define SIGMA_CERTIFICATE_BOUNDS_PER_BLOCK 48u
#define SIGMA_CERTIFICATE_BLOCKS_PER_PAGE 64u
#define SIGMA_CERTIFICATES_PER_PAGE 256u
#define SIGMA_CERTIFICATE_BOUNDS_PER_PAGE 3072u
#define SIGMA_PROOF_STATUS_STRIDE 8u
#define SIGMA_RAW_WORD4_PER_SAMPLE 6u
#define SIGMA_RAW_WORD4_PER_TILE 384u
#define SIGMA_RAW_FRAME_WORD4_COUNT 37u
#define SIGMA_INVALID_PROOF_SLOT 0xffffffffu
#define SIGMA_INVALID_RAW_TILE 0xffffffffu
#define SIGMA_CERTIFICATE_ACTIVE (1u << 0u)
#define SIGMA_CERTIFICATE_UNRESOLVED (1u << 1u)

#define SIGMA_PROOF_STATE_CHANGED (1u << 0u)
#define SIGMA_PROOF_SET_CHANGED (1u << 1u)
#define SIGMA_PROOF_RAW_CHANGED (1u << 2u)
#define SIGMA_PROOF_FAILED (1u << 31u)

#define SIGMA_RAW_REASON_CONFLICT (1u << 0u)
#define SIGMA_RAW_REASON_EXCLUSION (1u << 1u)
#define SIGMA_RAW_REASON_UNOBSERVABLE (1u << 2u)
#define SIGMA_RAW_REASON_CERTIFICATE_OVERFLOW (1u << 3u)
#define SIGMA_RAW_REASON_NONUNIFORM_CELL (1u << 4u)

#define SIGMA_ROLE_SUPPORT (1u << 0u)
#define SIGMA_ROLE_GEOMETRY (1u << 1u)
#define SIGMA_ROLE_APPEARANCE (1u << 2u)
#define SIGMA_ROLE_EXCLUSION (1u << 3u)

// 48 bytes. Bounds are stored sparsely in the page's block-local bound arena.
struct SigmaConstraintCertificateGpu
{
    uint4 identity; // coordinateMask, sourceClass, independenceKey, epoch
    uint4 range;    // roleMask, boundOffset, boundCount, flags
    uint2 sampleMask;
    uint2 reserved;
};

// 32 bytes. rawHead is an immutable observation-chain index, never geometry.
struct SigmaConstraintBlockGpu
{
    uint4 counts; // certificateCount, boundCount, rawHead, flags
    uint4 proof;  // meetMask, roleMask, independentCoordinateMask, revision
};

// 48 bytes.  This is inverse-proof scheduling metadata, never a detail field.
// It records the strongest exact failure of the current piecewise-projective
// readout to reproduce two independently accepted source cells in one 8x8
// carrier block.
struct SigmaGaugeDemandGpu
{
    uint4 trigger;  // valid, operator coordinate, local centre, axis
    uint4 evidence; // independence key 0/1, source mask, proof revision
    uint4 metric;   // reproduction error Q48, joint width Q48
};

// 32 bytes. Raw payload is a fixed 64-sample finite-footprint tile in a separate
// flat uint4 arena; frameSlot resolves immutable pose/calibration metadata.
struct SigmaRawObservationTileGpu
{
    uint4 identity;   // next, frameSlot, pageBlock, revision
    uint4 provenance; // reasons, epoch, sampleMaskLo, sampleMaskHi
};

// One immutable retained-frame record. The first 33 uint4 words are the exact
// capture metadata staged by C# (poses, intrinsics, timestamps and pairing
// health). The final four words are the accepted same-frame pose-gauge result,
// appended by the GPU only when unresolved evidence actually retains a raw tile.
// Ordinary submitted frames therefore consume no durable provenance slot.
struct SigmaRawFrameRecordGpu
{
    uint4 word[SIGMA_RAW_FRAME_WORD4_COUNT];
};

// Exact reducer output consumed by the stable raw-reservation pass. It is
// transaction scratch, not durable evidence and not reconstruction state.
struct SigmaRawRetentionRequestGpu
{
    uint4 value; // reasons, sampleMaskLo, sampleMaskHi, previous raw head
};

// One page scratch record is rewritten before each proof transaction. It is not
// durable state. The exact source cells are retained independently until the
// ledger reducer has formed their common proof.
struct SigmaProofSampleGpu
{
    uint4 meta; // proposal status, joint mask, RGB phase member, valid
    SigmaDepthCell3 depthLeft;
    SigmaDepthCell3 depthRight;
    SigmaAdmissibleCell16 rgbLeft;
    SigmaAdmissibleCell16 rgbRight;
    uint4 raw[SIGMA_RAW_WORD4_PER_SAMPLE];
};

uint SigmaProofStatusAddress(uint proofSlot, uint word)
{
    return proofSlot * SIGMA_PROOF_STATUS_STRIDE + word;
}

uint SigmaCertificateAddress(uint proofSlot, uint block, uint certificate)
{
    return proofSlot * SIGMA_CERTIFICATES_PER_PAGE +
        block * SIGMA_CERTIFICATES_PER_BLOCK + certificate;
}

uint SigmaCertificateBoundAddress(uint proofSlot, uint block, uint bound)
{
    return proofSlot * SIGMA_CERTIFICATE_BOUNDS_PER_PAGE +
        block * SIGMA_CERTIFICATE_BOUNDS_PER_BLOCK + bound;
}

uint SigmaConstraintBlockAddress(uint proofSlot, uint block)
{
    return proofSlot * SIGMA_CERTIFICATE_BLOCKS_PER_PAGE + block;
}

uint SigmaProofBlockForPageSample(uint sample)
{
    uint x = sample & 63u;
    uint y = sample >> 6u;
    return (y >> 3u) * 8u + (x >> 3u);
}

uint SigmaProofLocalForPageSample(uint sample)
{
    uint x = sample & 63u;
    uint y = sample >> 6u;
    return (y & 7u) * 8u + (x & 7u);
}

uint SigmaProofPageSample(uint block, uint local)
{
    uint blockX = block & 7u;
    uint blockY = block >> 3u;
    uint localX = local & 7u;
    uint localY = local >> 3u;
    return (blockY * 8u + localY) * 64u + blockX * 8u + localX;
}

#endif
