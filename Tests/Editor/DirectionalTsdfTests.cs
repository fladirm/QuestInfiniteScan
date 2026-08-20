using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class DirectionalTsdfTests
    {
        [Test]
        public void DirectionWeights_AreCompactSymmetricAndAtMostThreeWay()
        {
            Assert.That(DirectionalTsdfMath.Weight(Vector3.right,
                DirectionalTsdfDirection.PositiveX), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(DirectionalTsdfMath.Weight(Vector3.right,
                DirectionalTsdfDirection.NegativeX), Is.Zero);

            Vector3 diagonal = new Vector3(1f, 1f, 1f).normalized;
            int mask = DirectionalTsdfMath.ContributionMask(diagonal);
            Assert.That(DirectionalTsdfMath.CountContributions(mask), Is.EqualTo(3));
            Assert.That(mask & (1 << (int)DirectionalTsdfDirection.PositiveX), Is.Not.Zero);
            Assert.That(mask & (1 << (int)DirectionalTsdfDirection.PositiveY), Is.Not.Zero);
            Assert.That(mask & (1 << (int)DirectionalTsdfDirection.PositiveZ), Is.Not.Zero);

            int oppositeMask = DirectionalTsdfMath.ContributionMask(-diagonal);
            Assert.That(mask & oppositeMask, Is.Zero,
                "opposite faces must never update the same directional domain");
        }

        [Test]
        public void ThinPanel_OppositeFacesRemainIndependentInsideOneSpatialVoxel()
        {
            var channels = new DirectionalTsdfVoxel[DirectionalTsdfMath.DirectionCount];
            TsdfFusionParameters parameters = TsdfFusionParameters.Default;
            var front = new DirectionalTsdfObservation(Vector3.left,
                0f, 1f, 1f, 1f, true, new Color32(220, 20, 20, 255));
            var back = new DirectionalTsdfObservation(Vector3.right,
                0.02f, 1f, 1f, 1f, true, new Color32(20, 20, 220, 255));

            for (int frame = 0; frame < 40; frame++)
            {
                FuseObservation(channels, front, parameters);
                FuseObservation(channels, back, parameters);
            }

            DirectionalTsdfVoxel frontVoxel =
                channels[(int)DirectionalTsdfDirection.NegativeX];
            DirectionalTsdfVoxel backVoxel =
                channels[(int)DirectionalTsdfDirection.PositiveX];
            Assert.That(frontVoxel.IsObserved, Is.True);
            Assert.That(backVoxel.IsObserved, Is.True);
            Assert.That(frontVoxel.Sdf, Is.EqualTo(0f).Within(0.002f));
            Assert.That(backVoxel.Sdf, Is.EqualTo(0.02f / 0.15f).Within(0.01f));
            Assert.That(frontVoxel.Color.r, Is.GreaterThan(frontVoxel.Color.b));
            Assert.That(backVoxel.Color.b, Is.GreaterThan(backVoxel.Color.r));
        }

        [Test]
        public void ThinColumn_StoresFourIndependentLateralSurfaceDomains()
        {
            var channels = new DirectionalTsdfVoxel[DirectionalTsdfMath.DirectionCount];
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down };
            foreach (Vector3 normal in normals)
            {
                var observation = new DirectionalTsdfObservation(normal, 0f, 0.8f,
                    1f, 1f, true, new Color32(128, 128, 128, 255));
                for (int frame = 0; frame < 12; frame++)
                    FuseObservation(channels, observation, TsdfFusionParameters.Default);
            }

            Assert.That(channels[(int)DirectionalTsdfDirection.PositiveX].IsObserved, Is.True);
            Assert.That(channels[(int)DirectionalTsdfDirection.NegativeX].IsObserved, Is.True);
            Assert.That(channels[(int)DirectionalTsdfDirection.PositiveY].IsObserved, Is.True);
            Assert.That(channels[(int)DirectionalTsdfDirection.NegativeY].IsObserved, Is.True);
            Assert.That(channels[(int)DirectionalTsdfDirection.PositiveZ].IsObserved, Is.False);
            Assert.That(channels[(int)DirectionalTsdfDirection.NegativeZ].IsObserved, Is.False);
        }

        [Test]
        public void SameDirection_CloseObservationCanImproveFarEstimate()
        {
            var voxel = new DirectionalTsdfVoxel();
            var far = new DirectionalTsdfObservation(Vector3.forward,
                0.06f, 4f, 1f, 1f, true, new Color32(10, 20, 30, 255));
            var near = new DirectionalTsdfObservation(Vector3.forward,
                0f, 1f, 1f, 1f, true, new Color32(40, 50, 60, 255));
            for (int i = 0; i < 80; i++)
                DirectionalTsdfFusion.Fuse(ref voxel,
                    DirectionalTsdfDirection.PositiveZ, far, 0.15f, 5f,
                    TsdfFusionParameters.Default);
            float farError = Mathf.Abs(voxel.Sdf);

            for (int i = 0; i < 30; i++)
                DirectionalTsdfFusion.Fuse(ref voxel,
                    DirectionalTsdfDirection.PositiveZ, near, 0.15f, 5f,
                    TsdfFusionParameters.Default);

            Assert.That(Mathf.Abs(voxel.Sdf), Is.LessThan(farError * 0.6f));
        }

        [Test]
        public void PackedVoxel_RoundTripsWithinQuantizationAndPreservesFreeze()
        {
            var source = new DirectionalTsdfVoxel
            {
                Sdf = -0.3125f,
                Weight = 0.273f,
                BestQuality = 0.78f,
                Color = new Color32(12, 130, 251, 255),
                Frozen = true
            };
            DirectionalTsdfPackedVoxel packed = DirectionalTsdfVoxelCodec.Pack(source, 0.5f);
            DirectionalTsdfVoxel decoded = DirectionalTsdfVoxelCodec.Unpack(packed, 0.5f);

            Assert.That(decoded.Sdf, Is.EqualTo(source.Sdf).Within(1f / 32767f));
            Assert.That(decoded.Weight, Is.EqualTo(source.Weight).Within(0.5f / 32767f));
            Assert.That(decoded.BestQuality, Is.EqualTo(source.BestQuality).Within(1f / 255f));
            Assert.That(decoded.Color.r, Is.EqualTo(source.Color.r));
            Assert.That(decoded.Color.g, Is.EqualTo(source.Color.g));
            Assert.That(decoded.Color.b, Is.EqualTo(source.Color.b));
            Assert.That(decoded.Frozen, Is.True);
        }

        [Test]
        public void SparseMemoryPlan_StaysBelowQuestStorageBufferLimit()
        {
            bool ok = DirectionalTsdfMemoryPlan.TryCreate(new int3(256), 8,
                16384, VolumeIntegrator.MaximumStorageBufferRangeBytes,
                out DirectionalTsdfMemoryPlan plan);

            Assert.That(ok, Is.True);
            Assert.That(plan.SpatialBlockCount, Is.EqualTo(new int3(32)));
            Assert.That(plan.VoxelsPerBlock, Is.EqualTo(512));
            Assert.That(plan.VoxelPoolBytes, Is.EqualTo(64L * 1024L * 1024L));
            Assert.That(plan.LargestStorageBufferBytes,
                Is.LessThanOrEqualTo(VolumeIntegrator.MaximumStorageBufferRangeBytes));
            Assert.That(plan.TotalStorageBytes, Is.LessThan(66L * 1024L * 1024L));
        }

        [Test]
        public void SparseMemoryPlan_FailsClosedOnOversizedPoolOrMisalignedVolume()
        {
            Assert.That(DirectionalTsdfMemoryPlan.TryCreate(new int3(255, 256, 256),
                8, 16384, VolumeIntegrator.MaximumStorageBufferRangeBytes, out _), Is.False);
            Assert.That(DirectionalTsdfMemoryPlan.TryCreate(new int3(256), 8,
                40000, VolumeIntegrator.MaximumStorageBufferRangeBytes, out _), Is.False);
        }

        private static void FuseObservation(DirectionalTsdfVoxel[] channels,
            in DirectionalTsdfObservation observation,
            in TsdfFusionParameters parameters)
        {
            for (int direction = 0; direction < DirectionalTsdfMath.DirectionCount;
                 direction++)
            {
                DirectionalTsdfFusion.Fuse(ref channels[direction],
                    (DirectionalTsdfDirection)direction, observation, 0.15f, 5f,
                    parameters);
            }
        }
    }
}
