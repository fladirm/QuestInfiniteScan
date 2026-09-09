using System;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.UI;
using UnityEngine;

namespace Genesis.RoomScan
{
    public enum ScanLifecycleState
    {
        Stopped,
        Starting,
        Running,
        Quiescing
    }

    /// <summary>
    /// Small Quest scanner lifecycle: camera/depth observation feeds the one Merkaba
    /// grid, with explicit persistence and GLB actions. No reconstruction mode switch exists.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DepthCapture), typeof(PassthroughCameraProvider))]
    [RequireComponent(typeof(MerkabaGrid), typeof(MerkabaIntegrator))]
    [RequireComponent(typeof(MerkabaGridRenderer), typeof(MerkabaPersistence))]
    [RequireComponent(typeof(MerkabaExporter))]
    public sealed class RoomScanner : MonoBehaviour
    {
        public static RoomScanner Instance { get; private set; }

        [SerializeField, Range(5f, 30f)] private float integrationHz = 20f;
        [SerializeField, Range(0.005f, 0.05f)]
        private float maximumRgbdSkewSeconds = 1f / 30f;
        [SerializeField] private bool fineMode;
        [SerializeField, Range(0.01f, 0.5f)]
        private float fineBrushRadius = 0.1f;
        [SerializeField, Range(0.025f, 2f)]
        private float fineToolLength = 0.25f;
        [SerializeField] private LogLevel logLevel = LogLevel.Info;

        private DepthCapture _depthCapture;
        private PassthroughCameraProvider _cameraProvider;
        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;
        private MerkabaGridRenderer _renderer;
        private MerkabaPersistence _persistence;
        private MerkabaExporter _exporter;
        private RoomAnchorManager _anchorManager;
        private DebugMenuController _debugMenu;
        private ControllerRayDriver _controllerRay;
        private float _lastIntegrationTime;
        private ScanOperationState _operation = ScanOperationState.Idle;
        private readonly ScanOperationProgressTracker _operationProgress = new();
        private Task<bool> _quiesceTask;
        private Task _pauseTransitionTask = Task.CompletedTask;
        private Task _disableTeardownTask = Task.CompletedTask;
        private Task<bool> _exportTask;
        private CancellationTokenSource _exportCancellation;
        private bool _exportMutationHeld;
        private MerkabaArtifactViewer _exportDesignViewer;
        private Task<bool?> _nativeImportTask;
        private CancellationTokenSource _nativeImportCancellation;
        private uint _lifecycleGeneration;
        private bool _applicationPaused;
        private bool _resumeAfterPause;
        private Guid _resumeAnchorUuid;
        private bool _disableRequested;
        private bool _destroyed;
        private long _acceptedRgbdObservations;
        private long _expiredDepthFrames;
        private double _maximumRgbdSkewSeconds;
        private double _leftDepthDeltaMilliseconds;
        private double _rightDepthDeltaMilliseconds;
        private double _pairCenterOffsetMilliseconds;
        private double _firstPairCenterOffsetMilliseconds;
        private bool _hasPairClockBaseline;
        private float _lastRgbdLogTime;
        private bool _fineRefineHeld;
        private bool _fineEraseHeld;
        private bool _fineAuthorityActive;
        private bool _fineCycleArmed;
        private uint _fineMinimumLeftSequence;
        private uint _fineMinimumRightSequence;
        private uint _fineMinimumSurfaceTargetSequence;
        private uint _fineObservationTargetSequence;
        private FineBrushOperation _lastFineAction;
        private FineBrushDescriptor _fineObservationDescriptor;
        private FineBrushDescriptor _fineEraseDescriptor;
        private FineBrushDescriptor _finePreviewDescriptor;
        private bool _headTrackingBlocked;
        private float _lastHeadTrackingWarningTime;
        private bool _newSessionPending;
        private bool _newLiveScanRequired;

        public bool IsScanning { get; private set; }
        public bool IsScanStarting => ScanLifecycle == ScanLifecycleState.Starting;
        public ScanLifecycleState ScanLifecycle { get; private set; }
        public string LastScanStartError { get; private set; }
        public int ActiveChunkCount => _grid != null ? _grid.ActiveChunkCount : 0;
        public int OccupiedKernelCount => _grid != null ? _grid.OccupiedKernelCount : 0;
        public bool TryGetStoredScanProximity(Vector3 worldPosition,
            out Vector3 worldDirection, out float distance)
        {
            if (_grid != null)
                return _grid.TryGetStoredScanProximity(worldPosition,
                    out worldDirection, out distance);
            worldDirection = Vector3.zero;
            distance = 0f;
            return false;
        }
        public int IntegrationCount => _integrator != null ? _integrator.IntegrationCount : 0;
        public bool SavedSessionExists => _persistence != null && _persistence.SavedSessionExists;
        public bool AnySessionExists => _persistence != null &&
            _persistence.AnySessionExists;
        public Guid ActiveSessionId => _persistence?.ActiveSessionId ??
            Guid.Empty;
        internal Guid ActiveAnchorUuid => _persistence?.ActiveAnchorUuid ??
            Guid.Empty;
        public string ActiveSessionName => _persistence?.ActiveSessionName ??
            "No session";
        internal string ActiveDesignPath =>
            _persistence?.ActiveDesignPath ?? string.Empty;
        internal string DesignLibraryPath =>
            _persistence?.DesignLibraryPath ?? string.Empty;
        public bool SessionIsDirty => _persistence?.IsDirty ?? false;
        public System.Collections.Generic.IReadOnlyList<MerkabaSessionInfo>
            Sessions => _persistence?.Sessions ??
                Array.Empty<MerkabaSessionInfo>();
        public string PersistenceStatus => _persistence?.LastStatus ?? "Unavailable";
        public string ExportStatus => _exporter?.LastStatus ?? "Unavailable";
        public string LastExportPath => _exporter?.LastExportPath;
        public bool IsBusy => ScanLifecycle == ScanLifecycleState.Quiescing ||
                              _operation.Busy || (_persistence?.IsBusy ?? false) ||
                              (_exporter?.IsExporting ?? false) ||
                              _newSessionPending;
        public ScanOperationState CurrentOperation => _operation;
        // Current-source lease, not a historical snapshot. It includes the
        // immutable observation drain and is released only after all export IO.
        internal bool ExportMutationHeld => _exportMutationHeld;
        public bool FineMode
        {
            get => fineMode;
            set
            {
                if (ExportMutationHeld || fineMode == value) return;
                fineMode = value;
                _fineCycleArmed = false;
                _fineMinimumSurfaceTargetSequence = _depthCapture != null
                    ? _depthCapture.FineSurfaceTargetIssuedSequence : 0u;
            }
        }
        public bool FineEraseSelected { get; set; }
        internal bool FineAuthorityActive => _fineAuthorityActive;
        public float FineBrushRadius
        {
            get => fineBrushRadius;
            set => fineBrushRadius = Mathf.Clamp(value, 0.01f, 0.5f);
        }
        public float FineToolLength
        {
            get => fineToolLength;
            set => fineToolLength = Mathf.Clamp(value, 0.025f, 2f);
        }
        public float ScanOpacity
        {
            get => _renderer != null ? _renderer.ScanOpacity : 1f;
            set { if (_renderer != null) _renderer.ScanOpacity = value; }
        }
        public bool ReadoutDrawEnabled
        {
            get => _renderer == null || _renderer.ReadoutDrawEnabled;
            set
            {
                if (_renderer != null) _renderer.ReadoutDrawEnabled = value;
            }
        }
        public bool CheckerReadoutEnabled
        {
            get => _renderer != null && _renderer.CheckerReadoutEnabled;
            set
            {
                if (_renderer != null)
                    _renderer.CheckerReadoutEnabled = value;
            }
        }
        public bool DynamicOcclusionEnabled
        {
            get => _depthCapture == null ||
                _depthCapture.DynamicOcclusionEnabled;
            set
            {
                if (_depthCapture != null)
                    _depthCapture.DynamicOcclusionEnabled = value;
                _renderer?.SetDynamicOcclusionEnabled(value);
            }
        }

