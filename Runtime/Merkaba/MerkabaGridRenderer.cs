using System;
using System.Threading.Tasks;
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
        [SerializeField, Range(2f, 24f)] private float renderDistance = 12f;
        [SerializeField, Range(5f, 30f)] private float readoutBuildHz = 15f;
        [SerializeField, Range(0f, 4f)]
        private float readoutTranslationGuard = 1f;
        [SerializeField, Range(0f, 1f)] private float scanOpacity = 1f;

        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;
        private readonly Material[] _materials = new Material[2];
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
        private bool _buildInFlight;
        private int _frontReadout;
        private uint _sourceGeneration = 1u;
        private uint _submissionRevision;
        private uint _publishedRevision;
        private uint _lifecycleGeneration = 1u;
        private ReadoutBuildTicket _pendingBuild;
        private MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob
            _nativeReadoutJob;
        private bool _nativeReadoutGpuComplete;
        private double _nativeReadoutSubmittedAt;
        private double _nativeReadoutGpuCompleteAt;
        private bool _buildBlocked;
        private bool _blockedOnResidency;
        private uint _blockedSourceGeneration;
        private uint _blockedResidencyEpoch;
        private Vector3 _blockedGridPosition;
        private Matrix4x4 _blockedGridToWorld;
        private bool _hasPublishedCoverage;
        private bool _awaitingResidencyChange;
        private uint _readoutRevision;
        private uint _buildResidencyEpoch;
        private Vector3 _publishedGridPosition;
        private Matrix4x4 _publishedGridToWorld;
        private FineBrushDescriptor _finePreviewDescriptor;
        private Color _finePreviewColor;

        private readonly struct ReadoutBuildTicket
        {
            internal readonly int Slot;
            internal readonly uint Revision;
            internal readonly uint LifecycleGeneration;
            internal readonly uint SourceGeneration;
            internal readonly uint ResidencyEpoch;
            internal readonly Vector3 GridPosition;
            internal readonly Matrix4x4 GridToWorld;

            internal ReadoutBuildTicket(int slot, uint revision,
                uint lifecycleGeneration, uint sourceGeneration,
                uint residencyEpoch, Vector3 gridPosition,
                Matrix4x4 gridToWorld)
            {
                Slot = slot;
                Revision = revision;
                LifecycleGeneration = lifecycleGeneration;
                SourceGeneration = sourceGeneration;
                ResidencyEpoch = residencyEpoch;
                GridPosition = gridPosition;
                GridToWorld = gridToWorld;
            }
        }

        public int VisiblePrimitiveCount { get; private set; }
        public int VisibleSurfaceKernelCount { get; private set; }
        public int VisibleChunkCount { get; private set; }
        public int VisibleTileCount { get; private set; }
        public int LateDrawColdMisses { get; private set; }
        public bool RenderPrimitiveOverflow { get; private set; }
        internal bool HasReadoutBuildInFlight => _buildInFlight;
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
        private static readonly int FineEyeOriginId =
            Shader.PropertyToID("_FineEyeOrigin");
        private static readonly int FineBrushAxisId =
            Shader.PropertyToID("_FineBrushAxis");
        private static readonly int FineBrushParamsId =
            Shader.PropertyToID("_FineBrushParams");
        private static readonly int FinePreviewColorId =
            Shader.PropertyToID("_FinePreviewColor");

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _integrator = GetComponent<MerkabaIntegrator>();
            if (_grid != null) _grid.Cleared += MarkCanonicalReadoutDirty;
        }

        private void OnEnable()
        {
            if (!_gpuSubmissionSuspended) _active = this;
        }

        private void OnDisable()
        {
            if (_active == this) _active = null;
        }

        private void OnDestroy()
        {
            if (_active == this) _active = null;
            if (_grid != null) _grid.Cleared -= MarkCanonicalReadoutDirty;
            InvalidatePublicationCallbacks();
        }

        internal void MarkCanonicalReadoutDirty()
        {
            _canonicalDirty = true;
            unchecked
            {
                _sourceGeneration++;
                if (_sourceGeneration == 0u) _sourceGeneration = 1u;
            }
        }

        internal void SetFineSurfacePreview(FineBrushDescriptor descriptor,
            Color color)
        {
            _finePreviewDescriptor = descriptor;
            _finePreviewColor = color;
            ApplyFinePreviewState();
        }

        internal void SuspendGpuSubmission()
        {
            _gpuSubmissionSuspended = true;
            if (_active == this) _active = null;
        }

        internal void ResumeGpuSubmission()
        {
            _gpuSubmissionSuspended = false;
            if (isActiveAndEnabled) _active = this;
        }

        internal async Task FinishCurrentReadoutAsync()
        {
            while (_buildInFlight || _nativeReadoutJob != null)
            {
                PollNativeReadoutBuild();
                await Task.Yield();
            }
        }

        internal void ReleaseOwnedResourcesAfterGpuRetirement()
        {
            InvalidatePublicationCallbacks();
            for (int slot = 0; slot < 2; slot++)
            {
                if (_materials[slot] != null) Destroy(_materials[slot]);
                _materials[slot] = null;
            }
            _initialized = false;
            _statusReadbackPending = false;
            _canonicalDirty = true;
            _buildInFlight = false;
            _frontReadout = 0;
            _submissionRevision = 0u;
            _publishedRevision = 0u;
            _pendingBuild = default;
            _buildBlocked = false;
            _blockedOnResidency = false;
            _hasPublishedCoverage = false;
            _awaitingResidencyChange = false;
        }

        private void InvalidatePublicationCallbacks()
        {
            unchecked
            {
                _lifecycleGeneration++;
                if (_lifecycleGeneration == 0u) _lifecycleGeneration = 1u;
            }
            _buildInFlight = false;
            _statusReadbackPending = false;
        }

        internal Action CaptureOwnedGpuResourceRelease()
        {
            Material[] captured = { _materials[0], _materials[1] };
            bool released = false;
            return () =>
            {
                if (released) return;
                released = true;
                if (this != null)
                    ReleaseOwnedResourcesAfterGpuRetirement();
                else
                    foreach (Material material in captured)
                        if (material != null)
                            UnityEngine.Object.Destroy(material);
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
                readoutCompute.SetBuffer(kernel, FrameDispatchArgsId,
                    _grid.M8FrameDispatchArgs);
            }
            for (int slot = 0; slot < 2; slot++)
            {
                _materials[slot] = new Material(renderShader)
                {
                    name = $"Merkaba M8 Readout {slot}"
                };
                _materials[slot].SetBuffer(ReadoutVertices0Id,
                    _grid.GetM8ReadoutVertices0(slot));
                _materials[slot].SetBuffer(ReadoutVertices1Id,
                    _grid.GetM8ReadoutVertices1(slot));
            }
            ApplyOpacityState();
            ApplyFinePreviewState();
            _initialized = true;
            return true;
        }

        private void LateUpdate()
        {
            if (_gpuSubmissionSuspended || _grid == null ||
                _grid.GpuSubmissionSuspended) return;
            Camera camera = Camera.main;
            if (camera == null || !Initialize())
                return;

            PollNativeReadoutBuild();

            for (int slot = 0; slot < 2; slot++)
                _materials[slot].SetMatrix(GridToWorldId,
                    _grid.GridToWorldMatrix);
            Vector3 gridPosition = GetCoveragePosition(camera);
            bool coverageDirty = !_hasPublishedCoverage ||
                _publishedGridToWorld != _grid.GridToWorldMatrix ||
                Vector3.Distance(gridPosition, _publishedGridPosition) >
                    readoutTranslationGuard;
            bool residencyChanged = _awaitingResidencyChange &&
                _grid.ResidencyEpoch != _buildResidencyEpoch;
            bool scanQueueBusy = _buildInFlight ||
                MerkabaNativeVulkanExecutor.HasJobInFlight ||
                (_integrator != null &&
                 (_integrator.HasAttemptInFlight ||
                  _integrator.HasFineEraseAttemptInFlight));
            bool buildRequested = _canonicalDirty || coverageDirty || residencyChanged;
            if (_buildBlocked && !HasBuildReasonChanged(gridPosition))
                buildRequested = false;
            if (!scanQueueBusy && buildRequested &&
                Time.unscaledTime >= _nextReadoutBuild)
                SubmitReadoutBuild(camera, gridPosition);

            RequestStatusIfDue();
        }

        private void SubmitReadoutBuild(Camera camera, Vector3 gridPosition)
        {
            if (_buildInFlight) return;
            int backSlot = 1 - _frontReadout;
            uint revision = NextNonZero(ref _submissionRevision);
            var ticket = new ReadoutBuildTicket(backSlot, revision,
                _lifecycleGeneration, _sourceGeneration,
                _grid.ResidencyEpoch, gridPosition,
                _grid.GridToWorldMatrix);
#if !UNITY_EDITOR && UNITY_ANDROID
            SubmitNativeReadoutBuild(camera, ticket);
            return;
#else
            int querySide = ConfigureReadout(camera);
            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba M8 readout build");
            bool submitted = false;
            bool timedSubmission = false;
            _buildInFlight = true;
            _pendingBuild = ticket;
            try
            {
                timedSubmission = MerkabaGpuTimestamps.TryAcquire(
                    CaptureOwner.ReadoutBuild,
                    _readoutRevision == 0u ? 1u : _readoutRevision, command);
                command.SetComputeBufferParam(readoutCompute, _emitKernel,
                    ReadoutVertices0Id,
                    _grid.GetM8ReadoutVertices0(backSlot));
                command.SetComputeBufferParam(readoutCompute, _emitKernel,
                    ReadoutVertices1Id,
                    _grid.GetM8ReadoutVertices1(backSlot));
                command.SetComputeBufferParam(readoutCompute, _finalizeKernel,
                    DrawArgsId, _grid.GetM8DrawArgs(backSlot));
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
                MerkabaGpuTimestamps.End(CaptureOwner.ReadoutBuild, command,
                    timedSubmission);
                Graphics.ExecuteCommandBuffer(command);
                submitted = true;
                if (timedSubmission)
                    MerkabaGpuTimestamps.CaptureM8Metrics(_grid);
            }
            finally
            {
                MerkabaGpuTimestamps.Complete(CaptureOwner.ReadoutBuild,
                    timedSubmission, submitted);
                CommandBufferPool.Release(command);
            }
            if (!submitted)
            {
                _buildInFlight = false;
                _pendingBuild = default;
                _canonicalDirty = true;
                return;
            }
            unchecked
            {
                _readoutRevision++;
                if (_readoutRevision == 0u) _readoutRevision = 1u;
            }
            if (_sourceGeneration == ticket.SourceGeneration)
                _canonicalDirty = false;
            _nextReadoutBuild = Time.unscaledTime +
                1f / Mathf.Max(1f, readoutBuildHz);
            try
            {
                AsyncGPUReadback.Request(_grid.M8Counters, sizeof(uint),
                    MerkabaGrid.CounterReadoutBuildStatus * sizeof(uint),
                    request => CompleteReadoutBuild(ticket, request));
            }
            catch (Exception exception)
            {
                _buildInFlight = false;
                _pendingBuild = default;
                _canonicalDirty = true;
                Logger.Warning($"Merkaba readout completion request failed: " +
                    exception.Message);
            }
#endif
        }

        private void PollNativeReadoutBuild()
        {
            if (_nativeReadoutJob == null) return;
            if (_nativeReadoutGpuComplete) return;
            if (!_nativeReadoutJob.Poll(out string error)) return;
            ReadoutBuildTicket ticket = _pendingBuild;
            bool succeeded = string.IsNullOrEmpty(error);
            if (!succeeded)
            {
                _nativeReadoutJob.Dispose();
                _nativeReadoutJob = null;
                _nativeReadoutGpuComplete = false;
                _buildInFlight = false;
                _pendingBuild = default;
                _canonicalDirty = true;
                Logger.Error(error);
                return;
            }
            _nativeReadoutGpuComplete = true;
            _nativeReadoutGpuCompleteAt = Time.realtimeSinceStartupAsDouble;
            MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob nativeJob =
                _nativeReadoutJob;
            try
            {
                AsyncGPUReadback.Request(_grid.M8Counters, sizeof(uint),
                    MerkabaGrid.CounterReadoutBuildStatus * sizeof(uint),
                    request => CompleteNativeReadoutBuild(ticket, nativeJob,
                        request));
            }
            catch (Exception exception)
            {
                nativeJob.Dispose();
                if (ReferenceEquals(_nativeReadoutJob, nativeJob))
                    _nativeReadoutJob = null;
                _nativeReadoutGpuComplete = false;
                _buildInFlight = false;
                _pendingBuild = default;
                _canonicalDirty = true;
                Logger.Warning("Merkaba readout completion request failed: " +
                    exception.Message);
            }
        }

