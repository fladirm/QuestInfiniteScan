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
- `S4-01` is accepted for its isolated checkpoint commit.
- `S4-02` is the sole `in_progress` node.
- The retained product surface is only four-stream GPU capture/synchronization,
  immutable calibration/poses, Quest/XR lifecycle, permissions/anchors, input/UI,
  neutral GPU helpers and build/deploy tooling.
- Exact S16 mutation now has one CPU semantic domain, generated signed-XOR/operator
  authority and a GPU-resident device self-test gate. No carrier allocation or live
  state mutation exists until S4-02 supplies the sparse exact carrier.

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

## Next exact actions

1. Commit the accepted S4-01 exact operator checkpoint with this fresh code graph.
2. Implement the logically unbounded signed-64 two-dimensional `SigmaCarrier` with
   implicit `z_null`, 64x64 pages, 8x8 blocks and immutable generations.
3. Implement deterministic lossless NULL/CONST/AFFINE/DELTA/RAW physical codecs,
   segmented pools, certificate ranges and GPU dirty compaction.
4. Prove byte-identical encode/decode/re-encode and restart fixtures, regenerate the
   graph, then close S4-02 in its own commit.

## Verification policy

Use cheap compile/contracts during S4-00/S4-01. Regenerate the code graph at every
completed node. Android/device runs are batched at the meaningful forward/inverse
vertical milestones and final physical corpus; do not retest known capture plumbing
for every algebra substep.

Every accepted S4 node is committed separately. After the S4-07 commit, create a
source-only `git archive` ZIP from that exact commit and pause before S4-08 for user
audit.
