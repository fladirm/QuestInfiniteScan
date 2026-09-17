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
        // Plane encoding (octahedral normal, quantized offset) moves a knot
        // by up to a few 1e-5 m; geometry assertions above that use this.
        private const float QuantizationTolerance = 1e-4f;

        [Test]
        public void IsolatedMeasuredMain_EmitsOne25mmMembranePatch()
        {
            KernelState state = Surface(new int3(0), new float3(1, 0, 0), 0f);

            Assert.That(MerkabaOverlapShell.TryResolvePatch(new int3(0), state,
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
            Assert.That(MerkabaOverlapShell.TryResolvePatch(new int3(0), state,
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

            Assert.That(MerkabaOverlapShell.TryResolvePatch(main, context,
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

            Assert.That(MerkabaOverlapShell.TryResolvePatch(new int3(0, 0, 0),
                context, out MerkabaOverlapShell.Patch first), Is.True);
            Assert.That(MerkabaOverlapShell.TryResolvePatch(new int3(0, 1, 0),
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

            Assert.That(MerkabaOverlapShell.TryResolvePatch(main, context,
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

            Assert.That(MerkabaOverlapShell.TryResolvePatch(main, context,
                out MerkabaOverlapShell.Patch patch), Is.True);

            KernelState.DecodeSurfacePlane(context[main].Flags,
                out float3 decodedNormal, out float decodedOffset);
            Assert.That(patch.Normal.z, Is.GreaterThan(0f));
            Assert.That(Corners(patch).All(value => math.abs(
                math.dot(value, decodedNormal) - decodedOffset) <= Tolerance),
                Is.True);
        }

        // Rewritten for C5.2 (plan section 8, "two parallel sheets beyond g"):
        // the former fixture put the sheets exactly g = 15 mm apart, which
        // the line-node relation R now joins into one branch by contract.
        // The sheets are 17 mm apart here and keep one node each per line.
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
                    0.004f));
                context.Add(secondOwner, Surface(secondOwner,
                    new float3(1, 0, 0),
                    -0.004f));
            }

            Assert.That(MerkabaOverlapShell.TryResolvePatch(new int3(0), context,
                out MerkabaOverlapShell.Patch firstPatch), Is.True);
            Assert.That(MerkabaOverlapShell.TryResolvePatch(new int3(1, 0, 0),
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
            Assert.That(expectedSeparation, Is.GreaterThan(
                MerkabaOverlapShell.BranchHeightGap));
            for (int corner = 0; corner < 4; corner++)
            {
                Assert.That(firstPatch.GetCorner(corner).LineAddress, Is.EqualTo(
                    secondPatch.GetCorner(corner).LineAddress));
                Assert.That(firstPatch.GetCorner(corner).Key.Equals(
                    secondPatch.GetCorner(corner).Key), Is.False,
                    "one node per sheet on every shared line");
            }
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

            Assert.That(MerkabaOverlapShell.TryResolvePatch(main, context,
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

            Assert.That(MerkabaOverlapShell.TryResolvePatch(new int3(0),
                originContext, out MerkabaOverlapShell.Patch origin), Is.True);
            Assert.That(MerkabaOverlapShell.TryResolvePatch(translation,
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
            Assert.That(generated, Does.Contain("M8MembraneCanonicalChart"));
            Assert.That(generated, Does.Contain("M8_MEMBRANE_HALF_PITCH"));
            Assert.That(generated, Does.Not.Contain("M8EmitReadoutGlyph"));
            Assert.That(generated, Does.Not.Contain("Camera"));
            Assert.That(generated, Does.Not.Contain("Eye"));
        }

        // ---- review fixtures: the skin is the connected measured sheet ----

        private static Dictionary<int3, KernelState> Wall(int3 origin,
            int width, int height, float3 normal, HashSet<int3> holes = null)
        {
            var context = new Dictionary<int3, KernelState>();
            KernelState.DecodeSurfacePlane(Surface(origin, normal, 0f).Flags,
                out float3 decoded, out _);
            float planeConstant = math.dot((float3)origin *
                MerkabaConstants.LatticeStep, decoded);
            MerkabaOverlapShell.TangentAxes(
                MerkabaOverlapShell.DominantAxis(decoded), out int tangent0,
                out int tangent1);
            for (int a = 0; a < width; a++)
            for (int b = 0; b < height; b++)
            {
                int3 coord = origin;
                coord[tangent0] += a;
                coord[tangent1] += b;
                if (holes != null && holes.Contains(coord)) continue;
                float offset = planeConstant - math.dot((float3)coord *
                    MerkabaConstants.LatticeStep, decoded);
                context[coord] = Surface(coord, decoded, offset);
            }
            return context;
        }

        private static Dictionary<(int3, int), List<MerkabaOverlapShell.Corner>>
            Knots(IReadOnlyDictionary<int3, KernelState> context,
                out int patches)
        {
            var knots = new Dictionary<(int3, int),
                List<MerkabaOverlapShell.Corner>>();
            patches = 0;
            foreach (int3 coord in context.Keys.OrderBy(value => value.x)
                         .ThenBy(value => value.y).ThenBy(value => value.z))
            {
                if (!MerkabaOverlapShell.TryResolvePatch(coord, context,
                        out MerkabaOverlapShell.Patch patch)) continue;
                patches++;
                for (int index = 0; index < 4; index++)
                {
                    MerkabaOverlapShell.Corner corner = patch.GetCorner(index);
                    var key = (corner.LineAddress, corner.Chart);
                    if (!knots.TryGetValue(key, out var list))
                        knots[key] = list = new List<MerkabaOverlapShell.Corner>();
                    list.Add(corner);
                }
            }
            return knots;
        }

        private static void AssertKnotsShared(
            Dictionary<(int3, int), List<MerkabaOverlapShell.Corner>> knots)
        {
            foreach (var pair in knots)
                for (int index = 1; index < pair.Value.Count; index++)
                    Assert.That(pair.Value[index].Equals(pair.Value[0]), Is.True,
                        $"knot {pair.Key} differs between its patches");
        }

        [Test]
        public void WallKnotsAreSharedBitIdenticallyAndScaleWithCells()
        {
            Dictionary<int3, KernelState> context = Wall(new int3(-1, -1, 0),
                3, 3, new float3(0f, 0f, 1f));
            var knots = Knots(context, out int patches);
            Assert.That(patches, Is.EqualTo(9), "one quad per measured kernel");
            // A 3x3 sheet has 4x4 knots; every knot is one shared vertex.
            Assert.That(knots.Count, Is.EqualTo(16));
            AssertKnotsShared(knots);
            Assert.That(knots.Values.Count(list => list.Count == 4),
                Is.EqualTo(4), "interior knots belong to four patches");
        }

        [Test]
        public void EightByEightWallAcrossTileBoundaryHasOneKnotPerCorner()
        {
            // Tile boundary at x = 8 and y = 8: knots on it are one identity.
            Dictionary<int3, KernelState> context = Wall(new int3(4, 4, 0),
                8, 8, new float3(0f, 0f, 1f));
            var knots = Knots(context, out int patches);
            Assert.That(patches, Is.EqualTo(64));
            Assert.That(knots.Count, Is.EqualTo(81));
            AssertKnotsShared(knots);
        }

        [Test]
        public void DoorwayLeavesARealHoleAndNoBridge()
        {
            var holes = new HashSet<int3>();
            for (int x = -1; x <= 1; x++)
            for (int y = -2; y <= 0; y++)
                holes.Add(new int3(x, y, 0));
            Dictionary<int3, KernelState> context = Wall(new int3(-4, -2, 0),
                9, 5, new float3(0f, 0f, 1f), holes);
            var knots = Knots(context, out int patches);
            Assert.That(patches, Is.EqualTo(context.Count));
            AssertKnotsShared(knots);
            // The hole interior has no knot at all: nothing bridges it.
            Assert.That(knots.Keys.Any(key => key.Item1.x == 0 &&
                key.Item1.y == -2), Is.False);
        }

        [Test]
        public void NoisyNormalsAcrossTheChartBoundaryStillContribute()
        {
            // Two neighbours on one physical wall whose noisy normals sit on
            // either side of the 45 degree chart boundary (FINDING B).
            float3 left = math.normalize(new float3(0.72f, 0f, 0.69f));
            float3 right = math.normalize(new float3(0.69f, 0f, 0.72f));
            var context = new Dictionary<int3, KernelState>
            {
                [new int3(0, 0, 0)] = Surface(new int3(0, 0, 0), left, 0f),
                [new int3(1, 0, 0)] = Surface(new int3(1, 0, 0), right, 0f)
            };
            Assert.That(MerkabaOverlapShell.TryResolvePatch(new int3(0, 0, 0),
                context, out MerkabaOverlapShell.Patch first), Is.True);
            Assert.That(MerkabaOverlapShell.TryResolvePatch(new int3(1, 0, 0),
                context, out MerkabaOverlapShell.Patch second), Is.True);
            // The knot between them is solved from both planes, so it lies
            // within half a pitch of each plane instead of tearing.
            foreach (MerkabaOverlapShell.Patch patch in new[] { first, second })
            foreach (float3 corner in Corners(patch))
            {
                KernelState.DecodeSurfacePlane(context[patch.Main].Flags,
                    out float3 normal, out float offset);
                float residual = math.abs(math.dot(corner - (float3)patch.Main *
                    MerkabaConstants.LatticeStep, normal) - offset);
                Assert.That(residual, Is.LessThan(
                    MerkabaOverlapShell.MembraneHalfPitch));
            }
        }

        [Test]
        public void QuantizedSlopePublishesOneQuadPerKernelWithSharedKnots()
        {
            float3 normal = math.normalize(new float3(-0.45f, 0.89f, 0f));
            var context = new Dictionary<int3, KernelState>();
            for (int x = -5; x <= 5; x++)
            for (int z = -1; z <= 1; z++)
            {
                int3 coord = new(x, (int)math.round(x / 2f), z);
                context[coord] = Surface(coord, normal, 0f);
            }
            var knots = Knots(context, out int patches);
            Assert.That(patches, Is.EqualTo(context.Count));
            AssertKnotsShared(knots);
        }

        // ---- knot-line fixtures: a knot is a function of its line, chart,
        // side and layer; identical winners are one identity ----

        private static Dictionary<int3, KernelState> Sheet(
            IEnumerable<int3> coords, float3 normal, float planeConstant)
        {
            var context = new Dictionary<int3, KernelState>();
            KernelState.DecodeSurfacePlane(
                Surface(new int3(0), normal, 0f).Flags, out float3 decoded,
                out _);
            foreach (int3 coord in coords)
            {
                float offset = planeConstant - math.dot((float3)coord *
                    MerkabaConstants.LatticeStep, decoded);
                if (math.abs(offset) > MerkabaConstants.SurfacePlaneOffsetRange)
                    continue;
                context[coord] = Surface(coord, decoded, offset);
            }
            return context;
        }

        [Test]
        public void SlopedSheetKnotsAreIdenticalFromEveryLayerAcrossTileEdge()
        {
            // 45 degree sheet x = z through the tile edge x = 8: the MAINs on
            // both sides of a shared knot sit in different normal layers and
            // still resolve the identical knot (same winners, same numbers).
            var coords = new List<int3>();
            for (int x = 4; x <= 12; x++)
            for (int y = 0; y <= 3; y++)
                coords.Add(new int3(x, y, x));
            float3 normal = math.normalize(new float3(-1f, 0f, 1f));
            Dictionary<int3, KernelState> context = Sheet(coords, normal, 0f);
            var knots = Knots(context, out int patches);
            Assert.That(patches, Is.EqualTo(context.Count));
            AssertKnotsShared(knots);
            Assert.That(knots.Values.Count(list => list.Count == 4),
                Is.GreaterThan(0), "interior knots belong to four patches");
            foreach (var pair in knots)
                Assert.That(pair.Value.Select(c => c.Key).Distinct().Count(),
                    Is.EqualTo(1), $"knot {pair.Key} has one identity");
        }

        [Test]
        public void ThreeSheetsStackedInOneColumnStayThreeKnots()
        {
            var context = new Dictionary<int3, KernelState>();
            foreach (int z in new[] { 0, 6, 12 })
                foreach (KeyValuePair<int3, KernelState> pair in
                         Wall(new int3(-1, -1, z), 3, 3, new float3(0f, 0f, 1f)))
                    context[pair.Key] = pair.Value;
            var knots = Knots(context, out int patches);
            Assert.That(patches, Is.EqualTo(27));
            foreach (var pair in knots)
            {
                var identities = pair.Value.Select(c => c.Key).Distinct()
                    .ToArray();
                Assert.That(identities.Length, Is.EqualTo(3),
                    $"line {pair.Key} carries one knot per sheet");
                foreach (MerkabaOverlapShell.Corner corner in pair.Value)
                    Assert.That(math.abs(corner.GridPosition.z -
                        math.round(corner.GridPosition.z /
                            (6f * MerkabaConstants.LatticeStep)) *
                        6f * MerkabaConstants.LatticeStep),
                        Is.LessThan(QuantizationTolerance),
                        "no bridging between sheets");
            }
        }

        [Test]
        public void ThinLeafHasTwoSidesThatNeverMix()
        {
            // Front face at z = 0 (normal +Z, free above), back face at z = -1
            // (normal -Z, free below): two sides, two knot families per line.
            var context = new Dictionary<int3, KernelState>();
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                context[new int3(x, y, 0)] = Surface(new int3(x, y, 0),
                    new float3(0f, 0f, 1f), 0f);
                context[new int3(x, y, -1)] = Surface(new int3(x, y, -1),
                    new float3(0f, 0f, -1f), 0f);
                context[new int3(x, y, 1)] = StrongFree();
                context[new int3(x, y, -2)] = StrongFree();
            }
            var knots = Knots(context, out int patches);
            Assert.That(patches, Is.EqualTo(18));
            foreach (var pair in knots)
            {
                Assert.That(pair.Value.Select(c => c.Key.Side).Distinct()
                    .Count(), Is.EqualTo(2), $"line {pair.Key} has two sides");
                foreach (MerkabaOverlapShell.Corner corner in pair.Value)
                    Assert.That(corner.Key.Side == 2
                        ? corner.GridPosition.z
                        : corner.GridPosition.z + MerkabaConstants.LatticeStep,
                        Is.EqualTo(0f).Within(QuantizationTolerance),
                        "each side keeps its own height");
            }
        }

        [Test]
        public void NoisyNormalsAroundFortyFiveDegreesShareOneCanonicalChart()
        {
            // One physical sheet x + z = const whose measured normals scatter
            // +-10 degrees around 38 degrees: single cells fall on either
            // side of the 45 degree chart boundary, the canonical chart
            // (26-neighbour sheet mean) does not.
            float3 baseNormal = math.normalize(new float3(0.788f, 0f, 0.616f));
            var context = new Dictionary<int3, KernelState>();
            for (int x = -2; x <= 2; x++)
            for (int y = -2; y <= 2; y++)
            {
                int3 coord = new(x, y, -x);
                uint hash = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                hash ^= hash >> 13;
                hash *= 0x5bd1e995u;
                float angle = ((hash & 1023u) / 1023f * 2f - 1f) *
                    math.radians(10f);
                float3 normal = math.normalize(new float3(
                    baseNormal.x * math.cos(angle) - baseNormal.z * math.sin(angle),
                    0f,
                    baseNormal.x * math.sin(angle) + baseNormal.z * math.cos(angle)));
                context[coord] = Surface(coord, normal, 0f);
            }
            int scatteredCharts = context.Select(pair =>
            {
                KernelState.DecodeSurfacePlane(pair.Value.Flags,
                    out float3 normal, out _);
                return MerkabaOverlapShell.DominantAxis(normal);
            }).Distinct().Count();
            Assert.That(scatteredCharts, Is.EqualTo(2),
                "the fixture really straddles the chart boundary");
            var charts = new HashSet<int>();
            foreach (int3 coord in context.Keys)
                charts.Add(MerkabaOverlapShell.CanonicalChart(coord,
                    context[coord], context));
            Assert.That(charts.Count, Is.EqualTo(1),
                "one canonical chart for one sheet");
        }

        // ---- line-node membrane (contract C5.1-C5.4, plan section 8) ----

        [Test]
        public void ComponentSpanningThreeLayersHasNoNodeAndItsPatchesAreAbsent()
        {
            // Line through the +X+Y corner of the origin, chart Z: four uses
            // in layers 0..3, consecutive heights 12 mm apart (<= g).
            var line = new MerkabaOverlapShell.LineKey(new int3(1, 1, 0), 2);
            var context = new Dictionary<int3, KernelState>();
            MerkabaMembraneFixtures.AddSpanChain(context, new int3(0), 1);
            Assert.That(context.Count, Is.EqualTo(4));
            Assert.That(MerkabaOverlapShell.TrySolveNode(line, 0, 0, context,
                null, out _), Is.False);
            foreach (int3 main in context.Keys)
                Assert.That(MerkabaOverlapShell.TryResolvePatch(main, context,
                    out _), Is.False, $"MAIN {main} uses the invalid branch");

            // Positive control: without the fourth use the span is two, the
            // common window is layer 1 and its use is the only contributor.
            var control = new Dictionary<int3, KernelState>();
            MerkabaMembraneFixtures.AddSpanChain(control, new int3(0), 1,
                withoutLastUse: true);
            Assert.That(control.Count, Is.EqualTo(3));
            Assert.That(MerkabaOverlapShell.TrySolveNode(line, 0, 0, control,
                null, out MerkabaOverlapShell.Node node), Is.True);
            Assert.That(node.GridPosition.z, Is.EqualTo(LineHeight(control,
                new int3(0, 1, 1), 2, line.LineAddress)).Within(1e-6f));
            var keys = new HashSet<MerkabaOverlapShell.NodeKey>();
            foreach (int3 main in control.Keys)
            {
                Assert.That(MerkabaOverlapShell.TryResolvePatch(main, control,
                    out MerkabaOverlapShell.Patch patch), Is.True, $"{main}");
                for (int corner = 0; corner < 4; corner++)
                    if (math.all(patch.GetCorner(corner).LineAddress ==
                            line.LineAddress))
                        keys.Add(patch.GetCorner(corner).Key);
            }
            Assert.That(keys.Count, Is.EqualTo(1), "one shared node");
        }

        [Test]
        public void FreeSeparatorBlocksMembershipBetweenColumns()
        {
            // Uses of one line in columns 0 (layer 0) and 2 (layer 2), 10 mm
            // apart. Joined, their window is layer 1 and holds no
            // contributor; KNOWN FREE in column 0 at layer 2 separates them.
            var line = new MerkabaOverlapShell.LineKey(new int3(1, 1, 0), 2);
            var context = new Dictionary<int3, KernelState>();
            MerkabaMembraneFixtures.Put(context, new int3(0, 0, 0),
                new float3(0f, 0f, 1f), 0.020f);
            MerkabaMembraneFixtures.Put(context, new int3(1, 0, 2),
                new float3(0f, 0f, 1f), 0.030f);
            Assert.That(MerkabaOverlapShell.TrySolveNode(line, 0, 0, context,
                null, out _), Is.False, "joined branch has no contributor");

            context[new int3(0, 0, 2)] = MerkabaMembraneFixtures.KnownFree();
            Assert.That(MerkabaOverlapShell.TrySolveNode(line, 0, 0, context,
                null, out MerkabaOverlapShell.Node lower), Is.True);
            Assert.That(MerkabaOverlapShell.TrySolveNode(line, 2, 2, context,
                null, out MerkabaOverlapShell.Node upper), Is.True);
            Assert.That(lower.Key.Equals(upper.Key), Is.False);
            // One offset quantization step (0.2 mm) of the stored planes.
            float step = MerkabaConstants.SurfacePlaneOffsetRange / 127f;
            Assert.That(lower.GridPosition.z, Is.EqualTo(0.020f).Within(step));
            Assert.That(upper.GridPosition.z, Is.EqualTo(0.030f).Within(step));
            Assert.That(MerkabaOverlapShell.TryResolvePatch(new int3(0, 0, 0),
                context, out _), Is.True);
            Assert.That(MerkabaOverlapShell.TryResolvePatch(new int3(1, 0, 2),
                context, out _), Is.True);
        }

        [Test]
        public void ShrubLeavesStayOnTheirPlanesAndShareNodes()
        {
            Dictionary<int3, KernelState> context =
                MerkabaMembraneFixtures.Shrub();
            var cache = new MerkabaOverlapShell.SolveCache();
            var nodes = new Dictionary<MerkabaOverlapShell.NodeKey, float3>();
            var uses = new Dictionary<MerkabaOverlapShell.NodeKey, int>();
            int patches = 0;
            foreach (int3 main in context.Keys.OrderBy(value => value.x)
                         .ThenBy(value => value.y).ThenBy(value => value.z))
            {
                if (!MerkabaOverlapShell.TryResolvePatch(main, context, cache,
                        out MerkabaOverlapShell.Patch patch))
                    continue;
                patches++;
                KernelState.DecodeSurfacePlane(context[main].Flags,
                    out float3 normal, out float offset);
                float constant = math.dot((float3)main *
                    MerkabaConstants.LatticeStep, normal) + offset;
                for (int corner = 0; corner < 4; corner++)
                {
                    MerkabaOverlapShell.Corner value = patch.GetCorner(corner);
                    float3 next = patch.GetCorner((corner + 1) & 3).GridPosition;
                    Assert.That(math.abs(math.dot(value.GridPosition, normal) -
                        constant), Is.LessThanOrEqualTo(
                        MerkabaOverlapShell.BranchHeightGap), $"{main}");
                    Assert.That(math.distance(value.GridPosition, next),
                        Is.LessThanOrEqualTo(MerkabaOverlapShell.MembraneMaxEdge),
                        $"{main}");
                    if (nodes.TryGetValue(value.Key, out float3 known))
                        Assert.That(math.all(math.asint(known) ==
                            math.asint(value.GridPosition)), Is.True,
                            "a node key is one position");
                    nodes[value.Key] = value.GridPosition;
                    uses[value.Key] = uses.TryGetValue(value.Key,
                        out int count) ? count + 1 : 1;
                }
            }
            Assert.That(context.Count, Is.GreaterThan(100));
            Assert.That(patches, Is.GreaterThanOrEqualTo(context.Count * 9 / 10),
                "coverage of the measured leaves");
            Assert.That(uses.Values.Count(count => count > 1),
                Is.GreaterThan(nodes.Count / 2), "leaves are shared skins");
        }

        // Checkerboard noise keeps the 3x3 sheet mean near 48.6 degrees while
        // single cells fall on both sides of the 45 degree chart boundary.
        // Interior cells only: a fixture border has no sheet to average.
        [TestCase(4, 15f)]
        [TestCase(16, 12f)]
        public void NoisyDiagonalAroundFortyEightDegreesHasOneCanonicalChart(
            int halfSize, float amplitude)
        {
            var context = new Dictionary<int3, KernelState>();
            MerkabaMembraneFixtures.AddNoisyDiagonal(context, new int3(0),
                halfSize, 48.6f, amplitude);
            var raw = new HashSet<int>();
            var canonical = new HashSet<int>();
            foreach (KeyValuePair<int3, KernelState> pair in context)
            {
                KernelState.DecodeSurfacePlane(pair.Value.Flags,
                    out float3 normal, out _);
                raw.Add(MerkabaOverlapShell.DominantAxis(normal));
                if (math.abs(pair.Key.x) < halfSize &&
                    math.abs(pair.Key.y) < halfSize)
                    canonical.Add(MerkabaOverlapShell.CanonicalChart(pair.Key,
                        pair.Value, context));
            }
            Assert.That(raw.Count, Is.EqualTo(2),
                "the fixture really straddles the chart boundary");
            Assert.That(canonical, Is.EquivalentTo(new[] { 2 }));
        }

        [Test]
        public void ChartFallsBackToTheRawAxisWhenTheSheetAxisIsUnusable()
        {
            int3 main = new(0);
            var context = new Dictionary<int3, KernelState>
            {
                [main] = Surface(main, new float3(0.45f, 0f, 0.893f), 0f),
                [new int3(0, 1, 0)] = Surface(new int3(0, 1, 0),
                    new float3(0.9f, 0f, 0.436f), 0f),
                [new int3(0, -1, 0)] = Surface(new int3(0, -1, 0),
                    new float3(0.9f, 0f, 0.436f), 0f)
            };
            float3 sum = float3.zero;
            foreach (KernelState state in context.Values)
            {
                KernelState.DecodeSurfacePlane(state.Flags, out float3 normal,
                    out _);
                sum += normal;
            }
            Assert.That(MerkabaOverlapShell.DominantAxis(sum), Is.EqualTo(0),
                "the sheet sum points along X, where |N_m.x| < 0.5");
            Assert.That(MerkabaOverlapShell.CanonicalChart(main, context[main],
                context), Is.EqualTo(2));
            Assert.That(MerkabaOverlapShell.TryResolvePatch(main, context,
                out _), Is.True);
        }

        [TestCase(3, TestName = "ChartFoldInsideOneTile")]
        [TestCase(7, TestName = "ChartFoldAcrossTileEdge")]
        public void ChartFoldClosesWithOneNonSelfIntersectingTransition(int y0)
        {
            var context = new Dictionary<int3, KernelState>();
            MerkabaMembraneFixtures.AddFold(context, 0, y0, 0);
            int3 lower = new(0, y0, 0);
            int3 upper = new(0, y0 + 1, 0);
            Assert.That(MerkabaOverlapShell.CanonicalChart(lower, context[lower],
                context), Is.EqualTo(0));
            Assert.That(MerkabaOverlapShell.CanonicalChart(upper, context[upper],
                context), Is.EqualTo(2));

            var cache = new MerkabaOverlapShell.SolveCache();
            var patchKeys = new HashSet<MerkabaOverlapShell.NodeKey>();
            foreach (int3 main in new[] { lower, upper })
            {
                Assert.That(MerkabaOverlapShell.TryResolvePatch(main, context,
                    cache, out MerkabaOverlapShell.Patch patch), Is.True);
                for (int corner = 0; corner < 4; corner++)
                    patchKeys.Add(patch.GetCorner(corner).Key);
            }

            int transitions = 0;
            MerkabaOverlapShell.Patch transition = default;
            foreach (int3 main in context.Keys)
            for (int axis = 0; axis < 3; axis++)
            {
                if (!MerkabaOverlapShell.TryBuildTransition(main, axis, context,
                        cache, out MerkabaOverlapShell.Patch built))
                    continue;
                transitions++;
                transition = built;
                Assert.That(main, Is.EqualTo(lower));
                Assert.That(axis, Is.EqualTo(1));
            }
            Assert.That(transitions, Is.EqualTo(1));

            // Closed seam: the quad joins the facing edge nodes of both
            // patches and adds no node of its own.
            var corners = new float3[4];
            for (int corner = 0; corner < 4; corner++)
            {
                Assert.That(patchKeys.Contains(transition.GetCorner(corner).Key),
                    Is.True);
                corners[corner] = transition.GetCorner(corner).GridPosition;
                Assert.That(math.distance(corners[corner],
                    transition.GetCorner((corner + 1) & 3).GridPosition),
                    Is.LessThanOrEqualTo(MerkabaOverlapShell.MembraneMaxEdge));
            }
            float3 first = math.cross(corners[1] - corners[0],
                corners[2] - corners[0]);
            float3 second = math.cross(corners[2] - corners[0],
                corners[3] - corners[0]);
            Assert.That(math.dot(first, second), Is.GreaterThan(0f),
                "both triangles face the same way");
            // All four nodes lie in the plane y = y0 + 1/2.
            float2 Planar(float3 value) => value.xz;
            Assert.That(SegmentsCross(Planar(corners[0]), Planar(corners[1]),
                Planar(corners[2]), Planar(corners[3])), Is.False);
            Assert.That(SegmentsCross(Planar(corners[1]), Planar(corners[2]),
                Planar(corners[3]), Planar(corners[0])), Is.False);
        }

        [Test]
        public void ReadoutHaloWindowGivesTheGlobalMembraneOfTheTile()
        {
            // The GPU solves tile [0, 8)^3 from the cells of -5..12 (plan
            // section 3). Every patch and transition it owns must equal the
            // one solved from the unbounded context.
            Dictionary<int3, KernelState> global =
                MerkabaMembraneFixtures.TileCorpus();
            Dictionary<int3, KernelState> window = global.Where(pair =>
                    math.all(pair.Key >= -5) && math.all(pair.Key <= 12))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var globalCache = new MerkabaOverlapShell.SolveCache();
            var windowCache = new MerkabaOverlapShell.SolveCache();
            int patches = 0;
            int absent = 0;
            int transitions = 0;
            foreach (int3 main in global.Keys.OrderBy(value => value.x)
                         .ThenBy(value => value.y).ThenBy(value => value.z))
            {
                if (!math.all(main >= 0) || !math.all(main < 8) ||
                    !global[main].IsOccupied ||
                    !global[main].HasMeasuredSurfacePlane)
                    continue;
                bool solved = MerkabaOverlapShell.TryResolvePatch(main, global,
                    globalCache, out MerkabaOverlapShell.Patch expected);
                Assert.That(MerkabaOverlapShell.TryResolvePatch(main, window,
                        windowCache, out MerkabaOverlapShell.Patch actual),
                    Is.EqualTo(solved), $"patch {main}");
                if (solved)
                {
                    Assert.That(actual.Equals(expected), Is.True, $"{main}");
                    patches++;
                }
                else absent++;
                for (int axis = 0; axis < 3; axis++)
                {
                    bool built = MerkabaOverlapShell.TryBuildTransition(main,
                        axis, global, globalCache,
                        out MerkabaOverlapShell.Patch expectedTransition);
                    Assert.That(MerkabaOverlapShell.TryBuildTransition(main,
                            axis, window, windowCache,
                            out MerkabaOverlapShell.Patch actualTransition),
                        Is.EqualTo(built), $"transition {main} {axis}");
                    if (!built) continue;
                    Assert.That(actualTransition.Equals(expectedTransition),
                        Is.True, $"transition {main} {axis}");
                    transitions++;
                }
            }
            Assert.That(patches, Is.GreaterThan(100));
            Assert.That(absent, Is.GreaterThan(0), "the corpus holds invalid spans");
            Assert.That(transitions, Is.GreaterThanOrEqualTo(1));
        }

        // 045be20 regression: where every column of a node line holds exactly
        // one measured use of MAIN's sheet in MAIN's layer and nothing else
        // within two layers, the 045be20 corner was the column-order mean of
        // the four line heights. The line node is the same number.
        [TestCase(false, TestName = "LineNodesEqual045be20OnAFlatWall")]
        [TestCase(true, TestName = "LineNodesEqual045be20InARoomCorner")]
        public void LineNodesEqualThe045be20CornersWhereAllMainsAgree(
            bool roomCorner)
        {
            var context = new Dictionary<int3, KernelState>();
            var sheet = new Dictionary<int3, int>();
            void AddWall(int id, int3 normalAxis, IEnumerable<int3> cells)
            {
                foreach (int3 cell in cells)
                    if (MerkabaMembraneFixtures.Put(context, cell,
                            (float3)normalAxis, 0.003f))
                        sheet[cell] = id;
            }
            IEnumerable<int> Range(int from, int to) =>
                Enumerable.Range(from, to - from + 1);
            AddWall(0, new int3(1, 0, 0), Range(0, 7).SelectMany(y =>
                Range(0, 7).Select(z => new int3(0, y, z))));
            if (roomCorner)
            {
                AddWall(1, new int3(0, 1, 0), Range(1, 7).SelectMany(x =>
                    Range(0, 7).Select(z => new int3(x, 0, z))));
                AddWall(2, new int3(0, 0, 1), Range(1, 7).SelectMany(x =>
                    Range(1, 7).Select(y => new int3(x, y, 0))));
            }

            int compared = 0;
            foreach (int3 main in context.Keys)
            {
                Assert.That(MerkabaOverlapShell.TryResolvePatch(main, context,
                    out MerkabaOverlapShell.Patch patch), Is.True, $"{main}");
                int chart = MerkabaOverlapShell.CanonicalChart(main,
                    context[main], context);
                MerkabaOverlapShell.TangentAxes(chart, out int tangent0,
                    out int tangent1);
                for (int corner = 0; corner < 4; corner++)
                {
                    MerkabaOverlapShell.Corner value = patch.GetCorner(corner);
                    int3 line = value.LineAddress;
                    float sum = 0f;
                    bool agreed = true;
                    for (int column = 0; column < 4 && agreed; column++)
                    for (int layer = main[chart] - 2; layer <= main[chart] + 2;
                         layer++)
                    {
                        int3 cell = default;
                        cell[tangent0] = MerkabaConstants.FloorDiv(line[tangent0],
                            2) + (column >> 1);
                        cell[tangent1] = MerkabaConstants.FloorDiv(line[tangent1],
                            2) + (column & 1);
                        cell[chart] = layer;
                        bool present = sheet.TryGetValue(cell, out int id);
                        if (layer == main[chart])
                        {
                            agreed &= present && id == sheet[main];
                            if (agreed) sum += LineHeight(context, cell, chart,
                                line);
                        }
                        else agreed &= !present;
                    }
                    if (!agreed) continue;
                    compared++;
                    Assert.That(value.GridPosition[chart],
                        Is.EqualTo(sum / 4f).Within(1e-6f), $"{main} {line}");
                }
            }
            Assert.That(compared, Is.GreaterThan(100));
        }

        private static float LineHeight(
            IReadOnlyDictionary<int3, KernelState> context, int3 cell,
            int chart, int3 lineAddress)
        {
            KernelState.DecodeSurfacePlane(context[cell].Flags,
                out float3 normal, out float offset);
            float constant = math.dot((float3)cell *
                MerkabaConstants.LatticeStep, normal) + offset;
            float3 point = (float3)lineAddress *
                MerkabaOverlapShell.MembraneHalfPitch;
            point[chart] = 0f;
            return (constant - math.dot(point, normal)) / normal[chart];
        }

        private static bool SegmentsCross(float2 p, float2 q, float2 r,
            float2 s)
        {
            static float Cross(float2 u, float2 v) => u.x * v.y - u.y * v.x;
            return Cross(q - p, r - p) * Cross(q - p, s - p) < 0f &&
                Cross(s - r, p - r) * Cross(s - r, q - r) < 0f;
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
