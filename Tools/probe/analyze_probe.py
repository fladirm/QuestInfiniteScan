#!/usr/bin/env python3
"""FinalScan C01 probe analyzer.

Parses the evidence directory produced by Tools/probe/run_probe.sh (probe jsonl + logcat + meminfo samples)
and writes a DEVICE_ENVELOPE.md draft. Every section is independent and degrades to "no data" when its
inputs are missing, so partial runs still produce a report.

Usage: analyze_probe.py <evidence_dir_or_jsonl> [-o OUT.md] [--logcat logcat.txt] [--meminfo-dir DIR]
"""
import argparse
import glob
import json
import os
import re
import statistics
import sys
from collections import OrderedDict, defaultdict

PROBE_RE = re.compile(r'FS-PROBE (\{.*\})\s*$')
CHUNK_RE = re.compile(r'FS-PROBE-CHUNK (\d+) (\d+)/(\d+) (.*)$')
NATIVE_RE = re.compile(r'FS-NATIVE[:\s](.*)$')
VRAPI_FPS_RE = re.compile(r'FPS=(\d+)/(\d+)')
VRAPI_FIELD_RE = re.compile(r'(\w+%?)=([^,]+)')
LOGCAT_TS_RE = re.compile(r'^(\d\d-\d\d \d\d:\d\d:\d\d\.\d+)')


# --------------------------------------------------------------------------- loading
def load_jsonl(path):
    events = []
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                events.append(json.loads(line))
            except json.JSONDecodeError:
                pass
    return events


def load_logcat(path):
    """Returns (probe_events_from_logcat, native_lines, vrapi_samples, raw_lines)."""
    events, native, vrapi = [], [], []
    chunks = defaultdict(dict)
    chunk_total = {}
    if not path or not os.path.exists(path):
        return events, native, vrapi
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.rstrip('\n')
            m = PROBE_RE.search(line)
            if m:
                try:
                    events.append(json.loads(m.group(1)))
                except json.JSONDecodeError:
                    pass
                continue
            m = CHUNK_RE.search(line)
            if m:
                seq, i, n, part = int(m.group(1)), int(m.group(2)), int(m.group(3)), m.group(4)
                chunks[seq][i] = part
                chunk_total[seq] = n
                if len(chunks[seq]) == n:
                    doc = ''.join(chunks[seq][k] for k in sorted(chunks[seq]))
                    try:
                        events.append(json.loads(doc))
                    except json.JSONDecodeError:
                        pass
                    del chunks[seq]
                continue
            m = NATIVE_RE.search(line)
            if m:
                native.append(m.group(1).strip())
                continue
            if 'FPS=' in line and ('VrApi' in line or 'OVR' in line or 'VrRuntime' in line):
                fm = VRAPI_FPS_RE.search(line)
                if fm:
                    fields = dict(VRAPI_FIELD_RE.findall(line))
                    tm = LOGCAT_TS_RE.match(line)
                    vrapi.append({'ts': tm.group(1) if tm else '', 'fps': int(fm.group(1)), 'target': int(fm.group(2)), 'fields': fields})
    return events, native, vrapi


MEMINFO_KEYS = OrderedDict([
    ('TOTAL PSS', re.compile(r'TOTAL PSS:\s*(\d+)')),
    ('TOTAL RSS', re.compile(r'TOTAL RSS:\s*(\d+)')),
    ('Graphics', re.compile(r'^\s*Graphics:\s*(\d+)', re.M)),
    ('GL mtrack', re.compile(r'GL mtrack\s+(\d+)')),
    ('EGL mtrack', re.compile(r'EGL mtrack\s+(\d+)')),
    ('Native Heap', re.compile(r'Native Heap\s+(\d+)')),
    ('Native Heap (summary)', re.compile(r'Native Heap:\s*(\d+)')),
    ('Java Heap', re.compile(r'Java Heap:\s*(\d+)')),
    ('Code', re.compile(r'^\s*Code:\s*(\d+)', re.M)),
])


def load_meminfo(dirpath):
    samples = []
    if not dirpath or not os.path.isdir(dirpath):
        return samples
    for p in sorted(glob.glob(os.path.join(dirpath, 'meminfo-*.txt'))):
        try:
            txt = open(p, 'r', encoding='utf-8', errors='replace').read()
        except OSError:
            continue
        s = {'file': os.path.basename(p)}
        hm = re.search(r'elapsed=(\d+)', txt)
        s['elapsed'] = int(hm.group(1)) if hm else None
        for k, rx in MEMINFO_KEYS.items():
            m = rx.search(txt)
            if m:
                s[k] = int(m.group(1))
        samples.append(s)
    return samples


