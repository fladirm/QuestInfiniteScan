# Active architecture decisions

`new_spec.md` is authority. Historical v6/v7, germ-first v8 and v8.1
branch/chart decisions remain in Git history only.

## ADR-S400 — Clean branch and one reconstruction product

- The previous mapper is recoverable from Git but is not an implementation donor.
- Active reconstruction remains under `Runtime/SigmaPrism` and
  `Runtime/Resources/SigmaPrism`.
- No compatibility reconstruction, fallback or second canonical world may coexist.

## ADR-S401V2 — One carrier and one S16 medium

- Canonical reality is `Psi : Sigma_2 -> S16` in checked nearest-even Q16.48.
- `Σ₂` is one intrinsic carrier/gauge, not a physical chart/sheet/object
  namespace; signed-64 coordinates address its current finite representative.
- `S16` is an algebra value, never semantic XYZ/RGB/topology lanes.
- Disconnected/folded/two-sided manifestations are field values and relations.

## ADR-S403 — Exact arithmetic and brackets

- Generated signed-XOR algebra and explicit expression brackets define canonical
  arithmetic.
- Intervals round outward; overflow fails closed; FP cannot decide mutation.
- GPU CSE/fusion may not reassociate expressions.

## ADR-S423V3 — One generated Merkaba relation-program IR

- Baseline `CPQ4-2026-08-25-S16-v8.3` supersedes the v8.2 incomplete
  representation/runtime contract and the v8.1 descriptor-subsystem
  decomposition before live N1 work.
- Exact S16 algebra, the supplied TOE native artifact and frozen query-boundary
  schemas compile to one fingerprinted relation-program IR with per-expression
  provenance, exact arity, neighbourhood, brackets, eigenmode/shadow coupling,
  query reductions and reverse contractors.
- Sensor, eye, intrinsic relation, prediction, export and debug are entry points of
  that same IR. Specialized plans/kernels are compiler products.
- E22 is optional inventory; exclusive routing requires contextual separation and
  complete-law sufficiency for every supplied arity/operand position, or direct
  S16 dependencies remain.
- The donor scalar opcode vocabulary may be extended/replaced only when the
  source-backed TOE/query law requires it; physics is never simplified to fit it.
- `I_Q` includes calibrated illumination/exposure/gain/transfer nuisance relations
  with a conservative provenance-bearing missing-metadata route.

## ADR-S424V2 — Query-level shadows and first hit

- Physical output is `M_q[Psi]`.
- Local evaluation and whole-query reduction are lowerings, not ontology.
- Reduction owns overlap, direct order, first-hit, occlusion, finite footprint and
  query-relevant ZD/nonassoc context.
- The rig supplies two coherent RGB-D shadows; depth and optical leaves per eye are
  separately retained, non-bootstrap and non-weighted while their shared
  footprint/pose/first-hit context remains part of the joint reverse query.

## ADR-S425V2 — Preimage disjunction is contractor scratch

- Exact inverse is `{Phi : M_q[Phi] in O}` and may be disjunctive.
- One-winner and mutate-all are invalid.
- Branch masks/ranges are disposable reverse-program scratch.
- Cross-frame ambiguity persists only as exact unresolved constraints plus source
  evidence, with no hypothesis/branch/chart/extent identity.

## ADR-S426V2 — One NativeCloseCommit

- Both-eye reverse constraints and native relation predicates enter one feasible
  close before selection.
- The current feasible field produces canonical no-change.
- A delta requires one resolved physical equivalence class or one common delta
  over the complete surviving union.
- Physical phases and profiler labels never become semantic authorities.

## ADR-S427V2 — Complete-program fibres, not per-query kernels

- A query-transparent native direction may remain active through TOE-supplied
  coupling.
- Preserve the prior representative only along an equivalence fibre proven for the
  complete accumulated relation program.
- A linear right-lift is legal only after generated complete decoupling proof.

## ADR-S428 — ZD, near singular, nonassociation and order differ

- Exact ZD requires exact annihilation.
- A calibrated nonzero residual is near-singular, never exact ZD.
- Bracket trees remain descriptor-owned.
- Direct projective order never comes from ZD.
- Relation caches are disposable and own no topology.

## ADR-S429V2 — Native default is a field value

- N1R derives/fingerprints `ZEmpty` from the authoritative program and proves exact
  unbacked/allocated/NULL representation parity in every query/relation context.
- All-default neighbourhoods yield reducer identity, `DEFAULT_SAT` and no active
  work; mixed default/support boundaries retain descriptor-defined semantics.
- Replacing supported state by `ZEmpty` is a physical mutation and may reveal
  farther first-hit support; it is not covered by backing equivalence.
- Current `ZNullDyad` is only a candidate until sensor/eye/relation/export gates
  pass; `Gz=0` alone is insufficient.
- After proof, unbacked logical carrier, allocated `ZEmpty` and NULL codec decode
  are storage representations of the same S16 value.
- No physical ABSENT state or support bitmap exists beside S16.

## ADR-S430V3 — Exact representation gauge and refinement remain the same field

- Repeated queries first contract S16 at current sampling.
- A published finite representative uses exact `chi : Z2_backing -> Sigma_2` plus
  generated `kappa` intrinsic support/measure/reconstruction. Any Riemann-like
  local metric and `rho=sqrt(det(g_chi))=abs(det(J_chi))^-1` are derived
  representation density, not physical state or floating mutation authority.
- Gauge equivalence requires a generated admissible bijection with pointwise full-
  S16 equality and exact relation/proof transport. Equal readouts alone may not
  erase a hidden native direction.
- If retained evidence proves finer intrinsic variation, an exact normalized
  `chi/kappa` reparameterization increases sampling density of the same `Psi`.
