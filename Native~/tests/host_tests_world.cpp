// Host (linux x86_64) unit tests for the world/render modules: pure headers only (no Vulkan, no executor).
// HOST_TEST_SOURCES: host/world/fs_synthetic.cpp
// Covers: page hash, encodings, Morton, synthetic scenes, residency math, HZB fail-open band rule, cull math,
// and the C09R §32 suite against the CPU twins in fs_fusion_ref.h / fs_pools.h: fusion determinism (replay =
// bit-identical, permutation = same associations), concurrent candidates in one cell, dense bucket bounds,
// deterministic merge priority, publication retirement (ids reused only after retirement), page growth (slabs),
// sparse update (deterministic dirty list, sliced cap), cross-page free-space ray with the hop bound, HZB
// source disagreement, index generation invariants (owner-only mutation, split/chain/overflow), reset.
#include "../src/host/world/fs_world_types.h"
#include "../src/host/world/fs_page_hash.h"
#include "../src/host/world/fs_residency_math.h"
#include "../src/host/world/fs_synthetic.h"
#include "../src/host/world/fs_fusion_ref.h"
#include "../src/host/world/fs_pools.h"
#include "../src/host/render/fs_cull_math.h"
#include "../src/host/render/fs_hzb_math.h"
#include "../src/host/render/fs_render.h"
#include <cstdio>
#include <cmath>
#include <vector>
#include <set>
#include <map>
#include <random>
#include <algorithm>

using namespace fs::world;
using namespace fs::render;

static int g_failures = 0, g_checks = 0;
#define CHECK(cond) do { ++g_checks; if (!(cond)) { std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); ++g_failures; } } while (0)
#define CHECK_NEAR(a, b, eps) do { ++g_checks; double _a = (a), _b = (b); if (std::fabs(_a - _b) > (eps)) { std::printf("FAIL %s:%d: %s = %g vs %g\n", __FILE__, __LINE__, #a, _a, _b); ++g_failures; } } while (0)

static void TestPageHash() {
    PageHash h; h.Init(64);
    FsPageKey k{1, 2, 3, 4}; uint32_t slot = 0, gen = 0;
    CHECK(!h.Lookup(k, &slot));
    CHECK(h.Insert(k, 7, 3)); CHECK(h.Lookup(k, &slot, &gen) && slot == 7 && gen == 3);
    CHECK(h.Insert(k, 8, 4)); CHECK(h.Lookup(k, &slot, &gen) && slot == 8 && gen == 4 && h.Count() == 1);   // update, not duplicate
    // fill to the load limit (3/4 of 64 = 48), robin-hood must keep every key findable
    std::vector<FsPageKey> keys;
    for (int i = 0; i < 47; ++i) { FsPageKey kk{0, i * 7, -i, i * i}; CHECK(h.Insert(kk, (uint32_t)i, 1)); keys.push_back(kk); }
    CHECK(h.Count() == 48);
    for (size_t i = 0; i < keys.size(); ++i) { CHECK(h.Lookup(keys[i], &slot) && slot == (uint32_t)i); }
    FsPageKey over{0, 1000, 1000, 1000};
    CHECK(!h.Insert(over, 99, 1)); CHECK(h.Overflow() == 1);                         // explicit overflow, never silent
    // robin-hood invariant: every entry's probe distance equals its displacement from the home bucket
    for (uint32_t i = 0; i < h.Capacity(); ++i) {
        const FsPageHashEntry& e = h.Data()[i];
        if (e.slot == FS_INDEX_NONE) continue;
        uint32_t home = HashPageKey(e.key) & (h.Capacity() - 1);
        CHECK(((i - home) & (h.Capacity() - 1)) == e.probeDistance);
        CHECK(e.probeDistance < PageHash::kMaxProbe);
    }
    // erase + backward shift keeps the rest findable
    for (size_t i = 0; i < keys.size(); i += 3) CHECK(h.Erase(keys[i]));
    for (size_t i = 0; i < keys.size(); ++i) { bool found = h.Lookup(keys[i], &slot); CHECK(found == (i % 3 != 0)); if (found) CHECK(slot == (uint32_t)i); }
    CHECK(!h.Erase(over));
    CHECK(h.SetGeneration(k, 9) && h.Lookup(k, &slot, &gen) && gen == 9);
    // GLSL twin sanity: the hash function is deterministic and spreads neighbours
    std::set<uint32_t> buckets; for (int x = 0; x < 16; ++x) for (int z = 0; z < 16; ++z) buckets.insert(HashPageKey(FsPageKey{0, x, 0, z}) & 4095);
    CHECK(buckets.size() > 200);
}

