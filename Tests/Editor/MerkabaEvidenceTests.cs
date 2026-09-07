using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaEvidenceTests
    {
        [Test]
        public void KernelStateAbi_RemainsExactlySixteenBytes()
        {
            Assert.That(Marshal.SizeOf<KernelState>(), Is.EqualTo(16));
        }

        [Test]
        public void SurfacePlane_OctahedralNormalRoundTripIsBounded()
        {
            float3[] normals =
            {
                new(1f, 0f, 0f), new(0f, 1f, 0f), new(0f, 0f, 1f),
                math.normalize(new float3(0.17f, -0.63f, 0.76f)),
                math.normalize(new float3(-0.42f, 0.88f, -0.21f))
            };
            foreach (float3 expected in normals)
            {
                uint flags = KernelState.SetSurfacePlane(0u, expected, 0f);
                KernelState.DecodeSurfacePlane(flags, out float3 decoded,
                    out _);
                Assert.That(math.abs(math.dot(math.normalize(expected),
                    decoded)), Is.GreaterThan(0.99999f));
            }
        }

        [Test]
        public void SurfacePlane_OppositeFreeSidePreservesCanonicalPlane()
        {
            float3 normal = math.normalize(new float3(-0.3f, 0.7f, 0.2f));
            uint forward = KernelState.SetSurfacePlane(0u, normal, 0.011f);
            uint reverse = KernelState.SetSurfacePlane(0u, -normal, -0.011f);
            Assert.That(reverse ^ forward,
                Is.EqualTo(MerkabaConstants.SurfacePlaneFreeSideFlag));
        }

        [Test]
        public void SurfacePlane_OffsetEndpointsAndSubMillimetreValueRoundTrip()
        {
            foreach (float expected in new[] { -0.025f, -0.0073f, 0f,
                         0.0091f, 0.025f })
            {
                uint flags = KernelState.SetSurfacePlane(0u,
                    new float3(0f, 0f, 1f), expected);
                KernelState.DecodeSurfacePlane(flags, out _, out float actual);
                Assert.That(actual, Is.EqualTo(expected).Within(
                    MerkabaConstants.SurfacePlaneOffsetRange / 127f * 0.51f));
            }
        }

        [Test]
        public void SurfacePlane_ClearPreservesUnrelatedFlagsAndLegacyIsInvalid()
        {
            uint preserved = MerkabaConstants.OccupiedFlag |
                MerkabaConstants.R1SeedFlag;
            uint flags = KernelState.SetSurfacePlane(preserved,
                math.normalize(new float3(1f, 2f, 3f)), 0.004f);
            Assert.That(KernelState.HasSurfacePlane(flags), Is.True);
            Assert.That(KernelState.ClearSurfacePlane(flags), Is.EqualTo(preserved));
            Assert.That(KernelState.HasSurfacePlane(7u << 2), Is.False);
        }

    }
}
