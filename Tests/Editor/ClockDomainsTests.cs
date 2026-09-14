using System;
using System.Collections.Generic;
using FinalScan.Platform.Sensor;
using NUnit.Framework;

namespace FinalScan.Tests
{
    public class ClockDomainsTests
    {
        [Test]
        public void Fit_RecoversExactLinearRelation()
        {
            var x = new List<double>(); var y = new List<double>();
            for (int i = 0; i < 50; i++) { double t = 1e6 + i * 5.0; x.Add(t); y.Add(1.000002 * t + 123.456); }
            var f = ClockDomains.Fit(x, y);
            Assert.IsTrue(f.valid);
            Assert.AreEqual(50, f.count);
            Assert.AreEqual(1.000002, f.slope, 1e-9);
            Assert.AreEqual(123.456, f.offset, 1e-3);
            Assert.Less(f.residualRms, 1e-6);
            Assert.AreEqual(2.0, f.DriftPpm, 1e-3);
            Assert.AreEqual(y[7], f.Map(x[7]), 1e-6);
            Assert.AreEqual(x[7], f.Inverse(y[7]), 1e-6);
        }

        [Test]
        public void Fit_ReportsResidualsForNoisyData()
        {
            var x = new double[] { 0, 1, 2, 3, 4 };
            var y = new double[] { 0.1, 0.9, 2.1, 2.9, 4.1 };
            var f = ClockDomains.Fit(x, y);
            Assert.IsTrue(f.valid);
            Assert.AreEqual(1.0, f.slope, 1e-9);
            Assert.AreEqual(0.02, f.offset, 1e-9);
            Assert.Greater(f.residualRms, 0.05);
            Assert.AreEqual(0.12, f.residualMax, 1e-9);
        }

        [Test]
        public void Fit_InvalidWithDegenerateInput()
        {
            Assert.IsFalse(ClockDomains.Fit(null, null).valid);
            Assert.IsFalse(ClockDomains.Fit(new double[] { 1 }, new double[] { 2 }).valid);
            Assert.IsFalse(ClockDomains.Fit(new double[] { 3, 3, 3 }, new double[] { 1, 2, 3 }).valid);
            Assert.IsTrue(double.IsNaN(ClockDomains.Convert(1.0, default)));
        }

        [Test]
        public void Fit_OverTriplets_MapsOvrToMonotonicAndBoottime()
        {
            var samples = new List<ClockTriplet>();
            // boottime = monotonic + 2 s (suspend), ovr = monotonic seconds exactly, utc = unix 1.7e9 + monotonic.
            for (int i = 0; i < 20; i++)
            {
                long mono = 5_000_000_000L + i * 5_000_000_000L;
                samples.Add(new ClockTriplet
                {
                    ovrSeconds = mono * 1e-9,
                    monotonicNs = mono,
                    boottimeNs = mono + 2_000_000_000L,
                    utcTicks = ClockDomains.UnixSecondsToUtcTicks(1.7e9 + mono * 1e-9)
                });
            }
            var om = ClockDomains.Fit(samples, ClockAxis.OvrSeconds, ClockAxis.MonotonicNs);
            Assert.IsTrue(om.valid); Assert.AreEqual(1.0, om.slope, 1e-9); Assert.AreEqual(0.0, om.offset, 1e-6);
            var mb = ClockDomains.Fit(samples, ClockAxis.MonotonicNs, ClockAxis.BoottimeNs);
            Assert.AreEqual(1.0, mb.slope, 1e-9); Assert.AreEqual(2.0, mb.offset, 1e-6);
            var mu = ClockDomains.Fit(samples, ClockAxis.MonotonicNs, ClockAxis.UtcTicks);
            Assert.AreEqual(1.0, mu.slope, 1e-9); Assert.AreEqual(1.7e9, mu.offset, 1e-3);
            Assert.IsTrue(ClockDomains.TryOffset(samples, ClockAxis.MonotonicNs, ClockAxis.BoottimeNs, out double off, out double spread));
            Assert.AreEqual(2.0, off, 1e-9); Assert.AreEqual(0.0, spread, 1e-9);
        }

        [Test]
        public void UnixConversions_RoundTrip()
        {
            long ticks = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc).Ticks;
            double unix = ClockDomains.UtcTicksToUnixSeconds(ticks);
            Assert.AreEqual(1789387200.0, unix, 1e-6);
            Assert.AreEqual(ticks, ClockDomains.UnixSecondsToUtcTicks(unix));
            Assert.AreEqual(ClockDomains.UnixEpochTicks, DateTime.UnixEpoch.Ticks);
            Assert.AreEqual(ticks, ClockDomains.UnixMicrosToUtcTicks((long)(unix * 1e6)));
            Assert.AreEqual(1.5, ClockDomains.NsToSeconds(1_500_000_000L), 1e-12);
            Assert.AreEqual(1_500_000_000L, ClockDomains.SecondsToNs(1.5));
            var t = new ClockTriplet { utcTicks = ticks, monotonicNs = 3_000_000_000L, boottimeNs = 4_000_000_000L };
            Assert.AreEqual(unix, t.UnixSeconds, 1e-6); Assert.AreEqual(3.0, t.MonotonicSeconds, 1e-12); Assert.AreEqual(4.0, t.BoottimeSeconds, 1e-12);
            Assert.IsTrue(double.IsNaN(ClockDomains.Seconds(t, (ClockAxis)99)));
        }
    }
}
