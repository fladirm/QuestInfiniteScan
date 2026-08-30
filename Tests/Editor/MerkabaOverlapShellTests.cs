using System;
using System.Collections.Generic;
using System.Linq;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaOverlapShellTests
    {
        private static readonly Color32 SurfaceColor =
            new(72, 144, 216, 255);

        [Test]
        public void FlatPlane_IsOneZeroThicknessTiledSheet()
        {
            Scene scene = AxisHeightField(0, (_, _) => 0, -4, 4, 1);
            List<MerkabaOverlapShell.Patch> patches = BuildInterior(scene,
                0, (_, _) => 0, -3, 3);

            Assert.That(patches.Count, Is.EqualTo(49));
            AssertSharedCorners(patches);
            foreach (MerkabaOverlapShell.Patch patch in patches)
            {
                Assert.That(patch.SurfaceSignature.NormalAxis, Is.EqualTo(0));
                Assert.That(patch.SurfaceSignature.FreeSign, Is.EqualTo(1));
                Assert.That(patch.SurfaceSignature.HasKnownFreeSide, Is.True);
                for (int corner = 0; corner < 4; corner++)
                    Assert.That(patch.GetCorner(corner).QuarterCoordinate.x,
                        Is.Zero);
                AssertWinding(patch);
            }
        }

        [Test]
        public void TranslatedPlane_IsByteIdenticalInSignedCoordinates()
        {
            Scene origin = AxisHeightField(2, (_, _) => 0, -3, 3, -1);
            int3 translation = new(-257, 31, -65);
            Scene moved = origin.Translated(translation);
            List<MerkabaOverlapShell.Patch> reference = BuildAll(origin);
            List<MerkabaOverlapShell.Patch> translated = BuildAll(moved);

            Assert.That(translated.Count, Is.EqualTo(reference.Count));
            int3 quarterTranslation = translation *
                MerkabaOverlapShell.QuarterUnitsPerLatticeStep;
            for (int index = 0; index < reference.Count; index++)
            {
                MerkabaOverlapShell.Patch expected = reference[index];
                MerkabaOverlapShell.Patch actual = translated[index];
                Assert.That(math.all(actual.Main - translation == expected.Main),
                    Is.True);
                Assert.That(actual.SurfaceSignature,
                    Is.EqualTo(expected.SurfaceSignature));
                for (int corner = 0; corner < 4; corner++)
                {
                    MerkabaOverlapShell.Corner left = expected.GetCorner(corner);
                    MerkabaOverlapShell.Corner right = actual.GetCorner(corner);
                    Assert.That(math.all(right.QuarterCoordinate -
                        quarterTranslation == left.QuarterCoordinate), Is.True);
                    Assert.That(right.PackedColor, Is.EqualTo(left.PackedColor));
                    Assert.That(right.ContributorCount,
                        Is.EqualTo(left.ContributorCount));
                }
            }
        }

        [Test]
        public void QuantizedSlopes_HaveContinuousSharedHalfStepCorners()
        {
            foreach (Func<int, int, int> height in new Func<int, int, int>[]
                     {
                         (_, v) => v,
                         (u, v) => MerkabaConstants.FloorDiv(2 * u + v, 3),
                         (u, v) => u + v
                     })
            {
                Scene scene = AxisHeightField(0, height, -5, 5, 1);
                List<MerkabaOverlapShell.Patch> patches = BuildInterior(scene,
                    0, height, -3, 3);
                Assert.That(patches, Is.Not.Empty);
                Assert.That(patches.All(p =>
                    p.SurfaceSignature.NormalAxis == 0), Is.True);
                AssertSharedCorners(patches);
                AssertNeighbourEdges(scene, patches, 0);
                foreach (MerkabaOverlapShell.Patch patch in patches)
                    AssertWinding(patch);
            }
        }

        [Test]
        public void MedianCornerReduction_IsSeedAndEnumerationInvariant()
        {
            int accepted = 0;
            for (int h00 = -2; h00 <= 2; h00++)
            for (int h10 = -2; h10 <= 2; h10++)
            for (int h01 = -2; h01 <= 2; h01++)
            for (int h11 = -2; h11 <= 2; h11++)
            {
                int[] heights = { h00, h10, h01, h11 };
                if (Math.Abs(h00 - h10) > 1 ||
                    Math.Abs(h00 - h01) > 1 ||
                    Math.Abs(h10 - h11) > 1 ||
                    Math.Abs(h01 - h11) > 1)
                    continue;
                int? expected = null;
                foreach (int seed in heights)
                {
                    int[] visible = heights.Where(value =>
                        Math.Abs(value - seed) <= 1).ToArray();
                    foreach (int[] permutation in Permutations(visible))
                    {
                        int reduced = MerkabaOverlapShell.MedianQuarterHeight(
                            permutation);
                        expected ??= reduced;
                        Assert.That(reduced, Is.EqualTo(expected.Value),
                            $"[{string.Join(",", heights)}], seed={seed}");
                    }
                }
                accepted++;
            }
            Assert.That(accepted, Is.GreaterThan(0));
        }

        [Test]
        public void SharedCorners_HaveBitIdenticalContributorColor()
        {
            var scene = new Scene();
            for (int u = -4; u <= 4; u++)
            for (int v = -4; v <= 4; v++)
            {
                int3 coord = new(u + v, u, v);
                scene.Surface(coord, new Color32(
                    (byte)(32 + (u + 4) * 17),
                    (byte)(48 + (v + 4) * 13),
                    (byte)(96 + (u - v + 8) * 7), 255));
            }
            for (int u = -4; u <= 4; u++)
            for (int v = -4; v <= 4; v++)
                scene.FreeUnlessSurface(new int3(u + v + 1, u, v));

            List<MerkabaOverlapShell.Patch> patches = BuildInterior(scene,
                0, (u, v) => u + v, -3, 3);
            AssertSharedCorners(patches);
        }

        [Test]
        public void ParallelSheets_KeepDistinctBranchesAndNeverAverage()
        {
            var scene = new Scene();
            AddAxisPlane(scene, 0, 0, -3, 3, 1);
            AddAxisPlane(scene, 0, 2, -3, 3, 1);
            List<MerkabaOverlapShell.Patch> patches = BuildAll(scene);
            AssertSharedCorners(patches);
            foreach (MerkabaOverlapShell.Patch patch in patches)
            {
                int mainHeight = patch.Main.x;
                Assert.That(mainHeight, Is.EqualTo(0).Or.EqualTo(2));
                for (int corner = 0; corner < 4; corner++)
                    Assert.That(patch.GetCorner(corner).QuarterCoordinate.x,
                        Is.EqualTo(mainHeight * 4));
            }
        }

        [Test]
        public void ThinPartition_PreservesBothPhysicalSidesAndWinding()
        {
            var scene = new Scene();
            AddAxisPlane(scene, 0, 0, -3, 3, -1);
            AddAxisPlane(scene, 0, 2, -3, 3, 1);
            List<MerkabaOverlapShell.Patch> patches = BuildAll(scene);
            Assert.That(patches.Any(p => p.Main.x == 0 &&
                p.SurfaceSignature.FreeSign == -1), Is.True);
            Assert.That(patches.Any(p => p.Main.x == 2 &&
                p.SurfaceSignature.FreeSign == 1), Is.True);
            AssertSharedCorners(patches);
            foreach (MerkabaOverlapShell.Patch patch in patches)
                AssertWinding(patch);
        }

        [Test]
        public void ConvexConcaveAndTJunctions_AreDeterministicAndDoNotBridge()
        {
            foreach (Scene scene in new[]
                     {
                         OrthogonalCorner(-1),
                         OrthogonalCorner(1),
                         TJunction()
                     })
            {
                List<MerkabaOverlapShell.Patch> forward = BuildAll(scene);
                List<MerkabaOverlapShell.Patch> reverse = BuildAll(
                    scene.ReversedInsertion());
                AssertPatchesEqual(forward, reverse);
                AssertSharedCorners(forward);
                foreach (MerkabaOverlapShell.Patch patch in forward)
                {
                    var mainNormal = patch.Main[
                        patch.SurfaceSignature.NormalAxis] * 4;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        int normal = patch.GetCorner(corner).QuarterCoordinate[
                            patch.SurfaceSignature.NormalAxis];
                        Assert.That(normal, Is.EqualTo(mainNormal),
                            "Orthogonal sheets may meet, but no patch may bridge them.");
                    }
                }
            }
        }

        [Test]
        public void IsolatedMain_IsDeterministicAndHasNoBackside()
        {
            var scene = new Scene();
            scene.Surface(new int3(0));
            Assert.That(MerkabaOverlapShell.TryBuildPatch(new int3(0),
                scene.Sample, out MerkabaOverlapShell.Patch patch), Is.True);
            Assert.That(patch.SurfaceSignature.NormalAxis, Is.Zero);
            Assert.That(patch.SurfaceSignature.FreeSign, Is.EqualTo(1));
            Assert.That(patch.SurfaceSignature.HasKnownFreeSide, Is.False);
            Assert.That(MerkabaOverlapShell.TrianglesPerPatch, Is.EqualTo(2));
            Assert.That(Enumerable.Range(0,
                    MerkabaOverlapShell.VerticesPerPatch)
                .Select(index => patch.GetTriangleVertex(index)
                    .QuarterCoordinate).Distinct().Count(), Is.EqualTo(4));
            AssertWinding(patch);
        }

        [Test]
        public void DistanceTwoSurface_NeverBecomesContributor()
        {
            var isolated = new Scene();
            isolated.Surface(new int3(0));
            var separated = new Scene();
            separated.Surface(new int3(0));
            separated.Surface(new int3(0, 2, 0));
            MerkabaOverlapShell.TryBuildPatch(new int3(0), isolated.Sample,
                out MerkabaOverlapShell.Patch reference);
            MerkabaOverlapShell.TryBuildPatch(new int3(0), separated.Sample,
                out MerkabaOverlapShell.Patch actual);
            AssertPatchEqual(reference, actual);
        }

        [Test]
        public void OracleSource_HasNoViewOrPersistentGeometryAuthority()
        {
            string source = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaOverlapShell.cs"));
            Assert.That(source, Does.Not.Contain("Camera"));
            Assert.That(source, Does.Not.Contain("Eye"));
            Assert.That(source, Does.Not.Contain("Mesh"));
            Assert.That(source, Does.Not.Contain("QEF"));
            Assert.That(source, Does.Not.Contain("TSDF"));
            Assert.That(source, Does.Contain("new KernelState[27]"));
            Assert.That(source, Does.Contain(
                "Overlap-shell queried non-immediate offset"));
        }

        private static Scene AxisHeightField(int normalAxis,
            Func<int, int, int> height, int minimum, int maximum,
            int freeSign)
        {
            var scene = new Scene();
            MerkabaOverlapShell.Axes(normalAxis, out int3 normal,
                out int3 tangent0, out int3 tangent1);
            for (int u = minimum; u <= maximum; u++)
            for (int v = minimum; v <= maximum; v++)
            {
                int3 surface = tangent0 * u + tangent1 * v +
                    normal * height(u, v);
                scene.Surface(surface);
            }
            for (int u = minimum; u <= maximum; u++)
            for (int v = minimum; v <= maximum; v++)
            {
                int3 surface = tangent0 * u + tangent1 * v +
                    normal * height(u, v);
                scene.FreeUnlessSurface(surface + normal * freeSign);
            }
            return scene;
        }

        private static void AddAxisPlane(Scene scene, int normalAxis,
            int height, int minimum, int maximum, int freeSign)
        {
            MerkabaOverlapShell.Axes(normalAxis, out int3 normal,
                out int3 tangent0, out int3 tangent1);
            for (int u = minimum; u <= maximum; u++)
            for (int v = minimum; v <= maximum; v++)
                scene.Surface(normal * height + tangent0 * u + tangent1 * v);
            for (int u = minimum; u <= maximum; u++)
            for (int v = minimum; v <= maximum; v++)
                scene.FreeUnlessSurface(normal * (height + freeSign) +
                    tangent0 * u + tangent1 * v);
        }

        private static List<MerkabaOverlapShell.Patch> BuildInterior(
            Scene scene, int normalAxis, Func<int, int, int> height,
            int minimum, int maximum)
        {
            MerkabaOverlapShell.Axes(normalAxis, out int3 normal,
                out int3 tangent0, out int3 tangent1);
            var result = new List<MerkabaOverlapShell.Patch>();
            for (int u = minimum; u <= maximum; u++)
            for (int v = minimum; v <= maximum; v++)
            {
                int3 main = tangent0 * u + tangent1 * v +
                    normal * height(u, v);
                Assert.That(MerkabaOverlapShell.TryBuildPatch(main,
                    scene.Sample, out MerkabaOverlapShell.Patch patch), Is.True);
                result.Add(patch);
            }
            return result.OrderBy(p => p.Main.x).ThenBy(p => p.Main.y)
                .ThenBy(p => p.Main.z).ToList();
        }

        private static List<MerkabaOverlapShell.Patch> BuildAll(Scene scene)
        {
            var result = new List<MerkabaOverlapShell.Patch>();
            foreach (int3 main in scene.SurfaceCoordinates.OrderBy(p => p.x)
                         .ThenBy(p => p.y).ThenBy(p => p.z))
            {
                Assert.That(MerkabaOverlapShell.TryBuildPatch(main,
                    scene.Sample, out MerkabaOverlapShell.Patch patch), Is.True);
                result.Add(patch);
            }
            return result;
        }

        private static void AssertSharedCorners(
            IEnumerable<MerkabaOverlapShell.Patch> patches)
        {
            var corners = new Dictionary<(byte, sbyte, int3),
                MerkabaOverlapShell.Corner>();
            var mains = new HashSet<int3>();
            foreach (MerkabaOverlapShell.Patch patch in patches)
            {
                Assert.That(mains.Add(patch.Main), Is.True,
                    $"MAIN {patch.Main} emitted more than one patch");
                MerkabaOverlapShell.Axes(patch.SurfaceSignature.NormalAxis,
                    out _, out int3 tangent0, out int3 tangent1);
                int3 tangentSpan0 = patch.Corner10.QuarterCoordinate -
                    patch.Corner00.QuarterCoordinate;
                int3 tangentSpan1 = patch.Corner01.QuarterCoordinate -
                    patch.Corner00.QuarterCoordinate;
                Assert.That(math.dot(tangentSpan0, tangent0), Is.EqualTo(4));
                Assert.That(math.dot(tangentSpan1, tangent1), Is.EqualTo(4));
                Assert.That(math.dot(tangentSpan0, tangent1), Is.Zero);
                Assert.That(math.dot(tangentSpan1, tangent0), Is.Zero);
            for (int index = 0; index < 4; index++)
            {
                MerkabaOverlapShell.Corner corner = patch.GetCorner(index);
                var key = (patch.SurfaceSignature.NormalAxis,
                    patch.SurfaceSignature.FreeSign,
                    corner.QuarterCoordinate);
                if (corners.TryGetValue(key, out MerkabaOverlapShell.Corner old))
                    Assert.That(corner, Is.EqualTo(old),
                        $"shared corner {key} differs");
                else
                    corners.Add(key, corner);
            }
            }
        }

        private static void AssertNeighbourEdges(Scene scene,
            IReadOnlyList<MerkabaOverlapShell.Patch> patches, int normalAxis)
        {
            var byMain = patches.ToDictionary(p => p.Main);
            MerkabaOverlapShell.Axes(normalAxis, out int3 normal,
                out int3 tangent0, out int3 tangent1);
            foreach (MerkabaOverlapShell.Patch patch in patches)
            {
                foreach ((int3 tangent, int own0, int own1,
                             int neighbour0, int neighbour1) in new[]
                         {
                             (tangent0, 1, 2, 0, 3),
                             (tangent1, 3, 2, 0, 1)
                         })
                {
                    int mainHeight = patch.Main[normalAxis];
                    int3 column = patch.Main + tangent;
                    MerkabaOverlapShell.Patch? neighbour = null;
                    for (int delta = -1; delta <= 1; delta++)
                    {
                        int3 candidate = column + normal * delta;
                        if (byMain.TryGetValue(candidate, out var found))
                        {
                            neighbour = found;
                            break;
                        }
                    }
                    if (!neighbour.HasValue) continue;
                    Assert.That(patch.GetCorner(own0),
                        Is.EqualTo(neighbour.Value.GetCorner(neighbour0)));
                    Assert.That(patch.GetCorner(own1),
                        Is.EqualTo(neighbour.Value.GetCorner(neighbour1)));
                }
            }
        }

        private static void AssertWinding(MerkabaOverlapShell.Patch patch)
        {
            float3 a = patch.GetTriangleVertex(0).QuarterCoordinate;
            float3 b = patch.GetTriangleVertex(1).QuarterCoordinate;
            float3 c = patch.GetTriangleVertex(2).QuarterCoordinate;
            float3 normal = math.cross(b - a, c - a);
            Assert.That(normal[patch.SurfaceSignature.NormalAxis] *
                patch.SurfaceSignature.FreeSign, Is.GreaterThan(0f));
        }

        private static Scene OrthogonalCorner(int freeSign)
        {
            var scene = new Scene();
            AddAxisPlane(scene, 0, 0, -2, 2, freeSign);
            AddAxisPlane(scene, 2, 0, -2, 2, freeSign);
            return scene;
        }

        private static Scene TJunction()
        {
            var scene = AxisHeightField(2, (_, _) => 0, -3, 3, 1);
            for (int y = -2; y <= 2; y++)
            for (int z = 0; z <= 2; z++)
                scene.Surface(new int3(0, y, z));
            for (int y = -2; y <= 2; y++)
            for (int z = 0; z <= 2; z++)
                scene.FreeUnlessSurface(new int3(-1, y, z));
            return scene;
        }

        private static void AssertPatchesEqual(
            IReadOnlyList<MerkabaOverlapShell.Patch> expected,
            IReadOnlyList<MerkabaOverlapShell.Patch> actual)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (int index = 0; index < expected.Count; index++)
                AssertPatchEqual(expected[index], actual[index]);
        }

        private static void AssertPatchEqual(MerkabaOverlapShell.Patch expected,
            MerkabaOverlapShell.Patch actual)
        {
            Assert.That(math.all(actual.Main == expected.Main), Is.True);
            Assert.That(actual.SurfaceSignature,
                Is.EqualTo(expected.SurfaceSignature));
            for (int corner = 0; corner < 4; corner++)
                Assert.That(actual.GetCorner(corner),
                    Is.EqualTo(expected.GetCorner(corner)));
        }

        private static IEnumerable<int[]> Permutations(int[] values)
        {
            int[] copy = (int[])values.Clone();
            return Permute(copy, 0);
        }

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
