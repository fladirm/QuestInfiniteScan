# Sigma-PRISM-16 implementation state

Updated: 2026-08-24 (Europe/Prague)

## Authority and active work

- Canonical product: `new_spec.md`, `CPQ4-2026-08-22-S16-v6`.
- Branch: `feat/sigma-prism-16-cpq4-20260822`.
- Accepted predecessors: S4-00 through S4-07.
- Active node: S4-08, current-device forensic closure after the
  **S4-08.3D transition/residency** implementation.
  S4-09 is pending and unopened.
- Forensic closure: `s4-083-audit.md`.
- Frozen deterministic implementation contract: `.codex/S4-08.3D_PLAN.md`.
- Current audit execution checkpoint: `.codex/S4-08.3_PREVIEW_AUDIT.md`.

## Frozen ontology

- `Psi : Sigma_2 -> S16` is the only canonical physical state.
- Exact Q16.48 values, validity, gaps, source provenance, minimal proof and
  transition signatures are invariant under partitioning/scheduling.
- Bundle/page/block/microtile/candidate limits are execution/storage bounds, never
  physical or canonical evidence limits.
- Derived topology, direct XR carrier preview and later meshlets are disposable
  readouts and never authorize mutation.
- GPU owns inverse/proof/transition/publication. Existing async telemetry remains
  diagnostic-only and cannot control scheduling or canonical state.

## Preserved completed implementation

- S4-08.2 exact pose gauge, corrected calibration/reraster, rigid GPU SE(3), pose
  provenance, reusable raw records and fail-closed graphics completion.
- S4-08.3 persistent sealed source/transaction/proof/transition/dormant/publication
  arenas, deterministic generated token costs and indirect worklists.
- Coordinate-major five-stage inverse, proof fixed point with lossless windows,
  generation-sealed historical revalidation and revision-manifest publication.
- Separate nonblocking ingress/canonical/derived submissions and temporary stereo
  carrier readout without a second world.
- Previous gates: focused Vulkan streaming 4/4, full EditMode 69/69, generated
  outputs current and Quest UAV limit 8.

## Closed device forensic result

The installed S4-08.3 Release received coherent frames but produced an empty scan.
The complete report and evidence are in `s4-083-audit.md`. Confirmed contract breaks:

1. all three streaming inverse kernels missed the complete constraint-ledger bind;
2. schedule diagnostics missed `_StreamProbation`, and bundle extraction silently
   missed the two pose-consume matrices;
3. the split inverse had no transaction-generation/current-work phase completion,
   so skipped/faulted work could advance through stale scratch;
4. zero accepted evidence could close proof/transition and become PUBLISHABLE;
5. publication inferred page count by scanning zero-filled unused references,
   aliasing page slot zero despite the existing explicit `publication.z` count;
6. ingress discarded candidates beyond an artificial two-bundle cap;
7. one dependent opcode per host submission caused about 36.6 seconds to first
   publication; the first visible readout appeared only with the next publication.

The audit did not find a second world or a failure of the 16D ontology. The defect
class is execution ownership, binding, fail-closed state progression and scheduling
granularity.

## S4-08.3C minimal closure

- Add one transient 16-byte `execution` field to the existing transaction: exact
  owner tuple, five phase-completion bits, accumulated existing proposal/outcome
  bits and one execution-fault bit.
- Reuse the existing ledger binder, outcome classes, transaction states,
  `publication.z`, revision manifest, resident bundle arena and async telemetry.
- Require generation+owner+phase completion at every inverse stage; only successful
  Final advances. Faults fail closed and zero accepted evidence becomes dormant.
- New UNKNOWN pages require accepted `NULL_PROMOTION`; existing pages require
  accepted `EXISTING_UPDATE`; proof-only accepted updates need not set CHANGED.
- Record eight fixed canonical rounds over one budget refill. Only the last round
  emits publication so its worklist remains the existing CB3 derived handoff.
- Remove only the artificial ingress cap; report resident exhaustion explicitly.
- Extend the current ~3 KiB/1 Hz diagnostic readback with transaction owner/phase,
  exact outcomes, counters and generated-cost load. It remains observational.

## Implemented S4-08.3C candidate

- Generated transaction ABI is 368 bytes and owns the exact source/block/microtile
  execution tuple, issued/five-phase completion and accumulated proposal/outcome/
  fault bits; existing canonical records are unchanged.
- Production graph binds the full constraint ledger, probation and both pose-consume
  matrices. Only a current fully completed Final advances transaction progress.
- Revalidation now rejects execution failure, zero accepted evidence and an
  outcome incompatible with novel/existing page identity before PUBLISHABLE.
- Publication consumes only explicit `publication.z`; unused page handles are
  invalid and visible/readout-dirty caches resolve in the same publication list.
