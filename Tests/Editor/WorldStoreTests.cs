using System;
using System.Collections.Generic;
using System.IO;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class WorldStoreTests
    {
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "QuestInfiniteScanTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_testRoot) && Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, true);
        }

        [Test]
        public void ManifestCommitKeepsBackupAndIgnoresInterruptedPendingFile()
        {
            var store = new WorldStore(_testRoot);
            WorldManifest manifest = CreateManifest();
            Assert.That(store.TryCommitManifest(manifest, out string firstError), Is.True,
                firstError);

            manifest.revision = 1;
            manifest.updatedUnixMilliseconds = 2_000;
            manifest.chunks[0].updatedUnixMilliseconds = 2_000;
            Assert.That(store.TryCommitManifest(manifest, out string secondError), Is.True,
                secondError);

            string worldDirectory = store.GetWorldDirectory(manifest.worldId);
            File.WriteAllText(Path.Combine(worldDirectory,
                WorldStore.ManifestFileName + ".pending-interrupted"), "{broken");
            Assert.That(store.TryLoadManifest(manifest.worldId, out WorldManifest current,
                out WorldManifestLoadSource currentSource, out _), Is.True);
            Assert.That(currentSource, Is.EqualTo(WorldManifestLoadSource.Primary));
            Assert.That(current.revision, Is.EqualTo(1));

            File.WriteAllText(Path.Combine(worldDirectory, WorldStore.ManifestFileName),
                "{broken");
            Assert.That(store.TryLoadManifest(manifest.worldId, out WorldManifest recovered,
                out WorldManifestLoadSource recoveredSource, out string recoveryNotice), Is.True);
            Assert.That(recoveredSource, Is.EqualTo(WorldManifestLoadSource.Backup));
            Assert.That(recovered.revision, Is.EqualTo(0));
            Assert.That(recoveryNotice, Does.Contain("last known-good backup"));

            recovered.revision = 2;
            recovered.updatedUnixMilliseconds = 3_000;
            recovered.chunks[0].updatedUnixMilliseconds = 3_000;
            Assert.That(store.TryCommitManifest(recovered, out string recoveryCommitError),
                Is.True, recoveryCommitError);
            File.WriteAllText(Path.Combine(worldDirectory, WorldStore.ManifestFileName),
                "{broken-again");
            Assert.That(store.TryLoadManifest(recovered.worldId, out WorldManifest stillRecoverable,
                out WorldManifestLoadSource sourceAfterRecovery, out _), Is.True);
            Assert.That(sourceAfterRecovery, Is.EqualTo(WorldManifestLoadSource.Backup));
            Assert.That(stillRecoverable.revision, Is.EqualTo(0),
                "committing over a corrupt primary must not overwrite the known-good backup");
        }

        [Test]
        public void ChunkRevisionPublishesPayloadBeforeAtomicManifestReference()
        {
            var store = new WorldStore(_testRoot);
            WorldManifest manifest = CreateManifest();
            Assert.That(store.TryCommitManifest(manifest, out string createError), Is.True,
                createError);
            Assert.That(store.TryBeginChunkRevision(manifest.worldId,
                manifest.chunks[0].chunkId, 1, out ChunkRevisionTransaction transaction,
                out string beginError), Is.True, beginError);

            using (transaction)
            {
                byte[] payload = { 1, 3, 3, 7 };
                Assert.That(transaction.TryStageBytes(ChunkArtifactKind.PrismCanonical, 6,
                    "canonical.prism", payload, out ChunkArtifactRecord artifact,
                    out string stageError), Is.True, stageError);

                ChunkRecord chunk = manifest.chunks[0];
                chunk.revision = 1;
                chunk.state = ChunkLifecycleState.Persisted;
                chunk.updatedUnixMilliseconds = 2_000;
                chunk.artifacts.Add(artifact);
                manifest.revision = 1;
                manifest.updatedUnixMilliseconds = 2_000;

                Assert.That(transaction.TryCommit(manifest, out string commitError), Is.True,
                    commitError);
                string artifactPath = Path.Combine(store.GetWorldDirectory(manifest.worldId),
                    artifact.relativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.That(File.ReadAllBytes(artifactPath), Is.EqualTo(payload));
            }

            Assert.That(store.TryLoadManifest(manifest.worldId, out WorldManifest loaded,
                out _, out _), Is.True);
            Assert.That(loaded.chunks[0].revision, Is.EqualTo(1));
            Assert.That(loaded.chunks[0].artifacts[0].sha256,
                Is.EqualTo("acb86a9cb70a84f695de89e7fe22819466205759d798d52d4a3dd95b0cdaa2a1"));
        }

        [Test]
        public void RejectedChunkManifestDoesNotPublishPayloadDirectory()
        {
            var store = new WorldStore(_testRoot);
            WorldManifest manifest = CreateManifest();
            Assert.That(store.TryCommitManifest(manifest, out _), Is.True);
            Assert.That(store.TryBeginChunkRevision(manifest.worldId,
                manifest.chunks[0].chunkId, 2, out ChunkRevisionTransaction transaction,
                out string beginError), Is.True, beginError);

            string finalDirectory = transaction.FinalDirectory;
            using (transaction)
            {
                Assert.That(transaction.TryStageBytes(ChunkArtifactKind.PrismCanonical, 6,
                    "canonical.prism", new byte[] { 9 }, out _, out _), Is.True);
                Assert.That(transaction.TryCommit(manifest, out string commitError), Is.False);
                Assert.That(commitError, Does.Contain("does not publish"));
                Assert.That(Directory.Exists(finalDirectory), Is.False);
            }
        }

        [Test]
        public void StoreRejectsChangedOrStaleManifestRevision()
        {
            var store = new WorldStore(_testRoot);
            WorldManifest manifest = CreateManifest();
            Assert.That(store.TryCommitManifest(manifest, out _), Is.True);

            manifest.displayName = "Changed without revision";
            Assert.That(store.TryCommitManifest(manifest, out string unchangedRevisionError),
                Is.False);
            Assert.That(unchangedRevisionError, Does.Contain("must increment"));

            manifest.displayName = "Revision two";
            manifest.revision = 2;
            manifest.updatedUnixMilliseconds = 2_000;
            manifest.chunks[0].updatedUnixMilliseconds = 2_000;
            Assert.That(store.TryCommitManifest(manifest, out _), Is.True);

            manifest.revision = 1;
            Assert.That(store.TryCommitManifest(manifest, out string staleError), Is.False);
            Assert.That(staleError, Does.Contain("Stale world revision"));
        }

        [Test]
        public void StreamedArtifactIsVerifiedAndTamperingIsRejected()
        {
            var store = new WorldStore(_testRoot);
            WorldManifest manifest = CreateManifest();
            Assert.That(store.TryCommitManifest(manifest, out _), Is.True);
            Assert.That(store.TryBeginChunkRevision(manifest.worldId,
                manifest.chunks[0].chunkId, 1, out ChunkRevisionTransaction transaction,
                out string beginError), Is.True, beginError);

            ChunkArtifactRecord artifact;
            using (transaction)
            {
                Assert.That(transaction.TryStageStream(ChunkArtifactKind.PrismMeshlets, 1,
                    "mesh/contact_meshlets.bin", stream =>
                    {
                        using var writer = new BinaryWriter(stream,
                            System.Text.Encoding.UTF8, true);
                        writer.Write(0x12345678);
                        writer.Write("streamed");
                    }, out artifact, out string stageError), Is.True, stageError);

                ChunkRecord chunk = manifest.chunks[0];
                chunk.revision = 1;
                chunk.artifacts.Add(artifact);
                chunk.updatedUnixMilliseconds = 2_000;
                manifest.revision = 1;
                manifest.updatedUnixMilliseconds = 2_000;
                Assert.That(transaction.TryCommit(manifest, out string commitError), Is.True,
                    commitError);
            }

            Assert.That(store.TryResolveVerifiedArtifact(manifest.worldId, artifact,
                out string path, out string verifyError), Is.True, verifyError);
            Assert.That(File.Exists(path), Is.True);

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write,
                       FileShare.None))
                stream.WriteByte(0xFF);
            Assert.That(store.TryResolveVerifiedArtifact(manifest.worldId, artifact,
                out _, out string tamperError), Is.False);
            Assert.That(tamperError, Does.Contain("SHA-256"));
        }

        private static WorldManifest CreateManifest()
        {
            return new WorldManifest
            {
                worldId = "world-atomic-test",
                displayName = "Atomic world",
                createdUnixMilliseconds = 1_000,
                updatedUnixMilliseconds = 1_000,
                revision = 0,
                worldAnchorId = string.Empty,
                chunks = new List<ChunkRecord>
                {
                    new()
                    {
                        chunkId = "chunk-000000",
                        revision = 0,
                        state = ChunkLifecycleState.Active,
                        worldFromChunk = RigidPoseData.Identity,
                        localBounds = new BoundsData(Vector3.zero, new Vector3(2f, 2f, 2f)),
                        anchorId = string.Empty,
                        createdUnixMilliseconds = 1_000,
                        updatedUnixMilliseconds = 1_000,
                        quality = 0f,
                        artifacts = new List<ChunkArtifactRecord>()
                    }
                },
                edges = new List<PoseGraphEdgeRecord>()
            };
        }

    }
}
