using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaPoseGaugeTests
    {
        [Test]
        public void ExactGpuResultSelectsMinimumMagnitudeMeetPoint()
        {
            long[] twist =
            {
                SigmaNumericDomain.Quantize(0.01),
                SigmaNumericDomain.Quantize(-0.02), 0L,
                SigmaNumericDomain.Quantize(0.01), 0L, 0L
            };
            var words = new NativeArray<uint>(16, Allocator.Temp);
            try
            {
                words[0] = 1u;
                for (int component = 0; component < 6; ++component)
                {
                    ulong raw = unchecked((ulong)twist[component]);
                    words[4 + component * 2] = unchecked((uint)raw);
                    words[5 + component * 2] = unchecked((uint)(raw >> 32));
                }

                SigmaPoseGaugeState gauge = SigmaPoseGaugeState.FromGpu(words,
                    7u, 9u);

                Assert.That(gauge.Resolved, Is.True);
                Assert.That(gauge.CalibrationEpoch, Is.EqualTo(7u));
                Assert.That(gauge.Revision, Is.EqualTo(9u));
                for (int component = 0; component < 6; ++component)
                    Assert.That(gauge.Raw(component), Is.EqualTo(twist[component]));
            }
            finally { words.Dispose(); }
            Assert.That(SigmaPoseGaugeState.MinimumMagnitude(-2L, 4L), Is.Zero);
            Assert.That(SigmaPoseGaugeState.MinimumMagnitude(2L, 4L), Is.EqualTo(2L));
            Assert.That(SigmaPoseGaugeState.MinimumMagnitude(-4L, -2L), Is.EqualTo(-2L));
        }

        [Test]
        public void UnresolvedMeetIsIdentityAndResolvedGaugePreservesRigExtrinsics()
        {
            var words = new NativeArray<uint>(16, Allocator.Temp);
            try
            {
                words[1] = 1u;
                Assert.That(SigmaPoseGaugeState.FromGpu(words, 3u, 4u).Resolved,
                    Is.False);
            }
            finally { words.Dispose(); }

            var gauge = new SigmaPoseGaugeState(3u, 4u, true,
                SigmaNumericDomain.Quantize(0.02), 0L, 0L,
                0L, SigmaNumericDomain.Quantize(0.01), 0L);
            Pose left = new(new Vector3(1f, 2f, 3f),
                Quaternion.Euler(4f, 5f, 6f));
            Pose right = new(left.position + left.rotation * Vector3.right * 0.064f,
                left.rotation);
            Matrix4x4 rawRelative = Matrix(left).inverse * Matrix(right);
            Matrix4x4 correctedRelative = Matrix(gauge.Apply(left, left)).inverse *
                Matrix(gauge.Apply(left, right));

            Assert.That((rawRelative.GetColumn(3) -
                correctedRelative.GetColumn(3)).magnitude, Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(rawRelative.rotation,
                correctedRelative.rotation), Is.LessThan(1e-4f));
        }

        [Test]
        public void VulkanPoseGaugeKernelIsPresent()
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaPoseGauge");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.FindKernel("BuildPoseGaugePartials"),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("ReducePoseGauge"),
                Is.GreaterThanOrEqualTo(0));
        }

        private static Matrix4x4 Matrix(Pose pose) => Matrix4x4.TRS(
            pose.position, pose.rotation, Vector3.one);
    }
}
