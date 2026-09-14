# PERFORMANCE_BUDGET (C01 measured, provisional budgets for C02–C13)

Source: `docs/evidence/C01/DEVICE_ENVELOPE_20260914T154429Z.md` (Quest 3S panther, Horizon OS v207,
Adreno 740 driver 512.837.9, Unity 6000.5.9f1, app `eu.monle.finalscan` 0.1.0-c01, indoor room light).
Every number below is *measured* unless marked *provisional*.

## Sensor

| Item | Measured | Budget / rule |
|---|---|---|
| PCA 1280×960 @30 (L+R) | 29.7 fps per eye | NORMAL_30 profile baseline |
| PCA 1280×960 @60 (L+R) | 49.6 fps per eye (sensor 50 Hz mode) | NORMAL_60 is really ≤ 50 fps indoors; governor treats delivered < 0.8× as LOWLIGHT |
| PCA 1280×1280 @60 / @30 | 49.6 / 28.3–29.8 fps | 1280×1280 costs nothing extra in cadence; default resolution 1280×1280 (wider FOV) *provisional* |
| Capture → receive latency | p50 30 ms, p90 35, p99 37, max 62 | pose ring ≥ 2 s; xrLocateSpace at callback (≤ 50 ms guaranteed history OK) |
| L/R nearest-timestamp delta @30 | 0 / 20 / 40 ms (622/228/221 of 1188) | pairing window = 1.5 × period (50 ms @30, 30 ms @50); skew 20 ms = motion-compensate class |
| L/R delta @50 | 0 / 20 ms (992/972) | same |
| Timestamp monotonic violations | 0 (managed path) | validator stays on (HAL evidence from donors) |
| Clock gate | ovr→monotonic slope 1.000000035, residual rms 6 µs, max 32 µs | **path A** (`_timestampNsMonotonic` is XrTime base); uncertainty budget 50 µs |
| Intrinsics | fx≈870 px @1280 width, cx,cy≈640, sensor 1280×1280 | baseline 63.4 mm (lens offsets ±31.7 mm); σz(1 m, σd 0.25 px) ≈ 4.5 mm; σz(3 m) ≈ 4 cm |
| Head pose delta per PCA frame | p50 2 mm / 0.6°, p99 9 mm / 2.9° @30 | motion gate 90°/s *provisional* (never hit in probe) |
| Env Depth | 25 Hz (XrTime Δ 40 ms), age p50 56 ms p99 79 ms, 320×320×2, poses+fovs OK, near 0.1 m, far ∞ | prior only; reproject with its own pose; dedupe repeats (callback 72 Hz) |
| PSS baseline | init 314 MB → XR+passthrough 405 MB → +L+R PCA 420–427 MB (+15–22 MB) → +depth +5 MB | working budget = 5.75 GiB − ~0.45 GB baseline; governor watermark **3.5 GB** *provisional* (contract §16) |
| Thermal (3 min) | 0 warnings, headroom 0.53–0.58, GPU level 2 SustainedHigh | 60 min run pending (C31) |

## Vulkan / GPU

| Item | Measured | Budget / rule |
|---|---|---|
| Queue topology | family 0 G|C|T ×4 (48-bit ts), family 1 sparse-only ×1 | scanner queue = family 0 index 1 (injected, VK_SUCCESS, no alias) |
| Extensions enabled by intercept | sync2, timeline, dynamic_rendering, maintenance5, pipeline_binary, host_query_reset, BDA (+43 Unity) | ycbcr conversion feature NOT enabled (AHB YUV import failed) → enable in C02 intercept; fp16/int8 features not enabled → enable when supported |
| Empty submit (scanner queue) | CPU submit 0.2 µs median, fence complete ~23 µs, roundtrip 0.7 µs | per-job CPU overhead negligible; jobs may be ≤ 1 ms |
| Overlap test | 2 ms jobs back-to-back, duty 93 % for 10 s: frame p50 13.90 ms (pre 13.88, post 13.89); **0 frame marks inside scanner jobs** | GPU **interleaves** at job granularity, no concurrency. Design point: quantum ≤ 2 ms, per-frame scan budget **2.5 ms** *provisional*, adaptive from GPU headroom |
| Timestamps | period 52.083 ns, 48 bits, compute+graphics | CPU↔GPU offset via frame marks (no calibrated_timestamps) |
| Pipeline create (trivial compute) | no cache 9.6 ms; own VkPipelineCache warm 0.32 ms; pipeline_binary create-from-binary 0.09 ms; key 16 B global / 4 B per pipeline; binary 4.5 KB | warm-up before READY; cache + binaries under `<files>/finalscan/` |
| AHB → VkImage | RGBA8 import OK (dedicated alloc); YUV_420_888 fails (ycbcr feature off) | fix in C02 intercept; zero-copy camera path stays a spike (Camera2 NDK opened cameras 1/50/51, TIMESTAMP_SOURCE UNKNOWN → path C for that route) |
| Frame time (probe app, no scan) | p50 13.9 ms, p99 17 ms, max 57 ms (startup) | 72 Hz target holds; FrameTimingManager must be enabled (was off) |

## Budgets adopted for C02–C13 (*provisional*, re-measured in C30)

| Class | Quantum | In flight | Per-frame share |
|---|---|---|---|
| PUBLISH | 0.5 ms | 4 | first |
| SCAN | 2.0 ms | 2 | ≤ 2.5 ms total scan+publish |
| INNER_RESIDENCY | 2.0 ms | 2 | only when pages missing |
| APPEARANCE | 1.5 ms | 1 | leftover |
| COLD | 1.0 ms | 1 | leftover |

Render: cull + HZB ≤ 1.0 ms, draw ≤ 4 ms at 200 k leaf surfels *provisional* (C05 benchmark decides).
Scan tick ≤ 20 Hz, ≤ 16 k measurements per tick *provisional*.
