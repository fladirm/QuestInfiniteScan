using System;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaGaugeRefinementTests
    {
        private const int TransitionCount =
            SigmaDecodedPage.SampleCount * 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt2
        {
            public uint X;
            public uint Y;

            public UInt2(uint x, uint y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt4
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;

            public UInt4(uint x, uint y, uint z, uint w)
            {
                X = x;
                Y = y;
                Z = z;
                W = w;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GaugeRequestGpu
        {
            public UInt4 Control;
            public UInt4 Region;
            public UInt4 Metric;
            public UInt4 Evidence;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BoundsGpu
        {
            public UInt2 Lo;
            public UInt2 Hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CertificateGpu
        {
            public UInt4 Identity;
            public UInt4 Range;
            public UInt2 SampleMask;
            public UInt2 Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ConstraintBlockGpu
        {
            public UInt4 Counts;
            public UInt4 Proof;
        }

        [TestCase(SigmaGaugeAxis.X, SigmaGaugeDirection.Positive, 2, 3)]
        [TestCase(SigmaGaugeAxis.X, SigmaGaugeDirection.Negative, 5, 3)]
        [TestCase(SigmaGaugeAxis.Y, SigmaGaugeDirection.Positive, 2, 4)]
        [TestCase(SigmaGaugeAxis.Y, SigmaGaugeDirection.Negative, 6, 4)]
        public void ExactSeparableGaugeRoundTripsEveryCanonicalLane(
            SigmaGaugeAxis axis, SigmaGaugeDirection direction,
            int sourceAxisBlock, int spanBlocks)
        {
            SigmaGaugeMap map = axis == SigmaGaugeAxis.X
                ? new SigmaGaugeMap(sourceAxisBlock, 3, axis, direction,
                    spanBlocks)
                : new SigmaGaugeMap(3, sourceAxisBlock, axis, direction,
                    spanBlocks);
            SigmaS16[] source = BuildPage(map);

            SigmaS16[] transformed = SigmaGaugeRefinement.Apply(source, map);
            SigmaS16[] restored = SigmaGaugeRefinement.ApplyInverse(
                transformed, map);

            Assert.That(restored, Is.EqualTo(source));
            for (int sample = 0; sample < source.Length; ++sample)
            {
                if (!SigmaGaugeRefinement.TryMapRetainedSample(sample, map,
                        out int target))
                    continue;
                Assert.That(transformed[target], Is.EqualTo(source[sample]),
                    $"retained sample {sample} was not transported bit-exactly");
            }
        }

        [Test]
        public void GaugeRequiresTwoExactNullTailBands()
        {
            var map = new SigmaGaugeMap(2, 2, SigmaGaugeAxis.X,
                SigmaGaugeDirection.Positive, 3);
            SigmaS16[] source = BuildPage(map);
            int forbiddenSample = 12 * SigmaDecodedPage.PageSize + 3 * 8;
            source[forbiddenSample] = Value(forbiddenSample);

            Assert.That(SigmaGaugeRefinement.TerminalBandsAreNull(source, map),
                Is.False);
            Assert.Throws<System.InvalidOperationException>(() =>
                SigmaGaugeRefinement.Apply(source, map));
        }

        [Test]
        public void RawProofBlockTransportUsesOnlyActualMappedSamples()
        {
            var map = new SigmaGaugeMap(2, 3, SigmaGaugeAxis.X,
                SigmaGaugeDirection.Positive, 3);
            int sourceBlock = 3 * 8 + 2;
            int[] targets = SigmaGaugeRefinement.TargetBlocksForSourceBlock(
                sourceBlock, map);

            Assert.That(targets, Is.EqualTo(new[] { sourceBlock,
                sourceBlock + 1 }));
            int terminalBlock = 3 * 8 + 4;
            Assert.That(SigmaGaugeRefinement.TargetBlocksForSourceBlock(
                terminalBlock, map), Is.Empty);
        }

        [Test]
        public void ReproductionDemandRequiresIndependentProofAndExactExcess()
        {
            var block = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            long mass = SigmaNumericDomain.FromInteger(8);
            for (int y = 0; y < 8; ++y)
            for (int x = 0; x < 8; ++x)
            {
                long position = SigmaNumericDomain.Quantize(
                    x == 3 ? 0.25 : 0.0);
                block[y * 8 + x] = SigmaGeometryReadout.LiftFixture(mass,
                    position, 0L, 0L);
            }
            int coordinate = SigmaGeneratedAlgebra.GeometryRows[1];

            Assert.That(SigmaGaugeRefinement.HasProjectiveReproductionDemand(
                block, coordinate, 0L, false, out _), Is.False);
            Assert.That(SigmaGaugeRefinement.HasProjectiveReproductionDemand(
                block, coordinate, SigmaNumericDomain.Quantize(0.3), true,
                out long below), Is.False);
            Assert.That(below, Is.GreaterThan(0L));
            Assert.That(SigmaGaugeRefinement.HasProjectiveReproductionDemand(
                block, coordinate, SigmaNumericDomain.Quantize(0.1), true,
                out long above), Is.True);
            Assert.That(above, Is.GreaterThan(
                SigmaNumericDomain.Quantize(0.1)));
        }

        [Test]
        public void VulkanGaugeMatchesOracleAndTransportsProofAndSingularity()
        {
            var map = new SigmaGaugeMap(2, 3, SigmaGaugeAxis.X,
                SigmaGaugeDirection.Positive, 3);
            SigmaS16[] source = BuildPage(map);
            const int singularX = 17;
            const int singularY = 24;
            SigmaS16[] expected = SigmaGaugeRefinement.Apply(source, map,
                (axis, x, y) => axis == SigmaGaugeAxis.X && x == singularX &&
                    y == singularY ? SigmaTopologyClass.Singular :
                    SigmaTopologyClass.Regular);
            UInt2[] sourceWords = Pack(source);
            UInt2[] targetWords = (UInt2[])sourceWords.Clone();
            var sourceTopology = new UInt4[TransitionCount];
            var targetTopology = new UInt4[TransitionCount];
            const uint annihilatorId = 7u;
            int singularSourceSample = singularY *
                SigmaDecodedPage.PageSize + singularX;
            int singularSourceTransition = singularSourceSample;
            sourceTopology[singularSourceTransition] = new UInt4(
                1u | (annihilatorId << 8), 41u, 59u, 0u);

            int sourceBlock = 3 * SigmaDecodedPage.BlocksPerAxis + 2;
            var sourceCertificates = new CertificateGpu[
                SigmaConstraintLedger.CertificatesPerPage];
            var targetCertificates = new CertificateGpu[
                SigmaConstraintLedger.CertificatesPerPage];
            var sourceBounds = new BoundsGpu[
                SigmaConstraintLedger.BoundsPerPage];
            var targetBounds = new BoundsGpu[
                SigmaConstraintLedger.BoundsPerPage];
            var sourceBlocks = new ConstraintBlockGpu[
                SigmaConstraintLedger.BlocksPerPage];
            var targetBlocks = new ConstraintBlockGpu[
                SigmaConstraintLedger.BlocksPerPage];
            int certificateAddress = sourceBlock *
                SigmaConstraintLedger.CertificatesPerBlock;
            int boundAddress = sourceBlock * SigmaConstraintLedger.BoundsPerBlock;
            sourceCertificates[certificateAddress] = new CertificateGpu
            {
                Identity = new UInt4(1u << 2, 1u, 101u, 9u),
                Range = new UInt4(3u, 0u, 1u, 1u),
                SampleMask = new UInt2((1u << 0) | (1u << 4), 0u),
            };
            sourceBounds[boundAddress] = new BoundsGpu
            {
                Lo = Pack(11L),
                Hi = Pack(23L),
            };
            sourceBlocks[sourceBlock] = new ConstraintBlockGpu
            {
                Counts = new UInt4(1u, 1u, uint.MaxValue, 0u),
                Proof = new UInt4(1u << 2, 3u, 0u, 17u),
            };
            var request = new[]
            {
                new GaugeRequestGpu
                {
                    Control = new UInt4(1u, (uint)sourceBlock, 0u, 0u),
                    Region = new UInt4(3u, 0u, 9u, 17u),
                    Metric = new UInt4(1u, 0u, 0u, 0u),
                    Evidence = new UInt4(101u, 202u, 3u, 17u),
                }
            };
            var targetRawHeads = new uint[SigmaConstraintLedger.BlocksPerPage];
            Array.Fill(targetRawHeads, uint.MaxValue);
            var status = new UInt4[4];

            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaGaugeRefinement");
            Assert.That(shader, Is.Not.Null);
            int stateKernel = shader.FindKernel("TransformGaugeState");
            int proofKernel = shader.FindKernel("TransformGaugeProof");
            int transportKernel = shader.FindKernel(
                "TransportGaugeTopologyPrior");
            int validateKernel = shader.FindKernel("ValidateGaugeTransform");
            int validateTopologyKernel = shader.FindKernel(
                "ValidateGaugeTopology");

            using SigmaExactBackendGate gate = SigmaExactBackendGate.Dispatch();
            using GraphicsBuffer sourceStateBuffer = Buffer(sourceWords);
            using GraphicsBuffer targetStateBuffer = Buffer(targetWords);
            using GraphicsBuffer sourceTopologyBuffer = Buffer(sourceTopology);
            using GraphicsBuffer targetTopologyBuffer = Buffer(targetTopology);
            using GraphicsBuffer sourceCertificateBuffer = Buffer(
                sourceCertificates);
            using GraphicsBuffer targetCertificateBuffer = Buffer(
                targetCertificates);
            using GraphicsBuffer sourceBoundBuffer = Buffer(sourceBounds);
            using GraphicsBuffer targetBoundBuffer = Buffer(targetBounds);
            using GraphicsBuffer sourceBlockBuffer = Buffer(sourceBlocks);
            using GraphicsBuffer targetBlockBuffer = Buffer(targetBlocks);
            using GraphicsBuffer rawHeadBuffer = Buffer(targetRawHeads);
            using GraphicsBuffer requestBuffer = Buffer(request);
            using GraphicsBuffer statusBuffer = Buffer(status);

            shader.SetInt("_SourceCarrierPageSlot", 0);
            shader.SetInt("_SourceCarrierPageCapacity", 1);
            shader.SetInt("_TargetCarrierPageSlot", 0);
            shader.SetInt("_TargetCarrierPageCapacity", 1);
            shader.SetInt("_SourceTopologyPageSlot", 0);
            shader.SetInt("_SourceTopologyPageCapacity", 1);
            shader.SetInt("_TargetTopologyPageSlot", 0);
            shader.SetInt("_TargetTopologyPageCapacity", 1);
            shader.SetInt("_GaugeSourceProofSlot", 0);
            shader.SetInt("_GaugeTargetProofSlot", 0);
            shader.SetInt("_GaugeProofCapacity", 1);
            shader.SetInt("_GaugeRequestIndex", 0);
            shader.SetInt("_GaugeRequestCapacity", 1);
            shader.SetInt("_GaugeTargetRevision", 17);

            BindState(shader, stateKernel, sourceStateBuffer,
                targetStateBuffer, sourceTopologyBuffer, requestBuffer,
                statusBuffer, gate);
            BindProof(shader, proofKernel, sourceCertificateBuffer,
                sourceBoundBuffer, sourceBlockBuffer, targetCertificateBuffer,
                targetBoundBuffer, targetBlockBuffer, rawHeadBuffer,
                requestBuffer, statusBuffer, gate);
            BindTopology(shader, transportKernel, sourceTopologyBuffer,
                targetTopologyBuffer, requestBuffer, statusBuffer, gate);
            BindState(shader, validateKernel, sourceStateBuffer,
                targetStateBuffer, sourceTopologyBuffer, requestBuffer,
                statusBuffer, gate);
            BindTopology(shader, validateTopologyKernel, sourceTopologyBuffer,
                targetTopologyBuffer, requestBuffer, statusBuffer, gate);

            shader.Dispatch(stateKernel, SigmaDecodedPage.SampleCount / 64,
                1, 1);
            shader.Dispatch(proofKernel,
                SigmaConstraintLedger.BlocksPerPage, 1, 1);
            shader.Dispatch(transportKernel, TransitionCount / 64, 1, 1);
            shader.Dispatch(validateKernel,
                SigmaDecodedPage.SampleCount / 64, 1, 1);
            shader.Dispatch(validateTopologyKernel, TransitionCount / 64,
                1, 1);

            targetStateBuffer.GetData(targetWords);
            targetCertificateBuffer.GetData(targetCertificates);
            targetBoundBuffer.GetData(targetBounds);
            targetBlockBuffer.GetData(targetBlocks);
            targetTopologyBuffer.GetData(targetTopology);
            statusBuffer.GetData(status);

            AssertPackedPage(expected, targetWords);
            Assert.That(status[0].X, Is.EqualTo(3u * 8u * 64u));
            Assert.That(status[0].Y,
                Is.EqualTo((uint)SigmaConstraintLedger.BlocksPerPage));
            Assert.That(status[0].W, Is.Zero);
            Assert.That(status[2].X,
                Is.EqualTo((uint)(SigmaDecodedPage.SampleCount - 16 * 64)));
            Assert.That(status[2].Y, Is.EqualTo((uint)TransitionCount));
            Assert.That(status[3].X, Is.Zero);
            Assert.That(status[3].Y, Is.EqualTo(1u));
            Assert.That(status[3].Z, Is.EqualTo(1u));
            Assert.That(status[3].W, Is.Zero);

            int targetBlock0 = sourceBlock;
            int targetBlock1 = sourceBlock + 1;
            AssertTransportedCertificate(targetBlock0, targetBlocks,
                targetCertificates, targetBounds);
            AssertTransportedCertificate(targetBlock1, targetBlocks,
                targetCertificates, targetBounds);
            int singularTargetSample = 24 * SigmaDecodedPage.PageSize + 19;
            UInt4 singular = targetTopology[singularTargetSample];
            Assert.That(singular.X & 3u, Is.EqualTo(1u));
            Assert.That((singular.X >> 8) & 0xffu,
                Is.EqualTo(annihilatorId));
            Assert.That(singular.Y, Is.EqualTo(41u));
            Assert.That(singular.Z, Is.EqualTo(59u));
        }

        private static void BindState(ComputeShader shader, int kernel,
            GraphicsBuffer source, GraphicsBuffer target,
            GraphicsBuffer topology, GraphicsBuffer request,
            GraphicsBuffer status, SigmaExactBackendGate gate)
        {
            shader.SetBuffer(kernel, "_SourceCarrierState", source);
            shader.SetBuffer(kernel, "_TargetCarrierState", target);
            shader.SetBuffer(kernel, "_SourceTopologyTransitions", topology);
            shader.SetBuffer(kernel, "_GaugeRequests", request);
            shader.SetBuffer(kernel, "_GaugeStatus", status);
            gate.Bind(shader, kernel);
        }

        private static void BindProof(ComputeShader shader, int kernel,
            GraphicsBuffer sourceCertificates, GraphicsBuffer sourceBounds,
            GraphicsBuffer sourceBlocks, GraphicsBuffer targetCertificates,
            GraphicsBuffer targetBounds, GraphicsBuffer targetBlocks,
            GraphicsBuffer targetRawHeads, GraphicsBuffer request,
            GraphicsBuffer status, SigmaExactBackendGate gate)
        {
            shader.SetBuffer(kernel, "_GaugeSourceCertificates",
                sourceCertificates);
            shader.SetBuffer(kernel, "_GaugeSourceBounds", sourceBounds);
            shader.SetBuffer(kernel, "_GaugeSourceBlocks", sourceBlocks);
            shader.SetBuffer(kernel, "_GaugeTargetCertificates",
                targetCertificates);
            shader.SetBuffer(kernel, "_GaugeTargetBounds", targetBounds);
            shader.SetBuffer(kernel, "_GaugeTargetBlocks", targetBlocks);
            shader.SetBuffer(kernel, "_GaugeTargetRawHeads", targetRawHeads);
            shader.SetBuffer(kernel, "_GaugeRequests", request);
            shader.SetBuffer(kernel, "_GaugeStatus", status);
            gate.Bind(shader, kernel);
        }

        private static void BindTopology(ComputeShader shader, int kernel,
            GraphicsBuffer source, GraphicsBuffer target,
            GraphicsBuffer request, GraphicsBuffer status,
            SigmaExactBackendGate gate)
        {
            shader.SetBuffer(kernel, "_SourceTopologyTransitions", source);
            shader.SetBuffer(kernel, "_TargetTopologyTransitions", target);
            shader.SetBuffer(kernel, "_GaugeRequests", request);
            shader.SetBuffer(kernel, "_GaugeStatus", status);
            gate.Bind(shader, kernel);
        }

        private static void AssertTransportedCertificate(int block,
            ConstraintBlockGpu[] blocks, CertificateGpu[] certificates,
            BoundsGpu[] bounds)
        {
            Assert.That(blocks[block].Counts.X, Is.EqualTo(1u));
            Assert.That(blocks[block].Counts.Y, Is.EqualTo(1u));
            int certificate = block *
                SigmaConstraintLedger.CertificatesPerBlock;
            Assert.That(certificates[certificate].SampleMask.X,
                Is.EqualTo(1u));
            Assert.That(certificates[certificate].Identity.Z,
                Is.EqualTo(101u));
            int bound = block * SigmaConstraintLedger.BoundsPerBlock;
            Assert.That(Unpack(bounds[bound].Lo), Is.EqualTo(11L));
            Assert.That(Unpack(bounds[bound].Hi), Is.EqualTo(23L));
        }

        private static void AssertPackedPage(SigmaS16[] expected,
            UInt2[] actual)
        {
            for (int sample = 0; sample < expected.Length; ++sample)
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                UInt2 packed = actual[sample * SigmaS16.LaneCount + lane];
                Assert.That(Unpack(packed), Is.EqualTo(expected[sample][lane]),
                    $"sample {sample}, lane {lane}");
            }
        }

        private static UInt2[] Pack(SigmaS16[] page)
        {
            var packed = new UInt2[page.Length * SigmaS16.LaneCount];
            for (int sample = 0; sample < page.Length; ++sample)
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                packed[sample * SigmaS16.LaneCount + lane] =
                    Pack(page[sample][lane]);
            return packed;
        }

        private static UInt2 Pack(long value) => new(unchecked((uint)value),
            unchecked((uint)((ulong)value >> 32)));

        private static long Unpack(UInt2 value) => unchecked((long)(
            ((ulong)value.Y << 32) | value.X));

        private static GraphicsBuffer Buffer<T>(T[] data) where T : struct
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                data.Length, Marshal.SizeOf<T>());
            buffer.SetData(data);
            return buffer;
        }

        private static SigmaS16[] BuildPage(SigmaGaugeMap map)
        {
            var page = new SigmaS16[SigmaDecodedPage.SampleCount];
            for (int sample = 0; sample < page.Length; ++sample)
                page[sample] = SigmaS16Operators.NullState;
            for (int sample = 0; sample < page.Length; ++sample)
            {
                int x = sample & 63;
                int y = sample >> 6;
                int axis = map.Axis == SigmaGaugeAxis.X ? x : y;
                int sourceStart = map.SourceAxisBlock *
                    SigmaDecodedPage.BlockSize;
                int oriented = map.Negative
                    ? sourceStart + SigmaDecodedPage.BlockSize - 1 - axis
                    : axis - sourceStart;
                bool terminal = oriented >= map.RetainedLength &&
                    oriented < map.RegionLength;
                if (!terminal)
                    page[sample] = Value(sample);
            }
            return page;
        }

        private static SigmaS16 Value(int sample)
        {
            var lanes = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < lanes.Length; ++lane)
                lanes[lane] = SigmaNumericDomain.Quantize(
                    ((sample * 17 + lane * 13) % 97 - 48) / 256.0);
            return SigmaS16.FromArray(lanes);
        }
    }
}