        public MerkabaGrid Grid => _grid;
        public MerkabaPersistence Persistence => _persistence;
        public MerkabaExporter Exporter => _exporter;

        public event Action ScanStarted;
        public event Action ScanStopped;
        public event Action Integrated;
        public event Action OperationChanged;

        private float IntegrationInterval => 1f / Mathf.Max(1f, integrationHz);

        private void Awake()
        {
            Instance = this;
            Logger.Level = logLevel;
            _depthCapture = GetComponent<DepthCapture>();
            _cameraProvider = GetComponent<PassthroughCameraProvider>();
            _grid = GetComponent<MerkabaGrid>();
            _integrator = GetComponent<MerkabaIntegrator>();
            _renderer = GetComponent<MerkabaGridRenderer>();
            _persistence = GetComponent<MerkabaPersistence>();
            _exporter = GetComponent<MerkabaExporter>();
            _anchorManager = FindAnyObjectByType<RoomAnchorManager>(
                FindObjectsInactive.Include);
            _debugMenu = FindAnyObjectByType<DebugMenuController>(
                FindObjectsInactive.Include);
            _controllerRay = FindAnyObjectByType<ControllerRayDriver>(
                FindObjectsInactive.Include);
            _integrator.Integrated += OnIntegrated;
            _integrator.AuthorityChanged += OnAuthorityChanged;
            _integrator.FineErased += OnFineErased;
            _renderer.SetDynamicOcclusionEnabled(
                _depthCapture.DynamicOcclusionEnabled);
        }

        private void Start()
        {
            if (!XRRuntimeGuard.IsXRActive)
            {
                Logger.Warning("RoomScanner: " + XRRuntimeGuard.EditorDisabledMessage);
                enabled = false;
                return;
            }
            Camera camera = Camera.main;
            if (camera != null && !_integrator.ExclusionZones.Contains(camera.transform))
                _integrator.ExclusionZones.Add(camera.transform);
            Logger.Info("Quest Infinite Merkaba ready — use START / RESUME to scan");
        }

        private void Update()
        {
            MerkabaGpuTimestamps.Poll();
            _integrator.TryRetireFineEraseAttempt();
            _integrator.TryRetireObservationAttempt();
            UpdateFineAuthorityBoundary();
            UpdateFineActionBoundary();
            UpdateFinePreview();
            if (!IsScanning) return;
            LogRgbdPairing();
            if (_integrator.HasPendingFineErase)
            {
                if (!_integrator.HasFineEraseAttemptInFlight)
                    _integrator.TrySubmitFineEraseAttempt();
                return;
            }
            if (_integrator.HasPendingObservation)
            {
                if (!_integrator.HasAttemptInFlight)
                    _integrator.TrySubmitObservationAttempt();
                return;
            }
            if (!HasTrackedHeadPose())
            {
                if (_depthCapture.HasUnprocessedFrame)
                {
                    _expiredDepthFrames++;
                    _depthCapture.DiscardReadyDepthFrame();
                }
                ArmNextObservation();
                return;
            }
            if (_fineAuthorityActive)
            {
                // Authority transitions retire/discard work before becoming
                // visible. While an OFF transition is pending, do not admit a
                // new manual operation.
                if (!fineMode) return;
                if (CurrentFineAction() == FineBrushOperation.Erase)
                    UpdateFineErase();
                else
                    UpdateFineRefine();
                return;
            }
            if (Time.time - _lastIntegrationTime < IntegrationInterval) return;
            if (!_depthCapture.HasUnprocessedFrame) return;

            if (!_integrator.HasReadyStereoCameraFrame)
            {
                if (!_depthCapture.TryGetReadyFrameUnixTime(
                        out double depthUnixSeconds, out _))
                    return;
                double clockUncertainty =
                    _depthCapture.TimestampMappingUncertaintySeconds;
                double availableSkew = maximumRgbdSkewSeconds -
                    clockUncertainty;
                if (availableSkew <= 0.0)
                {
                    _expiredDepthFrames++;
                    _depthCapture.DiscardReadyDepthFrame();
                    ArmNextObservation();
                    return;
                }
                StereoFrameMatch match = _cameraProvider.TryGetSynchronizedFrame(
                    depthUnixSeconds, availableSkew,
                    out StereoCameraFrame cameraFrame);
                if (match == StereoFrameMatch.Waiting) return;
                if (match == StereoFrameMatch.DepthExpired)
                {
                    _expiredDepthFrames++;
                    _depthCapture.DiscardReadyDepthFrame();
                    ArmNextObservation();
                    return;
                }
                RecordRgbdClockEvidence(depthUnixSeconds, cameraFrame);
                cameraFrame = new StereoCameraFrame(cameraFrame.Left,
                    cameraFrame.Right, cameraFrame.MaximumSkewSeconds +
                    clockUncertainty);
                if (!_integrator.SetStereoCameraData(cameraFrame)) return;
                _acceptedRgbdObservations++;
                _maximumRgbdSkewSeconds = Math.Max(
                    _maximumRgbdSkewSeconds,
                    cameraFrame.MaximumSkewSeconds);
            }

            if (_integrator.TrySubmitObservationAttempt())
            {
                _lastIntegrationTime = Time.time;
                ArmNextObservation();
            }
        }

        private bool HasTrackedHeadPose()
        {
            bool tracked = OVRPlugin.GetNodePositionTracked(
                    OVRPlugin.Node.EyeCenter) &&
                OVRPlugin.GetNodeOrientationTracked(OVRPlugin.Node.EyeCenter);
            if (!tracked)
            {
                float now = Time.unscaledTime;
                if (!_headTrackingBlocked ||
                    now - _lastHeadTrackingWarningTime >= 1f)
                {
                    _lastHeadTrackingWarningTime = now;
                    Logger.Warning("Merkaba observation paused: Quest head " +
                        "position/orientation tracking is invalid");
                }
                _headTrackingBlocked = true;
                return false;
            }
            if (_headTrackingBlocked)
                Logger.Info("Merkaba observation resumed after Quest head " +
                    "tracking recovered");
            _headTrackingBlocked = false;
            return true;
        }

        private void OnEnable() => _disableRequested = false;

        private void OnApplicationPause(bool paused)
        {
            if (paused && !_applicationPaused)
            {
                _resumeAfterPause = IsScanning || IsScanStarting;
                _resumeAnchorUuid = _resumeAfterPause && _persistence != null
                    ? _persistence.ActiveAnchorUuid : Guid.Empty;
            }
            _applicationPaused = paused;
            Logger.Info($"Application pause={paused} resumeScan=" +
                        $"{_resumeAfterPause} lifecycle={ScanLifecycle}");
            Task prior = _pauseTransitionTask;
            _pauseTransitionTask = ApplyApplicationPauseAsync(prior, paused);
        }

