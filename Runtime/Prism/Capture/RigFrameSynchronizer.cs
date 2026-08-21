using System;
using System.Collections.Generic;
using UnityEngine;

namespace Genesis.RoomScan.Prism
{
    internal sealed class RgbRigSample : IDisposable
    {
        internal RgbRigSample(GpuTextureLease lease, GpuImageView view, uint calibrationEpoch)
        {
            Lease = lease ?? throw new ArgumentNullException(nameof(lease));
            View = view;
            CalibrationEpoch = calibrationEpoch;
        }

        internal GpuTextureLease Lease { get; private set; }
        internal GpuImageView View { get; }
        internal uint CalibrationEpoch { get; }

        internal GpuTextureLease TakeLease()
        {
            GpuTextureLease result = Lease;
            Lease = null;
            return result;
        }

        public void Dispose()
        {
            Lease?.Dispose();
            Lease = null;
        }
    }

    internal sealed class StereoDepthRigSample : IDisposable
    {
        internal StereoDepthRigSample(GpuTextureLease lease, GpuImageView left,
            GpuImageView right, uint calibrationEpoch, Vector2Int resolution,
            Vector2 nearFar)
        {
            Lease = lease ?? throw new ArgumentNullException(nameof(lease));
            Left = left;
            Right = right;
            CalibrationEpoch = calibrationEpoch;
            Resolution = resolution;
            NearFar = nearFar;
        }

        internal GpuTextureLease Lease { get; private set; }
        internal GpuImageView Left { get; }
        internal GpuImageView Right { get; }
        internal uint CalibrationEpoch { get; }
        internal Vector2Int Resolution { get; }
        internal Vector2 NearFar { get; }
        internal bool HasValidStereoContract =>
            RigDepthContract.IsValid(Resolution, NearFar) &&
            RigDepthContract.ViewMatches(Left, RigEye.Left, Lease?.Texture, 0,
                Resolution, NearFar) &&
            RigDepthContract.ViewMatches(Right, RigEye.Right, Lease?.Texture, 1,
                Resolution, NearFar) &&
            Left.SourceSequence == Right.SourceSequence && Left.Timestamp == Right.Timestamp;

        internal GpuTextureLease TakeLease()
        {
            GpuTextureLease result = Lease;
            Lease = null;
            return result;
        }

        public void Dispose()
        {
            Lease?.Dispose();
            Lease = null;
        }
    }

    public readonly struct RigCaptureDiagnosticSnapshot
    {
        internal RigCaptureDiagnosticSnapshot(long accepted, long rejected,
            RigFrameRejectionReason lastRejection, long lastRgbDeltaNs,
            long lastRgbDepthDeltaNs)
        {
            AcceptedFrames = accepted;
            RejectedSamples = rejected;
            LastRejection = lastRejection;
            LastRgbDeltaNanoseconds = lastRgbDeltaNs;
            LastRgbDepthDeltaNanoseconds = lastRgbDepthDeltaNs;
        }

        public long AcceptedFrames { get; }
        public long RejectedSamples { get; }
        public RigFrameRejectionReason LastRejection { get; }
        public long LastRgbDeltaNanoseconds { get; }
        public long LastRgbDepthDeltaNanoseconds { get; }
    }

    /// <summary>
    /// Small metadata-only nearest-timestamp synchronizer. GPU images remain in leased
    /// ring slots; rejected/stale samples release their slot without a readback.
    /// </summary>
    internal sealed class RigFrameSynchronizer : IDisposable
    {
        private readonly List<RgbRigSample> _left = new();
        private readonly List<RgbRigSample> _right = new();
        private readonly List<StereoDepthRigSample> _depth = new();
        private readonly long _maxRgbDeltaNs;
        private readonly long _maxRgbDepthDeltaNs;
        private readonly long _maxClockUncertaintyNs;
        private readonly int _maxQueue;
        private long _maximumSeenLeftNs;
        private long _maximumSeenRightNs;
        private long _maximumSeenDepthNs;
        private long _lastPublishedDepthNs;
        private long _sequence;
        private long _accepted;
        private long _rejected;
        private long _lastRgbDelta;
        private long _lastRgbDepthDelta;
        private RigFrameRejectionReason _lastRejection;

