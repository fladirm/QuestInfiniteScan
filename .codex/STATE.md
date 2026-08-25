# Sigma-PRISM-16 implementation state

Updated: 2026-08-25 (Europe/Prague)

## Authority and active work

- Canonical product: `new_spec.md`, `CPQ4-2026-08-24-S16-v7`.
- Branch: `feat/sigma-prism-16-cpq4-20260822`.
- Accepted source milestones: S4-00 through S4-07; S4-08 is reopened.
- Active DAG node: **S4-08**; S4-09 remains pending and unopened.
- Active repair: **S4-08.5 direct-frame closure repair**.
- Frozen delta contract: `.codex/S4-08.5_DIRECT_FRAME_CLOSURE_PLAN.md`.
- Sole routine resume cursor: `.codex/S4-08.5_RESUME.md`.
- Current forensic authority: `refact.md`; older evidence remains in
  `s4-083-audit.md` and archived device captures.

## Active replacement cursor — 2026-08-24

- Commit `87d33f2` is not a device candidate: a 320x320 frame emitted illegal
  direct dispatch dimensions (`102400`, `204800`, `102400`) and stayed at
  root/publication/readout zero. This is an execution-lowering failure, never an
  exact-inverse no-change result.
- The narrow repair caps every binding-derived execution window at 32,512
  footprints, so the worst two-groups-per-footprint dispatch is 65,024. All direct
  dispatch wrappers reject dimensions outside `1..65535` before command recording.
  The 320x320 production recorder and full Vulkan EditMode suite pass 65/65.
- Completion accounting is now truthful: only `EVIDENCE_RETAINED`, zero fault and
  `root>=revision` is PUBLISHED and may retain evidence/increment `CommittedFrames`;
  `RESOLVED+0` is NO_CHANGE, while shader faults, incomplete post-fence states and
  stale revisions fail closed. Full Vulkan EditMode passes 66/66.
- The forensic audit in `refact.md` is closed and implementation is authorized.
- R3 replacement is accepted: image/XYZ only proposes work; physical claims and
  incident-only mutation deferral come solely from exact first-hit/four-source/S16
  closure. Vulkan/EditMode passes 64/64 without shader/bind errors.
- Dirty R4 is being replaced, not extended: complete source evidence is owned once
  per observation, referenced generation-safely, and publication exposes only a
  complete immutable view by one final root exchange.
- Proof minimization follows ownership transfer and cannot delay frame-slot reuse.
- R3, R4 and R5 land as separate gates; final production LOC must be negative
  against `d3b83e1` with no retired lifecycle or fallback.

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

The persistent-PENDING repair is committed at `e28a956`. Commit the isolated
diagnostic replacement of global Vulkan dispatch interception with explicit
one-shot plugin events, then push, build and install that exact commit. Native
Android compilation and the profiling-disabled production-dispatch parity fixture
are green. No R3/R4, S16, carrier, accepted-bit or publication change is authorized.

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
- R1 executes a production 320x320 observation through seven aligned Vulkan
  binding windows, preserves two distinct CURRENT segment/page/generation/sample
  targets and strips the right-eye bit from sample addressing. Focused Vulkan
  inverse tests pass 4/4 and resource/ABI tests pass 3/3.
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
- M6 pre-freeze Release compiler gate is green on the source candidate: Quest
  eight-UAV validation passes and the Android/Vulkan Release APK builds with zero
  shader/C# errors. Compiler warnings remain reported rather than hidden.
- First post-freeze device launch exposed a pre-ingress lifecycle regression: M5
  retained the donor MRUK `IsRoomLoaded` gate and assumed a scene-authored
  `RoomSpaceRoot`. The repair deletes MRUK room-scene loading/fallback, creates the
  root at runtime and retains OVR spatial-anchor create/save/load/localization.
  Unity Vulkan EditMode remains green at 54/54 and the Quest eight-UAV gate passes.
- Device revalidation is negative: coherent capture starts, but the first 320x320
  direct submission throws `Direct-frame execution window is incomplete or
  segmented`; the fail-closed controller then stops inverse work and revision,
  carrier, topology and readout remain zero. Source audit confirms G1-G14 in the
  S4-08.5 plan; S4-08.4 M3/M6 were not physically closed.
- S4-08.5 R2 stable-orders complete target/source keys and exact-reduces duplicate
  targets across every execution window before one final S16 reconstruction. A
  Vulkan two-window duplicate-target fixture is bit-identical under reversed source
  order; focused inverse passes 5/5 and resource ownership remains 3/3.
- S4-08.5 R3 retains unresolved exact S16 evidence under persistent handles and
  reuses it before NOVEL on later frames. Exact intrinsic closure is invariant over
  one/two/seven Vulkan windows and regular/thin edges crossing a physical segment
  boundary; all 11 applicable focused inverse tests pass with no missing bind,
  shader or C# error. Atomic publication remains intentionally red for R4.
- The dispatch-grid performance slice restores binding-derived storage windows and
  lowers only `ReduceTargetWindow`, `ClosePendingEdges` and
  `PersistPendingTargets` onto legal two-dimensional Vulkan grids. A 320x320 frame
  now uses one execution window and exact grids 51200x2, 51200x4 and 51200x2;
  one-window/four-window reduction is bit-identical and Vulkan EditMode passes
  66/66 with the Quest eight-UAV and generated-output gates green.
- Production non-development builds no longer register per-kernel GPU samplers,
  enable Unity Profiler or emit BeginSample/EndSample around direct-frame
  dispatches. All direct/indirect wrappers retain identical calls and fail-closed
  dimension validation; an Editor production-graph trace proves identical kernel
  IDs, order and 320x320 grids with profiling off. Vulkan EditMode passes 67/67.
- Continuation closure now snapshots its resolved CURRENT target ordinal into the
  accepted target before `_PendingLinks` is repurposed for retention slots;
  publication consumes only that immutable snapshot. A fixture forces root link
  `0 -> 1` aliasing, then publishes root 2 with bit-correct mapping and no
  `0x004/0x100` fault; Vulkan EditMode passes 68/68.

## Completion protocol

After S4-08.5 R1-R5 gates: regenerate code graph, validate controls and commit the
exact candidate. Archive/build/install that commit and run R6 Quest acceptance.
Only a passing physical gate may mark S4-08 done. Stop before S4-09.
