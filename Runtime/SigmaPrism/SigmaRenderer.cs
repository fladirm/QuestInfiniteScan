using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Disposable world-space readout of the one canonical carrier. Exact packed
    /// Q16.48 projective readout is cached on GPU; raster hardware selects first
    /// hits for prediction and presents the temporary S4-08 preview. No readout
    /// cache owns identity or participates in canonical mutation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SigmaCarrier))]
    [RequireComponent(typeof(SigmaRigBridge))]
    [DefaultExecutionOrder(-10)]
    public sealed class SigmaRenderer : MonoBehaviour, IRoomScanModule
    {
        public const int ReadoutExtent = SigmaCarrier.PageSize;
        public const int ReadoutSamplesPerPage = ReadoutExtent * ReadoutExtent;
        public const int VerticesPerCarrierPage =
            SigmaCarrier.PageSize * SigmaCarrier.PageSize * 6;

        private const string PredictionResource = "SigmaPrism/SigmaPredict";
        private const string PreviewResource =
            "SigmaPrism/SigmaDirectCarrierPreview";
        private const int CorrectedTargetRingSlots = 4;

        [SerializeField, Range(3, 12)] private int targetRingSlots = 4;
        private const float ReadoutRadius = 6f;
        [SerializeField, Range(0f, 1f)] private float readoutOpacity = 0.35f;

        private static readonly int PageMetadataId = Shader.PropertyToID(
            "_PageMetadata");
        private static readonly int ReadoutSamplesId = Shader.PropertyToID(
            "_ReadoutSamples");
        private static readonly int ReadoutEyeId = Shader.PropertyToID("_ReadoutEye");
        private static readonly int ReadoutOpacityId = Shader.PropertyToID("_ReadoutOpacity");
        private static readonly int CurrentPageSlotsId = Shader.PropertyToID(
            "_CurrentPageSlots");
        private static readonly int ClipFromWorldId = Shader.PropertyToID(
            "_ClipFromWorld");
        private static readonly int OpticalFromWorldId = Shader.PropertyToID(
            "_OpticalFromWorld");
        private static readonly int SegmentIndexId = Shader.PropertyToID(
            "_SegmentIndex");
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
        private static readonly int RoomToWorldId = Shader.PropertyToID(
            "_RoomToWorld");
        private static readonly int ContactFootprintPixelsId = Shader.PropertyToID(
            "_ContactFootprintPixels");

        private readonly List<SigmaCarrierReadBatch> _readBatches = new();
        private readonly List<SegmentReadoutCache> _segmentCaches = new();
        private readonly RenderTargetIdentifier[] _mrt =
            new RenderTargetIdentifier[4];

        private RoomScanner _scanner;
        private SigmaCarrier _carrier;
        private SigmaRigBridge _rigBridge;
        private SigmaExactBackendGate _backendGate;
        private Material _predictionMaterial;
        private Material _previewMaterial;
        private MaterialPropertyBlock _properties;
        private SigmaPredictionTargetRing _targets;
        private SigmaPredictionTargetRing _correctedTargets;
        private GraphicsBuffer _identityPoseResult;
        private SigmaPredictionFrameLease _latest;
        private RigCalibration _calibration;
        private SigmaNativeVulkanReadout.ReadoutJob _readoutJob;
        private byte[] _pendingReadoutConstants;
        private uint _requestedReadoutRevision;
        private bool _readoutRequested;
        private uint _nextReadoutRevision = 1u;
        private string _readoutFault;
        private bool _running;
        private bool _initialized;

        public string ModuleName => "Sigma forward readout";
        public bool IsInitialized => _initialized;
        public long RenderedFrames { get; private set; }
        public long BackpressureFrames { get; private set; }
        // One mailbox turn must run after the current close/persistence turn.
        // Otherwise Update can admit another scan before this LateUpdate ever
        // sees the carrier idle, permanently starving the first FRONT build.
        internal bool HasPendingReadout => _readoutRequested &&
            _readoutFault == null;
        public float ReadoutOpacity
        {
            get => readoutOpacity;
            set => readoutOpacity = Mathf.Clamp01(value);
        }

        internal bool TryGetReadoutDiagnostics(int segmentIndex,
            out GraphicsBuffer drawArguments,
            out GraphicsBuffer currentPageSlots,
            out GraphicsBuffer vertices,
            out int pageCapacity)
        {
            drawArguments = null;
            currentPageSlots = null;
            vertices = null;
            pageCapacity = 0;
            if (!_initialized)
                return false;
            EnsureSegmentCaches();
            int count = Math.Min(_readBatches.Count, _segmentCaches.Count);
            for (int index = 0; index < count; ++index)
            {
                if (_readBatches[index].SegmentIndex != segmentIndex)
                    continue;
                SegmentReadoutCache cache = _segmentCaches[index];
                ReadoutGeneration front = cache.Front;
                drawArguments = front.DrawArguments;
                currentPageSlots = front.CurrentPageSlots;
                vertices = front.Samples;
                pageCapacity = cache.Capacity;
                return true;
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
            _rigBridge = scanner.RigBridge ?? GetComponent<SigmaRigBridge>();
            _backendGate = scanner.ExactBackendGate ??
                throw new InvalidOperationException(
                    "Sigma renderer requires the exact backend gate.");
            Shader prediction = Resources.Load<Shader>(PredictionResource);
            Shader preview = Resources.Load<Shader>(PreviewResource);
            if (_carrier == null || _rigBridge == null ||
                prediction == null || preview == null)
                throw new InvalidOperationException(
                    "Sigma forward-readout resources are incomplete.");

            _predictionMaterial = new Material(prediction)
            {
                name = "[Sigma-PRISM-16] Prediction Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            _previewMaterial = new Material(preview)
            {
                name = "[Sigma-PRISM-16] Pure Eye Readout",
                hideFlags = HideFlags.HideAndDontSave
            };
            _properties = new MaterialPropertyBlock();
            _targets = new SigmaPredictionTargetRing(targetRingSlots);
            _correctedTargets = new SigmaPredictionTargetRing(
                CorrectedTargetRingSlots);
            _targets.Preallocate(
                SigmaNativeQuestAperture.PhysicalDepthResolution);
            _correctedTargets.Preallocate(
                SigmaNativeQuestAperture.PhysicalDepthResolution);
            _identityPoseResult = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 4, sizeof(uint) * 4)
            {
                name = "Sigma identity pose-gauge readout"
            };
            _identityPoseResult.SetData(new uint[16]);
            _carrier.CollectReadableSegments(_readBatches);
            EnsureSegmentCaches();
            _initialized = true;
        }

        public void OnScanStarted()
        {
            _running = true;
        }

        public void OnScanStopped()
        {
            _running = false;
            _latest?.Dispose();
            _latest = null;
        }

        internal void ResetDisposableReadoutAfterClear()
        {
            if (!_initialized)
                return;
            if (_readoutJob != null)
                throw new InvalidOperationException("Clear must retire the N6 readout writer first.");
            _readoutRequested = false;
            _pendingReadoutConstants = null;
            _latest?.Dispose();
            _latest = null;
            for (int index = 0; index < _segmentCaches.Count; ++index)
            {
                SegmentReadoutCache prior = _segmentCaches[index];
                _segmentCaches[index] = new SegmentReadoutCache(
                    _readBatches[index]);
                RetireSegmentCache(prior);
            }
        }

        private void LateUpdate()
        {
            if (!_initialized)
                return;
            TryPublishReadoutCaches();
            TrySubmitReadout();
            RenderDirectCarrierPreview();
        }

        internal SigmaPredictionAcquireResult TryAcquireScannerPrediction(
            StereoRigFrameLease source, Matrix4x4 worldToRoom,
            out SigmaPredictionFrameLease prediction)
        {
            prediction = null;
            if (!_running || !_initialized || source == null ||
                !source.IsValid || _readoutFault != null || _targets == null)
                return SigmaPredictionAcquireResult.Faulted;
            if (SigmaNativeVulkanExecutor.HasJobInFlight ||
                SigmaNativeVulkanReadout.HasJobInFlight)
            {
                BackpressureFrames++;
                return SigmaPredictionAcquireResult.Busy;
            }
            if (_calibration == null || !_calibration.IsCompatible(source))
            {
                if (!RigCalibration.TryCreate(source, out _calibration))
                    return SigmaPredictionAcquireResult.Faulted;
            }
            SigmaPredictionAcquireResult result = _targets.TryBegin(source,
                SigmaPoseGaugeState.Identity(source.CalibrationEpoch),
                worldToRoom, out prediction);
            if (result == SigmaPredictionAcquireResult.Busy)
                BackpressureFrames++;
            return result;
        }

        internal SigmaPredictionAcquireResult TryAcquirePoseGaugePrediction(
            StereoRigFrameLease source, Matrix4x4 worldToRoom,
            out SigmaPredictionFrameLease prediction)
        {
            prediction = null;
            if (!_initialized || source == null || !source.IsValid ||
                _correctedTargets == null)
                return SigmaPredictionAcquireResult.Faulted;
            if (SigmaNativeVulkanExecutor.HasJobInFlight ||
                SigmaNativeVulkanReadout.HasJobInFlight)
            {
                BackpressureFrames++;
                return SigmaPredictionAcquireResult.Busy;
            }
            SigmaPredictionAcquireResult result = _correctedTargets.TryBegin(
                source, SigmaPoseGaugeState.Identity(source.CalibrationEpoch),
                worldToRoom, out prediction);
            if (result == SigmaPredictionAcquireResult.Busy)
                BackpressureFrames++;
            return result;
        }

        internal void RecordPoseGaugePrediction(CommandBuffer command,
            StereoRigFrameLease source, GraphicsBuffer poseResult,
            Matrix4x4 worldToRoom, SigmaPredictionFrameLease prediction)
        {
            if (!_initialized || command == null || source == null ||
                !source.IsValid || poseResult == null || prediction == null ||
                prediction.IsDisposed)
                throw new InvalidOperationException(
                    "Corrected Sigma prediction inputs are incomplete.");
            _carrier.CollectReadableSegments(_readBatches);
            EnsureSegmentCaches();
            Matrix4x4 referenceWorld = SigmaRoomFrame.FromCamera(
                worldToRoom,
                source.DepthLeft.WorldFromCamera);
            for (int eye = 0; eye < 2; ++eye)
            {
                GpuImageView view = eye == 0 ? source.DepthLeft :
                    source.DepthRight;
                Matrix4x4 rawWorld = SigmaRoomFrame.FromCamera(worldToRoom,
                    view.WorldFromCamera);
                Matrix4x4 opticalFromWorld = rawWorld.inverse;
                SetMrt(prediction);
                command.SetRenderTarget(_mrt,
                    new RenderTargetIdentifier(prediction.HardwareDepth), 0,
                    CubemapFace.Unknown, eye);
                command.ClearRenderTarget(true, true, Color.clear, 1f);
                DrawSegments(command, eye,
                    BuildClipFromWorld(view,
                        opticalFromWorld), opticalFromWorld, poseResult,
                    referenceWorld.inverse, referenceWorld);
            }
        }

        internal bool RecordScannerPrediction(CommandBuffer command,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            out uint readoutRevision)
        {
            readoutRevision = 0u;
            if (!_initialized || command == null || source == null ||
                !source.IsValid || prediction == null || prediction.IsDisposed)
                throw new InvalidOperationException(
                    "Scanner readout/prediction inputs are incomplete.");
            _carrier.CollectReadableSegments(_readBatches);
            EnsureSegmentCaches();
            if (_readoutFault != null || _readBatches.Count != 2)
            {
                BackpressureFrames++;
                return false;
            }
            Matrix4x4 worldToRoom = prediction.WorldToRoom;
            readoutRevision = NextReadoutRevision();
            _pendingReadoutConstants = BuildReadoutConstants(source, worldToRoom);
            SigmaPoseGaugeState gauge = prediction.PoseGauge;
            for (int eye = 0; eye < 2; ++eye)
            {
                GpuImageView view = eye == 0 ? source.DepthLeft :
                    source.DepthRight;
                Pose referencePose = SigmaRoomFrame.CameraPose(worldToRoom,
                    source.DepthLeft.WorldFromCamera);
                Pose viewPose = SigmaRoomFrame.CameraPose(worldToRoom,
                    view.WorldFromCamera);
                Pose correctedPose = gauge.Apply(referencePose, viewPose);
                Matrix4x4 opticalFromWorld = Matrix4x4.TRS(
                    correctedPose.position, correctedPose.rotation,
                    Vector3.one).inverse;
                SetMrt(prediction);
                command.SetRenderTarget(_mrt,
                    new RenderTargetIdentifier(prediction.HardwareDepth), 0,
                    CubemapFace.Unknown, eye);
                command.ClearRenderTarget(true, true, Color.clear, 1f);
                Matrix4x4 referenceWorld = Matrix4x4.TRS(
                    referencePose.position, referencePose.rotation,
                    Vector3.one);
                DrawSegments(command, eye,
                    BuildClipFromWorld(view, opticalFromWorld),
                    opticalFromWorld, _identityPoseResult,
                    referenceWorld.inverse, referenceWorld);
            }
            return true;
        }

        internal void CommitScannerTransaction(
            SigmaPredictionFrameLease prediction,
            SigmaPredictionFrameLease correctedPrediction,
            SigmaGpuCompletionTicket completion, uint readoutRevision)
        {
            if (prediction == null || prediction.IsDisposed ||
                correctedPrediction == null || correctedPrediction.IsDisposed ||
                !completion.IsValid || readoutRevision == 0u)
                throw new InvalidOperationException(
                    "Scanner transaction publication is incomplete.");
            prediction.CommitGpuWrite(completion);
            correctedPrediction.CommitGpuWrite(completion);
            _requestedReadoutRevision = readoutRevision;
            _readoutRequested = true;
            SigmaPredictionFrameLease previous = _latest;
            _latest = prediction.Retain();
            previous?.Dispose();
            RenderedFrames++;
        }

        private void TrySubmitReadout()
        {
            if (!_readoutRequested || _pendingReadoutConstants == null ||
                _readoutFault != null || _readoutJob != null ||
                SigmaNativeVulkanExecutor.HasJobInFlight ||
                SigmaNativeVulkanColdEncode.HasJobInFlight ||
                SigmaNativeVulkanColdUpload.HasJobInFlight ||
                _carrier.Persistence.IsBusy ||
                _carrier.Pager.Activity != SigmaPagerActivity.Idle)
                return;
            _carrier.CollectReadableSegments(_readBatches);
            EnsureSegmentCaches();
            if (_readBatches.Count != 2)
                return;
            ReadoutGeneration back0 = _segmentCaches[0].Back;
            ReadoutGeneration back1 = _segmentCaches[1].Back;
            var buffers = new[]
            {
                _backendGate.Buffer,
                _readBatches[0].State, _readBatches[1].State,
                _readBatches[0].Metadata, _readBatches[1].Metadata,
                _readBatches[0].PublicationRoot,
                back0.Samples, back1.Samples,
                back0.CurrentPageSlots, back1.CurrentPageSlots,
                back0.RenderPageMetadata, back1.RenderPageMetadata,
                back0.DrawArguments, back1.DrawArguments,
            };
            var resources = new IntPtr[buffers.Length];
            for (int index = 0; index < buffers.Length; ++index)
                resources[index] = buffers[index].GetNativeBufferPtr();
            byte[] constants = (byte[])_pendingReadoutConstants.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(constants.AsSpan(160),
                checked((uint)_readBatches[0].PageCapacity));
            BinaryPrimitives.WriteUInt32LittleEndian(constants.AsSpan(164),
                checked((uint)_readBatches[1].PageCapacity));
            BinaryPrimitives.WriteUInt32LittleEndian(constants.AsSpan(168),
                _requestedReadoutRevision);
            CommandBuffer command = CommandBufferPool.Get("Sigma N6 BACK readout");
            try
            {
                _readoutJob = SigmaNativeVulkanReadout.Create(
                    _requestedReadoutRevision, resources, constants,
                    _readBatches[0].PageCapacity, _readBatches[1].PageCapacity);
                _readoutJob.Record(command);
                // Ownership is installed before submission, including the
                // uncertain-submission failure path. No FRONT buffer is a writer.
                for (int index = 0; index < _segmentCaches.Count; ++index)
                    _segmentCaches[index].MarkBackSubmitted(_readoutJob,
                        _requestedReadoutRevision);
                Graphics.ExecuteCommandBuffer(command);
                _readoutRequested = false;
                Logger.Info($"Sigma N6 BACK queued: generation={_requestedReadoutRevision} " +
                    $"pages={_readBatches[0].PageCapacity}+{_readBatches[1].PageCapacity}.");
            }
            catch (Exception exception)
            {
                _readoutFault = exception.Message;
                Logger.Error("Sigma N6 BACK submission failed closed: " + _readoutFault);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private static byte[] BuildReadoutConstants(StereoRigFrameLease source,
            Matrix4x4 worldToRoom)
        {
            var constants = new byte[SigmaNativeVulkanReadout.ConstantBytes];
            Vector3 head = Vector3.zero;
            for (int eye = 0; eye < 2; ++eye)
            {
                GpuImageView view = eye == 0 ? source.DepthLeft : source.DepthRight;
                Pose pose = SigmaRoomFrame.CameraPose(worldToRoom, view.WorldFromCamera);
                Vector3 forward = pose.rotation * Vector3.forward;
                head += pose.position * 0.5f;
                for (int axis = 0; axis < 3; ++axis)
                {
                    WriteReadoutQ(constants, axis * 16 + eye * 8, pose.position[axis]);
                    WriteReadoutQ(constants, (3 + axis) * 16 + eye * 8, forward[axis]);
                }
                WriteReadoutQ(constants, 6 * 16 + eye * 8, view.DepthNearFar.x);
                WriteReadoutQ(constants, 7 * 16 + eye * 8,
                    RigDepthContract.FiniteRasterFar(view.DepthNearFar));
            }
            WriteReadoutQ(constants, 128, head.x);
            WriteReadoutQ(constants, 136, head.y);
            WriteReadoutQ(constants, 144, head.z);
            WriteReadoutQ(constants, 152, ReadoutRadius);
            return constants;
        }

        private static void WriteReadoutQ(byte[] bytes, int offset, float value) =>
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset),
                SigmaNumericDomain.Quantize(value));

        private void DrawSegments(CommandBuffer command,
            int eye,
            Matrix4x4 clipFromWorld, Matrix4x4 opticalFromWorld,
            GraphicsBuffer poseResult, Matrix4x4 referenceFromWorld,
            Matrix4x4 worldFromReference)
        {
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                SegmentReadoutCache cache = _segmentCaches[index];
                ReadoutGeneration generation = cache.Front;
                _properties.Clear();
                _properties.SetMatrix(ClipFromWorldId, clipFromWorld);
                _properties.SetMatrix(OpticalFromWorldId, opticalFromWorld);
                _properties.SetMatrix(PoseReferenceFromWorldId,
                    referenceFromWorld);
                _properties.SetMatrix(PoseWorldFromReferenceId,
                    worldFromReference);
                _properties.SetInt(SegmentIndexId, batch.SegmentIndex);
                _properties.SetInt(ReadoutEyeId, eye);
                _properties.SetFloat(ContactFootprintPixelsId, 1.35f);
                _properties.SetBuffer(PoseResultId, poseResult);
                _properties.SetBuffer(ReadoutSamplesId, generation.Samples);
                _properties.SetBuffer(CurrentPageSlotsId,
                    generation.CurrentPageSlots);
                _properties.SetBuffer(PageMetadataId,
                    generation.RenderPageMetadata);
                command.DrawProceduralIndirect(Matrix4x4.identity,
                    _predictionMaterial, 0, MeshTopology.Triangles,
                    generation.DrawArguments, 0, _properties);
            }
        }

        private bool TryPublishReadoutCaches()
        {
            bool hasSubmittedBack = false;
            for (int index = 0; index < _segmentCaches.Count; ++index)
            {
                SegmentReadoutCache cache = _segmentCaches[index];
                if (!cache.BackSubmitted)
                    continue;
                hasSubmittedBack = true;
                SigmaGpuCompletionStatus status = cache.PollBack(
                    out string error);
                if (status == SigmaGpuCompletionStatus.Pending)
                    return false;
                if (status == SigmaGpuCompletionStatus.Faulted)
                {
                    _readoutFault = string.IsNullOrWhiteSpace(error)
                        ? "Readout BACK completion is unprovable."
                        : error;
                    Logger.Error("Sigma FRONT/BACK readout failed closed: " +
                        _readoutFault);
                    return false;
                }
            }
            if (!hasSubmittedBack)
                return true;
            for (int index = 0; index < _segmentCaches.Count; ++index)
                if (_segmentCaches[index].BackSubmitted)
                    _segmentCaches[index].PublishBack();
            _readoutJob?.Dispose();
            _readoutJob = null;
            Logger.Info($"Sigma N6 FRONT published: generation={_segmentCaches[0].FrontRevision} " +
                $"banks={_segmentCaches.Count} immutable=1.");
            return true;
        }

        private uint NextReadoutRevision()
        {
            uint revision = _nextReadoutRevision++;
            if (revision == 0u || _nextReadoutRevision == 0u)
                throw new OverflowException(
                    "Disposable readout generation exhausted.");
            return revision;
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
                    RetireSegmentCache(_segmentCaches[index]);
                    _segmentCaches[index] = new SegmentReadoutCache(batch);
                }
                else
                    _segmentCaches.Add(new SegmentReadoutCache(batch));
            }
            for (int index = _segmentCaches.Count - 1;
                index >= _readBatches.Count; --index)
            {
                RetireSegmentCache(_segmentCaches[index]);
                _segmentCaches.RemoveAt(index);
            }
        }

        private static void RetireSegmentCache(SegmentReadoutCache cache)
        {
            if (cache == null)
                return;
            if (cache.BackSubmitted)
            {
                SigmaGpuRetirement.Quarantine(cache.Dispose,
                    "Sigma N6 readout writer", "BACK completion is still pending.");
                return;
            }
            try
            {
                SigmaGpuCompletionTicket fence =
                    SigmaGpuCompletion.InsertAfterGraphicsWork();
                SigmaGpuRetirement.Retire(fence, cache.Dispose,
                    "Sigma replaced FRONT/BACK readout cache");
            }
            catch (Exception exception)
            {
                SigmaGpuRetirement.Quarantine(cache.Dispose,
                    "Sigma replaced FRONT/BACK readout cache",
                    exception.Message);
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

        private void RenderDirectCarrierPreview()
        {
            if (_scanner == null ||
                _scanner.CurrentRenderMode == ScanRenderMode.None ||
                !_scanner.IsRoomAnchorReady ||
                _previewMaterial == null || _readBatches.Count == 0)
                return;
            Camera main = Camera.main;
            Vector3 boundsCenter = main != null
                ? main.transform.position
                : Vector3.zero;
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                SegmentReadoutCache cache = _segmentCaches[index];
                ReadoutGeneration front = cache.Front;
                _properties.Clear();
                _properties.SetFloat(PreviewWireframeId,
                    _scanner.CurrentRenderMode == ScanRenderMode.Wireframe
                        ? 1f : 0f);
                _properties.SetFloat(PreviewContactPixelsId,
                    _scanner.CurrentRenderMode == ScanRenderMode.Wireframe
                        ? 1.75f : 1.35f);
                _properties.SetMatrix(RoomToWorldId,
                    SigmaRoomFrame.ToUnityWorld);
                _properties.SetFloat(ReadoutOpacityId, readoutOpacity);
                _properties.SetBuffer(ReadoutSamplesId, front.Samples);
                _properties.SetBuffer(CurrentPageSlotsId,
                    front.CurrentPageSlots);
                _properties.SetBuffer(PageMetadataId,
                    front.RenderPageMetadata);
                var renderParams = new RenderParams(_previewMaterial)
                {
                    worldBounds = new Bounds(boundsCenter,
                        Vector3.one * (2f * ReadoutRadius)),
                    matProps = _properties,
                    receiveShadows = false,
                    shadowCastingMode = ShadowCastingMode.Off,
                    layer = gameObject.layer
                };
                Graphics.RenderPrimitivesIndirect(renderParams,
                    MeshTopology.Triangles, front.DrawArguments, 1);
            }
        }

        private void OnDestroy()
        {
            _running = false;
            _latest?.Dispose();
            _latest = null;
            SigmaPredictionTargetRing targets = _targets;
            _targets = null;
            SigmaPredictionTargetRing correctedTargets = _correctedTargets;
            _correctedTargets = null;
            GraphicsBuffer identityPoseResult = _identityPoseResult;
            _identityPoseResult = null;
            SegmentReadoutCache[] segmentCaches = _segmentCaches.ToArray();
            _segmentCaches.Clear();
            Material predictionMaterial = _predictionMaterial;
            _predictionMaterial = null;
            Material previewMaterial = _previewMaterial;
            _previewMaterial = null;

            void ReleaseOwnedResources()
            {
                targets?.Dispose();
                correctedTargets?.Dispose();
                identityPoseResult?.Dispose();
                for (int index = 0; index < segmentCaches.Length; ++index)
                    segmentCaches[index]?.Dispose();
                DestroyMaterial(predictionMaterial);
                DestroyMaterial(previewMaterial);
            }

            try
            {
                if (_readoutJob != null)
                    throw new InvalidOperationException(
                        "N6 readout writer is retained during renderer teardown.");
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
            _backendGate = null;
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

        internal sealed class ReadoutGeneration : IDisposable
        {
            internal ReadoutGeneration(int segmentIndex, int generationIndex,
                int capacity)
            {
                Samples = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    checked(capacity * ReadoutSamplesPerPage),
                    sizeof(float) * 16)
                {
                    name = $"Sigma pure eye samples {segmentIndex}:{generationIndex}"
                };
                CurrentPageSlots = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, capacity, sizeof(uint))
                {
                    name = $"Sigma current page slots {segmentIndex}:{generationIndex}"
                };
                DrawArguments = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured |
                    GraphicsBuffer.Target.IndirectArguments,
                    4, sizeof(uint))
                {
                    name = $"Sigma readout draw args {segmentIndex}:{generationIndex}"
                };
                DrawArguments.SetData(new uint[] { 0u, 1u, 0u, 0u });
                RenderPageMetadata = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, capacity,
                    SigmaCarrier.PageMetadataStride)
                {
                    name = $"Sigma render page metadata {segmentIndex}:{generationIndex}"
                };
            }

            internal GraphicsBuffer Samples { get; }
            internal GraphicsBuffer CurrentPageSlots { get; }
            internal GraphicsBuffer DrawArguments { get; }
            internal GraphicsBuffer RenderPageMetadata { get; }
            internal bool Initialized { get; private set; }

            internal void MarkInitialized() => Initialized = true;

            public void Dispose()
            {
                Samples.Dispose();
                CurrentPageSlots.Dispose();
                DrawArguments.Dispose();
                RenderPageMetadata.Dispose();
            }
        }

        internal sealed class SegmentReadoutCache : IDisposable
        {
            private readonly GraphicsBuffer _stateIdentity;
            private readonly GraphicsBuffer _metadataIdentity;
            private readonly ReadoutGeneration[] _generations;

            public SegmentReadoutCache(SigmaCarrierReadBatch batch)
            {
                SegmentIndex = batch.SegmentIndex;
                Capacity = batch.PageCapacity;
                _stateIdentity = batch.State;
                _metadataIdentity = batch.Metadata;
                _generations = new[]
                {
                    new ReadoutGeneration(SegmentIndex, 0, Capacity),
                    new ReadoutGeneration(SegmentIndex, 1, Capacity),
                };
            }

            public int SegmentIndex { get; }
            public int Capacity { get; }
            internal int FrontIndex { get; private set; }
            internal int BackIndex => 1 - FrontIndex;
            internal int GenerationCount => _generations.Length;
            internal ReadoutGeneration Front => _generations[FrontIndex];
            internal ReadoutGeneration Back => _generations[BackIndex];
            internal SigmaNativeVulkanReadout.ReadoutJob BackReadyFence { get; private set; }
            internal bool BackSubmitted { get; private set; }
            internal uint FrontRevision { get; private set; }
            internal uint BackRevision { get; private set; }

            internal void MarkBackSubmitted(SigmaNativeVulkanReadout.ReadoutJob fence,
                uint revision)
            {
                if (BackSubmitted || revision == 0u)
                    throw new InvalidOperationException(
                        "Readout BACK publication state is invalid.");
                BackReadyFence = fence;
                BackRevision = revision;
                BackSubmitted = true;
            }

            internal SigmaGpuCompletionStatus PollBack(out string error) =>
                BackSubmitted
                    ? BackReadyFence.Poll(out error)
                    : CompleteWithoutFence(out error);

            internal void PublishBack()
            {
                if (!BackSubmitted)
                    throw new InvalidOperationException(
                        "No readout BACK generation is ready to publish.");
                Back.MarkInitialized();
                FrontIndex = BackIndex;
                FrontRevision = BackRevision;
                BackRevision = 0u;
                BackSubmitted = false;
                BackReadyFence = null;
            }

            private static SigmaGpuCompletionStatus CompleteWithoutFence(
                out string error)
            {
                error = null;
                return SigmaGpuCompletionStatus.Complete;
            }

            public bool Matches(SigmaCarrierReadBatch batch) =>
                SegmentIndex == batch.SegmentIndex &&
                Capacity == batch.PageCapacity &&
                ReferenceEquals(_stateIdentity, batch.State) &&
                ReferenceEquals(_metadataIdentity, batch.Metadata);

            public void Dispose()
            {
                _generations[0].Dispose();
                _generations[1].Dispose();
            }
        }
    }
}