# --------------------------------------------------------------------------- helpers
def fmt(v, nd=1):
    if v is None:
        return '-'
    if isinstance(v, bool):
        return 'yes' if v else 'no'
    if isinstance(v, (int,)):
        return str(v)
    if isinstance(v, float):
        if v != v:
            return 'nan'
        return f'{v:.{nd}f}'
    return str(v)


def st(d, key, nd=1):
    """Format a SampleStats object {n,min,max,mean,p50,p90,p99}."""
    s = d.get(key) if isinstance(d, dict) else None
    if not s or not isinstance(s, dict) or not s.get('n'):
        return '-'
    return f"n={s.get('n')} p50={fmt(s.get('p50'), nd)} p90={fmt(s.get('p90'), nd)} p99={fmt(s.get('p99'), nd)} min={fmt(s.get('min'), nd)} max={fmt(s.get('max'), nd)}"


def table(headers, rows):
    if not rows:
        return '_no data_\n'
    out = ['| ' + ' | '.join(headers) + ' |', '|' + '|'.join('---' for _ in headers) + '|']
    for r in rows:
        out.append('| ' + ' | '.join(str(c) for c in r) + ' |')
    return '\n'.join(out) + '\n'


def by_event(events, name):
    return [e for e in events if e.get('event') == name]


def first(events, name):
    lst = by_event(events, name)
    return lst[0] if lst else None


def last(events, name):
    lst = by_event(events, name)
    return lst[-1] if lst else None


def linear_fit(xs, ys):
    n = min(len(xs), len(ys))
    if n < 2:
        return None
    mx = sum(xs[:n]) / n
    my = sum(ys[:n]) / n
    sxx = sum((x - mx) ** 2 for x in xs[:n])
    if sxx <= 0:
        return None
    sxy = sum((xs[i] - mx) * (ys[i] - my) for i in range(n))
    slope = sxy / sxx
    offset = my - slope * mx
    res = [ys[i] - (slope * xs[i] + offset) for i in range(n)]
    rms = (sum(r * r for r in res) / n) ** 0.5
    return {'n': n, 'slope': slope, 'offset': offset, 'driftPpm': (slope - 1) * 1e6, 'rmsMs': rms * 1e3, 'maxMs': max(abs(r) for r in res) * 1e3}


def section(title):
    return f'\n## {title}\n\n'


# --------------------------------------------------------------------------- sections
def sec_boot(ev):
    out = section('Boot / device')
    b = first(ev, 'boot')
    if not b:
        return out + '_no data_\n'
    si = b.get('sysinfo', {}) or {}
    an = b.get('android') or {}
    rows = [(k, fmt(v)) for k, v in [
        ('deviceModel', si.get('deviceModel')), ('os', si.get('os')), ('cpu', si.get('cpu')), ('cpuCount', si.get('cpuCount')),
        ('systemMemoryMB', si.get('systemMemoryMB')), ('gpu', si.get('gpu')), ('gpuVersion', si.get('gpuVersion')), ('gpuType', si.get('gpuType')),
        ('supportsAsyncCompute', si.get('supportsAsyncCompute')), ('unity', (b.get('app') or {}).get('unity')), ('appVersion', (b.get('app') or {}).get('version')),
        ('android.release', an.get('release')), ('android.sdkInt', an.get('sdkInt')), ('android.fingerprint', an.get('fingerprint')),
        ('vrosSdkVersion', an.get('vrosSdkVersion')), ('pcaIsSupported', an.get('pcaIsSupported')),
    ]]
    out += table(['key', 'value'], rows)
    d = first(ev, 'display_refresh')
    if d:
        out += '\n**Display refresh**: requested %s, OVR before/after %s/%s (set attempted %s, err %s), OpenXR request %s (err %s), XRDisplaySubsystem before/after %s/%s, available %s\n' % (
            fmt(d.get('requested')), fmt(d.get('ovrBefore')), fmt(d.get('ovrAfter')), fmt(d.get('ovrSetAttempted')), d.get('ovrError'),
            fmt(d.get('openxrSet')), d.get('openxrError'), fmt(d.get('xrDisplayRateBefore')), fmt(d.get('xrDisplayRateAfter')), d.get('available'))
    p = first(ev, 'permission')
    if p:
        out += '\n**Permissions**: HEADSET_CAMERA=%s CAMERA=%s\n' % (fmt(p.get('headsetCamera')), fmt(p.get('androidCamera')))
    return out


