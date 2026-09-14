// FinalScan world module: pure (no Vulkan) encode/decode helpers and page/cell coordinate math.
// Every formula here has a byte-identical twin in Native~/shaders/world/fs_world_common.glsl; the host
// tests (Native~/tests/host_tests_world.cpp) pin the roundtrip behaviour both sides rely on.
// Contract §8.1 (fixed point surfel), §9.1 (page key), §9.2 (page-local cells).
#pragma once
#include <stdint.h>
#include <math.h>
#include <string.h>
#include "../../../include/finalscan_world_abi.h"
#include "fs_world_params.h"

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
    if (ux > 65535u) ux = 65535u;
    if (uy > 65535u) uy = 65535u;
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

// ---- Morton key over cell coordinates (15 bits; twin: fsMorton in GLSL) --------------------------
inline uint32_t Part1By2(uint32_t v) {                    // 5 bits -> every third bit
    v &= 0x1Fu; v = (v | (v << 8)) & 0x100F00Fu; v = (v | (v << 4)) & 0x10C30C3u; v = (v | (v << 2)) & 0x1249249u;
    return v;
}
inline uint32_t MortonOfCell(uint32_t cell) {
    uint32_t cx = cell & 31u, cy = (cell >> 5) & 31u, cz = (cell >> 10) & 31u;
    return Part1By2(cx) | (Part1By2(cy) << 1) | (Part1By2(cz) << 2);
}
inline uint32_t CellOfMorton(uint32_t m) {                // inverse (twin: fsCellOfMorton)
    uint32_t cx = 0, cy = 0, cz = 0;
    for (uint32_t b = 0; b < 5; ++b) { cx |= ((m >> (3 * b)) & 1u) << b; cy |= ((m >> (3 * b + 1)) & 1u) << b; cz |= ((m >> (3 * b + 2)) & 1u) << b; }
    return cx | (cy << 5) | (cz << 10);
}
inline uint32_t CellOfSurfel(const FsSurfel& s) { return CellOf(DecodePos(s.px), DecodePos(s.py), DecodePos(s.pz)); }
// Octant inside a cell (micro-bucket, 2x2x2 -> 6.25 cm), twin: fsOctant.
inline uint32_t OctantOf(float lx, float ly, float lz) {
    const float half = FS_PAGE_EXTENT_M * 0.5f, e = FS_CELL_EXTENT_M;
    float fx = (lx + half) / e, fy = (ly + half) / e, fz = (lz + half) / e;
    uint32_t ox = (fx - floorf(fx)) >= 0.5f ? 1u : 0u, oy = (fy - floorf(fy)) >= 0.5f ? 1u : 0u, oz = (fz - floorf(fz)) >= 0.5f ? 1u : 0u;
    return ox | (oy << 1) | (oz << 2);
}

// ---- canonical tangent frame of a normal (twin: fsTangentFrame; the Unity surfel shader must build the same)
inline void TangentFrame(const float n[3], float t1[3], float t2[3]) {
    float up[3] = {0.f, 1.f, 0.f}; if (fabsf(n[1]) > 0.99f) { up[0] = 1.f; up[1] = 0.f; }
    t1[0] = n[1] * up[2] - n[2] * up[1]; t1[1] = n[2] * up[0] - n[0] * up[2]; t1[2] = n[0] * up[1] - n[1] * up[0];
    float l = sqrtf(t1[0] * t1[0] + t1[1] * t1[1] + t1[2] * t1[2]); if (l < 1e-9f) { t1[0] = 1.f; t1[1] = 0.f; t1[2] = 0.f; l = 1.f; }
    t1[0] /= l; t1[1] /= l; t1[2] /= l;
    t2[0] = n[1] * t1[2] - n[2] * t1[1]; t2[1] = n[2] * t1[0] - n[0] * t1[2]; t2[2] = n[0] * t1[1] - n[1] * t1[0];
}
// Footprint ellipse of an AABB seen along `axis`: semi-axes (a, b) along the canonical tangent frame and the
// 2D centre; the aggregate is drawn as this ellipse (radiusMajor = a, radiusMinor = b, tangentAngle = 0).
inline void FootprintOfAabb(const float bmin[3], const float bmax[3], const float t1[3], const float t2[3], float& a, float& b, float& cx, float& cy) {
    float x0 = 1e30f, x1 = -1e30f, y0 = 1e30f, y1 = -1e30f;
    for (int i = 0; i < 8; ++i) {
        float c[3] = {(i & 1) ? bmax[0] : bmin[0], (i & 2) ? bmax[1] : bmin[1], (i & 4) ? bmax[2] : bmin[2]};
        float x = c[0] * t1[0] + c[1] * t1[1] + c[2] * t1[2], y = c[0] * t2[0] + c[1] * t2[1] + c[2] * t2[2];
        x0 = fminf(x0, x); x1 = fmaxf(x1, x); y0 = fminf(y0, y); y1 = fmaxf(y1, y);
    }
    a = 0.5f * (x1 - x0); b = 0.5f * (y1 - y0); cx = 0.5f * (x0 + x1); cy = 0.5f * (y0 + y1);
}
inline uint32_t PackFootprint(float a, float b) { return (uint32_t)EncodeLogRadius(a) | ((uint32_t)EncodeLogRadius(b) << 16); }
inline float FootprintA(uint32_t packed) { return DecodeLogRadius((uint16_t)(packed & 0xFFFFu)); }
inline float FootprintB(uint32_t packed) { return DecodeLogRadius((uint16_t)(packed >> 16)); }