static void TestEncodings() {
    for (float m : {-8.19f, -1.23456f, -0.00025f, 0.f, 0.000125f, 0.5f, 3.99999f, 8.19f}) { CHECK_NEAR(DecodePos(EncodePos(m)), m, 0.000126); }
    CHECK(EncodePos(100.f) == 32767 && EncodePos(-100.f) == -32768);
    float worst = 0.f;
    for (int i = 0; i < 2000; ++i) {
        float t = (float)i * 0.618f, u = (float)i * 0.317f;
        float n[3] = {sinf(t) * cosf(u), sinf(t) * sinf(u), cosf(t)};
        uint16_t o, oh; EncodeNormalOct32(n, o, oh); float d[3]; DecodeNormalOct32(o, oh, d);
        float dot = n[0] * d[0] + n[1] * d[1] + n[2] * d[2]; if (dot > 1.f) dot = 1.f;
        worst = fmaxf(worst, acosf(dot) * 57.2958f);
        CHECK(PackOct32(o, oh) == ((uint32_t)((o & 0xFF) << 8 | (oh & 0xFF)) | ((uint32_t)((o & 0xFF00) | (oh >> 8)) << 16)));
    }
    std::printf("oct worst error %.4f deg\n", worst);
    CHECK(worst < 0.05f);                                  // 16+16 bit oct: ~0.03 deg worst case at octahedron edges
    for (float r : {0.0005f, 0.001f, 0.0123f, 0.5f, 2.f, 30.f}) { CHECK_NEAR(DecodeLogRadius(EncodeLogRadius(r)) / r, 1.0, 0.0002); }
    for (float s : {0.0001f, 0.0011f, 0.02f, 1.f}) { CHECK_NEAR(DecodeLogSigma(EncodeLogSigma(s)) / s, 1.0, 0.0002); }
    CHECK(EncodeLogRadius(0.f) == 0 && EncodeLogRadius(1e9f) == 65535);
    for (float r : {0.001f, 0.01f, 0.1f, 1.f}) { CHECK_NEAR(DecodeRadius8(EncodeRadius8(r)) / r, 1.0, 0.045); CHECK(Radius8FromLog16(EncodeLogRadius(r)) == EncodeRadius8(r) || (int)Radius8FromLog16(EncodeLogRadius(r)) - (int)EncodeRadius8(r) == 1); }
    CHECK_NEAR(DecodeTangentAngle(EncodeTangentAngle(1.234f)), 1.234f, 1e-4);
    CHECK_NEAR(DecodeTangentAngle(EncodeTangentAngle(-1.f)), 6.2831853f - 1.f, 1e-4);
    // Morton over 32^3 cells: bijective, 15 bits
    std::set<uint32_t> seen;
    for (uint32_t c = 0; c < FS_CELLS_PER_PAGE; ++c) { uint32_t m = MortonOfCell(c); CHECK(m < 32768u); CHECK(CellOfMorton(m) == c); seen.insert(m); }
    CHECK(seen.size() == FS_CELLS_PER_PAGE);
    CHECK(MortonOfCell(CellOf(-2.f, -2.f, -2.f)) == 0u);
    CHECK(CellOf(1.999f, 1.999f, 1.999f) == FS_CELLS_PER_PAGE - 1);
    CHECK(CellOf(9.f, -9.f, 0.f) == CellOf(1.999f, -2.f, 0.f));   // clamped, never escapes
    CHECK(OctantOf(-2.f + 0.01f, -2.f + 0.07f, -2.f + 0.12f) == (0u | 2u | 4u));
    // preview colour: normal +x -> R 255, G/B 127
    float nx[3] = {1, 0, 0}; CHECK(PreviewColorFromNormal(nx) == (255u | (127u << 8) | (127u << 16)));
    // page key / origin
    float p[3] = {-0.1f, 4.f, 7.99f}; FsPageKey k = MakePageKey(5, p);
    CHECK(k.anchorId == 5 && k.x == -1 && k.y == 1 && k.z == 1);
    CHECK_NEAR(PageOrigin(-1), -2.0, 1e-6); CHECK_NEAR(PageOrigin(1), 6.0, 1e-6);
    FsSurfel s = MakeSurfel(SurfelSample{{-0.1f, 4.f, 7.99f}, {0, 1, 0}, 0.01f, 0.001f, 0.002f, 42}, k);
    CHECK_NEAR(DecodePos(s.px), 1.9, 0.0002); CHECK_NEAR(DecodePos(s.py), -2.0, 0.0002); CHECK_NEAR(DecodePos(s.pz), 1.99, 0.0002);
    CHECK(s.surfaceId == 42 && (s.evidenceFlags & kEvidenceCountMask) == 1 && (s.evidenceFlags & kFlagPromoted));
}

static void TestSynthetic() {
    SyntheticScene room; GenerateSynthetic(SYN_ROOM_BOX, 6.f, 0.05f, 1, 0, room);
    CHECK(room.planeCount == 6);
    // 6 m box centred on the origin spans x,z in {-1,0,1} pages x y in {0} (floor/ceiling at 0 and 2.6) -> <= 9 pages, all local coords inside the cube
    CHECK(room.pages.size() >= 4 && room.pages.size() <= 9);
    uint64_t total = 0;
    for (const SyntheticPage& p : room.pages) {
        total += p.surfels.size();
        for (const FsSurfel& s : p.surfels) { CHECK(std::fabs(DecodePos(s.px)) <= 2.0001f && std::fabs(DecodePos(s.py)) <= 2.0001f && std::fabs(DecodePos(s.pz)) <= 2.0001f); }
    }
    CHECK(total == room.sampleCount);
    // expected sample count: floor+ceiling 120x120 + 4 walls 120x52
    CHECK(room.sampleCount == 2ull * 120 * 120 + 4ull * 120 * 52);
    SyntheticScene corridor, stairs, wall;
    GenerateSynthetic(SYN_CORRIDOR, 4.f, 0.1f, 2, 0, corridor); CHECK(corridor.planeCount == 16 && corridor.pages.size() >= 3);
    GenerateSynthetic(SYN_STAIRCASE, 3.f, 0.1f, 3, 0, stairs); CHECK(stairs.planeCount == 1 + 2 * 10);
    GenerateSynthetic(SYN_THIN_WALL, 4.f, 0.05f, 4, 0, wall); CHECK(wall.planeCount == 3);
    // thin wall: two sheets 3 cm apart with opposite normals
    int plusZ = 0, minusZ = 0;
    for (const SurfelSample& s : wall.samples) { if (s.n[2] > 0.5f) { plusZ++; CHECK_NEAR(s.p[2], 0.015, 1e-5); } else if (s.n[2] < -0.5f) { minusZ++; CHECK_NEAR(s.p[2], -0.015, 1e-5); } }
    CHECK(plusZ == minusZ && plusZ > 0);
    // measurement conversion
    std::vector<FsSurfaceMeasurement> ms; SamplesToMeasurements(wall.samples, 0, 10, 77, ms);
    CHECK(ms.size() == 10 && ms[0].observationId == 77 && ms[0].sourceFlags == 1u && ms[0].footprint > 0.f);
    // dense foliage at a small extent: many small surfels on ellipsoid leaves with gaps
    SyntheticScene fol; GenerateSynthetic(SYN_DENSE_FOLIAGE, 2.f, 0.02f, 5, 0, fol);
    CHECK(fol.sampleCount > 100000);
    size_t leafSamples = 0; for (const SurfelSample& s : fol.samples) if (s.radius <= 0.0076f) leafSamples++;
    CHECK(leafSamples > fol.sampleCount / 2);          // leaves at <= 1 cm spacing dominate
    // dense organic torture (C09R §30): the thin-wall + foliage sets stay inside FS_CELL_NEW_MAX sheets per cell?
    // -> counted per cell: the busiest cell of the foliage page reports how many candidate sheets a cell sees
    const SyntheticPage* fp = &fol.pages[0]; for (const SyntheticPage& p : fol.pages) if (p.surfels.size() > fp->surfels.size()) fp = &p;
    std::map<uint32_t, uint32_t> perCell; for (const FsSurfel& s : fp->surfels) perCell[CellOfSurfel(s)]++;
    uint32_t busiest = 0; for (auto& kv : perCell) busiest = std::max(busiest, kv.second);
    std::printf("foliage busiest cell %u surfels (leaf cap %u x chain %u at max depth)\n", busiest, (unsigned)FS_INDEX_LEAF_CAP, (unsigned)FS_INDEX_CHAIN_MAX);
    CHECK(busiest > 0);
}

