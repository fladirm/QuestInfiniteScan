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
// Robust accumulator of the pixel contributions of ONE observation to one surfel (C09R-E4, contract §8.5). The pixels
// of one depth frame are correlated: they form one SurfaceObservationEstimate (Huber-weighted mean plane offset, normal,
// tangential offset and spread) whose precision is that of a single measurement, never the sum over pixels.
struct FsAccum {
    float wN;            // sum of Huber-weighted normal precisions
    float dN;            // precision-weighted signed plane distance
    vec3  nSum;          // precision-weighted normals
    float wT;            // tangent precision sum
    vec2  tMean;         // precision-weighted tangential offset (canonical frame of the surfel normal)
    float mxx, myy, mxy; // weighted second moments of tangential offsets (+ footprint)
    float d2;            // Huber-weighted sum of squared plane distances
    float s2;            // Huber-weighted sum of measurement sigmaN^2 (expected residual scale)
    float h;             // sum of Huber weights (effective contribution count)
    float fp;            // max footprint
    vec3  col;           // sum of measurement colours (0..1) carrying FS_MEAS_COLOR_VALID
    float colW;          // number of coloured contributions
    uint  srcAnd;        // AND of the contribution source flags (depth-prior-only observation keeps the systematic floor)
    uint  obs;           // observation id of the contributions (one epoch ingests one frame)
    uint  n;
};
FsAccum fsAccumInit() { FsAccum a; a.wN = 0.0; a.dN = 0.0; a.nSum = vec3(0.0); a.wT = 0.0; a.tMean = vec2(0.0); a.mxx = 0.0; a.myy = 0.0; a.mxy = 0.0; a.d2 = 0.0; a.s2 = 0.0; a.h = 0.0; a.fp = 0.0; a.col = vec3(0.0); a.colW = 0.0; a.srcAnd = 0xFFFFFFFFu; a.obs = 0u; a.n = 0u; return a; }
vec3 fsColorOf(uint word) { return vec3(float(word & 0xFFu), float((word >> 8) & 0xFFu), float((word >> 16) & 0xFFu)) / 255.0; }
uint fsColorPack(vec3 c) { uvec3 u = uvec3(clamp(c, 0.0, 1.0) * 255.0 + 0.5); return u.x | (u.y << 8) | (u.z << 16); }
// Precision-weighted appearance blend (contract §14, C16a): prior weight = distinct observations, measurement weight = 1 observation.
// appearanceHandle bit 31 (FS_APPEARANCE_MEASURED) = measured colour present; the draw record keeps the low 24 bits.
uint fsBlendAppearance(uint old, vec3 meanCol, float wPrior, float wMeas) {
    if ((old & FS_APPEARANCE_MEASURED) == 0u) return FS_APPEARANCE_MEASURED | fsColorPack(meanCol);
    return FS_APPEARANCE_MEASURED | fsColorPack(mix(fsColorOf(old), meanCol, wMeas / (wPrior + wMeas)));
}
// Plane gate with the topological bound: a broad measurement sigma never joins sheets farther apart than FS_ASSOC_PLANE_MAX_M.
float fsPlaneGate(float sigmaA, float sigmaB) { return min(FS_ASSOC_SIGMA_GATE * sqrt(sigmaA * sigmaA + sigmaB * sigmaB), FS_ASSOC_PLANE_MAX_M); }
// Huber weight of a plane residual d against the combined sigma.
float fsHuber(float d, float sigmaComb) { float ad = abs(d); float c = FS_HUBER_K * sigmaComb; return ad <= c ? 1.0 : c / max(ad, 1e-12); }
void fsAccumAdd(inout FsAccum a, FsPatch q, vec3 t1, vec3 t2, vec3 pm, vec3 nm, float sigmaNm, float sigmaTm, float footprint, uint colorWord, uint srcFlags, uint obs) {
    vec3 delta = pm - q.p;
    float d = dot(q.n, delta);
    float hw = fsHuber(d, sqrt(q.sigmaN * q.sigmaN + sigmaNm * sigmaNm));
    float wN = hw / (sigmaNm * sigmaNm), wT = hw / (sigmaTm * sigmaTm);
    vec2 tv = vec2(dot(delta, t1), dot(delta, t2));
    a.wN += wN; a.dN += wN * d; a.nSum += nm * wN;
    a.wT += wT; a.tMean += tv * wT;
    float f2 = footprint * footprint;
    a.mxx += wT * (tv.x * tv.x + f2); a.myy += wT * (tv.y * tv.y + f2); a.mxy += wT * tv.x * tv.y;
    a.d2 += hw * d * d; a.s2 += hw * sigmaNm * sigmaNm; a.h += hw; a.fp = max(a.fp, footprint); a.n++;
    a.srcAnd &= srcFlags; a.obs = obs;
    if ((colorWord & FS_MEAS_COLOR_VALID) != 0u) { a.col += fsColorOf(colorWord); a.colW += 1.0; }
}
float fsSigmaFloor(uint srcAnd) { return (srcAnd & 4u) != 0u ? max(FS_SIGMA_N_FLOOR_M, FS_DEPTH_PRIOR_SIGMA_FLOOR_M) : FS_SIGMA_N_FLOOR_M; }   // bit 2 = FS_MEAS_SRC_DEPTH_PRIOR
#endif // FS_FUSION_GLSL
