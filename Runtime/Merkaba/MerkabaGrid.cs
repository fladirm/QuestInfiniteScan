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

        private Matrix4x4 _anchorFromGrid = Matrix4x4.identity;
        private RoomSpaceRoot _boundRoomSpace;

        /// <summary>
        /// The one persisted spatial relation of the M8 lattice: grid frame
        /// expressed in the session anchor frame. RoomSpaceRoot local space is
        /// the anchor space, so this is also the grid's local pose under it.
        /// </summary>
        internal Matrix4x4 AnchorFromGrid => _anchorFromGrid;

        private void Awake()
        {
            Instance = this;
        }

        private void Start() => BindRoomSpace();

        internal void SetAnchorFromGrid(Matrix4x4 anchorFromGrid)
        {
            if (!IsRigid(anchorFromGrid))
                throw new ArgumentException(
                    "AnchorFromGrid must be a finite rigid transform.",
                    nameof(anchorFromGrid));
            _anchorFromGrid = anchorFromGrid;
            BindRoomSpace();
            ApplyAnchorFrame();
        }

        private void BindRoomSpace()
        {
            RoomSpaceRoot root = RoomSpaceRoot.Instance;
            if (root == _boundRoomSpace) return;
            if (_boundRoomSpace != null) _boundRoomSpace.Bound -= OnRoomSpaceBound;
            _boundRoomSpace = root;
            if (root != null) root.Bound += OnRoomSpaceBound;
        }

        // RoomSpaceRoot preserves children's world poses when it rebinds. For
        // the M8 lattice that would bake the previous tracking frame into the
        // local offset, so the persisted relation is re-applied on every bind.
        private void OnRoomSpaceBound(Transform anchor) => ApplyAnchorFrame();

        private void ApplyAnchorFrame()
        {
            RoomSpaceRoot root = RoomSpaceRoot.Instance;
            if (root != null && transform.parent != root.transform)
                transform.SetParent(root.transform, false);
            Vector4 position = _anchorFromGrid.GetColumn(3);
            transform.localPosition = new Vector3(position.x, position.y,
                position.z);
            transform.localRotation = _anchorFromGrid.rotation;
            transform.localScale = Vector3.one;
        }

        internal static bool IsRigid(Matrix4x4 matrix)
        {
            for (int index = 0; index < 16; index++)
                if (!float.IsFinite(matrix[index])) return false;
            if (Mathf.Abs(matrix.m30) > 1e-5f || Mathf.Abs(matrix.m31) > 1e-5f ||
                Mathf.Abs(matrix.m32) > 1e-5f ||
                Mathf.Abs(matrix.m33 - 1f) > 1e-5f)
                return false;
            for (int column = 0; column < 3; column++)
                if (Mathf.Abs(((Vector3)matrix.GetColumn(column)).magnitude -
                        1f) > 1e-3f)
                    return false;
            return Mathf.Abs(matrix.determinant - 1f) < 1e-3f;
        }

        private void OnDestroy()
        {
            if (_boundRoomSpace != null) _boundRoomSpace.Bound -= OnRoomSpaceBound;
            if (Instance == this) Instance = null;
        }

        public void Clear()
        {
            ClearGpuWorldForNewScan();
            Cleared?.Invoke();
        }
    }
}
