// CPU reference of the staged fusion (C09R-B, contract §8.2–§8.6): byte-for-byte twins of fs_fusion.glsl,
// fs_cluster.glsl, fs_index.glsl, fs_maint.glsl, fuse_reduce.comp, fuse_freespace.comp, fuse_associate.comp
// (scoring) and the stable 32-bit LSD radix sort. Pure headers, no Vulkan: the host tests (Native~/tests/
// host_tests_world.cpp) pin determinism, bounds and the owner/generation invariants against these twins.
//
// Determinism definition (C09R review gap 2): an epoch's result is a pure function of the measurement SEQUENCE
// (ingest order = ring order = arrival order) and the immutable canonical/index generation the epoch reads.
// Every segment is reduced SEQUENTIALLY in stable-sort order (key, then measurement index); no tree reduction,
// no atomics on floats. Replaying the same sequence reproduces every bit; a permutation of the same set reaches
// the same associations and the same result up to floating-point summation order.
#pragma once
#include <stdint.h>
#include <math.h>
#include <string.h>
#include <vector>
#include <functional>
#include <algorithm>
#include "fs_world_types.h"
#include "fs_world_params.h"

namespace fs {
namespace world {

constexpr float kPi = 3.14159265358979f;
struct V3 { float x = 0.f, y = 0.f, z = 0.f; };
inline V3 v3(float x, float y, float z) { return V3{x, y, z}; }
inline V3 operator+(V3 a, V3 b) { return V3{a.x + b.x, a.y + b.y, a.z + b.z}; }
inline V3 operator-(V3 a, V3 b) { return V3{a.x - b.x, a.y - b.y, a.z - b.z}; }
inline V3 operator*(V3 a, float s) { return V3{a.x * s, a.y * s, a.z * s}; }
inline float Dot(V3 a, V3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
inline float Len(V3 a) { return sqrtf(Dot(a, a)); }
inline V3 Norm(V3 a) { float l = Len(a); return l > 0.f ? a * (1.f / l) : a; }
inline V3 Cross(V3 a, V3 b) { return V3{a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x}; }
inline void Frame(V3 n, V3& t1, V3& t2) { float nn[3] = {n.x, n.y, n.z}, a[3], b[3]; TangentFrame(nn, a, b); t1 = v3(a[0], a[1], a[2]); t2 = v3(b[0], b[1], b[2]); }
inline V3 PageOrigin3(const FsPageKey& k) { return v3(PageOrigin(k.x), PageOrigin(k.y), PageOrigin(k.z)); }
inline V3 LocalPos(const FsSurfel& s) { return v3(DecodePos(s.px), DecodePos(s.py), DecodePos(s.pz)); }
inline V3 Normal(const FsSurfel& s) { float n[3]; DecodeNormalOct32(s.normalOct, s.normalOctHi, n); return v3(n[0], n[1], n[2]); }
inline uint32_t CellOfV(V3 l) { return CellOf(l.x, l.y, l.z); }
inline V3 ClampV(V3 a, V3 lo, V3 hi) { return v3(fminf(fmaxf(a.x, lo.x), hi.x), fminf(fmaxf(a.y, lo.y), hi.y), fminf(fmaxf(a.z, lo.z), hi.z)); }

// ---- patch (twin: FsPatch / fsUnpack / fsPack) ---------------------------------------------------------
struct Patch { V3 p, n; float angle = 0.f, rM = 0.f, rm = 0.f, sigmaN = 0.f, sigmaT = 0.f; uint32_t flags = 0, surfaceId = 0, appearance = 0; };
inline Patch Unpack(const FsSurfel& s) {
    Patch q; q.p = LocalPos(s); q.n = Normal(s);
    q.angle = (float)s.tangentAngle / 65536.f * 2.f * kPi;
    q.rM = DecodeLogRadius(s.radiusMajor); q.rm = DecodeLogRadius(s.radiusMinor);
    q.sigmaN = DecodeLogSigma(s.sigmaN); q.sigmaT = DecodeLogSigma(s.sigmaTMajor);
    q.flags = s.evidenceFlags; q.surfaceId = s.surfaceId; q.appearance = s.appearanceHandle;
    return q;
}
inline FsSurfel Pack(const Patch& q) {
    FsSurfel s; memset(&s, 0, sizeof s);
    s.px = EncodePos(q.p.x); s.py = EncodePos(q.p.y); s.pz = EncodePos(q.p.z);
    float n[3] = {q.n.x, q.n.y, q.n.z}; EncodeNormalOct32(n, s.normalOct, s.normalOctHi);
    float a = q.angle; a = a - floorf(a / (2.f * kPi)) * 2.f * kPi;
    s.tangentAngle = (uint16_t)((uint32_t)(a / (2.f * kPi) * 65536.f) & 0xFFFFu);
    s.radiusMajor = EncodeLogRadius(q.rM); s.radiusMinor = EncodeLogRadius(q.rm);
    s.sigmaN = EncodeLogSigma(q.sigmaN); s.sigmaTMajor = s.sigmaTMinor = EncodeLogSigma(q.sigmaT);
    s.evidenceFlags = (uint16_t)q.flags; s.surfaceId = q.surfaceId; s.appearanceHandle = q.appearance;
    return s;
}
inline void PatchMoment(const Patch& q, float& a, float& b, float& c) {
    float cs = cosf(q.angle), sn = sinf(q.angle), M = q.rM * q.rM, m = q.rm * q.rm;
    a = M * cs * cs + m * sn * sn; b = M * sn * sn + m * cs * cs; c = (M - m) * cs * sn;
}
inline void MomentToEllipse(float a, float b, float c, float& rM, float& rm, float& angle) {
    float tr = a + b, det = a * b - c * c;
    float disc = sqrtf(fmaxf(tr * tr * 0.25f - det, 0.f));
    float l1 = tr * 0.5f + disc, l2 = fmaxf(tr * 0.5f - disc, 0.f);
    rM = sqrtf(fmaxf(l1, 0.f)); rm = sqrtf(l2);
    angle = fabsf(c) < 1e-12f ? (a >= b ? 0.f : 0.5f * kPi) : atan2f(l1 - a, c);
}

// ---- measurement helpers ------------------------------------------------------------------------------
inline V3 MeasPos(const FsSurfaceMeasurement& m) { return v3(m.px, m.py, m.pz); }
inline V3 MeasNormal(const FsSurfaceMeasurement& m) { return Norm(v3(m.nx, m.ny, m.nz)); }
inline float MeasSigmaN(const FsSurfaceMeasurement& m) { return fmaxf(m.sigmaN, (float)FS_SIGMA_N_FLOOR_M); }
inline float MeasSigmaT(const FsSurfaceMeasurement& m) { return fmaxf(m.sigmaT, (float)FS_SIGMA_T_FLOOR_M); }
inline float MeasFootprint(const FsSurfaceMeasurement& m) { return fminf(fmaxf(m.footprint, (float)FS_RADIUS_MIN_M), (float)FS_RADIUS_MAX_M); }

// ---- F0 association scoring (twin of the candidate loop in fuse_associate.comp) -----------------------
// Returns true when the surfel is compatible with the measurement; `score` is the Mahalanobis-like rank.
inline bool AssocCompatible(const FsSurfel& s, V3 local, V3 n, float sigmaNm, float footprint, float& score) {
    if (s.evidenceFlags & kFlagRemoved) return false;
    V3 sp = LocalPos(s), sn = Normal(s);
    if (Dot(sn, n) < (float)FS_ASSOC_MIN_DOT) return false;
    V3 delta = local - sp; float d = Dot(sn, delta);
    float sigmaNs = DecodeLogSigma(s.sigmaN);
    float gate = (float)FS_ASSOC_SIGMA_GATE * sqrtf(sigmaNs * sigmaNs + sigmaNm * sigmaNm);
    if (fabsf(d) > gate) return false;
    V3 tv = delta - sn * d;
    float reach = fmaxf((float)FS_ASSOC_TANGENT_K * (DecodeLogRadius(s.radiusMajor) + footprint), (float)FS_ASSOC_REACH_MIN_M);
    float t2 = Dot(tv, tv);
    if (t2 > reach * reach) return false;
    score = (d * d) / fmaxf(gate * gate, 1e-12f) + t2 / fmaxf(reach * reach, 1e-12f);
    return true;
}
// Best candidate among `handles` (deterministic tie-break: lower handle). FS_INDEX_NONE when none.
inline uint32_t AssocBest(const std::vector<FsSurfel>& surfels, const std::vector<uint32_t>& handles, V3 local, V3 n, float sigmaNm, float footprint, float* bestScoreOut = nullptr) {
    uint32_t best = FS_INDEX_NONE; float bestScore = 1e30f; uint32_t examined = 0;
    for (uint32_t h : handles) {
        if (examined >= FS_ASSOC_CANDIDATE_MAX) break;
        examined++;
        float sc; if (!AssocCompatible(surfels[h], local, n, sigmaNm, footprint, sc)) continue;
        if (sc < bestScore || (sc == bestScore && h < best)) { bestScore = sc; best = h; }
    }
    if (bestScoreOut) *bestScoreOut = bestScore;
    return best;
}
inline uint32_t UnmatchedKey(uint32_t pageSlot, uint32_t cell) { return FS_ASSOC_KEY_UNMATCHED | (pageSlot << 15) | cell; }

// ---- F1 stable sort (twin: sort_histogram / sort_scan / sort_scatter: 4 x 8-bit LSD passes, sequential rank) --
inline void RadixSortAssoc(std::vector<FsAssociation>& a) {
    std::vector<FsAssociation> b(a.size());
    for (uint32_t pass = 0; pass < 4; ++pass) {
        uint32_t shift = pass * 8; uint32_t hist[256] = {};
        for (const FsAssociation& r : a) hist[(r.key >> shift) & 255u]++;
        uint32_t sum = 0; for (uint32_t d = 0; d < 256; ++d) { uint32_t c = hist[d]; hist[d] = sum; sum += c; }
        for (const FsAssociation& r : a) b[hist[(r.key >> shift) & 255u]++] = r;   // stable: rank by arrival within a digit
        a.swap(b);
    }
}

// ---- F2 segmented reduce (twin: fs_fusion.glsl FsAccum + fuse_reduce.comp) -----------------------------
struct Accum { float wN = 0, dN = 0; V3 nSum; float wT = 0; float tx = 0, ty = 0; float mxx = 0, myy = 0, mxy = 0, d2 = 0, s2 = 0, fp = 0; V3 col; float colW = 0; uint32_t n = 0; };
inline V3 ColorOf(uint32_t w) { return v3((float)(w & 0xFFu), (float)((w >> 8) & 0xFFu), (float)((w >> 16) & 0xFFu)) * (1.f / 255.f); }
inline uint32_t ColorPack(V3 c) { auto q = [](float x) { x = x < 0.f ? 0.f : (x > 1.f ? 1.f : x); return (uint32_t)(x * 255.f + 0.5f); }; return q(c.x) | (q(c.y) << 8) | (q(c.z) << 16); }
inline uint32_t BlendAppearance(uint32_t old, V3 meanCol, float wPrior, float wMeas) {
    if ((old & FS_APPEARANCE_MEASURED) == 0u) return FS_APPEARANCE_MEASURED | ColorPack(meanCol);
    V3 o = ColorOf(old); float t = wMeas / (wPrior + wMeas);
    return FS_APPEARANCE_MEASURED | ColorPack(o + (meanCol - o) * t);
}
inline void AccumAdd(Accum& a, const Patch& q, V3 t1, V3 t2, V3 pm, V3 nm, float sigmaNm, float sigmaTm, float footprint, uint32_t colorWord = 0u) {
    float wN = 1.f / (sigmaNm * sigmaNm), wT = 1.f / (sigmaTm * sigmaTm);
    V3 delta = pm - q.p; float d = Dot(q.n, delta);
    float tvx = Dot(delta, t1), tvy = Dot(delta, t2);
    a.wN += wN; a.dN += wN * d; a.nSum = a.nSum + nm * wN;
    a.wT += wT; a.tx += tvx * wT; a.ty += tvy * wT;
    float f2 = footprint * footprint;
    a.mxx += wT * (tvx * tvx + f2); a.myy += wT * (tvy * tvy + f2); a.mxy += wT * tvx * tvy;
    a.d2 += d * d; a.s2 += sigmaNm * sigmaNm; a.fp = fmaxf(a.fp, footprint); a.n++;
    if (colorWord & FS_MEAS_COLOR_VALID) { a.col = a.col + ColorOf(colorWord); a.colW += 1.f; }
}
struct ReduceResult { FsSurfel surfel; FsSurfelEvidence evidence; uint32_t folded = 0; bool overflow = false; bool changed = false; };
// One matched segment = one surfel; `seg` in sorted (measurement-sequence) order; `origin` = page origin.
inline ReduceResult ReduceSegment(const FsSurfel& s, const FsSurfelEvidence& evIn, const std::vector<FsSurfaceMeasurement>& seg, V3 origin, uint32_t tick) {
    ReduceResult r; r.surfel = s; r.evidence = evIn;
    Patch q = Unpack(s);
    if (q.flags & kFlagRemoved) return r;
    V3 t1, t2; Frame(q.n, t1, t2);
    Accum acc;
    for (size_t k = 0; k < seg.size(); ++k) {
        if (k == FS_SEG_REDUCE_MAX) { r.overflow = true; break; }
        const FsSurfaceMeasurement& m = seg[k];
        AccumAdd(acc, q, t1, t2, MeasPos(m) - origin, MeasNormal(m), MeasSigmaN(m), MeasSigmaT(m), MeasFootprint(m), m.reserved);
    }
    if (acc.n == 0) return r;
    float wNs = 1.f / (q.sigmaN * q.sigmaN), wTs = 1.f / (q.sigmaT * q.sigmaT);
    float aN = acc.wN / (wNs + acc.wN), aT = acc.wT / (wTs + acc.wT);
    float dMean = acc.dN / acc.wN;
    float tMx = acc.tx / acc.wT, tMy = acc.ty / acc.wT;
    V3 np = q.p + q.n * (dMean * aN) + (t1 * tMx + t2 * tMy) * aT;
    V3 nn = Norm(q.n * wNs + acc.nSum);
    float newSigmaN = fmaxf(sqrtf(1.f / (wNs + acc.wN)), (float)FS_SIGMA_N_FLOOR_M);
    float newSigmaT = fmaxf(sqrtf(1.f / (wTs + acc.wT)), (float)FS_SIGMA_T_FLOOR_M);
    uint32_t cnt = q.flags & kEvidenceCountMask;
    float ma, mb, mc; PatchMoment(q, ma, mb, mc);
    float wPrior = (float)(cnt > 1 ? cnt : 1), wMeas = (float)acc.n;
    float mxx = acc.mxx / acc.wT - tMx * tMx * aT, myy = acc.myy / acc.wT - tMy * tMy * aT, mxy = acc.mxy / acc.wT - tMx * tMy * aT;
    float fa = (ma * wPrior + mxx * wMeas) / (wPrior + wMeas), fb = (mb * wPrior + myy * wMeas) / (wPrior + wMeas), fc = (mc * wPrior + mxy * wMeas) / (wPrior + wMeas);
    float rM, rm, ang; MomentToEllipse(fa, fb, fc, rM, rm, ang);
    rM = fminf(fmaxf(fmaxf(rM, acc.fp), (float)FS_RADIUS_MIN_M), (float)FS_RADIUS_MAX_M); rm = fminf(fmaxf(fmaxf(rm, (float)FS_RADIUS_MIN_M), (float)FS_RADIUS_MIN_M), rM);
    uint32_t cell = CellOfV(q.p);
    float bmin[3], bmax[3]; CellBounds(cell, bmin, bmax);
    np = ClampV(np, v3(bmin[0] + 0.0005f, bmin[1] + 0.0005f, bmin[2] + 0.0005f), v3(bmax[0] - 0.0005f, bmax[1] - 0.0005f, bmax[2] - 0.0005f));
    FsSurfelEvidence ev = evIn;
    const float varBase = (float)FS_VAR_NORM_BASE;
    float var = DecodeLog(ev.varianceQ, varBase);
    float meanD2 = (acc.d2 / (float)acc.n) / fmaxf(acc.s2 / (float)acc.n, 1e-12f);
    var += (meanD2 - var) * ((float)acc.n / (float)(cnt + acc.n));
    uint32_t stat = std::min<uint32_t>(ev.staticEvidence + acc.n, 65535u);
    uint32_t flags = q.flags;
    cnt = std::min<uint32_t>(cnt + acc.n, FS_EVIDENCE_COUNT_MAX);
    if ((flags & kFlagTransient) && stat >= FS_PROMOTE_STATIC) flags = (flags & ~(uint32_t)kFlagTransient) | kFlagPromoted;
    q.p = np; q.n = nn; q.angle = ang; q.rM = rM; q.rm = rm; q.sigmaN = newSigmaN; q.sigmaT = newSigmaT;
    q.flags = (flags & ~(uint32_t)kEvidenceCountMask) | cnt;
    if (acc.colW > 0.f) q.appearance = BlendAppearance(q.appearance, acc.col * (1.f / acc.colW), wPrior, acc.colW);
    r.surfel = Pack(q);
    ev.staticEvidence = (uint16_t)stat; ev.varianceQ = EncodeLog(var, varBase); ev.lastSeenFrame = (uint16_t)(tick & 0xFFFFu);
    r.evidence = ev; r.folded = acc.n; r.changed = true;
    return r;
}

// ---- F3 clustering of an unmatched cell segment (twin: fs_cluster.glsl fsClusterSegment) --------------
// Greedy first-fit in sorted (measurement-sequence) order: a measurement joins the FIRST candidate (creation
// order) that is compatible (normal dot, plane gate, tangent distance <= 2 x footprint); otherwise it seeds a
// new candidate while fewer than FS_CELL_NEW_MAX exist (extra sheets wait for the next epoch, counted by the
// caller); at most FS_SEG_CLUSTER_MAX measurements are examined (the rest set `overflowed`). Candidate ids are
// idBase + exclusive prefix over segments in sorted order (fuse_prefix / fuse_cluster_write).
struct Cand { V3 p, n; float wN = 0, wT = 0; V3 nSum; float mxx = 0, myy = 0, mxy = 0, d2 = 0, fp = 0; uint32_t cnt = 0; float sigmaN = 0, sigmaT = 0; V3 col; float colW = 0; };
inline uint32_t ClusterSegment(const std::vector<FsSurfaceMeasurement>& seg, V3 origin, Cand cands[FS_CELL_NEW_MAX], bool& overflowed) {
    uint32_t nc = 0; overflowed = false;
    for (size_t k = 0; k < seg.size(); ++k) {
        if (k == FS_SEG_CLUSTER_MAX) { overflowed = true; break; }
        const FsSurfaceMeasurement& m = seg[k];
        V3 pm = MeasPos(m) - origin, nm = MeasNormal(m);
        float sN = MeasSigmaN(m), sT = MeasSigmaT(m), fp = MeasFootprint(m);
        float wN = 1.f / (sN * sN), wT = 1.f / (sT * sT);
        int hit = -1;
        for (uint32_t c = 0; c < nc; ++c) {
            const Cand& q = cands[c];
            if (Dot(q.n, nm) < (float)FS_ASSOC_MIN_DOT) continue;
            V3 delta = pm - q.p; float d = Dot(q.n, delta);
            float gate = (float)FS_ASSOC_SIGMA_GATE * sqrtf(q.sigmaN * q.sigmaN + sN * sN);
            if (fabsf(d) > gate) continue;
            V3 tv = delta - q.n * d;
            if (Dot(tv, tv) > 4.f * fp * fp) continue;
            hit = (int)c; break;
        }
        if (hit < 0) {
            if (nc >= FS_CELL_NEW_MAX) continue;
            Cand q; q.p = pm; q.n = nm; q.wN = wN; q.wT = wT; q.nSum = nm * wN; q.mxx = fp * fp; q.myy = fp * fp; q.mxy = 0; q.d2 = 0; q.fp = fp; q.cnt = 1; q.sigmaN = sN; q.sigmaT = sT;
            if (m.reserved & FS_MEAS_COLOR_VALID) { q.col = ColorOf(m.reserved); q.colW = 1.f; }
            cands[nc++] = q;
        } else {
            Cand& q = cands[hit];
            V3 delta = pm - q.p; float d = Dot(q.n, delta);
            float aN = wN / (q.wN + wN), aT = wT / (q.wT + wT);
            V3 tv = delta - q.n * d;
            q.p = q.p + q.n * (d * aN) + tv * aT;
            q.wN += wN; q.wT += wT; q.nSum = q.nSum + nm * wN; q.cnt++;
            q.d2 += d * d; q.fp = fmaxf(q.fp, fp);
            V3 t1, t2; Frame(q.n, t1, t2);
            float tvx = Dot(tv, t1), tvy = Dot(tv, t2);
            q.mxx += tvx * tvx + fp * fp; q.myy += tvy * tvy + fp * fp; q.mxy += tvx * tvy;
            q.sigmaN = fmaxf(sqrtf(1.f / q.wN), (float)FS_SIGMA_N_FLOOR_M); q.sigmaT = fmaxf(sqrtf(1.f / q.wT), (float)FS_SIGMA_T_FLOOR_M);
            if (m.reserved & FS_MEAS_COLOR_VALID) { q.col = q.col + ColorOf(m.reserved); q.colW += 1.f; }
        }
    }
    return nc;
}

// ---- F0b free-space DDA (twin: fuse_freespace.comp) ----------------------------------------------------
// `lookup(key, slot)` = page hash; stamps (slot, cell) pairs in walk order. Bounded by FS_FREE_WALK_MAX steps,
// FS_FREE_MAX_RANGE_M and FS_FREE_PAGE_HOPS_MAX logical page changes (review gap 6: a ray never touches more than
// FS_FREE_PAGE_HOPS_MAX pages; the overflow is counted, never silently continued).
struct FreeWalk { std::vector<std::pair<uint32_t, uint32_t>> stamped; uint32_t hops = 0; bool hopOverflow = false; bool skipped = false; uint8_t stamp = 0; };
inline FreeWalk FreeSpaceWalk(V3 eye, V3 p, int32_t anchorId, uint32_t tick, const std::function<bool(const FsPageKey&, uint32_t&)>& lookup) {
    FreeWalk w; w.stamp = (uint8_t)(1u + (tick % 255u));
    V3 dir = p - eye; float len = Len(dir);
    if (len < (float)FS_FREE_STEP_M * 2.f || len > (float)FS_FREE_MAX_RANGE_M) { w.skipped = true; return w; }
    dir = dir * (1.f / len);
    float tEnd = len - (float)FS_FREE_END_CLEARANCE_M;
    FsPageKey key; key.anchorId = anchorId; key.x = 0x7FFFFFFF; key.y = 0; key.z = 0;
    const int32_t hpx = PageCoord(p.x), hpy = PageCoord(p.y), hpz = PageCoord(p.z);
    FsPageKey hk; hk.anchorId = anchorId; hk.x = hpx; hk.y = hpy; hk.z = hpz;
    const uint32_t hitCell = CellOfV(p - PageOrigin3(hk));
    uint32_t slot = FS_INDEX_NONE; bool haveSlot = false; uint32_t lastCell = 0xFFFFFFFFu;
    for (uint32_t i = 0; i < FS_FREE_WALK_MAX; ++i) {
        float tt = ((float)i + 0.5f) * (float)FS_FREE_STEP_M;
        if (tt >= tEnd) break;
        V3 q = eye + dir * tt;
        int32_t kx = PageCoord(q.x), ky = PageCoord(q.y), kz = PageCoord(q.z);
        if (kx != key.x || ky != key.y || kz != key.z) {
            if (w.hops >= FS_FREE_PAGE_HOPS_MAX) { w.hopOverflow = true; break; }
            w.hops++; key.x = kx; key.y = ky; key.z = kz;
            haveSlot = lookup(key, slot); lastCell = 0xFFFFFFFFu;
        }
        if (!haveSlot) continue;
        uint32_t cell = CellOfV(q - PageOrigin3(key));
        if (cell != lastCell) {
            lastCell = cell;
            if (cell == hitCell && kx == hpx && ky == hpy && kz == hpz) continue;
            w.stamped.push_back({slot, cell});
        }
    }
    return w;
}

// ---- adaptive index (twin: fs_index.glsl on the word arena; owner-only mutation, no atomics) -----------
// Leaves: FS_INDEX_LEAF_WORDS words {count, next, handles[14]}; nodes: 8 child entries; per page a 32^3 directory.
constexpr uint32_t kIndexLeafWords = sizeof(FsIndexLeaf) / 4;
class IndexRef {
public:
    struct Counters { uint32_t leafSplits = 0, overflow = 0, poolLeafEmpty = 0, poolNodeEmpty = 0; };
    void Init(uint32_t pages, uint32_t leaves, uint32_t nodes, const std::vector<FsSurfel>* surfels) {
        dir_.assign((size_t)pages * FS_CELLS_PER_PAGE, 0u); leaf_.assign((size_t)leaves * kIndexLeafWords, 0u); node_.assign((size_t)nodes * 8, 0u);
        freeLeaf_.clear(); for (uint32_t i = leaves; i > 0; --i) freeLeaf_.push_back(i - 1);
        freeNode_.clear(); for (uint32_t i = nodes; i > 0; --i) freeNode_.push_back(i - 1);
        surfels_ = surfels; ctr_ = Counters{}; retiredLeaves_.clear();
    }
    uint32_t& Dir(uint32_t page, uint32_t cell) { return dir_[(size_t)page * FS_CELLS_PER_PAGE + cell]; }
    uint32_t& LeafWord(uint32_t leaf, uint32_t w) { return leaf_[(size_t)leaf * kIndexLeafWords + w]; }
    uint32_t& NodeWord(uint32_t node, uint32_t child) { return node_[(size_t)node * 8 + child]; }
    const Counters& Ctr() const { return ctr_; }
    uint32_t FreeLeaves() const { return (uint32_t)freeLeaf_.size(); }
    uint32_t FreeNodes() const { return (uint32_t)freeNode_.size(); }
    const std::vector<uint32_t>& RetiredLeaves() const { return retiredLeaves_; }
    void FreeLeaf(uint32_t id) { freeLeaf_.push_back(id); }
    uint32_t FindLeaf(uint32_t page, uint32_t cell, V3 l) {
        uint32_t e = Dir(page, cell);
        float bmin[3], bmax[3]; CellBounds(cell, bmin, bmax);
        for (uint32_t d = 0; d < FS_INDEX_MAX_DEPTH; ++d) {
            if (!EntryIsNode(e)) break;
            float lp[3] = {l.x, l.y, l.z}; uint32_t o = OctantOfBox(lp, bmin, bmax);
            e = NodeWord(EntryId(e), o);
        }
        return EntryIsLeaf(e) ? EntryId(e) : FS_INDEX_NONE;
    }
    bool Insert(uint32_t page, uint32_t cell, V3 l, uint32_t h) {
        uint32_t* parent = &Dir(page, cell);
        uint32_t e = *parent;
        float bmin[3], bmax[3]; CellBounds(cell, bmin, bmax);
        for (uint32_t d = 0; d <= FS_INDEX_MAX_DEPTH; ++d) {
            if (e == 0u) { uint32_t nl = NewLeaf(h); if (nl == FS_INDEX_NONE) return false; *parent = LeafEntry(nl); return true; }
            if (EntryIsLeaf(e)) {
                uint32_t leaf = EntryId(e), cnt = LeafWord(leaf, 0);
                if (cnt < FS_INDEX_LEAF_CAP || d == FS_INDEX_MAX_DEPTH) return ChainAppend(leaf, h);
                if (freeNode_.empty()) { ctr_.poolNodeEmpty++; return false; }
                uint32_t node = freeNode_.back(); freeNode_.pop_back();
                for (uint32_t k = 0; k < 8; ++k) NodeWord(node, k) = 0u;
                for (uint32_t i = 0; i < cnt && i < FS_INDEX_LEAF_CAP; ++i) {
                    uint32_t hh = LeafWord(leaf, 2 + i);
                    V3 pl = LocalPos((*surfels_)[hh]);
                    float b0[3] = {bmin[0], bmin[1], bmin[2]}, b1[3] = {bmax[0], bmax[1], bmax[2]}; float lp[3] = {pl.x, pl.y, pl.z};
                    uint32_t o = OctantOfBox(lp, b0, b1);
                    uint32_t ce = NodeWord(node, o);
                    if (ce == 0u) { uint32_t nl = NewLeaf(hh); if (nl == FS_INDEX_NONE) return false; NodeWord(node, o) = LeafEntry(nl); }
                    else if (!ChainAppend(EntryId(ce), hh)) return false;
                }
                retiredLeaves_.push_back(leaf); ctr_.leafSplits++;
                *parent = NodeEntry(node); e = NodeEntry(node);
            }
            float lp[3] = {l.x, l.y, l.z}; uint32_t o = OctantOfBox(lp, bmin, bmax);
            parent = &NodeWord(EntryId(e), o); e = *parent;
        }
        ctr_.overflow++;
        return false;
    }
    bool Remove(uint32_t page, uint32_t cell, V3 l, uint32_t h) {
        uint32_t leaf = FindLeaf(page, cell, l);
        for (uint32_t c = 0; c < FS_INDEX_CHAIN_MAX; ++c) {
            if (leaf == FS_INDEX_NONE) return false;
            uint32_t cnt = LeafWord(leaf, 0);
            for (uint32_t i = 0; i < cnt && i < FS_INDEX_LEAF_CAP; ++i)
                if (LeafWord(leaf, 2 + i) == h) { LeafWord(leaf, 2 + i) = LeafWord(leaf, 2 + cnt - 1); LeafWord(leaf, 0) = cnt - 1; return true; }
            leaf = LeafWord(leaf, 1);
        }
        return false;
    }
    // Deterministic leaf order of a cell subtree (twin: fsCollectLeaves: depth-first, octant order, chains in order).
    std::vector<uint32_t> CollectLeaves(uint32_t page, uint32_t cell) {
        std::vector<uint32_t> out; std::vector<uint32_t> stack;
        uint32_t e = Dir(page, cell); if (e == 0u) return out; stack.push_back(e);
        for (uint32_t it = 0; it < 256 && !stack.empty(); ++it) {
            e = stack.back(); stack.pop_back();
            if (EntryIsLeaf(e)) { uint32_t leaf = EntryId(e); for (uint32_t c = 0; c < FS_INDEX_CHAIN_MAX && leaf != FS_INDEX_NONE && out.size() < 64; ++c) { out.push_back(leaf); leaf = LeafWord(leaf, 1); } }
            else if (EntryIsNode(e)) { for (int k = 7; k >= 0; --k) { uint32_t ce = NodeWord(EntryId(e), (uint32_t)k); if (ce != 0u && stack.size() < 32) stack.push_back(ce); } }
        }
        return out;
    }
    std::vector<uint32_t> Handles(uint32_t page, uint32_t cell) {
        std::vector<uint32_t> out;
        for (uint32_t leaf : CollectLeaves(page, cell)) { uint32_t cnt = LeafWord(leaf, 0); for (uint32_t i = 0; i < cnt && i < FS_INDEX_LEAF_CAP; ++i) out.push_back(LeafWord(leaf, 2 + i)); }
        return out;
    }
private:
    uint32_t NewLeaf(uint32_t h) {
        if (freeLeaf_.empty()) { ctr_.poolLeafEmpty++; return FS_INDEX_NONE; }
        uint32_t leaf = freeLeaf_.back(); freeLeaf_.pop_back();
        LeafWord(leaf, 0) = 1u; LeafWord(leaf, 1) = FS_INDEX_NONE; LeafWord(leaf, 2) = h;
        return leaf;
    }
    bool ChainAppend(uint32_t leaf, uint32_t h) {
        uint32_t cur = leaf;
        for (uint32_t c = 0; c < FS_INDEX_CHAIN_MAX; ++c) {
            uint32_t cnt = LeafWord(cur, 0);
            if (cnt < FS_INDEX_LEAF_CAP) { LeafWord(cur, 2 + cnt) = h; LeafWord(cur, 0) = cnt + 1; return true; }
            uint32_t nx = LeafWord(cur, 1);
            if (nx == FS_INDEX_NONE) {
                if (c + 1 >= FS_INDEX_CHAIN_MAX) break;
                uint32_t nl = NewLeaf(h); if (nl == FS_INDEX_NONE) return false;
                LeafWord(cur, 1) = nl; return true;
            }
            cur = nx;
        }
        ctr_.overflow++;
        return false;
    }
    std::vector<uint32_t> dir_, leaf_, node_, freeLeaf_, freeNode_, retiredLeaves_;
    const std::vector<FsSurfel>* surfels_ = nullptr; Counters ctr_;
};

// ---- F4 maintenance rules (twin: fs_maint.glsl) --------------------------------------------------------
inline bool SplitWanted(const Patch& q, const FsSurfelEvidence& ev) {
    uint32_t cnt = q.flags & kEvidenceCountMask;
    if (cnt < FS_SPLIT_MIN_SUPPORT || (q.flags & kFlagPromoted) == 0) return false;
    float var = DecodeLog(ev.varianceQ, (float)FS_VAR_NORM_BASE);
    return sqrtf(var) > (float)FS_SPLIT_VAR_K && q.rM > 2.f * (float)FS_RADIUS_MIN_M;
}
// Merge priority: higher static evidence, then lower sigma, then lower SurfaceID survives.
inline bool Survives(const Patch& a, const FsSurfelEvidence& ea, const Patch& b, const FsSurfelEvidence& eb) {
    if (ea.staticEvidence != eb.staticEvidence) return ea.staticEvidence > eb.staticEvidence;
    if (a.sigmaN != b.sigmaN) return a.sigmaN < b.sigmaN;
    return a.surfaceId < b.surfaceId;
}
inline bool Mergeable(const Patch& a, const Patch& b) {
    if ((a.flags & kFlagPromoted) == 0 || (b.flags & kFlagPromoted) == 0) return false;
    if (Dot(a.n, b.n) < (float)FS_MERGE_MIN_DOT) return false;
    V3 delta = b.p - a.p; float d = Dot(a.n, delta);
    if (fabsf(d) > (float)FS_ASSOC_SIGMA_GATE * sqrtf(a.sigmaN * a.sigmaN + b.sigmaN * b.sigmaN)) return false;
    return Len(delta - a.n * d) <= (float)FS_MERGE_OVERLAP_K * (a.rM + b.rM);
}

// ---- dirty list (twin: fuse_dirty_pages / prefix / emit) ------------------------------------------------
// Deterministic order: pages ascending, cells ascending; entries beyond `cap` stay set in the mask (sliced).
inline std::vector<uint32_t> DirtyListEmit(std::vector<std::vector<uint32_t>>& masks, uint32_t cap) {
    std::vector<uint32_t> list;
    for (uint32_t page = 0; page < masks.size(); ++page)
        for (uint32_t w = 0; w < masks[page].size(); ++w) {
            uint32_t bits = masks[page][w], keep = 0;
            while (bits) {
                uint32_t bit = (uint32_t)__builtin_ctz(bits); bits &= bits - 1;
                if (list.size() < cap) list.push_back((page << 15) | (w * 32 + bit)); else keep |= 1u << bit;
            }
            masks[page][w] = keep;
        }
    return list;
}

} // namespace world
} // namespace fs
