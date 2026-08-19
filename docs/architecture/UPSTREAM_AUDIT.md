# Upstream and environment audit

Audit date: 2026-08-19 (Europe/Prague)

Pinned inputs:

- QuestRoomScan: `2fdaaae71f60b21b7853e67db943fc42f75d0c2f`
- DiffSoup: `c74e35de74ad0116977b23e7951f4cbc25ab0f6b`
- RoomScan-GaussianSplatServer:
  `a70ad5f8e6849e96e18fdb9e7f7276adf0c29b66`

This audit is the implementation boundary for V1. It corrects assumptions in the
initial proposal where the checked source is more restrictive than the README-level
architecture suggests.

## QuestRoomScan contract

### Build and package surface

- The repository is a Unity Package Manager package, not a complete host project.
- `package.json` requires Unity `6000.0` and pins AR Foundation 6.1.1, Burst
  1.8.18, Collections 2.4.3, Mathematics 1.3.2, URP 17.0.3, and Meta MRUK
  205.0.0.
- The package license is MIT (`LICENSE.md`), including the retained attribution
  notice for the MIT-licensed `anaglyphs/lasertag` architecture.
- A generated host project is therefore required for repeatable editor tests and
  an Android APK build. The package itself must remain usable as a UPM dependency.

### Live lifecycle extension points

- `Runtime/Core/RoomScanner.cs::Update` is the real-time orchestration point. It
  calls `ProvideColorFrame`, `VolumeIntegrator.Integrate`, and
  `MeshExtractor.Extract`, then raises `Integrated` and `MeshExtracted`.
- `RoomScanner.StartScanning`/`StopScanning` own scan session transitions.
- Optional modules implement `IRoomScanModule`; this is suitable for services and
  status/UI integration, but chunk rollover must also wrap the core integration
  lifecycle because it changes volume coordinates and ownership.
- `VolumeIntegrator` owns the TSDF/color `RenderTexture`s, integration counts,
  clear/load/readback behavior, and compute bindings.
- `MeshExtractor` delegates GPU extraction to `GPUSurfaceNets`, while
  `GPUMeshRenderer` performs one indirect triangle draw.

### Coordinate-system correction

The existing volume is centered at world origin. It is not already a relocatable
local volume:

- `VolumeHelpers.hlsl::gsVoxelToWorld` and `gsWorldToVoxelFloat` directly convert
  between voxel indices and world coordinates with no volume pose.
- `VolumeIntegration.compute` projects those generated world points into the depth
  and RGB cameras.
- `SurfaceNetsExtract.compute` writes the same positions as GPU vertex positions.
- `ScanMeshVertexColor.shader` feeds those positions directly to
  `TransformWorldToHClip`, samples volume/freeze state using them, and treats them
  as world-space normals and positions.
- `GPUMeshRenderer` supplies no local-to-world matrix and its draw bounds are world
  bounds.

Consequently, changing only the camera pose is incorrect. V1 must introduce an
explicit `worldFromChunk`/`chunkFromWorld` contract and apply it consistently to
integration, exclusions, extracted mesh rendering, volume sampling, bounds, and
atlas/keyframe projection. Chunk-local geometry remains immutable; pose-graph
updates change only `worldFromChunk`.

### Memory and rollover implications

- Default TSDF is 256 cubed RG8_SNorm (about 32 MiB) and color is 256 cubed RGBA8
  (about 64 MiB).
- `GPUSurfaceNets` also allocates large derived buffers/textures, including a
  256-cubed RGBA32Float temporal texture. The README estimate and code comments
  do not fully agree, so device profiling is the authority.
- The optional three 4096-squared triplanar textures cost about 192 MiB and should
  default off only in opt-in large-world mode.
- Sequential rollover can reuse one live TSDF buffer set. Spatial overlap is
  obtained because the first observations of the next volume cover geometry also
  present in the finalized previous volume. Holding two complete live volume sets
  is not required for V1.
- Final mesh/readback handoff must be asynchronous and bounded; inactive geometry
  is represented by compact CPU/disk mesh artifacts, not resident TSDFs.

### Persistence and keyframe risks to fix

- `RoomScanPersistence.SaveToNewPackageAsync` can rename the `_tmp` directory to
  its permanent name before GPU readback and all subsequent writes succeed. The
  world layer must instead write a sibling staging directory/file, validate it,
  flush it, then atomically promote it without replacing the last known-good
  manifest on failure.
- Legacy `scan.bin` is retained as a migration input. New readers must cap counts
  and byte lengths before allocation; a raw default chunk is roughly 96 MiB before
  ancillary data, so infinite persistence also needs compression/retention policy.
- `KeyframeCollector.SaveKeyframeData` starts a background closure that reads the
  mutable `_imagesDir` and `_manifestPath`. A chunk switch can therefore split one
  keyframe across sessions, and pending writes are not drained before finalization.
- Numeric JSON is built with culture-sensitive `ToString`, which is invalid on a
  decimal-comma locale. The fix must capture an immutable destination/session,
  write invariant JSON, use a stable per-session lock, and expose an async drain.
- `RoomAnchorManager` owns a single active spatial anchor. V1 uses one world anchor
  with chunk transforms in the pose graph; sparse regional anchors may be added
  later. It does not create one free independent anchor per chunk.

### Reusable refinement and export inputs

- `TextureRefinement` already performs GPU readback, xatlas unwrap, multiview atlas
  bake, depth occlusion, seam operations, and Sobel normal generation.
- The refined result already contains mesh positions/normals/tangents/UVs/indices,
  base-color atlas bytes, and normal-map bytes. These are the canonical V1 GLB/PBR
  inputs.
