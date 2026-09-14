// CPU reference of the derived ClusterTree build (contract §13.4). The GLSL kernels
// world_cluster_leaves.comp / world_cluster_internal.comp implement exactly this per node; the host
// tests pin the reference, the device path is checked by invariants (counts, containment, coverage
// ranges) because float summation order differs between the two. Pure: no Vulkan.
//
// Layout (ComputeClusterLayout): node 0 = root, levels top-down, leaves last. Leaves cover consecutive
// runs of `leafSize` surfels of the Morton-sorted FRONT range; internal nodes have up to 8 children
// (the last node of a level may have fewer, down to 1). A page with n <= leafSize surfels is a
// single-node tree (root is a leaf).
#pragma once
#include <stdint.h>
#include <math.h>
#include <string.h>
#include <vector>
#include <algorithm>
#include "fs_world_types.h"

namespace fs {
namespace world {

constexpr float kPi = 3.14159265358979f;

// Coverage (contract §13.4 coverage-preserving far LOD): the node's footprint is the ellipse inscribed in
// its bounds projected along the cone axis (semi-axes a, b in the canonical tangent frame, packed into
// FsClusterNode.reserved as two log16 radii). A leaf's coverage is the fraction of FS_COVERAGE_GRID^2
// grid cells inside that ellipse that contain at least one projected surfel centre (gaps stay gaps,
// overlapping discs do not count twice); an internal node combines its children area-weighted:
// sum(cov_i * a_i * b_i) / (a * b), clamped. A multi-layer volume (dense bush) therefore saturates to 1
// while leaf clusters with real gaps stay well below 1.
inline bool CoverageCellInside(uint32_t cx, uint32_t cy) {
    float u = ((float)cx + 0.5f) / (float)FS_COVERAGE_GRID * 2.f - 1.f, v = ((float)cy + 0.5f) / (float)FS_COVERAGE_GRID * 2.f - 1.f;
    return u * u + v * v <= 1.f;
}

// Builds one leaf from surfels[first .. first+count) (indices into `front`, count <= leafSize).
inline FsClusterNode BuildLeaf(const FsSurfel* front, uint32_t first, uint32_t count, uint32_t globalFirst) {
    FsClusterNode n; memset(&n, 0, sizeof n);
    float bmin[3] = {1e30f, 1e30f, 1e30f}, bmax[3] = {-1e30f, -1e30f, -1e30f};
    float nsum[3] = {0, 0, 0}, csum[3] = {0, 0, 0}, colsum[3] = {0, 0, 0};
    float areaSum = 0.f, rmax = 0.f;
    uint32_t used = 0;
    for (uint32_t i = 0; i < count && i < FS_CLUSTER_LEAF_LOOP_MAX; ++i) {
        const FsSurfel& s = front[first + i];
        float p[3] = {DecodePos(s.px), DecodePos(s.py), DecodePos(s.pz)};
        float nn[3]; DecodeNormalOct32(s.normalOct, s.normalOctHi, nn);
        float rM = DecodeLogRadius(s.radiusMajor), rm = DecodeLogRadius(s.radiusMinor);
        for (int a = 0; a < 3; ++a) { bmin[a] = fminf(bmin[a], p[a] - rM); bmax[a] = fmaxf(bmax[a], p[a] + rM); csum[a] += p[a]; nsum[a] += nn[a]; }
        colsum[0] += (float)(s.appearanceHandle & 0xFFu); colsum[1] += (float)((s.appearanceHandle >> 8) & 0xFFu); colsum[2] += (float)((s.appearanceHandle >> 16) & 0xFFu);
        areaSum += kPi * rM * rm; rmax = fmaxf(rmax, rM); ++used;
    }
    if (used == 0) { for (int a = 0; a < 3; ++a) { bmin[a] = bmax[a] = 0.f; } }
    float inv = used ? 1.f / (float)used : 0.f;
    float nl = sqrtf(nsum[0] * nsum[0] + nsum[1] * nsum[1] + nsum[2] * nsum[2]);
    float axis[3] = {0.f, 0.f, 1.f}; float coneCos = -1.f;
    if (nl > 1e-6f) {
        axis[0] = nsum[0] / nl; axis[1] = nsum[1] / nl; axis[2] = nsum[2] / nl; coneCos = 1.f;
        for (uint32_t i = 0; i < used; ++i) {
            float nn[3]; DecodeNormalOct32(front[first + i].normalOct, front[first + i].normalOctHi, nn);
            coneCos = fminf(coneCos, axis[0] * nn[0] + axis[1] * nn[1] + axis[2] * nn[2]);
        }
    }
    for (int a = 0; a < 3; ++a) { n.bmin[a] = bmin[a]; n.bmax[a] = bmax[a]; n.coneAxis[a] = axis[a]; n.repCenter[a] = csum[a] * inv; }
    n.coneCos = coneCos;
    float d[3] = {bmax[0] - bmin[0], bmax[1] - bmin[1], bmax[2] - bmin[2]};
    n.repRadius = fmaxf(0.5f * sqrtf(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]), rmax);
    uint16_t o, oh; EncodeNormalOct32(axis, o, oh); n.repNormalOct32 = PackOct32(o, oh);
    uint32_t r = (uint32_t)(colsum[0] * inv + 0.5f), g = (uint32_t)(colsum[1] * inv + 0.5f), b = (uint32_t)(colsum[2] * inv + 0.5f);
    n.repColorOrHandle = (r & 0xFFu) | ((g & 0xFFu) << 8) | ((b & 0xFFu) << 16) | 0xFF000000u;
    // footprint ellipse + projected occupancy
    float t1[3], t2[3]; TangentFrame(axis, t1, t2);
    float fa, fb, fcx, fcy; FootprintOfAabb(bmin, bmax, t1, t2, fa, fb, fcx, fcy);
    fa = fmaxf(fa, rmax); fb = fmaxf(fb, rmax);
    uint64_t occ = 0;
    for (uint32_t i = 0; i < used; ++i) {
        const FsSurfel& s = front[first + i];
        float p[3] = {DecodePos(s.px), DecodePos(s.py), DecodePos(s.pz)};
        float u = ((p[0] * t1[0] + p[1] * t1[1] + p[2] * t1[2]) - fcx) / fa, v = ((p[0] * t2[0] + p[1] * t2[1] + p[2] * t2[2]) - fcy) / fb;
        if (u * u + v * v > 1.f) continue;
        int cx = (int)floorf((u + 1.f) * 0.5f * (float)FS_COVERAGE_GRID), cy = (int)floorf((v + 1.f) * 0.5f * (float)FS_COVERAGE_GRID);
        cx = cx < 0 ? 0 : (cx >= FS_COVERAGE_GRID ? FS_COVERAGE_GRID - 1 : cx); cy = cy < 0 ? 0 : (cy >= FS_COVERAGE_GRID ? FS_COVERAGE_GRID - 1 : cy);
        occ |= 1ull << (uint32_t)(cy * FS_COVERAGE_GRID + cx);
    }
    uint32_t occupied = 0;
    for (uint32_t cy = 0; cy < FS_COVERAGE_GRID; ++cy) for (uint32_t cx = 0; cx < FS_COVERAGE_GRID; ++cx) if (CoverageCellInside(cx, cy) && (occ >> (cy * FS_COVERAGE_GRID + cx)) & 1ull) occupied++;
    float cov = used ? (float)occupied / (float)FS_COVERAGE_INSIDE_CELLS : 0.f; cov = cov > 1.f ? 1.f : cov;
    n.coverage = (uint16_t)(cov * 65535.f + 0.5f);
    n.surfelCount = (uint16_t)(used > 65535u ? 65535u : used);
    n.firstChildOrSurfel = globalFirst; n.childCount = 0; n.leafSurfelCount = (uint16_t)(count > 65535u ? 65535u : count);
    n.reserved = PackFootprint(fa, fb);
    (void)areaSum;
    return n;
}

// Builds an internal node over nodes[firstChild .. firstChild+childCount) (indices into `nodes`).
inline FsClusterNode BuildInternal(const FsClusterNode* nodes, uint32_t firstChild, uint32_t childCount) {
    FsClusterNode n; memset(&n, 0, sizeof n);
    float bmin[3] = {1e30f, 1e30f, 1e30f}, bmax[3] = {-1e30f, -1e30f, -1e30f};
    float csum[3] = {0, 0, 0}, asum[3] = {0, 0, 0}, colsum[3] = {0, 0, 0};
    float areaSum = 0.f, wsum = 0.f; uint32_t count = 0; bool anyDir = false;
    for (uint32_t i = 0; i < childCount && i < FS_CLUSTER_FANOUT; ++i) {
        const FsClusterNode& c = nodes[firstChild + i];
        float w = (float)c.surfelCount; if (w < 1.f) w = 1.f;
        for (int a = 0; a < 3; ++a) { bmin[a] = fminf(bmin[a], c.bmin[a]); bmax[a] = fmaxf(bmax[a], c.bmax[a]); csum[a] += c.repCenter[a] * w; asum[a] += c.coneAxis[a] * w; }
        colsum[0] += (float)(c.repColorOrHandle & 0xFFu) * w; colsum[1] += (float)((c.repColorOrHandle >> 8) & 0xFFu) * w; colsum[2] += (float)((c.repColorOrHandle >> 16) & 0xFFu) * w;
        areaSum += (float)c.coverage / 65535.f * FootprintA(c.reserved) * FootprintB(c.reserved);   // covered footprint area / pi
        wsum += w; count += c.surfelCount; if (c.coneCos <= -1.f) anyDir = true;
    }
    float inv = wsum > 0.f ? 1.f / wsum : 0.f;
    float al = sqrtf(asum[0] * asum[0] + asum[1] * asum[1] + asum[2] * asum[2]);
    float axis[3] = {0.f, 0.f, 1.f}; float coneCos = -1.f;
    if (!anyDir && al > 1e-6f) {
        axis[0] = asum[0] / al; axis[1] = asum[1] / al; axis[2] = asum[2] / al;
        float half = 0.f;
        for (uint32_t i = 0; i < childCount && i < FS_CLUSTER_FANOUT; ++i) {
            const FsClusterNode& c = nodes[firstChild + i];
            float d = axis[0] * c.coneAxis[0] + axis[1] * c.coneAxis[1] + axis[2] * c.coneAxis[2];
            d = d < -1.f ? -1.f : (d > 1.f ? 1.f : d);
            float cc = c.coneCos < -1.f ? -1.f : (c.coneCos > 1.f ? 1.f : c.coneCos);
            half = fmaxf(half, acosf(d) + acosf(cc));
        }
        coneCos = half >= kPi ? -1.f : cosf(half);
    }
    for (int a = 0; a < 3; ++a) { n.bmin[a] = bmin[a]; n.bmax[a] = bmax[a]; n.coneAxis[a] = axis[a]; n.repCenter[a] = csum[a] * inv; }
    n.coneCos = coneCos;
    float d[3] = {bmax[0] - bmin[0], bmax[1] - bmin[1], bmax[2] - bmin[2]};
    n.repRadius = 0.5f * sqrtf(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
    uint16_t o, oh; EncodeNormalOct32(axis, o, oh); n.repNormalOct32 = PackOct32(o, oh);
    uint32_t r = (uint32_t)(colsum[0] * inv + 0.5f), g = (uint32_t)(colsum[1] * inv + 0.5f), b = (uint32_t)(colsum[2] * inv + 0.5f);
    n.repColorOrHandle = (r & 0xFFu) | ((g & 0xFFu) << 8) | ((b & 0xFFu) << 16) | 0xFF000000u;
    float t1[3], t2[3]; TangentFrame(axis, t1, t2);
    float fa, fb, fcx, fcy; FootprintOfAabb(bmin, bmax, t1, t2, fa, fb, fcx, fcy);
    float aggArea = fa * fb;
    float cov = aggArea > 0.f ? areaSum / aggArea : 0.f; cov = cov < 0.f ? 0.f : (cov > 1.f ? 1.f : cov);
    n.coverage = (uint16_t)(cov * 65535.f + 0.5f);
    n.surfelCount = (uint16_t)(count > 65535u ? 65535u : count);
    n.firstChildOrSurfel = firstChild; n.childCount = (uint16_t)childCount; n.leafSurfelCount = 0;
    n.reserved = PackFootprint(fa, fb);
    return n;
}

// Full build. `front` is the Morton-sorted FRONT range (n surfels) whose first global arena index is
// globalFirst; nodeCapacity bounds the tree (coarser leaves when exceeded; *overflowed set).
inline ClusterLayout BuildClusterTree(const FsSurfel* front, uint32_t n, uint32_t globalFirst, uint32_t nodeCapacity,
                                      std::vector<FsClusterNode>& out, bool* overflowed = nullptr) {
    uint32_t shift = ChooseLeafShift(n, nodeCapacity);
    if (overflowed) *overflowed = shift > 0;
    ClusterLayout L = ComputeClusterLayout(n, shift);
    out.assign(L.total, FsClusterNode{});
    if (L.levels == 0) return L;
    for (uint32_t j = 0; j < L.count[0]; ++j) {
        uint32_t first = j * L.leafSize, cnt = std::min(L.leafSize, n - first);
        out[L.offset[0] + j] = BuildLeaf(front, first, cnt, globalFirst + first);
    }
    for (uint32_t lv = 1; lv < L.levels; ++lv)
        for (uint32_t j = 0; j < L.count[lv]; ++j) {
            uint32_t firstChild = L.offset[lv - 1] + j * FS_CLUSTER_FANOUT;
            uint32_t cc = std::min<uint32_t>(FS_CLUSTER_FANOUT, L.count[lv - 1] - j * FS_CLUSTER_FANOUT);
            out[L.offset[lv] + j] = BuildInternal(out.data(), firstChild, cc);
        }
    return L;
}

// Stable Morton sort of a page's surfels (direct-upload path; the GPU publish path orders by cell list walk).
inline void MortonSort(std::vector<FsSurfel>& surfels) {
    std::stable_sort(surfels.begin(), surfels.end(), [](const FsSurfel& a, const FsSurfel& b) {
        return MortonOfCell(CellOfSurfel(a)) < MortonOfCell(CellOfSurfel(b)); });
}

} // namespace world
} // namespace fs
