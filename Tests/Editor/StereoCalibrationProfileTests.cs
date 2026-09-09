using System;
using System.Reflection;
using Genesis.RoomScan;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class StereoCalibrationProfileTests
    {
        private StereoCalibrationProfile _profile;

        [SetUp]
        public void CreateProfile()
        {
            _profile = ScriptableObject.CreateInstance<StereoCalibrationProfile>();
            // Synthetic test evidence only; never a host/device calibration.
            _profile.Source = "unit-test synthetic bounds";
            _profile.DepthLeft = _profile.DepthRight = new Vector4(0.000005f, 0.000001f, 0.000005f, 1f);
            _profile.Plane = new Vector4(0.01f, 0.0001f, 0f, 1f);
            _profile.RgbLeft = _profile.RgbRight = new Vector4(0.001f, 0.001f, 0.001f, 1f);
        }

        [TearDown]
        public void DestroyProfile() => UnityEngine.Object.DestroyImmediate(_profile);

        [Test]
        public void BoundsRequireProvenanceAndReservedPlaneComponent()
        {
            Assert.That(_profile.TryValidate(out _), Is.True);
            _profile.Source = " ";
            Assert.That(_profile.TryValidate(out string reason), Is.False);
            Assert.That(reason, Does.Contain("source/provenance"));
            _profile.Source = "synthetic";
            _profile.Plane.z = 1f;
            Assert.That(_profile.TryValidate(out reason), Is.False);
            Assert.That(reason, Does.Contain("reserved"));
        }

        [TestCase(nameof(StereoCalibrationProfile.DepthLeft))]
        [TestCase(nameof(StereoCalibrationProfile.DepthRight))]
        [TestCase(nameof(StereoCalibrationProfile.Plane))]
        [TestCase(nameof(StereoCalibrationProfile.RgbLeft))]
        [TestCase(nameof(StereoCalibrationProfile.RgbRight))]
        public void EveryStreamRequiresFiniteNonnegativeCalibratedBounds(string name)
        {
            FieldInfo field = typeof(StereoCalibrationProfile).GetField(name);
            foreach (Vector4 invalid in new[]
            {
                Vector4.zero, new Vector4(-1f, 0f, 0f, 1f),
                new Vector4(0f, float.NaN, 0f, 1f),
                new Vector4(float.PositiveInfinity, 0f, 0f, 1f),
                new Vector4(0f, 0f, 0f, 0.5f)
            })
            {
                field.SetValue(_profile, invalid);
                Assert.That(_profile.TryValidate(out string reason), Is.False, name);
                Assert.That(reason, Does.Contain(name));
            }
        }

        [Test]
        public void StartupAndPresentationUseTheSameProfile()
        {
            var host = new GameObject("Calibration binding");
            try
            {
                DepthCapture capture = host.AddComponent<DepthCapture>();
                Assert.Throws<InvalidOperationException>(capture.RequireValidCalibration);
                Assert.That(capture.TryGetFlowerPlaneBounds(out _), Is.False);
                typeof(DepthCapture).GetField("calibrationProfile",
                    BindingFlags.Instance | BindingFlags.NonPublic).SetValue(capture, _profile);
                Assert.DoesNotThrow(capture.RequireValidCalibration);
                Assert.That(capture.TryGetFlowerPlaneBounds(out Vector2 bounds), Is.True);
                Assert.That(bounds.x, Is.GreaterThan(_profile.Plane.x));
                Assert.That(bounds.y, Is.GreaterThan(_profile.Plane.y));
                _profile.RgbRight.w = 0f;
                Assert.Throws<InvalidOperationException>(capture.RequireValidCalibration);
                Assert.That(capture.TryGetFlowerPlaneBounds(out _), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void MetricsDoNotCallAcceptedWithR3UniqueOrMissingCalibrationRawDepthLoss()
        {
            var values = new uint[MerkabaGpuTimestamps.RefineMetricValueCount];
            int start = MerkabaGpuTimestamps.RefineMetricCount;
            values[start + 6] = 7;
            values[start + 8] = 64;
            values[start + 9] = 64;
            values[start + 15] = 3;
            string result = MerkabaGpuTimestamps.FormatRefineBin(values, 1);
            Assert.That(result, Does.Contain("acceptedWithR3=7"));
            Assert.That(result, Does.Contain("sourceDepthValid=64"));
            Assert.That(result, Does.Contain("calibrationInvalid=64"));
            Assert.That(result, Does.Contain("nonUniqueHypotheses=3"));
        }
    }
}
