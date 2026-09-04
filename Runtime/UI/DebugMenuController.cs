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
            _annotationEdit, _annotationDelete, _tabScan, _tabPaint, _tabPlan,
            _paintView, _paintLoad, _paintSave, _paintLine, _paintSurface,
            _paintSpatial, _paintErase, _planView, _planLoad, _planStyle,
            _planAnnotationMode, _planAnnotationSave, _planAnnotationEdit,
            _planAnnotationDelete;
        private Label _scanning, _chunks, _kernels, _visibleBoundary;
        private Label _saved, _exportStatus, _pointer, _fps, _proximity;
        private Label _artifactStatus;
        private Label _paintStatus, _paintWidthValue, _planStatus,
            _planOpacityValue;
        private TextField _annotationNote, _planAnnotationNote;
        private Slider _opacity;
        private Slider _paintValue, _paintAlpha, _paintWidth, _planOpacity;
        private Toggle _artifactWorldLock, _artifactRoomAlign,
            _planWorldLock, _planRoomAlign;
        private Slider _fineRadius, _fineLength;
        private Label _opacityValue, _fineRadiusValue, _fineLengthValue;
        private Label _operationSpinner, _operationStage;
        private VisualElement _operationPanel;
        private VisualElement _scanPanel, _paintPanel, _planPanel,
            _paintColorSwatch, _paintColorWheel, _paintColorCursor;
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
        private MenuTab _selectedTab;
        private Texture2D _paintWheelTexture;
        private int _paintWheelPointer = -1;
        private float _paintHue;
        private float _paintSaturation;
        private bool _paintHsvInitialized;

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
            InitializePaintColorWheel();
            if (_boundRoot != _root)
            {
                Bind();
                _boundRoot = _root;
            }
        }

        private void OnDestroy()
        {
            if (_paintWheelTexture != null)
                Destroy(_paintWheelTexture);
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
            _tabPlan = _root.Q<Button>("btn-tab-plan");
            _paintView = _root.Q<Button>("btn-paint-view");
            _paintLoad = _root.Q<Button>("btn-paint-load");
            _paintSave = _root.Q<Button>("btn-paint-save");
            _paintLine = _root.Q<Button>("btn-paint-line");
            _paintSurface = _root.Q<Button>("btn-paint-surface");
            _paintSpatial = _root.Q<Button>("btn-paint-spatial");
            _paintErase = _root.Q<Button>("btn-paint-erase");
            _planView = _root.Q<Button>("btn-plan-view");
            _planLoad = _root.Q<Button>("btn-plan-load");
            _planStyle = _root.Q<Button>("btn-plan-style");
            _planAnnotationMode = _root.Q<Button>("btn-plan-annotation-mode");
            _planAnnotationSave = _root.Q<Button>("btn-plan-annotation-save");
            _planAnnotationEdit = _root.Q<Button>("btn-plan-annotation-edit");
            _planAnnotationDelete = _root.Q<Button>("btn-plan-annotation-delete");
            _artifactWorldLock = _root.Q<Toggle>("artifact-world-lock");
            _artifactRoomAlign = _root.Q<Toggle>("artifact-room-align");
            _planWorldLock = _root.Q<Toggle>("plan-world-lock");
            _planRoomAlign = _root.Q<Toggle>("plan-room-align");
            _annotationNote = _root.Q<TextField>("annotation-note");
            _planAnnotationNote = _root.Q<TextField>("plan-annotation-note");
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
            _planStatus = _root.Q<Label>("val-plan-status");
            _planOpacityValue = _root.Q<Label>("val-plan-opacity");
            _scanPanel = _root.Q<VisualElement>("scan-panel");
            _paintPanel = _root.Q<VisualElement>("paint-panel");
            _planPanel = _root.Q<VisualElement>("plan-panel");
            _paintColorSwatch = _root.Q<VisualElement>("paint-color-swatch");
            _paintColorWheel = _root.Q<VisualElement>("paint-color-wheel");
            _paintColorCursor = _root.Q<VisualElement>("paint-color-cursor");
            _opacity = _root.Q<Slider>("scan-opacity");
            _opacityValue = _root.Q<Label>("val-opacity");
            _fineRadius = _root.Q<Slider>("fine-radius");
            _fineLength = _root.Q<Slider>("fine-length");
            _paintValue = _root.Q<Slider>("paint-value");
            _paintAlpha = _root.Q<Slider>("paint-alpha");
            _paintWidth = _root.Q<Slider>("paint-width");
            _planOpacity = _root.Q<Slider>("plan-opacity");
            _fineRadiusValue = _root.Q<Label>("val-fine-radius");
            _fineLengthValue = _root.Q<Label>("val-fine-length");
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
            _tabScan?.RegisterCallback<ClickEvent>(evt => SetTab(MenuTab.Scan));
            _tabPaint?.RegisterCallback<ClickEvent>(evt => SetTab(MenuTab.Paint));
            _tabPlan?.RegisterCallback<ClickEvent>(evt => SetTab(MenuTab.Plan));
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
            _planView?.RegisterCallback<ClickEvent>(evt =>
            {
                EnsureArtifactViewer();
                if (_artifactViewer != null) _ = _artifactViewer.ToggleAsync();
            });
            _planLoad?.RegisterCallback<ClickEvent>(evt =>
            {
                EnsureArtifactViewer();
                _artifactViewer?.RequestPackageFromDisk();
            });
            _planStyle?.RegisterCallback<ClickEvent>(evt =>
            {
                EnsureArtifactViewer();
                if (_artifactViewer != null)
                    _artifactViewer.PlanViewEnabled =
                        !_artifactViewer.PlanViewEnabled;
            });
            _planAnnotationMode?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.CycleAnnotationMode());
            _planAnnotationSave?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.SaveAnnotations());
            _planAnnotationEdit?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.BeginNoteEdit());
            _planAnnotationDelete?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.DeleteSelectedAnnotation());
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
            _planWorldLock?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.WorldLocked = evt.newValue;
            });
            _planRoomAlign?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.RoomAligned = evt.newValue;
            });
            _annotationNote?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.SelectedNote = evt.newValue;
            });
            _planAnnotationNote?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.SelectedNote = evt.newValue;
            });
            _opacity?.RegisterValueChangedCallback(evt =>
                SetArtifactOpacity(evt.newValue));
            _fineRadius?.RegisterValueChangedCallback(evt =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner != null) scanner.FineBrushRadius = evt.newValue;
            });
            _fineLength?.RegisterValueChangedCallback(evt =>
            {
                RoomScanner scanner = RoomScanner.Instance;
                if (scanner != null) scanner.FineToolLength = evt.newValue;
            });
            _paintAlpha?.RegisterValueChangedCallback(evt =>
                SetPaintAlpha(evt.newValue));
            _paintValue?.RegisterValueChangedCallback(evt =>
                SetPaintValue(evt.newValue));
            _paintWidth?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.PaintWidth = evt.newValue;
            });
            _planOpacity?.RegisterValueChangedCallback(evt =>
                SetArtifactOpacity(evt.newValue));
            _paintColorWheel?.RegisterCallback<PointerDownEvent>(
                OnPaintWheelPointerDown);
            _paintColorWheel?.RegisterCallback<PointerMoveEvent>(
                OnPaintWheelPointerMove);
            _paintColorWheel?.RegisterCallback<PointerUpEvent>(
                OnPaintWheelPointerUp);
            _paintColorWheel?.RegisterCallback<PointerCaptureOutEvent>(evt =>
                _paintWheelPointer = -1);
        }

        private void SetTab(MenuTab tab)
        {
            _selectedTab = tab;
            if (_scanPanel != null)
                _scanPanel.style.display = tab == MenuTab.Scan
                    ? DisplayStyle.Flex : DisplayStyle.None;
            if (_paintPanel != null)
                _paintPanel.style.display = tab == MenuTab.Paint
                    ? DisplayStyle.Flex : DisplayStyle.None;
            if (_planPanel != null)
                _planPanel.style.display = tab == MenuTab.Plan
                    ? DisplayStyle.Flex : DisplayStyle.None;
            _tabScan?.EnableInClassList("mode-tab--selected",
                tab == MenuTab.Scan);
            _tabPaint?.EnableInClassList("mode-tab--selected",
                tab == MenuTab.Paint);
            _tabPlan?.EnableInClassList("mode-tab--selected",
                tab == MenuTab.Plan);
            if (_artifactViewer != null)
                _artifactViewer.PaintInputEnabled = tab == MenuTab.Paint;
            RefreshStatus();
        }

        private void SetPaintTool(MerkabaArtifactPaintTool tool)
        {
            _artifactViewer ??= FindAnyObjectByType<MerkabaArtifactViewer>();
            if (_artifactViewer != null) _artifactViewer.PaintTool = tool;
        }

        private void EnsureArtifactViewer() => _artifactViewer ??=
            FindAnyObjectByType<MerkabaArtifactViewer>();

        private void SetArtifactOpacity(float value)
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner != null) scanner.ScanOpacity = value;
            EnsureArtifactViewer();
            if (_artifactViewer != null) _artifactViewer.PreviewOpacity = value;
        }

        private void SetPaintAlpha(float alpha)
        {
            EnsureArtifactViewer();
            if (_artifactViewer == null) return;
            Color color = _artifactViewer.PaintColor;
            color.a = Mathf.Clamp01(alpha);
            _artifactViewer.PaintColor = color;
        }

        private void SetPaintValue(float value)
        {
            EnsureArtifactViewer();
            if (_artifactViewer == null) return;
            Color rgb = Color.HSVToRGB(_paintHue, _paintSaturation,
                Mathf.Clamp01(value));
            rgb.a = _artifactViewer.PaintColor.a;
            _artifactViewer.PaintColor = rgb;
        }

        private void InitializePaintColorWheel()
        {
            if (_paintColorWheel == null) return;
            if (_paintWheelTexture != null)
            {
                _paintColorWheel.style.backgroundImage =
                    new StyleBackground(_paintWheelTexture);
                return;
            }
            const int size = 128;
            var pixels = new Color32[size * size];
            float radius = (size - 1) * 0.5f;
            Vector2 center = Vector2.one * radius;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 delta = new Vector2(x, y) - center;
                float saturation = delta.magnitude / radius;
                if (saturation > 1f)
                {
                    pixels[y * size + x] = new Color32(0, 0, 0, 0);
                    continue;
                }
                float hue = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x) /
                    (Mathf.PI * 2f), 1f);
                pixels[y * size + x] = Color.HSVToRGB(hue, saturation, 1f);
            }
            _paintWheelTexture = new Texture2D(size, size,
                TextureFormat.RGBA32, false, false)
            {
                name = "Merkaba Paint Color Wheel",
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _paintWheelTexture.SetPixels32(pixels);
            _paintWheelTexture.Apply(false, true);
            _paintColorWheel.style.backgroundImage =
                new StyleBackground(_paintWheelTexture);
        }

        private void OnPaintWheelPointerDown(PointerDownEvent evt)
        {
            if (_paintColorWheel == null || evt.button != 0) return;
            _paintWheelPointer = evt.pointerId;
            _paintColorWheel.CapturePointer(evt.pointerId);
            SetPaintWheelPosition(new Vector2(evt.position.x, evt.position.y));
            evt.StopPropagation();
        }

        private void OnPaintWheelPointerMove(PointerMoveEvent evt)
        {
            if (_paintWheelPointer != evt.pointerId) return;
            SetPaintWheelPosition(new Vector2(evt.position.x, evt.position.y));
            evt.StopPropagation();
        }

        private void OnPaintWheelPointerUp(PointerUpEvent evt)
        {
            if (_paintWheelPointer != evt.pointerId) return;
            SetPaintWheelPosition(new Vector2(evt.position.x, evt.position.y));
            if (_paintColorWheel.HasPointerCapture(evt.pointerId))
                _paintColorWheel.ReleasePointer(evt.pointerId);
            _paintWheelPointer = -1;
            evt.StopPropagation();
        }

        private void SetPaintWheelPosition(Vector2 panelPosition)
        {
            EnsureArtifactViewer();
            if (_artifactViewer == null || _paintColorWheel == null) return;
            Vector2 local = _paintColorWheel.WorldToLocal(panelPosition);
            float width = _paintColorWheel.contentRect.width;
            float height = _paintColorWheel.contentRect.height;
            if (!(width > 0f) || !(height > 0f)) return;
            Vector2 center = new(width * 0.5f, height * 0.5f);
            Vector2 delta = local - center;
            float radius = Mathf.Max(1f, Mathf.Min(width, height) * 0.5f);
            float magnitude = Mathf.Min(delta.magnitude, radius);
            if (delta.sqrMagnitude > radius * radius)
                delta = delta.normalized * radius;
            _paintHue = Mathf.Repeat(Mathf.Atan2(-delta.y, delta.x) /
                (Mathf.PI * 2f), 1f);
            _paintSaturation = magnitude / radius;
            float value = _paintValue != null ? _paintValue.value : 1f;
            Color rgb = Color.HSVToRGB(_paintHue, _paintSaturation, value);
            rgb.a = _artifactViewer.PaintColor.a;
            _artifactViewer.PaintColor = rgb;
            _paintHsvInitialized = true;
            UpdatePaintColorCursor(width, height);
        }

        private void UpdatePaintColorCursor(float width = -1f,
            float height = -1f)
        {
            if (_paintColorCursor == null || _paintColorWheel == null) return;
            if (!(width > 0f)) width = _paintColorWheel.contentRect.width;
            if (!(height > 0f)) height = _paintColorWheel.contentRect.height;
            if (!(width > 0f) || !(height > 0f)) return;
            float radius = Mathf.Min(width, height) * 0.5f;
            float angle = _paintHue * Mathf.PI * 2f;
            Vector2 point = new(width * 0.5f + Mathf.Cos(angle) *
                _paintSaturation * radius, height * 0.5f - Mathf.Sin(angle) *
                _paintSaturation * radius);
            _paintColorCursor.style.left = point.x - 6f;
            _paintColorCursor.style.top = point.y - 6f;
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
            Set(_planStatus, _artifactViewer?.Status ??
                "GLB View unavailable");

            float opacity = scanner.ScanOpacity;
            if (_artifactViewer != null)
                _artifactViewer.PreviewOpacity = opacity;
            _opacity?.SetValueWithoutNotify(opacity);
            Set(_opacityValue, $"{opacity * 100f:F0}%");
            _planOpacity?.SetValueWithoutNotify(opacity);
            Set(_planOpacityValue, $"{opacity * 100f:F0}%");
            _fineRadius?.SetValueWithoutNotify(scanner.FineBrushRadius);
            _fineLength?.SetValueWithoutNotify(scanner.FineToolLength);
            Set(_fineRadiusValue, $"{scanner.FineBrushRadius * 100f:F0} cm");
            Set(_fineLengthValue, $"{scanner.FineToolLength:F2} m");
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
            _planWorldLock?.SetValueWithoutNotify(
                _artifactViewer?.WorldLocked ?? true);
            _planRoomAlign?.SetValueWithoutNotify(
                _artifactViewer?.RoomAligned ?? false);
            if (_planAnnotationNote != null)
                _planAnnotationNote.SetValueWithoutNotify(
                    _artifactViewer?.SelectedNote ?? string.Empty);
            if (_planView != null)
                _planView.text = _artifactViewer != null &&
                    _artifactViewer.IsOpen ? "VIEW  ON" : "VIEW  OFF";
            if (_planStyle != null)
                _planStyle.text = _artifactViewer?.PlanViewEnabled ?? false
                    ? "PLAN  ON" : "MODEL  ON";
            if (_planAnnotationMode != null)
                _planAnnotationMode.text = "MARK  " +
                    (_artifactViewer?.AnnotationModeText ?? "OFF");
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
            _planView?.SetEnabled(!operationBusy && _artifactViewer != null);
            _planLoad?.SetEnabled(!operationBusy && _artifactViewer != null);
            _planStyle?.SetEnabled(!operationBusy && reviewing);
            _planAnnotationMode?.SetEnabled(!operationBusy && reviewing);
            _planAnnotationSave?.SetEnabled(!operationBusy && reviewing);
            _planAnnotationNote?.SetEnabled(!operationBusy && reviewing);
            _planAnnotationEdit?.SetEnabled(!operationBusy && reviewing &&
                (_artifactViewer?.HasSelectedAnnotation ?? false));
            _planAnnotationDelete?.SetEnabled(!operationBusy && reviewing &&
                (_artifactViewer?.HasSelectedAnnotation ?? false));
            _planWorldLock?.SetEnabled(!operationBusy && reviewing);
            _planRoomAlign?.SetEnabled(!operationBusy && reviewing);
        }

        private void RefreshPaintControls()
        {
            if (_artifactViewer == null) return;
            _artifactViewer.PaintInputEnabled = _selectedTab == MenuTab.Paint;
            Color color = _artifactViewer.PaintColor;
            if (!_paintHsvInitialized)
            {
                Color.RGBToHSV(color, out _paintHue, out _paintSaturation,
                    out _);
                _paintHsvInitialized = true;
            }
            Color.RGBToHSV(color, out _, out _, out float value);
            _paintValue?.SetValueWithoutNotify(value);
            _paintAlpha?.SetValueWithoutNotify(color.a);
            _paintWidth?.SetValueWithoutNotify(_artifactViewer.PaintWidth);
            Set(_paintWidthValue, $"{_artifactViewer.PaintWidth * 1000f:F0} mm");
            if (_paintColorSwatch != null)
                _paintColorSwatch.style.backgroundColor = color;
            UpdatePaintColorCursor();
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
        private enum MenuTab { Scan, Paint, Plan }
    }
}
