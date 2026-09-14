using System;
using FinalScan.Platform.Sensor;
using UnityEngine;

namespace FinalScan.Tests
{
    /// <summary>Synthetic feeders for the sensor authority tests (pure C#, no Unity engine calls).</summary>
    public static class SensorTestKit
    {
        public const long Ms = 1_000_000L;
        public static readonly Pose LensL = new Pose(new Vector3(-0.0317f, -0.0129f, 0.0745f), new Quaternion(0.0567f, -0.0016f, 0.0032f, 0.9984f));
        public static readonly Pose LensR = new Pose(new Vector3(0.0317f, -0.0128f, 0.0750f), new Quaternion(0.0535f, -0.0033f, 0.0006f, 0.9986f));

        public static CameraIntrinsicsData Intrinsics(CameraEye eye)
            => CameraIntrinsicsData.Create(870f, 870f, 640f, 640f, 1280, 960, 1280, 1280, eye == CameraEye.Left ? LensL : LensR);

        /// <summary>Makes the clock gate select path A: references where OVR seconds == CLOCK_MONOTONIC exactly.</summary>
        public static void VerifyPathA(SensorPipeline p, long startNs = 1_000_000_000L)
        {
            for (int i = 0; i < 8; i++)
            {
                long mono = startNs + i * 13_890_000L;
                double ovr = mono * 1e-9;
                p.AddClockReference(ovr, UtcTicksFor(mono), ovr + 5e-6, mono);
            }
        }

        public static long UtcTicksFor(long monoNs) => ClockDomains.UnixEpochTicks + 1_700_000_000L * 10_000_000L + monoNs / 100;

        /// <summary>Fills the pose ring at 72 Hz with a head rotating about +Y at <paramref name="degPerSec"/> from t0 to t1.</summary>
        public static void FillPoses(SensorPipeline p, long fromNs, long toNs, float degPerSec = 0f, Vector3 velocity = default)
        {
            for (long t = fromNs; t <= toNs; t += 13_888_889L)
            {
                double s = (t - fromNs) * 1e-9;
                var head = new Pose(velocity * (float)s, SensorMath.AngleAxis(degPerSec * (float)s, Vector3.up));
                p.PushPose(new PoseSample { xrTimeNs = t, head = head, eyeLeft = head, eyeRight = head, valid = true });
            }
        }

        /// <summary>Feeds one camera frame captured at <paramref name="captureNs"/> and received <paramref name="latencyNs"/> later (path A inputs).</summary>
        public static bool Feed(SensorPipeline p, CameraEye eye, long captureNs, long latencyNs = 30 * Ms, long uncertaintyOverrideNs = -1)
        {
            long receive = captureNs + latencyNs;
            if (!p.TryBeginFrame(eye, captureNs, UtcTicksFor(captureNs), receive * 1e-9, receive, Intrinsics(eye), null, out var lease)) return false;
            if (uncertaintyOverrideNs >= 0) lease.uncertaintyNs = uncertaintyOverrideNs;
            return p.CommitFrame(lease);
        }

        public static SensorPipeline NewPipeline(CaptureProfile profile = CaptureProfile.Normal60, NullSensorSink sink = null)
        {
            var p = new SensorPipeline(sink ?? new NullSensorSink(), profile);
            VerifyPathA(p);
            return p;
        }
    }
}
