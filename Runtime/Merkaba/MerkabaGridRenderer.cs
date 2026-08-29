using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>GPU M8 Q_DRAW/Q_WARM frame compiler and one indirect SPI draw.</summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaGridRenderer : MonoBehaviour
    {
        private static MerkabaGridRenderer _active;

        [SerializeField] private ComputeShader frameCompilerCompute;
        [SerializeField] private Shader renderShader;
        [SerializeField, Range(2f, 12f)] private float renderDistance = 8f;
        [SerializeField, Range(0f, 1f)] private float scanOpacity = 1f;

        private MerkabaGrid _grid;
        private Material _material;
        private int _resetKernel;
        private int _queryKernel;
        private int _prepareKernel;
        private int _compileKernel;
        private int _finalizeKernel;
        private bool _initialized;
        private bool _gpuSubmissionSuspended;
        private bool _statusReadbackPending;
        private float _nextStatusReadback;
        private readonly Vector4[] _drawPlanes = new Vector4[12];
        private readonly Vector4[] _eyePositions = new Vector4[2];
        private readonly Plane[] _frustumScratch = new Plane[6];

        public int VisiblePrimitiveCount { get; private set; }
        public int VisibleSurfaceKernelCount { get; private set; }
        public int VisibleChunkCount { get; private set; }
        public int VisibleTileCount { get; private set; }
        public int LateDrawColdMisses { get; private set; }
        public bool RenderPrimitiveOverflow { get; private set; }
        public float ScanOpacity
        {
            get => scanOpacity;
            set
            {
                scanOpacity = Mathf.Clamp01(value);
                ApplyOpacityState();
            }
        }

        private static readonly int GridToWorldId =
            Shader.PropertyToID("_MerkabaGridToWorld");
        private static readonly int WorldToGridId =
            Shader.PropertyToID("_MerkabaWorldToGrid");
        private static readonly int VisibleTilesId =
            Shader.PropertyToID("_M8VisibleTiles");
        private static readonly int VisiblePrimitivesId =
            Shader.PropertyToID("_M8VisiblePrimitives");
        private static readonly int FrameDispatchArgsId =
            Shader.PropertyToID("_M8FrameDispatchArgs");
        private static readonly int DrawArgsId = Shader.PropertyToID("_M8DrawArgs");
        private static readonly int ScanOpacityId = Shader.PropertyToID("_ScanOpacity");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        private void Awake() => _grid = GetComponent<MerkabaGrid>();

        private void OnEnable()
        {
            if (!_gpuSubmissionSuspended) _active = this;
        }

        private void OnDisable()
        {
            if (_active == this) _active = null;
            MerkabaGpuTimestamps.CloseIncompleteFrame();
        }

        private void OnDestroy()
        {
            if (_active == this) _active = null;
        }

        internal void SuspendGpuSubmission()
        {
            _gpuSubmissionSuspended = true;
            if (_active == this) _active = null;
            MerkabaGpuTimestamps.CloseIncompleteFrame();
        }

        internal void ResumeGpuSubmission()
        {
            _gpuSubmissionSuspended = false;
            if (isActiveAndEnabled) _active = this;
        }

        internal void ReleaseOwnedResourcesAfterGpuRetirement()
        {
            if (_material != null) Destroy(_material);
            _material = null;
            _initialized = false;
            _statusReadbackPending = false;
        }

        internal static bool TryGetActive(Camera camera,
            out MerkabaGridRenderer renderer)
        {
            renderer = _active;
            return renderer != null && renderer._initialized &&
                   renderer.isActiveAndEnabled && camera == Camera.main;
        }

        private bool Initialize()
        {
            if (_initialized) return true;
            if (_grid == null || frameCompilerCompute == null || renderShader == null)
            {
                Logger.Error("Merkaba frame compiler assets are not wired.");
                enabled = false;
                return false;
            }
            _grid.EnsureGpuResources();
            _resetKernel = frameCompilerCompute.FindProfiledKernel(
                "ResetFrame", MerkabaGpuStage.WorldQuery);
            _queryKernel = frameCompilerCompute.FindProfiledKernel(
                "QueryM8Frame", MerkabaGpuStage.WorldQuery);
            _prepareKernel = frameCompilerCompute.FindProfiledKernel(
                "PrepareFrameCompilerArgs", MerkabaGpuStage.FrameCompile);
            _compileKernel = frameCompilerCompute.FindProfiledKernel(
                "CompileVisiblePrimitives", MerkabaGpuStage.FrameCompile);
            _finalizeKernel = frameCompilerCompute.FindProfiledKernel(
                "FinalizeDrawArgs", MerkabaGpuStage.FrameCompile);
            foreach (int kernel in new[]
                     {
                         _resetKernel, _queryKernel, _prepareKernel,
                         _compileKernel, _finalizeKernel
                     })
            {
                _grid.BindWorldBuffers(frameCompilerCompute, kernel);
                frameCompilerCompute.SetBuffer(kernel, VisibleTilesId,
                    _grid.M8VisibleTiles);
                frameCompilerCompute.SetBuffer(kernel, "_M8VisibleTilesRead",
                    _grid.M8VisibleTiles);
                frameCompilerCompute.SetBuffer(kernel, VisiblePrimitivesId,
                    _grid.M8VisiblePrimitives);
                frameCompilerCompute.SetBuffer(kernel, FrameDispatchArgsId,
                    _grid.M8FrameDispatchArgs);
                frameCompilerCompute.SetBuffer(kernel, DrawArgsId,
                    _grid.M8DrawArgs);
            }
            _material = new Material(renderShader)
            {
                name = "Merkaba M8 Readout"
            };
            _material.SetBuffer(VisiblePrimitivesId, _grid.M8VisiblePrimitives);
            ApplyOpacityState();
            _initialized = true;
            return true;
        }

        private void LateUpdate()
        {
            if (_gpuSubmissionSuspended) return;
            Camera camera = Camera.main;
            if (camera == null || !Initialize())
            {
                MerkabaGpuTimestamps.CloseIncompleteFrame();
                return;
            }

            ConfigureFrame(camera);
            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba M8 frame compiler");
            bool submitted = false;
            try
            {
                if (MerkabaGpuTimestamps.IsRecording)
                    _grid.RecordHashBenchmark(command);
                int querySide = Mathf.CeilToInt((renderDistance +
                    MerkabaSpatial.BlockWorldSize) /
                    MerkabaSpatial.BlockWorldSize) * 2 + 3;
                command.DispatchComputeProfiled(frameCompilerCompute,
                    _resetKernel, 1, 1, 1);
                command.DispatchComputeProfiled(frameCompilerCompute,
                    _queryKernel, querySide * querySide * querySide, 1, 1);
                command.DispatchComputeProfiled(frameCompilerCompute,
                    _prepareKernel, 1, 1, 1);
                command.DispatchComputeProfiled(frameCompilerCompute,
                    _compileKernel, _grid.M8FrameDispatchArgs);
                command.DispatchComputeProfiled(frameCompilerCompute,
                    _finalizeKernel, 1, 1, 1);
                Graphics.ExecuteCommandBuffer(command);
                submitted = true;
            }
            finally
            {
                if (!submitted)
                    MerkabaGpuTimestamps.CancelUnsubmittedFrame();
                CommandBufferPool.Release(command);
            }
            _material.SetMatrix(GridToWorldId, _grid.GridToWorldMatrix);
            MerkabaGpuTimestamps.CaptureM8Metrics(_grid);
            RequestStatusIfDue();
        }

        internal void RecordRenderPass(RasterCommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!_gpuSubmissionSuspended && _initialized && _material != null &&
                scanOpacity > 0.001f)
                command.DrawProceduralIndirectProfiled(Matrix4x4.identity,
                    _material, 0, MeshTopology.Triangles, _grid.M8DrawArgs, 0);
            MerkabaGpuTimestamps.RecordProfileEnd(command);
            MerkabaGpuTimestamps.CompleteFrameSubmission(true);
        }

        private void ConfigureFrame(Camera camera)
        {
            Matrix4x4 leftView;
            Matrix4x4 rightView;
            Matrix4x4 leftProjection;
            Matrix4x4 rightProjection;
            if (camera.stereoEnabled)
            {
                leftView = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
                rightView = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
                leftProjection = camera.GetStereoProjectionMatrix(
                    Camera.StereoscopicEye.Left);
                rightProjection = camera.GetStereoProjectionMatrix(
                    Camera.StereoscopicEye.Right);
            }
            else
            {
                leftView = rightView = camera.worldToCameraMatrix;
                leftProjection = rightProjection = camera.projectionMatrix;
            }
            WritePlanes(leftProjection * leftView, 0);
            WritePlanes(rightProjection * rightView, 6);
            Matrix4x4 worldToGrid = _grid.GridToWorldMatrix.inverse;
            _eyePositions[0] = worldToGrid.MultiplyPoint3x4(
                leftView.inverse.GetColumn(3));
            _eyePositions[1] = worldToGrid.MultiplyPoint3x4(
                rightView.inverse.GetColumn(3));
            Vector3 cameraGridMeters = worldToGrid.MultiplyPoint3x4(
                camera.transform.position);
            var global = new Unity.Mathematics.int3(
                Mathf.FloorToInt(cameraGridMeters.x / MerkabaConstants.LatticeStep),
                Mathf.FloorToInt(cameraGridMeters.y / MerkabaConstants.LatticeStep),
                Mathf.FloorToInt(cameraGridMeters.z / MerkabaConstants.LatticeStep));
            Unity.Mathematics.int3 centerBlock = MerkabaSpatial.Encode(global).BlockCoord;
            float warmDistance = renderDistance + MerkabaSpatial.BlockWorldSize;
            int radius = Mathf.CeilToInt(warmDistance /
                MerkabaSpatial.BlockWorldSize) + 1;
            int side = radius * 2 + 1;

            frameCompilerCompute.SetMatrix(GridToWorldId,
                _grid.GridToWorldMatrix);
            frameCompilerCompute.SetMatrix(WorldToGridId, worldToGrid);
            frameCompilerCompute.SetVectorArray("_M8DrawPlanes", _drawPlanes);
            frameCompilerCompute.SetVectorArray("_M8EyeGridPositions", _eyePositions);
            frameCompilerCompute.SetFloat("_M8GridWindingSign",
                _grid.GridToWorldMatrix.determinant < 0f ? -1f : 1f);
            frameCompilerCompute.SetVector("_M8CameraWorld",
                camera.transform.position);
            frameCompilerCompute.SetFloat("_M8RenderDistance", renderDistance);
            frameCompilerCompute.SetFloat("_M8WarmDistance", warmDistance);
            frameCompilerCompute.SetInts("_M8QueryCenterBlock", centerBlock.x,
                centerBlock.y, centerBlock.z);
            frameCompilerCompute.SetInt("_M8QueryBlockRadius", radius);
            frameCompilerCompute.SetInt("_M8QueryBlockSide", side);
        }

        private void WritePlanes(Matrix4x4 matrix, int offset)
        {
            GeometryUtility.CalculateFrustumPlanes(matrix, _frustumScratch);
            for (int i = 0; i < 6; i++)
                _drawPlanes[offset + i] = new Vector4(
                    _frustumScratch[i].normal.x, _frustumScratch[i].normal.y,
                    _frustumScratch[i].normal.z, _frustumScratch[i].distance);
        }

        private void ApplyOpacityState()
        {
            if (_material == null) return;
            bool opaque = scanOpacity >= 0.999f;
            _material.SetFloat(ScanOpacityId, scanOpacity);
            _material.SetInt(SrcBlendId, (int)(opaque
                ? BlendMode.One : BlendMode.SrcAlpha));
            _material.SetInt(DstBlendId, (int)(opaque
                ? BlendMode.Zero : BlendMode.OneMinusSrcAlpha));
            _material.SetInt(ZWriteId, 1);
            _material.renderQueue = opaque
                ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent;
        }

        private void RequestStatusIfDue()
        {
            if (_statusReadbackPending || Time.unscaledTime < _nextStatusReadback)
                return;
            _statusReadbackPending = true;
            _nextStatusReadback = Time.unscaledTime + 1f;
            AsyncGPUReadback.Request(_grid.M8Counters, request =>
            {
                _statusReadbackPending = false;
                if (request.hasError) return;
                var counters = request.GetData<uint>();
                VisibleTileCount = ToInt(counters[21]);
                VisiblePrimitiveCount = ToInt(counters[22]);
                LateDrawColdMisses = ToInt(counters[24]);
                VisibleChunkCount = ToInt(counters[28]);
                VisibleSurfaceKernelCount = ToInt(counters[29]);
                RenderPrimitiveOverflow = counters[23] != 0u;
            });
        }

        private static int ToInt(uint value) =>
            value > int.MaxValue ? int.MaxValue : (int)value;
    }
}
