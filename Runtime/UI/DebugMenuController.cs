using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
        private Button _start, _save, _saveAs, _load, _new, _rename,
            _deleteSession, _export,
            _exportSaveAs, _fine, _readout, _occlusion, _checker,
            _artifactView, _artifactLoad, _annotationMode, _annotationSave,
            _annotationEdit, _annotationDelete, _tabScan, _tabRefine,
            _tabPaint, _tabPlan, _fineRefine, _fineErase,
            _paintView, _paintLoad, _paintSave, _paintBrush, _paintLine,
            _paintSurface, _paintSpatial, _paintSpray, _paintErase,
            _paintEyedropper, _paintShapeRound, _paintShapeSquare,
            _designPaint, _designObjects, _objectImport, _objectPlace,
            _objectSelect, _objectDuplicate, _objectVisible, _objectLock,
            _objectDelete, _designUndo, _designRedo,
            _saveSwatch, _planView, _planStyle, _library, _overflow,
            _libraryBack, _diagnosticsBack, _libraryScans, _libraryExports,
            _libraryModels, _viewLibrary, _objectLibrary, _libraryUseObject;
        private Label _sessionNameLabel, _scanning, _chunks, _kernels,
            _visibleBoundary;
        private Label _saved, _exportStatus, _pointer, _fps, _proximity;
        private Label _exportSaveAsStatus;
        private Label _anchorStatus, _lastExport, _libraryModelStatus,
            _libraryModelCount, _selectedObjectAsset, _paintToolName;
        private Label _artifactStatus, _objectStatus;
        private Label _paintStatus, _paintWidthValue, _paintFlowValue,
            _paintHardnessValue, _paintSaturationValue, _paintDensityValue,
            _paintScatterValue;
        private TextField _sessionName, _annotationNote, _exportName;
        private DropdownField _sessionPicker, _objectAssetPicker,
            _objectInstancePicker, _exportFormat;
        private Slider _opacity;
        private Slider _paintValue, _paintAlpha, _paintWidth, _paintFlow,
            _paintHardness, _paintSaturationSlider, _paintDensity,
            _paintScatter;
        private Toggle _artifactWorldLock, _artifactRoomAlign,
            _objectSurfaceSnap, _objectUprightSnap, _objectGridSnap;
        private Slider _fineRadius, _fineLength;
        private Label _opacityValue, _fineRadiusValue, _fineLengthValue;
        private Label _operationStage;
        private VisualElement _operationPanel;
        private VisualElement _scanPanel, _refinePanel, _paintPanel, _planPanel,
            _paintColorSwatch, _paintColorWheel, _paintColorCursor, _recentSwatches,
            _savedSwatches, _paintSpraySettings, _paintWorkspace,
            _objectsWorkspace, _designHistoryActions, _paintColorCard,
            _paintPalette, _paintRowValue, _paintRowAlpha, _paintRowWidth,
            _paintRowFlow, _paintRowHardness, _paintRowSaturation,
            _paintRowShape, _modeTabs, _libraryPanel, _libraryScansPanel,
            _libraryExportsPanel, _libraryModelsPanel, _diagnosticsPanel;
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
        private readonly List<MerkabaSessionInfo> _sessionEntries = new();
        private readonly List<Color> _recentColors = new();
        private readonly List<Color> _savedColors = new();
        private readonly List<MerkabaDesignAsset> _designAssets = new();
        private readonly List<MerkabaDesignInstance> _designInstances = new();
        private int _selectedSessionIndex = -1;
        private DesignSubmode _designSubmode;
        private bool _hasLastRecentColor;
        private Color _lastRecentColor;
        private bool _exportFileSavePending;
        private string _exportFileSaveStatus = string.Empty;
        private ConsoleDestination _destination;
        private LibraryTab _libraryTab;
        private bool _exportAsTiles;

        private bool DesignInteractionActive =>
            _destination == ConsoleDestination.Tools &&
            _selectedTab == MenuTab.Design;

        public bool IsVisible => _visible;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) gameObject.layer = uiLayer;
            _document.sortingOrder = 1000;
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
            ApplyNavigation();
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
            RefreshSessionChoices();
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
            _save = _root.Q<Button>("btn-save");
            _saveAs = _root.Q<Button>("btn-save-as");
            _load = _root.Q<Button>("btn-load");
            _new = _root.Q<Button>("btn-new");
            _rename = _root.Q<Button>("btn-rename");
            _deleteSession = _root.Q<Button>("btn-delete-session");
            _export = _root.Q<Button>("btn-export");
            _exportSaveAs = _root.Q<Button>("btn-export-save-as");
            _library = _root.Q<Button>("btn-library");
            _overflow = _root.Q<Button>("btn-overflow");
            _libraryBack = _root.Q<Button>("btn-library-back");
            _diagnosticsBack = _root.Q<Button>("btn-diagnostics-back");
            _libraryScans = _root.Q<Button>("btn-library-scans");
            _libraryExports = _root.Q<Button>("btn-library-exports");
            _libraryModels = _root.Q<Button>("btn-library-models");
            _viewLibrary = _root.Q<Button>("btn-view-library");
            _objectLibrary = _root.Q<Button>("btn-object-library");
            _libraryUseObject = _root.Q<Button>("btn-library-use-object");
            _fine = _root.Q<Button>("btn-fine");
            _readout = _root.Q<Button>("btn-readout");
            _occlusion = _root.Q<Button>("btn-occlusion");
            _checker = _root.Q<Button>("btn-checker");
            _artifactView = _root.Q<Button>("btn-artifact-view");
            _artifactLoad = _root.Q<Button>("btn-artifact-load");
            _annotationMode = _root.Q<Button>("btn-annotation-mode");
            _annotationSave = _root.Q<Button>("btn-annotation-save");
            _annotationEdit = _root.Q<Button>("btn-annotation-edit");
            _annotationDelete = _root.Q<Button>("btn-annotation-delete");
            _tabScan = _root.Q<Button>("btn-tab-scan");
            _tabRefine = _root.Q<Button>("btn-tab-refine");
            _tabPaint = _root.Q<Button>("btn-tab-design");
            _tabPlan = _root.Q<Button>("btn-tab-view");
            _fineRefine = _root.Q<Button>("btn-fine-refine");
            _fineErase = _root.Q<Button>("btn-fine-erase");
            _paintView = _root.Q<Button>("btn-paint-view");
            _paintLoad = _root.Q<Button>("btn-paint-load");
            _paintSave = _root.Q<Button>("btn-paint-save");
            _paintBrush = _root.Q<Button>("btn-paint-brush");
            _paintLine = _root.Q<Button>("btn-paint-line");
            _paintSurface = _root.Q<Button>("btn-paint-surface");
            _paintSpatial = _root.Q<Button>("btn-paint-spatial");
            _paintSpray = _root.Q<Button>("btn-paint-spray");
            _paintErase = _root.Q<Button>("btn-paint-erase");
            _paintEyedropper = _root.Q<Button>("btn-paint-eyedropper");
            _paintShapeRound = _root.Q<Button>("btn-paint-round");
            _paintShapeSquare = _root.Q<Button>("btn-paint-square");
            _designPaint = _root.Q<Button>("btn-design-paint");
            _designObjects = _root.Q<Button>("btn-design-objects");
            _objectImport = _root.Q<Button>("btn-object-import");
            _objectPlace = _root.Q<Button>("btn-object-place");
            _objectSelect = _root.Q<Button>("btn-object-select");
            _objectDuplicate = _root.Q<Button>("btn-object-duplicate");
            _objectVisible = _root.Q<Button>("btn-object-visible");
            _objectLock = _root.Q<Button>("btn-object-lock");
            _objectDelete = _root.Q<Button>("btn-object-delete");
            _designUndo = _root.Q<Button>("btn-design-undo");
            _designRedo = _root.Q<Button>("btn-design-redo");
            _saveSwatch = _root.Q<Button>("btn-save-swatch");
            _planView = _root.Q<Button>("btn-plan-model");
            _planStyle = _root.Q<Button>("btn-plan-style");
            _artifactWorldLock = _root.Q<Toggle>("artifact-world-lock");
            _artifactRoomAlign = _root.Q<Toggle>("artifact-room-align");
            _annotationNote = _root.Q<TextField>("annotation-note");
            _sessionName = _root.Q<TextField>("session-name");
            _exportName = _root.Q<TextField>("export-name");
            _sessionPicker = _root.Q<DropdownField>("session-picker");
            _exportFormat = _root.Q<DropdownField>("export-format");
            if (_exportFormat != null)
            {
                _exportFormat.choices = new List<string> { "GLB", "3D Tiles ZIP" };
                _exportFormat.SetValueWithoutNotify(_exportAsTiles
                    ? "3D Tiles ZIP" : "GLB");
            }
            _objectAssetPicker = _root.Q<DropdownField>(
                "object-asset-picker");
            _objectInstancePicker = _root.Q<DropdownField>(
                "object-instance-picker");
            _sessionNameLabel = _root.Q<Label>("val-session-name");
            BindBuildStamp();
            _scanning = _root.Q<Label>("val-scanning");
            _chunks = _root.Q<Label>("val-chunks");
            _kernels = _root.Q<Label>("val-kernels");
            _visibleBoundary = _root.Q<Label>("val-visible");
            _saved = _root.Q<Label>("val-saved");
            _proximity = _root.Q<Label>("val-proximity");
            _exportStatus = _root.Q<Label>("val-export");
            _exportSaveAsStatus = _root.Q<Label>("val-export-save-as");
            _anchorStatus = _root.Q<Label>("val-anchor");
            _lastExport = _root.Q<Label>("val-last-export");
            _libraryModelStatus = _root.Q<Label>("val-library-model-status");
            _libraryModelCount = _root.Q<Label>("val-library-model-count");
            _selectedObjectAsset = _root.Q<Label>("val-selected-object-asset");
            _paintToolName = _root.Q<Label>("val-paint-tool-name");
            _pointer = _root.Q<Label>("val-pointer");
            _fps = _root.Q<Label>("val-fps");
            _artifactStatus = _root.Q<Label>("val-artifact");
            _objectStatus = _root.Q<Label>("val-object-status");
            _paintStatus = _root.Q<Label>("val-paint-status");
            _paintWidthValue = _root.Q<Label>("val-paint-width");
            _paintFlowValue = _root.Q<Label>("val-paint-flow");
            _paintHardnessValue = _root.Q<Label>("val-paint-hardness");
            _paintSaturationValue = _root.Q<Label>("val-paint-saturation");
            _paintDensityValue = _root.Q<Label>("val-paint-density");
            _paintScatterValue = _root.Q<Label>("val-paint-scatter");
            _scanPanel = _root.Q<VisualElement>("scan-panel");
            _refinePanel = _root.Q<VisualElement>("refine-panel");
            _paintPanel = _root.Q<VisualElement>("design-panel");
            _planPanel = _root.Q<VisualElement>("view-panel");
            _modeTabs = _root.Q<VisualElement>("mode-tabs");
            _libraryPanel = _root.Q<VisualElement>("library-panel");
            _libraryScansPanel = _root.Q<VisualElement>("library-scans");
            _libraryExportsPanel = _root.Q<VisualElement>("library-exports");
            _libraryModelsPanel = _root.Q<VisualElement>("library-models");
            _diagnosticsPanel = _root.Q<VisualElement>("diagnostics-foldout");
            _paintColorSwatch = _root.Q<VisualElement>("paint-color-swatch");
            _paintColorWheel = _root.Q<VisualElement>("paint-color-wheel");
            _paintColorCursor = _root.Q<VisualElement>("paint-color-cursor");
            _recentSwatches = _root.Q<VisualElement>("recent-swatches");
            _savedSwatches = _root.Q<VisualElement>("saved-swatches");
            _opacity = _root.Q<Slider>("scan-opacity");
            _opacityValue = _root.Q<Label>("val-opacity");
            _fineRadius = _root.Q<Slider>("fine-radius");
            _fineLength = _root.Q<Slider>("fine-length");
            _paintValue = _root.Q<Slider>("paint-value");
            _paintAlpha = _root.Q<Slider>("paint-alpha");
            _paintWidth = _root.Q<Slider>("paint-width");
            _paintFlow = _root.Q<Slider>("paint-flow");
            _paintHardness = _root.Q<Slider>("paint-hardness");
            _paintSaturationSlider = _root.Q<Slider>("paint-saturation");
            _paintDensity = _root.Q<Slider>("paint-density");
            _paintScatter = _root.Q<Slider>("paint-scatter");
            _paintSpraySettings = _root.Q<VisualElement>(
                "paint-spray-settings");
            _paintColorCard = _root.Q<VisualElement>("paint-color-card");
            _paintPalette = _root.Q<VisualElement>("paint-palette");
            _paintRowValue = _root.Q<VisualElement>("paint-row-value");
            _paintRowAlpha = _root.Q<VisualElement>("paint-row-alpha");
            _paintRowWidth = _root.Q<VisualElement>("paint-row-width");
            _paintRowFlow = _root.Q<VisualElement>("paint-row-flow");
            _paintRowHardness = _root.Q<VisualElement>("paint-row-hardness");
            _paintRowSaturation = _root.Q<VisualElement>("paint-row-saturation");
            _paintRowShape = _root.Q<VisualElement>("paint-row-shape");
            _paintWorkspace = _root.Q<VisualElement>("paint-workspace");
            _objectsWorkspace = _root.Q<VisualElement>("objects-workspace");
            _designHistoryActions = _root.Q<VisualElement>(
                "design-history-actions");
            _objectSurfaceSnap = _root.Q<Toggle>("object-surface-snap");
            _objectUprightSnap = _root.Q<Toggle>("object-upright-snap");
            _objectGridSnap = _root.Q<Toggle>("object-grid-snap");
            _fineRadiusValue = _root.Q<Label>("val-fine-radius");
            _fineLengthValue = _root.Q<Label>("val-fine-length");
            _operationPanel = _root.Q<VisualElement>("operation-panel");
            _operationStage = _root.Q<Label>("operation-stage");
            _operationProgress = _root.Q<ProgressBar>("operation-progress");
        }

        // Which APK is actually on the headset. Application.buildGUID is
        // regenerated by every player build, so a stamp that did not change
        // means the device is still running the previous APK - the one question
        // a screenshot of a running scan can never answer on its own.
        private void BindBuildStamp()
        {
            Label stamp = _root.Q<Label>("val-build-stamp");
            if (stamp == null) return;
            string guid = Application.buildGUID;
            stamp.text = string.IsNullOrEmpty(guid)
                ? $"v{Application.version} editor"
                : $"v{Application.version} {guid.Substring(0, Mathf.Min(8, guid.Length))}";
        }

        private void Bind()
        {
            _start?.RegisterCallback<ClickEvent>(evt =>
                RoomScanner.Instance?.ToggleScanning());
            _save?.RegisterCallback<ClickEvent>(evt => _ = SaveActiveAsync());
            _saveAs?.RegisterCallback<ClickEvent>(evt => _ = SaveAsAsync());
            _load?.RegisterCallback<ClickEvent>(evt => _ = OpenSelectedAsync());
            _new?.RegisterCallback<ClickEvent>(evt => _ = NewSessionAsync());
            _rename?.RegisterCallback<ClickEvent>(evt => RenameActiveSession());
            _deleteSession?.RegisterCallback<ClickEvent>(evt =>
                _ = DeleteSelectedSessionAsync());
            _export?.RegisterCallback<ClickEvent>(evt =>
                _ = ExportAsync(_exportAsTiles));
            _exportFormat?.RegisterValueChangedCallback(evt =>
            {
                _exportAsTiles = evt.newValue == "3D Tiles ZIP";
                RefreshStatus();
            });
            _exportSaveAs?.RegisterCallback<ClickEvent>(evt =>
                RequestExportSaveAs());
            _library?.RegisterCallback<ClickEvent>(evt =>
                ShowLibrary(_libraryTab));
            _overflow?.RegisterCallback<ClickEvent>(evt =>
            {
                _destination = _destination == ConsoleDestination.Diagnostics
                    ? ConsoleDestination.Tools : ConsoleDestination.Diagnostics;
                ApplyNavigation();
            });
            _libraryBack?.RegisterCallback<ClickEvent>(evt => CloseDestination());
            _diagnosticsBack?.RegisterCallback<ClickEvent>(evt => CloseDestination());
            _libraryScans?.RegisterCallback<ClickEvent>(evt => ShowLibrary(LibraryTab.Scans));
            _libraryExports?.RegisterCallback<ClickEvent>(evt => ShowLibrary(LibraryTab.Exports));
            _libraryModels?.RegisterCallback<ClickEvent>(evt => ShowLibrary(LibraryTab.Models));
            _viewLibrary?.RegisterCallback<ClickEvent>(evt => ShowLibrary(LibraryTab.Models));
            _objectLibrary?.RegisterCallback<ClickEvent>(evt => ShowLibrary(LibraryTab.Models));
            _libraryUseObject?.RegisterCallback<ClickEvent>(evt =>
            {
                SetDesignSubmode(DesignSubmode.Objects);
                SetTab(MenuTab.Design);
            });
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
            _tabRefine?.RegisterCallback<ClickEvent>(evt =>
                SetTab(MenuTab.Refine));
            _tabPaint?.RegisterCallback<ClickEvent>(evt =>
                SetTab(MenuTab.Design));
            _tabPlan?.RegisterCallback<ClickEvent>(evt => SetTab(MenuTab.View));
            _fineRefine?.RegisterCallback<ClickEvent>(evt =>
                SelectFineTool(false));
            _fineErase?.RegisterCallback<ClickEvent>(evt =>
                SelectFineTool(true));
            _paintView?.RegisterCallback<ClickEvent>(evt =>
            {
                _artifactViewer ??=
                    FindAnyObjectByType<MerkabaArtifactViewer>();
                if (_artifactViewer != null) _ = _artifactViewer.ToggleAsync();
            });
            _paintLoad?.RegisterCallback<ClickEvent>(evt =>
                ShowLibrary(LibraryTab.Models));
            _paintSave?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.SaveAnnotations());
            _paintBrush?.RegisterCallback<ClickEvent>(evt =>
                SetPaintTool(MerkabaArtifactPaintTool.Brush));
            _paintLine?.RegisterCallback<ClickEvent>(evt =>
                SetPaintTool(MerkabaArtifactPaintTool.Line));
            _paintSurface?.RegisterCallback<ClickEvent>(evt =>
                SetPaintTool(MerkabaArtifactPaintTool.SurfaceBrush));
            _paintSpatial?.RegisterCallback<ClickEvent>(evt =>
                SetPaintTool(MerkabaArtifactPaintTool.SpatialBrush));
            _paintSpray?.RegisterCallback<ClickEvent>(evt =>
                SetPaintTool(MerkabaArtifactPaintTool.Spray));
            _paintErase?.RegisterCallback<ClickEvent>(evt =>
                SetPaintTool(MerkabaArtifactPaintTool.Erase));
            _paintEyedropper?.RegisterCallback<ClickEvent>(evt =>
                SetPaintTool(MerkabaArtifactPaintTool.Eyedropper));
            _paintShapeRound?.RegisterCallback<ClickEvent>(evt =>
                SetPaintShape(MerkabaBrushShape.Round));
            _paintShapeSquare?.RegisterCallback<ClickEvent>(evt =>
                SetPaintShape(MerkabaBrushShape.Square));
            _designPaint?.RegisterCallback<ClickEvent>(evt =>
                SetDesignSubmode(DesignSubmode.Paint));
            _designObjects?.RegisterCallback<ClickEvent>(evt =>
                SetDesignSubmode(DesignSubmode.Objects));
            _objectImport?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.RequestDesignAssetFromDisk());
            _objectPlace?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.SetObjectPlacementEnabled(true));
            _objectSelect?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.SetObjectPlacementEnabled(false));
            _objectDuplicate?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.DuplicateSelectedDesignObject());
            _objectVisible?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.ToggleSelectedDesignObjectVisible());
            _objectLock?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.ToggleSelectedDesignObjectLocked());
            _objectDelete?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.DeleteSelectedDesignObject());
            _designUndo?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.UndoDesign());
            _designRedo?.RegisterCallback<ClickEvent>(evt =>
                _artifactViewer?.RedoDesign());
            _saveSwatch?.RegisterCallback<ClickEvent>(evt => SaveCurrentSwatch());
            _planStyle?.RegisterCallback<ClickEvent>(evt =>
            {
                EnsureArtifactViewer();
                if (_artifactViewer != null) _artifactViewer.PlanViewEnabled = true;
            });
            _planView?.RegisterCallback<ClickEvent>(evt =>
            {
                EnsureArtifactViewer();
                if (_artifactViewer != null) _artifactViewer.PlanViewEnabled = false;
            });
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
            _paintFlow?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.PaintFlow = evt.newValue;
            });
            _paintHardness?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.PaintHardness = evt.newValue;
            });
            _paintSaturationSlider?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.PaintSaturation = evt.newValue;
            });
            _paintDensity?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.SprayDensity = evt.newValue;
            });
            _paintScatter?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.SprayScatter = evt.newValue;
            });
            _paintColorWheel?.RegisterCallback<PointerDownEvent>(
                OnPaintWheelPointerDown);
            _paintColorWheel?.RegisterCallback<PointerMoveEvent>(
                OnPaintWheelPointerMove);
            _paintColorWheel?.RegisterCallback<PointerUpEvent>(
                OnPaintWheelPointerUp);
            _paintColorWheel?.RegisterCallback<PointerCaptureOutEvent>(evt =>
                _paintWheelPointer = -1);
            _sessionPicker?.RegisterValueChangedCallback(evt =>
                SelectSessionChoice(evt.newValue));
            _objectAssetPicker?.RegisterValueChangedCallback(evt =>
                SelectObjectAssetChoice(evt.newValue));
            _objectInstancePicker?.RegisterValueChangedCallback(evt =>
                SelectObjectInstanceChoice(evt.newValue));
            _objectSurfaceSnap?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.ObjectSurfaceSnap = evt.newValue;
            });
            _objectUprightSnap?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.ObjectUprightSnap = evt.newValue;
            });
            _objectGridSnap?.RegisterValueChangedCallback(evt =>
            {
                if (_artifactViewer != null)
                    _artifactViewer.ObjectGridSnap = evt.newValue;
            });
            BuildSwatchButtons();
        }

        private void SetTab(MenuTab tab)
        {
            _selectedTab = tab;
            _destination = ConsoleDestination.Tools;
            ApplyNavigation();
            RefreshStatus();
        }

        private void ShowLibrary(LibraryTab tab)
        {
            _destination = ConsoleDestination.Library;
            _libraryTab = tab;
            if (tab == LibraryTab.Scans) RefreshSessionChoices();
            ApplyNavigation();
            RefreshStatus();
        }

        private void CloseDestination()
        {
            _destination = ConsoleDestination.Tools;
            ApplyNavigation();
            RefreshStatus();
        }

        private void ApplyNavigation()
        {
            bool tools = _destination == ConsoleDestination.Tools;
            bool library = _destination == ConsoleDestination.Library;
            MenuTab tab = _selectedTab;
            SetDisplayed(_modeTabs, tools);
            SetDisplayed(_scanPanel, tools && tab == MenuTab.Scan);
            SetDisplayed(_refinePanel, tools && tab == MenuTab.Refine);
            SetDisplayed(_paintPanel, tools && tab == MenuTab.Design);
            SetDisplayed(_planPanel, tools && tab == MenuTab.View);
            SetDisplayed(_libraryPanel, library);
            SetDisplayed(_diagnosticsPanel,
                _destination == ConsoleDestination.Diagnostics);
            SetDisplayed(_libraryScansPanel, _libraryTab == LibraryTab.Scans);
            SetDisplayed(_libraryExportsPanel, _libraryTab == LibraryTab.Exports);
            SetDisplayed(_libraryModelsPanel, _libraryTab == LibraryTab.Models);
            SetDisplayed(_designHistoryActions, true);
            _libraryScans?.EnableInClassList("segment--selected", _libraryTab == LibraryTab.Scans);
            _libraryExports?.EnableInClassList("segment--selected", _libraryTab == LibraryTab.Exports);
            _libraryModels?.EnableInClassList("segment--selected", _libraryTab == LibraryTab.Models);
            _library?.EnableInClassList("session-heading--selected", library);
            _overflow?.EnableInClassList("segment--selected",
                _destination == ConsoleDestination.Diagnostics);
            _tabScan?.EnableInClassList("mode-tab--selected",
                tab == MenuTab.Scan);
            _tabRefine?.EnableInClassList("mode-tab--selected",
                tab == MenuTab.Refine);
            _tabPaint?.EnableInClassList("mode-tab--selected",
                tab == MenuTab.Design);
            _tabPlan?.EnableInClassList("mode-tab--selected",
                tab == MenuTab.View);
            SetDesignSubmode(_designSubmode);
        }

        private void SelectFineTool(bool erase)
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null) return;
            scanner.FineEraseSelected = erase;
            scanner.FineMode = true;
            RefreshFineTool();
        }

        private void RefreshFineTool()
        {
            bool erase = RoomScanner.Instance?.FineEraseSelected ?? false;
            _fineRefine?.EnableInClassList("segment--selected", !erase);
            _fineErase?.EnableInClassList("segment--selected", erase);
        }

        private void SetPaintTool(MerkabaArtifactPaintTool tool)
        {
            _artifactViewer ??= FindAnyObjectByType<MerkabaArtifactViewer>();
            if (_artifactViewer != null) _artifactViewer.PaintTool = tool;
        }

        private void SetPaintShape(MerkabaBrushShape shape)
        {
            _artifactViewer ??= FindAnyObjectByType<MerkabaArtifactViewer>();
            if (_artifactViewer != null) _artifactViewer.PaintShape = shape;
        }

        private void SetDesignSubmode(DesignSubmode mode)
        {
            _designSubmode = mode;
            bool paint = mode == DesignSubmode.Paint;
            if (_paintWorkspace != null)
                _paintWorkspace.style.display = paint
                    ? DisplayStyle.Flex : DisplayStyle.None;
            if (_objectsWorkspace != null)
                _objectsWorkspace.style.display = paint
                    ? DisplayStyle.None : DisplayStyle.Flex;
            _designPaint?.EnableInClassList("segment--selected", paint);
            _designObjects?.EnableInClassList("segment--selected", !paint);
            if (_artifactViewer != null)
            {
                bool design = DesignInteractionActive;
                _artifactViewer.PaintInputEnabled = design && paint;
                _artifactViewer.ObjectInputEnabled = design && !paint;
            }
        }

        private void SelectObjectAssetChoice(string choice)
        {
            for (int index = 0; index < _designAssets.Count; index++)
                if (string.Equals(ObjectAssetChoice(_designAssets[index]),
                        choice, StringComparison.Ordinal))
                {
                    _artifactViewer?.SelectDesignAsset(
                        _designAssets[index].id);
                    return;
                }
        }

        private void SelectObjectInstanceChoice(string choice)
        {
            for (int index = 0; index < _designInstances.Count; index++)
                if (string.Equals(ObjectInstanceChoice(
                            _designInstances[index]), choice,
                        StringComparison.Ordinal))
                {
                    _artifactViewer?.SelectDesignInstance(
                        _designInstances[index].instanceId);
                    return;
                }
        }

        private static string ObjectAssetChoice(MerkabaDesignAsset asset) =>
            $"{asset.displayName} · {asset.id.Substring(0, 8)}";

        private static string ObjectInstanceChoice(
            MerkabaDesignInstance instance)
        {
            string asset = instance.assetId ?? string.Empty;
            string shortId = asset.Length >= 8 ? asset.Substring(0, 8) : asset;
            return $"#{instance.instanceId} · {shortId}";
        }

        private void EnsureArtifactViewer() => _artifactViewer ??=
            FindAnyObjectByType<MerkabaArtifactViewer>();

        private async Task ExportAsync(bool tiles)
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null || scanner.IsBusy || _exportFileSavePending)
                return;
            _exportFileSaveStatus = string.Empty;
            string name = _exportName?.value;
            bool success = tiles
                ? await scanner.ExportViewerPackageAsync(name)
                : await scanner.ExportGlbAsync(name);
            if (success)
                _exportFileSaveStatus = Path.GetFileName(scanner.LastExportPath) +
                    " is ready in app storage.";
        }

        private void RequestExportSaveAs()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (_exportFileSavePending || scanner == null || scanner.IsBusy)
                return;
            string source = scanner.LastExportPath;
            if (string.IsNullOrEmpty(source) || !File.Exists(source))
            {
                _exportFileSaveStatus = "Export a GLB or 3D Tiles package first.";
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = unityPlayer.GetStatic<
                    AndroidJavaObject>("currentActivity");
                using var picker = new AndroidJavaClass(
                    "com.genesis.roomscan.MerkabaPackagePicker");
                _exportFileSavePending = true;
                _exportFileSaveStatus = "Choose where to save " +
                    Path.GetFileName(source);
                picker.CallStatic("save", activity, source,
                    Path.GetFileName(source), source.EndsWith(".glb",
                        StringComparison.OrdinalIgnoreCase)
                        ? "model/gltf-binary" : "application/zip",
                    gameObject.name, nameof(OnExportDocumentResult));
            }
            catch (Exception exception)
            {
                _exportFileSavePending = false;
                _exportFileSaveStatus = "Export remains in app storage; " +
                    "Save As failed: " + exception.Message;
                Logger.Error(_exportFileSaveStatus);
            }
