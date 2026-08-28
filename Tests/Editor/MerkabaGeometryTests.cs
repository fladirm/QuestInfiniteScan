using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaGeometryTests
    {
        private static IEnumerable<TestCaseData> RequiredPatterns()
        {
            yield return Case("single occupied site", Set(new int3(0)), "SingleKernel");
            yield return Case("two adjacent +X", Set(new int3(0), new int3(1, 0, 0)), "AdjacentPositiveX");
            yield return Case("two adjacent -X", Set(new int3(0), new int3(-1, 0, 0)), "AdjacentNegativeX");
            yield return Case("two adjacent +Y", Set(new int3(0), new int3(0, 1, 0)), "AdjacentPositiveY");
            yield return Case("two adjacent -Y", Set(new int3(0), new int3(0, -1, 0)), "AdjacentNegativeY");
            yield return Case("two adjacent +Z", Set(new int3(0), new int3(0, 0, 1)), "AdjacentPositiveZ");
            yield return Case("two adjacent -Z", Set(new int3(0), new int3(0, 0, -1)), "AdjacentNegativeZ");
            yield return Case("face diagonal", Set(new int3(0), new int3(1, 1, 0)), "FaceDiagonal");
            yield return Case("body diagonal", Set(new int3(0), new int3(1, 1, 1)), "BodyDiagonal");
            yield return Case("3x3x3 solid block", Solid(-1, 1), "SolidBlock");
            yield return Case("large X wall", Wall(0, 0, -3, 3), "XWall");
            yield return Case("large Y wall", Wall(1, 0, -3, 3), "YWall");
            yield return Case("large Z wall", Wall(2, 0, -3, 3), "ZWall");
            yield return Case("90 degree XY corner",
                Union(Wall(0, 0, -2, 2), Wall(1, 0, -2, 2)), "XYCorner");
            yield return Case("XYZ corner",
                Union(Wall(0, 0, -2, 2), Wall(1, 0, -2, 2), Wall(2, 0, -2, 2)),
                "XYZCorner");
            yield return Case("diagonal staircase", Diagonal(), "DiagonalStaircase");
            yield return Case("one-layer thin sheet", Wall(2, 3, -3, 3), "ThinSheet");
            yield return Case("two close parallel sheets",
                Union(Wall(2, 0, -2, 2), Wall(2, 3, -2, 2)), "ParallelSheets");
            yield return Case("cylinder-like occupancy", Cylinder(), "CylinderLike");
            yield return Case("sphere-like occupancy", Sphere(), "SphereLike");
        }

        [TestCaseSource(nameof(RequiredPatterns))]
        public void FrozenPrimitivePredicates_EqualIndependentAnalyticUnion(
            string name, HashSet<int3> occupied)
        {
            Assert.That(occupied, Is.Not.Empty, name);
            HashSet<MerkabaAnalyticUnionOracle.TriangleKey> expected =
                MerkabaAnalyticUnionOracle.Boundary(occupied);
            Dictionary<MerkabaAnalyticUnionOracle.TriangleKey, PrimitiveOwner> actual =
                ProductionBoundary(occupied);

            MerkabaAnalyticUnionOracle.TriangleKey[] missing = expected
                .Except(actual.Keys).Take(8).ToArray();
            MerkabaAnalyticUnionOracle.TriangleKey[] extra = actual.Keys
                .Except(expected).Take(8).ToArray();
            Assert.That(missing, Is.Empty,
                $"{name}: cracks/missing analytic exterior: {string.Join("; ", missing)}");
            Assert.That(extra, Is.Empty,
                $"{name}: interior/non-Merkaba triangles emitted: {string.Join("; ", extra)}");
            Assert.That(actual.Count, Is.EqualTo(expected.Count),
                $"{name}: duplicate exterior ownership");
        }

        [Test]
        public void CanonicalOccupiedSupport_IsStellaOctangulaNotCube()
        {
            Assert.That(MerkabaCanonicalGeometry.TetraA.ToArray(), Is.EquivalentTo(new[]
            {
                new int3(1, 1, 1), new int3(1, -1, -1),
                new int3(-1, 1, -1), new int3(-1, -1, 1)
            }));
            Assert.That(MerkabaCanonicalGeometry.TetraB.ToArray(), Is.EquivalentTo(new[]
            {
                new int3(-1, -1, -1), new int3(-1, 1, 1),
                new int3(1, -1, 1), new int3(1, 1, -1)
            }));
            Assert.That(MerkabaCanonicalGeometry.CentralOctahedron.Length, Is.EqualTo(6));
            Assert.That(MerkabaCanonicalGeometry.BaseFaces.Length, Is.EqualTo(24));
            Assert.That(MerkabaCanonicalGeometry.Primitives.Length, Is.EqualTo(96));

            double occupiedVolume = CentralOctahedronVolume() + TipVolume();
            Assert.That(occupiedVolume, Is.EqualTo(4d).Within(1e-12));
            Assert.That(occupiedVolume, Is.Not.EqualTo(8d),
                "The 12 complement wedges would turn the occupied support back into a cube.");
        }

        [Test]
        public void CubePatchImplementation_IsNotAcceptedAsMerkaba()
        {
            var normalDirections = new HashSet<int3>();
            for (int primitive = 0;
                 primitive < MerkabaCanonicalGeometry.PrimitiveCount; primitive++)
            {
                float3 normal = MerkabaCanonicalGeometry.PrimitiveNormal(primitive);
                int3 signedDirection = (int3)math.round(normal * math.sqrt(3f));
                normalDirections.Add(signedDirection);
                Assert.That(math.abs(normal.x), Is.GreaterThan(0.5f));
                Assert.That(math.abs(normal.y), Is.GreaterThan(0.5f));
                Assert.That(math.abs(normal.z), Is.GreaterThan(0.5f));
            }
            Assert.That(normalDirections.Count, Is.EqualTo(8));
            Assert.That(normalDirections.Contains(new int3(1, 0, 0)), Is.False);
            Assert.That(normalDirections.Contains(new int3(0, 1, 0)), Is.False);
            Assert.That(normalDirections.Contains(new int3(0, 0, 1)), Is.False);
        }

        [Test]
        public void PrimitiveBasis_IsOutwardWoundInsideFiveCentimetreSupport()
        {
            var occupied = Set(new int3(0));
            for (int primitiveId = 0;
                 primitiveId < MerkabaCanonicalGeometry.PrimitiveCount; primitiveId++)
            {
                MerkabaCanonicalGeometry.CanonicalPrimitive primitive =
                    MerkabaCanonicalGeometry.Primitives[primitiveId];
                Assert.That(primitive.SuppressionMask >> 26, Is.Zero);
                var vertices = new float3[3];
                float3 normal = default;
                for (int corner = 0; corner < 3; corner++)
                {
                    MerkabaCanonicalGeometry.PrimitiveVertex(primitiveId, corner,
                        out vertices[corner], out normal);
                    Assert.That(math.cmax(math.abs(vertices[corner])),
                        Is.LessThanOrEqualTo(MerkabaConstants.HalfSupport + 1e-7f));
                }
                float3 cross = math.normalize(math.cross(vertices[1] - vertices[0],
                    vertices[2] - vertices[0]));
                Assert.That(math.dot(cross, normal), Is.GreaterThan(0.99999f));

                double3 centroid = ((double3)vertices[0] + vertices[1] + vertices[2]) /
                    (3d * MerkabaConstants.HalfSupport);
                Assert.That(MerkabaAnalyticUnionOracle.ContainsUnion(occupied,
                    centroid - (double3)normal * 1e-5), Is.True);
                Assert.That(MerkabaAnalyticUnionOracle.ContainsUnion(occupied,
                    centroid + (double3)normal * 1e-5), Is.False);
            }
        }

        [Test]
        public void ChunkBorderAndNegativeTranslations_AreTopologyInvariant()
        {
            HashSet<int3> pattern = Union(Diagonal(),
                Set(new int3(0), new int3(1, 0, 0), new int3(1, 1, 0)));
            HashSet<MerkabaAnalyticUnionOracle.TriangleKey> reference =
                ProductionBoundary(pattern).Keys.ToHashSet();

            foreach (int3 offset in new[]
                     {
                         new int3(31, 0, 0), new int3(32, 31, -1),
                         new int3(-33, -1, -65)
                     })
            {
                HashSet<int3> translated = pattern.Select(value => value + offset).ToHashSet();
                HashSet<MerkabaAnalyticUnionOracle.TriangleKey> normalized =
                    ProductionBoundary(translated).Keys
                        .Select(key => key.RelativeTo(offset)).ToHashSet();
                Assert.That(normalized, Is.EquivalentTo(reference),
                    $"translation {offset} changed topology/ownership");
                Assert.That(normalized, Is.EquivalentTo(
                    MerkabaAnalyticUnionOracle.Boundary(translated)
                        .Select(key => key.RelativeTo(offset))));
            }
        }

        [Test]
        public void GeneratedHlsl_MatchesCpuGeometryAuthorityByteForByte()
        {
            const string relative =
                "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaCanonicalGeometry.generated.hlsl";
            string path = Path.GetFullPath(relative);
            Assert.That(File.Exists(path), Is.True,
                "Run Quest Infinite Scan/Merkaba/Regenerate Canonical HLSL.");
            Assert.That(File.ReadAllText(path),
                Is.EqualTo(MerkabaCanonicalGeometry.BuildGeneratedHlsl()));
        }

        [Test]
        public void FloorAddressing_IsCorrectAtNegativeChunkBoundaries()
        {
            var cases = new[]
            {
                (global: -65, chunk: -3, local: 31),
                (global: -64, chunk: -2, local: 0),
                (global: -33, chunk: -2, local: 31),
                (global: -32, chunk: -1, local: 0),
                (global: -1, chunk: -1, local: 31),
                (global: 0, chunk: 0, local: 0),
                (global: 31, chunk: 0, local: 31),
                (global: 32, chunk: 1, local: 0)
            };
            foreach (var item in cases)
            {
                Assert.That(MerkabaConstants.FloorDiv(item.global, 32), Is.EqualTo(item.chunk));
                Assert.That(MerkabaConstants.FloorMod(item.global, 32), Is.EqualTo(item.local));
            }
        }

        private static Dictionary<MerkabaAnalyticUnionOracle.TriangleKey, PrimitiveOwner>
            ProductionBoundary(HashSet<int3> occupied)
        {
            var result = new Dictionary<MerkabaAnalyticUnionOracle.TriangleKey, PrimitiveOwner>();
            foreach (int3 center in occupied.OrderBy(value => value.x)
                         .ThenBy(value => value.y).ThenBy(value => value.z))
            foreach (int primitiveId in MerkabaCanonicalGeometry.VisiblePrimitives(
                         center, occupied.Contains))
            {
                var vertices = new double3[3];
                for (int corner = 0; corner < 3; corner++)
                {
                    MerkabaCanonicalGeometry.PrimitiveVertex(primitiveId, corner,
                        out float3 local, out _);
                    vertices[corner] = center + (double3)local /
                        MerkabaConstants.HalfSupport;
                }
                var key = new MerkabaAnalyticUnionOracle.TriangleKey(
                    vertices[0], vertices[1], vertices[2]);
                Assert.That(result.TryAdd(key, new PrimitiveOwner(center, primitiveId)),
                    Is.True, $"duplicate triangle {key}: {result.GetValueOrDefault(key)}");
            }
            return result;
        }

        private static double CentralOctahedronVolume()
        {
            double volume = 0d;
            int3 origin = new(0);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                volume += TetraVolume(origin, new int3(x, 0, 0),
                    new int3(0, y, 0), new int3(0, 0, z));
            return volume;
        }

        private static double TipVolume()
        {
            double volume = 0d;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                volume += TetraVolume(new int3(x, y, z), new int3(x, 0, 0),
                    new int3(0, y, 0), new int3(0, 0, z));
            return volume;
        }

        private static double TetraVolume(int3 a, int3 b, int3 c, int3 d)
        {
            double3 ab = b - a;
            double3 ac = c - a;
            double3 ad = d - a;
            return math.abs(math.dot(ab, math.cross(ac, ad))) / 6d;
        }

        private static TestCaseData Case(string name, HashSet<int3> occupied,
            string testName) => new TestCaseData(name, occupied).SetName(testName);
        private static HashSet<int3> Set(params int3[] values) => values.ToHashSet();

        private static HashSet<int3> Union(params HashSet<int3>[] sets)
        {
            var result = new HashSet<int3>();
            foreach (HashSet<int3> set in sets) result.UnionWith(set);
            return result;
        }

        private static HashSet<int3> Solid(int minimum, int maximum)
        {
            var result = new HashSet<int3>();
            for (int x = minimum; x <= maximum; x++)
            for (int y = minimum; y <= maximum; y++)
            for (int z = minimum; z <= maximum; z++)
                result.Add(new int3(x, y, z));
            return result;
        }

        private static HashSet<int3> Wall(int axis, int coordinate, int minimum,
            int maximum)
        {
            var result = new HashSet<int3>();
            for (int a = minimum; a <= maximum; a++)
            for (int b = minimum; b <= maximum; b++)
            {
                int3 value = default;
                value[axis] = coordinate;
                value[(axis + 1) % 3] = a;
                value[(axis + 2) % 3] = b;
                result.Add(value);
            }
            return result;
        }

        private static HashSet<int3> Diagonal()
        {
            var result = new HashSet<int3>();
            for (int diagonal = -3; diagonal <= 3; diagonal++)
            for (int z = -2; z <= 2; z++)
                result.Add(new int3(diagonal, diagonal, z));
            return result;
        }

        private static HashSet<int3> Cylinder()
        {
            var result = new HashSet<int3>();
            for (int x = -4; x <= 4; x++)
            for (int y = -4; y <= 4; y++)
            for (int z = -2; z <= 2; z++)
            {
                float radius = math.sqrt(x * x + y * y);
                if (radius is >= 2.5f and <= 3.5f) result.Add(new int3(x, y, z));
            }
            return result;
        }

        private static HashSet<int3> Sphere()
        {
            var result = new HashSet<int3>();
            for (int x = -4; x <= 4; x++)
            for (int y = -4; y <= 4; y++)
            for (int z = -4; z <= 4; z++)
            {
                float radius = math.length(new float3(x, y, z));
                if (math.abs(radius - 3f) <= 0.6f) result.Add(new int3(x, y, z));
            }
            return result;
        }

        private readonly struct PrimitiveOwner
        {
            private readonly int3 _center;
            private readonly int _primitiveId;

            public PrimitiveOwner(int3 center, int primitiveId)
            {
                _center = center;
                _primitiveId = primitiveId;
            }

            public override string ToString() => $"{_center}/p{_primitiveId}";
        }
    }
}