        internal RigFrameSynchronizer(float maxRgbDeltaMilliseconds,
            float maxRgbDepthDeltaMilliseconds, float maxClockUncertaintyMilliseconds,
            int maxQueue = 8)
        {
            if (maxRgbDeltaMilliseconds <= 0f || maxRgbDepthDeltaMilliseconds <= 0f ||
                maxClockUncertaintyMilliseconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maxRgbDeltaMilliseconds));
            _maxRgbDeltaNs = MillisecondsToNanoseconds(maxRgbDeltaMilliseconds);
            _maxRgbDepthDeltaNs = MillisecondsToNanoseconds(maxRgbDepthDeltaMilliseconds);
            _maxClockUncertaintyNs = MillisecondsToNanoseconds(maxClockUncertaintyMilliseconds);
            _maxQueue = Math.Max(3, maxQueue);
        }

        internal RigCaptureDiagnosticSnapshot Diagnostics => new(_accepted, _rejected,
            _lastRejection, _lastRgbDelta, _lastRgbDepthDelta);

        internal bool AddRgb(RgbRigSample sample)
        {
            if (sample == null)
                return false;
            List<RgbRigSample> queue = sample.View.Eye == RigEye.Left ? _left : _right;
            ref long maximumSeen = ref sample.View.Eye == RigEye.Left
                ? ref _maximumSeenLeftNs
                : ref _maximumSeenRightNs;
            if (!ValidateIncoming(sample.View, sample.CalibrationEpoch,
                    out RigFrameRejectionReason rejection) ||
                !AcceptReorderedTimestamp(sample.View.Timestamp.UnixNanoseconds,
                    ref maximumSeen, out rejection) ||
                ContainsTimestamp(queue, sample.View.Timestamp.UnixNanoseconds))
            {
                if (rejection == RigFrameRejectionReason.None)
                    rejection = RigFrameRejectionReason.OutOfOrder;
                Reject(sample, rejection);
                return false;
            }
            InsertSorted(queue, sample);
            Trim(queue);
            return true;
        }

        internal bool AddDepth(StereoDepthRigSample sample)
        {
            if (sample == null)
                return false;
            if (!sample.HasValidStereoContract)
            {
                Reject(sample, RigFrameRejectionReason.StereoDepthContractMismatch);
                return false;
            }
            if (!ValidateIncoming(sample.Left, sample.CalibrationEpoch,
                    out RigFrameRejectionReason rejection) ||
                !AcceptReorderedTimestamp(sample.Left.Timestamp.UnixNanoseconds,
                    ref _maximumSeenDepthNs, out rejection) ||
                ContainsTimestamp(_depth, sample.Left.Timestamp.UnixNanoseconds))
            {
                if (rejection == RigFrameRejectionReason.None)
                    rejection = RigFrameRejectionReason.OutOfOrder;
                Reject(sample, rejection);
                return false;
            }
            InsertSorted(_depth, sample);
            Trim(_depth);
            return true;
        }

        internal bool TryDequeue(out StereoRigFrameLease frame)
        {
            frame = null;
            while (_left.Count > 0 && _right.Count > 0 && _depth.Count > 0)
            {
                if (!FindEarliestCoherentTriplet(out int leftIndex,
                        out int rightIndex, out int depthIndex,
                        out long rgbDelta, out long rgbDepthDelta))
                {
                    DropPastPublishedSamples();
                    return false;
                }

                RgbRigSample left = _left[leftIndex];
                RgbRigSample right = _right[rightIndex];
                StereoDepthRigSample depth = _depth[depthIndex];
                _left.RemoveAt(leftIndex);
                _right.RemoveAt(rightIndex);
                _depth.RemoveAt(depthIndex);

                GpuTextureLease leftLease = left.TakeLease();
                GpuTextureLease rightLease = right.TakeLease();
                GpuTextureLease depthLease = depth.TakeLease();
                left.Dispose();
                right.Dispose();
                depth.Dispose();

                long clockUncertainty = Math.Max(depth.Left.Timestamp.MappingUncertaintyNanoseconds,
                    Math.Max(left.View.Timestamp.MappingUncertaintyNanoseconds,
                        right.View.Timestamp.MappingUncertaintyNanoseconds));
                var health = new RigPairingHealth(rgbDelta, rgbDepthDelta, clockUncertainty);
                frame = new StereoRigFrameLease(++_sequence, left.CalibrationEpoch,
                    leftLease, left.View, rightLease, right.View, depthLease,
                    depth.Left, depth.Right, depth.Resolution, depth.NearFar, health);
                _accepted++;
                _lastPublishedDepthNs = depth.Left.Timestamp.UnixNanoseconds;
                _lastRgbDelta = rgbDelta;
                _lastRgbDepthDelta = rgbDepthDelta;
                DropPastPublishedSamples();
                return true;
            }
            return false;
        }

