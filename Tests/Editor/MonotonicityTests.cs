using FinalScan.Platform.Sensor;
using NUnit.Framework;
using static FinalScan.Tests.SensorTestKit;

namespace FinalScan.Tests
{
    public class MonotonicityTests
    {
        const long T0 = 2_000_000_000L;

        static CameraFrameLease Lease(long id, long t, long receive = 0)
        {
            var l = new CameraFrameLease { frameId = id, eye = CameraEye.Left, xrTimeNs = t, receiveXrTimeNs = receive > 0 ? receive : t + 30 * Ms };
            l.Begin(null);
            return l;
        }

        [Test]
        public void Ring_DropsEqualAndOlderTimestamps_AndCounts()
        {
            var r = new PcaFrameRing(CameraEye.Left);
            Assert.IsTrue(r.Push(Lease(1, T0)));
            Assert.IsTrue(r.Push(Lease(2, T0 + 20 * Ms)));
            var dup = Lease(3, T0 + 20 * Ms);
            Assert.IsFalse(r.Push(dup));
            Assert.IsFalse(dup.Alive);
            Assert.IsFalse(r.Push(Lease(4, T0 + 10 * Ms)));
            Assert.AreEqual(2, r.DroppedNonMonotonic);
            Assert.AreEqual(2, r.Count);
            Assert.AreEqual(T0 + 20 * Ms, r.NewestTimeNs);
            Assert.IsTrue(r.Push(Lease(5, T0 + 40 * Ms)));
            Assert.AreEqual(3, r.Accepted);
        }

        [Test]
        public void Ring_DropsStaleFrames()
        {
            var r = new PcaFrameRing(CameraEye.Right);
            Assert.IsFalse(r.Push(Lease(1, T0, receive: T0 + PcaFrameRing.StaleNs + 1)));
            Assert.AreEqual(1, r.DroppedStale);
            Assert.IsTrue(r.Push(Lease(2, T0 + Ms, receive: T0 + 400 * Ms)));
            Assert.IsFalse(r.Push(Lease(3, 0)));
            Assert.AreEqual(1, r.DroppedInvalidTime);
        }

        [Test]
        public void Ring_EvictsOldest_ReleasingLease_HoldersKeepIt()
        {
            var r = new PcaFrameRing(CameraEye.Left, 3);
            var first = Lease(1, T0); var held = first.Acquire();
            r.Push(first); r.Push(Lease(2, T0 + Ms)); r.Push(Lease(3, T0 + 2 * Ms)); r.Push(Lease(4, T0 + 3 * Ms));
            Assert.AreEqual(3, r.Count);
            Assert.AreEqual(1, r.Evicted);
            Assert.IsTrue(first.Alive);
            Assert.AreEqual(1, first.RefCount);
            held.Release();
            Assert.IsFalse(first.Alive);
            Assert.IsNull(r.Find(1));
            Assert.IsNotNull(r.Find(4));
        }

        [Test]
        public void Pipeline_NonMonotonicFrame_NeverReachesPairing_SlotReturned()
        {
            var p = NewPipeline();
            FillPoses(p, T0 - 100 * Ms, T0 + 2000 * Ms);
            Assert.IsTrue(Feed(p, CameraEye.Left, T0 + 40 * Ms));
            Assert.IsFalse(Feed(p, CameraEye.Left, T0 + 20 * Ms));   // HAL glitch: older timestamp after a newer one
            Assert.AreEqual(1, p.RingL.DroppedNonMonotonic);
            Assert.AreEqual(SensorPipeline.PoolPerEye - 1, p.PoolL.FreeCount);
            Assert.IsTrue(Feed(p, CameraEye.Right, T0 + 20 * Ms));
            Assert.AreEqual(0, p.Pairer.Pairs);                         // R(20) has no L partner: L(20) was dropped, L(40) is 20 ms away → LowConfidence would be allowed; check window
            Assert.IsTrue(Feed(p, CameraEye.Right, T0 + 40 * Ms));
            Assert.AreEqual(1, p.Pairer.Pairs);
            Assert.AreEqual(SkewClass.Direct, p.Pairer.Latest.skew);
        }

        [Test]
        public void Pipeline_PoolExhaustion_EvictsOldestRingEntry_NeverBlocks()
        {
            var p = NewPipeline();
            FillPoses(p, T0 - 100 * Ms, T0 + 4000 * Ms);
            for (int i = 0; i < 40; i++) Assert.IsTrue(Feed(p, CameraEye.Left, T0 + i * 20 * Ms));
            Assert.AreEqual(0, p.FramesPoolExhausted);
            Assert.AreEqual(PcaFrameRing.DefaultDepth, p.RingL.Count);
            Assert.Greater(p.RingL.Evicted, 0);
        }

        [Test]
        public void Pipeline_RejectsFrameWithoutClock_Counts()
        {
            var p = new SensorPipeline(new NullSensorSink());
            Assert.IsFalse(Feed(p, CameraEye.Left, T0));
            Assert.AreEqual(1, p.FramesNoTime);
            Assert.AreEqual(SensorPipeline.PoolPerEye, p.PoolL.FreeCount);
        }

        [Test]
        public void Cadence_MeasuresPeriodAndFps()
        {
            var c = new CadenceMeter(1e9 / 30);
            Assert.AreEqual(1e9 / 30, c.MedianPeriodNs, 1);
            Assert.AreEqual(0.0, c.DeliveredFps(T0), 1e-9);
            for (int i = 0; i < 150; i++) c.Add(T0 + i * 20 * Ms, T0 + i * 20 * Ms + 30 * Ms);
            Assert.AreEqual(20 * Ms, c.MedianPeriodNs, 1);
            Assert.AreEqual(0.0, c.DeliveredFps(T0 + 50 * 20 * Ms + 30 * Ms), 1e-9);            // warm-up: window not yet full → "not measured"
            Assert.AreEqual(50.0, c.DeliveredFps(T0 + 149 * 20 * Ms + 30 * Ms), 1.0);
            Assert.AreEqual(0.0, c.DeliveredFps(T0 + 149 * 20 * Ms + 30 * Ms + 3_000 * Ms), 1e-9); // stalled stream decays
        }
    }
}
