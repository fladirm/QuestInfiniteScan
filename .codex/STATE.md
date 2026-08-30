# Sigma‑PRISM‑16 implementation state

Updated: 2026-08-30 (Europe/Prague)

## Authority

- Canonical spec: `new_spec.md`, `CPQ4-2026-08-25-S16-v8.3`.
- Active forensic branch: `forensic/n4r-cut-e-scheduler`.
- Runtime replacement baseline: `cac9ab012f4ce574e5eb9bee88290982fd9c4fe8`.
- Accepted primitive milestones: S4‑00 through S4‑07 where compatible with v8.3.
- Active node: S4‑08. Corrective N1R-7 closes abstract native stitching and the
  complete 24-assignment representation-only `Z² semidirect D4` chart theorem.
  Corrective N2R-7 now proves the same set operation on CPU and Vulkan with
  Runtime/Resources `+0/-0`; N3R remains the accepted live bootstrap, N4R is the
  sole active implementation cut, and S4-09 remains unopened.
- Active repair: S4‑08.6 one-medium native closure.
- Frozen plan: `.codex/S4-08.6_NATIVE_CLOSURE_PLAN.md`.
- Sole routine cursor: `.codex/S4-08.6_RESUME.md`.
- Forensic facts and replacement matrix: `analyza.md`.
- S4‑09 remains pending/unopened.

## Active N4.1R Quest-first GPU cardinality closure

N4.1R is now the sole execution cut inside in-progress N4R. It does not reopen
N1R/N2R/N3R, change A--D semantics or open N5R. The current Quest baseline is
`n4r_fault_fix_retry_20260830_032312`:

```text
timestamped compute                            500.6230 ms
ContractNativeQuery x2                         233.1091 ms
PrepareNativeComponentOrder                     73.4488 ms
BuildNativeObservation                          66.4428 ms
canonical Seed/Runs/Select                      109.6929 ms
native graph                                    exactly 16 submissions
```

The active correction is frozen as whole-kernel replacement, not line-by-line
post-build repair:

```text
compile small finite Q48/D4 algebra in the generator
rewrite observation/FOOTPRINT around measured Quest team/LDS occupancy
replace TILE_CLOSE barrier relaxation with one transient forest + pointer doubling
replace capacity canonical/refinement walks with realized compact streams
retain exact N1/N2 bytes, D4 witness order and root-last publication
```

`Tools/sigma/quest_shader_contract.json` records the measured Quest 3 / Adreno 740
limits and the exact phase budgets. `validate_sigma_quest_compute.py` checks the
16-stage graph, production kernel set, <=256 threads, <=8 UAVs, variant LDS, both
group-sync spellings and generated symbol namespace. The hard LDS limit is 32768
bytes and >16384 bytes requires occupancy/device evidence. Storage bindings remain
bounded by `min(SystemInfo.maxGraphicsBufferSize, 128 MiB)`, direct dimensions by
65535 and storage offsets by 64-byte alignment.

The complete-file Quest shader review remains the binding source for this cut. It
covered every entry point and recursively included generated/math/ABI surface in
`SigmaNativeFrame.compute`, `SigmaNativeContract.compute` and
`SigmaNativeQuery.compute`, plus every production binding/dispatch and directly
affected test. No seventeenth dispatch or additional buffer owner exists.

WIP baseline `ad01eee` retains the first real N4.1R lowering:

```text
BuildNativeObservation                    32 footprints / 256-thread WG
Contract FOOTPRINT                        16 isolated 16-lane S16 teams
small-dyadic shadow/dual actions           generated exact specialization
D4 compose/inverse/orbit/adjacency         generated finite tables
TILE_CLOSE                                 one 8-round weighted three-orbit forest
canonical run                              1024 entries, not 16384
native graph                               14 entry points / exactly 16 dispatches
```

The first corrective subcut above `ad01eee` closes one real cross-dispatch race.
Dispatch 12 previously stored refinement prefix/child-order scheduler receipts in
`GaugeDelta`, while multi-workgroup dispatch 13 concurrently overwrote the same
records with terminal mutation payload. The immutable scheduler is now wholly in
three ranges of the existing `CloseScratch`: a 64 KiB refined bitset, 16 KiB block
prefix and 3.125 MiB child-order arena. At `PrepareNativeRevision`, StateDelta and
GaugeDelta are output-only. No new buffer, scalar ABI, kernel or dispatch exists.

Current Cut-1 gate evidence:

```text
graph/kernel/thread/UAV/synchronization/name gate           PASS
exact production variants compiled                          16/16
Vulkan 1.1 SPIR-V / spirv-val                               PASS
variant groupshared bytes
    frame 29952; FOOTPRINT 17920; TILE_CLOSE 15372;
    BOUNDARY 27984; GLOBAL_CLOSE 31184
CloseScratch 320x320                                          94.047 MiB
storage-buffer hard bound                                     <=128 MiB
focused refinement/page/ZEmpty/capacity gates                 7/7 PASS
adversarial >4-observation duplicate-child permutation        100/100 PASS
remaining Sigma CPU/Vulkan fixtures                          77/77 PASS
remaining same-process Frame fixtures                        25/25 PASS
complete semantic corpus in clean-process shards            109/109 PASS
```

One EditMode-only ordering artefact is recorded rather than hidden: the legacy
immediate-dispatch TILE/GLOBAL fixture followed by a different immediate Frame
shader contaminates seven later fixtures in the same editor frame (25/32). The
same seven assertions pass 7/7 in a clean Vulkan process, including the 100-repeat
race test, and the production graph uses one explicitly fenced command buffer.
Missing current canonical scratch bindings in those direct TILE/GLOBAL fixtures
were corrected. This is not Quest acceptance and no device timing is claimed.

The first physical Quest profile of the cardinality cuts is now recorded. The
installed Release APK executed the exact 16-dispatch native graph at 320x320 and
reported the four additional pose/depth preparation timestamps separately:

```text
ContractNativeQuery.FOOTPRINT                 133.9806 ms
PrepareNativeRefinementScan                    52.5162 ms
BuildNativeObservation                         25.4787 ms
ContractNativeQuery.TILE_CLOSE                 14.2876 ms
PrepareNativeRefinementProof                    9.3548 ms
EvaluateNativeRelation.GLOBAL_CLOSE             4.2490 ms
remaining timed stages                         9.8121 ms
full 20-dispatch timestamp checksum           249.6790 ms
```

Revision/root advanced monotonically from 1 through 49. Revision 50 then
reported the preserved `SIGMA_N4_FAULT_LOGICAL_EXTENT`/PageFault receipt
`0x00000100`; root-last retained root 49. No KGSL/MMU/device-lost/fence-timeout
event occurred in this run. Capture correctly entered held backpressure after the
terminal fault rather than overwriting owned ingress. This is correctness/lifecycle
evidence, not performance acceptance: the `<=40 ms` typical / `<50 ms` p95 gate
is still missed by 6.24x. The checkpoint is WIP and the next cut is restricted to
the measured FOOTPRINT, RefinementScan, Observation and TILE paths; canonical,
component-order and BOUNDARY work remain frozen unless new timestamps prove a
regression. N5R and S4-09 remain unopened.

## N4 resident-capacity safety and frozen N5R persistence design

The historical Quest GPU crash and the current N4 capacity receipt are now
forensically separated. In the SimpleScanner donor, commit `24f84a5` used an
async-compute graphics-fence polling path around publication replacement; commit
`ba286ac` replaced it with an asynchronous scanner-owned completion token and
retained both generations while queued GPU use was unproved. That is the verified
publication/readout use-after-free failure class.

Current Sigma already has the corresponding correct lifecycle boundary:
`RecordAfterAllWork` inserts a `CPUSynchronisation` graphics-queue ticket after
the complete native command buffer; ingress/capture/prediction/readout resources
remain owned through completion, and a polling fault is quarantined by
`SigmaGpuRetirement`. No lifetime redesign was added to N4.