        internal void Flush(RigFrameRejectionReason reason)
        {
            RejectAll(_left, reason);
            RejectAll(_right, reason);
            RejectAll(_depth, reason);
        }

        public void Dispose() => Flush(RigFrameRejectionReason.Stale);

        private bool ValidateIncoming(GpuImageView view, uint calibrationEpoch,
            out RigFrameRejectionReason rejection)
        {
            rejection = RigFrameRejectionReason.None;
            if (!view.IsValid)
                rejection |= RigFrameRejectionReason.MissingTexture |
                             RigFrameRejectionReason.MissingTimestamp |
                             RigFrameRejectionReason.InvalidIntrinsics;
            if (calibrationEpoch == 0u)
                rejection |= RigFrameRejectionReason.CalibrationMismatch;
            if (view.Timestamp.MappingUncertaintyNanoseconds > _maxClockUncertaintyNs)
                rejection |= RigFrameRejectionReason.ClockMappingUncertain;
            return rejection == RigFrameRejectionReason.None;
        }

        private bool FindEarliestCoherentTriplet(out int bestLeft,
            out int bestRight, out int bestDepth, out long bestRgbDelta,
            out long bestRgbDepthDelta)
        {
            bestLeft = bestRight = bestDepth = -1;
            bestRgbDelta = bestRgbDepthDelta = long.MaxValue;
            for (int depthIndex = 0; depthIndex < _depth.Count; depthIndex++)
            {
                StereoDepthRigSample depth = _depth[depthIndex];
                long depthTimestamp = depth.Left.Timestamp.UnixNanoseconds;
                if (depthTimestamp <= _lastPublishedDepthNs)
                    continue;
                long bestScore = long.MaxValue;
                int candidateLeft = -1;
                int candidateRight = -1;
                long candidateRgbDelta = long.MaxValue;
                long candidateDepthDelta = long.MaxValue;
                for (int leftIndex = 0; leftIndex < _left.Count; leftIndex++)
                {
                    RgbRigSample left = _left[leftIndex];
                    if (left.CalibrationEpoch != depth.CalibrationEpoch) continue;
                    for (int rightIndex = 0; rightIndex < _right.Count; rightIndex++)
                    {
                        RgbRigSample right = _right[rightIndex];
                        if (right.CalibrationEpoch != depth.CalibrationEpoch) continue;
                        long rgbDelta = left.View.Timestamp.AbsoluteDeltaNanoseconds(
                            right.View.Timestamp);
                        if (rgbDelta > _maxRgbDeltaNs) continue;
                        long midpoint = Midpoint(left.View.Timestamp.UnixNanoseconds,
                            right.View.Timestamp.UnixNanoseconds);
                        long depthDelta = AbsoluteDelta(midpoint, depthTimestamp);
                        if (depthDelta > _maxRgbDepthDeltaNs) continue;
                        long score = depthDelta * 2L + rgbDelta;
                        if (score >= bestScore) continue;
                        bestScore = score;
                        candidateLeft = leftIndex;
                        candidateRight = rightIndex;
                        candidateRgbDelta = rgbDelta;
                        candidateDepthDelta = depthDelta;
                    }
                }
                // Depth is the metric cadence. Select the earliest depth sample
                // that already has a coherent pair, then the closest RGB pair for
                // that sample. This preserves time order without assuming callback
                // arrival order.
                if (candidateLeft < 0) continue;
                bestLeft = candidateLeft;
                bestRight = candidateRight;
                bestDepth = depthIndex;
                bestRgbDelta = candidateRgbDelta;
                bestRgbDepthDelta = candidateDepthDelta;
                return true;
            }
            return false;
        }

        private bool AcceptReorderedTimestamp(long timestamp,
            ref long maximumSeen, out RigFrameRejectionReason rejection)
        {
            rejection = RigFrameRejectionReason.None;
            long reorderHorizon = 2L * Math.Max(_maxRgbDepthDeltaNs,
                _maxRgbDeltaNs);
            if (maximumSeen > 0L && timestamp < maximumSeen - reorderHorizon)
            {
                rejection = RigFrameRejectionReason.OutOfOrder |
                            RigFrameRejectionReason.Stale;
                return false;
            }
            maximumSeen = Math.Max(maximumSeen, timestamp);
            return true;
        }

