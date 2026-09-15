// Unmatched-measurement clustering shared by fuse_cluster_count / fuse_cluster_write (C09R-B F3, C09R-E4): the
// measurements of one owner cell segment (sorted, deterministic order) are greedily clustered into at most
// FS_CELL_NEW_MAX candidates. A candidate keeps plain sums of its members in the frame of its seed (offsets along the seed
// tangents and normal, their squares and products), so its mean position, mean normal and tangent covariance are exact
// about the CURRENT mean (Welford); a measurement joins the first candidate whose MEAN normal is within FS_ASSOC_MIN_DOT,
// whose mean plane is within the bounded plane gate and whose mean centre is within the association reach; otherwise it
// seeds a new candidate. All members come from ONE observation: the candidate carries a single-measurement sigma.
#ifndef FS_CLUSTER_GLSL
#define FS_CLUSTER_GLSL
struct FsCand { vec3 p0; vec3 n0; vec3 t1; vec3 t2; float c; float sd, sdd, sx, sy, sxx, syy, sxy, sfp2; vec3 nSum; float wN, wT; float fp; vec3 col; float colW; uint srcAnd; };
vec3 fsCandMean(FsCand q) { return q.p0 + q.n0 * (q.sd / q.c) + q.t1 * (q.sx / q.c) + q.t2 * (q.sy / q.c); }
vec3 fsCandNormal(FsCand q) { float l = length(q.nSum); return l > 1e-12 ? q.nSum / l : q.n0; }
float fsCandSigmaN(FsCand q) { return sqrt(q.c / max(q.wN, 1e-12)); }   // mean single-measurement sigma (members are correlated)
float fsCandSigmaT(FsCand q) { return sqrt(q.c / max(q.wT, 1e-12)); }
void fsCandAdd(inout FsCand q, vec3 pm, vec3 nm, float sN, float sT, float fp, uint colorWord, uint srcFlags) {
    vec3 o = pm - q.p0;
    float ox = dot(o, q.t1), oy = dot(o, q.t2), od = dot(o, q.n0);
    q.c += 1.0; q.sd += od; q.sdd += od * od; q.sx += ox; q.sy += oy; q.sxx += ox * ox; q.syy += oy * oy; q.sxy += ox * oy; q.sfp2 += fp * fp;
    q.nSum += nm / (sN * sN); q.wN += 1.0 / (sN * sN); q.wT += 1.0 / (sT * sT); q.fp = max(q.fp, fp); q.srcAnd &= srcFlags;
    if ((colorWord & FS_MEAS_COLOR_VALID) != 0u) { q.col += fsColorOf(colorWord); q.colW += 1.0; }
}
// Covariance about the mean in the seed tangent frame (+ mean squared footprint), and the normalised normal-offset variance.
void fsCandMoments(FsCand q, out float mxx, out float myy, out float mxy, out float varNorm) {
    float mx = q.sx / q.c, my = q.sy / q.c, md = q.sd / q.c, f2 = q.sfp2 / q.c;
    mxx = max(q.sxx / q.c - mx * mx, 0.0) + f2; myy = max(q.syy / q.c - my * my, 0.0) + f2; mxy = q.sxy / q.c - mx * my;
    float sN = fsCandSigmaN(q);
    varNorm = max(q.sdd / q.c - md * md, 0.0) / max(sN * sN, 1e-12);
}
uint fsClusterSegment(uint start, uint count, uint key, vec3 origin, out FsCand cands[FS_CELL_NEW_MAX], out uint overflowed) {
    uint nc = 0u; overflowed = 0u;
    for (uint k = 0u; k < uint(FS_SEG_CLUSTER_MAX) + 1u; ++k) {
        uint j = start + k;
        if (j >= count || assocA[j].key != key) break;
        if (k == uint(FS_SEG_CLUSTER_MAX)) { overflowed = 1u; break; }
        FsSurfaceMeasurement m = meas[assocA[j].meas];
        vec3 pm = vec3(m.px, m.py, m.pz) - origin;
        vec3 nm = normalize(vec3(m.nx, m.ny, m.nz));
        float sN = max(m.sigmaN, FS_SIGMA_N_FLOOR_M), sT = max(m.sigmaT, FS_SIGMA_T_FLOOR_M);
        float fp = clamp(m.footprint, FS_RADIUS_MIN_M, FS_RADIUS_MAX_M);
        int hit = -1;
        for (uint c = 0u; c < uint(FS_CELL_NEW_MAX); ++c) {
            if (c >= nc) break;
            FsCand q = cands[c];
            vec3 nq = fsCandNormal(q);
            if (dot(nq, nm) < FS_ASSOC_MIN_DOT) continue;
            vec3 delta = pm - fsCandMean(q); float d = dot(nq, delta);
            if (abs(d) > fsPlaneGate(fsCandSigmaN(q), sN)) continue;
            vec3 tv = delta - d * nq;
            float reach = max(2.0 * fp, FS_ASSOC_REACH_MIN_M);
            if (dot(tv, tv) > reach * reach) continue;
            hit = int(c); break;
        }
        if (hit < 0) {
            if (nc >= uint(FS_CELL_NEW_MAX)) continue;                     // dedupe bound: extra sheets wait for the next observation
            FsCand q; q.p0 = pm; q.n0 = nm; fsTangentFrame(nm, q.t1, q.t2);
            q.c = 0.0; q.sd = 0.0; q.sdd = 0.0; q.sx = 0.0; q.sy = 0.0; q.sxx = 0.0; q.syy = 0.0; q.sxy = 0.0; q.sfp2 = 0.0;
            q.nSum = vec3(0.0); q.wN = 0.0; q.wT = 0.0; q.fp = 0.0; q.col = vec3(0.0); q.colW = 0.0; q.srcAnd = 0xFFFFFFFFu;
            fsCandAdd(q, pm, nm, sN, sT, fp, m.reserved, m.sourceFlags);
            cands[nc] = q; nc++;
        } else {
            FsCand q = cands[hit];
            fsCandAdd(q, pm, nm, sN, sT, fp, m.reserved, m.sourceFlags);
            cands[hit] = q;
        }
    }
    return nc;
}
#endif
