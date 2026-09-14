# PROGRESS

| Cut | Commit | Status | Device receipt |
|---|---|---|---|
| C00 genesis | 98eb4a5 | done | — |
| C01 device envelope | 0e09df3..b151cf1 | done | `docs/evidence/C01` |
| C02 executor | 11f7178 | done | live runs 2026-09-14 |
| C03 sensor authority | 22b05e2 | done | FS-SENSOR receipts |
| C04–C08 world/render/residency (first pass) | 627fefa, cf24a3d | superseded by C09R | live-20260914T181934Z (3 fps) |
| C09 depth prior measurements | 118e367 | done | FS-MEAS receipts |
| base fix pass | 075e371 | done | live-20260914T190219Z, live-20260914T191458Z |
| C09R forensic world-core closure | 06f8c12..F (series head = `ACCEPTANCE_SHA`) | source gate passed (gen_abi --check, relic grep, build_native.sh 31 kernels/17 sources, host tests 144159 checks) | device runs pending (`DEVICE_ACCEPTANCE.md`) |
