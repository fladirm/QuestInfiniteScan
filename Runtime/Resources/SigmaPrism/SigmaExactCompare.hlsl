#ifndef SIGMA_EXACT_COMPARE_INCLUDED
#define SIGMA_EXACT_COMPARE_INCLUDED

// Shared comparison-only lowering for signed Q16.48 values stored as two
// little-endian uint limbs.  Keep this dependency small: metadata/proof kernels
// need exact ordering but must not compile the complete S16 arithmetic library.
bool SigmaU64Equal(uint2 a, uint2 b)
{
    // Explicit scalar comparison avoids legacy HLSL vector-bool lowering
    // differences across Vulkan compiler paths.
    return a.x == b.x && a.y == b.y;
}

bool SigmaU64Less(uint2 a, uint2 b)
{
    return a.y < b.y || (a.y == b.y && a.x < b.x);
}

bool SigmaI64Less(uint2 a, uint2 b)
{
    int ah = asint(a.y);
    int bh = asint(b.y);
    return ah < bh || (ah == bh && a.x < b.x);
}

bool SigmaQ48Less(uint2 a, uint2 b)
{
    return SigmaI64Less(a, b);
}

uint2 SigmaQ48Min(uint2 a, uint2 b)
{
    return SigmaI64Less(a, b) ? a : b;
}

uint2 SigmaQ48Max(uint2 a, uint2 b)
{
    return SigmaI64Less(a, b) ? b : a;
}

#endif
