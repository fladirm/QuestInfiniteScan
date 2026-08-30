using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaOverlapShellTests
    {
        private static readonly Color32 MainColor = new(80, 120, 160, 255);

        [Test]
        public void IsolatedOrientedMain_AlwaysEmitsOneTwoTrianglePatch()
        {
            var scene = new Scene();
            scene.Surface(0, new int3(0), MainColor);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                scene.Sample, out MerkabaOverlapShell.Patch patch), Is.True);
            Assert.That(MerkabaOverlapShell.TrianglesPerPatch, Is.EqualTo(2));
            Assert.That(MerkabaOverlapShell.VerticesPerPatch, Is.EqualTo(6));
            Assert.That(patch.NormalIndex, Is.Zero);
            AssertCanonicalWinding(patch);
            AssertSupportFootprint(patch);
        }

        [Test]
        public void OccupiedMainWithoutOrientation_EmitsNoLegacyGuess()
        {
            var states = new Dictionary<int3, KernelState>();
            KernelState state = default;
            state.SetOccupiedForFixture(true, MainColor);
            states[new int3(0)] = state;
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                coord => states.TryGetValue(coord, out KernelState value)
                    ? value : default, out _), Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AxisAlignedSheet_EveryOrientedMainHasPatch(int branch)
        {
            var scene = new Scene();
            int3 normal = MerkabaOverlapShell.CanonicalNormals[branch];
            for (int a = -2; a <= 2; a++)
            for (int b = -2; b <= 2; b++)
            {
                int3 coord = branch == 0 ? new int3(0, a, b) :
                    branch == 1 ? new int3(a, 0, b) : new int3(a, b, 0);
                scene.Surface(branch, coord, MainColor);
            }
            foreach (int3 main in scene.Coordinates)
            {
                Assert.That(MerkabaOverlapShell.TryBuildPatch(main,
                    scene.Sample, out MerkabaOverlapShell.Patch patch), Is.True);
                Assert.That(math.abs(math.dot(patch.Normal,
                    math.normalize((float3)normal))), Is.EqualTo(1f).Within(1e-6f));
                AssertSupportFootprint(patch);
            }
        }

        [Test]
        public void CanonicalDiagonalSheet_ProducesContiguousOverlapPatches()
        {
            const int branch = 9;
            var scene = new Scene();
            for (int x = -2; x <= 2; x++)
            for (int y = -2; y <= 2; y++)
                scene.Surface(branch, new int3(x, y, -x - y), MainColor);
            var patches = scene.Coordinates.Select(main =>
            {
                Assert.That(MerkabaOverlapShell.TryBuildPatch(main,
                    scene.Sample, out MerkabaOverlapShell.Patch patch), Is.True);
                return patch;
            }).ToArray();
            Assert.That(patches, Has.Length.EqualTo(25));
            Assert.That(patches.All(p => p.NormalIndex == branch), Is.True);
            Assert.That(patches.All(p => Enumerable.Range(0, 4).All(c =>
                p.GetCorner(c).ContributorCount >= 1)), Is.True);
        }

        [Test]
        public void SameBranchImmediateDonor_RefinesNormalHeightByMedian()
        {
            var scene = new Scene();
            scene.Surface(0, new int3(0), MainColor);
            scene.Surface(0, new int3(1, 0, 0),
                new Color32(160, 80, 40, 255));
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                scene.Sample, out MerkabaOverlapShell.Patch patch), Is.True);
            float3 center = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                float height = math.dot(
                    patch.GetCorner(corner).GridPosition - center,
                    patch.Normal);
                Assert.That(height, Is.EqualTo(0.0125f).Within(1e-6f));
                Assert.That(patch.GetCorner(corner).ContributorCount,
                    Is.EqualTo(2));
            }
        }

        [Test]
        public void DifferentOrientationNeighbour_DoesNotDeformMain()
        {
            var scene = new Scene();
            scene.Surface(0, new int3(0), MainColor);
            scene.Surface(1, new int3(1, 0, 0), Color.red);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                scene.Sample, out MerkabaOverlapShell.Patch patch), Is.True);
            for (int corner = 0; corner < 4; corner++)
            {
                Assert.That(math.dot(patch.GetCorner(corner).GridPosition,
                    patch.Normal), Is.EqualTo(0f).Within(1e-6f));
                Assert.That(patch.GetCorner(corner).ContributorCount,
                    Is.EqualTo(1));
            }
        }

        [Test]
        public void LogicalEmptyNeighbours_PreserveFallbackPatch()
        {
            var scene = new Scene();
            scene.Surface(8, new int3(0), MainColor);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                scene.Sample, out MerkabaOverlapShell.Patch patch), Is.True);
            Assert.That(Enumerable.Range(0, 4).All(c =>
                patch.GetCorner(c).ContributorCount == 1), Is.True);
        }

        [Test]
        public void DistanceTwoParallelSheets_DoNotBridgeAcrossEmptyCenter()
        {
            var scene = new Scene();
            scene.Surface(0, new int3(0), MainColor);
            scene.Surface(0, new int3(2, 0, 0), Color.blue);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                scene.Sample, out MerkabaOverlapShell.Patch first), Is.True);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(2, 0, 0),
                scene.Sample, out MerkabaOverlapShell.Patch second), Is.True);
            Assert.That(Enumerable.Range(0, 4).All(c =>
                first.GetCorner(c).ContributorCount == 1), Is.True);
            Assert.That(Enumerable.Range(0, 4).All(c =>
                second.GetCorner(c).ContributorCount == 1), Is.True);
            Assert.That(second.GetCorner(0).GridPosition.x -
                first.GetCorner(0).GridPosition.x,
                Is.EqualTo(2f * MerkabaConstants.LatticeStep).Within(1e-6f));
        }

        [TestCase(-8, -1, -1)]
        [TestCase(7, 7, 7)]
        [TestCase(31, 31, 31)]
        [TestCase(255, 255, 255)]
        [TestCase(-256, -256, -256)]
        public void TranslationAndHierarchyBoundaries_AreInvariant(
            int x, int y, int z)
        {
            int3 translation = new(x, y, z);
            MerkabaOverlapShell.Patch origin = BuildTranslatedPatch(new int3(0));
            MerkabaOverlapShell.Patch moved = BuildTranslatedPatch(translation);
            float3 metricTranslation = (float3)translation *
                MerkabaConstants.LatticeStep;
            Assert.That(moved.NormalIndex, Is.EqualTo(origin.NormalIndex));
            for (int corner = 0; corner < 4; corner++)
                AssertFloat3(moved.GetCorner(corner).GridPosition -
                    metricTranslation, origin.GetCorner(corner).GridPosition);
        }

        [Test]
        public void DonorEnumerationPermutation_IsByteDeterministic()
        {
            var scene = new Scene();
            scene.Surface(3, new int3(0), MainColor);
            foreach (int3 offset in MerkabaOverlapShell.CanonicalImmediateOffsets)
                if ((offset.x + offset.y + offset.z & 1) == 0)
                    scene.Surface(3, offset, new Color32(
                        (byte)(offset.x * 20 + 100),
                        (byte)(offset.y * 20 + 100),
                        (byte)(offset.z * 20 + 100), 255));
            int3[] forward = MerkabaOverlapShell.CanonicalImmediateOffsets.ToArray();
            int3[] reverse = forward.Reverse().ToArray();
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                scene.Sample, forward, out MerkabaOverlapShell.Patch first),
                Is.True);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                scene.Sample, reverse, out MerkabaOverlapShell.Patch second),
                Is.True);
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void TangentBasis_IsCanonicalOrthonormalForAllBranches()
        {
            for (int branch = 0;
                 branch < MerkabaOverlapShell.CanonicalNormalCount; branch++)
            {
                MerkabaOverlapShell.TangentBasis(branch, out float3 normal,
                    out float3 tangent0, out float3 tangent1);
                Assert.That(math.dot(normal, tangent0),
                    Is.EqualTo(0f).Within(1e-6f));
                Assert.That(math.dot(normal, tangent1),
                    Is.EqualTo(0f).Within(1e-6f));
                Assert.That(math.dot(tangent0, tangent1),
                    Is.EqualTo(0f).Within(1e-6f));
                Assert.That(math.length(tangent0),
                    Is.EqualTo(1f).Within(1e-6f));
                float first = tangent0.x != 0f ? tangent0.x :
                    tangent0.y != 0f ? tangent0.y : tangent0.z;
                Assert.That(first, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void ContributorBound_CoversEveryCanonicalCorner()
        {
            for (int branch = 0;
                 branch < MerkabaOverlapShell.CanonicalNormalCount; branch++)
            {
                MerkabaOverlapShell.TangentBasis(branch, out _,
                    out float3 tangent0, out float3 tangent1);
                for (int sign0 = -1; sign0 <= 1; sign0 += 2)
                for (int sign1 = -1; sign1 <= 1; sign1 += 2)
                {
                    float3 corner = tangent0 * sign0 + tangent1 * sign1;
                    int donors = 1;
                    foreach (int3 offset in
                             MerkabaOverlapShell.CanonicalImmediateOffsets)
                        if (MerkabaOverlapShell.DonorSupportContainsCorner(
                                offset, corner, tangent0, tangent1))
                            donors++;
                    Assert.That(donors, Is.LessThanOrEqualTo(
                        MerkabaOverlapShell.MaximumContributorsPerCorner));
                }
            }
        }

        [Test]
        public void GeneratedGpuOracle_IsExactlyTheCpuAuthority()
        {
            string directory = Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Shaders");
            Assert.That(File.ReadAllText(Path.Combine(directory,
                "MerkabaSurfaceOrientation.generated.hlsl")), Is.EqualTo(
                MerkabaOverlapShell.BuildSurfaceOrientationHlsl()));
            string generated = File.ReadAllText(Path.Combine(directory,
                "MerkabaOverlapShell.generated.hlsl"));
            Assert.That(generated,
                Is.EqualTo(MerkabaOverlapShell.BuildGeneratedHlsl()));
            Assert.That(generated, Does.Contain(
                "M8BuildOrientedOverlapPatch"));
            Assert.That(generated, Does.Contain(
                "M8_OVERLAP_PATCH_HALF_EXTENT 0.025"));
            Assert.That(generated, Does.Not.Contain("M8OverlapFreeSide"));
            Assert.That(generated, Does.Not.Contain("M8BeginOverlapBranch"));
            Assert.That(generated, Does.Not.Contain("freeSign"));
        }

        [Test]
        public void OracleAndGeneratedCode_HaveNoViewOrPersistentAuthority()
        {
            string source = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaOverlapShell.cs"));
            string generated = MerkabaOverlapShell.BuildGeneratedHlsl();
            Assert.That(source + generated, Does.Not.Contain("Camera"));
            Assert.That(source + generated, Does.Not.Contain("Eye"));
            Assert.That(source + generated, Does.Not.Contain("QEF"));
            Assert.That(source + generated, Does.Not.Contain("TSDF"));
            Assert.That(source + generated, Does.Not.Contain("FREE"));
            Assert.That(source, Does.Contain(
                "Overlap patch queried non-immediate offset"));
        }

        private static MerkabaOverlapShell.Patch BuildTranslatedPatch(
            int3 translation)
        {
            var scene = new Scene();
            scene.Surface(5, translation, MainColor);
            scene.Surface(5, translation + new int3(1, 0, 1), Color.red);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(translation,
                scene.Sample, out MerkabaOverlapShell.Patch patch), Is.True);
            return patch;
        }

        private static void AssertSupportFootprint(
            MerkabaOverlapShell.Patch patch)
        {
            float3 center = (float3)patch.Main * MerkabaConstants.LatticeStep;
            int[] sign0 = { -1, 1, 1, -1 };
            int[] sign1 = { -1, -1, 1, 1 };
            for (int corner = 0; corner < 4; corner++)
            {
                float3 relative = patch.GetCorner(corner).GridPosition - center;
                Assert.That(math.dot(relative, patch.Tangent0), Is.EqualTo(
                    sign0[corner] * MerkabaConstants.HalfSupport).Within(1e-6f));
                Assert.That(math.dot(relative, patch.Tangent1), Is.EqualTo(
                    sign1[corner] * MerkabaConstants.HalfSupport).Within(1e-6f));
            }
        }

        private static void AssertCanonicalWinding(
            MerkabaOverlapShell.Patch patch)
        {
            for (int triangle = 0; triangle < 2; triangle++)
            {
                float3 a = patch.GetTriangleVertex(triangle * 3).GridPosition;
                float3 b = patch.GetTriangleVertex(triangle * 3 + 1).GridPosition;
                float3 c = patch.GetTriangleVertex(triangle * 3 + 2).GridPosition;
                Assert.That(math.dot(math.cross(b - a, c - a), patch.Normal),
                    Is.GreaterThan(0f));
            }
        }

        private static void AssertFloat3(float3 actual, float3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-6f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-6f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-6f));
        }

        private sealed class Scene
        {
            private readonly Dictionary<int3, KernelState> _states = new();
            internal IEnumerable<int3> Coordinates => _states.Keys;

            internal void Surface(int branch, int3 coord, Color32 color)
            {
                KernelState state = default;
                state.SetOccupiedForFixture(true, color);
                state.Flags = KernelState.SetSurfaceOrientation(
                    state.Flags, branch);
                _states[coord] = state;
            }

            internal KernelState Sample(int3 coord) =>
                _states.TryGetValue(coord, out KernelState state)
                    ? state : default;
        }
    }
}
