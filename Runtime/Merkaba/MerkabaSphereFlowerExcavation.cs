using System;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        public const int DirtFaceClassCount = 6;
        public static readonly float4 DirtSupportLinearRgba =
            new(0.35f, 0.35f, 0.35f, 1f);

        internal enum ExcavationCellState : byte
        {
            Full = 0,
            Free = 1,
            Ambiguous = 2
        }

        // These eight values describe the supports U+b, never samples of a
        // scalar field. One certified covering support proves the whole cell.
        internal static ExcavationCellState ClassifyFreeCell(
            ReadOnlySpan<MerkabaDualReadResult> supports)
        {
            if (supports.Length != 8)
                throw new ArgumentException("A cell has exactly eight covering supports.", nameof(supports));
            bool allFull = true;
            for (int i = 0; i < 8; ++i)
            {
                if (supports[i] == MerkabaDualReadResult.CertainThrough)
                    return ExcavationCellState.Free;
                allFull &= supports[i] == MerkabaDualReadResult.CertainFull;
            }
            return allFull ? ExcavationCellState.Full : ExcavationCellState.Ambiguous;
        }

        internal static ExcavationCellState ClassifyFreeCell(uint packedSupports)
        {
            if ((packedSupports & 0xffff0000u) != 0u)
                return ExcavationCellState.Ambiguous;
            if ((packedSupports & ~(packedSupports >> 1) & 0x5555u) != 0u)
                return ExcavationCellState.Free;
            return packedSupports == 0u ? ExcavationCellState.Full : ExcavationCellState.Ambiguous;
        }

        internal static ProofClassification ClassifyDirtFace(
            ExcavationCellState source, ExcavationCellState neighbor)
        {
            if (source == ExcavationCellState.Full || neighbor == ExcavationCellState.Free)
                return ProofClassification.Impossible;
            return source == ExcavationCellState.Free && neighbor == ExcavationCellState.Full
                ? ProofClassification.Certain : ProofClassification.Ambiguous;
        }

        // Face order is the contract's +X,-X,+Y,-Y,+Z,-Z. n points from FREE
        // into FULL; every presentation triangle has cross(edge1,edge2)=-n.
        public static int3 DirtFaceDirection(int face)
        {
            if ((uint)face >= DirtFaceClassCount)
                throw new ArgumentOutOfRangeException(nameof(face));
            int3 direction = default;
            direction[face >> 1] = (face & 1) == 0 ? 1 : -1;
            return direction;
        }

        internal static int3 DirtFaceCorner(int face, int corner)
        {
            if ((uint)face >= DirtFaceClassCount || (uint)corner >= 4u)
                throw new ArgumentOutOfRangeException(nameof(face));
            int axis = face >> 1;
            bool positive = (face & 1) == 0;
            int a = corner == 1 || corner == 2 ? 1 : 0;
            int b = corner >= 2 ? 1 : 0;
            int3 offset = default;
            offset[axis] = positive ? 1 : 0;
            offset[(axis + 1) % 3] = positive ? b : a;
            offset[(axis + 2) % 3] = positive ? a : b;
            return offset;
        }

        public static int3 DirtFaceVertex(int3 cell, int face, int half, int vertex)
        {
            if ((uint)half >= 2u || (uint)vertex >= 3u)
                throw new ArgumentOutOfRangeException(nameof(half));
            int corner = vertex == 0 ? 0 : vertex + half;
            int3 offset = DirtFaceCorner(face, corner);
            return new int3(checked(cell.x + offset.x),
                checked(cell.y + offset.y), checked(cell.z + offset.z));
        }

        public static float3 DirtFaceGridPosition(int3 cell, int face, int half, int vertex)
        {
            int3 p = DirtFaceVertex(cell, face, half, vertex);
            return new float3(DirtGridCoordinate(p.x), DirtGridCoordinate(p.y),
                DirtGridCoordinate(p.z));
        }

        // Exact integer/40 -> binary32 RN-even. Neither int->float->multiply
        // double rounding nor hardware binary64 is required on the GPU.
        public static float DirtGridCoordinate(int coordinate)
        {
            if (coordinate == 0) return 0f;
            uint magnitude = coordinate < 0 ? unchecked(0u - (uint)coordinate) : (uint)coordinate;
            int highest = 31 - math.lzcnt(magnitude);
            int exponent = highest - 6;
            bool upper = highest >= 5
                ? magnitude >= (40u << (highest - 5))
                : (magnitude << (5 - highest)) >= 40u;
            if (upper) ++exponent;
            int shift = 20 - exponent;
            uint numerator = shift >= 0 ? magnitude << shift : magnitude;
            uint denominator = shift >= 0 ? 5u : 5u << -shift;
            uint significand = numerator / denominator;
            uint twiceRemainder = (numerator - significand * denominator) << 1;
            if (twiceRemainder > denominator ||
                (twiceRemainder == denominator && (significand & 1u) != 0u))
                ++significand;
            if (significand == 0x01000000u)
            {
                significand >>= 1;
                ++exponent;
            }
            uint bits = (coordinate < 0 ? 0x80000000u : 0u) |
                ((uint)(exponent + 127) << 23) | (significand & 0x007fffffu);
            return math.asfloat(bits);
        }
    }
}
