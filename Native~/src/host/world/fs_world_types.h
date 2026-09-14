// FinalScan world module: pure (no Vulkan) encode/decode helpers and page/cell coordinate math.
// Every formula here has a byte-identical twin in Native~/shaders/world/fs_world_common.glsl; the host
// tests (Native~/tests/host_tests_world.cpp) pin the roundtrip behaviour both sides rely on.
// Contract §8.1 (fixed point surfel), §9.1 (page key), §9.2 (page-local cells).
#pragma once
#include <stdint.h>
#include <math.h>
#include <string.h>
#include "../../../include/finalscan_world_abi.h"

namespace fs {
namespace world {

static_assert(sizeof(FsSurfel) == 32, "FsSurfel must be 32 B (contract §8.1)");
static_assert(sizeof(FsPageKey) == 16, "FsPageKey layout");
// NOTE: the header trailer comment says 64 B but the natural layout is 68 B (16 + 11*4 + 8). All three
// consumers (C++, GLSL via gen_abi.py, C#) use the compiler-true 68 B. Reported to the header owner.
static_assert(sizeof(FsPageHeader) == 68, "FsPageHeader layout (see note)");
static_assert(sizeof(FsPageHashEntry) == 32, "FsPageHashEntry layout");
static_assert(sizeof(FsSurfaceMeasurement) == 48, "FsSurfaceMeasurement layout");
static_assert(sizeof(FsDrawRecord) == 32, "FsDrawRecord layout");
static_assert(sizeof(FsIndirectDrawArgs) == 16, "FsIndirectDrawArgs layout");
static_assert(sizeof(FsSurfelEvidence) == 8, "FsSurfelEvidence layout");
static_assert(sizeof(FsClusterNode) == 80, "FsClusterNode layout");

// ---- fixed point position (q0.25 mm, page-local) -----------------------------------------------
inline int16_t EncodePos(float metres) {
    float q = roundf(metres / FS_SURFEL_POS_UNIT_M);
    if (q > 32767.f) q = 32767.f;
    if (q < -32768.f) q = -32768.f;
    return (int16_t)q;
}
inline float DecodePos(int16_t q) { return (float)q * FS_SURFEL_POS_UNIT_M; }

// ---- oct normal, 16+16 bits split into normalOct (high bytes) and normalOctHi (low bytes) --------
inline void EncodeNormalOct32(const float n[3], uint16_t& oct, uint16_t& octHi) {
    float ax = fabsf(n[0]), ay = fabsf(n[1]), az = fabsf(n[2]);
    float l1 = ax + ay + az; if (l1 < 1e-12f) l1 = 1e-12f;
    float x = n[0] / l1, y = n[1] / l1;
    if (n[2] < 0.f) {
        float ox = (1.f - fabsf(y)) * (x >= 0.f ? 1.f : -1.f);
        float oy = (1.f - fabsf(x)) * (y >= 0.f ? 1.f : -1.f);
        x = ox; y = oy;
    }
    uint32_t ux = (uint32_t)roundf((x * 0.5f + 0.5f) * 65535.f);
    uint32_t uy = (uint32_t)roundf((y * 0.5f + 0.5f) * 65535.f);
    if (ux > 65535u) ux = 65535u; if (uy > 65535u) uy = 65535u;
    oct   = (uint16_t)((ux >> 8) | ((uy >> 8) << 8));
    octHi = (uint16_t)((ux & 0xFFu) | ((uy & 0xFFu) << 8));
}
inline uint32_t PackOct32(uint16_t oct, uint16_t octHi) {          // FsDrawRecord.normalOct32: x16 | y16<<16
    uint32_t ux = ((oct & 0xFFu) << 8) | (octHi & 0xFFu);
    uint32_t uy = (oct & 0xFF00u) | (octHi >> 8);
    return ux | (uy << 16);
}
inline void DecodeOct32(uint32_t packed, float n[3]) {
    float x = (float)(packed & 0xFFFFu) / 65535.f * 2.f - 1.f;
    float y = (float)(packed >> 16) / 65535.f * 2.f - 1.f;
    float z = 1.f - fabsf(x) - fabsf(y);
    if (z < 0.f) {
        float ox = (1.f - fabsf(y)) * (x >= 0.f ? 1.f : -1.f);
        float oy = (1.f - fabsf(x)) * (y >= 0.f ? 1.f : -1.f);
        x = ox; y = oy;
    }
    float len = sqrtf(x * x + y * y + z * z); if (len < 1e-12f) len = 1.f;
    n[0] = x / len; n[1] = y / len; n[2] = z / len;
}
inline void DecodeNormalOct32(uint16_t oct, uint16_t octHi, float n[3]) { DecodeOct32(PackOct32(oct, octHi), n); }

// ---- log encodings: r = base * 2^(v/4096) --------------------------------------------------------
constexpr float kRadiusBaseM = 0.0005f;   // 0.5 mm .. ~32 m
constexpr float kSigmaBaseM  = 0.0001f;   // 0.1 mm .. ~6.5 m
inline uint16_t EncodeLog(float metres, float base) {
    if (metres <= base) return 0;
    float v = roundf(log2f(metres / base) * 4096.f);
    if (v > 65535.f) v = 65535.f;
    return (uint16_t)v;
}
inline float DecodeLog(uint16_t v, float base) { return base * exp2f((float)v / 4096.f); }
inline uint16_t EncodeLogRadius(float m) { return EncodeLog(m, kRadiusBaseM); }
inline float    DecodeLogRadius(uint16_t v) { return DecodeLog(v, kRadiusBaseM); }
inline uint16_t EncodeLogSigma(float m) { return EncodeLog(m, kSigmaBaseM); }
inline float    DecodeLogSigma(uint16_t v) { return DecodeLog(v, kSigmaBaseM); }
inline uint16_t EncodeTangentAngle(float rad) {
    const float twoPi = 6.283185307f;
    float t = fmodf(rad, twoPi); if (t < 0.f) t += twoPi;
    return (uint16_t)(t / twoPi * 65536.f);
}
inline float DecodeTangentAngle(uint16_t v) { return (float)v / 65536.f * 6.283185307f; }

// ---- evidence flags -----------------------------------------------------------------------------
constexpr uint16_t kEvidenceCountMask = 0x03FF;
constexpr uint16_t kFlagSidedA = 1u << 10, kFlagSidedB = 1u << 11, kFlagTransient = 1u << 12,
                   kFlagPromoted = 1u << 13, kFlagRemoved = 1u << 14, kFlagDetail = 1u << 15;

// ---- preview colour: RGB8 from normal in appearanceHandle low 24 bits (C05 preview, contract C04) --
inline uint32_t PreviewColorFromNormal(const float n[3]) {
    uint32_t r = (uint32_t)(fminf(fmaxf(n[0] * 0.5f + 0.5f, 0.f), 1.f) * 255.f);
    uint32_t g = (uint32_t)(fminf(fmaxf(n[1] * 0.5f + 0.5f, 0.f), 1.f) * 255.f);
    uint32_t b = (uint32_t)(fminf(fmaxf(n[2] * 0.5f + 0.5f, 0.f), 1.f) * 255.f);
    return r | (g << 8) | (b << 16);
}

// ---- page / cell coordinates --------------------------------------------------------------------
// Logical page coord c = floor(p / E); the page cube covers [c*E, (c+1)*E) and its origin (the point
// page-local fixed point is relative to) is the cube centre (c + 0.5) * E. Page-local ∈ [-E/2, E/2).
inline int32_t PageCoord(float metres) { return (int32_t)floorf(metres / FS_PAGE_EXTENT_M); }
inline float   PageOrigin(int32_t coord) { return ((float)coord + 0.5f) * FS_PAGE_EXTENT_M; }
inline FsPageKey MakePageKey(int32_t anchorId, const float p[3]) {
    FsPageKey k; k.anchorId = anchorId; k.x = PageCoord(p[0]); k.y = PageCoord(p[1]); k.z = PageCoord(p[2]);
    return k;
}
inline bool KeyEq(const FsPageKey& a, const FsPageKey& b) {
    return a.anchorId == b.anchorId && a.x == b.x && a.y == b.y && a.z == b.z;
}
inline uint32_t HashPageKey(const FsPageKey& k) {                  // twin: fsHashPageKey in GLSL
    uint32_t h = (uint32_t)k.anchorId * 0x9E3779B1u;
    h ^= (uint32_t)k.x * 0x85EBCA77u; h = (h << 13) | (h >> 19);
    h ^= (uint32_t)k.y * 0xC2B2AE3Du; h = (h << 13) | (h >> 19);
    h ^= (uint32_t)k.z * 0x27D4EB2Fu;
    h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
    return h;
}
// Cell index of a page-local position (metres, [-E/2, E/2)); clamped so out-of-range never escapes.
inline uint32_t CellOf(float lx, float ly, float lz) {
    const float half = FS_PAGE_EXTENT_M * 0.5f;
    int cx = (int)floorf((lx + half) / FS_CELL_EXTENT_M);
    int cy = (int)floorf((ly + half) / FS_CELL_EXTENT_M);
    int cz = (int)floorf((lz + half) / FS_CELL_EXTENT_M);
    auto clampc = [](int c) { return c < 0 ? 0 : (c >= FS_CELLS_PER_AXIS ? FS_CELLS_PER_AXIS - 1 : c); };
    return (uint32_t)clampc(cx) + (uint32_t)clampc(cy) * FS_CELLS_PER_AXIS +
           (uint32_t)clampc(cz) * FS_CELLS_PER_AXIS * FS_CELLS_PER_AXIS;
}

// ---- surfel construction from a measurement-like sample -----------------------------------------
struct SurfelSample { float p[3]; float n[3]; float radius; float sigmaN, sigmaT; uint32_t surfaceId; };
inline FsSurfel MakeSurfel(const SurfelSample& s, const FsPageKey& page) {
    FsSurfel r; memset(&r, 0, sizeof r);
    r.px = EncodePos(s.p[0] - PageOrigin(page.x));
    r.py = EncodePos(s.p[1] - PageOrigin(page.y));
    r.pz = EncodePos(s.p[2] - PageOrigin(page.z));
    EncodeNormalOct32(s.n, r.normalOct, r.normalOctHi);
    r.tangentAngle = 0;
    r.radiusMajor = r.radiusMinor = EncodeLogRadius(s.radius);
    r.sigmaN = EncodeLogSigma(s.sigmaN);
    r.sigmaTMajor = r.sigmaTMinor = EncodeLogSigma(s.sigmaT);
    r.evidenceFlags = 1 | kFlagPromoted;
    r.surfaceId = s.surfaceId;
    r.appearanceHandle = PreviewColorFromNormal(s.n);
    return r;
}

} // namespace world
} // namespace fs
