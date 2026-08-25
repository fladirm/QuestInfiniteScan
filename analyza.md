# Σ‑PRISM‑16 v8 — native S16/Merkaba closure audit and replacement map

**Status:** canonical-rebase analysis for S4‑08 closure
**Audited source:** `cac9ab012f4ce574e5eb9bee88290982fd9c4fe8`
**Device evidence:** `SigmaPrism16-cac9ab0-device-20260825-070954` and
`SigmaPrism16-cac9ab0-device-20260825-072119`
**Previous normative baseline:** `CPQ4-2026-08-24-S16-v7`
**Replacement baseline prepared by this analysis:** `CPQ4-2026-08-25-S16-v8`

This document is the forensic and implementation bridge between the measured v7
runtime and the v8 native-S16 specification. It is not a second specification.
Whenever prose here and `new_spec.md` differ, `new_spec.md` is authority.

---

## 1. Executive verdict

The current implementation stores the right canonical type,

\[
\Psi:\Sigma_2\rightarrow S16,
\]

and it already has valuable exact Q16.48 algebra, signed-XOR tables, carrier
storage, immutable root publication and a usable Release timestamp path. The main
failure is one level above those primitives: the runtime still behaves as a
classical RGB-D scanner that happens to encode its result in S16.

The live physical flow is currently:

```text
four sensor-specific cell builders
→ raster/pending proposal kinds
→ provider × segment candidate evaluation
→ global target sort/reduction
→ image-edge topology universe
→ separate annihilator/associator closure
→ pixel-shaped NOVEL placement
→ page mapping/publication
→ page-neighbour readout
```

That decomposition is not merely verbose. Several stages possess physical
authority they cannot have:

- a wrong eye pixel can remove a valid pending preimage;
- a one-winner raster collapses the admissible pending fibre set;
- an unconditional `NOVEL` proposal can mint identity before candidate exhaustion;
- depth conditions the domain from which the RGB cell is constructed;
- an XYZ interval overlap decides whether an S16 transition may exist;
- `CURRENT↔CURRENT` changed transitions are omitted by proposal kind;
- a frame pixel bounding box defines new carrier coordinates;
- physical backing segments are visible to halo readout;
- append-only physical windows make cost grow with revision number.

The correct replacement is not another manager or another graph. It is one
fingerprinted, bracket-preserving native relation descriptor evaluated in both
directions:

\[
\boxed{
S16
\xrightarrow{E_{22}}
\mathcal V_E
\xrightarrow{\mathcal M}
\text{local manifestation}
\xrightarrow{\Pi_q}
\text{sensor/readout shadow}
}
\]

and scan is the exact inverse-image pullback of that same mapping:

\[
\boxed{
\mathcal A_{q,p}
=
(\Pi_q\circ\mathcal M\circ E_{22})^{-1}(O_{q,p}).
}
\]

The 22 Merkaba relations are an overcomplete atlas of one 16-dimensional state.
They are not 22 scalar state channels and are not a second canonical world. The
same descriptor also generates intrinsic neighbour stitching. Geometry,
appearance and topology cease being separate solvers; they are different
observables and compatibility strata of one native closure.

---

## 2. Viewpoint of a native 16D observer

A 3D scanner normally treats the sensor world as primary and asks how to encode
its reconstructed XYZ/RGB result. That is the wrong direction for this product.

The native world is S16. A camera frame is a lossy drawing of that world, analogous
to a 2D engineering drawing of a 3D object. A native 16D observer does not elevate
the drawing into reality. It asks which native states could have produced it.

For a carrier germ `ξ`:

\[
s_\xi=\Psi(\xi)\in S16.
\]

The camera never directly supplies `sξ`. It supplies a finite-footprint shadow
set `Oq,p`. The information content of that observation is exactly its preimage:

\[
\mathcal A_{q,p}
=
\{s\in S16\mid \mathcal S_{q,p}(s)\in O_{q,p}\},
\qquad
\mathcal S_{q,p}=\Pi_{q,p}\circ\mathcal M\circ E_{22}.
\]

A revisit does not add a temporal correction vector. It intersects another shadow
preimage with the same native admissible region:

\[
\mathcal A^{n+1}_\xi
=
\mathcal A^n_\xi\cap\mathcal A_{q,t,\xi}.
\]

What one eye cannot see remains a member of the observation fibre. It is not zero,
empty or disposable. The canonical state is selected by native minimum change,
which preserves the prior along every unconstrained fibre direction.

The reverse readout is equally ordinary from this viewpoint: an eye image, a
scanner prediction and a textured GLB are three different drawings of the same
native state. A cheap drawing may discard information. The world may not.

---

## 3. Exact native mathematical model

### 3.1 Canonical state

\[
\boxed{\Psi:\Sigma_2\rightarrow S16}
\]

- `Σ₂` is a sparse, logically unbounded intrinsic carrier.
- `Ψ(ξ)` is the complete native local state.
- Q16.48 coefficients are canonical.
- pages, segments, pixels, mesh vertices and 3D positions are not identity.
- proof/certificate data justifies inference but is not a second physical world.

### 3.2 Native relation descriptor

The v8 generator freezes one self-contained descriptor

\[
\mathcal D_M=(\mathcal A_{S16},E_{0..21},\mathcal M,
\Pi_q,\mathcal T,\mathcal Z,\mathcal B,\Delta),
\]

where:

- `A_S16` is the exact signed-XOR Cayley-Dickson algebra;
- `Ek` is one exact, explicitly bracketed Merkaba relation expression;
- `M` maps the consistent 22-relation atlas to a local manifestation;
- `Πq` is a calibrated sensor/readout shadow operator;
- `T` is intrinsic transition transport;
- `Z` is the generated zero-divisor/annihilator stratum test;
- `B` is the associator/bracket-context plan;
- `Δ` is the deterministic native minimum-change selector.

The exact TOE/E22 expressions are not invented in scanner code. They become
canonical only when imported into this descriptor, emitted as generated C#/HLSL,
and frozen by a semantic fingerprint plus forward/inverse/stitch oracle parity.
There is no runtime dependency on an external repository.

### 3.3 Overcomplete relation atlas

Define

\[
E_{22}(s)=(E_0(s),\ldots,E_{21}(s)),
\qquad
\mathcal V_E=E_{22}(S16).
\]