- One canonical submission records eight dependency rounds over one refill and
  publishes only in the final round; host recording allocates no per-round arrays.
- Ingress seals every candidate fitting the resident arena and reports exhaustion.
- Existing telemetry decodes execution/page ownership and generated last-round
  load. Work-graph counters stage through its existing scheduler UAV so every
  Quest kernel remains at or below eight UAVs.

## S4-08.3D device root cause and frozen repair

- The installed `3430929` graph now reaches exact inverse and closes 1024/1024
  proof blocks, but all 16 bootstrap pages fail candidate transition closure:
  131072 directed edges run the complete 168-action catalog (22020096 witnesses)
  with no accepted transition and no publication.
- Missing/unpublished neighbours are currently materialized as physical `z_null`.
  Contact-side eye provenance is OR-ed across the edge and can falsely support a
  contact/null discontinuity although the null endpoint was never observed.
- Candidate scope is page-local, so storage edges manufacture the same false null
  transition inside one measured footprint. Generic inverse correctly does not
  manufacture a singular perimeter at the edge of current evidence.
- Failed candidates park whole bundle-owned raw/calibration ranges. At 64/64
  resident bundles, new evidence receives a fresh Morton allocation address,
  cannot join the dormant probation and ingress advances despite zero admission.
- `.codex/S4-08.3D_PLAN.md` freezes the low-code repair: evidence-qualified
  endpoint/no-claim semantics; Morton only as allocator; exact probation proposal;
  transfer only unresolved residue into the existing ConstraintLedger; reclaim
  execution residency only after successful ownership transfer; never advance
  unowned ingress.

## Current deployed-device forensic closure

- Audited/deployed commit is `c2720b51637643bb04eef16894d7dd9bf9702720`.
- The 320 x 320 capture grid deterministically yields 10 x 10 ingress blocks and
  therefore 5 x 5 gauge pages. Device screenshots show exactly this 25-page board.
- After page 25 the runtime allocates fresh Morton logical coordinates at the same
  image origins. At publication 27 the draw plan contains exactly 27 current pages,
  proving that the second pass overlays new canonical pages rather than advancing
  the first generation or merely redrawing it.
- The 64 resident bundle slots fill in roughly three unmatched frames. Candidates
  that do not obtain a slot are counted as skipped but are not immutably sealed
  before the capture frame is released, so later side/rear observations never reach
  canonical processing.
- Durable raw retention has a deterministic session ceiling: transient tiles own
  0..4095 and the monotonic proof allocator consumes 4096..8191. After exactly 4096
  closed proof blocks the next `EMIT_RAW` allocation fails, the proof owner repeats
  forever and publication stops. Device telemetry freezes at
  `diag.proof=[4097,4096,0,0]`.
- The 12-candidate spill flag has no production continuation reader. It did not
  cause this captured stall but remains a separate losslessness violation.
- The renderer draws every manifest-current page and the prediction graph includes
  contact footprints. The tilted 5 x 5 board is therefore canonical acquisition and
  identity failure, not a hidden completed room or a triangle-only preview defect.
- Current Release kernel timing is unavailable: profiler recorders returned zero
  blocks. KGSL was roughly 91 percent busy at 456 MHz, so no budget increase is
  authorized without the next diagnostic build.
- Complete causal proof, file/line evidence and the minimal ontology-preserving
  repair contract are in `s4-083-audit.md`; execution notes are in
  `.codex/S4-08.3_PREVIEW_AUDIT.md`.
- The diagnostic-only telemetry follow-up compiles and the complete Unity Vulkan
  EditMode suite passes 73/73; generated operators, diff, code graph/control and
  Quest eight-UAV gates are green.

## Exact next action

Commit the audit plus diagnostic-only profiler visibility follow-up, regenerate and
validate controls, create an exact source-only archive and forensic evidence bundle,
and push that checkpoint to `origin/prism`. Do not modify reconstruction runtime or
open S4-09 until this evidence checkpoint is delivered and the next repair is
explicitly started.

## Acceptance still required

- Lossless ingress ownership, stable pending-or-published association, reclaimable
  segmented raw retention and proof continuation beyond bounded execution windows.
- Page-layout and scheduling partition invariance, including no duplicate logical
  gauge allocation for the same compatible sealed footprint.
- Generated outputs, code graph, control validation, Quest UAV gate, full Vulkan
  EditMode and clean Android/Vulkan Release.
- Fresh Quest install: first accepted publication/readout within five seconds,
  non-zero support/information/draw, no missing binding/fault/exhaustion and three
  minutes continuous scan.
- Commit, source-only `git archive` and APK must be the same accepted revision;
  then stop before S4-09.