The distinct N4 bug was a real resident-capacity planning error. CUT E owns 128
bounded page-plan scratch entries, but the live Quest segment exposed 112 physical
pages = 56 current/shadow pairs. The old planner could therefore derive target
slot 112 from logical plan 56. Production now:

```text
legal page plans = min(PagePlanCapacity, TargetPageCapacity / 2)
scratch addressing remains sized by the full plan arena
semantic source/target planning stops at legal resident pairs
capacity fault zeroes mutation + clone + scatter work
clone/scatter independently reject invalid target page/sample
root-last retains the prior root
```

The production-faithful one-pair regression fills the only resident source page,
requests the next logical page, then proves zero mutation/clone/scatter and
byte-identical state/chi-kappa/certificate metadata/root even when clone/scatter
are invoked defensively. An invalid odd/one-page target ABI also fails closed
rather than returning false `NO_CHANGE`.

Actual evidence over the current tree:

```text
ResidentPairCapacityFailsClosedBeforeCloneOrScatter       PASS
PreRootFaultLeavesStateGaugeCertificateAndRootUntouched   PASS
complete Unity EditMode/Vulkan                         107/107 PASS
generator --check / UAV / git diff --check        PASS / PASS / PASS
compute UAV maximum                                             <=8
N1/N2 opcode/node/table semantics diff                            0
generated metadata change        spec SHA + plan SHA + derived program fingerprint
```

`errusual.md` records the reusable lifetime, capacity and fault-taxonomy patterns.
`new_spec.md` and the frozen closure plan now define N5R as dirty-page immutable
COW/CAS persistence:

```text
HEAD -> RootObject -> sparse immutable page map -> PageRecord -> EncodePage blob
one sparse GPU logical-page locator + one dense reverse slot table
ABSENT_IN_ROOT != COLD_DURABLE
slot reuse = durable reachability + GPU completion + zero generation leases
```

N5R remains pending/unopened until N4R acceptance. N4 still fails closed at its
resident boundary; N5 alone converts that receipt into asynchronous cold
evict/rehydrate/backpressure plus exact replay.

The latest capture containing actual per-entrypoint GPU timestamps is
`n4r_fault_fix_retry_20260830_032312`. Its shader execution shape is still the
current N4 shape; the later 15 Hz scheduler does not replace these kernels. For
revision 1 the two dominant entries are:

```text
SigmaNativeContract.ContractNativeQuery       233.1091 ms / 2 dispatches
SigmaNativeFrame.PrepareNativeComponentOrder   73.4488 ms / 1 dispatch
BuildNativeObservation (next)                  66.4428 ms / 1 dispatch
total timestamped compute                     500.6230 ms / 20 profiled dispatches
```

`ContractNativeQuery` telemetry aggregates the independent FOOTPRINT and
TILE_CLOSE submissions: one costs 155.0864 ms and the other 78.0227 ms, but the
current telemetry record does not name which sample belongs to which mode.
Source/work cardinality makes FOOTPRINT the likely maximum; that attribution is
an inference, not a measured per-mode label. `PrepareNativeComponentOrder` is a
real remaining global close interpreter: after parallel materialization, the
last workgroup performs component discovery, complete-component comparison/
bitonic ordering, fresh-width walks and component scans. Together the top two
consume 61.2% of the timestamped compute. The older 4304.165 ms
`PrepareNativeRefinementPlan` and 868.472 ms `PrepareNativeCanonicalSelect`
measure the rejected pre-parallel CUT-E lowering and are not current blockers.

The current checkpoint action is WIP-only: commit and push this verified capacity/
N5R-design slice on `forensic/n4r-cut-e-scheduler`, then build and install one
Release Android/Vulkan APK from that exact source SHA. This does not accept N4R,
open N5R or waive the remaining Quest performance/physical gates.

## N4R fixed-cadence scan scheduler closure

The current WIP adopts only the proven SimpleScanner execution cadence, not its
world representation. `RoomScanner` admits at most one coherent four-stream
observation every `1/15 s`, and only when `SigmaInverseController` has neither a
pending prediction nor an in-flight native close. A successful late submission
sets a fresh deadline from its actual submission time, so elapsed ticks cannot
form a catch-up queue. `SigmaRigBridge` continues to hold exactly one coherent
owned observation across the gate and rejects repeated provider timestamps.

`SigmaRenderer` no longer consumes sensor observations from its XR `LateUpdate`.
That method draws the latest immutable carrier root every XR frame, including
between scan ticks and after sensor scan stop. The scheduler invokes the retained
prediction/inverse transfer separately at 15 Hz or slower under backpressure. No
S16/`Sigma_2` rule, generated relation, shader, resource ABI, buffer owner, kernel
or native dispatch changed; `HotDispatchCount` remains 16.

The prior Quest stop is now recorded without conflation: revision 52 failed page
validation after revisions 1--51 had consumed 228737 of 229376 warm-segment
logical sample slots, and root-last retained root 51. The simultaneous 4--9 FPS
plus fence/KGSL warnings were GPU-starvation evidence. This cadence cut prevents
queued scan submissions but does not claim to solve the remaining kernel cost or
N5 residency.

Actual evidence over the current source:

```text
focused cadence/four-stream contract corpus                 16/16 PASS
complete Unity EditMode/Vulkan                             100/100 PASS
generator --check / UAV / git diff --check          PASS / PASS / PASS
generated semantic expression/IR shape                         unchanged
HotDispatchCount                                                16
```

N4R remains WIP. The exact next action is a Release Android/Vulkan build from the
checkpoint SHA, streamed Quest install without launch, then a user-directed live
capture proving admitted scan cadence, independent XR presentation and the still-
open per-kernel performance/page-capacity evidence. N5R and S4-09 remain unopened.

## N4R completion-disposition correction and installed Quest build

The first post-publication Quest capture exposed a host-only tagged-receipt
decode defect.  Production correctly retained `Disposition.w` as the exact
certificate-delta count when `Disposition.x` changed from `RESOLVED` to
`PUBLISHED`, but `SigmaInverseController` treated every nonzero word as a fault.
The first sealed 16-record completion batch therefore misclassified a valid
publication containing 4887 certificate deltas, latched `_completionFaulted` and
stopped reconstruction while capture and XR rendering continued.

The correction interprets `Disposition.w` by its disposition tag: it is a fault
receipt only for `FAULTED`, and a certificate-delta count for `PUBLISHED`.
Runtime telemetry and the debug UI now expose `CertificateDeltaCount` separately
and never render that count as `FaultMask`.  The independently reserved submitted
revision remains the host comparison authority; the existing wrong-nonzero-GPU-
revision negative control stays green.  No shader, generated ABI, authority,
buffer, kernel or dispatch changed.

Actual evidence:

```text
focused completion classification corpus                    3/3 PASS
SigmaNativeFrameTests                                      30/30 PASS
complete Unity EditMode/Vulkan                             97/97 PASS
generator --check / UAV / git diff --check          PASS / PASS / PASS
HotDispatchCount                                                16
Release Android/Vulkan APK                                   PASS
APK SHA-256             ab0a979421bc8173c893b516bfaf27c8d54883369cf72eddab3330af4aa13cb7
APK bytes                                                 67526535
Quest streamed install                                      PASS
```

N4R remains WIP.  The exact next implementation cut is measured algorithmic
reduction of the real Quest hot kernels, starting with the two
`ContractNativeQuery` modes and then component/canonical preparation.  Fence
timeouts are treated as a symptom of GPU starvation, not an independent retry or
watchdog feature.  N5R and S4-09 remain unopened.

## N4R realized-component D4 parity corrective cut

