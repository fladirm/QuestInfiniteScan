// FinalScan measurement front-end parameters (C09: Environment Depth -> SurfaceMeasurement). Preprocessor
// only so the same file is included by C++ (fs_meas_math.h) and GLSL (fs_meas_common.glsl). Every number
// is provisional (contract §6, §7.4, §7.6) until C10/C12 replace the depth model and the information score.
#ifndef FS_MEAS_PARAMS_H
#define FS_MEAS_PARAMS_H

// ---- kernel shape ------------------------------------------------------------------------------------
#define FS_MEAS_WG                   64        // one thread per depth texel of BOTH layers (lanes alternate eyes), one dispatch
#define FS_MEAS_DEPTH_W              320       // measured Env Depth extent (C01); the kernel reads w/h from push constants
#define FS_MEAS_DEPTH_H              320
#define FS_MEAS_DEPTH_LAYERS         2

// ---- geometry gates ------------------------------------------------------------------------------------
#define FS_MEAS_MAX_DEPTH_M          6.0       // provisional far gate for the depth prior (contract §6)
#define FS_MEAS_EDGE_RATIO           0.10      // neighbour depth step > 10 % of depth = discontinuity (edge, bit 8)
#define FS_MEAS_FLAT_LAPLACIAN_RATIO 0.005     // |laplacian| < 0.5 % of depth = flat region (lowTexture, bit 9)

// ---- provisional Env Depth uncertainty model (contract §6 / §7.4): sigmaN = 0.02 d^2 + 0.01 m -------------
#define FS_MEAS_SIGMA_QUAD           0.02
#define FS_MEAS_SIGMA_BASE_M         0.01

// ---- device measurement ring (fs_meas_gpu.h): two slots so backproject(N+1) overlaps integrate(N) ----------
#define FS_MEAS_GPU_RING_CAPACITY    65536     // FsSurfaceMeasurement records per slot (3 MiB), power of two
#define FS_MEAS_GPU_RING_SLOTS       2
#define FS_MEAS_DEFAULT_DECIM_K      2         // (x + y + frame) % k == 0 keeps ~1/k of the valid texels (§7.6 placeholder)
#define FS_MEAS_DEFAULT_MAX_OUT      65536     // hard cap per depth frame (= 4 x FS_INTEGRATE_MAX_MEAS); a fully covered
                                               // 320x320x2 frame at k = 2 reserves ~101 k -> ~35 k overflow, counted
#define FS_MEAS_ROW_PHASE_STRIDE     97        // rows the texel order rotates per frame: the cap cuts a different band each frame

// ---- push-constant flags -------------------------------------------------------------------------------
#define FS_MEAS_FLAG_FLIP_Y          1u        // texture row 0 is the bottom (tanDown) edge instead of the top
#define FS_MEAS_FLAG_LINEAR_DEPTH    2u        // texel value is metres along the eye z axis (not [0,1] projected depth)
#define FS_MEAS_FLAG_DETAIL          4u        // keep edge texels as geometry (flagged) instead of skipping them
#define FS_MEAS_FLAG_NO_DECIMATION   8u

// ---- FsSurfaceMeasurement.sourceFlags bits written here ----------------------------------------------------
#define FS_MEAS_SRC_DEPTH_PRIOR      4u        // bit 2
#define FS_MEAS_SRC_EDGE             256u      // bit 8
#define FS_MEAS_SRC_LOW_TEXTURE      512u      // bit 9
#define FS_MEAS_SRC_EYE_SHIFT        16        // bit 16: eye (0 left, 1 right) the record was back-projected from (free-space ray origin)

// ---- frame block (per ring slot, host-visible): u32 counters (reset by the CPU before each job) followed by the
// per-eye parameters the CPU writes before submit (twin: fs::meas::FrameBlock / GLSL FrameBlock) -------------
#define FS_MEAS_CTR_RESERVED         0         // records reserved (may exceed maxOut; stored = min(reserved, maxOut))
#define FS_MEAS_CTR_OVERFLOW         1         // records refused by the maxOut cap
#define FS_MEAS_CTR_VALID            2         // texels that produced a record (== reserved)
#define FS_MEAS_CTR_EDGE             3         // texels flagged edge (skipped unless DETAIL)
#define FS_MEAS_CTR_LOWTEX           4         // records flagged lowTexture
#define FS_MEAS_CTR_INVALID          5         // no depth / out of gate / border / neighbour hole
#define FS_MEAS_CTR_DECIMATED        6         // valid texels dropped by the (x+y+frame)%k rule
#define FS_MEAS_CTR_GROUPS           7         // workgroups that ran (telemetry sanity)
#define FS_MEAS_CTR_WORDS            16        // counters region (words 8..15 reserved)
#define FS_MEAS_FB_BYTES             224       // 64 B counters + 2 x mat4 anchorFromEye + 2 x vec4 fov

// ---- bindings ----------------------------------------------------------------------------------------------
#define FS_MEAS_B_DEPTH              0         // combined image sampler, sampler2DArray (nearest)
#define FS_MEAS_B_RECORDS            1         // FsSurfaceMeasurement[] (ring slot records)
#define FS_MEAS_B_COUNTERS           2         // frame block: uint[FS_MEAS_CTR_WORDS] + per-eye params

#endif // FS_MEAS_PARAMS_H
