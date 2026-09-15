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
inline float PlaneGate(float sigmaA, float sigmaB) { return fminf((float)FS_ASSOC_SIGMA_GATE * sqrtf(sigmaA * sigmaA + sigmaB * sigmaB), (float)FS_ASSOC_PLANE_MAX_M); }
// Returns true when the surfel is compatible with the measurement; `score` is the Mahalanobis-like rank.
inline bool AssocCompatible(const FsSurfel& s, V3 local, V3 n, float sigmaNm, float footprint, float& score) {
    if (s.evidenceFlags & kFlagRemoved) return false;
    V3 sp = LocalPos(s), sn = Normal(s);
    if (Dot(sn, n) < (float)FS_ASSOC_MIN_DOT) return false;
    V3 delta = local - sp; float d = Dot(sn, delta);
    float gate = PlaneGate(DecodeLogSigma(s.sigmaN), sigmaNm);
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

// ---- F2 segmented reduce (twin: fs_fusion.glsl FsAccum + fuse_reduce.comp, C09R-E4) -----------------------
struct Accum { float wN = 0, dN = 0; V3 nSum; float wT = 0; float tx = 0, ty = 0; float mxx = 0, myy = 0, mxy = 0, d2 = 0, s2 = 0, h = 0, fp = 0; V3 col; float colW = 0; uint32_t srcAnd = 0xFFFFFFFFu, obs = 0, n = 0;
               uint32_t oCnt[2] = {0, 0}; V3 oP[2], oN[2]; float oFp[2] = {0, 0}, oS[2] = {0, 0}, worstD = 0; uint32_t worstSide = 0; };   // E4.1C unexplained surface per residual sign
inline V3 ColorOf(uint32_t w) { return v3((float)(w & 0xFFu), (float)((w >> 8) & 0xFFu), (float)((w >> 16) & 0xFFu)) * (1.f / 255.f); }
inline uint32_t ColorPack(V3 c) { auto q = [](float x) { x = x < 0.f ? 0.f : (x > 1.f ? 1.f : x); return (uint32_t)(x * 255.f + 0.5f); }; return q(c.x) | (q(c.y) << 8) | (q(c.z) << 16); }
inline uint32_t AppearanceObs(uint32_t w) { return (w & FS_APPEARANCE_MEASURED) ? (w & FS_APPEARANCE_OBS_MASK) >> FS_APPEARANCE_OBS_SHIFT : 0u; }
inline bool AppearanceConfirmed(uint32_t w) { return AppearanceObs(w) >= FS_APPEARANCE_CONFIRM_OBS; }
inline uint32_t AppearanceWord(V3 c, uint32_t obs) { return FS_APPEARANCE_MEASURED | (std::min<uint32_t>(obs, 15u) << FS_APPEARANCE_OBS_SHIFT) | ColorPack(c); }
inline uint32_t BlendAppearance(uint32_t old, V3 meanCol, float wPrior, float wMeas) {
    if ((old & FS_APPEARANCE_MEASURED) == 0u) return AppearanceWord(meanCol, 1u);
    V3 o = ColorOf(old); float t = wMeas / (wPrior + wMeas);
    return AppearanceWord(o + (meanCol - o) * t, AppearanceObs(old) + 1u);
}
inline float Huber(float d, float sigmaComb) { float ad = fabsf(d), c = (float)FS_HUBER_K * sigmaComb; return ad <= c ? 1.f : c / fmaxf(ad, 1e-12f); }
inline float SigmaFloor(uint32_t srcAnd) { return (srcAnd & 4u) ? fmaxf((float)FS_SIGMA_N_FLOOR_M, (float)FS_DEPTH_PRIOR_SIGMA_FLOOR_M) : (float)FS_SIGMA_N_FLOOR_M; }
inline void AccumAdd(Accum& a, const Patch& q, V3 t1, V3 t2, V3 pm, V3 nm, float sigmaNm, float sigmaTm, float footprint, uint32_t colorWord, uint32_t srcFlags, uint32_t obs) {
    V3 delta = pm - q.p; float d = Dot(q.n, delta);
    float sComb = sqrtf(q.sigmaN * q.sigmaN + sigmaNm * sigmaNm);
    float hw = Huber(d, sComb);
    if (fabsf(d) > (float)FS_REFINE_OUTLIER_K * sComb) {
        uint32_t side = d >= 0.f ? 0u : 1u;
        a.oCnt[side]++; a.oP[side] = a.oP[side] + pm; a.oN[side] = a.oN[side] + nm; a.oFp[side] = fmaxf(a.oFp[side], footprint); a.oS[side] += sigmaNm;
        if (fabsf(d) > a.worstD) { a.worstD = fabsf(d); a.worstSide = side; }
    }
    float wN = hw / (sigmaNm * sigmaNm), wT = hw / (sigmaTm * sigmaTm);
    float tvx = Dot(delta, t1), tvy = Dot(delta, t2);
    a.wN += wN; a.dN += wN * d; a.nSum = a.nSum + nm * wN;
    a.wT += wT; a.tx += tvx * wT; a.ty += tvy * wT;
    float f2 = footprint * footprint;
    a.mxx += wT * (tvx * tvx + f2); a.myy += wT * (tvy * tvy + f2); a.mxy += wT * tvx * tvy;
    a.d2 += hw * d * d; a.s2 += hw * sigmaNm * sigmaNm; a.h += hw; a.fp = fmaxf(a.fp, footprint); a.n++;
    a.srcAnd &= srcFlags; a.obs = obs;
    if (colorWord & FS_MEAS_COLOR_VALID) { a.col = a.col + ColorOf(colorWord); a.colW += 1.f; }
}
// ---- E6R evidence hysteresis (twins: fsEvidencePositive / fsEvidenceCharge / maintenance + topology transitions) ----------------------
inline uint32_t EvidencePositive(FsSurfelEvidence& e, uint16_t viewBit) {
    uint32_t r = 0;
    e.positiveSupport = (uint16_t)std::min<uint32_t>(e.positiveSupport + 1u, 65535u);
    if (e.contradictionDebt > 0) { e.contradictionDebt = (uint16_t)(e.contradictionDebt > FS_DEBT_HEAL ? e.contradictionDebt - FS_DEBT_HEAL : 0); r |= 1u; }
    e.viewDiversity |= viewBit;
    const uint32_t st = e.lifecycle & 3u;
    if (st == FS_LIFE_SUSPECT && e.contradictionDebt < FS_DEBT_SUSPECT / 2) { e.lifecycle = (uint16_t)((e.lifecycle & 0xFF00u) | FS_LIFE_ACTIVE); e.contradictionViews = 0; r |= 2u; }
    else if (st == FS_LIFE_RETIRING && e.contradictionDebt < FS_DEBT_RETIRE / 2) { e.lifecycle = (uint16_t)((e.lifecycle & 0xFF00u) | FS_LIFE_SUSPECT); r |= 2u; }
    return r;
}
// free-space narrow phase (one ray): weighted hit, observation id, ray sector
inline void EvidenceRay(FsSurfelEvidence& e, bool precise, uint32_t obs, uint16_t viewBit) { e.contradictionHits += precise ? FS_DEBT_W_PRECISE : FS_DEBT_W_PRIOR; e.lastContradictionObs = obs; e.contradictionViews |= viewBit; }
inline bool EvidenceCharge(FsSurfelEvidence& e) {
    const uint32_t hits = e.contradictionHits;
    if (!hits) return false;
    e.contradictionHits = 0;
    if (e.lastContradictionObs == e.lastDebtObs && e.lastContradictionObs != 0) return false;
    e.lastDebtObs = e.lastContradictionObs;
    e.contradictionDebt = (uint16_t)std::min<uint32_t>(e.contradictionDebt + std::min<uint32_t>(hits, FS_DEBT_MAX_PER_OBS), 65535u);
    return true;
}
// free-space narrow phase (twin: fuse_freespace.comp): does the ray eye -> measured point (length len) cross the support ellipse of
// patch b (world centre c) in front of the measurement, beyond the clearance and both sigmas, not grazing?
inline bool RayContradictsSupport(const Patch& b, V3 c, V3 eye, V3 dir, float len, float sigmaMeas) {
    const float dn = Dot(b.n, dir);
    if (fabsf(dn) < (float)FS_FREE_GRAZE_COS) return false;
    const float th = Dot(b.n, c - eye) / dn;
    const float guard = fmaxf((float)FS_FREE_END_CLEARANCE_M, 3.f * sqrtf(b.sigmaN * b.sigmaN + sigmaMeas * sigmaMeas));
    if (th <= (float)FS_FREE_STEP_M || th >= len - guard) return false;
    V3 hp = eye + dir * th - c, f1, f2; Frame(b.n, f1, f2);
    const V3 tM = f1 * cosf(b.angle) + f2 * sinf(b.angle), tm = Cross(b.n, tM);
    const float u = Dot(hp, tM) / fmaxf(b.rM, 1e-4f), v = Dot(hp, tm) / fmaxf(b.rm, 1e-4f);
    return u * u + v * v <= 1.f;
}
enum LifeOutcome : uint32_t { LIFE_NONE = 0, LIFE_SUSPECT, LIFE_REMOVED, LIFE_RETIRE_PENDING };
// maintenance transition of a promoted surfel after a charge (twin: fuse_maint_apply.comp)
inline LifeOutcome MaintTransition(FsSurfelEvidence& e, uint16_t tick16) {
    const uint32_t st = e.lifecycle & 3u, debt = e.contradictionDebt;
    if (st == FS_LIFE_ACTIVE && debt >= FS_DEBT_SUSPECT) { e.lifecycle = (uint16_t)((e.lifecycle & 0xFF00u) | FS_LIFE_SUSPECT); e.suspectSince = tick16; return LIFE_SUSPECT; }
    if (st == FS_LIFE_SUSPECT && debt >= FS_DEBT_RETIRE) return LIFE_RETIRE_PENDING;
    if (st == FS_LIFE_RETIRING && debt >= FS_DEBT_REMOVE) return LIFE_REMOVED;
    return LIFE_NONE;
}
// topology retirement decision (twin: sheet_fit.comp): neighbour states of the mutual ring
inline bool TopologyRetire(const FsSurfelEvidence& e, uint16_t tick16, const std::vector<uint32_t>& neighbourStates) {
    if ((e.lifecycle & 3u) != FS_LIFE_SUSPECT || e.contradictionDebt < FS_DEBT_RETIRE) return false;
    if ((uint32_t)__builtin_popcount(e.contradictionViews) < FS_RETIRE_MIN_VIEWS) return false;
    if ((uint16_t)(tick16 - e.suspectSince) < FS_SUSPECT_MIN_TICKS) return false;
    uint32_t bad = 0; for (uint32_t s : neighbourStates) if (s != FS_LIFE_ACTIVE) bad++;
    return neighbourStates.empty() || (float)bad >= (float)FS_RETIRE_NEIGHBOUR_FRACTION * (float)neighbourStates.size();
}

struct ReduceResult { FsSurfel surfel; FsSurfelEvidence evidence; uint32_t folded = 0; bool overflow = false; bool changed = false; bool duplicate = false; bool relocated = false; uint32_t newCell = 0;
                      bool refine = false; V3 siteP, siteN; float siteFp = 0.f, siteSigma = 0.f; };   // E4.1C refinement request (twin: fuse_reduce.comp)
// One matched segment = one surfel; `seg` in sorted order, all from ONE observation (one epoch ingests one frame).
inline ReduceResult ReduceSegment(const FsSurfel& s, const FsSurfelEvidence& evIn, const std::vector<FsSurfaceMeasurement>& seg, V3 origin, uint32_t tick) {
    ReduceResult r; r.surfel = s; r.evidence = evIn;
    Patch q = Unpack(s);
    if (q.flags & kFlagRemoved) return r;
    V3 t1, t2; Frame(q.n, t1, t2);
    Accum acc;
    for (size_t k = 0; k < seg.size(); ++k) {
        if (k == FS_SEG_REDUCE_MAX) { r.overflow = true; break; }
        const FsSurfaceMeasurement& m = seg[k];
        AccumAdd(acc, q, t1, t2, MeasPos(m) - origin, MeasNormal(m), MeasSigmaN(m), MeasSigmaT(m), MeasFootprint(m), m.reserved, m.sourceFlags, m.observationId);
    }
    if (acc.n == 0 || acc.h <= 0.f) return r;
    { const uint32_t ws = acc.worstSide, oc = acc.oCnt[ws];
      if (oc >= FS_REFINE_MIN_OUTLIERS && (q.flags & kFlagPromoted) && (q.flags & kEvidenceCountMask) >= FS_REFINE_MIN_SUPPORT) {
          r.refine = true; r.siteP = acc.oP[ws] * (1.f / (float)oc); r.siteN = Norm(acc.oN[ws]); r.siteFp = acc.oFp[ws]; r.siteSigma = acc.oS[ws] / (float)oc; } }
    if (evIn.lastObservationId == acc.obs && acc.obs != 0u) { r.duplicate = true; return r; }
    const float nEff = acc.h;
    float wObsN = acc.wN / nEff, wObsT = acc.wT / nEff;
    float dMean = acc.dN / acc.wN, tMx = acc.tx / acc.wT, tMy = acc.ty / acc.wT;
    V3 nObs = Norm(acc.nSum);
    float sxx = fmaxf(acc.mxx / acc.wT - tMx * tMx, 0.f), syy = fmaxf(acc.myy / acc.wT - tMy * tMy, 0.f), sxy = acc.mxy / acc.wT - tMx * tMy;
    float wNs = 1.f / (q.sigmaN * q.sigmaN), wTs = 1.f / (q.sigmaT * q.sigmaT);
    float aN = wObsN / (wNs + wObsN), aT = wObsT / (wTs + wObsT);
    V3 np = q.p + q.n * (dMean * aN) + (t1 * tMx + t2 * tMy) * aT;
    V3 nn = Norm(q.n * wNs + nObs * wObsN);
    float newSigmaN = fmaxf(sqrtf(1.f / (wNs + wObsN)), SigmaFloor(acc.srcAnd));
    float newSigmaT = fmaxf(sqrtf(1.f / (wTs + wObsT)), (float)FS_SIGMA_T_FLOOR_M);
    uint32_t cnt = q.flags & kEvidenceCountMask;
    float ma, mb, mc; PatchMoment(q, ma, mb, mc);
    float wPrior = (float)(cnt > 1 ? cnt : 1);
    float rM, rm, ang; MomentToEllipse((ma * wPrior + sxx) / (wPrior + 1.f), (mb * wPrior + syy) / (wPrior + 1.f), (mc * wPrior + sxy) / (wPrior + 1.f), rM, rm, ang);
    rM = fminf(fmaxf(fmaxf(rM, acc.fp), (float)FS_RADIUS_MIN_M), (float)FS_RADIUS_MAX_M); rm = fminf(fmaxf(rm, (float)FS_RADIUS_MIN_M), rM);
    const float hh = (float)FS_PAGE_EXTENT_M * 0.5f - 0.0005f;
    np = ClampV(np, v3(-hh, -hh, -hh), v3(hh, hh, hh));                              // representable-range guard only
    const uint32_t oldCell = CellOfV(q.p), newCell = CellOfV(np);
    r.relocated = newCell != oldCell; r.newCell = newCell;
    FsSurfelEvidence ev = evIn;
    const float varBase = (float)FS_VAR_NORM_BASE;
    float var = DecodeLog(ev.varianceQ, varBase);
    float meanD2 = (acc.d2 / nEff) / fmaxf(acc.s2 / nEff, 1e-12f);
    var += (meanD2 - var) / (float)(cnt + 1);
    EvidencePositive(ev, 0u);
    uint32_t stat = ev.positiveSupport;
    uint32_t flags = q.flags;
    cnt = std::min<uint32_t>(cnt + 1, FS_EVIDENCE_COUNT_MAX);
    if ((flags & kFlagTransient) && stat >= FS_PROMOTE_STATIC) flags = (flags & ~(uint32_t)kFlagTransient) | kFlagPromoted;
    q.p = np; q.n = nn; q.angle = ang; q.rM = rM; q.rm = rm; q.sigmaN = newSigmaN; q.sigmaT = newSigmaT;
    q.flags = (flags & ~(uint32_t)kEvidenceCountMask) | cnt;
    if (acc.colW > 0.f) q.appearance = BlendAppearance(q.appearance, acc.col * (1.f / acc.colW), wPrior, 1.f);
    r.surfel = Pack(q);
    ev.varianceQ = EncodeLog(var, varBase); ev.lastSeenFrame = (uint16_t)(tick & 0xFFFFu); ev.lastObservationId = acc.obs;
    r.evidence = ev; r.folded = acc.n; r.changed = true;
    return r;
}

// ---- F3 clustering of an unmatched cell segment (twin: fs_cluster.glsl, C09R-E4) --------------------------
// Greedy first-fit in sorted order against each candidate's CURRENT mean (plain seed-frame sums: exact Welford mean and
// covariance), mean normal, bounded plane gate, association reach; <= FS_CELL_NEW_MAX candidates, <= FS_SEG_CLUSTER_MAX
// measurements examined. Candidate ids = idBase + exclusive prefix. All members are one observation.
struct Cand {
    V3 p0, n0, t1, t2; float c = 0, sd = 0, sdd = 0, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sfp2 = 0; V3 nSum; float wN = 0, wT = 0, fp = 0; V3 col; float colW = 0; uint32_t srcAnd = 0xFFFFFFFFu;
    V3 Mean() const { return p0 + n0 * (sd / c) + t1 * (sx / c) + t2 * (sy / c); }
    V3 NormalMean() const { float l = Len(nSum); return l > 1e-12f ? nSum * (1.f / l) : n0; }
    float SigmaN() const { return sqrtf(c / fmaxf(wN, 1e-12f)); }
    float SigmaT() const { return sqrtf(c / fmaxf(wT, 1e-12f)); }
    void Moments(float& mxx, float& myy, float& mxy, float& varNorm) const {
        float mx = sx / c, my = sy / c, md = sd / c, f2 = sfp2 / c;
        mxx = fmaxf(sxx / c - mx * mx, 0.f) + f2; myy = fmaxf(syy / c - my * my, 0.f) + f2; mxy = sxy / c - mx * my;
        float s = SigmaN(); varNorm = fmaxf(sdd / c - md * md, 0.f) / fmaxf(s * s, 1e-12f);
    }
    void Add(V3 pm, V3 nm, float sN, float sT, float f, uint32_t colorWord, uint32_t src) {
        V3 o = pm - p0; float ox = Dot(o, t1), oy = Dot(o, t2), od = Dot(o, n0);
        c += 1.f; sd += od; sdd += od * od; sx += ox; sy += oy; sxx += ox * ox; syy += oy * oy; sxy += ox * oy; sfp2 += f * f;
        nSum = nSum + nm * (1.f / (sN * sN)); wN += 1.f / (sN * sN); wT += 1.f / (sT * sT); fp = fmaxf(fp, f); srcAnd &= src;
        if (colorWord & FS_MEAS_COLOR_VALID) { col = col + ColorOf(colorWord); colW += 1.f; }
    }
};
inline uint32_t ClusterSegment(const std::vector<FsSurfaceMeasurement>& seg, V3 origin, Cand cands[FS_CELL_NEW_MAX], bool& overflowed) {
    uint32_t nc = 0; overflowed = false;
    for (size_t k = 0; k < seg.size(); ++k) {
        if (k == FS_SEG_CLUSTER_MAX) { overflowed = true; break; }
        const FsSurfaceMeasurement& m = seg[k];
        V3 pm = MeasPos(m) - origin, nm = MeasNormal(m);
        float sN = MeasSigmaN(m), sT = MeasSigmaT(m), fp = MeasFootprint(m);
        int hit = -1;
        for (uint32_t ci = 0; ci < nc; ++ci) {
            const Cand& q = cands[ci];
            V3 nq = q.NormalMean();
            if (Dot(nq, nm) < (float)FS_ASSOC_MIN_DOT) continue;
            V3 delta = pm - q.Mean(); float d = Dot(nq, delta);
            if (fabsf(d) > PlaneGate(q.SigmaN(), sN)) continue;
            V3 tv = delta - nq * d; float reach = fmaxf(2.f * fp, (float)FS_ASSOC_REACH_MIN_M);
            if (Dot(tv, tv) > reach * reach) continue;
            hit = (int)ci; break;
        }
        if (hit < 0) {
            if (nc >= FS_CELL_NEW_MAX) continue;
            Cand q; q.p0 = pm; q.n0 = nm; Frame(nm, q.t1, q.t2);
            q.Add(pm, nm, sN, sT, fp, m.reserved, m.sourceFlags);
            cands[nc++] = q;
        } else cands[hit].Add(pm, nm, sN, sT, fp, m.reserved, m.sourceFlags);
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

// ---- F4 maintenance rules (twin: fs_maint.glsl / fs_fusion.glsl) -------------------------------------------
// Merge priority: higher static evidence, then lower sigma, then lower SurfaceID survives.
inline bool Survives(const Patch& a, const FsSurfelEvidence& ea, const Patch& b, const FsSurfelEvidence& eb) {
    if (ea.positiveSupport != eb.positiveSupport) return ea.positiveSupport > eb.positiveSupport;
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

// ---- C09R-E4.1R surface complex core (twin: fs_sheet.glsl, sheet_graph / sheet_fit / sheet_apply) ---------------------
struct SheetSite { Patch q; V3 world; uint32_t id; FsSurfelEvidence ev; };            // world = page origin + local position
inline bool SheetEdgeR(const Patch& a, V3 pa, const Patch& b, V3 pb, float& dist) {
    dist = 1e30f;
    if (Dot(a.n, b.n) < (float)FS_SHEET_MIN_DOT) return false;
    V3 nm = Norm(a.n + b.n), delta = pb - pa;
    float d = Dot(nm, delta);
    if (fabsf(d) > fminf((float)FS_ASSOC_SIGMA_GATE * sqrtf(a.sigmaN * a.sigmaN + b.sigmaN * b.sigmaN), (float)FS_SHEET_PLANE_MAX_M)) return false;
    dist = Len(delta);
    return dist <= (float)FS_SHEET_LINK_R_M;
}
// Graph pass: the TRUE metric top-K over every candidate of the ball (ties by id), never "the first K found".
inline std::vector<uint32_t> SheetProposals(const SheetSite& a, const std::vector<SheetSite>& candidates) {
    std::vector<std::pair<float, uint32_t>> best;
    for (const SheetSite& b : candidates) {
        if (b.id == a.id || (b.q.flags & (kFlagRemoved | kFlagTransient)) || !(b.q.flags & kFlagPromoted)) continue;
        float dist; if (!SheetEdgeR(a.q, a.world, b.q, b.world, dist)) continue;
        best.push_back({dist, b.id});
    }
    std::sort(best.begin(), best.end());
    if (best.size() > FS_SHEET_K) best.resize(FS_SHEET_K);
    std::vector<uint32_t> out; for (auto& x : best) out.push_back(x.second);
    return out;
}
struct SheetCell { uint32_t word = 0; float rMax = 0.f; float Radius(uint32_t k) const { return rMax * (float)(((word >> (4u * (k & 7u))) & 15u) + 1u) / 16.f; } };
struct SheetDelta { float offset = 0.f; V3 normal; bool flat = false; uint32_t degree = 0; SheetCell cell; float rho[8] = {}; float planeRms = 0.f; float overlap = 0.f, hole = 0.f; std::vector<uint32_t> frontier;
                    uint32_t contractTarget = UINT32_MAX; float contractE = 0.f; };   // E4.1C: survivor id when this node contracts
// Fit pass over the MUTUAL ring (both ends propose each other); proposals of non-mutual neighbours become frontier marks.
inline float Angle8(float x, float y) { float t = atan2f(y, x) / (0.25f * 3.14159265f); return t < 0.f ? t + 8.f : t; }
// Fit pass (twin: sheet_fit.comp); `cellOf` = the stored cells of every site (zero = none yet).
inline SheetDelta SheetFitR(const SheetSite& a, const std::vector<uint32_t>& aProps, const std::vector<SheetSite>& sites, const std::vector<std::vector<uint32_t>>& props, const std::vector<SheetCell>& cellOf) {
    SheetDelta r;
    std::vector<size_t> mut;
    for (uint32_t id : aProps) {
        if (id >= sites.size() || (sites[id].q.flags & (kFlagRemoved | kFlagTransient))) continue;
        if (std::find(props[id].begin(), props[id].end(), a.id) != props[id].end()) mut.push_back(id); else r.frontier.push_back(id);
    }
    r.degree = (uint32_t)mut.size();
    if (mut.empty()) return r;
    std::vector<float> dists; for (size_t id : mut) dists.push_back(Len(sites[id].world - a.world));
    // E4.2R restricted Voronoi cell (twin of sheet_fit.comp)
    V3 ct1, ct2; Frame(a.q.n, ct1, ct2);
    V3 f1, f2; Frame(a.q.n, f1, f2); V3 eM = f1 * cosf(a.q.angle) + f2 * sinf(a.q.angle), em = Cross(a.q.n, eM);
    float rMaxCell = 0.f;
    auto tangentOffset = [&](size_t id) { V3 d = sites[id].world - a.world; return d - a.q.n * Dot(d, a.q.n); };
    for (uint32_t k = 0; k < 8; ++k) {
        float ang = (float)k * 0.25f * 3.14159265f; V3 u = ct1 * cosf(ang) + ct2 * sinf(ang);
        float rr = (float)FS_SHEET_COVER_MAX_M; bool supported = false;
        for (size_t id : mut) { V3 d = tangentOffset(id); float L2 = Dot(d, d), c = Dot(u, d); if (c <= 1e-6f) continue; rr = fminf(rr, 0.5f * L2 / c); if (c >= 0.5f * sqrtf(L2)) supported = true; }
        if (!supported) { float x = Dot(u, eM) / fmaxf(a.q.rM, 1e-5f), y = Dot(u, em) / fmaxf(a.q.rm, 1e-5f); rr = fminf(rr, 1.f / sqrtf(fmaxf(x * x + y * y, 1e-12f))); }
        r.rho[k] = fmaxf(rr, (float)FS_RADIUS_MIN_M); rMaxCell = fmaxf(rMaxCell, r.rho[k]);
    }
    uint32_t word = 0; for (uint32_t k = 0; k < 8; ++k) { int q = (int)ceilf(r.rho[k] / rMaxCell * 16.f) - 1; word |= (uint32_t)(q < 0 ? 0 : (q > 15 ? 15 : q)) << (4u * k); }
    for (size_t k = 0; k < mut.size(); ++k) {
        V3 d = tangentOffset(mut[k]); float L = Len(d); if (L < 1e-6f) continue;
        SheetCell mine{word, rMaxCell};
        float ra = mine.Radius((uint32_t)floorf(Angle8(Dot(d, ct1), Dot(d, ct2)) + 0.5f));
        const SheetSite& b = sites[mut[k]]; float rb = b.q.rM;
        if (cellOf[mut[k]].rMax > 0.f) { V3 bt1, bt2; Frame(b.q.n, bt1, bt2); rb = cellOf[mut[k]].Radius((uint32_t)floorf(Angle8(-Dot(d, bt1), -Dot(d, bt2)) + 0.5f)); }
        float gap = L - ra - rb, w = 2.f * fminf(ra, rb);
        if (gap < 0.f) r.overlap += -gap * w; else r.hole += gap * w;
    }
    if (r.degree >= FS_SHEET_CELL_MIN_DEGREE) r.cell = SheetCell{word, rMaxCell};
    const float L2 = (float)(FS_SHEET_LINK_R_M * FS_SHEET_LINK_R_M);
    float wA = 1.f / (a.q.sigmaN * a.q.sigmaN);
    V3 cSum = a.world * wA, nSum = a.q.n * wA; float wSum = wA, s2Sum = wA * a.q.sigmaN * a.q.sigmaN;
    for (size_t k = 0; k < mut.size(); ++k) { const Patch& b = sites[mut[k]].q; float w = (1.f / (b.sigmaN * b.sigmaN)) / (1.f + dists[k] * dists[k] / L2); cSum = cSum + sites[mut[k]].world * w; nSum = nSum + b.n * w; wSum += w; s2Sum += w * b.sigmaN * b.sigmaN; }
    V3 cFit = cSum * (1.f / wSum), nFit = Norm(nSum);
    float e0 = Dot(nFit, a.world - cFit), res2 = wA * e0 * e0;
    for (size_t k = 0; k < mut.size(); ++k) { const Patch& b = sites[mut[k]].q; float w = (1.f / (b.sigmaN * b.sigmaN)) / (1.f + dists[k] * dists[k] / L2); float e = Dot(nFit, sites[mut[k]].world - cFit); res2 += w * e * e; }
    r.planeRms = sqrtf(res2 / wSum);
    r.flat = r.planeRms <= (float)FS_SHEET_FLAT_K * sqrtf(s2Sum / wSum);
    if (r.flat) { r.offset = Dot(nFit, cFit - a.world); r.normal = nFit; }
    // E4.1C contraction decision (twin: sheet_fit.comp): converged flat interior node of lowest priority in its mutual ring
    if (!r.flat || r.degree < FS_CONTRACT_MIN_DEGREE || a.ev.positiveSupport < FS_CONTRACT_MIN_STATIC || (a.ev.lifecycle & 3u) != FS_LIFE_ACTIVE) return r;
    const float sigFit = fmaxf(sqrtf(s2Sum / wSum), (float)FS_SIGMA_N_FLOOR_M);
    for (size_t id : mut) if (!Survives(sites[id].q, sites[id].ev, a.q, a.ev)) return r;
    float bestE = (float)FS_CONTRACT_BUDGET;
    for (size_t k = 0; k < mut.size(); ++k) {
        const SheetSite& b = sites[mut[k]];
        if (dists[k] > (float)FS_CONTRACT_DIST_MAX_M || b.ev.positiveSupport < FS_CONTRACT_MIN_STATIC || (b.ev.lifecycle & 3u) != FS_LIFE_ACTIVE) continue;
        float E = fabsf(Dot(nFit, b.world - cFit)) / sigFit + (float)FS_CONTRACT_W_CURV * r.planeRms / sigFit + (float)FS_CONTRACT_W_NORMAL * (1.f - Dot(b.q.n, nFit))
                + (float)FS_CONTRACT_W_BOUNDARY * (float)(FS_SHEET_K - r.degree);
        if (a.q.appearance & b.q.appearance & FS_APPEARANCE_MEASURED) E += (float)FS_CONTRACT_W_COLOR * Len(ColorOf(a.q.appearance) - ColorOf(b.q.appearance));
        if (E < bestE || (E == bestE && r.contractTarget != UINT32_MAX && b.id < r.contractTarget)) { bestE = E; r.contractTarget = b.id; r.contractE = E; }
    }
    return r;
}
// Survivor absorbs a patch of the same surface (twin: fsAbsorbPatch); `pd` in the survivor's page frame.
inline Patch AbsorbPatch(const Patch& ps, const Patch& pd) {
    float ws = 1.f / (ps.sigmaN * ps.sigmaN), wd = 1.f / (pd.sigmaN * pd.sigmaN);
    V3 delta = pd.p - ps.p; float d = Dot(ps.n, delta); V3 tv = delta - ps.n * d;
    Patch r = ps;
    r.p = ps.p + ps.n * (d * wd / (ws + wd)) + tv * (wd / (ws + wd));
    r.n = Norm(ps.n * ws + pd.n * wd);
    float sa, sb, sc, da, db, dc; PatchMoment(ps, sa, sb, sc); PatchMoment(pd, da, db, dc);
    V3 t1, t2; Frame(r.n, t1, t2);
    float osx = Dot(ps.p - r.p, t1), osy = Dot(ps.p - r.p, t2), odx = Dot(pd.p - r.p, t1), ody = Dot(pd.p - r.p, t2);
    float cs = (float)std::max<uint32_t>(ps.flags & kEvidenceCountMask, 1u), cd = (float)std::max<uint32_t>(pd.flags & kEvidenceCountMask, 1u);
    float rM, rm, ang;
    MomentToEllipse((cs * (sa + osx * osx) + cd * (da + odx * odx)) / (cs + cd), (cs * (sb + osy * osy) + cd * (db + ody * ody)) / (cs + cd), (cs * (sc + osx * osy) + cd * (dc + odx * ody)) / (cs + cd), rM, rm, ang);
    r.rM = fminf(fmaxf(rM, (float)FS_RADIUS_MIN_M), (float)FS_RADIUS_MAX_M); r.rm = fminf(fmaxf(rm, (float)FS_RADIUS_MIN_M), r.rM); r.angle = ang;
    r.sigmaN = fmaxf(sqrtf(1.f / (ws + wd)), (float)FS_SIGMA_N_FLOOR_M);
    r.flags = (ps.flags & ~(uint32_t)kEvidenceCountMask) | std::min<uint32_t>((ps.flags & kEvidenceCountMask) + (pd.flags & kEvidenceCountMask), FS_EVIDENCE_COUNT_MAX);
    bool ms = ps.appearance & FS_APPEARANCE_MEASURED, md = pd.appearance & FS_APPEARANCE_MEASURED;
    if (ms && md) r.appearance = AppearanceWord((ColorOf(ps.appearance) * cs + ColorOf(pd.appearance) * cd) * (1.f / (cs + cd)), AppearanceObs(ps.appearance) + AppearanceObs(pd.appearance));
    else if (md) r.appearance = pd.appearance;
    return r;
}
// Refinement site coverage (twin: sheet_refine.comp fsSiteCovered): a live surfel already explains the location.
inline bool RefineCovered(const std::vector<Patch>& live, V3 sp, V3 sn, float sSigma) {
    for (const Patch& b : live) {
        if ((b.flags & kFlagRemoved) || Dot(b.n, sn) < (float)FS_SHEET_MIN_DOT) continue;
        V3 delta = sp - b.p; float d = Dot(b.n, delta);
        if (fabsf(d) > PlaneGate(b.sigmaN, sSigma)) continue;
        if (Len(delta - b.n * d) <= b.rM) return true;
    }
    return false;
}
// Apply: the surfel moves along the fitted normal and turns toward it; canonical radii never change (statistical support).
inline Patch SheetApplyR(const Patch& a, const SheetDelta& d) {
    Patch o = a;
    if (!d.flat) return o;
    o.p = a.p + d.normal * (d.offset * (float)FS_SHEET_BLEND);
    o.n = Norm(a.n * (1.f - (float)FS_SHEET_BLEND) + d.normal * (float)FS_SHEET_BLEND);
    return o;
}

// ---- C09R-E5R persistent dirty frontier (twin: fsMarkDirtyCell / dirty_take.comp) ----------------------------------------------
struct DirtyRing {
    std::vector<std::vector<uint32_t>> masks; std::vector<uint32_t> entries, ticks; uint32_t head = 0, tail = 0, cap = 0, drops = 0;
    void Init(uint32_t pages, uint32_t capacity) { masks.assign(pages, std::vector<uint32_t>(FS_CELLS_PER_PAGE / 32, 0u)); cap = capacity; entries.assign(cap, 0u); ticks.assign(cap, 0u); head = tail = drops = 0; }
    bool Mark(uint32_t page, uint32_t cell, uint32_t tick) {
        uint32_t& w = masks[page][cell >> 5]; const uint32_t bit = 1u << (cell & 31);
        if (w & bit) return false;
        w |= bit;
        const uint32_t t = tail++;
        if (t - head >= cap) { w &= ~bit; drops++; return false; }
        entries[t & (cap - 1)] = (page << 15) | cell; ticks[t & (cap - 1)] = tick;
        return true;
    }
    // take: the consumer advances head by `count` first, then lists the entries whose bit is still set (clearing it)
    std::vector<uint32_t> Take(uint32_t count, uint32_t pageCount) {
        std::vector<uint32_t> list; const uint32_t begin = head; count = std::min(count, tail - head); head += count;
        for (uint32_t i = 0; i < count; ++i) {
            const uint32_t e = entries[(begin + i) & (cap - 1)], page = e >> 15, cell = e & 0x7FFFu;
            if (page >= pageCount) continue;
            uint32_t& w = masks[page][cell >> 5]; const uint32_t bit = 1u << (cell & 31);
            if (!(w & bit)) continue;
            w &= ~bit; list.push_back(e);
        }
        return list;
    }
    void ReleasePage(uint32_t page) { std::fill(masks[page].begin(), masks[page].end(), 0u); }
};

} // namespace world
} // namespace fs
