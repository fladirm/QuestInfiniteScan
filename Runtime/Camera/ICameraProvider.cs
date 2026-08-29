using UnityEngine;

namespace Genesis.RoomScan
{
    public enum StereoEye
    {
        Left = 0,
        Right = 1
    }

    public enum StereoFrameMatch
    {
        Waiting,
        Ready,
        DepthExpired
    }

    /// <summary>One immutable PCA image descriptor in Unity world space.</summary>
    public readonly struct CameraFrameDescriptor
    {
        public readonly Texture Texture;
        public readonly Pose WorldPose;
        public readonly Vector2 FocalLength;
        public readonly Vector2 PrincipalPoint;
        public readonly Vector2 SensorResolution;
        public readonly Vector2 CurrentResolution;
        public readonly double TimestampUnixSeconds;
        public readonly uint Sequence;
        public readonly StereoEye Eye;

        public CameraFrameDescriptor(Texture texture, Pose worldPose,
            Vector2 focalLength, Vector2 principalPoint,
            Vector2 sensorResolution, Vector2 currentResolution,
            double timestampUnixSeconds, uint sequence,
            StereoEye eye)
        {
            Texture = texture;
            WorldPose = worldPose;
            FocalLength = focalLength;
            PrincipalPoint = principalPoint;
            SensorResolution = sensorResolution;
            CurrentResolution = currentResolution;
            TimestampUnixSeconds = timestampUnixSeconds;
            Sequence = sequence;
            Eye = eye;
        }

        public bool IsValid => Texture != null && Sequence != 0u &&
                               SensorResolution.x > 0f &&
                               SensorResolution.y > 0f &&
                               CurrentResolution.x > 0f &&
                               CurrentResolution.y > 0f;
        public bool HasCoherentTime => IsValid &&
            double.IsFinite(TimestampUnixSeconds) &&
            TimestampUnixSeconds > 0.0;
    }

    /// <summary>
    /// The two PCA inputs that accompany one owned stereo depth frame. Both are
    /// mandatory and already matched to depth in one Unix timestamp domain.
    /// </summary>
    public readonly struct StereoCameraFrame
    {
        public readonly CameraFrameDescriptor Left;
        public readonly CameraFrameDescriptor Right;
        public readonly double MaximumSkewSeconds;

        public StereoCameraFrame(CameraFrameDescriptor left,
            CameraFrameDescriptor right, double maximumSkewSeconds)
        {
            Left = left;
            Right = right;
            MaximumSkewSeconds = maximumSkewSeconds;
        }

        public bool IsValid => Left.HasCoherentTime && Right.HasCoherentTime &&
                               Left.Eye == StereoEye.Left &&
                               Right.Eye == StereoEye.Right &&
                               double.IsFinite(MaximumSkewSeconds) &&
                               MaximumSkewSeconds >= 0.0;
    }

    /// <summary>
    /// Interface for providing RGB camera frames and intrinsics to the scan pipeline.
    /// Implement this to plug in custom camera sources (Meta PassthroughCameraAccess,
    /// UXR QuestCamera, etc.).
    /// </summary>
    public interface ICameraProvider
    {
        /// <summary>True when both physical PCA streams have timestamped frames.</summary>
        bool IsReady { get; }

        /// <summary>True only while both PCA streams are actively running.</summary>
        bool IsPlaying { get; }

        /// <summary>
        /// Match the latest complete PCA L/R pair to one already-owned depth
        /// timestamp. No texture is accepted outside the specified skew window.
        /// </summary>
        StereoFrameMatch TryGetSynchronizedFrame(double depthUnixSeconds,
            double maximumSkewSeconds, out StereoCameraFrame frame);

        /// <summary>Begins camera frame acquisition.</summary>
        void StartCapture();

        /// <summary>Stops camera frame acquisition and releases resources.</summary>
        void StopCapture();
    }
}
