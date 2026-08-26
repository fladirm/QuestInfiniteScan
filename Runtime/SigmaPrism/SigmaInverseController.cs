using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum SigmaFrameCompletionDisposition : byte
    {
        Faulted = 0,
        NoChange = 1,
        Unresolved = 2,
        Published = 3,
    }

    /// <summary>
    /// Fixed host recorder for the direct whole-frame inverse. The CPU owns only
    /// immutable calibration uploads, complete-frame resource leases and fences.
    /// Every accepted coherent frame executes one fixed GPU dataflow and publishes
    /// one atomic Psi revision; execution partition never acquires identity.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SigmaCarrier))]
    [RequireComponent(typeof(SigmaRenderer))]
    [RequireComponent(typeof(SigmaRigBridge))]
    [DefaultExecutionOrder(0)]
    public sealed class SigmaInverseController : MonoBehaviour, IRoomScanModule
    {
        private const string DepthNormalizeResource =
            "SigmaPrism/DepthNormalize";
        private const string ConeLutResource = "SigmaPrism/ConeLut";
        private const string PoseGaugeResource = "SigmaPrism/SigmaPoseGauge";
        private const int CalibrationStride = 36;
        private const int RgbCalibrationStride = 8;
        private const int PosePriorValueCount = 15;

        [Header("Direct coherent-frame graph")]
        [SerializeField, Range(3, 8)] private int ingressSlotCount = 4;
        [SerializeField, Range(0.005f, 0.1f)]
        private float poseTranslationPriorMetres = 0.03f;
        [SerializeField, Range(0.25f, 5f)]
        private float poseRotationPriorDegrees = 2f;
        [SerializeField, Range(4, 32)] private int poseSampleStride = 16;
        [SerializeField] private bool profileNextCanonicalSubmission = true;

        private readonly Queue<SigmaPredictionFrameLease> _pendingIngress =
            new();
        private readonly Queue<SigmaUnresolvedEvidenceRecord> _unresolvedEvidence =
            new();
        private readonly List<SigmaNativeEvidenceReceipt> _evidenceReceipts =
            new();
        private readonly SigmaPackedQ48[] _calibrationUpload =
            new SigmaPackedQ48[CalibrationStride * 2];
        private readonly SigmaPackedQ48[] _rgbCalibrationUpload =
            new SigmaPackedQ48[RgbCalibrationStride * 2];
        private readonly SigmaPackedQ48[] _posePriorUpload =
            new SigmaPackedQ48[PosePriorValueCount];
        private readonly LatencyTracker _frameLatency = new();
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];
        private SigmaRuntimeTelemetrySnapshot _runtimeTelemetry =
            SigmaRuntimeTelemetrySnapshot.Awaiting;

        private RoomScanner _scanner;
        private SigmaCarrier _carrier;
        private SigmaRenderer _renderer;
        private SigmaRigBridge _rigBridge;
        private SigmaExactBackendGate _backendGate;
        private SigmaCarrierReadBatch _pool;
        private SigmaNativeFrameGraph _graph;
        private RigCalibration _calibration;
        private RigConeLutSet _coneLuts;

        private ComputeShader _normalizeShader;
        private ComputeShader _coneLutShader;
        private ComputeShader _poseGaugeShader;
        private int _normalizeKernel;
        private int _poseBuildKernel;
        private int _poseReduceKernel;
        private int _poseCalibrationKernel;

        private IngressSlot[] _ingressSlots;

        private SigmaGpuCompletionTicket _lastCompletion;
        private bool _hasLastCompletion;
        private bool _running;
        private bool _initialized;
        private bool _disposed;
        private bool _completionFaulted;
        private uint _nextRevision = 1u;
        private Pose _previousTrackingPose;
        private long _previousTrackingTimestampNs;
        private bool _hasPreviousTrackingPose;

        public string ModuleName => "Sigma direct whole-frame RGB-D inverse";
        public bool IsInitialized => _initialized && !_disposed;
        public long SubmittedFrames { get; private set; }
        public long CommittedFrames { get; private set; }
        public long DroppedFrames { get; private set; }
        public long FailedFrames { get; private set; }
        public long CommittedPageGenerations { get; private set; }
        public long AllocatedGaugePages { get; private set; }
        public long PeakCompletionAgeFrames { get; private set; }
        public int CompletionTickets => CountCompletionTickets() +
            SigmaGpuRetirement.PendingCount;
        public GraphicsBuffer PerformanceCounters => null;
        public SigmaRuntimeTelemetrySnapshot RuntimeTelemetry =>
            _runtimeTelemetry;

        public void OnModuleInitialize(RoomScanner scanner)
        {
            if (_initialized)
                return;
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _carrier = scanner.Carrier ?? GetComponent<SigmaCarrier>();
            _renderer = scanner.SigmaRenderer ?? GetComponent<SigmaRenderer>();
            _rigBridge = scanner.RigBridge ?? GetComponent<SigmaRigBridge>();
            _backendGate = scanner.ExactBackendGate ??
                throw new InvalidOperationException(
                    "Sigma inverse requires the exact backend gate.");
            SigmaGpuCompletion.RequireSupported();

            _normalizeShader = Resources.Load<ComputeShader>(
                DepthNormalizeResource);
            _coneLutShader = Resources.Load<ComputeShader>(ConeLutResource);
            _poseGaugeShader = Resources.Load<ComputeShader>(PoseGaugeResource);
            if (_carrier == null || _renderer == null ||
                _rigBridge == null || _normalizeShader == null ||
                _coneLutShader == null || _poseGaugeShader == null)
                throw new InvalidOperationException(
                    "Sigma direct inverse resources are incomplete.");

            FindKernels();
            _pool = _carrier.AcquireGpuManagedPool();
            _renderer.PredictionReady += OnPredictionReady;
            _initialized = true;
            Logger.Info("Sigma direct frame host ready; the first coherent " +
                        "prediction fixes the owned-frame resolution.");
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
            _frameLatency.Reset();
            if (profileNextCanonicalSubmission)
                SigmaGpuKernelTelemetry.RequestSingleSubmission();
        }

        public void OnScanStopped()
        {
            _running = false;
            ReleasePendingIngress();
            // Every already-submitted complete frame owns its source leases until
            // its fence closes; stopping cannot expose a partial revision.
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

            PollIngress();
            FrameTimingManager.CaptureFrameTimings();
            SigmaGpuKernelTelemetry.CaptureAndLogFrame();
            if (_completionFaulted)
                return;

            if (_running && _pendingIngress.Count != 0)
            {
                try
                {
                    SigmaPredictionFrameLease prediction =
                        _pendingIngress.Peek();
                    EnsureDirectGraph(prediction.Source);
                    if (TryGetFreeIngressSlot(out IngressSlot slot) &&
                        SubmitIngress(slot, prediction))
                        _pendingIngress.Dequeue();
                }
                catch (Exception exception)
                {
                    if (_pendingIngress.Count != 0)
                        _pendingIngress.Dequeue().Dispose();
                    LatchCompletionFault("Sigma direct-frame submission failed: " +
                        exception.Message);
                }
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
                _frameLatency.Snapshot,
                hasFrameTiming ? _frameTimings[0].cpuFrameTime : 0.0,
                hasFrameTiming ? _frameTimings[0].gpuFrameTime : 0.0,
                hasFrameTiming);
        }

        private void EnsureDirectGraph(StereoRigFrameLease source)
        {
            if (_graph != null)
            {
                if (_graph.Resolution != source.DepthResolution)
                    throw new InvalidOperationException(
                        "Depth resolution changed inside one direct-frame session.");
                return;
            }
            _graph = new SigmaNativeFrameGraph(source.DepthResolution,
                _backendGate, ingressSlotCount);
            CreatePersistentResources(_graph.FrameCapacity);
            Logger.Info($"Sigma direct graph ready: resolution=" +
                        $"{source.DepthResolution.x}x{source.DepthResolution.y}, " +
                        $"ownedFrames={_graph.FrameCapacity}, " +
                        $"hotDispatches={SigmaNativeFrameGraph.HotDispatchCount}, " +
                        $"memory={_graph.OwnedBytes / (1024L * 1024L)}MiB.");
        }

        private bool SubmitIngress(IngressSlot slot,
            SigmaPredictionFrameLease prediction)
        {
            StereoRigFrameLease source = prediction.Source;
            if (!source.IsValid)
                throw new InvalidOperationException(
                    "Inverse source lease is invalid.");
            Matrix4x4 worldToRoom = prediction.WorldToRoom;
            SigmaPredictionAcquireResult correctedAcquisition =
                _renderer.TryAcquirePoseGaugePrediction(source, worldToRoom,
                    out SigmaPredictionFrameLease correctedPrediction);
            if (correctedAcquisition == SigmaPredictionAcquireResult.Busy)
                return false;
            if (correctedAcquisition == SigmaPredictionAcquireResult.Faulted)
                throw new InvalidOperationException(
                    "Same-frame corrected prediction ring faulted.");

            ConeLutLease luts = null;
            CommandBuffer command = null;
            SigmaNativeFrameLease ownedFrame = null;
            uint revision = 0u;
            bool profiling = false;
            bool submitted = false;
            try
            {
                EnsureCalibration(source);
                slot.EnsureFrameResources(source.DepthResolution);
                revision = NextRevision();
                uint leftKey = IndependenceKey(source.DepthLeft,
                    source.CalibrationEpoch, worldToRoom);
                uint rightKey = IndependenceKey(source.DepthRight,
                    source.CalibrationEpoch, worldToRoom);
                uint rgbLeftKey = IndependenceKey(source.RgbLeft,
                    source.CalibrationEpoch, worldToRoom);
                uint rgbRightKey = IndependenceKey(source.RgbRight,
                    source.CalibrationEpoch, worldToRoom);
                UploadExactCalibration(slot, source, worldToRoom);
                UploadPosePrior(slot, source, worldToRoom);
                luts = _coneLuts.Acquire();
                command = CommandBufferPool.Get(
                    "Sigma-PRISM-16 Direct Coherent Frame");
                profiling = SigmaGpuKernelTelemetry.BeginProfiledSubmission(
                    revision);
                if (profiling)
                    SigmaGpuKernelTelemetry.RecordProfileBegin(command);
                RecordNormalize(command, slot, source, luts);
                RecordPoseGauge(command, slot, source, prediction, revision,
                    luts);
                RecordCorrectedCalibration(command, slot, source,
                    worldToRoom);
                _renderer.RecordPoseGaugePrediction(command, source,
                    slot.PoseResult, worldToRoom, correctedPrediction);

                var input = new SigmaNativeFrameInput(correctedPrediction,
                    slot.MetricDepth, slot.DepthFlags,
                    slot.CorrectedDepthCalibration,
                    slot.CorrectedRgbCalibration, slot.PoseResult, luts,
                    leftKey, rightKey, rgbLeftKey, rgbRightKey,
                    _pool);
                if (!_graph.TryAcquire(out ownedFrame))
                    return false;

                _graph.RecordNativeCloseCommit(command, ownedFrame, revision,
                    source.CalibrationEpoch, input);
                if (profiling)
                    SigmaGpuKernelTelemetry.RecordProfileEnd(command);
                SigmaGpuCompletionTicket ticket =
                    SigmaGpuCompletion.RecordAfterAllWork(command);
                Graphics.ExecuteCommandBuffer(command);
                submitted = true;
                if (profiling)
                    SigmaGpuKernelTelemetry.EndProfiledSubmission(revision,
                        true);
                slot.Begin(prediction, correctedPrediction, luts, ownedFrame,
                    ownedFrame.Resources.NativeFrame, _pool.PublicationRoot,
                    ticket, revision, Time.realtimeSinceStartupAsDouble);
                correctedPrediction = null;
                luts = null;
                ownedFrame = null;
                SubmittedFrames++;
                TrackLast(ticket);
                return true;
            }
            catch
            {
                throw;
            }
            finally
            {
                if (profiling && !submitted)
                    SigmaGpuKernelTelemetry.EndProfiledSubmission(revision,
                        false);
                ownedFrame?.Dispose();
                correctedPrediction?.Dispose();
                luts?.Dispose();
                if (command != null)
                    CommandBufferPool.Release(command);
            }
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
                SigmaGpuKernelTelemetry.CompleteProfiledSubmission(
                    slot.Revision);
                if (status == SigmaGpuCompletionStatus.Faulted)
                {
                    LatchCompletionFault($"Sigma ingress slot {index} failed " +
                        $"closed: {error}");
                    continue;
                }
                _frameLatency.Add(slot.ElapsedMilliseconds(
                    Time.realtimeSinceStartupAsDouble));
                SigmaFrameCompletionDisposition disposition =
                    slot.CompletionDisposition;
                if (disposition == SigmaFrameCompletionDisposition.Unresolved)
                {
                    SigmaUnresolvedEvidenceRecord evidence =
                        slot.DetachEvidence();
                    _unresolvedEvidence.Enqueue(evidence);
                    Logger.Info(evidence.FormatLogLine(slot.Revision));
                }
                _evidenceReceipts.Add(new SigmaNativeEvidenceReceipt(
                    slot.Revision, disposition));
                _runtimeTelemetry = SigmaRuntimeTelemetrySnapshot.From(
                    slot.Revision, slot.PublishedRoot, disposition,
                    slot.TerminalFrame, CaptureTimingTelemetry());
                Logger.Info(_runtimeTelemetry.FormatLogLine());
                if (slot.Complete() == SigmaFrameCompletionDisposition.Published)
                    CommittedFrames++;
            }
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
            command.SetComputeIntParams(_normalizeShader, "_Resolution",
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeVectorParam(_normalizeShader, "_NearFar",
                new Vector4(source.DepthNearFar.x, source.DepthNearFar.y,
                    0f, 0f));
            command.SetComputeTextureParam(_normalizeShader, _normalizeKernel,
                "_RawDepth", source.DepthLeft.Texture);
            command.SetComputeTextureParam(_normalizeShader, _normalizeKernel,
                "_DepthRayCenterLeft",
                luts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_normalizeShader, _normalizeKernel,
                "_DepthRayCenterRight",
                luts.DepthRight.CenterRaySolidAngle);
            command.SetComputeTextureParam(_normalizeShader, _normalizeKernel,
                "_MetricDepth", slot.MetricDepth);
            command.SetComputeTextureParam(_normalizeShader, _normalizeKernel,
                "_DepthFlags", slot.DepthFlags);
            command.DispatchComputeProfiled(_normalizeShader, _normalizeKernel,
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
            command.DispatchComputeProfiled(_poseGaugeShader, _poseBuildKernel,
                partialCount, 1, 1);

            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_PosePrior", slot.PosePrior);
            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_PosePartials", slot.PosePartials);
            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_PoseResult", slot.PoseResult);
            command.DispatchComputeProfiled(_poseGaugeShader, _poseReduceKernel,
                1, 1, 1);
        }

        private void RecordCorrectedCalibration(CommandBuffer command,
            IngressSlot slot, StereoRigFrameLease source,
            Matrix4x4 worldToRoom)
        {
            Matrix4x4 referenceWorld = SigmaRoomFrame.FromCamera(worldToRoom,
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
            command.DispatchComputeProfiled(_poseGaugeShader,
                _poseCalibrationKernel, 2, 1, 1);
        }

        private void UploadExactCalibration(IngressSlot slot,
            StereoRigFrameLease source, Matrix4x4 worldToRoom)
        {
            FillCalibration(0, source.DepthLeft, source.Health, worldToRoom);
            FillCalibration(1, source.DepthRight, source.Health, worldToRoom);
            slot.RawDepthCalibration.SetData(_calibrationUpload);
            FillRgbCalibration(0, source.RgbLeft.WorldFromCamera,
                source.Health, worldToRoom);
            FillRgbCalibration(1, source.RgbRight.WorldFromCamera,
                source.Health, worldToRoom);
            slot.RawRgbCalibration.SetData(_rgbCalibrationUpload);
        }

        private void UploadPosePrior(IngressSlot slot,
            StereoRigFrameLease source, Matrix4x4 worldToRoom)
        {
            for (int component = 0; component < 6; ++component)
                _posePriorUpload[component] = SigmaPackedQ48.FromRaw(0L);
            Vector2 envelope = BuildTrackingPriorEnvelope(source, worldToRoom);
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

        private Vector2 BuildTrackingPriorEnvelope(StereoRigFrameLease source,
            Matrix4x4 worldToRoom)
        {
            Pose current = SigmaRoomFrame.CameraPose(worldToRoom,
                source.DepthLeft.WorldFromCamera);
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
            _normalizeKernel = _normalizeShader.FindProfiledKernel(
                "NormalizeStereoDepth");
            _poseBuildKernel = _poseGaugeShader.FindProfiledKernel(
                "BuildPoseGaugePartials");
            _poseReduceKernel = _poseGaugeShader.FindProfiledKernel(
                "ReducePoseGauge");
            _poseCalibrationKernel = _poseGaugeShader.FindProfiledKernel(
                "BuildCorrectedCalibration");
        }

        private void CreatePersistentResources(int frameCapacity)
        {
            int packedStride = Marshal.SizeOf<SigmaPackedQ48>();
            ingressSlotCount = Mathf.Clamp(ingressSlotCount, 3,
                Math.Min(8, frameCapacity));
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
                        $"Sigma ingress {index} pose result"), index);
            }
        }

        private void FillCalibration(int eye, GpuImageView view,
            RigPairingHealth health, Matrix4x4 worldToRoom)
        {
            int offset = eye * CalibrationStride;
            SetQ(offset + 0, view.Intrinsics.FocalLength.x);
            SetQ(offset + 1, view.Intrinsics.FocalLength.y);
            SetQ(offset + 2, view.Intrinsics.PrincipalPoint.x);
            SetQ(offset + 3, view.Intrinsics.PrincipalPoint.y);
            Matrix4x4 world = SigmaRoomFrame.FromCamera(worldToRoom,
                view.WorldFromCamera);
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
            RigPairingHealth health, Matrix4x4 worldToRoom)
        {
            int offset = eye * RgbCalibrationStride;
            Matrix4x4 roomFromCamera = SigmaRoomFrame.FromCamera(worldToRoom,
                pose);
            SetRgbQ(offset + 0, roomFromCamera.m03);
            SetRgbQ(offset + 1, roomFromCamera.m13);
            SetRgbQ(offset + 2, roomFromCamera.m23);
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
            if (_ingressSlots == null)
            {
                result = null;
                return false;
            }
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

        private int CountCompletionTickets()
        {
            int count = 0;
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
        private static uint IndependenceKey(GpuImageView view, uint epoch,
            Matrix4x4 worldToRoom)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, epoch);
                Mix(ref hash, (uint)view.Eye + 1u);
                Pose roomPose = SigmaRoomFrame.CameraPose(worldToRoom,
                    view.WorldFromCamera);
                Vector3 p = roomPose.position;
                Quaternion q = roomPose.rotation;
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
            SigmaGpuKernelTelemetry.CancelSingleSubmission();
            if (_renderer != null)
                _renderer.PredictionReady -= OnPredictionReady;
            ReleasePendingIngress();
            _unresolvedEvidence.Clear();

            IngressSlot[] slots = _ingressSlots;
            _ingressSlots = null;
            RigConeLutSet coneLuts = _coneLuts;
            _coneLuts = null;
            SigmaNativeFrameGraph graph = _graph;
            _graph = null;
            _initialized = false;

            void ReleaseOwnedResources()
            {
                if (slots != null)
                    for (int index = 0; index < slots.Length; ++index)
                        slots[index]?.Dispose();
                coneLuts?.Retire();
                graph?.Dispose();
            }

            if (_completionFaulted)
            {
                SigmaGpuRetirement.Quarantine(ReleaseOwnedResources,
                    "Sigma direct-frame controller resources",
                    "A completion fault left GPU ownership unproven.");
            }
            else if (_hasLastCompletion)
            {
                SigmaGpuRetirement.Retire(_lastCompletion,
                    ReleaseOwnedResources,
                    "Sigma direct-frame controller teardown");
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

        private readonly struct SigmaNativeEvidenceReceipt
        {
            internal SigmaNativeEvidenceReceipt(uint revision,
                SigmaFrameCompletionDisposition disposition)
            {
                Revision = revision;
                Disposition = disposition;
            }

            internal uint Revision { get; }
            internal SigmaFrameCompletionDisposition Disposition { get; }
        }

        /// <summary>
        /// Small exact in-session journal record for an unresolved N3
        /// observation.  It owns copied Q48/query bytes, never capture-ring
        /// texture leases or physical identity.  N4 replaces this bounded
        /// handoff representation with durable/reclaimable certificates.
        /// </summary>
        private sealed class SigmaUnresolvedEvidenceRecord
        {
            internal SigmaUnresolvedEvidenceRecord(
                SigmaUnresolvedConstraintGpu constraint,
                SigmaFrameUInt4Gpu[] observationHeaders,
                SigmaFrameUInt2Gpu[] roomRays,
                SigmaFrameUInt2Gpu[] codeLeaves)
            {
                Constraint = constraint;
                ObservationHeaders = observationHeaders ?? throw new
                    ArgumentNullException(nameof(observationHeaders));
                RoomRays = roomRays ?? throw new
                    ArgumentNullException(nameof(roomRays));
                CodeLeaves = codeLeaves ?? throw new
                    ArgumentNullException(nameof(codeLeaves));
            }

            internal SigmaUnresolvedConstraintGpu Constraint { get; }
            internal SigmaFrameUInt4Gpu[] ObservationHeaders { get; }
            internal SigmaFrameUInt2Gpu[] RoomRays { get; }
            internal SigmaFrameUInt2Gpu[] CodeLeaves { get; }

            internal string FormatLogLine(uint revision) =>
                $"Sigma unresolved exact-record revision={revision} " +
                $"observation={Constraint.Observation.X:x8}/" +
                $"{Constraint.Observation.Y:x8}/" +
                $"{Constraint.Observation.Z:x8}/" +
                $"{Constraint.Observation.W:x8} relation=" +
                $"{Constraint.Relation.X}/{Constraint.Relation.Y}/" +
                $"{Constraint.Relation.Z}/{Constraint.Relation.W} " +
                $"evidence={Constraint.Evidence.X:x8}/" +
                $"{Constraint.Evidence.Y:x8}/" +
                $"{Constraint.Evidence.Z:x8}/" +
                $"{Constraint.Evidence.W:x8} admission=" +
                $"{Constraint.Provenance.X}/{Constraint.Provenance.Y} " +
                $"epoch={Constraint.Provenance.W}";
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
            private SigmaNativeFrameLease _ownedFrame;
            private SigmaGpuCompletionTicket _ticket;
            private GraphicsBuffer _nativeFrame;
            private GraphicsBuffer _publicationRoot;
            private GraphicsBuffer _unresolved;
            private GraphicsBuffer _freshObservationHeaders;
            private GraphicsBuffer _freshRoomRays;
            private GraphicsBuffer _freshCodeLeaves;
            private AsyncGPUReadbackRequest _frameDispositionReadback;
            private AsyncGPUReadbackRequest _publicationRootReadback;
            private AsyncGPUReadbackRequest _unresolvedReadback;
            private AsyncGPUReadbackRequest _freshObservationHeadersReadback;
            private AsyncGPUReadbackRequest _freshRoomRaysReadback;
            private AsyncGPUReadbackRequest _freshCodeLeavesReadback;
            private bool _completionReadbackPending;
            private SigmaFrameCompletionDisposition _completionDisposition;
            private SigmaNativeFrameGpu _terminalFrame;
            private uint _terminalPublishedRoot;
            private SigmaUnresolvedEvidenceRecord _terminalUnresolvedEvidence;
            private double _submittedAt;
            private readonly int _index;
            private Vector2Int _resolution;
            private int _posePartialCapacity;

            internal IngressSlot(GraphicsBuffer rawDepthCalibration,
                GraphicsBuffer rawRgbCalibration,
                GraphicsBuffer correctedDepthCalibration,
                GraphicsBuffer correctedRgbCalibration,
                GraphicsBuffer posePrior, GraphicsBuffer poseResult, int index)
            {
                RawDepthCalibration = rawDepthCalibration;
                RawRgbCalibration = rawRgbCalibration;
                CorrectedDepthCalibration = correctedDepthCalibration;
                CorrectedRgbCalibration = correctedRgbCalibration;
                PosePrior = posePrior;
                PoseResult = poseResult;
                _index = index;
            }

            internal GraphicsBuffer RawDepthCalibration { get; }
            internal GraphicsBuffer RawRgbCalibration { get; }
            internal GraphicsBuffer CorrectedDepthCalibration { get; }
            internal GraphicsBuffer CorrectedRgbCalibration { get; }
            internal GraphicsBuffer PosePrior { get; }
            internal GraphicsBuffer PoseResult { get; }
            internal GraphicsBuffer PosePartials { get; private set; }
            internal RenderTexture MetricDepth { get; private set; }
            internal RenderTexture DepthFlags { get; private set; }
            internal bool InFlight { get; private set; }
            internal long AgeFrames { get; private set; }
            internal uint Revision { get; private set; }
            internal SigmaFrameCompletionDisposition CompletionDisposition =>
                _completionDisposition;
            internal SigmaNativeFrameGpu TerminalFrame => _terminalFrame;
            internal uint PublishedRoot => _terminalPublishedRoot;

            internal void Begin(SigmaPredictionFrameLease prediction,
                SigmaPredictionFrameLease correctedPrediction,
                ConeLutLease coneLuts, SigmaNativeFrameLease ownedFrame,
                GraphicsBuffer nativeFrame, GraphicsBuffer publicationRoot,
                SigmaGpuCompletionTicket ticket,
                uint revision, double submittedAt)
            {
                if (InFlight)
                    throw new InvalidOperationException(
                        "Sigma ingress slot is already in flight.");
                _prediction = prediction;
                _correctedPrediction = correctedPrediction;
                _coneLuts = coneLuts;
                _ownedFrame = ownedFrame ?? throw new ArgumentNullException(
                    nameof(ownedFrame));
                _nativeFrame = nativeFrame ?? throw new ArgumentNullException(
                    nameof(nativeFrame));
                _publicationRoot = publicationRoot ??
                    throw new ArgumentNullException(nameof(publicationRoot));
                _unresolved = _ownedFrame.Resources.Unresolved;
                _freshObservationHeaders =
                    _ownedFrame.Resources.FreshObservationHeaders;
                _freshRoomRays = _ownedFrame.Resources.FreshRoomRays;
                _freshCodeLeaves = _ownedFrame.Resources.FreshCodeLeaves;
                _ticket = ticket;
                _submittedAt = submittedAt;
                Revision = revision;
                _completionReadbackPending = false;
                _completionDisposition =
                    SigmaFrameCompletionDisposition.Faulted;
                _terminalFrame = default;
                _terminalPublishedRoot = 0u;
                _terminalUnresolvedEvidence = null;
                AgeFrames = 0L;
                InFlight = true;
            }

            internal SigmaGpuCompletionStatus Poll(out string error)
            {
                if (!_completionReadbackPending)
                {
                    SigmaGpuCompletionStatus fence = _ticket.Poll(out error);
                    if (fence != SigmaGpuCompletionStatus.Complete)
                        return fence;
                    if (!SystemInfo.supportsAsyncGPUReadback)
                    {
                        error = "Published evidence ownership requires async " +
                            "GPU readback support.";
                        return SigmaGpuCompletionStatus.Faulted;
                    }
                    _frameDispositionReadback = AsyncGPUReadback.Request(
                        _nativeFrame, SigmaGeneratedFrame.NativeFrameStride, 0);
                    _publicationRootReadback = AsyncGPUReadback.Request(
                        _publicationRoot, sizeof(uint), 0);
                    _unresolvedReadback = AsyncGPUReadback.Request(_unresolved,
                        SigmaGeneratedFrame.UnresolvedConstraintStride, 0);
                    _freshObservationHeadersReadback =
                        AsyncGPUReadback.Request(_freshObservationHeaders,
                            sizeof(uint) * 4 * 2, 0);
                    _freshRoomRaysReadback = AsyncGPUReadback.Request(
                        _freshRoomRays, sizeof(uint) * 2 * 6, 0);
                    _freshCodeLeavesReadback = AsyncGPUReadback.Request(
                        _freshCodeLeaves, sizeof(uint) * 2 * 16, 0);
                    // The fence proves every raw capture texture has been
                    // consumed.  Release its ring ownership now; completion of
                    // the tiny exact-record readbacks must never back-pressure
                    // sensor ingress.
                    ReleaseTransientInputs();
                    _completionReadbackPending = true;
                    error = null;
                    return SigmaGpuCompletionStatus.Pending;
                }

                if (!_frameDispositionReadback.done ||
                    !_publicationRootReadback.done ||
                    !_unresolvedReadback.done ||
                    !_freshObservationHeadersReadback.done ||
                    !_freshRoomRaysReadback.done ||
                    !_freshCodeLeavesReadback.done)
                {
                    error = null;
                    return SigmaGpuCompletionStatus.Pending;
                }
                if (_frameDispositionReadback.hasError ||
                    _publicationRootReadback.hasError ||
                    _unresolvedReadback.hasError ||
                    _freshObservationHeadersReadback.hasError ||
                    _freshRoomRaysReadback.hasError ||
                    _freshCodeLeavesReadback.hasError)
                {
                    error = "Published evidence ownership readback failed.";
                    return SigmaGpuCompletionStatus.Faulted;
                }

                var frames = _frameDispositionReadback
                    .GetData<SigmaNativeFrameGpu>();
                var roots = _publicationRootReadback.GetData<uint>();
                if (frames.Length != 1 || roots.Length != 1)
                {
                    error = "Published evidence ownership readback returned " +
                        "an invalid record count.";
                    return SigmaGpuCompletionStatus.Faulted;
                }
                _terminalFrame = frames[0];
                _terminalPublishedRoot = roots[0];
                _completionDisposition = ClassifyFrameCompletion(
                    _terminalFrame, _terminalPublishedRoot, Revision,
                    out error);
                if (_completionDisposition ==
                    SigmaFrameCompletionDisposition.Unresolved)
                {
                    var unresolved = _unresolvedReadback
                        .GetData<SigmaUnresolvedConstraintGpu>();
                    var headers = _freshObservationHeadersReadback
                        .GetData<SigmaFrameUInt4Gpu>();
                    var rays = _freshRoomRaysReadback
                        .GetData<SigmaFrameUInt2Gpu>();
                    var leaves = _freshCodeLeavesReadback
                        .GetData<SigmaFrameUInt2Gpu>();
                    if (unresolved.Length != 1 || headers.Length != 2 ||
                        rays.Length != 6 || leaves.Length != 16)
                    {
                        error = "Unresolved evidence readback returned an " +
                            "invalid exact-record shape.";
                        return SigmaGpuCompletionStatus.Faulted;
                    }
                    var headerCopy = new SigmaFrameUInt4Gpu[headers.Length];
                    var rayCopy = new SigmaFrameUInt2Gpu[rays.Length];
                    var leafCopy = new SigmaFrameUInt2Gpu[leaves.Length];
                    headers.CopyTo(headerCopy);
                    rays.CopyTo(rayCopy);
                    leaves.CopyTo(leafCopy);
                    _terminalUnresolvedEvidence =
                        new SigmaUnresolvedEvidenceRecord(unresolved[0],
                            headerCopy, rayCopy, leafCopy);
                }
                return _completionDisposition ==
                    SigmaFrameCompletionDisposition.Faulted
                    ? SigmaGpuCompletionStatus.Faulted
                    : SigmaGpuCompletionStatus.Complete;
            }
            internal void AdvanceAge() => AgeFrames++;
            internal double ElapsedMilliseconds(double completedAt) =>
                Math.Max(0.0, completedAt - _submittedAt) * 1000.0;

            internal void EnsureFrameResources(Vector2Int resolution)
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

            internal SigmaUnresolvedEvidenceRecord DetachEvidence()
            {
                if (_completionDisposition !=
                    SigmaFrameCompletionDisposition.Unresolved ||
                    _terminalUnresolvedEvidence == null)
                    throw new InvalidOperationException(
                        "Only unresolved native evidence can be detached.");
                SigmaUnresolvedEvidenceRecord result =
                    _terminalUnresolvedEvidence;
                _terminalUnresolvedEvidence = null;
                return result;
            }

            internal SigmaFrameCompletionDisposition Complete()
            {
                ReleaseTransientInputs();
                SigmaFrameCompletionDisposition completed =
                    _completionDisposition;
                _ownedFrame?.Dispose();
                _ownedFrame = null;
                _nativeFrame = null;
                _publicationRoot = null;
                _unresolved = null;
                _freshObservationHeaders = null;
                _freshRoomRays = null;
                _freshCodeLeaves = null;
                _completionReadbackPending = false;
                _completionDisposition =
                    SigmaFrameCompletionDisposition.Faulted;
                _terminalFrame = default;
                _terminalPublishedRoot = 0u;
                _terminalUnresolvedEvidence = null;
                InFlight = false;
                AgeFrames = 0L;
                _submittedAt = 0.0;
                return completed;
            }

            public void Dispose()
            {
                ReleaseTransientInputs();
                _ownedFrame?.Dispose();
                _ownedFrame = null;
                _nativeFrame = null;
                _publicationRoot = null;
                _unresolved = null;
                _freshObservationHeaders = null;
                _freshRoomRays = null;
                _freshCodeLeaves = null;
                _terminalUnresolvedEvidence = null;
                RawDepthCalibration?.Dispose();
                RawRgbCalibration?.Dispose();
                CorrectedDepthCalibration?.Dispose();
                CorrectedRgbCalibration?.Dispose();
                PosePrior?.Dispose();
                PoseResult?.Dispose();
                PosePartials?.Dispose();
                DestroyTexture(MetricDepth);
                DestroyTexture(DepthFlags);
                MetricDepth = null;
                DepthFlags = null;
                InFlight = false;
                _submittedAt = 0.0;
            }

            private void ReleaseTransientInputs()
            {
                _prediction?.Dispose();
                _prediction = null;
                _correctedPrediction?.Dispose();
                _correctedPrediction = null;
                _coneLuts?.Dispose();
                _coneLuts = null;
            }
        }

        internal static SigmaFrameCompletionDisposition ClassifyFrameCompletion(
            SigmaNativeFrameGpu frame, uint publishedRoot, uint revision,
            out string error)
        {
            if (revision == 0u || frame.Identity.X != revision)
            {
                error = $"Sigma frame disposition revision mismatch: expected " +
                    $"{revision}, received {frame.Identity.X}.";
                return SigmaFrameCompletionDisposition.Faulted;
            }
            SigmaNativeFrameDisposition state =
                (SigmaNativeFrameDisposition)frame.Disposition.X;
            if (state == SigmaNativeFrameDisposition.Faulted ||
                frame.Disposition.W != 0u)
            {
                error = $"Sigma frame revision {revision} reported publication " +
                    $"fault 0x{frame.Disposition.W:x8}.";
                return SigmaFrameCompletionDisposition.Faulted;
            }
            if (state == SigmaNativeFrameDisposition.Published)
            {
                if (publishedRoot != revision ||
                    frame.Publication.Y != revision)
                {
                    error = $"Sigma frame revision {revision} reported publication " +
                        $"evidence before publication root {publishedRoot}.";
                    return SigmaFrameCompletionDisposition.Faulted;
                }
                error = null;
                return SigmaFrameCompletionDisposition.Published;
            }
            if (state == SigmaNativeFrameDisposition.NoChange)
            {
                error = null;
                return SigmaFrameCompletionDisposition.NoChange;
            }
            if (state == SigmaNativeFrameDisposition.Unresolved)
            {
                error = null;
                return SigmaFrameCompletionDisposition.Unresolved;
            }

            error = $"Sigma frame revision {revision} ended at illegal post-fence " +
                $"state {state}.";
            return SigmaFrameCompletionDisposition.Faulted;
        }
    }

}
