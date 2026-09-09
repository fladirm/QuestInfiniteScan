using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>GPU-only frozen-observation Flower integration.</summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaIntegrator : MonoBehaviour
    {
        [SerializeField] private ComputeShader compute;
        [SerializeField] private ComputeShader observationBins;
        private MerkabaObservationBinsGpu _bins;
        [SerializeField, Range(1f, 8f)] private float maxUpdateDistance = 5f;

        private MerkabaGrid _grid;
        private RoomScanner _scanner;
        private DepthCapture _depthCapture;
        private MerkabaGridRenderer _pageRenderer;
        private int _pageOpportunityFrame = -1;
        private int _observationRequestedFrame = -1;
        private bool _observationRequestedIsFine;
        private int _flowerCommitKernel;
        private int _drainGeometryKernel;
        private int _advanceStageKernel;
        private int _resolveCarriersKernel;
        private int _drainSkinRgbKernel;
        private int _drainSkinVKernel;
        private int _updateObservationDualKernel;
        private int _finalizeKernel;
        private int _queryFineEraseKernel;
        private int _eraseFineTilesKernel;
        private int _finalizeFineEraseKernel;
        private bool _initialized;
        private bool _observationPrepared;
        private bool _reportedNonRigidFrame;
        private uint _observationToken;
        private Matrix4x4 _observationGridToWorld;
        private Matrix4x4 _observationWorldToGrid;
        private float _observationMaxUpdateDistance;
        private int _observationExclusionCount;
        private int _observationDepthVersion;
        private bool _attemptInFlight;
        private uint _attemptSequence;
        private uint _attemptToken;
        private MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob
            _nativeAttemptJob;
        private bool _nativeAttemptIncludesPreprocess;
        private bool _nativeAttemptCapturesRefineMetrics;
        private bool _nativeAttemptGpuComplete;
        private bool _nativeAttemptCompletionRequested;
        private uint _nativeAttemptDualGeneration;
        private double _nativeAttemptSubmittedAt;
        private double _nativeAttemptGpuCompleteAt;
        private bool _fineErasePrepared;
        private bool _fineEraseAttemptInFlight;
        private bool _fineEraseWaitingForDependency;
        private uint _fineEraseAttemptToken;
        private uint _fineEraseResidencyEpoch;
        private FineBrushDescriptor _fineEraseDescriptor;
        private MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob
            _nativeFineEraseJob;
        private bool _nativeFineEraseGpuComplete;
        private bool _nativeFineEraseCompletionRequested;
        private uint _nativeFineEraseDualGeneration;

        private const int CameraEyeCount = 2;
        private const int CameraObservationSlots = 2;
        // How much refinement ONE bounded snapshot contributes per tile. It is
        // not a continuation quantum: unconsumed candidates retain nothing, the
        // snapshot releases at its fence, and the tile's cursor stays in the
        // world for whichever snapshot comes next. It never limits admitted
        // detail, only how much of it a single frame pays for.
        private const int RefinementCandidatePassesPerQuantum = 64;
#if UNITY_EDITOR || !UNITY_ANDROID
        private static readonly uint[] FineEraseZero = { 0u };
        private static readonly uint[] FineEraseInitialArguments = { 0u, 1u, 1u };
#endif
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
        private readonly FineBrushDescriptor[] _cameraFineBrush =
            new FineBrushDescriptor[CameraObservationSlots];
        private readonly RenderTexture[] _cameraFrameCopies = new RenderTexture[4];
        private int _readyCameraSlot = -1;
        private int _heldCameraSlot = -1;
        private bool _cameraObservationHeld;
        private FineBrushDescriptor _heldFineBrush;
        private ulong _cameraCopySubmittedEpoch;
        private ulong _cameraCopyRetiredEpoch;
        private Task _cameraCopyRetirementTask = Task.CompletedTask;
        private readonly Vector4[] _exclusionPositions = new Vector4[64];
        private readonly Vector4[] _scanCoveragePlanes =
            new Vector4[MerkabaMutationCoverage.PlaneCount];

        public readonly List<Transform> ExclusionZones = new();
        public int IntegrationCount { get; private set; }
        public float MaxUpdateDistance => maxUpdateDistance;
        public bool HasPendingObservation => _observationPrepared;
        public bool HasAttemptInFlight => _attemptInFlight;
        internal bool HasPendingFineErase => _fineErasePrepared;
        internal bool HasFineEraseAttemptInFlight =>
            _fineEraseAttemptInFlight;
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
        // Update can request observation while a page job still owns the
        // lease; renderer polling retires it in LateUpdate. Do not immediately
        // replace that page job before the scanner's next admission turn.
        // The explicit post-observation page opportunity remains one quantum.
        internal bool ObservationHasBoundaryPriority =>
            _observationRequestedFrame == Time.frameCount &&
            (_observationRequestedIsFine || _pageOpportunityFrame != Time.frameCount);
        public event Action Integrated;
        internal event Action AuthorityChanged;
        internal event Action FineErased;

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
        private static readonly int AttemptTokenId =
            Shader.PropertyToID("_M8AttemptToken");
        private static readonly int FineRefineActiveId =
            Shader.PropertyToID("_M8FineRefineActive");
        private static readonly int FineCursorPositionId =
            Shader.PropertyToID("_M8FineCursorPosition");
        private static readonly int FineBrushAxisId =
            Shader.PropertyToID("_M8FineBrushAxis");
        private static readonly int FineRadiusSquaredId =
            Shader.PropertyToID("_M8FineRadiusSquared");
        private static readonly int FineLengthId =
            Shader.PropertyToID("_M8FineLength");
        private static readonly int ScanCenterBlockId =
            Shader.PropertyToID("_M8ScanCenterBlock");
        private static readonly int ScanBlockRadiusId =
            Shader.PropertyToID("_M8ScanBlockRadius");
        private static readonly int ScanBlockSideId =
            Shader.PropertyToID("_M8ScanBlockSide");

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _scanner = GetComponent<RoomScanner>();
            _depthCapture = GetComponent<DepthCapture>();
            _pageRenderer = GetComponent<MerkabaGridRenderer>();
        }

        internal void ReleaseOwnedResourcesAfterGpuRetirement()
        {
            for (int slot = 0; slot < _cameraFrameCopies.Length; slot++)
            {
                if (_cameraFrameCopies[slot] != null)
                    Destroy(_cameraFrameCopies[slot]);
                _cameraFrameCopies[slot] = null;
            }
            if (_bins != null)
            {
                _bins.EndAfterFinalization();
                _bins.Dispose();
                _bins = null;
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
            MerkabaObservationBinsGpu capturedBins = _bins;
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
                if (capturedBins != null)
                {
                    capturedBins.EndAfterFinalization();
                    capturedBins.Dispose();
                }
                foreach (UnityEngine.Object resource in captured)
                    if (resource != null) UnityEngine.Object.Destroy(resource);
            };
        }

        private bool Initialize()
        {
            if (_initialized) return true;
            if (compute == null || observationBins == null || _grid == null || _depthCapture == null)
                return false;
            _grid.EnsureGpuResources();
            _bins = new MerkabaObservationBinsGpu(observationBins, _grid,
                _grid.M8ObservationRecords);
            _flowerCommitKernel = compute.FindProfiledKernel(
                "FlowerCommit", MerkabaGpuStage.SurfaceIntegration);
            // Two entry points over one snapshot. Geometry serves the root, L1
            // and L2 barriers and skin serves the fourth; neither carries the
            // other's code. The barrier between them is a dispatch of its own,
            // because a workgroup cannot publish a stage its own sibling groups
            // may not have read yet.
            _drainGeometryKernel = compute.FindProfiledKernel(
                "DrainFlowerGeometry", MerkabaGpuStage.SurfaceIntegration);
            _advanceStageKernel = compute.FindProfiledKernel(
                "AdvanceRefinementStage", MerkabaGpuStage.SurfaceIntegration);
            // The resolver predicts the parent, applies the persisted
            // residual and classifies the carrier exactly once; the two
            // signal passes consume its receipt and repeat none of it.
            _resolveCarriersKernel = compute.FindProfiledKernel(
                "ResolveFlowerCarriers", MerkabaGpuStage.SurfaceIntegration);
            _drainSkinRgbKernel = compute.FindProfiledKernel(
                "DrainFlowerSkinRgb", MerkabaGpuStage.SurfaceIntegration);
            _drainSkinVKernel = compute.FindProfiledKernel(
                "DrainFlowerSkinV", MerkabaGpuStage.SurfaceIntegration);
            _updateObservationDualKernel = compute.FindProfiledKernel(
                "UpdateObservationDual", MerkabaGpuStage.DualIntegration);
            _finalizeKernel = compute.FindProfiledKernel(
                "FinalizeObservation", MerkabaGpuStage.SurfaceIntegration);
            _queryFineEraseKernel = compute.FindProfiledKernel(
                "QueryFineEraseTiles", MerkabaGpuStage.WorldQuery);
            _eraseFineTilesKernel = compute.FindProfiledKernel(
                "EraseFineTiles", MerkabaGpuStage.SurfaceIntegration);
            _finalizeFineEraseKernel = compute.FindProfiledKernel(
                "FinalizeFineErase", MerkabaGpuStage.SurfaceIntegration);
            foreach (int kernel in new[]
                     {
                         _flowerCommitKernel, _drainGeometryKernel,
                         _advanceStageKernel, _resolveCarriersKernel,
                         _drainSkinRgbKernel, _drainSkinVKernel,
                         _updateObservationDualKernel,
                         _finalizeKernel, _queryFineEraseKernel,
                         _eraseFineTilesKernel, _finalizeFineEraseKernel
                     })
            {
                _grid.BindWorldBuffers(compute, kernel);
                BindWorkBuffers(kernel);
            }
            compute.SetBuffer(_finalizeKernel, "_M8AttemptCompletion",
                _grid.M8AttemptCompletion);
            compute.SetBuffer(_finalizeFineEraseKernel,
                "_M8AttemptCompletion", _grid.M8AttemptCompletion);
            _initialized = true;
            return true;
        }

        private void BindWorkBuffers(int kernel)
        {
            compute.SetBuffer(kernel, "_M8TouchedTileQueue",
                _grid.M8TouchedTileQueue);
            compute.SetBuffer(kernel, "_M8TouchedTileQueueRead",
                _grid.M8TouchedTileQueue);
            compute.SetBuffer(kernel, "_M8ObservationDispatchArgs",
                _grid.M8ObservationDispatchArgs);
        }

        internal bool SetStereoCameraData(StereoCameraFrame frame)
        {
            return SetStereoCameraData(frame, default);
        }

        internal bool SetStereoCameraData(StereoCameraFrame frame,
            FineBrushDescriptor fineBrush)
        {
            if (_scanner != null && _scanner.ExportMutationHeld) return false;
            if (!ReferenceEquals(_grid, null) &&
                _grid.GpuSubmissionSuspended) return false;
            if (!frame.IsValid) return false;
            int slot = _cameraObservationHeld && _heldCameraSlot >= 0
                ? 1 - _heldCameraSlot
                : _readyCameraSlot >= 0 ? _readyCameraSlot : 0;
            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba true-stereo PCA snapshot");
            bool submitted = false;
            uint timingRevision = unchecked(
                (uint)(_cameraCopySubmittedEpoch + 1UL));
            if (timingRevision == 0u) timingRevision = 1u;
            bool timedSubmission = false;
            try
            {
                timedSubmission = MerkabaGpuTimestamps.TryAcquire(
                    CaptureOwner.PcaObservationCopy, timingRevision, command);
                StoreCameraEye(command, slot, 0, frame.Left, timedSubmission);
                StoreCameraEye(command, slot, 1, frame.Right, timedSubmission);
                // Provider-owned history copies, these immutable observation
                // copies, and later M8 work share the graphics queue. Queue
                // ordering publishes matching pixels and metadata without a
                // per-observation CPU fence or readback.
                MerkabaGpuTimestamps.End(CaptureOwner.PcaObservationCopy,
                    command, timedSubmission);
                Graphics.ExecuteCommandBuffer(command);
                submitted = true;
            }
            finally
            {
                MerkabaGpuTimestamps.Complete(CaptureOwner.PcaObservationCopy,
                    timedSubmission, submitted);
                CommandBufferPool.Release(command);
            }

            _cameraPairAvailable[slot] = true;
            _cameraMaximumSkewSeconds[slot] = frame.MaximumSkewSeconds;
            _cameraFineBrush[slot] = fineBrush;
            _readyCameraSlot = slot;
            unchecked
            {
                _cameraCopySubmittedEpoch++;
                if (_cameraCopySubmittedEpoch == 0u)
                    _cameraCopySubmittedEpoch = 1u;
            }
            return true;
        }

        /// <summary>
        /// Changes observation authority only at a transaction boundary.
        /// An already prepared/in-flight observation is immutable and must
        /// retire first. An unsubmitted stereo pair is discarded rather than
        /// being reinterpreted under the other authority.
        /// </summary>
        internal bool TrySwitchObservationAuthority()
        {
            if (_scanner != null && _scanner.ExportMutationHeld) return false;
            if (_observationPrepared || _attemptInFlight ||
                _cameraObservationHeld || _fineErasePrepared ||
                _fineEraseAttemptInFlight)
                return false;
            if (_readyCameraSlot >= 0)
            {
                _cameraPairAvailable[_readyCameraSlot] = false;
                _cameraFineBrush[_readyCameraSlot] = default;
                _readyCameraSlot = -1;
            }
            return true;
        }

        internal bool TryRetireObservationAttempt()
        {
            if (!_observationPrepared || !_attemptInFlight)
                return false;

            if (_nativeAttemptJob != null)
            {
                if (!_nativeAttemptGpuComplete)
                {
                    if (!_nativeAttemptJob.Poll(out string nativeError))
                        return false;
                    _grid.CompleteNativeDualMutation(_nativeAttemptDualGeneration,
                        string.IsNullOrEmpty(nativeError));
                    _nativeAttemptDualGeneration = 0u;
                    if (!string.IsNullOrEmpty(nativeError))
                    {
                        _nativeAttemptJob.Dispose();
                        _nativeAttemptJob = null;
                        _nativeAttemptIncludesPreprocess = false;
                        Logger.Error(nativeError);
                        _attemptInFlight = false;
                        _nativeAttemptCompletionRequested = false;
                        ReleaseOwnedObservation();
                        _observationPrepared = false;
                        return false;
                    }
                    if (_nativeAttemptIncludesPreprocess)
                        _depthCapture.CompleteNativeDepthPreprocess(
                            _nativeAttemptCapturesRefineMetrics, _observationToken,
                            _attemptToken, _observationDepthVersion);
                    _nativeAttemptIncludesPreprocess = false;
                    _nativeAttemptGpuComplete = true;
                    _nativeAttemptGpuCompleteAt = Time.realtimeSinceStartupAsDouble;
                }
                if (!_nativeAttemptCompletionRequested)
                {
                    _grid.RequestAttemptCompletion(_attemptToken);
                    _nativeAttemptCompletionRequested = true;
                    return false;
                }
            }

            if (_grid.CompletedAttemptToken != _attemptToken)
                return false;

            _nativeAttemptJob?.Dispose();
            _nativeAttemptJob = null;
            _nativeAttemptGpuComplete = false;
            double nativeAttemptRetiredAt = Time.realtimeSinceStartupAsDouble;
            MerkabaNativeVulkanExecutor.ObserveHeldPublication(_observationToken,
                _grid.CompletedObservationToken == _observationToken,
                _nativeAttemptGpuCompleteAt > 0.0
                    ? (nativeAttemptRetiredAt - _nativeAttemptGpuCompleteAt) * 1000.0 : 0.0);
            Logger.Info("Merkaba native observation publication " +
                $"attempt={_attemptToken} " +
                $"totalMs={(nativeAttemptRetiredAt - _nativeAttemptSubmittedAt) * 1000.0:F3} " +
                $"completionReadbackMs={(nativeAttemptRetiredAt - _nativeAttemptGpuCompleteAt) * 1000.0:F3}");
            _attemptInFlight = false;
            _nativeAttemptCompletionRequested = false;
            // Leave this frame's post-view queue boundary available to one
            // bounded page quantum. Retrying in Update immediately would
            // monopolize the lease for the entire immutable observation.
            // The held evidence/workset is unchanged; ERASE remains first.
            _pageOpportunityFrame = Time.frameCount;
            // Certified dual progress is already published at this fence.
            // Notify its consumers even when more resident work is pending
            // or the observation cannot finish. This is not a new observation.
            if (_grid.CompletedObservationChangedReadout)
                AuthorityChanged?.Invoke();
            if (_grid.CompletedObservationToken == _observationToken)
            {
                Logger.Info("Merkaba observation complete " +
                            $"observation={_observationToken} " +
                            $"attempt={_attemptToken} " +
                            $"depthVersion={_observationDepthVersion} " +
                            $"failure=0x{_grid.CompletedObservationFailure:x}");
                return FinishObservation(_grid.CompletedObservationFailure);
            }

            // FinalizeObservation publishes and releases unconditionally, so a
            // retired attempt whose token did not complete means the graph did
            // not reach its finalize dispatch. Release the snapshot rather than
            // holding it: the certified work is already published, and the next
            // snapshot observes a newer world with a newer camera.
            Logger.Error("Merkaba observation did not reach its finalize " +
                        $"dispatch; observation={_observationToken} " +
                        $"attempt={_attemptToken} " +
                        $"completed={_grid.CompletedObservationToken}");
            return FinishObservation(_grid.CompletedObservationFailure);
        }

        internal bool TryPrepareFineErase(FineBrushDescriptor descriptor)
        {
            if (_scanner != null && _scanner.ExportMutationHeld) return false;
            if (!descriptor.IsErase || _observationPrepared ||
                _attemptInFlight || _fineErasePrepared ||
                (_bins != null && _bins.FrozenObservation != 0u) ||
                _fineEraseAttemptInFlight || !Initialize())
                return false;
            // The completed observation's fused finalization retirement
            // precedes its completion fence. Merely retiring one retry is
            // insufficient: the frozen reservation still owns this queue.
            _fineEraseDescriptor = descriptor;
            _fineErasePrepared = true;
            _fineEraseWaitingForDependency = false;
            _fineEraseAttemptToken = 0u;
            _fineEraseResidencyEpoch = 0u;
            return true;
        }

        internal bool TryRetireFineEraseAttempt()
        {
            if (!_fineErasePrepared || !_fineEraseAttemptInFlight)
                return false;

            if (_nativeFineEraseJob != null)
            {
                if (!_nativeFineEraseGpuComplete)
                {
                    if (!_nativeFineEraseJob.Poll(out string nativeError))
                        return false;
                    _grid.CompleteNativeDualMutation(_nativeFineEraseDualGeneration,
                        string.IsNullOrEmpty(nativeError));
                    _nativeFineEraseDualGeneration = 0u;
                    if (!string.IsNullOrEmpty(nativeError))
                    {
                        _nativeFineEraseJob.Dispose();
                        _nativeFineEraseJob = null;
                        _nativeFineEraseGpuComplete = false;
                        _fineEraseAttemptInFlight = false;
                        Logger.Error(nativeError);
                        return false;
                    }
                    _nativeFineEraseGpuComplete = true;
                }
                if (!_nativeFineEraseCompletionRequested)
                {
                    _grid.RequestAttemptCompletion(_fineEraseAttemptToken);
                    _nativeFineEraseCompletionRequested = true;
                    return false;
                }
            }

            if (_grid.CompletedAttemptToken != _fineEraseAttemptToken)
                return false;

            _nativeFineEraseJob?.Dispose();
            _nativeFineEraseJob = null;
            _nativeFineEraseGpuComplete = false;
            _nativeFineEraseCompletionRequested = false;
            _fineEraseAttemptInFlight = false;
            if (_grid.CompletedObservationChangedReadout)
                AuthorityChanged?.Invoke();
            if (_grid.CompletedObservationToken == _fineEraseAttemptToken)
            {
                _fineErasePrepared = false;
                _fineEraseWaitingForDependency = false;
                _fineEraseAttemptToken = 0u;
                _fineEraseResidencyEpoch = 0u;
                _fineEraseDescriptor = default;
                FineErased?.Invoke();
                return true;
            }

            // Epoch-wrap contention can leave a finite suffix while other
            // owners committed in this quantum. Drain that real progress
            // against the held ERASE descriptor without requiring motion.
            _fineEraseWaitingForDependency = !_grid.CompletedRefinementProgress;
            return false;
        }

        internal bool TrySubmitFineEraseAttempt()
        {
            if (!_fineErasePrepared || _fineEraseAttemptInFlight ||
                _observationPrepared || _attemptInFlight ||
                (_bins != null && _bins.FrozenObservation != 0u) ||
                _grid == null || _grid.GpuSubmissionSuspended ||
                !Initialize()) return false;
            if (!_grid.ObservationMutationSubmissionAllowed) return false;
            if (_fineEraseWaitingForDependency &&
                _grid.ResidencyEpoch == _fineEraseResidencyEpoch)
                return false;

            _fineEraseResidencyEpoch = _grid.ResidencyEpoch;
            _fineEraseAttemptToken = NextAttemptToken();
#if !UNITY_EDITOR && UNITY_ANDROID
            return TrySubmitNativeFineEraseAttempt();
#else
            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba exact FINE erase");
            bool submitted = false;
            uint dualGeneration = 0u;
            try
            {
                dualGeneration = _grid.RecordDualMutation(command, compute);
                ConfigureFineErase(command, _fineEraseDescriptor);
                command.SetComputeIntParam(compute, AttemptTokenId,
                    unchecked((int)_fineEraseAttemptToken));
                // Same bounded setup as native vkCmdFillBuffer. These are
                // command-stream constant writes, never GPU-count readbacks.
                command.SetBufferData(_grid.M8Counters, FineEraseZero,
                    0, MerkabaGrid.CounterFineEraseTileCount, 1);
                command.SetBufferData(_grid.M8Counters, FineEraseZero,
                    0, MerkabaGrid.CounterUnresolvedObservationTiles, 1);
                command.SetBufferData(_grid.M8Counters, FineEraseZero,
                    0, MerkabaGrid.CounterObservationChangeMask, 1);
                command.SetBufferData(_grid.M8ObservationDispatchArgs, FineEraseInitialArguments, 0, 0, 3);
                DispatchFineEraseQuery(command, _fineEraseDescriptor);
                command.DispatchComputeProfiled(compute,
                    _eraseFineTilesKernel, _grid.M8ObservationDispatchArgs);
                command.DispatchComputeProfiled(compute,
                    _finalizeFineEraseKernel, 1, 1, 1);
                _grid.SubmitDualMutation(command, dualGeneration);
                submitted = true;
                _fineEraseAttemptInFlight = true;
                _fineEraseWaitingForDependency = false;
                _grid.RequestAttemptCompletion(_fineEraseAttemptToken);
                return true;
            }
            finally
            {
                if (!submitted) _grid.CancelDualMutationBeforeSubmit(dualGeneration);
                CommandBufferPool.Release(command);
                if (!submitted) _fineEraseAttemptToken = 0u;
            }
#endif
        }

