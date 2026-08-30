using System;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Frozen signed M8 address and PCG3D block-hash contract shared with GPU code.
    /// </summary>
    public static class MerkabaSpatial
    {
        public const int BlockKernelSpan = 256;
        public const int BlockChunkCount = 512;
        public const int TilesPerChunk = 64;
        public const int TileSize = 8;
        public const int KernelsPerTile = 512;
        public const float BlockWorldSize =
            BlockKernelSpan * MerkabaConstants.LatticeStep;

        public const int BlockCapacity = 8192;
        public const int ChunkCapacity = 262144;
        public const int PhysicalTileCapacity = 32768;
        public const int PhysicalTileBankCapacity = 8192;
        public const int PhysicalTileBankShift = 13;
        public const int PhysicalTileBankMask =
            PhysicalTileBankCapacity - 1;
        public const int PhysicalTileBankCount =
            PhysicalTileCapacity / PhysicalTileBankCapacity;
        public const int HashBucketCount = 8192;
        public const int HashBucketMask = HashBucketCount - 1;
        public const int HashSlotsPerBucket = 4;
        public const int HashEntryCount = HashBucketCount * HashSlotsPerBucket;
        public const int OwnerChunkOffset = BlockCapacity;
        public const int OwnerRecordCount = BlockCapacity + ChunkCapacity;
        public const int ChunkPresenceStride = 9;
        public const int ChunkPresenceWordCount =
            ChunkCapacity * ChunkPresenceStride;
        public const int TileWordCount = 16;
        public const int TileBitRecordCount =
            PhysicalTileCapacity * TileWordCount;
        public const int TileRecordStride = 2;
        public const int TileRecordCount =
            PhysicalTileCapacity * TileRecordStride;
        public const int ClaimBlockOffset = 0;
        public const int ClaimChunkOffset = BlockCapacity;
        public const int ClaimTileOffset = BlockCapacity + ChunkCapacity;
        public const int ClaimRecordCount =
            BlockCapacity + ChunkCapacity + PhysicalTileCapacity;

        public const uint EmptyRef = 0u;
        public const uint ClaimedNewRef = 0xffffffffu;
        public const uint ColdOnSsdRef = 0xfffffffeu;
        public const uint LoadingRef = 0xfffffffdu;
        public const uint EvictingRef = 0xfffffffcu;

        public static int PhysicalTileBank(int physicalSlot)
        {
            if ((uint)physicalSlot >= PhysicalTileCapacity)
                throw new ArgumentOutOfRangeException(nameof(physicalSlot));
            return physicalSlot >> PhysicalTileBankShift;
        }

        public static int PhysicalTileInBank(int physicalSlot)
        {
            if ((uint)physicalSlot >= PhysicalTileCapacity)
                throw new ArgumentOutOfRangeException(nameof(physicalSlot));
            return physicalSlot & PhysicalTileBankMask;
        }

        public static int BankStateIndex(int physicalSlot, int kernelLocal)
        {
            if ((uint)kernelLocal >= KernelsPerTile)
                throw new ArgumentOutOfRangeException(nameof(kernelLocal));
            return checked(PhysicalTileInBank(physicalSlot) * KernelsPerTile +
                           kernelLocal);
        }

        public readonly struct Address : IEquatable<Address>
        {
            public readonly int3 BlockCoord;
            public readonly int3 Local;
            public readonly byte D4;
            public readonly byte D3;
            public readonly byte D2;
            public readonly byte D1;
            public readonly byte D0;
            public readonly int ChunkLocal;
            public readonly int TileLocal;
            public readonly int KernelLocal;

            internal Address(int3 blockCoord, int3 local, byte d4, byte d3,
                byte d2, byte d1, byte d0, int chunkLocal, int tileLocal,
                int kernelLocal)
            {
                BlockCoord = blockCoord;
                Local = local;
                D4 = d4;
                D3 = d3;
                D2 = d2;
                D1 = d1;
                D0 = d0;
                ChunkLocal = chunkLocal;
                TileLocal = tileLocal;
                KernelLocal = kernelLocal;
            }

            public int3 GlobalCoord => BlockCoord * BlockKernelSpan + Local;
            public uint LocalAddress => (uint)(ChunkLocal | (TileLocal << 9));

            public bool Equals(Address other) =>
                math.all(BlockCoord == other.BlockCoord) &&
                math.all(Local == other.Local) &&
                D4 == other.D4 && D3 == other.D3 && D2 == other.D2 &&
                D1 == other.D1 && D0 == other.D0 &&
                ChunkLocal == other.ChunkLocal &&
                TileLocal == other.TileLocal && KernelLocal == other.KernelLocal;

            public override bool Equals(object obj) =>
                obj is Address other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(BlockCoord.GetHashCode(), Local.GetHashCode(),
                    ChunkLocal, TileLocal, KernelLocal);
        }

        public static Address Encode(int3 globalCoord)
        {
            int3 blockCoord = new(
                FloorDiv(globalCoord.x, BlockKernelSpan),
                FloorDiv(globalCoord.y, BlockKernelSpan),
                FloorDiv(globalCoord.z, BlockKernelSpan));
            int3 local = new(
                FloorMod(globalCoord.x, BlockKernelSpan),
                FloorMod(globalCoord.y, BlockKernelSpan),
                FloorMod(globalCoord.z, BlockKernelSpan));

            byte d4 = Octant(local, 7);
            byte d3 = Octant(local, 6);
            byte d2 = Octant(local, 5);
            byte d1 = Octant(local, 4);
            byte d0 = Octant(local, 3);
            int chunkLocal = (d4 << 6) | (d3 << 3) | d2;
            int tileLocal = (d1 << 3) | d0;
            int kernelLocal = (local.x & 7) +
                              TileSize * ((local.y & 7) +
                              TileSize * (local.z & 7));
            return new Address(blockCoord, local, d4, d3, d2, d1, d0,
                chunkLocal, tileLocal, kernelLocal);
        }

        public static int3 Decode(int3 blockCoord, int chunkLocal,
            int tileLocal, int kernelLocal)
        {
            if ((uint)chunkLocal >= BlockChunkCount)
                throw new ArgumentOutOfRangeException(nameof(chunkLocal));
            if ((uint)tileLocal >= TilesPerChunk)
                throw new ArgumentOutOfRangeException(nameof(tileLocal));
            if ((uint)kernelLocal >= KernelsPerTile)
                throw new ArgumentOutOfRangeException(nameof(kernelLocal));

            int d4 = (chunkLocal >> 6) & 7;
            int d3 = (chunkLocal >> 3) & 7;
            int d2 = chunkLocal & 7;
            int d1 = (tileLocal >> 3) & 7;
            int d0 = tileLocal & 7;
            int kx = kernelLocal & 7;
            int ky = (kernelLocal >> 3) & 7;
            int kz = (kernelLocal >> 6) & 7;
            int3 local = new(
                OctantAxis(d4, 0, 7) | OctantAxis(d3, 0, 6) |
                OctantAxis(d2, 0, 5) | OctantAxis(d1, 0, 4) |
                OctantAxis(d0, 0, 3) | kx,
                OctantAxis(d4, 1, 7) | OctantAxis(d3, 1, 6) |
                OctantAxis(d2, 1, 5) | OctantAxis(d1, 1, 4) |
                OctantAxis(d0, 1, 3) | ky,
                OctantAxis(d4, 2, 7) | OctantAxis(d3, 2, 6) |
                OctantAxis(d2, 2, 5) | OctantAxis(d1, 2, 4) |
                OctantAxis(d0, 2, 3) | kz);
            return blockCoord * BlockKernelSpan + local;
        }

        public static int3 Decode(int3 blockCoord, uint localAddress,
            int kernelLocal) => Decode(blockCoord,
                (int)(localAddress & 0x1ffu),
                (int)((localAddress >> 9) & 0x3fu), kernelLocal);

        public static uint3 Pcg3d(int3 blockCoord)
        {
            unchecked
            {
                uint x = (uint)blockCoord.x * 1664525u + 1013904223u;
                uint y = (uint)blockCoord.y * 1664525u + 1013904223u;
                uint z = (uint)blockCoord.z * 1664525u + 1013904223u;
                x += y * z;
                y += z * x;
                z += x * y;
                x ^= x >> 16;
                y ^= y >> 16;
                z ^= z >> 16;
                x += y * z;
                y += z * x;
                z += x * y;
                return new uint3(x, y, z);
            }
        }

        public static uint2 BucketPair(int3 blockCoord)
        {
            uint3 hash = Pcg3d(blockCoord);
            uint first = hash.x & HashBucketMask;
            uint second = hash.y & HashBucketMask;
            if (first == second) second ^= 1u;
            return new uint2(first, second);
        }

        public static uint2 BucketSearchOrder(int3 blockCoord)
        {
            uint3 hash = Pcg3d(blockCoord);
            uint2 pair = BucketPair(blockCoord);
            return (hash.z & 1u) == 0u ? pair : pair.yx;
        }

        /// <summary>
        /// Selects the one canonical lattice kernel owned by a measured surface
        /// point. Half-step ties are resolved away from zero identically to the
        /// production HLSL implementation.
        /// </summary>
        public static int3 NearestKernel(float3 gridPosition) => new(
            RoundNearest(gridPosition.x), RoundNearest(gridPosition.y),
            RoundNearest(gridPosition.z));

        private static int RoundNearest(float value) => value >= 0f
            ? (int)math.floor(value + 0.5f)
            : (int)math.ceil(value - 0.5f);

        public static int FloorDiv(int value, int divisor)
        {
            if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
            int quotient = value / divisor;
            int remainder = value - quotient * divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        public static int FloorMod(int value, int divisor)
        {
            if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
            int quotient = value / divisor;
            int remainder = value - quotient * divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }

        private static byte Octant(int3 local, int bit) => (byte)(
            ((local.x >> bit) & 1) |
            (((local.y >> bit) & 1) << 1) |
            (((local.z >> bit) & 1) << 2));

        private static int OctantAxis(int digit, int axis, int bit) =>
            ((digit >> axis) & 1) << bit;
    }
}
