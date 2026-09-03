# Quest Infinite Merkaba algorithm

> Production closure contract: [`contr.md`](contr.md). It is authoritative over conflicting historical documentation.

## 1. Coordinate model

The sole geometry authority is a signed cubic lattice:

```text
support size = 0.050 m
lattice step = 0.025 m
half support = 0.025 m
world centre = globalCoord * 0.025 m
```

Chunks are dense `32 × 32 × 32` arrays allocated inside a sparse world map.
For every axis:

```text
chunk = floorDiv(global, 32)
local = floorMod(global, 32)
```

This definition is identical at positive coordinates, negative coordinates,
and chunk boundaries.

## 2. Canonical state

One kernel record is 16 bytes:

```text
signed occupancy evidence
packed RGBA8 colour
unsigned colour confidence
minimal occupancy flag
```

Coordinates imply position. Boundary masks, normals, vertices, and indices are
derived and are never persisted.

Evidence saturates to `[-32768, 32767]`. A previously empty record turns occupied
at 512. A previously occupied record turns empty at 128 or below. Surface and
free updates use quality-squared integer weights scaled by 640 and 256,
respectively. A surface quality below 0.25 is ignored. Surface RGB uses the same
quality as an integer-weighted temporal average, with confidence capped at
65535.

## 3. Depth relation

The existing Quest frontend reconstructs stereo depth and normals, performs
edge-aware filtering and dilation, and provides the camera projection and
tracking transforms. For each resident lattice centre in the current frustum:

1. Reject invalid depth, projection, exclusion, distance, or dilation inputs.
2. Compute `relation = measuredEyeDistance - kernelEyeDistance`.
3. `relation > 0.025` proves free space.
4. `abs(relation) <= 0.025` is a surface observation.
5. A negative relation beyond that band is behind the observed surface and is
   left unchanged.
6. Apply disparity, dilation/occlusion, and surface-normal checks.
7. Compute distance quality times angle quality.

Only valid free space subtracts evidence. This makes correction local and
reversible: a false foreground can cross the occupied threshold after a bad
hit, then cross the empty threshold after repeated clear observations. Unknown
space is never carved.

## 4. Canonical support and boundary

For `a = 0.025 m`, support corners are `(±a, ±a, ±a)`. The two inscribed
tetrahedra use alternating cube corners. Their frozen decomposition contains:

```text
one central octahedron
eight corner-tip tetrahedra
twelve edge-wedge tetrahedra
```

The live surface basis is 24 half-step face quadrants, four per cube face. Each
active bit expands to two fixed triangles. A predicate for a patch:

1. rejects it if any immediate occupied centre on its outward side contains
   that physical patch;
2. among coplanar occupied supports sharing it, assigns it to the
   lexicographically least signed integer centre.

Thus a physical exterior patch is emitted once and an interior patch zero times.
The 26-bit neighbourhood is only predicate input; there is no exponential lookup
table, runtime Boolean construction, welding, marching, or global rebuild.

## 5. GPU working set

The CPU owns sparse chunk existence and canonical persistence. A bounded LRU
working set maps chunks into 96 dense GPU pages. Per frame:

- up to 48 current-frustum pages receive one integration dispatch;
- occupancy transitions dirty only their local `3 × 3 × 3` region;
- up to 64 frustum-visible pages derive/compact boundary masks;
- one indirect procedural draw expands compact records to fixed vertices.

Eviction synchronizes one page asynchronously when needed. Saving/exporting
explicitly synchronize resident canonical state. Ordinary integration/rendering
does not read back the frame, scan historical chunks, or rebuild the world.

## 6. World alignment

`RoomAnchorManager` creates or localizes one Quest spatial anchor.
`RoomSpaceRoot` binds with an identity local transform under it. The grid is a
child of that room-space root, so signed lattice coordinates remain stable
across sessions without relocation or resampling.

## 7. Persistence

The versioned binary stream contains exact constants, anchor UUID/matrix,
integration count, sorted chunk coordinates, and dense 16-byte kernel records.
Never-observed chunks are omitted. Loading validates the header and constants,
localizes the saved anchor where possible, restores canonical records, recounts
occupancy, and lazily rebuilds GPU residency/topology.

## 8. GLB readout

Export walks occupied kernels in deterministic order, evaluates the same local
ownership predicates, expands only active boundary patches, mirrors Unity X,
reverses winding, and writes one GLB 2.0 mesh with:

```text
float POSITION
float NORMAL
normalized RGBA8 COLOR_0
uint indices
white base colour factor
metallic factor 0
roughness factor 0.85
```

Export is the only path that constructs conventional indexed geometry.