#else
            _exportFileSaveStatus = "Save As is available in Quest Files; " +
                "export remains in app storage.";
#endif
        }

        [UnityEngine.Scripting.Preserve]
        public void OnExportDocumentResult(string result)
        {
            if (!_exportFileSavePending) return;
            if (result == "COPYING")
            {
                _exportFileSaveStatus = "Copying export to selected document…";
                return;
            }
            _exportFileSavePending = false;
            if (string.IsNullOrWhiteSpace(result) || result == "CANCELLED")
                _exportFileSaveStatus = "Save As cancelled; export remains in app storage.";
            else if (result.StartsWith("SAVED:", StringComparison.Ordinal))
                _exportFileSaveStatus = "Export saved to Quest Files.";
            else
            {
                string detail = result.StartsWith("ERROR:", StringComparison.Ordinal)
                    ? result.Substring(6) : result;
                _exportFileSaveStatus = "Save As failed: " + detail +
                    ". Destination may be incomplete; export remains in app storage.";
                Logger.Error(_exportFileSaveStatus);
            }
        }

        private async Task NewSessionAsync()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null) return;
            await scanner.NewClearAsync(SessionNameInput());
            RefreshSessionChoices();
        }

        private async Task OpenSelectedAsync()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null || _selectedSessionIndex < 0 ||
                _selectedSessionIndex >= _sessionEntries.Count) return;
            await scanner.OpenSessionAsync(
                _sessionEntries[_selectedSessionIndex].Id);
            RefreshSessionChoices();
        }

        private async Task SaveAsAsync()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null) return;
            await scanner.SaveAsAsync(SessionNameInput());
            RefreshSessionChoices();
        }

        private async Task SaveActiveAsync()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null) return;
            await scanner.SaveAsync();
            RefreshSessionChoices();
        }

        private void RenameActiveSession()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null) return;
            scanner.RenameActiveSession(SessionNameInput());
            RefreshSessionChoices();
        }

        private async Task DeleteSelectedSessionAsync()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null || _selectedSessionIndex < 0 ||
                _selectedSessionIndex >= _sessionEntries.Count) return;
            await scanner.DeleteSessionAsync(
                _sessionEntries[_selectedSessionIndex].Id);
            RefreshSessionChoices();
        }

        private string SessionNameInput() =>
            string.IsNullOrWhiteSpace(_sessionName?.value)
                ? null : _sessionName.value.Trim();

        private void RefreshSessionChoices()
        {
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null || _sessionPicker == null) return;
            _sessionEntries.Clear();
            _sessionEntries.AddRange(scanner.Sessions);
            var choices = new List<string>(_sessionEntries.Count);
            _selectedSessionIndex = -1;
            for (int index = 0; index < _sessionEntries.Count; index++)
            {
                MerkabaSessionInfo session = _sessionEntries[index];
                choices.Add(SessionChoice(session));
                if (session.Id == scanner.ActiveSessionId)
                    _selectedSessionIndex = index;
            }
            if (_selectedSessionIndex < 0 && choices.Count > 0)
                _selectedSessionIndex = 0;
            _sessionPicker.choices = choices;
            _sessionPicker.SetValueWithoutNotify(_selectedSessionIndex >= 0
                ? choices[_selectedSessionIndex] : string.Empty);
            if (_sessionName != null && scanner.ActiveSessionId != Guid.Empty)
                _sessionName.SetValueWithoutNotify(scanner.ActiveSessionName);
        }

        private void SelectSessionChoice(string choice)
        {
            _selectedSessionIndex = -1;
            for (int index = 0; index < _sessionEntries.Count; index++)
                if (string.Equals(SessionChoice(_sessionEntries[index]), choice,
                        StringComparison.Ordinal))
                {
                    _selectedSessionIndex = index;
                    _sessionName?.SetValueWithoutNotify(
                        _sessionEntries[index].displayName);
                    break;
                }
        }

        private static string SessionChoice(MerkabaSessionInfo session) =>
            $"{session.displayName} · {session.Id.ToString("N").Substring(0, 8)} · No thumbnail";

        private void BuildSwatchButtons()
        {
            if (_recentColors.Count == 0)
                _recentColors.AddRange(new[]
                {
                    new Color(0.1f, 0.8f, 1f, 0.85f),
                    new Color(1f, 0.3f, 0.2f, 0.85f),
                    new Color(1f, 0.82f, 0.18f, 0.85f),
                    new Color(0.2f, 0.9f, 0.45f, 0.85f),
                    new Color(0.68f, 0.34f, 1f, 0.85f),
                    Color.white, Color.black, Color.gray
                });
            RebuildSwatchRow(_recentSwatches, _recentColors);
            RebuildSwatchRow(_savedSwatches, _savedColors);
        }

        private void RebuildSwatchRow(VisualElement row, List<Color> colors)
        {
            if (row == null) return;
            row.Clear();
            for (int index = 0; index < colors.Count && index < 8; index++)
            {
                Color color = colors[index];
                var button = new Button(() => SelectSwatch(color));
                button.AddToClassList("color-swatch");
                button.style.backgroundColor = color;
                row.Add(button);
            }
        }

        private void SelectSwatch(Color color)
        {
            EnsureArtifactViewer();
            if (_artifactViewer == null) return;
            _artifactViewer.PaintColor = color;
            Color.RGBToHSV(color, out _paintHue, out _paintSaturation, out _);
            RefreshPaintControls();
        }

        private void SaveCurrentSwatch()
        {
            EnsureArtifactViewer();
            if (_artifactViewer == null) return;
            Color color = _artifactViewer.PaintColor;
            _savedColors.RemoveAll(item => Approximately(item, color));
            _savedColors.Insert(0, color);
            if (_savedColors.Count > 8) _savedColors.RemoveAt(8);
            RebuildSwatchRow(_savedSwatches, _savedColors);
        }

        private void RecordRecentColor(Color color)
        {
            _recentColors.RemoveAll(item => Approximately(item, color));
            _recentColors.Insert(0, color);
            if (_recentColors.Count > 8) _recentColors.RemoveAt(8);
            RebuildSwatchRow(_recentSwatches, _recentColors);
        }

        private static bool Approximately(Color left, Color right) =>
            Mathf.Abs(left.r - right.r) < 0.002f &&
            Mathf.Abs(left.g - right.g) < 0.002f &&
            Mathf.Abs(left.b - right.b) < 0.002f &&
            Mathf.Abs(left.a - right.a) < 0.002f;

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
            if (_artifactViewer != null)
                RecordRecentColor(_artifactViewer.PaintColor);
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
            Set(_sessionNameLabel, scanner.ActiveSessionName);
            string state = scanner.ScanLifecycle switch
            {
                ScanLifecycleState.Starting => "Starting…",
                ScanLifecycleState.Running => "Active",
                ScanLifecycleState.Quiescing => "Quiescing…",
                _ when !string.IsNullOrEmpty(scanner.LastScanStartError) =>
                    "Failed: " + scanner.LastScanStartError,
                _ => "Stopped"
            };
            StatusKind scanKind = scanner.IsScanning ? StatusKind.Good :
                scanner.IsScanStarting ? StatusKind.Warning : StatusKind.Neutral;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!MerkabaNativeVulkanExecutor.IsAvailable)
            {
                // The executor owns the cached device status. Do not invent
                // a UI timeout or hide a failed pipeline behind "Starting".
                state = MerkabaNativeVulkanExecutor.StartupSummary;
                scanKind = MerkabaNativeVulkanExecutor.StartupFailed
                    ? StatusKind.Error : StatusKind.Warning;
            }
