using Genesis.RoomScan.Exporting;
using Genesis.RoomScan.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Genesis.RoomScan.UI
{
    /// <summary>
    /// Pure Cone-PRISM operator panel. The production UI intentionally exposes no
    /// TSDF, Surface Nets, triplanar, GSplat, DiffSoup, or legacy atlas path.
    /// </summary>
    [RequireComponent(typeof(UIDocument), typeof(DebugMenuFollower))]
    public sealed class DebugMenuController : MonoBehaviour
    {
        private UIDocument _document;
        private DebugMenuFollower _follower;
        private VisualElement _root;
        private VisualElement _boundRoot;
        private bool _visible;

        private Button _navScan;
        private Button _navWorld;
        private VisualElement _viewScan;
        private VisualElement _viewWorld;
        private Button _toggleScan;
        private Button _renderMode;
        private Button _clearAll;
        private Button _exportChunk;
        private Button _exportWorld;
        private Label _scanState;
        private Label _renderState;
        private Label _focusFrames;
        private Label _worldMode;
        private Label _worldId;
        private Label _activeChunk;
        private Label _chunkLifecycle;
        private Label _residency;
        private Label _graph;
        private Label _worldStorage;
        private Label _glbStatus;
        private Label _fps;
        private GlbExportController _glb;
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
            QueryElements();
            if (_boundRoot != _root)
            {
                BindOnce();
                _boundRoot = _root;
            }
            SelectView(_viewScan);
        }

        private void Update()
        {
            UpdateFps();
            if (_visible) RefreshStatus();
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

        private void QueryElements()
        {
            _navScan = _root.Q<Button>("nav-scan");
            _navWorld = _root.Q<Button>("nav-world");
            _viewScan = _root.Q<VisualElement>("view-scan");
            _viewWorld = _root.Q<VisualElement>("view-world");
            _toggleScan = _root.Q<Button>("btn-toggle-scan");
            _renderMode = _root.Q<Button>("btn-render-mode");
            _clearAll = _root.Q<Button>("btn-clear-all");
            _exportChunk = _root.Q<Button>("btn-export-chunk-glb");
            _exportWorld = _root.Q<Button>("btn-export-world-glb");
            _scanState = _root.Q<Label>("val-scanning");
            _renderState = _root.Q<Label>("val-render");
            _focusFrames = _root.Q<Label>("val-focus-frames");
            _worldMode = _root.Q<Label>("val-world-mode");
            _worldId = _root.Q<Label>("val-world-id");
            _activeChunk = _root.Q<Label>("val-active-chunk");
            _chunkLifecycle = _root.Q<Label>("val-chunk-lifecycle");
            _residency = _root.Q<Label>("val-residency");
            _graph = _root.Q<Label>("val-graph");
            _worldStorage = _root.Q<Label>("val-world-storage");
            _glbStatus = _root.Q<Label>("val-glb-export");
            _fps = _root.Q<Label>("val-fps");

        }

        private void BindOnce()
        {
            _navScan?.RegisterCallback<ClickEvent>(_ => SelectView(_viewScan));
            _navWorld?.RegisterCallback<ClickEvent>(_ => SelectView(_viewWorld));
            _toggleScan?.RegisterCallback<ClickEvent>(_ =>
                RoomScanner.Instance?.ToggleScanning());
            _renderMode?.RegisterCallback<ClickEvent>(_ =>
                RoomScanner.Instance?.CycleRenderMode());
            _clearAll?.RegisterCallback<ClickEvent>(_ =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner == null) return;
                _clearAll.SetEnabled(false);
                _clearAll.text = "Clearing...";
                scanner.ClearAllDataAsync(() =>
                {
                    _clearAll.text = "Clear All Data";
                    _clearAll.SetEnabled(true);
                });
            });
            _exportChunk?.RegisterCallback<ClickEvent>(async _ =>
            {
                EnsureExporter();
                if (_glb == null) return;
                _exportChunk.SetEnabled(false);
                GlbUserExportResult result = await _glb.ExportActiveChunkAsync();
                Set(_glbStatus, result.Success ? result.Path : result.Error);
                _exportChunk.SetEnabled(true);
            });
            _exportWorld?.RegisterCallback<ClickEvent>(async _ =>
            {
                EnsureExporter();
                if (_glb == null) return;
                _exportWorld.SetEnabled(false);
                GlbUserExportResult result = await _glb.ExportWorldAsync();
                Set(_glbStatus, result.Success ? result.Path : result.Error);
                _exportWorld.SetEnabled(true);
            });
        }

        private void SelectView(VisualElement selected)
        {
            if (_viewScan != null)
                _viewScan.style.display = selected == _viewScan
                    ? DisplayStyle.Flex : DisplayStyle.None;
            if (_viewWorld != null)
                _viewWorld.style.display = selected == _viewWorld
                    ? DisplayStyle.Flex : DisplayStyle.None;
            _navScan?.EnableInClassList("nav-btn--active", selected == _viewScan);
            _navWorld?.EnableInClassList("nav-btn--active", selected == _viewWorld);
        }

        private void RefreshStatus()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null) return;
            string lifecycle = scanner.ScanLifecycle switch
            {
                ScanLifecycleState.Starting => "Starting...",
                ScanLifecycleState.Running => "Active",
                ScanLifecycleState.Stopping => "Stopping...",
                _ when !string.IsNullOrEmpty(scanner.LastScanStartError) =>
                    "Start failed: " + scanner.LastScanStartError,
                _ => "Stopped"
            };
            Set(_scanState, lifecycle);
            Set(_renderState, scanner.CurrentRenderMode.ToString());
            var focus = scanner.PrismPhotometricRefiner;
            Set(_focusFrames, focus != null
                ? $"{focus.StereoFrames}/{focus.TemporalFrames}"
                : "0/0");
            if (_toggleScan != null)
            {
                _toggleScan.text = scanner.IsScanStarting ? "Starting..." :
                    scanner.IsScanning ? "Stop Scanning" : "Start Scanning";
                _toggleScan.SetEnabled(!scanner.IsScanStarting);
            }
            if (_renderMode != null)
                _renderMode.text = "Render: " + scanner.CurrentRenderMode;

            SubmapManager world = scanner.GetComponent<SubmapManager>();
            WorldManifest manifest = world?.Manifest;
            ChunkRecord chunk = world?.ActiveChunk;
            Set(_worldMode, world != null && world.LargeWorldMode
                ? "Cone-PRISM infinite" : "Unavailable");
            Set(_worldId, manifest?.worldId ?? "--");
            Set(_activeChunk, chunk?.chunkId ?? "--");
            Set(_chunkLifecycle, chunk?.state.ToString() ?? "--");
            PrismChunkResidencyManager residency = scanner.PrismChunkResidency;
            Set(_residency, residency == null ? "--" :
                $"{(residency.IsTransitioning ? "transitioning" : "resident")}, " +
                $"recent={residency.RecentCanonicalCount}");
            Set(_graph, manifest != null
                ? $"{manifest.chunks.Count} chunks / {manifest.edges.Count} edges"
                : "--");
            Set(_worldStorage, Application.persistentDataPath);

            EnsureExporter();
            bool canExport = world?.HasWorld == true && !scanner.IsScanning &&
                             _glb != null && !_glb.IsBusy;
            _exportChunk?.SetEnabled(canExport);
            _exportWorld?.SetEnabled(canExport);
            Set(_fps, $"{_currentFps:F0} FPS");
        }

        private void EnsureExporter()
        {
            if (_glb != null) return;
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner != null) _glb = scanner.GetComponent<GlbExportController>();
        }

        private void UpdateFps()
        {
            _fpsFrames++;
            _fpsWindow += Time.unscaledDeltaTime;
            if (_fpsWindow < 0.5f) return;
            _currentFps = _fpsFrames / Mathf.Max(0.001f, _fpsWindow);
            _fpsFrames = 0;
            _fpsWindow = 0f;
        }

        private static void Set(Label label, string value)
        {
            if (label != null) label.text = value ?? string.Empty;
        }
    }
}
