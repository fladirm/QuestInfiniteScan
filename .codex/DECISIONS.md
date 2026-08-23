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

## ADR-S407 — Exact intrinsic topology is derived readout, not a second world

- Decision: S4-05 derives transition classes, annihilator evidence and
  singular/unresolved cuts from generation-keyed Sigma carrier readout. It does
  not introduce ContactFilm, BoundaryCurve, manifold, proximity or other
  topology state, and it never mutates the canonical carrier.
- Decision: changed pages use the full exact topology build; unchanged active
  pages use only bounded epoch/frame-validated evidence accumulation. Published
  topology keys are generation/revision keyed and stale or cut readout is
  rejected by prediction.
- Consequence: S4-06 RGB inverse evidence must narrow the same carrier and emit
  proof certificates/conflicts; it may not create a detached appearance or
  geometry correction world.

## ADR-S408 — RGB-D proof narrows the same hyperdimensional carrier

- Decision: both RGB eyes and both depth eyes remain four independent source cells
  until exact admissible-set intersection against one selected `Psi` generation.
  RGB can alter geometry-relevant S16 directions only where the generated view
  operator actually makes them observable; there is no texture-world or detached
  photometric correction.
- Decision: sparse `ConstraintCertificate` records and unresolved raw tiles are
  directional inference proof for `Psi`, not physical state. Certificates are
  reapplied to the exact prior of later inverse evaluations; raw tiles retain the
  original timestamps, pairing uncertainty, pose/calibration gauge, footprint and
  source cells required for deterministic replay.
- Decision: independent support is evaluated per S16 operator coordinate. Two
  distinct sources may harden only the directions both actually constrain; an
  appearance-only constraint cannot make an unrelated geometry direction resistive.
- Consequence: S4-07 may change only carrier gauge sampling through a bijection. It
  must transport/revalidate these retained constraints over the same `Psi` and may
  not reinterpret the proof ledger as geometry, topology, detail or appearance.

## ADR-S409 — Supported detail is a local carrier gauge bijection

- Decision: gauge demand is derived only from independently accepted proof cells
  whose exact common admissible width cannot reproduce local projective variation;
  image tiles, pages and repeated correlated views never demand detail by count.
- Decision: the accepted local gauge is a separable continuous bijection of the
  same carrier. It expands one requested eight-sample band, translates retained
  support and draws capacity only from two exact implicit-null tail bands while
  fixing the outer support endpoint.
- Decision: carrier samples, proof footprints, retained raw observations and
  intrinsic transition evidence are transported and revalidated in one immutable
  generation transaction. Singular or unresolved interpolation fails closed.
- Consequence: supported fine geometry and appearance remain literal variation of
  `Psi`; no displacement, chart, mip hierarchy or secondary detail ontology is
  permitted. S4-08 changes only the observation pose gauge and must reuse this
  exact-cell infrastructure rather than introduce a second SLAM.

## ADR-S410 — Pose correction is an exact same-frame readout gauge

- Decision: conditioned dual-eye overlaps emit independent conservative twist
  intervals which meet the bounded Meta-pose tracking prior in exact Q16.48. The
  immutable Meta pose is its centre. Numeric tracking covariance is projected when
  exposed by the capture API; otherwise a deterministic envelope is derived from
  coherent-frame timing/skew uncertainty, observed tracking rates, fixed-rig
  residuals and persisted calibration bounds. Missing covariance never means zero
  uncertainty. A non-empty meet selects the componentwise minimum-magnitude point;
  conflict or insufficient independent support retains the immutable Meta pose.
- Decision: an accepted nonzero twist rerasterizes the same retained
  `StereoRigFrame` before any carrier inverse work. Depth and RGB calibration then
  consume that corrected prediction/gauge snapshot together; a correction from
  frame N is never blindly carried into frame N+1.
- Decision: overlap work is distributed into GPU partial meets and one fixed
  reduction. The exact result remains GPU-resident and is consumed directly by
  same-frame depth/RGB association and projection; CPU sees only a fence. It is a
  readout gauge, not carrier state, and cannot rewrite `Psi`, intrinsic topology,
  timestamps or rig extrinsics.
- Consequence: S4-09 receives one unchanged canonical carrier plus a bounded
  observation gauge, not a pose graph, second SLAM or historical geometry rewrite.

