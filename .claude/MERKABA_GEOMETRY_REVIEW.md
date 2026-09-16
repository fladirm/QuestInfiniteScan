# Standing review — membrane stitching and the loop-network proposal (Claude)

Rewritten 2026-09-05 after finding the two defects below. The earlier revision of this
file argued against the loop proposal on information-content grounds. That framing was
wrong: it treated the loops as a re-encoding of the stored plane, when the point of the
proposal is where the degrees of freedom live, not what the curve is.

## FINDING A — the Merkaba decomposition is dead in production

`MerkabaCanonicalGeometry` (8 body-diagonal directions, octahedron faces + tip sides,
24 active primitives) is referenced **only by `Tests/Editor/MerkabaGeometryTests.cs`**.
`MerkabaIntegration.compute` `#include`s `MerkabaCanonicalGeometry.generated.hlsl` and
uses no symbol from it. No production shader references `kMerkabaBodyDiagonalOffsets`,
`MERKABA_DIRECTION_COUNT`, `OctahedronFace` or `TipSide`.

The live geometry is `MerkabaOverlapShell`: `DominantAxis(normal)` picks one of three
lattice axes, the two tangent axes are lattice axes, and the patch is an
**axis-aligned 25 mm square with four corner heights**. That is a per-axis height field,
not a Merkaba tetrahedral support. The name in the live path is vestigial.

## FINDING B — stitching is two hard quantizations of a noisy normal [CLOSED 2026-09-17: residual-based admission, see RISK-2]

A contributor is admitted to a corner only if BOTH hold as exact equalities:

```text
M8MembraneDominantAxis(candidateNormal) == dominantAxis          // 3 cells, 45 deg boundaries
M8MembraneCanonicalSheet(MerkabaNearestGridNormalStep(n)) == mainSheet  // 26 cells, 22.5 deg boundaries
```

`MerkabaNearestGridNormalStep` quantizes the measured normal to one of the 26 lattice
directions (6 axis + 12 face-diagonal + 8 body-diagonal). The axis cell for +X reaches
22.5 deg in-plane before the face-diagonal cell takes over.

Consequence: two adjacent kernels 25 mm apart on the same physical wall, whose noisy
normals straddle a cell boundary, are classified as different sheets. The contributor is
rejected; if every column of a corner rejects, `accepted == 0` and `TryBuildPatch`
returns false, so **the patch is not emitted at all**. A wall whose orientation sits near
a 22.5 deg or 45 deg boundary therefore tears and holes stochastically, and the effect
**grows with measurement noise**. On a sensor with decimetre-class outliers this is a
noise amplifier, not a robustness mechanism.

Add the MAIN-dependent tie-break already recorded as RISK-2 and the stitching layer is a
predicate cascade with three independent ways to disagree about the same physical corner.

## What that means for the loop-network proposal

Read as "cycloids are extra information", the proposal is empty — the curves are
determined and free, as the author says. Read correctly, it is: **replace the discrete
compatibility predicates with shared degrees of freedom.** Knots belong to several
overlapping kernels by construction, so continuity is structural instead of tested, and
disagreement is a continuous residual instead of a class mismatch. That is precisely the
right medicine for FINDING B, and it is the reason the author's "sensor precision is
irrelevant" claim holds: a coupled network with shared unknowns denoises; a quantized
predicate cascade amplifies.

The cost objection in the earlier revision was also wrong-signed. Today one patch costs
4 corners x 4 columns x 3 normal offsets = **48 candidate evaluations**, each doing an
oct-decode plus two more `normalize` calls (`DominantAxis`, `NearestGridNormalStep`) and
about four groupshared loads. Per 8^3 tile that is 512 x 48 = ~24,576 candidate decodes
for ~1,000 distinct kernels including halo — every kernel is re-decoded on the order of
20 times. A shared-knot solve computes each shared quantity **once** and reuses it. Even
without changing the algorithm, hoisting per-kernel derived state (unit normal, dominant
axis, sheet, free signature) into groupshared once per tile is roughly a 20x reduction of
the classify stage. The proposal's direction removes work here; it does not add it.

## Where the limit actually is

Coupling reduces error on parameters the data over-determines. It cannot recover relief
the sensor never sampled. So:

```text
L0 (25 mm)        canonical owners, as today
L1-L2 (12.5, 6.25) legitimate as denoising / refinement of shared knots
L3-L5 (3.1-0.78)   no measured geometric content; procedural appearance/material only
```

