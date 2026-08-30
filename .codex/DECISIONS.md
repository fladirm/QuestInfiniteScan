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

## ADR-S441 — N1R scanner algebra authority is capsule-bounded

- `Tools/sigma/authority/I_TOE_S16_K16_NATIVE_CLOSURE.md` is the sole admitted
  TOE input. Its workspace SHA-256 is
  `9cdc8b1f3bfecfa3a49805be82ea786cdbf681ee8ffbdab0733d18dc24cfffef`;
  the declared upstream monograph SHA-256 is
  `9d2e3604846305cfe5244a4ef49f169632c60582cf895256fadc36426dc5786f`.
- Only capsule sections 1–8 enter the generated program. Cosmology, particles,
  masses, couplings, SI/metrology and every other monograph sector remain outside
  the scanner authority boundary.
- The Merkaba input fingerprint uses the numeric/multiplication/zero-divisor
  native core and excludes legacy `G/F/RGB` readout/operator bundle authority.
- The capsule supplies no E22 inventory and no `C_vk=C_kv=0` proof. Direct full
  S16 dependencies therefore remain and no shadow-transparent mode is frozen.
- The capsule fixes `A_k^2=-(2^k-1)I` but not one `A_1` orientation. Generated
  code retains only the orientation-independent `-1/-3/-7/-15` recurrence and
  never manufactures a concrete shell matrix.
- Capsule section 8 freezes diffraction `G=2A^TA=-2A^2`, exact link defect,
  primitive-normalized link and associator factors, `(W-I)/2` plaquette defect and
  their direct sum. There is no `epsilon_cl`, fitted tolerance or independent
  continuous closure weight.
- Q16.48 lowering retains outward normalized defect intervals: zero-excluding is
  incompatible, singleton zero exact-closed, zero-containing non-singleton
  unresolved. Zero primitive `G`-norm remains an unresolved diffraction-kernel
  factor.
- Algebra zero is the proved `ZEmpty` representation and legacy nonzero
  `ZNullDyad` is rejected. Three backing spellings abstract-interpret identically
  through all seven generated forward query entries.
- The sole self-hashed generator emits numeric CPU/HLSL expression, reduction and
  reverse plans with arity, neighbourhood, brackets and provenance. After the
  corrective fresh-admission expression, program fingerprint is
  `c98855216dd16d059ebaf0c33652250b7acac4681b01e0d585ab0ba28de67af3`.
- N1R generated execution plans are test-only to preserve the production-LOC
  stop rule until a run has an immediate consuming/deletion path. Production
  `Runtime/SigmaPrism + Runtime/Resources/SigmaPrism` is byte-equal to N0R and
  `cac9ab0`. The corrective commit is `+0/-392` production LOC versus rejected
  parent `b541635`; cumulative N1R is 17274 authoritative LOC, gross/new/net
  `0/0/0` versus `cac9ab0` and net `+601` versus the 16673-LOC `d3b83e1`
  baseline. These are an N1 run cursor, not a claim that final N7 deletion gates
  have passed.
- N1R introduces no live mutation. At its checkpoint N2R remained unopened; the
  later non-mutating oracle decision is ADR-S442.

## ADR-S442 — N2R oracle executes the generated graph with bounded parallel lowering

- The premature N2R checkpoint `869f848` and incomplete correction `b4d88d6` are
  rejected. A generated entry point is executable authority: its forward/reverse
  expression, actual arity and reducer descriptors drive evaluation; an entry-
  point name cannot label one hand-written sensor evaluator.
- `NONE/DEBUG`, direct-order first-hit and `EXPORT_RELATION_GATED` are distinct
  reductions. DEBUG returns only its requested generated relation; export keeps
  manifestation and gates connectivity through the generated native-relation
  class. `EYE_PAIR` is one coherent observation context with two retinal query
  rows, evaluated together rather than impersonated by one generic query.
- Native relation and complete-program identity transport are derived from the
  generated relation factors plus exact gauge records. The contractor ABI carries
  no authoritative `relation satisfied` or `identity transport` booleans.
- Whole-query reduction groups refined contributions by the complete 64-bit
  support identity before first-hit classification. A 32-bit support mask is not
  a semantic representation.
