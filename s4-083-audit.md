# S4-08.3 current-device forensic closure

Date: 2026-08-24 (Europe/Prague)
Audited/deployed commit: `c2720b51637643bb04eef16894d7dd9bf9702720`
Package: `com.questinfinitescan.smoke`

## Scope

This report closes the current tilted 5 x 5 carrier-board, duplicate-page,
missing-side-coverage and eventual hard-stall device run. It is source- and
device-evidence based. It does not authorize a second geometry world or a weaker
canonical decision path.

The only canonical world remains:

```text
Psi : Sigma_2 -> S16
```

Page layout, image blocks, prediction targets, association slots, proof windows,
raw residency and the temporary XR preview are execution/readout mechanisms only.

## Verdict

The renderer is not hiding a completed room scan. It faithfully draws every
manifest-current page, and the canonical stream itself contains only a slowly
published, repeatedly allocated initial-view footprint.

The visible failure is the composition of three confirmed P0 defects:

1. A 320 x 320 frame is deterministically compacted into a 5 x 5 array of 64 px
   gauge candidates. Every unmatched frame can assign those same image origins
   fresh Morton logical coordinates before the previous candidates become visible
   to prediction. The second 5 x 5 pass therefore overlays new logical pages rather
   than advancing the first set.
2. The 64-slot resident bundle arena is used as acquisition capacity. It fills in
   roughly three all-unmatched frames. Candidates from later head directions are
   counted as skipped but are not sealed anywhere before the capture frame is
   released.
3. After 4096 completed proof blocks the durable raw allocator reaches the end of
   its second 4096-tile half. It never reuses, segments or spills. One proof owner
   then repeats the same failed `EMIT_RAW` quantum forever, stopping all further
   publication.

This explains the complete device observation:

```text
exact 5 x 5 initial-view board
    -> restart at tile 1 with different page colours
    -> overlap and increasing preview cost
    -> no newly turned side/behind coverage
    -> permanent publication stall
```

## Evidence

### Second-run visual oracle

The Quest gallery frames at 03:30 show exactly five equal page domains across the
top row and subsequent equal rows below it:

- `com.questinfinitescan.smoke-20260824-033007.jpg`
- `com.questinfinitescan.smoke-20260824-033037.jpg`
- `com.questinfinitescan.smoke-20260824-033046.jpg`

The screenshots are retained under `/tmp/sigma-second-run-gallery/Screenshots/`
and in the final evidence archive.

The matching device telemetry is:

```text
diag.publication = 27
draw             = 663552 vertices
663552 / 24576   = 27 current pages
```

Thus the exact 25-page first footprint plus two pages from the next pass are all
manifest-current simultaneously. They are not retired generations of the same
logical pages and they are not a preview-only duplicate.

The first device run supplies the same equality at 28 pages:

```text
diag.publication = 28
draw             = 688128
688128 / 24576   = 28
```

### Discrete 5 x 5 derivation

`SigmaInverseController.cs:305-308` computes the gauge block resolution as:

```text
ceil(depthWidth / 32), ceil(depthHeight / 32)
```

For the live 320 x 320 depth stream this is 10 x 10 blocks.

`SigmaInverseWorkGraph.compute:326-340` groups 2 x 2 blocks into one candidate:

```text
gaugeWidth  = (10 + 1) >> 1 = 5
gaugeHeight = (10 + 1) >> 1 = 5
```

The visual 5 x 5 board is therefore a direct image-domain fingerprint of the
canonical gauge admission path.

## Confirmed root causes

### P0-1 — fresh logical gauge allocation repeats the pending camera footprint

`SigmaStreamIngress.compute:135-194` first marks any predicted current page active,
then independently marks each 32 x 32 block unmatched when stereo does not prove
the same predicted first hit.

`SigmaInverseWorkGraph.compute:320-351` compacts those blocks into the 5 x 5 gauge
candidate grid. For each admitted gauge candidate, `:436-443` performs:

```text
gaugeOrdinal = StreamGaugeOrdinalBase + gaugePrefix
coordinate   = SignedMorton(gaugeOrdinal)
imageOrigin  = imagePage * 64
```

The image origin repeats on the next frame, but the logical coordinate does not.

The existing probation path does not prevent this initial duplication.
`SigmaBundleSealsAlone()` at `:525-531` lets any dual-depth or depth+RGB bundle seal
alone. Consequently the first all-unmatched frame can immediately create 25
independent gauge transactions; subsequent frames can create another 25 while the
first set is still invisible to published-carrier prediction.

After pages do become visible, one frame may still generate both:

