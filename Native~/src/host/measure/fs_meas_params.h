// FinalScan measurement front-end parameters (C09: Environment Depth -> SurfaceMeasurement). Preprocessor
// only so the same file is included by C++ (fs_meas_math.h) and GLSL (fs_meas_common.glsl). Every number
// is provisional (contract §6, §7.4, §7.6) until C10/C11 replace the depth model and refine the information score.
#ifndef FS_MEAS_PARAMS_H
#define FS_MEAS_PARAMS_H

// ---- kernel shape ------------------------------------------------------------------------------------
#define FS_MEAS_WG                   64        // one thread per depth texel of BOTH layers (lanes alternate eyes), one dispatch
#define FS_MEAS_DEPTH_W              320       // measured Env Depth extent (C01); the kernel reads w/h from push constants
#define FS_MEAS_DEPTH_H              320
#define FS_MEAS_DEPTH_LAYERS         2

// ---- geometry gates ------------------------------------------------------------------------------------
#define FS_MEAS_MAX_DEPTH_M          6.0       // provisional far gate for the depth prior (contract §6)
#define FS_MEAS_MIN_DEPTH_M          0.5       // contract §6 invariant: closer texels (hands, cables, controllers) are never measurements
#define FS_MEAS_EDGE_RATIO           0.10      // neighbour depth step > 10 % of depth = discontinuity (edge, bit 8)
#define FS_MEAS_FLAT_LAPLACIAN_RATIO 0.005     // |laplacian| < 0.5 % of depth = flat region (lowTexture, bit 9)

// ---- provisional Env Depth uncertainty model (contract §6 / §7.4): sigmaN = 0.02 d^2 + 0.01 m -------------
#define FS_MEAS_SIGMA_QUAD           0.02
#define FS_MEAS_SIGMA_BASE_M         0.01

// ---- device measurement ring (fs_meas_gpu.h): two slots so compaction(N+1) overlaps integrate(N) --------------
#define FS_MEAS_GPU_RING_CAPACITY    65536     // FsSurfaceMeasurement records per slot (3 MiB), power of two
#define FS_MEAS_GPU_RING_SLOTS       2
#define FS_MEAS_DEFAULT_BUDGET       4096      // device-derived bounded information set: finish the observation instead of abandoning 16k partial work
#define FS_MEAS_DEFAULT_MAX_OUT      8192      // hard cap with threshold-bin/C11R2 headroom; ring capacity remains unchanged
#define FS_MEAS_MAX_GROUPS           4096      // workgroups of a 320x320x2 frame = 3200 (count/prefix scratch)
#define FS_MEAS_ROW_PHASE_STRIDE     97        // rows the texel order rotates per frame (the emit order is a pure function of frame + selection)

// ---- information score (C09R-E2, contract §7.6): a pure function of the canonical prediction (FRONT reprojected into
// the depth camera = the previous rendered depth), the residual in sigma units, curvature, steepness and DETAIL.
// HIGH: no prediction (newly seen), large residual (new surface / moved / error), steep, curved, DETAIL.
// LOW:  prediction agrees within sigma on a flat converged patch. Score 0 = never emitted.
#define FS_MEAS_SCORE_NEW            224       // valid texel without canonical prediction
#define FS_MEAS_SCORE_FAR_BASE       192       // residual > 3 sigma: 192 + min(63, 4 r)
#define FS_MEAS_SCORE_BAND_BASE      96        // 1 < residual <= 3 sigma: 96 + 48 (r - 1)
#define FS_MEAS_SCORE_CONVERGED_BASE 16        // residual <= 1 sigma: 16 + 32 r
#define FS_MEAS_SCORE_CURVATURE      24        // not flat (Laplacian above the flat ratio)
#define FS_MEAS_SCORE_STEEP          40        // neighbour step > half the edge ratio (thin / oblique / near a discontinuity)
#define FS_MEAS_SCORE_DETAIL_MIN     200       // DETAIL request: never below this
#define FS_MEAS_STEEP_RATIO          0.05      // = FS_MEAS_EDGE_RATIO / 2

// ---- push-constant flags -------------------------------------------------------------------------------
#define FS_MEAS_FLAG_FLIP_Y          1u        // texture row 0 is the bottom (tanDown) edge instead of the top
#define FS_MEAS_FLAG_LINEAR_DEPTH    2u        // texel value is metres along the eye z axis (not [0,1] projected depth)
#define FS_MEAS_FLAG_DETAIL          4u        // keep edge texels as geometry (flagged) instead of skipping them
#define FS_MEAS_FLAG_PRED_FLIP_Y     8u        // prediction texture row 0 is the bottom edge (Unity RT convention on device: receipt-driven)