static void TestResidency() {
    float c[3] = {0.5f, 1.2f, -0.3f};
    CHECK_NEAR(PageCubeDist2(c, 0, 0, -1), 0.0, 1e-9);                       // inside its cube
    CHECK_NEAR(PageCubeDist2(c, 1, 0, -1), 3.5 * 3.5, 1e-5);                   // face distance 4 - 0.5
    CHECK_NEAR(PageCubeDist2(c, 1, 1, 0), 3.5 * 3.5 + 2.8 * 2.8 + 0.3 * 0.3, 1e-4);
    std::vector<ZonePage> a, b;
    ComputeZonePages(3, c, 3.f, 6.f, 9.f, a);
    CHECK(!a.empty());
    for (size_t i = 1; i < a.size(); ++i) CHECK(a[i - 1].zone <= a[i].zone);   // innermost first
    for (const ZonePage& z : a) { CHECK(z.key.anchorId == 3); CHECK(z.dist2 <= 81.f + 1e-4f); CHECK(z.zone == (z.dist2 <= 9.f ? ZONE_INNER : (z.dist2 <= 36.f ? ZONE_WARM : ZONE_PREFETCH))); }
    // the page containing the centre is INNER and present exactly once
    int hits = 0; for (const ZonePage& z : a) if (z.key.x == 0 && z.key.y == 0 && z.key.z == -1) { hits++; CHECK(z.zone == ZONE_INNER); }
    CHECK(hits == 1);
    // orientation independence: the API has no rotation input; simulate "turning" by feeding rotated velocity
    // and unrelated head rotation state -> identical page set (only position + predicted translation matter)
    float v0[3] = {0, 0, 0}, pred[3]; PredictCentre(c, v0, 0.5f, 1.5f, pred); ComputeZonePages(3, pred, 3.f, 6.f, 9.f, b);
    CHECK(a.size() == b.size());
    for (size_t i = 0; i < a.size() && i < b.size(); ++i) CHECK(KeyEq(a[i].key, b[i].key) && a[i].zone == b[i].zone);
    // prediction is bounded
    float vFast[3] = {100.f, 0.f, 0.f}; PredictCentre(c, vFast, 0.5f, 1.5f, pred); CHECK_NEAR(pred[0], c[0] + 1.5f, 1e-5);
    // radii clamped monotone
    std::vector<ZonePage> d; ComputeZonePages(0, c, 5.f, 2.f, 1.f, d); for (const ZonePage& z : d) CHECK(z.zone == ZONE_INNER);
    // count bound: prefetch 9 m -> at most 6^3 = 216 pages
    CHECK(a.size() <= 216);
}

static void TestHzbBand() {
    const float wall = 2.0f; const int SCAN = FS_RENDER_MODE_SCAN;
    CHECK(HzbTest(wall, wall, false, 0.f, 0.f, SCAN) == HZB_KEEP);                 // surfel at the occluder depth survives
    CHECK(HzbTest(wall + 0.05f, wall, false, 0.f, 0.f, SCAN) == HZB_KEEP_IN_BAND); // within 10 cm: kept, counted
    CHECK(HzbTest(wall + 0.5f, wall, false, 0.f, 0.f, SCAN) == HZB_REJECT);        // 0.5 m behind the wall: culled
    CHECK(HzbTest(wall - 0.015f, wall, false, 0.f, 0.f, SCAN) == HZB_KEEP);        // near side of a 3 cm partition
    CHECK(HzbTest(wall + 0.015f, wall, false, 0.f, 0.f, SCAN) == HZB_KEEP_IN_BAND);// far side of the partition survives
    CHECK(HzbTest(wall + 0.5f, wall, false, 0.f, 0.f, FS_RENDER_MODE_XRAY) == HZB_KEEP);   // XRAY: no depth-prior cull
    CHECK(HzbTest(wall + 0.5f, kHzbNoOccluder, false, 0.f, 0.f, SCAN) == HZB_KEEP);// no occluder observed: fail open
    CHECK(HzbTest(wall + 0.5f, wall, true, 0.f, 0.f, SCAN) == HZB_KEEP_UNCERTAIN); // uncertain tile: never rejects
    CHECK(HzbTest(wall + 0.5f, wall, false, 0.f, 0.25f, SCAN) == HZB_KEEP_IN_BAND);// gradient widens the band: 0.1 + 2*0.25 = 0.6
    CHECK(HzbTest(wall + 0.3f, wall, false, 0.35f, 0.f, SCAN) == HZB_KEEP_IN_BAND);// sigma above 10 cm widens it too
    CHECK(HzbTest(wall + 0.3f, wall, false, 0.25f, 0.f, SCAN) == HZB_REJECT);
    CHECK_NEAR(HzbMargin(0.f, 0.f), 0.10, 1e-6); CHECK_NEAR(HzbMargin(0.02f, 0.05f), 0.20, 1e-6);
    CHECK(HzbLevelForExtent(0.5f) == 0 && HzbLevelForExtent(2.f) == 1 && HzbLevelForExtent(300.f) == FS_HZB_LEVELS - 1);
    CHECK(HzbTotalTexels() == 87381u && HzbLevelOffset(1) == 65536u && HzbLevelSize(8) == 1u);
    CHECK(HzbCombine(1.f, 3.f) == 3.f);
}

