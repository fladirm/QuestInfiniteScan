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
- `S4-02` is accepted for its isolated checkpoint commit.
- `S4-03` is the sole `in_progress` node.
- The retained product surface is only four-stream GPU capture/synchronization,
  immutable calibration/poses, Quest/XR lifecycle, permissions/anchors, input/UI,
  neutral GPU helpers and build/deploy tooling.
- Exact S16 mutation has one CPU semantic domain, generated signed-XOR/operator
  authority and a GPU-resident device self-test gate. The sparse decoded carrier is
  now live GPU state; sensor-driven inverse mutation remains gated until S4-04.

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

## Next exact actions

1. Commit the accepted S4-02 carrier/codec checkpoint with this fresh code graph.
2. Implement `SigmaRigBridge`-driven forward geometry readout over visible resident
   carrier pages into per-eye depth, CarrierUV, support and generation-key targets.
3. Ensure generated `z_null`/unsupported transitions emit no contact and hardware
   rasterization remains the sole first-hit visibility mechanism.
4. Prove deterministic regular/folded/null carrier fixtures, regenerate the graph
   and close S4-03 in its own commit.

## Verification policy

Use cheap compile/contracts during S4-00/S4-01. Regenerate the code graph at every
completed node. Android/device runs are batched at the meaningful forward/inverse
vertical milestones and final physical corpus; do not retest known capture plumbing
for every algebra substep.

Every accepted S4 node is committed separately. After the S4-07 commit, create a
source-only `git archive` ZIP from that exact commit and pause before S4-08 for user
audit.
