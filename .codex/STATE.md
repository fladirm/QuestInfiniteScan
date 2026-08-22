# Execution state

Updated: 2026-08-22 (Europe/Prague)

## Source of truth

- `new_spec.md` is the sole canonical Σ-PRISM-16 specification
  (`CPQ4-2026-08-22-S16-v6`).
- `.codex/TASK_DAG.json` is a new independent `S4-00..S4-13` pursuit DAG copied
  directly from section 49 of that specification.
- The old Cone-PRISM goal, DAG and implementation claims are superseded on this
  branch and remain recoverable from Git history.

## Repository safety

- Active branch: `feat/sigma-prism-16-cpq4-20260822`.
- Branch parent: committed Cone-PRISM checkpoint `cabcbc7`.
- Existing untracked archives, device captures and `.source-archives/` are user
  artifacts and must remain untouched/uncommitted.
- Never touch `~/.codex`, capture imagery, device identifiers or generated APKs.

## Current DAG gate

- `S4-00` is accepted in commit `be95c9e`.
- `S4-01` is accepted in commit `5f71653`.
- `S4-02` is accepted in commit `8a04057`.
- `S4-03` is accepted in commit `9a12f7e`.
- `S4-04` is accepted in commit `d5c05b2`.
- `S4-05` is accepted in commit `96cbeca`.
- `S4-06` is accepted in commit `c6dabef`.
- `S4-07` is accepted in commit `dc6abf5`.
- `S4-08` is accepted after the bounded `S4-08.1` repair and its real Vulkan pose
  closure fixtures. The exact committed source is ready for its required Release
  build/install and user device audit. `S4-09` remains paused.
- The retained product surface is only four-stream GPU capture/synchronization,
  immutable calibration/poses, Quest/XR lifecycle, permissions/anchors, input/UI,
  neutral GPU helpers and build/deploy tooling.
- Exact S16 mutation has one CPU semantic domain, generated signed-XOR/operator
  authority and a GPU-resident device self-test gate. The sparse decoded carrier is
  live GPU state with exact disposable dual-eye forward and joint four-stream
  inverse readouts. Intrinsic singular topology is a derived readout only.

## S4-01 accepted implementation

- `SigmaNumericDomain` is checked nearest-even Q16.48 semantic truth with outward
  interval helpers; native execution formats are non-authoritative lowerings.
- The deterministic generator emitted 1,344 sorted zero-divisor pairs, 168 unique
  annihilator actions, `z_null=(-e1-e10)`, geometry rows `{1,2,5,6}`, CPU/HLSL
  signed-XOR descriptors and stable fingerprints.
- Immutable `SigmaS16`, sparse left/right basis and signed-dyad actions, Hadamard
  `B/B^T/G/F`, specialized quaternionic view operator, explicit transition and
  associator plans, projective meet/commit, codec predicates and generation-pair
  transition cache are implemented.
- `SigmaOperatorPlan` preserves bracket trees, performs deterministic CSE and
  lowers from one descriptor DAG. Generic dense multiplication remains explicitly
  named semantic reference/fallback and is absent from sparse dyad/readout paths.
- Packed-32 Vulkan Q16.48 uses checked validity propagation, widened multiplication,
  bounded exact division and outward interval rounding. `SigmaExactBackendGate`
  dispatches a GPU-only exact witness; downstream mutation must bind and test it.
- Verification: generated-output check passed; eight-UAV gate passed; Unity Vulkan
  EditMode passed 21/21; final Android/Vulkan IL2CPP build produced a fresh
  224,368,573-byte APK with no Sigma shader/C# compile error. This is build evidence,
  not a claimed physical headset scan.

## S4-02 accepted implementation

- `SigmaCarrier` is a logically unbounded signed-64 page map whose unallocated
  state is the generated `z_null`; 64x64 decoded pages contain 8x8 codec blocks.
- GPU page state is packed exact Q16.48 and segmented below the runtime Vulkan
  binding range. Immutable write leases publish monotonically numbered generations;
  prior generations remain independently releasable.
