# Directional geometry decision

Status: post-checkpoint design boundary; not part of the current release DAG.

## Problem statement

The current mapper stores one signed-distance/confidence pair per voxel. B06 can
reject a lower-quality or opposite-facing observation so it does not destroy a
stable surface, but rejection cannot store the second surface. This is a fundamental
representational limit for thin structures viewed from multiple sides: partitions,
thin columns, pipes, rails, monitor panels, door leaves, furniture edges, and similar
geometry all belong to the same problem class.

[Directional TSDF (IROS 2019)](https://arxiv.org/abs/1908.05146) is directly relevant.
It separates surface hypotheses by orientation and uses a modified Marching Cubes
stage that can retain opposite zero crossings. The later
[rendering/tracking paper](https://arxiv.org/abs/2108.08115) describes sparse 8³ blocks
keyed by position and direction and reports roughly 1.5–2× regular-TSDF memory for
most evaluated scenes, rather than six dense scene volumes.

## Corrections to the proposed rewrite

1. DTSDF does not remove the voxel-resolution limit. The original paper explicitly
   states that regular TSDF cannot represent geometry thinner than one voxel; DTSDF
   prevents contradictory orientations from corrupting each other and improves
   topology/coherence, but a 20 mm object cannot be claimed metrically resolved by
   our 50 mm grid. Local fine resolution is a separate feature.
2. The public reference repository is not MIT/permissive. It is derived from
   InfiniTAM v3 and its checked-in license restricts use to non-commercial purposes
   and imposes conditions on reproduction, modification, and transfer. This project
   is explicitly non-commercial, so its CUDA/C++ implementation may be mechanically
   ported while retaining the complete original copyright, source, attribution,
   non-commercial restriction, and other license conditions. The port must be clearly
   distinguished from the fork's MIT-authored code.
3. This is not only a CUDA-kernel substitution. It changes voxel allocation,
   direction classification, conflict-free fusion, meshing/filtering/regularization,
   persistence, snapshots, reload, GPU bounds, renderer inputs, GLB topology, and the
   device acceptance corpus. The paper also calls out thread-safety challenges for
   ray-based fusion and memory-bound multi-direction lookup.
4. Sparse bricks and infinite submaps solve different problems. Our moving submaps
   already make world-size TSDF residency O(1). Sparse bricks could buy finer local
   resolution and allocate directional surfaces economically, but are not required
   to finish the large-world lifecycle.
5. Adaptive scalar→directional and coarse→fine promotion adds migration, seam, and
   extraction complexity. It should not be the first directional implementation.

## Synergistic path

### 1. Finish and stabilize the current checkpoint

Fix the known chunk `Finalizing`/revisit/cache lifecycle defect and complete the
physical B06/D03/F02 acceptance. Geometry experiments must not obscure a broken world
state machine.

### 2. Add measurement-confidence improvements to the scalar baseline

These changes benefit the replacement geometry core and address thin columns as well
as walls:

- make `DepthNormals.compute` bounds-safe and emit a confidence value based on local
  depth discontinuity and finite-difference consistency;
- downweight/reject fusion and especially negative/free-space support near uncertain
  silhouettes instead of allowing jump-flood dilation to bridge foreground and
  background;
- retain the existing RGB-guided bilateral filter only as an edge-preserving cue;
  RGB may reduce destructive smoothing but must not fabricate metric geometry;
- add bounded GPU counters or a coarse conflict heatmap for opposite-orientation,
  occluded, grazing, and residual rejection. Real captures can then show where a
  second surface hypothesis would be useful.

The current code already computes depth normals, uses RGB/depth bilateral weights,
checks dilated-depth visibility, and derives stable-surface orientation. The missing
piece is an explicit trustworthy edge/normal confidence carried into arbitration.

### 3. Establish a temporary migration boundary and captured corpus

Only after baseline stability, define an `IGeometryVolumeBackend` contract covering
integration, extraction, snapshot version, restore, memory accounting, and local
mesh output. This boundary exists to migrate and A/B-test safely; it is not the final
product architecture. Add a bounded diagnostic corpus with thin walls, square/round
columns, pipes, rails, door edges, corners, oblique planes, and foreground
silhouettes.

Evaluation must compare topology retention, completeness, metric error, frame cost,
peak resident memory, reload determinism, and GLB output against the same recorded
poses/depth—not visual anecdotes from different walks.

### 4. Port and promote one sparse directional replacement

The replacement should use a single sparse 8³ block pool keyed by
`(blockX, blockY, blockZ, direction)` and direction-aware meshing. Do not begin with
six dense 256³ volumes, scalar/directional promotion, local multiresolution, or a
speculative `K=2` voxel. Quest tracking already supplies poses, so upstream DTSDF ICP
and raycast tracking are not required for this port.

Surface Nets may exist temporarily for A/B validation, but it cannot be the
authoritative extractor for multiple directional zero crossings. Directional
extraction is part of replacement correctness, not optional polish. After parity and
snapshot migration pass, DTSDF becomes the main mapper path and the scalar backend is
removed from the production configuration.

### 5. Add adaptive fine bricks only after directional parity

If captures prove that topology is retained but the 50 mm grid misses required
diameters/details, add one finer level around measured complex/conflict regions. Fine
allocation must be driven by repeatable depth evidence and bounded budgets, not RGB
edges alone. Cross-level seams, persistence, and pose-graph-local transforms require
their own acceptance tests.

## Decision

Do not replace the current mapper or add a DTSDF implementation node to the active
DAG before the feature checkpoint and lifecycle bugfix are complete. Adopt depth-edge
confidence plus conflict telemetry as the first compatible geometry improvement.
Then mechanically port the relevant directional allocation/fusion/extraction code
under its retained non-commercial license and use the migration boundary only until
the sparse DTSDF replaces scalar TSDF + scalar Surface Nets in the main mapper path.
Reject six dense volumes, RGB-generated geometry, and an unvalidated adaptive/K=2
design.
