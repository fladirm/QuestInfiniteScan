using System;
using UnityEngine;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Maps AR depth XrTime nanoseconds into PCA's Unix-realtime timestamp domain.
    /// Anchors are bracketed so scheduler latency becomes explicit uncertainty.
    /// </summary>
    internal sealed class RigClockMapper
    {
        private readonly Func<double> _xrSeconds;
        private readonly Func<DateTime> _utcNow;
        private long _unixMinusXrNanoseconds;
        private long _anchorUncertaintyNanoseconds;
        private bool _valid;

        internal RigClockMapper(Func<double> xrSeconds, Func<DateTime> utcNow)
        {
            _xrSeconds = xrSeconds ?? throw new ArgumentNullException(nameof(xrSeconds));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        internal static RigClockMapper CreateRuntime()
        {
            return new RigClockMapper(
                () =>
                {
                    double seconds = OVRPlugin.GetTimeInSeconds();
                    return seconds > 0.0 ? seconds : Time.realtimeSinceStartupAsDouble;
                },
                () => DateTime.UtcNow);
        }

        internal bool IsValid => _valid;
        internal long AnchorUncertaintyNanoseconds => _anchorUncertaintyNanoseconds;

        internal bool TryCaptureAnchor(long maximumBracketNanoseconds = 5_000_000L)
        {
            double beforeSeconds = _xrSeconds();
            DateTime utc = _utcNow();
            double afterSeconds = _xrSeconds();
            if (!double.IsFinite(beforeSeconds) || !double.IsFinite(afterSeconds) ||
                beforeSeconds <= 0.0 || afterSeconds < beforeSeconds ||
                utc.Ticks <= DateTime.UnixEpoch.Ticks)
            {
                _valid = false;
                return false;
            }

            long before = SecondsToNanoseconds(beforeSeconds);
            long after = SecondsToNanoseconds(afterSeconds);
            long bracket = after - before;
            if (bracket < 0L || bracket > maximumBracketNanoseconds)
            {
                _valid = false;
                return false;
            }

            long xrMidpoint = before + bracket / 2L;
            long unixNanoseconds = checked((utc.Ticks - DateTime.UnixEpoch.Ticks) * 100L);
            _unixMinusXrNanoseconds = unixNanoseconds - xrMidpoint;
            // DateTime.UtcNow resolution and call bracketing are both represented.
            _anchorUncertaintyNanoseconds = Math.Max(100_000L, bracket / 2L + 100L);
            _valid = true;
            return true;
        }

        internal bool TryMapXrTimestamp(long xrNanoseconds, out RigTimestamp timestamp)
        {
            if (!_valid || xrNanoseconds <= 0L)
            {
                timestamp = default;
                return false;
            }

            long unixNanoseconds;
            try
            {
                unixNanoseconds = checked(xrNanoseconds + _unixMinusXrNanoseconds);
            }
            catch (OverflowException)
            {
                timestamp = default;
                return false;
            }

            timestamp = new RigTimestamp(RigClockDomain.XrMonotonic, xrNanoseconds,
                unixNanoseconds, _anchorUncertaintyNanoseconds);
            return timestamp.IsValid;
        }

        private static long SecondsToNanoseconds(double seconds) =>
            checked((long)Math.Round(seconds * 1_000_000_000.0));
    }
}