- The N2 proof graph has exactly four query/relation and three contractor/overflow
  GPU entry points. Relation count maps to 256-thread workgroups containing the
  16x16 signed-XOR product plane, exact signed 256-bit Q16.48 `G` reduction and all
  168 annihilator actions; field reduction uses one 128-thread bitonic/segmented/
  minimum network. Cardinality changes grids
  and work items, never a dispatch sequence per relation, support, lane, bracket,
  PWL segment or execution window. A serial interpreter is forbidden.
- The hot reducer handles up to 128 contributions without truncation. Larger
  bounded oracle input emits an explicit reason-coded cold-continuation receipt;
  it never silently drops support 128+, and cold handling may not become an added
  hot dispatch chain.
- The optical contractor executes the calibrated per-channel exposure, gain,
  illumination, white-balance, offset and monotone PWL transfer law. A fingerprint
  without transfer evaluation has no proof value.
- N2 remains non-mutating and production-neutral. Editor/Vulkan timings prove
  semantic parity and graph shape only; product GPU comparison against the
  `cac9ab0` profile begins when the generated graph becomes live on Quest in N3R.

## ADR-S443 — Fresh base admission is reverse-program output, never supplied identity

- The N3 preflight correctly rejected an oracle that began with externally
  proposed S16/gauge values. Base-density `ZEmpty -> supported` admission belongs
  to the sole generated Merkaba reverse program and must exist before live N3.
- For the frozen minimal fresh context, coherent left/right outward shadow cells
  intersect, satisfy the four-axis tangent constraint, select the deterministic
  minimum-change representative from `ZEmpty`, and lift through the exact dual
  Merkaba frame into one full S16 state.
- The generated program forward-verifies the lifted shadow and derives its mixed
  `(state,ZEmpty,ZEmpty)` native relation internally. No boundary enum, relation-
  satisfied/identity boolean, proposed state/gauge, pixel, XYZ, candidate kind or
  NOVEL identity is an input.
- The relative base pattern is exactly one level-zero `chi_0/kappa_0` cell for the
  N3 vertical slice. All surviving reverse alternatives must serialize the same
  full-S16 state, relative support and relation witness modulo the generated
  global-translation gauge; otherwise the result is `UNRESOLVED`.
- A nonzero exact Q16.48 point defect numerator with positive primitive `G` norm
  is a proof of a nonzero normalized factor even if an outward enclosure contains
  zero. Uncertain interval defects retain interval classification; diffraction-
  kernel factors remain unresolved.
- The corrective N2 supplement proves this generated operation from coherent raw
  observation input with branch-parallel workgroups and bounded collective union
  reduction. Its hot proof graph is exactly three dispatches over the existing
  Contract/Relation entry points; branch count changes workgroups, not submissions.
  Unique/common/order/right-eye cases match CPU and Vulkan; ambiguity, behind-hit
  and missing evidence remain unresolved. More than four hot alternatives emits an
  explicit cold-continuation reason rather than truncating or minting support.
- N3 may bind this accepted operation only after the corrective N2 checkpoint and
  may not recreate it in host or live shader code.

## ADR-S436V3 — Constructive Quest-to-Merkaba boundary

- `StereoRigFrameLease` remains raw coherent capture ABI; it is not itself an
  `I_Q` observation and may not carry host-manufactured Merkaba rows.
- The sole generator now owns one fingerprinted adapter from existing per-view
  pose/intrinsics/timestamp/format/depth fields plus the calibrated cone/footprint
  and metric-order instrument math into exactly eight retained sensor leaves.
- The calibrated room-gauge cone ray selects one canonical global-sign/four-axis
  permutation through the integer tetrahedral pullback and a fixed comparison
  network. Ties use axis order; no observation/allocation order, pixel identity,
  XYZ identity or dispatch-per-row enters the selection.
- Each leaf remains separately sourced. Only after retention does the common
  generated expression form the outward `4I-11^T` Merkaba tangent envelope; all
  original leaf factors remain required for forward verification.
- PCA exposes post-ISP GPU textures but no raw exposure/gain/white-balance record.
  Therefore the bounded fallback is the existing calibrated 2x2 post-sampler code
  hull fingerprinted by graphics format and calibration provenance. It may prove
  only a post-ISP code relation, never scene-linear radiance or arbitrary optical
  compatibility.
