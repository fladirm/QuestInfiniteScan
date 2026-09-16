using System;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Exact topology oracle for the M8 union skin.  The local solid is the
    /// 26-faced convex envelope of the seven-cube cross.  The 80 facelets are
    /// the boundary partition induced by the 26 immediate lattice neighbours.
    /// Runtime work is only a 26-bit neighbour mask and LUT rejection.
    /// </summary>
    internal static class MerkabaSkin
    {
        internal const int VertexCount = 42;
        internal const int FaceletCount = 80;
        internal const int VerticesPerFacelet = 3;
        internal const int IndicesPerFacelet = 3;

        internal readonly struct Facelet
        {
            internal readonly byte A;
            internal readonly byte B;
            internal readonly byte C;
            internal readonly uint OccluderMask;

            internal Facelet(int a, int b, int c, uint occluderMask)
            {
                A = checked((byte)a);
                B = checked((byte)b);
                C = checked((byte)c);
                OccluderMask = occluderMask;
            }
        }

        private static readonly int3[] HalfVerticesValue =
        {
            new int3(-3, -1, -1),
            new int3(-3, -1, 1),
            new int3(-3, 0, 0),
            new int3(-3, 1, -1),
            new int3(-3, 1, 1),
            new int3(-2, -2, 0),
            new int3(-2, 0, -2),
            new int3(-2, 0, 2),
            new int3(-2, 2, 0),
            new int3(-1, -3, -1),
            new int3(-1, -3, 1),
            new int3(-1, -1, -3),
            new int3(-1, -1, 3),
            new int3(-1, 1, -3),
            new int3(-1, 1, 3),
            new int3(-1, 3, -1),
            new int3(-1, 3, 1),
            new int3(0, -3, 0),
            new int3(0, -2, -2),
            new int3(0, -2, 2),
            new int3(0, 0, -3),
            new int3(0, 0, 3),
            new int3(0, 2, -2),
            new int3(0, 2, 2),
            new int3(0, 3, 0),
            new int3(1, -3, -1),
            new int3(1, -3, 1),
            new int3(1, -1, -3),
            new int3(1, -1, 3),
            new int3(1, 1, -3),
            new int3(1, 1, 3),
            new int3(1, 3, -1),
            new int3(1, 3, 1),
            new int3(2, -2, 0),
            new int3(2, 0, -2),
            new int3(2, 0, 2),
            new int3(2, 2, 0),
            new int3(3, -1, -1),
            new int3(3, -1, 1),
            new int3(3, 0, 0),
            new int3(3, 1, -1),
            new int3(3, 1, 1)
        };

        private static readonly int3[] NeighboursValue =
        {
            new int3(-1, -1, -1),
            new int3(0, -1, -1),
            new int3(1, -1, -1),
            new int3(-1, 0, -1),
            new int3(0, 0, -1),
            new int3(1, 0, -1),
            new int3(-1, 1, -1),
            new int3(0, 1, -1),
            new int3(1, 1, -1),
            new int3(-1, -1, 0),
            new int3(0, -1, 0),
            new int3(1, -1, 0),
            new int3(-1, 0, 0),
            new int3(1, 0, 0),
            new int3(-1, 1, 0),
            new int3(0, 1, 0),
            new int3(1, 1, 0),
            new int3(-1, -1, 1),
            new int3(0, -1, 1),
            new int3(1, -1, 1),
            new int3(-1, 0, 1),
            new int3(0, 0, 1),
            new int3(1, 0, 1),
            new int3(-1, 1, 1),
            new int3(0, 1, 1),
            new int3(1, 1, 1)
        };

        private static readonly Facelet[] FaceletsValue =
        {
            new Facelet(1, 2, 0, 0x00125209u),
            new Facelet(1, 4, 2, 0x00925208u),
            new Facelet(2, 3, 0, 0x00105249u),
            new Facelet(4, 3, 2, 0x00905248u),
            new Facelet(37, 39, 38, 0x00492824u),
            new Facelet(37, 40, 39, 0x00412924u),
            new Facelet(39, 41, 38, 0x02492820u),
            new Facelet(40, 41, 39, 0x02412920u),
            new Facelet(9, 17, 10, 0x00060e03u),
            new Facelet(9, 25, 17, 0x00040e07u),
            new Facelet(17, 26, 10, 0x000e0e02u),
            new Facelet(25, 26, 17, 0x000c0e06u),
            new Facelet(16, 24, 15, 0x0181c0c0u),
            new Facelet(16, 32, 24, 0x0381c080u),
            new Facelet(24, 31, 15, 0x0101c1c0u),
            new Facelet(32, 31, 24, 0x0301c180u),
            new Facelet(13, 20, 11, 0x000000fbu),
            new Facelet(13, 29, 20, 0x000001fau),
            new Facelet(20, 27, 11, 0x000000bfu),
            new Facelet(29, 27, 20, 0x000001beu),
            new Facelet(12, 21, 14, 0x01f60000u),
            new Facelet(12, 28, 21, 0x017e0000u),
            new Facelet(21, 30, 14, 0x03f40000u),
            new Facelet(28, 30, 21, 0x037c0000u),
            new Facelet(0, 5, 1, 0x00121609u),
            new Facelet(0, 9, 5, 0x0002160bu),
            new Facelet(1, 5, 10, 0x00161601u),
            new Facelet(5, 9, 10, 0x00061603u),
            new Facelet(3, 8, 15, 0x0080d0c8u),
            new Facelet(4, 8, 3, 0x0090d048u),
            new Facelet(4, 16, 8, 0x0190d040u),
            new Facelet(8, 16, 15, 0x0180d0c0u),
            new Facelet(25, 33, 26, 0x000c2c06u),
            new Facelet(25, 37, 33, 0x00082c26u),
            new Facelet(26, 33, 38, 0x004c2c04u),
            new Facelet(33, 37, 38, 0x00482c24u),
            new Facelet(31, 36, 40, 0x0201a1a0u),
            new Facelet(32, 36, 31, 0x0301a180u),
            new Facelet(32, 41, 36, 0x0341a100u),
            new Facelet(36, 41, 40, 0x0241a120u),
            new Facelet(11, 18, 9, 0x0000061fu),
            new Facelet(11, 27, 18, 0x0000043fu),
            new Facelet(18, 25, 9, 0x00000e17u),
            new Facelet(27, 25, 18, 0x00000c37u),
            new Facelet(10, 19, 12, 0x003e0600u),
            new Facelet(10, 26, 19, 0x002e0e00u),
            new Facelet(19, 28, 12, 0x007e0400u),
            new Facelet(26, 28, 19, 0x006e0c00u),
            new Facelet(15, 22, 13, 0x0000c1d8u),
            new Facelet(15, 31, 22, 0x0001c1d0u),
            new Facelet(22, 29, 13, 0x000081f8u),
            new Facelet(31, 29, 22, 0x000181f0u),
            new Facelet(14, 23, 16, 0x03b0c000u),
            new Facelet(14, 30, 23, 0x03f08000u),
            new Facelet(23, 32, 16, 0x03a1c000u),
            new Facelet(30, 32, 23, 0x03e18000u),
            new Facelet(0, 6, 11, 0x0000125bu),
            new Facelet(3, 6, 0, 0x00005259u),
            new Facelet(3, 13, 6, 0x000050d9u),
            new Facelet(6, 13, 11, 0x000010dbu),
            new Facelet(27, 34, 37, 0x00002936u),
            new Facelet(29, 34, 27, 0x000021b6u),
            new Facelet(29, 40, 34, 0x000121b4u),
            new Facelet(34, 40, 37, 0x00012934u),
            new Facelet(1, 7, 4, 0x00b25200u),
            new Facelet(1, 12, 7, 0x00b61200u),
            new Facelet(4, 7, 14, 0x01b25000u),
            new Facelet(7, 12, 14, 0x01b61000u),
            new Facelet(28, 35, 30, 0x036c2000u),
            new Facelet(28, 38, 35, 0x026c2800u),
            new Facelet(30, 35, 41, 0x03692000u),
            new Facelet(35, 38, 41, 0x02692800u),
            new Facelet(0, 11, 9, 0x0000161bu),
            new Facelet(1, 10, 12, 0x00361600u),
            new Facelet(3, 15, 13, 0x0000d0d8u),
            new Facelet(4, 14, 16, 0x01b0d000u),
            new Facelet(27, 37, 25, 0x00002c36u),
            new Facelet(26, 38, 28, 0x006c2c00u),
            new Facelet(31, 40, 29, 0x0001a1b0u),
            new Facelet(30, 41, 32, 0x0361a000u)
        };

        internal static ReadOnlySpan<int3> HalfVertices => HalfVerticesValue;
        internal static ReadOnlySpan<int3> Neighbours => NeighboursValue;
        internal static ReadOnlySpan<Facelet> Facelets => FaceletsValue;

        internal static uint NeighbourMask(HashSet<int3> occupied, int3 coord)
        {
            if (occupied == null) throw new ArgumentNullException(nameof(occupied));
            uint mask = 0u;
            for (int index = 0; index < NeighboursValue.Length; index++)
                if (occupied.Contains(coord + NeighboursValue[index]))
                    mask |= 1u << index;
            return mask;
        }

        internal static uint CompatibleNeighbourMask(int3 coord,
            KernelState main, IReadOnlyDictionary<int3, KernelState> context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            uint mask = 0u;
            for (int index = 0; index < NeighboursValue.Length; index++)
            {
                int3 neighbourCoord = coord + NeighboursValue[index];
                if (!context.TryGetValue(neighbourCoord, out KernelState neighbour) ||
                    !neighbour.IsOccupied || !neighbour.HasMeasuredSurfacePlane)
                    continue;
                if (Compatible(coord, main, neighbourCoord, neighbour, context))
                    mask |= 1u << index;
            }
            return mask;
        }

        private static bool Compatible(int3 mainCoord, KernelState main,
            int3 neighbourCoord, KernelState neighbour,
            IReadOnlyDictionary<int3, KernelState> context)
        {
            int3 mainFree = FreeSide(mainCoord, main, context);
            int3 neighbourFree = FreeSide(neighbourCoord, neighbour, context);
            if (math.all(mainFree == int3.zero) ||
                math.all(neighbourFree == int3.zero))
                return true;
            return math.dot(mainFree, neighbourFree) >= 0;
        }

        private static int3 FreeSide(int3 coord, KernelState state,
            IReadOnlyDictionary<int3, KernelState> context)
        {
            if (!state.HasMeasuredSurfacePlane) return int3.zero;
            KernelState.DecodeSurfacePlane(state.Flags, out float3 normal, out _);
            int3 step = NormalStep(normal);
            bool plus = KnownFree(coord + step, context);
            bool minus = KnownFree(coord - step, context);
            if (plus == minus) return int3.zero;
            return plus ? step : -step;
        }

        private static bool KnownFree(int3 coord,
            IReadOnlyDictionary<int3, KernelState> context) =>
            context.TryGetValue(coord, out KernelState state) &&
            !state.IsOccupied &&
            state.OccupancyEvidence <= MerkabaConstants.ExportKnownFreeThreshold;

        private static int3 NormalStep(float3 normal)
        {
            float3 a = math.abs(normal);
            if (a.x >= a.y && a.x >= a.z)
                return new int3(normal.x >= 0f ? 1 : -1, 0, 0);
            if (a.y >= a.z)
                return new int3(0, normal.y >= 0f ? 1 : -1, 0);
            return new int3(0, 0, normal.z >= 0f ? 1 : -1);
        }

        internal static int VisibleFaceletCount(uint neighbourMask)
        {
            int count = 0;
            foreach (Facelet facelet in FaceletsValue)
                if ((facelet.OccluderMask & neighbourMask) == 0u)
                    count++;
            return count;
        }

        internal static float3 GridPosition(int3 kernelCoord, byte vertex)
        {
            int3 halfKey = kernelCoord * 2 + HalfVerticesValue[vertex];
            return (float3)halfKey * (0.5f * MerkabaConstants.LatticeStep);
        }

        internal static float3 FaceNormal(Facelet facelet)
        {
            float3 a = (float3)HalfVerticesValue[facelet.A];
            float3 b = (float3)HalfVerticesValue[facelet.B];
            float3 c = (float3)HalfVerticesValue[facelet.C];
            return math.normalizesafe(math.cross(b - a, c - a));
        }

        internal static string BuildGeneratedHlsl()
        {
            var text = new StringBuilder(16384);
            text.AppendLine("// GENERATED from MerkabaSkin.cs. DO NOT EDIT.");
            text.AppendLine("#ifndef GENESIS_MERKABA_SKIN_INCLUDED");
            text.AppendLine("#define GENESIS_MERKABA_SKIN_INCLUDED");
            text.AppendLine();
            text.AppendLine("#define M8_SKIN_VERTEX_COUNT 42u");
            text.AppendLine("#define M8_SKIN_FACELET_COUNT 80u");
            text.AppendLine("#define M8_SKIN_NEIGHBOUR_COUNT 26u");
            text.AppendLine();
            text.AppendLine("static const int3 M8_SKIN_HALF_VERTICES[42] = {");
            for (int i = 0; i < HalfVerticesValue.Length; i++)
            {
                int3 v = HalfVerticesValue[i];
                text.Append("    int3(").Append(v.x).Append(", ").Append(v.y)
                    .Append(", ").Append(v.z).Append(')');
                text.AppendLine(i + 1 == HalfVerticesValue.Length ? "" : ",");
            }
            text.AppendLine("};");
            text.AppendLine("static const int3 M8_SKIN_NEIGHBOURS[26] = {");
            for (int i = 0; i < NeighboursValue.Length; i++)
            {
                int3 v = NeighboursValue[i];
                text.Append("    int3(").Append(v.x).Append(", ").Append(v.y)
                    .Append(", ").Append(v.z).Append(')');
                text.AppendLine(i + 1 == NeighboursValue.Length ? "" : ",");
            }
            text.AppendLine("};");
            text.AppendLine("static const uint4 M8_SKIN_FACELETS[80] = {");
            for (int i = 0; i < FaceletsValue.Length; i++)
            {
                Facelet f = FaceletsValue[i];
                text.Append("    uint4(").Append(f.A).Append("u, ")
                    .Append(f.B).Append("u, ").Append(f.C).Append("u, 0x")
                    .Append(f.OccluderMask.ToString("x8")).Append("u)");
                text.AppendLine(i + 1 == FaceletsValue.Length ? "" : ",");
            }
            text.AppendLine("};");
            text.AppendLine();
            text.AppendLine("#endif");
            return text.ToString();
        }
    }
}