## ADR-S411 — Live inverse completion is one GPU-resident Psi transaction

- Decision: active/unmatched work compaction, generation-paired scratch allocation,
  inverse-cell solve, raw-proof reservation, exact proof reduction and immutable
  publication execute as one indirect GPU command graph. CPU owns lifecycle,
  immutable frame metadata and a completion fence only.
- Decision: topology/readout execution adjacency is derived solely from exact
  signed-64 `Sigma_2` logical page addresses. Image neighbourhoods, Euclidean 3D
  proximity and storage-segment boundaries have no identity or physical meaning.
- Consequence: no callback, CPU scheduler, chart/mesh topology or parallel world is
  permitted to decide canonical mutation; singular structure remains an exact
  annihilator/associator readout of the same `Psi`.

## ADR-S412 — Canonical transaction is independent of GPU scheduling time

- Decision: ADR-S411's one proof-closed `Psi` transaction remains the semantic
  unit, but it is no longer required to execute as one page-sized command-buffer
  quantum. A transaction may persist across XR frames in owned GPU scratch while
  deterministic 16-sample execution microtiles complete 64-sample proof blocks.
  Partitioning, token budget, fair interleaving and pause/resume must reproduce the
  one-shot semantic oracle bit-for-bit, including validity, conflicts, provenance,
  minimal certificates and candidate transition signatures.
- Decision: one bounded generation-safe GPU transaction arena and one generated
  cost-token scheduler own live progress. The CPU records fixed indirect ingress,
  canonical and derived submissions and polls completion only for resource
  lifetime. No CPU work selection, timing readback, callback mutation authority or
  hardware-async requirement is introduced. Admission scans a fixed source-header
  ring before the eight active records; transactions with intersecting exact
  logical footprint masks are dependency-chained and published in checked ticket
  order, while disjoint work may interleave.
- Decision: an ingress-surviving source bundle owns compact immutable sensor and
  prediction payload plus provenance. It never retains a capture/prediction ring
  lease. Its source set is sealed before exact evaluation; certificate
  minimization occurs only after sealed proof closure in section-30 order. A
  transient section-18 probation record may collect multiple immutable bundle
  handles in source order, but it cannot begin mutation or publish geometry until
  the independent-support rule seals that exact handle set.
- Decision: novel association handles same-carrier, mixed-eye, no-prediction and
  incompatible-carrier cases through exact admissible-cell and first-hit rules.
  Prediction proposes identity but is not an existence condition. Empty exact
  evidence is retained with a dependency fingerprint and is not retried until a
  relevant `Psi`, pose, calibration, support or independence generation changes.
  Dependency hashes are lookup accelerators only; exact decisions compare the full
  generation tuple. Before publication, changed first-hit dependencies rerasterize
  manifest-resolved `Psi` from the retained observation pose and repeat exact gates.
- Decision: canonical candidate-transition validation evaluates candidate S16,
  exact annihilator/associator plans and retained evidence directly before commit.
  The generation-keyed topology cache remains derived readout only. A
  generation-safe publication manifest exposes every page generation belonging to
  one affected carrier footprint with one all-or-none visibility marker. New pages
  name a `bornManifest`; replaced pages name the same `retiredByManifest`; the
  shared visibility rule changes both sides only when that manifest atomically
  becomes published. Per-page current flags are derived caches and page boundaries
  retain zero physical meaning.
- Decision: scheduler costs combine generated operator-plan counts with a fixed
  kernel execution manifest covering workgroup shape, memory traffic, barriers,
  scratch and witness work. Static device token profiles bound submissions;
  Section-44 telemetry validates the profile but never changes canonical physics.
- Decision: resident bundle headers, source scratch and twelve-candidate proof
  arrays are execution windows, never canonical limits. A transaction owns a
  generation-safe segmented stream of its complete sealed evidence. Exhausted
  windows pause or losslessly spill; only after source closure may stable
  lexicographic coalescing and reverse-order redundancy run to a fixed point.
  Bundle partitioning, token budget and scratch capacity cannot change Psi, proof
  bytes, validity or provenance.
- Consequence: sensor ingress is decoupled from inverse latency, exact evidence is
  never blurred or made scheduling-dependent, and the renderer sees only committed
  manifest-visible `Psi`. S4-10 supplies durable lossless overflow and S4-11 later
  replaces the temporary direct carrier readout without changing this contract.
