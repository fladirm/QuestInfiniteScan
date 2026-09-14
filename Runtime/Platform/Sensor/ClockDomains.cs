using System;
using System.Collections.Generic;

namespace FinalScan.Platform.Sensor
{
    /// <summary>One simultaneous reading of the four clocks the C01 clock-domain gate relates (§4.2).</summary>
    [Serializable]
    public struct ClockTriplet
    {
        /// <summary>OVRPlugin.GetTimeInSeconds() (OpenXR runtime clock; XrTime = seconds * 1e9 on Quest).</summary>
        public double ovrSeconds;
        /// <summary>CLOCK_MONOTONIC ns (libFinalScanNative FsNative_MonotonicNowNs).</summary>
        public long monotonicNs;
        /// <summary>CLOCK_BOOTTIME ns (FsNative_BoottimeNowNs) - Camera2 SENSOR_TIMESTAMP base when TIMESTAMP_SOURCE=REALTIME.</summary>
        public long boottimeNs;
        /// <summary>DateTime.UtcNow.Ticks (100 ns since 0001-01-01; CLOCK_REALTIME) - base of PassthroughCameraAccess.Timestamp.</summary>
        public long utcTicks;

        public double UnixSeconds => ClockDomains.UtcTicksToUnixSeconds(utcTicks);
        public double MonotonicSeconds => monotonicNs * 1e-9;
        public double BoottimeSeconds => boottimeNs * 1e-9;
    }

    public enum ClockAxis { OvrSeconds, MonotonicNs, BoottimeNs, UtcTicks }

    /// <summary>y = slope * x + offset, both axes in seconds.</summary>
    public struct LinearFit
    {
        public bool valid;
        public int count;
        public double slope;
        public double offset;
        public double residualRms;
        public double residualMax;
        /// <summary>Mean of x used for centering (offset already refers to uncentered x).</summary>
        public double xMean, yMean;

        public double Map(double x) => slope * x + offset;
        public double Inverse(double y) => slope == 0 ? double.NaN : (y - offset) / slope;
        /// <summary>Deviation of the slope from 1 in parts per million (clock drift).</summary>
        public double DriftPpm => (slope - 1.0) * 1e6;
    }

    /// <summary>Pure math helpers for mapping between clock domains (least squares, no Unity API).</summary>
    public static class ClockDomains
    {
        public const long UnixEpochTicks = 621355968000000000L; // DateTime.UnixEpoch.Ticks
        public const double TicksPerSecond = 1e7;

        public static double UtcTicksToUnixSeconds(long ticks) => (ticks - UnixEpochTicks) / TicksPerSecond;
        public static long UnixSecondsToUtcTicks(double unixSeconds) => UnixEpochTicks + (long)Math.Round(unixSeconds * TicksPerSecond);
        public static double NsToSeconds(long ns) => ns * 1e-9;
        public static long SecondsToNs(double s) => (long)Math.Round(s * 1e9);
        /// <summary>Unix microseconds (PassthroughCameraAccess.Timestamp = UnixEpoch + micros) to UTC ticks.</summary>
        public static long UnixMicrosToUtcTicks(long micros) => UnixEpochTicks + micros * 10L;

        public static double Seconds(in ClockTriplet t, ClockAxis axis)
        {
            switch (axis)
            {
                case ClockAxis.OvrSeconds: return t.ovrSeconds;
                case ClockAxis.MonotonicNs: return t.monotonicNs * 1e-9;
                case ClockAxis.BoottimeNs: return t.boottimeNs * 1e-9;
                case ClockAxis.UtcTicks: return UtcTicksToUnixSeconds(t.utcTicks);
                default: return double.NaN;
            }
        }

        /// <summary>
        /// Ordinary least squares y = a*x + b over paired samples. Inputs are centered on their means before
        /// solving, which keeps the fit numerically sound for clocks with large absolute values (ns since boot,
        /// seconds since 1970). Returns valid=false with fewer than 2 distinct x values.
        /// </summary>
        public static LinearFit Fit(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            var f = new LinearFit();
            if (x == null || y == null) return f;
            int n = Math.Min(x.Count, y.Count);
            if (n < 2) return f;
            double mx = 0, my = 0;
            for (int i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
            mx /= n; my /= n;
            double sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++) { double dx = x[i] - mx; sxx += dx * dx; sxy += dx * (y[i] - my); }
            if (sxx <= 0) return f;
            f.slope = sxy / sxx;
            f.offset = my - f.slope * mx;
            f.xMean = mx; f.yMean = my; f.count = n;
            double sq = 0, mx2 = 0;
            for (int i = 0; i < n; i++)
            {
                double r = y[i] - (f.slope * (x[i] - mx) + my);
                sq += r * r; if (Math.Abs(r) > mx2) mx2 = Math.Abs(r);
            }
            f.residualRms = Math.Sqrt(sq / n);
            f.residualMax = mx2;
            f.valid = true;
            return f;
        }

        /// <summary>Fits <paramref name="to"/> = f(<paramref name="from"/>) over triplets (both in seconds).</summary>
        public static LinearFit Fit(IReadOnlyList<ClockTriplet> samples, ClockAxis from, ClockAxis to)
        {
            if (samples == null) return default;
            var xs = new double[samples.Count]; var ys = new double[samples.Count];
            for (int i = 0; i < samples.Count; i++) { xs[i] = Seconds(samples[i], from); ys[i] = Seconds(samples[i], to); }
            return Fit(xs, ys);
        }

        /// <summary>Offset-only estimate (slope fixed at 1): median of (to - from) plus spread.</summary>
        public static bool TryOffset(IReadOnlyList<ClockTriplet> samples, ClockAxis from, ClockAxis to, out double offsetSeconds, out double spreadSeconds)
        {
            offsetSeconds = double.NaN; spreadSeconds = double.NaN;
            if (samples == null || samples.Count == 0) return false;
            var d = new List<double>(samples.Count);
            foreach (var s in samples) d.Add(Seconds(s, to) - Seconds(s, from));
            d.Sort();
            offsetSeconds = d[d.Count / 2];
            spreadSeconds = d[d.Count - 1] - d[0];
            return true;
        }

        /// <summary>Converts a timestamp on axis <paramref name="from"/> to axis <paramref name="to"/> with a fit (seconds in, seconds out).</summary>
        public static double Convert(double fromSeconds, in LinearFit fit) => fit.valid ? fit.Map(fromSeconds) : double.NaN;

        /// <summary>XrTime (ns) to OVR seconds assumes the same monotonic base; the fit tells whether that holds.</summary>
        public static double XrTimeNsToOvrSeconds(long xrTimeNs) => xrTimeNs * 1e-9;
    }
}
