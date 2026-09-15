// Host (linux x86_64) unit tests for the C09 measurement front-end math (no Vulkan): depth linearisation,
// ray / back-projection against synthetic planes, central-difference normals, edge rejection, the
// provisional sigma model, decimation counts, workgroup ring reservation and the whole-dispatch CPU
// reference (the kernel's twin) including anchor transforms and the maxOut cap.
// HOST_TEST_SOURCES:
#include "../src/host/measure/fs_meas_math.h"
#include <cmath>
#include <cstdio>
#include <cstring>
#include <vector>

static int g_failures = 0;
#define CHECK(cond) do { if (!(cond)) { std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); ++g_failures; } } while (0)
#define CHECK_EQ(a, b) do { auto _a = (a); auto _b = (b); if (!(_a == _b)) { std::printf("FAIL %s:%d: %s == %s (got %lld vs %lld)\n", __FILE__, __LINE__, #a, #b, (long long)_a, (long long)_b); ++g_failures; } } while (0)
#define CHECK_NEAR(a, b, tol) do { double _a = (a), _b = (b); if (std::fabs(_a - _b) > (tol)) { std::printf("FAIL %s:%d: %s ~= %s (got %g vs %g)\n", __FILE__, __LINE__, #a, #b, _a, _b); ++g_failures; } } while (0)

using namespace fs::meas;

static const float kFov[4] = { -0.9f, 0.9f, 0.8f, -0.8f };      // tan(left, right, up, down), provider signs
static const uint32_t W = FS_MEAS_DEPTH_W, H = FS_MEAS_DEPTH_H;

// Projected depth of a metric z (OpenGL z01) for the two far conventions; inverse of LinearizeDepth.
static float ProjectDepth(float z, float n, float f) {
    if (f > 0.f && std::isfinite(f)) { float ndc = (f + n) / (f - n) - 2.f * f * n / ((f - n) * z); return ndc * 0.5f + 0.5f; }
    return 1.f - n / z;
}
static PushCompact DefaultPush(uint32_t layers = 1) {
    PushCompact pc; memset(&pc, 0, sizeof pc);
    pc.nearZ = 0.1f; pc.farZ = 0.f; pc.maxDepthM = (float)FS_MEAS_MAX_DEPTH_M; pc.minDepthM = (float)FS_MEAS_MIN_DEPTH_M;
    pc.layers = layers; pc.budget = 2 * W * H; pc.maxOut = FS_MEAS_GPU_RING_CAPACITY; pc.flags = 0;   // budget above every valid count: nothing rejected, maxOut is the only limit
    pc.obsId = 0xC09u; pc.frame = 7; pc.width = W; pc.height = H;
    return pc;
}
// No canonical prediction: every valid texel is NEW (predInfo.valid = 0 in DefaultBlock).
static float NoPred(uint32_t, uint32_t, uint32_t) { return 0.f; }
static uint32_t Stored(const FrameBlock& blk) { return blk.ctr[FS_MEAS_CTR_RESERVED]; }
// World -> clip with Vulkan [0,1] depth d = 1 - n/z (infinite far; identical to ProjectDepth(z, n, inf)) and the
// measured fov tangents; eye at the origin looking +Z. Column-major.
static void PredProjection(float n, float out[16], float inv[16]) {
    float l = kFov[0], r = kFov[1], u = kFov[2], d = kFov[3];
    Mat4 P; memset(P.m, 0, sizeof P.m);
    P.m[0] = 2.f / (r - l); P.m[8] = -(r + l) / (r - l);
    P.m[5] = 2.f / (u - d); P.m[9] = -(u + d) / (u - d);
    P.m[10] = 1.f; P.m[14] = -n;                      // clip z = z - n
    P.m[11] = 1.f;                                    // clip w = z
    Mat4 I; bool ok = Invert(P, I); CHECK(ok);
    memcpy(out, P.m, 64); memcpy(inv, I.m, 64);
}
static FrameBlock DefaultBlock() {
    FrameBlock b; memset(&b, 0, sizeof b);
    Mat4 I = Identity();
    for (int e = 0; e < 2; ++e) { memcpy(b.anchorFromEye[e], I.m, 64); memcpy(b.fov[e], kFov, 16); }
    return b;
}
// single-layer depth function adapter for the dispatch reference
template <class F> struct OneLayer { const F& f; float operator()(uint32_t, uint32_t x, uint32_t y) const { return f(x, y); } };
template <class F> OneLayer<F> Layer0(const F& f) { return OneLayer<F>{f}; }
// Depth image of a plane n.p = dist (eye space, n facing the camera has n.z < 0): z = dist / (n.x tx + n.y ty + n.z).
struct PlaneImage {
    float n[3]; float dist; float nearZ, farZ; uint32_t flags; std::vector<float> tex;
    PlaneImage(const float nn[3], float d, float nz, float fz, uint32_t fl) : dist(d), nearZ(nz), farZ(fz), flags(fl), tex(W * H, 0.f) {
        memcpy(n, nn, 12);
        for (uint32_t y = 0; y < H; ++y) for (uint32_t x = 0; x < W; ++x) {
            float tx, ty; RayTangents(x, y, W, H, kFov, flags, tx, ty);
            float den = n[0] * tx + n[1] * ty + n[2];
            float z = den < -1e-6f ? dist / den : -1.f;                 // plane in front of the camera only
            if (z <= 0.f) { tex[y * W + x] = 0.f; continue; }
            tex[y * W + x] = (flags & FS_MEAS_FLAG_LINEAR_DEPTH) ? z : ProjectDepth(z, nearZ, farZ);
        }
    }
    float operator()(uint32_t x, uint32_t y) const { return tex[y * W + x]; }
};

