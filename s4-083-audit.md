# S4-08.3 device forensic closure audit

Date: 2026-08-23 (Europe/Prague)
Audited source: `082eda09e16d6dea3e00a00d3c61540ce94effba`
Device evidence: `/home/wraith/Stažené/newlog.log`

## Scope and invariants

This is a read-only forensic closure of the empty-scan Quest run. It does not
authorize a new reconstruction model. The only canonical physical world remains:

```text
Psi : Sigma_2 -> S16
```

The repair may not introduce a side mesh, point cloud, topology world, FP
canonical decision, CPU readback scheduler, dropped evidence, or a monolithic
frame-blocking inverse batch. Q16.48 value and validity semantics, complete sealed
evidence, proof/provenance, first-hit causality and atomic publication remain
unchanged.

No source file was modified while producing this audit.

## Verdict

The empty scan is not caused by the preview shader or by insufficient Quest
memory. It fails earlier in the exact transaction path:

```text
incomplete inverse bindings
    -> missing/stale joint-evidence scratch
    -> fail-open transaction progression
    -> proof/transition closure without accepted evidence
    -> publication of an all-null Psi page
    -> zero support and zero visible geometry
```

An independent publication-handle defect then makes the first publication
invisible, so a non-zero draw plan appears only after the second publication.

## Confirmed root causes

| Priority | Defect | Direct consequence |
| --- | --- | --- |
| P0 | Streaming inverse omits the complete constraint-ledger binding contract | RGB/meet stages cannot produce a valid joint constraint |
| P0 | Pre-proof scratch has no transaction-generation/phase-completion ownership | A skipped or faulted stage can be consumed as null, undefined or old evidence |
| P0 | Proof, transition, revalidation and publication do not require accepted/changed evidence | Execution failure can publish a novel all-null Psi generation |
| P0 | Zero-initialized unused page references alias physical page slot 0 | The first publication retires its own real page |
| P0 | `SKIPPED_ADMISSION` evidence is not sealed before the ingress cursor advances | Sensor evidence is dropped because of scheduling capacity |
| P1 | One small microtile/chunk is advanced per host canonical submission | First exact publication takes 36.57 seconds |
| P1 | Bundle extraction binds `_PoseResult` but not its pose-consume matrices | A non-zero accepted pose has a latent source-extraction correctness defect |

### P0-1: incomplete inverse constraint-ledger binding

The device reports:

```text
newlog.log:416  SigmaStreamInverse kernel 1: _ConstraintBlocks is not set
newlog.log:422  SigmaStreamInverse kernel 2: _ConstraintBlocks is not set
newlog.log:428  SigmaStreamInverse kernel 3: _ConstraintBlocks is not set
```

The kernel map is:

```text
0 PrepareTransactionMicrotile
1 EvaluateTransactionRgbLeft
2 EvaluateTransactionRgbRight
3 MeetTransactionMicrotile
4 EvaluateTransactionMicrotile
```

`Runtime/SigmaPrism/SigmaStreamingGraph.cs:549-592`, `BindInverse()`, omits:

```text
_ConstraintCertificates
_ConstraintCertificateBounds
_ConstraintBlocks
_ConstraintProofCapacity
```

The complete existing read contract is implemented by
`Runtime/SigmaPrism/SigmaConstraintLedger.cs:372-388`, `BindReadOnly()`.
`Runtime/Resources/SigmaPrism/SigmaConstraintPrior.hlsl:21-73` also consumes
`_ConstraintProofCapacity`; binding only `_ConstraintBlocks` would still leave the
prior invalid.

### P0-2: a failed evidence phase does not stop transaction progress

Unity's public API does not normatively specify every internal dispatch action
after a missing-property error. This audit therefore does not assume that a whole
shader or command buffer is skipped. The device evidence proves the narrower and
sufficient facts:

1. kernels 1-3 emit binding errors;
2. their required writes are not available as valid current-generation evidence;
3. later dispatches execute and transaction/proof/transition/publication counters
   advance.

