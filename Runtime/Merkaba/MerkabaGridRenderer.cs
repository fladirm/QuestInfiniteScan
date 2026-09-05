using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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
        [SerializeField] private bool readoutDrawEnabled = true;
        [SerializeField] private bool meshReadoutEnabled;
        [SerializeField] private bool checkerReadoutEnabled;

        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;
        private DepthCapture _depthCapture;
        private readonly Material[] _materials = new Material[2];
        private int _resetKernel;
        private int _queryKernel;
        private int _prepareKernel;
        private int _buildKernel;
        private int _projectMeshKernel;
        private int _buildMeshKernel;
        private int _finalizeKernel;
        private int _visibilityKernel;
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
        private bool _hasPublishedCoverage;
        private bool _awaitingResidencyChange;
        private uint _readoutRevision;
        private uint _buildResidencyEpoch;
        private FineBrushDescriptor _finePreviewDescriptor;
        private Color _finePreviewColor;
        private bool _dynamicOcclusionEnabled = true;
        private TaskCompletionSource<bool> _loadedCoverageReady;
        private uint _loadedCoverageSourceGeneration;

        private readonly struct ReadoutBuildTicket
        {
            internal readonly int Slot;
            internal readonly uint Revision;
            internal readonly uint LifecycleGeneration;
            internal readonly uint SourceGeneration;
            internal readonly uint ResidencyEpoch;
            internal readonly Matrix4x4 GridToWorld;
            internal readonly bool MeshReadout;
            internal readonly DepthCapture.ReadoutDepthLease DepthLease;

            internal ReadoutBuildTicket(int slot, uint revision,
                uint lifecycleGeneration, uint sourceGeneration,
                uint residencyEpoch, Matrix4x4 gridToWorld, bool meshReadout,
                DepthCapture.ReadoutDepthLease depthLease)
            {
                Slot = slot;
                Revision = revision;
                LifecycleGeneration = lifecycleGeneration;
                SourceGeneration = sourceGeneration;
                ResidencyEpoch = residencyEpoch;
                GridToWorld = gridToWorld;
                MeshReadout = meshReadout;
                DepthLease = depthLease;
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
        public bool ReadoutDrawEnabled
        {
            get => readoutDrawEnabled;
            set => readoutDrawEnabled = value;
        }
        public bool MeshReadoutEnabled
        {
            get => meshReadoutEnabled;
            set
            {
                if (meshReadoutEnabled == value) return;
                meshReadoutEnabled = value;
                if (value && checkerReadoutEnabled)
                {
                    checkerReadoutEnabled = false;
                    ApplyCheckerReadoutState();
                }
                MarkCanonicalReadoutDirty();
                Logger.Info("Merkaba live readout mode: " +
                    (value ? "stereo depth mesh" : "canonical patches"));
            }
        }
        public bool CheckerReadoutEnabled
        {
            get => checkerReadoutEnabled;
            set
            {
                if (checkerReadoutEnabled == value) return;
                checkerReadoutEnabled = value;
                if (value && meshReadoutEnabled)
                {
                    meshReadoutEnabled = false;
                    MarkCanonicalReadoutDirty();
                }
                ApplyCheckerReadoutState();
                Logger.Info("Merkaba coverage checker: " +
                    (value ? "enabled" : "disabled"));
            }
        }

        internal void SetDynamicOcclusionEnabled(bool enabled)
        {
            _dynamicOcclusionEnabled = enabled;
            ApplyRasterFeatureState();
        }

        private static readonly int GridToWorldId =
            Shader.PropertyToID("_MerkabaGridToWorld");
        private static readonly int VisibleTilesId =
            Shader.PropertyToID("_M8VisibleTiles");
        private static readonly int ReadoutVerticesId =
            Shader.PropertyToID("_M8ReadoutVertices");
        private static readonly int ReadoutVerticesReadId =
            Shader.PropertyToID("_M8ReadoutVerticesRead");
        private static readonly int MeshEyeVertexOffsetId =
            Shader.PropertyToID("_M8MeshEyeVertexOffset");
        private static readonly int ReadoutIndicesId =
            Shader.PropertyToID("_M8ReadoutIndices");
        private static readonly int FrameDispatchArgsId =
            Shader.PropertyToID("_M8FrameDispatchArgs");
        private static readonly int DrawArgsId = Shader.PropertyToID("_M8DrawArgs");
        private static readonly int MeshEnabledId =
            Shader.PropertyToID("_M8MeshReadoutEnabled");
        private static readonly int MeshDepthId = Shader.PropertyToID("_SrcDepth");
        private static readonly int MeshDepthSizeId =
            Shader.PropertyToID("_M8MeshDepthSize");
        private static readonly int MeshGridToWorldId =
            Shader.PropertyToID("_MerkabaGridToWorld");
        private static readonly int MeshWorldToGridId =
            Shader.PropertyToID("_MerkabaWorldToGrid");
        private static readonly int MeshDepthProj0Id =
            Shader.PropertyToID("_M8MeshDepthProj0");
        private static readonly int MeshDepthProj1Id =
            Shader.PropertyToID("_M8MeshDepthProj1");
        private static readonly int MeshDepthProjInv0Id =
            Shader.PropertyToID("_M8MeshDepthProjInv0");
        private static readonly int MeshDepthProjInv1Id =
            Shader.PropertyToID("_M8MeshDepthProjInv1");
        private static readonly int MeshDepthView0Id =
            Shader.PropertyToID("_M8MeshDepthView0");
        private static readonly int MeshDepthView1Id =
            Shader.PropertyToID("_M8MeshDepthView1");
        private static readonly int MeshDepthViewInv0Id =
            Shader.PropertyToID("_M8MeshDepthViewInv0");
        private static readonly int MeshDepthViewInv1Id =
            Shader.PropertyToID("_M8MeshDepthViewInv1");
        private static readonly int ScanOpacityId = Shader.PropertyToID("_ScanOpacity");
        private static readonly int FineCursorPositionId =
            Shader.PropertyToID("_FineCursorPosition");
        private static readonly int FineBrushAxisId =
            Shader.PropertyToID("_FineBrushAxis");
        private static readonly int FineBrushParamsId =
            Shader.PropertyToID("_FineBrushParams");
        private static readonly int FinePreviewColorId =
            Shader.PropertyToID("_FinePreviewColor");
        private static readonly int CullGridPlanesId =
            Shader.PropertyToID("_M8CullGridPlanes");
        private static readonly uint[] ZeroDrawCount = { 0u };
        private readonly Plane[] _leftCullPlanes = new Plane[6];
        private readonly Plane[] _rightCullPlanes = new Plane[6];
        private readonly Vector4[] _gridCullPlanes = new Vector4[12];

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _integrator = GetComponent<MerkabaIntegrator>();
            _depthCapture = GetComponent<DepthCapture>();
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

        internal void BeginLoadedCoverageWarmup()
        {
            _loadedCoverageReady?.TrySetResult(true);
            _loadedCoverageReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            MarkCanonicalReadoutDirty();
            _loadedCoverageSourceGeneration = _sourceGeneration;
        }

        internal Task WaitForLoadedCoverageReadyAsync() =>
            _loadedCoverageReady?.Task ?? Task.CompletedTask;

        internal void CancelLoadedCoverageWarmup()
        {
            _loadedCoverageReady?.TrySetResult(true);
            _loadedCoverageReady = null;
            _loadedCoverageSourceGeneration = 0u;
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
            CancelLoadedCoverageWarmup();
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
                   renderer.readoutDrawEnabled &&
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
            _buildKernel = readoutCompute.FindProfiledKernel(
                "BuildReadoutVertices", MerkabaGpuStage.ReadoutBuild);
            _projectMeshKernel = readoutCompute.FindProfiledKernel(
                "ProjectReadoutMeshPins", MerkabaGpuStage.ReadoutBuild);
            _buildMeshKernel = readoutCompute.FindProfiledKernel(
                "BuildReadoutMesh", MerkabaGpuStage.ReadoutBuild);
            _finalizeKernel = readoutCompute.FindProfiledKernel(
                "FinalizeReadout", MerkabaGpuStage.ReadoutBuild);
            _visibilityKernel = readoutCompute.FindProfiledKernel(
                "CullReadoutVisibility", MerkabaGpuStage.MerkabaDraw);
            foreach (int kernel in new[]
                     {
                         _resetKernel, _queryKernel, _prepareKernel,
                         _buildKernel,
                         _projectMeshKernel, _buildMeshKernel, _finalizeKernel
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
                _materials[slot].SetBuffer(ReadoutVerticesId,
                    _grid.GetM8ReadoutVertices(slot));
                _materials[slot].SetInt(MeshEyeVertexOffsetId,
                    MerkabaGrid.ReadoutVertexCapacity / 2);
            }
            ApplyOpacityState();
            ApplyFinePreviewState();
            ApplyRasterFeatureState();
            ApplyCheckerReadoutState();
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
            bool coverageDirty = !_hasPublishedCoverage;
            bool residencyChanged = _awaitingResidencyChange &&
                _grid.ResidencyEpoch != _buildResidencyEpoch;
            bool scannerWork = _integrator != null &&
                (_integrator.HasPendingObservation ||
                 _integrator.HasAttemptInFlight ||
                 _integrator.HasPendingFineErase ||
                 _integrator.HasFineEraseAttemptInFlight);
            bool scanQueueBusy = _buildInFlight || scannerWork ||
                MerkabaNativeVulkanExecutor.HasJobInFlight ||
                _nativeReadoutJob != null;
            bool buildRequested = _canonicalDirty || coverageDirty || residencyChanged;
            if (_buildBlocked && !HasBuildReasonChanged())
                buildRequested = false;
            if (!scanQueueBusy && buildRequested &&
                Time.unscaledTime >= _nextReadoutBuild)
                SubmitReadoutBuild(camera);

            RequestStatusIfDue();
        }

        private void SubmitReadoutBuild(Camera camera)
        {
            if (_buildInFlight) return;
            DepthCapture.ReadoutDepthLease depthLease = default;
            if (meshReadoutEnabled && (_depthCapture == null ||
                !_depthCapture.TryAcquireReadoutDepth(out depthLease)))
                return;
            int backSlot = 1 - _frontReadout;
            uint revision = NextNonZero(ref _submissionRevision);
            var ticket = new ReadoutBuildTicket(backSlot, revision,
                _lifecycleGeneration, _sourceGeneration,
                _grid.ResidencyEpoch, _grid.GridToWorldMatrix,
                meshReadoutEnabled, depthLease);
            ApplyReadoutModeState(backSlot, ticket.MeshReadout);
#if !UNITY_EDITOR && UNITY_ANDROID
            SubmitNativeReadoutBuild(camera, ticket);
            return;
#else
            int querySide = ConfigureReadout(camera);
            int meshGroupsX = ticket.MeshReadout
                ? Mathf.CeilToInt(depthLease.Texture.width / 8f) : 1;
            int meshGroupsY = ticket.MeshReadout
                ? Mathf.CeilToInt(depthLease.Texture.height / 8f) : 1;
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
                command.SetComputeBufferParam(readoutCompute, _buildKernel,
                    ReadoutVerticesId,
                    _grid.GetM8ReadoutVertices(backSlot));
                command.SetComputeBufferParam(readoutCompute, _buildKernel,
                    ReadoutIndicesId, _grid.GetM8ReadoutIndices(backSlot));
                command.SetComputeBufferParam(readoutCompute, _finalizeKernel,
                    DrawArgsId, _grid.GetM8DrawArgs(backSlot));
                ConfigureMeshReadoutCommand(command, ticket);
                command.DispatchComputeProfiled(readoutCompute,
                    _resetKernel, ticket.MeshReadout ? 1 :
                        MerkabaGrid.ReadoutResetGroupCount, 1, 1);
                command.DispatchComputeProfiled(readoutCompute,
                    _queryKernel, querySide * querySide * querySide, 1, 1);
                command.DispatchComputeProfiled(readoutCompute,
                    _prepareKernel, meshGroupsX, meshGroupsY, 1);
                if (ticket.MeshReadout)
                {
                    command.DispatchComputeProfiled(readoutCompute,
                        _projectMeshKernel, _grid.M8FrameDispatchArgs);
                    command.DispatchComputeProfiled(readoutCompute,
                        _buildMeshKernel, meshGroupsX, meshGroupsY, 1);
                }
                else
                {
                    command.DispatchComputeProfiled(readoutCompute,
                        _buildKernel, _grid.M8FrameDispatchArgs);
                }
                command.DispatchComputeProfiled(readoutCompute,
                    _finalizeKernel, 1, 1, 1);
                MerkabaGpuTimestamps.End(CaptureOwner.ReadoutBuild, command,
                    timedSubmission);
                Graphics.ExecuteCommandBuffer(command);
                submitted = true;
                if (timedSubmission)
                    MerkabaGpuTimestamps.CaptureM8Metrics(_grid);
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba readout submission failed: " +
                    exception.Message);
            }
            finally
            {
                MerkabaGpuTimestamps.Complete(CaptureOwner.ReadoutBuild,
                    timedSubmission, submitted);
                CommandBufferPool.Release(command);
            }
            if (!submitted)
            {
                ReleaseDepthLease(ticket);
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
                ReleaseDepthLease(ticket);
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
                ReleaseDepthLease(ticket);
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
                ReleaseDepthLease(ticket);
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
            if (MerkabaNativeVulkanExecutor.HasJobInFlight)
            {
                ReleaseDepthLease(ticket);
                return;
            }
            MerkabaNativeUniformTable uniforms =
                BuildNativeReadoutUniforms(camera, ticket,
                    out int queryGroups, out int depthGroupsX,
                    out int depthGroupsY);
            var resources = new IntPtr[
                MerkabaNativeVulkanExecutor.ResourceCount];
            _grid.FillNativeExecutorWorldResources(resources);
            if (ticket.MeshReadout)
                resources[(int)MerkabaNativeVulkanExecutor.Resource.RawDepth] =
                    ticket.DepthLease.Texture.GetNativeTexturePtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource.ReadoutVertices] =
                _grid.GetM8ReadoutVertices(ticket.Slot).GetNativeBufferPtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource.ReadoutIndices] =
                _grid.GetM8ReadoutIndices(ticket.Slot).GetNativeBufferPtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource.DrawArgs] =
                _grid.GetM8DrawArgs(ticket.Slot).GetNativeBufferPtr();
            if (!MerkabaNativeVulkanExecutor.TryCreateJob(
                    ticket.MeshReadout
                        ? MerkabaNativeVulkanExecutor.JobKind.MeshReadout
                        : MerkabaNativeVulkanExecutor.JobKind.Readout,
                    ticket.Revision, resources, uniforms, depthGroupsX,
                    depthGroupsY, 0,
                    queryGroups, out var nativeJob))
            {
                ReleaseDepthLease(ticket);
                return;
            }

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
                ReleaseDepthLease(ticket);
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
            Camera camera, ReadoutBuildTicket ticket, out int queryGroups,
            out int depthGroupsX, out int depthGroupsY)
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
            values.UInt("_M8MeshReadoutEnabled",
                ticket.MeshReadout ? 1u : 0u);
            values.Matrix("_MerkabaGridToWorld", ticket.GridToWorld);
            values.Matrix("_MerkabaWorldToGrid", ticket.GridToWorld.inverse);
            if (ticket.MeshReadout)
            {
                RenderTexture depth = ticket.DepthLease.Texture;
                depthGroupsX = Mathf.CeilToInt(depth.width / 8f);
                depthGroupsY = Mathf.CeilToInt(depth.height / 8f);
                values.UInt2("_M8MeshDepthSize", depth.width, depth.height);
                AddMeshDepthUniforms(values, ticket.DepthLease);
            }
            else
            {
                depthGroupsX = 1;
                depthGroupsY = 1;
                values.UInt2("_M8MeshDepthSize", 1, 1);
            }
            return values;
        }