def sec_pca(ev):
    out = section('PCA cadence per profile per eye')
    profiles = by_event(ev, 'pca_profile')
    skips = by_event(ev, 'pca_skip')
    sup = first(ev, 'pca_supported')
    if sup:
        out += 'Supported resolutions L: %s  R: %s\n\n' % (sup.get('left'), sup.get('right'))
    rows = []
    for p in profiles:
        req = p.get('requested', {})
        for eye in ('L', 'R'):
            e = (p.get('eyes') or {}).get(eye)
            if not e:
                continue
            rows.append((p.get('profile'), eye, f"{req.get('w')}x{req.get('h')}@{req.get('fps')}", f"{e.get('currentW')}x{e.get('currentH')}",
                         e.get('frames'), fmt(e.get('deliveredFps')), st(e, 'deltaMs'), e.get('nonMonotonic'), st(e, 'unityFramesBetween', 2)))
    for s in skips:
        rows.append((s.get('profile'), '-', '-', '-', '-', 'skipped', s.get('reason'), '-', '-'))
    out += table(['profile', 'eye', 'requested', 'current', 'frames', 'fps', 'ts delta ms', 'non-mono', 'unity frames between'], rows)

    out += '\n### Latency (receive - capture)\n\n'
    rows = []
    for p in profiles:
        for eye in ('L', 'R'):
            e = (p.get('eyes') or {}).get(eye)
            if not e:
                continue
            rows.append((p.get('profile'), eye, st(e, 'latencyUtcMs'), st(e, 'latencyMonoMs'), st(e, 'latencyOvrMs'), e.get('monoFieldMissing')))
    out += table(['profile', 'eye', 'UtcNow - Timestamp (ms)', 'CLOCK_MONOTONIC - monoTs (ms)', 'OVR now - monoTs (ms)', 'mono field missing'], rows)
    out += '\nTimestamp semantics: %s\n' % (profiles[0].get('timestampType') if profiles else '-')

    out += '\n### L/R pairing (nearest timestamp of the other eye)\n\n'
    rows = []
    for p in profiles:
        pr = p.get('pairing') or {}
        if not pr.get('samples'):
            continue
        hist = (pr.get('absHistMs') or {})
        counts = hist.get('counts') or []
        bw = hist.get('binWidth') or 1
        top = sorted(((c, i) for i, c in enumerate(counts)), reverse=True)[:4]
        peaks = ', '.join(f'{i * bw:.0f}-{(i + 1) * bw:.0f}ms:{c}' for c, i in top if c)
        rows.append((p.get('profile'), pr.get('samples'), st(pr, 'absMs'), st(pr, 'signedMs'), peaks))
    out += table(['profile', 'pairs', '|dt| ms', 'signed dt ms (L-R)', 'histogram peaks'], rows)

    out += '\n### Pose deltas between consecutive frames (GetCameraPose)\n\n'
    rows = []
    for p in profiles:
        for eye in ('L', 'R'):
            e = (p.get('eyes') or {}).get(eye)
            if not e:
                continue
            rows.append((p.get('profile'), eye, st(e, 'poseDeltaMm'), st(e, 'poseDeltaDeg', 2)))
    out += table(['profile', 'eye', 'position delta mm', 'rotation delta deg'], rows)

    starts = by_event(ev, 'pca_start')
    if starts:
        out += '\n### Intrinsics (first start per eye)\n\n'
        seen = set()
        rows = []
        for s in starts:
            if not s.get('playing') or s.get('eye') in seen:
                continue
            seen.add(s.get('eye'))
            i = s.get('intrinsics') or {}
            lo = i.get('lensOffset') or {}
            t = s.get('texture') or {}
            rows.append((s.get('eye'), s.get('profile'), f"{(s.get('current') or {}).get('w')}x{(s.get('current') or {}).get('h')}",
                         f"fx={fmt(i.get('fx'), 2)} fy={fmt(i.get('fy'), 2)} cx={fmt(i.get('cx'), 2)} cy={fmt(i.get('cy'), 2)} sensor={i.get('sensorW')}x{i.get('sensorH')}",
                         f"p=({fmt(lo.get('px'), 4)},{fmt(lo.get('py'), 4)},{fmt(lo.get('pz'), 4)}) q=({fmt(lo.get('qx'), 4)},{fmt(lo.get('qy'), 4)},{fmt(lo.get('qz'), 4)},{fmt(lo.get('qw'), 4)})",
                         f"{t.get('type')} {t.get('format')} {t.get('dimension')}"))
        out += table(['eye', 'profile', 'resolution', 'intrinsics', 'lens offset', 'texture'], rows)
        # baseline estimate from lens offsets
        lp = {s.get('eye'): (s.get('intrinsics') or {}).get('lensOffset') for s in starts if s.get('playing')}
        if lp.get('L') and lp.get('R'):
            l, r = lp['L'], lp['R']
            try:
                base = ((l['px'] - r['px']) ** 2 + (l['py'] - r['py']) ** 2 + (l['pz'] - r['pz']) ** 2) ** 0.5
                out += f'\nStereo baseline from lens offsets: **{base * 1000:.1f} mm**\n'
            except (KeyError, TypeError):
                pass
    return out


