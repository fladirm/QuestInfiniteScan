using System;
using System.Threading.Tasks;
using UnityEngine;
using System.Collections.Generic;
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
        [SerializeField, Range(5f, 72f)] private float readoutBuildHz = 30f;
        [SerializeField, Range(0f, 4f)]
        private float readoutTranslationGuard = 1f;
        [SerializeField, Range(0f, 1f)] private float scanOpacity = 1f;
        [SerializeField] private bool readoutDrawEnabled = true;
        // Diagnostic hard-off of the publication producer: no readout build
        // is submitted while false, the canonical world keeps scanning. It
        // exists so that draw cost and producer cost can be measured apart.
        [SerializeField] private bool readoutProducerEnabled = true;
        [SerializeField] private bool checkerReadoutEnabled;

        /// <summary>
        /// Readout publishes a world sphere around the head, never a view.
        /// Frustum, stereo visibility and occlusion are draw concerns.
        /// </summary>
        internal const float ReadoutCoverageRadius = 6.4f;
        private const float ResidencyQueryTranslation = 1f;
        private const uint ReadoutBacklogBit = 0x80000000u;

        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;
        private Material _material;
        // Draw-only compute asset, isolated from the native publication
        // ABI: Android may never address the three 72 Hz kernels as
        // indices of the native readout producer.
        private ComputeShader _visibilityCompute;
        private int _beginKernel;
        private int _warmKernel;
        private int _prepareReclaimKernel;
        private int _reclaimKernel;
        private int _applyReclaimKernel;
        private int _copyKernel;
        private int _markKernel;
        private int _applyMutationKernel;
        private int _collectKernel;
        private int _prepareKernel;
        private int _collectPatchKernel;
        private int _solveKnotsKernel;
        private int _emitGeometryKernel;
        private int _publishKernel;
        private int _advanceBatchKernel;
        private int _validateKernel;
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
        private bool _cycleReadoutPending;
        private bool _buildInFlight;
        private int _frontReadout;
        // Strict two-slot ping-pong. After a swap the old FRONT may only be
        // written again once this fence proves every submitted frame that
        // could draw it has finished.
        private GraphicsFence _frontReleaseFence;
        private bool _hasFrontReleaseFence;
        private GraphicsFence _pendingPublicationFence;
        private bool _hasPendingPublicationFence;
        private int _frontTileCount;
        private bool _previousPublished;
        private uint _sourceGeneration = 1u;
        private uint _submissionRevision;
        private uint _lifecycleGeneration = 1u;
        private uint _renderResetGeneration;
        private ReadoutBuildTicket _pendingBuild;
        private MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob
            _nativeReadoutJob;
        // Native readout transaction: Begin -> Batch* -> Finalize, at most
        // one job per frame; nothing publishes before Finalize decided.
        private enum NativeReadoutPhase { None, Begin, Batch, Finalize }
        private NativeReadoutPhase _nativePhase;
        private int _nativeQueryGroups;
        private int _nativeBatchesSubmitted;
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

        public bool ReadoutProducerEnabled
        {
            get => readoutProducerEnabled;
            set
            {
                if (readoutProducerEnabled == value) return;
                readoutProducerEnabled = value;
                if (!value) return;
                // Re-enabling republishes the latest canonical world; the scan
                // kept mutating it while the producer was hard-off.
                _residencyQueryRequested = true;
                MarkCanonicalReadoutDirty();
            }
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
        private static readonly int PublishedIndicesId =
            Shader.PropertyToID("_M8RenderIndices");
        private static readonly int PublishedIndicesReadId =
            Shader.PropertyToID("_M8RenderIndicesRead");
        private static readonly int VisibleIndicesId =
            Shader.PropertyToID("_M8VisibleIndices");
        private static readonly int ReadoutRevisionId =
            Shader.PropertyToID("_M8ReadoutRevision");
        private static readonly int AllowWarmLoadsId =
            Shader.PropertyToID("_M8AllowWarmLoads");
        private static readonly int ResidencyChangedId =
            Shader.PropertyToID("_M8ResidencyChanged");
        private static readonly int RenderMutationQueueId =
            Shader.PropertyToID("_M8RenderMutationQueue");
        private static readonly int RenderPatchScratchId =
            Shader.PropertyToID("_M8RenderPatchScratch");
        private static readonly int RenderKnotScratchId =
            Shader.PropertyToID("_M8RenderKnotScratch");
        private static readonly int RenderKnotOwnerId =
            Shader.PropertyToID("_M8RenderKnotOwner");
        private static readonly int RenderScratchHeaderId =
            Shader.PropertyToID("_M8RenderScratchHeader");
        private static readonly int RenderPatchScratchReadId =
            Shader.PropertyToID("_M8RenderPatchScratchRead");
        private static readonly int RenderKnotScratchReadId =
            Shader.PropertyToID("_M8RenderKnotScratchRead");
        private static readonly int RenderKnotOwnerReadId =
            Shader.PropertyToID("_M8RenderKnotOwnerRead");
        private static readonly int RenderScratchHeaderReadId =
            Shader.PropertyToID("_M8RenderScratchHeaderRead");
        private static readonly int RenderVersionsId =
            Shader.PropertyToID("_M8RenderVersions");
        private static readonly int RenderVersionsReadId =
            Shader.PropertyToID("_M8RenderVersionsRead");
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
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        private const int DepthOnlyPass = 1;
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
        private static readonly uint[] ZeroCullControl = { 0u, 0u, 0u, 0u };
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

        /// <summary>
        /// The producer cycle is strictly serial: a scan transaction ends only
        /// once its readout publication has been adopted.
        /// </summary>
        // Work already submitted stays a scan barrier even when the producer
        // is hard-off; only a not yet submitted cycle is skipped.
        internal bool ReadoutCycleBusy => _initialized &&
            !_gpuSubmissionSuspended &&
            (_buildInFlight || MerkabaNativeVulkanExecutor.HasJobInFlight ||
             (readoutProducerEnabled && _cycleReadoutPending));

        /// <summary>Closes a scan transaction by requesting its readout.</summary>
        internal void RequestCycleReadout()
        {
            if (!_initialized || !readoutDrawEnabled) return;
            _cycleReadoutPending = true;
            MarkCanonicalReadoutDirty();
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

        /// <summary>
        /// The active producer, regardless of which camera draws it.
        /// </summary>
        internal static bool TryGetActiveProducer(
            out MerkabaGridRenderer renderer)
        {
            renderer = _active;
            return renderer != null && renderer._initialized &&
                   !renderer._gpuSubmissionSuspended &&
                   renderer.isActiveAndEnabled;
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
            if (_visibilityCompute == null)
                _visibilityCompute = Resources.Load<ComputeShader>(
                    "Merkaba/MerkabaVisibility");
            if (_grid == null || readoutCompute == null ||
                _visibilityCompute == null || renderShader == null)
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
            _prepareReclaimKernel = readoutCompute.FindProfiledKernel(
                "PrepareRenderReclaim", MerkabaGpuStage.ReadoutBuild);
            _reclaimKernel = readoutCompute.FindProfiledKernel(
                "ReclaimRenderPages", MerkabaGpuStage.ReadoutBuild);
            _applyReclaimKernel = readoutCompute.FindProfiledKernel(
                "ApplyRenderReclaim", MerkabaGpuStage.ReadoutBuild);
            _copyKernel = readoutCompute.FindProfiledKernel(
                "CopyRenderIndex", MerkabaGpuStage.ReadoutBuild);
            _markKernel = readoutCompute.FindProfiledKernel(
                "MarkRenderRebuildTiles", MerkabaGpuStage.ReadoutBuild);
            _applyMutationKernel = readoutCompute.FindProfiledKernel(
                "ApplyRenderMutationJournal", MerkabaGpuStage.ReadoutBuild);
            _collectKernel = readoutCompute.FindProfiledKernel(
                "CollectRenderRebuildTiles", MerkabaGpuStage.ReadoutBuild);
            _prepareKernel = readoutCompute.FindProfiledKernel(
                "PrepareRenderBuild", MerkabaGpuStage.ReadoutBuild);
            // Every kernel of the readout must exist; a shader that lost one
            // fails here, never as per-frame "Kernel at index is invalid".
            foreach (string name in new[]
                     {
                         "AdvanceRenderBuildBatch", "CollectRenderPatchInputs",
                         "SolveSharedKnots", "EmitRenderTileGeometry"
                     })
                if (!readoutCompute.HasKernel(name))
                    throw new InvalidOperationException(
                        $"MerkabaReadout.compute lost kernel {name}.");
            foreach (string name in new[]
                     {
                         "CullRenderTiles", "PrepareVisibleIndices",
                         "EmitVisibleIndices"
                     })
                if (!_visibilityCompute.HasKernel(name))
                    throw new InvalidOperationException(
                        $"MerkabaVisibility.compute lost kernel {name}.");
            _collectPatchKernel = readoutCompute.FindProfiledKernel(
                "CollectRenderPatchInputs", MerkabaGpuStage.ReadoutBuild);
            _solveKnotsKernel = readoutCompute.FindProfiledKernel(
                "SolveSharedKnots", MerkabaGpuStage.ReadoutBuild);
            _advanceBatchKernel = readoutCompute.FindProfiledKernel(
                "AdvanceRenderBuildBatch", MerkabaGpuStage.ReadoutBuild);
            _emitGeometryKernel = readoutCompute.FindProfiledKernel(
                "EmitRenderTileGeometry", MerkabaGpuStage.ReadoutBuild);
            _publishKernel = readoutCompute.FindProfiledKernel(
                "PublishRenderTileList", MerkabaGpuStage.ReadoutBuild);
            _validateKernel = readoutCompute.FindProfiledKernel(
                "ValidatePublication", MerkabaGpuStage.ReadoutBuild);
            _finalizeKernel = readoutCompute.FindProfiledKernel(
                "FinalizeReadout", MerkabaGpuStage.ReadoutBuild);
            _cullKernel = _visibilityCompute.FindProfiledKernel(
                "CullRenderTiles", MerkabaGpuStage.MerkabaDraw);
            _prepareVisibleKernel = _visibilityCompute.FindProfiledKernel(
                "PrepareVisibleIndices", MerkabaGpuStage.MerkabaDraw);
            _emitVisibleKernel = _visibilityCompute.FindProfiledKernel(
                "EmitVisibleIndices", MerkabaGpuStage.MerkabaDraw);
            foreach (int kernel in new[]
                     {
                         _beginKernel, _warmKernel, _prepareReclaimKernel,
                         _reclaimKernel,
                         _applyReclaimKernel, _copyKernel, _markKernel,
                         _applyMutationKernel, _collectKernel,
                         _prepareKernel, _advanceBatchKernel,
                         _collectPatchKernel, _solveKnotsKernel,
                         _emitGeometryKernel, _publishKernel,
                         _validateKernel, _finalizeKernel
                     })
            {
                _grid.BindWorldBuffers(readoutCompute, kernel);
                readoutCompute.SetBuffer(kernel, VisibleTilesId,
                    _grid.M8VisibleTiles);
                readoutCompute.SetBuffer(kernel, VisibleTilesReadId,
                    _grid.M8VisibleTiles);
                readoutCompute.SetBuffer(kernel, FrameDispatchArgsId,
                    _grid.M8FrameDispatchArgs);
                readoutCompute.SetBuffer(kernel, RenderVersionsId,
                    _grid.M8RenderVersions);
                readoutCompute.SetBuffer(kernel, RenderVersionsReadId,
                    _grid.M8RenderVersions);
                readoutCompute.SetBuffer(kernel, AttemptCompletionId,
                    _grid.M8AttemptCompletion);
            }
            foreach (int kernel in new[]
                     { _markKernel, _applyMutationKernel, _emitGeometryKernel })
                readoutCompute.SetBuffer(kernel, RenderMutationQueueId,
                    _grid.M8RenderMutationQueue);
            // Scratch views: the collect kernel writes descriptors and the
            // header, the solve kernel reads descriptors and writes knots,
            // the emit kernel only reads. Every kernel stays within the
            // eight writable bindings of the Quest gate.
            readoutCompute.SetBuffer(_collectPatchKernel, RenderPatchScratchId,
                _grid.M8RenderPatchScratch);
            readoutCompute.SetBuffer(_collectPatchKernel, RenderScratchHeaderId,
                _grid.M8RenderScratchHeader);
            readoutCompute.SetBuffer(_solveKnotsKernel, RenderPatchScratchId,
                _grid.M8RenderPatchScratch);
            readoutCompute.SetBuffer(_solveKnotsKernel, RenderKnotScratchId,
                _grid.M8RenderKnotScratch);
            readoutCompute.SetBuffer(_solveKnotsKernel, RenderKnotOwnerId,
                _grid.M8RenderKnotOwner);
            readoutCompute.SetBuffer(_solveKnotsKernel, RenderScratchHeaderId,
                _grid.M8RenderScratchHeader);
            readoutCompute.SetBuffer(_emitGeometryKernel, RenderPatchScratchReadId,
                _grid.M8RenderPatchScratch);
            readoutCompute.SetBuffer(_emitGeometryKernel, RenderKnotScratchReadId,
                _grid.M8RenderKnotScratch);
            readoutCompute.SetBuffer(_emitGeometryKernel, RenderKnotOwnerReadId,
                _grid.M8RenderKnotOwner);
            readoutCompute.SetBuffer(_emitGeometryKernel, RenderScratchHeaderReadId,
                _grid.M8RenderScratchHeader);
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
            PollPendingPublication();
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
            float coverageDistance = ReadoutCoverageRadius;
            _grid.SetResidencyFocus(cameraGrid,
                coverageDistance + MerkabaSpatial.BlockWorldSize);

            // Unity compute submissions are ordered on the graphics queue.
            // Pending/in-flight scanner work must not starve readout; only the
            // native executor owns buffers outside that queue.
            bool queueBusy = _buildInFlight ||
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
            // A readout that closes a scan transaction runs at once; a readout
            // without a scan keeps the free-running cadence.
            if (readoutProducerEnabled && !queueBusy && buildRequested &&
                BackSlotReleased() &&
                (_cycleReadoutPending ||
                 Time.unscaledTime >= _nextReadoutBuild))
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
            // The warm pass builds the publication list from the coverage
            // sphere on every build; residencyQuery only asks it to also
            // request loads, so it always runs over the whole query.
            int queryGroups = query.Groups;
            // Tiles waiting for a COLD halo retry only after residency moved.
            _retryPendingTiles = _grid.ResidencyEpoch != _pendingRetryEpoch;
            _pendingRetryEpoch = _grid.ResidencyEpoch;
#if !UNITY_EDITOR && UNITY_ANDROID
            SubmitNativeReadoutBuild(ticket, query, query.Groups);
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
                    _retryPendingTiles, query, query.Groups, residencyQuery);
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
                _canonicalDirty = true;
                Logger.Warning("Merkaba readout completion request failed: " +
                    exception.Message);
            }
#endif
        }

        /// <summary>Records one readout job on the graphics queue (editor).</summary>
        internal void RecordBuild(CommandBuffer command, int backSlot,
            uint revision, bool previousPublished, bool retryPendingTiles,
            Vector3 cameraGridMeters)
        {
            if (!Initialize())
                throw new InvalidOperationException(
                    "Merkaba readout is not initialized.");
            QueryShape query = ComputeQueryShape(cameraGridMeters);
            // The warm pass always sweeps the whole coverage sphere.
            RecordBuild(command, backSlot, revision, previousPublished,
                retryPendingTiles, query, query.Groups, true);
        }

        private void RecordBuild(CommandBuffer command, int backSlot,
            uint revision, bool previousPublished, bool retryPendingTiles,
            QueryShape query, int queryGroups, bool allowWarmLoads)
        {
            SetBuildParameters(command, revision, previousPublished,
                retryPendingTiles, query, allowWarmLoads);
            ComputeBuffer back = _grid.GetM8RenderIndex(backSlot);
            ComputeBuffer front = _grid.GetM8RenderIndex(1 - backSlot);
            foreach (int kernel in new[]
                     {
                         _beginKernel, _warmKernel, _copyKernel,
                         _collectKernel, _prepareKernel, _collectPatchKernel,
                         _emitGeometryKernel, _publishKernel,
                         _finalizeKernel
                     })
                command.SetComputeBufferParam(readoutCompute, kernel,
                    RenderIndexBackId, back);
            command.SetComputeBufferParam(readoutCompute, _validateKernel,
                RenderIndexBackId, back);
            command.SetComputeBufferParam(readoutCompute, _copyKernel,
                RenderIndexFrontReadId, front);
            // Everything the build writes belongs to the BACK slot only.
            ComputeBuffer backPages = _grid.GetM8RenderPageQueues(backSlot);
            foreach (int kernel in new[]
                     {
                         _beginKernel, _warmKernel, _prepareReclaimKernel,
                         _reclaimKernel, _applyReclaimKernel, _copyKernel,
                         _collectKernel, _collectPatchKernel,
                         _emitGeometryKernel, _validateKernel
                     })
                command.SetComputeBufferParam(readoutCompute, kernel,
                    RenderPageQueuesId, backPages);
            command.SetComputeBufferParam(readoutCompute, _emitGeometryKernel,
                RenderVerticesId, _grid.GetM8RenderVertices(backSlot));
            command.SetComputeBufferParam(readoutCompute, _emitGeometryKernel,
                PublishedIndicesId, _grid.GetM8PublishedIndices(backSlot));
            command.SetComputeBufferParam(readoutCompute, _validateKernel,
                PublishedIndicesReadId, _grid.GetM8PublishedIndices(backSlot));
            command.DispatchComputeProfiled(readoutCompute, _beginKernel,
                1, 1, 1);
            command.DispatchComputeProfiled(readoutCompute,
                _prepareReclaimKernel, 1, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _reclaimKernel,
                _grid.M8FrameDispatchArgs);
            command.DispatchComputeProfiled(readoutCompute,
                _applyReclaimKernel, 1, 1, 1);
            // The kernels read the published tile count themselves, so the
            // dispatch is a fixed slot sweep and no CPU value gates it.
            int frontGroups = MerkabaSpatial.PhysicalTileCapacity / 128;
            command.DispatchComputeProfiled(readoutCompute, _copyKernel,
                frontGroups, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _markKernel,
                1, 1, 1);
            command.DispatchComputeProfiled(readoutCompute,
                _applyMutationKernel, _grid.M8FrameDispatchArgs);
            command.DispatchComputeProfiled(readoutCompute, _warmKernel,
                queryGroups, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _collectKernel,
                frontGroups, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _prepareKernel,
                1, 1, 1);
            // One BACK transaction in batches of RenderBatchTiles: the batch
            // cursor lives on the GPU, so the editor records every possible
            // batch behind indirect arguments (zero groups once the rebuild
            // list is exhausted). Nothing publishes before Finalize.
            int batches = MerkabaSpatial.PhysicalTileCapacity /
                MerkabaGrid.RenderBatchTiles;
            for (int batch = 0; batch < batches; batch++)
            {
                command.DispatchComputeProfiled(readoutCompute,
                    _advanceBatchKernel, 1, 1, 1);
                command.DispatchComputeProfiled(readoutCompute,
                    _collectPatchKernel, _grid.M8FrameDispatchArgs);
                command.DispatchComputeProfiled(readoutCompute,
                    _solveKnotsKernel, _grid.M8FrameDispatchArgs);
                command.DispatchComputeProfiled(readoutCompute,
                    _emitGeometryKernel, _grid.M8FrameDispatchArgs);
            }
            command.DispatchComputeProfiled(readoutCompute, _publishKernel,
                1, 1, 1);
            command.DispatchComputeProfiled(readoutCompute, _validateKernel,
                frontGroups, 1, 1);
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
            // Coverage is the world sphere; the guard only keeps residency
            // ahead of head translation, and the halo resolves boundary
            // topology one lattice layer past coverage.
            float coverageDistance = ReadoutCoverageRadius;
            float warmDistance = coverageDistance + readoutTranslationGuard +
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
            bool previousPublished, bool retryPendingTiles, QueryShape query,
            bool allowWarmLoads)
        {
            command.SetComputeIntParam(readoutCompute, AllowWarmLoadsId,
                allowWarmLoads ? 1 : 0);
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
                query.CoverageDistance + MerkabaConstants.LatticeStep);
            command.SetComputeIntParams(readoutCompute, QueryCenterBlockId,
                query.CenterBlock.x, query.CenterBlock.y, query.CenterBlock.z);
            command.SetComputeIntParam(readoutCompute, QueryBlockRadiusId,
                query.Radius);
            command.SetComputeIntParam(readoutCompute, QueryBlockSideId,
                query.Side);
            command.SetComputeIntParam(readoutCompute, ReadoutRevisionId,
                unchecked((int)revision));
            command.SetComputeIntParam(readoutCompute, ResidencyChangedId,
                retryPendingTiles ? 1 : 0);

        }

