// Host (linux x86_64) unit tests for the world/render modules: pure headers only (no Vulkan, no executor).
// Covers: page hash (insert/lookup/erase/robin-hood/overflow), fixed-point/oct/log encodings, Morton and
// root word packing, cluster layout, synthetic page counts, residency sphere-cube math and orientation
// independence, HZB band rule, cluster build CPU reference invariants, coverage preserving gaps (foliage).
#include "../src/host/world/fs_world_types.h"
#include "../src/host/world/fs_page_hash.h"
#include "../src/host/world/fs_meas_ring.h"
#include "../src/host/world/fs_residency_math.h"
#include "../src/host/world/fs_synthetic.h"
#include "../src/host/world/fs_cluster_build.h"
#include "../src/host/render/fs_cull_math.h"
#include "../src/host/render/fs_hzb_math.h"
#include "../src/host/render/fs_render.h"
#include <cstdio>
#include <cmath>
#include <vector>
#include <set>

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
    // root word
    RootWord r; r.frontCount = 123456; r.published = true; r.parity = 1; r.treeValid = true; r.leafShift = 3; r.generation = 0x1FE;
    RootWord u = UnpackRoot(PackRoot(r));
    CHECK(u.frontCount == 123456 && u.published && u.parity == 1 && u.treeValid && u.leafShift == 3 && u.generation == 0xFE);
    r.frontCount = 1u << 20; CHECK(UnpackRoot(PackRoot(r)).frontCount == FS_MAX_FRONT_COUNT);
    CHECK(PackRoot(RootWord{}) == 0u);
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

static void TestClusterLayout() {
    ClusterLayout L = ComputeClusterLayout(0, 0); CHECK(L.levels == 0 && L.total == 0);
    L = ComputeClusterLayout(50, 0); CHECK(L.levels == 1 && L.total == 1 && L.count[0] == 1 && L.offset[0] == 0);
    L = ComputeClusterLayout(16384, 0);
    CHECK(L.levels == 4 && L.count[0] == 256 && L.count[1] == 32 && L.count[2] == 4 && L.count[3] == 1 && L.total == 293);
    CHECK(L.offset[3] == 0 && L.offset[2] == 1 && L.offset[1] == 5 && L.offset[0] == 37);
    CHECK(NodesPerSlotFor(16384) == 293);
    CHECK(ChooseLeafShift(16384, 293) == 0 && ChooseLeafShift(16384, 100) == 2);
    uint32_t bigCap = 16384 * FS_BIG_SLOT_FACTOR;
    CHECK(NodesPerSlotFor(bigCap) == FS_CLUSTER_MAX_NODES_PER_PAGE);
    uint32_t sh = ChooseLeafShift(bigCap, FS_CLUSTER_MAX_NODES_PER_PAGE);
    CHECK(sh > 0 && ComputeClusterLayout(bigCap, sh).total <= FS_CLUSTER_MAX_NODES_PER_PAGE);      // overflow -> coarser leaves
    CHECK((64u << sh) <= FS_CLUSTER_LEAF_LOOP_MAX);
}

static void CheckTree(const std::vector<FsClusterNode>& nodes, const ClusterLayout& L, uint32_t n) {
    CHECK(nodes.size() == L.total);
    if (L.levels == 0) return;
    uint32_t leafSum = 0;
    for (uint32_t j = 0; j < L.count[0]; ++j) { const FsClusterNode& leaf = nodes[L.offset[0] + j]; CHECK(leaf.childCount == 0); CHECK(leaf.leafSurfelCount <= L.leafSize); leafSum += leaf.leafSurfelCount; }
    CHECK(leafSum == n);
    for (uint32_t lv = 1; lv < L.levels; ++lv)
        for (uint32_t j = 0; j < L.count[lv]; ++j) {
            const FsClusterNode& p = nodes[L.offset[lv] + j];
            CHECK(p.childCount >= 1 && p.childCount <= FS_CLUSTER_FANOUT);
            uint32_t sum = 0;
            for (uint32_t c = 0; c < p.childCount; ++c) {
                const FsClusterNode& ch = nodes[p.firstChildOrSurfel + c];
                for (int a = 0; a < 3; ++a) { CHECK(ch.bmin[a] >= p.bmin[a] - 1e-6f); CHECK(ch.bmax[a] <= p.bmax[a] + 1e-6f); }
                CHECK(ch.repRadius <= p.repRadius + 1e-6f);                    // ownError monotone up the tree
                sum += ch.surfelCount;
            }
            CHECK(p.surfelCount == std::min<uint32_t>(sum, 65535));
            CHECK(p.coverage <= 65535);
        }
    CHECK(nodes[0].surfelCount == std::min<uint32_t>(n, 65535));
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
}

