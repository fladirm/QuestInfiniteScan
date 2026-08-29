using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaGeometryTests
    {
        private const uint IsolatedMask = 0x11111111u;

        [Test]
        public void IsolatedOccupiedKernel_EmitsEightOctahedronFacesAndNoTips()
        {
            HashSet<int3> occupied = Set(new int3(0));
            uint mask = MerkabaCanonicalGeometry.ActivePrimitiveMask(new int3(0),
                occupied.Contains);

            Assert.That(mask, Is.EqualTo(IsolatedMask));
            Assert.That(math.countbits(mask), Is.EqualTo(8));
            int[] active = MerkabaCanonicalGeometry.VisiblePrimitives(new int3(0),
                occupied.Contains).ToArray();
            Assert.That(active, Has.Length.EqualTo(8));
            Assert.That(active.All(id => !MerkabaCanonicalGeometry.IsTipPrimitive(id)),
                Is.True);
            Assert.That(active, Is.EqualTo(Enumerable.Range(0, 8)
                .Select(direction => direction * 4)));
        }

        [Test]
        public void EveryBodyDiagonalPair_ReplacesConnectingBaseWithThreeTipSides()
        {
            for (int direction = 0;
                 direction < MerkabaCanonicalGeometry.DirectionCount; direction++)
            {
                int3 offset = MerkabaCanonicalGeometry.Directions[direction].Offset;
                HashSet<int3> occupied = Set(new int3(0), offset);
                uint originMask = MerkabaCanonicalGeometry.ActivePrimitiveMask(
                    new int3(0), occupied.Contains);
                int first = MerkabaCanonicalGeometry.BasePrimitiveId(direction);

                Assert.That(originMask & (1u << first), Is.Zero,
                    $"direction {offset}: connecting octahedron face survived");
                Assert.That(originMask & (0xEu << first), Is.EqualTo(0xEu << first),
                    $"direction {offset}: three tip sides were not emitted");
                Assert.That(math.countbits(originMask), Is.EqualTo(10));

                int reverseDirection = direction ^ 7;
                uint neighbourMask = MerkabaCanonicalGeometry.ActivePrimitiveMask(
                    offset, occupied.Contains);
                int reverseFirst = MerkabaCanonicalGeometry.BasePrimitiveId(reverseDirection);
                Assert.That(neighbourMask & (1u << reverseFirst), Is.Zero);
                Assert.That(neighbourMask & (0xEu << reverseFirst),
                    Is.EqualTo(0xEu << reverseFirst));
                Assert.That(math.countbits(neighbourMask), Is.EqualTo(10));
            }
        }

        [Test]
        public void RemovingBodyDiagonalNeighbour_RemovesTipAndRestoresBase()
        {
            int3 center = new(-7, 11, -31);
            for (int direction = 0;
                 direction < MerkabaCanonicalGeometry.DirectionCount; direction++)
            {
                int3 neighbour = center +
                    MerkabaCanonicalGeometry.Directions[direction].Offset;
                HashSet<int3> occupied = Set(center, neighbour);
                uint withNeighbour = MerkabaCanonicalGeometry.ActivePrimitiveMask(center,
                    occupied.Contains);
                occupied.Remove(neighbour);
                uint withoutNeighbour = MerkabaCanonicalGeometry.ActivePrimitiveMask(center,
                    occupied.Contains);
                int first = MerkabaCanonicalGeometry.BasePrimitiveId(direction);

                Assert.That(withNeighbour & (0xFu << first),
                    Is.EqualTo(0xEu << first));
                Assert.That(withoutNeighbour & (0xFu << first),
                    Is.EqualTo(1u << first));
                Assert.That(withoutNeighbour, Is.EqualTo(IsolatedMask));
            }
        }

        [Test]
        public void AxisAndFaceDiagonalNeighbours_DoNotActivateTips()
        {
            var offsets = new List<int3>();
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                int3 offset = new(x, y, z);
                int nonzero = (x == 0 ? 0 : 1) + (y == 0 ? 0 : 1) +
                              (z == 0 ? 0 : 1);
                if (nonzero is 1 or 2) offsets.Add(offset);
            }

            foreach (int3 offset in offsets)
            {
                HashSet<int3> occupied = Set(new int3(0), offset);
                Assert.That(MerkabaCanonicalGeometry.ActivePrimitiveMask(new int3(0),
                    occupied.Contains), Is.EqualTo(IsolatedMask), offset.ToString());
                Assert.That(MerkabaCanonicalGeometry.ActivePrimitiveMask(offset,
                    occupied.Contains), Is.EqualTo(IsolatedMask), offset.ToString());
            }
        }

        [Test]
        public void TipApex_IsExactlyBodyDiagonalNeighbourCentre()
        {
            for (int direction = 0;
                 direction < MerkabaCanonicalGeometry.DirectionCount; direction++)
            {
                MerkabaCanonicalGeometry.DirectionRule rule =
                    MerkabaCanonicalGeometry.Directions[direction];
                float3 apex = MerkabaCanonicalGeometry.Vertices[rule.ApexVertex].Position;
                Assert.That(math.distance(apex,
                    MerkabaConstants.WorldCenter(rule.Offset)), Is.LessThan(1e-8f));

                int baseId = MerkabaCanonicalGeometry.BasePrimitiveId(direction);
                for (int corner = 0; corner < 3; corner++)
                {
                    MerkabaCanonicalGeometry.PrimitiveVertex(baseId, corner,
                        out float3 vertex, out _);
                    Assert.That(math.dot((float3)rule.Offset, vertex),
                        Is.EqualTo(MerkabaConstants.HalfSupport).Within(1e-8f));
                }
            }
        }

        [Test]
        public void FixedPrimitiveAlphabet_HasEightFacesAndTwentyFourTipSides()
        {
            Assert.That(MerkabaCanonicalGeometry.OctahedronVertices.Length, Is.EqualTo(6));
            Assert.That(MerkabaCanonicalGeometry.Directions.Length, Is.EqualTo(8));
            Assert.That(MerkabaCanonicalGeometry.Vertices.Length, Is.EqualTo(14));
            Assert.That(MerkabaCanonicalGeometry.Primitives.Length, Is.EqualTo(32));
            Assert.That(MerkabaCanonicalGeometry.Primitives.ToArray().Count(value =>
                value.Kind == MerkabaCanonicalGeometry.PrimitiveKind.OctahedronFace),
                Is.EqualTo(8));
            Assert.That(MerkabaCanonicalGeometry.Primitives.ToArray().Count(value =>
                value.Kind == MerkabaCanonicalGeometry.PrimitiveKind.TipSide),
                Is.EqualTo(24));
        }

        [Test]
        public void PrimitiveWinding_IsDeterministicAndOutward()
        {
            for (int direction = 0;
                 direction < MerkabaCanonicalGeometry.DirectionCount; direction++)
            {
                MerkabaCanonicalGeometry.DirectionRule rule =
                    MerkabaCanonicalGeometry.Directions[direction];
                int first = MerkabaCanonicalGeometry.BasePrimitiveId(direction);
                float3 baseNormal = MerkabaCanonicalGeometry.PrimitiveNormal(first);
                Assert.That(math.dot(baseNormal, rule.Offset), Is.GreaterThan(0.999f));

                for (int side = 1; side <= 3; side++)
                {
                    int primitiveId = first + side;
                    MerkabaCanonicalGeometry.CanonicalPrimitive primitive =
                        MerkabaCanonicalGeometry.Primitives[primitiveId];
                    float3 a = MerkabaCanonicalGeometry.Vertices[primitive.Vertex0].Position;
                    float3 b = MerkabaCanonicalGeometry.Vertices[primitive.Vertex1].Position;
                    float3 c = MerkabaCanonicalGeometry.Vertices[primitive.Vertex2].Position;
                    float3 normal = MerkabaCanonicalGeometry.PrimitiveNormal(primitiveId);
                    Assert.That(math.dot(math.normalize(math.cross(b - a, c - a)),
                        normal), Is.GreaterThan(0.99999f));

                    byte opposite = OppositeBaseVertex(rule, primitive);
                    float3 inside = MerkabaCanonicalGeometry.Vertices[opposite].Position;
                    Assert.That(math.dot(normal, inside - a), Is.LessThan(0f),
                        $"tip side {primitiveId} points into its tetrahedron");
                }
            }
        }

        [Test]
        public void CubePatchImplementation_IsNotAcceptedAsMerkaba()
        {
            for (int primitive = 0;
                 primitive < MerkabaCanonicalGeometry.PrimitiveCount; primitive++)
            {
                float3 normal = MerkabaCanonicalGeometry.PrimitiveNormal(primitive);
                Assert.That(math.abs(normal.x), Is.GreaterThan(0.5f));
                Assert.That(math.abs(normal.y), Is.GreaterThan(0.5f));
                Assert.That(math.abs(normal.z), Is.GreaterThan(0.5f),
                    $"primitive {primitive} regressed to a cube-axis normal");
            }
        }

        [Test]
        public void ChunkBorderAndNegativeTranslations_PreserveDirectRuleExactly()
        {
            HashSet<int3> pattern = Set(new int3(0), new int3(1, 1, 1),
                new int3(-1, 1, -1), new int3(2, 2, 2));
            Dictionary<int3, uint> reference = MasksRelative(pattern, new int3(0));

            foreach (int3 translation in new[]
                     {
                         new int3(31, 31, 31), new int3(32, -1, -33),
                         new int3(-32, -65, 31), new int3(-33, -1, -65)
                     })
            {
                HashSet<int3> translated = pattern.Select(value => value + translation)
                    .ToHashSet();
                Assert.That(MasksRelative(translated, translation),
                    Is.EquivalentTo(reference), translation.ToString());
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
        public void GeneratedHlsl_LiteralCasesMatchAllCpuVertexPositions()
        {
            string hlsl = MerkabaCanonicalGeometry.BuildGeneratedHlsl();
            Assert.That(hlsl, Does.Not.Contain("kMerkabaCanonicalPrimitives"));
            Assert.That(hlsl, Does.Not.Contain("kMerkabaCanonicalVertexUnits"));
            Assert.That(hlsl, Does.Not.Contain("cross("));
            Assert.That(hlsl, Does.Not.Contain("normalize("));

            int positionStart = hlsl.IndexOf(
                "float3 MerkabaCanonicalPrimitivePosition",
                StringComparison.Ordinal);
            int facingStart = hlsl.IndexOf(
                "void MerkabaCanonicalPrimitiveFacing",
                StringComparison.Ordinal);
            Assert.That(positionStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(facingStart, Is.GreaterThan(positionStart));
            string positions = hlsl.Substring(positionStart,
                facingStart - positionStart);
            MatchCollection cases = Regex.Matches(positions,
                @"case (?<id>\d+)u:(?<body>.*?)break;", RegexOptions.Singleline);
            Assert.That(cases, Has.Count.EqualTo(
                MerkabaCanonicalGeometry.PrimitiveCount));

            var seen = new HashSet<int>();
            foreach (Match primitiveCase in cases)
            {
                int primitiveId = int.Parse(primitiveCase.Groups["id"].Value,
                    CultureInfo.InvariantCulture);
                Assert.That(seen.Add(primitiveId), Is.True,
                    $"duplicate generated primitive case {primitiveId}");
                MatchCollection vertices = Regex.Matches(
                    primitiveCase.Groups["body"].Value,
                    @"[abc] = float3\((?<x>-?\d+), (?<y>-?\d+), (?<z>-?\d+)\)");
                Assert.That(vertices, Has.Count.EqualTo(3),
                    $"primitive {primitiveId} literal vertex count");

                var parsed = new float3[3];
                for (int corner = 0; corner < 3; corner++)
                {
                    Match vertex = vertices[corner];
                    parsed[corner] = new float3(
                        int.Parse(vertex.Groups["x"].Value,
                            CultureInfo.InvariantCulture),
                        int.Parse(vertex.Groups["y"].Value,
                            CultureInfo.InvariantCulture),
                        int.Parse(vertex.Groups["z"].Value,
                            CultureInfo.InvariantCulture)) * MerkabaConstants.HalfSupport;
                    MerkabaCanonicalGeometry.PrimitiveVertex(primitiveId, corner,
                        out float3 expectedPosition, out _);
                    Assert.That(math.distance(parsed[corner], expectedPosition),
                        Is.LessThan(1e-8f),
                        $"primitive {primitiveId}, corner {corner}");
                }

            }

            Assert.That(seen, Is.EquivalentTo(Enumerable.Range(0,
                MerkabaCanonicalGeometry.PrimitiveCount)));
        }

        [Test]
        public void GeneratedFacingLiterals_MatchAllCpuCentresAndOrientedNormals()
        {
            string hlsl = MerkabaCanonicalGeometry.BuildGeneratedHlsl();
            int facingStart = hlsl.IndexOf(
                "void MerkabaCanonicalPrimitiveFacing",
                StringComparison.Ordinal);
            Assert.That(facingStart, Is.GreaterThanOrEqualTo(0));
            string facing = hlsl.Substring(facingStart);
            MatchCollection cases = Regex.Matches(facing,
                @"case (?<id>\d+)u:(?<body>.*?)break;", RegexOptions.Singleline);
            Assert.That(cases, Has.Count.EqualTo(
                MerkabaCanonicalGeometry.PrimitiveCount));

            foreach (Match primitiveCase in cases)
            {
                int primitiveId = int.Parse(primitiveCase.Groups["id"].Value,
                    CultureInfo.InvariantCulture);
                string body = primitiveCase.Groups["body"].Value;
                Match center = Regex.Match(body,
                    @"primitiveCenterOffset = float3\((?<x>-?\d+), (?<y>-?\d+), (?<z>-?\d+)\)");
                Match normal = Regex.Match(body,
                    @"orientedNormal = float3\((?<x>-?\d+), (?<y>-?\d+), (?<z>-?\d+)\)");
                Assert.That(center.Success, Is.True, $"primitive {primitiveId} center");
                Assert.That(normal.Success, Is.True, $"primitive {primitiveId} normal");
                float3 parsedCenter = ParseInt3(center) *
                    (MerkabaConstants.HalfSupport / 3f);
                float3 parsedNormal = ParseInt3(normal);
                Assert.That(math.distance(parsedCenter,
                    MerkabaCanonicalGeometry.PrimitiveCentroid(primitiveId)),
                    Is.LessThan(1e-8f), $"primitive {primitiveId} center");
                Assert.That(parsedNormal, Is.EqualTo(
                    MerkabaCanonicalGeometry.PrimitiveFacingNormal(primitiveId)),
                    $"primitive {primitiveId} normal");
            }
        }

        [Test]
        public void CheapGridSpaceFacing_MatchesFormerWorldTriangleOracle()
        {
            Matrix4x4[] transforms =
            {
                Matrix4x4.TRS(new Vector3(1.2f, -0.7f, 3.1f),
                    Quaternion.Euler(17f, 83f, -11f),
                    new Vector3(1f, 1f, 1f)),
                Matrix4x4.TRS(new Vector3(-2.4f, 1.3f, 0.8f),
                    Quaternion.Euler(-31f, 19f, 47f),
                    new Vector3(0.7f, 1.4f, 1.1f)),
                Matrix4x4.TRS(new Vector3(0.2f, 2.1f, -1.8f),
                    Quaternion.Euler(9f, -58f, 23f),
                    new Vector3(-1.2f, 0.8f, 1.5f))
            };
            int3[] kernels = { new(0), new(-257, 33, -9), new(256, -31, 7) };
            Vector3[] gridEyes =
            {
                new(1.37f, -0.44f, 2.61f),
                new(-3.73f, 1.29f, -0.82f)
            };

            foreach (Matrix4x4 gridToWorld in transforms)
            foreach (int3 kernel in kernels)
            foreach (Vector3 gridEye in gridEyes)
            for (int primitive = 0;
                 primitive < MerkabaCanonicalGeometry.PrimitiveCount; primitive++)
            {
                float3 kernelCenter = (float3)kernel *
                    MerkabaConstants.LatticeStep;
                MerkabaCanonicalGeometry.PrimitiveVertex(primitive, 0,
                    out float3 localA, out _);
                MerkabaCanonicalGeometry.PrimitiveVertex(primitive, 1,
                    out float3 localB, out _);
                MerkabaCanonicalGeometry.PrimitiveVertex(primitive, 2,
                    out float3 localC, out _);
                Vector3 worldA = gridToWorld.MultiplyPoint3x4(
                    (Vector3)(kernelCenter + localA));
                Vector3 worldB = gridToWorld.MultiplyPoint3x4(
                    (Vector3)(kernelCenter + localB));
                Vector3 worldC = gridToWorld.MultiplyPoint3x4(
                    (Vector3)(kernelCenter + localC));
                Vector3 worldEye = gridToWorld.MultiplyPoint3x4(gridEye);
                Vector3 worldNormal = Vector3.Cross(worldB - worldA,
                    worldC - worldA);
                Vector3 worldCenter = (worldA + worldB + worldC) / 3f;
                bool former = Vector3.Dot(worldNormal,
                    worldEye - worldCenter) > 0f;

                float3 center = kernelCenter +
                    MerkabaCanonicalGeometry.PrimitiveCentroid(primitive);
                float3 oriented =
                    MerkabaCanonicalGeometry.PrimitiveFacingNormal(primitive);
                float windingSign = gridToWorld.determinant < 0f ? -1f : 1f;
                bool current = math.dot(oriented,
                    (float3)gridEye - center) * windingSign > 0f;
                Assert.That(current, Is.EqualTo(former),
                    $"primitive={primitive}, kernel={kernel}");
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

        private static byte OppositeBaseVertex(
            MerkabaCanonicalGeometry.DirectionRule rule,
            MerkabaCanonicalGeometry.CanonicalPrimitive side)
        {
            foreach (byte candidate in new[]
                     {
                         rule.FaceVertex0, rule.FaceVertex1, rule.FaceVertex2
                     })
                if (candidate != side.Vertex1 && candidate != side.Vertex2)
                    return candidate;
            throw new InvalidOperationException("Tip side does not use a base edge.");
        }

        private static float3 ParseInt3(Match value) => new(
            int.Parse(value.Groups["x"].Value, CultureInfo.InvariantCulture),
            int.Parse(value.Groups["y"].Value, CultureInfo.InvariantCulture),
            int.Parse(value.Groups["z"].Value, CultureInfo.InvariantCulture));

        private static Dictionary<int3, uint> MasksRelative(HashSet<int3> occupied,
            int3 translation)
        {
            return occupied.OrderBy(value => value.x).ThenBy(value => value.y)
                .ThenBy(value => value.z).ToDictionary(value => value - translation,
                    value => MerkabaCanonicalGeometry.ActivePrimitiveMask(value,
                        occupied.Contains));
        }

        private static HashSet<int3> Set(params int3[] values) => values.ToHashSet();
    }
}
