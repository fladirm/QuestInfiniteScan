using System;
using System.IO;
using System.Text;
using Genesis.RoomScan.Prism;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Detached canonical Cone-PRISM state. Rectangular chart extents are numerical
    /// domains only; all durable topology lives in support contours, half-edges,
    /// shared boundary curves and ordered frontier loops.
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
        public int SupportContourPageCount;
        public int SupportContourSegmentCount;
        public int SurfaceHalfEdgeCount;
        public int FrontierLoopCount;
        public int ContinuationEvidenceCount;
        public int CrossChunkPortalCount;
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
        public byte[] ManifoldAllocatorState = Array.Empty<byte>();
        public byte[] CurrentManifoldState = Array.Empty<byte>();
        public byte[] SupportContourPages = Array.Empty<byte>();
        public byte[] SupportContours = Array.Empty<byte>();
        public byte[] SurfaceHalfEdges = Array.Empty<byte>();
        public byte[] FrontierLoops = Array.Empty<byte>();
        public byte[] ContinuationEvidence = Array.Empty<byte>();
        public byte[] ElasticChartStates = Array.Empty<byte>();
        public byte[] FilmTopologyRanges = Array.Empty<byte>();
        public byte[] AtlasAllocatorState = Array.Empty<byte>();
        public byte[] CrossChunkTopologyPortals = Array.Empty<byte>();

        public byte[] BoundaryHeaders = Array.Empty<byte>();
        public byte[] BoundaryInformation = Array.Empty<byte>();
        public byte[] BoundaryCurveTopology = Array.Empty<byte>();

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

        public byte[] AppearanceState = Array.Empty<byte>();
        public byte[] ObservationState = Array.Empty<byte>();
        public string[] KeyframeReferences = Array.Empty<string>();
    }

    /// <summary>
    /// Strict schema-v6 codec. Versions 2-5 encoded rectangular frontier ontology
    /// and are intentionally rejected: reconstructing four chart edges during
    /// migration would fabricate topology that was never measured.
    /// </summary>
    public static class PrismCanonicalChunkCodec
    {
        public const int FormatVersion = 6;

        private const uint Magic = 0x33515043; // CPQ3
        private const uint EndianMarker = 0x01020304;
        private const uint AtlasLayoutMagic = 0x364D5043; // CPM6
        private const int MaximumFilms = 1_000_000;
        private const int MaximumBoundaries = 2_000_000;
        private const int MaximumManifolds = 1_000_000;
        private const int MaximumAtlasElements = 16_000_000;
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
        private const int FilmAllocatorBytes = sizeof(uint) * 8;
        private const int ManifoldAllocatorBytes = sizeof(uint) *
            PressureManifoldPool.AllocatorWords;
        private const int CurrentManifoldBytes = sizeof(uint) * 4;
        private const int AtlasAllocatorBytes = sizeof(uint) * 16;
        private const int DisplacementAllocatorBytes = sizeof(uint) * 8;
        private const int FilmTopologyRangeStride = sizeof(uint) * 4;

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
                writer.Write(AtlasLayoutMagic);
                WriteStrides(writer);
                WriteCounts(writer, snapshot);

                WriteBytes(writer, snapshot.FilmHeaders);
                WriteBytes(writer, snapshot.FilmInformation);
                WriteBytes(writer, snapshot.FilmSlotStates);
                WriteBytes(writer, snapshot.ActiveFilmIndices);
                WriteBytes(writer, snapshot.DirtyFilmIndices);
                WriteBytes(writer, snapshot.FilmAllocatorState);

                WriteBytes(writer, snapshot.PressureManifoldHeaders);
                WriteBytes(writer, snapshot.FilmMemberships);
                WriteBytes(writer, snapshot.ManifoldAllocatorState);
                WriteBytes(writer, snapshot.CurrentManifoldState);
                WriteBytes(writer, snapshot.SupportContourPages);
                WriteBytes(writer, snapshot.SupportContours);
                WriteBytes(writer, snapshot.SurfaceHalfEdges);
                WriteBytes(writer, snapshot.FrontierLoops);
                WriteBytes(writer, snapshot.ContinuationEvidence);
                WriteBytes(writer, snapshot.ElasticChartStates);
                WriteBytes(writer, snapshot.FilmTopologyRanges);
                WriteBytes(writer, snapshot.AtlasAllocatorState);
                WriteBytes(writer, snapshot.CrossChunkTopologyPortals);

                WriteBytes(writer, snapshot.BoundaryHeaders);
                WriteBytes(writer, snapshot.BoundaryInformation);
                WriteBytes(writer, snapshot.BoundaryCurveTopology);

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

        public static bool TryRead(Stream stream,
            out PrismCanonicalChunkSnapshot snapshot, out string error)
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
                if (version != FormatVersion)
                    throw new InvalidDataException(
                        $"PRISM schema {version} is not a support-contour atlas; " +
                        $"schema {FormatVersion} is required.");
                if (reader.ReadUInt32() != AtlasLayoutMagic)
                    throw new InvalidDataException(
                        "PRISM v6 layout predates the canonical manifold atlas.");

                RequireStrides(reader);
                PrismCanonicalChunkSnapshot candidate = ReadCounts(reader);
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
                candidate.FilmAllocatorState = ReadBytes(reader,
                    FilmAllocatorBytes);

                candidate.PressureManifoldHeaders = ReadBytes(reader,
                    Bytes(candidate.ManifoldCount,
                        PressureManifoldHeaderGpu.Stride));
                candidate.FilmMemberships = ReadBytes(reader,
                    Bytes(candidate.FilmCount, FilmMembershipGpu.Stride));
                candidate.ManifoldAllocatorState = ReadBytes(reader,
                    ManifoldAllocatorBytes);
                candidate.CurrentManifoldState = ReadBytes(reader,
                    CurrentManifoldBytes);
                candidate.SupportContourPages = ReadBytes(reader,
                    Bytes(candidate.SupportContourPageCount,
                        SupportContourPageGpu.Stride));
                candidate.SupportContours = ReadBytes(reader,
                    Bytes(candidate.SupportContourSegmentCount,
                        SupportContourSegmentGpu.Stride));
                candidate.SurfaceHalfEdges = ReadBytes(reader,
                    Bytes(candidate.SurfaceHalfEdgeCount,
                        SurfaceHalfEdgeGpu.Stride));
                candidate.FrontierLoops = ReadBytes(reader,
                    Bytes(candidate.FrontierLoopCount, FrontierLoopGpu.Stride));
                candidate.ContinuationEvidence = ReadBytes(reader,
                    Bytes(candidate.ContinuationEvidenceCount,
                        ContinuationEvidenceGpu.Stride));
                candidate.ElasticChartStates = ReadBytes(reader,
                    Bytes(candidate.FilmCount, ElasticChartStateGpu.Stride));
                candidate.FilmTopologyRanges = ReadBytes(reader,
                    Bytes(candidate.FilmCount, FilmTopologyRangeStride));
                candidate.AtlasAllocatorState = ReadBytes(reader,
                    AtlasAllocatorBytes);
                candidate.CrossChunkTopologyPortals = ReadBytes(reader,
                    Bytes(candidate.CrossChunkPortalCount,
                        CrossChunkTopologyPortalGpu.Stride));

                candidate.BoundaryHeaders = ReadBytes(reader,
                    Bytes(candidate.BoundaryCount,
                        ContactBoundaryHeaderGpu.Stride));
                candidate.BoundaryInformation = ReadBytes(reader,
                    Bytes(candidate.BoundaryCount, BoundaryInformationStride));
                candidate.BoundaryCurveTopology = ReadBytes(reader,
                    Bytes(candidate.BoundaryCount,
                        BoundaryCurveTopologyGpu.Stride));

                int displacementPages = checked(
                    candidate.DisplacementBasePageCount +
                    candidate.DisplacementMicroPageCount);
                candidate.DisplacementPageHeaders = ReadBytes(reader,
                    Bytes(displacementPages, DisplacementPageHeaderGpu.Stride));
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
                    Bytes(candidate.MeshletVertexCount,
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

                if (stream.Position != stream.Length)
                    throw new InvalidDataException(
                        "PRISM payload has trailing bytes.");
                error = Validate(candidate);
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

        private static void WriteStrides(BinaryWriter writer)
        {
            writer.Write(ContactFilmHeaderGpu.Stride);
            writer.Write(ContactBoundaryHeaderGpu.Stride);
            writer.Write(DisplacementPageHeaderGpu.Stride);
            writer.Write(DisplacementCellGpu.Stride);
            writer.Write(ContactTopologyEvidenceGpu.Stride);
            writer.Write(ContactMeshletVertexGpu.Stride);
            writer.Write(ContactMeshletDescriptorGpu.Stride);
            writer.Write(PressureManifoldHeaderGpu.Stride);
            writer.Write(FilmMembershipGpu.Stride);
            writer.Write(SupportContourPageGpu.Stride);
            writer.Write(SupportContourSegmentGpu.Stride);
            writer.Write(SurfaceHalfEdgeGpu.Stride);
            writer.Write(FrontierLoopGpu.Stride);
            writer.Write(ContinuationEvidenceGpu.Stride);
            writer.Write(ElasticChartStateGpu.Stride);
            writer.Write(CrossChunkTopologyPortalGpu.Stride);
            writer.Write(BoundaryCurveTopologyGpu.Stride);
            writer.Write(FilmTopologyRangeStride);
        }

        private static void RequireStrides(BinaryReader reader)
        {
            RequireStride(reader, ContactFilmHeaderGpu.Stride, "film header");
            RequireStride(reader, ContactBoundaryHeaderGpu.Stride,
                "boundary header");
            RequireStride(reader, DisplacementPageHeaderGpu.Stride,
                "displacement page");
            RequireStride(reader, DisplacementCellGpu.Stride,
                "displacement cell");
            RequireStride(reader, ContactTopologyEvidenceGpu.Stride,
                "topology evidence");
            RequireStride(reader, ContactMeshletVertexGpu.Stride,
                "meshlet vertex");
            RequireStride(reader, ContactMeshletDescriptorGpu.Stride,
                "meshlet descriptor");
            RequireStride(reader, PressureManifoldHeaderGpu.Stride,
                "pressure manifold header");
            RequireStride(reader, FilmMembershipGpu.Stride, "film membership");
            RequireStride(reader, SupportContourPageGpu.Stride,
                "support contour page");
            RequireStride(reader, SupportContourSegmentGpu.Stride,
                "support contour segment");
            RequireStride(reader, SurfaceHalfEdgeGpu.Stride,
                "surface half-edge");
            RequireStride(reader, FrontierLoopGpu.Stride, "frontier loop");
            RequireStride(reader, ContinuationEvidenceGpu.Stride,
                "continuation evidence");
            RequireStride(reader, ElasticChartStateGpu.Stride,
                "elastic chart state");
            RequireStride(reader, CrossChunkTopologyPortalGpu.Stride,
                "cross-chunk topology portal");
            RequireStride(reader, BoundaryCurveTopologyGpu.Stride,
                "boundary curve topology");
            RequireStride(reader, FilmTopologyRangeStride,
                "film topology range");
        }

        private static void WriteCounts(BinaryWriter writer,
            PrismCanonicalChunkSnapshot snapshot)
        {
            writer.Write(snapshot.FilmCount);
            writer.Write(snapshot.BoundaryCount);
            writer.Write(snapshot.DisplacementBasePageCount);
            writer.Write(snapshot.DisplacementMicroPageCount);
            writer.Write(snapshot.MeshletVertexCount);
            writer.Write(snapshot.MeshletIndexCount);
            writer.Write(snapshot.MeshletDescriptorCount);
            writer.Write(snapshot.ManifoldCount);
            writer.Write(snapshot.SupportContourPageCount);
            writer.Write(snapshot.SupportContourSegmentCount);
            writer.Write(snapshot.SurfaceHalfEdgeCount);
            writer.Write(snapshot.FrontierLoopCount);
            writer.Write(snapshot.ContinuationEvidenceCount);
            writer.Write(snapshot.CrossChunkPortalCount);
            writer.Write(snapshot.FilmGeneration);
            writer.Write(snapshot.BoundaryGeneration);
            writer.Write(snapshot.DisplacementGeneration);
            writer.Write(snapshot.MeshletGeneration);
            writer.Write(snapshot.CalibrationEpoch);
        }

        private static PrismCanonicalChunkSnapshot ReadCounts(BinaryReader reader) =>
            new()
            {
                FilmCount = reader.ReadInt32(),
                BoundaryCount = reader.ReadInt32(),
                DisplacementBasePageCount = reader.ReadInt32(),
                DisplacementMicroPageCount = reader.ReadInt32(),
                MeshletVertexCount = reader.ReadInt32(),
                MeshletIndexCount = reader.ReadInt32(),
                MeshletDescriptorCount = reader.ReadInt32(),
                ManifoldCount = reader.ReadInt32(),
                SupportContourPageCount = reader.ReadInt32(),
                SupportContourSegmentCount = reader.ReadInt32(),
                SurfaceHalfEdgeCount = reader.ReadInt32(),
                FrontierLoopCount = reader.ReadInt32(),
                ContinuationEvidenceCount = reader.ReadInt32(),
                CrossChunkPortalCount = reader.ReadInt32(),
                FilmGeneration = reader.ReadUInt32(),
                BoundaryGeneration = reader.ReadUInt32(),
                DisplacementGeneration = reader.ReadUInt32(),
                MeshletGeneration = reader.ReadUInt32(),
                CalibrationEpoch = reader.ReadUInt64()
            };

        private static string Validate(PrismCanonicalChunkSnapshot snapshot)
        {
            if (snapshot == null) return "PRISM snapshot is null.";
            try { ValidateCounts(snapshot); }
            catch (Exception exception) { return exception.Message; }

            if (!LengthIs(snapshot.FilmHeaders,
                    (long)snapshot.FilmCount * ContactFilmHeaderGpu.Stride) ||
                !LengthIs(snapshot.FilmInformation,
                    (long)snapshot.FilmCount * FilmInformationStride) ||
                !LengthIs(snapshot.FilmSlotStates,
                    (long)snapshot.FilmCount * ContactFilmSlotStateGpu.Stride) ||
                !LengthIs(snapshot.ActiveFilmIndices,
                    (long)snapshot.FilmCount * sizeof(uint)) ||
                !LengthIs(snapshot.DirtyFilmIndices,
                    (long)snapshot.FilmCount * sizeof(uint)) ||
                !LengthIs(snapshot.FilmAllocatorState, FilmAllocatorBytes))
                return "PRISM film-posterior lengths do not match counts.";

            if (!LengthIs(snapshot.PressureManifoldHeaders,
                    (long)snapshot.ManifoldCount *
                    PressureManifoldHeaderGpu.Stride) ||
                !LengthIs(snapshot.FilmMemberships,
                    (long)snapshot.FilmCount * FilmMembershipGpu.Stride) ||
                !LengthIs(snapshot.ManifoldAllocatorState,
                    ManifoldAllocatorBytes) ||
                !LengthIs(snapshot.CurrentManifoldState,
                    CurrentManifoldBytes) ||
                !LengthIs(snapshot.SupportContourPages,
                    (long)snapshot.SupportContourPageCount *
                    SupportContourPageGpu.Stride) ||
                !LengthIs(snapshot.SupportContours,
                    (long)snapshot.SupportContourSegmentCount *
                    SupportContourSegmentGpu.Stride) ||
                !LengthIs(snapshot.SurfaceHalfEdges,
                    (long)snapshot.SurfaceHalfEdgeCount *
                    SurfaceHalfEdgeGpu.Stride) ||
                !LengthIs(snapshot.FrontierLoops,
                    (long)snapshot.FrontierLoopCount * FrontierLoopGpu.Stride) ||
                !LengthIs(snapshot.ContinuationEvidence,
                    (long)snapshot.ContinuationEvidenceCount *
                    ContinuationEvidenceGpu.Stride) ||
                !LengthIs(snapshot.ElasticChartStates,
                    (long)snapshot.FilmCount * ElasticChartStateGpu.Stride) ||
                !LengthIs(snapshot.FilmTopologyRanges,
                    (long)snapshot.FilmCount * FilmTopologyRangeStride) ||
                !LengthIs(snapshot.AtlasAllocatorState, AtlasAllocatorBytes) ||
                !LengthIs(snapshot.CrossChunkTopologyPortals,
                    (long)snapshot.CrossChunkPortalCount *
                    CrossChunkTopologyPortalGpu.Stride))
                return "PRISM topology-atlas lengths do not match counts.";

            if (!LengthIs(snapshot.BoundaryHeaders,
                    (long)snapshot.BoundaryCount *
                    ContactBoundaryHeaderGpu.Stride) ||
                !LengthIs(snapshot.BoundaryInformation,
                    (long)snapshot.BoundaryCount * BoundaryInformationStride) ||
                !LengthIs(snapshot.BoundaryCurveTopology,
                    (long)snapshot.BoundaryCount *
                    BoundaryCurveTopologyGpu.Stride))
                return "PRISM shared-boundary lengths do not match counts.";

            long displacementPages = (long)snapshot.DisplacementBasePageCount +
                snapshot.DisplacementMicroPageCount;
            if (!LengthIs(snapshot.DisplacementPageHeaders,
                    displacementPages * DisplacementPageHeaderGpu.Stride) ||
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
                    DisplacementAllocatorBytes))
                return "PRISM displacement lengths do not match counts.";

            if (!LengthIs(snapshot.MeshletVertices,
                    (long)snapshot.MeshletVertexCount *
                    ContactMeshletVertexGpu.Stride) ||
                !LengthIs(snapshot.MeshletIndices,
                    (long)snapshot.MeshletIndexCount * sizeof(uint)) ||
                !LengthIs(snapshot.MeshletDescriptors,
                    (long)snapshot.MeshletDescriptorCount *
                    ContactMeshletDescriptorGpu.Stride))
                return "PRISM derived-meshlet lengths do not match counts.";

            if (!OpaqueLengthValid(snapshot.AppearanceState) ||
                !OpaqueLengthValid(snapshot.ObservationState))
                return "PRISM appearance/observation state exceeds its bound.";
            string referenceError = ValidateReferences(snapshot.KeyframeReferences);
            if (referenceError != null) return referenceError;

            if ((snapshot.FilmCount > 0 && snapshot.FilmGeneration == 0u) ||
                (snapshot.BoundaryCount > 0 && snapshot.BoundaryGeneration == 0u) ||
                ((snapshot.DisplacementBasePageCount > 0 ||
                  snapshot.DisplacementMicroPageCount > 0) &&
                 snapshot.DisplacementGeneration == 0u) ||
                ((snapshot.MeshletVertexCount > 0 ||
                  snapshot.MeshletIndexCount > 0 ||
                  snapshot.MeshletDescriptorCount > 0) &&
                 snapshot.MeshletGeneration == 0u))
                return "PRISM live generations must be non-zero.";
            return null;
        }

        private static void ValidateCounts(PrismCanonicalChunkSnapshot snapshot)
        {
            if (snapshot.FilmCount < 0 || snapshot.FilmCount > MaximumFilms ||
                snapshot.BoundaryCount < 0 ||
                snapshot.BoundaryCount > MaximumBoundaries ||
                snapshot.ManifoldCount < 0 ||
                snapshot.ManifoldCount > MaximumManifolds ||
                snapshot.DisplacementBasePageCount < 0 ||
                snapshot.DisplacementMicroPageCount < 0 ||
                (long)snapshot.DisplacementBasePageCount +
                    snapshot.DisplacementMicroPageCount >
                    MaximumDisplacementPages ||
                snapshot.MeshletVertexCount < 0 ||
                snapshot.MeshletVertexCount > MaximumMeshletVertices ||
                snapshot.MeshletIndexCount < 0 ||
                snapshot.MeshletIndexCount > MaximumMeshletIndices ||
                snapshot.MeshletDescriptorCount < 0 ||
                snapshot.MeshletDescriptorCount > MaximumMeshletDescriptors)
                throw new InvalidDataException(
                    "PRISM canonical counts exceed limits.");

            if (snapshot.SupportContourPageCount < 0 ||
                snapshot.SupportContourPageCount > MaximumAtlasElements ||
                snapshot.SupportContourSegmentCount < 0 ||
                snapshot.SupportContourSegmentCount > MaximumAtlasElements ||
                snapshot.SurfaceHalfEdgeCount < 0 ||
                snapshot.SurfaceHalfEdgeCount > MaximumAtlasElements ||
                snapshot.FrontierLoopCount < 0 ||
                snapshot.FrontierLoopCount > MaximumAtlasElements ||
                snapshot.ContinuationEvidenceCount < 0 ||
                snapshot.ContinuationEvidenceCount > MaximumAtlasElements ||
                snapshot.CrossChunkPortalCount < 0 ||
                snapshot.CrossChunkPortalCount > MaximumAtlasElements)
                throw new InvalidDataException(
                    "PRISM topology-atlas counts exceed limits.");
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
                throw new InvalidDataException(
                    $"PRISM {section} stride changed.");
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
                throw new InvalidDataException(
                    "PRISM section length is invalid.");
            return ReadExact(reader, length);
        }

        private static byte[] ReadBoundedBytes(BinaryReader reader, int maximum,
            string label)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > maximum)
                throw new InvalidDataException(
                    $"PRISM {label} section is too large.");
            return ReadExact(reader, length);
        }

        private static byte[] ReadExact(BinaryReader reader, int length)
        {
            long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (length < 0 || remaining < length)
                throw new EndOfStreamException("PRISM section is truncated.");
            byte[] result = reader.ReadBytes(length);
            if (result.Length != length) throw new EndOfStreamException();
            return result;
        }

        private static void WriteReferences(BinaryWriter writer,
            string[] references)
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
                throw new InvalidDataException(
                    "PRISM keyframe reference count is invalid.");
            var result = new string[count];
            for (int index = 0; index < count; index++)
            {
                int length = reader.ReadInt32();
                if (length < 0 || length > MaximumReferenceUtf8Bytes)
                    throw new InvalidDataException(
                        "PRISM keyframe reference length is invalid.");
                result[index] = Encoding.UTF8.GetString(
                    ReadExact(reader, length));
            }
            return result;
        }

        private static string ValidateReferences(string[] references)
        {
            if (references == null ||
                references.Length > MaximumKeyframeReferences)
                return "PRISM keyframe references are invalid.";
            foreach (string reference in references)
            {
                if (reference == null || reference.Length == 0 ||
                    reference.IndexOf('\0') >= 0 ||
                    reference.IndexOf('\\') >= 0 ||
                    Path.IsPathRooted(reference) ||
                    Encoding.UTF8.GetByteCount(reference) >
                        MaximumReferenceUtf8Bytes)
                    return "PRISM keyframe reference is invalid.";
                string[] segments = reference.Split('/');
                foreach (string segment in segments)
                    if (segment.Length == 0 || segment == "." || segment == "..")
                        return "PRISM keyframe reference is invalid.";
            }
            return null;
        }
    }
}
