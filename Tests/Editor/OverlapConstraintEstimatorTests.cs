using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.Prism;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class OverlapConstraintEstimatorTests
    {
        [Test]
        public async Task PointToPlaneIcpRecoversSixDofCornerAndProducesAuditableEdge()
        {
            OverlapRegistrationRequest request = CreateCornerRequest(
                out RigidPoseData expectedSourceFromTarget);
            var estimator = new PointToPlaneIcpEstimator(Settings());

            OverlapConstraintEstimate estimate = await estimator.EstimateAsync(request,
                CancellationToken.None);

            Assert.That(estimate.Succeeded, Is.True, estimate.FailureReason);
            Assert.That(Vector3.Distance(estimate.SourceFromTarget.position,
                expectedSourceFromTarget.position), Is.LessThan(0.012f));
            Assert.That(Quaternion.Angle(estimate.SourceFromTarget.rotation,
                expectedSourceFromTarget.rotation), Is.LessThan(0.8f));
            Assert.That(estimate.CorrespondenceCount, Is.GreaterThanOrEqualTo(48));
            Assert.That(estimate.RmsMeters, Is.LessThan(0.015f));
            Assert.That(estimate.Confidence, Is.InRange(0.05f, 1f));
            Assert.That(estimate.CovarianceDiagonal, Has.Length.EqualTo(6));
            Assert.That(estimate.CovarianceDiagonal,
                Has.All.GreaterThan(0f).And.LessThanOrEqualTo(100f));
            Assert.That(estimate.Provenance, Does.StartWith("point-to-plane-icp/v1;"));
            Assert.That(estimate.Provenance, Does.Contain("sourceRevision=7"));

            Assert.That(PoseGraphConstraintFactory.TryCreateFromEstimate("edge-icp-0001",
                request, estimate, out PoseGraphEdgeRecord edge, out string edgeError),
                Is.True, edgeError);
            Assert.That(edge.kind, Is.EqualTo(PoseGraphConstraintKind.Icp));
            Assert.That(edge.sourceChunkId, Is.EqualTo("chunk-source"));
            Assert.That(edge.targetChunkId, Is.EqualTo("chunk-target"));
            Assert.That(edge.confidence, Is.EqualTo(estimate.Confidence));
            Assert.That(edge.covarianceDiagonal, Is.EqualTo(estimate.CovarianceDiagonal));
            Assert.That(edge.provenance, Is.EqualTo(estimate.Provenance));
        }

        [Test]
        public async Task PointToPlaneIcpIsDeterministicForIdenticalBoundedInput()
        {
            OverlapRegistrationRequest request = CreateCornerRequest(out _);
            var estimator = new PointToPlaneIcpEstimator(Settings());

            OverlapConstraintEstimate first = await estimator.EstimateAsync(request,
                CancellationToken.None);
            OverlapConstraintEstimate second = await estimator.EstimateAsync(request,
                CancellationToken.None);

            Assert.That(first.Succeeded, Is.True, first.FailureReason);
            Assert.That(second.Succeeded, Is.True, second.FailureReason);
            Assert.That(first.SourceFromTarget.position,
                Is.EqualTo(second.SourceFromTarget.position));
            Assert.That(first.SourceFromTarget.rotation,
                Is.EqualTo(second.SourceFromTarget.rotation));
            Assert.That(first.CovarianceDiagonal,
                Is.EqualTo(second.CovarianceDiagonal));
            Assert.That(first.CorrespondenceCount,
                Is.EqualTo(second.CorrespondenceCount));
            Assert.That(first.Provenance, Is.EqualTo(second.Provenance));
        }

        [Test]
        public async Task InsufficientOrNormalIncompatibleOverlapFailsClosed()
        {
            CreateCornerClouds(RigidPoseData.Identity,
                out OverlapPointCloud source, out OverlapPointCloud target);
            var farPoints = new List<Vector3>();
            var farNormals = new List<Vector3>();
            for (int i = 0; i < target.Count; i++)
            {
                farPoints.Add(new Vector3(20f + i * 0.001f, 20f, 20f));
                farNormals.Add(Vector3.right);
            }
            Assert.That(OverlapPointCloud.TryCreate(farPoints, farNormals,
                out OverlapPointCloud farTarget, out string cloudError),
                Is.True, cloudError);
            Assert.That(OverlapRegistrationRequest.TryCreate("chunk-source", 1,
                source, "chunk-far", 1, farTarget, RigidPoseData.Identity, 5_000,
                out OverlapRegistrationRequest farRequest, out string requestError),
                Is.True, requestError);

            var estimator = new PointToPlaneIcpEstimator(Settings());
            OverlapConstraintEstimate result = await estimator.EstimateAsync(farRequest,
                CancellationToken.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Does.Contain("too few"));

            var points = new List<Vector3>();
            var oppositeNormals = new List<Vector3>();
            for (int i = 0; i < source.Count; i++)
            {
                points.Add(source.PointAt(i));
                oppositeNormals.Add(-source.NormalAt(i));
            }
            Assert.That(OverlapPointCloud.TryCreate(points, oppositeNormals,
                out OverlapPointCloud opposite, out cloudError), Is.True, cloudError);
            Assert.That(OverlapRegistrationRequest.TryCreate("chunk-source", 1,
                source, "chunk-opposite", 1, opposite, RigidPoseData.Identity, 5_000,
                out OverlapRegistrationRequest oppositeRequest, out requestError),
                Is.True, requestError);
            result = await estimator.EstimateAsync(oppositeRequest,
                CancellationToken.None);
            Assert.That(result.Succeeded, Is.False,
                "opposite-facing surfaces must not become ICP correspondences");
        }

        [Test]
        public async Task CancellationDoesNotPublishPartialEstimate()
        {
            OverlapRegistrationRequest request = CreateCornerRequest(out _);
            var estimator = new PointToPlaneIcpEstimator(Settings());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            bool cancelled = false;
            try
            {
                await estimator.EstimateAsync(request, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            Assert.That(cancelled, Is.True);
        }

        [Test]
        public void ConstraintFactoryRequiresPositiveCovarianceConfidenceAndProvenance()
        {
            float[] covariance = { 0.01f, 0.01f, 0.01f, 0.02f, 0.02f, 0.02f };
            Assert.That(PoseGraphConstraintFactory.TryCreate("edge-valid",
                "chunk-source", "chunk-target", PoseGraphConstraintKind.Overlap,
                RigidPoseData.Identity, 0.8f, covariance, 1_000, "unit-test",
                out PoseGraphEdgeRecord edge, out string validError),
                Is.True, validError);
            covariance[0] = 99f;
            Assert.That(edge.covarianceDiagonal[0], Is.EqualTo(0.01f),
                "stored uncertainty must not alias caller-owned mutable memory");

            Assert.That(PoseGraphConstraintFactory.TryCreate("edge-no-covariance",
                "chunk-source", "chunk-target", PoseGraphConstraintKind.Overlap,
                RigidPoseData.Identity, 0.8f, Array.Empty<float>(), 1_000, "unit-test",
                out _, out _), Is.False);
            Assert.That(PoseGraphConstraintFactory.TryCreate("edge-no-origin",
                "chunk-source", "chunk-target", PoseGraphConstraintKind.Overlap,
                RigidPoseData.Identity, 0.8f,
                new[] { 1f, 1f, 1f, 1f, 1f, 1f }, 1_000, " ",
                out _, out _), Is.False);
            Assert.That(PoseGraphConstraintFactory.TryCreate("edge-no-confidence",
                "chunk-source", "chunk-target", PoseGraphConstraintKind.Overlap,
                RigidPoseData.Identity, 0f,
                new[] { 1f, 1f, 1f, 1f, 1f, 1f }, 1_000, "unit-test",
                out _, out _), Is.False);
        }

        [Test]
        public async Task NoneBackendIsExplicitlyNonPublishing()
        {
            OverlapRegistrationRequest request = CreateCornerRequest(out _);
            IOverlapConstraintEstimator estimator =
                new NoneOverlapConstraintEstimator();

            OverlapConstraintEstimate estimate = await estimator.EstimateAsync(request,
                CancellationToken.None);

            Assert.That(estimate.Succeeded, Is.False);
            Assert.That(estimate.FailureReason, Does.Contain("disabled"));
            Assert.That(PoseGraphConstraintFactory.TryCreateFromEstimate("edge-disabled",
                request, estimate, out _, out _), Is.False);
        }

        [Test]
        public void ContactMeshletSnapshotConversionIsBoundedAndDeterministic()
        {
            ContactMeshSnapshot snapshot = CreateLiveMeshSnapshot(200);

            Assert.That(OverlapPointCloudBuilder.TryCreate(snapshot, 64,
                out OverlapPointCloud first, out string firstError), Is.True, firstError);
            Assert.That(OverlapPointCloudBuilder.TryCreate(snapshot, 64,
                out OverlapPointCloud second, out string secondError), Is.True, secondError);

            Assert.That(first.Count, Is.EqualTo(64));
            Assert.That(second.Count, Is.EqualTo(first.Count));
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(second.PointAt(i), Is.EqualTo(first.PointAt(i)));
                Assert.That(second.NormalAt(i), Is.EqualTo(first.NormalAt(i)));
            }
        }

        [Test]
        public async Task CoordinatorDurablyCommitsIcpAndPublishesOptimizedTransforms()
        {
            string root = Path.Combine(Path.GetTempPath(), "QuestInfiniteScanTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                WorldManifest manifest = CreateCoordinatorManifest();
                var store = new WorldStore(root);
                Assert.That(store.TryCommitManifest(manifest, out string createError),
                    Is.True, createError);
                OverlapPointCloud cloud = CreateSmallCloud();
                Assert.That(OverlapRegistrationRequest.TryCreate("chunk-source", 0,
                    cloud, "chunk-target", 0, cloud,
                    new RigidPoseData(new Vector3(2.5f, 0f, 0f),
                        Quaternion.identity), 2_100,
                    out OverlapRegistrationRequest request, out string requestError),
                    Is.True, requestError);
                var estimate = OverlapConstraintEstimate.Success(
                    new RigidPoseData(new Vector3(2f, 0f, 0f), Quaternion.identity),
                    1f,
                    new[] { 0.0004f, 0.0004f, 0.0004f,
                        0.001f, 0.001f, 0.001f },
                    64, 0.002f, 5, true,
                    "point-to-plane-icp/test;inliers=64/64");
                using var coordinator = new PoseGraphRefinementCoordinator(
                    new FixedEstimator(estimate), new PoseGraphOptimizationSettings
                    {
                        MaximumIterations = 120,
                        Relaxation = 0.4f,
                        TranslationConvergenceMeters = 0.00001f,
                        RotationConvergenceDegrees = 0.001f
                    });

                PoseGraphRefinementResult result = await coordinator.RefineAsync(
                    manifest, store, request, 2_200, CancellationToken.None);

                Assert.That(result.Succeeded, Is.True, result.Error);
                Assert.That(result.Edge.kind, Is.EqualTo(PoseGraphConstraintKind.Icp));
                Assert.That(result.Edge.covarianceDiagonal, Has.Length.EqualTo(6));
                Assert.That(result.Edge.provenance, Does.Contain("point-to-plane-icp"));
                Assert.That(manifest.edges.Count, Is.EqualTo(2));
                Assert.That(manifest.chunks[1].worldFromChunk.position.x,
                    Is.EqualTo(2f).Within(0.02f));
                Assert.That(store.TryLoadManifest(manifest.worldId,
                    out WorldManifest durable, out _, out string loadError),
                    Is.True, loadError);
                Assert.That(durable.edges.Count, Is.EqualTo(2));
                Assert.That(durable.chunks[1].worldFromChunk.position.x,
                    Is.EqualTo(manifest.chunks[1].worldFromChunk.position.x)
                        .Within(0.00001f));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Test]
        public void InvalidConstraintCommitRollsBackManifestExactly()
        {
            WorldManifest manifest = CreateCoordinatorManifest();
            Assert.That(WorldManifestJson.TrySerialize(manifest, false,
                out string before, out WorldValidationResult beforeValidation),
                Is.True, beforeValidation.ToString());
            Assert.That(PoseGraphConstraintFactory.TryCreate("edge-missing-target",
                "chunk-source", "chunk-missing", PoseGraphConstraintKind.Icp,
                RigidPoseData.Identity, 1f,
                new[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f },
                2_000, "invalid-reference-test", out PoseGraphEdgeRecord edge,
                out string edgeError), Is.True, edgeError);

            Assert.That(PoseGraphConstraintCommitter.TryAppend(manifest, edge, null,
                2_100, out _, out string commitError), Is.False);

            Assert.That(commitError, Does.Contain("does not reference a chunk"));
            Assert.That(WorldManifestJson.TrySerialize(manifest, false,
                out string after, out WorldValidationResult afterValidation),
                Is.True, afterValidation.ToString());
            Assert.That(after, Is.EqualTo(before));
        }

        private static OverlapRegistrationRequest CreateCornerRequest(
            out RigidPoseData expectedSourceFromTarget)
        {
            expectedSourceFromTarget = new RigidPoseData(
                new Vector3(0.08f, -0.04f, 0.06f),
                Quaternion.Euler(2f, -3f, 1f));
            CreateCornerClouds(expectedSourceFromTarget,
                out OverlapPointCloud source, out OverlapPointCloud target);
            var initialDelta = new RigidPoseData(
                new Vector3(0.025f, -0.018f, 0.02f),
                Quaternion.Euler(0.8f, -0.9f, 0.6f));
            RigidPoseData initial = initialDelta * expectedSourceFromTarget;
            Assert.That(OverlapRegistrationRequest.TryCreate("chunk-source", 7,
                source, "chunk-target", 9, target, initial, 12_345,
                out OverlapRegistrationRequest request, out string error),
                Is.True, error);
            return request;
        }

        private static void CreateCornerClouds(RigidPoseData sourceFromTarget,
            out OverlapPointCloud sourceCloud, out OverlapPointCloud targetCloud)
        {
            var sourcePoints = new List<Vector3>();
            var sourceNormals = new List<Vector3>();
            const int side = 12;
            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                float a = 0.15f + x * 0.075f;
                float b = 0.15f + y * 0.075f;
                sourcePoints.Add(new Vector3(0f, a, b));
                sourceNormals.Add(Vector3.right);
                sourcePoints.Add(new Vector3(a, 0f, b));
                sourceNormals.Add(Vector3.up);
                sourcePoints.Add(new Vector3(a, b, 0f));
                sourceNormals.Add(Vector3.forward);
            }
            RigidPoseData targetFromSource = sourceFromTarget.Inverse();
            var targetPoints = new List<Vector3>(sourcePoints.Count);
            var targetNormals = new List<Vector3>(sourceNormals.Count);
            for (int i = 0; i < sourcePoints.Count; i++)
            {
                targetPoints.Add(targetFromSource.TransformPoint(sourcePoints[i]));
                targetNormals.Add(targetFromSource.rotation * sourceNormals[i]);
            }
            Assert.That(OverlapPointCloud.TryCreate(sourcePoints, sourceNormals,
                out sourceCloud, out string sourceError), Is.True, sourceError);
            Assert.That(OverlapPointCloud.TryCreate(targetPoints, targetNormals,
                out targetCloud, out string targetError), Is.True, targetError);
        }

        private static PointToPlaneIcpSettings Settings()
        {
            return new PointToPlaneIcpSettings
            {
                MaximumSamples = 512,
                MaximumIterations = 40,
                MinimumCorrespondences = 48,
                MinimumInlierRatio = 0.2f,
                MaximumCorrespondenceDistanceMeters = 0.25f,
                MaximumNormalAngleDegrees = 35f,
                HuberDistanceMeters = 0.025f,
                MaximumAcceptedRmsMeters = 0.04f,
                MinimumConfidence = 0.05f,
                MaximumTranslationStepMeters = 0.08f,
                MaximumRotationStepDegrees = 4f,
                TranslationConvergenceMeters = 0.00001f,
                RotationConvergenceDegrees = 0.001f
            };
        }

        private static ContactMeshSnapshot CreateLiveMeshSnapshot(int vertexCount)
        {
            var vertices = new byte[vertexCount * ContactMeshletVertexGpu.Stride];
            for (int i = 0; i < vertexCount; i++)
            {
                int offset = i * ContactMeshletVertexGpu.Stride;
                WriteFloat(vertices, offset, i * 0.01f);
                WriteFloat(vertices, offset + 4, i % 7 * 0.02f);
                WriteFloat(vertices, offset + 8, i % 11 * 0.03f);
                WriteFloat(vertices, offset + 16, 0f);
                WriteFloat(vertices, offset + 20, 1f);
                WriteFloat(vertices, offset + 24, 0f);
            }
            return new ContactMeshSnapshot
            {
                VertexCount = vertexCount,
                VertexBytes = vertices
            };
        }

        private static void WriteFloat(byte[] destination, int offset, float value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, destination, offset,
                sizeof(float));
        }

        private static OverlapPointCloud CreateSmallCloud()
        {
            var points = new Vector3[64];
            var normals = new Vector3[64];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new Vector3(i % 8 * 0.1f, i / 8 * 0.1f, 0f);
                normals[i] = Vector3.forward;
            }
            Assert.That(OverlapPointCloud.TryCreate(points, normals,
                out OverlapPointCloud cloud, out string error), Is.True, error);
            return cloud;
        }

        private static WorldManifest CreateCoordinatorManifest()
        {
            var source = new ChunkRecord
            {
                chunkId = "chunk-source",
                state = ChunkLifecycleState.Persisted,
                worldFromChunk = RigidPoseData.Identity,
                localBounds = new BoundsData(Vector3.zero, Vector3.one * 6.4f),
                createdUnixMilliseconds = 1_000,
                updatedUnixMilliseconds = 2_000,
                quality = 0.5f,
                artifacts = new List<ChunkArtifactRecord>()
            };
            var target = new ChunkRecord
            {
                chunkId = "chunk-target",
                state = ChunkLifecycleState.Active,
                worldFromChunk = new RigidPoseData(new Vector3(2.5f, 0f, 0f),
                    Quaternion.identity),
                localBounds = new BoundsData(Vector3.zero, Vector3.one * 6.4f),
                createdUnixMilliseconds = 1_010,
                updatedUnixMilliseconds = 2_000,
                quality = 0.5f,
                artifacts = new List<ChunkArtifactRecord>()
            };
            Assert.That(PoseGraphConstraintFactory.TryCreate("edge-tracking",
                source.chunkId, target.chunkId, PoseGraphConstraintKind.Tracking,
                target.worldFromChunk, 0.1f,
                new[] { 0.25f, 0.25f, 0.25f, 0.5f, 0.5f, 0.5f },
                1_900, "weak-tracking", out PoseGraphEdgeRecord tracking,
                out string edgeError), Is.True, edgeError);
            return new WorldManifest
            {
                worldId = "coordinator-world",
                displayName = "Coordinator",
                createdUnixMilliseconds = 1_000,
                updatedUnixMilliseconds = 2_000,
                revision = 0,
                chunks = new List<ChunkRecord> { source, target },
                edges = new List<PoseGraphEdgeRecord> { tracking }
            };
        }

        private sealed class FixedEstimator : IOverlapConstraintEstimator
        {
            private readonly OverlapConstraintEstimate _estimate;

            internal FixedEstimator(OverlapConstraintEstimate estimate)
            {
                _estimate = estimate;
            }

            public Task<OverlapConstraintEstimate> EstimateAsync(
                OverlapRegistrationRequest request,
                CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return Task.FromCanceled<OverlapConstraintEstimate>(
                        cancellationToken);
                return Task.FromResult(_estimate);
            }
        }
    }
}
