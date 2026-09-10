using System;
using Genesis.RoomScan;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class StereoObservationPolicyTests
    {
        [Test]
        public void PlaneEnclosureNeedsNoExternalCalibrationAsset()
        {
            var host = new GameObject("Realtime observation binding");
            try
            {
                DepthCapture capture = host.AddComponent<DepthCapture>();
                Assert.That(capture.TryGetFlowerPlaneBounds(out Vector2 bounds), Is.True);
                Assert.That(bounds.x, Is.GreaterThanOrEqualTo(2.0 * Math.Sqrt(18.0) / 1023.0));
                Assert.That(bounds.y, Is.GreaterThanOrEqualTo((double)MerkabaConstants.LatticeStep / 254.0));
                Assert.That(DepthCapture.ObservationPlaneBounds, Is.EqualTo(new Vector4(0,0,0,1)));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void ExcavationClearanceIsSeparateFromMetricAndRgbRepresentation()
        {
            Assert.That(DepthCapture.ObservationDepthBounds,
                Is.EqualTo(new Vector4(0,0,MerkabaConstants.LatticeStep,1)));
            Assert.That(DepthCapture.ObservationRgbBounds,
                Is.EqualTo(new Vector4(0.5f/255f,0.5f/255f,0.5f/255f,1)));
        }

        [Test]
        public void MetricsDistinguishMeasuredDepthFromMetricCorrection()
        {
            var values = new uint[MerkabaGpuTimestamps.RefineMetricValueCount];
            int start = MerkabaGpuTimestamps.RefineMetricCount;
            values[start] = 64;
            values[start + 5] = 7;
            values[start + 6] = 2;
            values[start + 7] = 9;
            string result = MerkabaGpuTimestamps.FormatRefineBin(values, 1);
            Assert.That(result, Does.Contain("planeAccepted=64"));
            Assert.That(result, Does.Contain("measuredDepth=7"));
            Assert.That(result, Does.Contain("rgbCorrection=2"));
            Assert.That(result, Does.Contain("accepted=9"));
        }
    }
}
