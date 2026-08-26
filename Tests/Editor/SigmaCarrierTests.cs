using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaCarrierTests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct UInt2
        {
            public uint X;
            public uint Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt4
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;
        }

        [Test]
        public void SignedCarrierAddressingUsesMathematicalFloorAcrossZero()
        {
            AssertAddress(-65, -65, -2, -2, 63, 63);
            AssertAddress(-64, -64, -1, -1, 0, 0);
            AssertAddress(-1, -1, -1, -1, 63, 63);
            AssertAddress(0, 0, 0, 0, 0, 0);
            AssertAddress(63, 63, 0, 0, 63, 63);
            AssertAddress(64, 64, 1, 1, 0, 0);
            AssertAddress(long.MinValue, long.MaxValue,
                long.MinValue / 64, long.MaxValue / 64, 0, 63);
        }

        [Test]
        public void EveryExactBlockModeIsCanonicalAndRoundTripsBitForBit()
        {
            var fixtures = new Dictionary<SigmaBlockMode, SigmaS16[]>
            {
                [SigmaBlockMode.Null] = MakeNullBlock(),
                [SigmaBlockMode.Constant] = MakeConstantBlock(),
                [SigmaBlockMode.Affine] = MakeAffineBlock(),
                [SigmaBlockMode.Delta] = MakeDeltaBlock(),
                [SigmaBlockMode.Raw] = MakeRawBlock(),
            };

            foreach ((SigmaBlockMode expectedMode, SigmaS16[] source) in fixtures)
            {
                SigmaEncodedBlock encoded = SigmaCarrierCodec.EncodeBlock(source);
                Assert.That(encoded.Mode, Is.EqualTo(expectedMode), expectedMode.ToString());
                CollectionAssert.AreEqual(source,
                    SigmaCarrierCodec.DecodeBlock(encoded), expectedMode.ToString());
                SigmaEncodedBlock reencoded = SigmaCarrierCodec.EncodeBlock(
                    SigmaCarrierCodec.DecodeBlock(encoded));
                Assert.That(reencoded.Mode, Is.EqualTo(encoded.Mode));
                CollectionAssert.AreEqual(encoded.Payload, reencoded.Payload);
            }
        }

        [Test]
        public void DefaultBackingIsZEmptyAndStateWithoutGaugeIsRejected()
        {
            var empty = new SigmaS16[SigmaDecodedPage.SampleCount];
            var page = new SigmaDecodedPage(
                new SigmaCarrierPageCoordinate(0, 0), 1u, 1u, 0u, 0u,
                0u, 0u, 0u, 0u,
                Array.Empty<SigmaCarrierRepresentationRecord>(), empty);
            byte[] encoded = SigmaCarrierCodec.EncodePage(page);
            SigmaDecodedPage decoded = SigmaCarrierCodec.DecodePage(encoded);
            Assert.That(decoded.ActiveSampleCount, Is.Zero);
            Assert.That(decoded.CopyRepresentation(), Is.Empty);
            Assert.That(decoded.CopySamples(), Is.All.EqualTo(
                SigmaS16Operators.ZEmpty));

            empty[0] = State(1L);
            Assert.Throws<ArgumentException>(() => new SigmaDecodedPage(
                new SigmaCarrierPageCoordinate(0, 0), 1u, 1u, 0u, 0u,
                0u, 0u, 0u, 0u,
                Array.Empty<SigmaCarrierRepresentationRecord>(), empty));
        }

        [Test]
        public void PageAndSortedSnapshotRestartAreByteIdentical()
        {
            var representationWords = new uint[
                SigmaCarrierRepresentationRecord.WordCount];
            representationWords[0] = 17u;
            representationWords[4] = 3u;
            representationWords[5] =
                (uint)(SigmaNativeGaugeCellFlags.Active |
                    SigmaNativeGaugeCellFlags.Normalized);
            representationWords[6] = Convert.ToUInt32(
                SigmaGeneratedFrame.ChiFingerprint.Substring(0, 8), 16);
            representationWords[7] = Convert.ToUInt32(
                SigmaGeneratedFrame.KappaFingerprint.Substring(0, 8), 16);
            representationWords[8] =
                (uint)(SigmaNativeCertificateFlags.Valid |
                    SigmaNativeCertificateFlags.Minimized);
            representationWords[11] = 5u;
            var pageASamples = new SigmaS16[SigmaDecodedPage.SampleCount];
            pageASamples[0] = State(17L);
            SigmaDecodedPage pageA = new SigmaDecodedPage(
                new SigmaCarrierPageCoordinate(17, -9), 3u, 11u, 1234UL,
                1u, 2u, 5u, 3u, 1u,
                new[]
                {
                    new SigmaCarrierRepresentationRecord(0,
                        representationWords),
                }, pageASamples);
            SigmaDecodedPage pageB = MakeRepresentedPage(
                new SigmaCarrierPageCoordinate(-2, -9), 5u, 12u, 4567UL,
                9u, 23L);
            byte[] encodedA = SigmaCarrierCodec.EncodePage(pageA);
            SigmaDecodedPage decodedA = SigmaCarrierCodec.DecodePage(encodedA);
            CollectionAssert.AreEqual(encodedA, SigmaCarrierCodec.EncodePage(decodedA));
            CollectionAssert.AreEqual(pageA.CopySamples(), decodedA.CopySamples());
            Assert.That(decodedA.GaugeGeneration, Is.EqualTo(2u));
            Assert.That(decodedA.CertificateGeneration, Is.EqualTo(5u));
            Assert.That(decodedA.RepresentationFlags, Is.EqualTo(3u));
            Assert.That(decodedA.ActiveSampleCount, Is.EqualTo(1u));
            CollectionAssert.AreEqual(representationWords,
                decodedA.CopyRepresentation()[0].Words);

            byte[] snapshot = SigmaCarrierCodec.EncodeSnapshot(
                new[] { pageA, pageB });
            SigmaDecodedPage[] restarted = SigmaCarrierCodec.DecodeSnapshot(snapshot);
            Assert.That(restarted[0].Coordinate, Is.EqualTo(pageB.Coordinate));
            Assert.That(restarted[1].Coordinate, Is.EqualTo(pageA.Coordinate));
            CollectionAssert.AreEqual(snapshot,
                SigmaCarrierCodec.EncodeSnapshot(restarted));
        }

        [Test]
        public void PackedGpuCodecMatchesCanonicalCpuBytesAndDecodedState()
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaCarrierCodec");
            Assert.That(shader, Is.Not.Null);
            int encodeKernel = shader.FindKernel("EncodePageBlocks");
            int decodeKernel = shader.FindKernel("DecodePageBlocks");
            SigmaS16[] samples = MakeMixedSamples();
            UInt2[] packed = PackPage(samples);
            var decoded = new UInt2[packed.Length];
            var descriptors = new UInt4[SigmaDecodedPage.BlockCount];
            var payloadWords = new uint[
                SigmaDecodedPage.BlockCount * SigmaCarrierCodec.RawBlockBytes / sizeof(uint)];

            using SigmaExactBackendGate gate = SigmaExactBackendGate.Dispatch();
            using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                packed.Length, Marshal.SizeOf<UInt2>());
            using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                decoded.Length, Marshal.SizeOf<UInt2>());
            using var descriptorBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, descriptors.Length,
                Marshal.SizeOf<UInt4>());
            using var payload = new GraphicsBuffer(GraphicsBuffer.Target.Raw,
                payloadWords.Length, sizeof(uint));
            input.SetData(packed);
            payload.SetData(payloadWords);

            BindCodec(shader, encodeKernel, gate, input, output,
                descriptorBuffer, payload);
            shader.Dispatch(encodeKernel, SigmaDecodedPage.BlockCount, 1, 1);
            BindCodec(shader, decodeKernel, gate, input, output,
                descriptorBuffer, payload);
            shader.Dispatch(decodeKernel, SigmaDecodedPage.BlockCount, 1, 1);
            descriptorBuffer.GetData(descriptors);
            payload.GetData(payloadWords);
            output.GetData(decoded);

            byte[] gpuPayload = WordsToLittleEndian(payloadWords);
            for (int block = 0; block < SigmaDecodedPage.BlockCount; ++block)
            {
                SigmaEncodedBlock expected = SigmaCarrierCodec.EncodeBlock(
                    CopyBlock(samples, block));
                UInt4 actual = descriptors[block];
                Assert.That(actual.W & 1u, Is.EqualTo(1u),
                    $"valid block {block}, flags={actual.W}");
                Assert.That(actual.X, Is.EqualTo((uint)expected.Mode),
                    $"mode block {block}, flags={actual.W}");
                Assert.That(actual.Y, Is.EqualTo((uint)expected.PayloadBytes),
                    $"size block {block}");
                var payloadSlice = new byte[expected.PayloadBytes];
                Buffer.BlockCopy(gpuPayload, checked((int)actual.Z), payloadSlice, 0,
                    payloadSlice.Length);
                CollectionAssert.AreEqual(expected.Payload, payloadSlice,
                    $"payload block {block}");
            }
            for (int index = 0; index < packed.Length; ++index)
            {
                Assert.That(decoded[index].X, Is.EqualTo(packed[index].X),
                    $"decoded low sample={index / 16} lane={index & 15}");
                Assert.That(decoded[index].Y, Is.EqualTo(packed[index].Y),
                    $"decoded high sample={index / 16} lane={index & 15}");
            }
        }

        [Test]
        public void SegmentSizingRespectsBindingLimitAndGenerationPairs()
        {
            Assert.That(SigmaCarrier.DecodedPageBytes, Is.EqualTo(524288));
            Assert.That(SigmaCarrier.RepresentationPageBytes,
                Is.EqualTo(1179648));
            Assert.That(SigmaCarrier.ResidentPageBytes, Is.EqualTo(1703936));
            Assert.That(SigmaCarrier.DefaultDecodedBudgetMegabytes,
                Is.EqualTo(1024));
            Assert.That(SigmaCarrier.InitialResidentPageCapacity,
                Is.EqualTo(2),
                "The decoded budget is a ceiling, not a cold-start allocation.");
            long decodedPages = (long)SigmaCarrier.DefaultDecodedBudgetMegabytes *
                1024L * 1024L / SigmaCarrier.ResidentPageBytes;
            Assert.That(decodedPages, Is.EqualTo(630));
            Assert.That(decodedPages / 2L, Is.EqualTo(315));

            int wideSegment = SigmaCarrier.ComputeSegmentPageCapacity(
                1024L * 1024L * 1024L);
            int exact128MiBSegment = SigmaCarrier.ComputeSegmentPageCapacity(
                128L * 1024L * 1024L);
            Assert.That(wideSegment,
                Is.EqualTo(SigmaCarrier.MaximumPagesPerSegment));
            Assert.That(exact128MiBSegment, Is.EqualTo(112));
            Assert.That((decodedPages + wideSegment - 1L) / wideSegment,
                Is.EqualTo(3));
            Assert.That((decodedPages + exact128MiBSegment - 1L) /
                exact128MiBSegment, Is.EqualTo(6));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SigmaCarrier.ComputeSegmentPageCapacity(
                    SigmaCarrier.DecodedPageBytes));
            Assert.Throws<InvalidOperationException>(() =>
                SigmaCarrier.ComputeSegmentPageCapacity(
                    SigmaCarrier.RepresentationPageBytes));
        }

        private static void AssertAddress(long x, long y, long pageX, long pageY,
            int localX, int localY)
        {
            SigmaCarrierAddress address = SigmaCarrierCodec.ResolveAddress(x, y);
            Assert.That(address.Page, Is.EqualTo(
                new SigmaCarrierPageCoordinate(pageX, pageY)));
            int blockX = localX >> 3;
            int blockY = localY >> 3;
            Assert.That(address.BlockIndex, Is.EqualTo(blockY * 8 + blockX));
            Assert.That(address.SampleIndex,
                Is.EqualTo((localY & 7) * 8 + (localX & 7)));
        }

        private static SigmaS16[] MakeNullBlock()
        {
            var block = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            for (int index = 0; index < block.Length; ++index)
                block[index] = SigmaS16Operators.NullState;
            return block;
        }

        private static SigmaS16[] MakeConstantBlock()
        {
            var lanes = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < lanes.Length; ++lane)
                lanes[lane] = lane % 2 == 0 ? long.MaxValue - lane :
                    long.MinValue + lane;
            SigmaS16 value = SigmaS16.FromArray(lanes);
            var block = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            for (int index = 0; index < block.Length; ++index)
                block[index] = value;
            return block;
        }

        private static SigmaS16[] MakeAffineBlock()
        {
            var block = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            for (int v = 0; v < 8; ++v)
            {
                for (int u = 0; u < 8; ++u)
                {
                    var lanes = new long[SigmaS16.LaneCount];
                    for (int lane = 0; lane < lanes.Length; ++lane)
                    {
                        long scale = lane + 1L;
                        lanes[lane] = checked(scale * 100L * SigmaNumericDomain.One +
                            u * scale * 10L * SigmaNumericDomain.One -
                            v * scale * 5L * SigmaNumericDomain.One);
                    }
                    block[v * 8 + u] = SigmaS16.FromArray(lanes);
                }
            }
            return block;
        }

        private static SigmaS16[] MakeDeltaBlock()
        {
            var block = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            for (int sample = 0; sample < block.Length; ++sample)
            {
                var lanes = new long[SigmaS16.LaneCount];
                lanes[0] = (sample * sample + 3 * sample + 1) % 17 - 8;
                lanes[5] = sample % 11 == 0 ? -13 : 0;
                block[sample] = SigmaS16.FromArray(lanes);
            }
            return block;
        }

        private static SigmaS16[] MakeRawBlock()
        {
            var block = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            ulong state = 0x9e3779b97f4a7c15UL;
            for (int sample = 0; sample < block.Length; ++sample)
            {
                var lanes = new long[SigmaS16.LaneCount];
                for (int lane = 0; lane < lanes.Length; ++lane)
                {
                    state ^= state << 7;
                    state ^= state >> 9;
                    state ^= state << 8;
                    lanes[lane] = unchecked((long)state);
                }
                block[sample] = SigmaS16.FromArray(lanes);
            }
            return block;
        }

        private static SigmaS16[] MakeMixedSamples()
        {
            var page = new SigmaS16[SigmaDecodedPage.SampleCount];
            SigmaS16[][] fixtures =
            {
                MakeNullBlock(), MakeConstantBlock(), MakeAffineBlock(),
                MakeDeltaBlock(), MakeRawBlock()
            };
            for (int block = 0; block < SigmaDecodedPage.BlockCount; ++block)
                StoreBlock(page, block, fixtures[block % fixtures.Length]);
            return page;
        }

        private static SigmaDecodedPage MakeRepresentedPage(
            SigmaCarrierPageCoordinate coordinate, uint generation,
            uint revision, ulong certificateOffset,
            uint certificateGeneration, long stateValue)
        {
            var samples = new SigmaS16[SigmaDecodedPage.SampleCount];
            samples[0] = State(stateValue);
            var words = new uint[SigmaCarrierRepresentationRecord.WordCount];
            words[4] = 0u;
            words[5] = (uint)(SigmaNativeGaugeCellFlags.Active |
                SigmaNativeGaugeCellFlags.Normalized);
            words[6] = Convert.ToUInt32(
                SigmaGeneratedFrame.ChiFingerprint.Substring(0, 8), 16);
            words[7] = Convert.ToUInt32(
                SigmaGeneratedFrame.KappaFingerprint.Substring(0, 8), 16);
            words[8] = (uint)(SigmaNativeCertificateFlags.Valid |
                SigmaNativeCertificateFlags.Minimized);
            words[11] = certificateGeneration;
            return new SigmaDecodedPage(coordinate, generation, revision,
                certificateOffset, 1u, 1u, certificateGeneration, 3u, 1u,
                new[] { new SigmaCarrierRepresentationRecord(0, words) },
                samples);
        }

        private static SigmaS16 State(long value)
        {
            var lanes = new long[SigmaS16.LaneCount];
            lanes[0] = value;
            return SigmaS16.FromArray(lanes);
        }

        private static void StoreBlock(SigmaS16[] page, int blockIndex,
            SigmaS16[] block)
        {
            int blockX = blockIndex & 7;
            int blockY = blockIndex >> 3;
            for (int v = 0; v < 8; ++v)
            {
                for (int u = 0; u < 8; ++u)
                    page[(blockY * 8 + v) * 64 + blockX * 8 + u] = block[v * 8 + u];
            }
        }

        private static SigmaS16[] CopyBlock(SigmaS16[] page, int blockIndex)
        {
            var block = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            int blockX = blockIndex & 7;
            int blockY = blockIndex >> 3;
            for (int v = 0; v < 8; ++v)
                for (int u = 0; u < 8; ++u)
                    block[v * 8 + u] = page[
                        (blockY * 8 + v) * 64 + blockX * 8 + u];
            return block;
        }

        private static UInt2[] PackPage(SigmaS16[] samples)
        {
            var packed = new UInt2[samples.Length * SigmaS16.LaneCount];
            for (int sample = 0; sample < samples.Length; ++sample)
            {
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                {
                    long value = samples[sample][lane];
                    packed[sample * SigmaS16.LaneCount + lane] = new UInt2
                    {
                        X = unchecked((uint)value),
                        Y = unchecked((uint)(value >> 32)),
                    };
                }
            }
            return packed;
        }

        private static byte[] WordsToLittleEndian(uint[] words)
        {
            var bytes = new byte[words.Length * sizeof(uint)];
            for (int index = 0; index < words.Length; ++index)
            {
                uint value = words[index];
                int offset = index * sizeof(uint);
                bytes[offset] = (byte)value;
                bytes[offset + 1] = (byte)(value >> 8);
                bytes[offset + 2] = (byte)(value >> 16);
                bytes[offset + 3] = (byte)(value >> 24);
            }
            return bytes;
        }

        private static void BindCodec(ComputeShader shader, int kernel,
            SigmaExactBackendGate gate, GraphicsBuffer input, GraphicsBuffer output,
            GraphicsBuffer descriptors, GraphicsBuffer payload)
        {
            gate.Bind(shader, kernel);
            shader.SetInt("_InputPageSlot", 0);
            shader.SetInt("_OutputPageSlot", 0);
            shader.SetInt("_BlockCount", SigmaDecodedPage.BlockCount);
            shader.SetBuffer(kernel, "_DecodedInput", input);
            shader.SetBuffer(kernel, "_DecodedOutput", output);
            shader.SetBuffer(kernel, "_BlockDescriptors", descriptors);
            shader.SetBuffer(kernel, "_CodecPayload", payload);
        }
    }
}