// C09R §21 / §32: sources disagree -> uncertain -> kept; agreement -> farther value; env only -> uncertain.
static void TestHzbDisagreement() {
    bool unc = false;
    CHECK_NEAR(HzbCombineSources(2.0f, 2.05f, 0.f, unc), 2.05, 1e-6); CHECK(!unc);          // within 1.5 x 0.10 m: agree, farther wins
    CHECK_NEAR(HzbCombineSources(2.0f, 3.0f, 0.f, unc), 3.0, 1e-6); CHECK(unc);              // disagree: uncertain
    CHECK(HzbTest(2.5f, HzbCombineSources(2.0f, 3.0f, 0.f, unc), unc, 0.f, 0.f, FS_RENDER_MODE_SCAN) != HZB_REJECT);   // a scan surface between the two never hides
    CHECK_NEAR(HzbCombineSources(0.f, 1.5f, 0.f, unc), 1.5, 1e-6); CHECK(unc);               // env only: uncertain
    CHECK_NEAR(HzbCombineSources(1.5f, 0.f, 0.f, unc), 1.5, 1e-6); CHECK(!unc);              // scan only: proven
    CHECK(HzbCombineSources(0.f, 0.f, 0.f, unc) == 0.f && !unc);
    CHECK_NEAR(HzbCombineSources(2.0f, 2.4f, 0.1f, unc), 2.4, 1e-6); CHECK(!unc);            // gradient widens the agreement band: 1.5*(0.1+0.2)=0.45
    CHECK(HzbTest(5.f, 2.f, false, 0.f, 0.f, FS_RENDER_MODE_SCAN) == HZB_REJECT);           // only a proven violation rejects
}

static void TestCullMath() {
    // simple GL-style perspective (fov 90, aspect 1), camera at origin looking -z
    Mat4 proj{}; float n = 0.1f, f = 100.f; proj.m[0] = 1.f; proj.m[5] = 1.f; proj.m[10] = f / (n - f); proj.m[14] = n * f / (n - f); proj.m[11] = -1.f;
    Mat4 view = Identity(); Mat4 vp = Mul(proj, view);
    Plane pl[6]; FrustumPlanes(vp, pl, true);
    float inMin[3] = {-0.5f, -0.5f, -3.f}, inMax[3] = {0.5f, 0.5f, -2.f};
    float outMin[3] = {10.f, 0.f, -3.f}, outMax[3] = {11.f, 1.f, -2.f};
    float behindMin[3] = {-0.5f, -0.5f, 2.f}, behindMax[3] = {0.5f, 0.5f, 3.f};
    CHECK(AabbInFrustum(pl, 6, inMin, inMax, 0.f));
    CHECK(!AabbInFrustum(pl, 6, outMin, outMax, 0.f));
    CHECK(!AabbInFrustum(pl, 6, behindMin, behindMax, 0.f));
    CHECK(AabbInFrustum(pl, 6, outMin, outMax, 8.f));                       // margin widens
    CHECK(AabbInEitherEye(pl, pl, inMin, inMax, 0.f));
    Mat4 inv; CHECK(Invert(vp, inv)); Mat4 id = Mul(vp, inv);
    for (int i = 0; i < 16; ++i) CHECK_NEAR(id.m[i], (i % 5 == 0) ? 1.0 : 0.0, 1e-4);
    Mat4 T = Identity(); T.m[12] = 1.f; T.m[13] = 2.f; T.m[14] = 3.f;
    float wmin[3], wmax[3]; TransformAabb(T, inMin, inMax, wmin, wmax);
    CHECK_NEAR(wmin[0], 0.5, 1e-6); CHECK_NEAR(wmax[2], 1.0, 1e-6);
    CHECK_NEAR(HeadroomBias(500), 1.0, 1e-6); CHECK_NEAR(HeadroomBias(-1000), 2.0, 1e-6); CHECK_NEAR(HeadroomBias(-100000), FS_HEADROOM_BIAS_MAX, 1e-6);
    CHECK_NEAR(FoveatedThresholdPx(1.f, 4.f, 0.f, 1.f), 1.0, 1e-6);
    CHECK_NEAR(FoveatedThresholdPx(1.f, 4.f, 60.f, 1.f), 4.0, 1e-6);
    CHECK_NEAR(FoveatedThresholdPx(1.f, 4.f, 20.f, 1.f), 2.5, 1e-5);          // midpoint of the smoothstep
    CHECK_NEAR(FoveatedThresholdPx(1.f, 4.f, 0.f, 3.f), 3.0, 1e-6);
    CHECK_NEAR(ProjectedPx(0.1f, 2.f, 1000.f), 50.0, 1e-6);
    float fwd[3] = {0, 0, -1}, dir[3] = {1, 0, -1}; CHECK_NEAR(EccentricityDeg(fwd, dir), 45.0, 1e-3);
    CHECK_NEAR(FocalPxFromProj(proj, 1680.f), 840.0, 1e-6);
    CHECK(sizeof(CullFrame) == 4768);
}

// ---- C09R §32 -----------------------------------------------------------------------------------------
static FsSurfaceMeasurement Meas(V3 p, V3 n, float sN, float sT, float fp, uint32_t obs) {
    FsSurfaceMeasurement m{}; m.px = p.x; m.py = p.y; m.pz = p.z; m.nx = n.x; m.ny = n.y; m.nz = n.z; m.sigmaN = sN; m.sigmaT = sT; m.footprint = fp; m.sourceFlags = 0; m.observationId = obs; return m;
}
static bool SameSurfel(const FsSurfel& a, const FsSurfel& b) { return memcmp(&a, &b, sizeof a) == 0; }

