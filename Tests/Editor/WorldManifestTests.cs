using System.Collections.Generic;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class WorldManifestTests
    {
        [Test]
        public void JsonRoundTripPreservesIdentifiersAndTransformDirections()
        {
            WorldManifest source = CreateValidManifest();

            Assert.That(WorldManifestJson.TrySerialize(source, true, out string json,
                out WorldValidationResult writeValidation), Is.True, writeValidation.ToString());
            Assert.That(WorldManifestJson.TryDeserialize(json, out WorldManifest loaded,
                out WorldValidationResult readValidation), Is.True, readValidation.ToString());

            Assert.That(loaded.worldId, Is.EqualTo("world-0001"));
            Assert.That(loaded.chunks[1].chunkId, Is.EqualTo("chunk-0001"));
            Assert.That(Vector3.Distance(loaded.chunks[1].worldFromChunk.position,
                new Vector3(4.25f, 0.5f, -2f)), Is.LessThan(0.00001f));
            Assert.That(Quaternion.Angle(loaded.chunks[1].worldFromChunk.rotation,
                Quaternion.Euler(0f, 35f, 0f)), Is.LessThan(0.001f));
            Assert.That(loaded.edges[0].sourceChunkId, Is.EqualTo("chunk-0000"));
            Assert.That(loaded.edges[0].targetChunkId, Is.EqualTo("chunk-0001"));
        }

        [Test]
        public void JsonReaderRejectsMalformedAndUnsupportedDocuments()
        {
            Assert.That(WorldManifestJson.TryDeserialize("[1,2,3]", out _, out _), Is.False);
            Assert.That(WorldManifestJson.TryDeserialize("{ definitely-not-json", out _, out _),
                Is.False);

            WorldManifest manifest = CreateValidManifest();
            manifest.schemaVersion = WorldSchema.CurrentVersion + 1;
            string json = JsonUtility.ToJson(manifest);
            Assert.That(WorldManifestJson.TryDeserialize(json, out _,
                out WorldValidationResult validation), Is.False);
            Assert.That(validation.ToString(), Does.Contain("unsupported version"));

            Assert.That(WorldManifestJson.TryDeserialize("{\"worldId\":\"world-0001\"}",
                out _, out WorldValidationResult missingFields), Is.False);
            Assert.That(missingFields.ToString(), Does.Contain("schemaVersion"));
        }

        [Test]
        public void ValidatorRejectsDuplicateIdsUnsafePathsAndInvalidQuaternion()
        {
            WorldManifest manifest = CreateValidManifest();
            manifest.chunks[1].chunkId = manifest.chunks[0].chunkId;
            manifest.chunks[0].artifacts[0].relativePath = "../../escape.bin";
            manifest.chunks[0].worldFromChunk.rotation = new Quaternion(0f, 0f, 0f, 0f);
            manifest.worldAnchorId = "..";

            WorldValidationResult result = WorldManifestValidator.Validate(manifest);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ToString(), Does.Contain("duplicate identifier"));
            Assert.That(result.ToString(), Does.Contain("unsafe path segment"));
            Assert.That(result.ToString(), Does.Contain("unit quaternion"));
            Assert.That(result.ToString(), Does.Contain("cannot contain '..'"));
        }

        [Test]
        public void PoseUpdateChangesOnlyWorldPoseAndGraphRevision()
        {
            WorldManifest manifest = CreateValidManifest();
            ChunkRecord chunk = manifest.chunks[0];
            BoundsData originalBounds = chunk.localBounds;
            ChunkArtifactRecord originalArtifact = chunk.artifacts[0];
            int originalChunkRevision = chunk.revision;
            int originalWorldRevision = manifest.revision;
            Assert.That(PoseGraphModel.TryCreate(manifest, out PoseGraphModel graph,
                out WorldValidationResult validation), Is.True, validation.ToString());

            var replacement = new RigidPoseData(new Vector3(8f, 1f, -3f),
                Quaternion.Euler(0f, 90f, 0f));
            Assert.That(graph.TrySetWorldFromChunk(chunk.chunkId, replacement, 3_000), Is.True);

            Assert.That(chunk.worldFromChunk.position, Is.EqualTo(replacement.position));
            Assert.That(Quaternion.Angle(chunk.worldFromChunk.rotation, replacement.rotation),
                Is.LessThan(0.001f));
            Assert.That(chunk.localBounds, Is.EqualTo(originalBounds));
            Assert.That(chunk.artifacts[0], Is.SameAs(originalArtifact));
            Assert.That(chunk.revision, Is.EqualTo(originalChunkRevision));
            Assert.That(manifest.revision, Is.EqualTo(originalWorldRevision + 1));
        }

        [Test]
        public void EdgeDirectionPredictsWorldFromTarget()
        {
            WorldManifest manifest = CreateValidManifest();
            manifest.chunks[0].worldFromChunk = new RigidPoseData(new Vector3(10f, 0f, 0f),
                Quaternion.Euler(0f, 90f, 0f));
            manifest.edges[0].sourceFromTarget = new RigidPoseData(new Vector3(0f, 0f, 2f),
                Quaternion.identity);
            Assert.That(PoseGraphModel.TryCreate(manifest, out PoseGraphModel graph,
                out WorldValidationResult validation), Is.True, validation.ToString());

            Assert.That(graph.TryPredictTargetWorldPose(manifest.edges[0],
                out RigidPoseData predicted), Is.True);

            Vector3 expected = manifest.chunks[0].worldFromChunk.TransformPoint(
                manifest.edges[0].sourceFromTarget.position);
            Assert.That(Vector3.Distance(predicted.position, expected), Is.LessThan(0.0001f));
        }

        private static WorldManifest CreateValidManifest()
        {
            const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var first = new ChunkRecord
            {
                chunkId = "chunk-0000",
                revision = 2,
                state = ChunkLifecycleState.Persisted,
                worldFromChunk = RigidPoseData.Identity,
                localBounds = new BoundsData(Vector3.zero, new Vector3(6.4f, 6.4f, 6.4f)),
                createdUnixMilliseconds = 1_100,
                updatedUnixMilliseconds = 1_900,
                quality = 0.8f,
                artifacts = new List<ChunkArtifactRecord>
                {
                    new()
                    {
                        kind = ChunkArtifactKind.PrismCanonical,
                        formatVersion = 6,
                        chunkRevision = 2,
                        relativePath = "chunks/chunk-0000/canonical.prism",
                        sha256 = Hash,
                        byteLength = 4096
                    }
                }
            };
            var second = new ChunkRecord
            {
                chunkId = "chunk-0001",
                revision = 0,
                state = ChunkLifecycleState.Active,
                worldFromChunk = new RigidPoseData(new Vector3(4.25f, 0.5f, -2f),
                    Quaternion.Euler(0f, 35f, 0f)),
                localBounds = new BoundsData(Vector3.zero, new Vector3(6.4f, 6.4f, 6.4f)),
                createdUnixMilliseconds = 1_900,
                updatedUnixMilliseconds = 2_000,
                quality = 0.2f,
                artifacts = new List<ChunkArtifactRecord>()
            };
            return new WorldManifest
            {
                worldId = "world-0001",
                displayName = "Test world",
                createdUnixMilliseconds = 1_000,
                updatedUnixMilliseconds = 2_000,
                revision = 3,
                worldAnchorId = "anchor-0001",
                chunks = new List<ChunkRecord> { first, second },
                edges = new List<PoseGraphEdgeRecord>
                {
                    new()
                    {
                        edgeId = "edge-0000-0001",
                        sourceChunkId = first.chunkId,
                        targetChunkId = second.chunkId,
                        kind = PoseGraphConstraintKind.Tracking,
                        sourceFromTarget = second.worldFromChunk,
                        confidence = 0.95f,
                        covarianceDiagonal = new[] { 0.01f, 0.01f, 0.01f, 0.02f, 0.02f, 0.02f },
                        observedUnixMilliseconds = 2_000,
                        provenance = "test"
                    }
                }
            };
        }
    }
}
