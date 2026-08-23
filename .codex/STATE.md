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

## Exact next action

1. Regenerate the code graph and validate the control plane.
2. Commit only the S4-08.3 source/control set.
3. Build the Android/Vulkan IL2CPP Release APK from that exact commit.
4. Install it on the connected Quest and create a source-only `git archive` ZIP
   from the same commit.
5. Stop before S4-09 for the user's physical device audit.

## Unresolved until device audit / later DAG nodes

- Release build, installation and physical scan behavior are not yet run for this
  dirty tree and must not be claimed before execution.
- Resident exhaustion is explicit unresolved/spill state; S4-10 supplies durable
  arbitrarily long overload storage. S4-08.3 never truncates or partially commits.