`V_E` is a 16-dimensional consistency image embedded in the product of 22 relation
spaces. The 22 records may not be independently mutated. A valid edge tuple is one
that has a shared S16 preimage and passes the descriptor consistency identities.

This prevents replacing S16 with a 22-channel tensor while still exposing the
relations needed by manifestation, inverse and stitching.

### 3.4 Forward shadow

For sensor/readout query `q`, finite footprint `p`, sub-carrier coordinate `δ` and
direction `ω`:

\[
\mathcal S_{q,p}(s)
=
\Pi_{q,p}\bigl(\mathcal M(E_{22}(s);\delta,\omega)\bigr).
\]

`Πq,p` includes calibration, finite footprint integration, support, direct order,
first-hit visibility and optical response. A 3D manifestation may be emitted as a
readout, but the scan contractor is generated from `S` directly and does not need
to persist or canonize that 3D intermediate.

### 3.5 Inverse shadow pullback

For observation region `Oq,p`:

\[
\mathcal A_{q,p}=\mathcal S_{q,p}^{-1}(O_{q,p}).
\]

This is not `s=M^{-1}v`, a matrix inverse or a pseudoinverse. The generated
contractor traverses the exact forward expression DAG in reverse while preserving
every bracket node. For example, if

\[
E_k(s)=(a_ks)b_k,
\]

the contractor may not silently replace it by `a_k(sb_k)`.

The persistent exact evidence is a conjunction of native relation inequalities,
not necessarily one axis-aligned 16D box. A bounded contractor may conservatively
overapproximate an intermediate region, but it may commit only a Q16.48 state that
forward-verifies every original relation. Inability to decide yields `UNRESOLVED`,
never a false accept or false conflict.

### 3.6 Independent observation meet

For all independent sources/views associated with germ `i`:

\[
\mathcal C_i
=
\mathcal C_{prior,i}
\cap
\bigcap_{q,p}\mathcal A_{q,p,i}
\cap
\bigcap_{j\in N(i)}\mathcal N_{ij}.
\]

Source independence is retained by certificate identity. Intersections are exact
logical conjunctions. Confidence changes the observation region width; it never
becomes a weighted sum.

### 3.7 Native minimum change and hidden-mode preservation

\[
s'_i
=
\operatorname*{argmin}_{s\in\mathcal C_i\cap Q48^{16}}
\Delta_{\mathcal D_M}(s,s_i),
\]

with generated deterministic tie-breaking.

For a linear descriptor branch with row operator `R`, this implies