- `SigmaCarrierCodec` owns deterministic persistence bytes for
  NULL/CONST/AFFINE/DELTA/RAW. The GPU codec matches its exact CPU oracle, including
  widened DELTA predictor/residual arithmetic and stable LSB-first packing.
- Dirty pages compact stably in ascending physical slot order into indirect work
  without runtime readback. Page/block/segment boundaries remain storage only.
- Verification: Unity Vulkan EditMode passed 26/26; CPU/GPU codec bytes and decoded
  samples match, page/snapshot restart re-encodes byte-identically, the eight-UAV
  gate passes, and Android/Vulkan IL2CPP produced a fresh 224,657,841-byte APK with
  no Sigma compile error. This is build evidence, not a device scan claim.

## S4-03 accepted implementation

- Exact packed Q16.48 `G` plus checked projective division produces disposable
  GPU readout position and information mass; `z_null`, invalid division and
  unsupported transitions produce no contact.
- `SigmaForwardReadout.compute` rebuilds only stably compacted current dirty pages
  through indirect dispatch. Derived 65x65 halos join logical neighbours across
  physical page and segment boundaries without adding canonical samples.
- `SigmaRenderer` consumes coherent `SigmaRigBridge` depth-eye poses and immutable
  calibration epochs, then hardware-rasterizes both views into depth/support,
  exact signed-64 carrier-page limbs, local CarrierUV/normal and immutable
  generation/revision keys.
- Prediction targets and readout vertices are ref-counted disposable GPU caches;
  runtime contains no coefficient readback, CPU pixel loop, Unity Mesh or second
  geometry state.
- Verification: Unity Vulkan EditMode passed 28/28, including exact CPU/GPU
  readout, folded first-hit selection and null no-contact; generated operators and
  eight-UAV checks pass. Android/Vulkan IL2CPP produced a fresh 224,804,460-byte
  APK with zero build errors. This is build evidence, not a physical scan claim.

## S4-04 accepted implementation

- `SigmaInverse.compute` independently constructs conservative finite-footprint
  Q16.48 source cells from both depth eyes against the same immutable prediction,
  then performs exact inclusive componentwise meet without stereo averaging.
- HIT, PRE_HIT_EXCLUSION and NO_CONSTRAINT are explicit first-hit sectors. Empty
  intersections append bounded gap/sector/source provenance and leave the carrier
  byte-unchanged; nothing behind a measured first hit contributes a mutation.
- Accepted updates intersect the projective prior, apply the exact sparse geometry
  transpose correction, retain the stronger information mass and revalidate before
  publishing a new immutable carrier generation. Replaying correlated eye/pose
  keys cannot add count-based hardness.
- Unmatched depth uses image blocks only as bounded GPU scheduling metadata. A
  latent gauge page is published only after independent L/R support promotes at
  least one exact sample; empty speculative pages are aborted. Existing adjacent
  carrier pages are preferred without assigning block or page boundaries topology.
- Frame-critical pixel, interval, meet, proposal, validation and promotion work is
  GPU-only. CPU observes only small asynchronous page/block scheduling flags;
  teardown retains resources until all callbacks complete and never blocks on a
  synchronous readback.
- Verification: generated operator and eight-UAV checks pass; Unity Vulkan
  EditMode passed 34/34 including source-order invariance, exact gap provenance,
  behind-hit no-effect, stronger-prior resistance and independent-support gauge
  promotion. Android/Vulkan IL2CPP produced a fresh 225,040,262-byte APK with
  `Build Finished, Result: Success` and no build errors. This is build evidence,
  not a claimed physical headset scan.

## S4-05 accepted evidence

- `SigmaIntrinsicTopology.compute` implements generation-keyed exact transition
  caching, the complete generated 168-action annihilator catalog, integer
  associator gates, regular/singular/unresolved classification and fail-closed
  disposable readout cuts without topology objects or proximity identity.
- `SigmaTopologyController` validates epoch/frame evidence, supports exact page
  build plus bounded evidence-only revisit accumulation, and publishes topology
  keys before prediction consumes them.
