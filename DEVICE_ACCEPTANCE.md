# DEVICE ACCEPTANCE

Device: Quest 3S `340YC20G7X0QZ4` (Adreno 740, driver 512.837.9). Runs live under
`/mnt/kingston-unity/Builds/FinalScan/evidence/`.

| Run | SHA | Scope | Verdict |
|---|---|---|---|
| live-20260914T181934Z | pre-075e371 | first live build | FAIL: 3 fps, no receipts |
| live-20260914T190219Z | pre-075e371 + fusion | live scan | PARTIAL: 28–63 fps, cull 6–14 ms, reset broke rebind |
| live-20260914T191458Z | 075e371 | live scan | FAIL: HZB 400 ms/build, 3 fps, page-wide publish emitted zero surfels |
| accept1-20260914T223018Z | d31744b | synthetic dense foliage (kind 4) + spin 90 s | FAIL: 3 fps (`render.hzb.scatter` 370–420 ms/build = defect R); fusion itself healthy: 51 epochs, 16 616 new surfels, 1 852 matched segments, 517 merges, 46 ghosts, 84 roots published, 0 index/candidate overflow, pools 0.523 GiB, fuse job avg 2.7 ms (max 30.6 ms), sort share 11.9 % of the fuse job (review gap 3: global sort stays), quantum gate halved epochMeasCap 65536 → 4096 (10 halvings, 52 slices), 0 crashes. Full table: `python3 Tools~/probe/eval_run.py <dir> --spin`. |
| C09R acceptance (after C09R-G) | de43982 | synthetic dense/spin + live 60 s + RESET | pending |
