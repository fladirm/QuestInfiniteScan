using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Dual-eye forward geometry readout of the one canonical carrier. Exact packed
    /// Q16.48 projective readout is cached on GPU, then ordinary raster hardware
    /// selects first hit. All targets and readout vertices are disposable.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SigmaCarrier))]
    [RequireComponent(typeof(SigmaTopologyController))]
    [RequireComponent(typeof(SigmaRigBridge))]
    [DefaultExecutionOrder(-10)]
    public sealed class SigmaRenderer : MonoBehaviour, IRoomScanModule
    {
        public const int ReadoutExtent = SigmaCarrier.PageSize + 1;
        public const int ReadoutSamplesPerPage = ReadoutExtent * ReadoutExtent;
        public const int VerticesPerCarrierPage =
            SigmaCarrier.PageSize * SigmaCarrier.PageSize * 6;

        private const string ReadoutResource = "SigmaPrism/SigmaForwardReadout";
        private const string PredictionResource = "SigmaPrism/SigmaPredict";
        private const string PreviewResource =
            "SigmaPrism/SigmaDirectCarrierPreview";
        private const string RevalidationComputeResource =
            "SigmaPrism/SigmaStreamRevalidation";
        private const string RevalidationShaderResource =
            "SigmaPrism/SigmaStreamRevalidation";

        [SerializeField, Range(3, 12)] private int targetRingSlots = 4;
        [SerializeField, Min(16f)] private float directPreviewBounds = 128f;

        private static readonly int ExactGateId = Shader.PropertyToID(
            "_SigmaExactBackendGate");
        private static readonly int CarrierStateId = Shader.PropertyToID(
            "_CarrierState");
        private static readonly int PageMetadataId = Shader.PropertyToID(
            "_PageMetadata");
        private static readonly int StreamManifestsId = Shader.PropertyToID(
            "_StreamManifests");
        private static readonly int StreamPageVisibilityId =
            Shader.PropertyToID("_StreamPageVisibility");
        private static readonly int StreamManifestCapacityId =
            Shader.PropertyToID("_StreamManifestCapacity");
        private static readonly int ReadoutDirtyFlagsId = Shader.PropertyToID(
            "_ReadoutDirtyFlags");
        private static readonly int ReadoutVerticesId = Shader.PropertyToID(
            "_ReadoutVertices");
        private static readonly int CurrentPageSlotsId = Shader.PropertyToID(
            "_CurrentPageSlots");
        private static readonly int ReadoutDrawArgumentsId = Shader.PropertyToID(
            "_ReadoutDrawArguments");
        private static readonly int ReadoutDirtyPageSlotsId = Shader.PropertyToID(
            "_ReadoutDirtyPageSlots");
        private static readonly int ReadoutBuildArgumentsId = Shader.PropertyToID(
            "_ReadoutBuildArguments");
        private static readonly int ReadoutHaloArgumentsId = Shader.PropertyToID(
            "_ReadoutHaloArguments");
        private static readonly int PageCapacityId = Shader.PropertyToID(
            "_PageCapacity");
        private static readonly int ClipFromWorldId = Shader.PropertyToID(
            "_ClipFromWorld");
        private static readonly int OpticalFromWorldId = Shader.PropertyToID(
            "_OpticalFromWorld");
        private static readonly int SegmentIndexId = Shader.PropertyToID(
            "_SegmentIndex");
        private static readonly int TopologyCellFlagsId = Shader.PropertyToID(
            "_TopologyCellFlags");
        private static readonly int TopologyPageKeysId = Shader.PropertyToID(
            "_TopologyPageKeys");
        private static readonly int PoseResultId = Shader.PropertyToID(
            "_PoseResult");
        private static readonly int PoseReferenceFromWorldId = Shader.PropertyToID(
            "_PoseConsumeReferenceFromWorld");
        private static readonly int PoseWorldFromReferenceId = Shader.PropertyToID(
            "_PoseConsumeWorldFromReference");
        private static readonly int PreviewWireframeId = Shader.PropertyToID(
            "_PreviewWireframe");
        private static readonly int PreviewContactPixelsId = Shader.PropertyToID(
            "_PreviewContactPixels");
        private static readonly int ContactFootprintPixelsId = Shader.PropertyToID(
            "_ContactFootprintPixels");
        private static readonly int StreamTransactionsId = Shader.PropertyToID(
            "_StreamTransactions");
        private static readonly int StreamBundlesId = Shader.PropertyToID(
            "_StreamBundles");
        private static readonly int StreamSourceSegmentsId = Shader.PropertyToID(
            "_StreamSourceSegments");
        private static readonly int StreamWorkItemsId = Shader.PropertyToID(
            "_StreamWorkItems");
        private static readonly int StreamWorkCountsId = Shader.PropertyToID(
            "_StreamWorkCounts");
        private static readonly int StreamAssociationOwnersId =
            Shader.PropertyToID("_StreamAssociationOwners");
        private static readonly int StreamAssociationId = Shader.PropertyToID(
            "_StreamAssociation");
        private static readonly int StreamSchedulerControlId =
            Shader.PropertyToID("_StreamSchedulerControl");
        private static readonly int StreamDiagnosticsId = Shader.PropertyToID(
            "_StreamDiagnostics");
        private static readonly int StreamBundleCalibrationId =
            Shader.PropertyToID("_StreamBundleCalibration");
        private static readonly int StreamBundleRayEpochId =
            Shader.PropertyToID("_StreamBundleRayEpoch");
        private static readonly int StreamSourceSegmentCapacityId =
            Shader.PropertyToID("_StreamSourceSegmentCapacity");
        private static readonly int RawWordsId = Shader.PropertyToID("_RawWords");
        private static readonly int RevalidationContextId =
            Shader.PropertyToID("_RevalidationContext");
        private static readonly int RevalidationPageSnapshotId =
            Shader.PropertyToID("_RevalidationPageSnapshot");
        private static readonly int RevalidationDrawArgumentsId =
            Shader.PropertyToID("_RevalidationDrawArguments");
        private static readonly int RevalidationTargetResolutionId =
            Shader.PropertyToID("_RevalidationTargetResolution");
        private static readonly int RevalidationDepthSupportId =
            Shader.PropertyToID("_RevalidationDepthSupport");
        private static readonly int RevalidationCarrierPageId =
            Shader.PropertyToID("_RevalidationCarrierPage");
        private static readonly int RevalidationCarrierUvNormalId =
            Shader.PropertyToID("_RevalidationCarrierUvNormal");
        private static readonly int RevalidationStateKeyId =
            Shader.PropertyToID("_RevalidationStateKey");

        private readonly List<SigmaCarrierReadBatch> _readBatches = new();
        private readonly List<SegmentReadoutCache> _segmentCaches = new();
        private readonly RenderTargetIdentifier[] _mrt =
            new RenderTargetIdentifier[4];
        private readonly RenderTargetIdentifier[] _revalidationMrt =
            new RenderTargetIdentifier[4];
        private readonly List<HistoricalRevalidationTargets>
            _retiredRevalidationTargets = new();
        private readonly List<GraphicsBuffer> _retiredRevalidationSnapshots =
            new();

        private RoomScanner _scanner;
        private SigmaCarrier _carrier;
        private SigmaTopologyController _topology;
        private SigmaRigBridge _rigBridge;
        private SigmaExactBackendGate _backendGate;
        private ComputeShader _readoutCompute;
        private ComputeShader _revalidationCompute;
        private Material _predictionMaterial;
        private Material _previewMaterial;
        private Material _revalidationMaterial;
        private MaterialPropertyBlock _properties;
        private SigmaPredictionTargetRing _targets;
        private GraphicsBuffer _identityPoseResult;
        private GraphicsBuffer _streamManifests;
        private GraphicsBuffer _streamPageVisibility;
        private GraphicsBuffer _revalidationContext;
        private GraphicsBuffer _revalidationDrawArguments;
        private GraphicsBuffer _revalidationPageSnapshot;
        private HistoricalRevalidationTargets _revalidationTargets;
        private SigmaPredictionFrameLease _latest;
        private RigCalibration _calibration;
        private int _buildKernel;
        private int _compactKernel;
        private int _resolveHaloKernel;
        private int _prepareRevalidationKernel;
        private int _buildRevalidationArgsKernel;
        private int _rebuildRevalidationAssociationKernel;
        private int _finalizeRevalidationKernel;
        private int _cancelRevalidationKernel;
        private int _streamManifestCapacity;
        private int _streamSegmentIndex = -1;
        private long _lastSourceSequence;
        private bool _running;
        private bool _initialized;

        public string ModuleName => "Sigma forward readout";
        public bool IsInitialized => _initialized;
        public long RenderedFrames { get; private set; }
        public long BackpressureFrames { get; private set; }

        public event Action<SigmaPredictionFrameLease> PredictionReady;

        internal void BindStreamingManifest(int segmentIndex,
            GraphicsBuffer manifests, GraphicsBuffer pageVisibility,
            int capacity)
        {
            if (segmentIndex < 0 || manifests == null ||
                pageVisibility == null || capacity <= 0 ||
                manifests.count < capacity || pageVisibility.count < capacity)
                throw new ArgumentException(
                    "Forward readout requires a complete manifest view.");
            _streamSegmentIndex = segmentIndex;
            _streamManifests = manifests;
            _streamPageVisibility = pageVisibility;
            _streamManifestCapacity = capacity;
            EnsureHistoricalRevalidationPageSnapshot(capacity);
        }

        internal void UnbindStreamingManifest(GraphicsBuffer manifests)
        {
            if (!ReferenceEquals(_streamManifests, manifests))
                return;
            _streamSegmentIndex = -1;
            _streamManifests = null;
            _streamPageVisibility = null;
            _streamManifestCapacity = 0;
        }

        internal bool TryGetStreamingReadoutDiagnostics(
            out GraphicsBuffer drawArguments,
            out GraphicsBuffer currentPageSlots,
            out GraphicsBuffer vertices,
            out int pageCapacity)
        {
            drawArguments = null;
            currentPageSlots = null;
            vertices = null;
            pageCapacity = 0;
            if (_streamSegmentIndex < 0 || !_initialized)
                return false;
            EnsureSegmentCaches();
            int count = Math.Min(_readBatches.Count, _segmentCaches.Count);
            for (int index = 0; index < count; ++index)
            {
                if (_readBatches[index].SegmentIndex != _streamSegmentIndex)
                    continue;
                SegmentReadoutCache cache = _segmentCaches[index];
                drawArguments = cache.DrawArguments;
                currentPageSlots = cache.CurrentPageSlots;
                vertices = cache.Vertices;
                pageCapacity = cache.Capacity;
                return drawArguments != null && currentPageSlots != null &&
                    vertices != null && pageCapacity > 0;
            }
            return false;
        }

        public bool TryAcquireLatest(out SigmaPredictionFrameLease frame)
        {
            if (_latest == null || _latest.IsDisposed)
            {
                frame = null;
                return false;
            }
            frame = _latest.Retain();
            return true;
        }

        public void OnModuleInitialize(RoomScanner scanner)
        {
            if (_initialized)
                return;
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _carrier = scanner.Carrier ?? GetComponent<SigmaCarrier>();
            _topology = scanner.SigmaTopology ??
                GetComponent<SigmaTopologyController>();
            _rigBridge = scanner.RigBridge ?? GetComponent<SigmaRigBridge>();
            _backendGate = scanner.ExactBackendGate ??
                throw new InvalidOperationException(
                    "Sigma renderer requires the exact backend gate.");
            _readoutCompute = Resources.Load<ComputeShader>(ReadoutResource);
            _revalidationCompute = Resources.Load<ComputeShader>(
                RevalidationComputeResource);
            Shader prediction = Resources.Load<Shader>(PredictionResource);
            Shader preview = Resources.Load<Shader>(PreviewResource);
            Shader revalidation = Resources.Load<Shader>(
                RevalidationShaderResource);
            if (_carrier == null || _topology == null || _rigBridge == null ||
                _readoutCompute == null || _revalidationCompute == null ||
                prediction == null || preview == null || revalidation == null)
                throw new InvalidOperationException(
                    "Sigma forward-readout resources are incomplete.");

            _buildKernel = _readoutCompute.FindKernel("BuildCarrierReadout");
            _compactKernel = _readoutCompute.FindKernel("CompactCurrentPages");
            _resolveHaloKernel = _readoutCompute.FindKernel(
                "ResolveCarrierHalos");
            _prepareRevalidationKernel = _revalidationCompute.FindKernel(
                "PrepareHistoricalRevalidation");
            _buildRevalidationArgsKernel = _revalidationCompute.FindKernel(
                "BuildHistoricalDrawArguments");
            _rebuildRevalidationAssociationKernel =
                _revalidationCompute.FindKernel(
                    "RebuildHistoricalAssociation");
            _finalizeRevalidationKernel = _revalidationCompute.FindKernel(
                "FinalizeHistoricalRevalidation");
            _cancelRevalidationKernel = _revalidationCompute.FindKernel(
                "CancelHistoricalRevalidation");
            _predictionMaterial = new Material(prediction)
            {
                name = "[Sigma-PRISM-16] Prediction Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            _previewMaterial = new Material(preview)
            {
                name = "[Sigma-PRISM-16] Temporary Direct Carrier Preview",
                hideFlags = HideFlags.HideAndDontSave
            };
            _revalidationMaterial = new Material(revalidation)
            {
                name = "[Sigma-PRISM-16] Historical Revalidation",
                hideFlags = HideFlags.HideAndDontSave
            };
            _properties = new MaterialPropertyBlock();
            _targets = new SigmaPredictionTargetRing(targetRingSlots);
            _identityPoseResult = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 4, sizeof(uint) * 4)
            {
                name = "Sigma identity pose-gauge readout"
            };
            _identityPoseResult.SetData(new uint[16]);
            _revalidationContext = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 4, sizeof(uint) * 4)
            {
                name = "Sigma historical revalidation context"
            };
            _revalidationContext.SetData(new uint[] {
                uint.MaxValue, 0u, 0u, 0u,
                uint.MaxValue, 0u, 0u, 0u,
                0u, 0u, 0u, 0u,
                0u, 0u, 0u, 0u
            });
            _revalidationDrawArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 8, sizeof(uint))
            {
                name = "Sigma historical revalidation draw args"
            };
            _revalidationDrawArguments.SetData(new uint[] {
                0u, 2u, 0u, 0u, 0u, 2u, 0u, 0u
            });
            _initialized = true;
        }

        public void OnScanStarted()
        {
            _running = true;
            _lastSourceSequence = 0L;
        }

        public void OnScanStopped()
        {
            _running = false;
            _latest?.Dispose();
            _latest = null;
        }

        private void LateUpdate()
        {
            if (!_running || !_initialized)
                return;
            if (_carrier.IsInitialized &&
                _rigBridge.TryAcquireLatest(out StereoRigFrameLease source))
            {
                try
                {
                    if (source.Sequence != _lastSourceSequence &&
                        TryRender(source))
                        _lastSourceSequence = source.Sequence;
                }
                finally
                {
                    source.Dispose();
                }
            }
            RenderDirectCarrierPreview();
        }

        internal bool TryRender(StereoRigFrameLease source)
        {
            if (source == null)
                return false;
            return RenderPrediction(source,
                SigmaPoseGaugeState.Identity(source.CalibrationEpoch), true,
                out _);
        }

        internal bool TryRenderPoseGauge(StereoRigFrameLease source,
            SigmaPoseGaugeState gauge, out SigmaPredictionFrameLease prediction)
        {
            if (!gauge.Resolved || source == null ||
                gauge.CalibrationEpoch != source.CalibrationEpoch)
            {
                prediction = null;
                return false;
            }
            return RenderPrediction(source, gauge, false, out prediction);
        }

        /// <summary>
        /// Records the accepted GPU-resident pose gauge into the caller's same-frame
        /// transaction. No result is read back: the returned target lease remains
        /// alive until the caller's fence passes.
        /// </summary>
        internal bool TryRecordPoseGaugePrediction(CommandBuffer command,
            StereoRigFrameLease source, GraphicsBuffer poseResult,
            out SigmaPredictionFrameLease prediction)
        {
            prediction = null;
            if (!_initialized || command == null || source == null ||
                !source.IsValid || poseResult == null || _readBatches.Count == 0)
                return false;
            if (!_targets.TryBegin(source,
                    SigmaPoseGaugeState.Identity(source.CalibrationEpoch),
                    out prediction))
            {
                BackpressureFrames++;
                return false;
            }
            try
            {
                Matrix4x4 referenceWorld = Matrix4x4.TRS(
                    source.DepthLeft.WorldFromCamera.position,
                    source.DepthLeft.WorldFromCamera.rotation, Vector3.one);
                for (int eye = 0; eye < 2; ++eye)
                {
                    GpuImageView view = eye == 0 ? source.DepthLeft :
                        source.DepthRight;
                    Matrix4x4 rawWorld = Matrix4x4.TRS(
                        view.WorldFromCamera.position,
                        view.WorldFromCamera.rotation, Vector3.one);
                    Matrix4x4 opticalFromWorld = rawWorld.inverse;
                    SetMrt(prediction);
                    command.SetRenderTarget(_mrt,
                        new RenderTargetIdentifier(prediction.HardwareDepth), 0,
                        CubemapFace.Unknown, eye);
                    command.ClearRenderTarget(true, true, Color.clear, 1f);
                    DrawSegments(command, BuildClipFromWorld(view,
                            opticalFromWorld), opticalFromWorld, poseResult,
                        referenceWorld.inverse, referenceWorld);
                }
                return true;
            }
            catch
            {
                prediction.Dispose();
                prediction = null;
                throw;
            }
        }

        internal void EnsureHistoricalRevalidationTargets(
            Vector2Int sourceResolution)
        {
            if (sourceResolution.x <= 0 || sourceResolution.y <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceResolution));
            if (_revalidationTargets != null &&
                _revalidationTargets.Resolution.x >= sourceResolution.x &&
                _revalidationTargets.Resolution.y >= sourceResolution.y)
                return;
            Vector2Int resolution = _revalidationTargets == null
                ? sourceResolution
                : new Vector2Int(
                    Math.Max(sourceResolution.x,
                        _revalidationTargets.Resolution.x),
                    Math.Max(sourceResolution.y,
                        _revalidationTargets.Resolution.y));
            if (_revalidationTargets != null)
                _retiredRevalidationTargets.Add(_revalidationTargets);
            _revalidationTargets = new HistoricalRevalidationTargets(
                resolution);
        }

        /// <summary>
        /// Rebuilds one stale retained-source association entirely on GPU. The
        /// current carrier cache is disposable geometry; bundle-owned Q48
        /// calibration and raw pixels remain the immutable source authority.
        /// </summary>
        internal void RecordHistoricalRevalidation(CommandBuffer command,
            SigmaStreamingResources stream, SigmaConstraintLedger ledger)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (ledger == null)
                throw new ArgumentNullException(nameof(ledger));

            SigmaCarrierReadBatch batch = default;
            SegmentReadoutCache cache = null;
            SigmaTopologySegmentView topologyView = default;
            bool cacheReady = _revalidationTargets != null &&
                PrepareReadoutCaches(command) &&
                TryGetStreamingReadout(out batch, out cache,
                    out topologyView);
            if (!cacheReady)
            {
                BindCancelHistoricalRevalidation(command, stream);
                command.DispatchCompute(_revalidationCompute,
                    _cancelRevalidationKernel, 1, 1, 1);
                return;
            }

            BindPrepareHistoricalRevalidation(command, stream, batch);
            command.DispatchCompute(_revalidationCompute,
                _prepareRevalidationKernel, 1, 1, 1);
            BindHistoricalDrawArguments(command, stream);
            command.DispatchCompute(_revalidationCompute,
                _buildRevalidationArgsKernel, 1, 1, 1);

            _revalidationTargets.SetMrt(_revalidationMrt);
            command.SetRenderTarget(_revalidationMrt,
                new RenderTargetIdentifier(_revalidationTargets.HardwareDepth),
                0, CubemapFace.Unknown, -1);
            command.DrawProceduralIndirect(Matrix4x4.identity,
                _revalidationMaterial, 0, MeshTopology.Triangles,
                _revalidationDrawArguments, 0);
            _properties.Clear();
            _properties.SetInt(SegmentIndexId, batch.SegmentIndex);
            _properties.SetBuffer(ReadoutVerticesId, cache.Vertices);
            _properties.SetBuffer(RevalidationPageSnapshotId,
                _revalidationPageSnapshot);
            _properties.SetBuffer(StreamPageVisibilityId,
                stream.PageVisibility);
            _properties.SetBuffer(PageMetadataId, batch.Metadata);
            _properties.SetBuffer(TopologyCellFlagsId, topologyView.CellFlags);
            _properties.SetBuffer(TopologyPageKeysId, topologyView.PageKeys);
            _properties.SetBuffer(RevalidationContextId,
                _revalidationContext);
            _properties.SetBuffer(StreamBundleCalibrationId,
                stream.BundleCalibration);
            _properties.SetBuffer(StreamBundleRayEpochId,
                stream.BundleRayEpoch);
            command.DrawProceduralIndirect(Matrix4x4.identity,
                _revalidationMaterial, 1, MeshTopology.Triangles,
                _revalidationDrawArguments, sizeof(uint) * 4, _properties);

            BindRebuildHistoricalAssociation(command, stream, ledger);
            command.DispatchCompute(_revalidationCompute,
                _rebuildRevalidationAssociationKernel, 64, 1, 1);
            BindFinalizeHistoricalRevalidation(command, stream);
            command.DispatchCompute(_revalidationCompute,
                _finalizeRevalidationKernel, 1, 1, 1);
        }

        private bool TryGetStreamingReadout(out SigmaCarrierReadBatch batch,
            out SegmentReadoutCache cache,
            out SigmaTopologySegmentView topologyView)
        {
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch candidate = _readBatches[index];
                if (candidate.SegmentIndex != _streamSegmentIndex ||
                    !_topology.TryGetSegmentView(candidate.SegmentIndex,
                        out topologyView))
                    continue;
                batch = candidate;
                cache = _segmentCaches[index];
                return true;
            }
            batch = default;
            cache = null;
            topologyView = default;
            return false;
        }

        private void BindPrepareHistoricalRevalidation(CommandBuffer command,
            SigmaStreamingResources stream, SigmaCarrierReadBatch batch)
        {
            int kernel = _prepareRevalidationKernel;
            SetRevalidationBuffer(command, kernel, StreamWorkCountsId,
                stream.WorkCounts);
            SetRevalidationBuffer(command, kernel, StreamWorkItemsId,
                stream.WorkItems);
            SetRevalidationBuffer(command, kernel, StreamSourceSegmentsId,
                stream.SourceHandleSegments);
            SetRevalidationBuffer(command, kernel, StreamManifestsId,
                stream.PublicationManifests);
            SetRevalidationBuffer(command, kernel, StreamPageVisibilityId,
                stream.PageVisibility);
            SetRevalidationBuffer(command, kernel, PageMetadataId,
                batch.Metadata);
            SetRevalidationBuffer(command, kernel, StreamTransactionsId,
                stream.Transactions);
            SetRevalidationBuffer(command, kernel, StreamBundlesId,
                stream.Bundles);
            SetRevalidationBuffer(command, kernel, StreamAssociationOwnersId,
                stream.AssociationOwners);
            SetRevalidationBuffer(command, kernel, StreamSchedulerControlId,
                stream.SchedulerControl);
            SetRevalidationBuffer(command, kernel, RevalidationContextId,
                _revalidationContext);
            SetRevalidationBuffer(command, kernel,
                RevalidationPageSnapshotId, _revalidationPageSnapshot);
            SetRevalidationBuffer(command, kernel, StreamDiagnosticsId,
                stream.Diagnostics);
            command.SetComputeIntParam(_revalidationCompute, PageCapacityId,
                batch.PageCapacity);
            command.SetComputeIntParam(_revalidationCompute,
                StreamManifestCapacityId, stream.PageVisibility.count);
            command.SetComputeIntParam(_revalidationCompute,
                StreamSourceSegmentCapacityId,
                stream.SourceHandleSegments.count);
        }

        private void BindHistoricalDrawArguments(CommandBuffer command,
            SigmaStreamingResources stream)
        {
            int kernel = _buildRevalidationArgsKernel;
            SetRevalidationBuffer(command, kernel, StreamWorkCountsId,
                stream.WorkCounts);
            SetRevalidationBuffer(command, kernel, StreamSchedulerControlId,
                stream.SchedulerControl);
            SetRevalidationBuffer(command, kernel, RevalidationContextId,
                _revalidationContext);
            SetRevalidationBuffer(command, kernel,
                RevalidationDrawArgumentsId, _revalidationDrawArguments);
        }

        private void BindRebuildHistoricalAssociation(CommandBuffer command,
            SigmaStreamingResources stream, SigmaConstraintLedger ledger)
        {
            int kernel = _rebuildRevalidationAssociationKernel;
            SetRevalidationBuffer(command, kernel, StreamWorkCountsId,
                stream.WorkCounts);
            SetRevalidationBuffer(command, kernel, StreamWorkItemsId,
                stream.WorkItems);
            SetRevalidationBuffer(command, kernel, StreamTransactionsId,
                stream.Transactions);
            SetRevalidationBuffer(command, kernel, StreamBundlesId,
                stream.Bundles);
            SetRevalidationBuffer(command, kernel, StreamBundleRayEpochId,
                stream.BundleRayEpoch);
            SetRevalidationBuffer(command, kernel, RawWordsId,
                ledger.RawWordsBuffer);
            SetRevalidationBuffer(command, kernel, StreamAssociationId,
                stream.Association);
            SetRevalidationBuffer(command, kernel, RevalidationContextId,
                _revalidationContext);
            SetRevalidationBuffer(command, kernel, StreamPageVisibilityId,
                stream.PageVisibility);
            SetRevalidationBuffer(command, kernel,
                RevalidationPageSnapshotId, _revalidationPageSnapshot);
            command.SetComputeIntParam(_revalidationCompute, PageCapacityId,
                stream.PageVisibility.count);
            command.SetComputeTextureParam(_revalidationCompute, kernel,
                RevalidationDepthSupportId,
                _revalidationTargets.DepthSupport);
            command.SetComputeTextureParam(_revalidationCompute, kernel,
                RevalidationCarrierPageId,
                _revalidationTargets.CarrierPage);
            command.SetComputeTextureParam(_revalidationCompute, kernel,
                RevalidationCarrierUvNormalId,
                _revalidationTargets.CarrierUvNormal);
            command.SetComputeTextureParam(_revalidationCompute, kernel,
                RevalidationStateKeyId, _revalidationTargets.StateKey);
            command.SetComputeVectorParam(_revalidationCompute,
                RevalidationTargetResolutionId,
                new Vector4(_revalidationTargets.Resolution.x,
                    _revalidationTargets.Resolution.y, 0f, 0f));
        }

        private void BindFinalizeHistoricalRevalidation(CommandBuffer command,
            SigmaStreamingResources stream)
        {
            int kernel = _finalizeRevalidationKernel;
            SetRevalidationBuffer(command, kernel, StreamWorkCountsId,
                stream.WorkCounts);
            SetRevalidationBuffer(command, kernel, StreamWorkItemsId,
                stream.WorkItems);
            SetRevalidationBuffer(command, kernel, StreamTransactionsId,
                stream.Transactions);
            SetRevalidationBuffer(command, kernel, StreamBundlesId,
                stream.Bundles);
            SetRevalidationBuffer(command, kernel, StreamSchedulerControlId,
                stream.SchedulerControl);
            SetRevalidationBuffer(command, kernel, RevalidationContextId,
                _revalidationContext);
            SetRevalidationBuffer(command, kernel, StreamPageVisibilityId,
                stream.PageVisibility);
            SetRevalidationBuffer(command, kernel,
                RevalidationPageSnapshotId, _revalidationPageSnapshot);
            SetRevalidationBuffer(command, kernel, StreamDiagnosticsId,
                stream.Diagnostics);
            command.SetComputeIntParam(_revalidationCompute, PageCapacityId,
                stream.PageVisibility.count);
        }

        private void BindCancelHistoricalRevalidation(CommandBuffer command,
            SigmaStreamingResources stream)
        {
            int kernel = _cancelRevalidationKernel;
            SetRevalidationBuffer(command, kernel, StreamSchedulerControlId,
                stream.SchedulerControl);
            SetRevalidationBuffer(command, kernel, RevalidationContextId,
                _revalidationContext);
            SetRevalidationBuffer(command, kernel, StreamPageVisibilityId,
                stream.PageVisibility);
            SetRevalidationBuffer(command, kernel,
                RevalidationPageSnapshotId, _revalidationPageSnapshot);
            command.SetComputeIntParam(_revalidationCompute, PageCapacityId,
                stream.PageVisibility.count);
        }

        private void EnsureHistoricalRevalidationPageSnapshot(int capacity)
        {
            if (_revalidationPageSnapshot != null &&
                _revalidationPageSnapshot.count >= capacity)
                return;
            if (_revalidationPageSnapshot != null)
                _retiredRevalidationSnapshots.Add(
                    _revalidationPageSnapshot);
            _revalidationPageSnapshot = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, capacity,
                sizeof(uint) * 4)
            {
                name = "Sigma sealed historical page snapshot"
            };
        }

        private void SetRevalidationBuffer(CommandBuffer command, int kernel,
            int property, GraphicsBuffer buffer)
        {
            command.SetComputeBufferParam(_revalidationCompute, kernel,
                property, buffer);
        }

        private bool RenderPrediction(StereoRigFrameLease source,
            SigmaPoseGaugeState gauge, bool publish,
            out SigmaPredictionFrameLease result)
        {
            result = null;
            if (!_initialized || source == null || !source.IsValid)
                return false;
            if (_calibration == null || !_calibration.IsCompatible(source))
            {
                if (!RigCalibration.TryCreate(source, out _calibration))
                    return false;
            }
            if (!_targets.TryBegin(source, gauge,
                    out SigmaPredictionFrameLease prediction))
            {
                BackpressureFrames++;
                return false;
            }

            CommandBuffer command = CommandBufferPool.Get(
                "Sigma-PRISM-16 Forward Readout");
            try
            {
                PrepareReadoutCaches(command);
                for (int eye = 0; eye < 2; ++eye)
                {
                    GpuImageView view = eye == 0
                        ? source.DepthLeft
                        : source.DepthRight;
                    Pose correctedPose = gauge.Apply(
                        source.DepthLeft.WorldFromCamera,
                        view.WorldFromCamera);
                    Matrix4x4 opticalFromWorld = Matrix4x4.TRS(
                        correctedPose.position, correctedPose.rotation,
                        Vector3.one).inverse;
                    Matrix4x4 clipFromWorld = BuildClipFromWorld(view,
                        opticalFromWorld);
                    SetMrt(prediction);
                    command.SetRenderTarget(_mrt,
                        new RenderTargetIdentifier(prediction.HardwareDepth), 0,
                        CubemapFace.Unknown, eye);
                    command.ClearRenderTarget(true, true, Color.clear, 1f);
                    Matrix4x4 referenceWorld = Matrix4x4.TRS(
                        source.DepthLeft.WorldFromCamera.position,
                        source.DepthLeft.WorldFromCamera.rotation, Vector3.one);
                    DrawSegments(command, clipFromWorld, opticalFromWorld,
                        _identityPoseResult, referenceWorld.inverse,
                        referenceWorld);
                }

                Graphics.ExecuteCommandBuffer(command);
                prediction.CommitGpuWrite();
                result = prediction;
                if (publish)
                {
                    SigmaPredictionFrameLease previous = _latest;
                    _latest = prediction;
                    previous?.Dispose();
                    PredictionReady?.Invoke(prediction);
                }
                RenderedFrames++;
                return true;
            }
            catch (Exception exception)
            {
                prediction.Dispose();
                result = null;
                Logger.Error("Sigma forward readout failed: " + exception.Message);
                return false;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private bool PrepareReadoutCaches(CommandBuffer command)
        {
            if (_streamManifests == null || _streamPageVisibility == null ||
                _streamManifestCapacity <= 0)
                return false;
            _carrier.CollectReadableSegments(_readBatches);
            EnsureSegmentCaches();
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                if (batch.SegmentIndex != _streamSegmentIndex)
                    continue;
                SegmentReadoutCache cache = _segmentCaches[index];
                BindCompaction(command, batch, cache);
                command.DispatchCompute(_readoutCompute, _compactKernel, 1, 1, 1);
                BindBuild(command, batch, cache);
                command.DispatchCompute(_readoutCompute, _buildKernel,
                    cache.BuildDispatchArguments, 0);
                BindHaloResolve(command, batch, cache);
                command.DispatchCompute(_readoutCompute, _resolveHaloKernel,
                    cache.HaloDispatchArguments, 0);
            }
            return _readBatches.Count != 0;
        }

        private void BindBuild(CommandBuffer command, SigmaCarrierReadBatch batch,
            SegmentReadoutCache cache)
        {
            command.SetComputeIntParam(_readoutCompute, PageCapacityId,
                batch.PageCapacity);
            command.SetComputeBufferParam(_readoutCompute, _buildKernel,
                ExactGateId, _backendGate.Buffer);
            command.SetComputeBufferParam(_readoutCompute, _buildKernel,
                CarrierStateId, batch.State);
            command.SetComputeBufferParam(_readoutCompute, _buildKernel,
                PageMetadataId, batch.Metadata);
            command.SetComputeBufferParam(_readoutCompute, _buildKernel,
                StreamManifestsId, _streamManifests);
            command.SetComputeBufferParam(_readoutCompute, _buildKernel,
                StreamPageVisibilityId, _streamPageVisibility);
            command.SetComputeIntParam(_readoutCompute,
                StreamManifestCapacityId, _streamManifestCapacity);
            command.SetComputeBufferParam(_readoutCompute, _buildKernel,
                ReadoutDirtyFlagsId, batch.ReadoutDirtyFlags);
            command.SetComputeBufferParam(_readoutCompute, _buildKernel,
                ReadoutDirtyPageSlotsId, cache.DirtyPageSlots);
            command.SetComputeBufferParam(_readoutCompute, _buildKernel,
                ReadoutVerticesId, cache.Vertices);
        }

        private void BindCompaction(CommandBuffer command,
            SigmaCarrierReadBatch batch, SegmentReadoutCache cache)
        {
            command.SetComputeIntParam(_readoutCompute, PageCapacityId,
                batch.PageCapacity);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                StreamManifestsId, _streamManifests);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                StreamPageVisibilityId, _streamPageVisibility);
            command.SetComputeIntParam(_readoutCompute,
                StreamManifestCapacityId, _streamManifestCapacity);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                ReadoutDirtyFlagsId, batch.ReadoutDirtyFlags);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                CurrentPageSlotsId, cache.CurrentPageSlots);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                ReadoutDrawArgumentsId, cache.DrawArguments);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                ReadoutDirtyPageSlotsId, cache.DirtyPageSlots);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                ReadoutBuildArgumentsId, cache.BuildDispatchArguments);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                ReadoutHaloArgumentsId, cache.HaloDispatchArguments);
        }

        private void BindHaloResolve(CommandBuffer command,
            SigmaCarrierReadBatch batch, SegmentReadoutCache cache)
        {
            command.SetComputeIntParam(_readoutCompute, PageCapacityId,
                batch.PageCapacity);
            command.SetComputeBufferParam(_readoutCompute, _resolveHaloKernel,
                PageMetadataId, batch.Metadata);
            command.SetComputeBufferParam(_readoutCompute, _resolveHaloKernel,
                StreamManifestsId, _streamManifests);
            command.SetComputeBufferParam(_readoutCompute, _resolveHaloKernel,
                StreamPageVisibilityId, _streamPageVisibility);
            command.SetComputeIntParam(_readoutCompute,
                StreamManifestCapacityId, _streamManifestCapacity);
            command.SetComputeBufferParam(_readoutCompute, _resolveHaloKernel,
                CurrentPageSlotsId, cache.CurrentPageSlots);
            command.SetComputeBufferParam(_readoutCompute, _resolveHaloKernel,
                ReadoutVerticesId, cache.Vertices);
        }

        private void DrawSegments(CommandBuffer command, Matrix4x4 clipFromWorld,
            Matrix4x4 opticalFromWorld, GraphicsBuffer poseResult,
            Matrix4x4 referenceFromWorld, Matrix4x4 worldFromReference)
        {
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                if (batch.SegmentIndex != _streamSegmentIndex)
                    continue;
                SegmentReadoutCache cache = _segmentCaches[index];
                if (!_topology.TryGetSegmentView(batch.SegmentIndex,
                        out SigmaTopologySegmentView topologyView))
                    continue;
                _properties.Clear();
                _properties.SetMatrix(ClipFromWorldId, clipFromWorld);
                _properties.SetMatrix(OpticalFromWorldId, opticalFromWorld);
                _properties.SetMatrix(PoseReferenceFromWorldId,
                    referenceFromWorld);
                _properties.SetMatrix(PoseWorldFromReferenceId,
                    worldFromReference);
                _properties.SetInt(SegmentIndexId, batch.SegmentIndex);
                _properties.SetFloat(ContactFootprintPixelsId, 1.35f);
                _properties.SetBuffer(PoseResultId, poseResult);
                _properties.SetBuffer(ReadoutVerticesId, cache.Vertices);
                _properties.SetBuffer(CurrentPageSlotsId, cache.CurrentPageSlots);
                _properties.SetBuffer(PageMetadataId, batch.Metadata);
                _properties.SetBuffer(TopologyCellFlagsId,
                    topologyView.CellFlags);
                _properties.SetBuffer(TopologyPageKeysId,
                    topologyView.PageKeys);
                command.DrawProceduralIndirect(Matrix4x4.identity,
                    _predictionMaterial, 0, MeshTopology.Triangles,
                    cache.DrawArguments, 0, _properties);
                command.DrawProceduralIndirect(Matrix4x4.identity,
                    _predictionMaterial, 1, MeshTopology.Triangles,
                    cache.DrawArguments, 0, _properties);
            }
        }

        private void EnsureSegmentCaches()
        {
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                if (index < _segmentCaches.Count &&
                    _segmentCaches[index].Matches(batch))
                    continue;
                if (index < _segmentCaches.Count)
                {
                    _segmentCaches[index].Dispose();
                    _segmentCaches[index] = new SegmentReadoutCache(batch);
                }
                else
                    _segmentCaches.Add(new SegmentReadoutCache(batch));
            }
            for (int index = _segmentCaches.Count - 1;
                index >= _readBatches.Count; --index)
            {
                _segmentCaches[index].Dispose();
                _segmentCaches.RemoveAt(index);
            }
        }

        private void SetMrt(SigmaPredictionFrameLease prediction)
        {
            _mrt[0] = new RenderTargetIdentifier(prediction.DepthSupport);
            _mrt[1] = new RenderTargetIdentifier(prediction.CarrierPage);
            _mrt[2] = new RenderTargetIdentifier(prediction.CarrierUvNormal);
            _mrt[3] = new RenderTargetIdentifier(prediction.StateKey);
        }

        public static Matrix4x4 BuildClipFromWorld(GpuImageView view,
            Matrix4x4 opticalFromWorld)
        {
            RigIntrinsics intrinsics = view.Intrinsics;
            Vector2Int resolution = intrinsics.ImageResolution;
            Vector2 nearFar = view.DepthNearFar;
            float rasterFar = RigDepthContract.FiniteRasterFar(nearFar);
            float left = -intrinsics.PrincipalPoint.x /
                intrinsics.FocalLength.x * nearFar.x;
            float right = (resolution.x - intrinsics.PrincipalPoint.x) /
                intrinsics.FocalLength.x * nearFar.x;
            float bottom = -intrinsics.PrincipalPoint.y /
                intrinsics.FocalLength.y * nearFar.x;
            float top = (resolution.y - intrinsics.PrincipalPoint.y) /
                intrinsics.FocalLength.y * nearFar.x;
            Matrix4x4 projection = Matrix4x4.Frustum(left, right, bottom, top,
                nearFar.x, rasterFar);
            Matrix4x4 graphicsFromOptical = Matrix4x4.Scale(
                new Vector3(1f, 1f, -1f));
            return GL.GetGPUProjectionMatrix(projection, true) *
                graphicsFromOptical * opticalFromWorld;
        }

        // Temporary S4-08 diagnostic backend. It submits the already-derived Psi
        // contact footprint through Unity's normal URP renderer registration, the
        // same Quest XR lifecycle proven by the prior PRISM renderer. It owns no
        // geometry and S4-11 replaces it with the meshlet readout.
        private void RenderDirectCarrierPreview()
        {
            if (_scanner == null ||
                _scanner.CurrentRenderMode == ScanRenderMode.None ||
                _previewMaterial == null || _readBatches.Count == 0)
                return;
            Camera main = Camera.main;
            Vector3 boundsCenter = main != null
                ? main.transform.position
                : Vector3.zero;
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                if (batch.SegmentIndex != _streamSegmentIndex)
                    continue;
                SegmentReadoutCache cache = _segmentCaches[index];
                _properties.Clear();
                _properties.SetInt(SegmentIndexId, batch.SegmentIndex);
                _properties.SetFloat(PreviewWireframeId,
                    _scanner.CurrentRenderMode == ScanRenderMode.Wireframe
                        ? 1f : 0f);
                _properties.SetFloat(PreviewContactPixelsId,
                    _scanner.CurrentRenderMode == ScanRenderMode.Wireframe
                        ? 3.5f : 2.5f);
                _properties.SetBuffer(ReadoutVerticesId, cache.Vertices);
                _properties.SetBuffer(CurrentPageSlotsId,
                    cache.CurrentPageSlots);
                _properties.SetBuffer(PageMetadataId, batch.Metadata);
                var renderParams = new RenderParams(_previewMaterial)
                {
                    worldBounds = new Bounds(boundsCenter,
                        Vector3.one * directPreviewBounds),
                    matProps = _properties,
                    receiveShadows = false,
                    shadowCastingMode = ShadowCastingMode.Off,
                    layer = gameObject.layer
                };
                Graphics.RenderPrimitivesIndirect(renderParams,
                    MeshTopology.Triangles, cache.DrawArguments, 1);
            }
        }

        private void OnDestroy()
        {
            _running = false;
            _latest?.Dispose();
            _latest = null;
            SigmaPredictionTargetRing targets = _targets;
            _targets = null;
            GraphicsBuffer identityPoseResult = _identityPoseResult;
            _identityPoseResult = null;
            GraphicsBuffer revalidationContext = _revalidationContext;
            _revalidationContext = null;
            GraphicsBuffer revalidationDrawArguments =
                _revalidationDrawArguments;
            _revalidationDrawArguments = null;
            GraphicsBuffer revalidationPageSnapshot =
                _revalidationPageSnapshot;
            _revalidationPageSnapshot = null;
            GraphicsBuffer[] retiredRevalidationSnapshots =
                _retiredRevalidationSnapshots.ToArray();
            _retiredRevalidationSnapshots.Clear();
            HistoricalRevalidationTargets revalidationTargets =
                _revalidationTargets;
            _revalidationTargets = null;
            HistoricalRevalidationTargets[] retiredTargets =
                _retiredRevalidationTargets.ToArray();
            _retiredRevalidationTargets.Clear();
            SegmentReadoutCache[] segmentCaches = _segmentCaches.ToArray();
            _segmentCaches.Clear();
            Material predictionMaterial = _predictionMaterial;
            _predictionMaterial = null;
            Material previewMaterial = _previewMaterial;
            _previewMaterial = null;
            Material revalidationMaterial = _revalidationMaterial;
            _revalidationMaterial = null;

            void ReleaseOwnedResources()
            {
                targets?.Dispose();
                identityPoseResult?.Dispose();
                revalidationContext?.Dispose();
                revalidationDrawArguments?.Dispose();
                revalidationPageSnapshot?.Dispose();
                for (int index = 0;
                    index < retiredRevalidationSnapshots.Length; ++index)
                    retiredRevalidationSnapshots[index]?.Dispose();
                revalidationTargets?.Dispose();
                for (int index = 0; index < retiredTargets.Length; ++index)
                    retiredTargets[index]?.Dispose();
                for (int index = 0; index < segmentCaches.Length; ++index)
                    segmentCaches[index]?.Dispose();
                DestroyMaterial(predictionMaterial);
                DestroyMaterial(previewMaterial);
                DestroyMaterial(revalidationMaterial);
            }

            try
            {
                SigmaGpuCompletionTicket fence =
                    SigmaGpuCompletion.InsertAfterGraphicsWork();
                SigmaGpuRetirement.Retire(fence, ReleaseOwnedResources,
                    "Sigma renderer teardown");
            }
            catch (Exception exception)
            {
                SigmaGpuRetirement.Quarantine(ReleaseOwnedResources,
                    "Sigma renderer resources", exception.Message);
            }
            _readoutCompute = null;
            _revalidationCompute = null;
            _streamManifests = null;
            _streamPageVisibility = null;
            _streamManifestCapacity = 0;
            _streamSegmentIndex = -1;
            _backendGate = null;
            _topology = null;
            _carrier = null;
            _rigBridge = null;
            _scanner = null;
            _initialized = false;
        }

        private static void DestroyMaterial(Material material)
        {
            if (material == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(material);
            else
                UnityEngine.Object.DestroyImmediate(material);
        }

        private sealed class HistoricalRevalidationTargets : IDisposable
        {
            public HistoricalRevalidationTargets(Vector2Int resolution)
            {
                Resolution = resolution;
                DepthSupport = CreateColor(resolution,
                    GraphicsFormat.R32G32_SFloat,
                    "Sigma historical depth/support");
                CarrierPage = CreateColor(resolution,
                    GraphicsFormat.R32G32B32A32_UInt,
                    "Sigma historical carrier page");
                CarrierUvNormal = CreateColor(resolution,
                    GraphicsFormat.R32G32B32A32_SFloat,
                    "Sigma historical carrier UV/normal");
                StateKey = CreateColor(resolution,
                    GraphicsFormat.R32G32B32A32_UInt,
                    "Sigma historical state key");
                RenderTextureDescriptor depthDescriptor =
                    new(resolution.x, resolution.y)
                    {
                        dimension = TextureDimension.Tex2DArray,
                        volumeDepth = 2,
                        msaaSamples = 1,
                        graphicsFormat = GraphicsFormat.None,
                        depthStencilFormat = GraphicsFormat.D32_SFloat,
                        sRGB = false
                    };
                HardwareDepth = new RenderTexture(depthDescriptor)
                {
                    name = "Sigma historical hardware depth",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!HardwareDepth.Create())
                    throw new InvalidOperationException(
                        "Unable to allocate historical depth target.");
            }

            public Vector2Int Resolution { get; }
            public RenderTexture DepthSupport { get; }
            public RenderTexture CarrierPage { get; }
            public RenderTexture CarrierUvNormal { get; }
            public RenderTexture StateKey { get; }
            public RenderTexture HardwareDepth { get; }

            public void SetMrt(RenderTargetIdentifier[] targets)
            {
                targets[0] = new RenderTargetIdentifier(DepthSupport);
                targets[1] = new RenderTargetIdentifier(CarrierPage);
                targets[2] = new RenderTargetIdentifier(CarrierUvNormal);
                targets[3] = new RenderTargetIdentifier(StateKey);
            }

            public void Dispose()
            {
                DestroyTexture(DepthSupport);
                DestroyTexture(CarrierPage);
                DestroyTexture(CarrierUvNormal);
                DestroyTexture(StateKey);
                DestroyTexture(HardwareDepth);
            }

            private static RenderTexture CreateColor(Vector2Int resolution,
                GraphicsFormat format, string name)
            {
                RenderTextureDescriptor descriptor =
                    new(resolution.x, resolution.y)
                    {
                        dimension = TextureDimension.Tex2DArray,
                        volumeDepth = 2,
                        msaaSamples = 1,
                        graphicsFormat = format,
                        depthStencilFormat = GraphicsFormat.None,
                        sRGB = false,
                        enableRandomWrite = false
                    };
                RenderTexture texture = new(descriptor)
                {
                    name = name,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!texture.Create())
                    throw new InvalidOperationException(
                        $"Unable to allocate {name}.");
                return texture;
            }

            private static void DestroyTexture(RenderTexture texture)
            {
                if (texture == null)
                    return;
                texture.Release();
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(texture);
                else
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private sealed class SegmentReadoutCache : IDisposable
        {
            private readonly GraphicsBuffer _stateIdentity;
            private readonly GraphicsBuffer _metadataIdentity;

            public SegmentReadoutCache(SigmaCarrierReadBatch batch)
            {
                SegmentIndex = batch.SegmentIndex;
                Capacity = batch.PageCapacity;
                _stateIdentity = batch.State;
                _metadataIdentity = batch.Metadata;
                Vertices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    checked(Capacity * ReadoutSamplesPerPage),
                    sizeof(float) * 4)
                {
                    name = $"Sigma readout vertices {SegmentIndex}"
                };
                CurrentPageSlots = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, Capacity, sizeof(uint))
                {
                    name = $"Sigma current page slots {SegmentIndex}"
                };
                DrawArguments = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured |
                    GraphicsBuffer.Target.IndirectArguments,
                    4, sizeof(uint))
                {
                    name = $"Sigma readout draw args {SegmentIndex}"
                };
                DrawArguments.SetData(new uint[] { 0u, 1u, 0u, 0u });
                DirtyPageSlots = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, Capacity, sizeof(uint))
                {
                    name = $"Sigma readout dirty page slots {SegmentIndex}"
                };
                BuildDispatchArguments = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured |
                    GraphicsBuffer.Target.IndirectArguments,
                    3, sizeof(uint))
                {
                    name = $"Sigma readout build args {SegmentIndex}"
                };
                BuildDispatchArguments.SetData(new uint[] { 64u, 0u, 1u });
                HaloDispatchArguments = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured |
                    GraphicsBuffer.Target.IndirectArguments,
                    3, sizeof(uint))
                {
                    name = $"Sigma readout halo args {SegmentIndex}"
                };
                HaloDispatchArguments.SetData(new uint[] { 1u, 0u, 1u });
            }

            public int SegmentIndex { get; }
            public int Capacity { get; }
            public GraphicsBuffer Vertices { get; }
            public GraphicsBuffer CurrentPageSlots { get; }
            public GraphicsBuffer DrawArguments { get; }
            public GraphicsBuffer DirtyPageSlots { get; }
            public GraphicsBuffer BuildDispatchArguments { get; }
            public GraphicsBuffer HaloDispatchArguments { get; }

            public bool Matches(SigmaCarrierReadBatch batch) =>
                SegmentIndex == batch.SegmentIndex &&
                Capacity == batch.PageCapacity &&
                ReferenceEquals(_stateIdentity, batch.State) &&
                ReferenceEquals(_metadataIdentity, batch.Metadata);

            public void Dispose()
            {
                Vertices.Dispose();
                CurrentPageSlots.Dispose();
                DrawArguments.Dispose();
                DirtyPageSlots.Dispose();
                BuildDispatchArguments.Dispose();
                HaloDispatchArguments.Dispose();
            }
        }
    }
}
