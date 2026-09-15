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
#define FS_MEAS_DEFAULT_BUDGET       16384     // C09R-E2 (contract §7.6, provisional 16-64 k): measurements selected per depth frame
#define FS_MEAS_DEFAULT_MAX_OUT      32768     // hard cap of the emitted records (>= budget; the fraction rounding of the threshold bin never reaches it)
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
#define FS_MEAS_SRC_DEPTH_PRIOR      4u        // bit 2
#define FS_MEAS_SRC_EDGE             256u      // bit 8
#define FS_MEAS_SRC_LOW_TEXTURE      512u      // bit 9
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
#define FS_MEAS_CTR_WORDS            16        // counters region (words 13..15 reserved)
#define FS_MEAS_FB_BYTES             800       // 64 B counters + 2 x mat4 anchorFromEye + 2 x vec4 fov + 2 x mat4 worldFromEye + 2 x mat4 predViewProj + 2 x mat4 predInvViewProj + uvec4 predInfo + 2 x mat4 camFromWorld + 2 x vec4 camIntrinsics + uvec4 camInfo
// ---- appearance sample (C16a, contract §14 keyframe authority = the PCA frame lease at its own pose/time): the emit kernel
// projects the measured point into the PCA camera of the same eye captured closest to the depth time and stores RGB8 in
// FsSurfaceMeasurement.reserved with FS_MEAS_COLOR_VALID; fusion blends it precision-weighted into appearanceHandle.
#define FS_MEAS_CAM_MAX_AGE_NS       120000000  // |depth time - camera time| above this: no colour sample (the frame is not the same moment)
#define FS_MEAS_SCORE_BINS           256

// ---- bindings ----------------------------------------------------------------------------------------------
#define FS_MEAS_B_DEPTH              0         // combined image sampler, sampler2DArray (nearest): Environment Depth
#define FS_MEAS_B_RECORDS            1         // FsSurfaceMeasurement[] (ring slot records)
#define FS_MEAS_B_COUNTERS           2         // frame block: uint[FS_MEAS_CTR_WORDS] + per-eye params + prediction params
#define FS_MEAS_B_PRED               3         // combined image sampler, sampler2DArray: canonical prediction (previous rendered depth)
#define FS_MEAS_B_SCORE              4         // uint score[texel threads] (scratch, one job in flight)
#define FS_MEAS_B_SELECT             5         // uint hist[256] + wgCount[FS_MEAS_MAX_GROUPS] + wgBase[FS_MEAS_MAX_GROUPS]
#define FS_MEAS_B_CAM_L              6         // combined image sampler: PCA left frame (owned copy)
#define FS_MEAS_B_CAM_R              7         // combined image sampler: PCA right frame
#define FS_MEAS_SEL_HIST             0
#define FS_MEAS_SEL_WGCOUNT          FS_MEAS_SCORE_BINS
#define FS_MEAS_SEL_WGBASE           (FS_MEAS_SCORE_BINS + FS_MEAS_MAX_GROUPS)
#define FS_MEAS_SEL_WORDS            (FS_MEAS_SCORE_BINS + 2 * FS_MEAS_MAX_GROUPS)

#endif // FS_MEAS_PARAMS_H
