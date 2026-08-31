using System;
using System.Threading.Tasks;
using Genesis.RoomScan.SigmaPrism;
using Genesis.RoomScan.UI;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>Derived readout visibility; it never changes canonical carrier state.</summary>
    public enum ScanRenderMode
    {
        Carrier = 0,
        None = 1,
        Wireframe = 2
    }

    public enum ScanLifecycleState
    {
        Stopped,
        Starting,
        Running,
        Stopping
    }

    /// <summary>
    /// Representation-neutral Quest lifecycle shell for Σ-PRISM-16. It owns sensor
    /// ingress and operator UX only. Reconstruction state and decisions live under
    /// <c>Runtime/SigmaPrism</c>; no previous mapper or persistence path is reachable.
    /// </summary>
    [RequireComponent(typeof(DepthCapture))]
    [RequireComponent(typeof(RoomAnchorManager))]
    [RequireComponent(typeof(SigmaRigBridge))]
    [RequireComponent(typeof(SigmaCarrier))]
    [RequireComponent(typeof(SigmaRenderer))]
    [RequireComponent(typeof(SigmaInverseController))]
    public sealed class RoomScanner : MonoBehaviour
    {
        public static RoomScanner Instance { get; private set; }

        [Header("Derived readout")]
        [SerializeField] private ScanRenderMode renderMode = ScanRenderMode.Carrier;
        [SerializeField, Range(0.2f, 5f)] private float wireThickness = 1.5f;

        [Header("Scan cadence")]
        [SerializeField, Range(5f, 30f)] private float scanHz = 5f;

        [Header("Logging")]
        [SerializeField] private LogLevel logLevel = LogLevel.Info;

        private DepthCapture _depthCapture;
        private SigmaRigBridge _rigBridge;
        private SigmaCarrier _carrier;
        private SigmaRenderer _sigmaRenderer;
        private SigmaInverseController _sigmaInverse;
        private RoomAnchorManager _roomAnchor;
        private DebugMenuController _debugMenu;
        private IRoomScanModule[] _modules;
        private SigmaExactBackendGate _exactBackendGate;
        private Task _startTask = Task.CompletedTask;
        private uint _lifecycleGeneration;
        private bool _resourcesReleased;
        private double _nextScanAdmissionTime;

        public ScanLifecycleState ScanLifecycle { get; private set; } =
            ScanLifecycleState.Stopped;
        public bool IsScanning => ScanLifecycle is ScanLifecycleState.Starting or
                                  ScanLifecycleState.Running;
        public bool IsScanStarting => ScanLifecycle == ScanLifecycleState.Starting;
        public string LastScanStartError { get; private set; }
        public ScanRenderMode CurrentRenderMode => renderMode;
        public string RuntimeStage =>
            "S4-08 exact four-stream inverse + bounded pose gauge";
        public DepthCapture DepthCapture => _depthCapture;
        public SigmaRigBridge RigBridge => _rigBridge;
        public SigmaCarrier Carrier => _carrier;
        public SigmaRenderer SigmaRenderer => _sigmaRenderer;
        public SigmaInverseController SigmaInverse => _sigmaInverse;
        public DebugMenuController DebugMenu => _debugMenu;
        public bool ScanResourcesReleased => _resourcesReleased;
        public SigmaExactBackendGate ExactBackendGate => _exactBackendGate;

        public event Action ScanStarted;
        public event Action ScanStopped;
        public event Action<ScanRenderMode> RenderModeChanged;
        public event Action<Guid, Matrix4x4> ScanAnchorCreated;

        private static readonly int WireframeId = Shader.PropertyToID("_RSWireframe");
        private static readonly int WireThicknessId = Shader.PropertyToID("_RSWireThickness");

        private void Awake()
        {
            Instance = this;
            Logger.Level = logLevel;
            _depthCapture = GetComponent<DepthCapture>();
            _rigBridge = GetComponent<SigmaRigBridge>();
            _carrier = GetComponent<SigmaCarrier>();
            _sigmaRenderer = GetComponent<SigmaRenderer>();
            _sigmaInverse = GetComponent<SigmaInverseController>();
            _roomAnchor = GetComponent<RoomAnchorManager>();
            _debugMenu = GetComponentInChildren<DebugMenuController>(true);
            if (RoomSpaceRoot.Instance == null)
            {
                var roomSpace = new GameObject("[SigmaRoomSpace]");
                roomSpace.AddComponent<RoomSpaceRoot>();
            }
            Shader.SetGlobalFloat(WireframeId, 0f);
            Shader.SetGlobalFloat(WireThicknessId, wireThickness);
        }

        private void Start()
        {
            if (!XRRuntimeGuard.IsXRActive)
            {
                Logger.Warning("RoomScanner: " + XRRuntimeGuard.EditorDisabledMessage);
                enabled = false;
                return;
            }

            try
            {
                _ = SigmaOperatorSet.Canonical;
                _exactBackendGate = SigmaExactBackendGate.Dispatch();
            }
            catch (Exception exception)
            {
                LastScanStartError = "Exact S16 backend initialization failed: " +
                    exception.Message;
                Logger.Error(LastScanStartError);
                enabled = false;
                return;
            }

            _modules = GetComponents<IRoomScanModule>();
            foreach (IRoomScanModule module in _modules)
                module.OnModuleInitialize(this);
            Logger.Info("Σ-PRISM-16 Quest shell ready; scanner awaits Start.");
        }

        private void Update()
        {
            if (ScanLifecycle != ScanLifecycleState.Running ||
                _sigmaInverse == null || _sigmaRenderer == null ||
                !_sigmaInverse.CanAcceptScheduledObservation)
                return;

            double now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextScanAdmissionTime ||
                !_sigmaInverse.TryScheduleLatestObservation())
                return;

            // Match the donor's fixed-cadence admission: missed ticks never queue
            // or catch up. The immutable published-root draw remains XR-cadenced.
            _nextScanAdmissionTime = NextScanAdmissionTime(now, scanHz);
        }

        private void OnDisable()
        {
            StopScanning();
        }

        private void OnDestroy()
        {
            _exactBackendGate?.Dispose();
            _exactBackendGate = null;
            if (Instance == this)
                Instance = null;
        }

        public Task StartScanningAsync()
        {
            if (ScanLifecycle == ScanLifecycleState.Running)
                return Task.CompletedTask;
            if (ScanLifecycle == ScanLifecycleState.Starting)
                return _startTask;

            uint generation = ++_lifecycleGeneration;
            ScanLifecycle = ScanLifecycleState.Starting;
            LastScanStartError = null;
            _startTask = StartScanningCoreAsync(generation);
            return _startTask;
        }

        private async Task StartScanningCoreAsync(uint generation)
        {
            try
            {
                _resourcesReleased = false;
                bool cameraPermission =
                    await PassthroughCameraProvider.RequestCameraPermissionAsync();
                if (!cameraPermission)
                    throw new InvalidOperationException(
                        "HEADSET_CAMERA permission is required for RGB_L/R ingress.");

                if (generation != _lifecycleGeneration ||
                    ScanLifecycle != ScanLifecycleState.Starting)
                    return;

                if (!await EnsureScanAnchorAsync())
                    throw new InvalidOperationException(
                        "A localized spatial anchor is required before canonical scan ingress.");
                if (RoomSpaceRoot.Instance == null ||
                    !await RoomSpaceRoot.WaitForBindAsync())
                    throw new InvalidOperationException(
                        "RoomSpaceRoot did not bind to the localized scan anchor.");

                if (generation != _lifecycleGeneration ||
                    ScanLifecycle != ScanLifecycleState.Starting)
                    return;

                _depthCapture.StartDepthCapture();
                _rigBridge.StartCapture();
                if (!_rigBridge.IsCapturing)
                    throw new InvalidOperationException(
                        "Σ-PRISM-16 four-stream rig bridge did not enter capture state.");

                foreach (IRoomScanModule module in _modules ?? Array.Empty<IRoomScanModule>())
                    module.OnScanStarted();

                _nextScanAdmissionTime = Time.realtimeSinceStartupAsDouble;
                ScanLifecycle = ScanLifecycleState.Running;
                Logger.Info("StartScanning — Σ-PRISM-16 synchronized capture, exact " +
                            "dual-eye inverse and intrinsic singular topology active.");
                ScanStarted?.Invoke();
            }
            catch (Exception exception)
            {
                _rigBridge?.StopCapture();
                _depthCapture?.StopDepthCapture();
                if (generation == _lifecycleGeneration)
                {
                    ScanLifecycle = ScanLifecycleState.Stopped;
                    LastScanStartError = exception.Message;
                }
                Logger.Error("StartScanning failed: " + exception);
                throw;
            }
        }

        public void StopScanning()
        {
            if (ScanLifecycle is ScanLifecycleState.Stopped or ScanLifecycleState.Stopping)
                return;

            ++_lifecycleGeneration;
            bool hadIngress = ScanLifecycle == ScanLifecycleState.Running;
            ScanLifecycle = ScanLifecycleState.Stopping;
            _rigBridge?.StopCapture();
            _depthCapture?.StopDepthCapture();

            if (hadIngress)
            {
                foreach (IRoomScanModule module in _modules ?? Array.Empty<IRoomScanModule>())
                    module.OnScanStopped();
                ScanStopped?.Invoke();
            }

            ScanLifecycle = ScanLifecycleState.Stopped;
            _nextScanAdmissionTime = 0.0;
            Logger.Info("Σ-PRISM-16 sensor ingress stopped; no old persistence path invoked.");
        }

        internal static double NextScanAdmissionTime(double submittedAt,
            float frequencyHz) => submittedAt + 1.0 / Math.Max(1.0, frequencyHz);

        public void ToggleScanning()
        {
            if (ScanLifecycle == ScanLifecycleState.Starting)
                return;
            if (ScanLifecycle == ScanLifecycleState.Running)
                StopScanning();
            else
                ObserveStart(StartScanningAsync());
        }

        private static async void ObserveStart(Task task)
        {
            try { await task; }
            catch { /* StartScanningCoreAsync owns the surfaced error. */ }
        }

        public void ReleaseScanResources()
        {
            StopScanning();
            _depthCapture?.ReleaseResources();
            _resourcesReleased = true;
            SetRenderMode(ScanRenderMode.None);
        }

        public void ClearScan()
        {
            StopScanning();
            _resourcesReleased = true;
            SetRenderMode(ScanRenderMode.None);
        }

        public void ClearAllDataAsync(Action onComplete = null)
        {
            // S4-00 has no durable reconstruction. Exact carrier publication and
            // clearing are introduced together by S4-10, never through a fallback.
            ClearScan();
            onComplete?.Invoke();
        }

        public void SetRenderMode(ScanRenderMode mode)
        {
            renderMode = mode;
            Shader.SetGlobalFloat(WireframeId,
                mode == ScanRenderMode.Wireframe ? 1f : 0f);
            Shader.SetGlobalFloat(WireThicknessId, wireThickness);
            RenderModeChanged?.Invoke(mode);
        }

        public void CycleRenderMode()
        {
            SetRenderMode(renderMode switch
            {
                ScanRenderMode.Carrier => ScanRenderMode.Wireframe,
                ScanRenderMode.Wireframe => ScanRenderMode.None,
                _ => ScanRenderMode.Carrier,
            });
        }

        public bool IsModeAvailable(ScanRenderMode mode) =>
            mode == ScanRenderMode.None || !_resourcesReleased;

        public void ToggleDebugMenu() => _debugMenu?.Toggle();

        private async Task<bool> EnsureScanAnchorAsync()
        {
            if (_roomAnchor == null)
                return false;
            if (_roomAnchor.HasSpatialAnchor)
                return true;
            Vector3 position = Camera.main != null
                ? Camera.main.transform.position
                : Vector3.zero;
            var result = await _roomAnchor.CreateAndSaveSpatialAnchorAsync(
                position, Quaternion.identity);
            if (!result.HasValue)
                return false;
            ScanAnchorCreated?.Invoke(result.Value.uuid, result.Value.matrix);
            return true;
        }
    }
}
