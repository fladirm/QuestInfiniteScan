# Sigma‑PRISM‑16 implementation state

Updated: 2026-08-26 (Europe/Prague)

## Authority

- Canonical spec: `new_spec.md`, `CPQ4-2026-08-25-S16-v8.3`.
- Branch: `feat/sigma-prism-16-cpq4-20260822`.
- Runtime replacement baseline: `cac9ab012f4ce574e5eb9bee88290982fd9c4fe8`.
- Accepted primitive milestones: S4‑00 through S4‑07 where compatible with v8.3.
- Active node: S4‑08; corrective N1R is accepted and no subsequent run is active.
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
fingerprint `2595954ac6f0a2f1a096c7bdde8661c892820101d67ab90b4aeb49fbd4882bc1`:

```text
opcodes / nodes / operands              29 / 48 / 46
expressions / query entry points        15 / 7
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
EditMode                              87/87 passed, 0 failed
focused N1R tests                    10/10 passed
generator regeneration/check         passed
compute UAV <= 8                     passed
production equality vs df5200f       passed
git diff hygiene                     passed
```

N2R is pending and has not started.

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
code graph                       current, 108 files
validate_goal_state              green, active S4-08
```

## LOC cursor

```text
N1R current production LOC                  17274
corrective checkpoint vs b541635            +0 / -392 / net -392
N1R run delta vs df5200f                    +0 / -0 / net 0
N1R gross deletion vs cac9ab0               0
N1R new production vs cac9ab0               0
N1R net vs cac9ab0                          0
N1R net vs d3b83e1                          +601

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
N1R is not the final replacement deletion cut and claims none of the N7 size
gates. Its own corrective commit is production-negative and cumulative N1R has no
disconnected production addition.

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
