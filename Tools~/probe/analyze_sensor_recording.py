#!/usr/bin/env python3
"""FinalScan C03 sensor recording analyzer (contract §4.7).

Summarises a recording written by SensorRecorder (Runtime/Platform/Sensor/SensorRecorder.cs):
<dir>/index.jsonl plus frames/*.rgba and depth/*.r32. Every section degrades to "no data" when its
events are missing, so partial recordings still produce a report.

Usage: analyze_sensor_recording.py <recording_dir_or_index.jsonl> [-o OUT.md] [--json OUT.json]
"""
import argparse
import json
import math
import os
import sys
from collections import Counter, defaultdict

NS_MS = 1e-6


# --------------------------------------------------------------------------- helpers
def load(path):
    index = path if os.path.isfile(path) else os.path.join(path, 'index.jsonl')
    events = []
    with open(index, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                events.append(json.loads(line))
            except json.JSONDecodeError:
                pass
    return os.path.dirname(os.path.abspath(index)), events


def pct(values, p):
    if not values:
        return float('nan')
    s = sorted(values)
    k = max(0, min(len(s) - 1, int(math.ceil(p / 100.0 * len(s))) - 1))
    return s[k]


def stats(values, unit=''):
    if not values:
        return 'no data'
    return 'n=%d p50=%.1f p90=%.1f p99=%.1f min=%.1f max=%.1f%s' % (
        len(values), pct(values, 50), pct(values, 90), pct(values, 99), min(values), max(values), unit)


def hist(values, width, top=6):
    c = Counter(int(math.floor(v / width)) * width for v in values)
    return ', '.join('%g-%gms:%d' % (k, k + width, n) for k, n in sorted(c.items(), key=lambda kv: -kv[1])[:top])


def fps(times_ns):
    if len(times_ns) < 2:
        return 0.0
    span = (max(times_ns) - min(times_ns)) * 1e-9
    return (len(times_ns) - 1) / span if span > 0 else 0.0


# --------------------------------------------------------------------------- sections
def section_meta(events, out):
    meta = next((e for e in events if e.get('e') == 'meta'), None)
    out.append('## Recording')
    if not meta:
        out.append('no meta event')
    else:
        out.append('| key | value |\n|---|---|')
        for k, v in meta.items():
            if k != 'e':
                out.append('| %s | %s |' % (k, v))
    profiles = [e for e in events if e.get('e') == 'profile']
    if profiles:
        out.append('\n**Profiles**: ' + ' → '.join('%s (%s)' % (p.get('p'), p.get('reason')) for p in profiles))
    intr = [e for e in events if e.get('e') == 'intr']
    if intr:
        out.append('\n| eye | fx | fy | cx | cy | res | sensor | lens offset |\n|---|---|---|---|---|---|---|---|')
        for i in intr:
            lp = i.get('lp', [0, 0, 0])
            out.append('| %s | %.2f | %.2f | %.2f | %.2f | %dx%d | %dx%d | (%.4f, %.4f, %.4f) |' % (
                'LR'[i.get('eye', 0)], i.get('fx', 0), i.get('fy', 0), i.get('cx', 0), i.get('cy', 0),
                i.get('w', 0), i.get('h', 0), i.get('sw', 0), i.get('sh', 0), lp[0], lp[1], lp[2]))
        lens = {i.get('eye'): i.get('lp') for i in intr}
        if 0 in lens and 1 in lens:
            b = math.sqrt(sum((a - c) ** 2 for a, c in zip(lens[0], lens[1])))
            out.append('\nStereo baseline from lens offsets: **%.1f mm**' % (b * 1000))
    out.append('')


def section_frames(events, out, summary):
    frames = [e for e in events if e.get('e') == 'frame']
    out.append('## PCA frames per eye')
    if not frames:
        out.append('no frame events\n')
        return
    out.append('| eye | frames | fps (capture) | capture delta ms | latency ms (receive-capture) | uncertainty ms | gate paths | pose runtime/ring/none | images |')
    out.append('|---|---|---|---|---|---|---|---|---|')
    for eye in (0, 1):
        fe = [f for f in frames if f.get('eye') == eye]
        if not fe:
            continue
        t = [f['t'] for f in fe if 't' in f]
        deltas = [(b - a) * NS_MS for a, b in zip(t, t[1:])]
        lat = [(f['on'] * 1e9 - f['t']) * NS_MS for f in fe if 'on' in f and 't' in f and f['on'] > 0]
        unc = [f.get('u', 0) * NS_MS for f in fe]
        gates = Counter(f.get('g') for f in fe)
        rt = sum(1 for f in fe if f.get('rt'))
        pv = sum(1 for f in fe if f.get('pv'))
        imgs = sum(1 for f in fe if f.get('img'))
        out.append('| %s | %d | %.1f | %s | %s | %s | %s | %d/%d/%d | %d |' % (
            'LR'[eye], len(fe), fps(t), stats(deltas), stats(lat), stats(unc), dict(gates), rt, pv - rt, len(fe) - pv, imgs))
        summary.append('%s=%.1ffps' % ('LR'[eye], fps(t)))
        nonmono = sum(1 for d in deltas if d <= 0)
        if nonmono:
            out.append('\nWARNING eye %s: %d non-increasing capture timestamps in the committed stream (ring should have dropped them)' % ('LR'[eye], nonmono))
    out.append('')


def section_pairs(events, out, summary):
    obs = [e for e in events if e.get('e') == 'obs']
    out.append('## Stereo observations')
    if not obs:
        out.append('no obs events\n')
        return
    skew = [o.get('skewNs', 0) * NS_MS for o in obs]
    cls = Counter(o.get('cls') for o in obs)
    motion = Counter(o.get('motion') for o in obs)
    geom = sum(1 for o in obs if o.get('geom'))
    ang = [o.get('ang', 0) for o in obs]
    tl = [o['tl'] for o in obs if 'tl' in o]
    base = [o.get('baseline', 0) * 1000 for o in obs]
    out.append('| observations | pairs/s | skew ms (R-L) | |skew| histogram | classes | motion | geometry evidence | head deg/s | baseline mm |')
    out.append('|---|---|---|---|---|---|---|---|---|')
    out.append('| %d | %.1f | %s | %s | %s | %s | %d (%.0f%%) | %s | %s |' % (
        len(obs), fps(tl), stats(skew), hist([abs(s) for s in skew], 2.0), dict(cls), dict(motion), geom,
        100.0 * geom / len(obs), stats(ang), stats(base)))
    frames = [e for e in events if e.get('e') == 'frame']
    if frames:
        paired = set()
        for o in obs:
            paired.add(o.get('l'))
            paired.add(o.get('r'))
        per_eye = Counter(f.get('eye') for f in frames)
        used = Counter(f.get('eye') for f in frames if f.get('id') in paired)
        out.append('\nPairing acceptance: L %d/%d (%.0f%%), R %d/%d (%.0f%%)' % (
            used[0], per_eye[0], 100.0 * used[0] / max(1, per_eye[0]), used[1], per_eye[1], 100.0 * used[1] / max(1, per_eye[1])))
    summary.append('pairs=%.1f/s' % fps(tl))
    out.append('')


def section_poses(events, out):
    poses = [e for e in events if e.get('e') == 'pose']
    out.append('## Pose ring samples')
    if not poses:
        out.append('no pose events\n')
        return
    t = [p['t'] for p in poses if 't' in p]
    deltas = [(b - a) * NS_MS for a, b in zip(t, t[1:])]
    out.append('samples=%d rate=%.1f Hz delta ms: %s' % (len(poses), fps(t), stats(deltas)))
    nonmono = sum(1 for d in deltas if d <= 0)
    if nonmono:
        out.append('WARNING: %d non-increasing pose sample times' % nonmono)
    out.append('')


def section_clock(events, out, summary):
    clocks = [e for e in events if e.get('e') == 'clock']
    out.append('## Clock references (OVR seconds vs CLOCK_MONOTONIC vs UTC)')
    if not clocks:
        out.append('no clock events\n')
        return
    offs = [(0.5 * (c['ob'] + c['oa']) * 1e9 - c['m']) * NS_MS for c in clocks if c.get('m', 0) > 0 and c.get('ob', 0) > 0]
    brackets = [(c['oa'] - c['ob']) * 1e3 for c in clocks if c.get('ob', 0) > 0]
    out.append('samples=%d OVR-monotonic offset ms: %s bracket ms: %s' % (len(clocks), stats(offs), stats(brackets)))
    if offs:
        med = pct(offs, 50)
        jitter = math.sqrt(sum((o - med) ** 2 for o in offs) / len(offs))
        verdict = 'A (XrTime == CLOCK_MONOTONIC)' if abs(med) < 2.0 and jitter < 2.0 else 'C (mapping not exact)'
        out.append('median offset %.3f ms, jitter rms %.3f ms → clock gate candidate: **%s**' % (med, jitter, verdict))
        summary.append('gate=%s' % verdict[0])
    gates = Counter(e.get('g') for e in events if e.get('e') == 'frame')
    if gates:
        out.append('gate paths used by frames: %s' % dict(gates))
    out.append('')


def section_depth(events, out, summary):
    depth = [e for e in events if e.get('e') == 'depth']
    out.append('## Environment Depth')
    if not depth:
        out.append('no depth events\n')
        return
    t = [d['t'] for d in depth if 't' in d]
    deltas = [(b - a) * NS_MS for a, b in zip(t, t[1:])]
    age = [d.get('age', 0) * NS_MS for d in depth]
    d0 = depth[0]
    out.append('| frames | fps | XrTime delta ms | age ms | size | near/far | poses/fovs | raw files |\n|---|---|---|---|---|---|---|---|')
    out.append('| %d | %.1f | %s | %s | %dx%d | %.2f/%s | %d/%d | %d |' % (
        len(depth), fps(t), stats(deltas), stats(age), d0.get('w', 0), d0.get('h', 0), d0.get('near', 0), d0.get('far'),
        sum(1 for d in depth if d.get('pv')), sum(1 for d in depth if d.get('fv')), sum(1 for d in depth if d.get('raw'))))
    f0 = d0.get('fov0')
    if f0:
        out.append('\nfov0 tangents (l, r, u, d): %s → degrees %s' % (f0, [round(math.degrees(math.atan(x)), 1) for x in f0]))
    summary.append('depth=%.1fHz age=%.0fms' % (fps(t), pct(age, 50) if age else float('nan')))
    out.append('')


def section_telemetry(events, out):
    tel = [e.get('json') for e in events if e.get('e') == 'telemetry' and isinstance(e.get('json'), dict)]
    out.append('## Telemetry (FS-SENSOR)')
    if not tel:
        out.append('no telemetry events\n')
        return
    last = tel[-1]
    keys = ['profile', 'fpsL', 'fpsR', 'pairsPerSec', 'pairs', 'rejectedSkew', 'rejectedUncertainty', 'expiredUnpaired', 'timestampNonMonotonic',
            'staleDropped', 'poolExhausted', 'clockGatePath', 'clockUncertaintyNs', 'motionGated', 'ticks', 'ticksCoalesced', 'poseResidualMm', 'poseResidualDeg', 'monoClock', 'trackingSpace']
    out.append('| key | last | min | max |\n|---|---|---|---|')
    for k in keys:
        vals = [t.get(k) for t in tel if k in t]
        nums = [v for v in vals if isinstance(v, (int, float))]
        if nums:
            out.append('| %s | %s | %g | %g |' % (k, last.get(k), min(nums), max(nums)))
        elif vals:
            out.append('| %s | %s | | |' % (k, last.get(k)))
    depth = [t.get('depth') for t in tel if isinstance(t.get('depth'), dict)]
    if depth:
        out.append('\nDepth telemetry (last): %s' % json.dumps(depth[-1]))
    rec = [t.get('recorder') for t in tel if isinstance(t.get('recorder'), dict)]
    if rec:
        out.append('\nRecorder (last): %s' % json.dumps(rec[-1]))
    out.append('')


def section_files(root, events, out):
    frames_dir = os.path.join(root, 'frames')
    depth_dir = os.path.join(root, 'depth')
    nf = len(os.listdir(frames_dir)) if os.path.isdir(frames_dir) else 0
    nd = len(os.listdir(depth_dir)) if os.path.isdir(depth_dir) else 0
    ref_f = sum(1 for e in events if e.get('e') == 'frame' and e.get('img'))
    ref_d = sum(1 for e in events if e.get('e') == 'depth' and e.get('raw'))
    out.append('## Files\n\nframes/: %d files (%d referenced)  depth/: %d files (%d referenced)' % (nf, ref_f, nd, ref_d))
    missing = [e['img'] for e in events if e.get('e') == 'frame' and e.get('img') and not os.path.exists(os.path.join(root, e['img']))]
    if missing:
        out.append('WARNING: %d referenced frame images missing (readback dropped/failed before write): %s ...' % (len(missing), missing[:3]))
    out.append('')


# --------------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('path')
    ap.add_argument('-o', '--out')
    ap.add_argument('--json', help='write the key numbers as JSON')
    args = ap.parse_args()
    root, events = load(args.path)
    out = ['# SENSOR_RECORDING (generated by Tools~/probe/analyze_sensor_recording.py)', '',
           'Source: %s  events=%d  kinds=%s' % (root, len(events), dict(Counter(e.get('e') for e in events))), '']
    summary = []
    section_meta(events, out)
    section_frames(events, out, summary)
    section_pairs(events, out, summary)
    section_poses(events, out)
    section_clock(events, out, summary)
    section_depth(events, out, summary)
    section_telemetry(events, out)
    section_files(root, events, out)
    out.insert(3, 'Summary: `%s`' % ' | '.join(summary))
    text = '\n'.join(out)
    if args.out:
        with open(args.out, 'w', encoding='utf-8') as f:
            f.write(text + '\n')
        print('wrote', args.out)
    else:
        print(text)
    if args.json:
        with open(args.json, 'w', encoding='utf-8') as f:
            json.dump({'events': len(events), 'summary': summary}, f, indent=1)
    return 0


if __name__ == '__main__':
    sys.exit(main())
