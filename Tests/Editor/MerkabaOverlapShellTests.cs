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
        private const float Tolerance = 2e-5f;

        [Test]
        public void IsolatedMeasuredMain_EmitsOne25mmMembranePatch()
        {
            KernelState state = Surface(new int3(0), new float3(1, 0, 0), 0f);

            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0), state,
                out MerkabaOverlapShell.Patch patch), Is.True);

            Assert.That(MerkabaOverlapShell.TrianglesPerPatch, Is.EqualTo(2));
            Assert.That(MerkabaOverlapShell.VerticesPerPatch, Is.EqualTo(4));
            Assert.That(MerkabaOverlapShell.IndicesPerPatch, Is.EqualTo(6));
            Assert.That(MerkabaOverlapShell.MembranePatchPitch,
                Is.EqualTo(MerkabaConstants.LatticeStep));
            AssertFootprint(patch, 0);
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
        public void MeasuredPlaneDefinesEveryResolvedCorner(float x, float y,
            float z)
        {
            float3 requested = math.normalize(new float3(x, y, z));
            int3 main = new(0);
            var context = PlaneNeighbourhood(main, requested, 0.004f);

            Assert.That(MerkabaOverlapShell.TryBuildPatch(main, context,
                out MerkabaOverlapShell.Patch patch), Is.True);

            KernelState.DecodeSurfacePlane(context[main].Flags,
                out float3 normal, out float offset);
            float plane = math.dot((float3)main * MerkabaConstants.LatticeStep,
                normal) + offset;
            for (int corner = 0; corner < 4; corner++)
                Assert.That(math.dot(patch.GetCorner(corner).GridPosition,
                    normal), Is.EqualTo(plane).Within(
                    MerkabaConstants.SurfacePlaneOffsetRange / 127f * 2f));
            AssertCanonicalWinding(patch);
        }

        [Test]
        public void DominantAxisTieOrder_IsXThenYThenZ()
        {
            Assert.That(MerkabaOverlapShell.DominantAxis(new float3(1, 1, 1)),
                Is.EqualTo(0));
            Assert.That(MerkabaOverlapShell.DominantAxis(new float3(0, 1, 1)),
                Is.EqualTo(1));
            Assert.That(MerkabaOverlapShell.DominantAxis(new float3(0, 0, 1)),
                Is.EqualTo(2));
        }

        [Test]
        public void AdjacentPatches_CalculateBitIdenticalSharedCorners()
        {
            var context = new Dictionary<int3, KernelState>();
            for (int y = -1; y <= 2; y++)
            for (int z = -1; z <= 1; z++)
                context.Add(new int3(0, y, z),
                    Surface(new int3(0, y, z), new float3(1, 0, 0), 0.003f));

            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0, 0, 0),
                context, out MerkabaOverlapShell.Patch first), Is.True);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0, 1, 0),
                context, out MerkabaOverlapShell.Patch second), Is.True);

            float sharedY = MerkabaConstants.LatticeStep * 0.5f;
            float3[] firstShared = Corners(first).Where(value =>
                math.abs(value.y - sharedY) < 1e-7f).OrderBy(value => value.z)
                .ToArray();
            float3[] secondShared = Corners(second).Where(value =>
                math.abs(value.y - sharedY) < 1e-7f).OrderBy(value => value.z)
                .ToArray();
            Assert.That(secondShared.Length, Is.EqualTo(2));
            Assert.That(firstShared.Length, Is.EqualTo(2));
            for (int index = 0; index < 2; index++)
                Assert.That(math.all(math.asint(firstShared[index]) ==
                    math.asint(secondShared[index])), Is.True);
        }

        [Test]
        public void FreeSeparator_BlocksContributorBehindIt()
        {
            int3 main = new(0, 0, 0);
            var context = new Dictionary<int3, KernelState>
            {
                [main] = Surface(main, new float3(1, 0, 0), 0f),
                [new int3(0, 1, 0)] = StrongFree(),
                [new int3(1, 1, 0)] = Surface(new int3(1, 1, 0),
                    new float3(1, 0, 0), 0f)
            };

            Assert.That(MerkabaOverlapShell.TryBuildPatch(main, context,
                out MerkabaOverlapShell.Patch patch), Is.True);

            foreach (float3 corner in Corners(patch))
                Assert.That(corner.x, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void UnknownNeighbour_CreatesNoBacksideOrInventedContributor()
        {
            int3 main = new(0);
            var context = new Dictionary<int3, KernelState>
            {
                [main] = Surface(main, new float3(0, 0, 1), 0.006f)
            };

            Assert.That(MerkabaOverlapShell.TryBuildPatch(main, context,
                out MerkabaOverlapShell.Patch patch), Is.True);

            KernelState.DecodeSurfacePlane(context[main].Flags,
                out float3 decodedNormal, out float decodedOffset);
            Assert.That(patch.Normal.z, Is.GreaterThan(0f));
            Assert.That(Corners(patch).All(value => math.abs(
                math.dot(value, decodedNormal) - decodedOffset) <= Tolerance),
                Is.True);
        }

        [Test]
        public void TwoCloseParallelSheets_RemainDistinct()
        {
            var context = new Dictionary<int3, KernelState>();
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                int3 firstOwner = new(0, y, z);
                int3 secondOwner = new(1, y, z);
                context.Add(firstOwner, Surface(firstOwner,
                    new float3(1, 0, 0),
                    0.005f));
                context.Add(secondOwner, Surface(secondOwner,
                    new float3(1, 0, 0),
                    -0.005f));
            }

            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0), context,
                out MerkabaOverlapShell.Patch firstPatch), Is.True);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(1, 0, 0),
                context, out MerkabaOverlapShell.Patch secondPatch), Is.True);

            float first = Corners(firstPatch).Average(value => value.x);
            float second = Corners(secondPatch).Average(value => value.x);
            KernelState.DecodeSurfacePlane(context[new int3(0)].Flags,
                out _, out float firstOffset);
            KernelState.DecodeSurfacePlane(context[new int3(1, 0, 0)].Flags,
                out _, out float secondOffset);
            float expectedSeparation = MerkabaConstants.LatticeStep +
                secondOffset - firstOffset;
            Assert.That(second - first,
                Is.EqualTo(expectedSeparation).Within(Tolerance));
        }

        [TestCase(0, 0, 0, TestName = "ConvexCorner")]
        [TestCase(0, 1, 0, TestName = "ConcaveCorner")]
        [TestCase(0, 0, 1, TestName = "Doorway")]
        [TestCase(0, 1, 1, TestName = "TJunction")]
        public void IncompatibleDominantBranches_DoNotDeformMainPatch(
            int x, int y, int z)
        {
            int3 main = new(0);
            int3 other = new int3(x, y, z) + new int3(0, 1, 0);
            var context = new Dictionary<int3, KernelState>
            {
                [main] = Surface(main, new float3(1, 0, 0), 0f),
                [other] = Surface(other, new float3(0, 1, 0), 0f)
            };

            Assert.That(MerkabaOverlapShell.TryBuildPatch(main, context,
                out MerkabaOverlapShell.Patch patch), Is.True);
            Assert.That(Corners(patch).All(value =>
                math.abs(value.x) <= Tolerance), Is.True);
        }

        [TestCase(-8, -1, -1)]
        [TestCase(7, 7, 7)]
        [TestCase(8, 8, 8)]
        [TestCase(31, 31, 31)]
        [TestCase(32, 32, 32)]
        [TestCase(255, 255, 255)]
        [TestCase(256, 256, 256)]
        [TestCase(-256, -256, -256)]
        public void TranslationTileChunkAndBlockBoundariesAreInvariant(
            int x, int y, int z)
        {
            int3 translation = new(x, y, z);
            float3 normal = math.normalize(new float3(0.3f, 0.7f, -0.2f));
            Dictionary<int3, KernelState> originContext =
                PlaneNeighbourhood(new int3(0), normal, 0.004f);
            Dictionary<int3, KernelState> movedContext = Translate(
                originContext, translation);

            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                originContext, out MerkabaOverlapShell.Patch origin), Is.True);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(translation,
                movedContext, out MerkabaOverlapShell.Patch moved), Is.True);

            float3 metricTranslation = (float3)translation *
                MerkabaConstants.LatticeStep;
            float3[] originCorners = Corners(origin).OrderBy(Key).ToArray();
            float3[] movedCorners = Corners(moved).Select(value =>
                value - metricTranslation).OrderBy(Key).ToArray();
            for (int corner = 0; corner < 4; corner++)
                AssertFloat3(movedCorners[corner], originCorners[corner]);
        }

        [Test]
        public void GeneratedGpuMembraneIsExactlyTheCpuAuthority()
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
            Assert.That(generated, Does.Contain("M8TryBuildMembranePatch"));
            Assert.That(generated, Does.Contain("M8_MEMBRANE_HALF_PITCH"));
            Assert.That(generated, Does.Not.Contain("M8EmitReadoutGlyph"));
            Assert.That(generated, Does.Not.Contain("Camera"));
            Assert.That(generated, Does.Not.Contain("Eye"));
        }

        private static Dictionary<int3, KernelState> PlaneNeighbourhood(
            int3 main, float3 requestedNormal, float mainOffset)
        {
            KernelState mainState = Surface(main, requestedNormal, mainOffset);
            KernelState.DecodeSurfacePlane(mainState.Flags, out float3 normal,
                out float decodedOffset);
            float planeConstant = math.dot((float3)main *
                MerkabaConstants.LatticeStep, normal) + decodedOffset;
            int dominant = MerkabaOverlapShell.DominantAxis(normal);
            MerkabaOverlapShell.TangentAxes(dominant, out int tangent0,
                out int tangent1);
            var result = new Dictionary<int3, KernelState>();
            for (int a = -1; a <= 1; a++)
            for (int b = -1; b <= 1; b++)
            {
                int3 owner = main;
                owner[tangent0] += a;
                owner[tangent1] += b;
                float ownerOffset = planeConstant - math.dot((float3)owner *
                    MerkabaConstants.LatticeStep, normal);
                if (math.abs(ownerOffset) >
                    MerkabaConstants.SurfacePlaneOffsetRange) continue;
                result[owner] = Surface(owner, normal, ownerOffset);
            }
            result[main] = mainState;
            return result;
        }

        private static Dictionary<int3, KernelState> Translate(
            IReadOnlyDictionary<int3, KernelState> source, int3 translation)
        {
            var result = new Dictionary<int3, KernelState>(source.Count);
            foreach (KeyValuePair<int3, KernelState> pair in source)
                result.Add(pair.Key + translation, pair.Value);
            return result;
        }

        private static KernelState Surface(int3 owner, float3 normal,
            float localOffset)
        {
            KernelState state = default;
            state.SetOccupiedForFixture(true, MainColor);
            state.Flags = KernelState.SetSurfacePlane(state.Flags, normal,
                localOffset);
            return state;
        }

        private static KernelState StrongFree() => new()
        {
            OccupancyEvidence = MerkabaConstants.ExportKnownFreeThreshold
        };

        private static IEnumerable<float3> Corners(
            MerkabaOverlapShell.Patch patch)
        {
            for (int index = 0; index < 4; index++)
                yield return patch.GetCorner(index).GridPosition;
        }

        private static void AssertFootprint(MerkabaOverlapShell.Patch patch,
            int dominantAxis)
        {
            MerkabaOverlapShell.TangentAxes(dominantAxis, out int tangent0,
                out int tangent1);
            float minimum0 = Corners(patch).Min(value => value[tangent0]);
            float maximum0 = Corners(patch).Max(value => value[tangent0]);
            float minimum1 = Corners(patch).Min(value => value[tangent1]);
            float maximum1 = Corners(patch).Max(value => value[tangent1]);
            Assert.That(maximum0 - minimum0,
                Is.EqualTo(MerkabaConstants.LatticeStep).Within(Tolerance));
            Assert.That(maximum1 - minimum1,
                Is.EqualTo(MerkabaConstants.LatticeStep).Within(Tolerance));
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

        private static string Key(float3 value) =>
            $"{math.asint(value.x):X8}{math.asint(value.y):X8}" +
            $"{math.asint(value.z):X8}";

        private static void AssertFloat3(float3 actual, float3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Tolerance));
        }
    }
}
