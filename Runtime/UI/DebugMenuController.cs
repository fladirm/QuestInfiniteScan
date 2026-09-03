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
            _exportTiles, _fine, _readout, _mesh, _occlusion, _checker,
            _artifactView, _artifactLoad, _annotationMode, _annotationSave,
            _annotationEdit, _annotationDelete, _tabScan, _tabPaint,
            _paintView, _paintLoad, _paintSave, _paintLine, _paintSurface,
            _paintSpatial, _paintErase;
        private Label _scanning, _chunks, _kernels, _visibleBoundary;
        private Label _saved, _exportStatus, _pointer, _fps, _proximity;
        private Label _artifactStatus;
        private Label _paintStatus, _paintWidthValue;
        private TextField _annotationNote;
        private Slider _opacity;
        private Slider _paintRed, _paintGreen, _paintBlue, _paintAlpha,
            _paintWidth;
        private Toggle _artifactWorldLock, _artifactRoomAlign;
        private Slider _fineAngle, _fineDepth;
        private Label _opacityValue, _fineAngleValue, _fineDepthValue;
        private Label _operationSpinner, _operationStage;
        private VisualElement _operationPanel;
        private VisualElement _scanPanel, _paintPanel, _paintColorSwatch;
        private ProgressBar _operationProgress;
        private ControllerRayDriver _rayDriver;
        private MerkabaArtifactViewer _artifactViewer;
        private bool _visible;
        private float _fpsWindow;
        private int _fpsFrames;
        private float _currentFps;
        private float _nextProximityRefresh;
        private string _proximityText = "No stored region";
        private StatusKind _proximityKind = StatusKind.Neutral;
        private bool _paintTabSelected;

        public bool IsVisible => _visible;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _follower = GetComponent<DebugMenuFollower>();
            _rayDriver = FindAnyObjectByType<ControllerRayDriver>();
            _artifactViewer = FindAnyObjectByType<MerkabaArtifactViewer>();
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
            _artifactView = _root.Q<Button>("btn-artifact-view");
            _artifactLoad = _root.Q<Button>("btn-artifact-load");
            _annotationMode = _root.Q<Button>("btn-annotation-mode");
            _annotationSave = _root.Q<Button>("btn-annotation-save");
            _annotationEdit = _root.Q<Button>("btn-annotation-edit");
            _annotationDelete = _root.Q<Button>("btn-annotation-delete");
            _tabScan = _root.Q<Button>("btn-tab-scan");
            _tabPaint = _root.Q<Button>("btn-tab-paint");
            _paintView = _root.Q<Button>("btn-paint-view");
            _paintLoad = _root.Q<Button>("btn-paint-load");
            _paintSave = _root.Q<Button>("btn-paint-save");
            _paintLine = _root.Q<Button>("btn-paint-line");
            _paintSurface = _root.Q<Button>("btn-paint-surface");
            _paintSpatial = _root.Q<Button>("btn-paint-spatial");
            _paintErase = _root.Q<Button>("btn-paint-erase");
            _artifactWorldLock = _root.Q<Toggle>("artifact-world-lock");
            _artifactRoomAlign = _root.Q<Toggle>("artifact-room-align");
            _annotationNote = _root.Q<TextField>("annotation-note");
            _scanning = _root.Q<Label>("val-scanning");
            _chunks = _root.Q<Label>("val-chunks");
            _kernels = _root.Q<Label>("val-kernels");
            _visibleBoundary = _root.Q<Label>("val-visible");
            _saved = _root.Q<Label>("val-saved");
            _proximity = _root.Q<Label>("val-proximity");
            _exportStatus = _root.Q<Label>("val-export");
            _pointer = _root.Q<Label>("val-pointer");
            _fps = _root.Q<Label>("val-fps");
            _artifactStatus = _root.Q<Label>("val-artifact");
            _paintStatus = _root.Q<Label>("val-paint-status");
            _paintWidthValue = _root.Q<Label>("val-paint-width");
            _scanPanel = _root.Q<VisualElement>("scan-panel");
            _paintPanel = _root.Q<VisualElement>("paint-panel");
            _paintColorSwatch = _root.Q<VisualElement>("paint-color-swatch");
            _opacity = _root.Q<Slider>("scan-opacity");
            _opacityValue = _root.Q<Label>("val-opacity");
            _fineAngle = _root.Q<Slider>("fine-angle");
            _fineDepth = _root.Q<Slider>("fine-depth");
            _paintRed = _root.Q<Slider>("paint-red");
            _paintGreen = _root.Q<Slider>("paint-green");
            _paintBlue = _root.Q<Slider>("paint-blue");
            _paintAlpha = _root.Q<Slider>("paint-alpha");
            _paintWidth = _root.Q<Slider>("paint-width");
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
            _artifactView?.RegisterCallback<ClickEvent>(evt =>
            {
                _artifactViewer ??=
                    FindAnyObjectByType<MerkabaArtifactViewer>();
                if (_artifactViewer != null) _ = _artifactViewer.ToggleAsync();
            });
            _artifactLoad?.RegisterCallback<ClickEvent>(evt =>
            {
                _artifactViewer ??=
                    FindAnyObjectByType<MerkabaArtifactViewer>();
                _artifactViewer?.RequestPackageFromDisk();
            });
            _annotationMode?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.CycleAnnotationMode());
            _annotationSave?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.SaveAnnotations());
            _annotationEdit?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.BeginNoteEdit());
            _annotationDelete?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.DeleteSelectedAnnotation());
            _tabScan?.RegisterCallback<ClickEvent>(evt => SetPaintTab(false));
            _tabPaint?.RegisterCallback<ClickEvent>(evt => SetPaintTab(true));
            _paintView?.RegisterCallback<ClickEvent>(evt =>
            {
                _artifactViewer ??=
                    FindAnyObjectByType<MerkabaArtifactViewer>();
                if (_artifactViewer != null) _ = _artifactViewer.ToggleAsync();
            });
            _paintLoad?.RegisterCallback<ClickEvent>(evt =>
            {
                _artifactViewer ??=
                    FindAnyObjectByType<MerkabaArtifactViewer>();
                _artifactViewer?.RequestPackageFromDisk();
            });
            _paintSave?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.SaveAnnotations());
            _paintLine?.RegisterCallback<ClickEvent>(evt =>
                SetPaintTool(MerkabaArtifactPaintTool.Line));
            _paintSurface?.RegisterCallback<ClickEvent>(evt =>
                SetPaintTool(MerkabaArtifactPaintTool.SurfaceBrush));
            _paintSpatial?.RegisterCallback<ClickEvent>(evt =>
                SetPaintTool(MerkabaArtifactPaintTool.SpatialBrush));
            _paintErase?.RegisterCallback<ClickEvent>(evt =>
                SetPaintTool(MerkabaArtifactPaintTool.Erase));
            _artifactWorldLock?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.WorldLocked = evt.newValue;
            });
            _artifactRoomAlign?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.RoomAligned = evt.newValue;
            });
            _annotationNote?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.SelectedNote = evt.newValue;
            });
            _opacity?.RegisterValueChangedCallback(evt =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner != null) scanner.ScanOpacity = evt.newValue;
                if (_artifactViewer != null)
                    _artifactViewer.PreviewOpacity = evt.newValue;
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
            _paintRed?.RegisterValueChangedCallback(evt =>
                SetPaintColorChannel(0, evt.newValue));
            _paintGreen?.RegisterValueChangedCallback(evt =>
                SetPaintColorChannel(1, evt.newValue));
            _paintBlue?.RegisterValueChangedCallback(evt =>
                SetPaintColorChannel(2, evt.newValue));
            _paintAlpha?.RegisterValueChangedCallback(evt =>
                SetPaintColorChannel(3, evt.newValue));
            _paintWidth?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.PaintWidth = evt.newValue;
            });
        }

        private void SetPaintTab(bool paint)
        {
            _paintTabSelected = paint;
            if (_scanPanel != null)
                _scanPanel.style.display = paint
                    ? DisplayStyle.None : DisplayStyle.Flex;
            if (_paintPanel != null)
                _paintPanel.style.display = paint
                    ? DisplayStyle.Flex : DisplayStyle.None;
            _tabScan?.EnableInClassList("mode-tab--selected", !paint);
            _tabPaint?.EnableInClassList("mode-tab--selected", paint);
            if (_artifactViewer != null)
                _artifactViewer.PaintInputEnabled = paint;
            RefreshStatus();
        }

        private void SetPaintTool(MerkabaArtifactPaintTool tool)
        {
            _artifactViewer ??= FindAnyObjectByType<MerkabaArtifactViewer>();
            if (_artifactViewer != null) _artifactViewer.PaintTool = tool;
        }

        private void SetPaintColorChannel(int channel, float value)
        {
            _artifactViewer ??= FindAnyObjectByType<MerkabaArtifactViewer>();
            if (_artifactViewer == null) return;
            Color color = _artifactViewer.PaintColor;
            color[channel] = Mathf.Clamp01(value);
            _artifactViewer.PaintColor = color;
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
            if (Time.unscaledTime >= _nextProximityRefresh)
            {
                _nextProximityRefresh = Time.unscaledTime + 0.25f;
                Camera camera = Camera.main;
                if (camera != null && scanner.TryGetStoredScanProximity(
                        camera.transform.position, out Vector3 direction,
                        out float proximityDistance))
                {
                    if (proximityDistance <= 0.05f)
                    {
                        _proximityText = "Inside stored scan";
                        _proximityKind = StatusKind.Good;
                    }
                    else
                    {
                        Vector3 local = camera.transform.InverseTransformDirection(
                            direction);
                        string arrow = DirectionArrow(local);
                        _proximityText =
                            $"Outside · {proximityDistance:F1} m {arrow}";
                        _proximityKind = StatusKind.Warning;
                    }
                }
                else
                {
                    _proximityText = "No stored region";
                    _proximityKind = StatusKind.Neutral;
                }
            }
            SetStatus(_proximity, _proximityText, _proximityKind);
            Set(_exportStatus, scanner.ExportStatus);
            _rayDriver ??= FindAnyObjectByType<ControllerRayDriver>();
            SetStatus(_pointer, _rayDriver == null ? "Missing" :
                _rayDriver.HasTrackedPose ? "Tracked · trigger selects" :
                "Waiting for controller pose", _rayDriver == null ? StatusKind.Error :
                _rayDriver.HasTrackedPose ? StatusKind.Good : StatusKind.Warning);
            Set(_fps, $"{_currentFps:F0} FPS");
            _artifactViewer ??= FindAnyObjectByType<MerkabaArtifactViewer>();
            Set(_artifactStatus, _artifactViewer?.Status ??
                "GLB View unavailable");
            Set(_paintStatus, _artifactViewer?.Status ??
                "GLB View unavailable");

            float opacity = scanner.ScanOpacity;
            if (_artifactViewer != null)
                _artifactViewer.PreviewOpacity = opacity;
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
            if (_artifactView != null)
                _artifactView.text = _artifactViewer != null &&
                    _artifactViewer.IsOpen ? "GLB VIEW  ON" : "GLB VIEW  OFF";
            if (_annotationMode != null)
                _annotationMode.text = "NOTE  " +
                    (_artifactViewer?.AnnotationModeText ?? "OFF");
            if (_annotationNote != null)
                _annotationNote.SetValueWithoutNotify(
                    _artifactViewer?.SelectedNote ?? string.Empty);
            _artifactWorldLock?.SetValueWithoutNotify(
                _artifactViewer?.WorldLocked ?? true);
            _artifactRoomAlign?.SetValueWithoutNotify(
                _artifactViewer?.RoomAligned ?? false);
            RefreshPaintControls();

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

            bool reviewing = _artifactViewer?.IsOpen ?? false;
            bool operationBusy = scanner.IsBusy;
            bool busy = operationBusy || reviewing;
            _start?.SetEnabled(!busy && !scanner.IsScanning &&
                !scanner.IsScanStarting);
            _stop?.SetEnabled(!busy && (scanner.IsScanning ||
                scanner.IsScanStarting));
            _save?.SetEnabled(!busy);
            _load?.SetEnabled(!busy && scanner.SavedSessionExists);
            _new?.SetEnabled(!busy);
            _export?.SetEnabled(!busy);
            _exportTiles?.SetEnabled(!busy);
            _fine?.SetEnabled(!busy);
            _mesh?.SetEnabled(!busy);
            _occlusion?.SetEnabled(!busy);
            _checker?.SetEnabled(!busy);
            _artifactView?.SetEnabled(!operationBusy && _artifactViewer != null);
            _artifactLoad?.SetEnabled(!operationBusy &&
                _artifactViewer != null);
            _annotationMode?.SetEnabled(!operationBusy && reviewing);
            _annotationSave?.SetEnabled(!operationBusy && reviewing);
            _annotationNote?.SetEnabled(!operationBusy && reviewing);
            _annotationEdit?.SetEnabled(!operationBusy && reviewing &&
                (_artifactViewer?.HasSelectedAnnotation ?? false));
            _annotationDelete?.SetEnabled(!operationBusy && reviewing &&
                (_artifactViewer?.HasSelectedAnnotation ?? false));
            _artifactWorldLock?.SetEnabled(!operationBusy && reviewing);
            _artifactRoomAlign?.SetEnabled(!operationBusy && reviewing);
            _paintView?.SetEnabled(!operationBusy && _artifactViewer != null);
            _paintLoad?.SetEnabled(!operationBusy && _artifactViewer != null);
            _paintSave?.SetEnabled(!operationBusy && reviewing);
            _paintLine?.SetEnabled(!operationBusy && reviewing);
            _paintSurface?.SetEnabled(!operationBusy && reviewing);
            _paintSpatial?.SetEnabled(!operationBusy && reviewing);
            _paintErase?.SetEnabled(!operationBusy && reviewing);
        }

        private void RefreshPaintControls()
        {
            if (_artifactViewer == null) return;
            _artifactViewer.PaintInputEnabled = _paintTabSelected;
            Color color = _artifactViewer.PaintColor;
            _paintRed?.SetValueWithoutNotify(color.r);
            _paintGreen?.SetValueWithoutNotify(color.g);
            _paintBlue?.SetValueWithoutNotify(color.b);
            _paintAlpha?.SetValueWithoutNotify(color.a);
            _paintWidth?.SetValueWithoutNotify(_artifactViewer.PaintWidth);
            Set(_paintWidthValue, $"{_artifactViewer.PaintWidth * 1000f:F0} mm");
            if (_paintColorSwatch != null)
                _paintColorSwatch.style.backgroundColor = color;
            MerkabaArtifactPaintTool tool = _artifactViewer.PaintTool;
            _paintLine?.EnableInClassList("paint-tool--selected",
                tool == MerkabaArtifactPaintTool.Line);
            _paintSurface?.EnableInClassList("paint-tool--selected",
                tool == MerkabaArtifactPaintTool.SurfaceBrush);
            _paintSpatial?.EnableInClassList("paint-tool--selected",
                tool == MerkabaArtifactPaintTool.SpatialBrush);
            _paintErase?.EnableInClassList("paint-tool--selected",
                tool == MerkabaArtifactPaintTool.Erase);
            if (_paintView != null)
                _paintView.text = _artifactViewer.IsOpen
                    ? "GLB VIEW  ON" : "GLB VIEW  OFF";
        }

        private static string DirectionArrow(Vector3 cameraLocalDirection)
        {
            if (Mathf.Abs(cameraLocalDirection.y) > Mathf.Max(
                    Mathf.Abs(cameraLocalDirection.x),
                    Mathf.Abs(cameraLocalDirection.z)))
                return cameraLocalDirection.y >= 0f ? "⇧" : "⇩";
            float angle = Mathf.Atan2(cameraLocalDirection.x,
                cameraLocalDirection.z) * Mathf.Rad2Deg;
            int sector = Mathf.RoundToInt(angle / 45f) & 7;
            return sector switch
            {
                0 => "↑",
                1 => "↗",
                2 => "→",
                3 => "↘",
                4 => "↓",
                5 => "↙",
                6 => "←",
                _ => "↖"
            };
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
                _operationProgress.style.display = DisplayStyle.Flex;
                if (indeterminate)
                {
                    _operationProgress.value = Mathf.PingPong(
                        Time.unscaledTime * 55f, 100f);
                    _operationProgress.title = "WORKING";
                }
                else
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