static void TestLinearizeDepth() {
    // infinite far: z01 = 1 - n/z
    for (float z : { 0.2f, 0.5f, 1.f, 2.5f, 5.9f }) {
        CHECK_NEAR(LinearizeDepth(ProjectDepth(z, 0.1f, 0.f), 0.1f, 0.f, 0), z, 1e-3 * z);
        CHECK_NEAR(LinearizeDepth(ProjectDepth(z, 0.1f, 10.f), 0.1f, 10.f, 0), z, 2e-3 * z);
        CHECK_NEAR(LinearizeDepth(ProjectDepth(z, 0.1f, INFINITY), 0.1f, INFINITY, 0), z, 1e-3 * z);   // inf far == unbounded
    }
    CHECK(LinearizeDepth(0.f, 0.1f, 0.f, 0) < 0.f);                       // no data
    CHECK(LinearizeDepth(1.f, 0.1f, 0.f, 0) < 0.f);                       // at/beyond far
    CHECK(LinearizeDepth(NAN, 0.1f, 0.f, 0) < 0.f);
    CHECK_NEAR(LinearizeDepth(3.25f, 0.1f, 0.f, FS_MEAS_FLAG_LINEAR_DEPTH), 3.25f, 1e-6);
    CHECK(!DepthUsable(0.1f, 0.1f, 6.f)); CHECK(DepthUsable(0.11f, 0.1f, 6.f)); CHECK(!DepthUsable(6.01f, 0.1f, 6.f)); CHECK(!DepthUsable(NAN, 0.1f, 6.f));
    CHECK(DepthUsable(6.f, 0.1f, 6.f));
}

static void TestRays() {
    float tx, ty;
    RayTangents(0, 0, W, H, kFov, 0, tx, ty);                              // first texel: near left / top
    CHECK_NEAR(tx, kFov[0] + (kFov[1] - kFov[0]) * 0.5f / W, 1e-6); CHECK_NEAR(ty, kFov[2] + (kFov[3] - kFov[2]) * 0.5f / H, 1e-6);
    RayTangents(W - 1, H - 1, W, H, kFov, 0, tx, ty); CHECK(tx > 0.89f && ty < -0.79f);
    RayTangents(0, 0, W, H, kFov, FS_MEAS_FLAG_FLIP_Y, tx, ty); CHECK(ty < -0.79f);    // flipped: row 0 = bottom
    // image centre looks straight ahead
    RayTangents(W / 2, H / 2, W, H, kFov, 0, tx, ty); CHECK_NEAR(tx, (kFov[1] - kFov[0]) * 0.5f / W, 1e-6); CHECK_NEAR(ty, (kFov[3] - kFov[2]) * 0.5f / H, 1e-6);
    Vec3 p = EyePoint(0.5f, -0.25f, 2.f); CHECK_NEAR(p.x, 1.f, 1e-6); CHECK_NEAR(p.y, -0.5f, 1e-6); CHECK_NEAR(p.z, 2.f, 1e-6);
    CHECK_NEAR(Footprint(2.f, kFov, W, H), 2.f * 1.8f / W, 1e-6);           // horizontal texel angle is the larger one here
}

// Non-negotiable regression (C09R-E1): the Environment Depth image convention is row 0 = bottom (tanDown). With the
// default flags and the measured Quest 3S fov tangents, texel y = 0 must look DOWN and y = h - 1 UP; x = 0 left.
static void TestDepthImageConvention() {
    const float fov[4] = {-1.15f, 1.00f, 1.11f, -1.19f};
    const uint32_t W = 320, H = 320, flags = FS_MEAS_FLAG_FLIP_Y;    // FS_MEAS_FLAG_FLIP_Y is the Measure default
    float tx, ty;
    RayTangents(0, 0, W, H, fov, flags, tx, ty);         CHECK_NEAR(ty, -1.19f + (1.11f + 1.19f) * 0.5f / H, 1e-5); CHECK_NEAR(tx, -1.15f + 2.15f * 0.5f / W, 1e-5);
    RayTangents(0, H - 1, W, H, fov, flags, tx, ty);     CHECK_NEAR(ty, 1.11f - (1.11f + 1.19f) * 0.5f / H, 1e-5);
    RayTangents(W - 1, 0, W, H, fov, flags, tx, ty);     CHECK_NEAR(tx, 1.00f - 2.15f * 0.5f / W, 1e-5);
    RayTangents(0, 0, W, H, fov, 0, tx, ty);             CHECK(ty > 1.0f);                                            // the legacy "row 0 = top" reading is the mirrored one
}

static void TestSigmaModel() {
    CHECK_NEAR(SigmaN(0.f), 0.01, 1e-7);
    CHECK_NEAR(SigmaN(1.f), 0.03, 1e-7);
    CHECK_NEAR(SigmaN(2.f), 0.09, 1e-6);
    CHECK_NEAR(SigmaN(3.f), 0.19, 1e-6);
    CHECK(SigmaN(6.f) > SigmaN(5.f));
}