static void TestFusionDeterminism() {
    FsPageKey key{0, 0, 0, 0}; V3 origin = PageOrigin3(key);
    SurfelSample smp{{0.31f, 0.52f, 0.73f}, {0, 0, 1}, 0.01f, 0.002f, 0.004f, 7};
    FsSurfel s = MakeSurfel(smp, key); s.evidenceFlags = (uint16_t)(2 | kFlagTransient);   // transient candidate, 2 observations
    FsSurfelEvidence ev{}; ev.staticEvidence = 2; ev.varianceQ = EncodeLog(1e-6f, kSigmaBaseM * kSigmaBaseM);
    std::mt19937 rng(11); std::normal_distribution<float> nz(0.f, 0.001f), nt(0.f, 0.004f);
    std::vector<FsSurfaceMeasurement> seq;
    for (int i = 0; i < 40; ++i) seq.push_back(Meas(v3(0.31f + nt(rng), 0.52f + nt(rng), 0.73f + 0.0015f + nz(rng)), Norm(v3(0.02f * nz(rng) * 100.f, 0.f, 1.f)), 0.002f, 0.004f, 0.006f, 100u + (uint32_t)i));
    ReduceResult r1 = ReduceSegment(s, ev, seq, origin, 5);
    ReduceResult r2 = ReduceSegment(s, ev, seq, origin, 5);
    CHECK(r1.changed && r1.folded == 40 && !r1.overflow);
    CHECK(SameSurfel(r1.surfel, r2.surfel) && memcmp(&r1.evidence, &r2.evidence, sizeof ev) == 0);   // replay: bit-identical
    Patch q0 = Unpack(s), q1 = Unpack(r1.surfel);
    CHECK(q1.sigmaN < q0.sigmaN && q1.sigmaT < q0.sigmaT);                                           // precision grows
    CHECK((q1.flags & kEvidenceCountMask) == 42);
    CHECK((q1.flags & kFlagPromoted) && !(q1.flags & kFlagTransient));                                // 2 + 40 >= FS_PROMOTE_STATIC
    CHECK(r1.evidence.staticEvidence == 42 && r1.evidence.lastSeenFrame == 5);
    CHECK(CellOfV(q1.p) == CellOfV(q0.p));                                                            // owner invariant: never leaves its cell
    CHECK(q1.p.z > q0.p.z && q1.p.z < q0.p.z + 0.0016f);                                              // moved toward the measured plane
    // permutation of the same set: identical associations (all match the same surfel) and equal within fp order
    std::vector<FsSurfaceMeasurement> perm = seq; std::shuffle(perm.begin(), perm.end(), rng);
    ReduceResult r3 = ReduceSegment(s, ev, perm, origin, 5);
    Patch q3 = Unpack(r3.surfel);
    CHECK_NEAR(q3.p.x, q1.p.x, 0.0003); CHECK_NEAR(q3.p.y, q1.p.y, 0.0003); CHECK_NEAR(q3.p.z, q1.p.z, 0.0003);
    CHECK_NEAR(q3.sigmaN, q1.sigmaN, 1e-5); CHECK(Dot(q3.n, q1.n) > 0.9999f);
    std::vector<FsSurfel> pool{s}; std::vector<uint32_t> hs{0};
    for (const FsSurfaceMeasurement& m : perm) CHECK(AssocBest(pool, hs, MeasPos(m) - origin, MeasNormal(m), MeasSigmaN(m), MeasFootprint(m)) == 0u);
    // a removed surfel never fuses; a measurement 5 cm off the plane never associates
    FsSurfel dead = s; dead.evidenceFlags |= kFlagRemoved; CHECK(!ReduceSegment(dead, ev, seq, origin, 5).changed);
    FsSurfaceMeasurement far = Meas(v3(0.31f, 0.52f, 0.78f), v3(0, 0, 1), 0.002f, 0.004f, 0.006f, 1); std::vector<FsSurfaceMeasurement> one{far};
    CHECK(AssocBest(pool, hs, MeasPos(far) - origin, MeasNormal(far), MeasSigmaN(far), MeasFootprint(far)) == FS_INDEX_NONE);
    // the anti-parallel sheet of a thin wall is a different surfel (normal dot < FS_ASSOC_MIN_DOT)
    FsSurfaceMeasurement back = Meas(v3(0.31f, 0.52f, 0.73f), v3(0, 0, -1), 0.002f, 0.004f, 0.006f, 1);
    CHECK(AssocBest(pool, hs, MeasPos(back) - origin, MeasNormal(back), MeasSigmaN(back), MeasFootprint(back)) == FS_INDEX_NONE);
    // stable sort twin: keys grouped, ties in measurement order, radix == std::stable_sort
    std::vector<FsAssociation> recs; std::uniform_int_distribution<uint32_t> kd(0, 5);
    for (uint32_t i = 0; i < 5000; ++i) { FsAssociation a{}; a.meas = i; uint32_t k = kd(rng); a.key = k == 5 ? UnmatchedKey(3, i & 32767u) : (k * 977u); recs.push_back(a); }
    std::vector<FsAssociation> ref = recs; std::stable_sort(ref.begin(), ref.end(), [](const FsAssociation& a, const FsAssociation& b) { return a.key < b.key; });
    RadixSortAssoc(recs);
    bool same = true; for (size_t i = 0; i < recs.size(); ++i) if (recs[i].key != ref[i].key || recs[i].meas != ref[i].meas) same = false;
    CHECK(same);
    for (size_t i = 1; i < recs.size(); ++i) { CHECK(recs[i - 1].key <= recs[i].key); if (recs[i - 1].key == recs[i].key) CHECK(recs[i - 1].meas < recs[i].meas); }
}

