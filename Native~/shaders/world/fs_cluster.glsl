// Unmatched-measurement clustering shared by fuse_cluster_count / fuse_cluster_write (C09R-B F3): the
// measurements of one owner cell segment (sorted, deterministic order) are greedily clustered into at most
// FS_CELL_NEW_MAX candidates: a measurement joins the first candidate whose plane distance is inside the
// sigma gate, normal dot >= FS_ASSOC_MIN_DOT and tangent distance <= 2 x footprint; otherwise it seeds a new
// candidate. Both kernels run the identical function so counts and contents agree without a scratch record.
#ifndef FS_CLUSTER_GLSL
#define FS_CLUSTER_GLSL
struct FsCand { vec3 p; vec3 n; float wN, wT; vec3 nSum; float mxx, myy, mxy; float d2; float fp; uint cnt; float sigmaN, sigmaT; };
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
        float wN = 1.0 / (sN * sN), wT = 1.0 / (sT * sT);
        int hit = -1;
        for (uint c = 0u; c < uint(FS_CELL_NEW_MAX); ++c) {
            if (c >= nc) break;
            FsCand q = cands[c];
            if (dot(q.n, nm) < FS_ASSOC_MIN_DOT) continue;
            vec3 delta = pm - q.p; float d = dot(q.n, delta);
            float gate = FS_ASSOC_SIGMA_GATE * sqrt(q.sigmaN * q.sigmaN + sN * sN);
            if (abs(d) > gate) continue;
            vec3 tv = delta - d * q.n;
            if (dot(tv, tv) > 4.0 * fp * fp) continue;
            hit = int(c); break;
        }
        if (hit < 0) {
            if (nc >= uint(FS_CELL_NEW_MAX)) continue;                     // dedupe bound: extra sheets wait for the next epoch
            FsCand q; q.p = pm; q.n = nm; q.wN = wN; q.wT = wT; q.nSum = nm * wN; q.mxx = fp * fp; q.myy = fp * fp; q.mxy = 0.0; q.d2 = 0.0; q.fp = fp; q.cnt = 1u; q.sigmaN = sN; q.sigmaT = sT;
            cands[nc] = q; nc++;
        } else {
            FsCand q = cands[hit];
            vec3 delta = pm - q.p; float d = dot(q.n, delta);
            float aN = wN / (q.wN + wN), aT = wT / (q.wT + wT);
            vec3 tv = delta - d * q.n;
            q.p = q.p + q.n * (d * aN) + tv * aT;
            q.wN += wN; q.wT += wT; q.nSum += nm * wN; q.cnt++;
            q.d2 += d * d; q.fp = max(q.fp, fp);
            vec3 t1, t2; fsTangentFrame(q.n, t1, t2);
            vec2 tvv = vec2(dot(tv, t1), dot(tv, t2));
            q.mxx += tvv.x * tvv.x + fp * fp; q.myy += tvv.y * tvv.y + fp * fp; q.mxy += tvv.x * tvv.y;
            q.sigmaN = max(sqrt(1.0 / q.wN), FS_SIGMA_N_FLOOR_M); q.sigmaT = max(sqrt(1.0 / q.wT), FS_SIGMA_T_FLOOR_M);
            cands[hit] = q;
        }
    }
    return nc;
}
#endif
