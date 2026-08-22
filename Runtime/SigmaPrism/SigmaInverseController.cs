using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// S4-06 joint four-stream inverse readout. Pixel classification, finite-cone
    /// cell construction, exact meet and projective commit witnesses remain on GPU.
    /// The CPU sees only bounded asynchronous page/block scheduling flags required
    /// by the immutable carrier allocator; it never reads pixels or geometry.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SigmaCarrier))]
    [RequireComponent(typeof(SigmaTopologyController))]
    [RequireComponent(typeof(SigmaRenderer))]
    [RequireComponent(typeof(SigmaRigBridge))]
    [DefaultExecutionOrder(0)]
    public sealed class SigmaInverseController : MonoBehaviour, IRoomScanModule
    {
        private const string NormalizeResource = "SigmaPrism/DepthNormalize";
        private const string InverseResource = "SigmaPrism/SigmaInverse";
        private const string ConeLutResource = "SigmaPrism/ConeLut";
        private const string PoseGaugeResource = "SigmaPrism/SigmaPoseGauge";
        private const int SegmentFlagStride = SigmaCarrier.MaximumPagesPerSegment;
        private const int CalibrationStride = 36;
        private const int RgbCalibrationStride = 8;
        private const int ConflictStride = 192;
        private const int MaximumGaugeCommitSlots = 8;
        private static readonly Vector2Int[] GaugeNeighbourOffsets =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1)
        };

        [Header("Bounded asynchronous scheduling")]
        [SerializeField, Range(4096, 131072)] private int conflictCapacity = 32768;
        [SerializeField, Range(1, 8)] private int maxGaugePagesPerCommit = 6;
        [SerializeField, Range(0.005f, 0.1f)]
        private float poseTranslationPriorMetres = 0.03f;
        [SerializeField, Range(0.25f, 5f)]
        private float poseRotationPriorDegrees = 2f;
        [SerializeField, Range(4, 32)] private int poseSampleStride = 16;

        private readonly List<SigmaCarrierReadBatch> _readBatches = new();
        private readonly List<SigmaCarrierPageHandle> _currentPages = new();
        private readonly List<SegmentInverseScratch> _segmentScratch = new();
        private readonly Dictionary<int, SigmaCarrierPageHandle>
            _pageSnapshot = new();
        private readonly Dictionary<GaugeImagePage, uint> _gaugePages = new();
        private readonly List<GaugeImagePage> _gaugePageOrder = new();
        private readonly SigmaPackedQ48[] _calibrationUpload =
            new SigmaPackedQ48[CalibrationStride * 2];
        private readonly SigmaPackedQ48[] _rgbCalibrationUpload =
            new SigmaPackedQ48[RgbCalibrationStride * 2];
        private readonly SigmaPackedQ48[] _posePriorUpload =
            new SigmaPackedQ48[12];

        private RoomScanner _scanner;
        private SigmaCarrier _carrier;
        private SigmaTopologyController _topology;
        private SigmaRenderer _renderer;
        private SigmaRigBridge _rigBridge;
        private SigmaExactBackendGate _backendGate;
        private ComputeShader _normalize;
        private ComputeShader _inverse;
        private ComputeShader _coneLutShader;
        private ComputeShader _poseGaugeCompute;
        private RigCalibration _calibration;
        private RigConeLutSet _coneLuts;
        private SigmaPredictionFrameLease _pendingPrediction;
        private InFlightFrame _inFlight;
        private RenderTexture _metricDepth;
        private RenderTexture _depthFlags;
        private GraphicsBuffer _calibrationQ48;
        private GraphicsBuffer _rgbCalibrationQ48;
        private GraphicsBuffer _rgbViewOperators;
        private GraphicsBuffer _rgbViewSupportScale;
        private SigmaRgbViewCatalog _rgbViewCatalog;
        private SigmaConstraintLedger _proofLedger;
        private SigmaGaugeController _localGauge;
        private GraphicsBuffer _activePageFlags;
        private GraphicsBuffer _commitPageFlags;
        private GraphicsBuffer _unmatchedBlockFlags;
        private GraphicsBuffer _unmatchedBlockAnchors;
        private GraphicsBuffer _conflictRecords;
        private GraphicsBuffer _conflictCount;
        private GraphicsBuffer _frameCounters;
        private GraphicsBuffer _gaugePromotionCounts;
        private GraphicsBuffer _posePrior;
        private GraphicsBuffer _poseResult;
        private GraphicsBuffer _posePartials;
        private int _posePartialCapacity;
        private int _activeFlagCapacity;
        private int _blockFlagCapacity;
        private Vector2Int _scratchResolution;
        private uint _nextRevision = 1u;
        private ulong _nextGaugeOriginOrdinal;
        private bool _running;
        private bool _initialized;
        private bool _disposed;

        private int _normalizeKernel;
        private int _clearKernel;
        private int _classifyKernel;
        private int _compactKernel;
        private int _proposalKernel;
        private int _commitKernel;
        private int _promoteKernel;
        private int _poseBuildKernel;
        private int _poseReduceKernel;

        public string ModuleName => "Sigma joint RGB-D inverse";
        public bool IsInitialized => _initialized && !_disposed;
        public long SubmittedFrames { get; private set; }
        public long CommittedFrames { get; private set; }
        public long DroppedFrames { get; private set; }
        public long FailedFrames { get; private set; }
        public long CommittedPageGenerations { get; private set; }
        public long AllocatedGaugePages { get; private set; }
        public SigmaInverseDiagnosticSnapshot LastDiagnostics { get; private set; }

        public void OnModuleInitialize(RoomScanner scanner)
        {
            if (_initialized)
                return;
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _carrier = scanner.Carrier ?? GetComponent<SigmaCarrier>();
            _topology = scanner.SigmaTopology ??
                GetComponent<SigmaTopologyController>();
            _renderer = scanner.SigmaRenderer ?? GetComponent<SigmaRenderer>();
            _rigBridge = scanner.RigBridge ?? GetComponent<SigmaRigBridge>();
            _backendGate = scanner.ExactBackendGate ?? throw new InvalidOperationException(
                "Sigma inverse requires the GPU-resident exact backend gate.");
            _normalize = Resources.Load<ComputeShader>(NormalizeResource);
            _inverse = Resources.Load<ComputeShader>(InverseResource);
            _coneLutShader = Resources.Load<ComputeShader>(ConeLutResource);
            _poseGaugeCompute = Resources.Load<ComputeShader>(PoseGaugeResource);
            if (_carrier == null || _topology == null || _renderer == null ||
                _rigBridge == null ||
                _normalize == null || _inverse == null || _coneLutShader == null ||
                _poseGaugeCompute == null)
                throw new InvalidOperationException(
                    "Sigma joint inverse resources are incomplete.");
            if (!SystemInfo.supportsAsyncGPUReadback)
                throw new InvalidOperationException(
                    "Sigma immutable-page scheduling requires non-blocking GPU readback support.");

            _normalizeKernel = _normalize.FindKernel("NormalizeStereoDepth");
            _clearKernel = _inverse.FindKernel("ClearInverseFrame");
            _classifyKernel = _inverse.FindKernel("ClassifyDepthFrame");
            _compactKernel = _inverse.FindKernel("CompactActivePages");
            _proposalKernel = _inverse.FindKernel("BuildDepthProposals");
            _commitKernel = _inverse.FindKernel("CommitDepthProposals");
            _promoteKernel = _inverse.FindKernel("PromoteGaugePage");
            _poseBuildKernel = _poseGaugeCompute.FindKernel(
                "BuildPoseGaugePartials");
            _poseReduceKernel = _poseGaugeCompute.FindKernel("ReducePoseGauge");
            _calibrationQ48 = CreateBuffer(CalibrationStride * 2,
                Marshal.SizeOf<SigmaPackedQ48>(), "Sigma depth calibration Q48");
            _rgbCalibrationQ48 = CreateBuffer(RgbCalibrationStride * 2,
                Marshal.SizeOf<SigmaPackedQ48>(), "Sigma RGB calibration Q48");
            _rgbViewCatalog = SigmaRgbViewCatalog.CreateCanonical();
            var operatorUpload = new SigmaPackedQ48[
                _rgbViewCatalog.OperatorRaw.Count];
            for (int index = 0; index < operatorUpload.Length; ++index)
                operatorUpload[index] = SigmaPackedQ48.FromRaw(
                    _rgbViewCatalog.OperatorRaw[index]);
            _rgbViewOperators = CreateBuffer(operatorUpload.Length,
                Marshal.SizeOf<SigmaPackedQ48>(), "Sigma exact RGB view operators");
            _rgbViewOperators.SetData(operatorUpload);
            var scaleUpload = new uint[_rgbViewCatalog.SupportScale.Count];
            for (int index = 0; index < scaleUpload.Length; ++index)
                scaleUpload[index] = _rgbViewCatalog.SupportScale[index];
            _rgbViewSupportScale = CreateBuffer(scaleUpload.Length, sizeof(uint),
                "Sigma RGB view support scales");
            _rgbViewSupportScale.SetData(scaleUpload);
            _conflictRecords = CreateBuffer(conflictCapacity, ConflictStride,
                "Sigma inverse conflict records");
            _conflictCount = CreateBuffer(1, sizeof(uint),
                "Sigma inverse conflict count");
            _frameCounters = CreateBuffer(8, sizeof(uint),
                "Sigma inverse frame counters");
            _gaugePromotionCounts = CreateBuffer(MaximumGaugeCommitSlots,
                sizeof(uint), "Sigma inverse gauge promotion counts");
            _posePrior = CreateBuffer(12, Marshal.SizeOf<SigmaPackedQ48>(),
                "Sigma exact pose prior");
            _poseResult = CreateBuffer(4, sizeof(uint) * 4,
                "Sigma exact pose meet");
            _proofLedger = new SigmaConstraintLedger(
                _carrier.DecodedBudgetPages,
                Math.Max(1024, Math.Min(4096,
                    _carrier.DecodedBudgetPages * 4)), _backendGate);
            _localGauge = new SigmaGaugeController(_carrier, _topology,
                _proofLedger, _backendGate);
            _conflictCount.SetData(new uint[1]);
            _frameCounters.SetData(new uint[8]);
            _gaugePromotionCounts.SetData(new uint[MaximumGaugeCommitSlots]);
            _poseResult.SetData(new uint[16]);
            _renderer.PredictionReady += OnPredictionReady;
            _initialized = true;
        }

        public void OnScanStarted()
        {
            _running = true;
            _nextRevision = DetermineNextRevision();
        }

        public void OnScanStopped()
        {
            _running = false;
            _pendingPrediction?.Dispose();
            _pendingPrediction = null;
            if (_inFlight != null)
                _inFlight.Discard = true;
        }

        private void OnPredictionReady(SigmaPredictionFrameLease prediction)
        {
            if (!_running || !_initialized || prediction == null ||
                prediction.IsDisposed)
                return;
            if (_pendingPrediction != null || _inFlight != null)
            {
                DroppedFrames++;
                return;
            }
            _pendingPrediction = prediction.Retain();
        }

        private void LateUpdate()
        {
            if (!_initialized || _disposed)
                return;
            if (_inFlight != null && _inFlight.AllRequestsDone)
                CompleteInFlight();
            if (!_running || _inFlight != null || _pendingPrediction == null)
                return;

            SigmaPredictionFrameLease prediction = _pendingPrediction;
            _pendingPrediction = null;
            try
            {
                BeginInverse(prediction);
            }
            catch (Exception exception)
            {
                prediction.Dispose();
                FailedFrames++;
                Logger.Error("Sigma inverse submission failed: " + exception.Message);
            }
        }

        private void BeginInverse(SigmaPredictionFrameLease prediction)
        {
            StereoRigFrameLease source = prediction.Source;
            if (!source.IsValid)
                throw new InvalidOperationException("Inverse source lease is invalid.");
            EnsureCalibration(source);
            EnsureFrameResources(source.DepthResolution);
            UploadExactCalibration(source, prediction.PoseGauge);

            _carrier.CollectReadableSegments(_readBatches);
            EnsureSegmentScratch();
            _carrier.CollectCurrentPages(_currentPages);
            _pageSnapshot.Clear();
            for (int index = 0; index < _currentPages.Count; ++index)
            {
                SigmaCarrierPageHandle page = _currentPages[index];
                _pageSnapshot.Add(GlobalPageIndex(page.SegmentIndex, page.PageSlot),
                    page);
            }

            int segmentCount = _readBatches.Count;
            int activeFlagCount = Math.Max(1, segmentCount * SegmentFlagStride);
            Vector2Int blockResolution = new(
                CeilDiv(source.DepthResolution.x, 32),
                CeilDiv(source.DepthResolution.y, 32));
            int blockCount = checked(blockResolution.x * blockResolution.y);
            EnsureFlagBuffers(activeFlagCount, blockCount);

            uint revision = _nextRevision++;
            if (_nextRevision == 0u)
                throw new OverflowException("Sigma world revision exhausted.");
            uint leftKey = IndependenceKey(source.DepthLeft, source.CalibrationEpoch);
            uint rightKey = IndependenceKey(source.DepthRight, source.CalibrationEpoch);
            uint rgbLeftKey = IndependenceKey(source.RgbLeft,
                source.CalibrationEpoch);
            uint rgbRightKey = IndependenceKey(source.RgbRight,
                source.CalibrationEpoch);

            CommandBuffer command = CommandBufferPool.Get(
                "Sigma-PRISM-16 Joint Inverse RGB-D");
            try
            {
                RecordNormalize(command, source);
                RecordPoseGauge(command, source, prediction, revision);
                Graphics.ExecuteCommandBuffer(command);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }

            var pageSnapshot = new Dictionary<int, SigmaCarrierPageHandle>(
                _pageSnapshot);
            Dictionary<SigmaCarrierPageCoordinate, SigmaTopologyEvidenceView>
                topologyEvidence = BuildTopologyEvidenceMap(pageSnapshot,
                    revision, leftKey, rightKey);
            var readbackLatch = new ReadbackRetirementLatch();
            var inFlight = new InFlightFrame(prediction, revision,
                source.DepthResolution, blockResolution, segmentCount,
                leftKey, rightKey, rgbLeftKey, rgbRightKey, pageSnapshot,
                topologyEvidence,
                readbackLatch,
                readbackLatch.Request(_poseResult));
            _inFlight = inFlight;
            SubmittedFrames++;
        }

        private Dictionary<SigmaCarrierPageCoordinate,
            SigmaTopologyEvidenceView> BuildTopologyEvidenceMap(
            Dictionary<int, SigmaCarrierPageHandle> pageSnapshot,
            uint frameSerial, uint leftKey, uint rightKey)
        {
            var evidence = new Dictionary<SigmaCarrierPageCoordinate,
                SigmaTopologyEvidenceView>(pageSnapshot.Count);
            foreach (SigmaCarrierPageHandle page in pageSnapshot.Values)
            {
                SegmentInverseScratch scratch = _segmentScratch[page.SegmentIndex];
                evidence[page.Coordinate] = SigmaTopologyEvidenceView.Proposals(
                    page.Coordinate, scratch.ProposalStatus,
                    scratch.ProposalEpoch, page.PageSlot, scratch.Capacity,
                    frameSerial, leftKey, rightKey);
            }
            return evidence;
        }

        private void CompleteInFlight()
        {
            InFlightFrame frame = _inFlight;
            try
            {
                switch (frame.Phase)
                {
                    case InFlightPhase.PoseGauge:
                        CompletePoseGauge(frame);
                        break;
                    case InFlightPhase.Initial:
                        CompleteInitialReadback(frame);
                        break;
                    case InFlightPhase.MatchedProof:
                        CompleteMatchedProofs(frame);
                        break;
                    case InFlightPhase.LocalGaugeRequest:
                        CompleteLocalGaugeRequest(frame);
                        break;
                    case InFlightPhase.LocalGaugeTransaction:
                        CompleteLocalGaugeTransaction(frame);
                        break;
                    case InFlightPhase.GaugeProof:
                        CompleteGaugePromotions(frame);
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown Sigma inverse completion phase.");
                }
            }
            catch (Exception exception)
            {
                frame.AbortPendingWrites();
                FailedFrames++;
                Logger.Error("Sigma inverse completion failed: " + exception.Message);
                FinishFrame(frame, 0u);
            }
        }

        private void CompletePoseGauge(InFlightFrame frame)
        {
            if (frame.HasError || frame.Discard)
            {
                FailedFrames += frame.HasError ? 1L : 0L;
                FinishFrame(frame, 0u);
                return;
            }
            StereoRigFrameLease source = frame.Prediction.Source;
            SigmaPoseGaugeState gauge = SigmaPoseGaugeState.FromGpu(
                frame.PoseGauge.GetData<uint>(), source.CalibrationEpoch,
                frame.Revision);
            if (gauge.Resolved && !gauge.IsIdentity)
            {
                if (!_renderer.TryRenderPoseGauge(source, gauge,
                        out SigmaPredictionFrameLease corrected))
                    throw new InvalidOperationException(
                        "Accepted pose gauge could not be rasterized.");
                frame.ReplacePrediction(corrected);
            }
            BeginCarrierInverse(frame);
        }

        private void BeginCarrierInverse(InFlightFrame frame)
        {
            SigmaPredictionFrameLease prediction = frame.Prediction;
            StereoRigFrameLease source = prediction.Source;
            UploadExactCalibration(source, prediction.PoseGauge);
            int activeFlagCount = Math.Max(1,
                frame.SegmentCount * SegmentFlagStride);
            int blockCount = checked(frame.BlockResolution.x *
                frame.BlockResolution.y);
            CommandBuffer command = CommandBufferPool.Get(
                "Sigma-PRISM-16 Joint Inverse RGB-D");
            try
            {
                RecordClear(command, activeFlagCount, blockCount);
                BindFrameInputs(command, source, prediction,
                    frame.BlockResolution, frame.SegmentCount,
                    frame.LeftIndependenceKey, frame.RightIndependenceKey,
                    frame.RgbLeftIndependenceKey,
                    frame.RgbRightIndependenceKey, frame.Revision);
                command.DispatchCompute(_inverse, _classifyKernel,
                    CeilDiv(source.DepthResolution.x, 8),
                    CeilDiv(source.DepthResolution.y, 8), 2);
                for (int segment = 0; segment < frame.SegmentCount; ++segment)
                {
                    SigmaCarrierReadBatch batch = _readBatches[segment];
                    SegmentInverseScratch scratch = _segmentScratch[segment];
                    RecordCompactActive(command, batch, scratch, segment);
                    RecordBuildProposals(command, batch, scratch, segment,
                        prediction, frame.Revision);
                }
                Graphics.ExecuteCommandBuffer(command);
            }
            finally { CommandBufferPool.Release(command); }
            frame.BeginInitialReadbacks(_activePageFlags, _commitPageFlags,
                _unmatchedBlockFlags, _unmatchedBlockAnchors, _frameCounters,
                _conflictCount);
        }

        private void CompleteInitialReadback(InFlightFrame frame)
        {
            if (frame.HasError || frame.Discard)
            {
                FailedFrames += frame.HasError ? 1L : 0L;
                FinishFrame(frame, 0u);
                return;
            }
            NativeArray<uint> conflictCountData =
                frame.ConflictCount.GetData<uint>();
            uint producedConflicts = conflictCountData.Length > 0
                ? conflictCountData[0] : uint.MaxValue;
            frame.ProducedConflicts = producedConflicts;
            if (producedConflicts > (uint)conflictCapacity)
            {
                FailedFrames++;
                Logger.Warning(
                    "Sigma inverse evidence capacity exceeded; frame failed closed.");
                FinishFrame(frame, 0u);
                return;
            }

            frame.ProofFrame = _proofLedger.BeginFrame(frame.Prediction.Source,
                frame.Revision, frame.LeftIndependenceKey,
                frame.RightIndependenceKey, frame.RgbLeftIndependenceKey,
                frame.RgbRightIndependenceKey);
            NativeArray<uint> commits = frame.CommitFlags.GetData<uint>();
            int pending = BeginMatchedPageCommits(frame, commits);
            if (pending != 0)
            {
                frame.BeginMatchedProofReadback(_proofLedger.StatusBuffer);
                return;
            }
            ContinueAfterMatched(frame, Array.Empty<SigmaCarrierPageHandle>());
        }

        private void CompleteMatchedProofs(InFlightFrame frame)
        {
            if (frame.HasError || frame.Discard)
            {
                frame.AbortMatchedWrites();
                FailedFrames += frame.HasError ? 1L : 0L;
                FinishFrame(frame, 0u);
                return;
            }
            NativeArray<uint> statuses = frame.ProofStatus.GetData<uint>();
            var pending = new List<PendingMatchedPublication>(
                frame.MatchedWrites);
            pending.Sort(static (left, right) =>
                right.Source.Coordinate.CompareTo(left.Source.Coordinate));
            var published = new List<SigmaCarrierPageHandle>(pending.Count);
            for (int index = 0; index < pending.Count; ++index)
            {
                PendingMatchedPublication item = pending[index];
                SigmaProofPageStatus proof = _proofLedger.ReadStatus(statuses,
                    item.Proof);
                if (!proof.IsValid || !proof.HasMutation)
                {
                    item.Carrier.Dispose();
                    item.Proof.Dispose();
                    continue;
                }
                _proofLedger.ValidateForPublication(item.Proof, proof);
                ResolveTopologyEvidence(frame, item.Source.Coordinate,
                    out SigmaTopologyEvidenceView targetEvidence,
                    out SigmaTopologyEvidenceView rightEvidence,
                    out SigmaTopologyEvidenceView downEvidence);
                SigmaTopologyBuildToken topology = _topology.BuildGeneration(
                    item.Carrier.Handle, item.Source, targetEvidence,
                    rightEvidence, downEvidence);
                SigmaCarrierPageHandle handle = item.Carrier.Publish();
                _proofLedger.Publish(item.Proof, proof);
                topology.Publish();
                item.Carrier.Dispose();
                item.Proof.Dispose();
                published.Add(handle);
                _carrier.TryReleaseRetiredGeneration(item.Source);
                frame.MatchedPageCommits++;
            }
            frame.MatchedWrites.Clear();
            ContinueAfterMatched(frame, published);
        }

        private void ContinueAfterMatched(InFlightFrame frame,
            IReadOnlyList<SigmaCarrierPageHandle> publishedMatched)
        {
            NativeArray<uint> activePages =
                frame.ActivePageFlags.GetData<uint>();
            UpdateObservedTopologyPages(frame, activePages, publishedMatched);
            frame.LocalGaugeSources.Clear();
            for (int index = 0; index < publishedMatched.Count; ++index)
                frame.LocalGaugeSources.Add(publishedMatched[index]);
            frame.LocalGaugeSources.Sort(static (left, right) =>
                left.Coordinate.CompareTo(right.Coordinate));
            if (frame.LocalGaugeSources.Count != 0 &&
                _localGauge.BuildRequests(frame.LocalGaugeSources) != 0)
            {
                frame.BeginLocalGaugeRequest(_localGauge.RequestBuffer);
                return;
            }
            ContinueWithLatentPromotions(frame);
        }

        private void CompleteLocalGaugeRequest(InFlightFrame frame)
        {
            if (frame.Discard || frame.LocalGaugeRequest.hasError ||
                !_localGauge.TryReadBestRequest(
                    frame.LocalGaugeRequest.GetData<uint>(),
                    frame.LocalGaugeSources, out SigmaGaugeSelection selection) ||
                !_carrier.TryGetLatest(selection.Source.Coordinate,
                    out SigmaCarrierPageHandle latest) ||
                !latest.Equals(selection.Source) ||
                GaugeTouchesResidentTransverseNeighbour(selection))
            {
                ContinueWithLatentPromotions(frame);
                return;
            }
            try
            {
                SigmaGaugeTransaction transaction = null;
                try
                {
                    transaction = _localGauge.BeginTransform(selection);
                    SigmaTopologyEvidenceView evidence =
                        SigmaTopologyEvidenceView.GaugeRebuild(
                            transaction.Source.Coordinate, frame.Revision);
                    _localGauge.TransportTopologyPrior(transaction);
                    transaction.Topology = _topology.FinishGaugeGeneration(
                        transaction.Carrier.Handle, evidence);
                    _localGauge.ValidateTopology(transaction);
                    frame.LocalGauge = transaction;
                    transaction = null;
                    frame.BeginLocalGaugeTransaction(_localGauge.StatusBuffer,
                        _localGauge.RawCloneStatusBuffer,
                        frame.LocalGauge.Proof.ClonePlan.Length);
                }
                finally
                {
                    transaction?.Dispose();
                }
            }
            catch (InvalidOperationException exception)
            {
                Logger.Warning("Sigma local gauge skipped: " + exception.Message);
                frame.LocalGauge?.Dispose();
                frame.LocalGauge = null;
                ContinueWithLatentPromotions(frame);
            }
        }

        private bool GaugeTouchesResidentTransverseNeighbour(
            SigmaGaugeSelection selection)
        {
            long dx = selection.Request.Axis == (uint)SigmaGaugeAxis.Y ? 1L : 0L;
            long dy = selection.Request.Axis == (uint)SigmaGaugeAxis.X ? 1L : 0L;
            try
            {
                var negative = new SigmaCarrierPageCoordinate(
                    checked(selection.Source.Coordinate.X - dx),
                    checked(selection.Source.Coordinate.Y - dy));
                var positive = new SigmaCarrierPageCoordinate(
                    checked(selection.Source.Coordinate.X + dx),
                    checked(selection.Source.Coordinate.Y + dy));
                return _carrier.TryGetLatest(negative, out _) ||
                    _carrier.TryGetLatest(positive, out _);
            }
            catch (OverflowException)
            {
                return true;
            }
        }

        private void CompleteLocalGaugeTransaction(InFlightFrame frame)
        {
            SigmaGaugeTransaction transaction = frame.LocalGauge;
            frame.LocalGauge = null;
            if (transaction == null)
            {
                ContinueWithLatentPromotions(frame);
                return;
            }
            try
            {
                if (frame.Discard || frame.LocalGaugeStatus.hasError ||
                    frame.LocalGaugeRawStatus.hasError ||
                    !_carrier.TryGetLatest(transaction.Source.Coordinate,
                        out SigmaCarrierPageHandle latest) ||
                    !latest.Equals(transaction.Source))
                    return;
                SigmaGaugeTransactionStatus status = _localGauge.ReadStatus(
                    frame.LocalGaugeStatus.GetData<uint>(), transaction);
                if (!status.IsValid)
                {
                    Logger.Warning("Sigma local gauge failed closed: " +
                        $"samples={status.TransformedSamples}, " +
                        $"proof={status.ProofBlocks}, failed={status.Failed}.");
                    return;
                }
                NativeArray<uint> raw = frame.LocalGaugeRawStatus.GetData<uint>();
                _localGauge.ValidateProofForPublication(transaction, raw);
                SigmaCarrierPageHandle published = transaction.Carrier.Publish();
                _localGauge.PublishProof(transaction, raw);
                transaction.Topology.Publish();
                transaction.MarkPublished();
                _carrier.TryReleaseRetiredGeneration(transaction.Source);
                frame.LocalGaugePageCommits++;
                RebuildAffectedTopology(frame, new[] { published });
            }
            finally
            {
                transaction.Dispose();
                ContinueWithLatentPromotions(frame);
            }
        }

        private void ContinueWithLatentPromotions(InFlightFrame frame)
        {
            NativeArray<uint> unmatched = frame.UnmatchedBlocks.GetData<uint>();
            NativeArray<uint> anchors = frame.UnmatchedAnchors.GetData<uint>();
            int scheduled = BeginGaugePageCommits(frame, unmatched, anchors);
            if (scheduled != 0)
            {
                frame.BeginGaugeReadback(_gaugePromotionCounts,
                    _proofLedger.StatusBuffer);
                return;
            }
            FinishFrame(frame, 0u);
        }

        private void CompleteGaugePromotions(InFlightFrame frame)
        {
            if (frame.HasError || frame.Discard)
            {
                frame.AbortGaugeWrites();
                FailedFrames += frame.HasError ? 1L : 0L;
                FinishFrame(frame, 0u);
                return;
            }

            NativeArray<uint> counts = frame.GaugePromotions.GetData<uint>();
            NativeArray<uint> proofStatuses = frame.ProofStatus.GetData<uint>();
            uint promotedSamples = 0u;
            int publishedPages = 0;
            var publishedHandles = new List<SigmaCarrierPageHandle>(
                frame.GaugeWrites.Count);
            var pendingGauge = new List<PendingGaugePublication>(
                frame.GaugeWrites.Count);
            for (int index = 0; index < frame.GaugeWrites.Count; ++index)
            {
                PendingGaugeWrite pending = frame.GaugeWrites[index];
                SigmaCarrierWriteLease write = pending.Carrier;
                uint count = (uint)index < (uint)counts.Length ? counts[index] : 0u;
                SigmaProofPageStatus proof = _proofLedger.ReadStatus(
                    proofStatuses, pending.Proof);
                if (count == 0u || !proof.IsValid)
                {
                    write.Dispose();
                    pending.Proof.Dispose();
                    continue;
                }
                pendingGauge.Add(new PendingGaugePublication(write,
                    pending.Proof, proof, count));
            }
            pendingGauge.Sort(static (left, right) =>
                right.Write.Handle.Coordinate.CompareTo(
                    left.Write.Handle.Coordinate));
            for (int index = 0; index < pendingGauge.Count; ++index)
            {
                SigmaCarrierWriteLease write = pendingGauge[index].Write;
                SigmaProofPageLease proofWrite = pendingGauge[index].Proof;
                SigmaProofPageStatus proof = pendingGauge[index].ProofStatus;
                uint count = pendingGauge[index].PromotedSamples;
                frame.TopologyEvidence[write.Handle.Coordinate] =
                    SigmaTopologyEvidenceView.FullStereo(
                        write.Handle.Coordinate, frame.Revision,
                        frame.LeftIndependenceKey, frame.RightIndependenceKey);
                ResolveTopologyEvidence(frame, write.Handle.Coordinate,
                    out SigmaTopologyEvidenceView targetEvidence,
                    out SigmaTopologyEvidenceView rightEvidence,
                    out SigmaTopologyEvidenceView downEvidence);
                SigmaTopologyBuildToken topology = _topology.BuildGeneration(
                    write.Handle, default, targetEvidence, rightEvidence,
                    downEvidence);
                _proofLedger.ValidateForPublication(proofWrite, proof);
                SigmaCarrierPageHandle published = write.Publish();
                _proofLedger.Publish(proofWrite, proof);
                topology.Publish();
                write.Dispose();
                proofWrite.Dispose();
                publishedHandles.Add(published);
                promotedSamples = checked(promotedSamples + count);
                publishedPages++;
            }
            frame.GaugeWrites.Clear();
            RebuildAffectedTopology(frame, publishedHandles);
            frame.PublishedGaugePages = publishedPages;
            FinishFrame(frame, promotedSamples);
        }

        private void FinishFrame(InFlightFrame frame, uint promotedSamples)
        {
            if (!ReferenceEquals(_inFlight, frame))
                return;
            _inFlight = null;
            if (!frame.Discard && !frame.InitialReadbackHasError)
            {
                NativeArray<uint> counters = frame.Counters.GetData<uint>();
                LastDiagnostics = SigmaInverseDiagnosticSnapshot.From(counters,
                    frame.ProducedConflicts, promotedSamples);
            }
            int committedPages = frame.MatchedPageCommits +
                frame.LocalGaugePageCommits + frame.PublishedGaugePages;
            if (committedPages != 0)
                CommittedFrames++;
            CommittedPageGenerations += committedPages;
            AllocatedGaugePages += frame.PublishedGaugePages;
            frame.Dispose();
        }

        private int BeginMatchedPageCommits(InFlightFrame frame,
            NativeArray<uint> commitFlags)
        {
            int committed = 0;
            var changedSources = new List<SigmaCarrierPageHandle>();
            foreach (KeyValuePair<int, SigmaCarrierPageHandle> pair in
                frame.PageSnapshot)
            {
                int global = pair.Key;
                if ((uint)global >= (uint)commitFlags.Length ||
                    commitFlags[global] == 0u)
                    continue;
                SigmaCarrierPageHandle source = pair.Value;
                if (!_carrier.TryGetLatest(source.Coordinate,
                        out SigmaCarrierPageHandle latest) || !latest.Equals(source))
                    continue;
                changedSources.Add(source);
            }
            changedSources.Sort(static (left, right) =>
                left.Coordinate.CompareTo(right.Coordinate));
            for (int index = 0; index < changedSources.Count; ++index)
            {
                SigmaCarrierPageHandle source = changedSources[index];
                SegmentInverseScratch scratch = _segmentScratch[source.SegmentIndex];
                SigmaCarrierReadBatch sourceBatch =
                    _readBatches[source.SegmentIndex];
                SigmaProofPageLease proof = _proofLedger.BeginPage(source,
                    frame.ProofFrame);
                SigmaCarrierWriteLease target = null;
                try
                {
                    target = _carrier.BeginNextGeneration(source.Coordinate,
                        frame.Revision, proof.CertificateOffset,
                        proof.CertificateCount);
                    _proofLedger.Prepare(proof);
                    target.BindWritable(_inverse, _commitKernel,
                        "_TargetCarrierState", "_TargetPageSlot",
                        "_TargetPageCapacity");
                    _proofLedger.BindInverse(_inverse, _commitKernel, proof);
                    _backendGate.Bind(_inverse, _commitKernel);
                    StereoRigFrameLease rig = frame.Prediction.Source;
                    BindCommonInverseInputs(_inverse, _commitKernel, rig,
                        frame.Prediction, frame.LeftIndependenceKey,
                        frame.RightIndependenceKey,
                        frame.RgbLeftIndependenceKey,
                        frame.RgbRightIndependenceKey, frame.Revision);
                    _inverse.SetInts("_Resolution", frame.Resolution.x,
                        frame.Resolution.y);
                    _inverse.SetInt("_SegmentIndex", source.SegmentIndex);
                    _inverse.SetInt("_PageCapacity", sourceBatch.PageCapacity);
                    _inverse.SetInt("_SourcePageSlot", source.PageSlot);
                    _inverse.SetBuffer(_commitKernel, "_PageMetadata",
                        sourceBatch.Metadata);
                    _inverse.SetBuffer(_commitKernel, "_ProposalGeometryRead",
                        scratch.ProposalGeometry);
                    _inverse.SetBuffer(_commitKernel, "_ProposalMassRead",
                        scratch.ProposalMass);
                    _inverse.SetBuffer(_commitKernel, "_ProposalStatusRead",
                        scratch.ProposalStatus);
                    _inverse.Dispatch(_commitKernel,
                        SigmaCarrier.SamplesPerPage / 64, 1, 1);
                    _proofLedger.Reduce(proof, target);
                    frame.MatchedWrites.Add(new PendingMatchedPublication(source,
                        target, proof));
                    target = null;
                    proof = null;
                    committed++;
                }
                finally
                {
                    target?.Dispose();
                    proof?.Dispose();
                }
            }
            return committed;
        }

        private void UpdateObservedTopologyPages(InFlightFrame frame,
            NativeArray<uint> activeFlags,
            IReadOnlyList<SigmaCarrierPageHandle> changedPages)
        {
            var rebuild = new Dictionary<SigmaCarrierPageCoordinate, bool>();
            var changed = new HashSet<SigmaCarrierPageCoordinate>();
            for (int index = 0; index < changedPages.Count; ++index)
                changed.Add(changedPages[index].Coordinate);
            foreach (KeyValuePair<int, SigmaCarrierPageHandle> pair in
                frame.PageSnapshot)
            {
                if ((uint)pair.Key < (uint)activeFlags.Length &&
                    activeFlags[pair.Key] != 0u &&
                    !changed.Contains(pair.Value.Coordinate))
                    AddTopologyRebuild(rebuild, pair.Value.Coordinate, false);
            }
            for (int index = 0; index < changedPages.Count; ++index)
            {
                SigmaCarrierPageCoordinate coordinate =
                    changedPages[index].Coordinate;
                if (TryOffset(coordinate, -1L, 0L,
                        out SigmaCarrierPageCoordinate left) &&
                    !changed.Contains(left))
                    AddTopologyRebuild(rebuild, left, true);
                if (TryOffset(coordinate, 0L, -1L,
                        out SigmaCarrierPageCoordinate up) &&
                    !changed.Contains(up))
                    AddTopologyRebuild(rebuild, up, true);
            }
            RebuildTopologyCoordinates(frame, rebuild);
        }

        private void RebuildAffectedTopology(InFlightFrame frame,
            IReadOnlyList<SigmaCarrierPageHandle> changedPages)
        {
            if (changedPages == null || changedPages.Count == 0)
                return;
            var changed = new HashSet<SigmaCarrierPageCoordinate>();
            for (int index = 0; index < changedPages.Count; ++index)
                changed.Add(changedPages[index].Coordinate);
            var rebuild = new Dictionary<SigmaCarrierPageCoordinate, bool>();
            for (int index = 0; index < changedPages.Count; ++index)
            {
                SigmaCarrierPageCoordinate coordinate =
                    changedPages[index].Coordinate;
                if (TryOffset(coordinate, -1L, 0L,
                        out SigmaCarrierPageCoordinate left) &&
                    !changed.Contains(left))
                    AddTopologyRebuild(rebuild, left, true);
                if (TryOffset(coordinate, 0L, -1L,
                        out SigmaCarrierPageCoordinate up) &&
                    !changed.Contains(up))
                    AddTopologyRebuild(rebuild, up, true);
            }
            RebuildTopologyCoordinates(frame, rebuild);
        }

        private void RebuildTopologyCoordinates(InFlightFrame frame,
            Dictionary<SigmaCarrierPageCoordinate, bool> coordinates)
        {
            var ordered = new List<SigmaCarrierPageCoordinate>(coordinates.Keys);
            ordered.Sort(static (left, right) =>
                right.CompareTo(left));
            for (int index = 0; index < ordered.Count; ++index)
            {
                SigmaCarrierPageCoordinate coordinate = ordered[index];
                if (!_carrier.TryGetLatest(coordinate,
                        out SigmaCarrierPageHandle page))
                    continue;
                ResolveTopologyEvidence(frame, coordinate,
                    out SigmaTopologyEvidenceView targetEvidence,
                    out SigmaTopologyEvidenceView rightEvidence,
                    out SigmaTopologyEvidenceView downEvidence);
                _topology.RebuildCurrent(page, targetEvidence, rightEvidence,
                    downEvidence, coordinates[coordinate]);
            }
        }

        private static void AddTopologyRebuild(
            Dictionary<SigmaCarrierPageCoordinate, bool> rebuild,
            SigmaCarrierPageCoordinate coordinate, bool forceBoundaryTransitions)
        {
            if (rebuild.TryGetValue(coordinate, out bool existing))
                rebuild[coordinate] = existing || forceBoundaryTransitions;
            else
                rebuild.Add(coordinate, forceBoundaryTransitions);
        }

        private static void ResolveTopologyEvidence(InFlightFrame frame,
            SigmaCarrierPageCoordinate coordinate,
            out SigmaTopologyEvidenceView target,
            out SigmaTopologyEvidenceView right,
            out SigmaTopologyEvidenceView down)
        {
            frame.TopologyEvidence.TryGetValue(coordinate, out target);
            right = default;
            down = default;
            if (TryOffset(coordinate, 1L, 0L,
                    out SigmaCarrierPageCoordinate rightCoordinate))
                frame.TopologyEvidence.TryGetValue(rightCoordinate, out right);
            if (TryOffset(coordinate, 0L, 1L,
                    out SigmaCarrierPageCoordinate downCoordinate))
                frame.TopologyEvidence.TryGetValue(downCoordinate, out down);
        }

        private static bool TryOffset(SigmaCarrierPageCoordinate source,
            long deltaX, long deltaY, out SigmaCarrierPageCoordinate result)
        {
            try
            {
                result = new SigmaCarrierPageCoordinate(
                    checked(source.X + deltaX), checked(source.Y + deltaY));
                return true;
            }
            catch (OverflowException)
            {
                result = default;
                return false;
            }
        }

        private int BeginGaugePageCommits(InFlightFrame frame,
            NativeArray<uint> blockFlags, NativeArray<uint> blockAnchors)
        {
            _gaugePages.Clear();
            for (int blockY = 0; blockY < frame.BlockResolution.y; ++blockY)
            {
                for (int blockX = 0; blockX < frame.BlockResolution.x; ++blockX)
                {
                    int blockIndex = blockY * frame.BlockResolution.x + blockX;
                    if ((uint)blockIndex >= (uint)blockFlags.Length ||
                        blockFlags[blockIndex] == 0u)
                        continue;
                    var page = new GaugeImagePage(blockX >> 1, blockY >> 1);
                    uint quadrant = 1u << (((blockY & 1) << 1) | (blockX & 1));
                    _gaugePages.TryGetValue(page, out uint mask);
                    _gaugePages[page] = mask | quadrant;
                }
            }
            if (_gaugePages.Count == 0)
                return 0;

            _gaugePageOrder.Clear();
            foreach (GaugeImagePage page in _gaugePages.Keys)
                _gaugePageOrder.Add(page);
            _gaugePageOrder.Sort();
            if (_gaugePageOrder.Count > maxGaugePagesPerCommit)
                _gaugePageOrder.RemoveRange(maxGaugePagesPerCommit,
                    _gaugePageOrder.Count - maxGaugePagesPerCommit);

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            for (int index = 0; index < _gaugePageOrder.Count; ++index)
            {
                minX = Math.Min(minX, _gaugePageOrder[index].X);
                minY = Math.Min(minY, _gaugePageOrder[index].Y);
            }
            SigmaCarrierPageCoordinate origin = FindFreeGaugeOrigin(frame,
                blockAnchors, _gaugePageOrder, minX, minY);
            int scheduled = 0;
            for (int index = 0; index < _gaugePageOrder.Count; ++index)
            {
                GaugeImagePage imagePage = _gaugePageOrder[index];
                var coordinate = new SigmaCarrierPageCoordinate(
                    checked(origin.X + imagePage.X - minX),
                    checked(origin.Y + imagePage.Y - minY));
                SigmaProofPageLease proof = _proofLedger.BeginPage(default,
                    frame.ProofFrame);
                SigmaCarrierWriteLease target = null;
                try
                {
                    target = _carrier.BeginNullGeneration(coordinate,
                        frame.Revision, proof.CertificateOffset,
                        proof.CertificateCount);
                    _proofLedger.Prepare(proof);
                    BindPromotion(frame, target, proof, imagePage,
                        _gaugePages[imagePage], index);
                    _inverse.Dispatch(_promoteKernel, 8, 8, 1);
                    _proofLedger.Reduce(proof, target);
                    frame.GaugeWrites.Add(new PendingGaugeWrite(target, proof));
                    target = null;
                    proof = null;
                    scheduled++;
                }
                finally
                {
                    target?.Dispose();
                    proof?.Dispose();
                }
            }
            return scheduled;
        }

        private void BindPromotion(InFlightFrame frame,
            SigmaCarrierWriteLease target, SigmaProofPageLease proof,
            GaugeImagePage imagePage,
            uint blockMask, int commitIndex)
        {
            StereoRigFrameLease source = frame.Prediction.Source;
            target.BindWritable(_inverse, _promoteKernel, "_TargetCarrierState",
                "_TargetPageSlot", "_TargetPageCapacity");
            _proofLedger.BindInverse(_inverse, _promoteKernel, proof);
            _backendGate.Bind(_inverse, _promoteKernel);
            BindCommonInverseInputs(_inverse, _promoteKernel, source,
                frame.Prediction, frame.LeftIndependenceKey,
                frame.RightIndependenceKey);
            _inverse.SetInts("_Resolution", frame.Resolution.x, frame.Resolution.y);
            _inverse.SetInts("_GaugePixelOrigin", imagePage.X * 64,
                imagePage.Y * 64);
            _inverse.SetInt("_GaugeBlockMask", unchecked((int)blockMask));
            _inverse.SetInt("_GaugeCommitCapacity", MaximumGaugeCommitSlots);
            _inverse.SetInt("_GaugeCommitIndex", commitIndex);
            _inverse.SetBuffer(_promoteKernel, "_GaugePromotionCounts",
                _gaugePromotionCounts);
        }

        private void RecordNormalize(CommandBuffer command,
            StereoRigFrameLease source)
        {
            command.SetComputeIntParams(_normalize, Id("_Resolution"),
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeVectorParam(_normalize, Id("_NearFar"),
                new Vector4(source.DepthNearFar.x, source.DepthNearFar.y, 0f, 0f));
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                Id("_RawDepth"), source.DepthLeft.Texture);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                Id("_DepthRayCenterLeft"), _coneLuts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                Id("_DepthRayCenterRight"), _coneLuts.DepthRight.CenterRaySolidAngle);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                Id("_MetricDepth"), _metricDepth);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                Id("_DepthFlags"), _depthFlags);
            command.DispatchCompute(_normalize, _normalizeKernel,
                CeilDiv(source.DepthResolution.x, 8),
                CeilDiv(source.DepthResolution.y, 8), 2);
        }

        private void RecordPoseGauge(CommandBuffer command,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            uint revision)
        {
            SigmaPoseGaugeState current = prediction.PoseGauge;
            for (int component = 0; component < 6; ++component)
                _posePriorUpload[component] = SigmaPackedQ48.FromRaw(
                    current.CalibrationEpoch == source.CalibrationEpoch
                        ? current.Raw(component) : 0L);
            long translationWidth = SigmaNumericDomain.Quantize(
                poseTranslationPriorMetres);
            long rotationWidth = SigmaNumericDomain.Quantize(
                poseRotationPriorDegrees * Mathf.Deg2Rad);
            for (int component = 0; component < 6; ++component)
                _posePriorUpload[6 + component] = SigmaPackedQ48.FromRaw(
                    component < 3 ? translationWidth : rotationWidth);
            _posePrior.SetData(_posePriorUpload);
            int sampleWidth = CeilDiv(source.DepthResolution.x,
                poseSampleStride);
            int sampleHeight = CeilDiv(source.DepthResolution.y,
                poseSampleStride);
            int partialCount = CeilDiv(checked(sampleWidth * sampleHeight * 2),
                64);
            EnsurePosePartials(partialCount);

            Pose left = GaugePose(source, source.DepthLeft, current);
            Pose right = GaugePose(source, source.DepthRight, current);
            Matrix4x4 leftWorld = PoseMatrix(left);
            command.SetComputeBufferParam(_poseGaugeCompute, _poseBuildKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_poseGaugeCompute, _poseBuildKernel,
                "_DepthCalibrationQ48", _calibrationQ48);
            command.SetComputeBufferParam(_poseGaugeCompute, _poseBuildKernel,
                "_PosePrior", _posePrior);
            command.SetComputeBufferParam(_poseGaugeCompute, _poseBuildKernel,
                "_PosePartials", _posePartials);
            command.SetComputeTextureParam(_poseGaugeCompute, _poseBuildKernel,
                "_PoseMetricDepth", _metricDepth);
            command.SetComputeTextureParam(_poseGaugeCompute, _poseBuildKernel,
                "_PoseDepthFlags", _depthFlags);
            command.SetComputeTextureParam(_poseGaugeCompute, _poseBuildKernel,
                "_PosePredDepthSupport", prediction.DepthSupport);
            command.SetComputeTextureParam(_poseGaugeCompute, _poseBuildKernel,
                "_PosePredCarrierUvNormal", prediction.CarrierUvNormal);
            command.SetComputeTextureParam(_poseGaugeCompute, _poseBuildKernel,
                "_PoseRayLeft", _coneLuts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_poseGaugeCompute, _poseBuildKernel,
                "_PoseRayRight", _coneLuts.DepthRight.CenterRaySolidAngle);
            command.SetComputeIntParams(_poseGaugeCompute, "_PoseResolution",
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeIntParam(_poseGaugeCompute, "_PoseSampleStride",
                poseSampleStride);
            command.SetComputeIntParam(_poseGaugeCompute, "_PoseRevision",
                unchecked((int)revision));
            command.SetComputeIntParam(_poseGaugeCompute, "_PosePartialCount",
                partialCount);
            command.SetComputeVectorParam(_poseGaugeCompute, "_PosePriorFloat",
                new Vector4(poseTranslationPriorMetres,
                    poseRotationPriorDegrees * Mathf.Deg2Rad, 0.00025f, 0f));
            command.SetComputeMatrixParam(_poseGaugeCompute,
                "_PoseWorldFromOpticalLeft", leftWorld);
            command.SetComputeMatrixParam(_poseGaugeCompute,
                "_PoseWorldFromOpticalRight", PoseMatrix(right));
            command.SetComputeMatrixParam(_poseGaugeCompute,
                "_PoseReferenceFromWorld",
                PoseMatrix(source.DepthLeft.WorldFromCamera).inverse);
            command.DispatchCompute(_poseGaugeCompute, _poseBuildKernel,
                partialCount, 1, 1);
            command.SetComputeBufferParam(_poseGaugeCompute, _poseReduceKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_poseGaugeCompute, _poseReduceKernel,
                "_PosePrior", _posePrior);
            command.SetComputeBufferParam(_poseGaugeCompute, _poseReduceKernel,
                "_PosePartials", _posePartials);
            command.SetComputeBufferParam(_poseGaugeCompute, _poseReduceKernel,
                "_PoseResult", _poseResult);
            command.DispatchCompute(_poseGaugeCompute, _poseReduceKernel, 1, 1, 1);
        }

        private void EnsurePosePartials(int partialCount)
        {
            if (_posePartials != null && _posePartialCapacity >= partialCount)
                return;
            _posePartials?.Dispose();
            _posePartialCapacity = Math.Max(1, partialCount);
            _posePartials = CreateBuffer(checked(_posePartialCapacity * 7),
                sizeof(uint) * 4, "Sigma pose partial meets");
        }

        private void RecordClear(CommandBuffer command, int activeFlagCount,
            int blockCount)
        {
            int clearCount = Math.Max(Math.Max(activeFlagCount, blockCount), 8);
            command.SetComputeIntParam(_inverse, "_ActiveFlagCount",
                activeFlagCount);
            command.SetComputeIntParam(_inverse, "_BlockFlagCount", blockCount);
            command.SetComputeIntParam(_inverse, "_GaugeCommitCapacity",
                MaximumGaugeCommitSlots);
            command.SetComputeIntParam(_inverse, "_ClearCount", clearCount);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_ActivePageFlags", _activePageFlags);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_CommitPageFlags", _commitPageFlags);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_UnmatchedBlockFlags", _unmatchedBlockFlags);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_UnmatchedBlockAnchors", _unmatchedBlockAnchors);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_ConflictCount", _conflictCount);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_FrameCounters", _frameCounters);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_GaugePromotionCounts", _gaugePromotionCounts);
            command.DispatchCompute(_inverse, _clearKernel,
                CeilDiv(clearCount, 64), 1, 1);
        }

        private void BindFrameInputs(CommandBuffer command,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            Vector2Int blockResolution, int segmentCount, uint leftKey,
            uint rightKey, uint rgbLeftKey, uint rgbRightKey, uint frameSerial)
        {
            command.SetComputeIntParams(_inverse, Id("_Resolution"),
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeIntParams(_inverse, Id("_GaugeBlockResolution"),
                blockResolution.x, blockResolution.y);
            command.SetComputeIntParam(_inverse, "_SegmentCount", segmentCount);
            command.SetComputeIntParam(_inverse, "_LeftIndependenceKey",
                unchecked((int)leftKey));
            command.SetComputeIntParam(_inverse, "_RightIndependenceKey",
                unchecked((int)rightKey));
            command.SetComputeIntParam(_inverse, "_RgbLeftIndependenceKey",
                unchecked((int)rgbLeftKey));
            command.SetComputeIntParam(_inverse, "_RgbRightIndependenceKey",
                unchecked((int)rgbRightKey));
            command.SetComputeIntParam(_inverse, "_RgbPhase",
                unchecked((int)(frameSerial & 15u)));
            command.SetComputeIntParams(_inverse, Id("_RgbResolutionLeft"),
                source.RgbLeft.Resolution.x, source.RgbLeft.Resolution.y);
            command.SetComputeIntParams(_inverse, Id("_RgbResolutionRight"),
                source.RgbRight.Resolution.x, source.RgbRight.Resolution.y);
            SetFrameMatrices(command, source, prediction.PoseGauge);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_DepthCalibrationQ48", _calibrationQ48);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_MetricDepth"), _metricDepth);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_DepthFlags"), _depthFlags);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_PredDepthSupport"), prediction.DepthSupport);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_PredStateKey"), prediction.StateKey);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_DepthRayCenterLeft"), _coneLuts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_DepthRayCenterRight"), _coneLuts.DepthRight.CenterRaySolidAngle);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_DepthSlopeBoundsLeft"), _coneLuts.DepthLeft.SlopeBounds);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_DepthSlopeBoundsRight"), _coneLuts.DepthRight.SlopeBounds);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_ActivePageFlags", _activePageFlags);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_UnmatchedBlockFlags", _unmatchedBlockFlags);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_UnmatchedBlockAnchors", _unmatchedBlockAnchors);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_FrameCounters", _frameCounters);
        }

        private void RecordCompactActive(CommandBuffer command,
            SigmaCarrierReadBatch batch, SegmentInverseScratch scratch, int segment)
        {
            command.SetComputeIntParam(_inverse, "_PageCapacity", batch.PageCapacity);
            command.SetComputeIntParam(_inverse, "_SegmentFlagOffset",
                segment * SegmentFlagStride);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_ActivePageFlagsRead", _activePageFlags);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_ActivePageSlots", scratch.ActivePageSlots);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_ActiveDispatchArgs", scratch.ActiveDispatchArguments);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_FrameCounters", _frameCounters);
            command.DispatchCompute(_inverse, _compactKernel, 1, 1, 1);
        }

        private void RecordBuildProposals(CommandBuffer command,
            SigmaCarrierReadBatch batch, SegmentInverseScratch scratch, int segment,
            SigmaPredictionFrameLease prediction, uint frameSerial)
        {
            command.SetComputeIntParam(_inverse, "_PageCapacity", batch.PageCapacity);
            command.SetComputeIntParam(_inverse, "_SegmentIndex", segment);
            command.SetComputeIntParam(_inverse, "_SegmentFlagOffset",
                segment * SegmentFlagStride);
            command.SetComputeIntParam(_inverse, "_ConflictCapacity",
                conflictCapacity);
            command.SetComputeIntParam(_inverse, "_ProposalFrameSerial",
                unchecked((int)frameSerial));
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_DepthCalibrationQ48", _calibrationQ48);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_RgbCalibrationQ48", _rgbCalibrationQ48);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_RgbViewOperators", _rgbViewOperators);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_RgbViewSupportScale", _rgbViewSupportScale);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_CarrierState", batch.State);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_PageMetadata", batch.Metadata);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_CurrentFlags", batch.CurrentFlags);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ActivePageSlotsRead", scratch.ActivePageSlots);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ProposalGeometry", scratch.ProposalGeometry);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ProposalMass", scratch.ProposalMass);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ProposalStatus", scratch.ProposalStatus);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ProposalEpoch", scratch.ProposalEpoch);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_CommitPageFlags", _commitPageFlags);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ConflictRecords", _conflictRecords);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ConflictCount", _conflictCount);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_FrameCounters", _frameCounters);
            _proofLedger.BindReadOnly(command, _inverse, _proposalKernel);
            BindFrameTextures(command, _proposalKernel, prediction);
            command.DispatchCompute(_inverse, _proposalKernel,
                scratch.ActiveDispatchArguments, 0);
        }

        private void BindFrameTextures(CommandBuffer command, int kernel,
            SigmaPredictionFrameLease prediction)
        {
            StereoRigFrameLease source = prediction.Source;
            command.SetComputeTextureParam(_inverse, kernel, Id("_MetricDepth"),
                _metricDepth);
            command.SetComputeTextureParam(_inverse, kernel, Id("_DepthFlags"),
                _depthFlags);
            command.SetComputeTextureParam(_inverse, kernel, Id("_PredDepthSupport"),
                prediction.DepthSupport);
            command.SetComputeTextureParam(_inverse, kernel, Id("_PredCarrierPage"),
                prediction.CarrierPage);
            command.SetComputeTextureParam(_inverse, kernel,
                Id("_PredCarrierUvNormal"), prediction.CarrierUvNormal);
            command.SetComputeTextureParam(_inverse, kernel, Id("_PredStateKey"),
                prediction.StateKey);
            command.SetComputeTextureParam(_inverse, kernel,
                Id("_DepthSlopeBoundsLeft"), _coneLuts.DepthLeft.SlopeBounds);
            command.SetComputeTextureParam(_inverse, kernel,
                Id("_DepthSlopeBoundsRight"), _coneLuts.DepthRight.SlopeBounds);
            command.SetComputeTextureParam(_inverse, kernel, Id("_RgbLeft"),
                source.RgbLeft.Texture);
            command.SetComputeTextureParam(_inverse, kernel, Id("_RgbRight"),
                source.RgbRight.Texture);
        }

        private void BindCommonInverseInputs(ComputeShader shader, int kernel,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            uint leftKey, uint rightKey, uint rgbLeftKey = 0u,
            uint rgbRightKey = 0u, uint frameSerial = 0u)
        {
            shader.SetBuffer(kernel, "_DepthCalibrationQ48", _calibrationQ48);
            shader.SetBuffer(kernel, "_RgbCalibrationQ48", _rgbCalibrationQ48);
            shader.SetBuffer(kernel, "_RgbViewOperators", _rgbViewOperators);
            shader.SetBuffer(kernel, "_RgbViewSupportScale", _rgbViewSupportScale);
            _proofLedger.BindReadOnly(shader, kernel);
            shader.SetTexture(kernel, "_MetricDepth", _metricDepth);
            shader.SetTexture(kernel, "_DepthFlags", _depthFlags);
            shader.SetTexture(kernel, "_PredDepthSupport", prediction.DepthSupport);
            shader.SetTexture(kernel, "_PredCarrierPage", prediction.CarrierPage);
            shader.SetTexture(kernel, "_PredCarrierUvNormal",
                prediction.CarrierUvNormal);
            shader.SetTexture(kernel, "_PredStateKey", prediction.StateKey);
            shader.SetTexture(kernel, "_DepthRayCenterLeft",
                _coneLuts.DepthLeft.CenterRaySolidAngle);
            shader.SetTexture(kernel, "_DepthRayCenterRight",
                _coneLuts.DepthRight.CenterRaySolidAngle);
            shader.SetTexture(kernel, "_DepthSlopeBoundsLeft",
                _coneLuts.DepthLeft.SlopeBounds);
            shader.SetTexture(kernel, "_DepthSlopeBoundsRight",
                _coneLuts.DepthRight.SlopeBounds);
            shader.SetTexture(kernel, "_RgbLeft", source.RgbLeft.Texture);
            shader.SetTexture(kernel, "_RgbRight", source.RgbRight.Texture);
            shader.SetInt("_LeftIndependenceKey", unchecked((int)leftKey));
            shader.SetInt("_RightIndependenceKey", unchecked((int)rightKey));
            shader.SetInt("_RgbLeftIndependenceKey", unchecked((int)rgbLeftKey));
            shader.SetInt("_RgbRightIndependenceKey", unchecked((int)rgbRightKey));
            shader.SetInt("_RgbPhase", unchecked((int)(frameSerial & 15u)));
            shader.SetInts("_RgbResolutionLeft", source.RgbLeft.Resolution.x,
                source.RgbLeft.Resolution.y);
            shader.SetInts("_RgbResolutionRight", source.RgbRight.Resolution.x,
                source.RgbRight.Resolution.y);
            SetFrameMatrices(shader, source, prediction.PoseGauge);
        }

        private void EnsureCalibration(StereoRigFrameLease source)
        {
            if (_calibration != null && _calibration.IsCompatible(source))
                return;
            if (!RigCalibration.TryCreate(source, out RigCalibration calibration))
                throw new InvalidOperationException("Unable to freeze inverse rig calibration.");
            _coneLuts?.Retire();
            _calibration = calibration;
            _coneLuts = RigConeLutSet.Create(_coneLutShader, calibration);
        }

        private void EnsureFrameResources(Vector2Int resolution)
        {
            if (_scratchResolution == resolution && _metricDepth != null &&
                _depthFlags != null)
                return;
            DestroyTexture(_metricDepth);
            DestroyTexture(_depthFlags);
            _metricDepth = CreateArrayTexture("Sigma metric depth", resolution,
                GraphicsFormat.R32G32_SFloat);
            _depthFlags = CreateArrayTexture("Sigma depth flags", resolution,
                GraphicsFormat.R32_UInt);
            _scratchResolution = resolution;
        }

        private void EnsureFlagBuffers(int activeFlagCount, int blockCount)
        {
            if (_activeFlagCapacity < activeFlagCount)
            {
                _activePageFlags?.Dispose();
                _commitPageFlags?.Dispose();
                _activeFlagCapacity = NextPowerOfTwo(activeFlagCount);
                _activePageFlags = CreateBuffer(_activeFlagCapacity, sizeof(uint),
                    "Sigma active page flags");
                _commitPageFlags = CreateBuffer(_activeFlagCapacity, sizeof(uint),
                    "Sigma commit page flags");
            }
            if (_blockFlagCapacity < blockCount)
            {
                _unmatchedBlockFlags?.Dispose();
                _unmatchedBlockAnchors?.Dispose();
                _blockFlagCapacity = NextPowerOfTwo(blockCount);
                _unmatchedBlockFlags = CreateBuffer(_blockFlagCapacity,
                    sizeof(uint), "Sigma unmatched gauge blocks");
                _unmatchedBlockAnchors = CreateBuffer(_blockFlagCapacity,
                    sizeof(uint), "Sigma unmatched gauge anchors");
            }
        }

        private void EnsureSegmentScratch()
        {
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                if (index < _segmentScratch.Count &&
                    _segmentScratch[index].Matches(batch))
                    continue;
                if (index < _segmentScratch.Count)
                {
                    _segmentScratch[index].Dispose();
                    _segmentScratch[index] = new SegmentInverseScratch(batch);
                }
                else
                    _segmentScratch.Add(new SegmentInverseScratch(batch));
            }
            for (int index = _segmentScratch.Count - 1;
                index >= _readBatches.Count; --index)
            {
                _segmentScratch[index].Dispose();
                _segmentScratch.RemoveAt(index);
            }
        }

        private void UploadExactCalibration(StereoRigFrameLease source,
            SigmaPoseGaugeState gauge)
        {
            FillCalibration(0, source.DepthLeft,
                GaugePose(source, source.DepthLeft, gauge), source.Health);
            FillCalibration(1, source.DepthRight,
                GaugePose(source, source.DepthRight, gauge), source.Health);
            _calibrationQ48.SetData(_calibrationUpload);
            FillRgbCalibration(0, GaugePose(source, source.RgbLeft, gauge),
                source.Health);
            FillRgbCalibration(1, GaugePose(source, source.RgbRight, gauge),
                source.Health);
            _rgbCalibrationQ48.SetData(_rgbCalibrationUpload);
        }

        private void FillRgbCalibration(int eye, Pose pose,
            RigPairingHealth health)
        {
            int offset = eye * RgbCalibrationStride;
            SetRgbQ(offset + 0, pose.position.x);
            SetRgbQ(offset + 1, pose.position.y);
            SetRgbQ(offset + 2, pose.position.z);
            SetRgbQRaw(offset + 3, SigmaNumericDomain.FromRatio(2, 255));
            SetRgbQRaw(offset + 4, SigmaNumericDomain.FromRatio(1, 64));
            double clockWidth = Math.Min(0.01,
                health.ClockUncertaintyNanoseconds * 1e-9 * 0.5);
            SetRgbQ(offset + 5, Math.Max(0.0005, clockWidth));
            SetRgbQRaw(offset + 6, SigmaNumericDomain.FromRatio(1, 255));
            SetRgbQRaw(offset + 7, 0L);
        }

        private void FillCalibration(int eye, GpuImageView view, Pose pose,
            RigPairingHealth health)
        {
            int offset = eye * CalibrationStride;
            SetQ(offset + 0, view.Intrinsics.FocalLength.x);
            SetQ(offset + 1, view.Intrinsics.FocalLength.y);
            SetQ(offset + 2, view.Intrinsics.PrincipalPoint.x);
            SetQ(offset + 3, view.Intrinsics.PrincipalPoint.y);
            Matrix4x4 world = PoseMatrix(pose);
            int cursor = offset + 4;
            for (int row = 0; row < 3; ++row)
            {
                for (int column = 0; column < 3; ++column)
                    SetQ(cursor++, world[row, column]);
            }
            SetQ(offset + 13, world[0, 3]);
            SetQ(offset + 14, world[1, 3]);
            SetQ(offset + 15, world[2, 3]);
            SetQ(offset + 16, view.DepthNearFar.x);
            SetQ(offset + 17, RigDepthContract.FiniteRasterFar(view.DepthNearFar));
            double clockWidth = Math.Min(0.01,
                health.ClockUncertaintyNanoseconds * 1e-9 * 0.5);
            SetQ(offset + 18, Math.Max(0.001, clockWidth));
            double[] thresholds = { 0.5, 1.0, 2.0, 3.0, 5.0, 32767.0 };
            double[] widths = { 0.003, 0.0045, 0.007, 0.012, 0.025, 0.05 };
            for (int bin = 0; bin < 6; ++bin)
            {
                SetQ(offset + 19 + bin, thresholds[bin]);
                SetQ(offset + 25 + bin, widths[bin]);
            }
            SetQ(offset + 31, 0.001);
            SetQ(offset + 32, 0.05);
            SetQRaw(offset + 33, SigmaNumericDomain.FromRatio(1, 64));
            SetQRaw(offset + 34, 0L);
            SetQRaw(offset + 35, 0L);
        }

        private void SetQ(int index, double value) =>
            SetQRaw(index, SigmaNumericDomain.Quantize(value));

        private void SetQRaw(int index, long raw) =>
            _calibrationUpload[index] = SigmaPackedQ48.FromRaw(raw);

        private void SetRgbQ(int index, double value) =>
            SetRgbQRaw(index, SigmaNumericDomain.Quantize(value));

        private void SetRgbQRaw(int index, long raw) =>
            _rgbCalibrationUpload[index] = SigmaPackedQ48.FromRaw(raw);

        private void SetFrameMatrices(CommandBuffer command,
            StereoRigFrameLease source, SigmaPoseGaugeState gauge)
        {
            Matrix4x4 leftWorld = PoseMatrix(GaugePose(source,
                source.DepthLeft, gauge));
            Matrix4x4 rightWorld = PoseMatrix(GaugePose(source,
                source.DepthRight, gauge));
            command.SetComputeMatrixParam(_inverse, Id("_WorldFromOpticalLeft"), leftWorld);
            command.SetComputeMatrixParam(_inverse, Id("_WorldFromOpticalRight"), rightWorld);
            command.SetComputeMatrixParam(_inverse, Id("_OpticalFromWorldLeft"),
                leftWorld.inverse);
            command.SetComputeMatrixParam(_inverse, Id("_OpticalFromWorldRight"),
                rightWorld.inverse);
            command.SetComputeVectorParam(_inverse, Id("_DepthIntrinsicsLeft"),
                IntrinsicsVector(source.DepthLeft.Intrinsics));
            command.SetComputeVectorParam(_inverse, Id("_DepthIntrinsicsRight"),
                IntrinsicsVector(source.DepthRight.Intrinsics));
            Matrix4x4 rgbLeftWorld = PoseMatrix(GaugePose(source,
                source.RgbLeft, gauge));
            Matrix4x4 rgbRightWorld = PoseMatrix(GaugePose(source,
                source.RgbRight, gauge));
            command.SetComputeMatrixParam(_inverse,
                Id("_RgbOpticalFromWorldLeft"), rgbLeftWorld.inverse);
            command.SetComputeMatrixParam(_inverse,
                Id("_RgbOpticalFromWorldRight"), rgbRightWorld.inverse);
            command.SetComputeVectorParam(_inverse, Id("_RgbIntrinsicsLeft"),
                IntrinsicsVector(source.RgbLeft.Intrinsics));
            command.SetComputeVectorParam(_inverse, Id("_RgbIntrinsicsRight"),
                IntrinsicsVector(source.RgbRight.Intrinsics));
        }

        private void SetFrameMatrices(ComputeShader shader,
            StereoRigFrameLease source, SigmaPoseGaugeState gauge)
        {
            Matrix4x4 leftWorld = PoseMatrix(GaugePose(source,
                source.DepthLeft, gauge));
            Matrix4x4 rightWorld = PoseMatrix(GaugePose(source,
                source.DepthRight, gauge));
            shader.SetMatrix("_WorldFromOpticalLeft", leftWorld);
            shader.SetMatrix("_WorldFromOpticalRight", rightWorld);
            shader.SetMatrix("_OpticalFromWorldLeft", leftWorld.inverse);
            shader.SetMatrix("_OpticalFromWorldRight", rightWorld.inverse);
            shader.SetVector("_DepthIntrinsicsLeft",
                IntrinsicsVector(source.DepthLeft.Intrinsics));
            shader.SetVector("_DepthIntrinsicsRight",
                IntrinsicsVector(source.DepthRight.Intrinsics));
            Matrix4x4 rgbLeftWorld = PoseMatrix(GaugePose(source,
                source.RgbLeft, gauge));
            Matrix4x4 rgbRightWorld = PoseMatrix(GaugePose(source,
                source.RgbRight, gauge));
            shader.SetMatrix("_RgbOpticalFromWorldLeft", rgbLeftWorld.inverse);
            shader.SetMatrix("_RgbOpticalFromWorldRight", rgbRightWorld.inverse);
            shader.SetVector("_RgbIntrinsicsLeft",
                IntrinsicsVector(source.RgbLeft.Intrinsics));
            shader.SetVector("_RgbIntrinsicsRight",
                IntrinsicsVector(source.RgbRight.Intrinsics));
        }

        private static Pose GaugePose(StereoRigFrameLease source,
            GpuImageView view, SigmaPoseGaugeState gauge) =>
            gauge.CalibrationEpoch == source.CalibrationEpoch
                ? gauge.Apply(source.DepthLeft.WorldFromCamera,
                    view.WorldFromCamera)
                : view.WorldFromCamera;

        private static Matrix4x4 PoseMatrix(Pose pose) => Matrix4x4.TRS(
            pose.position, pose.rotation, Vector3.one);

        private static Vector4 IntrinsicsVector(RigIntrinsics intrinsics) => new(
            intrinsics.FocalLength.x, intrinsics.FocalLength.y,
            intrinsics.PrincipalPoint.x, intrinsics.PrincipalPoint.y);

        private SigmaCarrierPageCoordinate FindFreeGaugeOrigin(
            InFlightFrame frame, NativeArray<uint> blockAnchors,
            IReadOnlyList<GaugeImagePage> pages, int minX, int minY)
        {
            if (TryFindAdjacentGaugeOrigin(frame, blockAnchors, pages, minX,
                    minY, out SigmaCarrierPageCoordinate adjacent))
                return adjacent;

            for (int attempt = 0; attempt < 1_000_000; ++attempt)
            {
                SigmaCarrierPageCoordinate candidate = SignedMortonCoordinate(
                    _nextGaugeOriginOrdinal++);
                if (GaugeRegionIsFree(candidate, pages, minX, minY))
                    return candidate;
            }
            throw new InvalidOperationException(
                "Unable to place a deterministic latent gauge page group.");
        }

        private bool TryFindAdjacentGaugeOrigin(InFlightFrame frame,
            NativeArray<uint> blockAnchors, IReadOnlyList<GaugeImagePage> pages,
            int minX, int minY, out SigmaCarrierPageCoordinate origin)
        {
            for (int pageIndex = 0; pageIndex < pages.Count; ++pageIndex)
            {
                GaugeImagePage imagePage = pages[pageIndex];
                uint mask = _gaugePages[imagePage];
                for (int quadrant = 0; quadrant < 4; ++quadrant)
                {
                    if ((mask & (1u << quadrant)) == 0u)
                        continue;
                    int blockX = imagePage.X * 2 + (quadrant & 1);
                    int blockY = imagePage.Y * 2 + (quadrant >> 1);
                    if ((uint)blockX >= (uint)frame.BlockResolution.x ||
                        (uint)blockY >= (uint)frame.BlockResolution.y)
                        continue;
                    int blockIndex = blockY * frame.BlockResolution.x + blockX;
                    if ((uint)blockIndex >= (uint)blockAnchors.Length)
                        continue;
                    uint globalAnchor = blockAnchors[blockIndex];
                    if (globalAnchor == uint.MaxValue ||
                        !frame.PageSnapshot.TryGetValue(
                            unchecked((int)globalAnchor), out SigmaCarrierPageHandle anchor))
                        continue;

                    for (int neighbourIndex = 0;
                        neighbourIndex < GaugeNeighbourOffsets.Length;
                        ++neighbourIndex)
                    {
                        Vector2Int offset = GaugeNeighbourOffsets[neighbourIndex];
                        var candidate = new SigmaCarrierPageCoordinate(
                            checked(anchor.Coordinate.X + offset.x -
                                (imagePage.X - minX)),
                            checked(anchor.Coordinate.Y + offset.y -
                                (imagePage.Y - minY)));
                        if (!GaugeRegionIsFree(candidate, pages, minX, minY))
                            continue;
                        origin = candidate;
                        return true;
                    }
                }
            }
            origin = default;
            return false;
        }

        private bool GaugeRegionIsFree(SigmaCarrierPageCoordinate origin,
            IReadOnlyList<GaugeImagePage> pages, int minX, int minY)
        {
            for (int index = 0; index < pages.Count; ++index)
            {
                var coordinate = new SigmaCarrierPageCoordinate(
                    checked(origin.X + pages[index].X - minX),
                    checked(origin.Y + pages[index].Y - minY));
                if (_carrier.TryGetLatest(coordinate, out _))
                    return false;
            }
            return true;
        }

        internal static SigmaCarrierPageCoordinate SignedMortonCoordinate(
            ulong ordinal)
        {
            uint x = CompactMorton(ordinal);
            uint y = CompactMorton(ordinal >> 1);
            return new SigmaCarrierPageCoordinate(ZigZagDecode(x), ZigZagDecode(y));
        }

        private static uint CompactMorton(ulong value)
        {
            value &= 0x5555_5555_5555_5555UL;
            value = (value | value >> 1) & 0x3333_3333_3333_3333UL;
            value = (value | value >> 2) & 0x0f0f_0f0f_0f0f_0f0fUL;
            value = (value | value >> 4) & 0x00ff_00ff_00ff_00ffUL;
            value = (value | value >> 8) & 0x0000_ffff_0000_ffffUL;
            value = (value | value >> 16) & 0x0000_0000_ffff_ffffUL;
            return (uint)value;
        }

        private static long ZigZagDecode(uint value) =>
            (long)(value >> 1) ^ -((long)value & 1L);

        private uint DetermineNextRevision()
        {
            _carrier.CollectCurrentPages(_currentPages);
            uint maximum = 0u;
            for (int index = 0; index < _currentPages.Count; ++index)
                maximum = Math.Max(maximum, _currentPages[index].Revision);
            return checked(maximum + 1u);
        }

        private static uint IndependenceKey(GpuImageView view, uint epoch)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, epoch);
                Mix(ref hash, (uint)view.Eye + 1u);
                Vector3 position = view.WorldFromCamera.position;
                Quaternion rotation = view.WorldFromCamera.rotation;
                Mix(ref hash, (uint)Mathf.RoundToInt(position.x * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(position.y * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(position.z * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(rotation.x * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(rotation.y * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(rotation.z * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(rotation.w * 64f));
                return hash == 0u ? 1u : hash;
            }
        }

        private static void Mix(ref uint hash, uint value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 16777619u;
            }
        }

        private static int GlobalPageIndex(int segment, int slot) =>
            checked(segment * SegmentFlagStride + slot);

        private static int Id(string name) => Shader.PropertyToID(name);

        private static RenderTexture CreateArrayTexture(string name,
            Vector2Int resolution, GraphicsFormat format)
        {
            if (!SystemInfo.IsFormatSupported(format,
                    GraphicsFormatUsage.LoadStore))
                throw new InvalidOperationException(
                    $"Required inverse texture format unsupported: {format}.");
            var descriptor = new RenderTextureDescriptor(resolution.x, resolution.y)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };
            var texture = new RenderTexture(descriptor)
            {
                name = $"[Sigma-PRISM-16] {name}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.Create())
                throw new InvalidOperationException($"Unable to create {name}.");
            return texture;
        }

        private static GraphicsBuffer CreateBuffer(int count, int stride,
            string name) => new(GraphicsBuffer.Target.Structured,
                Math.Max(1, count), stride) { name = name };

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
                result = checked(result << 1);
            return result;
        }

        private static void DestroyTexture(RenderTexture texture)
        {
            if (texture == null)
                return;
            texture.Release();
            if (Application.isPlaying)
                Destroy(texture);
            else
                DestroyImmediate(texture);
        }

        private void OnDestroy()
        {
            if (_disposed)
                return;
            _disposed = true;
            _running = false;
            if (_renderer != null)
                _renderer.PredictionReady -= OnPredictionReady;
            _pendingPrediction?.Dispose();
            _pendingPrediction = null;
            InverseOwnedResources resources = DetachOwnedResources();
            InFlightFrame frame = _inFlight;
            _inFlight = null;
            if (frame == null)
                resources.Dispose();
            else
            {
                frame.Discard = true;
                frame.RetireWhenReadbacksComplete(() =>
                {
                    try { frame.Dispose(); }
                    finally { resources.Dispose(); }
                });
            }
            _initialized = false;
        }

        private InverseOwnedResources DetachOwnedResources()
        {
            var scratch = _segmentScratch.ToArray();
            _segmentScratch.Clear();
            var resources = new InverseOwnedResources(_coneLuts, _metricDepth,
                _depthFlags, _calibrationQ48, _rgbCalibrationQ48,
                _rgbViewOperators, _rgbViewSupportScale, _activePageFlags,
                _commitPageFlags, _unmatchedBlockFlags, _unmatchedBlockAnchors,
                _conflictRecords,
                _conflictCount, _frameCounters, _gaugePromotionCounts,
                _posePrior, _poseResult, _posePartials, _proofLedger,
                _localGauge, scratch);
            _coneLuts = null;
            _calibration = null;
            _metricDepth = null;
            _depthFlags = null;
            _calibrationQ48 = null;
            _rgbCalibrationQ48 = null;
            _rgbViewOperators = null;
            _rgbViewSupportScale = null;
            _rgbViewCatalog = null;
            _activePageFlags = null;
            _commitPageFlags = null;
            _unmatchedBlockFlags = null;
            _unmatchedBlockAnchors = null;
            _conflictRecords = null;
            _conflictCount = null;
            _frameCounters = null;
            _gaugePromotionCounts = null;
            _posePrior = null;
            _poseResult = null;
            _posePartials = null;
            _posePartialCapacity = 0;
            _proofLedger = null;
            _localGauge = null;
            _activeFlagCapacity = 0;
            _blockFlagCapacity = 0;
            _scratchResolution = default;
            return resources;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct SigmaPackedQ48
        {
            private SigmaPackedQ48(uint low, uint high)
            {
                Low = low;
                High = high;
            }

            public readonly uint Low;
            public readonly uint High;

            public static SigmaPackedQ48 FromRaw(long raw) => new(
                unchecked((uint)raw), unchecked((uint)(raw >> 32)));
        }

        private readonly struct PendingGaugePublication
        {
            public PendingGaugePublication(SigmaCarrierWriteLease write,
                SigmaProofPageLease proof,
                SigmaProofPageStatus proofStatus, uint promotedSamples)
            {
                Write = write;
                Proof = proof;
                ProofStatus = proofStatus;
                PromotedSamples = promotedSamples;
            }

            public SigmaCarrierWriteLease Write { get; }
            public SigmaProofPageLease Proof { get; }
            public SigmaProofPageStatus ProofStatus { get; }
            public uint PromotedSamples { get; }
        }

        private readonly struct PendingMatchedPublication
        {
            public PendingMatchedPublication(SigmaCarrierPageHandle source,
                SigmaCarrierWriteLease carrier, SigmaProofPageLease proof)
            {
                Source = source;
                Carrier = carrier;
                Proof = proof;
            }

            public SigmaCarrierPageHandle Source { get; }
            public SigmaCarrierWriteLease Carrier { get; }
            public SigmaProofPageLease Proof { get; }
        }

        private readonly struct PendingGaugeWrite
        {
            public PendingGaugeWrite(SigmaCarrierWriteLease carrier,
                SigmaProofPageLease proof)
            {
                Carrier = carrier;
                Proof = proof;
            }
            public SigmaCarrierWriteLease Carrier { get; }
            public SigmaProofPageLease Proof { get; }
        }

        private readonly struct GaugeImagePage : IEquatable<GaugeImagePage>,
            IComparable<GaugeImagePage>
        {
            public GaugeImagePage(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; }
            public int Y { get; }
            public int CompareTo(GaugeImagePage other)
            {
                int y = Y.CompareTo(other.Y);
                return y != 0 ? y : X.CompareTo(other.X);
            }
            public bool Equals(GaugeImagePage other) => X == other.X && Y == other.Y;
            public override bool Equals(object obj) =>
                obj is GaugeImagePage other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(X, Y);
        }

        private sealed class SegmentInverseScratch : IDisposable
        {
            private readonly GraphicsBuffer _stateIdentity;
            private readonly GraphicsBuffer _metadataIdentity;

            public SegmentInverseScratch(SigmaCarrierReadBatch batch)
            {
                SegmentIndex = batch.SegmentIndex;
                Capacity = batch.PageCapacity;
                _stateIdentity = batch.State;
                _metadataIdentity = batch.Metadata;
                ActivePageSlots = CreateBuffer(Capacity, sizeof(uint),
                    $"Sigma inverse active slots {SegmentIndex}");
                ActiveDispatchArguments = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured |
                    GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint))
                {
                    name = $"Sigma inverse dispatch {SegmentIndex}"
                };
                ActiveDispatchArguments.SetData(new uint[] { 64u, 0u, 1u });
                ProposalGeometry = CreateBuffer(checked(Capacity *
                    SigmaCarrier.SamplesPerPage * 3), sizeof(uint) * 2,
                    $"Sigma inverse proposal geometry {SegmentIndex}");
                ProposalStatus = CreateBuffer(checked(Capacity *
                    SigmaCarrier.SamplesPerPage), sizeof(uint),
                    $"Sigma inverse proposal status {SegmentIndex}");
                ProposalEpoch = CreateBuffer(Capacity, sizeof(uint),
                    $"Sigma inverse proposal epoch {SegmentIndex}");
                ProposalEpoch.SetData(new uint[Capacity]);
                ProposalMass = CreateBuffer(checked(Capacity *
                    SigmaCarrier.SamplesPerPage), sizeof(uint) * 2,
                    $"Sigma inverse proposal mass {SegmentIndex}");
            }

            public int SegmentIndex { get; }
            public int Capacity { get; }
            public GraphicsBuffer ActivePageSlots { get; }
            public GraphicsBuffer ActiveDispatchArguments { get; }
            public GraphicsBuffer ProposalGeometry { get; }
            public GraphicsBuffer ProposalMass { get; }
            public GraphicsBuffer ProposalStatus { get; }
            public GraphicsBuffer ProposalEpoch { get; }

            public bool Matches(SigmaCarrierReadBatch batch) =>
                SegmentIndex == batch.SegmentIndex && Capacity == batch.PageCapacity &&
                ReferenceEquals(_stateIdentity, batch.State) &&
                ReferenceEquals(_metadataIdentity, batch.Metadata);

            public void Dispose()
            {
                ActivePageSlots.Dispose();
                ActiveDispatchArguments.Dispose();
                ProposalGeometry.Dispose();
                ProposalMass.Dispose();
                ProposalStatus.Dispose();
                ProposalEpoch.Dispose();
            }
        }

        private sealed class InverseOwnedResources : IDisposable
        {
            private RigConeLutSet _coneLuts;
            private RenderTexture _metricDepth;
            private RenderTexture _depthFlags;
            private GraphicsBuffer[] _buffers;
            private SegmentInverseScratch[] _scratch;
            private SigmaConstraintLedger _proofLedger;
            private SigmaGaugeController _localGauge;

            public InverseOwnedResources(RigConeLutSet coneLuts,
                RenderTexture metricDepth, RenderTexture depthFlags,
                GraphicsBuffer calibration, GraphicsBuffer rgbCalibration,
                GraphicsBuffer rgbOperators, GraphicsBuffer rgbSupportScale,
                GraphicsBuffer activeFlags,
                GraphicsBuffer commitFlags, GraphicsBuffer unmatchedFlags,
                GraphicsBuffer unmatchedAnchors,
                GraphicsBuffer conflicts, GraphicsBuffer conflictCount,
                GraphicsBuffer counters, GraphicsBuffer gaugePromotionCounts,
                GraphicsBuffer posePrior, GraphicsBuffer poseResult,
                GraphicsBuffer posePartials,
                SigmaConstraintLedger proofLedger,
                SigmaGaugeController localGauge,
                SegmentInverseScratch[] scratch)
            {
                _coneLuts = coneLuts;
                _metricDepth = metricDepth;
                _depthFlags = depthFlags;
                _buffers = new[] { calibration, rgbCalibration, rgbOperators,
                    rgbSupportScale, activeFlags, commitFlags,
                    unmatchedFlags, unmatchedAnchors, conflicts, conflictCount, counters,
                    gaugePromotionCounts, posePrior, poseResult, posePartials };
                _scratch = scratch ?? Array.Empty<SegmentInverseScratch>();
                _proofLedger = proofLedger;
                _localGauge = localGauge;
            }

            public void Dispose()
            {
                _coneLuts?.Retire();
                _coneLuts = null;
                DestroyTexture(_metricDepth);
                DestroyTexture(_depthFlags);
                _metricDepth = null;
                _depthFlags = null;
                if (_buffers != null)
                {
                    for (int index = 0; index < _buffers.Length; ++index)
                        _buffers[index]?.Dispose();
                    _buffers = null;
                }
                if (_scratch != null)
                {
                    for (int index = 0; index < _scratch.Length; ++index)
                        _scratch[index]?.Dispose();
                    _scratch = null;
                }
                _proofLedger?.Dispose();
                _proofLedger = null;
                _localGauge?.Dispose();
                _localGauge = null;
            }
        }

        /// <summary>
        /// Keeps teardown resources alive until every small asynchronous scheduler
        /// readback issued for the frame has completed. It never exposes pixel or
        /// carrier data to the CPU and never blocks the render thread.
        /// </summary>
        private sealed class ReadbackRetirementLatch
        {
            private readonly object _gate = new();
            private int _pending;
            private Action _retirement;

            public AsyncGPUReadbackRequest Request(GraphicsBuffer buffer)
                => Request(buffer, 0, 0);

            public AsyncGPUReadbackRequest Request(GraphicsBuffer buffer,
                int size, int offset)
            {
                if (buffer == null)
                    throw new ArgumentNullException(nameof(buffer));
                lock (_gate)
                    checked { _pending++; }
                try
                {
                    return size > 0
                        ? AsyncGPUReadback.Request(buffer, size, offset,
                            OnCompleted)
                        : AsyncGPUReadback.Request(buffer, OnCompleted);
                }
                catch
                {
                    OnCompleted(default);
                    throw;
                }
            }

            public void RetireWhenComplete(Action retirement)
            {
                if (retirement == null)
                    throw new ArgumentNullException(nameof(retirement));
                Action invoke = null;
                lock (_gate)
                {
                    if (_retirement != null)
                        throw new InvalidOperationException(
                            "Readback retirement was already registered.");
                    if (_pending == 0)
                        invoke = retirement;
                    else
                        _retirement = retirement;
                }
                invoke?.Invoke();
            }

            private void OnCompleted(AsyncGPUReadbackRequest request)
            {
                Action invoke = null;
                lock (_gate)
                {
                    if (_pending <= 0)
                        return;
                    _pending--;
                    if (_pending == 0 && _retirement != null)
                    {
                        invoke = _retirement;
                        _retirement = null;
                    }
                }
                invoke?.Invoke();
            }
        }

        private enum InFlightPhase
        {
            PoseGauge,
            Initial,
            MatchedProof,
            LocalGaugeRequest,
            LocalGaugeTransaction,
            GaugeProof,
        }

        private sealed class InFlightFrame : IDisposable
        {
            public InFlightFrame(SigmaPredictionFrameLease prediction,
                uint revision, Vector2Int resolution, Vector2Int blockResolution,
                int segmentCount, uint leftIndependenceKey,
                uint rightIndependenceKey, uint rgbLeftIndependenceKey,
                uint rgbRightIndependenceKey,
                Dictionary<int, SigmaCarrierPageHandle> pageSnapshot,
                Dictionary<SigmaCarrierPageCoordinate,
                    SigmaTopologyEvidenceView> topologyEvidence,
                ReadbackRetirementLatch readbackLatch,
                AsyncGPUReadbackRequest poseGauge)
            {
                Prediction = prediction;
                Revision = revision;
                Resolution = resolution;
                BlockResolution = blockResolution;
                SegmentCount = segmentCount;
                LeftIndependenceKey = leftIndependenceKey;
                RightIndependenceKey = rightIndependenceKey;
                RgbLeftIndependenceKey = rgbLeftIndependenceKey;
                RgbRightIndependenceKey = rgbRightIndependenceKey;
                PageSnapshot = pageSnapshot;
                TopologyEvidence = topologyEvidence ?? throw new
                    ArgumentNullException(nameof(topologyEvidence));
                _readbackLatch = readbackLatch ?? throw new ArgumentNullException(
                    nameof(readbackLatch));
                PoseGauge = poseGauge;
                Phase = InFlightPhase.PoseGauge;
            }

            public SigmaPredictionFrameLease Prediction { get; private set; }
            public uint Revision { get; }
            public Vector2Int Resolution { get; }
            public Vector2Int BlockResolution { get; }
            public int SegmentCount { get; }
            public uint LeftIndependenceKey { get; }
            public uint RightIndependenceKey { get; }
            public uint RgbLeftIndependenceKey { get; }
            public uint RgbRightIndependenceKey { get; }
            public Dictionary<int, SigmaCarrierPageHandle> PageSnapshot { get; }
            public Dictionary<SigmaCarrierPageCoordinate,
                SigmaTopologyEvidenceView> TopologyEvidence { get; }
            public AsyncGPUReadbackRequest ActivePageFlags { get; private set; }
            public AsyncGPUReadbackRequest CommitFlags { get; private set; }
            public AsyncGPUReadbackRequest UnmatchedBlocks { get; private set; }
            public AsyncGPUReadbackRequest UnmatchedAnchors { get; private set; }
            public AsyncGPUReadbackRequest Counters { get; private set; }
            public AsyncGPUReadbackRequest ConflictCount { get; private set; }
            public AsyncGPUReadbackRequest PoseGauge { get; }
            public AsyncGPUReadbackRequest LocalGaugeRequest { get; private set; }
            public AsyncGPUReadbackRequest LocalGaugeStatus { get; private set; }
            public AsyncGPUReadbackRequest LocalGaugeRawStatus { get; private set; }
            public AsyncGPUReadbackRequest GaugePromotions { get; private set; }
            public AsyncGPUReadbackRequest ProofStatus { get; private set; }
            public List<PendingMatchedPublication> MatchedWrites { get; } = new();
            public List<PendingGaugeWrite> GaugeWrites { get; } = new();
            public List<SigmaCarrierPageHandle> LocalGaugeSources { get; } = new();
            public SigmaGaugeTransaction LocalGauge { get; set; }
            public SigmaProofFrameLease ProofFrame { get; set; }
            public bool Discard { get; set; }
            public InFlightPhase Phase { get; private set; }
            public int MatchedPageCommits { get; set; }
            public int LocalGaugePageCommits { get; set; }
            public int PublishedGaugePages { get; set; }
            public uint ProducedConflicts { get; set; }
            private readonly ReadbackRetirementLatch _readbackLatch;
            public bool InitialReadbackHasError => ActivePageFlags.hasError ||
                CommitFlags.hasError ||
                UnmatchedBlocks.hasError || UnmatchedAnchors.hasError ||
                Counters.hasError || ConflictCount.hasError;
            public bool AllRequestsDone => Phase switch
            {
                InFlightPhase.PoseGauge => PoseGauge.done,
                InFlightPhase.Initial => ActivePageFlags.done &&
                    CommitFlags.done && UnmatchedBlocks.done && Counters.done &&
                    UnmatchedAnchors.done && ConflictCount.done,
                InFlightPhase.MatchedProof => ProofStatus.done,
                InFlightPhase.LocalGaugeRequest => LocalGaugeRequest.done,
                InFlightPhase.LocalGaugeTransaction => LocalGaugeStatus.done &&
                    LocalGaugeRawStatus.done,
                InFlightPhase.GaugeProof => GaugePromotions.done &&
                    ProofStatus.done,
                _ => false,
            };
            public bool HasError => Phase switch
            {
                InFlightPhase.PoseGauge => PoseGauge.hasError,
                InFlightPhase.Initial => InitialReadbackHasError,
                InFlightPhase.MatchedProof => ProofStatus.hasError,
                InFlightPhase.LocalGaugeRequest => false,
                InFlightPhase.LocalGaugeTransaction => false,
                InFlightPhase.GaugeProof => GaugePromotions.hasError ||
                    ProofStatus.hasError,
                _ => true,
            };

            public void ReplacePrediction(SigmaPredictionFrameLease prediction)
            {
                if (Phase != InFlightPhase.PoseGauge || prediction == null)
                    throw new InvalidOperationException(
                        "Pose rerasterization requires the pose-gauge phase.");
                Prediction.Dispose();
                Prediction = prediction;
            }

            public void BeginInitialReadbacks(GraphicsBuffer activePageFlags,
                GraphicsBuffer commitFlags, GraphicsBuffer unmatchedBlocks,
                GraphicsBuffer unmatchedAnchors, GraphicsBuffer counters,
                GraphicsBuffer conflictCount)
            {
                if (Phase != InFlightPhase.PoseGauge)
                    throw new InvalidOperationException(
                        "Carrier inverse must follow the pose-gauge meet.");
                ActivePageFlags = _readbackLatch.Request(activePageFlags);
                CommitFlags = _readbackLatch.Request(commitFlags);
                UnmatchedBlocks = _readbackLatch.Request(unmatchedBlocks);
                UnmatchedAnchors = _readbackLatch.Request(unmatchedAnchors);
                Counters = _readbackLatch.Request(counters);
                ConflictCount = _readbackLatch.Request(conflictCount);
                Phase = InFlightPhase.Initial;
            }

            public void BeginMatchedProofReadback(GraphicsBuffer proofStatus)
            {
                if (Phase != InFlightPhase.Initial)
                    throw new InvalidOperationException(
                        "Matched proof readback requires the initial phase.");
                ProofStatus = _readbackLatch.Request(proofStatus);
                Phase = InFlightPhase.MatchedProof;
            }

            public void BeginLocalGaugeRequest(GraphicsBuffer requests)
            {
                if (Phase != InFlightPhase.Initial &&
                    Phase != InFlightPhase.MatchedProof)
                    throw new InvalidOperationException(
                        "Local gauge request must follow matched publication.");
                LocalGaugeRequest = _readbackLatch.Request(requests);
                Phase = InFlightPhase.LocalGaugeRequest;
            }

            public void BeginLocalGaugeTransaction(GraphicsBuffer status,
                GraphicsBuffer rawStatus, int rawCloneCount)
            {
                if (Phase != InFlightPhase.LocalGaugeRequest)
                    throw new InvalidOperationException(
                        "Local gauge transaction requires a selected request.");
                LocalGaugeStatus = _readbackLatch.Request(status);
                int bytes = Math.Max(sizeof(uint) * 4,
                    checked(rawCloneCount * sizeof(uint) * 4));
                LocalGaugeRawStatus = _readbackLatch.Request(rawStatus,
                    bytes, 0);
                Phase = InFlightPhase.LocalGaugeTransaction;
            }

            public void BeginGaugeReadback(GraphicsBuffer promotionCounts,
                GraphicsBuffer proofStatus)
            {
                if (Phase == InFlightPhase.GaugeProof)
                    throw new InvalidOperationException(
                        "Gauge promotion readback has already been requested.");
                GaugePromotions = _readbackLatch.Request(promotionCounts);
                ProofStatus = _readbackLatch.Request(proofStatus);
                Phase = InFlightPhase.GaugeProof;
            }

            public void RetireWhenReadbacksComplete(Action retirement) =>
                _readbackLatch.RetireWhenComplete(retirement);

            public void AbortMatchedWrites()
            {
                for (int index = 0; index < MatchedWrites.Count; ++index)
                {
                    MatchedWrites[index].Carrier.Dispose();
                    MatchedWrites[index].Proof.Dispose();
                }
                MatchedWrites.Clear();
            }

            public void AbortGaugeWrites()
            {
                for (int index = 0; index < GaugeWrites.Count; ++index)
                {
                    GaugeWrites[index].Carrier.Dispose();
                    GaugeWrites[index].Proof.Dispose();
                }
                GaugeWrites.Clear();
            }

            public void AbortPendingWrites()
            {
                AbortMatchedWrites();
                AbortGaugeWrites();
                LocalGauge?.Dispose();
                LocalGauge = null;
            }

            public void Dispose()
            {
                AbortPendingWrites();
                ProofFrame?.Dispose();
                ProofFrame = null;
                Prediction.Dispose();
            }
        }
    }

    public readonly struct SigmaInverseDiagnosticSnapshot
    {
        private SigmaInverseDiagnosticSnapshot(uint activePages, uint hitSamples,
            uint changedSamples, uint emptyMeets, uint exclusions,
            uint unmatchedBlocks, uint promotedSamples, uint failedChecks,
            uint evidenceRecords)
        {
            ActivePages = activePages;
            HitSamples = hitSamples;
            ChangedSamples = changedSamples;
            EmptyMeets = emptyMeets;
            Exclusions = exclusions;
            UnmatchedBlocks = unmatchedBlocks;
            PromotedSamples = promotedSamples;
            FailedChecks = failedChecks;
            EvidenceRecords = evidenceRecords;
        }

        public uint ActivePages { get; }
        public uint HitSamples { get; }
        public uint ChangedSamples { get; }
        public uint EmptyMeets { get; }
        public uint Exclusions { get; }
        public uint UnmatchedBlocks { get; }
        public uint PromotedSamples { get; }
        public uint FailedChecks { get; }
        public uint EvidenceRecords { get; }

        internal static SigmaInverseDiagnosticSnapshot From(
            NativeArray<uint> counters, uint evidenceRecords,
            uint promotedSamples) => new(
                Value(counters, 0), Value(counters, 1), Value(counters, 2),
                Value(counters, 3), Value(counters, 4), Value(counters, 5),
                promotedSamples, Value(counters, 7), evidenceRecords);

        private static uint Value(NativeArray<uint> values, int index) =>
            (uint)index < (uint)values.Length ? values[index] : 0u;
    }
}
