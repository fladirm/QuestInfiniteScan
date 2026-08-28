using System;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>Dense state storage for one allocated 32-cubed sparse-world chunk.</summary>
    public sealed class MerkabaChunk
    {
        public int3 Coord { get; }
        public KernelState[] States { get; }
        /// <summary>
        /// Derived 768-byte occupancy halo. It is rebuilt from canonical States and is
        /// never persisted as a second reconstruction authority.
        /// </summary>
        internal uint[] BoundaryOccupancyWords { get; }
        public int OccupiedCount { get; internal set; }
        public bool CpuStateCurrent { get; internal set; } = true;
        public bool Persisted { get; internal set; }

        public MerkabaChunk(int3 coord)
        {
            Coord = coord;
            States = new KernelState[MerkabaConstants.KernelsPerChunk];
            BoundaryOccupancyWords = new uint[MerkabaConstants.BoundaryWordCount];
        }

        public ref KernelState StateAt(int3 local) =>
            ref States[MerkabaConstants.Flatten(local)];

        public ref KernelState StateAt(int index)
        {
            if ((uint)index >= States.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return ref States[index];
        }

        internal void SetBoundaryOccupancy(int3 local, bool occupied)
        {
            if (local.x == 0) SetFaceBit(0, local.y, local.z, occupied);
            if (local.x == MerkabaConstants.ChunkSize - 1)
                SetFaceBit(1, local.y, local.z, occupied);
            if (local.y == 0) SetFaceBit(2, local.x, local.z, occupied);
            if (local.y == MerkabaConstants.ChunkSize - 1)
                SetFaceBit(3, local.x, local.z, occupied);
            if (local.z == 0) SetFaceBit(4, local.x, local.y, occupied);
            if (local.z == MerkabaConstants.ChunkSize - 1)
                SetFaceBit(5, local.x, local.y, occupied);
        }

        internal bool BoundaryOccupied(int3 local)
        {
            if (local.x == 0) return FaceBit(0, local.y, local.z);
            if (local.x == MerkabaConstants.ChunkSize - 1)
                return FaceBit(1, local.y, local.z);
            if (local.y == 0) return FaceBit(2, local.x, local.z);
            if (local.y == MerkabaConstants.ChunkSize - 1)
                return FaceBit(3, local.x, local.z);
            if (local.z == 0) return FaceBit(4, local.x, local.y);
            if (local.z == MerkabaConstants.ChunkSize - 1)
                return FaceBit(5, local.x, local.y);
            return false;
        }

        internal void RebuildBoundaryOccupancy()
        {
            Array.Clear(BoundaryOccupancyWords, 0, BoundaryOccupancyWords.Length);
            for (int index = 0; index < States.Length; index++)
            {
                if (!States[index].IsOccupied) continue;
                int3 local = MerkabaConstants.Unflatten(index);
                if (local.x != 0 && local.x != MerkabaConstants.ChunkSize - 1 &&
                    local.y != 0 && local.y != MerkabaConstants.ChunkSize - 1 &&
                    local.z != 0 && local.z != MerkabaConstants.ChunkSize - 1)
                    continue;
                SetBoundaryOccupancy(local, true);
            }
        }

        private void SetFaceBit(int face, int u, int v, bool occupied)
        {
            int bitIndex = face * MerkabaConstants.BoundaryBitsPerFace +
                           u + MerkabaConstants.ChunkSize * v;
            int word = bitIndex >> 5;
            uint bit = 1u << (bitIndex & 31);
            if (occupied) BoundaryOccupancyWords[word] |= bit;
            else BoundaryOccupancyWords[word] &= ~bit;
        }

        private bool FaceBit(int face, int u, int v)
        {
            int bitIndex = face * MerkabaConstants.BoundaryBitsPerFace +
                           u + MerkabaConstants.ChunkSize * v;
            return (BoundaryOccupancyWords[bitIndex >> 5] &
                    (1u << (bitIndex & 31))) != 0u;
        }
    }
}
