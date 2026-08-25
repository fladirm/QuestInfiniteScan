# Sigma‑PRISM‑16 implementation state

Updated: 2026-08-25 (Europe/Prague)

## Authority

- Canonical spec: `new_spec.md`, `CPQ4-2026-08-25-S16-v8`.
- Branch: `feat/sigma-prism-16-cpq4-20260822`.
- Runtime replacement baseline: `cac9ab012f4ce574e5eb9bee88290982fd9c4fe8`.
- Accepted primitive milestones: S4‑00 through S4‑07 where compatible with v8.
- Active node: S4‑08, reopened and in progress.
- Active replacement: S4‑08.6 native S16/Merkaba closure.
- Frozen plan: `.codex/S4-08.6_NATIVE_CLOSURE_PLAN.md`.
- Sole resume cursor: `.codex/S4-08.6_RESUME.md`.
- Forensic/current→native matrix: `analyza.md`.
- S4‑09 remains pending/unopened.

## Device-proven starting point

Release commit `cac9ab0` produced two coherent Quest evidence runs:

```text
root progress       1→30 and 1→31
sampled dispatches  266 across 61 kernels
compute checksum    4520.946 ms
first sampled wall  4776.70 ms
top kernels         ClosePendingEdges 2328.958 ms
                    BuildRgbSourceCells 937.364 ms
                    EvaluateCandidateMeets 837.657 ms
root-31 stop        carrier pairs missing=34, free=25, fault=0x120
pending             frame cap removed, but fragmented growth and 0 reuse/promotions
```

The top three v7 physical solvers consume 90.78% of compute. Source audit proves
wrong-eye pending lookup, one-winner candidate loss, unconditional NOVEL,
hardcoded HIT, depth-conditioned RGB pullback, incorrect dyadic mass,
XYZ-authoritative edge claims, omitted changed supported↔supported edges, missing
generation cache/stable singular proof, pixel gauge mapping, incomplete evidence
lifetime and segment-visible readout. Details and source anchors are frozen in
`analyza.md`; do not repeat that audit.

## Frozen v8 ontology

```text
native world:
    Psi : Sigma_2 -> S16

forward/readout:
    S16 -> E22 native relation atlas -> manifestation -> query shadow

scan:
    RGB-D shadow region -> reverse of same bracket DAG -> admissible S16 fibre

topology:
    neighbouring germs -> same E22 transport/stitch -> native relation stratum

mutation:
    GermDelta + exact proof -> sparse immutable pages -> root exchange last
```

The 22 relations are one overcomplete image of one S16 state. They cannot be
persisted or mutated as an independent world. `CURRENT/PENDING/CONTINUATION/NOVEL`
have no v8 physical meaning. Eye/prediction/export/debug are pure readouts.

## Current exact action

N0 canonical rebase gates are green at this checkpoint: v8 spec/audit/plan/cursor
are present, active controls are compact and consistent, Markdown/math/fence/local
links validate, the generated code graph is current, control validation
reports 14 nodes with only S4‑08 active, and no Runtime/Resources file changed.

Begin N1 by freezing the supplied authoritative TOE E22 semantic artifact and
extending the existing operator generator to emit one descriptor plus forward,
pullback, stitch and reference evaluators. No live runtime cutover occurs in N1.
Do not guess missing TOE equations.

## Replacement and LOC cursor

Current runtime/resource diff versus `d3b83e1` is net `+569` LOC.

Final S4‑08.6 gates:

```text
gross production deletion vs cac9ab0  >=10000
new production code                   <=4000
net vs cac9ab0                         <=-6100
net vs d3b83e1                         <=-5500
retired assets/symbols/fallbacks       0
```

Every N3–N7 commit deletes the branch it replaces. Dead code is removed, never
left disconnected.

## Completion gate

Only N8 may close S4‑08. It requires exact/Vulkan/LOC/code-graph gates, a source
archive and Release APK from the same commit, installation, physical scan/readout
corpus, actual per-kernel timestamps, `Omega+Xi <=1500 ms`, wall `<=1800 ms`, no
revision/segment latency slope, bounded memory and no capacity/identity fault.
