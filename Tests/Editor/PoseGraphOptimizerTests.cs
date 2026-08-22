using System;
using System.Collections.Generic;
using System.IO;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class PoseGraphOptimizerTests
    {
        private const string ArtifactHash =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
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
            if (!string.IsNullOrEmpty(_root) && Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void ClosedLoopReducesSe3ErrorKeepsRootAndIsDetached()
        {
            WorldManifest manifest = CreateDriftedSquare(includeLoop: true);
            string before = Serialize(manifest);
            PoseGraphOptimizationSettings settings = Settings();

            Assert.That(PoseGraphOptimizer.TryOptimize(manifest, settings,
                out PoseGraphSolution solution, out string error), Is.True, error);

            Assert.That(Serialize(manifest), Is.EqualTo(before),
                "optimization must remain detached until an explicit atomic apply");
            Assert.That(solution.FixedChunkIds, Does.Contain("chunk-a"));
            Assert.That(solution.FinalError.TranslationRmsMeters,
                Is.LessThan(solution.InitialError.TranslationRmsMeters * 0.45f));
            Assert.That(solution.FinalError.RotationRmsDegrees,
                Is.LessThan(solution.InitialError.RotationRmsDegrees * 0.45f));
            PoseGraphPoseUpdate root = FindUpdate(solution, "chunk-a");
            AssertPose(root.OptimizedPose, root.OriginalPose, 1e-6f, 1e-4f);
        }

        [Test]
        public void RepeatedOptimizationIsDeterministicAndRejectsGrossLoopWithProvenance()
        {
            WorldManifest manifest = CreateDriftedSquare(includeLoop: true);
            manifest.edges.Add(MakeEdge("edge-outlier", "chunk-a", "chunk-c",
                PoseGraphConstraintKind.LoopClosure,
                new RigidPoseData(new Vector3(50f, 0f, -30f),
                    Quaternion.Euler(0f, 150f, 0f)), 1f,
                new[] { 0.0001f, 0.0001f, 0.0001f, 0.0001f, 0.0001f, 0.0001f },
                "synthetic-gross-outlier"));
            Assert.That(WorldManifestValidator.Validate(manifest).IsValid, Is.True);

            Assert.That(PoseGraphOptimizer.TryOptimize(manifest, Settings(),
                out PoseGraphSolution first, out string firstError), Is.True, firstError);
            Assert.That(PoseGraphOptimizer.TryOptimize(manifest, Settings(),
                out PoseGraphSolution second, out string secondError), Is.True, secondError);

            Assert.That(first.RejectedEdges.Count, Is.EqualTo(1));
            Assert.That(first.RejectedEdges[0].EdgeId, Is.EqualTo("edge-outlier"));
            Assert.That(first.RejectedEdges[0].Provenance,
                Is.EqualTo("synthetic-gross-outlier"));
            Assert.That(first.Updates.Count, Is.EqualTo(second.Updates.Count));
            for (int i = 0; i < first.Updates.Count; i++)
            {
                Assert.That(first.Updates[i].ChunkId, Is.EqualTo(second.Updates[i].ChunkId));
                AssertPose(first.Updates[i].OptimizedPose,
                    second.Updates[i].OptimizedPose, 1e-7f, 1e-5f);
            }
            Assert.That(FindUpdate(first, "chunk-c").OptimizedPose.position.magnitude,
                Is.LessThan(5f), "a rejected loop must not warp the connected component");
        }

        [Test]
        public void DisconnectedComponentsEachKeepTheirOldestRoot()
        {
            WorldManifest manifest = CreateDriftedSquare(includeLoop: false);
            manifest.edges.Clear();
            manifest.edges.Add(MakeEdge("edge-ab", "chunk-a", "chunk-b",
                PoseGraphConstraintKind.Tracking,
                new RigidPoseData(new Vector3(2f, 0f, 0f), Quaternion.identity),
                1f, DefaultCovariance(), "component-one"));
            manifest.edges.Add(MakeEdge("edge-cd", "chunk-c", "chunk-d",
                PoseGraphConstraintKind.Tracking,
                new RigidPoseData(new Vector3(-2f, 0f, 0f), Quaternion.identity),
                1f, DefaultCovariance(), "component-two"));

            Assert.That(PoseGraphOptimizer.TryOptimize(manifest, Settings(),
                out PoseGraphSolution solution, out string error), Is.True, error);

            Assert.That(solution.FixedChunkIds, Is.EquivalentTo(
                new[] { "chunk-a", "chunk-c" }));
            AssertPose(FindUpdate(solution, "chunk-a").OriginalPose,
                FindUpdate(solution, "chunk-a").OptimizedPose, 1e-7f, 1e-5f);
            AssertPose(FindUpdate(solution, "chunk-c").OriginalPose,
                FindUpdate(solution, "chunk-c").OptimizedPose, 1e-7f, 1e-5f);
        }

        [Test]
        public void AtomicApplyPersistsOnlyGraphPosesAndMetadataRevision()
        {
            var store = new WorldStore(_root);
            WorldManifest manifest = CreateDriftedSquare(includeLoop: true);
            Assert.That(store.TryCommitManifest(manifest, out string createError),
                Is.True, createError);
            Assert.That(PoseGraphOptimizer.TryOptimize(manifest, Settings(),
                out PoseGraphSolution solution, out string optimizeError),
                Is.True, optimizeError);
            string[] stableChunkState = CaptureStableChunkState(manifest);
            int oldWorldRevision = manifest.revision;

            Assert.That(PoseGraphOptimizer.TryApplySolution(manifest, solution,
                store, 3_000, out string applyError), Is.True, applyError);

            Assert.That(manifest.revision, Is.EqualTo(oldWorldRevision + 1));
            Assert.That(CaptureStableChunkState(manifest), Is.EqualTo(stableChunkState),
                "optimizer may not alter chunk-local bounds, revisions, state, quality, " +
                "anchors, or artifact references");
            Assert.That(store.TryLoadManifest(manifest.worldId,
                out WorldManifest durable, out _, out string loadError), Is.True, loadError);
            Assert.That(Serialize(durable), Is.EqualTo(Serialize(manifest)));
        }

        [Test]
        public void StalePoseLateInSolutionLeavesEveryEarlierPoseUntouched()
        {
            WorldManifest manifest = CreateDriftedSquare(includeLoop: true);
            Assert.That(PoseGraphOptimizer.TryOptimize(manifest, Settings(),
                out PoseGraphSolution solution, out string optimizeError),
                Is.True, optimizeError);
            manifest.chunks[^1].worldFromChunk.position += Vector3.up * 0.25f;
            string immediatelyBeforeApply = Serialize(manifest);

            Assert.That(PoseGraphOptimizer.TryApplySolution(manifest, solution,
                null, 3_000, out string error), Is.False);

            Assert.That(error, Does.Contain("no longer matches"));
            Assert.That(Serialize(manifest), Is.EqualTo(immediatelyBeforeApply));
        }

        [Test]
        public void DurableRevisionConflictRollsBackEveryInMemoryPoseAndTimestamp()
        {
            var store = new WorldStore(_root);
            WorldManifest manifest = CreateDriftedSquare(includeLoop: true);
            Assert.That(store.TryCommitManifest(manifest, out string createError),
                Is.True, createError);
            Assert.That(PoseGraphOptimizer.TryOptimize(manifest, Settings(),
                out PoseGraphSolution solution, out string optimizeError),
                Is.True, optimizeError);

            WorldManifest concurrent = Clone(manifest);
            concurrent.revision++;
            concurrent.displayName = "Concurrent writer";
            concurrent.updatedUnixMilliseconds = 2_500;
            for (int i = 0; i < concurrent.chunks.Count; i++)
                concurrent.chunks[i].updatedUnixMilliseconds = 2_500;
            Assert.That(store.TryCommitManifest(concurrent,
                out string concurrentError), Is.True, concurrentError);
            string immediatelyBeforeApply = Serialize(manifest);

            Assert.That(PoseGraphOptimizer.TryApplySolution(manifest, solution,
                store, 3_000, out string applyError), Is.False);

            Assert.That(applyError, Does.Contain("must increment"));
            Assert.That(Serialize(manifest), Is.EqualTo(immediatelyBeforeApply));
        }

        [Test]
        public void CovarianceAndConfidenceGivePreciseConstraintMoreInfluence()
        {
            WorldManifest manifest = CreateDriftedSquare(includeLoop: false);
            manifest.chunks.RemoveRange(2, 2);
            manifest.edges.Clear();
            manifest.chunks[1].worldFromChunk = new RigidPoseData(
                new Vector3(3f, 0f, 0f), Quaternion.identity);
            manifest.edges.Add(MakeEdge("edge-precise", "chunk-a", "chunk-b",
                PoseGraphConstraintKind.Icp,
                new RigidPoseData(new Vector3(2f, 0f, 0f), Quaternion.identity),
                1f, new[] { 0.0004f, 0.0004f, 0.0004f, 0.001f, 0.001f, 0.001f },
                "high-quality-icp"));
            manifest.edges.Add(MakeEdge("edge-weak", "chunk-a", "chunk-b",
                PoseGraphConstraintKind.Overlap,
                new RigidPoseData(new Vector3(4f, 0f, 0f), Quaternion.identity),
                0.1f, new[] { 0.25f, 0.25f, 0.25f, 0.5f, 0.5f, 0.5f },
                "weak-overlap"));

            Assert.That(PoseGraphOptimizer.TryOptimize(manifest, Settings(),
                out PoseGraphSolution solution, out string error), Is.True, error);

            float optimizedX = FindUpdate(solution, "chunk-b").OptimizedPose.position.x;
            Assert.That(optimizedX, Is.EqualTo(2f).Within(0.02f));
        }

        private static WorldManifest CreateDriftedSquare(bool includeLoop)
        {
            var groundTruth = new Dictionary<string, RigidPoseData>
            {
                ["chunk-a"] = Pose(0f, 0f, 0f),
                ["chunk-b"] = Pose(2f, 0f, 0f),
                ["chunk-c"] = Pose(2f, 0f, 2f),
                ["chunk-d"] = Pose(0f, 0f, 2f)
            };
            var manifest = new WorldManifest
            {
                worldId = "pose-graph-world",
                displayName = "Pose graph test",
                createdUnixMilliseconds = 1_000,
                updatedUnixMilliseconds = 2_000,
                revision = 3,
                worldAnchorId = "anchor-root",
                chunks = new List<ChunkRecord>
                {
                    Chunk("chunk-a", Pose(0f, 0f, 0f), 1_000),
                    Chunk("chunk-b", Pose(2.10f, 2f, 0.05f), 1_010),
                    Chunk("chunk-c", Pose(2.25f, 4f, 2.15f), 1_020),
                    Chunk("chunk-d", Pose(0.20f, 5f, 2.25f), 1_030)
                },
                edges = new List<PoseGraphEdgeRecord>()
            };
            AddMeasured(manifest, "edge-ab", "chunk-a", "chunk-b", groundTruth);
            AddMeasured(manifest, "edge-bc", "chunk-b", "chunk-c", groundTruth);
            AddMeasured(manifest, "edge-cd", "chunk-c", "chunk-d", groundTruth);
            if (includeLoop)
                AddMeasured(manifest, "edge-da", "chunk-d", "chunk-a", groundTruth,
                    PoseGraphConstraintKind.LoopClosure);
            Assert.That(WorldManifestValidator.Validate(manifest).IsValid, Is.True);
            return manifest;
        }

        private static void AddMeasured(WorldManifest manifest, string edgeId,
            string sourceId, string targetId,
            Dictionary<string, RigidPoseData> groundTruth,
            PoseGraphConstraintKind kind = PoseGraphConstraintKind.Tracking)
        {
            RigidPoseData measurement = groundTruth[sourceId].Inverse() *
                                        groundTruth[targetId];
            manifest.edges.Add(MakeEdge(edgeId, sourceId, targetId, kind,
                measurement, 0.95f, DefaultCovariance(), "synthetic-ground-truth"));
        }

        private static PoseGraphEdgeRecord MakeEdge(string edgeId, string sourceId,
            string targetId, PoseGraphConstraintKind kind, RigidPoseData measurement,
            float confidence, float[] covariance, string provenance)
        {
            Assert.That(PoseGraphConstraintFactory.TryCreate(edgeId, sourceId,
                targetId, kind, measurement, confidence, covariance, 1_900,
                provenance, out PoseGraphEdgeRecord edge, out string error),
                Is.True, error);
            return edge;
        }

        private static ChunkRecord Chunk(string id, RigidPoseData pose, long created)
        {
            return new ChunkRecord
            {
                chunkId = id,
                revision = 2,
                state = ChunkLifecycleState.Persisted,
                worldFromChunk = pose,
                localBounds = new BoundsData(new Vector3(0.1f, 0.2f, 0.3f),
                    new Vector3(6.4f, 6.4f, 6.4f)),
                anchorId = "anchor-root",
                createdUnixMilliseconds = created,
                updatedUnixMilliseconds = 2_000,
                quality = 0.65f,
                artifacts = new List<ChunkArtifactRecord>
                {
                    new()
                    {
                        kind = ChunkArtifactKind.PrismMeshlets,
                        formatVersion = 1,
                        chunkRevision = 2,
                        relativePath = $"chunks/{id}/mesh.bin",
                        sha256 = ArtifactHash,
                        byteLength = 1234
                    }
                }
            };
        }

        private static RigidPoseData Pose(float x, float yawDegrees, float z)
        {
            return new RigidPoseData(new Vector3(x, 0f, z),
                Quaternion.Euler(0f, yawDegrees, 0f));
        }

        private static float[] DefaultCovariance()
        {
            return new[] { 0.01f, 0.01f, 0.01f, 0.02f, 0.02f, 0.02f };
        }

        private static PoseGraphOptimizationSettings Settings()
        {
            return new PoseGraphOptimizationSettings
            {
                MaximumIterations = 200,
                Relaxation = 0.4f,
                TranslationConvergenceMeters = 0.00001f,
                RotationConvergenceDegrees = 0.001f
            };
        }

        private static PoseGraphPoseUpdate FindUpdate(PoseGraphSolution solution,
            string chunkId)
        {
            for (int i = 0; i < solution.Updates.Count; i++)
            {
                if (solution.Updates[i].ChunkId == chunkId)
                    return solution.Updates[i];
            }
            Assert.Fail("Missing pose update for " + chunkId);
            return null;
        }

        private static string[] CaptureStableChunkState(WorldManifest manifest)
        {
            var values = new string[manifest.chunks.Count];
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                ChunkRecord chunk = manifest.chunks[i];
                ChunkArtifactRecord artifact = chunk.artifacts[0];
                values[i] = string.Join("|", chunk.chunkId, chunk.revision, chunk.state,
                    chunk.localBounds.center, chunk.localBounds.extents, chunk.anchorId,
                    chunk.createdUnixMilliseconds, chunk.quality, artifact.kind,
                    artifact.formatVersion, artifact.chunkRevision, artifact.relativePath,
                    artifact.sha256, artifact.byteLength);
            }
            return values;
        }

        private static WorldManifest Clone(WorldManifest manifest)
        {
            string json = Serialize(manifest);
            Assert.That(WorldManifestJson.TryDeserialize(json, out WorldManifest clone,
                out WorldValidationResult validation), Is.True, validation.ToString());
            return clone;
        }

        private static string Serialize(WorldManifest manifest)
        {
            Assert.That(WorldManifestJson.TrySerialize(manifest, false,
                out string json, out WorldValidationResult validation), Is.True,
                validation.ToString());
            return json;
        }

        private static void AssertPose(RigidPoseData actual, RigidPoseData expected,
            float positionTolerance, float rotationToleranceDegrees)
        {
            Assert.That(Vector3.Distance(actual.position, expected.position),
                Is.LessThanOrEqualTo(positionTolerance));
            Assert.That(Quaternion.Angle(actual.rotation, expected.rotation),
                Is.LessThanOrEqualTo(rotationToleranceDegrees));
        }
    }
}