Scratch ownership in `SigmaStreamInverse.compute` is:

| Scratch | Writer | Reader | Current fault/generation protection |
| --- | --- | --- | --- |
| `EvalProjective` | Prepare | RGB, Meet, Final | none |
| `ProofSamples.rgbLeft/right` | RGB-L/R | Proof | expected overwrite only |
| `JointBounds` | Meet | Final, Proof | no per-record generation |
| `JointProvenance` | Meet | Final, Proof | no per-record generation |
| `SampleMetadata` | Meet | Final | no per-record generation |
| `SampleOutcomes` | Meet | Final, Proof, Transition | no per-record generation |

Prepare starts at `SigmaStreamInverse.compute:740`; RGB entrypoints start at
`:872/:879`; Meet starts at `:886`; Final starts at `:1097`. Final validates the
work/transaction identity but not a same-generation completion receipt for
Prepare/RGB-L/R/Meet. It then advances progress.

Consequences:

- first use can consume undefined/zero scratch after a skipped phase;
- reused transaction slots can consume a previous generation's scratch;
- normal-path overwrite is not a correctness guarantee for fault or skipped
  execution.

### P0-3: the canonical state machine is fail-open

The currently possible path is:

```text
no valid accepted source evidence
    -> PROOF_PENDING
    -> TRANSITION_PENDING
    -> NULL <-> NULL classified UNSUPPORTED but CLOSED
    -> REVALIDATE_PENDING
    -> PUBLISHABLE
    -> PUBLISHED
```

`SigmaStreamTransition.compute:565-599`, `SigmaCandidateCloseRecord()`, closes an
unchanged/unsupported transition. `SigmaStreamRevalidation.compute:356-358` makes
the transaction publishable when transition phase is closed, without requiring a
completed evidence phase or accepted canonical change. `SigmaStreamPublication`
checks structural closure but not accepted/changed/non-null evidence.

The live state machine therefore does not distinguish:

```text
VALID_NO_CHANGE_EXISTING
NOVEL_CONTACT_ACCEPTED
CONTRADICTION_OR_UNRESOLVED
EXECUTION_FAILED_OR_INCOMPLETE
```

A valid no-change observation of an existing carrier must not create a novel
generation. UNKNOWN-to-contact requires exact accepted promotion evidence.
Contradiction remains unresolved/dormant. Missing execution must fail closed.

### P0-4: unused page references corrupt publication visibility

`SigmaInverseWorkGraph.compute:989-1018` zero-initializes a transaction and only
initializes `page0`. `page1`, `page2` and `page3` remain all-zero records.

`SigmaStreamPublication.compute:109-119` infers page validity only from:

```text
page.state.y < _PageCapacity
```

An all-zero unused page therefore names valid physical slot 0. Publication runs
four lanes:

```text
lane 0    real page0
lanes 1-3 fake zero pages, also targeting slot 0
```

During the first publication the real target is slot 0. The fake lanes write
visibility/retirement metadata for the same slot, producing a page whose born and
retired manifests coincide. The publication counter advances, but the page is not
visible.

During the second publication the real page uses another slot. The fake lanes
still corrupt slot 0, while the real second slot survives and produces a non-zero
draw plan.

Observed chronology:

```text
Start Scan             13:17:48.710
first publication      13:18:25.280  draw=0
second publication     13:19:03.531  draw=24576
publication->draw lag  38.251 s
```

The readout does not require a second publication. The earlier stable-scan/lane-0
hypothesis is falsified. This is a page-handle validity defect.

The non-zero draw plan is not proof of non-null geometry: published telemetry
still reports `supported=0`, `nonzero=0`, `information=0` and a zero AABB.

### P0-5: ingress is not lossless

`SigmaInverseWorkGraph.compute:275-490`, `CompactIngressBundles`, admits at most
two page packets from one coherent frame. Unselected candidates only increment
`SKIPPED_ADMISSION`; no owned sealed source/raw payload has been created for them
when the ingress cursor advances.