static void TestEdgeAndFlat() {
    CHECK(!IsEdge(2.f, 2.05f, 1.95f, 2.1f, 1.9f));        // 5 % steps: no edge
    CHECK(IsEdge(2.f, 2.25f, 2.f, 2.f, 2.f));             // 12.5 % step on one side: edge
    CHECK(IsEdge(2.f, 2.f, 2.f, 2.f, 1.7f));
    CHECK(IsFlat(2.f, 2.001f, 1.999f, 2.002f, 1.998f));   // laplacian 0
    CHECK(IsFlat(2.f, 2.004f, 2.004f, 2.f, 2.f));         // laplacian 0.008 < 0.01
    CHECK(!IsFlat(2.f, 2.01f, 2.01f, 2.f, 2.f));          // laplacian 0.02 > 0.01
}

static void TestNormals() {
    // fronto-parallel plane: every point has the same z -> normal (0,0,-1) (facing the camera at the origin)
    Vec3 p = V3(0.f, 0.f, 2.f);
    Vec3 n = NormalFromNeighbours(p, V3(-0.01f, 0.f, 2.f), V3(0.01f, 0.f, 2.f), V3(0.f, 0.01f, 2.f), V3(0.f, -0.01f, 2.f));
    CHECK_NEAR(n.x, 0, 1e-6); CHECK_NEAR(n.y, 0, 1e-6); CHECK_NEAR(n.z, -1, 1e-6);
    // swapped neighbour order flips the cross product; the orientation rule keeps it camera-facing
    n = NormalFromNeighbours(p, V3(0.01f, 0.f, 2.f), V3(-0.01f, 0.f, 2.f), V3(0.f, 0.01f, 2.f), V3(0.f, -0.01f, 2.f));
    CHECK_NEAR(n.z, -1, 1e-6);
    // 45 degree wall: x + z = 3  -> normal ∝ (-1, 0, -1)
    n = NormalFromNeighbours(V3(1.f, 0.f, 2.f), V3(0.9f, 0.f, 2.1f), V3(1.1f, 0.f, 1.9f), V3(1.f, 0.1f, 2.f), V3(1.f, -0.1f, 2.f));
    CHECK_NEAR(n.x, -0.7071, 1e-4); CHECK_NEAR(n.y, 0, 1e-6); CHECK_NEAR(n.z, -0.7071, 1e-4);
    // degenerate neighbours -> zero normal, not NaN
    n = NormalFromNeighbours(p, p, p, p, p); CHECK(n.x == 0.f && n.y == 0.f && n.z == 0.f);
}

static void TestReservation() {
    uint32_t reserved = 0, overflow = 0;
    CHECK_EQ(ReserveGroup(reserved, 10, 100, overflow), 0u); CHECK_EQ(reserved, 10u); CHECK_EQ(overflow, 0u);
    CHECK_EQ(ReserveGroup(reserved, 64, 100, overflow), 10u); CHECK_EQ(reserved, 74u); CHECK_EQ(overflow, 0u);
    CHECK_EQ(ReserveGroup(reserved, 64, 100, overflow), 74u); CHECK_EQ(reserved, 138u); CHECK_EQ(overflow, 38u);   // straddles the cap
    CHECK_EQ(ReserveGroup(reserved, 64, 100, overflow), 138u); CHECK_EQ(overflow, 102u);                              // fully beyond
    CHECK_EQ(StoredCount(reserved, 100), 100u); CHECK_EQ(StoredCount(50, 100), 50u);
    CHECK_EQ(reserved - overflow, StoredCount(reserved, 100));                                                         // identity: stored == reserved - overflow
    CHECK_EQ(Overflow(0, 0, 100), 0u); CHECK_EQ(Overflow(100, 5, 100), 5u); CHECK_EQ(Overflow(98, 5, 100), 3u);
}