That distinction must be explicit in any spec, because it decides whether a level is
scanned or invented.

## What a spec has to pin down before code

The knot graph, not the curve, is what gets implemented and tested. Required:

1. the canonical knot address set per kernel and which kernels share each knot;
2. the map from committed neighbour state to knot position (closed form, CPU/GPU exact);
3. the residual and the accept/reject threshold that replaces the two quantizations;
4. the refinement rule from level l to l+1 and its termination condition;
5. whether knot state is derived per build or persisted — if persisted, that is a second
   store and needs an explicit decision, not an omission;
6. the transverse coverage kernel and its partition of unity, per C7, before GPU work.

## Standing instruction

FINDING A and FINDING B are defects in the current tree, independent of any replacement.
Fix them, or specify the replacement in terms of items 1-6 above. Do not open a geometry
authority change without at least items 1-3 written down.

## Measured device evidence the proposal has to beat

Quest 3S, `Builds/DeviceEvidence/c113ab7_live_20260830_1332/kernel-timings-complete.txt`,
253 samples, mean per invocation:

```text
MerkabaGrid.DrawProceduralIndirect          5.115 ms   (max 10.83)
MerkabaReadout.CompileReadoutVertices       3.664 ms   (max 10.57)
MerkabaReadout.QueryM8Readout               3.309 ms   (max  7.23)
MerkabaIntegration.IntegrateCarveTiles      2.180 ms
StereoRgbdRefine                            1.562 ms
DepthDilation.DilateDepthStep  9x            0.751 ms total
MerkabaIntegration.IntegrateSurfaceCandidates 0.025 ms
```

Native job lifetimes on the shared serial Vulkan queue,
`Builds/QuestMerkabaScan/evidence/device-20260902-124133.log`, 561 observation and 507
readout jobs, zero errors:

```text
observation   queueFence median 43.4 ms (max  74.1)   lifetime median  83.7 ms
readout       queueFence median 53.9 ms (max 145.6)   lifetime median  99.4 ms
```

Read those two blocks together. The membrane *math* is ~7 ms of GPU work; the readout
*job* takes ~99 ms wall clock, of which ~54 ms is waiting for the queue. The measured
bottleneck is queue serialization plus the 5.1 ms draw — not the cost of deciding what
the surface is. A representation that multiplies emitted primitives by orders of
magnitude attacks nothing that is measured and inflates the single largest kernel.

Capacity for scale: `MERKABA_M8_READOUT_TRIANGLES_PER_BUFFER = 2,097,152`, two buffers,
4,194,304 logical triangles, 4 vertices + 6 indices per patch. One measured occupied
owner costs 2 triangles. The whole capacity is ~2.1 M owners, i.e. ~2.1 M x 25 mm
patches. A single dyadic level of the proposed weave multiplies element count by 4 per
tangent dimension pair; level 5 is 1024x. There is no headroom for that and no device
evidence that it is affordable.

Also note: the newest device evidence on disk is 2026-09-02, i.e. **before** the membrane
cut `2e5c071`. The current readout has no device measurement at all. Any performance
claim about it — improvement or regression — is unfounded until a Quest run exists.

## What to keep from the proposal

- **Overdetermined consistency as the acceptance test.** A candidate should have to be
  simultaneously consistent with many independent shared entities, not pass a chain of
  discrete predicates. This belongs in the integrator as ghost / shadow-plate rejection
  on plane residuals, refining the S2/S3 authority classes. It needs no new curve.
- **Symmetric shared-entity solve.** "The shared thing must be computed identically from
  both sides" is exactly contract C5, and it is currently violated — see
  `MERKABA_RISKS.md` RISK-2. This is the cheap, high-value part of the whole idea.
- **Dyadic, lattice-anchored detail with automatic phase lock.** The observation that a
  25 mm neighbour shift is an integer number of child periods at every dyadic level, so
  no UV atlas and no seam solver is needed, is correct and valuable. Use it later as a
  subdivision/wavelet *appearance* residual over the existing 25 mm patch grid, with the
  level count capped by the 12.5 mm measurement floor.
- **Drop the cycloid.** Keep "the shared thing must agree".

## Standing instruction

Do not open a geometry-authority replacement while RISK-1 (no real CPU/GPU parity) and
RISK-2 (asymmetric shared corner) are open. Those two are the actual defects that the
cycloid story is groping toward, and they are fixable inside the existing oracle.