\[
P_{\ker R}(s'_i-s_i)=0.
\]

For nonlinear/bracketed relations the stronger general rule is fibre invariance:
if two states are indistinguishable under all new constrained relations, the
selector retains the prior representative on that equivalence fibre. An
observation cannot erase a native direction it did not constrain.

### 3.8 Intrinsic Merkaba stitching

For intrinsic carrier neighbours:

\[
\tau_{ij}=\overline{s_i}s_j.
\]

The same descriptor generates transport/compatibility of all relevant relation
modes:

\[
\mathcal N_{ij}
=
\bigcap_{k=0}^{21}
\operatorname{Compat}_k
\left(E_k(s_j),\mathcal T_k(\tau_{ij})E_k(s_i),e_{ij}\right).
\]

The evidence relation `eij` carries first-hit/order support. Outcomes are:

```text
REGULAR       relation atlas closes under regular transport
SINGULAR      supported relation enters stable ZD/nonassociative stratum
NO_RELATION   first-hit/native relations prohibit a stitch
UNRESOLVED    available evidence cannot decide
```

Topology is this stitchability classification. It is not an XYZ classifier and
not a separate graph world. A generation-keyed stitch cache is disposable.

---

## 4. Device evidence and measured baseline

Both evidence bundles identify the same source commit and APK hash. Run 1 provides
the authoritative one-shot kernel sample. Run 2 provides the clean long-run
carrier exhaustion proof.

| Quantity | Measured value | Interpretation |
|---|---:|---|
| Source | `cac9ab012f4c` | exact audited source |
| APK SHA-256 | `626e81e…effb8869` | same binary in both runs |
| Sampled dispatches | 266 | one canonical submission |
| Named kernels | 61 | current physical lowering |
| Timestamp valid bits | 48 | usable Vulkan timestamps |
| Compute checksum | 4520.946 ms | sum of paired dispatch timestamps |
| First sampled `wall.direct` | 4776.70 ms | diagnostic-instrumented submission |
| Unattributed wall gap | 255.754 ms | queue/events/non-compute around sample |
| Root progress run 1 | 1 → 30 | pending frame-cap cliff removed |
| Root progress run 2 | 1 → 31 | then fail-closed carrier exhaustion |
| Revision 32 fault | `0x20 + 0x100 = 0x120` | missing pairs 34, free pairs 25 |
| Initial pending backing | `0/102400` | one frame-sized initial segment |
| Run 2 pending backing | `201391/303791` | logically grows, physically fragmented |
| Pending promotion holes/reuse | `0 / 0` on every sample | lifecycle not physically demonstrated |
| Wall-latency slope | about +99…109 ms/revision | shortfall segment multiplication |
| Run 2 PSS delta | +55,895 KiB | bounded source journals; pending backing dominates growth |
| Thermal state | 0 | slowdown is not thermal throttling |

The timestamped submission is diagnostic-contaminated and is not a final
production wall-time baseline. Its per-kernel ordering and relative cost are,
however, directly measured and internally consistent: compute explains 94.64% of
the measured wall interval.

---

## 5. Current pipeline stage matrix

| Current stage | Inputs | Output/authority | Measured GPU time | Correct native content | Defect | v8 disposition |
|---|---|---|---:|---|---|---|
| Normalize + pose | coherent depth/RGB, Meta pose | normalized samples, corrected query gauge | 0.291 ms | calibration/pose remain readout gauge | none shown by this sample | keep as infrastructure |
| Pending projection | persistent pending state, sensor poses | one depth winner per eye pixel | 0.226 ms | disposable forward candidate proposal | wrong right-eye lookup later; one winner destroys fibre coverage | replace by multi-fibre sensor shadow index |
| Sensor inverse | four images, prediction, pending, carrier segments | source cells and candidate results | 1844.923 ms | exact Q48 constraint pullback intent | modality-specific worlds, depth-conditioned RGB domain, provider×segment repetition | replace by one generated inverse-Merkaba contractor |
| Target sorts | candidate/target records | deterministic target order | 83.751 ms | deterministic same-germ reduction is required | full-frame global order is an avoidable lowering | replace by one native-germ owner/compacted reduction |
| Target finalization excluding sort | ordered sources | reduced state/cells | 138.644 ms | exact same-germ conjunction and min-change | current mass is wrong; provisional coordinate boxes lose native relation form | replace by native region close + forward verification |
| Topology closure | optical image edges, reduced states | regular/singular labels and pending unions | 2339.724 ms | exact transition/ZD/associator algebra is valuable | wrong domain and authority; full optical universe; no cache; misses CURRENT↔CURRENT | replace by dirty intrinsic `Stitch22` cache misses only |
| Pending lifetime | pending outcomes/backing | slots, labels, persisted pending | 94.772 ms | unresolved evidence must survive | serial allocator, pixel local chart, fragmented growth, no observed reuse | replace by latent relation journal keyed by native seed/evidence |
| Publication excluding second sort | changed targets, carrier pairs | shadow pages + root | 18.615 ms | root-last immutable publication is correct | global pixel bbox, duplicated map/scan machinery, finite residency | retain semantics; replace lowering with touched-page sparse commit |

The top three current kernels consume 4103.979 ms, or **90.78%** of measured
compute. They correspond exactly to the three physics splits v8 removes:
separate topology, separate RGB construction and provider×segment candidate solve.

---

## 6. Complete measured kernel matrix

`Fate` means the production kernel name/role after v8 cutover, not whether its
underlying exact primitive survives.

| Rank | Kernel | Dispatches | Total ms | Share | What it computes | Correctness | Fate |
|---:|---|---:|---:|---:|---|---|---|
| 1 | `ClosePendingEdges` | 1 | 2328.958 | 51.51% | optical-edge τ, 168 annihilator actions, conditional associator | exact algebra inside; wrong/full domain and XYZ claim gate | delete; `StitchNative22` dirty misses only |
| 2 | `BuildRgbSourceCells` | 2 | 937.364 | 20.73% | RGB interval contraction over 16 coordinates | independent-source domain not proven; depth-conditioned | delete; generated sensor pullback |
| 3 | `EvaluateCandidateMeets` | 11 | 837.657 | 18.53% | candidate × provider/segment cell meet/lift | exact meet intent; incomplete candidates and repeated whole footprint | delete; one native germ owner |
| 4 | `FinalizeReducedTargets` | 11 | 131.996 | 2.92% | reduced target reconstruction/mass | mass formula violates v7 §13.1 | replace by `ConstrainNativeGerms` finalizer |
| 5 | `AllocatePendingRetention` | 1 | 90.779 | 2.01% | serial persistent pending allocation | backing only, but O(N) and append behaviour | delete with latent journal lowering |
| 6 | `BuildDepthSourceCells` | 2 | 65.972 | 1.46% | finite depth cone → XYZ rows | finite footprint useful; hardcoded HIT and 3D-first pullback wrong | delete; sensor relation pullback |
| 7 | `MergeTargetTails` | 18 | 31.509 | 0.70% | groupshared bitonic tails | deterministic but unnecessary after owner cut | delete |
| 8 | `MergeTargetStage` | 90 | 26.429 | 0.58% | global bitonic stages | deterministic but unnecessary after owner cut | delete |
| 9 | `SortTargetBlocks` | 2 | 25.812 | 0.57% | 256-record block sort | deterministic but unnecessary after owner cut | delete |
| 10 | `ScatterSegmentTargets` | 9 | 7.141 | 0.16% | changed states into shadow pages | correct sparse scatter semantics | fold into `ScatterChangedGerms` |
| 11 | `PrepareSegmentPages` | 9 | 6.997 | 0.15% | clone/init destination generations | correct immutable shadow concept | fold into sparse page preparation |
| 12 | `GatherEdgeEndpoints` | 1 | 6.393 | 0.14% | full optical-edge endpoint gather | backing/window workaround | delete |
| 13 | `ReduceTargetWindow` | 1 | 5.673 | 0.13% | exact equal-target meet | semantic reduction required | fuse into one-owner germ closure |
| 14 | `PersistPendingTargets` | 1 | 3.428 | 0.08% | pending state/bounds/generation | reduced cell is not complete evidence | replace by latent evidence journal |
| 15 | `BuildFrameProposals` | 1 | 2.330 | 0.05% | CURRENT L/R, one PENDING, unconditional NOVEL | source-proven association defects | delete |
| 16 | `ApplyPendingEdges` | 1 | 1.934 | 0.04% | union labels/link current anchor | proposal topology becomes identity | delete |
| 17 | `AssignPagePairs` | 9 | 1.361 | 0.03% | map requests to physical current/shadow pairs | storage only | simplify/fuse |
| 18 | `RelaxPendingLabels` | 18 | 0.967 | 0.02% | fixed image component propagation | image-domain chart authority | delete |
| 19 | `ClearFrameState` | 1 | 0.897 | 0.02% | clear frame-wide scratch | oversized old ABI | replace by compact counters/indirect args clear |
| 20 | `CompactResolvedTargets` | 1 | 0.703 | 0.02% | accepted footprint target compaction | useful work compaction | replace by native relation work compaction |
| 21 | `AccumulateNovelBounds` | 1 | 0.664 | 0.01% | global image bbox | canonical gauge violation | delete |
| 22 | `BuildPendingEdges` | 1 | 0.619 | 0.01% | full horizontal/vertical optical edges | wrong topology domain | delete |
| 23 | `ClearPublicationState` | 1 | 0.410 | <0.01% | publication scratch/fault bridge | root-last infrastructure useful | simplify |
| 24 | `MarkPromotedPendingSlots` | 1 | 0.364 | <0.01% | reusable pending bitmap | backing mechanism only | delete with latent journal |
| 25 | `PropagatePageMappings` | 1 | 0.359 | <0.01% | scatter page mappings to targets | second mapping graph | delete/fuse |
| 26 | `ClearTargetReduction` | 1 | 0.335 | <0.01% | reduction buffers/counters | old global circuit | delete |
| 27 | `MapFrameTargets` | 1 | 0.289 | <0.01% | CURRENT/NOVEL/CONTINUATION → carrier address | pixel delta/global bbox authority | delete |
| 28 | `DeferUnresolvedEdges` | 1 | 0.279 | <0.01% | defer claimed unresolved incident targets | fail-closed idea correct | fold into stitch result masks |
| 29 | `MarkMissingPages` | 1 | 0.243 | <0.01% | detect no physical backing | storage-local | fuse |
| 30 | `FinalizeExactClosure` | 1 | 0.234 | <0.01% | proposal-kind promotion/continuation | kinds are accidental physics | delete |
| 31 | `CompactTargetHeads` | 1 | 0.219 | <0.01% | target head compaction | old global circuit | delete |
| 32 | `MarkPageHeads` | 1 | 0.208 | <0.01% | unique mapped pages | useful touched-page discovery | replace by direct page-key compact |
| 33 | `CommitPromotedPending` | 1 | 0.201 | <0.01% | OPEN/SUPPORTED → PROMOTED after root | lifecycle intent correct | replace by latent absorption receipt |
| 34 | `ScanPageValues` | 2 | 0.181 | <0.01% | page prefix | storage lowering | fuse/shared compact helper |
| 35 | `MarkSegmentPages` | 9 | 0.179 | <0.01% | per-segment touched mask | storage lowering | fuse |
| 36 | `MapTargetOrdinals` | 1 | 0.178 | <0.01% | target range ordinal map | global sort support | delete |
| 37 | `MapPageOrdinals` | 1 | 0.176 | <0.01% | page head ordinal map | publication scan support | simplify |
| 38 | `ClearExactClosure` | 1 | 0.175 | <0.01% | labels, links, edge state | old topology/pending ABI | delete |
| 39 | `FinalizePendingGauges` | 1 | 0.165 | <0.01% | component status | image component ontology | delete |
| 40 | `NormalizeStereoDepth` | 1 | 0.130 | <0.01% | capture-format normalization | representation-neutral | keep |
| 41 | `MarkTargetHeads` | 1 | 0.128 | <0.01% | equal-key head flags | old global circuit | delete |
| 42 | `BuildPoseGaugePartials` | 1 | 0.120 | <0.01% | exact pose-cell partials | readout gauge, not world | keep/adapt descriptor rows |
| 43 | `ScanPageBlocks` | 2 | 0.116 | <0.01% | page prefix hierarchy | storage lowering | simplify |
| 44 | `CountFreePairs` | 9 | 0.094 | <0.01% | free carrier pairs | correct residency accounting | keep/fuse, later paging |
| 45 | `ScanTargetHeads` | 1 | 0.087 | <0.01% | target head scan | old global circuit | delete |
| 46 | `ClearPendingProjection` | 1 | 0.086 | <0.01% | one-winner raster clear | wrong candidate representation | delete |
| 47 | `ProjectPendingDepth` | 1 | 0.083 | <0.01% | pending first-depth raster | incomplete fibre acceleration | replace by multi-fibre shadow compact |
| 48 | `FindExistingPages` | 9 | 0.076 | <0.01% | logical page lookup | backing-only | fuse |
| 49 | `ResolvePendingProjection` | 1 | 0.058 | <0.01% | select minimum handle at nearest depth | one-winner loss/race | delete |
| 50 | `BeginSegment` | 9 | 0.047 | <0.01% | per-segment counters | orchestration | delete/fuse |
| 51 | `ReducePoseGauge` | 1 | 0.031 | <0.01% | exact pose-cell meet | correct readout-gauge primitive | keep |
| 52 | `ScanTargetHeadBlocks` | 1 | 0.020 | <0.01% | target scan hierarchy | old circuit | delete |
| 53 | `ScanPageSupers` | 2 | 0.019 | <0.01% | page scan hierarchy | storage lowering | simplify |
| 54 | `CompactPageHeads` | 1 | 0.019 | <0.01% | unique page compact | useful semantics | shared touched-page compact |
| 55 | `BuildCorrectedCalibration` | 1 | 0.011 | <0.01% | corrected same-frame query descriptor | readout gauge | keep/adapt |
| 56 | `ReserveNovelExtent` | 1 | 0.010 | <0.01% | allocate frame-global pixel rectangle | gauge violation | delete |
| 57 | `ScanFreeSegments` | 1 | 0.009 | <0.01% | physical allocation prefix | backing only | simplify |
| 58 | `ScanTargetHeadSupers` | 1 | 0.009 | <0.01% | target scan hierarchy | old circuit | delete |
| 59 | `CloseFrameRevision` | 1 | 0.008 | <0.01% | validate immutable revision | correct | keep/fuse |
| 60 | `PublishFrameRevision` | 1 | 0.006 | <0.01% | final root exchange | correct and mandatory | keep as final instruction |
| 61 | `CommitPhysicalAllocation` | 1 | 0.005 | <0.01% | reserve missing physical pairs | backing only; finite residency cliff | keep fail-closed, later out-of-core |

The low-cost rows are not automatically worth preserving. Many exist solely to
support data structures that disappear. Conversely `PublishFrameRevision` is only
6.2 μs but is semantically essential.

---

## 7. Source-level correctness matrix

| ID | Current source fact | Violated native invariant | Severity | Exact correction in v8 |
|---|---|---|---|---|
| C01 | `BuildFrameProposals` calls `SigmaFramePendingCandidate(footprint,1)` although CURRENT uses reprojected `rightPixel` | both eyes constrain their actual shadow fibre | P0 | remove pixel winner; enumerate right-eye fibre from actual `Π_R` projection |
| C02 | `ResolvePendingProjection` stores one nearest handle; proposal ABI has one pending slot | candidate pruning may not discard an untested admissible preimage | P0 | compact all viable fibre keys; prune only with a proof bound |
| C03 | proposal slot 3 is unconditional `NOVEL` | latent manifestation only after exhaustive compatible-fibre failure and promotion proof | P0 | no NOVEL physical kind; emit latent solve only after native candidate closure |
| C04 | `BuildDepthSourceCells` consumes only `cell.sector==HIT`; `SigmaBuildDepthWorldCell` hardcodes HIT | first-hit sector is part of sensor shadow pullback | P0 | generated sensor descriptor emits HIT / PRE_HIT_EXCLUSION / NO_CLAIM before closure |
| C05 | RGB box begins at depth bootstrap ± depth-derived prior width | independent observations may not pre-contract one another | P0 | build each `Aq` from the full admissible native domain/fibre; intersect only in native closure |
| C06 | `SigmaInformationMassForWidth` uses a 16-step dyadic staircase and one maximum width | v7 exact per-relation precision rule and hidden-mode preservation | P0 | epistemic precision remains relation certificate; any native support amplitude is descriptor-defined, not this approximation |
| C07 | `SigmaFrameQualifyEdge` uses XYZ-row overlap to create CONTACT | 3D readout may propose work but cannot decide native relation | P0 | first-hit/native relation packet qualifies stitch; geometry is only a derived observable |
| C08 | `BuildPendingEdges` skips `CURRENT↔CURRENT` | dirty domain is endpoint generation/evidence, not proposal kind | P0 | enumerate intrinsic incident stitch keys whenever either endpoint/evidence generation changed |
| C09 | no live generation-pair transition cache in frame closure | stable native relation must not be recomputed per optical observation | P1/perf | descriptor-fingerprinted cache keyed by both endpoint and relation-evidence generations |
| C10 | singularity uses current-frame keys only; no durable same-annihilator multi-view witness | stable singular stratum needs independent-view proof | P0 proof | persist minimal native stitch certificate keyed by relation signature and independence class |
| C11 | pending `LocalExtent` is overwritten from current frame pixel | latent chart is native/local and stable across views | P0 | store descriptor-local latent coordinate/relations; sensor coordinates remain provenance only |
| C12 | pending record retains reduced state/bounds and two keys, not complete source relations | unresolved evidence must outlive the frame that created it | P0 | latent record references the complete native relation journal until deterministic minimization |
| C13 | published source journal is recycled before a proven minimal/durable certificate handoff | published state must remain reproducible | P0 | root receipt owns complete journal; release only after minimal certificate/raw handoff |
| C14 | `AccumulateNovelBounds` forms one global pixel bbox; `MapFrameTargets` maps raw pixel deltas | sensor grid cannot allocate `Σ₂` gauge | P0 | native latent component owns intrinsic local chart; promotion allocator places that chart deterministically |
| C15 | carrier current/shadow pairs monotonically exhaust at root 31 | residency is not world size | P0 long-run | retain fail-closed now; S4-10 adds encode/evict/rehydrate without changing germ identity |
| C16 | pending `GrowTo` allocates exact shortfall, producing about one segment per revision | physical segment count may not multiply logical physics evaluations | P1/perf | whole-quantum backing growth; native closure cardinality independent of segment count |
| C17 | `holes=0,reused=0` in both device runs | latent reuse/promotion is unproven | P0 acceptance | instrument projected/evaluated/accepted/absorbed native fibres and require physical reuse corpus |
| C18 | `ResolveCarrierHalos` searches only bound segment page metadata | backing decomposition visible to readout | P1 | direct readout uses intrinsic stitch keys/global logical lookup, no page halo authority |
| C19 | `SigmaRigBridge` overwrites `_latestFrame` before explicit canonical admission | coherent capture candidates and admitted observations are conflated | P1 | deterministic pre-admission sampling allowed; admitted frame becomes owned and cannot be overwritten |
| C20 | 61 named kernels express several physical solvers | one descriptor must own manifestation, inverse and stitch | structural | two semantic solves, bounded compact lowerings; no lifecycle graph |

### Important qualification

`reused=0` does not prove that CURRENT association is zero. CURRENT candidates do
exist. It proves that the persistent pending path has not demonstrated a single
successful handle reuse/promotion in these runs, and C01/C02 provide concrete
mechanisms that can cause that result.

Likewise, the current global NOVEL bbox does not eagerly allocate every empty page
inside its rectangle. It still uses the camera grid as canonical coordinate
authority, which is the actual violation.

---

## 8. Proven causal chains

### 8.1 False latent identity

```text
pending germ projects to a real right-eye pixel
→ proposal reads pending at reference-left footprint instead
→ valid right-eye pending fibre is absent
→ nearest single pending winner may be incompatible
→ no second pending fibre can be tested
→ unconditional NOVEL remains available
→ false new latent identity
→ pixel bbox allocates another carrier extent
→ extra pages and no pending reuse
```

### 8.2 Topology work and authority

```text
102400 footprints
→ 204800 optical H/V edge slots
→ all slots schedule one 256-thread workgroup
→ XYZ interval overlap creates CONTACT claim
→ τ / annihilator / associator closure
→ 2328.958 ms
```

Not every workgroup executes the entire catalog: `NONE` claims exit the expensive
part. The invalidity is still twofold: all 204800 groups pay the skeleton, and the
subset entering native algebra was selected by a 3D readout predicate rather than
native first-hit/relation evidence.

### 8.3 Linear slowdown

```text
pending capacity shortfall at revision n
→ GrowTo(shortfall), not GrowTo(full quantum)
→ one small physical segment is appended
→ PendingWindowCount ≈ revision count
→ candidate/reduction/persistence loops traverse every window
→ roughly +99…109 ms per revision
```

The logical fix that removed the 102400 cap is correct. Its physical lowering is
not. V8 eliminates provider/window multiplicity from semantic evaluation entirely.

### 8.4 Root-31 stop

```text
rev31: missing=30, free=55
→ successful allocation leaves 25
rev32: missing=34, free=25
→ CommitPhysicalAllocation sets 0x20
→ no page pair assignment
→ PropagatePageMappings detects unmapped request and adds 0x100
→ 0x120, root remains 31
```

For every adjacent successful revision in both runs,
`free(next)=free(current)-missing(current)`. The stop is exact carrier-residency
exhaustion, not R3, pending or timing noise.

---

## 9. Replacement architecture: one native world, two semantic solves

### 9.1 Ω — `INVERSE_NATIVE_22`

Input:

```text
one owned coherent RGB_L/R + D_L/R observation
immutable calibration/pose descriptor
published root
disposable multi-fibre sensor shadow candidates
latent native-relation candidates
prior native certificates
```

Logical work owner: one candidate native germ/fibre, never one page, pixel tile or
provider segment.

Operation:

\[
\mathcal C_i
=
\mathcal C_{prior,i}
\cap
\bigcap_{q,p}
(\Pi_{q,p}\circ\mathcal M\circ E_{22})^{-1}(O_{q,p}).
\]

Then select native minimum change, preserve unconstrained fibre directions and
forward-verify every relation. Output is at most one `GermCandidate` per native
key plus exact evidence disposition:

```text
UNCHANGED / CHANGED / CONFLICT / UNRESOLVED / EXCLUSION / FAULT
```

No depth state, RGB state, temporal state or candidate-kind state is persisted.

### 9.2 Ξ — `STITCH_COMMIT_NATIVE_22`

Input: changed/latent germ candidates, their intrinsic incident keys, complete
evidence journal and current root.

Operation:

```text
changed germs
→ unique intrinsic neighbour stitch keys
→ generation/evidence cache lookup
→ exact 22-relation transport closure on misses
→ regular / singular / no-relation / unresolved
→ resolve or retain latent relations
→ sparse GermDelta stream
→ touched logical pages
→ shadow generations
→ certificate ownership
→ one final root exchange
```

An unresolved claimed stitch defers only the incident candidate. No full-frame
fixed point, label propagation or image component is canonical.

### 9.3 Readout is not a third reconstruction solve

\[
\operatorname{Readout}_r(\Psi)
=
\Pi_r\circ\mathcal M\circ E_{22}(\Psi).
\]

Eye, prediction, export and debug are independently compiled pure consumers. Only
Ω/Ξ may produce `ΔΨ`.

---

## 10. Proposed physical kernel matrix

The kernel list is a lowering, not an ontology. A later exact fusion may reduce it.

| Kernel | Cardinality | Inputs | Exact operation | Outputs | Initial Quest gate |
|---|---|---|---|---|---:|
| `ProjectSensorShadow` | active sensor footprints × compact visible fibres | RGB-D, calibration, published root, relation descriptor | forward native shadow + first-hit/order fibre proposal; shared E22 CSE | `ShadowRelationPacket`, fibre keys | ≤150 ms |
| `ReduceSensorShadow` | shadow packets | packet keys/relations | stable compact by native germ/latent seed; no physics decision | owner ranges + indirect args | ≤30 ms |
| `ConstrainNativeGerms` | unique owner ranges | prior S16, packets, certificates, descriptor | reverse-DAG pullback, exact conjunction, min-change, forward verification | one `GermCandidate` + relation witness | ≤775 ms |
| `ResolveStitchOverflow` | dirty intrinsic cache misses only | endpoint candidates/states, stitch evidence | τ + E22 transport + ZD/associator stratum | stitch outcome/cache entry/defer mask | ≤300 ms |
| `ResolveLatentRelations` | unresolved latent work only, indirect/cold | latent regions, fibre proposals, stitch outcomes | absorb/refine/promote native local chart | latent delta or promoted germ keys | ≤150 ms active; zero when empty |
| `PrepareChangedPages` | unique touched logical pages | changed germ keys, carrier root/free backing | allocate/clone immutable shadows | destination handles | ≤20 ms |
| `ScatterChangedGerms` | changed germs | exact S16 bytes, destination handles | one writer per germ, exact scatter | shadow pages + witness refs | ≤15 ms |
| `CloseAndPublishRevision` | one bounded closure | page receipts, evidence receipts, deferred/fault masks | validate all-or-none revision, root exchange last | published root/disposition | ≤1 ms |

Final S4‑08 target for the same 320×320 fixture:

```text
retired v7 kernels                         0 dispatch / 0 ms
Ω + Ξ canonical compute                   <= 1500 ms
owned-frame submission-to-completion      <= 1800 ms
30-revision steady-state latency slope     <= 1% total drift
semantic work count vs backing segments    bit-identical and count-identical
```

These are acceptance budgets, not claimed measurements. Every run reports actual
Release timestamps. Failure triggers lowering/CSE/compaction work, never weaker
physics or reduced sensor resolution.

---

## 11. Data contracts

### 11.1 `NativeRelationDescriptor`

```text
numericDomainFingerprint
sedenionTableFingerprint
relationDescriptorFingerprint
relationCount = 22
for each relation:
    value type / interval type
    signed-XOR permutations
    constants and dyadic scales
    explicit bracket tree
    forward expression root
    reverse-contractor plan
    stitch-transport plan
    readout participation masks
shared common-subexpression graph
minimum-change descriptor
```

Generated artifacts are the only authority. Hand-maintained duplicate sensor or
topology equations are forbidden.

### 11.2 `ShadowRelationPacket`

```text
observationRevision
source/eye and independence key
calibration/pose epoch
finite footprint identity
first-hit sector and direct-order interval
candidate native fibre key or latent seed key
relation mask
exact relation lower/upper or predicate bounds
raw-reference handle when contraction cannot yet be certified
```

Four streams remain independent records until one native owner intersects them.

### 11.3 `GermCandidate`

```text
NativeGermKey
prior generation
candidate S16[16] Q16.48
outcome and changed mask
native relation validity/gap witness
ordered evidence range
incident stitch key range
latent chart receipt, when applicable
```

Exactly one candidate owner may write one germ in one observation. If physical
lowering emits partials, their reduction is exact and owner-local.

### 11.4 `NativeRelationCertificate`

```text
descriptor fingerprint
native germ/latent key
relation mask
exact relation bounds/predicates
first-hit/order role
source class and independence key
calibration/pose epoch
raw reference if irreducible
stitch signature/role when required
```

The final reduced joint region is a cache, not a substitute for its complete
source relations. A journal is retained once per observation; page generations
hold references, never duplicate source data.

### 11.5 `LatentGerm`

```text
stable native local chart/seed
admissible native relation region
complete evidence references
candidate intrinsic stitch references
generation and lifecycle receipt
```

It has no canonical `Σ₂` coordinate and no render identity. Its sensor pixel is
provenance, not its chart.

---

## 12. Readout family matrix

| Consumer | Native query | Output | Loss policy | Canonical authority | Live cost policy |
|---|---|---|---|---|---|
| XR left/right eyes | eye-specific `Πeye∘M∘E22` | 2×2D retinal RGB/depth/order | deliberately lossy | none | direct, disposable, target 72/90 Hz |
| Scanner prediction | sensor `Πsensor∘M∘E22` | multi-fibre keys, order/depth/RGB/support | may prune only with proof | proposal only | active visible fibres only |
| 3D export | rich manifestation + intrinsic stitch | metric geometry, non-welded connectivity, textures/material | requested quality threshold only | none | on demand/off hot path |
| Debug/analysis | arbitrary generated relation projections | XYZ, confidence, ZD, associator, evidence age, key | diagnostic | none | opt-in only |

Direct eye readout may omit information that export needs. That information stays
in `Ψ` and its certificates. Export never consumes the eye cache. Prediction never
mints carrier identity.

---

## 13. First-hit, latent state and scene evolution

First-hit belongs to the shadow descriptor, not a post-hoc depth classifier.

```text
measured first hit before predicted fibre
    → candidate different/native latent fibre; old behind-hit fibre gets no claim

measured/predicted intervals compatible
    → inclusive native pullback for that fibre

predicted contact inside measured pre-hit path
    → PRE_HIT_EXCLUSION relation, no subtraction

behind measured first hit
    → NO_CLAIM, exactly zero evidence
```

`UNKNOWN != EMPTY` survives automatically because the complement of one shadow is
an observation fibre, not a null state.

Scene change in later S4‑09 consumes the same native relation packets:

- appearance is latent→supported native closure;
- disappearance requires independent pre-hit exclusions and pass-through;
- transport tests whether one native carrier relation maps old and new shadows;
- temporary occlusion supplies no evidence behind the nearer hit.

No temporal solver or object graph is introduced.

---

## 14. Refinement and detail

A germ is not forced to be one 3D point. The descriptor may expose local
manifestation modes over sub-carrier offset/direction:

\[
\mathcal R_\xi(\delta,\omega)
=
\mathcal M(E_{22}(s_\xi);\delta,\omega).
\]

Independent shifted/grazing views can constrain previously shadowed modes of the
same germ. Only when retained observations distinguish structure that the current
germ/modal capacity cannot jointly reproduce does the carrier refine:

```text
exact refinement demand
→ bijective intrinsic gauge split
→ transport complete evidence
→ instantiate finer S16 germs
→ run the same Ω/Ξ closure
```

There is no displacement mesh, texture world or 3D voxel refinement hierarchy.

---

## 15. Determinism and failure semantics

For one accepted observation sequence, the following must be byte-identical:

- `Ψ` pages and generations;
- native relation certificates and exact gaps;
- latent seed/handle generations and promotion order;
- stitch classes/signatures;
- gauge allocation;
- revision root sequence.

They must be invariant under:

- source order;
- workgroup/subgroup shape;
- binding segmentation;
- carrier page placement/residency;
- compact work ordering before stable native-key ownership;
- contractor scratch-window size;
- cache hit versus forced miss;
- proof-minimization window size.

Any bounded contractor overflow, missing exact lowering, evidence-capacity failure
or unresolved required stitch fails closed before `ΔΨ`. It may backpressure an
unadmitted observation; it cannot publish a partial revision or silently mint a
latent identity.

---

## 16. Hard replacement and LOC matrix

Current production LOC in the main replacement surface is 11,922 lines. The v8
run deletes old ownership when each replacement becomes green; it never leaves a
disabled fallback.

| Current production surface | Current LOC | Replacement | Required fate |
|---|---:|---|---|
| `SigmaFrameInverse.compute` | 1412 | native sensor shadow + inverse contractor | delete whole old shader |
| `SigmaFrameClosure.compute` | 1704 | native owner reduction + stitch/latent closure | delete whole old shader |
| `SigmaFramePublish.compute` | 841 | compact sparse commit | delete whole old shader |
| `SigmaFrameGraph.cs` | 1795 | fixed native closure recorder | delete/replace whole file |
| `SigmaFrameResources.cs` | 1201 | small relation/germ/journal resources | delete/replace whole file |
| `SigmaFrameAbi.hlsl` | 102 | generated native ABI | delete/replace |
| `SigmaInverseMath.hlsl` + `SigmaRgbInverseMath.hlsl` | 792 | generated descriptor contractor | delete sensor-specific math after parity |
| `SigmaTopologyMath.hlsl` + `SigmaIntrinsicTopology.cs` | 410 | generated stitch descriptor/cache | delete separate topology subsystem |
| `SigmaDepthInverse.cs` + `SigmaRgbInverse.cs` | 777 | CPU native oracle fixtures only | remove live duplicate APIs |
| controller/renderer/telemetry portions | 2674 total files | thin lifecycle/readout consumers | remove old branches, retain donor plumbing only |

Hard gates against `cac9ab0`:

```text
gross deleted production LOC                  >= 10,000
new production LOC before device gate         <= 4,000
final Runtime/Resources net                    <= -6,100
final net versus d3b83e1                       <= -5,500
legacy CURRENT/PENDING/NOVEL physics symbols   0 live references
old FrameInverse/Closure/Publish shader names  0 assets/references
old graph fallback/feature flag                0
```

Generated descriptor tables are reported separately but may not hide handwritten
orchestration growth. Tests are reported separately. A run that needs a scheduler,
manager, second world or compatibility path stops and simplifies.

---

## 17. Deterministic S4‑08.6 closure runs

### N0 — canonical rebase and frozen descriptor contract

Deliverables:

- replace v7 contradictions with v8 `new_spec.md`;
- freeze this audit and one compact resume cursor;
- define the exact `NativeRelationDescriptor` schema;
- define the mandatory authoritative TOE E22 artifact/provenance and fingerprint
  contract; freezing the supplied artifact itself is the first N1 gate;
- no runtime change.

Gate: control validation, Markdown/math audit, no contradictory old physical flow,
one active node, clean diff. Commit separately.

### N1 — one generated descriptor, four evaluators

Generate from one semantic IR:

```text
ForwardNative22
PullbackNative22
StitchNative22
Native22ReferenceOracle
```

Gate:

- all 22 bracket trees and shared subexpressions fingerprinted;
- forward CPU/HLSL bit parity;
- inverse contractor soundness: every accepted state forward-verifies;
- deterministic unresolved on bounded incompleteness;
- stitch cache hit/miss parity;
- no handwritten sensor/topology alternate equation.

No live cutover yet. Commit N1.

### N2 — native sensor shadow and independent pullback

Implement `ProjectSensorShadow`, `ReduceSensorShadow` and a non-mutating
`ConstrainNativeGerms` oracle path.

Required fixtures:

- actual left/right reprojection, asymmetric views;
- more than one compatible native fibre at one pixel;
- no candidate winner collapse;
- HIT/PRE_HIT_EXCLUSION/NO_CLAIM;
- RGB source independence from depth/prior;
- hidden-fibre preservation;
- exact source permutation parity;
- 320×320 legal Vulkan lowering.

Compare forward sensor predictions to captured evidence, not v7 S16 bytes where v7
is known noncanonical. Commit N2.

### N3 — carrier-pull owner cutover

Cut live scan to one native germ owner and exact relation conjunction. In the same
commit hard-delete:

```text
BuildFrameProposals
BuildDepthSourceCells
BuildRgbSourceCells
EvaluateCandidateMeets provider×segment loop
global target sort/reduction support that owner-local closure replaces
four global source-cell worlds
```

Gate: one/many fibres and one/many sensor packets produce one bit-identical
`GermCandidate`; no multiple writer; current root remains immutable because stitch
is still fail-closed. Production LOC must already be negative vs `cac9ab0`.

### N4 — native stitch cutover

Implement only dirty intrinsic/evidence stitch keys and generation cache. In the
same commit hard-delete:

```text
BuildPendingEdges / GatherEdgeEndpoints / ClosePendingEdges
ApplyPendingEdges / 18×RelaxPendingLabels
XYZ CONTACT qualifier
separate topology ABI/math/controller
full optical edge arrays
```

Gate: regular wall, fold, thin double side, no-relation overlap, unknown edge,
CURRENT↔CURRENT changed pair, cross-page/segment decomposition and independent-view
singularity stability. Same descriptor oracle must decide forward/inverse/stitch.

### N5 — latent native relation cutover

Replace proposal kinds and pending pixel charts with `LatentGerm`. In the same
commit delete pending projection winner, persistent pending SoA/label/link
machinery, global NOVEL bbox and pixel-delta continuation.

Gate:

- repeated not-yet-supported surface refines one latent germ;
- right/left/asymmetric views find all admissible fibres;
- side reveal absorbs into an existing intrinsic carrier branch when stitchable;
- 5 mm parallel surfaces remain distinct;
- stable local chart survives sensor/image resolution changes;
- failed promotion has no canonical effect and retains required evidence;
- no capacity/window/revision-count scaling.

### N6 — sparse root-last commit and evidence ownership

Replace 23-kernel publication with touched-page sparse preparation/scatter and one
root-last close. Complete observation journal transfers once; deterministic
certificate/raw handoff precedes release. In the same commit delete duplicate page
sort/map/scan lifecycle and old frame graph/resources.

Gate:

- CHANGED only scatters;
- UNCHANGED strengthens certificate without page generation;
- fault/unresolved cannot advance root;
- multi-page/multi-segment readers see all old or all new;
- frame-slot recycle preserves evidence;
- certificate minimization window parity;
- real residency exhaustion backpressures/fails closed, never reports commit.

### N7 — pure readout families

Implement sensor prediction and direct XR eye shadow from the same descriptor.
Remove page halo and live persistent XYZ/mesh readout once parity is established.
Define on-demand textured export inputs from full `Ψ`; do not put export work in
the live scan graph.

Gate:

- eye output has correct stereo disparity/order and survives cache deletion;
- scanner prediction returns complete fibre proposals and has no allocation
  authority;
- backing segment decomposition is visually and bitwise invisible;
- debug and export readouts cannot bind writable canonical buffers.

### N8 — hard deletion, Release and physical closure

Before build:

- run the LOC gates in §16;
- grep/graph proves every retired kernel/file/callsite absent;
- regenerate code graph;
- full exact/Vulkan fixtures;
- Release compiler and eight-UAV gate;
- archive/build/install the same commit.

Quest gates:

```text
first published root/revision/pages/draw > 0
root advances for >= 50 admitted observations or until user stop
no fault, silent no-change, capacity cliff or revision-dependent latency slope
thin board front/back and bucket inside/outside remain separate
front + 90° + grazing revisit refines one native branch without false extents
fold/no-relation signatures stable across independent views
complete per-kernel Release timestamps
Ω + Ξ <= 1500 ms compute, wall <= 1800 ms on the frozen fixture
eye readout remains frame-rate safe and independent of scan cadence
PSS bounded by active residency/evidence ownership
```

Only N8 may close S4‑08. S4‑09 remains blocked. No source-only or visually plausible
result is acceptance.

---

## 18. Acceptance corpus by native invariant

| Fixture | Native question | Required result |
|---|---|---|
| flat wall 1/5/20 passes | do new shadows contract the same germ? | bytes stable or tighter supported modes; no duplicate extent |
| front + grazing wall | are shadowed native modes preserved/revealed? | grazing evidence adds detail; front view does not erase it |
| asymmetric L/R information | are both sensor row-spaces used? | right-only relation affects result without wrong-pixel loss |
| 5 mm coloured sheet | can two manifestations share near XYZ but not stitch? | two carrier preimages, distinct appearance, no weld |
| fold/door frame | does E22 stitch enter stable singular stratum? | persistent signature with independent-view proof |
| overlapping broad XYZ cells | can 3D proximity manufacture identity? | `NO_RELATION` or unresolved unless native evidence supports stitch |
| hidden background behind person | is behind-hit complement untouched? | identical background S16/certificates |
| removed cabinet pass-through | is PRE_HIT exclusion live? | no change until independent gate; then supported scene transition |
| subpixel relief | can modal capacity refine before gauge split? | tighter native modes; split only if one germ cannot reproduce all views |
| page/segment permutation | is storage invisible? | byte-identical pages/certificates/stitches/readouts |
| restart/rehydrate | is descriptor/world complete? | identical continuation and readout from persisted root |
| eye vs export | can readout be cheap without deleting state? | eye may be simpler; export retains best geometry/texture/detail |

---

## 19. Final architectural conclusion

The measured v7 graph is not saved by optimizing `ClosePendingEdges`, adding more
pending candidates or repairing the global NOVEL bbox separately. Those would
preserve the classical scanner decomposition that created the contradictions.

The v8 replacement has one physical vocabulary:

```text
native S16 germ
↕ same generated 22-relation descriptor
sensor shadow pullback / intrinsic stitch / eye readout / export readout
```

The scanner becomes the inverse Merkaba/eigenmode microscope: it interprets 3D/RGB
sensor shadows inside its native 16D reality. The eye and exporter then draw two
different lower-dimensional representations of that same reality.

The practical result is also the low-code result: remove sensor-specific worlds,
proposal-kind physics, optical topology, pixel gauge and duplicated publication
machinery; retain the exact algebra, carrier, certificates and root-last commit.
The implementation cost follows new native information and dirty intrinsic
relations, not pixels × providers × backing segments × optical edges × revision
history.