The current WIP aligns production TILE_CLOSE orbit equivalence with the accepted
N1R/N2R canonical component stream.  Production previously compared the four
latent native-sector-to-chart directions for every chart assignment, including
directions absent from the realized component.  An edge-free singleton therefore
manufactured three non-D4 physical alternatives and the live full frame failed
closed as `UNRESOLVED`.  The correction compares only realized component cells
under one common D4 action; resolved native edge receipts remain the incidence
constraints and unused latent directions have no canonical bytes.

The focused Vulkan regression also exposed one adjacent accepted invariant:
distinct realized localities may not occupy the same dyadic cell.  TILE_CLOSE now
invalidates such an orbit before the D4 quotient while allowing repeated
observations carrying the same exact published-support locator to remain one
logical locality.  This is a fixed bounded tile-local check in the existing
`ContractNativeQuery` entry point.  It adds no submission, kernel, buffer owner,
authority input or physical identity.

Actual evidence over the final source:

```text
focused production TILE_CLOSE orbit corpus                 1/1 PASS
SigmaNativeFrameTests                                      28/28 PASS
complete Unity EditMode/Vulkan suite                       95/95 PASS
generator --check / UAV / git diff --check         PASS / PASS / PASS
new correction shader warning                                   0
HotDispatchCount                                               16
authority/capsule/generator semantic diff                        0
correction production diff vs 4b757f0                    +39/-21
cumulative production diff vs N2R b4399db             +9487/-1505
```

The decisive production cases are one edge-free singleton, two disconnected
singletons with independent chart gauges, one exact resolved edge and a generated
three-locality incidence whose distinct localities overlap in every candidate
embedding and therefore remains unresolved.  Existing resolved cycle, redundant-
edge inconsistency and 256-hit same-support certificate meet assertions remain
green.  N4R remains WIP: the exact next action is the user-directed Quest run from
this corrective commit, measuring whether revisions now escape the prior all-
unresolved stream and profiling the still-open N4 performance gate.  N5R and
S4-09 remain unopened.

## N4R exact footprint-team / compact-boundary WIP

Current source above `e192462a` closes the accepted A--D execution cut without
adding a seventeenth submission.  `BuildNativeObservation` retains its measured
eight independent 8-lane footprint teams.  `ContractNativeQuery FOOTPRINT` now
uses sixteen disjoint 16-lane teams in one 256-thread workgroup: every former
shared value/reduction is partitioned by team, the 48 neutral lanes of the old
isolated 64-lane lowering are removed, and every output remains addressed by its
original footprint id.  The full 320x320 workgroup count is therefore 6401
(one retained legacy group plus 6400 footprint groups), down from 102401.

`ContractNativeQuery` performs only the generated exact first-hit boundary-
envelope contact test before atomically appending execution-only canonical
boundary ids.  Invalid or exactly separated endpoints receive an explicit fresh
`NO_STITCH` receipt; contact candidates retain the unchanged 256-lane heavy
evaluator over all 16 sector pairs and all 16 intrinsic associator contexts, and
write back by original boundary id.  Unequal-level contact remains heavy and
therefore preserves `UNRESOLVED`.  Atomic append order cannot reach component,
D4, canonical, publication or physical identity.  GLOBAL_CLOSE unresolved state
is captured by canonical seed before scratch/counter reuse, so a genuine
unresolved frame can no longer become false `NO_CHANGE`.

Actual host/build evidence:

```text
HotDispatchCount / profiled calls                         16 / 16
BuildNativeObservation 320x320 groups                     12800
ContractNativeQuery FOOTPRINT groups                       6401
implicit adjacency boundaries                            204160
footprint isolated/team/tail/order parity                   PASS
boundary execution-order/result-class parity                PASS
GLOBAL_CLOSE unresolved -> CUT E receipt                    PASS
Unity EditMode/Vulkan                                    95/95 PASS
generator --check / UAV / git diff --check         PASS / PASS / PASS
Release Android/Vulkan APK                                  PASS
APK SHA-256                    59042dacf1a94f8695da07796ac6bef40435d32189a2ad71997879f48f8178ad
Quest streamed install                                      PASS
production delta vs e192462                         +515/-267, net +248
production delta vs N2R b4399db                    +9469/-1505, net +7964
```

Real Quest timing remains unclaimed: the connected device is stopped by Meta's
system `com.oculus.os.vrlockscreen/.SensorLockActivity` before the application
process is created.  A clean device run requires physical camera-safety unlock,
then Start Scan.  Exact next action is one clean capture of all 16 timestamped
submissions, revision/root progression and KGSL/fence evidence from this already
installed APK; source must not change before that measurement.  N4R remains WIP
and S4-09/N5R remains unopened.

## N4R Quest lifecycle / parallel CUT-E forensic WIP

Current dirty work above `6d231c3d` is being preserved as a forensic WIP
checkpoint, not accepted N4R.  It contains the production frame-slot lifecycle
initialization and independent submitted-revision verification discovered by the
live Quest run, plus the in-progress fixed-graph CUT-E parallel lowering and its
production-faithful fixtures.  No N5R work is open.

The live evidence that motivates the next correction remains:

```text
first profiled frame total GPU                         528.927 ms
ContractNativeQuery                                   278.991 ms
EvaluateNativeRelation                                 95.172 ms
observed terminal receipts                             176 x NO_CHANGE
observed published root / unresolved                    0 / 0
KGSL fence-timeout samples                              324
```

Source audit identifies two still-open N4R defects: FOOTPRINT/BOUNDARY currently
overdispatch one large workgroup per logical item instead of batching the fixed
domains, and the GLOBAL_CLOSE unresolved/component receipt is not consumed by
CUT E before its no-change decision.  The exact next implementation action after
this WIP checkpoint is a runtime-only fixed-pass batching of those two existing
phases plus lossless propagation of the existing global close receipt.  It must
retain the current fixed graph ceiling, accepted N1R/N2R semantics, root-last
publication, GPU-only authority and zero new physical ontology.  No acceptance,
performance closure or Quest pass is claimed by this checkpoint.

## N4R Quest direct-grid correction WIP

The first installed 14-dispatch CUT-E APK reached the real `320x320` graph but
failed before its first GPU dispatch because two direct workgroup counts were
placed wholly in Vulkan X:

```text
ContractNativeQuery FOOTPRINT       102401 x 1 x 1
EvaluateNativeRelation BOUNDARY     204161 x 1 x 1
Quest/Vulkan per-dimension limit     65535
```

This is an execution-grid lowering defect, not an authority, topology or closure
defect.  The narrow correction retains the same 14 submissions and reconstructs
the existing logical group index as `x + y * uniformGridWidth` in only those two
existing variants.  Group zero remains the collective/control group; logical
groups `1..102400` and `1..204160` retain their exact former payload ownership.
Padding groups fail the existing bounds and write nothing.  No kernel, shader
family, buffer owner, physical ABI, readback, fallback, S32 path or semantic rule
was added.

Actual post-correction evidence:

```text
footprint direct grid                              51201 x 2 x 1
boundary direct grid                               51041 x 4 x 1
logical coverage / duplicate coverage              exact / zero
Unity EditMode/Vulkan                              91/91 PASS
generator --check / UAV / git diff --check         PASS / PASS / PASS
HotDispatchCount                                   14
Release Android/Vulkan APK                         BUILD PASS
APK SHA-256                                        681b8e2f3aed57493062dea51f0d109815af8e4faf690a4625c27b197f99954c
APK size                                           66965051 bytes
Quest streamed install                             PASS
production diff vs b4399db                         +7791/-1308, net +6483
production diff vs 1d005cc                         +2944/-659, net +2285
```

The installed app launches, but unattended device smoke is presently paused by
the Quest system safety overlay requiring a physical power-button press to enable
cameras and microphones.  Exact next action is capture of the first post-unlock
terminal frame, all fourteen GPU timestamp rows and absence of the former illegal
dispatch, followed by the remaining physical corpus/deletion audit.  N4R is not
yet accepted and N5R remains unopened.

## N4R 14-dispatch CUT-E Quest-build audit checkpoint

