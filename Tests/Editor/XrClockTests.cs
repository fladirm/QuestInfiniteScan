using System;
using System.Collections.Generic;
using FinalScan.Platform.Sensor;
using NUnit.Framework;

namespace FinalScan.Tests
{
    public class XrClockTests
    {
        const long Ms = 1_000_000L;
        const long Base = 224_849_999_945_542L; // CLOCK_MONOTONIC ns seen on device

        static long Utc(long monoNs) => ClockDomains.UnixEpochTicks + 1_789_175_965L * 10_000_000L + monoNs / 100;

        static void Refs(XrClock c, int n, double jitterNs = 0, double offsetNs = 0, int seed = 1)
        {
            var rnd = new Random(seed);
            for (int i = 0; i < n; i++)
            {
                long mono = Base + i * 13_890_000L;
                double ovr = (mono + offsetNs + (rnd.NextDouble() * 2 - 1) * jitterNs) * 1e-9;
                c.AddReference(ovr, Utc(mono), ovr + 8e-6, mono);
            }
        }

        [Test]
        public void PathA_SelectedWhenMonotonicFieldPresentAndOffsetStable()
        {
            var c = new XrClock(); var lines = new List<string>(); c.GateChanged += lines.Add;
            Refs(c, 16, jitterNs: 6_000); // 6 µs residual as measured
            Assert.IsTrue(c.PathAVerified);
            long capture = Base + 500 * Ms;
            Assert.IsTrue(c.TryConvert(capture, Utc(capture), (capture + 30 * Ms) * 1e-9, out long xr, out long unc, out var path));
            Assert.AreEqual(ClockGatePath.A, path);
            Assert.AreEqual(capture, xr);
            Assert.Less(unc, XrClock.ExactLimitNs);
            Assert.AreEqual(ClockUncertaintyClass.Exact, c.UncertaintyClass);
            Assert.AreEqual(1, lines.Count);
            StringAssert.StartsWith("FS-SENSOR clock_gate {", lines[0]);
            StringAssert.Contains("\"path\":\"A\"", lines[0]);
        }

        [Test]
        public void PathC_WhenMonotonicFieldMissing_LinearFitWithUncertainty()
        {
            var c = new XrClock();
            Refs(c, 64);
            long capture = Base + 700 * Ms;
            Assert.IsTrue(c.TryConvert(-1, Utc(capture), (capture + 30 * Ms) * 1e-9, out long xr, out long unc, out var path));
            Assert.AreEqual(ClockGatePath.C, path);
            Assert.AreEqual(capture, xr, 200_000); // within 0.2 ms of truth (utc has 100 ns resolution, fit exact)
            Assert.Less(unc, XrClock.GoodLimitNs);
            Assert.IsTrue(c.PathCFit.valid);
            Assert.AreEqual(1.0, c.PathCFit.slope, 1e-6);
        }

        [Test]
        public void PathA_RefusedWhenOffsetJitterAboveTwoMs_FallsToC()
        {
            var c = new XrClock();
            Refs(c, 64, jitterNs: 4 * Ms);
            Assert.IsFalse(c.PathAVerified);
            long capture = Base + 700 * Ms;
            Assert.IsTrue(c.TryConvert(capture, Utc(capture), (capture + 30 * Ms) * 1e-9, out _, out long unc, out var path));
            Assert.AreEqual(ClockGatePath.C, path);
            Assert.GreaterOrEqual(unc, Ms); // jittered references show up as fit residual
            Assert.Less(unc, XrClock.DegradedLimitNs);
        }

        [Test]
        public void PathA_RefusedWhenOffsetLarge()
        {
            var c = new XrClock();
            Refs(c, 16, offsetNs: 5 * Ms);
            Assert.IsFalse(c.PathAVerified);
        }

        [Test]
        public void ImplausibleLatency_DropsToPathC_AfterTenFrames()
        {
            var c = new XrClock();
            Refs(c, 32);
            long capture = Base + 700 * Ms;
            for (int i = 0; i < XrClock.PathAImplausibleLimit; i++)
                c.TryConvert(capture + i * Ms, Utc(capture + i * Ms), (capture + i * Ms + 900 * Ms) * 1e-9, out _, out _, out _);
            Assert.IsTrue(c.TryConvert(capture, Utc(capture), (capture + 900 * Ms) * 1e-9, out _, out _, out var path));
            Assert.AreEqual(ClockGatePath.C, path);
            Assert.AreEqual(XrClock.PathAImplausibleLimit + 1, c.ImplausibleTotal);
        }

        [Test]
        public void NoReferences_RejectsFrames()
        {
            var c = new XrClock();
            Assert.IsFalse(c.TryConvert(Base, Utc(Base), (Base + 30 * Ms) * 1e-9, out _, out _, out var path));
            Assert.AreEqual(ClockGatePath.Unknown, path);
            Assert.AreEqual(1, c.FramesRejected);
        }

        [Test]
        public void VeryNoisyReferences_PathCRejectsAboveEightMs()
        {
            var c = new XrClock();
            var rnd = new Random(7);
            for (int i = 0; i < 64; i++)
            {
                long mono = Base + i * 13_890_000L;
                double ovr = (mono + (rnd.NextDouble() * 2 - 1) * 20 * Ms) * 1e-9;
                c.AddReference(ovr, Utc(mono), ovr + 8e-6, -1);
            }
            long capture = Base + 700 * Ms;
            bool ok = c.TryConvert(-1, Utc(capture), (capture + 30 * Ms) * 1e-9, out _, out long unc, out var path);
            Assert.AreEqual(ClockGatePath.C, path);
            Assert.IsFalse(ok);
            Assert.GreaterOrEqual(unc, XrClock.DegradedLimitNs);
            Assert.AreEqual(ClockUncertaintyClass.Reject, c.UncertaintyClass);
        }

        [Test]
        public void GateLine_IsLoggedOnlyOnChange()
        {
            var c = new XrClock(); int n = 0; c.GateChanged += _ => n++;
            Refs(c, 16);
            for (int i = 0; i < 5; i++) { long t = Base + (300 + i * 20) * Ms; c.TryConvert(t, Utc(t), (t + 30 * Ms) * 1e-9, out _, out _, out _); }
            Assert.AreEqual(1, n);
            Assert.AreEqual(5, c.FramesConverted);
        }
    }
}
