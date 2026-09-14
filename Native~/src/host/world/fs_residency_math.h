// Residency zone math (contract §12): pure functions, no state. The readout sphere is centred on the
// predicted head POSITION only. Orientation is not an input anywhere in this file or in the residency
// API (FsResidency_SetCenter has no rotation parameter by design, §12.1/§12.4); the host tests prove that
// the same position with any rotation yields an identical page set.
#pragma once
#include <stdint.h>
#include <math.h>
#include <vector>
#include "fs_world_types.h"

namespace fs {
namespace world {

enum ZoneId : uint8_t { ZONE_INNER = 0, ZONE_WARM = 1, ZONE_PREFETCH = 2, ZONE_OUTSIDE = 3 };

struct ZonePage { FsPageKey key; uint8_t zone; float dist2; };   // dist2: squared distance sphere centre → cube

// Squared distance from a point to the page cube [c*E, (c+1)*E)^3.
inline float PageCubeDist2(const float p[3], int32_t cx, int32_t cy, int32_t cz) {
    const float E = FS_PAGE_EXTENT_M;
    float d2 = 0.f;
    const int32_t c[3] = {cx, cy, cz};
    for (int a = 0; a < 3; ++a) {
        float lo = (float)c[a] * E, hi = lo + E;
        float d = p[a] < lo ? lo - p[a] : (p[a] > hi ? p[a] - hi : 0.f);
        d2 += d * d;
    }
    return d2;
}

// Enumerates every page whose cube intersects the prefetch sphere and classifies it by the innermost
// sphere it intersects. Output is sorted innermost-first then by distance (deterministic order) so a
// slot-limited allocator serves INNER before WARM before PREFETCH. Bounded by the prefetch radius.
inline void ComputeZonePages(int32_t anchorId, const float centre[3], float innerR, float warmR, float prefetchR,
                             std::vector<ZonePage>& out) {
    out.clear();
    if (prefetchR < warmR) prefetchR = warmR;
    if (warmR < innerR) warmR = innerR;
    const float E = FS_PAGE_EXTENT_M;
    int32_t lo[3], hi[3];
    for (int a = 0; a < 3; ++a) { lo[a] = PageCoord(centre[a] - prefetchR); hi[a] = PageCoord(centre[a] + prefetchR); }
    (void)E;
    float i2 = innerR * innerR, w2 = warmR * warmR, p2 = prefetchR * prefetchR;
    for (int32_t z = lo[2]; z <= hi[2]; ++z)
        for (int32_t y = lo[1]; y <= hi[1]; ++y)
            for (int32_t x = lo[0]; x <= hi[0]; ++x) {
                float d2 = PageCubeDist2(centre, x, y, z);
                if (d2 > p2) continue;
                ZonePage zp; zp.key.anchorId = anchorId; zp.key.x = x; zp.key.y = y; zp.key.z = z; zp.dist2 = d2;
                zp.zone = d2 <= i2 ? ZONE_INNER : (d2 <= w2 ? ZONE_WARM : ZONE_PREFETCH);
                out.push_back(zp);
            }
    // insertion sort by (zone, dist2): page counts are small (hundreds) and this keeps the header pure
    for (size_t i = 1; i < out.size(); ++i) {
        ZonePage v = out[i]; size_t j = i;
        while (j > 0 && (out[j - 1].zone > v.zone || (out[j - 1].zone == v.zone && out[j - 1].dist2 > v.dist2))) { out[j] = out[j - 1]; --j; }
        out[j] = v;
    }
}

// Predicted centre = p + v * horizon, horizon bounded (§12.2).
inline void PredictCentre(const float p[3], const float v[3], float horizonS, float maxShiftM, float out[3]) {
    float dx = v[0] * horizonS, dy = v[1] * horizonS, dz = v[2] * horizonS;
    float l = sqrtf(dx * dx + dy * dy + dz * dz);
    if (l > maxShiftM && l > 0.f) { float s = maxShiftM / l; dx *= s; dy *= s; dz *= s; }
    out[0] = p[0] + dx; out[1] = p[1] + dy; out[2] = p[2] + dz;
}

} // namespace world
} // namespace fs
