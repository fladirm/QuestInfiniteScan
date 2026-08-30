using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    public enum RigEye : byte
    {
        Left = 0,
        Right = 1
    }

    public enum RigStreamKind : byte
    {
        Rgb = 0,
        Depth = 1
    }

    /// <summary>Encoding of a depth image before Sigma-PRISM-16 metric normalization.</summary>
    public enum RigDepthEncoding : byte
    {
        NotDepth = 0,
        /// <summary>
        /// Meta Environment Depth's projection depth in [0,1]. It represents camera
        /// view-Z through the frame's near/far planes, not Euclidean ray range.
        /// </summary>
        ProjectionDepth01 = 1
    }

    public enum RigClockDomain : byte
    {
        Invalid = 0,
        UnixRealtime = 1,
        XrMonotonic = 2,
        UnityMonotonicSimulation = 3
    }

    [Flags]
    public enum RigFrameRejectionReason : uint
    {
        None = 0,
        MissingTexture = 1u << 0,
        MissingTimestamp = 1u << 1,
        MissingPose = 1u << 2,
        InvalidIntrinsics = 1u << 3,
        UnsupportedTexture = 1u << 4,
        Stale = 1u << 5,
        OutOfOrder = 1u << 6,
        MissingEye = 1u << 7,
        RgbPairDeltaExceeded = 1u << 8,
        RgbDepthDeltaExceeded = 1u << 9,
        CalibrationMismatch = 1u << 10,
        ClockMappingUncertain = 1u << 11,
        RingExhausted = 1u << 12,
        GpuCopyFailed = 1u << 13,
        QueueOverflow = 1u << 14,
        StereoDepthContractMismatch = 1u << 15
    }

    internal enum RigLatestSnapshotMatch : byte
    {
        Waiting = 0,
        Ready = 1,
        DiscardDepth = 2
    }

    internal readonly struct RigLatestSnapshotMatchResult
    {
        internal RigLatestSnapshotMatchResult(RigLatestSnapshotMatch disposition,
            RigFrameRejectionReason rejection, long rgbDeltaNanoseconds,
            long rgbDepthDeltaNanoseconds, long clockUncertaintyNanoseconds)
        {
            Disposition = disposition;
            Rejection = rejection;
            RgbDeltaNanoseconds = rgbDeltaNanoseconds;
            RgbDepthDeltaNanoseconds = rgbDepthDeltaNanoseconds;
            ClockUncertaintyNanoseconds = clockUncertaintyNanoseconds;
        }

        internal RigLatestSnapshotMatch Disposition { get; }
        internal RigFrameRejectionReason Rejection { get; }
        internal long RgbDeltaNanoseconds { get; }
        internal long RgbDepthDeltaNanoseconds { get; }
        internal long ClockUncertaintyNanoseconds { get; }
    }

    /// <summary>
    /// Lossless pre-admission match for the latest two PCA descriptors and one owned
    /// depth snapshot. It only decides capture eligibility; the four sensor leaves
    /// remain separate inputs to the generated query program.
    /// </summary>
    internal static class RigLatestSnapshotMatcher
    {
        internal static RigLatestSnapshotMatchResult Match(
            RigTimestamp left, RigTimestamp right, RigTimestamp depth,
            long maximumRgbDeltaNanoseconds,
            long maximumRgbDepthDeltaNanoseconds,
            long maximumClockUncertaintyNanoseconds)
        {
            if (!left.IsValid || !right.IsValid || !depth.IsValid)
                return new RigLatestSnapshotMatchResult(
                    RigLatestSnapshotMatch.Waiting,
                    RigFrameRejectionReason.MissingTimestamp,
                    long.MaxValue, long.MaxValue, long.MaxValue);

            long uncertainty = Math.Max(depth.MappingUncertaintyNanoseconds,
                Math.Max(left.MappingUncertaintyNanoseconds,
                    right.MappingUncertaintyNanoseconds));
            if (uncertainty > maximumClockUncertaintyNanoseconds)
                return new RigLatestSnapshotMatchResult(
                    RigLatestSnapshotMatch.DiscardDepth,
                    RigFrameRejectionReason.ClockMappingUncertain,
                    long.MaxValue, long.MaxValue, uncertainty);

            long rgbDelta = left.AbsoluteDeltaNanoseconds(right);
            long midpoint = left.UnixNanoseconds +
                (right.UnixNanoseconds - left.UnixNanoseconds) / 2L;
            long depthDelta = AbsoluteDelta(midpoint, depth.UnixNanoseconds);
            RigFrameRejectionReason mismatch = RigFrameRejectionReason.None;
            if (rgbDelta > maximumRgbDeltaNanoseconds)
                mismatch |= RigFrameRejectionReason.RgbPairDeltaExceeded;
            if (depthDelta > maximumRgbDepthDeltaNanoseconds)
                mismatch |= RigFrameRejectionReason.RgbDepthDeltaExceeded;
            if (mismatch == RigFrameRejectionReason.None)
                return new RigLatestSnapshotMatchResult(
                    RigLatestSnapshotMatch.Ready,
                    RigFrameRejectionReason.None, rgbDelta, depthDelta,
                    uncertainty);

            // PCA exposes only its latest mutable images. Once either eye has moved
            // beyond every timestamp that could satisfy both exact windows, this
            // owned depth snapshot can never form a coherent future triplet.
            long latestRgb = Math.Max(left.UnixNanoseconds,
                right.UnixNanoseconds);
            long expiryMargin = SaturatingAdd(maximumRgbDepthDeltaNanoseconds,
                maximumRgbDeltaNanoseconds / 2L);
            long expiry = SaturatingAdd(depth.UnixNanoseconds, expiryMargin);
            return new RigLatestSnapshotMatchResult(
                latestRgb > expiry
                    ? RigLatestSnapshotMatch.DiscardDepth
                    : RigLatestSnapshotMatch.Waiting,
                latestRgb > expiry
                    ? mismatch | RigFrameRejectionReason.Stale
                    : mismatch,
                rgbDelta, depthDelta, uncertainty);
        }

        private static long AbsoluteDelta(long first, long second)
        {
            long delta = first - second;
            return delta == long.MinValue ? long.MaxValue : Math.Abs(delta);
        }

        private static long SaturatingAdd(long first, long second)
        {
            if (second > 0L && first > long.MaxValue - second)
                return long.MaxValue;
            if (second < 0L && first < long.MinValue - second)
                return long.MinValue;
            return first + second;
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
    /// A source timestamp plus its normalized Unix-realtime estimate. The source value is
    /// never overwritten: diagnostics can distinguish native PCA realtime from depth XrTime.
    /// </summary>
    public readonly struct RigTimestamp : IEquatable<RigTimestamp>
    {
        public RigTimestamp(
            RigClockDomain sourceDomain,
            long sourceNanoseconds,
            long unixNanoseconds,
            long mappingUncertaintyNanoseconds)
        {
            SourceDomain = sourceDomain;
            SourceNanoseconds = sourceNanoseconds;
            UnixNanoseconds = unixNanoseconds;
            MappingUncertaintyNanoseconds = Math.Max(0L, mappingUncertaintyNanoseconds);
        }

        public RigClockDomain SourceDomain { get; }
        public long SourceNanoseconds { get; }
        public long UnixNanoseconds { get; }
        public long MappingUncertaintyNanoseconds { get; }
        public bool IsValid => SourceDomain != RigClockDomain.Invalid &&
                               SourceNanoseconds > 0L && UnixNanoseconds > 0L;

        public static RigTimestamp FromUnixDateTime(DateTime timestamp)
        {
            long unixNanoseconds = checked((timestamp.Ticks - DateTime.UnixEpoch.Ticks) * 100L);
            return new RigTimestamp(RigClockDomain.UnixRealtime, unixNanoseconds,
                unixNanoseconds, 0L);
        }

        public long AbsoluteDeltaNanoseconds(RigTimestamp other)
        {
            long delta = UnixNanoseconds - other.UnixNanoseconds;
            if (delta == long.MinValue)
                return long.MaxValue;
            return Math.Abs(delta);
        }

        public bool Equals(RigTimestamp other) =>
            SourceDomain == other.SourceDomain &&
            SourceNanoseconds == other.SourceNanoseconds &&
            UnixNanoseconds == other.UnixNanoseconds &&
            MappingUncertaintyNanoseconds == other.MappingUncertaintyNanoseconds;

        public override bool Equals(object obj) => obj is RigTimestamp other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)SourceDomain,
            SourceNanoseconds, UnixNanoseconds, MappingUncertaintyNanoseconds);
        public static bool operator ==(RigTimestamp left, RigTimestamp right) => left.Equals(right);
        public static bool operator !=(RigTimestamp left, RigTimestamp right) => !left.Equals(right);
        public override string ToString() =>
            $"{SourceDomain}:source={SourceNanoseconds},unix={UnixNanoseconds}," +
            $"uncertaintyNs={MappingUncertaintyNanoseconds}";
    }

    /// <summary>Immutable calibration carried by one stream sample.</summary>
    public readonly struct RigIntrinsics : IEquatable<RigIntrinsics>
    {
        public RigIntrinsics(
            Vector2 focalLength,
            Vector2 principalPoint,
            Vector2Int sensorResolution,
            Vector2Int imageResolution,
            Pose headFromSensor,
            Vector4 fovRadians,
            ulong signature)
        {
            FocalLength = focalLength;
            PrincipalPoint = principalPoint;
            SensorResolution = sensorResolution;
            ImageResolution = imageResolution;
            HeadFromSensor = headFromSensor;
            FovRadians = fovRadians;
            Signature = signature;
        }

        public Vector2 FocalLength { get; }
        public Vector2 PrincipalPoint { get; }
        public Vector2Int SensorResolution { get; }
        public Vector2Int ImageResolution { get; }
        public Pose HeadFromSensor { get; }
        /// <summary>Left, right, up, down OpenXR angles in radians where available.</summary>
        public Vector4 FovRadians { get; }
        public ulong Signature { get; }

        public bool IsValid => FocalLength.x > 0f && FocalLength.y > 0f &&
                               SensorResolution.x > 0 && SensorResolution.y > 0 &&
                               ImageResolution.x > 0 && ImageResolution.y > 0 &&
                               Signature != 0UL;

        public bool Equals(RigIntrinsics other) => Signature == other.Signature &&
            FocalLength == other.FocalLength && PrincipalPoint == other.PrincipalPoint &&
            SensorResolution == other.SensorResolution &&
            ImageResolution == other.ImageResolution &&
            HeadFromSensor.Equals(other.HeadFromSensor) && FovRadians == other.FovRadians;

        public override bool Equals(object obj) => obj is RigIntrinsics other && Equals(other);
        public override int GetHashCode() => Signature.GetHashCode();
    }

    /// <summary>
    /// Metadata for one eye view. The texture remains valid only while its owning
    /// <see cref="StereoRigFrameLease"/> is alive.
    /// </summary>
    public readonly struct GpuImageView
    {
        internal GpuImageView(
            RigStreamKind kind,
            RigEye eye,
            Texture texture,
            int arraySlice,
            long sourceSequence,
            RigTimestamp timestamp,
            Pose worldFromCamera,
            RigIntrinsics intrinsics,
            GraphicsFormat graphicsFormat,
            RigDepthEncoding depthEncoding = RigDepthEncoding.NotDepth,
            Vector2 depthNearFar = default)
        {
            Kind = kind;
            Eye = eye;
            Texture = texture;
            ArraySlice = arraySlice;
            SourceSequence = sourceSequence;
            Timestamp = timestamp;
            WorldFromCamera = worldFromCamera;
            Intrinsics = intrinsics;
            GraphicsFormat = graphicsFormat;
            DepthEncoding = depthEncoding;
            DepthNearFar = depthNearFar;
        }

        public RigStreamKind Kind { get; }
        public RigEye Eye { get; }
        public Texture Texture { get; }
        public int ArraySlice { get; }
        public long SourceSequence { get; }
        public RigTimestamp Timestamp { get; }
        public Pose WorldFromCamera { get; }
        public RigIntrinsics Intrinsics { get; }
        public GraphicsFormat GraphicsFormat { get; }
        public RigDepthEncoding DepthEncoding { get; }
        /// <summary>Camera view-Z clip interval in metres for a raw depth view.</summary>
        public Vector2 DepthNearFar { get; }
        public Vector2Int Resolution => Intrinsics.ImageResolution;
        public bool IsValid => Texture != null && Timestamp.IsValid && Intrinsics.IsValid &&
                               IsFinite(WorldFromCamera.position) && IsFinite(WorldFromCamera.rotation) &&
                               (Kind != RigStreamKind.Depth ||
                                (DepthEncoding != RigDepthEncoding.NotDepth &&
                                 RigDepthContract.IsValidRange(DepthNearFar)));

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool IsFinite(Quaternion value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) &&
            float.IsFinite(value.z) && float.IsFinite(value.w) &&
            value.x * value.x + value.y * value.y + value.z * value.z +
            value.w * value.w > 0.5f;
    }

    /// <summary>Immutable contract shared by both slices of one native depth frame.</summary>
    internal static class RigDepthContract
    {
        internal static bool IsValid(Vector2Int resolution, Vector2 nearFar) =>
            resolution.x > 0 && resolution.y > 0 && IsValidRange(nearFar);

        internal static bool IsValidRange(Vector2 nearFar) =>
            float.IsFinite(nearFar.x) && nearFar.x > 0f &&
            ((float.IsFinite(nearFar.y) && nearFar.y > nearFar.x) ||
             float.IsPositiveInfinity(nearFar.y));

        internal static bool EquivalentRange(Vector2 first, Vector2 second) =>
            IsValidRange(first) && IsValidRange(second) && first.x == second.x &&
            (first.y == second.y ||
             (float.IsPositiveInfinity(first.y) &&
              float.IsPositiveInfinity(second.y)));

        internal static bool ViewMatches(GpuImageView view, RigEye eye,
            Texture sharedTexture, int slice, Vector2Int resolution, Vector2 nearFar) =>
            view.IsValid && view.Kind == RigStreamKind.Depth && view.Eye == eye &&
            ReferenceEquals(view.Texture, sharedTexture) && view.ArraySlice == slice &&
            view.Resolution == resolution &&
            view.DepthEncoding == RigDepthEncoding.ProjectionDepth01 &&
            EquivalentRange(view.DepthNearFar, nearFar);

        internal static float FiniteRasterFar(Vector2 nearFar) =>
            float.IsFinite(nearFar.y) ? nearFar.y : Mathf.Max(1000f, nearFar.x + 1f);
    }

    public readonly struct RigExtrinsicsSnapshot
    {
        internal RigExtrinsicsSnapshot(
            Pose leftRgbFromRightRgb,
            Pose leftDepthFromRightDepth,
            Pose leftRgbFromLeftDepth,
            Pose rightRgbFromRightDepth)
        {
            LeftRgbFromRightRgb = leftRgbFromRightRgb;
            LeftDepthFromRightDepth = leftDepthFromRightDepth;
            LeftRgbFromLeftDepth = leftRgbFromLeftDepth;
            RightRgbFromRightDepth = rightRgbFromRightDepth;
        }

        public Pose LeftRgbFromRightRgb { get; }
        public Pose LeftDepthFromRightDepth { get; }
        public Pose LeftRgbFromLeftDepth { get; }
        public Pose RightRgbFromRightDepth { get; }

        internal static RigExtrinsicsSnapshot FromViews(
            GpuImageView rgbLeft,
            GpuImageView rgbRight,
            GpuImageView depthLeft,
            GpuImageView depthRight) => new(
                RigPoseMath.DestinationFromSource(rgbLeft.WorldFromCamera,
                    rgbRight.WorldFromCamera),
                RigPoseMath.DestinationFromSource(depthLeft.WorldFromCamera,
                    depthRight.WorldFromCamera),
                RigPoseMath.DestinationFromSource(rgbLeft.WorldFromCamera,
                    depthLeft.WorldFromCamera),
                RigPoseMath.DestinationFromSource(rgbRight.WorldFromCamera,
                    depthRight.WorldFromCamera));
    }

    public readonly struct RigPairingHealth
    {
        internal RigPairingHealth(long rgbDeltaNs, long rgbDepthDeltaNs,
            long clockUncertaintyNs)
        {
            RgbDeltaNanoseconds = rgbDeltaNs;
            RgbDepthDeltaNanoseconds = rgbDepthDeltaNs;
            ClockUncertaintyNanoseconds = clockUncertaintyNs;
        }

        public long RgbDeltaNanoseconds { get; }
        public long RgbDepthDeltaNanoseconds { get; }
        public long ClockUncertaintyNanoseconds { get; }
    }

    /// <summary>
    /// Immutable coherent four-view frame. Retain before storing beyond the callback that
    /// supplied it; disposal releases its preallocated GPU-ring slots for reuse.
    /// </summary>
    public sealed class StereoRigFrameLease : IDisposable
    {
        private GpuTextureLease _rgbLeftOwner;
        private GpuTextureLease _rgbRightOwner;
        private GpuTextureLease _depthOwner;
        private bool _disposed;

        internal StereoRigFrameLease(
            long sequence,
            uint calibrationEpoch,
            GpuTextureLease rgbLeftOwner,
            GpuImageView rgbLeft,
            GpuTextureLease rgbRightOwner,
            GpuImageView rgbRight,
            GpuTextureLease depthOwner,
            GpuImageView depthLeft,
            GpuImageView depthRight,
            Vector2Int depthResolution,
            Vector2 depthNearFar,
            RigPairingHealth health)
        {
            Sequence = sequence;
            CalibrationEpoch = calibrationEpoch;
            _rgbLeftOwner = rgbLeftOwner ?? throw new ArgumentNullException(nameof(rgbLeftOwner));
            _rgbRightOwner = rgbRightOwner ?? throw new ArgumentNullException(nameof(rgbRightOwner));
            _depthOwner = depthOwner ?? throw new ArgumentNullException(nameof(depthOwner));
            RgbLeft = rgbLeft;
            RgbRight = rgbRight;
            DepthLeft = depthLeft;
            DepthRight = depthRight;
            DepthResolution = depthResolution;
            DepthNearFar = depthNearFar;
            Health = health;
            Extrinsics = RigExtrinsicsSnapshot.FromViews(rgbLeft, rgbRight, depthLeft,
                depthRight);
        }

        public long Sequence { get; }
        public uint CalibrationEpoch { get; }
        public GpuImageView RgbLeft { get; }
        public GpuImageView RgbRight { get; }
        public GpuImageView DepthLeft { get; }
        public GpuImageView DepthRight { get; }
        public Vector2Int DepthResolution { get; }
        public Vector2 DepthNearFar { get; }
        public RigExtrinsicsSnapshot Extrinsics { get; }
        public RigPairingHealth Health { get; }
        public bool IsDisposed => _disposed;
        public bool IsValid => !_disposed && CalibrationEpoch != 0u && RgbLeft.IsValid &&
                               RgbRight.IsValid &&
                               RigDepthContract.IsValid(DepthResolution, DepthNearFar) &&
                               RigDepthContract.ViewMatches(DepthLeft, RigEye.Left,
                                   DepthLeft.Texture, 0, DepthResolution, DepthNearFar) &&
                               RigDepthContract.ViewMatches(DepthRight, RigEye.Right,
                                   DepthLeft.Texture, 1, DepthResolution, DepthNearFar) &&
                               DepthLeft.Timestamp == DepthRight.Timestamp;

        public StereoRigFrameLease Retain()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StereoRigFrameLease));
            return new StereoRigFrameLease(Sequence, CalibrationEpoch,
                _rgbLeftOwner.Retain(), RgbLeft,
                _rgbRightOwner.Retain(), RgbRight,
                _depthOwner.Retain(), DepthLeft, DepthRight,
                DepthResolution, DepthNearFar, Health);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _rgbLeftOwner.Dispose();
            _rgbRightOwner.Dispose();
            _depthOwner.Dispose();
            _rgbLeftOwner = null;
            _rgbRightOwner = null;
            _depthOwner = null;
        }
    }

    internal static class RigPoseMath
    {
        internal static Pose DestinationFromSource(Pose worldFromDestination,
            Pose worldFromSource)
        {
            Quaternion destinationFromWorld = Quaternion.Inverse(worldFromDestination.rotation);
            return new Pose(
                destinationFromWorld * (worldFromSource.position - worldFromDestination.position),
                destinationFromWorld * worldFromSource.rotation);
        }
    }
}
