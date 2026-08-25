# Sigma‑PRISM‑16 implementation state

Updated: 2026-08-25 (Europe/Prague)

## Authority

- Canonical spec: `new_spec.md`, `CPQ4-2026-08-25-S16-v8.1`.
- Branch: `feat/sigma-prism-16-cpq4-20260822`.
- Runtime replacement baseline: `cac9ab012f4ce574e5eb9bee88290982fd9c4fe8`.
- Accepted primitive milestones: S4‑00 through S4‑07 where compatible with v8.1.
- Active node: S4‑08, reopened and in progress.
- Active repair: S4‑08.6 ontology-reset native field closure.
- Frozen plan: `.codex/S4-08.6_NATIVE_CLOSURE_PLAN.md`.
- Sole routine cursor: `.codex/S4-08.6_RESUME.md`.
- Forensic facts and corrected matrix: `analyza.md`.
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

## Superseded checkpoint

Commit `f24ecc3` correctly rejected v7 sensor/proposal/topology decomposition but
its germ-first architecture is superseded before any N1 runtime work. Do not
implement these parts from it:

```text
unary E22 as assumed complete native law
per-germ physical sensor shadow
candidate identity inside observation packets
LatentGerm as first unresolved state
Omega inverse then Xi stitch/commit semantic split
StitchNative22 as physical authority
ABSENT interpreted as native null
scalar precision as canonical epistemic physics
```

## Frozen v8.1 ontology

```text
canonical:
    full Psi : intrinsic atlas Sigma_2 -> S16
    + authoritative TOE Merkaba/eigenmode law

forward:
    local contributions from bounded native neighbourhoods
    -> whole-scene shadow reduction

observation:
    two coherent RGB-D whole-field shadows

inverse:
    union of possible support hypotheses per footprint

closure:
    observation unions + native law + atlas incidence
    -> one NativeCloseCommit feasible solve

unresolved:
    evidence branch; no germ/chart/extent

publication:
    only resolved/common full-S16 NativeStateDelta, root last

readouts:
    eye/prediction/export/debug are pure field shadows
```

`ABSENT != NATIVE_NULL`. Exact ZD != near-singular != direct order. Minimum-change
operates only inside an already resolved harmless-equivalence fibre.

## Current exact action

N0R is accepted with zero Runtime/Resources diff. N1R is the sole active run:
locate, hash and freeze the supplied authoritative TOE `K_M`
operator/frame/kernel artifact, including relation arities/brackets,
manifestation, local contribution and scene-reducer laws, ZD distinctions,
fibre-preservation and modal refinement.

If the artifact is absent/incomplete, N1R is blocked. Do not guess or substitute
current B/G/F/inverse/topology equations.

## LOC cursor

```text
gross production deletion vs cac9ab0  >=10000
new production code                   <=4000
net vs cac9ab0                         <=-6100
net vs d3b83e1                         <=-5500
retired live symbols/fallbacks         0
```

N3R–N7R delete each replaced branch in the same commit. Dead code is removed, not
left disconnected.

## Completion gate

Only N8R closes S4‑08. It requires exact oracle/Vulkan/LOC/code-graph gates,
same-commit source archive and Release APK, Quest installation and physical corpus,
truthful kernel times, `NativeCloseCommit <=1500 ms`, wall `<=1800 ms`, bounded
memory and no revision/segment latency slope.