// ---- draw record radius byte: r = 0.5 mm * 2^(v/16) (0.5 mm .. 32 m); twin: fsEncodeRadius8 ------
inline uint32_t EncodeRadius8(float metres) {
    if (metres <= kRadiusBaseM) return 0;
    float v = roundf(log2f(metres / kRadiusBaseM) * 16.f);
    return v > 255.f ? 255u : (uint32_t)v;
}
inline float DecodeRadius8(uint32_t v) { return kRadiusBaseM * exp2f((float)v / 16.f); }
inline uint32_t Radius8FromLog16(uint16_t v) { uint32_t r = ((uint32_t)v + 128u) >> 8; return r > 255u ? 255u : r; }   // 4096/16 = 256

// ---- root word (fs_world_params.h): the one u32 the cull reads per slot -----------------------------
struct RootWord {
    uint32_t frontCount = 0; bool published = false; uint32_t parity = 0; bool treeValid = false; uint32_t leafShift = 0; uint32_t generation = 0;
};
inline uint32_t PackRoot(const RootWord& r) {
    uint32_t c = r.frontCount > FS_MAX_FRONT_COUNT ? FS_MAX_FRONT_COUNT : r.frontCount;
    return c | (r.published ? FS_ROOT_PUBLISHED : 0u) | (r.parity ? FS_ROOT_PARITY : 0u) | (r.treeValid ? FS_ROOT_TREE_VALID : 0u) |
           ((r.leafShift & 7u) << FS_ROOT_LEAF_SHIFT) | ((r.generation & 0xFFu) << FS_ROOT_GEN_SHIFT);
}
inline RootWord UnpackRoot(uint32_t w) {
    RootWord r; r.frontCount = w & FS_ROOT_COUNT_MASK; r.published = (w & FS_ROOT_PUBLISHED) != 0; r.parity = (w & FS_ROOT_PARITY) ? 1u : 0u;
    r.treeValid = (w & FS_ROOT_TREE_VALID) != 0; r.leafShift = (w & FS_ROOT_LEAF_MASK) >> FS_ROOT_LEAF_SHIFT; r.generation = w >> FS_ROOT_GEN_SHIFT;
    return r;
}

// ---- static per-slot layout (device table; twin: FsSlotLayout in fs_world_common.glsl) ---------------
struct FsSlotLayout {
    uint32_t surfelBase;      // first surfel of the slot: BACK = [base, base+cap), FRONT p = base + cap*(1+p)
    uint32_t capacity;        // surfels per range
    uint32_t linkBase;        // link/count index of BACK surfel i = linkBase + (i - surfelBase)
    uint32_t cellBase;        // cell head index = cellBase + cell
    uint32_t microBase;       // micro head index = microBase + block*8 + octant
    uint32_t nodeBase;        // cluster node index of parity p = nodeBase + p*nodesPerSlot
    uint32_t nodesPerSlot;
    uint32_t freeSpaceBase;   // u8 offset (stub)
};
static_assert(sizeof(FsSlotLayout) == 32, "FsSlotLayout layout");
inline uint32_t FrontOffsetOf(const FsSlotLayout& l, uint32_t parity) { return l.surfelBase + l.capacity * (1u + parity); }

// ---- cluster tree layout (§13.4): root at node 0, then levels top-down; leaves last. Twin: fsClusterLayout.
struct ClusterLayout {
    uint32_t levels = 0;                          // 0 when n == 0
    uint32_t count[FS_CLUSTER_MAX_LEVELS] = {};   // count[0] = leaves ... count[levels-1] = 1 (root)
    uint32_t offset[FS_CLUSTER_MAX_LEVELS] = {};  // node index of the first node of each level
    uint32_t total = 0;
    uint32_t leafSize = 64;
};
inline ClusterLayout ComputeClusterLayout(uint32_t n, uint32_t leafShift) {
    ClusterLayout L; L.leafSize = 64u << leafShift;
    if (n == 0) return L;
    uint32_t c = (n + L.leafSize - 1) / L.leafSize, lv = 0;
    for (;;) {
        L.count[lv++] = c;
        if (c == 1 || lv >= FS_CLUSTER_MAX_LEVELS) break;
        c = (c + FS_CLUSTER_FANOUT - 1) / FS_CLUSTER_FANOUT;
    }
    L.levels = lv;
    uint32_t off = 0;
    for (int i = (int)lv - 1; i >= 0; --i) { L.offset[i] = off; off += L.count[i]; }
    L.total = off;
    return L;
}
// Smallest leaf shift whose node total fits nodeCapacity (coarser leaves on overflow, counted by caller).
inline uint32_t ChooseLeafShift(uint32_t n, uint32_t nodeCapacity) {
    for (uint32_t s = 0; s <= FS_CLUSTER_MAX_LEAF_SHIFT; ++s) if (ComputeClusterLayout(n, s).total <= nodeCapacity) return s;
    return FS_CLUSTER_MAX_LEAF_SHIFT;
}
inline uint32_t NodesPerSlotFor(uint32_t capacity) {
    uint32_t t = ComputeClusterLayout(capacity, 0).total;
    return t > FS_CLUSTER_MAX_NODES_PER_PAGE ? FS_CLUSTER_MAX_NODES_PER_PAGE : (t < 8u ? 8u : t);
}

} // namespace world
} // namespace fs