// ---- FsSurfaceMeasurement.sourceFlags bits written here ----------------------------------------------------
#define FS_MEAS_SRC_STEREO           1u        // bit 0: C10 PCA L/R stereo measurement (geometry authority = stereo, sigma from disparity)
#define FS_MEAS_SRC_TEMPORAL         2u        // bit 1: C11 temporal large-baseline multiview (current left PCA frame vs a keyframe 20-50 cm away)
#define FS_MEAS_SRC_DEPTH_PRIOR      4u        // bit 2
#define FS_MEAS_SRC_EDGE             256u      // bit 8
#define FS_MEAS_SRC_LOW_TEXTURE      512u      // bit 9
#define FS_MEAS_SRC_PLANAR           1024u     // bit 10: C11 robust plane fit of the Env Depth tile (low-texture path)
#define FS_MEAS_SRC_CAND_STEREO      2048u     // bit 11: C10R candidate for the bounded stereo refine pass (cleared by it)
#define FS_MEAS_SRC_CAND_TEMPORAL    4096u     // bit 12: C11R candidate for the bounded temporal refine pass (cleared by it)
#define FS_MEAS_SRC_TARGET           8192u     // bit 13: C11R2 record generated from a SurfaceComplex refinement target (kept only when the temporal solve accepts it)
#define FS_MEAS_SRC_EYE_SHIFT        16        // bit 16: eye (0 left, 1 right) the record was back-projected from (free-space ray origin)

