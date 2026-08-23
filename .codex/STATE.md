# Sigma-PRISM-16 implementation state

Updated: 2026-08-23 (Europe/Prague)

## Authority and repository

- Canonical product: `new_spec.md`, `CPQ4-2026-08-22-S16-v6`.
- Branch: `feat/sigma-prism-16-cpq4-20260822`.
- S4-08.3 base: `8087a6ef1688af760642bb953eb2f2dd51509610`.
- Active node: `S4-08`, repair run `S4-08.3 bounded streaming exact
  transactions`. `S4-09` remains pending and unopened.
- Accepted predecessors: S4-00 through S4-07.
- User archives, device forensics, captures and old APK/source archives remain
  untouched and uncommitted.

## Frozen S4-08.3 contract

- `Psi : Sigma_2 -> S16` is the only canonical physical world.
- Canonical Q16.48 value and validity results are independent of scheduling,
  partitioning, token budget, pause/resume and source interleaving.
- A page transaction may span frames but publishes only through one completed
  revision manifest; partial Psi is never visible.
- Sixteen-sample microtiles, 64-sample proof blocks, eight-source scratch and
  twelve-candidate arrays are bounded execution windows, never canonical caps.
- Complete sealed evidence and proof candidates remain lossless. Coalescing and
  reverse-order redundancy close to a fixed point before publication.
- Multi-frame transactions own copied GPU payload and never retain capture or
  prediction leases.
- Exact candidate transition validation is canonical; topology/readout caches are
  disposable and cannot authorize mutation.
- CPU owns resources, command recording and nonblocking completion tickets only.
  No readback, CPU canonical scheduler, fallback or hardware-async queue exists.

## S4-08.3 implementation closure

- Persistent generation-safe source, transaction, proof, transition, revision and
  derived-work arenas are driven by generated deterministic token costs.
- Ingress copies immutable RGB-D/calibration/pose/provenance payload before sensor
  leases are released. Exact mixed-eye inverse runs in 16-sample GPU microtiles.
- Proof uses stable ordering/coalescing, persistent exclusive prefix/suffix meets,
  bounded pair/removal windows and one reverse-order candidate decision per
  quantum. Resident exhaustion fails closed and never publishes truncated proof.
- Historical revalidation pins one sealed manifest generation, rasterizes one page
  per quantum and replays association/proof only after the snapshot completes.
- Dormant evidence retains complete owned sources and reactivates only when an
  exact dependency capable of changing the result changes.
- Publication exposes all shadow generations through one revision indirection;
  derived topology/readout starts only from the published revision.
- Active compute bindings stay within Quest's eight-UAV range. Work-graph page
  visibility is read-only SRV state.
- Renderer/inverse teardown keeps GPU ownership behind the final graphics-queue
  completion ticket; polling faults quarantine instead of recycling resources.

## Green evidence

- Generated Sigma operators and streaming ABI/cost outputs are current.
- `git diff --check` is green.
- Quest static compute validation is green at the eight-UAV limit.
- Vulkan streaming contract suite is 4/4 green.
- The GPU proof fixture closes 30 candidates to the same canonical 16-certificate
  essential set; CPU 1/2/7/max partition and three-interleaving parity is green.
- Unity 6000.5.9f1 Vulkan EditMode is 69/69 green with no shader error.
- The first S4-08.3 Release attempt reached Android/Vulkan shader compilation and
  exposed four portability defects that EditMode variants did not compile. The
  affected work-graph, proof, publication and derived shaders were manually
  reviewed with their ABI and host dispatch/bind graph and replaced as complete
  files. The focused Vulkan streaming suite is 4/4 green, the Quest eight-UAV
  gate and `git diff --check` are green; the repaired Release is not yet claimed.
- The complete Android compiler inventory then exposed uniformity/dataflow issues
  in inverse, transition, historical revalidation and the same proof/derived
  variants plus one nested scheduler unroll. All six shaders were reviewed with
  their host bindings and replaced as complete files. The resulting focused
  Vulkan streaming gate is 4/4 and UAV/diff gates remain green.
- The next Release compiler reached `EvaluateTransactionMicrotile` without a
  shader error but spent over three minutes expanding its exact fixed reductions.
  `SigmaStreamInverse.compute`, its ABI and complete host bind/dispatch path were
  manually reviewed and the whole shader was re-emitted with identical sequential
  Q48/validity ordering under bounded `[loop]` lowering rather than forced clone
  expansion. Focused Vulkan streaming remains 4/4 and the UAV limit remains 8.
- The remaining Android compiler stall was the still-monolithic inverse entrypoint,
  not an ABI error. After a complete-file/manual dataflow audit it is now one
  compiler-bounded five-stage dispatch chain: exact depth/projective preparation,
  RGB-left contraction, RGB-right contraction, source-ordered exact meet, then
  checked final lift/cursor advance. The stages share only transaction-owned
  projective scratch; no intermediate stage advances progress or publishes `Psi`.
  The split preserves source order, Q16.48 validity, L/R provenance masks and the
  final candidate round-trip, and removes the prior non-atomic shared-mask race.
  Generated cost metadata accounts for all five stages. Vulkan streaming is 4/4,
  full EditMode is 69/69, the UAV limit is 8 and `git diff --check` is green.
- Release build `3924c6e4447a613cabe3388e38dce67198b725be` compiled the
  five-stage inverse and scheduler successfully, then the player gate rejected 45
  repeated diagnostics from one unique proof defect: nonzero lanes returned from
  `SigmaProofReduceSource` before lane zero finished canonical metadata reduction,
  so the following source call reached its barrier through varying flow. The full
  proof shader/ABI/host sequence was manually audited and the reducer re-emitted as
  one uniform 64-lane schedule with a closing sync after lane-zero publication.
  Exact reduction order and proof bytes are unchanged; generated costs account for
  the four added source-closing barriers and focused Vulkan proof/streaming is 4/4.

## Exact next action

1. Commit the compiler-bounded five-stage complete-file inverse replacement and
   current generated code graph.
2. Build the Android/Vulkan IL2CPP Release APK from that exact commit.
3. Install it on the connected Quest and create a source-only `git archive` ZIP
   from the same commit.
4. Stop before S4-09 for the user's physical device audit.

## Unresolved until device audit / later DAG nodes

- Release build, installation and physical scan behavior are not yet run for this
  dirty tree and must not be claimed before execution.
- Resident exhaustion is explicit unresolved/spill state; S4-10 supplies durable
  arbitrarily long overload storage. S4-08.3 never truncates or partially commits.
