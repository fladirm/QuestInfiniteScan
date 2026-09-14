using System;
using System.Globalization;

namespace FinalScan.Platform.Sensor
{
    /// <summary>Which clock-domain path (contract §4.2) currently produces XrTime for camera frames.</summary>
    public enum ClockGatePath
    {
        /// <summary>No frame converted yet.</summary>
        Unknown = 0,
        /// <summary>A: the frame carries a CLOCK_MONOTONIC/XrTime-based timestamp verified against the OpenXR clock.</summary>
        A = 1,
        /// <summary>B: controllable Camera2 timestamp base (native ingest only; never selected on the MRUK path).</summary>
        B = 2,
        /// <summary>C: DateTime (CLOCK_REALTIME) timestamp mapped through a running linear fit with explicit uncertainty.</summary>
        C = 3
    }

    /// <summary>Uncertainty class of the produced timestamps (provisional thresholds from §4.2: 2 ms lowers confidence, 8 ms rejects).</summary>
    public enum ClockUncertaintyClass { Unknown = 0, Exact = 1, Good = 2, Degraded = 3, Reject = 4 }

    /// <summary>
    /// Canonical time = XrTime ns (contract §4.1). Converts PassthroughCameraAccess timestamps into XrTime and exposes the
    /// gate path and uncertainty. Pure C#: the host feeds reference readings every frame and asks for conversions on every
    /// camera callback; the class never calls Unity/OVR APIs so it runs identically in host tests and replay.
    ///
    /// Path A: MRUK's private <c>_timestampNsMonotonic</c> claims the XrTime base. It is trusted only while a running
    /// comparison of native CLOCK_MONOTONIC (FsNative_MonotonicNowNs) against OVRPlugin.GetTimeInSeconds()*1e9 shows
    /// |median offset| &lt; <see cref="PathAOffsetLimitNs"/> and jitter &lt; <see cref="PathAJitterLimitNs"/>, and the frame
    /// latency (ovrNow - timestamp) stays inside [<see cref="MinPlausibleLatencyNs"/>, <see cref="MaxPlausibleLatencyNs"/>].
    /// Uncertainty = measured jitter.
    /// Path C: DateTime.Timestamp (Unix micros, CLOCK_REALTIME) → OVR seconds through <see cref="ClockDomains.Fit"/> over the
    /// last <see cref="Window"/> reference samples; uncertainty = residual RMS combined with the sampling bracket.
    /// Path B is reserved for the native Camera2 ingest adapter (§5) and never selected here.
    /// </summary>
    public sealed class XrClock
    {
        public const int Window = 64;
        public const long PathAOffsetLimitNs = 2_000_000;      // 2 ms
        public const long PathAJitterLimitNs = 2_000_000;      // 2 ms
        public const long MinPlausibleLatencyNs = -5_000_000;  // -5 ms (allow tiny negative from scheduling)
        public const long MaxPlausibleLatencyNs = 500_000_000; // 500 ms
        public const int PathAImplausibleLimit = 10;           // consecutive implausible frames before falling to C
        public const long UncertaintyFloorNs = 100_000;        // 0.1 ms: nothing is reported below this
        public const long ExactLimitNs = 500_000, GoodLimitNs = 2_000_000, DegradedLimitNs = 8_000_000;

        public ClockGatePath GatePath { get; private set; } = ClockGatePath.Unknown;
        public long UncertaintyNs { get; private set; } = long.MaxValue;
        public ClockUncertaintyClass UncertaintyClass { get; private set; } = ClockUncertaintyClass.Unknown;
        /// <summary>True once a frame carried a positive monotonic-ns field.</summary>
        public bool MonotonicFieldSeen { get; private set; }
        /// <summary>True while path A verification (native monotonic vs OVR) passes.</summary>
        public bool PathAVerified { get; private set; }
        public double PathAOffsetNs { get; private set; } = double.NaN;
        public double PathAJitterNs { get; private set; } = double.NaN;
        public int PathAOffsetSamples => _offsetCount;
        public LinearFit PathCFit { get; private set; }
        public int ReferenceSamples => _refCount;
        public long FramesConverted { get; private set; }
        public long FramesRejected { get; private set; }
        public int ConsecutiveImplausible { get; private set; }
        public long ImplausibleTotal { get; private set; }
        public int GateChanges { get; private set; }
        /// <summary>Invoked with a ready-to-log "FS-SENSOR clock_gate {json}" line whenever path or uncertainty class changes.</summary>
        public event Action<string> GateChanged;

