using System;
using System.Collections.Generic;
using UnityEngine;
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

        [SerializeField, Range(3, 12)] private int targetRingSlots = 4;

        private static readonly int ExactGateId = Shader.PropertyToID(
            "_SigmaExactBackendGate");
        private static readonly int CarrierStateId = Shader.PropertyToID(
            "_CarrierState");
        private static readonly int PageMetadataId = Shader.PropertyToID(
            "_PageMetadata");
        private static readonly int CurrentFlagsId = Shader.PropertyToID(
            "_CurrentFlags");
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
        private static readonly int HaloSourceVerticesId = Shader.PropertyToID(
            "_HaloSourceVertices");
        private static readonly int HaloTargetVerticesId = Shader.PropertyToID(
            "_HaloTargetVertices");
        private static readonly int PageCapacityId = Shader.PropertyToID(
            "_PageCapacity");
        private static readonly int SourcePageCapacityId = Shader.PropertyToID(
            "_SourcePageCapacity");
        private static readonly int SourcePageSlotId = Shader.PropertyToID(
            "_SourcePageSlot");
        private static readonly int TargetPageSlotId = Shader.PropertyToID(
            "_TargetPageSlot");
        private static readonly int HaloDirectionId = Shader.PropertyToID(
            "_HaloDirection");
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

        private readonly List<SigmaCarrierReadBatch> _readBatches = new();
        private readonly List<SigmaCarrierPageHandle> _currentPages = new();
        private readonly HashSet<SigmaCarrierPageCoordinate>
            _readoutChangedCoordinates = new();
        private readonly HashSet<SigmaCarrierPageCoordinate>
            _haloRefreshCoordinates = new();
        private readonly List<SigmaCarrierPageCoordinate> _coordinateOrder = new();
        private readonly List<SegmentReadoutCache> _segmentCaches = new();
        private readonly RenderTargetIdentifier[] _mrt =
            new RenderTargetIdentifier[4];

        private RoomScanner _scanner;
        private SigmaCarrier _carrier;
        private SigmaTopologyController _topology;
        private SigmaRigBridge _rigBridge;
        private SigmaExactBackendGate _backendGate;
        private ComputeShader _readoutCompute;
        private Material _predictionMaterial;
        private MaterialPropertyBlock _properties;
        private SigmaPredictionTargetRing _targets;
        private SigmaPredictionFrameLease _latest;
        private RigCalibration _calibration;
        private int _buildKernel;
        private int _clearHaloKernel;
        private int _copyHaloKernel;
        private int _compactKernel;
        private long _lastSourceSequence;
        private bool _running;
        private bool _initialized;

        public string ModuleName => "Sigma forward readout";
        public bool IsInitialized => _initialized;
        public long RenderedFrames { get; private set; }
        public long BackpressureFrames { get; private set; }

        public event Action<SigmaPredictionFrameLease> PredictionReady;

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
            Shader prediction = Resources.Load<Shader>(PredictionResource);
            if (_carrier == null || _topology == null || _rigBridge == null ||
                _readoutCompute == null || prediction == null)
                throw new InvalidOperationException(
                    "Sigma forward-readout resources are incomplete.");

            _buildKernel = _readoutCompute.FindKernel("BuildCarrierReadout");
            _clearHaloKernel = _readoutCompute.FindKernel("ClearCarrierHalos");
            _copyHaloKernel = _readoutCompute.FindKernel("CopyCarrierHalo");
            _compactKernel = _readoutCompute.FindKernel("CompactCurrentPages");
            _predictionMaterial = new Material(prediction)
            {
                name = "[Sigma-PRISM-16] Prediction Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            _properties = new MaterialPropertyBlock();
            _targets = new SigmaPredictionTargetRing(targetRingSlots);
            _carrier.ReadoutChanged += OnCarrierReadoutChanged;
            if (_carrier.IsInitialized)
            {
                _carrier.CollectCurrentPages(_currentPages);
                for (int index = 0; index < _currentPages.Count; ++index)
                    _readoutChangedCoordinates.Add(
                        _currentPages[index].Coordinate);
            }
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
            if (!_running || !_initialized || !_carrier.IsInitialized ||
                !_rigBridge.TryAcquireLatest(out StereoRigFrameLease source))
                return;
            try
            {
                if (source.Sequence == _lastSourceSequence)
                    return;
                if (!TryRender(source))
                    return;
                _lastSourceSequence = source.Sequence;
            }
            finally
            {
                source.Dispose();
            }
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
                bool cacheChanged = PrepareReadoutCaches(command);
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
                    DrawSegments(command, clipFromWorld, opticalFromWorld);
                }

                Graphics.ExecuteCommandBuffer(command);
                if (cacheChanged)
                {
                    for (int index = 0; index < _readBatches.Count; ++index)
                        _segmentCaches[index].BuiltRevision =
                            _readBatches[index].ReadoutRevision;
                }
                if (cacheChanged)
                    _readoutChangedCoordinates.Clear();
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
            _carrier.CollectReadableSegments(_readBatches);
            EnsureSegmentCaches();
            bool changed = false;
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                SegmentReadoutCache cache = _segmentCaches[index];
                if (cache.BuiltRevision == batch.ReadoutRevision)
                    continue;
                changed = true;
                BindCompaction(command, batch, cache);
                command.DispatchCompute(_readoutCompute, _compactKernel, 1, 1, 1);
                BindBuild(command, batch, cache);
                command.DispatchCompute(_readoutCompute, _buildKernel,
                    cache.BuildDispatchArguments, 0);
            }
            if (!changed)
                return false;

            RefreshChangedHalos(command);
            return true;
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
                CurrentFlagsId, batch.CurrentFlags);
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
                CurrentFlagsId, batch.CurrentFlags);
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
        }

        private void OnCarrierReadoutChanged(
            SigmaCarrierPageCoordinate coordinate) =>
            _readoutChangedCoordinates.Add(coordinate);

        private void RefreshChangedHalos(CommandBuffer command)
        {
            _haloRefreshCoordinates.Clear();
            foreach (SigmaCarrierPageCoordinate changed in
                _readoutChangedCoordinates)
            {
                _haloRefreshCoordinates.Add(changed);
                AddOffset(_haloRefreshCoordinates, changed, -1L, 0L);
                AddOffset(_haloRefreshCoordinates, changed, 0L, -1L);
                AddOffset(_haloRefreshCoordinates, changed, -1L, -1L);
            }
            _coordinateOrder.Clear();
            foreach (SigmaCarrierPageCoordinate coordinate in
                _haloRefreshCoordinates)
                _coordinateOrder.Add(coordinate);
            _coordinateOrder.Sort(static (left, right) => left.CompareTo(right));

            for (int index = 0; index < _coordinateOrder.Count; ++index)
            {
                if (!_carrier.TryGetLatest(_coordinateOrder[index],
                        out SigmaCarrierPageHandle target))
                    continue;
                ClearPageHalos(command, target);
                CopyNeighbourHalo(command, target, 1L, 0L, 0);
                CopyNeighbourHalo(command, target, 0L, 1L, 1);
                CopyNeighbourHalo(command, target, 1L, 1L, 2);
            }
        }

        private void ClearPageHalos(CommandBuffer command,
            SigmaCarrierPageHandle target)
        {
            SigmaCarrierReadBatch batch = _readBatches[target.SegmentIndex];
            SegmentReadoutCache cache = _segmentCaches[target.SegmentIndex];
            command.SetComputeIntParam(_readoutCompute, PageCapacityId,
                batch.PageCapacity);
            command.SetComputeIntParam(_readoutCompute, TargetPageSlotId,
                target.PageSlot);
            command.SetComputeBufferParam(_readoutCompute, _clearHaloKernel,
                ReadoutVerticesId, cache.Vertices);
            command.DispatchCompute(_readoutCompute, _clearHaloKernel, 1, 1, 1);
        }

        private static void AddOffset(
            HashSet<SigmaCarrierPageCoordinate> destination,
            SigmaCarrierPageCoordinate source, long deltaX, long deltaY)
        {
            if (TryOffset(source, deltaX, deltaY, out var result))
                destination.Add(result);
        }

        private void CopyNeighbourHalo(CommandBuffer command,
            SigmaCarrierPageHandle target, long deltaX, long deltaY, int direction)
        {
            if (!TryOffset(target.Coordinate, deltaX, deltaY,
                    out SigmaCarrierPageCoordinate neighbourCoordinate) ||
                !_carrier.TryGetLatest(neighbourCoordinate,
                    out SigmaCarrierPageHandle source))
                return;
            SegmentReadoutCache targetCache = _segmentCaches[target.SegmentIndex];
            SegmentReadoutCache sourceCache = _segmentCaches[source.SegmentIndex];
            SigmaCarrierReadBatch targetBatch = _readBatches[target.SegmentIndex];
            SigmaCarrierReadBatch sourceBatch = _readBatches[source.SegmentIndex];
            command.SetComputeIntParam(_readoutCompute, PageCapacityId,
                targetBatch.PageCapacity);
            command.SetComputeIntParam(_readoutCompute, SourcePageCapacityId,
                sourceBatch.PageCapacity);
            command.SetComputeIntParam(_readoutCompute, TargetPageSlotId,
                target.PageSlot);
            command.SetComputeIntParam(_readoutCompute, SourcePageSlotId,
                source.PageSlot);
            command.SetComputeIntParam(_readoutCompute, HaloDirectionId, direction);
            command.SetComputeBufferParam(_readoutCompute, _copyHaloKernel,
                HaloTargetVerticesId, targetCache.Vertices);
            command.SetComputeBufferParam(_readoutCompute, _copyHaloKernel,
                HaloSourceVerticesId, sourceCache.Vertices);
            command.DispatchCompute(_readoutCompute, _copyHaloKernel, 1, 1, 1);
        }

        private void DrawSegments(CommandBuffer command, Matrix4x4 clipFromWorld,
            Matrix4x4 opticalFromWorld)
        {
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                SegmentReadoutCache cache = _segmentCaches[index];
                if (!_topology.TryGetSegmentView(batch.SegmentIndex,
                        out SigmaTopologySegmentView topologyView))
                    continue;
                _properties.Clear();
                _properties.SetMatrix(ClipFromWorldId, clipFromWorld);
                _properties.SetMatrix(OpticalFromWorldId, opticalFromWorld);
                _properties.SetInt(SegmentIndexId, batch.SegmentIndex);
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

        private void OnDestroy()
        {
            _running = false;
            if (_carrier != null)
                _carrier.ReadoutChanged -= OnCarrierReadoutChanged;
            _latest?.Dispose();
            _latest = null;
            _targets?.Dispose();
            _targets = null;
            for (int index = 0; index < _segmentCaches.Count; ++index)
                _segmentCaches[index].Dispose();
            _segmentCaches.Clear();
            if (_predictionMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_predictionMaterial);
                else
                    DestroyImmediate(_predictionMaterial);
            }
            _predictionMaterial = null;
            _readoutCompute = null;
            _backendGate = null;
            _topology = null;
            _carrier = null;
            _rigBridge = null;
            _scanner = null;
            _initialized = false;
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
            }

            public int SegmentIndex { get; }
            public int Capacity { get; }
            public ulong BuiltRevision { get; set; }
            public GraphicsBuffer Vertices { get; }
            public GraphicsBuffer CurrentPageSlots { get; }
            public GraphicsBuffer DrawArguments { get; }
            public GraphicsBuffer DirtyPageSlots { get; }
            public GraphicsBuffer BuildDispatchArguments { get; }

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
            }
        }
    }
}
