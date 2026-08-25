# Active architecture decisions

`new_spec.md` is the detailed authority. This file records only decisions needed to
resume active implementation. Superseded v6/v7 execution decisions and their
evidence remain in Git history; they are intentionally not repeated here.

## ADR-S400 — Clean branch and one reconstruction product

- The previous mapper remains recoverable from Git and is not an implementation
  donor.
- Active reconstruction lives only under `Runtime/SigmaPrism` and
  `Runtime/Resources/SigmaPrism`.
- Representation-neutral capture, calibration, XR lifecycle, UI, anchors, fences,
  indirect helpers and build/deploy tooling may remain.
- No compatibility reconstruction, alternate world or fallback path may coexist
  with the active implementation.

## ADR-S401 — Full native S16 is the only physical world

- The only canonical reconstructed reality is
  `Psi : Sigma_2 -> S16` in checked nearest-even Q16.48.
- One carrier germ is a full native S16 state, not an `xyz/rgb/normal/...` channel
  tuple and not an encoded 3D point.
- Geometry, appearance, first-hit order, detail, topology, scene evolution and all
  exported/displayed forms are constraints on or readouts of this same state.
- Pages, blocks, segments, image pixels, workgroups and readout coordinates have no
  physical identity.

## ADR-S403 — Exact algebra and expression trees are semantic authority

- `SigmaNumericDomain`, the generated signed-XOR Cayley-Dickson algebra and explicit
  bracket trees define canonical arithmetic.
- Q16.48 point operations round nearest-even; interval operations round outward and
  checked overflow fails closed.
- GPU schedules may lower an expression DAG but may not reassociate it. FP cannot
  decide canonical mutation.
- Packed-32 or native-I64 execution is legal only behind exact bit-parity gates.

## ADR-S404 — Sparse immutable carrier and lossless persistence

- The signed-64 two-dimensional carrier is logically unbounded; unallocated state
  is implicit generated native null.
- Published page generations are immutable. NULL/CONST/AFFINE/DELTA/RAW codecs are
  deterministic lossless encodings, not alternative physical states.
- Residency and binding segmentation may change cost only. Durable publication
  precedes eviction and restart reproduces canonical bytes and operator
  fingerprints.

## ADR-S415 — One frozen native-relation descriptor owns all physics

- Baseline `CPQ4-2026-08-25-S16-v8` replaces the v7 scanner execution ontology.
- A supplied authoritative TOE artifact must be frozen as one generated
  `NativeRelationDescriptor`. If its exact 22-relation factorization is proven, the
  resulting `E22` atlas is an overcomplete image of one S16 state, not 22 degrees of
  freedom and not a second world.
- The descriptor owns relation expressions, signed-XOR routing, constants, query
  rows, zero-divisor families and every bracket tree.
- Exactly four evaluators are generated from the same descriptor and fingerprint:
  `ForwardNative22`, `PullbackNative22`, `StitchNative22` and
  `Native22ReferenceOracle`.
- The project must not infer or invent missing TOE relations from current v7
  geometry/RGB/topology code.

## ADR-S416 — Scan is exact inverse-image pullback of the forward manifestation

- Physical causality is `S16 -> native relations -> manifested/query shadow`.
  Scanning evaluates the exact inverse image of a coherent measured RGB-D shadow
  through that same expression DAG; it is not a numerical matrix inverse and not
  `RGB-D -> XYZ -> S16`.
- Left/right RGB and depth evidence remain independent until componentwise exact
  conjunction in native relation space. A source contractor may not be pre-narrowed
  by another source or by the prior without a generated equivalence proof.
- The admissible result is a native S16 fibre/region, not necessarily an axis-aligned
  16-lane box. Minimum-change selection is S16-native and fingerprinted.
- A partial shadow may constrain only observable native directions. It cannot erase
  a prior distinction in its unobserved fibre; every selected state is forward
  verified against all source shadows.
- First-hit/pre-hit exclusion/behind-hit no-claim are persistent exact relation
  semantics, never a hardcoded depth sector.

## ADR-S417 — Topology is the same native relation stitch

- For intrinsic neighbours, the exact transition is
  `tau_ij = conjugate(s_i) * s_j` with descriptor-owned bracketing.
- Regular, singular, no-relation and unresolved are outcomes of compatibility of
  the same native-relation manifestations under transition transport.
- Zero-divisor annihilation and non-zero associators are native strata inside that
  stitch, not a separate topology pipeline.
- Dirty work is keyed by intrinsic endpoint generations or changed relation
  evidence. Optical/XYZ proximity may propose bounded work only and can never claim
  contact or identity.