```text
MATCHED work for pixels with a proven same first hit
+
fresh GAUGE work for other 32 x 32 blocks that missed that association
```

The captured arena contains both classes (37 MATCHED, 27 GAUGE), but the active
forensic samples are older GAUGE promotions. Therefore the current feedback loop is:

```text
slow pending/publication path
    -> published Psi remains incomplete/stale
    -> same-first-hit misses remain common
    -> new unmatched blocks receive new Morton identities
    -> old gauge tickets and duplicate pages increase backlog
    -> compatible MATCHED updates wait
    -> published Psi remains incomplete/stale
```

This is not S4-07 gauge refinement. Different preview hues identify different
logical page coordinates; generation changes only tone/point size.

### P0-2 — resident bundle exhaustion drops later directions

`SigmaInverseWorkGraph.compute:354-390` clamps admitted work to
`StreamFreeBundleCount`. Anything else only increments:

```text
SKIPPED_ADMISSION
PENDING_INGRESS_EXHAUSTION
```

Only selected lanes at `:397-475` receive a bundle slot and owned raw range. The
unselected candidate has no immutable sealed payload when the ingress command and
capture lease finish.

The current run reached:

```text
56 READY + 8 ACTIVE = 64/64 resident bundles
lifetime admission  = 91 = 27 completed + 64 resident
```

After saturation the headset continued producing thousands of coherent frames, but
observations from side and rear head directions could not enter canonical work.
Re-observing something similar later is not retention of the original evidence.

The current ingress cursor no longer advances when admission is exactly zero; that
older defect is fixed. Evidence is still lost because the transient capture frame
is released without an owned seal for unselected candidates.

### P0-3 — deterministic 4096-block raw-retention hard stall

The live raw capacity is 8192 tiles. Graph initialization reserves tiles 0..4095
for 64 bundle slots x 64 transient proof blocks and sets `_RawAllocator[0] = 4096`
at `SigmaInverseWorkGraph.compute:245-246`.

`SigmaStreamProof.compute:1645-1786` is the durable retention path. At `:1691-1701`
it only performs:

```text
cursor = RawAllocator[0]
if cursor < RawTileCapacity:
    destination = cursor
    RawAllocator[0] = cursor + 1
else:
    memory.z++
```

It never searches `_RawLiveWords`, selects another segment or persists a residue.
When allocation fails, `RawCopyActive` stays zero (`:1725`) and neither
`closure.sourceCursor` nor transaction state advances. `CompleteProofBlock` cannot
run because closure never reaches `CLOSED`; the singleton proof owner remains held.

Device fingerprint:

```text
diag.proof       = [4097 reduced, 4096 closed, 0, 0]
proofOwner       = one fixed transaction
work             = FINALIZE_PROOF_BLOCK repeatedly
diag.memory.z    = monotonically increasing
diag.publication = frozen (27 or 28 by run)
```

This is a hard session ceiling, not a renderer problem and not association
deadlock.

### P0-4 — the 12-candidate proof window still has no continuation

`SigmaStreamProof.compute:692-704` marks `PROOF_SPILLED` when the next source-class
records would reach the bounded candidate capacity. No production opcode consumes
that flag or continues the journal in another window.

It did not trigger the captured device stall, but it remains a canonical-cap bug:
12 records may be an execution window only. Complete sealed evidence must be
accumulated losslessly, then stable coalescing and reverse-order redundancy must run
to fixed point independently of partitioning.

## Latency and load

### What is proven

- Publications in captured windows arrive roughly one every 8-19 seconds; the
  initial result was observed only after tens of seconds.
- One page requires 256 16-sample inverse microtiles, 64 source reductions, 64
  proof closures, 8192 intrinsic edge records, the 168-action annihilator catalog,
  associator closure, revalidation and atomic publication.
- The scheduler has eight transaction slots and one proof owner. The run ends with
  seven transactions waiting behind the owner stalled in raw retention.
- At 27 pages the temporary preview submits 27 x 4096 point billboards = 110592
  points / 663552 vertices every XR frame. Duplicate pages therefore increase
  preview cost linearly and explain the observed progressive FPS degradation.
- KGSL sampling during the run showed approximately 91% gpubusy at 456 MHz. The
  device must not be treated as idle and budgets must not be multiplied blindly.

### Current scheduler facts

Current `c2720b5` records eight canonical rounds and, unlike the prior `c887ad5`
audit, refills static deficits in every round (`SigmaStreamingGraph.cs:279-285`).
Therefore the older claim that REVALIDATE must wait three whole submissions is no
longer true.