// ---- frame block (per ring slot, host-visible): u32 counters (reset by the CPU before each job) followed by the
// per-eye parameters the CPU writes before submit and the selection words the select kernel writes
// (twin: fs::meas::FrameBlock / GLSL FrameBlock) ------------------------------------------------------------------
#define FS_MEAS_CTR_RESERVED         0         // records emitted (= selected, written by the prefix kernel; <= maxOut)
#define FS_MEAS_CTR_OVERFLOW         1         // selected records refused by the maxOut cap (fraction rounding only)
#define FS_MEAS_CTR_VALID            2         // texels with a usable measurement (score > 0)
#define FS_MEAS_CTR_EDGE             3         // texels flagged edge (skipped unless DETAIL)
#define FS_MEAS_CTR_LOWTEX           4         // emitted records flagged lowTexture
#define FS_MEAS_CTR_INVALID          5         // no depth / out of gate / border / neighbour hole
#define FS_MEAS_CTR_REJECTED         6         // valid texels below the selection threshold (information too low for the budget)
#define FS_MEAS_CTR_GROUPS           7         // workgroups that ran (telemetry sanity)
#define FS_MEAS_CTR_PREDICTED        8         // valid texels with a canonical prediction
#define FS_MEAS_CTR_CONSISTENT       9         // predicted texels within 1 sigma (converged)
#define FS_MEAS_CTR_NEW              10        // valid texels without a prediction (newly seen)
#define FS_MEAS_CTR_THRESHOLD        11        // selection threshold score of this frame
#define FS_MEAS_CTR_FRACTION         12        // 16.16 fraction of the threshold bin kept
#define FS_MEAS_CTR_CONSISTENT_ALT   13        // predicted texels within 1 sigma under the OTHER prediction row convention (receipt)
// C10 stereo receipts (emit kernel, per selected texel with an Env Depth prior closer than FS_STEREO_MAX_DEPTH_M)
#define FS_MEAS_CTR_STEREO_TESTED    16        // texels the stereo solve ran on (pair valid, prior in range)
#define FS_MEAS_CTR_STEREO_VALID     17        // stereo measurements emitted (geometry from the L/R match)
#define FS_MEAS_CTR_STEREO_LOWTEX    18        // left/right world patch below FS_STEREO_MIN_STD: Env Depth record kept
#define FS_MEAS_CTR_STEREO_AMBIG     19        // ZNCC below FS_STEREO_MIN_ZNCC or not unique: Env Depth record kept
#define FS_MEAS_CTR_STEREO_EDGEBAND  20        // best hypothesis on the band edge (prior disagrees beyond the band): Env Depth record kept
#define FS_MEAS_CTR_STEREO_NOCOVER   21        // a patch sample left a camera image
#define FS_MEAS_CTR_STEREO_BIN_N     22        // 4 words: valid stereo count per prior distance bin [<0.75, <1.5, <2.5, >=2.5 m] (targets 0.5/1/2/3 m)
#define FS_MEAS_CTR_STEREO_BIN_RES   26        // 4 words: sum |z_stereo - z_envdepth| per bin (0.1 mm units): EnvDepth-only vs stereo residual
#define FS_MEAS_CTR_STEREO_SIGMA_UM  30        // sum of the stereo sigma of valid records (um)
#define FS_MEAS_CTR_ENV_SIGMA_UM     31        // sum of the Env Depth sigma of the same records (um)
// C11 receipts
#define FS_MEAS_CTR_TEMPORAL_TESTED  32        // texels the temporal solve ran on (keyframe bound, textured, prior in range)
#define FS_MEAS_CTR_TEMPORAL_VALID   33        // temporal measurements emitted
#define FS_MEAS_CTR_TEMPORAL_LOWTEX  34
#define FS_MEAS_CTR_TEMPORAL_AMBIG   35
#define FS_MEAS_CTR_TEMPORAL_EDGE    36
#define FS_MEAS_CTR_TEMPORAL_NOCOVER 37        // the point is not visible in the keyframe image
#define FS_MEAS_CTR_TEMPORAL_DISAGREE 38       // temporal endpoint outside 3 sigma of a valid L/R stereo endpoint: neither overrides, stereo kept
#define FS_MEAS_CTR_TEMPORAL_SIGMA_UM 39
#define FS_MEAS_CTR_PLANAR_TESTED    40        // low-texture texels the planar fit ran on
#define FS_MEAS_CTR_PLANAR_VALID     41
#define FS_MEAS_CTR_PLANAR_REJECTED  42        // too few inliers or residual beyond the flat bound (not a plane)
#define FS_MEAS_CTR_PLANAR_SIGMA_UM  43
#define FS_MEAS_CTR_PLANAR_RMS_UM    44
#define FS_MEAS_CTR_CAND_STEREO      45        // C10R records the cheap gates admitted to the stereo refine pass
#define FS_MEAS_CTR_CAND_TEMPORAL    46        // C11R records admitted to the temporal refine pass
#define FS_MEAS_CTR_REFINE_SKIPPED   47        // candidate records not refined: the frame's refine budget ended first
// C11R2 surface-driven temporal refinement receipts
#define FS_MEAS_CTR_TGT_CONSIDERED   48        // targets handed from the topology pass to this frame
#define FS_MEAS_CTR_TGT_TEXTURE      49        // ... with textured tiles in the left image and the keyframe
#define FS_MEAS_CTR_TGT_KEYFRAME     50        // ... with a keyframe bound and the point + band inside it
#define FS_MEAS_CTR_TGT_BASELINE_REJ 51        // ... refused: effective baseline below FS_TEMPORAL_BEFF_MIN_M
#define FS_MEAS_CTR_TGT_VISIBILITY_REJ 52      // ... refused: behind / outside the left camera or seen at grazing incidence
#define FS_MEAS_CTR_TGT_WRITTEN      53        // candidate records appended after the frame's records
#define FS_MEAS_CTR_TGT_SOLVED       54        // temporal solves run on target records
#define FS_MEAS_CTR_TGT_ACCEPTED     55        // accepted (kept as FS_MEAS_SRC_TEMPORAL measurements)
#define FS_MEAS_CTR_TGT_SIGMA_BEFORE_UM 56     // sum of the target sigma of accepted records
#define FS_MEAS_CTR_TGT_SIGMA_AFTER_UM 57      // sum of the temporal sigma of accepted records
#define FS_MEAS_CTR_TGT_INFO_GAIN    58        // sum of log2(sigmaBefore / sigmaAfter) x 1000 (information gain, accepted)
#define FS_MEAS_CTR_WORDS            64        // counters region
#define FS_MEAS_FB_BYTES             1232      // 256 B counters + 2 x mat4 anchorFromEye + 2 x vec4 fov + 2 x mat4 worldFromEye + 2 x mat4 predViewProj + 2 x mat4 predInvViewProj + uvec4 predInfo + 2 x mat4 camFromWorld + 2 x vec4 camIntrinsics + uvec4 camInfo + mat4 anchorFromWorld + uvec4 stereoInfo + mat4 keyCamFromWorld + vec4 keyIntrinsics + uvec4 keyInfo + mat4 worldFromAnchor
// ---- appearance sample (C16a, contract §14 keyframe authority = the PCA frame lease at its own pose/time): the emit kernel
// projects the measured point into the PCA camera of the same eye captured closest to the depth time and stores RGB8 in
// FsSurfaceMeasurement.reserved with FS_MEAS_COLOR_VALID; fusion blends it precision-weighted into appearanceHandle.
#define FS_MEAS_CAM_MAX_AGE_NS       120000000  // |depth time - camera time| above this: no colour sample (the frame is not the same moment)
#define FS_MEAS_SCORE_BINS           256
// ---- C10 PCA L/R stereo (contract §6: Env Depth only as disparity prior + free space). The pair is committed by the managed
// StereoPairer by capture timestamps; the solve projects world-space hypotheses along the left PCA ray through each camera's
// own located pose and calibrated pinhole intrinsics (Camera2 reports no lens distortion for the PCA streams: C01 probe,
// ACAMERA_LENS_DISTORTION = [] for ids 50/51, images are delivered rectilinear), so no image resampling is needed.
#define FS_STEREO_MAX_DEPTH_M        3.5       // beyond: disparity < ~16 px, stereo sigma no better than the prior
#define FS_STEREO_MAX_AGE_NS         120000000 // |depth time - pair time| above this: no stereo for this depth frame
#define FS_STEREO_HYPS               13        // disparity hypotheses (odd; centre = Env Depth prior)
#define FS_STEREO_STEP_MIN_PX        0.5       // hypothesis spacing floor (pixels of disparity)
#define FS_STEREO_BAND_MAX_PX        6.0       // band half-width cap (pixels): spacing stays <= 1 px so the subpixel parabola is sampled
#define FS_STEREO_BAND_K             3.0       // band half-width = K x Env Depth sigma, in disparity, at least (HYPS-1)/2 x step
#define FS_STEREO_PATCH_PX           4.0       // world patch spacing = this many left-camera pixels at the hypothesis depth (3x3 samples, 8 px span: aperture)
#define FS_STEREO_MIN_STD            0.015     // luma std of each 3x3 patch (0..1) below this = textureless
#define FS_STEREO_MIN_ZNCC           0.85      // best hypothesis must correlate at least this well
#define FS_STEREO_UNIQ_MARGIN        0.05      // best cost + margin < best non-neighbour cost (unique minimum)
#define FS_STEREO_SIGMA_D_PX         0.25      // subpixel disparity sigma at ZNCC 1 (divided by ZNCC^2)
// ---- C11 temporal multiview + planar path (contract §6, provisional) ---------------------------------------------------------
#define FS_MEAS_KEYFRAMES            4         // keyframe slots (managed store: left PCA copies spaced by camera motion)
#define FS_TEMPORAL_BASELINE_MIN_M   0.20      // usable keyframe: camera centre 20..50 cm from the current left PCA camera
#define FS_TEMPORAL_BASELINE_MAX_M   0.50
#define FS_TEMPORAL_BASELINE_BEST_M  0.30      // preferred baseline (closest wins)
#define FS_TEMPORAL_MAX_ANGLE_DEG    35.0      // optical axes within this angle (co-visibility / foreshortening of the 3x3 patch)
#ifdef __cplusplus
#define FS_TEMPORAL_MAX_AGE_NS       8000000000LL // older keyframes are not trusted as the same static scene (CPU selection only: beyond GLSL int)
#endif
#define FS_TEMPORAL_BAND_MAX_PX      12.0      // band cap of the large-baseline solve (step <= 2 px over 13 hypotheses)
#define FS_TEMPORAL_AGREE_K          3.0       // temporal vs L/R stereo consistency (combined sigma units)
// ---- C10R / C11R information-first measurement pipeline (cheap gates in the compaction job, bounded refine jobs) --------------
#define FS_TEX_TILES_X               40        // PCA texture confidence map: luma std per tile of a 40 x 30 grid over the image (4x4 taps)
#define FS_TEX_TILES_Y               30
#define FS_TEX_TILES                 1200
#define FS_STEREO_MIN_TILE_STD       0.015     // tile luma std below this = textureless: no image solve is attempted
#define FS_STEREO_PATCH_MARGIN_PX    2.0       // bilinear footprint beyond the 3x3 patch
#define FS_STEREO_BEFF_MIN_M         0.03      // effective (ray-perpendicular) baseline floor of the L/R solve
#define FS_TEMPORAL_BEFF_MIN_M       0.15      // effective baseline floor of the temporal solve (forward motion has no parallax)
#define FS_MEAS_TARGETS_MAX          256       // C11R2 targets per frame (twin: FS_TEMPORAL_TARGETS)
#define FS_TEMPORAL_FACING_COS       0.26      // a target seen more grazing than 75 deg from the left camera is not a useful viewpoint
#define FS_STEREO_MIN_CONFIDENCE     0.5       // pair confidence (StereoPairer: 1 direct, 0.7 compensated, 0.4 low; halved when degraded)
#define FS_STEREO_MAX_BLUR           0.5       // pair blur penalty (MotionGate, 0..1) above this: no geometry from the images
#define FS_STEREO_MAX_UNCERTAINTY_NS 8000000   // capture-time uncertainty above this: no geometry from the pair
#define FS_MEAS_REFINE_MAX_US        6000      // GPU budget of the refine jobs of one depth frame (the rest keeps its Env Depth records)
#define FS_PLANAR_TILE               8         // Env Depth planar tiles: 8x8 texels, 16 fit points (stride 2)
#define FS_PLANAR_TILES_X            40
#define FS_PLANAR_TILES              1600      // per layer (320 / 8)^2
#define FS_PLANAR_TILE_WORDS         6         // a, b, c (float bits), rms z (float bits), inliers, valid
#define FS_PLANAR_TILE_MIN_INLIERS   12        // of 16
#define FS_PLANAR_SAMPLE_A           2         // tile-local texel (A, A) and (B, B) become the tile's planar measurements (2 per tile,
#define FS_PLANAR_SAMPLE_B           6         //   spatially separated; the other flat texels of the tile stay Env Depth records)
#define FS_PLANAR_RADIUS             2         // (2R+1)^2 window of the CPU twin PlanarFit test
#define FS_PLANAR_MIN_INLIERS        18        // of 25
#define FS_PLANAR_HUBER_K            1.345     // IRLS on the inverse-depth plane residual
#define FS_PLANAR_MAX_RMS_RATIO      0.004     // inlier RMS above 0.4 % of depth: not a plane
#define FS_PLANAR_CORR_TEXELS        4.0       // Env Depth texels are correlated: effective samples = inliers / this

