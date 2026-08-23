# Sigma-PRISM-16 implementation state

Updated: 2026-08-23 (Europe/Prague)

## Authority and active work

- Canonical product: `new_spec.md`, `CPQ4-2026-08-22-S16-v6`.
- Branch: `feat/sigma-prism-16-cpq4-20260822`.
- Accepted predecessors: S4-00 through S4-07.
- Active node: S4-08, repair run **S4-08.3D transition/residency closure**.
  S4-09 is pending and unopened.
- Forensic closure: `s4-083-audit.md`.
- Frozen deterministic implementation contract: `.codex/S4-08.3D_PLAN.md`.
- Sole routine resume checkpoint: `.codex/S4-08.3_RESUME.md`.

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

## Exact next action

The post-D device run exposed two remaining wiring errors, now repaired without a
new subsystem: streaming `CHANGED` is the bit-exact final candidate/prior delta;
source conflict/invalid flags remain retained evidence but no longer invalidate a
valid fallback endpoint; and valid unresolved transitions close conservatively as
required by section 22 while derived readout keeps cutting them. Historical
revalidation now deterministically revokes association caches owned by non-active
bundles before allowing them to block a live canonical transaction. Null promotion,
proof, manifest publication and derived readout contracts are unchanged.

The final device audit then isolated the remaining canonical bootstrap defect in the
lift itself: constrained gauge coordinates were clamped from zero and every accepted
candidate used the minimum 1/64 information mass, while the inverse/Hadamard round
trip was required to reproduce that mass within one LSB. The repaired production
kernel now centres constrained coordinates in the complete final exact meet, derives
mass from the maximum independently supported Q48 width, requires independent dual-
eye geometry for null promotion and applies the exact post-quantization residual
through state lane zero (the generated Hadamard column zero is uniformly +1). No
buffer, scheduler, topology state or second world was added; the independence mask
reuses the unused high half of transaction-owned sample metadata.

The actual production `EvaluateTransactionMicrotile` Vulkan fixture proves four
width-dependent masses, exact joint-cell containment, bit-identical source-order
permutation and one-eye fail-closed promotion. Full Unity Vulkan EditMode passes
72/72; generated output, `git diff --check` and the Quest eight-UAV gate pass.

The temporary pre-S4-11 human readout now explicitly maps capture tracking space
through Unity XR `TrackingSpace`, emits conservative adjacent-sample triangles,
shows their barycentric edges in Wireframe mode and retains point fallback only for
isolated support. It is one disposable readout of existing Psi and does not alter
prediction, topology or canonical state. Vulkan EditMode remains 72/72 with no new
shader diagnostic. Exact next action: commit this visualization closure, build the
same revision as Android/Vulkan Release and install it for world-lock inspection.
Do not open S4-09.

## Acceptance still required

- Physical no-claim/page-invariance, dormant replay/reclaim, publication/readout and
  ingress-ownership evidence on the installed Release.
- Generated outputs, code graph, control validation, Quest UAV gate, full Vulkan
  EditMode and clean Android/Vulkan Release.
- Fresh Quest install: first accepted publication/readout within five seconds,
  non-zero support/information/draw, no missing binding/fault/exhaustion and three
  minutes continuous scan.
- Commit, source-only `git archive` and APK must be the same accepted revision;
  then stop before S4-09.
