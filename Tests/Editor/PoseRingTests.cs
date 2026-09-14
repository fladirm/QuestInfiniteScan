using FinalScan.Platform.Sensor;
using NUnit.Framework;
using UnityEngine;

namespace FinalScan.Tests
{
    public class PoseRingTests
    {
        const long Ms = 1_000_000L;

        static PoseRing Linear(int n, long stepNs, Vector3 vel, float degPerSec)
        {
            var r = new PoseRing();
            for (int i = 0; i < n; i++)
            {
                double s = i * stepNs * 1e-9;
                var head = new Pose(vel * (float)s, SensorMath.AngleAxis(degPerSec * (float)s, Vector3.up));
                r.Push(1_000_000_000L + i * stepNs, head, head, head);
            }
            return r;
        }

        [Test]
        public void Interpolates_BetweenSamples()
        {
            var r = Linear(20, 10 * Ms, new Vector3(1f, 0, 0), 90f);
            Assert.IsTrue(r.TryLocate(1_000_000_000L + 55 * Ms, out Pose p));
            Assert.AreEqual(0.055f, p.position.x, 1e-4f);
            Assert.AreEqual(4.95f, SensorMath.AngleDeg(Quaternion.identity, p.rotation), 0.05f);
            Assert.AreEqual(1, r.LocateInterpolated);
        }

        [Test]
        public void ExactSampleTime_ReturnsSample()
        {
            var r = Linear(5, 10 * Ms, new Vector3(1f, 0, 0), 0f);
            Assert.IsTrue(r.TryLocate(1_000_000_000L + 30 * Ms, out Pose p));
            Assert.AreEqual(0.03f, p.position.x, 1e-6f);
        }

        [Test]
        public void Extrapolates_UpTo20ms_ThenFails()
        {
            var r = Linear(10, 10 * Ms, new Vector3(1f, 0, 0), 0f);
            long newest = r.NewestTimeNs;
            Assert.IsTrue(r.TryLocate(newest + 15 * Ms, PoseNode.Head, out Pose p, out long ex));
            Assert.AreEqual(15 * Ms, ex);
            Assert.AreEqual(0.09f + 0.015f, p.position.x, 1e-4f);
            Assert.AreEqual(1, r.LocateExtrapolated);
            Assert.IsFalse(r.TryLocate(newest + 21 * Ms, out _));
            Assert.AreEqual(1, r.LocateFailed);
        }

        [Test]
        public void BeforeOldest_Fails_AndRingWrapsKeepingNewest()
        {
            var r = new PoseRing(8);
            for (int i = 0; i < 20; i++) r.Push(1000L + i * 10, Pose.identity, Pose.identity, Pose.identity);
            Assert.AreEqual(8, r.Count);
            Assert.AreEqual(1000L + 12 * 10, r.OldestTimeNs);
            Assert.AreEqual(1000L + 19 * 10, r.NewestTimeNs);
            Assert.IsFalse(r.TryLocate(1000L, out _));
            Assert.IsTrue(r.TryLocate(1000L + 150, out _));
        }

        [Test]
        public void NonMonotonicSamples_Rejected()
        {
            var r = new PoseRing();
            Assert.IsTrue(r.Push(100, Pose.identity, Pose.identity, Pose.identity));
            Assert.IsFalse(r.Push(100, Pose.identity, Pose.identity, Pose.identity));
            Assert.IsFalse(r.Push(50, Pose.identity, Pose.identity, Pose.identity));
            Assert.AreEqual(2, r.RejectedNonMonotonic);
            Assert.AreEqual(1, r.Count);
        }

        [Test]
        public void Motion_ReportsPeakAngularAndLinearSpeed()
        {
            var r = Linear(72, 13_888_889L, new Vector3(0, 0, 0.8f), 120f);
            Assert.IsTrue(r.TryMotion(1_000_000_000L + 200 * Ms, 1_000_000_000L + 260 * Ms, out float ang, out float lin));
            Assert.AreEqual(120f, ang, 1.0f);
            Assert.AreEqual(0.8f, lin, 0.01f);
            Assert.IsFalse(r.TryMotion(1_000_000_000L - 500 * Ms, 1_000_000_000L - 400 * Ms, out _, out _));
        }

        [Test]
        public void Capacity_CoversTwoSecondsAt72Hz()
        {
            Assert.GreaterOrEqual(PoseRing.DefaultCapacity, PoseRing.CoverageSeconds * PoseRing.NominalRateHz);
            var r = Linear(PoseRing.DefaultCapacity, 13_888_889L, Vector3.zero, 0f);
            Assert.GreaterOrEqual(r.NewestTimeNs - r.OldestTimeNs, 2_000 * Ms);
        }

        [Test]
        public void EyeNodes_LocatedSeparately()
        {
            var r = new PoseRing();
            r.Push(100 * Ms, Pose.identity, new Pose(new Vector3(-0.03f, 0, 0), Quaternion.identity), new Pose(new Vector3(0.03f, 0, 0), Quaternion.identity));
            r.Push(200 * Ms, Pose.identity, new Pose(new Vector3(-0.03f, 0, 0), Quaternion.identity), new Pose(new Vector3(0.03f, 0, 0), Quaternion.identity));
            Assert.IsTrue(r.TryLocate(150 * Ms, PoseNode.EyeLeft, out Pose l));
            Assert.IsTrue(r.TryLocate(150 * Ms, PoseNode.EyeRight, out Pose rr));
            Assert.AreEqual(0.06f, rr.position.x - l.position.x, 1e-6f);
        }
    }
}
