# Cone-PRISM Q3

Pure-Quest continuous surface reconstruction for Meta Quest 3/3S. The app consumes
the calibrated moving rig as four timestamped observation fields:

```text
RGB-L + RGB-R + DEPTH-L + DEPTH-R + per-camera poses
```

The canonical world is not a voxel volume, point cloud, triangle soup or Gaussian
cloud. It is a persistent atlas of one-sided probabilistic `ContactFilm` charts whose
measured support is connected by oriented half-edges, shared 3D boundaries and ordered
manifold frontier loops.

The reconstruction path is entirely on-device. It has no server, CUDA, TSDF,
Surface Nets, 3GS training, COLMAP or CPU pixel loop.

## Reconstruction physics

Every pixel represents a finite calibrated cone, not an infinitesimal point:

```text
camera  ---- observed FREE ----> first CONTACT | UNKNOWN behind contact
```

- only the segment before the first hit is proven free;
- the first hit deposits or tightens a one-sided ContactFilm;
- everything behind it remains unknown and cannot be carved;
- incompatible evidence creates another hypothesis, boundary or split, never an
  average;
- close, frontal and well-calibrated observations carry higher information;
- accumulated posterior information is geometric resistance, so a weak distant or
  grazing observation cannot overwrite a strong close scan;
- RGB-L/R and temporal views refine an existing surface only along its supported
  normal uncertainty interval.

The binding mathematical and product specification is [specka.md](specka.md),
identifier `CPQ3-2026-08-21-v6`.

## Canonical architecture

```text
Quest four-stream rig
        ↓
finite ConeEvents + immutable calibration LUTs
        ↓
dual-eye prediction raster (hardware first-hit association)
        ↓
Contact posterior
  quadratic SurfaceChartGeometry + displacement + H/Σ
        ↓
PressureManifold atlas
  support contours + half-edges + shared boundaries + FrontierLoops
        ↓
derived GPU materialization
  adaptive measured meshlets + indirect culling/rasterization
        ↓
persistent chunk storage / revisit / GLB-PBR pipeline
```

A chart rectangle is only a numerical coordinate domain. It has no topological
meaning. Physical topology comes from measured support contours. Chunk ownership is
only storage locality and never creates a new manifold, optical seed or physical seam.

## Current implementation milestone

Q3-15.6 rebases the geometry core from rectangular patch bookkeeping to the atlas
above. Implemented production foundations include:

- coherent GPU-only four-stream capture and immutable rig/cone LUTs;
- L/R metric consensus, uncertainty, adaptive normals and boundary evidence;
- hardware prediction raster and exhaustive first-hit cone classification;
- deterministic cross-tile/cross-eye component convergence;
- global representative frames and posterior refit in one coordinate system;
- support-contour extraction, arbitrary half-edges and ordered frontier loops;
- evidence-bearing continuation, shared boundary topology and cached intersections;
- evidence-aligned canonical split and local elastic-island solve;
- stable global manifold identity with cross-chunk ghost portals;
- measured-only transactional meshlet materialization, culling and indirect draw;
- schema-v6 canonical persistence and resume;
- GPU information-gain keyframe ingress.

Surface-space measured texture superresolution, directional appearance/PBR and final
GLB integration remain later DAG stages; they must build on this canonical geometry
and must not revive an older mapper.

## Repository map

```text
Runtime/Prism/Capture/       four-stream acquisition, pairing, leases, calibration
Runtime/Prism/Preprocessing/ metric depth, consensus, normal and boundary products
Runtime/Prism/Association/   prediction and finite-cone classification
Runtime/Prism/Geometry/      ContactFilm posterior, atlas, boundaries and meshlets
Runtime/Prism/Refinement/    photometric focus and information-gain keyframes
Runtime/Resources/Prism/     Vulkan compute/raster workgraph
Runtime/World/               chunk locality, schema-v6 persistence and pose graph
Runtime/Export/              deterministic glTF/GLB writers
Editor/                      clean PRISM scene/build setup
Tests/Editor/                focused ABI, persistence and algorithm contracts
Tools/unity/                 Vulkan tests, build, deploy and profiling helpers
```

The generated file/function map is [docs/architecture/CODE_GRAPH.md](docs/architecture/CODE_GRAPH.md)
and its machine-readable companion is [.codex/CODE_GRAPH.json](.codex/CODE_GRAPH.json).

## Build and verification

The package targets Unity 6, URP, OpenXR/Meta XR and Android Vulkan.

```bash
Tools/unity/run_editmode_tests.sh
Tools/unity/validate_prism_compute_uav.py
Tools/unity/build_smoke_apk.sh
Tools/unity/deploy_smoke_apk.sh
```

Device work is intentionally batched after a complete vertical milestone. Static
contracts do not substitute for the physical thin-plate, boundary, revisit and
cross-chunk corpus defined in `specka.md`.

## Source archives

Release/checkpoint source bundles are created with `git archive`; generated APKs,
device captures, logs, credentials and local forensic data are not committed.

## License

MIT. Upstream copyrights remain intact.
