using System;
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

        [SerializeField, Range(5f, 30f)] private float integrationHz = 15f;
        [SerializeField, Range(0.005f, 0.05f)]
        private float maximumRgbdSkewSeconds = 1f / 30f;
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
        private float _lastIntegrationTime;
        private ScanOperationState _operation = ScanOperationState.Idle;
        private readonly ScanOperationProgressTracker _operationProgress = new();
        private Task<bool> _quiesceTask;
        private Task _pauseTransitionTask = Task.CompletedTask;
        private Task _disableTeardownTask = Task.CompletedTask;
        private uint _lifecycleGeneration;
        private bool _applicationPaused;
        private bool _resumeAfterPause;
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

        public bool IsScanning { get; private set; }
        public bool IsScanStarting => ScanLifecycle == ScanLifecycleState.Starting;
        public ScanLifecycleState ScanLifecycle { get; private set; }
        public string LastScanStartError { get; private set; }
        public int ActiveChunkCount => _grid != null ? _grid.ActiveChunkCount : 0;
        public int OccupiedKernelCount => _grid != null ? _grid.OccupiedKernelCount : 0;
        public int PublishedPrimitiveCount =>
            _renderer != null ? _renderer.VisiblePrimitiveCount : 0;
        public int VisibleChunkCount =>
            _renderer != null ? _renderer.VisibleChunkCount : 0;
        public int VisibleSurfaceKernelCount =>
            _renderer != null ? _renderer.VisibleSurfaceKernelCount : 0;
        public int IntegrationCount => _integrator != null ? _integrator.IntegrationCount : 0;
        public bool SavedSessionExists => _persistence != null && _persistence.SavedSessionExists;
        public string PersistenceStatus => _persistence?.LastStatus ?? "Unavailable";
        public string ExportStatus => _exporter?.LastStatus ?? "Unavailable";
        public bool IsBusy => ScanLifecycle == ScanLifecycleState.Quiescing ||
                              _operation.Busy || (_persistence?.IsBusy ?? false) ||
                              (_exporter?.IsExporting ?? false);
        public ScanOperationState CurrentOperation => _operation;
        public float ScanOpacity
        {
            get => _renderer != null ? _renderer.ScanOpacity : 1f;
            set { if (_renderer != null) _renderer.ScanOpacity = value; }
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
            _integrator.Integrated += OnIntegrated;
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
            if (!IsScanning) return;
            LogRgbdPairing();
            _integrator.TryRetireObservationAttempt();
            if (_integrator.HasPendingObservation)
            {
                if (!_integrator.HasAttemptInFlight)
                    _integrator.TrySubmitObservationAttempt();
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

        private void OnEnable() => _disableRequested = false;

        private void OnApplicationPause(bool paused)
        {
            if (paused && !_applicationPaused)
                _resumeAfterPause = IsScanning || IsScanStarting;
            _applicationPaused = paused;
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
            if (Instance == this) Instance = null;
        }

        public async Task StartScanningAsync()
        {
            if (IsScanning || IsScanStarting) return;
            if (_disableTeardownTask != null && !_disableTeardownTask.IsCompleted)
                await _disableTeardownTask;
            if (_destroyed || _disableRequested || !isActiveAndEnabled) return;
            _grid?.ResumeGpuSubmission();
            _renderer?.ResumeGpuSubmission();
            if (ScanLifecycle == ScanLifecycleState.Quiescing &&
                !await QuiesceScanningAsync())
                return;
            uint generation = NextLifecycleGeneration();
            ScanLifecycle = ScanLifecycleState.Starting;
            LastScanStartError = null;
            try
            {
                await EnsureRoomAnchorAsync();
                if (!StartIsCurrent(generation)) return;
                _grid.EnsureGpuResources();
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
                _depthCapture.StartDepthCapture();
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

        public async Task<bool> SaveAsync()
        {
            if (IsBusy) return false;
            if (!TryBeginOperation(ScanOperationKind.Save,
                    ScanOperationStage.SynchronizingScan,
                    "Retiring current scan observation")) return false;
            bool success = false;
            try
            {
                if (!await QuiesceScanningAsync()) return false;
                ReportOperation(ScanOperationKind.Save,
                    ScanOperationStage.SynchronizingScan, 1L, 1L,
                    "Scan synchronized");
                success = _persistence != null && await _persistence.SaveAsync();
                return success;
            }
            finally
            {
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
                if (!await QuiesceScanningAsync()) return false;
                ReportOperation(ScanOperationKind.Load,
                    ScanOperationStage.SynchronizingScan, 1L, 1L,
                    "Scan synchronized");
                success = _persistence != null && await _persistence.LoadAsync();
                if (success) _renderer?.MarkCanonicalReadoutDirty();
                return success;
            }
            finally
            {
                FinishOperation(ScanOperationKind.Load, success,
                    _persistence?.LastStatus ?? "Load unavailable");
            }
        }

        public async Task NewClearAsync()
        {
            if (!await QuiesceScanningAsync()) return;
            _integrator?.Clear();
            _persistence?.ClearSavedSession();
            _exporter?.ClearExport();
            await Task.Yield();
            Logger.Info("Started a new empty Merkaba session");
        }

        public async Task<bool> ExportGlbAsync()
        {
            if (IsBusy) return false;
            if (!TryBeginOperation(ScanOperationKind.ExportGlb,
                    ScanOperationStage.SynchronizingScan,
                    "Retiring current scan observation")) return false;
            bool success = false;
            try
            {
                if (!await QuiesceScanningAsync()) return false;
                ReportOperation(ScanOperationKind.ExportGlb,
                    ScanOperationStage.SynchronizingScan, 1L, 1L,
                    "Scan synchronized");
                success = _exporter != null && await _exporter.ExportGlbAsync();
                return success;
            }
            finally
            {
                FinishOperation(ScanOperationKind.ExportGlb, success,
                    _exporter?.LastStatus ?? "Export unavailable");
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
            if (_anchorManager == null || !_anchorManager.enabled) return;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!_anchorManager.IsRoomLoaded && Time.realtimeSinceStartup < deadline)
                await Task.Yield();
            if (!_anchorManager.IsRoomLoaded)
            {
                Logger.Warning("MRUK room load timed out; using the current world frame.");
                return;
            }
            if (!_anchorManager.HasSpatialAnchor)
            {
                Vector3 position = Camera.main != null
                    ? Camera.main.transform.position : Vector3.zero;
                var anchor = await _anchorManager.CreateAndSaveSpatialAnchorAsync(
                    position, Quaternion.identity);
                if (!anchor.HasValue)
                {
                    Logger.Warning("Spatial anchor creation failed; using MRUK/world fallback.");
                    return;
                }
            }
            if (RoomSpaceRoot.Instance != null)
                await RoomSpaceRoot.WaitForBindAsync(5f);
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
            _renderer?.MarkCanonicalReadoutDirty();
            Integrated?.Invoke();
        }

        internal Task<bool> QuiesceScanningAsync()
        {
            if (_quiesceTask != null && !_quiesceTask.IsCompleted)
                return _quiesceTask;
            if (ScanLifecycle == ScanLifecycleState.Stopped &&
                !IsScanning && !(_integrator?.HasPendingObservation ?? false))
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
                if (!ReferenceEquals(_integrator, null))
                    await _integrator.FinishCurrentObservationAsync();
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
                    await QuiesceScanningAsync();
                    return;
                }
                if (!_resumeAfterPause || _applicationPaused ||
                    _disableRequested || _destroyed || !isActiveAndEnabled)
                    return;
                _resumeAfterPause = false;
                await StartScanningAsync();
            }
            catch (Exception exception)
            {
                Logger.Error("Application pause lifecycle failed: " + exception);
            }
        }

        private void BeginDisableTeardown()
        {
            if (_disableTeardownTask != null && !_disableTeardownTask.IsCompleted)
                return;
            _renderer?.SuspendGpuSubmission();
            _disableTeardownTask = DisableTeardownCoreAsync(_pauseTransitionTask);
        }

        private async Task DisableTeardownCoreAsync(Task prior)
        {
            try
            {
                await prior;
                if (!await QuiesceScanningAsync()) return;
                // Capture the exact stable set only after observation and
                // capture-copy retirement, but before the final grid marker.
                Action release = CaptureOwnedGpuResourceRelease();
                _grid?.BeginGpuSubmissionQuiesce();
                if (!ReferenceEquals(_grid, null))
                    await _grid.RetireSubmittedGpuWorkAsync();
                release?.Invoke();
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
            generation == _lifecycleGeneration &&
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
