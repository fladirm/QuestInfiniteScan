using System;
using FinalScan.Host;
using FinalScan.Platform.Native;
using FinalScan.Render;
using FinalScan.Residency;
using NUnit.Framework;
using UnityEngine;
using RenderMode = FinalScan.Render.RenderMode;

namespace FinalScan.Tests
{
    /// <summary>Pure helpers of the C02..C07 host side (no device, no native).</summary>
    public class HostHelpersTests
    {
        [Test]
        public void HostConfig_MatchesNativeStructSize()
        {
            FinalScanHostNative.AssertLayout();
            Assert.AreEqual(100, FinalScanHostNative.HostConfig.SizeBytes);
            var c = FinalScanHostNative.HostConfig.Create();
            Assert.AreEqual(100u, c.structSize);
            c.SetInFlightMax(FinalScanHostNative.JobClass.Cold, 7);
            c.SetQuantumUs(FinalScanHostNative.JobClass.Scan, 2000);
            Assert.AreEqual(7u, c.GetInFlightMax(FinalScanHostNative.JobClass.Cold));
            Assert.AreEqual(2000u, c.GetQuantumUs(FinalScanHostNative.JobClass.Scan));
            Assert.AreEqual(0u, c.GetInFlightMax(FinalScanHostNative.JobClass.Publish));
        }