static void TestFrontoParallelPlane() {
    const float n[3] = { 0.f, 0.f, -1.f };
    PlaneImage img(n, -2.f, 0.1f, 0.f, 0);                 // z = 2 m everywhere
    PushCompact pc = DefaultPush(); FrameBlock blk = DefaultBlock(); uint32_t* ctr = blk.ctr;
    std::vector<FsSurfaceMeasurement> rec(FS_MEAS_GPU_RING_CAPACITY);
    CompactDispatchReference(pc, blk, Layer0(img), NoPred, rec.data());
    const uint32_t interior = (W - 2) * (H - 2);
    CHECK_EQ(ctr[FS_MEAS_CTR_VALID], interior);              // every interior texel is usable
    CHECK_EQ(ctr[FS_MEAS_CTR_INVALID], W * H - interior);    // the border ring
    CHECK_EQ(ctr[FS_MEAS_CTR_EDGE], 0u); CHECK_EQ(ctr[FS_MEAS_CTR_NEW], interior); CHECK_EQ(ctr[FS_MEAS_CTR_PREDICTED], 0u);
    CHECK_EQ(ctr[FS_MEAS_CTR_RESERVED], (uint32_t)FS_MEAS_GPU_RING_CAPACITY);   // one undecimated eye exceeds a ring slot: the cap holds
    CHECK_EQ(ctr[FS_MEAS_CTR_OVERFLOW], interior - FS_MEAS_GPU_RING_CAPACITY);
    CHECK_EQ(ctr[FS_MEAS_CTR_REJECTED], 0u); CHECK_EQ(ctr[FS_MEAS_CTR_THRESHOLD], 1u);
    CHECK_EQ(ctr[FS_MEAS_CTR_LOWTEX], ctr[FS_MEAS_CTR_RESERVED]);   // a plane is flat everywhere
    CHECK_EQ(ctr[FS_MEAS_CTR_GROUPS], (W * H + FS_MEAS_WG - 1) / FS_MEAS_WG);
    const uint32_t stored = Stored(blk);
    double maxErr = 0; bool flagsOk = true;
    for (uint32_t i = 0; i < stored; ++i) {
        const FsSurfaceMeasurement& m = rec[i];
        maxErr = std::fmax(maxErr, std::fabs(m.pz - 2.0)); maxErr = std::fmax(maxErr, std::fabs(m.nz + 1.0)); maxErr = std::fmax(maxErr, std::fabs(m.nx)); maxErr = std::fmax(maxErr, std::fabs(m.ny));
        if ((m.sourceFlags & 0xFFFFu) != (FS_MEAS_SRC_DEPTH_PRIOR | FS_MEAS_SRC_LOW_TEXTURE) || m.observationId != 0xC09u || m.reserved != 0) flagsOk = false;
        if (std::fabs(m.sigmaN - SigmaN(2.f)) > 1e-4f || std::fabs(m.footprint - Footprint(2.f, kFov, W, H)) > 1e-5f || m.sigmaT != m.footprint) flagsOk = false;
    }
    CHECK(maxErr < 2e-3);                                    // float depth round trip through the projection
    CHECK(flagsOk);
    // x/y of the records span the fov at 2 m
    float xmin = 1e9f, xmax = -1e9f, ymin = 1e9f, ymax = -1e9f;
    for (uint32_t i = 0; i < stored; ++i) { xmin = std::fmin(xmin, rec[i].px); xmax = std::fmax(xmax, rec[i].px); ymin = std::fmin(ymin, rec[i].py); ymax = std::fmax(ymax, rec[i].py); }
    CHECK(xmin < -1.75f && xmax > 1.75f);                    // the stored band (65 % of the rows, rotated by frame) spans the full width
    CHECK(ymax - ymin > 1.9f && ymax - ymin < 2.2f);
}

static void TestTiltedPlaneNormalsAndFinitFar() {
    const float n[3] = { -0.6f, 0.f, -0.8f };               // wall turned 37 degrees, facing the camera
    PlaneImage img(n, -2.4f, 0.1f, 8.f, 0);                  // finite far plane convention
    PushCompact pc = DefaultPush(); pc.farZ = 8.f; FrameBlock blk = DefaultBlock(); uint32_t* ctr = blk.ctr;
    std::vector<FsSurfaceMeasurement> rec(FS_MEAS_GPU_RING_CAPACITY);
    CompactDispatchReference(pc, blk, Layer0(img), NoPred, rec.data());
    CHECK(ctr[FS_MEAS_CTR_RESERVED] > 40000u);
    CHECK_EQ(ctr[FS_MEAS_CTR_EDGE], 0u);
    double maxNormalErr = 0, maxPlaneErr = 0;
    for (uint32_t i = 0; i < Stored(blk); ++i) {
        const FsSurfaceMeasurement& m = rec[i];
        maxNormalErr = std::fmax(maxNormalErr, std::fabs(m.nx - n[0]) + std::fabs(m.ny - n[1]) + std::fabs(m.nz - n[2]));
        maxPlaneErr = std::fmax(maxPlaneErr, std::fabs(n[0] * m.px + n[1] * m.py + n[2] * m.pz + 2.4));
    }
    CHECK(maxNormalErr < 0.02);                              // central differences on a plane recover the exact normal
    CHECK(maxPlaneErr < 0.01);                               // points lie on the plane (finite-far round trip)
}

static void TestEdgeRejectionAndDetail() {
    // two fronto-parallel planes: left half at 2 m, right half at 3 m -> one column of edge texels on each side
    std::vector<float> tex(W * H);
    for (uint32_t y = 0; y < H; ++y) for (uint32_t x = 0; x < W; ++x) tex[y * W + x] = ProjectDepth(x < W / 2 ? 2.f : 3.f, 0.1f, 0.f);
    auto depth = [&](uint32_t x, uint32_t y) { return tex[y * W + x]; };
    PushCompact pc = DefaultPush(); FrameBlock blk = DefaultBlock(); uint32_t* ctr = blk.ctr;
    std::vector<FsSurfaceMeasurement> rec(FS_MEAS_GPU_RING_CAPACITY);
    CompactDispatchReference(pc, blk, Layer0(depth), NoPred, rec.data());
    const uint32_t interior = (W - 2) * (H - 2);
    CHECK_EQ(ctr[FS_MEAS_CTR_EDGE], 2 * (H - 2));            // columns W/2-1 and W/2, interior rows
    CHECK_EQ(ctr[FS_MEAS_CTR_RESERVED] + ctr[FS_MEAS_CTR_OVERFLOW], interior - 2 * (H - 2));
    for (uint32_t i = 0; i < Stored(blk); ++i) CHECK((rec[i].sourceFlags & FS_MEAS_SRC_EDGE) == 0u);
    // DETAIL keeps the edge texels, flagged
    pc.flags |= FS_MEAS_FLAG_DETAIL; memset(ctr, 0, sizeof blk.ctr);
    CompactDispatchReference(pc, blk, Layer0(depth), NoPred, rec.data());
    CHECK_EQ(ctr[FS_MEAS_CTR_RESERVED] + ctr[FS_MEAS_CTR_OVERFLOW], interior);
    uint32_t flagged = 0; for (uint32_t i = 0; i < Stored(blk); ++i) if (rec[i].sourceFlags & FS_MEAS_SRC_EDGE) ++flagged;
    CHECK(flagged > 0 && flagged <= 2 * (H - 2));            // the stored prefix holds a part of the two edge columns
    // holes: a texel next to "no data" is invalid, never an edge
    tex[10 * W + 10] = 0.f; pc.flags = 0; memset(ctr, 0, sizeof blk.ctr);
    CompactDispatchReference(pc, blk, Layer0(depth), NoPred, rec.data());
    CHECK_EQ(ctr[FS_MEAS_CTR_INVALID], W * H - interior + 5);   // the hole and its 4 neighbours
    // per-texel API on the discontinuity
    FsSurfaceMeasurement m; bool edge = false;
    CHECK_EQ((int)BackprojectTexel(pc, blk, 0, W / 2 - 1, H / 2, depth, m, edge), (int)TEXEL_EDGE); CHECK(edge);
    CHECK_EQ((int)BackprojectTexel(pc, blk, 0, 5, H / 2, depth, m, edge), (int)TEXEL_WRITTEN); CHECK(!edge);
    CHECK_EQ((int)BackprojectTexel(pc, blk, 0, 0, H / 2, depth, m, edge), (int)TEXEL_INVALID);
    CHECK_EQ((int)BackprojectTexel(pc, blk, 0, W, H / 2, depth, m, edge), (int)TEXEL_INVALID);
}

