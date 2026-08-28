# Quest Merkaba Repair Verification

All results below are filled with exact commands and outcomes as each checkpoint closes.

## R1 Geometry Authority

Commit `3d14f65` proved useful shared-authority plumbing but encoded a superseded
96-microtriangle exact-Boolean-union interpretation. R1b preserves only the plumbing
and replaces its geometry authority.

Commands:

```bash
/mnt/kingston-unity/Unity/Hub/Editor/6000.5.9f1/Editor/Unity \
  -batchmode -nographics -quit \
  -projectPath /mnt/kingston-unity/Unity/Projects/QuestMerkabaScanHost \
  -executeMethod Genesis.RoomScan.Editor.MerkabaCanonicalGeometryGenerator.GenerateForBatch
Tools/unity/run_merkaba_tests.sh
```

Corrected result:

- CPU-to-HLSL generation: PASS; 14 vertices, 8 directions, 32 possible triangles.
- Direct-rule EditMode suite: 25 passed, 0 failed, 0 skipped.
- Isolated kernel: exactly 8 octahedron faces, 0 tip sides.
- Each of 8 body-diagonal pairs: connecting base suppressed and 3 tip sides emitted on both kernels; 10 active triangles per kernel.
- Neighbour removal restores base; axis/face-diagonal neighbours do not activate tips.
- Chunk-border and negative-coordinate translation invariance: PASS.
- Cube-axis-normal anti-regression: PASS.
- Result XML: `/mnt/kingston-unity/Builds/TestResults/merkaba-results.xml`.
- Log: `/mnt/kingston-unity/Builds/TestResults/merkaba-tests.log`.

## R2 Shared Live/GLB Geometry

- Production CPU topology: direct 32-bit rule mask.
- GPU topology across chunk border equals CPU mask: PASS.
- Live shader consumes generated primitive vertex IDs: shader compile PASS.
- GLB isolated kernel: 8 triangles / 24 vertices, all non-axis normals: PASS.
- GLB body-diagonal pair: 20 triangles / 60 vertices: PASS.
- Old `MerkabaTopology`, cube `BoundaryPatchCount`, `PatchVertex`, and cube shader vertex authority removed.

## R3 Depth Pipeline

PENDING

## R4 Integration and Carve

PENDING

## R5 Residency

PENDING

## R6 Publication

PENDING

## R7 UX and Export Shell

PENDING

## R8 Full Local Verification and APK

PENDING

## R9 Device Evidence

PENDING