        [Test]
        public void HostMath_ColumnMajorRoundTripsAndOrdersColumns()
        {
            var m = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), new Vector3(1, 2, 1));
            var f = new float[16];
            HostMath.ToColumnMajor(m, f);
            // element [c*4 + r] = M[r][c]: translation lives in column 3 → indices 12..14
            Assert.AreEqual(1f, f[12], 1e-6f); Assert.AreEqual(2f, f[13], 1e-6f); Assert.AreEqual(3f, f[14], 1e-6f); Assert.AreEqual(1f, f[15], 1e-6f);
            Assert.AreEqual(m.m10, f[1], 1e-6f);
            Matrix4x4 back = HostMath.FromColumnMajor(f);
            for (int i = 0; i < 16; i++) Assert.AreEqual(m[i], back[i], 1e-6f);
        }

        [Test]
        public void HostMath_FovTangentsKeepProviderSigns()
        {
            var t = new float[4];
            HostMath.FovTangents(-49f * Mathf.Deg2Rad, 45f * Mathf.Deg2Rad, 48f * Mathf.Deg2Rad, -50f * Mathf.Deg2Rad, t);
            Assert.Less(t[0], 0f); Assert.Greater(t[1], 0f); Assert.Greater(t[2], 0f); Assert.Less(t[3], 0f);
            Assert.AreEqual(Mathf.Tan(45f * Mathf.Deg2Rad), t[1], 1e-5f);
        }

        [Test]
        public void HostMath_GpuHeadroomAndPredictedDisplayTime()
        {
            Assert.AreEqual(3889, HostMath.GpuHeadroomUs(10.0), 1);
            Assert.Less(HostMath.GpuHeadroomUs(15.0), 0);
            Assert.AreEqual(13889, HostMath.GpuHeadroomUs(double.NaN), 1);   // no timing → full period
            Assert.AreEqual(100.0 + 1.0 / 72.0, HostMath.PredictedDisplayTime(100.0, 72f), 1e-9);
            Assert.AreEqual(100.0 + 1.0 / 90.0 + 0.002, HostMath.PredictedDisplayTime(100.0, 90f, 0.002), 1e-9);
            Assert.AreEqual(100.0 + 1.0 / 72.0, HostMath.PredictedDisplayTime(100.0, 0f), 1e-9);
        }

        [Test]
        public void RenderModeParser_AcceptsSetpropSpellings()
        {
            Assert.IsTrue(RenderModeParser.TryParse("xray", out var m) && m == RenderMode.XRay);
            Assert.IsTrue(RenderModeParser.TryParse(" X-Ray ", out m) && m == RenderMode.XRay);
            Assert.IsTrue(RenderModeParser.TryParse("radar", out m) && m == RenderMode.XRay);
            Assert.IsTrue(RenderModeParser.TryParse("plan", out m) && m == RenderMode.Plan);
            Assert.IsTrue(RenderModeParser.TryParse("0", out m) && m == RenderMode.Scan);
            Assert.IsFalse(RenderModeParser.TryParse("", out _));
            Assert.IsFalse(RenderModeParser.TryParse("bogus", out _));
            Assert.AreEqual("xray", RenderModeParser.Name(RenderMode.XRay));
            Assert.IsTrue(AndroidSystemProps.IsTruthy("1") && AndroidSystemProps.IsTruthy("TRUE") && !AndroidSystemProps.IsTruthy("0") && !AndroidSystemProps.IsTruthy(null));
            // synthetic fixture: value = kind + 1; unset/0/garbage never creates a world
            Assert.AreEqual(-1, AndroidSystemProps.ParseSyntheticKind(""));
            Assert.AreEqual(-1, AndroidSystemProps.ParseSyntheticKind("0"));
            Assert.AreEqual(-1, AndroidSystemProps.ParseSyntheticKind("x"));
            Assert.AreEqual(0, AndroidSystemProps.ParseSyntheticKind("1"));
            Assert.AreEqual(4, AndroidSystemProps.ParseSyntheticKind("5"));
            Assert.AreEqual(-1, AndroidSystemProps.ParseSyntheticKind("6"));
        }

        [Test]
        public void DepthFrameDedupe_DropsRepeatsAndNonMonotonic()
        {
            var d = new DepthFrameDedupe();
            Assert.IsFalse(d.Accept(false, 0));
            Assert.IsTrue(d.Accept(true, 1000));
            Assert.IsFalse(d.Accept(true, 1000));   // 72 Hz callback repeating a 25 Hz frame
            Assert.IsFalse(d.Accept(true, 1000));
            Assert.IsFalse(d.Accept(true, 900));    // non-monotonic
            Assert.IsTrue(d.Accept(true, 41000000));
            Assert.AreEqual(2, d.Accepted); Assert.AreEqual(2, d.Repeats); Assert.AreEqual(1, d.NonMonotonic); Assert.AreEqual(1, d.NoTimestamp);
        }

        [Test]
        public void SurfelRenderer_InitialArgsAndBucketLayout()
        {
            uint[] args = SurfelRenderer.InitialArgs(SurfelRenderer.PolygonFanVertexCount);
            Assert.AreEqual(SurfelRenderer.ArgsUintCount, args.Length);
            Assert.AreEqual(24u, args[0]); Assert.AreEqual(0u, args[1]); Assert.AreEqual(0u, args[3]);
            Assert.AreEqual(6u, args[4]); Assert.AreEqual(0u, args[5]);
            Assert.AreEqual(16, SurfelRenderer.AggregateArgsByteOffset);
            Assert.AreEqual(32, SurfelDrawAbi.DrawRecordStride);
        }

        [Test]
        public void SpinAcceptance_PassesCleanRunAndFailsOnEachInvariant()
        {
            SpinAcceptance.Result r = Run(frames: 2160, yawRate: 180f, orientation: 0, visible: 5000, frameMs: 13.9);
            Assert.IsTrue(r.Pass, string.Join(";", r.Reasons));
            Assert.Greater(r.TotalYawDeg, 5000);
            Assert.AreEqual(2160, r.Frames);
            StringAssert.Contains("\"verdict\":\"PASS\"", r.ToJson());

            r = Run(2160, 180f, orientation: 1, visible: 5000, frameMs: 13.9);
            Assert.IsFalse(r.Pass); StringAssert.Contains("orientationResidencyRequests", r.Reasons[0]);

            r = Run(2160, 180f, 0, visible: 0, frameMs: 13.9);
            Assert.IsFalse(r.Pass); StringAssert.Contains("zero visible", r.Reasons[0]);
            Assert.AreEqual(2160, r.ZeroVisibleWhileFacing);

            r = Run(2160, 180f, 0, 5000, frameMs: 25.0);
            Assert.IsFalse(r.Pass); StringAssert.Contains("20 ms", r.Reasons[0]);

            r = Run(2160, 1f, 0, 5000, 13.9);   // user did not spin
            Assert.IsFalse(r.Pass); StringAssert.Contains("insufficient rotation", r.Reasons[0]);

            // zero visible is only counted while facing geometry
            var s = new SpinAcceptance();
            for (int i = 0; i < 300; i++) s.Add(new SpinAcceptance.Sample { TimeSec = i / 72.0, YawRateDegPerSec = 400f, VisibleSurfels = 0, FrameMs = 10, FacingGeometry = false });
            Assert.AreEqual(0, s.Evaluate().ZeroVisibleWhileFacing);
            StringAssert.Contains("\"verdict\":\"FAIL\"", Run(10, 0, 0, 0, 10).ToJson());
        }

        static SpinAcceptance.Result Run(int frames, float yawRate, long orientation, long visible, double frameMs)
        {
            var s = new SpinAcceptance();
            for (int i = 0; i < frames; i++)
                s.Add(new SpinAcceptance.Sample { TimeSec = i * frameMs / 1000.0, YawRateDegPerSec = yawRate, OrientationRequests = orientation, RequestsThisFrame = 0, VisibleSurfels = visible, FrameMs = frameMs, FacingGeometry = true });
            return s.Evaluate();
        }

        [Test]
        public void ResidencyPrediction_HasNoOrientationInput()
        {
            // Position-only by construction (§12.1): the API surface takes Vector3 position/velocity only.
            var method = typeof(ResidencyPrediction).GetMethod(nameof(ResidencyPrediction.PredictCenter));
            foreach (var p in method.GetParameters()) Assert.AreNotEqual(typeof(Quaternion), p.ParameterType);
            Vector3 c = ResidencyPrediction.PredictCenter(Vector3.zero, new Vector3(10, 0, 0));
            Assert.AreEqual(1f, c.x, 1e-5f);   // clamped to 1 m lead
        }
    }
}
