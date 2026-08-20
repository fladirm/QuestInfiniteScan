using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Genesis.RoomScan.HeavyCompute;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class ChunkBundleBuilderTests
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
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public async Task BuilderProducesDeterministicRealQrsProtocolBundle()
        {
            string worlds = Path.Combine(_root, "worlds");
            var worldStore = new WorldStore(worlds);
            Assert.That(WorldSessionFactory.TryCreate(worldStore, "world-bundle", "Bundle",
                RigidPoseData.Identity, new BoundsData(Vector3.zero, Vector3.one * 3f),
                1_000, out WorldManifest manifest, out string createError), Is.True,
                createError);
            string keyframes = Path.Combine(_root, "capture");
            Directory.CreateDirectory(Path.Combine(keyframes, "images"));
            File.WriteAllText(Path.Combine(keyframes, "frames.jsonl"),
                "{\"id\":0,\"ts\":1.0,\"space\":\"chunk\"," +
                "\"chunk\":\"chunk-000000\",\"revision\":0," +
                "\"px\":0.0,\"py\":0.0,\"pz\":0.0," +
                "\"qx\":0.0,\"qy\":0.0,\"qz\":0.0,\"qw\":1.0," +
                "\"fx\":32.0,\"fy\":32.0,\"cx\":16.0,\"cy\":16.0," +
                "\"sw\":32,\"sh\":32,\"w\":32,\"h\":32}\n");
            File.WriteAllBytes(Path.Combine(keyframes, "images", "000000.jpg"), Jpeg32());
            ChunkRecord chunk = manifest.chunks[0];
            ChunkSnapshotPublishResult published = await ChunkSnapshotPublisher.PublishAsync(
                worldStore, manifest, chunk, Snapshot(), 2_000, keyframes,
                ChunkLifecycleState.Persisted);
            Assert.That(published.Success, Is.True, published.Error);
            Assert.That(chunk.revision, Is.EqualTo(1));

            var queue = new HeavyComputeQueueStore(Path.Combine(_root, "queue"));
            var key = new HeavyComputeJobKey(manifest.worldId, chunk.chunkId, chunk.revision);
            string destination = queue.GetInputPath(key.JobId);
            ChunkBundleBuildResult first = ChunkBundleBuilder.Build(worldStore,
                manifest.worldId, chunk.chunkId, chunk.revision, destination);
            Assert.That(first.Success, Is.True, first.Error);
            byte[] firstBytes = File.ReadAllBytes(destination);

            using (var archive = ZipFile.OpenRead(destination))
            {
                Assert.That(archive.Entries.Select(entry => entry.FullName), Is.EquivalentTo(
                    new[] { "input.json", "mesh/live_mesh.qism",
                        "keyframes/frames.jsonl", "keyframes/images/000000.jpg" }));
                Assert.That(archive.Entries.All(entry => entry.LastWriteTime.Year == 1980),
                    Is.True);
                using var reader = new StreamReader(archive.GetEntry("input.json").Open());
                string input = reader.ReadToEnd();
                Assert.That(input, Does.Contain("\"schemaVersion\":2"));
                Assert.That(input, Does.Contain("\"coordinateSystem\":" +
                                                "\"unity-lh-y-up-z-forward\""));
                Assert.That(input, Does.Contain("\"chunkRevision\":1"));
            }

            File.Delete(destination);
            ChunkBundleBuildResult second = ChunkBundleBuilder.Build(worldStore,
                manifest.worldId, chunk.chunkId, chunk.revision, destination);
            Assert.That(second.Success, Is.True, second.Error);
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(firstBytes),
                "same immutable revision must rebuild byte-identically for idempotency");

            string fixture = Environment.GetEnvironmentVariable("QIS_UNITY_BUNDLE_FIXTURE");
            if (!string.IsNullOrEmpty(fixture))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fixture)));
                File.Copy(destination, fixture, true);
            }
        }

        private static ChunkGpuSnapshot Snapshot()
        {
            const int stride = ChunkLiveMeshSnapshot.VertexStride;
            var vertices = new byte[3 * stride];
            Vector3[] positions =
            {
                new(-0.5f, -0.5f, 2f), new(0f, 0.5f, 2f), new(0.5f, -0.5f, 2f)
            };
            for (int i = 0; i < positions.Length; i++)
            {
                WriteFloat(vertices, i * stride, positions[i].x);
                WriteFloat(vertices, i * stride + 4, positions[i].y);
                WriteFloat(vertices, i * stride + 8, positions[i].z);
                WriteFloat(vertices, i * stride + 20, -1f);
                Buffer.BlockCopy(BitConverter.GetBytes(0xFFC09060u), 0, vertices,
                    i * stride + 24, sizeof(uint));
                Buffer.BlockCopy(BitConverter.GetBytes((uint)i), 0, vertices,
                    i * stride + 28, sizeof(uint));
            }
            var indices = new byte[12];
            Buffer.BlockCopy(new uint[] { 0, 1, 2 }, 0, indices, 0, indices.Length);
            const int voxelTotal = 8;
            return new ChunkGpuSnapshot
            {
                Volume = new ChunkVolumeSnapshot
                {
                    VoxelCount = new Vector3Int(2, 2, 2),
                    VoxelSize = 0.05f,
                    IntegrationCount = 1,
                    WorldFromVolume = RigidPoseData.Identity,
                    TsdfBytes = new byte[voxelTotal * 2],
                    ColorBytes = new byte[voxelTotal * 4]
                },
                LiveMesh = new ChunkLiveMeshSnapshot
                {
                    VertexCount = 3,
                    IndexCount = 3,
                    LocalBounds = new BoundsData(new Vector3(0f, 0f, 2f), Vector3.one),
                    VertexBytes = vertices,
                    IndexBytes = indices
                }
            };
        }

        private static void WriteFloat(byte[] destination, int offset, float value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, destination, offset,
                sizeof(float));
        }

        private static byte[] Jpeg32()
        {
            return Convert.FromBase64String(
                "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoH" +
                "BwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQME" +
                "BAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQU" +
                "FBQUFBQUFBQUFBQUFBT/wAARCAAgACADASIAAhEBAxEB/8QAFQABAQAAAAAAAAAA" +
                "AAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAA" +
                "AAAAf/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCmAqqaAAAAAAP/" +
                "2Q==");
        }
    }
}
