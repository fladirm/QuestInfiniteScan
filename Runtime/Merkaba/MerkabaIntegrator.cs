using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>GPU-only M8 surface discovery/integration and Q_SCAN carve.</summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaIntegrator : MonoBehaviour
    {
        [SerializeField] private ComputeShader compute;
        [SerializeField, Range(1f, 8f)] private float maxUpdateDistance = 5f;
        [SerializeField, Range(1f, 10f)] private float cameraExposure = 3f;
        [SerializeField, Min(0)] private int warmupIntegrations = 3;

        private const double HeldObservationTimeoutSeconds = 10.0;

        private MerkabaGrid _grid;
        private DepthCapture _depthCapture;
        private int _discoverKernel;
        private int _prepareResolveKernel;
        private int _resolveBlocksKernel;
        private int _resolveChunksKernel;
        private int _resolveTilesKernel;
        private int _queueResolvedKernel;
        private int _retryPendingTilesKernel;
        private int _prepareIntegrateKernel;
        private int _integrateSurfaceKernel;
        private int _queryCarveKernel;
        private int _prepareCarveKernel;
        private int _integrateCarveKernel;
        private int _finalizeKernel;
        private bool _initialized;
        private bool _observationPrepared;
        private uint _observationToken;
        private double _observationPreparedAt;
        private int _observationDepthVersion;
        private bool _attemptInFlight;
        private bool _waitingForDependency;
        private uint _attemptSequence;
        private uint _attemptToken;
        private uint _retryDependencyVersion;

        private readonly bool[] _cameraAvailable = new bool[2];
        private readonly Vector3[] _cameraPosition = new Vector3[2];
        private readonly Quaternion[] _cameraRotation = new Quaternion[2];
        private readonly Vector2[] _cameraFocalLength = new Vector2[2];
        private readonly Vector2[] _cameraPrincipalPoint = new Vector2[2];
        private readonly Vector2[] _cameraSensorResolution = new Vector2[2];
        private readonly Vector2[] _cameraCurrentResolution = new Vector2[2];
        private readonly RenderTexture[] _cameraFrameCopies = new RenderTexture[2];
        private int _readyCameraSlot = -1;
        private int _heldCameraSlot = -1;
        private bool _cameraObservationHeld;
        private ulong _cameraCopySubmittedEpoch;
        private ulong _cameraCopyRetiredEpoch;
        private int _lastCameraCopySlot = -1;
        private Task _cameraCopyRetirementTask = Task.CompletedTask;
        private Texture2D _dummyCameraTexture;
        private readonly Vector4[] _exclusionPositions = new Vector4[64];

        public readonly List<Transform> ExclusionZones = new();
        public int IntegrationCount { get; private set; }
        public float MaxUpdateDistance => maxUpdateDistance;
        public bool HasPendingObservation => _observationPrepared;
        public bool HasAttemptInFlight => _attemptInFlight;
        internal uint ObservationToken => _observationToken;
        internal uint AttemptToken => _attemptToken;
        internal RenderTexture OwnedCameraFrame
        {
            get
            {
                int slot = _readyCameraSlot >= 0
                    ? _readyCameraSlot : _heldCameraSlot;
                return slot >= 0 ? _cameraFrameCopies[slot] : null;
            }
        }
        internal bool CameraFrameAvailable
        {
            get
            {
                int slot = _readyCameraSlot >= 0
                    ? _readyCameraSlot : _heldCameraSlot;
                return slot >= 0 && _cameraAvailable[slot];
            }
        }
        public event Action Integrated;

        private static readonly int GridToWorldId =
            Shader.PropertyToID("_MerkabaGridToWorld");
        private static readonly int WorldToGridId =
            Shader.PropertyToID("_MerkabaWorldToGrid");
        private static readonly int MaxDistanceId =
            Shader.PropertyToID("_MerkabaMaxUpdateDistance");
        private static readonly int ExclusionCountId =
            Shader.PropertyToID("_MerkabaExclusionCount");
        private static readonly int ExclusionHeadsId =
            Shader.PropertyToID("_MerkabaExclusionHeads");
        private static readonly int CameraRgbId = Shader.PropertyToID("_MerkabaCameraRgb");
        private static readonly int CameraAvailableId =
            Shader.PropertyToID("_MerkabaCameraAvailable");
        private static readonly int CameraPositionId =
            Shader.PropertyToID("_MerkabaCameraPosition");
        private static readonly int CameraInverseRotationId =
            Shader.PropertyToID("_MerkabaCameraInverseRotation");
        private static readonly int CameraFocalLengthId =
            Shader.PropertyToID("_MerkabaCameraFocalLength");
        private static readonly int CameraPrincipalPointId =
            Shader.PropertyToID("_MerkabaCameraPrincipalPoint");
        private static readonly int CameraSensorResolutionId =
            Shader.PropertyToID("_MerkabaCameraSensorResolution");
        private static readonly int CameraCurrentResolutionId =
            Shader.PropertyToID("_MerkabaCameraCurrentResolution");
        private static readonly int CameraExposureId =
            Shader.PropertyToID("_MerkabaCameraExposure");
        private static readonly int AbortObservationId =
            Shader.PropertyToID("_M8AbortObservation");
        private static readonly int AttemptTokenId =
            Shader.PropertyToID("_M8AttemptToken");

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _depthCapture = GetComponent<DepthCapture>();
        }

        internal void ReleaseOwnedResourcesAfterGpuRetirement()
        {
            for (int slot = 0; slot < _cameraFrameCopies.Length; slot++)
            {
                if (_cameraFrameCopies[slot] != null)
                    Destroy(_cameraFrameCopies[slot]);
                _cameraFrameCopies[slot] = null;
            }
            if (_dummyCameraTexture != null) Destroy(_dummyCameraTexture);
            _dummyCameraTexture = null;
            _initialized = false;
        }

        internal Action CaptureOwnedGpuResourceRelease()
        {
            UnityEngine.Object[] captured =
            {
                _cameraFrameCopies[0], _cameraFrameCopies[1],
                _dummyCameraTexture
            };
            bool released = false;
            return () =>
            {
                if (released) return;
                released = true;
                if (this != null)
                {
                    ReleaseOwnedResourcesAfterGpuRetirement();
                    return;
                }
                foreach (UnityEngine.Object resource in captured)
                    if (resource != null) UnityEngine.Object.Destroy(resource);
            };
        }

        private bool Initialize()
        {
            if (_initialized) return true;
            if (compute == null || _grid == null || _depthCapture == null)
                return false;
            _grid.EnsureGpuResources();
            _discoverKernel = compute.FindProfiledKernel(
                "DiscoverSurfaceCandidates", MerkabaGpuStage.SurfaceIntegration);
            _prepareResolveKernel = compute.FindProfiledKernel(
                "PrepareResolveArgs", MerkabaGpuStage.SurfaceIntegration);
            _resolveBlocksKernel = compute.FindProfiledKernel(
                "ResolveSurfaceBlocks", MerkabaGpuStage.SurfaceIntegration);
            _resolveChunksKernel = compute.FindProfiledKernel(
                "ResolveSurfaceChunks", MerkabaGpuStage.SurfaceIntegration);
            _resolveTilesKernel = compute.FindProfiledKernel(
                "ResolveSurfaceTiles", MerkabaGpuStage.SurfaceIntegration);
            _queueResolvedKernel = compute.FindProfiledKernel(
                "QueueResolvedSurfaceCandidates",
                MerkabaGpuStage.SurfaceIntegration);
            _retryPendingTilesKernel = compute.FindProfiledKernel(
                "RetryPendingNewTiles", MerkabaGpuStage.SurfaceIntegration);
            _prepareIntegrateKernel = compute.FindProfiledKernel(
                "PrepareIntegrateArgs", MerkabaGpuStage.SurfaceIntegration);
            _integrateSurfaceKernel = compute.FindProfiledKernel(
                "IntegrateSurfaceCandidates",
                MerkabaGpuStage.SurfaceIntegration);
            _queryCarveKernel = compute.FindProfiledKernel(
                "QueryCarveTiles", MerkabaGpuStage.CarveIntegration);
            _prepareCarveKernel = compute.FindProfiledKernel(
                "PrepareCarveArgs", MerkabaGpuStage.CarveIntegration);
            _integrateCarveKernel = compute.FindProfiledKernel(
                "IntegrateCarveTiles", MerkabaGpuStage.CarveIntegration);
            _finalizeKernel = compute.FindProfiledKernel(
                "FinalizeObservation", MerkabaGpuStage.SurfaceIntegration);
            foreach (int kernel in new[]
                     {
                         _discoverKernel, _prepareResolveKernel,
                         _resolveBlocksKernel, _resolveChunksKernel,
                         _resolveTilesKernel, _queueResolvedKernel,
                         _retryPendingTilesKernel, _prepareIntegrateKernel,
                         _integrateSurfaceKernel, _queryCarveKernel,
                         _prepareCarveKernel, _integrateCarveKernel, _finalizeKernel
                     })
            {
                _grid.BindWorldBuffers(compute, kernel);
                BindWorkBuffers(kernel);
            }
            _initialized = true;
            return true;
        }

        private void BindWorkBuffers(int kernel)
        {
            compute.SetBuffer(kernel, "_M8SurfaceCandidates",
                _grid.M8SurfaceCandidates);
            compute.SetBuffer(kernel, "_M8SurfaceCandidatesRead",
                _grid.M8SurfaceCandidates);
            compute.SetBuffer(kernel, "_M8SurfaceQueue", _grid.M8SurfaceQueue);
            compute.SetBuffer(kernel, "_M8SurfaceQueueRead",
                _grid.M8SurfaceQueue);
            compute.SetBuffer(kernel, "_M8TouchedTileQueue",
                _grid.M8TouchedTileQueue);
            compute.SetBuffer(kernel, "_M8CarveTiles", _grid.M8CarveTiles);
            compute.SetBuffer(kernel, "_M8CarveTilesRead", _grid.M8CarveTiles);
            compute.SetBuffer(kernel, "_M8ObservationDispatchArgs",
                _grid.M8ObservationDispatchArgs);
            compute.SetBuffer(kernel, "_M8CarveDispatchArgs",
                _grid.M8CarveDispatchArgs);
        }

        public void SetCameraData(Texture frame, Vector3 position,
            Quaternion rotation, Vector2 focalLength, Vector2 principalPoint,
            Vector2 sensorResolution, Vector2 currentResolution)
        {
            if (!ReferenceEquals(_grid, null) &&
                _grid.GpuSubmissionSuspended) return;
            int slot = _cameraObservationHeld && _heldCameraSlot >= 0
                ? 1 - _heldCameraSlot
                : _readyCameraSlot >= 0 ? _readyCameraSlot : 0;
            _cameraAvailable[slot] = frame != null;
            if (frame != null) CopyCameraFrame(frame, slot);
            _cameraPosition[slot] = position;
            _cameraRotation[slot] = rotation;
            _cameraFocalLength[slot] = focalLength;
            _cameraPrincipalPoint[slot] = principalPoint;
            _cameraSensorResolution[slot] = sensorResolution;
            _cameraCurrentResolution[slot] = currentResolution;
            _readyCameraSlot = slot;
        }

        internal bool TryRetireObservationAttempt()
        {
            if (!_observationPrepared || !_attemptInFlight ||
                _grid.CompletedAttemptToken != _attemptToken)
                return false;

            _attemptInFlight = false;
            if (_grid.CompletedObservationToken == _observationToken)
            {
                Logger.Info("Merkaba observation complete " +
                            $"observation={_observationToken} " +
                            $"attempt={_attemptToken} " +
                            $"depthVersion={_observationDepthVersion} " +
                            $"failure=0x{_grid.CompletedObservationFailure:x}");
                return FinishObservation(_grid.CompletedObservationFailure);
            }

            _waitingForDependency = true;
            _retryDependencyVersion = _grid.ObservationDependencyVersion;
            Logger.Info("Merkaba observation attempt unresolved " +
                        $"observation={_observationToken} " +
                        $"attempt={_attemptToken} " +
                        $"dependencyVersion={_retryDependencyVersion}");
            return false;
        }

        internal bool TrySubmitObservationAttempt()
        {
            if (_grid == null || _grid.GpuSubmissionSuspended ||
                !Initialize() || _attemptInFlight)
                return false;
            bool newObservation = !_observationPrepared;
            if (newObservation)
            {
                if (!DepthCapture.DepthAvailable ||
                    !_depthCapture.HasUnprocessedFrame)
                    return false;
            }
            else if (!CanRetryPreparedObservation())
                return false;

            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba M8 observation");
            bool submitted = false;
            try
            {
                MerkabaGpuTimestamps.TryBeginFrame(
                    unchecked((uint)Math.Max(1, IntegrationCount + 1)));
                MerkabaGpuTimestamps.RecordProfileBegin(command);
                if (newObservation)
                {
                    AcquireCameraObservation();
                    bool consumed = _depthCapture.ConsumeLatestDepthFrame(command);
                    if (!consumed || _depthCapture.DepthTex == null ||
                        _depthCapture.NormTex == null ||
                        _depthCapture.DilatedDepthTex == null)
                    {
                        ReleaseOwnedObservation();
                        MerkabaGpuTimestamps.CancelUnsubmittedFrame();
                        return false;
                    }
                    _observationToken =
                        _grid.RecordResetObservationGpuCounters(command);
                    _observationPreparedAt =
                        Time.realtimeSinceStartupAsDouble;
                    _observationDepthVersion =
                        _depthCapture.ProcessedRawFrameVersion;
                    _observationPrepared = true;
                }

                _attemptToken = NextAttemptToken();
                command.SetComputeIntParam(compute, AttemptTokenId,
                    unchecked((int)_attemptToken));
                if (newObservation)
                {
                    ConfigureObservation();
                    command.DispatchComputeProfiled(compute, _discoverKernel,
                        Mathf.CeilToInt(_depthCapture.DepthTex.width / 8f),
                        Mathf.CeilToInt(_depthCapture.DepthTex.height / 8f), 2);
                }
                else
                    ConfigureAttempt();

                // Dispatch boundaries publish each CLAIMED radix level before the
                // next one. CLAIMED never spins inside a shader invocation.
                command.DispatchComputeProfiled(compute, _prepareResolveKernel,
                    1, 1, 1);
                command.DispatchComputeProfiled(compute, _resolveBlocksKernel,
                    _grid.M8ObservationDispatchArgs);
                _grid.RecordPublishClaimedBlocks(command);
                command.DispatchComputeProfiled(compute, _resolveChunksKernel,
                    _grid.M8ObservationDispatchArgs);
                _grid.RecordPublishClaimedChunks(command);
                command.DispatchComputeProfiled(compute, _resolveTilesKernel,
                    _grid.M8ObservationDispatchArgs);
                command.DispatchComputeProfiled(compute,
                    _retryPendingTilesKernel, _grid.M8ObservationDispatchArgs);
                _grid.RecordPrepareNewTileDispatch(command);
                _grid.RecordInitializeClaimedTiles(command);
                _grid.RecordResetClaimQueues(command);
                command.DispatchComputeProfiled(compute, _queueResolvedKernel,
                    _grid.M8ObservationDispatchArgs);

                // Q_SCAN must resolve the whole corrective working set before
                // either SURFACE or FREE mutates this immutable observation.
                DispatchCarveQuery(command);
                command.DispatchComputeProfiled(compute,
                    _prepareIntegrateKernel, 1, 1, 1);
                command.DispatchComputeProfiled(compute,
                    _integrateSurfaceKernel, _grid.M8ObservationDispatchArgs);
                command.DispatchComputeProfiled(compute, _prepareCarveKernel,
                    1, 1, 1);
                command.DispatchComputeProfiled(compute, _integrateCarveKernel,
                    _grid.M8CarveDispatchArgs);
                command.DispatchComputeProfiled(compute, _finalizeKernel,
                    1, 1, 1);
                _grid.RecordClearTouchedSurfaceCandidates(command);

                Graphics.ExecuteCommandBuffer(command);
                submitted = true;
                _attemptInFlight = true;
                _waitingForDependency = false;
                Logger.Info("Merkaba observation attempt submitted " +
                            $"observation={_observationToken} " +
                            $"attempt={_attemptToken} " +
                            $"depthVersion={_observationDepthVersion} " +
                            $"retry={!newObservation}");

                // Completion is sampled by the fixed SSD control pump. Until its
                // exact token completes, this owned observation remains immutable.
                return true;
            }
            finally
            {
                if (!submitted)
                    MerkabaGpuTimestamps.CancelUnsubmittedFrame();
                CommandBufferPool.Release(command);
            }
        }

        private bool CanRetryPreparedObservation()
        {
            if (!_observationPrepared || _attemptInFlight) return false;
            if (ObservationTimedOut()) return true;
            return _waitingForDependency &&
                   _grid.ObservationDependencyVersion !=
                   _retryDependencyVersion;
        }

        private bool ObservationTimedOut() =>
            _observationPreparedAt > 0.0 &&
            Time.realtimeSinceStartupAsDouble - _observationPreparedAt >=
            HeldObservationTimeoutSeconds;

        private uint NextAttemptToken()
        {
            unchecked
            {
                _attemptSequence++;
                if (_attemptSequence == 0u) _attemptSequence = 1u;
            }
            return _attemptSequence;
        }

        private bool FinishObservation(uint failureReason)
        {
            _observationPrepared = false;
            _observationToken = 0u;
            _observationPreparedAt = 0.0;
            _observationDepthVersion = 0;
            _attemptInFlight = false;
            _attemptToken = 0u;
            _waitingForDependency = false;
            _retryDependencyVersion = 0u;
            ReleaseOwnedObservation();
            if (failureReason != 0u)
            {
                Logger.Error("Merkaba observation rejected without canonical " +
                             $"mutation; failure=0x{failureReason:x}");
                return false;
            }
            IntegrationCount++;
            if (warmupIntegrations > 0 && IntegrationCount == warmupIntegrations)
            {
                _grid.Clear();
                Logger.Info($"Merkaba warmup complete ({warmupIntegrations}); " +
                            "discarded startup evidence");
            }
            Integrated?.Invoke();
            return true;
        }

        internal async System.Threading.Tasks.Task FinishCurrentObservationAsync()
        {
            while (_observationPrepared)
            {
                TryRetireObservationAttempt();
                if (!_observationPrepared) break;
                if (!_attemptInFlight)
                    TrySubmitObservationAttempt();
                if (_observationPrepared)
                {
                    _grid?.PumpStorageForLifecycleRetirement();
                    await System.Threading.Tasks.Task.Yield();
                }
            }
        }

        private void ConfigureObservation()
        {
            compute.SetMatrixArray(DepthCapture.ViewID, _depthCapture.View);
            compute.SetMatrixArray(DepthCapture.ProjID, _depthCapture.Proj);
            compute.SetMatrixArray(DepthCapture.ViewInvID, _depthCapture.ViewInv);
            compute.SetMatrixArray(DepthCapture.ProjInvID, _depthCapture.ProjInv);
            compute.SetVector(DepthCapture.ZParamsID, _depthCapture.Planes);
            compute.SetVector(DepthCapture.TexSizeID,
                new Vector2(_depthCapture.DepthTex.width,
                    _depthCapture.DepthTex.height));
            compute.SetMatrix(GridToWorldId, _grid.GridToWorldMatrix);
            compute.SetMatrix(WorldToGridId, _grid.GridToWorldMatrix.inverse);
            compute.SetFloat(MaxDistanceId, maxUpdateDistance);
            ConfigureAttempt();

            int exclusionCount = Mathf.Min(ExclusionZones.Count,
                _exclusionPositions.Length);
            for (int i = 0; i < exclusionCount; i++)
                _exclusionPositions[i] = ExclusionZones[i] != null
                    ? ExclusionZones[i].position : Vector3.positiveInfinity;
            compute.SetInt(ExclusionCountId, exclusionCount);
            compute.SetVectorArray(ExclusionHeadsId, _exclusionPositions);

            BindDepth(_discoverKernel);
            BindDepth(_integrateSurfaceKernel);
            BindDepth(_integrateCarveKernel);
            BindCamera(_integrateSurfaceKernel);
        }

        private void ConfigureAttempt()
        {
            compute.SetInt(AbortObservationId,
                ObservationTimedOut() ? 1 : 0);
        }

        private void BindDepth(int kernel)
        {
            compute.SetTexture(kernel, DepthCapture.DepthTexID,
                _depthCapture.DepthTex);
            compute.SetTexture(kernel, DepthCapture.NormTexID,
                _depthCapture.NormTex);
            compute.SetTexture(kernel, DepthCapture.DilatedDepthTexID,
                _depthCapture.DilatedDepthTex);
        }

        private void BindCamera(int kernel)
        {
            bool available = _cameraObservationHeld && _heldCameraSlot >= 0 &&
                             _cameraAvailable[_heldCameraSlot];
            Texture cameraTexture = available
                ? _cameraFrameCopies[_heldCameraSlot] : DummyCameraTexture();
            compute.SetTexture(kernel, CameraRgbId, cameraTexture);
            compute.SetInt(CameraAvailableId, available ? 1 : 0);
            int slot = _heldCameraSlot;
            compute.SetVector(CameraPositionId, slot >= 0
                ? _cameraPosition[slot] : Vector3.zero);
            compute.SetMatrix(CameraInverseRotationId,
                Matrix4x4.Rotate(slot >= 0
                    ? _cameraRotation[slot] : Quaternion.identity).inverse);
            compute.SetVector(CameraFocalLengthId, slot >= 0
                ? _cameraFocalLength[slot] : Vector2.one);
            compute.SetVector(CameraPrincipalPointId, slot >= 0
                ? _cameraPrincipalPoint[slot] : Vector2.zero);
            compute.SetVector(CameraSensorResolutionId, slot >= 0
                ? _cameraSensorResolution[slot] : Vector2.one);
            compute.SetVector(CameraCurrentResolutionId, slot >= 0
                ? _cameraCurrentResolution[slot] : Vector2.one);
            compute.SetFloat(CameraExposureId, cameraExposure);
        }

        private void DispatchCarveQuery(CommandBuffer command)
        {
            Vector3 leftOrigin = _depthCapture.ViewInv[0].GetColumn(3);
            Vector3 rightOrigin = _depthCapture.ViewInv[1].GetColumn(3);
            Vector3 observationOrigin = (leftOrigin + rightOrigin) * 0.5f;
            Matrix4x4 worldToGrid = _grid.GridToWorldMatrix.inverse;
            float3 gridCamera = (float3)worldToGrid.MultiplyPoint3x4(
                observationOrigin) / MerkabaConstants.LatticeStep;
            int3 globalKernel = (int3)math.floor(gridCamera);
            int3 centerBlock = MerkabaSpatial.Encode(globalKernel).BlockCoord;
            int radius = Mathf.CeilToInt(maxUpdateDistance /
                MerkabaSpatial.BlockWorldSize) + 1;
            int side = radius * 2 + 1;
            compute.SetInts("_M8ScanCenterBlock", centerBlock.x,
                centerBlock.y, centerBlock.z);
            compute.SetInt("_M8ScanBlockRadius", radius);
            compute.SetInt("_M8ScanBlockSide", side);
            compute.SetVector("_M8ScanCameraWorld", observationOrigin);

            command.DispatchComputeProfiled(compute, _queryCarveKernel,
                side * side * side, 1, 1);
        }

        private void AcquireCameraObservation()
        {
            _cameraObservationHeld = true;
            _heldCameraSlot = _readyCameraSlot;
            _readyCameraSlot = -1;
            bool available = _heldCameraSlot >= 0 &&
                             _cameraAvailable[_heldCameraSlot];
            _depthCapture.SetRGBGuide(available
                ? _cameraFrameCopies[_heldCameraSlot] : null);
        }

        private void ReleaseOwnedObservation()
        {
            _depthCapture?.ReleaseConsumedObservation();
            _depthCapture?.SetRGBGuide(null);
            _cameraObservationHeld = false;
            _heldCameraSlot = -1;
        }

        private void CopyCameraFrame(Texture frame, int slot)
        {
            int width = Mathf.Max(1, frame.width);
            int height = Mathf.Max(1, frame.height);
            RenderTexture owned = _cameraFrameCopies[slot];
            if (owned == null || owned.width != width || owned.height != height)
            {
                if (owned != null) Destroy(owned);
                owned = new RenderTexture(width, height, 0,
                    GraphicsFormat.R8G8B8A8_UNorm)
                {
                    name = $"Merkaba Owned PCA {slot}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                owned.Create();
                _cameraFrameCopies[slot] = owned;
            }
            Graphics.Blit(frame, owned);
            unchecked
            {
                _cameraCopySubmittedEpoch++;
                if (_cameraCopySubmittedEpoch == 0u)
                    _cameraCopySubmittedEpoch = 1u;
            }
            _lastCameraCopySlot = slot;
        }

        internal void BeginObservationQuiesce()
        {
            _readyCameraSlot = -1;
        }

        internal Task RetireSubmittedCameraCopiesAsync()
        {
            ulong target = _cameraCopySubmittedEpoch;
            if (_cameraCopyRetiredEpoch >= target || target == 0u)
                return Task.CompletedTask;
            if (!_cameraCopyRetirementTask.IsCompleted)
                return _cameraCopyRetirementTask;
            if (!SystemInfo.supportsAsyncGPUReadback)
                return Task.FromException(new NotSupportedException(
                    "Quest PCA copy retirement requires asynchronous GPU readback."));
            if (_lastCameraCopySlot < 0 ||
                _cameraFrameCopies[_lastCameraCopySlot] == null)
                return Task.FromException(new IOException(
                    "Owned PCA copy target disappeared before retirement."));

            RenderTexture owned = _cameraFrameCopies[_lastCameraCopySlot];
            var completion = new TaskCompletionSource<bool>();
            _cameraCopyRetirementTask = completion.Task;
            AsyncGPUReadback.Request(owned, 0, 0, 1, 0, 1, 0, 1, request =>
            {
                if (request.hasError)
                {
                    completion.TrySetException(new IOException(
                        "Owned PCA GPU-copy retirement readback failed."));
                    return;
                }
                _cameraCopyRetiredEpoch = Math.Max(
                    _cameraCopyRetiredEpoch, target);
                completion.TrySetResult(true);
            });
            return _cameraCopyRetirementTask;
        }

        private Texture2D DummyCameraTexture()
        {
            if (_dummyCameraTexture != null) return _dummyCameraTexture;
            _dummyCameraTexture = new Texture2D(1, 1,
                TextureFormat.RGBA32, false, true);
            _dummyCameraTexture.SetPixel(0, 0, Color.black);
            _dummyCameraTexture.Apply(false, true);
            return _dummyCameraTexture;
        }

        public void Clear()
        {
            _grid?.Clear();
            _observationPrepared = false;
            _observationToken = 0u;
            _observationPreparedAt = 0.0;
            _observationDepthVersion = 0;
            _attemptInFlight = false;
            _attemptToken = 0u;
            _waitingForDependency = false;
            _retryDependencyVersion = 0u;
            ReleaseOwnedObservation();
            _readyCameraSlot = -1;
            IntegrationCount = 0;
        }

        internal void RestoreIntegrationCount(int integrationCount) =>
            IntegrationCount = Mathf.Max(0, integrationCount);

        public static bool IntegrateClassified(ref KernelState state,
            MerkabaObservationKind kind, float quality, Color32 color) =>
            state.Apply(kind, quality, color);
    }
}
