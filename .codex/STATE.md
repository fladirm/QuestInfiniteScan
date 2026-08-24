# Sigma-PRISM-16 implementation state

Updated: 2026-08-24 (Europe/Prague)

## Authority and active work

- Canonical product: `new_spec.md`, `CPQ4-2026-08-24-S16-v7`.
- Branch: `feat/sigma-prism-16-cpq4-20260822`.
- Accepted predecessors: S4-00 through S4-07.
- Active DAG node: S4-08; S4-09 remains pending and unopened.
- Active repair: **S4-08.4 direct whole-frame S16 inverse cutover**.
- Frozen implementation contract: `.codex/S4-08.4_DIRECT_FRAME_PLAN.md`.
- Sole routine resume cursor: `.codex/S4-08.4_RESUME.md`.
- Forensic evidence: `s4-083-audit.md` and archived device captures.

## Frozen ontology

- `Psi : Sigma_2 -> S16` is the only canonical physical state.
- Canonical Q16.48 values, validity, gaps, provenance, proof and intrinsic
  transition results are invariant under execution/storage decomposition.
- The complete coherent `RGB_L/R + DEPTH_L/R` frame is one observation and one
  sparse revision boundary.
- Page/block/workgroup/proof-window dimensions have no identity or physical meaning.
- Pending gauges are transient local parameterizations/evidence, not canonical
  addresses, prediction authority or a second world.
- GPU owns source-cell construction, exact meet/lift, pending closure, transition,
  scatter, publication and readout. CPU owns resources, calibration and fences.
- Async telemetry is diagnostic-only and cannot schedule or mutate.

## Why S4-08.3 is superseded

The Quest device audit proved the old execution model itself violates the ontology:

- 320x320 input becomes a visible 5x5 set of 25 fresh logical page identities;
- subsequent frames allocate duplicate Morton gauges over the same footprint;
- the 64-bundle arena loses later side/rear evidence before immutable ownership;
- raw retention hard-stalls after exactly 4096 closed blocks;
- the 12-record proof spill has no continuation;
- page/transaction/token closure delays visibility by tens of seconds;
- Release profiler recorders return no usable GPU samples.

These are not repaired by larger pools, altered token costs or renamed transactions.
ADR-S413 retires the page/bundle/transaction/token foreground graph.

## Preserved implementation

- Exact NumericDomain and generated signed-XOR/bracketed S16 operator plans.
- Sparse signed-64 carrier, immutable generations and exact lossless codec.
- Exact geometry/readout, four independent depth/RGB source-cell mathematics,
  first-hit rules, conflict/gap/provenance and width-derived information mass.
- Exact topology/annihilator/associator and gauge-refinement mathematics.
- S4-08 pose-gauge interval solve, corrected same-frame reraster/calibration, rigid
  GPU SE(3), pose provenance and fail-closed graphics completion.
- Capture/sync, forward raster prediction, XR/UI and deployment/toolchain.

## Active direct-frame contract

```text
owned coherent frame
 -> current/pending/continuation/novel proposals
 -> materialized independent D_L/D_R/RGB_L/R 16D cells
 -> exact target grouping and meet/lift
 -> pending reuse/closure/promotion
 -> complete immutable evidence journal
 -> incident claimed-edge transition closure
 -> shadow carrier scatter
 -> atomic frame-revision root flip
 -> immediate disposable world-space readout
```

The complete run, file deletion boundary, fixed kernels/resources, deterministic
extent allocation, proof/raw ownership and milestone gates are frozen in the plan.

## Current exact action

M6: run final generated/control/source gates, commit accepted S4-08, archive that
exact commit, then build and install the Quest Android/Vulkan Release. Production
source changes are allowed only for a concrete Release compiler blocker.

## Required end state

- Old streaming lifecycle files and compiled references are absent, not disabled.
- New production additions stay <= 4,200 LOC and net runtime/resource deletion is
  >= 5,500 LOC unless a stop-and-simplify review proves otherwise.
- Same observation yields byte-identical state/proof/provenance under legal
  workgroup, proof-window and backing-page decompositions.
- Complete frames/evidence are owned losslessly; no fixed scratch/raw capacity is a
  canonical cap.
- First exact support becomes visible without page completion or proof minimization.
- Release profiler reports actual per-kernel GPU time or explicit unsupported state.
- Three-minute Quest scan has continuous directional coverage/progress, no 5x5
  board, duplicate-gauge loop, lost frames, capacity cliff or stale/faulted work.

## Last green evidence before cutover

- Source HEAD entering M0: `dc75700469c26d932afed5b486783c3e40585db4`.
- Unity Vulkan EditMode 73/73, generated operators, code graph/control,
  `git diff --check` and Quest eight-UAV validation were green.
- Device evidence remains negative acceptance and must not be restated as success.
- S4-08.4 M0 control gate is green: DAG JSON parses, generated code graph is
  current, control validation names active S4-08 and diff whitespace is clean.
- M1 generated ABI/resource gate is green: generated outputs are current and Unity
  Vulkan EditMode passes 76/76 including the eight-UAV frame ABI dispatch.
- M2 direct inverse gate is green: the new whole-frame shader materializes four
  independent source cells and resolves the novel exact state against a CPU oracle;
  full and three-footprint execution partitions are bit-identical and Unity Vulkan
  EditMode passes 77/77.
- M3 closure/publication gate is green: a complete coherent-frame candidate closes
  pending exact components, allocates storage-independent carrier extents, scatters
  accepted S16 deltas to shadow pages and publishes one root-visible revision with
  bit-identical state/proof/provenance across tested decompositions. Incident edges
  remain fail-closed unresolved. Full Unity Vulkan EditMode passes 78/78; generated,
  bind/UAV and diff gates are green.
- M4 direct live cutover gate is green: only `SigmaFrameGraph` is instantiated;
  the old stream lifecycle, ABI, scheduler, proof owner, Morton allocation and
  associated tests are absent; direct readout/telemetry compile and Vulkan EditMode
  passes 53/53. The code graph is current and the net diff is -22,715 LOC.
  diff, shader bind/UAV and manual changed-file gates are green.
- M5 stable room-frame gate is green: scan ingress waits for a localized spatial
  anchor; all canonical calibration/prediction/pose inputs retain one coherent-frame
  room transform snapshot and only temporary XR presentation maps back to current
  Unity world. Focused rigid-anchor parity and full Unity Vulkan EditMode pass
  54/54; generated/diff gates are green.

## Completion protocol

After M6 gates: regenerate code graph, validate controls, commit accepted S4-08,
create a source-only `git archive`, build/install Release APK from the same commit,
run and capture the Quest acceptance. Stop before S4-09.
