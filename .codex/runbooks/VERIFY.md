# Σ-PRISM-16 verification runbook

Apply the narrowest relevant layers; broader layers never replace exact semantic
fixtures:

1. generated algebra/operator fingerprints and bit-exact CPU fixtures;
2. CPU semantic oracle versus real-GPU lowering parity;
3. captured four-stream/invariance fixtures named by the active S4 gate;
4. code graph, control-plane and `git diff --check`;
5. Unity package compilation on the pinned Unity 6000.5 editor;
6. Android Vulkan/IL2CPP ARM64 build at consolidated vertical milestones;
7. physical Quest corpus from sections 40, 43 and 44 of `new_spec.md`.

Run `python3 Tools/unity/validate_sigma_compute_uav.py` for every shader checkpoint.
Unity compute fixtures need a real graphics backend; NullGfx import is not GPU proof.
No result may be reported as a device result unless it was run on that deployed APK.

Before a required deployment: validate, commit the exact node, archive when required,
build from that commit, hash the APK, deploy that exact artifact, then batch the
physical gate rather than rebuilding for each subpass.
