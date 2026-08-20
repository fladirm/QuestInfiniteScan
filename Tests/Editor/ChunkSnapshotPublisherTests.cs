using System;
using System.IO;
using System.Threading.Tasks;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class ChunkSnapshotPublisherTests
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
        public async Task PublisherAtomicallyAddsVolumeAndMeshRevision()
        {
            WorldStore store = CreateWorld(out WorldManifest manifest);
            ChunkRecord chunk = manifest.chunks[0];
            ChunkGpuSnapshot snapshot = CreateSnapshot(chunk.worldFromChunk);

            ChunkSnapshotPublishResult result = await ChunkSnapshotPublisher.PublishAsync(
                store, manifest, chunk, snapshot, 2_000);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(chunk.revision, Is.EqualTo(1));
            Assert.That(manifest.revision, Is.EqualTo(1));
            Assert.That(chunk.artifacts.Count, Is.EqualTo(2));
            Assert.That(store.TryResolveVerifiedArtifact(manifest.worldId,
                result.VolumeArtifact, out _, out string volumeError), Is.True, volumeError);
            Assert.That(store.TryResolveVerifiedArtifact(manifest.worldId,
                result.LiveMeshArtifact, out _, out string meshError), Is.True, meshError);
        }

        [Test]
        public async Task PublisherRejectsWrongFrameWithoutChangingManifest()
        {
            WorldStore store = CreateWorld(out WorldManifest manifest);
            ChunkRecord chunk = manifest.chunks[0];
            ChunkGpuSnapshot snapshot = CreateSnapshot(new RigidPoseData(
                new Vector3(1f, 0f, 0f), Quaternion.identity));

            ChunkSnapshotPublishResult result = await ChunkSnapshotPublisher.PublishAsync(
                store, manifest, chunk, snapshot, 2_000);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("frame"));
            Assert.That(chunk.revision, Is.Zero);
            Assert.That(manifest.revision, Is.Zero);
            Assert.That(chunk.artifacts, Is.Empty);
        }

        [Test]
        public async Task PublisherIncludesKeyframesInSameAtomicRevision()
        {
            WorldStore store = CreateWorld(out WorldManifest manifest);
            ChunkRecord chunk = manifest.chunks[0];
            string keyframes = Path.Combine(_root, "capture");
            Directory.CreateDirectory(Path.Combine(keyframes, "images"));
            File.WriteAllText(Path.Combine(keyframes, "frames.jsonl"), "{\"id\":0}\n");
            File.WriteAllBytes(Path.Combine(keyframes, "images", "000000.jpg"),
                new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

            ChunkSnapshotPublishResult result = await ChunkSnapshotPublisher.PublishAsync(
                store, manifest, chunk, CreateSnapshot(chunk.worldFromChunk), 2_000,
                keyframes);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.KeyframesArtifact, Is.Not.Null);
            Assert.That(chunk.artifacts.Count, Is.EqualTo(3));
            Assert.That(store.TryResolveVerifiedArtifact(manifest.worldId,
                result.KeyframesArtifact, out _, out string error), Is.True, error);
            Assert.That(result.KeyframesArtifact.chunkRevision, Is.EqualTo(chunk.revision));
        }

        [Test]
        public async Task RevisitReplacesKeyframeArtifactWithMonotonicRevision()
        {
            WorldStore store = CreateWorld(out WorldManifest manifest);
            ChunkRecord chunk = manifest.chunks[0];
            string keyframes = Path.Combine(_root, "revisit");
            string images = Path.Combine(keyframes, "images");
            Directory.CreateDirectory(images);
            File.WriteAllText(Path.Combine(keyframes, "frames.jsonl"), "{\"id\":0}\n");
            File.WriteAllBytes(Path.Combine(images, "000000.jpg"),
                new byte[] { 0xFF, 0xD8, 0, 0xFF, 0xD9 });
            ChunkGpuSnapshot snapshot = CreateSnapshot(chunk.worldFromChunk);

            ChunkSnapshotPublishResult first = await ChunkSnapshotPublisher.PublishAsync(
                store, manifest, chunk, snapshot, 2_000, keyframes);
            Assert.That(first.Success, Is.True, first.Error);
            File.AppendAllText(Path.Combine(keyframes, "frames.jsonl"), "{\"id\":1}\n");
            File.WriteAllBytes(Path.Combine(images, "000001.jpg"),
                new byte[] { 0xFF, 0xD8, 1, 0xFF, 0xD9 });

            ChunkSnapshotPublishResult second = await ChunkSnapshotPublisher.PublishAsync(
                store, manifest, chunk, snapshot, 3_000, keyframes);

            Assert.That(second.Success, Is.True, second.Error);
            Assert.That(second.Revision, Is.EqualTo(2));
            Assert.That(second.KeyframesArtifact.chunkRevision, Is.EqualTo(2));
            Assert.That(second.KeyframesArtifact.relativePath,
                Is.Not.EqualTo(first.KeyframesArtifact.relativePath));
            Assert.That(chunk.artifacts.FindAll(a => a.kind == ChunkArtifactKind.Keyframes),
                Has.Count.EqualTo(1));
            Assert.That(chunk.artifacts, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task BackgroundPublicationFinishesFinalizingSourceAsPersisted()
        {
            WorldStore store = CreateWorld(out WorldManifest manifest);
            ChunkRecord chunk = manifest.chunks[0];
            chunk.state = ChunkLifecycleState.Finalizing;

            ChunkSnapshotPublishResult result = await ChunkSnapshotPublisher.PublishAsync(
                store, manifest, chunk, CreateSnapshot(chunk.worldFromChunk), 2_000,
                null, ChunkLifecycleState.Persisted);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(chunk.state, Is.EqualTo(ChunkLifecycleState.Persisted));
            Assert.That(chunk.revision, Is.EqualTo(1));
        }

        private WorldStore CreateWorld(out WorldManifest manifest)
        {
            var store = new WorldStore(_root);
            Assert.That(WorldSessionFactory.TryCreate(store, "world-publisher", "Publisher",
                RigidPoseData.Identity,
                new BoundsData(Vector3.zero, Vector3.one * 2f), 1_000,
                out manifest, out string error), Is.True, error);
            return store;
        }

        private static ChunkGpuSnapshot CreateSnapshot(RigidPoseData pose)
        {
            const int voxels = 2 * 2 * 2;
            var vertices = new byte[3 * ChunkLiveMeshSnapshot.VertexStride];
            for (int i = 0; i < 3; i++)
            {
                Buffer.BlockCopy(BitConverter.GetBytes((float)i), 0, vertices,
                    i * ChunkLiveMeshSnapshot.VertexStride, sizeof(float));
                Buffer.BlockCopy(BitConverter.GetBytes(1f), 0, vertices,
                    i * ChunkLiveMeshSnapshot.VertexStride + 16, sizeof(float));
                Buffer.BlockCopy(BitConverter.GetBytes(0xFFFFFFFFu), 0, vertices,
                    i * ChunkLiveMeshSnapshot.VertexStride + 24, sizeof(uint));
            }
            var indices = new byte[3 * sizeof(uint)];
            Buffer.BlockCopy(new[] { 0, 1, 2 }, 0, indices, 0, indices.Length);
            return new ChunkGpuSnapshot
            {
                Volume = new ChunkVolumeSnapshot
                {
                    VoxelCount = new Vector3Int(2, 2, 2),
                    VoxelSize = 0.05f,
                    IntegrationCount = 10,
                    WorldFromVolume = pose,
                    TsdfBytes = new byte[voxels * 2],
                    ColorBytes = new byte[voxels * 4]
                },
                LiveMesh = new ChunkLiveMeshSnapshot
                {
                    VertexCount = 3,
                    IndexCount = 3,
                    LocalBounds = new BoundsData(Vector3.zero, Vector3.one),
                    VertexBytes = vertices,
                    IndexBytes = indices
                }
            };
        }
    }
}
