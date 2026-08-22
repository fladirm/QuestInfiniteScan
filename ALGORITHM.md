# Cone-PRISM Q3 algorithm

This document describes the production reconstruction workgraph. `specka.md` is the
binding specification if a detail conflicts.

## 1. Observation model

For calibrated pixel `i`:

```text
Cᵢ = (Oᵢ, Ωᵢ, dᵢ, Iᵢ, Tᵢ, Σᵢ)
```

`Ωᵢ` is the finite pixel footprint cone. A valid depth observation establishes:

```text
0 < s < dᵢ       observed free segment
s ≈ dᵢ           first-contact manifold
s > dᵢ           unknown
```

No pass may infer empty space behind the measured first hit.

## 2. Surface posterior

A one-sided ContactFilm evaluates a local chart:

```text
X(u,v) = P + u Tu + v Tv + h(u,v) N
h(u,v) = a + bu + cv + du² + euv + fv² + D(u,v)
```

The six analytic parameters use information sufficient statistics:

```text
H ← H + w JᵀJ
g ← g + w Jᵀe
H Δθ = g
```

`D` is hierarchical measured displacement. Posterior covariance, independent view
support, best footprint and calibration/pose quality are canonical state. The weight
is absolute physical precision with a robust innovation cap, not a frame vote.

Weak evidence can add visibility/support information without moving a stronger
posterior. This is the numerical form of pressure resistance: close observations
usually press harder; an already compressed film resists distant/grazing overwrite.

## 3. Three separate layers

### Contact posterior

- analytic chart, displacement and uncertainty;
- rectangular extent only bounds the numerical parameterization;
- measured coverage/information field;
- one-sided competing hypotheses.

### PressureManifold atlas

- support-contour pages and arbitrary contour segments;
- oriented generation-safe surface half-edges;
- smooth, crease and occlusion continuation evidence;
- one shared world-space BoundaryCurve for both incident sides;
- ordered component-level FrontierLoops;
- global manifold identity independent of storage chunks.

### Derived materialization

- adaptive measured meshlets;
- prediction MRTs and Hi-Z;
- view/LOD compaction and indirect draw;
- later surface appearance and export.

Derived data may be discarded and rebuilt without changing the canonical posterior
or topology.

## 4. Frame workgraph

```text
Acquire RGB-L/R + DEPTH-L/R + poses
  → GPU ring copy and coherent timestamp pairing
  → depth normalization through immutable cone LUT
  → L/R consensus, covariance, adaptive normals, boundary evidence
  → render published measured meshlets from both depth eyes
  → classify every valid cone event
  → refine compatible ContactFilms
  → emit and reduce new-contact candidates
  → update support contours / half-edges / boundaries
  → solve dirty elastic islands and evidence-aligned splits
  → run scheduled stereo/temporal normal-direction focus
  → rebuild only dirty meshlets
  → evaluate keyframe information gain
```

There is no per-frame CPU pixel or geometry traversal and no synchronous GPU readback.

## 5. Capture and calibration

`PrismRigCapture` captures both Meta passthrough cameras and both slices of the stereo
environment-depth texture into generation-safe GPU rings. Timestamp queues publish a
frame only when eye identity, poses, calibration epoch and bounded timing agree.

`RigCalibration` freezes intrinsics/extrinsics for the calibration epoch. `ConeLut`
precomputes center rays and ray differentials. Per-pixel compute never reinverts camera
intrinsics.

## 6. Depth preprocessing

`DepthNormalize.compute` converts projection depth to one metric convention while
preserving independent eyes.

`DepthConsensusNormalBoundary.compute`:

- cross-projects L/R first hits and keeps agreement/disagreement explicit;
- separates depth sensor variance from finite-footprint spatial bandwidth;
- estimates boundary-safe normals at the smallest stable support scale;
- emits depth/normal/RGB/visibility boundary evidence;
- learns guarded residual statistics from mature surfaces.

## 7. Hardware association and classification

Published measured meshlets render MRTs for each depth eye:

```text
PredDepth, PredNormal, FilmID, UV, Sigma, Generation, Sidedness, Confidence
```

The Z-buffer performs first-hit visibility and association. Latent topology always has
`FilmID=0` and cannot enter prediction.

Each measured event ends in exactly one class:

```text
MATCH | NEW_FRONT | BEHIND | NEW_LAYER | UNSEEN | BOUNDARY | INVALID
```

`BEHIND` accumulates contradiction only within the observed pre-hit segment; it does
not delete geometry after the measured hit. Incompatibility creates a new layer,
BoundaryCurve or split.

## 8. New-contact component reduction

8×8 groups are computation blocks only. They emit provisional candidates; they never
define canonical tiles or frontiers.

