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
        public int ManifoldCount;
        public int ManifoldLinkCount;
        public int ManifoldFrontierCount;
        public uint FilmGeneration;
        public uint BoundaryGeneration;
        public uint DisplacementGeneration;
        public uint MeshletGeneration;
        public ulong CalibrationEpoch;
        public byte[] FilmHeaders = Array.Empty<byte>();
        public byte[] FilmInformation = Array.Empty<byte>();
        public byte[] FilmSlotStates = Array.Empty<byte>();
        public byte[] ActiveFilmIndices = Array.Empty<byte>();
        public byte[] DirtyFilmIndices = Array.Empty<byte>();
        public byte[] FilmAllocatorState = Array.Empty<byte>();
        public byte[] PressureManifoldHeaders = Array.Empty<byte>();
        public byte[] FilmMemberships = Array.Empty<byte>();
        public byte[] ManifoldLinks = Array.Empty<byte>();
        public byte[] ManifoldLinkIncidences = Array.Empty<byte>();
        public byte[] ManifoldFrontierIncidences = Array.Empty<byte>();
        public byte[] LatentFrontiers = Array.Empty<byte>();
        public byte[] ManifoldAllocatorState = Array.Empty<byte>();
        public byte[] CurrentManifoldState = Array.Empty<byte>();
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
    /// Strict versioned binary codec for resumable ContactFilms. Version 5 persists
    /// the generation-safe PressureManifold graph, film-slot ownership and independent
    /// pressure posterior. Versions 2-4 remain readable and are widened into an
    /// explicit RestoredUnlinked latent graph rather than reinterpreting old padding.
    /// </summary>
    public static class PrismCanonicalChunkCodec
    {
        public const int FormatVersion = 5;
        private const int Version4 = 4;
        private const int PreviousFormatVersion = 3;
        private const int LegacyFormatVersion = 2;
        private const int LegacyFilmHeaderStride = 144;
        private const int LegacyDisplacementCellStride = 32;
        private const int LegacyFilmInformationStride = 9 * sizeof(float) * 4;
        private const int LegacyMeshletVertexStride = 64;
        private const uint Magic = 0x33515043; // "CPQ3"
        private const uint EndianMarker = 0x01020304;
        private const int MaximumFilms = 1_000_000;
        private const int MaximumBoundaries = 2_000_000;
        private const int MaximumManifolds = 1_000_000;
        private const int MaximumManifoldLinks = 2_000_000;
        private const int MaximumManifoldFrontiers = 4_000_000;
        private const int MaximumDisplacementPages = 4_000_000;
        private const int MaximumMeshletVertices = 16_000_000;
        private const int MaximumMeshletIndices = 64_000_000;
        private const int MaximumMeshletDescriptors = 4_000_000;
        private const int MaximumOpaqueSectionBytes = 1024 * 1024 * 1024;
        private const int MaximumKeyframeReferences = 65_536;
        private const int MaximumReferenceUtf8Bytes = 4096;
        private const int FilmInformationStride =
            ContactFilmPool.InformationRecords * sizeof(float) * 4;
        private const int BoundaryInformationStride =
            ContactBoundaryPool.InformationRecordsPerBoundary * sizeof(float) * 4;
        private const int DisplacementAllocatorBytes = sizeof(uint) * 8;
        private const int FilmAllocatorBytes = sizeof(uint) * 8;
        private const int ManifoldAllocatorBytes = sizeof(uint) *
            PressureManifoldPool.AllocatorWords;
        private const int CurrentManifoldBytes = sizeof(uint) * 4;

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
                writer.Write(PressureManifoldHeaderGpu.Stride);
                writer.Write(FilmMembershipGpu.Stride);
                writer.Write(ManifoldLinkGpu.Stride);
                writer.Write(ManifoldLinkIncidenceGpu.Stride);
                writer.Write(ManifoldFrontierIncidenceGpu.Stride);
                writer.Write(LatentFrontierSegmentGpu.Stride);
                writer.Write(snapshot.FilmCount);
                writer.Write(snapshot.BoundaryCount);
                writer.Write(snapshot.DisplacementBasePageCount);
                writer.Write(snapshot.DisplacementMicroPageCount);
                writer.Write(snapshot.MeshletVertexCount);
                writer.Write(snapshot.MeshletIndexCount);
                writer.Write(snapshot.MeshletDescriptorCount);
                writer.Write(snapshot.ManifoldCount);
                writer.Write(snapshot.ManifoldLinkCount);
                writer.Write(snapshot.ManifoldFrontierCount);
                writer.Write(snapshot.FilmGeneration);
                writer.Write(snapshot.BoundaryGeneration);
                writer.Write(snapshot.DisplacementGeneration);
                writer.Write(snapshot.MeshletGeneration);
                writer.Write(snapshot.CalibrationEpoch);
                WriteBytes(writer, snapshot.FilmHeaders);
                WriteBytes(writer, snapshot.FilmInformation);
                WriteBytes(writer, snapshot.FilmSlotStates);
                WriteBytes(writer, snapshot.ActiveFilmIndices);
                WriteBytes(writer, snapshot.DirtyFilmIndices);
                WriteBytes(writer, snapshot.FilmAllocatorState);
                WriteBytes(writer, snapshot.PressureManifoldHeaders);
                WriteBytes(writer, snapshot.FilmMemberships);
                WriteBytes(writer, snapshot.ManifoldLinks);
                WriteBytes(writer, snapshot.ManifoldLinkIncidences);
                WriteBytes(writer, snapshot.ManifoldFrontierIncidences);
                WriteBytes(writer, snapshot.LatentFrontiers);
                WriteBytes(writer, snapshot.ManifoldAllocatorState);
                WriteBytes(writer, snapshot.CurrentManifoldState);
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
                    PreviousFormatVersion => ReadVersion3(reader),
                    Version4 => ReadVersion4(reader),
                    FormatVersion => ReadVersion5(reader),
                    _ => throw new InvalidDataException(
                        $"Unsupported PRISM format version {version}.")
                };
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("PRISM payload has trailing bytes.");
                error = Validate(candidate, version < FormatVersion);
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

        private static PrismCanonicalChunkSnapshot ReadVersion5(BinaryReader reader)
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
            RequireStride(reader, PressureManifoldHeaderGpu.Stride,
                "pressure manifold header");
            RequireStride(reader, FilmMembershipGpu.Stride, "film membership");
            RequireStride(reader, ManifoldLinkGpu.Stride, "manifold link");
            RequireStride(reader, ManifoldLinkIncidenceGpu.Stride,
                "manifold link incidence");
            RequireStride(reader, ManifoldFrontierIncidenceGpu.Stride,
                "manifold frontier incidence");
            RequireStride(reader, LatentFrontierSegmentGpu.Stride,
                "latent frontier");
            var candidate = new PrismCanonicalChunkSnapshot
            {
                FilmCount = reader.ReadInt32(),
                BoundaryCount = reader.ReadInt32(),
                DisplacementBasePageCount = reader.ReadInt32(),
                DisplacementMicroPageCount = reader.ReadInt32(),
                MeshletVertexCount = reader.ReadInt32(),
                MeshletIndexCount = reader.ReadInt32(),
                MeshletDescriptorCount = reader.ReadInt32(),
                ManifoldCount = reader.ReadInt32(),
                ManifoldLinkCount = reader.ReadInt32(),
                ManifoldFrontierCount = reader.ReadInt32(),
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
            candidate.FilmSlotStates = ReadBytes(reader,
                Bytes(candidate.FilmCount, ContactFilmSlotStateGpu.Stride));
            candidate.ActiveFilmIndices = ReadBytes(reader,
                Bytes(candidate.FilmCount, sizeof(uint)));
            candidate.DirtyFilmIndices = ReadBytes(reader,
                Bytes(candidate.FilmCount, sizeof(uint)));
            candidate.FilmAllocatorState = ReadBytes(reader, FilmAllocatorBytes);
            candidate.PressureManifoldHeaders = ReadBytes(reader,
                Bytes(candidate.ManifoldCount, PressureManifoldHeaderGpu.Stride));
            candidate.FilmMemberships = ReadBytes(reader,
                Bytes(candidate.FilmCount, FilmMembershipGpu.Stride));
            candidate.ManifoldLinks = ReadBytes(reader,
                Bytes(candidate.ManifoldLinkCount, ManifoldLinkGpu.Stride));
            candidate.ManifoldLinkIncidences = ReadBytes(reader,
                Bytes(checked(candidate.ManifoldLinkCount * 2),
                    ManifoldLinkIncidenceGpu.Stride));
            candidate.ManifoldFrontierIncidences = ReadBytes(reader,
                Bytes(candidate.ManifoldFrontierCount,
                    ManifoldFrontierIncidenceGpu.Stride));
            candidate.LatentFrontiers = ReadBytes(reader,
                Bytes(candidate.ManifoldFrontierCount,
                    LatentFrontierSegmentGpu.Stride));
            candidate.ManifoldAllocatorState = ReadBytes(reader,
                ManifoldAllocatorBytes);
            candidate.CurrentManifoldState = ReadBytes(reader,
                CurrentManifoldBytes);
            ReadSharedGeometrySections(reader, candidate, false);
            return candidate;
        }

        private static void ReadSharedGeometrySections(BinaryReader reader,
            PrismCanonicalChunkSnapshot candidate, bool legacyMeshletVertex)
        {
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
            candidate.MeshletVertices = legacyMeshletVertex
                ? ReadLegacyMeshletVertices(reader, candidate.MeshletVertexCount)
                : ReadBytes(reader, Bytes(candidate.MeshletVertexCount,
                    ContactMeshletVertexGpu.Stride));
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
        }

        private static PrismCanonicalChunkSnapshot ReadVersion4(BinaryReader reader)
        {
            RequireStride(reader, ContactFilmHeaderGpu.Stride, "film header");
            RequireStride(reader, ContactBoundaryHeaderGpu.Stride, "boundary header");
            RequireStride(reader, DisplacementPageHeaderGpu.Stride,
                "displacement page");
            RequireStride(reader, DisplacementCellGpu.Stride, "displacement cell");
            RequireStride(reader, ContactTopologyEvidenceGpu.Stride,
                "topology evidence");
            RequireStride(reader, LegacyMeshletVertexStride, "meshlet vertex");
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
            candidate.FilmInformation = ReadLegacyFilmInformation(reader,
                candidate.FilmCount);
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
            candidate.MeshletVertices = ReadLegacyMeshletVertices(reader,
                candidate.MeshletVertexCount);
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
            BuildLegacyRestoredTopology(candidate);
            return candidate;
        }

        private static PrismCanonicalChunkSnapshot ReadVersion3(BinaryReader reader)
        {
            RequireStride(reader, LegacyFilmHeaderStride, "film header");
            RequireStride(reader, ContactBoundaryHeaderGpu.Stride, "boundary header");
            RequireStride(reader, DisplacementPageHeaderGpu.Stride,
                "displacement page");
            RequireStride(reader, LegacyDisplacementCellStride,
                "displacement cell");
            RequireStride(reader, ContactTopologyEvidenceGpu.Stride,
                "topology evidence");
            RequireStride(reader, LegacyMeshletVertexStride, "meshlet vertex");
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
            candidate.FilmHeaders = ReadLegacyFilmHeaders(reader,
                candidate.FilmCount);
            candidate.FilmInformation = ReadLegacyFilmInformation(reader,
                candidate.FilmCount);
            candidate.BoundaryHeaders = ReadBytes(reader,
                Bytes(candidate.BoundaryCount, ContactBoundaryHeaderGpu.Stride));
            candidate.BoundaryInformation = ReadBytes(reader,
                Bytes(candidate.BoundaryCount, BoundaryInformationStride));
            candidate.DisplacementPageHeaders = ReadBytes(reader,
                Bytes(checked(candidate.DisplacementBasePageCount +
                    candidate.DisplacementMicroPageCount),
                    DisplacementPageHeaderGpu.Stride));
            candidate.DisplacementBaseCells = ReadLegacyDisplacementCells(reader,
                checked(candidate.DisplacementBasePageCount *
                    ContactDisplacementPool.BaseCellsPerPage));
            candidate.DisplacementMicroCells = ReadLegacyDisplacementCells(reader,
                checked(candidate.DisplacementMicroPageCount *
                    ContactDisplacementPool.MicroCellsPerPage));
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
            candidate.MeshletVertices = ReadLegacyMeshletVertices(reader,
                candidate.MeshletVertexCount);
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
            BuildLegacyRestoredTopology(candidate);
            return candidate;
        }

        private static PrismCanonicalChunkSnapshot ReadVersion2(BinaryReader reader)
        {
            RequireStride(reader, LegacyFilmHeaderStride, "film header");
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
            candidate.FilmHeaders = ReadLegacyFilmHeaders(reader,
                candidate.FilmCount);
            candidate.FilmInformation = ReadLegacyFilmInformation(reader,
                candidate.FilmCount);
            candidate.BoundaryHeaders = ReadBytes(reader,
                Bytes(candidate.BoundaryCount, ContactBoundaryHeaderGpu.Stride));
            candidate.BoundaryInformation = ReadBytes(reader,
                Bytes(candidate.BoundaryCount, BoundaryInformationStride));
            BuildLegacyRestoredTopology(candidate);
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
            if (!LengthIs(snapshot.FilmSlotStates,
                    (long)snapshot.FilmCount * ContactFilmSlotStateGpu.Stride) ||
                !LengthIs(snapshot.ActiveFilmIndices,
                    (long)snapshot.FilmCount * sizeof(uint)) ||
                !LengthIs(snapshot.DirtyFilmIndices,
                    (long)snapshot.FilmCount * sizeof(uint)) ||
                !LengthIs(snapshot.FilmAllocatorState, FilmAllocatorBytes) ||
                !LengthIs(snapshot.PressureManifoldHeaders,
                    (long)snapshot.ManifoldCount * PressureManifoldHeaderGpu.Stride) ||
                !LengthIs(snapshot.FilmMemberships,
                    (long)snapshot.FilmCount * FilmMembershipGpu.Stride) ||
                !LengthIs(snapshot.ManifoldLinks,
                    (long)snapshot.ManifoldLinkCount * ManifoldLinkGpu.Stride) ||
                !LengthIs(snapshot.ManifoldLinkIncidences,
                    (long)snapshot.ManifoldLinkCount * 2 *
                    ManifoldLinkIncidenceGpu.Stride) ||
                !LengthIs(snapshot.ManifoldFrontierIncidences,
                    (long)snapshot.ManifoldFrontierCount *
                    ManifoldFrontierIncidenceGpu.Stride) ||
                !LengthIs(snapshot.LatentFrontiers,
                    (long)snapshot.ManifoldFrontierCount *
                    LatentFrontierSegmentGpu.Stride) ||
                !LengthIs(snapshot.ManifoldAllocatorState,
                    ManifoldAllocatorBytes) ||
                !LengthIs(snapshot.CurrentManifoldState,
                    CurrentManifoldBytes))
                return "PRISM manifold/slot lengths do not match canonical counts.";
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
                snapshot.MeshletDescriptorCount > MaximumMeshletDescriptors ||
                snapshot.ManifoldCount < 0 ||
                snapshot.ManifoldCount > MaximumManifolds ||
                snapshot.ManifoldLinkCount < 0 ||
                snapshot.ManifoldLinkCount > MaximumManifoldLinks ||
                snapshot.ManifoldFrontierCount < 0 ||
                snapshot.ManifoldFrontierCount > MaximumManifoldFrontiers)
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

        private static byte[] ReadLegacyFilmHeaders(BinaryReader reader, int count)
        {
            byte[] legacy = ReadBytes(reader, Bytes(count,
                LegacyFilmHeaderStride));
            byte[] widened = new byte[Bytes(count, ContactFilmHeaderGpu.Stride)];
            const int prefixBytes = 132; // through boundaryCount
            const int insertedMaskBytes = sizeof(uint) * 2;
            const int legacyTailBytes = sizeof(uint) * 3;
            for (int index = 0; index < count; index++)
            {
                int source = index * LegacyFilmHeaderStride;
                int destination = index * ContactFilmHeaderGpu.Stride;
                Buffer.BlockCopy(legacy, source, widened, destination,
                    prefixBytes);
                // supportMaskLow/high remain zero: an old rectangular primitive has
                // no trustworthy observed domain and must not invent one on restore.
                Buffer.BlockCopy(legacy, source + prefixBytes, widened,
                    destination + prefixBytes + insertedMaskBytes,
                    legacyTailBytes);
            }
            return widened;
        }

        private static byte[] ReadLegacyFilmInformation(BinaryReader reader,
            int count)
        {
            byte[] legacy = ReadBytes(reader,
                Bytes(count, LegacyFilmInformationStride));
            byte[] widened = new byte[Bytes(count, FilmInformationStride)];
            for (int index = 0; index < count; index++)
                Buffer.BlockCopy(legacy,
                    index * LegacyFilmInformationStride, widened,
                    index * FilmInformationStride,
                    LegacyFilmInformationStride);
            // Record 9 remains zero: old artifacts carry no independent-view mask
            // or covariance-derived normal variance and therefore fail closed to
            // collecting new evidence rather than fabricating prior precision.
            return widened;
        }

        private static byte[] ReadLegacyMeshletVertices(BinaryReader reader,
            int count)
        {
            byte[] legacy = ReadBytes(reader,
                Bytes(count, LegacyMeshletVertexStride));
            byte[] widened = new byte[Bytes(count, ContactMeshletVertexGpu.Stride)];
            for (int index = 0; index < count; index++)
                Buffer.BlockCopy(legacy, index * LegacyMeshletVertexStride,
                    widened, index * ContactMeshletVertexGpu.Stride,
                    LegacyMeshletVertexStride);
            // boundarySampleId/reserved remain zero. Old derived caches did not carry
            // a generation-safe canonical seam identity and must not fabricate one.
            return widened;
        }

        private static void BuildLegacyRestoredTopology(
            PrismCanonicalChunkSnapshot snapshot)
        {
            int activeCount = 0;
            int freeCount = 0;
            int freeHead = 0;
            int[] frontierStart = new int[snapshot.FilmCount];
            for (int film = 0; film < snapshot.FilmCount; film++)
            {
                uint flags = BitConverter.ToUInt32(snapshot.FilmHeaders,
                    film * ContactFilmHeaderGpu.Stride + 12);
                if ((flags & (uint)ContactFilmFlags.Active) != 0)
                {
                    frontierStart[film] = activeCount * 4 + 1;
                    activeCount++;
                }
            }
            snapshot.ManifoldCount = activeCount > 0 ? 1 : 0;
            snapshot.ManifoldLinkCount = 0;
            snapshot.ManifoldFrontierCount = activeCount * 4;
            snapshot.FilmSlotStates = BuildSection(writer =>
            {
                uint activeOrdinal = 0u;
                for (int film = 0; film < snapshot.FilmCount; film++)
                {
                    int offset = film * ContactFilmHeaderGpu.Stride;
                    uint generation = BitConverter.ToUInt32(snapshot.FilmHeaders,
                        offset + 4);
                    uint flags = BitConverter.ToUInt32(snapshot.FilmHeaders,
                        offset + 12);
                    bool active = (flags & (uint)ContactFilmFlags.Active) != 0;
                    writer.Write(Math.Max(1u, generation));
                    writer.Write(active ? activeOrdinal++ : uint.MaxValue);
                    if (active) writer.Write(0u);
                    else
                    {
                        writer.Write((uint)freeHead);
                        freeHead = film + 1;
                        freeCount++;
                    }
                    writer.Write(active ? 7u : 8u);
                }
            });
            snapshot.ActiveFilmIndices = BuildSection(writer =>
            {
                for (int film = 0; film < snapshot.FilmCount; film++)
                {
                    uint flags = BitConverter.ToUInt32(snapshot.FilmHeaders,
                        film * ContactFilmHeaderGpu.Stride + 12);
                    if ((flags & (uint)ContactFilmFlags.Active) != 0)
                        writer.Write((uint)film);
                }
                for (int tail = activeCount; tail < snapshot.FilmCount; tail++)
                    writer.Write(0u);
            });
            snapshot.DirtyFilmIndices = (byte[])snapshot.ActiveFilmIndices.Clone();
            snapshot.FilmAllocatorState = BuildSection(writer =>
            {
                writer.Write((uint)snapshot.FilmCount);
                writer.Write((uint)activeCount);
                writer.Write(0u);
                writer.Write(Math.Max(1u, snapshot.FilmGeneration));
                writer.Write((uint)freeHead);
                writer.Write((uint)freeCount);
                writer.Write((uint)activeCount);
                writer.Write((uint)activeCount);
            });
            snapshot.PressureManifoldHeaders = snapshot.ManifoldCount == 0
                ? Array.Empty<byte>()
                : BuildSection(writer =>
                {
                    writer.Write(1u); writer.Write(1u); writer.Write(0u);
                    writer.Write((uint)(PressureManifoldFlags.Active |
                        PressureManifoldFlags.DirtyTopology |
                        PressureManifoldFlags.RestoredUnlinked));
                    writer.Write(0f); writer.Write(0f); writer.Write(0f);
                    writer.Write(0.05f);
                    writer.Write(1u); writer.Write((uint)activeCount);
                    writer.Write(0u); writer.Write(0u);
                    writer.Write(activeCount > 0 ? 1u : 0u);
                    writer.Write((uint)snapshot.ManifoldFrontierCount);
                    writer.Write(1u); writer.Write(0u); writer.Write(0u);
                    writer.Write(0u); writer.Write(0u); writer.Write(0u);
                });
            snapshot.FilmMemberships = BuildSection(writer =>
            {
                for (int film = 0; film < snapshot.FilmCount; film++)
                {
                    int offset = film * ContactFilmHeaderGpu.Stride;
                    uint id = BitConverter.ToUInt32(snapshot.FilmHeaders, offset);
                    uint generation = BitConverter.ToUInt32(snapshot.FilmHeaders,
                        offset + 4);
                    uint flags = BitConverter.ToUInt32(snapshot.FilmHeaders,
                        offset + 12);
                    bool active = (flags & (uint)ContactFilmFlags.Active) != 0;
                    writer.Write(id); writer.Write(generation);
                    writer.Write(active ? 1u : 0u); writer.Write(active ? 1u : 0u);
                    writer.Write(0u); writer.Write(0u);
                    writer.Write(active ? (uint)frontierStart[film] : 0u);
                    writer.Write(active ? 4u : 0u);
                    writer.Write(active ? 3u : 0u); writer.Write(1u);
                }
            });
            snapshot.ManifoldLinks = Array.Empty<byte>();
            snapshot.ManifoldLinkIncidences = Array.Empty<byte>();
            snapshot.ManifoldFrontierIncidences = BuildSection(writer =>
            {
                int incidence = 0;
                for (int film = 0; film < snapshot.FilmCount; film++)
                {
                    int offset = film * ContactFilmHeaderGpu.Stride;
                    uint id = BitConverter.ToUInt32(snapshot.FilmHeaders, offset);
                    uint generation = BitConverter.ToUInt32(snapshot.FilmHeaders,
                        offset + 4);
                    uint flags = BitConverter.ToUInt32(snapshot.FilmHeaders,
                        offset + 12);
                    if ((flags & (uint)ContactFilmFlags.Active) == 0) continue;
                    int first = incidence + 1;
                    for (int edge = 0; edge < 4; edge++)
                    {
                        int incidenceId = first + edge;
                        writer.Write((uint)incidenceId); writer.Write(1u);
                        writer.Write((uint)incidenceId); writer.Write(1u);
                        writer.Write(id); writer.Write(generation);
                        writer.Write(edge < 3 ? (uint)(incidenceId + 1) : 0u);
                        writer.Write(edge < 3 ? 1u : 0u);
                        writer.Write(1u); writer.Write(0u);
                        incidence++;
                    }
                }
            });
            snapshot.LatentFrontiers = BuildSection(writer =>
            {
                int activeOrdinal = 0;
                for (int film = 0; film < snapshot.FilmCount; film++)
                {
                    int offset = film * ContactFilmHeaderGpu.Stride;
                    uint id = BitConverter.ToUInt32(snapshot.FilmHeaders, offset);
                    uint generation = BitConverter.ToUInt32(snapshot.FilmHeaders,
                        offset + 4);
                    uint flags = BitConverter.ToUInt32(snapshot.FilmHeaders,
                        offset + 12);
                    if ((flags & (uint)ContactFilmFlags.Active) == 0) continue;
                    int first = activeOrdinal * 4 + 1;
                    float[] edges = { 0,0,1,0, 1,0,1,1,
                        1,1,0,1, 0,1,0,0 };
                    for (int edge = 0; edge < 4; edge++)
                    {
                        int frontierId = first + edge;
                        writer.Write((uint)frontierId); writer.Write(1u);
                        writer.Write(1u); writer.Write(1u);
                        writer.Write(id); writer.Write(generation);
                        writer.Write((uint)(first + ((edge + 1) & 3)));
                        writer.Write(1u);
                        writer.Write((uint)(first + ((edge + 3) & 3)));
                        writer.Write(1u);
                        writer.Write(7u); writer.Write(1u);
                        for (int component = 0; component < 4; component++)
                            writer.Write(edges[edge * 4 + component]);
                        writer.Write(0.05f); writer.Write(0f);
                        writer.Write(0f); writer.Write(0f);
                    }
                    activeOrdinal++;
                }
            });
            snapshot.ManifoldAllocatorState = BuildSection(writer =>
            {
                writer.Write((uint)snapshot.ManifoldCount);
                writer.Write((uint)snapshot.ManifoldCount);
                writer.Write(0u); writer.Write(1u);
                writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(1u);
                writer.Write((uint)snapshot.ManifoldFrontierCount);
                writer.Write((uint)snapshot.ManifoldFrontierCount);
                writer.Write(0u); writer.Write(1u);
                writer.Write((uint)activeCount); writer.Write(0u);
                writer.Write(0u); writer.Write(1u);
            });
            snapshot.CurrentManifoldState = BuildSection(writer =>
            {
                writer.Write(snapshot.ManifoldCount > 0 ? 1u : 0u);
                writer.Write(snapshot.ManifoldCount > 0 ? 1u : 0u);
                writer.Write(0u); writer.Write(0u);
            });
        }

        private static byte[] BuildSection(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                write(writer);
            return stream.ToArray();
        }

        private static byte[] ReadLegacyDisplacementCells(BinaryReader reader,
            int count)
        {
            byte[] legacy = ReadBytes(reader, Bytes(count,
                LegacyDisplacementCellStride));
            byte[] widened = new byte[Bytes(count, DisplacementCellGpu.Stride)];
            const int prefixBytes = 7 * sizeof(float); // through residual variance
            const int insertedPosteriorBytes = sizeof(float) + sizeof(uint);
            const int legacyTailBytes = sizeof(uint); // revision
            for (int index = 0; index < count; index++)
            {
                int source = index * LegacyDisplacementCellStride;
                int destination = index * DisplacementCellGpu.Stride;
                Buffer.BlockCopy(legacy, source, widened, destination,
                    prefixBytes);
                // A v3 cell had no persistent free-space posterior. New pressure
                // and view-mask fields remain zero, so restore cannot invent
                // destructive evidence.
                Buffer.BlockCopy(legacy, source + prefixBytes, widened,
                    destination + prefixBytes + insertedPosteriorBytes,
                    legacyTailBytes);
            }
            return widened;
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
