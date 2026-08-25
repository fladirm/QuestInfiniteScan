# Active architecture decisions

`new_spec.md` is authority. Historical v6/v7 and superseded germ-first v8 decisions
remain in Git history only.

## ADR-S400 — Clean branch and one reconstruction product

- The previous mapper is recoverable from Git but is not an implementation donor.
- Active reconstruction remains under `Runtime/SigmaPrism` and
  `Runtime/Resources/SigmaPrism`.
- Only representation-neutral capture/calibration/XR/lifecycle/UI/fence/indirect/
  persistence/build plumbing may survive.
- No compatibility reconstruction, fallback or second canonical world may coexist.

## ADR-S401 — Full S16 atlas field is the only physical world

- Canonical reality is `Psi : Sigma_2 -> S16` in checked nearest-even Q16.48.
- `Σ₂` is an intrinsic sparse atlas namespace supporting disconnected components,
  folds, multiple sheets and two-sided manifestations.
- Chart incidence is canonical domain structure, not a separate topology graph.
- A carrier state is full native S16, not independent property channels.
- Observations, hypotheses, branches, certificates, eye maps, geometry, meshes,
  textures and exports are noncanonical evidence or readouts.

## ADR-S403 — Exact arithmetic and bracket trees are semantic authority

- `SigmaNumericDomain`, generated signed-XOR algebra and authoritative explicit
  expression brackets define canonical arithmetic.
- Point arithmetic is nearest-even Q16.48; intervals round outward; overflow fails
  closed; FP cannot decide canonical mutation.
- GPU fusion may share expressions but cannot reassociate them.
- Packed-32/native-I64 paths require exact parity gates.

## ADR-S404R — ABSENT and allocated native null are distinct

- `ABSENT` means no materialized atlas/carrier state and has no S16 value.
- `NATIVE_NULL` is an allocated descriptor-defined S16 state/stratum and may be
  nonzero or use ZD/eigenmode structure.
- Missing pages and codec tags cannot silently mean native null.
- Immutable exact carrier generations and lossless codecs remain storage
  primitives; persistence/restart preserves the distinction.

## ADR-S423 — Authoritative native-law descriptor preserves actual TOE arity

- Baseline `CPQ4-2026-08-25-S16-v8.1` supersedes germ-first v8 before live N1 work.
- One generated descriptor contains S16 algebra, authoritative TOE
  Merkaba/eigenmode operator/frame/kernel semantics, relation family with explicit
  arity/brackets, manifestation, local query plans, whole-scene reducers, exact ZD,
  near-singular definition, nonassoc context, fibre-preserving selector/right-lifts
  and modal refinement capacity.
- Exactly 22 relations are an optional generated inventory, never independent
  channels. Exclusive E22 factorization requires faithfulness modulo a frozen
  harmless gauge/null equivalence. Without proof, direct S16/eigenmode dependencies
  remain.
- Scanner code cannot infer missing TOE equations from current geometry, inverse or
  topology code.

## ADR-S424 — Physical observations are whole-field shadows

- A bounded native neighbourhood produces only a local contribution.
- The physical sensor/eye shadow is a whole-scene reduction over all relevant local
  contributions.
- Scene reduction owns overlap, direct order, first-hit, occlusion, fold/sheet
  collapse, native-null manifestation, query-relevant ZD/nonassoc context and finite
  footprint integration.
- The rig supplies two coherent RGB-D shadows. Depth and RGB are independent leaves
  within each eye and cannot pre-contract one another.
- First-hit never belongs to one local germ descriptor.

## ADR-S425 — Inverse preserves support disjunction

- A measured footprint defines a union of possible native support hypotheses.
- One-winner pruning is invalid without an exact conservative exclusion proof.
- Mutating every enumerated support is also invalid.
- Canonical mutation is legal only for one exactly resolved support, an identical
  complete delta under every surviving support, or a native-law-proven common
  update over the full union.
- Otherwise ambiguity is retained as `UnresolvedShadowBranch` with no canonical
  identity, chart or physical extent.
- Immutable `ShadowObservation` records contain no candidate/native/chart key;
  disposable `ShadowHypothesis` records carry alternatives.

## ADR-S426 — One `NativeCloseCommit` owns all canonical feasibility

- Observation support unions, authoritative Merkaba/eigenmode relations and
  intrinsic atlas incidence enter one feasible set before selection.
- `NativeCloseCommit` is the sole semantic mutation operation.
- Projection, scene reduction, close, overflow and commit are physical GPU phases
  or profiler labels, not independent authorities.
- `StitchNative22`, inverse-then-topology and latent-object solving are not semantic
  subsystems.
- Minimum-change acts only inside an already resolved harmless-equivalence fibre;
  it cannot choose physical support identity.

## ADR-S427 — Hidden native modes are preserved by construction

- Linear query families use generated exact right-lifts with
  `R L_R = I` and a frozen `Im L_R ⊕ ker R` decomposition; the kernel
  representative remains byte-identical.
