# Merkaba pursuit state

```text
BASE COMMIT: 2fdaaae71f60b21b7853e67db943fc42f75d0c2f
WORK BRANCH: feat/simple-infinite-merkaba
HEAD: da0b4ac (complete implementation/tooling checkpoint; closure ledger follows)
OTHER_SCAN_ROOT: /mnt/aidisk/prace/otherscan
TARGET UNITY HOST: /mnt/kingston-unity/Unity/Projects/QuestMerkabaScanHost
CURRENT TASK: Final ledger and clean-worktree closure
```

## DONE

- Deterministically bootstrapped clean target `main` and the work branch.
- Selected and froze the read-only OtherScan donor, Unity, Android, ADB, host, and APK paths.
- Created the independent target host; its package link resolves only to this repository.
- Preserved the clean QRS depth/RGB/normal/dilation/projection/quality/anchor frontend.
- Implemented the one sparse signed-coordinate `MerkabaGrid`, 32-cubed dense chunks,
  reversible signed evidence, hysteresis, RGB refinement, and valid-free-space carving.
- Froze the canonical 5 cm support, 2.5 cm lattice, 24-patch local basis, 26-neighbour
  predicates, and lexicographic exact ownership without an exponential table.
- Implemented bounded GPU pages, one integration pass, local transition dirtiness,
  GPU topology compaction, chunk culling, and indirect fixed-primitive rendering.
- Implemented canonical-only save/load/resume and spatial-anchor-aligned room coordinates.
- Implemented deterministic offline GLB PBR output with POSITION/NORMAL/COLOR_0/indices.
- Adapted donor controller-ray, menu-following, UI Toolkit interaction, and menu styling;
  all six required actions are bound only to the Merkaba scanner.
- Consolidated one target setup wizard and generic test/build/deploy scripts.
- Deleted the superseded dense field, generated-surface, appearance cache, splat,
  alternate module, native atlas/optimizer, and old setup/UI authorities.
- Sanitized Meta's disabled DevAgent after its build hook; final APK contains no donor
  LAN address/token and no forbidden reconstruction signature.
- Completed source, host, serialized-scene, package, performance, and APK audits.

## NEXT

- NONE.

## TEST STATUS

- Unity EditMode/Vulkan: PASS, 30/30, 0 failed, 0 skipped, 3.199 s.
- Production GPU tests: false foreground carved while true wall persists; topology matches
  CPU ownership across a chunk boundary.
- GLB interoperability: PASS, Khronos validator 0 errors/0 warnings; 432 POSITION/NORMAL/
  COLOR_0 vertices and indices, metallic 0, roughness 0.85.
- C#/shader log audit: PASS, no compiler errors, shader errors, or missing scripts.

## BUILD STATUS

- PASS: Unity prepare and Android build explicit success markers present.
- APK: /mnt/kingston-unity/Builds/QuestMerkabaScan/QuestMerkabaScan-release.apk
- APK SHA-256: d91e6777eb1ac2af952ed41ff61649e95c6f0954ceeec3b7fbec8dd4b0b3bdfc
- APK size: 63,115,094 bytes; ARM64; min/target SDK 32/36.

## DEPLOY STATUS

- PASS: exactly one authorized Quest 3S (`340YC20G7X0QZ4`).
- Fresh APK installed with `adb -s 340YC20G7X0QZ4 install -r -d`.
- Device package `com.genesis.questmerkabascan` reports version 0.1.0 (code 8).

## REAL BLOCKERS

- None.