static void TestBudgetSelection() {
    const float n[3] = { 0.f, 0.f, -1.f };
    PlaneImage img(n, -1.5f, 0.1f, 0.f, 0);
    const uint32_t interior = (W - 2) * (H - 2);
    // budget above the valid count: everything is kept, threshold 1, nothing rejected
    PushCompact pc = DefaultPush(); pc.budget = interior + 5; pc.maxOut = FS_MEAS_GPU_RING_CAPACITY; pc.frame = 3; FrameBlock blk = DefaultBlock(); uint32_t* ctr = blk.ctr;
    std::vector<FsSurfaceMeasurement> rec(FS_MEAS_GPU_RING_CAPACITY + 1024);
    CompactDispatchReference(pc, blk, Layer0(img), NoPred, rec.data());
    CHECK_EQ(ctr[FS_MEAS_CTR_RESERVED], (uint32_t)FS_MEAS_GPU_RING_CAPACITY); CHECK_EQ(ctr[FS_MEAS_CTR_THRESHOLD], 1u); CHECK_EQ(ctr[FS_MEAS_CTR_FRACTION], 65536u);
    CHECK_EQ(ctr[FS_MEAS_CTR_OVERFLOW], interior - FS_MEAS_GPU_RING_CAPACITY); CHECK_EQ(ctr[FS_MEAS_CTR_REJECTED], 0u);   // maxOut is the only limit
    // budget 10000 out of 101k equal-score texels: the hash fraction keeps ~10000 (binomial: +-3 sigma = 300), records beyond stay untouched
    pc.budget = 10000; pc.maxOut = 20000; memset(ctr, 0, sizeof blk.ctr);
    for (auto& r : rec) r.observationId = 0xDEAD;
    CompactDispatchReference(pc, blk, Layer0(img), NoPred, rec.data());
    const uint32_t stored = Stored(blk);
    CHECK(stored >= 9700 && stored <= 10300); CHECK_EQ(ctr[FS_MEAS_CTR_OVERFLOW], 0u); CHECK_EQ(ctr[FS_MEAS_CTR_REJECTED], interior - stored);
    bool written = true; for (uint32_t i = 0; i < stored; ++i) if (rec[i].observationId != 0xC09u) written = false;
    CHECK(written); CHECK_EQ(rec[stored].observationId, 0xDEADu);
    // deterministic: the same inputs reproduce every byte of the record stream and the counters
    std::vector<FsSurfaceMeasurement> rec2(FS_MEAS_GPU_RING_CAPACITY + 1024); FrameBlock blk2 = DefaultBlock();
    CompactDispatchReference(pc, blk2, Layer0(img), NoPred, rec2.data());
    CHECK(memcmp(rec.data(), rec2.data(), stored * sizeof(FsSurfaceMeasurement)) == 0); CHECK(memcmp(blk.ctr, blk2.ctr, sizeof blk.ctr) == 0);
    // another frame index moves the tie-break lattice: a different subset, similar count
    PushCompact pc3 = pc; pc3.frame = 4; FrameBlock blk3 = DefaultBlock(); std::vector<FsSurfaceMeasurement> rec3(FS_MEAS_GPU_RING_CAPACITY + 1024);
    CompactDispatchReference(pc3, blk3, Layer0(img), NoPred, rec3.data());
    CHECK(Stored(blk3) >= 9700 && Stored(blk3) <= 10300); CHECK(memcmp(rec.data(), rec3.data(), 9000 * sizeof(FsSurfaceMeasurement)) != 0);
    // hard cap: a budget above maxOut is clamped by the caller; the reference counts the excess as overflow
    pc.budget = 30000; pc.maxOut = 20000; memset(ctr, 0, sizeof blk.ctr);
    CompactDispatchReference(pc, blk, Layer0(img), NoPred, rec.data());
    CHECK_EQ(Stored(blk), 20000u); CHECK(ctr[FS_MEAS_CTR_OVERFLOW] >= 9700 && ctr[FS_MEAS_CTR_OVERFLOW] <= 10300);
    // both eyes in one dispatch (the device configuration): equal scores -> the budget is shared evenly between the eyes
    PushCompact pc2 = DefaultPush(2); pc2.budget = FS_MEAS_DEFAULT_BUDGET; pc2.maxOut = FS_MEAS_DEFAULT_MAX_OUT; pc2.frame = 11;
    memset(ctr, 0, sizeof blk.ctr);
    auto twoEyes = [&](uint32_t layer, uint32_t x, uint32_t y) { return layer == 0 ? img(x, y) : ProjectDepth(2.5f, 0.1f, 0.f); };
    CompactDispatchReference(pc2, blk, twoEyes, NoPred, rec.data());
    const uint32_t stored2 = Stored(blk);
    CHECK(stored2 >= FS_MEAS_DEFAULT_BUDGET - 400 && stored2 <= FS_MEAS_DEFAULT_BUDGET + 400); CHECK_EQ(ctr[FS_MEAS_CTR_OVERFLOW], 0u);
    CHECK_EQ(ctr[FS_MEAS_CTR_GROUPS], (W * H * 2 + FS_MEAS_WG - 1) / FS_MEAS_WG);
    uint32_t eyeR = 0; for (uint32_t i = 0; i < stored2; ++i) if (std::fabs(rec[i].pz - 2.5f) < 0.01f) ++eyeR;
    CHECK(eyeR > stored2 / 2 - 400 && eyeR < stored2 / 2 + 400);
    // the rotating row phase: two consecutive frames map thread 0 to different rows
    uint32_t layer, x, y0, y1;
    ThreadToTexel(0, 2, W, H, 11, layer, x, y0); ThreadToTexel(0, 2, W, H, 12, layer, x, y1);
    CHECK_EQ(y0, RowPhase(11, H)); CHECK_EQ(y1, RowPhase(12, H)); CHECK(y0 != y1);
    ThreadToTexel(1, 2, W, H, 11, layer, x, y0); CHECK_EQ(layer, 1u); CHECK_EQ(x, 0u);
    ThreadToTexel(2 * W * H - 1, 2, W, H, 0, layer, x, y0); CHECK_EQ(layer, 1u); CHECK_EQ(x, W - 1); CHECK_EQ(y0, H - 1);
}

