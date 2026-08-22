using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.Exporting;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class WorldGlbExporterTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "QuestInfiniteScanWorldGlbTests",
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
        public void MonolithicWriterStreamsChunkBinsAndAppliesPoseExactlyOnce()
        {
            string firstPath = Path.Combine(_root, "first.glb");
            string secondPath = Path.Combine(_root, "second.glb");
            ChunkGlbWriteResult first = WriteChunk(firstPath, "first");
            ChunkGlbWriteResult second = WriteChunk(secondPath, "second");
            var secondPose = new RigidPoseData(new Vector3(2f, 3f, 4f),
                Quaternion.Euler(0f, 90f, 0f));
            var inputs = new[]
            {
                new WorldGlbChunkInput
                {
                    ChunkId = "chunk-000000", Revision = 4,
                    WorldFromChunk = RigidPoseData.Identity,
                    GlbPath = firstPath, ChunkLayout = first
                },
                new WorldGlbChunkInput
                {
                    ChunkId = "chunk-000001", Revision = 7,
                    WorldFromChunk = secondPose,
                    GlbPath = secondPath, ChunkLayout = second
                }
            };

            using var destination = new MemoryStream();
            Assert.That(WorldGlbWriter.TryWrite(destination, inputs,
                new WorldGlbWriteOptions(), out WorldGlbWriteResult result,
                out string error), Is.True, error);
            byte[] world = destination.ToArray();
            string json = ReadJson(world, out byte[] binary);
            Assert.That(result.ChunkCount, Is.EqualTo(2));
            Assert.That(result.PeakCopyBufferBytes, Is.EqualTo(1024 * 1024));
            Assert.That(result.ByteLength, Is.EqualTo(world.Length));
            Assert.That(json, Does.Contain("chunk-000000_r0000000004"));
            Assert.That(json, Does.Contain("chunk-000001_r0000000007"));
            Assert.That(json, Does.Contain("\"mesh\":0"));
            Assert.That(json, Does.Contain("\"mesh\":1"));

            Matrix4x4 expected = WorldGlbWriter.ToGltfMatrix(secondPose);
            Assert.That(json, Does.Contain("\"matrix\":[" + MatrixJson(expected) + "]"));
            // The local geometry is already converted by each chunk writer. Its bytes are
            // concatenated unchanged; only the node receives worldFromChunk.
            byte[] firstBin = ReadBinary(File.ReadAllBytes(firstPath));
            byte[] secondBin = ReadBinary(File.ReadAllBytes(secondPath));
            Assert.That(binary.Take(firstBin.Length).ToArray(), Is.EqualTo(firstBin));
            Assert.That(binary.Skip(firstBin.Length).Take(secondBin.Length).ToArray(),
                Is.EqualTo(secondBin));

            using var bounded = new MemoryStream();
            Assert.That(WorldGlbWriter.TryWrite(bounded, inputs,
                new WorldGlbWriteOptions { MaximumByteLength = result.ByteLength - 1 },
                out _, out error), Is.False);
            Assert.That(error, Does.Contain("building.json"));
            Assert.That(bounded.Length, Is.Zero);
        }

        [Test]
        public async Task ShardedExporterCommitsManifestChunksAndBoundedFallbackAtomically()
        {
            var store = new WorldStore(Path.Combine(_root, "store"));
            Assert.That(WorldSessionFactory.TryCreate(store, "world-export", "Export",
                RigidPoseData.Identity, new BoundsData(Vector3.zero, Vector3.one), 1_000,
                out WorldManifest manifest, out string createError), Is.True, createError);
            ChunkRecord chunk = manifest.chunks[0];
            PrismChunkPublishResult snapshot = await PrismChunkPublisher.PublishAsync(
                store, manifest, chunk,
                ChunkGlbWriterTests.CreateCanonicalSnapshot(), 2_000);
            Assert.That(snapshot.Success, Is.True, snapshot.Error);
            ChunkRefinedPublishResult refined = await ChunkRefinedArtifactPublisher.PublishAsync(
                store, manifest, chunk, ChunkGlbWriterTests.CreateRefined(), 3_000);
            Assert.That(refined.Success, Is.True, refined.Error);

            string output = Path.Combine(_root, "building-sharded");
            WorldGlbExportResult exported = await WorldGlbExporter.ExportAsync(store,
                manifest, output, new WorldGlbExportOptions
                {
                    WriteMonolithicGlb = true,
                    MaximumMonolithicByteLength = 64
                }, 4_000);
            Assert.That(exported.Success, Is.True, exported.Error);
            Assert.That(exported.ChunkCount, Is.EqualTo(1));
            Assert.That(exported.ShardedByteLength, Is.GreaterThan(0));
            Assert.That(exported.MonolithicGlbPath, Is.Null);
            Assert.That(exported.MonolithicError, Does.Contain("building.json"));
            Assert.That(File.Exists(exported.BuildingManifestPath), Is.True);
            Assert.That(File.Exists(Path.Combine(output, "chunks",
                "chunk-000000_r" + chunk.revision.ToString("D10") + ".glb")), Is.True);
            Assert.That(Directory.GetDirectories(_root, ".building-sharded.pending-*").Length,
                Is.Zero);

            string building = File.ReadAllText(exported.BuildingManifestPath, Encoding.UTF8);
            Assert.That(building, Does.Contain("\"schemaVersion\":1"));
            Assert.That(building, Does.Contain("\"worldId\":\"world-export\""));
            Assert.That(building, Does.Contain("\"transformConvention\":" +
                                              "\"worldFromChunk-applied-once\""));
            Assert.That(building, Does.Contain("\"compression\":{" +
                "\"meshopt\":false,\"ktx2\":false,\"extensionsUsed\":[]}"));
            Assert.That(building, Does.Contain("\"monolithic\":null"));
            Assert.That(building, Does.Contain("\"sha256\":"));

            WorldGlbExportResult duplicate = await WorldGlbExporter.ExportAsync(store,
                manifest, output, new WorldGlbExportOptions(), 5_000);
            Assert.That(duplicate.Success, Is.False);
            Assert.That(duplicate.Error, Does.Contain("already exists"));

            string canceledPath = Path.Combine(_root, "canceled");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            WorldGlbExportResult canceled = await WorldGlbExporter.ExportAsync(store,
                manifest, canceledPath, new WorldGlbExportOptions(), 6_000,
                cancellation.Token);
            Assert.That(canceled.Success, Is.False);
            Assert.That(canceled.Error, Does.Contain("canceled"));
            Assert.That(Directory.Exists(canceledPath), Is.False);
        }

        [Test]
        public void CompressionNegotiationRequiresProbeAndConsumerDeclaration()
        {
            GlbCompressionSelection baseline = GlbCompressionNegotiator.Negotiate(
                new GlbCompressionRequest
                {
                    Meshopt = GlbCompressionRequirement.Prefer,
                    Ktx2 = GlbCompressionRequirement.Prefer,
                    ConsumerExtensions = new[]
                    {
                        GlbCompressionNegotiator.MeshoptExtension,
                        GlbCompressionNegotiator.Ktx2Extension
                    }
                }, GlbCompressionRuntimeCapabilities.BaselineOnly);
            Assert.That(baseline.Success, Is.True);
            Assert.That(baseline.UseMeshopt, Is.False);
            Assert.That(baseline.UseKtx2, Is.False);
            Assert.That(baseline.FallbackReason, Does.Contain("not verified"));

            var verified = new GlbCompressionRuntimeCapabilities
            {
                MeshoptEncoderVerified = true,
                MeshoptImplementationId = "meshoptimizer-test-probe",
                Ktx2EncoderVerified = true,
                Ktx2ImplementationId = "basisu-test-probe"
            };
            GlbCompressionSelection selected = GlbCompressionNegotiator.Negotiate(
                new GlbCompressionRequest
                {
                    Meshopt = GlbCompressionRequirement.Require,
                    Ktx2 = GlbCompressionRequirement.Require,
                    ConsumerExtensions = new[]
                    {
                        GlbCompressionNegotiator.MeshoptExtension,
                        GlbCompressionNegotiator.Ktx2Extension
                    }
                }, verified);
            Assert.That(selected.Success, Is.True, selected.Error);
            Assert.That(selected.UseMeshopt, Is.True);
            Assert.That(selected.UseKtx2, Is.True);

            GlbCompressionSelection undeclared = GlbCompressionNegotiator.Negotiate(
                new GlbCompressionRequest
                {
                    Meshopt = GlbCompressionRequirement.Require,
                    ConsumerExtensions = Array.Empty<string>()
                }, verified);
            Assert.That(undeclared.Success, Is.False);
            Assert.That(undeclared.Error, Does.Contain("consumer did not declare"));

            verified.MeshoptImplementationId = null;
            GlbCompressionSelection unidentifiable = GlbCompressionNegotiator.Negotiate(
                new GlbCompressionRequest
                {
                    Meshopt = GlbCompressionRequirement.Require,
                    ConsumerExtensions = new[] { GlbCompressionNegotiator.MeshoptExtension }
                }, verified);
            Assert.That(unidentifiable.Success, Is.False);
            Assert.That(unidentifiable.Error, Does.Contain("not verified"));
        }

        private ChunkGlbWriteResult WriteChunk(string path, string name)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            Assert.That(ChunkGlbWriter.TryWrite(stream,
                ChunkGlbWriterTests.CreateExportData(name), new ChunkGlbWriteOptions(),
                out ChunkGlbWriteResult result, out string error), Is.True, error);
            return result;
        }

        private static string ReadJson(byte[] glb, out byte[] binary)
        {
            int jsonLength = checked((int)BitConverter.ToUInt32(glb, 12));
            string json = Encoding.UTF8.GetString(glb, 20, jsonLength).TrimEnd(' ');
            int binaryHeader = checked(20 + jsonLength);
            int binaryLength = checked((int)BitConverter.ToUInt32(glb, binaryHeader));
            binary = new byte[binaryLength];
            Buffer.BlockCopy(glb, binaryHeader + 8, binary, 0, binaryLength);
            return json;
        }

        private static byte[] ReadBinary(byte[] glb)
        {
            ReadJson(glb, out byte[] binary);
            return binary;
        }

        private static string MatrixJson(Matrix4x4 matrix)
        {
            float[] values =
            {
                matrix.m00, matrix.m10, matrix.m20, matrix.m30,
                matrix.m01, matrix.m11, matrix.m21, matrix.m31,
                matrix.m02, matrix.m12, matrix.m22, matrix.m32,
                matrix.m03, matrix.m13, matrix.m23, matrix.m33
            };
            return string.Join(",", values.Select(ChunkGlbWriter.JsonFloat));
        }
    }
}
