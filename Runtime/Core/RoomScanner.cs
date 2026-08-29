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
        private Task<bool> _quiesceTask;
        private uint _lifecycleGeneration;

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
            _integrator.TryRetireObservationAttempt();
            if (_integrator.HasPendingObservation)
            {
                if (!_integrator.HasAttemptInFlight)
                    _integrator.TrySubmitObservationAttempt();
                return;
            }
            if (Time.time - _lastIntegrationTime < IntegrationInterval) return;
            if (!_depthCapture.HasUnprocessedFrame) return;
            if (_integrator.TrySubmitObservationAttempt())
            {
                _lastIntegrationTime = Time.time;
                ArmNextObservation();
            }
        }

        private void OnDisable() => StopScanning();

        private void OnDestroy()
        {
            if (_integrator != null) _integrator.Integrated -= OnIntegrated;
            if (Instance == this) Instance = null;
        }

        public async Task StartScanningAsync()
        {
            if (IsScanning || IsScanStarting) return;
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
                    Logger.Warning("HEADSET_CAMERA permission denied; scanning continues without RGB.");
                else
                    _cameraProvider?.StartCapture();
                _depthCapture.StartDepthCapture();
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
            if (!await QuiesceScanningAsync()) return false;
            return _persistence != null && await _persistence.SaveAsync();
        }

        public async Task<bool> LoadAsync()
        {
            if (IsBusy) return false;
            if (!await QuiesceScanningAsync()) return false;
            return _persistence != null && await _persistence.LoadAsync();
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
            if (!await QuiesceScanningAsync()) return false;
            return _exporter != null && await _exporter.ExportGlbAsync();
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

        private void ProvideColorFrame()
        {
            if (_cameraProvider != null && _cameraProvider.IsReady)
            {
                Texture frame = _cameraProvider.CurrentFrame;
                if (frame != null)
                {
                    Pose pose = _depthCapture.TrackingToWorld(_cameraProvider.CameraPose);
                    _integrator.SetCameraData(frame, pose.position, pose.rotation,
                        _cameraProvider.FocalLength, _cameraProvider.PrincipalPoint,
                        _cameraProvider.SensorResolution, _cameraProvider.CurrentResolution);
                    return;
                }
            }
            _integrator.SetCameraData(null, Vector3.zero, Quaternion.identity,
                Vector2.one, Vector2.zero, Vector2.one, Vector2.one);
        }

        private void ArmNextObservation()
        {
            if (_depthCapture.RequestNextDepthFrame())
                ProvideColorFrame();
        }

        private void OnIntegrated() => Integrated?.Invoke();

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
            _integrator?.BeginObservationQuiesce();
            try
            {
                if (_integrator != null)
                    await _integrator.FinishCurrentObservationAsync();
                Task depthRetirement = _depthCapture != null
                    ? _depthCapture.RetireSubmittedDepthCopiesAsync()
                    : Task.CompletedTask;
                Task cameraRetirement = _integrator != null
                    ? _integrator.RetireSubmittedCameraCopiesAsync()
                    : Task.CompletedTask;
                await Task.WhenAll(depthRetirement, cameraRetirement);
                _depthCapture?.CompleteDepthCaptureStop();
                _cameraProvider?.StopCapture();
                MerkabaGpuTimestamps.CloseIncompleteFrame();
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
            SetOperation(new ScanOperationState(kind, stage, -1f, true,
                statusText));
            return true;
        }

        internal void ReportOperation(ScanOperationKind kind,
            ScanOperationStage stage, float progress01, string statusText)
        {
            if (!_operation.Busy || _operation.Kind != kind) return;
            SetOperation(new ScanOperationState(kind, stage, progress01, true,
                statusText));
        }

        internal void FinishOperation(ScanOperationKind kind, bool success,
            string statusText)
        {
            if (_operation.Kind != kind) return;
            SetOperation(new ScanOperationState(kind, success
                    ? ScanOperationStage.Complete : ScanOperationStage.Failed,
                success ? 1f : 0f, false, statusText));
        }

        private void SetOperation(ScanOperationState operation)
        {
            _operation = operation;
            OperationChanged?.Invoke();
        }
    }
}