def sec_clocks(ev):
    out = section('Clock domains (OVR seconds vs CLOCK_MONOTONIC vs CLOCK_BOOTTIME vs UTC)')
    tr = by_event(ev, 'clock_triplet')
    tr = [t for t in tr if (t.get('ovrSeconds') or 0) > 0 and (t.get('monotonicNs') or 0) > 0]
    if not tr:
        return out + '_no clock triplets (native plugin or OVRPlugin unavailable)_\n'
    ovr = [t['ovrSeconds'] for t in tr]
    mono = [t['monotonicNs'] * 1e-9 for t in tr]
    boot = [t['boottimeNs'] * 1e-9 for t in tr]
    unix = [t.get('unixSeconds') or 0 for t in tr]
    rows = []
    for name, x, y in (('ovr -> monotonic', ovr, mono), ('monotonic -> boottime', mono, boot), ('monotonic -> utc', mono, unix), ('ovr -> utc', ovr, unix)):
        f = linear_fit(x, y)
        if f:
            rows.append((name, f['n'], f"{f['slope']:.9f}", f"{f['offset']:.6f}", fmt(f['driftPpm'], 3), fmt(f['rmsMs'], 3), fmt(f['maxMs'], 3)))
    out += table(['mapping', 'n', 'slope', 'offset s', 'drift ppm', 'residual rms ms', 'residual max ms'], rows)
    om = [(t.get('ovrMinusMonoMs') or 0) for t in tr]
    bm = [(t.get('bootMinusMonoMs') or 0) for t in tr]
    br = [(t.get('bracketNs') or 0) / 1e3 for t in tr]
    out += f'\nOVR - monotonic: median {statistics.median(om):.3f} ms (spread {max(om) - min(om):.3f} ms); boottime - monotonic: median {statistics.median(bm):.1f} ms (spread {max(bm) - min(bm):.3f} ms); sampling bracket median {statistics.median(br):.1f} us\n'
    out += '\nInterpretation: |OVR - monotonic| < 1 ms with zero drift means XrTime == CLOCK_MONOTONIC ns (path A candidate via XR_KHR_convert_timespec_time). boottime - monotonic = accumulated suspend time.\n'
    d = last(ev, 'done')
    if d and d.get('clockFits'):
        out += '\nOn-device fits (ClockDomains.Fit): `%s`\n' % json.dumps(d['clockFits'])
    return out


def sec_depth(ev):
    out = section('Environment Depth (AROcclusionManager)')
    s = first(ev, 'depth_start')
    d = last(ev, 'depth_profile')
    if s:
        out += 'found=%s addedManager=%s addedSession=%s subsystem=%s descriptor=%s mode=%s error=%s\n\n' % (
            fmt(s.get('found')), fmt(s.get('addedManager')), fmt(s.get('addedSession')), fmt(s.get('subsystem')), s.get('descriptor'), s.get('currentMode'), s.get('error'))
    if not d:
        return out + '_no depth profile_\n'
    rows = [
        ('frames / seconds / fps', f"{d.get('frames')} / {fmt(d.get('seconds'))} / {fmt(d.get('fps'), 2)}"),
        ('XrTime delta ms', st(d, 'deltaMs', 2)),
        ('Unity callback delta ms', st(d, 'unityCallbackDeltaMs', 2)),
        ('non-monotonic / no timestamp', f"{d.get('nonMonotonic')} / {d.get('noTimestamp')}"),
        ('age vs OVR now ms', st(d, 'ageVsOvrMs', 2)),
        ('age vs CLOCK_MONOTONIC ms', st(d, 'ageVsMonotonicMs', 2)),
        ('age vs CLOCK_BOOTTIME ms', st(d, 'ageVsBoottimeMs', 2)),
        ('texture', f"{d.get('texWidth')}x{d.get('texHeight')} slices={d.get('texSlices')} {d.get('texDimension')} {d.get('texFormat')} {d.get('texType')} externalTextures={d.get('externalTextures')}"),
        ('TryGetPoses ok / count', f"{d.get('posesOk')} / {d.get('poseCount')}"),
        ('TryGetFovs ok / count / fov0 deg', f"{d.get('fovsOk')} / {d.get('fovCount')} / {d.get('fov0Deg')}"),
        ('near/far', f"{fmt(d.get('nearZ'), 3)} / {fmt(d.get('farZ'), 3)} (planesOk={d.get('planesOk')})"),
        ('pose delta mm / deg', f"{st(d, 'poseDeltaMm')} / {st(d, 'poseDeltaDeg', 2)}"),
    ]
    out += table(['metric', 'value'], rows)
    return out