        private void OnDisable()
        {
            _disableRequested = true;
            _resumeAfterPause = false;
            BeginDisableTeardown();
        }

        private void OnDestroy()
        {
            _destroyed = true;
            BeginDisableTeardown();
            if (_integrator != null) _integrator.Integrated -= OnIntegrated;
            if (_integrator != null) _integrator.AuthorityChanged -= OnAuthorityChanged;
            if (_integrator != null) _integrator.FineErased -= OnFineErased;
            if (Instance == this) Instance = null;
        }

        public async Task StartScanningAsync()
        {
            if (ExportMutationHeld || _operation.Busy || _newSessionPending ||
                IsScanning || IsScanStarting) return;
            if (_disableTeardownTask != null && !_disableTeardownTask.IsCompleted)
                await _disableTeardownTask;
            if (ExportMutationHeld || _operation.Busy || _destroyed ||
                _disableRequested || !isActiveAndEnabled) return;
            _grid?.ResumeGpuSubmission();
            if (ScanLifecycle == ScanLifecycleState.Quiescing &&
                !await QuiesceScanningAsync())
                return;
            uint generation = NextLifecycleGeneration();
            ScanLifecycle = ScanLifecycleState.Starting;
            LastScanStartError = null;
            try
            {
                if (_depthCapture == null)
                    throw new InvalidOperationException("DepthCapture is unavailable.");
                _depthCapture.RequireValidCalibration();
                if (_newLiveScanRequired)
                {
                    await NewClearAsync(null);
                    if (_newLiveScanRequired || _applicationPaused || _destroyed || _disableRequested)
                        return;
                    generation = NextLifecycleGeneration();
                    ScanLifecycle = ScanLifecycleState.Starting;
                }
                await EnsureRoomAnchorAsync();
                if (!StartIsCurrent(generation)) return;
                if (_persistence != null)
                    await _persistence.RestoreReleasedGpuWorldAsync();
                if (!StartIsCurrent(generation)) return;
                _grid.EnsureGpuResources();
                _renderer?.ResumeGpuSubmission();
                await Task.Yield();
                await Task.Yield();
                if (!StartIsCurrent(generation)) return;
                bool cameraPermission = await PassthroughCameraProvider
                    .RequestCameraPermissionAsync();
                if (!StartIsCurrent(generation)) return;
                if (!cameraPermission)
                    throw new InvalidOperationException(
                        "True-stereo scan requires HEADSET_CAMERA permission " +
                        "for both PCA eyes.");
                _cameraProvider?.StartCapture();
                Task<bool> depthReady = _depthCapture.StartDepthCaptureAsync();
                Task<bool> stereoReady = _cameraProvider != null
                    ? _cameraProvider.WaitForFreshStereoReadyAsync()
                    : Task.FromResult(false);
                bool[] sensorReady = await Task.WhenAll(depthReady,
                    stereoReady);
                if (!StartIsCurrent(generation)) return;
                if (!sensorReady[0])
                    throw new InvalidOperationException(
                        "Environment Depth did not become ready with a fresh " +
                        "owned stereo snapshot.");
                if (!sensorReady[1])
                    throw new InvalidOperationException(
                        "Both physical PCA eyes did not produce fresh frames.");
                _acceptedRgbdObservations = 0L;
                _expiredDepthFrames = 0L;
                _maximumRgbdSkewSeconds = 0.0;
                _leftDepthDeltaMilliseconds = 0.0;
                _rightDepthDeltaMilliseconds = 0.0;
                _pairCenterOffsetMilliseconds = 0.0;
                _firstPairCenterOffsetMilliseconds = 0.0;
                _hasPairClockBaseline = false;
                _lastRgbdLogTime = Time.unscaledTime;
                _lastIntegrationTime = Time.time;
                IsScanning = true;
                ScanLifecycle = ScanLifecycleState.Running;
                ArmNextObservation();
                MerkabaGpuTimestamps.NotifyScanStarted();
                ScanStarted?.Invoke();
                Logger.Info("Merkaba scanning started/resumed");
            }
            catch (Exception exception)
            {
                if (!StartIsCurrent(generation)) return;
                LastScanStartError = exception.Message;
                Logger.Error("Could not start Merkaba scan: " + exception);
                await QuiesceScanningAsync();
            }
        }

        public void StopScanning()
        {
            _ = QuiesceScanningAsync();
        }

        public void ToggleScanning()
        {
            if (IsScanning || IsScanStarting) StopScanning();
            else _ = StartScanningAsync();
        }

        internal void SetFineHeldActions(bool refineHeld, bool eraseHeld)
        {
            if (ExportMutationHeld) refineHeld = eraseHeld = false;
            _fineRefineHeld = refineHeld;
            _fineEraseHeld = eraseHeld;
        }

        public async Task<bool> SaveAsync()
        {
            if (IsBusy) return false;
            if (!TryBeginOperation(ScanOperationKind.Save,
                    ScanOperationStage.SynchronizingScan,
                    "Retiring current scan observation")) return false;
            bool success = false;
            try
            {
                if (!await BeginFrozenWorldOperationAsync()) return false;
                if (!SaveOpenDesign()) return false;
                ReportOperation(ScanOperationKind.Save,
                    ScanOperationStage.SynchronizingScan, 1L, 1L,
                    "Scan synchronized");
                success = _persistence != null && await _persistence.SaveAsync();
                return success;
            }
            finally
            {
                EndFrozenWorldOperation();
                FinishOperation(ScanOperationKind.Save, success,
                    _persistence?.LastStatus ?? "Save unavailable");
            }
        }

        public async Task<bool> LoadAsync()
        {
            if (IsBusy) return false;
            if (!TryBeginOperation(ScanOperationKind.Load,
                    ScanOperationStage.SynchronizingScan,
                    "Retiring current scan observation")) return false;
            bool success = false;
            try
            {
                if (!await BeginFrozenWorldOperationAsync()) return false;
                if (!CloseOpenDesignForSessionSwitch()) return false;
                ReportOperation(ScanOperationKind.Load,
                    ScanOperationStage.SynchronizingScan, 1L, 1L,
                    "Scan synchronized");
                success = _persistence != null && await _persistence.LoadAsync();
                if (success) _newLiveScanRequired = false;
                return success;
            }
            finally
            {
                EndFrozenWorldOperation();
                FinishOperation(ScanOperationKind.Load, success,
                    _persistence?.LastStatus ?? "Load unavailable");
            }
        }

        // A canonical M8 tile is complete at every instant; a running scan only
        // keeps refining it. So SAVE, LOAD, OPEN, DELETE, NEW, EXPORT and
        // IMPORT do not wait for anything to "finish" - they take the world as
        // it stands and then own it exclusively. Two producers have to stop for
        // that to be true: the scan, and the dirty-page readout job, which is
        // resubmitted every rendered frame once the native scanner is ready and
        // otherwise keeps competing for the queue the operation needs. Leaving
        // it running is what made a new session fail on a leased dual world and
        // a save crawl on "Counting dirty canonical tiles".
        //
        // This is the order DisableTeardownCoreAsync already established, and
        // closure 4.7 requires the in-flight job be waited out rather than
        // preempted by a CPU flag. The renderer's submission gate is not the
        // grid's: suspending it stops new readout jobs while GpuSubmissionAllowed
        // stays true, which SwitchStorageRootAsync requires when it clears.
        private async Task<bool> BeginFrozenWorldOperationAsync()
        {
            _renderer?.PausePagePublication();
            if (!await QuiesceScanningAsync()) return false;
            if (!ReferenceEquals(_renderer, null))
                await _renderer.FinishCurrentReadoutAsync();
            if (!ReferenceEquals(_grid, null))
                await _grid.FinishObservationDurableCutAsync();
            return true;
        }

