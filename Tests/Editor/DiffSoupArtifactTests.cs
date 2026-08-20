using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Genesis.RoomScan.HeavyCompute;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class DiffSoupArtifactTests
    {
        private sealed class ArtifactFixture
        {
            public string Path;
            public HeavyComputeBlobDescriptor Descriptor;
        }

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
        public void ImporterAcceptsCanonicalRendererPayload()
        {
            HeavyComputeSubmission submission = CreateSubmission("world-artifact",
                "chunk-000000", 1);
            ArtifactFixture fixture = BuildArtifact("valid", submission);

            DiffSoupArtifactImportResult result = DiffSoupArtifactImporter.Import(
                fixture.Path, submission, fixture.Descriptor);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Data.Positions, Has.Length.EqualTo(3));
            Assert.That(result.Data.Indices, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(result.Data.Manifest.model.lutWidth, Is.EqualTo(3));
            Assert.That(result.Data.Mlp.W1, Has.Length.EqualTo(256));
            Assert.That(result.Data.Metadata.num_faces, Is.EqualTo(1));
        }

        [Test]
        public void ImporterRejectsCorruptIndicesDimensionsVersionsAndOuterHash()
        {
            HeavyComputeSubmission submission = CreateSubmission("world-reject",
                "chunk-000000", 1);

            ArtifactFixture badIndex = BuildArtifact("bad-index", submission,
                invalidIndex: true);
            AssertRejected(badIndex, submission, "invalid index");

            ArtifactFixture badDimension = BuildArtifact("bad-dimension", submission,
                manifestLutWidth: 2);
            AssertRejected(badDimension, submission, "dimensions");

            ArtifactFixture badVersion = BuildArtifact("bad-version", submission,
                artifactFormatVersion: 99);
            AssertRejected(badVersion, submission, "protocol v2");

            ArtifactFixture changed = BuildArtifact("changed", submission);
            File.AppendAllText(changed.Path, "changed", Encoding.ASCII);
            AssertRejected(changed, submission, "changed relative");
        }

        [Test]
        public void ImporterRejectsUnsafeOrUndeclaredZipMembership()
        {
            HeavyComputeSubmission submission = CreateSubmission("world-membership",
                "chunk-000000", 1);
            ArtifactFixture fixture = BuildArtifact("unsafe", submission,
                extraPath: "../escape.bin");

            AssertRejected(fixture, submission, "unsafe");
        }

        [Test]
        public void ImporterAcceptsOptionalActualCudaWorkerArtifact()
        {
            string fixtureRoot = Environment.GetEnvironmentVariable(
                "QIS_DIFFSOUP_CUDA_FIXTURE");
            if (string.IsNullOrEmpty(fixtureRoot))
                Assert.Ignore("Set QIS_DIFFSOUP_CUDA_FIXTURE to a completed server job root.");
            const string jobId =
                "9cf9ccbcdcd863c5372a6bec1844552c4917be555f93ce4512cff2e479bade1c";
            string input = Path.Combine(fixtureRoot, "uploads", jobId + ".zip");
            string artifact = Path.Combine(fixtureRoot, "artifacts", jobId + ".zip");
            Assert.That(File.Exists(input), Is.True, input);
            Assert.That(File.Exists(artifact), Is.True, artifact);
            var inputDescriptor = new HeavyComputeBlobDescriptor
            {
                mediaType = HeavyComputeProtocol.ChunkBundleMediaType,
                formatVersion = HeavyComputeProtocol.ChunkBundleVersion,
                byteLength = new FileInfo(input).Length,
                sha256 = Hashing.ComputeSha256(input)
            };
            Assert.That(HeavyComputeSubmission.TryCreate(new HeavyComputeJobKey(
                    "world-bundle", "chunk-000000", 1), inputDescriptor, "preview", true,
                null, out HeavyComputeSubmission submission, out string createError), Is.True,
                createError);
            Assert.That(submission.jobId, Is.EqualTo(jobId));
            var artifactDescriptor = new HeavyComputeBlobDescriptor
            {
                mediaType = HeavyComputeProtocol.DiffSoupArtifactMediaType,
                formatVersion = HeavyComputeProtocol.DiffSoupArtifactVersion,
                byteLength = new FileInfo(artifact).Length,
                sha256 = Hashing.ComputeSha256(artifact)
            };

            DiffSoupArtifactImportResult result = DiffSoupArtifactImporter.Import(artifact,
                submission, artifactDescriptor);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Data.Manifest.producer.sourceCommit,
                Is.EqualTo("c74e35de74ad0116977b23e7951f4cbc25ab0f6b"));
            Assert.That(result.Data.Positions, Has.Length.EqualTo(3));
            Assert.That(result.Data.Indices, Has.Length.EqualTo(3));
        }

        [Test]
        public async Task PublisherPersistsContentAddressedArtifactIdempotently()
        {
            (WorldStore store, WorldManifest manifest, ChunkRecord chunk) =
                await CreatePublishedWorld("world-promote");
            HeavyComputeSubmission submission = CreateSubmission(manifest.worldId,
                chunk.chunkId, chunk.revision);
            ArtifactFixture fixture = BuildArtifact("promote", submission);
            HeavyComputeQueueItem job = ReadyJob(submission, fixture.Descriptor);

            DiffSoupArtifactPublishResult first = await DiffSoupArtifactPublisher.PublishAsync(
                store, manifest, chunk, job, fixture.Path, 3_000);

            Assert.That(first.Success, Is.True, first.Error);
            Assert.That(first.Artifact.kind, Is.EqualTo(ChunkArtifactKind.DiffSoup));
            Assert.That(first.Artifact.chunkRevision, Is.EqualTo(1));
            Assert.That(first.Artifact.relativePath, Does.Contain(
                "/enhancements/0000000001/diffsoup-" + fixture.Descriptor.sha256 + "/"));
            Assert.That(store.TryResolveVerifiedArtifact(manifest.worldId, first.Artifact,
                out string storedPath, out string verifyError), Is.True, verifyError);
            Assert.That(File.ReadAllBytes(storedPath), Is.EqualTo(File.ReadAllBytes(fixture.Path)));
            int committedWorldRevision = manifest.revision;

            DiffSoupArtifactPublishResult repeated = await DiffSoupArtifactPublisher.PublishAsync(
                store, manifest, chunk, job, fixture.Path, 3_000);
            Assert.That(repeated.Success, Is.True, repeated.Error);
            Assert.That(manifest.revision, Is.EqualTo(committedWorldRevision));
            Assert.That(chunk.artifacts.Count(candidate =>
                candidate.kind == ChunkArtifactKind.DiffSoup), Is.EqualTo(1));
        }

        [Test]
        public async Task InvalidConflictingAndLateArtifactsKeepLastKnownGoodReference()
        {
            (WorldStore store, WorldManifest manifest, ChunkRecord chunk) =
                await CreatePublishedWorld("world-keep-good");
            HeavyComputeSubmission revisionOne = CreateSubmission(manifest.worldId,
                chunk.chunkId, 1);
            ArtifactFixture good = BuildArtifact("good", revisionOne);
            DiffSoupArtifactPublishResult accepted = await DiffSoupArtifactPublisher.PublishAsync(
                store, manifest, chunk, ReadyJob(revisionOne, good.Descriptor), good.Path, 3_000);
            Assert.That(accepted.Success, Is.True, accepted.Error);
            string knownGoodPath = accepted.Artifact.relativePath;
            string knownGoodHash = accepted.Artifact.sha256;
            int worldRevision = manifest.revision;

            ArtifactFixture corrupt = BuildArtifact("corrupt", revisionOne,
                invalidIndex: true);
            DiffSoupArtifactPublishResult corruptResult =
                await DiffSoupArtifactPublisher.PublishAsync(store, manifest, chunk,
                    ReadyJob(revisionOne, corrupt.Descriptor), corrupt.Path, 4_000);
            Assert.That(corruptResult.Success, Is.False);
            AssertLastKnownGood(chunk, knownGoodPath, knownGoodHash, worldRevision, manifest);

            ArtifactFixture conflicting = BuildArtifact("conflicting", revisionOne,
                positionOffset: 0.25f);
            DiffSoupArtifactPublishResult conflictResult =
                await DiffSoupArtifactPublisher.PublishAsync(store, manifest, chunk,
                    ReadyJob(revisionOne, conflicting.Descriptor), conflicting.Path, 4_000);
            Assert.That(conflictResult.Success, Is.False);
            Assert.That(conflictResult.Error, Does.Contain("different immutable artifact"));
            AssertLastKnownGood(chunk, knownGoodPath, knownGoodHash, worldRevision, manifest);

            ChunkSnapshotPublishResult revisionTwo = await ChunkSnapshotPublisher.PublishAsync(
                store, manifest, chunk, CreateSnapshot(), 5_000);
            Assert.That(revisionTwo.Success, Is.True, revisionTwo.Error);
            Assert.That(chunk.revision, Is.EqualTo(2));
            int revisionAfterMapper = manifest.revision;
            DiffSoupArtifactPublishResult late = await DiffSoupArtifactPublisher.PublishAsync(
                store, manifest, chunk, ReadyJob(revisionOne, good.Descriptor), good.Path, 6_000);
            Assert.That(late.Success, Is.False);
            Assert.That(late.Error, Does.Contain("stale"));
            AssertLastKnownGood(chunk, knownGoodPath, knownGoodHash,
                revisionAfterMapper, manifest);
        }

        [Test]
        public async Task PromotionTransactionCannotPublishAfterChunkAdvances()
        {
            (WorldStore store, WorldManifest manifest, ChunkRecord chunk) =
                await CreatePublishedWorld("world-race");
            HeavyComputeSubmission submission = CreateSubmission(manifest.worldId,
                chunk.chunkId, 1);
            ArtifactFixture fixture = BuildArtifact("race", submission);
            Assert.That(store.TryBeginChunkArtifactPromotion(manifest.worldId, chunk.chunkId,
                1, ChunkArtifactKind.DiffSoup, 1, "diffsoup.zip", fixture.Path,
                fixture.Descriptor.byteLength, fixture.Descriptor.sha256,
                out ChunkArtifactPromotion promotion, out string beginError), Is.True, beginError);

            using (promotion)
            {
                ChunkSnapshotPublishResult advanced = await ChunkSnapshotPublisher.PublishAsync(
                    store, manifest, chunk, CreateSnapshot(), 3_000);
                Assert.That(advanced.Success, Is.True, advanced.Error);
                Assert.That(promotion.TryCommit(manifest, chunk, 4_000,
                    out string commitError), Is.False);
                Assert.That(commitError, Does.Contain("current chunk revision"));
                Assert.That(Directory.Exists(promotion.FinalDirectory), Is.False);
            }
        }

        [Test]
        public async Task SchedulerReconcilesDownloadedArtifactAfterRestart()
        {
            string persistentRoot = Path.Combine(_root, "persistent");
            string worldsRoot = Path.Combine(persistentRoot, "InfiniteWorlds");
            var worldStore = new WorldStore(worldsRoot);
            Assert.That(WorldSessionFactory.TryCreate(worldStore, "world-restart",
                "Restart", RigidPoseData.Identity,
                new BoundsData(Vector3.zero, Vector3.one), 1_000,
                out WorldManifest manifest, out string worldError), Is.True, worldError);
            ChunkRecord chunk = manifest.chunks[0];
            ChunkSnapshotPublishResult snapshot = await ChunkSnapshotPublisher.PublishAsync(
                worldStore, manifest, chunk, CreateSnapshot(), 2_000);
            Assert.That(snapshot.Success, Is.True, snapshot.Error);
            HeavyComputeSubmission submission = CreateSubmission(manifest.worldId,
                chunk.chunkId, chunk.revision);
            ArtifactFixture fixture = BuildArtifact("restart", submission);
            var queue = new HeavyComputeQueueStore(Path.Combine(worldsRoot,
                ".heavy-compute"));
            string input = queue.GetInputPath(submission.jobId);
            File.WriteAllBytes(input, Encoding.ASCII.GetBytes("input"));
            Assert.That(queue.TryEnqueue(submission, input, 2_100,
                out _, out string enqueueError), Is.True, enqueueError);
            string queueArtifact = queue.GetArtifactPath(submission.jobId);
            File.Copy(fixture.Path, queueArtifact);
            Assert.That(queue.TryApply(submission.jobId, current =>
            {
                current.localState = HeavyComputeLocalState.Ready;
                current.artifactBundle = fixture.Descriptor;
                current.artifactRelativePath = "artifacts/" + submission.jobId +
                                               ".diffsoup.zip";
                current.progress = 1f;
                current.nextAttemptUnixMs = long.MaxValue;
                current.updatedUnixMs = 2_200;
                current.message = "downloaded before restart";
                return true;
            }, out _, out string readyError), Is.True, readyError);

            var gameObject = new GameObject("Refinement scheduler restart test");
            try
            {
                var scheduler = gameObject.AddComponent<ChunkRefinementScheduler>();
                Assert.That(scheduler.TryInitialize(persistentRoot, out string initError),
                    Is.True, initError);
                int promotedEvents = 0;
                scheduler.ArtifactPromoted += (_, result) =>
                {
                    if (result.Success) promotedEvents++;
                };

                Assert.That(await scheduler.ReconcileOneReadyArtifactAsync(), Is.True);
                Assert.That(promotedEvents, Is.EqualTo(1));
                Assert.That(worldStore.TryLoadManifest(manifest.worldId,
                    out WorldManifest durable, out _, out string loadError), Is.True,
                    loadError);
                ChunkArtifactRecord artifact = durable.chunks[0].artifacts.Single(candidate =>
                    candidate.kind == ChunkArtifactKind.DiffSoup);
                Assert.That(artifact.sha256, Is.EqualTo(fixture.Descriptor.sha256));
                Assert.That(worldStore.TryResolveVerifiedArtifact(manifest.worldId, artifact,
                    out _, out string verifyError), Is.True, verifyError);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static void AssertRejected(ArtifactFixture fixture,
            HeavyComputeSubmission submission, string message)
        {
            DiffSoupArtifactImportResult result = DiffSoupArtifactImporter.Import(
                fixture.Path, submission, fixture.Descriptor);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Data, Is.Null);
            Assert.That(result.Error, Does.Contain(message).IgnoreCase);
        }

        private static void AssertLastKnownGood(ChunkRecord chunk, string path,
            string hash, int expectedWorldRevision, WorldManifest manifest)
        {
            ChunkArtifactRecord current = chunk.artifacts.Single(candidate =>
                candidate.kind == ChunkArtifactKind.DiffSoup);
            Assert.That(current.relativePath, Is.EqualTo(path));
            Assert.That(current.sha256, Is.EqualTo(hash));
            Assert.That(manifest.revision, Is.EqualTo(expectedWorldRevision));
        }

        private async Task<(WorldStore Store, WorldManifest Manifest, ChunkRecord Chunk)>
            CreatePublishedWorld(string worldId)
        {
            var store = new WorldStore(_root);
            Assert.That(WorldSessionFactory.TryCreate(store, worldId, "Artifact tests",
                RigidPoseData.Identity, new BoundsData(Vector3.zero, Vector3.one), 1_000,
                out WorldManifest manifest, out string createError), Is.True, createError);
            ChunkRecord chunk = manifest.chunks[0];
            ChunkSnapshotPublishResult published = await ChunkSnapshotPublisher.PublishAsync(
                store, manifest, chunk, CreateSnapshot(), 2_000);
            Assert.That(published.Success, Is.True, published.Error);
            return (store, manifest, chunk);
        }

        private ArtifactFixture BuildArtifact(string name, HeavyComputeSubmission submission,
            bool invalidIndex = false, int manifestLutWidth = 3,
            int artifactFormatVersion = HeavyComputeProtocol.DiffSoupArtifactVersion,
            string extraPath = null, float positionOffset = 0f)
        {
            var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["model/mesh.ply"] = MeshBytes(invalidIndex, positionOffset),
                ["model/lut0.png"] = PngBytes(3, 1, 31),
                ["model/lut1.png"] = PngBytes(3, 1, 127),
                ["model/mlp_weights.json"] = Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                    new DiffSoupMlpWeights
                    {
                        W1 = new float[256], b1 = new float[16],
                        W2 = new float[256], b2 = new float[16],
                        W3 = new float[48], b3 = new float[3]
                    })),
                ["model/meta.json"] = Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                    new DiffSoupMetadata
                    {
                        up = new[] { 0f, 1f, 0f }, level = 0,
                        background = new[] { 0f, 0f, 0f },
                        num_faces = 1, num_verts = 3
                    }))
            };
            var files = new[]
            {
                Descriptor("mesh", "model/mesh.ply",
                    "application/vnd.questinfinitescan.diffsoup-mesh", payloads),
                Descriptor("lut0", "model/lut0.png", "image/png", payloads),
                Descriptor("lut1", "model/lut1.png", "image/png", payloads),
                Descriptor("mlp", "model/mlp_weights.json", "application/json", payloads),
                Descriptor("meta", "model/meta.json", "application/json", payloads)
            };
            var manifest = new DiffSoupArtifactManifest
            {
                schemaVersion = HeavyComputeProtocol.Version,
                artifactFormatVersion = artifactFormatVersion,
                jobId = submission.jobId,
                requestFingerprint = submission.requestFingerprint,
                key = submission.key,
                producer = new DiffSoupProducer
                {
                    name = "diffsoup",
                    sourceCommit = new string('a', 40),
                    compatibilityTag = new string('b', 64)
                },
                model = new DiffSoupModelDescription
                {
                    meshSpace = "chunk-local",
                    coordinateSystem = "unity-lh-y-up-z-forward",
                    units = "meter",
                    frontFace = "clockwise",
                    featureEncoding = "diffsoup-sh2-mlp16-v1",
                    level = 0,
                    numVertices = 3,
                    numFaces = 1,
                    lutWidth = manifestLutWidth,
                    lutHeight = 1
                },
                files = files
            };
            string artifactPath = Path.Combine(_root, name + ".diffsoup.zip");
            using (var file = new FileStream(artifactPath, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, false))
            {
                WriteEntry(archive, "artifact.json",
                    Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest)));
                foreach (KeyValuePair<string, byte[]> payload in payloads)
                    WriteEntry(archive, payload.Key, payload.Value);
                if (extraPath != null)
                    WriteEntry(archive, extraPath, new byte[] { 1 });
            }
            return new ArtifactFixture
            {
                Path = artifactPath,
                Descriptor = new HeavyComputeBlobDescriptor
                {
                    mediaType = HeavyComputeProtocol.DiffSoupArtifactMediaType,
                    formatVersion = HeavyComputeProtocol.DiffSoupArtifactVersion,
                    byteLength = new FileInfo(artifactPath).Length,
                    sha256 = Hashing.ComputeSha256(artifactPath)
                }
            };
        }

        private static DiffSoupArtifactFile Descriptor(string role, string path,
            string mediaType, IReadOnlyDictionary<string, byte[]> payloads)
        {
            byte[] bytes = payloads[path];
            return new DiffSoupArtifactFile
            {
                role = role,
                path = path,
                mediaType = mediaType,
                formatVersion = 1,
                byteLength = bytes.Length,
                sha256 = Sha256(bytes)
            };
        }

        private static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path,
                System.IO.Compression.CompressionLevel.NoCompression);
            entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using Stream stream = entry.Open();
            stream.Write(bytes, 0, bytes.Length);
        }

        private static byte[] MeshBytes(bool invalidIndex, float positionOffset)
        {
            const string header = "ply\nformat binary_little_endian 1.0\n" +
                                  "element vertex 3\nproperty float x\nproperty float y\n" +
                                  "property float z\nelement face 1\n" +
                                  "property list uchar int vertex_indices\nend_header\n";
            using var stream = new MemoryStream();
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                foreach (Vector3 vertex in new[]
                         {
                             new Vector3(positionOffset, 0f, 0f),
                             new Vector3(1f, 0f, 0f),
                             new Vector3(0f, 1f, 0f)
                         })
                {
                    writer.Write(vertex.x);
                    writer.Write(vertex.y);
                    writer.Write(vertex.z);
                }
                writer.Write((byte)3);
                writer.Write(0);
                writer.Write(1);
                writer.Write(invalidIndex ? 9 : 2);
            }
            return stream.ToArray();
        }

        private static byte[] PngBytes(int width, int height, byte value)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                texture.SetPixelData(Enumerable.Repeat(value, width * height * 4).ToArray(), 0);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static HeavyComputeSubmission CreateSubmission(string worldId,
            string chunkId, int revision)
        {
            byte[] input = Encoding.ASCII.GetBytes("input");
            var descriptor = new HeavyComputeBlobDescriptor
            {
                mediaType = HeavyComputeProtocol.ChunkBundleMediaType,
                formatVersion = HeavyComputeProtocol.ChunkBundleVersion,
                byteLength = input.Length,
                sha256 = Sha256(input)
            };
            Assert.That(HeavyComputeSubmission.TryCreate(
                new HeavyComputeJobKey(worldId, chunkId, revision), descriptor,
                "preview", true, null, out HeavyComputeSubmission submission,
                out string error), Is.True, error);
            return submission;
        }

        private static HeavyComputeQueueItem ReadyJob(HeavyComputeSubmission submission,
            HeavyComputeBlobDescriptor descriptor) => new()
        {
            submission = submission,
            localState = HeavyComputeLocalState.Ready,
            artifactBundle = descriptor,
            artifactRelativePath = "artifacts/" + submission.jobId + ".diffsoup.zip"
        };

        private static string Sha256(byte[] bytes)
        {
            using var algorithm = SHA256.Create();
            return string.Concat(algorithm.ComputeHash(bytes).Select(value =>
                value.ToString("x2")));
        }

        private static ChunkGpuSnapshot CreateSnapshot() => new()
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
                IndexBytes = UInt32Bytes(0, 1, 2)
            }
        };

        private static byte[] UInt32Bytes(params uint[] values)
        {
            var bytes = new byte[values.Length * sizeof(uint)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
