using System.Collections.Generic;
using FinalScan.Platform.Sensor;
using NUnit.Framework;
using UnityEngine;
using static FinalScan.Tests.SensorTestKit;

namespace FinalScan.Tests
{
    public class StereoPairerTests
    {
        const long T0 = 2_000_000_000L;

        [Test]
        public void SynchronousStreams_PairDirectly()
        {
            var p = NewPipeline();
            FillPoses(p, T0 - 100 * Ms, T0 + 2000 * Ms);
            for (int i = 0; i < 40; i++) { long t = T0 + i * 20 * Ms; Feed(p, CameraEye.Left, t); Feed(p, CameraEye.Right, t); }
            Assert.AreEqual(40, p.Pairer.Pairs);
            Assert.AreEqual(40, p.Pairer.Direct);
            Assert.AreEqual(0, p.Pairer.RejectedBySkew + p.Pairer.ExpiredUnpaired);
            Assert.AreEqual(SkewClass.Direct, p.Pairer.Latest.skew);
            Assert.AreEqual(1f, p.Pairer.Latest.confidence);
            Assert.IsTrue(p.Pairer.Latest.geometryEvidence);
            Assert.AreEqual(ClockGatePath.A, p.Pairer.Latest.gatePath);
        }

        [Test]
        public void MeasuredThirtyHzPattern_HistoryRingPairsWhatLatestOnlyWouldLose()
        {
            // C01: at "30 Hz" the sensor base is 50 Hz, capture times quantised to 20 ms; R may run 40 ms behind L in
            // receive order. The history ring pairs R(t) with L(t) (dt = 0) instead of the newest L (dt = 40 ms).
            var p = NewPipeline(CaptureProfile.Normal30);
            FillPoses(p, T0 - 100 * Ms, T0 + 4000 * Ms);
            var left = new List<long>(); var right = new List<long>();
            for (int i = 0; i < 30; i++) { left.Add(T0 + i * 40 * Ms); right.Add(T0 + i * 40 * Ms); }
            // receive order: L(i) then R(i-1)  → R arrives one period late
            for (int i = 0; i < 30; i++) { Feed(p, CameraEye.Left, left[i]); if (i >= 1) Feed(p, CameraEye.Right, right[i - 1]); }
            Feed(p, CameraEye.Right, right[29]);
            Assert.GreaterOrEqual(p.Pairer.Pairs, 29);
            Assert.AreEqual(p.Pairer.Pairs, p.Pairer.Direct);
            Assert.AreEqual(0, p.Pairer.RejectedBySkew);
        }

        [Test]
        public void HalfPeriodOffset_ClassifiedLowConfidence_AndSkewSigned()
        {
            var p = NewPipeline(CaptureProfile.Normal30);
            FillPoses(p, T0 - 100 * Ms, T0 + 4000 * Ms);
            for (int i = 0; i < 30; i++) { Feed(p, CameraEye.Left, T0 + i * 40 * Ms); Feed(p, CameraEye.Right, T0 + i * 40 * Ms + 20 * Ms); }
            Assert.GreaterOrEqual(p.Pairer.Pairs, 29);
            Assert.AreEqual(0, p.Pairer.Direct);
            Assert.AreEqual(0, p.Pairer.RejectedBySkew);
            var o = p.Pairer.Latest;
            Assert.AreEqual(20 * Ms, o.skewNs);
            Assert.AreEqual(SkewClass.LowConfidence, o.skew);  // 20 ms == period/2 → not motionCompensate (strict), < period → low
            Assert.AreEqual(0.4f, o.confidence, 1e-6f);
        }

        [Test]
        public void SmallSkew_MotionCompensateClass_WithHeadVelocityAttached()
        {
            var p = NewPipeline(CaptureProfile.Normal60);
            FillPoses(p, T0 - 100 * Ms, T0 + 2000 * Ms, degPerSec: 30f);
            for (int i = 0; i < 20; i++) { Feed(p, CameraEye.Left, T0 + i * 20 * Ms); Feed(p, CameraEye.Right, T0 + i * 20 * Ms + 6 * Ms); }
            var o = p.Pairer.Latest;
            Assert.AreEqual(SkewClass.MotionCompensate, o.skew);
            Assert.AreEqual(30f, o.headAngularDegPerSec, 1.5f);
            Assert.AreEqual(MotionVerdict.GeometryEvidence, o.motion);
            Assert.AreEqual(0.7f, o.confidence, 1e-6f);
        }

        [Test]
        public void FastHeadRotation_IsNotGeometryEvidence()
        {
            var p = NewPipeline();
            FillPoses(p, T0 - 100 * Ms, T0 + 2000 * Ms, degPerSec: 150f);
            for (int i = 0; i < 10; i++) { long t = T0 + i * 20 * Ms; Feed(p, CameraEye.Left, t); Feed(p, CameraEye.Right, t); }
            var o = p.Pairer.Latest;
            Assert.AreEqual(MotionVerdict.NotGeometryEvidence, o.motion);
            Assert.IsFalse(o.geometryEvidence);
            Assert.Greater(o.blurPenalty, 0f);
            Assert.AreEqual(p.Pairer.Pairs, p.Pairer.MotionGated);
        }

        [Test]
        public void MissingFrameOnOneEye_ExpiresUnpaired_RestPairs()
        {
            var p = NewPipeline();
            FillPoses(p, T0 - 100 * Ms, T0 + 2000 * Ms);
            for (int i = 0; i < 30; i++) { long t = T0 + i * 20 * Ms; Feed(p, CameraEye.Left, t); if (i != 10) Feed(p, CameraEye.Right, t); }
            Assert.AreEqual(29, p.Pairer.Pairs);
            Assert.AreEqual(1, p.Pairer.ExpiredUnpaired);
        }