        private void EndFrozenWorldOperation() => _renderer?.ResumePagePublication();

        public async Task<bool> OpenSessionAsync(Guid sessionId)
        {
            if (IsBusy || sessionId == Guid.Empty) return false;
            if (!TryBeginOperation(ScanOperationKind.Load,
                    ScanOperationStage.SynchronizingScan,
                    "Retiring current scan observation")) return false;
            bool success = false;
            try
            {
                if (!await BeginFrozenWorldOperationAsync()) return false;
                if (!CloseOpenDesignForSessionSwitch()) return false;
                ReportOperation(ScanOperationKind.Load,
                    ScanOperationStage.SynchronizingScan, 1L, 1L,
                    "Scan synchronized");
                success = _persistence != null &&
                    await _persistence.OpenSessionAsync(sessionId);
                if (success) _newLiveScanRequired = false;
                return success;
            }
            finally
            {
                EndFrozenWorldOperation();
                FinishOperation(ScanOperationKind.Load, success,
                    _persistence?.LastStatus ?? "Open unavailable");
            }
        }

        public async Task<bool> SaveAsAsync(string displayName)
        {
            if (IsBusy) return false;
            if (!TryBeginOperation(ScanOperationKind.Save,
                    ScanOperationStage.SynchronizingScan,
                    "Retiring current scan observation")) return false;
            bool success = false;
            try
            {
                if (!await BeginFrozenWorldOperationAsync()) return false;
                if (!SaveOpenDesign()) return false;
                ReportOperation(ScanOperationKind.Save,
                    ScanOperationStage.SynchronizingScan, 1L, 1L,
                    "Scan synchronized");
                success = _persistence != null &&
                    await _persistence.SaveAsAsync(displayName);
                if (success)
                    FindAnyObjectByType<MerkabaArtifactViewer>()?.
                        RebindSessionDesign();
                return success;
            }
            finally
            {
                EndFrozenWorldOperation();
                FinishOperation(ScanOperationKind.Save, success,
                    _persistence?.LastStatus ?? "Save As unavailable");
            }
        }

        public bool RenameActiveSession(string displayName) =>
            !IsBusy && _persistence != null &&
            _persistence.RenameActiveSession(displayName);

        public async Task<bool> DeleteSessionAsync(Guid sessionId)
        {
            if (IsBusy || sessionId == Guid.Empty) return false;
            bool wasActive = sessionId == ActiveSessionId;
            // Only the active session replaces the GPU world; a stored session
            // is deleted without touching the native queue.
            if (wasActive)
            {
                if (!await BeginFrozenWorldOperationAsync()) return false;
            }
            else if (!await QuiesceScanningAsync()) return false;
            try
            {
                if (wasActive && !CloseOpenDesignForSessionSwitch()) return false;
                return _persistence != null &&
                    await _persistence.DeleteSessionAsync(sessionId);
            }
            finally
            {
                if (wasActive) EndFrozenWorldOperation();
            }
        }

        public async Task NewClearAsync() => await NewClearAsync(null);

        public async Task NewClearAsync(string displayName)
        {
            if (IsBusy) return;
            _newSessionPending = true;
            try
            {
                if (!await BeginFrozenWorldOperationAsync()) return;
                if (!CloseOpenDesignForSessionSwitch()) return;
                _anchorManager ??= RoomAnchorManager.Instance ??
                    FindAnyObjectByType<RoomAnchorManager>(
                        FindObjectsInactive.Include);
                if (_anchorManager == null || !_anchorManager.enabled ||
                    !await _anchorManager.EnsureSessionAnchorAsync(Guid.Empty,
                        true) || _anchorManager.SpatialAnchorUuid == Guid.Empty)
                    throw new InvalidOperationException(
                        "A new persisted room anchor could not be created.");
                if (_persistence == null)
                    throw new InvalidOperationException(
                        "Session persistence is unavailable.");
                await _persistence.BeginNewSessionAsync(
                    _anchorManager.SpatialAnchorUuid, displayName);
                _integrator?.Clear();
                await Task.Yield();
                _newLiveScanRequired = false;
                _resumeAnchorUuid = Guid.Empty;
                _resumeAfterPause = false;
                LastScanStartError = null;
                Logger.Info("Started a new empty anchored Merkaba session " +
                    _anchorManager.SpatialAnchorUuid.ToString("D"));
            }
            catch (Exception exception)
            {
                LastScanStartError = exception.Message;
                Logger.Error("Could not create a new Merkaba session: " +
                    exception);
            }
            finally
            {
                EndFrozenWorldOperation();
                _newSessionPending = false;
            }
        }

        public Task<bool> ExportGlbAsync() => ExportGlbAsync(null);

        public Task<bool> ExportGlbAsync(string fileName,
            CancellationToken cancellationToken = default) =>
            BeginExportAsync(fileName, false, cancellationToken);

        public Task<bool> ExportViewerPackageAsync() =>
            ExportViewerPackageAsync(null);

        public Task<bool> ExportViewerPackageAsync(string fileName,
            CancellationToken cancellationToken = default) =>
            BeginExportAsync(fileName, true, cancellationToken);

        public void CancelExport() => _exportCancellation?.Cancel();

        private Task<bool> BeginExportAsync(string fileName, bool viewerPackage,
            CancellationToken cancellationToken)
        {
            if (IsBusy || IsScanStarting || _destroyed || _disableRequested ||
                (_pauseTransitionTask != null && !_pauseTransitionTask.IsCompleted) ||
                (_disableTeardownTask != null && !_disableTeardownTask.IsCompleted) ||
                cancellationToken.IsCancellationRequested || _exporter == null ||
                _persistence == null) return Task.FromResult(false);
            if (!TryBeginOperation(ScanOperationKind.ExportGlb,
                    ScanOperationStage.SynchronizingScan,
                    "Retiring current scan observation")) return Task.FromResult(false);
            try
            {
                // This synchronous save precedes the mutation lease. No input
                // update or asynchronous document worker runs between them.
                _exportDesignViewer = FindAnyObjectByType<MerkabaArtifactViewer>();
                if (_exportDesignViewer != null && _exportDesignViewer.IsOpen &&
                    !_exportDesignViewer.SaveDesign())
                    throw new InvalidOperationException("Could not save the export design source.");
                _exportCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                _exportMutationHeld = true;
                _fineRefineHeld = _fineEraseHeld = _fineCycleArmed = false;
                _resumeAfterPause = false;
                _exportTask = RunExportAsync(fileName, viewerPackage,
                    _exportCancellation.Token);
                return _exportTask;
            }
            catch (Exception exception)
            {
                _exportMutationHeld = false;
                _exportCancellation?.Dispose();
                _exportCancellation = null;
                CompleteExportDesignRelease();
                _exporter.ReportExportFailure(exception);
                FinishOperation(ScanOperationKind.ExportGlb, false, _exporter.LastStatus);
                return Task.FromResult(false);
            }
        }