// ---- bindings ----------------------------------------------------------------------------------------------
#define FS_MEAS_B_DEPTH              0         // combined image sampler, sampler2DArray (nearest): Environment Depth
#define FS_MEAS_B_RECORDS            1         // FsSurfaceMeasurement[] (ring slot records)
#define FS_MEAS_B_COUNTERS           2         // frame block: uint[FS_MEAS_CTR_WORDS] + per-eye params + prediction params
#define FS_MEAS_B_PRED               3         // combined image sampler, sampler2DArray: canonical prediction (previous rendered depth)
#define FS_MEAS_B_SCORE              4         // uint score[texel threads] (scratch, one job in flight)
#define FS_MEAS_B_SELECT             5         // uint hist[256] + wgCount[FS_MEAS_MAX_GROUPS] + wgBase[FS_MEAS_MAX_GROUPS]
#define FS_MEAS_B_CAM_L              6         // combined image sampler: PCA left frame (owned copy)
#define FS_MEAS_B_CAM_R              7         // combined image sampler: PCA right frame
#define FS_MEAS_B_CAM_K              8         // combined image sampler: C11 bound keyframe (earlier left PCA copy)
#define FS_MEAS_B_TARGETS            10        // C11R2 refinement targets (host-visible, FS_TEMPORAL_TARGETS x 9 words: pos xyz, normal xyz, sigmaN, SurfaceID, support)
#define FS_MEAS_B_TILES              9         // C10R/C11R scratch: PCA texture tiles (L, R, keyframe) + Env Depth planar tiles (both layers)
#define FS_MEAS_SEL_HIST             0
#define FS_MEAS_SEL_WGCOUNT          FS_MEAS_SCORE_BINS
#define FS_MEAS_SEL_WGBASE           (FS_MEAS_SCORE_BINS + FS_MEAS_MAX_GROUPS)
#define FS_MEAS_SEL_WORDS            (FS_MEAS_SCORE_BINS + 2 * FS_MEAS_MAX_GROUPS)

#endif // FS_MEAS_PARAMS_H