        // path A verification ring: ovrNow*1e9 - monoNowNs
        readonly double[] _offsets = new double[Window]; int _offsetHead, _offsetCount;
        // path C fit ring: x = unix seconds (utc), y = ovr seconds
        readonly double[] _xs = new double[Window]; readonly double[] _ys = new double[Window]; int _refHead, _refCount;
        double _bracketMaxSeconds;
        readonly double[] _xsTmp = new double[Window]; readonly double[] _ysTmp = new double[Window];

        /// <summary>
        /// One reference reading per host frame. <paramref name="ovrBeforeSeconds"/>/<paramref name="ovrAfterSeconds"/> bracket
        /// the DateTime read; <paramref name="monoNowNs"/> &lt; 0 when the native clock is unavailable (path A then cannot verify).
        /// </summary>
        public void AddReference(double ovrBeforeSeconds, long utcTicks, double ovrAfterSeconds, long monoNowNs)
        {
            if (ovrBeforeSeconds <= 0 || ovrAfterSeconds <= 0) return;
            double ovrMid = 0.5 * (ovrBeforeSeconds + ovrAfterSeconds);
            double bracket = 0.5 * Math.Abs(ovrAfterSeconds - ovrBeforeSeconds);
            _xs[_refHead] = ClockDomains.UtcTicksToUnixSeconds(utcTicks); _ys[_refHead] = ovrMid;
            _refHead = (_refHead + 1) % Window; if (_refCount < Window) _refCount++;
            _bracketMaxSeconds = _refCount == 1 ? bracket : Math.Max(bracket, _bracketMaxSeconds * 0.9); // decays so one hiccup does not stick
            if (monoNowNs > 0)
            {
                _offsets[_offsetHead] = ovrMid * 1e9 - monoNowNs;
                _offsetHead = (_offsetHead + 1) % Window; if (_offsetCount < Window) _offsetCount++;
                RecomputePathA();
            }
            RecomputePathC();
        }

        void RecomputePathA()
        {
            if (_offsetCount < 4) { PathAVerified = false; return; }
            double med = SensorMath.Median(_offsets, _offsetCount);
            double sq = 0; for (int i = 0; i < _offsetCount; i++) { double r = _offsets[i] - med; sq += r * r; }
            double rms = Math.Sqrt(sq / _offsetCount);
            PathAOffsetNs = med; PathAJitterNs = rms;
            PathAVerified = Math.Abs(med) < PathAOffsetLimitNs && rms < PathAJitterLimitNs;
        }

        void RecomputePathC()
        {
            if (_refCount < 4) { PathCFit = default; return; }
            for (int i = 0; i < _refCount; i++) { _xsTmp[i] = _xs[i]; _ysTmp[i] = _ys[i]; }
            PathCFit = ClockDomains.Fit(new ArraySegment<double>(_xsTmp, 0, _refCount), new ArraySegment<double>(_ysTmp, 0, _refCount));
        }