        private async Task<bool> RunExportAsync(string fileName, bool viewerPackage,
            CancellationToken cancellationToken)
        {
            bool success = false;
            try
            {
                // Cancellation cannot abandon a held GPU observation or its
                // SSD acknowledgement. Both retire before any source is read.
                if (!await BeginFrozenWorldOperationAsync())
                    throw new InvalidOperationException("Export observation retirement was not proven.");
                await _persistence.PrepareExportDurableCutAsync(
                    _exporter.HasResumeReceipt(fileName, viewerPackage));
                cancellationToken.ThrowIfCancellationRequested();
                ReportOperation(ScanOperationKind.ExportGlb,
                    ScanOperationStage.SynchronizingScan, 1L, 1L,
                    "Scan paused; committed export source held");
                success = viewerPackage
                    ? await _exporter.ExportViewerPackageCoreAsync(fileName, cancellationToken)
                    : await _exporter.ExportGlbCoreAsync(fileName, cancellationToken);
                return success;
            }
            catch (Exception exception)
            {
                _exporter.ReportExportFailure(exception);
                return false;
            }
            finally
            {
                // Every worker is awaited by the core, including cancellation
                // and staging cleanup. STOP remains STOP after lease release.
                EndFrozenWorldOperation();
                _exportMutationHeld = false;
                _exportCancellation.Dispose();
                _exportCancellation = null;
                CompleteExportDesignRelease();
                FinishOperation(ScanOperationKind.ExportGlb, success,
                    _exporter?.LastStatus ?? "Export unavailable");
            }
        }

        private void CompleteExportDesignRelease()
        {
            MerkabaArtifactViewer viewer = _exportDesignViewer;
            _exportDesignViewer = null;
            try { viewer?.CompleteDeferredExportClose(); }
            catch (Exception exception)
            { Logger.Error("Export design close failed: " + exception); }
        }

        // null means no native declaration: the caller may use its ordinary
        // preview reader. A declared invalid package is a failure, not preview.
        public Task<bool?> ImportNativePackageAsync(string filePath,
            CancellationToken cancellationToken = default)
        {
            if (IsBusy || IsScanStarting || _destroyed || _disableRequested ||
                (_pauseTransitionTask != null && !_pauseTransitionTask.IsCompleted) ||
                (_disableTeardownTask != null && !_disableTeardownTask.IsCompleted) ||
                _persistence == null || cancellationToken.IsCancellationRequested)
                return Task.FromResult<bool?>(false);
            if (!TryBeginOperation(ScanOperationKind.Load,
                ScanOperationStage.RebuildingStorageIndex, "Validating native package"))
                return Task.FromResult<bool?>(false);
            _nativeImportCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _nativeImportTask = RunNativeImportAsync(filePath, _nativeImportCancellation.Token);
            return _nativeImportTask;
        }

        public void CancelNativeImport() => _nativeImportCancellation?.Cancel();

