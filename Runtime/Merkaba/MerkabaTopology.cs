using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>Frozen canonical Merkaba decomposition and exact local boundary ownership.</summary>
    public static class MerkabaTopology
    {
        private static readonly int[] ForwardVertexOrder = { 0, 1, 2, 0, 2, 3 };
        private static readonly int[] ReverseVertexOrder = { 0, 2, 1, 0, 3, 2 };

        public static readonly int3[] CubeCorners =
        {
            new(-1, -1, -1), new(-1, -1,  1), new(-1,  1, -1), new(-1,  1,  1),
            new( 1, -1, -1), new( 1, -1,  1), new( 1,  1, -1), new( 1,  1,  1)
        };

        public static readonly int3[] TetraA =
        {
            new( 1,  1,  1), new( 1, -1, -1),
            new(-1,  1, -1), new(-1, -1,  1)
        };

        public static readonly int3[] TetraB =
        {
            new(-1, -1, -1), new(-1,  1,  1),
            new( 1, -1,  1), new( 1,  1, -1)
        };

        public static readonly int3[] CentralOctahedron =
        {
            new( 1, 0, 0), new(-1, 0, 0), new(0,  1, 0),
            new(0, -1, 0), new(0, 0,  1), new(0, 0, -1)
        };

        public static readonly int3[][] TipTetrahedra = BuildTips();
        public static readonly int3[][] EdgeWedgeTetrahedra = BuildEdgeWedges();

        /// <summary>
        /// Returns the 24 active half-step face-quadrants for one occupied support.
        /// Each bit expands to two fixed triangles. No table is indexed by the 26-bit input.
        /// </summary>
        public static uint BoundaryMask(int3 center, Func<int3, bool> occupied)
        {
            if (occupied == null) throw new ArgumentNullException(nameof(occupied));
            if (!occupied(center)) return 0;

            uint mask = 0;
            for (int patch = 0; patch < MerkabaConstants.BoundaryPatchCount; patch++)
            {
                DecodePatch(patch, out int axis, out int sign,
                    out int tangentSign0, out int tangentSign1);
                if (OwnsPatch(center, axis, sign, tangentSign0, tangentSign1, occupied))
                    mask |= 1u << patch;
            }
            return mask;
        }

        public static bool OwnsPatch(int3 center, int axis, int sign,
            int tangentSign0, int tangentSign1, Func<int3, bool> occupied)
        {
            if (axis is < 0 or > 2 || (sign != -1 && sign != 1) ||
                (tangentSign0 != -1 && tangentSign0 != 1) ||
                (tangentSign1 != -1 && tangentSign1 != 1))
                throw new ArgumentOutOfRangeException();
            if (!occupied(center)) return false;

            Axes(axis, out int3 normal, out int3 tangent0, out int3 tangent1);
            int3 outward = normal * sign;
            int3 tangentOffset0 = tangent0 * tangentSign0;
            int3 tangentOffset1 = tangent1 * tangentSign1;

            // Any half-support centre on the outward side contains this whole patch,
            // making it interior to the exact union of overlapping supports.
            for (int b = 0; b <= 1; b++)
            for (int c = 0; c <= 1; c++)
            {
                int3 candidate = center + outward +
                    (b == 0 ? default : tangentOffset0) +
                    (c == 0 ? default : tangentOffset1);
                if (occupied(candidate)) return false;
            }

            // Coplanar supports share the same physical patch. The least signed integer
            // centre owns it, so the patch is emitted exactly once without cleanup.
            int3[] coplanar =
            {
                center + tangentOffset0,
                center + tangentOffset1,
                center + tangentOffset0 + tangentOffset1
            };
            foreach (int3 candidate in coplanar)
            {
                if (occupied(candidate) &&
                    MerkabaConstants.LexicographicallyLess(candidate, center))
                    return false;
            }
            return true;
        }

        public static void DecodePatch(int patch, out int axis, out int sign,
            out int tangentSign0, out int tangentSign1)
        {
            if ((uint)patch >= MerkabaConstants.BoundaryPatchCount)
                throw new ArgumentOutOfRangeException(nameof(patch));
            int face = patch >> 2;
            int quadrant = patch & 3;
            axis = face >> 1;
            sign = (face & 1) == 0 ? -1 : 1;
            tangentSign0 = (quadrant & 1) == 0 ? -1 : 1;
            tangentSign1 = (quadrant & 2) == 0 ? -1 : 1;
        }

        public static int EncodePatch(int axis, int sign, int tangentSign0,
            int tangentSign1)
        {
            int face = axis * 2 + (sign > 0 ? 1 : 0);
            int quadrant = (tangentSign0 > 0 ? 1 : 0) |
                           (tangentSign1 > 0 ? 2 : 0);
            return face * 4 + quadrant;
        }

        /// <summary>Returns one of the six fixed vertices (two triangles) of a patch.</summary>
        public static void PatchVertex(int patch, int vertex, out float3 position,
            out float3 normal)
        {
            if ((uint)vertex >= MerkabaConstants.VerticesPerPatch)
                throw new ArgumentOutOfRangeException(nameof(vertex));
            DecodePatch(patch, out int axis, out int sign, out int u, out int v);
            Axes(axis, out int3 nI, out int3 bI, out int3 cI);
            float3 n = nI;
            float3 b = bI;
            float3 c = cI;
            float a = MerkabaConstants.HalfSupport;
            float3 p00 = n * (sign * a);
            float3 p10 = p00 + b * (u * a);
            float3 p11 = p10 + c * (v * a);
            float3 p01 = p00 + c * (v * a);

            bool forward = sign * u * v > 0;
            int corner = forward ? ForwardVertexOrder[vertex] : ReverseVertexOrder[vertex];
            position = corner switch
            {
                0 => p00,
                1 => p10,
                2 => p11,
                _ => p01
            };
            normal = n * sign;
        }

        public static IEnumerable<int> ActivePatches(uint mask)
        {
            for (int patch = 0; patch < MerkabaConstants.BoundaryPatchCount; patch++)
                if ((mask & (1u << patch)) != 0)
                    yield return patch;
        }

        private static void Axes(int normalAxis, out int3 normal,
            out int3 tangent0, out int3 tangent1)
        {
            switch (normalAxis)
            {
                case 0:
                    normal = new int3(1, 0, 0);
                    tangent0 = new int3(0, 1, 0);
                    tangent1 = new int3(0, 0, 1);
                    break;
                case 1:
                    normal = new int3(0, 1, 0);
                    tangent0 = new int3(0, 0, 1);
                    tangent1 = new int3(1, 0, 0);
                    break;
                default:
                    normal = new int3(0, 0, 1);
                    tangent0 = new int3(1, 0, 0);
                    tangent1 = new int3(0, 1, 0);
                    break;
            }
        }

        private static int3[][] BuildTips()
        {
            var tips = new List<int3[]>(8);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                tips.Add(new[]
                {
                    new int3(x, y, z), new int3(x, 0, 0),
                    new int3(0, y, 0), new int3(0, 0, z)
                });
            return tips.ToArray();
        }

        private static int3[][] BuildEdgeWedges()
        {
            var wedges = new List<int3[]>(12);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                wedges.Add(new[]
                {
                    new int3(x, y, -1), new int3(x, y, 1),
                    new int3(x, 0, 0), new int3(0, y, 0)
                });
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                wedges.Add(new[]
                {
                    new int3(x, -1, z), new int3(x, 1, z),
                    new int3(x, 0, 0), new int3(0, 0, z)
                });
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                wedges.Add(new[]
                {
                    new int3(-1, y, z), new int3(1, y, z),
                    new int3(0, y, 0), new int3(0, 0, z)
                });
            return wedges.ToArray();
        }
    }
}
