# DEVICE ACCEPTANCE

Device: Quest 3S `340YC20G7X0QZ4` (Adreno 740, driver 512.837.9). Runs live under
`/mnt/kingston-unity/Builds/FinalScan/evidence/`.

| Run | SHA | Scope | Verdict |
|---|---|---|---|
| live-20260914T181934Z | pre-075e371 | first live build | FAIL: 3 fps, no receipts |
| live-20260914T190219Z | pre-075e371 + fusion | live scan | PARTIAL: 28–63 fps, cull 6–14 ms, reset broke rebind |
| live-20260914T191458Z | 075e371 | live scan | FAIL: HZB 400 ms/build, 3 fps, page-wide publish emitted zero surfels |
| accept1-20260914T223018Z | d31744b | synthetic dense foliage (kind 4) + spin 90 s | FAIL: 3 fps (`render.hzb.scatter` 370–420 ms/build = defect R); fusion itself healthy: 51 epochs, 16 616 new surfels, 1 852 matched segments, 517 merges, 46 ghosts, 84 roots published, 0 index/candidate overflow, pools 0.523 GiB, fuse job avg 2.7 ms (max 30.6 ms), sort share 11.9 % of the fuse job (review gap 3: global sort stays), quantum gate halved epochMeasCap 65536 → 4096 (10 halvings, 52 slices), 0 crashes. Full table: `python3 Tools~/probe/eval_run.py <dir> --spin`. |
| live-20260914T224021Z | de43982 | live | 73 fps / App 2.8 ms (defect R gone); depth wrong: every texel 0.13-0.24 m (Graphics.Blit of the external depth attachment) → fixed 90ccce6 (donor compute copy) |
| live-20260914T225146Z | 90ccce6 | live | depth correct (0.13-6 m); FREEZE after ~9 s: ABBA deadlock executor mutex ↔ World::m_ (all 101 threads sleeping) → fixed 5e339b9; ARF depth poses were transformed through trackingSpace twice → fixed 5e339b9 |
| live-20260914T232204Z | 5e339b9 | live 71 s | no freeze, world locked to the room, but back-projection mirrored vertically (bbox y up to 5.55 m with eye 1.87 m, 15 % matched associations, canonical 31k → 227k linear) → fixed f0468c7 (FLIP_Y default) |
| live-20260914T234452Z | f0468c7 | live 142 s, user walk-through (stand / side-step / turn away & back) | **user: "teď to sedí"**; matched associations 27 % → 49 % cumulative (per-window 58-72 % on revisits), canonical 71k → 171k then flat (170 850 → 171 023 over the last 20 s = convergence), merges 28k, ghosts 134k, draw 36-104k records, 0 crashes. Remaining: 22-30 fps (App 26-49 ms) from the 65 536 measurements per depth frame (0.6-1.1 M/s) → next cut C09R-E2 bounded information-driven compaction. Bundle: `~/Stažené/finalscan-live-20260914T234452Z.zip` |
