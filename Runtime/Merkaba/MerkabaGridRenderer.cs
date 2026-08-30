using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Genesis.RoomScan
{
    /// <summary>Disposable GPU readout rebuilt from M8 and drawn once per XR frame.</summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaGridRenderer : MonoBehaviour
    {
        private static MerkabaGridRenderer _active;

        [FormerlySerializedAs("frameCompilerCompute")]
        [SerializeField] private ComputeShader readoutCompute;
        [SerializeField] private Shader renderShader;
        [SerializeField, Range(2f, 12f)] private float renderDistance = 8f;
        [SerializeField, Range(5f, 30f)] private float readoutBuildHz = 15f;
        [SerializeField, Range(0f, 60f)]
        private float readoutAngularGuardDegrees = 30f;
        [SerializeField, Range(0f, 1f)]
        private float readoutTranslationGuard = 0.25f;
        [SerializeField, Range(0f, 1f)] private float scanOpacity = 1f;

        private MerkabaGrid _grid;
        private Material _material;
        private int _resetKernel;
        private int _queryKernel;
        private int _prepareKernel;
        private int _preflightKernel;
        private int _prepareEmitKernel;
        private int _emitKernel;
        private int _finalizeKernel;
        private bool _initialized;
        private volatile bool _gpuSubmissionSuspended;
        private bool _statusReadbackPending;
        private float _nextStatusReadback;
        private float _nextReadoutBuild;
        private bool _canonicalDirty = true;
        private bool _hasPublishedCoverage;
        private bool _awaitingResidencyChange;
        private uint _readoutRevision;
        private uint _buildResidencyEpoch;
        private Vector3 _publishedGridPosition;
        private Vector3 _publishedGridForward;
        private Vector3 _publishedGridUp;
        private readonly Vector4[] _drawPlanes = new Vector4[12];
        private readonly Vector4[] _dependencyPlanes = new Vector4[12];
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
        private static readonly int VisibleTilesId =
            Shader.PropertyToID("_M8VisibleTiles");
        private static readonly int ReadoutVertices0Id =
            Shader.PropertyToID("_M8ReadoutVertices0");
        private static readonly int ReadoutVertices1Id =
            Shader.PropertyToID("_M8ReadoutVertices1");
        private static readonly int FrameDispatchArgsId =
            Shader.PropertyToID("_M8FrameDispatchArgs");
        private static readonly int DrawArgsId = Shader.PropertyToID("_M8DrawArgs");
        private static readonly int ScanOpacityId = Shader.PropertyToID("_ScanOpacity");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            if (_grid != null) _grid.Cleared += MarkCanonicalReadoutDirty;
        }

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
            if (_grid != null) _grid.Cleared -= MarkCanonicalReadoutDirty;
        }

        internal void MarkCanonicalReadoutDirty() => _canonicalDirty = true;

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
            _canonicalDirty = true;
            _hasPublishedCoverage = false;
            _awaitingResidencyChange = false;
        }

        internal Action CaptureOwnedGpuResourceRelease()
        {
            Material captured = _material;
            bool released = false;
            return () =>
            {
                if (released) return;
                released = true;
                if (this != null)
                    ReleaseOwnedResourcesAfterGpuRetirement();
                else if (captured != null)
                    UnityEngine.Object.Destroy(captured);
            };
        }

        internal static bool TryGetActive(Camera camera,
            out MerkabaGridRenderer renderer)
        {
            renderer = _active;
            return renderer != null && renderer._initialized &&
                   !renderer._gpuSubmissionSuspended &&
                   !renderer._grid.GpuSubmissionSuspended &&
                   renderer.isActiveAndEnabled && camera == Camera.main;
        }

        private bool Initialize()
        {
            if (_initialized) return true;
            if (_grid == null || readoutCompute == null || renderShader == null)
            {
                Logger.Error("Merkaba readout assets are not wired.");
                enabled = false;
                return false;
            }
            _grid.EnsureGpuResources();
            _resetKernel = readoutCompute.FindProfiledKernel(
                "ResetReadoutBuild", MerkabaGpuStage.WorldQuery);
            _queryKernel = readoutCompute.FindProfiledKernel(
                "QueryM8Readout", MerkabaGpuStage.WorldQuery);
            _prepareKernel = readoutCompute.FindProfiledKernel(
                "PrepareReadoutBuild", MerkabaGpuStage.ReadoutBuild);
            _preflightKernel = readoutCompute.FindProfiledKernel(
                "PreflightReadout", MerkabaGpuStage.ReadoutBuild);
            _prepareEmitKernel = readoutCompute.FindProfiledKernel(
                "PrepareReadoutEmit", MerkabaGpuStage.ReadoutBuild);
            _emitKernel = readoutCompute.FindProfiledKernel(
                "EmitReadoutVertices", MerkabaGpuStage.ReadoutBuild);
            _finalizeKernel = readoutCompute.FindProfiledKernel(
                "FinalizeReadout", MerkabaGpuStage.ReadoutBuild);
            foreach (int kernel in new[]
                     {
                         _resetKernel, _queryKernel, _prepareKernel,
                         _preflightKernel, _prepareEmitKernel, _emitKernel,
                         _finalizeKernel
                     })
            {
                _grid.BindWorldBuffers(readoutCompute, kernel);
                readoutCompute.SetBuffer(kernel, VisibleTilesId,
                    _grid.M8VisibleTiles);
                readoutCompute.SetBuffer(kernel, "_M8VisibleTilesRead",
                    _grid.M8VisibleTiles);
                readoutCompute.SetBuffer(kernel, ReadoutVertices0Id,
                    _grid.M8ReadoutVertices0);
                readoutCompute.SetBuffer(kernel, ReadoutVertices1Id,
                    _grid.M8ReadoutVertices1);
                readoutCompute.SetBuffer(kernel, FrameDispatchArgsId,
                    _grid.M8FrameDispatchArgs);
                readoutCompute.SetBuffer(kernel, DrawArgsId,
                    _grid.M8DrawArgs);
            }
            _material = new Material(renderShader)
            {
                name = "Merkaba M8 Readout"
            };
            _material.SetBuffer(ReadoutVertices0Id,
                _grid.M8ReadoutVertices0);
            _material.SetBuffer(ReadoutVertices1Id,
                _grid.M8ReadoutVertices1);
            ApplyOpacityState();
            _initialized = true;
            return true;
        }

        private void LateUpdate()
        {
            if (_gpuSubmissionSuspended || _grid == null ||
                _grid.GpuSubmissionSuspended) return;
            Camera camera = Camera.main;
            if (camera == null || !Initialize())
            {
                MerkabaGpuTimestamps.CloseIncompleteFrame();
                return;
            }

            _material.SetMatrix(GridToWorldId, _grid.GridToWorldMatrix);
            MerkabaGpuTimestamps.TryBeginFrame(
                _readoutRevision == 0u ? 1u : _readoutRevision);
            GetCoveragePose(camera, out Vector3 gridPosition,
                out Vector3 gridForward, out Vector3 gridUp);
            bool coverageDirty = !_hasPublishedCoverage ||
                Vector3.Distance(gridPosition, _publishedGridPosition) >
                    readoutTranslationGuard * 0.5f ||
                Vector3.Angle(gridForward, _publishedGridForward) >
                    readoutAngularGuardDegrees * 0.5f ||
                Vector3.Angle(gridUp, _publishedGridUp) >
                    readoutAngularGuardDegrees * 0.5f;
            bool residencyChanged = _awaitingResidencyChange &&
                _grid.ResidencyEpoch != _buildResidencyEpoch;
            if ((_canonicalDirty || coverageDirty || residencyChanged) &&
                Time.unscaledTime >= _nextReadoutBuild)
                SubmitReadoutBuild(camera, gridPosition, gridForward, gridUp);

            MerkabaGpuTimestamps.CaptureM8Metrics(_grid);
            RequestStatusIfDue();
        }

        private void SubmitReadoutBuild(Camera camera, Vector3 gridPosition,
            Vector3 gridForward, Vector3 gridUp)
        {
            int querySide = ConfigureReadout(camera);
            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba M8 readout build");
            bool submitted = false;
            try
            {
                if (MerkabaGpuTimestamps.IsRecording)
                    _grid.RecordHashBenchmark(command);
                command.DispatchComputeProfiled(readoutCompute,
                    _resetKernel, 1, 1, 1);
                command.DispatchComputeProfiled(readoutCompute,
                    _queryKernel, querySide * querySide * querySide, 1, 1);
                command.DispatchComputeProfiled(readoutCompute,
                    _prepareKernel, 1, 1, 1);
                command.DispatchComputeProfiled(readoutCompute,
                    _preflightKernel, _grid.M8FrameDispatchArgs);
                command.DispatchComputeProfiled(readoutCompute,
                    _prepareEmitKernel, 1, 1, 1);
                command.DispatchComputeProfiled(readoutCompute,
                    _emitKernel, _grid.M8FrameDispatchArgs);
                command.DispatchComputeProfiled(readoutCompute,
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
            if (!submitted) return;
            unchecked
            {
                _readoutRevision++;
                if (_readoutRevision == 0u) _readoutRevision = 1u;
            }
            _canonicalDirty = false;
            _hasPublishedCoverage = true;
            _awaitingResidencyChange = true;
            _buildResidencyEpoch = _grid.ResidencyEpoch;
            _publishedGridPosition = gridPosition;
            _publishedGridForward = gridForward;
            _publishedGridUp = gridUp;
            _nextReadoutBuild = Time.unscaledTime +
                1f / Mathf.Max(1f, readoutBuildHz);
        }

        internal void RecordRenderPass(RasterCommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (_gpuSubmissionSuspended || _grid == null ||
                _grid.GpuSubmissionSuspended)
            {
                MerkabaGpuTimestamps.CancelUnsubmittedFrame();
                return;
            }
            if (_initialized && _material != null && scanOpacity > 0.001f)
                command.DrawProceduralIndirectProfiled(Matrix4x4.identity,
                    _material, 0, MeshTopology.Triangles, _grid.M8DrawArgs, 0);
            MerkabaGpuTimestamps.RecordProfileEnd(command);
            MerkabaGpuTimestamps.CompleteFrameSubmission(true);
        }

        private int ConfigureReadout(Camera camera)
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
            Matrix4x4 worldToGrid = _grid.GridToWorldMatrix.inverse;
            Matrix4x4 leftEyeToWorld = leftView.inverse;
            Matrix4x4 rightEyeToWorld = rightView.inverse;
            WriteExpandedPlanes(leftProjection * leftView, leftEyeToWorld, 0);
            WriteExpandedPlanes(rightProjection * rightView,
                rightEyeToWorld, 6);
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

            readoutCompute.SetVectorArray("_M8DrawPlanes", _drawPlanes);
            readoutCompute.SetVectorArray("_M8DependencyPlanes",
                _dependencyPlanes);
            readoutCompute.SetVector("_M8CameraGridMeters",
                cameraGridMeters);
            MerkabaReadoutCoverage.WriteGridMetric(
                _grid.GridToWorldMatrix, out Vector3 metricDiagonal,
                out Vector3 metricCross);
            readoutCompute.SetVector("_M8GridMetricDiagonal",
                metricDiagonal);
            readoutCompute.SetVector("_M8GridMetricCross", metricCross);
            readoutCompute.SetFloat("_M8RenderDistance", renderDistance);
            readoutCompute.SetFloat("_M8WarmDistance", warmDistance);
            readoutCompute.SetFloat("_M8DependencyDistance",
                renderDistance + MaxNeighbourWorldDistance(
                    _grid.GridToWorldMatrix));
            readoutCompute.SetInts("_M8QueryCenterBlock", centerBlock.x,
                centerBlock.y, centerBlock.z);
            readoutCompute.SetInt("_M8QueryBlockRadius", radius);
            readoutCompute.SetInt("_M8QueryBlockSide", side);
            return side;
        }

        private void WriteExpandedPlanes(Matrix4x4 viewProjection,
            Matrix4x4 eyeToWorld, int offset)
        {
            GeometryUtility.CalculateFrustumPlanes(viewProjection,
                _frustumScratch);
            Vector3 eye = (Vector3)eyeToWorld.GetColumn(3);
            Vector3 right = ((Vector3)eyeToWorld.GetColumn(0)).normalized;
            Vector3 up = ((Vector3)eyeToWorld.GetColumn(1)).normalized;
            Vector3 forward = -((Vector3)eyeToWorld.GetColumn(2)).normalized;
            Matrix4x4 gridToWorld = _grid.GridToWorldMatrix;
            Vector3 gridAxisX = gridToWorld.MultiplyVector(Vector3.right);
            Vector3 gridAxisY = gridToWorld.MultiplyVector(Vector3.up);
            Vector3 gridAxisZ = gridToWorld.MultiplyVector(Vector3.forward);
            for (int i = 0; i < 6; i++)
            {
                Vector3 normal = _frustumScratch[i].normal.normalized;
                float distance = _frustumScratch[i].distance;
                if (i < 4)
                {
                    Vector3 axis = i < 2 ? up : right;
                    normal = ExpandSideNormal(normal, axis, forward,
                        readoutAngularGuardDegrees);
                    distance = -Vector3.Dot(normal, eye);
                }
                distance += readoutTranslationGuard;
                float topologyHalo = MerkabaConstants.LatticeStep *
                    (Mathf.Abs(Vector3.Dot(normal, gridAxisX)) +
                     Mathf.Abs(Vector3.Dot(normal, gridAxisY)) +
                     Mathf.Abs(Vector3.Dot(normal, gridAxisZ)));
                _drawPlanes[offset + i] =
                    MerkabaReadoutCoverage.WorldToKernelPlane(
                        new Vector4(normal.x, normal.y, normal.z, distance),
                        gridToWorld);
                _dependencyPlanes[offset + i] =
                    MerkabaReadoutCoverage.WorldToKernelPlane(
                        new Vector4(normal.x, normal.y, normal.z,
                            distance + topologyHalo), gridToWorld);
            }
        }

        private static Vector3 ExpandSideNormal(Vector3 normal, Vector3 axis,
            Vector3 forward, float angle)
        {
            Vector3 positive = Quaternion.AngleAxis(angle, axis) * normal;
            Vector3 negative = Quaternion.AngleAxis(-angle, axis) * normal;
            float positiveFacing = Vector3.Dot(positive, forward);
            float negativeFacing = Vector3.Dot(negative, forward);
            if (positiveFacing >= 0f && negativeFacing >= 0f)
                return positiveFacing <= negativeFacing ? positive : negative;
            if (positiveFacing >= 0f) return positive;
            if (negativeFacing >= 0f) return negative;
            return normal;
        }

        private void GetCoveragePose(Camera camera, out Vector3 gridPosition,
            out Vector3 gridForward, out Vector3 gridUp)
        {
            Matrix4x4 worldToGrid = _grid.GridToWorldMatrix.inverse;
            gridPosition = worldToGrid.MultiplyPoint3x4(
                camera.transform.position);
            gridForward = worldToGrid.MultiplyVector(
                camera.transform.forward).normalized;
            gridUp = worldToGrid.MultiplyVector(camera.transform.up).normalized;
        }

        private static float MaxNeighbourWorldDistance(Matrix4x4 matrix)
        {
            float maximum = 0f;
            for (int z = -1; z <= 1; z += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int x = -1; x <= 1; x += 2)
            {
                Vector3 offset = new Vector3(x, y, z) *
                    MerkabaConstants.LatticeStep;
                maximum = Mathf.Max(maximum,
                    matrix.MultiplyVector(offset).magnitude);
            }
            return maximum;
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
            if (_grid == null || _grid.GpuSubmissionSuspended ||
                _statusReadbackPending || Time.unscaledTime < _nextStatusReadback)
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
                uint readoutStatus = counters[
                    MerkabaGrid.CounterReadoutBuildStatus];
                if (readoutStatus == 2u || readoutStatus == 3u ||
                    readoutStatus == 5u)
                    _awaitingResidencyChange = false;
            });
        }

        private static int ToInt(uint value) =>
            value > int.MaxValue ? int.MaxValue : (int)value;
    }
}
