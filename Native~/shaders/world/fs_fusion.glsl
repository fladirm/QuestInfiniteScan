// Fusion mathematics shared by fuse_reduce / fuse_cluster_* / fuse_maint_* (C09R-B). Twin: fs_fusion_ref.h.
// A surfel is an oriented anisotropic patch: centre, normal, tangent frame angle, semi-axes (rM >= rm), sigma
// along the normal and in the tangent plane. Contributions are precision-weighted; the tangent ellipse is the
// eigen-decomposition of the combined second moment of tangential offsets (representation and fusion agree).
#ifndef FS_FUSION_GLSL
#define FS_FUSION_GLSL
struct FsPatch {
    vec3 p; vec3 n; float angle; float rM, rm; float sigmaN, sigmaT;
    uint flags; uint surfaceId; uint appearance;
};
FsPatch fsUnpack(FsSurfel s) {
    FsPatch q;
    q.p = fsSurfelLocalPos(s); q.n = fsSurfelNormal(s);
    q.angle = float(fsGet_FsSurfel_tangentAngle(s)) / 65536.0 * 2.0 * FS_PI;
    q.rM = fsDecodeLogRadius(fsGet_FsSurfel_radiusMajor(s)); q.rm = fsDecodeLogRadius(fsGet_FsSurfel_radiusMinor(s));
    q.sigmaN = fsDecodeLogSigma(fsGet_FsSurfel_sigmaN(s)); q.sigmaT = fsDecodeLogSigma(fsGet_FsSurfel_sigmaTMajor(s));
    q.flags = fsGet_FsSurfel_evidenceFlags(s); q.surfaceId = s.surfaceId; q.appearance = s.appearanceHandle;
    return q;
}
FsSurfel fsPack(FsPatch q) {
    FsSurfel s;
    s.px_py = 0u; s.pz_normalOct = 0u; s.normalOctHi_tangentAngle = 0u; s.radiusMajor_radiusMinor = 0u; s.sigmaN_sigmaTMajor = 0u; s.sigmaTMinor_evidenceFlags = 0u;
    fsSet_FsSurfel_px(s, fsEncodePos(q.p.x)); fsSet_FsSurfel_py(s, fsEncodePos(q.p.y)); fsSet_FsSurfel_pz(s, fsEncodePos(q.p.z));
    uint o, oh; fsSplitOct32(fsEncodeOct32(q.n), o, oh);
    fsSet_FsSurfel_normalOct(s, o); fsSet_FsSurfel_normalOctHi(s, oh);
    float a = q.angle; a = a - floor(a / (2.0 * FS_PI)) * 2.0 * FS_PI;
    fsSet_FsSurfel_tangentAngle(s, uint(a / (2.0 * FS_PI) * 65536.0) & 0xFFFFu);
    fsSet_FsSurfel_radiusMajor(s, fsEncodeLogRadius(q.rM)); fsSet_FsSurfel_radiusMinor(s, fsEncodeLogRadius(q.rm));
    fsSet_FsSurfel_sigmaN(s, fsEncodeLogSigma(q.sigmaN));
    fsSet_FsSurfel_sigmaTMajor(s, fsEncodeLogSigma(q.sigmaT)); fsSet_FsSurfel_sigmaTMinor(s, fsEncodeLogSigma(q.sigmaT));
    fsSet_FsSurfel_evidenceFlags(s, q.flags);
    s.surfaceId = q.surfaceId; s.appearanceHandle = q.appearance;
    return s;
}
// Tangent frame of a patch: canonical frame rotated by angle (twin: SurfelDrawAbi.TangentFrame / fsTangentFrame).
void fsPatchFrame(FsPatch q, out vec3 tMajor, out vec3 tMinor) {
    vec3 t1, t2; fsTangentFrame(q.n, t1, t2);
    tMajor = cos(q.angle) * t1 + sin(q.angle) * t2;
    tMinor = cross(q.n, tMajor);
}
// Second-moment ellipse (a,b,c of [[a,c],[c,b]]) of a patch in the canonical frame of normal n.
void fsPatchMoment(FsPatch q, out float a, out float b, out float c) {
    float cs = cos(q.angle), sn = sin(q.angle), M = q.rM * q.rM, m = q.rm * q.rm;
    a = M * cs * cs + m * sn * sn; b = M * sn * sn + m * cs * cs; c = (M - m) * cs * sn;
}
// Eigen-decomposition of the symmetric 2x2 moment: semi-axes and angle of the major axis.
void fsMomentToEllipse(float a, float b, float c, out float rM, out float rm, out float angle) {
    float tr = a + b, det = a * b - c * c;
    float disc = sqrt(max(tr * tr * 0.25 - det, 0.0));
    float l1 = tr * 0.5 + disc, l2 = max(tr * 0.5 - disc, 0.0);
    rM = sqrt(max(l1, 0.0)); rm = sqrt(l2);
    angle = abs(c) < 1e-12 ? (a >= b ? 0.0 : 0.5 * FS_PI) : atan(l1 - a, c);
}
// Running accumulator of contributions to one surfel (tick-local, deterministic in sorted order).
struct FsAccum {
    float wN;            // sum of normal precisions
    float dN;            // precision-weighted signed plane distance
    vec3  nSum;          // precision-weighted normals
    float wT;            // tangent precision sum
    vec2  tMean;         // precision-weighted tangential offset (canonical frame of the surfel normal)
    float mxx, myy, mxy; // weighted second moments of tangential offsets (+ footprint)
    float d2;            // sum of squared plane distances (residual)
    float fp;            // max footprint
    uint  n;
};
FsAccum fsAccumInit() { FsAccum a; a.wN = 0.0; a.dN = 0.0; a.nSum = vec3(0.0); a.wT = 0.0; a.tMean = vec2(0.0); a.mxx = 0.0; a.myy = 0.0; a.mxy = 0.0; a.d2 = 0.0; a.fp = 0.0; a.n = 0u; return a; }
void fsAccumAdd(inout FsAccum a, FsPatch q, vec3 t1, vec3 t2, vec3 pm, vec3 nm, float sigmaNm, float sigmaTm, float footprint) {
    float wN = 1.0 / (sigmaNm * sigmaNm), wT = 1.0 / (sigmaTm * sigmaTm);
    vec3 delta = pm - q.p;
    float d = dot(q.n, delta);
    vec2 tv = vec2(dot(delta, t1), dot(delta, t2));
    a.wN += wN; a.dN += wN * d; a.nSum += nm * wN;
    a.wT += wT; a.tMean += tv * wT;
    float f2 = footprint * footprint;
    a.mxx += wT * (tv.x * tv.x + f2); a.myy += wT * (tv.y * tv.y + f2); a.mxy += wT * tv.x * tv.y;
    a.d2 += d * d; a.fp = max(a.fp, footprint); a.n++;
}
#endif // FS_FUSION_GLSL