#if !UNITY_EDITOR && UNITY_ANDROID
        /// <summary>
        /// Records the readout on the native scanner queue so the producer
        /// never occupies the graphics queue that draws FRONT at 72 Hz.
        /// </summary>
        private void SubmitNativeReadoutBuild(ReadoutBuildTicket ticket,
            QueryShape query, int queryGroups)
        {
            if (MerkabaNativeVulkanExecutor.HasJobInFlight) return;
            _nativeQueryGroups = queryGroups;
            _nativeBatchesSubmitted = 0;
            _buildInFlight = true;
            _pendingBuild = ticket;
            if (!TrySubmitNativeReadoutJob(
                    MerkabaNativeVulkanExecutor.JobKind.ReadoutBegin, ticket,
                    query))
            {
                _buildInFlight = false;
                _pendingBuild = default;
                _nativePhase = NativeReadoutPhase.None;
                return;
            }
            _nativePhase = NativeReadoutPhase.Begin;
            OnBuildSubmitted(ticket);
        }
#endif

        /// <summary>
        /// One job of the readout transaction on the native scanner queue:
        /// Begin (reclaim, journal, coverage list, rebuild list), Batch (one
        /// batch of rebuild tiles into the same BACK) or Finalize (validate,
        /// publication record). The same resources and uniforms every time.
        /// </summary>
        private bool TrySubmitNativeReadoutJob(
            MerkabaNativeVulkanExecutor.JobKind kind, ReadoutBuildTicket ticket,
            QueryShape query)
        {
            var values = new MerkabaNativeUniformTable();
            values.Vector3("_M8CameraGridMeters", query.CameraGridMeters);
            values.Vector3("_M8GridMetricDiagonal", query.MetricDiagonal);
            values.Vector3("_M8GridMetricCross", query.MetricCross);
            values.Float("_M8RenderDistance", query.CoverageDistance);
            values.Float("_M8WarmDistance", query.WarmDistance);
            values.Float("_M8DependencyDistance",
                query.CoverageDistance + MerkabaConstants.LatticeStep);
            values.Int3("_M8QueryCenterBlock", query.CenterBlock.x,
                query.CenterBlock.y, query.CenterBlock.z);
            values.Int("_M8QueryBlockRadius", query.Radius);
            values.Int("_M8QueryBlockSide", query.Side);
            values.UInt("_M8ReadoutRevision", ticket.Revision);
            values.UInt("_M8ResidencyChanged", _retryPendingTiles ? 1u : 0u);
            values.UInt("_M8AllowWarmLoads", ticket.ResidencyQuery ? 1u : 0u);
            var resources = new IntPtr[
                MerkabaNativeVulkanExecutor.ResourceCount];
            _grid.FillNativeExecutorWorldResources(resources);
            resources[(int)MerkabaNativeVulkanExecutor.Resource
                .RenderVertices] = _grid.GetM8RenderVertices(ticket.Slot)
                .GetNativeBufferPtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource
                .RenderPageQueues] = _grid.GetM8RenderPageQueues(ticket.Slot)
                .GetNativeBufferPtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource
                .RenderIndices] = _grid.GetM8PublishedIndices(ticket.Slot)
                .GetNativeBufferPtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource
                .RenderIndexBack] = _grid.GetM8RenderIndex(ticket.Slot)
                .GetNativeBufferPtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource
                .RenderIndexFront] = _grid.GetM8RenderIndex(1 - ticket.Slot)
                .GetNativeBufferPtr();
            int frontGroups = MerkabaSpatial.PhysicalTileCapacity / 128;
            if (!MerkabaNativeVulkanExecutor.TryCreateJob(kind,
                    ticket.Revision, resources, values, 0, 0, 0,
                    _nativeQueryGroups, out var nativeJob, frontGroups))
                return false;

            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba native readout submit");
            bool recorded = false;
            try
            {
                nativeJob.RecordPrepareAndSubmit(command);
                recorded = true;
                Graphics.ExecuteCommandBuffer(command);
                _nativeReadoutJob = nativeJob;
                return true;
            }
            catch (Exception exception)
            {
                if (recorded)
                {
                    _nativeReadoutJob = nativeJob;
                    Logger.Error("Merkaba native readout submission became " +
                        "uncertain; BACK remains quarantined: " +
                        exception.Message);
                    return true;
                }
                nativeJob.CancelBeforeExecution();
                nativeJob.Dispose();
                Logger.Error("Merkaba native readout submission failed: " +
                    exception.Message);
                return false;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private void PollNativeReadoutBuild()
        {
            if (_nativeReadoutJob == null) return;
            if (!_nativeReadoutJob.Poll(out string error)) return;
            ReadoutBuildTicket ticket = _pendingBuild;
            // Release the serial native lane at its GPU fence. Publication is
            // decided from the readout's own completion record, copied by the
            // job behind that fence.
            MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob job =
                _nativeReadoutJob;
            _nativeReadoutJob = null;
            using var release = job;
            if (!string.IsNullOrEmpty(error))
            {
                Logger.Error(error);
                AbortNativeTransaction(ticket);
                return;
            }
            if (!job.TryReadCompletion(_completionRecord))
            {
                AbortNativeTransaction(ticket);
                return;
            }
            switch (_nativePhase)
            {
                case NativeReadoutPhase.Begin:
                case NativeReadoutPhase.Batch:
                {
                    // Record 2 = (revision, cursor, rebuild count, scheduled).
                    uint cursor = _completionRecord[9];
                    uint total = _completionRecord[10];
                    int maximumBatches = MerkabaSpatial.PhysicalTileCapacity /
                        MerkabaGrid.RenderBatchTiles + 1;
                    if (_completionRecord[8] != ticket.Revision ||
                        _nativeBatchesSubmitted > maximumBatches)
                    {
                        AbortNativeTransaction(ticket);
                        return;
                    }
                    bool more = cursor < total;
                    MerkabaNativeVulkanExecutor.JobKind next = more
                        ? MerkabaNativeVulkanExecutor.JobKind.ReadoutBatch
                        : MerkabaNativeVulkanExecutor.JobKind.ReadoutFinalize;
                    QueryShape query = ComputeQueryShape(
                        ticket.CameraGridMeters);
                    if (!TrySubmitNativeReadoutJob(next, ticket, query))
                    {
                        AbortNativeTransaction(ticket);
                        return;
                    }
                    if (more)
                    {
                        _nativeBatchesSubmitted++;
                        _nativePhase = NativeReadoutPhase.Batch;
                    }
                    else
                    {
                        _nativePhase = NativeReadoutPhase.Finalize;
                        Logger.Info("Merkaba readout transaction " +
                            $"revision={ticket.Revision} tiles={total} " +
                            $"batches={_nativeBatchesSubmitted}");
                    }
                    return;
                }
                case NativeReadoutPhase.Finalize:
                {
                    _nativePhase = NativeReadoutPhase.None;
                    if (!DecidePublication(ticket, _completionRecord, 4))
                        RejectPublication(ticket);
                    return;
                }
                default:
                    AbortNativeTransaction(ticket);
                    return;
            }
        }

        /// <summary>
        /// Any failure inside the transaction rejects the whole BACK: FRONT,
        /// its tile count and the scan barrier stay exactly as they were.
        /// </summary>
        private void AbortNativeTransaction(ReadoutBuildTicket ticket)
        {
            _nativePhase = NativeReadoutPhase.None;
            RejectPublication(ticket);
        }

        // 0 editor readback, 1 publication decision, 2 batch progress.
        private readonly uint[] _completionRecord = new uint[12];

        /// <summary>
        /// Adopts BACK as FRONT only when the readout itself reports a
        /// published record for this revision. Anything else keeps FRONT.
        /// </summary>
        private bool DecidePublication(ReadoutBuildTicket ticket,
            uint[] record, int offset)
        {
            if (record == null || record.Length < offset + 4) return false;
            if (record[offset] != ticket.Revision ||
                record[offset + 1] != MerkabaGrid.ReadoutPublishedStatus ||
                ticket.RenderResetGeneration != _grid.RenderResetGeneration)
                return false;
            _buildInFlight = false;
            _pendingBuild = default;
            _cycleReadoutPending = false;
            _frontReadout = ticket.Slot;
            _frontTileCount = (int)Math.Min(record[offset + 2],
                (uint)MerkabaSpatial.PhysicalTileCapacity);
            _previousPublished = true;
            bool backlog = (record[offset + 3] & ReadoutBacklogBit) != 0u;
            if (backlog) _canonicalDirty = true;
            if (ticket.ResidencyQuery) _residencyQueryRequested = false;
            AdoptPublication();
            return true;
        }

        /// <summary>
        /// A failed BACK never changes FRONT. Its pages are reclaimed by the
        /// next build and the scan transaction stays open until a readout
        /// publishes, so no further scan mutates the world in between.
        /// </summary>
        private void RejectPublication(ReadoutBuildTicket ticket)
        {
            // FRONT, its tile count and _previousPublished stay exactly as
            // they were: the draw keeps the last valid publication while the
            // rejected BACK is completed by its next build.
            _buildInFlight = false;
            _pendingBuild = default;
            _canonicalDirty = true;
            _nextReadoutBuild = 0f;
        }

        /// <summary>
        /// Publication is confirmed by a GPU fence, never by reading the
        /// completion record back on the hot path.
        /// </summary>
        private void RequestReadoutCompletion(ReadoutBuildTicket ticket)
        {
            _pendingBuild = ticket;
            _pendingPublicationFence = Graphics.CreateGraphicsFence(
                GraphicsFenceType.CPUSynchronisation,
                SynchronisationStageFlags.AllGPUOperations);
            _hasPendingPublicationFence = true;
        }

        private void PollPendingPublication()
        {
            if (!_hasPendingPublicationFence ||
                !_pendingPublicationFence.passed) return;
            _hasPendingPublicationFence = false;
            ReadoutBuildTicket ticket = _pendingBuild;
            // Editor only: the graphics-queue build has no native completion
            // copy, so the 16-byte record is read back after the fence.
            uint lifecycle = _lifecycleGeneration;
            AsyncGPUReadback.Request(_grid.M8AttemptCompletion, 16, 16,
                request =>
                {
                    if (this == null || lifecycle != _lifecycleGeneration ||
                        !_buildInFlight || ticket.Revision !=
                        _pendingBuild.Revision)
                        return;
                    bool valid = !request.hasError;
                    if (valid)
                    {
                        Unity.Collections.NativeArray<uint> data =
                            request.GetData<uint>();
                        for (int index = 0; index < 4; index++)
                            _completionRecord[index] = data[index];
                        valid = DecidePublication(ticket, _completionRecord,
                            0);
                    }
                    if (!valid) RejectPublication(ticket);
                });
        }

        /// <summary>
        /// Atomic FRONT/BACK swap. The fence recorded here proves, once passed,
        /// that no submitted frame can still read the old FRONT, which is the
        /// only condition for writing it as BACK again.
        /// </summary>
        private void AdoptPublication()
        {
            _frontReleaseFence = Graphics.CreateGraphicsFence(
                GraphicsFenceType.CPUSynchronisation,
                SynchronisationStageFlags.AllGPUOperations);
            _hasFrontReleaseFence = true;
        }

        private bool BackSlotReleased() =>
            !_hasFrontReleaseFence || _frontReleaseFence.passed;

        /// <summary>Frame index buffer of the FRONT slot's mesh.</summary>
        internal GraphicsBuffer FrontVisibleIndices =>
            _grid != null && _initialized
                ? _grid.GetM8RenderIndices(_frontReadout) : null;

        internal bool TryGetFrontRenderResources(Camera camera,
            out ComputeBuffer frontIndex, out int frontTileCount,
            out Vector4[] gridCullPlanes)
        {
            frontIndex = null;
            frontTileCount = 0;
            gridCullPlanes = null;
            if (camera == null || !_initialized || _grid == null ||
                scanOpacity <= 0.001f || !_previousPublished)
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
            command.SetBufferData(cullControl, ZeroCullControl, 0, 0,
                MerkabaGrid.CullControlWords);
            Matrix4x4 gridToWorld = _grid.GridToWorldMatrix;
            MerkabaReadoutCoverage.WriteGridMetric(gridToWorld,
                out Vector3 metricDiagonal, out Vector3 metricCross);
            command.SetComputeVectorParam(_visibilityCompute, CameraGridMetersId,
                gridToWorld.inverse.MultiplyPoint3x4(cameraWorld));
            command.SetComputeVectorParam(_visibilityCompute, GridMetricDiagonalId,
                metricDiagonal);
            command.SetComputeVectorParam(_visibilityCompute, GridMetricCrossId,
                metricCross);
            // Draw policy may shorten the view, never extend it past what the
            // publication actually covers.
            command.SetComputeFloatParam(_visibilityCompute, RenderDistanceId,
                Mathf.Min(renderDistance, ReadoutCoverageRadius));
            command.SetComputeVectorArrayParam(_visibilityCompute,
                CullGridPlanesId, gridCullPlanes);
            command.SetComputeBufferParam(_visibilityCompute, _cullKernel,
                RenderIndexFrontReadId, frontIndex);
            command.SetComputeBufferParam(_visibilityCompute, _cullKernel,
                RenderPageQueuesId, _grid.GetM8RenderPageQueues(_frontReadout));
            command.SetComputeBufferParam(_visibilityCompute, _cullKernel,
                VisiblePagesId, _grid.M8VisiblePages);
            command.SetComputeBufferParam(_visibilityCompute, _cullKernel,
                CullControlId, cullControl);
            command.SetComputeBufferParam(_visibilityCompute,
                _prepareVisibleKernel, CullControlId, cullControl);
            command.SetComputeBufferParam(_visibilityCompute,
                _prepareVisibleKernel, RenderDrawArgsId,
                _grid.M8RenderDrawArgs);
            command.SetComputeBufferParam(_visibilityCompute, _emitVisibleKernel,
                VisiblePagesReadId, _grid.M8VisiblePages);
            command.SetComputeBufferParam(_visibilityCompute, _emitVisibleKernel,
                CullControlReadId, cullControl);
            command.SetComputeBufferParam(_visibilityCompute, _emitVisibleKernel,
                VisibleIndicesId, indices);
            command.SetComputeBufferParam(_visibilityCompute, _emitVisibleKernel,
                PublishedIndicesReadId,
                _grid.GetM8PublishedIndices(_frontReadout));
            int groups = MerkabaSpatial.PhysicalTileCapacity / 128;
            command.DispatchComputeProfiled(_visibilityCompute, _cullKernel,
                groups, 1, 1);
            command.DispatchComputeProfiled(_visibilityCompute,
                _prepareVisibleKernel, 1, 1, 1);
            command.DispatchComputeProfiled(_visibilityCompute,
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
            Mesh mesh = _grid.GetM8RenderMesh(_frontReadout);
            bool canDraw = _initialized && mesh != null && _material != null &&
                scanOpacity > 0.001f && _previousPublished;
            bool timedSubmission = timingStartedByVisibility ||
                canDraw && MerkabaGpuTimestamps.TryAcquire(CaptureOwner.Draw,
                    _submissionRevision == 0u ? 1u : _submissionRevision,
                    command);
            if (canDraw)
            {
                // Translucent: depth-only first, then the blended colour pass
                // with ZTest Equal (no stacked-layer overdraw).
                if (scanOpacity < 0.999f)
                    command.DrawMeshInstancedIndirectProfiled(mesh, 0,
                        _material, DepthOnlyPass, _grid.M8RenderDrawArgs, 0);
                command.DrawMeshInstancedIndirectProfiled(mesh, 0,
                    _material, 0, _grid.M8RenderDrawArgs, 0);
            }
            MerkabaGpuTimestamps.End(CaptureOwner.Draw, command,
                timedSubmission);
            MerkabaGpuTimestamps.Complete(CaptureOwner.Draw, timedSubmission,
                true);
        }

        private void ApplyOpacityState()
        {
            if (_material == null) return;
            // Real alpha blending, selected on the CPU as material state: a
            // translucent membrane blends over passthrough and writes no
            // depth; an opaque one is the depth-writing geometry it was.
            bool translucent = scanOpacity < 0.999f;
            _material.SetFloat(ScanOpacityId, scanOpacity);
            // 5 = SrcAlpha, 10 = OneMinusSrcAlpha, 1 = One, 0 = Zero.
            _material.SetInt(SrcBlendId, translucent ? 5 : 1);
            _material.SetInt(DstBlendId, translucent ? 10 : 0);
            _material.SetInt(ZWriteId, translucent ? 0 : 1);
            // 3 = Equal: the colour pass follows the depth-only pass and
            // blends exactly the nearest sheet layer. 4 = LEqual.
            _material.SetInt(ZTestId, translucent ? 3 : 4);
            if (translucent) _material.EnableKeyword("M8_ALPHA_BLEND");
            else _material.DisableKeyword("M8_ALPHA_BLEND");
            _material.renderQueue = translucent
                ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
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
                // Telemetry only. _frontTileCount changes solely on the
                // atomic publication swap; this counter may already hold the
                // count of a BACK that was later rejected.
                VisibleTileCount = ToInt(counters[
                    MerkabaGrid.CounterRenderPublishedTiles]);
                _coverageIncomplete = counters[
                    MerkabaGrid.CounterReadoutUnresolved] != 0u;
                if (_loadedCoverageReady != null && !_coverageIncomplete)
                {
                    TaskCompletionSource<bool> ready = _loadedCoverageReady;
                    _loadedCoverageReady = null;
                    _loadedCoverageSourceGeneration = 0u;
                    ready.TrySetResult(true);
                }
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
