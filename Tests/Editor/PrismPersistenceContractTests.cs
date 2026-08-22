using System;
using System.IO;
using System.Reflection;
using Genesis.RoomScan.Prism;
using Genesis.RoomScan.World;
using NUnit.Framework;

namespace Genesis.RoomScan.Tests
{
    public sealed class PrismPersistenceContractTests
    {
        [Test]
        public void SchemaSixRoundTripsCanonicalAtlasWithoutFabrication()
        {
            PrismCanonicalChunkSnapshot source = PopulatedSnapshot();
            using var stream = new MemoryStream();

            Assert.That(PrismCanonicalChunkCodec.TryWrite(stream, source,
                out string writeError), Is.True, writeError);
            stream.Position = 0;
            Assert.That(PrismCanonicalChunkCodec.TryRead(stream,
                out PrismCanonicalChunkSnapshot restored, out string readError),
                Is.True, readError);

            Assert.That(restored.FilmCount, Is.EqualTo(source.FilmCount));
            Assert.That(restored.BoundaryCount, Is.EqualTo(source.BoundaryCount));
            Assert.That(restored.ManifoldCount, Is.EqualTo(source.ManifoldCount));
            Assert.That(restored.SupportContourSegmentCount,
                Is.EqualTo(source.SupportContourSegmentCount));
            Assert.That(restored.SurfaceHalfEdgeCount,
                Is.EqualTo(source.SurfaceHalfEdgeCount));
            Assert.That(restored.FrontierLoopCount,
                Is.EqualTo(source.FrontierLoopCount));
            Assert.That(restored.CalibrationEpoch,
                Is.EqualTo(source.CalibrationEpoch));
            foreach (FieldInfo field in typeof(PrismCanonicalChunkSnapshot)
                         .GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType != typeof(byte[])) continue;
                Assert.That((byte[])field.GetValue(restored),
                    Is.EqualTo((byte[])field.GetValue(source)), field.Name);
            }
            Assert.That(restored.KeyframeReferences,
                Is.EqualTo(source.KeyframeReferences));
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void RectangleEraSchemasAreRejectedInsteadOfInventingFrontiers(
            int version)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream,
                       System.Text.Encoding.UTF8, true))
            {
                writer.Write(0x33515043u);
                writer.Write(version);
                writer.Write(0x01020304u);
            }
            stream.Position = 0;

            Assert.That(PrismCanonicalChunkCodec.TryRead(stream, out _,
                out string error), Is.False);
            Assert.That(error, Does.Contain($"schema {version}"));
            Assert.That(error, Does.Contain("schema 6"));
        }

        [Test]
        public void PreAtlasVersionSixLayoutIsRejected()
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream,
                       System.Text.Encoding.UTF8, true))
            {
                writer.Write(0x33515043u);
                writer.Write(PrismCanonicalChunkCodec.FormatVersion);
                writer.Write(0x01020304u);
                writer.Write(ContactFilmHeaderGpu.Stride);
            }
            stream.Position = 0;

            Assert.That(PrismCanonicalChunkCodec.TryRead(stream, out _,
                out string error), Is.False);
            Assert.That(error, Does.Contain("predates"));
        }

        [Test]
        public void CanonicalSnapshotRejectsTrailingPayload()
        {
            PrismCanonicalChunkSnapshot source = EmptySnapshot();
            using var stream = new MemoryStream();
            Assert.That(PrismCanonicalChunkCodec.TryWrite(stream, source,
                out string writeError), Is.True, writeError);
            stream.WriteByte(0x5a);
            stream.Position = 0;

            Assert.That(PrismCanonicalChunkCodec.TryRead(stream, out _,
                out string error), Is.False);
            Assert.That(error, Does.Contain("trailing"));
        }

        [TestCase("../../outside.ktx2")]
        [TestCase("/absolute/keyframe.ktx2")]
        [TestCase("keyframes\\windows-path.ktx2")]
        public void CanonicalSnapshotRejectsUnsafeKeyframeReference(string path)
        {
            PrismCanonicalChunkSnapshot source = EmptySnapshot();
            source.KeyframeReferences = new[] { path };
            using var stream = new MemoryStream();

            Assert.That(PrismCanonicalChunkCodec.TryWrite(stream, source,
                out string error), Is.False);
            Assert.That(error, Does.Contain("reference"));
        }

        private static PrismCanonicalChunkSnapshot PopulatedSnapshot()
        {
            const int films = 2;
            const int boundaries = 1;
            const int contourPages = 1;
            const int contourSegments = 3;
            const int halfEdges = 3;
            const int loops = 2;
            const int evidence = 3;
            const int portals = 1;
            var result = EmptySnapshot();
            result.FilmCount = films;
            result.BoundaryCount = boundaries;
            result.ManifoldCount = 1;
            result.SupportContourPageCount = contourPages;
            result.SupportContourSegmentCount = contourSegments;
            result.SurfaceHalfEdgeCount = halfEdges;
            result.FrontierLoopCount = loops;
            result.ContinuationEvidenceCount = evidence;
            result.CrossChunkPortalCount = portals;
            result.DisplacementBasePageCount = 1;
            result.DisplacementMicroPageCount = 1;
            result.MeshletVertexCount = 3;
            result.MeshletIndexCount = 3;
            result.MeshletDescriptorCount = 1;
            result.FilmGeneration = 7;
            result.BoundaryGeneration = 9;
            result.DisplacementGeneration = 11;
            result.MeshletGeneration = 13;
            result.CalibrationEpoch = 0x123456789abcdef0ul;

            result.FilmHeaders = Pattern(films * ContactFilmHeaderGpu.Stride, 3);
            result.FilmInformation = Pattern(films *
                ContactFilmPool.InformationRecords * 16, 5);
            result.FilmSlotStates = Pattern(films *
                ContactFilmSlotStateGpu.Stride, 7);
            result.ActiveFilmIndices = Pattern(films * sizeof(uint), 11);
            result.DirtyFilmIndices = Pattern(films * sizeof(uint), 13);
            result.FilmAllocatorState = Pattern(8 * sizeof(uint), 17);
            result.PressureManifoldHeaders = Pattern(
                PressureManifoldHeaderGpu.Stride, 19);
            result.FilmMemberships = Pattern(films * FilmMembershipGpu.Stride, 23);
            result.ManifoldAllocatorState = Pattern(
                PressureManifoldPool.AllocatorWords * sizeof(uint), 29);
            result.CurrentManifoldState = Pattern(4 * sizeof(uint), 31);
            result.SupportContourPages = Pattern(contourPages *
                SupportContourPageGpu.Stride, 37);
            result.SupportContours = Pattern(contourSegments *
                SupportContourSegmentGpu.Stride, 41);
            result.SurfaceHalfEdges = Pattern(halfEdges *
                SurfaceHalfEdgeGpu.Stride, 43);
            result.FrontierLoops = Pattern(loops * FrontierLoopGpu.Stride, 47);
            result.ContinuationEvidence = Pattern(evidence *
                ContinuationEvidenceGpu.Stride, 53);
            result.ElasticChartStates = Pattern(films *
                ElasticChartStateGpu.Stride, 59);
            result.FilmTopologyRanges = Pattern(films * sizeof(uint) * 4, 61);
            result.AtlasAllocatorState = Pattern(16 * sizeof(uint), 67);
            result.CrossChunkTopologyPortals = Pattern(portals *
                CrossChunkTopologyPortalGpu.Stride, 71);
            result.BoundaryHeaders = Pattern(boundaries *
                ContactBoundaryHeaderGpu.Stride, 73);
            result.BoundaryInformation = Pattern(boundaries *
                ContactBoundaryPool.InformationRecordsPerBoundary * 16, 79);
            result.BoundaryCurveTopology = Pattern(boundaries *
                BoundaryCurveTopologyGpu.Stride, 83);
            result.DisplacementPageHeaders = Pattern(2 *
                DisplacementPageHeaderGpu.Stride, 89);
            result.DisplacementBaseCells = Pattern(
                ContactDisplacementPool.BaseCellsPerPage *
                DisplacementCellGpu.Stride, 97);
            result.DisplacementMicroCells = Pattern(
                ContactDisplacementPool.MicroCellsPerPage *
                DisplacementCellGpu.Stride, 101);
            result.DisplacementBaseChildren = Pattern(
                ContactDisplacementPool.BaseCellsPerPage * sizeof(uint), 103);
            result.DisplacementMicroChildren = Pattern(
                ContactDisplacementPool.MicroCellsPerPage * sizeof(uint), 107);
            result.TopologyEvidence = Pattern(films *
                ContactTopologyEvidenceGpu.Stride, 109);
            result.DisplacementAllocator = Pattern(8 * sizeof(uint), 113);
            result.MeshletVertices = Pattern(3 *
                ContactMeshletVertexGpu.Stride, 127);
            result.MeshletIndices = Pattern(3 * sizeof(uint), 131);
            result.MeshletDescriptors = Pattern(
                ContactMeshletDescriptorGpu.Stride, 137);
            result.AppearanceState = Pattern(193, 139);
            result.ObservationState = Pattern(127, 149);
            result.KeyframeReferences = new[]
            {
                "keyframes/rgb_l_0001.ktx2", "keyframes/rgb_r_0001.ktx2"
            };
            return result;
        }

        private static PrismCanonicalChunkSnapshot EmptySnapshot() => new()
        {
            FilmGeneration = 1,
            BoundaryGeneration = 1,
            DisplacementGeneration = 1,
            MeshletGeneration = 1,
            FilmAllocatorState = new byte[8 * sizeof(uint)],
            ManifoldAllocatorState = new byte[
                PressureManifoldPool.AllocatorWords * sizeof(uint)],
            CurrentManifoldState = new byte[4 * sizeof(uint)],
            AtlasAllocatorState = new byte[16 * sizeof(uint)],
            DisplacementAllocator = new byte[8 * sizeof(uint)]
        };

        private static byte[] Pattern(int count, int multiplier)
        {
            var result = new byte[count];
            for (int index = 0; index < result.Length; index++)
                result[index] = (byte)((index * multiplier + 17) & 0xff);
            return result;
        }
    }
}
