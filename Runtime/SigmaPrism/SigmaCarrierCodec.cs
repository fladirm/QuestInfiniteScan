using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Genesis.RoomScan.SigmaPrism
{
    public enum SigmaBlockMode : byte
    {
        Null = 0,
        Constant = 1,
        Affine = 2,
        Delta = 3,
        Raw = 4,
    }

    public readonly struct SigmaCarrierPageCoordinate :
        IEquatable<SigmaCarrierPageCoordinate>, IComparable<SigmaCarrierPageCoordinate>
    {
        public SigmaCarrierPageCoordinate(long x, long y)
        {
            X = x;
            Y = y;
        }

        public long X { get; }
        public long Y { get; }

        public int CompareTo(SigmaCarrierPageCoordinate other)
        {
            int y = Y.CompareTo(other.Y);
            return y != 0 ? y : X.CompareTo(other.X);
        }

        public bool Equals(SigmaCarrierPageCoordinate other) =>
            X == other.X && Y == other.Y;
        public override bool Equals(object obj) =>
            obj is SigmaCarrierPageCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X},{Y})";
    }

    public readonly struct SigmaCarrierAddress
    {
        public SigmaCarrierAddress(SigmaCarrierPageCoordinate page,
            int blockIndex, int sampleIndex)
        {
            Page = page;
            BlockIndex = blockIndex;
            SampleIndex = sampleIndex;
        }

        public SigmaCarrierPageCoordinate Page { get; }
        public int BlockIndex { get; }
        public int SampleIndex { get; }
    }

    public sealed class SigmaDecodedPage
    {
        public const int PageSize = 64;
        public const int BlockSize = 8;
        public const int BlocksPerAxis = PageSize / BlockSize;
        public const int BlockCount = BlocksPerAxis * BlocksPerAxis;
        public const int SamplesPerBlock = BlockSize * BlockSize;
        public const int SampleCount = PageSize * PageSize;

        private readonly SigmaS16[] _samples;

        public SigmaDecodedPage(SigmaCarrierPageCoordinate coordinate,
            uint generation, uint revision, ulong certificateOffset,
            uint certificateCount, SigmaS16[] samples)
        {
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));
            if (samples.Length != SampleCount)
                throw new ArgumentException("A carrier page contains exactly 64x64 samples.",
                    nameof(samples));
            Coordinate = coordinate;
            Generation = generation;
            Revision = revision;
            CertificateOffset = certificateOffset;
            CertificateCount = certificateCount;
            _samples = (SigmaS16[])samples.Clone();
        }

        public SigmaCarrierPageCoordinate Coordinate { get; }
        public uint Generation { get; }
        public uint Revision { get; }
        public ulong CertificateOffset { get; }
        public uint CertificateCount { get; }
        public SigmaS16 this[int x, int y]
        {
            get
            {
                if ((uint)x >= PageSize || (uint)y >= PageSize)
                    throw new ArgumentOutOfRangeException();
                return _samples[y * PageSize + x];
            }
        }

        public SigmaS16[] CopySamples() => (SigmaS16[])_samples.Clone();

        public SigmaS16[] CopyBlock(int blockIndex)
        {
            if ((uint)blockIndex >= BlockCount)
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
            int blockX = blockIndex & 7;
            int blockY = blockIndex >> 3;
            var block = new SigmaS16[SamplesPerBlock];
            for (int v = 0; v < BlockSize; ++v)
            {
                for (int u = 0; u < BlockSize; ++u)
                    block[v * BlockSize + u] = this[blockX * BlockSize + u,
                        blockY * BlockSize + v];
            }
            return block;
        }
    }

    public readonly struct SigmaEncodedBlock
    {
        public SigmaEncodedBlock(SigmaBlockMode mode, byte[] payload)
        {
            Mode = mode;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public SigmaBlockMode Mode { get; }
        public byte[] Payload { get; }
        public int PayloadBytes => Payload.Length;
    }

    /// <summary>
    /// Slow exact CPU oracle/recovery codec. Live carrier mutation and bulk codec
    /// work use the GPU lowering; this implementation defines persistence bytes and
    /// fixture truth only.
    /// </summary>
    public static class SigmaCarrierCodec
    {
        public const uint PageMagic = 0x50363153u; // S16P little-endian
        public const uint SnapshotMagic = 0x43363153u; // S16C little-endian
        public const uint SchemaVersion = 6u;
        public const int RawBlockBytes =
            SigmaDecodedPage.SamplesPerBlock * SigmaS16.LaneCount * sizeof(long);
        public const int ConstantBlockBytes = SigmaS16.LaneCount * sizeof(long);
        public const int AffineBlockBytes = 3 * ConstantBlockBytes;
        public const int DeltaWidthBytes = SigmaS16.LaneCount;

        public static SigmaCarrierAddress ResolveAddress(long carrierX, long carrierY)
        {
            long pageX = FloorDivide(carrierX, SigmaDecodedPage.PageSize);
            long pageY = FloorDivide(carrierY, SigmaDecodedPage.PageSize);
            int localX = (int)(carrierX - pageX * SigmaDecodedPage.PageSize);
            int localY = (int)(carrierY - pageY * SigmaDecodedPage.PageSize);
            int blockX = localX >> 3;
            int blockY = localY >> 3;
            int sampleX = localX & 7;
            int sampleY = localY & 7;
            return new SigmaCarrierAddress(
                new SigmaCarrierPageCoordinate(pageX, pageY),
                blockY * SigmaDecodedPage.BlocksPerAxis + blockX,
                sampleY * SigmaDecodedPage.BlockSize + sampleX);
        }

        public static SigmaEncodedBlock EncodeBlock(SigmaS16[] samples)
        {
            ValidateBlock(samples);
            var candidates = new List<SigmaEncodedBlock>(5);
            if (IsNull(samples))
                candidates.Add(new SigmaEncodedBlock(SigmaBlockMode.Null,
                    Array.Empty<byte>()));
            if (TryEncodeConstant(samples, out byte[] constant))
                candidates.Add(new SigmaEncodedBlock(SigmaBlockMode.Constant, constant));
            if (TryEncodeAffine(samples, out byte[] affine))
                candidates.Add(new SigmaEncodedBlock(SigmaBlockMode.Affine, affine));
            if (TryEncodeDelta(samples, out byte[] delta))
                candidates.Add(new SigmaEncodedBlock(SigmaBlockMode.Delta, delta));
            candidates.Add(new SigmaEncodedBlock(SigmaBlockMode.Raw, EncodeRaw(samples)));

            SigmaEncodedBlock best = candidates[0];
            for (int index = 1; index < candidates.Count; ++index)
            {
                SigmaEncodedBlock candidate = candidates[index];
                if (candidate.PayloadBytes < best.PayloadBytes ||
                    candidate.PayloadBytes == best.PayloadBytes &&
                    candidate.Mode < best.Mode)
                    best = candidate;
            }
            return best;
        }

        public static SigmaS16[] DecodeBlock(SigmaEncodedBlock encoded)
        {
            return encoded.Mode switch
            {
                SigmaBlockMode.Null => DecodeNull(encoded.Payload),
                SigmaBlockMode.Constant => DecodeConstant(encoded.Payload),
                SigmaBlockMode.Affine => DecodeAffine(encoded.Payload),
                SigmaBlockMode.Delta => DecodeDelta(encoded.Payload),
                SigmaBlockMode.Raw => DecodeRaw(encoded.Payload),
                _ => throw new InvalidDataException("Unknown Sigma block mode."),
            };
        }

        public static byte[] EncodePage(SigmaDecodedPage page)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            var blocks = new SigmaEncodedBlock[SigmaDecodedPage.BlockCount];
            var offsets = new uint[SigmaDecodedPage.BlockCount + 1];
            uint payloadBytes = 0u;
            for (int block = 0; block < blocks.Length; ++block)
            {
                blocks[block] = EncodeBlock(page.CopyBlock(block));
                offsets[block] = payloadBytes;
                payloadBytes = checked(payloadBytes +
                    (uint)blocks[block].PayloadBytes);
            }
            offsets[blocks.Length] = payloadBytes;

            int initialCapacity = payloadBytes <= (uint)(int.MaxValue - 512)
                ? checked(512 + (int)payloadBytes)
                : 0;
            using var stream = new MemoryStream(initialCapacity);
            using var writer = new BinaryWriter(stream);
            writer.Write(PageMagic);
            writer.Write(SchemaVersion);
            writer.Write(page.Coordinate.X);
            writer.Write(page.Coordinate.Y);
            writer.Write(page.Generation);
            writer.Write(page.Revision);
            writer.Write(page.CertificateOffset);
            writer.Write(page.CertificateCount);
            WriteFingerprint(writer, SigmaS16Operators.BundleFingerprint);
            WriteFingerprint(writer, SigmaOperatorPlans.PlanBundleFingerprint);
            for (int block = 0; block < blocks.Length; ++block)
                writer.Write((byte)blocks[block].Mode);
            for (int index = 0; index < offsets.Length; ++index)
                writer.Write(offsets[index]);
            for (int block = 0; block < blocks.Length; ++block)
                writer.Write(blocks[block].Payload);
            writer.Flush();
            return stream.ToArray();
        }

        public static SigmaDecodedPage DecodePage(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == PageMagic, "Invalid Sigma page magic.");
            Require(reader.ReadUInt32() == SchemaVersion, "Unsupported Sigma page schema.");
            var coordinate = new SigmaCarrierPageCoordinate(
                reader.ReadInt64(), reader.ReadInt64());
            uint generation = reader.ReadUInt32();
            uint revision = reader.ReadUInt32();
            ulong certificateOffset = reader.ReadUInt64();
            uint certificateCount = reader.ReadUInt32();
            Require(ReadFingerprint(reader) == SigmaS16Operators.BundleFingerprint,
                "Sigma algebra fingerprint mismatch.");
            Require(ReadFingerprint(reader) == SigmaOperatorPlans.PlanBundleFingerprint,
                "Sigma plan fingerprint mismatch.");
            var modes = new SigmaBlockMode[SigmaDecodedPage.BlockCount];
            for (int block = 0; block < modes.Length; ++block)
            {
                modes[block] = (SigmaBlockMode)reader.ReadByte();
                Require(modes[block] <= SigmaBlockMode.Raw,
                    "Invalid Sigma block mode.");
            }
            var offsets = new uint[SigmaDecodedPage.BlockCount + 1];
            for (int index = 0; index < offsets.Length; ++index)
            {
                offsets[index] = reader.ReadUInt32();
                if (index > 0)
                    Require(offsets[index] >= offsets[index - 1],
                        "Sigma payload offsets are not monotone.");
            }
            long payloadStart = stream.Position;
            Require(payloadStart + offsets[offsets.Length - 1] == stream.Length,
                "Sigma page payload size mismatch.");
            var pageSamples = new SigmaS16[SigmaDecodedPage.SampleCount];
            for (int block = 0; block < SigmaDecodedPage.BlockCount; ++block)
            {
                int length = checked((int)(offsets[block + 1] - offsets[block]));
                stream.Position = payloadStart + offsets[block];
                byte[] payload = reader.ReadBytes(length);
                Require(payload.Length == length, "Truncated Sigma block payload.");
                SigmaS16[] decoded = DecodeBlock(
                    new SigmaEncodedBlock(modes[block], payload));
                StoreBlock(pageSamples, block, decoded);
            }
            return new SigmaDecodedPage(coordinate, generation, revision,
                certificateOffset, certificateCount, pageSamples);
        }

        public static byte[] EncodeSnapshot(IReadOnlyList<SigmaDecodedPage> pages)
        {
            if (pages == null)
                throw new ArgumentNullException(nameof(pages));
            var sorted = new List<SigmaDecodedPage>(pages.Count);
            for (int index = 0; index < pages.Count; ++index)
                sorted.Add(pages[index] ?? throw new ArgumentNullException(nameof(pages)));
            sorted.Sort(ComparePages);
            for (int index = 1; index < sorted.Count; ++index)
            {
                Require(ComparePages(sorted[index - 1], sorted[index]) != 0,
                    "Duplicate Sigma page generation in snapshot.");
            }
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(SnapshotMagic);
            writer.Write(SchemaVersion);
            WriteFingerprint(writer, SigmaS16Operators.BundleFingerprint);
            WriteFingerprint(writer, SigmaOperatorPlans.PlanBundleFingerprint);
            writer.Write((uint)sorted.Count);
            for (int index = 0; index < sorted.Count; ++index)
            {
                byte[] page = EncodePage(sorted[index]);
                writer.Write((uint)page.Length);
                writer.Write(page);
            }
            writer.Flush();
            return stream.ToArray();
        }

        public static SigmaDecodedPage[] DecodeSnapshot(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == SnapshotMagic, "Invalid Sigma snapshot magic.");
            Require(reader.ReadUInt32() == SchemaVersion,
                "Unsupported Sigma snapshot schema.");
            Require(ReadFingerprint(reader) == SigmaS16Operators.BundleFingerprint,
                "Snapshot algebra fingerprint mismatch.");
            Require(ReadFingerprint(reader) == SigmaOperatorPlans.PlanBundleFingerprint,
                "Snapshot plan fingerprint mismatch.");
            uint count = reader.ReadUInt32();
            Require(count <= (uint)int.MaxValue,
                "Sigma snapshot page count is invalid.");
            var pages = new SigmaDecodedPage[(int)count];
            for (int index = 0; index < pages.Length; ++index)
            {
                uint length = reader.ReadUInt32();
                Require(length <= (uint)int.MaxValue,
                    "Sigma page record is too large.");
                byte[] pageBytes = reader.ReadBytes((int)length);
                Require((uint)pageBytes.Length == length,
                    "Truncated Sigma page record.");
                pages[index] = DecodePage(pageBytes);
                if (index > 0)
                    Require(ComparePages(pages[index - 1], pages[index]) < 0,
                        "Snapshot pages are not in canonical order.");
            }
            Require(stream.Position == stream.Length, "Trailing Sigma snapshot bytes.");
            return pages;
        }

        private static bool IsNull(SigmaS16[] samples)
        {
            SigmaS16 zNull = SigmaS16Operators.NullState;
            for (int index = 0; index < samples.Length; ++index)
            {
                if (samples[index] != zNull)
                    return false;
            }
            return true;
        }

        private static bool TryEncodeConstant(SigmaS16[] samples, out byte[] payload)
        {
            SigmaS16 first = samples[0];
            for (int index = 1; index < samples.Length; ++index)
            {
                if (samples[index] != first)
                {
                    payload = null;
                    return false;
                }
            }
            using var stream = new MemoryStream(ConstantBlockBytes);
            using var writer = new BinaryWriter(stream);
            WriteState(writer, first);
            writer.Flush();
            payload = stream.ToArray();
            return true;
        }

        private static bool TryEncodeAffine(SigmaS16[] samples, out byte[] payload)
        {
            var baseLane = new long[SigmaS16.LaneCount];
            var stepU = new long[SigmaS16.LaneCount];
            var stepV = new long[SigmaS16.LaneCount];
            try
            {
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                {
                    baseLane[lane] = samples[0][lane];
                    stepU[lane] = SigmaNumericDomain.QSub(samples[1][lane],
                        samples[0][lane]);
                    stepV[lane] = SigmaNumericDomain.QSub(
                        samples[SigmaDecodedPage.BlockSize][lane], samples[0][lane]);
                }
                for (int v = 0; v < SigmaDecodedPage.BlockSize; ++v)
                {
                    for (int u = 0; u < SigmaDecodedPage.BlockSize; ++u)
                    {
                        SigmaS16 actual = samples[v * SigmaDecodedPage.BlockSize + u];
                        for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                        {
                            long expected = AddScaled(baseLane[lane], stepU[lane], u,
                                stepV[lane], v);
                            if (expected != actual[lane])
                            {
                                payload = null;
                                return false;
                            }
                        }
                    }
                }
            }
            catch (OverflowException)
            {
                payload = null;
                return false;
            }
            using var stream = new MemoryStream(AffineBlockBytes);
            using var writer = new BinaryWriter(stream);
            WriteState(writer, SigmaS16.FromArray(baseLane));
            WriteState(writer, SigmaS16.FromArray(stepU));
            WriteState(writer, SigmaS16.FromArray(stepV));
            writer.Flush();
            payload = stream.ToArray();
            return true;
        }

        private static bool TryEncodeDelta(SigmaS16[] samples, out byte[] payload)
        {
            var widths = new byte[SigmaS16.LaneCount];
            var residuals = new long[SigmaS16.LaneCount][];
            try
            {
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                {
                    residuals[lane] = new long[SigmaDecodedPage.SamplesPerBlock];
                    int maximumWidth = 0;
                    for (int sample = 0;
                        sample < SigmaDecodedPage.SamplesPerBlock; ++sample)
                    {
                        int u = sample & 7;
                        int v = sample >> 3;
                        BigInteger predictor = Predictor(samples, lane, u, v);
                        BigInteger residual = new BigInteger(samples[sample][lane]) - predictor;
                        if (residual < long.MinValue || residual > long.MaxValue)
                        {
                            payload = null;
                            return false;
                        }
                        long exact = (long)residual;
                        residuals[lane][sample] = exact;
                        maximumWidth = Math.Max(maximumWidth, SignedBitWidth(exact));
                    }
                    widths[lane] = (byte)maximumWidth;
                }
            }
            catch (OverflowException)
            {
                payload = null;
                return false;
            }

            var bits = new LeastSignificantBitWriter();
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                int width = widths[lane];
                for (int sample = 0;
                    sample < SigmaDecodedPage.SamplesPerBlock; ++sample)
                    bits.WriteSigned(residuals[lane][sample], width);
            }
            byte[] packed = bits.ToArray();
            payload = new byte[DeltaWidthBytes + packed.Length];
            Buffer.BlockCopy(widths, 0, payload, 0, widths.Length);
            Buffer.BlockCopy(packed, 0, payload, widths.Length, packed.Length);
            return true;
        }

        private static byte[] EncodeRaw(SigmaS16[] samples)
        {
            using var stream = new MemoryStream(RawBlockBytes);
            using var writer = new BinaryWriter(stream);
            for (int sample = 0; sample < samples.Length; ++sample)
                WriteState(writer, samples[sample]);
            writer.Flush();
            return stream.ToArray();
        }

        private static SigmaS16[] DecodeNull(byte[] payload)
        {
            Require(payload.Length == 0, "NULL block has a payload.");
            var samples = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            SigmaS16 zNull = SigmaS16Operators.NullState;
            for (int index = 0; index < samples.Length; ++index)
                samples[index] = zNull;
            return samples;
        }

        private static SigmaS16[] DecodeConstant(byte[] payload)
        {
            Require(payload.Length == ConstantBlockBytes, "CONST payload size mismatch.");
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream);
            SigmaS16 value = ReadState(reader);
            var samples = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            for (int index = 0; index < samples.Length; ++index)
                samples[index] = value;
            return samples;
        }

        private static SigmaS16[] DecodeAffine(byte[] payload)
        {
            Require(payload.Length == AffineBlockBytes, "AFFINE payload size mismatch.");
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream);
            SigmaS16 origin = ReadState(reader);
            SigmaS16 stepU = ReadState(reader);
            SigmaS16 stepV = ReadState(reader);
            var samples = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            for (int v = 0; v < SigmaDecodedPage.BlockSize; ++v)
            {
                for (int u = 0; u < SigmaDecodedPage.BlockSize; ++u)
                {
                    var lanes = new long[SigmaS16.LaneCount];
                    for (int lane = 0; lane < lanes.Length; ++lane)
                        lanes[lane] = AddScaled(origin[lane], stepU[lane], u,
                            stepV[lane], v);
                    samples[v * SigmaDecodedPage.BlockSize + u] =
                        SigmaS16.FromArray(lanes);
                }
            }
            return samples;
        }

        private static SigmaS16[] DecodeDelta(byte[] payload)
        {
            Require(payload.Length >= DeltaWidthBytes, "DELTA payload is truncated.");
            var widths = new byte[SigmaS16.LaneCount];
            Buffer.BlockCopy(payload, 0, widths, 0, widths.Length);
            long expectedBits = 0L;
            for (int lane = 0; lane < widths.Length; ++lane)
            {
                Require(widths[lane] <= 64, "DELTA residual width exceeds 64 bits.");
                expectedBits += widths[lane] * SigmaDecodedPage.SamplesPerBlock;
            }
            int expectedBytes = checked((int)((expectedBits + 7) >> 3));
            Require(payload.Length == DeltaWidthBytes + expectedBytes,
                "DELTA bitstream size mismatch.");
            var packed = new byte[expectedBytes];
            Buffer.BlockCopy(payload, DeltaWidthBytes, packed, 0, expectedBytes);
            var reader = new LeastSignificantBitReader(packed);
            var lanesBySample = new long[SigmaDecodedPage.SamplesPerBlock][];
            for (int sample = 0; sample < lanesBySample.Length; ++sample)
                lanesBySample[sample] = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                for (int sample = 0; sample < SigmaDecodedPage.SamplesPerBlock; ++sample)
                {
                    int u = sample & 7;
                    int v = sample >> 3;
                    long residual = reader.ReadSigned(widths[lane]);
                    BigInteger predictor = Predictor(lanesBySample, lane, u, v);
                    BigInteger value = predictor + residual;
                    Require(value >= long.MinValue && value <= long.MaxValue,
                        "DELTA decode overflow.");
                    lanesBySample[sample][lane] = (long)value;
                }
            }
            Require(reader.PositionBits == expectedBits, "DELTA bitstream was not exhausted.");
            var samples = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            for (int sample = 0; sample < samples.Length; ++sample)
                samples[sample] = SigmaS16.FromArray(lanesBySample[sample]);
            return samples;
        }

        private static SigmaS16[] DecodeRaw(byte[] payload)
        {
            Require(payload.Length == RawBlockBytes, "RAW payload size mismatch.");
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream);
            var samples = new SigmaS16[SigmaDecodedPage.SamplesPerBlock];
            for (int sample = 0; sample < samples.Length; ++sample)
                samples[sample] = ReadState(reader);
            return samples;
        }

        private static int SignedBitWidth(long value)
        {
            if (value == 0L)
                return 0;
            ulong varying = value < 0 ? unchecked((ulong)~value) : (ulong)value;
            int magnitudeBits = 0;
            while (varying != 0UL)
            {
                ++magnitudeBits;
                varying >>= 1;
            }
            return Math.Min(64, magnitudeBits + 1);
        }

        private static BigInteger Predictor(SigmaS16[] samples, int lane, int u, int v)
        {
            if (u == 0 && v == 0)
                return BigInteger.Zero;
            if (v == 0)
                return samples[u - 1][lane];
            if (u == 0)
                return samples[(v - 1) * SigmaDecodedPage.BlockSize][lane];
            return new BigInteger(samples[v * SigmaDecodedPage.BlockSize + u - 1][lane]) +
                samples[(v - 1) * SigmaDecodedPage.BlockSize + u][lane] -
                samples[(v - 1) * SigmaDecodedPage.BlockSize + u - 1][lane];
        }

        private static BigInteger Predictor(long[][] samples, int lane, int u, int v)
        {
            if (u == 0 && v == 0)
                return BigInteger.Zero;
            if (v == 0)
                return samples[u - 1][lane];
            if (u == 0)
                return samples[(v - 1) * SigmaDecodedPage.BlockSize][lane];
            return new BigInteger(samples[v * SigmaDecodedPage.BlockSize + u - 1][lane]) +
                samples[(v - 1) * SigmaDecodedPage.BlockSize + u][lane] -
                samples[(v - 1) * SigmaDecodedPage.BlockSize + u - 1][lane];
        }

        private static long AddScaled(long origin, long stepU, int u,
            long stepV, int v)
        {
            long value = origin;
            for (int index = 0; index < u; ++index)
                value = SigmaNumericDomain.QAdd(value, stepU);
            for (int index = 0; index < v; ++index)
                value = SigmaNumericDomain.QAdd(value, stepV);
            return value;
        }

        private static void WriteState(BinaryWriter writer, SigmaS16 state)
        {
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                writer.Write(state[lane]);
        }

        private static SigmaS16 ReadState(BinaryReader reader)
        {
            var lanes = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < lanes.Length; ++lane)
                lanes[lane] = reader.ReadInt64();
            return SigmaS16.FromArray(lanes);
        }

        private static void WriteFingerprint(BinaryWriter writer, string fingerprint)
        {
            Require(fingerprint != null && fingerprint.Length == 64,
                "Sigma fingerprint is not SHA-256.");
            for (int index = 0; index < fingerprint.Length; index += 2)
                writer.Write(Convert.ToByte(fingerprint.Substring(index, 2), 16));
        }

        private static string ReadFingerprint(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(32);
            Require(bytes.Length == 32, "Truncated Sigma fingerprint.");
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void StoreBlock(SigmaS16[] page, int blockIndex,
            SigmaS16[] block)
        {
            int blockX = blockIndex & 7;
            int blockY = blockIndex >> 3;
            for (int v = 0; v < SigmaDecodedPage.BlockSize; ++v)
            {
                for (int u = 0; u < SigmaDecodedPage.BlockSize; ++u)
                {
                    page[(blockY * SigmaDecodedPage.BlockSize + v) *
                        SigmaDecodedPage.PageSize + blockX *
                        SigmaDecodedPage.BlockSize + u] =
                        block[v * SigmaDecodedPage.BlockSize + u];
                }
            }
        }

        private static int ComparePages(SigmaDecodedPage left, SigmaDecodedPage right)
        {
            int coordinate = left.Coordinate.CompareTo(right.Coordinate);
            return coordinate != 0 ? coordinate :
                left.Generation.CompareTo(right.Generation);
        }

        private static long FloorDivide(long value, int divisor)
        {
            long quotient = value / divisor;
            long remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private static void ValidateBlock(SigmaS16[] samples)
        {
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));
            if (samples.Length != SigmaDecodedPage.SamplesPerBlock)
                throw new ArgumentException("A Sigma block contains exactly 8x8 samples.",
                    nameof(samples));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidDataException(message);
        }

        private sealed class LeastSignificantBitWriter
        {
            private readonly List<byte> _bytes = new();
            private long _bitPosition;

            public void WriteSigned(long value, int width)
            {
                if ((uint)width > 64u)
                    throw new ArgumentOutOfRangeException(nameof(width));
                if (width == 0)
                {
                    if (value != 0L)
                        throw new InvalidDataException("Non-zero residual has zero width.");
                    return;
                }
                ulong bits = unchecked((ulong)value);
                for (int bit = 0; bit < width; ++bit)
                {
                    int byteIndex = checked((int)(_bitPosition >> 3));
                    while (_bytes.Count <= byteIndex)
                        _bytes.Add(0);
                    if (((bits >> bit) & 1UL) != 0UL)
                        _bytes[byteIndex] |= (byte)(1 << (int)(_bitPosition & 7));
                    ++_bitPosition;
                }
            }

            public byte[] ToArray() => _bytes.ToArray();
        }

        private sealed class LeastSignificantBitReader
        {
            private readonly byte[] _bytes;
            public LeastSignificantBitReader(byte[] bytes) =>
                _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            public long PositionBits { get; private set; }

            public long ReadSigned(int width)
            {
                if ((uint)width > 64u)
                    throw new InvalidDataException("Invalid signed bit width.");
                if (width == 0)
                    return 0L;
                ulong value = 0UL;
                for (int bit = 0; bit < width; ++bit)
                {
                    int byteIndex = checked((int)(PositionBits >> 3));
                    if ((uint)byteIndex >= _bytes.Length)
                        throw new EndOfStreamException();
                    if (((_bytes[byteIndex] >> (int)(PositionBits & 7)) & 1) != 0)
                        value |= 1UL << bit;
                    ++PositionBits;
                }
                if (width < 64 && (value & (1UL << (width - 1))) != 0UL)
                    value |= ulong.MaxValue << width;
                return unchecked((long)value);
            }
        }
    }
}