N4R remains the sole active run.  The rejected monolithic CUT-E
`PrepareNativeRevision` lowering (600.77 s cold import followed by compiler
failure, approximately 4614 CFG blocks / 428 loop nodes / 77 synchronization
sites) has been mechanically split inside the existing shader/resource family.
The current fixed native graph contains exactly 14 hot dispatches, below the
hard ceiling of 16:

```text
BuildNativeObservation
ContractNativeQuery FOOTPRINT
EvaluateNativeRelation BOUNDARY
ContractNativeQuery TILE_CLOSE
EvaluateNativeRelation GLOBAL_CLOSE
PrepareNativeCanonicalSeed
PrepareNativeCanonicalSelect
PrepareNativeRefinementProof
PrepareNativeComponentOrder
PrepareNativeRefinementPlan
PrepareNativeRevision
PrepareNativePage
ScatterNativeState
CloseAndPublishNativeRevision
```

All fourteen calls use the existing `DispatchComputeProfiled` timestamp path.
No new shader family, buffer owner, physical ABI/authority, CPU semantic path,
readback, fallback ordering or S32 path was added.  A--D, the complete intrinsic
16-basis associator, generated collision-free comparator and root-last
publication semantics remain frozen.  The split stages communicate only through
the already-owned counters and bounded close scratch.

Actual host evidence for the current dirty-tree source before this forensic
checkpoint:

```text
focused CUT-E publication/refinement fixtures       8/8 PASS
required >4-observation refinement permutation      PASS
SigmaNativeFrameTests                              23/23 PASS
complete Unity EditMode/Vulkan suite               89/89 PASS
full-suite result duration                         23.5301183 s
generator --check / UAV / git diff --check          PASS / PASS / PASS
HotDispatchCount                                    14
Release Android/Vulkan APK                          BUILD PASS
APK SHA-256                                         dfcb57431d5483ff1976800e8b65414ddab5a2fe4442d05effd556ad41cfd487
Quest streamed install                              PASS
production diff vs b4399db                          +7775/-1307, net +6468
production diff vs 1d005cc                          +2919/-649, net +2270
production diff vs forensic parent aba829d          +1137/-464, net +673
```

The first complete suite run exposed four stale shared-shader oracle-fixture ABI
leaks, not production semantic failures: boundary/global keywords were not reset,
the fresh fixture did not force `_NativeFootprintCount=0`, `_NativeCounters` was
unbound, and one source-shape assertion named the removed lift helper.  Those
fixture defects were corrected narrowly; focused reruns and the complete 89-test
rerun pass without another production-shader change.

This checkpoint is not accepted N4R.  Exact next gate is the installed Quest
`320x320` physical corpus with all fourteen per-entrypoint GPU timestamps,
cardinalities, memory and ordinary/warm p95 evidence.  Any performance failure is
localized from those timestamps before a runtime-only lowering change.  The
final deletion audit and N4R acceptance commit remain pending; N5R is unopened.

## N4R intrinsic-associator/CUT B-D WIP checkpoint

N4R remains the sole active run.  The N4-discovered narrow generated completion
removes the undefined caller-supplied `BracketContext` input and evaluates the
complete intrinsic 16-basis K16 associator profile transiently in the existing
256-thread BOUNDARY workgroup.  It stores no `16 x S16` profile per boundary,
does not conflate the associator with the independent zero-divisor/annihilator
path, and changes no production kernel or submission count.

Actual green regression evidence before CUT E:

```text
generated CPU associator theorem                 14/14 PASS
N2 CPU<->Vulkan set oracle                       PASS
decisive link-closed/nonzero-associator boundary PASS
CUT C 16x16 production oracle                    PASS
CUT D global production oracle                   PASS
SigmaNativeFrameTests                            19/19 PASS
generator --check / UAV / diff                   PASS
HotDispatchCount                                 9
new production kernel / shader helper            0 / 0
production diff vs b4399db                       +3582/-264, net +3318
```

The current checkpoint is deliberately WIP, not accepted N4R.  No CUT-E
production mutation has been added.  The next exact action is one generated,
collision-free structured canonical comparator whose CPU/HLSL ordering is proved
against the already-accepted complete canonical serialization.  It must then be
consumed only in the existing `PrepareNativeRevision` position; a hash/tile/order
winner or a new kernel/dispatch is forbidden.

## Earlier N4R clean-restart CUT D checkpoint and CUT E hard stop

The clean N4R restart is preserved in local forensic commits `05b43f5` and
`a637252` above accepted base `b4399db`.  CUT A through CUT D now lower the full
`320x320` footprint domain into the existing nine-dispatch graph: 102400
footprints, 204160 implicit RIGHT/DOWN boundaries, one 16x16/256-thread local
close over 400 tiles, and one fixed global-summary close.  No production kernel,
shader family, dispatch, manager or persistent topology/component object was
added.  Authority JSON/capsule and the generator are byte-identical to
`b4399db`.

Verified cached Vulkan evidence:

```text
SigmaNativeFrameTests                         18/18 passed
ProductionGlobalCloseJoinsTiles...            passed, 0.062 s GPU/test body
cross-tile resolved join                       one resolved component
disconnected tile-local cycles                 two independently resolved components
inconsistent redundant seam                    UNRESOLVED
HotDispatchCount                               9
new production kernel/helper shader            0 / 0
production diff vs b4399db                     +3338 / -264 / net +3074
generated runtime activation                   +237 / -16 / net +221
handwritten production                         +3101 / -248 / net +2853
```

N4R is now stopped exactly at the plan's CUT E canonical-comparator hard gate.
The accepted N1 CPU authority canonicalizes a component from complete locality
payload plus complete directed stitch receipts: raw full-S16 forward/reverse link
and associator factors, their normalized Q48 intervals/classes, transport,
brackets, provenance, `chi/kappa`, certificates and refinement level.  The
accepted generated runtime HLSL exposes the 24 assignments/D4 actions but no
GPU-executable complete component canonical serializer/comparator.  The current
production boundary receipt is only six `uint2` words containing disposition,
sector/masks, two 32-bit factor hashes, sign bits, contact and bracket ID.  It has
already discarded information required by the accepted complete-byte order.

Consequently `PrepareNativeRevision` cannot legally canonical-sort and backing-
pack disconnected components.  Pixel/tile/scratch order or a hash winner would
violate N1R and the explicit N4 restart directive.  Exact missing primitive:

```text
generated GPU-executable complete stitch-component canonical serialization /
comparison receipt, preserving the accepted N1 complete payload semantics
```

No CUT E publication/refinement edit is legal until that primitive is exposed by
the sole generated program and proved CPU/Vulkan; the frozen N4 authority files
and generator have not been changed.

## N4R stopped forensic checkpoint

The forensic working tree implements the structural N4 representation slice without
changing the fixed native graph: exact `chi/kappa` representation records,
four-way repeatable arbitrary-parent refinement, atomic state/gauge/certificate
publication, representation-aware codec parity, static exclusion and an exact
durable unresolved-constraint journal. The live graph remains nine native
dispatches. Unity Vulkan EditMode passes 78/78, generator/check/UAV/diff gates
pass, and the Release APK builds and installs.

N4R is deliberately not accepted. Quest evidence exposed a deterministic
main/XR-thread history-scaling defect in the first journal implementation:

```text
completion batch                          16 records x 352 bytes
journal at observed failure               >2600 unique unresolved factors
revision 2032 -> 2033 gap                 0.800 s
revision 2160 -> 2161 gap                 0.854 s
observed native-close maximum             4.432 s
GPU native-close dispatch count           9 (unchanged)
```

The transfer size and carrier memory are not the cause. After every admitted
record, `SigmaExactConstraintJournal.Add` scans the full journal and calls
`_entries.Sort(CompareEntries)`. The comparator regenerates allocated 272-byte
canonical encodings for both operands on every comparison. Startup `Load()`
performs the same whole-journal sort. The cost therefore grows with historical
unresolved evidence and is delivered in 16-record bursts by the completion
batch. This violates the N4 bounded-proof/no-history-hot-path contract.

