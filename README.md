# Σ-PRISM-16

Σ-PRISM-16 is a pure on-device Quest 3/3S spatial scanner. Its canonical
reconstruction baseline is `CPQ4-2026-08-24-S16-v7`, defined exclusively by
[`new_spec.md`](new_spec.md).

The only durable physical world is one sparse exact carrier:

```text
Ψ : Σ₂ → S16
```

Geometry, intrinsic topology, folds, boundaries, fine detail, directional
appearance, scene evolution, render meshes and exports are readouts or constraints
of that carrier. They are not parallel reconstruction states.

## Retained Quest shell

The repository retains the proven Unity/Meta XR environment for:

- direct GPU acquisition of `RGB_L`, `RGB_R`, `DEPTH_L` and `DEPTH_R`;
- timestamp pairing and immutable calibration epochs;
- exact per-sensor poses, intrinsics, extrinsics and cone LUT construction;
- Quest permissions, anchors, lifecycle, input and operator UI;
- Vulkan compute, fences, indirect work and Android build/deploy tooling.

The former mapper is available only from Git history and its archival branch. This
branch contains no TSDF/DTSDF, ContactFilm/PressureManifold atlas, Gaussian map,
server/CUDA pipeline, old chunk persistence, CPU meshing or compatibility fallback.

## Implementation order

The active goal is the independent DAG in [`.codex/TASK_DAG.json`](.codex/TASK_DAG.json):

```text
S4-00  clean Quest shell
S4-01  exact Q16.48 NumericDomain and generated S16 operators
S4-02  sparse exact carrier and lossless codecs
S4-03  forward geometry readout
S4-04  joint inverse depth readout
S4-05  intrinsic singular topology
S4-06  joint RGB inverse readout and certificates
S4-07  bijective carrier-gauge detail refinement
S4-08  bounded pose-gauge intersection
S4-09  deterministic scene evolution
S4-10  infinite paging, persistence and revisit
S4-11  disposable GPU meshlet readout
S4-12  directional appearance and GLB/PBR readout
S4-13  complete physical Quest acceptance corpus
```

Each stage must pass its semantic gate before the next stage may mutate canonical
state. Physical Quest acceptance, not compilation alone, closes the product goal.

## Source layout

```text
Runtime/SigmaPrism/             canonical Σ-PRISM runtime and retained rig bridge
Runtime/Resources/SigmaPrism/   generated/operator/readout Vulkan shaders
Runtime/Core/                   representation-neutral lifecycle and XR helpers
Runtime/UI/                     task-oriented Quest controls and diagnostics
Editor/                         idempotent Quest shell/build setup
Tests/Editor/                   exact semantic, ABI and captured-fixture gates
Tools/                          generator, verification and build/deploy tooling
```

## Development contract

Read `new_spec.md`, then `.codex/GOAL.md`, `.codex/STATE.md`,
`.codex/TASK_DAG.json`, `.codex/SESSION_TAIL.md` and relevant decisions before
implementation. [`AGENTS.md`](AGENTS.md) contains the repository-wide execution and
safety rules.
