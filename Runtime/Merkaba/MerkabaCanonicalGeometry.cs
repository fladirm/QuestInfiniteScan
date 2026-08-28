using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// The one direct Merkaba geometry authority. Each occupied lattice site owns one
    /// central octahedron. For each body-diagonal direction it publishes either the
    /// exposed octahedron face or the three fixed sides of the tip whose apex is the
    /// occupied neighbour's centre. Intentional primitive overlap is never clipped.
    /// </summary>
    public static class MerkabaCanonicalGeometry
    {
        public const int DirectionCount = 8;
        public const int PrimitivesPerDirection = 4;
        public const int PrimitiveCount = DirectionCount * PrimitivesPerDirection;
        public const int VerticesPerPrimitive = 3;
        public const int MinimumActivePrimitiveCount = 8;
        public const int MaximumActivePrimitiveCount = 24;

        public enum PrimitiveKind : byte
        {
            OctahedronFace = 0,
            TipSide = 1
        }

        public readonly struct CanonicalVertex
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

            public int3 Coordinate => new(X, Y, Z);
            public float3 Position => (float3)Coordinate * MerkabaConstants.HalfSupport;
        }

        public readonly struct DirectionRule
        {
            public readonly int3 Offset;
            public readonly byte FaceVertex0;
            public readonly byte FaceVertex1;
            public readonly byte FaceVertex2;
            public readonly byte ApexVertex;

            public DirectionRule(int3 offset, byte faceVertex0, byte faceVertex1,
                byte faceVertex2, byte apexVertex)
            {
                Offset = offset;
                FaceVertex0 = faceVertex0;
                FaceVertex1 = faceVertex1;
                FaceVertex2 = faceVertex2;
                ApexVertex = apexVertex;
            }
        }

        public readonly struct CanonicalPrimitive
        {
            public readonly byte Vertex0;
            public readonly byte Vertex1;
            public readonly byte Vertex2;
            public readonly byte Direction;
            public readonly PrimitiveKind Kind;

            public CanonicalPrimitive(byte vertex0, byte vertex1, byte vertex2,
                byte direction, PrimitiveKind kind)
            {
                Vertex0 = vertex0;
                Vertex1 = vertex1;
                Vertex2 = vertex2;
                Direction = direction;
                Kind = kind;
            }

            public byte VertexIndex(int corner) => corner switch
            {
                0 => Vertex0,
                1 => Vertex1,
                2 => Vertex2,
                _ => throw new ArgumentOutOfRangeException(nameof(corner))
            };
        }

        // Six central-octahedron axis vertices followed by the eight tip apexes.
        // One coordinate unit is exactly a = 0.025 m.
        private static readonly CanonicalVertex[] VerticesValue =
        {
            new(-1,  0,  0), new( 1,  0,  0),
            new( 0, -1,  0), new( 0,  1,  0),
            new( 0,  0, -1), new( 0,  0,  1),
            new(-1, -1, -1), new( 1, -1, -1),
            new(-1,  1, -1), new( 1,  1, -1),
            new(-1, -1,  1), new( 1, -1,  1),
            new(-1,  1,  1), new( 1,  1,  1)
        };

        // Face winding points out of the central octahedron in Offset direction.
        // ApexVertex is c + a*Offset, exactly the body-diagonal neighbour centre.
        private static readonly DirectionRule[] DirectionsValue =
        {
            new(new int3(-1, -1, -1), 0, 4, 2,  6),
            new(new int3( 1, -1, -1), 1, 2, 4,  7),
            new(new int3(-1,  1, -1), 0, 3, 4,  8),
            new(new int3( 1,  1, -1), 1, 4, 3,  9),
            new(new int3(-1, -1,  1), 0, 2, 5, 10),
            new(new int3( 1, -1,  1), 1, 5, 2, 11),
            new(new int3(-1,  1,  1), 0, 5, 3, 12),
            new(new int3( 1,  1,  1), 1, 3, 5, 13)
        };

        private static readonly CanonicalPrimitive[] PrimitivesValue = BuildPrimitives();

        public static ReadOnlySpan<CanonicalVertex> Vertices => VerticesValue;
        public static ReadOnlySpan<CanonicalVertex> OctahedronVertices =>
            VerticesValue.AsSpan(0, 6);
        public static ReadOnlySpan<DirectionRule> Directions => DirectionsValue;
        public static ReadOnlySpan<CanonicalPrimitive> Primitives => PrimitivesValue;

        public static uint ActivePrimitiveMask(int3 center,
            Func<int3, bool> occupied)
        {
            if (occupied == null) throw new ArgumentNullException(nameof(occupied));
            if (!occupied(center)) return 0u;

            uint mask = 0u;
            for (int direction = 0; direction < DirectionCount; direction++)
            {
                int firstPrimitive = direction * PrimitivesPerDirection;
                if (occupied(center + DirectionsValue[direction].Offset))
                    mask |= 0xEu << firstPrimitive;
                else
                    mask |= 1u << firstPrimitive;
            }
            return mask;
        }

        public static IEnumerable<int> VisiblePrimitives(int3 center,
            Func<int3, bool> occupied)
        {
            uint mask = ActivePrimitiveMask(center, occupied);
            for (int primitive = 0; primitive < PrimitiveCount; primitive++)
                if ((mask & (1u << primitive)) != 0u)
                    yield return primitive;
        }

        public static bool IsTipPrimitive(int primitiveId)
        {
            if ((uint)primitiveId >= PrimitiveCount)
                throw new ArgumentOutOfRangeException(nameof(primitiveId));
            return PrimitivesValue[primitiveId].Kind == PrimitiveKind.TipSide;
        }

        public static int BasePrimitiveId(int direction)
        {
            if ((uint)direction >= DirectionCount)
                throw new ArgumentOutOfRangeException(nameof(direction));
            return direction * PrimitivesPerDirection;
        }

        public static void PrimitiveVertex(int primitiveId, int corner,
            out float3 position, out float3 normal)
        {
            if ((uint)primitiveId >= PrimitiveCount)
                throw new ArgumentOutOfRangeException(nameof(primitiveId));
            if ((uint)corner >= VerticesPerPrimitive)
                throw new ArgumentOutOfRangeException(nameof(corner));
            CanonicalPrimitive primitive = PrimitivesValue[primitiveId];
            position = VerticesValue[primitive.VertexIndex(corner)].Position;
            normal = PrimitiveNormal(primitiveId);
        }

        public static float3 PrimitiveNormal(int primitiveId)
        {
            if ((uint)primitiveId >= PrimitiveCount)
                throw new ArgumentOutOfRangeException(nameof(primitiveId));
            CanonicalPrimitive primitive = PrimitivesValue[primitiveId];
            float3 a = VerticesValue[primitive.Vertex0].Position;
            float3 b = VerticesValue[primitive.Vertex1].Position;
            float3 c = VerticesValue[primitive.Vertex2].Position;
            return math.normalize(math.cross(b - a, c - a));
        }

