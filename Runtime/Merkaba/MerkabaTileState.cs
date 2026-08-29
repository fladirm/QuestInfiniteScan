using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct MerkabaTileAddress : IEquatable<MerkabaTileAddress>,
        IComparable<MerkabaTileAddress>
    {
        internal readonly int3 BlockCoord;
        internal readonly uint LocalAddress;

        internal MerkabaTileAddress(int3 blockCoord, uint localAddress)
        {
            if ((localAddress & 0xffff8000u) != 0u)
                throw new ArgumentOutOfRangeException(nameof(localAddress));
            BlockCoord = blockCoord;
            LocalAddress = localAddress;
        }

        internal int ChunkLocal => (int)(LocalAddress & 0x1ffu);
        internal int TileLocal => (int)((LocalAddress >> 9) & 0x3fu);

        public bool Equals(MerkabaTileAddress other) =>
            math.all(BlockCoord == other.BlockCoord) &&
            LocalAddress == other.LocalAddress;

        public override bool Equals(object obj) =>
            obj is MerkabaTileAddress other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(BlockCoord.x, BlockCoord.y, BlockCoord.z,
                LocalAddress);

        public int CompareTo(MerkabaTileAddress other)
        {
            if (BlockCoord.x != other.BlockCoord.x)
                return BlockCoord.x.CompareTo(other.BlockCoord.x);
            if (BlockCoord.y != other.BlockCoord.y)
                return BlockCoord.y.CompareTo(other.BlockCoord.y);
            if (BlockCoord.z != other.BlockCoord.z)
                return BlockCoord.z.CompareTo(other.BlockCoord.z);
            return LocalAddress.CompareTo(other.LocalAddress);
        }
    }

    internal sealed class MerkabaTileSnapshot
    {
        internal MerkabaTileAddress Address;
        internal uint Generation;
        internal KernelState[] States;
    }

    internal readonly struct MerkabaKernelSnapshot
    {
        internal readonly int3 Coord;
        internal readonly KernelState State;

        internal MerkabaKernelSnapshot(int3 coord, KernelState state)
        {
            Coord = coord;
            State = state;
        }
    }
}
