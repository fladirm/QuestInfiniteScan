using System;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>Dense state storage for one allocated 32-cubed sparse-world chunk.</summary>
    public sealed class MerkabaChunk
    {
        public int3 Coord { get; }
        public KernelState[] States { get; }
        public int OccupiedCount { get; internal set; }
        public bool CpuStateCurrent { get; internal set; } = true;
        public bool Persisted { get; internal set; }

        public MerkabaChunk(int3 coord)
        {
            Coord = coord;
            States = new KernelState[MerkabaConstants.KernelsPerChunk];
        }

        public ref KernelState StateAt(int3 local) =>
            ref States[MerkabaConstants.Flatten(local)];

        public ref KernelState StateAt(int index)
        {
            if ((uint)index >= States.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return ref States[index];
        }
    }
}
