using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Borrowed metadata view emitted synchronously by <see cref="DepthCapture"/>.
    /// Consumers that need the image after the callback must enqueue a GPU-to-GPU copy.
    /// </summary>
    public readonly struct RawStereoDepthFrame
    {
        internal RawStereoDepthFrame(Texture stereoTexture, long timestampNanoseconds,
            Pose worldFromLeft, Pose worldFromRight, XRFov leftFov, XRFov rightFov,
            Vector2 nearFar, long sequence)
        {
            StereoTexture = stereoTexture;
            TimestampNanoseconds = timestampNanoseconds;
            WorldFromLeft = worldFromLeft;
            WorldFromRight = worldFromRight;
            LeftFov = leftFov;
            RightFov = rightFov;
            NearFar = nearFar;
            Sequence = sequence;
        }

        public Texture StereoTexture { get; }
        public long TimestampNanoseconds { get; }
        public Pose WorldFromLeft { get; }
        public Pose WorldFromRight { get; }
        public XRFov LeftFov { get; }
        public XRFov RightFov { get; }
        public Vector2 NearFar { get; }
        public long Sequence { get; }
        public bool IsValid => StereoTexture != null && TimestampNanoseconds > 0L &&
                               StereoTexture.width > 0 && StereoTexture.height > 0 &&
                               StereoTexture.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray &&
                               IsFinite(WorldFromLeft) && IsFinite(WorldFromRight) &&
                               IsFinite(LeftFov) && IsFinite(RightFov) &&
                               RigDepthContract.IsValidRange(NearFar);

        private static bool IsFinite(Pose pose) =>
            float.IsFinite(pose.position.x) && float.IsFinite(pose.position.y) &&
            float.IsFinite(pose.position.z) && float.IsFinite(pose.rotation.x) &&
            float.IsFinite(pose.rotation.y) && float.IsFinite(pose.rotation.z) &&
            float.IsFinite(pose.rotation.w) &&
            pose.rotation.x * pose.rotation.x + pose.rotation.y * pose.rotation.y +
            pose.rotation.z * pose.rotation.z + pose.rotation.w * pose.rotation.w > 0.5f;

        private static bool IsFinite(XRFov fov) =>
            float.IsFinite(fov.angleLeft) && float.IsFinite(fov.angleRight) &&
            float.IsFinite(fov.angleUp) && float.IsFinite(fov.angleDown) &&
            fov.angleRight > fov.angleLeft && fov.angleUp > fov.angleDown;
    }
}