- The reparameterization is a representation theorem proved against the complete
  generated program; it need not be a separately named TOE source equation.
- Gauge-equivalent results require a constructive observation/allocation-order-
  independent normal form or remain unresolved.
- State/gauge/certificate/directory generations publish atomically under one root.
- No physical germ split, chart manager, voxel hierarchy or detail world appears.

## ADR-S431V2 — Evidence is proof with bounded exact minimization

- Exact uncertainty is preimage/disjunction, provenance, independence and source
  evidence.
- Scalar confidence is diagnostic only.
- Complete evidence precedes publication and deterministic certificates govern
  reclaim.
- A minimizer may release raw evidence only after proving complete-program
  equivalence; converged duplicate/weak revisit storage is bounded and proof grows
  only with unresolved or genuinely new native information.
- Evidence cannot supply export colour/detail missing from `Psi`.

## ADR-S432 — Static correction before temporal evolution

- Independent clear-path/pre-hit evidence may remove false support from one static
  field.
- Behind-hit remains no evidence.
- S4‑09 handles only observations irreconcilable as one static admitted epoch.

## ADR-S433V2 — Pure query readouts

- Eye, prediction, intrinsic diagnostics and export use entry points of the same
  generated program.
- Readout caches own no continuity or identity.
- Deleting a cache changes no field, proof or other readout.

## ADR-S434V3 — Low-code lowering and hard deletion

- Target phases are SelectNativeQuerySupport, EvaluateNativeQuery,
  ReduceNativeQuery, ContractNativeQuery,
  CloseNativeConstraints, cold ResolveContractorOverflow and sparse root-last
  commit.
- N3R is the single publication-capable sensor/native-relation cutover and deletes
  both legacy inverse and legacy topology/edge paths plus their generated ABI in
  the same commit. It already admits proved base-density fresh support. N4R–N6R
  delete each later replacement immediately.
- Final gates: gross delete >=10000, new production <=4000, net <=-6100 versus
  cac9ab0, net <=-5500 versus d3b83e1, zero legacy/fallback.

## ADR-S435V3 — Deterministic closure and realtime contracts

- S4‑08.6 follows the active N0R–N7R plan and sole resume cursor.
- N1R blocks rather than guessing TOE law, full-query default or coupling.
- Every accepted run regenerates code graph and commits separately.
- Only N7R archives/builds/installs and physically accepts the identical commit.
- `1500/1800 ms` is a recovery ceiling. Final gates are stable no-change
  `<=33.3 ms`, ordinary informative p95 `<=100 ms`, and eye queries
  `<=13.89/11.11 ms` at 72/90 Hz; cold work cannot block published-root eyes.
- S4‑09 remains unopened.

## ADR-S436 — Relation support is derived, never a graph

- Exact coordinate-local neighbourhoods are the primary sparse/fast relation
  domain, not the exclusive ontology of `Σ₂`.
- The generated program may derive descriptor-proven seam/gauge/nonlocal support
  tuples from `Psi`, query context and native law.
- Those tuples and generation caches are disposable. No seam table, chart
  incidence, XYZ welding or canonical topology graph exists.

## ADR-S437V2 — Frozen page geometry with gauge-aware unbounded backing

- S4‑08.6 freezes `64×64` logical pages and `8×8` codec blocks.
- Segment count, residency, dispatch grids and scratch partition may change cost
  only and must preserve byte-identical pages/generations/certificates/readouts.
- Adaptive density is encoded by exact normalized `chi/kappa`, not page-size
  changes or storage-invented interpolation.
- N5R owns a durable logical page/gauge directory, lossless on-device blobs,
  bounded decoded GPU cache, eviction/rehydration and clear/restore lifecycle.
- Resident capacity is never logical world size.
- A future page/block geometry migration requires an explicit backing-independent
  canonical logical serialization and is outside this closure run.

## ADR-S438 — Directional mould action and locality information

- Whole-query clear pre-hit plus measured first-hit reverses through the same
  bracketed sensor program into an exact query-direction action; behind-hit is
  identically `NO_CLAIM`.
- This is proof/scratch, not ContactFilm, a pressure buffer, carving, gradient
  descent or physical field beside `Psi`.
- Active localities retain exact feasible-set/coupled-factor certificates. A
  directional information form may compress them only after equivalence proof and
  may never replace disjunction/cross-locality constraints or become scalar
  confidence.
- Compatible evidence retains or tightens the feasible set; weaker evidence never
  broadens stronger state, duplicates do not vote and conflicts never average.
- Sampling refinement transports full S16 state, exact constraints, directional
  information, independent-view receipts, evidence, relations and supported
  bandwidth; old readouts match before higher-frequency information is added.

## ADR-S439 — Conservative sparse query support

- The generator owns `B_q(P)=0 => exact zero contribution` summaries for every
  query family. False positives are legal; false negatives are forbidden.
- A rebuildable index covers resident, nonresident and locally refined regions.
  Missing/stale summaries fail closed or rehydrate; they never hide physics.
- The index and page summaries are derived caches, not support identity.

## ADR-S440 — Fresh support and sole generated ABI cut

- N3R is not accepted until a fresh all-ZEmpty field publishes uniquely or
  gauge-equivalently proved base-density support without legacy proposal kinds.
- N3R modifies the sole generator and generated frame ABI; Candidate, Pending,
  Continuation, Novel and DirtyEdge definitions become unregeneratable.
- N3 unresolved evidence is in-session only; N4 replaces it with durable minimized
  exact constraints.

## Explicit supersession

- ADR-S404R, ADR-S423 through ADR-S435 and their V2 revisions are superseded by
  the active V3/V2 replacements above.
- ADR-S415 through ADR-S422 remain superseded germ-first history.
- ADR-S405 through ADR-S414 remain evidence for exact primitives and failed
  lowerings only.
