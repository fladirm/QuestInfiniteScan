using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// The single exact geometry authority for an occupied 5 cm Stella-octangula support.
    /// Coordinates are stored in half-units: one unit is 12.5 mm. The 24 exterior
    /// Merkaba faces are split at their edge midpoints into 96 fixed micro-triangles.
    /// Each micro-triangle carries only the small 26-neighbour suppression predicate
    /// derived by the independent analytic-union oracle.
    /// </summary>
    public static class MerkabaCanonicalGeometry
    {
        public const int BaseFaceCount = 24;
        public const int MicroTrianglesPerFace = 4;
        public const int PrimitiveCount = BaseFaceCount * MicroTrianglesPerFace;
        public const int VerticesPerPrimitive = 3;
        public const float HalfUnit = MerkabaConstants.HalfSupport * 0.5f;

        public readonly struct CanonicalVertex : IEquatable<CanonicalVertex>
        {
            public readonly sbyte X;
            public readonly sbyte Y;
            public readonly sbyte Z;

            public CanonicalVertex(sbyte x, sbyte y, sbyte z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public float3 Position => new float3(X, Y, Z) * HalfUnit;

            public bool Equals(CanonicalVertex other) =>
                X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) =>
                obj is CanonicalVertex other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(X, Y, Z);
            public override string ToString() => $"({X},{Y},{Z})";
        }

        public readonly struct CanonicalFace
        {
            public readonly byte Vertex0;
            public readonly byte Vertex1;
            public readonly byte Vertex2;

            public CanonicalFace(byte vertex0, byte vertex1, byte vertex2)
            {
                Vertex0 = vertex0;
                Vertex1 = vertex1;
                Vertex2 = vertex2;
            }
        }

        public readonly struct CanonicalPrimitive
        {
            public readonly byte Vertex0;
            public readonly byte Vertex1;
            public readonly byte Vertex2;
            public readonly byte SourceFace;
            public readonly uint SuppressionMask;

            public CanonicalPrimitive(byte vertex0, byte vertex1, byte vertex2,
                byte sourceFace, uint suppressionMask)
            {
                Vertex0 = vertex0;
                Vertex1 = vertex1;
                Vertex2 = vertex2;
                SourceFace = sourceFace;
                SuppressionMask = suppressionMask;
            }

            public byte VertexIndex(int corner) => corner switch
            {
                0 => Vertex0,
                1 => Vertex1,
                2 => Vertex2,
                _ => throw new ArgumentOutOfRangeException(nameof(corner))
            };
        }

        // Corner order uses x as the low bit, then y, then z. Axis vertices follow.
        private static readonly CanonicalVertex[] SupportVerticesValue =
        {
            new(-2, -2, -2), new( 2, -2, -2),
            new(-2,  2, -2), new( 2,  2, -2),
            new(-2, -2,  2), new( 2, -2,  2),
            new(-2,  2,  2), new( 2,  2,  2),
            new(-2,  0,  0), new( 2,  0,  0),
            new( 0, -2,  0), new( 0,  2,  0),
            new( 0,  0, -2), new( 0,  0,  2)
        };

        // Exactly the outward faces of the eight tip tetrahedra. These are the
        // boundary of tetrahedron A union tetrahedron B, not faces of the cube.
        private static readonly CanonicalFace[] BaseFacesValue =
        {
            new(0, 10,  8), new(0, 12, 10), new(0,  8, 12),
            new(1,  9, 10), new(1, 10, 12), new(1, 12,  9),
            new(2,  8, 11), new(2, 11, 12), new(2, 12,  8),
            new(3, 11,  9), new(3, 12, 11), new(3,  9, 12),
            new(4,  8, 10), new(4, 10, 13), new(4, 13,  8),
            new(5, 10,  9), new(5, 13, 10), new(5,  9, 13),
            new(6, 11,  8), new(6, 13, 11), new(6,  8, 13),
            new(7,  9, 11), new(7, 11, 13), new(7, 13,  9)
        };

        // Bit i addresses NeighbourOffsets[i]. A set bit means that an occupied
        // neighbour either covers the outward side of this micro-triangle or is
        // the lexicographically smaller owner of the same coplanar boundary.
        // These predicates were frozen from exact rational plane arrangement.
        private static readonly uint[] SuppressionMasksValue =
        {
            0x0000020Bu, 0x0000060Au, 0x0000120Au, 0x0000161Bu, 0x0000020Bu, 0x0000021Au, 0x0000060Au, 0x0000161Bu,
            0x0000020Bu, 0x0000120Au, 0x0000021Au, 0x0000161Bu, 0x00000806u, 0x00002802u, 0x00000C02u, 0x00002C16u,
            0x00000006u, 0x00000402u, 0x00000012u, 0x00002416u, 0x00000026u, 0x00000032u, 0x00002022u, 0x00002436u,
            0x00004048u, 0x00005008u, 0x0000C008u, 0x0000D058u, 0x000040C8u, 0x0000C088u, 0x00004098u, 0x0000D0D8u,
            0x00004048u, 0x00004018u, 0x00005008u, 0x0000D058u, 0x00010100u, 0x00018000u, 0x00012000u, 0x0001A110u,
            0x00000180u, 0x00000090u, 0x00008080u, 0x0000A190u, 0x00000120u, 0x00002020u, 0x00000030u, 0x0000A130u,
            0x00160200u, 0x00141200u, 0x00140600u, 0x00361600u, 0x00160200u, 0x00140600u, 0x00340200u, 0x00361600u,
            0x00160200u, 0x00340200u, 0x00141200u, 0x00361600u, 0x000C0800u, 0x00040C00u, 0x00042800u, 0x002C2C00u,
            0x000C0000u, 0x00240000u, 0x00040400u, 0x002C2400u, 0x004C0000u, 0x00442000u, 0x00640000u, 0x006C2400u,
            0x00904000u, 0x0010C000u, 0x00105000u, 0x00B0D000u, 0x01904000u, 0x01304000u, 0x0110C000u, 0x01B0D000u,
            0x00904000u, 0x00105000u, 0x00304000u, 0x00B0D000u, 0x02010000u, 0x00012000u, 0x00018000u, 0x0221A000u,
            0x03000000u, 0x01008000u, 0x01200000u, 0x0320A000u, 0x02400000u, 0x00600000u, 0x00402000u, 0x0260A000u
        };

        private static readonly int3[] NeighbourOffsetsValue = BuildNeighbourOffsets();
        private static readonly int3[] TetraAValue =
        {
            new( 1,  1,  1), new( 1, -1, -1),
            new(-1,  1, -1), new(-1, -1,  1)
        };
        private static readonly int3[] TetraBValue =
        {
            new(-1, -1, -1), new(-1,  1,  1),
            new( 1, -1,  1), new( 1,  1, -1)
        };
        private static readonly int3[] CentralOctahedronValue =
        {
            new( 1, 0, 0), new(-1, 0, 0),
            new(0,  1, 0), new(0, -1, 0),
            new(0, 0,  1), new(0, 0, -1)
        };

        private static readonly DerivedGeometry Derived = BuildDerivedGeometry();

        public static ReadOnlySpan<CanonicalVertex> SupportVertices => SupportVerticesValue;
        public static ReadOnlySpan<CanonicalVertex> Vertices => Derived.Vertices;
        public static ReadOnlySpan<CanonicalFace> BaseFaces => BaseFacesValue;
        public static ReadOnlySpan<CanonicalPrimitive> Primitives => Derived.Primitives;
        public static ReadOnlySpan<int3> NeighbourOffsets => NeighbourOffsetsValue;
        public static ReadOnlySpan<int3> TetraA => TetraAValue;
        public static ReadOnlySpan<int3> TetraB => TetraBValue;
        public static ReadOnlySpan<int3> CentralOctahedron => CentralOctahedronValue;

        public static bool IsPrimitiveVisible(int3 center, int primitiveId,
            Func<int3, bool> occupied)
        {
            if (occupied == null) throw new ArgumentNullException(nameof(occupied));
            if ((uint)primitiveId >= PrimitiveCount)
                throw new ArgumentOutOfRangeException(nameof(primitiveId));
            if (!occupied(center)) return false;

            uint mask = Derived.Primitives[primitiveId].SuppressionMask;
            for (int bit = 0; bit < MerkabaConstants.NeighbourCount; bit++)
            {
                if ((mask & (1u << bit)) != 0u &&
                    occupied(center + NeighbourOffsetsValue[bit]))
                    return false;
            }
            return true;
        }

        public static IEnumerable<int> VisiblePrimitives(int3 center,
            Func<int3, bool> occupied)
        {
            if (occupied == null) throw new ArgumentNullException(nameof(occupied));
            if (!occupied(center)) yield break;
            for (int primitive = 0; primitive < PrimitiveCount; primitive++)
                if (IsPrimitiveVisible(center, primitive, occupied))
                    yield return primitive;
        }

        public static void PrimitiveVertex(int primitiveId, int corner,
            out float3 position, out float3 normal)
        {
            if ((uint)primitiveId >= PrimitiveCount)
                throw new ArgumentOutOfRangeException(nameof(primitiveId));
            if ((uint)corner >= VerticesPerPrimitive)
                throw new ArgumentOutOfRangeException(nameof(corner));
            CanonicalPrimitive primitive = Derived.Primitives[primitiveId];
            position = Derived.Vertices[primitive.VertexIndex(corner)].Position;
            normal = PrimitiveNormal(primitiveId);
        }

        public static float3 PrimitiveNormal(int primitiveId)
        {
            if ((uint)primitiveId >= PrimitiveCount)
                throw new ArgumentOutOfRangeException(nameof(primitiveId));
            CanonicalPrimitive primitive = Derived.Primitives[primitiveId];
            float3 a = Derived.Vertices[primitive.Vertex0].Position;
            float3 b = Derived.Vertices[primitive.Vertex1].Position;
            float3 c = Derived.Vertices[primitive.Vertex2].Position;
            return math.normalize(math.cross(b - a, c - a));
        }

#if UNITY_EDITOR
        internal static string BuildGeneratedHlsl()
        {
            var text = new StringBuilder(24000);
            text.Append("// GENERATED from MerkabaCanonicalGeometry.cs. DO NOT EDIT.\n")
                .Append("#ifndef GENESIS_MERKABA_CANONICAL_GEOMETRY_INCLUDED\n")
                .Append("#define GENESIS_MERKABA_CANONICAL_GEOMETRY_INCLUDED\n\n")
                .Append("#define MERKABA_CANONICAL_VERTEX_COUNT ").Append(Derived.Vertices.Length).Append("\n")
                .Append("#define MERKABA_CANONICAL_PRIMITIVE_COUNT ").Append(PrimitiveCount).Append("\n")
                .Append("#define MERKABA_VERTICES_PER_PRIMITIVE 3\n")
                .Append("#define MERKABA_CANONICAL_HALF_UNIT 0.0125\n\n")
                .Append("static const int3 kMerkabaNeighbourOffsets[26] =\n{\n");
            for (int index = 0; index < NeighbourOffsetsValue.Length; index++)
            {
                int3 value = NeighbourOffsetsValue[index];
                text.Append("    int3(").Append(value.x).Append(", ")
                    .Append(value.y).Append(", ").Append(value.z).Append(')')
                    .Append(index + 1 == NeighbourOffsetsValue.Length ? "\n" : ",\n");
            }
            text.Append("};\n\nstatic const int3 kMerkabaCanonicalVertexHalfUnits[MERKABA_CANONICAL_VERTEX_COUNT] =\n{\n");
            for (int index = 0; index < Derived.Vertices.Length; index++)
            {
                CanonicalVertex value = Derived.Vertices[index];
                text.Append("    int3(").Append(value.X).Append(", ")
                    .Append(value.Y).Append(", ").Append(value.Z).Append(')')
                    .Append(index + 1 == Derived.Vertices.Length ? "\n" : ",\n");
            }
            text.Append("};\n\n// xyz = vertex indices, w = neighbour suppression mask.\n")
                .Append("static const uint4 kMerkabaCanonicalPrimitives[MERKABA_CANONICAL_PRIMITIVE_COUNT] =\n{\n");
            for (int index = 0; index < Derived.Primitives.Length; index++)
            {
                CanonicalPrimitive value = Derived.Primitives[index];
                text.Append("    uint4(").Append(value.Vertex0).Append("u, ")
                    .Append(value.Vertex1).Append("u, ").Append(value.Vertex2)
                    .Append("u, 0x").Append(value.SuppressionMask.ToString("X8", CultureInfo.InvariantCulture))
                    .Append("u)").Append(index + 1 == Derived.Primitives.Length ? "\n" : ",\n");
            }
            text.Append("};\n\n")
                .Append("float3 MerkabaCanonicalVertexPosition(uint vertexIndex)\n{\n")
                .Append("    return (float3)kMerkabaCanonicalVertexHalfUnits[vertexIndex] * MERKABA_CANONICAL_HALF_UNIT;\n}\n\n")
                .Append("void MerkabaCanonicalPrimitiveVertex(uint primitiveId, uint corner, out float3 position, out float3 normal)\n{\n")
                .Append("    uint4 primitive = kMerkabaCanonicalPrimitives[primitiveId];\n")
                .Append("    uint vertexIndex = corner == 0u ? primitive.x : (corner == 1u ? primitive.y : primitive.z);\n")
                .Append("    float3 a = MerkabaCanonicalVertexPosition(primitive.x);\n")
                .Append("    float3 b = MerkabaCanonicalVertexPosition(primitive.y);\n")
                .Append("    float3 c = MerkabaCanonicalVertexPosition(primitive.z);\n")
                .Append("    position = MerkabaCanonicalVertexPosition(vertexIndex);\n")
                .Append("    normal = normalize(cross(b - a, c - a));\n}\n\n")
                .Append("#endif\n");
            return text.ToString();
        }
#endif

        private static DerivedGeometry BuildDerivedGeometry()
        {
            if (BaseFacesValue.Length != BaseFaceCount ||
                SuppressionMasksValue.Length != PrimitiveCount)
                throw new InvalidOperationException("Canonical Merkaba table length mismatch.");

            var vertices = new List<CanonicalVertex>(SupportVerticesValue);
            var primitives = new CanonicalPrimitive[PrimitiveCount];
            for (int faceIndex = 0; faceIndex < BaseFacesValue.Length; faceIndex++)
            {
                CanonicalFace face = BaseFacesValue[faceIndex];
                byte midpoint01 = IndexOrAdd(vertices, Midpoint(
                    vertices[face.Vertex0], vertices[face.Vertex1]));
                byte midpoint12 = IndexOrAdd(vertices, Midpoint(
                    vertices[face.Vertex1], vertices[face.Vertex2]));
                byte midpoint20 = IndexOrAdd(vertices, Midpoint(
                    vertices[face.Vertex2], vertices[face.Vertex0]));
                int first = faceIndex * MicroTrianglesPerFace;
                primitives[first + 0] = new CanonicalPrimitive(face.Vertex0,
                    midpoint01, midpoint20, (byte)faceIndex, SuppressionMasksValue[first + 0]);
                primitives[first + 1] = new CanonicalPrimitive(midpoint01,
                    face.Vertex1, midpoint12, (byte)faceIndex, SuppressionMasksValue[first + 1]);
                primitives[first + 2] = new CanonicalPrimitive(midpoint20,
                    midpoint12, face.Vertex2, (byte)faceIndex, SuppressionMasksValue[first + 2]);
                primitives[first + 3] = new CanonicalPrimitive(midpoint01,
                    midpoint12, midpoint20, (byte)faceIndex, SuppressionMasksValue[first + 3]);
            }

            var result = new DerivedGeometry(vertices.ToArray(), primitives);
            Validate(result);
            return result;
        }

        private static void Validate(DerivedGeometry geometry)
        {
            uint invalidMask = ~((1u << MerkabaConstants.NeighbourCount) - 1u);
            for (int primitiveId = 0; primitiveId < geometry.Primitives.Length; primitiveId++)
            {
                CanonicalPrimitive primitive = geometry.Primitives[primitiveId];
                if ((primitive.SuppressionMask & invalidMask) != 0u)
                    throw new InvalidOperationException($"Primitive {primitiveId} references a non-neighbour bit.");
                float3 a = geometry.Vertices[primitive.Vertex0].Position;
                float3 b = geometry.Vertices[primitive.Vertex1].Position;
                float3 c = geometry.Vertices[primitive.Vertex2].Position;
                float3 cross = math.cross(b - a, c - a);
                if (math.lengthsq(cross) <= 1e-12f || math.dot(cross, a + b + c) <= 0f)
                    throw new InvalidOperationException($"Primitive {primitiveId} is degenerate or not outward wound.");
            }
        }

        private static CanonicalVertex Midpoint(CanonicalVertex left,
            CanonicalVertex right)
        {
            int x = left.X + right.X;
            int y = left.Y + right.Y;
            int z = left.Z + right.Z;
            if (((x | y | z) & 1) != 0)
                throw new InvalidOperationException("Canonical midpoint is not an exact half-unit.");
            return new CanonicalVertex((sbyte)(x / 2), (sbyte)(y / 2),
                (sbyte)(z / 2));
        }

        private static byte IndexOrAdd(List<CanonicalVertex> vertices,
            CanonicalVertex value)
        {
            int index = vertices.IndexOf(value);
            if (index < 0)
            {
                index = vertices.Count;
                vertices.Add(value);
            }
            if (index > byte.MaxValue)
                throw new InvalidOperationException("Canonical vertex index overflow.");
            return (byte)index;
        }

        private static int3[] BuildNeighbourOffsets()
        {
            var result = new int3[MerkabaConstants.NeighbourCount];
            int index = 0;
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0 && z == 0) continue;
                result[index++] = new int3(x, y, z);
            }
            return result;
        }

        private sealed class DerivedGeometry
        {
            public readonly CanonicalVertex[] Vertices;
            public readonly CanonicalPrimitive[] Primitives;

            public DerivedGeometry(CanonicalVertex[] vertices,
                CanonicalPrimitive[] primitives)
            {
                Vertices = vertices;
                Primitives = primitives;
            }
        }
    }
}
