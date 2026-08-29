using System;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Maps Environment Depth XrTime nanoseconds into PCA's Unix-realtime
    /// timestamp domain. UTC is bracketed by the OVR monotonic clock, so
    /// callback delivery latency cannot bias RGB-D pairing.
    /// </summary>
    internal sealed class SensorClockMapper
    {
        private readonly Func<double> _xrSeconds;
        private readonly Func<DateTime> _utcNow;
        private double _unixMinusXrSeconds;

        internal SensorClockMapper() : this(
            () =>
            {
                double seconds = OVRPlugin.GetTimeInSeconds();
                return seconds > 0.0
                    ? seconds : Time.realtimeSinceStartupAsDouble;
            },
            () => DateTime.UtcNow)
        {
        }

        internal SensorClockMapper(Func<double> xrSeconds,
            Func<DateTime> utcNow)
        {
            _xrSeconds = xrSeconds ?? throw new ArgumentNullException(
                nameof(xrSeconds));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        internal bool IsReady { get; private set; }
        internal double UncertaintySeconds { get; private set; } =
            double.PositiveInfinity;

        internal void Reset()
        {
            IsReady = false;
            _unixMinusXrSeconds = 0.0;
            UncertaintySeconds = double.PositiveInfinity;
        }

        internal bool TryCaptureAnchor(double maximumBracketSeconds = 0.005)
        {
            double before = _xrSeconds();
            DateTime utc = _utcNow();
            double after = _xrSeconds();
            double bracket = after - before;
            if (!double.IsFinite(before) || !double.IsFinite(after) ||
                before <= 0.0 || after < before ||
                bracket > maximumBracketSeconds ||
                utc.Ticks <= DateTime.UnixEpoch.Ticks)
            {
                Reset();
                return false;
            }

            double midpoint = before + bracket * 0.5;
            double unixSeconds = (utc.Ticks - DateTime.UnixEpoch.Ticks) /
                (double)TimeSpan.TicksPerSecond;
            _unixMinusXrSeconds = unixSeconds - midpoint;
            UncertaintySeconds = Math.Max(0.0001,
                bracket * 0.5 + 0.0000001);
            IsReady = true;
            return true;
        }

        internal bool TryMapXrNanoseconds(long timestampNanoseconds,
            out double unixSeconds)
        {
            if (!IsReady || timestampNanoseconds <= 0L)
            {
                unixSeconds = 0.0;
                return false;
            }

            unixSeconds = timestampNanoseconds * 1e-9 +
                _unixMinusXrSeconds;
            return double.IsFinite(unixSeconds);
        }
    }
}
