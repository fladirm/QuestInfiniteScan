using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class ChunkRefinedArtifactTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "QuestInfiniteScanTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void CodecsRoundTripMeshAtlasAndNormal()
        {
            RefinedTextureResult source = CreateRefined(true);
            using var meshStream = new MemoryStream();
            Assert.That(ChunkRefinedArtifactCodec.TryWriteMesh(meshStream, source,
                out string meshWriteError), Is.True, meshWriteError);
            meshStream.Position = 0;
            Assert.That(ChunkRefinedArtifactCodec.TryReadMesh(meshStream,
                out RefinedTextureResult restored, out string meshReadError), Is.True,
                meshReadError);
            Assert.That(restored.Positions, Is.EqualTo(source.Positions));
            Assert.That(restored.Normals, Is.EqualTo(source.Normals));
            Assert.That(restored.UVs, Is.EqualTo(source.UVs));
            Assert.That(restored.Indices, Is.EqualTo(source.Indices));
            Assert.That(restored.AtlasWidth, Is.EqualTo(2));
            Assert.That(restored.AtlasHeight, Is.EqualTo(2));

            using var atlasStream = new MemoryStream();
            Assert.That(ChunkRefinedArtifactCodec.TryWriteRgbaTexture(atlasStream,
                source.AtlasPixels, 2, 2, out string atlasWriteError), Is.True,
                atlasWriteError);
            atlasStream.Position = 0;
            Assert.That(ChunkRefinedArtifactCodec.TryReadRgbaTexture(atlasStream,
                out byte[] atlas, out int width, out int height, out string atlasReadError),
                Is.True, atlasReadError);
            Assert.That(atlas, Is.EqualTo(source.AtlasPixels));
            Assert.That((width, height), Is.EqualTo((2, 2)));
        }

        [Test]
        public void CodecsRejectCorruptCountsAndIndicesBeforePublication()
        {
            RefinedTextureResult source = CreateRefined(false);
            source.Indices[2] = 99;
            using var stream = new MemoryStream();
            Assert.That(ChunkRefinedArtifactCodec.TryWriteMesh(stream, source,
                out string error), Is.False);
            Assert.That(error, Does.Contain("index"));

            using var texture = new MemoryStream();
            Assert.That(ChunkRefinedArtifactCodec.TryWriteRgbaTexture(texture,
                new byte[15], 2, 2, out error), Is.False);
            Assert.That(error, Does.Contain("dimensions"));
        }

        [Test]
        public async Task PublisherAddsAndReplacesRefinedSetWithoutDroppingMapperArtifacts()
        {
            var store = new WorldStore(_root);
            Assert.That(WorldSessionFactory.TryCreate(store, "world-refined", "Refined",
                RigidPoseData.Identity, new BoundsData(Vector3.zero, Vector3.one), 1_000,
                out WorldManifest manifest, out string createError), Is.True, createError);
            ChunkRecord chunk = manifest.chunks[0];
            PrismChunkPublishResult mapper = await PrismChunkPublisher.PublishAsync(
                store, manifest, chunk, ChunkGlbWriterTests.CreateCanonicalSnapshot(),
                2_000);
            Assert.That(mapper.Success, Is.True, mapper.Error);

            ChunkRefinedPublishResult first = await ChunkRefinedArtifactPublisher.PublishAsync(
                store, manifest, chunk, CreateRefined(true), 3_000);
            Assert.That(first.Success, Is.True, first.Error);
            Assert.That(first.Revision, Is.EqualTo(2));
            Assert.That(chunk.artifacts.Select(artifact => artifact.kind), Is.EquivalentTo(
                new[]
                {
                    ChunkArtifactKind.PrismCanonical,
                    ChunkArtifactKind.RefinedMesh,
                    ChunkArtifactKind.RefinedAtlas,
                    ChunkArtifactKind.RefinedNormal
                }));
            AssertVerified(store, manifest.worldId, first.MeshArtifact);
            AssertVerified(store, manifest.worldId, first.AtlasArtifact);
            AssertVerified(store, manifest.worldId, first.NormalArtifact);

            ChunkRefinedPublishResult second = await ChunkRefinedArtifactPublisher.PublishAsync(
                store, manifest, chunk, CreateRefined(false), 4_000);
            Assert.That(second.Success, Is.True, second.Error);
            Assert.That(second.Revision, Is.EqualTo(3));
            Assert.That(second.NormalArtifact, Is.Null);
            Assert.That(chunk.artifacts.Count(artifact =>
                artifact.kind == ChunkArtifactKind.RefinedNormal), Is.Zero);
            Assert.That(chunk.artifacts.Count(artifact =>
                artifact.kind == ChunkArtifactKind.PrismCanonical), Is.EqualTo(1));
            Assert.That(chunk.artifacts.Count(artifact =>
                artifact.kind == ChunkArtifactKind.RefinedMesh), Is.EqualTo(1));
        }

        private static RefinedTextureResult CreateRefined(bool includeNormal)
        {
            return new RefinedTextureResult
            {
                Positions = new[]
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(0f, 1f, 0f)
                },
                Normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                UVs = new[] { Vector2.zero, Vector2.right, Vector2.up },
                Indices = new[] { 0, 1, 2 },
                AtlasWidth = 2,
                AtlasHeight = 2,
                AtlasPixels = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray(),
                NormalPixels = includeNormal
                    ? Enumerable.Repeat((byte)127, 16).ToArray()
                    : null
            };
        }

        private static void AssertVerified(WorldStore store, string worldId,
            ChunkArtifactRecord artifact)
        {
            Assert.That(store.TryResolveVerifiedArtifact(worldId, artifact, out _,
                out string error), Is.True, error);
        }
    }
}