#if !UNITY_EDITOR && UNITY_ANDROID
        private bool TrySubmitNativeFineEraseAttempt()
        {
            if (MerkabaNativeVulkanExecutor.HasJobInFlight)
                return false;
            Matrix4x4 worldToGrid = _grid.GridToWorldMatrix.inverse;
            float3 gridCenter = (float3)worldToGrid.MultiplyPoint3x4(
                _fineEraseDescriptor.BoundsCenter) /
                MerkabaConstants.LatticeStep;
            int3 centerBlock = MerkabaSpatial.Encode(
                (int3)math.floor(gridCenter)).BlockCoord;
            int radius = Mathf.CeilToInt(
                (_fineEraseDescriptor.BoundsRadius +
                 MerkabaConstants.HalfSupport) /
                MerkabaSpatial.BlockWorldSize) + 1;
            int side = radius * 2 + 1;
            int queryGroups = checked(side * side * side);
            var resources = new IntPtr[
                MerkabaNativeVulkanExecutor.ResourceCount];
            _grid.FillNativeExecutorWorldResources(resources);
            var uniforms = new MerkabaNativeUniformTable();
            uniforms.Matrix("_MerkabaGridToWorld",
                _grid.GridToWorldMatrix);
            uniforms.Matrix("_MerkabaWorldToGrid", worldToGrid);
            uniforms.Vector3("_M8FineCursorPosition",
                _fineEraseDescriptor.CursorPosition);
            uniforms.Vector3("_M8FineBrushAxis",
                _fineEraseDescriptor.Axis);
            uniforms.Float("_M8FineRadiusSquared",
                _fineEraseDescriptor.Radius * _fineEraseDescriptor.Radius);
            uniforms.Float("_M8FineLength",
                _fineEraseDescriptor.Length);
            uniforms.Int3("_M8ScanCenterBlock", centerBlock.x,
                centerBlock.y, centerBlock.z);
            uniforms.Int("_M8ScanBlockRadius", radius);
            uniforms.Int("_M8ScanBlockSide", side);
            uniforms.UInt("_M8AttemptToken", _fineEraseAttemptToken);
            _nativeFineEraseDualGeneration = _grid.BeginNativeDualMutation(uniforms);
            MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob nativeJob;
            bool created;
            try
            {
                created = MerkabaNativeVulkanExecutor.TryCreateJob(
                    MerkabaNativeVulkanExecutor.JobKind.FineErase,
                    _fineEraseAttemptToken, resources, uniforms, 0, 0,
                    queryGroups, 0, out nativeJob);
            }
            catch
            {
                _grid.CancelDualMutationBeforeSubmit(_nativeFineEraseDualGeneration);
                _nativeFineEraseDualGeneration = 0u;
                throw;
            }
            if (!created)
            {
                _grid.CancelDualMutationBeforeSubmit(_nativeFineEraseDualGeneration);
                _nativeFineEraseDualGeneration = 0u;
                return false;
            }

            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba native FINE erase submit");
            bool recorded = false;
            try
            {
                nativeJob.RecordPrepareAndSubmit(command);
                recorded = true;
                Graphics.ExecuteCommandBuffer(command);
                _nativeFineEraseJob = nativeJob;
                _nativeFineEraseGpuComplete = false;
                _nativeFineEraseCompletionRequested = false;
                _fineEraseAttemptInFlight = true;
                _fineEraseWaitingForDependency = false;
                return true;
            }
            catch (Exception exception)
            {
                if (recorded)
                {
                    _nativeFineEraseJob = nativeJob;
                    _nativeFineEraseGpuComplete = false;
                    _nativeFineEraseCompletionRequested = false;
                    _fineEraseAttemptInFlight = true;
                    Logger.Error("Merkaba native FINE erase submission " +
                        "became uncertain; resources remain quarantined: " +
                        exception.Message);
                    return true;
                }
                nativeJob.CancelBeforeExecution();
                nativeJob.Dispose();
                _grid.CancelDualMutationBeforeSubmit(_nativeFineEraseDualGeneration);
                _nativeFineEraseDualGeneration = 0u;
                _fineEraseAttemptToken = 0u;
                return false;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }
#endif

        internal bool TrySubmitObservationAttempt()
        {
            if (_grid == null || _grid.GpuSubmissionSuspended ||
                !Initialize() || _attemptInFlight || _fineErasePrepared ||
                _fineEraseAttemptInFlight)
                return false;
            // A snapshot is acquired, submitted as one bounded transaction and
            // released at its fence. There is nothing to resume: while one is
            // prepared, the only thing that may happen to it is retirement.
            if (_observationPrepared) return false;
            if (_scanner != null && _scanner.ExportMutationHeld) return false;
            bool fineRequest = _readyCameraSlot >= 0 &&
                _cameraFineBrush[_readyCameraSlot].IsRefine;
            if (!fineRequest && _pageOpportunityFrame == Time.frameCount &&
                _pageRenderer != null && _pageRenderer.isActiveAndEnabled &&
                _pageRenderer.ReadoutDrawEnabled)
                return false;
            if (!DepthCapture.DepthAvailable ||
                !_depthCapture.HasUnprocessedFrame ||
                !HasReadyStereoCameraFrame)
                return false;
            // Calibration intervals and the 25 mm lattice are metric.
            // Reject a scaled/sheared scene hierarchy before either eye
            // or a depth observation is leased; do not silently rescale
            // measured errors or reinterpret the canonical scan space.
            if (!HasRigidObservationFrame()) return false;

            // Record an actual logically eligible request before the lease
            // gate: it may still be held by a page job that retires later in
            // this frame. This is scheduling state, not observation evidence.
            _observationRequestedFrame = Time.frameCount;
            _observationRequestedIsFine = fineRequest;
            if (!_grid.ObservationMutationSubmissionAllowed) return false;

#if !UNITY_EDITOR && UNITY_ANDROID
            if (!MerkabaNativeVulkanExecutor.IsAvailable)
            {
                // Availability owns startup/failure logging. A driver compile
                // in progress is not an error to spam once per XR frame.
                return false;
            }
            return TrySubmitNativeObservationAttempt();
#else

            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba M8 observation");
            bool submitted = false;
            bool timedSubmission = false;
            uint dualGeneration = 0u;
            try
            {
                timedSubmission = MerkabaGpuTimestamps.TryAcquire(
                    CaptureOwner.Observation,
                    unchecked((uint)Math.Max(1, IntegrationCount + 1)),
                    command);
                {
                    AcquireCameraObservation();
                    bool consumed = _depthCapture.ConsumeLatestDepthFrame(
                        command, HeldStereoCameraFrame(), _heldFineBrush,
                        _observationGridToWorld);
                    if (!consumed || _depthCapture.DepthTex == null ||
                        _depthCapture.NormTex == null ||
                        _depthCapture.DepthCertificate == null)
                    {
                        ReleaseOwnedObservation();
                        return false;
                    }
                    _observationToken =
                        _grid.AllocateObservationToken();
                    _observationDepthVersion =
                        _depthCapture.ProcessedRawFrameVersion;
                    _observationPrepared = true;
                    BeginObservationBins();
                    _depthCapture.RecordDepthCertificate(command, _bins);
                }

                _attemptToken = NextAttemptToken();
                dualGeneration = _grid.RecordDualMutation(command, compute);
                command.SetComputeIntParam(compute, AttemptTokenId,
                    unchecked((int)_attemptToken));
                // FINE/ERASE may have used this shader since the last
                // snapshot. Bind this snapshot's own observation.
                ConfigureObservation();
                _bins.Record(command, reset: false);
                // All required resident negative support is resolved before
                // the one touched-tile workgroup can commit direct evidence.
                DispatchObservationDual(command);
                _bins.RecordTouchedPublication(command);
                _bins.RecordCommit(command, compute, _flowerCommitKernel);
                // root, L1 and L2 are three dependency barriers of THIS
                // snapshot, each followed by its own one-group stage advance.
                // No camera acquisition and no readback between them.
                for (int barrier = 0; barrier < 3; barrier++)
                {
                    _bins.RecordCommit(command, compute, _drainGeometryKernel);
                    command.DispatchComputeProfiled(compute, _advanceStageKernel,
                        1, 1, 1);
                }
                // Resolve, then RGB, then V. Each returns immediately outside
                // its own stage, and only V advances the shared cursor.
                _bins.RecordCommit(command, compute, _resolveCarriersKernel);
                _bins.RecordCommit(command, compute, _drainSkinRgbKernel);
                _bins.RecordCommit(command, compute, _drainSkinVKernel);
                // Negative-volume claims are storage dependencies of this
                // same frozen observation. Publish after commit: installation
                // reuses its indirect argument buffer for tile counts.
                _bins.RecordTileRequestPublication(command);
                command.SetComputeBufferParam(compute, _finalizeKernel,
                    "_M8ObservationTileBins", _bins.TileBins);
                command.DispatchComputeProfiled(compute, _finalizeKernel,
                    1, 1, 1);

                MerkabaGpuTimestamps.End(CaptureOwner.Observation, command,
                    timedSubmission);
                _grid.SubmitDualMutation(command, dualGeneration);
                submitted = true;
                if (timedSubmission)
                    MerkabaGpuTimestamps.CaptureM8Metrics(_grid);
                _attemptInFlight = true;
                _grid.RequestAttemptCompletion(_attemptToken);
                Logger.Info("Merkaba observation submitted " +
                            $"observation={_observationToken} " +
                            $"attempt={_attemptToken} " +
                            $"depthVersion={_observationDepthVersion}");
                return true;
            }
            finally
            {
                if (!submitted) _grid.CancelDualMutationBeforeSubmit(dualGeneration);
                MerkabaGpuTimestamps.Complete(CaptureOwner.Observation,
                    timedSubmission, submitted);
                CommandBufferPool.Release(command);
            }
#endif
        }

#if !UNITY_EDITOR && UNITY_ANDROID
        private bool TrySubmitNativeObservationAttempt()
        {
            if (MerkabaNativeVulkanExecutor.HasJobInFlight) return false;
            {
                AcquireCameraObservation();
                if (!_depthCapture.PrepareLatestDepthFrameForNative(
                        HeldStereoCameraFrame()))
                {
                    ReleaseOwnedObservation();
                    return false;
                }
                _observationToken = _grid.AllocateObservationToken();
                _observationDepthVersion =
                    _depthCapture.ProcessedRawFrameVersion;
                _observationPrepared = true;
                BeginObservationBins();
            }

            _attemptToken = NextAttemptToken();
            IntPtr[] resources = BuildNativeObservationResources();
            MerkabaNativeUniformTable uniforms =
                BuildNativeObservationUniforms(out int queryGroups);
            int depthGroupsX = Mathf.CeilToInt(_depthCapture.DepthTex.width / 8f);
            int depthGroupsY = Mathf.CeilToInt(_depthCapture.DepthTex.height / 8f);
            _nativeAttemptDualGeneration = _grid.BeginNativeDualMutation(uniforms);
            MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob nativeJob;
            bool created;
            // The dual belongs to the observation token. One kind: acquire the
            // evidence, refine against it, publish, release.
            const MerkabaNativeVulkanExecutor.JobKind kind =
                MerkabaNativeVulkanExecutor.JobKind.Observation;
            try
            {
                created = MerkabaNativeVulkanExecutor.TryCreateJob(kind,
                    _attemptToken, resources, uniforms, depthGroupsX,
                    depthGroupsY, queryGroups, 0, out nativeJob);
            }
            catch
            {
                _grid.CancelDualMutationBeforeSubmit(_nativeAttemptDualGeneration);
                _nativeAttemptDualGeneration = 0u;
                throw;
            }
            if (!created)
            {
                _grid.CancelDualMutationBeforeSubmit(_nativeAttemptDualGeneration);
                _nativeAttemptDualGeneration = 0u;
                ReleaseOwnedObservation();
                _observationPrepared = false;
                return false;
            }

            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba native scanner submit");
            bool recorded = false;
            try
            {
                nativeJob.RecordPrepareAndSubmit(command);
                recorded = true;
                Graphics.ExecuteCommandBuffer(command);
                _nativeAttemptJob = nativeJob;
                _nativeAttemptIncludesPreprocess = true;
                _nativeAttemptGpuComplete = false;
                _nativeAttemptCompletionRequested = false;
                _nativeAttemptSubmittedAt = Time.realtimeSinceStartupAsDouble;
                _nativeAttemptGpuCompleteAt = 0.0;
                _attemptInFlight = true;
                Logger.Info("Merkaba native observation submitted " +
                    $"observation={_observationToken} " +
                    $"attempt={_attemptToken} " +
                    $"kind={kind} queue=1");
                return true;
            }
            catch (Exception exception)
            {
                if (recorded)
                {
                    // Execution is uncertain after Unity accepted plugin events.
                    // Retain every lease and native object until its fence becomes
                    // terminal; never recycle sensor/world resources here.
                    _nativeAttemptJob = nativeJob;
                    _nativeAttemptIncludesPreprocess = true;
                    _nativeAttemptGpuComplete = false;
                    _nativeAttemptSubmittedAt = Time.realtimeSinceStartupAsDouble;
                    _nativeAttemptGpuCompleteAt = 0.0;
                    _attemptInFlight = true;
                    Logger.Error("Merkaba native submit became uncertain; " +
                        $"resources quarantined until terminal fence: " +
                        exception.Message);
                    return true;
                }
                nativeJob.CancelBeforeExecution();
                nativeJob.Dispose();
                _grid.CancelDualMutationBeforeSubmit(_nativeAttemptDualGeneration);
                _nativeAttemptDualGeneration = 0u;
                ReleaseOwnedObservation();
                _observationPrepared = false;
                return false;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private IntPtr[] BuildNativeObservationResources()
        {
            var resources = new IntPtr[
                MerkabaNativeVulkanExecutor.ResourceCount];
            _grid.FillNativeExecutorWorldResources(resources);
            _depthCapture.FillNativeExecutorDepthResources(resources);
            _bins.FillNativeResources(resources);
            resources[(int)MerkabaNativeVulkanExecutor.Resource.CameraLeft] =
                _cameraFrameCopies[CameraResourceIndex(_heldCameraSlot, 0)]
                    .GetNativeTexturePtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource.CameraRight] =
                _cameraFrameCopies[CameraResourceIndex(_heldCameraSlot, 1)]
                    .GetNativeTexturePtr();
            return resources;
        }

        private MerkabaNativeUniformTable BuildNativeObservationUniforms(
            out int queryGroups)
        {
            var values = new MerkabaNativeUniformTable();
            int width = _depthCapture.DepthTex.width;
            int height = _depthCapture.DepthTex.height;
            values.UInt("_DepthW", checked((uint)width));
            values.UInt("_DepthH", checked((uint)height));
            values.Matrices("_DepthProj", _depthCapture.Proj);
            // _DepthProjInv belongs to the held observation's frozen set and is
            // written by WriteDepthCertificateUniforms below. Writing it here
            // too made the uniform table reject the whole observation with
            // "Duplicate native uniform value", so the frame stayed held and
            // never became ready: the scan reported active and produced nothing.
            values.Matrices("_DepthView", _depthCapture.View);
            values.Matrices("_DepthViewInv", _depthCapture.ViewInv);
            _nativeAttemptCapturesRefineMetrics =
                MerkabaGpuTimestamps.ShouldCaptureNativeRefineMetrics;
            values.UInt("_RefineMetricsEnabled", _nativeAttemptCapturesRefineMetrics ? 1u : 0u);
            values.UInt("_RefineMetricGroupsX",
                checked((uint)Mathf.CeilToInt(width / 8f)));
            values.UInt2("gsDepthTexSize", width, height);
            values.Matrices("gsDepthProj", _depthCapture.Proj);
            values.Matrices("gsDepthProjInv", _depthCapture.ProjInv);
            values.Matrices("gsDepthView", _depthCapture.View);
            values.Matrices("gsDepthViewInv", _depthCapture.ViewInv);
            _depthCapture.WriteDepthCertificateUniforms(values);
            values.UInt("_M8ObservationToken", _observationToken);
            values.UInt("_M8ObservationHotSlotCount", MerkabaSpatial.PhysicalTileCapacity);
            values.UInt("_M8ObservationRecordCapacity", MerkabaObservationRecord.Capacity);
            values.UInt("_M8AttemptToken", _attemptToken);
            values.UInt("_M8RefinementQuantum", RefinementCandidatePassesPerQuantum);
            Matrix4x4 gridToWorld = _observationGridToWorld;
            Matrix4x4 worldToGrid = _observationWorldToGrid;
            values.Matrix("_MerkabaGridToWorld", gridToWorld);
            values.Matrix("_MerkabaWorldToGrid", worldToGrid);
            values.Float("_MerkabaMaxUpdateDistance",
                _observationMaxUpdateDistance);
            values.UInt("_M8FineRefineActive",
                _heldFineBrush.IsRefine ? 1u : 0u);
            values.Vector3("_M8FineCursorPosition",
                _heldFineBrush.CursorPosition);
            values.Vector3("_M8FineBrushAxis", _heldFineBrush.Axis);
            values.Float("_M8FineRadiusSquared",
                _heldFineBrush.Radius * _heldFineBrush.Radius);
            values.Float("_M8FineLength", _heldFineBrush.Length);

            for (int eye = 0; eye < CameraEyeCount; ++eye)
            {
                int resource = CameraResourceIndex(_heldCameraSlot, eye);
                string suffix = eye == 0 ? "Left" : "Right";
                values.Vector3("_MerkabaCameraPosition" + suffix,
                    _cameraPosition[resource]);
                values.Matrix("_MerkabaCameraInverseRotation" + suffix,
                    Matrix4x4.Rotate(_cameraRotation[resource]).inverse);
                values.Vector2("_MerkabaCameraFocalLength" + suffix,
                    _cameraFocalLength[resource]);
                values.Vector2("_MerkabaCameraPrincipalPoint" + suffix,
                    _cameraPrincipalPoint[resource]);
                values.Vector2("_MerkabaCameraSensorResolution" + suffix,
                    _cameraSensorResolution[resource]);
                values.Vector2("_MerkabaCameraCurrentResolution" + suffix,
                    _cameraCurrentResolution[resource]);
            }

            values.Int("_MerkabaExclusionCount", _observationExclusionCount);
            values.Vector3Array("_MerkabaExclusionHeads",
                _exclusionPositions);

            Vector3 leftOrigin = _depthCapture.ViewInv[0].GetColumn(3);
            Vector3 rightOrigin = _depthCapture.ViewInv[1].GetColumn(3);
            Vector3 observationOrigin = (leftOrigin + rightOrigin) * 0.5f;
            float3 gridCamera = (float3)worldToGrid.MultiplyPoint3x4(
                observationOrigin) / MerkabaConstants.LatticeStep;
            int3 globalKernel = (int3)math.floor(gridCamera);
            int3 centerBlock = MerkabaSpatial.Encode(globalKernel).BlockCoord;
            int radius = Mathf.CeilToInt(_observationMaxUpdateDistance /
                MerkabaSpatial.BlockWorldSize) + 1;
            int side = radius * 2 + 1;
            queryGroups = checked(side * side * side);
            values.Vector3("_M8ScanCameraWorld", observationOrigin);
            values.Int3("_M8ScanCenterBlock", centerBlock.x,
                centerBlock.y, centerBlock.z);
            values.Int("_M8ScanBlockRadius", radius);
            values.Int("_M8ScanBlockSide", side);
            MerkabaMutationCoverage.WriteGridPlanes(_depthCapture.View,
                _depthCapture.Proj, gridToWorld, _scanCoveragePlanes,
                _heldFineBrush.IsRefine ? 1f :
                    MerkabaConstants.MutationOuterRadius);
            values.Vector4Array("_M8ScanCoveragePlanes",
                _scanCoveragePlanes);
            return values;
        }
#endif

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
            MerkabaNativeVulkanExecutor.EndHeldObservationTiming(_observationToken,
                failureReason == 0u, failureReason);
            _observationPrepared = false;
            _observationToken = 0u;
            _observationDepthVersion = 0;
            _attemptInFlight = false;
            _attemptToken = 0u;
            ReleaseOwnedObservation();
            if (failureReason != 0u)
            {
                Logger.Error("Merkaba observation stopped; published certified " +
                             $"changes retained; failure=0x{failureReason:x}");
                return false;
            }
            IntegrationCount++;
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

        internal async Task FinishCurrentFineEraseAsync()
        {
            while (_fineErasePrepared)
            {
                TryRetireFineEraseAttempt();
                if (!_fineErasePrepared) break;
                if (!_fineEraseAttemptInFlight)
                    TrySubmitFineEraseAttempt();
                if (_fineErasePrepared)
                {
                    _grid?.PumpStorageForLifecycleRetirement();
                    await Task.Yield();
                }
            }
        }

        private void BeginObservationBins()
        {
            _bins.Begin(_observationToken, _depthCapture.DepthTex, _depthCapture.NormTex,
                _depthCapture.ProjInv[0], _depthCapture.ViewInv[0], _observationWorldToGrid,
                _observationMaxUpdateDistance, _observationExclusionCount,
                _exclusionPositions, _heldFineBrush);
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
            compute.SetMatrix(GridToWorldId, _observationGridToWorld);
            compute.SetMatrix(WorldToGridId, _observationWorldToGrid);
            compute.SetFloat(MaxDistanceId, _observationMaxUpdateDistance);
            MerkabaMutationCoverage.WriteGridPlanes(_depthCapture.View,
                _depthCapture.Proj, _observationGridToWorld,
                _scanCoveragePlanes, _heldFineBrush.IsRefine
                    ? 1f : MerkabaConstants.MutationOuterRadius);
            compute.SetVectorArray("_M8ScanCoveragePlanes",
                _scanCoveragePlanes);
            compute.SetInt(FineRefineActiveId,
                _heldFineBrush.IsRefine ? 1 : 0);
            compute.SetVector(FineCursorPositionId,
                _heldFineBrush.CursorPosition);
            compute.SetVector(FineBrushAxisId, _heldFineBrush.Axis);
            compute.SetFloat(FineRadiusSquaredId,
                _heldFineBrush.Radius * _heldFineBrush.Radius);
            compute.SetFloat(FineLengthId, _heldFineBrush.Length);
            compute.SetInt(ExclusionCountId, _observationExclusionCount);
            compute.SetVectorArray(ExclusionHeadsId, _exclusionPositions);

            BindDepth(_updateObservationDualKernel);
            BindDepth(_flowerCommitKernel);
            BindCamera(_flowerCommitKernel);
            BindDepth(_drainGeometryKernel);
            BindCamera(_drainGeometryKernel);
            BindDepth(_resolveCarriersKernel);
            BindCamera(_resolveCarriersKernel);
            BindDepth(_drainSkinRgbKernel);
            BindCamera(_drainSkinRgbKernel);
            BindDepth(_drainSkinVKernel);
            BindCamera(_drainSkinVKernel);
            compute.SetInt("_M8RefinementQuantum", RefinementCandidatePassesPerQuantum);
        }

        private void BindDepth(int kernel)
        {
            compute.SetTexture(kernel, DepthCapture.DepthTexID,
                _depthCapture.DepthTex);
            compute.SetTexture(kernel, DepthCapture.NormTexID,
                _depthCapture.NormTex);
            _depthCapture.BindDepthCertificate(compute, kernel);
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

        private void DispatchObservationDual(CommandBuffer command)
        {
            _bins.RecordBindConsumer(command, compute, _updateObservationDualKernel);
            Vector3 leftOrigin = _depthCapture.ViewInv[0].GetColumn(3);
            Vector3 rightOrigin = _depthCapture.ViewInv[1].GetColumn(3);
            Vector3 observationOrigin = (leftOrigin + rightOrigin) * 0.5f;
            Matrix4x4 worldToGrid = _observationWorldToGrid;
            float3 gridCamera = (float3)worldToGrid.MultiplyPoint3x4(
                observationOrigin) / MerkabaConstants.LatticeStep;
            int3 globalKernel = (int3)math.floor(gridCamera);
            int3 centerBlock = MerkabaSpatial.Encode(globalKernel).BlockCoord;
            int radius = Mathf.CeilToInt(_observationMaxUpdateDistance /
                MerkabaSpatial.BlockWorldSize) + 1;
            int side = radius * 2 + 1;
            _bins.SetDualStorageDomain(command, centerBlock, radius, side);
            compute.SetInts("_M8ScanCenterBlock", centerBlock.x,
                centerBlock.y, centerBlock.z);
            compute.SetInt("_M8ScanBlockRadius", radius);
            compute.SetInt("_M8ScanBlockSide", side);
            compute.SetVector("_M8ScanCameraWorld", observationOrigin);

            command.DispatchComputeProfiled(compute, _updateObservationDualKernel,
                side * side * side, 1, 1);
        }

        private void ConfigureFineErase(CommandBuffer command,
            FineBrushDescriptor descriptor)
        {
            command.SetComputeMatrixParam(compute, GridToWorldId,
                _grid.GridToWorldMatrix);
            command.SetComputeMatrixParam(compute, WorldToGridId,
                _grid.GridToWorldMatrix.inverse);
            command.SetComputeVectorParam(compute, FineCursorPositionId,
                descriptor.CursorPosition);
            command.SetComputeVectorParam(compute, FineBrushAxisId,
                descriptor.Axis);
            command.SetComputeFloatParam(compute, FineRadiusSquaredId,
                descriptor.Radius * descriptor.Radius);
            command.SetComputeFloatParam(compute, FineLengthId,
                descriptor.Length);
        }

        private void DispatchFineEraseQuery(CommandBuffer command,
            FineBrushDescriptor descriptor)
        {
            Matrix4x4 worldToGrid = _grid.GridToWorldMatrix.inverse;
            float3 gridCenter = (float3)worldToGrid.MultiplyPoint3x4(
                descriptor.BoundsCenter) / MerkabaConstants.LatticeStep;
            int3 centerBlock = MerkabaSpatial.Encode(
                (int3)math.floor(gridCenter)).BlockCoord;
            int radius = Mathf.CeilToInt(
                (descriptor.BoundsRadius + MerkabaConstants.HalfSupport) /
                MerkabaSpatial.BlockWorldSize) + 1;
            int side = radius * 2 + 1;
            command.SetComputeIntParams(compute, ScanCenterBlockId,
                centerBlock.x, centerBlock.y, centerBlock.z);
            command.SetComputeIntParam(compute, ScanBlockRadiusId, radius);
            command.SetComputeIntParam(compute, ScanBlockSideId, side);
            command.DispatchComputeProfiled(compute, _queryFineEraseKernel,
                side * side * side, 1, 1);
        }

        private bool HasRigidObservationFrame()
        {
            bool rigid = true;
            for (Transform current = _grid.transform; current != null;
                 current = current.parent)
            {
                Vector3 scale = current.localScale;
                Quaternion rotation = current.localRotation;
                Vector3 position = current.localPosition;
                // Do not use Vector3.operator==: its tolerance would admit
                // an actual scale change into a metre-calibrated authority.
                if (scale.x != 1f || scale.y != 1f || scale.z != 1f ||
                    !float.IsFinite(position.x) || !float.IsFinite(position.y) ||
                    !float.IsFinite(position.z) || !float.IsFinite(rotation.x) ||
                    !float.IsFinite(rotation.y) || !float.IsFinite(rotation.z) ||
                    !float.IsFinite(rotation.w) ||
                    (rotation.x == 0f && rotation.y == 0f &&
                     rotation.z == 0f && rotation.w == 0f))
                {
                    rigid = false;
                    break;
                }
            }
            if (!rigid && !_reportedNonRigidFrame)
                Logger.Error("M8 observation requires a finite rigid metre frame; " +
                    "scaled scan hierarchy is not a calibrated observation.");
            _reportedNonRigidFrame = !rigid;
            return rigid;
        }

        private void AcquireCameraObservation()
        {
            if (_readyCameraSlot < 0 ||
                !_cameraPairAvailable[_readyCameraSlot])
                throw new InvalidOperationException(
                    "A complete synchronized PCA pair is required.");
            MerkabaNativeVulkanExecutor.BeginHeldObservationTiming();
            _cameraObservationHeld = true;
            _heldCameraSlot = _readyCameraSlot;
            _heldFineBrush = _cameraFineBrush[_heldCameraSlot];
            _readyCameraSlot = -1;
            _observationGridToWorld = _grid.GridToWorldMatrix;
            _observationWorldToGrid = _observationGridToWorld.inverse;
            _observationMaxUpdateDistance = maxUpdateDistance;
            _observationExclusionCount = Mathf.Min(ExclusionZones.Count,
                _exclusionPositions.Length);
            for (int index = 0; index < _exclusionPositions.Length; ++index)
                _exclusionPositions[index] =
                    index < _observationExclusionCount &&
                    ExclusionZones[index] != null
                        ? ExclusionZones[index].position
                        : Vector3.positiveInfinity;
        }

        private void ReleaseOwnedObservation()
        {
            MerkabaNativeVulkanExecutor.EndHeldObservationTiming(_observationToken,
                false, 0u);
            if (_bins != null && _bins.FrozenObservation != 0u)
                _bins.EndAfterFinalization();
            _depthCapture?.ReleaseConsumedObservation();
            if (_heldCameraSlot >= 0)
                _cameraPairAvailable[_heldCameraSlot] = false;
            _cameraObservationHeld = false;
            _heldCameraSlot = -1;
            _heldFineBrush = default;
        }

        private void StoreCameraEye(CommandBuffer command, int slot, int eye,
            CameraFrameDescriptor frame, bool timedSubmission)
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
            command.BlitPcaObservationProfiled(frame.Texture, owned,
                timedSubmission);
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
            if (!SystemInfo.supportsGraphicsFence)
                return Task.FromException(new NotSupportedException(
                    "Quest PCA copy retirement requires a graphics fence."));

            // Lifecycle-only fence after every already submitted PCA copy.
            // No pixel is transferred to the CPU and acquisition never waits.
            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba PCA copy retirement");
            try
            {
                // RetireCameraCopiesAsync awaits this on the CPU, so it
                // must be a CPU-synchronisation fence; see MerkabaGrid.Gpu.
                GraphicsFence fence = command.CreateGraphicsFence(
                    GraphicsFenceType.CPUSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                Graphics.ExecuteCommandBuffer(command);
                _cameraCopyRetirementTask = RetireCameraCopiesAsync(fence,
                    target);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
            return _cameraCopyRetirementTask;
        }

        private async Task RetireCameraCopiesAsync(GraphicsFence fence,
            ulong target)
        {
            while (!fence.passed) await Task.Yield();
            _cameraCopyRetiredEpoch = Math.Max(_cameraCopyRetiredEpoch, target);
        }

        public void Clear()
        {
            if (_scanner != null && _scanner.ExportMutationHeld)
                throw new InvalidOperationException("Cannot clear the held export source.");
            _grid?.Clear();
            _observationPrepared = false;
            _observationToken = 0u;
            _observationDepthVersion = 0;
            _attemptInFlight = false;
            _attemptToken = 0u;
            _fineErasePrepared = false;
            _fineEraseAttemptInFlight = false;
            _fineEraseWaitingForDependency = false;
            _fineEraseAttemptToken = 0u;
            _fineEraseResidencyEpoch = 0u;
            _fineEraseDescriptor = default;
            ReleaseOwnedObservation();
            if (_readyCameraSlot >= 0)
                _cameraPairAvailable[_readyCameraSlot] = false;
            _readyCameraSlot = -1;
            IntegrationCount = 0;
        }

        internal void RestoreIntegrationCount(int integrationCount) =>
            IntegrationCount = Mathf.Max(0, integrationCount);

    }
}