// C09R-E2 information score and the canonical prediction (contract §7.6): a converged flat plane is low value, a
// new area / a surface off the prediction / curvature / steepness / DETAIL are high value; the histogram threshold
// spends the budget on the high-value texels first.
static void TestScoreAndPrediction() {
    CHECK_EQ(ScoreOf(false, 0.f, true, false, false), (uint32_t)FS_MEAS_SCORE_NEW);
    CHECK_EQ(ScoreOf(true, 0.f, true, false, false), (uint32_t)FS_MEAS_SCORE_CONVERGED_BASE);
    CHECK_EQ(ScoreOf(true, 0.5f, true, false, false), (uint32_t)FS_MEAS_SCORE_CONVERGED_BASE + 16);
    CHECK_EQ(ScoreOf(true, 2.f, true, false, false), (uint32_t)FS_MEAS_SCORE_BAND_BASE + 48);
    CHECK_EQ(ScoreOf(true, 5.f, true, false, false), (uint32_t)FS_MEAS_SCORE_FAR_BASE + 20);
    CHECK_EQ(ScoreOf(true, 100.f, true, false, false), 255u);
    CHECK_EQ(ScoreOf(true, 0.f, false, true, false), (uint32_t)(FS_MEAS_SCORE_CONVERGED_BASE + FS_MEAS_SCORE_CURVATURE + FS_MEAS_SCORE_STEEP));
    CHECK_EQ(ScoreOf(true, 0.f, true, false, true), (uint32_t)FS_MEAS_SCORE_DETAIL_MIN);
    CHECK(ScoreOf(false, 0.f, false, true, false) <= 255u);
    // threshold: hist with 100 @ 200, 300 @ 100, 1000 @ 20; budget 250 -> T = 100, fraction (250-100)/300
    uint32_t hist[FS_MEAS_SCORE_BINS] = {}; hist[200] = 100; hist[100] = 300; hist[20] = 1000;
    uint32_t T = 0, f = 0; SelectThreshold(hist, 250, T, f); CHECK_EQ(T, 100u); CHECK_EQ(f, (uint32_t)(((uint64_t)150 << 16) / 300));
    SelectThreshold(hist, 50, T, f); CHECK_EQ(T, 200u); CHECK_EQ(f, (uint32_t)(((uint64_t)50 << 16) / 100));
    SelectThreshold(hist, 5000, T, f); CHECK_EQ(T, 1u); CHECK_EQ(f, 65536u);                    // everything fits
    SelectThreshold(hist, 1400, T, f); CHECK_EQ(T, 20u); CHECK_EQ(f, 65536u);                   // exact fit of the lowest bin
    CHECK(Selected(201, 5, 1, 100, f) && !Selected(99, 5, 1, 100, f) && !Selected(0, 5, 1, 1, 65536));
    uint32_t kept = 0; for (uint32_t t = 0; t < 10000; ++t) if (Selected(100, t, 3, 100, 32768)) ++kept;
    CHECK(kept > 4700 && kept < 5300);                                                            // half of the tie bin, deterministic
    CHECK_EQ(HashTexel(123, 7), HashTexel(123, 7)); CHECK(HashTexel(123, 7) != HashTexel(124, 7)); CHECK(HashTexel(123, 7) != HashTexel(123, 8));
    // prediction: the rendered depth of the same plane (identity eye pose) -> residual 0 -> converged, lowest score
    const float n[3] = { 0.f, 0.f, -1.f };
    PlaneImage img(n, -2.f, 0.1f, 0.f, 0);
    FrameBlock blk = DefaultBlock();
    for (int e = 0; e < 2; ++e) { PredProjection(0.1f, blk.predViewProj[e], blk.predInvViewProj[e]); memcpy(blk.worldFromEye[e], Identity().m, 64); }
    blk.predInfo[0] = W; blk.predInfo[1] = H; blk.predInfo[2] = 2; blk.predInfo[3] = 1;
    PushCompact pc = DefaultPush();
    auto predSame = [&](uint32_t, uint32_t, uint32_t) { return ProjectDepth(2.f, 0.1f, 0.f); };
    auto predFar  = [&](uint32_t, uint32_t, uint32_t) { return ProjectDepth(2.5f, 0.1f, 0.f); };
    bool predicted = false, consistent = false;
    uint32_t sSame = ScoreTexel(pc, blk, 0, W / 2, H / 2, img, predSame, predicted, consistent);
    CHECK(predicted && consistent); CHECK_EQ(sSame, (uint32_t)FS_MEAS_SCORE_CONVERGED_BASE);
    uint32_t sFar = ScoreTexel(pc, blk, 0, W / 2, H / 2, img, predFar, predicted, consistent);
    CHECK(predicted && !consistent); CHECK(sFar >= (uint32_t)FS_MEAS_SCORE_FAR_BASE);            // 0.5 m off at sigma(2 m) = 9 cm: > 3 sigma
    uint32_t sNone = ScoreTexel(pc, blk, 0, W / 2, H / 2, img, NoPred, predicted, consistent);   // nothing rendered there -> NEW
    CHECK(!predicted); CHECK_EQ(sNone, (uint32_t)FS_MEAS_SCORE_NEW);
    // the residual is measured along the eye ray at the projected texel: an oblique texel of the same plane stays consistent
    uint32_t sCorner = ScoreTexel(pc, blk, 0, 10, 10, img, predSame, predicted, consistent);
    CHECK(predicted && consistent); CHECK(sCorner <= (uint32_t)FS_MEAS_SCORE_CONVERGED_BASE + 8);
    // whole job with the prediction: with a new region (no prediction in the right half) the budget goes to the new region first
    std::vector<FsSurfaceMeasurement> rec(FS_MEAS_GPU_RING_CAPACITY + 1024);
    pc.budget = 10000; pc.maxOut = 20000;
    auto predHalf = [&](uint32_t, uint32_t px, uint32_t) { return px < W / 2 ? ProjectDepth(2.f, 0.1f, 0.f) : 0.f; };
    std::vector<uint32_t> scores(W * H);
    CompactDispatchReference(pc, blk, Layer0(img), predHalf, rec.data(), scores.data());
    uint32_t* ctr = blk.ctr;
    CHECK(ctr[FS_MEAS_CTR_PREDICTED] > 40000 && ctr[FS_MEAS_CTR_NEW] > 40000); CHECK_EQ(ctr[FS_MEAS_CTR_CONSISTENT], ctr[FS_MEAS_CTR_PREDICTED]);
    CHECK_EQ(ctr[FS_MEAS_CTR_THRESHOLD], (uint32_t)FS_MEAS_SCORE_NEW);                           // the budget never reaches the converged half
    uint32_t stored = Stored(blk); CHECK(stored >= 9700 && stored <= 10300);
    uint32_t leftHalf = 0; for (uint32_t i = 0; i < stored; ++i) if (rec[i].px < 0.f) ++leftHalf;   // eye x < 0 <- left texels (tan left negative)
    CHECK_EQ(leftHalf, 0u);                                                                       // every selected record is in the new (right) half
}