```text
local candidates
  → spatial broad phase
  → deterministic hook/shortcut to convergence
  → compact connected components
  → weighted global frame and support centroid
  → orthonormal (N,Tu,Tv)
  → refit all original cone samples in that frame
  → aggregate robust posterior solve
  → component representability test
  → one or several linked ContactFilms
```

No numerically lowest DSU root donates its frame or posterior. Transitive pairwise
compatibility cannot force a component into one chart if the global model fails.

## 9. Support contours and topology

`SupportContourExtract.compute` derives deterministic marching-squares contours from
measured support/information. Ambiguous cells use a consistent scalar-field decision.
Persistent boundary evidence clips/refines the contour.

`ManifoldHalfEdgeUpdate.compute` materializes oriented arcs, hashes endpoints, and
twins only continuations supported by explicit `ContinuationEvidence`. Internal arcs
cancel; remaining arcs are ordered into manifold-level `FrontierLoop` records.

Chart rectangle edges have no physical meaning. A circular or diagonal support region
therefore retains its actual contour.

Unknown closure is topological, not measured Euclidean geometry. A debug latent sheet
may be derived from a FrontierLoop and optical seed, but has zero FilmID and never feeds
prediction or opaque export.

## 10. Boundaries and adaptive topology

A shared BoundaryCurve owns one 3D posterior, covariance, left/right half-edge
incidences, sidedness and first-hit/view evidence. Dirty updates precompute chart-cell
intersection caches; mesh materialization does not repeatedly search a spline.

Canonical split follows evidence:

- persistent curve: split along that curve;
- bimodal residual: split by the supported mode separator;
- non-single-valued or sidedness conflict: independent hypotheses.

The current production splitter commits an evidence-aligned two-region transaction;
the ABI permits later measured 3/4-way partitions. Quadtree pages remain valid only
for displacement/texture residency, not canonical surface topology.

## 11. Elastic pressure coupling

Half-edge adjacency defines bounded local compatibility energies:

```text
smooth: position + normal continuity
crease: position continuity + hinge freedom
occlusion/discontinuity: no smoothing across the boundary
```

Only a compact dirty connected island is solved on GPU. This couples neighbouring
posterior charts into an elastic measured sheet without reprocessing the world or
inventing geometry in unknown space.

## 12. Photometric refinement and keyframes

Once a chart exists, photometric refinement searches only:

```text
X' = X + δN,   |δ| bounded by posterior uncertainty
```

Current RGB-L/R and selected historical views evaluate a narrow 1D focus cost. A
result is accepted only with sufficient gradient, uniqueness, visibility and pose /
calibration quality.

`InformationGainKeyframes.compute` scores new surface/side, expected posterior gain,
footprint, angle/baseline diversity, boundary value, unresolved regions, sharpness and
exposure. Motion/time is only a starvation fallback.

## 13. Chunking and persistence

Chunks own storage/local coordinates only. Global manifold IDs and half-edge portals
survive chunk boundaries. Eviction replaces remote endpoints with generation-safe
ghost metadata; it never converts them to a physical frontier or creates a new optical
seed.

Schema v6 persists:

- chart headers, posterior information and allocator generations;
- manifold headers/membership;
- support contours, half-edges, FrontierLoops and continuation evidence;
- shared boundary topology;
- displacement hierarchy and elastic state;
- cross-chunk portals;
- derived meshlet cache and observation/keyframe references.

Versions encoding four rectangle edges as topology are intentionally rejected rather
than migrated by fabricating unmeasured contours.

## 14. Meshlet publication and rendering

Meshlet build is transactional:

```text
dirty compact list → count → prefix ranges → capacity validate → write → publish
```

Overflow keeps the last valid generation. Graphics fences protect every buffer/texture
generation still readable by Adreno. View culling, Hi-Z, geometry LOD and appearance
mip selection generate indirect draw lists on GPU.

Only measured support enters normal preview, collision, prediction and export. The
renderer uses ordinary opaque hardware triangles; no alpha cloud or Gaussian sort is
required.

## 15. Remaining appearance path

Later DAG stages add, without changing geometry ontology:

```text
TextureAccumulate.compute       calibrated surface-space EWA deposition
virtual measured texture pages footprint-driven resolution
DirectionalAppearance.compute  stable A(u,v) + compact V(u,v,ω)
PBR derivation                  confidence-bearing GLB approximation
```

Lower-information frames may never overwrite sharper measured detail.

## 16. Hard invariants

1. Unknown behind the first hit is never carved.
2. Incompatible evidence is never averaged.
3. One-sided surfaces may coexist at arbitrarily small represented separation.
4. Chart rectangle boundaries are never topology.
5. Latent topology has zero first-hit identity.
6. A weak observation cannot degrade a stronger posterior.
7. Chunks never change manifold physics.
8. Canonical mutations publish atomically; overflow keeps the last good generation.
9. Live geometry/topology work remains GPU-driven and indirect.
10. Revisit continues the same posterior solve rather than starting another scan.
