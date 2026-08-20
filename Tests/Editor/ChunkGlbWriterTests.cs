using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.Exporting;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class ChunkGlbWriterTests
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
        public void WriterProducesGoldenGlbLayoutCoordinatesTexturesAndHonestPbr()
        {
            ChunkGlbExportData source = CreateExportData("chunk_\"ž\"");
            var options = new ChunkGlbWriteOptions
            {
                RoughnessFactor = 0.73f,
                NormalScale = 0.5f,
                DoubleSided = true
            };
            using var stream = new MemoryStream();
            Assert.That(ChunkGlbWriter.TryWrite(stream, source, options,
                out ChunkGlbWriteResult result, out string error), Is.True, error);
            byte[] glb = stream.ToArray();

            GlbFixture fixture = ParseGlb(glb);
            Assert.That(ReadUInt32Little(glb, 0), Is.EqualTo(0x46546C67u));
            Assert.That(ReadUInt32Little(glb, 4), Is.EqualTo(2u));
            Assert.That(ReadUInt32Little(glb, 8), Is.EqualTo((uint)glb.Length));
            Assert.That(result.ByteLength, Is.EqualTo(glb.Length));
            Assert.That(result.JsonChunkLength % 4, Is.Zero);
            Assert.That(result.BinaryChunkLength % 4, Is.Zero);
            Assert.That(fixture.Json, Does.Contain("\"version\":\"2.0\""));
            Assert.That(fixture.Json, Does.Contain("chunk_\\\"\\u017e\\\""));
            Assert.That(fixture.Json, Does.Contain("\"POSITION\":0"));
            Assert.That(fixture.Json, Does.Contain("\"NORMAL\":1"));
            Assert.That(fixture.Json, Does.Contain("\"TANGENT\":2"));
            Assert.That(fixture.Json, Does.Contain("\"TEXCOORD_0\":3"));
            Assert.That(fixture.Json, Does.Contain("\"metallicFactor\":0"));
            Assert.That(fixture.Json, Does.Contain("\"roughnessFactor\":0.73"));
            Assert.That(fixture.Json, Does.Contain(
                "\"normalTexture\":{\"index\":1,\"scale\":0.5}"));
            Assert.That(fixture.Json, Does.Contain("\"doubleSided\":true"));
            Assert.That(fixture.Json, Does.Not.Contain("metallicRoughnessTexture"));
            Assert.That(fixture.Json, Does.Not.Contain("occlusionTexture"));

            const int positionOffset = 0;
            const int normalOffset = 36;
            const int tangentOffset = 72;
            const int uvOffset = 120;
            const int indexOffset = 144;
            const int basePngOffset = 156;
            const int pngLength = 86;
            const int normalPngOffset = 244;
            const int binaryLength = 332;
            Assert.That(fixture.Binary.Length, Is.EqualTo(binaryLength));
            Assert.That(fixture.Json, Does.Contain(
                "{\"buffer\":0,\"byteOffset\":156,\"byteLength\":86}"));
            Assert.That(fixture.Json, Does.Contain(
                "{\"buffer\":0,\"byteOffset\":244,\"byteLength\":86}"));
            Assert.That(fixture.Json, Does.Contain("\"byteLength\":332"));

            AssertVector3(fixture.Binary, positionOffset, new Vector3(-1f, 2f, 3f));
            AssertVector3(fixture.Binary, positionOffset + 12, new Vector3(-2f, 2f, 3f));
            AssertVector3(fixture.Binary, positionOffset + 24, new Vector3(-1f, 3f, 3f));
            AssertVector3(fixture.Binary, normalOffset, Vector3.forward);
            AssertVector4(fixture.Binary, tangentOffset, new Vector4(-1f, 0f, 0f, -1f));
            AssertVector2(fixture.Binary, uvOffset, Vector2.zero);
            AssertVector2(fixture.Binary, uvOffset + 8, Vector2.right);
            AssertVector2(fixture.Binary, uvOffset + 16, Vector2.up);
            Assert.That(ReadUInt32Little(fixture.Binary, indexOffset), Is.EqualTo(0u));
            Assert.That(ReadUInt32Little(fixture.Binary, indexOffset + 4), Is.EqualTo(2u));
            Assert.That(ReadUInt32Little(fixture.Binary, indexOffset + 8), Is.EqualTo(1u));

            byte[] basePng = fixture.Binary.Skip(basePngOffset).Take(pngLength).ToArray();
            byte[] normalPng = fixture.Binary.Skip(normalPngOffset).Take(pngLength).ToArray();
            Assert.That(DecodeStoredRgbaPng(basePng, out int baseWidth,
                out int baseHeight), Is.EqualTo(source.BaseColorRgba32));
            Assert.That((baseWidth, baseHeight), Is.EqualTo((2, 2)));
            Assert.That(DecodeStoredRgbaPng(normalPng, out int normalWidth,
                out int normalHeight), Is.EqualTo(source.NormalRgba32));
            Assert.That((normalWidth, normalHeight), Is.EqualTo((2, 2)));

            // The first source row (v=0) is the first encoded PNG row. glTF defines
            // (0,0) at that first row, so UVs and both aligned maps stay unflipped.
            CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 },
                source.BaseColorRgba32.Take(8).ToArray());
            string digest = Sha256(glb);
            TestContext.WriteLine("G01 golden SHA-256: " + digest);
            Assert.That(digest, Is.EqualTo(
                "e680b118e41ce8332d8f95c9dc5bc14ca00ec35bfd3c2f953c9baf624c9f8e28"));
        }

        [Test]
        public void WriterIsDeterministicAndRejectsInvalidInputsBeforeWriting()
        {
            ChunkGlbExportData source = CreateExportData("deterministic");
            byte[] first = Write(source, new ChunkGlbWriteOptions());
            byte[] second = Write(source, new ChunkGlbWriteOptions());
            Assert.That(second, Is.EqualTo(first));

            using var invalid = new MemoryStream();
            source.Normals[0] = Vector3.zero;
            Assert.That(ChunkGlbWriter.TryWrite(invalid, source,
                new ChunkGlbWriteOptions(), out _, out string error), Is.False);
            Assert.That(error, Does.Contain("zero-length normal"));
            Assert.That(invalid.Length, Is.Zero);

            source = CreateExportData("missing-normal-map");
            source.NormalRgba32 = null;
            Assert.That(ChunkGlbWriter.TryWrite(invalid, source,
                new ChunkGlbWriteOptions(), out _, out error), Is.False);
            Assert.That(error, Does.Contain("Normal RGBA8"));
            Assert.That(invalid.Length, Is.Zero);

            source = CreateExportData("bad-index");
            source.Indices[2] = 3;
            Assert.That(ChunkGlbWriter.TryWrite(invalid, source,
                new ChunkGlbWriteOptions(), out _, out error), Is.False);
            Assert.That(error, Does.Contain("out-of-range index"));
            Assert.That(invalid.Length, Is.Zero);

            source = CreateExportData("bad-material");
            Assert.That(ChunkGlbWriter.TryWrite(invalid, source,
                new ChunkGlbWriteOptions { RoughnessFactor = float.NaN },
                out _, out error), Is.False);
            Assert.That(error, Does.Contain("roughnessFactor"));
            Assert.That(invalid.Length, Is.Zero);

            using var canceled = new MemoryStream();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(ChunkGlbWriter.TryWrite(canceled, source,
                new ChunkGlbWriteOptions(), out _, out error, cancellation.Token), Is.False);
            Assert.That(error, Does.Contain("canceled"));
            Assert.That(canceled.Length, Is.Zero);
        }

        [Test]
        public void PngWriterCrossesStoredBlocksWithoutChangingRowOrder()
        {
            const int width = 257;
            const int height = 70;
            byte[] rgba = new byte[width * height * 4];
            for (int i = 0; i < rgba.Length; i++)
                rgba[i] = (byte)((i * 29 + i / 17) & 0xFF);
            using var stream = new MemoryStream();
            Assert.That(DeterministicPngWriter.TryWriteRgba8(stream, rgba, width, height,
                default, out long written, out string error), Is.True, error);
            Assert.That(written, Is.EqualTo(stream.Length));
            byte[] restored = DecodeStoredRgbaPng(stream.ToArray(), out int decodedWidth,
                out int decodedHeight);
            Assert.That((decodedWidth, decodedHeight), Is.EqualTo((width, height)));
            Assert.That(restored, Is.EqualTo(rgba));
        }

        [Test]
        public async Task RefinedExporterPublishesIdempotentlyAndPreservesLastGoodOnConflict()
        {
            var store = new WorldStore(_root);
            Assert.That(WorldSessionFactory.TryCreate(store, "world-glb", "GLB",
                RigidPoseData.Identity, new BoundsData(Vector3.zero, Vector3.one), 1_000,
                out WorldManifest manifest, out string createError), Is.True, createError);
            ChunkRecord chunk = manifest.chunks[0];
            ChunkSnapshotPublishResult snapshot = await ChunkSnapshotPublisher.PublishAsync(
                store, manifest, chunk, CreateSnapshot(), 2_000);
            Assert.That(snapshot.Success, Is.True, snapshot.Error);
            ChunkRefinedPublishResult refined = await ChunkRefinedArtifactPublisher.PublishAsync(
                store, manifest, chunk, CreateRefined(), 3_000);
            Assert.That(refined.Success, Is.True, refined.Error);

            var options = new ChunkGlbWriteOptions { RoughnessFactor = 0.8f };
            ChunkGlbExportResult first = await ChunkGlbExporter.ExportRefinedAsync(store,
                manifest, chunk, options, 4_000);
            Assert.That(first.Success, Is.True, first.Error);
            Assert.That(first.Artifact.kind, Is.EqualTo(ChunkArtifactKind.Glb));
            Assert.That(first.Artifact.formatVersion,
                Is.EqualTo(ChunkGlbWriter.ArtifactFormatVersion));
            Assert.That(first.Artifact.chunkRevision, Is.EqualTo(chunk.revision));
            Assert.That(store.TryResolveVerifiedArtifact(manifest.worldId, first.Artifact,
                out string glbPath, out string verifyError), Is.True, verifyError);
            Assert.That(ReadUInt32Little(File.ReadAllBytes(glbPath), 0),
                Is.EqualTo(0x46546C67u));
            Assert.That(chunk.artifacts.Count(item => item.kind == ChunkArtifactKind.Glb),
                Is.EqualTo(1));
            Assert.That(chunk.artifacts.Any(item =>
                item.kind == ChunkArtifactKind.RefinedMesh), Is.True);
            Assert.That(chunk.artifacts.Any(item =>
                item.kind == ChunkArtifactKind.RefinedNormal), Is.True);
            int revisionAfterFirst = manifest.revision;

            ChunkGlbExportResult replay = await ChunkGlbExporter.ExportRefinedAsync(store,
                manifest, chunk, options, 5_000);
            Assert.That(replay.Success, Is.True, replay.Error);
            Assert.That(replay.Artifact.sha256, Is.EqualTo(first.Artifact.sha256));
            Assert.That(manifest.revision, Is.EqualTo(revisionAfterFirst));

            ChunkGlbExportResult conflict = await ChunkGlbExporter.ExportRefinedAsync(store,
                manifest, chunk, new ChunkGlbWriteOptions { RoughnessFactor = 0.5f }, 6_000);
            Assert.That(conflict.Success, Is.False);
            Assert.That(conflict.Failure, Is.EqualTo(ChunkGlbExportFailure.ImmutableConflict));
            Assert.That(chunk.artifacts.Single(item => item.kind == ChunkArtifactKind.Glb)
                .sha256, Is.EqualTo(first.Artifact.sha256));
            Assert.That(store.TryResolveVerifiedArtifact(manifest.worldId, first.Artifact,
                out _, out verifyError), Is.True, verifyError);

            Assert.That(store.TryLoadManifest(manifest.worldId, out WorldManifest durable,
                out _, out string loadError), Is.True, loadError);
            ChunkArtifactRecord durableGlb = durable.chunks[0].artifacts.Single(item =>
                item.kind == ChunkArtifactKind.Glb);
            Assert.That(durableGlb.sha256, Is.EqualTo(first.Artifact.sha256));
        }

        private static byte[] Write(ChunkGlbExportData source,
            ChunkGlbWriteOptions options)
        {
            using var stream = new MemoryStream();
            Assert.That(ChunkGlbWriter.TryWrite(stream, source, options,
                out _, out string error), Is.True, error);
            return stream.ToArray();
        }

        internal static ChunkGlbExportData CreateExportData(string name)
        {
            return new ChunkGlbExportData
            {
                Name = name,
                Positions = new[]
                {
                    new Vector3(1f, 2f, 3f),
                    new Vector3(2f, 2f, 3f),
                    new Vector3(1f, 3f, 3f)
                },
                Normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                TexCoords0 = new[] { Vector2.zero, Vector2.right, Vector2.up },
                Indices = new[] { 0, 1, 2 },
                TextureWidth = 2,
                TextureHeight = 2,
                // Row zero / v=0: red, green. Row one / v=1: blue, white.
                BaseColorRgba32 = new byte[]
                {
                    255, 0, 0, 255, 0, 255, 0, 255,
                    0, 0, 255, 255, 255, 255, 255, 255
                },
                NormalRgba32 = new byte[]
                {
                    128, 64, 255, 255, 128, 96, 255, 255,
                    128, 160, 255, 255, 128, 192, 255, 255
                }
            };
        }

        internal static RefinedTextureResult CreateRefined()
        {
            ChunkGlbExportData source = CreateExportData("refined");
            return new RefinedTextureResult
            {
                Positions = source.Positions,
                Normals = source.Normals,
                UVs = source.TexCoords0,
                Indices = source.Indices,
                AtlasPixels = source.BaseColorRgba32,
                NormalPixels = source.NormalRgba32,
                AtlasWidth = source.TextureWidth,
                AtlasHeight = source.TextureHeight
            };
        }

        internal static ChunkGpuSnapshot CreateSnapshot()
        {
            return new ChunkGpuSnapshot
            {
                Volume = new ChunkVolumeSnapshot
                {
                    VoxelCount = new Vector3Int(2, 2, 2),
                    VoxelSize = 0.05f,
                    IntegrationCount = 1,
                    WorldFromVolume = RigidPoseData.Identity,
                    TsdfBytes = new byte[16],
                    ColorBytes = new byte[32]
                },
                LiveMesh = new ChunkLiveMeshSnapshot
                {
                    VertexCount = 3,
                    IndexCount = 3,
                    LocalBounds = new BoundsData(Vector3.zero, Vector3.one),
                    VertexBytes = new byte[3 * ChunkLiveMeshSnapshot.VertexStride],
                    IndexBytes = UIntBytes(0, 1, 2)
                }
            };
        }

        private static byte[] UIntBytes(params uint[] values)
        {
            byte[] bytes = new byte[values.Length * sizeof(uint)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static GlbFixture ParseGlb(byte[] glb)
        {
            Assert.That(glb.Length, Is.GreaterThanOrEqualTo(28));
            int jsonLength = checked((int)ReadUInt32Little(glb, 12));
            Assert.That(ReadUInt32Little(glb, 16), Is.EqualTo(0x4E4F534Au));
            string json = Encoding.UTF8.GetString(glb, 20, jsonLength).TrimEnd(' ');
            int binaryHeader = checked(20 + jsonLength);
            int binaryLength = checked((int)ReadUInt32Little(glb, binaryHeader));
            Assert.That(ReadUInt32Little(glb, binaryHeader + 4), Is.EqualTo(0x004E4942u));
            Assert.That(binaryHeader + 8 + binaryLength, Is.EqualTo(glb.Length));
            byte[] binary = new byte[binaryLength];
            Buffer.BlockCopy(glb, binaryHeader + 8, binary, 0, binary.Length);
            return new GlbFixture { Json = json, Binary = binary };
        }

        private static byte[] DecodeStoredRgbaPng(byte[] png, out int width,
            out int height)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            Assert.That(png.Take(8).ToArray(), Is.EqualTo(signature));
            width = checked((int)ReadUInt32Big(png, 16));
            height = checked((int)ReadUInt32Big(png, 20));
            Assert.That(png[24], Is.EqualTo(8));
            Assert.That(png[25], Is.EqualTo(6));
            var idat = new List<byte>();
            int cursor = 8;
            while (cursor < png.Length)
            {
                int length = checked((int)ReadUInt32Big(png, cursor));
                string type = Encoding.ASCII.GetString(png, cursor + 4, 4);
                if (type == "IDAT")
                    idat.AddRange(png.Skip(cursor + 8).Take(length));
                cursor = checked(cursor + 12 + length);
                if (type == "IEND")
                    break;
            }
            Assert.That(cursor, Is.EqualTo(png.Length));
            Assert.That(idat.Count, Is.GreaterThan(6));
            Assert.That(idat[0], Is.EqualTo(0x78));
            int compressed = 2;
            var filtered = new List<byte>();
            bool final;
            do
            {
                byte header = idat[compressed++];
                final = (header & 1) != 0;
                Assert.That((header >> 1) & 3, Is.Zero, "PNG must use stored DEFLATE");
                int length = idat[compressed] | idat[compressed + 1] << 8;
                int complement = idat[compressed + 2] | idat[compressed + 3] << 8;
                compressed += 4;
                Assert.That((length ^ complement) & 0xFFFF, Is.EqualTo(0xFFFF));
                filtered.AddRange(idat.Skip(compressed).Take(length));
                compressed += length;
            } while (!final);
            Assert.That(compressed + 4, Is.EqualTo(idat.Count)); // Adler-32 follows
            int rowBytes = checked(width * 4);
            Assert.That(filtered.Count, Is.EqualTo(height * (rowBytes + 1)));
            byte[] rgba = new byte[checked(width * height * 4)];
            int source = 0;
            for (int row = 0; row < height; row++)
            {
                Assert.That(filtered[source++], Is.Zero, "PNG row filter must be None");
                filtered.CopyTo(source, rgba, row * rowBytes, rowBytes);
                source += rowBytes;
            }
            return rgba;
        }

        private static void AssertVector2(byte[] bytes, int offset, Vector2 expected)
        {
            Assert.That(ReadSingle(bytes, offset), Is.EqualTo(expected.x).Within(1e-6f));
            Assert.That(ReadSingle(bytes, offset + 4), Is.EqualTo(expected.y).Within(1e-6f));
        }

        private static void AssertVector3(byte[] bytes, int offset, Vector3 expected)
        {
            AssertVector2(bytes, offset, new Vector2(expected.x, expected.y));
            Assert.That(ReadSingle(bytes, offset + 8), Is.EqualTo(expected.z).Within(1e-6f));
        }

        private static void AssertVector4(byte[] bytes, int offset, Vector4 expected)
        {
            AssertVector3(bytes, offset, new Vector3(expected.x, expected.y, expected.z));
            Assert.That(ReadSingle(bytes, offset + 12), Is.EqualTo(expected.w).Within(1e-6f));
        }

        private static float ReadSingle(byte[] bytes, int offset) =>
            BitConverter.ToSingle(bytes, offset);

        private static uint ReadUInt32Little(byte[] bytes, int offset) =>
            BitConverter.ToUInt32(bytes, offset);

        private static uint ReadUInt32Big(byte[] bytes, int offset) =>
            (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 |
                   bytes[offset + 2] << 8 | bytes[offset + 3]);

        private static string Sha256(byte[] bytes)
        {
            using var algorithm = SHA256.Create();
            return string.Concat(algorithm.ComputeHash(bytes)
                .Select(value => value.ToString("x2")));
        }

        private sealed class GlbFixture
        {
            internal string Json;
            internal byte[] Binary;
        }
    }
}