#if UNITY_EDITOR
        internal static string BuildGeneratedHlsl()
        {
            var text = new StringBuilder(12000);
            text.Append("// GENERATED from MerkabaCanonicalGeometry.cs. DO NOT EDIT.\n")
                .Append("#ifndef GENESIS_MERKABA_CANONICAL_GEOMETRY_INCLUDED\n")
                .Append("#define GENESIS_MERKABA_CANONICAL_GEOMETRY_INCLUDED\n\n")
                .Append("#define MERKABA_DIRECTION_COUNT 8\n")
                .Append("#define MERKABA_CANONICAL_VERTEX_COUNT ").Append(VerticesValue.Length).Append("\n")
                .Append("#define MERKABA_CANONICAL_PRIMITIVE_COUNT ").Append(PrimitiveCount).Append("\n")
                .Append("#define MERKABA_PRIMITIVES_PER_DIRECTION 4\n")
                .Append("#define MERKABA_VERTICES_PER_PRIMITIVE 3\n")
                .Append("#define MERKABA_CANONICAL_UNIT 0.025\n\n")
                .Append("static const int3 kMerkabaBodyDiagonalOffsets[MERKABA_DIRECTION_COUNT] =\n{\n");
            for (int index = 0; index < DirectionsValue.Length; index++)
            {
                int3 value = DirectionsValue[index].Offset;
                text.Append("    int3(").Append(value.x).Append(", ")
                    .Append(value.y).Append(", ").Append(value.z).Append(')')
                    .Append(index + 1 == DirectionsValue.Length ? "\n" : ",\n");
            }
            text.Append("};\n\nstatic const int3 kMerkabaCanonicalVertexUnits[MERKABA_CANONICAL_VERTEX_COUNT] =\n{\n");
            for (int index = 0; index < VerticesValue.Length; index++)
            {
                CanonicalVertex value = VerticesValue[index];
                text.Append("    int3(").Append(value.X).Append(", ")
                    .Append(value.Y).Append(", ").Append(value.Z).Append(')')
                    .Append(index + 1 == VerticesValue.Length ? "\n" : ",\n");
            }
            text.Append("};\n\n// xyz = vertex indices, w = (direction << 1) | tip-side flag.\n")
                .Append("static const uint4 kMerkabaCanonicalPrimitives[MERKABA_CANONICAL_PRIMITIVE_COUNT] =\n{\n");
            for (int index = 0; index < PrimitivesValue.Length; index++)
            {
                CanonicalPrimitive value = PrimitivesValue[index];
                uint metadata = ((uint)value.Direction << 1) |
                    (value.Kind == PrimitiveKind.TipSide ? 1u : 0u);
                text.Append("    uint4(").Append(value.Vertex0).Append("u, ")
                    .Append(value.Vertex1).Append("u, ").Append(value.Vertex2)
                    .Append("u, ").Append(metadata.ToString(CultureInfo.InvariantCulture))
                    .Append("u)").Append(index + 1 == PrimitivesValue.Length ? "\n" : ",\n");
            }
            text.Append("};\n\n")
                .Append("float3 MerkabaCanonicalVertexPosition(uint vertexIndex)\n{\n")
                .Append("    return (float3)kMerkabaCanonicalVertexUnits[vertexIndex] * MERKABA_CANONICAL_UNIT;\n}\n\n")
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

        private static CanonicalPrimitive[] BuildPrimitives()
        {
            var result = new CanonicalPrimitive[PrimitiveCount];
            for (byte direction = 0; direction < DirectionCount; direction++)
            {
                DirectionRule rule = DirectionsValue[direction];
                int first = direction * PrimitivesPerDirection;
                result[first] = new CanonicalPrimitive(rule.FaceVertex0,
                    rule.FaceVertex1, rule.FaceVertex2, direction,
                    PrimitiveKind.OctahedronFace);
                result[first + 1] = OutwardTipSide(rule.ApexVertex,
                    rule.FaceVertex0, rule.FaceVertex1, rule.FaceVertex2, direction);
                result[first + 2] = OutwardTipSide(rule.ApexVertex,
                    rule.FaceVertex1, rule.FaceVertex2, rule.FaceVertex0, direction);
                result[first + 3] = OutwardTipSide(rule.ApexVertex,
                    rule.FaceVertex2, rule.FaceVertex0, rule.FaceVertex1, direction);
            }
            Validate(result);
            return result;
        }

        private static CanonicalPrimitive OutwardTipSide(byte apex, byte edge0,
            byte edge1, byte oppositeBaseVertex, byte direction)
        {
            float3 a = VerticesValue[apex].Coordinate;
            float3 b = VerticesValue[edge0].Coordinate;
            float3 c = VerticesValue[edge1].Coordinate;
            float3 opposite = VerticesValue[oppositeBaseVertex].Coordinate;
            if (math.dot(math.cross(b - a, c - a), opposite - a) > 0f)
                (edge0, edge1) = (edge1, edge0);
            return new CanonicalPrimitive(apex, edge0, edge1, direction,
                PrimitiveKind.TipSide);
        }

        private static void Validate(CanonicalPrimitive[] primitives)
        {
            if (VerticesValue.Length != 14 || DirectionsValue.Length != DirectionCount ||
                primitives.Length != PrimitiveCount)
                throw new InvalidOperationException("Direct Merkaba table length mismatch.");

            for (int direction = 0; direction < DirectionCount; direction++)
            {
                DirectionRule rule = DirectionsValue[direction];
                if (math.any(math.abs(rule.Offset) != 1) ||
                    math.any(VerticesValue[rule.ApexVertex].Coordinate != rule.Offset))
                    throw new InvalidOperationException($"Direction {direction} has an invalid apex.");

                CanonicalPrimitive face = primitives[BasePrimitiveId(direction)];
                float3 a = VerticesValue[face.Vertex0].Coordinate;
                float3 b = VerticesValue[face.Vertex1].Coordinate;
                float3 c = VerticesValue[face.Vertex2].Coordinate;
                if (math.dot(math.cross(b - a, c - a), rule.Offset) <= 0f)
                    throw new InvalidOperationException($"Direction {direction} face is not outward wound.");
            }

            for (int primitive = 0; primitive < primitives.Length; primitive++)
            {
                CanonicalPrimitive value = primitives[primitive];
                float3 a = VerticesValue[value.Vertex0].Coordinate;
                float3 b = VerticesValue[value.Vertex1].Coordinate;
                float3 c = VerticesValue[value.Vertex2].Coordinate;
                if (math.lengthsq(math.cross(b - a, c - a)) <= 1e-12f)
                    throw new InvalidOperationException($"Primitive {primitive} is degenerate.");
            }
        }
    }
}