- `SigmaRenderer`/`SigmaPredict` reject stale topology generation/revision and cut
  triangles; topology remains derived disposable state and never mutates the
  Sigma carrier.
- Verification: topology fixtures 4/4, forward-cut fixtures 2/2, full Unity
  Vulkan EditMode 38/38, generated operator check, diff check and eight-UAV
  validator passed. Android build is intentionally deferred to the accepted
  S4-08 consolidated base milestone; no physical Quest scan is claimed here.

## S4-06 accepted evidence

- `RGB_L` and `RGB_R` now enter the same exact projective S16 inverse as both
  depth eyes. Each eye constructs its own outward-rounded finite-footprint cell
  through a generated 27-direction view-operator catalog and the fixed two
  forward plus two reverse bounded interval-contraction schedule.
- The joint proposal intersects the durable directional proof prior and all four
  current source cells before one checked minimum-change lift back into `Psi`.
  Unobservable RGB directions add no bound; incompatible RGB/depth bounds remain
  explicit gap/provenance conflicts; no texture or detached correction state
  exists.
- `SigmaConstraintLedger` deterministically coalesces equal-provenance cells,
  performs reverse-lexicographic redundancy sweeps and stores sparse Q16.48
  `ConstraintCertificate` records. Independence is tracked per constrained S16
  coordinate, so appearance-only evidence cannot manufacture geometry hardness.
- Nonuniform finite footprints, exclusions, conflicts, unobservable contractions
  and certificate overflow retain exact raw source cells plus immutable four-view
  timestamps, pairing uncertainty, calibration epoch, poses, intrinsics and source
  keys for later replay. This ledger is inference proof for the same `Psi`, never
  a physical side world.
- Proof and carrier publication are one validated generation transaction. A
  subsequent proposal reads the selected generation's certificates back into its
  exact prior, preventing weak/far/broad evidence from pulling a narrower supported
  state.
- Verification: generated-operator check, diff check and eight-UAV validator pass;
  Unity 6000.5.9f1 Vulkan EditMode passed 52/52. Direct GPU reducer fixtures prove
  exact minimal depth proof, raw retention for nonuniform cells and per-coordinate
  independent support. Cached reducer dispatches complete in 0.009--0.018 seconds;
  the first desktop fixture invocation includes roughly 40 seconds of Vulkan shader
  compilation and is not a claimed Quest runtime duration. Android/device work is
  intentionally deferred to the accepted S4-08 consolidated milestone.

## S4-07 accepted evidence

- `BuildGaugeDemand` consumes the accepted directional proof of the same `Psi`
  and streams one generated projective coordinate at a time. It requests gauge
  work only for an adjacent carrier triple constrained by two independent source
  keys whose exact midpoint reproduction error exceeds the common admissible
  width.
- `SigmaGaugeRefinement` defines a continuous separable local bijection rather
  than a detail hierarchy: the requested eight-sample band expands by two, the
  retained middle translates, two exact null tail bands compress into one and
  the outer support endpoint remains fixed. The inverse restores every retained
  Q16.48 carrier sample.
- `SigmaGaugeController` transports immutable carrier state, minimal proof
  certificates, retained raw observation footprints and intrinsic topology
  evidence through the same gauge transaction. Unresolved/singular interpolation,
  non-null reservoir state or any proof/topology mismatch aborts publication.
- The gauge map changes only the parameterization of `Psi`; it introduces no
  displacement, texture, chart, mip, topology graph or parallel geometry state.
- Direct Vulkan fixtures prove demand gating, exact carrier/oracle parity, split
  proof-footprint transport and singular-transition transport. The full Unity
  Vulkan EditMode batch passed 61/61; generated operators, diff check and the
  Quest eight-UAV validator passed. Android/device work remains batched with the
  accepted S4-08 base milestone.

## S4-08 accepted evidence

- `SigmaPoseGauge.compute` converts conditioned dual-eye first-hit overlaps into
  conservative six-component twist intervals and intersects them with the bounded
  Meta prior using the accepted packed-Q16.48 interval primitives. Empty,
  insufficient or conflicting evidence returns identity/unresolved; a non-empty
  meet selects the deterministic componentwise minimum-magnitude correction.
