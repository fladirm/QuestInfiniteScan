# Claude execution authority — Quest Infinite Merkaba Scan

Read [`contr.md`](contr.md) verbatim before changing this repository. It is the current
production closure contract and supersedes every conflicting historical note in
`README.md`, `ALGORITHM.md`, `kontrakt.md`, `errors.md`, and `.codex/`.

`.codex/` is a second agent's ledger. It is partly stale and it is **live** — another
agent commits into this working tree during sessions. Re-read `git status` and
`git log` before and after any edit; never assume the tree you read is the tree you write.

## Verified ledgers maintained for this agent

- [`.claude/MERKABA_ENV.md`](.claude/MERKABA_ENV.md) — what actually runs on this machine now.
- [`.claude/MERKABA_STATE.md`](.claude/MERKABA_STATE.md) — implementation state per contract section.
- [`.claude/MERKABA_RISKS.md`](.claude/MERKABA_RISKS.md) — evidence-backed open defects and risks.
- [`.claude/MERKABA_GEOMETRY_REVIEW.md`](.claude/MERKABA_GEOMETRY_REVIEW.md) — standing verdict on
  proposed geometry/readout replacements.

Update the ledger you invalidate in the same change that invalidates it.

## Frozen invariants (do not reopen)

```text
world truth        M8 KernelState (16 B: evidence, packedColor, colorConfidence, flags)
support            0.050 m          lattice step 0.025 m        half support 0.025 m
evidence           ON 512  OFF 128  SURFACE 640  FREE 256  cap +/-2560  clearance 0.150 m
address            signed int3 -> block 256^3 / 5 octant digits / tile 8^3 / 512 kernels
residency          32768 HOT physical tiles, 4 banks, PCG3D 2-choice cuckoo, SSD COLD
membrane           MerkabaOverlapShell: one 25 mm patch per measured occupied owner,
                   4 shared half-lattice corners, 2 triangles, no view input
readout            M8 -> membrane -> indexed FRONT -> one indirect draw
derived only       membrane, readout, GLB, 3D Tiles. Exports are never truth.
```

Forbidden in production: TSDF, Surface Nets, QEF, Marching Cubes, trilinear
reconstruction, persistent surfel/normal/mesh authority, a second geometry or
appearance database, GSplat, eye- or camera-dependent topology or winding,
billboard/glyph/card fallback, mono depth fallback, shrinking view distance as an
optimization, retuning evidence constants without device evidence.

## Change discipline

- Low code. Read the complete current flow, replace the wrong authority, delete it.
  Never add a second implementation beside one that already exists.
- Prefer editing existing files. Keep component names that scene assets bind to.
- `Runtime/Shaders/*.generated.hlsl` are produced by `Editor/Merkaba*Generator.cs`.
  Change the C# authority, regenerate, never hand-edit the generated file.
- The CPU membrane oracle (`MerkabaOverlapShell.TryBuildPatch`) and its HLSL twin are
  two hand-maintained texts, not one compiled source. Any change to one is a change to
  the other in the same commit, plus the parity test.

## Gates before claiming completion

```bash
Tools/shaders/audit_merkaba_compute_spirv.sh   # runs here today
Tools/unity/run_merkaba_tests.sh               # needs the Unity host, see MERKABA_ENV
Tools/gltf/validate_merkaba_glb.sh             # needs the Unity host + npm install
Tools/unity/build_merkaba_apk.sh               # needs the Unity host
```

State exactly which gates ran and which did not. If Quest acceptance was not executed,
write `DEVICE ACCEPTANCE PENDING`. Never claim measured performance without Quest
timestamps.
