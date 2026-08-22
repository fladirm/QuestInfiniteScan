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
        public void PageAndSortedSnapshotRestartAreByteIdentical()
        {
            SigmaDecodedPage pageA = MakeMixedPage(
                new SigmaCarrierPageCoordinate(17, -9), 3u, 11u, 1234UL, 7u);
            SigmaDecodedPage pageB = MakeMixedPage(
                new SigmaCarrierPageCoordinate(-2, -9), 5u, 12u, 4567UL, 9u);
            byte[] encodedA = SigmaCarrierCodec.EncodePage(pageA);
            SigmaDecodedPage decodedA = SigmaCarrierCodec.DecodePage(encodedA);
            CollectionAssert.AreEqual(encodedA, SigmaCarrierCodec.EncodePage(decodedA));
            CollectionAssert.AreEqual(pageA.CopySamples(), decodedA.CopySamples());

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
            SigmaDecodedPage page = MakeMixedPage(
                new SigmaCarrierPageCoordinate(0, 0), 1u, 1u, 0UL, 0u);
            SigmaS16[] samples = page.CopySamples();
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
                    page.CopyBlock(block));
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
        public void DirtyCompactionIsStableAndSegmentSizingRespectsBindingLimit()
        {
            Assert.That(SigmaCarrier.DecodedPageBytes, Is.EqualTo(524288));
            Assert.That(SigmaCarrier.ComputeSegmentPageCapacity(128L * 1024 * 1024,
                64), Is.EqualTo(128));
            Assert.That(SigmaCarrier.ComputeSegmentPageCapacity(
                SigmaCarrier.DecodedPageBytes, 64), Is.EqualTo(1));

            const int capacity = 64;
            uint[] flags = new uint[capacity];
            int[] expectedIndices = { 0, 3, 4, 17, 31, 63 };
            foreach (int index in expectedIndices)
                flags[index] = 1u;
            uint[] slots = new uint[capacity];
            uint[] count = new uint[1];
            uint[] arguments = new uint[3];
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaCarrier");
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("CompactDirtyPages");
            using var flagBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                capacity, sizeof(uint));
            using var slotBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                capacity, sizeof(uint));
            using var countBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, sizeof(uint));
            using var argumentBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                3, sizeof(uint));
            flagBuffer.SetData(flags);
            shader.SetInt("_PageCapacity", capacity);
            shader.SetBuffer(kernel, "_DirtyFlags", flagBuffer);
            shader.SetBuffer(kernel, "_DirtyPageSlots", slotBuffer);
            shader.SetBuffer(kernel, "_DirtyCount", countBuffer);
            shader.SetBuffer(kernel, "_DirtyDispatchArgs", argumentBuffer);
            shader.Dispatch(kernel, 1, 1, 1);
            slotBuffer.GetData(slots);
            countBuffer.GetData(count);
            argumentBuffer.GetData(arguments);

            Assert.That(count[0], Is.EqualTo((uint)expectedIndices.Length));
            Assert.That(arguments, Is.EqualTo(new uint[]
                { (uint)expectedIndices.Length, 1u, 1u }));
            for (int index = 0; index < expectedIndices.Length; ++index)
                Assert.That(slots[index], Is.EqualTo((uint)expectedIndices[index]));
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

        private static SigmaDecodedPage MakeMixedPage(
            SigmaCarrierPageCoordinate coordinate, uint generation, uint revision,
            ulong certificateOffset, uint certificateCount)
        {
            var page = new SigmaS16[SigmaDecodedPage.SampleCount];
            SigmaS16[][] fixtures =
            {
                MakeNullBlock(), MakeConstantBlock(), MakeAffineBlock(),
                MakeDeltaBlock(), MakeRawBlock()
            };
            for (int block = 0; block < SigmaDecodedPage.BlockCount; ++block)
                StoreBlock(page, block, fixtures[block % fixtures.Length]);
            return new SigmaDecodedPage(coordinate, generation, revision,
                certificateOffset, certificateCount, page);
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