The history-scaling implementation defect has a bounded-certificate correction in
the forensic tree, together with parallel normalized refinement/certificate
transport. That work is deliberately not accepted because the full-frame audit
proved a prior semantic omission:

```text
many simultaneous unattached footprint groups
    -> one exact relative finite Sigma_2 support pattern
    -> canonical normalized state/gauge/certificate delta
```

is not defined by the frozen generated authority. The accepted authority proves
only alternative preimages of one coherent footprint group yielding one relative
level-zero cell. It neither derives relative coordinates for several fresh
supports nor proves a canonical independent-component translation gauge.

Exact audit: `.codex/S4-08.6_N4R_PRE_CLOSURE_AUDIT.md`.

The user-supplied scanner-level authority resolves that upstream omission without
claiming it was present in TOE. Exact calibrated shared-footprint contact is only
fresh-frame broad phase; the generated K16 program internally evaluates all 16
abstract continuation-sector pairs from full endpoint S16 states and an explicit
bracket context. Sector-pair transport is the source-backed signed-XOR law
`g=a xor b`, `U_ab(u)=epsilon(a,g)[u e_g]`; it has no sample-side or chart-axis
meaning. No caller supplies relation/factor/loop truth.

The physical result is abstract boundary incidence plus exact native transport,
not a signed chart displacement. The separate chart theorem integrates finite
incidence patterns and canonicalizes only the admitted representation orbit
`Z² semidirect D4`. D4 acts on square-dyadic chart coordinates only; it never
rotates physical S16/native witnesses, and dyadic level is not gauge. Distinct
surviving embeddings outside one gauge orbit remain unresolved. Disconnected
components are canonicalized independently and backing-packed only after their
complete bytes are ordered; packing distance has no physical meaning.

Corrective N1R-7 also freezes the implicit full-frame broad phase and hot semantic
shape `FOOTPRINT -> BOUNDARY -> CLOSE`: valid same-frame RIGHT/DOWN footprint
boundaries are enumerated once, never materialized as a physical/contact graph,
and never mapped to `Sigma_2` axes. Runtime activation remains N4 ownership.

Verified corrective N1R-7 evidence:

```text
Unity Vulkan EditMode                         79/79 passed
generator/check + git diff hygiene           passed
external stitch semantic-truth inputs        0
sample-side/native-sector -> DeltaU/DeltaV    0
caller loop/Plaquette truth                   0
legacy Candidate/Pending/Novel/DirtyEdge      0
Runtime/Resources production delta            +0 / -0
```

## Corrective N1R-7 abstract modal stitching and chart gauge

```text
program version                     CPQ4-S16-MERKABA-N1R-7
program fingerprint                 948a856d1bfe7a760d0c1e1b1624343e2d07cafed5548295c7aeee1034a4b3a6
IR                                   39 opcodes / 61 nodes / 70 operands
expressions / entries / reducers     17 / 7 / 5
constructive stitch proof            3e13b56d0cd06b864ef946ecc7abc83a390a76c6dc9015e1aad04a1ba6d8dc22
contact epsilon count                0
pixel/XYZ authority count            0
persistent component identity        false
chart gauge                           Z² semidirect D4
abstract-sector chart assignments     24
assignment D4 orbits                  3 x 8
implicit boundaries at 320x320        204160
hot semantic phases                   3
component gauge                      independent Z² semidirect D4
Unity Vulkan EditMode                79/79 passed
generator regeneration/check         passed
compute UAV gate                      passed (<=8)
new stitch shader warnings            0
Runtime/Resources delta               +0 / -0
```

The generated set expression is one semantic chain:

```text
WHOLE_FRAME_REVERSE_SET
 -> CONTACT_CANDIDATE_SET
 -> MERKABA_MODAL_STITCH
 -> STITCH_LOOP_CLOSURE
 -> FRESH_SUPPORT_SET_PATTERN
 -> COMPONENT_TRANSLATION_NORMALIZE
 -> CERTIFICATE_MINIMIZE
 -> COMMON_UNION_OR_UNRESOLVED
```

The CPU reference constructs transient stitch components, solves every resolved
incidence simultaneously, rejects inconsistent loops, translation-normalizes each
component, canonical-sorts the complete component byte multiset and performs no
sequential insertion or physical packing inference. The HLSL primitive parity
kernel exercises exact contact/no-contact/behind-hit and resolved/unresolved/
no-stitch classification only; N2 owns the bounded parallel set-level oracle.

## Accepted corrective N2R-7 bounded stitch oracle

The disposable Editor/Vulkan oracle executes the N1R-7 constructive operation
from actual full-S16 endpoint/contact/bracket inputs. It never accepts caller
relation, factor, loop or chart truth. The proof graph is exactly two test-only
dispatches:

```text
EvaluateNativeStitchPairs    one 256-thread workgroup per relation
CloseNativeStitchCases       one 256-thread workgroup per bounded proof case
```

The pair pass evaluates all 16 abstract sector pairs, signed-XOR transport,
forward/reverse link, endpoint associators and exact factor receipts in parallel.
The close pass assigns 24 chart candidates across 192 concurrent locality lanes,
uses two fixed eight-round group-local contraction schedules, derives transient
components and their feasible-assignment/D4-orbit masks inside the same dispatch,
and emits the final authoritative case disposition. Disconnected components own
independent chart problems; joining them recomputes one common chart problem. The
CPU harness independently compares component membership, assignment masks, orbit
receipts and final disposition but never repairs the GPU result. The rounds are
neither data-dependent nor host-redispatched; no cross-workgroup barrier,
per-component submission or serial graph interpreter is hidden in the fixture.
The eight-locality bound and 32-bit chart scratch are oracle-only; levels outside
that scratch domain fail closed rather than clamp.

```text
Unity 6000.5.9f1 Vulkan EditMode       80/80 passed
generator/check                        passed
compute UAV limit                      passed (<=8)
both stitch entrypoints SPIR-V         compiled
new stitch uninitialized warnings      0
test/oracle source delta               +1605 / -1
Runtime/Resources production delta     +0 / -0
production dispatch graph              unchanged (nine native submissions)
```

The corpus covers reversal, sampling-side and edge-order permutations, all 24
assignments/three D4 orbits, planar/rejoined/disconnected patterns, exact and
incompatible nonassociative folds, open occlusion followed by side-view resolution,
spatially separated equal modes, thin/two-sided support, consistent/inconsistent
fundamental cycles and mixed level 0/1/2 normalization. N4 must consume the same
semantics as compact wide worklists; none of the bounded oracle fixture limits or
test-only entrypoints is production architecture.

## Device-proven starting point

Release `cac9ab0` produced two coherent Quest runs:

```text
root progress       1→30 and 1→31
sampled dispatches  266 across 61 kernels
compute checksum    4520.946 ms
sampled wall        4776.70 ms
top kernels         ClosePendingEdges 2328.958 ms
                    BuildRgbSourceCells 937.364 ms
                    EvaluateCandidateMeets 837.657 ms
top-three share     90.78%
root-31 stop        missing=34, free=25, fault=0x120
pending             fragmented growth; observed holes/reuse=0/0
```

Forensic §§4–8 of `analyza.md` preserve the exact timing/kernel/source/causal
facts. Do not repeat that audit.

## Source-proven architectural basis

```text
SigmaS16 is an algebra value, not a world object/property layout.
Geometry and directional RGB are generated operations over all 16 coefficients.
Transition/associator/annihilator are relations of the same algebra.
SigmaOperatorPlan is one exact fingerprinted DAG vocabulary with bracket ownership.
Sigma_2 carrier addresses are signed-64 (u,v).
IntrinsicTopology owns no topology/geometry state.
Current pool/NULL codec use generated ZNullDyad.
Current address/page/codec ABI has no local chi/gauge-density map.
Current live carrier is one finite preallocated decoded pool; TryGetLatest is false.
Current sole generator emits Candidate/PendingGauge/DirtyEdge and proposal kinds.
Current GpuImageView has no exposure/gain/illumination nuisance region.
```

