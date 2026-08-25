# Sigma‑PRISM‑16 implementation state

Updated: 2026-08-25 (Europe/Prague)

## Authority

- Canonical spec: `new_spec.md`, `CPQ4-2026-08-25-S16-v8.3`.
- Branch: `feat/sigma-prism-16-cpq4-20260822`.
- Runtime replacement baseline: `cac9ab012f4ce574e5eb9bee88290982fd9c4fe8`.
- Accepted primitive milestones: S4‑00 through S4‑07 where compatible with v8.3.
- Active node: S4‑08, reopened and in progress.
- Active repair: S4‑08.6 one-medium native closure.
- Frozen plan: `.codex/S4-08.6_NATIVE_CLOSURE_PLAN.md`.
- Sole routine cursor: `.codex/S4-08.6_RESUME.md`.
- Forensic facts and replacement matrix: `analyza.md`.
- S4‑09 remains pending/unopened.

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

## Current exact action

N0R v8.3 documentation correction is accepted by this checkpoint with zero
Runtime/Resources diff. N1R has not started and remains the sole next
implementation run:

1. locate/hash the supplied authoritative TOE artifact and freeze/hash `I_Q`;
2. extend/replace the donor operator vocabulary only if authoritative source
   requires it, then generate one provenance-bearing relation-program IR with all
   arities/brackets/couplings;
3. generate query reductions and exact reverse contractors;
4. generate exact directional mould action, behind-hit zero action and native
   uncertainty/information pullback;
5. freeze calibrated photometric nuisance relations and generate exact
   zero-false-negative query-support summaries;
6. prove locality certificate compression and deterministic minimization preserve
   coupled/disjunctive exactness with bounded duplicate-revisit growth;
7. prove contextual E22 separation/complete-law sufficiency or retain direct S16;
8. prove `ZEmpty` representation parity plus all-default quiescence or block;
9. prove complete-program fibre/coupling rules;
10. freeze exact `chi/kappa`, representation decode/transport and constructive canonical
    gauge normalizer; prove base-density fresh support admission;
11. establish CPU/HLSL parity without live mutation.

Do not infer missing law from current B/G/F/RGB/topology code.

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
Runtime/Resources diff           zero
code graph                       current, 107 files
validate_goal_state              green, active S4-08
```

## LOC cursor

```text
gross production deletion vs cac9ab0  >=10000
new production code                   <=4000
net vs cac9ab0                         <=-6100
net vs d3b83e1                         <=-5500
retired live symbols/fallbacks         0
```

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
