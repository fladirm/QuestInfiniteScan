# Quest Merkaba Production Closure State

BASE: `0e9081060ed1068aad6e075f4961ad25b72245ff`
BRANCH: `fix/merkaba-production-closure`
HEAD: R1b/R2 checkpoint (resolve with `git rev-parse HEAD` after commit)
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

## CURRENT

- R3: fix depth shader OOB access, include dilation step 1, preprocess only consumed frames, and integrate both depth eyes.

## NEXT

- R3: depth OOB, complete dilation, stereo consumption, and preprocess cadence.
- R4-R9 remain mandatory in the external goal authority.

## TEST STATUS

- Direct-rule Unity EditMode: 25/25 passed, 0 failed, 0 skipped.
- CPU/HLSL byte identity, CPU/GPU cross-chunk mask identity, and direct GLB counts/non-axis normals passed.
- Isolated kernel and every body-diagonal pair rule passed; axis/face-diagonal non-activation passed.

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
