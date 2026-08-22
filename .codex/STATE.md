# Execution state

Updated: 2026-08-21 (Europe/Prague)

## Source of truth

- `specka.md` is the frozen canonical Cone-PRISM-Q3 specification
  (`CPQ3-2026-08-21-v6`).
- `.codex/TASK_DAG.json` is the only active pursuit DAG.
- `.codex/runbooks/Q3-15.6_PRESSURE_MANIFOLD_ATLAS_REBASE.md` is the binding
  execution order for the current geometry gate.
- Q3-15.6 is an architectural topology rebase of the audited `3521c44`
  checkpoint. It must not regress to rectangular ContactFilm topology, patch soup,
  TSDF/DTSDF, surfels, triangle soup, Gaussian training, CPU geometry readback or
  server reconstruction.

## Repository and branch safety

- Active branch: `feat/cone-prism-pressure-manifold-atlas-20260821`.
- Current committed parent: `6c377adb78ff`; the Q3-15.6 tree is not committed yet.
- Donor checkpoint: Q3-15.5 `3521c44`.
- Legacy implementations remain recoverable from git/archive branches and are
  deliberately absent from the active production tree.
- Do not push this run. Commit locally, create a workspace ZIP with `git archive`
  from that exact commit, then build and deploy the exact commit.
- Never add `.device-forensics/`, `.source-archives/`, existing ZIPs, captured room
  imagery, device identifiers or build products to the commit.

## Current DAG gate

- Q3-15.6 is the sole `in_progress` node.
- Source implementation, documentation cleanup, schema-v6 persistence and static
  verification are complete in the working tree.
- Commit/archive, Android build, exact APK installation and one batched physical
  Quest geometry/lifecycle run remain before Q3-15.6 can be accepted.
- Q3-07 through Q3-15 remain physically unaccepted after the forensic audit even
  where their implementation is present. Q3-16 through Q3-22 remain behind the
  topology gate.

## Q3-15.6 implemented working tree

- Contact posterior, topology atlas and derived meshlet materialization are separate
  ownership layers. A chart rectangle is only a numerical parameter domain.
- Provisional 8x8/cross-eye candidates use one complete evidence hook followed by a
  capacity-derived pointer-jump bound. Components select a global orthonormal frame,
  refit directly from original finite-cone samples and reject non-representable
  transitive unions instead of publishing one root tile posterior.
- Measured Grid16 support is converted by deterministic marching squares into
  arbitrary contour segments. The former four-edges-per-film frontier capacity,
  rectangle validator and rectangle closure tests are removed.
- Generation-safe measured half-edges are welded only from explicit continuation
  evidence: covariance/coincidence, sidedness, first-hit ordering, visibility,
  independent view bins and pose/calibration quality. Unpaired arcs are ordered into
  manifold-level FrontierLoops.
- Latent closure is topology-only UNKNOWN with FilmID zero. It is excluded from
  prediction, ordinary display geometry and GLB export; no fake Euclidean back sheet
  is asserted in unobserved space.
- One shared BoundaryCurve atlas record owns both chart incidences and a precomputed
  cell-intersection cache. Canonical evidence-aligned split emits two supported
  partitions along a boundary/residual separator instead of fixed four quadrants.
- Dirty topology islands receive a bounded GPU elastic solve: smooth links couple
  position/normal, creases preserve positional continuity with hinge freedom, and
  supported discontinuities do not smooth across the boundary.
- Global manifold/component identity is independent of storage chunks. Generation-
  safe cross-chunk portals retain ghost endpoints; staging no longer creates a new
  optical seed or physical latent seam.
- Contact-normal covariance separates sensor, pose/calibration, motion/mixed-pixel
  and model terms from cone footprint bandwidth. Normal estimation chooses the
  smallest stable boundary-safe 3x3/5x5/7x7 support.
- GPU information-gain ingress ranks new surface/side, posterior reduction,
  footprint, angular/baseline diversity, boundary value, sharpness and exposure;
  motion/time remains starvation fallback only.
- Vulkan resource retirement is fence-safe. Active/dirty lists, count/validate/commit
  mesh publication, GPU culling and indirect rendering remain preserved.
- Geometry evidence integrates every sensor ingress. Topology and derived meshlets
  coalesce two ingress frames transactionally; deterministic capacity-bounded
  hook/shortcut convergence remains intact without dropping observations or lowering
  canonical detail.
- Canonical persistence is schema v6 and includes atlas contours, half-edges,
  FrontierLoops, shared boundary topology/cache, continuation evidence, elastic
  state and cross-chunk portals.
- The active project no longer contains production TSDF, Surface Nets, triplanar,
  GSplat, DiffSoup/server, XAtlas or their stale setup/documentation paths.

## Verified evidence

- Unity 6000.5.9f1 Vulkan EditMode: 84 total, 84 passed, 0 failed, 0 skipped.
  Results: `/mnt/kingston-unity/Builds/TestResults/editmode-results.xml`.
  Log: `/mnt/kingston-unity/Builds/TestResults/editmode.log`.
- `Tools/unity/validate_prism_compute_uav.py`: passed; all reachable PRISM kernels
  remain at or below the Quest/Adreno eight-UAV limit.
- `git diff --check`: passed.
- Code graph digest `402d1527bbdd`: 161 source files, 2192 symbols, 1627 methods/
  functions, 182 GPU kernels and 17 event links.
- `Tools/validate_goal_state.py`: control plane valid; 24 nodes, Q3-15.6 sole active.
- No Android/APK/install claim exists yet for this working tree. Earlier APK hashes
  belong to rejected implementations and must not be reused.

## Next exact actions

1. Re-run graph/control/static checks after this state update.
2. Review the staged path set and commit the exact Q3-15.6 tree while excluding all
   archives, captures and generated builds.
3. Create `QuestInfiniteScan-Q3-15.6-<commit>-source.zip` using `git archive` and
   record its SHA-256.
4. Build a fresh Android ARM64/Vulkan APK from that committed tree and require the
   Unity BuildReport success marker with zero errors.
5. Install that exact APK on the one authorized Quest and record APK SHA-256/package.
6. Run one batched physical acceptance: continuous support, no rectangular cards or
   room-spanning curtains, continued scan growth, front/back thin surfaces, chunk
   continuity and Stop/Start retention.
7. Close Q3-15.6 only after the physical evidence passes. Otherwise preserve the
   gate and repair the demonstrated systemic cause.

## Safety and quality

- Keep build caches and device captures outside git.
- Do not lower sensor resolution, chart/microtile detail, uncertainty physics,
  topology guarantees or GPU-only/indirect ownership to make acceptance easier.
- Tests are batched at the vertical milestone; implementation remains the dominant
  effort.