#endif

        private void ConfigureMeshReadoutCommand(CommandBuffer command,
            ReadoutBuildTicket ticket)
        {
            command.SetComputeIntParam(readoutCompute, MeshEnabledId,
                ticket.MeshReadout ? 1 : 0);
            command.SetComputeMatrixParam(readoutCompute, MeshGridToWorldId,
                ticket.GridToWorld);
            command.SetComputeMatrixParam(readoutCompute, MeshWorldToGridId,
                ticket.GridToWorld.inverse);
            if (!ticket.MeshReadout) return;
            BindMeshPublication(command, _prepareKernel, ticket.Slot);
            BindMeshPublication(command, _projectMeshKernel, ticket.Slot);
            BindMeshPublication(command, _buildMeshKernel, ticket.Slot);
            DepthCapture.ReadoutDepthLease lease = ticket.DepthLease;
            command.SetComputeTextureParam(readoutCompute,
                _projectMeshKernel, MeshDepthId, lease.Texture);
            command.SetComputeTextureParam(readoutCompute,
                _buildMeshKernel, MeshDepthId, lease.Texture);
            command.SetComputeIntParams(readoutCompute, MeshDepthSizeId,
                lease.Texture.width, lease.Texture.height);
            SetMeshDepthMatrices(command, lease);
        }

        private void BindMeshPublication(CommandBuffer command, int kernel,
            int slot)
        {
            command.SetComputeBufferParam(readoutCompute, kernel,
                ReadoutVerticesId, _grid.GetM8ReadoutVertices(slot));
        }

        private void SetMeshDepthMatrices(CommandBuffer command,
            DepthCapture.ReadoutDepthLease lease)
        {
            command.SetComputeMatrixParam(readoutCompute, MeshDepthProj0Id,
                lease.Proj0);
            command.SetComputeMatrixParam(readoutCompute, MeshDepthProj1Id,
                lease.Proj1);
            command.SetComputeMatrixParam(readoutCompute, MeshDepthProjInv0Id,
                lease.ProjInv0);
            command.SetComputeMatrixParam(readoutCompute, MeshDepthProjInv1Id,
                lease.ProjInv1);
            command.SetComputeMatrixParam(readoutCompute, MeshDepthView0Id,
                lease.View0);
            command.SetComputeMatrixParam(readoutCompute, MeshDepthView1Id,
                lease.View1);
            command.SetComputeMatrixParam(readoutCompute, MeshDepthViewInv0Id,
                lease.ViewInv0);
            command.SetComputeMatrixParam(readoutCompute, MeshDepthViewInv1Id,
                lease.ViewInv1);
        }

        private static void AddMeshDepthUniforms(
            MerkabaNativeUniformTable values,
            DepthCapture.ReadoutDepthLease lease)
        {
            values.Matrix("_M8MeshDepthProj0", lease.Proj0);
            values.Matrix("_M8MeshDepthProj1", lease.Proj1);
            values.Matrix("_M8MeshDepthProjInv0", lease.ProjInv0);
            values.Matrix("_M8MeshDepthProjInv1", lease.ProjInv1);
            values.Matrix("_M8MeshDepthView0", lease.View0);
            values.Matrix("_M8MeshDepthView1", lease.View1);
            values.Matrix("_M8MeshDepthViewInv0", lease.ViewInv0);
            values.Matrix("_M8MeshDepthViewInv1", lease.ViewInv1);
        }

        private void ReleaseDepthLease(ReadoutBuildTicket ticket)
        {
            if (ticket.MeshReadout)
                _depthCapture?.ReleaseReadoutDepth(ticket.DepthLease);
        }

        private void ApplyReadoutModeState(int slot, bool mesh)
        {
            Material material = slot is >= 0 and < 2 ? _materials[slot] : null;
            if (material == null) return;
            if (mesh) material.EnableKeyword("M8_STEREO_MESH");
            else material.DisableKeyword("M8_STEREO_MESH");
            ApplyCheckerReadoutState(material);
        }

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
            ReleaseDepthLease(ticket);
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
                _awaitingResidencyChange = false;
                _buildResidencyEpoch = ticket.ResidencyEpoch;
                _buildBlocked = false;
                _blockedOnResidency = false;
                if (_loadedCoverageReady != null &&
                    unchecked((int)(ticket.SourceGeneration -
                        _loadedCoverageSourceGeneration)) >= 0)
                {
                    TaskCompletionSource<bool> ready = _loadedCoverageReady;
                    _loadedCoverageReady = null;
                    _loadedCoverageSourceGeneration = 0u;
                    ready.TrySetResult(true);
                }
                if (_sourceGeneration != ticket.SourceGeneration)
                    _canonicalDirty = true;
                return;
            }

            _buildBlocked = true;
            _blockedOnResidency = status == 1u;
            _blockedSourceGeneration = ticket.SourceGeneration;
            _blockedResidencyEpoch = ticket.ResidencyEpoch;
            _awaitingResidencyChange = _blockedOnResidency;
            _buildResidencyEpoch = ticket.ResidencyEpoch;
            if (_sourceGeneration != ticket.SourceGeneration)
                _canonicalDirty = true;
        }

        private bool HasBuildReasonChanged()
        {
            if (_sourceGeneration != _blockedSourceGeneration)
                return true;
            return _blockedOnResidency &&
                _grid.ResidencyEpoch != _blockedResidencyEpoch;
        }

        internal bool TryGetFrontRenderResources(Camera camera, out int slot,
            out GraphicsBuffer vertices, out GraphicsBuffer indices,
            out Vector4[] gridCullPlanes,
            out bool compactVisibility)
        {
            slot = _frontReadout;
            vertices = null;
            indices = null;
            gridCullPlanes = null;
            compactVisibility = false;
            if (camera == null || !_initialized || _grid == null ||
                scanOpacity <= 0.001f)
                return false;

            vertices = _grid.GetM8ReadoutVertices(slot);
            indices = _grid.GetM8ReadoutIndices(slot);
            Material material = _materials[slot];
            if (vertices == null || indices == null || material == null)
                return false;
            compactVisibility = !material.IsKeywordEnabled("M8_STEREO_MESH");

            Matrix4x4 gridToWorld = _grid.GridToWorldMatrix;
            Matrix4x4 view0 = camera.worldToCameraMatrix;
            Matrix4x4 view1 = view0;
            Matrix4x4 projection0 = camera.projectionMatrix;
            Matrix4x4 projection1 = projection0;
            if (camera.stereoEnabled)
            {
                view0 = camera.GetStereoViewMatrix(
                    Camera.StereoscopicEye.Left);
                view1 = camera.GetStereoViewMatrix(
                    Camera.StereoscopicEye.Right);
                projection0 = camera.GetStereoProjectionMatrix(
                    Camera.StereoscopicEye.Left);
                projection1 = camera.GetStereoProjectionMatrix(
                    Camera.StereoscopicEye.Right);
            }
            WriteGridFrustumPlanes(projection0 * view0, gridToWorld,
                _leftCullPlanes, _gridCullPlanes, 0);
            WriteGridFrustumPlanes(projection1 * view1, gridToWorld,
                _rightCullPlanes, _gridCullPlanes, 6);
            gridCullPlanes = _gridCullPlanes;
            return true;
        }

        private static void WriteGridFrustumPlanes(Matrix4x4 worldToClip,
            Matrix4x4 gridToWorld, Plane[] scratch, Vector4[] destination,
            int destinationOffset)
        {
            GeometryUtility.CalculateFrustumPlanes(worldToClip, scratch);
            Matrix4x4 worldPlaneToGrid = gridToWorld.transpose;
            for (int index = 0; index < 6; index++)
            {
                Plane plane = scratch[index];
                Vector4 transformed = worldPlaneToGrid * new Vector4(
                    plane.normal.x, plane.normal.y, plane.normal.z,
                    plane.distance);
                float inverseLength = 1f / Mathf.Max(1e-12f,
                    new Vector3(transformed.x, transformed.y,
                        transformed.z).magnitude);
                destination[destinationOffset + index] =
                    transformed * inverseLength;
            }
        }

        internal bool RecordVisibilityPass(ComputeCommandBuffer command,
            int slot, BufferHandle vertices, BufferHandle indices,
            Vector4[] gridCullPlanes)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            ComputeBuffer drawArgs = _grid.GetM8DrawArgs(slot);
            bool timedSubmission = MerkabaGpuTimestamps.TryAcquire(
                CaptureOwner.Draw,
                _readoutRevision == 0u ? 1u : _readoutRevision, command);
            command.SetBufferData(drawArgs, ZeroDrawCount, 0, 0, 1);
            command.SetComputeBufferParam(readoutCompute, _visibilityKernel,
                ReadoutVerticesReadId, vertices);
            command.SetComputeBufferParam(readoutCompute, _visibilityKernel,
                ReadoutIndicesId, indices);
            command.SetComputeBufferParam(readoutCompute, _visibilityKernel,
                DrawArgsId, drawArgs);
            command.SetComputeVectorArrayParam(readoutCompute,
                CullGridPlanesId, gridCullPlanes);
            command.DispatchComputeProfiled(readoutCompute, _visibilityKernel,
                drawArgs, 6u * sizeof(uint));
            return timedSubmission;
        }

        internal void RecordRenderPass(RasterCommandBuffer command, int front,
            bool timingStartedByVisibility)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!readoutDrawEnabled || _gpuSubmissionSuspended || _grid == null ||
                _grid.GpuSubmissionSuspended)
                return;
            Material material = _materials[front];
            Mesh mesh = _grid.GetM8ReadoutMesh(front);
            ComputeBuffer drawArgs = _grid.GetM8DrawArgs(front);
            bool canDraw = _initialized && mesh != null && material != null &&
                scanOpacity > 0.001f;
            bool timedSubmission = timingStartedByVisibility ||
                canDraw && MerkabaGpuTimestamps.TryAcquire(CaptureOwner.Draw,
                    _readoutRevision == 0u ? 1u : _readoutRevision, command);
            if (canDraw)
                command.DrawMeshInstancedIndirectProfiled(mesh, 0,
                    material, 0, drawArgs, 0);
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

        private void ApplyOpacityState()
        {
            bool coverage = scanOpacity < 0.999f;
            for (int slot = 0; slot < 2; slot++)
            {
                Material material = _materials[slot];
                if (material == null) continue;
                material.SetFloat(ScanOpacityId, scanOpacity);
                if (coverage) material.EnableKeyword("M8_ALPHA_COVERAGE");
                else material.DisableKeyword("M8_ALPHA_COVERAGE");
                material.renderQueue = (int)RenderQueue.Geometry;
            }
        }

        private void ApplyFinePreviewState()
        {
            bool active = _finePreviewDescriptor.IsActive;
            Color tint = _finePreviewColor;
            tint.a = 0.25f;
            Vector4 parameters = active
                ? new Vector4(1f,
                    _finePreviewDescriptor.Radius *
                    _finePreviewDescriptor.Radius,
                    _finePreviewDescriptor.Length, 0f)
                : Vector4.zero;
            for (int slot = 0; slot < 2; slot++)
            {
                Material material = _materials[slot];
                if (material == null) continue;
                material.SetVector(FineCursorPositionId,
                    _finePreviewDescriptor.CursorPosition);
                material.SetVector(FineBrushAxisId,
                    _finePreviewDescriptor.Axis);
                material.SetVector(FineBrushParamsId, parameters);
                material.SetColor(FinePreviewColorId, tint);
                if (active) material.EnableKeyword("M8_FINE_PREVIEW");
                else material.DisableKeyword("M8_FINE_PREVIEW");
            }
        }

        private void ApplyRasterFeatureState()
        {
            for (int slot = 0; slot < 2; slot++)
            {
                Material material = _materials[slot];
                if (material == null) continue;
                if (_dynamicOcclusionEnabled)
                    material.EnableKeyword("M8_ENVIRONMENT_OCCLUSION");
                else
                    material.DisableKeyword("M8_ENVIRONMENT_OCCLUSION");
            }
            ApplyOpacityState();
        }

        private void ApplyCheckerReadoutState()
        {
            for (int slot = 0; slot < 2; slot++)
            {
                Material material = _materials[slot];
                if (material == null) continue;
                ApplyCheckerReadoutState(material);
            }
        }

        private void ApplyCheckerReadoutState(Material material)
        {
            bool standardReadout =
                !material.IsKeywordEnabled("M8_STEREO_MESH");
            if (checkerReadoutEnabled && standardReadout)
                material.EnableKeyword("M8_CHECKER_READOUT");
            else
                material.DisableKeyword("M8_CHECKER_READOUT");
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