static void TestAnchorTransform() {
    // eye pose: translated (1, 2, 3) and rotated 90 degrees about +Y (Unity: +Z forward -> +X)
    Mat4 worldFromEye = Identity();
    worldFromEye.m[0] = 0.f; worldFromEye.m[2] = -1.f; worldFromEye.m[8] = 1.f; worldFromEye.m[10] = 0.f;   // columns: X->(0,0,-1), Z->(1,0,0)
    worldFromEye.m[12] = 1.f; worldFromEye.m[13] = 2.f; worldFromEye.m[14] = 3.f;
    Mat4 worldFromAnchor = Identity(); worldFromAnchor.m[12] = 10.f; worldFromAnchor.m[13] = -1.f; worldFromAnchor.m[14] = 0.5f;
    Mat4 anchorFromWorld; CHECK(Invert(worldFromAnchor, anchorFromWorld));
    Mat4 anchorFromEye = Mul(anchorFromWorld, worldFromEye);
    Mat4 back; CHECK(Invert(anchorFromEye, back)); Mat4 I = Mul(anchorFromEye, back);
    for (int i = 0; i < 16; ++i) CHECK_NEAR(I.m[i], (i % 5 == 0) ? 1.f : 0.f, 1e-5);
    const float n[3] = { 0.f, 0.f, -1.f };
    PlaneImage img(n, -2.f, 0.1f, 0.f, 0);
    PushCompact pc = DefaultPush(); FrameBlock blk = DefaultBlock(); memcpy(blk.anchorFromEye[1], anchorFromEye.m, 64);
    FsSurfaceMeasurement m; bool edge;
    CHECK_EQ((int)BackprojectTexel(pc, blk, 1, W / 2, H / 2, img, m, edge), (int)TEXEL_WRITTEN);   // eye 1 uses matrix 1
    // eye point ~(0,0,2) -> world (1,2,3) + 2 * (1,0,0) = (3,2,3) -> anchor-local (-7, 3, 2.5)
    CHECK_NEAR(m.px, -7.f, 0.01); CHECK_NEAR(m.py, 3.f, 0.01); CHECK_NEAR(m.pz, 2.5f, 0.01);
    // normal (0,0,-1) in eye space -> world (-1,0,0) (points back at the eye)
    CHECK_NEAR(m.nx, -1.f, 1e-4); CHECK_NEAR(m.ny, 0.f, 1e-4); CHECK_NEAR(m.nz, 0.f, 1e-4);
    Mat4 singular; memset(singular.m, 0, sizeof singular.m); Mat4 out; CHECK(!Invert(singular, out));
}