The original observation is therefore lost. Seeing a similar region in a future
frame is not lossless retention of the original evidence.

## Exact empty-scan causal graph

```text
coherent RGB-D ingress
    -> transaction admission
    -> inverse kernels 1-3 lack full constraint-ledger bindings
    -> current RGB/Meet scratch is not validly completed
    -> Final has no phase receipt or scratch generation guard
    -> transaction progress advances
    -> proof closes null/stale evidence
    -> NULL<->NULL transition becomes UNSUPPORTED but CLOSED
    -> revalidation sets PUBLISHABLE without accepted-evidence gate
    -> publication creates an all-null Psi generation
    -> support=0, information=0, zero AABB
    -> temporary carrier preview correctly has no physical geometry to show
```

In parallel:

```text
first publication
    -> unused page1..3 alias page slot 0
    -> real slot 0 is retired by its own manifest
    -> no visible page and draw=0
    -> second publication uses a different real slot
    -> draw plan becomes non-zero, but canonical payload remains all-null
```

## 36.57-second first-result latency

Measured:

```text
Start Scan          13:17:48.710
first publication   13:18:25.280
latency             36.570 s
canonical quanta    1663
effective rate      approximately 45.5 quanta/s
```

Generated constants currently decompose one isolated single-source page roughly
as follows:

```text
1 admission
64 proof blocks * (
    4 16-sample evaluation microtiles
  + 1 source reduction
  + 1 proof scheduling/finalization step)
128 annihilator chunks
64 associator chunks
1 revalidation
1 publication
-----------------------------------------
approximately 579 scheduler quanta
```

At the observed rate, even the isolated floor is about 12.7 seconds. The observed
36.57 seconds results from breadth-first co-progress of multiple transactions,
completion of at least two complete 64-block proof pages, serialized proof-owner
progress, and the host issuing only a small opcode step per canonical submission.
Canonical and derived submissions also complete as a pair before the next pair is
issued.

The expensive exact algebra is not the sole cause. Storage page, execution
microtile and XR-frame scheduling are still coupled through a one-opcode-per-host-
quantum handshake.

The systemic repair must retain bounded work. A suitable execution unit is one
complete 64-sample proof block composed of four fixed 16-sample coordinate
microtiles, plus a fixed number of ordered transition chunks per token quantum.
Persistent cursors and proof order remain exact; this is not a return to a
page-sized monster batch.

## Lease ownership and backpressure

`_pendingIngress` has no explicit count cap, but it is not unbounded safe storage:

- the prediction target ring has four slots;
- one ingress retains its original prediction lease;
- it also reserves a corrected prediction target until completion;
- practical concurrency is therefore about two original/corrected pairs;
- retained prediction leases pin their capture sources;
- each capture stream has eight ring slots;
- the synchronizer can retain up to six unpaired samples and drops oldest entries
  on overflow.

An unbounded CPU queue merely moves backpressure into the prediction/capture
rings. A transaction that survives short ingress must own copied immutable GPU
payload and release all capture/prediction leases.

## Additional confirmed gaps

### Diagnostics probation binding

`newlog.log:434` reports `_StreamProbation is not set` for
`SigmaInverseWorkGraph`, kernel 6 (`FinalizeStreamingScheduleDiagnostics`).
`SigmaStreamingGraph.cs:458-467`, `BindScheduleDiagnostics()`, omits that buffer.
Active/dormant/probation scheduling fields and related admission telemetry can
therefore be stale or unwritten. Proof, transition and publication counters
written independently remain useful.

### Non-zero pose source extraction

`SigmaSourceBundle.compute::ExtractSealedBundleSamples` consumes
`SigmaPoseApplyWorld`/`SigmaPoseUnapplyWorld`. `BindBundleSource()` at
`SigmaStreamingGraph.cs:497-547` binds `_PoseResult` but not
`_PoseConsumeReferenceFromWorld` and `_PoseConsumeWorldFromReference`.
This is latent when the accepted twist is zero and incorrect when it is non-zero.

