using System;
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
        public const int ReadoutExtent = SigmaCarrier.PageSize + 1;
        public const int ReadoutSamplesPerPage = ReadoutExtent * ReadoutExtent;
        public const int VerticesPerCarrierPage =
            SigmaCarrier.PageSize * SigmaCarrier.PageSize * 6;

        private const string ReadoutResource = "SigmaPrism/SigmaForwardReadout";
        private const string PredictionResource = "SigmaPrism/SigmaPredict";
        private const string PreviewResource =
            "SigmaPrism/SigmaDirectCarrierPreview";
        private const int CorrectedTargetRingSlots = 4;

        [SerializeField, Range(3, 12)] private int targetRingSlots = 4;
        [SerializeField, Min(16f)] private float directPreviewBounds = 128f;

        private static readonly int ExactGateId = Shader.PropertyToID(
            "_SigmaExactBackendGate");
        private static readonly int CarrierStateId = Shader.PropertyToID(
            "_CarrierState");
        private static readonly int PageMetadataId = Shader.PropertyToID(
            "_PageMetadata");
        private static readonly int PublishedRevisionRootId = Shader.PropertyToID(
            "_PublishedRevisionRoot");
        private static readonly int ReadoutDirtyFlagsId = Shader.PropertyToID(
            "_ReadoutDirtyFlags");
        private static readonly int ReadoutVerticesId = Shader.PropertyToID(
            "_ReadoutVertices");
        private static readonly int CurrentPageSlotsId = Shader.PropertyToID(
            "_CurrentPageSlots");
        private static readonly int ReadoutDrawArgumentsId = Shader.PropertyToID(
            "_ReadoutDrawArguments");
        private static readonly int PreviewDrawArgumentsId = Shader.PropertyToID(
            "_PreviewDrawArguments");
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
        private ComputeShader _readoutCompute;
        private Material _predictionMaterial;
        private Material _previewMaterial;
        private MaterialPropertyBlock _properties;
        private SigmaPredictionTargetRing _targets;
        private SigmaPredictionTargetRing _correctedTargets;
        private GraphicsBuffer _identityPoseResult;
        private SigmaPredictionFrameLease _latest;
        private RigCalibration _calibration;
        private int _buildKernel;
        private int _compactKernel;
        private int _resolveHaloKernel;
        private long _lastSourceSequence;
        private bool _running;
        private bool _initialized;

        public string ModuleName => "Sigma forward readout";
        public bool IsInitialized => _initialized;
        public long RenderedFrames { get; private set; }
        public long BackpressureFrames { get; private set; }

        public event Action<SigmaPredictionFrameLease> PredictionReady;

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
                drawArguments = cache.DrawArguments;
                currentPageSlots = cache.CurrentPageSlots;
                vertices = cache.Vertices;
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
            _readoutCompute = Resources.Load<ComputeShader>(ReadoutResource);
            Shader prediction = Resources.Load<Shader>(PredictionResource);
            Shader preview = Resources.Load<Shader>(PreviewResource);
            if (_carrier == null || _rigBridge == null ||
                _readoutCompute == null || prediction == null || preview == null)
                throw new InvalidOperationException(
                    "Sigma forward-readout resources are incomplete.");

            _buildKernel = _readoutCompute.FindProfiledKernel(
                "BuildCarrierReadout");
            _compactKernel = _readoutCompute.FindProfiledKernel(
                "CompactCurrentPages");
            _resolveHaloKernel = _readoutCompute.FindProfiledKernel(
                "ResolveCarrierHalos");
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
            _properties = new MaterialPropertyBlock();
            _targets = new SigmaPredictionTargetRing(targetRingSlots);
            _correctedTargets = new SigmaPredictionTargetRing(
                CorrectedTargetRingSlots);
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

        internal SigmaPredictionAcquireResult TryAcquirePoseGaugePrediction(
            StereoRigFrameLease source, Matrix4x4 worldToRoom,
            out SigmaPredictionFrameLease prediction)
        {
            prediction = null;
            if (!_initialized || source == null || !source.IsValid ||
                _correctedTargets == null)
                return SigmaPredictionAcquireResult.Faulted;
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
            try
            {
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
                    DrawSegments(command, BuildClipFromWorld(view,
                            opticalFromWorld), opticalFromWorld, poseResult,
                        referenceWorld.inverse, referenceWorld);
                }
            }
            catch
            {
                prediction.Dispose();
                throw;
            }
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
            Matrix4x4 worldToRoom = RoomSpaceRoot.WorldToRoom;
            SigmaPredictionAcquireResult acquisition = _targets.TryBegin(source,
                gauge, worldToRoom, out SigmaPredictionFrameLease prediction);
            if (acquisition != SigmaPredictionAcquireResult.Acquired)
            {
                if (acquisition == SigmaPredictionAcquireResult.Busy)
                    BackpressureFrames++;
                else
                    Logger.Error("Sigma published prediction ring fault: " +
                        (_targets.CompletionFault ?? "unknown fault"));
                return false;
            }

            CommandBuffer command = CommandBufferPool.Get(
                "Sigma-PRISM-16 Forward Readout");
            try
            {
                PrepareReadoutCaches(command);
                for (int eye = 0; eye < 2; ++eye)
                {
                    GpuImageView view = eye == 0 ? source.DepthLeft :
                        source.DepthRight;
                    Pose referencePose = SigmaRoomFrame.CameraPose(worldToRoom,
                        source.DepthLeft.WorldFromCamera);
                    Pose viewPose = SigmaRoomFrame.CameraPose(worldToRoom,
                        view.WorldFromCamera);
                    Pose correctedPose = gauge.Apply(
                        referencePose, viewPose);
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
                    DrawSegments(command, BuildClipFromWorld(view,
                            opticalFromWorld), opticalFromWorld,
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
                Logger.Error("Sigma forward readout failed: " +
                    exception.Message);
                return false;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private bool PrepareReadoutCaches(CommandBuffer command)
        {
            _carrier.CollectReadableSegments(_readBatches);
            EnsureSegmentCaches();
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                SegmentReadoutCache cache = _segmentCaches[index];
                BindCompaction(command, batch, cache);
                command.DispatchComputeProfiled(_readoutCompute,
                    _compactKernel, 1, 1, 1);
                BindBuild(command, batch, cache);
                command.DispatchComputeProfiled(_readoutCompute, _buildKernel,
                    cache.BuildDispatchArguments, 0);
                BindHaloResolve(command, batch, cache);
                command.DispatchComputeProfiled(_readoutCompute,
                    _resolveHaloKernel, cache.HaloDispatchArguments, 0);
            }
            return _readBatches.Count != 0;
        }

        private void BindBuild(CommandBuffer command,
            SigmaCarrierReadBatch batch, SegmentReadoutCache cache)
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
                PublishedRevisionRootId, batch.PublicationRoot);
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
                PageMetadataId, batch.Metadata);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                PublishedRevisionRootId, batch.PublicationRoot);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                ReadoutDirtyFlagsId, batch.ReadoutDirtyFlags);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                CurrentPageSlotsId, cache.CurrentPageSlots);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                ReadoutDrawArgumentsId, cache.DrawArguments);
            command.SetComputeBufferParam(_readoutCompute, _compactKernel,
                PreviewDrawArgumentsId, cache.PreviewDrawArguments);
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
                PublishedRevisionRootId, batch.PublicationRoot);
            command.SetComputeBufferParam(_readoutCompute, _resolveHaloKernel,
                CurrentPageSlotsId, cache.CurrentPageSlots);
            command.SetComputeBufferParam(_readoutCompute, _resolveHaloKernel,
                ReadoutVerticesId, cache.Vertices);
        }

        private void DrawSegments(CommandBuffer command,
            Matrix4x4 clipFromWorld, Matrix4x4 opticalFromWorld,
            GraphicsBuffer poseResult, Matrix4x4 referenceFromWorld,
            Matrix4x4 worldFromReference)
        {
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                SegmentReadoutCache cache = _segmentCaches[index];
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
                _properties.SetBuffer(CurrentPageSlotsId,
                    cache.CurrentPageSlots);
                _properties.SetBuffer(PageMetadataId, batch.Metadata);
                command.DrawProceduralIndirect(Matrix4x4.identity,
                    _predictionMaterial, 0, MeshTopology.Triangles,
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

        // Temporary S4-08 diagnostic backend. S4-11 replaces only this XR
        // presentation with meshlets; the exact world-space readout stays shared.
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
                SegmentReadoutCache cache = _segmentCaches[index];
                _properties.Clear();
                _properties.SetFloat(PreviewWireframeId,
                    _scanner.CurrentRenderMode == ScanRenderMode.Wireframe
                        ? 1f : 0f);
                _properties.SetFloat(PreviewContactPixelsId,
                    _scanner.CurrentRenderMode == ScanRenderMode.Wireframe
                        ? 1.75f : 1.35f);
                _properties.SetMatrix(RoomToWorldId,
                    SigmaRoomFrame.ToUnityWorld);
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
                    MeshTopology.Triangles, cache.PreviewDrawArguments, 1);
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
                PreviewDrawArguments = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured |
                    GraphicsBuffer.Target.IndirectArguments,
                    4, sizeof(uint))
                {
                    name = $"Sigma preview draw args {SegmentIndex}"
                };
                PreviewDrawArguments.SetData(new uint[] { 0u, 1u, 0u, 0u });
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
            public GraphicsBuffer PreviewDrawArguments { get; }
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
                PreviewDrawArguments.Dispose();
                DirtyPageSlots.Dispose();
                BuildDispatchArguments.Dispose();
                HaloDispatchArguments.Dispose();
            }
        }
    }
}
