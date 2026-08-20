using System;
using UnityEngine;

namespace Genesis.RoomScan.Prism
{
    public enum RigProjection : byte
    {
        RgbLeft = 0,
        RgbRight = 1,
        DepthLeft = 2,
        DepthRight = 3
    }

    public enum RigLensModel : byte
    {
        /// <summary>Meta's public PCA/environment-depth images are already rectified.</summary>
        RectifiedPinhole = 0
    }

    public readonly struct RigProjectionCalibration
    {
        internal RigProjectionCalibration(RigProjection projection, RigStreamKind stream,
            RigEye eye, RigIntrinsics intrinsics, RigLensModel lensModel)
        {
            Projection = projection;
            Stream = stream;
            Eye = eye;
            Intrinsics = intrinsics;
            LensModel = lensModel;
        }

        public RigProjection Projection { get; }
        public RigStreamKind Stream { get; }
        public RigEye Eye { get; }
        public RigIntrinsics Intrinsics { get; }
        public RigLensModel LensModel { get; }
        public Vector2Int Resolution => Intrinsics.ImageResolution;
        public bool IsValid => Intrinsics.IsValid;
    }

    /// <summary>
    /// Immutable calibration epoch. Per-frame poses remain on <see cref="StereoRigFrameLease"/>;
    /// this object contains only projection geometry that is safe to precompute into LUTs.
    /// </summary>
    public sealed class RigCalibration
    {
        private RigCalibration(uint epoch, ulong signature,
            RigProjectionCalibration rgbLeft, RigProjectionCalibration rgbRight,
            RigProjectionCalibration depthLeft, RigProjectionCalibration depthRight,
            RigExtrinsicsSnapshot referenceExtrinsics)
        {
            Epoch = epoch;
            Signature = signature;
            RgbLeft = rgbLeft;
            RgbRight = rgbRight;
            DepthLeft = depthLeft;
            DepthRight = depthRight;
            ReferenceExtrinsics = referenceExtrinsics;
        }

        public uint Epoch { get; }
        public ulong Signature { get; }
        public RigProjectionCalibration RgbLeft { get; }
        public RigProjectionCalibration RgbRight { get; }
        public RigProjectionCalibration DepthLeft { get; }
        public RigProjectionCalibration DepthRight { get; }

        /// <summary>
        /// Diagnostic reference only. Reconstruction always uses the exact independently
        /// timestamped camera poses from each frame rather than freezing this snapshot.
        /// </summary>
        public RigExtrinsicsSnapshot ReferenceExtrinsics { get; }

        public RigProjectionCalibration Get(RigProjection projection) => projection switch
        {
            RigProjection.RgbLeft => RgbLeft,
            RigProjection.RgbRight => RgbRight,
            RigProjection.DepthLeft => DepthLeft,
            RigProjection.DepthRight => DepthRight,
            _ => throw new ArgumentOutOfRangeException(nameof(projection))
        };

        public bool IsCompatible(StereoRigFrameLease frame)
        {
            if (frame == null || !frame.IsValid || frame.CalibrationEpoch != Epoch)
                return false;
            return frame.RgbLeft.Intrinsics.Signature == RgbLeft.Intrinsics.Signature &&
                   frame.RgbRight.Intrinsics.Signature == RgbRight.Intrinsics.Signature &&
                   frame.DepthLeft.Intrinsics.Signature == DepthLeft.Intrinsics.Signature &&
                   frame.DepthRight.Intrinsics.Signature == DepthRight.Intrinsics.Signature &&
                   frame.DepthLeft.DepthEncoding == RigDepthEncoding.ProjectionDepth01 &&
                   frame.DepthRight.DepthEncoding == RigDepthEncoding.ProjectionDepth01;
        }

        public static bool TryCreate(StereoRigFrameLease frame, out RigCalibration calibration)
        {
            calibration = null;
            if (frame == null || !frame.IsValid || frame.CalibrationEpoch == 0u)
                return false;
            if (frame.RgbLeft.Resolution != frame.RgbRight.Resolution ||
                frame.DepthLeft.Resolution != frame.DepthRight.Resolution ||
                frame.DepthLeft.DepthEncoding != RigDepthEncoding.ProjectionDepth01 ||
                frame.DepthRight.DepthEncoding != RigDepthEncoding.ProjectionDepth01)
                return false;

            var rgbLeft = Projection(RigProjection.RgbLeft, frame.RgbLeft);
            var rgbRight = Projection(RigProjection.RgbRight, frame.RgbRight);
            var depthLeft = Projection(RigProjection.DepthLeft, frame.DepthLeft);
            var depthRight = Projection(RigProjection.DepthRight, frame.DepthRight);
            ulong signature = RigCalibrationMath.CombineSignatures(
                rgbLeft.Intrinsics.Signature, rgbRight.Intrinsics.Signature,
                depthLeft.Intrinsics.Signature, depthRight.Intrinsics.Signature);
            calibration = new RigCalibration(frame.CalibrationEpoch, signature,
                rgbLeft, rgbRight, depthLeft, depthRight, frame.Extrinsics);
            return true;
        }

        private static RigProjectionCalibration Projection(RigProjection projection,
            GpuImageView view) => new(projection, view.Kind, view.Eye,
            view.Intrinsics, RigLensModel.RectifiedPinhole);
    }
}