static void TestClusterBuildAndCoverage() {
    // flat wall page: 1 cm spacing -> coverage ~1 at every level
    SyntheticScene wall; GenerateSynthetic(SYN_ROOM_BOX, 3.f, 0.01f, 9, 0, wall);
    const SyntheticPage* big = &wall.pages[0]; for (const SyntheticPage& p : wall.pages) if (p.surfels.size() > big->surfels.size()) big = &p;
    std::vector<FsSurfel> surf = big->surfels; MortonSort(surf);
    for (size_t i = 1; i < surf.size(); ++i) CHECK(MortonOfCell(CellOfSurfel(surf[i - 1])) <= MortonOfCell(CellOfSurfel(surf[i])));
    std::vector<FsClusterNode> nodes; bool overflow = false;
    uint32_t n = (uint32_t)std::min<size_t>(surf.size(), 16384);
    ClusterLayout L = BuildClusterTree(surf.data(), n, 1000, NodesPerSlotFor(16384), nodes, &overflow);
    CHECK(!overflow); CheckTree(nodes, L, n);
    CHECK(nodes[L.offset[0]].firstChildOrSurfel == 1000);                    // leaf references the global FRONT offset
    float leafCovAvg = 0.f; for (uint32_t j = 0; j < L.count[0]; ++j) leafCovAvg += nodes[L.offset[0] + j].coverage / 65535.f; leafCovAvg /= (float)L.count[0];
    std::printf("wall leaf coverage avg %.3f root %.3f\n", leafCovAvg, nodes[0].coverage / 65535.f);
    // solid wall: leaves whose 64-run straddles a Morton jump span two patches (~0.5), pure-cell leaves ~0.9;
    // the area-weighted combination saturates the root (README: cell-aligned leaves are the follow-up)
    CHECK(leafCovAvg > 0.6f);
    CHECK(nodes[0].coverage / 65535.f > 0.9f);
    CHECK(FootprintA(nodes[L.offset[0]].reserved) > 0.f && FootprintB(nodes[L.offset[0]].reserved) > 0.f);
    // cone: a wall leaf has a tight cone (single normal) -> coneCos ~ 1
    CHECK(nodes[L.offset[0]].coneCos > 0.99f);
    // single-node tree
    std::vector<FsClusterNode> one; ClusterLayout L1 = BuildClusterTree(surf.data(), 40, 0, 293, one, &overflow);
    CHECK(L1.levels == 1 && one.size() == 1 && one[0].childCount == 0 && one[0].leafSurfelCount == 40);
    // node cap overflow -> coarser leaves, counted
    std::vector<FsClusterNode> coarse; ClusterLayout L2 = BuildClusterTree(surf.data(), n, 0, 40, coarse, &overflow);
    CHECK(overflow && L2.total <= 40 && L2.leafSize > 64); CheckTree(coarse, L2, n);
    // foliage: gaps must survive aggregation (§13.4 coverage-preserving far LOD): coverage < 0.6
    SyntheticScene fol; GenerateSynthetic(SYN_DENSE_FOLIAGE, 2.f, 0.01f, 5, 0, fol);
    const SyntheticPage* fp = &fol.pages[0]; for (const SyntheticPage& p : fol.pages) if (p.surfels.size() > fp->surfels.size()) fp = &p;
    std::vector<FsSurfel> fs = fp->surfels; MortonSort(fs);
    // only leaf surfels (radius <= 7.5 mm) so the wall/floor of the room do not dominate the page
    std::vector<FsSurfel> leaves; for (const FsSurfel& s : fs) if (DecodeLogRadius(s.radiusMajor) <= 0.0076f) leaves.push_back(s);
    uint32_t fn = (uint32_t)std::min<size_t>(leaves.size(), 16384 * FS_BIG_SLOT_FACTOR);
    std::vector<FsClusterNode> fnodes; ClusterLayout FL = BuildClusterTree(leaves.data(), fn, 0, FS_CLUSTER_MAX_NODES_PER_PAGE, fnodes, &overflow);
    CheckTree(fnodes, FL, fn);
    // gaps preserved at the leaf-cluster level and one level up (the whole-bush root may saturate: many layers)
    float leafCov = 0.f; for (uint32_t j = 0; j < FL.count[0]; ++j) leafCov += fnodes[FL.offset[0] + j].coverage / 65535.f; leafCov /= (float)FL.count[0];
    CHECK(leafCov < 0.6f);
    float l1Cov = 0.f; for (uint32_t j = 0; j < FL.count[1]; ++j) l1Cov += fnodes[FL.offset[1] + j].coverage / 65535.f; l1Cov /= (float)FL.count[1];
    std::printf("foliage leaf coverage avg %.3f level1 %.3f root %.3f (leaves %u)\n", leafCov, l1Cov, fnodes[0].coverage / 65535.f, fn);
    CHECK(l1Cov <= 1.f);                                                      // internal levels saturate for multi-layer volumes (documented)
    CHECK(leafCov < leafCovAvg);                                              // foliage clearly sparser than the wall
    // errors: parentError = parent's ownError, root INF (as fs_world.cpp / world_cluster_*.comp fill them)
    for (uint32_t lv = 0; lv + 1 < FL.levels; ++lv) for (uint32_t j = 0; j < FL.count[lv]; ++j) { float pe = fnodes[FL.offset[lv + 1] + j / FS_CLUSTER_FANOUT].repRadius; CHECK(pe >= fnodes[FL.offset[lv] + j].repRadius - 1e-6f); }
    // thin partition (3 cm apart, opposite normals): the sheets straddle the z = 0 page boundary, so per
    // page every leaf has a tight single-sided cone (one sign per page); re-encoded around one origin they
    // share cells and the root cone (and mixed leaves) open up -> the backface test can never reject a sheet.
    SyntheticScene tw; GenerateSynthetic(SYN_THIN_WALL, 2.f, 0.02f, 4, 0, tw);
    bool plusPage = false, minusPage = false;
    for (const SyntheticPage& pg : tw.pages) {
        std::vector<FsSurfel> ts; for (const FsSurfel& s : pg.surfels) if (std::fabs(DecodePos(s.py)) < 1.f) ts.push_back(s);
        if (ts.size() < 100) continue;
        MortonSort(ts);
        std::vector<FsClusterNode> tn; ClusterLayout TL = BuildClusterTree(ts.data(), (uint32_t)ts.size(), 0, 4096, tn, &overflow);
        for (uint32_t j = 0; j < TL.count[0]; ++j) { const FsClusterNode& lf = tn[TL.offset[0] + j]; CHECK(lf.coneCos > 0.9f); if (lf.coneAxis[2] > 0.9f) plusPage = true; if (lf.coneAxis[2] < -0.9f) minusPage = true; }
    }
    CHECK(plusPage && minusPage);
    FsPageKey common{0, 0, 0, -1}; std::vector<FsSurfel> both;
    for (const SurfelSample& smp : tw.samples) if (std::fabs(smp.n[2]) > 0.5f && smp.p[1] < 1.f) both.push_back(MakeSurfel(smp, common));
    MortonSort(both);
    std::vector<FsClusterNode> bn; ClusterLayout BL = BuildClusterTree(both.data(), (uint32_t)both.size(), 0, 4096, bn, &overflow);
    CHECK(BL.levels > 1 && bn[0].coneCos <= -0.99f);
    bool anyOpenLeaf = false; for (uint32_t j = 0; j < BL.count[0]; ++j) if (bn[BL.offset[0] + j].coneCos <= -0.99f) anyOpenLeaf = true;
    CHECK(anyOpenLeaf);
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
    const float wall = 2.0f;
    CHECK(HzbTest(wall, wall, 0.f, 0.f, FS_RENDER_MODE_SCAN) == HZB_KEEP);                 // surfel at the occluder depth survives
    CHECK(HzbTest(wall + 0.05f, wall, 0.f, 0.f, FS_RENDER_MODE_SCAN) == HZB_KEEP_IN_BAND); // within 10 cm: kept, counted
    CHECK(HzbTest(wall + 0.5f, wall, 0.f, 0.f, FS_RENDER_MODE_SCAN) == HZB_REJECT);        // 0.5 m behind the wall: culled
    CHECK(HzbTest(wall - 0.015f, wall, 0.f, 0.f, FS_RENDER_MODE_SCAN) == HZB_KEEP);        // near side of a 3 cm partition
    CHECK(HzbTest(wall + 0.015f, wall, 0.f, 0.f, FS_RENDER_MODE_SCAN) == HZB_KEEP_IN_BAND);// far side of the partition survives
    CHECK(HzbTest(wall + 0.5f, wall, 0.f, 0.f, FS_RENDER_MODE_XRAY) == HZB_KEEP);          // XRAY: no depth-prior cull at all
    CHECK(HzbTest(wall + 0.5f, kHzbNoOccluder, 0.f, 0.f, FS_RENDER_MODE_SCAN) == HZB_KEEP);// no occluder observed
    CHECK(HzbTest(wall + 0.5f, wall, 0.f, 0.25f, FS_RENDER_MODE_SCAN) == HZB_KEEP_IN_BAND);// gradient widens the band: 0.1 + 2*0.25 = 0.6
    CHECK(HzbTest(wall + 0.3f, wall, 0.35f, 0.f, FS_RENDER_MODE_SCAN) == HZB_KEEP_IN_BAND);// sigma above 10 cm widens it too
    CHECK(HzbTest(wall + 0.3f, wall, 0.25f, 0.f, FS_RENDER_MODE_SCAN) == HZB_REJECT);
    CHECK_NEAR(HzbMargin(0.f, 0.f), 0.10, 1e-6); CHECK_NEAR(HzbMargin(0.02f, 0.05f), 0.20, 1e-6);
    CHECK(HzbLevelForExtent(0.5f) == 0 && HzbLevelForExtent(2.f) == 1 && HzbLevelForExtent(300.f) == FS_HZB_LEVELS - 1);
    CHECK(HzbTotalTexels() == 87381u && HzbLevelOffset(1) == 65536u && HzbLevelSize(8) == 1u);
    CHECK(HzbCombine(1.f, 3.f) == 3.f);
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
    CHECK(sizeof(FsSlotLayout) == 32);
}

static void TestMeasRing() {
    MeasRing r; r.Init(8);
    FsSurfaceMeasurement m[10]; for (int i = 0; i < 10; ++i) { m[i] = FsSurfaceMeasurement{}; m[i].observationId = (uint32_t)i; }
    CHECK(r.Push(m, 3) == 3);                             // pre-attach staging
    std::vector<FsSurfaceMeasurement> store(8); r.Attach(store.data());
    CHECK(r.Pending() == 3);
    uint32_t base; uint64_t dropped;
    CHECK(r.Peek(16, &base, &dropped) == 3 && base == 0 && dropped == 0);
    CHECK(r.Pending() == 3); r.Advance(3); CHECK(r.Pending() == 0);
    CHECK(r.Push(m, 10) == 10);                           // overran capacity by 2 -> oldest dropped, counted
    CHECK(r.Take(16, &base, &dropped) == 8 && dropped == 2);
    CHECK(store[(base + 7) & 7].observationId == 9);
}

int main() {
    TestPageHash(); TestEncodings(); TestClusterLayout(); TestSynthetic(); TestClusterBuildAndCoverage(); TestResidency(); TestHzbBand(); TestCullMath(); TestMeasRing();
    std::printf("finalscan host world tests: %d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