The generated profile nevertheless exposes expensive work:

```text
EVALUATE_MICROTILE       88 tokens, 158 barriers
REDUCE_SOURCE_BLOCK      21 tokens
FINALIZE_PROOF_BLOCK     26 tokens
TRANSITION_ANNIHILATOR   46 tokens, 168 witnesses
TRANSITION_ASSOCIATOR     7 tokens
REVALIDATE              578 tokens, modelled 3 MiB read + 1 MiB write
```

Every round records scheduler, dormant stages, historical revalidation, five
inverse stages, reduction, eight proof stages and transition stages. Indirect zero
work may make many of these no-ops, but command/marker cost and actual GPU cost must
be measured rather than inferred from the final-round `work={...}` snapshot.

### Timing instrumentation gap

The deployed Release APK reports:

```text
Sigma gpu-kernels ... total=0.000ms blocks=0 kernels=0
```

This is not proof of idle kernels. Ninety-eight production compute entrypoints are
registered through the profiled dispatch wrappers, but Unity profiling was disabled
when the recorders were created. Raster work is also absent from the current kernel
registry: prediction surface/contact, historical revalidation raster and direct XR
preview draw.

The next diagnostic APK must produce non-zero per-stage GPU timestamps or explicitly
report instrumentation failure. Timings may validate a static Quest execution
profile; they must never decide canonical mutation.

## Preview and tracking findings

- The preview draws all manifest-current pages. There is no hidden surrounding
  carrier clipped to two pages.
- Local point geometry tracks recognizable room contours, so a gross head-locked
  transform is not the primary scan failure. The finite tilted board is the actual
  early-view canonical coverage.
- After sleep/wake the disposable preview is temporarily displaced, then re-aligns
  while publication count remains unchanged. That transient follows Meta tracking
  recovery. Psi must not be mutated to chase it; preview should be hidden/marked
  until tracking is stable.
- The temporary preview is diagnostic only and will be replaced by S4-11 meshlets.
  It must not be used to conceal duplicate logical pages.

## Falsified or corrected hypotheses

- **Renderer cap/head-lock is the primary defect:** falsified. Draw count exactly
  equals all current pages and local parallax is world-consistent.
- **Prediction is triangle-only:** falsified. The live renderer executes the
  contact-footprint prediction pass as well as the surface pass.
- **The coloured layers are merely newer generations:** falsified for these runs.
  Current-page count rises one-for-one with publication count and no replacement is
  observed.
- **The captured hard stall is the 12-candidate spill:** falsified. It is the raw
  allocator at 4096 durable blocks. The 12-window gap remains latent.
- **The current build refills only scheduler round zero:** outdated. `c2720b5`
  refills all eight rounds.
- **Quest is obviously under-loaded:** not supported. KGSL reports high gpubusy;
  per-kernel timings are unavailable.

## Exact causal graph

```text
coherent 320 x 320 stereo depth
    -> no/partial current prediction
    -> 10 x 10 unmatched 32 px flags
    -> 5 x 5 candidate 64 px pages
    -> dual-depth bundle seals alone
    -> each candidate receives fresh Morton logical coordinate
    -> 25 first-frame pages enter slow exact work
    -> later frames repeat the same image origins with new coordinates
    -> 64 resident bundles fill before current Psi catches up
    -> side/rear observations are not sealed
    -> old GAUGE work publishes slowly; MATCHED updates wait
    -> preview faithfully shows a duplicated initial-view board
    -> retained raw cursor consumes slots 4096..8191
    -> block closure 4097 cannot allocate
    -> proof owner repeats forever
    -> no more publication or directional coverage
```

## Minimal ontology-preserving repair contract

This is one closure run, not a new mapper. Reuse the existing probation,
association, source-handle, raw-ledger, manifest and scheduler structures.

### P0-A — exact pending-or-published association before fresh gauge allocation

Fresh Morton allocation is legal only after exact association has failed against:

1. a compatible manifest-current Psi page; and
2. a compatible pending candidate/probation handle from the same sealed dependency
   domain.

Image origin may propose a lookup but may not become canonical identity. The exact
Q16.48 admissible-cell/first-hit path decides compatibility. A compatible source is
attached as a dependent source/MATCHED update using the existing logical coordinate;
it does not receive another gauge ordinal.

Do not solve this by XYZ proximity, colour-key deduplication, hiding layers or
raising the bundle capacity.

### P0-B — seal evidence before resident admission

The 64 bundle records are execution residency, not evidence ownership. Before the
capture/prediction leases are released, every coherent candidate must have an owned
source/raw handle. Admission may decide when that handle receives a resident bundle,
never whether it survives.

