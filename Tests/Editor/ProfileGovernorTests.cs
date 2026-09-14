using FinalScan.Platform.Sensor;
using NUnit.Framework;
using UnityEngine;

namespace FinalScan.Tests
{
    public class ProfileGovernorTests
    {
        [Test]
        public void Specs_MatchContractOperatingPoints()
        {
            Assert.AreEqual(new Vector2Int(1280, 960), CaptureProfiles.Resolution(CaptureProfile.Normal30));
            Assert.AreEqual(30, CaptureProfiles.MaxFramerate(CaptureProfile.Normal30));
            Assert.AreEqual(60, CaptureProfiles.MaxFramerate(CaptureProfile.Normal60));
            Assert.AreEqual(new Vector2Int(1280, 1280), CaptureProfiles.Resolution(CaptureProfile.Detail60));
            Assert.AreEqual(30, CaptureProfiles.MaxFramerate(CaptureProfile.LowLight));
            Assert.AreEqual(30, CaptureProfiles.MaxFramerate(CaptureProfile.Thermal30));
            Assert.AreEqual(CaptureProfile.Normal60, CaptureProfiles.Nominal(ScanMode.Active));
            Assert.AreEqual(CaptureProfile.Detail60, CaptureProfiles.Nominal(ScanMode.Detail));
            Assert.AreEqual(CaptureProfile.Normal30, CaptureProfiles.Nominal(ScanMode.Idle));
        }

        [Test]
        public void ModeChange_TransitionsOnlyAfterDwell()
        {
            var g = new ProfileGovernor();
            Assert.IsFalse(g.Update(0.0, ScanMode.Idle, 30, 30, false));
            Assert.IsFalse(g.Update(1.0, ScanMode.Active, 30, 30, false));   // dwell 3 s not elapsed
            Assert.IsFalse(g.Update(2.9, ScanMode.Active, 30, 30, false));
            Assert.IsTrue(g.Update(3.0, ScanMode.Active, 30, 30, false));
            Assert.AreEqual(CaptureProfile.Normal60, g.Current);
            Assert.AreEqual(1, g.Transitions);
            Assert.IsFalse(g.Update(4.0, ScanMode.Idle, 50, 50, false));     // flapping suppressed
            Assert.IsTrue(g.Update(6.0, ScanMode.Idle, 50, 50, false));
            Assert.AreEqual(CaptureProfile.Normal30, g.Current);
        }

        [Test]
        public void Underdelivery_ForThreeSeconds_GoesLowLight_NotSooner()
        {
            var g = new ProfileGovernor(CaptureProfile.Normal60);
            g.Update(0.0, ScanMode.Active, 60, 60, false);
            // measured 49.6 fps at a 60 request: 49.6 > 0.8*60 = 48 → not under-delivery
            for (double t = 0.5; t < 6; t += 0.5) Assert.IsFalse(g.Update(t, ScanMode.Active, 49.6, 49.6, false));
            Assert.AreEqual(CaptureProfile.Normal60, g.Current);
            // low light: 40 fps < 48
            Assert.IsFalse(g.Update(6.5, ScanMode.Active, 40, 40, false));
            Assert.IsFalse(g.Update(8.0, ScanMode.Active, 40, 40, false));
            Assert.IsFalse(g.Update(9.4, ScanMode.Active, 40, 40, false));
            Assert.IsTrue(g.Update(9.5, ScanMode.Active, 40, 40, false));
            Assert.AreEqual(CaptureProfile.LowLight, g.Current);
            Assert.AreEqual(1, g.Fallbacks);
        }

        [Test]
        public void UnmeasuredFps_NeverCountsAsUnderdelivery()
        {
            var g = new ProfileGovernor(CaptureProfile.Normal60);
            for (double t = 0; t < 20; t += 0.5) Assert.IsFalse(g.Update(t, ScanMode.Active, 0, 0, false));
            Assert.AreEqual(CaptureProfile.Normal60, g.Current);
        }

        [Test]
        public void LowLightHold_ExpiresThenReturnsToNominal_WithBackoff()
        {
            var g = new ProfileGovernor(CaptureProfile.Normal60);
            g.Update(0.0, ScanMode.Active, 60, 60, false);
            double t = 0.5; while (!g.Update(t, ScanMode.Active, 40, 40, false)) t += 0.5;
            Assert.AreEqual(CaptureProfile.LowLight, g.Current);
            double fell = t;
            // healthy 30 fps at the 30 request; hold 10 s must pass first
            while (t < fell + 9.4) { t += 0.5; Assert.IsFalse(g.Update(t, ScanMode.Active, 30, 30, false)); }
            while (!g.Update(t, ScanMode.Active, 30, 30, false)) { t += 0.5; Assert.Less(t, fell + 12); }
            Assert.AreEqual(CaptureProfile.Normal60, g.Current);
            Assert.AreEqual(ProfileGovernor.FallbackHoldSeconds * 2, g.CurrentHoldSeconds); // next hold doubles
        }

        [Test]
        public void Thermal_ForcesThermal30_AndHolds()
        {
            var g = new ProfileGovernor(CaptureProfile.Normal60);
            g.Update(0.0, ScanMode.Active, 60, 60, false);
            Assert.IsFalse(g.Update(1.0, ScanMode.Active, 60, 60, true));   // dwell still applies
            Assert.IsTrue(g.Update(3.0, ScanMode.Active, 60, 60, true));
            Assert.AreEqual(CaptureProfile.Thermal30, g.Current);
            Assert.AreEqual("thermal", g.LastReason);
            Assert.IsFalse(g.Update(6.5, ScanMode.Active, 30, 30, false));  // hold 10 s after the warning
            Assert.AreEqual(CaptureProfile.Thermal30, g.Current);
        }

        [Test]
        public void DetailRequest_SelectsDetail60()
        {
            var g = new ProfileGovernor(CaptureProfile.Normal60);
            g.Update(0.0, ScanMode.Active, 60, 60, false);
            Assert.IsTrue(g.Update(3.5, ScanMode.Detail, 50, 50, false));
            Assert.AreEqual(CaptureProfile.Detail60, g.Current);
            Assert.AreEqual(CaptureProfiles.Spec(CaptureProfile.Detail60).Resolution, new Vector2Int(1280, 1280));
        }

        [Test]
        public void PipelineGovernor_UsesDeliveredFpsFromCadence()
        {
            var p = SensorTestKit.NewPipeline(CaptureProfile.Normal60);
            const long Ms = 1_000_000L; long t0 = 2_000_000_000L;
            SensorTestKit.FillPoses(p, t0 - 100 * Ms, t0 + 12_000 * Ms);
            // 25 fps delivered at a 60 request for 8 s → LowLight after warm-up (2 s) + 3 s
            bool applied = false; double appliedAt = -1;
            for (int i = 0; i < 200 && !applied; i++)
            {
                long t = t0 + i * 40 * Ms;
                SensorTestKit.Feed(p, CameraEye.Left, t); SensorTestKit.Feed(p, CameraEye.Right, t);
                applied = p.UpdateGovernor(i * 0.04, t + 30 * Ms, ScanMode.Active, false);
                if (applied) appliedAt = i * 0.04;
            }
            Assert.IsTrue(applied);
            Assert.AreEqual(CaptureProfile.LowLight, p.Profile);
            Assert.GreaterOrEqual(appliedAt, 5.0);
            Assert.Less(appliedAt, 6.0);
        }
    }
}
