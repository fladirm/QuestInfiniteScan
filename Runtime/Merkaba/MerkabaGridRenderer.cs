using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Disposable GPU readout derived from M8 and drawn once per XR frame.
    /// A build rewrites only tiles whose membrane inputs changed, plus their
    /// dependency halo, into free pages of one vertex pool. FRONT/BACK tile
    /// indices publish atomically; per-frame visibility culls tiles first and
    /// emits indices only for visible pages.
    /// </summary>
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
        [SerializeField] private bool checkerReadoutEnabled;

        /// <summary>Tiles rebuilt by one job; the remainder stays dirty.</summary>
        internal const int BuildTileCap = 2048;
        private const float ResidencyQueryTranslation = 1f;

        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;
        private Material _material;
        private int _beginKernel;
        private int _warmKernel;
        private int _reclaimKernel;
        private int _applyReclaimKernel;
        private int _copyKernel;
        private int _markKernel;
        private int _collectKernel;
        private int _prepareKernel;
        private int _buildKernel;
        private int _publishKernel;
        private int _finalizeKernel;
        private int _cullKernel;
        private int _prepareVisibleKernel;
        private int _emitVisibleKernel;
        private bool _initialized;
        private volatile bool _gpuSubmissionSuspended;
        private bool _statusReadbackPending;
        private float _nextStatusReadback;
        private float _nextReadoutBuild;
        private bool _canonicalDirty = true;
        private bool _buildInFlight;
        private int _frontReadout;
        private int _frontTileCount;
        private bool _previousPublished;
        private uint _sourceGeneration = 1u;
        private uint _submissionRevision;
        private uint _lifecycleGeneration = 1u;
        private uint _renderResetGeneration;
        private ReadoutBuildTicket _pendingBuild;
        private MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob
            _nativeReadoutJob;
        private uint _builtResidencyEpoch;
        private uint _pendingRetryEpoch;
        private bool _retryPendingTiles;
        private bool _residencyQueryRequested = true;
        private Vector3 _lastResidencyQueryCamera;
        private bool _coverageIncomplete = true;
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
            internal readonly uint RenderResetGeneration;
            internal readonly bool ResidencyQuery;
            internal readonly Vector3 CameraGridMeters;

            internal ReadoutBuildTicket(int slot, uint revision,
                uint lifecycleGeneration, uint sourceGeneration,
                uint residencyEpoch, uint renderResetGeneration,
                bool residencyQuery, Vector3 cameraGridMeters)
            {
                Slot = slot;
                Revision = revision;
                LifecycleGeneration = lifecycleGeneration;
                SourceGeneration = sourceGeneration;
                ResidencyEpoch = residencyEpoch;
                RenderResetGeneration = renderResetGeneration;
                ResidencyQuery = residencyQuery;
                CameraGridMeters = cameraGridMeters;
            }
        }

        private readonly struct QueryShape
        {
            internal readonly Vector3 CameraGridMeters;
            internal readonly Vector3 MetricDiagonal;
            internal readonly Vector3 MetricCross;
            internal readonly float CoverageDistance;
            internal readonly float WarmDistance;
            internal readonly Unity.Mathematics.int3 CenterBlock;
            internal readonly int Radius;
            internal int Side => Radius * 2 + 1;
            internal int Groups => Side * Side * Side;

            internal QueryShape(Vector3 camera, Vector3 diagonal, Vector3 cross,
                float coverage, float warm, Unity.Mathematics.int3 center,
                int radius)
            {
                CameraGridMeters = camera;
                MetricDiagonal = diagonal;
                MetricCross = cross;
                CoverageDistance = coverage;
                WarmDistance = warm;
                CenterBlock = center;
                Radius = radius;
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
        public bool CheckerReadoutEnabled
        {
            get => checkerReadoutEnabled;
            set
            {
                if (checkerReadoutEnabled == value) return;
                checkerReadoutEnabled = value;
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
        private static readonly int VisibleTilesReadId =
            Shader.PropertyToID("_M8VisibleTilesRead");
        private static readonly int RenderVerticesId =
            Shader.PropertyToID("_M8RenderVertices");
        private static readonly int RenderIndexBackId =
            Shader.PropertyToID("_M8RenderIndexBack");
        private static readonly int RenderIndexFrontReadId =
            Shader.PropertyToID("_M8RenderIndexFrontRead");
        private static readonly int RenderPageQueuesId =
            Shader.PropertyToID("_M8RenderPageQueues");
        private static readonly int FrameDispatchArgsId =
            Shader.PropertyToID("_M8FrameDispatchArgs");
        private static readonly int AttemptCompletionId =
            Shader.PropertyToID("_M8AttemptCompletion");
        private static readonly int VisiblePagesId =
            Shader.PropertyToID("_M8VisiblePages");
        private static readonly int VisiblePagesReadId =
            Shader.PropertyToID("_M8VisiblePagesRead");
        private static readonly int CullControlId =
            Shader.PropertyToID("_M8CullControl");
        private static readonly int CullControlReadId =
            Shader.PropertyToID("_M8CullControlRead");
        private static readonly int RenderDrawArgsId =
            Shader.PropertyToID("_M8RenderDrawArgs");
        private static readonly int VisibleIndicesId =
            Shader.PropertyToID("_M8VisibleIndices");
        private static readonly int FrontTileCountId =
            Shader.PropertyToID("_M8FrontTileCount");
        private static readonly int ReadoutRevisionId =
            Shader.PropertyToID("_M8ReadoutRevision");
        private static readonly int PreviousPublishedId =
            Shader.PropertyToID("_M8PreviousPublished");
        private static readonly int ResidencyChangedId =
            Shader.PropertyToID("_M8ResidencyChanged");
        private static readonly int BuildTileCapId =
            Shader.PropertyToID("_M8RenderBuildTileCap");
        private static readonly int CameraGridMetersId =
            Shader.PropertyToID("_M8CameraGridMeters");
        private static readonly int GridMetricDiagonalId =
            Shader.PropertyToID("_M8GridMetricDiagonal");
        private static readonly int GridMetricCrossId =
            Shader.PropertyToID("_M8GridMetricCross");
        private static readonly int RenderDistanceId =
            Shader.PropertyToID("_M8RenderDistance");
        private static readonly int WarmDistanceId =
            Shader.PropertyToID("_M8WarmDistance");
        private static readonly int DependencyDistanceId =
            Shader.PropertyToID("_M8DependencyDistance");
        private static readonly int QueryCenterBlockId =
            Shader.PropertyToID("_M8QueryCenterBlock");
        private static readonly int QueryBlockRadiusId =
            Shader.PropertyToID("_M8QueryBlockRadius");
        private static readonly int QueryBlockSideId =
            Shader.PropertyToID("_M8QueryBlockSide");
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
        private static readonly uint[] ZeroCullControl = { 0u, 0u, 1u, 1u };
        private readonly Plane[] _leftCullPlanes = new Plane[6];
        private readonly Plane[] _rightCullPlanes = new Plane[6];
        private readonly Vector4[] _gridCullPlanes = new Vector4[12];

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

        internal void BeginLoadedCoverageWarmup()
        {
            _loadedCoverageReady?.TrySetResult(true);
            _loadedCoverageReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            MarkCanonicalReadoutDirty();
            _residencyQueryRequested = true;
            _coverageIncomplete = true;
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
            if (_material != null) DestroyMaterial(_material);
            _material = null;
            _initialized = false;
            _statusReadbackPending = false;
            _canonicalDirty = true;
            _buildInFlight = false;
            _frontReadout = 0;
            _frontTileCount = 0;
            _previousPublished = false;
            _submissionRevision = 0u;
            _pendingBuild = default;
            _residencyQueryRequested = true;
            _coverageIncomplete = true;
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
            Material captured = _material;
            bool released = false;
            return () =>
            {
                if (released) return;
                released = true;
                if (this != null)
                    ReleaseOwnedResourcesAfterGpuRetirement();
                else if (captured != null)
                    DestroyMaterial(captured);
            };
        }

        private static void DestroyMaterial(Material material)
        {
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
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
            _beginKernel = readoutCompute.FindProfiledKernel(
                "BeginRenderBuild", MerkabaGpuStage.ReadoutBuild);
            _warmKernel = readoutCompute.FindProfiledKernel(
                "RequestWarmResidency", MerkabaGpuStage.WorldQuery);
            _reclaimKernel = readoutCompute.FindProfiledKernel(
                "ReclaimRenderPages", MerkabaGpuStage.ReadoutBuild);
            _applyReclaimKernel = readoutCompute.FindProfiledKernel(
                "ApplyRenderReclaim", MerkabaGpuStage.ReadoutBuild);
            _copyKernel = readoutCompute.FindProfiledKernel(
                "CopyRenderIndex", MerkabaGpuStage.ReadoutBuild);
            _markKernel = readoutCompute.FindProfiledKernel(
                "MarkRenderRebuildTiles", MerkabaGpuStage.ReadoutBuild);
            _collectKernel = readoutCompute.FindProfiledKernel(
                "CollectRenderRebuildTiles", MerkabaGpuStage.ReadoutBuild);
            _prepareKernel = readoutCompute.FindProfiledKernel(
                "PrepareRenderBuild", MerkabaGpuStage.ReadoutBuild);
            _buildKernel = readoutCompute.FindProfiledKernel(
                "BuildRenderTiles", MerkabaGpuStage.ReadoutBuild);
            _publishKernel = readoutCompute.FindProfiledKernel(
                "PublishRenderTileList", MerkabaGpuStage.ReadoutBuild);
            _finalizeKernel = readoutCompute.FindProfiledKernel(
                "FinalizeReadout", MerkabaGpuStage.ReadoutBuild);
            _cullKernel = readoutCompute.FindProfiledKernel(
                "CullRenderTiles", MerkabaGpuStage.MerkabaDraw);
            _prepareVisibleKernel = readoutCompute.FindProfiledKernel(
                "PrepareVisibleIndices", MerkabaGpuStage.MerkabaDraw);
            _emitVisibleKernel = readoutCompute.FindProfiledKernel(
                "EmitVisibleIndices", MerkabaGpuStage.MerkabaDraw);
            foreach (int kernel in new[]
                     {
                         _beginKernel, _warmKernel, _reclaimKernel,
                         _applyReclaimKernel, _copyKernel, _markKernel,
                         _collectKernel,
                         _prepareKernel, _buildKernel, _publishKernel,
                         _finalizeKernel
                     })
            {
                _grid.BindWorldBuffers(readoutCompute, kernel);
                readoutCompute.SetBuffer(kernel, VisibleTilesId,
                    _grid.M8VisibleTiles);
                readoutCompute.SetBuffer(kernel, VisibleTilesReadId,
                    _grid.M8VisibleTiles);
                readoutCompute.SetBuffer(kernel, FrameDispatchArgsId,
                    _grid.M8FrameDispatchArgs);
                readoutCompute.SetBuffer(kernel, RenderPageQueuesId,
                    _grid.M8RenderPageQueues);
                readoutCompute.SetBuffer(kernel, RenderVerticesId,
                    _grid.M8RenderVertices);
                readoutCompute.SetBuffer(kernel, AttemptCompletionId,
                    _grid.M8AttemptCompletion);
            }
            _material = new Material(renderShader)
            {
                name = "Merkaba M8 Readout"
            };
            ApplyOpacityState();
            ApplyFinePreviewState();
            ApplyRasterFeatureState();
            ApplyCheckerReadoutState();
            _renderResetGeneration = _grid.RenderResetGeneration;
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
            if (_grid.RenderResetGeneration != _renderResetGeneration)
            {
                // The world and both publication slots were cleared.
                _renderResetGeneration = _grid.RenderResetGeneration;
                _previousPublished = false;
                _frontTileCount = 0;
                _residencyQueryRequested = true;
                MarkCanonicalReadoutDirty();
            }

            Matrix4x4 gridToWorld = _grid.GridToWorldMatrix;
            _material.SetMatrix(GridToWorldId, gridToWorld);
            Vector3 cameraGrid = gridToWorld.inverse.MultiplyPoint3x4(
                camera.transform.position);
            float coverageDistance = renderDistance + readoutTranslationGuard;
            _grid.SetResidencyFocus(cameraGrid,
                coverageDistance + MerkabaSpatial.BlockWorldSize);

            bool scannerWork = _integrator != null &&
                (_integrator.HasPendingObservation ||
                 _integrator.HasAttemptInFlight ||
                 _integrator.HasPendingFineErase ||
                 _integrator.HasFineEraseAttemptInFlight);
            bool queueBusy = _buildInFlight || scannerWork ||
                MerkabaNativeVulkanExecutor.HasJobInFlight ||
                _nativeReadoutJob != null || _grid.StorageControlReady;
            // Head motion alone never rebuilds geometry. Entering new
            // coverage only requests residency (loads), whose installation
            // then marks the affected tiles dirty.
            bool residencyQuery = _residencyQueryRequested ||
                _coverageIncomplete ||
                Vector3.Distance(cameraGrid, _lastResidencyQueryCamera) >
                    ResidencyQueryTranslation;
            bool residencyChanged =
                _grid.ResidencyEpoch != _builtResidencyEpoch;
            bool buildRequested = _canonicalDirty || residencyChanged ||
                residencyQuery;
            if (!queueBusy && buildRequested &&
                Time.unscaledTime >= _nextReadoutBuild)
                SubmitReadoutBuild(cameraGrid, residencyQuery);

            RequestStatusIfDue();
        }

        private void SubmitReadoutBuild(Vector3 cameraGrid, bool residencyQuery)
        {
            if (_buildInFlight) return;
            int backSlot = 1 - _frontReadout;
            uint revision = NextNonZero(ref _submissionRevision);
            var ticket = new ReadoutBuildTicket(backSlot, revision,
                _lifecycleGeneration, _sourceGeneration, _grid.ResidencyEpoch,
                _grid.RenderResetGeneration, residencyQuery, cameraGrid);
            QueryShape query = ComputeQueryShape(cameraGrid);
            int queryGroups = residencyQuery ? query.Groups : 0;
            // Tiles waiting for a COLD halo retry only after residency moved.
            _retryPendingTiles = _grid.ResidencyEpoch != _pendingRetryEpoch;
            _pendingRetryEpoch = _grid.ResidencyEpoch;
#if !UNITY_EDITOR && UNITY_ANDROID
            SubmitNativeReadoutBuild(ticket, query, queryGroups);
#else
            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba M8 readout build");
            bool submitted = false;
            bool timedSubmission = false;
            _buildInFlight = true;
            _pendingBuild = ticket;
            try
            {
                timedSubmission = MerkabaGpuTimestamps.TryAcquire(
                    CaptureOwner.ReadoutBuild, revision, command);
                RecordBuild(command, ticket.Slot, revision, _previousPublished,
                    _retryPendingTiles, query, queryGroups);
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
                _buildInFlight = false;
                _pendingBuild = default;
                return;
            }
            OnBuildSubmitted(ticket);
            try
            {
                RequestReadoutCompletion(ticket);
            }
            catch (Exception exception)
            {
                _buildInFlight = false;
                _pendingBuild = default;
                _previousPublished = false;
                _canonicalDirty = true;
                Logger.Warning("Merkaba readout completion request failed: " +
                    exception.Message);
            }
#endif
        }

        /// <summary>Records one readout job on the graphics queue (editor).</summary>
        internal void RecordBuild(CommandBuffer command, int backSlot,
            uint revision, bool previousPublished, bool retryPendingTiles,
            Vector3 cameraGridMeters, bool residencyQuery)
        {
            if (!Initialize())
                throw new InvalidOperationException(
                    "Merkaba readout is not initialized.");
            QueryShape query = ComputeQueryShape(cameraGridMeters);
            RecordBuild(command, backSlot, revision, previousPublished,
                retryPendingTiles, query, residencyQuery ? query.Groups : 0);
        }

        private void RecordBuild(CommandBuffer command, int backSlot,
            uint revision, bool previousPublished, bool retryPendingTiles,
            QueryShape query, int queryGroups)
        {
            SetBuildParameters(command, revision, previousPublished,
                retryPendingTiles, query);
            ComputeBuffer back = _grid.GetM8RenderIndex(backSlot);
            ComputeBuffer front = _grid.GetM8RenderIndex(1 - backSlot);
            foreach (int kernel in new[]
                     {
                         _copyKernel, _collectKernel, _prepareKernel,
                         _buildKernel, _publishKernel, _finalizeKernel
                     })
                command.SetComputeBufferParam(readoutCompute, kernel,
                    RenderIndexBackId, back);
            command.SetComputeBufferParam(readoutCompute, _copyKernel,
                RenderIndexFrontReadId, front);
            int slotGroups = MerkabaSpatial.PhysicalTileCapacity / 256;
            command.DispatchComputeProfiled(readoutCompute, _beginKernel,
                1, 1, 1);
            if (queryGroups > 0)
                command.DispatchComputeProfiled(readoutCompute, _warmKernel,
                    queryGroups, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _reclaimKernel,
                _grid.M8FrameDispatchArgs);
            command.DispatchComputeProfiled(readoutCompute,
                _applyReclaimKernel, 1, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _copyKernel,
                slotGroups, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _markKernel,
                slotGroups, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _collectKernel,
                slotGroups, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _prepareKernel,
                1, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _buildKernel,
                _grid.M8FrameDispatchArgs);
            command.DispatchComputeProfiled(readoutCompute, _publishKernel,
                slotGroups, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _finalizeKernel,
                1, 1, 1);
        }

        private void OnBuildSubmitted(ReadoutBuildTicket ticket)
        {
            if (_sourceGeneration == ticket.SourceGeneration)
                _canonicalDirty = false;
            _builtResidencyEpoch = ticket.ResidencyEpoch;
            if (ticket.ResidencyQuery)
            {
                _residencyQueryRequested = false;
                _lastResidencyQueryCamera = ticket.CameraGridMeters;
            }
            _nextReadoutBuild = Time.unscaledTime +
                1f / Mathf.Max(1f, readoutBuildHz);
        }

        private QueryShape ComputeQueryShape(Vector3 cameraGridMeters)
        {
            var global = new Unity.Mathematics.int3(
                Mathf.FloorToInt(cameraGridMeters.x /
                    MerkabaConstants.LatticeStep),
                Mathf.FloorToInt(cameraGridMeters.y /
                    MerkabaConstants.LatticeStep),
                Mathf.FloorToInt(cameraGridMeters.z /
                    MerkabaConstants.LatticeStep));
            float coverageDistance = renderDistance + readoutTranslationGuard;
            float warmDistance = coverageDistance +
                MerkabaSpatial.BlockWorldSize;
            int radius = Mathf.CeilToInt(warmDistance /
                MerkabaSpatial.BlockWorldSize) + 1;
            MerkabaReadoutCoverage.WriteGridMetric(_grid.GridToWorldMatrix,
                out Vector3 metricDiagonal, out Vector3 metricCross);
            return new QueryShape(cameraGridMeters, metricDiagonal,
                metricCross, coverageDistance, warmDistance,
                MerkabaSpatial.Encode(global).BlockCoord, radius);
        }

        private void SetBuildParameters(CommandBuffer command, uint revision,
            bool previousPublished, bool retryPendingTiles, QueryShape query)
        {
            command.SetComputeVectorParam(readoutCompute, CameraGridMetersId,
                query.CameraGridMeters);
            command.SetComputeVectorParam(readoutCompute, GridMetricDiagonalId,
                query.MetricDiagonal);
            command.SetComputeVectorParam(readoutCompute, GridMetricCrossId,
                query.MetricCross);
            command.SetComputeFloatParam(readoutCompute, RenderDistanceId,
                query.CoverageDistance);
            command.SetComputeFloatParam(readoutCompute, WarmDistanceId,
                query.WarmDistance);
            command.SetComputeFloatParam(readoutCompute, DependencyDistanceId,
                query.CoverageDistance);
            command.SetComputeIntParams(readoutCompute, QueryCenterBlockId,
                query.CenterBlock.x, query.CenterBlock.y, query.CenterBlock.z);
            command.SetComputeIntParam(readoutCompute, QueryBlockRadiusId,
                query.Radius);
            command.SetComputeIntParam(readoutCompute, QueryBlockSideId,
                query.Side);
            command.SetComputeIntParam(readoutCompute, ReadoutRevisionId,
                unchecked((int)revision));
            command.SetComputeIntParam(readoutCompute, PreviousPublishedId,
                previousPublished ? 1 : 0);
            command.SetComputeIntParam(readoutCompute, ResidencyChangedId,
                retryPendingTiles ? 1 : 0);
            command.SetComputeIntParam(readoutCompute, BuildTileCapId,
                BuildTileCap);
        }

        private void PollNativeReadoutBuild()
        {
            if (_nativeReadoutJob == null) return;
            if (!_nativeReadoutJob.Poll(out string error)) return;
            ReadoutBuildTicket ticket = _pendingBuild;
            // Release the serial native lane at its GPU fence. Publication is
            // decided from the readout's own completion record.
            _nativeReadoutJob.Dispose();
            _nativeReadoutJob = null;
            if (!string.IsNullOrEmpty(error))
            {
                _buildInFlight = false;
                _pendingBuild = default;
                _previousPublished = false;
                _canonicalDirty = true;
                Logger.Error(error);
                return;
            }
            try
            {
                RequestReadoutCompletion(ticket);
            }
            catch (Exception exception)
            {
                _buildInFlight = false;
                _pendingBuild = default;
                _previousPublished = false;
                _canonicalDirty = true;
                Logger.Warning("Merkaba readout completion request failed: " +
                    exception.Message);
            }
        }

        private void RequestReadoutCompletion(ReadoutBuildTicket ticket)
        {
            AsyncGPUReadback.Request(_grid.M8AttemptCompletion, 16, 16,
                request => CompleteReadoutBuild(ticket, request));
        }

#if !UNITY_EDITOR && UNITY_ANDROID
        private void SubmitNativeReadoutBuild(ReadoutBuildTicket ticket,
            QueryShape query, int queryGroups)
        {
            if (MerkabaNativeVulkanExecutor.HasJobInFlight) return;
            var values = new MerkabaNativeUniformTable();
            values.Vector3("_M8CameraGridMeters", query.CameraGridMeters);
            values.Vector3("_M8GridMetricDiagonal", query.MetricDiagonal);
            values.Vector3("_M8GridMetricCross", query.MetricCross);
            values.Float("_M8RenderDistance", query.CoverageDistance);
            values.Float("_M8WarmDistance", query.WarmDistance);
            values.Float("_M8DependencyDistance", query.CoverageDistance);
            values.Int3("_M8QueryCenterBlock", query.CenterBlock.x,
                query.CenterBlock.y, query.CenterBlock.z);
            values.Int("_M8QueryBlockRadius", query.Radius);
            values.Int("_M8QueryBlockSide", query.Side);
            values.UInt("_M8ReadoutRevision", ticket.Revision);
            values.UInt("_M8PreviousPublished", _previousPublished ? 1u : 0u);
            values.UInt("_M8ResidencyChanged", _retryPendingTiles ? 1u : 0u);
            values.UInt("_M8RenderBuildTileCap", (uint)BuildTileCap);
            var resources = new IntPtr[
                MerkabaNativeVulkanExecutor.ResourceCount];
            _grid.FillNativeExecutorWorldResources(resources);
            resources[(int)MerkabaNativeVulkanExecutor.Resource.RenderVertices] =
                _grid.M8RenderVertices.GetNativeBufferPtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource.RenderIndexBack] =
                _grid.GetM8RenderIndex(ticket.Slot).GetNativeBufferPtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource.RenderIndexFront] =
                _grid.GetM8RenderIndex(1 - ticket.Slot).GetNativeBufferPtr();
            if (!MerkabaNativeVulkanExecutor.TryCreateJob(
                    MerkabaNativeVulkanExecutor.JobKind.Readout,
                    ticket.Revision, resources, values, 0, 0, 0,
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
                OnBuildSubmitted(ticket);
            }
            catch (Exception exception)
            {
                if (recorded)
                {
                    _nativeReadoutJob = nativeJob;
                    OnBuildSubmitted(ticket);
                    Logger.Error("Merkaba native readout submission became " +
                        "uncertain; BACK remains quarantined: " +
                        exception.Message);
                    return;
                }
                nativeJob.CancelBeforeExecution();
                nativeJob.Dispose();
                _buildInFlight = false;
                _pendingBuild = default;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }
#endif

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
            bool valid = !request.hasError &&
                ticket.RenderResetGeneration == _grid.RenderResetGeneration;
            Unity.Collections.NativeArray<uint> record = valid
                ? request.GetData<uint>() : default;
            valid &= valid && record.Length == 4 &&
                record[0] == ticket.Revision &&
                record[1] == MerkabaGrid.ReadoutPublishedStatus;
            if (!valid)
            {
                // BACK is never adopted; its fresh pages are reclaimed by the
                // next build and FRONT keeps drawing.
                _previousPublished = false;
                _canonicalDirty = true;
                return;
            }

            _frontReadout = ticket.Slot;
            _frontTileCount = (int)Math.Min(record[2],
                (uint)MerkabaSpatial.PhysicalTileCapacity);
            _previousPublished = true;
            uint unresolved = record[3];
            if (ticket.ResidencyQuery)
                _coverageIncomplete = unresolved != 0u;
            if (_loadedCoverageReady != null && ticket.ResidencyQuery &&
                unresolved == 0u &&
                unchecked((int)(ticket.SourceGeneration -
                    _loadedCoverageSourceGeneration)) >= 0)
            {
                TaskCompletionSource<bool> ready = _loadedCoverageReady;
                _loadedCoverageReady = null;
                _loadedCoverageSourceGeneration = 0u;
                ready.TrySetResult(true);
            }
        }

        internal bool TryGetFrontRenderResources(Camera camera,
            out ComputeBuffer frontIndex, out int frontTileCount,
            out Vector4[] gridCullPlanes)
        {
            frontIndex = null;
            frontTileCount = 0;
            gridCullPlanes = null;
            if (camera == null || !_initialized || _grid == null ||
                scanOpacity <= 0.001f || _frontTileCount <= 0)
                return false;

            frontIndex = _grid.GetM8RenderIndex(_frontReadout);
            frontTileCount = _frontTileCount;
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
            return frontIndex != null;
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

        /// <summary>
        /// Culls FRONT tiles against both eye frusta and the draw distance,
        /// then emits indices only for pages of visible tiles. Cost follows
        /// resident tiles plus visible pages, never all published patches.
        /// </summary>
        internal bool RecordVisibilityPass(ComputeCommandBuffer command,
            ComputeBuffer frontIndex, int frontTileCount,
            Vector4[] gridCullPlanes, Vector3 cameraWorld,
            GraphicsBuffer indices)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            bool timedSubmission = MerkabaGpuTimestamps.TryAcquire(
                CaptureOwner.Draw,
                _submissionRevision == 0u ? 1u : _submissionRevision, command);
            ComputeBuffer cullControl = _grid.M8CullControl;
            command.SetBufferData(cullControl, ZeroCullControl, 0, 0, 4);
            Matrix4x4 gridToWorld = _grid.GridToWorldMatrix;
            MerkabaReadoutCoverage.WriteGridMetric(gridToWorld,
                out Vector3 metricDiagonal, out Vector3 metricCross);
            command.SetComputeVectorParam(readoutCompute, CameraGridMetersId,
                gridToWorld.inverse.MultiplyPoint3x4(cameraWorld));
            command.SetComputeVectorParam(readoutCompute, GridMetricDiagonalId,
                metricDiagonal);
            command.SetComputeVectorParam(readoutCompute, GridMetricCrossId,
                metricCross);
            command.SetComputeFloatParam(readoutCompute, RenderDistanceId,
                renderDistance + readoutTranslationGuard);
            command.SetComputeIntParam(readoutCompute, FrontTileCountId,
                frontTileCount);
            command.SetComputeVectorArrayParam(readoutCompute,
                CullGridPlanesId, gridCullPlanes);
            command.SetComputeBufferParam(readoutCompute, _cullKernel,
                RenderIndexFrontReadId, frontIndex);
            command.SetComputeBufferParam(readoutCompute, _cullKernel,
                VisiblePagesId, _grid.M8VisiblePages);
            command.SetComputeBufferParam(readoutCompute, _cullKernel,
                CullControlId, cullControl);
            command.SetComputeBufferParam(readoutCompute,
                _prepareVisibleKernel, CullControlId, cullControl);
            command.SetComputeBufferParam(readoutCompute,
                _prepareVisibleKernel, RenderDrawArgsId,
                _grid.M8RenderDrawArgs);
            command.SetComputeBufferParam(readoutCompute, _emitVisibleKernel,
                VisiblePagesReadId, _grid.M8VisiblePages);
            command.SetComputeBufferParam(readoutCompute, _emitVisibleKernel,
                CullControlReadId, cullControl);
            command.SetComputeBufferParam(readoutCompute, _emitVisibleKernel,
                VisibleIndicesId, indices);
            int groups = Mathf.Max(1, (frontTileCount + 127) / 128);
            command.DispatchComputeProfiled(readoutCompute, _cullKernel,
                groups, 1, 1);
            command.DispatchComputeProfiled(readoutCompute,
                _prepareVisibleKernel, 1, 1, 1);
            command.DispatchComputeProfiled(readoutCompute,
                _emitVisibleKernel, cullControl, sizeof(uint));
            return timedSubmission;
        }

        internal void RecordRenderPass(RasterCommandBuffer command,
            bool timingStartedByVisibility)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!readoutDrawEnabled || _gpuSubmissionSuspended || _grid == null ||
                _grid.GpuSubmissionSuspended)
                return;
            Mesh mesh = _grid.M8RenderMesh;
            bool canDraw = _initialized && mesh != null && _material != null &&
                scanOpacity > 0.001f;
            bool timedSubmission = timingStartedByVisibility ||
                canDraw && MerkabaGpuTimestamps.TryAcquire(CaptureOwner.Draw,
                    _submissionRevision == 0u ? 1u : _submissionRevision,
                    command);
            if (canDraw)
                command.DrawMeshInstancedIndirectProfiled(mesh, 0,
                    _material, 0, _grid.M8RenderDrawArgs, 0);
            MerkabaGpuTimestamps.End(CaptureOwner.Draw, command,
                timedSubmission);
            MerkabaGpuTimestamps.Complete(CaptureOwner.Draw, timedSubmission,
                true);
        }

        private void ApplyOpacityState()
        {
            if (_material == null) return;
            bool coverage = scanOpacity < 0.999f;
            _material.SetFloat(ScanOpacityId, scanOpacity);
            if (coverage) _material.EnableKeyword("M8_ALPHA_COVERAGE");
            else _material.DisableKeyword("M8_ALPHA_COVERAGE");
            _material.renderQueue = (int)RenderQueue.Geometry;
        }

        private void ApplyFinePreviewState()
        {
            if (_material == null) return;
            bool active = _finePreviewDescriptor.IsActive;
            Color tint = _finePreviewColor;
            tint.a = 0.25f;
            Vector4 parameters = active
                ? new Vector4(1f,
                    _finePreviewDescriptor.Radius *
                    _finePreviewDescriptor.Radius,
                    _finePreviewDescriptor.Length, 0f)
                : Vector4.zero;
            _material.SetVector(FineCursorPositionId,
                _finePreviewDescriptor.CursorPosition);
            _material.SetVector(FineBrushAxisId, _finePreviewDescriptor.Axis);
            _material.SetVector(FineBrushParamsId, parameters);
            _material.SetColor(FinePreviewColorId, tint);
            if (active) _material.EnableKeyword("M8_FINE_PREVIEW");
            else _material.DisableKeyword("M8_FINE_PREVIEW");
        }

        private void ApplyRasterFeatureState()
        {
            if (_material != null)
            {
                if (_dynamicOcclusionEnabled)
                    _material.EnableKeyword("M8_ENVIRONMENT_OCCLUSION");
                else
                    _material.DisableKeyword("M8_ENVIRONMENT_OCCLUSION");
            }
            ApplyOpacityState();
        }

        private void ApplyCheckerReadoutState()
        {
            if (_material == null) return;
            if (checkerReadoutEnabled)
                _material.EnableKeyword("M8_CHECKER_READOUT");
            else
                _material.DisableKeyword("M8_CHECKER_READOUT");
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
                VisibleTileCount = ToInt(counters[
                    MerkabaGrid.CounterRenderPublishedTiles]);
                VisiblePrimitiveCount = ToInt(counters[
                    MerkabaGrid.CounterLogicalPrimitives]);
                LateDrawColdMisses = ToInt(counters[
                    MerkabaGrid.CounterLateDrawColdMisses]);
                VisibleChunkCount = ToInt(counters[
                    MerkabaGrid.CounterVisibleChunks]);
                VisibleSurfaceKernelCount = ToInt(counters[
                    MerkabaGrid.CounterRenderPublishedPatches]);
                RenderPrimitiveOverflow = counters[
                    MerkabaGrid.CounterRenderPrimitiveOverflow] != 0u;
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