def sec_vulkan(ev):
    out = section('Vulkan (native device report + probe)')
    nd = first(ev, 'native_device')
    rep = None
    if nd:
        out += 'native available=%s vulkanReady=%s ahbImportRc=%s error=%s\n\n' % (fmt(nd.get('available')), fmt(nd.get('vulkanReady')), nd.get('ahbImportRc'), nd.get('error'))
        rep = nd.get('deviceReport')
    vp = last(ev, 'vulkan_probe')
    if vp and isinstance(vp.get('deviceReport'), dict):
        rep = vp['deviceReport']
    if isinstance(rep, dict):
        out += '### Device report\n\n'
        simple = [(k, v) for k, v in rep.items() if not isinstance(v, (dict, list))]
        out += table(['key', 'value'], [(k, fmt(v)) for k, v in simple])
        qf = rep.get('queueFamilies')
        if isinstance(qf, list):
            out += '\n**Queue families**\n\n'
            rows = []
            for i, q in enumerate(qf):
                if isinstance(q, dict):
                    rows.append((i, q.get('flags', q.get('queueFlags')), q.get('count', q.get('queueCount')), q.get('timestampValidBits'), json.dumps({k: v for k, v in q.items() if k not in ('flags', 'queueFlags', 'count', 'queueCount', 'timestampValidBits')})))
            out += table(['family', 'flags', 'count', 'timestampValidBits', 'other'], rows)
        for key in ('limits', 'features', 'queueInjection', 'injection', 'ahbImport', 'unityQueue'):
            v = rep.get(key)
            if isinstance(v, dict):
                out += f'\n**{key}**\n\n' + table(['key', 'value'], [(k, fmt(x)) for k, x in v.items()])
        for key in ('enabledExtensions', 'availableExtensions', 'extensions'):
            v = rep.get(key)
            if isinstance(v, list):
                interesting = [e for e in v if any(s in str(e) for s in ('sync', 'timeline', 'pipeline_binary', 'maintenance5', 'push_descriptor', 'global_priority', 'host_query_reset', 'buffer_device_address', 'hardware_buffer', 'ycbcr', 'queue_family_foreign', 'float16', '16bit', 'calibrated'))]
                out += f'\n**{key}** ({len(v)} total; scanner-relevant): `{", ".join(map(str, interesting))}`\n'
                if 'calibrated' not in ' '.join(map(str, v)):
                    out += '\nVK_KHR_calibrated_timestamps NOT present (GPU<->CPU timestamp correlation must use submit-time bracketing).\n' if key != 'enabledExtensions' else ''
    else:
        out += '_no device report (native plugin unavailable or report not JSON)_\n'
        if nd and nd.get('deviceReportRaw'):
            out += '\nRaw: `%s`\n' % str(nd.get('deviceReportRaw'))[:500]

    out += '\n### Probe steps\n\n'
    rows = []
    for s in by_event(ev, 'vulkan_step'):
        r = s.get('results')
        rows.append((s.get('step'), json.dumps(r)[:600] if r is not None else '-'))
    sk = first(ev, 'vulkan_skip')
    if sk:
        rows.append(('skipped', json.dumps({k: v for k, v in sk.items() if k not in ('seq', 't', 'utc', 'phase', 'event')})))
    out += table(['step', 'results (truncated)'], rows)

    res = vp.get('results') if vp else None
    if isinstance(res, dict):
        out += '\n### Final probe results\n\n'
        simple = [(k, fmt(v)) for k, v in res.items() if not isinstance(v, (dict, list))]
        out += table(['key', 'value'], simple)
        es = res.get('emptySubmitUs')
        if isinstance(es, list) and es:
            nums = [float(x) for x in es if isinstance(x, (int, float))]
            if nums:
                out += f'\nEmpty submit cost: n={len(nums)} p50={statistics.median(nums):.1f} us max={max(nums):.1f} us\n'
        pb = res.get('pipelineBinary')
        if isinstance(pb, dict):
            out += '\n**Pipeline binary**: ' + ', '.join(f'{k}={fmt(v)}' for k, v in pb.items()) + '\n'
        ov = res.get('overlap')
        if isinstance(ov, dict):
            out += '\n**Overlap (native)**: ' + ', '.join(f'{k}={fmt(v) if not isinstance(v, list) else str(len(v)) + " items"}' for k, v in ov.items()) + '\n'
            marks = ov.get('graphicsFrameMarks')
            jobs = ov.get('scannerJobs')
            if isinstance(marks, list) and isinstance(jobs, list) and marks and jobs:
                out += analyze_overlap(marks, jobs, res.get('timestampPeriodNs'))
    return out


