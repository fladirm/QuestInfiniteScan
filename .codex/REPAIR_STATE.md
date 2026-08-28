# Quest Merkaba Production Closure State

BASE: `0e9081060ed1068aad6e075f4961ad25b72245ff`
BRANCH: `fix/merkaba-production-closure`
HEAD: `ee6071bd79b5c386791a465b46ba92883b0095c4`
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

## CURRENT

- R4: replace dense volume-chunk integration with surface candidates plus carve of existing evidence; prohibit free-only chunk allocation.

## NEXT

- R4: surface-driven positive work, existing-state carve work, free-volume sparsity, and carve regressions.
- R5-R9 remain mandatory in the external goal authority.

## TEST STATUS

- Unity EditMode after R3: 30/30 passed, 0 failed, 0 skipped.
- CPU/HLSL byte identity, CPU/GPU cross-chunk mask identity, and direct GLB counts/non-axis normals passed.
- Isolated kernel and every body-diagonal pair rule passed; axis/face-diagonal non-activation passed.
- Depth border/padding, stereo dilation, exact step sequence, consumed-frame cadence, right-eye-only production integration, and false-foreground carve passed.

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