### Misleading UI and timing

- UI topology reads the old `SigmaTopologyController` diagnostics, not streaming
  transition counters, so it can display zeros while the streaming graph performs
  millions of evaluations.
- `GPU witness=1` is the exact backend capability/parity witness, not proof of
  topology or scan progress.
- Section-44 ingress/canonical/derived milliseconds are submission-to-completion
  wall latency, including queueing, not per-kernel GPU duration.
- Diagnostic readback is non-authoritative telemetry, not a CPU scheduler, and
  counters read from multiple buffers do not form one atomic snapshot.

## Memory closure

Memory pressure is not the root cause of this empty scan, but the current footprint
is already a future long-session risk.

Exactly attributable persistent Sigma buffers:

| Allocation group | MiB |
| --- | ---: |
| Streaming arena and scratch | 25.374 |
| Constraint ledger total | 57.182 |
| Topology caches | 10.001 |
| Direct readout buffers | 4.127 |
| Decoded carrier | 32.000 |
| **Known exact Sigma buffers** | **128.684** |

The device reported approximately 1.069 GiB of GL allocations, 1.65 GiB total PSS
and 1113.7 MiB allocated GPU telemetry. The remainder is dominated by per-pixel
MRT/LUT/ring textures, XR swapchains, camera/passthrough buffers, driver caches and
alignment.

Known depth-dependent textures scale as approximately:

```text
856 * depthPixelsPerEye bytes
```

The device log does not state the exact depth resolution, so no exact total may be
derived from it. Memory scales independently with page capacity, transaction
capacity, raw-evidence capacity, capture resolution/ring depth and historical
revalidation targets.

## Falsified or refined claims

- **Falsified:** stable scan loses active lane 0.
- **Falsified:** readout inherently needs a second publication.
- **Falsified:** all UI topology counters reflect the streaming transition graph.
- **Falsified:** `_pendingIngress` is unlimited safe retention.
- **Falsified:** `SKIPPED_ADMISSION` is harmless telemetry.
- **Refined:** the audited live graph has two descriptor binding holes and one
  silent pose-matrix contract hole; no additional descriptor omissions were found
  across the production streaming kernels.
- **Refined:** Unity's exact internal missing-binding dispatch behavior is not
  assumed. The conclusion relies on the device errors, unavailable required
  writes, later dispatch execution and observed state progression.
- **Refined:** this capture remained near 72 compositor FPS. The proven failure is
  36-second canonical-result latency and empty state, not the 12-15 FPS behavior
  observed in an earlier run.

## Required regression gates before another Quest build

The binding test must execute production code, not maintain a parallel test bind
table:

1. instantiate a real `SigmaStreamingGraph` with minimal valid production
   resources;
2. invoke its actual initialize, ingress, canonical and derived recorders;
3. seed real indirect counts so every live kernel executes at least one minimal
   group;
4. complete through the production nonblocking completion ticket on Vulkan;
5. fail on any `Compute shader ... Property ... is not set` message.

Required regressions:

1. complete inverse constraint-ledger binding;
2. complete scheduler/probation binding;
3. skipped or faulted Meet cannot advance or publish;
4. transaction-slot reuse cannot consume old-generation scratch;
5. zero accepted evidence cannot create a novel published Psi generation;
6. unused page references cannot alias slot 0;
7. publication creates the matching visible/ReadoutDirty generation immediately;
8. non-zero pose extraction has both pose-consume matrices;
9. admission pressure preserves every coherent source losslessly;
10. proof partition/interleaving remains bit- and validity-identical;
11. full Vulkan EditMode and Android Release shader inventory compile cleanly;
12. only then perform the next Quest install and physical scan.

## Closure state

S4-08.3 is not device-acceptable at this audited revision. The 16D ontology and
exact carrier model survived the audit. The failures are in execution ownership,
state progression, page-handle validity, ingress retention and scheduling
granularity.