- Nonlinear/bracketed predicates preserve the prior representative on every fibre
  they cannot distinguish.
- This is an explicit descriptor/selector contract, not an assumed theorem of an
  arbitrary distance metric.

## ADR-S428 — ZD, near singular, nonassociation and order remain distinct

- Exact ZD requires exact annihilation. A calibrated nonzero residual is
  `NEAR_SINGULAR`, not exact ZD.
- Every multi-factor relation uses descriptor-owned bracket trees; associator
  magnitude is not a generic edge detector.
- Direct projective order owns front/back; ZD never substitutes for depth.
- Native relation subtype is part of the one field closure. A generation-keyed
  cache may memoize it but owns no topology.

## ADR-S429 — Unresolved, bound and supported stages are proof ordered

- F0 `UnresolvedShadowBranch` is evidence/preimage/disjunction only, with no chart
  or physical identity.
- F1 `BoundNativeBranch` exists only after independent native-relation proof and may
  own a noncanonical local chart.
- F2 supported materialization allocates `Σ₂` only after support/contact and atlas
  attachment/component semantics are proven and both scene shadows forward-verify.
- Observation revision/provenance may order only proven gauge-equivalent placements;
  it cannot create physical placement.
- `LatentGerm`, PENDING and NOVEL are retired physical ontologies.

## ADR-S430 — Refinement follows authoritative modal capacity

- N1R must import/freeze actual TOE eigenmode/eigenspace semantics, query-mode
  coupling/kernel visibility, mode transport and refinement capacity.
- Refinement requires jointly supportable observations, proven current-capacity
  insufficiency, proven finer-capacity sufficiency and complete forward verification.
- It performs intrinsic gauge remap plus full S16/evidence/relation transport.
- Voxel size, depth distance, image resolution and repeat count have no canonical
  refinement authority.

## ADR-S431 — Epistemic state is structured; certificates are nonphysical

- Exact uncertainty consists of preimage regions, hypothesis unions, provenance,
  independence, first-hit roles and unresolved ancestry.
- No scalar confidence/precision drives fusion, relation classification,
  hypothesis choice, chart allocation, refinement or export appearance.
- Complete evidence precedes publication. Deterministic certificates prove state
  and may carry uncertainty metadata.
- Certificates and raw observations cannot supply physical colour/detail missing
  from `Ψ`; first close/refine the field, then export.

## ADR-S432 — Static correction and temporal evolution are separate

- Independent clear-path/pre-hit evidence can contract one static-scene feasible
  field and remove a false reconstruction in S4‑08.
- Behind-hit remains no evidence and a nearer occluder never removes background.
- S4‑09 handles only observations not reconcilable under one admitted static
  scene/epoch model.

## ADR-S433 — Pure readout family

- Sensor prediction and XR eyes use local field projection plus whole-scene
  reduction; prediction retains support alternatives and has no identity authority.
- Export reads full latest `Ψ`, intrinsic atlas incidence and native relation law.
  It derives geometry/connectivity/appearance without a canonical mesh/texture
  world.
- Eye/export/debug caches are disposable; deleting them cannot alter `Ψ`, proof or
  another readout.

## ADR-S434 — Low-code lowering and hard replacement

- Initial production phases are `ProjectNativeShadow`, `ReduceNativeShadow`,
  `CloseNativeField`, sparse `ResolveClosureOverflow`, `PrepareChangedPages`,
  `ScatterChangedStates` and `CloseAndPublishRevision`.
- Only `NativeCloseCommit` is semantic. Physical stages cannot become lifecycle
  objects or managers.
- C# owns lifecycle/resources/residency/fences only; GPU owns shadow/closure/deltas.
- N3R–N7R delete each replaced branch in the same commit.
- Final gates: gross deletion `>=10000`, new production `<=4000`, net `<=-6100`
  versus `cac9ab0`, net `<=-5500` versus `d3b83e1`, zero legacy/fallback.

## ADR-S435 — Deterministic reset and physical closure

- S4‑08.6 runs N0R through N8R exactly as frozen in the active plan; the resume
  file is the sole routine cursor.
- N1R blocks rather than guessing an absent TOE artifact.
- Every accepted run regenerates the code graph and commits separately.
- Only N8R archives/builds/installs and physically accepts the same exact commit,
  with truthful Release kernel times, `NativeCloseCommit <=1500 ms`, wall
  `<=1800 ms`, bounded memory and no revision/segment latency slope.
- S4‑09 remains unopened until that gate passes.

## Explicit supersession

- ADR-S415 through ADR-S422 are superseded. Their unary-E22/germ-first shadow,
  `LatentGerm`, `StitchNative22` and Omega/Xi semantic split must not be implemented.
- ADR-S405 through ADR-S414 remain historical evidence for primitives and failed
  lowerings only.
- Exact NumericDomain/algebra, sparse immutable storage/codec primitives,
  representation-neutral Quest infrastructure, complete evidence and root-last
  publication survive only where v8.1 permits them.
