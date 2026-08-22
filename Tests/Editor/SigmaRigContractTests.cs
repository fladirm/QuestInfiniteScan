using System;
using System.Collections.Generic;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaRigContractTests
    {
        [Test]
        public void UnixTimestampPreservesSourceTimeExactly()
        {
            DateTime source = DateTime.UnixEpoch.AddTicks(17_777_777_123_450_000L);
            RigTimestamp timestamp = RigTimestamp.FromUnixDateTime(source);

            Assert.That(timestamp.SourceDomain, Is.EqualTo(RigClockDomain.UnixRealtime));
            Assert.That(timestamp.SourceNanoseconds,
                Is.EqualTo((source.Ticks - DateTime.UnixEpoch.Ticks) * 100L));
            Assert.That(timestamp.UnixNanoseconds, Is.EqualTo(timestamp.SourceNanoseconds));
            Assert.That(timestamp.MappingUncertaintyNanoseconds, Is.Zero);
        }

        [Test]
        public void XrClockAnchorUsesBracketMidpointAndCarriesUncertainty()
        {
            var xrSamples = new Queue<double>(new[] { 100.000, 100.002 });
            DateTime unix = DateTime.UnixEpoch.AddSeconds(1_000.001);
            var mapper = new RigClockMapper(() => xrSamples.Dequeue(), () => unix);

            Assert.That(mapper.TryCaptureAnchor(), Is.True);
            Assert.That(mapper.TryMapXrTimestamp(100_011_000_000L,
                out RigTimestamp mapped), Is.True);
            Assert.That(mapped.UnixNanoseconds, Is.EqualTo(1_000_011_000_000L));
            Assert.That(mapped.MappingUncertaintyNanoseconds,
                Is.InRange(1_000_000L, 1_100_000L));
        }

        [Test]
        public void XrClockAnchorFailsClosedWhenSamplingBracketIsTooWide()
        {
            var xrSamples = new Queue<double>(new[] { 100.000, 100.020 });
            var mapper = new RigClockMapper(() => xrSamples.Dequeue(),
                () => DateTime.UnixEpoch.AddSeconds(1_000));

            Assert.That(mapper.TryCaptureAnchor(), Is.False);
            Assert.That(mapper.TryMapXrTimestamp(100_001_000_000L, out _), Is.False);
        }

        [Test]
        public void DepthFovProducesDeliveredPixelIntrinsics()
        {
            const float left = -0.5f;
            const float right = 0.6f;
            var fov = new XRFov(left, right, 0.55f, -0.45f);
            var resolution = new Vector2Int(320, 240);

            RigIntrinsics intrinsics = RigCalibrationMath.FromDepthFov(fov, resolution);

            Assert.That(intrinsics.IsValid, Is.True);
            Assert.That(intrinsics.FocalLength.x,
                Is.EqualTo(320f / (Mathf.Tan(right) - Mathf.Tan(left))).Within(0.0001f));
            Assert.That(intrinsics.PrincipalPoint.x,
                Is.EqualTo(-intrinsics.FocalLength.x * Mathf.Tan(left)).Within(0.0001f));
            Assert.That(intrinsics.ImageResolution, Is.EqualTo(resolution));
        }

        [Test]
        public void CalibrationSignatureChangesWithConeGeometry()
        {
            var resolution = new Vector2Int(320, 240);
            RigIntrinsics first = RigCalibrationMath.FromDepthFov(
                new XRFov(-0.5f, 0.5f, 0.5f, -0.5f), resolution);
            RigIntrinsics changed = RigCalibrationMath.FromDepthFov(
                new XRFov(-0.5f, 0.51f, 0.5f, -0.5f), resolution);

            Assert.That(first.Signature, Is.Not.EqualTo(changed.Signature));
        }

        [Test]
        public void ConeReferenceCarriesRayDifferentialsAndSolidAngle()
        {
            RigIntrinsics intrinsics = RigCalibrationMath.FromDepthFov(
                new XRFov(-0.5f, 0.5f, 0.5f, -0.5f), new Vector2Int(320, 240));

            RigCalibrationMath.ConeRayReference cone =
                RigCalibrationMath.ConeRayAtPixel(intrinsics, 160, 120);

            Assert.That(cone.Center.magnitude, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(Vector3.Dot(cone.Center, cone.DifferentialX),
                Is.EqualTo(0f).Within(1e-6f));
            Assert.That(Vector3.Dot(cone.Center, cone.DifferentialY),
                Is.EqualTo(0f).Within(1e-6f));
            Assert.That(cone.HalfAngleX, Is.GreaterThan(0f));
            Assert.That(cone.HalfAngleY, Is.GreaterThan(0f));
            Assert.That(cone.SolidAngle, Is.GreaterThan(0f));
        }

        [Test]
        public void FiniteConeFootprintExpandsWithRangeAndGrazingIncidence()
        {
            RigIntrinsics intrinsics = RigCalibrationMath.FromDepthFov(
                new XRFov(-0.5f, 0.5f, 0.5f, -0.5f), new Vector2Int(320, 240));
            RigCalibrationMath.ConeRayReference cone =
                RigCalibrationMath.ConeRayAtPixel(intrinsics, 160, 120);

            Assert.That(ConeProjectionMath.TrySurfaceFootprint(cone.Center,
                cone.DifferentialX, cone.DifferentialY, 1f, -cone.Center,
                out MetricConeFootprint near), Is.True);
            Assert.That(ConeProjectionMath.TrySurfaceFootprint(cone.Center,
                cone.DifferentialX, cone.DifferentialY, 4f, -cone.Center,
                out MetricConeFootprint far), Is.True);
            Vector3 grazingNormal = Vector3.Cross(cone.Center, Vector3.up).normalized;
            grazingNormal = (grazingNormal * 0.98f - cone.Center * 0.2f).normalized;
            Assert.That(ConeProjectionMath.TrySurfaceFootprint(cone.Center,
                cone.DifferentialX, cone.DifferentialY, 1f, grazingNormal,
                out MetricConeFootprint grazing), Is.True);

            Assert.That(far.AreaSquareMeters / near.AreaSquareMeters,
                Is.EqualTo(16f).Within(0.02f));
            Assert.That(grazing.AreaSquareMeters, Is.GreaterThan(near.AreaSquareMeters));
        }
    }
}
