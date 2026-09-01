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

        private Matrix4x4 _sceneGridToWorld;

        private void Awake()
        {
            Instance = this;
            _sceneGridToWorld = transform.localToWorldMatrix;
        }

        internal void RelocateForLoadedAnchor(Matrix4x4 anchorNow,
            Matrix4x4 anchorAtSave)
        {
            Matrix4x4 relocated = anchorNow * anchorAtSave.inverse *
                _sceneGridToWorld;
            Vector4 position = relocated.GetColumn(3);
            transform.SetPositionAndRotation(
                new Vector3(position.x, position.y, position.z),
                relocated.rotation);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Clear()
        {
            ClearGpuWorldForNewScan();
            Cleared?.Invoke();
        }
    }
}
