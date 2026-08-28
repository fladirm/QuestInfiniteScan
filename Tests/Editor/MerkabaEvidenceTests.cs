using System.Collections.Generic;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaEvidenceTests
    {
        private static readonly Color32 Red = new(255, 16, 8, 255);
        private static readonly Color32 Blue = new(8, 32, 255, 255);

        [Test]
        public void RepeatedSurface_StrengthensStabilizesAndConvergesRgb()
        {
            KernelState state = default;
            MerkabaObservationInput weakRed = SurfaceInput(2f, 2.005f, 0.55f, 4f);
            MerkabaObservationInput strongBlue = SurfaceInput(1f, 1.005f, 1f, 5f);

            for (int i = 0; i < 16; i++)
                MerkabaIntegrator.IntegrateObservation(ref state, weakRed, Red);
            int evidenceAfterWeak = state.OccupancyEvidence;
            uint confidenceAfterWeak = state.ColorConfidence;
            Assert.That(state.IsOccupied, Is.True);

            for (int i = 0; i < 24; i++)
                MerkabaIntegrator.IntegrateObservation(ref state, strongBlue, Blue);

            Assert.That(state.OccupancyEvidence, Is.GreaterThan(evidenceAfterWeak));
            Assert.That(state.ColorConfidence, Is.GreaterThan(confidenceAfterWeak));
            Assert.That(state.IsOccupied, Is.True, "topology must remain stable");
            Assert.That(state.Color.b, Is.GreaterThan(state.Color.r),
                "later better observations must correct the earlier weak colour");
        }

        [Test]
        public void FalseSurfaceCarve_UsesRealClassificationAndCrossesEmptyThreshold()
        {
            KernelState state = default;
            MerkabaObservationResult hit = MerkabaIntegrator.IntegrateObservation(
                ref state, SurfaceInput(0.5f, 0.5f, 1f, 5f), Red);
            Assert.That(hit.Kind, Is.EqualTo(MerkabaObservationKind.Surface));
            Assert.That(state.IsOccupied, Is.True, "one strong bad hit is temporarily visible");

            int iterations = 0;
            while (state.IsOccupied && iterations++ < 16)
            {
                MerkabaObservationResult clear = MerkabaIntegrator.IntegrateObservation(
                    ref state, FreeInput(0.5f, 1.5f, 1f, 5f), Blue);
                Assert.That(clear.Kind, Is.EqualTo(MerkabaObservationKind.Free));
            }

            Assert.That(iterations, Is.LessThan(16));
            Assert.That(state.IsOccupied, Is.False);
            Assert.That(state.OccupancyEvidence,
                Is.LessThanOrEqualTo(MerkabaConstants.OccupiedOffThreshold));
            Assert.That(state.ColorConfidence, Is.Zero,
                "disproved foreground colour must not bias a later real surface");
        }

        [Test]
        public void FalseForegroundDisappearsWhileTrueWallPersists()
        {
            KernelState foreground = default;
            KernelState wall = default;
            MerkabaIntegrator.IntegrateObservation(ref foreground,
                SurfaceInput(1f, 1f, 1f, 5f), Red);
            for (int i = 0; i < 12; i++)
                MerkabaIntegrator.IntegrateObservation(ref wall,
                    SurfaceInput(2f, 2f, 1f, 5f), Blue);

            for (int i = 0; i < 12; i++)
            {
                MerkabaIntegrator.IntegrateObservation(ref foreground,
                    FreeInput(1f, 2f, 1f, 5f), Red);
                MerkabaIntegrator.IntegrateObservation(ref wall,
                    SurfaceInput(2f, 2f, 1f, 5f), Blue);
            }

            Assert.That(foreground.IsOccupied, Is.False);
            Assert.That(wall.IsOccupied, Is.True);
            Assert.That(wall.OccupancyEvidence, Is.GreaterThan(
                MerkabaConstants.OccupiedOnThreshold));
        }

        [Test]
        public void MultiAngleCorner_PreservesConsistentCornerAndEatsInconsistentArtefacts()
        {
            var corner = new[] { new KernelState(), new KernelState(), new KernelState() };
            KernelState artefact = default;
            for (int pass = 0; pass < 20; pass++)
            {
                int angle = pass % 3;
                for (int i = 0; i < corner.Length; i++)
                    MerkabaIntegrator.IntegrateObservation(ref corner[i],
                        SurfaceInput(1.5f + i * 0.01f, 1.5f + i * 0.01f,
                            i == angle ? 1f : 0.7f, 5f), Blue);

                if (pass == 0)
                    MerkabaIntegrator.IntegrateObservation(ref artefact,
                        SurfaceInput(1f, 1f, 1f, 5f), Red);
                else
                    MerkabaIntegrator.IntegrateObservation(ref artefact,
                        FreeInput(1f, 2f, 0.8f, 5f), Red);
            }

            Assert.That(corner, Has.All.Matches<KernelState>(state => state.IsOccupied));
            Assert.That(artefact.IsOccupied, Is.False);
        }

        [Test]
        public void EvidenceCorrection_IsIdenticalAcrossPositiveAndNegativeChunkBorders()
        {
            GameObject host = new("MerkabaGridFixture");
            try
            {
                MerkabaGrid grid = host.AddComponent<MerkabaGrid>();
                var coords = new[]
                {
                    new int3(31, 0, 0), new int3(32, 0, 0),
                    new int3(-1, 0, 0), new int3(-32, -33, 0)
                };
                var transitions = new Dictionary<int3, int>();
                grid.OccupancyChanged += (coord, _) =>
                    transitions[coord] = transitions.GetValueOrDefault(coord) + 1;

                foreach (int3 coord in coords)
                    grid.ApplyObservation(coord, SurfaceInput(0.5f, 0.5f, 1f, 5f), Red);
                foreach (int3 coord in coords)
                for (int pass = 0; pass < 8; pass++)
                    grid.ApplyObservation(coord, FreeInput(0.5f, 1.5f, 1f, 5f), Blue);

                Assert.That(grid.ActiveChunkCount, Is.GreaterThanOrEqualTo(3));
                Assert.That(grid.OccupiedKernelCount, Is.Zero);
                foreach (int3 coord in coords)
                {
                    Assert.That(grid.TryGetState(coord, out KernelState state), Is.True);
                    Assert.That(state.IsOccupied, Is.False);
                    Assert.That(transitions[coord], Is.EqualTo(2),
                        "each kernel must dirty topology exactly on on/off crossings");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void FreeVolumeSparsity_TransientPagesAndFreeObservationsStayNonCanonical()
        {
            GameObject host = new("MerkabaSparseAllocationFixture");
            GameObject cameraHost = new("MerkabaSparseAllocationCamera");
            try
            {
                MerkabaGrid grid = host.AddComponent<MerkabaGrid>();
                var serialized = new SerializedObject(grid);
                serialized.FindProperty("maxResidentChunks").intValue = 16;
                serialized.FindProperty("maxIntegrationChunks").intValue = 8;
                serialized.FindProperty("maxVisibleChunks").intValue = 8;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Camera camera = cameraHost.AddComponent<Camera>();
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 4f;
                camera.fieldOfView = 90f;
                MerkabaResidencyFrame frame = grid.RefreshResidency(camera, 1f, true);
                Assert.That(frame.IntegrationChunkCount, Is.GreaterThan(0));
                Assert.That(grid.TransientResidentPageCount, Is.GreaterThan(0));
                Assert.That(grid.ActiveChunkCount, Is.Zero,
                    "empty frustum residency must remain a transient GPU cache only");

                grid.Clear();
                for (int x = -96; x <= 96; x += 16)
                    grid.ApplyObservation(new int3(x, 0, 0),
                        FreeInput(0.5f, 2f, 1f, 5f), Blue);
                Assert.That(grid.ActiveChunkCount, Is.Zero,
                    "FREE in never-seen world must not allocate canonical chunks");

                int3 surfaceCoord = new(-33, 2, 7);
                grid.ApplyObservation(surfaceCoord,
                    SurfaceInput(0.5f, 0.5f, 1f, 5f), Red);
                Assert.That(grid.ActiveChunkCount, Is.EqualTo(1));
                for (int pass = 0; pass < 8; pass++)
                    grid.ApplyObservation(surfaceCoord,
                        FreeInput(0.5f, 2f, 1f, 5f), Blue);
                Assert.That(grid.TryGetState(surfaceCoord, out KernelState carved), Is.True);
                Assert.That(carved.IsOccupied, Is.False);
                Assert.That(carved.OccupancyEvidence, Is.LessThan(0),
                    "allocated surface chunks retain useful local known-free evidence");
                Assert.That(grid.ActiveChunkCount, Is.EqualTo(1),
                    "local FREE evidence must not allocate additional air chunks");
            }
            finally
            {
                Object.DestroyImmediate(cameraHost);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RenderResidency_IsIndependentStableAndKeepsTransientPagesVisible()
        {
            GameObject host = new("MerkabaResidencyCadenceFixture");
            GameObject cameraHost = new("MerkabaResidencyCadenceCamera");
            try
            {
                MerkabaGrid grid = host.AddComponent<MerkabaGrid>();
                var serialized = new SerializedObject(grid);
                serialized.FindProperty("maxResidentChunks").intValue = 16;
                serialized.FindProperty("maxIntegrationChunks").intValue = 8;
                serialized.FindProperty("maxVisibleChunks").intValue = 12;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                KernelState occupied = default;
                occupied.SetOccupiedForFixture(true, Red);
                int3 renderOnlyChunk = new(0, 0, 2);
                grid.SetState(MerkabaConstants.ChunkOrigin(renderOnlyChunk), occupied);

                Camera camera = cameraHost.AddComponent<Camera>();
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 5f;
                camera.fieldOfView = 90f;
                MerkabaResidencyFrame integration = grid.RefreshResidency(camera,
                    1f, true);
                Assert.That(integration.IntegrationChunkCount, Is.GreaterThan(0));
                int integrationCount = grid.IntegrationChunkCount;

                grid.RefreshResidency(camera, 2f, false);
                Assert.That(grid.IntegrationChunkCount, Is.EqualTo(integrationCount),
                    "render-cadence refresh must not overwrite the 15 Hz integration set");
                HashSet<int3> first = VisibleChunkCoords(grid);
                Assert.That(first, Does.Contain(renderOnlyChunk),
                    "render distance must publish canonical chunks outside integration range");
                Assert.That(grid.TransientResidentPageCount, Is.GreaterThan(0));

                camera.transform.position += new Vector3(0.05f, 0f, 0f);
                grid.RefreshResidency(camera, 2f, false);
                HashSet<int3> afterSmallMove = VisibleChunkCoords(grid);
                Assert.That(afterSmallMove.SetEquals(first), Is.True,
                    "one-chunk guard/hysteresis must prevent 5 cm page churn");
            }
            finally
            {
                Object.DestroyImmediate(cameraHost);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void InvalidDilationNormalAndBehindDepthRemainUnknownAndNonDestructive()
        {
            KernelState state = default;
            MerkabaIntegrator.IntegrateObservation(ref state,
                SurfaceInput(1f, 1f, 1f, 5f), Red);
            int evidence = state.OccupancyEvidence;

            var behind = new MerkabaObservationInput(true, true, true,
                2f, 1f, 2f, 1f, 1.1f, 1f, 5f);
            var badNormal = SurfaceInput(1f, 1f, 0.1f, 5f);
            var occluded = new MerkabaObservationInput(true, true, true,
                1f, 1f, 3f, 3f, 1f, 1f, 5f);

            Assert.That(MerkabaIntegrator.IntegrateObservation(ref state, behind, Blue).Kind,
                Is.EqualTo(MerkabaObservationKind.Unknown));
            Assert.That(MerkabaIntegrator.IntegrateObservation(ref state, badNormal, Blue).Kind,
                Is.EqualTo(MerkabaObservationKind.Unknown));
            Assert.That(MerkabaIntegrator.IntegrateObservation(ref state, occluded, Blue).Kind,
                Is.EqualTo(MerkabaObservationKind.Unknown));
            Assert.That(state.OccupancyEvidence, Is.EqualTo(evidence));
            Assert.That(state.Color.r, Is.GreaterThan(state.Color.b));
        }

        private static MerkabaObservationInput SurfaceInput(float kernelDistance,
            float measuredDistance, float normalFacing, float maxDistance) => new(
                true, true, true, kernelDistance, measuredDistance,
                kernelDistance, measuredDistance, measuredDistance + 0.1f,
                normalFacing, maxDistance);

        private static HashSet<int3> VisibleChunkCoords(MerkabaGrid grid)
        {
            var visible = new int[grid.VisibleSlotsBuffer.count];
            var pages = new int4[grid.PageCoordsBuffer.count];
            grid.VisibleSlotsBuffer.GetData(visible);
            grid.PageCoordsBuffer.GetData(pages);
            var result = new HashSet<int3>();
            for (int i = 0; i < grid.VisibleChunkCount; i++)
                result.Add(pages[visible[i]].xyz);
            return result;
        }

        private static MerkabaObservationInput FreeInput(float kernelDistance,
            float measuredDistance, float normalFacing, float maxDistance) => new(
                true, true, true, kernelDistance, measuredDistance,
                kernelDistance, measuredDistance, measuredDistance + 0.1f,
                normalFacing, maxDistance);
    }
}
