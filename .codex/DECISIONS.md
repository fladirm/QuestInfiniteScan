# Active architecture decisions

`new_spec.md` is the detailed authority. Previous Cone-PRISM decisions remain in
Git history and are not active on this branch.

## ADR-S400 — Canonical replacement and clean branch

- Decision: `CPQ4-2026-08-22-S16-v6` replaces the complete previous reconstruction
  architecture. Development occurs on `feat/sigma-prism-16-cpq4-20260822`, whose
  parent preserves the former mapper.
- Decision: retain only representation-neutral Quest/Unity capture, calibration,
  lifecycle, input/UI and GPU/build plumbing. Delete old reconstruction,
  persistence and compatibility wiring from the active branch.
- Consequence: no ContactFilm, PressureManifold atlas, explicit boundary/topology
  graph, old chunk schema or derived old mesh cache may become a donor abstraction.

## ADR-S401 — One exact carrier is the physical world

- Decision: the only durable physical state is `Psi : Sigma_2 -> S16`; unallocated
  carrier is implicit `z_null`. Geometry, singular topology, detail, appearance,
  scene evolution and render/export products are operators/readouts of this state.
- Decision: canonical values and state decisions use the inherited checked
  nearest-even Q16.48 NumericDomain. Independent sensors meet as exact admissible
  sets; confidence changes interval width and never becomes a summation weight.
- Consequence: all later DAG nodes must fail closed rather than introduce a second
  physical world, FP decision path, sensor consensus object or paging-defined seam.

## ADR-S402 — Section 49 is the implementation order

- Decision: the active DAG is exactly `S4-00..S4-13` from `new_spec.md`; additional
  kernels/helpers may only decompose those semantic stages and may not create a new
  ontology.
- Consequence: S4-01 exact algebra is a hard gate before live mutation, and S4-13
  physical Quest acceptance—not compilation—is the final product gate.

## ADR-S403 — Exact algebra authority and fail-closed Quest lowering

- Decision: `SigmaNumericDomain` is the sole CPU semantic authority for checked
  nearest-even Q16.48. One deterministic generator owns the signed-XOR
  Cayley-Dickson table, sparse basis/dyad actions, annihilator catalog, Hadamard
  rows and semantic bundle fingerprints consumed by C# and HLSL.
- Decision: hot-path algebra is an explicit bracket-preserving operator DAG with
  deterministic common-subexpression sharing. Sparse sign/XOR/permutation/readout
  operations may not route through generic qmul/qdiv; dense multiplication is an
  explicitly named reference/generated fallback only where arbitrary coefficient
  products require it.
- Decision: packed-32 Vulkan is the currently proven exact execution lowering.
  Native I64 remains fail-closed until separate parity evidence exists. A
  GPU-resident startup witness gate—not an optimistic CPU flag—must be bound by
  every later canonical mutation kernel.
- Consequence: later carrier/inverse kernels consume `SigmaOperatorSet.Canonical`,
  generated descriptors and the backend gate; they may not hand-code alternate
  sedenion arithmetic or infer backend legality from platform names.

## ADR-S404 — Exact sparse carrier storage and immutable publication

- Decision: canonical persistence bytes are defined by the deterministic
  NULL/CONST/AFFINE/DELTA/RAW CPU codec oracle; packed GPU decoded pages are an
  exact execution lowering of the same Q16.48 samples, not another state format.
- Decision: every published page generation is immutable. Canonical mutation owns
  an unpublished GPU write lease, publishes atomically after the exact backend
  gate, and never reuses a generation number after abort.
- Decision: physical pages, 8x8 codec blocks and segmented Vulkan buffers are only
  addressing/storage boundaries. Dirty publication is stably compacted and driven
  indirectly; no boundary may acquire reconstruction meaning.
- Consequence: later readout/inverse stages consume generation-keyed decoded pages
  and must create a new generation for accepted mutation. Persistence/restart must
  reproduce the exact selected page bytes and algebra/operator fingerprints.

## ADR-S405 — Exact derived forward readout and raster first-hit authority

- Decision: geometry support and projective position are decided by the generated
  exact packed-Q16.48 `G`/`qdiv` plan. Conversion to FP32 occurs only after that
  decision in a disposable readout cache; it cannot mutate or reinterpret `Psi`.
- Decision: only the latest immutable generation of each logical carrier page may
  enter prediction. Changed pages compact stably into indirect exact-readout work;
  derived neighbour halos cross physical page/segment boundaries and are refreshed
  only in the changed local neighbourhood.
- Decision: hardware rasterization is the visibility/first-hit operator for both
  timestamped depth-eye poses. Prediction preserves exact signed-64 carrier page
  limbs and immutable page generation/revision keys so S4-04 can pull every source
  constraint back into the same canonical state.
- Consequence: prediction/readout buffers are deletable, ref-counted GPU caches.
  No CPU geometry, synchronous readback, Unity Mesh or parallel geometry world may
  be introduced by inverse, topology or rendering stages.

## ADR-S406 — Independent depth cells and transactional latent-gauge promotion

- Decision: left and right depth remain independent conservative finite-cone Q16.48
  source cells. Their contribution is exact inclusive admissible-set intersection;
  confidence changes interval width only and never becomes a weighted sensor sum.
- Decision: empty meets and pre-hit exclusions produce bounded provenance evidence,
  not canonical mutation. Accepted proposals are revalidated against the immutable
  source generation before a new page generation is published, and weaker or
  correlated observations cannot reduce existing information resistance.
- Decision: image blocks and nearby carrier pages may schedule unmatched work but
  have no topological meaning. A latent-gauge generation remains unpublished until
  distinct L/R evidence promotes at least one exact sample; otherwise the write is
  aborted. Small scheduling counters may return asynchronously while all pixel and
  carrier arithmetic remains on GPU.
- Consequence: S4-05 topology must be derived from exact carrier transitions and
  evidence signatures, never from gauge allocation layout, image blocks, depth
  patches or spatial proximity.