- A disposable cache is keyed by complete endpoint-generation/evidence identity;
  stable singular classification requires the specified independent-view proof.

## ADR-S418 — Association, latent state and refinement are native

- Every admissible supported or latent fibre that can explain a measured shadow is
  evaluated before new identity is admitted. Candidate pruning needs an exact
  coverage proof.
- `CURRENT`, `PENDING`, `CONTINUATION` and `NOVEL` are not physical classes in v8;
  pixel winner selection cannot mint or discard identity.
- Unresolved evidence is a `LatentGerm`: native admissible region, complete evidence
  references and a stable local relation gauge. It is neither a pixel chart nor a
  second pending world.
- Exact stitch closure absorbs a latent germ into an existing branch or promotes it
  into deterministic collision-free carrier gauge. Failure or uncertainty makes no
  canonical claim.
- When one germ cannot represent independently supported variation, exact bijective
  gauge refinement adds finer S16 germs and transports retained evidence. It never
  creates a detail mesh or texture world.

## ADR-S419 — Complete evidence precedes sparse root-last publication

- Only `GermDelta` records can mutate canonical state.
- All deltas targeting the same complete native germ key reduce deterministically
  before one final selection and revalidation. Execution order never selects a
  writer.
- A visible revision owns complete independent source evidence or a proven exact
  certificate/raw handoff before publication. Reduced joint state is only a
  witness/cache, never a substitute for the source journal.
- Touched pages receive unpublished shadow generations; validation closes one
  revision and a single root exchange is the final visible operation.
- Proof minimization and evidence reclamation may run later, but cannot weaken or
  outlive their ownership contract.

## ADR-S420 — Multiple lower-dimensional readouts are pure consumers

- Direct stereo eye, scanner prediction, textured 3D export and debug/analytics are
  independent query descriptors over full latest `Psi`.
- Eye output may be deliberately cheap and lossy; discarded directions remain in
  `Psi` for later views and rich export.
- Prediction proposes native fibres and direct order but has no allocation or
  identity authority.
- Export derives geometry, intrinsic connectivity and multi-view appearance from
  full `Psi`; it never consumes an old preview cache as canonical truth.
- Readout caches, eye maps, meshes and textures are disposable. Deleting them cannot
  change replay or carrier bytes.

## ADR-S421 — Two semantic solves, bounded GPU lowering and hard replacement

- Live reconstruction has only two semantic solves:
  `Omega = INVERSE_NATIVE_22` and `Xi = STITCH_COMMIT_NATIVE_22`. Readout is a pure
  evaluation, not a third reconstruction world.
- The initial physical lowering is a small fixed set of compact/indirect kernels.
  Relation expressions and common subexpressions are fused by the generator;
  dispatch stages are not semantic lifecycle objects.
- C# owns capture admission, immutable resources, residency, fences, publication
  lifecycle and errors only. GPU owns association, pullback, stitch, mutation and
  readout.
- Canonical results must be invariant under workgroup, segment, page, relation and
  dispatch decomposition. Illegal dispatch/binding dimensions fail before command
  execution.
- S4-08.6 is a replacement run: each N3-N7 cut deletes the branch it supersedes.
  Final gates are gross deletion `>=10000` production LOC, new production
  `<=4000`, net `<=-6100` versus `cac9ab0`, net `<=-5500` versus `d3b83e1`, and no
  legacy/fallback symbols.

## ADR-S422 — Deterministic closure and physical acceptance

- S4-08.6 runs strictly N0 through N8 as frozen in
  `.codex/S4-08.6_NATIVE_CLOSURE_PLAN.md`; `.codex/S4-08.6_RESUME.md` is its sole
  routine resume cursor.
- N1 freezes the authoritative TOE descriptor before runtime work. No live cutover
  may begin from guessed relations.
- Every accepted node regenerates the code graph and is committed separately.
- Only N8 may close S4-08: archive the exact source commit, build/install that same
  Android/Vulkan Release and pass the physical scan/readout corpus with truthful
  kernel timing, `Omega+Xi <=1500 ms`, wall `<=1800 ms`, bounded memory and no
  revision/segment latency slope.
- S4-09 remains unopened until this same-commit device gate is accepted.

## Explicit supersession

- ADR-S405 through ADR-S414 remain historical evidence for accepted primitives and
  failed lowerings only.
- Their sensor-specific cell worlds, proposal-kind identity, provider-by-segment
  evaluation, optical edge universe, pixel pending chart, global novel bbox,
  page-halo continuity and duplicated publication graph are superseded and must be
  deleted, not renamed.
- Root-last immutable publication, exact carrier/codec, complete evidence,
  generated algebra and representation-neutral Quest infrastructure survive where
  they satisfy v8.
