#!/usr/bin/env python3
"""Evaluates one run_live.sh evidence directory against the C09R acceptance matrix (docs/C09R_FORENSIC_CLOSURE.md §3).

Receipts read (device data only, never estimates): VrApi lines (FPS, App ms, GPU%) from logcat.txt, FS-RENDER /
FS-WORLD / FS-ACCEPT lines from fs.txt, the FS-HOST-CS jsonl (files/host/host-*.jsonl pulled into host/), dumpsys
meminfo snapshots, errors.txt. Prints a markdown table and a PASS/FAIL verdict per row; exit 1 on any FAIL.
Usage: eval_run.py <evidence-dir> [--live] [--reset] [--spin]
"""
import glob, json, os, re, sys

def pct(v, p):
    if not v: return 0.0
    s = sorted(v); k = (len(s) - 1) * p / 100.0; f = int(k); c = min(f + 1, len(s) - 1)
    return s[f] + (s[c] - s[f]) * (k - f)

def main():
    if len(sys.argv) < 2: print(__doc__); return 2
    d = sys.argv[1]; live = "--live" in sys.argv; want_reset = "--reset" in sys.argv; want_spin = "--spin" in sys.argv
    rows = []
    def row(name, value, ok, note=""): rows.append((name, value, "PASS" if ok else "FAIL", note))
    sha = open(os.path.join(d, "ACCEPTANCE_SHA")).read().strip() if os.path.exists(os.path.join(d, "ACCEPTANCE_SHA")) else "?"
    # ---- VrApi (skip the first 15 s: launch, warm-up)
    fps, app, gpu = [], [], []
    t0 = None
    for line in open(os.path.join(d, "logcat.txt"), errors="replace"):
        if "VrApi" not in line or "FPS=" not in line: continue
        m = re.search(r"FPS=(\d+)/(\d+).*?App=([\d.]+)ms.*?GPU%=([\d.]+)", line)
        if not m: continue
        ts = line[:18]
        if t0 is None: t0 = ts; n = 0
        n += 1
        if n <= 15: continue
        fps.append(int(m.group(1))); app.append(float(m.group(3))); gpu.append(float(m.group(4)))
    row("VrApi samples (after 15 s)", len(fps), len(fps) >= 20)
    if fps:
        row("FPS p50 / p5 / min", f"{pct(fps,50):.0f} / {pct(fps,5):.0f} / {min(fps)}", pct(fps, 50) >= 72 and pct(fps, 5) >= 60, "p50 >= 72, p5 >= 60")
        row("App ms p50 / p95 / p99", f"{pct(app,50):.2f} / {pct(app,95):.2f} / {pct(app,99):.2f}", pct(app, 95) <= 13.9, "p95 <= 13.9 ms (72 Hz)")
        row("GPU% p50 / p95", f"{pct(gpu,50):.2f} / {pct(gpu,95):.2f}", pct(gpu, 95) < 0.95, "p95 < 0.95")
    # ---- FS lines
    fs = open(os.path.join(d, "fs.txt"), errors="replace").read().splitlines() if os.path.exists(os.path.join(d, "fs.txt")) else []
    cull, hzb, records, uncertain = [], [], [], []
    for l in fs:
        m = re.search(r"FS-RENDER receipt: cull (\d+) us .*?hzb (\d+) us .*?uncertain=(\d+) .*?records=(\d+)", l)
        if m: cull.append(int(m.group(1))); hzb.append(int(m.group(2))); uncertain.append(int(m.group(3))); records.append(int(m.group(4)))
    row("FS-RENDER receipts", len(cull), len(cull) >= 1)
    if cull:
        row("cull us last / max", f"{cull[-1]} / {max(cull)}", max(cull) <= 4000, "<= 4 ms")
        row("hzb us last / max", f"{hzb[-1]} / {max(hzb)}", max(hzb) <= 4000, "<= 4 ms (075e371: 400 ms)")
        row("draw records last / max", f"{records[-1]} / {max(records)}", max(records) > 0, "> 0 (075e371 published zero surfels)")
    quantum = [l for l in fs if "FS-WORLD quantum" in l]
    row("quantum gate events", len(quantum), True, "informational")
    resets = [l for l in fs if "FS-WORLD reset complete" in l]
    if want_reset: row("RESET WORLD completed", len(resets), len(resets) >= 1)
    spin = [l for l in fs if "FS-ACCEPT spin" in l]
    if want_spin: row("FS-ACCEPT spin verdict", spin[-1][spin[-1].index("FS-ACCEPT"):][:160] if spin else "none", bool(spin) and "PASS" in spin[-1])
    # ---- host jsonl
    recs = []
    for f in sorted(glob.glob(os.path.join(d, "host", "host-*.jsonl"))):
        for l in open(f, errors="replace"):
            try: recs.append(json.loads(l))
            except Exception: pass
    tele = [r for r in recs if "world" in r and isinstance(r.get("world"), dict) and "epochs" in r["world"]]
    row("FS-HOST-CS records", len(tele), len(tele) >= 3)
    if tele:
        last = tele[-1]; w = last["world"]; fu = w.get("fusion", {}); mem = w.get("memory", {}); rn = last.get("render", {}) if isinstance(last.get("render"), dict) else {}
        row("fusion epochs", w.get("epochs"), (w.get("epochs") or 0) >= 1)
        row("new surfels / matched segments / contributions", f"{fu.get('newSurfels')} / {fu.get('segmentsMatched')} / {fu.get('contributions')}", (fu.get("newSurfels") or 0) > 0)
        row("merges / splits / ghosts", f"{fu.get('merges')} / {fu.get('splits')} / {fu.get('ghosts')}", True, "informational")
        row("index overflow / candidate overflow / hop overflow", f"{fu.get('indexOverflow')} / {fu.get('candidateOverflow')} / {fu.get('freeHopOverflow')}", True, "counted, never silent")
        row("roots published / retire backlog", f"{w.get('rootsPublished')} / {w.get('retirementBacklog')}", (w.get("rootsPublished") or 0) > 0)
        row("epochMeasCap / epochDirtyCap / slices / halvings", f"{w.get('epochMeasCap')} / {w.get('epochDirtyCap')} / {w.get('slices')} / {w.get('capHalvings')}", True, "review gap 1")
        gib = (mem.get("bytesCommitted") or 0) / 2**30
        row("world pools committed GiB", f"{gib:.3f}", gib < 1.0, "far below 1.09 GiB")
        alloc_fail = sum((mem.get(k, {}) or {}).get("allocFailures", 0) for k in ("indexLeaves", "indexNodes", "renderBlocks", "renderNodes"))
        row("pool alloc failures", alloc_fail, True, "counted")
        cut = rn.get("cut", {}); st = rn.get("stages", {})
        if cut: row("cut: frontier nodes / blocks / aggregates / uncertain / overflow", f"{cut.get('frontierNodes')} / {cut.get('blocks')} / {cut.get('aggregates')} / {cut.get('hzbUncertain')} / {cut.get('frontierOverflow')}", True, "informational")
        if st: row("render stage max us (pages/expand/blocks/compact/scatter/combine/mips)", " / ".join(str(st.get(k, {}).get("maxUs")) for k in ("cull.pages", "cull.expand", "cull.blocks", "cull.compact", "hzb.scatter", "hzb.combine", "hzb.mips")), True)
        # C09R-E4.1C / E4.2R / E5 / C10 / C11 receipts (informational rows)
        row("sheet edges / nodes / crossPage / contractions / refineBirths", f"{fu.get('sheetEdges')} / {fu.get('sheetNodes')} / {fu.get('crossPageEdges')} / {fu.get('edgeContractions')} / {fu.get('refinementBirths')}", True, "E4.1C")
        row("coverage overlap / hole mm2 (sums)", f"{fu.get('coverageOverlapMm2')} / {fu.get('coverageHoleMm2')}", True, "E4.2R cells")
        # C09R-E5R acceptance rows
        fr = last.get("frontSurfels") or 0; pl = w.get("promotedLive") or 0
        ratio = (fr / pl) if pl > 0 else 0.0
        row("FRONT surfels / promoted live in BACK / ratio", f"{fr} / {pl} / {ratio:.2f}", ratio > 0.8, "E5R: > ~0.8 after settling")
        row("publication age p50 / p95 ms (samples)", f"{w.get('publicationAgeP50Ms')} / {w.get('publicationAgeP95Ms')} ({w.get('publicationAgeSamples')})", (w.get('publicationAgeP95Ms') or 1e9) < 100, "E5R: p95 < 100 ms")
        row("sheet age p50 / p95 ms (samples)", f"{w.get('sheetAgeP50Ms')} / {w.get('sheetAgeP95Ms')} ({w.get('sheetAgeSamples')})", (w.get('sheetAgeP95Ms') or 1e9) < 250, "E5R: p95 < 250 ms")
        row("scheduler jobs fuse / publish / sheet; overdue publish / sheet", f"{w.get('schedFuseJobs')} / {w.get('schedPublishJobs')} / {w.get('schedSheetJobs')}; {w.get('schedOverduePublish')} / {w.get('schedOverdueSheet')}", (w.get('sheetJobs') or 0) > 100, "E5R: sheet jobs regular")
        row("dirty ring pending / drops; sheet ring pending", f"{w.get('dirtyRingPending')} / {fu.get('dirtyRingDrop')}; {w.get('sheetRingPending')}", (fu.get('dirtyRingDrop') or 0) == 0, "E5R")
        row("cost fuse fixed us / per item us / structural", f"{w.get('costFuseFixedUs')} / {(w.get('costFusePerItemUs1000') or 0)/1000:.3f} / {w.get('costFuseStructural')}", True, "E5R")
        row("cost maint / leaves / sheet fixed us (structural)", f"{w.get('costMaintFixedUs')} ({w.get('costMaintStructural')}) / {w.get('costLeavesFixedUs')} ({w.get('costLeavesStructural')}) / {w.get('costSheetFixedUs')} ({w.get('costSheetStructural')})", True, "E5R")
        meas_total, meas_drop = last.get("measurements") or 0, last.get("measurementsDropped") or 0
        row("measurements dropped share", f"{(meas_drop / meas_total * 100) if meas_total else 0:.1f} %", meas_total == 0 or meas_drop / meas_total < 0.5, "E5R: not tens of percent (latest-only drops OK)")
        row("publish chunks / leavesChunk / sheet jobs / sheetBatch", f"{w.get('publishChunks')} / {w.get('leavesChunk')} / {w.get('sheetJobs')} / {w.get('sheetBatch')}", True, "E5")
        row("last us: fuse / maint / leaves / levels / sheet", f"{w.get('fuseGpuUsLast')} / {w.get('publishMaintUsLast')} / {w.get('publishLeavesUsLast')} / {w.get('publishLevelsUsLast')} / {w.get('sheetGpuUsLast')}", True, "E5")
        row("frames abandoned / measurements abandoned", f"{w.get('framesAbandoned')} / {w.get('measurementsAbandoned')}", True, "E5")
        row("render recuts / reused cuts", f"{rn.get('recuts')} / {rn.get('reusedCuts')}", True, "E5")
        so = last.get("stereo") if isinstance(last.get("stereo"), dict) else {}
        if so:
            bn, br = so.get("binN", [0]*4), so.get("binResidual0p1mm", [0]*4)
            res = " ".join(f"{(br[i]*0.1/bn[i]) if bn[i] else 0:.1f}mm(n{bn[i]})" for i in range(4))
            v = so.get("valid") or 0
            row("stereo tested / valid / lowTex / ambiguous / bandEdge / noCover", f"{so.get('tested')} / {v} / {so.get('lowTex')} / {so.get('ambiguous')} / {so.get('bandEdge')} / {so.get('noCover')}", True, "C10")
            row("stereo |z_s - z_env| by bin <0.75/<1.5/<2.5/>=2.5 m", res, True, "C10")
            row("stereo sigma mean vs env sigma mean (mm)", f"{(so.get('sigmaUm',0)/1000/v) if v else 0:.2f} vs {(so.get('envSigmaUm',0)/1000/v) if v else 0:.2f}", True, "C10")
        mv = last.get("multiview") if isinstance(last.get("multiview"), dict) else {}
        if mv:
            tv, pv = mv.get("temporalValid") or 0, mv.get("planarValid") or 0
            row("temporal tested / valid / lowTex / ambiguous / bandEdge / noCover / disagree", f"{mv.get('temporalTested')} / {tv} / {mv.get('temporalLowTex')} / {mv.get('temporalAmbiguous')} / {mv.get('temporalBandEdge')} / {mv.get('temporalNoCover')} / {mv.get('temporalDisagree')}", True, "C11")
            row("temporal sigma mean mm / keyframes set / frames with keyframe", f"{(mv.get('temporalSigmaUm',0)/1000/tv) if tv else 0:.2f} / {mv.get('keyframesSet')} / {mv.get('framesWithKeyframe')}", True, "C11")
            row("candidates stereo / temporal; refine skipped / jobs; pairs rejected for geometry", f"{mv.get('candidatesStereo')} / {mv.get('candidatesTemporal')}; {mv.get('refineSkipped')} / {mv.get('refineJobs')}; {mv.get('pairsGeometryRejected')}", True, "C10R/C11R")
            row("refine ms per frame p95 / last compaction us", f"{(mv.get('refineFrameUsP95') or 0)/1000:.2f} / {mv.get('lastCompactUs')}", True, "C10R: compaction back to ~1 ms")
            row("planar tested / valid / rejected / sigma mm / rms mm", f"{mv.get('planarTested')} / {pv} / {mv.get('planarRejected')} / {(mv.get('planarSigmaUm',0)/1000/pv) if pv else 0:.2f} / {(mv.get('planarRmsUm',0)/1000/pv) if pv else 0:.2f}", True, "C11")
        # sort share (review gap 3): native stage ring of the last records
        sort_us, fuse_us = 0.0, 0.0
        for r in tele[-5:]:
            for s in (r.get("native", {}) or {}).get("stages", []) or []:
                nm = s.get("stage") or ""; us = s.get("gpuUs") or 0
                if nm.startswith("sort_"): sort_us += us
                elif nm == "world.fuse": fuse_us += us
        if fuse_us > 0: row("sort share of fuse job (last 5 records)", f"{100.0 * sort_us / fuse_us:.1f} %", 100.0 * sort_us / fuse_us <= 25.0, "review gap 3: <= 25 %")
        else: row("sort share of fuse job", "no fuse stages in the ring", False, "review gap 3 receipt missing")
        if live:
            mps = [r.get("measurementsPerSec", 0) for r in tele]
            row("measurements/s p50 / max", f"{pct(mps,50):.0f} / {max(mps):.0f}", max(mps) > 0)
        if want_reset:
            ev = [r for r in recs if r.get("event") == "reset"]
            row("reset event rc", ev[-1].get("rc") if ev else "none", bool(ev) and ev[-1].get("rc") == 0)
            after = [r for r in tele if r.get("t", 0) > (max((r.get("t", 0) for r in tele if r["world"].get("resets", 0) == 0), default=0))]
            row("epochs after reset", (after[-1]["world"].get("epochs") if after else None), bool(after) and (after[-1]["world"].get("epochs") or 0) > 0, "scanning resumes")
    # ---- memory / errors
    pss = []
    for f in glob.glob(os.path.join(d, "meminfo", "m-*.txt")):
        m = re.search(r"TOTAL PSS:\s+(\d+)", open(f).read())
        if m: pss.append(int(m.group(1)) / 1024.0)
    if pss: row("TOTAL PSS MiB max", f"{max(pss):.0f}", max(pss) < 2600)
    errs = open(os.path.join(d, "errors.txt"), errors="replace").read() if os.path.exists(os.path.join(d, "errors.txt")) else ""
    fatal = len(re.findall(r"FATAL|SIGSEGV|SIGABRT|DEVICE_LOST", errs))
    row("FATAL / SIGSEGV / DEVICE_LOST lines", fatal, fatal == 0)
    gone = any("app process gone" in l for l in open(os.path.join(d, "deploy.txt"), errors="replace")) if os.path.exists(os.path.join(d, "deploy.txt")) else False
    row("process survived the run", not gone, not gone)
    print(f"# {os.path.basename(d)}  (ACCEPTANCE_SHA {sha})\n")
    print("| Check | Value | Verdict | Rule |\n|---|---|---|---|")
    for n, v, ok, note in rows: print(f"| {n} | {v} | {ok} | {note} |")
    fails = [r for r in rows if r[2] == "FAIL"]
    print(f"\nVERDICT: {'PASS' if not fails else 'FAIL (' + str(len(fails)) + ')'}")
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
