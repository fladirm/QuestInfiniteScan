using System;
using System.IO;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SubmapRolloverTests
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
            if (!string.IsNullOrEmpty(_root) && Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void BoundaryRolloverPreservesOverlapAndDoesNotOscillate()
        {
            var store = new WorldStore(_root);
            var initialPose = new RigidPoseData(new Vector3(10f, 2f, -4f),
                Quaternion.Euler(0f, 90f, 0f));
            Assert.That(WorldSessionFactory.TryCreate(store, "world-rollover", "Rollover",
                initialPose, new BoundsData(Vector3.zero, Vector3.one * 6.4f), 1_000,
                out WorldManifest manifest, out string createError), Is.True, createError);
            SubmapRolloverSettings settings = CreateSettings();
            Assert.That(SubmapRolloverController.TryCreate(manifest, settings,
                out SubmapRolloverController controller, out string controllerError), Is.True,
                controllerError);

            Vector3 insideWorld = initialPose.TransformPoint(new Vector3(5.0f, 0f, 0f));
            Assert.That(controller.TryObserveCamera(insideWorld, 1_100, out _, out _), Is.False);

            Vector3 boundaryWorld = initialPose.TransformPoint(new Vector3(5.5f, 0f, 0f));
            Assert.That(controller.TryObserveCamera(boundaryWorld, 1_200,
                out SubmapRolloverRequest request, out string requestError), Is.True,
                requestError);
            Assert.That(request.BoundaryAxis, Is.EqualTo(0));
            Assert.That(request.BoundaryDirection, Is.EqualTo(1));
            Assert.That(request.SourceFromTarget.position.x, Is.EqualTo(10.8f).Within(0.0001f));
            Vector3 expectedTargetOrigin = initialPose.TransformPoint(new Vector3(10.8f, 0f, 0f));
            Assert.That(Vector3.Distance(request.WorldFromTarget.position, expectedTargetOrigin),
                Is.LessThan(0.0001f));

            Assert.That(controller.TryCommitPending(store, 1_300, out ChunkRecord target,
                out string commitError), Is.True, commitError);
            Assert.That(controller.ResidentVolumeCount, Is.EqualTo(1));
            Assert.That(controller.Manifest.chunks[0].state,
                Is.EqualTo(ChunkLifecycleState.Persisted));
            Assert.That(target.state, Is.EqualTo(ChunkLifecycleState.Active));
            Assert.That(controller.Manifest.edges[0].sourceFromTarget.position.x,
                Is.EqualTo(10.8f).Within(0.0001f));

            Vector3 stillNearOverlap = target.worldFromChunk.TransformPoint(
                new Vector3(-5.5f, 0f, 0f));
            Assert.That(controller.TryObserveCamera(stillNearOverlap, 2_400, out _, out _),
                Is.False, "unarmed controller must not immediately roll back in overlap");
            Assert.That(controller.IsArmed, Is.False);

            Vector3 rearmWorld = target.worldFromChunk.TransformPoint(Vector3.zero);
            Assert.That(controller.TryObserveCamera(rearmWorld, 2_500, out _, out _), Is.False);
            Assert.That(controller.IsArmed, Is.True);

            Vector3 nominalReversePlane = target.worldFromChunk.TransformPoint(
                new Vector3(-5.5f, 0f, 0f));
            Assert.That(controller.TryObserveCamera(nominalReversePlane, 2_550,
                    out _, out _), Is.False,
                "re-arming must not erase the A<->B Schmitt band");

            Vector3 nextBoundary = target.worldFromChunk.TransformPoint(
                new Vector3(0f, 0f, 5.5f));
            Assert.That(controller.TryObserveCamera(nextBoundary, 2_600,
                out SubmapRolloverRequest next, out string nextError), Is.True, nextError);
            Assert.That(next.BoundaryAxis, Is.EqualTo(2));
        }

        [Test]
        public void EmergencyBoundaryAllowsSafeReverseBeforeRearm()
        {
            SubmapRolloverController controller = CreateController(out WorldStore store);
            RigidPoseData firstPose = controller.ActiveChunk.worldFromChunk;
            Vector3 positiveBoundary = firstPose.TransformPoint(new Vector3(5.5f, 0f, 0f));
            Assert.That(controller.TryObserveCamera(positiveBoundary, 1_100,
                out _, out _), Is.True);
            Assert.That(controller.TryCommitPending(store, 1_200, out ChunkRecord target,
                out string commitError), Is.True, commitError);

            Vector3 emergencyReverse = target.worldFromChunk.TransformPoint(
                new Vector3(-6.25f, 0f, 0f));
            Assert.That(controller.TryObserveCamera(emergencyReverse, 1_250,
                out SubmapRolloverRequest reverse, out string error), Is.True, error);
            Assert.That(reverse.BoundaryAxis, Is.EqualTo(0));
            Assert.That(reverse.BoundaryDirection, Is.EqualTo(-1));
            Assert.That(reverse.IsRevisit, Is.True);
            Assert.That(reverse.TargetChunkId, Is.EqualTo("chunk-000000"));

            Assert.That(controller.TryCommitPending(store, 1_300,
                out ChunkRecord revisited, out string revisitError), Is.True, revisitError);
            Assert.That(revisited.chunkId, Is.EqualTo("chunk-000000"));
            Assert.That(revisited.state, Is.EqualTo(ChunkLifecycleState.Active));
            Assert.That(controller.Manifest.chunks.Count, Is.EqualTo(2),
                "returning through an overlap must not duplicate an existing chunk");
            Assert.That(controller.Manifest.edges[^1].kind,
                Is.EqualTo(PoseGraphConstraintKind.Overlap));
        }

        [Test]
        public void RealtimeCommitLeavesSourceFinalizingAndActivatesTargetImmediately()
        {
            SubmapRolloverController controller = CreateController(out WorldStore store);
            Vector3 boundary = controller.ActiveChunk.worldFromChunk.TransformPoint(
                new Vector3(5.5f, 0f, 0f));
            Assert.That(controller.TryObserveCamera(boundary, 1_100,
                out SubmapRolloverRequest request, out string observeError), Is.True,
                observeError);
            ChunkRecord source = controller.ActiveChunk;

            Assert.That(controller.TryCommitPending(store, 1_200,
                ChunkLifecycleState.Finalizing, out ChunkRecord target,
                out string commitError), Is.True, commitError);

            Assert.That(request.SourceChunkId, Is.EqualTo(source.chunkId));
            Assert.That(source.state, Is.EqualTo(ChunkLifecycleState.Finalizing));
            Assert.That(target.state, Is.EqualTo(ChunkLifecycleState.Active));
            Assert.That(controller.ActiveChunk, Is.SameAs(target));
        }

        [Test]
        public void RepeatedRolloverKeepsOneResidentVolumeAndMonotonicGraph()
        {
            SubmapRolloverController controller = CreateController(out WorldStore store);
            long time = 2_000;
            for (int i = 0; i < 32; i++)
            {
                Vector3 center = controller.ActiveChunk.worldFromChunk.TransformPoint(Vector3.zero);
                Assert.That(controller.TryObserveCamera(center, time++, out _, out _), Is.False);
                Vector3 boundary = controller.ActiveChunk.worldFromChunk.TransformPoint(
                    new Vector3(5.5f, 0f, 0f));
                Assert.That(controller.TryObserveCamera(boundary, time += 1_100,
                    out _, out string observeError), Is.True, observeError);
                Assert.That(controller.TryCommitPending(store, ++time, out _,
                    out string commitError), Is.True, commitError);
                Assert.That(controller.ResidentVolumeCount,
                    Is.LessThanOrEqualTo(controller.MaximumResidentVolumeCount));
            }

            Assert.That(controller.Manifest.chunks.Count, Is.EqualTo(33));
            Assert.That(controller.Manifest.edges.Count, Is.EqualTo(32));
            Assert.That(controller.Manifest.revision, Is.EqualTo(32));
            Assert.That(store.TryLoadManifest(controller.Manifest.worldId,
                out WorldManifest persisted, out _, out _), Is.True);
            Assert.That(persisted.chunks.Count, Is.EqualTo(33));
        }

        [Test]
        public void TwentyAlternatingRevisitsKeepBothChunksRecoverable()
        {
            SubmapRolloverController controller = CreateController(out WorldStore store);
            long time = 10_000;
            for (int transition = 0; transition < 20; transition++)
            {
                Vector3 center = controller.ActiveChunk.worldFromChunk.TransformPoint(
                    Vector3.zero);
                Assert.That(controller.TryObserveCamera(center, time += 1_100,
                    out _, out string centerError), Is.False, centerError);
                float direction = transition % 2 == 0 ? 1f : -1f;
                Vector3 crossing = controller.ActiveChunk.worldFromChunk.TransformPoint(
                    new Vector3(direction * 6.25f, 0f, 0f));
                Assert.That(controller.TryObserveCamera(crossing, time += 1_100,
                    out SubmapRolloverRequest request, out string observeError),
                    Is.True, observeError);
                if (transition >= 1)
                    Assert.That(request.IsRevisit, Is.True,
                        $"transition {transition} must revisit the existing peer");
                Assert.That(controller.TryCommitPending(store, ++time,
                    out ChunkRecord active, out string commitError), Is.True,
                    commitError);
                Assert.That(active.state, Is.EqualTo(ChunkLifecycleState.Active));
                Assert.That(controller.Manifest.chunks,
                    Has.None.Matches<ChunkRecord>(chunk =>
                        chunk.state == ChunkLifecycleState.Finalizing));
                Assert.That(controller.ResidentVolumeCount, Is.EqualTo(1));
            }

            Assert.That(controller.Manifest.chunks.Count, Is.EqualTo(2));
            Assert.That(controller.Manifest.edges.Count, Is.EqualTo(20));
            Assert.That(store.TryLoadManifest(controller.Manifest.worldId,
                out WorldManifest persisted, out _, out string loadError), Is.True,
                loadError);
            Assert.That(persisted.chunks.Count, Is.EqualTo(2));
            Assert.That(persisted.chunks,
                Has.None.Matches<ChunkRecord>(chunk =>
                    chunk.state == ChunkLifecycleState.Finalizing));
        }

        [Test]
        public void WorldFactoryNeverOverwritesAnExistingWorld()
        {
            var store = new WorldStore(_root);
            BoundsData bounds = new(Vector3.zero, Vector3.one * 6.4f);
            Assert.That(WorldSessionFactory.TryCreate(store, "world-existing", "First",
                RigidPoseData.Identity, bounds, 1_000, out _, out _), Is.True);
            Assert.That(WorldSessionFactory.TryCreate(store, "world-existing", "Second",
                RigidPoseData.Identity, bounds, 2_000, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("already exists"));
            Assert.That(store.TryLoadManifest("world-existing", out WorldManifest loaded,
                out _, out _), Is.True);
            Assert.That(loaded.displayName, Is.EqualTo("First"));
        }

        private SubmapRolloverController CreateController(out WorldStore store)
        {
            store = new WorldStore(_root);
            Assert.That(WorldSessionFactory.TryCreate(store, "world-test", "Test",
                RigidPoseData.Identity,
                new BoundsData(Vector3.zero, Vector3.one * 6.4f), 1_000,
                out WorldManifest manifest, out string createError), Is.True, createError);
            Assert.That(SubmapRolloverController.TryCreate(manifest, CreateSettings(),
                out SubmapRolloverController controller, out string error), Is.True, error);
            return controller;
        }

        private static SubmapRolloverSettings CreateSettings()
        {
            return new SubmapRolloverSettings
            {
                boundaryMarginMeters = 1.0f,
                overlapMeters = 2.0f,
                rearmHysteresisMeters = 0.75f,
                emergencyBoundaryMarginMeters = 0.2f,
                cooldownMilliseconds = 1_000,
                maximumResidentChunkMeshes = 3
            };
        }
    }
}
