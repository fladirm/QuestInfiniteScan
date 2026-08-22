using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// GPU-resident four-stream inverse work graph. The CPU stages immutable rig
    /// metadata and records one bounded command graph; active-page selection,
    /// null-gauge allocation, exact inverse cells, proof reduction and immutable
    /// generation publication remain on GPU. Completion is a fence, never a
    /// scheduler readback.
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
        private const string RgbSourceResource =
            "SigmaPrism/SigmaRgbSourceCells";
        private const string WorkGraphResource =
            "SigmaPrism/SigmaInverseWorkGraph";
        private const string ConeLutResource = "SigmaPrism/ConeLut";
        private const string PoseGaugeResource = "SigmaPrism/SigmaPoseGauge";
        private const int CalibrationStride = 36;
        private const int RgbCalibrationStride = 8;
        private const int ConflictStride = 192;
        private const int WorkStride = 48;
        private const int WorkControlWords = 9;
        private const int RgbPhaseSamples = 256;
        private const uint RgbDispatchOffset = 0u;
        private const uint SolveDispatchOffset = 3u * sizeof(uint);
        private const uint PromoteDispatchOffset = 6u * sizeof(uint);
        private const uint ProofDispatchOffset = 9u * sizeof(uint);
        private const uint CommitDispatchOffset = 12u * sizeof(uint);

        [Header("Bounded GPU work graph")]
        [SerializeField, Range(1, 8)] private int inverseWorkCapacity = 8;
        [SerializeField, Range(1024, 8192)] private int rawTileCapacity = 4096;
        [SerializeField, Range(4096, 131072)] private int conflictCapacity =
            32768;
        [SerializeField, Range(0.005f, 0.1f)]
        private float poseTranslationPriorMetres = 0.03f;
        [SerializeField, Range(0.25f, 5f)]
        private float poseRotationPriorDegrees = 2f;
        [SerializeField, Range(4, 32)] private int poseSampleStride = 16;

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
        private ComputeShader _rgbSource;
        private ComputeShader _workGraph;
        private ComputeShader _ledgerShader;
        private ComputeShader _coneLutShader;
        private ComputeShader _poseGaugeCompute;
        private RigCalibration _calibration;
        private RigConeLutSet _coneLuts;
        private SigmaCarrierReadBatch _pool;
        private SigmaConstraintLedger _proofLedger;
        private SigmaPredictionFrameLease _pendingPrediction;
        private GpuSubmission _inFlight;

        private RenderTexture _metricDepth;
        private RenderTexture _depthFlags;
        private Vector2Int _scratchResolution;
        private GraphicsBuffer _calibrationQ48;
        private GraphicsBuffer _rgbCalibrationQ48;
        private GraphicsBuffer _rgbViewOperators;
        private GraphicsBuffer _rgbViewSupportScale;
        private GraphicsBuffer _activePageFlags;
        private GraphicsBuffer _unmatchedBlockFlags;
        private GraphicsBuffer _unmatchedBlockAnchors;
        private GraphicsBuffer _conflictRecords;
        private GraphicsBuffer _conflictCount;
        private GraphicsBuffer _frameCounters;
        private GraphicsBuffer _gaugePromotionCounts;
        private GraphicsBuffer _proposalStatus;
        private GraphicsBuffer _proposalEpoch;
        private GraphicsBuffer _rgbSourceBounds;
        private GraphicsBuffer _rgbSourceMeta;
        private GraphicsBuffer _posePrior;
        private GraphicsBuffer _poseResult;
        private GraphicsBuffer _posePartials;
        private GraphicsBuffer _inverseWork;
        private GraphicsBuffer _inverseWorkControl;
        private GraphicsBuffer _dispatchArguments;
        private int _posePartialCapacity;
        private int _blockFlagCapacity;
        private int _activeFlagCount;

        private int _normalizeKernel;
        private int _clearKernel;
        private int _classifyKernel;
        private int _rgbSourceKernel;
        private int _commitKernel;
        private int _promoteKernel;
        private int _poseBuildKernel;
        private int _poseReduceKernel;
        private int _workClearKernel;
        private int _workCompactKernel;
        private int _workPrepareKernel;
        private int _workRawPlanKernel;
        private int _workCommitKernel;
        private int _proofClearKernel;
        private int _proofReduceKernel;
        private int _proofGaugeKernel;

        private uint _nextRevision = 1u;
        private bool _running;
        private bool _initialized;
        private bool _disposed;

        public string ModuleName => "Sigma GPU-resident joint RGB-D inverse";
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
            _backendGate = scanner.ExactBackendGate ??
                throw new InvalidOperationException(
                    "Sigma inverse requires the GPU-resident exact backend gate.");
            _normalize = Resources.Load<ComputeShader>(NormalizeResource);
            _inverse = Resources.Load<ComputeShader>(InverseResource);
            _rgbSource = Resources.Load<ComputeShader>(RgbSourceResource);
            _workGraph = Resources.Load<ComputeShader>(WorkGraphResource);
            _ledgerShader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaConstraintLedger");
            _coneLutShader = Resources.Load<ComputeShader>(ConeLutResource);
            _poseGaugeCompute = Resources.Load<ComputeShader>(PoseGaugeResource);
            if (_carrier == null || _topology == null || _renderer == null ||
                _rigBridge == null || _normalize == null || _inverse == null ||
                _rgbSource == null || _workGraph == null ||
                _ledgerShader == null ||
                _coneLutShader == null || _poseGaugeCompute == null)
                throw new InvalidOperationException(
                    "Sigma GPU inverse resources are incomplete.");

            FindKernels();
            _pool = _carrier.AcquireGpuManagedPool();
            inverseWorkCapacity = Math.Min(inverseWorkCapacity,
                Math.Max(1, _pool.PageCapacity / 2));
            _proofLedger = new SigmaConstraintLedger(_pool.PageCapacity,
                rawTileCapacity, _backendGate, inverseWorkCapacity);
            CreatePersistentResources();
            _renderer.PredictionReady += OnPredictionReady;
            _initialized = true;
            Logger.Info($"Sigma inverse GPU work graph ready: work={inverseWorkCapacity}, " +
                        $"carrierPages={_pool.PageCapacity}, raw={rawTileCapacity}.");
        }

        public void OnScanStarted()
        {
            _running = true;
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
            if (_inFlight != null && _inFlight.IsComplete)
            {
                GpuSubmission completed = _inFlight;
                _inFlight = null;
                if (!completed.Discard)
                    CommittedFrames++;
                completed.Dispose();
            }
            if (!_running || _inFlight != null || _pendingPrediction == null)
                return;

            SigmaPredictionFrameLease prediction = _pendingPrediction;
            _pendingPrediction = null;
            try
            {
                SubmitGpuGraph(prediction);
            }
            catch (Exception exception)
            {
                prediction.Dispose();
                FailedFrames++;
                Logger.Error("Sigma inverse submission failed: " +
                    exception.Message);
            }
        }

        private void SubmitGpuGraph(SigmaPredictionFrameLease prediction)
        {
            StereoRigFrameLease source = prediction.Source;
            if (!source.IsValid)
                throw new InvalidOperationException("Inverse source lease is invalid.");
            EnsureCalibration(source);
            EnsureFrameResources(source.DepthResolution);
            UploadExactCalibration(source);

            Vector2Int blockResolution = new(
                CeilDiv(source.DepthResolution.x, 32),
                CeilDiv(source.DepthResolution.y, 32));
            int blockCount = checked(blockResolution.x * blockResolution.y);
            EnsureBlockBuffers(blockCount);
            uint revision = NextRevision();
            uint leftKey = IndependenceKey(source.DepthLeft,
                source.CalibrationEpoch);
            uint rightKey = IndependenceKey(source.DepthRight,
                source.CalibrationEpoch);
            uint rgbLeftKey = IndependenceKey(source.RgbLeft,
                source.CalibrationEpoch);
            uint rgbRightKey = IndependenceKey(source.RgbRight,
                source.CalibrationEpoch);
            int frameSlot = _proofLedger.UploadGpuFrame(source, revision,
                leftKey, rightKey, rgbLeftKey, rgbRightKey);

            CommandBuffer command = CommandBufferPool.Get(
                "Sigma-PRISM-16 GPU Inverse Transaction");
            GraphicsFence fence;
            try
            {
                RecordNormalize(command, source);
                RecordPoseGauge(command, source, prediction, revision);
                RecordClearAndClassify(command, source, prediction,
                    blockResolution, blockCount, revision, leftKey, rightKey,
                    rgbLeftKey, rgbRightKey);
                RecordWorkCompaction(command, blockResolution, blockCount,
                    revision, frameSlot, source.CalibrationEpoch);
                RecordPrepareTransactions(command);
                RecordProofClear(command, frameSlot, source.CalibrationEpoch,
                    revision);
                RecordRgbSource(command, source, prediction, revision,
                    leftKey, rightKey, rgbLeftKey, rgbRightKey);
                RecordMatchedSolve(command, source, prediction, revision,
                    leftKey, rightKey, rgbLeftKey, rgbRightKey);
                RecordGaugePromote(command, source, prediction, revision,
                    leftKey, rightKey, rgbLeftKey, rgbRightKey);
                RecordRawPlan(command, frameSlot, source.CalibrationEpoch);
                RecordProofReduce(command, frameSlot,
                    source.CalibrationEpoch, revision);
                RecordProofGaugeDemand(command, frameSlot,
                    source.CalibrationEpoch, revision);
                RecordProofCommit(command);
                _topology.RecordGpuInverseTopology(command, _pool,
                    _inverseWork, _inverseWorkControl, _proposalStatus,
                    _proposalEpoch, inverseWorkCapacity, revision, leftKey,
                    rightKey);
                fence = command.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.ComputeProcessing);
                Graphics.ExecuteCommandBuffer(command);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
            _inFlight = new GpuSubmission(prediction, fence);
            SubmittedFrames++;
            LastDiagnostics = SigmaInverseDiagnosticSnapshot.GpuResident(
                revision);
        }

        private void RecordClearAndClassify(CommandBuffer command,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            Vector2Int blockResolution, int blockCount, uint revision,
            uint leftKey, uint rightKey, uint rgbLeftKey, uint rgbRightKey)
        {
            int clearCount = Math.Max(Math.Max(_activeFlagCount, blockCount),
                Math.Max(8, inverseWorkCapacity));
            command.SetComputeIntParam(_inverse, "_ActiveFlagCount",
                _activeFlagCount);
            command.SetComputeIntParam(_inverse, "_BlockFlagCount", blockCount);
            command.SetComputeIntParam(_inverse, "_GaugeCommitCapacity",
                inverseWorkCapacity);
            command.SetComputeIntParam(_inverse, "_ClearCount", clearCount);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_ActivePageFlags", _activePageFlags);
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

            BindFrameConstants(command, _inverse, _classifyKernel, source,
                prediction,
                blockResolution, leftKey, rightKey, rgbLeftKey, rgbRightKey,
                revision);
            command.SetComputeIntParam(_inverse, "_SegmentCount",
                _pool.SegmentIndex + 1);
            command.SetComputeIntParam(_inverse, "_ActiveFlagCount",
                _activeFlagCount);
            command.SetComputeIntParam(_inverse, "_BlockFlagCount", blockCount);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_DepthCalibrationQ48", _calibrationQ48);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                "_MetricDepth", _metricDepth);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                "_DepthFlags", _depthFlags);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                "_PredDepthSupport", prediction.DepthSupport);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                "_PredStateKey", prediction.StateKey);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                "_DepthRayCenterLeft", _coneLuts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                "_DepthRayCenterRight", _coneLuts.DepthRight.CenterRaySolidAngle);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_ActivePageFlags", _activePageFlags);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_UnmatchedBlockFlags", _unmatchedBlockFlags);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_UnmatchedBlockAnchors", _unmatchedBlockAnchors);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_FrameCounters", _frameCounters);
            command.DispatchCompute(_inverse, _classifyKernel,
                CeilDiv(source.DepthResolution.x, 8),
                CeilDiv(source.DepthResolution.y, 8), 2);
        }

        private void RecordWorkCompaction(CommandBuffer command,
            Vector2Int blockResolution, int blockCount, uint revision,
            int frameSlot, uint calibrationEpoch)
        {
            BindWorkGraph(command, _workClearKernel);
            command.DispatchCompute(_workGraph, _workClearKernel, 1, 1, 1);

            BindWorkGraph(command, _workCompactKernel);
            command.SetComputeIntParam(_workGraph, "_SegmentIndex",
                _pool.SegmentIndex);
            command.SetComputeIntParam(_workGraph, "_ActiveFlagCount",
                _activeFlagCount);
            command.SetComputeIntParam(_workGraph, "_BlockFlagCount",
                blockCount);
            command.SetComputeIntParams(_workGraph, "_GaugeBlockResolution",
                blockResolution.x, blockResolution.y);
            command.SetComputeIntParam(_workGraph, "_FrameRevision",
                unchecked((int)revision));
            command.SetComputeIntParam(_workGraph, "_ProofFrameSlot", frameSlot);
            command.SetComputeIntParam(_workGraph, "_ProofCalibrationEpoch",
                unchecked((int)calibrationEpoch));
            command.DispatchCompute(_workGraph, _workCompactKernel, 1, 1, 1);
        }

        private void RecordPrepareTransactions(CommandBuffer command)
        {
            BindWorkGraph(command, _workPrepareKernel);
            command.DispatchCompute(_workGraph, _workPrepareKernel,
                _dispatchArguments, ProofDispatchOffset);
        }

        private void RecordProofClear(CommandBuffer command, int frameSlot,
            uint calibrationEpoch, uint revision)
        {
            BindLedger(command, _proofClearKernel, frameSlot,
                calibrationEpoch, revision);
            command.SetComputeBufferParam(_proofLedgerShader, _proofClearKernel,
                "_ProofSamples", _proofLedger.ProofSampleBuffer);
            command.SetComputeBufferParam(_proofLedgerShader, _proofClearKernel,
                "_ProofPageStatus", _proofLedger.StatusBuffer);
            command.DispatchCompute(_proofLedgerShader, _proofClearKernel,
                _dispatchArguments, ProofDispatchOffset);
        }

        private ComputeShader _proofLedgerShader => _ledgerShader;

        private void RecordRgbSource(CommandBuffer command,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            uint revision, uint leftKey, uint rightKey, uint rgbLeftKey,
            uint rgbRightKey)
        {
            BindExactCommon(command, _rgbSource, _rgbSourceKernel, source,
                prediction, revision, leftKey, rightKey, rgbLeftKey,
                rgbRightKey);
            command.SetComputeIntParam(_rgbSource, "_UseInverseWorkList", 1);
            command.SetComputeIntParam(_rgbSource, "_PageCapacity",
                _pool.PageCapacity);
            command.SetComputeBufferParam(_rgbSource, _rgbSourceKernel,
                "_CarrierState", _pool.State);
            command.SetComputeBufferParam(_rgbSource, _rgbSourceKernel,
                "_PageMetadata", _pool.Metadata);
            command.SetComputeBufferParam(_rgbSource, _rgbSourceKernel,
                "_RgbSourceBounds", _rgbSourceBounds);
            command.SetComputeBufferParam(_rgbSource, _rgbSourceKernel,
                "_RgbSourceMeta", _rgbSourceMeta);
            BindInverseWork(command, _rgbSource, _rgbSourceKernel);
            command.DispatchCompute(_rgbSource, _rgbSourceKernel,
                _dispatchArguments, RgbDispatchOffset);
        }

        private void RecordMatchedSolve(CommandBuffer command,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            uint revision, uint leftKey, uint rightKey, uint rgbLeftKey,
            uint rgbRightKey)
        {
            BindInverseMutation(command, _commitKernel, source, prediction,
                revision, leftKey, rightKey, rgbLeftKey, rgbRightKey);
            command.DispatchCompute(_inverse, _commitKernel,
                _dispatchArguments, SolveDispatchOffset);
        }

        private void RecordGaugePromote(CommandBuffer command,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            uint revision, uint leftKey, uint rightKey, uint rgbLeftKey,
            uint rgbRightKey)
        {
            BindInverseMutation(command, _promoteKernel, source, prediction,
                revision, leftKey, rightKey, rgbLeftKey, rgbRightKey);
            command.DispatchCompute(_inverse, _promoteKernel,
                _dispatchArguments, PromoteDispatchOffset);
        }

        private void RecordRawPlan(CommandBuffer command, int frameSlot,
            uint calibrationEpoch)
        {
            BindWorkGraph(command, _workRawPlanKernel);
            command.SetComputeIntParam(_workGraph, "_ProofFrameSlot", frameSlot);
            command.SetComputeIntParam(_workGraph, "_ProofCalibrationEpoch",
                unchecked((int)calibrationEpoch));
            command.SetComputeIntParam(_workGraph, "_RawTileCapacity",
                _proofLedger.RawTileCapacity);
            command.SetComputeBufferParam(_workGraph, _workRawPlanKernel,
                "_ProofSamplesRead", _proofLedger.ProofSampleBuffer);
            command.SetComputeBufferParam(_workGraph, _workRawPlanKernel,
                "_RawReservations", _proofLedger.RawReservationBuffer);
            command.SetComputeBufferParam(_workGraph, _workRawPlanKernel,
                "_RawAllocator", _proofLedger.RawAllocatorBuffer);
            command.DispatchCompute(_workGraph, _workRawPlanKernel, 1, 1, 1);
        }

        private void RecordProofReduce(CommandBuffer command, int frameSlot,
            uint calibrationEpoch, uint revision)
        {
            BindLedger(command, _proofReduceKernel, frameSlot,
                calibrationEpoch, revision);
            ComputeShader shader = _proofLedgerShader;
            command.SetComputeBufferParam(shader, _proofReduceKernel,
                "_ProofSamples", _proofLedger.ProofSampleBuffer);
            command.SetComputeBufferParam(shader, _proofReduceKernel,
                "_Certificates", _proofLedger.CertificateBuffer);
            command.SetComputeBufferParam(shader, _proofReduceKernel,
                "_CertificateBounds", _proofLedger.CertificateBoundsBuffer);
            command.SetComputeBufferParam(shader, _proofReduceKernel,
                "_ConstraintBlocks", _proofLedger.ConstraintBlockBuffer);
            command.SetComputeBufferParam(shader, _proofReduceKernel,
                "_RawTiles", _proofLedger.RawHeaderBuffer);
            command.SetComputeBufferParam(shader, _proofReduceKernel,
                "_RawTileWords", _proofLedger.RawWordsBuffer);
            command.SetComputeBufferParam(shader, _proofReduceKernel,
                "_RawReservations", _proofLedger.RawReservationBuffer);
            command.SetComputeBufferParam(shader, _proofReduceKernel,
                "_ProofPageStatus", _proofLedger.StatusBuffer);
            command.SetComputeBufferParam(shader, _proofReduceKernel,
                "_GaugeDemand", _proofLedger.GaugeDemandBuffer);
            command.DispatchCompute(shader, _proofReduceKernel,
                _dispatchArguments, ProofDispatchOffset);
        }

        private void RecordProofGaugeDemand(CommandBuffer command,
            int frameSlot, uint calibrationEpoch, uint revision)
        {
            BindLedger(command, _proofGaugeKernel, frameSlot,
                calibrationEpoch, revision);
            ComputeShader shader = _proofLedgerShader;
            command.SetComputeBufferParam(shader, _proofGaugeKernel,
                "_ProofSamples", _proofLedger.ProofSampleBuffer);
            command.SetComputeBufferParam(shader, _proofGaugeKernel,
                "_GaugeDemand", _proofLedger.GaugeDemandBuffer);
            command.DispatchCompute(shader, _proofGaugeKernel,
                _dispatchArguments, ProofDispatchOffset);
        }

        private void RecordProofCommit(CommandBuffer command)
        {
            BindWorkGraph(command, _workCommitKernel);
            command.SetComputeBufferParam(_workGraph, _workCommitKernel,
                "_ProofPageStatus", _proofLedger.StatusBuffer);
            command.SetComputeBufferParam(_workGraph, _workCommitKernel,
                "_GaugePromotionCounts", _gaugePromotionCounts);
            command.DispatchCompute(_workGraph, _workCommitKernel,
                _dispatchArguments, CommitDispatchOffset);
        }

        private void BindWorkGraph(CommandBuffer command, int kernel)
        {
            command.SetComputeIntParam(_workGraph, "_PageCapacity",
                _pool.PageCapacity);
            command.SetComputeIntParam(_workGraph, "_InverseWorkCapacity",
                inverseWorkCapacity);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_CarrierState", _pool.State);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_PageMetadata", _pool.Metadata);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_DirtyFlags", _pool.DirtyFlags);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_CurrentFlags", _pool.CurrentFlags);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_ReadoutDirtyFlags", _pool.ReadoutDirtyFlags);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_ActivePageFlags", _activePageFlags);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_UnmatchedBlockFlags", _unmatchedBlockFlags);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_UnmatchedBlockAnchors", _unmatchedBlockAnchors);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_InverseWork", _inverseWork);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_InverseWorkControl", _inverseWorkControl);
            command.SetComputeBufferParam(_workGraph, kernel,
                "_DispatchArgs", _dispatchArguments);
        }

        private void BindLedger(CommandBuffer command, int kernel,
            int frameSlot, uint calibrationEpoch, uint revision)
        {
            ComputeShader shader = _proofLedgerShader;
            command.SetComputeIntParam(shader, "_UseInverseWorkList", 1);
            command.SetComputeIntParam(shader, "_ProofFrameSlot", frameSlot);
            command.SetComputeIntParam(shader, "_ProofCalibrationEpoch",
                unchecked((int)calibrationEpoch));
            command.SetComputeIntParam(shader, "_ProofRevision",
                unchecked((int)revision));
            command.SetComputeIntParam(shader, "_RawTileCapacity",
                _proofLedger.RawTileCapacity);
            command.SetComputeIntParam(shader, "_ProofCarrierPageCapacity",
                _pool.PageCapacity);
            command.SetComputeBufferParam(shader, kernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(shader, kernel,
                "_InverseWork", _inverseWork);
            command.SetComputeBufferParam(shader, kernel,
                "_InverseWorkControl", _inverseWorkControl);
            command.SetComputeBufferParam(shader, kernel,
                "_ProofCarrierState", _pool.State);
        }

        private void BindInverseMutation(CommandBuffer command, int kernel,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            uint revision, uint leftKey, uint rightKey, uint rgbLeftKey,
            uint rgbRightKey)
        {
            BindExactCommon(command, _inverse, kernel, source, prediction,
                revision, leftKey, rightKey, rgbLeftKey, rgbRightKey);
            command.SetComputeIntParam(_inverse, "_UseInverseWorkList", 1);
            command.SetComputeIntParam(_inverse, "_SegmentIndex",
                _pool.SegmentIndex);
            command.SetComputeIntParam(_inverse, "_PageCapacity",
                _pool.PageCapacity);
            command.SetComputeIntParam(_inverse, "_TargetPageCapacity",
                _pool.PageCapacity);
            command.SetComputeIntParam(_inverse, "_GaugeCommitCapacity",
                inverseWorkCapacity);
            command.SetComputeIntParam(_inverse, "_ConflictCapacity",
                conflictCapacity);
            command.SetComputeIntParam(_inverse, "_ProposalFrameSerial",
                unchecked((int)revision));
            command.SetComputeBufferParam(_inverse, kernel,
                "_CarrierState", _pool.State);
            command.SetComputeBufferParam(_inverse, kernel,
                "_TargetCarrierState", _pool.State);
            command.SetComputeBufferParam(_inverse, kernel,
                "_PageMetadata", _pool.Metadata);
            command.SetComputeBufferParam(_inverse, kernel,
                "_CurrentFlags", _pool.CurrentFlags);
            command.SetComputeBufferParam(_inverse, kernel,
                "_ProposalStatus", _proposalStatus);
            command.SetComputeBufferParam(_inverse, kernel,
                "_ProposalEpoch", _proposalEpoch);
            command.SetComputeBufferParam(_inverse, kernel,
                "_RgbSourceBoundsRead", _rgbSourceBounds);
            command.SetComputeBufferParam(_inverse, kernel,
                "_RgbSourceMetaRead", _rgbSourceMeta);
            command.SetComputeBufferParam(_inverse, kernel,
                "_ConflictRecords", _conflictRecords);
            command.SetComputeBufferParam(_inverse, kernel,
                "_ConflictCount", _conflictCount);
            command.SetComputeBufferParam(_inverse, kernel,
                "_FrameCounters", _frameCounters);
            command.SetComputeBufferParam(_inverse, kernel,
                "_GaugePromotionCounts", _gaugePromotionCounts);
            command.SetComputeBufferParam(_inverse, kernel,
                "_ProofSamples", _proofLedger.ProofSampleBuffer);
            command.SetComputeBufferParam(_inverse, kernel,
                "_ProofPageStatus", _proofLedger.StatusBuffer);
            BindInverseWork(command, _inverse, kernel);
        }

        private void BindInverseWork(CommandBuffer command,
            ComputeShader shader, int kernel)
        {
            command.SetComputeBufferParam(shader, kernel, "_InverseWork",
                _inverseWork);
            command.SetComputeBufferParam(shader, kernel,
                "_InverseWorkControl", _inverseWorkControl);
        }

        private void BindExactCommon(CommandBuffer command,
            ComputeShader shader, int kernel, StereoRigFrameLease source,
            SigmaPredictionFrameLease prediction, uint revision,
            uint leftKey, uint rightKey, uint rgbLeftKey, uint rgbRightKey)
        {
            command.SetComputeBufferParam(shader, kernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(shader, kernel,
                "_DepthCalibrationQ48", _calibrationQ48);
            command.SetComputeBufferParam(shader, kernel,
                "_RgbCalibrationQ48", _rgbCalibrationQ48);
            command.SetComputeBufferParam(shader, kernel,
                "_RgbViewOperators", _rgbViewOperators);
            command.SetComputeBufferParam(shader, kernel,
                "_RgbViewSupportScale", _rgbViewSupportScale);
            command.SetComputeBufferParam(shader, kernel, "_PoseResult",
                _poseResult);
            _proofLedger.BindReadOnly(command, shader, kernel);
            command.SetComputeTextureParam(shader, kernel, "_MetricDepth",
                _metricDepth);
            command.SetComputeTextureParam(shader, kernel, "_DepthFlags",
                _depthFlags);
            command.SetComputeTextureParam(shader, kernel, "_PredDepthSupport",
                prediction.DepthSupport);
            command.SetComputeTextureParam(shader, kernel, "_PredCarrierPage",
                prediction.CarrierPage);
            command.SetComputeTextureParam(shader, kernel,
                "_PredCarrierUvNormal", prediction.CarrierUvNormal);
            command.SetComputeTextureParam(shader, kernel, "_PredStateKey",
                prediction.StateKey);
            command.SetComputeTextureParam(shader, kernel,
                "_DepthRayCenterLeft", _coneLuts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(shader, kernel,
                "_DepthRayCenterRight", _coneLuts.DepthRight.CenterRaySolidAngle);
            command.SetComputeTextureParam(shader, kernel,
                "_DepthSlopeBoundsLeft", _coneLuts.DepthLeft.SlopeBounds);
            command.SetComputeTextureParam(shader, kernel,
                "_DepthSlopeBoundsRight", _coneLuts.DepthRight.SlopeBounds);
            command.SetComputeTextureParam(shader, kernel, "_RgbLeft",
                source.RgbLeft.Texture);
            command.SetComputeTextureParam(shader, kernel, "_RgbRight",
                source.RgbRight.Texture);
            command.SetComputeIntParams(shader, "_Resolution",
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeIntParams(shader, "_RgbResolutionLeft",
                source.RgbLeft.Resolution.x, source.RgbLeft.Resolution.y);
            command.SetComputeIntParams(shader, "_RgbResolutionRight",
                source.RgbRight.Resolution.x, source.RgbRight.Resolution.y);
            command.SetComputeIntParam(shader, "_LeftIndependenceKey",
                unchecked((int)leftKey));
            command.SetComputeIntParam(shader, "_RightIndependenceKey",
                unchecked((int)rightKey));
            command.SetComputeIntParam(shader, "_RgbLeftIndependenceKey",
                unchecked((int)rgbLeftKey));
            command.SetComputeIntParam(shader, "_RgbRightIndependenceKey",
                unchecked((int)rgbRightKey));
            command.SetComputeIntParam(shader, "_RgbPhase",
                unchecked((int)(revision & 15u)));
            Matrix4x4 poseReference = PoseMatrix(
                source.DepthLeft.WorldFromCamera);
            command.SetComputeMatrixParam(shader,
                "_PoseConsumeReferenceFromWorld", poseReference.inverse);
            command.SetComputeMatrixParam(shader,
                "_PoseConsumeWorldFromReference", poseReference);
            SetFrameMatrices(command, shader, source);
        }

        private void BindFrameConstants(CommandBuffer command,
            ComputeShader shader, int kernel, StereoRigFrameLease source,
            SigmaPredictionFrameLease prediction, Vector2Int blockResolution,
            uint leftKey, uint rightKey, uint rgbLeftKey, uint rgbRightKey,
            uint revision)
        {
            command.SetComputeIntParams(shader, "_Resolution",
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeIntParams(shader, "_GaugeBlockResolution",
                blockResolution.x, blockResolution.y);
            command.SetComputeIntParam(shader, "_LeftIndependenceKey",
                unchecked((int)leftKey));
            command.SetComputeIntParam(shader, "_RightIndependenceKey",
                unchecked((int)rightKey));
            command.SetComputeIntParam(shader, "_RgbLeftIndependenceKey",
                unchecked((int)rgbLeftKey));
            command.SetComputeIntParam(shader, "_RgbRightIndependenceKey",
                unchecked((int)rgbRightKey));
            command.SetComputeIntParam(shader, "_RgbPhase",
                unchecked((int)(revision & 15u)));
            command.SetComputeBufferParam(shader, kernel, "_PoseResult",
                _poseResult);
            Matrix4x4 poseReference = PoseMatrix(
                source.DepthLeft.WorldFromCamera);
            command.SetComputeMatrixParam(shader,
                "_PoseConsumeReferenceFromWorld", poseReference.inverse);
            command.SetComputeMatrixParam(shader,
                "_PoseConsumeWorldFromReference", poseReference);
            SetFrameMatrices(command, shader, source);
        }

        private void RecordNormalize(CommandBuffer command,
            StereoRigFrameLease source)
        {
            command.SetComputeIntParams(_normalize, "_Resolution",
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeVectorParam(_normalize, "_NearFar",
                new Vector4(source.DepthNearFar.x, source.DepthNearFar.y, 0f, 0f));
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                "_RawDepth", source.DepthLeft.Texture);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                "_DepthRayCenterLeft", _coneLuts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                "_DepthRayCenterRight", _coneLuts.DepthRight.CenterRaySolidAngle);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                "_MetricDepth", _metricDepth);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                "_DepthFlags", _depthFlags);
            command.DispatchCompute(_normalize, _normalizeKernel,
                CeilDiv(source.DepthResolution.x, 8),
                CeilDiv(source.DepthResolution.y, 8), 2);
        }

        private void RecordPoseGauge(CommandBuffer command,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            uint revision)
        {
            for (int component = 0; component < 6; ++component)
                _posePriorUpload[component] = SigmaPackedQ48.FromRaw(0L);
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
            Matrix4x4 leftWorld = PoseMatrix(source.DepthLeft.WorldFromCamera);
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
                "_PoseWorldFromOpticalRight",
                PoseMatrix(source.DepthRight.WorldFromCamera));
            command.SetComputeMatrixParam(_poseGaugeCompute,
                "_PoseReferenceFromWorld", leftWorld.inverse);
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

        private void EnsureCalibration(StereoRigFrameLease source)
        {
            if (_calibration != null && _calibration.IsCompatible(source))
                return;
            if (!RigCalibration.TryCreate(source, out RigCalibration calibration))
                throw new InvalidOperationException(
                    "Unable to freeze inverse rig calibration.");
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

        private void EnsureBlockBuffers(int blockCount)
        {
            if (_blockFlagCapacity >= blockCount)
                return;
            _unmatchedBlockFlags?.Dispose();
            _unmatchedBlockAnchors?.Dispose();
            _blockFlagCapacity = NextPowerOfTwo(blockCount);
            _unmatchedBlockFlags = CreateBuffer(_blockFlagCapacity,
                sizeof(uint), "Sigma unmatched inverse blocks");
            _unmatchedBlockAnchors = CreateBuffer(_blockFlagCapacity,
                sizeof(uint), "Sigma unmatched inverse anchors");
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

        private void UploadExactCalibration(StereoRigFrameLease source)
        {
            FillCalibration(0, source.DepthLeft, source.Health);
            FillCalibration(1, source.DepthRight, source.Health);
            _calibrationQ48.SetData(_calibrationUpload);
            FillRgbCalibration(0, source.RgbLeft.WorldFromCamera, source.Health);
            FillRgbCalibration(1, source.RgbRight.WorldFromCamera, source.Health);
            _rgbCalibrationQ48.SetData(_rgbCalibrationUpload);
        }

        private void FillCalibration(int eye, GpuImageView view,
            RigPairingHealth health)
        {
            int offset = eye * CalibrationStride;
            SetQ(offset + 0, view.Intrinsics.FocalLength.x);
            SetQ(offset + 1, view.Intrinsics.FocalLength.y);
            SetQ(offset + 2, view.Intrinsics.PrincipalPoint.x);
            SetQ(offset + 3, view.Intrinsics.PrincipalPoint.y);
            Matrix4x4 world = PoseMatrix(view.WorldFromCamera);
            int cursor = offset + 4;
            for (int row = 0; row < 3; ++row)
            for (int column = 0; column < 3; ++column)
                SetQ(cursor++, world[row, column]);
            SetQ(offset + 13, world[0, 3]);
            SetQ(offset + 14, world[1, 3]);
            SetQ(offset + 15, world[2, 3]);
            SetQ(offset + 16, view.DepthNearFar.x);
            SetQ(offset + 17, RigDepthContract.FiniteRasterFar(
                view.DepthNearFar));
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

        private void SetFrameMatrices(CommandBuffer command,
            ComputeShader shader, StereoRigFrameLease source)
        {
            Matrix4x4 leftWorld = PoseMatrix(source.DepthLeft.WorldFromCamera);
            Matrix4x4 rightWorld = PoseMatrix(source.DepthRight.WorldFromCamera);
            command.SetComputeMatrixParam(shader, "_WorldFromOpticalLeft",
                leftWorld);
            command.SetComputeMatrixParam(shader, "_WorldFromOpticalRight",
                rightWorld);
            command.SetComputeMatrixParam(shader, "_OpticalFromWorldLeft",
                leftWorld.inverse);
            command.SetComputeMatrixParam(shader, "_OpticalFromWorldRight",
                rightWorld.inverse);
            command.SetComputeVectorParam(shader, "_DepthIntrinsicsLeft",
                IntrinsicsVector(source.DepthLeft.Intrinsics));
            command.SetComputeVectorParam(shader, "_DepthIntrinsicsRight",
                IntrinsicsVector(source.DepthRight.Intrinsics));
            command.SetComputeMatrixParam(shader, "_RgbOpticalFromWorldLeft",
                PoseMatrix(source.RgbLeft.WorldFromCamera).inverse);
            command.SetComputeMatrixParam(shader, "_RgbOpticalFromWorldRight",
                PoseMatrix(source.RgbRight.WorldFromCamera).inverse);
            command.SetComputeVectorParam(shader, "_RgbIntrinsicsLeft",
                IntrinsicsVector(source.RgbLeft.Intrinsics));
            command.SetComputeVectorParam(shader, "_RgbIntrinsicsRight",
                IntrinsicsVector(source.RgbRight.Intrinsics));
        }

        private void FindKernels()
        {
            _normalizeKernel = _normalize.FindKernel("NormalizeStereoDepth");
            _clearKernel = _inverse.FindKernel("ClearInverseFrame");
            _classifyKernel = _inverse.FindKernel("ClassifyDepthFrame");
            _rgbSourceKernel = _rgbSource.FindKernel("BuildRgbSourceCells");
            _commitKernel = _inverse.FindKernel("SolveAndCommitPage");
            _promoteKernel = _inverse.FindKernel("PromoteGaugePage");
            _poseBuildKernel = _poseGaugeCompute.FindKernel(
                "BuildPoseGaugePartials");
            _poseReduceKernel = _poseGaugeCompute.FindKernel("ReducePoseGauge");
            _workClearKernel = _workGraph.FindKernel("ClearInverseWorkGraph");
            _workCompactKernel = _workGraph.FindKernel("CompactInverseWork");
            _workPrepareKernel = _workGraph.FindKernel(
                "PrepareInverseTransactions");
            _workRawPlanKernel = _workGraph.FindKernel("PlanRawReservations");
            _workCommitKernel = _workGraph.FindKernel(
                "CommitInverseTransactions");
            ComputeShader ledger = _proofLedgerShader;
            _proofClearKernel = ledger.FindKernel("ClearProofTransaction");
            _proofReduceKernel = ledger.FindKernel("ReduceProofPage");
            _proofGaugeKernel = ledger.FindKernel("BuildGaugeDemand");
        }

        private void CreatePersistentResources()
        {
            _calibrationQ48 = CreateBuffer(CalibrationStride * 2,
                Marshal.SizeOf<SigmaPackedQ48>(), "Sigma depth calibration Q48");
            _rgbCalibrationQ48 = CreateBuffer(RgbCalibrationStride * 2,
                Marshal.SizeOf<SigmaPackedQ48>(), "Sigma RGB calibration Q48");
            SigmaRgbViewCatalog catalog = SigmaRgbViewCatalog.CreateCanonical();
            var operators = new SigmaPackedQ48[catalog.OperatorRaw.Count];
            for (int index = 0; index < operators.Length; ++index)
                operators[index] = SigmaPackedQ48.FromRaw(
                    catalog.OperatorRaw[index]);
            _rgbViewOperators = CreateBuffer(operators.Length,
                Marshal.SizeOf<SigmaPackedQ48>(), "Sigma RGB view operators");
            _rgbViewOperators.SetData(operators);
            var scales = new uint[catalog.SupportScale.Count];
            for (int index = 0; index < scales.Length; ++index)
                scales[index] = catalog.SupportScale[index];
            _rgbViewSupportScale = CreateBuffer(scales.Length, sizeof(uint),
                "Sigma RGB view support scales");
            _rgbViewSupportScale.SetData(scales);

            _activeFlagCount = Math.Max(1, (_pool.SegmentIndex + 1) * 256);
            _activePageFlags = CreateBuffer(_activeFlagCount, sizeof(uint),
                "Sigma active page flags");
            _conflictRecords = CreateBuffer(conflictCapacity, ConflictStride,
                "Sigma inverse conflict records");
            _conflictCount = CreateBuffer(1, sizeof(uint),
                "Sigma inverse conflict count");
            _frameCounters = CreateBuffer(8, sizeof(uint),
                "Sigma inverse GPU counters");
            _gaugePromotionCounts = CreateBuffer(inverseWorkCapacity,
                sizeof(uint), "Sigma gauge promotion counts");
            _proposalStatus = CreateBuffer(checked(_pool.PageCapacity *
                SigmaCarrier.SamplesPerPage), sizeof(uint),
                "Sigma inverse proposal status");
            _proposalEpoch = CreateBuffer(_pool.PageCapacity, sizeof(uint),
                "Sigma inverse proposal epoch");
            _rgbSourceBounds = CreateBuffer(checked(inverseWorkCapacity * 2 *
                RgbPhaseSamples * 16), sizeof(uint) * 4,
                "Sigma compact RGB source bounds");
            _rgbSourceMeta = CreateBuffer(checked(inverseWorkCapacity * 2 *
                RgbPhaseSamples), sizeof(uint) * 4,
                "Sigma compact RGB source metadata");
            _posePrior = CreateBuffer(12, Marshal.SizeOf<SigmaPackedQ48>(),
                "Sigma exact pose prior");
            _poseResult = CreateBuffer(4, sizeof(uint) * 4,
                "Sigma GPU-resident exact pose meet");
            _inverseWork = CreateBuffer(inverseWorkCapacity, WorkStride,
                "Sigma compact inverse work");
            _inverseWorkControl = CreateBuffer(WorkControlWords, sizeof(uint),
                "Sigma inverse work control");
            _inverseWorkControl.SetData(new uint[WorkControlWords]);
            _dispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments,
                15, sizeof(uint))
            {
                name = "Sigma inverse graph indirect argument arena"
            };
            _dispatchArguments.SetData(new uint[15]);
        }

        private uint NextRevision()
        {
            uint revision = _nextRevision++;
            if (revision == 0u || _nextRevision == 0u)
                throw new OverflowException("Sigma world revision exhausted.");
            return revision;
        }

        private void SetQ(int index, double value) =>
            SetQRaw(index, SigmaNumericDomain.Quantize(value));
        private void SetQRaw(int index, long raw) =>
            _calibrationUpload[index] = SigmaPackedQ48.FromRaw(raw);
        private void SetRgbQ(int index, double value) =>
            SetRgbQRaw(index, SigmaNumericDomain.Quantize(value));
        private void SetRgbQRaw(int index, long raw) =>
            _rgbCalibrationUpload[index] = SigmaPackedQ48.FromRaw(raw);

        private static Matrix4x4 PoseMatrix(Pose pose) => Matrix4x4.TRS(
            pose.position, pose.rotation, Vector3.one);
        private static Vector4 IntrinsicsVector(RigIntrinsics intrinsics) => new(
            intrinsics.FocalLength.x, intrinsics.FocalLength.y,
            intrinsics.PrincipalPoint.x, intrinsics.PrincipalPoint.y);

        private static uint IndependenceKey(GpuImageView view, uint epoch)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, epoch);
                Mix(ref hash, (uint)view.Eye + 1u);
                Vector3 p = view.WorldFromCamera.position;
                Quaternion q = view.WorldFromCamera.rotation;
                Mix(ref hash, (uint)Mathf.RoundToInt(p.x * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(p.y * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(p.z * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(q.x * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(q.y * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(q.z * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(q.w * 64f));
                return hash == 0u ? 1u : hash;
            }
        }

        private static void Mix(ref uint hash, uint value)
        {
            unchecked { hash = (hash ^ value) * 16777619u; }
        }

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
                result = checked(result << 1);
            return result;
        }

        private static GraphicsBuffer CreateBuffer(int count, int stride,
            string name) => new(GraphicsBuffer.Target.Structured,
                Math.Max(1, count), stride) { name = name };
        private static GraphicsBuffer CreateIndirectBuffer(string name) =>
            new(GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint))
            { name = name };

        private static RenderTexture CreateArrayTexture(string name,
            Vector2Int resolution, GraphicsFormat format)
        {
            if (!SystemInfo.IsFormatSupported(format,
                    GraphicsFormatUsage.LoadStore))
                throw new InvalidOperationException(
                    $"Required inverse texture format unsupported: {format}.");
            var descriptor = new RenderTextureDescriptor(resolution.x,
                resolution.y)
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
            _inFlight?.Dispose();
            _inFlight = null;
            _coneLuts?.Retire();
            _coneLuts = null;
            DestroyTexture(_metricDepth);
            DestroyTexture(_depthFlags);
            GraphicsBuffer[] buffers = {
                _calibrationQ48, _rgbCalibrationQ48, _rgbViewOperators,
                _rgbViewSupportScale, _activePageFlags, _unmatchedBlockFlags,
                _unmatchedBlockAnchors, _conflictRecords, _conflictCount,
                _frameCounters, _gaugePromotionCounts, _proposalStatus,
                _proposalEpoch, _rgbSourceBounds, _rgbSourceMeta, _posePrior,
                _poseResult, _posePartials, _inverseWork,
                _inverseWorkControl, _dispatchArguments
            };
            for (int index = 0; index < buffers.Length; ++index)
                buffers[index]?.Dispose();
            _proofLedger?.Dispose();
            _proofLedger = null;
            _initialized = false;
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

        private sealed class GpuSubmission : IDisposable
        {
            private SigmaPredictionFrameLease _prediction;
            private readonly GraphicsFence _fence;
            internal GpuSubmission(SigmaPredictionFrameLease prediction,
                GraphicsFence fence)
            {
                _prediction = prediction;
                _fence = fence;
            }
            internal bool Discard { get; set; }
            internal bool IsComplete => _fence.passed;
            public void Dispose()
            {
                _prediction?.Dispose();
                _prediction = null;
            }
        }
    }

    public readonly struct SigmaInverseDiagnosticSnapshot
    {
        private SigmaInverseDiagnosticSnapshot(uint revision)
        {
            ActivePages = 0u;
            HitSamples = 0u;
            ChangedSamples = 0u;
            EmptyMeets = 0u;
            Exclusions = 0u;
            UnmatchedBlocks = 0u;
            PromotedSamples = 0u;
            FailedChecks = 0u;
            EvidenceRecords = revision;
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
        internal static SigmaInverseDiagnosticSnapshot GpuResident(
            uint revision) => new(revision);
    }
}