        [Test]
        public void StalledEye_CandidateBeyondPeriodInsideWindow_RejectedBySkew()
        {
            var p = NewPipeline(CaptureProfile.Normal60);
            FillPoses(p, T0 - 100 * Ms, T0 + 4000 * Ms);
            for (int i = 0; i < 20; i++) { long t = T0 + i * 20 * Ms; Feed(p, CameraEye.Left, t); if (i < 19) Feed(p, CameraEye.Right, t); } // period measured 20 ms → window 30 ms; L19 pending
            Assert.AreEqual(19, p.Pairer.Pairs);
            long last = T0 + 19 * 20 * Ms;
            // L stalls; R continues 25 ms after the last L: inside the window (30) but beyond the period (20).
            // The pair waits while a closer L could still arrive, and is decided once R has moved past the window.
            Feed(p, CameraEye.Right, last + 25 * Ms);
            Assert.AreEqual(0, p.Pairer.RejectedBySkew);
            Feed(p, CameraEye.Right, last + 45 * Ms);
            Assert.AreEqual(19, p.Pairer.Pairs);
            Assert.AreEqual(1, p.Pairer.RejectedBySkew);
            Assert.AreEqual(PairState.Rejected, p.RingL.Newest.pairState);
            Assert.AreEqual(PairState.Pending, p.RingR.Newest.pairState); // a delayed L batch could still pair it; ring eviction bounds the wait
        }

        [Test]
        public void DegradedTimestampUncertainty_LowersConfidence_RejectAboveEightMs()
        {
            var p = NewPipeline();
            FillPoses(p, T0 - 100 * Ms, T0 + 2000 * Ms);
            Feed(p, CameraEye.Left, T0, uncertaintyOverrideNs: 3 * Ms); Feed(p, CameraEye.Right, T0);
            Assert.AreEqual(1, p.Pairer.Pairs);
            Assert.IsTrue(p.Pairer.Latest.uncertaintyDegraded);
            Assert.AreEqual(0.5f, p.Pairer.Latest.confidence, 1e-6f);
            Feed(p, CameraEye.Left, T0 + 20 * Ms, uncertaintyOverrideNs: 9 * Ms); Feed(p, CameraEye.Right, T0 + 20 * Ms);
            Assert.AreEqual(1, p.Pairer.Pairs);
            Assert.AreEqual(1, p.Pairer.RejectedByUncertainty);
        }

        [Test]
        public void ExtrinsicsFromLensOffsets_GiveMeasuredBaseline()
        {
            var p = NewPipeline();
            FillPoses(p, T0 - 100 * Ms, T0 + 2000 * Ms, velocity: new Vector3(0.5f, 0, 0));
            Feed(p, CameraEye.Left, T0); Feed(p, CameraEye.Right, T0);
            var o = p.Pairer.Latest;
            Assert.AreEqual(0.0634f, o.baselineMeters, 0.0005f);
            Assert.AreEqual(0.0634f, o.extrinsicsRightInLeft.position.magnitude, 0.001f); // same instant → head motion cancels
            Assert.AreEqual(0.0634f, o.cameraPoseR.position.x - o.cameraPoseL.position.x, 0.005f);
            Assert.AreEqual((uint)1, o.observationId);
        }

        [Test]
        public void History_KeepsEightObservations_TexturesOnlyForNewestTwo_PoolRecovers()
        {
            var p = NewPipeline();
            FillPoses(p, T0 - 100 * Ms, T0 + 2000 * Ms);
            for (int i = 0; i < 24; i++) { long t = T0 + i * 20 * Ms; Feed(p, CameraEye.Left, t); Feed(p, CameraEye.Right, t); }
            Assert.AreEqual(8, p.Pairer.History.Count);
            Assert.AreEqual(24, p.Pairer.Pairs);
            int withTex = 0; foreach (var o in p.Pairer.History) if (o.HasTextures) withTex++;
            Assert.AreEqual(StereoPairer.TextureHistoryDepth, withTex);
            Assert.IsTrue(p.Pairer.Latest.HasTextures);
            Assert.AreEqual(0, p.FramesPoolExhausted);
            Assert.AreEqual(SensorPipeline.PoolPerEye - p.RingL.Count, p.PoolL.FreeCount);
            Assert.AreEqual((uint)24, p.Pairer.Latest.observationId);
        }

        [Test]
        public void ScanTicks_LatestObservationOnly_AtMostTwentyPerSecond()
        {
            var sink = new NullSensorSink();
            var p = NewPipeline(CaptureProfile.Normal60, sink);
            FillPoses(p, T0 - 100 * Ms, T0 + 2000 * Ms);
            for (int i = 0; i < 50; i++) { long t = T0 + i * 20 * Ms; Feed(p, CameraEye.Left, t); Feed(p, CameraEye.Right, t); }
            // 50 pairs over 1 s (receive at +30 ms) → ≤ 21 ticks, each for the newest observation at that moment
            // frames arrive on a 20 ms grid, so the 50 ms minimum spacing quantises to 60 ms → 16-17 ticks per second
            Assert.LessOrEqual(sink.Ticks, 21);
            Assert.GreaterOrEqual(sink.Ticks, 16);
            Assert.AreEqual(p.TicksRequested, sink.Ticks);
            Assert.Greater(p.TicksCoalesced, 0);
            p.PumpTicks(T0 + 10_000 * Ms);
            Assert.AreEqual(p.Pairer.Latest.observationId, sink.LastObservationId);
            Assert.AreEqual((uint)0, p.PendingTickObservation);
        }
    }
}
