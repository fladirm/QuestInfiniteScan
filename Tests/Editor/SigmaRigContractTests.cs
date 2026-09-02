using System;
using System.Collections.Generic;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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

        [Test]
        public void DepthIngressArmsExactlyOneFutureOwnedSnapshot()
        {
            var host = new GameObject("Depth ingress demand gate");
            try
            {
                var capture = host.AddComponent<DepthCapture>();
                capture.StartDepthCapture();

                Assert.That(capture.RequestNextDepthFrame(), Is.True);
                Assert.That(capture.RequestNextDepthFrame(), Is.False,
                    "A second provider callback may not be admitted while one " +
                    "owned snapshot request is outstanding.");

                capture.StopDepthCapture();
                Assert.That(capture.RequestNextDepthFrame(), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void FixedScanCadenceIsFiveHertzAndNeverCatchesUp()
        {
            double first = RoomScanner.NextScanAdmissionTime(100.0, 5f);
            Assert.That(first, Is.EqualTo(100.2)
                .Within(1e-12));

            // A late admission attempt starts one fresh interval from its actual
            // tick time even when scanner backpressure rejects the observation;
            // elapsed ticks are not replayed as a burst.
            double late = RoomScanner.NextScanAdmissionTime(101.0, 5f);
            Assert.That(late, Is.EqualTo(101.2)
                .Within(1e-12));
            Assert.That(late - first, Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void SpatialAnchorSelectorRoundTripsAndRejectsInvalidIdentity()
        {
            Guid uuid = Guid.NewGuid();
            string encoded = RoomAnchorManager.EncodeSpatialAnchorUuid(uuid);

            Assert.That(RoomAnchorManager.TryDecodeSpatialAnchorUuid(encoded,
                out Guid decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(uuid));
            Assert.That(RoomAnchorManager.TryDecodeSpatialAnchorUuid(
                "not-an-anchor", out _), Is.False);
            Assert.That(RoomAnchorManager.TryDecodeSpatialAnchorUuid(
                Guid.Empty.ToString("D"), out _), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RoomAnchorManager.EncodeSpatialAnchorUuid(Guid.Empty));
        }

        [Test]
        public void DurableRoomContentCannotAcquireAnUnrelatedNewAnchor()
        {
            Assert.That(RoomScanner.MayCreateNewSpatialAnchor(false), Is.True,
                "An empty durable namespace may establish its first room frame.");
            Assert.That(RoomScanner.MayCreateNewSpatialAnchor(true), Is.False,
                "Existing durable bytes must fail closed without their saved " +
                "spatial-anchor selector.");
        }

        [Test]
        public void AnchorReadinessRequiresTranslationAndRotationStability()
        {
            Vector3 position = new(1f, 2f, 3f);
            Quaternion rotation = Quaternion.Euler(2f, 3f, 4f);
            Assert.That(RoomAnchorManager.IsStablePoseStep(position, rotation,
                position + Vector3.right * 0.0005f,
                Quaternion.Euler(2f, 3.05f, 4f)), Is.True);
            Assert.That(RoomAnchorManager.IsStablePoseStep(position, rotation,
                position + Vector3.right * 0.002f, rotation), Is.False);
            Assert.That(RoomAnchorManager.IsStablePoseStep(position, rotation,
                position, Quaternion.Euler(2f, 3.2f, 4f)), Is.False);
        }

        [Test]
        public void DurableSupportQueryEnclosesNativeRoiFrustaAndPosePrior()
        {
            var texture = new Texture2D(1, 1);
            try
            {
                RigIntrinsics intrinsics = RigCalibrationMath.FromDepthFov(
                    new XRFov(-0.5f, 0.5f, 0.5f, -0.5f),
                    new Vector2Int(320, 320));
                var timestamp = new RigTimestamp(RigClockDomain.XrMonotonic,
                    1L, 1L, 0L);
                var left = new GpuImageView(RigStreamKind.Depth, RigEye.Left,
                    texture, 0, 1L, timestamp, new Pose(Vector3.zero,
                        Quaternion.identity), intrinsics,
                    GraphicsFormat.R32_SFloat,
                    RigDepthEncoding.ProjectionDepth01,
                    new Vector2(0.2f, 5f));
                var right = new GpuImageView(RigStreamKind.Depth, RigEye.Right,
                    texture, 1, 1L, timestamp, new Pose(
                        new Vector3(0.07f, 0f, 0f), Quaternion.identity),
                    intrinsics, GraphicsFormat.R32_SFloat,
                    RigDepthEncoding.ProjectionDepth01,
                    new Vector2(0.2f, 5f));

                Assert.That(SigmaQuerySupportPlan.TryBuildSensorQueryBounds(
                    left, right, Matrix4x4.identity, 0.03f, 2f,
                    out SigmaQ48Bounds3 bounds), Is.True);
                Assert.That(
                    SigmaQuerySupportPlan.TryBuildSensorQueryBoundsForAperture(
                        left, right, Matrix4x4.identity, 0.03f, 2f,
                        Vector2Int.zero, new Vector2Int(320, 320),
                        out SigmaQ48Bounds3 fullBounds), Is.True);
                long near = SigmaNumericDomain.Quantize(0.2);
                long far = SigmaNumericDomain.Quantize(5.0);
                Assert.That(bounds.LowerZ, Is.LessThanOrEqualTo(near));
                Assert.That(bounds.UpperZ, Is.GreaterThanOrEqualTo(far));
                Assert.That(bounds.LowerX,
                    Is.LessThan(SigmaNumericDomain.Quantize(-2.0)));
                Assert.That(bounds.UpperX,
                    Is.GreaterThan(SigmaNumericDomain.Quantize(2.0)));
                Assert.That(bounds.LowerX, Is.GreaterThan(fullBounds.LowerX));
                Assert.That(bounds.UpperX, Is.LessThan(fullBounds.UpperX));
                Assert.That(bounds.LowerY, Is.GreaterThan(fullBounds.LowerY));
                Assert.That(bounds.UpperY, Is.LessThan(fullBounds.UpperY));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [TestCase(true, false, true, false, 0, false, true)]
        [TestCase(true, false, true, false, 1, false, false)]
        [TestCase(true, false, true, false, 0, true, false)]
        [TestCase(true, false, true, true, 0, false, false)]
        [TestCase(true, false, false, false, 0, false, false)]
        public void ScanAdmissionRequiresAnEmptyTerminalNativePipeline(
            bool initialized, bool disposed, bool running, bool faulted,
            int pending, bool inFlight, bool expected)
        {
            Assert.That(SigmaInverseController.ScheduledObservationReady(
                initialized, disposed, running, faulted, pending, inFlight),
                Is.EqualTo(expected));
        }

        [Test]
        public void LatestPcaPairMatchesOwnedDepthWithoutFusingFourLeaves()
        {
            RigLatestSnapshotMatchResult result =
                RigLatestSnapshotMatcher.Match(
                    TimestampAtMilliseconds(1_000),
                    TimestampAtMilliseconds(1_012),
                    TimestampAtMilliseconds(1_008, 1),
                    20_000_000L, 35_000_000L, 5_000_000L);
            RigLatestSnapshotMatchResult reversed =
                RigLatestSnapshotMatcher.Match(
                    TimestampAtMilliseconds(1_012),
                    TimestampAtMilliseconds(1_000),
                    TimestampAtMilliseconds(1_008, 1),
                    20_000_000L, 35_000_000L, 5_000_000L);

            Assert.That(result.Disposition,
                Is.EqualTo(RigLatestSnapshotMatch.Ready));
            Assert.That(result.Rejection,
                Is.EqualTo(RigFrameRejectionReason.None));
            Assert.That(result.RgbDeltaNanoseconds, Is.EqualTo(12_000_000L));
            Assert.That(result.RgbDepthDeltaNanoseconds, Is.EqualTo(2_000_000L));
            Assert.That(reversed.Disposition, Is.EqualTo(result.Disposition));
            Assert.That(reversed.RgbDeltaNanoseconds,
                Is.EqualTo(result.RgbDeltaNanoseconds));
            Assert.That(reversed.RgbDepthDeltaNanoseconds,
                Is.EqualTo(result.RgbDepthDeltaNanoseconds));
        }

        [Test]
        public void LatestPcaPairWaitsThenExpiresAnUnmatchableDepthSnapshot()
        {
            RigLatestSnapshotMatchResult waiting =
                RigLatestSnapshotMatcher.Match(
                    TimestampAtMilliseconds(1_000),
                    TimestampAtMilliseconds(1_060),
                    TimestampAtMilliseconds(1_030),
                    20_000_000L, 35_000_000L, 5_000_000L);
            RigLatestSnapshotMatchResult expired =
                RigLatestSnapshotMatcher.Match(
                    TimestampAtMilliseconds(1_090),
                    TimestampAtMilliseconds(1_100),
                    TimestampAtMilliseconds(1_030),
                    20_000_000L, 35_000_000L, 5_000_000L);

            Assert.That(waiting.Disposition,
                Is.EqualTo(RigLatestSnapshotMatch.Waiting));
            Assert.That(expired.Disposition,
                Is.EqualTo(RigLatestSnapshotMatch.DiscardDepth));
            Assert.That(expired.Rejection & RigFrameRejectionReason.Stale,
                Is.EqualTo(RigFrameRejectionReason.Stale));
            Assert.That(expired.Rejection &
                RigFrameRejectionReason.RgbDepthDeltaExceeded,
                Is.EqualTo(RigFrameRejectionReason.RgbDepthDeltaExceeded));
        }

        private static RigTimestamp TimestampAtMilliseconds(long milliseconds,
            long uncertaintyMilliseconds = 0L)
        {
            long nanoseconds = milliseconds * 1_000_000L;
            return new RigTimestamp(RigClockDomain.UnixRealtime, nanoseconds,
                nanoseconds, uncertaintyMilliseconds * 1_000_000L);
        }
    }
}
