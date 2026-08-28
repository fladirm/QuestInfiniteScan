# Quest Merkaba Production Closure State

BASE: `0e9081060ed1068aad6e075f4961ad25b72245ff`
BRANCH: `fix/merkaba-production-closure`
HEAD: `f6d011a8e16dd84ce9df83d734f88fb2d04bd6e4`
GOAL AUTHORITY: `/mnt/aidisk/prace/.codex-pursuits/quest-merkaba-production-closure/REPAIR_GOAL.md`
GOAL SHA-256: `9135e66973e4fe2e36af2cb67869a56afcc6aee75b9b57f50fdd269e109e923f`

## DONE

- Exact forensic baseline verified clean.
- Isolated repair branch created.
- Immutable pursuit authority copied outside the repository.
- R1 shared CPU-to-HLSL authority/generator infrastructure committed at `3d14f65`.
- Geometry-contract correction recorded in the external goal authority; exact Boolean-union extraction is forbidden.
- R1b direct authority: six octahedron vertices, eight body-diagonal rules, eight neighbour-centre apexes, 32 possible triangles.
- R1b removed the 96-microtriangle analytic-union oracle and all suppression/coverage tables.
- R2 CPU topology, generated HLSL, live compute/shader, and GLB now consume the same direct primitive IDs and winding.
- R2 emits 8 faces for an isolated kernel and 10 primitives per member of a single body-diagonal pair.
- R3 depth kernels guard padded dispatch and every neighbour read before access.
- R3 dilation is stereo and executes the exact `256..1` sequence.
- R3 retains only the latest raw sensor frame and preprocesses it once at the consuming integration tick.
- R3 integrates both depth eyes in one compute dispatch with SURFACE winning over conflicting destructive FREE.
- R4 removed dense `integrationChunks × 32768` depth projection.
- R4b depth pixels generate a deduplicated union of the original three ray-band candidates and three dominant-axis boundary guards.
- R4b candidates are work only; both eyes re-run the preserved depth relation, normal, dilation/disparity, distance and angle quality predicates before positive evidence.
- R4 CARVE projects only kernels previously touched by valid surface evidence, retaining local negative evidence down to the export-known-free threshold.
- R4 uses transient empty GPU pages; only nonzero SURFACE-derived state materializes a canonical chunk during synchronization/eviction.
- R4 FREE in never-seen world allocates no canonical chunk; false foreground carve remains correct across a negative chunk border.
- R5 canonical chunks maintain a derived 6-face × 32² occupancy summary (768 bytes) for nonresident topology queries; it is not persisted authority.
- R5 resident, pending-eviction, summarized-nonresident, and reloaded body-diagonal neighbours produce bit-identical primitive masks across an XYZ chunk corner.
- R5 keeps a pending page live until its snapshot and summary replacement are ready, and permits only one asynchronous eviction at a time.
- R5 render residency refreshes every render frame independently of 15 Hz integration, with a one-chunk leave guard and stable previous-membership score.
- R6 occupancy transitions append unique dirty resident slots; residency movement alone cannot dirty canonical topology.
- R6 rebuilds one dirty chunk as 512 × 64 flat threads, one lattice kernel/thread, with eight body-diagonal queries and one atomic record reservation per occupied kernel.
- R6 persists compact three-vertex primitive records in two arithmetic bank segments; live draw invokes only active triangles.
- R6 finalize publishes bank/count/args/version atomically only after a complete build.
- R6 measured overflow retains the prior bank and exact draw unchanged while an asynchronous replacement is migrated and swapped.
- R6 clean/static frames execute zero topology/publication workgroups; diagnostic readback is coalesced to at most 1 Hz.
- R7 scan opacity is live 0..1; opaque and ghost-overlay material states are selected on CPU with no per-vertex/per-fragment branch.
- R7 SAVE/LOAD/EXPORT share one reactive operation state with indeterminate spinner, measured progress, stage text, and busy button labels.
- R7 export captures full canonical chunk evidence, performs one sparse radius-1 closing with strong-FREE veto, and selects observed-frontier shells without mutating the grid.
- R7 components without usable FREE evidence retain a one-kernel exterior compatibility shell; synthetic heal colour is deterministic and export-local.
- R7 GLB continues through the shared direct Merkaba primitive authority and the existing PBR writer; the validator now validates the exact supplied path.

## CURRENT

- R8: isolated real GPU timestamp telemetry, cleanup, full local verification, and fresh Android APK.

## NEXT

- R8: add real per-stage GPU timestamps using the generic proven `OtherScan` mechanism, never profiler-derived timing.
- R9: install/run on the one Quest if authorized and collect timestamps, screenshots, GLB, logs, and closure evidence.

## TEST STATUS

- Unity EditMode after R7: 52/52 passed, 0 failed, 0 skipped.
- CPU/HLSL byte identity, CPU/GPU cross-chunk mask identity, and direct GLB counts/non-axis normals passed.
- Isolated kernel and every body-diagonal pair rule passed; axis/face-diagonal non-activation passed.
- Depth border/padding, stereo dilation, exact step sequence, consumed-frame cadence, right-eye-only production integration, and false-foreground carve passed.
- Sparse GPU surface/carve work, no dense fallback symbol, free-volume sparsity, transient-page noncanonicality, negative chunk-border carve, and diagonal ray+guard candidate union passed.
- Boundary-summary residency invariance, pending live publication, render/integration independence, render-distance use, and 5 cm guard-band stability passed.
- Flat-parallel dirty publication, CPU/GPU primitive equality, static publication stability, and overflow-safe atomic replacement passed.
- Evidence-aware export rear pruning, two-sided wall, UNKNOWN heal, strong-FREE veto, large opening, diagonal wall, chunk-border, determinism, no-mutation, and canonical-GLB tests passed.
- Opacity shader branch anti-regression and required Quest menu/progress controls passed.
- Fixture GLB and exact-path validator: 0 errors, 0 warnings; 4,948 bytes, 40 triangles, POSITION/NORMAL/COLOR_0 and matte PBR.

## BUILD STATUS

- Baseline APK exists; no repair APK built yet.

## DEVICE STATUS

- Device status will be checked at R9; existing device evidence belongs to the baseline.

## MEASUREMENTS

- Baseline steady FPS: ~38.32.
- Baseline GPU utilization: ~97.9%.
- Baseline App time: ~22.19 ms.
- Baseline CPU+GPU time: ~29.41 ms.
- Baseline GLB: cube-only axis normals, 775,724 triangles.

## REAL BLOCKERS

- None.
