using Genesis.RoomScan.SigmaPrism;
using UnityEngine;
using UnityEngine.UIElements;

namespace Genesis.RoomScan.UI
{
    /// <summary>Task-oriented Σ-PRISM operator panel; diagnostics are read-only.</summary>
    [RequireComponent(typeof(UIDocument), typeof(DebugMenuFollower))]
    public sealed class DebugMenuController : MonoBehaviour
    {
        private UIDocument _document;
        private DebugMenuFollower _follower;
        private VisualElement _root;
        private VisualElement _boundRoot;
        private Button _toggleScan;
        private Button _renderMode;
        private Button _clear;
        private Label _scanState;
        private Label _renderState;
        private Label _pipeline;
        private Label _gateState;
        private Label _carrierState;
        private Label _inverseState;
        private Label _pointerState;
        private Label _rigState;
        private Label _pairing;
        private Label _fps;
        private ControllerRayDriver _rayDriver;
        private bool _visible;
        private float _fpsWindow;
        private int _fpsFrames;
        private float _currentFps;

        public bool IsVisible => _visible;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _follower = GetComponent<DebugMenuFollower>();
            _rayDriver = FindAnyObjectByType<ControllerRayDriver>();
        }

        private void OnEnable()
        {
            _root = _document.rootVisualElement;
            _root.style.display = DisplayStyle.None;
            _visible = false;
            Query();
            if (_boundRoot != _root)
            {
                Bind();
                _boundRoot = _root;
            }
        }

        private void Update()
        {
            _fpsFrames++;
            _fpsWindow += Time.unscaledDeltaTime;
            if (_fpsWindow >= 0.5f)
            {
                _currentFps = _fpsFrames / Mathf.Max(0.001f, _fpsWindow);
                _fpsFrames = 0;
                _fpsWindow = 0f;
            }
            if (_visible)
                RefreshStatus();
        }

        public void Toggle()
        {
            if (_visible) Hide();
            else Show();
        }

        public void Show()
        {
            _visible = true;
            _root.style.display = DisplayStyle.Flex;
            _follower?.SnapToLeftController();
            RefreshStatus();
        }

        public void Hide()
        {
            // UI visibility is deliberately orthogonal to scanner lifecycle,
            // canonical state and every derived readout.
            _visible = false;
            _root.style.display = DisplayStyle.None;
            _follower?.StopTracking();
        }

        private void Query()
        {
            _toggleScan = _root.Q<Button>("btn-toggle-scan");
            _renderMode = _root.Q<Button>("btn-render-mode");
            _clear = _root.Q<Button>("btn-clear-all");
            _scanState = _root.Q<Label>("val-scanning");
            _renderState = _root.Q<Label>("val-render");
            _pipeline = _root.Q<Label>("val-pipeline");
            _gateState = _root.Q<Label>("val-gate");
            _carrierState = _root.Q<Label>("val-carrier");
            _inverseState = _root.Q<Label>("val-inverse");
            _pointerState = _root.Q<Label>("val-pointer");
            _rigState = _root.Q<Label>("val-rig");
            _pairing = _root.Q<Label>("val-pairing");
            _fps = _root.Q<Label>("val-fps");
        }

        private void Bind()
        {
            _toggleScan?.RegisterCallback<ClickEvent>(_ =>
                RoomScanner.Instance?.ToggleScanning());
            _renderMode?.RegisterCallback<ClickEvent>(_ =>
                RoomScanner.Instance?.CycleRenderMode());
            _clear?.RegisterCallback<ClickEvent>(_ =>
                RoomScanner.Instance?.ClearAllDataAsync());
        }

