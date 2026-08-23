#ifndef SIGMA_MANIFEST_VISIBILITY_INCLUDED
#define SIGMA_MANIFEST_VISIBILITY_INCLUDED

#include "SigmaStreamingAbi.hlsl"

// Publication visibility is one exact generation-safe indirection. Page flags
// may cache this result for compaction but never authorize a canonical reader.
bool SigmaManifestHandleValid(uint2 handle, uint capacity)
{
    return handle.x < capacity && handle.y != 0u;
}

bool SigmaManifestHandlePublished(uint2 handle,
    SigmaPublicationManifestGpu manifest)
{
    return manifest.identity.x == SIGMA_STREAM_MANIFEST_PUBLISHED &&
        manifest.identity.y == handle.y;
}

bool SigmaManifestPageVisible(SigmaPageVisibilityGpu visibility,
    SigmaPublicationManifestGpu born,
    SigmaPublicationManifestGpu retired)
{
    uint2 bornHandle = visibility.bornRetired.xy;
    uint2 retiredHandle = visibility.bornRetired.zw;
    if (!SigmaManifestHandlePublished(bornHandle, born))
        return false;
    return retiredHandle.x == SIGMA_STREAM_INVALID ||
        !SigmaManifestHandlePublished(retiredHandle, retired);
}

#endif
