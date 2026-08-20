using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Tests
{
    public sealed class TsdfFusionPolicyTests
    {
        private const float VoxelSize = 0.05f;
        private const float TruncationDistance = 0.15f;
        private const float VoxelMinimum = 0.10f;
        private const float BackBandVoxels = 1.25f;
        private const float MaxDistance = 5f;

        [Test]
        public void NearThenFar_DistantObservationCannotPullStableSurface()
        {
            var voxel = new SyntheticVoxel();
            SeedAndStabilize(voxel, 0f, 1f, 1f, -1f, 30);
            float beforeTsdf = voxel.Tsdf;
            float beforeWeight = voxel.Weight;

            TsdfFusionResult result = voxel.Observe(0.45f, 4f, 1f,
                true, -1f);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Decision, Is.EqualTo(TsdfFusionDecision.LowerQuality));
            Assert.That(voxel.Tsdf, Is.EqualTo(beforeTsdf));
            Assert.That(voxel.Weight, Is.EqualTo(beforeWeight));
        }

        [Test]
        public void FarThenNear_BetterCloseObservationRefinesInsteadOfLocking()
        {
            var voxel = new SyntheticVoxel();
            SeedAndStabilize(voxel, 0.40f, 3.5f, 1f, -1f, 50);
            float farEstimate = voxel.Tsdf;

            TsdfFusionResult firstClose = voxel.Observe(0f, 1f, 1f,
                true, -1f);
            for (int i = 0; i < 20; i++)
                voxel.Observe(0f, 1f, 1f, true, -1f);

            Assert.That(firstClose.Accepted, Is.True);
            Assert.That(firstClose.ObservationQuality, Is.GreaterThan(
                firstClose.ExistingQuality + TsdfFusionParameters.Default.QualityHysteresis));
            Assert.That(Mathf.Abs(voxel.Tsdf), Is.LessThan(Mathf.Abs(farEstimate) * 0.35f));
            Assert.That(voxel.BestQuality, Is.EqualTo(0.8f).Within(0.001f));
        }

        [Test]
        public void GrazingAngle_CannotSeedOrErodeFrontFacingSurface()
        {
            var empty = new SyntheticVoxel();
            TsdfFusionResult seed = empty.Observe(0f, 1f, 0.2f,
                true, -1f);
            Assert.That(seed.Accepted, Is.False);
            Assert.That(seed.Decision,
                Is.EqualTo(TsdfFusionDecision.InsufficientSeedQuality));

            var stable = new SyntheticVoxel();
            SeedAndStabilize(stable, 0f, 1f, 1f, -1f, 30);
            float before = stable.Tsdf;
            TsdfFusionResult grazing = stable.Observe(0.4f, 1f, 0.2f,
                true, -1f);
            Assert.That(grazing.Accepted, Is.False);
            Assert.That(grazing.Decision, Is.EqualTo(TsdfFusionDecision.LowerQuality));
            Assert.That(stable.Tsdf, Is.EqualTo(before));
        }

        [Test]
        public void ObliqueSurface_RepeatedProvisionalSamplesEventuallyBecomeMeshable()
        {
            var voxel = new SyntheticVoxel();
            const float incidence = 0.30f;
            const float distance = 1.5f;

            TsdfFusionResult first = voxel.Observe(0f, distance, incidence,
                true, -1f);

            Assert.That(first.Accepted, Is.True);
            Assert.That(first.Decision, Is.EqualTo(TsdfFusionDecision.Seeded));
            Assert.That(first.Weight, Is.LessThan(0.08f),
                "one weak oblique frame must not immediately create visible geometry");

            for (int i = 0; i < 80 && voxel.Weight < 0.08f; i++)
                voxel.Observe(0f, distance, incidence, true, -1f);

            Assert.That(voxel.Weight, Is.GreaterThanOrEqualTo(0.08f),
                "consistent oblique evidence must be able to fill a wall over time");
        }

        [Test]
        public void LowerQualityConsistentSampleMayConvergeButCannotMoveStableSurface()
        {
            var stable = new SyntheticVoxel();
            SeedAndStabilize(stable, 0f, 1f, 1f, -1f, 30);

            TsdfFusionResult consistent = stable.Observe(0.05f, 3f, 0.8f,
                true, -1f);
            TsdfFusionResult moving = stable.Observe(0.45f, 3f, 0.8f,
                true, -1f);

            Assert.That(consistent.Accepted, Is.True,
                "a lower-quality but geometrically consistent same-side frame should refine");
            Assert.That(moving.Accepted, Is.False);
            Assert.That(moving.Decision, Is.EqualTo(TsdfFusionDecision.LowerQuality));
        }

        [Test]
        public void ThinWall_FrontAndBackStayDistinctAndOppositeFaceCannotOverwrite()
        {
            const float frontSurface = 0f;
            // 8 cm is only 1.6 default voxels: still resolvable as two crossings,
            // while the old 30 cm back-fill would have overwritten it completely.
            const float backSurface = 0.08f;
            float[] positions =
            {
                -0.15f, -0.10f, -0.05f, 0f, 0.05f,
                0.10f, 0.15f, 0.20f, 0.25f
            };
            var voxels = new SyntheticVoxel[positions.Length];
            for (int i = 0; i < voxels.Length; i++)
                voxels[i] = new SyntheticVoxel();

            for (int frame = 0; frame < 30; frame++)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    float raySignedDistance = frontSurface - positions[i];
                    bool visible = IsWithinBackBand(raySignedDistance);
                    voxels[i].Observe(NormalizeTsdf(raySignedDistance), 1f, 1f,
                        visible, -1f);
                }
            }

            float protectedFrontTsdf = voxels[3].Tsdf;
            float protectedInteriorTsdf = voxels[4].Tsdf;
            int oppositeRejections = 0;
            for (int frame = 0; frame < 30; frame++)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    // Camera is now on +X looking toward -X; positive TSDF is on
                    // the room-facing (+X) side of the back surface.
                    float raySignedDistance = positions[i] - backSurface;
                    bool visible = IsWithinBackBand(raySignedDistance);
                    TsdfFusionResult result = voxels[i].Observe(
                        NormalizeTsdf(raySignedDistance), 1f, 1f,
                        visible, +1f);
                    if (result.Decision == TsdfFusionDecision.OppositeSurface)
                        oppositeRejections++;
                }
            }

            Assert.That(oppositeRejections, Is.GreaterThan(0));
            Assert.That(voxels[2].Tsdf, Is.GreaterThan(0f), "front free side");
            Assert.That(Mathf.Abs(voxels[3].Tsdf), Is.LessThan(0.02f),
                "front zero crossing");
            Assert.That(voxels[4].Tsdf, Is.LessThan(0f), "wall interior");
            Assert.That(voxels[5].Tsdf, Is.GreaterThan(0f), "back free side");
            float backCrossing = 0.05f + 0.05f *
                (-voxels[4].Tsdf / (voxels[5].Tsdf - voxels[4].Tsdf));
            Assert.That(backCrossing, Is.InRange(0.065f, 0.10f),
                "back zero crossing must remain separate from the front face");
            Assert.That(voxels[3].Tsdf, Is.EqualTo(protectedFrontTsdf).Within(0.00001f));
            Assert.That(voxels[4].Tsdf, Is.EqualTo(protectedInteriorTsdf).Within(0.00001f),
                "opposite-facing observations must not rewrite the front-face tail");
            Assert.That(voxels[3].SurfaceNormal, Is.EqualTo(-1f));
            Assert.That(voxels[5].SurfaceNormal, Is.EqualTo(+1f));
        }

        [Test]
        public void OccludedObservation_NeverSeedsOrChangesSurface()
        {
            var empty = new SyntheticVoxel();
            TsdfFusionResult emptyResult = empty.Observe(0f, 1f, 1f,
                false, -1f);
            Assert.That(emptyResult.Decision, Is.EqualTo(TsdfFusionDecision.Occluded));
            Assert.That(empty.Weight, Is.Zero);

            var stable = new SyntheticVoxel();
            SeedAndStabilize(stable, 0f, 1f, 1f, -1f, 30);
            float beforeTsdf = stable.Tsdf;
            float beforeWeight = stable.Weight;
            TsdfFusionResult result = stable.Observe(-0.5f, 1f, 1f,
                false, -1f);

            Assert.That(result.Decision, Is.EqualTo(TsdfFusionDecision.Occluded));
            Assert.That(stable.Tsdf, Is.EqualTo(beforeTsdf));
            Assert.That(stable.Weight, Is.EqualTo(beforeWeight));
        }

        [Test]
        public void Revisit_ConsistentSameSideSamplesCanStillImproveStableSurface()
        {
            var voxel = new SyntheticVoxel();
            SeedAndStabilize(voxel, 0f, 1f, 1f, -1f, 30);
            float before = voxel.Tsdf;

            TsdfFusionResult first = default;
            for (int i = 0; i < 20; i++)
            {
                first = voxel.Observe(0.05f, 1f, 1f, true, -1f);
                Assert.That(first.Decision, Is.Not.EqualTo(
                    TsdfFusionDecision.OppositeSurface));
            }

            Assert.That(voxel.Tsdf, Is.GreaterThan(before + 0.005f));
            Assert.That(voxel.Tsdf, Is.LessThan(0.051f));
        }

        [Test]
        public void NoisyDepth_OutliersAreRejectedWhileSmallNoiseConverges()
        {
            var voxel = new SyntheticVoxel();
            SeedAndStabilize(voxel, 0f, 1f, 1f, -1f, 30);
            int rejectedOutliers = 0;

            for (int i = 0; i < 40; i++)
            {
                float sample = i % 4 == 0 ? 0.45f :
                    i % 4 == 1 ? -0.45f :
                    i % 4 == 2 ? 0.025f : -0.025f;
                TsdfFusionResult result = voxel.Observe(sample, 1f, 1f,
                    true, -1f);
                if (Mathf.Abs(sample) > 0.4f)
                {
                    Assert.That(result.Accepted, Is.False);
                    Assert.That(result.Decision,
                        Is.EqualTo(TsdfFusionDecision.InconsistentOutlier));
                    rejectedOutliers++;
                }
            }

            Assert.That(rejectedOutliers, Is.EqualTo(20));
            Assert.That(Mathf.Abs(voxel.Tsdf), Is.LessThan(0.02f));
        }

        [Test]
        public void CorrectedBackBand_IsPhysicalAndDoesNotAllocateAnotherVolume()
        {
            float band = TsdfFusionPolicy.BehindSurfaceBandMeters(
                VoxelMinimum, VoxelSize, BackBandVoxels);
            Assert.That(band, Is.EqualTo(0.0625f).Within(0.000001f));
            Assert.That(TsdfFusionPolicy.IsBehindSurfaceVisible(-0.0624f,
                VoxelMinimum, VoxelSize, BackBandVoxels), Is.True);
            Assert.That(TsdfFusionPolicy.IsBehindSurfaceVisible(-0.0626f,
                VoxelMinimum, VoxelSize, BackBandVoxels), Is.False);
            Assert.That(TsdfFusionPolicy.IsProjectedBehindSurfaceVisible(-0.15f, 0.4f,
                VoxelMinimum, VoxelSize, BackBandVoxels), Is.True,
                "an oblique ray inside the same 6 cm normal-space band must remain visible");
            Assert.That(TsdfFusionPolicy.IsProjectedBehindSurfaceVisible(-0.15f, 1f,
                VoxelMinimum, VoxelSize, BackBandVoxels), Is.False,
                "the same ray depth behind a front-facing wall must remain occluded");

            Assert.That(VolumeIntegrator.TryCalculateActiveVolumeMemory(
                new int3(256, 256, 256), 1_000_000,
                out long tsdfBytes, out long colorBytes, out long frustumBytes), Is.True);
            Assert.That(tsdfBytes, Is.EqualTo(32L * 1024L * 1024L));
            Assert.That(colorBytes, Is.EqualTo(64L * 1024L * 1024L));
            Assert.That(frustumBytes, Is.EqualTo(12_000_000L));
            Assert.That(frustumBytes,
                Is.LessThanOrEqualTo(VolumeIntegrator.MaximumStorageBufferRangeBytes));
            Assert.That(tsdfBytes + colorBytes, Is.EqualTo(96L * 1024L * 1024L),
                "B06 must reuse RG8 TSDF confidence and RGBA8 quality, not add a 3D buffer");

            Assert.That(GPUSurfaceNets.TryCreateMemoryPlan(
                new int3(256, 256, 256), 0.08f,
                out GpuSurfaceNetsMemoryPlan meshPlan), Is.True);
            Assert.That(meshPlan.IndexBytes, Is.LessThanOrEqualTo(
                VolumeIntegrator.MaximumStorageBufferRangeBytes));
            Assert.That(meshPlan.MaximumStorageBufferBytes, Is.EqualTo(meshPlan.IndexBytes));
            Assert.That(meshPlan.TemporalTextureBytes,
                Is.EqualTo(256L * 1024L * 1024L),
                "the temporal allocation is a legal storage image, not a storage buffer");
        }

        [Test]
        public void GpuPolicyMatchesCpuReference()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                !SystemInfo.supportsComputeShaders)
                Assert.Ignore("Run this parity gate with a real compute-capable graphics device.");

            const string assetPath =
                "Packages/com.genesis.roomscan/Tests/Editor/TsdfFusionPolicyParity.compute";
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(assetPath);
            Assert.That(shader, Is.Not.Null, assetPath);

            TsdfFusionInput[] cpuInputs =
            {
                Input(-1f, 0f, 0f, 0f, 1f, 1f, false, false, 1f),
                Input(-1f, 0f, 0f, 0f, 1f, 0.2f, true, false, 1f),
                Input(-1f, 0f, 0f, 0f, 1f, 1f, true, false, 1f),
                Input(0f, 0.5f, 0.8f, 0.45f, 4f, 1f, true, true, 1f),
                Input(0f, 0.5f, 0.8f, -0.5f, 1f, 1f, true, true, -1f),
                Input(0f, 0.5f, 0.8f, 0.5f, 1f, 1f, true, true, 1f),
                Input(0f, 0.5f, 0.3f, 1f, 1f, 1f, true, true, 1f),
                Input(0.02f, 0.2f, 0.7f, 0.05f, 1f, 1f, true, true, 0.95f)
            };
            var gpuInputs = new GpuFusionInput[cpuInputs.Length];
            var expected = new TsdfFusionResult[cpuInputs.Length];
            for (int i = 0; i < cpuInputs.Length; i++)
            {
                gpuInputs[i] = new GpuFusionInput(cpuInputs[i]);
                expected[i] = TsdfFusionPolicy.Fuse(cpuInputs[i],
                    TsdfFusionParameters.Default);
            }

            var actual = new GpuFusionOutput[cpuInputs.Length];
            using var inputBuffer = new ComputeBuffer(cpuInputs.Length,
                Marshal.SizeOf<GpuFusionInput>());
            using var outputBuffer = new ComputeBuffer(cpuInputs.Length,
                Marshal.SizeOf<GpuFusionOutput>());
            inputBuffer.SetData(gpuInputs);
            int kernel = shader.FindKernel("Evaluate");
            shader.SetBuffer(kernel, "_Inputs", inputBuffer);
            shader.SetBuffer(kernel, "_Outputs", outputBuffer);
            shader.SetInt("_CaseCount", cpuInputs.Length);
            ApplyGpuDefaults(shader);
            shader.Dispatch(kernel, 1, 1, 1);
            outputBuffer.GetData(actual);

            for (int i = 0; i < actual.Length; i++)
            {
                Assert.That(actual[i].Accepted > 0.5f, Is.EqualTo(expected[i].Accepted),
                    $"case {i} accepted");
                Assert.That(actual[i].Decision, Is.EqualTo((int)expected[i].Decision),
                    $"case {i} decision");
                Assert.That(actual[i].Tsdf, Is.EqualTo(expected[i].Tsdf).Within(0.00001f),
                    $"case {i} tsdf");
                Assert.That(actual[i].Weight, Is.EqualTo(expected[i].Weight).Within(0.00001f),
                    $"case {i} weight");
                Assert.That(actual[i].Quality,
                    Is.EqualTo(expected[i].ObservationQuality).Within(0.00001f),
                    $"case {i} quality");
                Assert.That(actual[i].Blend, Is.EqualTo(expected[i].Blend).Within(0.00001f),
                    $"case {i} blend");
                Assert.That(actual[i].ExistingQuality,
                    Is.EqualTo(expected[i].ExistingQuality).Within(0.00001f),
                    $"case {i} existing quality");
            }
        }

        private static bool IsWithinBackBand(float raySignedDistance)
        {
            return TsdfFusionPolicy.IsBehindSurfaceVisible(raySignedDistance,
                VoxelMinimum, VoxelSize, BackBandVoxels);
        }

        private static TsdfFusionInput Input(float oldTsdf, float oldWeight,
            float bestQuality, float incomingTsdf, float distance, float incidence,
            bool visible, bool hasOrientation, float orientationDot)
        {
            return new TsdfFusionInput(oldTsdf, oldWeight, bestQuality,
                incomingTsdf, distance, MaxDistance, incidence, visible,
                hasOrientation, orientationDot);
        }

        private static void ApplyGpuDefaults(ComputeShader shader)
        {
            TsdfFusionParameters p = TsdfFusionParameters.Default;
            shader.SetInt("gsFusionProtectionEnabled", p.Enabled ? 1 : 0);
            shader.SetFloat("gsFusionStableConfidence", p.StableConfidence);
            shader.SetFloat("gsFusionExistingQualityFloor", p.ExistingQualityFloor);
            shader.SetFloat("gsFusionQualityHysteresis", p.QualityHysteresis);
            shader.SetFloat("gsFusionOppositeOrientationDot", p.OppositeOrientationDot);
            shader.SetFloat("gsFusionSurfaceBand", p.SurfaceBand);
            shader.SetFloat("gsFusionResidualTolerance", p.ResidualTolerance);
            shader.SetFloat("gsFusionResidualConfidenceSlack", p.ResidualConfidenceSlack);
            shader.SetFloat("gsFusionImprovementResidualScale", p.ImprovementResidualScale);
            shader.SetFloat("gsFusionStableBlendFloor", p.StableBlendFloor);
            shader.SetFloat("gsFusionNormalMinWeight", 0.08f);
            shader.SetFloat("gsFusionBackBandVoxels", BackBandVoxels);
            shader.SetFloat("_MaxWeight", p.MaxWeight);
            shader.SetFloat("_BlendRate", p.BlendRate);
            shader.SetFloat("_Stability", p.Stability);
            shader.SetFloat("_WeightGrowth", p.WeightGrowth);
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct GpuFusionInput
        {
            internal readonly float OldTsdf;
            internal readonly float OldWeight;
            internal readonly float ExistingBestQuality;
            internal readonly float IncomingTsdf;
            internal readonly float SurfaceDistance;
            internal readonly float MaximumDistance;
            internal readonly float Incidence;
            internal readonly float Visible;
            internal readonly float HasOrientation;
            internal readonly float OrientationDot;

            internal GpuFusionInput(TsdfFusionInput input)
            {
                OldTsdf = input.ExistingTsdf;
                OldWeight = input.ExistingWeight;
                ExistingBestQuality = input.ExistingBestQuality;
                IncomingTsdf = input.IncomingTsdf;
                SurfaceDistance = input.SurfaceDistanceMeters;
                MaximumDistance = input.MaximumUpdateDistanceMeters;
                Incidence = input.Incidence;
                Visible = input.Visible ? 1f : 0f;
                HasOrientation = input.HasExistingOrientation ? 1f : 0f;
                OrientationDot = input.OrientationDot;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GpuFusionOutput
        {
            internal float Accepted;
            internal float Tsdf;
            internal float Weight;
            internal float Quality;
            internal float Blend;
            internal float ExistingQuality;
            internal int Decision;
            internal float Padding;
        }

        private static float NormalizeTsdf(float signedDistance)
        {
            return Mathf.Clamp(signedDistance / TruncationDistance, -1f, 1f);
        }

        private static void SeedAndStabilize(SyntheticVoxel voxel, float tsdf,
            float distance, float incidence, float normal, int observations)
        {
            for (int i = 0; i < observations; i++)
            {
                TsdfFusionResult result = voxel.Observe(tsdf, distance, incidence,
                    true, normal);
                Assert.That(result.Accepted, Is.True,
                    $"observation {i} rejected as {result.Decision}");
            }
        }

        private sealed class SyntheticVoxel
        {
            internal float Tsdf = -1f;
            internal float Weight;
            internal float BestQuality;
            internal bool HasSurfaceNormal;
            internal float SurfaceNormal;

            internal TsdfFusionResult Observe(float incomingTsdf,
                float surfaceDistance, float incidence, bool visible,
                float incomingNormal)
            {
                bool hasOrientation = HasSurfaceNormal;
                float orientationDot = hasOrientation
                    ? SurfaceNormal * incomingNormal : 1f;
                var input = new TsdfFusionInput(Tsdf, Weight, BestQuality,
                    incomingTsdf, surfaceDistance, MaxDistance, incidence,
                    visible, hasOrientation, orientationDot);
                TsdfFusionResult result = TsdfFusionPolicy.Fuse(input,
                    TsdfFusionParameters.Default);
                if (!result.Accepted)
                    return result;

                Tsdf = result.Tsdf;
                Weight = result.Weight;
                if (!HasSurfaceNormal && Mathf.Abs(incomingTsdf) < 0.85f)
                {
                    HasSurfaceNormal = true;
                    SurfaceNormal = incomingNormal;
                }

                bool promoteQuality = result.Decision == TsdfFusionDecision.Seeded ||
                    Mathf.Abs(result.Tsdf - incomingTsdf) <=
                    TsdfFusionParameters.Default.ResidualTolerance;
                if (Mathf.Abs(incomingTsdf) < 0.5f && promoteQuality)
                    BestQuality = Mathf.Max(BestQuality, result.ObservationQuality);
                return result;
            }
        }
    }
}