- Corrective N1/N2 remain production-neutral. Any live N3 consumer must bind this
  generated construction and may not reproduce its logic as a parallel host or
  shader authority.

## ADR-S444 — N3 fixed graph, lazy residency and evidence ownership

- The N3 live `NativeCloseCommit` is a fixed nine-dispatch graph. Relation and
  support cardinality map to parallel workgroups; host dispatch count never scales
  with pages, segments, relations, supports, source leaves or S16 lanes.
- The decoded-memory budget is a residency ceiling, not an eager allocation
  target. N3 starts with one bounded two-page current/shadow pair; N5 owns pager
  growth, eviction and rehydration.
- A terminal `UNRESOLVED` outcome owns a copied compact exact record, never a
  coherent capture lease or RGB/depth textures. Capture leases are released after
  the GPU fence and readback issue on every terminal path.
- N3's temporary visual preview remains a disposable legacy-G readout until N6.
  It is not evidence or mutation authority and may not force the fresh Merkaba
  preimage into an old geometry projection. The published root is authoritative
  even when the one-cell N3 bootstrap is visually imperceptible.

## ADR-S445 — Full-frame fresh support is one set-level native operation

- N3's one coherent footprint -> one relative level-zero cell is an accepted
  bootstrap theorem, not a full-frame placement rule.
- N4 may not map pixels, XYZ, tiles, hashes, page/sample addresses, allocation
  order or workgroup completion order into physical `Sigma_2` incidence.
- Before full-frame N4 runtime work resumes, the sole generated program must own
  one constructive set-level operation that maps the whole coherent observation,
  published field and retained exact frontier to either a common normalized finite
  relative support/state/gauge/certificate delta or an exact unresolved union.
- The missing authority must define how native multi-support relation context
  yields relative `Sigma_2` incidence/displacement and how disconnected-component
  translations are normalized. The latter is now frozen: independent integer
  translation only, per-component translation normalization and complete-byte
  multiset ordering; no rotation/reflection/axis permutation/sign/scale gauge and
  no persistent component identity. A relation classifier on already supplied
  tuples is insufficient.
- Inspection of the approved upstream B.12--B.16 hard-stops corrective N1. B.14
  assumes neighbouring modes and supplied `U_ij`; B.12 supplies sign transport
  only after its bit generator/address are known. No approved equation constructs
  `(native ports, U_ij, Sigma_2 incidence)` from two modes and complete context.
- Tiles/workgroups remain disposable execution partitions. They may change grid
  cardinality but never normalized bytes or dispatch-sequence semantics.
- The current N4 certificate/refinement tree is forensic evidence, not an accepted
  checkpoint, until a narrow production-neutral corrective N1/N2 theorem/oracle
  closes this authority gate.

## Explicit supersession

- ADR-S404R, ADR-S423 through ADR-S435 and their V2 revisions are superseded by
  the active V3/V2 replacements above.
- ADR-S415 through ADR-S422 remain superseded germ-first history.
- ADR-S405 through ADR-S414 remain evidence for exact primitives and failed
  lowerings only.

## ADR-S446 — Constructive incidence is query contact AND native modal stitch

- Upstream B.12--B.16 remains authority for native sign transport, bracket,
  associator and loop compatibility only; it is not retroactively claimed to
  construct scanner incidence.
- Scanner `I_Q` constructs an exact transient contact candidate only from
  intersecting calibrated finite first-hit boundary envelopes. There is no fitted
  epsilon, and pixel/XYZ proximity cannot prove incidence.
- `I_TOE` then evaluates the complete native relation. No stitch, one equivalent
  stitch class and multiple non-equivalent classes mean respectively no incidence,
  resolved incidence and an exact unresolved disjunction.
- A resolved stitch owns only relative dyadic boundary incidence plus exact
  orientation/transport and bracketed proof context. All stitches are solved as
  one constraint set; inconsistent loops remain unresolved without coordinate
  repair.
- Stitch-disconnected components admit independent signed integer translation
  gauge only. Component identity is transient normalization scratch. Complete
  normalized component bytes are multiset-sorted; packing distance is not physics.
