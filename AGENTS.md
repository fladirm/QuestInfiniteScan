# Sigma-PRISM-16 working contract

These instructions apply to the whole repository and preserve implementation intent
after context compaction.

## Resume order

At the start of every implementation turn and after every compaction, read in order:

1. `new_spec.md` — sole canonical reconstruction/product specification
2. `.codex/GOAL.md`
3. `.codex/STATE.md`
4. `.codex/TASK_DAG.json`
5. `.codex/SESSION_TAIL.md`
6. `.codex/DECISIONS.md` when the current node touches architecture

Trust checked-in source and verification evidence over prose. Never redo a node
marked `done` unless its evidence is absent or a later change regressed it.

## Execution budget

- Spend about 90% of effort on implementation, 5% on proportional verification and
  5% on control/prose.
- Keep at most one DAG node `in_progress`.
- Update controls only at meaningful checkpoints.
- After every completed DAG node run `python3 Tools/generate_code_graph.py`; commit
  `.codex/CODE_GRAPH.json` and `docs/architecture/CODE_GRAPH.md` with the node.
- Land every completed `S4-xx` node as its own Git commit after its acceptance
  evidence and generated code graph are current; never combine two accepted nodes
  into one checkpoint commit.
- After committing accepted `S4-08`, freeze that exact commit as the consolidated
  base milestone: create a source-only ZIP with `git archive`, build the Quest
  Android/Vulkan APK from the same commit, deploy it to the connected headset, and
  stop before activating `S4-09` for the user's audit/device evaluation.
- Prefer complete vertical slices over disconnected scaffolding.
- Report accepted DAG nodes and the next exact gate, not file-count percentages.

## Canonical product

`new_spec.md`, baseline `CPQ4-2026-08-22-S16-v6`, is frozen. The product is a fully
on-device Quest 3 scanner whose only durable physical world is:

```text
Psi : Sigma_2 -> S16
```

The canonical carrier is sparse, logically unbounded and exact Q16.48. Geometry,
topology/singularities, boundaries, detail, directional appearance, scene changes,
meshlets, PBR and GLB are inverse constraints or readouts of this same state. Never
create a parallel canonical geometry, topology, texture, scene-history or object
world.

The previous Cone-PRISM/ContactFilm/PressureManifold mapper is recoverable from Git
history only. It is not an implementation donor. The only retained donor surface is:

- Unity 6 / Android / Vulkan / Meta XR setup and build tooling;
- synchronized GPU-native `RGB_L/R + DEPTH_L/R` capture;
- exact timestamps, poses, intrinsics, extrinsics and immutable calibration epochs;
- permissions, lifecycle, anchors, input, VR UI and logging;
- representation-neutral fences, indirect-dispatch helpers and deployment tooling.

## Reconstruction guardrails

- Canonical coefficient semantics are
  `num.fixed.q16_48.checked.nearest_even`: signed 16.48, 64-bit storage, checked
  overflow and nearest-even point arithmetic. Interval arithmetic rounds outward.
- Execution layouts are replaceable exact lowerings. Native I64 or packed-32 may run
  only after bit-parity/capability gates; FP never decides canonical mutation.
- Generate the signed-XOR Cayley-Dickson table, conjugation, basis permutations,
  annihilator catalog, Hadamard/readout rows and operator fingerprints. Do not hand
  maintain tables or use dense schoolbook S16 multiplication by default.
- Preserve explicit product bracketing. Optimized operator DAGs must equal their
  semantic reference bit-for-bit and use mask/select/fixed schedules for bounded
  GPU control.
- Both RGB streams, both depth views and temporal sources remain independent until
  their exact Q16.48 admissible cells meet by componentwise max/min. Confidence
  changes interval width, never a weighted sum.
- Each finite calibrated pixel footprint constrains only the pre-hit sector and its
  first supported hit. State behind the hit receives no inclusive cell, exclusion
  cell or confidence change. UNKNOWN is never inferred EMPTY.
- Contradictory cells retain exact gaps and provenance; they never average or cancel.
- Stronger prior intervals and minimal certificates are state resistance. Broad,
  far, grazing or redundant observations may confirm but cannot blur a narrower
  supported result.
- Null-to-contact uses deterministic gauge allocation and independent-support
  probation. Contact-to-null requires proven multi-view pass-through. Coherent
  identity-preserving transport is tested before retire/recreate.
- Topology is intrinsic to regular/singular sedenion transitions and exact
  annihilator/associator gates. Do not allocate canonical charts, films,
  BoundaryCurves, half-edge graphs, split/merge objects or 3D proximity identities.
- Fine geometry and appearance are local variation of the same carrier. Gauge
  refinement is a bijective reparameterization; no displacement/texture world may
  appear beside `Psi`.
- Page/block/codec/chunk boundaries are storage only and have zero physical meaning.

## GPU and runtime guardrails

- Keep association, inverse-cell construction/meet, state mutation, transition
  algebra, gauge work, readout, culling, LOD, draw compaction and rendering on GPU.
- Use compacted work and indirect dispatch/draw. No CPU pixel loop, CPU mesh,
  synchronous GPU readback or full-world frame traversal.
- C# owns lifecycle, calibration epoch, resources, page residency, fences,
  immutable publication, staging, error reporting and export orchestration only.
- Render meshes/prediction caches are disposable readouts. Deleting them must not
  alter replay or canonical page bytes.
- No storage-buffer binding may exceed the runtime Vulkan range. Use segmented pools
  and the memory governor; pressure may evict clean pages/caches, never canonical
  detail or accepted sensor resolution.
- Sensor ingress never waits for reconstruction. Stage work by active visible,
  inverse-active, dirty and unresolved locality.

## Persistence and export

- Persist the exact sparse carrier pages, algebra/operator metadata, minimal proof
  certificates and only unresolved raw observation tiles required by `new_spec.md`.
- Durable publication precedes eviction. Restart plus the same observation sequence
  must reproduce byte-identical carrier pages/certificates.
- Persistence writes sorted logical page generations; codec mode is deterministic
  and lossless.
- Mesh/PBR/GLB are readouts and never mutate or replace `Psi`.

## Repository and safety

- Active reconstruction code lives only under `Runtime/SigmaPrism` and
  `Runtime/Resources/SigmaPrism`.
- Do not restore old TSDF/DTSDF, ContactFilm/PressureManifold, GS/DiffSoup/server,
  old persistence or compatibility fallback code to this branch.
- Preserve unrelated user changes and untracked archives/captures. Never use
  destructive Git recovery commands.
- Add Unity `.meta` files for every new Unity-visible asset.
- Never commit credentials, LAN addresses, device identifiers, room captures,
  generated models, Unity caches, Android products or third-party weights.
- Never modify anything under `~/.codex`.

## Verification and checkpoint protocol

Verification is proportional: exact unit/captured fixtures and compilation during
implementation; Android/device builds only at meaningful forward/inverse, paging,
renderer/export and final physical milestones. Never claim an unrun device result.

Before compaction, handoff or a major commit:

1. update `.codex/STATE.md` with current node, changed files, exact next action and
   real evidence;
2. update `.codex/TASK_DAG.json`;
3. regenerate the code graph, then run `python3 Tools/validate_goal_state.py`;
4. record only actual architectural decisions in `.codex/DECISIONS.md`;
5. replace `.codex/SESSION_TAIL.md` with the latest two exchange snapshots.