        private async Task<bool?> RunNativeImportAsync(string filePath,
            CancellationToken cancellationToken)
        {
            bool success = false;
            string status = "Native import unavailable";
            try
            {
                using MerkabaNativePackage.ValidatedImport package = await Task.Run(() =>
                    MerkabaNativePackage.TryRead(filePath, cancellationToken));
                if (package == null)
                {
                    success = true;
                    status = "Preview-only package; no native scan records";
                    return null;
                }
                if (!await BeginFrozenWorldOperationAsync())
                    throw new InvalidOperationException("Native import requires proven scan retirement.");
                cancellationToken.ThrowIfCancellationRequested();
                if (_persistence.IsDirty)
                    throw new InvalidOperationException("Save the active scan before importing a native session.");
                if (!CloseOpenDesignForSessionSwitch())
                    throw new InvalidOperationException("Could not save and close the current design.");
                if (_persistence.IsDirty)
                    throw new InvalidOperationException("Save the changed active design before importing a native session.");
                Guid importedId = await _persistence.RegisterNativeSessionAsync(package, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                // OPEN owns its ordinary anchor/binding/residency transaction.
                // Once started, await it rather than cancelling halfway through
                // canonical source replacement. No imported mesh enters M8.
                success = await _persistence.OpenSessionAsync(importedId);
                status = _persistence.LastStatus;
                return success;
            }
            catch (OperationCanceledException)
            {
                status = "Native import cancelled; current scan retained";
                Logger.Info(status);
                return false;
            }
            catch (Exception exception)
            {
                status = "Native import failed: " + exception.Message;
                Logger.Error(status);
                return false;
            }
            finally
            {
                EndFrozenWorldOperation();
                _nativeImportCancellation.Dispose();
                _nativeImportCancellation = null;
                FinishOperation(ScanOperationKind.Load, success, status);
            }
        }

        public async void ClearAllDataAsync(Action onComplete = null)
        {
            await NewClearAsync();
            onComplete?.Invoke();
        }

        public void ToggleDebugMenu()
        {
            _debugMenu ??= FindAnyObjectByType<DebugMenuController>(
                FindObjectsInactive.Include);
            _debugMenu?.Toggle();
        }

        private async Task EnsureRoomAnchorAsync()
        {
            _anchorManager ??= RoomAnchorManager.Instance ??
                FindAnyObjectByType<RoomAnchorManager>(
                    FindObjectsInactive.Include);
            if (_anchorManager == null || !_anchorManager.enabled)
                throw new InvalidOperationException(
                    "RoomAnchorManager is unavailable for persistent scanning.");
            Guid requiredUuid = _persistence != null
                ? _persistence.ActiveAnchorUuid : Guid.Empty;
            if (requiredUuid == Guid.Empty)
                throw new InvalidOperationException(
                    "Create or open a scan session before starting.");
            if (!await _anchorManager.EnsureSessionAnchorAsync(requiredUuid,
                    false) || _anchorManager.SpatialAnchorUuid != requiredUuid)
                throw new InvalidOperationException(
                    "Room anchor not localized");
        }

        private void UpdateFineAuthorityBoundary()
        {
            if (ExportMutationHeld) return;
            if (_fineAuthorityActive == fineMode) return;
            if (_integrator == null ||
                !_integrator.TrySwitchObservationAuthority())
                return;

            _fineAuthorityActive = fineMode;
            _fineCycleArmed = false;
            _lastFineAction = FineBrushOperation.None;
            _fineMinimumSurfaceTargetSequence = _depthCapture != null
                ? _depthCapture.FineSurfaceTargetIssuedSequence : 0u;
            if (_fineAuthorityActive) return;

            _fineObservationDescriptor = default;
            _fineEraseDescriptor = default;
            _finePreviewDescriptor = default;
            _controllerRay?.SetFineBrushPreview(default,
                FineBrushOperation.None);
            _renderer?.SetFineSurfacePreview(default, Color.clear);
        }

        private void UpdateFineActionBoundary()
        {
            FineBrushOperation action = _fineAuthorityActive && fineMode
                ? CurrentFineAction() : FineBrushOperation.None;
            if (action == _lastFineAction) return;
            _lastFineAction = action;
            _fineCycleArmed = false;
            _fineMinimumSurfaceTargetSequence = _depthCapture != null
                ? _depthCapture.FineSurfaceTargetIssuedSequence : 0u;
        }

        private void UpdateFinePreview()
        {
            _controllerRay ??= FindAnyObjectByType<ControllerRayDriver>(
                FindObjectsInactive.Include);
            if (!_fineAuthorityActive || _controllerRay == null)
            {
                _finePreviewDescriptor = default;
                _controllerRay?.SetFineBrushPreview(default,
                    FineBrushOperation.None);
                _renderer?.SetFineSurfacePreview(default, Color.clear);
                return;
            }

            FineBrushOperation action = fineMode
                ? CurrentFineAction() : FineBrushOperation.None;
            FineBrushOperation previewOperation = action ==
                FineBrushOperation.None ? FineBrushOperation.Preview : action;
            bool cursorOnSurface = false;
            if (TryGetPendingFineDescriptor(out FineBrushDescriptor pending))
            {
                _finePreviewDescriptor = pending;
                cursorOnSurface = true;
                action = pending.Operation;
            }
            else if (fineMode && TryCreateFineDescriptor(previewOperation,
                         out FineBrushDescriptor liveDescriptor,
                         out cursorOnSurface))
                _finePreviewDescriptor = liveDescriptor;
            else
            {
                _finePreviewDescriptor = default;
                _controllerRay.SetFineBrushPreview(default,
                    FineBrushOperation.None);
                _renderer?.SetFineSurfacePreview(default, Color.clear);
                return;
            }
            Color color = _controllerRay.GetFineBrushPreviewColor(action);
            _controllerRay.SetFineBrushPreview(_finePreviewDescriptor, action,
                cursorOnSurface);
            _renderer?.SetFineSurfacePreview(_finePreviewDescriptor, color);
        }

        private bool TryGetPendingFineDescriptor(
            out FineBrushDescriptor descriptor)
        {
            if (_integrator != null && _integrator.HasPendingFineErase &&
                _fineEraseDescriptor.IsErase)
            {
                descriptor = _fineEraseDescriptor;
                return true;
            }
            if (_fineObservationDescriptor.IsRefine &&
                (_fineCycleArmed ||
                 (_integrator?.HasPendingObservation ?? false)))
            {
                descriptor = _fineObservationDescriptor;
                return true;
            }
            descriptor = default;
            return false;
        }

        private void UpdateFineRefine()
        {
            if (CurrentFineAction() != FineBrushOperation.Refine)
            {
                _fineCycleArmed = false;
                if (!(_integrator?.HasPendingObservation ?? false))
                    _fineObservationDescriptor = default;
                return;
            }

            if (!_fineCycleArmed)
            {
                if (!TryCreateFineDescriptor(FineBrushOperation.Refine,
                        out _fineObservationDescriptor))
                    return;
                _fineObservationTargetSequence =
                    _depthCapture.FineSurfaceTargetCompletedSequence;
                _cameraProvider.GetLatestSequences(
                    out _fineMinimumLeftSequence,
                    out _fineMinimumRightSequence);
                if (!_depthCapture.RequestFreshDepthFrame()) return;
                _fineCycleArmed = true;
            }

            if (Time.time - _lastIntegrationTime < IntegrationInterval ||
                !_depthCapture.HasUnprocessedFrame)
                return;

            if (!_integrator.HasReadyStereoCameraFrame)
            {
                if (!_depthCapture.TryGetReadyFrameUnixTime(
                        out double depthUnixSeconds, out _))
                    return;
                double clockUncertainty =
                    _depthCapture.TimestampMappingUncertaintySeconds;
                double availableSkew = maximumRgbdSkewSeconds -
                    clockUncertainty;
                if (availableSkew <= 0.0)
                {
                    RestartFineCycleAfterExpiredDepth();
                    return;
                }
                StereoFrameMatch match = _cameraProvider.TryGetSynchronizedFrame(
                    depthUnixSeconds, availableSkew,
                    _fineMinimumLeftSequence, _fineMinimumRightSequence,
                    out StereoCameraFrame cameraFrame);
                if (match == StereoFrameMatch.Waiting) return;
                if (match == StereoFrameMatch.DepthExpired)
                {
                    RestartFineCycleAfterExpiredDepth();
                    return;
                }
                RecordRgbdClockEvidence(depthUnixSeconds, cameraFrame);
                cameraFrame = new StereoCameraFrame(cameraFrame.Left,
                    cameraFrame.Right, cameraFrame.MaximumSkewSeconds +
                    clockUncertainty);
                if (!_integrator.SetStereoCameraData(cameraFrame,
                        _fineObservationDescriptor))
                    return;
                _acceptedRgbdObservations++;
                _maximumRgbdSkewSeconds = Math.Max(
                    _maximumRgbdSkewSeconds,
                    cameraFrame.MaximumSkewSeconds);
            }

            if (!_integrator.TrySubmitObservationAttempt()) return;
            _lastIntegrationTime = Time.time;
            _fineMinimumSurfaceTargetSequence =
                _fineObservationTargetSequence;
            _fineCycleArmed = false;
        }

        private void UpdateFineErase()
        {
            _fineCycleArmed = false;
            if (CurrentFineAction() != FineBrushOperation.Erase)
                return;
            if (!TryCreateFineDescriptor(FineBrushOperation.Erase,
                    out FineBrushDescriptor descriptor) ||
                !_integrator.TryPrepareFineErase(descriptor))
                return;
            _fineEraseDescriptor = descriptor;
            if (!_integrator.TrySubmitFineEraseAttempt()) return;
            _fineMinimumSurfaceTargetSequence =
                _depthCapture.FineSurfaceTargetCompletedSequence;
        }

        private void RestartFineCycleAfterExpiredDepth()
        {
            _expiredDepthFrames++;
            _depthCapture.DiscardReadyDepthFrame();
            _fineMinimumSurfaceTargetSequence =
                _fineObservationTargetSequence;
            _fineCycleArmed = false;
        }

        private FineBrushOperation CurrentFineAction()
        {
            if (_fineRefineHeld == _fineEraseHeld)
                return FineBrushOperation.None;
            return _fineRefineHeld
                ? FineBrushOperation.Refine : FineBrushOperation.Erase;
        }

        private bool TryCreateFineDescriptor(FineBrushOperation operation,
            out FineBrushDescriptor descriptor)
        {
            return TryCreateFineDescriptor(operation, out descriptor, out _);
        }

        private bool TryCreateFineDescriptor(FineBrushOperation operation,
            out FineBrushDescriptor descriptor, out bool cursorOnSurface)
        {
            descriptor = default;
            cursorOnSurface = false;
            if (_controllerRay == null ||
                !_controllerRay.TryGetWorldRay(out Vector3 rayOrigin,
                    out Vector3 rayDirection))
                return false;

            float targetDistance = _integrator != null
                ? _integrator.MaxUpdateDistance : 5f;
            if (!_depthCapture.TryUpdateFineSurfaceTarget(rayOrigin,
                    rayDirection, targetDistance, true,
                    out Vector3 cursorPosition, out Vector3 surfaceNormal))
                return false;
            if ((operation == FineBrushOperation.Refine ||
                 operation == FineBrushOperation.Erase) &&
                _depthCapture.FineSurfaceTargetCompletedSequence <=
                    _fineMinimumSurfaceTargetSequence)
                return false;
            cursorOnSurface = true;
            return FineBrushDescriptor.TryCreate(cursorPosition,
                surfaceNormal, rayDirection, fineBrushRadius, fineToolLength,
                operation,
                out descriptor);
        }

        private void ArmNextObservation() =>
            _depthCapture.RequestNextDepthFrame();

        private void LogRgbdPairing()
        {
            float now = Time.unscaledTime;
            if (now - _lastRgbdLogTime < 5f) return;
            _lastRgbdLogTime = now;
            Logger.Info("TrueStereo RGB-D: " +
                        $"paired={_acceptedRgbdObservations}, " +
                        $"expiredDepth={_expiredDepthFrames}, " +
                        $"maxSkewMs={_maximumRgbdSkewSeconds * 1000.0:F2}, " +
                        $"deltaLms={_leftDepthDeltaMilliseconds:F2}, " +
                        $"deltaRms={_rightDepthDeltaMilliseconds:F2}, " +
                        $"centerOffsetMs={_pairCenterOffsetMilliseconds:F2}, " +
                        $"centerDriftMs=" +
                        $"{(_pairCenterOffsetMilliseconds - _firstPairCenterOffsetMilliseconds):F2}, " +
                        $"clockUncertaintyMs=" +
                        $"{_depthCapture.TimestampMappingUncertaintySeconds * 1000.0:F2}");
        }

        private void RecordRgbdClockEvidence(double depthUnixSeconds,
            StereoCameraFrame frame)
        {
            _leftDepthDeltaMilliseconds =
                (frame.Left.TimestampUnixSeconds - depthUnixSeconds) * 1000.0;
            _rightDepthDeltaMilliseconds =
                (frame.Right.TimestampUnixSeconds - depthUnixSeconds) * 1000.0;
            _pairCenterOffsetMilliseconds =
                (_leftDepthDeltaMilliseconds + _rightDepthDeltaMilliseconds) *
                0.5;
            if (_hasPairClockBaseline) return;
            _firstPairCenterOffsetMilliseconds =
                _pairCenterOffsetMilliseconds;
            _hasPairClockBaseline = true;
        }

        private void OnIntegrated()
        {
            Integrated?.Invoke();
        }

        private void OnAuthorityChanged()
        {
            _persistence?.MarkDirty();
        }

        private void OnFineErased()
        {
            _fineEraseDescriptor = default;
            Integrated?.Invoke();
        }

        internal void MarkDesignDirty() => _persistence?.MarkDirty();

        private static bool SaveOpenDesign()
        {
            MerkabaArtifactViewer viewer =
                FindAnyObjectByType<MerkabaArtifactViewer>();
            return viewer == null || !viewer.IsOpen || viewer.SaveDesign();
        }

        private static bool CloseOpenDesignForSessionSwitch()
        {
            MerkabaArtifactViewer viewer =
                FindAnyObjectByType<MerkabaArtifactViewer>();
            if (viewer == null || !viewer.IsOpen) return true;
            if (!viewer.SaveDesign()) return false;
            viewer.Close();
            return true;
        }

        internal Task<bool> QuiesceScanningAsync()
        {
            if (_quiesceTask != null && !_quiesceTask.IsCompleted)
                return _quiesceTask;
            if (ScanLifecycle == ScanLifecycleState.Stopped &&
                !IsScanning && !(_integrator?.HasPendingObservation ?? false) &&
                !(_integrator?.HasPendingFineErase ?? false) &&
                !(_grid?.HasObservationDurableCut ?? false))
                return Task.FromResult(true);
            _quiesceTask = QuiesceCoreAsync();
            return _quiesceTask;
        }

        private async Task<bool> QuiesceCoreAsync()
        {
            NextLifecycleGeneration();
            ScanLifecycle = ScanLifecycleState.Quiescing;
            IsScanning = false;
            _depthCapture?.BeginQuiesceDepthCapture();
            _cameraProvider?.BeginSnapshotQuiesce();
            _integrator?.BeginObservationQuiesce();
            try
            {
                // A pressure-triggered cut owns the canonical source until
                // its existing drain finishes. Keep GPU submission available
                // before asking the held observation to resume its work.
                if (!ReferenceEquals(_grid, null))
                    await _grid.FinishObservationDurableCutAsync();
                if (!ReferenceEquals(_integrator, null))
                    await _integrator.FinishCurrentFineEraseAsync();
                if (!ReferenceEquals(_integrator, null))
                    await _integrator.FinishCurrentObservationAsync();
                // The retiring observation may itself have triggered a cut.
                if (!ReferenceEquals(_grid, null))
                    await _grid.FinishObservationDurableCutAsync();
                Task depthRetirement = !ReferenceEquals(_depthCapture, null)
                    ? _depthCapture.RetireSubmittedDepthCopiesAsync()
                    : Task.CompletedTask;
                Task cameraRetirement = !ReferenceEquals(_integrator, null)
                    ? _integrator.RetireSubmittedCameraCopiesAsync()
                    : Task.CompletedTask;
                Task pcaHistoryRetirement =
                    !ReferenceEquals(_cameraProvider, null)
                        ? _cameraProvider.RetireSubmittedSnapshotCopiesAsync()
                        : Task.CompletedTask;
                await Task.WhenAll(depthRetirement, cameraRetirement,
                    pcaHistoryRetirement);
                _depthCapture?.CompleteDepthCaptureStop();
                _cameraProvider?.StopCapture();
                ScanLifecycle = ScanLifecycleState.Stopped;
                ScanStopped?.Invoke();
                Logger.Info("Merkaba scanning stopped after observation and " +
                            "capture-copy retirement");
                return true;
            }
            catch (Exception exception)
            {
                // Fail safe: the callbacks are detached, but producer-owned GPU
                // resources remain alive because their retirement was not proven.
                Logger.Error("Merkaba quiesce failed; capture providers retained: " +
                             exception);
                return false;
            }
        }

        private async Task ApplyApplicationPauseAsync(Task prior, bool paused)
        {
            try
            {
                await prior;
                if (paused)
                {
                    if (!await QuiesceScanningAsync()) return;
                    _depthCapture?.SuspendEnvironmentDepthForApplicationPause();
                    return;
                }
                if (_applicationPaused || _disableRequested || _destroyed ||
                    !isActiveAndEnabled)
                    return;
                // Sensor lifecycle is independent of whether a saved/live
                // anchor can be localized. Failure must still permit NEW.
                if (_depthCapture != null &&
                    !await _depthCapture.RestoreEnvironmentDepthAfterApplicationResumeAsync())
                {
                    LastScanStartError = "Environment Depth did not recover after wake.";
                    Logger.Error(LastScanStartError);
                    _resumeAfterPause = false;
                    return;
                }
                if (_applicationPaused || _disableRequested || _destroyed || !isActiveAndEnabled)
                    return;
                if (_resumeAfterPause)
                {
                    if (!await WaitForTrackedHeadPoseAsync())
                    {
                        _resumeAfterPause = false;
                        LastScanStartError = "Room anchor not localized";
                        Logger.Error("Application resume did not recover Quest " +
                            "head tracking before anchor localization.");
                        return;
                    }
                    if (_resumeAnchorUuid == Guid.Empty ||
                        _persistence == null ||
                        _persistence.ActiveAnchorUuid != _resumeAnchorUuid)
                    {
                        _resumeAfterPause = false;
                        LastScanStartError = "Room anchor not localized";
                        Logger.Error("Application resume has no exact active " +
                            "session anchor to localize.");
                        return;
                    }
                    _anchorManager ??= RoomAnchorManager.Instance ??
                        FindAnyObjectByType<RoomAnchorManager>(
                            FindObjectsInactive.Include);
                    if (_anchorManager == null || !_anchorManager.enabled ||
                        !await _anchorManager.EnsureSessionAnchorAsync(
                            _resumeAnchorUuid, false) ||
                        _anchorManager.SpatialAnchorUuid != _resumeAnchorUuid)
                    {
                        _resumeAfterPause = false;
                        if (!_applicationPaused && !_disableRequested && !_destroyed &&
                            _persistence.ActiveAnchorUuid == _resumeAnchorUuid)
                            _newLiveScanRequired = true;
                        LastScanStartError = "Room anchor did not recover. Start begins a new live scan; saved sessions are kept.";
                        Logger.Error("Application resume could not localize " +
                            $"session anchor {_resumeAnchorUuid:D}.");
                        return;
                    }
                }
                if (_applicationPaused || _disableRequested || _destroyed || !isActiveAndEnabled)
                    return;
                if (!_resumeAfterPause) return;
                _resumeAfterPause = false;
                await StartScanningAsync();
            }
            catch (Exception exception)
            {
                Logger.Error("Application pause lifecycle failed: " + exception);
            }
        }

        private async Task<bool> WaitForTrackedHeadPoseAsync(
            float timeoutSeconds = 10f)
        {
            float deadline = Time.realtimeSinceStartup +
                Mathf.Max(0.1f, timeoutSeconds);
            while (!_applicationPaused && !_disableRequested && !_destroyed &&
                   isActiveAndEnabled)
            {
                if (HasTrackedHeadPose()) return true;
                if (Time.realtimeSinceStartup >= deadline) return false;
                await Task.Yield();
            }
            return false;
        }

        private void BeginDisableTeardown()
        {
            if (_disableTeardownTask != null && !_disableTeardownTask.IsCompleted)
                return;
            // Capture while Unity anchor objects still exist. The async drain
            // may outlive OnDestroy, but must retain this exact session frame.
            MerkabaCommitMetadata? releaseMetadata = null;
            if (!ReferenceEquals(_persistence, null) &&
                _persistence.TryCaptureCommitMetadata(out MerkabaCommitMetadata captured))
                releaseMetadata = captured;
            _renderer?.SuspendGpuSubmission();
            _disableTeardownTask = DisableTeardownCoreAsync(_pauseTransitionTask,
                releaseMetadata);
        }

        private async Task DisableTeardownCoreAsync(Task prior,
            MerkabaCommitMetadata? releaseMetadata)
        {
            try
            {
                CancelExport();
                Task<bool> export = _exportTask;
                if (export != null) await export;
                CancelNativeImport();
                Task<bool?> import = _nativeImportTask;
                if (import != null) await import;
                await prior;
                if (!await QuiesceScanningAsync()) return;
                if (!ReferenceEquals(_renderer, null))
                    await _renderer.FinishCurrentReadoutAsync();
                // Storage remains able to submit its final bounded drain
                // batches until this await completes; closing submission
                // first would strand its GPU receipt/acknowledgement work.
                if (!ReferenceEquals(_grid, null))
                    await _grid.FinishObservationDurableCutAsync();
                if (!_disableRequested && !_destroyed)
                {
                    if (!_applicationPaused) _renderer?.ResumeGpuSubmission();
                    return;
                }
                MerkabaSessionOpenState releasedState = !ReferenceEquals(_persistence, null)
                    ? await _persistence.PrepareGpuReleaseAsync(releaseMetadata) : null;
                if (!_disableRequested && !_destroyed)
                {
                    if (!_applicationPaused) _renderer?.ResumeGpuSubmission();
                    return;
                }
                // Capture the exact stable set only after observation and
                // capture-copy retirement, but before the final grid marker.
                Action release = CaptureOwnedGpuResourceRelease();
                _grid?.BeginGpuSubmissionQuiesce();
                if (!ReferenceEquals(_grid, null))
                    await _grid.RetireSubmittedGpuWorkAsync();
                if (!_disableRequested && !_destroyed)
                {
                    _grid?.ResumeGpuSubmission();
                    if (!_applicationPaused) _renderer?.ResumeGpuSubmission();
                    return;
                }
                release?.Invoke();
                if (!ReferenceEquals(_persistence, null))
                    _persistence.MarkGpuWorldReleased(releasedState);
            }
            catch (Exception exception)
            {
                Logger.Error("Scanner resource teardown retained GPU resources " +
                             "because retirement was not proven: " + exception);
            }
        }

        private Action CaptureOwnedGpuResourceRelease()
        {
            Action rendererRelease = !ReferenceEquals(_renderer, null)
                ? _renderer.CaptureOwnedGpuResourceRelease() : null;
            Action depthRelease = !ReferenceEquals(_depthCapture, null)
                ? _depthCapture.CaptureOwnedGpuResourceRelease() : null;
            Action integratorRelease = !ReferenceEquals(_integrator, null)
                ? _integrator.CaptureOwnedGpuResourceRelease() : null;
            Action cameraProviderRelease =
                !ReferenceEquals(_cameraProvider, null)
                    ? _cameraProvider.CaptureOwnedGpuResourceRelease() : null;
            Action gridRelease = !ReferenceEquals(_grid, null)
                ? _grid.CaptureOwnedGpuResourceRelease() : null;
            return () =>
            {
                rendererRelease?.Invoke();
                depthRelease?.Invoke();
                integratorRelease?.Invoke();
                cameraProviderRelease?.Invoke();
                gridRelease?.Invoke();
            };
        }

        private uint NextLifecycleGeneration()
        {
            unchecked
            {
                _lifecycleGeneration++;
                if (_lifecycleGeneration == 0u) _lifecycleGeneration = 1u;
            }
            return _lifecycleGeneration;
        }

        private bool StartIsCurrent(uint generation) =>
            !ExportMutationHeld && generation == _lifecycleGeneration &&
            ScanLifecycle == ScanLifecycleState.Starting;

        internal bool TryBeginOperation(ScanOperationKind kind,
            ScanOperationStage stage, string statusText)
        {
            if (_operation.Busy) return false;
            _operationProgress.Begin(kind);
            SetOperation(new ScanOperationState(kind, stage, -1f, true,
                statusText));
            return true;
        }

        internal void ReportOperation(ScanOperationKind kind,
            ScanOperationStage stage, long completed, long total,
            string statusText)
        {
            if (!_operation.Busy || _operation.Kind != kind) return;
            float progress = _operationProgress.Report(kind, stage,
                completed, total);
            SetOperation(new ScanOperationState(kind, stage, progress, true,
                statusText));
        }

        internal void ReportOperation(ScanOperationKind kind,
            OperationWorkProgress progress) => ReportOperation(kind,
            progress.Stage, progress.Completed, progress.Total, progress.Text);

        internal void FinishOperation(ScanOperationKind kind, bool success,
            string statusText)
        {
            if (_operation.Kind != kind) return;
            if (!success && (string.IsNullOrWhiteSpace(statusText) ||
                statusText.IndexOf("fail", StringComparison.OrdinalIgnoreCase) < 0))
                statusText = "Failed: " + (string.IsNullOrWhiteSpace(statusText)
                    ? kind.ToString() : statusText);
            SetOperation(new ScanOperationState(kind, success
                    ? ScanOperationStage.Complete : ScanOperationStage.Failed,
                success ? 1f : _operationProgress.LastDeterminate, false,
                statusText));
        }

        private void SetOperation(ScanOperationState operation)
        {
            _operation = operation;
            OperationChanged?.Invoke();
        }
    }
}
