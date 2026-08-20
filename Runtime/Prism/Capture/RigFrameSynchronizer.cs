using System;
using System.Collections.Generic;

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
            GpuImageView right, uint calibrationEpoch)
        {
            Lease = lease ?? throw new ArgumentNullException(nameof(lease));
            Left = left;
            Right = right;
            CalibrationEpoch = calibrationEpoch;
        }

        internal GpuTextureLease Lease { get; private set; }
        internal GpuImageView Left { get; }
        internal GpuImageView Right { get; }
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
        private long _lastLeftNs;
        private long _lastRightNs;
        private long _lastDepthNs;
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
            ref long lastTimestamp = ref sample.View.Eye == RigEye.Left
                ? ref _lastLeftNs
                : ref _lastRightNs;
            if (!ValidateIncoming(sample.View, sample.CalibrationEpoch, ref lastTimestamp,
                    out RigFrameRejectionReason rejection))
            {
                Reject(sample, rejection);
                return false;
            }
            queue.Add(sample);
            Trim(queue);
            return true;
        }

        internal bool AddDepth(StereoDepthRigSample sample)
        {
            if (sample == null)
                return false;
            if (!sample.Left.IsValid || !sample.Right.IsValid ||
                sample.Left.Timestamp != sample.Right.Timestamp)
            {
                Reject(sample, RigFrameRejectionReason.MissingEye |
                               RigFrameRejectionReason.MissingPose);
                return false;
            }
            if (!ValidateIncoming(sample.Left, sample.CalibrationEpoch, ref _lastDepthNs,
                    out RigFrameRejectionReason rejection))
            {
                Reject(sample, rejection);
                return false;
            }
            _depth.Add(sample);
            Trim(_depth);
            return true;
        }

        internal bool TryDequeue(out StereoRigFrameLease frame)
        {
            frame = null;
            while (_left.Count > 0 && _right.Count > 0)
            {
                RgbRigSample left = _left[0];
                RgbRigSample right = _right[0];
                if (left.CalibrationEpoch != right.CalibrationEpoch)
                {
                    DropOlderCalibration(left, right);
                    continue;
                }

                long rgbDelta = left.View.Timestamp.AbsoluteDeltaNanoseconds(
                    right.View.Timestamp);
                if (rgbDelta > _maxRgbDeltaNs)
                {
                    if (left.View.Timestamp.UnixNanoseconds < right.View.Timestamp.UnixNanoseconds)
                        RejectAndRemove(_left, 0, RigFrameRejectionReason.RgbPairDeltaExceeded);
                    else
                        RejectAndRemove(_right, 0, RigFrameRejectionReason.RgbPairDeltaExceeded);
                    continue;
                }

                long rgbMidpoint = Midpoint(left.View.Timestamp.UnixNanoseconds,
                    right.View.Timestamp.UnixNanoseconds);
                int depthIndex = FindNearestDepth(rgbMidpoint, left.CalibrationEpoch,
                    out long rgbDepthDelta);
                if (depthIndex < 0)
                {
                    DropProvablyStaleRgbOrDepth(rgbMidpoint);
                    return false;
                }

                StereoDepthRigSample depth = _depth[depthIndex];
                _left.RemoveAt(0);
                _right.RemoveAt(0);
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
                    depth.Left, depth.Right, health);
                _accepted++;
                _lastRgbDelta = rgbDelta;
                _lastRgbDepthDelta = rgbDepthDelta;
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
            ref long lastTimestamp, out RigFrameRejectionReason rejection)
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
            if (view.Timestamp.UnixNanoseconds <= lastTimestamp)
                rejection |= RigFrameRejectionReason.OutOfOrder;
            if (rejection != RigFrameRejectionReason.None)
                return false;
            lastTimestamp = view.Timestamp.UnixNanoseconds;
            return true;
        }

        private int FindNearestDepth(long rgbMidpoint, uint calibrationEpoch,
            out long bestDelta)
        {
            int best = -1;
            bestDelta = long.MaxValue;
            for (int i = 0; i < _depth.Count; i++)
            {
                StereoDepthRigSample candidate = _depth[i];
                if (candidate.CalibrationEpoch != calibrationEpoch)
                    continue;
                long delta = AbsoluteDelta(candidate.Left.Timestamp.UnixNanoseconds,
                    rgbMidpoint);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = i;
                }
            }
            if (bestDelta > _maxRgbDepthDeltaNs)
                return -1;
            return best;
        }

        private void DropProvablyStaleRgbOrDepth(long rgbMidpoint)
        {
            while (_depth.Count > 0 &&
                   _depth[0].Left.Timestamp.UnixNanoseconds < rgbMidpoint - _maxRgbDepthDeltaNs)
            {
                RejectAndRemove(_depth, 0, RigFrameRejectionReason.RgbDepthDeltaExceeded);
            }

            if (_depth.Count > 0 &&
                _depth[0].Left.Timestamp.UnixNanoseconds > rgbMidpoint + _maxRgbDepthDeltaNs)
            {
                RejectAndRemove(_left, 0, RigFrameRejectionReason.RgbDepthDeltaExceeded);
                RejectAndRemove(_right, 0, RigFrameRejectionReason.RgbDepthDeltaExceeded);
            }
        }

        private void DropOlderCalibration(RgbRigSample left, RgbRigSample right)
        {
            if (left.CalibrationEpoch < right.CalibrationEpoch)
                RejectAndRemove(_left, 0, RigFrameRejectionReason.CalibrationMismatch);
            else
                RejectAndRemove(_right, 0, RigFrameRejectionReason.CalibrationMismatch);
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