- The N1R-5 executable CPU/HLSL authority is test-only during corrective N1/N2.
  Runtime activation is deferred to N4 after bounded CPU/Vulkan set-level proof.

## ADR-S447 — Implicit footprint complex and unresolved native orientation boundary

- A coherent fresh frame is a disposable implicit square footprint complex, not a
  general contact graph. Fresh broad phase enumerates each `RIGHT`/`DOWN` shared
  footprint boundary exactly once; for `320x320` this is `204160` boundaries and
  `101761` implicit plaquettes. Neither collection is materialized as world state.
- The only hot semantic phases are `FOOTPRINT -> BOUNDARY -> CLOSE`, mapped into
  the existing native graph. Plaquette, cycle, component, potential, normalization
  and packing are mathematics inside `CLOSE`, never managers, identities, object
  ABIs or dispatch families. Cardinality changes workgroups, not submissions.
- Sampling `LEFT/RIGHT/UP/DOWN` is broad-phase query orientation only. It cannot
  produce a native port, `Sigma_2` incidence or `DeltaU/DeltaV`.
- N1R-5's executable acceptance is revoked. Removing caller-supplied factor/loop
  truth is insufficient while a caller-supplied `PlaquetteC` still chooses a
  native branch or an unproved fixed `{1,2,4,8}->{+U,-U,+V,-V}` table supplies
  intrinsic direction.
- Corrective N1 is hard-stopped until scanner authority provides the exact
  matched-native-sector/transport-to-signed-dyadic-transform equation and its
  reversal/composition law, or explicitly declares and justifies a complete
  representation theorem. Pixel, XYZ, sample side, page/address, allocation order
  and every legacy topology path remain forbidden fallbacks.

## ADR-S448 — Abstract native incidence precedes `Z² semidirect D4` chart embedding

- ADR-S447's final hard stop is resolved by removing its over-strong requirement:
  a native Merkaba stitch does not output physical `DeltaU/DeltaV`. It outputs an
  abstract matched continuation-sector incidence plus exact signed-XOR native
  transport, brackets and factor receipts.
- The generated pair transport is source-backed K16 algebra:
  `g=a xor b`, `U_ab(u)=epsilon(a,g)[u e_g]`; the swapped reverse is evaluated
  independently. Native sectors and sample boundary sides have no chart-axis
  meaning and no static mapping to signed U/V exists.
- Finite square-dyadic coordinates are a separate representation embedding. One
  stitch-connected component is quotient by `Z² semidirect D4`; disconnected
  components receive independent copies of that chart gauge until a later stitch
  joins them. Dyadic scale/level is not gauge.
- D4 changes chart incidence representation only. It never transforms physical
  S16/native transport. Lexical byte minimization is legal only within one admitted
  chart-gauge orbit; non-D4-equivalent surviving incidence patterns remain exact
  unresolved alternatives.
- Four abstract native sectors have 24 possible square-side assignments, which
  split into three D4 orbits of eight. No fixed sector-to-side chart convention is
  authority: the generated normalizer enumerates the complete finite assignment
  set, resolves only one surviving orbit and retains multiple surviving orbit
  classes as `UNRESOLVED`. Sampling boundary sides cannot choose an orbit.
- The assignment candidate is component-wide chart representation. Local chart
  frames are then derived from matched sectors plus the exact native orientation
  witness. It is neither one globally fixed native-sector convention nor an
  independently chosen chart assignment at every locality.
- Full-frame fresh broad phase remains the disposable implicit RIGHT/DOWN finite-
  footprint complex. It proposes contact only; generated native closure decides
  incidence. Runtime remains only `FOOTPRINT -> BOUNDARY -> CLOSE`, with no graph,
  loop, component or placement manager authority.

## ADR-S449 — Corrective N2 is a bounded parallel proof, not runtime topology

- Corrective N2 lowers the generated constructive stitch theorem into exactly two
  disposable Vulkan proof submissions: one 256-way native sector-pair evaluator
  and one 256-way bounded set closure. Runtime/Resources and the accepted nine-
  dispatch live graph remain unchanged.
- One proof case fits in one workgroup, so its fixed eight-round label/potential
  contractions use valid group-local barriers. They are not a data-dependent
  solver, a cross-workgroup synchronization scheme or a physical component-size
  limit.
