// C10 PCA L/R stereo solve (twin: fs::meas::StereoSolve in fs_meas_math.h). Requires the frame block, camL / camR bound
// with a LINEAR sampler, and FS_MEAS_B_COUNTERS writable. Reference = the left PCA camera ray through the Env Depth point;
// hypotheses are spaced in disparity around the prior (band = FS_STEREO_BAND_K x the prior sigma); each hypothesis is
// scored by ZNCC of a 3x3 world patch on the prior's tangent plane projected through BOTH cameras' own located pose and
// calibrated pinhole intrinsics (homologous world samples, not same-pixel offsets). Subpixel parabola on the cost curve;
// sigma_z = z^2 sigma_d / (f b).
#ifndef FS_MEAS_STEREO_GLSL
#define FS_MEAS_STEREO_GLSL
const uint FS_STEREO_OK = 0u, FS_STEREO_ST_LOWTEX = 1u, FS_STEREO_ST_AMBIG = 2u, FS_STEREO_ST_EDGE = 3u, FS_STEREO_ST_NOCOVER = 4u, FS_STEREO_ST_SKIP = 5u;
vec3 fsCamCentre(uint e) { mat4 M = blk.camFromWorld[e]; return -(transpose(mat3(M)) * M[3].xyz); }
// luma of world point pw in camera e; false when a bilinear footprint leaves the image
bool fsCamLuma(uint e, vec3 pw, out float luma) {
    luma = 0.0;
    vec3 pc = (blk.camFromWorld[e] * vec4(pw, 1.0)).xyz;
    if (pc.z <= 0.05) return false;
    vec4 k = blk.camIntrinsics[e];
    float u = k.z + k.x * pc.x / pc.z, v = k.w + k.y * pc.y / pc.z;      // pixel coords, bottom-left origin
    float W = float(blk.camInfo.x), H = float(blk.camInfo.y);
    if (u < 1.0 || v < 1.0 || u > W - 1.0 || v > H - 1.0) return false;
    float t = blk.camInfo.w != 0u ? H - v : v;                            // texture row coordinate
    vec3 c = (e == 0u ? textureLod(camL, vec2(u / W, t / H), 0.0) : textureLod(camR, vec2(u / W, t / H), 0.0)).rgb;
    luma = dot(c, vec3(0.2126, 0.7152, 0.0722));
    return true;
}
// Solves one texel. pEnv / nEnv world (nEnv facing the depth eye), sigmaEnv = prior sigma (m). Out: world endpoint, sigma, zncc.
uint fsStereoSolve(vec3 pEnv, vec3 nEnv, float sigmaEnv, out vec3 pOut, out float sigmaOut, out float znccOut) {
    pOut = pEnv; sigmaOut = sigmaEnv; znccOut = 0.0;
    vec3 cL = fsCamCentre(0u), cR = fsCamCentre(1u);
    float b = length(cR - cL), fx = blk.camIntrinsics[0].x;
    vec3 pcL = (blk.camFromWorld[0] * vec4(pEnv, 1.0)).xyz;
    if (pcL.z <= 0.1 || b < 0.01) return FS_STEREO_ST_SKIP;
    vec3 rayW = transpose(mat3(blk.camFromWorld[0])) * (pcL / pcL.z);    // world direction per unit of left-camera depth
    float z0 = pcL.z, d0 = fx * b / z0;
    const int halfN = (FS_STEREO_HYPS - 1) / 2;
    float halfBand = min(max(FS_STEREO_BAND_K * sigmaEnv * fx * b / (z0 * z0), float(halfN) * FS_STEREO_STEP_MIN_PX), FS_STEREO_BAND_MAX_PX);
    float step = halfBand / float(halfN);
    vec3 t1, t2; { vec3 up = abs(nEnv.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0); t1 = normalize(cross(nEnv, up)); t2 = cross(nEnv, t1); }
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
            if (!fsCamLuma(0u, q, a[j]) || !fsCamLuma(1u, q, r[j])) return FS_STEREO_ST_NOCOVER;
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
