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
- `S4-04` is accepted by the current isolated checkpoint.
- `S4-05` is accepted in the current checkpoint commit (pending commit creation in this turn).
- `S4-06` is the sole `in_progress` node: joint RGB inverse readout and minimal proof certificates.
- The retained product surface is only four-stream GPU capture/synchronization,
  immutable calibration/poses, Quest/XR lifecycle, permissions/anchors, input/UI,
  neutral GPU helpers and build/deploy tooling.
- Exact S16 mutation has one CPU semantic domain, generated signed-XOR/operator
  authority and a GPU-resident device self-test gate. The sparse decoded carrier is
  live GPU state with exact disposable dual-eye forward and joint inverse depth
  readouts. Intrinsic singular topology begins only in S4-05.

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
  validator passed. Android build is intentionally deferred to S4-07's
  consolidated vertical milestone; no physical Quest scan is claimed here.

## Next exact actions

1. Implement independent RGB_L/R finite-footprint Q16.48 source cells through the
   generated view operator and exact bounded interval contraction.
2. Narrow RGB and depth onto one carrier state; preserve incompatible cells as
   explicit conflicts and emit no bound on unobservable appearance axes.
3. Reduce absorbed evidence to minimal deterministic ConstraintCertificate proof
   sets, retaining raw tiles only while inverse evaluation still needs them.
4. Add calibrated subpixel-view fixtures proving geometry/appearance sharpening
   without sensor summation or a detached texture state, then checkpoint S4-06.

## Verification policy

Use cheap compile/contracts during S4-00/S4-01. Regenerate the code graph at every
completed node. Android/device runs are batched at the meaningful forward/inverse
vertical milestones and final physical corpus; do not retest known capture plumbing
for every algebra substep.

Every accepted S4 node is committed separately. After the S4-07 commit, create a
source-only `git archive` ZIP from that exact commit and pause before S4-08 for user
audit.
