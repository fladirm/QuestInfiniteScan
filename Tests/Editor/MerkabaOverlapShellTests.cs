using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaOverlapShellTests
    {
        private static readonly Color32 MainColor = new(80, 120, 160, 255);

        [Test]
        public void IsolatedMeasuredMain_EmitsOneExactSupportPatch()
        {
            KernelState state = Surface(new float3(1f, 0f, 0f), 0f,
                MainColor);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0), state,
                out MerkabaOverlapShell.Patch patch), Is.True);
            Assert.That(MerkabaOverlapShell.TrianglesPerPatch, Is.EqualTo(2));
            Assert.That(MerkabaOverlapShell.VerticesPerPatch, Is.EqualTo(6));
            AssertSupportFootprint(patch);
            AssertCanonicalWinding(patch);
        }

        [Test]
        public void OccupiedLegacyMainWithoutPlane_EmitsNothing()
        {
            KernelState state = default;
            state.SetOccupiedForFixture(true, MainColor);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0), state,
                out _), Is.False);
        }

        [TestCase(1f, 0f, 0f)]
        [TestCase(0f, 1f, 0f)]
        [TestCase(0f, 0f, 1f)]
        [TestCase(1f, 1f, 0f)]
        [TestCase(0.17f, -0.63f, 0.76f)]
        public void MeasuredNormalDefinesPatchPlane(float x, float y, float z)
        {
            float3 requested = math.normalize(new float3(x, y, z));
            KernelState state = Surface(requested, 0.007f, MainColor);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0), state,
                out MerkabaOverlapShell.Patch patch), Is.True);
            KernelState.DecodeSurfacePlane(state.Flags, out float3 expected,
                out float offset);
            for (int corner = 0; corner < 4; corner++)
                Assert.That(math.dot(patch.GetCorner(corner).GridPosition,
                    expected), Is.EqualTo(offset).Within(1e-6f));
            AssertSupportFootprint(patch);
        }

        [Test]
        public void SubLatticeOffsetMovesOnlyAlongMeasuredNormal()
        {
            KernelState state = Surface(
                math.normalize(new float3(1f, 2f, -3f)), -0.0094f,
                MainColor);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0), state,
                out MerkabaOverlapShell.Patch patch), Is.True);
            KernelState.DecodeSurfacePlane(state.Flags, out float3 normal,
                out float offset);
            float3 center = (patch.Corner00.GridPosition +
                patch.Corner10.GridPosition + patch.Corner11.GridPosition +
                patch.Corner01.GridPosition) * 0.25f;
            AssertFloat3(center, normal * offset);
        }

        [Test]
        public void OppositePlaneEncodingProducesIdenticalGeometry()
        {
            float3 normal = math.normalize(new float3(-1f, 2f, 3f));
            KernelState forward = Surface(normal, 0.008f, MainColor);
            KernelState reverse = Surface(-normal, -0.008f, MainColor);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(5, -2, 7),
                forward, out MerkabaOverlapShell.Patch first), Is.True);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(5, -2, 7),
                reverse, out MerkabaOverlapShell.Patch second), Is.True);
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void AdjacentParallelMeasuredLayersNeverCollapse()
        {
            KernelState state = Surface(new float3(1f, 0f, 0f), 0.003f,
                MainColor);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0), state,
                out MerkabaOverlapShell.Patch first), Is.True);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(1, 0, 0),
                state, out MerkabaOverlapShell.Patch second), Is.True);
            float separation = math.dot(second.Corner00.GridPosition -
                first.Corner00.GridPosition, first.Normal);
            Assert.That(separation, Is.EqualTo(
                MerkabaConstants.LatticeStep).Within(1e-6f));
        }

        [Test]
        public void MainPatchHasNoNeighbourInputOrCameraDependency()
        {
            string source = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaOverlapShell.cs"));
            string generated = MerkabaOverlapShell.BuildGeneratedHlsl();
            Assert.That(source + generated, Does.Not.Contain("Neighbour"));
            Assert.That(source + generated, Does.Not.Contain("Donor"));
            Assert.That(source + generated, Does.Not.Contain("Camera"));
            Assert.That(source + generated, Does.Not.Contain("Eye"));
            Assert.That(source + generated, Does.Not.Contain("FREE"));
        }

        [TestCase(-8, -1, -1)]
        [TestCase(7, 7, 7)]
        [TestCase(31, 31, 31)]
        [TestCase(255, 255, 255)]
        [TestCase(-256, -256, -256)]
        public void TranslationAndHierarchyBoundariesAreInvariant(
            int x, int y, int z)
        {
            int3 translation = new(x, y, z);
            KernelState state = Surface(
                math.normalize(new float3(0.3f, 0.7f, -0.2f)), 0.004f,
                MainColor);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0), state,
                out MerkabaOverlapShell.Patch origin), Is.True);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(translation, state,
                out MerkabaOverlapShell.Patch moved), Is.True);
            float3 metricTranslation = (float3)translation *
                MerkabaConstants.LatticeStep;
            for (int corner = 0; corner < 4; corner++)
                AssertFloat3(moved.GetCorner(corner).GridPosition -
                    metricTranslation, origin.GetCorner(corner).GridPosition);
        }

        [Test]
        public void TangentBasisIsCanonicalAndOrthonormal()
        {
            float3[] normals =
            {
                new(1f, 0f, 0f), new(0f, 1f, 0f), new(0f, 0f, 1f),
                math.normalize(new float3(0.1f, -0.4f, 0.9f)),
                math.normalize(new float3(1f, 1f, 1f))
            };
            foreach (float3 normal in normals)
            {
                MerkabaOverlapShell.TangentBasis(normal,
                    out float3 tangent0, out float3 tangent1);
                Assert.That(math.dot(normal, tangent0),
                    Is.EqualTo(0f).Within(1e-6f));
                Assert.That(math.dot(normal, tangent1),
                    Is.EqualTo(0f).Within(1e-6f));
                Assert.That(math.dot(tangent0, tangent1),
                    Is.EqualTo(0f).Within(1e-6f));
                float first = tangent0.x != 0f ? tangent0.x :
                    tangent0.y != 0f ? tangent0.y : tangent0.z;
                Assert.That(first, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void GeneratedGpuPatchIsExactlyTheCpuAuthority()
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
                "M8TryBuildMeasuredPlanePatch"));
            Assert.That(generated, Does.Not.Contain("donor"));
            Assert.That(generated, Does.Not.Contain("neighbour"));
        }

        private static KernelState Surface(float3 normal, float offset,
            Color32 color)
        {
            KernelState state = default;
            state.SetOccupiedForFixture(true, color);
            state.Flags = KernelState.SetSurfacePlane(state.Flags, normal,
                offset);
            return state;
        }

        private static void AssertSupportFootprint(
            MerkabaOverlapShell.Patch patch)
        {
            float3 center = (patch.Corner00.GridPosition +
                patch.Corner10.GridPosition + patch.Corner11.GridPosition +
                patch.Corner01.GridPosition) * 0.25f;
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
    }
}