The source does not prove the authoritative TOE program, contextual
separation/complete-law sufficiency of an arbitrary-arity E22 inventory,
full-query validity and all-default quiescence of current `ZNullDyad`, complete
shadow-mode coupling/fibres, an exact carrier-reparameterization/canonical-gauge
theorem, calibrated photometric nuisance law, conservative nonresident query
support, durable pager or bounded exact certificate minimizer.

## Frozen v8.3 model

```text
CANONICAL
    one Psi : one carrier Sigma_2 -> S16

LAW
    M = Compile(A_S16, I_TOE, I_Q)
    with algebra/TOE/query-boundary provenance

QUERY
    sensor / eye / relation / prediction / export = M_q[Psi]

SCAN
    exact reverse evaluation of the same program;
    directional pre-hit/first-hit mould action, zero behind hit

INFORMATION
    exact feasible-set/coupled-factor certificate per active locality;
    optional proved directional compression, never scalar confidence

AMBIGUITY
    transient contractor disjunction; persisted constraint/evidence only

CLOSE
    both-eye constraints + native relations -> one NativeCloseCommit

DEFAULT
    ZEmpty has representation parity + all-default quiescence;
    supported->ZEmpty remains a physical mutation

REFINE
    contract S16 or revise exact chi/kappa to increase local density of the same Psi

REPRESENT
    normalized (chi, kappa, Psi-hat, certificate, directory) under one root;
    chi/kappa and Riemann-like density are representation, not physical state

QUERY SUPPORT
    generated conservative summaries, zero false negatives, resident/nonresident

BACKING
    durable logical page/gauge directory + encoded on-device blobs;
    bounded decoded GPU residency

PUBLISH
    resolved/common full-S16 deltas, root last
```

There is no physical chart/sheet/branch/hypothesis/materialization/topology world.
Preservation is defined on complete-program equivalence fibres, not one query
kernel.

## Corrective N1R-4 constructive capture boundary

The sole generated program now owns the previously missing constructive `I_Q`
adapter. Its immutable input is a quantized instrument boundary assembled from
`StereoRigFrameLease`/`GpuImageView`, `RigCalibrationMath`/`ConeLut`, the depth
encoding/range contract and the existing 2x2 optical footprint hull. It accepts no
host-built query rows, proposed S16 state, gauge address or relation truth.

For each eye it preserves four separately sourced leaves, derives one calibrated
Merkaba signed-permutation routing from the actual room-gauge cone ray, retains the
metric direct-order interval and finite cone footprint, and produces the exact
outward tangent envelope used by the existing fresh-preimage expression. Raw
photometric metadata absent from PCA is handled as a bounded post-ISP code-domain
region fingerprinted by graphics format/calibration provenance; it is explicitly
not a scene-linear-radiance claim.

```text
program version                    CPQ4-S16-MERKABA-N1R-4
program fingerprint                89d6d581391978f78eb9fc3bd461a65d36575e69eb01ed0c6b4189c9e076e435
capture-boundary fingerprint       2b492bf2deba23077ff873275f8672a3949e460a2b1ec2429c199fcd62691ba2
generated leaves                   8
focused N1 CPU/Vulkan              12/12 passed
generator --check                  passed
git diff --check                   passed
Runtime/Resources production       +0 / -0; still 17274 LOC
authority/test source              +1256 / -23 before controls
```

The earlier N2 result targets N1R-3 and is therefore no longer an acceptance
witness for this fingerprint. Corrective N2 must start from raw coherent
capture/calibration fixtures, execute this adapter on CPU and Vulkan, then enter
the unchanged reverse/relation/admission oracle. N3 remains closed until that
checkpoint is accepted.

## N1R accepted corrective checkpoint

The source audit revoked the premature `b541635` acceptance. The corrective N1R
keeps its valid K16 basis and closes the audited false-green gates without opening
N2R or changing live reconstruction.

The sole TOE input is
`Tools/sigma/authority/I_TOE_S16_K16_NATIVE_CLOSURE.md`:

```text
workspace SHA-256   9cdc8b1f3bfecfa3a49805be82ea786cdbf681ee8ffbdab0733d18dc24cfffef
upstream SHA-256    9d2e3604846305cfe5244a4ef49f169632c60582cf895256fadc36426dc5786f
```

Capsule §8 now supplies the canonical native closure law:

```text
G = 2 A^T A = -2 A^2                 (A is diffraction, not shell)
d_ij = u_j - U_ij u_i
primitive-normalized link/associator factors
F_hat = (W-I)/2
D_cl = direct sum of the exact normalized factors
independent closure weights = 0
epsilon_cl parameter = absent
```

The same bracket tree is lowered with checked Q16.48 points and outward intervals.
An interval excluding zero is incompatible, singleton zero is exact-closed and a
non-singleton interval containing zero remains unresolved. A zero primitive
`G`-norm remains an explicit diffraction-kernel factor.

The self-hashed sole generator emits a numeric CPU/HLSL relation plan under program
fingerprint `c98855216dd16d059ebaf0c33652250b7acac4681b01e0d585ab0ba28de67af3`:

```text
opcodes / nodes / operands              35 / 55 / 58
expressions / query entry points        16 / 7
E22 inventory                           0; direct full S16 retained
shadow-decoupling proof                 absent; hidden modes not frozen
associator nonzero basis triples        1848
negative sign-holonomy fixtures         2688
```

Corrective proof gates:

```text
actual bracket-DAG forward/reverse domain    4913 zero+basis triples
set-valued associator output classes         31/31 ambiguous, max preimage 3065
total reverse/scene/interval fixtures         5615
query-support exhaustive fixtures            512, false negatives 0
refined / nonresident support fixtures        256 / 128
default spellings x query entries             3 x 7 = 21
duplicate revisit certificate                 10000 -> 1 factor + multiplicity
coupled/disjunctive certificate factors       2 retained
recursive dyadic gauge orders                 24
transported gauge payload fields              6
fresh non-equivalent support                  rejected
fresh shadow/preimage fixtures                127
fresh admitted / unresolved                   125 / 2
fresh dual-frame round trips                  125
fresh mixed-boundary resolutions              125
fresh external relation-truth inputs          0
fresh exact one-LSB defect fixture             1
```

The query-support proof independently evaluates generated Merkaba shadows plus
the retained direct-S16 intrinsic route. Default parity abstract-interprets each
actual forward entry DAG. The certificate minimizer is executable and compared
against exhaustive feasible assignments. Gauge proof covers recursive dyadic
split/collapse, disconnected support, state/factor/relation/evidence/information/
bandwidth transport, exact measure, allocation translation and order.

Generated N1R execution plans live only under `Tests/Editor/Generated`; moving the
previous disconnected generated files out of `Runtime` makes production
`Runtime/SigmaPrism + Runtime/Resources/SigmaPrism` exactly `+0/-0` versus N0R
`df5200f`. There is no live call site, mutation path or second generator.

Verified on Unity `6000.5.9f1` with Vulkan:

```text
EditMode                              102/102 passed, 0 failed
focused N1R tests                    11/11 passed
N2 consumer recompile                14/14 passed; no generated action warning
generator regeneration/check         passed
compute UAV <= 8                     passed
production equality vs a129a85/cac9ab0 passed
git diff hygiene                     passed
```

## N2R corrected non-mutating oracle checkpoint

Source audit revoked both the premature `869f848` checkpoint and the still-
incomplete correction at `b4d88d6`. The superseding checkpoint retains the
non-mutating scope, consumes the N1R generated program only from the Editor/Vulkan
test assembly and closes every audited semantic false-green. It adds no runtime
call site, canonical buffer, mutation path, publication path or legacy cutover.
The CPU semantic evaluator and Vulkan fixtures implement:

```text
SelectNativeQuerySupport       generated conservative all-default bound
EvaluateNativeQuery            generated entry-point full-S16 query contraction
ReduceNativeQuery             128-thread U64 grouping + descriptor-owned reduction
EvaluateNativeRelation         complete relation factors + ZD/near/nonassoc classes
ContractNativeQuery            exact preimage filtering + directional/PWL action
ResolveContractorOverflow      cold indirect continuation of the same contractor
```

Evaluation follows each actual generated entry-point expression and reducer instead
of treating the entry point as a label. `NONE/DEBUG` emits only the explicitly
requested generated relation projection; `EXPORT_RELATION_GATED` keeps manifested
supports and emits connectivity only for descriptor-permitted native relations;
neither aliases the first-hit reducer. `EYE_PAIR` is one generated invocation over
two query rows in the dispatch Y dimension and retains one shared immutable
observation-revision/pose-calibration context plus distinct left/right results.

The field reducer groups refined children by the complete 64-bit support key before
first-hit classification; keys `1`, `33` and `2^40+1` remain distinct. It handles
128 same-support and 96 mixed-support contributions inside one fixed workgroup; 129
contributions fail closed into an explicit reason-coded cold-continuation receipt
rather than truncating. The joint contractor derives native-relation and identity-
preserving transport from frozen relation/gauge records rather than accepting truth
booleans, retains one action/claim witness per coherent query and executes the
calibrated three-channel exposure/gain/illumination/white-balance/offset plus
monotone PWL transfer law. Right-eye evidence therefore cannot collapse into the
left lookup. Alternative supports survive only in disposable test-assembly scratch.
No semantic branch type or shader is present under production `Runtime/SigmaPrism`
or `Runtime/Resources/SigmaPrism`.

The GPU lowering is cardinality-parallel rather than a serial interpreter:

```text
fixed oracle entry points                    4 query/relation + 3 contract/overflow
native relation mapping                      1 workgroup per relation, 256 threads
signed-XOR product plane                     16 x 16 pair terms in parallel
annihilator catalogue                        168 actions in parallel in same group
field reduction                             128-thread bitonic + segmented scans
dispatch-per relation/lane/support/segment   none
```

The relation factor lowering no longer accepts only integral `[-32,32]` lattice
states. One 16x16 metric plane reduces the exact signed 256-bit raw Q16.48
quadratic form and is checked byte-for-byte against CPU `BigInteger` `G` norms for
fractional, multi-lane and large valid coefficients. Exact zero, diffraction
kernel, incompatible, ZD, near-singular and nonassociative classes remain distinct.

The corrective N1R-3 fresh expression is now consumed from raw coherent observation
input rather than an externally supplied proposed state/gauge. CPU and Vulkan both
perform exact signed-axis pullback of the separately retained order plus three
optical leaves from each eye, intersect the common four-axis shadow cell, select
the generated tangent representative, lift all 16 S16 lanes through the dual frame,
forward-replay both original query rows and derive the mixed-ZEmpty boundary through
the existing full native-relation entry point. Unique and complete-union-common
results admit one relative `chi_0/kappa_0` cell; non-equivalent alternatives,
missing evidence and behind/no-first-hit inputs remain unresolved.
The CPU↔Vulkan matrix additionally covers all 18 nonzero half-step points of the
`{-1,0,1}^4` tangent lattice, alternating positive/negative right-eye row routing.

The Vulkan lowering adds no entry point. One 64-thread workgroup maps the eight
coherent leaves, four shadow axes and sixteen S16 lanes for each reverse branch;
one existing 256-thread relation group derives each boundary; one fixed 64-thread
collective emits common-result or unresolved. Thus the bounded fresh proof is
exactly three dispatches over two existing kernel names. Branch cardinality changes
workgroups only. More than four hot branches is never truncated: it emits an
explicit retained cold-continuation reason.

Accepted exact fixture groups cover:

```text
one support / only-A / ambiguous union / common delta                     passed
right-eye-only discrimination / two sheets / joint-direction intersection passed
whole-query first hit / behind NO_CLAIM / farther-hit reveal              passed
near-false mould / unrelated-sheet rejection / two-direction equilibrium  passed
strong/weak geometry+optical order / missing metadata / calibrated light   passed
five single-query field entries x 15 nonempty worlds                      75 parity cases
coherent two-query EYE_PAIR + EXPORT/DEBUG reducer distinction              passed
complete native relation corpus incl. fractional/multi-lane/large Q48      130 tuples
uniform/refined same-support reduction and U64 support identities           passed
128 same-support / 96 mixed / 129 explicit cold continuation               passed
resident/nonresident and 1/2/7 reversed execution windows                  passed
three-channel two-segment PWL law + hot/indirect overflow                   passed
support-index exhaustive mixed/refined/nonresident worlds                  256
duplicate compatible revisits                                              10000 bounded
```

Verified on Unity `6000.5.9f1` with Vulkan:

```text
EditMode                             104/104 passed, 0 failed
focused N2R fixture groups            15/15 passed
generator regeneration/check         passed; N1 fingerprint unchanged
compute UAV <= 8                     passed
production equality vs 63c042a       passed
Runtime/Resources live call sites    0
git diff hygiene                     passed
```

The Vulkan compiler's generated directional-action diagnostic is constructionally
closed rather than ignored: the generated function assigns all witness fields on
both paths, and the GPU corpus explicitly checks initialized pre-hit, `NO_CLAIM`
and first-hit-mould roles plus both interval endpoints. The corrected reducer and
exact U256 relation math emit no uninitialized-variable diagnostic. The known
constant-catalog size diagnostic remains a compiler lowering warning, not hidden
serial work; its complete table is exercised bitwise. Existing production-shader
warnings are outside this production-neutral N2 diff.

The N2R-4 raw-boundary supplement adds `+333/-151` non-production oracle/test
source lines over corrective N1R-4 `c3ac16a`; no Unity asset or entry point is
added. The
earlier N2R oracle remains confined to Editor/Vulkan proof code. Production remains
exactly `+0/-0`; the
accepted runtime is untouched. These Editor timings validate semantics and graph
shape only; Release Quest GPU timing begins with the live N3 cutover.

## Corrective N1R fresh-support authority

The N3 preflight correctly exposed that the accepted program normalized only an
externally supplied relative support pattern. This checkpoint closes that N1-owned
omission inside the sole generated program. The new executable expression is:

```text
coherent left/right sensor reverse branches
    -> intersect four exact outward Merkaba-shadow cells per branch
    -> enforce the tangent sum constraint
    -> deterministic minimum-change selector from ZEmpty inside the resolved fibre
    -> exact dual-frame lift into one full S16 value
    -> forward shadow verification
    -> internally generated mixed (state,ZEmpty,ZEmpty) native-relation witness
    -> one relative level-zero chi_0/kappa_0 support cell
    -> unique/common complete-union result or UNRESOLVED
```

The generated ABI accepts no proposed S16 state, proposed gauge cell, boundary
relation enum, relation-satisfied bit, identity-transport bit, pixel, XYZ or NOVEL
kind. The canonical base relation context is evaluated from the lifted state and
`ZEmpty` operands by the generated S16 relation code. Algebra-zero support is not
admitted; a nonzero diffraction-kernel defect remains unresolved. A nonzero exact
Q16.48 point numerator with positive primitive `G` norm remains provably nonzero
even when an outward normalized enclosure contains zero, while uncertain interval
factors retain the normal unresolved classification.

