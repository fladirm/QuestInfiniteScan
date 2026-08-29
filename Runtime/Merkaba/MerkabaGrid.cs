using System;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// One GPU-authoritative signed M8 world. CPU state is limited to sampled telemetry
    /// and explicit storage/export operation snapshots.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class MerkabaGrid : MonoBehaviour
    {
        public static MerkabaGrid Instance { get; private set; }

        public int ActiveChunkCount => M8ChunkCount;
        public int OccupiedKernelCount => M8OccupiedKernelCount;
        public int HotTileCount => M8HotTileCount;
        public int ColdTileCount => M8ColdTileCount;

        public event Action Cleared;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            ReleaseGpuResources();
            if (Instance == this) Instance = null;
        }

        public void Clear()
        {
            ClearGpuWorldForNewScan();
            Cleared?.Invoke();
        }
    }
}