        private void DropPastPublishedSamples()
        {
            if (_lastPublishedDepthNs <= 0L) return;
            while (_depth.Count > 0 &&
                   _depth[0].Left.Timestamp.UnixNanoseconds <= _lastPublishedDepthNs)
                RejectAndRemove(_depth, 0, RigFrameRejectionReason.Stale);
            long rgbFloor = _lastPublishedDepthNs - _maxRgbDepthDeltaNs -
                _maxRgbDeltaNs / 2L;
            while (_left.Count > 0 &&
                   _left[0].View.Timestamp.UnixNanoseconds < rgbFloor)
                RejectAndRemove(_left, 0, RigFrameRejectionReason.Stale);
            while (_right.Count > 0 &&
                   _right[0].View.Timestamp.UnixNanoseconds < rgbFloor)
                RejectAndRemove(_right, 0, RigFrameRejectionReason.Stale);
        }

        private static bool ContainsTimestamp(List<RgbRigSample> queue,
            long timestamp) => queue.Exists(sample =>
                sample.View.Timestamp.UnixNanoseconds == timestamp);

        private static bool ContainsTimestamp(List<StereoDepthRigSample> queue,
            long timestamp) => queue.Exists(sample =>
                sample.Left.Timestamp.UnixNanoseconds == timestamp);

        private static void InsertSorted(List<RgbRigSample> queue,
            RgbRigSample sample)
        {
            long timestamp = sample.View.Timestamp.UnixNanoseconds;
            int index = queue.BinarySearch(sample,
                RgbTimestampComparer.Instance);
            if (index < 0) index = ~index;
            queue.Insert(index, sample);
        }

        private static void InsertSorted(List<StereoDepthRigSample> queue,
            StereoDepthRigSample sample)
        {
            int index = queue.BinarySearch(sample,
                DepthTimestampComparer.Instance);
            if (index < 0) index = ~index;
            queue.Insert(index, sample);
        }

        private sealed class RgbTimestampComparer : IComparer<RgbRigSample>
        {
            internal static readonly RgbTimestampComparer Instance = new();
            public int Compare(RgbRigSample x, RgbRigSample y) =>
                (x == null ? long.MinValue :
                    x.View.Timestamp.UnixNanoseconds).CompareTo(
                    y == null ? long.MinValue :
                    y.View.Timestamp.UnixNanoseconds);
        }

        private sealed class DepthTimestampComparer :
            IComparer<StereoDepthRigSample>
        {
            internal static readonly DepthTimestampComparer Instance = new();
            public int Compare(StereoDepthRigSample x, StereoDepthRigSample y) =>
                (x == null ? long.MinValue :
                    x.Left.Timestamp.UnixNanoseconds).CompareTo(
                    y == null ? long.MinValue :
                    y.Left.Timestamp.UnixNanoseconds);
        }

        private void Trim<T>(List<T> queue) where T : IDisposable
        {
            while (queue.Count > _maxQueue)
                RejectAndRemove(queue, 0, RigFrameRejectionReason.QueueOverflow);
        }

        private void RejectAll<T>(List<T> queue, RigFrameRejectionReason reason)
            where T : IDisposable
        {
            while (queue.Count > 0)
                RejectAndRemove(queue, queue.Count - 1, reason);
        }

        private void RejectAndRemove<T>(List<T> queue, int index,
            RigFrameRejectionReason reason) where T : IDisposable
        {
            T item = queue[index];
            queue.RemoveAt(index);
            Reject(item, reason);
        }

        private void Reject(IDisposable sample, RigFrameRejectionReason reason)
        {
            sample.Dispose();
            _rejected++;
            _lastRejection = reason;
        }

        private static long MillisecondsToNanoseconds(float milliseconds) =>
            (long)Math.Round(milliseconds * 1_000_000.0);

        private static long Midpoint(long a, long b) => a + (b - a) / 2L;

        private static long AbsoluteDelta(long a, long b)
        {
            long delta = a - b;
            return delta == long.MinValue ? long.MaxValue : Math.Abs(delta);
        }
    }
}