All surviving reverse branches must serialize the same full-S16 state, relative
support and generated relation witness modulo the admitted global translation
gauge. Non-equivalent branches remain unresolved. The proof covers 125 admitted
singleton shadow cells, common-result permutation, impossible/ambiguous cases,
diffraction-kernel rejection and an exact one-LSB nonkernel boundary. The HLSL
directional-action helper uses explicit initialized outputs rather than a returned
aggregate; this changes no entry point or dispatch and removes the Vulkan
uninitialized diagnostic in every N2 consumer.

This is still an authority/test-generated N1 cut: Runtime/Resources remain
byte-identical to `a129a85` and `cac9ab0`, at the frozen 17,274 production LOC.
The corrective N2 checkpoint now proves this operation from calibrated raw
left/right instrument boundaries. Room-gauge cone rays and four code intervals
per eye are assembled by the generated program inside the existing 64-thread
contractor workgroup. N3 may bind it but may not recreate the adapter or
constructor in live host/shader code.

## N3R accepted live NativeCloseCommit checkpoint

N3R binds the N1R/N2R-proved calibrated fresh-admission expression into one live
GPU close and hard-deletes the legacy proposal/source-cell/provider/topology/edge/
sort publication graph. The live native close is a fixed nine-dispatch graph:

```text
BuildNativeObservation
ContractNativeQuery                x2
EvaluateNativeRelation             x2
PrepareNativeRevision
PrepareNativePage
ScatterNativeState
CloseAndPublishNativeRevision
```

Relation cardinality changes workgroups, not submissions. One 256-thread relation
group evaluates the 16x16 signed-XOR plane, explicit bracket phases, exact G
reductions and 168 annihilator actions in parallel. No per-page, per-support,
per-relation, per-lane or per-segment dispatch loop exists. The profiled complete
scan submission includes four retained instrument/pose preparation dispatches, so
Quest records 13 dispatches across 11 entry points; the N3 native term is 9 and the
normative `D_budget <= 80` gate passes.

The decoded budget is now a ceiling rather than an eager allocation target. N3
allocates only one two-page current/shadow pair. An unresolved terminal outcome
copies a compact 272-byte exact record after the GPU fence and immediately releases
the RGB/depth lease; it never queues capture textures. This removed the observed
19-second cold allocation stall and eight-frame `RingExhausted` ownership leak.

Quest Release evidence from the installed N3 APK:

```text
first graph ready -> first terminal receipt       about 114 ms
first published root                              revision 9
published delta                                   1 full-S16 state, 0 gauge
first profiled unresolved submission              13 dispatches / 11 kernels
GPU timestamp checksum                            0.670 ms
second start profiled submission                  0.729 ms
native-close wall                                 ~57-59 ms steady mean
observed maximum                                  92.23 ms
continuous receipts                               revisions 1..840
stop/start continuation                           revision 582..840
RingExhausted / canonical fault                   0 / 0
Unity Vulkan EditMode                             65/65 passed
generator / UAV / diff hygiene                    passed / passed / passed
```

The timestamp sample is an unresolved-path GPU measurement, not an informative-
commit GPU claim. Revision 9 independently proves that fresh all-`ZEmpty` support
can publish. Later unattached support remains deliberately unresolved in N3 because
regional support/density and durable constraint closure begin in N4.

The temporary preview is still the pre-N6 `SigmaGeometryReadout` consumer. N3
publishes only one base-density sample, and the new Merkaba tangent lift is not
equivalent to that legacy G projector (two of its four selected geometry rows are
identically zero on the lift). A blank/imperceptible preview therefore does not
mean the N3 root is absent; the authoritative generated eye/readout cutover remains
N6 ownership and no readout heuristic is added to N3.

## N0R implementation-safety correction

The accepted N0R ontology is unchanged, but its implementation contract is now
closed against the reviewed gaps:

```text
ZEmpty backing equivalence != physical support removal
all-default neighbourhood = reducer identity + DEFAULT_SAT + zero work
arbitrary-arity E22 requires contextual separation/complete-law proof
M = Compile(A_S16, I_TOE, I_Q) with explicit provenance
coordinate locality is fast relation support, not exclusive topology
N3R jointly cuts sensor inverse + native relation before publication
64x64 page / 8x8 codec block is frozen for S4-08.6 parity
exact directional pre-hit/first-hit mould action; zero behind hit
exact locality feasible-set/coupled-factor information; no scalar confidence
exact chi/kappa gauge-density/reconstruction representation and observation-order-independent normalizer
pointwise full-S16 gauge transport; readout equality alone is insufficient
atomic state/gauge/certificate/directory root
fresh-world base support in N3 + generated ABI hard cut
calibrated optical nuisance law
zero-false-negative resident/nonresident query-support index
bounded exact certificate minimization
real durable pager in N5; decoded residency is not world size
recovery ceiling separated from no-change/informative/eye realtime contracts
```

Changed documentation/control files are `new_spec.md`, `analyza.md`, the frozen
plan/resume and GOAL/STATE/TASK_DAG/DECISIONS plus regenerated code graph. Runtime
and Resources remain byte-untouched.

Verified locally:

```text
TASK_DAG JSON                     valid
Markdown fences                  balanced
git diff --check                 clean
Runtime/Resources vs df5200f     byte-identical
code graph                       current, 103 files, 5c6645942471
validate_goal_state              green, active S4-08
```

## LOC cursor

```text
corrective N1R-7 current Sigma production LOC             9764
corrective N1R-7 vs 1842841 Runtime/Resources      +0 / -0 / net 0
corrective N1R-7 tests/generator/authority         +785 / -251 / net +534
corrective N1R-7 spec/controls/code graph          +299 / -242 / net +57
N2R run delta vs eacf261                    +0 / -0 / net 0
N2R non-production source / metadata        +5003 / +27
N2R correction vs rejected b4d88d6           +1085 / -233
fresh N2 supplement vs 63c042a                +1013 / -2 test source
N2R-4 raw boundary vs c3ac16a                 +333 / -151 / net +182 test-only
corrective checkpoint vs b541635            +0 / -392 / net -392
N1R run delta vs df5200f                    +0 / -0 / net 0
N2R gross deletion vs cac9ab0               0
N2R new production vs cac9ab0               0
N2R net vs cac9ab0                          0
N2R net vs d3b83e1                          +601
N3R Sigma production vs N2R              +2498 / -10008 / net -7510
N3R all Runtime code incl. requested UX  +2607 / -10110 / net -7503
current Sigma production LOC                              9764
current gross/new/net vs cac9ab0             10008 / 2498 / -7510
current net vs d3b83e1                                    -6909

final gross deletion vs cac9ab0             >=10000
final new production                         <=4000
final net vs cac9ab0                         <=-6100
final net vs d3b83e1                         <=-5500
final retired live symbols/fallbacks         0
```

The corrective checkpoint deletes exactly the rejected 392-line disconnected N1
runtime/fixture addition from parent `b541635`. The authoritative baseline totals
are `17274` at `cac9ab0` and `16673` at `d3b83e1`. Corrected cumulative N1R is
byte-identical to both `df5200f` and `cac9ab0`, so its gross/new/net against
`cac9ab0` is zero; `+601` against `d3b83e1` is the baseline-total difference.
N2R is not a production replacement cut and claims none of the N7 size gates. Its
oracle code is confined to the test assembly and immediately exercises the N1R
plans; cumulative N1R+N2R has no disconnected production addition.

N3R is one joint publication-capable sensor/native-relation cutover; it deletes
both old inverse and old topology/edge paths in the same commit. N4R–N6R delete
each subsequently replaced branch immediately.

## Completion gate

Only N7R closes S4‑08. It requires exact oracle/Vulkan/LOC/code-graph gates,
same-commit source archive and Release APK, Quest installation and physical corpus,
truthful kernel times, recovery `1500/1800 ms`, final stable no-change `<=33.3 ms`,
ordinary informative p95 `<=100 ms`, eye query `<=13.89/11.11 ms` at 72/90 Hz,
long scan beyond decoded residency, bounded duplicate evidence and no revision/
segment latency slope.
