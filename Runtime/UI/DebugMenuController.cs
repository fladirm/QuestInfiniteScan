using UnityEngine;
using UnityEngine.UIElements;

namespace Genesis.RoomScan.UI
{
    /// <summary>Donor-styled task panel bound only to the simple Merkaba scanner.</summary>
    [RequireComponent(typeof(UIDocument), typeof(DebugMenuFollower))]
    public sealed class DebugMenuController : MonoBehaviour
    {
        private static readonly string[] SpinnerFrames = { "|", "/", "—", "\\" };

        private UIDocument _document;
        private DebugMenuFollower _follower;
        private VisualElement _root;
        private VisualElement _boundRoot;
        private Button _start, _stop, _save, _load, _new, _export,
            _exportTiles, _fine, _readout, _mesh, _occlusion, _checker;
        private Label _scanning, _chunks, _kernels, _visibleBoundary;
        private Label _saved, _exportStatus, _pointer, _fps;
        private Slider _opacity;
        private Slider _fineAngle, _fineDepth;
        private Label _opacityValue, _fineAngleValue, _fineDepthValue;
        private Label _operationSpinner, _operationStage;
        private VisualElement _operationPanel;
        private ProgressBar _operationProgress;
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
            _exportTiles = _root.Q<Button>("btn-export-tiles");
            _fine = _root.Q<Button>("btn-fine");
            _readout = _root.Q<Button>("btn-readout");
            _mesh = _root.Q<Button>("btn-mesh");
            _occlusion = _root.Q<Button>("btn-occlusion");
            _checker = _root.Q<Button>("btn-checker");
            _scanning = _root.Q<Label>("val-scanning");
            _chunks = _root.Q<Label>("val-chunks");
            _kernels = _root.Q<Label>("val-kernels");
            _visibleBoundary = _root.Q<Label>("val-visible");
            _saved = _root.Q<Label>("val-saved");
            _exportStatus = _root.Q<Label>("val-export");
            _pointer = _root.Q<Label>("val-pointer");
            _fps = _root.Q<Label>("val-fps");
            _opacity = _root.Q<Slider>("scan-opacity");
            _opacityValue = _root.Q<Label>("val-opacity");
            _fineAngle = _root.Q<Slider>("fine-angle");
            _fineDepth = _root.Q<Slider>("fine-depth");
            _fineAngleValue = _root.Q<Label>("val-fine-angle");
            _fineDepthValue = _root.Q<Label>("val-fine-depth");
            _operationPanel = _root.Q<VisualElement>("operation-panel");
            _operationSpinner = _root.Q<Label>("operation-spinner");
            _operationStage = _root.Q<Label>("operation-stage");
            _operationProgress = _root.Q<ProgressBar>("operation-progress");
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
            _exportTiles?.RegisterCallback<ClickEvent>(evt =>
                _ = RoomScanner.Instance?.ExportViewerPackageAsync());
            _fine?.RegisterCallback<ClickEvent>(evt =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner != null) scanner.FineMode = !scanner.FineMode;
            });
            _readout?.RegisterCallback<ClickEvent>(evt =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner != null)
                    scanner.ReadoutDrawEnabled = !scanner.ReadoutDrawEnabled;
            });
            _mesh?.RegisterCallback<ClickEvent>(evt =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner != null)
                    scanner.MeshReadoutEnabled = !scanner.MeshReadoutEnabled;
            });
            _occlusion?.RegisterCallback<ClickEvent>(evt =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner != null)
                    scanner.DynamicOcclusionEnabled =
                        !scanner.DynamicOcclusionEnabled;
            });
            _checker?.RegisterCallback<ClickEvent>(evt =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner != null)
                    scanner.CheckerReadoutEnabled =
                        !scanner.CheckerReadoutEnabled;
            });
            _opacity?.RegisterValueChangedCallback(evt =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner != null) scanner.ScanOpacity = evt.newValue;
            });
            _fineAngle?.RegisterValueChangedCallback(evt =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner != null) scanner.FineBrushAngle = evt.newValue;
            });
            _fineDepth?.RegisterValueChangedCallback(evt =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner != null) scanner.FineToolDepth = evt.newValue;
            });
        }

        private void RefreshStatus()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null) return;
            string state = scanner.ScanLifecycle switch
            {
                ScanLifecycleState.Starting => "Starting…",
                ScanLifecycleState.Running => "Active",
                ScanLifecycleState.Quiescing => "Quiescing…",
                _ when !string.IsNullOrEmpty(scanner.LastScanStartError) =>
                    "Failed: " + scanner.LastScanStartError,
                _ => "Stopped"
            };
            SetStatus(_scanning, state, scanner.IsScanning ? StatusKind.Good :
                scanner.IsScanStarting ? StatusKind.Warning : StatusKind.Neutral);
            Set(_chunks, scanner.ActiveChunkCount.ToString());
            Set(_kernels, scanner.PublishedPrimitiveCount.ToString());
            Set(_visibleBoundary, scanner.VisibleChunkCount.ToString());
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

            float opacity = scanner.ScanOpacity;
            _opacity?.SetValueWithoutNotify(opacity);
            Set(_opacityValue, $"{opacity * 100f:F0}%");
            _fineAngle?.SetValueWithoutNotify(scanner.FineBrushAngle);
            _fineDepth?.SetValueWithoutNotify(scanner.FineToolDepth);
            Set(_fineAngleValue, $"{scanner.FineBrushAngle:F0}°");
            Set(_fineDepthValue, $"{scanner.FineToolDepth:F2} m");
            if (_fine != null)
                _fine.text = scanner.FineMode ? "FINE  ON" : "FINE  OFF";
            if (_readout != null)
                _readout.text = scanner.ReadoutDrawEnabled
                    ? "READOUT  ON" : "READOUT  OFF";
            if (_mesh != null)
                _mesh.text = scanner.MeshReadoutEnabled
                    ? "MESH  ON" : "MESH  OFF";
            if (_occlusion != null)
                _occlusion.text = scanner.DynamicOcclusionEnabled
                    ? "OCCLUSION  ON" : "OCCLUSION  OFF";
            _checker?.EnableInClassList("checker-toggle--on",
                scanner.CheckerReadoutEnabled);

            ScanOperationState operation = scanner.CurrentOperation;
            RefreshOperation(operation);
            if (_start != null) _start.text = scanner.IsScanStarting
                ? "STARTING…" : scanner.IsScanning ? "RUNNING" : "START / RESUME";
            if (_stop != null) _stop.text = scanner.ScanLifecycle ==
                ScanLifecycleState.Quiescing ? "QUIESCING…" : "STOP";
            if (_save != null) _save.text = operation.Busy &&
                operation.Kind == ScanOperationKind.Save ? "SAVING…" : "SAVE";
            if (_load != null) _load.text = operation.Busy &&
                operation.Kind == ScanOperationKind.Load ? "LOADING…" : "LOAD";
            if (_export != null) _export.text = operation.Busy &&
                operation.Kind == ScanOperationKind.ExportGlb
                ? "EXPORTING…" : "EXPORT GLB";
            if (_exportTiles != null) _exportTiles.text = operation.Busy &&
                operation.Kind == ScanOperationKind.ExportGlb
                ? "EXPORTING…" : "EXPORT 3D TILES";

            bool busy = scanner.IsBusy;
            _start?.SetEnabled(!busy && !scanner.IsScanning && !scanner.IsScanStarting);
            _stop?.SetEnabled(!busy && (scanner.IsScanning || scanner.IsScanStarting));
            _save?.SetEnabled(!busy);
            _load?.SetEnabled(!busy && scanner.SavedSessionExists);
            _new?.SetEnabled(!busy);
            _export?.SetEnabled(!busy);
            _exportTiles?.SetEnabled(!busy);
            _fine?.SetEnabled(!busy);
            _mesh?.SetEnabled(!busy);
            _occlusion?.SetEnabled(!busy);
            _checker?.SetEnabled(!busy);
        }

        private void RefreshOperation(ScanOperationState operation)
        {
            if (_operationPanel == null) return;
            bool hasOperation = operation.Kind != ScanOperationKind.None;
            _operationPanel.style.display = hasOperation
                ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasOperation) return;

            Set(_operationStage, operation.StatusText);
            bool indeterminate = operation.IsIndeterminate;
            if (_operationSpinner != null)
            {
                _operationSpinner.style.display = indeterminate
                    ? DisplayStyle.Flex : DisplayStyle.None;
                if (indeterminate)
                {
                    _operationSpinner.text = SpinnerFrames[
                        Mathf.FloorToInt(Time.unscaledTime * 8f) & 3];
                }
            }
            if (_operationProgress != null)
            {
                _operationProgress.style.display = indeterminate
                    ? DisplayStyle.None : DisplayStyle.Flex;
                if (!indeterminate)
                {
                    float percent = operation.Progress01 * 100f;
                    _operationProgress.value = percent;
                    _operationProgress.title = $"{percent:F0}%";
                }
            }
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