def analyze_overlap(marks, jobs, period_ns):
    """marks: graphics timestamps (ticks) per frame; jobs: [{start,end}] or [[start,end]] scanner ticks.
    Fraction of scanner busy time that overlaps frame intervals on the same GPU timeline."""
    try:
        p = float(period_ns) if period_ns else 1.0
        ticks = sorted(float(m if not isinstance(m, dict) else m.get('ticks', m.get('t', 0))) for m in marks)
        ivals = []
        for j in jobs:
            if isinstance(j, dict):
                s, e = j.get('start', j.get('startTicks')), j.get('end', j.get('endTicks'))
            else:
                s, e = j[0], j[1]
            if s is None or e is None or e <= s:
                continue
            ivals.append((float(s), float(e)))
        if len(ticks) < 2 or not ivals:
            return '\n_overlap: not enough data_\n'
        frames = list(zip(ticks[:-1], ticks[1:]))
        busy = sum(e - s for s, e in ivals)
        # overlap between scanner intervals and inter-mark frame intervals; the whole timeline is covered by frames,
        # so instead measure how many frames contain scanner work and the per-frame scanner occupancy.
        occ = []
        for fs, fe in frames:
            o = 0.0
            for s, e in ivals:
                o += max(0.0, min(fe, e) - max(fs, s))
            occ.append(o / (fe - fs) if fe > fs else 0.0)
        with_work = sum(1 for o in occ if o > 0)
        job_ms = [(e - s) * p / 1e6 for s, e in ivals]
        frame_ms = [(fe - fs) * p / 1e6 for fs, fe in frames]
        return ('\n**Overlap analysis** (same GPU timestamp timeline): frames=%d, frames containing scanner work=%d (%.0f%%), '
                'scanner occupancy per frame p50=%.2f max=%.2f, scanner job ms p50=%.3f max=%.3f (n=%d), frame interval ms p50=%.2f p99=%.2f, total scanner busy=%.1f ms\n') % (
            len(frames), with_work, 100.0 * with_work / len(frames), statistics.median(occ), max(occ),
            statistics.median(job_ms), max(job_ms), len(job_ms), statistics.median(frame_ms), sorted(frame_ms)[int(0.99 * (len(frame_ms) - 1))], busy * p / 1e6)
    except Exception as e:  # noqa: BLE001
        return f'\n_overlap analysis failed: {e}_\n'


def sec_frames(ev):
    out = section('Unity frame time with vs without scanner-queue overlap')
    segs = by_event(ev, 'vulkan_frames')
    rows = []
    for s in segs:
        xr = s.get('xrStats') or {}
        xrs = ', '.join(f"{k}: p50={fmt((v or {}).get('p50'), 2)} p99={fmt((v or {}).get('p99'), 2)}" for k, v in xr.items())
        rows.append((s.get('segment'), s.get('frames'), st(s, 'frameMs', 2), st(s, 'gpuMs', 2) if s.get('frameTimingManager') else 'FrameTimingManager off', st(s, 'mainThreadMs', 2), st(s, 'renderThreadMs', 2), xrs or '-'))
    out += table(['segment', 'frames', 'unscaledDeltaTime ms', 'gpu ms', 'main thread ms', 'render thread ms', 'XRStats'], rows)
    if len(segs) >= 2:
        d = {s.get('segment'): s for s in segs}
        pre, ov, post = d.get('pre'), d.get('overlap'), d.get('post')
        if ov and (pre or post):
            base = pre or post
            try:
                out += '\nOverlap effect on frame p99: %.2f ms -> %.2f ms (delta %.2f ms); p50: %.2f -> %.2f ms\n' % (
                    base['frameMs']['p99'], ov['frameMs']['p99'], ov['frameMs']['p99'] - base['frameMs']['p99'], base['frameMs']['p50'], ov['frameMs']['p50'])
            except (KeyError, TypeError):
                pass
    return out


def sec_camera2(ev):
    out = section('Camera2 NDK probe')
    rows = []
    for c in by_event(ev, 'camera2'):
        rep = c.get('report')
        if isinstance(rep, dict):
            summary = json.dumps(rep)[:1200]
        else:
            summary = str(c.get('reportRaw') or c.get('reason') or '-')[:400]
        rows.append((c.get('mode'), fmt(c.get('available')), c.get('startRc'), c.get('stopRc'), fmt(c.get('pcaLeftPlayingBefore')), fmt(c.get('pcaRightPlayingBefore')), c.get('pcaEyesPlayingAfter'), c.get('error'), summary))
    out += table(['mode', 'available', 'startRc', 'stopRc', 'PCA L before', 'PCA R before', 'PCA eyes playing after', 'error', 'report (truncated)'], rows)
    for c in by_event(ev, 'camera2'):
        rep = c.get('report')
        if isinstance(rep, dict):
            cams = rep.get('cameras')
            if isinstance(cams, list):
                out += f"\n**{c.get('mode')}: cameras**\n\n"
                r2 = []
                for cam in cams:
                    if isinstance(cam, dict):
                        r2.append((cam.get('id'), cam.get('timestampSource', cam.get('TIMESTAMP_SOURCE')), cam.get('position', cam.get('lensFacing')), cam.get('frames'), fmt(cam.get('fps')),
                                   json.dumps({k: v for k, v in cam.items() if k not in ('id', 'timestampSource', 'TIMESTAMP_SOURCE', 'position', 'lensFacing', 'frames', 'fps')})[:400]))
                out += table(['id', 'timestamp source', 'position', 'frames', 'fps', 'other'], r2)
    return out