#if !UNITY_EDITOR && UNITY_ANDROID
        private void SubmitNativeReadoutBuild(Camera camera,
            ReadoutBuildTicket ticket)
        {
            if (MerkabaNativeVulkanExecutor.HasJobInFlight) return;
            MerkabaNativeUniformTable uniforms =
                BuildNativeReadoutUniforms(camera, out int queryGroups);
            var resources = new IntPtr[
                MerkabaNativeVulkanExecutor.ResourceCount];
            _grid.FillNativeExecutorWorldResources(resources);
            resources[(int)MerkabaNativeVulkanExecutor.Resource.ReadoutVertices0] =
                _grid.GetM8ReadoutVertices0(ticket.Slot).GetNativeBufferPtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource.ReadoutVertices1] =
                _grid.GetM8ReadoutVertices1(ticket.Slot).GetNativeBufferPtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource.DrawArgs] =
                _grid.GetM8DrawArgs(ticket.Slot).GetNativeBufferPtr();
            if (!MerkabaNativeVulkanExecutor.TryCreateJob(
                    MerkabaNativeVulkanExecutor.JobKind.Readout,
                    ticket.Revision, resources, uniforms, 0, 0, 0,
                    queryGroups, out var nativeJob))
                return;

            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba native readout submit");
            bool recorded = false;
            _buildInFlight = true;
            _pendingBuild = ticket;
            try
            {
                nativeJob.RecordPrepareAndSubmit(command);
                recorded = true;
                Graphics.ExecuteCommandBuffer(command);
                _nativeReadoutJob = nativeJob;
                _nativeReadoutGpuComplete = false;
                _nativeReadoutSubmittedAt = Time.realtimeSinceStartupAsDouble;
                _nativeReadoutGpuCompleteAt = 0.0;
                if (_sourceGeneration == ticket.SourceGeneration)
                    _canonicalDirty = false;
                _nextReadoutBuild = Time.unscaledTime +
                    1f / Mathf.Max(1f, readoutBuildHz);
            }
            catch (Exception exception)
            {
                if (recorded)
                {
                    _nativeReadoutJob = nativeJob;
                    _nativeReadoutGpuComplete = false;
                    _nativeReadoutSubmittedAt = Time.realtimeSinceStartupAsDouble;
                    _nativeReadoutGpuCompleteAt = 0.0;
                    Logger.Error("Merkaba native readout submission became " +
                        "uncertain; BACK remains quarantined: " +
                        exception.Message);
                    return;
                }
                nativeJob.CancelBeforeExecution();
                nativeJob.Dispose();
                _buildInFlight = false;
                _pendingBuild = default;
                _canonicalDirty = true;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private MerkabaNativeUniformTable BuildNativeReadoutUniforms(
            Camera camera, out int queryGroups)
        {
            Matrix4x4 worldToGrid = _grid.GridToWorldMatrix.inverse;
            Vector3 cameraGridMeters = worldToGrid.MultiplyPoint3x4(
                camera.transform.position);
            var global = new Unity.Mathematics.int3(
                Mathf.FloorToInt(cameraGridMeters.x /
                    MerkabaConstants.LatticeStep),
                Mathf.FloorToInt(cameraGridMeters.y /
                    MerkabaConstants.LatticeStep),
                Mathf.FloorToInt(cameraGridMeters.z /
                    MerkabaConstants.LatticeStep));
            Unity.Mathematics.int3 centerBlock =
                MerkabaSpatial.Encode(global).BlockCoord;
            float coverageDistance = renderDistance + readoutTranslationGuard;
            float warmDistance = coverageDistance +
                MerkabaSpatial.BlockWorldSize;
            int radius = Mathf.CeilToInt(warmDistance /
                MerkabaSpatial.BlockWorldSize) + 1;
            int side = radius * 2 + 1;
            queryGroups = checked(side * side * side);
            MerkabaReadoutCoverage.WriteGridMetric(_grid.GridToWorldMatrix,
                out Vector3 metricDiagonal, out Vector3 metricCross);
            var values = new MerkabaNativeUniformTable();
            values.Vector3("_M8CameraGridMeters", cameraGridMeters);
            values.Vector3("_M8GridMetricDiagonal", metricDiagonal);
            values.Vector3("_M8GridMetricCross", metricCross);
            values.Float("_M8RenderDistance", coverageDistance);
            values.Float("_M8WarmDistance", warmDistance);
            values.Float("_M8DependencyDistance", coverageDistance);
            values.Int3("_M8QueryCenterBlock", centerBlock.x,
                centerBlock.y, centerBlock.z);
            values.Int("_M8QueryBlockRadius", radius);
            values.Int("_M8QueryBlockSide", side);
            return values;
        }
#endif

        private void CompleteNativeReadoutBuild(ReadoutBuildTicket ticket,
            MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob nativeJob,
            AsyncGPUReadbackRequest request)
        {
            nativeJob?.Dispose();
            if (ReferenceEquals(_nativeReadoutJob, nativeJob))
                _nativeReadoutJob = null;
            _nativeReadoutGpuComplete = false;
            double retiredAt = Time.realtimeSinceStartupAsDouble;
            uint status = request.hasError ? uint.MaxValue :
                request.GetData<uint>()[0];
            Logger.Info("Merkaba native readout publication " +
                $"revision={ticket.Revision} slot={ticket.Slot} " +
                $"totalMs={(retiredAt - _nativeReadoutSubmittedAt) * 1000.0:F3} " +
                $"completionReadbackMs={(retiredAt - _nativeReadoutGpuCompleteAt) * 1000.0:F3} " +
                $"status={status}");
            CompleteReadoutBuild(ticket, request);
        }

        private void CompleteReadoutBuild(ReadoutBuildTicket ticket,
            AsyncGPUReadbackRequest request)
        {
            if (this == null || ticket.LifecycleGeneration !=
                _lifecycleGeneration)
                return;
            if (!_buildInFlight || ticket.Revision != _pendingBuild.Revision ||
                ticket.Slot != _pendingBuild.Slot)
                return;

            _buildInFlight = false;
            _pendingBuild = default;
            if (request.hasError)
            {
                _canonicalDirty = true;
                return;
            }

            uint status = request.GetData<uint>()[0];
            if (status == 3u)
            {
                _frontReadout = ticket.Slot;
                _publishedRevision = ticket.Revision;
                _hasPublishedCoverage = true;
                _publishedGridPosition = ticket.GridPosition;
                _publishedGridToWorld = ticket.GridToWorld;
                _awaitingResidencyChange = false;
                _buildBlocked = false;
                _blockedOnResidency = false;
                if (_sourceGeneration != ticket.SourceGeneration)
                    _canonicalDirty = true;
                return;
            }

            _buildBlocked = true;
            _blockedOnResidency = status == 1u;
            _blockedSourceGeneration = ticket.SourceGeneration;
            _blockedResidencyEpoch = ticket.ResidencyEpoch;
            _blockedGridPosition = ticket.GridPosition;
            _blockedGridToWorld = ticket.GridToWorld;
            _awaitingResidencyChange = _blockedOnResidency;
            _buildResidencyEpoch = ticket.ResidencyEpoch;
            if (_sourceGeneration != ticket.SourceGeneration)
                _canonicalDirty = true;
        }

        private bool HasBuildReasonChanged(Vector3 gridPosition)
        {
            if (_sourceGeneration != _blockedSourceGeneration ||
                _grid.GridToWorldMatrix != _blockedGridToWorld ||
                Vector3.Distance(gridPosition, _blockedGridPosition) >
                    readoutTranslationGuard)
                return true;
            return _blockedOnResidency &&
                _grid.ResidencyEpoch != _blockedResidencyEpoch;
        }

        internal void RecordRenderPass(RasterCommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (_gpuSubmissionSuspended || _grid == null ||
                _grid.GpuSubmissionSuspended)
                return;
            int front = _frontReadout;
            Material material = _materials[front];
            ComputeBuffer drawArgs = _grid.GetM8DrawArgs(front);
            bool canDraw = _initialized && material != null &&
                scanOpacity > 0.001f;
            bool timedSubmission = canDraw && MerkabaGpuTimestamps.TryAcquire(
                CaptureOwner.Draw,
                _readoutRevision == 0u ? 1u : _readoutRevision, command);
            if (canDraw)
                command.DrawProceduralIndirectProfiled(Matrix4x4.identity,
                    material, 0, MeshTopology.Triangles, drawArgs, 0);
            MerkabaGpuTimestamps.End(CaptureOwner.Draw, command,
                timedSubmission);
            MerkabaGpuTimestamps.Complete(CaptureOwner.Draw, timedSubmission,
                true);
        }

        private int ConfigureReadout(Camera camera)
        {
            Matrix4x4 worldToGrid = _grid.GridToWorldMatrix.inverse;
            Vector3 cameraGridMeters = worldToGrid.MultiplyPoint3x4(
                camera.transform.position);
            var global = new Unity.Mathematics.int3(
                Mathf.FloorToInt(cameraGridMeters.x / MerkabaConstants.LatticeStep),
                Mathf.FloorToInt(cameraGridMeters.y / MerkabaConstants.LatticeStep),
                Mathf.FloorToInt(cameraGridMeters.z / MerkabaConstants.LatticeStep));
            Unity.Mathematics.int3 centerBlock = MerkabaSpatial.Encode(global).BlockCoord;
            float coverageDistance = renderDistance + readoutTranslationGuard;
            float warmDistance = coverageDistance +
                MerkabaSpatial.BlockWorldSize;
            int radius = Mathf.CeilToInt(warmDistance /
                MerkabaSpatial.BlockWorldSize) + 1;
            int side = radius * 2 + 1;

            readoutCompute.SetVector("_M8CameraGridMeters",
                cameraGridMeters);
            MerkabaReadoutCoverage.WriteGridMetric(
                _grid.GridToWorldMatrix, out Vector3 metricDiagonal,
                out Vector3 metricCross);
            readoutCompute.SetVector("_M8GridMetricDiagonal",
                metricDiagonal);
            readoutCompute.SetVector("_M8GridMetricCross", metricCross);
            readoutCompute.SetFloat("_M8RenderDistance", coverageDistance);
            readoutCompute.SetFloat("_M8WarmDistance", warmDistance);
            readoutCompute.SetFloat("_M8DependencyDistance",
                coverageDistance);
            readoutCompute.SetInts("_M8QueryCenterBlock", centerBlock.x,
                centerBlock.y, centerBlock.z);
            readoutCompute.SetInt("_M8QueryBlockRadius", radius);
            readoutCompute.SetInt("_M8QueryBlockSide", side);
            return side;
        }

        private Vector3 GetCoveragePosition(Camera camera)
        {
            Matrix4x4 worldToGrid = _grid.GridToWorldMatrix.inverse;
            return worldToGrid.MultiplyPoint3x4(
                camera.transform.position);
        }

        private void ApplyOpacityState()
        {
            bool opaque = scanOpacity >= 0.999f;
            for (int slot = 0; slot < 2; slot++)
            {
                Material material = _materials[slot];
                if (material == null) continue;
                material.SetFloat(ScanOpacityId, scanOpacity);
                material.SetInt(SrcBlendId, (int)BlendMode.One);
                material.SetInt(DstBlendId,
                    (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt(ZWriteId, 1);
                material.renderQueue = opaque
                    ? (int)RenderQueue.Geometry :
                    (int)RenderQueue.Transparent;
            }
        }

        private void ApplyFinePreviewState()
        {
            bool active = _finePreviewDescriptor.IsActive;
            Color tint = _finePreviewColor;
            tint.a = 0.5f;
            Vector4 parameters = active
                ? new Vector4(1f,
                    _finePreviewDescriptor.CosHalfAngleSquared,
                    _finePreviewDescriptor.ToolDepthSquared, 0f)
                : Vector4.zero;
            for (int slot = 0; slot < 2; slot++)
            {
                Material material = _materials[slot];
                if (material == null) continue;
                material.SetVector(FineEyeOriginId,
                    _finePreviewDescriptor.EyeOrigin);
                material.SetVector(FineBrushAxisId,
                    _finePreviewDescriptor.Axis);
                material.SetVector(FineBrushParamsId, parameters);
                material.SetColor(FinePreviewColorId, tint);
            }
        }

        private void RequestStatusIfDue()
        {
            if (_grid == null || _grid.GpuSubmissionSuspended ||
                MerkabaNativeVulkanExecutor.HasJobInFlight ||
                _statusReadbackPending || Time.unscaledTime < _nextStatusReadback)
                return;
            _statusReadbackPending = true;
            _nextStatusReadback = Time.unscaledTime + 1f;
            uint lifecycleGeneration = _lifecycleGeneration;
            AsyncGPUReadback.Request(_grid.M8Counters, request =>
            {
                if (this == null || lifecycleGeneration !=
                    _lifecycleGeneration)
                    return;
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

        private static uint NextNonZero(ref uint value)
        {
            unchecked
            {
                value++;
                if (value == 0u) value = 1u;
                return value;
            }
        }

        private static int ToInt(uint value) =>
            value > int.MaxValue ? int.MaxValue : (int)value;
    }
}
