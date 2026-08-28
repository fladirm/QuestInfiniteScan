using System;
using System.Collections.Generic;
using System.Linq;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaGeometryTests
    {
        private readonly struct PatchKey : IEquatable<PatchKey>
        {
            public readonly int Axis;
            public readonly int Plane;
            public readonly int Tangent0Twice;
            public readonly int Tangent1Twice;
            public readonly int Sign;

            public PatchKey(int axis, int plane, int tangent0Twice,
                int tangent1Twice, int sign)
            {
                Axis = axis;
                Plane = plane;
                Tangent0Twice = tangent0Twice;
                Tangent1Twice = tangent1Twice;
                Sign = sign;
            }

            public bool Equals(PatchKey other) => Axis == other.Axis &&
                Plane == other.Plane && Tangent0Twice == other.Tangent0Twice &&
                Tangent1Twice == other.Tangent1Twice && Sign == other.Sign;
            public override bool Equals(object obj) => obj is PatchKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Axis, Plane,
                Tangent0Twice, Tangent1Twice, Sign);
            public override string ToString() =>
                $"a={Axis},p={Plane},t0x2={Tangent0Twice},t1x2={Tangent1Twice},s={Sign}";

            public PatchKey RelativeTo(int3 offset)
            {
                TangentAxes(Axis, out int tangent0, out int tangent1);
                return new PatchKey(Axis, Plane - offset[Axis],
                    Tangent0Twice - 2 * offset[tangent0],
                    Tangent1Twice - 2 * offset[tangent1], Sign);
            }
        }

        private static IEnumerable<TestCaseData> RequiredPatterns()
        {
            yield return new TestCaseData("single kernel", Set(new int3(0))).SetName("SingleKernel");
            yield return new TestCaseData("two neighbours", Set(new int3(0), new int3(1, 0, 0)))
                .SetName("TwoNeighbours");
            yield return new TestCaseData("solid block", Solid(-1, 1)).SetName("SolidBlock");
            yield return new TestCaseData("X wall", Wall(0, 0, -2, 2)).SetName("XWall");
            yield return new TestCaseData("Y wall", Wall(1, 0, -2, 2)).SetName("YWall");
            yield return new TestCaseData("Z wall", Wall(2, 0, -2, 2)).SetName("ZWall");
            yield return new TestCaseData("90 degree XY corner",
                Union(Wall(0, 0, -2, 2), Wall(1, 0, -2, 2))).SetName("XYCorner");
            yield return new TestCaseData("XYZ corner",
                Union(Wall(0, 0, -2, 2), Wall(1, 0, -2, 2), Wall(2, 0, -2, 2)))
                .SetName("XYZCorner");
            yield return new TestCaseData("diagonal surface", Diagonal()).SetName("DiagonalSurface");
            yield return new TestCaseData("single layer sheet", Wall(2, 3, -3, 3))
                .SetName("SingleLayerSheet");
            yield return new TestCaseData("two close parallel sheets",
                Union(Wall(2, 0, -2, 2), Wall(2, 3, -2, 2)))
                .SetName("TwoCloseParallelSheets");
            yield return new TestCaseData("cylinder-like occupancy", Cylinder())
                .SetName("CylinderLikeOccupancy");
            yield return new TestCaseData("sphere-like occupancy", Sphere())
                .SetName("SphereLikeOccupancy");
        }

        [TestCaseSource(nameof(RequiredPatterns))]
        public void LocalOwnership_EqualsIndependentSupportUnionBoundary(string name,
            HashSet<int3> occupied)
        {
            Assert.That(occupied, Is.Not.Empty, name);
            HashSet<PatchKey> expected = OracleBoundary(occupied);
            Dictionary<PatchKey, int3> actual = ProductionBoundary(occupied);

            PatchKey[] missing = expected.Except(actual.Keys).Take(8).ToArray();
            PatchKey[] extra = actual.Keys.Except(expected).Take(8).ToArray();
            Assert.That(missing, Is.Empty,
                $"{name}: missing exterior patches: {string.Join("; ", missing.Select(x => x.ToString()))}");
            Assert.That(extra, Is.Empty,
                $"{name}: emitted interior patches: {string.Join("; ", extra.Select(x => x.ToString()))}");
            Assert.That(actual.Count, Is.EqualTo(expected.Count),
                $"{name}: an exterior primitive was duplicated");
        }

        [Test]
        public void CanonicalMerkaba_DecompositionHasRequiredPiecesAndCubeVolume()
        {
            Assert.That(MerkabaTopology.TetraA, Is.EquivalentTo(new[]
            {
                new int3(1, 1, 1), new int3(1, -1, -1),
                new int3(-1, 1, -1), new int3(-1, -1, 1)
            }));
            Assert.That(MerkabaTopology.TetraB, Is.EquivalentTo(new[]
            {
                new int3(-1, -1, -1), new int3(-1, 1, 1),
                new int3(1, -1, 1), new int3(1, 1, -1)
            }));
            Assert.That(MerkabaTopology.CentralOctahedron, Has.Length.EqualTo(6));
            Assert.That(MerkabaTopology.TipTetrahedra, Has.Length.EqualTo(8));
            Assert.That(MerkabaTopology.EdgeWedgeTetrahedra, Has.Length.EqualTo(12));

            double volume = 0;
            int3 origin = new(0);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                volume += TetraVolume(origin, new int3(x, 0, 0),
                    new int3(0, y, 0), new int3(0, 0, z));
            foreach (int3[] tetra in MerkabaTopology.TipTetrahedra)
                volume += TetraVolume(tetra[0], tetra[1], tetra[2], tetra[3]);
            foreach (int3[] tetra in MerkabaTopology.EdgeWedgeTetrahedra)
                volume += TetraVolume(tetra[0], tetra[1], tetra[2], tetra[3]);

            Assert.That(volume, Is.EqualTo(8d).Within(1e-9),
                "central octahedron + 8 tips + 12 edge wedges must exactly fill side-two cube");
        }

        [Test]
        public void PatchBasis_IsOutwardWoundAndWithinFiveCentimetreSupport()
        {
            for (int patch = 0; patch < MerkabaConstants.BoundaryPatchCount; patch++)
            {
                var vertices = new float3[MerkabaConstants.VerticesPerPatch];
                float3 expectedNormal = default;
                for (int i = 0; i < vertices.Length; i++)
                {
                    MerkabaTopology.PatchVertex(patch, i, out vertices[i], out float3 normal);
                    expectedNormal = normal;
                    Assert.That(math.cmax(math.abs(vertices[i])),
                        Is.LessThanOrEqualTo(MerkabaConstants.HalfSupport + 1e-7f));
                }
                for (int triangle = 0; triangle < 2; triangle++)
                {
                    int first = triangle * 3;
                    float3 normal = math.normalize(math.cross(
                        vertices[first + 1] - vertices[first],
                        vertices[first + 2] - vertices[first]));
                    Assert.That(math.dot(normal, expectedNormal), Is.GreaterThan(0.999f));
                }
            }
        }

        [Test]
        public void ChunkBorderAndNegativeTranslations_AreTopologyInvariant()
        {
            HashSet<int3> pattern = Union(Diagonal(),
                Set(new int3(0), new int3(1, 0, 0), new int3(1, 1, 0)));
            HashSet<PatchKey> reference = ProductionBoundary(pattern).Keys.ToHashSet();

            foreach (int3 offset in new[]
                     {
                         new int3(31, 0, 0), new int3(32, 31, -1),
                         new int3(-33, -1, -65)
                     })
            {
                HashSet<int3> translated = pattern.Select(value => value + offset).ToHashSet();
                HashSet<PatchKey> normalized = ProductionBoundary(translated).Keys
                    .Select(key => key.RelativeTo(offset)).ToHashSet();
                Assert.That(normalized, Is.EquivalentTo(reference),
                    $"translation {offset} changed ownership at a chunk/negative boundary");
            }
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

        private static Dictionary<PatchKey, int3> ProductionBoundary(HashSet<int3> occupied)
        {
            var result = new Dictionary<PatchKey, int3>();
            foreach (int3 center in occupied)
            {
                uint mask = MerkabaTopology.BoundaryMask(center, occupied.Contains);
                foreach (int patch in MerkabaTopology.ActivePatches(mask))
                {
                    PatchKey key = Key(center, patch);
                    Assert.That(result.TryAdd(key, center), Is.True,
                        $"duplicate physical patch {key}: {result.GetValueOrDefault(key)} and {center}");
                }
            }
            return result;
        }

        private static HashSet<PatchKey> OracleBoundary(HashSet<int3> occupied)
        {
            var result = new HashSet<PatchKey>();
            foreach (int3 center in occupied)
            for (int patch = 0; patch < MerkabaConstants.BoundaryPatchCount; patch++)
            {
                MerkabaTopology.DecodePatch(patch, out int axis, out int sign,
                    out int tangentSign0, out int tangentSign1);
                TangentAxes(axis, out int tangent0, out int tangent1);
                double3 patchCenter = center;
                patchCenter[axis] += sign;
                patchCenter[tangent0] += tangentSign0 * 0.5;
                patchCenter[tangent1] += tangentSign1 * 0.5;
                double3 normal = default;
                normal[axis] = sign;
                // The topology contract deliberately has exactly 26 input bits. Supports
                // separated by an empty lattice centre are distinct close sheets even if
                // their 5 cm bounds merely touch, so the independent point-containment
                // oracle is restricted to this kernel's 3x3x3 neighbourhood.
                bool inside = ContainsLocal(occupied, center, patchCenter - normal * 0.25);
                bool outside = ContainsLocal(occupied, center, patchCenter + normal * 0.25);
                if (inside && !outside) result.Add(Key(center, patch));
            }
            return result;
        }

        private static bool ContainsLocal(HashSet<int3> occupied, int3 source, double3 point)
        {
            foreach (int3 center in occupied)
                if (math.cmax(math.abs(center - source)) <= 1 &&
                    math.cmax(math.abs(point - (double3)center)) < 1.0 - 1e-9)
                    return true;
            return false;
        }

        private static PatchKey Key(int3 center, int patch)
        {
            MerkabaTopology.DecodePatch(patch, out int axis, out int sign,
                out int tangentSign0, out int tangentSign1);
            TangentAxes(axis, out int tangent0, out int tangent1);
            return new PatchKey(axis, center[axis] + sign,
                2 * center[tangent0] + tangentSign0,
                2 * center[tangent1] + tangentSign1, sign);
        }

        private static void TangentAxes(int axis, out int tangent0, out int tangent1)
        {
            switch (axis)
            {
                case 0: tangent0 = 1; tangent1 = 2; break;
                case 1: tangent0 = 2; tangent1 = 0; break;
                default: tangent0 = 0; tangent1 = 1; break;
            }
        }

        private static double TetraVolume(int3 a, int3 b, int3 c, int3 d)
        {
            double3 ab = b - a;
            double3 ac = c - a;
            double3 ad = d - a;
            return math.abs(math.dot(ab, math.cross(ac, ad))) / 6d;
        }

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

        private static HashSet<int3> Wall(int axis, int coordinate, int minimum, int maximum)
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
    }
}
