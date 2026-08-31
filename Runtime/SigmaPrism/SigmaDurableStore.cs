using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Genesis.RoomScan.SigmaPrism
{
    internal readonly struct SigmaDurableHash :
        IEquatable<SigmaDurableHash>, IComparable<SigmaDurableHash>
    {
        internal const int ByteCount = 32;

        private readonly ulong _a;
        private readonly ulong _b;
        private readonly ulong _c;
        private readonly ulong _d;

        private SigmaDurableHash(ulong a, ulong b, ulong c, ulong d)
        {
            _a = a;
            _b = b;
            _c = c;
            _d = d;
        }

        internal bool IsZero => (_a | _b | _c | _d) == 0UL;
        internal static SigmaDurableHash Zero => default;

        internal static SigmaDurableHash Compute(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using SHA256 sha = SHA256.Create();
            return FromBytes(sha.ComputeHash(bytes));
        }

        internal static SigmaDurableHash Parse(string text)
        {
            if (text == null || text.Length != ByteCount * 2)
                throw new FormatException("A durable hash contains 64 hex digits.");
            var bytes = new byte[ByteCount];
            for (int index = 0; index < bytes.Length; ++index)
                bytes[index] = Convert.ToByte(text.Substring(index * 2, 2), 16);
            return FromBytes(bytes);
        }

        internal static SigmaDurableHash FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length != ByteCount)
                throw new ArgumentException("A durable hash contains 32 bytes.",
                    nameof(bytes));
            return new SigmaDurableHash(ReadU64(bytes, 0), ReadU64(bytes, 8),
                ReadU64(bytes, 16), ReadU64(bytes, 24));
        }

        internal byte[] ToBytes()
        {
            var bytes = new byte[ByteCount];
            WriteU64(bytes, 0, _a); WriteU64(bytes, 8, _b);
            WriteU64(bytes, 16, _c); WriteU64(bytes, 24, _d);
            return bytes;
        }

        internal void Write(BinaryWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            writer.Write(ToBytes());
        }

        internal static SigmaDurableHash Read(BinaryReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            byte[] bytes = reader.ReadBytes(ByteCount);
            Require(bytes.Length == ByteCount, "Truncated durable hash.");
            return FromBytes(bytes);
        }

        public bool Equals(SigmaDurableHash other) =>
            _a == other._a && _b == other._b && _c == other._c && _d == other._d;
        public override bool Equals(object obj) =>
            obj is SigmaDurableHash other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);
        public int CompareTo(SigmaDurableHash other)
        {
            byte[] left = ToBytes();
            byte[] right = other.ToBytes();
            for (int index = 0; index < left.Length; ++index)
            {
                int comparison = left[index].CompareTo(right[index]);
                if (comparison != 0) return comparison;
            }
            return 0;
        }

        public override string ToString()
        {
            byte[] bytes = ToBytes();
            const string alphabet = "0123456789abcdef";
            var result = new char[bytes.Length * 2];
            for (int index = 0; index < bytes.Length; ++index)
            {
                result[index * 2] = alphabet[bytes[index] >> 4];
                result[index * 2 + 1] = alphabet[bytes[index] & 15];
            }
            return new string(result);
        }

        public static bool operator ==(SigmaDurableHash left,
            SigmaDurableHash right) => left.Equals(right);
        public static bool operator !=(SigmaDurableHash left,
            SigmaDurableHash right) => !left.Equals(right);

        private static ulong ReadU64(byte[] bytes, int offset)
        {
            ulong value = 0UL;
            for (int index = 0; index < sizeof(ulong); ++index)
                value |= (ulong)bytes[offset + index] << (index * 8);
            return value;
        }

        private static void WriteU64(byte[] bytes, int offset, ulong value)
        {
            for (int index = 0; index < sizeof(ulong); ++index)
                bytes[offset + index] = (byte)(value >> (index * 8));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }

    internal static class SigmaDurableSchema
    {
        internal const uint Version = 1u;
        internal const string SchemaFingerprint =
            "00fef6206c5961fa092cff57d6154f6b3127b4c370898771979389f5cbe9d88e";
        // These values are emitted by the frozen Merkaba generator. They are
        // repeated here as interpretation pins so N5 cannot silently follow a
        // different generated program or canonical-default proof.
        internal const string ProgramFingerprint =
            "09564b2e81bb16313af6e80bc42845e47111d354d0f347e2fedd242eca0eab33";
        internal const string DefaultFingerprint =
            "4aa45c31a02ec4da9713f0dc8d5daa2cedf855ca16cb55d605a5c864f52779d6";

        internal static SigmaDurableHash SchemaHash =>
            SigmaDurableHash.Parse(SchemaFingerprint);
        internal static SigmaDurableHash ProgramHash =>
            SigmaDurableHash.Parse(ProgramFingerprint);
        internal static SigmaDurableHash DefaultHash =>
            SigmaDurableHash.Parse(DefaultFingerprint);
        internal static SigmaDurableHash ChiHash =>
            SigmaDurableHash.Parse(SigmaGeneratedFrame.ChiFingerprint);
        internal static SigmaDurableHash KappaHash =>
            SigmaDurableHash.Parse(SigmaGeneratedFrame.KappaFingerprint);
        internal static SigmaDurableHash CertificateHash =>
            SigmaDurableHash.Parse(SigmaGeneratedFrame.CertificateFingerprint);
    }

    [Flags]
    internal enum SigmaQuerySupportFlags : uint
    {
        None = 0u,
        MayContribute = 1u,
        Verified = 2u,
    }

    internal readonly struct SigmaQuerySupportSummary
    {
        private const uint Magic = 0x5351354eu; // N5QS
        private const uint LegacyVersion = 1u;
        private const uint CurrentVersion = 2u;

        private SigmaQuerySupportSummary(
            SigmaCarrierPageCoordinate coordinate, uint pageGeneration,
            uint revision, uint activeSampleCount,
            SigmaQuerySupportFlags flags, SigmaDurableHash schema,
            SigmaDurableHash program, SigmaDurableHash defaultValue,
            SigmaDurableHash chi, SigmaDurableHash kappa,
            SigmaDurableHash certificate, uint encodingVersion,
            SigmaDurableHash supportPlan,
            SigmaQuerySupportBoundsKind boundsKind,
            SigmaQ48Bounds3 projectiveBounds)
        {
            Coordinate = coordinate;
            PageGeneration = pageGeneration;
            Revision = revision;
            ActiveSampleCount = activeSampleCount;
            Flags = flags;
            SchemaFingerprint = schema;
            ProgramFingerprint = program;
            DefaultFingerprint = defaultValue;
            ChiFingerprint = chi;
            KappaFingerprint = kappa;
            CertificateFingerprint = certificate;
            EncodingVersion = encodingVersion;
            SupportPlanFingerprint = supportPlan;
            BoundsKind = boundsKind;
            ProjectiveBounds = projectiveBounds;
        }

        internal SigmaCarrierPageCoordinate Coordinate { get; }
        internal uint PageGeneration { get; }
        internal uint Revision { get; }
        internal uint ActiveSampleCount { get; }
        internal SigmaQuerySupportFlags Flags { get; }
        internal SigmaDurableHash SchemaFingerprint { get; }
        internal SigmaDurableHash ProgramFingerprint { get; }
        internal SigmaDurableHash DefaultFingerprint { get; }
        internal SigmaDurableHash ChiFingerprint { get; }
        internal SigmaDurableHash KappaFingerprint { get; }
        internal SigmaDurableHash CertificateFingerprint { get; }
        internal uint EncodingVersion { get; }
        internal SigmaDurableHash SupportPlanFingerprint { get; }
        internal SigmaQuerySupportBoundsKind BoundsKind { get; }
        internal SigmaQ48Bounds3 ProjectiveBounds { get; }
        internal bool HasExactProjectiveBounds =>
            EncodingVersion == CurrentVersion &&
            SupportPlanFingerprint == SigmaQuerySupportPlan.Fingerprint &&
            BoundsKind == SigmaQuerySupportBoundsKind.ExactProjectiveHull;

        internal static SigmaQuerySupportSummary FromPage(
            SigmaDecodedPage page, uint pageGeneration,
            SigmaQuerySupportFlags flags)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            SigmaQuerySupportBoundsKind kind =
                SigmaQuerySupportPlan.SummarizePage(page, out var bounds);
            return Create(SigmaEncodedPageHeader.FromPage(page),
                pageGeneration, flags, kind, bounds);
        }

        internal static SigmaQuerySupportSummary FromHeader(
            SigmaEncodedPageHeader header, uint pageGeneration,
            SigmaQuerySupportFlags flags)
        {
            return Create(header, pageGeneration, flags,
                SigmaQuerySupportBoundsKind.Unknown, default);
        }

        internal static SigmaQuerySupportSummary FromStagedBlocks(
            SigmaEncodedPageHeader header, uint pageGeneration,
            SigmaQuerySupportFlags flags,
            IReadOnlyList<SigmaEncodedBlock> blocks)
        {
            SigmaQuerySupportBoundsKind kind =
                SigmaQuerySupportPlan.SummarizeBlocks(header, blocks,
                    out var bounds);
            return Create(header, pageGeneration, flags, kind, bounds);
        }

        private static SigmaQuerySupportSummary Create(
            SigmaEncodedPageHeader header, uint pageGeneration,
            SigmaQuerySupportFlags flags,
            SigmaQuerySupportBoundsKind boundsKind,
            SigmaQ48Bounds3 projectiveBounds)
        {
            Require(header.Generation != 0u && pageGeneration != 0u &&
                header.Revision != 0u,
                "A query-support summary requires a published page.");
            Require((flags & ~(SigmaQuerySupportFlags.MayContribute |
                SigmaQuerySupportFlags.Verified)) == 0,
                "Invalid query-support summary flags.");
            Require(boundsKind == SigmaQuerySupportBoundsKind.Unknown ||
                boundsKind == SigmaQuerySupportBoundsKind.ExactProjectiveHull,
                "Invalid query-support bound kind.");
            return new SigmaQuerySupportSummary(header.Coordinate,
                pageGeneration, header.Revision, header.ActiveSampleCount,
                flags, SigmaDurableSchema.SchemaHash,
                SigmaDurableSchema.ProgramHash,
                SigmaDurableSchema.DefaultHash, SigmaDurableSchema.ChiHash,
                SigmaDurableSchema.KappaHash,
                SigmaDurableSchema.CertificateHash, CurrentVersion,
                SigmaQuerySupportPlan.Fingerprint, boundsKind,
                projectiveBounds);
        }

        internal byte[] Encode()
        {
            using var stream = new MemoryStream(320);
            using var writer = new BinaryWriter(stream);
            writer.Write(Magic); writer.Write(CurrentVersion);
            writer.Write(Coordinate.X); writer.Write(Coordinate.Y);
            writer.Write(PageGeneration); writer.Write(Revision);
            writer.Write(ActiveSampleCount); writer.Write((uint)Flags);
            SchemaFingerprint.Write(writer);
            ProgramFingerprint.Write(writer);
            DefaultFingerprint.Write(writer);
            ChiFingerprint.Write(writer);
            KappaFingerprint.Write(writer);
            CertificateFingerprint.Write(writer);
            SupportPlanFingerprint.Write(writer);
            writer.Write((uint)BoundsKind);
            writer.Write(ProjectiveBounds.LowerX);
            writer.Write(ProjectiveBounds.LowerY);
            writer.Write(ProjectiveBounds.LowerZ);
            writer.Write(ProjectiveBounds.UpperX);
            writer.Write(ProjectiveBounds.UpperY);
            writer.Write(ProjectiveBounds.UpperZ);
            writer.Flush();
            return stream.ToArray();
        }

        internal static SigmaQuerySupportSummary Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == Magic,
                "Invalid query-support summary header.");
            uint version = reader.ReadUInt32();
            Require(version == LegacyVersion || version == CurrentVersion,
                "Unsupported query-support summary version.");
            var coordinate = new SigmaCarrierPageCoordinate(reader.ReadInt64(),
                reader.ReadInt64());
            uint pageGeneration = reader.ReadUInt32();
            uint revision = reader.ReadUInt32();
            uint activeSampleCount = reader.ReadUInt32();
            var flags = (SigmaQuerySupportFlags)reader.ReadUInt32();
            Require(pageGeneration != 0u && revision != 0u &&
                activeSampleCount <= SigmaCarrier.SamplesPerPage &&
                (flags & ~(SigmaQuerySupportFlags.MayContribute |
                    SigmaQuerySupportFlags.Verified)) == 0,
                "Invalid query-support summary payload.");
            SigmaDurableHash schema = SigmaDurableHash.Read(reader);
            SigmaDurableHash program = SigmaDurableHash.Read(reader);
            SigmaDurableHash defaultValue = SigmaDurableHash.Read(reader);
            SigmaDurableHash chi = SigmaDurableHash.Read(reader);
            SigmaDurableHash kappa = SigmaDurableHash.Read(reader);
            SigmaDurableHash certificate = SigmaDurableHash.Read(reader);
            SigmaDurableHash supportPlan = SigmaDurableHash.Zero;
            SigmaQuerySupportBoundsKind boundsKind =
                SigmaQuerySupportBoundsKind.Unknown;
            SigmaQ48Bounds3 projectiveBounds = default;
            if (version == CurrentVersion)
            {
                supportPlan = SigmaDurableHash.Read(reader);
                boundsKind = (SigmaQuerySupportBoundsKind)reader.ReadUInt32();
                Require(boundsKind == SigmaQuerySupportBoundsKind.Unknown ||
                    boundsKind ==
                        SigmaQuerySupportBoundsKind.ExactProjectiveHull,
                    "Invalid query-support bound kind.");
                projectiveBounds = new SigmaQ48Bounds3(
                    reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(),
                    reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());
            }
            var summary = new SigmaQuerySupportSummary(coordinate,
                pageGeneration, revision, activeSampleCount, flags,
                schema, program, defaultValue, chi, kappa, certificate,
                version, supportPlan, boundsKind, projectiveBounds);
            Require(stream.Position == stream.Length,
                "Trailing query-support summary bytes.");
            return summary;
        }

        internal void Validate(SigmaDurablePageRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            Require(Coordinate.Equals(record.Coordinate) &&
                PageGeneration == record.PageGeneration &&
                Revision == record.Revision,
                "Query-support summary page key/generation mismatch.");
            Require(SchemaFingerprint == SigmaDurableSchema.SchemaHash &&
                ProgramFingerprint == SigmaDurableSchema.ProgramHash &&
                DefaultFingerprint == SigmaDurableSchema.DefaultHash &&
                ChiFingerprint == SigmaDurableSchema.ChiHash &&
                KappaFingerprint == SigmaDurableSchema.KappaHash &&
                CertificateFingerprint == SigmaDurableSchema.CertificateHash,
                "Query-support summary fingerprint mismatch.");
            Require(EncodingVersion == LegacyVersion ||
                SupportPlanFingerprint == SigmaQuerySupportPlan.Fingerprint,
                "Query-support plan fingerprint mismatch.");
            Require((Flags & SigmaQuerySupportFlags.Verified) != 0 &&
                Flags == record.QuerySupport.Flags,
                "Query-support summary/receipt flags mismatch.");
        }

        internal void Validate(SigmaDecodedPage page,
            SigmaQuerySupportReceipt receipt)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            Validate(SigmaEncodedPageHeader.FromPage(page), receipt);
        }

        internal void Validate(SigmaEncodedPageHeader header,
            SigmaQuerySupportReceipt receipt)
        {
            // The summary does not bind a PageBlob locator. Validating one must
            // therefore never re-encode and hash the complete 4096-sample page.
            // PageBlob bytes/hash are independently proved by the staged-page
            // gate and immutable object store.
            Require(Coordinate.Equals(header.Coordinate) &&
                PageGeneration == receipt.Generation &&
                Revision == header.Revision,
                "Query-support summary page key/generation mismatch.");
            Require(SchemaFingerprint == SigmaDurableSchema.SchemaHash &&
                ProgramFingerprint == SigmaDurableSchema.ProgramHash &&
                DefaultFingerprint == SigmaDurableSchema.DefaultHash &&
                ChiFingerprint == SigmaDurableSchema.ChiHash &&
                KappaFingerprint == SigmaDurableSchema.KappaHash &&
                CertificateFingerprint == SigmaDurableSchema.CertificateHash,
                "Query-support summary fingerprint mismatch.");
            Require(EncodingVersion == LegacyVersion ||
                SupportPlanFingerprint == SigmaQuerySupportPlan.Fingerprint,
                "Query-support plan fingerprint mismatch.");
            Require((Flags & SigmaQuerySupportFlags.Verified) != 0 &&
                Flags == receipt.Flags,
                "Query-support summary/receipt flags mismatch.");
            Require(ActiveSampleCount == header.ActiveSampleCount,
                "Query-support summary active extent mismatch.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }

    internal readonly struct SigmaQuerySupportReceipt :
        IEquatable<SigmaQuerySupportReceipt>
    {
        internal SigmaQuerySupportReceipt(uint generation,
            SigmaQuerySupportFlags flags, SigmaDurableHash summaryHash)
        {
            Generation = generation;
            Flags = flags;
            SummaryHash = summaryHash;
        }

        internal uint Generation { get; }
        internal SigmaQuerySupportFlags Flags { get; }
        internal SigmaDurableHash SummaryHash { get; }
        // Missing, stale and unverified summaries conservatively contribute.
        internal bool MayContribute =>
            (Flags & SigmaQuerySupportFlags.Verified) == 0 ||
            (Flags & SigmaQuerySupportFlags.MayContribute) != 0;

        internal byte[] Encode()
        {
            using var stream = new MemoryStream(40);
            using var writer = new BinaryWriter(stream);
            writer.Write(Generation);
            writer.Write((uint)Flags);
            SummaryHash.Write(writer);
            writer.Flush();
            return stream.ToArray();
        }

        internal static SigmaQuerySupportReceipt Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            uint generation = reader.ReadUInt32();
            var flags = (SigmaQuerySupportFlags)reader.ReadUInt32();
            Require((flags & ~(SigmaQuerySupportFlags.MayContribute |
                SigmaQuerySupportFlags.Verified)) == 0,
                "Invalid query-support flags.");
            SigmaDurableHash hash = SigmaDurableHash.Read(reader);
            Require(stream.Position == stream.Length,
                "Trailing query-support receipt bytes.");
            return new SigmaQuerySupportReceipt(generation, flags, hash);
        }

        public bool Equals(SigmaQuerySupportReceipt other) =>
            Generation == other.Generation && Flags == other.Flags &&
            SummaryHash == other.SummaryHash;
        public override bool Equals(object obj) =>
            obj is SigmaQuerySupportReceipt other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(Generation, (uint)Flags, SummaryHash);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }

    internal sealed class SigmaDurablePageRecord
    {
        private const uint Magic = 0x5250354eu; // N5PR

        internal SigmaDurablePageRecord(SigmaCarrierPageCoordinate coordinate,
            uint stateGeneration, uint gaugeGeneration,
            uint certificateGeneration, uint pageGeneration, uint revision,
            SigmaDurableHash pageBlobHash,
            SigmaQuerySupportReceipt querySupport)
        {
            if (stateGeneration == 0u || pageGeneration == 0u || revision == 0u)
                throw new ArgumentOutOfRangeException(nameof(stateGeneration),
                    "A durable page record names nonzero generations/revision.");
            if (pageBlobHash.IsZero)
                throw new ArgumentException("A page record requires a page blob.",
                    nameof(pageBlobHash));
            Coordinate = coordinate;
            StateGeneration = stateGeneration;
            GaugeGeneration = gaugeGeneration;
            CertificateGeneration = certificateGeneration;
            PageGeneration = pageGeneration;
            Revision = revision;
            PageBlobHash = pageBlobHash;
            QuerySupport = querySupport;
        }

        internal SigmaCarrierPageCoordinate Coordinate { get; }
        internal uint StateGeneration { get; }
        internal uint GaugeGeneration { get; }
        internal uint CertificateGeneration { get; }
        internal uint PageGeneration { get; }
        internal uint Revision { get; }
        internal SigmaDurableHash PageBlobHash { get; }
        internal SigmaQuerySupportReceipt QuerySupport { get; }

        internal static SigmaDurablePageRecord FromPage(SigmaDecodedPage page,
            uint pageGeneration, SigmaDurableHash pageBlobHash,
            SigmaQuerySupportReceipt querySupport)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            return FromHeader(SigmaEncodedPageHeader.FromPage(page),
                pageGeneration, pageBlobHash, querySupport);
        }

        internal static SigmaDurablePageRecord FromHeader(
            SigmaEncodedPageHeader header, uint pageGeneration,
            SigmaDurableHash pageBlobHash,
            SigmaQuerySupportReceipt querySupport)
        {
            return new SigmaDurablePageRecord(header.Coordinate,
                header.Generation, header.GaugeGeneration,
                header.CertificateGeneration, pageGeneration,
                header.Revision, pageBlobHash, querySupport);
        }

        internal byte[] Encode()
        {
            using var stream = new MemoryStream(128);
            using var writer = new BinaryWriter(stream);
            writer.Write(Magic); writer.Write(SigmaDurableSchema.Version);
            writer.Write(Coordinate.X); writer.Write(Coordinate.Y);
            writer.Write(StateGeneration); writer.Write(GaugeGeneration);
            writer.Write(CertificateGeneration); writer.Write(PageGeneration);
            writer.Write(Revision);
            PageBlobHash.Write(writer);
            writer.Write(QuerySupport.Encode());
            writer.Flush();
            return stream.ToArray();
        }

        internal static SigmaDurablePageRecord Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == Magic &&
                reader.ReadUInt32() == SigmaDurableSchema.Version,
                "Invalid durable PageRecord header.");
            var coordinate = new SigmaCarrierPageCoordinate(reader.ReadInt64(),
                reader.ReadInt64());
            uint stateGeneration = reader.ReadUInt32();
            uint gaugeGeneration = reader.ReadUInt32();
            uint certificateGeneration = reader.ReadUInt32();
            uint pageGeneration = reader.ReadUInt32();
            uint revision = reader.ReadUInt32();
            SigmaDurableHash pageHash = SigmaDurableHash.Read(reader);
            byte[] supportBytes = reader.ReadBytes(40);
            Require(supportBytes.Length == 40,
                "Truncated PageRecord query-support receipt.");
            Require(stream.Position == stream.Length,
                "Trailing durable PageRecord bytes.");
            return new SigmaDurablePageRecord(coordinate, stateGeneration,
                gaugeGeneration, certificateGeneration, pageGeneration,
                revision, pageHash,
                SigmaQuerySupportReceipt.Decode(supportBytes));
        }

        internal void ValidatePageBytes(byte[] pageBytes)
        {
            if (pageBytes == null) throw new ArgumentNullException(nameof(pageBytes));
            Require(SigmaDurableHash.Compute(pageBytes) == PageBlobHash,
                "PageBlob hash mismatch.");
            SigmaDecodedPage page = DecodeVerifiedPageBytes(pageBytes);
            Require(SigmaDurableHash.Compute(
                SigmaCarrierCodec.EncodePage(page)) == PageBlobHash,
                "PageBlob is not canonical EncodePage bytes.");
        }

        internal SigmaEncodedPageHeader ValidateVerifiedPageObjectBytes(
            byte[] pageBytes)
        {
            if (pageBytes == null) throw new ArgumentNullException(nameof(pageBytes));
            SigmaEncodedPageHeader header =
                SigmaCarrierCodec.ReadPageHeader(pageBytes);
            Require(header.Coordinate.Equals(Coordinate),
                "PageBlob logical coordinate mismatch.");
            Require(header.Generation == StateGeneration &&
                header.GaugeGeneration == GaugeGeneration &&
                header.CertificateGeneration == CertificateGeneration &&
                header.Revision == Revision,
                "PageBlob generation/revision mismatch.");
            return header;
        }

        internal SigmaDecodedPage DecodeVerifiedPageBytes(byte[] pageBytes)
        {
            if (pageBytes == null) throw new ArgumentNullException(nameof(pageBytes));
            SigmaDecodedPage page = SigmaCarrierCodec.DecodePage(pageBytes);
            Require(page.Coordinate.Equals(Coordinate),
                "PageBlob logical coordinate mismatch.");
            Require(page.Generation == StateGeneration &&
                page.GaugeGeneration == GaugeGeneration &&
                page.CertificateGeneration == CertificateGeneration &&
                page.Revision == Revision,
                "PageBlob generation/revision mismatch.");
            return page;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }

    internal sealed class SigmaDurableRootObject
    {
        private const uint Magic = 0x4f52354eu; // N5RO

        internal SigmaDurableRootObject(ulong revision,
            SigmaDurableHash sparsePageMapRootHash,
            SigmaDurableHash querySupportRootHash,
            SigmaDurableHash certificateManifestRootHash,
            SigmaDurableHash unresolvedFrontierRootHash)
        {
            Revision = revision;
            SparsePageMapRootHash = sparsePageMapRootHash;
            QuerySupportRootHash = querySupportRootHash;
            CertificateManifestRootHash = certificateManifestRootHash;
            UnresolvedFrontierRootHash = unresolvedFrontierRootHash;
        }

        internal ulong Revision { get; }
        internal SigmaDurableHash SparsePageMapRootHash { get; }
        internal SigmaDurableHash QuerySupportRootHash { get; }
        internal SigmaDurableHash CertificateManifestRootHash { get; }
        internal SigmaDurableHash UnresolvedFrontierRootHash { get; }

        internal byte[] Encode()
        {
            using var stream = new MemoryStream(336);
            using var writer = new BinaryWriter(stream);
            writer.Write(Magic); writer.Write(SigmaDurableSchema.Version);
            writer.Write(Revision);
            SigmaDurableSchema.SchemaHash.Write(writer);
            SigmaDurableSchema.ProgramHash.Write(writer);
            SigmaDurableSchema.DefaultHash.Write(writer);
            SigmaDurableSchema.ChiHash.Write(writer);
            SigmaDurableSchema.KappaHash.Write(writer);
            SigmaDurableSchema.CertificateHash.Write(writer);
            SparsePageMapRootHash.Write(writer);
            QuerySupportRootHash.Write(writer);
            CertificateManifestRootHash.Write(writer);
            UnresolvedFrontierRootHash.Write(writer);
            writer.Flush();
            return stream.ToArray();
        }

        internal static SigmaDurableRootObject Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == Magic &&
                reader.ReadUInt32() == SigmaDurableSchema.Version,
                "Invalid durable RootObject header.");
            ulong revision = reader.ReadUInt64();
            Require(SigmaDurableHash.Read(reader) == SigmaDurableSchema.SchemaHash,
                "Durable schema fingerprint mismatch.");
            Require(SigmaDurableHash.Read(reader) == SigmaDurableSchema.ProgramHash,
                "Merkaba program fingerprint mismatch.");
            Require(SigmaDurableHash.Read(reader) == SigmaDurableSchema.DefaultHash,
                "Canonical default fingerprint mismatch.");
            Require(SigmaDurableHash.Read(reader) == SigmaDurableSchema.ChiHash,
                "Chi fingerprint mismatch.");
            Require(SigmaDurableHash.Read(reader) == SigmaDurableSchema.KappaHash,
                "Kappa fingerprint mismatch.");
            Require(SigmaDurableHash.Read(reader) ==
                SigmaDurableSchema.CertificateHash,
                "Certificate fingerprint mismatch.");
            SigmaDurableHash pageRoot = SigmaDurableHash.Read(reader);
            SigmaDurableHash supportRoot = SigmaDurableHash.Read(reader);
            SigmaDurableHash certificateRoot = SigmaDurableHash.Read(reader);
            SigmaDurableHash frontierRoot = SigmaDurableHash.Read(reader);
            Require(stream.Position == stream.Length,
                "Trailing durable RootObject bytes.");
            return new SigmaDurableRootObject(revision, pageRoot, supportRoot,
                certificateRoot, frontierRoot);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }

    internal static class SigmaDurableHead
    {
        private const uint Magic = 0x4448354eu; // N5HD

        internal static byte[] Encode(SigmaDurableHash rootObjectHash)
        {
            if (rootObjectHash.IsZero)
                throw new ArgumentException("HEAD requires a RootObject hash.",
                    nameof(rootObjectHash));
            using var stream = new MemoryStream(40);
            using var writer = new BinaryWriter(stream);
            writer.Write(Magic); writer.Write(SigmaDurableSchema.Version);
            rootObjectHash.Write(writer);
            writer.Flush();
            return stream.ToArray();
        }

        internal static SigmaDurableHash Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == Magic &&
                reader.ReadUInt32() == SigmaDurableSchema.Version,
                "Invalid durable HEAD selector.");
            SigmaDurableHash root = SigmaDurableHash.Read(reader);
            Require(!root.IsZero && stream.Position == stream.Length,
                "Invalid durable HEAD RootObject hash.");
            return root;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }

    internal interface ISigmaDurableObjectStore
    {
        // A successful read returns complete bytes already verified against the
        // immutable content address. Consumers validate object framing/meaning,
        // but must not hash the same bytes a second time.
        bool TryRead(SigmaDurableHash hash, out byte[] bytes);
        SigmaDurableHash Put(byte[] bytes, SigmaDurableObjectKind kind,
            out bool created);
    }

    internal enum SigmaDurableObjectKind : byte
    {
        PageBlob = 1,
        PageMapNode = 2,
        QuerySupportMapNode = 3,
        CertificateManifest = 4,
        UnresolvedFrontier = 5,
        RootObject = 6,
        QuerySupportSummary = 7,
    }

    internal enum SigmaSparseValueKind : byte
    {
        PageRecord = 1,
        QuerySupport = 2,
    }

    internal readonly struct SigmaSparseUpdateResult
    {
        internal SigmaSparseUpdateResult(SigmaDurableHash root,
            int nodesCreated, bool changed)
        {
            Root = root;
            NodesCreated = nodesCreated;
            Changed = changed;
        }

        internal SigmaDurableHash Root { get; }
        internal int NodesCreated { get; }
        internal bool Changed { get; }
    }

    internal readonly struct SigmaSparseMapUpdate
    {
        internal SigmaSparseMapUpdate(SigmaCarrierPageCoordinate coordinate,
            byte[] canonicalValue)
        {
            Coordinate = coordinate;
            CanonicalValue = canonicalValue ?? throw new ArgumentNullException(
                nameof(canonicalValue));
        }

        internal SigmaCarrierPageCoordinate Coordinate { get; }
        internal byte[] CanonicalValue { get; }
    }

    /// <summary>
    /// Immutable 16-way, 32-level COW radix/Merkle map over the full signed
    /// 128-bit page coordinate. The radix route is storage-only; every leaf
    /// retains and compares the complete logical coordinate and canonical value.
    /// </summary>
    internal static class SigmaSparseMerkleMap
    {
        private const uint InternalMagic = 0x494d354eu; // N5MI
        private const uint LeafMagic = 0x4c4d354eu; // N5ML
        private const int Depth = 32;

        internal static SigmaSparseUpdateResult Set(
            ISigmaDurableObjectStore store, SigmaDurableHash root,
            SigmaSparseValueKind kind, SigmaCarrierPageCoordinate coordinate,
            byte[] canonicalValue)
        {
            return SetBatch(store, root, kind, new[]
            {
                new SigmaSparseMapUpdate(coordinate, canonicalValue),
            });
        }

        internal static SigmaSparseUpdateResult SetBatch(
            ISigmaDurableObjectStore store, SigmaDurableHash root,
            SigmaSparseValueKind kind,
            IReadOnlyList<SigmaSparseMapUpdate> updates)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (updates == null) throw new ArgumentNullException(nameof(updates));
            if (updates.Count == 0)
                return new SigmaSparseUpdateResult(root, 0, false);
            var entries = new BatchEntry[updates.Count];
            var coordinates = new HashSet<SigmaCarrierPageCoordinate>();
            for (int index = 0; index < updates.Count; ++index)
            {
                SigmaSparseMapUpdate update = updates[index];
                if (!coordinates.Add(update.Coordinate))
                    throw new InvalidDataException(
                        "A sparse-map batch contains a duplicate logical key.");
                entries[index] = new BatchEntry(update.Coordinate,
                    update.CanonicalValue, KeyBytes(update.Coordinate));
            }
            Array.Sort(entries, (left, right) =>
                CompareBytes(left.Key, right.Key));
            int created = 0;
            SigmaDurableHash next = SetBatchAt(store, root, kind, entries,
                0, entries.Length, 0, ref created, out bool changed);
            return new SigmaSparseUpdateResult(next, created, changed);
        }

        internal static bool TryGet(ISigmaDurableObjectStore store,
            SigmaDurableHash root, SigmaSparseValueKind kind,
            SigmaCarrierPageCoordinate coordinate, out byte[] canonicalValue)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            canonicalValue = null;
            if (root.IsZero) return false;
            byte[] key = KeyBytes(coordinate);
            SigmaDurableHash current = root;
            for (int depth = 0; depth < Depth; ++depth)
            {
                InternalNode node = ReadInternal(store, current, kind, depth);
                int child = Nibble(key, depth);
                current = node.Children[child];
                if (current.IsZero) return false;
            }
            LeafNode leaf = ReadLeaf(store, current, kind);
            if (!leaf.Coordinate.Equals(coordinate))
                throw new InvalidDataException(
                    "Sparse-map route reached a different full logical key.");
            canonicalValue = leaf.Value;
            return true;
        }

        internal static int VerifyReachable(ISigmaDurableObjectStore store,
            SigmaDurableHash root, SigmaSparseValueKind kind)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var visited = new HashSet<SigmaDurableHash>();
            VerifyAt(store, root, kind, 0, visited);
            return visited.Count;
        }

        internal static void Visit(ISigmaDurableObjectStore store,
            SigmaDurableHash root, SigmaSparseValueKind kind,
            Action<SigmaCarrierPageCoordinate, byte[]> visitor)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            VisitAt(store, root, kind, 0, visitor);
        }

        private static SigmaDurableHash SetBatchAt(
            ISigmaDurableObjectStore store, SigmaDurableHash current,
            SigmaSparseValueKind kind, BatchEntry[] entries, int begin, int end,
            int depth, ref int created, out bool changed)
        {
            if (depth == Depth)
            {
                Require(end == begin + 1,
                    "A complete sparse-map route must name one full key.");
                BatchEntry entry = entries[begin];
                if (!current.IsZero)
                {
                    LeafNode existing = ReadLeaf(store, current, kind);
                    Require(existing.Coordinate.Equals(entry.Coordinate),
                        "Sparse-map route reached a different full logical key.");
                    if (BytesEqual(existing.Value, entry.Value))
                    {
                        changed = false;
                        return current;
                    }
                }
                changed = true;
                return Put(store, EncodeLeaf(kind, entry.Coordinate,
                    entry.Value),
                    ObjectKind(kind), ref created);
            }

            InternalNode node = current.IsZero
                ? new InternalNode(new SigmaDurableHash[16])
                : ReadInternal(store, current, kind, depth);
            SigmaDurableHash[] children = null;
            int cursor = begin;
            while (cursor < end)
            {
                int child = Nibble(entries[cursor].Key, depth);
                int childEnd = cursor + 1;
                while (childEnd < end &&
                    Nibble(entries[childEnd].Key, depth) == child)
                    ++childEnd;
                SigmaDurableHash nextChild = SetBatchAt(store,
                    node.Children[child], kind, entries, cursor, childEnd,
                    depth + 1, ref created, out bool childChanged);
                if (childChanged)
                {
                    children ??= (SigmaDurableHash[])node.Children.Clone();
                    children[child] = nextChild;
                }
                cursor = childEnd;
            }
            if (children == null)
            {
                changed = false;
                return current;
            }
            changed = true;
            return Put(store, EncodeInternal(kind, depth, children),
                ObjectKind(kind), ref created);
        }

        private static SigmaDurableHash Put(ISigmaDurableObjectStore store,
            byte[] bytes, SigmaDurableObjectKind kind, ref int created)
        {
            SigmaDurableHash hash = store.Put(bytes, kind, out bool wasCreated);
            if (wasCreated) ++created;
            return hash;
        }

        private static void VerifyAt(ISigmaDurableObjectStore store,
            SigmaDurableHash hash, SigmaSparseValueKind kind, int depth,
            HashSet<SigmaDurableHash> visited)
        {
            if (hash.IsZero || !visited.Add(hash)) return;
            if (depth == Depth)
            {
                ReadLeaf(store, hash, kind);
                return;
            }
            InternalNode node = ReadInternal(store, hash, kind, depth);
            for (int child = 0; child < node.Children.Length; ++child)
                VerifyAt(store, node.Children[child], kind, depth + 1, visited);
        }

        private static void VisitAt(ISigmaDurableObjectStore store,
            SigmaDurableHash hash, SigmaSparseValueKind kind, int depth,
            Action<SigmaCarrierPageCoordinate, byte[]> visitor)
        {
            if (hash.IsZero) return;
            if (depth == Depth)
            {
                LeafNode leaf = ReadLeaf(store, hash, kind);
                visitor(leaf.Coordinate, leaf.Value);
                return;
            }
            InternalNode node = ReadInternal(store, hash, kind, depth);
            for (int child = 0; child < node.Children.Length; ++child)
                VisitAt(store, node.Children[child], kind, depth + 1, visitor);
        }

        private static byte[] EncodeInternal(SigmaSparseValueKind kind,
            int depth, SigmaDurableHash[] children)
        {
            if (children == null || children.Length != 16)
                throw new ArgumentException("A radix node has 16 children.",
                    nameof(children));
            ushort bitmap = 0;
            for (int index = 0; index < children.Length; ++index)
                if (!children[index].IsZero) bitmap |= (ushort)(1 << index);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(InternalMagic); writer.Write(SigmaDurableSchema.Version);
            writer.Write((byte)kind); writer.Write((byte)depth);
            writer.Write(bitmap);
            for (int index = 0; index < children.Length; ++index)
                if (!children[index].IsZero) children[index].Write(writer);
            writer.Flush();
            return stream.ToArray();
        }

        private static InternalNode ReadInternal(ISigmaDurableObjectStore store,
            SigmaDurableHash hash, SigmaSparseValueKind kind, int depth)
        {
            byte[] bytes = ReadVerified(store, hash);
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == InternalMagic &&
                reader.ReadUInt32() == SigmaDurableSchema.Version &&
                reader.ReadByte() == (byte)kind && reader.ReadByte() == depth,
                "Invalid sparse-map internal node.");
            ushort bitmap = reader.ReadUInt16();
            var children = new SigmaDurableHash[16];
            for (int index = 0; index < children.Length; ++index)
                if ((bitmap & (1 << index)) != 0)
                    children[index] = SigmaDurableHash.Read(reader);
            Require(stream.Position == stream.Length,
                "Trailing sparse-map internal-node bytes.");
            return new InternalNode(children);
        }

        private static byte[] EncodeLeaf(SigmaSparseValueKind kind,
            SigmaCarrierPageCoordinate coordinate, byte[] value)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(LeafMagic); writer.Write(SigmaDurableSchema.Version);
            writer.Write((byte)kind); writer.Write((byte)Depth);
            writer.Write((ushort)0u);
            writer.Write(coordinate.X); writer.Write(coordinate.Y);
            writer.Write((uint)value.Length); writer.Write(value);
            writer.Flush();
            return stream.ToArray();
        }

        private static LeafNode ReadLeaf(ISigmaDurableObjectStore store,
            SigmaDurableHash hash, SigmaSparseValueKind kind)
        {
            byte[] bytes = ReadVerified(store, hash);
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == LeafMagic &&
                reader.ReadUInt32() == SigmaDurableSchema.Version &&
                reader.ReadByte() == (byte)kind && reader.ReadByte() == Depth,
                "Invalid sparse-map leaf.");
            Require(reader.ReadUInt16() == 0u, "Invalid sparse-map leaf padding.");
            var coordinate = new SigmaCarrierPageCoordinate(reader.ReadInt64(),
                reader.ReadInt64());
            uint length = reader.ReadUInt32();
            Require(length <= int.MaxValue, "Sparse-map leaf value is too large.");
            byte[] value = reader.ReadBytes((int)length);
            Require(value.Length == (int)length && stream.Position == stream.Length,
                "Truncated sparse-map leaf value.");
            return new LeafNode(coordinate, value);
        }

        private static byte[] ReadVerified(ISigmaDurableObjectStore store,
            SigmaDurableHash hash)
        {
            Require(store.TryRead(hash, out byte[] bytes),
                $"Missing immutable object {hash}.");
            return bytes;
        }

        private static byte[] KeyBytes(SigmaCarrierPageCoordinate coordinate)
        {
            var bytes = new byte[16];
            WriteOrderedI64(bytes, 0, coordinate.X);
            WriteOrderedI64(bytes, 8, coordinate.Y);
            return bytes;
        }

        private static SigmaDurableObjectKind ObjectKind(
            SigmaSparseValueKind kind) => kind switch
            {
                SigmaSparseValueKind.PageRecord =>
                    SigmaDurableObjectKind.PageMapNode,
                SigmaSparseValueKind.QuerySupport =>
                    SigmaDurableObjectKind.QuerySupportMapNode,
                _ => throw new InvalidDataException("Unknown sparse-map value kind."),
            };

        private static void WriteOrderedI64(byte[] bytes, int offset, long value)
        {
            ulong ordered = unchecked((ulong)value) ^ 0x8000000000000000UL;
            for (int index = 0; index < 8; ++index)
                bytes[offset + index] = (byte)(ordered >> ((7 - index) * 8));
        }

        private static int Nibble(byte[] key, int depth)
        {
            byte value = key[depth >> 1];
            return (depth & 1) == 0 ? value >> 4 : value & 15;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int index = 0; index < left.Length; ++index)
                if (left[index] != right[index]) return false;
            return true;
        }

        private static int CompareBytes(byte[] left, byte[] right)
        {
            for (int index = 0; index < left.Length; ++index)
            {
                int comparison = left[index].CompareTo(right[index]);
                if (comparison != 0) return comparison;
            }
            return 0;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }

        private readonly struct InternalNode
        {
            internal InternalNode(SigmaDurableHash[] children) =>
                Children = children;
            internal SigmaDurableHash[] Children { get; }
        }

        private readonly struct LeafNode
        {
            internal LeafNode(SigmaCarrierPageCoordinate coordinate, byte[] value)
            {
                Coordinate = coordinate;
                Value = value;
            }
            internal SigmaCarrierPageCoordinate Coordinate { get; }
            internal byte[] Value { get; }
        }

        private readonly struct BatchEntry
        {
            internal BatchEntry(SigmaCarrierPageCoordinate coordinate,
                byte[] value, byte[] key)
            {
                Coordinate = coordinate;
                Value = value;
                Key = key;
            }

            internal SigmaCarrierPageCoordinate Coordinate { get; }
            internal byte[] Value { get; }
            internal byte[] Key { get; }
        }
    }

    internal enum SigmaDurableFailurePoint
    {
        ObjectTempCreated,
        ObjectWritten,
        ObjectFlushed,
        ObjectInstalled,
        CowMapNodeInstalled,
        SupportSummaryNodeInstalled,
        RootObjectWritten,
        RootObjectFlushed,
        BeforeHeadSwap,
        AfterHeadSwap,
        HeadDirectoryFlushed,
    }

    internal sealed class SigmaDurablePageUpdate
    {
        internal SigmaDurablePageUpdate(SigmaDecodedPage page,
            uint pageGeneration, SigmaQuerySupportReceipt querySupport,
            byte[] stagedCanonicalPageBytes = null,
            byte[] querySupportSummaryBytes = null)
        {
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Header = SigmaEncodedPageHeader.FromPage(page);
            if (pageGeneration == 0u)
                throw new ArgumentOutOfRangeException(nameof(pageGeneration));
            PageGeneration = pageGeneration;
            QuerySupport = querySupport;
            StagedCanonicalPageBytes = stagedCanonicalPageBytes == null ? null :
                (byte[])stagedCanonicalPageBytes.Clone();
            QuerySupportSummaryBytes = querySupportSummaryBytes == null ? null :
                (byte[])querySupportSummaryBytes.Clone();
        }

        private SigmaDurablePageUpdate(SigmaDecodedPage page,
            SigmaEncodedPageHeader header,
            uint pageGeneration, SigmaQuerySupportReceipt querySupport,
            byte[] stagedCanonicalPageBytes,
            byte[] querySupportSummaryBytes, bool directCodecParityProven,
            bool takeStageOwnership)
        {
            Page = page;
            Header = header;
            if (header.Generation == 0u || header.Revision == 0u)
                throw new ArgumentException(
                    "A staged durable page requires published metadata.",
                    nameof(header));
            if (pageGeneration == 0u)
                throw new ArgumentOutOfRangeException(nameof(pageGeneration));
            PageGeneration = pageGeneration;
            QuerySupport = querySupport;
            if (stagedCanonicalPageBytes == null)
                throw new ArgumentNullException(nameof(stagedCanonicalPageBytes));
            if (querySupportSummaryBytes == null)
                throw new ArgumentNullException(nameof(querySupportSummaryBytes));
            StagedCanonicalPageBytes = takeStageOwnership ?
                stagedCanonicalPageBytes :
                (byte[])stagedCanonicalPageBytes.Clone();
            QuerySupportSummaryBytes = takeStageOwnership ?
                querySupportSummaryBytes :
                (byte[])querySupportSummaryBytes.Clone();
            DirectCodecParityProven = directCodecParityProven;
        }

        internal static SigmaDurablePageUpdate FromDirectCodecVerifiedStage(
            SigmaEncodedPageHeader header, uint pageGeneration,
            SigmaQuerySupportReceipt querySupport,
            byte[] stagedCanonicalPageBytes,
            byte[] querySupportSummaryBytes) =>
            new SigmaDurablePageUpdate(null, header, pageGeneration,
                querySupport, stagedCanonicalPageBytes,
                querySupportSummaryBytes, true, true);

        internal SigmaDecodedPage Page { get; }
        internal SigmaEncodedPageHeader Header { get; }
        internal SigmaCarrierPageCoordinate Coordinate => Header.Coordinate;
        internal uint StateGeneration => Header.Generation;
        internal uint Revision => Header.Revision;
        internal ulong CertificateOffset => Header.CertificateOffset;
        internal uint CertificateCount => Header.CertificateCount;
        internal uint GaugeGeneration => Header.GaugeGeneration;
        internal uint CertificateGeneration => Header.CertificateGeneration;
        internal uint RepresentationFlags => Header.RepresentationFlags;
        internal uint ActiveSampleCount => Header.ActiveSampleCount;
        internal uint PageGeneration { get; }
        internal SigmaQuerySupportReceipt QuerySupport { get; }
        internal byte[] StagedCanonicalPageBytes { get; }
        internal byte[] QuerySupportSummaryBytes { get; }
        internal bool DirectCodecParityProven { get; }
    }

    internal sealed class SigmaDurableCommitRequest
    {
        internal SigmaDurableCommitRequest(ulong revision,
            IReadOnlyList<SigmaDurablePageUpdate> dirtyPages,
            byte[] certificateManifest = null, byte[] unresolvedFrontier = null)
        {
            if (revision == 0UL)
                throw new ArgumentOutOfRangeException(nameof(revision));
            Revision = revision;
            DirtyPages = dirtyPages ??
                throw new ArgumentNullException(nameof(dirtyPages));
            CertificateManifest = certificateManifest == null ? null :
                (byte[])certificateManifest.Clone();
            UnresolvedFrontier = unresolvedFrontier == null ? null :
                (byte[])unresolvedFrontier.Clone();
        }

        internal ulong Revision { get; }
        internal IReadOnlyList<SigmaDurablePageUpdate> DirtyPages { get; }
        internal byte[] CertificateManifest { get; }
        internal byte[] UnresolvedFrontier { get; }
    }

    internal readonly struct SigmaDurableCommitStatistics
    {
        internal SigmaDurableCommitStatistics(int pageBlobs, int pageMapNodes,
            int supportMapNodes, int immutableObjects, long bytesWritten,
            bool headSwapped, double pageObjectMilliseconds = 0.0,
            double cowMilliseconds = 0.0,
            double otherObjectMilliseconds = 0.0,
            double objectFlushMilliseconds = 0.0,
            double headPublishMilliseconds = 0.0)
        {
            PageBlobs = pageBlobs;
            PageMapNodes = pageMapNodes;
            SupportMapNodes = supportMapNodes;
            ImmutableObjects = immutableObjects;
            BytesWritten = bytesWritten;
            HeadSwapped = headSwapped;
            PageObjectMilliseconds = pageObjectMilliseconds;
            CowMilliseconds = cowMilliseconds;
            OtherObjectMilliseconds = otherObjectMilliseconds;
            ObjectFlushMilliseconds = objectFlushMilliseconds;
            HeadPublishMilliseconds = headPublishMilliseconds;
        }

        internal int PageBlobs { get; }
        internal int PageMapNodes { get; }
        internal int SupportMapNodes { get; }
        internal int ImmutableObjects { get; }
        internal long BytesWritten { get; }
        internal bool HeadSwapped { get; }
        internal double PageObjectMilliseconds { get; }
        internal double CowMilliseconds { get; }
        internal double OtherObjectMilliseconds { get; }
        internal double ObjectFlushMilliseconds { get; }
        internal double HeadPublishMilliseconds { get; }
    }

    internal readonly struct SigmaDurableCommitResult
    {
        internal SigmaDurableCommitResult(SigmaDurableHash rootObjectHash,
            SigmaDurableRootObject rootObject,
            SigmaDurableCommitStatistics statistics)
        {
            RootObjectHash = rootObjectHash;
            RootObject = rootObject;
            Statistics = statistics;
        }

        internal SigmaDurableHash RootObjectHash { get; }
        internal SigmaDurableRootObject RootObject { get; }
        internal SigmaDurableCommitStatistics Statistics { get; }
    }

    internal sealed class SigmaDurableRootLease : IDisposable
    {
        private SigmaDurableStore _owner;

        internal SigmaDurableRootLease(SigmaDurableStore owner,
            SigmaDurableHash hash, SigmaDurableRootObject root)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Hash = hash;
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        internal SigmaDurableHash Hash { get; }
        internal SigmaDurableRootObject Root { get; }
        internal bool IsValid => _owner != null;

        internal void RequireOwner(SigmaDurableStore owner)
        {
            if (!ReferenceEquals(_owner, owner))
                throw new ObjectDisposedException(nameof(SigmaDurableRootLease));
        }

        public void Dispose()
        {
            SigmaDurableStore owner = _owner;
            if (owner == null) return;
            _owner = null;
            owner.ReleaseRoot(Hash);
        }
    }

    /// <summary>
    /// Sole N5 durable object/root owner. Immutable objects are content-named;
    /// only the same-filesystem HEAD rename publishes a new durable revision.
    /// </summary>
    internal sealed class SigmaDurableStore : ISigmaDurableObjectStore
    {
        private const uint ManifestMagic = 0x464d354eu; // N5MF
        internal const int AndroidDirectoryOpenFlag = 0x4000;
        internal const int LinuxDirectoryOpenFlag = 0x10000;
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        internal const int CurrentDirectoryOpenFlag = LinuxDirectoryOpenFlag;
#elif UNITY_ANDROID
        internal const int CurrentDirectoryOpenFlag = AndroidDirectoryOpenFlag;
#endif
        private readonly object _gate = new();
        private readonly string _rootDirectory;
        private readonly string _objectDirectory;
        private readonly string _headPath;
        private readonly Func<byte[], SigmaDurableHash> _hash;
        private readonly Action<SigmaDurableFailurePoint,
            SigmaDurableObjectKind> _failure;
        private readonly Dictionary<SigmaDurableHash, int> _rootPins = new();
        private readonly SigmaQuerySupportIndex _querySupportIndex = new();
        private SigmaDurableHash _headHash;
        private SigmaDurableRootObject _head;
        private bool _objectBatchOpen;
        private bool _objectBatchDirty;
        private SigmaDurableHash _inventoryRootHash;
        private SigmaDurablePageRecord[] _inventoryRecords;
        private uint _inventoryLogicalExtent;
        private bool _inventoryLogicalExtentValid;
        private long _inventoryPageBytes;
        private readonly Dictionary<SigmaCarrierPageCoordinate,
            SigmaDurablePageRecord> _headRecordIndex = new();
        private SigmaDurableHash _headRecordIndexRootHash;
        private long _headRecordIndexLookups;

        internal SigmaDurableStore(string rootDirectory,
            Action<SigmaDurableFailurePoint, SigmaDurableObjectKind> failure = null,
            Func<byte[], SigmaDurableHash> hash = null)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("A durable store path is required.",
                    nameof(rootDirectory));
            _rootDirectory = rootDirectory;
            _objectDirectory = Path.Combine(rootDirectory, "objects");
            _headPath = Path.Combine(rootDirectory, "HEAD");
            _failure = failure;
            _hash = hash ?? SigmaDurableHash.Compute;
            Directory.CreateDirectory(_rootDirectory);
            Directory.CreateDirectory(_objectDirectory);
            LoadHead();
        }

        internal bool HasHead => !_headHash.IsZero;
        internal SigmaDurableHash HeadHash => _headHash;
        internal SigmaDurableRootObject Head => _head;
        internal int HeadInventoryPageCount => _inventoryRecords?.Length ?? 0;
        internal long HeadInventoryPageBytes => _inventoryPageBytes;
        internal int HeadRecordIndexPageCount
        {
            get
            {
                lock (_gate) return _headRecordIndex.Count;
            }
        }
        internal long HeadRecordIndexLookups
        {
            get
            {
                lock (_gate) return _headRecordIndexLookups;
            }
        }
        internal int QuerySupportPageCount => _querySupportIndex.Count;
        internal int QuerySupportUnboundedCount =>
            _querySupportIndex.UnboundedCount;
        internal int LastQuerySupportVisitedNodes =>
            _querySupportIndex.LastVisitedNodes;

        internal IReadOnlyList<SigmaResidencyKey> SelectPredictionSupport(
            SigmaQ48Bounds3 queryBounds) =>
            _querySupportIndex.Query(queryBounds);

        internal SigmaDurableRootLease PinHead()
        {
            lock (_gate)
            {
                if (_head == null || _headHash.IsZero)
                    throw new InvalidOperationException(
                        "The durable namespace has no selected HEAD.");
                _rootPins.TryGetValue(_headHash, out int count);
                _rootPins[_headHash] = checked(count + 1);
                return new SigmaDurableRootLease(this, _headHash, _head);
            }
        }

        internal Task<SigmaDurableCommitResult> CommitAsync(
            SigmaDurableCommitRequest request) =>
            Task.Run(() => Commit(request));

        internal Task<SigmaDurableCommitResult> SelectEmptyAsync(
            ulong revision) => Task.Run(() => SelectEmpty(revision));

        internal SigmaDurableCommitResult SelectEmpty(ulong revision)
        {
            if (revision == 0UL)
                throw new ArgumentOutOfRangeException(nameof(revision));
            lock (_gate)
            {
                if (_head != null && revision <= _head.Revision)
                    throw new InvalidOperationException(
                        "An empty durable root must advance HEAD revision.");
                var empty = new SigmaDurableRootObject(revision,
                    SigmaDurableHash.Zero, SigmaDurableHash.Zero,
                    SigmaDurableHash.Zero, SigmaDurableHash.Zero);
                byte[] bytes = empty.Encode();
                SigmaDurableHash hash = Put(bytes,
                    SigmaDurableObjectKind.RootObject, out bool created);
                PublishHead(hash);
                _headHash = hash;
                _head = empty;
                SetHeadInventory(hash, Array.Empty<SigmaDurablePageRecord>(),
                    0u, true, 0L);
                _querySupportIndex.Clear();
                return new SigmaDurableCommitResult(hash, empty,
                    new SigmaDurableCommitStatistics(0, 0, 0,
                        created ? 1 : 0, created ? bytes.Length : 0, true));
            }
        }

        internal SigmaDurableCommitResult Commit(SigmaDurableCommitRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            lock (_gate)
            {
                BeginObjectBatch();
                try
                {
                if (_head != null && request.Revision < _head.Revision)
                    throw new InvalidOperationException(
                        "A durable commit cannot regress HEAD revision.");
                SigmaDurableHash pageRoot = _head?.SparsePageMapRootHash ??
                    SigmaDurableHash.Zero;
                SigmaDurableHash supportRoot = _head?.QuerySupportRootHash ??
                    SigmaDurableHash.Zero;
                SigmaDurableHash certificateRoot =
                    _head?.CertificateManifestRootHash ?? SigmaDurableHash.Zero;
                SigmaDurableHash frontierRoot =
                    _head?.UnresolvedFrontierRootHash ?? SigmaDurableHash.Zero;
                int pageBlobs = 0;
                int pageNodes = 0;
                int supportNodes = 0;
                int immutableObjects = 0;
                long bytesWritten = 0L;
                var coordinates = new HashSet<SigmaCarrierPageCoordinate>();
                var pageMapUpdates = new List<SigmaSparseMapUpdate>(
                    request.DirtyPages.Count);
                var supportMapUpdates = new List<SigmaSparseMapUpdate>(
                    request.DirtyPages.Count);
                var queryIndexUpdates = new List<(SigmaDurablePageRecord Record,
                    SigmaQuerySupportSummary? Summary)>(
                        request.DirtyPages.Count);
                long pageObjectBegin =
                    System.Diagnostics.Stopwatch.GetTimestamp();

                for (int index = 0; index < request.DirtyPages.Count; ++index)
                {
                    SigmaDurablePageUpdate update = request.DirtyPages[index] ??
                        throw new ArgumentException("A dirty page update is null.");
                    if (!coordinates.Add(update.Coordinate))
                        throw new InvalidDataException(
                            "A transaction contains duplicate logical page keys.");
                    byte[] pageBytes = update.StagedCanonicalPageBytes;
                    if (pageBytes == null)
                    {
                        if (update.Page == null)
                            throw new InvalidDataException(
                                "A metadata-only update requires staged page bytes.");
                        pageBytes = SigmaCarrierCodec.EncodePage(update.Page);
                    }
                    if (update.DirectCodecParityProven)
                        ValidateCanonicalPageStageHeader(update.Header,
                            pageBytes);
                    else
                    {
                        if (update.Page == null)
                            throw new InvalidDataException(
                                "An oracle page stage requires decoded page truth.");
                        ValidateCanonicalPageStage(update.Page, pageBytes);
                    }
                    SigmaDurableHash pageHash = Put(pageBytes,
                        SigmaDurableObjectKind.PageBlob, out bool pageCreated);
                    if (pageCreated)
                    {
                        ++pageBlobs; ++immutableObjects;
                        bytesWritten += pageBytes.Length;
                    }
                    ValidateSupportStage(update);
                    SigmaQuerySupportSummary? supportSummary = null;
                    if (update.QuerySupportSummaryBytes != null)
                    {
                        supportSummary = SigmaQuerySupportSummary.Decode(
                            update.QuerySupportSummaryBytes);
                        SigmaDurableHash summaryHash = Put(
                            update.QuerySupportSummaryBytes,
                            SigmaDurableObjectKind.QuerySupportSummary,
                            out bool summaryCreated);
                        Require(summaryHash == update.QuerySupport.SummaryHash,
                            "Query-support summary hash/receipt mismatch.");
                        if (summaryCreated)
                        {
                            ++immutableObjects;
                            bytesWritten += update.QuerySupportSummaryBytes.Length;
                        }
                    }
                    SigmaDurablePageRecord record =
                        SigmaDurablePageRecord.FromHeader(update.Header,
                            update.PageGeneration, pageHash, update.QuerySupport);
                    pageMapUpdates.Add(new SigmaSparseMapUpdate(
                        update.Coordinate, record.Encode()));
                    supportMapUpdates.Add(new SigmaSparseMapUpdate(
                        update.Coordinate, update.QuerySupport.Encode()));
                    queryIndexUpdates.Add((record, supportSummary));
                }
                long pageObjectEnd =
                    System.Diagnostics.Stopwatch.GetTimestamp();

                SigmaSparseUpdateResult pageMapUpdate =
                    SigmaSparseMerkleMap.SetBatch(this, pageRoot,
                        SigmaSparseValueKind.PageRecord, pageMapUpdates);
                SigmaSparseUpdateResult supportMapUpdate =
                    SigmaSparseMerkleMap.SetBatch(this, supportRoot,
                        SigmaSparseValueKind.QuerySupport, supportMapUpdates);
                pageRoot = pageMapUpdate.Root;
                supportRoot = supportMapUpdate.Root;
                pageNodes = pageMapUpdate.NodesCreated;
                supportNodes = supportMapUpdate.NodesCreated;
                immutableObjects += pageNodes + supportNodes;
                long cowEnd = System.Diagnostics.Stopwatch.GetTimestamp();

                if (request.CertificateManifest != null)
                    certificateRoot = PutManifest(
                        SigmaDurableObjectKind.CertificateManifest,
                        request.CertificateManifest, ref immutableObjects,
                        ref bytesWritten);
                if (request.UnresolvedFrontier != null)
                    frontierRoot = PutManifest(
                        SigmaDurableObjectKind.UnresolvedFrontier,
                        request.UnresolvedFrontier, ref immutableObjects,
                        ref bytesWritten);
                long otherObjectEnd =
                    System.Diagnostics.Stopwatch.GetTimestamp();

                bool changed = _head == null || pageRoot !=
                    _head.SparsePageMapRootHash || supportRoot !=
                    _head.QuerySupportRootHash || certificateRoot !=
                    _head.CertificateManifestRootHash || frontierRoot !=
                    _head.UnresolvedFrontierRootHash;
                if (!changed)
                {
                    CompleteObjectBatch();
                    long noChangeFlushEnd =
                        System.Diagnostics.Stopwatch.GetTimestamp();
                    return new SigmaDurableCommitResult(_headHash, _head,
                        new SigmaDurableCommitStatistics(pageBlobs, pageNodes,
                            supportNodes, immutableObjects, bytesWritten, false,
                            CommitMilliseconds(pageObjectBegin, pageObjectEnd),
                            CommitMilliseconds(pageObjectEnd, cowEnd),
                            CommitMilliseconds(cowEnd, otherObjectEnd),
                            CommitMilliseconds(otherObjectEnd,
                                noChangeFlushEnd)));
                }
                if (_head != null && request.Revision <= _head.Revision)
                {
                    bool frontierOnlyAtSelectedRevision =
                        request.Revision == _head.Revision &&
                        pageRoot == _head.SparsePageMapRootHash &&
                        supportRoot == _head.QuerySupportRootHash &&
                        certificateRoot ==
                            _head.CertificateManifestRootHash;
                    if (!frontierOnlyAtSelectedRevision)
                        throw new InvalidOperationException(
                            "Only an unresolved-frontier update may advance " +
                            "HEAD at the same selected GPU revision.");
                }

                var next = new SigmaDurableRootObject(request.Revision, pageRoot,
                    supportRoot, certificateRoot, frontierRoot);
                byte[] rootBytes = next.Encode();
                SigmaDurableHash nextHash = Put(rootBytes,
                    SigmaDurableObjectKind.RootObject, out bool rootCreated);
                if (rootCreated)
                {
                    ++immutableObjects;
                    bytesWritten += rootBytes.Length;
                }
                long rootObjectEnd =
                    System.Diagnostics.Stopwatch.GetTimestamp();
                CompleteObjectBatch();
                long flushEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                PublishHead(nextHash);
                long headEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                SigmaDurableHash priorHeadHash = _headHash;
                _headHash = nextHash;
                _head = next;
                InvalidateHeadInventory();
                AdvanceHeadRecordIndex(priorHeadHash, nextHash,
                    queryIndexUpdates);
                for (int index = 0; index < queryIndexUpdates.Count; ++index)
                    _querySupportIndex.Upsert(
                        queryIndexUpdates[index].Record,
                        queryIndexUpdates[index].Summary);
                return new SigmaDurableCommitResult(nextHash, next,
                    new SigmaDurableCommitStatistics(pageBlobs, pageNodes,
                        supportNodes, immutableObjects, bytesWritten, true,
                        CommitMilliseconds(pageObjectBegin, pageObjectEnd),
                        CommitMilliseconds(pageObjectEnd, cowEnd),
                        CommitMilliseconds(cowEnd, rootObjectEnd),
                        CommitMilliseconds(rootObjectEnd, flushEnd),
                        CommitMilliseconds(flushEnd, headEnd)));
                }
                finally
                {
                    AbortObjectBatch();
                }
            }
        }

        public bool TryRead(SigmaDurableHash hash, out byte[] bytes)
        {
            bytes = null;
            if (hash.IsZero) return false;
            string path = ObjectPath(hash);
            if (!File.Exists(path)) return false;
            byte[] stored = File.ReadAllBytes(path);
            Require(SigmaDurableHash.Compute(stored) == hash,
                $"Immutable object {hash} failed SHA-256 validation.");
            bytes = stored;
            return true;
        }

        public SigmaDurableHash Put(byte[] bytes, SigmaDurableObjectKind kind,
            out bool created)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            SigmaDurableHash hash = _hash(bytes);
            if (hash.IsZero)
                throw new InvalidDataException("An immutable object hash is zero.");
            string path = ObjectPath(hash);
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            if (File.Exists(path))
            {
                Require(BytesEqual(File.ReadAllBytes(path), bytes),
                    $"Hash collision at immutable object {hash}.");
                created = false;
                return hash;
            }

            string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 4096,
                       _objectBatchOpen ? FileOptions.None :
                           FileOptions.WriteThrough))
            {
                Inject(SigmaDurableFailurePoint.ObjectTempCreated, kind);
                stream.Write(bytes, 0, bytes.Length);
                Inject(SigmaDurableFailurePoint.ObjectWritten, kind);
                if (kind == SigmaDurableObjectKind.RootObject)
                    Inject(SigmaDurableFailurePoint.RootObjectWritten, kind);
                if (_objectBatchOpen)
                    stream.Flush();
                else
                    stream.Flush(true);
                Inject(SigmaDurableFailurePoint.ObjectFlushed, kind);
                if (kind == SigmaDurableObjectKind.RootObject)
                    Inject(SigmaDurableFailurePoint.RootObjectFlushed, kind);
            }
            if (File.Exists(path))
            {
                Require(BytesEqual(File.ReadAllBytes(path), bytes),
                    $"Hash collision at concurrently installed object {hash}.");
                File.Delete(temporary);
                created = false;
                return hash;
            }
            File.Move(temporary, path);
            if (_objectBatchOpen)
                _objectBatchDirty = true;
            if (!_objectBatchOpen)
                SyncDirectory(directory);
            Inject(SigmaDurableFailurePoint.ObjectInstalled, kind);
            if (kind == SigmaDurableObjectKind.PageMapNode)
                Inject(SigmaDurableFailurePoint.CowMapNodeInstalled, kind);
            else if (kind == SigmaDurableObjectKind.QuerySupportMapNode)
                Inject(SigmaDurableFailurePoint.SupportSummaryNodeInstalled, kind);
            created = true;
            return hash;
        }

        private void BeginObjectBatch()
        {
            if (_objectBatchOpen)
                throw new InvalidOperationException(
                    "A durable immutable-object batch is already open.");
            _objectBatchOpen = true;
            _objectBatchDirty = false;
        }

        private void CompleteObjectBatch()
        {
            if (!_objectBatchOpen)
                throw new InvalidOperationException(
                    "No durable immutable-object batch is open.");
            // This is the same durability shape as the proven SimpleScanner
            // writeback log: many immutable records are one storage batch and
            // receive one physical flush before the selector is published.
            // syncfs covers every object file and sharded-directory rename on
            // this filesystem; HEAD is still written, flushed and renamed last.
            if (_objectBatchDirty)
                SyncFileSystem(_objectDirectory);
            _objectBatchOpen = false;
            _objectBatchDirty = false;
        }

        private void AbortObjectBatch()
        {
            _objectBatchOpen = false;
            _objectBatchDirty = false;
        }

        internal bool TryGetPageRecord(SigmaCarrierPageCoordinate coordinate,
            out SigmaDurablePageRecord record)
        {
            lock (_gate)
            {
                record = null;
                if (_head == null)
                    return false;
                if (_headRecordIndexRootHash == _headHash)
                {
                    ++_headRecordIndexLookups;
                    if (!_headRecordIndex.TryGetValue(coordinate, out record))
                        return false;
                    Require(record.Coordinate.Equals(coordinate),
                        "Current-HEAD record-index coordinate mismatch.");
                    return true;
                }
                if (!SigmaSparseMerkleMap.TryGet(this,
                    _head.SparsePageMapRootHash,
                    SigmaSparseValueKind.PageRecord, coordinate,
                    out byte[] bytes))
                    return false;
                record = SigmaDurablePageRecord.Decode(bytes);
                Require(record.Coordinate.Equals(coordinate),
                    "Current page-map key/PageRecord coordinate mismatch.");
                return true;
            }
        }

        internal bool TryGetPageRecord(SigmaDurableRootLease root,
            SigmaCarrierPageCoordinate coordinate,
            out SigmaDurablePageRecord record)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            root.RequireOwner(this);
            lock (_gate)
            {
                record = null;
                if (_headRecordIndexRootHash == root.Hash)
                {
                    ++_headRecordIndexLookups;
                    if (!_headRecordIndex.TryGetValue(coordinate, out record))
                        return false;
                    Require(record.Coordinate.Equals(coordinate),
                        "Pinned current-HEAD record-index coordinate mismatch.");
                    return true;
                }
            }
            record = null;
            if (!SigmaSparseMerkleMap.TryGet(this,
                root.Root.SparsePageMapRootHash,
                SigmaSparseValueKind.PageRecord, coordinate,
                out byte[] bytes))
                return false;
            record = SigmaDurablePageRecord.Decode(bytes);
            Require(record.Coordinate.Equals(coordinate),
                "Pinned page-map key/PageRecord coordinate mismatch.");
            return true;
        }

        internal bool TryReadCanonicalPage(SigmaDurableRootLease root,
            SigmaDurablePageRecord record, out SigmaDecodedPage page)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (record == null) throw new ArgumentNullException(nameof(record));
            root.RequireOwner(this);
            page = null;
            if (!TryRead(record.PageBlobHash, out byte[] bytes))
                return false;
            page = record.DecodeVerifiedPageBytes(bytes);
            return true;
        }

        internal IReadOnlyList<SigmaDurablePageRecord> EnumeratePageRecords(
            SigmaDurableRootLease root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            root.RequireOwner(this);
            lock (_gate)
            {
                if (_inventoryRecords != null &&
                    root.Hash == _inventoryRootHash)
                    return _inventoryRecords;
            }
            var records = new List<SigmaDurablePageRecord>();
            SigmaSparseMerkleMap.Visit(this,
                root.Root.SparsePageMapRootHash,
                SigmaSparseValueKind.PageRecord, (coordinate, bytes) =>
                {
                    SigmaDurablePageRecord record =
                        SigmaDurablePageRecord.Decode(bytes);
                    Require(record.Coordinate.Equals(coordinate),
                        "Page-map key/PageRecord coordinate mismatch.");
                    records.Add(record);
                });
            records.Sort((left, right) =>
                left.Coordinate.CompareTo(right.Coordinate));
            return records;
        }

        internal uint ComputeLogicalExtent(SigmaDurableRootLease root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            root.RequireOwner(this);
            lock (_gate)
            {
                if (_inventoryRecords != null &&
                    root.Hash == _inventoryRootHash)
                {
                    Require(_inventoryLogicalExtentValid,
                        "The N5 native logical stream contains an invalid page " +
                        "coordinate.");
                    return _inventoryLogicalExtent;
                }
            }
            uint extent = 0u;
            foreach (SigmaDurablePageRecord record in
                EnumeratePageRecords(root))
            {
                Require(record.Coordinate.Y == 0L &&
                    record.Coordinate.X >= 0L &&
                    (ulong)record.Coordinate.X <= uint.MaxValue /
                        (uint)SigmaCarrier.SamplesPerPage,
                    "The N5 native logical stream contains an invalid page " +
                    "coordinate.");
                Require(TryRead(record.PageBlobHash, out byte[] pageBytes),
                    "A durable PageRecord references a missing PageBlob.");
                SigmaEncodedPageHeader header =
                    record.ValidateVerifiedPageObjectBytes(pageBytes);
                ulong candidate = (ulong)record.Coordinate.X *
                    (uint)SigmaCarrier.SamplesPerPage +
                    header.ActiveSampleCount;
                Require(candidate <= uint.MaxValue,
                    "The native logical extent exceeds its exact uint ABI.");
                extent = Math.Max(extent, (uint)candidate);
            }
            return extent;
        }

        internal bool IsSupportSummaryVerified(
            SigmaDurableRootLease root, SigmaDurablePageRecord record)
            => TryReadQuerySupportSummary(root, record, out _);

        internal bool TryReadQuerySupportSummary(
            SigmaDurableRootLease root, SigmaDurablePageRecord record,
            out SigmaQuerySupportSummary summary)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (record == null) throw new ArgumentNullException(nameof(record));
            root.RequireOwner(this);
            SigmaQuerySupportReceipt receipt = record.QuerySupport;
            if ((receipt.Flags & SigmaQuerySupportFlags.Verified) == 0 ||
                receipt.Generation != record.PageGeneration ||
                receipt.SummaryHash.IsZero)
            {
                summary = default;
                return false;
            }
            try
            {
                if (!TryRead(receipt.SummaryHash, out byte[] bytes))
                {
                    summary = default;
                    return false;
                }
                summary = SigmaQuerySupportSummary.Decode(bytes);
                summary.Validate(record);
                return true;
            }
            catch (InvalidDataException)
            {
                summary = default;
                return false;
            }
            catch (IOException)
            {
                summary = default;
                return false;
            }
        }

        internal bool TryReadUnresolvedFrontier(
            SigmaDurableRootLease root, out byte[] frontier)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            root.RequireOwner(this);
            SigmaDurableHash hash = root.Root.UnresolvedFrontierRootHash;
            if (hash.IsZero)
            {
                frontier = null;
                return false;
            }
            Require(TryRead(hash, out byte[] bytes),
                "Missing durable unresolved-frontier object.");
            frontier = DecodeManifest(bytes,
                SigmaDurableObjectKind.UnresolvedFrontier);
            return true;
        }

        internal int PinnedRootCount
        {
            get
            {
                lock (_gate)
                {
                    int total = 0;
                    foreach (int count in _rootPins.Values)
                        total = checked(total + count);
                    return total;
                }
            }
        }

        internal void ReleaseRoot(SigmaDurableHash hash)
        {
            lock (_gate)
            {
                if (!_rootPins.TryGetValue(hash, out int count) || count <= 0)
                    throw new InvalidOperationException(
                        "Durable root lease accounting underflow.");
                if (count == 1) _rootPins.Remove(hash);
                else _rootPins[hash] = count - 1;
            }
        }

        private void LoadHead()
        {
            if (!File.Exists(_headPath)) return;
            SigmaDurableHash hash = SigmaDurableHead.Decode(
                File.ReadAllBytes(_headPath));
            Require(TryRead(hash, out byte[] rootBytes),
                "HEAD references a missing RootObject.");
            SigmaDurableRootObject root = SigmaDurableRootObject.Decode(rootBytes);
            ValidateReachable(root, out SigmaDurablePageRecord[] records,
                out uint logicalExtent, out bool logicalExtentValid,
                out long pageBytes);
            _headHash = hash;
            _head = root;
            SetHeadInventory(hash, records, logicalExtent,
                logicalExtentValid, pageBytes);
            RebuildQuerySupportIndex(records);
        }

        private void RebuildQuerySupportIndex(
            IReadOnlyList<SigmaDurablePageRecord> records)
        {
            var pages = new List<(SigmaDurablePageRecord Record,
                SigmaQuerySupportSummary? Summary)>(records.Count);
            // Startup validates immutable PageRecords/PageBlobs first. Optional
            // support summaries are then rebuilt into one disposable index;
            // broken metadata stays an unbounded fail-closed candidate.
            using SigmaDurableRootLease root = PinHead();
            for (int index = 0; index < records.Count; ++index)
            {
                SigmaDurablePageRecord record = records[index];
                SigmaQuerySupportSummary? summary =
                    TryReadQuerySupportSummary(root, record,
                        out SigmaQuerySupportSummary decoded)
                        ? decoded : null;
                pages.Add((record, summary));
            }
            _querySupportIndex.Rebuild(pages);
        }

        private void ValidateReachable(SigmaDurableRootObject root,
            out SigmaDurablePageRecord[] pageRecords,
            out uint logicalExtent, out bool logicalExtentValid,
            out long pageBytes)
        {
            var records = new List<SigmaDurablePageRecord>();
            uint extent = 0u;
            bool extentValid = true;
            long totalPageBytes = 0L;
            SigmaSparseMerkleMap.Visit(this, root.SparsePageMapRootHash,
                SigmaSparseValueKind.PageRecord, (coordinate, bytes) =>
                {
                    SigmaDurablePageRecord record =
                        SigmaDurablePageRecord.Decode(bytes);
                    Require(record.Coordinate.Equals(coordinate),
                        "Page-map leaf key/PageRecord coordinate mismatch.");
                    Require(TryRead(record.PageBlobHash, out byte[] pageBytes),
                        "PageRecord references a missing PageBlob.");
                    SigmaEncodedPageHeader header =
                        record.ValidateVerifiedPageObjectBytes(pageBytes);
                    totalPageBytes = checked(totalPageBytes + pageBytes.LongLength);
                    records.Add(record);
                    bool coordinateValid = record.Coordinate.Y == 0L &&
                        record.Coordinate.X >= 0L &&
                        (ulong)record.Coordinate.X <= uint.MaxValue /
                            (uint)SigmaCarrier.SamplesPerPage;
                    if (!coordinateValid)
                    {
                        extentValid = false;
                    }
                    else if (extentValid)
                    {
                        ulong candidate = (ulong)record.Coordinate.X *
                            (uint)SigmaCarrier.SamplesPerPage +
                            header.ActiveSampleCount;
                        if (candidate > uint.MaxValue)
                            extentValid = false;
                        else
                            extent = Math.Max(extent, (uint)candidate);
                    }
                });
            SigmaSparseMerkleMap.Visit(this, root.QuerySupportRootHash,
                SigmaSparseValueKind.QuerySupport, (coordinate, bytes) =>
                {
                    SigmaQuerySupportReceipt receipt =
                        SigmaQuerySupportReceipt.Decode(bytes);
                    if ((receipt.Flags & SigmaQuerySupportFlags.Verified) != 0 &&
                        !receipt.SummaryHash.IsZero)
                    {
                        // The receipt itself is canonical and reachable.  A
                        // missing/corrupt optional acceleration summary is a
                        // conservative MAY-CONTRIBUTE condition, never a page
                        // omission or a broken durable root.
                        try { TryRead(receipt.SummaryHash, out _); }
                        catch (InvalidDataException) { }
                        catch (IOException) { }
                    }
                });
            ValidateManifest(root.CertificateManifestRootHash,
                SigmaDurableObjectKind.CertificateManifest);
            ValidateManifest(root.UnresolvedFrontierRootHash,
                SigmaDurableObjectKind.UnresolvedFrontier);
            records.Sort((left, right) =>
                left.Coordinate.CompareTo(right.Coordinate));
            pageRecords = records.ToArray();
            logicalExtent = extent;
            logicalExtentValid = extentValid;
            pageBytes = totalPageBytes;
        }

        private void SetHeadInventory(SigmaDurableHash hash,
            SigmaDurablePageRecord[] records, uint logicalExtent,
            bool logicalExtentValid, long pageBytes)
        {
            _inventoryRootHash = hash;
            _inventoryRecords = records ??
                throw new ArgumentNullException(nameof(records));
            _inventoryLogicalExtent = logicalExtent;
            _inventoryLogicalExtentValid = logicalExtentValid;
            _inventoryPageBytes = pageBytes;
            _headRecordIndex.Clear();
            for (int index = 0; index < records.Length; ++index)
            {
                SigmaDurablePageRecord record = records[index];
                Require(!_headRecordIndex.ContainsKey(record.Coordinate),
                    "Verified HEAD inventory contains a duplicate page key.");
                _headRecordIndex.Add(record.Coordinate, record);
            }
            _headRecordIndexRootHash = hash;
        }

        private void AdvanceHeadRecordIndex(SigmaDurableHash priorHash,
            SigmaDurableHash nextHash,
            IReadOnlyList<(SigmaDurablePageRecord Record,
                SigmaQuerySupportSummary? Summary)> updates)
        {
            // This dictionary is only a disposable lookup accelerator for the
            // fully verified selected HEAD.  If its root binding is ever lost,
            // retain correctness by invalidating it; pinned roots continue to
            // use the immutable Merkle map.  A normal startup/empty selection
            // always establishes the exact prior binding.
            if (_headRecordIndexRootHash != priorHash)
            {
                _headRecordIndex.Clear();
                _headRecordIndexRootHash = SigmaDurableHash.Zero;
                return;
            }
            for (int index = 0; index < updates.Count; ++index)
            {
                SigmaDurablePageRecord record = updates[index].Record;
                _headRecordIndex[record.Coordinate] = record;
            }
            _headRecordIndexRootHash = nextHash;
        }

        private void InvalidateHeadInventory()
        {
            _inventoryRootHash = SigmaDurableHash.Zero;
            _inventoryRecords = null;
            _inventoryLogicalExtent = 0u;
            _inventoryLogicalExtentValid = false;
            _inventoryPageBytes = 0L;
        }

        private SigmaDurableHash PutManifest(SigmaDurableObjectKind kind,
            byte[] payload, ref int immutableObjects, ref long bytesWritten)
        {
            byte[] bytes = EncodeManifest(kind, payload);
            SigmaDurableHash hash = Put(bytes, kind, out bool created);
            if (created)
            {
                ++immutableObjects;
                bytesWritten += bytes.Length;
            }
            return hash;
        }

        private void ValidateManifest(SigmaDurableHash hash,
            SigmaDurableObjectKind kind)
        {
            if (hash.IsZero) return;
            Require(TryRead(hash, out byte[] bytes),
                $"Missing durable {kind} object.");
            DecodeManifest(bytes, kind);
        }

        private static byte[] EncodeManifest(SigmaDurableObjectKind kind,
            byte[] payload)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(ManifestMagic); writer.Write(SigmaDurableSchema.Version);
            writer.Write((byte)kind); writer.Write((byte)0u);
            writer.Write((ushort)0u); writer.Write((uint)payload.Length);
            writer.Write(payload); writer.Flush();
            return stream.ToArray();
        }

        private static byte[] DecodeManifest(byte[] bytes,
            SigmaDurableObjectKind expected)
        {
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == ManifestMagic &&
                reader.ReadUInt32() == SigmaDurableSchema.Version &&
                reader.ReadByte() == (byte)expected && reader.ReadByte() == 0u &&
                reader.ReadUInt16() == 0u, "Invalid durable manifest header.");
            uint length = reader.ReadUInt32();
            Require(length <= int.MaxValue, "Durable manifest is too large.");
            byte[] payload = reader.ReadBytes((int)length);
            Require(payload.Length == (int)length && stream.Position == stream.Length,
                "Truncated durable manifest.");
            return payload;
        }

        private void PublishHead(SigmaDurableHash rootObjectHash)
        {
            byte[] bytes = SigmaDurableHead.Encode(rootObjectHash);
            string temporary = _headPath + ".tmp." + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            Inject(SigmaDurableFailurePoint.BeforeHeadSwap,
                SigmaDurableObjectKind.RootObject);
            AtomicReplace(temporary, _headPath);
            Inject(SigmaDurableFailurePoint.AfterHeadSwap,
                SigmaDurableObjectKind.RootObject);
            SyncDirectory(_rootDirectory);
            Inject(SigmaDurableFailurePoint.HeadDirectoryFlushed,
                SigmaDurableObjectKind.RootObject);
        }

        private string ObjectPath(SigmaDurableHash hash)
        {
            string text = hash.ToString();
            return Path.Combine(_objectDirectory, text.Substring(0, 2),
                text + ".s5o");
        }

        private void Inject(SigmaDurableFailurePoint point,
            SigmaDurableObjectKind kind) => _failure?.Invoke(point, kind);

        private static void ValidateCanonicalPageStage(SigmaDecodedPage expected,
            byte[] bytes)
        {
            SigmaDecodedPage decoded = SigmaCarrierCodec.DecodePage(bytes);
            Require(decoded.Coordinate.Equals(expected.Coordinate) &&
                decoded.Generation == expected.Generation &&
                decoded.GaugeGeneration == expected.GaugeGeneration &&
                decoded.CertificateGeneration == expected.CertificateGeneration &&
                decoded.Revision == expected.Revision,
                "Staged page metadata does not match its pinned GPU revision.");
            Require(BytesEqual(bytes, SigmaCarrierCodec.EncodePage(decoded)),
                "Staged page bytes are not canonical EncodePage output.");
            Require(PagesEqual(expected, decoded),
                "Staged page decoded representation differs from the pinned page.");
        }

        private static void ValidateCanonicalPageStageHeader(
            SigmaEncodedPageHeader expected, byte[] bytes)
        {
            SigmaEncodedPageHeader header = SigmaCarrierCodec.ReadPageHeader(bytes);
            Require(header.Coordinate.Equals(expected.Coordinate) &&
                header.Generation == expected.Generation &&
                header.GaugeGeneration == expected.GaugeGeneration &&
                header.CertificateGeneration ==
                    expected.CertificateGeneration &&
                header.Revision == expected.Revision &&
                header.CertificateOffset == expected.CertificateOffset &&
                header.CertificateCount == expected.CertificateCount &&
                header.RepresentationFlags == expected.RepresentationFlags &&
                header.ActiveSampleCount == expected.ActiveSampleCount &&
                header.RepresentationCount == expected.RepresentationCount,
                "Direct-codec staged page metadata changed before commit.");
        }

        private static void ValidateSupportStage(SigmaDurablePageUpdate update)
        {
            SigmaQuerySupportReceipt receipt = update.QuerySupport;
            if (update.QuerySupportSummaryBytes == null)
            {
                Require((receipt.Flags & SigmaQuerySupportFlags.Verified) == 0 ||
                    receipt.SummaryHash.IsZero,
                    "A verified summary hash requires its complete summary bytes.");
                return;
            }
            Require(!receipt.SummaryHash.IsZero &&
                SigmaDurableHash.Compute(update.QuerySupportSummaryBytes) ==
                    receipt.SummaryHash,
                "Query-support summary bytes do not match their receipt.");
            SigmaQuerySupportSummary summary = SigmaQuerySupportSummary.Decode(
                update.QuerySupportSummaryBytes);
            summary.Validate(update.Header, receipt);
        }

        private static bool PagesEqual(SigmaDecodedPage left,
            SigmaDecodedPage right)
        {
            if (!left.Coordinate.Equals(right.Coordinate) ||
                left.Generation != right.Generation ||
                left.Revision != right.Revision ||
                left.CertificateOffset != right.CertificateOffset ||
                left.CertificateCount != right.CertificateCount ||
                left.GaugeGeneration != right.GaugeGeneration ||
                left.CertificateGeneration != right.CertificateGeneration ||
                left.RepresentationFlags != right.RepresentationFlags ||
                left.ActiveSampleCount != right.ActiveSampleCount)
                return false;
            for (int index = 0; index < SigmaCarrier.SamplesPerPage; ++index)
                if (left.SampleAt(index) != right.SampleAt(index)) return false;
            if (left.RepresentationCount != right.RepresentationCount)
                return false;
            for (int record = 0; record < left.RepresentationCount; ++record)
            {
                SigmaCarrierRepresentationRecord leftRecord =
                    left.RepresentationAt(record);
                SigmaCarrierRepresentationRecord rightRecord =
                    right.RepresentationAt(record);
                if (leftRecord.SampleIndex != rightRecord.SampleIndex ||
                    !WordsEqual(leftRecord.Words, rightRecord.Words))
                    return false;
            }
            return true;
        }

        private static bool WordsEqual(uint[] left, uint[] right)
        {
            if (left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; ++index)
                if (left[index] != right[index]) return false;
            return true;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int index = 0; index < left.Length; ++index)
                if (left[index] != right[index]) return false;
            return true;
        }

        private static double CommitMilliseconds(long begin, long end)
        {
            if (begin == 0L || end < begin)
                return 0.0;
            return (end - begin) * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency;
        }

        private static void AtomicReplace(string temporary, string destination)
        {
#if UNITY_ANDROID || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            if (NativeRename(temporary, destination) != 0)
                throw new IOException("Atomic rename failed with errno " +
                    Marshal.GetLastWin32Error() + ".");
#else
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
#endif
        }

        private static void SyncDirectory(string directory)
        {
#if UNITY_ANDROID || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            const int openReadOnly = 0;
            int descriptor = NativeOpen(directory,
                openReadOnly | CurrentDirectoryOpenFlag);
            if (descriptor < 0)
                throw new IOException("Directory open failed with errno " +
                    Marshal.GetLastWin32Error() + ".");
            try
            {
                if (NativeFsync(descriptor) != 0)
                    throw new IOException("Directory fsync failed with errno " +
                        Marshal.GetLastWin32Error() + ".");
            }
            finally
            {
                NativeClose(descriptor);
            }
#endif
        }

        private static void SyncFileSystem(string path)
        {
#if UNITY_ANDROID || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            const int openReadOnly = 0;
            int descriptor = NativeOpen(path,
                openReadOnly | CurrentDirectoryOpenFlag);
            if (descriptor < 0)
                throw new IOException("Filesystem sync open failed with errno " +
                    Marshal.GetLastWin32Error() + ".");
            try
            {
                if (NativeSyncFileSystem(descriptor) != 0)
                    throw new IOException("Filesystem sync failed with errno " +
                        Marshal.GetLastWin32Error() + ".");
            }
            finally
            {
                NativeClose(descriptor);
            }
#endif
        }

#if UNITY_ANDROID || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
        private static extern int NativeRename(string oldPath, string newPath);
        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int NativeOpen(string path, int flags);
        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        private static extern int NativeFsync(int descriptor);
        [DllImport("libc", EntryPoint = "syncfs", SetLastError = true)]
        private static extern int NativeSyncFileSystem(int descriptor);
        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int NativeClose(int descriptor);
#endif

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
