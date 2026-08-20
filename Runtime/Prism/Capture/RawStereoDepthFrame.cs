using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Genesis.RoomScan.Prism
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
                               StereoTexture.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray;
    }
}