- Overlap evaluation is GPU-parallel: many 64-lane workgroups produce compact
  partial meets and one bounded reduction leaves the exact result GPU-resident.
  Depth/RGB association and projection consume it directly in the same command
  graph; CPU observes only the completion fence. This avoids both a long single-
  workgroup scan and a readback-driven scheduler without adding a pose graph.
- Pose acceptance is same-frame. The immutable Meta pose remains the prior and an
  accepted twist focuses that same retained `StereoRigFrame` consistently in the
  GPU depth/RGB inverse projections. It is never carried blindly to the next
  timestamp.
- `SigmaPoseGaugeState` changes only FP readout matrices after the exact integer
  decision. It preserves fixed rig extrinsics and cannot alter carrier bytes,
  intrinsic topology, proof records, calibration epochs or captured timestamps.
- Verification: generated operator check, diff check and eight-UAV validation
  pass; Unity 6000.5.9f1 Vulkan EditMode passed 64/64. The first consolidated
  Android attempt correctly refused publication after the Android compiler found
  a varying-flow group barrier in the S4-05 topology kernel and two contradictory
  fixed-loop attributes in S4-04/S4-06 local-array lowering. Those three exact
  backend-legality roots are corrected without changing canonical semantics, the
  64/64 batch remains green, and build automation now explicitly clears all
  development/debug/profiler flags and selects IL2CPP Release.

## S4-08 Android release correction

- `SigmaRgbSourceCells.compute` assigns one 16-thread workgroup to each scheduled
  RGB source cell and one lane to each generated projective coordinate. Four fixed
  contraction sweeps remain exact; products/divisions are parallel and ordered
  reductions preserve the accepted Q16.48 semantics.
- `SigmaInverse.compute` consumes the two prebuilt RGB cells together with the two
  independent depth cells in one joint proof-gated page solve. Dead proposal
  geometry/mass buffers and their duplicate full solve were removed.
- The live frame path has no asynchronous result callback: compact work, physical
  generation allocation, proposal solve, raw-proof reservation, proof reduction,
  immutable carrier commit and pose consumption remain one GPU command graph.
  CPU owns only resources, immutable frame metadata and a completion fence.
- Commit `91e4721c30c0` built successfully as Android/Vulkan IL2CPP Release with
  zero shader/C# errors. The 66,757,006-byte APK was installed on the connected
  Quest; its source-only archive is
  `SigmaPrism16-S4-08-91e4721c30c0-source.zip`.

## S4-08 GPU-resident live-path repair

- `SigmaInverseWorkGraph.compute` stably compacts active and unmatched work,
  prepares generation-paired scratch transactions, plans raw-proof reservations
  and commits only proof-accepted proposals back into the same `Psi`.
- Exact pose-gauge output is consumed directly by depth/RGB association and
  projection on GPU. `Runtime/SigmaPrism` contains no `AsyncGPUReadback`, runtime
  `GetData` or callback retirement latch.
- Intrinsic topology and disposable readout halos resolve neighbours exclusively
  from exact signed-64 `Sigma_2` logical page addresses. They do not introduce
  image adjacency, XYZ proximity, chart identity or a second geometric world.
- The carrier initializes and allocates exact `z_null` generation pairs on GPU;
  dirty/current/readout publication remains generation-keyed and indirect.
- Verification: Unity Vulkan EditMode passed 64/64, the Quest eight-UAV validator
  passed, and a fresh Android/Vulkan IL2CPP Release built with zero errors and was
  installed on the connected headset. Two release-only HLSL portability defects
  (a duplicate include-owned gate declaration and a struct-valued ternary) were
  removed without changing algebra or topology semantics.

## S4-08.1 exact pose/live-graph repair candidate

- The pose solver now forms point-to-plane residuals and uncertainty intervals in
  exact Q16.48, makes canonical acceptance decisions after quantization, requires
  dual-eye six-axis observability and consumes a conservative tracking-derived
  prior rather than a fixed per-frame box.
