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
        private static readonly Color32 SurfaceColor =
            new(96, 144, 192, 255);

        [Test]
        public void IsolatedOrFreeOnlyMain_IsUnderdeterminedAndEmitsNoPatch()
        {
            var isolated = new Scene();
            isolated.Surface(new int3(0));
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                isolated.Sample, out _), Is.False);

            var freeOnly = new Scene();
            freeOnly.Surface(new int3(0));
            freeOnly.FreeUnlessSurface(new int3(1, 0, 0));
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                freeOnly.Sample, out _), Is.False,
                "FREE supplies winding, not a fabricated surface tangent.");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DenseAxisWall_IsOneZeroThicknessOverlappingSheet(int axis)
        {
            int3 normal = axis == 0 ? new int3(1, 0, 0) :
                axis == 1 ? new int3(0, 1, 0) : new int3(0, 0, 1);
            Scene scene = Plane(normal, -5, 5, 1);
            List<MerkabaOverlapShell.Patch> patches =
                BuildPlaneInterior(scene, normal, -3, 3);
            Assert.That(patches, Has.Count.EqualTo(49));
            foreach (MerkabaOverlapShell.Patch patch in patches)
            {
                Assert.That(patch.SurfaceSignature.Normal,
                    Is.EqualTo(normal));
                Assert.That(patch.SurfaceSignature.FreeSign, Is.EqualTo(1));
                Assert.That(MerkabaOverlapShell.TrianglesPerPatch,
                    Is.EqualTo(2));
                Assert.That(UniqueTriangleVertices(patch), Is.EqualTo(4));
                for (int corner = 0; corner < 4; corner++)
                    Assert.That(patch.GetCorner(corner).QuarterCoordinate[axis],
                        Is.EqualTo(patch.Main[axis] * 4));
                AssertFullSupportFootprint(patch);
                AssertWinding(patch);
            }
            AssertProjectedCoverageHasNoPitchGaps(patches, axis, -3, 3);
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
        [TestCase(12)]
        public void FaceAndBodyDiagonalPlanes_ResolveCanonicalSlope(int normalIndex)
        {
            int3 normal = MerkabaOverlapShell.CanonicalNormals[normalIndex];
            Scene scene = Plane(normal, -6, 6, 1);
            List<MerkabaOverlapShell.Patch> patches =
                BuildPlaneInterior(scene, normal, -3, 3);
            Assert.That(patches.Count, Is.GreaterThan(30));
            Assert.That(patches.All(p =>
                math.all(p.SurfaceSignature.Normal == normal)), Is.True);
            foreach (MerkabaOverlapShell.Patch patch in patches)
            {
                AssertFullSupportFootprint(patch);
                AssertWinding(patch);
                Assert.That(UniqueTriangleVertices(patch), Is.EqualTo(4));
            }
        }

        [Test]
        public void QuantizedArbitrarySlope_IsDeterministicAndSingleSheet()
        {
            var scene = new Scene();
            for (int u = -6; u <= 6; u++)
            for (int v = -6; v <= 6; v++)
            {
                int height = (int)Math.Round((2 * u + v) / 3.0,
                    MidpointRounding.AwayFromZero);
                int3 surface = new(height, u, v);
                scene.Surface(surface);
                scene.FreeUnlessSurface(surface + new int3(1, 0, 0));
            }
            List<MerkabaOverlapShell.Patch> first = BuildResolved(scene);
            List<MerkabaOverlapShell.Patch> second = BuildResolved(
                scene.ReversedInsertion());
            Assert.That(first.Count, Is.GreaterThan(40));
            AssertPatchesEqual(first, second);
            foreach (MerkabaOverlapShell.Patch patch in first)
            {
                AssertFullSupportFootprint(patch);
                AssertWinding(patch);
                Assert.That(UniqueTriangleVertices(patch), Is.EqualTo(4));
            }
        }

        [Test]
        public void TranslationNegativeAndStorageBoundaries_DoNotChangeOracle()
        {
            int3 normal = new(1, 1, 0);
            Scene referenceScene = Plane(normal, -5, 5, 1);
            List<MerkabaOverlapShell.Patch> reference =
                BuildResolved(referenceScene);
            foreach (int3 translation in new[]
                     {
                         new int3(-513, -257, -129),
                         new int3(7, 31, 255),
                         new int3(8, 32, 256)
                     })
            {
                List<MerkabaOverlapShell.Patch> moved = BuildResolved(
                    referenceScene.Translated(translation));
                AssertRelativeTranslation(reference, moved, translation);
            }
        }

        [Test]
        public void ParallelSheetsAndThinPartition_NeverAverageBranches()
        {
            var parallel = new Scene();
            AddAxisPlane(parallel, 0, 0, -4, 4, 1);
            AddAxisPlane(parallel, 0, 3, -4, 4, 1);
            List<MerkabaOverlapShell.Patch> parallelPatches =
                BuildResolved(parallel);
            AssertSheetHeights(parallelPatches, 0, 0, 3);

            var partition = new Scene();
            AddAxisPlane(partition, 0, 0, -4, 4, -1);
            AddAxisPlane(partition, 0, 1, -4, 4, 1);
            List<MerkabaOverlapShell.Patch> partitionPatches =
                BuildResolved(partition);
            Assert.That(partitionPatches.Any(p => p.Main.x == 0 &&
                p.SurfaceSignature.FreeSign == -1), Is.True);
            Assert.That(partitionPatches.Any(p => p.Main.x == 1 &&
                p.SurfaceSignature.FreeSign == 1), Is.True);
            AssertSheetHeights(partitionPatches, 0, 0, 1);
        }

        [Test]
        public void DistanceTwoSamples_DoNotCreateAnIntermediateSheet()
        {
            var scene = new Scene();
            for (int y = -3; y <= 3; y++)
            for (int z = -3; z <= 3; z++)
            {
                scene.Surface(new int3(0, y, z));
                scene.Surface(new int3(2, y, z));
                scene.FreeUnlessSurface(new int3(1, y, z));
                scene.FreeUnlessSurface(new int3(3, y, z));
            }
            List<MerkabaOverlapShell.Patch> patches = BuildResolved(scene);
            AssertSheetHeights(patches, 0, 0, 2);
            Assert.That(patches.All(p => p.Main.x != 1), Is.True);
        }

        [Test]
        public void ConvexConcaveAndTJunction_AreViewIndependentAndDoNotBridge()
        {
            foreach (Scene scene in new[]
                     {
                         OrthogonalCorner(-1),
                         OrthogonalCorner(1),
                         TJunction()
                     })
            {
                List<MerkabaOverlapShell.Patch> forward = BuildResolved(scene);
                List<MerkabaOverlapShell.Patch> reverse = BuildResolved(
                    scene.ReversedInsertion());
                Assert.That(forward, Is.Not.Empty);
                AssertPatchesEqual(forward, reverse);
                foreach (MerkabaOverlapShell.Patch patch in forward)
                {
                    AssertWinding(patch);
                    Assert.That(UniqueTriangleVertices(patch), Is.EqualTo(4));
                    int axis = patch.SurfaceSignature.ChartAxis;
                    int mainHeight = patch.Main[axis];
                    for (int corner = 0; corner < 4; corner++)
                        Assert.That(Math.Abs(
                            patch.GetCorner(corner).QuarterCoordinate[axis] -
                            mainHeight * 4), Is.LessThanOrEqualTo(4),
                            "A local patch crossed more than one normal layer.");
                }
            }
        }

        [Test]
        public void DoorwayKnownFreeRegion_IsNotBridged()
        {
            var scene = new Scene();
            for (int y = -7; y <= 7; y++)
            for (int z = -4; z <= 4; z++)
            {
                int3 point = new(0, y, z);
                if (Math.Abs(y) >= 3) scene.Surface(point);
                else scene.FreeUnlessSurface(point);
                scene.FreeUnlessSurface(point + new int3(1, 0, 0));
            }
            List<MerkabaOverlapShell.Patch> patches = BuildResolved(scene);
            Assert.That(patches, Is.Not.Empty);
            foreach (MerkabaOverlapShell.Patch patch in patches)
            {
                int minY = Math.Min(patch.Corner00.QuarterCoordinate.y,
                    patch.Corner11.QuarterCoordinate.y);
                int maxY = Math.Max(patch.Corner00.QuarterCoordinate.y,
                    patch.Corner11.QuarterCoordinate.y);
                Assert.That(minY <= 0 && maxY >= 0, Is.False,
                    "No support patch may span the centre of a known doorway.");
            }
        }

        [Test]
        public void DonorEnumerationAndColorReduction_AreByteDeterministic()
        {
            Scene scene = Plane(new int3(1, 0, 0), -4, 4, 1,
                (u, v) => new Color32((byte)(96 + u * 4),
                    (byte)(128 + v * 4), 192, 255));
            List<MerkabaOverlapShell.Patch> forward = BuildResolved(scene);
            List<MerkabaOverlapShell.Patch> reverse = BuildResolved(
                scene.ReversedInsertion());
            AssertPatchesEqual(forward, reverse);
            Assert.That(forward.Any(p => Enumerable.Range(0, 4).Any(c =>
                p.GetCorner(c).ContributorCount > 1)), Is.True);

            foreach (int[] values in Permutations(new[] { -1, 0, 1, 1 }))
                Assert.That(MerkabaOverlapShell.MedianQuarterHeight(values),
                    Is.EqualTo(2));
        }

        [Test]
        public void OracleSource_ContainsOnlyImmediateDisposableM8Authority()
        {
            string source = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaOverlapShell.cs"));
            Assert.That(source, Does.Not.Contain("Camera"));
            Assert.That(source, Does.Not.Contain("Eye"));
            Assert.That(source, Does.Not.Contain("QEF"));
            Assert.That(source, Does.Not.Contain("TSDF"));
            Assert.That(source, Does.Contain("new KernelState[27]"));
            Assert.That(source, Does.Contain("SupportHalfQuarterUnits = 4"));
            Assert.That(source, Does.Contain("NormalDictionary"));
            Assert.That(source, Does.Not.Contain(
                "MinimumAxis(occupiedCrossings)"));
            Assert.That(source, Does.Contain(
                "Overlap-shell queried non-immediate offset"));
        }

        [Test]
        public void GeneratedGpuOracle_IsExactlyTheResolvedR0Authority()
        {
            string path = Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Shaders/" +
                "MerkabaOverlapShell.generated.hlsl");
            string generated = File.ReadAllText(path);
            Assert.That(generated,
                Is.EqualTo(MerkabaOverlapShell.BuildGeneratedHlsl()));
            Assert.That(generated, Does.Contain(
                "#define M8_OVERLAP_NORMAL_COUNT 13u"));
            Assert.That(generated, Does.Contain(
                "M8_OVERLAP_SUPPORT_HALF_QUARTERS 4"));
            Assert.That(generated, Does.Contain(
                "bool M8TryBuildOverlapPatch"));
            Assert.That(generated, Does.Contain(
                "if (!found || tied)"));
            Assert.That(generated, Does.Not.Contain("M8OverlapMinimumAxis"));
            Assert.That(generated, Does.Not.Contain(
                "M8_OVERLAP_HALF_STEP_QUARTERS"));
        }

        private static Scene Plane(int3 normal, int minimum, int maximum,
            int freeSign, Func<int, int, Color32> color = null)
        {
            int chartAxis = FirstNonZeroAxis(normal);
            MerkabaOverlapShell.Axes(chartAxis, out int3 chartNormal,
                out int3 tangent0, out int3 tangent1);
            var scene = new Scene();
            for (int u = minimum; u <= maximum; u++)
            for (int v = minimum; v <= maximum; v++)
            {
                int height = -(normal[FirstNonZeroAxis(tangent0)] * u +
                    normal[FirstNonZeroAxis(tangent1)] * v);
                int3 surface = chartNormal * height +
                    tangent0 * u + tangent1 * v;
                scene.Surface(surface, color?.Invoke(u, v) ?? SurfaceColor);
            }
            foreach (int3 surface in scene.SurfaceCoordinates.ToArray())
                scene.FreeUnlessSurface(surface + normal * freeSign);
            return scene;
        }

        private static void AddAxisPlane(Scene scene, int axis, int height,
            int minimum, int maximum, int freeSign)
        {
            int3 normal = axis == 0 ? new int3(1, 0, 0) :
                axis == 1 ? new int3(0, 1, 0) : new int3(0, 0, 1);
            MerkabaOverlapShell.Axes(axis, out int3 chartNormal,
                out int3 tangent0, out int3 tangent1);
            for (int u = minimum; u <= maximum; u++)
            for (int v = minimum; v <= maximum; v++)
            {
                int3 surface = chartNormal * height + tangent0 * u +
                    tangent1 * v;
                scene.Surface(surface);
                scene.FreeUnlessSurface(surface + normal * freeSign);
            }
        }

        private static List<MerkabaOverlapShell.Patch> BuildPlaneInterior(
            Scene scene, int3 normal, int minimum, int maximum)
        {
            int chartAxis = FirstNonZeroAxis(normal);
            MerkabaOverlapShell.Axes(chartAxis, out int3 chartNormal,
                out int3 tangent0, out int3 tangent1);
            var patches = new List<MerkabaOverlapShell.Patch>();
            for (int u = minimum; u <= maximum; u++)
            for (int v = minimum; v <= maximum; v++)
            {
                int height = -(normal[FirstNonZeroAxis(tangent0)] * u +
                    normal[FirstNonZeroAxis(tangent1)] * v);
                int3 main = chartNormal * height + tangent0 * u + tangent1 * v;
                if (MerkabaOverlapShell.TryBuildPatch(main, scene.Sample,
                        out MerkabaOverlapShell.Patch patch))
                    patches.Add(patch);
            }
            return Sorted(patches);
        }

        private static List<MerkabaOverlapShell.Patch> BuildResolved(Scene scene)
        {
            var patches = new List<MerkabaOverlapShell.Patch>();
            foreach (int3 main in scene.SurfaceCoordinates)
                if (MerkabaOverlapShell.TryBuildPatch(main, scene.Sample,
                        out MerkabaOverlapShell.Patch patch))
                    patches.Add(patch);
            return Sorted(patches);
        }

        private static List<MerkabaOverlapShell.Patch> Sorted(
            IEnumerable<MerkabaOverlapShell.Patch> patches) => patches
            .OrderBy(p => p.Main.x).ThenBy(p => p.Main.y)
            .ThenBy(p => p.Main.z).ToList();

        private static int UniqueTriangleVertices(
            MerkabaOverlapShell.Patch patch) => Enumerable.Range(0,
                MerkabaOverlapShell.VerticesPerPatch).Select(index =>
                patch.GetTriangleVertex(index).QuarterCoordinate)
            .Distinct().Count();

        private static void AssertFullSupportFootprint(
            MerkabaOverlapShell.Patch patch)
        {
            MerkabaOverlapShell.Axes(patch.SurfaceSignature.ChartAxis,
                out _, out int3 tangent0, out int3 tangent1);
            int3 span0 = patch.Corner10.QuarterCoordinate -
                patch.Corner00.QuarterCoordinate;
            int3 span1 = patch.Corner01.QuarterCoordinate -
                patch.Corner00.QuarterCoordinate;
            Assert.That(math.dot(span0, tangent0), Is.EqualTo(8));
            Assert.That(math.dot(span1, tangent1), Is.EqualTo(8));
            Assert.That(math.dot(span0, tangent1), Is.Zero);
            Assert.That(math.dot(span1, tangent0), Is.Zero);
        }

        private static void AssertWinding(MerkabaOverlapShell.Patch patch)
        {
            float3 a = patch.GetTriangleVertex(0).QuarterCoordinate;
            float3 b = patch.GetTriangleVertex(1).QuarterCoordinate;
            float3 c = patch.GetTriangleVertex(2).QuarterCoordinate;
            float3 actual = math.cross(b - a, c - a);
            float3 expected = patch.SurfaceSignature.Normal *
                patch.SurfaceSignature.FreeSign;
            Assert.That(math.dot(actual, expected), Is.GreaterThan(0f));
        }

        private static void AssertProjectedCoverageHasNoPitchGaps(
            IReadOnlyList<MerkabaOverlapShell.Patch> patches, int chartAxis,
            int minimum, int maximum)
        {
            MerkabaOverlapShell.Axes(chartAxis, out _, out int3 tangent0,
                out int3 tangent1);
            for (int u = minimum * 4; u <= maximum * 4; u++)
            for (int v = minimum * 4; v <= maximum * 4; v++)
            {
                bool covered = patches.Any(p =>
                {
                    int centreU = math.dot(p.Main * 4, tangent0);
                    int centreV = math.dot(p.Main * 4, tangent1);
                    return Math.Abs(u - centreU) <= 4 &&
                           Math.Abs(v - centreV) <= 4;
                });
                Assert.That(covered, Is.True, $"uncovered tangent point {u},{v}");
            }
        }

        private static void AssertSheetHeights(
            IEnumerable<MerkabaOverlapShell.Patch> patches, int axis,
            params int[] allowed)
        {
            HashSet<int> quarterHeights = allowed.Select(value => value * 4)
                .ToHashSet();
            foreach (MerkabaOverlapShell.Patch patch in patches)
            for (int corner = 0; corner < 4; corner++)
                Assert.That(quarterHeights.Contains(
                    patch.GetCorner(corner).QuarterCoordinate[axis]),
                    Is.True, $"Parallel surface branches were averaged: " +
                    $"main={patch.Main} normal=" +
                    $"{patch.SurfaceSignature.Normal} corner={corner} " +
                    $"quarter={patch.GetCorner(corner).QuarterCoordinate}.");
        }

        private static void AssertRelativeTranslation(
            IReadOnlyList<MerkabaOverlapShell.Patch> expected,
            IReadOnlyList<MerkabaOverlapShell.Patch> actual, int3 translation)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            int3 quarterTranslation = translation * 4;
            for (int index = 0; index < expected.Count; index++)
            {
                Assert.That(actual[index].SurfaceSignature,
                    Is.EqualTo(expected[index].SurfaceSignature));
                Assert.That(actual[index].Main,
                    Is.EqualTo(expected[index].Main + translation));
                for (int corner = 0; corner < 4; corner++)
                {
                    var left = expected[index].GetCorner(corner);
                    var right = actual[index].GetCorner(corner);
                    Assert.That(right.QuarterCoordinate,
                        Is.EqualTo(left.QuarterCoordinate + quarterTranslation));
                    Assert.That(right.PackedColor, Is.EqualTo(left.PackedColor));
                    Assert.That(right.ContributorCount,
                        Is.EqualTo(left.ContributorCount));
                }
            }
        }

        private static void AssertPatchesEqual(
            IReadOnlyList<MerkabaOverlapShell.Patch> expected,
            IReadOnlyList<MerkabaOverlapShell.Patch> actual)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (int index = 0; index < expected.Count; index++)
            {
                Assert.That(actual[index].Main, Is.EqualTo(expected[index].Main));
                Assert.That(actual[index].SurfaceSignature,
                    Is.EqualTo(expected[index].SurfaceSignature));
                for (int corner = 0; corner < 4; corner++)
                    Assert.That(actual[index].GetCorner(corner),
                        Is.EqualTo(expected[index].GetCorner(corner)));
            }
        }

        private static int FirstNonZeroAxis(int3 value)
        {
            if (value.x != 0) return 0;
            return value.y != 0 ? 1 : 2;
        }

        private static Scene OrthogonalCorner(int freeSign)
        {
            var scene = new Scene();
            AddAxisPlane(scene, 0, 0, -3, 3, freeSign);
            AddAxisPlane(scene, 2, 0, -3, 3, freeSign);
            return scene;
        }

        private static Scene TJunction()
        {
            var scene = new Scene();
            AddAxisPlane(scene, 2, 0, -4, 4, 1);
            for (int y = -3; y <= 3; y++)
            for (int z = 0; z <= 3; z++)
            {
                scene.Surface(new int3(0, y, z));
                scene.FreeUnlessSurface(new int3(-1, y, z));
            }
            return scene;
        }

        private static IEnumerable<int[]> Permutations(int[] values) =>
            Permute((int[])values.Clone(), 0);

        private static IEnumerable<int[]> Permute(int[] values, int index)
        {
            if (index == values.Length)
            {
                yield return (int[])values.Clone();
                yield break;
            }
            for (int swap = index; swap < values.Length; swap++)
            {
                (values[index], values[swap]) = (values[swap], values[index]);
                foreach (int[] result in Permute(values, index + 1))
                    yield return result;
                (values[index], values[swap]) = (values[swap], values[index]);
            }
        }

        private sealed class Scene
        {
            private readonly Dictionary<int3, KernelState> _states = new();
            internal IEnumerable<int3> SurfaceCoordinates => _states
                .Where(pair => pair.Value.IsOccupied).Select(pair => pair.Key);

            internal KernelState Sample(int3 coord) =>
                _states.TryGetValue(coord, out KernelState state)
                    ? state : default;

            internal void Surface(int3 coord) => Surface(coord, SurfaceColor);

            internal void Surface(int3 coord, Color32 color)
            {
                _states[coord] = new KernelState
                {
                    OccupancyEvidence = MerkabaConstants.SurfaceEvidenceScale,
                    PackedColor = KernelState.PackColor(color),
                    ColorConfidence = 1,
                    Flags = MerkabaConstants.OccupiedFlag |
                        MerkabaConstants.NeedsCarveFlag
                };
            }

            internal void FreeUnlessSurface(int3 coord)
            {
                if (_states.TryGetValue(coord, out KernelState state) &&
                    state.IsOccupied)
                    return;
                _states[coord] = new KernelState
                {
                    OccupancyEvidence = -MerkabaConstants.FreeEvidenceScale
                };
            }

            internal Scene Translated(int3 offset)
            {
                var result = new Scene();
                foreach (KeyValuePair<int3, KernelState> pair in _states)
                    result._states.Add(pair.Key + offset, pair.Value);
                return result;
            }

            internal Scene ReversedInsertion()
            {
                var result = new Scene();
                foreach (KeyValuePair<int3, KernelState> pair in _states.Reverse())
                    result._states.Add(pair.Key, pair.Value);
                return result;
            }
        }
    }
}
