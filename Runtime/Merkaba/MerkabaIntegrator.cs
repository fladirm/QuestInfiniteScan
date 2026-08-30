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
        private uint _attemptResidencyEpoch;

        private const int CameraEyeCount = 2;
        private const int CameraObservationSlots = 2;
        private readonly bool[] _cameraPairAvailable =
            new bool[CameraObservationSlots];
        private readonly Vector3[] _cameraPosition = new Vector3[4];
        private readonly Quaternion[] _cameraRotation = new Quaternion[4];
        private readonly Vector2[] _cameraFocalLength = new Vector2[4];
        private readonly Vector2[] _cameraPrincipalPoint = new Vector2[4];
        private readonly Vector2[] _cameraSensorResolution = new Vector2[4];
        private readonly Vector2[] _cameraCurrentResolution = new Vector2[4];
        private readonly uint[] _cameraSequence = new uint[4];
        private readonly double[] _cameraTimestampUnixSeconds = new double[4];
        private readonly double[] _cameraMaximumSkewSeconds =
            new double[CameraObservationSlots];
        private readonly RenderTexture[] _cameraFrameCopies = new RenderTexture[4];
        private int _readyCameraSlot = -1;
        private int _heldCameraSlot = -1;
        private bool _cameraObservationHeld;
        private ulong _cameraCopySubmittedEpoch;
        private ulong _cameraCopyRetiredEpoch;
        private int _lastCameraCopySlot = -1;
        private Task _cameraCopyRetirementTask = Task.CompletedTask;
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
                return slot >= 0
                    ? _cameraFrameCopies[CameraResourceIndex(slot, 0)] : null;
            }
        }
        internal bool CameraFrameAvailable
        {
            get
            {
                int slot = _readyCameraSlot >= 0
                    ? _readyCameraSlot : _heldCameraSlot;
                return slot >= 0 && _cameraPairAvailable[slot];
            }
        }
        internal bool HasReadyStereoCameraFrame =>
            _readyCameraSlot >= 0 && _cameraPairAvailable[_readyCameraSlot];
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
        private static readonly int[] CameraRgbId =
        {
            Shader.PropertyToID("_MerkabaCameraRgbLeft"),
            Shader.PropertyToID("_MerkabaCameraRgbRight")
        };
        private static readonly int[] CameraPositionId =
        {
            Shader.PropertyToID("_MerkabaCameraPositionLeft"),
            Shader.PropertyToID("_MerkabaCameraPositionRight")
        };
        private static readonly int[] CameraInverseRotationId =
        {
            Shader.PropertyToID("_MerkabaCameraInverseRotationLeft"),
            Shader.PropertyToID("_MerkabaCameraInverseRotationRight")
        };
        private static readonly int[] CameraFocalLengthId =
        {
            Shader.PropertyToID("_MerkabaCameraFocalLengthLeft"),
            Shader.PropertyToID("_MerkabaCameraFocalLengthRight")
        };
        private static readonly int[] CameraPrincipalPointId =
        {
            Shader.PropertyToID("_MerkabaCameraPrincipalPointLeft"),
            Shader.PropertyToID("_MerkabaCameraPrincipalPointRight")
        };
        private static readonly int[] CameraSensorResolutionId =
        {
            Shader.PropertyToID("_MerkabaCameraSensorResolutionLeft"),
            Shader.PropertyToID("_MerkabaCameraSensorResolutionRight")
        };
        private static readonly int[] CameraCurrentResolutionId =
        {
            Shader.PropertyToID("_MerkabaCameraCurrentResolutionLeft"),
            Shader.PropertyToID("_MerkabaCameraCurrentResolutionRight")
        };
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
            _initialized = false;
        }

        internal Action CaptureOwnedGpuResourceRelease()
        {
            UnityEngine.Object[] captured =
            {
                _cameraFrameCopies[0], _cameraFrameCopies[1],
                _cameraFrameCopies[2], _cameraFrameCopies[3]
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

        internal bool SetStereoCameraData(StereoCameraFrame frame)
        {
            if (!ReferenceEquals(_grid, null) &&
                _grid.GpuSubmissionSuspended) return false;
            if (!frame.IsValid) return false;
            int slot = _cameraObservationHeld && _heldCameraSlot >= 0
                ? 1 - _heldCameraSlot
                : _readyCameraSlot >= 0 ? _readyCameraSlot : 0;
            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba true-stereo PCA snapshot");
            try
            {
                StoreCameraEye(command, slot, 0, frame.Left);
                StoreCameraEye(command, slot, 1, frame.Right);
                // Provider-owned history copies, these immutable observation
                // copies, and later M8 work share the graphics queue. Queue
                // ordering publishes matching pixels and metadata without a
                // per-observation CPU fence or readback.
                Graphics.ExecuteCommandBuffer(command);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }

            _cameraPairAvailable[slot] = true;
            _cameraMaximumSkewSeconds[slot] = frame.MaximumSkewSeconds;
            _readyCameraSlot = slot;
            unchecked
            {
                _cameraCopySubmittedEpoch++;
                if (_cameraCopySubmittedEpoch == 0u)
                    _cameraCopySubmittedEpoch = 1u;
            }
            _lastCameraCopySlot = CameraResourceIndex(slot, 1);
            return true;
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
            Logger.Info("Merkaba observation attempt unresolved " +
                        $"observation={_observationToken} " +
                        $"attempt={_attemptToken} " +
                        $"attemptResidencyEpoch={_attemptResidencyEpoch} " +
                        $"currentResidencyEpoch={_grid.ResidencyEpoch}");
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
                    !_depthCapture.HasUnprocessedFrame ||
                    !HasReadyStereoCameraFrame)
                    return false;
            }
            else if (!CanRetryPreparedObservation())
                return false;

            _attemptResidencyEpoch = _grid.ResidencyEpoch;

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
                    bool consumed = _depthCapture.ConsumeLatestDepthFrame(
                        command, HeldStereoCameraFrame());
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
                            $"residencyEpoch={_attemptResidencyEpoch} " +
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
                   _grid.ResidencyEpoch != _attemptResidencyEpoch;
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
            _attemptResidencyEpoch = 0u;
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
            if (!_cameraObservationHeld || _heldCameraSlot < 0 ||
                !_cameraPairAvailable[_heldCameraSlot])
                throw new InvalidOperationException(
                    "M8 observation cannot bind an incomplete stereo PCA pair.");
            for (int eye = 0; eye < CameraEyeCount; eye++)
            {
                int resource = CameraResourceIndex(_heldCameraSlot, eye);
                compute.SetTexture(kernel, CameraRgbId[eye],
                    _cameraFrameCopies[resource]);
                compute.SetVector(CameraPositionId[eye],
                    _cameraPosition[resource]);
                compute.SetMatrix(CameraInverseRotationId[eye],
                    Matrix4x4.Rotate(_cameraRotation[resource]).inverse);
                compute.SetVector(CameraFocalLengthId[eye],
                    _cameraFocalLength[resource]);
                compute.SetVector(CameraPrincipalPointId[eye],
                    _cameraPrincipalPoint[resource]);
                compute.SetVector(CameraSensorResolutionId[eye],
                    _cameraSensorResolution[resource]);
                compute.SetVector(CameraCurrentResolutionId[eye],
                    _cameraCurrentResolution[resource]);
            }
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
            if (_readyCameraSlot < 0 ||
                !_cameraPairAvailable[_readyCameraSlot])
                throw new InvalidOperationException(
                    "A complete synchronized PCA pair is required.");
            _cameraObservationHeld = true;
            _heldCameraSlot = _readyCameraSlot;
            _readyCameraSlot = -1;
        }

        private void ReleaseOwnedObservation()
        {
            _depthCapture?.ReleaseConsumedObservation();
            if (_heldCameraSlot >= 0)
                _cameraPairAvailable[_heldCameraSlot] = false;
            _cameraObservationHeld = false;
            _heldCameraSlot = -1;
        }

        private void StoreCameraEye(CommandBuffer command, int slot, int eye,
            CameraFrameDescriptor frame)
        {
            if (frame.Eye != (StereoEye)eye)
                throw new ArgumentException("Stereo PCA eye mismatch.",
                    nameof(frame));
            int resource = CameraResourceIndex(slot, eye);
            int width = Mathf.Max(1, frame.Texture.width);
            int height = Mathf.Max(1, frame.Texture.height);
            RenderTexture owned = _cameraFrameCopies[resource];
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
                _cameraFrameCopies[resource] = owned;
            }
            command.BlitPcaObservationProfiled(frame.Texture, owned);
            _cameraPosition[resource] = frame.WorldPose.position;
            _cameraRotation[resource] = frame.WorldPose.rotation;
            _cameraFocalLength[resource] = frame.FocalLength;
            _cameraPrincipalPoint[resource] = frame.PrincipalPoint;
            _cameraSensorResolution[resource] = frame.SensorResolution;
            _cameraCurrentResolution[resource] = frame.CurrentResolution;
            _cameraSequence[resource] = frame.Sequence;
            _cameraTimestampUnixSeconds[resource] =
                frame.TimestampUnixSeconds;
        }

        private static int CameraResourceIndex(int slot, int eye) =>
            slot * CameraEyeCount + eye;

        private StereoCameraFrame HeldStereoCameraFrame()
        {
            if (!_cameraObservationHeld || _heldCameraSlot < 0 ||
                !_cameraPairAvailable[_heldCameraSlot])
                return default;
            CameraFrameDescriptor Frame(int eye)
            {
                int resource = CameraResourceIndex(_heldCameraSlot, eye);
                return new CameraFrameDescriptor(_cameraFrameCopies[resource],
                    new Pose(_cameraPosition[resource],
                        _cameraRotation[resource]),
                    _cameraFocalLength[resource],
                    _cameraPrincipalPoint[resource],
                    _cameraSensorResolution[resource],
                    _cameraCurrentResolution[resource],
                    _cameraTimestampUnixSeconds[resource],
                    _cameraSequence[resource], (StereoEye)eye);
            }
            return new StereoCameraFrame(Frame(0), Frame(1),
                _cameraMaximumSkewSeconds[_heldCameraSlot]);
        }

        internal void BeginObservationQuiesce()
        {
            if (_readyCameraSlot >= 0)
                _cameraPairAvailable[_readyCameraSlot] = false;
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
            _attemptResidencyEpoch = 0u;
            ReleaseOwnedObservation();
            if (_readyCameraSlot >= 0)
                _cameraPairAvailable[_readyCameraSlot] = false;
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