- The set-close dispatch derives transient component membership and performs the
  complete 24-assignment/D4 quotient independently per component. Its result
  header is the final semantic disposition; host code may independently verify
  component, assignment and orbit receipts but may not repair or override it.
- Disconnected components may resolve under different non-D4 chart-orbit classes
  because each owns an independent chart gauge. A later stitch removes that
  independence and forces one joined chart problem, which remains unresolved when
  no unique common orbit survives.
- N4 may reuse the proved algebra and finite orbit semantics, but not the bounded
  case layout. Full-frame cardinality must become compact wide worklists and fixed
  global synchronization boundaries, never dispatch-per-component or a serial
  superkernel.

## ADR-S450 — N4R CUT-E uses fixed synchronization passes, not one superkernel

- N1R/N2R authority, the `FOOTPRINT -> BOUNDARY -> CLOSE` semantic cut, A--D and
  root-last publication remain unchanged. This decision changes runtime lowering
  only.
- The nine-dispatch WIP placed all CUT-E D4 ordering, exact witness comparison,
  refinement, component ordering, relocation and page planning in one 256-thread
  `PrepareNativeRevision`. Unity Vulkan FXC crashed after 600.77 seconds; the
  standalone lowering has about 4614 CFG blocks, 428 loop nodes and 77 static
  synchronization sites. It is rejected as a serial-superkernel evasion.
- N4R replaces that one entry point with at most six fixed wide passes in the
  existing shader/resource family. The target graph is 14 hot dispatches and the
  hard ceiling is 16. Submission count never depends on pixels, stitches,
  components, pages, mutations or history.
- Existing buffers, bounded close scratch, counters, carrier ABI and generated
  semantics are reused. No physical identity, manager, host loop, readback,
  fallback ordering or S32 path is introduced.
- Every CUT-E pass is timestamped by the existing GPU query instrumentation and
  reported independently. Final state/gauge/certificate visibility remains one
  root-last exchange.

## ADR-S451 — Scan admission is fixed at 15 Hz; immutable-root readout is XR-cadenced

- The cadence is execution policy only. It changes no `Psi`, S16, relation,
  evidence, chi/kappa, certificate or canonical byte semantics.
- `RoomScanner` owns one fixed `1/15 s` admission gate. It transfers a coherent
  observation only when no prediction is pending and no native close is in flight.
  A late success starts one fresh interval; elapsed ticks are discarded rather
  than replayed.
- `SigmaRigBridge` holds at most one coherent owned observation at the gate. The
  provider cannot turn display-frequency callbacks or duplicate timestamps into
  repeated scan work.
- `SigmaRenderer` presents the latest immutable published carrier every XR frame.
  Sensor admission is no longer driven from its `LateUpdate`, and stopping or
  backpressuring scan does not invalidate the published-root readout.
- This decision adds no dispatch, buffer, readback, CPU physical authority or
  fallback. It bounds submission backlog but does not waive the N4 kernel timing
  or resident-capacity gates.

## ADR-S452 — Durable COW root, disposable residency and completion-bound retirement

- Two page-fault classes are distinct. `SIGMA_N4_FAULT_PAGE_VALIDATION` is an
  application fail-closed receipt that retains the prior root. A KGSL/MMU page
  fault is an invalid device-memory access. The historical SimpleScanner crash is
  attributed to publication-resource retirement before queued GPU use completed;
  N4's 128-plan/56-resident-pair boundary is a separate capacity-planning defect.
- N4 safety computes legal page plans from actual resident current/shadow pairs,
  clears mutation/clone/scatter work on overflow, bounds-checks clone/scatter and
  leaves the previous root authoritative. Planning scratch is never resident
  capacity or logical world size.
- N5 durable backing is `HEAD -> immutable RootObject -> immutable sparse COW
  radix/Merkle page map -> PageRecord -> SHA-256(canonical EncodePage bytes)`.
  Content hashes provide integrity/lookup only; full logical keys and bytes remain
  authoritative. Live persistence writes dirty pages and affected COW paths, not
  full snapshots.
- GPU residency owns one sparse logical-page-to-transient-locator map and one dense
  reverse slot table. Segment/bank/slot/hash bucket/probe/allocation/eviction order
  is disposable and cannot reach physics, D4 normalization or canonical bytes.
