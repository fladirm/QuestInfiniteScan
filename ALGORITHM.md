# Σ-PRISM-16 algorithm map

This is a compact implementation map. [`new_spec.md`](new_spec.md) is the sole
normative definition; this file must never override its exact arithmetic,
admissibility, causality or persistence rules.

## Canonical state

```text
                synchronized finite-footprint sources
       RGB_L   RGB_R   DEPTH_L   DEPTH_R   retained views
          \      |       |         |          /
           independent outward-rounded S16 cells
                         │
                  exact intersection
                         │
                         ▼
              Ψ : Σ₂ → S16, Q16.48
                         │
      ┌──────────────────┼───────────────────┐
      ▼                  ▼                   ▼
 geometry readout   singular topology   appearance readout
      │                                      │
      └──────── disposable GPU mesh/PBR ─────┘
```

Canonical coefficients and every state-changing comparison use checked signed
Q16.48 with nearest-even point rounding and outward interval rounding. Sensor
confidence changes an admissible cell's width; sensors are never averaged or summed.

## Exact algebra

Build tooling generates one signed-XOR Cayley-Dickson descriptor for S16:

- multiplication and conjugation tables;
- left/right basis permutations;
- exact zero-divisor/annihilator dyads;
- Hadamard and readout rows;
- stable fingerprints and a bracket-preserving operator IR.

The CPU semantic oracle and HLSL lowering consume the same generated descriptor.
Dense generic multiplication is a reference/fallback only; hot paths lower to
permutation, sign, add/subtract, shifts, comparisons, masks and reductions.

## Carrier and causality

Unallocated carrier samples are exactly `z_null`. Sparse `64×64` pages and `8×8`
logical blocks are storage/execution locality only; page boundaries have no physical
meaning. Exact `NULL`, `CONST`, `AFFINE`, `DELTA` and `RAW` codecs reproduce every
Q16.48 coefficient bit-for-bit.

Each calibrated pixel is a finite cone. Its depth observation constrains the hit and
the observed pre-hit interval. Everything behind the first hit is force-free UNKNOWN
and has exactly zero canonical effect. A conflict remains an explicit empty
intersection with provenance; it cannot be hidden by averaging or destructive
carving.

## One joint inverse problem

Both depth views and both RGB views independently form conservative S16 source
cells against the same predicted carrier. Their inclusive componentwise meet is
source-order and left/right-order invariant. A legal non-empty meet commits the
minimum-change representative while preserving stronger prior information.

Repeated calibrated RGB footprints narrow the same carrier coordinates that produce
geometry and directional appearance. There is no canonical texture atlas,
displacement world, correspondence map or detached photometric correction.

## Intrinsic topology and detail

Topology is read from exact annihilator and associator singularities of neighbouring
carrier states. There are no explicit chart, boundary, split or merge objects.
Supported singular loci clip derived interpolation; unresolved loci fail closed.

Detail is literal local variation of Ψ. When independent evidence exceeds current
carrier sampling, a local bijective gauge deformation stretches the carrier into its
implicit-null domain while preserving accepted constraints, topology and information
strength.

## Scene evolution and world scale

Only independent multi-view pre-hit exclusion may support contact disappearance or
transport. Post-hit UNKNOWN and a nearer occluder cannot erase hidden state. The
current selected Ψ revision is the sole prediction source.

Resident pages are limited to visible, inverse-active, dirty, halo and probation
locality. Immutable exact generations and minimal proof certificates persist to
flash before eviction. Paging coordinates and eviction never alter physics.

## Derived rendering/export

Dirty supported cells materialize ordinary indexed meshlets entirely on GPU.
Raster first-hit visibility, singularity clipping, frustum/Hi-Z culling,
screen-space LOD and indirect stereo draw lists are disposable readout caches. GLB
and confidence-bearing PBR are derivative exports and cannot mutate Ψ.

The precise kernels, records, tests, performance budgets and physical acceptance
corpus are specified in sections 35–50 of `new_spec.md`.