        /// <summary>
        /// Converts one camera frame timestamp. <paramref name="monoFieldNs"/> ≤ 0 when the private field is missing.
        /// <paramref name="ovrNowSeconds"/> is the OpenXR clock read in the same callback (latency plausibility check).
        /// Returns false when no path can produce a timestamp (uncertainty class Reject or no fit yet).
        /// </summary>
        public bool TryConvert(long monoFieldNs, long utcTicks, double ovrNowSeconds, out long xrTimeNs, out long uncertaintyNs, out ClockGatePath path)
        {
            xrTimeNs = 0; uncertaintyNs = long.MaxValue; path = ClockGatePath.Unknown;
            if (monoFieldNs > 0) MonotonicFieldSeen = true;

            bool pathA = false;
            if (monoFieldNs > 0 && PathAVerified)
            {
                double latency = ovrNowSeconds > 0 ? ovrNowSeconds * 1e9 - monoFieldNs : 0;
                bool plausible = ovrNowSeconds <= 0 || (latency >= MinPlausibleLatencyNs && latency <= MaxPlausibleLatencyNs);
                if (plausible) { ConsecutiveImplausible = 0; pathA = true; }
                else { ConsecutiveImplausible++; ImplausibleTotal++; }
                if (ConsecutiveImplausible >= PathAImplausibleLimit) pathA = false;
            }

            if (pathA)
            {
                path = ClockGatePath.A;
                xrTimeNs = monoFieldNs;
                uncertaintyNs = Math.Max(UncertaintyFloorNs, (long)Math.Ceiling(PathAJitterNs));
            }
            else if (PathCFit.valid)
            {
                path = ClockGatePath.C;
                double sec = PathCFit.Map(ClockDomains.UtcTicksToUnixSeconds(utcTicks));
                xrTimeNs = (long)Math.Round(sec * 1e9);
                double unc = Math.Sqrt(PathCFit.residualRms * PathCFit.residualRms + _bracketMaxSeconds * _bracketMaxSeconds) * 1e9;
                uncertaintyNs = Math.Max(UncertaintyFloorNs, (long)Math.Ceiling(unc));
            }
            else
            {
                FramesRejected++;
                return false;
            }

            FramesConverted++;
            Publish(path, uncertaintyNs);
            return uncertaintyNs < DegradedLimitNs;
        }

        public static ClockUncertaintyClass Classify(long uncertaintyNs)
        {
            if (uncertaintyNs < ExactLimitNs) return ClockUncertaintyClass.Exact;
            if (uncertaintyNs < GoodLimitNs) return ClockUncertaintyClass.Good;
            if (uncertaintyNs < DegradedLimitNs) return ClockUncertaintyClass.Degraded;
            return ClockUncertaintyClass.Reject;
        }

        void Publish(ClockGatePath path, long uncertaintyNs)
        {
            var cls = Classify(uncertaintyNs);
            bool changed = path != GatePath || cls != UncertaintyClass;
            GatePath = path; UncertaintyNs = uncertaintyNs; UncertaintyClass = cls;
            if (!changed) return;
            GateChanges++;
            if (uncertaintyNs >= DegradedLimitNs) FramesRejected++;
            GateChanged?.Invoke(GateLine());
        }

        /// <summary>"FS-SENSOR clock_gate {json}" describing the current state.</summary>
        public string GateLine()
        {
            var ci = CultureInfo.InvariantCulture;
            return "FS-SENSOR clock_gate {\"path\":\"" + GatePath + "\",\"class\":\"" + UncertaintyClass + "\",\"uncertaintyNs\":" + UncertaintyNs.ToString(ci)
                 + ",\"monoField\":" + (MonotonicFieldSeen ? "true" : "false") + ",\"pathAVerified\":" + (PathAVerified ? "true" : "false")
                 + ",\"pathAOffsetNs\":" + Num(PathAOffsetNs) + ",\"pathAJitterNs\":" + Num(PathAJitterNs) + ",\"pathAOffsetSamples\":" + _offsetCount.ToString(ci)
                 + ",\"pathCValid\":" + (PathCFit.valid ? "true" : "false") + ",\"pathCResidualRmsNs\":" + Num(PathCFit.valid ? PathCFit.residualRms * 1e9 : double.NaN)
                 + ",\"pathCDriftPpm\":" + Num(PathCFit.valid ? PathCFit.DriftPpm : double.NaN) + ",\"refSamples\":" + _refCount.ToString(ci)
                 + ",\"implausible\":" + ImplausibleTotal.ToString(ci) + ",\"converted\":" + FramesConverted.ToString(ci) + ",\"rejected\":" + FramesRejected.ToString(ci) + "}";
        }

        static string Num(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("R", CultureInfo.InvariantCulture);
    }
}