def sec_memory(ev, meminfo):
    out = section('Memory: PSS per phase')
    mem = by_event(ev, 'memory')
    rows = []
    per_phase = OrderedDict()
    for m in mem:
        ph = m.get('phaseName')
        per_phase.setdefault(ph, []).append(m)
    prev = None
    for ph, ms in per_phase.items():
        lastm = ms[-1]
        pss = lastm.get('totalPssKB') or lastm.get('total_pssKB') or lastm.get('debugPssKB')
        gfx = lastm.get('graphicsKB')
        nh = lastm.get('native_heapKB')
        delta = (pss - prev) if (pss is not None and prev is not None) else None
        rows.append((ph, len(ms), fmt(pss / 1024.0 if pss else None), fmt(delta / 1024.0 if delta is not None else None), fmt(gfx / 1024.0 if gfx else None), fmt(nh / 1024.0 if nh else None),
                     fmt(lastm.get('java_heapKB', 0) / 1024.0 if lastm.get('java_heapKB') else None), fmt(lastm.get('unityAllocatedMB')), fmt(lastm.get('unityGfxDriverMB'))))
        if pss is not None:
            prev = pss
    out += table(['phase', 'samples', 'PSS MB (last)', 'delta vs prev phase MB', 'Graphics MB', 'Native heap MB', 'Java heap MB', 'Unity allocated MB', 'Unity gfx driver MB'], rows)
    out += '\nComponent mapping: boot = XR only; depth = +Env Depth; pca_L_30_960 = +PCA L; pca_30_960 = +PCA L+R; later PCA profiles = resolution/fps effect; camera2_excl = Camera2 NDK only; vulkan = +native plugin work.\n'
    if meminfo:
        out += '\n### dumpsys meminfo samples (host side, every 5 s)\n\n'
        rows = []
        for s in meminfo:
            rows.append((s.get('file'), s.get('elapsed'), fmt((s.get('TOTAL PSS') or 0) / 1024.0), fmt((s.get('Graphics') or 0) / 1024.0), fmt((s.get('GL mtrack') or 0) / 1024.0), fmt((s.get('EGL mtrack') or 0) / 1024.0), fmt((s.get('Native Heap') or s.get('Native Heap (summary)') or 0) / 1024.0)))
        out += table(['file', 'elapsed s', 'TOTAL PSS MB', 'Graphics MB', 'GL mtrack MB', 'EGL mtrack MB', 'Native Heap MB'], rows)
        vals = [s.get('TOTAL PSS') for s in meminfo if s.get('TOTAL PSS')]
        if vals:
            out += f'\nPSS min/max over run: {min(vals) / 1024.0:.0f} / {max(vals) / 1024.0:.0f} MB (limit 5.75 GiB = 5888 MB)\n'
    return out


