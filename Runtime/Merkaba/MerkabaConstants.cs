using System;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>Frozen physical, storage, and evidence constants for the one Merkaba grid.</summary>
    public static class MerkabaConstants
    {
        public const float SupportSize = 0.050f;
        public const float LatticeStep = 0.025f;
        public const float HalfSupport = 0.025f;

        public const int ChunkSize = 32;
        public const int KernelsPerChunk = ChunkSize * ChunkSize * ChunkSize;
        public const int NeighbourCount = 26;

        public const int MinimumEvidence = -32768;
        public const int MaximumEvidence = 32767;
        public const int OccupiedOnThreshold = 512;
        public const int OccupiedOffThreshold = 128;
        public const int ExportKnownFreeThreshold = -OccupiedOnThreshold;
        public const int SurfaceEvidenceScale = 640;
        public const int FreeEvidenceScale = 256;
        public const int MaximumColorConfidence = 65535;
        public const float MinimumSurfaceQuality = 0.25f;

        public const uint OccupiedFlag = 1u << 0;

        private static readonly int3[] NeighboursValue = BuildNeighbours();

        /// <summary>The 6 axis, 12 face-diagonal, and 8 body-diagonal offsets.</summary>
        public static ReadOnlySpan<int3> Neighbours => NeighboursValue;

        public static int FloorDiv(int value, int divisor)
        {
            if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        public static int FloorMod(int value, int divisor)
        {
            if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
            int remainder = value % divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }

        public static int3 ChunkCoord(int3 global) => new(
            FloorDiv(global.x, ChunkSize),
            FloorDiv(global.y, ChunkSize),
            FloorDiv(global.z, ChunkSize));

        public static int3 LocalCoord(int3 global) => new(
            FloorMod(global.x, ChunkSize),
            FloorMod(global.y, ChunkSize),
            FloorMod(global.z, ChunkSize));

        public static int3 ChunkOrigin(int3 chunk) => chunk * ChunkSize;

        public static int Flatten(int3 local)
        {
            if (math.any(local < 0) || math.any(local >= ChunkSize))
                throw new ArgumentOutOfRangeException(nameof(local));
            return local.x + ChunkSize * (local.y + ChunkSize * local.z);
        }

        public static int3 Unflatten(int index)
        {
            if ((uint)index >= KernelsPerChunk)
                throw new ArgumentOutOfRangeException(nameof(index));
            int x = index % ChunkSize;
            int yz = index / ChunkSize;
            int y = yz % ChunkSize;
            int z = yz / ChunkSize;
            return new int3(x, y, z);
        }

        public static float3 WorldCenter(int3 global) => (float3)global * LatticeStep;

        public static bool LexicographicallyLess(int3 left, int3 right) =>
            left.x < right.x ||
            (left.x == right.x && (left.y < right.y ||
             (left.y == right.y && left.z < right.z)));

        private static int3[] BuildNeighbours()
        {
            var result = new int3[NeighbourCount];
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

    }
}
