# Goal

[`new_spec.md`](../new_spec.md) is the sole canonical product and reconstruction
specification. Its baseline is `CPQ4-2026-08-24-S16-v7`; this file and the DAG may
sequence implementation but may not reinterpret or weaken it.

Build Σ-PRISM-16 as a fully on-device Quest 3 scanner whose only durable physical
world is the exact sparse carrier field

```text
Psi : Sigma_2 -> S16
```

Canonical sedenion coefficients and every state-changing decision use inherited
`num.fixed.q16_48.checked.nearest_even` semantics. Both RGB streams, both depth
views and retained temporal observations remain independent finite-footprint,
first-hit constraints until their exact Q16.48 admissible-set intersection is
formed. Geometry, topology, folds, boundaries, normals, fine detail, directional
appearance, motion state, PBR and meshes are readouts of that same carrier; none may
become a second canonical reconstruction.

The active implementation sequence is exactly `S4-00` through `S4-13` in
`.codex/TASK_DAG.json`. A node is complete only with its inspectable gate evidence.
The previous Cone-PRISM mapper remains recoverable from Git history/its previous
branch but its ContactFilm/PressureManifold, persistence and rendering code are not
part of this product branch.

The retained donor surface is deliberately narrow:

- Unity 6 / Android / Vulkan / Meta XR toolchain and lifecycle;
- GPU-native synchronized `RGB_L/R + DEPTH_L/R` capture, exact timestamped poses,
  immutable intrinsics/extrinsics and calibration epochs;
- permissions, anchors, input, task-oriented VR UI and logging;
- representation-neutral GPU fence/indirect helpers and build/deploy tooling.

Everything else is implemented from `new_spec.md` under `Runtime/SigmaPrism` and
`Runtime/Resources/SigmaPrism`. No TSDF/DTSDF, ContactFilm, explicit topology graph,
surfel/triangle world, Gaussian map, server/notebook, compatibility fallback, CPU
meshing, synchronous readback or independent texture world is permitted.

The active S4-08 closure uses one direct whole-observation GPU inverse. Image tiles,
source bundles, storage pages, proof windows and scheduling quanta cannot allocate
carrier identity or define canonical publication. The retired S4-08.3 transaction
graph may not remain as a fallback or be recreated under new names.

Completion is section 50 of `new_spec.md`, including the section 43 physical Quest
corpus. Compilation and synthetic tests alone are never the final acceptance.