- QRS has no glTF/GLB writer. The mandatory baseline is deterministic uncompressed
  GLB 2.0 with base color + normal, `metallicFactor = 0`, and an explicitly
  configured constant roughness. Compression is capability-gated and cannot be a
  prerequisite for export.

## DiffSoup contract

### License and build

- Upstream DiffSoup is MIT licensed.
- Its Python package requires Python 3.10+ and `torch>=2.1`; the extension build
  uses scikit-build-core and nanobind. The examples require additional scientific
  and image packages listed in `requirements.txt`.
- The repository's tested environment is older than this machine's CUDA 13.3 and
  Python 3.14 stack. Install the worker in a dedicated pinned environment and run a
  compile/smoke probe before selecting a Torch/CUDA combination. Do not mutate the
  system Python installation.

### Training input reality

- Upstream does accept an existing triangle mesh in examples, but it does not ship
  a generic QuestRoomScan dataset adapter or production job worker.
- `examples/02_synthetic.py` loads a MobileNeRF OBJ and NeRF-synthetic camera JSON,
  changes coordinate conventions, optimizes for a scheduled number of steps, and
  performs multiresolution/topology changes.
- V1 therefore needs an explicit QRS adapter for mesh, RGB files, intrinsics,
  image resolution, chunk-local camera transforms, masks/depth where supported,
  and coordinate/color conventions. Inputs must be validated before CUDA work.
- Training is more than the CUDA rasterizer: Python controls autograd, optimizer
  state, multiresolution schedules, losses, and remeshing. It remains off-device.

### Canonical runtime artifact

`examples/06_export_web.py` defines the verified baseline output:

- `mesh.ply`: float32 vertices and int32 triangle indices.
- `lut0.png` and `lut1.png`: two RGBA8 images packing seven accumulated features
  plus alpha in per-triangle multiresolution texels.
- `mlp_weights.json`: row-major `W1 16x16`, `b1 16`, `W2 16x16`, `b2 16`,
  `W3 3x16`, and `b3 3`.
- `meta.json`: at minimum model subdivision/up/background metadata used by the
  viewer.

The checkpoint exporter reads `V`, `F`, `feat_acc`, `alpha_acc`, `Rmax`, and
`color_mlp`; `up` or `flip_z` may describe orientation. The Unity importer must
wrap this in our own versioned manifest with byte sizes, hashes, hard limits,
coordinate convention, and network version. It must not trust raw PLY/PNG/JSON.

The viewer interpolates the triangular per-face LUT with barycentric coordinates,
discards texels whose LUT1 alpha is below 0.5, combines seven features with
second-order view-direction spherical harmonics into a 16-value MLP input, applies
two ReLU 16-wide layers and a sigmoid RGB output, then blends a residual using the
fourth LUT0 channel. Unity can render this with ordinary triangle depth testing,
but stereo correctness and fragment cost must be measured on Quest 3S.

### Revision/warm-start correction

The exported final checkpoint is not sufficient for exact optimizer resume. A true
warm start needs raw trainable multiresolution feature/alpha parameters, optimizer
states, step/schedule state, topology identity, camera convention, and versioned
code metadata. V1 worker will emit an extended resumable checkpoint only for an
exactly compatible prior revision; otherwise it deliberately starts a fresh job.

## Existing Gaussian server assessment

- The README advertises MIT, but the pinned repository has no root `LICENSE.md`;
  its README link to `../LICENSE.md` does not resolve inside that repository.
- Its useful public behavior is upload/status/cancel/download over LAN, but the
  implementation owns one global `TrainingManager`, reads an entire request body
  before enforcing its 2 GiB limit, and is not keyed by world/chunk/revision.
- V1 will not copy this source. It will implement a clean, independently licensed
  server in this repository with versioned idempotent jobs, bounded streaming
  uploads, safe extraction, hashes, durable state, restart recovery, and a fake
  backend usable without CUDA. The legacy lifecycle informs compatibility only.

## Verified machine baseline

- Ubuntu 26.04 LTS; NVIDIA RTX 4070 Laptop GPU; driver 610.43.02; CUDA runtime and
  toolkit 13.3; `nvcc` 13.3.73.
- Existing tools include Git/Git LFS, ADB, CMake, Ninja, Clang/GCC, Python, uv,
  Rust, Java, Node, and npm. They must not be duplicated.
- Authorized ADB target: Quest 3S, Android 14. Runtime depth delivery—not the model
  label—is the device acceptance criterion.
- Unity Hub 3.19.5 was installed from the user's downloaded official Debian
  package. Unity 6000.0.81f1 plus Hub-managed Android Build Support, SDK/NDK, and
  OpenJDK is the pinned editor target; editor installation is tracked separately
  from this completed source audit.
- Development/Unity storage is allocated only in the dedicated KINGSTON ext4
  image. No Codex state under `~/.codex` is eligible for deletion, movement,
  compression, or cleanup.

## Frozen V1 architecture

1. Keep QRS TSDF, Surface Nets, and atlas compute paths in Unity/Vulkan.
2. Add explicit movable chunk coordinate transforms and reuse one live TSDF set.
3. Persist versioned chunks atomically and keep only a bounded nearby mesh set
   resident; network failure never blocks scanning.
4. Use a world pose graph for overlap/loop corrections; keep local geometry fixed.
5. Train DiffSoup asynchronously per inactive chunk in a pinned Python/CUDA worker.
6. Validate and atomically promote a compact DiffSoup artifact to a Unity URP
   triangle/LUT renderer.
7. Export deterministic chunk GLB, sharded world GLBs, and a bounded optional
   monolithic GLB with honest PBR defaults.

