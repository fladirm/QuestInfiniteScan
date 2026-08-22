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
        private Label _rigState;
        private Label _pairing;
        private Label _fps;
        private bool _visible;
        private float _fpsWindow;
        private int _fpsFrames;
        private float _currentFps;

        public bool IsVisible => _visible;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _follower = GetComponent<DebugMenuFollower>();
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
            _follower?.SnapToView();
            RefreshStatus();
        }

        public void Hide()
        {
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
            Set(_scanState, lifecycle);
            Set(_renderState, scanner.CurrentRenderMode.ToString());
            Set(_pipeline, scanner.RuntimeStage);

            var rig = scanner.RigBridge;
            Set(_rigState, rig == null ? "missing" :
                rig.HasCoherentFrame ? $"coherent / epoch {rig.CalibrationEpoch}" :
                rig.IsCapturing ? "waiting for coherent frame" : "idle");
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
    }
}
