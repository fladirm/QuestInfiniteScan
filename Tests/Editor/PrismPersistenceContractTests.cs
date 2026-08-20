using System.IO;
using System.Threading.Tasks;
using Genesis.RoomScan.Prism;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class PrismPersistenceContractTests
    {
        [Test]
        public void RequiredQ313ChunkStageKernelsImport()
        {
            ComputeShader stage = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/ChunkStage.compute");
            Assert.That(stage, Is.Not.Null);
            foreach (string kernel in new[]
                     {
                         "PrepareChunkStage", "ClearFilmRemap", "StageFilms",
                         "StageBoundaries", "ClearPageRemap", "IndexBasePages",
                         "IndexMicroPages", "CopyBasePages", "CopyMicroPages",
                         "PatchFilmDisplacement", "StageMeshlets",
                         "FinalizeChunkStage"
                     })
                Assert.DoesNotThrow(() => stage.FindKernel(kernel), kernel);

            ComputeShader boundaries = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "ContactBoundaryUpdate.compute");
            Assert.That(boundaries, Is.Not.Null);
            Assert.DoesNotThrow(() =>
                boundaries.FindKernel("ClearLoadedBoundaryHash"));
            Assert.DoesNotThrow(() =>
                boundaries.FindKernel("RehashLoadedBoundaries"));
            Shader preview = AssetDatabase.LoadAssetAtPath<Shader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "ContactFilmPreview.shader");
            Assert.That(preview, Is.Not.Null);
        }

        [Test]
        public void CanonicalSnapshotRoundTripsExactPosteriorBytes()
        {
            var source = new PrismCanonicalChunkSnapshot
            {
                FilmCount = 2,
                BoundaryCount = 1,
                FilmGeneration = 7,
                BoundaryGeneration = 9,
                CalibrationEpoch = 123456789,
                FilmHeaders = Pattern(2 * ContactFilmHeaderGpu.Stride, 3),
                FilmInformation = Pattern(2 * 9 * 16, 5),
                BoundaryHeaders = Pattern(ContactBoundaryHeaderGpu.Stride, 7),
                BoundaryInformation = Pattern(
                    ContactBoundaryPool.InformationRecordsPerBoundary * 16, 11),
                DisplacementBasePageCount = 1,
                DisplacementMicroPageCount = 1,
                DisplacementGeneration = 13,
                DisplacementPageHeaders = Pattern(
                    2 * DisplacementPageHeaderGpu.Stride, 13),
                DisplacementBaseCells = Pattern(
                    ContactDisplacementPool.BaseCellsPerPage *
                    DisplacementCellGpu.Stride, 17),
                DisplacementMicroCells = Pattern(
                    ContactDisplacementPool.MicroCellsPerPage *
                    DisplacementCellGpu.Stride, 19),
                DisplacementBaseChildren = Pattern(
                    ContactDisplacementPool.BaseCellsPerPage * sizeof(uint), 23),
                DisplacementMicroChildren = Pattern(
                    ContactDisplacementPool.MicroCellsPerPage * sizeof(uint), 29),
                TopologyEvidence = Pattern(
                    2 * ContactTopologyEvidenceGpu.Stride, 31),
                DisplacementAllocator = Pattern(8 * sizeof(uint), 37),
                MeshletVertexCount = 3,
                MeshletIndexCount = 3,
                MeshletDescriptorCount = 1,
                MeshletGeneration = 15,
                MeshletVertices = Pattern(3 * ContactMeshletVertexGpu.Stride, 41),
                MeshletIndices = Pattern(3 * sizeof(uint), 43),
                MeshletDescriptors = Pattern(
                    ContactMeshletDescriptorGpu.Stride, 47),
                AppearanceState = Pattern(193, 53),
                ObservationState = Pattern(127, 59),
                KeyframeReferences = new[]
                {
                    "keyframes/rgb_l_0001.ktx2", "keyframes/rgb_r_0001.ktx2"
                }
            };
            using var stream = new MemoryStream();

            Assert.That(PrismCanonicalChunkCodec.TryWrite(stream, source,
                out string writeError), Is.True, writeError);
            stream.Position = 0;
            Assert.That(PrismCanonicalChunkCodec.TryRead(stream, out var restored,
                out string readError), Is.True, readError);
            Assert.That(restored.FilmCount, Is.EqualTo(source.FilmCount));
            Assert.That(restored.BoundaryCount, Is.EqualTo(source.BoundaryCount));
            Assert.That(restored.CalibrationEpoch, Is.EqualTo(source.CalibrationEpoch));
            Assert.That(restored.FilmHeaders, Is.EqualTo(source.FilmHeaders));
            Assert.That(restored.FilmInformation, Is.EqualTo(source.FilmInformation));
            Assert.That(restored.BoundaryHeaders, Is.EqualTo(source.BoundaryHeaders));
            Assert.That(restored.BoundaryInformation,
                Is.EqualTo(source.BoundaryInformation));
            Assert.That(restored.DisplacementPageHeaders,
                Is.EqualTo(source.DisplacementPageHeaders));
            Assert.That(restored.DisplacementBaseCells,
                Is.EqualTo(source.DisplacementBaseCells));
            Assert.That(restored.DisplacementMicroCells,
                Is.EqualTo(source.DisplacementMicroCells));
            Assert.That(restored.TopologyEvidence,
                Is.EqualTo(source.TopologyEvidence));
            Assert.That(restored.MeshletVertices,
                Is.EqualTo(source.MeshletVertices));
            Assert.That(restored.MeshletDescriptors,
                Is.EqualTo(source.MeshletDescriptors));
            Assert.That(restored.AppearanceState,
                Is.EqualTo(source.AppearanceState));
            Assert.That(restored.ObservationState,
                Is.EqualTo(source.ObservationState));
            Assert.That(restored.KeyframeReferences,
                Is.EqualTo(source.KeyframeReferences));
        }

        [Test]
        public async Task InterruptedReplacementPreservesPriorDurableRevision()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "ConePrismPersistence", System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store = new WorldStore(root);
                Assert.That(WorldSessionFactory.TryCreate(store, "prism-world",
                    "PRISM", RigidPoseData.Identity,
                    new BoundsData(Vector3.zero, Vector3.one), 1_000,
                    out WorldManifest manifest, out string createError), Is.True,
                    createError);
                ChunkRecord chunk = manifest.chunks[0];
                PrismCanonicalChunkSnapshot valid = EmptyValidSnapshot();
                PrismChunkPublishResult first = await PrismChunkPublisher.PublishAsync(
                    store, manifest, chunk, valid, 2_000);
                Assert.That(first.Success, Is.True, first.Error);
                int durableRevision = chunk.revision;
                ChunkArtifactRecord durableArtifact = first.CanonicalArtifact;

                PrismCanonicalChunkSnapshot torn = EmptyValidSnapshot();
                torn.FilmCount = 1;
                PrismChunkPublishResult rejected = await
                    PrismChunkPublisher.PublishAsync(store, manifest, chunk, torn,
                        3_000);

                Assert.That(rejected.Success, Is.False);
                Assert.That(chunk.revision, Is.EqualTo(durableRevision));
                Assert.That(store.TryResolveVerifiedArtifact(manifest.worldId,
                    durableArtifact, out _, out string verifyError), Is.True,
                    verifyError);
                chunk.state = ChunkLifecycleState.Persisted;
                PrismChunkPublishResult revisit = await
                    PrismChunkPublisher.PublishAsync(store, manifest, chunk, valid,
                        4_000);
                Assert.That(revisit.Success, Is.True, revisit.Error);
                Assert.That(revisit.Revision, Is.EqualTo(durableRevision + 1));
                Assert.That(chunk.artifacts.FindAll(artifact =>
                    artifact.kind == ChunkArtifactKind.PrismCanonical),
                    Has.Count.EqualTo(1));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void ChunkGpuIdentityIsStableAndNonZero()
        {
            Assert.That(PrismChunkIdentity.ToNumericId("chunk-000000"), Is.EqualTo(1u));
            Assert.That(PrismChunkIdentity.ToNumericId("chunk-000019"), Is.EqualTo(20u));
            Assert.That(PrismChunkIdentity.ToNumericId("custom-wing-a"), Is.Not.Zero);
            Assert.That(PrismChunkIdentity.ToNumericId("custom-wing-a"),
                Is.EqualTo(PrismChunkIdentity.ToNumericId("custom-wing-a")));
        }

        [Test]
        public void CanonicalSnapshotRejectsTrailingOrMismatchedPayload()
        {
            var source = new PrismCanonicalChunkSnapshot
            {
                FilmCount = 1,
                BoundaryCount = 0,
                FilmGeneration = 1,
                BoundaryGeneration = 1,
                FilmHeaders = new byte[ContactFilmHeaderGpu.Stride],
                FilmInformation = new byte[9 * 16],
                TopologyEvidence = new byte[ContactTopologyEvidenceGpu.Stride],
                DisplacementAllocator = new byte[8 * sizeof(uint)]
            };
            using var stream = new MemoryStream();
            Assert.That(PrismCanonicalChunkCodec.TryWrite(stream, source, out _), Is.True);
            stream.WriteByte(0x5a);
            stream.Position = 0;

            Assert.That(PrismCanonicalChunkCodec.TryRead(stream, out _,
                out string error), Is.False);
            Assert.That(error, Does.Contain("trailing"));
        }

        [Test]
        public void VersionTwoArtifactUpgradesMissingHierarchyToEmptyState()
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8,
                       true))
            {
                writer.Write(0x33515043u);
                writer.Write(2);
                writer.Write(0x01020304u);
                writer.Write(ContactFilmHeaderGpu.Stride);
                writer.Write(ContactBoundaryHeaderGpu.Stride);
                writer.Write(1);
                writer.Write(0);
                writer.Write(3u);
                writer.Write(1u);
                writer.Write(99ul);
                WriteSection(writer, Pattern(ContactFilmHeaderGpu.Stride, 5));
                WriteSection(writer, Pattern(9 * 16, 7));
                WriteSection(writer, System.Array.Empty<byte>());
                WriteSection(writer, System.Array.Empty<byte>());
            }
            stream.Position = 0;

            Assert.That(PrismCanonicalChunkCodec.TryRead(stream, out var restored,
                out string error), Is.True, error);
            Assert.That(restored.FilmCount, Is.EqualTo(1));
            Assert.That(restored.DisplacementBasePageCount, Is.Zero);
            Assert.That(restored.MeshletDescriptorCount, Is.Zero);
            Assert.That(restored.AppearanceState, Is.Empty);
        }

        [Test]
        public void CanonicalSnapshotRejectsUnsafeKeyframeReference()
        {
            var source = EmptyValidSnapshot();
            source.KeyframeReferences = new[] { "../../outside.jpg" };
            using var stream = new MemoryStream();

            Assert.That(PrismCanonicalChunkCodec.TryWrite(stream, source,
                out string error), Is.False);
            Assert.That(error, Does.Contain("reference"));
        }

        private static PrismCanonicalChunkSnapshot EmptyValidSnapshot() => new()
        {
            FilmGeneration = 1,
            BoundaryGeneration = 1,
            DisplacementGeneration = 1,
            MeshletGeneration = 1,
            DisplacementAllocator = new byte[8 * sizeof(uint)]
        };

        private static void WriteSection(BinaryWriter writer, byte[] bytes)
        {
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static byte[] Pattern(int count, int multiplier)
        {
            var bytes = new byte[count];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)((i * multiplier + 17) & 0xff);
            return bytes;
        }
    }
}
