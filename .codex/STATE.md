# Sigma‑PRISM‑16 implementation state

Updated: 2026-08-25 (Europe/Prague)

## Authority

- Canonical spec: `new_spec.md`, `CPQ4-2026-08-25-S16-v8.2`.
- Branch: `feat/sigma-prism-16-cpq4-20260822`.
- Runtime replacement baseline: `cac9ab012f4ce574e5eb9bee88290982fd9c4fe8`.
- Accepted primitive milestones: S4‑00 through S4‑07 where compatible with v8.2.
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
```

The source does not prove the authoritative TOE program, E22 faithfulness,
full-query validity of current `ZNullDyad`, complete shadow-mode coupling/fibres or
the final refinement transform.

## Frozen v8.2 model

```text
CANONICAL
    one Psi : one carrier Sigma_2 -> S16

LAW
    one authoritative generated Merkaba relation-program IR M

QUERY
    sensor / eye / relation / prediction / export = M_q[Psi]

SCAN
    exact reverse evaluation of the same program

AMBIGUITY
    transient contractor disjunction; persisted constraint/evidence only

CLOSE
    both-eye constraints + native relations -> one NativeCloseCommit

DEFAULT
    z_empty is a full-program-proven S16 value; backing absence is representation

REFINE
    contract S16 or increase sampling density of the same Psi

PUBLISH
    resolved/common full-S16 deltas, root last
```

There is no physical chart/sheet/branch/hypothesis/materialization/topology world.
Preservation is defined on complete-program equivalence fibres, not one query
kernel.

## Current exact action

N0R v8.2 is accepted with zero Runtime/Resources diff. N1R is the sole active run:

1. locate and hash the supplied authoritative TOE artifact;
2. generate one relation-program IR with all arities/brackets/couplings;
3. generate query reductions and exact reverse contractors;
4. prove E22 faithfulness or retain direct S16 dependencies;
5. prove full-query `z_empty` or block;
6. prove complete-program fibre/coupling rules;
7. import the authoritative sampling-refinement transform;
8. establish CPU/HLSL parity without live mutation.

Do not infer missing law from current B/G/F/RGB/topology code.

## LOC cursor

```text
gross production deletion vs cac9ab0  >=10000
new production code                   <=4000
net vs cac9ab0                         <=-6100
net vs d3b83e1                         <=-5500
retired live symbols/fallbacks         0
```

N3R–N7R delete each replaced branch in the same commit.

## Completion gate

Only N8R closes S4‑08. It requires exact oracle/Vulkan/LOC/code-graph gates,
same-commit source archive and Release APK, Quest installation and physical corpus,
truthful kernel times, `NativeCloseCommit <=1500 ms`, wall `<=1800 ms`, bounded
memory and no revision/segment latency slope.
