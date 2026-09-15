// E6R persistent surface topology, CPU twin of topo_faces.comp / topo_commit.comp / sheet_contract.comp (matching) used by the host tests.
// Sites are indices into a vector of SheetSite (fs_fusion_ref.h); proposals come from SheetProposals (true metric top-K). The model keeps
// the persistent state the GPU keeps: per vertex record (sheet, label, label generation, stable count, incident faces, owned faces) and
// sheet records (union-find with the lower sheet id as root, relabel generation, minimum label, vertex count).
#pragma once
#include "fs_fusion_ref.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <map>
#include <vector>

namespace fs {
namespace world {

struct FaceKey { uint32_t a = 0, b = 0, c = 0; bool operator<(const FaceKey& o) const { return a != o.a ? a < o.a : (b != o.b ? b < o.b : c < o.c); } bool operator==(const FaceKey& o) const { return a == o.a && b == o.b && c == o.c; } };
inline FaceKey MakeFaceKey(uint32_t x, uint32_t y, uint32_t z) { std::array<uint32_t, 3> v{x, y, z}; std::sort(v.begin(), v.end()); return FaceKey{v[0], v[1], v[2]}; }
inline std::pair<uint32_t, uint32_t> MakeEdgeKey(uint32_t x, uint32_t y) { return x < y ? std::make_pair(x, y) : std::make_pair(y, x); }

struct TopoSite { SheetSite s; std::vector<uint32_t> props; uint32_t life = FS_LIFE_ACTIVE; uint32_t support = 10; uint16_t lastSeen = 0; bool usable = true; };
struct FaceDerivation { std::vector<std::pair<uint32_t, uint32_t>> owned; bool interior = false; float holeMm2 = 0; };

// lifted in-circle predicate (twin: fsInCircleLifted): a at the origin, (a, b, c) counter-clockwise; d strictly inside -> true
inline float SidLift(uint32_t sid) { uint32_t k = sid * 2654435761u; k ^= k >> 15; return (float)FS_FACE_LIFT_M2 * (float)(k & 1023u) / 1023.f; }
inline bool InCircleLifted(float bx, float by, float cx, float cy, float dx, float dy, float la, float lb, float lc, float ld) {
    const float adx = -dx, ady = -dy, bdx = bx - dx, bdy = by - dy, cdx = cx - dx, cdy = cy - dy;
    const float dl = dx * dx + dy * dy + ld;
    const float al = la - dl, bl = bx * bx + by * by + lb - dl, cl = cx * cx + cy * cy + lc - dl;
    const float det = adx * (bdy * cl - bl * cdy) - ady * (bdx * cl - bl * cdx) + al * (bdx * cdy - bdy * cdx);
    return det > 0.f;
}
inline bool TopoUsable(const std::vector<TopoSite>& v, uint32_t i) { return i < v.size() && v[i].usable && v[i].life != FS_LIFE_RETIRING; }
inline bool TopoMutual(const std::vector<TopoSite>& v, uint32_t a, uint32_t b) { return std::find(v[b].props.begin(), v[b].props.end(), a) != v[b].props.end() && std::find(v[a].props.begin(), v[a].props.end(), b) != v[a].props.end(); }

// twin: topo_faces.comp
inline FaceDerivation DeriveFaces(const std::vector<TopoSite>& v, uint32_t ai) {
    FaceDerivation r;
    if (!TopoUsable(v, ai)) return r;
    const Patch& a = v[ai].s.q; const V3 pa = v[ai].s.world; V3 t1, t2; Frame(a.n, t1, t2);
    struct C { uint32_t id; float ang; float ux, uy; };
    std::vector<C> cs;
    for (uint32_t nb : v[ai].props) {
        if (!TopoUsable(v, nb) || !TopoMutual(v, ai, nb)) continue;
        V3 d = v[nb].s.world - pa; float ux = Dot(d, t1), uy = Dot(d, t2);
        cs.push_back({nb, atan2f(uy, ux), ux, uy});
    }
    std::stable_sort(cs.begin(), cs.end(), [](const C& x, const C& y) { return x.ang < y.ang; });
    bool interior = cs.size() >= 3;
    const size_t nc = cs.size();
    // for every candidate edge (a, c_j): the first CCW candidate c_k forming a valid (compatible, bounded, lifted-Delaunay) triangle.
    // Delaunay gives exactly one triangle on the CCW side of each Delaunay edge, so every owner finds the same faces.
    float coveredUntil = -1e9f; bool first = true; float firstStart = 0;
    for (size_t j = 0; j < nc && nc >= 2; ++j) {
        bool found = false;
        for (size_t step = 1; step < nc && step <= 3 && !found; ++step) {
            const size_t k = (j + step) % nc;
            float gap = cs[k].ang - cs[j].ang; if (gap <= 0) gap += 2.f * 3.14159265f;
            if (gap >= (float)FS_FACE_MAX_GAP_RAD || gap <= 1e-4f) break;
            const uint32_t bi = cs[j].id, ci = cs[k].id;
            float dbc; if (!SheetEdgeR(v[bi].s.q, v[bi].s.world, v[ci].s.q, v[ci].s.world, dbc)) continue;
            const float bx = cs[j].ux, by = cs[j].uy, cx = cs[k].ux, cy = cs[k].uy;
            const float den = 2.f * (bx * cy - by * cx);
            if (fabsf(den) < 1e-10f) continue;
            const float bb = bx * bx + by * by, cc = cx * cx + cy * cy;
            const float ox = (cy * bb - by * cc) / den, oy = (bx * cc - cx * bb) / den, R = sqrtf(ox * ox + oy * oy);
            if (R > (float)FS_FACE_MAX_CIRCUM_M) continue;
            bool delaunay = true;
            for (uint32_t src : {ai, bi, ci}) {
                for (uint32_t di : v[src].props) {
                    if (di == ai || di == bi || di == ci || !TopoUsable(v, di)) continue;
                    if (Dot(v[di].s.q.n, a.n) < (float)FS_SHEET_MIN_DOT) continue;
                    V3 dd = v[di].s.world - pa;
                    if (fabsf(Dot(dd, a.n)) > (float)FS_SHEET_PLANE_MAX_M) continue;
                    if (InCircleLifted(bx, by, cx, cy, Dot(dd, t1), Dot(dd, t2), SidLift(a.surfaceId), SidLift(v[bi].s.q.surfaceId), SidLift(v[ci].s.q.surfaceId), SidLift(v[di].s.q.surfaceId))) { delaunay = false; break; }
                }
                if (!delaunay) break;
            }
            if (!delaunay) continue;
            found = true;
            // fan coverage: a wedge between triangles that no triangle covers is a boundary / hole
            if (first) { firstStart = cs[j].ang; first = false; }
            else if (cs[j].ang > coveredUntil + 1e-3f) { interior = false; r.holeMm2 += 0.f; }
            coveredUntil = cs[j].ang + gap;
            if (a.surfaceId > v[bi].s.q.surfaceId || a.surfaceId > v[ci].s.q.surfaceId) continue;
            if (r.owned.size() < FS_TOPO_FACES) r.owned.push_back({bi, ci});
        }
        if (!found) interior = false;
    }
    if (!first && coveredUntil < firstStart + 2.f * 3.14159265f - 1e-3f) interior = false;
    r.interior = interior;
    return r;
}

// twin: topo_commit.comp (the persistent state + sheet records)
class TopologyModel {
public:
    struct Sheet { uint32_t sheetId = 0, parent = 0, vertices = 0, relabelGen = 1, minLabel = 0; };
    struct Vert { bool has = false; int32_t sheet = -1; uint32_t label = 0, labelGen = 0, stable = 0, incident = 0; std::vector<std::pair<uint32_t, uint32_t>> faces; std::vector<uint32_t> faceMeta; };
    std::vector<Sheet> sheets; std::vector<Vert> verts; uint64_t unions = 0, splits = 0, facesCreated = 0, facesRetired = 0, bridgesPending = 0;
    explicit TopologyModel(size_t n) : verts(n) {}
    int32_t Root(int32_t s) const { for (int k = 0; k < 64 && s >= 0; ++k) { if ((int32_t)sheets[s].parent == s) return s; s = (int32_t)sheets[s].parent; } return s; }
    int32_t NewSheet(uint32_t id) { Sheet sh; sh.sheetId = id; sh.parent = (uint32_t)sheets.size(); sh.minLabel = id; sheets.push_back(sh); return (int32_t)sheets.size() - 1; }
    void SetSheet(uint32_t vi, int32_t s) { Vert& x = verts[vi]; if (x.sheet == s) return; if (x.sheet >= 0) { int32_t r = Root(x.sheet); if (sheets[r].vertices) sheets[r].vertices--; } x.sheet = s; if (s >= 0) sheets[Root(s)].vertices++; x.has = true; }
    void Union(int32_t ra, int32_t rb) {
        if (ra == rb || ra < 0 || rb < 0) return;
        if (sheets[rb].sheetId < sheets[ra].sheetId) std::swap(ra, rb);
        sheets[rb].parent = (uint32_t)ra; sheets[ra].vertices += sheets[rb].vertices; sheets[ra].relabelGen++; sheets[ra].minLabel = std::min(sheets[ra].minLabel, sheets[rb].minLabel); unions++;
    }
    uint32_t ActiveSheets() const { uint32_t n = 0; for (size_t s = 0; s < sheets.size(); ++s) if (sheets[s].parent == s && sheets[s].vertices > 0) n++; return n; }
    std::vector<uint32_t> Release(const std::vector<TopoSite>& v, uint32_t vi) {
        std::vector<uint32_t> marks;
        Vert& x = verts[vi]; if (!x.has) return marks;
        for (auto& f : x.faces) { verts[f.first].incident--; verts[f.second].incident--; x.incident--; facesRetired++; }
        x.faces.clear(); x.faceMeta.clear();
        if (x.sheet >= 0) { int32_t r = Root(x.sheet); sheets[r].relabelGen++; sheets[r].minLabel = 0xFFFFFFFFu; }
        SetSheet(vi, -1); x.has = false;
        for (uint32_t nb : v[vi].props) marks.push_back(nb);
        return marks;
    }
    // commits one vertex; returns vertices to re-mark (changed frontier)
    std::vector<uint32_t> Commit(const std::vector<TopoSite>& v, uint32_t vi) {
        std::vector<uint32_t> marks;
        if (!TopoUsable(v, vi)) return Release(v, vi);
        FaceDerivation d = DeriveFaces(v, vi);
        Vert& x = verts[vi];
        if (!x.has && d.owned.empty()) return marks;
        if (!x.has) { x.has = true; x.label = v[vi].s.q.surfaceId; }
        // diff by SurfaceIDs
        std::vector<std::pair<uint32_t, uint32_t>> kept; std::vector<uint32_t> keptMeta;
        for (size_t f = 0; f < x.faces.size(); ++f) {
            auto it = std::find_if(d.owned.begin(), d.owned.end(), [&](auto& p) { return MakeEdgeKey(p.first, p.second) == MakeEdgeKey(x.faces[f].first, x.faces[f].second); });
            if (it != d.owned.end()) { kept.push_back(x.faces[f]); keptMeta.push_back(x.faceMeta[f]); d.owned.erase(it); }
            else { verts[x.faces[f].first].incident--; verts[x.faces[f].second].incident--; x.incident--; facesRetired++; }
        }
        for (auto& p : d.owned) { kept.push_back(p); keptMeta.push_back(0u); verts[p.first].incident++; verts[p.second].incident++; x.incident++; facesCreated++; }
        x.faces = kept; x.faceMeta = keptMeta;
        for (size_t f = 0; f < x.faces.size(); ++f) {
            const uint32_t bi = x.faces[f].first, ci = x.faces[f].second;
            int32_t sa = Root(x.sheet), sb = Root(verts[bi].sheet), sc = Root(verts[ci].sheet);
            int32_t root = sa >= 0 ? sa : (sb >= 0 ? sb : sc);
            bool bridge = (sa >= 0 && sa != root) || (sb >= 0 && sb != root) || (sc >= 0 && sc != root);
            if (bridge) {   // a still-growing sheet joins at once; two established sheets need repeated evidence
                uint32_t smallest = 0xFFFFFFFFu; for (int32_t rr : {sa, sb, sc}) if (rr >= 0) smallest = std::min(smallest, sheets[rr].vertices);
                if (smallest < FS_BRIDGE_FREE_VERTICES) { if (sa >= 0 && sb >= 0) Union(Root(sa), Root(sb)); if (sa >= 0 && sc >= 0) Union(Root(sa), Root(sc)); if (sb >= 0 && sc >= 0) Union(Root(sb), Root(sc)); root = Root(sa >= 0 ? sa : sb); bridge = false; }
            }
            if (bridge) {
                uint32_t& meta = x.faceMeta[f];
                const bool supported = v[vi].support >= FS_BRIDGE_MIN_SUPPORT && v[bi].support >= FS_BRIDGE_MIN_SUPPORT && v[ci].support >= FS_BRIDGE_MIN_SUPPORT;
                uint32_t ev = (meta >> 2) & 63u, last = (meta >> 8) & 0xFFFFu;
                if (supported && v[vi].lastSeen != last) { ev++; last = v[vi].lastSeen; }
                meta = (ev << 2) | (last << 8);
                if (ev < FS_BRIDGE_EVIDENCE) { bridgesPending++; continue; }
                if (sa >= 0 && sb >= 0) Union(Root(sa), Root(sb));
                if (sa >= 0 && sc >= 0) Union(Root(sa), Root(sc));
                if (sb >= 0 && sc >= 0) Union(Root(sb), Root(sc));
                root = Root(sa >= 0 ? sa : sb);
            }
            if (root < 0) {   // join a graph neighbour's sheet before founding a new one (no fragmentation during growth)
                int32_t best = -1; for (uint32_t nb : v[vi].props) { if (!TopoUsable(v, nb) || !verts[nb].has || verts[nb].sheet < 0) continue; int32_t nr = Root(verts[nb].sheet); if (best < 0 || sheets[nr].sheetId < sheets[best].sheetId) best = nr; }
                root = best >= 0 ? best : NewSheet(std::min({v[vi].s.q.surfaceId, v[bi].s.q.surfaceId, v[ci].s.q.surfaceId}));
            }
            SetSheet(vi, root); SetSheet(bi, root); SetSheet(ci, root);
            if (!verts[bi].has) { verts[bi].has = true; verts[bi].label = v[bi].s.q.surfaceId; }
            if (!verts[ci].has) { verts[ci].has = true; verts[ci].label = v[ci].s.q.surfaceId; }
        }
        // labels / split
        if (x.sheet >= 0) {
            int32_t r = Root(x.sheet); if (r != x.sheet) SetSheet(vi, r);
            Sheet& sh = sheets[r];
            if (x.labelGen != sh.relabelGen) { x.label = v[vi].s.q.surfaceId; x.labelGen = sh.relabelGen; x.stable = 0; for (uint32_t nb : v[vi].props) marks.push_back(nb); }
            uint32_t L = x.label;
            for (uint32_t nb : v[vi].props) { const Vert& y = verts[nb]; if (!y.has || y.labelGen != sh.relabelGen || Root(y.sheet) != r) continue; L = std::min(L, y.label); }
            if (L < x.label) { x.label = L; x.stable = 0; for (uint32_t nb : v[vi].props) marks.push_back(nb); } else x.stable = std::min(x.stable + 1u, 255u);
            sheets[r].minLabel = std::min(sheets[r].minLabel, x.label);
            if (x.label > sheets[r].minLabel) {
                if (x.stable >= FS_SPLIT_CONFIRM) {
                    int32_t target = -1;
                    for (uint32_t nb : v[vi].props) { const Vert& y = verts[nb]; if (!y.has) continue; int32_t nr = Root(y.sheet); if (nr >= 0 && nr != r && sheets[nr].sheetId == x.label) { target = nr; break; } }
                    if (target < 0) { target = NewSheet(x.label); sheets[target].relabelGen = sh.relabelGen; splits++; }
                    SetSheet(vi, target); x.labelGen = sheets[target].relabelGen;
                    for (uint32_t nb : v[vi].props) marks.push_back(nb);
                } else marks.push_back(vi);
            }
        }
        return marks;
    }
    // runs the changed frontier to quiescence (bounded)
    void Settle(const std::vector<TopoSite>& v, std::vector<uint32_t> frontier, uint32_t maxPasses = 4096, const std::vector<bool>* inView = nullptr) {
        for (uint32_t pass = 0; pass < maxPasses && !frontier.empty(); ++pass) {
            std::sort(frontier.begin(), frontier.end()); frontier.erase(std::unique(frontier.begin(), frontier.end()), frontier.end());
            std::vector<uint32_t> next;
            for (uint32_t vi : frontier) { if (inView && !(*inView)[vi]) continue; auto m = Commit(v, vi); next.insert(next.end(), m.begin(), m.end()); }
            frontier.swap(next);
        }
    }
};

// twin: sheet_contract.comp matching: proposals (loser, survivor) in batch order; a vertex takes part in at most one contraction per generation
inline std::vector<std::pair<uint32_t, uint32_t>> ContractionMatching(const std::vector<std::pair<uint32_t, uint32_t>>& proposals) {
    std::vector<std::pair<uint32_t, uint32_t>> accepted; std::map<uint32_t, bool> used;
    for (auto& p : proposals) { if (used[p.first] || used[p.second]) continue; used[p.first] = used[p.second] = true; accepted.push_back(p); }
    return accepted;
}

} // namespace world
} // namespace fs