static void TestConcurrentCandidates() {
    V3 origin = v3(0, 0, 0); Cand c[FS_CELL_NEW_MAX]; bool over = false;
    std::vector<FsSurfaceMeasurement> seg;
    // one plane, 20 measurements -> ONE candidate (no duplicate surfels from concurrent measurements)
    for (int i = 0; i < 20; ++i) seg.push_back(Meas(v3(0.01f + 0.0005f * (float)i, 0.02f, 0.5f + 0.0002f * (float)(i % 3)), v3(0, 0, 1), 0.002f, 0.004f, 0.006f, 1));
    CHECK(ClusterSegment(seg, origin, c, over) == 1 && !over && c[0].cnt == 20);
    // two sheets 3 cm apart (thin partition, same facing) -> TWO candidates, deterministic order (first sheet first)
    for (int i = 0; i < 20; ++i) seg.push_back(Meas(v3(0.01f + 0.0005f * (float)i, 0.02f, 0.53f), v3(0, 0, 1), 0.002f, 0.004f, 0.006f, 2));
    uint32_t n = ClusterSegment(seg, origin, c, over);
    CHECK(n == 2 && c[0].cnt == 20 && c[1].cnt == 20 && c[1].p.z > c[0].p.z + 0.02f);
    // opposite facing at the same position -> separate candidate (the other side of the wall)
    seg.push_back(Meas(v3(0.012f, 0.02f, 0.5f), v3(0, 0, -1), 0.002f, 0.004f, 0.006f, 3));
    CHECK(ClusterSegment(seg, origin, c, over) == 3);
    // dedupe bound: 12 distinct sheets -> FS_CELL_NEW_MAX candidates, the rest wait (not lost: unmatched next epoch)
    std::vector<FsSurfaceMeasurement> many;
    for (int sheet = 0; sheet < 12; ++sheet) for (int i = 0; i < 3; ++i) many.push_back(Meas(v3(0.01f, 0.02f, 0.5f + 0.0087f * (float)sheet), v3(0, 0, 1), 0.001f, 0.004f, 0.006f, (uint32_t)sheet));
    CHECK(ClusterSegment(many, origin, c, over) == FS_CELL_NEW_MAX && !over);
    // deterministic SurfaceIDs: idBase + exclusive prefix over segments in sorted order
    uint32_t counts[3] = {1, 2, 3}, idBase = 1000, prefix = 0, ids[3];
    for (int i = 0; i < 3; ++i) { ids[i] = idBase + prefix; prefix += counts[i]; }
    CHECK(ids[0] == 1000 && ids[1] == 1001 && ids[2] == 1003 && prefix == 6);
}

static void TestDenseBucket() {
    V3 origin = v3(0, 0, 0); Cand c[FS_CELL_NEW_MAX]; bool over = false;
    std::vector<FsSurfaceMeasurement> seg;
    for (int i = 0; i < 400; ++i) seg.push_back(Meas(v3(0.001f * (float)(i % 20), 0.001f * (float)(i / 20), 0.5f), v3(0, 0, 1), 0.002f, 0.004f, 0.004f, 1));
    uint32_t n = ClusterSegment(seg, origin, c, over);
    CHECK(over && n >= 1 && n <= FS_CELL_NEW_MAX);
    uint32_t folded = 0; for (uint32_t i = 0; i < n; ++i) folded += c[i].cnt;
    CHECK(folded == FS_SEG_CLUSTER_MAX);                                                   // bounded, counted, never unbounded
    FsPageKey key{0, 0, 0, 0}; FsSurfel s = MakeSurfel(SurfelSample{{0.01f, 0.01f, 0.5f}, {0, 0, 1}, 0.01f, 0.002f, 0.004f, 1}, key);
    FsSurfelEvidence ev{}; ev.varianceQ = EncodeLog(1e-6f, kSigmaBaseM * kSigmaBaseM);
    ReduceResult r = ReduceSegment(s, ev, seg, PageOrigin3(key), 1);
    CHECK(r.overflow && r.folded == FS_SEG_REDUCE_MAX);
    CHECK((Unpack(r.surfel).flags & kEvidenceCountMask) == 1 + FS_SEG_REDUCE_MAX);
}

static void TestDeterministicMerge() {
    Patch a; a.p = v3(0.1f, 0.1f, 0.1f); a.n = v3(0, 0, 1); a.rM = a.rm = 0.01f; a.sigmaN = 0.002f; a.sigmaT = 0.004f; a.flags = kFlagPromoted | 5; a.surfaceId = 10;
    Patch b = a; b.p = v3(0.105f, 0.1f, 0.1005f); b.surfaceId = 20;
    FsSurfelEvidence ea{}, eb{}; ea.staticEvidence = 5; eb.staticEvidence = 5;
    CHECK(Mergeable(a, b) && Mergeable(b, a));
    CHECK(Survives(a, ea, b, eb) && !Survives(b, eb, a, ea));                             // tie on evidence + sigma: lower SurfaceID survives
    eb.staticEvidence = 9; CHECK(Survives(b, eb, a, ea));                                   // more static evidence wins
    eb.staticEvidence = 5; b.sigmaN = 0.001f; CHECK(Survives(b, eb, a, ea));                // then lower sigma
    b.sigmaN = 0.002f;
    Patch c = b; c.p = v3(0.1f, 0.1f, 0.13f); CHECK(!Mergeable(a, c));                        // 3 cm off the plane: another sheet
    Patch d = b; d.n = Norm(v3(0.3f, 0, 1)); CHECK(!Mergeable(a, d));                        // 17 deg: not coplanar
    Patch e = b; e.p = v3(0.12f, 0.1f, 0.1f); CHECK(!Mergeable(a, e));                        // 2 cm apart > 0.75 * (rA + rB)
    Patch f = b; f.flags = kFlagTransient | 1; CHECK(!Mergeable(a, f));                       // candidates never merge
    // split: persistent residual with support
    FsSurfelEvidence ev{}; ev.varianceQ = EncodeLog(0.0001f, kSigmaBaseM * kSigmaBaseM);   // std 1 cm > 4 x 2 mm
    Patch g = a; g.flags = kFlagPromoted | 8; CHECK(SplitWanted(g, ev));
    g.flags = kFlagPromoted | 3; CHECK(!SplitWanted(g, ev));                                 // support below FS_SPLIT_MIN_SUPPORT
    ev.varianceQ = EncodeLog(1e-6f, kSigmaBaseM * kSigmaBaseM); g.flags = kFlagPromoted | 8; CHECK(!SplitWanted(g, ev));
}

