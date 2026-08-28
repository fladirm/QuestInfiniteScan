# Quest Merkaba Repair Verification

All results below are filled with exact commands and outcomes as each checkpoint closes.

## R1 Geometry Authority

Commands:

```bash
/mnt/kingston-unity/Unity/Hub/Editor/6000.5.9f1/Editor/Unity \
  -batchmode -nographics -quit \
  -projectPath /mnt/kingston-unity/Unity/Projects/QuestMerkabaScanHost \
  -executeMethod Genesis.RoomScan.Editor.MerkabaCanonicalGeometryGenerator.GenerateForBatch
Tools/unity/run_merkaba_tests.sh
```

Result:

- Canonical HLSL generation: PASS.
- Unity C# compilation: PASS.
- EditMode: 39 passed, 0 failed, 0 skipped.
- R1 geometry cases: 26 passed, including analytic union, axis/diagonal/body neighbours, solid/walls/corners/sheets/cylinder/sphere, chunk borders, negative coordinates, HLSL identity, and cube anti-regression.
- Result XML: `/mnt/kingston-unity/Builds/TestResults/merkaba-results.xml`.
- Log: `/mnt/kingston-unity/Builds/TestResults/merkaba-tests.log`.

## R2 Shared Live/GLB Geometry

PENDING

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