#endif
            SetStatus(_scanning, state, scanKind);
            RefreshAnchorStatus(scanner);
            Set(_chunks, scanner.ActiveChunkCount.ToString());
            // These counts need a completed current-view GPU counter producer;
            // legacy Query/Build counters are not Flower metrics.
            Set(_kernels, "Unavailable");
            Set(_visibleBoundary, "Unavailable");
            string saved = scanner.ActiveSessionId == Guid.Empty
                ? "No session"
                : !scanner.SavedSessionExists ? "Live scan"
                : scanner.SessionIsDirty ? "Live changes" : "Saved";
            SetStatus(_saved, saved, scanner.SessionIsDirty || !scanner.SavedSessionExists
                ? StatusKind.Neutral : scanner.ActiveSessionId != Guid.Empty
                    ? StatusKind.Good : StatusKind.Neutral);
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
            Set(_exportSaveAsStatus, _exportFileSaveStatus);
            Set(_lastExport, string.IsNullOrWhiteSpace(scanner.LastExportPath)
                ? "No export in this application session"
                : Path.GetFileName(scanner.LastExportPath));
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
            Set(_libraryModelStatus, _artifactViewer?.Status ??
                "Model viewer unavailable");

            float opacity = scanner.ScanOpacity;
            if (_artifactViewer != null)
                _artifactViewer.PreviewOpacity = opacity;
            _opacity?.SetValueWithoutNotify(opacity);
            Set(_opacityValue, $"{opacity * 100f:F0}%");
            _fineRadius?.SetValueWithoutNotify(scanner.FineBrushRadius);
            _fineLength?.SetValueWithoutNotify(scanner.FineToolLength);
            Set(_fineRadiusValue, $"{scanner.FineBrushRadius * 100f:F0} cm");
            Set(_fineLengthValue, $"{scanner.FineToolLength:F2} m");
            if (_fine != null)
                _fine.text = scanner.FineMode
                    ? "HAND TOOL ENABLED" : "ENABLE HAND TOOL";
            RefreshFineTool();
            if (_readout != null)
                _readout.text = scanner.ReadoutDrawEnabled
                    ? "Readout On" : "Readout Off";
            if (_occlusion != null)
                _occlusion.text = scanner.DynamicOcclusionEnabled
                    ? "Occlusion On" : "Occlusion Off";
            _checker?.EnableInClassList("checker-toggle--on",
                scanner.CheckerReadoutEnabled);
            if (_checker != null)
                _checker.text = scanner.CheckerReadoutEnabled
                    ? "Checker On" : "Checker Off";
            if (_artifactView != null)
                _artifactView.text = _artifactViewer != null &&
                    _artifactViewer.IsOpen ? "CLOSE MODEL" : "OPEN LAST EXPORT";
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
            if (_planView != null) _planView.text = "MODEL";
            if (_planStyle != null)
            {
                bool plan = _artifactViewer?.PlanViewEnabled ?? false;
                _planStyle.EnableInClassList("segment--selected", plan);
                _planView?.EnableInClassList("segment--selected", !plan);
            }
            RefreshPaintControls();
            RefreshObjectControls();

            ScanOperationState operation = scanner.CurrentOperation;
            RefreshOperation(operation);
            if (_start != null) _start.text = scanner.IsScanStarting
                ? "STARTING…" : scanner.IsScanning ? "STOP SCAN" : "START SCAN";
            _start?.EnableInClassList("primary-action--stop", scanner.IsScanning);
            _artifactView?.EnableInClassList("primary-action--stop",
                _artifactViewer?.IsOpen ?? false);
            _paintView?.EnableInClassList("primary-action--stop",
                _artifactViewer?.IsOpen ?? false);
            if (_save != null) _save.text = operation.Busy &&
                operation.Kind == ScanOperationKind.Save ? "SAVING…" : "SAVE";
            if (_load != null) _load.text = operation.Busy &&
                operation.Kind == ScanOperationKind.Load ? "LOADING…" : "LOAD";
            if (_export != null) _export.text = operation.Busy &&
                operation.Kind == ScanOperationKind.ExportGlb
                ? "EXPORTING…" : _exportAsTiles ? "EXPORT 3D TILES" : "EXPORT GLB";

            bool reviewing = _artifactViewer?.IsOpen ?? false;
            bool operationBusy = scanner.IsBusy || _exportFileSavePending;
            bool busy = operationBusy || reviewing;
            bool sessionDesign = _artifactViewer?.HasSessionDesign ?? false;
            _start?.SetEnabled(!busy && !scanner.IsScanStarting);
            // RoomScanner's existing save/close-before-switch handlers own
            // these transitions even while a session design is being viewed.
            _save?.SetEnabled(!operationBusy && scanner.ActiveSessionId != Guid.Empty);
            _saveAs?.SetEnabled(!operationBusy && scanner.ActiveSessionId != Guid.Empty);
            _load?.SetEnabled(!operationBusy && _selectedSessionIndex >= 0);
            _new?.SetEnabled(!operationBusy);
            _rename?.SetEnabled(!operationBusy && scanner.ActiveSessionId != Guid.Empty);
            _deleteSession?.SetEnabled(!operationBusy && _selectedSessionIndex >= 0);
            _sessionPicker?.SetEnabled(!operationBusy);
            _sessionName?.SetEnabled(!operationBusy);
            _export?.SetEnabled(!busy);
            _exportFormat?.SetEnabled(!busy);
            _exportName?.SetEnabled(!busy);
            _exportSaveAs?.SetEnabled(!busy &&
                !string.IsNullOrEmpty(scanner.LastExportPath));
            _fine?.SetEnabled(!busy);
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
            _artifactRoomAlign?.SetEnabled(!operationBusy && reviewing &&
                (_artifactViewer?.CanAlignToRoom ?? false));
            _paintView?.SetEnabled(!operationBusy && _artifactViewer != null);
            _paintLoad?.SetEnabled(!operationBusy && _artifactViewer != null);
            _paintSave?.SetEnabled(!operationBusy && reviewing);
            _paintBrush?.SetEnabled(!operationBusy && sessionDesign);
            _paintLine?.SetEnabled(!operationBusy && sessionDesign);
            _paintSurface?.SetEnabled(!operationBusy && sessionDesign);
            _paintSpatial?.SetEnabled(!operationBusy && sessionDesign);
            _paintSpray?.SetEnabled(!operationBusy && sessionDesign);
            _paintErase?.SetEnabled(!operationBusy && sessionDesign);
            _paintEyedropper?.SetEnabled(!operationBusy && sessionDesign);
            _paintShapeRound?.SetEnabled(!operationBusy && sessionDesign);
            _paintShapeSquare?.SetEnabled(!operationBusy && sessionDesign);
            _designPaint?.SetEnabled(!operationBusy);
            _designObjects?.SetEnabled(!operationBusy);
            bool objectMode = sessionDesign &&
                _designSubmode == DesignSubmode.Objects;
            bool hasAssets = (_artifactViewer?.DesignAssets.Count ?? 0) > 0;
            bool hasObject = (_artifactViewer?.SelectedDesignInstanceId ?? 0)
                != 0;
            _objectImport?.SetEnabled(!operationBusy && sessionDesign);
            _libraryUseObject?.SetEnabled(!operationBusy && sessionDesign &&
                !string.IsNullOrEmpty(_artifactViewer?.SelectedDesignAssetId));
            _objectPlace?.SetEnabled(!operationBusy && objectMode && hasAssets);
            _objectSelect?.SetEnabled(!operationBusy && objectMode);
            _objectDuplicate?.SetEnabled(!operationBusy && objectMode &&
                hasObject);
            _objectVisible?.SetEnabled(!operationBusy && objectMode &&
                hasObject);
            _objectLock?.SetEnabled(!operationBusy && objectMode && hasObject);
            _objectDelete?.SetEnabled(!operationBusy && objectMode && hasObject);
            _objectAssetPicker?.SetEnabled(!operationBusy && sessionDesign &&
                hasAssets);
            _objectInstancePicker?.SetEnabled(!operationBusy && objectMode &&
                (_artifactViewer?.DesignInstances.Count ?? 0) > 0);
            _objectSurfaceSnap?.SetEnabled(!operationBusy && objectMode);
            _objectUprightSnap?.SetEnabled(!operationBusy && objectMode);
            _objectGridSnap?.SetEnabled(!operationBusy && objectMode);
            bool designMode = sessionDesign && DesignInteractionActive;
            _designUndo?.SetEnabled(!operationBusy && designMode &&
                (_artifactViewer?.CanUndoDesign ?? false));
            _designRedo?.SetEnabled(!operationBusy && designMode &&
                (_artifactViewer?.CanRedoDesign ?? false));
            _saveSwatch?.SetEnabled(!operationBusy && reviewing);
            _planView?.SetEnabled(!operationBusy && _artifactViewer != null);
            _planStyle?.SetEnabled(!operationBusy && reviewing);
        }

        private void RefreshAnchorStatus(RoomScanner scanner)
        {
            RoomAnchorManager manager = RoomAnchorManager.Instance;
            if (scanner.ActiveAnchorUuid == Guid.Empty)
            {
                SetStatus(_anchorStatus, "No active session", StatusKind.Neutral);
                return;
            }
            if (manager == null || !manager.HasSpatialAnchor ||
                manager.SpatialAnchorUuid != scanner.ActiveAnchorUuid)
            {
                SetStatus(_anchorStatus, "Awaiting session anchor", StatusKind.Warning);
                return;
            }
            Transform anchor = manager.SpatialAnchorTransform;
            bool localized = anchor != null &&
                anchor.TryGetComponent(out OVRSpatialAnchor spatial) &&
                spatial.Localized && spatial.IsTracked;
            bool bound = localized && RoomSpaceRoot.Instance != null &&
                RoomSpaceRoot.Instance.CurrentAnchor == anchor;
            SetStatus(_anchorStatus, bound ? "Localized and bound" :
                localized ? "Awaiting room binding" : "Not localized / tracked",
                bound ? StatusKind.Good : StatusKind.Warning);
        }

        private void RefreshPaintControls()
        {
            if (_artifactViewer == null) return;
            _artifactViewer.PaintInputEnabled =
                DesignInteractionActive &&
                _designSubmode == DesignSubmode.Paint;
            Color color = _artifactViewer.PaintColor;
            if (_paintWheelPointer < 0 && (!_hasLastRecentColor ||
                !Approximately(_lastRecentColor, color)))
            {
                _lastRecentColor = color;
                _hasLastRecentColor = true;
                RecordRecentColor(color);
            }
            if (_paintWheelPointer < 0)
            {
                Color.RGBToHSV(color, out _paintHue, out _paintSaturation,
                    out _);
            }
            Color.RGBToHSV(color, out _, out _, out float value);
            _paintValue?.SetValueWithoutNotify(value);
            _paintAlpha?.SetValueWithoutNotify(color.a);
            _paintWidth?.SetValueWithoutNotify(_artifactViewer.PaintWidth);
            _paintFlow?.SetValueWithoutNotify(_artifactViewer.PaintFlow);
            _paintHardness?.SetValueWithoutNotify(
                _artifactViewer.PaintHardness);
            _paintSaturationSlider?.SetValueWithoutNotify(
                _artifactViewer.PaintSaturation);
            _paintDensity?.SetValueWithoutNotify(
                _artifactViewer.SprayDensity);
            _paintScatter?.SetValueWithoutNotify(
                _artifactViewer.SprayScatter);
            Set(_paintWidthValue, $"{_artifactViewer.PaintWidth * 1000f:F0} mm");
            Set(_paintFlowValue, $"{_artifactViewer.PaintFlow * 100f:F0}%");
            Set(_paintHardnessValue,
                $"{_artifactViewer.PaintHardness * 100f:F0}%");
            Set(_paintSaturationValue,
                $"{_artifactViewer.PaintSaturation * 100f:F0}%");
            Set(_paintDensityValue,
                $"{_artifactViewer.SprayDensity:F0}/s");
            Set(_paintScatterValue,
                $"{_artifactViewer.SprayScatter * 100f:F0} cm");
            if (_paintColorSwatch != null)
                _paintColorSwatch.style.backgroundColor = color;
            UpdatePaintColorCursor();
            MerkabaArtifactPaintTool tool = _artifactViewer.PaintTool;
            Set(_paintToolName, tool switch
            {
                MerkabaArtifactPaintTool.SurfaceBrush => "Surface\nbrush",
                MerkabaArtifactPaintTool.SpatialBrush => "3D brush",
                MerkabaArtifactPaintTool.Eyedropper => "Pick color",
                MerkabaArtifactPaintTool.Erase => "Eraser",
                _ => tool.ToString()
            });
            _paintBrush?.EnableInClassList("tool-button--selected",
                tool == MerkabaArtifactPaintTool.Brush);
            _paintLine?.EnableInClassList("tool-button--selected",
                tool == MerkabaArtifactPaintTool.Line);
            _paintSurface?.EnableInClassList("tool-button--selected",
                tool == MerkabaArtifactPaintTool.SurfaceBrush);
            _paintSpatial?.EnableInClassList("tool-button--selected",
                tool == MerkabaArtifactPaintTool.SpatialBrush);
            _paintSpray?.EnableInClassList("tool-button--selected",
                tool == MerkabaArtifactPaintTool.Spray);
            _paintErase?.EnableInClassList("tool-button--selected",
                tool == MerkabaArtifactPaintTool.Erase);
            _paintEyedropper?.EnableInClassList("tool-button--selected",
                tool == MerkabaArtifactPaintTool.Eyedropper);
            _paintShapeRound?.EnableInClassList("segment--selected",
                _artifactViewer.PaintShape == MerkabaBrushShape.Round);
            _paintShapeSquare?.EnableInClassList("segment--selected",
                _artifactViewer.PaintShape == MerkabaBrushShape.Square);
            bool eraser = tool == MerkabaArtifactPaintTool.Erase;
            bool eyedropper = tool == MerkabaArtifactPaintTool.Eyedropper;
            bool painting = !eraser && !eyedropper;
            bool surfaceBrush = tool is MerkabaArtifactPaintTool.Brush or
                MerkabaArtifactPaintTool.SurfaceBrush;
            SetDisplayed(_paintColorCard, !eraser);
            SetDisplayed(_paintPalette, !eraser);
            SetDisplayed(_paintColorWheel, painting);
            SetDisplayed(_paintRowValue, painting);
            SetDisplayed(_paintRowAlpha, painting);
            SetDisplayed(_paintRowWidth, !eyedropper);
            // Line color also consumes flow and saturation in the existing
            // PaintEngine, but its tube does not consume hardness or shape.
            SetDisplayed(_paintRowFlow, painting);
            SetDisplayed(_paintRowSaturation, painting);
            SetDisplayed(_paintRowHardness, surfaceBrush);
            SetDisplayed(_paintRowShape, painting &&
                tool != MerkabaArtifactPaintTool.Line);
            _paintSpraySettings?.EnableInClassList("mode-panel--hidden",
                tool != MerkabaArtifactPaintTool.Spray);
            if (_paintView != null)
                _paintView.text = _artifactViewer.IsOpen
                    ? "CLOSE MODEL" : "OPEN LAST EXPORT";
        }

        private static void SetDisplayed(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible
                ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshObjectControls()
        {
            if (_artifactViewer == null) return;
            _artifactViewer.ObjectInputEnabled =
                DesignInteractionActive &&
                _designSubmode == DesignSubmode.Objects;
            IReadOnlyList<MerkabaDesignAsset> assets =
                _artifactViewer.DesignAssets;
            bool assetsChanged = _designAssets.Count != assets.Count;
            if (!assetsChanged)
                for (int index = 0; index < assets.Count; index++)
                    if (_designAssets[index].id != assets[index].id)
                    {
                        assetsChanged = true;
                        break;
                    }
            if (assetsChanged)
            {
                _designAssets.Clear();
                _designAssets.AddRange(assets);
                var choices = new List<string>(_designAssets.Count);
                foreach (MerkabaDesignAsset asset in _designAssets)
                    choices.Add(ObjectAssetChoice(asset));
                _objectAssetPicker.choices = choices;
            }
            MerkabaDesignAsset selectedAsset = _designAssets.Find(asset =>
                asset.id == _artifactViewer.SelectedDesignAssetId);
            _objectAssetPicker?.SetValueWithoutNotify(selectedAsset != null
                ? ObjectAssetChoice(selectedAsset) : string.Empty);
            Set(_selectedObjectAsset, selectedAsset != null
                ? ObjectAssetChoice(selectedAsset) : "Choose an asset in Library → Models");
            Set(_libraryModelCount, _artifactViewer.HasSessionDesign
                ? $"{assets.Count} library assets · {_artifactViewer.DesignInstances.Count} placed in this session"
                : "No matching session design library open");

            IReadOnlyList<MerkabaDesignInstance> instances =
                _artifactViewer.DesignInstances;
            bool instancesChanged = _designInstances.Count != instances.Count;
            if (!instancesChanged)
                for (int index = 0; index < instances.Count; index++)
                    if (_designInstances[index].instanceId !=
                        instances[index].instanceId)
                    {
                        instancesChanged = true;
                        break;
                    }
            if (instancesChanged)
            {
                _designInstances.Clear();
                _designInstances.AddRange(instances);
                var choices = new List<string>(_designInstances.Count);
                foreach (MerkabaDesignInstance instance in _designInstances)
                    choices.Add(ObjectInstanceChoice(instance));
                _objectInstancePicker.choices = choices;
            }
            MerkabaDesignInstance selected = _designInstances.Find(instance =>
                instance.instanceId ==
                _artifactViewer.SelectedDesignInstanceId);
            _objectInstancePicker?.SetValueWithoutNotify(selected != null
                ? ObjectInstanceChoice(selected) : string.Empty);
            _objectSurfaceSnap?.SetValueWithoutNotify(
                _artifactViewer.ObjectSurfaceSnap);
            _objectUprightSnap?.SetValueWithoutNotify(
                _artifactViewer.ObjectUprightSnap);
            _objectGridSnap?.SetValueWithoutNotify(
                _artifactViewer.ObjectGridSnap);
            if (_objectPlace != null)
                _objectPlace.text = _artifactViewer.ObjectPlacementEnabled
                    ? "PLACING…" : "Place";
            if (_objectVisible != null)
                _objectVisible.text = selected?.visible == false
                    ? "Show" : "Hide";
            if (_objectLock != null)
                _objectLock.text = selected?.locked == true
                    ? "Unlock" : "Lock";
            Set(_objectStatus, selected != null
                ? $"Object #{selected.instanceId} · " +
                  (selected.locked ? "Locked" : "Editable")
                : assets.Count > 0
                    ? "Select Place, then point and trigger"
                    : "Import a GLB design object");
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

            SetStatus(_operationStage, operation.StatusText,
                operation.Stage == ScanOperationStage.Failed ? StatusKind.Error :
                operation.Stage == ScanOperationStage.Complete ? StatusKind.Good :
                StatusKind.Neutral);
            bool indeterminate = operation.IsIndeterminate;
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
        private enum MenuTab { Scan, Refine, Design, View }
        private enum DesignSubmode { Paint, Objects }
        private enum ConsoleDestination { Tools, Library, Diagnostics }
        private enum LibraryTab { Scans, Exports, Models }
    }
}