- `COLD_DURABLE` is not `ABSENT_IN_ROOT` and never means `ZEmpty`. A cold page or
  missing/stale support summary is conservatively included and asynchronously
  rehydrated.
- A slot/resource/generation is reusable only after its required durable root is
  reachable, its last GPU reader/writer completion is proved and all publication/
  readout/persistence leases release. Pending retains it; failed/unprovable
  completion quarantines it. CPU ownership reaching zero never proves retirement.
- N5 converts the N4 resident-capacity receipt into bounded cold eviction/load plus
  exact observation replay. It does not reopen N1--N4, add physical state or treat
  storage pressure as unresolved/default/end-of-scan. N5 remains pending until N4R
  acceptance.

## ADR-S453 — N4.1R is a Quest-first active-cardinality compiler cut

- The real N4 Quest profile rejects the current lowering, not its ontology:
  `500.623 ms` timestamped compute repeats full-capacity transformations and uses
  one-workgroup/global-run interpreters even though the graph is fixed.
- The native graph is frozen at exactly sixteen submissions. Existing positions
  may become real parallel synchronization cuts; no seventeenth dispatch, kernel
  family, buffer owner, CPU authority/readback, fallback order or S32 is admitted.
- The codebase-grounded fixed order retains `PrepareNativeRefinementPlan` at
  submission 8 as the canonical-run merge cut, followed by canonical select,
  D4 proof, component order and refinement scan. The legacy entrypoint name has
  no semantic authority; changing it would add churn without changing the fixed
  synchronization graph.
- Capacity work is allowed once for coherent raster ingress, once for FOOTPRINT
  contraction and once for lossless implicit-boundary broad phase. Afterwards work
  scales with active/realized/changed/unresolved/touched state. Atomic compaction
  order is routing only and can never enter canonical or physical bytes.
- The sole generator emits exact specialized small-dyadic actions and complete
  finite D4/orbit/adjacent-frame tables. Their optimized CPU/HLSL result must equal
  the existing semantic operation bit-for-bit, including per-term nearest-even
  rounding, overflow, outward intervals, brackets and summation order.
- No workgroup owns `O(frame)`, `O(world)`, `O(capacity)` or a giant sort run.
  TILE_CLOSE uses one transient deterministic forest, packed three-orbit pointer
  doubling and complete non-tree validation. Canonical/refinement stages consume
  compact realized streams and fixed parallel reductions.
- Quest 3 / Adreno 740 constraints are source gates: <=256 hot threads, <=8 UAVs,
  <=32768 bytes LDS (occupancy review above 16384), <=65535 direct grid dimension,
  <=128 MiB per storage binding and 64-byte storage alignment. Both group-sync
  intrinsics require uniform synchronization cardinality.
- Every exact shader variant first passes static graph/LDS/UAV/sync/name checks,
  HLSL-to-Vulkan-1.1 SPIR-V and `spirv-val`, then targeted Unity Android/Vulkan
  compile, semantic parity, full suite and finally Quest driver/performance proof.
  Full APK rebuilds are milestone gates, not a one-line shader debugger.
- N4.1R targets <=40 ms typical and <50 ms warm ordinary informative p95. The
  fixed 15 Hz admission scheduler and independent XR readout remain scheduling
  policy and cannot waive total GPU work.

## ADR-S454 — Refinement scheduling is immutable across the mutation boundary

- Dispatch 12 is the sole producer of refinement execution scheduling. Refined
  membership, block-prefix counts and child ordering live in explicit ranges of
  the existing disposable `CloseScratch`; no mutation arena aliases them.
- From entry to multi-workgroup `PrepareNativeRevision`, `StateDelta` and
  `GaugeDelta` are terminal output-only. No workgroup may read scheduler state
  from either buffer while another workgroup publishes mutations.
- The refined prefix is represented by one bit per logical sample plus one exact
  count/prefix per 256-sample block. Child order is a bounded actual-observation
  stream; four remains the physical child count and never an observation cap.
- This is an execution-lifetime correction only. It changes no S16, D4,
  certificate, refinement, page-plan or root-last semantic rule and adds no
  buffer owner, ABI field, kernel or dispatch.
