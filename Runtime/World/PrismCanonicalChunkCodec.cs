using System;
using System.IO;
using System.Text;
using Genesis.RoomScan.Prism;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Detached, immutable canonical PRISM payload safe for worker-thread encoding.
    /// Geometry posterior and observation state are resumable; meshlets are an optional
    /// acceleration cache and never replace the canonical sections.
    /// </summary>
    public sealed class PrismCanonicalChunkSnapshot
    {
        public int FilmCount;
        public int BoundaryCount;
        public int DisplacementBasePageCount;
        public int DisplacementMicroPageCount;
        public int MeshletVertexCount;
        public int MeshletIndexCount;
        public int MeshletDescriptorCount;
        public uint FilmGeneration;
        public uint BoundaryGeneration;
        public uint DisplacementGeneration;
        public uint MeshletGeneration;
        public ulong CalibrationEpoch;
        public byte[] FilmHeaders = Array.Empty<byte>();
        public byte[] FilmInformation = Array.Empty<byte>();
        public byte[] BoundaryHeaders = Array.Empty<byte>();
        public byte[] BoundaryInformation = Array.Empty<byte>();
        /// <summary>Base headers followed by packed micro headers.</summary>
        public byte[] DisplacementPageHeaders = Array.Empty<byte>();
        public byte[] DisplacementBaseCells = Array.Empty<byte>();
        public byte[] DisplacementMicroCells = Array.Empty<byte>();
        public byte[] DisplacementBaseChildren = Array.Empty<byte>();
        public byte[] DisplacementMicroChildren = Array.Empty<byte>();
        public byte[] TopologyEvidence = Array.Empty<byte>();
        public byte[] DisplacementAllocator = Array.Empty<byte>();
        public byte[] MeshletVertices = Array.Empty<byte>();
        public byte[] MeshletIndices = Array.Empty<byte>();
        public byte[] MeshletDescriptors = Array.Empty<byte>();
        /// <summary>Versioned page payload owned by Q3-17/Q3-19.</summary>
        public byte[] AppearanceState = Array.Empty<byte>();
        /// <summary>Resumable deposited-observation sufficient statistics.</summary>
        public byte[] ObservationState = Array.Empty<byte>();
        public string[] KeyframeReferences = Array.Empty<string>();
    }

    /// <summary>
    /// Strict versioned binary codec for resumable ContactFilms. Version 3 adds the
    /// complete hierarchical geometry posterior and derived render cache. Version 2 is
    /// still readable and upgrades missing sections to explicit empty state.
    /// </summary>
    public static class PrismCanonicalChunkCodec
    {
        public const int FormatVersion = 3;
        private const int LegacyFormatVersion = 2;
        private const uint Magic = 0x33515043; // "CPQ3"
        private const uint EndianMarker = 0x01020304;
        private const int MaximumFilms = 1_000_000;
        private const int MaximumBoundaries = 2_000_000;
        private const int MaximumDisplacementPages = 4_000_000;
        private const int MaximumMeshletVertices = 16_000_000;
        private const int MaximumMeshletIndices = 64_000_000;
        private const int MaximumMeshletDescriptors = 4_000_000;
        private const int MaximumOpaqueSectionBytes = 1024 * 1024 * 1024;
        private const int MaximumKeyframeReferences = 65_536;
        private const int MaximumReferenceUtf8Bytes = 4096;
        private const int FilmInformationStride = 9 * sizeof(float) * 4;
        private const int BoundaryInformationStride =
            ContactBoundaryPool.InformationRecordsPerBoundary * sizeof(float) * 4;
        private const int DisplacementAllocatorBytes = sizeof(uint) * 8;

        public static bool TryValidate(PrismCanonicalChunkSnapshot snapshot,
            out string error)
        {
            error = Validate(snapshot);
            return error == null;
        }

        public static bool TryWrite(Stream stream, PrismCanonicalChunkSnapshot snapshot,
            out string error)
        {
            error = Validate(snapshot);
            if (error != null) return false;
            if (stream == null || !stream.CanWrite)
            {
                error = "PRISM destination is not writable.";
                return false;
            }
            try
            {
                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(EndianMarker);
                writer.Write(ContactFilmHeaderGpu.Stride);
                writer.Write(ContactBoundaryHeaderGpu.Stride);
                writer.Write(DisplacementPageHeaderGpu.Stride);
                writer.Write(DisplacementCellGpu.Stride);
                writer.Write(ContactTopologyEvidenceGpu.Stride);
                writer.Write(ContactMeshletVertexGpu.Stride);
                writer.Write(ContactMeshletDescriptorGpu.Stride);
                writer.Write(snapshot.FilmCount);
                writer.Write(snapshot.BoundaryCount);
                writer.Write(snapshot.DisplacementBasePageCount);
                writer.Write(snapshot.DisplacementMicroPageCount);
                writer.Write(snapshot.MeshletVertexCount);
                writer.Write(snapshot.MeshletIndexCount);
                writer.Write(snapshot.MeshletDescriptorCount);
                writer.Write(snapshot.FilmGeneration);
                writer.Write(snapshot.BoundaryGeneration);
                writer.Write(snapshot.DisplacementGeneration);
                writer.Write(snapshot.MeshletGeneration);
                writer.Write(snapshot.CalibrationEpoch);
                WriteBytes(writer, snapshot.FilmHeaders);
                WriteBytes(writer, snapshot.FilmInformation);
                WriteBytes(writer, snapshot.BoundaryHeaders);
                WriteBytes(writer, snapshot.BoundaryInformation);
                WriteBytes(writer, snapshot.DisplacementPageHeaders);
                WriteBytes(writer, snapshot.DisplacementBaseCells);
                WriteBytes(writer, snapshot.DisplacementMicroCells);
                WriteBytes(writer, snapshot.DisplacementBaseChildren);
                WriteBytes(writer, snapshot.DisplacementMicroChildren);
                WriteBytes(writer, snapshot.TopologyEvidence);
                WriteBytes(writer, snapshot.DisplacementAllocator);
                WriteBytes(writer, snapshot.MeshletVertices);
                WriteBytes(writer, snapshot.MeshletIndices);
                WriteBytes(writer, snapshot.MeshletDescriptors);
                WriteBytes(writer, snapshot.AppearanceState);
                WriteBytes(writer, snapshot.ObservationState);
                WriteReferences(writer, snapshot.KeyframeReferences);
                writer.Flush();
                return true;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is ArgumentException ||
                                              exception is OverflowException)
            {
                error = "PRISM serialization failed: " + exception.Message;
                return false;
            }
        }

        public static bool TryRead(Stream stream, out PrismCanonicalChunkSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = null;
            if (stream == null || !stream.CanRead || !stream.CanSeek)
            {
                error = "PRISM source must be readable and bounded.";
                return false;
            }
            try
            {
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                if (reader.ReadUInt32() != Magic)
                    throw new InvalidDataException("PRISM magic is invalid.");
                int version = reader.ReadInt32();
                if (reader.ReadUInt32() != EndianMarker)
                    throw new InvalidDataException("PRISM endian marker is invalid.");
                PrismCanonicalChunkSnapshot candidate = version switch
                {
                    LegacyFormatVersion => ReadVersion2(reader),
                    FormatVersion => ReadVersion3(reader),
                    _ => throw new InvalidDataException(
                        $"Unsupported PRISM format version {version}.")
                };
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("PRISM payload has trailing bytes.");
                error = Validate(candidate, version == LegacyFormatVersion);
                if (error != null) return false;
                snapshot = candidate;
                return true;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is InvalidDataException ||
                                              exception is ArgumentException ||
                                              exception is OverflowException ||
                                              exception is EndOfStreamException)
            {
                error = "PRISM artifact rejected: " + exception.Message;
                return false;
            }
        }

        private static PrismCanonicalChunkSnapshot ReadVersion3(BinaryReader reader)
        {
            RequireStride(reader, ContactFilmHeaderGpu.Stride, "film header");
            RequireStride(reader, ContactBoundaryHeaderGpu.Stride, "boundary header");
            RequireStride(reader, DisplacementPageHeaderGpu.Stride,
                "displacement page");
            RequireStride(reader, DisplacementCellGpu.Stride, "displacement cell");
            RequireStride(reader, ContactTopologyEvidenceGpu.Stride,
                "topology evidence");
            RequireStride(reader, ContactMeshletVertexGpu.Stride, "meshlet vertex");
            RequireStride(reader, ContactMeshletDescriptorGpu.Stride,
                "meshlet descriptor");
            var candidate = new PrismCanonicalChunkSnapshot
            {
                FilmCount = reader.ReadInt32(),
                BoundaryCount = reader.ReadInt32(),
                DisplacementBasePageCount = reader.ReadInt32(),
                DisplacementMicroPageCount = reader.ReadInt32(),
                MeshletVertexCount = reader.ReadInt32(),
                MeshletIndexCount = reader.ReadInt32(),
                MeshletDescriptorCount = reader.ReadInt32(),
                FilmGeneration = reader.ReadUInt32(),
                BoundaryGeneration = reader.ReadUInt32(),
                DisplacementGeneration = reader.ReadUInt32(),
                MeshletGeneration = reader.ReadUInt32(),
                CalibrationEpoch = reader.ReadUInt64()
            };
            ValidateCounts(candidate);
            candidate.FilmHeaders = ReadBytes(reader,
                Bytes(candidate.FilmCount, ContactFilmHeaderGpu.Stride));
            candidate.FilmInformation = ReadBytes(reader,
                Bytes(candidate.FilmCount, FilmInformationStride));
            candidate.BoundaryHeaders = ReadBytes(reader,
                Bytes(candidate.BoundaryCount, ContactBoundaryHeaderGpu.Stride));
            candidate.BoundaryInformation = ReadBytes(reader,
                Bytes(candidate.BoundaryCount, BoundaryInformationStride));
            candidate.DisplacementPageHeaders = ReadBytes(reader,
                Bytes(checked(candidate.DisplacementBasePageCount +
                    candidate.DisplacementMicroPageCount),
                    DisplacementPageHeaderGpu.Stride));
            candidate.DisplacementBaseCells = ReadBytes(reader,
                Bytes(checked(candidate.DisplacementBasePageCount *
                    ContactDisplacementPool.BaseCellsPerPage),
                    DisplacementCellGpu.Stride));
            candidate.DisplacementMicroCells = ReadBytes(reader,
                Bytes(checked(candidate.DisplacementMicroPageCount *
                    ContactDisplacementPool.MicroCellsPerPage),
                    DisplacementCellGpu.Stride));
            candidate.DisplacementBaseChildren = ReadBytes(reader,
                Bytes(checked(candidate.DisplacementBasePageCount *
                    ContactDisplacementPool.BaseCellsPerPage), sizeof(uint)));
            candidate.DisplacementMicroChildren = ReadBytes(reader,
                Bytes(checked(candidate.DisplacementMicroPageCount *
                    ContactDisplacementPool.MicroCellsPerPage), sizeof(uint)));
            candidate.TopologyEvidence = ReadBytes(reader,
                Bytes(candidate.FilmCount, ContactTopologyEvidenceGpu.Stride));
            candidate.DisplacementAllocator = ReadBytes(reader,
                DisplacementAllocatorBytes);
            candidate.MeshletVertices = ReadBytes(reader,
                Bytes(candidate.MeshletVertexCount, ContactMeshletVertexGpu.Stride));
            candidate.MeshletIndices = ReadBytes(reader,
                Bytes(candidate.MeshletIndexCount, sizeof(uint)));
            candidate.MeshletDescriptors = ReadBytes(reader,
                Bytes(candidate.MeshletDescriptorCount,
                    ContactMeshletDescriptorGpu.Stride));
            candidate.AppearanceState = ReadBoundedBytes(reader,
                MaximumOpaqueSectionBytes, "appearance");
            candidate.ObservationState = ReadBoundedBytes(reader,
                MaximumOpaqueSectionBytes, "observation");
            candidate.KeyframeReferences = ReadReferences(reader);
            return candidate;
        }

        private static PrismCanonicalChunkSnapshot ReadVersion2(BinaryReader reader)
        {
            RequireStride(reader, ContactFilmHeaderGpu.Stride, "film header");
            RequireStride(reader, ContactBoundaryHeaderGpu.Stride, "boundary header");
            var candidate = new PrismCanonicalChunkSnapshot
            {
                FilmCount = reader.ReadInt32(),
                BoundaryCount = reader.ReadInt32(),
                FilmGeneration = reader.ReadUInt32(),
                BoundaryGeneration = reader.ReadUInt32(),
                CalibrationEpoch = reader.ReadUInt64()
            };
            ValidateCounts(candidate);
            candidate.FilmHeaders = ReadBytes(reader,
                Bytes(candidate.FilmCount, ContactFilmHeaderGpu.Stride));
            candidate.FilmInformation = ReadBytes(reader,
                Bytes(candidate.FilmCount, FilmInformationStride));
            candidate.BoundaryHeaders = ReadBytes(reader,
                Bytes(candidate.BoundaryCount, ContactBoundaryHeaderGpu.Stride));
            candidate.BoundaryInformation = ReadBytes(reader,
                Bytes(candidate.BoundaryCount, BoundaryInformationStride));
            return candidate;
        }

        private static string Validate(PrismCanonicalChunkSnapshot snapshot,
            bool allowLegacyMissingSections = false)
        {
            if (snapshot == null) return "PRISM snapshot is null.";
            try { ValidateCounts(snapshot); }
            catch (Exception exception) { return exception.Message; }
            if (!LengthIs(snapshot.FilmHeaders,
                    (long)snapshot.FilmCount * ContactFilmHeaderGpu.Stride) ||
                !LengthIs(snapshot.FilmInformation,
                    (long)snapshot.FilmCount * FilmInformationStride) ||
                !LengthIs(snapshot.BoundaryHeaders,
                    (long)snapshot.BoundaryCount * ContactBoundaryHeaderGpu.Stride) ||
                !LengthIs(snapshot.BoundaryInformation,
                    (long)snapshot.BoundaryCount * BoundaryInformationStride))
                return "PRISM film/boundary lengths do not match canonical counts.";
            if (!allowLegacyMissingSections)
            {
                long pageCount = (long)snapshot.DisplacementBasePageCount +
                    snapshot.DisplacementMicroPageCount;
                if (!LengthIs(snapshot.DisplacementPageHeaders,
                        pageCount * DisplacementPageHeaderGpu.Stride) ||
                    !LengthIs(snapshot.DisplacementBaseCells,
                        (long)snapshot.DisplacementBasePageCount *
                        ContactDisplacementPool.BaseCellsPerPage *
                        DisplacementCellGpu.Stride) ||
                    !LengthIs(snapshot.DisplacementMicroCells,
                        (long)snapshot.DisplacementMicroPageCount *
                        ContactDisplacementPool.MicroCellsPerPage *
                        DisplacementCellGpu.Stride) ||
                    !LengthIs(snapshot.DisplacementBaseChildren,
                        (long)snapshot.DisplacementBasePageCount *
                        ContactDisplacementPool.BaseCellsPerPage * sizeof(uint)) ||
                    !LengthIs(snapshot.DisplacementMicroChildren,
                        (long)snapshot.DisplacementMicroPageCount *
                        ContactDisplacementPool.MicroCellsPerPage * sizeof(uint)) ||
                    !LengthIs(snapshot.TopologyEvidence,
                        (long)snapshot.FilmCount * ContactTopologyEvidenceGpu.Stride) ||
                    !LengthIs(snapshot.DisplacementAllocator,
                        DisplacementAllocatorBytes) ||
                    !LengthIs(snapshot.MeshletVertices,
                        (long)snapshot.MeshletVertexCount *
                        ContactMeshletVertexGpu.Stride) ||
                    !LengthIs(snapshot.MeshletIndices,
                        (long)snapshot.MeshletIndexCount * sizeof(uint)) ||
                    !LengthIs(snapshot.MeshletDescriptors,
                        (long)snapshot.MeshletDescriptorCount *
                        ContactMeshletDescriptorGpu.Stride))
                    return "PRISM hierarchy/meshlet lengths do not match counts.";
                if (!OpaqueLengthValid(snapshot.AppearanceState) ||
                    !OpaqueLengthValid(snapshot.ObservationState))
                    return "PRISM opaque state section exceeds its bound.";
                string referenceError = ValidateReferences(snapshot.KeyframeReferences);
                if (referenceError != null) return referenceError;
            }
            if ((snapshot.FilmCount > 0 && snapshot.FilmGeneration == 0) ||
                (snapshot.BoundaryCount > 0 && snapshot.BoundaryGeneration == 0) ||
                ((snapshot.DisplacementBasePageCount > 0 ||
                  snapshot.DisplacementMicroPageCount > 0) &&
                 snapshot.DisplacementGeneration == 0) ||
                ((snapshot.MeshletVertexCount > 0 || snapshot.MeshletIndexCount > 0 ||
                  snapshot.MeshletDescriptorCount > 0) &&
                 snapshot.MeshletGeneration == 0))
                return "PRISM live generations must be non-zero.";
            return null;
        }

        private static void ValidateCounts(PrismCanonicalChunkSnapshot snapshot)
        {
            if (snapshot.FilmCount < 0 || snapshot.FilmCount > MaximumFilms ||
                snapshot.BoundaryCount < 0 ||
                snapshot.BoundaryCount > MaximumBoundaries ||
                snapshot.DisplacementBasePageCount < 0 ||
                snapshot.DisplacementMicroPageCount < 0 ||
                (long)snapshot.DisplacementBasePageCount +
                    snapshot.DisplacementMicroPageCount > MaximumDisplacementPages ||
                snapshot.MeshletVertexCount < 0 ||
                snapshot.MeshletVertexCount > MaximumMeshletVertices ||
                snapshot.MeshletIndexCount < 0 ||
                snapshot.MeshletIndexCount > MaximumMeshletIndices ||
                snapshot.MeshletDescriptorCount < 0 ||
                snapshot.MeshletDescriptorCount > MaximumMeshletDescriptors)
                throw new InvalidDataException("PRISM canonical counts exceed limits.");
        }

        private static int Bytes(int count, int stride) => checked(count * stride);

        private static bool LengthIs(byte[] bytes, long expected) =>
            bytes != null && bytes.LongLength == expected;

        private static bool OpaqueLengthValid(byte[] bytes) =>
            bytes != null && bytes.LongLength <= MaximumOpaqueSectionBytes;

        private static void RequireStride(BinaryReader reader, int expected,
            string section)
        {
            if (reader.ReadInt32() != expected)
                throw new InvalidDataException($"PRISM {section} stride changed.");
        }

        private static void WriteBytes(BinaryWriter writer, byte[] bytes)
        {
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static byte[] ReadBytes(BinaryReader reader, int expected)
        {
            int length = reader.ReadInt32();
            if (length != expected)
                throw new InvalidDataException("PRISM section length is invalid.");
            return ReadExact(reader, length);
        }

        private static byte[] ReadBoundedBytes(BinaryReader reader, int maximum,
            string label)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > maximum)
                throw new InvalidDataException($"PRISM {label} section is too large.");
            return ReadExact(reader, length);
        }

        private static byte[] ReadExact(BinaryReader reader, int length)
        {
            long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (remaining < length)
                throw new EndOfStreamException("PRISM section is truncated.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }

        private static void WriteReferences(BinaryWriter writer, string[] references)
        {
            writer.Write(references.Length);
            foreach (string reference in references)
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(reference);
                writer.Write(utf8.Length);
                writer.Write(utf8);
            }
        }

        private static string[] ReadReferences(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumKeyframeReferences)
                throw new InvalidDataException("PRISM keyframe reference count is invalid.");
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32();
                if (length < 0 || length > MaximumReferenceUtf8Bytes)
                    throw new InvalidDataException(
                        "PRISM keyframe reference length is invalid.");
                result[i] = Encoding.UTF8.GetString(ReadExact(reader, length));
            }
            return result;
        }

        private static string ValidateReferences(string[] references)
        {
            if (references == null || references.Length > MaximumKeyframeReferences)
                return "PRISM keyframe references are invalid.";
            foreach (string reference in references)
            {
                if (!StoragePath.IsSafeRelativePath(reference) ||
                    Encoding.UTF8.GetByteCount(reference) > MaximumReferenceUtf8Bytes)
                    return "PRISM keyframe reference path is invalid.";
            }
            return null;
        }
    }
}
