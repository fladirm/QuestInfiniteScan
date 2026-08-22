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