        private void RefreshStatus()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null)
                return;

            string lifecycle = scanner.ScanLifecycle switch
            {
                ScanLifecycleState.Starting => "Starting…",
                ScanLifecycleState.Running => "Active",
                ScanLifecycleState.Stopping => "Stopping…",
                _ when !string.IsNullOrEmpty(scanner.LastScanStartError) =>
                    "Failed: " + scanner.LastScanStartError,
                _ => "Stopped"
            };
            SetStatus(_scanState, lifecycle, scanner.ScanLifecycle switch
            {
                ScanLifecycleState.Running => StatusKind.Good,
                ScanLifecycleState.Starting or ScanLifecycleState.Stopping =>
                    StatusKind.Warning,
                _ when !string.IsNullOrEmpty(scanner.LastScanStartError) =>
                    StatusKind.Error,
                _ => StatusKind.Neutral
            });
            Set(_renderState, scanner.CurrentRenderMode.ToString());
            Set(_pipeline, scanner.RuntimeStage);

            var inverse = scanner.SigmaInverse;
            SigmaRuntimeTelemetrySnapshot telemetry =
                inverse?.RuntimeTelemetry;
            var gate = scanner.ExactBackendGate;
            SigmaExactBackendGateStatus gateStatus = gate?.DiagnosticStatus ??
                SigmaExactBackendGateStatus.Disposed;
            SetStatus(_gateState, telemetry?.HasSample == true
                ? telemetry.GateWord != 0u
                    ? $"PASS · GPU witness={telemetry.GateWord}"
                    : "FAIL-CLOSED · GPU witness=0"
                : gateStatus switch
            {
                SigmaExactBackendGateStatus.GpuResident =>
                    "GPU-resident · awaiting witness telemetry",
                SigmaExactBackendGateStatus.Disposed => "disposed",
                _ => "unavailable"
            }, telemetry?.HasSample == true
                ? telemetry.GateWord != 0u ? StatusKind.Good : StatusKind.Error
                : gateStatus switch
            {
                SigmaExactBackendGateStatus.GpuResident => StatusKind.Warning,
                _ => StatusKind.Error
            });

            var carrier = scanner.Carrier;
            string carrierText = carrier == null ? "missing" :
                !carrier.IsInitialized ? "initializing" :
                telemetry?.HasSample == true
                    ? $"root={telemetry.PublishedRoot} · " +
                      $"stateDelta={telemetry.StateDeltaCount} · " +
                      $"gaugeDelta={telemetry.GaugeDeltaCount}"
                    : "ready · awaiting GPU telemetry";
            StatusKind carrierKind = carrier == null ? StatusKind.Error :
                !carrier.IsInitialized ? StatusKind.Warning :
                telemetry?.HasSample != true ? StatusKind.Warning :
                telemetry.FaultMask == 0u ? StatusKind.Good : StatusKind.Error;
            SetStatus(_carrierState, carrierText, carrierKind);

            if (inverse == null)
                SetStatus(_inverseState, "missing", StatusKind.Error);
            else if (!inverse.IsInitialized)
                SetStatus(_inverseState, "initializing", StatusKind.Warning);
            else if (telemetry?.HasSample != true)
                SetStatus(_inverseState,
                    $"frames={inverse.CommittedFrames}/" +
                    $"{inverse.SubmittedFrames} · telemetry=" +
                    (telemetry?.Status ?? "unavailable"),
                    StatusKind.Warning);
            else
            {
                string text = $"frames={inverse.CommittedFrames}/" +
                    $"{inverse.SubmittedFrames} · root=" +
                    $"{telemetry.PublishedRoot} · stateΔ=" +
                    $"{telemetry.StateDeltaCount} · unresolved=" +
                    $"{telemetry.UnresolvedConstraintCount} · " +
                    $"dispatch={telemetry.NativeCloseDispatches} · " +
                    $"ms={telemetry.Timing.Frame.LastMs:F1} · " +
                    $"at={telemetry.Frontier}";
                SetStatus(_inverseState, text,
                    inverse.FailedFrames == 0 && telemetry.FaultMask == 0u
                        ? StatusKind.Good : StatusKind.Warning);
            }

            _rayDriver ??= FindAnyObjectByType<ControllerRayDriver>();
            SetStatus(_pointerState, _rayDriver == null ? "missing" :
                _rayDriver.HasTrackedPose ? "tracked · trigger selects" :
                "ready · waiting for controller pose", _rayDriver == null
                    ? StatusKind.Error : _rayDriver.HasTrackedPose
                        ? StatusKind.Good : StatusKind.Warning);

            var rig = scanner.RigBridge;
            SetStatus(_rigState, rig == null ? "missing" :
                rig.HasCoherentFrame ? $"coherent / epoch {rig.CalibrationEpoch}" :
                rig.IsCapturing ? "waiting for coherent frame" : "idle",
                rig == null ? StatusKind.Error : rig.HasCoherentFrame
                    ? StatusKind.Good : StatusKind.Neutral);
            if (rig != null)
            {
                var d = rig.PairingDiagnostics;
                Set(_pairing, $"accepted={d.AcceptedFrames}, rejected={d.RejectedSamples}");
            }
            else
                Set(_pairing, "--");

            if (_toggleScan != null)
            {
                _toggleScan.text = scanner.IsScanStarting ? "Starting…" :
                    scanner.IsScanning ? "Stop Scanning" : "Start Scanning";
                _toggleScan.SetEnabled(!scanner.IsScanStarting);
            }
            if (_renderMode != null)
                _renderMode.text = "Readout: " + scanner.CurrentRenderMode;
            Set(_fps, $"{_currentFps:F0} FPS");
        }

        private static void Set(Label label, string text)
        {
            if (label != null)
                label.text = text ?? string.Empty;
        }

        private static void SetStatus(Label label, string text, StatusKind kind)
        {
            if (label == null)
                return;
            label.text = text ?? string.Empty;
            label.RemoveFromClassList("status-val--good");
            label.RemoveFromClassList("status-val--warning");
            label.RemoveFromClassList("status-val--error");
            switch (kind)
            {
                case StatusKind.Good:
                    label.AddToClassList("status-val--good");
                    break;
                case StatusKind.Warning:
                    label.AddToClassList("status-val--warning");
                    break;
                case StatusKind.Error:
                    label.AddToClassList("status-val--error");
                    break;
            }
        }

        private enum StatusKind
        {
            Neutral,
            Good,
            Warning,
            Error
        }
    }
}