Reuse the existing source-handle segments and constraint-ledger raw payload. Do not
add a parallel frame/world database. If durable capacity is unavailable, retain the
owned handle and expose backpressure; do not increment a skip counter and discard
the observation.

### P0-C — make raw retention segmented/reclaimable and fail closed

Replace the monotonic raw cursor with deterministic generation-safe allocation over
the existing live bitmap/segments. Retired residues become reusable. When no
resident slot exists, the transaction enters a residency/persistence wait state,
releases the singleton proof owner and preserves its source cursor. It must not
spin, advance or publish partially.

S4-10 persistence provides lossless overflow for truly long sessions; the live
contract must already treat GPU raw slots as a cache, never as a 4096-block
canonical/session cap.

### P0-D — continue proof beyond the 12-record window

`PROOF_SPILLED` must schedule the next bounded journal window. Only after the entire
sealed source stream is accumulated may stable ordering, coalescing and reverse
redundancy reach fixed point. Window size, token budget and interleaving must not
change Psi, validity, proof order or provenance.

### P1 — service profile and preview

After P0 correctness, use measured GPU timestamps to establish one static Quest
profile that prioritizes completion of old/commit-ready work and prevents compatible
MATCHED updates from starving behind repeated GAUGE allocations. Preserve ticket
dependencies and bounded submissions; do not use runtime timings as canonical
decision input.

The disposable preview should colour logical coordinate, generation and
MATCHED/GAUGE path distinctly and suppress presentation during invalid wake
tracking. This is diagnostics, not a second reconstruction.

## Expected implementation size

Because the necessary records and ownership paths already exist, this should remain
a focused closure rather than a subsystem rewrite:

```text
pending/published association and gauge reuse      80-150 production LOC
raw allocation/wait/reclaim contract               80-140 production LOC
lossless ingress ownership handoff                100-200 production LOC
proof-window continuation                          80-160 production LOC
diagnostics/binds/tests                            150-250 LOC
```

Estimated production change: roughly 340-650 LOC plus focused tests. A smaller
hotfix can make the current room appear to work, but cannot satisfy the no-drop,
no-cap and week/city-session invariants.

## Required gates before another Quest build

1. A stationary scene over hundreds of frames produces at most one logical 5 x 5
   footprint; later evidence advances/replaces those logical pages instead of
   allocating another coloured grid.
2. Start, +90 degrees, -90 degrees and 180 degrees all become owned sealed work and
   eventually manifest-current Psi despite a full resident arena.
3. `SKIPPED_ADMISSION` cannot represent discarded evidence; capture lease release
   requires proof of owned sealing.
4. More than 4096 retained proof blocks complete without a fixed cursor stall;
   raw pressure releases proof ownership and resumes after reclaim/spill.
5. More than 12 canonical proof candidates across arbitrary partitions yields the
   same bits, validity, minimal proof and provenance as one-shot reference.
6. Compatible MATCHED evidence is serviced and creates a generation replacement;
   `new gauge while compatible visible/pending page existed` remains zero.
7. Draw current-page count equals manifest-current pages and retired generations do
   not remain visible.
8. Vulkan timings report every production compute and raster stage with non-zero
   executed samples; timing failure is explicit.
9. Wake invalid/recovering tracking does not mutate Psi and does not present a
   misleading displaced preview.

## Evidence inventory

Primary logs:

- `/tmp/sigma-c2720b5-live.log`
- `/tmp/sigma-c2720b5-window.log`
- `/tmp/sigma-current-tail2.log`
- `/tmp/sigma-second-run-evidence/device-full.log`
- `/tmp/sigma-second-run-evidence/device-filtered.log`

Primary images/video:

- `/tmp/sigma-second-run-gallery/Screenshots/com.questinfinitescan.smoke-20260824-032834.jpg`
- `/tmp/sigma-second-run-gallery/Screenshots/com.questinfinitescan.smoke-20260824-032846.jpg`
- `/tmp/sigma-second-run-gallery/Screenshots/com.questinfinitescan.smoke-20260824-033007.jpg`
- `/tmp/sigma-second-run-gallery/Screenshots/com.questinfinitescan.smoke-20260824-033037.jpg`
- `/tmp/sigma-second-run-gallery/Screenshots/com.questinfinitescan.smoke-20260824-033046.jpg`
- `/tmp/sigma-live-angle/current-motion.mp4`
- `/tmp/sigma-wake-evidence/`

Legacy `com.fladirmacht.voxelscanner-*` captures are explicitly excluded.
