# Sigma‑PRISM‑16 implementation state

Updated: 2026-08-26 (Europe/Prague)

## Authority

- Canonical spec: `new_spec.md`, `CPQ4-2026-08-25-S16-v8.3`.
- Branch: `feat/sigma-prism-16-cpq4-20260822`.
- Runtime replacement baseline: `cac9ab012f4ce574e5eb9bee88290982fd9c4fe8`.
- Accepted primitive milestones: S4‑00 through S4‑07 where compatible with v8.3.
- Active node: S4‑08; N1R accepted, no subsequent run active.
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

## N1R accepted checkpoint

N1R consumes only the workspace scanner capsule
`Tools/sigma/authority/I_TOE_S16_K16_NATIVE_CLOSURE.md`, whose workspace SHA-256
is `36a584dcff0c0c340d491ab476aa7428f7b1edf0c97e1407022e0f71181fdee1`
and whose declared upstream monograph SHA-256 is
`9d2e3604846305cfe5244a4ef49f169632c60582cf895256fadc36426dc5786f`.
No other TOE sector or `~/Stažené` copy is an input.

The sole generator now hashes itself, the capsule, exact `I_Q`/`I_REP`, canonical
spec/plan and an S16 native-core fingerprint which deliberately excludes legacy
`G/F/RGB` readout authority. It emits one auditable 19-expression provenance
inventory plus CPU/HLSL tables under program fingerprint
`ce356a5913c689908325a2f79bcf2350bc28691e917e3d0d71d7c51417193343`.

Source-faithful fail-closed boundaries are explicit:

```text
E22 inventory                         absent; direct full S16 retained
shadow-kernel decoupling proof        absent; transparent modes not frozen
shell A1 orientation                  absent; only -1/-3/-7/-15 invariant emitted
native modal G/epsilon region         parametric fingerprinted Q48 input;
                                      missing/unproved means UNRESOLVED
ZEmpty                                algebra zero in the new representation
legacy nonzero ZNullDyad              rejected as no-manifestation authority
all-default                           DEFAULT_SAT, reducer identity, zero work
behind-hit                            NO_CLAIM / zero reverse action
```

No production controller references the new program; N1R adds no live mutation,
fallback or second graph. Focused generator/JSON/UAV gates pass. Unity
`6000.5.9f1`, Vulkan EditMode warm run is `85/85` passed, including `8/8` N1R
tests and exhaustive CPU/HLSL basis-context parity. The preceding cold-import run
was `84/85`; its sole failure was an unrelated legacy test timeout during a
392-second first shader import, and the immediate warm rerun passed all 85.

N1R production diff versus `df5200f`/`cac9ab0` is `+392/-0` LOC: 229 generated
C#, 100 generated HLSL, 59 parity-fixture lines and four native-core fingerprint
lines. This is the generated replacement authority required before the N3 hard
cut; no subsequent run is active in this checkpoint.

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
