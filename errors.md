# QuestInfiniteScan verified issue ledger

This file records evidence-backed gaps so later closure runs do not rediscover
them from memory. `OPEN` entries are not authorization to cross the active
scope boundary.

## Current evidence baseline

- Code base: `3f437d59625c439f56a6da49dcddc6620c23e8dc` before the active RGB-D closure.
- Device log: `/mnt/kingston-unity/Builds/DeviceEvidence/true_stereo_projection_fix_20260830_020425/live-logcat.log`.
- 26 timestamp samples: `StereoRgbdRefine` 1.416 ms median / 2.663 ms max;
  nine `DilateDepthStep` invocations about 1.2 ms normally and 2.7 ms max;
  `DepthNorm` about 0.113 ms; `InitDepthDilation` about 0.118 ms.
- Pairing totals in that run: 775 accepted and 899 expired depth frames
  (46.3% accepted). Observation submit median was 161 ms (6.21 Hz), despite
  the 15 Hz admission ceiling.
- No physical-tile starvation, hash-full, primitive overflow, KGSL/UCHE or
  FenceChecker fault was present in this run.

## RGB-D snapshot closure

### Verified Quest sensor/API contract

- Installed MRUK `PassthroughCameraAccess` explicitly supports two concurrent
  instances selected as physical Left and Right cameras. `Timestamp` belongs
  to the latest image and `GetCameraPose()` is its Unity world-space pose.
- MRUK updates the native PCA texture on the render thread and explicitly
  warns that a blocking `Graphics.Blit()` observes the preceding image. The
  scanner therefore queues a graphics-command copy and publishes metadata and
  pixels together; it never retains the producer-owned texture in an
  observation.
- Installed AR Foundation documents Environment Depth timestamps in
  nanoseconds and `TryGetPoses()` results in Unity world space. The scanner
  consequently applies no extra tracking-space transform to either depth or
  PCA poses.
- PCA FOV is not assumed to equal Environment Depth FOV. Every depth
  hypothesis is projected independently through both PCA intrinsics, crop,
  resolution and timestamped world poses. A hypothesis outside either
  calibrated PCA image is invalid.

### RGBD-1 — latest-only PCA association [FIXED, DEVICE PENDING]

`PassthroughCameraProvider` retained only the newest PCA descriptor per eye.
When L and R advanced on different Unity frames, a temporally closer prior
image was unrecoverable. This explains the measured high expiry rate but does
not by itself prove the centre-of-view symptom.

Required invariant: retain only the previous/current owned image for each
physical PCA eye and choose the L/R pair with the smallest joint spread around
the owned depth timestamp. No producer-owned native image may survive as an
observation texture.

### RGBD-2 — distance-dependent stereo acceptance [FIXED, DEVICE PENDING]

`StereoRgbdRefine.compute` accepted opposite-eye residuals using
`max(25 mm, 1.25% of distance)`: 25 mm at 2 m, 50 mm at 4 m and 100 mm at
8 m. This directly violated the half-lattice precision target.

Required invariant: every emitted depth hypothesis is within 12.5 mm of its
source measurement and has opposite-depth support within 12.5 mm. PCA-L and
PCA-R calibrated coverage and patch correspondence are mandatory.

This is a strict scanner-added error budget, not a claim that changing the GPU
texture format improves the Environment Depth sensor. Correlated sensor error
or a locally textureless stereo patch remains physically unobservable from
photometry alone; the next Quest evidence run must measure the accepted output
instead of inferring ground-truth accuracy from format precision.

### RGBD-3 — refined NDC stored as R16_UNorm [FIXED, DEVICE PENDING]

The selected projection depth was quantized into R16. The metric error grows
non-linearly with range and consumed most of a 12.5 mm budget near the far end
of the scan. R32 does not improve the sensor; it only prevents the scanner
from adding this avoidable error after four-stream validation.

### RGBD-4 — external-depth copy had no real GPU timestamp [FIXED, DEVICE PENDING]

`CopyProjectionDepthArray` used direct `ComputeShader.Dispatch`, outside the
profiled command-buffer path. Its cost was absent from device timing logs.
It must use the existing Vulkan timestamp path; no new compute kernel is
needed.

### RGBD-5 — centre scans less than the periphery [UNRESOLVED]

The visual symptom is real, but the captured logs contain no radial reject
histogram, so its cause is not proven. Re-test after RGBD-1/2 because the old
latest-only pairing and loose depth test confound the result. If it persists,
the next evidence run must count raw-valid, four-camera-coverage, stereo-depth,
photometric and accepted pixels in centre/mid/edge regions without per-frame
readback. Do not tune thresholds blindly.

## Canonical integration gaps (outside the RGB-D snapshot scope)

### M8-1 — one refined hit deliberately admits multiple lattice layers [OPEN]

`DiscoverSurfaceCandidates` emits `surface - 25 mm`, `surface`,
`surface + 25 mm`, then `nearest - dominantAxis`, `nearest`, and
`nearest + dominantAxis`. `ObserveDepthEye` classifies the full +/-25 mm band
as SURFACE. Dedup removes identical coordinates, not distinct layers.

Therefore even a perfect 12.5 mm RGB-D snapshot cannot by itself guarantee
one canonical layer. This is the verified source-level reason that a single
view may still create up to three adjacent occupied lattice cells. Fixing it
requires a separate integration/refinement contract; do not hide it in the
sensor pipeline.

### M8-2 — occupancy may be committed without canonical RGB [OPEN]

`IntegrateSurfaceCandidates` calls `UpdateOccupancy` before it knows whether
the lattice kernel centre projects into either PCA image. If `rgbWeight == 0`,
the occupied state remains with `ColorConfidence == 0`, which presentation
shows as magenta.

The four-stream source hit can be colored while an offset support-band kernel
centre is outside PCA coverage. This is not evidence that a previously colored
kernel was erased. It is a boundary between validated source photometry and
canonical support-band admission.

### M8-3 — color is resampled at the lattice centre, not carried from the
validated RGB-D hit [OPEN]

The refine kernel validates PCA color at the reconstructed depth hit, but the
integration kernel later projects and samples the rounded/offset canonical
kernel centre again. The two positions can differ by 25 mm. This allows color
coverage and edge correspondence proven for the hit to be lost at integration.
Resolve together with M8-1/M8-2, without adding a CPU color mirror.

## Performance facts and remaining measurement gaps

### PERF-1 — depth preprocessing is not the dominant measured GPU load [FACT]

In the baseline run, stereo refinement plus dilation/normals was normally
about 2.8 ms per accepted observation and ran around 6.2 Hz. Increasing sample
work still needs Quest measurement, but this stage was not the 72 Hz raster
bottleneck.

### PERF-2 — PCA owned-history copies need device timing evidence [INSTRUMENTED]

The Meta PCA image is updated by a render-thread plugin event. A non-blocking
graphics-queue copy is required to own previous/current frames. It is a blit,
not a scanner compute kernel. The active closure names both history and final
observation copies and brackets them with Vulkan TRANSFER-stage timestamps.
Measure their actual Quest cost; do not replace them with a blocking readback
or assume they are free.

### PERF-3 — half-precision dilation is intentionally unchanged [FACT]

The dilation field is a derived occlusion/quality accelerator with a 0.5 m
disparity allowance; it is not the selected surface position. Promoting both
dilation ping-pong textures to RGBA32 would multiply nine-pass bandwidth
without fixing source depth error. Revisit only with device evidence.
