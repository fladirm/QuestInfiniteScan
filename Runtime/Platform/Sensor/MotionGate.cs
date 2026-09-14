using System;
using UnityEngine;

namespace FinalScan.Platform.Sensor
{
    public enum MotionVerdict
    {
        /// <summary>No pose history covering the frame: caller decides (observation still published, geometryEvidence=false).</summary>
        Unknown = 0,
        GeometryEvidence = 1,
        /// <summary>Head angular speed above the gate: not geometry evidence, may remain an appearance candidate with blur penalty (§4.5).</summary>
        NotGeometryEvidence = 2
    }

    /// <summary>
    /// Rolling-shutter model of the PCA sensors (contract §4.5). Meta does not document the shutter; C01 could not measure the
    /// row time (needs a scene with a known fast-moving edge or a flashing LED — C01 follow-up). Until <see cref="Known"/> is
    /// true the per-row pose correction is zero and the angular-velocity gate is the only protection.
    /// When C01 delivers a row time, set <see cref="RowTimeNs"/> and <see cref="ReadoutTopToBottom"/>; producers then call
    /// <see cref="RowTimeOffsetNs"/> to shift the capture time per row (or per row band in the pyramid stage).
    /// </summary>
    public sealed class RollingShutterModel
    {
        public bool Known;
        /// <summary>Time between the exposure start of consecutive rows.</summary>
        public double RowTimeNs;
        public bool ReadoutTopToBottom = true;
        /// <summary>Row whose exposure the frame timestamp refers to (0 = first row, height/2 = centre; unknown → centre assumed).</summary>
        public double ReferenceRowFraction = 0.5;

        /// <summary>Offset to add to the frame capture time for image row <paramref name="row"/> of <paramref name="height"/>; 0 while unknown.</summary>
        public double RowTimeOffsetNs(int row, int height)
        {
            if (!Known || RowTimeNs <= 0 || height <= 1) return 0;
            double r = ReadoutTopToBottom ? row : height - 1 - row;
            return (r - ReferenceRowFraction * (height - 1)) * RowTimeNs;
        }

        /// <summary>Total readout span of a frame; 0 while unknown.</summary>
        public double FrameReadoutNs(int height) => Known && RowTimeNs > 0 ? RowTimeNs * Math.Max(0, height - 1) : 0;
    }

    /// <summary>
    /// Angular-velocity gate (contract §4.5): head rotation faster than <see cref="AngularThresholdDegPerSec"/> (provisional
    /// 90°/s) during the capture window means the frame is not geometry evidence. Speeds come from <see cref="PoseRing"/>
    /// (peak between samples inside the window widened by <see cref="WindowPadNs"/> on both sides to cover exposure).
    /// </summary>
    public sealed class MotionGate
    {
        public const float DefaultAngularThresholdDegPerSec = 90f;
        public const long WindowPadNs = 10_000_000; // 10 ms each side: exposure + timestamp uncertainty

        public float AngularThresholdDegPerSec = DefaultAngularThresholdDegPerSec;
        public readonly RollingShutterModel RollingShutter = new RollingShutterModel();
        public long Evaluated { get; private set; }
        public long Gated { get; private set; }
        public long Unknown { get; private set; }

        public MotionVerdict Evaluate(PoseRing ring, long fromNs, long toNs, out float angularDegPerSec, out float linearMetersPerSec)
        {
            Evaluated++;
            angularDegPerSec = 0; linearMetersPerSec = 0;
            if (ring == null || !ring.TryMotion(Math.Min(fromNs, toNs) - WindowPadNs, Math.Max(fromNs, toNs) + WindowPadNs, out angularDegPerSec, out linearMetersPerSec))
            { Unknown++; return MotionVerdict.Unknown; }
            if (angularDegPerSec > AngularThresholdDegPerSec) { Gated++; return MotionVerdict.NotGeometryEvidence; }
            return MotionVerdict.GeometryEvidence;
        }

        /// <summary>Blur penalty in [0,1] for appearance use: 0 below half the threshold, 1 at 2× the threshold.</summary>
        public float BlurPenalty(float angularDegPerSec)
        {
            float lo = AngularThresholdDegPerSec * 0.5f, hi = AngularThresholdDegPerSec * 2f;
            return Mathf.Clamp01((angularDegPerSec - lo) / (hi - lo));
        }
    }
}
