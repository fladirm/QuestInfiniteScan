# DEVICE ACCEPTANCE

Device: Quest 3S `340YC20G7X0QZ4` (Adreno 740, driver 512.837.9). Runs live under
`/mnt/kingston-unity/Builds/FinalScan/evidence/`.

| Run | SHA | Scope | Verdict |
|---|---|---|---|
| live-20260914T181934Z | pre-075e371 | first live build | FAIL: 3 fps, no receipts |
| live-20260914T190219Z | pre-075e371 + fusion | live scan | PARTIAL: 28–63 fps, cull 6–14 ms, reset broke rebind |
| live-20260914T191458Z | 075e371 | live scan | FAIL: HZB 400 ms/build, 3 fps, page-wide publish emitted zero surfels |
| C09R acceptance | pending | synthetic dense/spin + live 60 s + RESET | pending |