def sec_thermal(ev, vrapi):
    out = section('Thermal / power')
    th = by_event(ev, 'thermal') + by_event(ev, 'thermal_snapshot')
    rows = []
    for t in th:
        o = t.get('ovr') or {}
        a = t.get('android') or {}
        zones = a.get('zones') or []
        hot = sorted(((z.get('milliC') or 0, z.get('type')) for z in zones if isinstance(z, dict)), reverse=True)[:3]
        rows.append((fmt(t.get('t')), t.get('phaseName'), fmt(o.get('batteryTemperatureC')), fmt(o.get('batteryLevel'), 2), o.get('powerSaving'), o.get('cpuLevel'), o.get('gpuLevel'),
                     o.get('suggestedCpuPerfLevel'), o.get('suggestedGpuPerfLevel'), a.get('thermalStatus'), fmt(a.get('thermalHeadroom10s'), 2), ', '.join(f'{n}={c / 1000.0:.1f}C' for c, n in hot)))
    out += table(['t s', 'phase', 'battery C', 'battery', 'powerSaving', 'cpuLevel', 'gpuLevel', 'sugg CPU', 'sugg GPU', 'android thermalStatus', 'headroom', 'hottest zones'], rows)
    if vrapi:
        fps = [v['fps'] for v in vrapi]
        out += '\n### VrApi logcat FPS lines\n\n'
        out += f'samples={len(fps)} fps min/median/max = {min(fps)}/{statistics.median(fps)}/{max(fps)} target={vrapi[0]["target"]}\n'
        keys = ['Prd', 'Stale', 'Early', 'App', 'GPU%', 'CPU%', 'Temp', 'Free', 'CPU4/GPU']
        rows = []
        step = max(1, len(vrapi) // 40)
        for v in vrapi[::step]:
            f = v['fields']
            rows.append([v['ts'], v['fps']] + [f.get(k, '-') for k in keys])
        out += table(['ts', 'FPS'] + keys, rows)
    return out


def sec_native_lines(native):
    out = section('FS-NATIVE logcat lines')
    if not native:
        return out + '_none_\n'
    out += f'{len(native)} lines; first 40:\n\n```\n' + '\n'.join(native[:40]) + '\n```\n'
    return out


def sec_errors(ev):
    errs = by_event(ev, 'error') + by_event(ev, 'pca_config_error')
    out = section('Errors reported by the probe')
    if not errs:
        return out + '_none_\n'
    return out + table(['phase', 'where', 'type', 'message'], [(e.get('phase'), e.get('where') or e.get('eye'), e.get('type'), (e.get('message') or e.get('error') or '')[:300]) for e in errs])


def sec_schedule(ev):
    out = section('Run schedule')
    rows = []
    begins = {}
    for e in by_event(ev, 'phase'):
        if e.get('action') == 'begin':
            begins[e.get('name')] = e
        elif e.get('action') == 'end':
            rows.append((e.get('name'), fmt(begins.get(e.get('name'), {}).get('elapsedRun')), fmt(e.get('seconds'))))
    d = last(ev, 'done')
    if d:
        out += f"Summary: `{d.get('summary')}`  run {fmt(d.get('runSeconds'))} s\n\n"
    return out + table(['phase', 'start s', 'duration s'], rows)


# --------------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('input', help='evidence directory (from run_probe.sh) or a probe jsonl file')
    ap.add_argument('-o', '--output', default=None, help='output markdown path (default <dir>/DEVICE_ENVELOPE.md)')
    ap.add_argument('--logcat', default=None)
    ap.add_argument('--meminfo-dir', default=None)
    args = ap.parse_args()

    jsonl_files = []
    logcat = args.logcat
    meminfo_dir = args.meminfo_dir
    if os.path.isdir(args.input):
        jsonl_files = sorted(glob.glob(os.path.join(args.input, '**', '*.jsonl'), recursive=True))
        logcat = logcat or (os.path.join(args.input, 'logcat.txt') if os.path.exists(os.path.join(args.input, 'logcat.txt')) else None)
        meminfo_dir = meminfo_dir or os.path.join(args.input, 'meminfo')
        out_path = args.output or os.path.join(args.input, 'DEVICE_ENVELOPE.md')
    else:
        jsonl_files = [args.input]
        out_path = args.output or os.path.splitext(args.input)[0] + '.DEVICE_ENVELOPE.md'

    events = []
    for p in jsonl_files:
        events.extend(load_jsonl(p))
    lc_events, native, vrapi = load_logcat(logcat)
    source = 'jsonl'
    if not events and lc_events:
        events = lc_events
        source = 'logcat'
    elif lc_events and len(lc_events) > len(events):
        # jsonl truncated (pull failed mid-run) - merge by seq
        seen = {e.get('seq') for e in events}
        events.extend(e for e in lc_events if e.get('seq') not in seen)
        source = 'jsonl+logcat'
    events.sort(key=lambda e: (e.get('seq') or 0))
    meminfo = load_meminfo(meminfo_dir)

    md = ['# DEVICE_ENVELOPE (draft, generated by Tools/probe/analyze_probe.py)\n',
          f'\nSource: {args.input}  events={len(events)} ({source})  jsonl={[os.path.basename(p) for p in jsonl_files]}  logcat={logcat}  meminfo samples={len(meminfo)}  vrapi lines={len(vrapi)}\n']
    for fn in (lambda: sec_schedule(events), lambda: sec_boot(events), lambda: sec_pca(events), lambda: sec_clocks(events), lambda: sec_depth(events),
               lambda: sec_vulkan(events), lambda: sec_frames(events), lambda: sec_camera2(events), lambda: sec_memory(events, meminfo),
               lambda: sec_thermal(events, vrapi), lambda: sec_native_lines(native), lambda: sec_errors(events)):
        try:
            md.append(fn())
        except Exception as e:  # noqa: BLE001
            md.append(f'\n## (section failed)\n\n_{type(e).__name__}: {e}_\n')
    os.makedirs(os.path.dirname(os.path.abspath(out_path)) or '.', exist_ok=True)
    with open(out_path, 'w', encoding='utf-8') as f:
        f.write(''.join(md))
    print(f'wrote {out_path} ({len(events)} events)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