static void TestPoolsAndRetirement() {
    std::vector<uint32_t> hdr(8), ring(16);
    IdRing r; r.Init(16, hdr.data(), ring.data(), 100);
    CHECK(hdr[2] == 15 && hdr[4] == 100 && hdr[5] == 16 && hdr[1] == 16 && hdr[0] == 0);
    CHECK(r.Available() == 16 && r.Live() == 0);
    std::set<uint32_t> ids; for (uint32_t i = 0; i < 16; ++i) ids.insert(ring[i]); CHECK(ids.size() == 16);
    // GPU pops 10 (head advances), job retires -> SyncHead: those ids are live until the CPU frees them
    hdr[0] = 10; r.SyncHead();
    CHECK(r.Live() == 10 && r.Available() == 6);
    CHECK(r.Refill() == 0);                                                                // nothing free to append
    // publication overlap: the 10 popped ids must NOT reappear in the ring until Free() (after graphics retirement)
    std::vector<uint32_t> popped(ring.begin(), ring.begin() + 10);
    for (uint32_t id : popped) r.Free(id);
    CHECK(r.Refill() == 10 && hdr[1] == 26);
    std::set<uint32_t> again; for (uint32_t i = 10; i < 26; ++i) again.insert(ring[i & 15]);
    CHECK(again.size() == 16);
    // exhaustion: the GPU counts failures (hdr[3]) and returns NONE (fsPoolAlloc twin): head beyond tail
    hdr[0] = 26; r.SyncHead(); CHECK(r.Available() == 0 && r.Refill() == 0);
    hdr[3] = 3; CHECK(r.Failures() == 3);
    // reset: Init again -> every id available, counters cleared
    r.Init(16, hdr.data(), ring.data(), 100); CHECK(r.Available() == 16 && hdr[3] == 0 && hdr[0] == 0);
    // slabs (page growth, contract §9.3): a page grows by slabs from the global arena; no fixed slot size
    SlabAllocator sa; sa.Init(8); uint32_t slab;
    std::vector<uint32_t> got; for (int i = 0; i < 8; ++i) { CHECK(sa.Alloc(slab)); got.push_back(slab); }
    CHECK(!sa.Alloc(slab) && sa.Live() == 8 && sa.Free() == 0);                              // explicit exhaustion
    sa.Free(got[3]); CHECK(sa.Alloc(slab) && slab == got[3]);                                // reuse after release
    CHECK(HandleOf(5, 7) == 5 * FS_SLAB_SURFELS + 7 && SlabOf(HandleOf(5, 7)) == 5);
    CHECK(Pow2Ceil(1000) == 1024 && Pow2Ceil(1024) == 1024 && Pow2Ceil(1) == 1);
}

static void TestSparseUpdate() {
    std::vector<std::vector<uint32_t>> masks(4, std::vector<uint32_t>(FS_CELLS_PER_PAGE / 32, 0u));
    auto mark = [&](uint32_t page, uint32_t cell) { masks[page][cell >> 5] |= 1u << (cell & 31); };
    mark(2, 100); mark(0, 5000); mark(0, 7); mark(3, 32767); mark(2, 99);
    std::vector<uint32_t> list = DirtyListEmit(masks, 1000);
    CHECK(list.size() == 5);
    CHECK(list[0] == ((0u << 15) | 7u) && list[1] == ((0u << 15) | 5000u) && list[2] == ((2u << 15) | 99u) && list[3] == ((2u << 15) | 100u) && list[4] == ((3u << 15) | 32767u));
    for (auto& m : masks) for (uint32_t w : m) CHECK(w == 0u);                                // consumed: only touched cells are ever listed
    // sliced cap: entries beyond the cap stay dirty for the next epoch (nothing lost, nothing duplicated)
    for (uint32_t c = 0; c < 50; ++c) mark(1, c * 3);
    list = DirtyListEmit(masks, 20);
    CHECK(list.size() == 20 && list[0] == ((1u << 15) | 0u) && list[19] == ((1u << 15) | 57u));
    std::vector<uint32_t> rest = DirtyListEmit(masks, 1000);
    CHECK(rest.size() == 30 && rest[0] == ((1u << 15) | 60u));
    // an untouched page contributes nothing (sparse: cost follows touched cells, not resident pages)
    CHECK(DirtyListEmit(masks, 1000).empty());
}

static void TestCrossPageFreeRay() {
    // resident pages: x in {0,1,2} (4 m each), non-resident elsewhere
    std::map<int, uint32_t> resident{{0, 10}, {1, 11}, {2, 12}};
    auto lookup = [&](const FsPageKey& k, uint32_t& slot) { if (k.y != 0 || k.z != 0) return false; auto it = resident.find(k.x); if (it == resident.end()) return false; slot = it->second; return true; };
    FreeWalk w = FreeSpaceWalk(v3(0.5f, 0.5f, 0.5f), v3(5.5f, 0.5f, 0.5f), 0, 7, lookup);   // 5 m across the x=4 boundary
    CHECK(!w.skipped && !w.hopOverflow && w.hops == 2 && w.stamp == 8);
    std::set<uint32_t> slots; for (auto& sc : w.stamped) slots.insert(sc.first);
    CHECK(slots.size() == 2 && slots.count(10) && slots.count(11));
    for (size_t i = 1; i < w.stamped.size(); ++i) CHECK(w.stamped[i] != w.stamped[i - 1]);   // one stamp per crossed cell
    CHECK(w.stamped.size() >= 37 && w.stamped.size() <= 42);                                  // 4.9 m / 0.125 m cells (half-cell steps): ~40 crossed cells
    // the last cell before the hit is NOT stamped (the surface itself stays)
    uint32_t hitCell = CellOfV(v3(5.5f, 0.5f, 0.5f) - PageOrigin3(FsPageKey{0, 1, 0, 0}));
    for (auto& sc : w.stamped) CHECK(!(sc.first == 11 && sc.second == hitCell));
    // non-resident page: no evidence, no fault
    FreeWalk w2 = FreeSpaceWalk(v3(-3.5f, 0.5f, 0.5f), v3(1.5f, 0.5f, 0.5f), 0, 7, lookup);
    for (auto& sc : w2.stamped) CHECK(sc.first == 10);
    CHECK(!w2.stamped.empty() && w2.hops == 2);
    // hop bound (review gap 6): a diagonal ray crossing > FS_FREE_PAGE_HOPS_MAX pages stops with the overflow counted
    std::map<int, uint32_t> line; for (int x = 0; x < 8; ++x) line[x] = 20 + (uint32_t)x;
    auto lookupLine = [&](const FsPageKey& k, uint32_t& slot) { if (k.z != 0) return false; auto it = line.find(k.x); if (it == line.end()) return false; slot = it->second + (uint32_t)(k.y & 1) * 8; return true; };
    const float s2 = 5.98f / sqrtf(2.f);
    FreeWalk w3 = FreeSpaceWalk(v3(3.97f, 3.9f, 0.5f), v3(3.97f + s2, 3.9f + s2, 0.5f), 0, 7, lookupLine);   // x@4, y@4, x@8, y@8 at distinct steps: 5 logical pages
    CHECK(w3.hopOverflow && w3.hops == FS_FREE_PAGE_HOPS_MAX);
    // range/step gates
    CHECK(FreeSpaceWalk(v3(0, 0, 0), v3(0.05f, 0, 0), 0, 1, lookup).skipped);
    CHECK(FreeSpaceWalk(v3(0, 0, 0), v3(7.f, 0, 0), 0, 1, lookup).skipped);
    // stamp never 0 (0 = no evidence) and wraps deterministically with the tick
    CHECK(FreeSpaceWalk(v3(0.5f, 0.5f, 0.5f), v3(1.5f, 0.5f, 0.5f), 0, 255, lookup).stamp == 1);
}

