using UnityEngine;
using UnityEngine.UIElements;

namespace Genesis.RoomScan.UI
{
    /// <summary>Donor-styled task panel bound only to the simple Merkaba scanner.</summary>
    [RequireComponent(typeof(UIDocument), typeof(DebugMenuFollower))]
    public sealed class DebugMenuController : MonoBehaviour
    {
        private UIDocument _document;
        private DebugMenuFollower _follower;
        private VisualElement _root;
        private VisualElement _boundRoot;
        private Button _start, _stop, _save, _load, _new, _export;
        private Label _scanning, _chunks, _kernels, _visibleBoundary;
        private Label _saved, _exportStatus, _pointer, _fps;
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
            if (_visible) RefreshStatus();
        }

        public void Toggle() { if (_visible) Hide(); else Show(); }

        public void Show()
        {
            _visible = true;
            _root.style.display = DisplayStyle.Flex;
            _follower?.SnapToLeftController();
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
            _start = _root.Q<Button>("btn-start");
            _stop = _root.Q<Button>("btn-stop");
            _save = _root.Q<Button>("btn-save");
            _load = _root.Q<Button>("btn-load");
            _new = _root.Q<Button>("btn-new");
            _export = _root.Q<Button>("btn-export");
            _scanning = _root.Q<Label>("val-scanning");
            _chunks = _root.Q<Label>("val-chunks");
            _kernels = _root.Q<Label>("val-kernels");
            _visibleBoundary = _root.Q<Label>("val-visible");
            _saved = _root.Q<Label>("val-saved");
            _exportStatus = _root.Q<Label>("val-export");
            _pointer = _root.Q<Label>("val-pointer");
            _fps = _root.Q<Label>("val-fps");
        }

        private void Bind()
        {
            _start?.RegisterCallback<ClickEvent>(evt =>
                _ = RoomScanner.Instance?.StartScanningAsync());
            _stop?.RegisterCallback<ClickEvent>(evt => RoomScanner.Instance?.StopScanning());
            _save?.RegisterCallback<ClickEvent>(evt => _ = RoomScanner.Instance?.SaveAsync());
            _load?.RegisterCallback<ClickEvent>(evt => _ = RoomScanner.Instance?.LoadAsync());
            _new?.RegisterCallback<ClickEvent>(evt => _ = RoomScanner.Instance?.NewClearAsync());
            _export?.RegisterCallback<ClickEvent>(evt => _ = RoomScanner.Instance?.ExportGlbAsync());
        }

        private void RefreshStatus()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null) return;
            string state = scanner.ScanLifecycle switch
            {
                ScanLifecycleState.Starting => "Starting…",
                ScanLifecycleState.Running => "Active",
                ScanLifecycleState.Stopping => "Stopping…",
                _ when !string.IsNullOrEmpty(scanner.LastScanStartError) =>
                    "Failed: " + scanner.LastScanStartError,
                _ => "Stopped"
            };
            SetStatus(_scanning, state, scanner.IsScanning ? StatusKind.Good :
                scanner.IsScanStarting ? StatusKind.Warning : StatusKind.Neutral);
            Set(_chunks, scanner.ActiveChunkCount.ToString());
            Set(_kernels, scanner.OccupiedKernelCount.ToString());
            Set(_visibleBoundary, scanner.VisibleSurfaceKernelCount.ToString());
            SetStatus(_saved, scanner.SavedSessionExists
                ? scanner.PersistenceStatus : "No saved session",
                scanner.SavedSessionExists ? StatusKind.Good : StatusKind.Neutral);
            Set(_exportStatus, scanner.ExportStatus);
            _rayDriver ??= FindAnyObjectByType<ControllerRayDriver>();
            SetStatus(_pointer, _rayDriver == null ? "Missing" :
                _rayDriver.HasTrackedPose ? "Tracked · trigger selects" :
                "Waiting for controller pose", _rayDriver == null ? StatusKind.Error :
                _rayDriver.HasTrackedPose ? StatusKind.Good : StatusKind.Warning);
            Set(_fps, $"{_currentFps:F0} FPS");

            bool busy = scanner.IsBusy;
            _start?.SetEnabled(!busy && !scanner.IsScanning && !scanner.IsScanStarting);
            _stop?.SetEnabled(!busy && (scanner.IsScanning || scanner.IsScanStarting));
            _save?.SetEnabled(!busy && scanner.ActiveChunkCount > 0);
            _load?.SetEnabled(!busy && scanner.SavedSessionExists);
            _new?.SetEnabled(!busy);
            _export?.SetEnabled(!busy && scanner.ActiveChunkCount > 0);
        }

        private static void Set(Label label, string text)
        {
            if (label != null) label.text = text ?? string.Empty;
        }

        private static void SetStatus(Label label, string text, StatusKind kind)
        {
            if (label == null) return;
            label.text = text ?? string.Empty;
            label.RemoveFromClassList("status-val--good");
            label.RemoveFromClassList("status-val--warning");
            label.RemoveFromClassList("status-val--error");
            if (kind == StatusKind.Good) label.AddToClassList("status-val--good");
            else if (kind == StatusKind.Warning) label.AddToClassList("status-val--warning");
            else if (kind == StatusKind.Error) label.AddToClassList("status-val--error");
        }

        private enum StatusKind { Neutral, Good, Warning, Error }
    }
}
