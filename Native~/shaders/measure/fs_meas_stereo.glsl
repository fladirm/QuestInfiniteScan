// C10 PCA L/R stereo + C11 temporal multiview solve (twin: fs::meas::StereoSolve in fs_meas_math.h). Requires the frame
// block and camL / camR / camK bound with a LINEAR sampler. Camera 0 = current left PCA frame (reference), 1 = current right
// PCA frame (L/R pair), 2 = bound keyframe (an earlier left PCA frame 20-50 cm away). Reference ray = camera 0 through the
// prior point; hypotheses spaced in disparity around the prior (band = FS_STEREO_BAND_K x prior sigma, capped); each scored by
// ZNCC of a 3x3 world patch on the prior's tangent plane projected through BOTH cameras' own located pose and calibrated
// pinhole intrinsics (homologous world samples). Subpixel parabola on the cost curve; sigma_z = z^2 sigma_d / (f b).
#ifndef FS_MEAS_STEREO_GLSL
#define FS_MEAS_STEREO_GLSL
const uint FS_STEREO_OK = 0u, FS_STEREO_ST_LOWTEX = 1u, FS_STEREO_ST_AMBIG = 2u, FS_STEREO_ST_EDGE = 3u, FS_STEREO_ST_NOCOVER = 4u, FS_STEREO_ST_SKIP = 5u;
mat4 fsCamM(uint e) { return e == 2u ? blk.keyCamFromWorld : blk.camFromWorld[e]; }
vec4 fsCamK(uint e) { return e == 2u ? blk.keyIntrinsics : blk.camIntrinsics[e]; }
vec3 fsCamCentre(uint e) { mat4 M = fsCamM(e); return -(transpose(mat3(M)) * M[3].xyz); }
// Appearance sample (contract §14, C16a; twin: SampleColor): the world point projected into PCA camera `eye` (0 left, 1 right); RGB8 |
// FS_MEAS_COLOR_VALID, else 0.
uint fsSampleColor(uint eye, vec3 pWorld) {
    if ((blk.camInfo.z & (1u << eye)) == 0u || blk.camInfo.x == 0u || blk.camInfo.y == 0u) return 0u;
    vec3 pcam = (blk.camFromWorld[eye] * vec4(pWorld, 1.0)).xyz;
    if (pcam.z <= 0.05) return 0u;
    vec4 k = blk.camIntrinsics[eye];
    float u = k.z + k.x * pcam.x / pcam.z, v = k.w + k.y * pcam.y / pcam.z;
    if (u < 0.0 || v < 0.0 || u >= float(blk.camInfo.x) || v >= float(blk.camInfo.y)) return 0u;
    ivec2 px = ivec2(int(u), int(v));
    if (blk.camInfo.w != 0u) px.y = int(blk.camInfo.y) - 1 - px.y;
    vec4 c = eye == 0u ? texelFetch(camL, px, 0) : texelFetch(camR, px, 0);
    uvec3 c8 = uvec3(clamp(c.rgb, 0.0, 1.0) * 255.0 + 0.5);
    return FS_MEAS_COLOR_VALID | c8.r | (c8.g << 8) | (c8.b << 16);
}
// pixel position (bottom-left origin) of world point pw in camera e; false behind the camera
bool fsCamPixel(uint e, vec3 pw, out vec2 px, out float zc) {
    vec3 pcam = (fsCamM(e) * vec4(pw, 1.0)).xyz; zc = pcam.z; px = vec2(-1.0);
    if (pcam.z <= 0.05) return false;
    vec4 k = fsCamK(e); px = vec2(k.z + k.x * pcam.x / pcam.z, k.w + k.y * pcam.y / pcam.z);
    return true;
}
float fsCamW(uint e) { return float(e == 2u ? blk.keyInfo.x : blk.camInfo.x); }
float fsCamH(uint e) { return float(e == 2u ? blk.keyInfo.y : blk.camInfo.y); }
// effective (ray-perpendicular) baseline of camera `other` against the left camera ray through pw (twin: EffectiveBaseline)
float fsEffectiveBaseline(uint other, vec3 pw) {
    vec3 cL = fsCamCentre(0u), dv = fsCamCentre(other) - cL, dir = normalize(pw - cL);
    return length(dv - dot(dv, dir) * dir);
}
// luma of world point pw in camera e; false when a bilinear footprint leaves the image
bool fsCamLuma(uint e, vec3 pw, out float luma) {
    luma = 0.0;
    vec3 pc = (fsCamM(e) * vec4(pw, 1.0)).xyz;
    if (pc.z <= 0.05) return false;
    vec4 k = fsCamK(e);
    float u = k.z + k.x * pc.x / pc.z, v = k.w + k.y * pc.y / pc.z;      // pixel coords, bottom-left origin
    float W = float(e == 2u ? blk.keyInfo.x : blk.camInfo.x), H = float(e == 2u ? blk.keyInfo.y : blk.camInfo.y);
    if (u < 1.0 || v < 1.0 || u > W - 1.0 || v > H - 1.0) return false;
    float t = (e == 2u ? blk.keyInfo.w : blk.camInfo.w) != 0u ? H - v : v;   // texture row coordinate
    vec2 uv = vec2(u / W, t / H);
    vec3 c = (e == 0u ? textureLod(camL, uv, 0.0) : (e == 1u ? textureLod(camR, uv, 0.0) : textureLod(camK, uv, 0.0))).rgb;
    luma = dot(c, vec3(0.2126, 0.7152, 0.0722));
    return true;
}
// Solves one texel against camera `other`. pPrior / nPrior world (n facing the viewer), sigmaPrior (m), bandMaxPx = band cap.
uint fsStereoSolve(uint other, vec3 pPrior, vec3 nPrior, float sigmaPrior, float bandMaxPx, float bMin, out vec3 pOut, out float sigmaOut, out float znccOut) {
    pOut = pPrior; sigmaOut = sigmaPrior; znccOut = 0.0;
    vec3 cL = fsCamCentre(0u);
    float b = fsEffectiveBaseline(other, pPrior), fx = blk.camIntrinsics[0].x;   // C11R: ray-perpendicular baseline (forward motion has no parallax)
    vec3 pcL = (blk.camFromWorld[0] * vec4(pPrior, 1.0)).xyz;
    if (pcL.z <= 0.1 || b < bMin) return FS_STEREO_ST_SKIP;
    vec3 rayW = transpose(mat3(blk.camFromWorld[0])) * (pcL / pcL.z);    // world direction per unit of left-camera depth
    float z0 = pcL.z, d0 = fx * b / z0;
    const int halfN = (FS_STEREO_HYPS - 1) / 2;
    float halfBand = min(max(FS_STEREO_BAND_K * sigmaPrior * fx * b / (z0 * z0), float(halfN) * FS_STEREO_STEP_MIN_PX), bandMaxPx);
    float step = halfBand / float(halfN);
    vec3 t1, t2; { vec3 up = abs(nPrior.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0); t1 = normalize(cross(nPrior, up)); t2 = cross(nPrior, t1); }
    float cost[FS_STEREO_HYPS]; bool lowTexCentre = false;
    for (int k = 0; k < FS_STEREO_HYPS; ++k) {
        cost[k] = 1.0;
        float d = d0 + float(k - halfN) * step;
        if (d <= 0.5) continue;
        float z = fx * b / d;
        vec3 X = cL + rayW * z;
        float s = FS_STEREO_PATCH_PX * z / fx;
        float a[9], r[9]; float ma = 0.0, mr = 0.0;
        for (int j = 0; j < 9; ++j) {
            vec3 q = X + t1 * (float(j % 3 - 1) * s) + t2 * (float(j / 3 - 1) * s);
            if (!fsCamLuma(0u, q, a[j]) || !fsCamLuma(other, q, r[j])) return FS_STEREO_ST_NOCOVER;
            ma += a[j]; mr += r[j];
        }
        ma /= 9.0; mr /= 9.0;
        float saa = 0.0, srr = 0.0, sar = 0.0;
        for (int j = 0; j < 9; ++j) { float da = a[j] - ma, dr = r[j] - mr; saa += da * da; srr += dr * dr; sar += da * dr; }
        float sa = sqrt(saa / 9.0), sr = sqrt(srr / 9.0);
        if (k == halfN && (sa < FS_STEREO_MIN_STD || sr < FS_STEREO_MIN_STD)) lowTexCentre = true;
        if (sa < FS_STEREO_MIN_STD || sr < FS_STEREO_MIN_STD) continue;
        cost[k] = 1.0 - sar / sqrt(saa * srr);
    }
    if (lowTexCentre) return FS_STEREO_ST_LOWTEX;
    int kb = 0; for (int k = 1; k < FS_STEREO_HYPS; ++k) if (cost[k] < cost[kb]) kb = k;
    float c2 = 2.0; for (int k = 0; k < FS_STEREO_HYPS; ++k) if (abs(k - kb) >= 2) c2 = min(c2, cost[k]);
    float zncc = 1.0 - cost[kb];
    if (zncc < FS_STEREO_MIN_ZNCC || c2 < cost[kb] + FS_STEREO_UNIQ_MARGIN) return FS_STEREO_ST_AMBIG;
    if (kb == 0 || kb == FS_STEREO_HYPS - 1) return FS_STEREO_ST_EDGE;
    float cm = cost[kb - 1], c0 = cost[kb], cp = cost[kb + 1], den = cm - 2.0 * c0 + cp;
    float delta = den > 1e-6 ? clamp(0.5 * (cm - cp) / den, -0.5, 0.5) : 0.0;
    float dStar = d0 + (float(kb - halfN) + delta) * step;
    float zStar = fx * b / dStar;
    pOut = cL + rayW * zStar;
    sigmaOut = zStar * zStar * (FS_STEREO_SIGMA_D_PX / (zncc * zncc)) / (fx * b);
    znccOut = zncc;
    return FS_STEREO_OK;
}
#endif
