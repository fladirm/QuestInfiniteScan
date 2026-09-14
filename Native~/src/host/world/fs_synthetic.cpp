// Synthetic world generator (C04). See fs_synthetic.h.
#include "fs_synthetic.h"
#include <math.h>
#include <string.h>
#include <unordered_map>

namespace fs {
namespace world {
namespace {

struct Rng { uint32_t s; float Next() { s = s * 1664525u + 1013904223u; return (float)(s >> 8) / 16777216.f; } };

// Axis-aligned rectangle: origin o, edge vectors u (length lu) and v (length lv), unit normal n.
struct Quad { float o[3]; float u[3]; float v[3]; float lu, lv; float n[3]; };
// Rectangular hole in a quad, expressed in (u,v) metres from the quad origin.
struct Hole { float u0, u1, v0, v1; };

struct Builder {
    SyntheticScene& out; float spacing; Rng rng; uint32_t nextSurface = 1;
    std::unordered_map<uint64_t, size_t> pageIndex;
    Builder(SyntheticScene& o, float sp, uint32_t seed) : out(o), spacing(sp), rng{seed | 1u} {}

    static uint64_t KeyHash(const FsPageKey& k) {
        return ((uint64_t)(uint32_t)k.x * 73856093ull) ^ ((uint64_t)(uint32_t)k.y * 19349663ull) ^
               ((uint64_t)(uint32_t)k.z * 83492791ull) ^ ((uint64_t)(uint32_t)k.anchorId << 48);
    }
    void AddQuad(const Quad& q, const Hole* holes, int holeCount) {
        uint32_t surface = nextSurface++;
        int nu = (int)floorf(q.lu / spacing), nv = (int)floorf(q.lv / spacing);
        for (int j = 0; j <= nv; ++j)
            for (int i = 0; i <= nu; ++i) {
                float du = (float)i * spacing + spacing * 0.5f, dv = (float)j * spacing + spacing * 0.5f;
                if (du > q.lu || dv > q.lv) continue;
                bool inHole = false;
                for (int h = 0; h < holeCount; ++h)
                    if (du >= holes[h].u0 && du <= holes[h].u1 && dv >= holes[h].v0 && dv <= holes[h].v1) { inHole = true; break; }
                if (inHole) continue;
                SurfelSample s;
                for (int a = 0; a < 3; ++a) s.p[a] = q.o[a] + q.u[a] * du + q.v[a] * dv;
                memcpy(s.n, q.n, sizeof s.n);
                s.radius = spacing * 0.75f;
                s.sigmaN = 0.001f + rng.Next() * 0.0002f;
                s.sigmaT = 0.002f;
                s.surfaceId = surface;
                out.samples.push_back(s);
            }
        out.planeCount++;
    }
    // Wall between corners (x0,z0)-(x1,z1) from y0 to y1 with normal n (must be ±x or ±z).
    void Wall(float x0, float z0, float x1, float z1, float y0, float y1, const float n[3], const Hole* holes = nullptr, int hc = 0) {
        Quad q{}; q.o[0] = x0; q.o[1] = y0; q.o[2] = z0;
        float dx = x1 - x0, dz = z1 - z0; float len = sqrtf(dx * dx + dz * dz);
        q.u[0] = dx / len; q.u[1] = 0; q.u[2] = dz / len; q.lu = len;
        q.v[0] = 0; q.v[1] = 1; q.v[2] = 0; q.lv = y1 - y0;
        memcpy(q.n, n, sizeof q.n);
        AddQuad(q, holes, hc);
    }
    void Horizontal(float x0, float z0, float x1, float z1, float y, float ny) {
        Quad q{}; q.o[0] = x0; q.o[1] = y; q.o[2] = z0;
        q.u[0] = 1; q.u[1] = 0; q.u[2] = 0; q.lu = x1 - x0;
        q.v[0] = 0; q.v[1] = 0; q.v[2] = 1; q.lv = z1 - z0;
        q.n[0] = 0; q.n[1] = ny; q.n[2] = 0;
        AddQuad(q, nullptr, 0);
    }
    // Room box [x0,x1]×[0,H]×[z0,z1], normals inward; optional doorway holes per wall (index 0..3: -z,+z,-x,+x).
    void Room(float x0, float z0, float x1, float z1, float H, const Hole* doorPerWall[4]) {
        const float nPz[3] = {0, 0, 1}, nNz[3] = {0, 0, -1}, nPx[3] = {1, 0, 0}, nNx[3] = {-1, 0, 0};
        Horizontal(x0, z0, x1, z1, 0.f, 1.f);      // floor, normal up
        Horizontal(x0, z0, x1, z1, H, -1.f);       // ceiling, normal down
        Wall(x0, z0, x1, z0, 0, H, nPz, doorPerWall[0], doorPerWall[0] ? 1 : 0);   // -z wall faces +z
        Wall(x0, z1, x1, z1, 0, H, nNz, doorPerWall[1], doorPerWall[1] ? 1 : 0);   // +z wall faces -z
        Wall(x0, z0, x0, z1, 0, H, nPx, doorPerWall[2], doorPerWall[2] ? 1 : 0);   // -x wall faces +x
        Wall(x1, z0, x1, z1, 0, H, nNx, doorPerWall[3], doorPerWall[3] ? 1 : 0);   // +x wall faces -x
    }
    // Ellipsoid leaf: centre c, semi-axes (a,b,t) in a random frame; parametric sampling at `spacing`
    // with kFoliageGapFraction random drop-out and one rectangular hole; normal = ellipsoid gradient.
    void Leaf(const float c[3], float a, float b, float t, uint32_t surface) {
        float yaw = rng.Next() * 6.2831853f, pitch = (rng.Next() - 0.5f) * 2.4f, roll = rng.Next() * 6.2831853f;
        float cy = cosf(yaw), sy = sinf(yaw), cp = cosf(pitch), sp = sinf(pitch), cr = cosf(roll), sr = sinf(roll);
        // frame axes (rows of R = Rz(yaw) Ry(pitch) Rx(roll))
        float U[3] = {cy * cp, sy * cp, -sp};
        float V[3] = {cy * sp * sr - sy * cr, sy * sp * sr + cy * cr, cp * sr};
        float W[3] = {cy * sp * cr + sy * sr, sy * sp * cr - cy * sr, cp * cr};
        int nu = (int)(6.2831853f * a / spacing) + 1, nv = (int)(3.1415926f * b / spacing) + 1;
        if (nu > 64) nu = 64;
        if (nv > 32) nv = 32;
        float h0 = rng.Next() * 0.6f + 0.2f, h1 = h0 + 0.15f, g0 = rng.Next() * 0.6f + 0.2f, g1 = g0 + 0.2f;
        uint32_t leafSeed = rng.s;
        for (int j = 1; j < nv; ++j)
            for (int i = 0; i < nu; ++i) {
                float u = (float)i / (float)nu, v = (float)j / (float)nv;
                if (u > h0 && u < h1 && v > g0 && v < g1) continue;                     // hole
                float phi = u * 6.2831853f, th = v * 3.1415926f;
                float lx = a * sinf(th) * cosf(phi), ly = b * sinf(th) * sinf(phi), lz = t * cosf(th);
                // spatially coherent gaps (2 cm blocks in the leaf's projected x/y so front and back sides
                // share them): far-LOD coverage must see real holes, not per-sample noise
                uint32_t bx = (uint32_t)(int32_t)floorf(lx / kFoliageGapBlockM), by = (uint32_t)(int32_t)floorf(ly / kFoliageGapBlockM);
                uint32_t hsh = (bx * 0x9E3779B1u) ^ (by * 0x85EBCA77u) ^ leafSeed; hsh ^= hsh >> 15; hsh *= 0x2C1B3C6Du; hsh ^= hsh >> 12;
                if ((float)(hsh & 0xFFFFu) / 65536.f < kFoliageGapFraction) continue;
                float gx = lx / (a * a), gy = ly / (b * b), gz = lz / (t * t);
                float gl = sqrtf(gx * gx + gy * gy + gz * gz); if (gl < 1e-9f) gl = 1.f;
                gx /= gl; gy /= gl; gz /= gl;
                SurfelSample s;
                for (int k = 0; k < 3; ++k) { s.p[k] = c[k] + U[k] * lx + V[k] * ly + W[k] * lz; s.n[k] = U[k] * gx + V[k] * gy + W[k] * gz; }
                s.radius = spacing * 0.75f; s.sigmaN = 0.002f; s.sigmaT = 0.003f; s.surfaceId = surface;
                out.samples.push_back(s);
            }
    }
    // Plant: leaves on random positions inside a sphere of radius R around c.
    void Plant(const float c[3], float R, int leaves) {
        uint32_t surface = nextSurface++;
        for (int l = 0; l < leaves; ++l) {
            float d[3] = {rng.Next() * 2.f - 1.f, rng.Next() * 2.f - 1.f, rng.Next() * 2.f - 1.f};
            float lc[3] = {c[0] + d[0] * R, c[1] + d[1] * R, c[2] + d[2] * R};
            float a = 0.03f + rng.Next() * 0.05f, b = a * (0.4f + rng.Next() * 0.4f), t = 0.002f + rng.Next() * 0.004f;
            Leaf(lc, a, b, t, surface);
        }
        out.planeCount++;
    }
    void Finish(int32_t anchorId) {
        out.sampleCount = out.samples.size();
        for (const SurfelSample& s : out.samples) {
            FsPageKey k = MakePageKey(anchorId, s.p);
            uint64_t h = KeyHash(k);
            auto it = pageIndex.find(h);
            size_t idx;
            if (it == pageIndex.end() || !KeyEq(out.pages[it->second].key, k)) {
                idx = out.pages.size(); out.pages.push_back(SyntheticPage{k, {}}); pageIndex[h] = idx;
            } else idx = it->second;
            out.pages[idx].surfels.push_back(MakeSurfel(s, k));
        }
    }
};

} // namespace

void GenerateSynthetic(int32_t kind, float extentM, float spacing, uint32_t seed, int32_t anchorId, SyntheticScene& out) {
    out = SyntheticScene{};
    if (spacing < 0.005f) spacing = 0.005f;
    if (spacing > 1.f) spacing = 1.f;
    if (extentM < 1.f) extentM = 1.f;
    Builder b(out, spacing, seed);
    const float H = kSyntheticRoomHeightM, E = extentM, h = extentM * 0.5f;
    const Hole* none[4] = {nullptr, nullptr, nullptr, nullptr};
    switch (kind & 0xFF) {
    case SYN_ROOM_BOX: {
        b.Room(-h, -h, h, h, H, none);
        break;
    }
    case SYN_CORRIDOR: {
        // Room A at z ∈ [-E, 0], corridor z ∈ [0, E/2] width 1.2 m, room B at z ∈ [E/2, E/2 + E].
        const float W = 1.2f, L = E * 0.5f, doorH = 2.0f;
        Hole doorA{h - W * 0.5f, h + W * 0.5f, 0.f, doorH};        // in +z wall of A, u measured from x=-h
        Hole doorB{h - W * 0.5f, h + W * 0.5f, 0.f, doorH};        // in -z wall of B
        const Hole* roomA[4] = {nullptr, &doorA, nullptr, nullptr};
        const Hole* roomB[4] = {&doorB, nullptr, nullptr, nullptr};
        b.Room(-h, -E, h, 0.f, H, roomA);
        b.Room(-h, L, h, L + E, H, roomB);
        const float nPx[3] = {1, 0, 0}, nNx[3] = {-1, 0, 0};
        b.Horizontal(-W * 0.5f, 0.f, W * 0.5f, L, 0.f, 1.f);
        b.Horizontal(-W * 0.5f, 0.f, W * 0.5f, L, H, -1.f);
        b.Wall(-W * 0.5f, 0.f, -W * 0.5f, L, 0.f, H, nPx);
        b.Wall(W * 0.5f, 0.f, W * 0.5f, L, 0.f, H, nNx);
        break;
    }
    case SYN_STAIRCASE: {
        // Floor, then steps climbing along +z: rise 0.17 m, run 0.28 m, width 1.2 m, up to extent/run steps.
        const float rise = 0.17f, run = 0.28f, W = 1.2f;
        int steps = (int)(E / run); if (steps < 1) steps = 1; if (steps > 24) steps = 24;
        b.Horizontal(-h, -h, h, 0.f, 0.f, 1.f);
        const float nNz[3] = {0, 0, -1};
        for (int s = 0; s < steps; ++s) {
            float z0 = (float)s * run, y = (float)(s + 1) * rise;
            b.Wall(-W * 0.5f, z0, W * 0.5f, z0, (float)s * rise, y, nNz);    // riser faces -z (toward climber)
            b.Horizontal(-W * 0.5f, z0, W * 0.5f, z0 + run, y, 1.f);       // tread faces up
        }
        break;
    }
    case SYN_DENSE_FOLIAGE: {
        // Room with 3 shelf levels along both long walls; plants placed until kFoliageTargetSurfels or
        // the shelves are full. Leaf spacing <= 1 cm regardless of the requested spacing.
        b.spacing = spacing < kFoliageLeafSpacingM ? spacing : kFoliageLeafSpacingM;
        b.Room(-h, -h, h, h, H, none);
        const float shelfY[3] = {0.4f, 1.1f, 1.8f}, R = 0.22f;
        const float rowZ[2] = {-h + 0.35f, h - 0.35f};
        int perRow = (int)(E / 0.45f); if (perRow < 1) perRow = 1;
        bool done = false;
        for (int pass = 0; pass < 4 && !done; ++pass)          // several plants per shelf position when needed
            for (int lvl = 0; lvl < 3 && !done; ++lvl)
                for (int row = 0; row < 2 && !done; ++row)
                    for (int i = 0; i < perRow && !done; ++i) {
                        float c[3] = {-h + 0.25f + (float)i * 0.45f + (b.rng.Next() - 0.5f) * 0.1f,
                                      shelfY[lvl] + R + (float)pass * 0.02f, rowZ[row] + (b.rng.Next() - 0.5f) * 0.1f};
                        b.Plant(c, R, 180);
                        if (out.samples.size() >= kFoliageTargetSurfels) done = true;
                    }
        break;
    }
    case SYN_THIN_WALL:
    default: {
        // Two sheets E wide × H tall, 3 cm apart, opposite normals, plus a floor.
        const float g = kSyntheticThinWallGapM * 0.5f;
        const float nPz[3] = {0, 0, 1}, nNz[3] = {0, 0, -1};
        b.Wall(-h, g, h, g, 0.f, H, nPz);       // sheet at +1.5 cm faces +z
        b.Wall(-h, -g, h, -g, 0.f, H, nNz);     // sheet at -1.5 cm faces -z
        b.Horizontal(-h, -h, h, h, 0.f, 1.f);
        break;
    }
    }
    b.Finish(anchorId);
}

void SamplesToMeasurements(const std::vector<SurfelSample>& samples, size_t begin, size_t end, uint32_t observationId,
                           std::vector<FsSurfaceMeasurement>& out) {
    out.clear();
    if (end > samples.size()) end = samples.size();
    for (size_t i = begin; i < end; ++i) {
        const SurfelSample& s = samples[i];
        FsSurfaceMeasurement m{};
        m.px = s.p[0]; m.py = s.p[1]; m.pz = s.p[2];
        m.nx = s.n[0]; m.ny = s.n[1]; m.nz = s.n[2];
        m.sigmaN = s.sigmaN; m.sigmaT = s.sigmaT; m.footprint = s.radius;
        m.sourceFlags = 1u << 0;     // stereo
        m.observationId = observationId;
        out.push_back(m);
    }
}

} // namespace world
} // namespace fs