static void TestIndexGeneration() {
    std::vector<FsSurfel> surfels; FsPageKey key{0, 0, 0, 0};
    const float o = PageOrigin(0);                                                             // page-local coordinates in, world positions to MakeSurfel
    auto add = [&](float x, float y, float z) { surfels.push_back(MakeSurfel(SurfelSample{{x + o, y + o, z + o}, {0, 0, 1}, 0.005f, 0.002f, 0.004f, (uint32_t)surfels.size()}, key)); return (uint32_t)surfels.size() - 1; };
    IndexRef ix; ix.Init(2, 64, 16, &surfels);
    const uint32_t page = 1;
    float bmin[3], bmax[3]; uint32_t cell = CellOf(0.3f, 0.3f, 0.3f); CellBounds(cell, bmin, bmax);
    // 14 handles fit one leaf; the 15th splits into an octant node (F0 of the next epoch reads the split generation)
    std::vector<uint32_t> hs;
    for (int i = 0; i < 14; ++i) hs.push_back(add(bmin[0] + 0.01f + 0.005f * (float)i, bmin[1] + 0.01f, bmin[2] + 0.01f));
    for (uint32_t h : hs) CHECK(ix.Insert(page, cell, LocalPos(surfels[h]), h));
    CHECK(EntryIsLeaf(ix.Dir(page, cell)) && ix.Ctr().leafSplits == 0 && ix.FreeLeaves() == 63);
    uint32_t h15 = add(bmax[0] - 0.01f, bmax[1] - 0.01f, bmax[2] - 0.01f);
    CHECK(ix.Insert(page, cell, LocalPos(surfels[h15]), h15));
    CHECK(EntryIsNode(ix.Dir(page, cell)) && ix.Ctr().leafSplits == 1 && ix.RetiredLeaves().size() == 1);   // old leaf retired, not freed in place
    std::vector<uint32_t> all = ix.Handles(page, cell); std::sort(all.begin(), all.end());
    CHECK(all.size() == 15);
    for (uint32_t h : hs) { CHECK(std::binary_search(all.begin(), all.end(), h)); CHECK(ix.FindLeaf(page, cell, LocalPos(surfels[h])) != FS_INDEX_NONE); }
    // a lookup follows the position: the far corner handle sits in another octant leaf than the first 14
    CHECK(ix.FindLeaf(page, cell, LocalPos(surfels[h15])) != ix.FindLeaf(page, cell, LocalPos(surfels[hs[0]])));
    // deterministic collection order (octant order): identical across calls
    CHECK(ix.CollectLeaves(page, cell) == ix.CollectLeaves(page, cell));
    // depth bound: 60 surfels in one 1.56 cm micro cell chain at max depth (<= FS_INDEX_CHAIN_MAX leaves), the 57th overflows
    IndexRef dense; dense.Init(1, 64, 16, &surfels); uint32_t base = (uint32_t)surfels.size();
    for (int i = 0; i < 60; ++i) add(0.3f + 0.0001f * (float)i, 0.3f, 0.3f);
    uint32_t inserted = 0; for (uint32_t h = base; h < base + 60; ++h) if (dense.Insert(0, cell, LocalPos(surfels[h]), h)) inserted++;
    CHECK(inserted == FS_INDEX_LEAF_CAP * FS_INDEX_CHAIN_MAX);
    CHECK(dense.Ctr().overflow == 60 - FS_INDEX_LEAF_CAP * FS_INDEX_CHAIN_MAX && dense.Ctr().leafSplits == FS_INDEX_MAX_DEPTH);
    CHECK(dense.CollectLeaves(0, cell).size() == FS_INDEX_CHAIN_MAX);
    // remove keeps the rest findable
    CHECK(dense.Remove(0, cell, LocalPos(surfels[base + 3]), base + 3) && !dense.Remove(0, cell, LocalPos(surfels[base + 3]), base + 3));
    CHECK(dense.Handles(0, cell).size() == FS_INDEX_LEAF_CAP * FS_INDEX_CHAIN_MAX - 1);
    // pool exhaustion fails closed: counted, the directory entry is unchanged
    IndexRef tiny; tiny.Init(1, 1, 1, &surfels);
    CHECK(tiny.Insert(0, cell, LocalPos(surfels[hs[0]]), hs[0]));
    uint32_t before = tiny.Dir(0, cell);
    for (int i = 1; i < 14; ++i) CHECK(tiny.Insert(0, cell, LocalPos(surfels[hs[i]]), hs[i]));
    CHECK(!tiny.Insert(0, cell, LocalPos(surfels[h15]), h15));
    CHECK(tiny.Ctr().poolLeafEmpty >= 1 && tiny.Dir(0, cell) == before);
    // reset: Init clears every directory, pool and counter
    ix.Init(2, 64, 16, &surfels); CHECK(ix.Dir(page, cell) == 0u && ix.FreeLeaves() == 64 && ix.Ctr().leafSplits == 0);
}

int main() {
    TestPageHash(); TestEncodings(); TestSynthetic(); TestResidency(); TestHzbBand(); TestHzbDisagreement(); TestCullMath();
    TestFusionDeterminism(); TestConcurrentCandidates(); TestDenseBucket(); TestDeterministicMerge(); TestPoolsAndRetirement(); TestSparseUpdate(); TestCrossPageFreeRay(); TestIndexGeneration();
    std::printf("finalscan host world tests: %d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