- The accepted pose is a true rigid SE(3) readout gauge. It corrects the exact
  depth/RGB calibration, rerasterizes the same retained frame before inverse work
  and is appended GPU-side to immutable raw provenance only when unresolved raw
  evidence is retained.
- Ordinary submitted frames no longer consume monotonically numbered provenance
  records. Live raw reservations are compacted from reusable active-local slots;
  inverse work, proof and carrier publication remain one indirect GPU transaction.
- Intrinsic topology has complete per-kernel bindings, a fail-closed associator,
  bounded fair candidate compaction and evaluates the expensive associator only
  after the complete annihilator scan proves its required near-singular premise.
- Every modified shader was manually read in full with its includes/ABI, all entry
  points, resource declarations, C# binding/dispatch call sites and affected tests
  before this candidate gate.
- Evidence: generated-operator check, `git diff --check` and Quest eight-UAV
  validation pass; Unity 6000.5.9f1 Vulkan EditMode passes 65/65. The new fixture
  executes `BuildPoseGaugePartials`, `ReducePoseGauge` and
  `BuildCorrectedCalibration`, proves a nonzero exact point-to-plane correction,
  preserves the fixed stereo baseline, and the forward fixture proves that a
  nonzero GPU pose result rerasterizes the same retained carrier frame.
- Section 28 and ADR-S410 now state the implemented prior contract exactly: the
  immutable Meta pose is the prior centre; capture-provided numeric covariance is
  used when available, otherwise a conservative deterministic tracking envelope
  is derived from coherent-frame timing/skew uncertainty, observed tracking rates,
  rig residuals and persisted bounds. Missing covariance is never zero uncertainty.
- The first closure Release compile rejected `PlanRawReservations` because an
  early zero-request return enclosed a later group prefix barrier in potentially
  varying flow. The redundant fast return is removed, so all 256 lanes execute the
  same two bounded scans; zero requests naturally publish invalid reservations.
  The free-pair predicate is also initialized explicitly. Unity Vulkan remains
  65/65 after this backend-legality correction.

## S4-00 neutral Quest-shell regression repair

- Device audit proved the fresh-scene automation had stopped calling the retained
  controller/UI setup path. The generated APK therefore had no EventSystem,
  OVRInputModule, PanelInputConfiguration, VRDocumentRaycaster or
  ControllerRayDriver even though the source helpers still existed.
- The clean-scene path now transactionally creates that complete representation-
  neutral XR pointer stack, removes StandaloneInputModule, assigns the main XR
  camera and serializes a dedicated URP controller-ray shader. Build preparation
  fails if any element, the operator UIDocument, its assets or RoomScanInputHandler
  is absent.
- The operator panel now reports the actual asynchronous GPU witness diagnostic,
  resident carrier state, S4-05 intrinsic-topology publication and S4-08 inverse
  counters. This diagnostic mirror never authorizes canonical mutation; kernels
  still consume the GPU gate buffer directly.
- Evidence: Unity Vulkan EditMode passed 64/64; generated-operator and eight-UAV
  checks pass. A fresh generated scene contains the complete EventSystem stack,
  main-camera raycaster, non-null shader GUID and menu/input components, and the
  preparation log has no C#/shader/UI import error.

## Next exact actions

1. Commit the accepted S4-08.1 closure and create its matching source-only
   `git archive`.
2. Build and install that exact Release commit.
3. Keep `S4-09` pending until the user completes the installed release audit.

## Verification policy

Use cheap compile/contracts during S4-00/S4-01. Regenerate the code graph at every
completed node. Android/device runs are batched at the meaningful forward/inverse
vertical milestones and final physical corpus; do not retest known capture plumbing
for every algebra substep.

Every accepted S4 node is committed separately. After accepted S4-08 is committed,
freeze that exact commit as the consolidated base: create its source-only
`git archive` ZIP, build its Android/Vulkan APK, deploy that APK to the connected
Quest, and pause before S4-09 for user audit/device evaluation.
