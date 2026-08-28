using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Compacts visible non-interior kernel records on GPU and renders the frozen
    /// canonical boundary basis directly. It never creates a Mesh or GameObject per kernel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaGridRenderer : MonoBehaviour
    {
        [SerializeField] private ComputeShader topologyCompute;
        [SerializeField] private Shader renderShader;
        [SerializeField, Range(2f, 12f)] private float renderDistance = 8f;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct RenderRecord
        {
            public int X, Y, Z;
            public uint ActiveMask;
            public uint PackedColor;
            public uint Padding0, Padding1, Padding2;
        }

        private MerkabaGrid _grid;
        private Material _material;
        private ComputeBuffer _renderRecords;
        private ComputeBuffer _indirectArgs;
        private int _buildKernel = -1;
        private bool _initialized;
        private bool _countReadbackPending;
        private float _nextCountReadback;

        public int VisibleSurfaceKernelCount { get; private set; }

        private static readonly int KernelsId = Shader.PropertyToID("_MerkabaKernels");
        private static readonly int PageCoordsId = Shader.PropertyToID("_MerkabaPageCoords");
        private static readonly int PageNeighboursId = Shader.PropertyToID("_MerkabaPageNeighbours");
        private static readonly int VisibleSlotsId = Shader.PropertyToID("_MerkabaVisibleSlots");
        private static readonly int DirtyId = Shader.PropertyToID("_MerkabaKernelDirty");
        private static readonly int MasksId = Shader.PropertyToID("_MerkabaTopologyMasks");
        private static readonly int RecordsId = Shader.PropertyToID("_MerkabaRenderRecords");
        private static readonly int VisibleCountId = Shader.PropertyToID("_MerkabaVisibleChunkCount");
        private static readonly int GridToWorldId = Shader.PropertyToID("_MerkabaGridToWorld");

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
        }

        private void OnDestroy()
        {
            _renderRecords?.Release();
            _indirectArgs?.Release();
            if (_material != null) Destroy(_material);
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null || _grid == null) return;
            if (!_initialized && !Initialize()) return;

            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null || !scanner.IsScanning)
                _grid.RefreshResidency(camera, renderDistance, false);
            if (_grid.VisibleChunkCount == 0) return;

            _renderRecords.SetCounterValue(0);
            topologyCompute.SetBuffer(_buildKernel, KernelsId, _grid.KernelBuffer);
            topologyCompute.SetBuffer(_buildKernel, PageCoordsId, _grid.PageCoordsBuffer);
            topologyCompute.SetBuffer(_buildKernel, PageNeighboursId,
                _grid.PageNeighboursBuffer);
            topologyCompute.SetBuffer(_buildKernel, VisibleSlotsId, _grid.VisibleSlotsBuffer);
            topologyCompute.SetBuffer(_buildKernel, DirtyId, _grid.KernelDirtyBuffer);
            topologyCompute.SetBuffer(_buildKernel, MasksId, _grid.TopologyMaskBuffer);
            topologyCompute.SetBuffer(_buildKernel, RecordsId, _renderRecords);
            topologyCompute.SetInt(VisibleCountId, _grid.VisibleChunkCount);
            int total = _grid.VisibleChunkCount * MerkabaConstants.KernelsPerChunk;
            topologyCompute.Dispatch(_buildKernel, Mathf.CeilToInt(total / 64f), 1, 1);

            ComputeBuffer.CopyCount(_renderRecords, _indirectArgs, sizeof(uint));
            _material.SetBuffer(RecordsId, _renderRecords);
            _material.SetMatrix(GridToWorldId, _grid.GridToWorldMatrix);
            Bounds bounds = new(camera.transform.position,
                Vector3.one * (renderDistance * 2.5f));
            Graphics.DrawProceduralIndirect(_material, bounds, MeshTopology.Triangles,
                _indirectArgs, 0, null, null, ShadowCastingMode.On, true, gameObject.layer);

            if (!_countReadbackPending && Time.unscaledTime >= _nextCountReadback)
            {
                _nextCountReadback = Time.unscaledTime + 1f;
                _countReadbackPending = true;
                AsyncGPUReadback.Request(_indirectArgs, request =>
                {
                    _countReadbackPending = false;
                    if (!request.hasError && request.GetData<uint>().Length >= 2)
                        VisibleSurfaceKernelCount = (int)request.GetData<uint>()[1];
                });
            }
        }

        private bool Initialize()
        {
            if (topologyCompute == null || renderShader == null)
            {
                Logger.Error("MerkabaGridRenderer: shader references are not wired");
                enabled = false;
                return false;
            }
            _grid.EnsureGpuResources();
            _buildKernel = topologyCompute.FindKernel("BuildVisibleRecords");
            int capacity = checked(_grid.MaxVisibleChunks * MerkabaConstants.KernelsPerChunk);
            int stride = Marshal.SizeOf<RenderRecord>();
            if (stride != 32)
                throw new System.InvalidOperationException(
                    $"Merkaba render-record ABI must be 32 bytes, got {stride}.");
            _renderRecords = new ComputeBuffer(capacity, stride, ComputeBufferType.Append);
            _indirectArgs = new ComputeBuffer(4, sizeof(uint),
                ComputeBufferType.IndirectArguments);
            _indirectArgs.SetData(new uint[]
            {
                MerkabaConstants.BoundaryPatchCount * MerkabaConstants.VerticesPerPatch,
                0, 0, 0
            });
            _material = new Material(renderShader) { name = "MerkabaGrid (Runtime)" };
            _initialized = true;
            return true;
        }
    }
}
