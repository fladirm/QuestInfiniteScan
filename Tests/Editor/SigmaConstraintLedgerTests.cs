using System;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaConstraintLedgerTests
    {
        private const int SamplesPerPage = 4096;
        private const int RawWordsPerTile = 384;
        private const uint InvalidSlot = uint.MaxValue;

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
        private struct BoundsGpu
        {
            public UInt2 Lo;
            public UInt2 Hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DepthCellGpu
        {
            public BoundsGpu X;
            public BoundsGpu Y;
            public BoundsGpu Z;
            public uint SourceClass;
            public uint IndependenceKey;
            public uint Sector;
            public uint Valid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RgbCellGpu
        {
            public BoundsGpu B00;
            public BoundsGpu B01;
            public BoundsGpu B02;
            public BoundsGpu B03;
            public BoundsGpu B04;
            public BoundsGpu B05;
            public BoundsGpu B06;
            public BoundsGpu B07;
            public BoundsGpu B08;
            public BoundsGpu B09;
            public BoundsGpu B10;
            public BoundsGpu B11;
            public BoundsGpu B12;
            public BoundsGpu B13;
            public BoundsGpu B14;
            public BoundsGpu B15;
            public uint CoordinateMask;
            public uint SourceClass;
            public uint IndependenceKey;
            public uint Valid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProofSampleGpu
        {
            public UInt4 Meta;
            public DepthCellGpu DepthLeft;
            public DepthCellGpu DepthRight;
            public RgbCellGpu RgbLeft;
            public RgbCellGpu RgbRight;
            public UInt4 Raw0;
            public UInt4 Raw1;
            public UInt4 Raw2;
            public UInt4 Raw3;
            public UInt4 Raw4;
            public UInt4 Raw5;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct RawTileGpu
        {
            public UInt4 Identity;
            public UInt4 Provenance;
        }

        [Test]
        public void CertificateArenaFitsTheFrozenQuestMetadataBudget()
        {
            const int measuredCarrierBudgetPages = 480;
            long bytesPerPage =
                (long)SigmaConstraintLedger.CertificatesPerPage *
                    SigmaConstraintLedger.CertificateStride +
                (long)SigmaConstraintLedger.BoundsPerPage *
                    SigmaConstraintLedger.BoundStride +
                (long)SigmaConstraintLedger.BlocksPerPage *
                    SigmaConstraintLedger.BlockStride;
            Assert.That(bytesPerPage, Is.EqualTo(63_488));
            Assert.That(bytesPerPage * measuredCarrierBudgetPages,
                Is.LessThanOrEqualTo(32L * 1024 * 1024));
            Assert.That(SigmaConstraintLedger.ProofSampleStride,
                Is.LessThan(2048), "one-page proof scratch must be a legal " +
                "Vulkan StructuredBuffer stride");
        }

        [Test]
        public void CertificateOffsetsRoundTripWithoutPageOrBlockPhysics()
        {
            for (int slot = 0; slot < 480; ++slot)
            {
                ulong offset = SigmaConstraintLedger.CertificateOffsetForSlot(slot);
                Assert.That(SigmaConstraintLedger.DecodeCertificateSlot(offset,
                    SigmaConstraintLedger.CertificatesPerPage, 480),
                    Is.EqualTo(slot));
            }
            Assert.That(SigmaConstraintLedger.DecodeCertificateSlot(1,
                SigmaConstraintLedger.CertificatesPerPage, 480), Is.EqualTo(-1));
            Assert.That(SigmaConstraintLedger.DecodeCertificateSlot(0, 0, 480),
                Is.EqualTo(-1));
        }

        [Test]
        public void GpuLedgerOwnsFocusedTransactionKernels()
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaConstraintLedger");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.HasKernel("ClearProofTransaction"), Is.True);
            Assert.That(shader.HasKernel("ReduceProofPage"), Is.True);
        }

        [Test]
        public void GpuReducerEmitsOneExactMinimalDepthCertificate()
        {
            ProofSampleGpu sample = DepthSample(10, 20, 30, 40, 50, 60,
                101u, 0x1111u);
            LedgerFixture result = RunReducer(sample);

            Assert.That(result.Status[0], Is.EqualTo(64u));
            Assert.That(result.Status[1] & (1u << 31), Is.Zero);
            Assert.That(result.Status[6], Is.EqualTo(1u));
            Assert.That(result.Status[2] | result.Status[3], Is.Zero,
                "a uniform source cell is completely represented by its certificate");

            ConstraintBlockGpu block = result.Blocks[0];
            Assert.That(block.Counts.X, Is.EqualTo(1u));
            Assert.That(block.Counts.Y, Is.EqualTo(3u));
            Assert.That(block.Counts.Z, Is.EqualTo(InvalidSlot));
            Assert.That(block.Proof.X,
                Is.EqualTo((1u << 2) | (1u << 5) | (1u << 6)));
            Assert.That(block.Proof.Y, Is.EqualTo(3u));
            Assert.That(block.Proof.Z, Is.Zero,
                "one source key cannot manufacture per-coordinate independence");

            CertificateGpu certificate = result.Certificates[0];
            Assert.That(certificate.Identity.X, Is.EqualTo(block.Proof.X));
            Assert.That(certificate.Identity.Y, Is.EqualTo(1u));
            Assert.That(certificate.Identity.Z, Is.EqualTo(101u));
            Assert.That(certificate.Identity.W, Is.EqualTo(7u));
            Assert.That(certificate.Range.X, Is.EqualTo(3u));
            Assert.That(certificate.Range.Z, Is.EqualTo(3u));
            Assert.That(certificate.SampleMask.X, Is.EqualTo(1u));
            AssertBound(result.Bounds[0], 10, 20);
            AssertBound(result.Bounds[1], 30, 40);
            AssertBound(result.Bounds[2], 50, 60);
        }

        [Test]
        public void NonuniformFiniteFootprintsRemainReplayableAsRawTile()
        {
            ProofSampleGpu first = DepthSample(10, 20, 30, 40, 50, 60,
                101u, 0xaaaau);
            ProofSampleGpu second = DepthSample(11, 21, 30, 40, 50, 60,
                101u, 0xbbbbu);
            LedgerFixture result = RunReducer(first, second);

            const uint nonuniformReason = 1u << 4;
            Assert.That(result.Status[1] & (1u << 31), Is.Zero);
            Assert.That(result.Status[2] & 1u, Is.EqualTo(1u));
            Assert.That(result.Status[7] & nonuniformReason,
                Is.EqualTo(nonuniformReason));
            Assert.That(result.Blocks[0].Counts.Z, Is.EqualTo(0u));
            Assert.That(result.RawTiles[0].Provenance.X & nonuniformReason,
                Is.EqualTo(nonuniformReason));
            Assert.That(result.RawTiles[0].Provenance.Z, Is.EqualTo(3u));
            Assert.That(result.RawWords[0].X, Is.EqualTo(0xaaaau));
            Assert.That(result.RawWords[6].X, Is.EqualTo(0xbbbbu));
            AssertBound(result.Bounds[0], 10, 21);
        }

        [Test]
        public void IndependenceIsProvenPerSharedS16Coordinate()
        {
            ProofSampleGpu sample = DepthSample(10, 20, 30, 40, 50, 60,
                101u, 0x1111u);
            sample.DepthRight = new DepthCellGpu
            {
                X = Bound(10, 20),
                Y = Bound(30, 40),
                Z = Bound(50, 60),
                SourceClass = 2u,
                IndependenceKey = 202u,
                Sector = 1u,
                Valid = 1u,
            };
            LedgerFixture result = RunReducer(sample);
            uint geometryMask = (1u << 2) | (1u << 5) | (1u << 6);

            Assert.That(result.Blocks[0].Counts.X, Is.EqualTo(2u));
            Assert.That(result.Blocks[0].Proof.X, Is.EqualTo(geometryMask));
            Assert.That(result.Blocks[0].Proof.Z, Is.EqualTo(geometryMask),
                "only coordinates constrained by two overlapping independent " +
                "keys may become resistant");
        }

        private static LedgerFixture RunReducer(params ProofSampleGpu[] input)
        {
            Assert.That(Marshal.SizeOf<ProofSampleGpu>(),
                Is.EqualTo(SigmaConstraintLedger.ProofSampleStride));
            var samples = new ProofSampleGpu[SamplesPerPage];
            Array.Copy(input, samples, input.Length);
            var certificates = new CertificateGpu[
                SigmaConstraintLedger.CertificatesPerPage];
            var bounds = new BoundsGpu[SigmaConstraintLedger.BoundsPerPage];
            var blocks = new ConstraintBlockGpu[
                SigmaConstraintLedger.BlocksPerPage];
            var rawTiles = new RawTileGpu[SigmaConstraintLedger.BlocksPerPage];
            var rawWords = new UInt4[SigmaConstraintLedger.BlocksPerPage *
                RawWordsPerTile];
            var reservations = new uint[SigmaConstraintLedger.BlocksPerPage];
            for (uint index = 0; index < reservations.Length; ++index)
                reservations[index] = index;
            var status = new uint[SigmaConstraintLedger.StatusStride];

            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaConstraintLedger");
            int kernel = shader.FindKernel("ReduceProofPage");
            using var sampleBuffer = Buffer(samples,
                Marshal.SizeOf<ProofSampleGpu>());
            using var certificateBuffer = Buffer(certificates,
                Marshal.SizeOf<CertificateGpu>());
            using var boundBuffer = Buffer(bounds, Marshal.SizeOf<BoundsGpu>());
            using var blockBuffer = Buffer(blocks,
                Marshal.SizeOf<ConstraintBlockGpu>());
            using var rawTileBuffer = Buffer(rawTiles,
                Marshal.SizeOf<RawTileGpu>());
            using var rawWordBuffer = Buffer(rawWords, Marshal.SizeOf<UInt4>());
            using var reservationBuffer = Buffer(reservations, sizeof(uint));
            using var statusBuffer = Buffer(status, sizeof(uint));

            shader.SetInt("_SourceProofSlot", unchecked((int)InvalidSlot));
            shader.SetInt("_TargetProofSlot", 0);
            shader.SetInt("_ProofFrameSlot", 5);
            shader.SetInt("_ProofCalibrationEpoch", 7);
            shader.SetInt("_ProofRevision", 11);
            shader.SetInt("_RawTileCapacity", rawTiles.Length);
            shader.SetBuffer(kernel, "_ProofSamples", sampleBuffer);
            shader.SetBuffer(kernel, "_Certificates", certificateBuffer);
            shader.SetBuffer(kernel, "_CertificateBounds", boundBuffer);
            shader.SetBuffer(kernel, "_ConstraintBlocks", blockBuffer);
            shader.SetBuffer(kernel, "_RawTiles", rawTileBuffer);
            shader.SetBuffer(kernel, "_RawTileWords", rawWordBuffer);
            shader.SetBuffer(kernel, "_RawReservations", reservationBuffer);
            shader.SetBuffer(kernel, "_ProofPageStatus", statusBuffer);
            shader.Dispatch(kernel, SigmaConstraintLedger.BlocksPerPage, 1, 1);

            certificateBuffer.GetData(certificates);
            boundBuffer.GetData(bounds);
            blockBuffer.GetData(blocks);
            rawTileBuffer.GetData(rawTiles);
            rawWordBuffer.GetData(rawWords);
            statusBuffer.GetData(status);
            return new LedgerFixture(certificates, bounds, blocks, rawTiles,
                rawWords, status);
        }

        private static GraphicsBuffer Buffer<T>(T[] data, int stride)
            where T : struct
        {
            var result = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                data.Length, stride);
            result.SetData(data);
            return result;
        }

        private static ProofSampleGpu DepthSample(long xLo, long xHi,
            long yLo, long yHi, long zLo, long zHi, uint key, uint rawMarker)
        {
            return new ProofSampleGpu
            {
                Meta = new UInt4(1u | 4u, 0u, 0u, 0u),
                DepthLeft = new DepthCellGpu
                {
                    X = Bound(xLo, xHi),
                    Y = Bound(yLo, yHi),
                    Z = Bound(zLo, zHi),
                    SourceClass = 1u,
                    IndependenceKey = key,
                    Sector = 1u,
                    Valid = 1u,
                },
                Raw0 = new UInt4(rawMarker, 2u, 3u, 4u),
            };
        }

        private static BoundsGpu Bound(long lower, long upper) => new()
        {
            Lo = Pack(lower),
            Hi = Pack(upper),
        };

        private static UInt2 Pack(long value) => new(unchecked((uint)value),
            unchecked((uint)((ulong)value >> 32)));

        private static long Unpack(UInt2 value) => unchecked((long)(
            ((ulong)value.Y << 32) | value.X));

        private static void AssertBound(BoundsGpu actual, long lower, long upper)
        {
            Assert.That(Unpack(actual.Lo), Is.EqualTo(lower));
            Assert.That(Unpack(actual.Hi), Is.EqualTo(upper));
        }

        private sealed class LedgerFixture
        {
            internal LedgerFixture(CertificateGpu[] certificates,
                BoundsGpu[] bounds, ConstraintBlockGpu[] blocks,
                RawTileGpu[] rawTiles, UInt4[] rawWords, uint[] status)
            {
                Certificates = certificates;
                Bounds = bounds;
                Blocks = blocks;
                RawTiles = rawTiles;
                RawWords = rawWords;
                Status = status;
            }

            internal CertificateGpu[] Certificates { get; }
            internal BoundsGpu[] Bounds { get; }
            internal ConstraintBlockGpu[] Blocks { get; }
            internal RawTileGpu[] RawTiles { get; }
            internal UInt4[] RawWords { get; }
            internal uint[] Status { get; }
        }
    }
}