static void TestFlipY() {
    // a floor-like plane (y = -1.5) fills the lower half of the image when row 0 is the top, and the upper half when flipped
    const float n[3] = { 0.f, 1.f, 0.f };                    // facing up: seen from above when the eye is at the origin (y = 0 > -1.5)
    PlaneImage top(n, -1.5f, 0.1f, 0.f, 0), flipped(n, -1.5f, 0.1f, 0.f, FS_MEAS_FLAG_FLIP_Y);
    uint32_t topLower = 0, topUpper = 0, flLower = 0, flUpper = 0;
    for (uint32_t y = 0; y < H; ++y) for (uint32_t x = 0; x < W; ++x) {
        if (top(x, y) > 0.f) { if (y >= H / 2) ++topLower; else ++topUpper; }
        if (flipped(x, y) > 0.f) { if (y >= H / 2) ++flLower; else ++flUpper; }
    }
    CHECK(topLower > 0 && topUpper == 0 && flUpper > 0 && flLower == 0);
    PushCompact pc = DefaultPush(); pc.flags = FS_MEAS_FLAG_FLIP_Y; FrameBlock blk = DefaultBlock();
    FsSurfaceMeasurement m; bool edge;
    CHECK_EQ((int)BackprojectTexel(pc, blk, 0, W / 2, H / 4, flipped, m, edge), (int)TEXEL_WRITTEN);
    CHECK_NEAR(m.py, -1.5f, 0.01); CHECK_NEAR(m.ny, 1.f, 1e-3);    // camera-facing normal of a floor below the eye points up
}

static void TestPushLayout() {
    CHECK_EQ(sizeof(PushCompact), 48u);
    CHECK_EQ(offsetof(PushCompact, layers), 16u); CHECK_EQ(offsetof(PushCompact, budget), 20u); CHECK_EQ(offsetof(PushCompact, obsId), 32u);
    CHECK_EQ(sizeof(FrameBlock), (size_t)FS_MEAS_FB_BYTES); CHECK_EQ(offsetof(FrameBlock, anchorFromEye), 64u); CHECK_EQ(offsetof(FrameBlock, fov), 192u);
    CHECK_EQ(offsetof(FrameBlock, worldFromEye), 224u); CHECK_EQ(offsetof(FrameBlock, predViewProj), 352u); CHECK_EQ(offsetof(FrameBlock, predInvViewProj), 480u); CHECK_EQ(offsetof(FrameBlock, predInfo), 608u);
    CHECK((size_t)FS_MEAS_SEL_WORDS * 4 < 65536); CHECK(FS_MEAS_DEFAULT_BUDGET <= FS_MEAS_DEFAULT_MAX_OUT);
    CHECK_EQ(sizeof(FsSurfaceMeasurement), 48u);
    CHECK_EQ((uint32_t)FS_MEAS_SRC_DEPTH_PRIOR, 1u << 2); CHECK_EQ((uint32_t)FS_MEAS_SRC_EDGE, 1u << 8); CHECK_EQ((uint32_t)FS_MEAS_SRC_LOW_TEXTURE, 1u << 9);
    CHECK((FS_MEAS_GPU_RING_CAPACITY & (FS_MEAS_GPU_RING_CAPACITY - 1)) == 0);
    CHECK(FS_MEAS_DEFAULT_MAX_OUT <= FS_MEAS_GPU_RING_CAPACITY);
}

int main() {
    TestPushLayout(); TestLinearizeDepth(); TestRays(); TestDepthImageConvention(); TestSigmaModel(); TestEdgeAndFlat(); TestNormals(); TestReservation();
    TestFrontoParallelPlane(); TestTiltedPlaneNormalsAndFinitFar(); TestEdgeRejectionAndDetail(); TestBudgetSelection(); TestScoreAndPrediction(); TestAnchorTransform(); TestFlipY();
    if (g_failures) { std::printf("host_tests_measure: %d failure(s)\n", g_failures); return 1; }
    std::printf("host_tests_measure: all passed\n");
    return 0;
}
