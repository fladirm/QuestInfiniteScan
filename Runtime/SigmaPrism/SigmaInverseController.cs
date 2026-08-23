using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Fixed host recorder for the bounded S4-08.3 GPU state machine.  The CPU
    /// owns only immutable ingress uploads, resources and completion lifetimes.
    /// Admission, scheduling, exact inverse/proof/transition decisions and
    /// manifest publication remain GPU-resident and independent of frame timing.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SigmaCarrier))]
    [RequireComponent(typeof(SigmaTopologyController))]
    [RequireComponent(typeof(SigmaRenderer))]
    [RequireComponent(typeof(SigmaRigBridge))]
    [DefaultExecutionOrder(0)]
    public sealed class SigmaInverseController : MonoBehaviour, IRoomScanModule
    {
        private const string IngressResource =
            "SigmaPrism/SigmaStreamIngress";
        private const string ConeLutResource = "SigmaPrism/ConeLut";
        private const string PoseGaugeResource = "SigmaPrism/SigmaPoseGauge";
        private const int CalibrationStride = 36;
        private const int RgbCalibrationStride = 8;
        private const int PosePriorValueCount = 15;

        [Header("Bounded streaming graph")]
        [SerializeField, Range(3, 8)] private int ingressSlotCount = 4;
        [SerializeField, Range(1024, 8192)] private int rawTileCapacity = 4096;
        [SerializeField, Range(0.005f, 0.1f)]
        private float poseTranslationPriorMetres = 0.03f;
        [SerializeField, Range(0.25f, 5f)]
        private float poseRotationPriorDegrees = 2f;
        [SerializeField, Range(4, 32)] private int poseSampleStride = 16;

        private readonly Queue<SigmaPredictionFrameLease> _pendingIngress =
            new();
        private readonly SigmaPackedQ48[] _calibrationUpload =
            new SigmaPackedQ48[CalibrationStride * 2];
        private readonly SigmaPackedQ48[] _rgbCalibrationUpload =
            new SigmaPackedQ48[RgbCalibrationStride * 2];
        private readonly SigmaPackedQ48[] _posePriorUpload =
            new SigmaPackedQ48[PosePriorValueCount];
        private readonly LatencyTracker _ingressLatency = new();
        private readonly LatencyTracker _canonicalLatency = new();
        private readonly LatencyTracker _derivedLatency = new();
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];

        private RoomScanner _scanner;
        private SigmaCarrier _carrier;
        private SigmaTopologyController _topology;
        private SigmaRenderer _renderer;
        private SigmaRigBridge _rigBridge;
        private SigmaExactBackendGate _backendGate;
        private SigmaCarrierReadBatch _pool;
        private SigmaConstraintLedger _proofLedger;
        private SigmaStreamingResources _stream;
        private SigmaStreamingGraph _graph;
        private SigmaDiagnosticTelemetry _diagnosticTelemetry;
        private RigCalibration _calibration;
        private RigConeLutSet _coneLuts;

        private ComputeShader _ingressShader;
        private ComputeShader _coneLutShader;
        private ComputeShader _poseGaugeShader;
        private int _normalizeKernel;
        private int _clearClassificationKernel;
        private int _classifyKernel;
        private int _poseBuildKernel;
        private int _poseReduceKernel;
        private int _poseCalibrationKernel;

        private GraphicsBuffer _rgbViewOperators;
        private GraphicsBuffer _rgbViewSupportScale;
        private IngressSlot[] _ingressSlots;
        private int _activeFlagCount;

        private Submission _initialization;
        private Submission _canonical;
        private Submission _derived;
        private SigmaGpuCompletionTicket _lastCompletion;
        private bool _hasLastCompletion;
        private bool _graphReady;
        private bool _running;
        private bool _initialized;
        private bool _disposed;
        private bool _completionFaulted;
        private uint _nextRevision = 1u;
        private Pose _previousTrackingPose;
        private long _previousTrackingTimestampNs;
        private bool _hasPreviousTrackingPose;

        public string ModuleName => "Sigma bounded streaming RGB-D inverse";
        public bool IsInitialized => _initialized && !_disposed;
        public long SubmittedFrames { get; private set; }
        public long CommittedFrames { get; private set; }
        public long DroppedFrames { get; private set; }
        public long FailedFrames { get; private set; }
        public long CommittedPageGenerations { get; private set; }
        public long AllocatedGaugePages { get; private set; }
        public long PeakCompletionAgeFrames { get; private set; }
        public int PendingCompletionTickets => CountPendingTickets() +
            SigmaGpuRetirement.PendingCount;
        public GraphicsBuffer PerformanceCounters => _stream?.Diagnostics;
        public SigmaRuntimeTelemetrySnapshot RuntimeTelemetry =>
            _diagnosticTelemetry?.Snapshot ??
            SigmaRuntimeTelemetrySnapshot.Awaiting;

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
                    "Sigma inverse requires the exact backend gate.");
            SigmaGpuCompletion.RequireSupported();

            _ingressShader = Resources.Load<ComputeShader>(IngressResource);
            _coneLutShader = Resources.Load<ComputeShader>(ConeLutResource);
            _poseGaugeShader = Resources.Load<ComputeShader>(PoseGaugeResource);
            if (_carrier == null || _topology == null || _renderer == null ||
                _rigBridge == null || _ingressShader == null ||
                _coneLutShader == null || _poseGaugeShader == null)
                throw new InvalidOperationException(
                    "Sigma streaming inverse resources are incomplete.");

            FindKernels();
            _pool = _carrier.AcquireGpuManagedPool();
            _proofLedger = new SigmaConstraintLedger(_pool.PageCapacity,
                rawTileCapacity, _backendGate,
                SigmaGeneratedStreaming.TransactionCapacity);
            _stream = new SigmaStreamingResources(_pool.PageCapacity);
            _diagnosticTelemetry = new SigmaDiagnosticTelemetry();
            _renderer.BindStreamingManifest(_pool.SegmentIndex,
                _stream.PublicationManifests, _stream.PageVisibility,
                _pool.PageCapacity);
            CreatePersistentResources();
            _topology.EnsureSegmentViews();
            if (!_topology.TryGetSegmentView(_pool.SegmentIndex,
                    out SigmaTopologySegmentView topologyView))
                throw new InvalidOperationException(
                    "Streaming topology cache is unavailable.");
            _graph = new SigmaStreamingGraph(_pool, _proofLedger, _stream,
                _backendGate, _renderer, _rgbViewOperators,
                _rgbViewSupportScale,
                topologyView);
            SigmaStreamingResources.ValidateTransientBudget(
                _proofLedger.CertificateBytes,
                _proofLedger.RawObservationBytes, _stream.OwnedBytes);
            RecordGraphInitialization();
            _renderer.PredictionReady += OnPredictionReady;
            _initialized = true;
            Logger.Info($"Sigma bounded stream ready: ingress={ingressSlotCount}, " +
                        $"transactions={SigmaGeneratedStreaming.TransactionCapacity}, " +
                        $"bundles={SigmaGeneratedStreaming.BundleCapacity}.");
        }

        public void OnScanStarted()
        {
            if (_completionFaulted)
            {
                Logger.Error("Sigma inverse cannot resume after an unproven GPU " +
                             "completion fault; restart the application.");
                return;
            }
            _running = true;
            _hasPreviousTrackingPose = false;
            _previousTrackingTimestampNs = 0L;
            _ingressLatency.Reset();
            _canonicalLatency.Reset();
            _derivedLatency.Reset();
        }

        public void OnScanStopped()
        {
            _running = false;
            ReleasePendingIngress();
            // Sealed transactions and their GPU cursors intentionally survive a
            // stop/resume.  In-flight short ingress is allowed to finish and then
            // releases its capture/prediction leases at its own fence.
        }

        private void OnPredictionReady(SigmaPredictionFrameLease prediction)
        {
            if (!_running || !_initialized || prediction == null ||
                prediction.IsDisposed || _completionFaulted)
                return;
            _pendingIngress.Enqueue(prediction.Retain());
        }

        private void LateUpdate()
        {
            if (!_initialized || _disposed)
                return;

            PollInitialization();
            PollIngress();
            PollCanonical();
            PollDerived();
            FrameTimingManager.CaptureFrameTimings();
            if (_completionFaulted || !_graphReady)
                return;

            if (_running && _pendingIngress.Count != 0 && TryGetFreeIngressSlot(
                    out IngressSlot slot))
            {
                SigmaPredictionFrameLease prediction =
                    _pendingIngress.Dequeue();
                try
                {
                    SubmitIngress(slot, prediction);
                }
                catch (Exception exception)
                {
                    prediction.Dispose();
                    FailedFrames++;
                    LatchCompletionFault("Sigma ingress submission failed: " +
                        exception.Message);
                }
            }

            if (!_completionFaulted && _canonical == null && _derived == null)
                SubmitCanonicalAndDerived();

            float telemetryTime = Time.unscaledTime;
            if (_diagnosticTelemetry?.IsDue(telemetryTime) == true)
            {
                _renderer.TryGetStreamingReadoutDiagnostics(
                    out GraphicsBuffer drawArguments,
                    out GraphicsBuffer currentPageSlots,
                    out GraphicsBuffer readoutVertices,
                    out int readoutPageCapacity);
                _diagnosticTelemetry.Tick(telemetryTime, SubmittedFrames,
                    CommittedFrames, unchecked(_nextRevision - 1u),
                    _backendGate.Buffer, _stream.Diagnostics,
                    _stream.SchedulerControl, _stream.WorkCounts,
                    _stream.Transactions, _topology.DiagnosticCounters,
                    drawArguments, currentPageSlots, readoutVertices,
                    readoutPageCapacity, CaptureTimingTelemetry());
            }
        }

        private SigmaRuntimeTimingTelemetry CaptureTimingTelemetry()
        {
            uint timingCount = FrameTimingManager.GetLatestTimings(1,
                _frameTimings);
            bool hasFrameTiming = timingCount != 0u &&
                _frameTimings[0].cpuFrameTime > 0.0 &&
                _frameTimings[0].gpuFrameTime > 0.0;
            return new SigmaRuntimeTimingTelemetry(
                _ingressLatency.Snapshot,
                _canonicalLatency.Snapshot,
                _derivedLatency.Snapshot,
                hasFrameTiming ? _frameTimings[0].cpuFrameTime : 0.0,
                hasFrameTiming ? _frameTimings[0].gpuFrameTime : 0.0,
                hasFrameTiming);
        }

        private void RecordGraphInitialization()
        {
            CommandBuffer command = CommandBufferPool.Get(
                "Sigma-PRISM-16 Stream Initialize");
            try
            {
                _graph.RecordInitialize(command);
                SigmaGpuCompletionTicket ticket =
                    SigmaGpuCompletion.RecordAfterAllWork(command);
                Graphics.ExecuteCommandBuffer(command);
                _initialization = new Submission(ticket,
                    "stream initialization", Time.realtimeSinceStartupAsDouble);
                TrackLast(ticket);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private void SubmitIngress(IngressSlot slot,
            SigmaPredictionFrameLease prediction)
        {
            StereoRigFrameLease source = prediction.Source;
            if (!source.IsValid)
                throw new InvalidOperationException(
                    "Inverse source lease is invalid.");
            EnsureCalibration(source);
            Vector2Int blockResolution = new(
                CeilDiv(source.DepthResolution.x, 32),
                CeilDiv(source.DepthResolution.y, 32));
            int blockCount = checked(blockResolution.x * blockResolution.y);
            slot.EnsureFrameResources(source.DepthResolution, blockCount);

            uint revision = NextRevision();
            uint leftKey = IndependenceKey(source.DepthLeft,
                source.CalibrationEpoch);
            uint rightKey = IndependenceKey(source.DepthRight,
                source.CalibrationEpoch);
            uint rgbLeftKey = IndependenceKey(source.RgbLeft,
                source.CalibrationEpoch);
            uint rgbRightKey = IndependenceKey(source.RgbRight,
                source.CalibrationEpoch);
            UploadExactCalibration(slot, source);
            UploadPosePrior(slot, source);
            _proofLedger.StageGpuFrame(slot.FrameStaging, source, revision,
                leftKey, rightKey, rgbLeftKey, rgbRightKey);

            ConeLutLease luts = _coneLuts.Acquire();
            CommandBuffer command = CommandBufferPool.Get(
                "Sigma-PRISM-16 Short Owned Ingress");
            SigmaPredictionFrameLease correctedPrediction = null;
            try
            {
                command.BeginSample("Sigma.Ingress.Normalize");
                RecordNormalize(command, slot, source, luts);
                command.EndSample("Sigma.Ingress.Normalize");
                command.BeginSample("Sigma.Ingress.PoseGauge");
                RecordPoseGauge(command, slot, source, prediction, revision,
                    luts);
                RecordCorrectedCalibration(command, slot, source);
                command.EndSample("Sigma.Ingress.PoseGauge");
                command.BeginSample("Sigma.Ingress.Reraster");
                if (!_renderer.TryRecordPoseGaugePrediction(command, source,
                        slot.PoseResult, out correctedPrediction))
                    throw new InvalidOperationException(
                        "Unable to reserve same-frame corrected prediction.");
                command.EndSample("Sigma.Ingress.Reraster");
                command.BeginSample("Sigma.Ingress.Seal");
                RecordClassification(command, slot, source,
                    correctedPrediction, blockResolution, blockCount, luts);
                _graph.RecordIngress(command, source, correctedPrediction,
                    slot.MetricDepth, slot.DepthFlags,
                    slot.CorrectedDepthCalibration,
                    slot.CorrectedRgbCalibration, slot.PoseResult,
                    slot.FrameStaging, slot.ActivePageFlags,
                    slot.UnmatchedBlockFlags, blockResolution, blockCount,
                    revision, leftKey, rightKey, rgbLeftKey, rgbRightKey,
                    source.CalibrationEpoch,
                    luts.DepthLeft.CenterRaySolidAngle);
                command.EndSample("Sigma.Ingress.Seal");
                SigmaGpuCompletionTicket ticket =
                    SigmaGpuCompletion.RecordAfterAllWork(command);
                Graphics.ExecuteCommandBuffer(command);
                slot.Begin(prediction, correctedPrediction, luts, ticket,
                    revision, Time.realtimeSinceStartupAsDouble);
                correctedPrediction = null;
                luts = null;
                SubmittedFrames++;
                TrackLast(ticket);
            }
            catch
            {
                correctedPrediction?.Dispose();
                luts?.Dispose();
                throw;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private void SubmitCanonicalAndDerived()
        {
            CommandBuffer canonical = CommandBufferPool.Get(
                "Sigma-PRISM-16 Bounded Canonical Quantum");
            try
            {
                _graph.RecordCanonicalQuantum(canonical,
                    _topology.SingularShift,
                    _topology.AssociatorShift);
                SigmaGpuCompletionTicket canonicalTicket =
                    SigmaGpuCompletion.RecordAfterAllWork(canonical);
                Graphics.ExecuteCommandBuffer(canonical);
                _canonical = new Submission(canonicalTicket,
                    "canonical quantum", Time.realtimeSinceStartupAsDouble);
                TrackLast(canonicalTicket);
            }
            catch (Exception exception)
            {
                LatchCompletionFault(
                    "Sigma canonical submission failed: " + exception.Message);
                return;
            }
            finally
            {
                CommandBufferPool.Release(canonical);
            }

            CommandBuffer derived = CommandBufferPool.Get(
                "Sigma-PRISM-16 Bounded Derived Quantum");
            try
            {
                _graph.RecordDerivedQuantum(derived);
                SigmaGpuCompletionTicket derivedTicket =
                    SigmaGpuCompletion.RecordAfterAllWork(derived);
                Graphics.ExecuteCommandBuffer(derived);
                _derived = new Submission(derivedTicket, "derived quantum",
                    Time.realtimeSinceStartupAsDouble);
                TrackLast(derivedTicket);
            }
            catch (Exception exception)
            {
                LatchCompletionFault(
                    "Sigma derived submission failed: " + exception.Message);
            }
            finally
            {
                CommandBufferPool.Release(derived);
            }
        }

        private void PollInitialization()
        {
            if (_initialization == null)
                return;
            SigmaGpuCompletionStatus status = _initialization.Poll(
                out string error);
            if (status == SigmaGpuCompletionStatus.Pending)
            {
                UpdatePeak(_initialization);
                return;
            }
            if (status == SigmaGpuCompletionStatus.Faulted)
            {
                LatchCompletionFault("Sigma stream initialization failed: " +
                    error);
                return;
            }
            _initialization = null;
            _graphReady = true;
        }

        private void PollIngress()
        {
            if (_ingressSlots == null)
                return;
            for (int index = 0; index < _ingressSlots.Length; ++index)
            {
                IngressSlot slot = _ingressSlots[index];
                if (!slot.InFlight)
                    continue;
                SigmaGpuCompletionStatus status = slot.Poll(out string error);
                if (status == SigmaGpuCompletionStatus.Pending)
                {
                    slot.AdvanceAge();
                    PeakCompletionAgeFrames = Math.Max(
                        PeakCompletionAgeFrames, slot.AgeFrames);
                    continue;
                }
                if (status == SigmaGpuCompletionStatus.Faulted)
                {
                    LatchCompletionFault($"Sigma ingress slot {index} failed " +
                        $"closed: {error}");
                    continue;
                }
                _ingressLatency.Add(slot.ElapsedMilliseconds(
                    Time.realtimeSinceStartupAsDouble));
                slot.Complete();
                CommittedFrames++;
            }
        }

        private void PollCanonical()
        {
            if (_canonical == null)
                return;
            SigmaGpuCompletionStatus status = _canonical.Poll(out string error);
            if (status == SigmaGpuCompletionStatus.Pending)
            {
                UpdatePeak(_canonical);
                return;
            }
            if (status == SigmaGpuCompletionStatus.Faulted)
            {
                LatchCompletionFault("Sigma canonical quantum failed closed: " +
                    error);
                return;
            }
            _canonicalLatency.Add(_canonical.ElapsedMilliseconds(
                Time.realtimeSinceStartupAsDouble));
            _canonical = null;
        }

        private void PollDerived()
        {
            if (_derived == null)
                return;
            SigmaGpuCompletionStatus status = _derived.Poll(out string error);
            if (status == SigmaGpuCompletionStatus.Pending)
            {
                UpdatePeak(_derived);
                return;
            }
            if (status == SigmaGpuCompletionStatus.Faulted)
            {
                LatchCompletionFault("Sigma derived quantum failed closed: " +
                    error);
                return;
            }
            _derivedLatency.Add(_derived.ElapsedMilliseconds(
                Time.realtimeSinceStartupAsDouble));
            _derived = null;
        }

        private void LatchCompletionFault(string message)
        {
            if (_completionFaulted)
                return;
            _completionFaulted = true;
            _running = false;
            FailedFrames++;
            ReleasePendingIngress();
            Logger.Error(message);
        }

        private void RecordNormalize(CommandBuffer command, IngressSlot slot,
            StereoRigFrameLease source, ConeLutLease luts)
        {
            command.SetComputeIntParams(_ingressShader, "_Resolution",
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeVectorParam(_ingressShader, "_NearFar",
                new Vector4(source.DepthNearFar.x, source.DepthNearFar.y,
                    0f, 0f));
            command.SetComputeTextureParam(_ingressShader, _normalizeKernel,
                "_RawDepth", source.DepthLeft.Texture);
            command.SetComputeTextureParam(_ingressShader, _normalizeKernel,
                "_DepthRayCenterLeft",
                luts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_ingressShader, _normalizeKernel,
                "_DepthRayCenterRight",
                luts.DepthRight.CenterRaySolidAngle);
            command.SetComputeTextureParam(_ingressShader, _normalizeKernel,
                "_MetricDepth", slot.MetricDepth);
            command.SetComputeTextureParam(_ingressShader, _normalizeKernel,
                "_DepthFlags", slot.DepthFlags);
            command.DispatchCompute(_ingressShader, _normalizeKernel,
                CeilDiv(source.DepthResolution.x, 8),
                CeilDiv(source.DepthResolution.y, 8), 2);
        }

        private void RecordClassification(CommandBuffer command,
            IngressSlot slot, StereoRigFrameLease source,
            SigmaPredictionFrameLease prediction,
            Vector2Int blockResolution, int blockCount, ConeLutLease luts)
        {
            int clearCount = Math.Max(_activeFlagCount, blockCount);
            command.SetComputeIntParam(_ingressShader, "_ActiveFlagCount",
                _activeFlagCount);
            command.SetComputeIntParam(_ingressShader, "_BlockFlagCount",
                blockCount);
            command.SetComputeIntParam(_ingressShader, "_ClearCount",
                clearCount);
            command.SetComputeBufferParam(_ingressShader,
                _clearClassificationKernel, "_ActivePageFlags",
                slot.ActivePageFlags);
            command.SetComputeBufferParam(_ingressShader,
                _clearClassificationKernel, "_UnmatchedBlockFlags",
                slot.UnmatchedBlockFlags);
            command.DispatchCompute(_ingressShader,
                _clearClassificationKernel, CeilDiv(clearCount, 64), 1, 1);

            command.SetComputeIntParams(_ingressShader, "_Resolution",
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeIntParams(_ingressShader,
                "_GaugeBlockResolution", blockResolution.x,
                blockResolution.y);
            command.SetComputeIntParam(_ingressShader, "_ActiveFlagCount",
                _activeFlagCount);
            command.SetComputeIntParam(_ingressShader, "_BlockFlagCount",
                blockCount);
            command.SetComputeIntParam(_ingressShader, "_SegmentCount",
                _pool.SegmentIndex + 1);
            command.SetComputeBufferParam(_ingressShader, _classifyKernel,
                "_PoseResult", slot.PoseResult);
            command.SetComputeBufferParam(_ingressShader, _classifyKernel,
                "_ActivePageFlags", slot.ActivePageFlags);
            command.SetComputeBufferParam(_ingressShader, _classifyKernel,
                "_UnmatchedBlockFlags", slot.UnmatchedBlockFlags);
            command.SetComputeTextureParam(_ingressShader, _classifyKernel,
                "_MetricDepth", slot.MetricDepth);
            command.SetComputeTextureParam(_ingressShader, _classifyKernel,
                "_DepthFlags", slot.DepthFlags);
            command.SetComputeTextureParam(_ingressShader, _classifyKernel,
                "_PredDepthSupport", prediction.DepthSupport);
            command.SetComputeTextureParam(_ingressShader, _classifyKernel,
                "_PredCarrierPage", prediction.CarrierPage);
            command.SetComputeTextureParam(_ingressShader, _classifyKernel,
                "_PredStateKey", prediction.StateKey);
            command.SetComputeTextureParam(_ingressShader, _classifyKernel,
                "_DepthRayCenterLeft",
                luts.DepthLeft.CenterRaySolidAngle);
            Matrix4x4 leftWorld = PoseMatrix(
                source.DepthLeft.WorldFromCamera);
            command.SetComputeMatrixParam(_ingressShader,
                "_WorldFromOpticalLeft", leftWorld);
            command.SetComputeMatrixParam(_ingressShader,
                "_OpticalFromWorldRight", PoseMatrix(
                    source.DepthRight.WorldFromCamera).inverse);
            command.SetComputeVectorParam(_ingressShader,
                "_DepthIntrinsicsRight",
                IntrinsicsVector(source.DepthRight.Intrinsics));
            command.SetComputeMatrixParam(_ingressShader,
                "_PoseConsumeReferenceFromWorld", leftWorld.inverse);
            command.SetComputeMatrixParam(_ingressShader,
                "_PoseConsumeWorldFromReference", leftWorld);
            command.DispatchCompute(_ingressShader, _classifyKernel,
                CeilDiv(source.DepthResolution.x, 8),
                CeilDiv(source.DepthResolution.y, 8), 2);
        }

        private void RecordPoseGauge(CommandBuffer command, IngressSlot slot,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            uint revision, ConeLutLease luts)
        {
            int sampleWidth = CeilDiv(source.DepthResolution.x,
                poseSampleStride);
            int sampleHeight = CeilDiv(source.DepthResolution.y,
                poseSampleStride);
            int partialCount = CeilDiv(checked(sampleWidth * sampleHeight * 2),
                64);
            slot.EnsurePosePartials(partialCount);
            command.SetComputeBufferParam(_poseGaugeShader, _poseBuildKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_poseGaugeShader, _poseBuildKernel,
                "_DepthCalibrationQ48", slot.RawDepthCalibration);
            command.SetComputeBufferParam(_poseGaugeShader, _poseBuildKernel,
                "_PosePrior", slot.PosePrior);
            command.SetComputeBufferParam(_poseGaugeShader, _poseBuildKernel,
                "_PosePartials", slot.PosePartials);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PoseMetricDepth", slot.MetricDepth);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PoseDepthFlags", slot.DepthFlags);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PosePredDepthSupport", prediction.DepthSupport);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PosePredCarrierUvNormal", prediction.CarrierUvNormal);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PoseRayLeft", luts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PoseRayRight", luts.DepthRight.CenterRaySolidAngle);
            command.SetComputeIntParams(_poseGaugeShader, "_PoseResolution",
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeIntParam(_poseGaugeShader, "_PoseSampleStride",
                poseSampleStride);
            command.SetComputeIntParam(_poseGaugeShader, "_PoseRevision",
                unchecked((int)revision));
            command.SetComputeIntParam(_poseGaugeShader, "_PosePartialCount",
                partialCount);
            command.DispatchCompute(_poseGaugeShader, _poseBuildKernel,
                partialCount, 1, 1);

            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_PosePrior", slot.PosePrior);
            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_PosePartials", slot.PosePartials);
            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_PoseResult", slot.PoseResult);
            command.DispatchCompute(_poseGaugeShader, _poseReduceKernel,
                1, 1, 1);
        }

        private void RecordCorrectedCalibration(CommandBuffer command,
            IngressSlot slot, StereoRigFrameLease source)
        {
            Matrix4x4 referenceWorld = PoseMatrix(
                source.DepthLeft.WorldFromCamera);
            command.SetComputeBufferParam(_poseGaugeShader,
                _poseCalibrationKernel, "_DepthCalibrationQ48",
                slot.RawDepthCalibration);
            command.SetComputeBufferParam(_poseGaugeShader,
                _poseCalibrationKernel, "_PoseRgbCalibrationQ48",
                slot.RawRgbCalibration);
            command.SetComputeBufferParam(_poseGaugeShader,
                _poseCalibrationKernel, "_CorrectedDepthCalibrationQ48",
                slot.CorrectedDepthCalibration);
            command.SetComputeBufferParam(_poseGaugeShader,
                _poseCalibrationKernel, "_CorrectedRgbCalibrationQ48",
                slot.CorrectedRgbCalibration);
            command.SetComputeBufferParam(_poseGaugeShader,
                _poseCalibrationKernel, "_PoseResult", slot.PoseResult);
            command.SetComputeMatrixParam(_poseGaugeShader,
                "_PoseConsumeReferenceFromWorld", referenceWorld.inverse);
            command.SetComputeMatrixParam(_poseGaugeShader,
                "_PoseConsumeWorldFromReference", referenceWorld);
            command.DispatchCompute(_poseGaugeShader,
                _poseCalibrationKernel, 2, 1, 1);
        }

        private void UploadExactCalibration(IngressSlot slot,
            StereoRigFrameLease source)
        {
            FillCalibration(0, source.DepthLeft, source.Health);
            FillCalibration(1, source.DepthRight, source.Health);
            slot.RawDepthCalibration.SetData(_calibrationUpload);
            FillRgbCalibration(0, source.RgbLeft.WorldFromCamera,
                source.Health);
            FillRgbCalibration(1, source.RgbRight.WorldFromCamera,
                source.Health);
            slot.RawRgbCalibration.SetData(_rgbCalibrationUpload);
        }

        private void UploadPosePrior(IngressSlot slot,
            StereoRigFrameLease source)
        {
            for (int component = 0; component < 6; ++component)
                _posePriorUpload[component] = SigmaPackedQ48.FromRaw(0L);
            Vector2 envelope = BuildTrackingPriorEnvelope(source);
            long translationWidth = SigmaNumericDomain.Quantize(envelope.x);
            long rotationWidth = SigmaNumericDomain.Quantize(envelope.y);
            for (int component = 0; component < 6; ++component)
                _posePriorUpload[6 + component] = SigmaPackedQ48.FromRaw(
                    component < 3 ? translationWidth : rotationWidth);
            _posePriorUpload[12] = SigmaPackedQ48.FromRaw(
                SigmaNumericDomain.Quantize(0.00025));
            _posePriorUpload[13] = SigmaPackedQ48.FromRaw(
                SigmaNumericDomain.Quantize(0.15));
            _posePriorUpload[14] = SigmaPackedQ48.FromRaw(
                SigmaNumericDomain.Quantize(0.03));
            slot.PosePrior.SetData(_posePriorUpload);
        }

        private Vector2 BuildTrackingPriorEnvelope(StereoRigFrameLease source)
        {
            Pose current = source.DepthLeft.WorldFromCamera;
            long timestamp = source.DepthLeft.Timestamp.UnixNanoseconds;
            float translation = poseTranslationPriorMetres;
            float rotation = poseRotationPriorDegrees * Mathf.Deg2Rad;
            if (_hasPreviousTrackingPose &&
                timestamp > _previousTrackingTimestampNs)
            {
                double deltaSeconds =
                    (timestamp - _previousTrackingTimestampNs) * 1e-9;
                long timingNanoseconds = SaturatingAdd(
                    Math.Max(0L,
                        source.Health.ClockUncertaintyNanoseconds),
                    SaturatingAdd(AbsNanoseconds(
                            source.Health.RgbDepthDeltaNanoseconds),
                        AbsNanoseconds(
                            source.Health.RgbDeltaNanoseconds) / 2L));
                double uncertaintySeconds = timingNanoseconds * 1e-9;
                float distance = Vector3.Distance(
                    _previousTrackingPose.position, current.position);
                float angle = Quaternion.Angle(
                    _previousTrackingPose.rotation, current.rotation) *
                    Mathf.Deg2Rad;
                float linearRate = distance /
                    (float)Math.Max(deltaSeconds, 1e-6);
                float angularRate = angle /
                    (float)Math.Max(deltaSeconds, 1e-6);
                translation = Mathf.Clamp(0.003f + linearRate *
                        (float)uncertaintySeconds * 2f +
                        MaxRigTranslationResidual(source),
                    0.003f, poseTranslationPriorMetres);
                rotation = Mathf.Clamp(0.25f * Mathf.Deg2Rad +
                        angularRate * (float)uncertaintySeconds * 2f +
                        MaxRigRotationResidual(source),
                    0.25f * Mathf.Deg2Rad,
                    poseRotationPriorDegrees * Mathf.Deg2Rad);
            }
            _previousTrackingPose = current;
            _previousTrackingTimestampNs = timestamp;
            _hasPreviousTrackingPose = true;
            return new Vector2(translation, rotation);
        }

        private float MaxRigTranslationResidual(StereoRigFrameLease source)
        {
            RigExtrinsicsSnapshot reference = _calibration.ReferenceExtrinsics;
            RigExtrinsicsSnapshot current = source.Extrinsics;
            return Mathf.Max(
                Vector3.Distance(reference.LeftDepthFromRightDepth.position,
                    current.LeftDepthFromRightDepth.position),
                Vector3.Distance(reference.LeftRgbFromLeftDepth.position,
                    current.LeftRgbFromLeftDepth.position),
                Vector3.Distance(reference.RightRgbFromRightDepth.position,
                    current.RightRgbFromRightDepth.position));
        }

        private float MaxRigRotationResidual(StereoRigFrameLease source)
        {
            RigExtrinsicsSnapshot reference = _calibration.ReferenceExtrinsics;
            RigExtrinsicsSnapshot current = source.Extrinsics;
            return Mathf.Deg2Rad * Mathf.Max(
                Quaternion.Angle(reference.LeftDepthFromRightDepth.rotation,
                    current.LeftDepthFromRightDepth.rotation),
                Quaternion.Angle(reference.LeftRgbFromLeftDepth.rotation,
                    current.LeftRgbFromLeftDepth.rotation),
                Quaternion.Angle(reference.RightRgbFromRightDepth.rotation,
                    current.RightRgbFromRightDepth.rotation));
        }

        private void EnsureCalibration(StereoRigFrameLease source)
        {
            if (_calibration != null && _calibration.IsCompatible(source))
                return;
            if (!RigCalibration.TryCreate(source,
                    out RigCalibration calibration))
                throw new InvalidOperationException(
                    "Unable to freeze inverse rig calibration.");
            _coneLuts?.Retire();
            _calibration = calibration;
            _coneLuts = RigConeLutSet.Create(_coneLutShader, calibration);
        }

        private void FindKernels()
        {
            _normalizeKernel = _ingressShader.FindKernel(
                "NormalizeStereoDepth");
            _clearClassificationKernel = _ingressShader.FindKernel(
                "ClearIngressClassification");
            _classifyKernel = _ingressShader.FindKernel("ClassifyIngress");
            _poseBuildKernel = _poseGaugeShader.FindKernel(
                "BuildPoseGaugePartials");
            _poseReduceKernel = _poseGaugeShader.FindKernel(
                "ReducePoseGauge");
            _poseCalibrationKernel = _poseGaugeShader.FindKernel(
                "BuildCorrectedCalibration");
        }

        private void CreatePersistentResources()
        {
            int packedStride = Marshal.SizeOf<SigmaPackedQ48>();
            SigmaRgbViewCatalog catalog = SigmaRgbViewCatalog.CreateCanonical();
            var operators = new SigmaPackedQ48[catalog.OperatorRaw.Count];
            for (int index = 0; index < operators.Length; ++index)
                operators[index] = SigmaPackedQ48.FromRaw(
                    catalog.OperatorRaw[index]);
            _rgbViewOperators = CreateBuffer(operators.Length, packedStride,
                "Sigma RGB view operators");
            _rgbViewOperators.SetData(operators);
            var scales = new uint[catalog.SupportScale.Count];
            for (int index = 0; index < scales.Length; ++index)
                scales[index] = catalog.SupportScale[index];
            _rgbViewSupportScale = CreateBuffer(scales.Length, sizeof(uint),
                "Sigma RGB view support scales");
            _rgbViewSupportScale.SetData(scales);

            _activeFlagCount = Math.Max(1,
                (_pool.SegmentIndex + 1) *
                SigmaCarrier.MaximumPagesPerSegment);
            ingressSlotCount = Mathf.Clamp(ingressSlotCount, 3, 8);
            _ingressSlots = new IngressSlot[ingressSlotCount];
            for (int index = 0; index < _ingressSlots.Length; ++index)
            {
                _ingressSlots[index] = new IngressSlot(
                    CreateBuffer(CalibrationStride * 2, packedStride,
                        $"Sigma ingress {index} raw depth calibration"),
                    CreateBuffer(RgbCalibrationStride * 2, packedStride,
                        $"Sigma ingress {index} raw RGB calibration"),
                    CreateBuffer(CalibrationStride * 2, packedStride,
                        $"Sigma ingress {index} corrected depth calibration"),
                    CreateBuffer(RgbCalibrationStride * 2, packedStride,
                        $"Sigma ingress {index} corrected RGB calibration"),
                    CreateBuffer(PosePriorValueCount, packedStride,
                        $"Sigma ingress {index} pose prior"),
                    CreateBuffer(4, sizeof(uint) * 4,
                        $"Sigma ingress {index} pose result"),
                    _proofLedger.CreateStreamingFrameStagingBuffer(
                        $"Sigma ingress {index} provenance"),
                    CreateBuffer(_activeFlagCount, sizeof(uint),
                        $"Sigma ingress {index} active carrier pages"),
                    CreateBuffer(1, sizeof(uint),
                        $"Sigma ingress {index} unmatched blocks"), index);
            }
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

        private bool TryGetFreeIngressSlot(out IngressSlot result)
        {
            for (int index = 0; index < _ingressSlots.Length; ++index)
            {
                if (_ingressSlots[index].InFlight)
                    continue;
                result = _ingressSlots[index];
                return true;
            }
            result = null;
            return false;
        }

        private int CountPendingTickets()
        {
            int count = _initialization != null ? 1 : 0;
            if (_canonical != null)
                count++;
            if (_derived != null)
                count++;
            if (_ingressSlots != null)
            {
                for (int index = 0; index < _ingressSlots.Length; ++index)
                    if (_ingressSlots[index].InFlight)
                        count++;
            }
            return count;
        }

        private void ReleasePendingIngress()
        {
            while (_pendingIngress.Count != 0)
                _pendingIngress.Dequeue().Dispose();
        }

        private void TrackLast(SigmaGpuCompletionTicket ticket)
        {
            _lastCompletion = ticket;
            _hasLastCompletion = true;
        }

        private void UpdatePeak(Submission submission)
        {
            submission.AdvanceAge();
            PeakCompletionAgeFrames = Math.Max(PeakCompletionAgeFrames,
                submission.AgeFrames);
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

        private static long AbsNanoseconds(long value) => value == long.MinValue
            ? long.MaxValue : Math.Abs(value);
        private static long SaturatingAdd(long left, long right) =>
            left > long.MaxValue - right ? long.MaxValue : left + right;
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
            ReleasePendingIngress();

            IngressSlot[] slots = _ingressSlots;
            _ingressSlots = null;
            RigConeLutSet coneLuts = _coneLuts;
            _coneLuts = null;
            GraphicsBuffer[] buffers = {
                _rgbViewOperators, _rgbViewSupportScale
            };
            SigmaConstraintLedger ledger = _proofLedger;
            SigmaStreamingResources stream = _stream;
            SigmaDiagnosticTelemetry diagnosticTelemetry =
                _diagnosticTelemetry;
            _diagnosticTelemetry = null;
            diagnosticTelemetry?.Dispose();
            _renderer?.UnbindStreamingManifest(
                stream?.PublicationManifests);
            _proofLedger = null;
            _stream = null;
            _graph = null;
            _initialized = false;

            void ReleaseOwnedResources()
            {
                if (slots != null)
                    for (int index = 0; index < slots.Length; ++index)
                        slots[index]?.Dispose();
                coneLuts?.Retire();
                for (int index = 0; index < buffers.Length; ++index)
                    buffers[index]?.Dispose();
                stream?.Dispose();
                ledger?.Dispose();
            }

            if (_completionFaulted)
            {
                SigmaGpuRetirement.Quarantine(ReleaseOwnedResources,
                    "Sigma streaming controller resources",
                    "A completion fault left GPU ownership unproven.");
            }
            else if (_hasLastCompletion)
            {
                SigmaGpuRetirement.Retire(_lastCompletion,
                    ReleaseOwnedResources,
                    "Sigma streaming controller teardown");
            }
            else
                ReleaseOwnedResources();
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

        private sealed class Submission
        {
            private readonly SigmaGpuCompletionTicket _ticket;
            private readonly double _submittedAt;

            internal Submission(SigmaGpuCompletionTicket ticket, string label,
                double submittedAt)
            {
                _ticket = ticket;
                Label = label;
                _submittedAt = submittedAt;
            }
            internal string Label { get; }
            internal long AgeFrames { get; private set; }
            internal void AdvanceAge() => AgeFrames++;
            internal double ElapsedMilliseconds(double completedAt) =>
                Math.Max(0.0, completedAt - _submittedAt) * 1000.0;
            internal SigmaGpuCompletionStatus Poll(out string error) =>
                _ticket.Poll(out error);
        }

        private sealed class LatencyTracker
        {
            private long _sampleCount;
            private double _lastMs;
            private double _totalMs;
            private double _maximumMs;

            internal SigmaStageLatencyTelemetry Snapshot =>
                new(_sampleCount, _lastMs,
                    _sampleCount == 0 ? 0.0 : _totalMs / _sampleCount,
                    _maximumMs);

            internal void Add(double milliseconds)
            {
                if (double.IsNaN(milliseconds) ||
                    double.IsInfinity(milliseconds))
                    return;
                milliseconds = Math.Max(0.0, milliseconds);
                _lastMs = milliseconds;
                _totalMs += milliseconds;
                _maximumMs = Math.Max(_maximumMs, milliseconds);
                _sampleCount++;
            }

            internal void Reset()
            {
                _sampleCount = 0L;
                _lastMs = 0.0;
                _totalMs = 0.0;
                _maximumMs = 0.0;
            }
        }

        private sealed class IngressSlot : IDisposable
        {
            private SigmaPredictionFrameLease _prediction;
            private SigmaPredictionFrameLease _correctedPrediction;
            private ConeLutLease _coneLuts;
            private SigmaGpuCompletionTicket _ticket;
            private double _submittedAt;
            private readonly int _index;
            private Vector2Int _resolution;
            private int _posePartialCapacity;
            private int _blockFlagCapacity = 1;

            internal IngressSlot(GraphicsBuffer rawDepthCalibration,
                GraphicsBuffer rawRgbCalibration,
                GraphicsBuffer correctedDepthCalibration,
                GraphicsBuffer correctedRgbCalibration,
                GraphicsBuffer posePrior, GraphicsBuffer poseResult,
                GraphicsBuffer frameStaging, GraphicsBuffer activePageFlags,
                GraphicsBuffer unmatchedBlockFlags, int index)
            {
                RawDepthCalibration = rawDepthCalibration;
                RawRgbCalibration = rawRgbCalibration;
                CorrectedDepthCalibration = correctedDepthCalibration;
                CorrectedRgbCalibration = correctedRgbCalibration;
                PosePrior = posePrior;
                PoseResult = poseResult;
                FrameStaging = frameStaging;
                ActivePageFlags = activePageFlags;
                UnmatchedBlockFlags = unmatchedBlockFlags;
                _index = index;
            }

            internal GraphicsBuffer RawDepthCalibration { get; }
            internal GraphicsBuffer RawRgbCalibration { get; }
            internal GraphicsBuffer CorrectedDepthCalibration { get; }
            internal GraphicsBuffer CorrectedRgbCalibration { get; }
            internal GraphicsBuffer PosePrior { get; }
            internal GraphicsBuffer PoseResult { get; }
            internal GraphicsBuffer FrameStaging { get; }
            internal GraphicsBuffer ActivePageFlags { get; }
            internal GraphicsBuffer UnmatchedBlockFlags { get; private set; }
            internal GraphicsBuffer PosePartials { get; private set; }
            internal RenderTexture MetricDepth { get; private set; }
            internal RenderTexture DepthFlags { get; private set; }
            internal bool InFlight { get; private set; }
            internal long AgeFrames { get; private set; }
            internal uint Revision { get; private set; }

            internal void Begin(SigmaPredictionFrameLease prediction,
                SigmaPredictionFrameLease correctedPrediction,
                ConeLutLease coneLuts, SigmaGpuCompletionTicket ticket,
                uint revision, double submittedAt)
            {
                if (InFlight)
                    throw new InvalidOperationException(
                        "Sigma ingress slot is already in flight.");
                _prediction = prediction;
                _correctedPrediction = correctedPrediction;
                _coneLuts = coneLuts;
                _ticket = ticket;
                _submittedAt = submittedAt;
                Revision = revision;
                AgeFrames = 0L;
                InFlight = true;
            }

            internal SigmaGpuCompletionStatus Poll(out string error) =>
                _ticket.Poll(out error);
            internal void AdvanceAge() => AgeFrames++;
            internal double ElapsedMilliseconds(double completedAt) =>
                Math.Max(0.0, completedAt - _submittedAt) * 1000.0;

            internal void EnsureFrameResources(Vector2Int resolution,
                int blockCount)
            {
                if (_resolution != resolution || MetricDepth == null ||
                    DepthFlags == null)
                {
                    DestroyTexture(MetricDepth);
                    DestroyTexture(DepthFlags);
                    MetricDepth = CreateArrayTexture(
                        $"Sigma ingress {_index} metric depth", resolution,
                        GraphicsFormat.R32G32_SFloat);
                    DepthFlags = CreateArrayTexture(
                        $"Sigma ingress {_index} depth flags", resolution,
                        GraphicsFormat.R32_UInt);
                    _resolution = resolution;
                }
                if (_blockFlagCapacity >= blockCount)
                    return;
                UnmatchedBlockFlags?.Dispose();
                _blockFlagCapacity = NextPowerOfTwo(blockCount);
                UnmatchedBlockFlags = CreateBuffer(_blockFlagCapacity,
                    sizeof(uint),
                    $"Sigma ingress {_index} unmatched blocks");
            }

            internal void EnsurePosePartials(int partialCount)
            {
                if (PosePartials != null &&
                    _posePartialCapacity >= partialCount)
                    return;
                PosePartials?.Dispose();
                _posePartialCapacity = Math.Max(1, partialCount);
                PosePartials = CreateBuffer(
                    checked(_posePartialCapacity * 7), sizeof(uint) * 4,
                    $"Sigma ingress {_index} pose partial meets");
            }

            internal void Complete()
            {
                _prediction?.Dispose();
                _prediction = null;
                _correctedPrediction?.Dispose();
                _correctedPrediction = null;
                _coneLuts?.Dispose();
                _coneLuts = null;
                InFlight = false;
                AgeFrames = 0L;
                _submittedAt = 0.0;
            }

            public void Dispose()
            {
                _prediction?.Dispose();
                _prediction = null;
                _correctedPrediction?.Dispose();
                _correctedPrediction = null;
                _coneLuts?.Dispose();
                _coneLuts = null;
                RawDepthCalibration?.Dispose();
                RawRgbCalibration?.Dispose();
                CorrectedDepthCalibration?.Dispose();
                CorrectedRgbCalibration?.Dispose();
                PosePrior?.Dispose();
                PoseResult?.Dispose();
                PosePartials?.Dispose();
                FrameStaging?.Dispose();
                ActivePageFlags?.Dispose();
                UnmatchedBlockFlags?.Dispose();
                DestroyTexture(MetricDepth);
                DestroyTexture(DepthFlags);
                MetricDepth = null;
                DepthFlags = null;
                InFlight = false;
                _submittedAt = 0.0;
            }
        }
    }

}
