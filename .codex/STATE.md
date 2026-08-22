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

- `S4-00` is accepted and committed next as an isolated clean-shell checkpoint.
- `S4-01` is the sole `in_progress` node.
- The retained product surface is only four-stream GPU capture/synchronization,
  immutable calibration/poses, Quest/XR lifecycle, permissions/anchors, input/UI,
  neutral GPU helpers and build/deploy tooling.
- No S16 live state mutation is accepted until the S4-01 exact arithmetic,
  generated-algebra and backend-parity gates pass.

## Next exact actions

1. Commit the accepted S4-00 clean-shell checkpoint with its generated code graph.
2. Implement one checked nearest-even Q16.48 semantic domain with outward intervals.
3. Add the deterministic Cayley-Dickson/operator generator and generated C#/HLSL
   descriptors from one authority.
4. Add bit-exact algebra/operator fixtures, including backend capability fail-closed
   contracts, then close S4-01 in its own commit.

## Verification policy

Use cheap compile/contracts during S4-00/S4-01. Regenerate the code graph at every
completed node. Android/device runs are batched at the meaningful forward/inverse
vertical milestones and final physical corpus; do not retest known capture plumbing
for every algebra substep.

Every accepted S4 node is committed separately. After the S4-07 commit, create a
source-only `git archive` ZIP from that exact commit and pause before S4-08 for user
audit.
